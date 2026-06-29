const API_BASE = import.meta.env.VITE_API_BASE_URL ?? "";

export class ApiError extends Error {
  status: number;
  body: unknown;

  constructor(status: number, body: unknown) {
    super(`API request failed with status ${status}`);
    this.name = "ApiError";
    this.status = status;
    this.body = body;
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
    let body: unknown = null;
    try {
      body = await response.json();
    } catch {
      body = await response.text();
    }
    throw new ApiError(response.status, body);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return response.json() as Promise<T>;
}

export interface MeResponse {
  accountUserId: string;
  accountId: string;
  isAuthenticated: boolean;
  isVerified: boolean;
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

export interface KeepRequestDetailResult {
  requestId: string;
  referenceCode: string;
  status: string;
  customerName: string;
  customerPhone: string;
  customerEmail: string | null;
  description: string;
  pageToken: string;
  needsShare: boolean;
  createdAtUtc: string;
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
};
