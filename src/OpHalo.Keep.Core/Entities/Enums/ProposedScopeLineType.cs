namespace OpHalo.Keep.Core.Entities.Enums;

/// <summary>
/// How a <see cref="OpHalo.Keep.Core.Entities.ProposedScopeLine"/> was selected (ADR-461's fixed
/// escape ladder, build-log/108). <c>PrimaryOffering</c> and <c>AssociatedItem</c> originate from
/// selecting an <see cref="OpHalo.Keep.Core.Entities.OfferingAssembly"/> — the primary item itself,
/// and its default associated items respectively — and carry <c>OfferingAssemblyId</c>.
/// <c>KnownCatalogItem</c> is a direct pick (Common Items, Categories, or Search rungs), no
/// assembly. <c>OffCatalogItem</c> is the always-available escape hatch — no <c>CatalogItemId</c>,
/// requires a description and quantity instead.
/// </summary>
public enum ProposedScopeLineType
{
    PrimaryOffering,
    AssociatedItem,
    KnownCatalogItem,
    OffCatalogItem,
}
