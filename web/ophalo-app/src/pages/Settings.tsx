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
      <div className="mx-auto w-full max-w-[1440px] px-4 pt-6 pb-4 sm:px-6 sm:pt-8">
        <h1 className="keep-page-title tracking-tight">Settings</h1>
        <p className="mt-1.5 keep-page-subtitle">
          Manage your public request link, response policy, and team.
        </p>
      </div>

      <div className="mx-auto w-full max-w-[1440px] px-4 sm:px-6" role="tablist" aria-label="Settings sections">
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

      <div className="mx-auto w-full max-w-[1440px] px-4 sm:px-6 pt-8 pb-10">
        <div className="max-w-2xl">
          {activeTab === "team" ? (
            <TeamSection callerRole={callerRole} />
          ) : needsSetup && setupLoading ? (
            <div className="flex items-center justify-center py-16">
              <span className="text-[var(--ophalo-muted)] text-sm">Loading…</span>
            </div>
          ) : needsSetup && (setupError || !setup) ? (
            <div className="flex items-center justify-center py-16">
              <span className="text-[var(--ophalo-muted)] text-sm">Could not load settings.</span>
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
