namespace OpHalo.Foundation.Application.Abstractions.Storage;

/// <summary>
/// Constrains what a caller may store through <see cref="IBusinessDocumentStorage"/>. Storage
/// generates the opaque object key from this purpose and the caller's account id; callers never
/// construct or supply a key (ADR-471).
/// </summary>
public enum DocumentPurpose
{
    PriceBookImport,
}
