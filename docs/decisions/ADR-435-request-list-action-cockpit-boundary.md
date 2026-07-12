# ADR-435 — Request List Action Cockpit Boundary

**Status:** Locked  
**Session:** S24 request workbench quick-action gap  
**Next free ADR after this:** ADR-436

---

## Context

Keep has two authenticated request surfaces with different jobs:

- the request list, used by busy owners/admins/operators to scan and move the queue;
- request detail, used to understand one request deeply and make careful changes.

During S24, request-list quick actions were temporarily implemented as navigation-intent links to
request detail because the end-to-end row action contract is not yet sufficient for safe executable
list mutations.

That fallback is safe, but it is not the product destination. If every quick action forces staff into
an open-detail, perform-action, back-to-list loop, the list becomes a directory instead of the
triage cockpit locked by ADR-145 and ADR-164.

The missing version/action metadata is therefore an API contract gap, not a reason to redefine the
request list as navigation-only.

---

## Decision

The request list remains the speed surface. Request detail remains the depth surface.

### Request List

The list must support low-risk, high-frequency, repeatable actions through compact row controls or
overlay modals once the list payload can do so safely.

List-eligible actions:

- send customer update;
- log external contact;
- add internal note;
- assign/self-assign;
- watch/unwatch;
- clear or acknowledge simple attention when server metadata says the row action is safe;
- mark work done only for no-active-attention cases when server metadata says it is safe;
- close request only from Ready to Close, with explicit confirmation and server metadata.

List actions must remain server-driven. The client must not infer permissions, attention effects,
status eligibility, or concurrency validity locally.

If `close_request` is exposed as a detail/focus action rather than an inline confirmed mutation, the
request-list control must be phrased as navigation/review, for example **Review closeout**. The
destructive **Close request** button remains on request detail unless a dedicated list confirmation
flow is explicitly implemented.

### Request Detail

Detail owns context-heavy, accountability-heavy, or ambiguous work.

Detail-only actions:

- review unresolved feedback and mark feedback reviewed;
- cancellation;
- spam/test/classification;
- edit service location;
- follow-up and planned-date management for V1;
- generic status changes;
- any action where the row does not provide enough context or safe mutation metadata.

The list may expose navigation to these workflows, but it must not present them as completed inline
actions.

### Temporary S24 Fallback

Until the list summary contract exposes safe row-mutation metadata, S24 may use quick-action deep
links or focus-intent links into detail.

Those links are a temporary safety fallback and must not be described as completed executable list
quick actions.

---

## Required List Contract

A follow-up backend/API/frontend contract slice must expose safe mutation metadata on request
summaries before true inline list actions are implemented.

Current finding:

- Backend `KeepRequestSummary` already includes `Version` from `KeepRequest.ConcurrencyVersion`.
- The PWA list summary type/mocks must still be checked and wired to consume `version`.
- `KeepQuickAction` does not yet expose explicit execution metadata such as `requiresVersion` and
  `executionMode`.

Minimum contract to confirm end to end:

```text
KeepRequestSummary.version
```

Recommended action metadata:

```text
quickActions[].requiresVersion
quickActions[].executionMode = inline | modal | detail
quickActions[].customerVisible
quickActions[].internalOnly
quickActions[].clearsAttention
quickActions[].changesStatus
```

The server remains authoritative for which actions appear and which execution mode is safe.

---

## Rationale

Small service businesses need a queue cockpit that works between calls, jobs, and interruptions.
Owners/admins should be able to send a quick update, log a call, add a private note, or handle simple
attention without opening and closing a full detail page for every row.

At the same time, concurrency protection matters. Existing request mutations use
`X-Keep-Request-Version`, and the list currently does not carry that version. Inline row mutations
without a current version would risk stale writes.

This ADR preserves both truths: the list is an action surface, and true inline actions wait for the
contract that makes them safe.

---

## Consequences

- S24 quick-action deep links are explicitly transitional.
- GAP-007 tracks the incomplete end-to-end list concurrency/action metadata.
- A follow-up S24g2/S24g3 path must complete the list contract and then replace eligible deep links
  with inline row modals.
- Feedback review, cancellation, classification, service-location editing, timing controls, and
  generic status changes stay detail-owned for V1.
- The request-list UX should not be accepted as complete while routine safe actions are only fancy
  navigation shortcuts.
