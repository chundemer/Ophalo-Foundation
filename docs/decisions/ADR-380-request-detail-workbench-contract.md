# ADR-380 — Request Detail Workbench Contract

**Date:** 2026-06-28  
**Status:** Locked  
**Source:** Session 13 S13d pre-code review; ADR-370; ADR-372; ADR-377; build-log 067

## Context

Session 13 builds the authenticated Keep PWA workbench. The request detail surface is the operational
center of that workbench: staff open one request, understand its state and history, contact the
customer, acknowledge attention, adjust participation, and safely perform versioned writes.

The backend already exposes an enriched authenticated detail contract, `KeepRequestDetailResult`.
That response includes the event timeline, server-computed available actions, validation hints,
contact affordances, participants, `NeedsShare`, `PageToken`, and the current concurrency version.

The S13d UI must therefore bind to the existing server contract rather than inventing a parallel
client policy layer.

Tracker sharing is part of the detail workbench, but the original detail action metadata did not
include a share-intent permission flag. S13d/S13e require that flag so the frontend does not infer
share permissions from role names.

## Decision

S13d uses `GET /keep/requests/{requestId}` and `KeepRequestDetailResult` as the request detail source
of truth.

The authenticated request detail workbench must:

- render the unified timeline from `detail.events`;
- render action availability from `detail.availableActions`;
- render local form limits from `detail.validation`;
- render contact launch affordances from `detail.contactActions`;
- construct tracker sharing gestures from `detail.pageToken` without exposing the raw token in a
  visible input;
- use `detail.needsShare` for sticky/share-required UI;
- use `detail.availableActions.canRecordShareIntent` for tracker share controls;
- use the GUID `detail.version` as the expected version for versioned mutations.

Every S13d versioned mutation sends:

`X-Keep-Request-Version: {detail.version}`

On mutation success, the workbench must adopt the returned `KeepRequestDetailResult` when the
endpoint returns one. That keeps the local version, timeline, metadata, and available actions aligned
with the server after each write.

If a versioned mutation returns `409 KeepRequest.RequestChanged`, the workbench must preserve any open
form input, stop further submission from that stale form, and tell the operator to refresh/retry from
latest state. The UI must not silently throw away typed notes.

S13d includes these baseline actions:

- status change;
- log external contact;
- acknowledge attention;
- assign/clear responsible;
- add/remove watchers where allowed;
- self watch/unwatch;
- mute/unmute;
- copy/native/manual tracker share intent clearing.

Before frontend implementation, `AvailableActionsMetadata` must be extended with
`CanRecordShareIntent`, exposed as JSON `availableActions.canRecordShareIntent`. The value must be
computed server-side from the same permission/access posture as `POST /keep/requests/{requestId}/share-intent`.

S13d explicitly excludes Follow Up On, Planned For, the customer-visible update composer, closeout,
feedback review, classification, reporting, and account/team settings unless a later slice pulls them
forward.

## Rationale

The request detail surface has the highest risk of accidental client-side policy drift. The backend
already owns tenant isolation, row visibility, role permissions, membership state, OffSeason/read-only
behavior, allowed status transitions, validation limits, feedback visibility, and concurrency.

Using `KeepRequestDetailResult` directly keeps the frontend simple and makes server authority visible:
the UI can be rich and responsive without becoming a second policy engine.

The versioning rule is especially important. Keep uses a GUID concurrency version, not an integer.
Client code must send the exact version returned by detail and then accept the rotated version returned
after successful writes.

The tracker link is also bearer-like. Staff need copy/share gestures, but S13d should not render the
raw page token in a normal input field where it is easier to scrape, copy accidentally, or expose in
screen captures.

Share intent is not currently a status/contact/participation action, so adding explicit metadata keeps
the broader S13d pattern intact: the server tells the frontend which actions are available.

## Consequences

- S13d frontend work should add typed request-detail DTOs matching the current backend response.
- The frontend API client should support per-request headers for `X-Keep-Request-Version`.
- The action rail must be driven by `availableActions`, not local role guesses.
- Tracker share controls must be driven by `availableActions.canRecordShareIntent`.
- Viewer/read-only detail treatment must be derived from server-visible detail plus false action
  flags.
- The timeline must tolerate unknown future event/action slugs with neutral fallback labels.
- Contact launchers remain convenience affordances; durable state changes only after a saved external
  contact log.
- Share intent clearing uses `POST /keep/requests/{requestId}/share-intent` with `copy_link`,
  `native_share`, or `manual_mark_shared`.

## Deferred

- Dedicated next/previous queue navigation UI.
- Customer-visible update composer.
- Follow Up On and Planned For controls.
- Closeout and feedback-review workbench flows.
- Classification/spam/test controls.
- Public customer tracker implementation in `ophalo-web`.
