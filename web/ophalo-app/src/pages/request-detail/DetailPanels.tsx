import { useState, useEffect, useRef } from "react";
import { useQuery } from "@tanstack/react-query";
import { Copy, Check, AlertTriangle, Clock, Phone, Mail, X } from "lucide-react";
import {
  api,
  ApiError,
  type KeepRequestDetailResult,
} from "../../lib/apiClient";
import { KeepButton } from "../../components/keep/KeepButton";
import { ResponsiveSheet } from "../../components/keep/ResponsiveSheet";
import { formatNaPhone } from "../../components/quick-capture/utils";
import { KeepBadge, type KeepBadgeVariant } from "../../components/keep/KeepBadge";
import { useCopyFeedback } from "../../hooks/useCopyFeedback";
import {
  FOCUS_RING,
  INPUT_CLS,
  STATUS_CONFLICT_MESSAGE,
  formatDate,
  buildAttentionGuidance,
  statusLabel,
  statusBadgeVariant,
} from "./helpers";
import {
  type HighlightLevel,
  type AttentionHighlights,
  highlightBorderCls,
  highlightBgCls,
  highlightBoxShadow,
  RecommendedActionBadge,
  maxHighlight,
} from "./highlights";

// ---------------------------------------------------------------------------
// Shared layout props — used by DesktopLayout and both MobileLayout components
// ---------------------------------------------------------------------------

export interface RequestDetailLayoutProps {
  requestId: string;
  detail: KeepRequestDetailResult;
  highlights: AttentionHighlights;
  showProminentFeedbackCard: boolean;
  onDetailUpdated: (updated: KeepRequestDetailResult) => void;
  onContactLaunched: (direction: string, channel: string) => void;
  onEditLocation: () => void;
  onRecordFollowUp: () => void;
  onCreateFollowUp: () => void;
  onReviewSuccess: () => void;
}

// ---------------------------------------------------------------------------
// Log external contact — card affordance that opens the controller-owned modal
// ---------------------------------------------------------------------------

interface LogContactCardProps {
  detail: KeepRequestDetailResult;
  onContactLaunched: (direction: string, channel: string) => void;
  highlight?: HighlightLevel;
}

