import { useState, useEffect } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { api, type KeepSetupResult, ApiError } from "../../lib/apiClient";
import { KeepButton } from "../../components/keep/KeepButton";

type PolicyField =
  | "first" | "standard" | "priority" | "statusCheck"
  | "firstBasis" | "standardBasis" | "priorityBasis";
const CLEAN: Record<PolicyField, boolean> = {
  first: false, standard: false, priority: false, statusCheck: false,
  firstBasis: false, standardBasis: false, priorityBasis: false,
};

const BASIS_OPTIONS = [
  { value: "Continuous", label: "Continuous" },
  { value: "StaffedHours", label: "Staffed hours" },
] as const;

const GENERIC_COPY = "We couldn't save your response policy. Check your entries and try again.";
const ERROR_COPY: Record<string, string> = {
  "KeepResponsePolicy.StaffedTimingRequiresWeeklyInterval":
    "Add at least one open day in Business Hours & Closures before switching a target to staffed hours.",
  "KeepResponsePolicy.StaffedHoursTargetUnreachable":
    "Your scheduled hours don't provide enough open time to meet that target. Shorten the target or add open hours.",
  "KeepSetup.PolicyValidation": "Response times must be whole numbers greater than zero.",
};

// ApiError.message is just "API 422 /path" and is never shown; unknown codes use the generic copy.
function saveErrorCopy(code: string | undefined): string {
  return (code && ERROR_COPY[code]) || GENERIC_COPY;
}

interface PolicySectionProps {
  setup: KeepSetupResult;
}

