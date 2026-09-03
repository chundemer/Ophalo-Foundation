import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { api, type AccountRole } from "../lib/apiClient";
import { CompanySection, draftFromSetup, type ProfileDraft } from "./settings/CompanySection";
import { PolicySection } from "./settings/PolicySection";
import { PublicLinkSection } from "./settings/PublicLinkSection";
import { TeamSection } from "./settings/TeamSection";

type SettingsTab = "public-profile" | "policy" | "team";

function initialTab(section?: "public-profile" | "policy" | "team"): SettingsTab {
  if (section === "policy") return "policy";
  if (section === "team") return "team";
  return "public-profile";
}

const TABS: Array<{ id: SettingsTab; label: string }> = [
  { id: "public-profile", label: "Public Link & Profile" },
  { id: "policy", label: "Response Policy" },
  { id: "team", label: "Team" },
];

export function Settings({
  callerRole,
  scrollToSection,
}: {
  callerRole: AccountRole;
  scrollToSection?: "public-profile" | "policy" | "team";
}) {
  const [activeTab, setActiveTab] = useState<SettingsTab>(() => initialTab(scrollToSection));

  const { data: setup, isLoading: setupLoading, isError: setupError } = useQuery({
    queryKey: ["setup"],
    queryFn: api.getSetup,
    staleTime: 2 * 60 * 1000,
  });

  // Unsaved profile draft, shared between the company form and the public-link
  // preview so the preview never presents unsaved edits as live. Re-synced
  // whenever `setup` changes identity (initial load or a successful save).
  const [profileDraft, setProfileDraft] = useState<ProfileDraft | null>(null);
  const [syncedSetup, setSyncedSetup] = useState<typeof setup>(undefined);
  if (setup && setup !== syncedSetup) {
    setSyncedSetup(setup);
    setProfileDraft(draftFromSetup(setup));
  }

  const needsSetup = activeTab === "public-profile" || activeTab === "policy";

  return (
    <div className="flex-1 min-w-0 flex flex-col">
      <div className="keep-settings-frame pt-6 pb-4 sm:pt-8">
        <h1 className="keep-page-title tracking-tight">Settings</h1>
        <p className="mt-1.5 keep-page-subtitle">
          Manage your public request link, response policy, and team.
        </p>
      </div>

      <div className="keep-settings-frame" role="tablist" aria-label="Settings sections">
        <div className="flex gap-5 border-b border-[var(--ophalo-border)]">
          {TABS.map((tab) => (
            <button
              key={tab.id}
              type="button"
              role="tab"
              aria-selected={activeTab === tab.id}
              onClick={() => setActiveTab(tab.id)}
              className={`relative -mb-px px-0.5 py-3 text-sm font-semibold transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2 ${
                activeTab === tab.id
                  ? "border-b-2 border-[var(--keep-accent)] text-[var(--ophalo-navy)]"
                  : "border-b-2 border-transparent text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)]"
              }`}
            >
              {tab.label}
            </button>
          ))}
        </div>
      </div>

      <div className="keep-settings-frame pt-8 pb-10">
        <div>
          {activeTab === "team" ? (
            <TeamSection callerRole={callerRole} />
          ) : needsSetup && setupLoading ? (
            <div
              className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] p-5 shadow-sm sm:p-6"
              role="status"
              aria-label="Loading settings"
            >
              <div className="h-5 w-40 animate-pulse rounded bg-[var(--ophalo-border)]" />
              <div className="mt-3 h-4 w-72 max-w-full animate-pulse rounded bg-[var(--ophalo-border-subtle)]" />
              <div className="mt-6 space-y-4">
                <div className="h-11 w-full animate-pulse rounded-lg bg-[var(--ophalo-border-subtle)]" />
                <div className="h-11 w-full animate-pulse rounded-lg bg-[var(--ophalo-border-subtle)]" />
                <div className="h-11 w-2/3 animate-pulse rounded-lg bg-[var(--ophalo-border-subtle)]" />
              </div>
            </div>
          ) : needsSetup && (setupError || !setup) ? (
            <div className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] p-5 shadow-sm sm:p-6">
              <p className="text-sm text-[var(--ophalo-ink)]">
                We couldn't load your settings. Refresh the page to try again.
              </p>
            </div>
          ) : setup && profileDraft && activeTab === "public-profile" ? (
            <div className="space-y-6">
              <CompanySection
                draft={profileDraft}
                onDraftChange={(patch) => setProfileDraft({ ...profileDraft, ...patch })}
              />
              <PublicLinkSection
                businessName={profileDraft.businessName}
                logoUrl={profileDraft.logoUrl}
              />
            </div>
          ) : setup && activeTab === "policy" ? (
            <PolicySection setup={setup} />
          ) : null}
        </div>
      </div>
    </div>
  );
}
