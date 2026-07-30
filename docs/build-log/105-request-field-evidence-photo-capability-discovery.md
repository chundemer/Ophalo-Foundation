# Build Log 105 — Request Field Evidence: Photo Capability Discovery

**Status:** Launch-critical discovery — no implementation decision or limit is locked
**Date:** 2026-07-29
**Scope:** Production-grade photo evidence for authenticated technician/office work on Keep requests
**Related:** Build 101, Build 103, Build 104, public customer access ADRs

## Why this is a distinct capability

Technicians need to upload pictures during service work. This is evidence and operational history,
not merely a UI attachment control. The feature therefore needs an explicit storage, authorization,
privacy, retention, cost, and failure contract before code is written.

The package name is deliberately **Request Field Evidence**, though the first implementation may
support photos only. That permits a durable domain boundary without authorizing generic files,
videos, customer uploads, or a document-management system.

## Proposed production boundary for the first slice

Unless the customer decision changes it, the first production slice should be:

- authenticated active account members upload approved image formats to a request;
- account and request visibility policy governs every upload, list, thumbnail, and download;
- photos are internal-only by default and are never exposed through a bearer customer-page link;
- each photo records uploader, request, time, and a user-supplied optional caption only if caption
  validation/audit policy is included;
- failures preserve the existing request state and clearly report that the evidence was not saved;
- upload is online-only. It must never claim it will synchronize later unless a durable queue,
  retry, conflict, and user-visible state design is separately implemented.

The capability must not be a pilot-only storage bucket, a client-only access check, a base64 blob in
the relational request table, or an unrestricted file proxy.

## Decisions required before preflight

1. **Workflow:** Is each photo attached to the request generally, a business update, or a completed
   work/evidence event? Are before/after groups required?
2. **Audience:** Which roles can upload, view, delete, and export? Is any customer-facing display
   needed for launch? Default answer: no.
3. **Limits:** Maximum images per request, per-file byte size, allowed formats (including HEIC),
   account storage quota, and behavior when a limit is reached. No placeholder `XXX` becomes a
   production constant without a cost/workflow decision.
4. **Storage and access:** Object-store provider, private object keys, signed-upload/download
   lifecycle, server authorization before issuing access, and no permanent public URL.
5. **Safety:** MIME/content validation, size enforcement before storage, malware scanning decision,
   image transformation/thumbnail policy, and EXIF/GPS stripping or explicit retention policy.
6. **Lifecycle:** Retention, deletion authority, legal/audit behavior after deletion, account
   deletion/export implications, and cost monitoring.
7. **Failure behavior:** Slow/failed upload, duplicate/retry, abandoned upload cleanup, mobile
   backgrounding, low-connectivity messaging, and no false audit record.
8. **Operational model:** storage credentials/configuration, monitoring/redacted error capture,
   provider outage behavior, and support investigation path.

## Architecture rules

```text
entitlement -> account may use Field Evidence
permission  -> active user may upload/view/delete
policy      -> action is allowed on this request/event state
storage     -> private object reference, never data/blob embedded in KeepRequest
```

The API must validate account/request membership before issuing upload/download authority and again
when creating the durable evidence record. Object keys contain no customer names, phone numbers,
addresses, or public page tokens. Derivatives inherit the original's authorization and lifecycle.

## Required proof for an implementation slice

- cross-account and unauthorized access cannot upload, enumerate, thumbnail, or download;
- limits and content validation fail before a durable evidence record is created;
- failed/abandoned storage work leaves no falsely successful evidence record;
- delete/retention behavior matches the locked policy;
- staff mobile/browser failure state is visible and does not claim queued synchronization;
- storage credentials and URLs are absent from client bundles and telemetry;
- entitlement, permission, and record/state policy are independently covered.

## Explicit non-goals for the first slice

- customer/public upload or public photo display;
- video, PDFs, generic documents, unlimited files, or a shared drive;
- offline-first queue/replay, background sync, or conflict resolution;
- AI image analysis, OCR, automatic equipment identification, or warranty inference;
- equipment QR/asset history, unless separately approved under Asset Operations.
