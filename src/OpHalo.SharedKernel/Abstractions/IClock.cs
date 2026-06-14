namespace OpHalo.SharedKernel.Abstractions;

/// <summary>
/// Canonical time abstraction. The single source of "now" across all layers and
/// products. Time is a generic, non-business cross-cutting concern, so it lives in
/// the SharedKernel; Foundation and Keep both depend on this one definition.
/// </summary>
/// <remarks>
/// Collapsed in Phase 3 from the duplicate definitions in the reference repo
/// (OpHalo.Shared.Abstractions.IClock and OpHalo.Application.Abstractions.Infrastructure.IClock).
/// </remarks>
public interface IClock
{
    DateTime UtcNow { get; }
}
