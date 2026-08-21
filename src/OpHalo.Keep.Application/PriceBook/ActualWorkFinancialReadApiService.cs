using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
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
    bool IsFinancialDataComplete,
    decimal? SellPriceSnapshot,
    decimal? StandardExpectedDirectCostSnapshot,
    decimal? LineSalesTotal,
    decimal? LineStandardExpectedDirectCostTotal,
    decimal? LineMargin);

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
    Guid RecorderAccountUserId,
    DateTime SubmittedAtUtc,
    DateTime? ReviewedAtUtc,
    Guid? ReviewedByAccountUserId,
    string? ReviewNote,
    bool HasIncompleteFinancialData,
    decimal? TotalSalesPrice,
    decimal? TotalStandardExpectedDirectCost,
    decimal? TotalMargin,
    IReadOnlyList<ActualWorkFinancialLineEntry> Lines,
    Guid ConcurrencyVersion);

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

        return Result<ActualWorkFinancialDetailResult>.Success(ToDetailResult(visit));
    }

    private static ActualWorkReviewQueueEntry ToQueueEntry(ActualWorkReviewQueueSourceRow row)
    {
        var totals = ActualWorkFinancialProjection.ComputeVisitTotals(row.Visit.Lines);
        return new ActualWorkReviewQueueEntry(
            row.Visit.Id, row.Visit.RequestId, row.ReferenceCode, row.CustomerName,
            row.Visit.SubmittedAtUtc!.Value, totals.HasIncompleteFinancialData, totals.IncompleteLineCount,
            totals.TotalSalesPrice, totals.TotalStandardExpectedDirectCost, totals.TotalMargin);
    }

    private static ActualWorkFinancialDetailResult ToDetailResult(ActualWork visit)
    {
        var lines = visit.Lines.OrderBy(l => l.CreatedAtUtc).ThenBy(l => l.Id).ToArray();
        var totals = ActualWorkFinancialProjection.ComputeVisitTotals(lines);

        return new ActualWorkFinancialDetailResult(
            visit.Id, visit.RequestId, visit.Status, visit.Outcome, visit.CompletionNote,
            visit.RecorderAccountUserId, visit.SubmittedAtUtc!.Value, visit.ReviewedAtUtc,
            visit.ReviewedByAccountUserId, visit.ReviewNote, totals.HasIncompleteFinancialData,
            totals.TotalSalesPrice, totals.TotalStandardExpectedDirectCost, totals.TotalMargin,
            lines.Select(ActualWorkFinancialProjection.ToLineEntry).ToArray(), visit.ConcurrencyVersion);
    }

    /// <summary>Owner/Admin office-review gate: authenticated, non-blocked/read-only account access,
    /// the Price Book entitlement, <c>RequestsOperate</c>, and an explicit Owner/Admin role check —
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

    /// <summary>A line is incomplete when either required financial snapshot is missing —
    /// <see cref="ActualWorkLine.SellPriceSnapshot"/> or
    /// <see cref="ActualWorkLine.StandardExpectedDirectCostSnapshot"/> null. Deliberately not based
    /// on <see cref="ActualWorkLine.PriceBookVersionLineId"/> — the domain does not guarantee the two
    /// snapshot fields are non-null whenever that id is set (build-log/129, "7 preflight —
    /// corrected 2026-08-21").</summary>
    internal static bool IsLineComplete(ActualWorkLine line) =>
        line.SellPriceSnapshot is not null && line.StandardExpectedDirectCostSnapshot is not null;

    internal static VisitTotals ComputeVisitTotals(IReadOnlyCollection<ActualWorkLine> lines)
    {
        var incompleteCount = lines.Count(l => !IsLineComplete(l));
        if (incompleteCount > 0)
            return new VisitTotals(true, incompleteCount, null, null, null);

        var totalSales = lines.Sum(l => l.SellPriceSnapshot!.Value * l.ActualQuantity);
        var totalCost = lines.Sum(l => l.StandardExpectedDirectCostSnapshot!.Value * l.ActualQuantity);
        return new VisitTotals(false, 0, totalSales, totalCost, totalSales - totalCost);
    }

    internal static ActualWorkFinancialLineEntry ToLineEntry(ActualWorkLine line)
    {
        var isComplete = IsLineComplete(line);
        var lineSalesTotal = isComplete ? line.SellPriceSnapshot!.Value * line.ActualQuantity : (decimal?)null;
        var lineCostTotal = isComplete ? line.StandardExpectedDirectCostSnapshot!.Value * line.ActualQuantity : (decimal?)null;
        var lineMargin = isComplete ? lineSalesTotal - lineCostTotal : (decimal?)null;

        return new ActualWorkFinancialLineEntry(
            line.Id, line.DisplayNameSnapshot, line.UnitOfMeasureSnapshot, line.ActualQuantity, line.Note,
            isComplete, line.SellPriceSnapshot, line.StandardExpectedDirectCostSnapshot,
            lineSalesTotal, lineCostTotal, lineMargin);
    }
}
