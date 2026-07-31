namespace OpHalo.Keep.Core.Entities.Enums;

/// <summary>
/// Lifecycle state of a <see cref="OpHalo.Keep.Core.Entities.CatalogItem"/> (build-log/108):
/// <c>Draft -&gt; Active -&gt; Inactive</c>, with <c>Active</c>/<c>Inactive</c> reversible by
/// Owner/Admin. <c>Draft</c> is also the state used for an off-catalog promotion pending normal
/// review/publish (ADR-459) — not modeled by this session, but the value is reserved for it.
/// </summary>
public enum CatalogItemActiveState
{
    Draft,
    Active,
    Inactive,
}
