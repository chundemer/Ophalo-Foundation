import { useState, useMemo } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import {
  ChevronLeft,
  Copy,
  Check,
  Share2,
  AlertTriangle,
  Clock,
  MessageSquare,
  Phone,
  Mail,
  User,
  X,
} from "lucide-react";
import {
  api,
  type KeepRequestDetailResult,
  type KeepRequestEventItem,
  type ShareIntentMethod,
} from "../lib/apiClient";
import { ApiError } from "../lib/apiClient";
import { NeedsShareBanner } from "../components/NeedsShareBanner";

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const EVENT_TYPE_LABELS: Record<string, string> = {
  request_created: "Request Created",
  status_changed: "Status Changed",
  message_added: "Business Update",
  request_closed: "Request Closed",
  request_cancelled: "Request Cancelled",
  internal_note_added: "Internal Note",
  attention_acknowledged: "Attention Acknowledged",
  external_contact_logged: "External Contact",
  participation_changed: "Participation Changed",
  feedback_reviewed: "Feedback Reviewed",
  follow_up_on_changed: "Follow-Up",
  planned_for_changed: "Planned Date",
  request_classified: "Request Classified",
  share_intent_recorded: "Tracker Link Shared",
};

function eventTypeLabel(type: string): string {
  return EVENT_TYPE_LABELS[type] ?? type.replace(/_/g, " ").replace(/\b\w/g, (c) => c.toUpperCase());
}

function formatEventTime(isoUtc: string): string {
  const d = new Date(isoUtc);
  const now = new Date();
  const diffMs = now.getTime() - d.getTime();
  const diffMins = Math.floor(diffMs / 60_000);
  if (diffMins < 1) return "just now";
  if (diffMins < 60) return `${diffMins}m ago`;
  const diffHours = Math.floor(diffMins / 60);
  if (diffHours < 24) return `${diffHours}h ago`;
  const sameYear = d.getFullYear() === now.getFullYear();
  return d.toLocaleDateString("en-US", {
    month: "short",
    day: "numeric",
    year: sameYear ? undefined : "numeric",
  });
}

function formatDate(isoUtc: string): string {
  return new Date(isoUtc).toLocaleDateString("en-US", {
    month: "short",
    day: "numeric",
    year: "numeric",
    hour: "numeric",
    minute: "2-digit",
  });
}

function formatDateOnly(isoDate: string): string {
  const [year, month, day] = isoDate.split("-").map(Number);
  return new Date(year, month - 1, day).toLocaleDateString("en-US", {
    month: "short",
    day: "numeric",
    year: "numeric",
  });
}

const FOLLOW_UP_REASON_LABELS: Record<string, string> = {
  weather: "Weather",
  parts: "Waiting on parts",
  customer_delay: "Waiting on customer",
  business_operator_availability: "Need to schedule",
  third_party: "Third party",
  other: "Other",
};

function statusLabel(status: string): string {
  return status.replace(/_/g, " ").replace(/\b\w/g, (c) => c.toUpperCase());
}

// ---------------------------------------------------------------------------
// Timeline
// ---------------------------------------------------------------------------

function timelineEventSummary(event: KeepRequestEventItem): string | null {
  if (event.eventType === "status_changed" && event.statusAfter) {
    return `Status changed to ${statusLabel(event.statusAfter)}`;
  }
  if (event.eventType === "participation_changed") {
    const action = event.participationAction ?? "";
    const target = event.participationTargetDisplayName ?? "someone";
    if (action === "assigned") return `Assigned to ${target}`;
    if (action === "unassigned") return `Unassigned from ${target}`;
    if (action === "watching_added") return `${target} started watching`;
    if (action === "watching_removed") return `${target} stopped watching`;
    if (action === "muted") return `${target} muted notifications`;
    if (action === "unmuted") return `${target} unmuted notifications`;
    return `Participation updated`;
  }
  if (event.eventType === "external_contact_logged") {
    const dir = event.externalContactDirection ?? "";
    const ch = event.externalContactChannel ?? "";
    const outcome = event.externalContactOutcome;
    const label = `${dir === "inbound" ? "Inbound" : "Outbound"} ${ch}`;
    return outcome ? `${label} — ${outcome.replace(/_/g, " ")}` : label;
  }
  if (event.eventType === "attention_acknowledged") return "Attention acknowledged";
  if (event.eventType === "share_intent_recorded") return "Tracker link shared with customer";
  if (event.eventType === "planned_for_changed") {
    return event.plannedForDate
      ? `Planned date set to ${formatDateOnly(event.plannedForDate)}`
      : "Planned date removed";
  }
  if (event.eventType === "follow_up_on_changed") {
    if (!event.followUpOnDate) return "Follow-up removed";
    const base = `Follow-up set for ${formatDateOnly(event.followUpOnDate)}`;
    const reasonLabel = event.followUpOnReason ? FOLLOW_UP_REASON_LABELS[event.followUpOnReason] : null;
    return reasonLabel ? `${base} · ${reasonLabel}` : base;
  }
  return null;
}

interface TimelineEventProps {
  event: KeepRequestEventItem;
}

