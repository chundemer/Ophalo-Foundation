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
  Eye,
  FileText,
} from "lucide-react";
import {
  api,
  type KeepRequestDetailResult,
  type KeepRequestEventItem,
  type ShareIntentMethod,
} from "../lib/apiClient";
import { ApiError } from "../lib/apiClient";
import { NeedsShareBanner } from "../components/NeedsShareBanner";
import { KeepButton } from "../components/keep/KeepButton";
import { KeepBadge, type KeepBadgeVariant } from "../components/keep/KeepBadge";

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

// Note: message_added label and treatment depend on actorType at render time —
// "customer" → "Customer message" (teal); "account_user" → "Business update" (success/green).
// messageIntent (string | null) also exists but its values are undocumented in the DTO;
// actorType is the reliable classifier.
const EVENT_TYPE_LABELS: Record<string, string> = {
  request_created: "Request created",
  status_changed: "Status changed",
  request_closed: "Request closed",
  request_cancelled: "Request cancelled",
  internal_note_added: "Internal note",
  attention_acknowledged: "Attention acknowledged",
  external_contact_logged: "External contact",
  participation_changed: "Team update",
  feedback_reviewed: "Feedback reviewed",
  follow_up_on_changed: "Follow-up timing",
  planned_for_changed: "Planned date",
  request_classified: "Request classified",
  share_intent_recorded: "Customer page shared",
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
  if (status === "in_progress") return "Active";
  return status.replace(/_/g, " ").replace(/\b\w/g, (c) => c.toUpperCase());
}

function statusBadgeVariant(status: string): KeepBadgeVariant {
  if (status === "in_progress") return "teal";
  if (status.includes("needs") || status.includes("waiting") || status.includes("reply")) return "attention";
  if (status === "complete") return "success";
  return "default";
}

const FOCUS_RING =
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2";

const INPUT_CLS =
  "w-full rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-3 py-2 text-sm text-[var(--ophalo-ink)] placeholder:text-[var(--ophalo-muted)] disabled:opacity-60 transition-colors focus:outline-none focus:ring-2 focus:ring-[var(--keep-accent)] focus:border-[var(--keep-accent)]";

// ---------------------------------------------------------------------------
// Timeline
// ---------------------------------------------------------------------------

// Always suppress these from the operator timeline (ADR-150)
const ALWAYS_HIDDEN_EVENT_TYPES = new Set(["customer_page_opened"]);

// Events shown in "Communication" filter (customer/business communication + lifecycle anchors)
const COMMUNICATION_EVENT_TYPES = new Set([
  "request_created",
  "message_added",
  "internal_note_added",
  "external_contact_logged",
  "share_intent_recorded",
  "feedback_reviewed",
  "request_closed",
  "request_cancelled",
]);

type TimelineFilter = "communication" | "all";

function isCommunicationEvent(event: KeepRequestEventItem): boolean {
  return COMMUNICATION_EVENT_TYPES.has(event.eventType);
}

interface EventIconConfig {
  Icon: React.ElementType | null;
  bgClass: string;
  iconClass: string;
}

// Static icon configs for event types that don't vary by actor
const EVENT_ICON_MAP: Record<string, EventIconConfig> = {
  internal_note_added:     { Icon: FileText,       bgClass: "bg-[var(--ophalo-canvas)]",       iconClass: "text-[var(--ophalo-muted)]" },
  external_contact_logged: { Icon: Phone,          bgClass: "bg-[var(--keep-accent-bg)]",      iconClass: "text-[var(--keep-accent)]" },
  share_intent_recorded:   { Icon: Share2,         bgClass: "bg-[var(--keep-info-bg)]",        iconClass: "text-[var(--keep-info)]" },
  feedback_reviewed:       { Icon: Check,          bgClass: "bg-[var(--ophalo-success-bg)]",   iconClass: "text-[var(--ophalo-success)]" },
  request_created:         { Icon: Clock,          bgClass: "bg-[var(--ophalo-canvas)]",       iconClass: "text-[var(--ophalo-muted)]" },
  request_closed:          { Icon: Check,          bgClass: "bg-[var(--ophalo-success-bg)]",   iconClass: "text-[var(--ophalo-success)]" },
  request_cancelled:       { Icon: X,              bgClass: "bg-[var(--ophalo-canvas)]",       iconClass: "text-[var(--ophalo-muted)]" },
  status_changed:          { Icon: null,           bgClass: "bg-[var(--ophalo-canvas)]",       iconClass: "text-[var(--ophalo-muted)]" },
  attention_acknowledged:  { Icon: Check,          bgClass: "bg-[var(--ophalo-attention-bg)]", iconClass: "text-[var(--ophalo-attention)]" },
  participation_changed:   { Icon: User,           bgClass: "bg-[var(--ophalo-canvas)]",       iconClass: "text-[var(--ophalo-muted)]" },
  planned_for_changed:     { Icon: Clock,          bgClass: "bg-[var(--ophalo-canvas)]",       iconClass: "text-[var(--ophalo-muted)]" },
  follow_up_on_changed:    { Icon: Clock,          bgClass: "bg-[var(--ophalo-canvas)]",       iconClass: "text-[var(--ophalo-muted)]" },
  request_classified:      { Icon: null,           bgClass: "bg-[var(--ophalo-canvas)]",       iconClass: "text-[var(--ophalo-muted)]" },
};

const DEFAULT_ICON: EventIconConfig = {
  Icon: null,
  bgClass: "bg-[var(--ophalo-canvas)]",
  iconClass: "text-[var(--ophalo-muted)]",
};

