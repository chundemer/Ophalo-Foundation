import { useMemo, useState } from "react";
import { ChevronDown, ChevronUp, FileText, Mail, MessageSquare, PhoneCall, Share2 } from "lucide-react";
import type { KeepRequestDetailResult, KeepRequestEventItem } from "../../lib/apiClient";
import { FOCUS_RING, eventTypeLabel } from "./helpers";

type CommunicationFilter = "all" | "customer" | "internal";

const COMMUNICATION_EVENT_TYPES = new Set([
  "message_added",
  "internal_note_added",
  "external_contact_logged",
  "share_intent_recorded",
  "feedback_received",
  "notification_confirmed",
]);

function eventKind(event: KeepRequestEventItem): string {
  return event.eventType
    .replace(/([a-z0-9])([A-Z])/g, "$1_$2")
    .replace(/[-\s]+/g, "_")
    .toLowerCase();
}

function actorKind(event: KeepRequestEventItem): string {
  return event.actorType.replace(/([a-z])([A-Z])/g, "$1_$2").toLowerCase();
}

export function isRequestCommunication(event: KeepRequestEventItem): boolean {
  const kind = eventKind(event);
  if (COMMUNICATION_EVENT_TYPES.has(kind)) return true;
  return kind === "status_changed" && !!event.content && !!event.messageIntent;
}

function isInternal(event: KeepRequestEventItem): boolean {
  return eventKind(event) === "internal_note_added" || event.visibility === "internal";
}

function formatDateTime(isoUtc: string): string {
  return new Date(isoUtc).toLocaleString("en-US", {
    month: "short",
    day: "numeric",
    year: "numeric",
    hour: "numeric",
    minute: "2-digit",
  });
}

function sentenceCase(value: string | null | undefined): string | null {
  if (!value) return null;
  return value.replace(/_/g, " ").replace(/^\w/, (character) => character.toUpperCase());
}

function contactChannelLabel(value: string | null | undefined): string | null {
  if (value === "sms" || value === "text_message") return "Text/SMS";
  if (value === "phone" || value === "phone_call") return "Phone call";
  return sentenceCase(value);
}

function entryTitle(event: KeepRequestEventItem): string {
  const kind = eventKind(event);
  if (kind === "message_added") {
    return actorKind(event) === "customer" ? "Customer message" : "Business update";
  }
  if (kind === "status_changed") return "Business update with status change";
  if (kind === "internal_note_added") return "Internal note";
  if (kind === "external_contact_logged") {
    const direction = event.externalContactDirection === "inbound" ? "Inbound" : "Outbound";
    return `${direction} ${contactChannelLabel(event.externalContactChannel) ?? "customer contact"}`;
  }
  if (kind === "share_intent_recorded") return "Customer request page shared";
  if (kind === "notification_confirmed") {
    return `${sentenceCase(event.communicationChannel) ?? "Customer"} notification confirmed`;
  }
  return eventTypeLabel(event.eventType);
}

function actorLabel(event: KeepRequestEventItem, detail: KeepRequestDetailResult): string {
  if (event.actorDisplayName) return event.actorDisplayName;
  if (actorKind(event) === "customer") return detail.customerName;
  if (actorKind(event) === "system") return "System";
  return "Business team member";
}

function contextLabel(event: KeepRequestEventItem): string {
  const kind = eventKind(event);
  if (kind === "internal_note_added") return "Internal only";
  if (kind === "external_contact_logged") return "Contact summary";
  if (kind === "notification_confirmed") return "Delivery confirmation";
  if (event.visibility === "all") return "Customer-visible";
  return sentenceCase(event.visibility) ?? "Request record";
}

function fallbackContent(event: KeepRequestEventItem): string | null {
  const kind = eventKind(event);
  if (kind === "external_contact_logged") {
    const parts = [sentenceCase(event.externalContactOutcome)];
    if (event.externalContactRequiresFollowUp) parts.push("Business follow-up required");
    return parts.filter(Boolean).join(" · ") || "No contact summary was recorded.";
  }
  if (kind === "share_intent_recorded") return "The customer request page was shared.";
  if (kind === "notification_confirmed") return "A team member confirmed that the customer notification was sent.";
  if (kind === "feedback_received") {
    if (event.feedbackWasResolved === true) return "The customer reported that the request was resolved.";
    if (event.feedbackWasResolved === false) return "The customer reported that the request was not resolved.";
  }
  return null;
}

function contentToggleLabel(event: KeepRequestEventItem, expanded: boolean): string {
  if (expanded) return "Show less";
  if (eventKind(event) === "internal_note_added") return "Show full note";
  if (eventKind(event) === "external_contact_logged") return "Show full contact summary";
  return "Show full message";
}

