# Reset local Actual Work — BL136 4c-i-r / ADR-494 D12

Developer-only, local-only tool: `scripts/reset-local-actual-work.sh`. Run it by hand,
immediately before applying the `AddActualWorkPerformer` migration (`4c-i-mig`), to validate
the strict non-null migration on a local database that already holds Actual Work rows. It is
never wired into an EF migration, application startup, or a deployment path.

```bash
RESET_DATABASE_URL='postgres://postgres:pw@localhost:5432/ophalo_local' \
  scripts/reset-local-actual-work.sh
```

It refuses any non-local host, prompts for confirmation, then `TRUNCATE … CASCADE`s
`keep_actual_works` and its dependent rows (lines, financial resolutions, office
dispositions, recorder-transfer audit). Price Book nudge rules/suggestions are configuration
and are left untouched. After it runs: apply the migration, then reseed demo tickets through
the normal capture flow so every seeded line has a deliberately chosen performer.
