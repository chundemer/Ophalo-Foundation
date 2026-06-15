using OpHalo.SharedKernel.Results;

namespace OpHalo.Foundation.Core.Entities.Accounts.Errors;

/// <summary>
/// Domain errors for AccountAuthCode lifecycle. Referenced by Application services
/// and the AccountAuthCode entity. Error codes use the ErrorHttpMapper suffix conventions.
/// </summary>
public static class AccountAuthCodeErrors
{
    public static readonly Error NotFound =
        Error.Create("AuthCode.NotFound", "Auth code not found.");

    public static readonly Error Expired =
        Error.Create("AuthCode.Expired", "This auth code has expired.");

    public static readonly Error AlreadyConsumed =
        Error.Create("AuthCode.AlreadyConsumed", "This auth code has already been used.");

    public static readonly Error CannotConsumeInvalidated =
        Error.Create("AuthCode.CannotConsumeInvalidated", "This auth code has been superseded and cannot be used.");

    public static readonly Error CannotInvalidateConsumed =
        Error.Create("AuthCode.CannotInvalidateConsumed", "This auth code has already been consumed and cannot be invalidated.");
}
