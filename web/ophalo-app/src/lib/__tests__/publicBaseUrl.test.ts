import { describe, it, expect, vi, afterEach } from "vitest";

// The module reads `import.meta.env.VITE_PUBLIC_BASE_URL` once at load time, so each case
// stubs the value and re-imports.
async function load(value: string) {
  vi.resetModules();
  vi.stubEnv("VITE_PUBLIC_BASE_URL", value);
  return import("../publicBaseUrl");
}

afterEach(() => {
  vi.unstubAllEnvs();
});

describe("publicBaseUrl accessor", () => {
  it("accepts a valid https origin and strips the trailing slash", async () => {
    const m = await load("https://app.example.com/");
    expect(m.publicBaseUrlResult).toEqual({ ok: true, value: "https://app.example.com" });
    expect(m.getPublicBaseUrl()).toBe("https://app.example.com");
  });

  it("preserves a configured base path without a trailing slash", async () => {
    const m = await load("https://example.com/keep/");
    expect(m.publicBaseUrlResult).toEqual({ ok: true, value: "https://example.com/keep" });
  });

  it("reports an empty value as missing without throwing", async () => {
    const m = await load("");
    expect(m.publicBaseUrlResult).toEqual({ ok: false, reason: "missing" });
    expect(m.getPublicBaseUrl()).toBe("");
  });

  it("reports a non-URL value as malformed", async () => {
    const m = await load("not a url");
    expect(m.publicBaseUrlResult).toEqual({ ok: false, reason: "malformed" });
    expect(m.getPublicBaseUrl()).toBe("");
  });

  it("rejects a non-http(s) scheme as malformed", async () => {
    const m = await load("ftp://example.com");
    expect(m.publicBaseUrlResult).toEqual({ ok: false, reason: "malformed" });
  });
});
