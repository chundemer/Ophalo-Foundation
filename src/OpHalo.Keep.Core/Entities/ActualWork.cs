using OpHalo.Foundation.Core.Entities.Shared;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Core.Entities;

/// <summary>
/// A field-recorded visit/execution event against a <c>KeepRequest</c> (ADR-487, build-log/129).
/// Price-Book-module-owned: references <see cref="RequestId"/> by id only, never the
/// <c>KeepRequest</c> entity itself. A distinct record from <see cref="ProposedScope"/> — never a
/// status change on it or on a commercial record. Price-blind at capture: no price/cost/margin
/// control exists on this record or <see cref="ActualWorkLine"/> for the recording technician;
/// Owner/Admin financial review (Batch 7) reads the immutable snapshots captured here.
///
/// Mutable only while <see cref="Status"/> is <see cref="ActualWorkStatus.Draft"/>; the pilot locks
/// one open Draft per request (a database partial unique index, not enforced here), owned by
/// <see cref="RecorderAccountUserId"/> — first-recorder ownership (GAP-055, superseding the
/// active-Responsible-only recorder rule): any qualified member may create it, and only its current
/// recorder may mutate or submit it, unless an Owner/Admin performs an explicit, reason-required
/// <see cref="TransferRecorder"/>. <see cref="Submit"/> is a pure status transition —
/// raising/reopening the request's Actual Work review signal is a separate atomic persistence
/// operation (Batch 4), never a side effect of this aggregate's own state change. A complex request
/// may retain multiple immutable submitted visit records; a later visit is always a new
/// <see cref="ActualWork"/> row, never a reopen of a submitted one.
/// </summary>
public sealed class ActualWork : BaseEntity
{
    public Guid AccountId { get; private set; }

    public Guid RequestId { get; private set; }

    public ActualWorkStatus Status { get; private set; }

    /// <summary>Required only when <see cref="Submit"/> is called with zero lines
    /// (build-log/129) — the truthful reason no billable work occurred.</summary>
    public ActualWorkOutcome? Outcome { get; private set; }

    /// <summary>Required only when <see cref="Submit"/> is called with zero lines. Distinct from a
    /// per-line <see cref="ActualWorkLine.Note"/>.</summary>
    public string? CompletionNote { get; private set; }

    public DateTime? SubmittedAtUtc { get; private set; }

    /// <summary>Set only by <see cref="MarkReviewed"/> (Batch 6) — office acknowledgement of a
    /// submitted visit. Never overwritten; a repeat <see cref="MarkReviewed"/> call is rejected
    /// rather than replacing this timestamp.</summary>
    public DateTime? ReviewedAtUtc { get; private set; }

    /// <summary>The Owner/Admin who reviewed this visit. Distinct from
    /// <see cref="RecorderAccountUserId"/> — review is an office role capability, never the field
    /// recorder's own action.</summary>
    public Guid? ReviewedByAccountUserId { get; private set; }

    /// <summary>Optional, trimmed-to-null, max 2,000 characters (Batch 6) — matches the feedback-
    /// review note convention. Distinct from <see cref="CompletionNote"/>, which is the
    /// technician's own field note.</summary>
    public string? ReviewNote { get; private set; }

    /// <summary>Current recorder-ownership holder (GAP-055): distinct from the immutable
    /// <see cref="Foundation.Core.Entities.Shared.BaseEntity.CreatedByUserId"/> authorship set at
    /// <see cref="Create"/>. Set at creation to the creating caller and changed only by
    /// <see cref="TransferRecorder"/>.</summary>
    public Guid RecorderAccountUserId { get; private set; }

    /// <summary>
    /// Application-managed opaque concurrency token — same pattern as
    /// <see cref="ProposedScope.ConcurrencyVersion"/>.
    /// </summary>
    public Guid ConcurrencyVersion { get; private set; } = Guid.NewGuid();

    private readonly List<ActualWorkLine> _lines = [];

