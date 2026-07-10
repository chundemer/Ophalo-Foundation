import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { api, type AccountRole } from "../lib/apiClient";
import { CompanySection } from "./settings/CompanySection";
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

  const needsSetup = activeTab === "public-profile" || activeTab === "policy";

  return (
    <div className="flex-1 overflow-y-auto">
      <div className="max-w-2xl mx-auto px-4 pt-8">
        <h1 className="text-xl font-semibold text-slate-900 mb-6">Settings</h1>

        <div className="flex border-b border-slate-200 mb-8">
          {TABS.map((tab) => (
            <button
              key={tab.id}
              type="button"
              onClick={() => setActiveTab(tab.id)}
              className={`px-4 py-2.5 text-sm font-medium border-b-2 -mb-px transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2 rounded-t-sm ${
                activeTab === tab.id
                  ? "border-[var(--keep-accent)] text-[var(--ophalo-navy)]"
                  : "border-transparent text-slate-500 hover:text-slate-800 hover:border-slate-300"
              }`}
            >
              {tab.label}
            </button>
          ))}
        </div>

        <div className="pb-8">
          {activeTab === "team" ? (
            <TeamSection callerRole={callerRole} />
          ) : needsSetup && setupLoading ? (
            <div className="flex items-center justify-center py-16">
              <span className="text-slate-400 text-sm">Loading…</span>
            </div>
          ) : needsSetup && (setupError || !setup) ? (
            <div className="flex items-center justify-center py-16">
              <span className="text-slate-500 text-sm">Could not load settings.</span>
            </div>
          ) : setup && activeTab === "public-profile" ? (
            <div className="space-y-10">
              <CompanySection setup={setup} />
              <hr className="border-slate-200" />
              <PublicLinkSection />
            </div>
          ) : setup && activeTab === "policy" ? (
            <PolicySection setup={setup} />
          ) : null}
        </div>
      </div>
    </div>
  );
}
