using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;

namespace OpHalo.Keep.Application.PriceBook;

/// <summary>
/// ADR-494 D2: the single performer-eligibility predicate shared by the performer-candidate read
/// (<see cref="GetActualWorkPerformerCandidatesService"/>) and the server-side revalidation of a
/// caller-supplied performer id in <see cref="ActualWorkDraftApiService"/> (ticket default at
/// create / <c>SetDefaultPerformer</c>, and an explicit per-line performer).
///
/// A performer must be an <b>active</b> account member who holds both <c>RequestsOperate</c> and
/// <c>ActualWorkCapture</c> under the account's purpose — exactly the recorder predicate, minus the
/// Owner/Admin restriction (an Operator office transcriber records work on a technician's behalf).
///
/// This is a point-in-time check applied only when a performer is <i>selected</i>. An inactive
/// former user stays valid on an already-recorded line and on an already-selected ticket default
/// (frozen at selection — Christian, 2026-08-29); those paths never call this.
/// </summary>
public static class ActualWorkPerformerEligibility
{
    public static bool IsEligible(
        IUserAccessPolicy userAccessPolicy,
        AccountUserRole role,
        MembershipStatus membershipStatus,
        AccountPurpose accountPurpose) =>
        userAccessPolicy.IsPermitted(
            role, membershipStatus, accountPurpose, PermissionKeys.Keep.RequestsOperate) &&
        userAccessPolicy.IsPermitted(
            role, membershipStatus, accountPurpose, PermissionKeys.Keep.ActualWorkCapture);
}
