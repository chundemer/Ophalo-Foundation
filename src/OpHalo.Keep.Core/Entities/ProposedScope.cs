using OpHalo.Foundation.Core.Entities.Shared;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Core.Entities;

/// <summary>
/// A field-captured proposed scope of work against a <c>KeepRequest</c> (ADR-461, ADR-473,
/// ADR-480, ADR-481, build-log/108). Price-Book-module-owned: references <see cref="RequestId"/>
/// by id only, never the <c>KeepRequest</c> entity itself. Price-blind — no price/cost/margin
/// field exists anywhere on this aggregate or <see cref="ProposedScopeLine"/>.
///
/// Mutable only while <see cref="Status"/> is <see cref="ProposedScopeStatus.Draft"/>; at most one
/// <c>Draft</c> row exists per request (a database partial unique index on <c>RequestId</c>, not
/// enforced here). <see cref="Submit"/> is a pure status transition only — it does not raise or
/// touch the request's <c>KeepRequestWorkSignal</c> (ADR-463); that coordination is a separate
/// atomic persistence operation (Session 3.3a.2), never a side effect of this aggregate's own
/// state change. A later field visit after this row reaches <c>OfficeReviewed</c> creates a new
/// <see cref="ProposedScope"/> row rather than reopening this one (ADR-464).
/// </summary>
public sealed class ProposedScope : BaseEntity
{
    public Guid AccountId { get; private set; }

    public Guid RequestId { get; private set; }

    public ProposedScopeStatus Status { get; private set; }

    public DateTime? SubmittedAtUtc { get; private set; }

    public Guid? ReviewedByAccountUserId { get; private set; }

    public DateTime? ReviewedAtUtc { get; private set; }

    /// <summary>
    /// Application-managed opaque concurrency token — same pattern as
    /// <see cref="OfferingAssembly.ConcurrencyVersion"/> (ADR-330).
    /// </summary>
    public Guid ConcurrencyVersion { get; private set; } = Guid.NewGuid();

    private readonly List<ProposedScopeLine> _lines = [];

    /// <summary>Owned lines: added, updated, and removed only through this aggregate, guarded by
    /// <see cref="ConcurrencyVersion"/> rather than a token of their own.</summary>
    public IReadOnlyCollection<ProposedScopeLine> Lines => _lines;

    private ProposedScope()
    {
    }

    public static Result<ProposedScope> Create(Guid accountId, Guid requestId, Guid createdByUserId)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("AccountId must not be empty.", nameof(accountId));
        if (requestId == Guid.Empty)
            throw new ArgumentException("RequestId must not be empty.", nameof(requestId));
        if (createdByUserId == Guid.Empty)
            throw new ArgumentException("CreatedByUserId must not be empty.", nameof(createdByUserId));