// Resolves event display properties — label, icon, badge variant — accounting for actor type.
// message_added: actorType "customer" → Customer message (teal); "account_user" → Business update (success).
// request_created: actorType "customer" → Customer submitted.
interface EventDisplay {
  label: string;
  iconConfig: EventIconConfig;
  badgeVariant: KeepBadgeVariant;
}

interface AttentionGuidance {
  label: string;
  why: string;
  resolveBy: string;
  sourceLabel: string | null;
  sourceText: string | null;
  afterHandled: string | null;
}

function resolveEventDisplay(event: KeepRequestEventItem): EventDisplay {
  if (event.eventType === "message_added") {
    if (event.actorType === "customer") {
      return {
        label: "Customer message",
        iconConfig: { Icon: MessageSquare, bgClass: "bg-[var(--keep-accent-bg)]", iconClass: "text-[var(--keep-accent)]" },
        badgeVariant: "teal",
      };
    }
    return {
      label: "Business update",
      iconConfig: { Icon: MessageSquare, bgClass: "bg-[var(--ophalo-success-bg)]", iconClass: "text-[var(--ophalo-success)]" },
      badgeVariant: "success",
    };
  }
  if (event.eventType === "request_created" && event.actorType === "customer") {
    return {
      label: "Customer submitted",
      iconConfig: EVENT_ICON_MAP.request_created ?? DEFAULT_ICON,
      badgeVariant: "default",
    };
  }
  const staticIcon = EVENT_ICON_MAP[event.eventType] ?? DEFAULT_ICON;
  let badgeVariant: KeepBadgeVariant = "default";
  if (event.eventType === "external_contact_logged") badgeVariant = "teal";
  if (event.eventType === "share_intent_recorded") badgeVariant = "info";
  if (event.eventType === "feedback_reviewed") badgeVariant = "success";
  if (event.eventType === "attention_acknowledged") badgeVariant = "attention";
  return { label: eventTypeLabel(event.eventType), iconConfig: staticIcon, badgeVariant };
}

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
    return "Participation updated";
  }
  if (event.eventType === "external_contact_logged") {
    const dir = event.externalContactDirection ?? "";
    const ch = event.externalContactChannel ?? "";
    const outcome = event.externalContactOutcome;
    const label = `${dir === "inbound" ? "Inbound" : "Outbound"} ${ch}`;
    return outcome ? `${label} — ${outcome.replace(/_/g, " ")}` : label;
  }
  if (event.eventType === "attention_acknowledged") return "Attention acknowledged";
  if (event.eventType === "share_intent_recorded") return "Customer page shared with customer";
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

const ATTENTION_LABELS: Record<string, string> = {
  customer_message: "Customer message",
  update_request: "Update requested",
  schedule_change_request: "Timing change requested",
  change_or_cancel_request: "Change or cancel request",
  complaint: "Complaint",
  first_response_due: "First response due",
  unresolved_feedback: "Feedback needs review",
  call_requested: "Call requested",
  timing_change_requested: "Timing change requested",
  cancellation_requested: "Cancellation requested",
};

function reasonLabel(reason: string): string {
  return ATTENTION_LABELS[reason] ?? reason.replace(/_/g, " ").replace(/\b\w/g, (c) => c.toUpperCase());
}

function latestAttentionSource(detail: KeepRequestDetailResult): KeepRequestEventItem | null {
  const events = [...detail.events]
    .filter((e) => !ALWAYS_HIDDEN_EVENT_TYPES.has(e.eventType))
    .sort((a, b) => new Date(b.occurredAtUtc).getTime() - new Date(a.occurredAtUtc).getTime());

  return (
    events.find((e) => e.actorType === "customer" && e.content) ??
    events.find((e) => e.eventType === "external_contact_logged" && e.externalContactRequiresFollowUp) ??
    events.find((e) => e.eventType === "message_added" && e.content) ??
    null
  );
}

