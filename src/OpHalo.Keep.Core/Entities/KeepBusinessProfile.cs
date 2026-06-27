using OpHalo.Foundation.Core.Entities.Shared;

namespace OpHalo.Keep.Core.Entities;

public sealed class KeepBusinessProfile : BaseEntity
{
    public Guid AccountId { get; private set; }
    public string? CustomerFacingPhone { get; private set; }
    public string? CustomerFacingEmail { get; private set; }

    public static KeepBusinessProfile Create(Guid accountId)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("Account ID is required.", nameof(accountId));

        return new KeepBusinessProfile { AccountId = accountId };
    }

    public void UpdateContact(string? customerFacingPhone, string? customerFacingEmail)
    {
        CustomerFacingPhone = string.IsNullOrWhiteSpace(customerFacingPhone) ? null : customerFacingPhone.Trim();
        CustomerFacingEmail = string.IsNullOrWhiteSpace(customerFacingEmail) ? null : customerFacingEmail.Trim();
    }
}
