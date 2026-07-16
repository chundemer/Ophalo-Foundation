namespace OpHalo.Keep.Core.Domain;

/// <summary>
/// Phone normalizer for Keep customer identity (ADR-444: 10-digit North American).
///
/// Strips all non-ASCII-digit characters, then strips a leading '1' country code
/// from 11-digit inputs. The canonical form is exactly 10 digits.
/// International numbers outside this range are unsupported at launch.
/// </summary>
public static class PhoneNormalizer
{
    public static string Normalize(string raw)
    {
        var digits = new string(raw.Where(char.IsAsciiDigit).ToArray());
        return digits.Length == 11 && digits[0] == '1' ? digits[1..] : digits;
    }

    public static bool IsValidLength(string canonical) =>
        canonical.Length == 10;
}
