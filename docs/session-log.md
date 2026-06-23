# Session Log — OpHalo Foundation

**Last updated:** 2026-06-23 (G8a pre-work complete; ready for Claude Code)
**Branch:** `main` (no remote yet)
**Current baseline:** 1224 tests (643 unit · 14 architecture · 567 integration).
**Next free ADR:** ADR-336
**Next batch: G8a — trusted client-IP/rate-limiter proof — READY TO CODE.**

---

## Session Protocol

This file is the current execution brief, not the historical build archive. Completed implementation
detail lives in `docs/build-log/`; authoritative decisions live in
`docs/decisions/decision-index.md`.

For every implementation slice:

- classify the work explicitly: discovery when pre-work is incomplete; mechanical implementation
  preflight when the current brief is marked pre-work complete;
- during preflight, use targeted `rg` only to confirm named signatures and enumerate compile-impact
  callers; do not rediscover already-locked architecture, scope, tests, or decisions;
- inspect current signatures and failure modes before calling existing code;
- always present the file-level gate before writing; when pre-work is complete it is a mechanical
  caller/signature gate, while incomplete pre-work also requires design decisions and approval;
- enforce the hard slice gate: at most 3 independent mutation handler families, 8 production files,
  and 12 total changed files including tests/fakes/docs; exact aliases sharing one handler/service
  count as one family only when all aliases are enumerated and parameterized-contract tested;
- preserve fail-closed account, row, action, and public-token behavior;
- add focused authorization/regression tests and run the proportionate broader suite;
- self-review for policy drift, accidental visibility expansion, untested direct-ID paths, stale
  documentation, and unrelated scope;
- commit only after Christian approves the completed diff.

## Pre-Session 6 Gap Resolution — IN PROGRESS

**Source of truth:** `docs/build-log/047-pre-session-6-phase-7-session-5-gap-audit.md`

**Required order:** G1 → G2 → G3 → G4 → G5 → G6 → G7 → G8. Do not validate or code Phase
8-B5 Session 6 until G8 records the final green gate.

### Completed gap references

- **G1 complete — 866 tests.** Account-safe schema, canonical phone identity, origin activity, and
  FirstResponseEvent FK. ADR-301–305; build-log/048.
- **G2 complete — 920 tests.** Shared intake validation, safe email preservation, and concurrent
  customer recovery. ADR-306–310; build-log/049.
- **G3 complete — 986 tests.** Intake-link ensure/status/replacement plus authenticated
  business-created requests. ADR-311–318; build-logs/050–051.
- **G4 complete — 1109 tests.** Row authorization, Available/list visibility, and shared action
  metadata policy. ADR-319–329; build-logs/052–055.

Historical Phase 8-B1 through B5 Session 5 completion detail is intentionally omitted here. Use
build logs 025–046 and ADR-084–294 when needed; do not reload those histories for G4 unless a
specific signature or locked behavior requires a targeted read.

---

## G4 — Shared Request-Row and Action Authorization — COMPLETE

G4 established account-wide, MyWork, ParticipationEntry, and Available row authorization; migrated
detail, mutation, list/count, and Available surfaces; and centralized action metadata in the shared
policy. Final G4 baseline: 1109 tests. Authoritative detail: ADR-319–329 and build-logs/052–055.

## G5 — Entity-Wide KeepRequest Optimistic Concurrency — COMPLETE

G5 added the opaque application-managed request version, strict mutation header contract, version
exposure, atomic rotation across request/event/participant/customer writes, stable stale/save-race
conflicts, and three two-context race proofs. Final G5 baseline: 1182 tests
(619 unit · 14 architecture · 549 integration). Commits: `c7435b2`, `2c8390a`, `7b4796f`,
`ab6399b`, `4998aab`, `19099e3`, `9c2df85`, `bb8010e`, `3fb154f`, `0a9d570`.
Authoritative detail: ADR-330–335 and build-log/056.

## G6 — Cancelled Customer-Page Expiry Correction — COMPLETE

**Finding:** GAP-011 · **Decision:** ADR-297 (implemented in part)
**Commit:** `c120702`

- `KeepRequest.ChangeStatus` sets `ExpiresAtUtc = nowUtc.AddDays(30)` on Cancelled transitions
  via a named `CancelledPageRetentionDays = 30` constant. Closed expiry deferred to Session 6.
- No persistence, service, API, or migration changes required; existing G5 commit path and
  `ExpiresAtUtc` mapping already handled atomicity and response exposure.
- 6 new tests: 2 unit (success + rejection), 2 integration status (success lifecycle + stale-409),
  2 integration customer-page (ADR-120 defensive non-terminal theory).
