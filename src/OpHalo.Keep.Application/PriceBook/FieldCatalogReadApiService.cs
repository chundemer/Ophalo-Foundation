using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Keep.Application.Requests;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.PriceBook;

/// <summary>Structurally valid list query for the field-safe catalog surface. No Type/Status —
/// field browsing is always Common Items, Active only (build-log/118, 3.4b).</summary>
public sealed record ListFieldCatalogItemsApiQuery(
    string? Search,
    Guid? CategoryId,
    int? Limit,
    string? Cursor);

public sealed record FieldCatalogItemRow(
    CatalogItem Item,
    CatalogItemMatchRank MatchRank,
    CatalogItemMatchReason? MatchReason);

public sealed record FieldCatalogItemListPage(
    IReadOnlyList<FieldCatalogItemRow> Items,
    int Limit,
    bool HasMore,
    string? NextCursor);

public sealed record FieldCatalogItemDetail(CatalogItem Item, CatalogCategory? Category);

/// <summary>
/// API-facing orchestration for the technician-reachable, price-free catalog read surface (Session
/// 3.4b, build-log/118): list/search, item detail, and category choices, scoped to
/// <see cref="CatalogItem.IsCommonItem"/> = true and Active only. Sits beside
/// <see cref="CatalogReadApiService"/> rather than reusing it — gate 3 is
/// <c>RequestsOperate</c> AND <c>ScopeCapture</c> (ADR-480), not <c>PriceBookCatalogManage</c>, and
/// every returned row/detail type deliberately omits <c>PriceBookVersionLine</c> so a price leak is
/// a compile error, not a runtime discipline. Gate 1/2 composition (Blocked-only account access,
/// then Price Book entitlement) matches <see cref="CatalogReadApiService"/> exactly — catalog data
/// is account-wide, not request-scoped, so there is no row-visibility step here (unlike
/// <see cref="ProposedScopeReadApiService"/>).
/// </summary>
public sealed class FieldCatalogReadApiService(
    ICatalogReadPersistence persistence,
    IAccountAccessSnapshotPersistence snapshotPersistence,
    ICurrentUser currentUser,
    IAccountAccessPolicy accountAccessPolicy,
    IAccountFeatureAccessResolver featureAccessResolver,
    IUserAccessPolicy userAccessPolicy,
    IClock clock,
    IKeepRequestListCursorProtector cursorProtector)
{
    private const int DefaultLimit = 50;
    private const int MaxLimit = 100;

    private static readonly Error Unauthorized =
        Error.Create("auth.unauthorized", "Authentication required.");

    private static readonly Error Forbidden =
        Error.Create("auth.forbidden", "You do not have permission to perform this action.");

    private static readonly Error InvalidLimit =
        Error.Create("FieldCatalogItemList.ValidationInvalidLimit", "Limit must be between 1 and 100.");

    private static readonly Error InvalidCursor =
        Error.Create("FieldCatalogItemList.ValidationInvalidCursor",
            "The cursor is invalid, malformed, or does not match the current query.");

    private static readonly Error NotFound =
        Error.Create("CatalogItem.NotFound", "Catalog item not found.");

    public async Task<Result<FieldCatalogItemListPage>> ListItemsAsync(ListFieldCatalogItemsApiQuery query, CancellationToken ct)
    {
        var gate = await AuthorizeAsync(ct);
        if (gate.IsFailure)
            return Result<FieldCatalogItemListPage>.Failure(gate.Error);

        var limit = query.Limit ?? DefaultLimit;
        if (limit < 1 || limit > MaxLimit)
            return Result<FieldCatalogItemListPage>.Failure(InvalidLimit);

        var filters = new CatalogItemListFilters(
            string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(),
            Type: null,
            query.CategoryId,
            CatalogItemActiveState.Active,
            IsCommonItem: true);

        var fingerprint = CatalogItemListCursor.ComputeFingerprint(filters);
        CatalogItemListCursorPosition? cursorPosition = null;
        if (!string.IsNullOrEmpty(query.Cursor))
        {
            if (!CatalogItemListCursor.TryDecode(cursorProtector, query.Cursor, fingerprint, out cursorPosition))
                return Result<FieldCatalogItemListPage>.Failure(InvalidCursor);
        }

        var rows = await persistence.SearchItemsAsync(currentUser.AccountId, filters, cursorPosition, limit + 1, ct);

        var hasMore = rows.Count > limit;
        var page = hasMore ? rows.Take(limit).ToList() : rows.ToList();

        string? nextCursor = null;
        if (hasMore && page.Count > 0)
        {
            var last = page[^1];
            nextCursor = CatalogItemListCursor.Encode(
                cursorProtector, fingerprint,
                new CatalogItemListCursorPosition(last.MatchRank, last.Item.DisplayName, last.Item.Id));
        }

        var fieldRows = page.Select(row => new FieldCatalogItemRow(row.Item, row.MatchRank, row.MatchReason)).ToList();
        return Result<FieldCatalogItemListPage>.Success(new FieldCatalogItemListPage(fieldRows, limit, hasMore, nextCursor));
    }

    public async Task<Result<FieldCatalogItemDetail>> GetItemDetailAsync(Guid catalogItemId, CancellationToken ct)
    {
        var gate = await AuthorizeAsync(ct);
        if (gate.IsFailure)
            return Result<FieldCatalogItemDetail>.Failure(gate.Error);

        var detail = await persistence.GetItemDetailAsync(currentUser.AccountId, catalogItemId, ct);
        if (detail is null || !detail.Item.IsCommonItem || detail.Item.ActiveState != CatalogItemActiveState.Active)
            return Result<FieldCatalogItemDetail>.Failure(NotFound);

        return Result<FieldCatalogItemDetail>.Success(new FieldCatalogItemDetail(detail.Item, detail.Category));
    }

    public async Task<Result<IReadOnlyList<CatalogCategory>>> GetCategoryChoicesAsync(CancellationToken ct)
    {
        var gate = await AuthorizeAsync(ct);
        if (gate.IsFailure)
            return Result<IReadOnlyList<CatalogCategory>>.Failure(gate.Error);

        var categories = await persistence.GetActiveCategoriesAsync(currentUser.AccountId, ct);
        return Result<IReadOnlyList<CatalogCategory>>.Success(categories);
    }

    private async Task<Result> AuthorizeAsync(CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result.Failure(Unauthorized);

        // Gate 1 — account access. A read: only Blocked denies, matching CatalogReadApiService and
        // ProposedScopeReadApiService's read gate.
        var accountSnapshot = await snapshotPersistence.GetAccountAccessSnapshotAsync(currentUser.AccountId, ct);
        if (accountSnapshot is null)
            return Result.Failure(Forbidden);

        var accessContext = new AccountAccessContext(
            accountSnapshot.LifecycleState,
            accountSnapshot.Purpose,
            accountSnapshot.CommercialState,
            accountSnapshot.TrialEndsAtUtc,
            accountSnapshot.PastDueGraceEndsAtUtc,
            accountSnapshot.OperatingMode,
            RequestImplementsAllowedInOffSeason: false,
            clock.UtcNow);

        var decision = accountAccessPolicy.Evaluate(accessContext);
        if (decision.IsBlocked)
            return Result.Failure(Forbidden);

        // Gate 2 — Price Book entitlement (ADR-462): plan or active capability-package enrollment.
        var featureContext = new AccountFeatureAccessContext(accountSnapshot.Plan);
        var enabled = await featureAccessResolver.IsEnabledAsync(
            currentUser.AccountId, featureContext, CapabilityPackageFeatureKeys.PriceBookQuotesMaterials, ct);
        if (!enabled)
            return Result.Failure(Forbidden);

        // Gate 3 — RequestsOperate (B2 operator gate) AND ScopeCapture (ADR-480), not
        // PriceBookCatalogManage — this surface sits beside the Admin-only catalog workspace.
        var roleSnapshot = await snapshotPersistence.GetAccountUserRoleSnapshotAsync(
            currentUser.AccountId, currentUser.UserId, ct);
        if (roleSnapshot is null)
            return Result.Failure(Forbidden);

        if (!userAccessPolicy.IsPermitted(
                roleSnapshot.Role, roleSnapshot.MembershipStatus, accountSnapshot.Purpose,
                PermissionKeys.Keep.RequestsOperate) ||
            !userAccessPolicy.IsPermitted(
                roleSnapshot.Role, roleSnapshot.MembershipStatus, accountSnapshot.Purpose,
                PermissionKeys.Keep.ScopeCapture))
        {
            return Result.Failure(Forbidden);
        }

        return Result.Success();
    }
}
