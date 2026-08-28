namespace OpHalo.Keep.Core.Entities.Enums;

/// <summary>
/// Why an <see cref="OpHalo.Keep.Core.Entities.ActualWorkLineFinancialResolution"/> supplies a
/// missing per-line financial component (ADR-493 / build-log/129, build-log/135 §4 Batch 1). An
/// office actor records the basis alongside the resolved value and a required reason.
/// </summary>
public enum FinancialResolutionBasis
{
    SupplierReceipt,
    OwnerSetPrice,
    FixedAgreement,
    Other,
}
