using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Core.Errors;

public static class KeepRequestErrors
{
    public static readonly Error NotFound =
        Error.Create("KeepRequest.NotFound", "Request not found.");

    public static readonly Error Forbidden =
        Error.Create("KeepRequest.Forbidden", "You do not have permission to access this request.");

    public static readonly Error CustomerNameRequired =
        Error.Create("KeepRequest.CustomerNameRequired", "Customer name is required.");

    public static readonly Error CustomerPhoneRequired =
        Error.Create("KeepRequest.CustomerPhoneRequired", "Customer phone is required.");

    public static readonly Error DescriptionRequired =
        Error.Create("KeepRequest.DescriptionRequired", "Description is required.");
}
