import { useState, useRef, useEffect } from "react";
import { useQuery } from "@tanstack/react-query";
import {
  RefreshCw, Search, ChevronLeft, ChevronRight,
  AlertTriangle, CheckCircle2,
} from "lucide-react";
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
  const seen = new Set<string>();
  return ALL_TABS.filter((t) => {
    if (!t.roles.includes(role)) return false;
    const key = t.view;
    if (seen.has(key)) return false;
    seen.add(key);
    return true;
  });
}

const EMPTY_STATE: Record<TabId, { heading: string; detail: string }> = {
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
    heading: "No feedback to review",
    detail: "Negative customer feedback will appear here until it is handled.",
  },
  available_work: {
    heading: "No available work",
    detail: "Unassigned requests that are open to claim will appear here.",
  },
};

const STATUS_OPTIONS = [
  { value: "", label: "All active statuses" },
  { value: "received", label: "Received" },
  { value: "scheduled", label: "Scheduled" },
  { value: "in_progress", label: "Active" },
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
    case "available_work":  return null;
    default:                return null;
  }
}

// --- Summary pills ---

interface SummaryPill {
  label: string;
  count: number;
  tabId: TabId;
  icon: React.ReactNode;
  variant: "attention" | "success";
}

function buildSummaryPills(
  viewCounts: KeepRequestViewCounts | null,
  tabs: TabDef[],
): SummaryPill[] {
  if (!viewCounts) return [];
  const pills: SummaryPill[] = [];

  if (viewCounts.needsAttention > 0 && tabs.some((t) => t.id === "needs_attention")) {
    pills.push({
      label: "Needs attention",
      count: viewCounts.needsAttention,
      tabId: "needs_attention",
      icon: <AlertTriangle className="h-3 w-3" />,
      variant: "attention",
    });
  }
  if (viewCounts.readyToClose > 0 && tabs.some((t) => t.id === "ready_to_close")) {
    pills.push({
      label: "Ready to close",
      count: viewCounts.readyToClose,
      tabId: "ready_to_close",
      icon: <CheckCircle2 className="h-3 w-3" />,
      variant: "success",
    });
  }
  return pills;
}

// --- Main component ---

interface RequestsProps {
  role: AccountRole;
  viewCounts: KeepRequestViewCounts | null;
  onViewCountsUpdate: (counts: KeepRequestViewCounts | null) => void;
  onSelectRequest: (requestId: string, navContext?: { requestIds: string[] }) => void;
}

