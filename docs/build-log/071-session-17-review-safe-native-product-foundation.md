# Build Log 071 — Session 17: Review-Safe Native Product Foundation

**Started:** 2026-07-03
**Status:** S17j complete; Session 17 done
**Session name:** s17 discussion, lock in, and final pass
**Next free ADR before this log:** ADR-396
**Next free ADR after this log:** ADR-406

---

## Purpose

Session 17 builds the native OpHalo Keep field workflow after the Session 16 mobile foundation.
The locked frame is:

> Build a review-safe native product foundation, not a "good enough for pilot" mobile shell.

S17 must produce a real on-the-road work app for Owner/Admin/Operator roles while preserving Apple
App Store and Google Play review posture. S19 owns store submission mechanics, signing, screenshots,
privacy labels, Universal/App Links, reviewer credentials, and final reviewer notes, but S17 must
not build product/auth/navigation choices that force rework before review.

Primary review sources used during the decision pass:
- Apple App Review Guidelines:
  `https://developer.apple.com/app-store/review/guidelines/`
- Apple account deletion guidance:
  `https://developer.apple.com/support/offering-account-deletion-in-your-app/`
- Apple user privacy and data use:
  `https://developer.apple.com/app-store/user-privacy-and-data-use/`
- Google Play review prep:
  `https://support.google.com/googleplay/android-developer/answer/9859455`
- Google Play user data policy:
  `https://support.google.com/googleplay/android-developer/answer/10144311`
- Google Play account deletion policy:
  `https://support.google.com/googleplay/android-developer/answer/13327111`
- Google Play functionality/content/user-experience policy:
  `https://support.google.com/googleplay/android-developer/answer/9898783`
- Google Play spam policy:
  `https://support.google.com/googleplay/android-developer/answer/9899034`

---

## Final Consistency Pass

The final S17 decision pass found no blocking contradictions after revision. The only material
correction from the discussion was account deletion: a generic support/email link is not sufficient
for Apple review. S17 may expose `Request Account Deletion`, but store submission is blocked until
S19 provides a direct compliant deletion web resource or equivalent flow.

Explicit S19 blockers carried forward:
- Seeded reviewer account credentials, inbox/access mechanics, and review notes.
- Direct account-deletion web resource or equivalent compliant deletion request flow.
- Universal Links/App Links, domain association files, entitlements, signing profiles, and store
  deep-link verification.
- Store assets, privacy labels, screenshots, production config verification, and final review notes.
- Deployed-service topology proof for auth/web/API/native handoff.

Explicit S18 carry-forward:
- Push permission, real APNs/FCM token capture, provider integration, delivery, notification payload
  verification, and push deep links.

---

## Locked Decisions

### ADR-396 — Reviewer access uses a seeded real account, not demo mode

Store review access will use a seeded real reviewer account against the deployed backend. S17 must
not add demo mode, local-only reviewer bypass, or dev-token auth for review builds. S19 owns reviewer
credentials, inbox/code mechanics, and review notes.

Reason: seeded review access proves the real backend, auth, role policy, and workflows. Demo mode
adds a second product path that can drift from production and create review/security risk.

### ADR-397 — Custom scheme continues for S17 with Universal/App Link-compatible routes

S17 continues using `ophalo://` custom-scheme links for auth handoff and request detail navigation.
Route names, path slugs, and query parameters must be shaped so S19 can add Universal/App Links
without rewriting product workflow code.

Rules:
- URL parsing must not hardcode `ophalo://`; use Expo Router params or Expo Linking parsing.
- Parameter names must mirror exactly across current custom-scheme and future web links, e.g.
  `?code=...` remains `?code=...`.
- Native route slugs must be lowercase, such as `requests/[id]`.

S19 owns domain association files, entitlements, signing, and store link configuration unless S17a
discovers a store-review blocker.

### ADR-398 — S17 requests zero platform permissions

S17 requests no Contacts, Location, SMS, Call Log, Camera, Microphone, Tracking, or Push
permissions. Contact actions use operator-initiated `tel:`, `sms:`, `mailto:`, copy, and native
share surfaces without protected device permissions.

Manual photo upload and manual arrived-at-location are legitimate future operator features, but they
are deferred out of S17. Future Camera or Location features must be explicit, operator-initiated, and
must not use background collection.

### ADR-399 — Account utility surface satisfies review access without becoming mobile settings

S17 Account is a grouped utility/review surface, not mobile settings. It includes:
- signed-in user/account context and role;
- Privacy Policy and Support links;
- `Request Account Deletion`;
- Logout;
- OpHalo Keep Mobile app/build metadata and environment/status information where useful.

S17 Account does not include team management, billing, business profile editing, intake-link
management, role management, notification preferences, account creation, or invite acceptance.

`Request Account Deletion` is config-gated in S17: mobile renders/opens it only when a configured
direct OpHalo account-deletion web resource or equivalent compliant flow exists. The config value is
`EXPO_PUBLIC_ACCOUNT_DELETION_URL`; if it is missing, empty, invalid, non-HTTPS, or not an approved
OpHalo deletion route, the UI row is omitted entirely. S17 must not ship a placeholder deletion URL,
a generic support link, disabled/masked row, or email-only deletion fallback. Store submission
remains blocked until S19 provides and verifies the compliant deletion URL/config. Mobile does not
implement deletion cascade logic in S17.

Support is also config-gated unless S17/S19 provides a known existing support route. The config value
is `EXPO_PUBLIC_SUPPORT_URL`; if it is missing, empty, invalid, non-HTTPS, or not an approved OpHalo
support route, the Support row is omitted entirely. Mobile must not invent a support path that is not
served by `ophalo-web`.

### ADR-400 — Native field workflow scope and navigation

S17 native V1 is the phone-first, on-the-road Keep operator workflow. Owners/Admins may use it, but
the UX is optimized for active field execution.

Primary navigation:
- `My Work`
- `Available`
- `Capture` as a tab-bar action that presents a modal/sheet, not a persistent tab screen
- `Account`

Request detail is a pushed lowercase stack route, such as `requests/[id]`.
Any authenticated route outside the `(tabs)` layout must have an explicit auth guard before it
renders content or fetches data. Deep-linkable root-stack routes such as `requests/[id]` do not
inherit the `(tabs)` redirect and must guard themselves or use a shared authenticated layout.

`My Work` contains a top segmented control:
- `My Promises` uses the assigned/responsible-to-me backend view or exact existing equivalent found
  in S17a preflight.
- `Watching` uses the watching backend view or exact existing equivalent.

`Available` uses the existing available/unassigned self-assign surface. S17 must not fetch a broad
account-wide blob and locally merge/filter it for the native lists.

Every list and primary tab must have real functionality or an intentional data-backed empty state.
No placeholder, blank, fake, or "coming soon" authenticated surfaces may remain by S17 close.

### ADR-401 — Native workflow actions are explicit, server-driven, and customer-safe

S17 includes:
- native Quick Capture over existing `GET /keep/requests/lookup?phone=...` and `POST /keep/requests`;
- current public intake link sharing for busy field workers, if a safe read path exists;
- request detail read surface;
- operator-initiated contact launch actions;
- explicit external-contact logging;
- tracker handoff and explicit mark-shared/share-intent recording;
- customer-visible update composer;
- `Send Update & Mark Completed`;
- Watch/Mute/Unmute;
- self-assign from Available;
- Follow Up / Planned For display and controls where backend actions permit.

Rules:
- Quick Capture uses existing source slugs/validation, staff-created `NeedsShare=true`, and post-save
  navigation to detail/tracker handoff.
- Public intake link sharing is separate from Quick Capture. Mobile may copy/share the current
  customer-facing intake URL, but it cannot create, replace, rotate, pause, or edit that link.
  If S17a finds no safe read contract, S17f ships Quick Capture only. Public intake sharing becomes
  a separately approved, pre-pilot gap slice requiring a narrow read-only backend contract and
  focused integration tests before it can land.
- Contact launch opens OS surfaces only. It does not log contact, clear attention, count first
  response, or prove customer communication. Native may show a contextual return-to-app prompt, but
  mutation requires explicit operator confirmation through the existing external-contact flow.
- Tracker handoff shares only the customer tracker URL. Opening an OS composer/share sheet alone
  must not clear `NeedsShare`; existing `share-intent`/mark-shared is recorded only after explicit
  operator confirmation/action.
- Customer updates use `POST /keep/requests/{id}/business-updates` with `X-Keep-Request-Version`,
  `availableActions.canSendBusinessUpdate`, and backend-provided active status choices.
- `Mark Completed` is implemented as `Send Update & Mark Completed`, using the customer-visible
  business-update/status contract with `setStatus: "resolved"` only when backend actions/statuses
  allow it. It is not a silent one-tap status flip.
- Watch/Mute/Self-Assign controls render from current backend capability metadata and current-user
  participation state. Native must not duplicate role/status/assignment rules client-side.
