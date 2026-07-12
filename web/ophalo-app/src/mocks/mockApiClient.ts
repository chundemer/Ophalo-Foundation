import { api } from "../lib/apiClient";
import type {
  AccountRole,
  AvailableActionsMetadata,
  KeepRequestDetailResult,
  KeepRequestEventItem,
  KeepRequestSummary,
} from "../lib/apiClient";
import {
  MOCK_USER_ID,
  MOCK_VALIDATION,
  OWNER_ACTIONS,
  mockIntake,
  mockMembers,
  mockMeByRole,
  mockOnboarding,
  mockSetup,
  mockViewCounts,
} from "./fixtures";
import {
  addMockRequest,
  currentMockRole,
  getMockDetail,
  getMockRequests,
  updateMockDetail,
} from "./mockState";

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function delay<T>(val: T, ms = 120): Promise<T> {
  return new Promise((resolve) => setTimeout(() => resolve(val), ms));
}

let mockEventCounter = 1000;
function nextEventId(): string {
  return `mock-event-gen-${++mockEventCounter}`;
}

let mockRequestCounter = 100;
function nextRequestId(): string {
  return `mock-req-gen-${++mockRequestCounter}`;
}

function actionsForRole(base: AvailableActionsMetadata, role: AccountRole): AvailableActionsMetadata {
  if (role === "viewer") {
    return {
      canChangeStatus: false,
      canSendBusinessUpdate: false,
      canAddInternalNote: false,
      canAcknowledgeAttention: false,
      canLogExternalContact: false,
      canAssignResponsible: false,
      canWatch: false,
      canUnwatch: false,
      canMute: false,
      canUnmute: false,
      canMarkFeedbackReviewed: false,
      canSetFollowUpOn: false,
      canSetPlannedFor: false,
      canClose: false,
      canClassify: false,
      canRecordShareIntent: false,
      allowedStatuses: [],
    };
  }
  if (role === "operator") {
    return {
      ...base,
      canAssignResponsible: false,
      canSetPlannedFor: false,
      canClose: false,
      canClassify: false,
      canMarkFeedbackReviewed: false,
      allowedStatuses: base.allowedStatuses.filter((s) => s !== "cancelled"),
    };
  }
  return base;
}

function withRoleActions(d: KeepRequestDetailResult): KeepRequestDetailResult {
  return { ...d, availableActions: actionsForRole(d.availableActions, currentMockRole) };
}

function appendEvent(
  d: KeepRequestDetailResult,
  event: Partial<KeepRequestEventItem> & { eventType: string; occurredAtUtc: string },
): KeepRequestDetailResult {
  const full: KeepRequestEventItem = {
    id: nextEventId(),
    eventType: event.eventType,
    content: event.content ?? null,
    visibility: event.visibility ?? "internal",
    occurredAtUtc: event.occurredAtUtc,
    actorType: "AccountUser",
    actorAccountUserId: MOCK_USER_ID,
    actorDisplayName: "Jamie Reyes",
    statusAfter: event.statusAfter ?? null,
    messageIntent: event.messageIntent ?? null,
    communicationChannel: null,
    externalContactDirection: event.externalContactDirection ?? null,
    externalContactChannel: event.externalContactChannel ?? null,
    externalContactOutcome: event.externalContactOutcome ?? null,
    externalContactRequiresFollowUp: event.externalContactRequiresFollowUp ?? null,
    externalContactSetFirstResponse: event.externalContactSetFirstResponse ?? null,
    externalContactClearedAttention: event.externalContactClearedAttention ?? null,
    participationAction: null,
    participationTargetAccountUserId: null,
    participationTargetDisplayName: null,
    participationPreviousResponsibleAccountUserId: null,
    participationInternalNote: null,
    plannedForDate: event.plannedForDate ?? null,
    followUpOnDate: event.followUpOnDate ?? null,
    followUpOnReason: event.followUpOnReason ?? null,
  };
  return {
    ...d,
    events: [...d.events, full],
    version: crypto.randomUUID(),
    lastBusinessActivityAt: full.occurredAtUtc,
  };
}

// ---------------------------------------------------------------------------
// Install
// ---------------------------------------------------------------------------

