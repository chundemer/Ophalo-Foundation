import { useState, useRef, useEffect } from "react";
import { useQuery } from "@tanstack/react-query";
import { api, type AccountRole, type RequestView, type KeepRequestViewCounts, type KeepRequestSummary, type KeepQuickAction } from "../lib/apiClient";
import { RequestRow } from "../components/RequestRow";
import { RequestRowActionModal } from "../components/RequestRowActionModal";
import { ShareLinkModal } from "../components/ShareLinkModal";
import { RequestsWorkspaceHeader } from "../components/requests/RequestsWorkspaceHeader";
import { RequestQueueNavigation } from "../components/requests/RequestQueueNavigation";
import { RequestListToolbar } from "../components/requests/RequestListToolbar";
import { RequestListContent } from "../components/requests/RequestListContent";
import { ApiError } from "../lib/apiClient";
import {
  getTabsForRole,
  getQueueSubtitle,
  EMPTY_STATE,
  resolveHistoryDateParams,
  HISTORY_SCOPE_LABELS,
  HISTORY_DATE_SCOPES,
  HISTORY_EMPTY_STATE,
  getStatusLabel,
  type TabDef,
  type HistoryScope,
  type HistoryDateScope,
} from "./requestsWorkspace";

// GAP-046: submitted-criteria wording shared by the applied-criteria line, the filtered-empty
// detail, and the live-region heading/range suffix — reports only submitted values, never draftQ.
function operationalAppliedText(q: string, statusFilter: string): string {
  const statusLabel = getStatusLabel(statusFilter);
  if (q) return `Search “${q}” · Status: ${statusLabel}`;
  if (statusFilter) return `Status: ${statusLabel}`;
  return "All active statuses";
}

function historyAppliedText(q: string, scopeLabel: string, dateLabel: string): string {
  if (q) return `Search “${q}” · ${scopeLabel} · ${dateLabel}`;
  return `${scopeLabel} · ${dateLabel}`;
}

function operationalHeadingSuffix(q: string, statusFilter: string): string {
  const statusLabel = getStatusLabel(statusFilter);
  const parts: string[] = [];
  if (q) parts.push(`for “${q}”`);
  if (statusFilter) parts.push(`Status: ${statusLabel}`);
  return parts.length > 0 ? ` ${parts.join(" · ")}` : "";
}

