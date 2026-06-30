import { useState, useEffect } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { api, type KeepSetupResult, ApiError } from "../lib/apiClient";

// ─── Timezone selector ───────────────────────────────────────────────────────

const COMMON_TIMEZONES = [
  "Australia/Sydney",
  "Australia/Melbourne",
  "Australia/Brisbane",
  "Australia/Perth",
  "Australia/Adelaide",
  "Australia/Darwin",
  "Australia/Hobart",
  "Pacific/Auckland",
  "America/New_York",
  "America/Chicago",
  "America/Denver",
  "America/Los_Angeles",
  "America/Toronto",
  "America/Vancouver",
  "Europe/London",
  "Europe/Paris",
  "Europe/Berlin",
  "Europe/Amsterdam",
  "Asia/Singapore",
  "Asia/Tokyo",
  "Asia/Hong_Kong",
  "Asia/Dubai",
];

// ─── Company section ─────────────────────────────────────────────────────────

interface CompanySectionProps {
  setup: KeepSetupResult;
}

function CompanySection({ setup }: CompanySectionProps) {
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

  const knownTz = COMMON_TIMEZONES.includes(timeZone);

  return (
    <section>
      <h2 className="text-base font-semibold text-slate-900 mb-4">Company</h2>
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
            {COMMON_TIMEZONES.map((tz) => (
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

// ─── Response policy section ──────────────────────────────────────────────────

interface PolicySectionProps {
  setup: KeepSetupResult;
}

function PolicySection({ setup }: PolicySectionProps) {
  const queryClient = useQueryClient();
  const p = setup.responsePolicy;

  const [firstResponse, setFirstResponse] = useState(String(p.firstResponseTargetMinutes));
  const [standardResponse, setStandardResponse] = useState(String(p.standardResponseTargetMinutes));
  const [priorityResponse, setPriorityResponse] = useState(String(p.priorityResponseTargetMinutes));
  const [statusCheck, setStatusCheck] = useState(String(p.statusCheckThresholdDays));
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  useEffect(() => {
    setFirstResponse(String(p.firstResponseTargetMinutes));
    setStandardResponse(String(p.standardResponseTargetMinutes));
    setPriorityResponse(String(p.priorityResponseTargetMinutes));
    setStatusCheck(String(p.statusCheckThresholdDays));
  }, [p.firstResponseTargetMinutes, p.standardResponseTargetMinutes, p.priorityResponseTargetMinutes, p.statusCheckThresholdDays]);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (submitting) return;
    setSubmitting(true);
    setError(null);
    setSaved(false);
    try {
      const updated = await api.updatePolicy({
        firstResponseTargetMinutes: Number(firstResponse),
        standardResponseTargetMinutes: Number(standardResponse),
        priorityResponseTargetMinutes: Number(priorityResponse),
        statusCheckThresholdDays: Number(statusCheck),
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

  return (
    <section>
      <h2 className="text-base font-semibold text-slate-900 mb-4">Response Policy</h2>
      <form onSubmit={handleSubmit} className="space-y-4 max-w-lg">
        <div className="grid grid-cols-2 gap-4">
          <div>
            <label className="block text-sm font-medium text-slate-700 mb-1">
              First response target (minutes)
            </label>
            <input
              type="number"
              min={0}
              value={firstResponse}
              onChange={(e) => { setFirstResponse(e.target.value); setSaved(false); }}
              required
              className="w-full rounded-md border border-slate-300 px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-slate-400"
            />
          </div>
          <div>
            <label className="block text-sm font-medium text-slate-700 mb-1">
              Standard response target (minutes)
            </label>
            <input
              type="number"
              min={0}
              value={standardResponse}
              onChange={(e) => { setStandardResponse(e.target.value); setSaved(false); }}
              required
              className="w-full rounded-md border border-slate-300 px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-slate-400"
            />
          </div>
          <div>
            <label className="block text-sm font-medium text-slate-700 mb-1">
              Priority response target (minutes)
            </label>
            <input
              type="number"
              min={0}
              value={priorityResponse}
              onChange={(e) => { setPriorityResponse(e.target.value); setSaved(false); }}
              required
              className="w-full rounded-md border border-slate-300 px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-slate-400"
            />
          </div>
          <div>
            <label className="block text-sm font-medium text-slate-700 mb-1">
              Status-check threshold (days)
            </label>
            <input
              type="number"
              min={0}
              value={statusCheck}
              onChange={(e) => { setStatusCheck(e.target.value); setSaved(false); }}
              required
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
            {submitting ? "Saving…" : "Save policy"}
          </button>
          {saved && <span className="text-sm text-green-700">Saved.</span>}
        </div>
      </form>
    </section>
  );
}

// ─── Settings page ────────────────────────────────────────────────────────────

export function Settings() {
  const { data: setup, isLoading, isError } = useQuery({
    queryKey: ["setup"],
    queryFn: api.getSetup,
    staleTime: 2 * 60 * 1000,
  });

  if (isLoading) {
    return (
      <div className="flex flex-1 items-center justify-center">
        <span className="text-slate-400 text-sm">Loading…</span>
      </div>
    );
  }

  if (isError || !setup) {
    return (
      <div className="flex flex-1 items-center justify-center">
        <span className="text-slate-500 text-sm">Could not load settings.</span>
      </div>
    );
  }

  return (
    <div className="flex-1 overflow-y-auto">
      <div className="max-w-2xl mx-auto px-4 py-8 space-y-10">
        <h1 className="text-xl font-semibold text-slate-900">Settings</h1>
        <CompanySection setup={setup} />
        <hr className="border-slate-200" />
        <PolicySection setup={setup} />
      </div>
    </div>
  );
}