function buildAttentionGuidance(detail: KeepRequestDetailResult): AttentionGuidance | null {
  if (detail.attentionLevel === "none" || !detail.attentionReason) return null;

  const canSendUpdate = detail.availableActions.canSendBusinessUpdate;
  const canLogContact = detail.availableActions.canLogExternalContact;
  const canAcknowledge = detail.availableActions.canAcknowledgeAttention;
  const source = latestAttentionSource(detail);
  const sourceText = source?.content?.trim() || null;
  const sourceLabel =
    sourceText && source?.actorType === "customer"
      ? `${source.actorDisplayName ?? "Customer"} said ${formatEventTime(source.occurredAtUtc)}`
      : sourceText
        ? `Latest related note ${formatEventTime(source!.occurredAtUtc)}`
        : detail.attentionSinceUtc
          ? `Needs attention since ${formatEventTime(detail.attentionSinceUtc)}`
          : null;

  const contactOrUpdate =
    canSendUpdate && canLogContact
      ? "Send a customer-page update, or log contact if you handle it by phone, text, email, or in person."
      : canSendUpdate
        ? "Send a customer-page update."
        : canLogContact
          ? "Log the outside contact after you handle it by phone, text, email, or in person."
          : "Open the request history and decide the next safe handoff.";

  const afterHandled =
    canAcknowledge
      ? "After the customer is taken care of, this stops showing as business-waiting."
      : detail.availableActions.canMarkFeedbackReviewed
        ? "Mark feedback reviewed when Owner/Admin handling is complete."
        : null;

  switch (detail.attentionReason) {
    case "customer_message":
      return {
        label: reasonLabel(detail.attentionReason),
        why: "The customer sent a message and the request is waiting on the business.",
        resolveBy: contactOrUpdate,
        sourceLabel,
        sourceText,
        afterHandled,
      };
    case "update_request":
      return {
        label: reasonLabel(detail.attentionReason),
        why: "The customer is asking for an update, so the next visible business touch matters.",
        resolveBy: contactOrUpdate,
        sourceLabel,
        sourceText,
        afterHandled,
      };
    case "first_response_due":
      return {
        label: reasonLabel(detail.attentionReason),
        why: "This request has not received its first business response inside the response window.",
        resolveBy: canSendUpdate
          ? "Send the first customer-page update, or log a real external contact if you respond outside Keep."
          : "Log the real first contact once you respond outside Keep.",
        sourceLabel,
        sourceText,
        afterHandled,
      };
    case "call_requested":
      return {
        label: reasonLabel(detail.attentionReason),
        why: "The customer asked for a call, so Keep needs a durable record that the contact was handled.",
        resolveBy: canLogContact
          ? "Call using your normal phone workflow, then save a phone contact log."
          : "Call using your normal phone workflow, then add a durable note when available.",
        sourceLabel,
        sourceText,
        afterHandled,
      };
    case "schedule_change_request":
    case "timing_change_requested":
      return {
        label: reasonLabel(detail.attentionReason),
        why: "The customer is asking about timing. Keep should protect the promise without becoming a schedule board.",
        resolveBy: canSendUpdate
          ? "Confirm the timing on the customer page, or log contact if you handle the timing outside Keep."
          : "Confirm timing through your normal channel, then log the contact.",
        sourceLabel,
        sourceText,
        afterHandled,
      };
    case "change_or_cancel_request":
      return {
        label: reasonLabel(detail.attentionReason),
        why: "The customer is asking to change or cancel the request, and the business needs to decide the next promise.",
        resolveBy: "Review the request, then update the customer or log contact. Change status only when the business decision is clear.",
        sourceLabel,
        sourceText,
        afterHandled,
      };
    case "cancellation_requested":
      return {
        label: reasonLabel(detail.attentionReason),
        why: "The customer appears to be asking to cancel.",
        resolveBy: "Confirm the cancellation intent, then update the customer or change status to Cancelled if appropriate.",
        sourceLabel,
        sourceText,
        afterHandled,
      };
    case "complaint":
      return {
        label: reasonLabel(detail.attentionReason),
        why: "The customer raised an issue that needs a deliberate business response.",
        resolveBy: contactOrUpdate,
        sourceLabel,
        sourceText,
        afterHandled,
      };
    case "unresolved_feedback":
      return {
        label: reasonLabel(detail.attentionReason),
        why: "The customer left unresolved feedback after closeout. This is Owner/Admin review work, not a normal acknowledgement.",
        resolveBy: "Review the feedback and customer context. Mark feedback reviewed when the follow-up decision is complete.",
        sourceLabel,
        sourceText,
        afterHandled,
      };
    default:
      return {
        label: reasonLabel(detail.attentionReason),
        why: "This request is waiting on business attention.",
        resolveBy: contactOrUpdate,
        sourceLabel,
        sourceText,
        afterHandled,
      };
  }
}

// ---------------------------------------------------------------------------
// Attention resolution highlights
// ---------------------------------------------------------------------------

type HighlightLevel = "primary" | "secondary";

interface AttentionHighlights {
  sendUpdate?: HighlightLevel;
  logContact?: HighlightLevel;
  workControls?: HighlightLevel;
  feedbackReview?: HighlightLevel;
  markHandled?: HighlightLevel;
}

function getAttentionResolutionHighlights(detail: KeepRequestDetailResult): AttentionHighlights {
  if (detail.attentionLevel === "none" || !detail.attentionReason) return {};
  const canSendUpdate = detail.availableActions.canSendBusinessUpdate;
  const canLogContact = detail.availableActions.canLogExternalContact;
  const canMarkHandled = detail.availableActions.canAcknowledgeAttention;
  switch (detail.attentionReason) {
    case "customer_message":
    case "update_request":
    case "first_response_due":
    case "complaint":
      return {
        sendUpdate: canSendUpdate ? "primary" : undefined,
        logContact: canLogContact ? (canSendUpdate ? "secondary" : "primary") : undefined,
      };
    case "call_requested":
      return {
        logContact: canLogContact ? "primary" : undefined,
        sendUpdate: canSendUpdate ? "secondary" : undefined,
      };
    case "schedule_change_request":
    case "timing_change_requested":
      return {
        sendUpdate: canSendUpdate ? "primary" : undefined,
        logContact: canLogContact ? (canSendUpdate ? "secondary" : "primary") : undefined,
      };
    case "cancellation_requested":
    case "change_or_cancel_request":
      return {
        workControls: "primary",
        sendUpdate: canSendUpdate ? "secondary" : undefined,
        logContact: (!canSendUpdate && canLogContact) ? "secondary" : undefined,
      };
    case "unresolved_feedback":
      return { feedbackReview: "primary" };
    default: {
      if (canSendUpdate) return { sendUpdate: "primary", logContact: canLogContact ? "secondary" : undefined };
      if (canLogContact) return { logContact: "primary" };
      if (canMarkHandled) return { markHandled: "primary" };
      return {};
    }
  }
}

function highlightBorderCls(level?: HighlightLevel): string {
  if (level === "primary") return "border-[var(--ophalo-attention)]";
  if (level === "secondary") return "border-[var(--ophalo-navy)]";
  return "border-[var(--ophalo-border)]";
}

function highlightBgCls(level?: HighlightLevel): string {
  if (level === "primary") return "bg-[var(--ophalo-attention-bg)]";
  return "bg-[var(--ophalo-card)]";
}

function highlightBoxShadow(level?: HighlightLevel): string | undefined {
  if (level === "primary") return "0 0 0 3px color-mix(in srgb, var(--ophalo-attention) 18%, transparent)";
  if (level === "secondary") return "0 0 0 3px color-mix(in srgb, var(--ophalo-navy) 10%, transparent)";
  return undefined;
}

