import type { AccountRole, RequestView, KeepRequestViewCounts } from "../lib/apiClient";

// --- Tab definitions ---

export type TabId =
  | "default"
  | "assigned_to_me"
  | "needs_attention"
  | "watching"
  | "ready_to_close"
  | "feedback_review"
  | "available_work";

export interface TabDef {
  id: TabId;
  label: string;
  view: RequestView | "available";
  roles: AccountRole[];
}

const ALL_TABS: TabDef[] = [
  { id: "default",        label: "All work",         view: "default",          roles: ["owner", "admin"] },
  { id: "assigned_to_me", label: "Assigned to Me",   view: "assigned_to_me",   roles: ["owner", "admin"] },
  { id: "assigned_to_me", label: "My Promises",      view: "assigned_to_me",   roles: ["operator"] },
  { id: "needs_attention",label: "Needs Attention",  view: "needs_attention",  roles: ["owner", "admin", "operator"] },
  { id: "watching",       label: "Watching",         view: "watching",         roles: ["owner", "admin", "operator"] },
  { id: "ready_to_close", label: "Ready to Close",   view: "ready_to_close",   roles: ["owner", "admin"] },
  { id: "feedback_review",label: "Feedback Review",  view: "feedback_review",  roles: ["owner", "admin"] },
  { id: "available_work", label: "Available Work",   view: "available",        roles: ["operator"] },
];

export function getTabsForRole(role: AccountRole): TabDef[] {
  const seen = new Set<string>();
  return ALL_TABS.filter((t) => {
    if (!t.roles.includes(role)) return false;
    const key = t.view;
    if (seen.has(key)) return false;
    seen.add(key);
    return true;
  });
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
};

export const STATUS_OPTIONS = [
  { value: "", label: "All active statuses" },
  { value: "received", label: "Received" },
  { value: "scheduled", label: "Scheduled" },
  { value: "in_progress", label: "Active" },
  { value: "pending_customer", label: "Waiting on Customer" },
  { value: "resolved", label: "Work completed" },
];

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

export function countForTab(tab: TabDef, counts: KeepRequestViewCounts | null): number | null {
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
