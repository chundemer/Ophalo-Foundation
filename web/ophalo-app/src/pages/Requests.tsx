import { useState, useRef, useEffect } from "react";
import { useQuery } from "@tanstack/react-query";
import {
  RefreshCw, Search, ChevronLeft, ChevronRight,
  AlertTriangle, CheckCircle2, X,
} from "lucide-react";
import { api, type AccountRole, type RequestView, type KeepRequestViewCounts, type KeepRequestSummary, type KeepQuickAction } from "../lib/apiClient";
import { RequestRow, AvailableRequestRow } from "../components/RequestRow";
import { RequestRowActionModal } from "../components/RequestRowActionModal";
import { ShareLinkModal } from "../components/ShareLinkModal";
import { RequestsOnboardingBanner } from "../components/RequestsOnboardingBanner";
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
  { id: "default",        label: "All work",         view: "default",          roles: ["owner", "admin"] },
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
    heading: "No customer feedback",
    detail: "Customer feedback will appear here after customers submit it.",
  },
  available_work: {
    heading: "No available work",
    detail: "Unassigned requests that are open to claim will appear here.",
  },
};

// GAP-041: a fixed, queue-agnostic skeleton — never the previous queue's real rows —
// so a first-time queue selection keeps stable list-region geometry instead of
// collapsing to a small "Loading…" blob.
const SKELETON_ROW_COUNT = 5;

function RequestRowSkeleton() {
  const pulse = "animate-pulse motion-reduce:animate-none rounded bg-[var(--ophalo-canvas)]";
  return (
    <div aria-hidden="true" className="space-y-2">
      {Array.from({ length: SKELETON_ROW_COUNT }).map((_, i) => (
        <div
          key={i}
          className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-4 py-3 space-y-2"
        >
          <div className="flex items-center gap-2">
            <div className={`h-4 w-28 ${pulse}`} />
            <div className={`h-4 w-16 ${pulse}`} />
          </div>
          <div className={`h-3 w-2/3 ${pulse}`} />
        </div>
      ))}
    </div>
  );
}

