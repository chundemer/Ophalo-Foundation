# ADR-471 — Private Business Document Storage

**Status:** Locked
**Date:** 2026-08-01
**Related:** ADR-469; build-log/108; build-log/110

## Decision

OpHalo's production business-document store is a **private Cloudflare R2 bucket**, accessed by the
.NET API through the S3-compatible AWS SDK. The application owns a shared
`IBusinessDocumentStorage` seam; provider details, credentials, endpoint configuration, and key
generation remain in Infrastructure. Vercel remains the web-hosting platform, not the API's document
storage backend. Local filesystem storage is permitted only for explicitly isolated development/test
fakes and is not a pilot or production backend.

The storage seam accepts an authorized account identifier and constrained `DocumentPurpose`; it
generates an immutable opaque object key and returns storage metadata. Callers must not construct
storage paths or supply a key. The original filename is metadata, never a storage path. Database rows
store only the opaque key and necessary metadata — never binary file data and never a public URL.

The existing, unexposed `PriceBookImport.SourceFileObjectKey` remains required/non-null in its
already-delivered staging schema. However, ADR-472 defers price-book CSV import from the MVP, so it
does not authorize an upload/parser workflow or create a CSV retention obligation for pilot.

All objects are private. Reads require account-scoped server authorization. Future browser/mobile
uploads may use short-lived, purpose-bound presigned R2 URLs followed by an authorized completion
step; they must not proxy large binary payloads through Vercel Functions.

## Pilot use

- Pilot-required images are the first active use. A separately scoped image/attachment slice must
  define allowed purposes, metadata, content-type and size validation, authorization, retrieval,
  and retention before it writes objects. Objects remain private and are never exposed through a
  public object URL.
- Price-book exports are generated from authoritative data and streamed incrementally by the .NET
  API, using paged or streamed database reads and response writing rather than materializing an
  entire CSV in a `StringBuilder` or byte array. V1 does not persist generated export artifacts.

## Scope boundary

This ADR does not authorize a document upload endpoint by itself. The next image-storage preflight
must define transport, limits, CORS (if browser-to-R2 access is ever used), and image safety rules.
Any CORS policy must enumerate only required production and local-development origins, methods, and
headers; it must not use a wildcard origin for authenticated direct upload.

## Rationale

R2 provides durable private object storage with an S3-compatible .NET integration and avoids using
ephemeral deployment disk or coupling the API's document operations to Vercel-specific SDK/routes.
The shared seam supports imports and future evidence without duplicating provider plumbing, while
the constrained purpose and storage-owned opaque keys preserve account isolation and prevent callers
from treating object keys as arbitrary paths.
