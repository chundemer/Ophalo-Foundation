import type { KeepBadgeVariant } from "../components/keep/KeepBadge";

// Mirrors ophalo-app's src/lib/requestStatus.ts variant mapping (single source of truth
// there) for the subset of statuses the customer tracker page can show.
const STATUS_LABELS: Record<string, string> = {
  received: "Received",
  scheduled: "Scheduled",
  in_progress: "Active",
  pending_customer: "Pending Customer",
  resolved: "Work completed",
  closed: "Closed",
  cancelled: "Cancelled",
};

const STATUS_BADGE_VARIANTS: Record<string, KeepBadgeVariant> = {
  received: "info",
  scheduled: "info",
  in_progress: "teal",
  pending_customer: "default",
  resolved: "success",
  closed: "success",
  cancelled: "default",
};

export function statusLabel(status: string): string {
  return (
    STATUS_LABELS[status] ??
    status.replace(/_/g, " ").replace(/\b\w/g, (c) => c.toUpperCase())
  );
}

export function statusBadgeVariant(status: string): KeepBadgeVariant {
  return STATUS_BADGE_VARIANTS[status] ?? "default";
}
