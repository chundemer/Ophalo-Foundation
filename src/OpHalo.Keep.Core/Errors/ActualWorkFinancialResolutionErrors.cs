using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Core.Errors;

/// <summary>
/// Domain validation errors for <see cref="OpHalo.Keep.Core.Entities.ActualWorkLineFinancialResolution"/>
/// and <see cref="OpHalo.Keep.Core.Entities.ActualWorkOfficeFinancialDisposition"/> (ADR-493 /
/// build-log/129, build-log/135 §4 Batch 1). Snapshot/review-state rules are enforced later against
/// the loaded visit (Batches 3a-ii / 3b-i), not here.
/// </summary>
public static class ActualWorkFinancialResolutionErrors
{
    public static readonly Error FinancialResolutionValueRequired =
        Error.Create(
            "ActualWork.FinancialResolutionValueRequired",
            "At least one resolved value (unit sell price or unit direct cost) is required.");

    public static readonly Error FinancialResolutionValueNegative =
        Error.Create(
            "ActualWork.FinancialResolutionValueNegative",
            "A resolved value must not be negative.");

    public static readonly Error FinancialResolutionInvalidBasis =
        Error.Create(
            "ActualWork.FinancialResolutionInvalidBasis",
            "The financial resolution basis is not valid.");

    public static readonly Error FinancialResolutionReasonRequired =
        Error.Create(
            "ActualWork.FinancialResolutionReasonRequired",
            "A reason is required for a financial resolution.");

    public static readonly Error FinancialResolutionReasonTooLong =
        Error.Create(
            "ActualWork.FinancialResolutionReasonTooLong",
            "Reason must not exceed 2000 characters.");

    /// <summary>Batch 3a-ii: the targeted line id does not belong to the loaded visit (wrong visit,
    /// deleted, or unknown). Maps to 404.</summary>
    public static readonly Error FinancialResolutionLineNotFound =
        Error.Create(
            "ActualWork.FinancialResolutionLineNotFound",
            "That line is not part of this visit.");

    /// <summary>Batch 3a-ii: a resolution may only supply a component whose captured snapshot is
    /// missing. The targeted component already has a non-null snapshot on the line. Maps to 409.</summary>
    public static readonly Error FinancialResolutionSnapshotComponentAlreadyValid =
        Error.Create(
            "ActualWork.FinancialResolutionSnapshotComponentAlreadyValid",
            "That line component already has a captured value and cannot be resolved.");

    /// <summary>Batch 3a-ii (drift D5): the visit has been financially reviewed
    /// (<c>ReviewedAtUtc != null</c>); a resolution would mutate effective financial facts after the
    /// Owner/Admin's approval. Post-review changes are the deferred correction path. Maps to 409.</summary>
    public static readonly Error FinancialResolutionVisitAlreadyReviewed =
        Error.Create(
            "ActualWork.FinancialResolutionVisitAlreadyReviewed",
            "This visit has already been reviewed and can no longer be resolved.");

    public static readonly Error DispositionInvalidKind =
        Error.Create(
            "ActualWork.DispositionInvalidKind",
            "The office financial disposition kind is not valid.");

    public static readonly Error DispositionReasonRequired =
        Error.Create(
            "ActualWork.DispositionReasonRequired",
            "A reason is required for an office financial disposition.");

    public static readonly Error DispositionReasonTooLong =
        Error.Create(
            "ActualWork.DispositionReasonTooLong",
            "Reason must not exceed 2000 characters.");
}
