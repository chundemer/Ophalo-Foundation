using OpHalo.SharedKernel.Results;

namespace OpHalo.Foundation.Core.Entities.Users.Errors;

/// <summary>
/// Domain errors for User lifecycle. Referenced by Application services and the User entity.
/// Error codes use the ErrorHttpMapper suffix conventions.
/// </summary>
public static class UserErrors
{
    public static readonly Error NameAlreadySet =
        Error.Create("User.NameAlreadySet", "This user already has a name and cannot be renamed this way.");

    public static readonly Error NameRequired =
        Error.Create("User.NameRequired", "A name is required to complete sign-in.");
}
