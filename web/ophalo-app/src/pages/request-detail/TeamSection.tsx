import { useState, useMemo } from "react";
import { useQuery } from "@tanstack/react-query";
import { User, X } from "lucide-react";
import { api, ApiError, type KeepRequestDetailResult } from "../../lib/apiClient";
import { FOCUS_RING } from "./helpers";
import { ResponsiveSheet } from "../../components/keep/ResponsiveSheet";
import { KeepButton } from "../../components/keep/KeepButton";

interface TeamSectionProps {
  requestId: string;
  detail: KeepRequestDetailResult;
  onDetailUpdated: (updated: KeepRequestDetailResult) => void;
  // compact: render only the assigned-owner line (Anchor's "owner context") — no card chrome,
  // no watcher list, no watch/mute controls. Those stay in the full card, used in canvas record
  // context. Assign/clear now surfaces as a quiet Change/Assign trigger that opens
  // OwnerReassignmentSheet outside the metadata ledger (slice 4, 2026-08-23); behavior unchanged.
  compact?: boolean;
  // bare: full mode without its own card chrome — used when a parent shares one enclosing
  // Record details module with other panels (locked exception, 2026-08-22).
  bare?: boolean;
  // Required when compact — opens OwnerReassignmentSheet. Unused in full mode.
  onOpenReassign?: () => void;
}

