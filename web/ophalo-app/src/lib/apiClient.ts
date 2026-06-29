const API_BASE = import.meta.env.VITE_API_BASE_URL;

export class ApiError extends Error {
  constructor(
    public readonly status: number,
    public readonly code: string | undefined,
    message: string,
  ) {
    super(message);
    this.name = "ApiError";
  }
}

async function apiFetchVoid(path: string, init?: RequestInit): Promise<void> {
  const response = await fetch(`${API_BASE}${path}`, {
    ...init,
    credentials: "include",
    headers: {
      "Content-Type": "application/json",
      ...init?.headers,
    },
  });
  if (!response.ok) {
    let code: string | undefined;
    try {
      const problem = (await response.json()) as Record<string, unknown>;
      const ext = problem["extensions"] as Record<string, unknown> | undefined;
      code =
        (ext?.["code"] as string | undefined) ??
        (problem["code"] as string | undefined);
    } catch {
      // body may be empty or non-JSON; code stays undefined
    }
    throw new ApiError(response.status, code, `API ${response.status} ${path}`);
  }
}

async function apiFetch<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${API_BASE}${path}`, {
    ...init,
    credentials: "include",
    headers: {
      "Content-Type": "application/json",
      ...init?.headers,
    },
  });

  if (!response.ok) {
    let code: string | undefined;
    try {
      const problem = (await response.json()) as Record<string, unknown>;
      const ext = problem["extensions"] as Record<string, unknown> | undefined;
      code =
        (ext?.["code"] as string | undefined) ??
        (problem["code"] as string | undefined);
    } catch {
      // body may be empty or non-JSON; code stays undefined
    }
    throw new ApiError(response.status, code, `API ${response.status} ${path}`);
  }

  return response.json() as Promise<T>;
}

export type AccountRole = "owner" | "admin" | "operator" | "viewer" | "unknown";

export interface MeResponse {
  accountUserId: string;
  accountId: string;
  isAuthenticated: boolean;
  isVerified: boolean;
  accountRole: AccountRole;
}

export interface OnboardingChecklist {
  profileAndContactSaved: boolean;
  timezoneSaved: boolean;
  policySaved: boolean;
  intakeLinkActive: boolean;
  operatorInvited: boolean;
  mobileDeviceRegistered: boolean;
  firstRequestCreated: boolean;
  quickCaptureExerciseDone: boolean;
  trackerReviewDone: boolean;
  spamClassificationExplained: boolean;
}

export interface PhoneLookupCustomer {
  name: string;
  phone: string;
  email: string | null;
}

export interface PhoneLookupActiveRequest {
  requestId: string;
  referenceCode: string;
  status: string;
  description: string;
  lastActivityAtUtc: string | null;
}

export interface PhoneLookupResult {
  customer: PhoneLookupCustomer | null;
  activeRequests: PhoneLookupActiveRequest[];
  hasMoreActiveRequests: boolean;
}

export interface CreateRequestBody {
  customerName: string;
  customerPhone: string;
  customerEmail?: string;
  description: string;
  source: string;
}

export interface AvailableActionsMetadata {
  canChangeStatus: boolean;
  canSendBusinessUpdate: boolean;
  canAddInternalNote: boolean;
  canAcknowledgeAttention: boolean;
  canLogExternalContact: boolean;
  canAssignResponsible: boolean;
  canWatch: boolean;
  canUnwatch: boolean;
  canMute: boolean;
  canUnmute: boolean;
  canMarkFeedbackReviewed: boolean;
  canSetFollowUpOn: boolean;
  canSetPlannedFor: boolean;
  canClose: boolean;
  canClassify: boolean;
  canRecordShareIntent: boolean;
  allowedStatuses: string[];
}

export interface ValidationHintsMetadata {
  businessUpdateMaxLength: number;
  internalNoteMaxLength: number;
  statusMessageMaxLength: number;
  acknowledgeReasonMaxLength: number;
  externalContactSummaryMaxLength: number;
  feedbackReviewNoteMaxLength: number;
  followUpNoteMaxLength: number;
  allowedFollowUpReasons: string[];
  messageRequiredForStatuses: string[];
}

export interface ContactActionItem {
  type: string;
  available: boolean;
  target: string;
}

export interface KeepRequestParticipantItem {
  accountUserId: string;
  displayName: string;
  role: string;
  participationType: string;
  notificationsEnabled: boolean;
  isEligible: boolean;
  attachedAtUtc: string;
  detachedAtUtc: string | null;
}

export interface CurrentUserDetailParticipation {
  participationType: string;
  notificationsEnabled: boolean | null;
}

export interface KeepRequestEventItem {
  id: string;
  eventType: string;
  content: string | null;
  visibility: string;
  occurredAtUtc: string;
  actorType: string;
  actorAccountUserId: string | null;
  actorDisplayName: string | null;
  statusAfter: string | null;
  messageIntent: string | null;
  communicationChannel: string | null;
  externalContactDirection: string | null;
  externalContactChannel: string | null;
  externalContactOutcome: string | null;
  externalContactRequiresFollowUp: boolean | null;
  externalContactSetFirstResponse: boolean | null;
  externalContactClearedAttention: boolean | null;
  participationAction: string | null;
  participationTargetAccountUserId: string | null;
  participationTargetDisplayName: string | null;
  participationPreviousResponsibleAccountUserId: string | null;
  participationInternalNote: string | null;
}

export interface KeepRequestNavigation {
  previousId: string | null;
  nextId: string | null;
  position: number;
  total: number;
}

export interface KeepRequestDetailResult {
  requestId: string;
  referenceCode: string;
  status: string;
  origin: string;
  source: string | null;
  needsShare: boolean;
  businessName: string;
  customerName: string;
  customerPhone: string;
  customerEmail: string | null;
  description: string;
  currentStatusText: string | null;
  pageToken: string;
  version: string;
  expiresAtUtc: string | null;
  createdAtUtc: string;
  lastBusinessActivityAt: string | null;
  lastCustomerActivityAt: string | null;
  terminatedAtUtc: string | null;
  followUpOnDate: string | null;
  followUpOnReason: string | null;
  followUpOnNote: string | null;
  plannedForDate: string | null;
  attentionLevel: string;
  waitingDirection: string;
  attentionReason: string | null;
  priorityBand: string;
  attentionSinceUtc: string | null;
  nextAttentionAtUtc: string | null;
  attentionClearedAtUtc: string | null;
  attentionClearedByAccountUserId: string | null;
  attentionClearReason: string | null;
  firstResponseDueAtUtc: string | null;
  firstRespondedAtUtc: string | null;
  firstResponderAccountUserId: string | null;
  firstResponseEventId: string | null;
  feedbackWasResolved: boolean | null;
  feedbackComment: string | null;
  feedbackSubmittedAtUtc: string | null;
  feedbackCommentVisible: boolean;
  feedbackReviewedAtUtc: string | null;
  feedbackReviewedByAccountUserId: string | null;
  feedbackReviewNote: string | null;
  feedbackReviewAgeBucket: string | null;
  feedbackReviewDueAtUtc: string | null;
  customerPageLastViewedAtUtc: string | null;
  customerPageViewedAfterLatestUpdate: boolean | null;
  contactActions: ContactActionItem[];
  participants: KeepRequestParticipantItem[];
  currentUserParticipation: CurrentUserDetailParticipation;
  events: KeepRequestEventItem[];
  availableActions: AvailableActionsMetadata;
  validation: ValidationHintsMetadata;
  navigation: KeepRequestNavigation | null;
}

export type ShareIntentMethod = "copy_link" | "native_share" | "manual_mark_shared";

export interface LogExternalContactBody {
  direction: string;
  channel: string;
  outcome?: string;
  requiresBusinessFollowUp?: boolean;
  summary?: string;
}

// --- Request list ---

export interface KeepRequestAttentionInfo {
  attentionLevel: string;
  waitingDirection: string;
  attentionReason: string | null;
  priorityBand: string;
  attentionSinceUtc: string | null;
  nextAttentionAtUtc: string | null;
  firstResponseDueAtUtc: string | null;
  firstRespondedAtUtc: string | null;
  firstResponsePending: boolean;
  firstResponseOverdue: boolean;
}

export interface KeepRequestPreviewInfo {
  previewText: string | null;
  previewSource: string | null;
  previewTruncated: boolean;
}

export interface KeepRequestParticipationInfo {
  responsibleCount: number;
  watchingCount: number;
  hasResponsible: boolean;
  isUnassigned: boolean;
  currentUserParticipationType: string;
  responsibleDisplayName: string | null;
}

export interface KeepQuickAction {
  code: string;
  label: string;
  visibility: string;
  clearsAttention: boolean;
  countsFirstResponse: boolean;
  changesStatus: boolean;
  effectSummaryCode: string;
}

export interface KeepRequestActionsInfo {
  quickActions: KeepQuickAction[];
}

export interface KeepRequestSummary {
  id: string;
  referenceCode: string;
  status: string;
  currentStatusText: string | null;
  customerName: string;
  customerPhone: string;
  customerEmail: string | null;
  description: string;
  lastCustomerActivityAtUtc: string | null;
  lastBusinessActivityAtUtc: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
  isTerminal: boolean;
  isPostCloseFollowUp: boolean;
  needsShare: boolean;
  attention: KeepRequestAttentionInfo;
  preview: KeepRequestPreviewInfo;
  participation: KeepRequestParticipationInfo;
  actions: KeepRequestActionsInfo;
}

export interface KeepRequestViewCounts {
  default: number;
  assignedToMe: number;
  watching: number;
  unassigned: number;
  needsAttention: number;
  feedbackReview: number;
  readyToClose: number;
}

export interface KeepRequestPageInfo {
  limit: number;
  hasMore: boolean;
  nextCursor: string | null;
}

export interface KeepRequestListContext {
  view: string;
  isDefaultCommandCenter: boolean;
  isHistory: boolean;
  isSearch: boolean;
}

export interface KeepRequestListResult {
  requests: KeepRequestSummary[];
  pageInfo: KeepRequestPageInfo;
  viewCounts: KeepRequestViewCounts | null;
  listContext: KeepRequestListContext;
}

export interface KeepRequestAvailableItem {
  requestId: string;
  referenceCode: string;
  customerName: string;
  status: string;
  createdAtUtc: string;
  attentionSinceUtc: string | null;
  nextAttentionAtUtc: string | null;
  priorityBand: string;
  attentionLevel: string;
  descriptionPreview: string;
  version: string;
  canSelfAssign: boolean;
  canWatch: boolean;
}

export interface KeepAvailableRequestsResult {
  requests: KeepRequestAvailableItem[];
  pageInfo: KeepRequestPageInfo;
}

export type RequestView =
  | "default"
  | "assigned_to_me"
  | "needs_attention"
  | "watching"
  | "ready_to_close"
  | "feedback_review";

export interface GetRequestsParams {
  view?: RequestView;
  status?: string;
  q?: string;
  cursor?: string;
  limit?: number;
}

export const api = {
  getMe: () => apiFetch<MeResponse>("/auth/me"),
  getOnboardingChecklist: () =>
    apiFetch<OnboardingChecklist>("/keep/setup/onboarding"),
  lookupRequestByPhone: (phone: string) =>
    apiFetch<PhoneLookupResult>(
      `/keep/requests/lookup?phone=${encodeURIComponent(phone)}`,
    ),
  createRequest: (body: CreateRequestBody) =>
    apiFetch<KeepRequestDetailResult>("/keep/requests", {
      method: "POST",
      body: JSON.stringify(body),
    }),
  getRequests: (params: GetRequestsParams = {}) => {
    const qs = new URLSearchParams();
    if (params.view) qs.set("view", params.view);
    if (params.status) qs.set("status", params.status);
    if (params.q) qs.set("q", params.q);
    if (params.cursor) qs.set("cursor", params.cursor);
    if (params.limit) qs.set("limit", String(params.limit));
    const query = qs.toString();
    return apiFetch<KeepRequestListResult>(
      `/keep/requests${query ? `?${query}` : ""}`,
    );
  },
  getAvailableRequests: (params: { cursor?: string; limit?: number } = {}) => {
    const qs = new URLSearchParams();
    if (params.cursor) qs.set("cursor", params.cursor);
    if (params.limit) qs.set("limit", String(params.limit));
    const query = qs.toString();
    return apiFetch<KeepAvailableRequestsResult>(
      `/keep/requests/available${query ? `?${query}` : ""}`,
    );
  },
  getRequestDetail: (requestId: string) =>
    apiFetch<KeepRequestDetailResult>(`/keep/requests/${requestId}`),
  recordShareIntent: (requestId: string, method: ShareIntentMethod) =>
    apiFetchVoid(`/keep/requests/${requestId}/share-intent`, {
      method: "POST",
      body: JSON.stringify({ method }),
    }),
  patchRequestStatus: (
    requestId: string,
    body: { status: string; message?: string },
    version: string,
  ) =>
    apiFetch<KeepRequestDetailResult>(`/keep/requests/${requestId}/status`, {
      method: "PATCH",
      headers: { "X-Keep-Request-Version": version },
      body: JSON.stringify(body),
    }),
  logExternalContact: (requestId: string, body: LogExternalContactBody, version: string) =>
    apiFetch<KeepRequestDetailResult>(`/keep/requests/${requestId}/external-contact`, {
      method: "POST",
      headers: { "X-Keep-Request-Version": version },
      body: JSON.stringify(body),
    }),
  acknowledgeAttention: (requestId: string, reason: string, version: string) =>
    apiFetch<KeepRequestDetailResult>(`/keep/requests/${requestId}/attention/acknowledge`, {
      method: "POST",
      headers: { "X-Keep-Request-Version": version },
      body: JSON.stringify({ reason }),
    }),
  selfWatch: (requestId: string, version: string) =>
    apiFetch<KeepRequestDetailResult>(`/keep/requests/${requestId}/watch`, {
      method: "PUT",
      headers: { "X-Keep-Request-Version": version },
    }),
  selfUnwatch: (requestId: string, version: string) =>
    apiFetch<KeepRequestDetailResult>(`/keep/requests/${requestId}/watch`, {
      method: "DELETE",
      headers: { "X-Keep-Request-Version": version },
    }),
  mute: (requestId: string, version: string) =>
    apiFetch<KeepRequestDetailResult>(`/keep/requests/${requestId}/mute`, {
      method: "PUT",
      headers: { "X-Keep-Request-Version": version },
    }),
  unmute: (requestId: string, version: string) =>
    apiFetch<KeepRequestDetailResult>(`/keep/requests/${requestId}/mute`, {
      method: "DELETE",
      headers: { "X-Keep-Request-Version": version },
    }),
};
