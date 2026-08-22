import { useEffect, useRef, useState, useCallback } from "react";
import type { AccountRole, KeepRequestSummary, KeepRequestViewCounts } from "../../lib/apiClient";
import { Requests, type AppliedQueueSnapshot } from "../../pages/Requests";
import { RequestDetail } from "../../pages/RequestDetail";
import { PriorityPreview } from "./PriorityPreview";

// Backlog item 3 (2026-08-21): duplicated rather than imported — PriorityPreview.tsx keeps this
// predicate internal. Same locked rule as PriorityPreview's own `hasAttention`: the
// `attentionLevel !== "none"` check used throughout the codebase (RequestRow.tsx, DetailHero.tsx,
// BusinessSection.tsx).
function hasAttention(row: KeepRequestSummary): boolean {
  return row.attention.attentionLevel !== "none";
}

// Locked in keep-ui-design-model-v2.md §13 (build-log 133): 360px Queue pane + 640px protected
// Workbench minimum + 1px border. Below this, no pane split can honor either minimum.
const PROTECTED_WORKSPACE_MIN_PX = 1001;

type ShellRoute = { page: "requests" } | { page: "detail"; requestId: string; focusPanel?: string };

interface RequestWorkbenchShellProps {
  role: AccountRole;
  route?: ShellRoute;
  viewCounts: KeepRequestViewCounts | null;
  onViewCountsUpdate: (counts: KeepRequestViewCounts | null) => void;
  onSelectRequest: (requestId: string, navContext?: { requestIds: string[] }, focus?: string) => void;
  onNavigateSettings: (section?: "public-profile" | "policy" | "team") => void;
  onStartCapture: () => void;
  // Bumped only for an explicit Requests navigation. It restarts initial selection without
  // remounting Requests, preserving the Queue pane's filters and scroll position.
  requestEntryIntent?: number;
  // Narrow-detail fallback only (structurally unchanged from the pre-Step-5 standalone route) —
  // the frozen navigation context set at selection time, not the Queue pane's live snapshot.
  onBack?: () => void;
  narrowPrevId?: string;
  narrowNextId?: string;
  onNarrowNavigate?: (requestId: string) => void;
}

