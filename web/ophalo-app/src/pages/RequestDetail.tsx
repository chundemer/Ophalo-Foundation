import { useState, useMemo, useRef, useEffect } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import {
  ChevronLeft,
  ChevronRight,
  Copy,
  Check,
  AlertTriangle,
  Clock,
  Phone,
  Mail,
  X,
} from "lucide-react";
import {
  api,
  ApiError,
  type KeepRequestDetailResult,
  type UpdateServiceLocationBody,
} from "../lib/apiClient";
import { NeedsShareBanner } from "../components/NeedsShareBanner";
import { ShareLinkModal } from "../components/ShareLinkModal";
import { KeepButton } from "../components/keep/KeepButton";
import { KeepBadge, type KeepBadgeVariant } from "../components/keep/KeepBadge";
import { ExternalContactForm } from "../components/ExternalContactForm";
import {
  FOCUS_RING,
  INPUT_CLS,
  STATUS_CONFLICT_MESSAGE,
  ALWAYS_HIDDEN_EVENT_TYPES,
  formatDate,
  buildAttentionGuidance,
} from "./request-detail/helpers";
import {
  type HighlightLevel,
  type AttentionHighlights,
  getAttentionResolutionHighlights,
  highlightBorderCls,
  highlightBgCls,
  highlightBoxShadow,
  RecommendedActionBadge,
  maxHighlight,
} from "./request-detail/highlights";
import { type TimelineFilter, isCommunicationEvent, TimelineEvent } from "./request-detail/TimelineEvent";
import { TimingPanel } from "./request-detail/TimingPanel";
import { TodayPromiseBanner, DetailHero } from "./request-detail/DetailHero";
import { TeamSection } from "./request-detail/TeamSection";
import { WorkDoneCard, CloseRequestCard, BusinessUpdateSection } from "./request-detail/BusinessSection";

// ---------------------------------------------------------------------------
// Handled outside Keep? — log contact affordance card
// ---------------------------------------------------------------------------

interface LogContactCardProps {
  detail: KeepRequestDetailResult;
  onContactLaunched: (direction: string, channel: string) => void;
  highlight?: HighlightLevel;
}

