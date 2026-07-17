const API_BASE = import.meta.env.VITE_API_BASE_URL;

export class ApiError extends Error {
  constructor(
    public readonly status: number,
    public readonly code: string | undefined,
    message: string,
    public readonly extensions?: Record<string, unknown>,
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
    let extensions: Record<string, unknown> | undefined;
    try {
      const problem = (await response.json()) as Record<string, unknown>;
      extensions = (problem["extensions"] as Record<string, unknown> | undefined) ?? problem;
      code =
        (extensions?.["code"] as string | undefined) ??
        (problem["code"] as string | undefined);
    } catch {
      // body may be empty or non-JSON; code stays undefined
    }
    throw new ApiError(response.status, code, `API ${response.status} ${path}`, extensions);
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
    let extensions: Record<string, unknown> | undefined;
    try {
      const problem = (await response.json()) as Record<string, unknown>;
      extensions = (problem["extensions"] as Record<string, unknown> | undefined) ?? problem;
      code =
        (extensions?.["code"] as string | undefined) ??
        (problem["code"] as string | undefined);
    } catch {
      // body may be empty or non-JSON; code stays undefined
    }
    throw new ApiError(response.status, code, `API ${response.status} ${path}`, extensions);
  }

  return response.json() as Promise<T>;
}

// For endpoints that return JSON on some paths and empty body on others.
async function apiFetchMaybeJson<T>(path: string, init?: RequestInit): Promise<T | null> {
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
    let extensions: Record<string, unknown> | undefined;
    try {
      const problem = (await response.json()) as Record<string, unknown>;
      extensions = (problem["extensions"] as Record<string, unknown> | undefined) ?? problem;
      code =
        (extensions?.["code"] as string | undefined) ??
        (problem["code"] as string | undefined);
    } catch {
      // body may be empty or non-JSON; code stays undefined
    }
    throw new ApiError(response.status, code, `API ${response.status} ${path}`, extensions);
  }
  const text = await response.text();
  return text ? (JSON.parse(text) as T) : null;
}

import type {
  AccountRole,
  MeResponse,
  OnboardingChecklist,
  KeepSetupPolicyResult,
  KeepSetupResult,
  SeatUsage,
  MemberItem,
  ListMembersResponse,
  KeepBusinessSetupResult,
  IntakeStatusResult,
  IntakeEnsureResult,
  IntakeReplaceResult,
  IntakeRenameLinkResult,
  CreateIntakeSmsHandoffResult,
  PhoneLookupCustomer,
  PhoneLookupActiveRequest,
  PhoneLookupResult,
  CreateRequestBody,
  AvailableActionsMetadata,
  ValidationHintsMetadata,
  ContactActionItem,
  KeepRequestParticipantItem,
  CurrentUserDetailParticipation,
  KeepRequestEventItem,
  KeepRequestNavigation,
  KeepRequestDetailResult,
  ShareIntentMethod,
  CreateSmsHandoffResult,
  LogExternalContactBody,
  UpdateServiceLocationBody,
  KeepRequestRankingInfo,
  KeepRequestAttentionInfo,
  KeepRequestPreviewInfo,
  KeepRequestParticipationInfo,
  KeepQuickAction,
  KeepRequestActionsInfo,
  KeepRequestTimingInfo,
  KeepRequestSummary,
  KeepRequestViewCounts,
  KeepRequestPageInfo,
  KeepRequestListContext,
  KeepRequestListResult,
  KeepRequestAvailableItem,
  KeepAvailableRequestsResult,
  RequestView,
  GetRequestsParams,
  ResolveFollowUpBody,
} from "./apiClient.types";

export type {
  AccountRole,
  MeResponse,
  OnboardingChecklist,
  KeepSetupPolicyResult,
  KeepSetupResult,
  SeatUsage,
  MemberItem,
  ListMembersResponse,
  KeepBusinessSetupResult,
  IntakeStatusResult,
  IntakeEnsureResult,
  IntakeReplaceResult,
  IntakeRenameLinkResult,
  CreateIntakeSmsHandoffResult,
  PhoneLookupCustomer,
  PhoneLookupActiveRequest,
  PhoneLookupResult,
  CreateRequestBody,
  AvailableActionsMetadata,
  ValidationHintsMetadata,
  ContactActionItem,
  KeepRequestParticipantItem,
  CurrentUserDetailParticipation,
  KeepRequestEventItem,
  KeepRequestNavigation,
  KeepRequestDetailResult,
  ShareIntentMethod,
  CreateSmsHandoffResult,
  LogExternalContactBody,
  UpdateServiceLocationBody,
  KeepRequestRankingInfo,
  KeepRequestAttentionInfo,
  KeepRequestPreviewInfo,
  KeepRequestParticipationInfo,
  KeepQuickAction,
  KeepRequestActionsInfo,
  KeepRequestTimingInfo,
  KeepRequestSummary,
  KeepRequestViewCounts,
  KeepRequestPageInfo,
  KeepRequestListContext,
  KeepRequestListResult,
  KeepRequestAvailableItem,
  KeepAvailableRequestsResult,
  RequestView,
  GetRequestsParams,
  ResolveFollowUpBody,
};

