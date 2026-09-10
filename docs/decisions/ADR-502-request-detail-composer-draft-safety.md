# ADR-502 — Request-detail composer draft safety

**Status:** Locked
**Date:** 2026-09-10 (§1 shared-store and §4 `beforeunload`-only points tightened same day after
review)
**Context:** GAP-073 (production-readiness audit Vector 4 F4.1–F4.2, Vector 5 F5.2–F5.3). The
request-detail unified composer keeps the customer-update draft, its optional status change, and
the internal-note text in React state only. `RequestDetail` is not keyed by `requestId` and Prev/Next
navigation swaps the route without remounting, so a draft typed for request A renders in request B's
composer and "Post update & prepare SMS" then sends it to B's customer. On a `409` the composer goes
`disabled` (not `readOnly`) and the banner tells the operator to refresh — following that instruction
destroys the draft, and the disabled field cannot even be selected to copy the text out. There is no
`beforeunload` / `pagehide` guard anywhere in `ophalo-app`. This is a supervised-pilot blocker
(before staff send customer updates).
**Related:** ADR-382 (customer-update composer — "drafts preserved on conflicts/errors"; this ADR
makes that durable), ADR-403 / ADR-484 (online-first; component-local drafts preserved on network
failure; no queued/replayed mutations), ADR-491 (two-domain customer communication — the composer
being protected), GAP-085 (broader accessible-409 recovery — explicitly out of scope here)

## Decision

### 1. Per-request `sessionStorage` composer draft (no `RequestDetail` remount)

- The draft holds three fields — `message`, `status`, `note` — persisted to `sessionStorage` under
  the key `keep:composer-draft:{requestId}` as a single JSON object.
- **Single shared per-request store, not two independent stateful hook instances.**
  `BusinessUpdateSection` (message + status) and `UnifiedComposer` (note) both consume the draft;
  if each owned its own `useState` copy of the whole object, a write or a clear-on-success from
  one composer would clobber the other composer's newer field (stale-object last-write-wins).
  Instead there is one external store per `requestId` — the in-memory object is the single source
  of truth, `subscribe` / `getSnapshot` are read via `useSyncExternalStore`, and every mutation is
  `setField(name, value)` / `clearFields(names)` on that store, which re-serializes its own current
  state to `sessionStorage` synchronously. Both composers therefore always agree on one draft
  object and re-render together on any field change. Each consumer still only *edits* its own
  fields.
- Exposed to components as a thin `useComposerDraft(requestId)` hook wrapping the store lookup +
  `useSyncExternalStore`. The current prop-drill of `customerUpdateDraft` /
  `customerUpdateDraftStatus` from `RequestDetail` through `RequestDetailContent` →
  `RequestDetailWorkCanvas` → `UnifiedComposer` is removed; the internal-note text (today local
  `useState` in `UnifiedComposer`) moves onto the shared store.
- Keying by `requestId` is what fixes the wrong-customer bug: changing `requestId` reads that
  request's own draft (or an empty one). **`key={requestId}` on `RequestDetail` is not adopted** —
  a full remount would also discard unrelated transient UI state (open panels, workspace tab,
  timeline filter, scroll position, in-flight conflict flags), some of which should persist across
  Prev/Next. The draft is isolated instead of the whole subtree.
- `sessionStorage` (not `localStorage`): a draft is tab-scoped working text, not cross-session
  state; it dies with the tab, which is the correct lifetime. Matches the existing
  `RequestDetailContent` / `RequestMemoryRail` tab-memory precedent. All reads/writes are
  `try/catch`-guarded and degrade to in-memory-only when storage is unavailable.

### 2. Clear only the successfully submitted field

- On a `2xx` customer-update post: clear `message` and `status` from the stored object; leave
  `note`.
- On a `2xx` internal-note save: clear `note`; leave `message` / `status`.
- No TTL and no other automatic clearing. `sessionStorage` expiry (tab close) is the only
  lifetime bound. A conflict, a validation error, or a network failure never clears a draft.

### 3. `readOnly`, not `disabled`, on a `409`

- On a stale-version conflict the composer textareas and the status `<select>` become `readOnly`
  (the `<select>` uses `aria-disabled` + ignored `onChange` since `readOnly` is not valid on
  `<select>`), not `disabled`. The operator can still select, copy, and read their text.
- The submit button stays `disabled` while the conflict flag is set.
- The conflict flag resets when `detail.version` advances (e.g. a refetch-on-focus brings the
  request to current), so the composer recovers without a full page reload. This
  version-advance reset is on **both** composer paths — the customer-update composer
  (`BusinessUpdateSection`) and the internal-note composer (`UnifiedComposer`) — each holding its
  own conflict flag; the draft text is untouched by the reset.
- The existing conflict banner copy stays, with "Your message is saved here" now literally true
  across a reload.

### 4. Shared unload dirty registry — the two composers only

- A module-level dirty registry (`Set` of string ids) with a single lazily-installed
  **`beforeunload`** listener that calls `preventDefault()` / sets `returnValue` to raise the
  browser's native "Leave site?" warning while the set is non-empty, and a
  `useUnloadGuard(id, isDirty)` hook that registers / deregisters an id.
- **`beforeunload` only.** `pagehide` and `visibilitychange` cannot raise the leave prompt, and a
  hard crash runs no handler. The shared store (§1) already writes to `sessionStorage`
  synchronously on every field change, so there is no deferred draft state for a `pagehide`
  flush to rescue — it is not added. Crash / force-quit recovery is covered passively by that
  synchronous `sessionStorage` draft, not by a listener. (If a future change debounces the store
  write, that is the point to add a `visibilitychange: hidden` flush — out of scope here.)
- **Scope: the customer-update composer and the internal-note composer only.** The ~6 existing
  hand-rolled sheet dirty-guards (`DetailPanels`, `ReplaceVisitForm`, `NoChargeDispositionForm`,
  `FinancialResolutionForm`, `LogContactModal`, `ExternalContactForm`) are **not** migrated onto
  the registry in this slice — that is a non-blocking fast-follow.
- The registry does **not** add an in-app SPA discard-confirm dialog for Prev/Next. With per-request
  drafts (§1) a Prev/Next no longer loses anything — the draft is still there on return — so no
  interstitial is needed. The registry only covers prompt-capable document-unload paths (Cmd-R,
  tab close, OS reload); a hard crash / force-quit is not prompted and is recovered from passively
  via the `sessionStorage` draft in §1.

### 5. Boundary — display safety, not mutation queueing

- This is client-side **display** draft safety: it restores text into an input after a reload. It
  does **not** queue, persist server-side, or replay mutations, and it introduces no offline write
  path. It stays entirely inside ADR-403 / ADR-484 (online-first; server-authoritative mutations;
  no queue/replay). ADR-382's "drafts preserved on conflicts/errors" is upgraded from in-memory to
  tab-durable; nothing else in those ADRs changes.

## Consequences

- `GET`/`POST` contracts are unchanged; no API, domain, or migration work. Frontend-only,
  `web/ophalo-app`.
- The customer-update draft prop-drill through four components is removed; `RequestDetail` no
  longer owns `businessUpdateDraft` / `businessUpdateDraftStatus`.
- A cross-tab edit of the same request in two tabs shares one `sessionStorage` namespace per tab
  (sessionStorage is not shared across tabs) — each tab keeps its own draft, which is correct.
- Broader `409` recovery for the non-composer conflict flags set in ~10 places and reset nowhere
  (audit F4.9 / F4.10 / F4.16) remains **GAP-085**, not this slice.
- Migrating the existing sheet dirty-guards onto the shared registry is a tracked fast-follow.
