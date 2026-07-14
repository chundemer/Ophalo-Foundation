import { FileText, Phone, Share2, Check, X, Clock, MessageSquare, User } from "lucide-react";
import { type KeepRequestEventItem } from "../../lib/apiClient";
import { KeepBadge, type KeepBadgeVariant } from "../../components/keep/KeepBadge";
import {
  eventTypeLabel,
  statusLabel,
  formatEventTime,
  formatDateOnly,
  FOLLOW_UP_REASON_LABELS,
} from "./helpers";

// Events shown in "Communication" filter (customer/business communication + lifecycle anchors)
const COMMUNICATION_EVENT_TYPES = new Set([
  "request_created",
  "message_added",
  "internal_note_added",
  "external_contact_logged",
  "share_intent_recorded",
  "feedback_reviewed",
  "feedback_received",
  "request_closed",
  "request_cancelled",
]);

export type TimelineFilter = "communication" | "all";

export function isCommunicationEvent(event: KeepRequestEventItem): boolean {
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
  feedback_received:       { Icon: MessageSquare,  bgClass: "bg-[var(--ophalo-canvas)]",       iconClass: "text-[var(--ophalo-muted)]" },
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
  if (event.eventType === "feedback_received") {
    if (event.feedbackWasResolved === true) return "Customer confirmed request was resolved";
    if (event.feedbackWasResolved === false) return "Customer reported request was not resolved";
  }
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
  isFirst: boolean;
}

export function TimelineEvent({ event, isFirst }: TimelineEventProps) {
  const { label, iconConfig, badgeVariant } = resolveEventDisplay(event);
  const { Icon, bgClass, iconClass } = iconConfig;
  const summary = timelineEventSummary(event);
  const isCommunication = isCommunicationEvent(event);

  return (
    <div
      className={`relative flex gap-3 rounded-lg px-3 py-3 transition-colors ${
        isFirst ? "bg-[var(--keep-accent-bg)]/45" : ""
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
