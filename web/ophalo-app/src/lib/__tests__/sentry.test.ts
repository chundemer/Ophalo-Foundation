import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";

const initMock = vi.fn();
const captureExceptionMock = vi.fn();

vi.mock("@sentry/react", () => ({
  init: (opts: unknown) => initMock(opts),
  captureException: (e: unknown) => captureExceptionMock(e),
}));

async function loadSentry() {
  vi.resetModules();
  return import("../sentry");
}

beforeEach(() => {
  initMock.mockClear();
  captureExceptionMock.mockClear();
});

afterEach(() => {
  vi.unstubAllEnvs();
});

describe("initSentry", () => {
  it("is a no-op when VITE_SENTRY_DSN is absent", async () => {
    vi.stubEnv("VITE_SENTRY_DSN", "");
    const { initSentry } = await loadSentry();
    initSentry();
    expect(initMock).not.toHaveBeenCalled();
  });

  it("initializes errors-only, no-PII capture when a DSN is present", async () => {
    vi.stubEnv("VITE_SENTRY_DSN", "https://public@o0.ingest.sentry.io/1");
    const { initSentry } = await loadSentry();
    initSentry();

    expect(initMock).toHaveBeenCalledTimes(1);
    const opts = initMock.mock.calls[0][0];
    expect(opts.dsn).toBe("https://public@o0.ingest.sentry.io/1");
    expect(opts.sendDefaultPii).toBe(false);
    expect(opts.maxBreadcrumbs).toBe(0);
    expect(opts.tracesSampleRate).toBe(0);
    expect(opts.beforeBreadcrumb()).toBeNull();
    // Injected compile-time constants resolve to their test defaults.
    expect(opts.release).toBe(__SENTRY_RELEASE__);
    expect(opts.environment).toBe(__DEPLOY_ENV__);
  });

  it("routes outgoing events through the scrubber via beforeSend", async () => {
    vi.stubEnv("VITE_SENTRY_DSN", "https://public@o0.ingest.sentry.io/1");
    const { initSentry } = await loadSentry();
    initSentry();

    const beforeSend = initMock.mock.calls[0][0].beforeSend;
    const scrubbed = beforeSend({
      release: "r",
      environment: "production",
      user: { email: "owner@x.com" },
      request: { url: "https://app.example.com/request/abc?email=x#/frag" },
      exception: { values: [{ type: "Error", value: "customer Jane Doe" }] },
    });

    expect(scrubbed.user).toBeUndefined();
    expect(scrubbed.request.url).toBe("/request/abc");
    expect(scrubbed.exception.values[0].value).toBeUndefined();
  });
});

describe("captureHandledError", () => {
  it("forwards to Sentry.captureException", async () => {
    const { captureHandledError } = await loadSentry();
    const err = new Error("boom");
    captureHandledError(err);
    expect(captureExceptionMock).toHaveBeenCalledWith(err);
  });
});
