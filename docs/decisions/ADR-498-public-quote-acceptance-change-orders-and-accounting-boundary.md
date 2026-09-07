# ADR-498 — Public Quote Acceptance, Change Orders, and Accounting Boundary

**Status:** Locked  
**Date:** 2026-09-07  
**Related:** ADR-473; ADR-475; ADR-487; ADR-493; DEF-086; DEF-088

## Context

The internal, request-bound quote foundation remains intentionally separate from customer delivery
and acceptance (ADR-473 and ADR-475). Customer approval is nevertheless a normal field-service
workflow and OpHalo will own that commercial approval record rather than treating a staff email or
an accounting-system artifact as the source of truth. The public capability needs a bounded,
auditable contract before it is scheduled.

## Decision

### D1 — OpHalo owns the public commercial approval record

An opaque capability link opens exactly one immutable, customer-safe `QuoteRevision`. The public
page shows its scope and price presentation, applicable terms link and displayed terms version,
expiry, and a downloadable durable copy. The revision can be accepted or declined only as a whole;
partial line acceptance, option groups, customer edits, and free-standing quotes are outside this
slice.

The acceptance flow requires the customer to type their full name and actively consent to conduct
the transaction electronically. A drawn signature is not required in this first slice. The product
must not claim that acceptance is "legally binding" until counsel has approved the terms, consent
copy, evidence policy, retention period, and applicable jurisdictional requirements.

### D2 — Revision, expiry, revocation, and audit rules are server-authoritative

Quote content is immutable once a public revision is issued. The server atomically records either
the terminal acceptance or decline and freezes a signed acceptance snapshot. A revision that has
expired or was revoked before a terminal decision cannot be accepted; revocation never changes a
previously recorded acceptance. New commercial scope or price is represented by a new linked
revision, never by editing an issued or accepted revision.

The acceptance snapshot retains, at minimum: revision identity and content hash; quoted business
identity; displayed terms version; typed name; accepted/declined outcome; timestamp; capability-link
identity; and durable-copy delivery record. IP address and user-agent, if collected, are protected
audit data, not business-visible by default; their retention policy must be explicitly decided
before collection.

### D3 — Durable receipt is a real delivery capability

After acceptance, OpHalo provides the customer a durable PDF/email copy and preserves a durable
retrieval path. This requires real transactional delivery, verified sender configuration, and
delivery/bounce handling; generating a PDF or opening `mailto:` alone does not satisfy the contract.
The signed copy contains the accepted snapshot appropriate for the customer, without exposing
protected audit data by default.

### D4 — Change orders preserve accepted work

An accepted revision remains valid for its authorized scope. Newly chargeable scope is a linked
change-order revision with its own all-or-nothing customer decision. It does not revoke or silently
alter the original acceptance, and it must not halt already-authorized or safety-critical work.

### D5 — Accounting remains downstream

OpHalo owns request-bound scope, quote approval, actuals, and commercial audit history. Accounting
remains authoritative for invoices, payments, taxes, and the ledger. A CSV or future accounting
handoff exports a reviewed immutable `BillingRevision` (ADR-493), not a quote; no quote acceptance
creates an invoice, payment, tax calculation, or accounting synchronization.

## Consequences

- This locks the public acceptance product contract but authorizes no implementation or scheduling.
  The existing internal quote foundation and the current pilot sequence remain prerequisites.
- The implementation preflight must define capability-link issuance/rotation and customer-safe
  rendering, revision lifecycle/state transitions, PDF generation/storage/retrieval, transactional
  email delivery and bounce behavior, authorization, rate limiting, audit-data protection, terms and
  consent configuration, retention, and end-to-end acceptance evidence.
- Multi-option/Good-Better-Best presentation, customer signature drawing, payments, invoice
  creation, accounting sync, and legal conclusions beyond counsel-approved copy remain out of scope.