function TimelineEvent({ event }: TimelineEventProps) {
  const isCustomerVisible = event.visibility === "all";
  const summary = timelineEventSummary(event);

  if (isCustomerVisible) {
    return (
      <div className="flex gap-3">
        <div className="flex flex-col items-center">
          <div className="h-2 w-2 rounded-full bg-slate-700 mt-1.5 shrink-0" />
          <div className="w-px flex-1 bg-slate-200 mt-1" />
        </div>
        <div className="pb-4 min-w-0 flex-1">
          <div className="flex items-baseline justify-between gap-2 mb-1">
            <span className="text-xs font-medium text-slate-700">{eventTypeLabel(event.eventType)}</span>
            <span className="text-xs text-slate-400 shrink-0">{formatEventTime(event.occurredAtUtc)}</span>
          </div>
          {summary && (
            <p className="text-sm text-slate-600">{summary}</p>
          )}
          {event.content && (
            <p className="text-sm text-slate-800 mt-0.5 whitespace-pre-wrap">{event.content}</p>
          )}
          {event.actorDisplayName && (
            <p className="text-xs text-slate-400 mt-1">{event.actorDisplayName}</p>
          )}
        </div>
      </div>
    );
  }

  // Internal / system event — quieter treatment
  return (
    <div className="flex gap-3">
      <div className="flex flex-col items-center">
        <div className="h-1.5 w-1.5 rounded-full bg-slate-300 mt-1.5 shrink-0" />
        <div className="w-px flex-1 bg-slate-100 mt-1" />
      </div>
      <div className="pb-3 min-w-0 flex-1">
        <div className="flex items-baseline justify-between gap-2">
          <span className="text-xs text-slate-400">{eventTypeLabel(event.eventType)}</span>
          <span className="text-xs text-slate-300 shrink-0">{formatEventTime(event.occurredAtUtc)}</span>
        </div>
        {summary && (
          <p className="text-xs text-slate-500 mt-0.5">{summary}</p>
        )}
        {event.content && event.visibility === "internal" && (
          <p className="text-sm text-slate-500 mt-0.5 italic whitespace-pre-wrap">{event.content}</p>
        )}
        {event.actorDisplayName && (
          <p className="text-xs text-slate-300 mt-0.5">{event.actorDisplayName}</p>
        )}
      </div>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Desktop share section (right rail)
// ---------------------------------------------------------------------------

interface DesktopShareSectionProps {
  requestId: string;
  pageToken: string;
  canRecordShareIntent: boolean;
  needsShare: boolean;
  onCleared: () => void;
}

function DesktopShareSection({ requestId, pageToken, canRecordShareIntent, needsShare, onCleared }: DesktopShareSectionProps) {
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [copied, setCopied] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const publicBaseUrl = import.meta.env.VITE_PUBLIC_BASE_URL as string;
  const trackerUrl = `${publicBaseUrl}/keep/r/${pageToken}`;
  const canNativeShare = typeof navigator !== "undefined" && typeof navigator.share === "function";

  if (!canRecordShareIntent) return null;

  async function submit(method: ShareIntentMethod, browserAction?: () => Promise<void>) {
    if (isSubmitting) return;
    setIsSubmitting(true);
    setError(null);
    try {
      if (browserAction) await browserAction();
      await api.recordShareIntent(requestId, method);
      onCleared();
    } catch (e) {
      if (e instanceof DOMException && e.name === "AbortError") {
        // user cancelled native share
      } else if (e instanceof ApiError) {
        setError("Could not record share. Try again.");
      } else if (!(e instanceof DOMException)) {
        setError("Could not complete share. Try again.");
      }
    } finally {
      setIsSubmitting(false);
    }
  }

  async function handleCopyLink() {
    await submit("copy_link", async () => {
      await navigator.clipboard.writeText(trackerUrl);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    });
  }

  async function handleNativeShare() {
    await submit("native_share", async () => {
      await navigator.share({ url: trackerUrl, title: "Track Your Request" });
    });
  }

  async function handleMarkShared() {
    await submit("manual_mark_shared");
  }

  return (
    <div className={`rounded-lg border p-4 ${needsShare ? "border-amber-200 bg-amber-50" : "border-slate-100 bg-slate-50"}`}>
      <p className="text-xs font-semibold text-slate-700 mb-2 uppercase tracking-wide">Tracker Link</p>
      {needsShare && (
        <p className="text-xs text-amber-700 font-medium mb-3">
          Not yet shared with customer.
        </p>
      )}
      <div className="flex flex-col gap-2">
        {canNativeShare && (
          <button
            type="button"
            onClick={() => void handleNativeShare()}
            disabled={isSubmitting}
            className="flex items-center gap-2 rounded-md bg-amber-600 px-3 py-2 text-sm font-medium text-white hover:bg-amber-700 disabled:opacity-60"
          >
            <Share2 className="h-3.5 w-3.5" />
            Share Link
          </button>
        )}
        <button
          type="button"
          onClick={() => void handleCopyLink()}
          disabled={isSubmitting}
          className="flex items-center gap-2 rounded-md border border-slate-300 px-3 py-2 text-sm font-medium text-slate-700 bg-white hover:bg-slate-50 disabled:opacity-60"
        >
          {copied ? <Check className="h-3.5 w-3.5 text-emerald-600" /> : <Copy className="h-3.5 w-3.5" />}
          {copied ? "Copied!" : "Copy Link"}
        </button>
        {needsShare && (
          <button
            type="button"
            onClick={() => void handleMarkShared()}
            disabled={isSubmitting}
            className="text-xs text-slate-500 hover:text-slate-700 text-left disabled:opacity-60"
          >
            Mark as shared manually
          </button>
        )}
      </div>
      {error && <p className="mt-2 text-xs text-red-600">{error}</p>}
    </div>
  );
}

// ---------------------------------------------------------------------------
// Status change
// ---------------------------------------------------------------------------

const STATUS_CONFLICT_MESSAGE =
  "This request has been updated by another team member. Copy your unsaved notes and refresh the workbench to load the latest history.";

interface StatusChangeSectionProps {
  requestId: string;
  detail: KeepRequestDetailResult;
  onDetailUpdated: (updated: KeepRequestDetailResult) => void;
}

function StatusChangeSection({ requestId, detail, onDetailUpdated }: StatusChangeSectionProps) {
  const { allowedStatuses } = detail.availableActions;
  const { messageRequiredForStatuses, statusMessageMaxLength } = detail.validation;

  const [selectedStatus, setSelectedStatus] = useState("");
  const [message, setMessage] = useState("");
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [conflictDisabled, setConflictDisabled] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (allowedStatuses.length === 0) return null;

  const messageRequired = messageRequiredForStatuses.includes(selectedStatus);
  const canSubmit = selectedStatus !== "" && !isSubmitting && !conflictDisabled &&
    (!messageRequired || message.trim().length > 0);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!canSubmit) return;
    setIsSubmitting(true);
    setError(null);
    try {
      const body: { status: string; message?: string } = { status: selectedStatus };
      if (message.trim()) body.message = message.trim();
      const updated = await api.patchRequestStatus(requestId, body, detail.version);
      onDetailUpdated(updated);
      setSelectedStatus("");
      setMessage("");
    } catch (e) {
      if (e instanceof ApiError && e.status === 409) {
        setConflictDisabled(true);
        setError(STATUS_CONFLICT_MESSAGE);
      } else {
        setError("Could not update status. Try again.");
      }
    } finally {
      setIsSubmitting(false);
    }
  }

  return (
    <div className="rounded-lg border border-slate-100 bg-slate-50 p-4">
      <p className="text-xs font-semibold text-slate-700 mb-3 uppercase tracking-wide">Change Status</p>
      {error && (
        <div className={`mb-3 rounded-md p-3 text-xs ${conflictDisabled ? "bg-amber-50 border border-amber-200 text-amber-800" : "bg-red-50 border border-red-200 text-red-700"}`}>
          {error}
        </div>
      )}
      <form onSubmit={(e) => void handleSubmit(e)} className="space-y-3">
        <select
          value={selectedStatus}
          onChange={(e) => { setSelectedStatus(e.target.value); setError(null); }}
          disabled={conflictDisabled}
          className="w-full rounded-md border border-slate-300 bg-white px-3 py-2 text-sm text-slate-800 disabled:opacity-60 focus:outline-none focus:ring-2 focus:ring-slate-400"
        >
          <option value="">Select new status…</option>
          {allowedStatuses.map((s) => (
            <option key={s} value={s}>{statusLabel(s)}</option>
          ))}
        </select>
        <div>
          <textarea
            value={message}
            onChange={(e) => setMessage(e.target.value)}
            maxLength={statusMessageMaxLength}
            disabled={conflictDisabled}
            placeholder={messageRequired ? "Message required for this status…" : "Optional message…"}
            rows={3}
            className="w-full rounded-md border border-slate-300 bg-white px-3 py-2 text-sm text-slate-800 resize-none placeholder:text-slate-400 disabled:opacity-60 focus:outline-none focus:ring-2 focus:ring-slate-400"
          />
        </div>
        <button
          type="submit"
          disabled={!canSubmit}
          className="w-full rounded-md bg-slate-800 px-3 py-2 text-sm font-medium text-white hover:bg-slate-700 disabled:opacity-40"
        >
          {isSubmitting ? "Updating…" : "Update Status"}
        </button>
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

function LogContactModal({ requestId, detail, initialDirection, initialChannel, onDetailUpdated, onClose }: LogContactModalProps) {
  const [direction, setDirection] = useState(initialDirection);
  const [channel, setChannel] = useState(initialChannel);
  const [outcome, setOutcome] = useState("");
  const [requiresFollowUp, setRequiresFollowUp] = useState(false);
  const [summary, setSummary] = useState("");
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [conflictDisabled, setConflictDisabled] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const isOutbound = direction === "outbound";
  const maxSummary = detail.validation.externalContactSummaryMaxLength;

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (isSubmitting || conflictDisabled) return;
    setIsSubmitting(true);
    setError(null);
    try {
      const body: { direction: string; channel: string; outcome?: string; requiresBusinessFollowUp?: boolean; summary?: string } = { direction, channel };
      if (isOutbound && outcome) body.outcome = outcome;
      if (!isOutbound) body.requiresBusinessFollowUp = requiresFollowUp;
      if (summary.trim()) body.summary = summary.trim();
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
        className="bg-white rounded-lg shadow-xl w-full max-w-md p-5"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-center justify-between mb-1">
          <h2 className="text-sm font-semibold text-slate-900">Log Contact</h2>
          <button type="button" onClick={onClose} className="text-slate-400 hover:text-slate-600 p-1">
            <X className="h-4 w-4" />
          </button>
        </div>
        <p className="text-xs text-slate-500 mb-4">Save a Keep record of this contact. Opening the phone or email app is a separate action.</p>
        {error && (
          <div className={`mb-3 rounded-md p-3 text-xs ${conflictDisabled ? "bg-amber-50 border border-amber-200 text-amber-800" : "bg-red-50 border border-red-200 text-red-700"}`}>
            {error}
          </div>
        )}
        <form onSubmit={(e) => void handleSubmit(e)} className="space-y-3">
          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="block text-xs font-medium text-slate-600 mb-1">Direction</label>
              <select
                value={direction}
                onChange={(e) => { setDirection(e.target.value); setOutcome(""); setRequiresFollowUp(false); }}
                disabled={conflictDisabled}
                className="w-full rounded-md border border-slate-300 bg-white px-3 py-2 text-sm text-slate-800 disabled:opacity-60 focus:outline-none focus:ring-2 focus:ring-slate-400"
              >
                <option value="outbound">Outbound</option>
                <option value="inbound">Inbound</option>
              </select>
            </div>
            <div>
              <label className="block text-xs font-medium text-slate-600 mb-1">Channel</label>
              <select
                value={channel}
                onChange={(e) => setChannel(e.target.value)}
                disabled={conflictDisabled}
                className="w-full rounded-md border border-slate-300 bg-white px-3 py-2 text-sm text-slate-800 disabled:opacity-60 focus:outline-none focus:ring-2 focus:ring-slate-400"
              >
                <option value="phone">Phone</option>
                <option value="sms">SMS</option>
                <option value="email">Email</option>
                <option value="in_person">In Person</option>
                <option value="other">Other</option>
              </select>
            </div>
          </div>
          {isOutbound && (
            <div>
              <label className="block text-xs font-medium text-slate-600 mb-1">Outcome (optional)</label>
              <select
                value={outcome}
                onChange={(e) => setOutcome(e.target.value)}
                disabled={conflictDisabled}
                className="w-full rounded-md border border-slate-300 bg-white px-3 py-2 text-sm text-slate-800 disabled:opacity-60 focus:outline-none focus:ring-2 focus:ring-slate-400"
              >
                <option value="">— not specified —</option>
                <option value="spoke_with_customer">Spoke with customer</option>
                <option value="left_voicemail">Left voicemail</option>
                <option value="no_answer">No answer</option>
                <option value="wrong_number">Wrong number</option>
              </select>
            </div>
          )}
          {!isOutbound && (
            <label className="flex items-center gap-2 text-sm text-slate-700">
              <input
                type="checkbox"
                checked={requiresFollowUp}
                onChange={(e) => setRequiresFollowUp(e.target.checked)}
                disabled={conflictDisabled}
                className="rounded border-slate-300"
              />
              Requires business follow-up
            </label>
          )}
          <div>
            <label className="block text-xs font-medium text-slate-600 mb-1">Summary (optional)</label>
            <textarea
              value={summary}
              onChange={(e) => setSummary(e.target.value)}
              maxLength={maxSummary}
              disabled={conflictDisabled}
              placeholder="Brief notes about this contact…"
              rows={3}
              className="w-full rounded-md border border-slate-300 bg-white px-3 py-2 text-sm text-slate-800 resize-none placeholder:text-slate-400 disabled:opacity-60 focus:outline-none focus:ring-2 focus:ring-slate-400"
            />
          </div>
          <div className="flex gap-2 pt-1">
            <button
              type="button"
              onClick={onClose}
              disabled={isSubmitting}
              className="flex-1 rounded-md border border-slate-300 px-3 py-2 text-sm font-medium text-slate-700 bg-white hover:bg-slate-50 disabled:opacity-60"
            >
              Cancel
            </button>
            <button
              type="submit"
              disabled={isSubmitting || conflictDisabled}
              className="flex-1 rounded-md bg-slate-800 px-3 py-2 text-sm font-medium text-white hover:bg-slate-700 disabled:opacity-40"
            >
              {isSubmitting ? "Saving…" : "Save Contact Log"}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Detail header
// ---------------------------------------------------------------------------

interface DetailHeaderProps {
  detail: KeepRequestDetailResult;
  onContactLaunched?: (direction: string, channel: string) => void;
}

function DetailHeader({ detail, onContactLaunched }: DetailHeaderProps) {
  const hasAttention = detail.attentionLevel !== "none";

  return (
    <div className="mb-6">
      <div className="flex items-center gap-2 mb-1">
        <span className="font-mono text-xs text-slate-400">{detail.referenceCode}</span>
        <span className="text-xs bg-slate-100 text-slate-600 rounded px-1.5 py-0.5">
          {statusLabel(detail.status)}
        </span>
        {detail.attentionReason && (
          <span className={`inline-flex items-center gap-1 rounded px-1.5 py-0.5 text-xs font-medium ${
            hasAttention ? "bg-orange-100 text-orange-800 border border-orange-200" : "bg-slate-100 text-slate-600"
          }`}>
            {detail.attentionLevel === "overdue"
              ? <AlertTriangle className="h-3 w-3" />
              : detail.attentionLevel === "needs_attention"
                ? <Clock className="h-3 w-3" />
                : <MessageSquare className="h-3 w-3" />
            }
            {detail.attentionReason.replace(/_/g, " ")}
          </span>
        )}
      </div>

      <h1 className="text-lg font-semibold text-slate-900 mb-1">{detail.customerName}</h1>

      {/* Inert contact info — display only, not launchers */}
      <div className="flex flex-wrap gap-x-4 gap-y-1 text-sm text-slate-600 mb-2">
        {detail.customerPhone && (
          <span className="flex items-center gap-1">
            <Phone className="h-3.5 w-3.5 text-slate-400" />
            {detail.customerPhone}
          </span>
        )}
        {detail.customerEmail ? (
          <span className="flex items-center gap-1">
            <Mail className="h-3.5 w-3.5 text-slate-400" />
            {detail.customerEmail}
          </span>
        ) : (
          <span className="flex items-center gap-1 text-slate-400 text-xs">
            <Mail className="h-3.5 w-3.5" />
            No customer email provided
          </span>
        )}
      </div>

      {/* Contact launchers — rendered from server contactActions, not raw field inference */}
      {detail.contactActions.filter((a) => a.available).length > 0 && (
        <div className="flex items-center gap-2 mb-3">
          {detail.contactActions.filter((a) => a.available).map((action) => (
            <a
              key={action.type}
              href={action.type === "call" ? `tel:${action.target}` : `mailto:${action.target}`}
              onClick={() => onContactLaunched?.("outbound", action.type === "call" ? "phone" : "email")}
              className="flex items-center gap-1.5 rounded-md border border-slate-300 px-3 py-1.5 text-sm font-medium text-slate-700 hover:bg-slate-50"
            >
              {action.type === "call"
                ? <Phone className="h-3.5 w-3.5" />
                : <Mail className="h-3.5 w-3.5" />}
              {action.type === "call" ? "Call" : "Email"}
            </a>
          ))}
        </div>
      )}

      {detail.description && (
        <p className="text-sm text-slate-700 mb-3 whitespace-pre-wrap">{detail.description}</p>
      )}

      {detail.currentStatusText && (
        <p className="text-sm text-slate-600 italic mb-3">"{detail.currentStatusText}"</p>
      )}

      <p className="text-xs text-slate-400">Created {formatDate(detail.createdAtUtc)}</p>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Read-only metadata (right rail / below header on mobile)
// ---------------------------------------------------------------------------

interface MetadataSectionProps {
  detail: KeepRequestDetailResult;
}

function MetadataSection({ detail }: MetadataSectionProps) {
  const responsible = detail.participants.find((p) => p.participationType === "responsible" && !p.detachedAtUtc);
  const watchers = detail.participants.filter((p) => p.participationType === "watching" && !p.detachedAtUtc);

  return (
    <div className="space-y-4">
      {/* Participation */}
      <div>
        <p className="text-xs font-semibold text-slate-500 uppercase tracking-wide mb-2">Assigned / Watching</p>
        {responsible ? (
          <div className="flex items-center gap-2 text-sm text-slate-700 mb-1">
            <User className="h-3.5 w-3.5 text-slate-400 shrink-0" />
            <span>{responsible.displayName}</span>
            <span className="text-xs text-slate-400">responsible</span>
          </div>
        ) : (
          <p className="text-sm text-amber-600 font-medium">Unassigned</p>
        )}
        {watchers.length > 0 && (
          <p className="text-xs text-slate-500 mt-1">
            {watchers.map((w) => w.displayName).join(", ")} watching
          </p>
        )}
      </div>

      {/* Attention */}
      {detail.attentionLevel !== "none" && (
        <div>
          <p className="text-xs font-semibold text-slate-500 uppercase tracking-wide mb-2">Attention</p>
          <p className="text-sm text-slate-700">
            {detail.attentionReason?.replace(/_/g, " ") ?? detail.attentionLevel}
          </p>
          {detail.attentionSinceUtc && (
            <p className="text-xs text-slate-400 mt-0.5">Since {formatEventTime(detail.attentionSinceUtc)}</p>
          )}
        </div>
      )}

      {/* Follow-up / planned */}
      {(detail.followUpOnDate || detail.plannedForDate) && (
        <div>
          <p className="text-xs font-semibold text-slate-500 uppercase tracking-wide mb-2">Scheduling</p>
          {detail.followUpOnDate && (
            <p className="text-sm text-slate-700">Follow up: {detail.followUpOnDate}</p>
          )}
          {detail.plannedForDate && (
            <p className="text-sm text-slate-700">Planned: {detail.plannedForDate}</p>
          )}
        </div>
      )}

      {/* Customer page activity */}
      {detail.customerPageLastViewedAtUtc && (
        <div>
          <p className="text-xs font-semibold text-slate-500 uppercase tracking-wide mb-1">Customer Tracker</p>
          <p className="text-xs text-slate-500">
            Last viewed {formatEventTime(detail.customerPageLastViewedAtUtc)}
            {detail.customerPageViewedAfterLatestUpdate === false && (
              <span className="text-amber-600 font-medium"> · Not seen since last update</span>
            )}
          </p>
        </div>
      )}
    </div>
  );
}

// ---------------------------------------------------------------------------
// Acknowledge attention
// ---------------------------------------------------------------------------

interface AcknowledgeAttentionSectionProps {
  requestId: string;
  detail: KeepRequestDetailResult;
  onDetailUpdated: (updated: KeepRequestDetailResult) => void;
}

function AcknowledgeAttentionSection({ requestId, detail, onDetailUpdated }: AcknowledgeAttentionSectionProps) {
  const { canAcknowledgeAttention } = detail.availableActions;
  const { acknowledgeReasonMaxLength } = detail.validation;

  const [reason, setReason] = useState("");
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [conflictDisabled, setConflictDisabled] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (!canAcknowledgeAttention) return null;

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
        setError("Could not acknowledge. Try again.");
      }
    } finally {
      setIsSubmitting(false);
    }
  }

  return (
    <div className="rounded-lg border border-orange-200 bg-orange-50 p-4">
      <p className="text-xs font-semibold text-orange-800 mb-3 uppercase tracking-wide">Acknowledge Attention</p>
      {error && (
        <div className={`mb-3 rounded-md p-3 text-xs ${conflictDisabled ? "bg-amber-50 border border-amber-200 text-amber-800" : "bg-red-50 border border-red-200 text-red-700"}`}>
          {error}
        </div>
      )}
      <form onSubmit={(e) => void handleSubmit(e)} className="space-y-3">
        <div>
          <label className="block text-xs font-medium text-orange-800 mb-1">Reason (required)</label>
          <textarea
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            maxLength={acknowledgeReasonMaxLength}
            disabled={conflictDisabled}
            placeholder="Describe why attention is being acknowledged…"
            rows={2}
            className="w-full rounded-md border border-orange-200 bg-white px-3 py-2 text-sm text-slate-800 resize-none placeholder:text-slate-400 disabled:opacity-60 focus:outline-none focus:ring-2 focus:ring-orange-300"
          />
        </div>
        <button
          type="submit"
          disabled={!canSubmit}
          className="w-full rounded-md bg-orange-700 px-3 py-2 text-sm font-medium text-white hover:bg-orange-800 disabled:opacity-40"
        >
          {isSubmitting ? "Acknowledging…" : "Acknowledge Attention"}
        </button>
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

function FeedbackReviewSection({ requestId, detail, onDetailUpdated }: FeedbackReviewSectionProps) {
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
  ) return null;

  const ageBucket = detail.feedbackReviewAgeBucket;
  const ageLabel =
    ageBucket === "overdue" ? "Overdue"
    : ageBucket === "aging" ? "Aging"
    : ageBucket === "new" ? "New"
    : null;
  const ageBadgeClass =
    ageBucket === "overdue"
      ? "bg-red-100 text-red-700"
      : ageBucket === "aging"
        ? "bg-amber-100 text-amber-700"
        : "bg-slate-100 text-slate-600";

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
    <div className="rounded-lg border border-slate-200 bg-slate-50 p-4">
      <div className="flex items-center gap-2 mb-3">
        <p className="text-xs font-semibold text-slate-700 uppercase tracking-wide">Customer left negative feedback</p>
        {ageLabel && (
          <span className={`text-xs font-medium px-1.5 py-0.5 rounded ${ageBadgeClass}`}>
            {ageLabel}
          </span>
        )}
      </div>
      {detail.feedbackCommentVisible && detail.feedbackComment && (
        <p className="text-sm text-slate-600 mb-3 italic">&ldquo;{detail.feedbackComment}&rdquo;</p>
      )}
      {error && (
        <div className={`mb-3 rounded-md p-3 text-xs ${conflictDisabled ? "bg-amber-50 border border-amber-200 text-amber-800" : "bg-red-50 border border-red-200 text-red-700"}`}>
          {error}
        </div>
      )}
      <form onSubmit={(e) => void handleSubmit(e)} className="space-y-3">
        <div>
          <label className="block text-xs font-medium text-slate-700 mb-1">Internal note (optional)</label>
          <textarea
            value={note}
            onChange={(e) => setNote(e.target.value)}
            maxLength={feedbackReviewNoteMaxLength}
            disabled={conflictDisabled}
            placeholder="Add an internal note about this feedback…"
            rows={2}
            className="w-full rounded-md border border-slate-300 bg-white px-3 py-2 text-sm text-slate-800 resize-none placeholder:text-slate-400 disabled:opacity-60 focus:outline-none focus:ring-2 focus:ring-slate-400"
          />
        </div>
        <p className="text-xs text-slate-500">Marking as reviewed does not reopen this request.</p>
        <button
          type="submit"
          disabled={isSubmitting || conflictDisabled}
          className="w-full rounded-md bg-slate-900 px-3 py-2 text-sm font-medium text-white hover:bg-slate-700 disabled:opacity-40"
        >
          {isSubmitting ? "Marking…" : "Mark reviewed"}
        </button>
      </form>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Participation controls
// ---------------------------------------------------------------------------

interface ParticipationSectionProps {
  requestId: string;
  detail: KeepRequestDetailResult;
  onDetailUpdated: (updated: KeepRequestDetailResult) => void;
}

function ParticipationSection({ requestId, detail, onDetailUpdated }: ParticipationSectionProps) {
  const { canWatch, canUnwatch, canMute, canUnmute, canAssignResponsible } = detail.availableActions;

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

  if (!canWatch && !canUnwatch && !canMute && !canUnmute && !canAssignResponsible) return null;

  async function act(key: string, fn: () => Promise<KeepRequestDetailResult>) {
    if (submitting) return;
    setSubmitting(key);
    setError(null);
    try {
      const updated = await fn();
      onDetailUpdated(updated);
    } catch (e) {
      if (e instanceof ApiError && e.status === 409) {
        setError("Request updated by another team member. Refresh to retry.");
      } else {
        setError("Action failed. Try again.");
      }
    } finally {
      setSubmitting(null);
    }
  }

  return (
    <div className="rounded-lg border border-slate-100 bg-slate-50 p-4">
      <p className="text-xs font-semibold text-slate-700 mb-3 uppercase tracking-wide">Participation</p>
      {error && (
        <p className="mb-2 rounded-md p-2 text-xs bg-red-50 border border-red-200 text-red-700">{error}</p>
      )}

      {/* Admin: responsible assignment */}
      {canAssignResponsible && (
        <div className="mb-3">
          <p className="text-xs text-slate-500 mb-1.5">Responsible</p>
          {responsible ? (
            <div className="flex items-center justify-between gap-2">
              <span className="text-sm text-slate-700">{responsible.displayName}</span>
              <button
                type="button"
                disabled={!!submitting}
                onClick={() => void act("clear-responsible", () => api.clearResponsible(requestId, detail.version))}
                className="text-xs text-slate-500 underline hover:text-slate-900 disabled:opacity-50"
              >
                {submitting === "clear-responsible" ? "Clearing…" : "Clear"}
              </button>
            </div>
          ) : (
            <div className="flex gap-2">
              <select
                value={assignUserId}
                onChange={(e) => setAssignUserId(e.target.value)}
                disabled={!!submitting}
                className="flex-1 min-w-0 rounded-md border border-slate-300 bg-white px-2 py-1.5 text-xs text-slate-800 disabled:opacity-60 focus:outline-none focus:ring-1 focus:ring-slate-400"
              >
                <option value="">Select member…</option>
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
                className="rounded-md bg-slate-900 px-2.5 py-1.5 text-xs font-medium text-white hover:bg-slate-700 disabled:opacity-50"
              >
                {submitting === "assign-responsible" ? "Assigning…" : "Assign"}
              </button>
            </div>
          )}
        </div>
      )}

      {/* Admin: watcher management */}
      {canAssignResponsible && (
        <div className="mb-3">
          <p className="text-xs text-slate-500 mb-1.5">Watchers</p>
          {watchers.length === 0 && <p className="text-xs text-slate-400 mb-1.5">No watchers</p>}
          {watchers.map((w) => (
            <div key={w.accountUserId} className="flex items-center justify-between gap-2 mb-1">
              <span className="text-xs text-slate-700">{w.displayName}</span>
              <button
                type="button"
                disabled={!!submitting}
                onClick={() =>
                  void act(`remove-watcher-${w.accountUserId}`, () =>
                    api.removeWatcher(requestId, w.accountUserId, detail.version),
                  )
                }
                className="text-xs text-slate-500 underline hover:text-slate-900 disabled:opacity-50"
              >
                {submitting === `remove-watcher-${w.accountUserId}` ? "Removing…" : "Remove"}
              </button>
            </div>
          ))}
          {addableWatchers.length > 0 && (
            <div className="flex gap-2 mt-1.5">
              <select
                value={addWatcherUserId}
                onChange={(e) => setAddWatcherUserId(e.target.value)}
                disabled={!!submitting}
                className="flex-1 min-w-0 rounded-md border border-slate-300 bg-white px-2 py-1.5 text-xs text-slate-800 disabled:opacity-60 focus:outline-none focus:ring-1 focus:ring-slate-400"
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
                className="rounded-md bg-slate-900 px-2.5 py-1.5 text-xs font-medium text-white hover:bg-slate-700 disabled:opacity-50"
              >
                {submitting === "add-watcher" ? "Adding…" : "Add"}
              </button>
            </div>
          )}
        </div>
      )}

      {/* Self: watch/mute controls */}
      <div className="flex flex-col gap-2">
        {canWatch && (
          <button
            type="button"
            disabled={!!submitting}
            onClick={() => void act("watch", () => api.selfWatch(requestId, detail.version))}
            className="w-full rounded-md border border-slate-300 px-3 py-2 text-sm font-medium text-slate-700 bg-white hover:bg-slate-50 disabled:opacity-60 text-left"
          >
            {submitting === "watch" ? "Watching…" : "Watch this request"}
          </button>
        )}
        {canUnwatch && (
          <button
            type="button"
            disabled={!!submitting}
            onClick={() => void act("unwatch", () => api.selfUnwatch(requestId, detail.version))}
            className="w-full rounded-md border border-slate-300 px-3 py-2 text-sm font-medium text-slate-700 bg-white hover:bg-slate-50 disabled:opacity-60 text-left"
          >
            {submitting === "unwatch" ? "Unwatching…" : "Stop watching"}
          </button>
        )}
        {canMute && (
          <button
            type="button"
            disabled={!!submitting}
            onClick={() => void act("mute", () => api.mute(requestId, detail.version))}
            className="w-full rounded-md border border-slate-300 px-3 py-2 text-sm font-medium text-slate-700 bg-white hover:bg-slate-50 disabled:opacity-60 text-left"
          >
            {submitting === "mute" ? "Muting…" : "Mute notifications"}
          </button>
        )}
        {canUnmute && (
          <button
            type="button"
            disabled={!!submitting}
            onClick={() => void act("unmute", () => api.unmute(requestId, detail.version))}
            className="w-full rounded-md border border-slate-300 px-3 py-2 text-sm font-medium text-slate-700 bg-white hover:bg-slate-50 disabled:opacity-60 text-left"
          >
            {submitting === "unmute" ? "Unmuting…" : "Unmute notifications"}
          </button>
        )}
      </div>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Business update composer
// ---------------------------------------------------------------------------

const BUSINESS_UPDATE_EXCLUDED_STATUSES = new Set(["closed", "cancelled", "spam", "test"]);
const BUSINESS_UPDATE_CONFLICT_MESSAGE =
  "This request was updated. Refresh to see the latest state. Your message is saved here.";

interface BusinessUpdateSectionProps {
  requestId: string;
  detail: KeepRequestDetailResult;
  onDetailUpdated: (updated: KeepRequestDetailResult) => void;
  draft: string;
  onDraftChange: (v: string) => void;
  draftStatus: string;
  onDraftStatusChange: (v: string) => void;
}

function BusinessUpdateSection({ requestId, detail, onDetailUpdated, draft, onDraftChange, draftStatus, onDraftStatusChange }: BusinessUpdateSectionProps) {
  const { canSendBusinessUpdate, canChangeStatus, allowedStatuses } = detail.availableActions;
  const maxLength = detail.validation.businessUpdateMaxLength;

  const message = draft;
  const setMessage = onDraftChange;
  const selectedStatus = draftStatus;
  const setSelectedStatus = onDraftStatusChange;
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [conflictDisabled, setConflictDisabled] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (!canSendBusinessUpdate) return null;

  const composerStatuses = allowedStatuses.filter(
    (s) => !BUSINESS_UPDATE_EXCLUDED_STATUSES.has(s),
  );
  const showStatusDropdown = canChangeStatus && composerStatuses.length > 0;
  const remaining = maxLength - message.length;
  const overLimit = remaining < 0;
  const canSubmit = message.trim().length > 0 && !overLimit && !isSubmitting && !conflictDisabled;

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!canSubmit) return;
    setIsSubmitting(true);
    setError(null);
    try {
      const body: { message: string; setStatus?: string } = { message: message.trim() };
      if (selectedStatus) body.setStatus = selectedStatus;
      const updated = await api.postBusinessUpdate(requestId, body, detail.version);
      onDetailUpdated(updated);
      setMessage("");
      setSelectedStatus("");
    } catch (e) {
      if (e instanceof ApiError && e.status === 409) {
        setConflictDisabled(true);
        setError(BUSINESS_UPDATE_CONFLICT_MESSAGE);
      } else {
        setError("Could not send update. Try again.");
      }
    } finally {
      setIsSubmitting(false);
    }
  }

  return (
    <div className="rounded-lg border border-slate-100 bg-slate-50 p-4">
      <p className="text-xs font-semibold text-slate-700 mb-3 uppercase tracking-wide">Send Customer Update</p>
      {detail.needsShare && (
        <p className="mb-3 text-xs text-amber-700">Tracker link not yet shared with customer.</p>
      )}
      {error && (
        <div className={`mb-3 rounded-md p-3 text-xs ${conflictDisabled ? "bg-amber-50 border border-amber-200 text-amber-800" : "bg-red-50 border border-red-200 text-red-700"}`}>
          {error}
        </div>
      )}
      <form onSubmit={(e) => void handleSubmit(e)} className="space-y-3">
        <div>
          <textarea
            value={message}
            onChange={(e) => { setMessage(e.target.value); if (error && !conflictDisabled) setError(null); }}
            disabled={conflictDisabled}
            placeholder="Write an update for the customer…"
            rows={4}
            className={`w-full rounded-md border px-3 py-2 text-sm text-slate-800 resize-none placeholder:text-slate-400 disabled:opacity-60 focus:outline-none focus:ring-2 ${overLimit ? "border-red-400 focus:ring-red-400" : "border-slate-300 focus:ring-slate-400"}`}
          />
          <p className={`mt-1 text-xs text-right ${overLimit ? "text-red-600 font-medium" : remaining <= 50 ? "text-amber-600" : "text-slate-400"}`}>
            {overLimit ? `${Math.abs(remaining)} over limit` : `${remaining} remaining`}
          </p>
        </div>
        {showStatusDropdown && (
          <select
            value={selectedStatus}
            onChange={(e) => setSelectedStatus(e.target.value)}
            disabled={conflictDisabled}
            className="w-full rounded-md border border-slate-300 bg-white px-3 py-2 text-sm text-slate-800 disabled:opacity-60 focus:outline-none focus:ring-2 focus:ring-slate-400"
          >
            <option value="">No status change</option>
            {composerStatuses.map((s) => (
              <option key={s} value={s}>{statusLabel(s)}</option>
            ))}
          </select>
        )}
        <p className="text-xs text-slate-500">
          Visible on the customer tracker.
          {selectedStatus && ` Status will change to ${statusLabel(selectedStatus)}.`}
        </p>
        <button
          type="submit"
          disabled={!canSubmit}
          className="w-full rounded-md bg-slate-800 px-3 py-2 text-sm font-medium text-white hover:bg-slate-700 disabled:opacity-40"
        >
          {isSubmitting ? "Sending…" : "Send update"}
        </button>
      </form>
    </div>
  );
}

// ---------------------------------------------------------------------------
// RequestDetail page
// ---------------------------------------------------------------------------

interface RequestDetailProps {
  requestId: string;
  onBack: () => void;
}

export function RequestDetail({ requestId, onBack }: RequestDetailProps) {
  const [shareCleared, setShareCleared] = useState(false);
  const [contactModal, setContactModal] = useState<{ direction: string; channel: string } | null>(null);
  const [businessUpdateDraft, setBusinessUpdateDraft] = useState("");
  const [businessUpdateDraftStatus, setBusinessUpdateDraftStatus] = useState("");
  const queryClient = useQueryClient();

  const { data: detail, isLoading, isError, error } = useQuery({
    queryKey: ["request-detail", requestId],
    queryFn: () => api.getRequestDetail(requestId),
  });

  const needsShareEffective = !shareCleared && (detail?.needsShare ?? false);
  const canShare = detail?.availableActions.canRecordShareIntent ?? false;

  function handleShareCleared() {
    setShareCleared(true);
    void queryClient.invalidateQueries({ queryKey: ["request-detail", requestId] });
  }

  function handleDetailUpdated(updated: KeepRequestDetailResult) {
    queryClient.setQueryData(["request-detail", requestId], updated);
    setShareCleared(false);
  }

  function handleContactLaunched(direction: string, channel: string) {
    setContactModal({ direction, channel });
  }

  return (
    <div className="flex flex-col h-full">
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

      {/* Mobile NeedsShare banner — sticky, above everything */}
      {detail && needsShareEffective && canShare && (
        <NeedsShareBanner
          requestId={requestId}
          pageToken={detail.pageToken}
          onCleared={handleShareCleared}
        />
      )}

      {/* Back breadcrumb + page header */}
      <div className="flex items-center gap-2 px-4 py-3 bg-white border-b border-slate-200 shrink-0">
        <button
          type="button"
          onClick={onBack}
          className="flex items-center gap-1 text-sm text-slate-500 hover:text-slate-900 -ml-1"
        >
          <ChevronLeft className="h-4 w-4" />
          Requests
        </button>
        {detail && (
          <span className="text-sm text-slate-400 font-mono ml-1">{detail.referenceCode}</span>
        )}
      </div>

      {/* Loading */}
      {isLoading && (
        <div className="flex flex-1 items-center justify-center">
          <span className="text-slate-400 text-sm">Loading…</span>
        </div>
      )}

      {/* Error */}
      {isError && (
        <div className="flex flex-1 items-center justify-center">
          <span className="text-slate-500 text-sm">
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
        <div className="flex flex-1 min-h-0 overflow-hidden">
          {/* Left / main column */}
          <div className="flex-1 overflow-y-auto px-4 md:px-6 py-5">
            <DetailHeader detail={detail} onContactLaunched={handleContactLaunched} />

            {/* Timeline */}
            <div>
              <p className="text-xs font-semibold text-slate-500 uppercase tracking-wide mb-4">Timeline</p>
              {detail.events.length === 0 ? (
                <p className="text-sm text-slate-400">No activity yet.</p>
              ) : (
                <div>
                  {detail.events.map((event) => (
                    <TimelineEvent key={event.id} event={event} />
                  ))}
                </div>
              )}
            </div>

            {/* Mobile metadata and share controls — below timeline */}
            <div className="md:hidden mt-6 space-y-6 pb-6">
              {canShare && (
                <DesktopShareSection
                  requestId={requestId}
                  pageToken={detail.pageToken}
                  canRecordShareIntent={canShare}
                  needsShare={needsShareEffective}
                  onCleared={handleShareCleared}
                />
              )}
              <BusinessUpdateSection
                requestId={requestId}
                detail={detail}
                onDetailUpdated={handleDetailUpdated}
                draft={businessUpdateDraft}
                onDraftChange={setBusinessUpdateDraft}
                draftStatus={businessUpdateDraftStatus}
                onDraftStatusChange={setBusinessUpdateDraftStatus}
              />
              <StatusChangeSection
                requestId={requestId}
                detail={detail}
                onDetailUpdated={handleDetailUpdated}
              />
              <FeedbackReviewSection
                requestId={requestId}
                detail={detail}
                onDetailUpdated={handleDetailUpdated}
              />
              <AcknowledgeAttentionSection
                requestId={requestId}
                detail={detail}
                onDetailUpdated={handleDetailUpdated}
              />
              <ParticipationSection
                requestId={requestId}
                detail={detail}
                onDetailUpdated={handleDetailUpdated}
              />
              <MetadataSection detail={detail} />
            </div>
          </div>

          {/* Right action rail — desktop only */}
          <aside className="hidden md:flex md:flex-col md:w-72 lg:w-80 md:shrink-0 border-l border-slate-200 bg-white overflow-y-auto px-4 py-5 gap-6">
            <DesktopShareSection
              requestId={requestId}
              pageToken={detail.pageToken}
              canRecordShareIntent={canShare}
              needsShare={needsShareEffective}
              onCleared={handleShareCleared}
            />
            <BusinessUpdateSection
              requestId={requestId}
              detail={detail}
              onDetailUpdated={handleDetailUpdated}
              draft={businessUpdateDraft}
              onDraftChange={setBusinessUpdateDraft}
              draftStatus={businessUpdateDraftStatus}
              onDraftStatusChange={setBusinessUpdateDraftStatus}
            />
            <StatusChangeSection
              requestId={requestId}
              detail={detail}
              onDetailUpdated={handleDetailUpdated}
            />
            <FeedbackReviewSection
              requestId={requestId}
              detail={detail}
              onDetailUpdated={handleDetailUpdated}
            />
            <AcknowledgeAttentionSection
              requestId={requestId}
              detail={detail}
              onDetailUpdated={handleDetailUpdated}
            />
            <ParticipationSection
              requestId={requestId}
              detail={detail}
              onDetailUpdated={handleDetailUpdated}
            />
            <MetadataSection detail={detail} />
          </aside>
        </div>
      )}
    </div>
  );
}
