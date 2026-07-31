namespace OpHalo.Keep.Core.Entities.Enums;

/// <summary>
/// Lifecycle state shared by price-book configuration children that have no meaningful draft
/// state — <see cref="OpHalo.Keep.Core.Entities.CatalogCategory"/> and
/// <see cref="OpHalo.Keep.Core.Entities.CatalogItemAlias"/> (build-log/108, build-log/110). Unlike
/// <see cref="CatalogItemActiveState"/>, there is no <c>Draft</c> value: a category or alias is
/// either in use or retired.
/// </summary>
public enum CatalogActiveState
{
    Active,
    Inactive,
}
