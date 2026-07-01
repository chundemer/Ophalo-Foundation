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
        }
      : r,
  );
}