        return Result<ProposedScope>.Success(new ProposedScope
        {
            CreatedByUserId = createdByUserId,
            AccountId = accountId,
            RequestId = requestId,
            Status = ProposedScopeStatus.Draft,
            ConcurrencyVersion = Guid.NewGuid(),
        });
    }

    public Result<ProposedScopeLine> AddLine(
        ProposedScopeLineType lineType,
        Guid? catalogItemId,
        Guid? offeringAssemblyId,
        decimal quantity,
        bool isException,
        string? offCatalogDescription,
        decimal? offCatalogQuantity,
        string? note,
        int displayOrder,
        string displayNameSnapshot,
        string? unitOfMeasureSnapshot,
        string? offeringAssemblyNameSnapshot,
        decimal? defaultQuantitySnapshot,
        Guid createdByUserId)
    {
        if (Status != ProposedScopeStatus.Draft)
            return Result<ProposedScopeLine>.Failure(ProposedScopeErrors.NotDraft);

        var createResult = ProposedScopeLine.Create(
            AccountId, Id, lineType, catalogItemId, offeringAssemblyId, quantity, isException,
            offCatalogDescription, offCatalogQuantity, note, displayOrder,
            displayNameSnapshot, unitOfMeasureSnapshot, offeringAssemblyNameSnapshot, defaultQuantitySnapshot,
            createdByUserId);
        if (createResult.IsFailure)
            return createResult;

        _lines.Add(createResult.Value);
        ConcurrencyVersion = Guid.NewGuid();
        return createResult;
    }

    public Result UpdateLine(Guid lineId, decimal quantity, bool isException, string? note, int displayOrder)
    {
        if (Status != ProposedScopeStatus.Draft)
            return Result.Failure(ProposedScopeErrors.NotDraft);

        var line = _lines.FirstOrDefault(l => l.Id == lineId);
        if (line is null)
            return Result.Failure(ProposedScopeErrors.LineNotFound);

        var updateResult = line.Update(quantity, isException, note, displayOrder);
        if (updateResult.IsFailure)
            return updateResult;

        ConcurrencyVersion = Guid.NewGuid();
        return Result.Success();
    }

    /// <summary>
    /// Removes a line and returns a full-field snapshot of it (Session 4b, build-log/119 decision
    /// 1) so the caller can persist a short-lived undo-delete record in the same transaction — the
    /// active row is hard-removed here, never soft-deleted.
    /// </summary>
    public Result<RemovedProposedScopeLineSnapshot> RemoveLine(Guid lineId, DateTime nowUtc)
    {
        if (Status != ProposedScopeStatus.Draft)
            return Result<RemovedProposedScopeLineSnapshot>.Failure(ProposedScopeErrors.NotDraft);

        var line = _lines.FirstOrDefault(l => l.Id == lineId);
        if (line is null)
            return Result<RemovedProposedScopeLineSnapshot>.Failure(ProposedScopeErrors.LineNotFound);

        var snapshot = RemovedProposedScopeLineSnapshot.FromLine(line, nowUtc);
        _lines.Remove(line);
        ConcurrencyVersion = Guid.NewGuid();
        return Result<RemovedProposedScopeLineSnapshot>.Success(snapshot);
    }

    /// <summary>
    /// Undo-delete (Session 4b, build-log/119 decision 1): reinserts the line captured by
    /// <paramref name="snapshot"/> at its original id and <c>DisplayOrder</c>. Accepted only when
    /// <paramref name="nowUtc"/> is no more than five seconds after
    /// <see cref="RemovedProposedScopeLineSnapshot.RemovedAtUtc"/> — the client-side toast timer is
    /// presentation only and cannot extend this window. Fails if the original line id is already
    /// present, so a second restore after a successful one is a real conflict, never a silent
    /// no-op. The caller is responsible for hard-deleting the consumed snapshot row in the same
    /// transaction as this mutation's commit.
    /// </summary>
    public Result RestoreLine(RemovedProposedScopeLineSnapshot snapshot, DateTime nowUtc)
    {
        if (Status != ProposedScopeStatus.Draft)
            return Result.Failure(ProposedScopeErrors.NotDraft);

        if (nowUtc > snapshot.RemovedAtUtc.AddSeconds(5))
            return Result.Failure(ProposedScopeErrors.RestoreExpired);

        if (_lines.Any(l => l.Id == snapshot.LineId))
            return Result.Failure(ProposedScopeErrors.RestoreLineAlreadyExists);

        _lines.Add(ProposedScopeLine.Restore(snapshot));
        ConcurrencyVersion = Guid.NewGuid();
        return Result.Success();
    }

    /// <summary>
    /// Pure status transition (<c>Draft</c> to <c>SubmittedToOffice</c>) and nothing else — no
    /// <c>KeepRequestWorkSignal</c> side effect. The caller (a dedicated atomic persistence
    /// operation, Session 3.3a.2) is responsible for coordinating that separately in the same
    /// database transaction as this state change.
    /// </summary>
    public Result Submit(DateTime submittedAtUtc)
    {
        if (Status != ProposedScopeStatus.Draft)
            return Result.Failure(ProposedScopeErrors.NotDraft);
        if (_lines.Count == 0)
            return Result.Failure(ProposedScopeErrors.EmptySubmit);

        Status = ProposedScopeStatus.SubmittedToOffice;
        SubmittedAtUtc = submittedAtUtc;
        ConcurrencyVersion = Guid.NewGuid();
        return Result.Success();
    }
}
