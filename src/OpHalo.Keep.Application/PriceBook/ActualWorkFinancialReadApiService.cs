using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Keep.Application.Requests;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.PriceBook;

/// <summary>One review-queue row: a submitted, unreviewed visit plus its request navigation context
/// and visit-level totals (Batch 7, build-log/129). Never a per-request rollup — each visit on a
/// request appears as its own row.</summary>
public sealed record ActualWorkReviewQueueEntry(
    Guid ActualWorkId,
    Guid RequestId,
    string ReferenceCode,
    string CustomerName,
    DateTime SubmittedAtUtc,
    bool HasIncompleteFinancialData,
    int IncompleteLineCount,
    decimal? TotalSalesPrice,
    decimal? TotalStandardExpectedDirectCost,
    decimal? TotalMargin);

/// <summary>A single line's factual record plus its computed financial snapshot (Batch 7,
/// build-log/129). <see cref="IsFinancialDataComplete"/> is decided by
/// <see cref="SellPriceSnapshot"/>/<see cref="StandardExpectedDirectCostSnapshot"/> both being
/// non-null — not by whether the line carries a Price Book version-line id, which is only an
/// explanatory field distinguishing a custom line from a catalog item that currently carries no
/// Price Book entry (<see cref="ActualWorkLine"/>'s three-state doc comment). Line totals/margin are
/// computed here from the immutable snapshots and are null whenever the line is incomplete — never
/// deferred to the UI.</summary>
public sealed record ActualWorkFinancialLineEntry(
    Guid Id,
    string DisplayNameSnapshot,
    string? UnitOfMeasureSnapshot,
    decimal ActualQuantity,
    string? Note,
    Guid PerformedByAccountUserId,
    string? PerformerDisplayName,
    bool IsFinancialDataComplete,
    decimal? SellPriceSnapshot,
    decimal? StandardExpectedDirectCostSnapshot,
    decimal? LineSalesTotal,
    decimal? LineStandardExpectedDirectCostTotal,
    decimal? LineMargin,
    bool SellPriceResolved,
    decimal? ResolvedSellPrice,
    string? ResolvedSellPriceBasis,
    bool DirectCostResolved,
    decimal? ResolvedStandardExpectedDirectCost,
    string? ResolvedStandardExpectedDirectCostBasis);

/// <summary>One unresolved line component blocking financial completeness of a submitted visit
/// (BL135 §4 Batch 3a-iii). A line appears once, naming whichever of its two components still has
/// neither a captured snapshot nor an effective financial resolution. Covers line components only —
/// the zero-line no-charge disposition path is Batch 3b (build-log/135 §5 proof 1).</summary>
public sealed record ActualWorkFinancialBlocker(
    Guid LineId,
    string DisplayNameSnapshot,
    bool SellPriceMissing,
    bool StandardExpectedDirectCostMissing);

/// <summary>The full factual and financial record of one submitted visit, for the Owner/Admin
/// request-detail review card (Batch 7, build-log/129). Supports both an unreviewed and an already-
/// reviewed submitted visit — never a <c>Draft</c>. Visit-level totals are null whenever any line is
/// incomplete, matching <see cref="ActualWorkReviewQueueEntry"/>'s rule. <see
/// cref="ConcurrencyVersion"/> (Slice 8A contract patch) is the expected-version the review card
/// must echo back on <c>POST .../review</c> — this read is the only place the review card can
/// obtain it, since it never opens the underlying <c>ActualWork</c> as a Draft.</summary>
public sealed record ActualWorkFinancialDetailResult(
    Guid Id,
    Guid RequestId,
    ActualWorkStatus Status,
    ActualWorkOutcome? Outcome,
    string? CompletionNote,
    string? VisitNote,
    Guid RecorderAccountUserId,
    DateTime SubmittedAtUtc,
    DateTime? ReviewedAtUtc,
    Guid? ReviewedByAccountUserId,
    string? ReviewedByDisplayName,
    string? ReviewNote,
    bool HasIncompleteFinancialData,
    decimal? TotalSalesPrice,
    decimal? TotalStandardExpectedDirectCost,
    decimal? TotalMargin,
    IReadOnlyList<ActualWorkFinancialLineEntry> Lines,
    Guid ConcurrencyVersion,
    IReadOnlyList<ActualWorkFinancialBlocker> Blockers,
    // BL135 §4 Batch 4a: true once a NoCharge office disposition has been recorded for this visit.
    // The zero-line review card reads this for a truthful post-reload "disposition recorded" state
    // rather than inferring it from a successful mutation; the hard review gate stays the race
    // backstop. Always false for a visit that has lines (disposition is zero-line-only).
    bool HasNoChargeDisposition);