function maxHighlight(...levels: (HighlightLevel | undefined)[]): HighlightLevel | undefined {
  if (levels.includes("primary")) return "primary";
  if (levels.includes("secondary")) return "secondary";
  return undefined;
}

interface TimelineEventProps {
  event: KeepRequestEventItem;
  isFirst: boolean;
}

function TimelineEvent({ event, isFirst }: TimelineEventProps) {
  const { label, iconConfig, badgeVariant } = resolveEventDisplay(event);
  const { Icon, bgClass, iconClass } = iconConfig;
  const summary = timelineEventSummary(event);
  const isCommunication = isCommunicationEvent(event);

  return (
    <div
      className={`relative flex gap-3 rounded-lg px-3 py-3 border transition-colors ${
        isFirst
          ? "border-[var(--keep-accent)] bg-[var(--keep-accent-bg)]"
          : "border-transparent"
      }`}
    >
      {isFirst && (
        <div className="absolute -left-px top-3 h-[calc(100%-1.5rem)] w-1 rounded-r-full bg-[var(--keep-accent)]" />
      )}

      {/* Icon dot */}
      <div
        className={`mt-0.5 flex h-8 w-8 shrink-0 items-center justify-center rounded-full border border-[var(--ophalo-border)] ${bgClass}`}
      >
        {Icon ? (
          <Icon className={`h-3.5 w-3.5 ${iconClass}`} />
        ) : (
          <div
            className={`h-2 w-2 rounded-full ${
              isCommunication ? "bg-[var(--ophalo-muted)]" : "bg-[var(--ophalo-border)]"
            }`}
          />
        )}
      </div>

      {/* Content */}
      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-center gap-1.5 mb-1">
          {isFirst && (
            <span className="rounded-full bg-[var(--keep-accent)] px-2 py-0.5 text-[10px] font-semibold leading-none text-white">
              Latest
            </span>
          )}
          <KeepBadge variant={badgeVariant}>{label}</KeepBadge>
          {event.actorDisplayName && (
            <span className="text-xs text-[var(--ophalo-muted)]">{event.actorDisplayName}</span>
          )}
        </div>
        {summary && (
          <p
            className={`text-sm leading-5 ${
              isCommunication ? "text-[var(--ophalo-ink)] font-medium" : "text-[var(--ophalo-muted)]"
            }`}
          >
            {summary}
          </p>
        )}
        {event.content && (
          <p
            className={`text-sm leading-6 mt-0.5 whitespace-pre-wrap ${
              event.visibility === "internal"
                ? "text-[var(--ophalo-muted)] italic"
                : "text-[var(--ophalo-ink)]"
            }`}
          >
            {event.content}
          </p>
        )}
        <p className="mt-1.5 text-xs text-[var(--ophalo-muted)]">{formatEventTime(event.occurredAtUtc)}</p>
      </div>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Customer page sharing (secondary)
// ---------------------------------------------------------------------------

interface DesktopShareSectionProps {
  requestId: string;
  pageToken: string;
  canRecordShareIntent: boolean;
  needsShare: boolean;
  onCleared: () => void;
}

function DesktopShareSection({
  requestId,
  pageToken,
  canRecordShareIntent,
  needsShare,
  onCleared,
}: DesktopShareSectionProps) {
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [copied, setCopied] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const publicBaseUrl = import.meta.env.VITE_PUBLIC_BASE_URL as string;
  const customerPageUrl = `${publicBaseUrl}/keep/r/${pageToken}`;
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
      await navigator.clipboard.writeText(customerPageUrl);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    });
  }

  async function handleNativeShare() {
    await submit("native_share", async () => {
      await navigator.share({ url: customerPageUrl, title: "Customer Request Page" });
    });
  }

  async function handleMarkShared() {
    await submit("manual_mark_shared");
  }

  return (
    <div
      className={`rounded-xl px-4 py-4 border ${
        needsShare
          ? "border-[var(--ophalo-border)] bg-[var(--ophalo-attention-bg)]"
          : "border-[var(--ophalo-border)] bg-[var(--ophalo-card)]"
      }`}
    >
      <div className="flex items-center justify-between mb-3">
        <p className="text-base font-semibold text-[var(--ophalo-ink)]">Customer page</p>
        {needsShare && <KeepBadge variant="attention">Not shared</KeepBadge>}
      </div>
      {needsShare && (
        <p className="text-xs text-[var(--ophalo-attention)] font-medium mb-3">
          Share the customer page so they can follow request updates.
        </p>
      )}
      <div className="flex flex-col gap-2">
        {canNativeShare && (
          <KeepButton
            variant="secondary"
            onClick={() => void handleNativeShare()}
            disabled={isSubmitting}
            className="w-full gap-2"
          >
            <Share2 className="h-3.5 w-3.5 shrink-0" />
            Share page
          </KeepButton>
        )}
        <KeepButton
          variant="secondary"
          onClick={() => void handleCopyLink()}
          disabled={isSubmitting}
          className="w-full gap-2"
        >
          {copied ? (
            <Check className="h-3.5 w-3.5 shrink-0 text-[var(--ophalo-success)]" />
          ) : (
            <Copy className="h-3.5 w-3.5 shrink-0" />
          )}
          {copied ? "Copied!" : "Copy page link"}
        </KeepButton>
        {needsShare && (
          <button
            type="button"
            onClick={() => void handleMarkShared()}
            disabled={isSubmitting}
            className={`text-xs text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] text-left disabled:opacity-60 transition-colors ${FOCUS_RING}`}
          >
            Mark as shared manually
          </button>
        )}
      </div>
      {error && <p className="mt-2 text-xs text-[var(--ophalo-danger)]">{error}</p>}
    </div>
  );
}