- Full suite: 1188 tests (621 unit · 14 architecture · 553 integration). build-log/057.

## G7 — Feedback Review Hardening — COMPLETE

**Findings:** GAP-017, GAP-018, GAP-020 · **Decisions:** ADR-300 reaffirms ADR-263; ADR-186 source note updated.

- **G7a — commit `a9e87bd`, baseline 1190.** Generic acknowledgement of `UnresolvedFeedback` now
  returns dedicated 409 `KeepRequest.AttentionRequiresFeedbackReview`; shared action policy removes
  the acknowledgement affordance.
- **G7b — commit `4f583d8`, baseline 1220.** Owner/Admin outbound external-contact logging permitted
  only on Closed + active `UnresolvedFeedback`. `HasActiveUnresolvedFeedbackReview` drives domain,
  service, policy, list, and detail affordances coherently. Operator/inbound/ordinary Closed/Cancelled
  remain blocked.
- **G7c — commit `623ab06`, baseline 1224.** `KeepRequestDetailMapper` split into `feedbackCommentVisible`
  (`Owner|Admin || FeedbackWasResolved == true`) and `reviewNoteVisible` (`Owner|Admin` only).
  Positive comments now visible to Operator/Viewer; `FeedbackReviewNote` remains Owner/Admin-only.
  4 new integration tests in `KeepRequestDetailB4Tests`. Ledger: ADR-263/ADR-300 marked Implemented,
  ADR-282 corrected to Deferred-Session-6, ADR-283/ADR-186 source notes updated, DEF-029/DEF-064
  marked Implemented. build-log/058.

Final G7 baseline: **1224 tests (643 unit · 14 architecture · 567 integration).**

## G8 — Edge Hardening, Token Safety, and Completion Gate — PRE-WORK IN PROGRESS

**Findings:** GAP-012, GAP-013, GAP-021. GAP-016 remains post-Session-6/pre-notification.

**Token-budget pilot:** Keep each Claude session to one G8 slice. Claude reads only the header,
the current G8 slice, and exact named files. Use targeted `rg`/small `sed`; report approximate
token use after preflight and after first implementation pass. No full suite unless the slice is
the G8 completion gate or Christian explicitly approves it.

### G8 split

- **G8a — trusted client-IP/rate-limiter proof:** production-like rate-limit test host, trusted
  proxy/IP resolver, spoofed forwarded-header rejection, 10/minute + zero-queue + partition tests.
- **G8b — bearer-token-safe logging proof:** application logging/tracing redaction/proof for public
  intake tokens and customer page tokens; Railway/Cloudflare access-log limits documented.
- **G8c — customer page-token at-rest decision + implementation, if chosen:** hash-plus-rotation /
  one-time disclosure, application encryption, or explicitly accepted retrievable storage.
- **G8d — ledger/final completion gate:** reconcile ADR/deferred statuses, migration-from-zero,
  build, focused/full tests, and final pre-Session-6 green gate.

### G8a — Trusted client IP / rate-limiter proof — READY TO CODE

**Purpose:** fix GAP-012. Untrusted peers must not choose rate-limiter partitions by spoofing
`CF-Connecting-IP` or `X-Forwarded-For`; public anonymous write routes must prove 10/minute,
`QueueLimit=0`, and 429 behavior in a production-like host where rate limiting is enabled.

**Likely files to create/modify:**

1. `src/OpHalo.Api/Program.cs`
   - Replace local `GetClientIp` trust of `CF-Connecting-IP` / `X-Forwarded-For`.
   - Add config-driven trusted-proxy behavior. Do not hardcode current Cloudflare IP ranges in code;
     read trusted proxy/CIDR configuration and document deployment values.
   - Add/allow a non-`Testing` production-like test environment (for example `RateLimitTesting`) so
     rate limiting runs under `WebApplicationFactory` while HTTPS redirect remains disabled for
     test-server requests.

2. New API helper under `src/OpHalo.Api/...` (name/location to confirm in preflight), e.g.
   `ClientIpResolver`.
   - Pure, unit-testable resolver for:
     - remote IP not trusted → ignore forwarded headers and use `RemoteIpAddress`;
     - trusted remote + `CF-Connecting-IP` valid → use CF value;
     - trusted remote + no CF + `X-Forwarded-For` valid → use first client IP;
     - malformed/blank headers → fall back safely;
     - unknown remote → stable safe partition such as `unknown`.

3. `tests/OpHalo.UnitTests/...` (new focused resolver tests)
   - Direct tests for trusted vs untrusted remote, CF precedence, XFF fallback, malformed headers,
     IPv4/IPv6 loopback/private examples, and fail-closed fallback.

