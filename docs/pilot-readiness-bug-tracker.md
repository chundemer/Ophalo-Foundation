# Pilot Readiness Bug And Gap Tracker

**Created:** 2026-07-02
**Purpose:** Live tracker for pilot-blocking or pilot-relevant bugs/gaps discovered during Session 14.
**Source:** Promoted from the Pre-S14e bug register in `docs/build-log/068-session-14-ophalo-web-front-door.md`.
**Current active item:** GAP-004 — Browser back / refresh does not preserve app location.
**Recently resolved:** GAP-005 — Same-account staff can submit through the public intake form.

This document is the current working tracker. Historical discovery notes stay in the build logs, but
triage, status, and next-session ordering should happen here.

As of S15c, all earlier active pilot-readiness bugs/gaps in this tracker were resolved.
GAP-004 is reactivated after ADR-427 locked durable PWA request-detail navigation behavior.

## Status Legend

- **Open:** Not fixed.
- **Resolved:** Fixed in a committed slice.
- **Deferred:** Known issue, not currently selected for the next implementation slice.
- **Needs decision:** Requires a product/architecture decision before implementation.

## P0/P1 Pilot Flow Bugs

### BUG-001 — Share-intent leaves stale detail version

**Status:** Resolved in S15a
**Severity:** P1
**Area:** `ophalo-app` request detail / Keep backend concurrency

`POST /share-intent` rotates `ConcurrencyVersion`, but the UI only sets a local `shareCleared` flag
and keeps using cached `detail.version`. The next staff action can produce a false `409` conflict.

Expected fix: after successful share-intent `204`, invalidate/refetch request detail, in addition to
or instead of the local flag.

### BUG-002 — `ApiError.extensions` parsing misses flattened ProblemDetails fields

**Status:** Resolved in S15a
**Severity:** P1
**Area:** `ophalo-app` API client / member management errors

ASP.NET Core flattens `ProblemDetails.Extensions` to top-level JSON. `apiClient.ts` reads
`problem["extensions"]`, so extension-driven copy such as `suggestedAction=reactivate|resend_invite`
is unreachable.

Expected fix: in one place in `apiClient.ts`, treat the whole problem object as the extension source
while preserving the existing fallback behavior.

### BUG-003 — Quick Capture navigation uses nonexistent URL routing

**Status:** Resolved in S15b
**Severity:** P1
**Area:** `ophalo-app` Quick Capture / app navigation

`QuickCapture.tsx` navigates with `window.location.href = "/keep/requests/{id}"`, but
`ophalo-app` routing is React state in `App.tsx`, not URL routing. Mobile post-success, "View Request
Workbench", and active-request card tap flows land on the default list after reload or can 404 on a
static host.

Expected fix: pass an `onSelectRequest` callback from `App.tsx` into `QuickCapture`.

### BUG-004 — Magic-link exchange checks obsolete duplicate-email error code

**Status:** Resolved in S15a
**Severity:** P2
**Area:** `ophalo-web` magic-link exchange

`ExchangeClient.tsx` checks `Account.NewAccountEmailAlreadyRegistered`, but the backend emits
`Account.EmailAlreadyInUse`. The duplicate start-to-exchange race falls into generic invalid-link
copy.

Expected fix: check `Account.EmailAlreadyInUse`.

### BUG-005 — Post-capture Copy Tracker Link does not record share intent

**Status:** Resolved in S15a
**Severity:** P1
**Area:** `ophalo-app` Quick Capture / tracker sharing

S13e says successful copy/share should record share intent. `SuccessPanel` copies the tracker link
without recording it, so Needs Share can continue nagging after the operator already shared.

Expected fix: after successful post-capture copy, call share-intent and refresh/align request detail
state as needed.

## Locked-Scope Gaps

### GAP-001 — Assign/clear responsible and watcher management missing

**Status:** Resolved in S15b
**Severity:** P1
**Area:** `ophalo-app` participation controls

S13d locked assign/clear responsible and watcher management. Backend contracts exist:
`PUT /responsible` and `PUT|DELETE /watchers/{id}`, with `canAssignResponsible` and
`canSelfAssignResponsible` computed. The client has no methods and `ParticipationSection` only
supports watch/mute.

Consequence: Operator "Available Work" is a dead end, and Owner/Admin cannot assign work.

### GAP-002 — Customer tracker page `/keep/r/{pageToken}` is missing

**Status:** Resolved in S15c
**Severity:** P1
**Area:** `ophalo-web` customer tracker

Staff share/copy paths build `{PublicBaseUrl}/keep/r/{token}`, but `ophalo-web` has no customer
tracker route. The API endpoint `/keep/r/{pageToken}` is JSON only.

Consequence: tracker links shared with customers 404 until the page exists.

