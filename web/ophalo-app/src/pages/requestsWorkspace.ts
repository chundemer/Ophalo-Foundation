import type { AccountRole, RequestView, KeepRequestViewCounts } from "../lib/apiClient";

// --- Tab definitions ---

export type TabId =
  | "default"
  | "assigned_to_me"
  | "needs_attention"
  | "watching"
  | "ready_to_close"
  | "feedback_review"
  | "available_work"
  | "actual_work_review";

export interface TabDef {
  id: TabId;
  label: string;
  // Slice 8A, build-log/129: "actual_work_review" is a client-only tab, not a server RequestView —
  // it's backed by the standalone GET /keep/pricebook/actual-work/review-queue endpoint, which
  // returns ActualWorkReviewQueueEntry rows (not full request-list rows).
  view: RequestView | "available" | "actual_work_review";
  roles: AccountRole[];
}

// UI-004 (locked, amended 2026-08-21): "My Work" is the single primary-tab label for
// assigned_to_me on both roles — do not use "Assigned to Me" or "My Promises" here.
const ALL_TABS: TabDef[] = [
  { id: "needs_attention",label: "Needs Attention",  view: "needs_attention",  roles: ["owner", "admin", "operator"] },
  { id: "default",        label: "All Work",         view: "default",          roles: ["owner", "admin"] },
  { id: "assigned_to_me", label: "My Work",          view: "assigned_to_me",   roles: ["owner", "admin", "operator"] },
  { id: "available_work", label: "Available Work",   view: "available",        roles: ["operator"] },
  { id: "watching",       label: "Watching",         view: "watching",         roles: ["owner", "admin", "operator"] },
  { id: "ready_to_close", label: "Ready to Close",   view: "ready_to_close",   roles: ["owner", "admin"] },
  { id: "feedback_review",label: "Feedback Review",  view: "feedback_review",  roles: ["owner", "admin"] },
  { id: "actual_work_review", label: "Actual Work Review", view: "actual_work_review", roles: ["owner", "admin"] },
];

// UI-004 amendment (2026-08-21): primary tabs are exactly 3, in this order.
// Owner/Admin: Needs Attention, All Work, My Work. Operator: My Work, Needs Attention, Available.
const PRIMARY_TAB_IDS: Partial<Record<AccountRole, TabId[]>> = {
  owner: ["needs_attention", "default", "assigned_to_me"],
  admin: ["needs_attention", "default", "assigned_to_me"],
  operator: ["assigned_to_me", "needs_attention", "available_work"],
};

// UI-004 amendment: Watching is the only Views member, for both roles.
const SECONDARY_VIEW_IDS: Partial<Record<AccountRole, TabId[]>> = {
  owner: ["watching"],
  admin: ["watching"],
  operator: ["watching"],
};

// UI-004 amendment: Office Review members — Owner/Admin only.
const OFFICE_REVIEW_MEMBER_IDS: Partial<Record<AccountRole, TabId[]>> = {
  owner: ["ready_to_close", "feedback_review", "actual_work_review"],
  admin: ["ready_to_close", "feedback_review", "actual_work_review"],
};

function tabsById(ids: TabId[] | undefined, role: AccountRole): TabDef[] {
  return (ids ?? [])
    .map((id) => ALL_TABS.find((t) => t.id === id && t.roles.includes(role)))
    .filter((t): t is TabDef => t !== undefined);
}

export function getTabsForRole(role: AccountRole): TabDef[] {
  return tabsById(PRIMARY_TAB_IDS[role], role);
}

export function getSecondaryViewsForRole(role: AccountRole): TabDef[] {
  return tabsById(SECONDARY_VIEW_IDS[role], role);
}

export function getOfficeReviewMembersForRole(role: AccountRole): TabDef[] {
  return tabsById(OFFICE_REVIEW_MEMBER_IDS[role], role);
}

// Session 3.5: a quiet, truthful description for each operational queue so its purpose is
// clear without All work's command-center subtitle. `needs_attention` avoids "your" — Owner/
// Admin/Viewer see it account-wide while Operator sees only their own MyWork scope (same tab,
// different server scope by role, not by view).
export function getQueueSubtitle(tabId: TabId, role: AccountRole): string | null {
  switch (tabId) {
    case "assigned_to_me":
      return role === "operator"
        ? "Your active customer promises — the requests assigned to you."
        : "Requests currently assigned to you.";
    case "needs_attention":
      return "Requests with customer promises needing attention now.";
    case "watching":
      return "Requests you're watching.";
    case "ready_to_close":
      return "Resolved work ready for owner/admin closeout.";
    case "feedback_review":
      return "Closed requests with customer feedback awaiting review.";
    default:
      return null;
  }
}

