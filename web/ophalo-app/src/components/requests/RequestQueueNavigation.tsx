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
  reviewQueueCount?: number | null;
  onSelectTab: (tab: TabDef) => void;
  historyMode: boolean;
  historyScope: HistoryScope;
  historyDateScope: HistoryDateScope;
  isOwnerOrAdmin: boolean;
  onEnterHistory: () => void;
  onExitHistory: () => void;
  onUpdateHistoryScope: (scope: HistoryScope) => void;
  onUpdateHistoryDateScope: (scope: HistoryDateScope) => void;
}

export function RequestQueueNavigation({
  tabs,
  activeTab,
  viewCounts,
  reviewQueueCount,
  onSelectTab,
  historyMode,
  historyScope,
  historyDateScope,
  isOwnerOrAdmin,
  onEnterHistory,
  onExitHistory,
  onUpdateHistoryScope,
  onUpdateHistoryDateScope,
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

  return (
    <div className="border-t border-[var(--ophalo-border)] overflow-x-auto">
      {!historyMode ? (
        <div className="flex items-center justify-between gap-2 px-4 sm:px-6 min-w-max">
          <div role="tablist" aria-label="Request queues" className="flex gap-0">
            {tabs.map((tab, i) => {
              const count = countForTab(tab, viewCounts, reviewQueueCount);
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
              onClick={onEnterHistory}
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