    /// <summary>Owned lines: added, updated, and removed only through this aggregate, guarded by
    /// <see cref="ConcurrencyVersion"/> rather than a token of their own.</summary>
    public IReadOnlyCollection<ActualWorkLine> Lines => _lines;

    private ActualWork()
    {
    }

    public static Result<ActualWork> Create(Guid accountId, Guid requestId, Guid createdByUserId)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("AccountId must not be empty.", nameof(accountId));
        if (requestId == Guid.Empty)
            throw new ArgumentException("RequestId must not be empty.", nameof(requestId));
        if (createdByUserId == Guid.Empty)
            throw new ArgumentException("CreatedByUserId must not be empty.", nameof(createdByUserId));

        return Result<ActualWork>.Success(new ActualWork
        {
            CreatedByUserId = createdByUserId,
            AccountId = accountId,
            RequestId = requestId,
            Status = ActualWorkStatus.Draft,
            RecorderAccountUserId = createdByUserId,
            ConcurrencyVersion = Guid.NewGuid(),
        });
    }

    /// <summary>Owner/Admin-only, reason-required recorder-ownership transfer of an unsubmitted
    /// Draft (GAP-055). Changes only <see cref="RecorderAccountUserId"/> — creation authorship
    /// (<see cref="Foundation.Core.Entities.Shared.BaseEntity.CreatedByUserId"/>) never changes.
    /// The caller's authorization (Owner/Admin) and the immutable
    /// <c>ActualWorkDraftRecorderTransferred</c> audit event are the API/persistence layer's
    /// responsibility (Batch D); this method only enforces the domain invariant that a submitted
    /// visit can never be transferred.</summary>
    public Result TransferRecorder(Guid newRecorderAccountUserId)
    {
        if (newRecorderAccountUserId == Guid.Empty)
            throw new ArgumentException("NewRecorderAccountUserId must not be empty.", nameof(newRecorderAccountUserId));

        if (Status != ActualWorkStatus.Draft)
            return Result.Failure(ActualWorkErrors.NotDraft);

        RecorderAccountUserId = newRecorderAccountUserId;
        ConcurrencyVersion = Guid.NewGuid();
        return Result.Success();
    }

    public Result<ActualWorkLine> AddLine(
        Guid? catalogItemId,
        Guid? priceBookVersionLineId,
        string displayNameSnapshot,
        string? unitOfMeasureSnapshot,
        decimal actualQuantity,
        decimal? sellPriceSnapshot,
        decimal? standardExpectedDirectCostSnapshot,
        string? note,
        Guid? commercialBaselineSourceLineId,
        Guid createdByUserId)
    {
        if (Status != ActualWorkStatus.Draft)
            return Result<ActualWorkLine>.Failure(ActualWorkErrors.NotDraft);

        var createResult = ActualWorkLine.Create(
            AccountId, Id, catalogItemId, priceBookVersionLineId, displayNameSnapshot,
            unitOfMeasureSnapshot, actualQuantity, sellPriceSnapshot, standardExpectedDirectCostSnapshot,
            note, commercialBaselineSourceLineId, createdByUserId);
        if (createResult.IsFailure)
            return createResult;

        _lines.Add(createResult.Value);
        ConcurrencyVersion = Guid.NewGuid();
        return createResult;
    }

    /// <summary>Updates only the fields a technician may adjust after capture: quantity and note.
    /// The catalog/price-book snapshot identity and every snapshot value are fixed at creation and
    /// have no update path — a different item is a different line.</summary>
    public Result UpdateLine(Guid lineId, decimal actualQuantity, string? note)
    {
        if (Status != ActualWorkStatus.Draft)
            return Result.Failure(ActualWorkErrors.NotDraft);

        var line = _lines.FirstOrDefault(l => l.Id == lineId);
        if (line is null)
            return Result.Failure(ActualWorkErrors.LineNotFound);

        var updateResult = line.Update(actualQuantity, note);
        if (updateResult.IsFailure)
            return updateResult;

        ConcurrencyVersion = Guid.NewGuid();
        return Result.Success();
    }

    public Result RemoveLine(Guid lineId)
    {
        if (Status != ActualWorkStatus.Draft)
            return Result.Failure(ActualWorkErrors.NotDraft);

        var line = _lines.FirstOrDefault(l => l.Id == lineId);
        if (line is null)
            return Result.Failure(ActualWorkErrors.LineNotFound);

        _lines.Remove(line);
        ConcurrencyVersion = Guid.NewGuid();
        return Result.Success();
    }

    /// <summary>
    /// Pure status transition (<c>Draft</c> to <c>Submitted</c>) and nothing else — no Actual Work
    /// review-signal side effect. The caller (a dedicated atomic persistence operation, Batch 4) is
    /// responsible for coordinating that separately in the same database transaction as this state
    /// change. A non-null <paramref name="outcome"/> must always be a defined
    /// <see cref="ActualWorkOutcome"/> value, whether or not the visit has lines. A zero-line submit
    /// is additionally accepted only with a non-whitespace <paramref name="completionNote"/> and a
    /// non-null <paramref name="outcome"/> (build-log/129); a submit with at least one line accepts
    /// either as optional.
    /// </summary>
    public Result Submit(DateTime submittedAtUtc, ActualWorkOutcome? outcome, string? completionNote)
    {
        if (Status != ActualWorkStatus.Draft)
            return Result.Failure(ActualWorkErrors.NotDraft);

        if (outcome is not null && !Enum.IsDefined(outcome.Value))
            return Result.Failure(ActualWorkErrors.InvalidOutcome);

        if (_lines.Count == 0)
        {
            if (string.IsNullOrWhiteSpace(completionNote))
                return Result.Failure(ActualWorkErrors.ZeroLineCompletionNoteRequired);
            if (outcome is null)
                return Result.Failure(ActualWorkErrors.ZeroLineOutcomeRequired);
        }

        Status = ActualWorkStatus.Submitted;
        Outcome = outcome;
        CompletionNote = completionNote;
        SubmittedAtUtc = submittedAtUtc;
        ConcurrencyVersion = Guid.NewGuid();
        return Result.Success();
    }

    /// <summary>
    /// Office acknowledgement of a submitted visit (Batch 6). Single-shot: only a
    /// <see cref="ActualWorkStatus.Submitted"/> visit with a null <see cref="ReviewedAtUtc"/> may be
    /// reviewed; a repeat call is rejected and never overwrites the existing reviewer, timestamp, or
    /// note. Does not change <see cref="Status"/> — reviewed visits remain <c>Submitted</c> per
    /// <see cref="ActualWorkStatus"/>'s doc comment. <paramref name="reviewNote"/> is trimmed to null
    /// and capped at 2,000 characters, matching the feedback-review note convention. Raising/
    /// resolving the request's Actual Work review signal is a separate atomic persistence operation
    /// (Batch 6), never a side effect of this aggregate's own state change, mirroring <see cref="Submit"/>.
    /// </summary>
    public Result MarkReviewed(Guid reviewedByAccountUserId, string? reviewNote, DateTime reviewedAtUtc)
    {
        if (Status != ActualWorkStatus.Submitted)
            return Result.Failure(ActualWorkErrors.NotSubmitted);

        if (ReviewedAtUtc is not null)
            return Result.Failure(ActualWorkErrors.AlreadyReviewed);

        var trimmedNote = string.IsNullOrWhiteSpace(reviewNote) ? null : reviewNote.Trim();
        if (trimmedNote is not null && trimmedNote.Length > 2000)
            return Result.Failure(ActualWorkErrors.ReviewNoteTooLong);

        ReviewedAtUtc = reviewedAtUtc;
        ReviewedByAccountUserId = reviewedByAccountUserId;
        ReviewNote = trimmedNote;
        ConcurrencyVersion = Guid.NewGuid();
        return Result.Success();
    }
}
