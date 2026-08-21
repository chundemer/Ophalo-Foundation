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
import { ActualWorkReviewQueueList } from "../components/requests/ActualWorkReviewQueueList";
import { ApiError } from "../lib/apiClient";
import type { OfficeReviewState } from "../components/requests/RequestQueueNavigation";
import {
  getTabsForRole,
  getSecondaryViewsForRole,
  getOfficeReviewMembersForRole,
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

// Search remains server-backed, but applies after a brief pause so the visible result set keeps
// pace with what an owner types without issuing a request for every keystroke. Enter/Search still
// applies immediately via submitSearch.
const SEARCH_DEBOUNCE_MS = 350;

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

// UI-001 Step 3: the settled applied-queue state a sibling Priority Preview consumes — never a
// second fetch, never the transient search draft.
export interface AppliedQueueSnapshot {
  requests: KeepRequestSummary[];
  isLoading: boolean;
  isError: boolean;
  isForbidden: boolean;
  isFiltered: boolean;
  isRankedView: boolean;
  onResetFilters: () => void;
  onRetry: () => void;
}

interface RequestsProps {
  role: AccountRole;
  viewCounts: KeepRequestViewCounts | null;
  onViewCountsUpdate: (counts: KeepRequestViewCounts | null) => void;
  onSelectRequest: (requestId: string, navContext?: { requestIds: string[] }, focus?: string) => void;
  onNavigateSettings: (section?: "public-profile" | "policy" | "team") => void;
  onStartCapture: () => void;
  onAppliedSnapshotChange?: (snapshot: AppliedQueueSnapshot) => void;
  // UI-001 Step 4: true when rendered inside the bounded Queue pane — see
  // RequestQueueNavigation's paneMode doc. Undefined/false keeps today's full-width layout.
  paneMode?: boolean;
}

export function Requests({
  role,
  viewCounts,
  onViewCountsUpdate,
  onSelectRequest,
  onNavigateSettings,
  onStartCapture,
  onAppliedSnapshotChange,
  paneMode,
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
  // Slice 8A, build-log/129: "actual_work_review" is a client-only tab, not a RequestView —
  // it bypasses listQuery/availableQuery entirely and sources its own endpoint.
  const isReviewTab = !historyMode && activeTab.view === "actual_work_review";
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

  useEffect(() => {
    if (draftQ === q) return;

    const timer = window.setTimeout(() => {
      setQ(draftQ);
      setCursor(null);
      cursorStack.current = [];
    }, SEARCH_DEBOUNCE_MS);

    return () => window.clearTimeout(timer);
  }, [draftQ, q]);

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
    enabled: !isAvailableTab && !isReviewTab,
    refetchInterval: isOnFirstPage ? 30_000 : false,
    refetchOnWindowFocus: isOnFirstPage,
  });

  const reviewQueueQuery = useQuery({
    queryKey: ["actual-work-review-queue"],
    queryFn: api.getActualWorkReviewQueue,
    enabled: isReviewTab,
  });

  const availableQuery = useQuery({
    queryKey: ["requests-available", cursor],
    queryFn: () => api.getAvailableRequests({ cursor: cursor ?? undefined }),
    enabled: isAvailableTab,
    refetchInterval: isOnFirstPage ? 30_000 : false,
    refetchOnWindowFocus: isOnFirstPage,
  });

  const isOwnerOrAdmin = role === "owner" || role === "admin";

  // UI-004 amendment: the authoritative Actual Work Review count for the Office Review
  // aggregate (Slice A-1) — never the full review-queue list's `.length`.
  const reviewQueueCountQuery = useQuery({
    queryKey: ["actual-work-review-queue-count"],
    queryFn: api.getActualWorkReviewQueueCount,
    enabled: isOwnerOrAdmin,
  });
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

  const secondaryViews = getSecondaryViewsForRole(role);
  const officeReviewMembers = getOfficeReviewMembersForRole(role);

  // UI-004 amendment: Office Review is withheld until both authoritative inputs (viewCounts,
  // the Actual Work Review count) are known. A failed input is an "error" state, not a
  // perpetual "loading" one — it never falls back to a guessed zero or partial aggregate.
  // Operator never renders Office Review (RequestQueueNavigation gates the whole row on
  // isOwnerOrAdmin) — "loading" here is inert, not a real state.
  const officeReview: OfficeReviewState = !isOwnerOrAdmin
    ? { status: "loading" }
    : listQuery.isError || reviewQueueCountQuery.isError
      ? {
          status: "error",
          retry: () => {
            if (listQuery.isError) void listQuery.refetch();
            if (reviewQueueCountQuery.isError) void reviewQueueCountQuery.refetch();
          },
        }
      : viewCounts === null || reviewQueueCountQuery.data === undefined
        ? { status: "loading" }
        : {
            status: "ready",
            aggregate: viewCounts.readyToClose + viewCounts.feedbackReview + reviewQueueCountQuery.data.count,
            members: {
              readyToClose: viewCounts.readyToClose,
              feedbackReview: viewCounts.feedbackReview,
              actualWorkReview: reviewQueueCountQuery.data.count,
            },
          };

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

  // UI-001 Step 3: reports the settled applied-queue state (never draftQ) so a sibling Priority
  // Preview can stay truthful to the same view/filter/search context without a second fetch.
  // Available and Actual Work Review are their own item shapes (KeepRequestAvailableItem / review
  // entries), not ranked KeepRequestSummary rows — Priority Preview cannot honestly branch on
  // them, so isRankedView tells the shell to keep the one-pane fallback for those tabs. History
  // is a closed/cancelled result set, not the active queue UI-003's branches describe (its
  // empty state is "no matching history", never "no active requests" + New Request) — also
  // excluded.
  const isRankedView = !isAvailableTab && !isReviewTab && !presentAsHistory;
  useEffect(() => {
    onAppliedSnapshotChange?.({
      requests: isRankedView ? listQuery.data?.requests ?? [] : [],
      isLoading,
      isError,
      isForbidden,
      isFiltered: criteriaActive,
      isRankedView,
      onResetFilters: clearFilters,
      onRetry: () => void (isAvailableTab ? availableQuery.refetch() : listQuery.refetch()),
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isRankedView, presentAsHistory, listQuery.data, isLoading, isError, isForbidden, criteriaActive, onAppliedSnapshotChange]);

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
        paneMode={paneMode}
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
          paneMode={paneMode}
        />

        <RequestQueueNavigation
          tabs={tabs}
          activeTab={activeTab}
          viewCounts={viewCounts}
          secondaryViews={secondaryViews}
          officeReviewMembers={officeReviewMembers}
          officeReview={officeReview}
          onSelectTab={selectTab}
          historyMode={historyMode}
          historyScope={historyScope}
          historyDateScope={historyDateScope}
          isOwnerOrAdmin={isOwnerOrAdmin}
          onEnterHistory={enterHistory}
          onExitHistory={exitHistory}
          onUpdateHistoryScope={updateHistoryScope}
          onUpdateHistoryDateScope={updateHistoryDateScope}
          paneMode={paneMode}
        />

        <RequestListToolbar
          isAvailableTab={isAvailableTab || isReviewTab}
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
          paneMode={paneMode}
        />

        </div>{/* /max-w-6xl */}
      </div>

      {isReviewTab ? (
        <ActualWorkReviewQueueList
          entries={reviewQueueQuery.data ?? []}
          isLoading={reviewQueueQuery.isLoading}
          isError={reviewQueueQuery.isError}
          onRetry={() => void reviewQueueQuery.refetch()}
          onSelectRequest={(requestId) => onSelectRequest(requestId)}
        />
      ) : (
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