- Self-assign is not purely optimistic. It disables the button while submitting, sends
  `X-Keep-Request-Version`, waits for server confirmation, and handles already-claimed conflicts by
  refetching/removing the row and showing a clear message.
- Follow Up / Planned For use existing endpoints and `X-Keep-Request-Version`. No calendar
  integration, scheduled-work filtering, notification timing rules, or new timing endpoints are
  introduced in S17.

### ADR-402 — Native auth, role access, session lifetime, and logout

S17 mobile sessions use the existing durable bearer session policy from ADR-378/S16 unless S17a
discovers a concrete security requirement to differ. Mobile stores accepted bearer tokens in
SecureStore, authenticates API calls with Bearer auth, and relies on normal backend expiry and
revocation.

Owner/Admin/Operator are the only supported native roles. The UI is operator-first, but Owners/Admins
are allowed because they often perform field work. Viewer/unknown roles are rejected before the
authenticated tab shell is shown.

During mobile handoff redemption and auth bootstrap, native validates `/auth/me` before saving or
retaining a local session. If the role is Viewer/unknown/ineligible, native drops or revokes the
local token, clears session state, and shows a dedicated `Mobile Access Restricted` screen.

Logout uses optimistic local wipe with best-effort server revocation. On logout tap, native starts
device/session revocation without blocking the UI, immediately deletes the bearer token from
SecureStore, clears local query/cache state, resets auth state, and returns to sign-in. Server
revocation failures must not trap the user inside an authenticated UI.

Authenticated API calls handle `401 Unauthorized` globally. A 401 on an authenticated request clears
the SecureStore bearer token, resets auth/query state, and returns the user to sign-in or the
appropriate restricted-access screen. Expired or revoked tokens must not leave the user stranded in a
partially hydrated authenticated shell.

S17c must remove the dev-token paste field from `mobile/ophalo-mobile/app/signin.tsx`. Review and
production builds must expose only the real magic-link sign-in path. Any future local-only developer
bootstrap must be reintroduced outside the review-bound sign-in UI and explicitly documented before
use.

### ADR-403 — S17 is online-first with cached reads and blocked offline writes

S17 is online-first. It may render recently fetched My Work, Available, badge, and request detail
data from runtime TanStack Query cache with a visible cached/offline indicator. It does not add
durable offline storage or mutation replay.

All business mutations require live server confirmation. When native network state reports offline,
mutation buttons render disabled with a connection-required state. If the OS appears online but an
HTTP request fails or times out, native preserves typed form input, releases loading state, and shows
a non-destructive retry message.

Mutation failure handling distinguishes server responses from no-response network failures. HTTP
responses follow the structured API error path. Connection drops, fetch/network failures, DNS
failures, request aborts, and timeouts may have no HTTP status; these preserve component-local form
state, avoid optimistic cache writes, release loading state, and show retry-safe copy. Draft mutation
inputs stay in the local screen/component state until server confirmation and are not committed to
global query cache or durable offline storage.

When connectivity returns or the app regains foreground focus, native invalidates/refetches active
work queries and `GET /me/badge`.

Logout is the only offline optimistic local mutation exception because local device data protection
wins over server confirmation.

### ADR-404 — Push is deferred; S17 freshness uses polling and app-resume sync

S17 does not request push notification permission, capture APNs/FCM tokens, or implement real push
delivery. It keeps S16 device registration with `pushToken` omitted/null and uses existing
`GET /me/badge`, active query polling, and `AppState` foreground/resume invalidation.

S18 owns push permission, token capture, provider selection, APNs/FCM delivery, push payloads, and
push/deep-link verification.

### ADR-405 — S17 is client-first with a genuine local E2E test harness

S17 reuses existing API contracts by default. New backend endpoints, DTO fields, or authorization
changes require explicit gap evidence from S17a preflight and documentation before implementation.
Any backend change must preserve fail-closed account scoping, role policy, action metadata,
optimistic concurrency, public-token safety, and automated integration test posture.

Known possible gap:
- Public intake link sharing may require a narrow read-only current-intake-url endpoint if existing
  setup endpoints are Owner/Admin-only or expose management semantics. Such an endpoint must derive
  `AccountId` from auth, return only the shareable public intake URL, expose no management fields,
  and prove Owner/Admin/Operator allowed plus Viewer/unauth denied in integration tests.

S17a must establish a genuine local account/mobile handoff test path before feature coding. Preferred
path: create the account through `ophalo-web`, capture the real magic link through local email/log
infrastructure, complete web account creation, then sign into mobile through the S16 handoff. Local
seed scripts are allowed only as a dev fallback/test fixture, never as reviewer/demo product behavior.

Every implementation slice must pass:
- static gate: mobile TypeScript must be clean from S17b onward; lint/build checks run as applicable;
- contract gate: real endpoint/payload alignment or focused integration tests;
- manual mobile workflow gate: simulator/device verification against the local stack with a real
  account where feasible.

---

## Explicit Non-Goals For S17

- Demo mode, local-only reviewer bypass, or production-visible dev auth.
- Native account creation or invite acceptance.
- Owner/Admin dispatch to other users.
- Team management, billing, reporting, account settings, intake-link management, or role management.
- Negative feedback review, Spam/Test classification, closed history, broad search/reporting, or
  admin queues.
- Backend SMS/email to customers.
- Native contact book import, background tracking, attachments, camera, location, microphone, push
  token capture, or any protected device permission.
- Sharing internal request summaries externally.
- Offline mutation queues or replay.
- Universal/App Links, EAS/signing/store submission assets, or store screenshots.

---

## Locked Implementation Slice Order

Slice dependencies are locked as part of the order. S17b and S17c must be complete before S17d
renders authenticated lists. S17e must establish the stable lowercase request-detail route before
S17f post-save navigation and before S17g/S17h/S17i action workflows. Later-slice action affordances
must not appear as fake or tappable placeholders in earlier slices.

### S17a — Decisions, Preflight, And E2E Harness

Goals:
- Record locked decisions and update docs/ADR index.
- Inspect exact current endpoint names, view names, DTO fields, mobile routes, and test coverage.
- Confirm the `X-Keep-Request-Version` conflict contract once for version-checked endpoints,
  including status code, structured JSON error payload shape, stable error code fields, and the
  typed mobile API-client parsing approach. Mobile must not rely on regex parsing of error text for
  already-claimed/conflict behavior.
- Establish the genuine local account/mobile handoff runbook.
- Identify whether public intake link sharing needs a backend gap slice.

No production implementation in S17a beyond docs/preflight notes.

### S17b — App Infrastructure And Navigation

Goals:
- Start from the S16 infrastructure already in place: QueryClient, AppState focus wiring, badge
  polling, SecureStore bearer auth, role blocking, and nullable-token device registration.
- Fill only the S17b delta: network/offline state, route shape, Account shell, and Capture
  modal/sheet wiring.
- Temporary auth/layout stubs, if needed, must be centralized and disposable. Screens must not embed
  mock auth logic that has to be hunted down later.

### S17c — Auth Cleanup And Role Gate

Goals:
- Remove dev-token auth from the sign-in UI.
- Validate mobile handoff and `/auth/me` before persisting local sessions.
- Reject Viewer/unknown roles before the tab shell.
- Implement offline-safe logout local wipe.

### S17d — Lists

Goals:
- My Work segmented into My Promises and Watching.
- Available work list.
- Intentional empty states, runtime cached read posture, badge/resume refresh.

An intentional data-backed empty state is a successful real query returning zero rows, rendered with
clear empty copy and current refresh/cached/offline context where available. It is not a placeholder
screen, static mock, or "coming soon" state.

### S17e — Request Detail Read Surface

Goals:
- Stable lowercase detail route.
- Field-focused request details, timeline/core fields, action metadata, contact/tracker affordances.

S17e may expose read-only action metadata, but controls owned by S17g/S17h/S17i must be absent or
disabled until their implementing slice ships and backend capability metadata allows them. No tappable
future-action placeholders are allowed.

### S17f — Quick Capture And Public Intake Share

Goals:
- Capture modal/sheet, phone lookup, create request, post-save detail navigation.
- Current public intake link share only if S17a proves a safe read contract exists.
- If no safe read contract exists, S17f ships Quick Capture only. Public intake sharing becomes a
  separately approved pre-pilot gap slice with a narrow read-only endpoint and focused integration
  tests.

### S17g — Contact And Tracker Handoff

Goals:
- OS contact launch, return-to-app contact log prompt, explicit external-contact logging.
- Customer tracker handoff and explicit mark-shared/share-intent confirmation.

### S17h — Customer Update And Mark Completed

Goals:
- Customer-visible update composer.
- `Send Update & Mark Completed` with customer-visible resolution message and conflict handling.

### S17i — Participation And Timing

