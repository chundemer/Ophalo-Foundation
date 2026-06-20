using OpHalo.Foundation.Core.Entities.Shared;
using OpHalo.Keep.Core.Domain;

namespace OpHalo.Keep.Core.Entities;

/// <summary>
/// A customer known to a specific account in Keep. Identity is the composite
/// (AccountId, CanonicalPhone) — the canonical digit-only form of the phone
/// number ensures that formatted variants of the same number resolve to the
/// same row. PrimaryPhone stores the submitted display form.
/// </summary>
public sealed class KeepCustomer : BaseEntity
{
    public Guid AccountId { get; private set; }
    public string Name { get; private set; } = string.Empty;

    /// <summary>Submitted/display phone as provided by the customer.</summary>
    public string PrimaryPhone { get; private set; } = string.Empty;

    /// <summary>Digit-only identity form derived from PrimaryPhone at creation. Immutable.</summary>
    public string CanonicalPhone { get; private set; } = string.Empty;

    public string? Email { get; private set; }

    public void UpdateContactInfo(string name, string? email)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Customer name is required.", nameof(name));

        Name = name.Trim();
        // Anonymous omission is never a clear-email command: preserve existing email when the
        // new submission omits or blanks it; replace only when a nonblank value is supplied.
        if (!string.IsNullOrWhiteSpace(email))
            Email = email.Trim();
    }

    public static KeepCustomer Create(
        Guid accountId,
        string name,
        string primaryPhone,
        string? email = null)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("Account ID is required.", nameof(accountId));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Customer name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(primaryPhone))
            throw new ArgumentException("Primary phone is required.", nameof(primaryPhone));

        var canonical = PhoneNormalizer.Normalize(primaryPhone.Trim());
        if (!PhoneNormalizer.IsValidLength(canonical))
            throw new ArgumentException(
                $"Phone must contain 7–15 digits after normalization; got {canonical.Length}.",
                nameof(primaryPhone));

        return new KeepCustomer
        {
            AccountId = accountId,
            Name = name.Trim(),
            PrimaryPhone = primaryPhone.Trim(),
            CanonicalPhone = canonical,
            Email = email?.Trim()
        };
    }
}