function CommunicationEntry({ event, detail }: { event: KeepRequestEventItem; detail: KeepRequestDetailResult }) {
  const [expanded, setExpanded] = useState(false);
  const content = event.content ?? fallbackContent(event);
  const isLong = !!content && (content.length > 320 || content.split("\n").length > 4);
  const internal = isInternal(event);
  const kind = eventKind(event);
  const actor = actorKind(event);
  const customerAuthored = actor === "customer";
  const contactMetadata = [
    sentenceCase(event.externalContactDirection),
    contactChannelLabel(event.externalContactChannel),
    sentenceCase(event.externalContactOutcome),
    event.externalContactRequiresFollowUp ? "Follow-up required" : null,
  ].filter((value): value is string => !!value);
  const Icon = kind === "internal_note_added"
    ? FileText
    : kind === "external_contact_logged"
      ? PhoneCall
      : kind === "share_intent_recorded"
        ? Share2
        : event.communicationChannel === "email"
          ? Mail
          : MessageSquare;

  return (
    <article className={`rounded-xl border p-4 ${internal ? "border-amber-200 bg-amber-50/60" : customerAuthored ? "border-teal-200 bg-teal-50/50" : "border-[var(--ophalo-border)] bg-[var(--ophalo-card)]"}`}>
      <div className="flex items-start gap-3">
        <div className={`mt-0.5 flex h-9 w-9 shrink-0 items-center justify-center rounded-full ${internal ? "bg-amber-100 text-amber-800" : customerAuthored ? "bg-teal-100 text-teal-800" : "bg-[var(--ophalo-canvas)] text-[var(--keep-accent)]"}`}>
          <Icon className="h-4 w-4" aria-hidden="true" />
        </div>
        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-start justify-between gap-x-3 gap-y-1">
            <div>
              <h3 className="text-sm font-semibold text-[var(--ophalo-ink)]">{entryTitle(event)}</h3>
              <p className="mt-0.5 text-xs text-[var(--ophalo-muted)]">
                <span className="font-semibold text-[var(--ophalo-ink)]">{actorLabel(event, detail)}</span>
                {kind === "external_contact_logged" ? " logged this" : actor === "customer" ? " · Customer" : actor === "account_user" ? " · Business" : ""}
              </p>
            </div>
            <div className="text-right">
              <p className={`text-[11px] font-bold uppercase tracking-wide ${internal ? "text-amber-800" : "text-[var(--keep-accent)]"}`}>{contextLabel(event)}</p>
              <time dateTime={event.occurredAtUtc} className="mt-0.5 block text-xs text-[var(--ophalo-muted)]">{formatDateTime(event.occurredAtUtc)}</time>
            </div>
          </div>

          {kind === "external_contact_logged" && (
            <p className="mt-2 text-xs font-medium text-[var(--ophalo-muted)]">
              {contactMetadata.join(" · ")}
            </p>
          )}

          {content && (
            <div className="mt-3">
              <p className={`whitespace-pre-wrap text-sm leading-6 text-[var(--ophalo-ink)] ${isLong && !expanded ? "line-clamp-4" : ""}`}>
                {content}
              </p>
              {isLong && (
                <button
                  type="button"
                  aria-expanded={expanded}
                  onClick={() => setExpanded((value) => !value)}
                  className={`mt-2 inline-flex items-center gap-1 rounded text-xs font-semibold text-[var(--keep-accent)] hover:underline ${FOCUS_RING}`}
                >
                  {expanded ? <ChevronUp className="h-3.5 w-3.5" aria-hidden="true" /> : <ChevronDown className="h-3.5 w-3.5" aria-hidden="true" />}
                  {contentToggleLabel(event, expanded)}
                </button>
              )}
            </div>
          )}
        </div>
      </div>
    </article>
  );
}

interface RequestCommunicationsWorkspaceProps {
  detail: KeepRequestDetailResult;
  composer: React.ReactNode;
}

export function RequestCommunicationsWorkspace({ detail, composer }: RequestCommunicationsWorkspaceProps) {
  const [filter, setFilter] = useState<CommunicationFilter>("all");
  const events = useMemo(() => {
    const communication = detail.events
      .filter(isRequestCommunication)
      .sort((a, b) => new Date(b.occurredAtUtc).getTime() - new Date(a.occurredAtUtc).getTime());
    if (filter === "internal") return communication.filter(isInternal);
    if (filter === "customer") return communication.filter((event) => !isInternal(event));
    return communication;
  }, [detail.events, filter]);

  return (
    <div data-request-communications-workspace className="space-y-5">
      {composer}

      <section className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] p-4 shadow-sm">
        <div className="flex flex-wrap items-end justify-between gap-3 border-b border-[var(--ophalo-border)] pb-4">
          <div>
            <h2 className="text-base font-semibold text-[var(--ophalo-ink)]">Communication history</h2>
            <p className="mt-0.5 text-xs text-[var(--ophalo-muted)]">Who communicated, what was recorded, and when · newest first</p>
          </div>
          <div className="inline-flex rounded-lg border border-[var(--ophalo-border)]" role="group" aria-label="Communication filter">
            {(["all", "customer", "internal"] as const).map((option, index) => (
              <button
                key={option}
                type="button"
                aria-pressed={filter === option}
                onClick={() => setFilter(option)}
                className={`px-3 py-1.5 text-xs font-semibold capitalize ${index > 0 ? "border-l border-[var(--ophalo-border)]" : ""} ${FOCUS_RING} ${filter === option ? "bg-[var(--ophalo-navy)] text-white" : "text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)]"}`}
              >
                {option}
              </button>
            ))}
          </div>
        </div>

        {events.length === 0 ? (
          <p className="mt-4 rounded-lg bg-[var(--ophalo-canvas)] px-4 py-6 text-sm text-[var(--ophalo-muted)]">
            {filter === "internal" ? "No internal notes yet." : filter === "customer" ? "No customer communication yet." : "No communication or internal notes yet."}
          </p>
        ) : (
          <div className="mt-4 space-y-3">
            {events.map((event) => <CommunicationEntry key={event.id} event={event} detail={detail} />)}
          </div>
        )}
      </section>
    </div>
  );
}
