import type { AccountRole, KeepRequestSummary, KeepRequestDetailResult } from "../lib/apiClient";
import { mockRequestSummaries, mockRequestDetails as initialDetails } from "./fixtures";

// ---------------------------------------------------------------------------
// Role state
// ---------------------------------------------------------------------------

export let currentMockRole: AccountRole = "owner";

export function setMockRole(role: AccountRole): void {
  currentMockRole = role;
}

// ---------------------------------------------------------------------------
// Request store (rebuilt fresh on each module load — not persisted)
// ---------------------------------------------------------------------------

let requests: KeepRequestSummary[] = [...mockRequestSummaries];
const details = new Map<string, KeepRequestDetailResult>(
  Object.entries(initialDetails),
);

const FOLLOW_UP_LABELS: Record<string, string> = {
  weather: "Weather",
  parts: "Parts",
  customer_delay: "Customer delay",
  business_operator_availability: "Availability",
  third_party: "Third party",
  other: "Follow up",
  waiting_on_customer: "Customer delay",
};

function plannedLabel(date: string | null): string | null {
  if (!date) return null;
  const [year, month, day] = date.split("-").map(Number);
  const d = new Date(year, month - 1, day);
  return `Planned ${d.toLocaleDateString("en-US", { weekday: "short" })}`;
}

function isFutureDateOnly(date: string | null): boolean {
  if (!date) return false;
  const [year, month, day] = date.split("-").map(Number);
  const target = new Date(year, month - 1, day);
  const now = new Date();
  const today = new Date(now.getFullYear(), now.getMonth(), now.getDate());
  return target > today;
}

export function getMockRequests(): KeepRequestSummary[] {
  return requests;
}

export function getMockDetail(id: string): KeepRequestDetailResult | null {
  return details.get(id) ?? null;
}

export function addMockRequest(
  summary: KeepRequestSummary,
  detail: KeepRequestDetailResult,
): void {
  requests = [summary, ...requests];
  details.set(summary.id, detail);
}

export function updateMockDetail(
  id: string,
  detail: KeepRequestDetailResult,
): void {
  details.set(id, detail);
  requests = requests.map((r) =>
    r.id === id
      ? {
          ...r,
          status: detail.status,
          currentStatusText: detail.currentStatusText,
          needsShare: detail.needsShare,
          lastBusinessActivityAtUtc: detail.lastBusinessActivityAt,
          timing: {
            followUpOnDate: detail.followUpOnDate,
            followUpOnReason: detail.followUpOnReason,
            followUpOnNote: detail.followUpOnNote,
            followUpOnLabel: detail.followUpOnReason
              ? FOLLOW_UP_LABELS[detail.followUpOnReason] ?? "Follow up"
              : detail.followUpOnDate ? "Follow up" : null,
            hasFutureFollowUpOn: isFutureDateOnly(detail.followUpOnDate),
            plannedForDate: detail.plannedForDate,
            plannedForLabel: plannedLabel(detail.plannedForDate),
            hasFuturePlannedFor: isFutureDateOnly(detail.plannedForDate),
          },
        }
      : r,
  );
}