// ---------------------------------------------------------------------------
// Change status
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
  const canSubmit =
    selectedStatus !== "" &&
    !isSubmitting &&
    !conflictDisabled &&
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
    <div>
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
        <label htmlFor="status-select" className="block text-sm font-semibold text-[var(--ophalo-ink)]">
          Change status
        </label>
        <select
          id="status-select"
          value={selectedStatus}
          onChange={(e) => {
            setSelectedStatus(e.target.value);
            setError(null);
          }}
          disabled={conflictDisabled}
          className={INPUT_CLS}
        >
          <option value="">Select new status…</option>
          {allowedStatuses.map((s) => (
            <option key={s} value={s}>{statusLabel(s)}</option>
          ))}
        </select>
        <div>
          <label htmlFor="status-message" className="sr-only">
            {messageRequired ? "Message (required)" : "Message (optional)"}
          </label>
          <textarea
            id="status-message"
            value={message}
            onChange={(e) => setMessage(e.target.value)}
            maxLength={statusMessageMaxLength}
            disabled={conflictDisabled}
            placeholder={messageRequired ? "Message required for this status…" : "Optional note…"}
            rows={2}
            className={`${INPUT_CLS} resize-none`}
          />
        </div>
        <KeepButton type="submit" variant="primary" disabled={!canSubmit} className="w-full">
          {isSubmitting ? "Updating…" : "Update status"}
        </KeepButton>
      </form>
    </div>
  );
}

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
  const hasAttention = detail.attentionLevel !== "none" && !!detail.attentionReason;
  if (!canLogExternalContact || !hasAttention) return null;
  const contactChannel = detail.customerPhone ? "phone" : detail.customerEmail ? "email" : "other";
  const shadow = highlightBoxShadow(highlight);
  return (
    <div
      className={`rounded-xl border px-5 py-4 transition-[border-color,background-color,box-shadow] ${highlightBorderCls(highlight)} ${highlightBgCls(highlight)}`}
      style={shadow ? { boxShadow: shadow } : undefined}
    >
      <p className="text-sm font-semibold text-[var(--ophalo-ink)] mb-1">Handled outside Keep?</p>
      <p className="text-xs text-[var(--ophalo-muted)] mb-3">
        Log a call, text, email, or in-person conversation.
      </p>
      <KeepButton
        type="button"
        variant="secondary"
        onClick={() => onContactLaunched("outbound", contactChannel)}
        className="w-full"
      >
        Log customer contact
      </KeepButton>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Already handled? — acknowledge attention card
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
        setError("Could not mark handled. Try again.");
      }
    } finally {
      setIsSubmitting(false);
    }
  }

  const shadow = highlightBoxShadow(highlight);

  return (
    <div
      className={`rounded-xl border px-5 py-4 transition-[border-color,background-color,box-shadow] ${highlightBorderCls(highlight)} ${highlightBgCls(highlight)}`}
      style={shadow ? { boxShadow: shadow } : undefined}
    >
      <p className="text-sm font-semibold text-[var(--ophalo-ink)] mb-1">Already handled?</p>
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
            Brief note before marking handled
          </label>
          <textarea
            id="ack-reason"
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            maxLength={acknowledgeReasonMaxLength}
            disabled={conflictDisabled}
            placeholder="Example: Called Kelley and confirmed Thursday morning."
            rows={2}
            className={`${INPUT_CLS} resize-none`}
          />
        </div>
        <KeepButton type="submit" variant="secondary" disabled={!canSubmit} className="w-full">
          {isSubmitting ? "Marking handled…" : "Mark attention handled"}
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
// Work controls group — status + attention + feedback in one card
// ---------------------------------------------------------------------------

interface WorkControlsGroupProps {
  requestId: string;
  detail: KeepRequestDetailResult;
  onDetailUpdated: (updated: KeepRequestDetailResult) => void;
  highlights: AttentionHighlights;
}

function WorkControlsGroup({ requestId, detail, onDetailUpdated, highlights }: WorkControlsGroupProps) {
  const hasStatus = detail.availableActions.allowedStatuses.length > 0;
  const hasFeedback =
    detail.availableActions.canMarkFeedbackReviewed &&
    detail.feedbackWasResolved === false &&
    detail.feedbackReviewedAtUtc == null;

  if (!hasStatus && !hasFeedback) return null;

  const cardHighlight = maxHighlight(highlights.workControls, highlights.feedbackReview);
  const shadow = highlightBoxShadow(cardHighlight);

  return (
    <div
      id="work-controls"
      className={`rounded-xl border overflow-hidden scroll-mt-4 transition-[border-color,box-shadow] bg-[var(--ophalo-card)] ${highlightBorderCls(cardHighlight)}`}
      style={shadow ? { boxShadow: shadow } : undefined}
    >
      {hasStatus && (
        <div className={`px-5 py-4 transition-colors ${highlights.workControls === "primary" ? "bg-[var(--ophalo-attention-bg)]" : ""}`}>
          <StatusChangeSection requestId={requestId} detail={detail} onDetailUpdated={onDetailUpdated} />
        </div>
      )}
      {hasFeedback && (
        <>
          {hasStatus && <div className="h-px bg-[var(--ophalo-border)]" />}
          <div className={`px-5 py-4 transition-colors ${highlights.feedbackReview === "primary" ? "bg-[var(--ophalo-attention-bg)]" : ""}`}>
            <FeedbackReviewSection requestId={requestId} detail={detail} onDetailUpdated={onDetailUpdated} />
          </div>
        </>
      )}
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
  const [direction, setDirection] = useState(initialDirection);
  const [channel, setChannel] = useState(initialChannel);
  const [outcome, setOutcome] = useState("");
  const [requiresFollowUp, setRequiresFollowUp] = useState(false);
  const [summary, setSummary] = useState("");
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [conflictDisabled, setConflictDisabled] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [phoneCopied, setPhoneCopied] = useState(false);

  const isOutbound = direction === "outbound";
  const maxSummary = detail.validation.externalContactSummaryMaxLength;
  const showPhone = channel === "phone" && !!detail.customerPhone;

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (isSubmitting || conflictDisabled) return;
    setIsSubmitting(true);
    setError(null);
    try {
      const body: {
        direction: string;
        channel: string;
        outcome?: string;
        requiresBusinessFollowUp?: boolean;
        summary?: string;
      } = { direction, channel };
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
        className="bg-[var(--ophalo-card)] rounded-xl shadow-xl w-full max-w-md p-5"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-center justify-between mb-1">
          <h2 className="text-base font-semibold text-[var(--ophalo-ink)]">Log contact</h2>
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

        {/* Phone number + secondary utilities — only for phone channel */}
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
        <form onSubmit={(e) => void handleSubmit(e)} className="space-y-3">
          <div className="grid grid-cols-2 gap-3">
            <div>
              <label htmlFor="log-direction" className="block text-xs font-medium text-[var(--ophalo-ink)] mb-1">
                Direction
              </label>
              <select
                id="log-direction"
                value={direction}
                onChange={(e) => {
                  setDirection(e.target.value);
                  setOutcome("");
                  setRequiresFollowUp(false);
                }}
                disabled={conflictDisabled}
                className={INPUT_CLS}
              >
                <option value="outbound">Outbound</option>
                <option value="inbound">Inbound</option>
              </select>
            </div>
            <div>
              <label htmlFor="log-channel" className="block text-xs font-medium text-[var(--ophalo-ink)] mb-1">
                Channel
              </label>
              <select
                id="log-channel"
                value={channel}
                onChange={(e) => setChannel(e.target.value)}
                disabled={conflictDisabled}
                className={INPUT_CLS}
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
              <label htmlFor="log-outcome" className="block text-xs font-medium text-[var(--ophalo-ink)] mb-1">
                Outcome (optional)
              </label>
              <select
                id="log-outcome"
                value={outcome}
                onChange={(e) => setOutcome(e.target.value)}
                disabled={conflictDisabled}
                className={INPUT_CLS}
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
            <label className="flex items-center gap-2 text-sm text-[var(--ophalo-ink)]">
              <input
                type="checkbox"
                checked={requiresFollowUp}
                onChange={(e) => setRequiresFollowUp(e.target.checked)}
                disabled={conflictDisabled}
                className={`rounded border-[var(--ophalo-border)] ${FOCUS_RING}`}
              />
              Requires business follow-up
            </label>
          )}
          <div>
            <label htmlFor="log-summary" className="block text-xs font-medium text-[var(--ophalo-ink)] mb-1">
              Summary (optional)
            </label>
            <textarea
              id="log-summary"
              value={summary}
              onChange={(e) => setSummary(e.target.value)}
              maxLength={maxSummary}
              disabled={conflictDisabled}
              placeholder="Brief notes about this contact…"
              rows={3}
              className={`${INPUT_CLS} resize-none`}
            />
          </div>
          <div className="flex gap-2 pt-1">
            <KeepButton type="button" variant="secondary" onClick={onClose} disabled={isSubmitting} className="flex-1">
              Cancel
            </KeepButton>
            <KeepButton type="submit" variant="primary" disabled={isSubmitting || conflictDisabled} className="flex-1">
              {isSubmitting ? "Saving…" : "Save log"}
            </KeepButton>
          </div>
        </form>
      </div>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Hero card — identity anchor
// ---------------------------------------------------------------------------

interface DetailHeroProps {
  detail: KeepRequestDetailResult;
}

function DetailHero({ detail }: DetailHeroProps) {
  const hasAttention = detail.attentionLevel !== "none";

  // ADR-150: customer page viewed info shown in header badges
  const pageViewedInfo = useMemo(() => {
    if (detail.customerPageLastViewedAtUtc) {
      return {
        text: `Viewed ${formatEventTime(detail.customerPageLastViewedAtUtc)}`,
        isAmber: detail.customerPageViewedAfterLatestUpdate === false,
      };
    }
    if (!detail.needsShare) {
      return { text: "Not yet viewed", isAmber: true };
    }
    return null;
  }, [detail.customerPageLastViewedAtUtc, detail.customerPageViewedAfterLatestUpdate, detail.needsShare]);

  return (
    <div className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-5 py-5 shadow-sm">
      {/* Badge row */}
      <div className="flex flex-wrap items-center gap-2 mb-3">
        <span className="font-mono text-xs text-[var(--ophalo-muted)]">{detail.referenceCode}</span>
        <KeepBadge variant={statusBadgeVariant(detail.status)}>{statusLabel(detail.status)}</KeepBadge>
        {hasAttention && detail.attentionReason && (
          <KeepBadge variant="attention">
            {detail.attentionLevel === "overdue" ? (
              <AlertTriangle className="h-3 w-3 mr-1 shrink-0" />
            ) : (
              <Clock className="h-3 w-3 mr-1 shrink-0" />
            )}
            {detail.attentionReason.replace(/_/g, " ")}
          </KeepBadge>
        )}
        {pageViewedInfo && (
          <span
            className={`inline-flex items-center gap-1 text-xs ${
              pageViewedInfo.isAmber ? "text-[var(--ophalo-attention)]" : "text-[var(--ophalo-muted)]"
            }`}
          >
            <Eye className="h-3 w-3 shrink-0" />
            {pageViewedInfo.text}
          </span>
        )}
      </div>

      {/* Customer name — page type anchor */}
      <h1 className="font-serif text-[26px] font-semibold leading-tight text-[var(--ophalo-ink)]">
        {detail.customerName}
      </h1>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Original request card — context below the hero
// ---------------------------------------------------------------------------

interface OriginalRequestCardProps {
  detail: KeepRequestDetailResult;
  onContactLaunched: (direction: string, channel: string) => void;
}

function OriginalRequestCard({ detail, onContactLaunched }: OriginalRequestCardProps) {
  return (
    <div className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-5 py-4">
      {detail.description && (
        <p className="text-sm leading-6 text-[var(--ophalo-ink)] whitespace-pre-wrap mb-3">
          {detail.description}
        </p>
      )}

      {/* Contact info + launchers */}
      <div className="flex flex-wrap items-center gap-2">
        {detail.customerPhone && (
          <span className="flex items-center gap-1.5 text-sm text-[var(--ophalo-muted)]">
            <Phone className="h-3.5 w-3.5 shrink-0" />
            {detail.customerPhone}
          </span>
        )}
        {detail.customerEmail && (
          <span className="flex items-center gap-1.5 text-sm text-[var(--ophalo-muted)]">
            <Mail className="h-3.5 w-3.5 shrink-0" />
            {detail.customerEmail}
          </span>
        )}
        {detail.contactActions
          .filter((a) => a.available)
          .map((action) =>
            action.type === "call" ? (
              // Phone: open log modal only — no tel: launch here.
              // Secondary tel: and copy utilities live inside the modal (ADR-PWA-phone).
              <button
                key={action.type}
                type="button"
                onClick={() => onContactLaunched("outbound", "phone")}
                className={`inline-flex min-h-[32px] items-center gap-1.5 rounded-full border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-3 text-xs font-semibold text-[var(--ophalo-ink)] hover:border-[var(--keep-accent)] hover:bg-[var(--keep-accent-bg)] hover:text-[var(--keep-accent)] transition-colors ${FOCUS_RING}`}
              >
                <Phone className="h-3 w-3 text-[var(--keep-accent)] shrink-0" />
                Log phone contact
              </button>
            ) : (
              <a
                key={action.type}
                href={`mailto:${action.target}`}
                onClick={() => onContactLaunched("outbound", "email")}
                className={`inline-flex min-h-[32px] items-center gap-1.5 rounded-full border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-3 text-xs font-semibold text-[var(--ophalo-ink)] hover:border-[var(--keep-accent)] hover:bg-[var(--keep-accent-bg)] hover:text-[var(--keep-accent)] transition-colors ${FOCUS_RING}`}
              >
                <Mail className="h-3 w-3 text-[var(--keep-accent)] shrink-0" />
                Email
              </a>
            ),
          )}
      </div>

      <p className="text-xs text-[var(--ophalo-muted)] mt-3">
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
    if (highlights.logContact === "primary") return "Log customer contact";
    if (highlights.workControls === "primary") return "Status / work controls";
    if (highlights.feedbackReview === "primary") return "Review feedback";
    if (highlights.markHandled === "primary") return "Mark attention handled";
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
                  ? "Recommended: Log customer contact using the highlighted panel on the right."
                  : "Use the highlighted panel on the right."}
            </p>
          </div>
        )}
      </div>
    </section>
  );
}

// ---------------------------------------------------------------------------
// Send customer update (primary communication action)
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
  highlight?: HighlightLevel;
}

