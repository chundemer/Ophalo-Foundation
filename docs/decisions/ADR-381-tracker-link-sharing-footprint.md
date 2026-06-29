# ADR-381 — Tracker Link Sharing Footprint

**Date:** 2026-06-28  
**Status:** Locked  
**Source:** Session 13 S13e tracker-sharing review; ADR-370; ADR-372; ADR-380; build-log 067

## Context

Staff-created Keep requests start with `NeedsShare=true` because the customer tracker page is useful
only after the business has actually initiated sharing the link. The backend already supports
share-intent clearing through:

`POST /keep/requests/{requestId}/share-intent`

with method values:

- `copy_link`;
- `native_share`;
- `manual_mark_shared`.

The endpoint records intent. It does not prove delivery, prove that the customer opened the link, or
send backend SMS/email.

The tracker page token is a bearer-like value exposed on authenticated request detail. Its UI
footprint should be intentionally narrow.

## Decision

Tracker link access appears in these places only:

- post-capture desktop confirmation panel: `Copy Tracker Link`;
- request detail workbench: copy/native/manual share controls;
- mobile request detail with `NeedsShare=true`: sticky banner,
  `Customer Tracker Link Not Shared. [Tap to Share]`.

List rows must not include the tracker URL or page token. List rows may show `NeedsShare` status, but
sharing happens from detail.

After `NeedsShare` is cleared, the request detail workbench must keep tracker access available but
de-emphasized. The banner/urgent treatment disappears; the operator can still copy/share the tracker
link later.

The UI must offer only the backend-supported methods:

- `Copy Link` -> `copy_link`;
- native share affordance -> `native_share`, only when `navigator.share` is available;
- `Mark Shared` -> `manual_mark_shared`.

Call `share-intent` only after the operator action succeeds or is explicitly confirmed:

- after clipboard copy succeeds;
- after the `navigator.share()` promise resolves;
- after manual mark shared confirmation.

Manual/recorded sharing copy:

`Recording this marks that you've initiated sharing the tracker link. It does not confirm the customer received it.`

Sharing the tracker link remains separate from customer-visible business updates. Do not couple
`share-intent` with `POST /keep/requests/{requestId}/business-updates`.

Before frontend implementation, detail action metadata must expose
`availableActions.canRecordShareIntent`. The frontend must render share controls from that server flag,
not from local role checks. A `403` response remains a defensive fallback.

Expected server policy for `canRecordShareIntent`:

- Owner/Admin: true for account-wide visible requests when account/access posture allows writes;
- Operator: true only for MyWork-visible requests when account/access posture allows writes;
- Viewer: false;
- OffSeason/read-only/blocked account posture: false.

## Rationale

The tracker token should be easy for staff to share intentionally and hard to leak accidentally.
Keeping the token out of list rows limits its footprint and keeps the list scan-first.

The three share methods match the backend audit vocabulary. Adding extra UI-only methods would create
translation ambiguity and unnecessary future reporting cleanup.

The micro-copy matters because `ShareIntentRecorded` is not delivery proof. The product should be
honest without burying operators in warning text.

Keeping share and business update flows separate preserves real field behavior: staff may share
verbally, through native SMS, through an email client, or by copy/paste without needing to compose a
Keep customer-visible update.

## Consequences

- List DTOs should not gain `PageToken` or tracker URL fields.
- Detail remains the primary tracker-sharing surface after quick capture.
- The frontend must not render raw page tokens in visible/copyable text inputs.
- The frontend should handle `204` from share-intent by clearing/refetching visible `NeedsShare`
  affordances and must not create fake timeline rows client-side.
- Standard `isSubmitting` locks are enough for duplicate-click protection; server idempotency prevents
  repeated cleared-state calls from creating duplicate share events.
- S13d/S13e backend work must add `CanRecordShareIntent` to `AvailableActionsMetadata`.

## Deferred

- Backend SMS/email delivery.
- Delivery/read receipt or proof-of-open semantics.
- Link rotation/revocation UI.
- Identity-bound customer access.
- List-row share quick actions.