export function LogContactCard({ detail, onContactLaunched, highlight }: LogContactCardProps) {
  const { canLogExternalContact } = detail.availableActions;
  if (!canLogExternalContact) return null;
  const contactChannel = detail.customerPhone ? "phone" : detail.customerEmail ? "email" : "other";
  const shadow = highlightBoxShadow(highlight);
  return (
    <div
      className={`rounded-xl border px-5 py-4 transition-[border-color,background-color,box-shadow] ${highlightBorderCls(highlight)} ${highlightBgCls()}`}
      style={shadow ? { boxShadow: shadow } : undefined}
    >
      <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
        <p className="text-sm font-semibold text-[var(--ophalo-ink)]">Log external contact</p>
        <RecommendedActionBadge level={highlight} />
      </div>
      <p className="text-xs text-[var(--ophalo-muted)] mb-3">
        Record a call, text, email, or in-person conversation outside Keep.
      </p>
      <KeepButton
        type="button"
        variant="secondary"
        onClick={() => onContactLaunched("outbound", contactChannel)}
        className="w-full"
      >
        Log external contact
      </KeepButton>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Clear attention
// ---------------------------------------------------------------------------

interface ClearAttentionSheetProps {
  requestId: string;
  detail: KeepRequestDetailResult;
  onDetailUpdated: (updated: KeepRequestDetailResult) => void;
  onClose: () => void;
}

export function ClearAttentionSheet({ requestId, detail, onDetailUpdated, onClose }: ClearAttentionSheetProps) {
  const { acknowledgeReasonMaxLength } = detail.validation;

  const [reason, setReason] = useState("");
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [conflictDisabled, setConflictDisabled] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [showDiscardConfirm, setShowDiscardConfirm] = useState(false);
  const keepEditingRef = useRef<HTMLButtonElement>(null);
  const discardRef = useRef<HTMLButtonElement>(null);
  const previousFocusRef = useRef<Element | null>(null);

  const dirty = reason.trim().length > 0;
  const canSubmit = dirty && !isSubmitting && !conflictDisabled;

  function attemptClose() {
    if (dirty) {
      setShowDiscardConfirm(true);
      return;
    }
    onClose();
  }

  // Nested alertdialog inside ResponsiveSheet's own dialog (matches CatalogItemDrawer's P2 fix):
  // captures Escape/Tab before ResponsiveSheet's own listener and traps focus between the two
  // confirm buttons while contentInert removes the background form from tab order/hit-testing.
  useEffect(() => {
    if (!showDiscardConfirm) return;
    previousFocusRef.current = document.activeElement;
    keepEditingRef.current?.focus();

    function onKeyDown(e: KeyboardEvent) {
      if (e.key === "Escape") {
        e.preventDefault();
        e.stopPropagation();
        setShowDiscardConfirm(false);
        return;
      }
      if (e.key !== "Tab") return;
      e.preventDefault();
      e.stopPropagation();
      const first = keepEditingRef.current;
      const last = discardRef.current;
      if (!first || !last) return;
      (document.activeElement === first ? last : first).focus();
    }
    document.addEventListener("keydown", onKeyDown, true);
    return () => {
      document.removeEventListener("keydown", onKeyDown, true);
      const prior = previousFocusRef.current;
      if (prior instanceof HTMLElement) prior.focus();
    };
  }, [showDiscardConfirm]);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!canSubmit) return;
    setIsSubmitting(true);
    setError(null);
    try {
      const updated = await api.acknowledgeAttention(requestId, reason.trim(), detail.version);
      onDetailUpdated(updated);
      onClose();
    } catch (e) {
      if (e instanceof ApiError && e.status === 409) {
        setConflictDisabled(true);
        setError(STATUS_CONFLICT_MESSAGE);
      } else {
        setError("Could not clear attention. Try again.");
      }
    } finally {
      setIsSubmitting(false);
    }
  }

  return (
    <ResponsiveSheet
      onClose={attemptClose}
      labelledBy="clear-attention-sheet-heading"
      contentInert={showDiscardConfirm}
      header={
        <div className="flex items-center justify-between">
          <h2 id="clear-attention-sheet-heading" className="text-base font-semibold text-[var(--ophalo-ink)]">
            Clear attention
          </h2>
          <button
            type="button"
            onClick={attemptClose}
            className={`text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] p-1 rounded-md transition-colors ${FOCUS_RING}`}
          >
            <X className="h-4 w-4" />
            <span className="sr-only">Close</span>
          </button>
        </div>
      }
      footer={
        <KeepButton
          type="submit"
          form="clear-attention-form"
          variant="secondary"
          disabled={!canSubmit}
          className="w-full"
        >
          {isSubmitting ? "Clearing…" : "Clear attention"}
        </KeepButton>
      }
      overlay={
        showDiscardConfirm && (
          <div
            role="alertdialog"
            aria-modal="true"
            aria-label="Discard changes"
            className="absolute inset-0 z-10 flex items-center justify-center bg-black/30 px-6"
          >
            <div className="max-w-xs w-full rounded-lg bg-[var(--ophalo-card)] shadow-xl p-4 flex flex-col gap-3">
              <p className="text-sm text-[var(--ophalo-ink)]">Discard your note and keep this attention active?</p>
              <div className="flex items-center justify-end gap-3">
                <button
                  ref={keepEditingRef}
                  type="button"
                  onClick={() => setShowDiscardConfirm(false)}
                  className={`text-sm text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] rounded ${FOCUS_RING}`}
                >
                  Keep editing
                </button>
                <button
                  ref={discardRef}
                  type="button"
                  onClick={onClose}
                  className={`px-3 py-1.5 rounded-lg text-sm font-medium bg-[var(--ophalo-danger)] text-white hover:opacity-90 ${FOCUS_RING}`}
                >
                  Discard
                </button>
              </div>
            </div>
          </div>
        )
      }
    >
      <p className="text-xs text-[var(--ophalo-muted)] mb-3">
        Use only when no customer update or contact log is needed.
      </p>
      {error && (
        <div
          className={`mb-3 rounded-lg p-3 text-xs ${
            conflictDisabled
              ? "bg-[var(--ophalo-attention-bg)] text-[var(--ophalo-attention)]"
              : "bg-[var(--ophalo-danger-bg)] text-[var(--ophalo-danger)]"
          }`}
        >
          {error}
        </div>
      )}
      <form id="clear-attention-form" onSubmit={(e) => void handleSubmit(e)}>
        <label htmlFor="ack-reason" className="block text-xs font-semibold text-[var(--ophalo-muted)] mb-1">
          Brief note before clearing
        </label>
        <textarea
          id="ack-reason"
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          maxLength={acknowledgeReasonMaxLength}
          disabled={conflictDisabled}
          placeholder="Example: Reviewed — no follow-up needed."
          rows={3}
          className={`${INPUT_CLS} resize-none`}
        />
      </form>
    </ResponsiveSheet>
  );
}

// ---------------------------------------------------------------------------
// Feedback review form — shared by WorkControlsGroup and ProminentFeedbackCard
// ---------------------------------------------------------------------------

interface FeedbackReviewSectionProps {
  requestId: string;
  detail: KeepRequestDetailResult;
  onDetailUpdated: (updated: KeepRequestDetailResult) => void;
  onReviewSuccess?: () => void;
}

