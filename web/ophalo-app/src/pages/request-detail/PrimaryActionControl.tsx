import { useState } from "react";
import { api, ApiError, type KeepRequestDetailResult } from "../../lib/apiClient";
import { KeepButton } from "../../components/keep/KeepButton";
import { ConnectionFailureBanner } from "./ConnectionFailureBanner";
import { MutationConfirmDialog } from "./MutationConfirmDialog";
import { announcePolite } from "../../lib/liveAnnouncer";

// Shared, exhaustive renderer over the closed server `target` vocabulary (Session 0A,
// 2026-08-25). Exactly one of `RequestDetailAnchor` (no active attention) or
// `HeroAttentionBanner` (active attention) mounts this for a given request — never both — so the
// same server-authored `detail.availableActions.primaryAction` is never rendered/labeled twice by
// independent logic. The client performs no attention, lifecycle, work-state, or closeout
// precedence derivation; an unrecognized target/key combination fails safely with factual
// "unavailable" feedback rather than falling back to capability-flag inference.

// Locked advisory copy (RD-058B-2): "Mark work done" completes the request lifecycle only. It
// applies wherever that action is offered — the no-attention Anchor primary and the demoted
// active-attention control alike — because the caveat is about what the action does, not where it
// sits. It does not notify the customer, does not complete internal financial review, and does
// not resolve active attention or an open Actual Work draft.
export const MARK_WORK_DONE_CONFIRMATION =
  "This marks the request as Work completed. It does not notify the customer, does not complete internal financial review, and leaves any active attention or open Actual Work draft unresolved.";

export interface PrimaryActionSlotProps {
  requestId: string;
  detail: KeepRequestDetailResult;
  onDetailUpdated: (updated: KeepRequestDetailResult) => void;
  onOpenClearAttention: () => void;
  onRecordFollowUp: () => void;
  onContactLaunched: (direction: string, channel: string) => void;
  onActivateCustomerUpdateComposer: () => void;
  // GAP-067 Slice 4: `HeroAttentionBanner` passes `"request-primary"` so its routed
  // customer-resolution action (respond / acknowledge / follow-up / logged contact) renders as the
  // visually dominant teal `--keep-request-primary` fill. The no-attention Anchor mounts leave it
  // at the default `"teal"`. Lifecycle mutations remain server-authored; `demoteMarkWorkDone`
  // changes only the no-attention desktop presentation when unfinished operational work is known.
  primaryEmphasis?: "teal" | "request-primary";
  demoteMarkWorkDone?: boolean;
}

export function PrimaryActionSlot({
  requestId,
  detail,
  onDetailUpdated,
  onOpenClearAttention,
  onRecordFollowUp,
  onContactLaunched,
  onActivateCustomerUpdateComposer,
  primaryEmphasis = "teal",
  demoteMarkWorkDone = false,
}: PrimaryActionSlotProps) {
  const action = detail.availableActions.primaryAction;
  if (!action) return null;

  switch (action.target) {
    case "attention_sheet":
      if (action.key !== "acknowledge_attention") return <PrimaryActionUnavailable />;
      return (
        <KeepButton type="button" variant={primaryEmphasis} onClick={onOpenClearAttention}>
          {action.label}
        </KeepButton>
      );

    case "follow_up_sheet":
      if (action.key !== "resolve_follow_up") return <PrimaryActionUnavailable />;
      return (
        <KeepButton type="button" variant={primaryEmphasis} onClick={onRecordFollowUp}>
          {action.label}
        </KeepButton>
      );

    case "customer_update_composer":
      if (action.key !== "respond_to_customer") return <PrimaryActionUnavailable />;
      return (
        <KeepButton type="button" variant={primaryEmphasis} onClick={onActivateCustomerUpdateComposer}>
          {action.label}
        </KeepButton>
      );

    case "contact_sheet": {
      if (action.key !== "log_external_contact") return <PrimaryActionUnavailable />;
      const channel = detail.customerPhone ? "phone" : detail.customerEmail ? "email" : "other";
      return (
        <KeepButton type="button" variant={primaryEmphasis} onClick={() => onContactLaunched("outbound", channel)}>
          {action.label}
        </KeepButton>
      );
    }

    case "mutation": {
      if (action.key !== "mark_work_done" && action.key !== "close_request") return <PrimaryActionUnavailable />;
      const targetStatus = action.key === "mark_work_done" ? "resolved" : "closed";
      return (
        <PrimaryMutationButton
          requestId={requestId}
          detail={detail}
          onDetailUpdated={onDetailUpdated}
          label={action.label}
          targetStatus={targetStatus}
          confirmationCopy={action.key === "mark_work_done" ? MARK_WORK_DONE_CONFIRMATION : action.confirmationCopy}
          variant={action.key === "mark_work_done" && demoteMarkWorkDone ? "neutral" : "primary"}
        />
      );
    }

    default:
      // Unknown/malformed server target — fail safely rather than guess or fall back to
      // capability-flag inference.
      return <PrimaryActionUnavailable />;
  }
}

