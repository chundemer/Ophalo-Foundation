# Build Log 069 — Session 15: Pilot Readiness Bug And Gap Closure

**Session:** S15
**Branch:** main
**Status:** In progress

---

## S15a — Pilot Loop Bug Triage And First Fix Set

**Slice status:** Complete (pending commit approval)
**Files changed:** 5 production + 1 doc + 1 new build log

### Fixes landed

#### BUG-001 — Share-intent stale version / false 409
**File:** `web/ophalo-app/src/pages/RequestDetail.tsx`

`handleShareCleared` now calls `queryClient.invalidateQueries({ queryKey: ["request-detail", requestId] })` after setting the local `shareCleared` flag. This triggers a background refetch so the next staff action receives the server's updated `ConcurrencyVersion` rather than the pre-share-intent value.

#### BUG-002 — `ApiError.extensions` flattened ProblemDetails parsing
**File:** `web/ophalo-app/src/lib/apiClient.ts`

All three fetch helpers (`apiFetchVoid`, `apiFetch`, `apiFetchMaybeJson`) changed from:
```ts
extensions = problem["extensions"] as Record<string, unknown> | undefined;
```
to:
```ts
extensions = (problem["extensions"] as Record<string, unknown> | undefined) ?? problem;
```
ASP.NET Core flattens `ProblemDetails.Extensions` to top-level JSON. The fallback to `problem` makes all extension fields (e.g. `suggestedAction`) reachable on `ApiError.extensions` regardless of whether the server uses the nested or flat form.

#### BUG-004 — Magic-link exchange checks obsolete duplicate-email error code
**File:** `web/ophalo-web/src/app/auth/exchange/ExchangeClient.tsx`

Changed `"Account.NewAccountEmailAlreadyRegistered"` → `"Account.EmailAlreadyInUse"` to match the code the backend actually emits on 409. The duplicate-start-to-exchange race now lands on `account_already_exists` copy instead of falling through to the generic invalid-link path.

#### BUG-005 — Post-capture Copy Tracker Link does not record share intent
**File:** `web/ophalo-app/src/components/QuickCapture.tsx`

Added `requestId` to `SuccessPanelProps`. After `navigator.clipboard.writeText` succeeds in `handleCopyLink`, calls `api.recordShareIntent(requestId, "copy_link")` silently (errors swallowed so clipboard success is not blocked). This aligns the post-capture copy path with S13e intent and prevents `NeedsShare` from nagging after the operator has already shared.

#### GAP-003 — Public intake setup does not expose the usable intake URL
**File:** `web/ophalo-app/src/pages/Settings.tsx`

`IntakeSection` now stores the `rawToken` returned by `ensureIntake` (when `created: true`) and `replaceIntake` (always). A one-time panel displays the full `{publicBaseUrl}/keep/intake/{rawToken}` URL with a copy button. The panel is shown immediately after create/replace and dismissed on next page load (the raw token is not retrievable from `getIntake`).

### Verification
- `tsc --noEmit` passed for both `ophalo-app` and `ophalo-web`
- `git diff --check` clean
- No backend changes; no integration tests required

---

## S15b — Navigation, Assign/Watcher, And Polish

**Slice status:** Complete (pending commit approval)
**Files changed:** 6 production + 1 deleted + docs

### Fixes landed

#### BUG-003 — Quick Capture navigation uses nonexistent URL routing
**Files:** `web/ophalo-app/src/App.tsx`, `web/ophalo-app/src/components/QuickCapture.tsx`

`QuickCapture` now accepts `onSelectRequest?: (id: string) => void`. `App.tsx` passes `selectRequest` (which sets React route state) plus `setCaptureOpen(false)`. All three navigation paths (mobile post-capture, "View Request Workbench", "Navigate to existing request") now use the callback when provided and fall back to `window.location.href` when not. This eliminates the page reload / 404 on static host.

#### GAP-001 — Assign/clear responsible and watcher management missing
**Files:** `web/ophalo-app/src/lib/apiClient.ts`, `web/ophalo-app/src/pages/RequestDetail.tsx`