4. `tests/OpHalo.IntegrationTests/Api/...` (new production-like rate-limit fixture)
   - A dedicated `WebApplicationFactory` variant that does **not** use environment `Testing` for
     rate limiting, but still avoids HTTPS redirect.
   - Prove public-intake route 11th request in one minute returns 429 with `QueueLimit=0`.
   - Prove partition isolation: different trusted client IPs or different customer page tokens do
     not share the same limiter bucket.
   - Prove spoof resistance: untrusted remote with different `CF-Connecting-IP` / `X-Forwarded-For`
     values still shares the remote-IP bucket and hits 429.

**Locked G8a decisions:**

- Use configuration key `Edge:TrustedProxyCidrs` for trusted proxy/network CIDRs. Empty/missing
  config means no forwarded headers are trusted.
- Do not hardcode Cloudflare IP ranges in code. Deployment supplies Cloudflare/Railway trusted
  network values; build-log/G8 docs record that code cannot verify external access-log behavior.
- Use `RateLimitTesting` as the production-like test environment. It should skip HTTPS redirect like
  `Testing`, but **must keep rate limiting enabled**.
- Trust loopback only in `RateLimitTesting` so TestServer can simulate a trusted proxy without
  relaxing production defaults.
- Keep existing policy names and limits: `public-intake`, `auth`, and `customer-write`; 10 requests
  per minute; `QueueLimit=0`.

### G8b draft — Token-safe application logging proof

**Purpose:** fix GAP-013 for application-controlled logs/traces. Public intake tokens and customer
page tokens are bearer secrets in route segments.

**Known current state:**

- App does not call `UseHttpLogging` or explicit request logging, but default framework logs and
  future friction/reporting hooks can still accidentally capture paths.
- Public routes are `/keep/public-intake/token/{publicIntakeToken}` and `/keep/r/{pageToken}...`.
- Cloudflare/Railway edge access logs cannot be fully proven by code; document required config and
  accepted residuals.

**Likely work:**

- Add a small redaction helper/policy for public-token paths.
- Add an integration/log-capture proof that application logs for public token routes do not contain
  raw tokens.
- Document deployment-side access-log expectations in build-log/G8 docs.

**Open G8b decision:** decide whether to suppress framework request-path logs, redact route values,
or both. Do not build a broad logging platform.

### G8c decision — Customer page-token at-rest protection — LOCKED

Current state: `KeepRequest.PageToken` is stored retrievably and authenticated detail returns it so
staff can re-share the customer link. Public intake links already store only `TokenHash`, but page
tokens are different because they are intentionally re-shareable from staff detail.

Decision: **Option 3 — documented accepted retrievable storage for pilot.**

- Keep raw `KeepRequest.PageToken` storage for now because authenticated staff re-sharing is a
  locked workflow.
- Do not add hash-only lookup, one-time disclosure, token rotation, application encryption, key
  management, or migration in G8.
- G8 must still prove token-safe application logs/traces/errors and document accepted residual
  risks, including database read exposure and Cloudflare/Railway access-log limitations.
- Defer stronger page-token at-rest protection to a dedicated future access-link management/security
  slice.

### G8 exclusions

G8 explicitly excludes notifications, realtime, frontend UI, Spam/Test implementation, Turnstile,
SMS verification, adaptive bot challenges, source blocking, and a general abuse platform.

---

## Active Carry-Forward Boundaries

- One durable public intake link; no ordinary pause/resume. Exceptional replacement is
  transactional and warns about stale links.
- Public-intake abuse posture before pilot: bounded validation, trusted-IP/rate-limit proof,
  Spam/Test classification in its planned V1 slice, token-safe logs, and internal emergency path.
- Hashed-source blocking, adaptive bot challenges, anomaly detection, and phone verification remain
  post-pilot/V1.1 unless evidence requires earlier work.
- Terminal customer pages expire 30 days after Closed/Cancelled; active and Resolved do not.
- Mobile is the on-the-road Operator surface; PWA is the Owner/Admin operational surface. Backend
  authorization remains identical regardless of client.
- No platform SMS is sent until consent/compliance posture is reviewed.
- Keep remains fresh-not-realtime for V1: refetch after writes, focus/resume sync, pull-to-refresh,
  bounded polling, and later push for urgent off-screen work.

## Operational Watch-Outs

- No GitHub remote is configured.
- Integration tests reset PostgreSQL schema and run migrations.
- Testing environment intentionally skips runtime rate limiting; G8 must use a production-like host.
- Deployment requires correct Cloudflare/Railway trusted-proxy and token-redaction configuration.
- Persistent local PostgreSQL setup/migration/smoke/reset runbook remains GAP-016 after Session 6 and
  before notifications.