function LogContactCard({ detail, onContactLaunched, highlight }: LogContactCardProps) {
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
// Clear attention — acknowledge stale attention without logging contact
// ---------------------------------------------------------------------------

interface MarkHandledCardProps {
  requestId: string;
  detail: KeepRequestDetailResult;
  onDetailUpdated: (updated: KeepRequestDetailResult) => void;
  highlight?: HighlightLevel;
}

function MarkHandledCard({ requestId, detail, onDetailUpdated, highlight }: MarkHandledCardProps) {
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
// Feedback review
// ---------------------------------------------------------------------------

interface FeedbackReviewSectionProps {
  requestId: string;
  detail: KeepRequestDetailResult;
  onDetailUpdated: (updated: KeepRequestDetailResult) => void;
}

function FeedbackReviewSection({
  requestId,
  detail,
  onDetailUpdated,
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
// Work controls group — focused review controls
// ---------------------------------------------------------------------------

interface WorkControlsGroupProps {
  requestId: string;
  detail: KeepRequestDetailResult;
  onDetailUpdated: (updated: KeepRequestDetailResult) => void;
  highlights: AttentionHighlights;
}

function WorkControlsGroup({ requestId, detail, onDetailUpdated, highlights }: WorkControlsGroupProps) {
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
        <FeedbackReviewSection requestId={requestId} detail={detail} onDetailUpdated={onDetailUpdated} />
      </div>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Internal note composer — side panel, never customer-visible
// ---------------------------------------------------------------------------

interface InternalNoteCardProps {
  requestId: string;
  detail: KeepRequestDetailResult;
  onDetailUpdated: (updated: KeepRequestDetailResult) => void;
}

function InternalNoteCard({ requestId, detail, onDetailUpdated }: InternalNoteCardProps) {
  const { canAddInternalNote } = detail.availableActions;
  const { internalNoteMaxLength } = detail.validation;
  const [note, setNote] = useState("");
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [conflictDisabled, setConflictDisabled] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (!canAddInternalNote) return null;

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!note.trim() || isSubmitting || conflictDisabled) return;
    setIsSubmitting(true);
    setError(null);
    try {
      const updated = await api.addInternalNote(requestId, { note: note.trim() }, detail.version);
      onDetailUpdated(updated);
      setNote("");
    } catch (err) {
      if (err instanceof ApiError && err.status === 409) {
        setConflictDisabled(true);
        setError(STATUS_CONFLICT_MESSAGE);
      } else {
        setError("Could not save note. Try again.");
      }
    } finally {
      setIsSubmitting(false);
    }
  }

  return (
    <div>
      <p className="text-sm font-semibold text-[var(--ophalo-ink)] mb-0.5">Internal note</p>
      <p className="text-xs text-[var(--ophalo-muted)] mb-2">Not visible to customer</p>
      {error && (
        <div className={`mb-2 rounded-lg p-3 text-xs ${
          conflictDisabled
            ? "bg-[var(--ophalo-attention-bg)] text-[var(--ophalo-attention)]"
            : "bg-[var(--ophalo-danger-bg)] text-[var(--ophalo-danger)]"
        }`}>
          {error}
        </div>
      )}
      <form onSubmit={(e) => void handleSubmit(e)} className="space-y-2">
        <textarea
          value={note}
          onChange={(e) => setNote(e.target.value)}
          maxLength={internalNoteMaxLength}
          disabled={conflictDisabled}
          placeholder="Add a note for your team…"
          rows={3}
          className={`${INPUT_CLS} resize-none`}
        />
        <KeepButton
          type="submit"
          variant="secondary"
          disabled={isSubmitting || conflictDisabled || !note.trim()}
          className="w-full"
        >
          {isSubmitting ? "Saving…" : "Save internal note"}
        </KeepButton>
      </form>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Log external contact modal
// ---------------------------------------------------------------------------

interface LogContactModalProps {
  requestId: string;
  detail: KeepRequestDetailResult;
  initialDirection: string;
  initialChannel: string;
  onDetailUpdated: (updated: KeepRequestDetailResult) => void;
  onClose: () => void;
}

function LogContactModal({
  requestId,
  detail,
  initialDirection,
  initialChannel,
  onDetailUpdated,
  onClose,
}: LogContactModalProps) {
  const [channel, setChannel] = useState(initialChannel);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [conflictDisabled, setConflictDisabled] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [phoneCopied, setPhoneCopied] = useState(false);

  const showPhone = channel === "phone" && !!detail.customerPhone;

  async function handleSubmit(body: Parameters<typeof api.logExternalContact>[1]) {
    if (isSubmitting || conflictDisabled) return;
    setIsSubmitting(true);
    setError(null);
    try {
      const updated = await api.logExternalContact(requestId, body, detail.version);
      onDetailUpdated(updated);
      onClose();
    } catch (e) {
      if (e instanceof ApiError && e.status === 409) {
        setConflictDisabled(true);
        setError(STATUS_CONFLICT_MESSAGE);
      } else {
        setError("Could not save contact log. Try again.");
      }
    } finally {
      setIsSubmitting(false);
    }
  }

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 px-4"
      onClick={onClose}
    >
      <div
        className="bg-[var(--ophalo-card)] rounded-xl shadow-xl w-full max-w-md p-5"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-center justify-between mb-1">
          <h2 className="text-base font-semibold text-[var(--ophalo-ink)]">Log external contact</h2>
          <button
            type="button"
            onClick={onClose}
            className={`text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] p-1 rounded-md transition-colors ${FOCUS_RING}`}
          >
            <X className="h-4 w-4" />
            <span className="sr-only">Close</span>
          </button>
        </div>
        <p className="text-xs text-[var(--ophalo-muted)] mb-4">
          Save a Keep record of this contact. Opening the phone app is a separate step.
        </p>

        {/* Phone number + utilities — detail-only affordance, synced via onChannelChange */}
        {showPhone && (
          <div className="flex flex-wrap items-center gap-3 mb-4 rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-canvas)] px-3 py-2.5">
            <span className="flex items-center gap-1.5 text-sm font-semibold text-[var(--ophalo-ink)]">
              <Phone className="h-3.5 w-3.5 text-[var(--keep-accent)] shrink-0" />
              {detail.customerPhone}
            </span>
            <div className="flex items-center gap-2 ml-auto">
              <button
                type="button"
                onClick={async () => {
                  await navigator.clipboard.writeText(detail.customerPhone!);
                  setPhoneCopied(true);
                  setTimeout(() => setPhoneCopied(false), 2000);
                }}
                className={`text-xs text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] transition-colors ${FOCUS_RING}`}
              >
                {phoneCopied ? "Copied!" : "Copy"}
              </button>
              <span className="text-[var(--ophalo-border)]">·</span>
              <a
                href={`tel:${detail.customerPhone}`}
                className={`text-xs text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] transition-colors ${FOCUS_RING}`}
              >
                Call with phone app
              </a>
            </div>
          </div>
        )}

        <ExternalContactForm
          initialDirection={initialDirection as "outbound" | "inbound"}
          initialChannel={initialChannel}
          maxSummaryLength={detail.validation.externalContactSummaryMaxLength}
          loading={isSubmitting}
          disabled={conflictDisabled}
          error={error}
          onSubmit={(body) => void handleSubmit(body)}
          onCancel={onClose}
          onChannelChange={setChannel}
        />
      </div>
    </div>
  );
}


// ---------------------------------------------------------------------------
// Original request card — context below the hero
// ---------------------------------------------------------------------------

// ---------------------------------------------------------------------------
// Service Location
// ---------------------------------------------------------------------------

const US_STATES: [string, string][] = [
  ["AL","Alabama"],["AK","Alaska"],["AZ","Arizona"],["AR","Arkansas"],["CA","California"],
  ["CO","Colorado"],["CT","Connecticut"],["DE","Delaware"],["DC","Washington DC"],["FL","Florida"],
  ["GA","Georgia"],["HI","Hawaii"],["ID","Idaho"],["IL","Illinois"],["IN","Indiana"],
  ["IA","Iowa"],["KS","Kansas"],["KY","Kentucky"],["LA","Louisiana"],["ME","Maine"],
  ["MD","Maryland"],["MA","Massachusetts"],["MI","Michigan"],["MN","Minnesota"],["MS","Mississippi"],
  ["MO","Missouri"],["MT","Montana"],["NE","Nebraska"],["NV","Nevada"],["NH","New Hampshire"],
  ["NJ","New Jersey"],["NM","New Mexico"],["NY","New York"],["NC","North Carolina"],["ND","North Dakota"],
  ["OH","Ohio"],["OK","Oklahoma"],["OR","Oregon"],["PA","Pennsylvania"],["RI","Rhode Island"],
  ["SC","South Carolina"],["SD","South Dakota"],["TN","Tennessee"],["TX","Texas"],["UT","Utah"],
  ["VT","Vermont"],["VA","Virginia"],["WA","Washington"],["WV","West Virginia"],["WI","Wisconsin"],
  ["WY","Wyoming"],
];

interface ServiceLocationModalProps {
  requestId: string;
  detail: KeepRequestDetailResult;
  onDetailUpdated: (updated: KeepRequestDetailResult) => void;
  onClose: () => void;
}

function ServiceLocationModal({ requestId, detail, onDetailUpdated, onClose }: ServiceLocationModalProps) {
  const [addressLine1, setAddressLine1] = useState(detail.serviceAddressLine1 ?? "");
  const [addressLine2, setAddressLine2] = useState(detail.serviceAddressLine2 ?? "");
  const [city, setCity] = useState(detail.serviceCity ?? "");
  const [state, setState] = useState(detail.serviceState ?? "");
  const [zip, setZip] = useState(detail.serviceZip ?? "");
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [conflictDisabled, setConflictDisabled] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const isEditing = !!(detail.serviceAddressLine1 || detail.serviceCity);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (isSubmitting || conflictDisabled) return;
    setIsSubmitting(true);
    setError(null);
    try {
      const body: UpdateServiceLocationBody = {
        addressLine1: addressLine1.trim(),
        city: city.trim(),
        state: state.trim(),
      };
      if (addressLine2.trim()) body.addressLine2 = addressLine2.trim();
      if (zip.trim()) body.zip = zip.trim();
      const updated = await api.updateServiceLocation(requestId, body, detail.version);
      onDetailUpdated(updated);
      onClose();
    } catch (e) {
      if (e instanceof ApiError && e.status === 409) {
        setConflictDisabled(true);
        setError(STATUS_CONFLICT_MESSAGE);
      } else {
        setError("Could not save location. Check fields and try again.");
      }
    } finally {
      setIsSubmitting(false);
    }
  }

  const inputCls = `w-full rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-canvas)] px-3 py-2 text-sm text-[var(--ophalo-ink)] placeholder:text-[var(--ophalo-muted)] focus:outline-none focus:ring-2 focus:ring-[var(--keep-accent)] focus:border-transparent ${FOCUS_RING}`;
  const labelCls = "block text-xs font-medium text-[var(--ophalo-muted)] mb-1";

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 px-4"
      onClick={onClose}
    >
      <div
        className="bg-[var(--ophalo-card)] rounded-xl shadow-xl w-full max-w-md p-5"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-center justify-between mb-4">
          <h2 className="text-base font-semibold text-[var(--ophalo-ink)]">
            {isEditing ? "Edit service location" : "Add service location"}
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

        <form onSubmit={handleSubmit} className="space-y-3">
          <div>
            <label htmlFor="sl-line1" className={labelCls}>
              Address line 1 <span className="text-[var(--ophalo-attention)]">*</span>
            </label>
            <input
              id="sl-line1"
              type="text"
              className={inputCls}
              value={addressLine1}
              onChange={(e) => setAddressLine1(e.target.value)}
              placeholder="123 Main St"
              required
              autoFocus
            />
          </div>
          <div>
            <label htmlFor="sl-line2" className={labelCls}>Address line 2</label>
            <input
              id="sl-line2"
              type="text"
              className={inputCls}
              value={addressLine2}
              onChange={(e) => setAddressLine2(e.target.value)}
              placeholder="Apt, unit, suite (optional)"
            />
          </div>
          <div className="grid grid-cols-2 gap-3">
            <div>
              <label htmlFor="sl-city" className={labelCls}>
                City <span className="text-[var(--ophalo-attention)]">*</span>
              </label>
              <input
                id="sl-city"
                type="text"
                className={inputCls}
                value={city}
                onChange={(e) => setCity(e.target.value)}
                placeholder="City"
                required
              />
            </div>
            <div>
              <label htmlFor="sl-zip" className={labelCls}>ZIP</label>
              <input
                id="sl-zip"
                type="text"
                className={inputCls}
                value={zip}
                onChange={(e) => setZip(e.target.value)}
                placeholder="00000 (optional)"
                inputMode="numeric"
              />
            </div>
          </div>
          <div>
            <label htmlFor="sl-state" className={labelCls}>
              State <span className="text-[var(--ophalo-attention)]">*</span>
            </label>
            <select
              id="sl-state"
              className={inputCls}
              value={state}
              onChange={(e) => setState(e.target.value)}
              required
            >
              <option value="">Select state…</option>
              {US_STATES.map(([code, name]) => (
                <option key={code} value={code}>{name}</option>
              ))}
            </select>
          </div>

          {error && (
            <p className="text-xs text-[var(--ophalo-danger)]">{error}</p>
          )}

          <div className="flex justify-end gap-2 pt-1">
            <button
              type="button"
              onClick={onClose}
              className={`px-3 py-1.5 text-sm text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] transition-colors rounded-md ${FOCUS_RING}`}
            >
              Cancel
            </button>
            <KeepButton
              type="submit"
              disabled={isSubmitting || conflictDisabled}
              className="min-h-[34px] px-4 py-1.5 text-sm"
            >
              {isSubmitting ? "Saving…" : "Save location"}
            </KeepButton>
          </div>
        </form>
      </div>
    </div>
  );
}

interface OriginalRequestCardProps {
  detail: KeepRequestDetailResult;
}

function OriginalRequestCard({ detail }: OriginalRequestCardProps) {
  if (!detail.description) return null;
  return (
    <div className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-5 py-4">
      <p className="text-[11px] font-semibold uppercase tracking-wide text-[var(--ophalo-muted)] mb-1">
        Customer description
      </p>
      <p className="text-sm leading-6 text-[var(--ophalo-ink)] whitespace-pre-wrap">
        {detail.description}
      </p>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Side panel context sections
// ---------------------------------------------------------------------------

interface CustomerPanelProps {
  detail: KeepRequestDetailResult;
  onContactLaunched: (direction: string, channel: string) => void;
}

function CustomerPanel({ detail, onContactLaunched }: CustomerPanelProps) {
  const [copiedPhone, setCopiedPhone] = useState(false);
  const [copiedEmail, setCopiedEmail] = useState(false);
  const callAction = detail.contactActions.find((a) => a.available && a.type === "call");
  const emailAction = detail.contactActions.find((a) => a.available && a.type !== "call");
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
      <p className="px-1 text-[10px] font-semibold uppercase tracking-widest text-[var(--ophalo-muted)] mb-2">Customer</p>
      <div className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-4 py-3 space-y-3">
        {detail.customerPhone && (
          <div>
            <p className="text-[11px] font-semibold uppercase tracking-wide text-[var(--ophalo-muted)] mb-1">Phone</p>
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
            <p className="text-[11px] font-semibold uppercase tracking-wide text-[var(--ophalo-muted)] mb-1">Email</p>
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
                  href={`mailto:${emailAction.target}`}
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

interface ServiceLocationPanelProps {
  detail: KeepRequestDetailResult;
  onDetailUpdated: (updated: KeepRequestDetailResult) => void;
}

function ServiceLocationPanel({ detail, onDetailUpdated }: ServiceLocationPanelProps) {
  const [showModal, setShowModal] = useState(false);
  const canEdit = detail.availableActions.canAddInternalNote;
  const hasAddress = !!(detail.serviceAddressLine1 || detail.serviceCity);

  return (
    <div>
      <div className="flex items-center justify-between px-1 mb-2">
        <p className="text-[10px] font-semibold uppercase tracking-widest text-[var(--ophalo-muted)]">Service Location</p>
        {canEdit && hasAddress && (
          <button
            type="button"
            onClick={() => setShowModal(true)}
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
              onClick={() => setShowModal(true)}
              className={`inline-flex min-h-[32px] shrink-0 items-center rounded-lg border border-[var(--ophalo-attention)] bg-[var(--ophalo-card)] px-3 text-xs font-semibold text-[var(--ophalo-ink)] hover:bg-white transition-colors ${FOCUS_RING}`}
            >
              Add location
            </button>
          )}
        </div>
      )}
      {showModal && (
        <ServiceLocationModal
          requestId={detail.requestId}
          detail={detail}
          onDetailUpdated={onDetailUpdated}
          onClose={() => setShowModal(false)}
        />
      )}
    </div>
  );
}

interface TriagePanelProps {
  detail: KeepRequestDetailResult;
  onDetailUpdated: (updated: KeepRequestDetailResult) => void;
}

function TriagePanel({ detail, onDetailUpdated }: TriagePanelProps) {
  const [pendingPriority, setPendingPriority] = useState<string | null | undefined>(undefined);
  const canEdit = detail.availableActions.canAddInternalNote;
  const displayPriority = pendingPriority !== undefined ? pendingPriority : detail.businessPriority;
  const hasCustomerSignal = detail.source === "public_intake" &&
    !!(detail.intakeUrgency || detail.contactPreference);

  return (
    <div>
      <p className="px-1 text-[10px] font-semibold uppercase tracking-widest text-[var(--ophalo-muted)] mb-2">Triage</p>
      <div className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-4 py-3 space-y-3">
        {hasCustomerSignal && (
          <div>
            <p className="text-[11px] font-semibold uppercase tracking-wide text-[var(--ophalo-muted)] mb-1">Customer signal</p>
            <div className="flex flex-wrap gap-1.5 mb-1.5">
              {detail.intakeUrgency === "urgent" && <KeepBadge variant="attention">Customer marked urgent</KeepBadge>}
              {detail.intakeUrgency === "soon" && <KeepBadge variant="default">Customer asked for soon follow-up</KeepBadge>}
              {detail.contactPreference === "phone_call" && <KeepBadge variant="default">Prefers call</KeepBadge>}
              {detail.contactPreference === "text_message" && <KeepBadge variant="default">Prefers text</KeepBadge>}
              {detail.contactPreference === "email" && <KeepBadge variant="default">Prefers email</KeepBadge>}
              {detail.contactPreference === "no_preference" && <KeepBadge variant="default">No preference</KeepBadge>}
            </div>
            <p className="text-[11px] text-[var(--ophalo-muted)]">
              Review the request, then update the customer or log contact if needed.
            </p>
          </div>
        )}
        <div>
          <p className="text-[11px] font-semibold uppercase tracking-wide text-[var(--ophalo-muted)] mb-1">Internal priority</p>
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
                <p className="text-[11px] text-[var(--ophalo-muted)] mt-1">
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

interface SourceMetaPanelProps {
  detail: KeepRequestDetailResult;
}

function SourceMetaPanel({ detail }: SourceMetaPanelProps) {
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
// Needs attention guidance — frontend-mapped until backend action effects land
// ---------------------------------------------------------------------------

interface AttentionGuidanceCardProps {
  detail: KeepRequestDetailResult;
  highlights: AttentionHighlights;
}

function AttentionGuidanceCard({ detail, highlights }: AttentionGuidanceCardProps) {
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
// ---------------------------------------------------------------------------
// RequestDetail page
// ---------------------------------------------------------------------------

interface RequestDetailProps {
  requestId: string;
  focusPanel?: string;
  onBack: () => void;
  prevId?: string;
  nextId?: string;
  onNavigate?: (id: string) => void;
}

export function RequestDetail({ requestId, focusPanel, onBack, prevId, nextId, onNavigate }: RequestDetailProps) {
  const [shareCleared, setShareCleared] = useState(false);
  const [shareModalOpen, setShareModalOpen] = useState(false);
  const [contactModal, setContactModal] = useState<{ direction: string; channel: string } | null>(null);
  const [businessUpdateDraft, setBusinessUpdateDraft] = useState("");
  const [businessUpdateDraftStatus, setBusinessUpdateDraftStatus] = useState("");
  const [timelineFilter, setTimelineFilter] = useState<TimelineFilter>("communication");
  const queryClient = useQueryClient();

  const { data: detail, isLoading, isError, error } = useQuery({
    queryKey: ["request-detail", requestId],
    queryFn: () => api.getRequestDetail(requestId),
  });

  const needsShareEffective = !shareCleared && (detail?.needsShare ?? false);
  const canShare = detail?.availableActions.canRecordShareIntent ?? false;

  const displayedEvents = useMemo(() => {
    if (!detail) return [];
    const base = detail.events.filter((e) => !ALWAYS_HIDDEN_EVENT_TYPES.has(e.eventType));
    const filtered = timelineFilter === "communication" ? base.filter(isCommunicationEvent) : base;
    return [...filtered].sort((a, b) => {
      const byDate = new Date(b.occurredAtUtc).getTime() - new Date(a.occurredAtUtc).getTime();
      if (byDate !== 0) return byDate;
      return b.id.localeCompare(a.id);
    });
  }, [detail, timelineFilter]);

  const focusScrolledRef = useRef(false);

  useEffect(() => {
    focusScrolledRef.current = false;
  }, [requestId]);

  useEffect(() => {
    if (!focusPanel || !detail || focusScrolledRef.current) return;
    const el = document.getElementById(`focus-panel-${focusPanel}`);
    if (el) {
      el.scrollIntoView({ behavior: "smooth", block: "nearest" });
      focusScrolledRef.current = true;
    }
  }, [focusPanel, detail]);

  const focusHighlights = useMemo((): AttentionHighlights => {
    if (!focusPanel || !detail) return {};
    switch (focusPanel) {
      case "update": return { sendUpdate: "primary" };
      case "contact": return { logContact: "primary" };
      case "attention": return { markHandled: "primary" };
      case "feedback_review": return { feedbackReview: "primary" };
      default: return {};
    }
  }, [focusPanel, detail]);

  const highlights = useMemo(() => {
    const attention = detail ? getAttentionResolutionHighlights(detail) : {};
    return {
      sendUpdate: attention.sendUpdate ?? focusHighlights.sendUpdate,
      logContact: attention.logContact ?? focusHighlights.logContact,
      workControls: attention.workControls ?? focusHighlights.workControls,
      feedbackReview: attention.feedbackReview ?? focusHighlights.feedbackReview,
      markHandled: attention.markHandled ?? focusHighlights.markHandled,
    };
  }, [detail, focusHighlights]);

  const workControlsIsHighlighted = !!highlights.feedbackReview;

  function handleShareCleared() {
    setShareCleared(true);
    setShareModalOpen(false);
    void queryClient.invalidateQueries({ queryKey: ["request-detail", requestId] });
  }

  function handleDetailUpdated(updated: KeepRequestDetailResult) {
    queryClient.setQueryData(["request-detail", requestId], updated);
    setShareCleared(false);
  }

  function handleContactLaunched(direction: string, channel: string) {
    setContactModal({ direction, channel });
  }

  const filterBtnCls = (active: boolean) =>
    `flex-1 px-3 py-1.5 text-xs font-semibold transition-colors ${FOCUS_RING} ${
      active
        ? "bg-[var(--ophalo-navy)] text-white"
        : "bg-[var(--ophalo-card)] text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)]"
    }`;

  // Primary communication actions: Send update + contact log + mark handled + work controls
  function renderPrimaryActions() {
    if (!detail) return null;
    return (
      <>
        <WorkDoneCard
          requestId={requestId}
          detail={detail}
          onDetailUpdated={handleDetailUpdated}
        />
        <CloseRequestCard
          requestId={requestId}
          detail={detail}
          onDetailUpdated={handleDetailUpdated}
        />
        <LogContactCard
          detail={detail}
          onContactLaunched={handleContactLaunched}
          highlight={highlights.logContact}
        />
        <MarkHandledCard
          requestId={requestId}
          detail={detail}
          onDetailUpdated={handleDetailUpdated}
          highlight={highlights.markHandled}
        />
        <WorkControlsGroup
          requestId={requestId}
          detail={detail}
          onDetailUpdated={handleDetailUpdated}
          highlights={highlights}
        />
      </>
    );
  }

  // Team / context: participation, timing
  function renderTeamSection() {
    if (!detail) return null;
    return (
      <TeamSection
        requestId={requestId}
        detail={detail}
        onDetailUpdated={handleDetailUpdated}
      />
    );
  }

  return (
    <div className="flex flex-col h-full bg-[var(--ophalo-canvas)]">
      {/* Log contact modal */}
      {contactModal && detail && (
        <LogContactModal
          requestId={requestId}
          detail={detail}
          initialDirection={contactModal.direction}
          initialChannel={contactModal.channel}
          onDetailUpdated={handleDetailUpdated}
          onClose={() => setContactModal(null)}
        />
      )}

      {/* Share Link modal */}
      {shareModalOpen && (
        <ShareLinkModal
          requestId={requestId}
          onClose={() => setShareModalOpen(false)}
          onShared={handleShareCleared}
        />
      )}

      {/* Mobile NeedsShare banner */}
      {detail && needsShareEffective && canShare && (
        <NeedsShareBanner onOpenShareDrawer={() => setShareModalOpen(true)} />
      )}

      {/* Back breadcrumb + queue navigation */}
      <div className="flex items-center gap-2 px-4 py-3 bg-[var(--ophalo-card)] border-b border-[var(--ophalo-border)] shrink-0">
        <button
          type="button"
          onClick={onBack}
          className={`flex items-center gap-1 text-sm text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] -ml-1 transition-colors ${FOCUS_RING}`}
        >
          <ChevronLeft className="h-4 w-4" />
          Requests
        </button>
        {detail && (
          <span className="text-sm text-[var(--ophalo-muted)] font-mono ml-1">
            {detail.referenceCode}
          </span>
        )}
        {onNavigate && (prevId !== undefined || nextId !== undefined) && (
          <div className="ml-auto flex items-center gap-1">
            <button
              type="button"
              disabled={!prevId}
              onClick={() => prevId && onNavigate(prevId)}
              aria-label="Previous request"
              className={`flex items-center gap-0.5 px-2 py-1 text-xs font-medium rounded text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] disabled:opacity-40 disabled:cursor-not-allowed transition-colors ${FOCUS_RING}`}
            >
              <ChevronLeft className="h-3.5 w-3.5" />
              Prev
            </button>
            <button
              type="button"
              disabled={!nextId}
              onClick={() => nextId && onNavigate(nextId)}
              aria-label="Next request"
              className={`flex items-center gap-0.5 px-2 py-1 text-xs font-medium rounded text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] disabled:opacity-40 disabled:cursor-not-allowed transition-colors ${FOCUS_RING}`}
            >
              Next
              <ChevronRight className="h-3.5 w-3.5" />
            </button>
          </div>
        )}
      </div>

      {/* Loading */}
      {isLoading && (
        <div className="flex flex-1 items-center justify-center">
          <span className="text-[var(--ophalo-muted)] text-sm">Loading…</span>
        </div>
      )}

      {/* Error */}
      {isError && (
        <div className="flex flex-1 items-center justify-center px-4">
          <span className="text-[var(--ophalo-muted)] text-sm text-center">
            {error instanceof ApiError && error.status === 403
              ? "You don't have access to this request."
              : error instanceof ApiError && error.status === 404
                ? "Request not found."
                : "Something went wrong. Try going back and reopening."}
          </span>
        </div>
      )}

      {/* Main content */}
      {detail && (
        <div className="flex flex-1 min-h-0 overflow-hidden md:grid md:[grid-template-columns:minmax(0,7fr)_minmax(320px,3fr)]">
          {/* Left / main column */}
          <div className="flex-1 md:flex-none overflow-y-auto px-4 md:px-6 py-5 space-y-4">
            <TodayPromiseBanner detail={detail} />

            {/* Hero: identity + status */}
            <DetailHero
              detail={detail}
              canRecordShareIntent={canShare}
              needsShare={needsShareEffective}
              onOpenShareDrawer={() => setShareModalOpen(true)}
            />

            {/* Original request: customer description */}
            <OriginalRequestCard detail={detail} />

            {/* Needs attention: why it is here + how to handle it */}
            <AttentionGuidanceCard
              detail={detail}
              highlights={highlights}
            />

            {/* Mobile: primary actions appear before the timeline */}
            <div className="md:hidden space-y-4">
              {renderPrimaryActions()}
            </div>

            {/* Send customer update — main work loop, above activity */}
            <div id="focus-panel-update">
              <BusinessUpdateSection
                requestId={requestId}
                detail={detail}
                onDetailUpdated={handleDetailUpdated}
                draft={businessUpdateDraft}
                onDraftChange={setBusinessUpdateDraft}
                draftStatus={businessUpdateDraftStatus}
                onDraftStatusChange={setBusinessUpdateDraftStatus}
                highlight={highlights.sendUpdate}
              />
            </div>

            {/* Activity timeline */}
            <div className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-5 py-5">
              <div className="flex items-center justify-between mb-4 gap-3">
                <p className="text-base font-semibold text-[var(--ophalo-ink)] shrink-0">Activity</p>
                <div
                  className="flex rounded-lg border border-[var(--ophalo-border)] overflow-hidden shrink-0"
                  role="group"
                  aria-label="Activity filter"
                >
                  <button
                    type="button"
                    aria-pressed={timelineFilter === "communication"}
                    onClick={() => setTimelineFilter("communication")}
                    className={filterBtnCls(timelineFilter === "communication")}
                  >
                    Conversation &amp; notes
                  </button>
                  <button
                    type="button"
                    aria-pressed={timelineFilter === "all"}
                    onClick={() => setTimelineFilter("all")}
                    className={`border-l border-[var(--ophalo-border)] ${filterBtnCls(timelineFilter === "all")}`}
                  >
                    All activity
                  </button>
                </div>
              </div>

              {displayedEvents.length === 0 ? (
                <p className="text-sm text-[var(--ophalo-muted)]">
                  {timelineFilter === "communication"
                    ? "No customer updates or internal notes yet."
                    : "No activity yet."}
                </p>
              ) : (
                <div className="relative space-y-2 border-l border-[var(--ophalo-border)] pl-3 ml-4">
                  {displayedEvents.map((event, idx) => (
                    <TimelineEvent key={event.id} event={event} isFirst={idx === 0} />
                  ))}
                </div>
              )}
            </div>

            {/* Mobile: contact, location, triage, timing, team, internal — after activity */}
            <div className="md:hidden space-y-4 pb-6">
              <CustomerPanel detail={detail} onContactLaunched={handleContactLaunched} />
              <ServiceLocationPanel detail={detail} onDetailUpdated={handleDetailUpdated} />
              <TriagePanel detail={detail} onDetailUpdated={handleDetailUpdated} />
              <TimingPanel requestId={requestId} detail={detail} onDetailUpdated={handleDetailUpdated} />
              {renderTeamSection()}
              <InternalNoteCard requestId={requestId} detail={detail} onDetailUpdated={handleDetailUpdated} />
              <SourceMetaPanel detail={detail} />
            </div>
          </div>

          {/* Right context + action panel — desktop only */}
          <aside className="hidden md:flex md:flex-col border-l border-[var(--ophalo-border)] bg-[var(--ophalo-card)] overflow-y-auto px-4 py-5 gap-4">
            {/* Lifecycle actions: work completion and closeout */}
            <WorkDoneCard
              requestId={requestId}
              detail={detail}
              onDetailUpdated={handleDetailUpdated}
            />
            <div id="focus-panel-closeout">
              <CloseRequestCard
                requestId={requestId}
                detail={detail}
                onDetailUpdated={handleDetailUpdated}
              />
            </div>
            {/* Resolution cards: primary actions for clearing attention */}
            <div id="focus-panel-contact">
              <LogContactCard
                detail={detail}
                onContactLaunched={handleContactLaunched}
                highlight={highlights.logContact}
              />
            </div>
            <div id="focus-panel-attention">
              <MarkHandledCard
                requestId={requestId}
                detail={detail}
                onDetailUpdated={handleDetailUpdated}
                highlight={highlights.markHandled}
              />
            </div>
            {workControlsIsHighlighted && (
              <div id="focus-panel-feedback_review">
                <WorkControlsGroup
                  requestId={requestId}
                  detail={detail}
                  onDetailUpdated={handleDetailUpdated}
                  highlights={highlights}
                />
              </div>
            )}

            {/* Context: customer, location, triage, timing */}
            <CustomerPanel detail={detail} onContactLaunched={handleContactLaunched} />
            <ServiceLocationPanel detail={detail} onDetailUpdated={handleDetailUpdated} />
            <TriagePanel detail={detail} onDetailUpdated={handleDetailUpdated} />
            <TimingPanel requestId={requestId} detail={detail} onDetailUpdated={handleDetailUpdated} />

            {/* Internal: notes not visible to customer */}
            <div className="space-y-3">
              <p className="px-1 text-[10px] font-semibold uppercase tracking-widest text-[var(--ophalo-muted)]">Internal</p>
              <InternalNoteCard
                requestId={requestId}
                detail={detail}
                onDetailUpdated={handleDetailUpdated}
              />
            </div>

            {/* Utilities: feedback review, team, admin (classification deferred — no compact V1 UI) */}
            <div className="space-y-3">
              <p className="px-1 text-[10px] font-semibold uppercase tracking-widest text-[var(--ophalo-muted)]">Utilities</p>
              {!workControlsIsHighlighted && (
                <div id="focus-panel-feedback_review">
                  <WorkControlsGroup
                    requestId={requestId}
                    detail={detail}
                    onDetailUpdated={handleDetailUpdated}
                    highlights={highlights}
                  />
                </div>
              )}
              {renderTeamSection()}
            </div>

            <SourceMetaPanel detail={detail} />
          </aside>
        </div>
      )}
    </div>
  );
}
