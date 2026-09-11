# BL152 — GAP-091: Quick Capture draft persistence

**Status:** Implemented, awaiting Christian's diff review — GAP-091 code-complete.
`mobile/ophalo-mobile` tsc clean, `vitest run` 33/33. Revised once after review caught three
correctness issues in the first pass (Close re-triggering its own guard, a generation-counter
race that could erase a newly started capture, and autosave briefly discarding a just-restored
draft) — see Implementation below for the corrected design.

**Scope reference:** [workboard](../workboard.md) Next item 4 (AUDIT-V5-A); audit
[production-readiness audit](../audits/production-readiness-audit.md) Vector 5 F5.1.
**Supervised-pilot gate** — before field techs rely on Quick Capture.

## Problem

Confirmed in code (`mobile/ophalo-mobile/app/modal.tsx`): the real S17f Quick Capture form
(phone, name, email, description, source, service address) held its entire state in `useState`
only. Offline disabled Save with no local save and no send-when-online queue; there was no
`beforeRemove`/dismiss guard anywhere in the mobile app. A tech capturing a job with no signal,
or who backgrounds/swipes away the modal, lost the whole capture.

## Locked decisions

| # | Resolution |
|---|------------|
| D1 | **Single-slot draft**, not multi-draft/per-request — Quick Capture is pre-creation (no `requestId` to key by) and only one capture is ever in flight in the modal. |
| D2 | **Per-user key**: `@ophalo/quickCaptureDraft:{accountUserId}:v1`. Draft is removed in `AuthContext.logout()` and in the `setOn401Handler` forced-sign-out path, so a shared field device never surfaces a prior user's captured PII. |
| D3 | **Plain AsyncStorage** — explicit, short-lived, per-user, authenticated-device exception for this pilot workflow (not a general statement about the app's storage posture, and not a SecureStore replacement candidate given free-text description length). |
| D4 | **No TTL.** `savedAt` is stored for display/schema evolution; enforcing an expiry would recreate the exact data-loss failure this gap exists to close, with no retention-policy basis for a number. |
| D5 | **Non-destructive close vs. destructive discard are two separate flows.** Dismissal (Cancel tap, swipe, back) while the form has content shows "Save draft and close?" → *Keep editing* / *Close* — Close flushes the pending write and leaves the draft persisted for restore. A separate, always-available "Discard draft" action (shown whenever the form has content, restored or freshly typed) clears storage after its own destructive confirm. Restored drafts show a timestamped banner. |
| D6 | **Lifecycle-race protection**, not just a debounce: hydration blocks editing (loading state until the initial AsyncStorage read resolves); a generation counter cancels a queued/in-flight write on discard or successful-submit cleanup so it cannot resurrect the draft; cleanup is awaited before navigating away on success; the draft is preserved on validation/API/network failure; a best-effort `AppState` background flush is attempted (explicitly not a guarantee against an OS process kill — no client code can promise that). |
| D7 | **Package manager**: `package-lock.json` / npm is canonical for `mobile/ophalo-mobile` (more recently touched than `pnpm-lock.yaml`; no CI/`packageManager` pin exists). `pnpm-lock.yaml` is deliberately left untouched and stale — **not to be used for mobile installs** until reconciled or removed. That reconciliation is out of scope here. |

## Implementation

- `src/hooks/quickCaptureDraft.ts` (new) — framework-free: draft schema/version, per-user key,
  `isDraftBlank`, `parseStoredDraft` (malformed/unknown-version → discard), and
  `QuickCaptureDraftCoordinator`. **Storage operations (writes, removals, and hydrate's
  cleanup-on-malformed-read) are serialized through a single per-key promise queue, executed
  strictly in invocation order** — not a generation-counter-and-undo heuristic, which was tried
  first and caught by review: it only proves one narrow race (an in-flight write landing after
  its own discard) and does not stop a *new* save started right after a discard from being
  wiped out if that discard's removal is still resolving when the new write lands. The queue
  makes ordering, not counting, the guarantee: `discard()` only resolves once any write already
  ahead of it in the queue has landed *and* been removed, and any save scheduled after that
  point is itself queued strictly behind the removal. Storage is injected (`DraftStorage`
  interface) so the coordinator is directly unit-testable without RN test infra — mirrors the
  existing `phoneUtils.ts` / `useQuickCapture.ts` split.
- `src/hooks/useQuickCaptureDraft.ts` (new) — thin React binding: hydrate-on-mount/account-change,
  `AppState` background-flush subscription, `saveDraft` / `flushDraft` / `discardDraft`.
- `app/modal.tsx` — hydration gate (loading state before `hydrated`); a `restoreApplied` flag set
  by the restore effect (whether or not there was anything to restore) that gates the autosave
  effect, so the very first post-hydration render — where React state is still blank because the
  restore effect hasn't run yet — can't have autosave discard a just-restored draft ahead of it
  being re-applied 300ms later; restored-draft banner + "Discard draft" action;
  `useNavigation().addListener('beforeRemove', …)` guard covering every dismissal path (Cancel
  tap included — Cancel stays a plain `router.back()`; the single `beforeRemove` listener is the
  one place the confirm lives, avoiding a double-dialog from two independent checks). The Close
  choice inside that guard sets `suppressGuardRef.current = true` **before** re-dispatching the
  original removal action — without it, the re-dispatched removal re-fires the same
  `beforeRemove` listener and reopens the confirm indefinitely (caught by review). Successful
  create: `await discardDraft()` then the same `suppressGuardRef` flag before
  `router.replace(...)`, so the post-submit navigation doesn't itself trip the dirty guard
  against not-yet-reset form state.
- `src/auth/AuthContext.tsx` — `clearQuickCaptureDraft` wired into `logout()` and the 401 handler;
  the 401 handler reads a `userRef` (not the `user` state closure, which it was registered against
  once on mount) so it always clears the account that was actually signed in at 401 time.
- Dependency: `@react-native-async-storage/async-storage` via `npx expo install` (SDK-57-pinned
  version, not a hand-picked semver — this is a native module). `package.json` +
  `package-lock.json` only, per D7.
- Navigation-guard spike: `usePreventRemove` is not exported from `expo-router`'s public
  entrypoint (only reachable via an internal `build/react-navigation/core/...` path — not used).
  `useNavigation` **is** public (`expo-router`'s own docs: "the full navigation API is available
  directly from `expo-router` — no `@react-navigation/*` install required") and its returned
  navigation object's `addListener('beforeRemove', …)` is what's wired in `modal.tsx`.
- Tests: `src/hooks/__tests__/quickCaptureDraft.test.ts` (new, 18 cases) — key scoping/isolation,
  blank-vs-content detection, `parseStoredDraft` malformed/wrong-version/missing-field rejection,
  debounce timing, discard cancels a pending write, and three reproduced races proven via an
  in-memory fake storage with artificial latency (not real timers/AsyncStorage): an in-flight
  `setItem` landing after `discard()`'s `removeItem`; a late autosave write landing after
  successful-submit cleanup; and — added after review — a new capture started immediately after
  a discard while the old write is still in flight, proving the new capture's write survives the
  old write/removal settling late.

## Known gap, not addressed here

`modal.tsx`'s post-success flow `router.replace()`s straight into request-detail. Planning
documents describe post-save Quick Capture as staying in the capture flow with
confirmation/actions. GAP-091 does not change this — noted here so it isn't read as aligned
behavior; a future gap should pick it up if it becomes a product priority.
