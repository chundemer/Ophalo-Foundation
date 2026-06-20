namespace OpHalo.Keep.Core.Domain;

/// <summary>
/// Conservative phone normalizer for Keep customer identity.
///
/// Strips all non-ASCII-digit characters from the submitted value to produce a
/// canonical digit-only string used for account-scoped uniqueness and lookup.
/// No country-code inference is performed — the canonical form deliberately
/// preserves leading zeros and reflects exactly the digits the customer provided.
/// Validation (7–15 digits) is enforced by KeepCustomer on creation and by the
/// application layer on intake validation (G2).
/// </summary>
public static class PhoneNormalizer
{
    public static string Normalize(string raw) =>
        new string(raw.Where(char.IsAsciiDigit).ToArray());

    public static bool IsValidLength(string canonical) =>
        canonical.Length is >= 7 and <= 15;
}
