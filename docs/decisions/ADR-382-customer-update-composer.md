# ADR-382 — Customer Update Composer

**Date:** 2026-06-28  
**Status:** Locked  
**Source:** Session 13 S13f customer update composer review; ADR-377; ADR-380; ADR-381; build-log 067

## Context

Keep staff need a simple way to send customer-visible updates from the authenticated workbench.
The backend already exposes:

`POST /keep/requests/{requestId}/business-updates`

with request body fields:

- `message`;
- optional `setStatus`.

The endpoint is versioned with `X-Keep-Request-Version` and returns an updated
`KeepRequestDetailResult` on success.

The detail contract already includes the permissions and hints the composer needs:

- `availableActions.canSendBusinessUpdate`;
- `availableActions.canChangeStatus`;
- `availableActions.allowedStatuses`;
- `validation.businessUpdateMaxLength`;
- `validation.messageRequiredForStatuses`;
- `version`.

## Decision

S13f implements the customer update composer only on request detail. There are no list-row customer
update quick actions in V1.

The primary action label is:

`Send update`

The composer renders only when `availableActions.canSendBusinessUpdate == true`.

Submitting sends:

`POST /keep/requests/{requestId}/business-updates`

with:

- `message`;
- optional `setStatus`;
- `X-Keep-Request-Version: {detail.version}`.

On successful response, the UI adopts the returned `KeepRequestDetailResult` and then clears the local
composer draft.

The composer may include an optional status dropdown, but only when:

- `availableActions.canChangeStatus == true`; and
- filtered `availableActions.allowedStatuses` contains at least one composer-allowed status.

Status options must be sourced from `availableActions.allowedStatuses`, then filtered for S13f. The
composer excludes terminal/destructive/admin statuses even if another flow may allow them:

- `closed`;
- `cancelled`;
- `spam`;
- `test`.

S13f status options are active/forward workflow states present in `allowedStatuses`, such as:

- `scheduled`;
- `in_progress`;
- `pending_customer`;
- `resolved`.

Closeout, cancellation, spam/test classification, and other terminal/destructive changes belong to
dedicated flows, not the customer update composer.

S13f uses manual text only. Canned replies, quick replies, response templates, AI suggestions, and
saved snippets remain out of scope until messaging-boundary work such as DEF-068 is explicitly
decided.

The browser uses `validation.businessUpdateMaxLength` for local length validation. It shows a
character count/countdown, warns near the limit, blocks submit when over limit, and never silently
truncates text.

Side-effect copy stays passive and narrow:

- `Visible on the customer tracker.`
- If a status is selected: `Status will change to {status label}.`

The UI must not predict attention clearing, SLA/first-response effects, notification routing, or
customer read state. The server-owned result and refreshed timeline are the authority after submit.

`NeedsShare` does not block customer updates. If `needsShare == true`, show a passive reminder:

`Tracker link not yet shared with customer.`

Do not auto-share, force share, or call `share-intent` from the customer update submit path.

On `409 KeepRequest.RequestChanged`, the UI preserves the typed message and selected status, disables
stale submit, and prompts refresh. On validation or network errors, it also preserves draft state.

## Rationale

Customer-visible text deserves full request context. Keeping the composer on detail avoids sending
updates from a scan-first row with incomplete context or stale version data.

Using existing detail metadata keeps the frontend from becoming a second workflow engine. The server
decides whether sending is allowed, which status transitions are valid, and what the current version
is.

Filtering terminal/destructive statuses out of the composer keeps this workflow focused: the operator
is sending a customer update, not closing, cancelling, or classifying work by accident.

Keeping `NeedsShare` separate from business updates follows ADR-381. Staff may share the tracker link
verbally, through native SMS, through an email client, or by copy/paste; they should not be forced into
a business update just to record sharing, and sending an update should not pretend that sharing
happened.

## Consequences

- Frontend S13f work is UI/client binding against existing backend contracts.
- No new backend endpoint is required for S13f.
- The composer must preserve draft state on 409, validation errors, and network failures.
- The UI must adopt returned detail after success so version, timeline, status, and actions refresh
  together.
- The status dropdown must be built from `allowedStatuses` and filtered, not hard-coded from generic
  role/status assumptions.

## Deferred

- List-row customer-update quick actions.
- Canned replies, quick replies, templates, saved snippets, and AI suggestions.
- Automated backend SMS/email delivery.
- Attachments.
- Terminal closeout/cancellation/classification through the composer.
- Customer reply inbox beyond existing customer page behavior.