export function PrimaryActionUnavailable() {
  return (
    <span role="status" className="text-xs text-[var(--ophalo-muted)]">
      Primary action unavailable
    </span>
  );
}

// ---------------------------------------------------------------------------
// Mark work done — server-authored secondary control (Session 0A). Its own null-check gates it to
// the (attention-active) case where the backend populates it. RD-058B-2: it renders in the Work
// Canvas after Actual Work and before the composer (desktop + mobile), a quiet contextual
// lifecycle action — never an Anchor competitor to the attention primary. Its local confirm step
// carries the shared `MARK_WORK_DONE_CONFIRMATION` advisory.
// ---------------------------------------------------------------------------

export function MarkWorkDoneSecondarySlot({
  requestId,
  detail,
  onDetailUpdated,
}: {
  requestId: string;
  detail: KeepRequestDetailResult;
  onDetailUpdated: (updated: KeepRequestDetailResult) => void;
}) {
  const secondary = detail.availableActions.markWorkDoneSecondary;
  if (!secondary) return null;

  switch (secondary.consequence) {
    case "attention_remains":
      return (
        <PrimaryMutationButton
          requestId={requestId}
          detail={detail}
          onDetailUpdated={onDetailUpdated}
          label={secondary.label}
          targetStatus="resolved"
          confirmationCopy={MARK_WORK_DONE_CONFIRMATION}
          variant="secondary"
          accessibleSuffix="attention remains"
        />
      );
    default:
      return <PrimaryActionUnavailable />;
  }
}

// ---------------------------------------------------------------------------
// Shared confirm-then-submit control for the two mutation-target primary/secondary actions
// ---------------------------------------------------------------------------

interface PrimaryMutationButtonProps {
  requestId: string;
  detail: KeepRequestDetailResult;
  onDetailUpdated: (updated: KeepRequestDetailResult) => void;
  label: string;
  targetStatus: "resolved" | "closed";
  confirmationCopy: string | null;
  variant?: "primary" | "secondary" | "neutral";
  accessibleSuffix?: string;
}

