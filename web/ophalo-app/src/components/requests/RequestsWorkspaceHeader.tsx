import type { KeepBusinessSetupResult } from "../../lib/apiClient";
import { RequestsOnboardingBanner } from "../RequestsOnboardingBanner";

interface RequestsWorkspaceHeaderProps {
  showOnboardingBanner: boolean;
  setup: KeepBusinessSetupResult | undefined;
  onNavigateSettings: (section?: "public-profile" | "policy" | "team") => void;
  onStartCapture: () => void;
  pageTitle: string;
  pageSubtitle: string | null;
  // UI-001 post-Step-4 density refinement (build-log 134 §1, locked 2026-08-21): the Queue pane
  // replaces the full-page H1/subtitle with a compact label to reclaim vertical space for the
  // request list. The one-pane fallback keeps the full H1/subtitle regardless of width.
  paneMode?: boolean;
  // Backlog item 4 (2026-08-21): pane-mode active-queue identity — the active primary/secondary
  // tab's own label (mirrors Requests.tsx's `contextLabel`, already the source of truth for
  // RequestListContent's live-region heading) and its authoritative count (RequestListToolbar's
  // `countForTab`, same number RequestQueueNavigation's tab row renders — never re-derived).
  // Undefined/unused outside paneMode.
  queueIdentityLabel?: string;
  queueIdentityCount?: number | null;
}

export function RequestsWorkspaceHeader({
  showOnboardingBanner,
  setup,
  onNavigateSettings,
  onStartCapture,
  pageTitle,
  pageSubtitle,
  paneMode = false,
  queueIdentityLabel,
  queueIdentityCount,
}: RequestsWorkspaceHeaderProps) {
  if (paneMode) {
    return (
      <div className="px-3 pt-2 sm:px-4">
        {showOnboardingBanner && setup && (
          <div className="mb-2">
            <RequestsOnboardingBanner
              setup={setup}
              onNavigateSettings={onNavigateSettings}
              onStartCapture={onStartCapture}
            />
          </div>
        )}
        <p className="text-xs font-semibold uppercase tracking-wide text-[var(--ophalo-muted)]">
          Request Queue
          {queueIdentityLabel && <> · {queueIdentityLabel}</>}
          {queueIdentityCount != null && <> · {queueIdentityCount}</>}
        </p>
      </div>
    );
  }

  return (
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
    </div>
  );
}
