import { useEffect, useRef, useState, useCallback } from "react";
import type { AccountRole, KeepRequestViewCounts } from "../../lib/apiClient";
import { Requests, type AppliedQueueSnapshot } from "../../pages/Requests";
import { PriorityPreview } from "./PriorityPreview";

// Locked in keep-ui-design-model-v2.md §13 (build-log 133): 360px Queue pane + 640px protected
// Workbench minimum + 1px border. Below this, no pane split can honor either minimum.
const PROTECTED_WORKSPACE_MIN_PX = 1001;

interface RequestWorkbenchShellProps {
  role: AccountRole;
  viewCounts: KeepRequestViewCounts | null;
  onViewCountsUpdate: (counts: KeepRequestViewCounts | null) => void;
  onSelectRequest: (requestId: string, navContext?: { requestIds: string[] }, focus?: string) => void;
  onNavigateSettings: (section?: "public-profile" | "policy" | "team") => void;
  onStartCapture: () => void;
}

export function RequestWorkbenchShell(props: RequestWorkbenchShellProps) {
  const { role, viewCounts, onViewCountsUpdate, onSelectRequest, onNavigateSettings, onStartCapture } = props;
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

  // UI-001 Step 3: Available and Actual Work Review have no ranked-request shape for Priority
  // Preview to branch on (their own item shapes, not KeepRequestSummary rows), and History is a
  // closed/cancelled result set outside UI-003's active-queue branches — all three fall back to
  // the existing one-pane presentation regardless of width until they get their own preflighted
  // wide treatment.
  const showTwoPane = isWide && !!snapshot?.isRankedView;

  return (
    <div ref={containerRef} className="flex h-full min-h-0 flex-1">
      <div className={showTwoPane ? "w-[360px] shrink-0 overflow-y-auto border-r border-[var(--ophalo-border)]" : "flex-1 min-w-0"}>
        <Requests
          role={role}
          viewCounts={viewCounts}
          onViewCountsUpdate={onViewCountsUpdate}
          onSelectRequest={onSelectRequest}
          onNavigateSettings={onNavigateSettings}
          onStartCapture={onStartCapture}
          onAppliedSnapshotChange={handleAppliedSnapshotChange}
          paneMode={showTwoPane}
        />
      </div>
      {showTwoPane && (
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
    </div>
  );
}