export function Requests({ role, viewCounts, onViewCountsUpdate, onSelectRequest }: RequestsProps) {
  const tabs = getTabsForRole(role);
  const [activeTab, setActiveTab] = useState<TabDef>(tabs[0]);
  const [q, setQ] = useState("");
  const [draftQ, setDraftQ] = useState("");
  const [statusFilter, setStatusFilter] = useState("");
  const [cursor, setCursor] = useState<string | null>(null);
  const cursorStack = useRef<(string | null)[]>([]);

  const isAvailableTab = activeTab.view === "available";
  const isOnFirstPage = cursor === null;

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

  const availableQuery = useQuery({
    queryKey: ["requests-available", cursor],
    queryFn: () => api.getAvailableRequests({ cursor: cursor ?? undefined }),
    enabled: isAvailableTab,
    refetchInterval: isOnFirstPage ? 30_000 : false,
    refetchOnWindowFocus: isOnFirstPage,
  });

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

  const summaryPills = buildSummaryPills(viewCounts, tabs);

  function handleRowSelect(id: string) {
    const ids = isAvailableTab
      ? (availableQuery.data?.requests ?? []).map((r) => r.requestId)
      : (listQuery.data?.requests ?? []).map((r) => r.id);
    onSelectRequest(id, { requestIds: ids });
  }

  return (
    <div className="flex flex-col h-full bg-[var(--ophalo-canvas)]">

      {/* Page anchor — Level 1 surface: elevated white card */}
      <div className="shrink-0 bg-[var(--ophalo-card)] shadow-sm">
        <div className="max-w-6xl mx-auto w-full">

        {/* H1 anchor + supporting copy + summary pills */}
        <div className="px-4 pt-5 pb-4 sm:px-6 sm:pt-6">
          <h1 className="keep-page-title tracking-tight">
            Requests
          </h1>
          <p className="mt-1 keep-page-subtitle">
            Active requests that may need ownership, follow-up, or closeout.
          </p>
          {summaryPills.length > 0 && (
            <div className="mt-3 flex flex-wrap gap-2">
              {summaryPills.map((pill) => {
                const tab = tabs.find((t) => t.id === pill.tabId);
                const colorCls = pill.variant === "attention"
                  ? "border-[var(--ophalo-attention-bg)] bg-[var(--ophalo-attention-bg)] text-[var(--ophalo-attention)] hover:border-[var(--ophalo-attention)]"
                  : "border-[var(--ophalo-success-bg)] bg-[var(--ophalo-success-bg)] text-[var(--ophalo-success)] hover:border-[var(--ophalo-success)]";
                return (
                  <button
                    key={pill.label}
                    type="button"
                    onClick={() => tab && selectTab(tab)}
                    className={`inline-flex items-center gap-1.5 rounded-full border px-3 py-1 text-xs font-semibold transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2 ${colorCls}`}
                  >
                    {pill.icon}
                    <span>{pill.count}</span>
                    <span>{pill.label}</span>
                  </button>
                );
              })}
            </div>
          )}
        </div>

        {/* Tab bar */}
        <div className="border-t border-[var(--ophalo-border)] overflow-x-auto">
          <div role="tablist" aria-label="Request queues" className="flex gap-0 px-4 sm:px-6 min-w-max">
            {tabs.map((tab) => {
              const count = countForTab(tab, viewCounts);
              const isActive = tab.view === activeTab.view;
              return (
                <button
                  key={`${tab.id}-${tab.label}`}
                  role="tab"
                  aria-selected={isActive}
                  type="button"
                  onClick={() => selectTab(tab)}
                  className={`flex items-center gap-1.5 px-3 py-4 text-sm border-b-2 whitespace-nowrap transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-inset ${
                    isActive
                      ? "font-semibold border-[var(--ophalo-navy)] text-[var(--ophalo-navy)]"
                      : "font-medium border-transparent text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] hover:border-[var(--ophalo-border)]"
                  }`}
                >
                  {tab.label}
                  {count != null && count > 0 && (
                    <span className={`rounded-full px-1.5 py-0.5 text-xs font-semibold ${
                      isActive
                        ? "bg-[var(--ophalo-navy)] text-white"
                        : "bg-[var(--keep-accent-bg)] text-[var(--keep-accent)]"
                    }`}>
                      {count}
                    </span>
                  )}
                </button>
              );
            })}
          </div>
        </div>

        {/* Search + status filter — demoted utility row */}
        {!isAvailableTab && (
          <div className="flex flex-wrap items-center gap-2 px-4 py-2 sm:px-6 border-t border-[var(--ophalo-border)]">
            <form onSubmit={submitSearch} className="flex items-center gap-2 flex-1 min-w-[180px]">
              <div className="relative flex-1">
                <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 h-3.5 w-3.5 text-[var(--ophalo-muted)] pointer-events-none" />
                <input
                  type="text"
                  value={draftQ}
                  onChange={(e) => setDraftQ(e.target.value)}
                  placeholder="Search requests…"
                  aria-label="Search requests"
                  className="w-full pl-8 pr-3 py-1.5 text-sm border border-[var(--ophalo-border)] rounded-lg bg-[var(--ophalo-card)] text-[var(--ophalo-ink)] placeholder:text-[var(--ophalo-muted)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-1"
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
              aria-label="Filter by status"
              className="shrink-0 text-sm border border-[var(--ophalo-border)] rounded-lg px-2 py-1.5 bg-[var(--ophalo-card)] text-[var(--ophalo-ink)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-1"
            >
              {STATUS_OPTIONS.map((o) => (
                <option key={o.value} value={o.value}>{o.label}</option>
              ))}
            </select>
          </div>
        )}

        {/* Staleness notice */}
        {showStalenessNotice && (
          <div className="flex items-center justify-between px-4 py-2 sm:px-6 bg-[var(--ophalo-attention-bg)] border-t border-[var(--ophalo-border)] text-xs text-[var(--ophalo-attention)]">
            <span>Auto-refresh paused while viewing older results</span>
            <button
              type="button"
              onClick={manualRefresh}
              className="flex items-center gap-1 font-semibold hover:opacity-80 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-1 rounded"
            >
              <RefreshCw className="h-3 w-3" />
              Refresh
            </button>
          </div>
        )}

        </div>{/* /max-w-6xl */}
      </div>

      {/* Content — scrollable, canvas background shows between cards */}
      <div
        className="flex-1 overflow-y-auto"
        role="region"
        aria-label={`${activeTab.label} requests`}
        aria-live="polite"
        aria-busy={isLoading}
      >
        <div className="max-w-6xl mx-auto w-full px-4 py-4 sm:px-6 sm:py-5">
        {isLoading && (
          <div className="flex justify-center py-12">
            <span className="text-[var(--ophalo-muted)] text-sm">Loading…</span>
          </div>
        )}

        {isError && !(error instanceof ApiError && error.status === 403) && (
          <div className="flex flex-col items-center py-12 text-center gap-2">
            <p className="text-[var(--ophalo-ink)] text-sm font-medium">Something went wrong</p>
            <p className="text-[var(--ophalo-muted)] text-sm">Try refreshing the page.</p>
          </div>
        )}

        {isError && error instanceof ApiError && error.status === 403 && (
          <div className="flex justify-center py-12">
            <p className="text-[var(--ophalo-muted)] text-sm">You don't have access to this view.</p>
          </div>
        )}

        {!isLoading && !isError && requests.length === 0 && (
          <div className="flex flex-col items-center justify-center py-16 text-center max-w-sm mx-auto gap-2">
            <p className="text-[var(--ophalo-ink)] text-sm font-semibold">
              {EMPTY_STATE[activeTab.id].heading}
            </p>
            <p className="text-[var(--ophalo-muted)] text-sm leading-relaxed">
              {EMPTY_STATE[activeTab.id].detail}
            </p>
          </div>
        )}

        {!isLoading && !isError && requests.length > 0 && (
          <div className="space-y-2">
            {isAvailableTab
              ? (availableQuery.data?.requests ?? []).map((row) => (
                  <AvailableRequestRow key={row.requestId} row={row} onSelect={handleRowSelect} />
                ))
              : (listQuery.data?.requests ?? []).map((row) => (
                  <RequestRow key={row.id} row={row} onSelect={handleRowSelect} />
                ))
            }
          </div>
        )}
        </div>{/* /max-w-6xl */}
      </div>

      {/* Pagination */}
      {!isLoading && !isError && (pageInfo?.hasMore || !isOnFirstPage) && (
        <div className="shrink-0 border-t border-[var(--ophalo-border)] bg-[var(--ophalo-card)]">
        <div className="max-w-6xl mx-auto w-full flex items-center justify-between px-4 py-3 sm:px-6">
          <button
            type="button"
            onClick={goPrevPage}
            disabled={isOnFirstPage}
            className="flex items-center gap-1 text-sm text-[var(--ophalo-muted)] disabled:opacity-40 hover:text-[var(--ophalo-ink)] disabled:cursor-not-allowed focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-1 rounded"
          >
            <ChevronLeft className="h-4 w-4" />
            Previous
          </button>
          <button
            type="button"
            onClick={goNextPage}
            disabled={!pageInfo?.hasMore}
            className="flex items-center gap-1 text-sm text-[var(--ophalo-muted)] disabled:opacity-40 hover:text-[var(--ophalo-ink)] disabled:cursor-not-allowed focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-1 rounded"
          >
            Next
            <ChevronRight className="h-4 w-4" />
          </button>
        </div>{/* /max-w-6xl */}
        </div>
      )}
    </div>
  );
}
