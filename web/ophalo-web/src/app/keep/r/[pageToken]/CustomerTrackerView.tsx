"use client";

import { useEffect, useRef, useState } from "react";
import {
  AlertCircle, AlertTriangle, ArrowRight, Calendar,
  Check, Copy, MessageCircle, Paperclip, Phone, RefreshCw, Share2,
} from "lucide-react";
import {
  KeepBusinessHeader,
  KeepPageFooter,
} from "@/components/keep/KeepPublicShell";

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
  intakeUrgency: string | null;
}

// ─── Constants ──────────────────────────────────────────────────────────────

const PRIMARY_ACTION = "question";

const SECONDARY_ACTIONS = [
  "update_request",
  "call_requested",
  "timing_change_requested",
  "information_added",
] as const;

const SECONDARY_ACTION_LABELS: Record<string, string> = {
  update_request: "Request update",
  call_requested: "Ask for a call",
  timing_change_requested: "Share availability",
  information_added: "Add details",
};

const SECONDARY_ACTION_ICONS: Record<string, typeof RefreshCw> = {
  update_request: RefreshCw,
  call_requested: Phone,
  timing_change_requested: Calendar,
  information_added: Paperclip,
};

const ACTION_COMPOSER_LABELS: Record<string, string> = {
  question: "Send a message",
  update_request: "Request an update",
  call_requested: "Request a call",
  timing_change_requested: "Update your availability",
  information_added: "Add details",
  cancellation_requested: "Request cancellation",
};

const trackerCanvasStyle = { backgroundColor: "var(--ophalo-canvas)" };

// ─── Helpers ────────────────────────────────────────────────────────────────

function businessInitials(name: string): string {
  const words = name.trim().split(/\s+/).filter(Boolean);
  if (words.length === 0) return "?";
  if (words.length === 1) return words[0].slice(0, 2).toUpperCase();
  return (words[0][0] + words[1][0]).toUpperCase();
}

function statusHeadline(status: string): string {
  switch (status) {
    case "received":         return "Your request has been received";
    case "scheduled":        return "Your request is scheduled";
    case "in_progress":      return "Your request is open";
    case "pending_customer": return "A reply is needed to continue";
    case "resolved":         return "Your request has been resolved";
    case "closed":           return "This request is closed";
    case "cancelled":        return "This request was cancelled";
    default:                 return "Request status";
  }
}

function statusSubtext(status: string, businessName: string): string {
  switch (status) {
    case "received":
    case "scheduled":
    case "in_progress":
      return `${businessName} has your details. Save this link to return anytime. No account required.`;
    case "pending_customer":
      return `${businessName} needs a reply from you. Use the form below to respond.`;
    default:
      return "Save this link to return anytime. No account required.";
  }
}

