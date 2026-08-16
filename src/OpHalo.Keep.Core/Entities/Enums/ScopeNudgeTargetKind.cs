namespace OpHalo.Keep.Core.Entities.Enums;

/// <summary>Which polymorphic target a <c>ScopeNudgeSuggestionFieldRow</c> carries — the field
/// nudge-read response's price-free shape has no domain object to infer this from, so it is stated
/// explicitly (build-log/123).</summary>
public enum ScopeNudgeTargetKind
{
    CatalogItem = 1,
    OfferingAssembly = 2,
}