Goals:
- Watch/Mute/Unmute.
- Race-safe self-assign from Available.
- Follow Up / Planned For display and controls.

### S17j — Review-Safe Final Pass

Goals:
- No placeholder authenticated tabs/screens.
- No unexpected permissions.
- No production-visible dev auth.
- Account Privacy/Support/Request Deletion links present.
- Route/deep-link guardrails verified.
- Mobile TypeScript checks and targeted API tests for any backend-touched contracts.
- Manual workflow smoke notes recorded.

---

## S17a Preflight Findings

Inspected during Session 17 start. No production code was changed in S17a.

### Current Mobile App Baseline (S16 output)

**Routes:**
- `(tabs)/index` — placeholder "Requests" screen showing badge count from `useBadge`
- `(tabs)/account` — Account screen: role, accountUserId, Sign out button
- `signin` — magic link sign-in + `__DEV__` bearer paste field (S17c removes this)
- `auth/callback` — mobile handoff redeem; reads `?code=` via Expo Router params; calls `storeToken`
- `modal` — generic placeholder modal
- `+not-found` — not-found fallback
- Stack registered: `(tabs)`, `signin`, `auth/callback`, `modal`
- Custom scheme: `ophalo://` (app.json `scheme: "ophalo"`)

**Infrastructure in place:** QueryClient + AppState focusManager, badge polling (`staleTime: 30s`, `refetchInterval: 60s`), SecureStore bearer auth, `AuthContext` (bootstrap, storeToken, logout best-effort revocation), role gate (Owner/Admin/Operator only), nullable-token device upsert.

**API client (`src/api/client.ts`):** `get`, `post`, `put`, `delete`. `post` defaults `authenticated: false`; callers pass `true` for authenticated mutations. `ApiError(status, rawTextBody)` — body is not JSON-parsed.

### Endpoint Catalog Confirmed For S17

All routes are at `OpHalo.Api` (`http://localhost:5092`).

| Method | Path | Notes |
|--------|------|-------|
| GET | `/keep/requests?view=assigned_to_me` | My Promises (Owner/Admin/Operator) |
| GET | `/keep/requests?view=watching` | Watching (Owner/Admin/Operator) |
| GET | `/keep/requests/available?limit=&cursor=` | Available work (Operator accessible; limit 1–50) |
| GET | `/keep/requests/lookup?phone=...` | Quick Capture phone lookup |
| POST | `/keep/requests` | Create request (Quick Capture); no version header |
| GET | `/keep/requests/{requestId}` | Request detail; returns `Version`, `AvailableActions`, `CurrentUserParticipation`, `ContactActions`, `Events`, `Validation`, `PageToken`, `NeedsShare` |
| PATCH | `/keep/requests/{requestId}/status` | Change status; `X-Keep-Request-Version` required |
| POST | `/keep/requests/{requestId}/business-updates` | Send customer update; `X-Keep-Request-Version` required; body: `{ message, setStatus? }` |
| POST | `/keep/requests/{requestId}/external-contact` | Log external contact; `X-Keep-Request-Version` required |
| POST | `/keep/requests/{requestId}/share-intent` | Record share intent / clear NeedsShare; version required |
| PUT | `/keep/requests/{requestId}/watch` | Watch; version required |
| DELETE | `/keep/requests/{requestId}/watch` | Unwatch; version required |
| PUT | `/keep/requests/{requestId}/mute` | Mute; version required |
| DELETE | `/keep/requests/{requestId}/mute` | Unmute; version required |
| PUT | `/keep/requests/{requestId}/responsible` | Self-assign; version required |
| DELETE | `/keep/requests/{requestId}/responsible` | Unassign self; version required |
| PUT | `/keep/requests/{requestId}/follow-up-on` | Set Follow Up On; version required |
| DELETE | `/keep/requests/{requestId}/follow-up-on` | Clear Follow Up On; version required |
| PUT | `/keep/requests/{requestId}/planned-for` | Set Planned For; version required |
| DELETE | `/keep/requests/{requestId}/planned-for` | Clear Planned For; version required |

### X-Keep-Request-Version Conflict Contract (Confirmed)

Header name: `X-Keep-Request-Version`. Canonical GUID "D" format (8-4-4-4-12), no braces, no Guid.Empty.

| Error condition | HTTP status | `code` field |
|-----------------|-------------|--------------|
| Header absent | 400 | `KeepRequest.ExpectedVersionRequired` |
| Header present but invalid (non-GUID, multi-value, Guid.Empty) | 400 | `KeepRequest.ExpectedVersionInvalid` |
| Stale version / EF race | 409 | `KeepRequest.RequestChanged` |
| Self-assign already claimed | 409 | `KeepRequest.ParticipationRequestAlreadyAssigned` |

All error responses are ProblemDetails JSON:
```json
{ "type": "about:blank", "title": "Conflict.", "status": 409, "detail": "...", "code": "KeepRequest.RequestChanged" }
```

Mobile **must** parse the JSON error body to read `code`. The current `ApiError` stores raw text only — S17b must add typed error parsing so conflict detection does not rely on regex/text matching.

### Public Intake Link Sharing — Gap Confirmed

`GET /keep/setup/intake` uses `PermissionKeys.Keep.SettingsManage` which is Admin/Owner only (not Operator). Operators receive 403. No existing Operator-accessible endpoint returns the shareable public intake URL.

**Decision:** S17f ships Quick Capture only. Public intake sharing is a separately approved pre-pilot gap slice. Gap scope: a narrow GET endpoint that derives AccountId from bearer auth, returns only the shareable public intake URL, allows Owner/Admin/Operator, denies Viewer/unauth, and proves authorization in focused integration tests.

### S17b API Client Gaps

The following must be addressed before any feature slice that needs them:

1. **Missing `patch` method** — needed for `PATCH /keep/requests/{id}/status`.
2. **No custom header support** — needed for `X-Keep-Request-Version` on all version-checked endpoints.
3. **`ApiError` has no typed `code` field** — `response.text()` is stored raw; JSON parsing and a `code` property are required for conflict handling.
4. **No global 401 intercept** — authenticated API calls that receive 401 just throw `ApiError(401)`; auth state is not reset. S17b must register a 401 callback so any authenticated 401 clears SecureStore, resets user/query state, and returns to sign-in.

### S17b Navigation Gaps

- No `(tabs)/available` route — must be added.
- `(tabs)/index` "Requests" tab → becomes "My Work" with segmented control (My Promises / Watching).
- Capture must be wired as a modal/sheet action, not a persistent tab screen.
- No `requests/[id]` detail stack route — must be added.
- Root `_layout.tsx` Stack must register `requests/[id]`.

### S17b Account Screen Gaps

Current account screen shows role + accountUserId + Sign out. S17b must add: user/account context (name or email), Privacy Policy link, Support link, `Request Account Deletion` (config-gated: `EXPO_PUBLIC_ACCOUNT_DELETION_URL` must be a valid HTTPS OpHalo deletion URL or row is omitted entirely), and app/build version metadata.

### S17c Required (Pre-Confirmed)

- Remove `__DEV__` bearer token paste field from `app/signin.tsx` (lines 86–110).
- Implement global 401 handler: clear SecureStore token, setUser(null), clear query cache, redirect to sign-in.

### Local E2E Handoff Runbook

Required before S17b feature coding begins. Preferred path:
1. Start `OpHalo.Api` locally (`http://localhost:5092`).
2. Start `ophalo-web` locally (`http://localhost:5173`).
3. Create an account through `ophalo-web`.
4. On the web `ophalo-app`, request a mobile handoff (web generates an `ophalo://auth/callback?code=<code>` link).
5. Open the link on an iOS simulator or Android emulator — Expo Go or a dev build registered with the `ophalo://` scheme must be running.
6. Verify handoff: `auth/callback.tsx` redeems the code via `POST /auth/mobile-handoff/redeem`, calls `storeToken` which validates `/auth/me` and checks role, then routes to `(tabs)`.
7. Confirm authenticated tab shell and badge polling start without error.

Dev-token paste field in `signin.tsx` may be used as a local dev shortcut until S17c removes it. It must not be used for reviewer/demo product behavior.

---

## S17b Implementation Notes

### Files Changed (8 production files + .env.example)

