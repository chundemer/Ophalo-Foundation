using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Keep.Application.Requests;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.PriceBook;

public sealed record FieldScopeSearchApiQuery(string? Search, int? Limit, string? Cursor);

public enum FieldScopeSearchResultKind
{
    OfferingAssembly,
    CatalogItem,
}

/// <summary>One merged search result row (build-log/121). Assembly-only and catalog-item-only
/// fields are null for the other kind — a single typed sequence, not two browser-merged pages.</summary>
public sealed record FieldScopeSearchResultRow(
    FieldScopeSearchResultKind Kind,
    Guid Id,
    string DisplayName,
    int? DefaultItemCount,
    string? CatalogItemType,
    string? ExternalKey);

public sealed record FieldScopeSearchPage(
    IReadOnlyList<FieldScopeSearchResultRow> Items,
    int Limit,
    bool HasMore,
    string? NextCursor);

/// <summary>
/// Polymorphic field-scope search (build-log/121, ADR-486): replaces the composer's Common-Item-only
/// text search with one price-free search across every <c>Active</c> catalog item and every
/// <c>Active</c>, operationally-eligible (ADR-479) assembly. Sits beside
/// <see cref="FieldCatalogReadApiService"/>/<see cref="FieldOfferingAssemblyReadApiService"/> rather
/// than reusing either — this is the only surface that merges both streams into one globally
/// rank-ordered, cursor-paginated sequence.
///
/// <para>
/// The two streams are asymmetric: catalog-item matching and Active-filtering both happen in SQL
/// (<see cref="ICatalogReadPersistence.SearchItemsAsync"/> already returns a complete, correctly-
/// ranked page), but assembly operational eligibility can only be computed in memory after the raw
/// name-match fetch (same constraint as <see cref="IOfferingAssemblyPersistence.ListAsync"/>). A
/// single bounded assembly fetch could return a page that is sparse or empty purely because that
/// raw window happened to be ineligible-heavy — for the plain assembly list that is fine, because
/// its caller cursor-walks automatically. This composer's search box does not: it fires one
/// debounced request with no cursor-walk/Load-more UI, so a sparse first page would present to a
/// technician as a false "no matches" state — exactly the failure ADR-486 exists to correct.
/// <see cref="SearchAsync"/> therefore keeps pulling further raw assembly chunks — no arbitrary scan
/// cap, since any cap could hide a real eligible match — until either the eligible buffer can fill
/// the requested page or the raw name-match stream itself is exhausted.
/// </para>
///
/// <para>
/// The merged cursor tracks each stream's own resume position independently
/// (<see cref="FieldScopeSearchCursor"/>). The assembly position always advances only to the last
/// row actually placed on a returned page (not the last row scanned) — a resume point strictly
/// behind any scanned-but-not-yet-returned eligible row would re-scan a few already-checked
/// ineligible rows next page (cheap), while a resume point ahead of an unreturned eligible row would
/// skip it forever (a real correctness bug). See per-field comments below.
/// </para>
/// </summary>
public sealed class FieldScopeSearchApiService(
    ICatalogReadPersistence catalogPersistence,
    IOfferingAssemblyPersistence assemblyPersistence,
    IAccountAccessSnapshotPersistence snapshotPersistence,
    ICurrentUser currentUser,
    IAccountAccessPolicy accountAccessPolicy,
    IAccountFeatureAccessResolver featureAccessResolver,
    IUserAccessPolicy userAccessPolicy,
    IClock clock,
    IKeepRequestListCursorProtector cursorProtector)
{
    private const int DefaultLimit = 20;
    private const int MaxLimit = 100;

    private static readonly Error Unauthorized =
        Error.Create("auth.unauthorized", "Authentication required.");

    private static readonly Error Forbidden =
        Error.Create("auth.forbidden", "You do not have permission to perform this action.");

    private static readonly Error InvalidLimit =
        Error.Create("FieldScopeSearch.ValidationInvalidLimit", "Limit must be between 1 and 100.");

    private static readonly Error InvalidCursor =
        Error.Create("FieldScopeSearch.ValidationInvalidCursor",
            "The cursor is invalid, malformed, or does not match the current query.");

    public async Task<Result<FieldScopeSearchPage>> SearchAsync(FieldScopeSearchApiQuery query, CancellationToken ct)
    {
        var gate = await AuthorizeAsync(ct);
        if (gate.IsFailure)
            return Result<FieldScopeSearchPage>.Failure(gate.Error);

        var limit = query.Limit ?? DefaultLimit;
        if (limit < 1 || limit > MaxLimit)
            return Result<FieldScopeSearchPage>.Failure(InvalidLimit);

        var searchTerm = query.Search?.Trim() ?? string.Empty;
        if (searchTerm.Length == 0)
            return Result<FieldScopeSearchPage>.Success(new FieldScopeSearchPage([], limit, false, null));

        var fingerprint = FieldScopeSearchCursor.ComputeFingerprint(searchTerm);
        var state = new FieldScopeSearchCursorState(null, null);
        if (!string.IsNullOrEmpty(query.Cursor))
        {
            if (!FieldScopeSearchCursor.TryDecode(cursorProtector, query.Cursor, fingerprint, out var decoded) || decoded is null)
                return Result<FieldScopeSearchPage>.Failure(InvalidCursor);
            state = decoded;
        }

        var fetchCount = limit + 1;

        var catalogFilters = new CatalogItemListFilters(searchTerm, null, null, CatalogItemActiveState.Active);
        var catalogBuffer = await catalogPersistence.SearchItemsAsync(
            currentUser.AccountId, catalogFilters, state.CatalogPosition, fetchCount, ct);

        // Keep pulling raw assembly chunks until the eligible buffer can fill a page or the raw
        // stream is exhausted — see class remarks for why a single bounded fetch is not safe here.
        var assemblyEligibleBuffer = new List<OfferingAssemblySearchRow>();
        var rawScanPosition = state.AssemblyPosition;
        while (assemblyEligibleBuffer.Count < fetchCount)
        {
            ct.ThrowIfCancellationRequested();
            var rawChunk = await assemblyPersistence.SearchAsync(
                currentUser.AccountId, searchTerm, rawScanPosition, fetchCount, ct);
            if (rawChunk.Count == 0)
                break;

            assemblyEligibleBuffer.AddRange(rawChunk.Where(r => r.IsOperationallyEligible));
            var lastRaw = rawChunk[^1];
            rawScanPosition = new OfferingAssemblySearchCursorPosition(lastRaw.MatchRank, lastRaw.Name, lastRaw.Id);

            if (rawChunk.Count < fetchCount)
                break;
        }

        var merged = new List<FieldScopeSearchResultRow>();
        var catalogIndex = 0;
        var assemblyIndex = 0;
        while (merged.Count < limit && (catalogIndex < catalogBuffer.Count || assemblyIndex < assemblyEligibleBuffer.Count))
        {
            if (ShouldPickCatalog(catalogBuffer, catalogIndex, assemblyEligibleBuffer, assemblyIndex))
                merged.Add(ToResultRow(catalogBuffer[catalogIndex++]));
            else
                merged.Add(ToResultRow(assemblyEligibleBuffer[assemblyIndex++]));
        }

        var hasMore = catalogIndex < catalogBuffer.Count || assemblyIndex < assemblyEligibleBuffer.Count;

        string? nextCursor = null;
        if (hasMore)
        {
            // Position at the last row actually placed on this page, never the raw scan-ahead
            // point — an unconsumed position stays put and is simply re-fetched (redundant, safe)
            // rather than being jumped over (lossy) next page.
            var catalogPosition = catalogIndex > 0
                ? PositionOf(catalogBuffer[catalogIndex - 1])
                : state.CatalogPosition;
            var assemblyPosition = assemblyIndex > 0
                ? new OfferingAssemblySearchCursorPosition(
                    assemblyEligibleBuffer[assemblyIndex - 1].MatchRank,
                    assemblyEligibleBuffer[assemblyIndex - 1].Name,
                    assemblyEligibleBuffer[assemblyIndex - 1].Id)
                : state.AssemblyPosition;

            nextCursor = FieldScopeSearchCursor.Encode(
                cursorProtector, fingerprint,
                new FieldScopeSearchCursorState(catalogPosition, assemblyPosition));
        }

        return Result<FieldScopeSearchPage>.Success(new FieldScopeSearchPage(merged, limit, hasMore, nextCursor));
    }

    // Global cross-kind order: (Rank, DisplayName, Kind, Id). Kind is a fixed, arbitrary tiebreak
    // (assembly before catalog item) needed only when rank and name both tie — the total order must
    // never depend on fetch/iteration order.
    private static bool ShouldPickCatalog(
        IReadOnlyList<CatalogItemListRow> catalogBuffer, int catalogIndex,
        IReadOnlyList<OfferingAssemblySearchRow> assemblyBuffer, int assemblyIndex)
    {
        if (catalogIndex >= catalogBuffer.Count)
            return false;
        if (assemblyIndex >= assemblyBuffer.Count)
            return true;

        var c = catalogBuffer[catalogIndex];
        var a = assemblyBuffer[assemblyIndex];

        var rankCompare = ((int)c.MatchRank).CompareTo((int)a.MatchRank);
        if (rankCompare != 0)
            return rankCompare < 0;

        var nameCompare = string.Compare(c.Item.DisplayName, a.Name, StringComparison.Ordinal);
        if (nameCompare != 0)
            return nameCompare < 0;

        return false;
    }

    private static CatalogItemListCursorPosition PositionOf(CatalogItemListRow row) =>
        new(row.MatchRank, row.Item.DisplayName, row.Item.Id);

    private static FieldScopeSearchResultRow ToResultRow(CatalogItemListRow row) => new(
        FieldScopeSearchResultKind.CatalogItem,
        row.Item.Id,
        row.Item.DisplayName,
        DefaultItemCount: null,
        CatalogItemType: row.Item.Type.ToString(),
        ExternalKey: row.Item.ExternalKey);

    private static FieldScopeSearchResultRow ToResultRow(OfferingAssemblySearchRow row) => new(
        FieldScopeSearchResultKind.OfferingAssembly,
        row.Id,
        row.Name,
        DefaultItemCount: row.ItemCount,
        CatalogItemType: null,
        ExternalKey: null);

    private async Task<Result> AuthorizeAsync(CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result.Failure(Unauthorized);

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

        var featureContext = new AccountFeatureAccessContext(accountSnapshot.Plan);
        var enabled = await featureAccessResolver.IsEnabledAsync(
            currentUser.AccountId, featureContext, CapabilityPackageFeatureKeys.PriceBookQuotesMaterials, ct);
        if (!enabled)
            return Result.Failure(Forbidden);

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
