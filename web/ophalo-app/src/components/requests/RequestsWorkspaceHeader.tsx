import { AlertTriangle, CheckCircle2 } from "lucide-react";
import type { KeepBusinessSetupResult, KeepRequestViewCounts } from "../../lib/apiClient";
import { RequestsOnboardingBanner } from "../RequestsOnboardingBanner";
import type { TabDef, TabId } from "../../pages/requestsWorkspace";

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

interface RequestsWorkspaceHeaderProps {
  showOnboardingBanner: boolean;
  setup: KeepBusinessSetupResult | undefined;
  onNavigateSettings: (section?: "public-profile" | "policy" | "team") => void;
  onStartCapture: () => void;
  pageTitle: string;
  pageSubtitle: string | null;
  viewCounts: KeepRequestViewCounts | null;
  tabs: TabDef[];
  onSelectTab: (tab: TabDef) => void;
}

export function RequestsWorkspaceHeader({
  showOnboardingBanner,
  setup,
  onNavigateSettings,
  onStartCapture,
  pageTitle,
  pageSubtitle,
  viewCounts,
  tabs,
  onSelectTab,
}: RequestsWorkspaceHeaderProps) {
  const summaryPills = buildSummaryPills(viewCounts, tabs);

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
                onClick={() => tab && onSelectTab(tab)}
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
  );
}
