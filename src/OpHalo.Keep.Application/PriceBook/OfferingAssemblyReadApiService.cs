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

/// <summary>Structurally valid list query, already parsed from the query string by the API-layer
/// binding.</summary>
public sealed record ListOfferingAssembliesApiQuery(string? Status, int? Limit, string? Cursor);

public sealed record OfferingAssemblyListPage(
    IReadOnlyList<OfferingAssemblyListRow> Items, int Limit, bool HasMore, string? NextCursor);

/// <summary>
/// API-facing orchestration for bounded offering/assembly reads (Session 3.2a.2): list and
/// detail. Owns a similar auth-stack composition to <see cref="OfferingAssemblyApiService"/>
/// (ADR-462: account access → entitlement → user permission), except gate 1 denies only
/// <c>IsBlocked</c> — not <c>IsReadOnly</c> — matching every other pure-read service (e.g.
/// <see cref="CatalogReadApiService"/>); an OffSeason/read-only account can still browse its
/// offerings.
/// </summary>
public sealed class OfferingAssemblyReadApiService(
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

    private static readonly Error InvalidStatus =
        Error.Create("OfferingAssemblyList.ValidationInvalidStatus", "Status must be Active, Inactive, or All.");

    private static readonly Error InvalidLimit =
        Error.Create("OfferingAssemblyList.ValidationInvalidLimit", "Limit must be between 1 and 100.");

    private static readonly Error InvalidCursor =
        Error.Create("OfferingAssemblyList.ValidationInvalidCursor",
            "The cursor is invalid, malformed, or does not match the current query.");

    public async Task<Result<OfferingAssemblyListPage>> ListAsync(ListOfferingAssembliesApiQuery query, CancellationToken ct)
    {
        var gate = await AuthorizeAsync(ct);
        if (gate.IsFailure)
            return Result<OfferingAssemblyListPage>.Failure(gate.Error);

        CatalogActiveState? activeState = null;
        if (!string.IsNullOrWhiteSpace(query.Status) && !string.Equals(query.Status, "All", StringComparison.OrdinalIgnoreCase))
        {
            if (!Enum.TryParse<CatalogActiveState>(query.Status, ignoreCase: true, out var parsedStatus) || !Enum.IsDefined(parsedStatus))
                return Result<OfferingAssemblyListPage>.Failure(InvalidStatus);
            activeState = parsedStatus;
        }

        var limit = query.Limit ?? DefaultLimit;
        if (limit < 1 || limit > MaxLimit)
            return Result<OfferingAssemblyListPage>.Failure(InvalidLimit);

        var filters = new OfferingAssemblyListFilters(activeState);

        var fingerprint = OfferingAssemblyListCursor.ComputeFingerprint(filters);
        OfferingAssemblyListCursorPosition? cursorPosition = null;
        if (!string.IsNullOrEmpty(query.Cursor))
        {
            if (!OfferingAssemblyListCursor.TryDecode(cursorProtector, query.Cursor, fingerprint, out cursorPosition))
                return Result<OfferingAssemblyListPage>.Failure(InvalidCursor);
        }

        var rows = await persistence.ListAsync(currentUser.AccountId, filters, cursorPosition, limit + 1, ct);

        var hasMore = rows.Count > limit;
        var page = hasMore ? rows.Take(limit).ToList() : rows.ToList();

        string? nextCursor = null;
        if (hasMore && page.Count > 0)
        {
            var last = page[^1];
            nextCursor = OfferingAssemblyListCursor.Encode(
                cursorProtector, fingerprint, new OfferingAssemblyListCursorPosition(last.Name, last.Id));
        }

        return Result<OfferingAssemblyListPage>.Success(new OfferingAssemblyListPage(page, limit, hasMore, nextCursor));
    }

    public async Task<Result<OfferingAssemblyDetail>> GetDetailAsync(Guid offeringAssemblyId, CancellationToken ct)
    {
        var gate = await AuthorizeAsync(ct);
        if (gate.IsFailure)
            return Result<OfferingAssemblyDetail>.Failure(gate.Error);

        var detail = await persistence.GetDetailAsync(currentUser.AccountId, offeringAssemblyId, ct);
        return detail is null
            ? Result<OfferingAssemblyDetail>.Failure(OfferingAssemblyErrors.NotFound)
            : Result<OfferingAssemblyDetail>.Success(detail);
    }

    /// <summary>Session 3.2d: active assemblies referencing a catalog item (primary or associated),
    /// for the catalog-item pre-inactivation confirmation. Same Owner/Admin gate as every other
    /// read on this service — reused as-is, no new gate logic.</summary>
    public async Task<Result<IReadOnlyList<OfferingAssemblyDependencyRow>>> GetActiveAssemblyDependenciesAsync(
        Guid catalogItemId, CancellationToken ct)
    {
        var gate = await AuthorizeAsync(ct);
        if (gate.IsFailure)
            return Result<IReadOnlyList<OfferingAssemblyDependencyRow>>.Failure(gate.Error);

        var rows = await persistence.ListActiveAssembliesReferencingCatalogItemAsync(currentUser.AccountId, catalogItemId, ct);
        return Result<IReadOnlyList<OfferingAssemblyDependencyRow>>.Success(rows);
    }

    private async Task<Result> AuthorizeAsync(CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result.Failure(Unauthorized);

        // Gate 1 — account access. A read: only Blocked denies, matching every other pure-read
        // service — unlike OfferingAssemblyApiService's mutation gate, which also denies
        // ReadOnly/OffSeason.
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