export function PolicySection({ setup }: PolicySectionProps) {
  const queryClient = useQueryClient();
  const p = setup.responsePolicy;

  const [firstResponse, setFirstResponse] = useState(String(p.firstResponseTargetMinutes));
  const [standardResponse, setStandardResponse] = useState(String(p.standardResponseTargetMinutes));
  const [priorityResponse, setPriorityResponse] = useState(String(p.priorityResponseTargetMinutes));
  const [statusCheck, setStatusCheck] = useState(String(p.statusCheckThresholdDays));
  const [firstBasis, setFirstBasis] = useState(p.firstResponseTimingBasis ?? "");
  const [standardBasis, setStandardBasis] = useState(p.standardResponseTimingBasis ?? "");
  const [priorityBasis, setPriorityBasis] = useState(p.priorityResponseTimingBasis ?? "");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);
  // Fields the user has edited since the last successful save; the sync effect never overwrites
  // these from the server (ADR-506: preserve unsaved values).
  const [dirty, setDirty] = useState<Record<PolicyField, boolean>>(CLEAN);
  const [conflict, setConflict] = useState(false);
  const [refreshing, setRefreshing] = useState(false);
  const [refreshed, setRefreshed] = useState(false);

  const settingsVersion = setup.settingsVersion;
  const hasVersion = typeof settingsVersion === "string" && settingsVersion.length > 0;
  // Never default a missing basis to Continuous (ADR-506): the save is blocked until settings reload.
  const hasBases = Boolean(
    p.firstResponseTimingBasis && p.standardResponseTimingBasis && p.priorityResponseTimingBasis,
  );
  const canSave = hasVersion && hasBases;
  const anyDirty = Object.values(dirty).some(Boolean);
  // Advisory only: the server's 422 stays authoritative (concurrent calendar changes).
  const staffedWithoutSchedule =
    [firstBasis, standardBasis, priorityBasis].includes("StaffedHours") &&
    setup.calendar !== undefined && setup.calendar.weeklyIntervals.length === 0;

  useEffect(() => {
    if (!dirty.first) setFirstResponse(String(p.firstResponseTargetMinutes));
    if (!dirty.standard) setStandardResponse(String(p.standardResponseTargetMinutes));
    if (!dirty.priority) setPriorityResponse(String(p.priorityResponseTargetMinutes));
    if (!dirty.statusCheck) setStatusCheck(String(p.statusCheckThresholdDays));
    if (!dirty.firstBasis) setFirstBasis(p.firstResponseTimingBasis ?? "");
    if (!dirty.standardBasis) setStandardBasis(p.standardResponseTimingBasis ?? "");
    if (!dirty.priorityBasis) setPriorityBasis(p.priorityResponseTimingBasis ?? "");
  }, [
    p.firstResponseTargetMinutes, p.standardResponseTargetMinutes, p.priorityResponseTargetMinutes, p.statusCheckThresholdDays,
    p.firstResponseTimingBasis, p.standardResponseTimingBasis, p.priorityResponseTimingBasis, dirty,
  ]);

  function edit(field: PolicyField, set: (value: string) => void, value: string) {
    set(value);
    setDirty((d) => ({ ...d, [field]: true }));
    setSaved(false);
  }

  async function handleRefresh() {
    if (refreshing) return;
    setRefreshing(true);
    try {
      await queryClient.refetchQueries({ queryKey: ["setup"] }, { throwOnError: true });
      setConflict(false);
      setError(null);
      setRefreshed(true);
    } catch {
      setError("Couldn't refresh settings. Please try again.");
    } finally {
      setRefreshing(false);
    }
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (submitting) return;
    if (!canSave) {
      setError("Settings aren't fully loaded. Refresh settings, then try again.");
      return;
    }
    setSubmitting(true);
    setError(null);
    setSaved(false);
    setRefreshed(false);
    try {
      const updated = await api.updatePolicy({
        firstResponseTargetMinutes: Number(firstResponse),
        standardResponseTargetMinutes: Number(standardResponse),
        priorityResponseTargetMinutes: Number(priorityResponse),
        statusCheckThresholdDays: Number(statusCheck),
        firstResponseTimingBasis: firstBasis,
        standardResponseTimingBasis: standardBasis,
        priorityResponseTimingBasis: priorityBasis,
        settingsVersion: settingsVersion as string,
      });
      queryClient.setQueryData(["setup"], updated);
      setDirty(CLEAN);
      setConflict(false);
      setSaved(true);
    } catch (err) {
      if (err instanceof ApiError && err.status === 409) {
        // Stale save: never retried automatically; the draft stays as typed.
        setConflict(true);
      } else if (err instanceof ApiError) {
        setError(saveErrorCopy(err.code));
      } else {
        setError("Something went wrong. Please try again.");
      }
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <section className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] p-5 shadow-sm sm:p-6">
      <div className="flex items-center gap-2 mb-1.5">
        <h2 className="keep-row-title">Response Policy</h2>
        <span className="inline-flex items-center gap-1.5 rounded-full border border-[color:var(--keep-accent)]/20 bg-[var(--keep-accent-bg)] px-2 py-0.5 text-xs font-medium text-[var(--keep-accent-hover)]">
          <span className="h-1.5 w-1.5 rounded-full bg-[var(--keep-accent)]" />
          Active
        </span>
      </div>
      <p className="text-sm text-[var(--ophalo-muted)] mb-4">
        Set the response targets your team works toward. The defaults work well for most service businesses — come back and adjust once you've seen how requests flow.
      </p>
      <p className="text-sm text-[var(--ophalo-muted)] mb-4">
        Continuous counts every hour. Staffed hours counts only the time your business is open, set in Business Hours &amp; Closures.
      </p>
      <form onSubmit={handleSubmit} className="space-y-5 max-w-lg">
        <div>
          <label className="block text-sm font-medium text-[var(--ophalo-ink)] mb-0.5">
            First response <span className="font-normal text-[var(--ophalo-muted)]">(minutes)</span>
          </label>
          <p className="text-xs text-[var(--ophalo-muted)] mb-1.5">How soon after a new request arrives should your team send an initial reply.</p>
          <input
            type="number"
            min={1}
            value={firstResponse}
            onChange={(e) => edit("first", setFirstResponse, e.target.value)}
            required
            className="keep-field w-36"
          />
          <select
            aria-label="First response timing"
            value={firstBasis}
            onChange={(e) => edit("firstBasis", setFirstBasis, e.target.value)}
            className="keep-field mt-2 w-44"
          >
            {firstBasis === "" && <option value="" disabled>—</option>}
            {BASIS_OPTIONS.map((o) => (
              <option key={o.value} value={o.value}>{o.label}</option>
            ))}
          </select>
        </div>
        <div>
          <label className="block text-sm font-medium text-[var(--ophalo-ink)] mb-0.5">
            Standard response <span className="font-normal text-[var(--ophalo-muted)]">(minutes)</span>
          </label>
          <p className="text-xs text-[var(--ophalo-muted)] mb-1.5">Your normal target for resolving or meaningfully advancing a typical open request.</p>
          <input
            type="number"
            min={1}
            value={standardResponse}
            onChange={(e) => edit("standard", setStandardResponse, e.target.value)}
            required
            className="keep-field w-36"
          />
          <select
            aria-label="Standard reply timing"
            value={standardBasis}
            onChange={(e) => edit("standardBasis", setStandardBasis, e.target.value)}
            className="keep-field mt-2 w-44"
          >
            {standardBasis === "" && <option value="" disabled>—</option>}
            {BASIS_OPTIONS.map((o) => (
              <option key={o.value} value={o.value}>{o.label}</option>
            ))}
          </select>
        </div>
        <div>
          <label className="block text-sm font-medium text-[var(--ophalo-ink)] mb-0.5">
            Priority response <span className="font-normal text-[var(--ophalo-muted)]">(minutes)</span>
          </label>
          <p className="text-xs text-[var(--ophalo-muted)] mb-1.5">Faster target for urgent requests that need quicker attention than standard.</p>
          <input
            type="number"
            min={1}
            value={priorityResponse}
            onChange={(e) => edit("priority", setPriorityResponse, e.target.value)}
            required
            className="keep-field w-36"
          />
          <select
            aria-label="Priority reply timing"
            value={priorityBasis}
            onChange={(e) => edit("priorityBasis", setPriorityBasis, e.target.value)}
            className="keep-field mt-2 w-44"
          >
            {priorityBasis === "" && <option value="" disabled>—</option>}
            {BASIS_OPTIONS.map((o) => (
              <option key={o.value} value={o.value}>{o.label}</option>
            ))}
          </select>
          <p className="mt-1 text-xs text-[var(--ophalo-muted)]">
            Priority is a response tier, not on-call or emergency coverage. Keep doesn't route or notify after hours.
          </p>
        </div>
        <div>
          <label className="block text-sm font-medium text-[var(--ophalo-ink)] mb-0.5">
            Status check <span className="font-normal text-[var(--ophalo-muted)]">(days)</span>
          </label>
          <p className="text-xs text-[var(--ophalo-muted)] mb-1.5">Flag open requests that haven't had any update in this many days so nothing goes stale.</p>
          <input
            type="number"
            min={1}
            value={statusCheck}
            onChange={(e) => edit("statusCheck", setStatusCheck, e.target.value)}
            required
            className="keep-field w-36"
          />
        </div>

        {staffedWithoutSchedule && (
          <p role="status" className="text-sm text-[var(--ophalo-muted)]">
            Staffed hours needs at least one open day. Add your hours in Business Hours &amp; Closures.
          </p>
        )}

        {conflict && (
          <p role="alert" className="text-sm text-[var(--ophalo-danger)]">
            Someone else changed these settings, so your save wasn't applied. Refresh settings to load the latest version, then review your changes and save again.
          </p>
        )}
        {!conflict && !hasVersion && (
          <p role="alert" className="text-sm text-[var(--ophalo-danger)]">
            Settings version isn't available. Refresh settings to continue.
          </p>
        )}
        {!conflict && hasVersion && !hasBases && (
          <p role="alert" className="text-sm text-[var(--ophalo-danger)]">
            Current timing settings aren't available. Refresh settings to continue.
          </p>
        )}
        {refreshed && anyDirty && (
          <p className="text-sm text-[var(--ophalo-muted)]">
            Refreshed settings are loaded. Your unsaved edits are still shown.
          </p>
        )}
        {error && (
          <p className="text-sm text-[var(--ophalo-danger)]">{error}</p>
        )}

        <div className="flex items-center gap-3">
          <KeepButton type="submit" variant="primary" disabled={submitting || !canSave}>
            {submitting ? "Saving…" : "Save policy"}
          </KeepButton>
          {(conflict || !canSave) && (
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
