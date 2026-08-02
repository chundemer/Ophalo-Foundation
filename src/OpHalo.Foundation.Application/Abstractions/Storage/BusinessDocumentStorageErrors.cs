using OpHalo.SharedKernel.Results;

namespace OpHalo.Foundation.Application.Abstractions.Storage;

public static class BusinessDocumentStorageErrors
{
    public static readonly Error UploadFailed =
        Error.Create("BusinessDocumentStorage.UploadFailed", "The document could not be stored.");
}