function BusinessUpdateSection({
  requestId,
  detail,
  onDetailUpdated,
  draft,
  onDraftChange,
  draftStatus,
  onDraftStatusChange,
  highlight,
}: BusinessUpdateSectionProps) {
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
    <div
      id="customer-update"
      className={`rounded-xl border px-5 py-5 scroll-mt-4 transition-[border-color,background-color,box-shadow] ${highlightBorderCls(highlight)} ${highlightBgCls(highlight)}`}
      style={{ boxShadow: highlightBoxShadow(highlight) }}
    >
      <p className="text-base font-semibold text-[var(--ophalo-ink)] mb-3">Send customer update</p>
      {detail.needsShare && (
        <p className="mb-3 text-xs text-[var(--ophalo-attention)]">
          Customer page not yet shared — the customer won't see this until you share it.
        </p>
      )}
      {error && (
        <div
          aria-live="polite"
          className={`mb-3 rounded-lg p-3 text-xs ${
            conflictDisabled
              ? "bg-[var(--ophalo-attention-bg)] text-[var(--ophalo-attention)]"
              : "bg-[var(--ophalo-danger-bg)] text-[var(--ophalo-danger)]"
          }`}
        >
          {error}
        </div>
      )}
      <form onSubmit={(e) => void handleSubmit(e)} className="space-y-3">
        <div>
          <label htmlFor="business-update-message" className="sr-only">
            Customer update message
          </label>
          <textarea
            id="business-update-message"
            value={message}
            onChange={(e) => {
              setMessage(e.target.value);
              if (error && !conflictDisabled) setError(null);
            }}
            disabled={conflictDisabled}
            placeholder="Write an update for the customer…"
            rows={4}
            className={`${INPUT_CLS} resize-none ${
              overLimit
                ? "border-[var(--ophalo-danger)] focus:ring-[var(--ophalo-danger)] focus:border-[var(--ophalo-danger)]"
                : ""
            }`}
          />
          <p
            className={`mt-1 text-xs text-right ${
              overLimit
                ? "text-[var(--ophalo-danger)] font-medium"
                : remaining <= 50
                  ? "text-[var(--ophalo-attention)]"
                  : "text-[var(--ophalo-muted)]"
            }`}
          >
            {overLimit ? `${Math.abs(remaining)} over limit` : `${remaining} remaining`}
          </p>
        </div>
        {showStatusDropdown && (
          <div>
            <label htmlFor="update-status-select" className="sr-only">
              Also change status (optional)
            </label>
            <select
              id="update-status-select"
              value={selectedStatus}
              onChange={(e) => setSelectedStatus(e.target.value)}
              disabled={conflictDisabled}
              className={INPUT_CLS}
            >
              <option value="">No status change</option>
              {composerStatuses.map((s) => (
                <option key={s} value={s}>{statusLabel(s)}</option>
              ))}
            </select>
          </div>
        )}
        <p className="text-xs text-[var(--ophalo-muted)]">
          Visible on the customer page.
          {selectedStatus && ` Status will change to ${statusLabel(selectedStatus)}.`}
        </p>
        <KeepButton type="submit" variant="teal" disabled={!canSubmit} className="w-full">
          {isSubmitting ? "Sending…" : "Send update"}
        </KeepButton>
      </form>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Team section — participation + team context (merged, quieter)
// ---------------------------------------------------------------------------

interface TeamSectionProps {
  requestId: string;
  detail: KeepRequestDetailResult;
  onDetailUpdated: (updated: KeepRequestDetailResult) => void;
}

function TeamSection({ requestId, detail, onDetailUpdated }: TeamSectionProps) {
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

  const hasTiming = detail.followUpOnDate || detail.plannedForDate;
  const hasTeamContent =
    canWatch || canUnwatch || canMute || canUnmute || canAssignResponsible ||
    responsible || watchers.length > 0 || hasTiming;

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

      {/* Timing */}
      {hasTiming && (
        <div>
          <p className="text-xs text-[var(--ophalo-muted)] mb-1">Timing</p>
          {detail.followUpOnDate && (
            <p className="text-xs text-[var(--ophalo-ink)]">
              Follow up: {formatDateOnly(detail.followUpOnDate)}
            </p>
          )}
          {detail.plannedForDate && (
            <p className="text-xs text-[var(--ophalo-ink)]">
              Planned: {formatDateOnly(detail.plannedForDate)}
            </p>
          )}
        </div>
      )}
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

  const highlights = useMemo(
    () => (detail ? getAttentionResolutionHighlights(detail) : {}),
    [detail],
  );

  const workControlsIsHighlighted = !!(highlights.workControls || highlights.feedbackReview);

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
        {canShare && (
          <DesktopShareSection
            requestId={requestId}
            pageToken={detail.pageToken}
            canRecordShareIntent={canShare}
            needsShare={needsShareEffective}
            onCleared={handleShareCleared}
          />
        )}
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

      {/* Mobile NeedsShare banner */}
      {detail && needsShareEffective && canShare && (
        <NeedsShareBanner
          requestId={requestId}
          pageToken={detail.pageToken}
          onCleared={handleShareCleared}
        />
      )}

      {/* Back breadcrumb */}
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
        <div className="flex flex-1 min-h-0 overflow-hidden">
          {/* Left / main column */}
          <div className="flex-1 overflow-y-auto px-4 md:px-6 py-5 space-y-4">
            {/* Hero: identity + status */}
            <DetailHero detail={detail} />

            {/* Original request: description, contact, timestamp */}
            <OriginalRequestCard
              detail={detail}
              onContactLaunched={handleContactLaunched}
            />

            {/* Needs attention: why it is here + how to handle it */}
            <AttentionGuidanceCard
              detail={detail}
              highlights={highlights}
            />

            {/* Mobile: primary actions appear before the timeline */}
            <div className="md:hidden space-y-4">
              {renderPrimaryActions()}
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
                    Communication
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
                    ? "No customer or contact updates yet."
                    : "No activity yet."}
                </p>
              ) : (
                <div className="relative space-y-1 border-l border-[var(--ophalo-border)] pl-3 ml-4">
                  {displayedEvents.map((event, idx) => (
                    <TimelineEvent key={event.id} event={event} isFirst={idx === 0} />
                  ))}
                </div>
              )}
            </div>

            {/* Mobile: team context after timeline */}
            <div className="md:hidden pb-6">
              {renderTeamSection()}
            </div>
          </div>

          {/* Right action rail — desktop only */}
          <aside className="hidden md:flex md:flex-col md:w-72 lg:w-80 md:shrink-0 border-l border-[var(--ophalo-border)] bg-[var(--ophalo-card)] overflow-y-auto px-4 py-5 gap-4">
            {/* Resolution cards: primary actions for clearing attention */}
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
            {workControlsIsHighlighted && (
              <WorkControlsGroup
                requestId={requestId}
                detail={detail}
                onDetailUpdated={handleDetailUpdated}
                highlights={highlights}
              />
            )}

            {/* Utilities: customer page, status, team context */}
            <div className="space-y-3">
              <p className="px-1 text-[10px] font-semibold uppercase tracking-widest text-[var(--ophalo-muted)]">Utilities</p>
              {canShare && (
                <DesktopShareSection
                  requestId={requestId}
                  pageToken={detail.pageToken}
                  canRecordShareIntent={canShare}
                  needsShare={needsShareEffective}
                  onCleared={handleShareCleared}
                />
              )}
              {!workControlsIsHighlighted && (
                <WorkControlsGroup
                  requestId={requestId}
                  detail={detail}
                  onDetailUpdated={handleDetailUpdated}
                  highlights={highlights}
                />
              )}
              {renderTeamSection()}
            </div>
          </aside>
        </div>
      )}
    </div>
  );
}