const STATUS_OPTIONS = [
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

type HistoryScope = "closed_history" | "cancelled_history" | "all_history";
type HistoryDateScope = "today" | "yesterday" | "this_week" | "all_time";

const HISTORY_SCOPES: { id: HistoryScope; label: string }[] = [
  { id: "all_history", label: "All" },
  { id: "closed_history", label: "Closed" },
  { id: "cancelled_history", label: "Cancelled" },
];

const HISTORY_DATE_SCOPES: { id: HistoryDateScope; label: string }[] = [
  { id: "today", label: "Today" },
  { id: "yesterday", label: "Yesterday" },
  { id: "this_week", label: "This week" },
  { id: "all_time", label: "All time" },
];

const HISTORY_SCOPE_LABELS: Record<HistoryScope, string> = {
  all_history: "All history",
  closed_history: "Closed history",
  cancelled_history: "Cancelled history",
};

const HISTORY_EMPTY_STATE: Record<HistoryScope, { heading: string; detail: string }> = {
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
function resolveHistoryDateParams(
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
  onSelectRequest: (requestId: string, navContext?: { requestIds: string[] }, focus?: string) => void;
  onNavigateSettings: (section?: "public-profile" | "policy" | "team") => void;
  onStartCapture: () => void;
}

export function Requests({
  role,
  viewCounts,
  onViewCountsUpdate,
  onSelectRequest,
  onNavigateSettings,
  onStartCapture,
}: RequestsProps) {
  const tabs = getTabsForRole(role);
  const [activeTab, setActiveTab] = useState<TabDef>(tabs[0]);
  const [activeModalAction, setActiveModalAction] = useState<{
    row: KeepRequestSummary;
    action: KeepQuickAction;
  } | null>(null);
  const [shareModalTarget, setShareModalTarget] = useState<KeepRequestSummary | null>(null);
  const [q, setQ] = useState("");
  const [draftQ, setDraftQ] = useState("");
  const [statusFilter, setStatusFilter] = useState("");
  const [cursor, setCursor] = useState<string | null>(null);
  const cursorStack = useRef<(string | null)[]>([]);
  const tabRefs = useRef<(HTMLButtonElement | null)[]>([]);
  const searchInputRef = useRef<HTMLInputElement | null>(null);
  const listRegionRef = useRef<HTMLDivElement | null>(null);
  const pageHeadingRef = useRef<HTMLHeadingElement | null>(null);

  const [historyMode, setHistoryMode] = useState(false);
  const [historyScope, setHistoryScope] = useState<HistoryScope>("all_history");
  const [historyDateScope, setHistoryDateScope] = useState<HistoryDateScope>("all_time");

  const isAvailableTab = !historyMode && activeTab.view === "available";
  const isOnFirstPage = cursor === null;

  function selectTab(tab: TabDef) {
    setActiveTab(tab);
    setQ("");
    setDraftQ("");
    setStatusFilter("");
    setCursor(null);
    cursorStack.current = [];
  }

  // GAP-044: entering/leaving history mode resets search/filter/cursor, same as selectTab —
  // it is its own distinct context, not a queue tab.
  function enterHistory() {
    setHistoryMode(true);
    setHistoryScope("all_history");
    setHistoryDateScope("all_time");
    setQ("");
    setDraftQ("");
    setStatusFilter("");
    setCursor(null);
    cursorStack.current = [];
  }

  function exitHistory() {
    setHistoryMode(false);
    setActiveTab(tabs[0]);
    setQ("");
    setDraftQ("");
    setStatusFilter("");
    setCursor(null);
    cursorStack.current = [];
  }

  function updateHistoryScope(scope: HistoryScope) {
    setHistoryScope(scope);
    setCursor(null);
    cursorStack.current = [];
  }

  function updateHistoryDateScope(scope: HistoryDateScope) {
    setHistoryDateScope(scope);
    setCursor(null);
    cursorStack.current = [];
  }

  // GAP-041: roving-tabindex keyboard pattern — Left/Right/Home/End move focus and
  // selection together; Enter/Space activation is native <button> behavior, unchanged.
  function handleTabKeyDown(e: React.KeyboardEvent<HTMLButtonElement>, index: number) {
    let nextIndex: number | null = null;
    switch (e.key) {
      case "ArrowRight":
        nextIndex = (index + 1) % tabs.length;
        break;
      case "ArrowLeft":
        nextIndex = (index - 1 + tabs.length) % tabs.length;
        break;
      case "Home":
        nextIndex = 0;
        break;
      case "End":
        nextIndex = tabs.length - 1;
        break;
      default:
        return;
    }
    e.preventDefault();
    selectTab(tabs[nextIndex]);
    tabRefs.current[nextIndex]?.focus();
  }

  function clearSearch() {
    setDraftQ("");
    setQ("");
    setCursor(null);
    cursorStack.current = [];
  }

  function submitSearch(e: React.FormEvent) {
    e.preventDefault();
    setQ(draftQ);
    setCursor(null);
    cursorStack.current = [];
  }

  // GAP-044: history mode substitutes its own view/date scope for the operational tab's view
  // and status filter; search and pagination stay wired the same way in both modes so neither
  // one can silently drop the user back into an active queue.
  const effectiveView: RequestView = historyMode ? historyScope : (activeTab.view as RequestView);
  const historyDateParams = historyMode ? resolveHistoryDateParams(historyDateScope) : {};

  const listQuery = useQuery({
    queryKey: ["requests", effectiveView, historyMode ? undefined : statusFilter, q, cursor, historyMode ? historyDateScope : null],
    queryFn: () =>
      api.getRequests({
        view: effectiveView,
        status: historyMode ? undefined : (statusFilter || undefined),
        q: q || undefined,
        cursor: cursor ?? undefined,
        ...historyDateParams,
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

  const isOwnerOrAdmin = role === "owner" || role === "admin";
  const guidedSetupQuery = useQuery({
    queryKey: ["guided-setup"],
    queryFn: api.getGuidedSetup,
    enabled: isOwnerOrAdmin,
    staleTime: 60_000,
  });
  const setup = guidedSetupQuery.data;
  const showOnboardingBanner =
    isOwnerOrAdmin &&
    !!setup &&
    !(setup.businessInfoComplete && setup.createIntakePageComplete && setup.addFirstRequestComplete);

  // ADR-449: business-name page heading is an Owner/Admin work-queue contract only.
  const businessSetupQuery = useQuery({
    queryKey: ["setup"],
    queryFn: api.getSetup,
    enabled: isOwnerOrAdmin,
    staleTime: 5 * 60_000,
  });
  const businessName = businessSetupQuery.data?.businessName ?? null;
  const pageTitle = isOwnerOrAdmin && businessName ? `Requests for ${businessName}` : "Requests";

  // GAP-044: presentation (labels/subtitle/empty-state/row-split) is driven by the server's
  // own listContext.isHistory once a response has loaded — historyMode is only the client's
  // loading/navigation intent (which controls to show, which view/date params to request),
  // not the authority on whether the returned rows are actually history.
  const serverListContext = isAvailableTab ? undefined : listQuery.data?.listContext;
  const presentAsHistory = serverListContext ? serverListContext.isHistory : historyMode;
  const contextLabel = presentAsHistory ? HISTORY_SCOPE_LABELS[historyScope] : activeTab.label;
  const emptyState = presentAsHistory ? HISTORY_EMPTY_STATE[historyScope] : EMPTY_STATE[activeTab.id];

  const pageSubtitle = presentAsHistory
    ? "Closed and cancelled work — not part of your active queues."
    : isOwnerOrAdmin && activeTab.id === "default"
      ? "Open requests and feedback requiring review, ranked with customer promises needing attention first."
      : null;

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

  // GAP-043: a truthful numbered range, never "of N" — this cursor model has no server total.
  // Valid under the existing fixed-limit, short-final-page contract: only the last page can be
  // smaller than `limit`, so cursorStack depth * limit is the current page's true start index.
  const pageLimit = pageInfo?.limit ?? 50;
  const pageStartIndex = cursorStack.current.length * pageLimit;
  const rangeLabel = !isLoading && !isError && requests.length > 0
    ? `Showing ${pageStartIndex + 1}–${pageStartIndex + requests.length}`
    : "";
  // Always a meaningful label when rendered — never an empty heading sitting in the outline —
  // and the same stable node across loading→loaded so a pending focus call lands correctly.
  const pageHeadingText = isLoading
    ? `Loading ${contextLabel} requests…`
    : isError
      ? ""
      : requests.length > 0
        ? rangeLabel
        : emptyState.heading;

  // GAP-043: scroll the list region (not the window — it isn't the scroll container)
  // immediately on page change, but only move focus once the new page has actually
  // rendered — focusing immediately would land on the stale range text and then strand
  // focus on an empty/loading heading while the new page fetches.
  const pendingPageFocusRef = useRef(false);

  useEffect(() => {
    if (pendingPageFocusRef.current && !isLoading && !isError) {
      pageHeadingRef.current?.focus();
      pendingPageFocusRef.current = false;
    }
  }, [isLoading, isError, requests]);

  function scrollListToTop() {
    if (typeof listRegionRef.current?.scrollTo === "function") {
      listRegionRef.current.scrollTo({ top: 0, behavior: "auto" });
    }
  }

  function goNextPage() {
    if (!pageInfo?.nextCursor) return;
    cursorStack.current.push(cursor);
    setCursor(pageInfo.nextCursor);
    scrollListToTop();
    pendingPageFocusRef.current = true;
  }

  function goPrevPage() {
    const prev = cursorStack.current.pop();
    setCursor(prev !== undefined ? prev : null);
    scrollListToTop();
    pendingPageFocusRef.current = true;
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
    const focus = !presentAsHistory && activeTab.id === "feedback_review" ? "feedback_review" : undefined;
    onSelectRequest(id, { requestIds: ids }, focus);
  }

  function handleRowSelectFocused(id: string, focus: string) {
    const ids = isAvailableTab
      ? (availableQuery.data?.requests ?? []).map((r) => r.requestId)
      : (listQuery.data?.requests ?? []).map((r) => r.id);
    onSelectRequest(id, { requestIds: ids }, focus);
  }

  function handleActionClick(row: KeepRequestSummary, action: KeepQuickAction) {
    setActiveModalAction({ row, action });
  }

  function handleModalSuccess() {
    setActiveModalAction(null);
    void listQuery.refetch();
  }

  // ADR-449: within "All work", split the already server-ranked page into a
  // quiet Needs attention / Open work pair without resorting rows — rowContext
  // is server-authoritative and RankingOrder already places attention rows first.
  const isDefaultTab = !presentAsHistory && activeTab.id === "default";
  const defaultRows = !isAvailableTab ? (listQuery.data?.requests ?? []) : [];
  const needsAttentionRows = isDefaultTab
    ? defaultRows.filter((row) => row.rowContext === "needs_attention")
    : [];
  const openWorkRows = isDefaultTab
    ? defaultRows.filter((row) => row.rowContext !== "needs_attention")
    : [];

  function renderRequestRow(row: KeepRequestSummary) {
    // ADR-450: composite key mirrors the query key so RequestRow's local expansion
    // state (originalSummary "Read full request") resets on any tab/filter/search/page
    // change, even when the same request appears in two different result sets.
    return (
      <RequestRow
        key={`${row.id}-${effectiveView}-${historyMode ? historyDateScope : statusFilter}-${q}-${cursor}`}
        row={row}
        onSelect={handleRowSelect}
        onSelectFocused={handleRowSelectFocused}
        onActionClick={handleActionClick}
        onShareClick={setShareModalTarget}
        showCloseoutCue={!presentAsHistory && activeTab.id === "ready_to_close"}
      />
    );
  }

  return (
    <div className="flex flex-col h-full bg-[var(--ophalo-canvas)]">

      {/* Page anchor — Level 1 surface: elevated white card */}
      <div className="shrink-0 bg-[var(--ophalo-card)] shadow-sm">
        <div className="max-w-6xl mx-auto w-full">

        {/* H1 anchor + supporting copy + summary pills */}
        <div className="px-4 pt-5 pb-4 sm:px-6 sm:pt-6">
          {showOnboardingBanner && setup && (
            <div className="mb-4">
              <RequestsOnboardingBanner
                setup={setup}
                onNavigateSettings={onNavigateSettings}
                onStartCapture={onStartCapture}
              />
            </div>
          )}
          <h1 className="keep-page-title tracking-tight">
            {pageTitle}
          </h1>
          {pageSubtitle && (
            <p className="mt-1 keep-page-subtitle">
              {pageSubtitle}
            </p>
          )}
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

        {/* Tab bar / History scope bar */}
        <div className="border-t border-[var(--ophalo-border)] overflow-x-auto">
          {!historyMode ? (
            <div className="flex items-center justify-between gap-2 px-4 sm:px-6 min-w-max">
              <div role="tablist" aria-label="Request queues" className="flex gap-0">
                {tabs.map((tab, i) => {
                  const count = countForTab(tab, viewCounts);
                  const isActive = tab.view === activeTab.view;
                  return (
                    <button
                      key={`${tab.id}-${tab.label}`}
                      ref={(el) => { tabRefs.current[i] = el; }}
                      role="tab"
                      aria-selected={isActive}
                      tabIndex={isActive ? 0 : -1}
                      type="button"
                      onClick={() => selectTab(tab)}
                      onKeyDown={(e) => handleTabKeyDown(e, i)}
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
              {/* GAP-044: demoted, non-competing entry point — not styled as a peer tab. */}
              {isOwnerOrAdmin && (
                <button
                  type="button"
                  onClick={enterHistory}
                  className="shrink-0 text-xs font-medium text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] rounded"
                >
                  History
                </button>
              )}
            </div>
          ) : (
            <div className="flex flex-wrap items-center gap-3 px-4 py-3 sm:px-6">
              <button
                type="button"
                onClick={exitHistory}
                className="flex items-center gap-1 text-sm font-medium text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] rounded"
              >
                <ChevronLeft className="h-4 w-4" />
                Back to queues
              </button>
              <div role="group" aria-label="History scope" className="flex items-center gap-1">
                {HISTORY_SCOPES.map((s) => (
                  <button
                    key={s.id}
                    type="button"
                    aria-pressed={historyScope === s.id}
                    onClick={() => updateHistoryScope(s.id)}
                    className={`px-2.5 py-1 text-xs font-semibold rounded-full border transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] ${
                      historyScope === s.id
                        ? "border-[var(--ophalo-navy)] bg-[var(--ophalo-navy)] text-white"
                        : "border-[var(--ophalo-border)] text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)]"
                    }`}
                  >
                    {s.label}
                  </button>
                ))}
              </div>
              <div role="group" aria-label="Date range" className="flex items-center gap-1">
                {HISTORY_DATE_SCOPES.map((s) => (
                  <button
                    key={s.id}
                    type="button"
                    aria-pressed={historyDateScope === s.id}
                    onClick={() => updateHistoryDateScope(s.id)}
                    className={`px-2.5 py-1 text-xs font-medium rounded-full border transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] ${
                      historyDateScope === s.id
                        ? "border-[var(--keep-accent)] bg-[var(--keep-accent-bg)] text-[var(--keep-accent)]"
                        : "border-[var(--ophalo-border)] text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)]"
                    }`}
                  >
                    {s.label}
                  </button>
                ))}
              </div>
            </div>
          )}
        </div>

        {/* Search + status filter — demoted utility row */}
        {!isAvailableTab && (
          <div className="flex flex-wrap items-center gap-2 px-4 py-2 sm:px-6 border-t border-[var(--ophalo-border)]">
            <form onSubmit={submitSearch} className="flex items-center gap-2 flex-1 min-w-[180px]">
              <div className="relative flex-1">
                <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 h-3.5 w-3.5 text-[var(--ophalo-muted)] pointer-events-none" />
                <input
                  ref={searchInputRef}
                  type="text"
                  value={draftQ}
                  onChange={(e) => setDraftQ(e.target.value)}
                  placeholder={presentAsHistory ? "Search closed & cancelled history…" : "Search requests…"}
                  aria-label="Search requests"
                  className={`w-full pl-8 py-1.5 text-sm border border-[var(--ophalo-border)] rounded-lg bg-[var(--ophalo-card)] text-[var(--ophalo-ink)] placeholder:text-[var(--ophalo-muted)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-1 ${draftQ.length > 0 ? "pr-7" : "pr-3"}`}
                />
                {draftQ.length > 0 && (
                  <button
                    type="button"
                    onClick={() => {
                      clearSearch();
                      searchInputRef.current?.focus();
                    }}
                    aria-label="Clear search"
                    className="absolute right-1.5 top-1/2 -translate-y-1/2 p-1 rounded text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)]"
                  >
                    <X className="h-3.5 w-3.5" />
                  </button>
                )}
              </div>
              <button type="submit" className="sr-only">Search</button>
            </form>
            {!historyMode && (
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
            )}
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
        ref={listRegionRef}
        className="flex-1 overflow-y-auto"
        role="region"
        aria-label={`${contextLabel} requests`}
        aria-live="polite"
        aria-busy={isLoading}
      >
        <div className="max-w-6xl mx-auto w-full px-4 py-4 sm:px-6 sm:py-5">
        {/* GAP-043: rendered whenever it has a meaningful label (loading/range/empty-state
            heading, never a blank node in the outline) — the same DOM node persists across
            the loading→loaded transition, so a pending post-page-change focus() lands once
            the new page's real content, not the stale prior range, is in place. */}
        {pageHeadingText && (
          <h2
            ref={pageHeadingRef}
            tabIndex={-1}
            className="mb-2 text-xs font-medium text-[var(--ophalo-muted)] outline-none"
          >
            {pageHeadingText}
          </h2>
        )}

        {isLoading && <RequestRowSkeleton />}

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
              {emptyState.heading}
            </p>
            <p className="text-[var(--ophalo-muted)] text-sm leading-relaxed">
              {emptyState.detail}
            </p>
          </div>
        )}

        {!isLoading && !isError && requests.length > 0 && (
          <div className="space-y-2">
            {isAvailableTab
              ? (availableQuery.data?.requests ?? []).map((row) => (
                  <AvailableRequestRow key={row.requestId} row={row} onSelect={handleRowSelect} />
                ))
              : isDefaultTab
                ? (
                  <>
                    {needsAttentionRows.length > 0 && (
                      <div className="space-y-2">
                        <h2 className="px-1 text-xs font-semibold uppercase tracking-wide text-[var(--ophalo-muted)]">
                          Needs attention
                        </h2>
                        {needsAttentionRows.map(renderRequestRow)}
                      </div>
                    )}
                    {openWorkRows.length > 0 && (
                      <div className={`space-y-2 ${needsAttentionRows.length > 0 ? "mt-4" : ""}`}>
                        <h2 className="px-1 text-xs font-semibold uppercase tracking-wide text-[var(--ophalo-muted)]">
                          Open work
                        </h2>
                        {openWorkRows.map(renderRequestRow)}
                      </div>
                    )}
                  </>
                )
                : (listQuery.data?.requests ?? []).map(renderRequestRow)
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
          {!pageInfo?.hasMore && (
            <span className="text-xs text-[var(--ophalo-muted)]">End of results</span>
          )}
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

      {/* Row action modal */}
      {activeModalAction && (
        <RequestRowActionModal
          row={activeModalAction.row}
          action={activeModalAction.action}
          onClose={() => setActiveModalAction(null)}
          onSuccess={handleModalSuccess}
        />
      )}

      {/* Share Link modal */}
      {shareModalTarget && (
        <ShareLinkModal
          requestId={shareModalTarget.id}
          onClose={() => setShareModalTarget(null)}
          onShared={() => {
            setShareModalTarget(null);
            void listQuery.refetch();
          }}
        />
      )}
    </div>
  );
}