/// <summary>
/// API-facing read orchestration for Owner/Admin Actual Work financial review (Batch 7,
/// build-log/129): a lightweight account-wide unreviewed-review queue and a single-visit financial
/// detail read. Both are read-only — no mutation. The financial-detail read exposes
/// <c>ConcurrencyVersion</c> (Slice 8A contract patch) so the review card has an expected version to
/// send to <c>POST .../review</c>; the review-queue list does not. Gate is
/// identical to <see cref="ActualWorkReviewApiService.AuthorizeAsync"/>: Owner/Admin role,
/// <c>RequestsOperate</c>, the Price Book entitlement, non-blocked/non-read-only account access — no
/// <c>ActualWorkCapture</c>, no new permission key. Duplicated here rather than shared, matching the
/// existing pattern of each Actual Work read/review service owning its own authorization copy.
/// </summary>
public sealed class ActualWorkFinancialReadApiService(
    IActualWorkFinancialReviewPersistence financialReviewPersistence,
    IActualWorkPersistence actualWorkPersistence,
    IActualWorkFinancialResolutionPersistence financialResolutionPersistence,
    IKeepRequestOperatePersistence operatePersistence,
    IAccountAccessSnapshotPersistence snapshotPersistence,
    ICurrentUser currentUser,
    IAccountAccessPolicy accountAccessPolicy,
    IAccountFeatureAccessResolver featureAccessResolver,
    IUserAccessPolicy userAccessPolicy,
    IClock clock)
{
    private static readonly Error Unauthorized =
        Error.Create("auth.unauthorized", "Authentication required.");

    private static readonly Error Forbidden =
        Error.Create("auth.forbidden", "You do not have permission to perform this action.");

    public async Task<Result<IReadOnlyList<ActualWorkReviewQueueEntry>>> GetReviewQueueAsync(CancellationToken ct)
    {
        var gate = await AuthorizeAsync(ct);
        if (gate.IsFailure)
            return Result<IReadOnlyList<ActualWorkReviewQueueEntry>>.Failure(gate.Error);

        var rows = await financialReviewPersistence.GetUnreviewedQueueAsync(currentUser.AccountId, ct);

        return Result<IReadOnlyList<ActualWorkReviewQueueEntry>>.Success(
            rows.Select(ToQueueEntry).ToArray());
    }

    /// <summary>Authoritative count for the same queue <see cref="GetReviewQueueAsync"/> returns, for
    /// badge/aggregate display that must not force a full queue load to get a number.</summary>
    public async Task<Result<int>> GetReviewQueueCountAsync(CancellationToken ct)
    {
        var gate = await AuthorizeAsync(ct);
        if (gate.IsFailure)
            return Result<int>.Failure(gate.Error);

        var count = await financialReviewPersistence.CountUnreviewedAsync(currentUser.AccountId, ct);
        return Result<int>.Success(count);
    }

    public async Task<Result<ActualWorkFinancialDetailResult>> GetFinancialDetailAsync(
        Guid actualWorkId, CancellationToken ct)
    {
        var gate = await AuthorizeAsync(ct);
        if (gate.IsFailure)
            return Result<ActualWorkFinancialDetailResult>.Failure(gate.Error);

        var visit = await actualWorkPersistence.GetByIdAsync(currentUser.AccountId, actualWorkId, ct);
        if (visit is null)
            return Result<ActualWorkFinancialDetailResult>.Failure(ActualWorkErrors.NotFound);

        if (visit.Status != ActualWorkStatus.Submitted)
            return Result<ActualWorkFinancialDetailResult>.Failure(ActualWorkErrors.NotSubmitted);

        // Resolve the reviewer's display name so the review card can name who reviewed the visit
        // rather than surface a raw account-user id (mirrors the recorder-identity resolution the
        // history read added in 1a-ii-a). Null for a not-yet-reviewed visit.
        var reviewedByDisplayName = visit.ReviewedByAccountUserId is { } reviewerId
            ? await operatePersistence.GetActorDisplayNameAsync(reviewerId, ct)
            : null;

        // Effective per-component financial state folds in every resolution appended before review
        // (BL135 §4 Batch 3a-iii). Account-scoped, newest-first from the seam; the projection
        // resolves sell price and direct cost independently, each from its own most-recent
        // supplying row.
        var resolutions = await financialResolutionPersistence.GetResolutionsForVisitAsync(
            currentUser.AccountId, actualWorkId, ct);

        // BL135 §4 Batch 4a: expose whether a zero-line NoCharge disposition already exists so the
        // review card can render a truthful recorded state after an authoritative reload.
        var dispositions = await financialResolutionPersistence.GetDispositionsForVisitAsync(
            currentUser.AccountId, actualWorkId, ct);
        var hasNoChargeDisposition =
            dispositions.Any(d => d.Kind == OfficeFinancialDispositionKind.NoCharge);

        // Per-distinct-id memoized performer-name resolution (locked 2026-08-29): one
        // GetActorDisplayNameAsync call per distinct line performer; a visit carries 1–2. No batch
        // seam method — mirrors the ReviewedByDisplayName resolution above.
        var performerNames = new Dictionary<Guid, string?>();
        foreach (var performerId in visit.Lines.Select(l => l.PerformedByAccountUserId).Distinct())
            performerNames[performerId] = await operatePersistence.GetActorDisplayNameAsync(performerId, ct);

        return Result<ActualWorkFinancialDetailResult>.Success(
            ToDetailResult(visit, reviewedByDisplayName, resolutions, hasNoChargeDisposition, performerNames));
    }

    /// <summary>The review-queue source seam does not carry financial-resolution rows, so queue-row
    /// totals stay snapshot-only: a visit whose blockers have since been resolved still reads
    /// pessimistically incomplete in the queue until Batch 3b-ii's transactional review gate. The
    /// direction is safe — the queue never reports "ready" when it is not.</summary>
    private static readonly IReadOnlyList<ActualWorkLineFinancialResolution> NoResolutions =
        Array.Empty<ActualWorkLineFinancialResolution>();

    private static ActualWorkReviewQueueEntry ToQueueEntry(ActualWorkReviewQueueSourceRow row)
    {
        var totals = ActualWorkFinancialProjection.ProjectVisit(row.Visit.Lines, NoResolutions).Totals;
        return new ActualWorkReviewQueueEntry(
            row.Visit.Id, row.Visit.RequestId, row.ReferenceCode, row.CustomerName,
            row.Visit.SubmittedAtUtc!.Value, totals.HasIncompleteFinancialData, totals.IncompleteLineCount,
            totals.TotalSalesPrice, totals.TotalStandardExpectedDirectCost, totals.TotalMargin);
    }

    private static ActualWorkFinancialDetailResult ToDetailResult(
        ActualWork visit, string? reviewedByDisplayName,
        IReadOnlyList<ActualWorkLineFinancialResolution> resolutions,
        bool hasNoChargeDisposition,
        IReadOnlyDictionary<Guid, string?> performerNames)
    {
        var lines = visit.Lines.OrderBy(l => l.CreatedAtUtc).ThenBy(l => l.Id).ToArray();
        var projection = ActualWorkFinancialProjection.ProjectVisit(lines, resolutions, performerNames);
        var totals = projection.Totals;

        return new ActualWorkFinancialDetailResult(
            visit.Id, visit.RequestId, visit.Status, visit.Outcome, visit.CompletionNote, visit.VisitNote,
            visit.RecorderAccountUserId, visit.SubmittedAtUtc!.Value, visit.ReviewedAtUtc,
            visit.ReviewedByAccountUserId, reviewedByDisplayName, visit.ReviewNote, totals.HasIncompleteFinancialData,
            totals.TotalSalesPrice, totals.TotalStandardExpectedDirectCost, totals.TotalMargin,
            projection.Lines, visit.ConcurrencyVersion, projection.Blockers, hasNoChargeDisposition);
    }

    /// <summary>Owner/Admin office-review gate: authenticated, non-blocked/read-only account access,
    /// the Price Book entitlement, <c>RequestsOperate</c>, the <c>AccountingManage</c> office-financial
    /// permission (ADR-493 / BL135), and an explicit Owner/Admin role check for defense-in-depth —
    /// no <c>ActualWorkCapture</c>. Identical composition to
    /// <see cref="ActualWorkReviewApiService.AuthorizeAsync"/>.</summary>
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
        if (decision.IsBlocked || decision.IsReadOnly)
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

        if (roleSnapshot.Role is not (AccountUserRole.Owner or AccountUserRole.Admin))
            return Result.Failure(Forbidden);

        if (!userAccessPolicy.IsPermitted(
                roleSnapshot.Role, roleSnapshot.MembershipStatus, accountSnapshot.Purpose,
                PermissionKeys.Keep.RequestsOperate))
            return Result.Failure(Forbidden);

        if (!userAccessPolicy.IsPermitted(
                roleSnapshot.Role, roleSnapshot.MembershipStatus, accountSnapshot.Purpose,
                PermissionKeys.Keep.AccountingManage))
            return Result.Failure(Forbidden);

        return Result.Success();
    }
}

