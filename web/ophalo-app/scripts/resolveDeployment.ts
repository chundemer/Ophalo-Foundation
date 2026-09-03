/**
 * Deployment classification for the workbench build (GAP-039, ADR-495).
 *
 * Pure and side-effect-free so `vite.config.ts` and its tests share one implementation.
 * `vite.config.ts` calls it with `process.env`; it throws when a classified build is
 * missing or contradicts a required signal.
 *
 * `OPHALO_DEPLOY_ENV` is an explicitly-configured, build-only classification variable
 * (Vercel Production = "production", Preview = "preview"). It is authoritative and does not
 * depend on Vercel "System Environment Variables" being enabled — so a disabled/missing
 * system-variable setting makes a *classified* build fail loudly rather than silently emit
 * a bundle tagged "local"/"development". `VERCEL_ENV` / `VERCEL_GIT_COMMIT_SHA` are system
 * variables that must corroborate the classification and supply the release SHA.
 *
 * Rules:
 *   - The only accepted explicit classifications are "production" and "preview"; any other
 *     non-empty value is rejected. Unset = an unclassified local build.
 *   - A classified build (production or preview) requires `VERCEL_ENV` to exist and exactly
 *     match the classification, and `VERCEL_GIT_COMMIT_SHA` to exist and not equal "local".
 *   - Production additionally requires the full Sentry upload configuration (DSN, auth
 *     token, org, project). Preview may omit all Sentry credentials.
 *   - Source-map upload is enabled only for a classified build with the complete upload
 *     configuration — never for an unclassified local build, even if the developer happens
 *     to have credentials in their shell.
 */

export interface DeploymentResolution {
  /** Sentry environment: "production" | "preview" | "development". */
  deployEnv: string;
  /** Release identifier — the commit SHA, or "local" for an unclassified local build. */
  release: string;
  /** True when a source-map upload is fully configured for a classified build. */
  sentryUploadEnabled: boolean;
  sentryUpload: {
    dsn?: string;
    authToken?: string;
    org?: string;
    project?: string;
  };
}

type Env = Record<string, string | undefined>;

const CLASSIFICATIONS = ["production", "preview"] as const;

export function resolveDeployment(env: Env): DeploymentResolution {
  const deployClass = env.OPHALO_DEPLOY_ENV;
  const vercelEnv = env.VERCEL_ENV;
  const releaseSha = env.VERCEL_GIT_COMMIT_SHA;

  const dsn = env.VITE_SENTRY_DSN;
  const authToken = env.SENTRY_AUTH_TOKEN;
  const org = env.SENTRY_ORG;
  const project = env.SENTRY_PROJECT;

  const isClassified =
    deployClass !== undefined && (CLASSIFICATIONS as readonly string[]).includes(deployClass);

  if (deployClass !== undefined && !isClassified) {
    throw new Error(
      `Invalid OPHALO_DEPLOY_ENV="${deployClass}". Allowed values: ${CLASSIFICATIONS.join(", ")}.`,
    );
  }

  if (isClassified) {
    const problems = [
      vercelEnv !== deployClass &&
        `VERCEL_ENV must equal "${deployClass}" (got "${vercelEnv ?? "unset"}" — enable Vercel 'System Environment Variables')`,
      (!releaseSha || releaseSha === "local") &&
        "VERCEL_GIT_COMMIT_SHA must be a non-local commit SHA",
      deployClass === "production" && !dsn && "VITE_SENTRY_DSN",
      deployClass === "production" && !authToken && "SENTRY_AUTH_TOKEN",
      deployClass === "production" && !org && "SENTRY_ORG",
      deployClass === "production" && !project && "SENTRY_PROJECT",
    ].filter(Boolean);
    if (problems.length > 0) {
      throw new Error(
        `${deployClass} build cannot proceed — missing or invalid: ${problems.join(
          ", ",
        )}. Configure these in the Vercel ${deployClass} environment.`,
      );
    }
  }

  const uploadConfigComplete = Boolean(dsn && authToken && org && project);

  return {
    deployEnv: isClassified ? (deployClass as string) : "development",
    release: isClassified ? (releaseSha as string) : "local",
    sentryUploadEnabled: isClassified && uploadConfigComplete,
    sentryUpload: { dsn, authToken, org, project },
  };
}
