namespace OpHalo.Keep.Core.Domain;

/// <summary>
/// Formats a configured business phone for customer-facing display only.
///
/// When the stored value normalizes to a canonical 10-digit North American number
/// (ADR-444), it is rendered as <c>(XXX) XXX-XXXX</c>. Any other value — an
/// extension, a partial number, an international format — is returned trimmed but
/// otherwise untouched so a deliberately non-standard configured value still shows.
///
/// This is presentation only. Canonical phone storage and matching elsewhere keep
/// using <see cref="PhoneNormalizer"/> unchanged.
/// </summary>
public static class PhoneDisplayFormatter
{
    public static string Format(string raw)
    {
        var trimmed = raw.Trim();
        var canonical = PhoneNormalizer.Normalize(trimmed);
        return PhoneNormalizer.IsValidLength(canonical)
            ? $"({canonical[..3]}) {canonical[3..6]}-{canonical[6..]}"
            : trimmed;
    }
}