/// <summary>Pure line/visit financial projection shared by the review queue and financial-detail
/// reads (Batch 7, build-log/129). No dependencies — directly unit-testable. Internal, exposed to
/// <c>OpHalo.UnitTests</c> via the project's existing <c>InternalsVisibleTo</c>.</summary>
internal static class ActualWorkFinancialProjection
{
    internal sealed record VisitTotals(
        bool HasIncompleteFinancialData,
        int IncompleteLineCount,
        decimal? TotalSalesPrice,
        decimal? TotalStandardExpectedDirectCost,
        decimal? TotalMargin);

    /// <summary>The single per-line financial fold (BL135 §4 Batch 3a-iii): the effective value of
    /// each component is its captured snapshot, or — only when the snapshot is missing — the
    /// most-recent financial-resolution row supplying that component. Sell price and direct cost
    /// resolve independently, each carrying its own provenance (build-log/135 §5 proof 2). Line
    /// completeness, line totals, and the blocker list are all derived from this one value so they
    /// cannot drift in the mixed-provenance case.</summary>
    internal readonly record struct EffectiveLineFinancials(
        decimal? SellPrice,
        bool SellPriceResolved,
        FinancialResolutionBasis? SellPriceBasis,
        decimal? StandardExpectedDirectCost,
        bool StandardExpectedDirectCostResolved,
        FinancialResolutionBasis? StandardExpectedDirectCostBasis)
    {
        internal bool IsComplete => SellPrice is not null && StandardExpectedDirectCost is not null;
    }

