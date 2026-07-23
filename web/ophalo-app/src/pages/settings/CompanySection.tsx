import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { api, type KeepSetupResult, ApiError } from "../../lib/apiClient";
import { normalizeNaPhoneInput, formatNaPhone } from "../../components/quick-capture/utils";

const ALL_TIMEZONES: string[] = (() => {
  try {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    return [...(Intl as any).supportedValuesOf("timeZone") as string[]].sort();
  } catch {
    return ["UTC"];
  }
})();

export interface ProfileDraft {
  businessName: string;
  timeZone: string;
  customerFacingPhone: string;
  customerFacingEmail: string;
  logoUrl: string;
  websiteUrl: string;
}

export function draftFromSetup(setup: KeepSetupResult): ProfileDraft {
  return {
    businessName: setup.businessName,
    timeZone: setup.timeZone,
    customerFacingPhone: setup.customerFacingPhone ?? "",
    customerFacingEmail: setup.customerFacingEmail ?? "",
    logoUrl: setup.logoUrl ?? "",
    websiteUrl: setup.websiteUrl ?? "",
  };
}

interface CompanySectionProps {
  draft: ProfileDraft;
  onDraftChange: (patch: Partial<ProfileDraft>) => void;
}

export function CompanySection({ draft, onDraftChange }: CompanySectionProps) {
  const queryClient = useQueryClient();

  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (submitting) return;
    setSubmitting(true);
    setError(null);
    setSaved(false);
    try {
      const updated = await api.updateProfile({
        businessName: draft.businessName.trim(),
        timeZone: draft.timeZone,
        customerFacingPhone: draft.customerFacingPhone.trim() || null,
        customerFacingEmail: draft.customerFacingEmail.trim() || null,
        logoUrl: draft.logoUrl.trim() || null,
        websiteUrl: draft.websiteUrl.trim() || null,
      });
      queryClient.setQueryData(["setup"], updated);
      setSaved(true);
    } catch (err) {
      if (err instanceof ApiError) {
        setError(err.message);
      } else {
        setError("Something went wrong. Please try again.");
      }
    } finally {
      setSubmitting(false);
    }
  }

  const knownTz = ALL_TIMEZONES.includes(draft.timeZone);

  return (
    <section>
      <h2 className="text-base font-semibold text-slate-900 mb-1.5">Company</h2>
      <p className="text-sm text-slate-500 mb-4">
        Customers see this business name on their request page. Add the public phone or email you want customers to use, or leave them hidden.
      </p>
      <form onSubmit={handleSubmit} className="space-y-4 max-w-lg">
        <div>
          <label htmlFor="company-business-name" className="block text-sm font-medium text-slate-700 mb-1">
            Business name
          </label>
          <input
            id="company-business-name"
            type="text"
            value={draft.businessName}
            onChange={(e) => { onDraftChange({ businessName: e.target.value }); setSaved(false); }}
            required
            className="w-full rounded-md border border-slate-300 px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-slate-400"
          />
        </div>

        <div>
          <label htmlFor="company-timezone" className="block text-sm font-medium text-slate-700 mb-1">
            Timezone
          </label>
          <select
            id="company-timezone"
            value={knownTz ? draft.timeZone : ""}
            onChange={(e) => { onDraftChange({ timeZone: e.target.value }); setSaved(false); }}
            className="w-full rounded-md border border-slate-300 px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-slate-400"
          >
            {!knownTz && (
              <option value="" disabled>
                {draft.timeZone} (custom)
              </option>
            )}
            {ALL_TIMEZONES.map((tz) => (
              <option key={tz} value={tz}>
                {tz}
              </option>
            ))}
          </select>
        </div>

        <div>
          <label htmlFor="company-phone" className="block text-sm font-medium text-slate-700 mb-1">
            Customer-facing phone
          </label>
          <input
            id="company-phone"
            type="tel"
            inputMode="tel"
            value={formatNaPhone(draft.customerFacingPhone)}
            onChange={(e) => { onDraftChange({ customerFacingPhone: normalizeNaPhoneInput(e.target.value) }); setSaved(false); }}
            placeholder="Optional"
            className="w-full rounded-md border border-slate-300 px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-slate-400"
          />
        </div>

        <div>
          <label htmlFor="company-email" className="block text-sm font-medium text-slate-700 mb-1">
            Customer-facing email
          </label>
          <input
            id="company-email"
            type="email"
            value={draft.customerFacingEmail}
            onChange={(e) => { onDraftChange({ customerFacingEmail: e.target.value }); setSaved(false); }}
            placeholder="Optional"
            className="w-full rounded-md border border-slate-300 px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-slate-400"
          />
        </div>

        <div className="pt-2 border-t border-slate-100">
          <h3 className="text-sm font-semibold text-slate-900 mb-1">
            Branding &amp; trust anchors
          </h3>
          <p className="text-xs text-slate-500 mb-3">
            The logo, website, and customer-facing phone above can appear on customer request and
            tracker pages.
          </p>

          <div className="mb-4">
            <label htmlFor="company-logo-url" className="block text-sm font-medium text-slate-700 mb-1">
              Logo URL
            </label>
            <input
              id="company-logo-url"
              type="url"
              value={draft.logoUrl}
              onChange={(e) => { onDraftChange({ logoUrl: e.target.value }); setSaved(false); }}
              placeholder="https://example.com/logo.png"
              className="w-full rounded-md border border-slate-300 px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-slate-400"
            />
          </div>

          <div>
            <label htmlFor="company-website-url" className="block text-sm font-medium text-slate-700 mb-1">
              Website URL
            </label>
            <input
              id="company-website-url"
              type="url"
              value={draft.websiteUrl}
              onChange={(e) => { onDraftChange({ websiteUrl: e.target.value }); setSaved(false); }}
              placeholder="https://example.com"
              className="w-full rounded-md border border-slate-300 px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-slate-400"
            />
          </div>
        </div>

        {error && (
          <p className="text-sm text-red-600">{error}</p>
        )}

        <div className="flex items-center gap-3">
          <button
            type="submit"
            disabled={submitting}
            className="rounded-md bg-slate-900 px-4 py-2 text-sm font-medium text-white hover:bg-slate-700 disabled:opacity-50"
          >
            {submitting ? "Saving…" : "Save company"}
          </button>
          {saved && <span className="text-sm text-green-700">Saved.</span>}
        </div>
      </form>
    </section>
  );
}
