import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { api, ApiError } from "./apiClient";
import { __resetRedirectGuardForTests } from "./redirectToSignIn";

// Centralized in-app 401 handling (session-expiry recovery gap). A protected call that
// comes back 401 means the SPA's session is gone: route to sign-in exactly once, while
// still throwing so the in-flight caller unwinds. 403 and every other failure are left
// untouched.

let hrefWrites: string[] = [];

function stubLocation() {
  hrefWrites = [];
  Object.defineProperty(window, "location", {
    configurable: true,
    value: {
      get href() {
        return hrefWrites[hrefWrites.length - 1] ?? "";
      },
      set href(value: string) {
        hrefWrites.push(value);
      },
    },
  });
}

function mockFetchResponse(status: number) {
  return vi.fn().mockResolvedValue({
    ok: status >= 200 && status < 300,
    status,
    json: async () => ({}),
    text: async () => "",
  });
}

beforeEach(() => {
  __resetRedirectGuardForTests();
  stubLocation();
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("apiClient — centralized 401 redirect", () => {
  it("redirects to /signin once when a protected call returns 401", async () => {
    vi.stubGlobal("fetch", mockFetchResponse(401));

    await expect(api.getMe()).rejects.toBeInstanceOf(ApiError);

    expect(hrefWrites).toHaveLength(1);
    expect(hrefWrites[0]).toMatch(/\/signin$/);
  });

  it("redirects when the void wrapper (apiFetchVoid) returns 401", async () => {
    vi.stubGlobal("fetch", mockFetchResponse(401));

    await expect(api.markQuickCaptureExercise()).rejects.toBeInstanceOf(ApiError);

    expect(hrefWrites).toHaveLength(1);
    expect(hrefWrites[0]).toMatch(/\/signin$/);
  });

  it("redirects when the maybe-JSON wrapper (apiFetchMaybeJson) returns 401", async () => {
    vi.stubGlobal("fetch", mockFetchResponse(401));

    await expect(api.resendInvite("member-1", "email")).rejects.toBeInstanceOf(ApiError);

    expect(hrefWrites).toHaveLength(1);
    expect(hrefWrites[0]).toMatch(/\/signin$/);
  });

  it("navigates only once even when several calls 401 in sequence", async () => {
    vi.stubGlobal("fetch", mockFetchResponse(401));

    await expect(api.getMe()).rejects.toBeInstanceOf(ApiError);
    await expect(api.getMe()).rejects.toBeInstanceOf(ApiError);
    await expect(api.getOnboardingChecklist()).rejects.toBeInstanceOf(ApiError);

    expect(hrefWrites).toHaveLength(1);
  });

  it("does not redirect on 403 (authorization, not authentication)", async () => {
    vi.stubGlobal("fetch", mockFetchResponse(403));

    await expect(api.getMe()).rejects.toMatchObject({ status: 403 });

    expect(hrefWrites).toHaveLength(0);
  });

  it("does not redirect on a 500 server error", async () => {
    vi.stubGlobal("fetch", mockFetchResponse(500));

    await expect(api.getMe()).rejects.toBeInstanceOf(ApiError);

    expect(hrefWrites).toHaveLength(0);
  });

  it("does not redirect on a transport failure", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("network down")));

    await expect(api.getMe()).rejects.toThrow();

    expect(hrefWrites).toHaveLength(0);
  });
});
