using OpHalo.Foundation.Core.Entities.Shared;

namespace OpHalo.Keep.Core.Entities;

/// <summary>
/// A customer known to a specific account in Keep. Identity is the composite
/// (AccountId, PrimaryPhone) — the same person calling from the same number
/// always resolves to the same KeepCustomer row.
/// </summary>
public sealed class KeepCustomer : BaseEntity
{
    public Guid AccountId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string PrimaryPhone { get; private set; } = string.Empty;
    public string? Email { get; private set; }

    public void UpdateContactInfo(string name, string? email)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Customer name is required.", nameof(name));

        Name = name.Trim();
        Email = email?.Trim();
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

        return new KeepCustomer
        {
            AccountId = accountId,
            Name = name.Trim(),
            PrimaryPhone = primaryPhone.Trim(),
            Email = email?.Trim()
        };
    }
}
