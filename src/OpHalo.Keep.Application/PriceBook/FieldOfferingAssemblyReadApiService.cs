using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Keep.Application.Requests;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.PriceBook;

/// <summary>Structurally valid list query for the field-safe assembly surface. No Status param —
/// field browsing is always Active, further filtered to operationally-eligible (ADR-479).</summary>
public sealed record ListFieldOfferingAssembliesApiQuery(int? Limit, string? Cursor);

public sealed record FieldOfferingAssemblyListRow(
    Guid Id,
    string Name,
    Guid PrimaryCatalogItemId,
    string PrimaryCatalogItemDisplayName);

public sealed record FieldOfferingAssemblyListPage(
    IReadOnlyList<FieldOfferingAssemblyListRow> Items, int Limit, bool HasMore, string? NextCursor);

public sealed record FieldOfferingAssemblyDetailItem(
    Guid Id,
    Guid CatalogItemId,
    string CatalogItemDisplayName,
    decimal DefaultQuantity,
    bool IsOptional,
    int DisplayOrder);

public sealed record FieldOfferingAssemblyDetail(
    Guid Id,
    string Name,
    Guid PrimaryCatalogItemId,
    string PrimaryCatalogItemDisplayName,
    IReadOnlyList<FieldOfferingAssemblyDetailItem> Items);