export const EMPTY_STATE: Record<TabId, { heading: string; detail: string }> = {
  default: {
    heading: "All promises covered",
    detail: "No active work needs company-wide attention right now.",
  },
  assigned_to_me: {
    heading: "Nothing assigned to you",
    detail: "Active requests assigned to you will appear here.",
  },
  needs_attention: {
    heading: "Nothing needs attention",
    detail: "Customer-facing promises are inside their current follow-up window.",
  },
  watching: {
    heading: "Not watching anything",
    detail: "Requests you are watching will appear here.",
  },
  ready_to_close: {
    heading: "Nothing ready to close",
    detail: "Resolved work will appear here when it is ready for owner/admin closeout.",
  },
  feedback_review: {
    heading: "No customer feedback",
    detail: "Customer feedback will appear here after customers submit it.",
  },
  available_work: {
    heading: "No available work",
    detail: "Unassigned requests that are open to claim will appear here.",
  },
  actual_work_review: {
    heading: "Nothing to review",
    detail: "Submitted visits awaiting review will appear here.",
  },
};

export const STATUS_OPTIONS = [
  { value: "", label: "All active statuses" },
  { value: "received", label: "Received" },
  { value: "scheduled", label: "Scheduled" },
  { value: "in_progress", label: "Active" },
  { value: "pending_customer", label: "Waiting on Customer" },
  { value: "resolved", label: "Work completed" },
];

export function getStatusLabel(value: string): string {
  return STATUS_OPTIONS.find((o) => o.value === value)?.label ?? STATUS_OPTIONS[0].label;
}

// --- GAP-044: History mode (Owner/Admin only) ---
// Demoted, non-competing entry point — not a peer tab — into the existing protected
// closed_history/cancelled_history/all_history contract. isHistory is already computed
// server-side (KeepRequestListContext); this drives the UI from client navigation intent,
// the same way activeTab already does for the operational queues.

export type HistoryScope = "closed_history" | "cancelled_history" | "all_history";
export type HistoryDateScope = "today" | "yesterday" | "this_week" | "all_time";

export const HISTORY_SCOPES: { id: HistoryScope; label: string }[] = [
  { id: "all_history", label: "All" },
  { id: "closed_history", label: "Closed" },
  { id: "cancelled_history", label: "Cancelled" },
];

export const HISTORY_DATE_SCOPES: { id: HistoryDateScope; label: string }[] = [
  { id: "today", label: "Today" },
  { id: "yesterday", label: "Yesterday" },
  { id: "this_week", label: "This week" },
  { id: "all_time", label: "All time" },
];

export const HISTORY_SCOPE_LABELS: Record<HistoryScope, string> = {
  all_history: "All history",
  closed_history: "Closed history",
  cancelled_history: "Cancelled history",
};

export const HISTORY_EMPTY_STATE: Record<HistoryScope, { heading: string; detail: string }> = {
  all_history: {
    heading: "No history in this range",
    detail: "Closed and cancelled requests will appear here.",
  },
  closed_history: {
    heading: "No closed requests in this range",
    detail: "Requests closed in this range will appear here.",
  },
  cancelled_history: {
    heading: "No cancelled requests in this range",
    detail: "Requests cancelled in this range will appear here.",
  },
};

// closedShortcut only defines "yesterday"/"this_week" server-side (GetKeepRequestListService.
// ResolveClosedShortcut). "Today" is sent as explicit closedFrom/closedTo using the same
// UTC-midnight, exclusive-upper-bound convention as that server logic — no backend change.
export function resolveHistoryDateParams(
  scope: HistoryDateScope,
): { closedFrom?: string; closedTo?: string; closedShortcut?: string } {
  if (scope === "all_time") return {};
  if (scope === "yesterday") return { closedShortcut: "yesterday" };
  if (scope === "this_week") return { closedShortcut: "this_week" };
  const now = new Date();
  const todayUtc = Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate());
  const tomorrowUtc = todayUtc + 24 * 60 * 60 * 1000;
  return {
    closedFrom: new Date(todayUtc).toISOString(),
    closedTo: new Date(tomorrowUtc).toISOString(),
  };
}

// --- Sidebar count helper ---

// actualWorkReviewCount is the authoritative server COUNT (Slice A-1, GET
// /keep/pricebook/actual-work/review-queue/count) — never the loaded review queue's
// `.length`, and never a guessed 0 before the count query has resolved (null until then).
export function countForTab(
  tab: TabDef,
  counts: KeepRequestViewCounts | null,
  actualWorkReviewCount?: number | null,
): number | null {
  if (tab.id === "actual_work_review") return actualWorkReviewCount ?? null;
  if (!counts) return null;
  switch (tab.id) {
    case "default":         return counts.default;
    case "assigned_to_me":  return counts.assignedToMe;
    case "needs_attention": return counts.needsAttention;
    case "watching":        return counts.watching;
    case "ready_to_close":  return counts.readyToClose;
    case "feedback_review": return counts.feedbackReview;
    case "available_work":  return null;
    default:                return null;
  }
}
