"use client";

import { useEffect, useRef, useState } from "react";
import {
  AlertCircle, AlertTriangle, ArrowRight, Bell, Calendar,
  Clock, Lock, MessageCircle, Paperclip, Phone, RefreshCw, Send, ShieldCheck,
} from "lucide-react";
import { KeepBadge, type KeepBadgeVariant } from "@/components/keep/KeepBadge";
import { KeepButton } from "@/components/keep/KeepButton";

// ─── Types ──────────────────────────────────────────────────────────────────

export interface CustomerEventItem {
  eventType: string;
  content: string | null;
  occurredAtUtc: string;
  actorLabel: string;
}

export interface CustomerPageData {
  businessName: string;
  referenceCode: string;
  status: string;
  description: string | null;
  currentStatusText: string | null;
  isTerminal: boolean | null;
  events: CustomerEventItem[] | null;
  allowedActions: string[] | null;
  version: string | null;
}

// ─── Constants ──────────────────────────────────────────────────────────────

const MESSAGE_ACTIONS = [
  "question",
  "update_request",
  "information_added",
  "call_requested",
  "timing_change_requested",
  "cancellation_requested",
] as const;

const ACTION_LABELS: Record<string, string> = {
  question: "Ask a question",
  update_request: "Request an update",
  information_added: "Add information",
  call_requested: "Request a call",
  timing_change_requested: "Timing change",
  cancellation_requested: "Request cancellation",
};

const ACTION_ICONS: Record<string, typeof AlertCircle> = {
  question: MessageCircle,
  update_request: RefreshCw,
  information_added: Paperclip,
  call_requested: Phone,
  timing_change_requested: Calendar,
  cancellation_requested: AlertTriangle,
};

// ─── Helpers ────────────────────────────────────────────────────────────────

function statusHeadline(status: string): string {
  switch (status) {
    case "received": return "Request Received";
    case "scheduled": return "Scheduled";
    case "in_progress": return "In Progress";
    case "pending_customer": return "Waiting for Your Reply";
    case "resolved": return "Request Resolved";
    case "closed": return "Closed";
    case "cancelled": return "Cancelled";
    default: return "Request Status";
  }
}

function statusBadgeVariant(status: string): KeepBadgeVariant {
  switch (status) {
    case "received":
    case "scheduled":
    case "in_progress": return "teal";
    case "pending_customer": return "attention";
    case "resolved": return "success";
    default: return "default";
  }
}

function statusChipLabel(status: string): string {
  switch (status) {
    case "received": return "Received";
    case "scheduled": return "Scheduled";
    case "in_progress": return "In progress";
    case "pending_customer": return "Needs your reply";
    case "resolved": return "Resolved";
    case "closed": return "Closed";
    case "cancelled": return "Cancelled";
    default: return status;
  }
}

function eventBadgeVariant(ev: CustomerEventItem): KeepBadgeVariant {
  if (ev.actorLabel === "customer") return "teal";
  if (ev.actorLabel === "business") return "success";
  if (ev.eventType === "request_cancelled" || ev.eventType === "request_closed") return "attention";
  return "default";
}

function eventBadgeLabel(ev: CustomerEventItem): string {
  if (ev.actorLabel === "customer") return "You";
  if (ev.actorLabel === "business") return "Business";
  switch (ev.eventType) {
    case "request_created": return "Created";
    case "status_changed": return "Status update";
    case "request_closed": return "Closed";
    case "request_cancelled": return "Cancelled";
    default: return "Update";
  }
}

function eventFallbackContent(eventType: string): string {
  switch (eventType) {
    case "request_created": return "Request created.";
    case "status_changed": return "Status updated.";
    case "request_closed": return "Request closed.";
    case "request_cancelled": return "Request cancelled.";
    case "attention_acknowledged": return "Message acknowledged.";
    default: return "";
  }
}

