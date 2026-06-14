namespace OpHalo.Foundation.Core.Entities.Accounts.Enums;

/// <summary>
/// Operational role of an account member within a business account (ADR-015).
/// Ownership is modeled separately via <c>Account.PrimaryOwnerAccountUserId</c> (ADR-019),
/// not as a flag on the member.
/// </summary>
/// <remarks>
/// Replaces the legacy Admin/Technician/Owner/Member set. Legacy mapping:
/// Owner→Owner, Admin→Admin, Technician→Operator, Member→Viewer (a later data migration).
/// Numeric values are Foundation-local and intentionally fresh.
/// </remarks>
public enum AccountUserRole
{
    /// <summary>Account owner — full access. Assigned only via <c>AccountUser.CreateOwner</c>.</summary>
    Owner = 1,

    /// <summary>Full operational access for trusted office/admin staff.</summary>
    Admin = 2,

    /// <summary>Standard field or limited operational user.</summary>
    Operator = 3,

    /// <summary>Read-mostly member — the default for invited members.</summary>
    Viewer = 4
}
