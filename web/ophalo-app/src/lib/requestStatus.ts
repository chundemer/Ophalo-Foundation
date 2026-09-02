import type { KeepBadgeVariant } from "../components/keep/KeepBadge";

// Single source of truth for request-status display across Request List, Request Detail,
// and Quick Capture (GAP-029). `in_progress` -> "Active" and `resolved` -> "Work completed"
// are locked staff-facing labels (ADR-425, ADR-434); the rest are this session's terminology
// decision. `closed` shares the `resolved` success variant per ADR-050 (Closed = resolved
// terminal, Cancelled = stopped without resolution).
const STATUS_LABELS: Record<string, string> = {
  received: "Received",
  scheduled: "Scheduled",
  in_progress: "Active",
  pending_customer: "Pending Customer",
  resolved: "Work completed",
  closed: "Closed",
  cancelled: "Cancelled",
  spam: "Spam",
  test: "Test",
};

const STATUS_BADGE_VARIANTS: Record<string, KeepBadgeVariant> = {
  received: "info",
  scheduled: "info",
  in_progress: "teal",
  pending_customer: "default",
  resolved: "success",
  closed: "success",
  cancelled: "default",
  spam: "default",
  test: "default",
};

/**
 * Status is authoritative server data, but UI rendering must remain fail-safe if a mixed-version
 * deployment or malformed response omits it. Normal contracts still require the value; this
 * prevents a missing display value from taking down the whole screen.
 */
export function statusLabel(status: string | null | undefined): string {
  if (!status) return "Status unavailable";
  return (
    STATUS_LABELS[status] ??
    status.replace(/_/g, " ").replace(/\b\w/g, (c) => c.toUpperCase())
  );
}

export function statusBadgeVariant(status: string): KeepBadgeVariant {
  return STATUS_BADGE_VARIANTS[status] ?? "default";
}