const ICON_CONFIGS = {
  customer:  { bg: "bg-[var(--keep-accent-bg)]",       iconColor: "text-[var(--keep-accent)]",       Icon: Send         },
  business:  { bg: "bg-[var(--ophalo-success-bg)]",    iconColor: "text-[var(--ophalo-success)]",    Icon: Bell         },
  attention: { bg: "bg-[var(--ophalo-attention-bg)]",  iconColor: "text-[var(--ophalo-attention)]",  Icon: AlertCircle  },
  default:   { bg: "bg-muted",                         iconColor: "text-muted-foreground",           Icon: Clock        },
} as const;

function eventIconConfig(ev: CustomerEventItem) {
  if (ev.actorLabel === "customer") return ICON_CONFIGS.customer;
  if (ev.actorLabel === "business") return ICON_CONFIGS.business;
  if (ev.eventType === "request_cancelled" || ev.eventType === "request_closed") return ICON_CONFIGS.attention;
  return ICON_CONFIGS.default;
}

function formatDate(iso: string): string {
  const d = new Date(iso);
  return d.toLocaleDateString("en-US", { month: "short", day: "numeric", year: "numeric" });
}

async function parseErrorCode(res: Response): Promise<string | null> {
  const body: unknown = await res.json().catch(() => null);
  if (body != null && typeof body === "object") {
    const ext = (body as Record<string, unknown>).extensions;
    if (ext != null && typeof ext === "object") {
      const code = (ext as Record<string, unknown>).code;
      if (typeof code === "string") return code;
    }
  }
  return null;
}

function errorMessageForCode(code: string | null, status: number): string {
  if (status === 429) return "Please wait a moment and try again.";
  switch (code) {
    case "KeepRequest.RequestChanged":
      return "The page has changed since you loaded it. Please refresh to see the latest updates.";
    case "KeepRequest.CustomerMessageTooLong":
      return "Message is too long (4,000 character limit).";
    case "KeepRequest.FeedbackCommentTooLong":
      return "Comment is too long (2,000 character limit).";
    case "KeepRequest.FeedbackUnavailable":
    case "KeepRequest.FeedbackAlreadySubmitted":
      return "Feedback is no longer available for this request.";
    case "KeepRequest.OffSeasonUnavailable":
    case "KeepRequest.TerminalState":
      return "This action is not available right now.";
    default:
      return "Message not sent. Please try again.";
  }
}

// ─── Main component ─────────────────────────────────────────────────────────

type ComposerPhase =
  | { kind: "idle" }
  | { kind: "composing"; action: string }
  | { kind: "submitting"; action: string }
  | { kind: "sent" }
  | { kind: "feedback" }
  | { kind: "submitting_feedback" }
  | { kind: "feedback_sent" };