export function RequestWorkbenchShell(props: RequestWorkbenchShellProps) {
  const {
    role,
    route = { page: "requests" },
    viewCounts,
    onViewCountsUpdate,
    onSelectRequest,
    onNavigateSettings,
    onStartCapture,
    requestEntryIntent = 0,
    onBack,
    narrowPrevId,
    narrowNextId,
    onNarrowNavigate,
  } = props;
  const containerRef = useRef<HTMLDivElement | null>(null);
  const [isWide, setIsWide] = useState(false);
  const [snapshot, setSnapshot] = useState<AppliedQueueSnapshot | null>(null);

  useEffect(() => {
    const el = containerRef.current;
    if (!el) return;

    const observer = new ResizeObserver((entries) => {
      const width = entries[0]?.contentRect.width ?? 0;
      setIsWide(width >= PROTECTED_WORKSPACE_MIN_PX);
    });
    observer.observe(el);
    return () => observer.disconnect();
  }, []);

  const handleAppliedSnapshotChange = useCallback((next: AppliedQueueSnapshot) => {
    setSnapshot(next);
  }, []);

  const detailRoute = route.page === "detail" ? route : null;

  // UI-001 Step 3: Available and Actual Work Review have no ranked-request shape for Priority
  // Preview to branch on (their own item shapes, not KeepRequestSummary rows), and History is a
  // closed/cancelled result set outside UI-003's active-queue branches — all three fall back to
  // the existing one-pane presentation regardless of width until they get their own preflighted
  // wide treatment.
  const showTwoPaneRequests = isWide && route.page === "requests" && !!snapshot?.isRankedView;
  const showPaneDetail = isWide && !!detailRoute;

  // Backlog item 3 (2026-08-21): one-time initial selection, latched for the life of this mount.
  // Any detail route — auto-selected or user-chosen — latches the ref so a browser Back/Forward
  // return to bare #/requests stays on the aggregate Priority Preview instead of re-selecting.
  // Only App's explicit Requests-navigation intent resets it; filters/tabs/live snapshot updates
  // never select a replacement customer.
  const hasAutoSelectedRef = useRef(false);
  const priorRequestEntryIntentRef = useRef(requestEntryIntent);
  useEffect(() => {
    if (priorRequestEntryIntentRef.current === requestEntryIntent) return;
    priorRequestEntryIntentRef.current = requestEntryIntent;
    hasAutoSelectedRef.current = false;
  }, [requestEntryIntent]);
  useEffect(() => {
    if (detailRoute) {
      hasAutoSelectedRef.current = true;
      return;
    }
    if (hasAutoSelectedRef.current) return;
    if (!showTwoPaneRequests) return;
    if (!snapshot || snapshot.isLoading || snapshot.isError || snapshot.isForbidden) return;
    if (snapshot.requests.length === 0) return;

    hasAutoSelectedRef.current = true;
    const firstEligible = snapshot.requests.find(hasAttention) ?? snapshot.requests[0];
    onSelectRequest(firstEligible.id, { requestIds: snapshot.requests.map((r) => r.id) });
  }, [detailRoute, showTwoPaneRequests, snapshot, onSelectRequest, requestEntryIntent]);
  const paneMode = showTwoPaneRequests || showPaneDetail;
  // The Queue pane persists mounted across #/requests <-> #/request/{id} at wide widths (Step 5
  // requirement 1) so its filters/scroll/live snapshot survive; below the protected minimum with
  // a detail route selected, the narrow fallback below owns the full page instead, matching the
  // pre-Step-5 standalone behavior exactly.
  const showQueue = route.page === "requests" || showPaneDetail;

  // Step 5 requirement 2: pane-mode Prev/Next is scoped to this live queue bridge only and never
  // touches the frozen navContext the narrow fallback uses. If the open request has scrolled out
  // of the applied snapshot, both ids come back undefined and RequestDetailHeader hides the
  // Prev/Next control entirely (its existing behavior) until the request reappears in the queue.
  const liveIds = snapshot?.requests.map((r) => r.id) ?? [];
  const liveIdx = detailRoute ? liveIds.indexOf(detailRoute.requestId) : -1;
  const livePrevId = liveIdx > 0 ? liveIds[liveIdx - 1] : undefined;
  const liveNextId = liveIdx >= 0 && liveIdx < liveIds.length - 1 ? liveIds[liveIdx + 1] : undefined;

  return (
    <div ref={containerRef} className="flex h-full min-h-0 flex-1">
      {/* Backlog item 4 (2026-08-21): the pane no longer owns vertical scroll itself — with
          overflow-y-auto here, nested flex children default to min-height:auto and grow past
          the available height, so this wrapper (not RequestListContent's own scroll region)
          ended up scrolling the whole pane, header included. min-h-0 lets it clip to the
          stretched height from the row-flex parent instead; RequestListContent.tsx's own
          flex-1 + min-h-0 + overflow-y-auto region is the sole scroll owner in pane mode. */}
      {showQueue && (
        <div className={paneMode ? "w-[360px] shrink-0 min-h-0 border-r border-[var(--ophalo-border)]" : "flex-1 min-w-0"}>
          <Requests
            role={role}
            viewCounts={viewCounts}
            onViewCountsUpdate={onViewCountsUpdate}
            onSelectRequest={onSelectRequest}
            onNavigateSettings={onNavigateSettings}
            onStartCapture={onStartCapture}
            onAppliedSnapshotChange={handleAppliedSnapshotChange}
            selectedRequestId={detailRoute?.requestId}
            paneMode={paneMode}
          />
        </div>
      )}
      {showTwoPaneRequests && (
        <div className="flex-1 min-w-0 overflow-y-auto">
          <PriorityPreview
            snapshot={snapshot}
            onOpenRequest={(requestId) =>
              onSelectRequest(requestId, { requestIds: snapshot?.requests.map((r) => r.id) ?? [] })
            }
            onStartCapture={onStartCapture}
          />
        </div>
      )}
      {showPaneDetail && detailRoute && (
        <div className="flex-1 min-w-0 overflow-y-auto">
          <RequestDetail
            requestId={detailRoute.requestId}
            focusPanel={detailRoute.focusPanel}
            paneMode
            prevId={livePrevId}
            nextId={liveNextId}
            onNavigate={(id) => onSelectRequest(id)}
            onBack={onBack ?? (() => {})}
          />
        </div>
      )}
      {!isWide && detailRoute && (
        <RequestDetail
          requestId={detailRoute.requestId}
          focusPanel={detailRoute.focusPanel}
          onBack={onBack ?? (() => {})}
          prevId={narrowPrevId}
          nextId={narrowNextId}
          onNavigate={onNarrowNavigate}
        />
      )}
    </div>
  );
}
