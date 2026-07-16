import { useState } from "react";
import { Copy, Check, AlertTriangle, Clock, Phone, Mail } from "lucide-react";
import {
  api,
  ApiError,
  type KeepRequestDetailResult,
} from "../../lib/apiClient";
import { KeepButton } from "../../components/keep/KeepButton";
import { KeepBadge, type KeepBadgeVariant } from "../../components/keep/KeepBadge";
import {
  FOCUS_RING,
  INPUT_CLS,
  STATUS_CONFLICT_MESSAGE,
  formatDate,
  buildAttentionGuidance,
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

interface MarkHandledCardProps {
  requestId: string;
  detail: KeepRequestDetailResult;
  onDetailUpdated: (updated: KeepRequestDetailResult) => void;
  highlight?: HighlightLevel;
}

export function MarkHandledCard({ requestId, detail, onDetailUpdated, highlight }: MarkHandledCardProps) {
  const { canAcknowledgeAttention } = detail.availableActions;
  const hasAttention = detail.attentionLevel !== "none" && !!detail.attentionReason;
  const { acknowledgeReasonMaxLength } = detail.validation;

  const [reason, setReason] = useState("");
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [conflictDisabled, setConflictDisabled] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (!canAcknowledgeAttention || !hasAttention) return null;

  const canSubmit = reason.trim().length > 0 && !isSubmitting && !conflictDisabled;

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!canSubmit) return;
    setIsSubmitting(true);
    setError(null);
    try {
      const updated = await api.acknowledgeAttention(requestId, reason.trim(), detail.version);
      onDetailUpdated(updated);
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

  const shadow = highlightBoxShadow(highlight);

  return (
    <div
      className={`rounded-xl border px-5 py-4 transition-[border-color,background-color,box-shadow] ${highlightBorderCls(highlight)} ${highlightBgCls()}`}
      style={shadow ? { boxShadow: shadow } : undefined}
    >
      <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
        <p className="text-sm font-semibold text-[var(--ophalo-ink)]">Clear attention</p>
        <RecommendedActionBadge level={highlight} />
      </div>
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
      <form onSubmit={(e) => void handleSubmit(e)} className="space-y-2.5">
        <div>
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
            rows={2}
            className={`${INPUT_CLS} resize-none`}
          />
        </div>
        <KeepButton type="submit" variant="secondary" disabled={!canSubmit} className="w-full">
          {isSubmitting ? "Clearing…" : "Clear attention"}
        </KeepButton>
      </form>
    </div>
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

export function FeedbackSummaryCard({ detail }: { detail: KeepRequestDetailResult }) {
  if (detail.feedbackWasResolved !== true || !detail.feedbackSubmittedAtUtc) return null;

  return (
    <div className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-5 py-4">
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
    <div className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-5 py-4">
      <p className="text-xs font-semibold uppercase tracking-wide text-[var(--ophalo-muted)] mb-1">
        Customer description
      </p>
      <p className="text-sm leading-6 text-[var(--ophalo-ink)] whitespace-pre-wrap">
        {detail.description}
      </p>
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
  const [copiedPhone, setCopiedPhone] = useState(false);
  const [copiedEmail, setCopiedEmail] = useState(false);
  const callAction = detail.contactActions.find((a) => a.available && a.type === "call");
  const emailAction = detail.contactActions.find((a) => a.available && a.type !== "call");
  const publicBaseUrl = (import.meta.env.VITE_PUBLIC_BASE_URL as string).replace(/\/$/, "");
  const customerPageUrl = detail.pageToken ? `${publicBaseUrl}/keep/r/${detail.pageToken}` : null;
  const canLogContact = detail.availableActions.canLogExternalContact;
  const hasContact = !!(detail.customerPhone || detail.customerEmail);

  if (!hasContact && !canLogContact) return null;

  function copyToClipboard(text: string, setDone: (v: boolean) => void) {
    navigator.clipboard.writeText(text).then(() => {
      setDone(true);
      setTimeout(() => setDone(false), 2000);
    });
  }

  return (
    <div>
      <p className="px-1 text-xs font-semibold uppercase tracking-widest text-[var(--ophalo-muted)] mb-2">Customer</p>
      <div className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-4 py-3 space-y-3">
        {detail.customerPhone && (
          <div>
            <p className="text-xs font-semibold uppercase tracking-wide text-[var(--ophalo-muted)] mb-1">Phone</p>
            <div className="flex items-center gap-2 flex-wrap">
              <Phone className="h-3.5 w-3.5 shrink-0 text-[var(--ophalo-muted)]" />
              <span className="text-sm text-[var(--ophalo-ink)]">{detail.customerPhone}</span>
              <button
                type="button"
                onClick={() => copyToClipboard(detail.customerPhone!, setCopiedPhone)}
                aria-label="Copy phone number"
                className={`text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] transition-colors ${FOCUS_RING} rounded`}
              >
                {copiedPhone ? <Check className="h-3.5 w-3.5 text-green-600" /> : <Copy className="h-3.5 w-3.5" />}
              </button>
              {callAction && (
                <a
                  href={`tel:${callAction.target}`}
                  onClick={() => onContactLaunched("outbound", "phone")}
                  className={`inline-flex items-center gap-1 text-xs font-semibold text-[var(--keep-accent)] hover:underline ${FOCUS_RING} rounded`}
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
                onClick={() => copyToClipboard(detail.customerEmail!, setCopiedEmail)}
                aria-label="Copy email address"
                className={`text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] transition-colors ${FOCUS_RING} rounded`}
              >
                {copiedEmail ? <Check className="h-3.5 w-3.5 text-green-600" /> : <Copy className="h-3.5 w-3.5" />}
              </button>
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

  return (
    <div>
      <div className="flex items-center justify-between px-1 mb-2">
        <p className="text-xs font-semibold uppercase tracking-widest text-[var(--ophalo-muted)]">Service Location</p>
        {canEdit && hasAddress && (
          <button
            type="button"
            onClick={onEditLocation}
            className={`text-xs text-[var(--keep-accent)] hover:underline ${FOCUS_RING} rounded`}
          >
            Edit
          </button>
        )}
      </div>
      {hasAddress ? (
        <div className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-4 py-3">
          {detail.serviceAddressLine1 && (
            <p className="text-sm font-semibold text-[var(--ophalo-ink)]">{detail.serviceAddressLine1}</p>
          )}
          {detail.serviceAddressLine2 && (
            <p className="text-sm text-[var(--ophalo-ink)]">{detail.serviceAddressLine2}</p>
          )}
          {detail.serviceCity && detail.serviceState && (
            <p className="text-sm text-[var(--ophalo-ink)]">
              {detail.serviceCity}, {detail.serviceState}{detail.serviceZip ? ` ${detail.serviceZip}` : ""}
            </p>
          )}
        </div>
      ) : (
        <div className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-[var(--ophalo-attention)] bg-[var(--ophalo-attention-bg)] px-4 py-3">
          <div className="flex min-w-0 items-start gap-2">
            <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0 text-[var(--ophalo-attention)]" />
            <div>
              <p className="text-xs font-semibold text-[var(--ophalo-ink)]">Service location needed</p>
              <p className="text-xs leading-5 text-[var(--ophalo-muted)]">
                Add the address before dispatching or scheduling field work.
              </p>
            </div>
          </div>
          {canEdit && (
            <button
              type="button"
              onClick={onEditLocation}
              className={`inline-flex min-h-[32px] shrink-0 items-center rounded-lg border border-[var(--ophalo-attention)] bg-[var(--ophalo-card)] px-3 text-xs font-semibold text-[var(--ophalo-ink)] hover:bg-white transition-colors ${FOCUS_RING}`}
            >
              Add location
            </button>
          )}
        </div>
      )}
    </div>
  );
}

// ---------------------------------------------------------------------------
// Triage panel — customer signal and internal priority
// ---------------------------------------------------------------------------

interface TriagePanelProps {
  detail: KeepRequestDetailResult;
  onDetailUpdated: (updated: KeepRequestDetailResult) => void;
}

export function TriagePanel({ detail, onDetailUpdated }: TriagePanelProps) {
  const [pendingPriority, setPendingPriority] = useState<string | null | undefined>(undefined);
  const canEdit = detail.availableActions.canAddInternalNote;
  const displayPriority = pendingPriority !== undefined ? pendingPriority : detail.businessPriority;
  const hasCustomerSignal = detail.source === "public_intake" &&
    !!(detail.intakeUrgency || detail.contactPreference);

  return (
    <div>
      <p className="px-1 text-xs font-semibold uppercase tracking-widest text-[var(--ophalo-muted)] mb-2">Triage</p>
      <div className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-4 py-3 space-y-3">
        {hasCustomerSignal && (
          <div>
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
        )}
        <div>
          <p className="text-xs font-semibold uppercase tracking-wide text-[var(--ophalo-muted)] mb-1">Internal priority</p>
          {canEdit ? (
            <>
              <select
                value={displayPriority ?? ""}
                onChange={async (e) => {
                  const val = e.target.value || null;
                  setPendingPriority(val);
                  try {
                    const updated = await api.setBusinessPriority(detail.requestId, val, detail.version);
                    onDetailUpdated(updated);
                  } catch {
                    // revert optimistic on error
                  } finally {
                    setPendingPriority(undefined);
                  }
                }}
                className="text-xs text-[var(--ophalo-ink)] bg-transparent border border-[var(--ophalo-border)] rounded px-2 py-0.5 focus:outline-none focus:ring-1 focus:ring-[var(--keep-accent)]"
              >
                <option value="">Not set</option>
                <option value="routine">Routine</option>
                <option value="soon">Soon</option>
                <option value="urgent">Urgent</option>
              </select>
              {!displayPriority && (
                <p className="text-xs text-[var(--ophalo-muted)] mt-1">
                  Set priority to handle this ahead of routine work.
                </p>
              )}
            </>
          ) : (
            <span className="text-sm font-semibold text-[var(--ophalo-ink)]">
              {detail.businessPriority === "urgent" && "Team marked urgent"}
              {detail.businessPriority === "soon" && "Team marked soon"}
              {detail.businessPriority === "routine" && "Routine"}
              {!detail.businessPriority && <span className="text-[var(--ophalo-muted)]">Not set</span>}
            </span>
          )}
        </div>
      </div>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Source metadata panel
// ---------------------------------------------------------------------------

export function SourceMetaPanel({ detail }: { detail: KeepRequestDetailResult }) {
  return (
    <div className="px-1 space-y-0.5">
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
// Needs attention guidance card
// ---------------------------------------------------------------------------

interface AttentionGuidanceCardProps {
  detail: KeepRequestDetailResult;
  highlights: AttentionHighlights;
}

export function AttentionGuidanceCard({ detail, highlights }: AttentionGuidanceCardProps) {
  const guidance = buildAttentionGuidance(detail);
  if (!guidance) return null;

  const isOverdue = detail.attentionLevel === "overdue";

  const primaryPanelLabel: string | null = (() => {
    if (highlights.sendUpdate === "primary") return "Send customer update";
    if (highlights.logContact === "primary") return "Log external contact";
    if (highlights.feedbackReview === "primary") return "Review feedback";
    if (highlights.markHandled === "primary") return "Clear attention";
    return null;
  })();

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

        {primaryPanelLabel && (
          <div>
            <p className="text-xs font-semibold uppercase tracking-wide text-[var(--ophalo-attention)]">
              Recommended next step
            </p>
            <p className="mt-1 text-sm text-[var(--ophalo-ink)]">
              {highlights.sendUpdate === "primary"
                ? "Recommended: Send customer update using the highlighted panel on the right."
                : highlights.logContact === "primary"
                  ? "Recommended: Log external contact using the highlighted panel on the right."
                  : "Use the highlighted panel on the right."}
            </p>
          </div>
        )}
      </div>
    </section>
  );
}
