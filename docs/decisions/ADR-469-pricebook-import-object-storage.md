# ADR-469 — Price-Book Import Object Storage

**Status:** Superseded by ADR-471
**Date:** 2026-07-31
**Related:** build-log/108, build-log/110, ADR-458

## Decision

`PriceBookImport.SourceFileObjectKey` is a private, non-public object-storage reference, never a
database blob column. The module owns a narrow `IPriceBookImportFileStorage` abstraction (put once
at upload/stage time, read back only for re-validation/audit) rather than reusing or inventing a
generic cross-module blob service. Retention: an uploaded source file is kept for the lifetime of its
`PriceBookImport` row (including after `Published`/`Discarded`) so a completed or abandoned import
remains reproducible/auditable; it is never deleted by any module action. No public/unauthenticated
URL is ever issued — every read goes through an authorized server-side fetch, matching the existing
account-isolation posture for every other module resource.

> **Superseded:** ADR-471 replaces this ADR's module-specific storage-abstraction decision with a
> shared, application-owned business-document storage seam backed by private Cloudflare R2. The
> opaque-key, no-database-blob, no-public-URL, and import-lifetime-retention constraints remain in
> force.

## Rationale

Build-log/108 assumed an existing Build 105 Field Evidence object-storage capability that this module
could point at, but Build 105 is only a discovery record (`build-log/105`), not implemented code — no
object-storage abstraction of any kind exists in the repository yet. Rather than block the Catalog
and Import slices on Field Evidence's unrelated photo-capture scope, Price Book, Quotes & Materials
gets its own narrowly-scoped storage seam for the one file type it needs (spreadsheet/CSV import
source), following the same "opaque pointer, never bytes in a row, never a public URL" shape already
implied by ADR-459's `EvidenceObjectRef`. Field Evidence may adopt or wrap the same underlying
provider later; that is a separate decision, not required here.
