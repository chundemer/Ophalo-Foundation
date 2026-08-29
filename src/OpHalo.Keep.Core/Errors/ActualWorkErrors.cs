using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Core.Errors;

public static class ActualWorkErrors
{
    public static readonly Error NotFound =
        Error.Create("ActualWork.NotFound", "Actual work visit not found.");

    public static readonly Error NotDraft =
        Error.Create("ActualWork.NotDraft", "This actual work visit can no longer be edited.");

    public static readonly Error VersionMismatch =
        Error.Create("ActualWork.VersionMismatch", "This actual work visit was changed by someone else. Reload and try again.");

    public static readonly Error DraftAlreadyOpenForRequest =
        Error.Create("ActualWork.DraftAlreadyOpenForRequest", "This request already has an open draft visit.");

    public static readonly Error ExpectedVersionRequired =
        Error.Create("ActualWork.ExpectedVersionRequired", "An expected actual work version is required.");

    public static readonly Error ExpectedVersionInvalid =
        Error.Create("ActualWork.ExpectedVersionInvalid", "The expected actual work version is not a valid version value.");

    public static readonly Error LineNotFound =
        Error.Create("ActualWork.LineNotFound", "Actual work line not found.");

    public static readonly Error LineQuantityMustBePositive =
        Error.Create("ActualWork.LineQuantityMustBePositive", "Quantity must be greater than zero.");

    public static readonly Error LineDisplayNameSnapshotRequired =
        Error.Create("ActualWork.LineDisplayNameSnapshotRequired", "A description is required.");

    /// <summary>An empty guid is never a valid optional id — a caller must pass null instead of
    /// <see cref="Guid.Empty"/> to mean "no catalog item"; silently normalizing it could turn
    /// malformed input into an unintended custom line.</summary>
    public static readonly Error LineCatalogItemIdEmpty =
        Error.Create("ActualWork.LineCatalogItemIdEmpty", "Catalog item id must not be an empty guid.");

    /// <summary>Same rule as <see cref="LineCatalogItemIdEmpty"/> for the price book version-line id.</summary>
    public static readonly Error LinePriceBookVersionLineIdEmpty =
        Error.Create("ActualWork.LinePriceBookVersionLineIdEmpty", "Price book version line id must not be an empty guid.");

    /// <summary>A field-supplied CatalogItemId that does not resolve to an account-owned catalog
    /// item. Unlike ProposedScope's field-select, ActiveState is not checked here — an inactive item
    /// is still a valid "catalog-backed without a snapshot" line (build-log/129's three-state
    /// design), not a rejected reference.</summary>
    public static readonly Error LineCatalogItemNotFound =
        Error.Create("ActualWork.LineCatalogItemNotFound", "Catalog item not found.");

    /// <summary>A line must be either catalog-backed (CatalogItemId) or custom
    /// (OffCatalogDescription), never both — mirrors the "never trust a caller snapshot" discipline
    /// of ProposedScope's field-select.</summary>
    public static readonly Error LineOffCatalogDescriptionWithCatalogItem =
        Error.Create("ActualWork.LineOffCatalogDescriptionWithCatalogItem", "A catalog-backed line must not also supply a custom description.");

    /// <summary>A Price Book version-line snapshot always resolves to a catalog item; a custom/
    /// off-catalog line cannot carry one.</summary>
    public static readonly Error LinePriceBookVersionLineRequiresCatalogItem =
        Error.Create("ActualWork.LinePriceBookVersionLineRequiresCatalogItem", "A price book snapshot requires a catalog item.");

    /// <summary>Sell price/direct cost values are only meaningful alongside the Price Book
    /// version-line identity they were captured from — never invented independently.</summary>
    public static readonly Error LineSnapshotValuesRequirePriceBookVersionLine =
        Error.Create("ActualWork.LineSnapshotValuesRequirePriceBookVersionLine", "Sell price and direct cost require a price book snapshot.");

    /// <summary>ADR-494 D1/D2: every line carries a non-null performer. A line is created with neither
    /// an explicit performer nor a persisted ticket-level default to seed from — the recording user
    /// must pick a technician first; the server never substitutes the creator or current recorder.</summary>
    public static readonly Error PerformerRequired =
        Error.Create("ActualWork.PerformerRequired", "Select who performed this work before adding lines.");

    /// <summary>ADR-494 D2 (4c-i-b): a caller-supplied performer id — the ticket default at create /
    /// <c>SetDefaultPerformer</c>, or an explicit per-line performer — is not an active, account-scoped
    /// staff member holding <c>RequestsOperate</c> + <c>ActualWorkCapture</c>. Non-member, cross-account,
    /// inactive, empty guid, and permission-ineligible all collapse to this one 422 so the endpoint can
    /// never enumerate account membership. An <b>inherited</b> ticket default is frozen at selection and
    /// is never revalidated here.</summary>
    public static readonly Error PerformerIneligible =
        Error.Create("ActualWork.PerformerIneligible", "That team member can't be recorded as the performer.");