export type { FollowUpResolutionOutcome, FollowUpCompletionReason } from "./apiClient.types";

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
  createSmsHandoff: (requestId: string, messageBody: string) =>
    apiFetch<CreateSmsHandoffResult>(`/keep/requests/${requestId}/sms-handoff`, {
      method: "POST",
      body: JSON.stringify({ messageBody }),
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
  updateServiceLocation: (requestId: string, body: UpdateServiceLocationBody, version: string) =>
    apiFetch<KeepRequestDetailResult>(`/keep/requests/${requestId}/service-location`, {
      method: "PUT",
      headers: { "X-Keep-Request-Version": version },
      body: JSON.stringify(body),
    }),
  setBusinessPriority: (requestId: string, priority: string | null, version: string) =>
    apiFetch<KeepRequestDetailResult>(`/keep/requests/${requestId}/priority`, {
      method: "PUT",
      headers: { "X-Keep-Request-Version": version },
      body: JSON.stringify({ priority }),
    }),
  acknowledgeAttention: (requestId: string, reason: string, version: string) =>
    apiFetch<KeepRequestDetailResult>(`/keep/requests/${requestId}/attention/acknowledge`, {
      method: "POST",
      headers: { "X-Keep-Request-Version": version },
      body: JSON.stringify({ reason }),
    }),
  setResponsible: (requestId: string, accountUserId: string, version: string) =>
    apiFetch<KeepRequestDetailResult>(`/keep/requests/${requestId}/responsible`, {
      method: "PUT",
      headers: { "X-Keep-Request-Version": version },
      body: JSON.stringify({ accountUserId }),
    }),
  clearResponsible: (requestId: string, version: string) =>
    apiFetch<KeepRequestDetailResult>(`/keep/requests/${requestId}/responsible`, {
      method: "DELETE",
      headers: { "X-Keep-Request-Version": version },
      body: JSON.stringify({}),
    }),
  addWatcher: (requestId: string, accountUserId: string, version: string) =>
    apiFetch<KeepRequestDetailResult>(
      `/keep/requests/${requestId}/watchers/${encodeURIComponent(accountUserId)}`,
      {
        method: "PUT",
        headers: { "X-Keep-Request-Version": version },
        body: JSON.stringify({}),
      },
    ),
  removeWatcher: (requestId: string, accountUserId: string, version: string) =>
    apiFetch<KeepRequestDetailResult>(
      `/keep/requests/${requestId}/watchers/${encodeURIComponent(accountUserId)}`,
      {
        method: "DELETE",
        headers: { "X-Keep-Request-Version": version },
        body: JSON.stringify({}),
      },
    ),
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
  postBusinessUpdate: (
    requestId: string,
    body: { message: string; setStatus?: string },
    version: string,
  ) =>
    apiFetch<KeepRequestDetailResult>(`/keep/requests/${requestId}/business-updates`, {
      method: "POST",
      headers: { "X-Keep-Request-Version": version },
      body: JSON.stringify(body),
    }),
  getSetup: () => apiFetch<KeepSetupResult>("/keep/setup"),
  updateProfile: (body: {
    businessName: string;
    timeZone: string;
    customerFacingPhone: string | null;
    customerFacingEmail: string | null;
  }) =>
    apiFetch<KeepSetupResult>("/keep/setup/profile", {
      method: "PUT",
      body: JSON.stringify(body),
    }),
  updatePolicy: (body: {
    firstResponseTargetMinutes: number;
    standardResponseTargetMinutes: number;
    priorityResponseTargetMinutes: number;
    statusCheckThresholdDays: number;
  }) =>
    apiFetch<KeepSetupResult>("/keep/setup/policy", {
      method: "PUT",
      body: JSON.stringify(body),
    }),
  listMembers: (includeRemoved = false) =>
    apiFetch<ListMembersResponse>(
      `/accounts/me/members${includeRemoved ? "?includeRemoved=true" : ""}`,
    ),
  getIntake: () => apiFetch<IntakeStatusResult>("/keep/setup/intake"),
  ensureIntake: () =>
    apiFetch<IntakeEnsureResult>("/keep/setup/intake/ensure", { method: "POST" }),
  replaceIntake: () =>
    apiFetch<IntakeReplaceResult>("/keep/setup/intake/replace", { method: "POST" }),
  updateIntakeLinkName: (desiredName: string) =>
    apiFetch<IntakeRenameLinkResult>("/keep/setup/intake/link-name", {
      method: "PUT",
      body: JSON.stringify({ desiredName }),
    }),
  createIntakeSmsHandoff: (customerPhone: string) =>
    apiFetch<CreateIntakeSmsHandoffResult>("/keep/setup/intake/sms-handoff", {
      method: "POST",
      body: JSON.stringify({ customerPhone }),
    }),
  inviteMember: (email: string, role: string) =>
    apiFetch<{ status: string }>("/accounts/me/invite", {
      method: "POST",
      body: JSON.stringify({ email, role }),
    }),
  // Returns { inviteUrl } for manual_share delivery; null for email delivery.
  resendInvite: (accountUserId: string, delivery: "email" | "manual_share") =>
    apiFetchMaybeJson<{ inviteUrl: string }>(
      `/accounts/me/members/${encodeURIComponent(accountUserId)}/resend-invite`,
      { method: "POST", body: JSON.stringify({ delivery }) },
    ),
  changeRole: (accountUserId: string, role: string) =>
    apiFetchVoid(`/accounts/me/members/${encodeURIComponent(accountUserId)}/role`, {
      method: "PATCH",
      body: JSON.stringify({ role }),
    }),
  suspendMember: (accountUserId: string) =>
    apiFetchVoid(`/accounts/me/members/${encodeURIComponent(accountUserId)}/suspend`, {
      method: "POST",
    }),
  reactivateMember: (accountUserId: string) =>
    apiFetchVoid(`/accounts/me/members/${encodeURIComponent(accountUserId)}/reactivate`, {
      method: "POST",
    }),
  removeMember: (accountUserId: string) =>
    apiFetchVoid(`/accounts/me/members/${encodeURIComponent(accountUserId)}`, {
      method: "DELETE",
    }),
  setFollowUpOn: (requestId: string, body: { date: string; reason: string; note?: string | null }, version: string) =>
    apiFetch<KeepRequestDetailResult>(`/keep/requests/${requestId}/follow-up-on`, {
      method: "PUT",
      body: JSON.stringify(body),
      headers: { "X-Keep-Request-Version": version },
    }),
  clearFollowUpOn: (requestId: string, version: string) =>
    apiFetch<KeepRequestDetailResult>(`/keep/requests/${requestId}/follow-up-on`, {
      method: "DELETE",
      headers: { "X-Keep-Request-Version": version },
    }),
  resolveFollowUp: (requestId: string, body: ResolveFollowUpBody, version: string) =>
    apiFetch<KeepRequestDetailResult>(`/keep/requests/${requestId}/follow-up-resolution`, {
      method: "POST",
      body: JSON.stringify(body),
      headers: { "X-Keep-Request-Version": version },
    }),
  setPlannedFor: (requestId: string, body: { date: string }, version: string) =>
    apiFetch<KeepRequestDetailResult>(`/keep/requests/${requestId}/planned-for`, {
      method: "PUT",
      body: JSON.stringify(body),
      headers: { "X-Keep-Request-Version": version },
    }),
  clearPlannedFor: (requestId: string, version: string) =>
    apiFetch<KeepRequestDetailResult>(`/keep/requests/${requestId}/planned-for`, {
      method: "DELETE",
      headers: { "X-Keep-Request-Version": version },
    }),
  addInternalNote: (requestId: string, body: { note: string }, version: string) =>
    apiFetch<KeepRequestDetailResult>(`/keep/requests/${requestId}/internal-notes`, {
      method: "POST",
      body: JSON.stringify(body),
      headers: { "X-Keep-Request-Version": version },
    }),
  markFeedbackReviewed: (requestId: string, body: { note?: string | null }, version: string) =>
    apiFetch<KeepRequestDetailResult>(`/keep/requests/${requestId}/feedback-review`, {
      method: "POST",
      body: JSON.stringify(body),
      headers: { "X-Keep-Request-Version": version },
    }),
  markQuickCaptureExercise: () =>
    apiFetchVoid("/keep/setup/onboarding/marks/quick-capture-exercise", { method: "POST" }),
  markTrackerReview: () =>
    apiFetchVoid("/keep/setup/onboarding/marks/tracker-review", { method: "POST" }),
  markSpamClassification: () =>
    apiFetchVoid("/keep/setup/onboarding/marks/spam-classification", { method: "POST" }),
  getGuidedSetup: () =>
    apiFetch<KeepBusinessSetupResult>("/keep/setup/guided"),
  deferSetupStep: (step: number) =>
    apiFetchVoid(`/keep/setup/guided/defer/${step}`, { method: "POST" }),
};
