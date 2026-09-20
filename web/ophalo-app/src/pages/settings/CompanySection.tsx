import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { api, type KeepSetupResult, type MeResponse, ApiError } from "../../lib/apiClient";
import { normalizeNaPhoneInput, formatNaPhone } from "../../components/quick-capture/utils";
import { KeepButton } from "../../components/keep/KeepButton";

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
  /** Called with the canonical setup after a successful save so the parent re-syncs the draft. */
  onSaved: (saved: KeepSetupResult) => void;
  /** Opaque whole-settings version from the canonical setup read (ADR-506). */
  settingsVersion?: string;
  /** The stored account timezone; a version is required only when the draft differs from it. */
  savedTimeZone: string;
}

export function CompanySection({ draft, onDraftChange, onSaved, settingsVersion, savedTimeZone }: CompanySectionProps) {
  const queryClient = useQueryClient();

  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);
  const [conflict, setConflict] = useState(false);
  const [refreshing, setRefreshing] = useState(false);

  const hasVersion = typeof settingsVersion === "string" && settingsVersion.length > 0;
  const timeZoneChanged = draft.timeZone !== savedTimeZone;
  const needsVersion = timeZoneChanged && !hasVersion;

  async function handleRefresh() {
    if (refreshing) return;
    setRefreshing(true);
    try {
      await queryClient.refetchQueries({ queryKey: ["setup"] }, { throwOnError: true });
      setConflict(false);
      setError(null);
    } catch {
      setError("Couldn't refresh settings. Please try again.");
    } finally {
      setRefreshing(false);
    }
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (submitting) return;
    if (needsVersion) {
      setError("Settings version isn't available. Refresh settings, then try again.");
      return;
    }
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
        settingsVersion,
      });
      queryClient.setQueryData(["setup"], updated);
      onSaved(updated);
      // GAP-042: keep the workspace-shell ["me"] cache in sync immediately (no title flicker),
      // then invalidate to reconfirm server authority.
      queryClient.setQueryData(["me"], (prev: MeResponse | undefined) =>
        prev ? { ...prev, businessName: updated.businessName } : prev);
      void queryClient.invalidateQueries({ queryKey: ["me"] });
      setConflict(false);
      setSaved(true);
    } catch (err) {
      if (err instanceof ApiError && err.status === 409) {
        // Stale save: never retried automatically; the draft stays as typed.
        setConflict(true);
      } else if (err instanceof ApiError) {
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
    <section className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] p-5 shadow-sm sm:p-6">
      <h2 className="keep-row-title mb-1.5">Company</h2>
      <p className="text-sm text-[var(--ophalo-muted)] mb-4">
        Customers see this business name on their request page. Add the public phone or email you want customers to use, or leave them hidden.
      </p>
      <form onSubmit={handleSubmit} className="space-y-5 max-w-lg">
        <div>
          <label htmlFor="company-business-name" className="block text-sm font-medium text-[var(--ophalo-ink)] mb-1">
            Business name
          </label>
          <input
            id="company-business-name"
            type="text"
            value={draft.businessName}
            onChange={(e) => { onDraftChange({ businessName: e.target.value }); setSaved(false); }}
            required
            className="keep-field w-full placeholder:text-[var(--ophalo-muted)]"
          />
        </div>

        <div>
          <label htmlFor="company-timezone" className="block text-sm font-medium text-[var(--ophalo-ink)] mb-1">
            Timezone
          </label>
          <select
            id="company-timezone"
            value={knownTz ? draft.timeZone : ""}
            onChange={(e) => { onDraftChange({ timeZone: e.target.value }); setSaved(false); }}
            className="keep-field w-full"
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
          <label htmlFor="company-phone" className="block text-sm font-medium text-[var(--ophalo-ink)] mb-1">
            Customer-facing phone
          </label>
          <input
            id="company-phone"
            type="tel"
            inputMode="tel"
            value={formatNaPhone(draft.customerFacingPhone)}
            onChange={(e) => { onDraftChange({ customerFacingPhone: normalizeNaPhoneInput(e.target.value) }); setSaved(false); }}
            placeholder="Optional"
            className="keep-field w-full placeholder:text-[var(--ophalo-muted)]"
          />
        </div>

        <div>
          <label htmlFor="company-email" className="block text-sm font-medium text-[var(--ophalo-ink)] mb-1">
            Customer-facing email
          </label>
          <input
            id="company-email"
            type="email"
            value={draft.customerFacingEmail}
            onChange={(e) => { onDraftChange({ customerFacingEmail: e.target.value }); setSaved(false); }}
            placeholder="Optional"
            className="keep-field w-full placeholder:text-[var(--ophalo-muted)]"
          />
        </div>

        <div className="pt-2 border-t border-[var(--ophalo-border)]">
          <h3 className="text-sm font-semibold text-[var(--ophalo-ink)] mb-1">
            Branding &amp; trust anchors
          </h3>
          <p className="text-xs text-[var(--ophalo-muted)] mb-3">
            The logo, website, and customer-facing phone above can appear on customer request and
            tracker pages.
          </p>

          <div className="mb-4">
            <label htmlFor="company-logo-url" className="block text-sm font-medium text-[var(--ophalo-ink)] mb-1">
              Logo URL
            </label>
            <input
              id="company-logo-url"
              type="url"
              value={draft.logoUrl}
              onChange={(e) => { onDraftChange({ logoUrl: e.target.value }); setSaved(false); }}
              placeholder="https://example.com/logo.png"
              className="keep-field w-full placeholder:text-[var(--ophalo-muted)]"
            />
          </div>

          <div>
            <label htmlFor="company-website-url" className="block text-sm font-medium text-[var(--ophalo-ink)] mb-1">
              Website URL
            </label>
            <input
              id="company-website-url"
              type="url"
              value={draft.websiteUrl}
              onChange={(e) => { onDraftChange({ websiteUrl: e.target.value }); setSaved(false); }}
              placeholder="https://example.com"
              className="keep-field w-full placeholder:text-[var(--ophalo-muted)]"
            />
          </div>
        </div>

        {conflict && (
          <p role="alert" className="text-sm text-[var(--ophalo-danger)]">
            Someone else changed these settings, so your save wasn't applied. Refresh settings to load the latest version, then review your changes and save again.
          </p>
        )}
        {error && (
          <p className="text-sm text-[var(--ophalo-danger)]">{error}</p>
        )}

        <div className="flex items-center gap-3">
          <KeepButton type="submit" variant="primary" disabled={submitting}>
            {submitting ? "Saving…" : "Save company"}
          </KeepButton>
          {(conflict || needsVersion) && (
            <KeepButton type="button" variant="secondary" onClick={handleRefresh} disabled={refreshing}>
              {refreshing ? "Refreshing…" : "Refresh settings"}
            </KeepButton>
          )}
          {saved && <span className="text-sm text-[var(--ophalo-success)]">Saved.</span>}
        </div>
      </form>
    </section>
  );
}
