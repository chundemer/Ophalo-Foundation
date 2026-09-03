import { describe, it, expect } from "vitest";
import { resolveDeployment } from "../../scripts/resolveDeployment";

const SHA = "1a2b3c4d5e6f7a8b9c0d1e2f3a4b5c6d7e8f9a0b";
const CREDS = {
  VITE_SENTRY_DSN: "https://public@o0.ingest.sentry.io/1",
  SENTRY_AUTH_TOKEN: "tok",
  SENTRY_ORG: "ophalo",
  SENTRY_PROJECT: "workbench",
};
const FULL_PROD = { OPHALO_DEPLOY_ENV: "production", VERCEL_ENV: "production", VERCEL_GIT_COMMIT_SHA: SHA, ...CREDS };

describe("resolveDeployment", () => {
  it("classifies an unconfigured build as local development with maps off", () => {
    const r = resolveDeployment({});
    expect(r.deployEnv).toBe("development");
    expect(r.release).toBe("local");
    expect(r.sentryUploadEnabled).toBe(false);
  });

  it("never enables source-map upload for an unclassified local build, even with credentials present", () => {
    const r = resolveDeployment({ ...CREDS });
    expect(r.deployEnv).toBe("development");
    expect(r.release).toBe("local");
    expect(r.sentryUploadEnabled).toBe(false);
  });

  it("rejects an invalid explicit classification", () => {
    expect(() => resolveDeployment({ OPHALO_DEPLOY_ENV: "staging" })).toThrow(
      /Invalid OPHALO_DEPLOY_ENV="staging"/,
    );
    expect(() => resolveDeployment({ OPHALO_DEPLOY_ENV: "development" })).toThrow(
      /Allowed values: production, preview/,
    );
  });

  it("fails a classified production build when Vercel system variables are unavailable", () => {
    expect(() => resolveDeployment({ OPHALO_DEPLOY_ENV: "production" })).toThrow(
      /production build cannot proceed/i,
    );
  });

  it("fails a classified preview build when Vercel system variables are unavailable", () => {
    expect(() => resolveDeployment({ OPHALO_DEPLOY_ENV: "preview" })).toThrow(
      /preview build cannot proceed/i,
    );
    expect(() => resolveDeployment({ OPHALO_DEPLOY_ENV: "preview" })).toThrow(
      /VERCEL_GIT_COMMIT_SHA must be a non-local commit SHA/,
    );
  });

  it("fails when VERCEL_ENV does not match the classification", () => {
    expect(() =>
      resolveDeployment({ OPHALO_DEPLOY_ENV: "production", VERCEL_ENV: "preview", VERCEL_GIT_COMMIT_SHA: SHA, ...CREDS }),
    ).toThrow(/VERCEL_ENV must equal "production"/);
  });

  it("fails a classified production build on a local release SHA", () => {
    expect(() => resolveDeployment({ ...FULL_PROD, VERCEL_GIT_COMMIT_SHA: "local" })).toThrow(
      /non-local commit SHA/,
    );
  });

  it("fails a classified production build when Sentry upload configuration is missing", () => {
    expect(() =>
      resolveDeployment({ OPHALO_DEPLOY_ENV: "production", VERCEL_ENV: "production", VERCEL_GIT_COMMIT_SHA: SHA }),
    ).toThrow(/VITE_SENTRY_DSN/);
  });

  it("accepts a fully-configured production build", () => {
    const r = resolveDeployment(FULL_PROD);
    expect(r.deployEnv).toBe("production");
    expect(r.release).toBe(SHA);
    expect(r.sentryUploadEnabled).toBe(true);
  });

  it("accepts a preview build with valid Vercel env/SHA and no Sentry credentials", () => {
    const r = resolveDeployment({
      OPHALO_DEPLOY_ENV: "preview",
      VERCEL_ENV: "preview",
      VERCEL_GIT_COMMIT_SHA: SHA,
    });
    expect(r.deployEnv).toBe("preview");
    expect(r.release).toBe(SHA);
    expect(r.sentryUploadEnabled).toBe(false);
  });

  it("enables upload for a preview build that supplies complete credentials", () => {
    const r = resolveDeployment({
      OPHALO_DEPLOY_ENV: "preview",
      VERCEL_ENV: "preview",
      VERCEL_GIT_COMMIT_SHA: SHA,
      ...CREDS,
    });
    expect(r.release).toBe(SHA);
    expect(r.sentryUploadEnabled).toBe(true);
  });
});
