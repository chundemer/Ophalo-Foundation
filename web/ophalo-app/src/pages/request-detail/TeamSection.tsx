import { useState, useMemo } from "react";
import { useQuery } from "@tanstack/react-query";
import { User } from "lucide-react";
import { api, ApiError, type KeepRequestDetailResult } from "../../lib/apiClient";
import { FOCUS_RING } from "./helpers";

interface TeamSectionProps {
  requestId: string;
  detail: KeepRequestDetailResult;
  onDetailUpdated: (updated: KeepRequestDetailResult) => void;
}

export function TeamSection({ requestId, detail, onDetailUpdated }: TeamSectionProps) {
  const { canWatch, canUnwatch, canMute, canUnmute, canAssignResponsible } =
    detail.availableActions;

  const [submitting, setSubmitting] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [assignUserId, setAssignUserId] = useState("");
  const [addWatcherUserId, setAddWatcherUserId] = useState("");

  const { data: membersData } = useQuery({
    queryKey: ["members"],
    queryFn: () => api.listMembers(),
    enabled: canAssignResponsible,
    staleTime: 5 * 60 * 1000,
  });

  const activeMembers = useMemo(
    () => membersData?.members.filter((m) => m.status === "active") ?? [],
    [membersData],
  );

  const responsible = detail.participants.find(
    (p) => p.participationType === "responsible" && !p.detachedAtUtc,
  );
  const watchers = detail.participants.filter(
    (p) => p.participationType === "watching" && !p.detachedAtUtc,
  );
  const watcherIds = useMemo(() => new Set(watchers.map((w) => w.accountUserId)), [watchers]);
  const assignableMembers = activeMembers.filter(
    (m) => m.accountUserId !== responsible?.accountUserId,
  );
  const addableWatchers = activeMembers.filter((m) => !watcherIds.has(m.accountUserId));

  const hasTeamContent =
    canWatch || canUnwatch || canMute || canUnmute || canAssignResponsible ||
    responsible || watchers.length > 0;

  if (!hasTeamContent) return null;

  async function act(key: string, fn: () => Promise<KeepRequestDetailResult>) {
    if (submitting) return;
    setSubmitting(key);
    setError(null);
    try {
      const updated = await fn();
      onDetailUpdated(updated);
    } catch (e) {
      if (e instanceof ApiError && e.status === 409) {
        setError("Updated by another team member. Refresh to retry.");
      } else {
        setError("Action failed. Try again.");
      }
    } finally {
      setSubmitting(null);
    }
  }

  const inlineBtnCls = `rounded-md px-2.5 py-1.5 text-xs font-semibold bg-[var(--ophalo-navy)] text-white hover:opacity-90 disabled:opacity-50 transition-colors ${FOCUS_RING}`;

  return (
    <div className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-4 py-4 space-y-4">
      <p className="text-sm font-semibold text-[var(--ophalo-muted)]">Team &amp; context</p>

      {error && (
        <p className="rounded-lg p-2 text-xs bg-[var(--ophalo-danger-bg)] text-[var(--ophalo-danger)]">
          {error}
        </p>
      )}

      {/* Assigned */}
      <div>
        <p className="text-xs text-[var(--ophalo-muted)] mb-1">Assigned</p>
        {responsible ? (
          <div className="flex items-center justify-between gap-2">
            <div className="flex items-center gap-1.5 text-sm text-[var(--ophalo-ink)]">
              <User className="h-3.5 w-3.5 text-[var(--ophalo-muted)] shrink-0" />
              {responsible.displayName}
            </div>
            {canAssignResponsible && (
              <button
                type="button"
                disabled={!!submitting}
                onClick={() =>
                  void act("clear-responsible", () =>
                    api.clearResponsible(requestId, detail.version),
                  )
                }
                className={`text-xs text-[var(--ophalo-muted)] underline hover:text-[var(--ophalo-ink)] disabled:opacity-50 transition-colors ${FOCUS_RING}`}
              >
                {submitting === "clear-responsible" ? "Clearing…" : "Clear"}
              </button>
            )}
          </div>
        ) : canAssignResponsible ? (
          <div className="flex gap-2">
            <label htmlFor="assign-select" className="sr-only">Select member to assign</label>
            <select
              id="assign-select"
              value={assignUserId}
              onChange={(e) => setAssignUserId(e.target.value)}
              disabled={!!submitting}
              className={`flex-1 min-w-0 rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-2 py-1.5 text-xs text-[var(--ophalo-ink)] disabled:opacity-60 focus:outline-none focus:ring-1 focus:ring-[var(--keep-accent)]`}
            >
              <option value="">Unassigned — select…</option>
              {assignableMembers.map((m) => (
                <option key={m.accountUserId} value={m.accountUserId}>{m.email}</option>
              ))}
            </select>
            <button
              type="button"
              disabled={!assignUserId || !!submitting}
              onClick={() => {
                if (!assignUserId) return;
                void act("assign-responsible", () =>
                  api.setResponsible(requestId, assignUserId, detail.version),
                ).then(() => setAssignUserId(""));
              }}
              className={inlineBtnCls}
            >
              {submitting === "assign-responsible" ? "Assigning…" : "Assign"}
            </button>
          </div>
        ) : (
          <p className="text-sm text-[var(--ophalo-attention)] font-medium">Unassigned</p>
        )}
      </div>

      {/* Watching */}
      {(watchers.length > 0 || canAssignResponsible) && (
        <div>
          <p className="text-xs text-[var(--ophalo-muted)] mb-1">Watching</p>
          {watchers.length === 0 && (
            <p className="text-xs text-[var(--ophalo-muted)]">No watchers</p>
          )}
          {watchers.map((w) => (
            <div key={w.accountUserId} className="flex items-center justify-between gap-2 mb-1">
              <span className="text-xs text-[var(--ophalo-ink)]">{w.displayName}</span>
              {canAssignResponsible && (
                <button
                  type="button"
                  disabled={!!submitting}
                  onClick={() =>
                    void act(`remove-watcher-${w.accountUserId}`, () =>
                      api.removeWatcher(requestId, w.accountUserId, detail.version),
                    )
                  }
                  className={`text-xs text-[var(--ophalo-muted)] underline hover:text-[var(--ophalo-ink)] disabled:opacity-50 transition-colors ${FOCUS_RING}`}
                >
                  {submitting === `remove-watcher-${w.accountUserId}` ? "Removing…" : "Remove"}
                </button>
              )}
            </div>
          ))}
          {canAssignResponsible && addableWatchers.length > 0 && (
            <div className="flex gap-2 mt-1.5">
              <label htmlFor="add-watcher-select" className="sr-only">Add watcher</label>
              <select
                id="add-watcher-select"
                value={addWatcherUserId}
                onChange={(e) => setAddWatcherUserId(e.target.value)}
                disabled={!!submitting}
                className={`flex-1 min-w-0 rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-2 py-1.5 text-xs text-[var(--ophalo-ink)] disabled:opacity-60 focus:outline-none focus:ring-1 focus:ring-[var(--keep-accent)]`}
              >
                <option value="">Add watcher…</option>
                {addableWatchers.map((m) => (
                  <option key={m.accountUserId} value={m.accountUserId}>{m.email}</option>
                ))}
              </select>
              <button
                type="button"
                disabled={!addWatcherUserId || !!submitting}
                onClick={() => {
                  if (!addWatcherUserId) return;
                  void act("add-watcher", () =>
                    api.addWatcher(requestId, addWatcherUserId, detail.version),
                  ).then(() => setAddWatcherUserId(""));
                }}
                className={inlineBtnCls}
              >
                {submitting === "add-watcher" ? "Adding…" : "Add"}
              </button>
            </div>
          )}
        </div>
      )}

      {/* Self participation: watch / mute */}
      {(canWatch || canUnwatch || canMute || canUnmute) && (
        <div className="flex flex-col gap-1.5">
          {canWatch && (
            <button
              type="button"
              disabled={!!submitting}
              onClick={() => void act("watch", () => api.selfWatch(requestId, detail.version))}
              className={`text-xs text-[var(--ophalo-ink)] underline hover:text-[var(--ophalo-navy)] text-left disabled:opacity-60 transition-colors ${FOCUS_RING}`}
            >
              {submitting === "watch" ? "Watching…" : "Watch this request"}
            </button>
          )}
          {canUnwatch && (
            <button
              type="button"
              disabled={!!submitting}
              onClick={() => void act("unwatch", () => api.selfUnwatch(requestId, detail.version))}
              className={`text-xs text-[var(--ophalo-ink)] underline hover:text-[var(--ophalo-navy)] text-left disabled:opacity-60 transition-colors ${FOCUS_RING}`}
            >
              {submitting === "unwatch" ? "Unwatching…" : "Stop watching"}
            </button>
          )}
          {canMute && (
            <button
              type="button"
              disabled={!!submitting}
              onClick={() => void act("mute", () => api.mute(requestId, detail.version))}
              className={`text-xs text-[var(--ophalo-muted)] underline hover:text-[var(--ophalo-ink)] text-left disabled:opacity-60 transition-colors ${FOCUS_RING}`}
            >
              {submitting === "mute" ? "Muting…" : "Mute notifications"}
            </button>
          )}
          {canUnmute && (
            <button
              type="button"
              disabled={!!submitting}
              onClick={() => void act("unmute", () => api.unmute(requestId, detail.version))}
              className={`text-xs text-[var(--ophalo-muted)] underline hover:text-[var(--ophalo-ink)] text-left disabled:opacity-60 transition-colors ${FOCUS_RING}`}
            >
              {submitting === "unmute" ? "Unmuting…" : "Unmute notifications"}
            </button>
          )}
        </div>
      )}

    </div>
  );
}
