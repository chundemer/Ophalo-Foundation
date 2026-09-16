import { useEffect, useRef, useState } from "react";
import { MoreVertical } from "lucide-react";
import { api, ApiError, type KeepRequestDetailResult } from "../../lib/apiClient";
import { KeepButton } from "../../components/keep/KeepButton";
import { MutationConfirmDialog } from "./MutationConfirmDialog";
import { FOCUS_RING } from "./helpers";

// GAP-063 / ADR-296: Spam and Test are terminal classifications, same family as Closed/Cancelled
// (locked 2026-09-16 — no client-side unclassify/reset, see decision-index ADR-296). Gating is
// solely on the server-authored `availableActions.canClassify`; it is independent of attention
// state (`KeepRequestActionPolicy.CanClassify = isOwnerAdmin && isNonTerminal`), so this renders
// in the Anchor header regardless of whether `HeroAttentionBanner` is also showing.
//
// Placement (2026-09-16 correction): a permanent card here would cost ~180px of vertical space in
// the memory rail for a sub-1%-frequency action, pushing Planning/Timing/Owner off-screen. A quiet
// "More actions" trigger next to the reference code costs nothing when unused. Modeled on
// `PrimaryMutationButton` (confirm-then-submit, snapshot version at attempt time, distinguish
// conflict/forbidden/other failure), not `CloseRequestCard`, which has neither a confirmation step
// nor reason input.
const REASON_MAX_LENGTH = 500;

type ClassifyTarget = "spam" | "test";

interface ClassifyRequestMenuProps {
  requestId: string;
  detail: KeepRequestDetailResult;
  onDetailUpdated: (updated: KeepRequestDetailResult) => void;
  /** Refetches Request Detail. Called on 409/403 so the menu re-renders against authoritative
   * state before the operator can retry — a conflict/forbidden response carries no updated
   * detail of its own. */
  onRefreshDetail: () => void;
}

const TARGET_COPY: Record<ClassifyTarget, { menuLabel: string; actionLabel: string; title: string; body: string }> = {
  spam: {
    menuLabel: "Mark as spam",
    actionLabel: "Mark as spam",
    title: "Mark this request as spam?",
    body: "This removes the request from active queues and metrics, and disables further activity on the customer page. This is final and cannot be undone from here.",
  },
  test: {
    menuLabel: "Mark as test",
    actionLabel: "Mark as test",
    title: "Mark this request as test?",
    body: "This removes the request from active queues and metrics, and disables further activity on the customer page. This is final and cannot be undone from here.",
  },
};

export function ClassifyRequestCard({ requestId, detail, onDetailUpdated, onRefreshDetail }: ClassifyRequestMenuProps) {
  const canClassify = detail.availableActions.canClassify;
  const [menuOpen, setMenuOpen] = useState(false);
  const [confirmingTarget, setConfirmingTarget] = useState<ClassifyTarget | null>(null);
  const [reason, setReason] = useState("");
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const reasonRef = useRef<HTMLTextAreaElement>(null);
  const rootRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!menuOpen) return;
    function onPointerDown(e: MouseEvent) {
      if (rootRef.current && !rootRef.current.contains(e.target as Node)) setMenuOpen(false);
    }
    function onKeyDown(e: KeyboardEvent) {
      if (e.key === "Escape") setMenuOpen(false);
    }
    document.addEventListener("mousedown", onPointerDown);
    document.addEventListener("keydown", onKeyDown);
    return () => {
      document.removeEventListener("mousedown", onPointerDown);
      document.removeEventListener("keydown", onKeyDown);
    };
  }, [menuOpen]);

  if (!canClassify) return null;

  function openConfirm(target: ClassifyTarget) {
    setMenuOpen(false);
    if (isSubmitting) return;
    setConfirmingTarget(target);
    setError(null);
  }

  function closeConfirm() {
    setConfirmingTarget(null);
    setReason("");
    setError(null);
  }

  async function submit() {
    if (!confirmingTarget || isSubmitting) return;
    const target = confirmingTarget;
    setIsSubmitting(true);
    setError(null);
    try {
      const updated = await api.classifyRequest(
        requestId,
        { targetStatus: target, reason: reason.trim() || undefined },
        detail.version,
      );
      onDetailUpdated(updated);
      setConfirmingTarget(null);
      setReason("");
    } catch (e) {
      if (e instanceof ApiError && e.status === 409) {
        setError("This request was updated elsewhere. Reloading the latest state — your reason is kept.");
        onRefreshDetail();
      } else if (e instanceof ApiError && e.status === 403) {
        setError("You no longer have permission to do this. Reloading the latest state.");
        onRefreshDetail();
      } else {
        setError(
          target === "spam" ? "Could not mark as spam. Try again." : "Could not mark as test. Try again.",
        );
      }
    } finally {
      setIsSubmitting(false);
    }
  }

  const activeTarget = confirmingTarget ? TARGET_COPY[confirmingTarget] : null;
  const reasonId = "classify-reason";

  return (
    <div ref={rootRef} className="relative inline-flex">
      <KeepButton
        type="button"
        variant="secondary"
        aria-label="Admin actions"
        aria-haspopup="menu"
        aria-expanded={menuOpen}
        disabled={isSubmitting}
        onClick={() => setMenuOpen((v) => !v)}
        className="!min-h-9 !px-2"
      >
        <MoreVertical className="h-4 w-4" aria-hidden="true" />
      </KeepButton>
      {menuOpen && (
        <div
          role="menu"
          className={`absolute right-0 top-[calc(100%+4px)] z-10 min-w-[180px] rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] py-1 shadow-lg`}
        >
          {(Object.keys(TARGET_COPY) as ClassifyTarget[]).map((target) => (
            <button
              key={target}
              type="button"
              role="menuitem"
              onClick={() => openConfirm(target)}
              className={`block w-full px-3 py-2 text-left text-sm text-[var(--ophalo-ink)] hover:bg-[var(--ophalo-canvas)] ${FOCUS_RING}`}
            >
              {TARGET_COPY[target].menuLabel}
            </button>
          ))}
        </div>
      )}
      {activeTarget && (
        <MutationConfirmDialog
          title={activeTarget.title}
          body={activeTarget.body}
          confirmLabel={isSubmitting ? "Working…" : activeTarget.actionLabel}
          onConfirm={() => void submit()}
          onCancel={closeConfirm}
          initialContentFocus={reasonRef}
          content={
            <div>
              {error && (
                <p aria-live="polite" className="mb-2 rounded-lg bg-[var(--ophalo-danger-bg)] p-2 text-xs text-[var(--ophalo-danger)]">
                  {error}
                </p>
              )}
              <label htmlFor={reasonId} className="text-xs font-semibold text-[var(--ophalo-ink)]">
                Internal reason (optional)
              </label>
              <textarea
                id={reasonId}
                ref={reasonRef}
                value={reason}
                maxLength={REASON_MAX_LENGTH}
                disabled={isSubmitting}
                onChange={(e) => setReason(e.target.value)}
                rows={3}
                className="mt-1 w-full resize-none rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-canvas)] p-2 text-sm text-[var(--ophalo-ink)]"
              />
              <p className="mt-1 text-right text-[11px] text-[var(--ophalo-muted)]">
                {reason.length}/{REASON_MAX_LENGTH}
              </p>
            </div>
          }
        />
      )}
    </div>
  );
}
