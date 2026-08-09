# Build Log 115 — Request Field Evidence Image Storage Preflight

**Status:** Paused pending Price Book continuation — implementation is not authorized  
**Date:** 2026-08-09  
**Scope:** Durable, request-bound internal image evidence for Keep.  
**Related:** Build 105; ADR-471; ADR-472; the R2 business-document storage runbook; Session 2e
completion.

## Purpose

This record preserves the Request Field Evidence decisions for a later, separate preflight. It is
not a generic file-upload feature and it does not authorize code by itself. Price Book continuation
is now the next required code preflight; see Build Log 116. The goal remains a production-durable
image-evidence capability with deliberately bounded product scope; it must not become a disposable
implementation that needs replacing when usage grows.

The controlling records remain:

- [Build Log 105](105-request-field-evidence-photo-capability-discovery.md) for the original field
  evidence workflow, safety, and failure requirements.
- [ADR-471](../decisions/ADR-471-private-business-document-storage.md) for the locked private R2
  storage seam and opaque-key rules.
- [ADR-472](../decisions/ADR-472-direct-pricebook-onboarding-and-deferred-csv-import.md) for the
  removal of CSV-import use; images are the first active storage use.
- [R2 setup runbook](../runbook/r2-business-document-storage-setup.md) for the production bucket,
  credential, and deployment configuration.

If this record conflicts with an ADR, the ADR wins. If preflight finds a conflict with the current
implementation, stop and record the issue rather than silently changing a locked boundary.

## Decisions already made

### 1. Product boundary

The first active storage capability is **Request Field Evidence**: authenticated internal staff add
approved images to an existing Keep request. It is evidence and operational history, not a generic
file manager or a public media feature.

The first slice excludes customer/public upload or display, video, PDFs, arbitrary documents,
unlimited files, offline mutation queues, background synchronization, AI/OCR, automatic equipment
identification, and asset-history/QR work. Those require separate decisions.

### 2. Durable storage and tenancy boundary

Images use the locked, private Cloudflare R2 `IBusinessDocumentStorage` seam. The application—not a
client—owns provider credentials and opaque key generation. Database rows store image metadata and
the opaque object key only; they never store image bytes or a permanent public URL.

Every write, list, retrieval, and deletion operation must pass account, entitlement, permission,
and request-row policy checks. Object keys must contain no customer names, addresses, phone numbers,
email addresses, or capability tokens.

### 3. Upload transport

The first implementation uses one authenticated multipart upload contract to the .NET API. The API
validates and bounds the incoming stream, then writes it through `IBusinessDocumentStorage` to
private R2. This is a complete production architecture, not a temporary route pending a presumed
presigned-upload migration.

The existing storage interface deliberately does not enforce size or request-row limits; the image
application/API boundary must enforce them before calling `PutAsync`. A failed post-write metadata
step must use `DeleteBestEffortAsync` so it cannot leave an apparently successful evidence record.

Presigned R2 upload or download URLs are not authorized by this record. ADR-471 permits a later
purpose-bound design if measured operational evidence warrants it; adopting it requires a separate
decision covering authorization, expiry, CORS, completion verification, idempotency, and orphan
cleanup.

### 4. Client image preparation is an optimization, not authorization

Web and native clients may prepare an image before upload to reduce time and storage consumption,
but this can never be the enforcement boundary. The API must independently validate the actual
stream's allowed type/signature, byte count, dimensions, and request/account limits.

The exact client preparation library and the web/Expo behavior are preflight proof items. Do not
assume a browser `FormData` path behaves identically in Expo/React Native, or claim resumable/offline
behavior that has not been designed and tested.

### 5. Retention is a policy decision

No automatic 30-, 60-, or 90-day deletion rule is implied by storage cost. Evidence retention,
deletion authority, audit posture, export/account-deletion behavior, and any legal/insurance
obligation must be explicitly decided before implementation.

## Decisions the preflight must resolve

| Area | Required decision/proof |
| --- | --- |
| Attachment anchor | Whether photos attach to the request generally, a specific business update, or a completed-work/evidence event; whether before/after grouping is needed. |
| Roles and policy | Exact upload, view, delete, and export rules by role and request visibility/state. Customer viewing remains excluded unless separately approved. |
| Limits and formats | Per-file byte cap, per-request count, account quota/limit behavior, allowed encoded formats including the HEIC decision, and server behavior at every limit. |
| Image safety | Signature and MIME validation, dimension/pixel-bomb limits, malformed-image handling, SVG exclusion/handling, malware-scanning decision, derivative/thumbnail policy, and EXIF/GPS retention/exposure policy. |
| Metadata and lifecycle | Required metadata fields, durable states (including pending/failed/available/deleted), captions and their audit/validation posture, cleanup schedule, delete semantics, and final retention rule. |
| Retrieval | Authorized API byte proxy versus short-lived signed R2 GET after API authorization; expiry, cache controls, revocation, error behavior, and assurance that no permanent public URL is produced. |
| Idempotency | A concrete strategy preventing duplicate objects/metadata when a client retries or double-taps on an unreliable connection. |
| Connectivity | Stated upload retry behavior, user-visible slow/failure state, mobile backgrounding behavior, and a deliberate retry-from-scratch versus resumable-upload decision. No offline queue/replay is implied. |
| Native proof | Current Expo/React Native multipart upload behavior for the chosen image limits, including memory, cancellation, retry, backgrounding, and authenticated-session behavior. |
| Operations | R2/API outage behavior, redacted diagnostics, storage-cost monitoring, credential rotation, and support investigation procedure. |

## Required implementation invariants

```text
entitlement -> account may use Request Field Evidence
permission  -> active staff member may take the proposed action
request policy -> actor may act on this specific request and state
validation -> stream is accepted only within locked safety and size bounds
storage -> opaque private object reference; no blob or permanent public URL
metadata -> evidence becomes visible only after storage and metadata both succeed
```

The implementation must prove:

- cross-account or unauthorized actors cannot upload, enumerate, retrieve, thumbnail, or delete;
- retry/double-submit cannot create duplicate evidence records or orphaned durable objects;
- a failure leaves no false success state, and cleanup is observable without exposing credentials or
  object URLs;
- browser and native clients provide truthful progress/failure messaging and never promise later
  synchronization unless an explicit queue/replay capability is built;
- retention/deletion behavior follows the approved policy; and
- storage credentials, presigned URLs if ever used, customer data, and raw image metadata do not
  enter client bundles, telemetry, or unsafe logs.

## Exit gate for the preflight

Before implementation begins, the preflight must produce: the resolved decision table above; exact
data model and state transitions; endpoint and authorization shapes; a bounded production-file/test
plan; client behavior for web and native; deployment/configuration impacts; and proportionate
automated plus live-browser/device verification. Any new transport, direct R2 client access, public
audience, or non-image file type requires a separately recorded decision.