**Modified:**
- `mobile/ophalo-mobile/src/api/client.ts` — `patch` method; custom `headers` option on all mutations; JSON-parse error bodies into `ApiError.code?: string`; `setOn401Handler` export; 401 handler gated on `authenticated` flag.
- `mobile/ophalo-mobile/src/auth/AuthContext.tsx` — registers 401 handler via `setOn401Handler`; handler calls `clearSessionToken()`, `setUser(null)`, `queryClient.clear()` (via `useQueryClient()`); `logout()` also clears query cache.
- `mobile/ophalo-mobile/app/_layout.tsx` — added `requests/[id]` Stack.Screen.
- `mobile/ophalo-mobile/app/(tabs)/_layout.tsx` — tabs restructured: My Work (index, checklist icon), Available (tray icon), Capture (custom `tabBarButton` → `router.push('/modal')`; never navigates to the tab screen), Account (unchanged).
- `mobile/ophalo-mobile/app/(tabs)/account.tsx` — settings-style list; role/truncated-user-ID context; Privacy Policy (`${WEB_URL}/privacy`); Support config-gated by `EXPO_PUBLIC_SUPPORT_URL` (any valid HTTPS URL); `Request Account Deletion` config-gated by `EXPO_PUBLIC_ACCOUNT_DELETION_URL` (HTTPS + ophalo.com); app version; Sign Out. Validation centralized in module-level `validOpHaloHttpsUrl` / `validHttpsUrl`.
- `mobile/ophalo-mobile/app/modal.tsx` — replaced stock placeholder with an intentional Quick Capture shell (title, description, Done button via `router.back()`; no workflow; S17f fills the form).
- `mobile/ophalo-mobile/.env.example` — documents `EXPO_PUBLIC_SUPPORT_URL` and `EXPO_PUBLIC_ACCOUNT_DELETION_URL`.

**New:**
- `mobile/ophalo-mobile/app/(tabs)/available.tsx` — "No requests available." empty state; S17d wires real query.
- `mobile/ophalo-mobile/app/requests/[id].tsx` — detail route stub with explicit `useAuth` guard (redirects to `/signin` when unauthenticated); shows request ID; no interaction; S17e fills content.
- `mobile/ophalo-mobile/app/(tabs)/capture.tsx` — Expo Router stub required for file-based routing; immediately redirects to `/(tabs)`; `tabBarButton` intercepts all presses so this screen is never shown; S17f replaces with Quick Capture form.

### S17b Gap Notes

**Email/name on Account screen:** `/auth/me` returns only `accountUserId`, `accountId`, `isAuthenticated`, `isVerified`, `accountRole`. `ICurrentUser` has no email field; session claims carry no email. Account screen shows role + truncated `accountUserId`. Email display requires a backend gap slice: DB lookup in `/auth/me` + DTO extension. Not in S17b scope.

**`useNetworkState` hook deferred to S17f:** ADR-403 requires offline mutation blocking. No mutations land until S17f (Quick Capture). The hook requires `@react-native-community/netinfo` (not installed). Defer to S17f so the install and hook land alongside the first code that needs them.

**Root-stack auth boundary:** Routes registered in the root Stack (e.g. `requests/[id]`) do not inherit the `(tabs)` auth redirect. Each such route must guard itself. ADR-400 updated to state this rule explicitly. Applied in `requests/[id].tsx` via `useAuth` + `<Redirect>`.

### TypeScript Gate

`npx tsc --noEmit` passes with 0 errors after the full S17b correction pass.

---

## S17c Implementation Notes

### Files Changed (1 production file)

**Modified:**
- `mobile/ophalo-mobile/app/signin.tsx` — removed `__DEV__` bearer token paste field: `devToken` state, `{__DEV__ && ...}` JSX block, unused `storeToken` destructure from `useAuth`, and `devLabel`/`devButton`/`devButtonText` styles. Sign-in screen now only offers magic-link email entry. 401 handler was already complete from S17b.

### TypeScript Gate

`npx tsc --noEmit` passes with 0 errors.

---

## S17d Implementation Notes

### Files Changed (4 files — 2 new hooks, 2 rewritten screens)

**New:**
- `mobile/ophalo-mobile/src/hooks/useMyWork.ts` — `useMyWork(view)` for `view=assigned_to_me` and `view=watching`. Types: `KeepRequestSummary` (id, referenceCode, status, currentStatusText, customerName, description, needsShare, attention.priorityBand, preview.previewText, participation.responsibleDisplayName, timing.followUpOnLabel/plannedForLabel), `MyWorkResult`, `MyWorkView`. `staleTime: 30s`, `refetchInterval: 60s`. Query key `['keepRequests', view]`.
- `mobile/ophalo-mobile/src/hooks/useAvailable.ts` — `useAvailableRequests()` for `/keep/requests/available?limit=25`. Types: `AvailableRequestItem` (requestId, referenceCode, customerName, status, descriptionPreview, priorityBand, attentionLevel, canSelfAssign, canWatch), `AvailableRequestsResult`. Same cache posture. Query key `['keepRequests', 'available']`. `canSelfAssign` / `canWatch` typed for S17i use; not surfaced in S17d UI.

**Modified:**
- `mobile/ophalo-mobile/app/(tabs)/index.tsx` — My Work screen: custom two-button segmented control (no native dependency); both queries fetched simultaneously and swapped on segment change; `MyWorkRow` shows customerName, referenceCode, status/currentStatusText, previewText, priorityBand (suppressed when Normal), needsShare, followUpOnLabel/plannedForLabel; `StatusLine` with cached timestamp and `hasMore` indicator; `EmptyState` with loading / error+retry / zero-row variants; `RefreshControl` pull-to-refresh. Dark mode: `useColorScheme` for segment track and card backgrounds; opacity-based secondary text throughout.
- `mobile/ophalo-mobile/app/(tabs)/available.tsx` — Available screen: same structure as My Work; `AvailableRow` shows customerName, referenceCode, status, descriptionPreview, priorityBand (suppressed when Normal); no action controls. All navigation via `router.push({ pathname: '/requests/[id]', params: { id } })`.

### Decisions Made

- **Row fields (locked):** customerName, referenceCode, status (normalized), previewText/descriptionPreview, priorityBand (if not Normal), needsShare, followUpOnLabel/plannedForLabel. No action buttons in any S17d row.
- **Segmented control:** custom `TouchableOpacity` pair — no new native package.
- **Pagination:** first page only for S17d. `hasMore: true` shown as status line indicator only; no load-more control.
- **Dark mode:** `useColorScheme` for backgrounds; opacity for secondary text. Hardcoded accent hex values (`#EAF2FF`, `#174A8B`, `#0057D9`) are carry-forwards from ChatGPT scaffolding — brand token alignment is deferred (see open gap below).

### Verified Gate (pre-commit)

- `npx tsc --noEmit` — 0 errors.
- `.expo/types/router.d.ts` hand-edit not committed (`.expo/` is gitignored; file regenerated by dev server).
- No action buttons in S17d rows — only row-level navigation and error-state retry.
- Available screen read-only except detail navigation.

### Open Gap — Brand Guide Alignment

The mobile screens do not yet follow `docs/ux-design/ux-design-model-v1.md`. Confirmed gaps:

- **Canvas:** `Colors.light.background = '#fff'`; spec requires `--ophalo-canvas` `#F8F6F1`.
- **No Keep teal moment:** `--keep-accent` `#168A9A` absent from all mobile screens (Richness Floor rule 3 failure).
- **Status pill tokens:** pill uses `#EAF2FF`/`#174A8B` instead of `--keep-info-bg` `#EAF1FF` / `--keep-info` `#244C95`.
- **Border:** `rgba(128,128,128,0.3)` instead of `--ophalo-border` `#DDD6C8`.
- **Typography:** Source Serif 4 (headings) and Inter (body) not installed; app uses system font.
- **Retry button:** `#0057D9` is not a brand token; should be `--keep-accent` or `--ophalo-navy`.

Recommended fix: dedicated UX alignment pass before S17j that adds a `src/constants/brand.ts` token file, fixes canvas/card two-tone separation, installs fonts via `expo-google-fonts`, and applies a single teal moment per screen. Do not mix into S17e–S17i behavior slices.

---

## S17e Implementation Notes

### Files Changed (2 production files)

**New:**
- `mobile/ophalo-mobile/src/hooks/useRequestDetail.ts` — `useRequestDetail(id)` for `GET /keep/requests/{id}`. Types: `KeepRequestDetailDto` (requestId, referenceCode, status, currentStatusText, needsShare, customerName, customerPhone, customerEmail, description, version, attentionLevel, attentionReason, priorityBand, waitingDirection, followUpOnDate, followUpOnReason, plannedForDate, contactActions, participants, currentUserParticipation, events, availableActions), `AvailableActionsDto`, `EventItem`, `ParticipantItem`, `CurrentUserParticipationDto`, `ContactActionItem`. `enabled: !!id` — does not fetch when id is absent. `staleTime: 30s`, `refetchInterval: 60s`. Query key `['keepRequestDetail', id]`. `version` retained in DTO for future mutation use (S17h/S17i); not used in S17e.