/// <summary>
/// API-facing orchestration for the technician-reachable, price-free assembly read surface
/// (Session 3.4c, build-log/118): list and detail, scoped to <c>ActiveState.Active</c> and
/// <see cref="OfferingAssemblyListRow.IsOperationallyEligible"/> (ADR-479). Sits beside
/// <see cref="OfferingAssemblyReadApiService"/> rather than reusing it — gate 3 is
/// <c>RequestsOperate</c> AND <c>ScopeCapture</c> (ADR-480), not <c>PriceBookCatalogManage</c>, and
/// every returned row/detail type omits <c>PriceTreatment</c> and the whole
/// <c>OfferingAssemblyPricingSummary</c> so a price leak is a compile error, not a runtime
/// discipline.
///
/// <para>
/// Pagination correctness (build-log/118 has already flagged this exact bug class twice):
/// <see cref="OfferingAssemblyListRow.IsOperationallyEligible"/> is computed in-memory after the
/// SQL page fetch (it joins catalog-item price lookups per row), so it cannot be pushed into the
/// persistence layer's <c>Take(fetchCount)</c> predicate. <see cref="ListAsync"/> therefore
/// reuses <see cref="IOfferingAssemblyPersistence.ListAsync"/> completely unmodified — same raw
/// <c>limit+1</c> Active-only fetch as the Admin service — and computes <c>HasMore</c>/
/// <c>NextCursor</c> from that raw page (the row at <c>limit - 1</c> when the raw fetch overflows,
/// last raw row otherwise) <em>before</em> filtering to eligible rows. Only the returned
/// <c>Items</c> list is filtered. This can produce a sparse or empty page with <c>HasMore: true</c>
/// (all rows in this raw window were ineligible) rather than ever skipping an eligible row further
/// down the raw sequence — callers already tolerate variable page sizes via the cursor-walk loop.
/// </para>
///
/// <para>
/// Cursor fingerprint: an Admin <c>?status=Active</c> cursor and this list's cursor would
/// otherwise carry an identical <see cref="OfferingAssemblyListCursor.ComputeFingerprint"/> value
/// (both query the same raw filter shape), even though the two endpoints return different result
/// sets once eligibility filtering is applied. <see cref="ListAsync"/> passes
/// <c>fieldOperationallyEligible: true</c> so the two surfaces' cursors are never interchangeable.
/// </para>
/// </summary>
public sealed class FieldOfferingAssemblyReadApiService(
    IOfferingAssemblyPersistence persistence,
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
        Error.Create("FieldOfferingAssemblyList.ValidationInvalidLimit", "Limit must be between 1 and 100.");

    private static readonly Error InvalidCursor =
        Error.Create("FieldOfferingAssemblyList.ValidationInvalidCursor",
            "The cursor is invalid, malformed, or does not match the current query.");

    public async Task<Result<FieldOfferingAssemblyListPage>> ListAsync(ListFieldOfferingAssembliesApiQuery query, CancellationToken ct)
    {
        var gate = await AuthorizeAsync(ct);
        if (gate.IsFailure)
            return Result<FieldOfferingAssemblyListPage>.Failure(gate.Error);

        var limit = query.Limit ?? DefaultLimit;
        if (limit < 1 || limit > MaxLimit)
            return Result<FieldOfferingAssemblyListPage>.Failure(InvalidLimit);

        var filters = new OfferingAssemblyListFilters(CatalogActiveState.Active);

        var fingerprint = OfferingAssemblyListCursor.ComputeFingerprint(filters, fieldOperationallyEligible: true);
        OfferingAssemblyListCursorPosition? cursorPosition = null;
        if (!string.IsNullOrEmpty(query.Cursor))
        {
            if (!OfferingAssemblyListCursor.TryDecode(cursorProtector, query.Cursor, fingerprint, out cursorPosition))
                return Result<FieldOfferingAssemblyListPage>.Failure(InvalidCursor);
        }

        var rawRows = await persistence.ListAsync(currentUser.AccountId, filters, cursorPosition, limit + 1, ct);

        // HasMore/NextCursor come from the raw fetch, before eligibility filtering — see the
        // class-level pagination-correctness note.
        var hasMore = rawRows.Count > limit;
        var rawPage = hasMore ? rawRows.Take(limit).ToList() : rawRows.ToList();

        string? nextCursor = null;
        if (hasMore && rawPage.Count > 0)
        {
            var last = rawPage[^1];
            nextCursor = OfferingAssemblyListCursor.Encode(
                cursorProtector, fingerprint, new OfferingAssemblyListCursorPosition(last.Name, last.Id));
        }

        var eligibleItems = rawPage
            .Where(r => r.IsOperationallyEligible)
            .Select(r => new FieldOfferingAssemblyListRow(r.Id, r.Name, r.PrimaryCatalogItemId, r.PrimaryCatalogItemDisplayName))
            .ToList();

        return Result<FieldOfferingAssemblyListPage>.Success(
            new FieldOfferingAssemblyListPage(eligibleItems, limit, hasMore, nextCursor));
    }

    public async Task<Result<FieldOfferingAssemblyDetail>> GetDetailAsync(Guid offeringAssemblyId, CancellationToken ct)
    {
        var gate = await AuthorizeAsync(ct);
        if (gate.IsFailure)
            return Result<FieldOfferingAssemblyDetail>.Failure(gate.Error);

        var detail = await persistence.GetDetailAsync(currentUser.AccountId, offeringAssemblyId, ct);
        if (detail is null || detail.ActiveState != CatalogActiveState.Active || !detail.Eligibility.IsEligible)
            return Result<FieldOfferingAssemblyDetail>.Failure(OfferingAssemblyErrors.NotFound);

        return Result<FieldOfferingAssemblyDetail>.Success(new FieldOfferingAssemblyDetail(
            detail.Id,
            detail.Name,
            detail.PrimaryCatalogItemId,
            detail.PrimaryCatalogItemDisplayName,
            detail.Items
                .Select(i => new FieldOfferingAssemblyDetailItem(
                    i.Id, i.CatalogItemId, i.CatalogItemDisplayName, i.DefaultQuantity, i.IsOptional, i.DisplayOrder))
                .ToList()));
    }

    private async Task<Result> AuthorizeAsync(CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result.Failure(Unauthorized);

        // Gate 1 — account access. A read: only Blocked denies, matching CatalogReadApiService,
        // FieldCatalogReadApiService, and OfferingAssemblyReadApiService.
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
        // PriceBookCatalogManage — this surface sits beside the Admin-only assembly workspace.
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
