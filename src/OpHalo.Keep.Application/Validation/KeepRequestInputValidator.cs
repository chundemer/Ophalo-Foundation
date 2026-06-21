using System.ComponentModel.DataAnnotations;
using OpHalo.Keep.Core.Domain;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.Validation;

public sealed record ValidatedKeepRequestInput(
    string TrimmedName,
    string TrimmedPhone,
    string CanonicalPhone,
    string? TrimmedEmail,
    string TrimmedDescription);

/// <summary>
/// Shared validation pipeline for request-creation input — used by both public and
/// authenticated business intake so the rules cannot drift between the two paths.
/// </summary>
public static class KeepRequestInputValidator
{
    private static readonly EmailAddressAttribute EmailValidator = new();

    public static Result<ValidatedKeepRequestInput> Validate(
        string? customerName,
        string? customerPhone,
        string? customerEmail,
        string? description)
    {
        // Required
        if (string.IsNullOrWhiteSpace(customerName))
            return Result<ValidatedKeepRequestInput>.Failure(KeepRequestErrors.CustomerNameRequired);
        if (string.IsNullOrWhiteSpace(customerPhone))
            return Result<ValidatedKeepRequestInput>.Failure(KeepRequestErrors.CustomerPhoneRequired);
        if (string.IsNullOrWhiteSpace(description))
            return Result<ValidatedKeepRequestInput>.Failure(KeepRequestErrors.DescriptionRequired);

        var trimmedName        = customerName.Trim();
        var trimmedPhone       = customerPhone.Trim();
        var trimmedEmail       = string.IsNullOrWhiteSpace(customerEmail) ? null : customerEmail.Trim();
        var trimmedDescription = description.Trim();

        // Max lengths
        if (trimmedName.Length > 200)
            return Result<ValidatedKeepRequestInput>.Failure(KeepRequestErrors.CustomerNameTooLong);
        if (trimmedPhone.Length > 50)
            return Result<ValidatedKeepRequestInput>.Failure(KeepRequestErrors.CustomerPhoneTooLong);
        if (trimmedEmail is not null && trimmedEmail.Length > 320)
            return Result<ValidatedKeepRequestInput>.Failure(KeepRequestErrors.CustomerEmailTooLong);
        if (trimmedDescription.Length > 4000)
            return Result<ValidatedKeepRequestInput>.Failure(KeepRequestErrors.DescriptionTooLong);

        // Phone character allowlist (+ only as leading international prefix)
        if (!HasValidPhoneCharacters(trimmedPhone))
            return Result<ValidatedKeepRequestInput>.Failure(KeepRequestErrors.CustomerPhoneInvalidCharacters);

        var canonicalPhone = PhoneNormalizer.Normalize(trimmedPhone);
        if (!PhoneNormalizer.IsValidLength(canonicalPhone))
            return Result<ValidatedKeepRequestInput>.Failure(KeepRequestErrors.CustomerPhoneInvalidFormat);

        // Email syntax
        if (trimmedEmail is not null && !EmailValidator.IsValid(trimmedEmail))
            return Result<ValidatedKeepRequestInput>.Failure(KeepRequestErrors.CustomerEmailInvalid);

        return Result<ValidatedKeepRequestInput>.Success(
            new ValidatedKeepRequestInput(trimmedName, trimmedPhone, canonicalPhone, trimmedEmail, trimmedDescription));
    }

    private static bool HasValidPhoneCharacters(string trimmedPhone)
    {
        for (var i = 0; i < trimmedPhone.Length; i++)
        {
            var c = trimmedPhone[i];
            if (char.IsAsciiDigit(c) || c is ' ' or '-' or '(' or ')' or '.')
                continue;
            if (c == '+' && i == 0)
                continue;
            return false;
        }
        return true;
    }
}
