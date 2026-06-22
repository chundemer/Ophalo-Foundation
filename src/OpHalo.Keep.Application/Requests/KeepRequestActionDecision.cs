using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Keep.Application.Requests;

/// <summary>
/// Structured action permission decision for one actor on one request (ADR-327).
/// Use <see cref="KeepRequestActionPolicy.DenyAll"/> for the canonical deny-all singleton.
/// AllowedStatuses contains enum-valued actual transitions and excludes the current status.
/// Same-status command semantics remain domain-authoritative and do not use this list as a gate.
/// Internal capabilities (SelfAssign, ClearResponsible, ManageWatchers) are advisory shared
/// policy vocabulary; they are not consumed as mutation execution gates and do not expand the
/// current AvailableActionsMetadata contract. Row-before-load and target-specific service/domain
/// checks remain authoritative for execution.
/// </summary>
public sealed record KeepRequestActionDecision(
    bool CanChangeStatus,
    bool CanSendBusinessUpdate,
    bool CanAddInternalNote,
    bool CanAcknowledgeAttention,
    bool CanLogExternalContact,
    bool CanAssignResponsible,
    bool CanSelfAssignResponsible,
    bool CanClearResponsible,
    bool CanManageWatchers,
    bool CanWatch,
    bool CanUnwatch,
    bool CanMute,
    bool CanUnmute,
    bool CanMarkFeedbackReviewed,
    IReadOnlyList<KeepRequestStatus> AllowedStatuses);