function historyHeadingSuffix(q: string, scopeLabel: string, dateLabel: string): string {
  const parts: string[] = [];
  if (q) parts.push(`for “${q}”`);
  parts.push(scopeLabel, dateLabel);
  return ` ${parts.join(" · ")}`;
}

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

  function updateStatusFilter(value: string) {
    setStatusFilter(value);
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

  // GAP-042: businessName is minimal authenticated workspace-shell context, sourced from the
  // shared ["me"] cache (populated at app-shell mount) rather than a List-specific query — so
  // the title is available on first paint and Owner/Admin/Operator all see it identically.
  const meQuery = useQuery({ queryKey: ["me"], queryFn: api.getMe });
  const canSeeBusinessIdentity = isOwnerOrAdmin || role === "operator";
  const businessName = meQuery.data?.businessName ?? null;
  const pageTitle = canSeeBusinessIdentity && businessName ? `Requests for ${businessName}` : "Requests";

  // GAP-044: presentation (labels/subtitle/empty-state/row-split) is driven by the server's
  // own listContext.isHistory once a response has loaded — historyMode is only the client's
  // loading/navigation intent (which controls to show, which view/date params to request),
  // not the authority on whether the returned rows are actually history.
  const serverListContext = isAvailableTab ? undefined : listQuery.data?.listContext;
  const presentAsHistory = serverListContext ? serverListContext.isHistory : historyMode;
  const contextLabel = presentAsHistory ? HISTORY_SCOPE_LABELS[historyScope] : activeTab.label;

  // GAP-046: submitted criteria only (never draftQ) — the single source of truth for the
  // applied-criteria line, the filtered-empty state, and the live-region heading/range suffix.
  const historyDateLabel = HISTORY_DATE_SCOPES.find((d) => d.id === historyDateScope)?.label ?? "All time";
  const historyScopeLabel = HISTORY_SCOPE_LABELS[historyScope];
  const criteriaActive = presentAsHistory
    ? q !== "" || historyScope !== "all_history" || historyDateScope !== "all_time"
    : q !== "" || statusFilter !== "";
  const showAppliedLine = !isAvailableTab && (criteriaActive || draftQ !== q);
  const appliedLineText = showAppliedLine
    ? `Applied: ${presentAsHistory ? historyAppliedText(q, historyScopeLabel, historyDateLabel) : operationalAppliedText(q, statusFilter)}`
    : null;
  const criteriaHeadingSuffix = criteriaActive
    ? (presentAsHistory ? historyHeadingSuffix(q, historyScopeLabel, historyDateLabel) : operationalHeadingSuffix(q, statusFilter))
    : "";

  const emptyState = criteriaActive
    ? {
        heading: presentAsHistory ? "No matching history" : "No matching requests",
        detail: presentAsHistory
          ? `No history matches ${historyAppliedText(q, historyScopeLabel, historyDateLabel)}.`
          : `No requests match ${operationalAppliedText(q, statusFilter)}.`,
        isFiltered: true,
      }
    : presentAsHistory
      ? HISTORY_EMPTY_STATE[historyScope]
      : EMPTY_STATE[activeTab.id];

  // GAP-046: the criteria-aware empty state's recovery action. History preserves the selected
  // scope/date — those aren't part of what a filtered-empty state is recovering from.
  function clearFilters() {
    setDraftQ("");
    setQ("");
    if (!presentAsHistory) {
      setStatusFilter("");
    }
    setCursor(null);
    cursorStack.current = [];
    searchInputRef.current?.focus();
  }

  const pageSubtitle = presentAsHistory
    ? "Closed and cancelled work — not part of your active queues."
    : isOwnerOrAdmin && activeTab.id === "default"
      ? "Open requests and feedback requiring review, ranked with customer promises needing attention first."
      : getQueueSubtitle(activeTab.id, role);

  // Session 3.5: preserve the last known-good universal counts while an unvisited tab loads
  // (React Query's `data` resets to undefined on a new queryKey) — never overwrite with null.
  const latestCounts = listQuery.data?.viewCounts ?? null;
  useEffect(() => {
    if (latestCounts) {
      onViewCountsUpdate(latestCounts);
    }
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
  const isForbidden = isError && error instanceof ApiError && error.status === 403;

  // GAP-043: a truthful numbered range, never "of N" — this cursor model has no server total.
  // Valid under the existing fixed-limit, short-final-page contract: only the last page can be
  // smaller than `limit`, so cursorStack depth * limit is the current page's true start index.
  const pageLimit = pageInfo?.limit ?? 50;
  const pageStartIndex = cursorStack.current.length * pageLimit;
  const rangeLabel = !isLoading && !isError && requests.length > 0
    ? `Showing ${pageStartIndex + 1}–${pageStartIndex + requests.length}${criteriaHeadingSuffix ? ` results${criteriaHeadingSuffix}` : ""}`
    : "";
  // Always a meaningful label when rendered — never an empty heading sitting in the outline —
  // and the same stable node across loading→loaded so a pending focus call lands correctly.
  const pageHeadingText = isLoading
    ? `Loading ${contextLabel} requests…`
    : isError
      ? ""
      : requests.length > 0
        ? rangeLabel
        : `${emptyState.heading}${criteriaHeadingSuffix}`;

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

        <RequestsWorkspaceHeader
          showOnboardingBanner={showOnboardingBanner}
          setup={setup}
          onNavigateSettings={onNavigateSettings}
          onStartCapture={onStartCapture}
          pageTitle={pageTitle}
          pageSubtitle={pageSubtitle}
          viewCounts={viewCounts}
          tabs={tabs}
          onSelectTab={selectTab}
        />

        <RequestQueueNavigation
          tabs={tabs}
          activeTab={activeTab}
          viewCounts={viewCounts}
          onSelectTab={selectTab}
          historyMode={historyMode}
          historyScope={historyScope}
          historyDateScope={historyDateScope}
          isOwnerOrAdmin={isOwnerOrAdmin}
          onEnterHistory={enterHistory}
          onExitHistory={exitHistory}
          onUpdateHistoryScope={updateHistoryScope}
          onUpdateHistoryDateScope={updateHistoryDateScope}
        />

        <RequestListToolbar
          isAvailableTab={isAvailableTab}
          historyMode={historyMode}
          presentAsHistory={presentAsHistory}
          searchInputRef={searchInputRef}
          draftQ={draftQ}
          onDraftQChange={setDraftQ}
          onSubmitSearch={submitSearch}
          onClearSearch={clearSearch}
          statusFilter={statusFilter}
          onStatusFilterChange={updateStatusFilter}
          showStalenessNotice={showStalenessNotice}
          onManualRefresh={manualRefresh}
          appliedLineText={appliedLineText}
        />

        </div>{/* /max-w-6xl */}
      </div>

      <RequestListContent
        listRegionRef={listRegionRef}
        pageHeadingRef={pageHeadingRef}
        contextLabel={contextLabel}
        heading={{
          headingText: pageHeadingText,
          isLoading,
          isError,
          isForbidden,
          emptyState,
          onClearFilters: clearFilters,
        }}
        rows={{
          itemCount: requests.length,
          isAvailableTab,
          availableRequests: availableQuery.data?.requests ?? [],
          onAvailableSelect: handleRowSelect,
          requests: listQuery.data?.requests ?? [],
          isDefaultTab,
          needsAttentionRows,
          openWorkRows,
          renderRow: renderRequestRow,
        }}
        pager={{
          hasMore: pageInfo?.hasMore ?? false,
          isOnFirstPage,
          onPrevPage: goPrevPage,
          onNextPage: goNextPage,
        }}
      />

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