    /// <summary>Build-log/129: a zero-line submit requires a non-whitespace completion note.</summary>
    public static readonly Error ZeroLineCompletionNoteRequired =
        Error.Create("ActualWork.ZeroLineCompletionNoteRequired", "A completion note is required to submit a visit with no lines.");

    /// <summary>Build-log/129: a zero-line submit requires one of the fixed truthful outcomes.</summary>
    public static readonly Error ZeroLineOutcomeRequired =
        Error.Create("ActualWork.ZeroLineOutcomeRequired", "A visit outcome is required to submit a visit with no lines.");

    /// <summary>A supplied outcome must be one of the fixed <c>ActualWorkOutcome</c> values, whether
    /// or not the visit has lines — an undefined enum value is never persisted.</summary>
    public static readonly Error InvalidOutcome =
        Error.Create("ActualWork.InvalidOutcome", "The visit outcome is not a valid value.");

    /// <summary>Build-log/129's 5d-i preflight lock: the ADR-479 operational-eligibility predicate,
    /// recomputed from the row-locked assembly/catalog-item state at expand time, failed.</summary>
    public static readonly Error ExpandAssemblyNotOperationallyEligible =
        Error.Create("ActualWork.ExpandAssemblyNotOperationallyEligible", "This assembly is no longer eligible to expand.");

    /// <summary>One or more submitted optional-item inclusion ids do not name a current optional
    /// associated item on the assembly (unknown id, or a required item's id).</summary>
    public static readonly Error ExpandInclusionItemInvalid =
        Error.Create("ActualWork.ExpandInclusionItemInvalid", "One or more selected optional items are not valid for this assembly.");

    /// <summary>GAP-055: an Owner/Admin recorder transfer must always state why.</summary>
    public static readonly Error RecorderTransferReasonRequired =
        Error.Create("ActualWork.RecorderTransferReasonRequired", "A reason is required to transfer the recorder.");

    /// <summary>GAP-055: the transfer target must be a specific account member, never an empty guid.</summary>
    public static readonly Error RecorderTransferTargetRequired =
        Error.Create("ActualWork.RecorderTransferTargetRequired", "A new recorder is required to transfer the recorder.");

    /// <summary>GAP-055 (option C): the transfer target is not an account member, or does not hold
    /// <c>RequestsOperate</c> + <c>ActualWorkCapture</c> — only a qualified recorder may hold a
    /// Draft. Non-member and unqualified collapse to one error so the endpoint cannot be used to
    /// enumerate account membership.</summary>
    public static readonly Error RecorderTransferTargetIneligible =
        Error.Create("ActualWork.RecorderTransferTargetIneligible", "That team member can't be assigned as the recorder.");

    /// <summary>Batch 6: only a Submitted visit may be marked reviewed — it is still Draft.</summary>
    public static readonly Error NotSubmitted =
        Error.Create("ActualWork.NotSubmitted", "This actual work visit has not been submitted yet.");

    /// <summary>Batch 6: single-shot review — a visit that already has a reviewer/timestamp cannot
    /// be reviewed again.</summary>
    public static readonly Error AlreadyReviewed =
        Error.Create("ActualWork.AlreadyReviewed", "This actual work visit was already reviewed.");

    /// <summary>Batch 6: matches the feedback-review note convention (max 2,000 characters).</summary>
    public static readonly Error ReviewNoteTooLong =
        Error.Create("ActualWork.ReviewNoteTooLong", "The review note must be 2,000 characters or fewer.");

    /// <summary>BL135 §4 Batch 3b-ii: the visit cannot be marked reviewed because at least one line
    /// still lacks an effective sell price or direct cost (no captured snapshot and no financial
    /// resolution supplying it). Maps to 409.</summary>
    public static readonly Error ReviewBlockedIncompleteFinancials =
        Error.Create(
            "ActualWork.ReviewBlockedIncompleteFinancials",
            "This visit still has line items with incomplete financial data and cannot be reviewed.");

    /// <summary>BL135 §4 Batch 3b-ii: a zero-line visit cannot be marked reviewed until it carries a
    /// <c>NoCharge</c> office financial disposition recording why no work is billed. Maps to 409.</summary>
    public static readonly Error ReviewBlockedZeroLineDispositionRequired =
        Error.Create(
            "ActualWork.ReviewBlockedZeroLineDispositionRequired",
            "This visit has no line items and needs a no-charge disposition before it can be reviewed.");
}