export function TeamSection({ requestId, detail, onDetailUpdated, compact = false, bare = false, onOpenReassign }: TeamSectionProps) {
  const { canWatch, canUnwatch, canMute, canUnmute, canAssignResponsible } =
    detail.availableActions;

  const [submitting, setSubmitting] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
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
  const addableWatchers = activeMembers.filter((m) => !watcherIds.has(m.accountUserId));

  // compact (Anchor owner context): show if there's an owner to display or a way to assign one.
  // full (record-details disclosure): the assigned-owner row is omitted (Anchor already shows
  // it), so this mode is only worth rendering for watcher/mute content.
  const hasTeamContent = compact
    ? !!responsible || canAssignResponsible
    : canWatch || canUnwatch || canMute || canUnmute || watchers.length > 0;

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

  const errorBlock = error && (
    <p className="rounded-lg p-2 text-xs bg-[var(--ophalo-danger-bg)] text-[var(--ophalo-danger)]">
      {error}
    </p>
  );

  // compact: static owner display plus a quiet Change/Assign trigger that opens
  // OwnerReassignmentSheet — the actual assign/clear controls live there now, outside the
  // metadata ledger (slice 4, 2026-08-23). No inline select/mutation here.
  const assignedBlock = (
    <div>
      {!compact && <p className="text-xs text-[var(--ophalo-muted)] mb-1">Assigned</p>}
      {compact && (
        <span className="text-xs font-semibold uppercase tracking-widest text-[var(--ophalo-muted)] mr-2">
          Owner
        </span>
      )}
      <div className="flex items-center justify-between gap-2">
        {responsible ? (
          <div className="flex items-center gap-1.5 text-sm text-[var(--ophalo-ink)]">
            <User className="h-3.5 w-3.5 text-[var(--ophalo-muted)] shrink-0" />
            {responsible.displayName}
          </div>
        ) : (
          <p className="text-sm text-[var(--ophalo-attention)] font-medium">Unassigned</p>
        )}
        {compact && canAssignResponsible && (
          <button
            type="button"
            onClick={onOpenReassign}
            className={`text-xs text-[var(--ophalo-muted)] underline hover:text-[var(--ophalo-ink)] transition-colors ${FOCUS_RING}`}
          >
            {responsible ? "Change" : "Assign"}
          </button>
        )}
      </div>
    </div>
  );

  if (compact) {
    // Inline Anchor context item (locked correction, 2026-08-22) — no independent card
    // border/padding/background; the Anchor owns the one boundary for the whole strip.
    return (
      <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
        {errorBlock}
        {assignedBlock}
      </div>
    );
  }

  return (
    <div className={bare ? "px-4 py-3 space-y-4" : "rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-4 py-4 space-y-4"}>
      <p className="text-sm font-semibold text-[var(--ophalo-muted)]">Team &amp; context</p>

      {errorBlock}

      {/* Assigned owner is already shown in the Anchor's compact owner context — not repeated here. */}

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

interface OwnerReassignmentSheetProps {
  requestId: string;
  detail: KeepRequestDetailResult;
  onDetailUpdated: (updated: KeepRequestDetailResult) => void;
  onClose: () => void;
}

/**
 * The Anchor's quiet Change/Assign trigger opens this — same setResponsible/clearResponsible
 * calls TeamSection's compact mode used inline before slice 4 (2026-08-23), just relocated
 * outside the metadata ledger. Authorization and mutation flow are unchanged.
 */
export function OwnerReassignmentSheet({ requestId, detail, onDetailUpdated, onClose }: OwnerReassignmentSheetProps) {
  const [assignUserId, setAssignUserId] = useState("");
  const [submitting, setSubmitting] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const { data: membersData } = useQuery({
    queryKey: ["members"],
    queryFn: () => api.listMembers(),
    staleTime: 5 * 60 * 1000,
  });
  const assignableMembers = useMemo(
    () => (membersData?.members ?? []).filter((m) => m.status === "active"),
    [membersData],
  );

  const responsible = detail.participants.find(
    (p) => p.participationType === "responsible" && !p.detachedAtUtc,
  );

  async function act(key: string, fn: () => Promise<KeepRequestDetailResult>) {
    if (submitting) return;
    setSubmitting(key);
    setError(null);
    try {
      const updated = await fn();
      onDetailUpdated(updated);
      onClose();
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

  return (
    <ResponsiveSheet
      onClose={onClose}
      labelledBy="owner-reassignment-sheet-heading"
      header={
        <div className="flex items-center justify-between">
          <h2 id="owner-reassignment-sheet-heading" className="text-base font-semibold text-[var(--ophalo-ink)]">
            Change owner
          </h2>
          <button
            type="button"
            onClick={onClose}
            className={`text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] p-1 rounded-md transition-colors ${FOCUS_RING}`}
          >
            <X className="h-4 w-4" />
            <span className="sr-only">Close</span>
          </button>
        </div>
      }
    >
      <div className="space-y-4">
        {error && (
          <p className="rounded-lg p-2 text-xs bg-[var(--ophalo-danger-bg)] text-[var(--ophalo-danger)]">
            {error}
          </p>
        )}

        {responsible && (
          <div>
            <p className="text-xs text-[var(--ophalo-muted)] mb-1">Currently assigned</p>
            <div className="flex items-center justify-between gap-2">
              <div className="flex items-center gap-1.5 text-sm text-[var(--ophalo-ink)]">
                <User className="h-3.5 w-3.5 text-[var(--ophalo-muted)] shrink-0" />
                {responsible.displayName}
              </div>
              <KeepButton
                variant="secondary"
                disabled={!!submitting}
                onClick={() => void act("clear-responsible", () => api.clearResponsible(requestId, detail.version))}
              >
                {submitting === "clear-responsible" ? "Clearing…" : "Clear"}
              </KeepButton>
            </div>
          </div>
        )}

        <div>
          <label htmlFor="reassign-owner-select" className="text-xs text-[var(--ophalo-muted)] mb-1 block">
            {responsible ? "Reassign to" : "Assign to"}
          </label>
          <div className="flex gap-2">
            <select
              id="reassign-owner-select"
              value={assignUserId}
              onChange={(e) => setAssignUserId(e.target.value)}
              disabled={!!submitting}
              className="flex-1 min-w-0 rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-2 py-1.5 text-sm text-[var(--ophalo-ink)] disabled:opacity-60 focus:outline-none focus:ring-1 focus:ring-[var(--keep-accent)]"
            >
              <option value="">Select…</option>
              {assignableMembers
                .filter((m) => m.accountUserId !== responsible?.accountUserId)
                .map((m) => (
                  <option key={m.accountUserId} value={m.accountUserId}>{m.email}</option>
                ))}
            </select>
            <KeepButton
              variant="secondary"
              disabled={!assignUserId || !!submitting}
              onClick={() => {
                if (!assignUserId) return;
                void act("assign-responsible", () => api.setResponsible(requestId, assignUserId, detail.version));
              }}
            >
              {submitting === "assign-responsible" ? "Assigning…" : "Assign"}
            </KeepButton>
          </div>
        </div>
      </div>
    </ResponsiveSheet>
  );
}
