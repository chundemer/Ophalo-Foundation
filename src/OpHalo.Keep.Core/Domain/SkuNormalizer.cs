namespace OpHalo.Keep.Core.Domain;

/// <summary>
/// Canonical SKU normalizer for <see cref="OpHalo.Keep.Core.Entities.CatalogItem.ExternalKey"/>
/// account-wide uniqueness/search (build-log/112): <c>cop34</c>, <c>COP-34</c>, and <c>cop 34</c>
/// must resolve to the same canonical term. ASCII-only by design, matching the migration backfill's
/// SQL character class, so runtime and backfill can never diverge on Unicode input.
/// </summary>
public static class SkuNormalizer
{
    public static string Normalize(string raw) =>
        new(raw.Where(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9')
            .Select(char.ToLowerInvariant)
            .ToArray());
}