// Always confirms before submitting, for both mark_work_done and close_request — this predates
// and is independent of the server's PrimaryActionMetadata.RequiresConfirmation flag, which only
// governs whether server-authored confirmationCopy is mandatory (close_request today). Do not
// gate the confirm step on that flag. RD-058B-2 correction: the confirm step is a focused
// `MutationConfirmDialog`, never an inline row that expands the Request Anchor.
function PrimaryMutationButton({
  requestId,
  detail,
  onDetailUpdated,
  label,
  targetStatus,
  confirmationCopy,
  variant = "primary",
  accessibleSuffix,
}: PrimaryMutationButtonProps) {
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [conflictDisabled, setConflictDisabled] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [confirming, setConfirming] = useState(false);
  const [connectionFailure, setConnectionFailure] = useState<{
    message: string;
    snapshot: { requestId: string; targetStatus: "resolved" | "closed"; version: string };
  } | null>(null);

  async function submit(retrySnapshot?: { requestId: string; targetStatus: "resolved" | "closed"; version: string }) {
    if (isSubmitting || conflictDisabled) return;
    // Snapshot at the original attempt (not read live from props at retry time) so a Retry
    // replays the exact request that failed, even if the parent re-renders with a newer
    // `detail.version` before the operator presses Retry — same rule as 5a's
    // ActualWorkComposer retries.
    const snapshot = retrySnapshot ?? { requestId, targetStatus, version: detail.version };
    const isRetry = retrySnapshot !== undefined;
    setConfirming(false);
    setIsSubmitting(true);
    setError(null);
    setConnectionFailure(null);
    try {
      const updated = await api.patchRequestStatus(snapshot.requestId, { status: snapshot.targetStatus }, snapshot.version);
      // `onDetailUpdated` can make the server-authored primary action disappear (and this
      // component with it) in the same commit — announce via the root-mounted live region
      // (`liveAnnouncer.ts`) rather than local state, which would never reach the DOM.
      if (isRetry) announcePolite("Retry succeeded.");
      onDetailUpdated(updated);
    } catch (e) {
      if (e instanceof ApiError) {
        if (e.status === 409) {
          setConflictDisabled(true);
          setError("This request was updated. Refresh to see the latest state.");
        } else {
          setError(snapshot.targetStatus === "resolved" ? "Could not mark work done. Try again." : "Could not close request. Try again.");
        }
      } else {
        setConnectionFailure({
          message: snapshot.targetStatus === "resolved" ? "Couldn't mark work done." : "Couldn't close request.",
          snapshot,
        });
      }
    } finally {
      setIsSubmitting(false);
    }
  }

  function handleRetry() {
    if (!connectionFailure) return;
    void submit(connectionFailure.snapshot);
  }

  function handleClick() {
    if (isSubmitting || conflictDisabled) return;
    setConfirming(true);
  }

  // Visible text always carries the consequence — never hide it in an aria-label-only suffix.
  // The demoted secondary (Mark work done, attention remains) must read its own consequence
  // before the user acts, not just be discoverable to screen readers.
  const visibleLabel = accessibleSuffix ? `${label}, ${accessibleSuffix}` : label;
  const dialogTitle = targetStatus === "resolved" ? "Mark request as Work completed?" : "Close this request?";
  // The advisory sits in the dialog body; omit it when it would merely repeat the title
  // (close_request's server copy is itself "Close this request?").
  const dialogBody = confirmationCopy && confirmationCopy !== dialogTitle ? confirmationCopy : null;

  return (
    <div className="flex flex-col gap-1">
      {connectionFailure && (
        <ConnectionFailureBanner message={connectionFailure.message} onRetry={handleRetry} isRetrying={isSubmitting} />
      )}
      {error && (
        <p
          aria-live="polite"
          className={`text-xs ${conflictDisabled ? "text-[var(--ophalo-attention)]" : "text-[var(--ophalo-danger)]"}`}
        >
          {error}
        </p>
      )}
      {variant === "primary" ? (
        <KeepButton
          type="button"
          variant="teal"
          disabled={isSubmitting || conflictDisabled}
          onClick={handleClick}
        >
          {isSubmitting ? "Working…" : visibleLabel}
        </KeepButton>
      ) : variant === "neutral" ? (
        <button
          type="button"
          disabled={isSubmitting || conflictDisabled}
          onClick={handleClick}
          className="inline-flex min-h-[42px] items-center justify-center rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-4 text-sm font-semibold text-[var(--ophalo-ink)] transition-colors hover:bg-[var(--ophalo-canvas)] disabled:cursor-not-allowed disabled:opacity-50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2"
        >
          {isSubmitting ? "Working…" : visibleLabel}
        </button>
      ) : (
        // Demoted secondary (locked desktop-polish decision, 2026-08-24): a quiet text-style
        // trigger, not an equal-weight outline button competing with the attention primary — and
        // its full visible text always states the consequence plainly before the user acts.
        <button
          type="button"
          disabled={isSubmitting || conflictDisabled}
          onClick={handleClick}
          className="px-2 text-left text-sm font-medium text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] disabled:opacity-50 disabled:cursor-not-allowed transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2 rounded"
        >
          {isSubmitting ? "Working…" : visibleLabel}
        </button>
      )}
      {confirming && (
        <MutationConfirmDialog
          title={dialogTitle}
          body={dialogBody}
          confirmLabel={label}
          onConfirm={() => void submit()}
          onCancel={() => setConfirming(false)}
        />
      )}
    </div>
  );
}