**Modified:**
- `mobile/ophalo-mobile/app/requests/[id].tsx` — full read-only detail screen replacing the S17b stub. `Stack.Screen title` updates to `referenceCode` once data loads. Sections: header (customerName, referenceCode, status pill, currentStatusText, priorityBand if not Normal, needsShare), description, attention (attentionLevel/attentionReason/waitingDirection — only when attentionLevel is not None), timing (followUpOnDate/followUpOnReason/plannedForDate — conditional), participation (currentUserParticipation + active responsible from participants), contact affordances (available ContactActions as text FieldRows — type + target, not tappable), available actions (plain text labels for true booleans in AvailableActionsDto — not button-shaped), timeline (EventRows oldest-first, matching backend ordering; actor, normalized eventType, statusAfter, content, visibility=Internal label). Loading/error/empty states intentional. Pull-to-refresh via `RefreshControl`. Auth guard (`useAuth` + `<Redirect href="/signin">`) retained from S17b stub.

### Decisions Made

- **Event ordering:** API delivers oldest-first (`OrderBy(e => e.OccurredAtUtc)` confirmed). No local reordering applied.
- **ContactActions display:** Text `FieldRow` (label + value) only — not tappable. OS handoff (tel:/sms:/mailto:) deferred to S17g.
- **AvailableActions display:** Plain `Text` labels from `ACTION_LABELS` map for true booleans — no disabled buttons, no button-shaped rows. Section titled "Actions".
- **Responsible display:** Active participant with `participationType === 'responsible'` and `detachedAtUtc === null` resolved client-side from participants array.
- **No S17g/S17h/S17i controls:** No `Linking`, `Share`, mutation hooks, or write-path imports anywhere in S17e files.

### TypeScript Gate

`npx tsc --noEmit` passes with 0 errors.

---

## Test Strategy

Use local simulator/device workflow as the inner loop and staging/store-bound builds as later gates.

1. Local E2E loop:
   - `OpHalo.Api` local
   - `ophalo-web` local for account creation and auth handoff
   - Expo app on simulator/emulator/real device
   - genuine account where feasible
2. Per-slice automated checks:
   - mobile TypeScript/lint/build as available
   - focused API integration tests for backend-touched contracts
3. Manual mobile gate:
   - touch behavior, route transitions, app-resume sync, offline/weak-network failure posture,
     contact/share OS handoff, and conflict/error states
4. Deployed-service gate before store confidence:
   - real topology for `ophalo.com`, `app.ophalo.com`, `api.ophalo.com`, and mobile callback paths
5. S19 store-submission gate:
   - EAS builds, signing, screenshots, reviewer credentials, privacy labels, account deletion URL,
     Universal/App Links, and review notes

---

## S17f Implementation Notes

### Files Changed (3 new hooks/screens + package install)

**New:**
- `mobile/ophalo-mobile/src/hooks/useNetworkState.ts` — wraps `@react-native-community/netinfo` (installed via `npx expo install`). Subscribes to `NetInfo.addEventListener`; only sets `isOnline: false` when `state.isConnected === false` explicitly; treats null/unknown as online per ADR-403 offline-blocking posture.
- `mobile/ophalo-mobile/src/hooks/useQuickCapture.ts` — `usePhoneLookup(rawPhone)`: digits-only normalization via `normalizePhoneDigits`; `enabled` when 7–15 digits; query key `['phoneLookup', digits]`; `staleTime: 30s`. `useCreateRequest()`: mutation to `POST /keep/requests` with `authenticated: true`; invalidates `['keepRequests']` on success. `CreatedRequestResult { requestId }` typed for post-save navigation only.

**Modified:**
- `mobile/ophalo-mobile/app/modal.tsx` — full Quick Capture form replacing S17b shell. Phone input (auto-focused, phone-pad keyboard); lookup result shown as read-only context (known customer name + active request list, not a blocker); auto-fill of name/email via `useEffect` on lookup result; clearing auto-filled fields on phone change. Customer name, email (optional), description (multiline), source picker (7 chips: phone/voicemail/text/email/walk_in/referral/other — `public_intake` excluded). Offline banner + Save disabled when `isOnline === false`; form state preserved. Error banner for create failures. Post-save: `router.replace({ pathname: '/requests/[id]', params: { id: created.requestId } })` — replaces modal in stack, back returns to tabs. Cancel via `router.back()`.

### Decisions Made

- **No intake share UI:** S17f ships Quick Capture only. Intake sharing remains a separately approved pre-pilot gap slice.
- **NetInfo permission posture:** `@react-native-community/netinfo` added. No runtime permission prompt expected (connectivity detection is passive on iOS/Android). Android manifest output should be verified at S17j or store gate if "zero platform permissions" is interpreted literally.
- **Offline blocking:** Save disabled when `isConnected === false`; form state preserved. Unknown/null connectivity treated as online.
- **Auto-fill:** `useEffect` on `[lookup, lookupApplied]`; fills once per lookup result; clears on phone change.
- **Post-save navigation:** `router.replace` to detail route — back returns to authenticated tab stack.

### TypeScript Gate

`npx tsc --noEmit` passes with 0 errors.

---

## S17g Implementation Notes

### Files Changed (2 new hooks + 2 modified files + .env.example)

**New:**
- `mobile/ophalo-mobile/src/hooks/useLogExternalContact.ts` — mutation for `POST /keep/requests/{id}/external-contact`. Variables: `{ requestId, version, direction, channel, outcome? }`. Sends `X-Keep-Request-Version` header with current `data.version`. On success: invalidates `['keepRequestDetail', requestId]`, `['keepRequests']`, `['badge']`. On error (including 409): invalidates `['keepRequestDetail', requestId]` so version and state refresh.
- `mobile/ophalo-mobile/src/hooks/useClearShareIntent.ts` — mutation for `POST /keep/requests/{id}/share-intent`. Variables: `{ requestId, method }` where method is `'copy_link' | 'native_share' | 'manual_mark_shared'`. No version header (see S17a table correction below). On success: invalidates `['keepRequestDetail', requestId]`, `['keepRequests']`, `['badge']`. On error: invalidates detail query.

**Modified:**
- `mobile/ophalo-mobile/src/hooks/useRequestDetail.ts` — `pageToken: string` added to `KeepRequestDetailDto`; stale S17e comment on `version` removed.
- `mobile/ophalo-mobile/app/requests/[id].tsx` — Contact section rows replaced with tappable `TouchableOpacity` rows (label + target + chevron). Phone tap: `Linking.canOpenURL` → `Linking.openURL('tel:...')` → on success sets `contactPending`; failure (unsupported or throw) does not prompt. Phone outcome sheet (custom `Modal`, fade animation, dark-mode aware `cardBg`): Spoke with customer / Left voicemail / No answer / Skip; first three call `logContact` mutation with outcome; Skip dismisses with no mutation. Email tap: same Linking pattern → email confirm sheet: "Log as email sent" / Skip. Share section shown when `canShare` (`trackerUrl` non-null + `needsShare` + `canRecordShareIntent`): "Share via…" opens `Share.share()` → `result.action === Share.sharedAction` → sets `shareConfirmMethod('native_share')` for post-share confirm; "Mark as shared" button sets `shareConfirmMethod('manual_mark_shared')`. Share confirm sheet: "Yes, mark as shared" calls `recordShareIntent`; Cancel dismisses. 409 errors surface a human-readable message in the active sheet and auto-refresh via invalidation. Mutation buttons disabled (`opacity: 0.45`) while pending. `ACTION_LABELS` entries for `canLogExternalContact` and `canRecordShareIntent` removed (now interactive sections). `PUBLIC_BASE_URL` normalized with trailing-slash strip.
- `mobile/ophalo-mobile/.env.example` — `EXPO_PUBLIC_PUBLIC_BASE_URL=https://ophalo.com` added.

### Decisions Made

- **Phone outcomes:** custom bottom sheet (`Modal` + `Pressable` overlay), not `Alert.alert`. Spoke/Voicemail/No answer/Skip. Skip sends nothing.
- **Email logging:** one-tap "Log as email sent" confirm; no outcome picker (outcome unknowable at send time).
- **Share intent (ADR-401):** opening the OS share composer alone does not record intent. Only explicit post-share or manual confirmation calls `POST .../share-intent`.
- **badge invalidation:** `useLogExternalContact` and `useClearShareIntent` both invalidate `['badge']` on success.

### S17a Table Correction

The S17a endpoint table listed `share-intent` as requiring `X-Keep-Request-Version`. Backend code (`Program.cs` line 476) confirms the handler takes no `HttpRequest` parameter and calls `ClearShareIntentService` directly — no `KeepRequestVersionHeader.Parse()`. **Share-intent requires no version header.** `useClearShareIntent` correctly omits it. External-contact continues to require the version header.

### TypeScript Gate

`npx tsc --noEmit` passes with 0 errors.

---

## S17h Implementation Notes

### Files Changed (1 new hook + 1 modified screen)

