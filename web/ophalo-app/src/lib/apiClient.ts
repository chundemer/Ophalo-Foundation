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

export const api = {
  getMe: () => apiFetch<MeResponse>("/auth/me"),
  getOnboardingChecklist: () =>
    apiFetch<OnboardingChecklist>("/keep/setup/onboarding"),
};
