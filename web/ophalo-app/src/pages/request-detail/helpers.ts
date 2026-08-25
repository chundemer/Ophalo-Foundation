import { type KeepRequestDetailResult, type KeepRequestEventItem } from "../../lib/apiClient";
import { statusLabel, statusBadgeVariant } from "../../lib/requestStatus";
import { DESCRIPTION_MAX_LENGTH } from "../../components/quick-capture/utils";

export { statusLabel, statusBadgeVariant };

// Note: message_added label and treatment depend on actorType at render time —
// "customer" → "Customer message" (teal); "account_user" → "Business update" (success/green).
// messageIntent (string | null) also exists but its values are undocumented in the DTO;
// actorType is the reliable classifier.
export const EVENT_TYPE_LABELS: Record<string, string> = {
  request_created: "Request created",
  status_changed: "Status changed",
  request_closed: "Request closed",
  request_cancelled: "Request cancelled",
  internal_note_added: "Internal note",
  attention_acknowledged: "Attention acknowledged",
  external_contact_logged: "External contact",
  participation_changed: "Team update",
  feedback_reviewed: "Feedback reviewed",
  feedback_received: "Customer feedback",
  follow_up_on_changed: "Follow-up timing",
  planned_for_changed: "Planned date",
  request_classified: "Request classified",
  share_intent_recorded: "Customer page shared",
  service_location_changed: "Service location updated",
};

export function eventTypeLabel(type: string): string {
  return EVENT_TYPE_LABELS[type] ?? type.replace(/_/g, " ").replace(/\b\w/g, (c) => c.toUpperCase());
}

export function formatEventTime(isoUtc: string): string {
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

export function formatDate(isoUtc: string): string {
  return new Date(isoUtc).toLocaleDateString("en-US", {
    month: "short",
    day: "numeric",
    year: "numeric",
    hour: "numeric",
    minute: "2-digit",
  });
}

export function formatDateOnly(isoDate: string): string {
  const [year, month, day] = isoDate.split("-").map(Number);
  return new Date(year, month - 1, day).toLocaleDateString("en-US", {
    month: "short",
    day: "numeric",
    year: "numeric",
  });
}

export function isDateOnlyToday(isoDate: string | null): boolean {
  if (!isoDate) return false;
  const now = new Date();
  const todayStr = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, "0")}-${String(now.getDate()).padStart(2, "0")}`;
  return isoDate === todayStr;
}

export function isDateOnlyPast(isoDate: string | null): boolean {
  if (!isoDate) return false;
  const now = new Date();
  const todayStr = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, "0")}-${String(now.getDate()).padStart(2, "0")}`;
  return isoDate < todayStr;
}

export function isDueOrOverdueFollowUp(isoDate: string | null): boolean {
  return isDateOnlyToday(isoDate) || isDateOnlyPast(isoDate);
}

export const COMPLETION_REASON_LABELS: Record<string, string> = {
  customer_contacted: "Customer contacted",
  work_completed: "Follow-up work done",
  no_longer_needed: "No longer needed",
  other: "Other",
};

export const FOLLOW_UP_REASON_LABELS: Record<string, string> = {
  weather: "Weather",
  parts: "Waiting on parts",
  customer_delay: "Waiting on customer",
  business_operator_availability: "Need to schedule",
  third_party: "Third party",
  other: "Other",
};

export const FOCUS_RING =
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2";

export const INPUT_CLS =
  "w-full rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-3 py-2 text-base md:text-sm text-[var(--ophalo-ink)] placeholder:text-[var(--ophalo-muted)] disabled:opacity-60 transition-colors focus:outline-none focus:ring-2 focus:ring-[var(--keep-accent)] focus:border-[var(--keep-accent)]";

export const STATUS_CONFLICT_MESSAGE =
  "This request has been updated by another team member. Copy your unsaved notes and refresh the workbench to load the latest history.";

export type NotifyChannel = "sms" | "email";

// GAP-052b / ADR-451: this only picks a default — the operator can always override it.
export function suggestedNotifyChannel(detail: KeepRequestDetailResult): NotifyChannel {
  return detail.customerEmail && detail.contactPreference === "email" ? "email" : "sms";
}

export function notifyChannelLabel(channel: NotifyChannel): string {
  return channel === "sms" ? "text" : "email";
}

/**
 * Builds a follow-up description that keeps `prefix + copiedText` within
 * DESCRIPTION_MAX_LENGTH, truncating only the copied original text — never the prefix —
 * at the last whitespace boundary within the available room, with a hard cut as fallback.
 * Reserves room for an ellipsis whenever the copied text is actually cut.
 */
export function buildFollowUpDescription(
  prefix: string,
  originalText: string,
): { description: string; wasTruncated: boolean } {
  const available = DESCRIPTION_MAX_LENGTH - prefix.length;
  if (originalText.length <= available) {
    return { description: prefix + originalText, wasTruncated: false };
  }

  const ellipsis = "…";
  const room = available - ellipsis.length;
  const hardCut = originalText.slice(0, room);
  const lastWhitespace = hardCut.search(/\s+\S*$/);
  const cutText = lastWhitespace > 0 ? hardCut.slice(0, lastWhitespace) : hardCut;

  return { description: prefix + cutText + ellipsis, wasTruncated: true };
}

