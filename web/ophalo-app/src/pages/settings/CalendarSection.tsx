import { useEffect, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { api, ApiError, type KeepSetupCalendarResult, type KeepSetupResult } from "../../lib/apiClient";
import { KeepButton } from "../../components/keep/KeepButton";

const WEEKDAYS = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"] as const;
const DEFAULT_OPENS_AT = "09:00";
const DEFAULT_CLOSES_AT = "17:00";

interface DayDraft {
  open: boolean;
  opensAt: string;
  closesAt: string;
}

interface CalendarDraft {
  days: Record<string, DayDraft>;
  closures: string[];
}

function draftFromCalendar(calendar: KeepSetupCalendarResult): CalendarDraft {
  const days: Record<string, DayDraft> = {};
  for (const weekday of WEEKDAYS) {
    const interval = calendar.weeklyIntervals.find((i) => i.weekday === weekday);
    days[weekday] = interval
      ? { open: true, opensAt: interval.opensAt, closesAt: interval.closesAt }
      : { open: false, opensAt: DEFAULT_OPENS_AT, closesAt: DEFAULT_CLOSES_AT };
  }
  return { days, closures: [...calendar.closureDates].sort() };
}

function calendarFromDraft(draft: CalendarDraft): KeepSetupCalendarResult {
  return {
    weeklyIntervals: WEEKDAYS.filter((w) => draft.days[w].open).map((weekday) => ({
      weekday,
      opensAt: draft.days[weekday].opensAt,
      closesAt: draft.days[weekday].closesAt,
    })),
    closureDates: [...draft.closures].sort(),
  };
}

function dayError(day: DayDraft): string | null {
  if (!day.open) return null;
  if (!day.opensAt || !day.closesAt) return "Enter both an opening and a closing time.";
  // HH:mm strings compare correctly lexicographically.
  if (day.closesAt <= day.opensAt) return "Closing time must be after opening time.";
  return null;
}

const CONFLICT_COPY = "Settings were updated by another user. Refresh to load current settings.";
const GENERIC_COPY = "We couldn't save your business hours. Check your entries and try again.";

const ERROR_COPY: Record<string, string> = {
  LastWeeklyIntervalRequired: "At least one weekly open window is required while staffed-hours timing is active.",
  StaffedTimingRequiresWeeklyInterval: "Cannot switch to staffed hours without at least one open weekly window.",
  StaffedHoursTargetUnreachable:
    "Your scheduled hours do not provide enough open time to satisfy your configured SLA response target.",
  "KeepResponsePolicy.DuplicateWeekday": "Each weekday can only have one open window.",
  "KeepResponsePolicy.DuplicateClosureDate": "That closure date is already added.",
};

// Unrecognised codes (KeepSetup.CalendarValidation, OverlappingClosureChange, …) fall back to the
// generic copy; ApiError.message is just "API 422 /path" and is never shown.
function saveErrorCopy(code: string | undefined): string {
  return (code && ERROR_COPY[code]) || GENERIC_COPY;
}

export function CalendarSection({ setup }: { setup: KeepSetupResult }) {
  const queryClient = useQueryClient();
  const calendar = setup.calendar;

  const [draft, setDraft] = useState<CalendarDraft | null>(() => (calendar ? draftFromCalendar(calendar) : null));
  // Set after the first edit since the last save; the sync effect never overwrites an edited draft.
  const [dirty, setDirty] = useState(false);
  const [newClosure, setNewClosure] = useState("");
  const [closureError, setClosureError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);
  const [conflict, setConflict] = useState(false);
  const [refreshing, setRefreshing] = useState(false);
  const [refreshed, setRefreshed] = useState(false);

  const settingsVersion = setup.settingsVersion;
  const hasVersion = typeof settingsVersion === "string" && settingsVersion.length > 0;

  useEffect(() => {
    if (!dirty && calendar) setDraft(draftFromCalendar(calendar));
  }, [calendar, dirty]);

  if (!calendar || !draft) {
    return (
      <section className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] p-5 shadow-sm sm:p-6">
        <p className="text-sm text-[var(--ophalo-ink)]">
          We couldn't load your business hours. Refresh the page to try again.
        </p>
      </section>
    );
  }

  const hasDayError = WEEKDAYS.some((w) => dayError(draft.days[w]) !== null);

  function edit(next: CalendarDraft) {
    setDraft(next);
    setDirty(true);
    setSaved(false);
  }

  function patchDay(weekday: string, patch: Partial<DayDraft>) {
    if (!draft) return;
    edit({ ...draft, days: { ...draft.days, [weekday]: { ...draft.days[weekday], ...patch } } });
  }

  function addClosure() {
    if (!draft || !newClosure) return;
    if (draft.closures.includes(newClosure)) {
      setClosureError("That closure date is already added.");
      return;
    }
    setClosureError(null);
    edit({ ...draft, closures: [...draft.closures, newClosure].sort() });
    setNewClosure("");
  }

  function removeClosure(date: string) {
    if (!draft) return;
    edit({ ...draft, closures: draft.closures.filter((d) => d !== date) });
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
    if (submitting || !draft) return;
    if (!hasVersion) {
      setError("Settings version isn't available. Refresh settings, then try again.");
      return;
    }
    if (hasDayError) return;
    setSubmitting(true);
    setError(null);
    setSaved(false);
    setRefreshed(false);
    try {
      const updated = await api.updateCalendar({ ...calendarFromDraft(draft), settingsVersion });
      queryClient.setQueryData(["setup"], updated);
      setDirty(false);
      setConflict(false);
      setSaved(true);
    } catch (err) {
      if (err instanceof ApiError && err.status === 409) {
        // Stale save: never retried automatically; the draft stays as edited.
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
      <h2 className="keep-row-title mb-1.5">Business hours &amp; closures</h2>
      <p className="text-sm text-[var(--ophalo-muted)] mb-4">
        Times are in {setup.timeZone}. Set your open hours; closed days have no hours.
      </p>
      <form onSubmit={handleSubmit} className="space-y-6 max-w-xl">
        <div className="space-y-3">
          {WEEKDAYS.map((weekday) => {
            const day = draft.days[weekday];
            const rowError = dayError(day);
            return (
              <div key={weekday}>
                <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
                  <span className="w-24 text-sm font-medium text-[var(--ophalo-ink)]">{weekday}</span>
                  <button
                    type="button"
                    role="switch"
                    aria-checked={day.open}
                    aria-label={`Open on ${weekday}`}
                    onClick={() => patchDay(weekday, { open: !day.open })}
                    className={`relative inline-flex h-6 w-11 shrink-0 items-center rounded-full transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2 ${
                      day.open ? "bg-[var(--ophalo-success)]" : "bg-[var(--ophalo-border)]"
                    }`}
                  >
                    <span
                      className={`inline-block h-5 w-5 rounded-full bg-white shadow transition-transform ${
                        day.open ? "translate-x-5" : "translate-x-0.5"
                      }`}
                    />
                  </button>
                  <span
                    className={`inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium ${
                      day.open
                        ? "bg-[var(--ophalo-success)]/10 text-[var(--ophalo-success)]"
                        : "bg-[var(--ophalo-border-subtle)] text-[var(--ophalo-muted)]"
                    }`}
                  >
                    {day.open ? "Open" : "Closed"}
                  </span>
                  {day.open && (
                    <span className="flex items-center gap-2 text-sm text-[var(--ophalo-muted)]">
                      <input
                        type="time"
                        aria-label={`${weekday} opens at`}
                        value={day.opensAt}
                        onChange={(e) => patchDay(weekday, { opensAt: e.target.value })}
                        className="keep-field w-32"
                      />
                      to
                      <input
                        type="time"
                        aria-label={`${weekday} closes at`}
                        value={day.closesAt}
                        onChange={(e) => patchDay(weekday, { closesAt: e.target.value })}
                        className="keep-field w-32"
                      />
                    </span>
                  )}
                </div>
                {rowError && (
                  <p role="alert" className="mt-1 text-sm text-[var(--ophalo-danger)]">
                    {rowError}
                  </p>
                )}
              </div>
            );
          })}
        </div>

        <div>
          <h3 className="text-sm font-medium text-[var(--ophalo-ink)] mb-1.5">Closures</h3>
          <p className="text-xs text-[var(--ophalo-muted)] mb-2">Dates your business is closed, such as holidays.</p>
          <div className="flex flex-wrap items-center gap-2">
            <input
              type="date"
              aria-label="Closure date"
              value={newClosure}
              onChange={(e) => {
                setNewClosure(e.target.value);
                setClosureError(null);
              }}
              className="keep-field w-44"
            />
            <KeepButton type="button" variant="secondary" onClick={addClosure} disabled={!newClosure}>
              Add closure
            </KeepButton>
          </div>
          {closureError && (
            <p role="alert" className="mt-1 text-sm text-[var(--ophalo-danger)]">
              {closureError}
            </p>
          )}
          {draft.closures.length === 0 ? (
            <p className="mt-3 text-sm text-[var(--ophalo-muted)]">No closures scheduled.</p>
          ) : (
            <ul className="mt-3 space-y-1.5">
              {draft.closures.map((date) => (
                <li key={date} className="flex items-center gap-3 text-sm text-[var(--ophalo-ink)]">
                  <span>{date}</span>
                  <KeepButton
                    type="button"
                    variant="secondary"
                    aria-label={`Remove closure ${date}`}
                    onClick={() => removeClosure(date)}
                  >
                    Remove
                  </KeepButton>
                </li>
              ))}
            </ul>
          )}
        </div>

        {conflict && (
          <p role="alert" className="text-sm text-[var(--ophalo-danger)]">
            {CONFLICT_COPY} Your edits are kept until you refresh and save again.
          </p>
        )}
        {!conflict && !hasVersion && (
          <p role="alert" className="text-sm text-[var(--ophalo-danger)]">
            Settings version isn't available. Refresh settings to continue.
          </p>
        )}
        {refreshed && dirty && (
          <p className="text-sm text-[var(--ophalo-muted)]">Refreshed settings are loaded. Your unsaved edits are still shown.</p>
        )}
        {error && <p role="alert" className="text-sm text-[var(--ophalo-danger)]">{error}</p>}

        <div className="flex items-center gap-3">
          <KeepButton type="submit" variant="primary" disabled={submitting || !hasVersion || !dirty || hasDayError}>
            {submitting ? "Saving…" : "Save hours"}
          </KeepButton>
          {(conflict || !hasVersion) && (
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
