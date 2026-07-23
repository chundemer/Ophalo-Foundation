# Production Smoke Test — GAP-039c / session 0.5

Repository-owned script: `scripts/production-smoke-test.mjs`. No dependencies — plain
Node 20+ (uses the built-in `fetch`). Run it from the repo root.

It checks, in order: `/health/live`, `/health/ready`, triggering `/auth/signin`, then
(if a session is available) `/auth/me` and `/keep/requests`. Each check prints
`✓ pass`, `✗ fail`, or `– skip`, and the process exits `0` only if nothing failed
(skips do not fail the run).

This uses the **dedicated internal smoke account** provisioned in session 0.1 — never a
pilot/customer account.

## Required environment

| Variable | Example | Notes |
|---|---|---|
| `SMOKE_API_BASE_URL` | `https://api.ophalo.com` | No trailing slash needed. |
| `SMOKE_ACCOUNT_EMAIL` | the smoke account's email | Used only to trigger `/auth/signin`. |

## Two modes

### Routine mode (default) — for regular checks after a deploy

Uses a **stored session cookie** for the smoke account instead of reading real email
each time. Set `SMOKE_SESSION_COOKIE` to the raw `ophalo.sid` cookie value.

```bash
SMOKE_API_BASE_URL=https://api.ophalo.com \
SMOKE_ACCOUNT_EMAIL=smoke-inbox@example.com \
SMOKE_SESSION_COOKIE=<raw ophalo.sid value> \
node scripts/production-smoke-test.mjs
```

**Obtaining/refreshing the cookie:** sign in as the smoke account in a browser (normal
`/signin` flow), then copy the `ophalo.sid` cookie value from DevTools → Application →
Cookies. Per ADR-378 the session is valid for 60 days from creation (30-day inactivity
window), so this only needs refreshing roughly every couple of months, or immediately
if `auth/me`/`keep/requests` start failing with `401`.

**Keep `SMOKE_SESSION_COOKIE` out of the repo.** Store it only in your local shell
environment or a local `.env` file that is gitignored — never commit it, never paste it
into a PR, issue, or chat log.

### Full end-to-end mode — occasional, proves email delivery + exchange

Pass `--exchange-code=<code>` with a real code copied from the magic-link email just
sent to the smoke inbox. This proves the full path: signin trigger → email delivery →
`/auth/exchange` → session → authenticated calls — not just the routine path.

```bash
SMOKE_API_BASE_URL=https://api.ophalo.com \
SMOKE_ACCOUNT_EMAIL=smoke-inbox@example.com \
node scripts/production-smoke-test.mjs --exchange-code=<code from the email>
```

Steps:
1. Run the script once without `--exchange-code` (or trigger sign-in manually) to send
   a fresh magic link to the smoke inbox.
2. Open the smoke inbox, copy the `code=` query value from the magic-link URL
   (`.../auth/exchange?code=<code>`).
3. Re-run the script with `--exchange-code=<code>` immediately — sign-in codes expire
   24 hours after issue and are single-use (a prior code is invalidated once a new one
   is issued for the same account).

If `--exchange-code` is supplied, its freshly-exchanged session takes priority over
`SMOKE_SESSION_COOKIE` for that run.

## When to run it

- After every production deploy (routine mode).
- Immediately if GAP-039's health/error-capture alerting fires.
- Full end-to-end mode periodically (e.g. monthly) or whenever email delivery
  configuration changes (Resend key/from-address/DNS), per the GAP-039a deployment
  notes in `docs/session-log.md`.

## Reading the result

- All `✓`, exit `0`: healthy.
- Any `✗`: investigate before treating the deploy as good — do not silently retry.
- `–` (skip) on `auth/me`/`keep/requests` just means no session was available for this
  run (neither `SMOKE_SESSION_COOKIE` nor `--exchange-code` was supplied) — it is not a
  failure signal by itself, but routine deploy checks should supply a session so these
  aren't silently skipped indefinitely.
