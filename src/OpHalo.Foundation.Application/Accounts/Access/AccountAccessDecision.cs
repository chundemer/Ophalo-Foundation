using OpHalo.SharedKernel.Results;

namespace OpHalo.Foundation.Application.Accounts.Access;

public sealed record AccountAccessDecision(
    AccountAccessPosture Posture,
    AccountAccessReason Reason,
    Error? BlockingError)
{
    // Posture is authoritative. BlockingError is the transport artifact attached to a Blocked or ReadOnly denial.
    public bool IsBlocked => Posture == AccountAccessPosture.Blocked;
    public bool IsReadOnly => Posture == AccountAccessPosture.ReadOnly;
}
