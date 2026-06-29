import { useState, useRef, useEffect } from "react";
import { useQuery } from "@tanstack/react-query";
import { RefreshCw, Search, ChevronLeft, ChevronRight } from "lucide-react";
import { api, type AccountRole, type RequestView, type KeepRequestViewCounts } from "../lib/apiClient";
import { RequestRow, AvailableRequestRow } from "../components/RequestRow";
import { ApiError } from "../lib/apiClient";

// --- Tab definitions ---

type TabId =
  | "default"
  | "assigned_to_me"
  | "needs_attention"
  | "watching"
  | "ready_to_close"
  | "feedback_review"
  | "available_work";

interface TabDef {
  id: TabId;
  label: string;
  view: RequestView | "available";
  roles: AccountRole[];
}

const ALL_TABS: TabDef[] = [
  { id: "default",        label: "Default Queue",    view: "default",          roles: ["owner", "admin"] },
  { id: "assigned_to_me", label: "Assigned to Me",   view: "assigned_to_me",   roles: ["owner", "admin"] },
  { id: "assigned_to_me", label: "My Promises",      view: "assigned_to_me",   roles: ["operator"] },
  { id: "needs_attention",label: "Needs Attention",  view: "needs_attention",  roles: ["owner", "admin", "operator"] },
  { id: "watching",       label: "Watching",         view: "watching",         roles: ["owner", "admin", "operator"] },
  { id: "ready_to_close", label: "Ready to Close",   view: "ready_to_close",   roles: ["owner", "admin"] },
  { id: "feedback_review",label: "Feedback Review",  view: "feedback_review",  roles: ["owner", "admin"] },
  { id: "available_work", label: "Available Work",   view: "available",        roles: ["operator"] },
];

function getTabsForRole(role: AccountRole): TabDef[] {
  // Deduplicate by view+role so "assigned_to_me" shows only once per role
  const seen = new Set<string>();
  return ALL_TABS.filter((t) => {
    if (!t.roles.includes(role)) return false;
    const key = t.view;
    if (seen.has(key)) return false;
    seen.add(key);
    return true;
  });
}

const EMPTY_STATE: Record<TabId, string> = {
  default: "All customer promises are covered. No active work needs company-wide attention right now.",
  assigned_to_me: "You have no active customer promises assigned to you.",
  needs_attention: "Nothing needs attention right now. Customer-facing promises are inside their current follow-up window.",
  watching: "You are not watching any active customer promises yet.",
  ready_to_close: "Nothing is ready to close. Resolved work will appear here when it is ready for owner/admin closeout.",
  feedback_review: "No feedback needs review. Negative customer feedback will appear here until it is handled.",
  available_work: "Available work is clear. No unassigned customer requests are waiting to be claimed.",
};

const STATUS_OPTIONS = [
  { value: "", label: "All active statuses" },
  { value: "received", label: "Received" },
  { value: "scheduled", label: "Scheduled" },
  { value: "in_progress", label: "In Progress" },
  { value: "pending_customer", label: "Waiting on Customer" },
  { value: "resolved", label: "Resolved" },
];

// --- Sidebar count helper ---

function countForTab(tab: TabDef, counts: KeepRequestViewCounts | null): number | null {
  if (!counts) return null;
  switch (tab.id) {
    case "default":         return counts.default;
    case "assigned_to_me":  return counts.assignedToMe;
    case "needs_attention": return counts.needsAttention;
    case "watching":        return counts.watching;
    case "ready_to_close":  return counts.readyToClose;
    case "feedback_review": return counts.feedbackReview;
    case "available_work":  return null; // available endpoint has no count in viewCounts
    default:                return null;
  }
}

// --- Main component ---

interface RequestsProps {
  role: AccountRole;
  viewCounts: KeepRequestViewCounts | null;
  onViewCountsUpdate: (counts: KeepRequestViewCounts | null) => void;
}

