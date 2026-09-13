# ADR-503 — Platform content bucket split from business-document storage

**Status:** Locked
**Date:** 2026-09-12
**Related:** ADR-471; ADR-500; ADR-501; GAP-038

## Decision

Cloudflare R2 storage is split into two buckets by data classification, not by environment:

- **`ophalo-business-documents`** — private tenant/customer artifacts only, per ADR-471's
  `IBusinessDocumentStorage` seam. No local-development credentials against this bucket, ever.
  Reads remain account-scoped server authorization only.
- **`ophalo-platform-content`** — founder-maintained editorial content only: the GAP-038/ADR-500
  Help & Updates feed (`platform/updates.json`) and guide screenshots
  (`platform/updates/guides/img/<name>`). This content has no tenant/PII data; it is
  founder-authored editorial copy served through an authenticated app-shell proxy. Read-only (and
  later read/write, for a founder-facing publisher) local-development tokens against this bucket
  are permitted.

`src/OpHalo.Api/Program.cs` previously bound both `IBusinessDocumentStorage` and
`IUpdatesContentSource` to one shared `R2Settings`/`R2` config section (one bucket, one
credential). Each now takes its own named settings type and config section
(`R2:BusinessDocuments`, `R2:PlatformContent`), each independently `IsConfigured`.

## Rationale

Cloudflare's standard R2 API token scope is bucket-level, not object-prefix-level: an "Object Read
Only" token authorized against a bucket can read every object in it. A single shared bucket meant
any credential minted for the editorial Help/Updates content — including a routine local-dev
read-only token — would also read wherever real per-account customer documents land once
`IBusinessDocumentStorage` gets its first consumer (none exists yet). That is a customer-data
blast-radius problem, not a "dev vs. prod" one: a dedicated dev/staging copy of editorial content
would not have fixed it, since the underlying defect is two unrelated trust boundaries sharing one
bucket, not sharing one environment.

Splitting now, while `IBusinessDocumentStorage` has zero consumers and the business-documents
bucket holds no live tenant data, is a config/DI change with a one-time object copy
(`platform/updates.json` + guide images) rather than a data migration. Waiting until the first
customer-document feature ships would turn the same fix into a live-data migration.

## Scope boundary

This ADR does not authorize a founder-facing content publisher UI (GAP-087) or change the
feedback/`POST /feedback` delivery path (ADR-500) in any way. It is confined to storage
configuration/DI wiring for the two existing R2 consumers and the operational runbook steps for
provisioning and rotating platform-content credentials.
