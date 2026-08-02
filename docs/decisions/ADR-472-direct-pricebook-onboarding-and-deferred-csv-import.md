# ADR-472 — Direct Price-Book Onboarding; Defer Generic CSV Import

**Status:** Locked
**Date:** 2026-08-02
**Related:** ADR-471; build-log/107; build-log/110; DEF-087

## Decision

For MVP, a business creates and maintains its price book directly in Keep. Owner/Admin users curate
catalog items, categories, prices, and office-owned offerings/assemblies through purpose-built
application workflows. Generic contractor CSV price-book import is deferred.

Price-book data portability is addressed by a later export of authoritative Keep data. Exports stream
from the API and are not persisted as document objects. They do not imply an import compatibility
commitment.

Before pilot release, remove the unexposed `PriceBookImport`/`PriceBookImportRow` domain,
validation/persistence surface, and schema. Retain only the generic document-storage seam for the
separately scoped pilot image capability. The exact migration operation is deployment-history
dependent: remove an unshared migration with its code; where it has reached a shared environment,
first confirm that no business records exist and then use a forward cleanup migration.

## Rationale

Contractor price sheets vary in layout and meaning, commonly mixing stale entries, incomplete
identifiers, labor assumptions, costs, markups, and prices. Supporting arbitrary uploads would require
a parser, source storage and retention, field mapping, validation/remediation, review UI, and an
ongoing compatibility/support promise before the field and office quoting workflow has been validated.

Direct onboarding has intentional setup friction, but it produces a smaller, reviewed catalog and
recognizable offerings that are appropriate for fast field selection. Ophalo should support that work
with a clear guided setup flow and efficient direct-entry affordances, rather than conceal it behind an
unreliable bulk import.

## Consequences

- Do not implement Session 2c.2b CSV parsing/upload orchestration or Session 2c.3 import review for
  MVP. CsvHelper, `OpenReadAsync` solely for CSV, CSV header aliases, row limits, and CSV-specific
  error/cleanup behavior are out of scope.
- Run a bounded import-cleanup session first. Audit migration deployment history and actual row counts
  before modifying schema: delete only unshared migration history; otherwise create a forward
  migration that drops the unused import tables and constraints. Remove the corresponding application
  code and tests in the same cleanup.
- Re-scope the next price-book publishing preflight around direct price entry and ADR-470's atomic
  version/publish semantics.
- Keep ADR-471's private R2 storage seam and real pilot provisioning. Pilot-required images are the
  active storage use case and require a dedicated image/attachment preflight.
- Development/testing may use clearly isolated sample-data fixtures. Do not seed fictitious catalog
  prices into a pilot business; pilot onboarding uses the real direct-entry workflow.
- A future import must be justified by pilot evidence and begin with a single documented Ophalo CSV
  template, preview, and explicit review. It must not begin as flexible ingestion of arbitrary vendor
  spreadsheets.