export function Requests({ role, viewCounts, onViewCountsUpdate }: RequestsProps) {
  const tabs = getTabsForRole(role);
  const [activeTab, setActiveTab] = useState<TabDef>(tabs[0]);
  const [q, setQ] = useState("");
  const [draftQ, setDraftQ] = useState("");
  const [statusFilter, setStatusFilter] = useState("");
  const [cursor, setCursor] = useState<string | null>(null);
  const cursorStack = useRef<(string | null)[]>([]);

  const isAvailableTab = activeTab.view === "available";
  const isOnFirstPage = cursor === null;

  // Swap tab — reset search, filters, pagination
  function selectTab(tab: TabDef) {
    setActiveTab(tab);
    setQ("");
    setDraftQ("");
    setStatusFilter("");
    setCursor(null);
    cursorStack.current = [];
  }

  function submitSearch(e: React.FormEvent) {
    e.preventDefault();
    setQ(draftQ);
    setCursor(null);
    cursorStack.current = [];
  }

  // Standard list query
  const listQuery = useQuery({
    queryKey: ["requests", activeTab.view, statusFilter, q, cursor],
    queryFn: () =>
      api.getRequests({
        view: activeTab.view as RequestView,
        status: statusFilter || undefined,
        q: q || undefined,
        cursor: cursor ?? undefined,
      }),
    enabled: !isAvailableTab,
    refetchInterval: isOnFirstPage ? 30_000 : false,
    refetchOnWindowFocus: isOnFirstPage,
  });

  // Available queue query (operator only)
  const availableQuery = useQuery({
    queryKey: ["requests-available", cursor],
    queryFn: () => api.getAvailableRequests({ cursor: cursor ?? undefined }),
    enabled: isAvailableTab,
    refetchInterval: isOnFirstPage ? 30_000 : false,
    refetchOnWindowFocus: isOnFirstPage,
  });

  // Propagate viewCounts upward so the sidebar can display them
  const latestCounts = listQuery.data?.viewCounts ?? null;
  useEffect(() => {
    onViewCountsUpdate(latestCounts);
  }, [latestCounts, onViewCountsUpdate]);

  const requests = isAvailableTab
    ? availableQuery.data?.requests ?? []
    : listQuery.data?.requests ?? [];

  const pageInfo = isAvailableTab
    ? availableQuery.data?.pageInfo
    : listQuery.data?.pageInfo;

  const isLoading = isAvailableTab ? availableQuery.isLoading : listQuery.isLoading;
  const isError = isAvailableTab ? availableQuery.isError : listQuery.isError;
  const error = isAvailableTab ? availableQuery.error : listQuery.error;

  function goNextPage() {
    if (!pageInfo?.nextCursor) return;
    cursorStack.current.push(cursor);
    setCursor(pageInfo.nextCursor);
  }

  function goPrevPage() {
    const prev = cursorStack.current.pop();
    setCursor(prev !== undefined ? prev : null);
  }

  const showStalenessNotice = !isOnFirstPage;

  function manualRefresh() {
    if (isAvailableTab) {
      void availableQuery.refetch();
    } else {
      void listQuery.refetch();
    }
  }

  return (
    <div className="flex flex-col h-full">
      {/* Tab bar */}
      <div className="border-b border-slate-200 bg-white overflow-x-auto">
        <div className="flex gap-0 px-4 min-w-max">
          {tabs.map((tab) => {
            const count = countForTab(tab, viewCounts);
            const isActive = tab.view === activeTab.view;
            return (
              <button
                key={`${tab.id}-${tab.label}`}
                type="button"
                onClick={() => selectTab(tab)}
                className={`flex items-center gap-1.5 px-3 py-3 text-sm font-medium border-b-2 whitespace-nowrap transition-colors ${
                  isActive
                    ? "border-slate-900 text-slate-900"
                    : "border-transparent text-slate-500 hover:text-slate-700 hover:border-slate-300"
                }`}
              >
                {tab.label}
                {count != null && count > 0 && (
                  <span className={`rounded-full px-1.5 py-0.5 text-xs font-medium ${
                    isActive ? "bg-slate-900 text-white" : "bg-slate-100 text-slate-600"
                  }`}>
                    {count}
                  </span>
                )}
              </button>
            );
          })}
        </div>
      </div>

      {/* Search + filter bar */}
      {!isAvailableTab && (
        <div className="flex items-center gap-2 px-4 py-3 bg-white border-b border-slate-100">
          <form onSubmit={submitSearch} className="flex items-center gap-2 flex-1 min-w-0">
            <div className="relative flex-1 max-w-xs">
              <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 h-4 w-4 text-slate-400 pointer-events-none" />
              <input
                type="text"
                value={draftQ}
                onChange={(e) => setDraftQ(e.target.value)}
                placeholder="Search requests…"
                className="w-full pl-8 pr-3 py-1.5 text-sm border border-slate-200 rounded-md focus:outline-none focus:ring-2 focus:ring-slate-400 bg-white"
              />
            </div>
            <button type="submit" className="sr-only">Search</button>
          </form>
          <select
            value={statusFilter}
            onChange={(e) => {
              setStatusFilter(e.target.value);
              setCursor(null);
              cursorStack.current = [];
            }}
            className="text-sm border border-slate-200 rounded-md px-2 py-1.5 bg-white focus:outline-none focus:ring-2 focus:ring-slate-400 text-slate-700"
          >
            {STATUS_OPTIONS.map((o) => (
              <option key={o.value} value={o.value}>{o.label}</option>
            ))}
          </select>
        </div>
      )}

      {/* Staleness notice */}
      {showStalenessNotice && (
        <div className="flex items-center justify-between px-4 py-2 bg-amber-50 border-b border-amber-100 text-xs text-amber-700">
          <span>Auto-refresh paused while viewing older results</span>
          <button
            type="button"
            onClick={manualRefresh}
            className="flex items-center gap-1 font-medium hover:text-amber-900"
          >
            <RefreshCw className="h-3 w-3" />
            Refresh
          </button>
        </div>
      )}

      {/* Content */}
      <div className="flex-1 overflow-y-auto px-4 py-4">
        {isLoading && (
          <div className="flex justify-center py-12">
            <span className="text-slate-400 text-sm">Loading…</span>
          </div>
        )}

        {isError && !(error instanceof ApiError && error.status === 403) && (
          <div className="flex justify-center py-12">
            <span className="text-slate-500 text-sm">Something went wrong. Try refreshing.</span>
          </div>
        )}

        {isError && error instanceof ApiError && error.status === 403 && (
          <div className="flex justify-center py-12">
            <span className="text-slate-500 text-sm">You don't have access to this view.</span>
          </div>
        )}

        {!isLoading && !isError && requests.length === 0 && (
          <div className="flex flex-col items-center justify-center py-16 text-center max-w-sm mx-auto">
            <p className="text-slate-500 text-sm leading-relaxed">
              {EMPTY_STATE[activeTab.id]}
            </p>
          </div>
        )}

        {!isLoading && !isError && requests.length > 0 && (
          <div className="space-y-2">
            {isAvailableTab
              ? (availableQuery.data?.requests ?? []).map((row) => (
                  <AvailableRequestRow key={row.requestId} row={row} />
                ))
              : (listQuery.data?.requests ?? []).map((row) => (
                  <RequestRow key={row.id} row={row} />
                ))
            }
          </div>
        )}
      </div>

      {/* Pagination */}
      {!isLoading && !isError && (pageInfo?.hasMore || !isOnFirstPage) && (
        <div className="flex items-center justify-between px-4 py-3 border-t border-slate-100 bg-white">
          <button
            type="button"
            onClick={goPrevPage}
            disabled={isOnFirstPage}
            className="flex items-center gap-1 text-sm text-slate-600 disabled:opacity-40 hover:text-slate-900 disabled:cursor-not-allowed"
          >
            <ChevronLeft className="h-4 w-4" />
            Previous
          </button>
          <button
            type="button"
            onClick={goNextPage}
            disabled={!pageInfo?.hasMore}
            className="flex items-center gap-1 text-sm text-slate-600 disabled:opacity-40 hover:text-slate-900 disabled:cursor-not-allowed"
          >
            Next
            <ChevronRight className="h-4 w-4" />
          </button>
        </div>
      )}
    </div>
  );
}