function FeedbackReviewSection({
  requestId,
  detail,
  onDetailUpdated,
  onReviewSuccess,
}: FeedbackReviewSectionProps) {
  const { canMarkFeedbackReviewed } = detail.availableActions;
  const { feedbackReviewNoteMaxLength } = detail.validation;

  const [note, setNote] = useState("");
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [conflictDisabled, setConflictDisabled] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (
    !canMarkFeedbackReviewed ||
    detail.feedbackWasResolved !== false ||
    detail.feedbackReviewedAtUtc != null
  )
    return null;

  const ageBucket = detail.feedbackReviewAgeBucket;
  const ageLabel =
    ageBucket === "overdue" ? "Overdue" : ageBucket === "aging" ? "Aging" : ageBucket === "new" ? "New" : null;
  const ageBadgeVariant: KeepBadgeVariant =
    ageBucket === "overdue" ? "danger" : ageBucket === "aging" ? "attention" : "default";

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (isSubmitting || conflictDisabled) return;
    setIsSubmitting(true);
    setError(null);
    try {
      const updated = await api.markFeedbackReviewed(
        requestId,
        { note: note.trim() || null },
        detail.version,
      );
      onDetailUpdated(updated);
      onReviewSuccess?.();
    } catch (e) {
      if (e instanceof ApiError && e.status === 409) {
        setConflictDisabled(true);
        setError(STATUS_CONFLICT_MESSAGE);
      } else {
        setError("Could not mark feedback reviewed. Try again.");
      }
    } finally {
      setIsSubmitting(false);
    }
  }

  return (
    <div>
      <div className="flex items-center gap-2 mb-2">
        <p className="text-sm font-semibold text-[var(--ophalo-ink)]">Negative feedback</p>
        {ageLabel && <KeepBadge variant={ageBadgeVariant}>{ageLabel}</KeepBadge>}
      </div>
      {detail.feedbackCommentVisible && detail.feedbackComment && (
        <p className="text-xs text-[var(--ophalo-muted)] mb-2 italic">
          &ldquo;{detail.feedbackComment}&rdquo;
        </p>
      )}
      {error && (
        <div
          className={`mb-2 rounded-lg p-3 text-xs ${
            conflictDisabled
              ? "bg-[var(--ophalo-attention-bg)] text-[var(--ophalo-attention)]"
              : "bg-[var(--ophalo-danger-bg)] text-[var(--ophalo-danger)]"
          }`}
        >
          {error}
        </div>
      )}
      <form onSubmit={(e) => void handleSubmit(e)} className="space-y-2">
        <div>
          <label htmlFor="feedback-note" className="sr-only">Internal note (optional)</label>
          <textarea
            id="feedback-note"
            value={note}
            onChange={(e) => setNote(e.target.value)}
            maxLength={feedbackReviewNoteMaxLength}
            disabled={conflictDisabled}
            placeholder="Internal note (optional)…"
            rows={2}
            className={`${INPUT_CLS} resize-none`}
          />
        </div>
        <KeepButton
          type="submit"
          variant="primary"
          disabled={isSubmitting || conflictDisabled}
          className="w-full"
        >
          {isSubmitting ? "Marking…" : "Mark reviewed"}
        </KeepButton>
      </form>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Feedback summary card — quiet completed state for positive feedback
// ---------------------------------------------------------------------------

export function FeedbackSummaryCard({ detail, bare = false }: { detail: KeepRequestDetailResult; bare?: boolean }) {
  if (detail.feedbackWasResolved !== true || !detail.feedbackSubmittedAtUtc) return null;

  return (
    <div className={bare ? "px-4 py-3" : "rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-5 py-4"}>
      <p className="text-sm font-semibold text-[var(--ophalo-ink)]">Customer feedback</p>
      <p className="mt-1 text-xs text-[var(--ophalo-muted)]">
        Customer confirmed their request was resolved
        {detail.feedbackSubmittedAtUtc ? ` on ${formatDate(detail.feedbackSubmittedAtUtc)}` : ""}.
      </p>
      {detail.feedbackCommentVisible && detail.feedbackComment && (
        <p className="mt-1.5 text-xs text-[var(--ophalo-muted)] italic">
          &ldquo;{detail.feedbackComment}&rdquo;
        </p>
      )}
    </div>
  );
}

// ---------------------------------------------------------------------------
// Work controls group — sidebar feedback review card
// ---------------------------------------------------------------------------

interface WorkControlsGroupProps {
  requestId: string;
  detail: KeepRequestDetailResult;
  onDetailUpdated: (updated: KeepRequestDetailResult) => void;
  highlights: AttentionHighlights;
  onReviewSuccess?: () => void;
}

export function WorkControlsGroup({ requestId, detail, onDetailUpdated, highlights, onReviewSuccess }: WorkControlsGroupProps) {
  const hasFeedback =
    detail.availableActions.canMarkFeedbackReviewed &&
    detail.feedbackWasResolved === false &&
    detail.feedbackReviewedAtUtc == null;

  if (!hasFeedback) return null;

  const cardHighlight = maxHighlight(undefined, highlights.feedbackReview);
  const shadow = highlightBoxShadow(cardHighlight);

  return (
    <div
      id="work-controls"
      className={`rounded-xl border overflow-hidden scroll-mt-4 transition-[border-color,box-shadow] bg-[var(--ophalo-card)] ${highlightBorderCls(cardHighlight)}`}
      style={shadow ? { boxShadow: shadow } : undefined}
    >
      <div className="px-5 py-4">
        <div className="mb-2 flex justify-end">
          <RecommendedActionBadge level={highlights.feedbackReview} />
        </div>
        <FeedbackReviewSection requestId={requestId} detail={detail} onDetailUpdated={onDetailUpdated} onReviewSuccess={onReviewSuccess} />
      </div>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Prominent feedback card — main column, shown when opened from Feedback Review
// ---------------------------------------------------------------------------

interface ProminentFeedbackCardProps {
  requestId: string;
  detail: KeepRequestDetailResult;
  onDetailUpdated: (updated: KeepRequestDetailResult) => void;
  onReviewSuccess: () => void;
}

export function ProminentFeedbackCard({ requestId, detail, onDetailUpdated, onReviewSuccess }: ProminentFeedbackCardProps) {
  const isUnreviewedNegative =
    detail.availableActions.canMarkFeedbackReviewed &&
    detail.feedbackWasResolved === false &&
    detail.feedbackReviewedAtUtc == null;

  if (!isUnreviewedNegative) return null;

  return (
    <div
      id="focus-panel-feedback_review"
      className="rounded-xl border border-[var(--ophalo-attention)] bg-[var(--ophalo-attention-bg)] px-5 py-4 scroll-mt-4 space-y-3"
    >
      <div className="flex items-start justify-between gap-2">
        <p className="text-sm font-semibold text-[var(--ophalo-ink)]">Customer feedback</p>
        <KeepBadge variant="attention">Needs review</KeepBadge>
      </div>
      <p className="text-xs text-[var(--ophalo-muted)]">
        Customer reported their request was <strong>not resolved</strong>
        {detail.feedbackSubmittedAtUtc ? ` on ${formatDate(detail.feedbackSubmittedAtUtc)}` : ""}.
      </p>
      {detail.feedbackCommentVisible && detail.feedbackComment && (
        <p className="text-sm text-[var(--ophalo-ink)] italic">&ldquo;{detail.feedbackComment}&rdquo;</p>
      )}
      <FeedbackReviewSection requestId={requestId} detail={detail} onDetailUpdated={onDetailUpdated} onReviewSuccess={onReviewSuccess} />
    </div>
  );
}

// ---------------------------------------------------------------------------
// Original request card — customer description
// ---------------------------------------------------------------------------

export function OriginalRequestCard({ detail }: { detail: KeepRequestDetailResult }) {
  if (!detail.description) return null;
  return (
    <div className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-4 py-2.5">
      <p className="text-xs font-semibold uppercase tracking-wide text-[var(--ophalo-muted)] mb-0.5">
        Customer description
      </p>
      <p className="text-sm leading-6 text-[var(--ophalo-ink)] whitespace-pre-wrap">
        {detail.description}
      </p>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Related work — compact same-customer continuity indicator (GAP-050)
// ---------------------------------------------------------------------------

interface RelatedWorkPanelProps {
  requestId: string;
  onNavigate?: (id: string) => void;
  bare?: boolean;
}

export function RelatedWorkPanel({ requestId, onNavigate, bare = false }: RelatedWorkPanelProps) {
  const { data } = useQuery({
    queryKey: ["request-related-work", requestId],
    queryFn: () => api.getRelatedWork(requestId),
    enabled: Boolean(onNavigate),
  });

  if (!onNavigate || !data || data.totalCount === 0) return null;

  return (
    <div className={bare ? "px-4 py-3" : "rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-5 py-4"}>
      <p className="text-xs font-semibold uppercase tracking-wide text-[var(--ophalo-muted)] mb-2">
        Related work for this customer ({data.totalCount})
      </p>
      <ul className="space-y-1.5">
        {data.items.map((item) => (
          <li key={item.requestId}>
            <button
              type="button"
              onClick={() => onNavigate(item.requestId)}
              className={`w-full flex items-center justify-between gap-3 rounded-lg px-2 py-1.5 text-left text-sm hover:bg-[var(--ophalo-canvas)] ${FOCUS_RING}`}
            >
              <span className="text-[var(--ophalo-ink)]">{item.referenceCode}</span>
              <span className="flex items-center gap-2 text-xs text-[var(--ophalo-muted)]">
                <KeepBadge variant={statusBadgeVariant(item.status)}>{statusLabel(item.status)}</KeepBadge>
                {formatDate(item.lastActivityAtUtc)}
              </span>
            </button>
          </li>
        ))}
      </ul>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Customer panel — phone, email, copy, and contact-launch affordances
// ---------------------------------------------------------------------------

interface CustomerPanelProps {
  detail: KeepRequestDetailResult;
  onContactLaunched: (direction: string, channel: string) => void;
}

export function CustomerPanel({ detail, onContactLaunched }: CustomerPanelProps) {
  const { copiedId: copiedContactId, failedId: copyContactFailedId, copy: copyContact } = useCopyFeedback();
  const callAction = detail.contactActions.find((a) => a.available && a.type === "call");
  const emailAction = detail.contactActions.find((a) => a.available && a.type !== "call");
  const publicBaseUrl = (import.meta.env.VITE_PUBLIC_BASE_URL as string).replace(/\/$/, "");
  const customerPageUrl = detail.pageToken ? `${publicBaseUrl}/keep/r/${detail.pageToken}` : null;
  const canLogContact = detail.availableActions.canLogExternalContact;
  const hasContact = !!(detail.customerPhone || detail.customerEmail);

  if (!hasContact && !canLogContact) return null;

  return (
    <div>
      <p className="px-1 text-xs font-semibold uppercase tracking-widest text-[var(--ophalo-muted)] mb-2">Customer</p>
      <div className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-4 py-3 space-y-3">
        {detail.customerPhone && (
          <div>
            <p className="text-xs font-semibold uppercase tracking-wide text-[var(--ophalo-muted)] mb-1">Phone</p>
            <div className="flex items-center gap-2 flex-wrap">
              <Phone className="h-3.5 w-3.5 shrink-0 text-[var(--ophalo-muted)]" />
              <span className="text-sm text-[var(--ophalo-ink)]">{formatNaPhone(detail.customerPhone)}</span>
              <button
                type="button"
                onClick={() => void copyContact(detail.customerPhone!, "phone")}
                aria-label={
                  copiedContactId === "phone"
                    ? "Phone number copied"
                    : copyContactFailedId === "phone"
                      ? "Couldn't copy phone number, try again"
                      : "Copy phone number"
                }
                className={`text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] transition-colors ${FOCUS_RING} rounded`}
              >
                {copiedContactId === "phone"
                  ? <Check className="h-3.5 w-3.5 text-green-600" />
                  : copyContactFailedId === "phone"
                    ? <AlertTriangle className="h-3.5 w-3.5 text-[var(--ophalo-attention)]" />
                    : <Copy className="h-3.5 w-3.5" />}
              </button>
              {copyContactFailedId === "phone" && (
                <span role="status" aria-live="polite" className="text-xs text-[var(--ophalo-attention)]">
                  Couldn't copy — try again.
                </span>
              )}
              {/* Mobile only — desktop uses CustomerContactStrip QR handoff (ADR-443) */}
              {callAction && (
                <a
                  href={`tel:${callAction.target}`}
                  onClick={() => onContactLaunched("outbound", "phone")}
                  className={`inline-flex md:hidden items-center gap-1 text-xs font-semibold text-[var(--keep-accent)] hover:underline ${FOCUS_RING} rounded`}
                >
                  <Phone className="h-3 w-3" />
                  Call
                </a>
              )}
            </div>
          </div>
        )}
        {detail.customerEmail && (
          <div>
            <p className="text-xs font-semibold uppercase tracking-wide text-[var(--ophalo-muted)] mb-1">Email</p>
            <div className="flex items-center gap-2 flex-wrap">
              <Mail className="h-3.5 w-3.5 shrink-0 text-[var(--ophalo-muted)]" />
              <span className="text-sm text-[var(--ophalo-ink)] break-all">{detail.customerEmail}</span>
              <button
                type="button"
                onClick={() => void copyContact(detail.customerEmail!, "email")}
                aria-label={
                  copiedContactId === "email"
                    ? "Email address copied"
                    : copyContactFailedId === "email"
                      ? "Couldn't copy email address, try again"
                      : "Copy email address"
                }
                className={`text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] transition-colors ${FOCUS_RING} rounded`}
              >
                {copiedContactId === "email"
                  ? <Check className="h-3.5 w-3.5 text-green-600" />
                  : copyContactFailedId === "email"
                    ? <AlertTriangle className="h-3.5 w-3.5 text-[var(--ophalo-attention)]" />
                    : <Copy className="h-3.5 w-3.5" />}
              </button>
              {copyContactFailedId === "email" && (
                <span role="status" aria-live="polite" className="text-xs text-[var(--ophalo-attention)]">
                  Couldn't copy — try again.
                </span>
              )}
              {emailAction && (
                <a
                  href={(() => {
                    const subject = encodeURIComponent("Your request page link");
                    const body = customerPageUrl
                      ? encodeURIComponent(`Here is a link to your private request page:\n\n${customerPageUrl}`)
                      : "";
                    return `mailto:${emailAction.target}?subject=${subject}${body ? `&body=${body}` : ""}`;
                  })()}
                  onClick={() => onContactLaunched("outbound", "email")}
                  className={`inline-flex items-center gap-1 text-xs font-semibold text-[var(--keep-accent)] hover:underline ${FOCUS_RING} rounded`}
                >
                  <Mail className="h-3 w-3" />
                  Email
                </a>
              )}
            </div>
          </div>
        )}
        {canLogContact && (
          <button
            type="button"
            onClick={() => onContactLaunched(
              "outbound",
              detail.customerPhone ? "phone" : detail.customerEmail ? "email" : "other"
            )}
            className={`w-full text-left text-xs font-semibold text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] transition-colors pt-2 border-t border-[var(--ophalo-border)] ${FOCUS_RING} rounded`}
          >
            Log external contact
          </button>
        )}
      </div>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Service location panel — controller opens the edit modal via onEditLocation
// ---------------------------------------------------------------------------

interface ServiceLocationPanelProps {
  detail: KeepRequestDetailResult;
  onEditLocation: () => void;
}

export function ServiceLocationPanel({ detail, onEditLocation }: ServiceLocationPanelProps) {
  const canEdit = detail.availableActions.canAddInternalNote;
  const hasAddress = !!(detail.serviceAddressLine1 || detail.serviceCity);
  const addressLine = [
    detail.serviceAddressLine1,
    detail.serviceAddressLine2,
    detail.serviceCity && detail.serviceState
      ? `${detail.serviceCity}, ${detail.serviceState}${detail.serviceZip ? ` ${detail.serviceZip}` : ""}`
      : null,
  ].filter(Boolean).join(", ");

  // Inline Anchor context item (three-row correction, 2026-08-22) — no independent card
  // border/padding/background; the Anchor owns the one boundary for the whole strip.
  return (
    <div className="flex flex-col gap-1">
      <span className="text-xs font-semibold uppercase tracking-widest text-[var(--ophalo-muted)] shrink-0">
        Service location
      </span>
      <div className="flex items-center gap-2">
        {hasAddress ? (
          <span className="text-sm text-[var(--ophalo-ink)]">{addressLine}</span>
        ) : (
          <span className="inline-flex items-center gap-1 text-sm font-medium text-[var(--ophalo-attention)]">
            <AlertTriangle className="h-3.5 w-3.5 shrink-0" />
            Not on file
          </span>
        )}
        {canEdit && (
          <button
            type="button"
            onClick={onEditLocation}
            className={`text-xs font-semibold text-[var(--keep-accent)] hover:underline ${FOCUS_RING} rounded`}
          >
            {hasAddress ? "Edit" : "Add"}
          </button>
        )}
      </div>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Customer signal panel — record-details-only surface for intake urgency/contact preference
// ---------------------------------------------------------------------------

interface CustomerSignalPanelProps {
  detail: KeepRequestDetailResult;
  bare?: boolean;
}

export function CustomerSignalPanel({ detail, bare = false }: CustomerSignalPanelProps) {
  const hasCustomerSignal = detail.source === "public_intake" &&
    !!(detail.intakeUrgency || detail.contactPreference);
  if (!hasCustomerSignal) return null;

  return (
    <div className={bare ? "px-4 py-3" : "rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-4 py-3"}>
      <p className="text-xs font-semibold uppercase tracking-wide text-[var(--ophalo-muted)] mb-1">Customer signal</p>
      <div className="flex flex-wrap gap-1.5 mb-1.5">
        {detail.intakeUrgency === "urgent" && <KeepBadge variant="attention">Customer marked urgent</KeepBadge>}
        {detail.intakeUrgency === "soon" && <KeepBadge variant="default">Customer asked for soon follow-up</KeepBadge>}
        {detail.contactPreference === "phone_call" && <KeepBadge variant="default">Prefers call</KeepBadge>}
        {detail.contactPreference === "text_message" && <KeepBadge variant="default">Prefers text</KeepBadge>}
        {detail.contactPreference === "email" && <KeepBadge variant="default">Prefers email</KeepBadge>}
        {detail.contactPreference === "no_preference" && <KeepBadge variant="default">No preference</KeepBadge>}
      </div>
      <p className="text-xs text-[var(--ophalo-muted)]">
        Review the request, then update the customer or log contact if needed.
      </p>
    </div>
  );
}

// Triage panel — internal priority only (customer signal moved to CustomerSignalPanel,
// Record details; locked exception 2026-08-22 moved this into the Communication & Planning
// planning row, alongside Follow Up On / Planned For)
// ---------------------------------------------------------------------------

interface TriagePanelProps {
  detail: KeepRequestDetailResult;
  onDetailUpdated: (updated: KeepRequestDetailResult) => void;
  // bare: no outer card chrome/label — used when a parent renders this as one tile of the
  // shared Communication & Planning planning row (locked exception, 2026-08-22).
  bare?: boolean;
}

const PRIORITY_CONFLICT_MESSAGE =
  "This request was updated by another team member. Refresh to see the latest priority.";

export function TriagePanel({ detail, onDetailUpdated, bare = false }: TriagePanelProps) {
  const [pendingPriority, setPendingPriority] = useState<string | null | undefined>(undefined);
  const [prioritySubmitting, setPrioritySubmitting] = useState(false);
  const [priorityConflictDisabled, setPriorityConflictDisabled] = useState(false);
  const [priorityError, setPriorityError] = useState<string | null>(null);
  const canEdit = detail.availableActions.canAddInternalNote;
  const displayPriority = pendingPriority !== undefined ? pendingPriority : detail.businessPriority;

  const content = (
    <>
      {canEdit ? (
            <>
              <select
                value={displayPriority ?? ""}
                disabled={prioritySubmitting || priorityConflictDisabled}
                onChange={async (e) => {
                  if (prioritySubmitting || priorityConflictDisabled) return;
                  const val = e.target.value || null;
                  setPendingPriority(val);
                  setPrioritySubmitting(true);
                  setPriorityError(null);
                  try {
                    const updated = await api.setBusinessPriority(detail.requestId, val, detail.version);
                    onDetailUpdated(updated);
                  } catch (err) {
                    if (err instanceof ApiError && err.status === 409) {
                      setPriorityConflictDisabled(true);
                      setPriorityError(PRIORITY_CONFLICT_MESSAGE);
                    } else {
                      setPriorityError("Could not save priority. Try again.");
                    }
                  } finally {
                    setPendingPriority(undefined);
                    setPrioritySubmitting(false);
                  }
                }}
                className="w-full text-sm text-[var(--ophalo-ink)] bg-transparent border border-[var(--ophalo-border)] rounded px-2 py-1.5 focus:outline-none focus:ring-1 focus:ring-[var(--keep-accent)] disabled:opacity-60 disabled:cursor-not-allowed"
              >
                <option value="">Not set</option>
                <option value="routine">Routine</option>
                <option value="soon">Soon</option>
                <option value="urgent">Urgent</option>
              </select>
              {priorityError ? (
                <p className="text-xs text-[var(--ophalo-danger)] mt-1" role="alert">
                  {priorityError}
                </p>
              ) : !displayPriority ? (
                <p className="text-xs text-[var(--ophalo-muted)] mt-1">
                  Set priority to handle this ahead of routine work.
                </p>
      ) : null}
        </>
      ) : (
        <span className="text-sm font-semibold text-[var(--ophalo-ink)]">
          {detail.businessPriority === "urgent" && "Team marked urgent"}
          {detail.businessPriority === "soon" && "Team marked soon"}
          {detail.businessPriority === "routine" && "Routine"}
          {!detail.businessPriority && <span className="text-[var(--ophalo-muted)]">Not set</span>}
        </span>
      )}
    </>
  );

  if (bare) {
    return (
      <div className="space-y-2">
        <p className="text-xs text-[var(--ophalo-muted)]">Internal priority</p>
        {content}
      </div>
    );
  }

  return (
    <div>
      <p className="px-1 text-xs font-semibold uppercase tracking-widest text-[var(--ophalo-muted)] mb-2">Triage</p>
      <div className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-4 py-3 space-y-1">
        <p className="text-xs font-semibold uppercase tracking-wide text-[var(--ophalo-muted)]">Internal priority</p>
        {content}
      </div>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Source metadata panel
// ---------------------------------------------------------------------------

export function SourceMetaPanel({ detail, bare = false }: { detail: KeepRequestDetailResult; bare?: boolean }) {
  return (
    <div className={bare ? "px-4 py-3 space-y-0.5" : "px-1 space-y-0.5"}>
      <p className="text-xs text-[var(--ophalo-muted)]">
        Source: {detail.source === "public_intake" ? "Customer intake form" : "Team added"}
      </p>
      <p className="text-xs text-[var(--ophalo-muted)]">
        Submitted {formatDate(detail.createdAtUtc)}
      </p>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Hero attention banner — one quiet amber surface composing the active
// guidance (why / verbatim source / resolve-by) with the single server-routed
// next-step destination. Replaces the former AttentionGuidanceCard +
// NextStepCard pair (locked spec, presentation pass item 4); routing logic is
// unchanged. acknowledge_attention and log_external_contact/resolve_follow_up
// route to the locked ResponsiveSheet workflows (step 4); respond_to_customer
// still scrolls to the always-open composer within the Work Canvas pending
// the collapsed-composer migration.
// ---------------------------------------------------------------------------

interface HeroAttentionBannerProps {
  detail: KeepRequestDetailResult;
  onRecordFollowUp: () => void;
  onContactLaunched: (direction: string, channel: string) => void;
  onOpenClearAttention: () => void;
}

function scrollAndFocusWithinWorkCanvas(id: string) {
  const target = document.getElementById(id);
  if (!target) return;

  const canvas = target.closest<HTMLElement>("[data-request-detail-work-canvas]");
  if (canvas) {
    const canvasRect = canvas.getBoundingClientRect();
    const targetRect = target.getBoundingClientRect();
    canvas.scrollTo({
      top: Math.max(0, canvas.scrollTop + targetRect.top - canvasRect.top - 24),
      behavior: "smooth",
    });
  }

  target.focus({ preventScroll: true });
}

function resolveNextStep(
  detail: KeepRequestDetailResult,
  callbacks: Pick<HeroAttentionBannerProps, "onRecordFollowUp" | "onContactLaunched" | "onOpenClearAttention">,
): { buttonLabel: string; onActivate: () => void } | null {
  const { guidanceKey } = detail.effectiveAttention;
  const { canAcknowledgeAttention, canSetFollowUpOn, canSendBusinessUpdate, canLogExternalContact } =
    detail.availableActions;
  const { onRecordFollowUp, onContactLaunched, onOpenClearAttention } = callbacks;

  switch (guidanceKey) {
    case "acknowledge_attention":
      if (!canAcknowledgeAttention) return null;
      return { buttonLabel: "Go to Clear attention", onActivate: onOpenClearAttention };
    case "resolve_follow_up":
      if (!canSetFollowUpOn) return null;
      return { buttonLabel: "Resolve follow-up", onActivate: onRecordFollowUp };
    case "respond_to_customer":
      // Two authorized resolution routes exist for this key — a customer update and an
      // external contact log. Only render no CTA when neither is authorized.
      if (canSendBusinessUpdate) {
        return {
          buttonLabel: "Respond to customer",
          onActivate: () => scrollAndFocusWithinWorkCanvas("focus-panel-update"),
        };
      }
      if (canLogExternalContact) {
        const contactChannel = detail.customerPhone ? "phone" : detail.customerEmail ? "email" : "other";
        return { buttonLabel: "Log contact", onActivate: () => onContactLaunched("outbound", contactChannel) };
      }
      return null;
    case "log_external_contact": {
      if (!canLogExternalContact) return null;
      const contactChannel = detail.customerPhone ? "phone" : detail.customerEmail ? "email" : "other";
      return { buttonLabel: "Log contact", onActivate: () => onContactLaunched("outbound", contactChannel) };
    }
    default:
      return null;
  }
}

export function HeroAttentionBanner({ detail, onRecordFollowUp, onContactLaunched, onOpenClearAttention }: HeroAttentionBannerProps) {
  const guidance = buildAttentionGuidance(detail);
  if (!guidance) return null;

  const isOverdue = detail.effectiveAttention.level === "overdue";
  const nextStep = resolveNextStep(detail, { onRecordFollowUp, onContactLaunched, onOpenClearAttention });

  return (
    <section className="rounded-xl border border-[var(--ophalo-border)] border-l-4 border-l-[var(--ophalo-attention)] bg-[var(--ophalo-attention-bg)] px-5 py-4">
      <div className="flex flex-wrap items-center gap-2 mb-3">
        <KeepBadge variant={isOverdue ? "danger" : "attention"}>
          {isOverdue ? (
            <AlertTriangle className="h-3 w-3 mr-1 shrink-0" />
          ) : (
            <Clock className="h-3 w-3 mr-1 shrink-0" />
          )}
          Needs attention
        </KeepBadge>
        <span className="text-sm font-semibold text-[var(--ophalo-ink)]">{guidance.label}</span>
      </div>

      <div className="space-y-3">
        <div>
          <p className="text-xs font-semibold uppercase tracking-wide text-[var(--ophalo-attention)]">
            Why
          </p>
          <p className="mt-1 text-sm leading-6 text-[var(--ophalo-ink)]">{guidance.why}</p>
        </div>

        {guidance.sourceText && (
          <div className="rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-3 py-2.5">
            {guidance.sourceLabel && (
              <p className="text-xs font-semibold text-[var(--ophalo-muted)] mb-1">
                {guidance.sourceLabel}
              </p>
            )}
            <p className="text-sm leading-6 text-[var(--ophalo-ink)] italic">
              "{guidance.sourceText}"
            </p>
          </div>
        )}

        <div>
          <p className="text-xs font-semibold uppercase tracking-wide text-[var(--ophalo-attention)]">
            Resolve by
          </p>
          <p className="mt-1 text-sm leading-6 text-[var(--ophalo-ink)]">{guidance.resolveBy}</p>
          {guidance.afterHandled && (
            <p className="mt-1 text-xs leading-5 text-[var(--ophalo-muted)]">
              {guidance.afterHandled}
            </p>
          )}
        </div>
      </div>

      {nextStep && (
        <div className="mt-4 pt-3 border-t border-[var(--ophalo-attention)]/25 flex flex-wrap items-center justify-between gap-3">
          <div>
            <p className="text-xs font-semibold uppercase tracking-wide text-[var(--ophalo-muted)]">
              Next step
            </p>
            <p className="mt-1 text-sm text-[var(--ophalo-ink)]">{nextStep.buttonLabel}</p>
          </div>
          <KeepButton onClick={nextStep.onActivate}>{nextStep.buttonLabel}</KeepButton>
        </div>
      )}
    </section>
  );
}
