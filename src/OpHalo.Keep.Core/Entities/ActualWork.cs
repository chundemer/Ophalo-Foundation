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

    /// <summary>ADR-494 D2: an optional, mutable Draft-level "Performed by" default that seeds the
    /// performer of <em>new</em> lines only — it never rewrites an existing line's captured
    /// performer, and Draft handoff leaves it untouched. Null until set; cleared by passing null to
    /// <see cref="SetDefaultPerformer"/>. Never server-derived from the creator or recorder.</summary>
    public Guid? DefaultPerformedByAccountUserId { get; private set; }

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

    public static Result<ActualWork> Create(
        Guid accountId,
        Guid requestId,
        Guid createdByUserId,
        Guid? defaultPerformedByAccountUserId = null)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("AccountId must not be empty.", nameof(accountId));
        if (requestId == Guid.Empty)
            throw new ArgumentException("RequestId must not be empty.", nameof(requestId));
        if (createdByUserId == Guid.Empty)
            throw new ArgumentException("CreatedByUserId must not be empty.", nameof(createdByUserId));
        // An explicitly supplied ticket default must be a real id or omitted — an empty guid is a
        // caller bug, never a silent "no default" (ADR-494 D2: never server-derived).
        if (defaultPerformedByAccountUserId == Guid.Empty)
            throw new ArgumentException(
                "DefaultPerformedByAccountUserId must not be an empty guid; omit it to create the draft with no default.",
                nameof(defaultPerformedByAccountUserId));

        return Result<ActualWork>.Success(new ActualWork
        {
            CreatedByUserId = createdByUserId,
            AccountId = accountId,
            RequestId = requestId,
            Status = ActualWorkStatus.Draft,
            RecorderAccountUserId = createdByUserId,
            DefaultPerformedByAccountUserId = defaultPerformedByAccountUserId,
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

    /// <summary>ADR-494 D2: sets or clears the Draft-level "Performed by" default. Allowed only while
    /// <see cref="Status"/> is <see cref="ActualWorkStatus.Draft"/>; passing null clears it. Never
    /// touches existing line performers — the default seeds new lines only. Recorder-ownership
    /// authorization is the API layer's responsibility (BL136 4c-i-b), mirroring
    /// <see cref="TransferRecorder"/> and <see cref="AddLine"/>; this method enforces only the
    /// Draft-only domain invariant.</summary>
    public Result SetDefaultPerformer(Guid? performedByAccountUserId)
    {
        if (performedByAccountUserId == Guid.Empty)
            throw new ArgumentException(
                "PerformedByAccountUserId must not be an empty guid; pass null to clear the default.",
                nameof(performedByAccountUserId));

        if (Status != ActualWorkStatus.Draft)
            return Result.Failure(ActualWorkErrors.NotDraft);

        DefaultPerformedByAccountUserId = performedByAccountUserId;
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
        Guid createdByUserId,
        Guid? performedByAccountUserId = null)
    {
        if (Status != ActualWorkStatus.Draft)
            return Result<ActualWorkLine>.Failure(ActualWorkErrors.NotDraft);

        // ADR-494 D2: an explicit performer wins; otherwise seed from the persisted ticket default.
        // With neither, Guid.Empty flows to ActualWorkLine.Create, which returns PerformerRequired —
        // the server never falls back to the creator or recorder.
        var performedBy = performedByAccountUserId ?? DefaultPerformedByAccountUserId ?? Guid.Empty;

        var createResult = ActualWorkLine.Create(
            AccountId, Id, catalogItemId, priceBookVersionLineId, displayNameSnapshot,
            unitOfMeasureSnapshot, actualQuantity, sellPriceSnapshot, standardExpectedDirectCostSnapshot,
            note, commercialBaselineSourceLineId, createdByUserId, performedBy);
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
    /// <param name="financialDataComplete">BL135 §4 Batch 3b-ii: every line on the visit has both an
    /// effective sell price and an effective direct cost (captured snapshot, or a financial resolution
    /// supplying that component). Computed by the review orchestration from the visit's lines and its
    /// account-scoped resolution rows — this method stays pure and loads nothing. Vacuously true for a
    /// zero-line visit.</param>
    /// <param name="zeroLineDispositionSatisfied">BL135 §4 Batch 3b-ii: the visit carries at least one
    /// <c>NoCharge</c> office financial disposition. Only consulted for a zero-line visit — a lined
    /// visit reaches billing eligibility through <paramref name="financialDataComplete"/>.</param>
    public Result MarkReviewed(
        Guid reviewedByAccountUserId, string? reviewNote, DateTime reviewedAtUtc,
        bool financialDataComplete, bool zeroLineDispositionSatisfied)
    {
        if (Status != ActualWorkStatus.Submitted)
            return Result.Failure(ActualWorkErrors.NotSubmitted);

        if (ReviewedAtUtc is not null)
            return Result.Failure(ActualWorkErrors.AlreadyReviewed);

        var trimmedNote = string.IsNullOrWhiteSpace(reviewNote) ? null : reviewNote.Trim();
        if (trimmedNote is not null && trimmedNote.Length > 2000)
            return Result.Failure(ActualWorkErrors.ReviewNoteTooLong);

        // BL135 §4 Batch 3b-ii — hard billing-readiness gate, ordered after the existing state/repeat/
        // note guards so previously-valid API failure modes are unchanged. Incomplete line financials
        // block first; the zero-line no-charge disposition requirement is the zero-line path only.
        if (!financialDataComplete)
            return Result.Failure(ActualWorkErrors.ReviewBlockedIncompleteFinancials);

        if (_lines.Count == 0 && !zeroLineDispositionSatisfied)
            return Result.Failure(ActualWorkErrors.ReviewBlockedZeroLineDispositionRequired);

        ReviewedAtUtc = reviewedAtUtc;
        ReviewedByAccountUserId = reviewedByAccountUserId;
        ReviewNote = trimmedNote;
        ConcurrencyVersion = Guid.NewGuid();
        return Result.Success();
    }

    /// <summary>
    /// Rotates <see cref="ConcurrencyVersion"/> after an immutable per-line financial resolution has
    /// been appended for this visit (BL135 §4 Batch 3a-ii). It exists solely to invalidate a stale
    /// financial-review command: the resolution row lives in a separate append-only entity, but it
    /// changes the effective financial state the Owner/Admin review card renders, so a review
    /// submitted against the pre-resolution version must be rejected as a conflict. This is the only
    /// sanctioned way for the financial-resolution persistence transaction to advance the visit
    /// token — it must not be used for any other purpose, and the transaction owns whether an
    /// append actually occurred before calling it.
    /// </summary>
    public void RefreshConcurrencyVersionForFinancialResolution()
    {
        ConcurrencyVersion = Guid.NewGuid();
    }

    /// <summary>
    /// Rotates <see cref="ConcurrencyVersion"/> after an immutable office financial disposition has
    /// been appended for this visit (BL135 §4 Batch 3b-i). Parallel to
    /// <see cref="RefreshConcurrencyVersionForFinancialResolution"/>: the disposition row lives in a
    /// separate append-only entity but changes the effective financial state the Owner/Admin review
    /// card renders (a zero-line visit becomes billing-eligible), so a review submitted against the
    /// pre-disposition version must be rejected as a conflict. This is the only sanctioned way for
    /// the disposition persistence transaction to advance the visit token — it must not be used for
    /// any other purpose, and the transaction owns whether an append actually occurred before
    /// calling it.
    /// </summary>
    public void RefreshConcurrencyVersionForOfficeFinancialDisposition()
    {
        ConcurrencyVersion = Guid.NewGuid();
    }
}
