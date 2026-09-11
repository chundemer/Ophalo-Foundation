# BL152 — GAP-091: Quick Capture draft persistence

**Status:** Implemented, awaiting Christian's diff review — GAP-091 code-complete.
`mobile/ophalo-mobile` tsc clean, `vitest run` 38/38. Revised once after review caught three
correctness issues in the first pass (Close re-triggering its own guard, a generation-counter
race that could erase a newly started capture, and autosave briefly discarding a just-restored
draft) — see Implementation below for the corrected design. Verified again after a second review
pass confirmed the first two fixes and their tests, and flagged that the third fix (the
restore/autosave gate) had no regression test — closed by extracting `useRestoreApplyGate` into
its own directly-testable hook plus a render-level integration test proving the storage
call-order guarantee (`useRestoreApplyGate.test.ts`, `useQuickCaptureDraft.restore.test.ts`;
`react-test-renderer` added as a dev dependency for this).

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
| D6 | **Lifecycle-race protection**, not just a debounce: hydration blocks editing (loading state until the initial AsyncStorage read resolves); every write/removal is serialized through a single per-key promise queue (ordering, not a generation counter, is the guarantee) so discard or successful-submit cleanup can never be resurrected by a write already in flight, nor can a new capture started right after a discard be erased by that discard's own removal settling late; cleanup is awaited before navigating away on success; the draft is preserved on validation/API/network failure; a `useRestoreApplyGate` hook holds autosave off until a restored draft has been applied to form state (or hydration confirmed there was none), so autosave can never observe one render of blank state and wipe a just-restored draft; a best-effort `AppState` background flush is attempted (explicitly not a guarantee against an OS process kill — no client code can promise that). |
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
- `src/hooks/useRestoreApplyGate.ts` (new) — extracted out of `modal.tsx` so the restore/autosave
  race fix is directly testable without mounting the full screen: applies a restored draft
  exactly once, synchronously within the same effect/render that flips its returned flag to
  `true` (whether or not there was anything to restore). React batches the consumer's field-state
  updates together with that flip into one commit, so a caller gating its own autosave effect on
  the returned flag can never observe a render where the gate is open but the restore hasn't
  landed yet — closing the exact race the second review pass caught (no regression test on the
  first pass's fix).
- `app/modal.tsx` — hydration gate (loading state before `hydrated`); autosave gated on
  `useRestoreApplyGate`'s returned flag instead of a hand-rolled `restoreApplied` state/effect
  pair, so the very first post-hydration render — where React state would otherwise still be
  blank because the restore hasn't been applied yet — can't have autosave discard a just-restored
  draft; restored-draft banner + "Discard draft" action;
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
  old write/removal settling late. `src/hooks/__tests__/useRestoreApplyGate.test.ts` (new, 4
  cases) proves the gate applies a restored draft in the same commit that opens (`react-test-renderer`,
  no JSX — `.ts` test files use `React.createElement`). `src/hooks/__tests__/useQuickCaptureDraft.restore.test.ts`
  (new, 1 case) wires the real `useQuickCaptureDraft` + `useRestoreApplyGate` together — the same
  pairing `modal.tsx` uses — behind a mocked `AsyncStorage`, and asserts the exact storage call
  order on reopening a persisted draft: a single `getItem` read followed by one `setItem` of the
  *restored* fields, with no `removeItem` and no blank `setItem` ever occurring first. Added
  `react-test-renderer@19.2.3` (pinned to the installed `react` version) as a dev-only dependency
  for these two files.

## Known gap, not addressed here

`modal.tsx`'s post-success flow `router.replace()`s straight into request-detail. Planning
documents describe post-save Quick Capture as staying in the capture flow with
confirmation/actions. GAP-091 does not change this — noted here so it isn't read as aligned
behavior; a future gap should pick it up if it becomes a product priority.