export function installMockApi(): void {
  // Identity
  api.getMe = () => delay({ ...mockMeByRole[currentMockRole] });

  // Onboarding / setup
  api.getOnboardingChecklist = () => delay({ ...mockOnboarding });
  api.markQuickCaptureExercise = () => delay(undefined as void);
  api.markTrackerReview = () => delay(undefined as void);
  api.markSpamClassification = () => delay(undefined as void);

  // Settings
  api.getSetup = () => delay({ ...mockSetup });
  api.updateProfile = (body) =>
    delay({ ...mockSetup, ...body });
  api.updatePolicy = (body) =>
    delay({ ...mockSetup, responsePolicy: { ...mockSetup.responsePolicy, ...body } });

  // Members
  api.listMembers = () => delay({ ...mockMembers, members: [...mockMembers.members] });
  api.inviteMember = () => delay({ status: "invited" });
  api.resendInvite = () => delay(null);
  api.changeRole = () => delay(undefined as void);
  api.suspendMember = () => delay(undefined as void);
  api.reactivateMember = () => delay(undefined as void);
  api.removeMember = () => delay(undefined as void);

  // Intake
  api.getIntake = () => delay({ ...mockIntake });
  api.ensureIntake = () =>
    delay({ created: false, rawToken: null, publicSlug: mockIntake.publicSlug });
  api.replaceIntake = () =>
    delay({ rawToken: "mock-raw-token-new", publicSlug: "apex-home-new9z", staleLinksWarning: true });

  // Request list
  api.getRequests = (params = {}) => {
    const all = getMockRequests();
    let filtered = all.filter((r) => !r.isTerminal);

    if (params.view === "assigned_to_me") {
      filtered = all.filter((r) => r.participation.currentUserParticipationType === "responsible");
    } else if (params.view === "needs_attention") {
      filtered = all.filter((r) => r.attention.attentionLevel === "elevated");
    } else if (params.view === "watching") {
      filtered = all.filter((r) => r.participation.currentUserParticipationType === "watching");
    } else if (params.view === "feedback_review") {
      filtered = all.filter((r) => r.preview.previewSource === "customer_feedback");
    } else if (params.view === "ready_to_close") {
      filtered = [];
    }

    if (params.status) {
      filtered = filtered.filter((r) => r.status === params.status);
    }
    if (params.q) {
      const q = params.q.toLowerCase();
      filtered = filtered.filter(
        (r) =>
          r.customerName.toLowerCase().includes(q) ||
          r.description.toLowerCase().includes(q) ||
          r.referenceCode.toLowerCase().includes(q),
      );
    }

    return delay({
      requests: filtered,
      pageInfo: { limit: 25, hasMore: false, nextCursor: null },
      viewCounts: params.view === "default" || !params.view ? mockViewCounts : null,
      listContext: {
        view: params.view ?? "default",
        isDefaultCommandCenter: !params.view || params.view === "default",
        isHistory: false,
        isSearch: !!params.q,
      },
    });
  };

  api.getAvailableRequests = () =>
    delay({ requests: [], pageInfo: { limit: 25, hasMore: false, nextCursor: null } });

  // Phone lookup
  api.lookupRequestByPhone = (phone) =>
    delay({
      customer:
        phone === "5555550100"
          ? { name: "Sarah Mitchell", phone, email: "sarah.mitchell@example.com" }
          : null,
      activeRequests: [],
      hasMoreActiveRequests: false,
    });

  // Create request
  api.createRequest = (body) => {
    const now = new Date().toISOString();
    const id = nextRequestId();
    const code = `KC-${String(mockRequestCounter).padStart(3, "0")}`;

    const detail: KeepRequestDetailResult = {
      requestId: id,
      referenceCode: code,
      status: "received",
      origin: "business_created",
      source: body.source,
      needsShare: false,
      businessName: mockSetup.businessName,
      customerName: body.customerName,
      customerPhone: body.customerPhone,
      customerEmail: body.customerEmail ?? null,
      description: body.description,
      currentStatusText: null,
      pageToken: `mock-page-token-${id}`,
      version: crypto.randomUUID(),
      expiresAtUtc: null,
      createdAtUtc: now,
      lastBusinessActivityAt: now,
      lastCustomerActivityAt: null,
      terminatedAtUtc: null,
      followUpOnDate: null,
      followUpOnReason: null,
      followUpOnNote: null,
      plannedForDate: null,
      attentionLevel: "normal",
      waitingDirection: "business",
      attentionReason: null,
      priorityBand: "normal",
      attentionSinceUtc: null,
      nextAttentionAtUtc: null,
      attentionClearedAtUtc: null,
      attentionClearedByAccountUserId: null,
      attentionClearReason: null,
      firstResponseDueAtUtc: null,
      firstRespondedAtUtc: null,
      firstResponderAccountUserId: null,
      firstResponseEventId: null,
      feedbackWasResolved: null,
      feedbackComment: null,
      feedbackSubmittedAtUtc: null,
      feedbackCommentVisible: false,
      feedbackReviewedAtUtc: null,
      feedbackReviewedByAccountUserId: null,
      feedbackReviewNote: null,
      feedbackReviewAgeBucket: null,
      feedbackReviewDueAtUtc: null,
      customerPageLastViewedAtUtc: null,
      customerPageViewedAfterLatestUpdate: null,
      intakeUrgency: "routine",
      businessPriority: null,
      contactPreference: "no_preference",
      serviceAddressLine1: null,
      serviceAddressLine2: null,
      serviceCity: null,
      serviceState: null,
      serviceZip: null,
      contactActions: [{ type: "call", available: true, target: body.customerPhone }],
      participants: [
        {
          accountUserId: MOCK_USER_ID,
          displayName: "Jamie Reyes",
          role: "owner",
          participationType: "responsible",
          notificationsEnabled: true,
          isEligible: true,
          attachedAtUtc: now,
          detachedAtUtc: null,
        },
      ],
      currentUserParticipation: { participationType: "responsible", notificationsEnabled: true },
      events: [
        {
          id: nextEventId(),
          eventType: "RequestCreated",
          content: null,
          visibility: "internal",
          occurredAtUtc: now,
          actorType: "AccountUser",
          actorAccountUserId: MOCK_USER_ID,
          actorDisplayName: "Jamie Reyes",
          statusAfter: "received",
          messageIntent: null,
          communicationChannel: null,
          externalContactDirection: null,
          externalContactChannel: null,
          externalContactOutcome: null,
          externalContactRequiresFollowUp: null,
          externalContactSetFirstResponse: null,
          externalContactClearedAttention: null,
          participationAction: null,
          participationTargetAccountUserId: null,
          participationTargetDisplayName: null,
          participationPreviousResponsibleAccountUserId: null,
          participationInternalNote: null,
          plannedForDate: null,
          followUpOnDate: null,
          followUpOnReason: null,
        },
      ],
      availableActions: { ...OWNER_ACTIONS },
      validation: MOCK_VALIDATION,
      navigation: null,
    };

    const summary: KeepRequestSummary = {
      id,
      referenceCode: code,
      status: "received",
      currentStatusText: null,
      customerName: body.customerName,
      customerPhone: body.customerPhone,
      customerEmail: body.customerEmail ?? null,
      description: body.description,
      lastCustomerActivityAtUtc: null,
      lastBusinessActivityAtUtc: now,
      createdAtUtc: now,
      updatedAtUtc: now,
      isTerminal: false,
      isPostCloseFollowUp: false,
      needsShare: false,
      source: "public_intake",
      intakeUrgency: "routine",
      businessPriority: null,
      contactPreference: "no_preference",
      serviceAddressLine1: null,
      serviceAddressLine2: null,
      serviceCity: null,
      serviceState: null,
      serviceZip: null,
      ranking: {
        rankingGroup: "first_response_pending",
        rankingOrder: 6,
        rankingReason: "first_response_pending",
        severity: "attention",
        isOverdue: false,
        elapsedSinceUtc: null,
        dueAtUtc: null,
        isPostClose: false,
      },
      attention: {
        attentionLevel: "normal",
        waitingDirection: "business",
        attentionReason: null,
        priorityBand: "normal",
        attentionSinceUtc: null,
        nextAttentionAtUtc: null,
        firstResponseDueAtUtc: null,
        firstRespondedAtUtc: null,
        firstResponsePending: true,
        firstResponseOverdue: false,
      },
      preview: { previewText: null, previewSource: null, previewTruncated: false },
      participation: {
        responsibleCount: 1,
        watchingCount: 0,
        hasResponsible: true,
        isUnassigned: false,
        currentUserParticipationType: "responsible",
        responsibleDisplayName: "Jamie Reyes",
      },
      actions: {
        quickActions: [
          {
            code: "start",
            label: "Start Work",
            visibility: "primary",
            clearsAttention: false,
            countsFirstResponse: true,
            changesStatus: true,
            effectSummaryCode: "in_progress",
          },
        ],
      },
    };

    addMockRequest(summary, detail);

    return delay(detail);
  };

  // Request detail
  api.getRequestDetail = (requestId) => {
    const d = getMockDetail(requestId);
    if (!d) return Promise.reject(new Error(`Mock: no detail for ${requestId}`));
    return delay(withRoleActions(d));
  };

  // Mutations — all update local state so re-fetches show changes

  api.recordShareIntent = (requestId) => {
    const d = getMockDetail(requestId);
    if (d) updateMockDetail(requestId, { ...d, needsShare: false });
    return delay(undefined as void);
  };

  api.patchRequestStatus = (requestId, body, _version) => {
    const d = getMockDetail(requestId);
    if (!d) return Promise.reject(new Error("Mock: request not found"));
    const updated = appendEvent(
      { ...d, status: body.status, currentStatusText: body.message ?? d.currentStatusText },
      { eventType: "StatusChanged", occurredAtUtc: new Date().toISOString(), statusAfter: body.status, content: body.message ?? null },
    );
    updateMockDetail(requestId, updated);
    return delay(withRoleActions(updated));
  };

  api.logExternalContact = (requestId, body, _version) => {
    const d = getMockDetail(requestId);
    if (!d) return Promise.reject(new Error("Mock: request not found"));
    const updated = appendEvent(d, {
      eventType: "ExternalContactLogged",
      occurredAtUtc: new Date().toISOString(),
      content: body.summary ?? null,
      externalContactDirection: body.direction,
      externalContactChannel: body.channel,
      externalContactOutcome: body.outcome ?? null,
      externalContactRequiresFollowUp: body.requiresBusinessFollowUp ?? false,
      externalContactSetFirstResponse: !d.firstRespondedAtUtc,
      externalContactClearedAttention: false,
    });
    updateMockDetail(requestId, updated);
    return delay(withRoleActions(updated));
  };

  api.acknowledgeAttention = (requestId, reason, _version) => {
    const d = getMockDetail(requestId);
    if (!d) return Promise.reject(new Error("Mock: request not found"));
    const now = new Date().toISOString();
    const updated = appendEvent(
      {
        ...d,
        attentionLevel: "normal",
        attentionClearedAtUtc: now,
        attentionClearedByAccountUserId: MOCK_USER_ID,
        attentionClearReason: reason,
        availableActions: { ...d.availableActions, canAcknowledgeAttention: false },
      },
      { eventType: "AttentionAcknowledged", occurredAtUtc: now, content: reason },
    );
    updateMockDetail(requestId, updated);
    return delay(withRoleActions(updated));
  };

  api.selfWatch = (requestId, _version) => {
    const d = getMockDetail(requestId);
    if (!d) return Promise.reject(new Error("Mock: request not found"));
    const updated: KeepRequestDetailResult = {
      ...d,
      currentUserParticipation: { participationType: "watching", notificationsEnabled: true },
      availableActions: { ...d.availableActions, canWatch: false, canUnwatch: true },
      version: crypto.randomUUID(),
    };
    updateMockDetail(requestId, updated);
    return delay(withRoleActions(updated));
  };

  api.selfUnwatch = (requestId, _version) => {
    const d = getMockDetail(requestId);
    if (!d) return Promise.reject(new Error("Mock: request not found"));
    const updated: KeepRequestDetailResult = {
      ...d,
      currentUserParticipation: { participationType: "none", notificationsEnabled: null },
      availableActions: { ...d.availableActions, canWatch: true, canUnwatch: false },
      version: crypto.randomUUID(),
    };
    updateMockDetail(requestId, updated);
    return delay(withRoleActions(updated));
  };

  api.mute = (requestId, _version) => {
    const d = getMockDetail(requestId);
    if (!d) return Promise.reject(new Error("Mock: request not found"));
    const updated = { ...d, availableActions: { ...d.availableActions, canMute: false, canUnmute: true }, version: crypto.randomUUID() };
    updateMockDetail(requestId, updated);
    return delay(withRoleActions(updated));
  };

  api.unmute = (requestId, _version) => {
    const d = getMockDetail(requestId);
    if (!d) return Promise.reject(new Error("Mock: request not found"));
    const updated = { ...d, availableActions: { ...d.availableActions, canMute: true, canUnmute: false }, version: crypto.randomUUID() };
    updateMockDetail(requestId, updated);
    return delay(withRoleActions(updated));
  };

  api.postBusinessUpdate = (requestId, body, _version) => {
    const d = getMockDetail(requestId);
    if (!d) return Promise.reject(new Error("Mock: request not found"));
    const updated = appendEvent(
      body.setStatus ? { ...d, status: body.setStatus } : d,
      {
        eventType: "BusinessUpdateSent",
        occurredAtUtc: new Date().toISOString(),
        content: body.message,
        visibility: "public",
        messageIntent: "update",
        statusAfter: body.setStatus ?? null,
      },
    );
    updateMockDetail(requestId, updated);
    return delay(withRoleActions(updated));
  };

  api.addInternalNote = (requestId, body, _version) => {
    const d = getMockDetail(requestId);
    if (!d) return Promise.reject(new Error("Mock: request not found"));
    const now = new Date().toISOString();
    const updated = appendEvent(d, {
      eventType: "InternalNoteAdded",
      occurredAtUtc: now,
      content: body.note,
    });
    updateMockDetail(requestId, updated);
    return delay(withRoleActions(updated));
  };

  api.setFollowUpOn = (requestId, body, _version) => {
    const d = getMockDetail(requestId);
    if (!d) return Promise.reject(new Error("Mock: request not found"));
    const now = new Date().toISOString();
    const updated = appendEvent(
      { ...d, followUpOnDate: body.date, followUpOnReason: body.reason, followUpOnNote: body.note ?? null },
      { eventType: "FollowUpOnChanged", occurredAtUtc: now, content: body.date },
    );
    updateMockDetail(requestId, updated);
    return delay(withRoleActions(updated));
  };

  api.clearFollowUpOn = (requestId, _version) => {
    const d = getMockDetail(requestId);
    if (!d) return Promise.reject(new Error("Mock: request not found"));
    const now = new Date().toISOString();
    const updated = appendEvent(
      { ...d, followUpOnDate: null, followUpOnReason: null, followUpOnNote: null },
      { eventType: "FollowUpOnChanged", occurredAtUtc: now, content: null },
    );
    updateMockDetail(requestId, updated);
    return delay(withRoleActions(updated));
  };

  api.setPlannedFor = (requestId, body, _version) => {
    const d = getMockDetail(requestId);
    if (!d) return Promise.reject(new Error("Mock: request not found"));
    const now = new Date().toISOString();
    const updated = appendEvent(
      { ...d, plannedForDate: body.date },
      { eventType: "PlannedForChanged", occurredAtUtc: now, content: body.date },
    );
    updateMockDetail(requestId, updated);
    return delay(withRoleActions(updated));
  };

  api.clearPlannedFor = (requestId, _version) => {
    const d = getMockDetail(requestId);
    if (!d) return Promise.reject(new Error("Mock: request not found"));
    const now = new Date().toISOString();
    const updated = appendEvent(
      { ...d, plannedForDate: null },
      { eventType: "PlannedForChanged", occurredAtUtc: now, content: null },
    );
    updateMockDetail(requestId, updated);
    return delay(withRoleActions(updated));
  };

  api.markFeedbackReviewed = (requestId, body, _version) => {
    const d = getMockDetail(requestId);
    if (!d) return Promise.reject(new Error("Mock: request not found"));
    const now = new Date().toISOString();
    const updated = appendEvent(
      {
        ...d,
        feedbackReviewedAtUtc: now,
        feedbackReviewedByAccountUserId: MOCK_USER_ID,
        feedbackReviewNote: body.note ?? null,
        availableActions: { ...d.availableActions, canMarkFeedbackReviewed: false },
      },
      { eventType: "FeedbackReviewed", occurredAtUtc: now, content: body.note ?? null },
    );
    updateMockDetail(requestId, updated);
    return delay(withRoleActions(updated));
  };

  api.setBusinessPriority = (requestId, priority, _version) => {
    const d = getMockDetail(requestId);
    if (!d) return Promise.reject(new Error("Mock: request not found"));
    const updated = appendEvent(
      { ...d, businessPriority: priority },
      {
        eventType: "business_priority_changed",
        occurredAtUtc: new Date().toISOString(),
        content: priority ? `Priority set to ${priority}` : "Priority cleared",
        visibility: "internal",
      },
    );
    updateMockDetail(requestId, updated);
    return delay(withRoleActions(updated));
  };
}