**New:**
- `mobile/ophalo-mobile/src/hooks/useSendBusinessUpdate.ts` — mutation for `POST /keep/requests/{id}/business-updates`. Variables: `{ requestId, version, message, setStatus? }`. Sends `X-Keep-Request-Version` header with current `data.version` via `api.post(..., true, { 'X-Keep-Request-Version': version })`. On success: invalidates `['keepRequestDetail', requestId]`, `['keepRequests']`, `['badge']`. On error (including 409): invalidates `['keepRequestDetail', requestId]` so version and state refresh.

**Modified:**
- `mobile/ophalo-mobile/app/requests/[id].tsx` — Customer Update composer section added. Rendered only when `availableActions.canSendBusinessUpdate === true`; omitted entirely otherwise. `TextInput` (multiline, 80px min height, disabled while pending). "Send Update" button posts `{ message }`; "Send Update & Mark Completed" button rendered only when `allowedStatuses.includes('resolved')` and posts `{ message, setStatus: 'resolved' }`. Both buttons disabled when `composerText.trim() === ''`, `isSendingUpdate`, or `!isOnline`. Offline notice shown when `!isOnline`. On success: clears `composerText` and `composerError`. On 409 or `ApiError.code === 'KeepRequest.RequestChanged'`: preserves text, shows conflict message. On other 4xx: preserves text, shows generic API error. On network/no-response failure: preserves text, shows retry-safe copy. `canSendBusinessUpdate` removed from `ACTION_LABELS` (now has interactive section). `useNetworkState` and `useSendBusinessUpdate` imported; `TextInput` added to react-native imports.

### Decisions Made

- **`allowedStatuses` case-sensitive:** `allowedStatuses.includes('resolved')` uses exact lowercase slug as returned by backend. No normalization.
- **Completion is not a one-tap status flip:** requires operator-authored message text; disabled on empty input. `setStatus: 'resolved'` only added when `allowedStatuses.includes('resolved')` and `canSendBusinessUpdate` both true.
- **Composer omitted entirely when `canSendBusinessUpdate` is false:** no disabled placeholder, no "coming soon" affordance.
- **badge invalidation on success:** completing a request changes attention state; badge must refresh.
- **Composer text preserved on all errors:** only cleared on success.

### TypeScript Gate

`npx tsc --noEmit` passes with 0 errors.

---

## S17i Implementation Notes

### Files Changed (5 new hooks + 2 modified screens)

**New:**
- `mobile/ophalo-mobile/src/hooks/useWatchRequest.ts` — exports `useWatchRequest` and `useUnwatchRequest`. `PUT`/`DELETE /keep/requests/{id}/watch` with `X-Keep-Request-Version` header; no body. On success: invalidates `['keepRequestDetail', requestId]`, `['keepRequests']`, `['badge']`. On error: invalidates `['keepRequestDetail', requestId]`.
- `mobile/ophalo-mobile/src/hooks/useMuteRequest.ts` — exports `useMuteRequest` and `useUnmuteRequest`. Same pattern against `PUT`/`DELETE /keep/requests/{id}/mute`.
- `mobile/ophalo-mobile/src/hooks/useAssignResponsible.ts` — `PUT /keep/requests/{id}/responsible` with body `{ accountUserId }` and version header. Identity source: `user.accountUserId` from `useAuth`. Same invalidation pattern. Used from both the detail screen and the Available list row.
- `mobile/ophalo-mobile/src/hooks/useFollowUpOn.ts` — exports `useSetFollowUpOn` and `useClearFollowUpOn`. Set: `PUT` with body `{ date }` (YYYY-MM-DD only; `reason` not surfaced); Clear: `DELETE`. Same invalidation.
- `mobile/ophalo-mobile/src/hooks/usePlannedFor.ts` — exports `useSetPlannedFor` and `useClearPlannedFor`. Same pattern against `/planned-for`.

**Modified:**
- `mobile/ophalo-mobile/app/requests/[id].tsx` — Participation section: "Assign to me" button rendered when `canAssignResponsible` (primary style, disabled while pending/offline); Watch/Unwatch rendered separately from `canWatch`/`canUnwatch` (outline style); Mute/Unmute from `canMute`/`canUnmute`. Per-action error state (`assignError`, `watchError`, `muteError`). Timing section: now renders when any of `followUpOnDate`, `plannedForDate`, `canSetFollowUpOn`, `canSetPlannedFor` is truthy (previously conditional on dates only). Set controls: `TextInput` placeholder `YYYY-MM-DD` with `keyboardType="numbers-and-punctuation"`, client-side calendar validity check via `isValidDateInput` before send; "Set" buttons disabled when input empty/pending/offline; "Clear" buttons shown only when a date is currently set. Typed input preserved on error; cleared on success only. `ACTION_LABELS` reduced to `canChangeStatus`, `canAddInternalNote`, `canClose` (S17i-owned keys removed). ADR-403 offline guard applied to previously-unguarded sheet mutation buttons: phone outcome options, email log option, share confirm button now include `|| !isOnline`.
- `mobile/ophalo-mobile/app/(tabs)/available.tsx` — `AvailableRow` converted from pure presentational to hook-bearing component. Each row calls `useAssignResponsible`, `useAuth`, `useNetworkState`, and `useState` independently. "Assign to me" button rendered when `canSelfAssign`; disabled while pending/offline. `KeepRequest.ParticipationRequestAlreadyAssigned` → "Already claimed — refreshed."; 409 `KeepRequest.RequestChanged` → "Request has changed — refreshed."; other errors → "Could not assign." Row navigation tap remains independent.

### Decisions Made

- **Date input is `TextInput` YYYY-MM-DD, not a date picker:** no `@react-native-community/datetimepicker` dependency added; client-side regex + calendar validity check before send. Error copy: "Use YYYY-MM-DD, for example 2026-07-10."
- **Follow-up `reason` field omitted from mobile PUT body:** `SetFollowUpOnRequestBody.Reason` is optional; mobile sends `{ date }` only.
- **Self-assign uses `user.accountUserId` from `useAuth`:** the responsible endpoint takes `{ accountUserId }` in the body; mobile passes the signed-in user's own ID.
- **`AvailableRow` holds its own hook instances:** each row manages its own pending/error state independently; no shared assign state in the parent screen.
- **Sheet mutation buttons offline-guarded (ADR-403 residual):** phone/email log and share-confirm sheet buttons missed in S17g; corrected here.

### TypeScript Gate

`npx tsc --noEmit` passes with 0 errors. `git diff --check` clean. 7 files changed.

---

## S17j Implementation Notes

### Files Changed (1 production file)

**Modified:**
- `mobile/ophalo-mobile/app/modal.tsx` — auth guard added. `CaptureModal` (default export) checks `user` via `useAuth`; redirects to `/signin` when unauthenticated. Form/query/mutation logic moved into `CaptureModalContent`. Root Stack routes do not inherit the tab auth redirect (ADR-400); this closes the gap.

### Gap Closed

`modal.tsx` is registered in the root Stack as `modal`. Without a guard, `ophalo://modal` deep links could render Quick Capture for an unauthenticated caller, exposing authenticated API calls. Fixed per the wrapper pattern so hooks never mount before auth is confirmed.

### Android Permission Note

No `android/` directory in the repo (managed Expo workflow). Android manifest is generated at EAS build or `npx expo prebuild`. `@react-native-community/netinfo` adds `ACCESS_NETWORK_STATE` and `CHANGE_NETWORK_STATE` — these are install-time, not runtime, permissions (not in the Contacts/Location/Camera class). Final manifest verification is deferred to the EAS/prebuild gate in S19.

### Brand Alignment

Deferred to a separate polish slice. `src/constants/brand.ts`, Inter/Source Serif 4 fonts, and canvas/card color separation remain out of scope.

### TypeScript Gate

`npx tsc --noEmit` passes with 0 errors. `git diff --check` clean. 1 file changed.

---

## Post-S17 Review Hardening Notes

A focused review pass after S17j found several native review-safety and auth-edge issues. Corrections
landed as a narrow mobile hardening pass:

- `mobile/ophalo-mobile/app.json` now declares iOS `LSApplicationQueriesSchemes` for `tel`,
  `telprompt`, and `mailto`, so standalone iOS/EAS builds can use `Linking.canOpenURL` for contact
  actions.
- `mobile/ophalo-mobile/app/requests/[id].tsx` now uses the same guard-wrapper pattern as
  `modal.tsx`; unauthenticated deep links redirect before request-detail query and mutation hooks
  mount. Unsupported contact actions now show device-appropriate feedback instead of silently
  no-oping.
- `mobile/ophalo-mobile/src/auth/AuthContext.tsx` no longer clears the SecureStore session token for
  generic bootstrap/network failures; it clears only on authenticated `401`/`403`. Logout now wipes
  local token/auth/query state before best-effort server revocation calls.
- `mobile/ophalo-mobile/app/_layout.tsx` adds a role-blocked escape action back to sign-in and hides
  the native modal header so Quick Capture does not double-render headers.
