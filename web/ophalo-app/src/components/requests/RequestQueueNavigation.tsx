import { useRef } from "react";
import { ChevronLeft } from "lucide-react";
import type { KeepRequestViewCounts } from "../../lib/apiClient";
import {
  countForTab,
  HISTORY_SCOPES,
  HISTORY_DATE_SCOPES,
  type TabDef,
  type HistoryScope,
  type HistoryDateScope,
} from "../../pages/requestsWorkspace";

interface RequestQueueNavigationProps {
  tabs: TabDef[];
  activeTab: TabDef;
  viewCounts: KeepRequestViewCounts | null;
  onSelectTab: (tab: TabDef) => void;
  historyMode: boolean;
  historyScope: HistoryScope;
  historyDateScope: HistoryDateScope;
  onExitHistory: () => void;
  onUpdateHistoryScope: (scope: HistoryScope) => void;
  onUpdateHistoryDateScope: (scope: HistoryDateScope) => void;
  // UI-001 post-Step-4 density refinement (build-log 134 §1, locked 2026-08-21): the 320-360
  // CSS-px bounded Queue pane compresses the primary tabs to a two-row grid. Undefined/false
  // keeps today's full-width single-row layout.
  paneMode?: boolean;
}

export function RequestQueueNavigation({
  tabs,
  activeTab,
  viewCounts,
  onSelectTab,
  historyMode,
  historyScope,
  historyDateScope,
  onExitHistory,
  onUpdateHistoryScope,
  onUpdateHistoryDateScope,
  paneMode = false,
}: RequestQueueNavigationProps) {
  const tabRefs = useRef<(HTMLButtonElement | null)[]>([]);

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
    onSelectTab(tabs[nextIndex]);
    tabRefs.current[nextIndex]?.focus();
  }

  // Shared tab-button rendering for the full-width horizontal strip and the pane-mode grid.
  // `fill` stretches each button to share its row evenly (pane mode); the full-width strip keeps
  // its intrinsic, whitespace-nowrap sizing instead. `denseCount` swaps the badge-pill count for
  // an inline "· N" (pane mode only, build-log 134 §1) — full labels are never abbreviated, only
  // the count treatment shrinks to fit three tabs on one row at the 320-360 CSS-px pane width.
  function renderTabButton(tab: TabDef, i: number, fill: boolean, denseCount = false) {
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
        onClick={() => onSelectTab(tab)}
        onKeyDown={(e) => handleTabKeyDown(e, i)}
        className={`flex items-center justify-center gap-1 border-b-2 transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-inset ${
          fill ? `flex-1 min-w-0 min-h-11 py-2.5 ${denseCount ? "px-1 text-xs" : "px-2 text-sm"}` : "px-3 py-4 text-sm whitespace-nowrap"
        } ${
          isActive
            ? "font-semibold border-[var(--ophalo-navy)] text-[var(--ophalo-navy)]"
            : "font-medium border-transparent text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] hover:border-[var(--ophalo-border)]"
        }`}
      >
        {tab.label}
        {count != null && count > 0 && (
          denseCount ? (
            <span className="text-[11px] font-semibold">· {count}</span>
          ) : (
            <span className={`rounded-full px-1.5 py-0.5 text-xs font-semibold ${
              isActive
                ? "bg-[var(--ophalo-navy)] text-white"
                : "bg-[var(--keep-accent-bg)] text-[var(--keep-accent)]"
            }`}>
              {count}
            </span>
          )
        )}
      </button>
    );
  }

  return (
    <div className="border-t border-[var(--ophalo-border)]">
      {!historyMode ? (
        paneMode ? (
          // Row 1: two-row primary tab grid, locked keep-ui-design-model-v2.md §13 — tabs[0]
          // gets its own full-width row, tabs[1]/tabs[2] share the second row. Role ordering
          // already places each role's primary tab first, so this split is role-agnostic.
          // Measured evidence (build-log 134 §2, 2026-08-21): at the true 360px pane width, a
          // single 3-way row with full labels + badge-pill counts wraps "Needs Attention" to
          // two lines (real-browser measurement via mock harness), so the single-row layout
          // was rejected in favor of retaining this two-row grid.
          <div role="tablist" aria-label="Request queues" className="flex flex-col gap-1 px-3 pt-2 pb-2 sm:px-4">
            <div className="flex">{renderTabButton(tabs[0], 0, true)}</div>
            {tabs.length > 1 && (
              <div className="flex gap-1">
                {tabs.slice(1).map((tab, i) => renderTabButton(tab, i + 1, true))}
              </div>
            )}
          </div>
        ) : (
          // Row 1: primary tabs, own scroll region.
          <div className="flex items-center px-4 sm:px-6">
            <div role="tablist" aria-label="Request queues" className="flex gap-0 overflow-x-auto shrink min-w-0">
              {tabs.map((tab, i) => renderTabButton(tab, i, false))}
            </div>
          </div>
        )
      ) : (
        <div className="flex flex-wrap items-center gap-3 px-4 py-3 sm:px-6">
          <button
            type="button"
            onClick={onExitHistory}
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
                onClick={() => onUpdateHistoryScope(s.id)}
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
                onClick={() => onUpdateHistoryDateScope(s.id)}
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
  );
}