Expected fix: add an `ophalo-web` customer tracker route at `/keep/r/{pageToken}` that fetches the
existing API JSON from `GET /keep/r/{pageToken}` and renders a customer-facing status/tracker page
without leaking the raw page token.

### GAP-003 — Public intake link setup does not appear to expose the usable S14g URL

**Status:** Resolved in S15a
**Severity:** P1
**Area:** `ophalo-app` Settings / public intake setup

S14g added `/keep/intake/{token}` in `ophalo-web`. The existing Settings intake section displays the
active `publicSlug` and calls ensure/replace, but the UI does not appear to construct/copy the full
customer-facing intake URL from the one-time raw token returned by setup.

Expected next step: verify the committed S14g setup flow and either expose the one-time
`{PublicBaseUrl}/keep/intake/{rawToken}` URL after ensure/replace or adjust the route/link contract
deliberately.

### GAP-004 — Browser back / refresh does not preserve app location

**Status:** Open
**Severity:** P2
**Area:** `ophalo-app` navigation

State routing pushes no browser history. S13d assumed standard browser back for detail-to-list; on
mobile PWA the back gesture can exit the app, and refresh on detail loses place.

Decision: ADR-427 locks this as pre-pilot PWA navigation behavior. Browser refresh and direct URL
open must preserve authorized request detail; Requests breadcrumb/back returns to the request list;
the OpHalo Keep logo returns to the request list/home workbench.

## Resolved During Session 22

### GAP-005 — Same-account staff can submit through the public intake form

**Status:** Resolved in S22 (2026-07-09)
**Severity:** P1
**Area:** `OpHalo.Keep.Application` public intake service; `ophalo-web` IntakeForm

Authenticated users who belong to the account that owns a public intake link were able to POST to
`/keep/public-intake/slug/{slug}` and `/keep/public-intake/token/{token}` and create requests
tagged as `public_intake` source. This breaks source integrity and audit semantics — public intake
must represent only customer-submitted requests.

**Fix:**
- `CreateKeepPublicIntakeService.ExecuteWithLinkAsync` now checks `currentUser.IsAuthenticated &&
  currentUser.AccountId == link.AccountId` and returns `keep.public_intake.staff_not_permitted`
  (422) before any database writes. Anonymous users and members of other accounts are unaffected.
- `ErrorHttpMapper` registers the new error code → 422 UnprocessableEntity.
- `IntakeForm.tsx` maps `keep.public_intake.staff_not_permitted` to a staff notice stage: "You're
  signed in to this account — use Quick Capture or Create Request in the app."
- 4 unit tests + 3 integration tests added; all existing tests remain green.

## Resolved During Session 14

### RES-001 — `ophalo-app` AuthGuard redirected to stale `/auth/signin?return_to=...`

**Status:** Resolved in S14f
**Area:** `ophalo-app` auth redirect

S14f changed unauthenticated redirect to `{PublicBaseUrl}/signin` with no `return_to`.

### RES-002 — Homepage customer-start copy became valid after S14g

**Status:** Resolved in S14g
**Area:** `ophalo-web` marketing copy / public intake

Before S14g, "Customers can start a request..." was premature. S14g delivered the public intake page,
so the claim is now backed by a route.

## Minor / Polish / Hardening

### POL-001 — `dev-auth.html` ships in production bundle

**Status:** Resolved in S15b
**Severity:** P2
**Area:** `ophalo-app` production artifact cleanup

`web/ophalo-app/public/dev-auth.html` is copied into every Vite deploy. It adds no new backend
capability, but it is a prototype artifact on a public origin.

Expected fix: exclude it from production builds or remove it after local runbooks no longer need it.

### POL-002 — Settings timezone selector is hardcoded and incomplete

**Status:** Resolved in S15b
**Severity:** P3
**Area:** `ophalo-app` Settings

Settings has a 22-zone hardcoded list that misses common zones such as Phoenix, Honolulu, and UTC,
while `/start` uses the full IANA list. A start-selected zone can later show as un-reselectable
`(custom)`.

Expected fix: reuse the `Intl.supportedValuesOf` approach.

### POL-003 — Manual-share invite URL and clipboard-copy polish

**Status:** Resolved in S15b
**Severity:** P3
**Area:** `ophalo-app` Settings / share sections

Manual-share invite URL stays rendered until unmount, though S13g said show once. Clipboard-copy
failures in share sections are silently swallowed in the `DOMException` branch.

### POL-004 — Composer draft is split across duplicated mobile/desktop mounts

**Status:** Resolved in S15b
**Severity:** P3
**Area:** `ophalo-app` request detail composer

Duplicated mobile/desktop mounts mean a draft typed at one viewport width may disappear after
resizing.