- `mobile/ophalo-mobile/app/modal.tsx` maps Quick Capture create failures to user-safe copy instead
  of rendering raw ProblemDetails JSON.
- `mobile/ophalo-mobile/src/api/client.ts` now allows best-effort revocation calls to pass the
  captured bearer token after local logout has already wiped SecureStore.
- `mobile/ophalo-mobile/src/hooks/useAvailable.ts` is role-aware after local smoke testing showed
  Owners receive `403` from `/keep/requests/available`. Operators continue to use the dedicated
  Available endpoint; Owner/Admin users use the existing authorized `GET /keep/requests?view=unassigned`
  contract mapped into the mobile Available row shape. Permission errors no longer retry noisily.

### TypeScript Gate

`npx tsc --noEmit` passes with 0 errors after the post-review hardening pass. `git diff --check`
clean.

---

## Post-Session Bug Fixes (2026-07-04)

Manual testing on device after S17 completion revealed five UI bugs, fixed in a single follow-up
pass without backend changes.

### Bugs Found and Fixed

**1. My Work list — responsible person not displayed**
`MyWorkRow` in `app/(tabs)/index.tsx` rendered customer name, reference code, preview, and meta
tags but never showed `participation.responsibleDisplayName`. The `KeepRequestSummary` type already
carried the field; it was simply not rendered. Fix: added a name line below the customer name header
when the field is present.

**2. "Assign to me" button visible when current user is already responsible**
The participation section in `app/requests/[id].tsx` gated the button solely on
`availableActions.canAssignResponsible`. After a successful assignment the server returns the user's
participation type as `'responsible'`, but the cached detail had not yet refreshed by the time the
user observed the screen. More importantly, the API could return `canAssignResponsible: true` for a
re-assign scenario even when already responsible, leaving the button permanently visible.
Fix: added a secondary guard — hide the button when
`currentUserParticipation.participationType.toLowerCase() === 'responsible'`.

**3. Attention section always visible (case-sensitivity mismatch)**
The section visibility condition compared `data.attentionLevel !== 'None'` against the raw API
value, which the server returns as `'none'` (lowercase). Because `'none' !== 'None'` is always
true, the Attention section always rendered even when attention level was effectively absent.
Fix: changed the comparison to `data.attentionLevel.toLowerCase() !== 'none'`.

**4. RefreshControl spinner stuck after assignment mutation**
`RefreshControl refreshing` was bound to `activeQuery.isRefetching`. When `useAssignResponsible`
succeeded, it called `queryClient.invalidateQueries({ queryKey: ['keepRequests'] })`, which
triggered an automatic background refetch on the My Work query. `isRefetching` became true,
showing the pull-to-refresh spinner with no way to dismiss it until the background fetch resolved.
On slow connections this appeared stuck indefinitely.
Fix: replaced the direct binding with a `isManualRefreshing` local state that is only set to true
on an explicit user pull gesture and cleared via `.finally()` when the refetch promise settles.
Background invalidation refetches no longer surface the spinner.

**5. Share / Tracker section hidden when `PUBLIC_BASE_URL` not configured**
`canShare` required both a valid `trackerUrl` (derived from the `EXPO_PUBLIC_PUBLIC_BASE_URL` env
var) and `availableActions.canRecordShareIntent`. When the env var was unset the entire Tracker
section was hidden, including "Mark as shared" — an action that does not require a link.
Fix: split into `canRecordShare` (needs share + action permitted) and `canShare` (also requires
`trackerUrl`). The Tracker section now renders whenever `canRecordShare` is true; "Share via…" is
conditionally rendered only when `canShare` is also true.

**6. YYYY-MM-DD text input replaced with native date picker sheet (ADR-406)**
The S17 timing controls used a free-text `TextInput` with `YYYY-MM-DD` format as a low-dependency
implementation choice. Manual field testing confirmed this is too fiddly for operators on a phone.
Decision: replace both Follow Up On and Planned For inputs with a shared `DateSheetPicker` bottom
sheet component, parametrized by label and context subtitle so each field communicates its intent
without changing the backend contract.

Sheet interaction:
- Quick-pick chips: Today, Tomorrow, This Friday (skipped when same as Today or Tomorrow), Next week.
  Chips immediately save the date and dismiss the sheet — one tap, done.
- Native spinner date picker (scroll wheel) for any other date.
- Save / Cancel / Clear date action row.

Display after set: friendly locale string — `Fri, Jul 10` — not raw `YYYY-MM-DD`.
Set date row shows `Change` to re-open the sheet pre-loaded on the existing date.
Trigger when no date set: outline button labeled with the field name (`Follow up on` / `Planned for`).

Requires `@react-native-community/datetimepicker` (`npx expo install @react-native-community/datetimepicker`).
Helper functions added at module level: `parseLocalDate`, `toDateStr`, `formatFriendlyDate`.
`isValidDateInput` and both `TextInput` state variables removed.

### Files Changed

- `mobile/ophalo-mobile/app/(tabs)/index.tsx` — responsible display name, manual refresh state
- `mobile/ophalo-mobile/app/requests/[id].tsx` — assign guard, attention case fix, share split,
  date picker sheet replacing text inputs

### TypeScript Gate

`npx tsc --noEmit` passes with one expected error (`Cannot find module '@react-native-community/datetimepicker'`)
that resolves after `npx expo install @react-native-community/datetimepicker`. All other types clean.

---

## Post-Session Issues (2026-07-05)

### Issue: Follow Up On — "Could not set follow-up. Please try again."

**Root cause:** `SetFollowUpOnRequestBody.Reason` was a non-nullable `string`, and
`ManageRequestTimingService` returned `FollowUpOnReasonRequired` (400) when reason was absent.
The S17 decision was that mobile sends only `{ date }` — no reason — because forcing a reason in
the field adds friction. The backend was built before that product call was locked.

**Fix:** Made reason optional throughout the stack and relaxed the service validation to only
reject a provided-but-invalid slug (non-empty string that does not match a known `FollowUpReason`
enum value). A missing reason stores `null` in the DB; a valid slug is accepted as before; an
unrecognized non-empty slug is still rejected with `FollowUpOnReasonRequired`.

Files changed (server):
- `src/OpHalo.Api/Keep/RequestTimingRequests.cs` — `string Reason` → `string? Reason`
- `src/OpHalo.Keep.Application/Requests/ManageRequestTimingService.cs` — command record and
  conditional slug validation (only when reason non-null)
- `src/OpHalo.Keep.Core/Entities/KeepRequest.cs` — `SetFollowUpOn` signature `FollowUpReason?`;
  removed `!Enum.IsDefined` guard (caller guarantees valid values; null is now legal)

Tests added:
- Unit: `SetFollowUpOn_null_reason_succeeds_and_stores_null`
- Integration: `SetFollowUpOn_NoReason_Returns200WithNullReason`,
  `SetFollowUpOn_InvalidReason_Returns400`

**Improved mobile error messages:** The `onError` handler for `handleSetFollowUp` and
`handleClearFollowUp` now distinguishes `RequestChanged` (version stale), inactive-request 409,
and connection/unknown errors — replacing the generic "Could not set follow-up. Please try again."

**Optional reason chips added to Follow Up On sheet:** After the date quick-chips, a
"Reason (optional)" chip row is rendered inside the same bottom sheet. Chips are single-select
and toggle off on second tap. The selected reason is passed alongside the date on save; if none
is selected, the date is saved with no reason. Planned For does not show reason chips.

Reason mapping (server slugs → plain-language labels):

| Slug | Label |
|------|-------|
| `customer_delay` | Waiting on customer |
| `parts` | Waiting on parts |
| `weather` | Weather |
| `business_operator_availability` | Need to schedule |
| `third_party` | Third party |
| `other` | Other |

Note: the user's initial suggestion included `estimate` and `schedule` slugs; these do not exist
in the `FollowUpReason` enum. Labels were adjusted to map to the actual server contract.

Files changed (mobile):
- `src/hooks/useFollowUpOn.ts` — `reason?: string` added to `SetFollowUpVars`; included in PUT body
  (undefined is omitted by `JSON.stringify`, so no-reason sends `{ date }` only)
- `app/requests/[id].tsx` — `FOLLOW_UP_REASONS` constant; `handleSetFollowUp` accepts optional
  reason; `DateSheetPicker` extended with `existingReason?`, `reasons?`, `selectedReason` state,
  reason chip row, and `onSave(dateStr, reason?)` signature; reason chip styles added

### Issue: Sharing tracker link on mobile required going through ophalo-app

**Decision (2026-07-05):** Two separate product jobs:
1. *Tracker link access* — "I need to send/copy this customer's link right now." Low-friction,
   always available, no side effects.
2. *NeedsShare resolution* — "This request has not had its tracker link intentionally handed
   off yet." Explicit, review-safe, records intent via `POST /share-intent`.

