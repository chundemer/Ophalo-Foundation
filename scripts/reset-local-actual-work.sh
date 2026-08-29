#!/usr/bin/env bash
# BL136 4c-i-r / ADR-494 D12 — developer-only local Actual Work reset.
#
# Clears every Actual Work visit row (and its cascaded lines, financial
# resolutions, office dispositions, and recorder-transfer audit rows) from a
# LOCAL database so the strict `AddActualWorkPerformer` migration can be
# validated locally before it is applied. Price Book nudge rules/suggestions are
# configuration, not visit data, and are left untouched.
#
# This tool is inert to the running application. It is NEVER invoked from an EF
# migration, application startup, or a deployment path, and it is not a fallback
# the migration calls. Run it deliberately, by hand, against a local database
# only — see docs/runbook/reset-local-actual-work.md.
#
# Usage:
#   RESET_DATABASE_URL='postgres://postgres:pw@localhost:5432/ophalo_local' \
#     scripts/reset-local-actual-work.sh
#
# The script refuses to run unless the target host is localhost / 127.0.0.1.

set -euo pipefail

url="${RESET_DATABASE_URL:-}"
if [[ -z "$url" ]]; then
  echo "RESET_DATABASE_URL is required (a local postgres connection URL)." >&2
  echo "See docs/runbook/reset-local-actual-work.md." >&2
  exit 2
fi

host="$(printf '%s' "$url" | sed -E 's#^[a-zA-Z+]+://([^/@]*@)?([^:/?]+).*#\2#')"
case "$host" in
  localhost | 127.0.0.1 | ::1) ;;
  *)
    echo "Refusing to run: host '$host' is not local. This tool is local-only (ADR-494 D12)." >&2
    exit 3
    ;;
esac

echo "About to DELETE all Actual Work rows from: $host"
read -r -p "Type 'reset' to continue: " confirm
if [[ "$confirm" != "reset" ]]; then
  echo "Aborted." >&2
  exit 1
fi

psql "$url" -v ON_ERROR_STOP=1 <<'SQL'
BEGIN;
TRUNCATE TABLE
  keep_actual_work_line_financial_resolutions,
  keep_actual_work_office_financial_dispositions,
  keep_actual_work_draft_recorder_transfers,
  keep_actual_work_lines,
  keep_actual_works
  RESTART IDENTITY CASCADE;
COMMIT;
SQL

echo "Actual Work rows cleared. Now apply the migration, then reseed demo tickets"
echo "through the normal capture flow so every line has a deliberately chosen performer."