// Always suppress these from the operator timeline (ADR-150)
export const ALWAYS_HIDDEN_EVENT_TYPES = new Set(["customer_page_opened"]);

export const ATTENTION_LABELS: Record<string, string> = {
  customer_message: "Customer message",
  update_request: "Update requested",
  schedule_change_request: "Timing change requested",
  change_or_cancel_request: "Change or cancel request",
  complaint: "Complaint",
  first_response_due: "First response due",
  follow_up_due: "Follow up due",
  unresolved_feedback: "Feedback needs review",
  call_requested: "Call requested",
  timing_change_requested: "Timing change requested",
  cancellation_requested: "Cancellation requested",
};

export function reasonLabel(reason: string): string {
  return ATTENTION_LABELS[reason] ?? reason.replace(/_/g, " ").replace(/\b\w/g, (c) => c.toUpperCase());
}

export function latestAttentionSource(detail: KeepRequestDetailResult): KeepRequestEventItem | null {
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

export interface AttentionGuidance {
  label: string;
  why: string;
  resolveBy: string;
  sourceLabel: string | null;
  sourceText: string | null;
  afterHandled: string | null;
}

export function buildAttentionGuidance(detail: KeepRequestDetailResult): AttentionGuidance | null {
  const attention = detail.effectiveAttention;
  if (attention.level === "none" || !attention.reason) return null;

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

  switch (attention.reason) {
    case "customer_message":
      return {
        label: reasonLabel(attention.reason),
        why: "The customer sent a message and the request is waiting on the business.",
        resolveBy: contactOrUpdate,
        sourceLabel,
        sourceText,
        afterHandled,
      };
    case "update_request":
      return {
        label: reasonLabel(attention.reason),
        why: "The customer is asking for an update, so the next visible business touch matters.",
        resolveBy: contactOrUpdate,
        sourceLabel,
        sourceText,
        afterHandled,
      };
    case "first_response_due":
      return {
        label: reasonLabel(attention.reason),
        why: "This request has not received its first business response inside the response window.",
        resolveBy: canLogContact
          ? "Contact the customer directly, then log the real external contact in Keep. You can also add a customer-page update afterward."
          : "Log the real first contact once you respond outside Keep.",
        sourceLabel,
        sourceText,
        afterHandled,
      };
    case "follow_up_due":
      return {
        label: reasonLabel(attention.reason),
        why: "The customer follow-up promise is due and needs a deliberate resolution.",
        resolveBy: attention.dueOnDate
          ? `Resolve by ${formatDateOnly(attention.dueOnDate)}.`
          : "Complete, move, or keep the customer follow-up promise.",
        sourceLabel,
        sourceText,
        afterHandled,
      };
    case "call_requested":
      return {
        label: reasonLabel(attention.reason),
        why: "The customer asked for a call, so Keep needs a durable record that the contact was handled.",
        resolveBy: canLogContact
          ? "Call using your normal phone workflow, then save an external contact log."
          : "Call using your normal phone workflow, then add a durable note when available.",
        sourceLabel,
        sourceText,
        afterHandled,
      };
    case "schedule_change_request":
    case "timing_change_requested":
      return {
        label: reasonLabel(attention.reason),
        why: "The customer is asking about timing. Keep should protect the promise without becoming a schedule board.",
        resolveBy: "Contact the customer by phone, text, or email, then save what happened in Keep. A customer-page update alone does not notify them.",
        sourceLabel,
        sourceText,
        afterHandled,
      };
    case "change_or_cancel_request":
      return {
        label: reasonLabel(attention.reason),
        why: "The customer is asking to change or cancel the request, and the business needs to decide the next promise.",
        resolveBy: "Review the request, then update the customer, log contact, or use the status selector when the business decision is clear.",
        sourceLabel,
        sourceText,
        afterHandled,
      };
    case "cancellation_requested":
      return {
        label: reasonLabel(attention.reason),
        why: "The customer appears to be asking to cancel.",
        resolveBy: "Confirm the cancellation intent, then update the customer or change status to Cancelled if appropriate.",
        sourceLabel,
        sourceText,
        afterHandled,
      };
    case "complaint":
      return {
        label: reasonLabel(attention.reason),
        why: "The customer raised an issue that needs a deliberate business response.",
        resolveBy: contactOrUpdate,
        sourceLabel,
        sourceText,
        afterHandled,
      };
    case "unresolved_feedback":
      return {
        label: reasonLabel(attention.reason),
        why: "The customer left unresolved feedback after closeout. This is Owner/Admin review work, not a normal acknowledgement.",
        resolveBy: "Review the feedback and customer context. Mark feedback reviewed when the follow-up decision is complete.",
        sourceLabel,
        sourceText,
        afterHandled,
      };
    default:
      return {
        label: reasonLabel(attention.reason),
        why: "This request is waiting on business attention.",
        resolveBy: contactOrUpdate,
        sourceLabel,
        sourceText,
        afterHandled,
      };
  }
}