export function CustomerTrackerView({
  initialPage,
  pageToken,
}: {
  initialPage: CustomerPageData;
  pageToken: string;
}) {
  const [page, setPage] = useState(initialPage);
  const [phase, setPhase] = useState<ComposerPhase>({ kind: "idle" });
  const [message, setMessage] = useState("");
  const [wasResolved, setWasResolved] = useState<boolean | null>(null);
  const [comment, setComment] = useState("");
  const [errorMsg, setErrorMsg] = useState<string | null>(null);
  const [expired, setExpired] = useState(false);
  const dismissTimer = useRef<ReturnType<typeof setTimeout> | null>(null);

  const apiBase = process.env.NEXT_PUBLIC_API_BASE_URL ?? "";
  const events = [...(page.events ?? [])].reverse();
  const allowedActions = page.allowedActions ?? [];

  const availableMessageActions = allowedActions.filter(
    (a) => (MESSAGE_ACTIONS as readonly string[]).includes(a)
  );
  const hasFeedbackAction = allowedActions.includes("feedback");
  const hasActions = availableMessageActions.length > 0 || hasFeedbackAction;

  const selectedAction =
    phase.kind === "composing" || phase.kind === "submitting"
      ? phase.action
      : null;
  const isSubmitting =
    phase.kind === "submitting" || phase.kind === "submitting_feedback";

  // Find most recent business-authored event with content for the continuity card
  const latestBusinessUpdate = (page.events ?? [])
    .slice()
    .reverse()
    .find((e) => e.actorLabel === "business" && e.content !== null) ?? null;

  // Auto-dismiss success confirmation after 5s
  useEffect(() => {
    if (phase.kind === "sent" || phase.kind === "feedback_sent") {
      dismissTimer.current = setTimeout(() => {
        setPhase({ kind: "idle" });
      }, 5000);
    }
    return () => {
      if (dismissTimer.current) clearTimeout(dismissTimer.current);
    };
  }, [phase.kind]);

  async function submitMessage(action: string) {
    const trimmed = message.trim();
    if (!trimmed) {
      setErrorMsg("Message can't be empty.");
      return;
    }
    setErrorMsg(null);
    setPhase({ kind: "submitting", action });

    try {
      const res = await fetch(
        `${apiBase}/keep/r/${encodeURIComponent(pageToken)}/${action}`,
        {
          method: "POST",
          headers: {
            "Content-Type": "application/json",
            "X-Keep-Request-Version": page.version ?? "",
          },
          body: JSON.stringify({ message: trimmed }),
        }
      );

      if (res.ok) {
        const updated: unknown = await res.json().catch(() => null);
        if (updated != null && typeof updated === "object") {
          setPage(updated as CustomerPageData);
        }
        setMessage("");
        setPhase({ kind: "sent" });
        return;
      }

      if (res.status === 410) {
        setExpired(true);
        return;
      }

      const code = await parseErrorCode(res);
      setErrorMsg(errorMessageForCode(code, res.status));
      setPhase({ kind: "composing", action });
    } catch {
      setErrorMsg("…check your connection and try again.");
      setPhase({ kind: "composing", action });
    }
  }

  async function submitFeedback() {
    if (wasResolved === null) {
      setErrorMsg("Please indicate whether your request was resolved.");
      return;
    }
    setErrorMsg(null);
    setPhase({ kind: "submitting_feedback" });

    try {
      const res = await fetch(
        `${apiBase}/keep/r/${encodeURIComponent(pageToken)}/feedback`,
        {
          method: "POST",
          headers: {
            "Content-Type": "application/json",
            "X-Keep-Request-Version": page.version ?? "",
          },
          body: JSON.stringify({ wasResolved, comment: comment.trim() || null }),
        }
      );

      if (res.ok) {
        const updated: unknown = await res.json().catch(() => null);
        if (updated != null && typeof updated === "object") {
          setPage(updated as CustomerPageData);
        }
        setComment("");
        setWasResolved(null);
        setPhase({ kind: "feedback_sent" });
        return;
      }

      if (res.status === 410) {
        setExpired(true);
        return;
      }

      const code = await parseErrorCode(res);
      setErrorMsg(errorMessageForCode(code, res.status));
      setPhase({ kind: "feedback" });
    } catch {
      setErrorMsg("…check your connection and try again.");
      setPhase({ kind: "feedback" });
    }
  }

  // ─── Expired state ──────────────────────────────────────────────────────

  if (expired) {
    return (
      <main className="bg-background px-4 py-6 sm:py-10">
        <div className="mx-auto w-full max-w-2xl rounded-xl border border-[var(--ophalo-border)] bg-card px-5 py-5">
          <p className="text-base font-semibold text-foreground">This tracker link has expired.</p>
          <p className="mt-2 text-sm leading-6 text-muted-foreground">
            The tracker for your request with{" "}
            <strong className="text-foreground">{page.businessName}</strong> is no longer active.
          </p>
          {page.referenceCode && (
            <p className="mt-2 text-sm text-muted-foreground">
              Reference:{" "}
              <span className="font-mono text-[13px] tracking-widest text-foreground">
                {page.referenceCode}
              </span>
            </p>
          )}
        </div>
      </main>
    );
  }

  // ─── Active page ─────────────────────────────────────────────────────────

  return (
    <main className="bg-background px-4 py-6 sm:py-10">
      <div className="mx-auto w-full max-w-2xl space-y-4 sm:space-y-5">

        {/* §1 — Status hero */}
        <div className="overflow-hidden rounded-2xl border border-[var(--ophalo-border)] bg-[var(--keep-accent-bg)] shadow-sm">
          <div className="h-1.5 bg-[var(--keep-accent)]" />
          <div className="px-5 pb-6 pt-5 sm:px-6">
            <p className="text-sm font-semibold text-foreground">{page.businessName}</p>
            <h1 className="mt-1 font-serif text-[28px] font-bold leading-tight tracking-tight text-foreground sm:text-[32px]">
              {statusHeadline(page.status)}
            </h1>
            {page.currentStatusText && (
              <p className="mt-1.5 text-sm leading-6 text-muted-foreground">
                {page.currentStatusText}
              </p>
            )}
            <div className="mt-3 inline-flex items-center gap-1.5 text-xs font-medium text-[var(--keep-accent)]">
              <Lock className="h-3 w-3" aria-hidden />
              Private tracking link
            </div>
          </div>
        </div>

        {/* Metadata row — status chip + reference */}
        <div className="flex flex-wrap items-center gap-3 px-1">
          <KeepBadge variant={statusBadgeVariant(page.status)}>
            {statusChipLabel(page.status)}
          </KeepBadge>
          <p className="text-xs text-muted-foreground">
            Ref:{" "}
            <span className="font-mono text-[11px] tracking-widest text-muted-foreground">
              {page.referenceCode}
            </span>
          </p>
        </div>

        {/* §2 — Continuity card */}
        {latestBusinessUpdate !== null ? (
          <div className="rounded-xl border border-[var(--ophalo-border)] border-l-4 border-l-[var(--keep-accent)] bg-[var(--keep-accent-bg)] px-5 py-5 shadow-[0_1px_2px_rgba(16,36,62,0.04)]">
            <div className="flex flex-wrap items-center gap-2">
              <KeepBadge variant="teal">Latest from {page.businessName}</KeepBadge>
            </div>
            <p className="mt-3 text-lg font-semibold leading-7 text-foreground">
              {latestBusinessUpdate.content}
            </p>
            <p className="mt-3 text-xs text-muted-foreground">
              {formatDate(latestBusinessUpdate.occurredAtUtc)}
            </p>
          </div>
        ) : (
          <div className="rounded-xl border border-[var(--ophalo-border)] bg-card px-5 py-5">
            <div className="flex items-start gap-3">
              <div className="mt-0.5 flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-[var(--keep-accent-bg)]">
                <ShieldCheck className="h-4 w-4 text-[var(--keep-accent)]" aria-hidden />
              </div>
              <div>
                <p className="text-sm font-semibold text-foreground">
                  Your request is in good hands.
                </p>
                <p className="mt-1.5 text-sm leading-6 text-muted-foreground">
                  {page.businessName} will follow up with you here and may also contact you directly.
                </p>
              </div>
            </div>
          </div>
        )}

        {/* §4 — Composer + §5 Action chips */}
        {hasActions && (
          <div className="rounded-xl border border-[var(--ophalo-border)] bg-card px-5 py-5">
            {phase.kind === "sent" ? (
              <div role="status" aria-live="polite">
                <p className="text-base font-semibold text-foreground">
                  Delivered to {page.businessName}.
                </p>
                <p className="mt-1 text-base leading-6 text-muted-foreground">
                  They&rsquo;ll follow up with you here.
                </p>
                <button
                  className="mt-3 text-xs text-muted-foreground underline underline-offset-2 hover:text-foreground"
                  onClick={() => {
                    if (dismissTimer.current) clearTimeout(dismissTimer.current);
                    setPhase({ kind: "idle" });
                  }}
                >
                  Send another message
                </button>
              </div>
            ) : phase.kind === "feedback_sent" ? (
              <div role="status" aria-live="polite">
                <p className="text-base font-semibold text-foreground">
                  Feedback submitted. Thank you.
                </p>
                <p className="mt-1 text-base leading-6 text-muted-foreground">
                  {page.businessName} appreciates you letting them know.
                </p>
              </div>
            ) : hasFeedbackAction ? (
              /* Feedback form for closed requests */
              <>
                <p className="text-base font-semibold text-foreground">
                  Was your request resolved?
                </p>
                <div className="mt-3 flex flex-wrap gap-2">
                  {[true, false].map((value) => (
                    <button
                      key={String(value)}
                      aria-pressed={wasResolved === value}
                      disabled={isSubmitting}
                      onClick={() => setWasResolved(value)}
                      className={`inline-flex min-h-[42px] items-center rounded-full border px-4 text-xs font-semibold transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2 ${
                        wasResolved === value
                          ? "border-[var(--keep-accent)] bg-[var(--keep-accent-bg)] text-[var(--keep-accent)]"
                          : "border-[var(--ophalo-border)] bg-card text-foreground hover:border-[var(--keep-accent)] hover:bg-[var(--keep-accent-bg)] hover:text-[var(--keep-accent)]"
                      } disabled:cursor-not-allowed disabled:opacity-50`}
                    >
                      {value ? "Yes" : "No"}
                    </button>
                  ))}
                </div>
                <div className="mt-4">
                  <label
                    htmlFor="tracker-fb-comment"
                    className="mb-1.5 block text-sm font-semibold text-foreground"
                  >
                    Comment{" "}
                    <span className="font-normal text-muted-foreground">(optional)</span>
                  </label>
                  <textarea
                    id="tracker-fb-comment"
                    value={comment}
                    onChange={(e) => setComment(e.target.value)}
                    disabled={isSubmitting}
                    placeholder="Any additional feedback…"
                    maxLength={2000}
                    rows={3}
                    className="w-full resize-none rounded-lg border border-[var(--ophalo-border)] bg-card px-4 py-3 text-base leading-6 text-foreground outline-none transition focus:border-[var(--keep-accent)] focus:ring-1 focus:ring-[var(--keep-accent)] disabled:opacity-50"
                  />
                </div>
                <p
                  aria-live="polite"
                  className={`mt-2 text-base text-destructive${errorMsg ? "" : " hidden"}`}
                >
                  {errorMsg}
                </p>
                <div className="mt-3">
                  <KeepButton
                    variant="primary"
                    className="w-full"
                    disabled={isSubmitting}
                    onClick={submitFeedback}
                  >
                    {isSubmitting ? "Submitting…" : "Submit feedback"}
                  </KeepButton>
                </div>
              </>
            ) : (
              /* Message intent chips + composer */
              <>
                <div className="flex flex-wrap gap-2">
                  {availableMessageActions.map((action) => {
                    const isCancellation = action === "cancellation_requested";
                    const selectedClass = isCancellation
                      ? "border-[var(--ophalo-danger)] bg-[var(--ophalo-danger-bg)] text-[var(--ophalo-danger)]"
                      : "border-[var(--keep-accent)] bg-[var(--keep-accent-bg)] text-[var(--keep-accent)]";
                    const restingClass = isCancellation
                      ? "border-[var(--ophalo-border)] bg-card text-[var(--ophalo-danger)] hover:border-[var(--ophalo-danger)] hover:bg-[var(--ophalo-danger-bg)] hover:text-[var(--ophalo-danger)]"
                      : "border-[var(--ophalo-border)] bg-card text-foreground hover:border-[var(--keep-accent)] hover:bg-[var(--keep-accent-bg)] hover:text-[var(--keep-accent)]";
                    const PillIcon = ACTION_ICONS[action];
                    return (
                      <button
                        key={action}
                        aria-pressed={selectedAction === action}
                        disabled={isSubmitting}
                        onClick={() => {
                          if (selectedAction === action) {
                            setPhase({ kind: "idle" });
                            setErrorMsg(null);
                          } else {
                            setPhase({ kind: "composing", action });
                            setErrorMsg(null);
                          }
                        }}
                        className={`inline-flex min-h-9 items-center gap-1.5 rounded-full border px-3 text-xs font-semibold transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2 ${
                          selectedAction === action ? selectedClass : restingClass
                        } disabled:cursor-not-allowed disabled:opacity-50`}
                      >
                        {PillIcon && <PillIcon className="h-3.5 w-3.5 shrink-0" aria-hidden />}
                        {ACTION_LABELS[action] ?? action}
                      </button>
                    );
                  })}
                </div>
                <div className="mt-4">
                  <textarea
                    id="tracker-message"
                    value={message}
                    onChange={(e) => setMessage(e.target.value)}
                    disabled={!selectedAction || isSubmitting}
                    placeholder={
                      selectedAction
                        ? "Write your message…"
                        : "Choose an option above to send a message."
                    }
                    maxLength={4000}
                    rows={4}
                    className="w-full resize-none rounded-lg border border-[var(--ophalo-border)] bg-card px-4 py-3 text-base leading-6 text-foreground outline-none transition focus:border-[var(--keep-accent)] focus:ring-1 focus:ring-[var(--keep-accent)] disabled:opacity-50"
                  />
                </div>
                <p
                  aria-live="polite"
                  className={`mt-2 text-base text-destructive${errorMsg ? "" : " hidden"}`}
                >
                  {errorMsg}
                </p>
                <div className="mt-3">
                  <KeepButton
                    variant="primary"
                    className="w-full"
                    disabled={!selectedAction || !message.trim() || isSubmitting}
                    onClick={() => {
                      if (selectedAction) submitMessage(selectedAction);
                    }}
                  >
                    {isSubmitting ? "Sending…" : (
                      <>Send <ArrowRight className="ml-1.5 h-4 w-4 shrink-0" aria-hidden /></>
                    )}
                  </KeepButton>
                </div>
              </>
            )}
          </div>
        )}

        {/* §3 — "What you sent" card */}
        {page.description && (
          <div className="rounded-xl border border-[var(--ophalo-border)] bg-card px-5 py-5">
            <p className="text-base font-semibold text-foreground">What you sent</p>
            <p className="mt-2 text-sm leading-6 text-foreground">{page.description}</p>
          </div>
        )}

        {/* §6 — Timeline (Level 3 — sits directly on canvas, no card wrapper) */}
        {page.events !== null && (
          <ul className="relative space-y-3 pl-3">
            {/* Connector rail centered on the icon dot column */}
            {events.length > 0 && (
              <div
                aria-hidden
                className="pointer-events-none absolute bottom-3 left-10 top-3 w-0.5 bg-[var(--ophalo-border)]"
              />
            )}
            {events.length === 0 ? (
              <li className="px-3 py-3">
                <p className="text-sm leading-6 text-muted-foreground">No updates yet.</p>
              </li>
            ) : events.map((ev, i) => {
              const isNewest = i === 0;
              const content = ev.content ?? eventFallbackContent(ev.eventType);
              const { bg, iconColor, Icon } = eventIconConfig(ev);
              const isCustomerEvent = ev.actorLabel === "customer";
              return (
                <li
                  key={i}
                  className={`relative flex gap-4 rounded-lg px-3 py-3 ${
                    isNewest
                      ? "border border-[var(--keep-accent)] bg-[var(--keep-accent-bg)]"
                      : isCustomerEvent
                      ? "border border-transparent border-l-[var(--keep-accent)]"
                      : "border border-transparent"
                  }`}
                >
                  <div className={`relative z-10 mt-1 flex h-8 w-8 shrink-0 items-center justify-center rounded-full ring-2 ring-white ${bg}`}>
                    <Icon className={`h-4 w-4 ${iconColor}`} aria-hidden />
                  </div>
                  <div className="min-w-0 flex-1">
                    <div className="flex flex-wrap items-center gap-2">
                      {isNewest && (
                        <span className="rounded-full bg-[var(--keep-accent)] px-2 py-0.5 text-[11px] font-semibold leading-none text-white">
                          Newest
                        </span>
                      )}
                      <KeepBadge variant={eventBadgeVariant(ev)}>
                        {eventBadgeLabel(ev)}
                      </KeepBadge>
                    </div>
                    {content && (
                      <p className="mt-1.5 text-sm leading-6 text-foreground">{content}</p>
                    )}
                    <p className="mt-2 text-xs text-muted-foreground">
                      {formatDate(ev.occurredAtUtc)}
                    </p>
                  </div>
                </li>
              );
            })}
          </ul>
        )}

      </div>
    </main>
  );
}
