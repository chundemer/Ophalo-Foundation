namespace OpHalo.Keep.Core.Entities.Enums;

/// <summary>
/// How the office financially disposes of a zero-line Actual Work visit (ADR-493 / build-log/129,
/// build-log/135 §4 Batch 1). <see cref="NoCharge"/> is the only kind this phase (build-log/135
/// §6.2); it is a real enum so a later kind is additive and exhaustively switched.
/// </summary>
public enum OfficeFinancialDispositionKind
{
    NoCharge,
}
