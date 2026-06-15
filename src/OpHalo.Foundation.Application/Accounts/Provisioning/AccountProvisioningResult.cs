using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Users;

namespace OpHalo.Foundation.Application.Accounts.Provisioning;

/// <summary>
/// The canonical object graph produced by <see cref="AccountProvisioningService"/> for a new
/// verified account: the owner <see cref="Users.User"/>, the <see cref="Accounts.Account"/>,
/// the owner <see cref="AccountUser"/> membership, and the <see cref="AccountEntitlements"/>.
/// </summary>
/// <remarks>
/// A pure carrier — how this graph is saved transactionally (the save contract, unique
/// constraints, aggregate boundary) is a persistence-phase concern, not modeled here.
/// </remarks>
public sealed record AccountProvisioningResult(
    User User,
    Account Account,
    AccountUser Owner,
    AccountEntitlements Entitlements);