function eventFallbackContent(eventType: string): string {
  switch (eventType) {
    case "request_created":        return "Request created.";
    case "status_changed":         return "Status updated.";
    case "request_closed":         return "Request closed.";
    case "request_cancelled":      return "Request cancelled.";
    case "attention_acknowledged": return "Message acknowledged.";
    default:                       return "";
  }
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
  const [copied, setCopied] = useState(false);
  const [canSharePage, setCanSharePage] = useState(false);
  const dismissTimer = useRef<ReturnType<typeof setTimeout> | null>(null);

  const apiBase = process.env.NEXT_PUBLIC_API_BASE_URL ?? "";
  const events = [...(page.events ?? [])].reverse();
  const allowedActions = page.allowedActions ?? [];

  const hasPrimaryAction = allowedActions.includes(PRIMARY_ACTION);
  const availableSecondaryActions = (SECONDARY_ACTIONS as readonly string[]).filter(
    (a) => allowedActions.includes(a)
  );
  const hasCancellationAction = allowedActions.includes("cancellation_requested");
  const hasFeedbackAction = allowedActions.includes("feedback");
  const hasActions =
    hasPrimaryAction ||
    availableSecondaryActions.length > 0 ||
    hasCancellationAction ||
    hasFeedbackAction;

  const selectedAction =
    phase.kind === "composing" || phase.kind === "submitting" ? phase.action : null;
  const isSubmitting =
    phase.kind === "submitting" || phase.kind === "submitting_feedback";

  const latestBusinessUpdate =
    (page.events ?? [])
      .slice()
      .reverse()
      .find((e) => e.actorLabel === "business" && e.content !== null) ?? null;

  const initials = businessInitials(page.businessName);

  useEffect(() => {
    setCanSharePage(typeof navigator !== "undefined" && typeof navigator.share === "function");
  }, []);

  // Auto-dismiss sent confirmation after 5s
  useEffect(() => {
    if (phase.kind === "sent" || phase.kind === "feedback_sent") {
      dismissTimer.current = setTimeout(() => setPhase({ kind: "idle" }), 5000);
    }
    return () => { if (dismissTimer.current) clearTimeout(dismissTimer.current); };
  }, [phase.kind]);

  async function shareOrCopyLink() {
    const url = window.location.href;

    if (navigator.share) {
      try {
        await navigator.share({
          title: `${page.businessName} request`,
          text: `View your request with ${page.businessName}.`,
          url,
        });
        return;
      } catch (error) {
        if (error instanceof DOMException && error.name === "AbortError") return;
      }
    }

    await navigator.clipboard.writeText(url);
    setCopied(true);
    window.setTimeout(() => setCopied(false), 2000);
  }

  function openAction(action: string) {
    setPhase({ kind: "composing", action });
    setErrorMsg(null);
  }

  function backToIdle() {
    setPhase({ kind: "idle" });
    setErrorMsg(null);
  }

  async function submitMessage(action: string) {
    const trimmed = message.trim();
    if (!trimmed) { setErrorMsg("Message can't be empty."); return; }
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
        if (updated != null && typeof updated === "object") setPage(updated as CustomerPageData);
        setMessage("");
        setPhase({ kind: "sent" });
        return;
      }
      if (res.status === 410) { setExpired(true); return; }
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
        if (updated != null && typeof updated === "object") setPage(updated as CustomerPageData);
        setComment("");
        setWasResolved(null);
        setPhase({ kind: "feedback_sent" });
        return;
      }
      if (res.status === 410) { setExpired(true); return; }
      const code = await parseErrorCode(res);
      setErrorMsg(errorMessageForCode(code, res.status));
      setPhase({ kind: "feedback" });
    } catch {
      setErrorMsg("…check your connection and try again.");
      setPhase({ kind: "feedback" });
    }
  }

  // ─── Expired ─────────────────────────────────────────────────────────────

  if (expired) {
    return (
      <main className="min-h-screen px-4 py-6 sm:py-10" style={trackerCanvasStyle}>
        <div className="mx-auto w-full max-w-2xl rounded-2xl border border-[var(--ophalo-border)] bg-card px-5 py-6 shadow-sm">
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
    <main className="min-h-screen px-4 py-6 sm:py-10" style={trackerCanvasStyle}>
      <div className="mx-auto w-full max-w-2xl space-y-4 sm:space-y-5">

        {/* §1 — Business identity */}
        <KeepBusinessHeader
          businessName={page.businessName}
          label="Private Request Page"
          description={`This private page keeps your request details and updates from ${page.businessName} in one place.`}
          className="pb-1"
        />

        {/* §2 — Status card */}
        <div className="rounded-2xl border border-[var(--ophalo-border)] bg-card px-5 py-5 shadow-sm">
          <div className="flex items-start justify-between gap-3">
            <div className="min-w-0 flex-1">
              <p className="text-[10px] font-semibold uppercase tracking-widest text-[var(--keep-accent)]">
                Current Status
              </p>
              <h1 className="mt-1 text-2xl font-bold leading-tight text-foreground sm:text-[26px]">
                {statusHeadline(page.status)}
              </h1>
              <p className="mt-1.5 text-sm text-muted-foreground">
                {statusSubtext(page.status, page.businessName)}
              </p>
            </div>
            <button
              onClick={shareOrCopyLink}
              className="shrink-0 inline-flex items-center gap-1.5 rounded-lg border border-[var(--ophalo-border)] bg-card px-3 py-2 text-xs font-semibold text-foreground transition hover:border-[var(--ophalo-ink)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)]"
            >
              {copied
                ? <Check className="h-3.5 w-3.5 text-[var(--keep-accent)]" aria-hidden />
                : canSharePage
                ? <Share2 className="h-3.5 w-3.5" aria-hidden />
                : <Copy className="h-3.5 w-3.5" aria-hidden />
              }
              {copied ? "Copied!" : canSharePage ? "Share page" : "Copy link"}
            </button>
          </div>
          <div className="mt-4 border-t border-[var(--ophalo-border)] pt-3">
            <p className="text-xs text-muted-foreground">
              Ref:{" "}
              <span className="font-mono tracking-widest">{page.referenceCode}</span>
              {latestBusinessUpdate && (
                <> · Last update {formatDate(latestBusinessUpdate.occurredAtUtc)}</>
              )}
            </p>
          </div>
        </div>

        {/* §3 — Actions */}
        {hasActions && (
          <div className="rounded-2xl border border-[var(--ophalo-border)] bg-card px-5 py-5 shadow-sm">

            {phase.kind === "sent" ? (
              <div role="status" aria-live="polite">
                <p className="text-base font-semibold text-foreground">
                  Delivered to {page.businessName}.
                </p>
                <p className="mt-1 text-sm leading-6 text-muted-foreground">
                  Your message was sent. You&apos;ll see it in the history below.
                </p>
                <button
                  onClick={() => { if (dismissTimer.current) clearTimeout(dismissTimer.current); setPhase({ kind: "idle" }); }}
                  className="mt-3 text-xs text-muted-foreground underline underline-offset-2 hover:text-foreground"
                >
                  Send another message
                </button>
              </div>

            ) : phase.kind === "feedback_sent" ? (
              <div role="status" aria-live="polite">
                <p className="text-base font-semibold text-foreground">
                  Feedback submitted. Thank you.
                </p>
                <p className="mt-1 text-sm leading-6 text-muted-foreground">
                  {page.businessName} appreciates you letting them know.
                </p>
              </div>

            ) : hasFeedbackAction ? (
              /* Feedback form for closed/resolved requests */
              <>
                <p className="text-base font-semibold text-foreground">Was your request resolved?</p>
                <div className="mt-3 flex flex-wrap gap-2">
                  {[true, false].map((value) => (
                    <button
                      key={String(value)}
                      aria-pressed={wasResolved === value}
                      disabled={isSubmitting}
                      onClick={() => setWasResolved(value)}
                      className={`inline-flex min-h-[42px] items-center rounded-full border px-4 text-xs font-semibold transition focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] ${
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
                  <label htmlFor="tracker-fb-comment" className="mb-1.5 block text-sm font-semibold text-foreground">
                    Comment <span className="font-normal text-muted-foreground">(optional)</span>
                  </label>
                  <textarea
                    id="tracker-fb-comment"
                    value={comment}
                    onChange={(e) => setComment(e.target.value)}
                    disabled={isSubmitting}
                    placeholder="Any additional feedback…"
                    maxLength={2000}
                    rows={3}
                    className="w-full resize-none rounded-lg border border-[var(--ophalo-border)] bg-card px-4 py-3 text-sm leading-6 text-foreground outline-none transition focus:border-[var(--keep-accent)] focus:ring-1 focus:ring-[var(--keep-accent)] disabled:opacity-50"
                  />
                </div>
                {errorMsg && (
                  <p aria-live="polite" className="mt-2 text-sm text-destructive">{errorMsg}</p>
                )}
                <div className="mt-3">
                  <button
                    disabled={isSubmitting}
                    onClick={submitFeedback}
                    className="w-full rounded-xl bg-[var(--keep-accent)] px-4 py-3 text-sm font-semibold text-white transition hover:bg-[var(--keep-accent-hover)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-50"
                  >
                    {isSubmitting ? "Submitting…" : "Submit feedback"}
                  </button>
                </div>
              </>

            ) : selectedAction ? (
              /* Composer — shown after any action button is tapped */
              <>
                <div className="mb-4 flex items-center justify-between">
                  <label htmlFor="tracker-message" className="text-sm font-semibold text-foreground">
                    {ACTION_COMPOSER_LABELS[selectedAction] ?? "Send a message"}
                  </label>
                  <button
                    onClick={backToIdle}
                    className="text-xs text-muted-foreground underline underline-offset-2 hover:text-foreground"
                  >
                    ← Back
                  </button>
                </div>
                <textarea
                  id="tracker-message"
                  value={message}
                  onChange={(e) => setMessage(e.target.value)}
                  disabled={isSubmitting}
                  placeholder="Write your message…"
                  maxLength={4000}
                  rows={4}
                  className="w-full resize-none rounded-lg border border-[var(--ophalo-border)] bg-card px-4 py-3 text-sm leading-6 text-foreground outline-none transition focus:border-[var(--keep-accent)] focus:ring-1 focus:ring-[var(--keep-accent)] disabled:opacity-50"
                />
                {errorMsg && (
                  <p aria-live="polite" className="mt-2 text-sm text-destructive">{errorMsg}</p>
                )}
                <div className="mt-3">
                  <button
                    disabled={!message.trim() || isSubmitting}
                    onClick={() => submitMessage(selectedAction)}
                    className="inline-flex w-full items-center justify-center gap-2 rounded-xl bg-[var(--keep-accent)] px-4 py-3 text-sm font-semibold text-white transition hover:bg-[var(--keep-accent-hover)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-50"
                  >
                    {isSubmitting
                      ? "Sending…"
                      : <><span>Send</span><ArrowRight className="h-4 w-4" aria-hidden /></>
                    }
                  </button>
                </div>
              </>

            ) : (
              /* Idle — action picker */
              <>
                <p className="text-sm font-semibold text-foreground">
                  Need to make a change or ask something?
                </p>

                {hasPrimaryAction && (
                  <button
                    onClick={() => openAction(PRIMARY_ACTION)}
                    className="mt-3 inline-flex w-full items-center justify-center gap-2 rounded-xl bg-[var(--keep-accent)] px-4 py-3.5 text-sm font-semibold text-white transition hover:bg-[var(--keep-accent-hover)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2"
                  >
                    <MessageCircle className="h-4 w-4" aria-hidden />
                    Send update or question
                  </button>
                )}

                {availableSecondaryActions.length > 0 && (
                  <div className="mt-3 grid grid-cols-2 gap-2">
                    {availableSecondaryActions.map((action) => {
                      const Icon = SECONDARY_ACTION_ICONS[action];
                      return (
                        <button
                          key={action}
                          onClick={() => openAction(action)}
                          className="inline-flex items-center gap-2 rounded-xl border border-[var(--ophalo-border)] bg-card px-4 py-3 text-sm font-semibold text-foreground transition hover:border-[var(--ophalo-ink)] hover:bg-[var(--ophalo-canvas)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)]"
                        >
                          {Icon && <Icon className="h-4 w-4 shrink-0 text-muted-foreground" aria-hidden />}
                          {SECONDARY_ACTION_LABELS[action]}
                        </button>
                      );
                    })}
                  </div>
                )}

                {hasCancellationAction && (
                  <div className="mt-4 text-center">
                    <button
                      onClick={() => openAction("cancellation_requested")}
                      className="text-sm font-semibold text-[var(--ophalo-danger)] underline-offset-2 hover:underline focus-visible:outline-none"
                    >
                      Cancel request
                    </button>
                  </div>
                )}
              </>
            )}
          </div>
        )}

        {/* §4 — Initial request */}
        {(page.description || page.intakeUrgency) && (
          <div className="rounded-2xl border border-[var(--ophalo-border)] bg-card px-5 py-5 shadow-sm">
            <p className="text-[10px] font-semibold uppercase tracking-widest text-muted-foreground">
              Initial Request
            </p>
            <p className="mt-2 text-sm font-semibold text-foreground">Your original message</p>
            {page.description && (
              <div className="mt-2 rounded-lg border border-[var(--ophalo-border)] px-4 py-3">
                <p className="text-sm leading-6 text-foreground">{page.description}</p>
              </div>
            )}
            {page.intakeUrgency && (
              <div className="mt-3 flex gap-2 rounded-lg px-3 py-2.5" style={{ background: "var(--ophalo-attention-bg)" }}>
                <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" style={{ color: "var(--ophalo-attention)" }} />
                <div>
                  <p className="text-xs font-semibold" style={{ color: "var(--ophalo-attention)" }}>
                    {page.intakeUrgency === "urgent" ? "Marked urgent" : "Marked as soon"}
                  </p>
                  <p className="mt-0.5 text-xs" style={{ color: "var(--ophalo-attention)" }}>
                    {page.intakeUrgency === "urgent"
                      ? `Your request has been flagged as urgent for ${page.businessName}.`
                      : `You requested a quick turnaround from ${page.businessName}.`}
                  </p>
                </div>
              </div>
            )}
          </div>
        )}

        {/* §5 — Request history */}
        {page.events !== null && (
          <div className="overflow-hidden rounded-2xl border border-[var(--ophalo-border)] bg-card shadow-sm">
            <div className="flex items-center justify-between border-b border-[var(--ophalo-border)] px-5 py-4">
              <p className="text-sm font-semibold text-foreground">Request history</p>
              <p className="font-mono text-[11px] tracking-widest text-muted-foreground">
                {page.referenceCode}
              </p>
            </div>

            {events.length === 0 ? (
              <p className="px-5 py-4 text-sm text-muted-foreground">No updates yet.</p>
            ) : (
              <ul className="divide-y divide-[var(--ophalo-border)]">
                {events.map((ev, i) => {
                  const isBusiness = ev.actorLabel === "business";
                  const isLatestBiz =
                    latestBusinessUpdate !== null &&
                    isBusiness &&
                    ev.occurredAtUtc === latestBusinessUpdate.occurredAtUtc;
                  const content = ev.content ?? eventFallbackContent(ev.eventType);
                  const actorName = isBusiness ? page.businessName : "You";

                  return (
                    <li key={i} className={`flex gap-3 px-5 py-4 ${isBusiness ? "border-l-2 border-l-[var(--keep-accent)] bg-[var(--keep-accent-bg)]" : ""}`}>
                      <div
                        className={`flex h-9 w-9 shrink-0 items-center justify-center rounded-full font-bold ${
                          isBusiness
                            ? "bg-[var(--ophalo-navy)] text-[11px] text-white"
                            : "bg-[var(--ophalo-border)] text-[9px] text-[var(--ophalo-ink)]"
                        }`}
                      >
                        {isBusiness ? initials : "YOU"}
                      </div>
                      <div className="min-w-0 flex-1">
                        <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
                          <span className="text-sm font-semibold text-foreground">{actorName}</span>
                          <span className="text-xs text-muted-foreground">
                            {formatDate(ev.occurredAtUtc)}
                          </span>
                          {isLatestBiz && (
                            <span className="rounded-full bg-[var(--keep-accent)] px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-white">
                              Latest business update
                            </span>
                          )}
                        </div>
                        {content && (
                          <p className="mt-1 text-sm leading-6 text-foreground">{content}</p>
                        )}
                      </div>
                    </li>
                  );
                })}
              </ul>
            )}
          </div>
        )}

      </div>

      {/* Footer — quiet Keep attribution */}
      <KeepPageFooter className="mx-auto w-full max-w-2xl" />
    </main>
  );
}
