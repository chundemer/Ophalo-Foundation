using OpHalo.Foundation.Application.Abstractions.Storage;

namespace OpHalo.Foundation.Infrastructure.Storage;

/// <summary>
/// Shared opaque-key generation and prefix validation for <see cref="IBusinessDocumentStorage"/>
/// implementations, so every provider generates and checks keys the same way. A key always has the
/// shape <c>{purpose}/{accountId:N}/{guid:N}</c>; <see cref="BelongsTo"/> lets a delete path refuse
/// to act on a key that was not generated for the given account/purpose, so a malformed or
/// unrelated key can never cause deletion of another object.
/// </summary>
internal static class BusinessDocumentObjectKey
{
    public static string Generate(Guid accountId, DocumentPurpose purpose) =>
        $"{Prefix(accountId, purpose)}{Guid.NewGuid():N}";

    public static bool BelongsTo(Guid accountId, DocumentPurpose purpose, string objectKey)
    {
        if (string.IsNullOrEmpty(objectKey))
            return false;

        var prefix = Prefix(accountId, purpose);
        if (!objectKey.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        var remainder = objectKey[prefix.Length..];
        return remainder.Length > 0 && !remainder.Contains('/') && !remainder.Contains('\\');
    }

    private static string Prefix(Guid accountId, DocumentPurpose purpose) =>
        $"{PurposeSegment(purpose)}/{accountId:N}/";

    private static string PurposeSegment(DocumentPurpose purpose) => purpose switch
    {
        DocumentPurpose.PriceBookImport => "price-book-import",
        _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "Unknown document purpose."),
    };
}