**Fix:**

- Added `handleShareTrackerLink()` — fires `Share.share()` only. No `POST /share-intent` call,
  no state side effects. Available on any open request with a `pageToken` / tracker URL.
- Contact section now shows whenever `contactActions.some(available) || !!trackerUrl`. A
  "Tracker link / Share…" row is appended after call/email rows when `trackerUrl` is present.
  Placement puts it at the moment operators are already in a call/text/email context.
- NeedsShare section (previously "Tracker"): banner text "Tracker link not yet shared" replaces
  the section label. "Share via…" renamed to "Share & mark as shared" to make the intent
  explicit — this path fires the share sheet and on return prompts "Did you share the tracker
  link? → Yes, mark as shared". "Mark as shared" (direct confirm, no share sheet) retained.
- `needsShareBannerText` style added (amber/warning tone: `#B45309`).

Files changed (1):
- `mobile/ophalo-mobile/app/requests/[id].tsx`

### Issue: Owner/admin on mobile had no way to add an internal note

**Decision (2026-07-05):**
- Wire now: Add internal note — high-value field action ("parts ordered", "gate code is 1234"),
  internal-only, does not notify the customer, fits mobile well as a small composer.
- Hide for now: Change status — deferred; mobile already covers the safest path (Send Update &
  Mark Completed → resolved). Generic status picker introduces product nuance around allowed
  transitions, customer notification, and lifecycle intent.
- Hide for now: Close — lifecycle/finalization; easier to misuse on phone; keep web-only for V1.
- Remove the display-only Actions section entirely — every visible action should be interactive.

**Fix:**

- Created `useAddInternalNote` hook: `POST /keep/requests/{id}/internal-notes` with
  `{ note }` body and `X-Keep-Request-Version` header. Invalidates `keepRequestDetail` and
  `keepRequests` on success; refetches detail on error (same pattern as `useSendBusinessUpdate`).
- Added `internalNoteMaxLength: number` to `KeepRequestDetailDto` (API already returns it).
- Added `noteText`/`noteError` state and `handleAddNote` handler in `RequestDetailContent`.
  Error handling mirrors the business-update composer: 409/`RequestChanged` preserves text,
  4xx shows retry copy, network error shows connection copy.
- Added "Internal Note" composer section gated on `canAddInternalNote`: multiline `TextInput`
  with placeholder "Note visible to your team only…", `maxLength` from `data.internalNoteMaxLength`,
  "Add Note" / "Saving…" button disabled when empty / pending / offline, offline warning line.
- Removed display-only `ACTION_LABELS` / `resolveAvailableActionLabels` / Actions section
  entirely — status change and close deferred to a later intentional mobile design pass.

Files changed (3):
- `mobile/ophalo-mobile/src/hooks/useAddInternalNote.ts` (new)
- `mobile/ophalo-mobile/src/hooks/useRequestDetail.ts`
- `mobile/ophalo-mobile/app/requests/[id].tsx`

### Issue: Mobile activity timeline showed raw event labels and no useful context

**Decision (2026-07-05):** Mobile should show activities as a lightweight field timeline
(context, not audit log). Rules: human-readable summaries only; internal events visible to
authenticated staff; customer-visibility boundary preserved; read-only; latest 5 newest-first;
no paging in this pass; overflow hint when more exist.

**Fix:**

- Added `EVENT_TYPE_LABELS` map covering all 13 event types with plain-language labels.
  `message_added` is actor-aware: `account_user` → "Customer update sent",
  `customer` → "Customer message". Timing events keep short header labels ("Follow-up",
  "Planned date") — detail comes from the existing `timingEventDetail()` below them.
- Added `eventContactDetail()` for `external_contact_logged` events: renders
  "Outbound Call — Spoke with customer" style summary from direction/channel/outcome fields.
- Updated `EventRow`: uses `eventLabel(event)` instead of `normalizeLabel(event.eventType)`;
  adds `contactDetail` line; replaces "Internal" badge with "Customer visible" badge
  (`visibility === 'all'` only — staff app assumes internal by default, exception gets flagged).
- Timeline section: renamed to "Activity"; renders `[...data.events].reverse().slice(0, 5)`
  (clone before reverse to preserve query data immutability); shows
  "Showing latest 5 of N events" overflow hint when total exceeds limit; "No activity yet."
  empty state.
- Added styles: `eventCustomerVisible` (green, 600 weight), `eventOverflowHint` (centered,
  muted); removed unused `eventInternal`.

Files changed (1):
- `mobile/ophalo-mobile/app/requests/[id].tsx`

### Issue: DateTimePicker `onChange` deprecation warning

`@react-native-community/datetimepicker` v9.1.0 deprecates `onChange` in favour of three
focused callbacks: `onValueChange`, `onDismiss`, and `onNeutralButtonPress`. The warning fired
on every picker render.

**Fix:** replaced `onChange={(_event, date?) => { if (date) setPickerDate(date); }}` with
`onValueChange={(_event, date) => setPickerDate(date)}`. The `date` argument is non-optional
in `onValueChange`, so the nil-guard is no longer needed.

File changed: `mobile/ophalo-mobile/app/requests/[id].tsx`

### Issue: Timing activity events showed no date detail

**Decision (2026-07-05):** Planned date is an internal ops-scheduling aid, not a customer
commitment. It must not appear on the customer page in V1 — customers only see timing when the
business explicitly sends a customer-visible update. The promise boundary is kept clean.

**Root cause:** `KeepRequestEventItem` did not include the date fields stored on
`KeepRequestEvent` (`PlannedForDate`, `FollowUpOnDate`, `FollowUpOnReason`). The mapper dropped
them, so both frontends rendered a static label ("Planned Date Updated") with no date detail.

**Fix:**

- Added `DateOnly? PlannedForDate`, `DateOnly? FollowUpOnDate`, and `string? FollowUpOnReason`
  to `KeepRequestEventItem` (populated only on the matching event type; null on all others).
- Updated `KeepRequestDetailMapper.MapEvent()` to pass the three fields conditionally via
  `isPlannedFor` / `isFollowUpOn` guards — matching the existing `isContact` / `isParticipation`
  pattern.
- ophalo-app `RequestDetail.tsx`: added `formatDateOnly` (local-date parse to avoid UTC offset)
  and `FOLLOW_UP_REASON_LABELS` map; updated `timelineEventSummary` to render:
  - "Planned date set to Jul 31, 2026" / "Planned date removed"
  - "Follow-up set for Jul 15, 2026 · Waiting on customer" / "Follow-up removed"
  - Tightened static label map entries to "Planned Date" / "Follow-Up" (detail is in the summary).
- Mobile `[id].tsx`: same logic extracted into `timingEventDetail()` helper; `EventRow` renders
  the result below the event-type label; reused existing `formatDateOnly` helper.
- Added `plannedForDate`, `followUpOnDate`, `followUpOnReason` to `KeepRequestEventItem` in
  `apiClient.ts` (ophalo-app) and `EventItem` in `useRequestDetail.ts` (mobile); mock factories
  updated to satisfy TypeScript.

Tests added (unit — `KeepRequestDetailServiceTests`):
- `PlannedForChanged_set_event_carries_date_in_mapped_item`
- `PlannedForChanged_clear_event_has_null_date_in_mapped_item`
- `FollowUpOnChanged_set_event_carries_date_and_reason_in_mapped_item`
- `FollowUpOnChanged_clear_event_has_null_date_and_reason_in_mapped_item`

Full unit suite: 944/944.

Files changed (9):
- `src/OpHalo.Keep.Application/Requests/KeepRequestDetailResult.cs`
- `src/OpHalo.Keep.Application/Requests/KeepRequestDetailMapper.cs`
- `tests/OpHalo.UnitTests/Keep/KeepRequestDetailServiceTests.cs`
- `web/ophalo-app/src/lib/apiClient.ts`
- `web/ophalo-app/src/mocks/fixtures.ts`
- `web/ophalo-app/src/mocks/mockApiClient.ts`
- `web/ophalo-app/src/pages/RequestDetail.tsx`
- `mobile/ophalo-mobile/src/hooks/useRequestDetail.ts`
- `mobile/ophalo-mobile/app/requests/[id].tsx`

### Issue: Date picker allowed selecting past dates

The `DateSheetPicker` had no lower bound, so operators and owners could set a Follow Up On or
Planned For date in the past — meaningless for scheduling and likely a data-entry error.

**Fix:**
- Added `startOfToday()` helper (midnight local time) at module level.
- `minimumDate={startOfToday()}` passed to `DateTimePicker` — the native spinner refuses
  to scroll past today.
- `openSheet` now clamps the initial picker value to today when the stored date is already
  in the past, preventing the picker from opening below the minimum.

File changed: `mobile/ophalo-mobile/app/requests/[id].tsx`
