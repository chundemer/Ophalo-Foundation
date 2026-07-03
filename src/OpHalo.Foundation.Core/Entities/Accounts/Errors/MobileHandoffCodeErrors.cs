using OpHalo.SharedKernel.Results;

namespace OpHalo.Foundation.Core.Entities.Accounts.Errors;

public static class MobileHandoffCodeErrors
{
    public static readonly Error InvalidToken =
        Error.Create("MobileHandoff.InvalidToken", "The mobile handoff code is invalid or expired.");
}
