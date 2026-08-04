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

/// <summary>Structurally valid list query, already parsed from the query string by the API-layer
/// binding. Raw Type/Status strings are validated against their slug maps here.</summary>
public sealed record ListCatalogItemsApiQuery(
    string? Search,
    string? Type,
    Guid? CategoryId,
    string? Status,
    int? Limit,
    string? Cursor);

public sealed record CatalogItemListPage(
    IReadOnlyList<CatalogItemListRow> Items,
    int Limit,
    bool HasMore,
    string? NextCursor);

/// <summary>
/// API-facing orchestration for bounded catalog reads (Session 2e.3, build-log/113): list/search,
/// item detail, and category choices. Owns the same auth-stack composition as
/// <see cref="CatalogItemApiService"/> (ADR-462: account access → entitlement → user permission),
/// except gate 1 denies only <c>IsBlocked</c> — not <c>IsReadOnly</c> — matching every other
/// pure-read service in this codebase (e.g. <see cref="GetKeepRequestListService"/>); an
/// OffSeason/read-only account can still browse its price book.
/// </summary>
public sealed class CatalogReadApiService(
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

    private static readonly Error InvalidType =
        Error.Create("CatalogItemList.ValidationInvalidType", "Type must be one of Material, Equipment, Service, Fee.");

    private static readonly Error InvalidStatus =
        Error.Create("CatalogItemList.ValidationInvalidStatus", "Status must be Active or Inactive.");

    private static readonly Error InvalidLimit =
        Error.Create("CatalogItemList.ValidationInvalidLimit", "Limit must be between 1 and 100.");

    private static readonly Error InvalidCursor =
        Error.Create("CatalogItemList.ValidationInvalidCursor",
            "The cursor is invalid, malformed, or does not match the current query.");

    private static readonly Error NotFound =
        Error.Create("CatalogItem.NotFound", "Catalog item not found.");

    public async Task<Result<CatalogItemListPage>> ListItemsAsync(ListCatalogItemsApiQuery query, CancellationToken ct)
    {
        var gate = await AuthorizeAsync(ct);
        if (gate.IsFailure)
            return Result<CatalogItemListPage>.Failure(gate.Error);

        CatalogItemType? type = null;
        if (!string.IsNullOrWhiteSpace(query.Type))
        {
            if (!Enum.TryParse<CatalogItemType>(query.Type, ignoreCase: true, out var parsedType) || !Enum.IsDefined(parsedType))
                return Result<CatalogItemListPage>.Failure(InvalidType);
            type = parsedType;
        }

        var activeState = CatalogItemActiveState.Active;
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (!Enum.TryParse<CatalogItemActiveState>(query.Status, ignoreCase: true, out var parsedStatus) ||
                parsedStatus is not (CatalogItemActiveState.Active or CatalogItemActiveState.Inactive))
            {
                return Result<CatalogItemListPage>.Failure(InvalidStatus);
            }
            activeState = parsedStatus;
        }

        var limit = query.Limit ?? DefaultLimit;
        if (limit < 1 || limit > MaxLimit)
            return Result<CatalogItemListPage>.Failure(InvalidLimit);

        var filters = new CatalogItemListFilters(
            string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(),
            type,
            query.CategoryId,
            activeState);

        var fingerprint = CatalogItemListCursor.ComputeFingerprint(filters);
        CatalogItemListCursorPosition? cursorPosition = null;
        if (!string.IsNullOrEmpty(query.Cursor))
        {
            if (!CatalogItemListCursor.TryDecode(cursorProtector, query.Cursor, fingerprint, out cursorPosition))
                return Result<CatalogItemListPage>.Failure(InvalidCursor);
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

        return Result<CatalogItemListPage>.Success(new CatalogItemListPage(page, limit, hasMore, nextCursor));
    }

    public async Task<Result<CatalogItemDetail>> GetItemDetailAsync(Guid catalogItemId, CancellationToken ct)
    {
        var gate = await AuthorizeAsync(ct);
        if (gate.IsFailure)
            return Result<CatalogItemDetail>.Failure(gate.Error);

        var detail = await persistence.GetItemDetailAsync(currentUser.AccountId, catalogItemId, ct);
        return detail is null
            ? Result<CatalogItemDetail>.Failure(NotFound)
            : Result<CatalogItemDetail>.Success(detail);
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

        // Gate 1 — account access. A read: only Blocked denies, matching every other pure-read
        // service (GetKeepRequestListService, GetKeepRequestDetailService, ...) — unlike
        // CatalogItemApiService's mutation gate, which also denies ReadOnly/OffSeason.
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

        // Gate 2 — account-aware feature resolver (entitlement-only: plan or active enrollment).
        var featureContext = new AccountFeatureAccessContext(accountSnapshot.Plan);
        var enabled = await featureAccessResolver.IsEnabledAsync(
            currentUser.AccountId, featureContext, CapabilityPackageFeatureKeys.PriceBookQuotesMaterials, ct);
        if (!enabled)
            return Result.Failure(Forbidden);

        // Gate 3 — user permission.
        var roleSnapshot = await snapshotPersistence.GetAccountUserRoleSnapshotAsync(
            currentUser.AccountId, currentUser.UserId, ct);
        if (roleSnapshot is null)
            return Result.Failure(Forbidden);

        if (!userAccessPolicy.IsPermitted(
                roleSnapshot.Role,
                roleSnapshot.MembershipStatus,
                accountSnapshot.Purpose,
                PermissionKeys.Keep.PriceBookCatalogManage))
            return Result.Failure(Forbidden);

        return Result.Success();
    }
}
