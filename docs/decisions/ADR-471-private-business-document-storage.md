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

`PriceBookImport.SourceFileObjectKey` is required (non-null) from the first schema migration. A
staged import represents a retained source artifact; it is not a pre-upload draft. Session 2c.1 unit
tests may use syntactically valid opaque test keys, but no production placeholder such as
`pending-upload` and no nullable transitional schema are permitted. The later upload slice stores the
private R2 object first, then creates the staged import with the returned key.

All objects are private. Reads require account-scoped server authorization. Future browser/mobile
uploads may use short-lived, purpose-bound presigned R2 URLs followed by an authorized completion
step; they must not proxy large binary payloads through Vercel Functions.

## V1 use

- Price-book imports are CSV-only when the later upload/parsing slice is authorized. V1 accepts
  UTF-8 CSV with or without a BOM. Unsupported/ambiguous legacy encodings are rejected with clear
  Excel export guidance; the parser must not silently fall back to Windows-1252 or another encoding
  that could corrupt source values. The retained source object remains available for the lifetime of
  the `PriceBookImport` row, including discarded imports, as required by ADR-469.
- Price-book exports are generated from authoritative data and streamed incrementally by the .NET
  API, using paged or streamed database reads and response writing rather than materializing an
  entire CSV in a `StringBuilder` or byte array. V1 does not persist generated export artifacts.
- Equipment photos are a later slice. They will use the same private store with client-side
  resizing/compression and direct, authorized upload; image transformation is not part of import
  staging/validation.

## Scope boundary

Session 2c.1 delivers import staging entities, validation, lifecycle transitions, and
exception-resolution behavior only. It does not implement upload, R2 provisioning, CSV parsing,
presigned URLs, or a file-upload endpoint. Those are a separately preflighted follow-up after the
R2 bucket, credentials, upload limits, and CORS policy have been provisioned/configured. That CORS
policy must enumerate only the required production frontend origins (and a separate local-development
origin when needed), constrained upload methods and required headers; it must not use a wildcard
origin for authenticated direct upload.

## Rationale

R2 provides durable private object storage with an S3-compatible .NET integration and avoids using
ephemeral deployment disk or coupling the API's document operations to Vercel-specific SDK/routes.
The shared seam supports imports and future evidence without duplicating provider plumbing, while
the constrained purpose and storage-owned opaque keys preserve account isolation and prevent callers
from treating object keys as arbitrary paths.
