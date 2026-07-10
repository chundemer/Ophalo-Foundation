import { useState, useEffect } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { api, type KeepSetupResult, ApiError } from "../../lib/apiClient";

const ALL_TIMEZONES: string[] = (() => {
  try {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    return [...(Intl as any).supportedValuesOf("timeZone") as string[]].sort();
  } catch {
    return ["UTC"];
  }
})();

interface CompanySectionProps {
  setup: KeepSetupResult;
}

export function CompanySection({ setup }: CompanySectionProps) {
  const queryClient = useQueryClient();

  const [businessName, setBusinessName] = useState(setup.businessName);
  const [timeZone, setTimeZone] = useState(setup.timeZone);
  const [phone, setPhone] = useState(setup.customerFacingPhone ?? "");
  const [email, setEmail] = useState(setup.customerFacingEmail ?? "");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  useEffect(() => {
    setBusinessName(setup.businessName);
    setTimeZone(setup.timeZone);
    setPhone(setup.customerFacingPhone ?? "");
    setEmail(setup.customerFacingEmail ?? "");
  }, [setup]);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (submitting) return;
    setSubmitting(true);
    setError(null);
    setSaved(false);
    try {
      const updated = await api.updateProfile({
        businessName: businessName.trim(),
        timeZone,
        customerFacingPhone: phone.trim() || null,
        customerFacingEmail: email.trim() || null,
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

  const knownTz = ALL_TIMEZONES.includes(timeZone);

  return (
    <section>
      <h2 className="text-base font-semibold text-slate-900 mb-1.5">Company</h2>
      <p className="text-sm text-slate-500 mb-4">
        Customers see this business name on their request page. Add the public phone or email you want customers to use, or leave them hidden.
      </p>
      <form onSubmit={handleSubmit} className="space-y-4 max-w-lg">
        <div>
          <label className="block text-sm font-medium text-slate-700 mb-1">
            Business name
          </label>
          <input
            type="text"
            value={businessName}
            onChange={(e) => { setBusinessName(e.target.value); setSaved(false); }}
            required
            className="w-full rounded-md border border-slate-300 px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-slate-400"
          />
        </div>

        <div>
          <label className="block text-sm font-medium text-slate-700 mb-1">
            Timezone
          </label>
          <select
            value={knownTz ? timeZone : ""}
            onChange={(e) => { setTimeZone(e.target.value); setSaved(false); }}
            className="w-full rounded-md border border-slate-300 px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-slate-400"
          >
            {!knownTz && (
              <option value="" disabled>
                {timeZone} (custom)
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
          <label className="block text-sm font-medium text-slate-700 mb-1">
            Customer-facing phone
          </label>
          <input
            type="tel"
            value={phone}
            onChange={(e) => { setPhone(e.target.value); setSaved(false); }}
            placeholder="Optional"
            className="w-full rounded-md border border-slate-300 px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-slate-400"
          />
        </div>

        <div>
          <label className="block text-sm font-medium text-slate-700 mb-1">
            Customer-facing email
          </label>
          <input
            type="email"
            value={email}
            onChange={(e) => { setEmail(e.target.value); setSaved(false); }}
            placeholder="Optional"
            className="w-full rounded-md border border-slate-300 px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-slate-400"
          />
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
