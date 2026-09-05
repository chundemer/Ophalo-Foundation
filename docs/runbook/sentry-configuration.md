# Sentry Configuration Runbook

**Purpose:** Maintain the production errors-only Sentry configuration for OpHalo without placing
credentials in the repository. This runbook implements the operational requirements in
[ADR-495](../decisions/ADR-495-gap-039-redacted-error-capture-and-release-safety.md).

## Scope and safety boundary

Sentry is used only for unhandled API and authenticated Workbench PWA errors. Do **not** enable
session replay, tracing, profiling, logs, metrics, user identity, or behavioral analytics as part
of this configuration. The API and browser SDKs enforce a redacted allowlist; detailed diagnosis
remains in Railway and Vercel logs.

Never record DSNs, auth-token values, session cookies, account data, or alert-recipient addresses
in this file, a commit, an issue, or a chat transcript.

## Sentry projects

| Component | Sentry project slug | Platform | DSN destination |
| --- | --- | --- | --- |
| API | `ophalo-api` | ASP.NET Core | Railway `Sentry__Dsn` |
| Authenticated Workbench PWA | `workbench-pwa` | React | Vercel `VITE_SENTRY_DSN` |

Sentry organization slug: `ophalo`.

Keep the two projects separate. A DSN belongs to one project; do not reuse an API DSN in the
browser or a Workbench DSN on the API.

## Railway API configuration

Service: **Ophalo-Foundation-API**, Production environment.

| Setting | Required value | Notes |
| --- | --- | --- |
| Variable | `Sentry__Dsn` | API project DSN. This is a deployment secret. |
| Healthcheck Path | `/health/ready` | Enter the path only, not `https://api.ophalo.com/health/ready`. |
| Environment | `ASPNETCORE_ENVIRONMENT=Production` | Existing production setting. |
| Release | `RAILWAY_GIT_COMMIT_SHA` | Railway-provided commit SHA; do not override. |

The API fails startup outside local/test environments if `Sentry__Dsn` is missing or malformed.
After a configuration change, verify the new deployment becomes healthy. The public manual check
is `https://api.ophalo.com/health/ready`; it should return HTTP 200 and `{"status":"healthy"}`.

Railway's healthcheck is a **deploy-time** gate, not continuous monitoring. It polls the configured
path only while deciding whether a new deployment can receive traffic.

## Vercel Workbench PWA configuration

Project: **ophalo-foundation-app**, Production environment.

Enable **Environment Variables → Enable access to System Environment Variables**. The build
requires `VERCEL_ENV` and `VERCEL_GIT_COMMIT_SHA`, supplied by Vercel.

| Variable | Vercel type | Production value |
| --- | --- | --- |
| `VITE_SENTRY_DSN` | Config | Workbench PWA project DSN. It is browser-public by design. |
| `SENTRY_AUTH_TOKEN` | Secret | Sentry organization token with `org:ci`; used only at build time. |
| `SENTRY_ORG` | Config | `ophalo` |
| `SENTRY_PROJECT` | Config | `workbench-pwa` |
| `OPHALO_DEPLOY_ENV` | Config | `production` |

The token should be named descriptively (currently `vercel-source-maps`) and rotated by creating a
replacement organization token, updating Vercel, deploying successfully, then revoking the old
token in Sentry.

`OPHALO_DEPLOY_ENV=production` is intentional fail-closed protection: it requires every variable
above, `VERCEL_ENV=production`, and a non-local `VERCEL_GIT_COMMIT_SHA`. A failed production build
after changing one of these settings is a configuration problem to correct, not a setting to bypass.

Preview is optional. If preview error capture is wanted, set `OPHALO_DEPLOY_ENV=preview` and a
Preview `VITE_SENTRY_DSN`; add the remaining Sentry variables only when preview source-map uploads
are also wanted. Preview events must remain in the `preview` Sentry environment.

## Deployment and verification checklist

1. Deploy the API and Workbench PWA from the same commit.
2. Verify Railway `/health/ready` is healthy and its deployment is marked healthy.
3. In Vercel build logs, confirm the Sentry Vite plugin uploaded release-matched source maps and
   deleted `.map` artifacts.
4. Confirm deployed JavaScript does not expose `.map` files or a `sourceMappingURL` comment.
5. In each Sentry project, create founder-email alerts for **new issue** and **regression**.
6. Use only the internal smoke account to run the production smoke test documented in
   [production-smoke-test.md](production-smoke-test.md).
7. Perform controlled API and PWA error tests, then verify release, environment, redaction, alert
   delivery, and source-map resolution as required by ADR-495.

## Changing configuration safely

- **DSN rotation:** Create a new client key in the matching Sentry project, update only its target
  platform variable, deploy and verify, then disable the old key.
- **Upload-token rotation:** Follow the token rotation sequence above. Never place the token in a
  `VITE_` variable.
- **Project slug or organization-slug change:** Update `SENTRY_ORG` and/or `SENTRY_PROJECT` in
  Vercel before the next production deploy. A project rename that changes its slug also requires
  checking the Workbench DSN and alert rules.
- **Healthcheck change:** Keep the Railway field as a path beginning with `/`; verify the deployed
  public endpoint separately. Do not paste a full URL into Railway's Healthcheck Path field.
- **Unexpected build failure:** Preserve the Vercel build log, confirm all five Production variables
  and system variables are present, then correct the missing/incorrect setting. Do not remove
  `OPHALO_DEPLOY_ENV` merely to make a production build pass.

## Incident response

For a new Sentry alert: inspect the release and environment first, copy the API correlation ID when
present, then search Railway logs for that ID. If errors coincide with a release and impact users,
rollback the API or PWA deployment first; investigate and fix forward after service is restored.
Record the incident outcome without recording protected event data or credentials.