    /// <summary>ADR-467: traditional round-half-up, applied to each computed line total
    /// independently. Financial inputs and quantities are non-negative in this domain, so
    /// <see cref="MidpointRounding.AwayFromZero"/> is round-half-up. Visit totals are the sum of
    /// these already-rounded line totals — never one rounding of an unrounded sum. No reusable
    /// quote-line helper exists yet; Batch 7b's snapshot writer reuses this one.</summary>
    internal static decimal RoundMoney(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>Fold one line against the visit's resolution rows, which the caller
    /// (<see cref="ProjectVisit"/>) has already ordered <c>ResolvedAtUtc DESC, Id DESC</c> — so the
    /// first supplying row per component is the most recent one.</summary>
    private static EffectiveLineFinancials ComputeEffective(
        ActualWorkLine line, IReadOnlyList<ActualWorkLineFinancialResolution> orderedResolutions)
    {
        var lineRows = orderedResolutions.Where(r => r.ActualWorkLineId == line.Id).ToArray();

        var sell = line.SellPriceSnapshot;
        var sellResolved = false;
        FinancialResolutionBasis? sellBasis = null;
        if (sell is null)
        {
            var row = lineRows.FirstOrDefault(r => r.ResolvedUnitSellPrice is not null);
            if (row is not null)
            {
                sell = row.ResolvedUnitSellPrice;
                sellResolved = true;
                sellBasis = row.Basis;
            }
        }

        var cost = line.StandardExpectedDirectCostSnapshot;
        var costResolved = false;
        FinancialResolutionBasis? costBasis = null;
        if (cost is null)
        {
            var row = lineRows.FirstOrDefault(r => r.ResolvedUnitStandardExpectedDirectCost is not null);
            if (row is not null)
            {
                cost = row.ResolvedUnitStandardExpectedDirectCost;
                costResolved = true;
                costBasis = row.Basis;
            }
        }

        return new EffectiveLineFinancials(
            sell, sellResolved, sellBasis, cost, costResolved, costBasis);
    }

    /// <summary>Totals, line DTOs, and blockers for one visit, all derived from a single per-line
    /// fold.</summary>
    internal sealed record VisitProjection(
        VisitTotals Totals,
        IReadOnlyList<ActualWorkFinancialLineEntry> Lines,
        IReadOnlyList<ActualWorkFinancialBlocker> Blockers);

    /// <summary>The one entry point for a visit's financial read (BL135 §4 Batch 3a-iii): resolution
    /// rows are ordered once (<c>ResolvedAtUtc DESC, Id DESC</c>), each line's effective per-component
    /// state is computed once, and completeness, rounded totals, line DTOs, and the blocker list are
    /// every one derived from that same per-line state — so they cannot drift in the mixed-provenance
    /// case. The persistence seam already returns rows newest-first; the re-order here also lets unit
    /// tests pass rows in any order.</summary>
    /// <summary>Per-distinct-id performer display names (4c-ii-b). Optional — the review-queue
    /// projection needs only totals, so it omits it; the financial-detail read supplies it.</summary>
    internal static VisitProjection ProjectVisit(
        IReadOnlyCollection<ActualWorkLine> lines,
        IReadOnlyList<ActualWorkLineFinancialResolution> resolutions,
        IReadOnlyDictionary<Guid, string?>? performerNames = null)
    {
        var orderedResolutions = resolutions
            .OrderByDescending(r => r.ResolvedAtUtc)
            .ThenByDescending(r => r.Id)
            .ToArray();

        var folds = lines
            .Select(line => (Line: line, Fin: ComputeEffective(line, orderedResolutions)))
            .ToArray();

        var incompleteCount = folds.Count(f => !f.Fin.IsComplete);
        var totals = incompleteCount > 0
            ? new VisitTotals(true, incompleteCount, null, null, null)
            : BuildCompleteTotals(folds);

        var lineEntries = folds.Select(f => ToLineEntry(f.Line, f.Fin, performerNames)).ToArray();

        var blockers = folds
            .Where(f => !f.Fin.IsComplete)
            .Select(f => new ActualWorkFinancialBlocker(
                f.Line.Id, f.Line.DisplayNameSnapshot,
                SellPriceMissing: f.Fin.SellPrice is null,
                StandardExpectedDirectCostMissing: f.Fin.StandardExpectedDirectCost is null))
            .ToArray();

        return new VisitProjection(totals, lineEntries, blockers);
    }

    private static VisitTotals BuildCompleteTotals(
        IReadOnlyList<(ActualWorkLine Line, EffectiveLineFinancials Fin)> folds)
    {
        // ADR-467: each line total rounded independently; visit totals are the exact sum of those.
        var totalSales = folds.Sum(f => RoundMoney(f.Fin.SellPrice!.Value * f.Line.ActualQuantity));
        var totalCost = folds.Sum(f => RoundMoney(f.Fin.StandardExpectedDirectCost!.Value * f.Line.ActualQuantity));
        return new VisitTotals(false, 0, totalSales, totalCost, totalSales - totalCost);
    }

    private static ActualWorkFinancialLineEntry ToLineEntry(
        ActualWorkLine line, EffectiveLineFinancials fin, IReadOnlyDictionary<Guid, string?>? performerNames)
    {
        var lineSalesTotal = fin.IsComplete ? RoundMoney(fin.SellPrice!.Value * line.ActualQuantity) : (decimal?)null;
        var lineCostTotal = fin.IsComplete ? RoundMoney(fin.StandardExpectedDirectCost!.Value * line.ActualQuantity) : (decimal?)null;
        var lineMargin = fin.IsComplete ? lineSalesTotal - lineCostTotal : (decimal?)null;

        return new ActualWorkFinancialLineEntry(
            line.Id, line.DisplayNameSnapshot, line.UnitOfMeasureSnapshot, line.ActualQuantity, line.Note,
            line.PerformedByAccountUserId,
            performerNames is null ? null : performerNames.GetValueOrDefault(line.PerformedByAccountUserId),
            fin.IsComplete, line.SellPriceSnapshot, line.StandardExpectedDirectCostSnapshot,
            lineSalesTotal, lineCostTotal, lineMargin,
            fin.SellPriceResolved, fin.SellPriceResolved ? fin.SellPrice : null, fin.SellPriceBasis?.ToString(),
            fin.StandardExpectedDirectCostResolved, fin.StandardExpectedDirectCostResolved ? fin.StandardExpectedDirectCost : null,
            fin.StandardExpectedDirectCostBasis?.ToString());
    }
}