Added four API client methods:
- `setResponsible(requestId, accountUserId, version)` → `PUT /responsible`
- `clearResponsible(requestId, version)` → `DELETE /responsible`
- `addWatcher(requestId, accountUserId, version)` → `PUT /watchers/{id}`
- `removeWatcher(requestId, accountUserId, version)` → `DELETE /watchers/{id}`

`ParticipationSection` now conditionally loads team members (via `useQuery(["members"], enabled: canAssignResponsible)`) and renders:
- Responsible: current person + "Clear" button, or member dropdown + "Assign" when unset.
- Watchers: per-watcher "Remove" buttons + "Add watcher" dropdown filtered to non-watching active members.
- Self watch/mute controls remain unchanged below.

#### POL-001 — `dev-auth.html` ships in production bundle
**File:** `web/ophalo-app/public/dev-auth.html` (deleted)

Removed the file. It had no backend capability; its only use was local runbook convenience which can be replaced by direct API calls.

#### POL-002 — Settings timezone selector is hardcoded and incomplete
**File:** `web/ophalo-app/src/pages/Settings.tsx`

Replaced the 22-entry hardcoded `COMMON_TIMEZONES` array with `Intl.supportedValuesOf("timeZone")` sorted alphabetically. Any zone selected from `/start` now appears as selectable in Settings.

#### POL-003 — Manual-share invite URL and clipboard-copy polish
**Files:** `web/ophalo-app/src/pages/Settings.tsx`, `web/ophalo-app/src/components/NeedsShareBanner.tsx`

- `MemberRow` manual-share URL panel now includes an "×" dismiss button so the URL is not displayed indefinitely.
- `NeedsShareBanner.submit` catch block changed from `!(e instanceof DOMException)` to a plain `else`, so clipboard denial now shows "Could not complete share. Try again." instead of being silently swallowed.

#### POL-004 — Composer draft is split across duplicated mobile/desktop mounts
**File:** `web/ophalo-app/src/pages/RequestDetail.tsx`

`BusinessUpdateSection` `message` and `selectedStatus` lifted to `RequestDetail` as `businessUpdateDraft` / `businessUpdateDraftStatus`. Both mobile and desktop mounts now share the same controlled state, so a draft survives a viewport-width resize.

### Verification
- `tsc --noEmit` passed for `ophalo-app`
- `git diff --check` clean
- No backend changes; no integration tests required

---

## S15c — GAP-002: Customer Tracker Page

**Slice status:** Next. This is the only remaining active pilot-readiness implementation item in
`docs/pilot-readiness-bug-tracker.md`; `GAP-004` remains deferred.

### Intent

Make staff-shared tracker links work for customers. Staff/share flows already build
`{PublicBaseUrl}/keep/r/{pageToken}`, but `ophalo-web` has no matching page. `OpHalo.Api` already
exposes customer-page JSON at `GET /keep/r/{pageToken}`.

### Expected scope

- Add an `ophalo-web` route for `/keep/r/{pageToken}`.
- Read `pageToken` from the route without logging or displaying the raw token as copyable text.
- Browser-fetch `GET ${NEXT_PUBLIC_API_BASE_URL}/keep/r/{pageToken}`.
- Render a customer-facing tracker page using active Keep customer-surface rules.
- Handle unavailable, expired, invalid, cancelled, and closed states according to the existing
  backend response shape and customer-page decisions.
- Avoid third-party scripts, pixels, and external links on the token-bearing page unless explicitly
  approved.
- Preserve referrer/token leakage protections for the token-bearing route.

### Likely file impact

- `web/ophalo-web/src/app/keep/r/[pageToken]/page.tsx`
- optional route-local client component under `web/ophalo-web/src/app/keep/r/[pageToken]/`
- `web/ophalo-web/src/app/globals.css` only if existing auth/customer styles are insufficient
- `docs/build-log/069-session-15-pilot-readiness.md`
- `docs/pilot-readiness-bug-tracker.md`
- `docs/session-log.md`

### Verification

- `pnpm -C web/ophalo-web typecheck`
- `pnpm -C web/ophalo-web build`
- route smoke for missing/invalid token state and a valid seeded page token when available
- `git diff --check`
- mark `GAP-002` resolved in `docs/pilot-readiness-bug-tracker.md` only after the page works.
