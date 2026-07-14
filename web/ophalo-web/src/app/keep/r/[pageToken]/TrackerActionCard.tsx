import { ArrowRight, MessageCircle } from "lucide-react";
import {
  PRIMARY_ACTION,
  SECONDARY_ACTION_LABELS,
  SECONDARY_ACTION_ICONS,
  ACTION_COMPOSER_LABELS,
  formatDate,
  type ComposerPhase,
} from "./tracker-types";

export function TrackerActionCard({
  phase,
  businessName,
  message,
  onMessageChange,
  comment,
  onCommentChange,
  wasResolved,
  onWasResolvedChange,
  feedbackSubmittedAtUtc,
  errorMsg,
  isSubmitting,
  selectedAction,
  hasPrimaryAction,
  availableSecondaryActions,
  hasCancellationAction,
  hasFeedbackAction,
  onOpenAction,
  onBackToIdle,
  onSubmitMessage,
  onSubmitFeedback,
  onDismissSent,
}: {
  phase: ComposerPhase;
  businessName: string;
  message: string;
  onMessageChange: (msg: string) => void;
  comment: string;
  onCommentChange: (c: string) => void;
  wasResolved: boolean | null;
  onWasResolvedChange: (v: boolean) => void;
  feedbackSubmittedAtUtc: string | null;
  errorMsg: string | null;
  isSubmitting: boolean;
  selectedAction: string | null;
  hasPrimaryAction: boolean;
  availableSecondaryActions: readonly string[];
  hasCancellationAction: boolean;
  hasFeedbackAction: boolean;
  onOpenAction: (action: string) => void;
  onBackToIdle: () => void;
  onSubmitMessage: (action: string) => void;
  onSubmitFeedback: () => void;
  onDismissSent: () => void;
}) {
  return (
    <div className="rounded-2xl border border-[var(--ophalo-border)] bg-card px-5 py-5 shadow-sm">

      {phase.kind === "sent" ? (
        <div role="status" aria-live="polite">
          <p className="text-base font-semibold text-foreground">
            Delivered to {businessName}.
          </p>
          <p className="mt-1 text-sm leading-6 text-muted-foreground">
            Your message was sent. You&apos;ll see it in the history below.
          </p>
          <button
            onClick={onDismissSent}
            className="mt-3 text-xs text-muted-foreground underline underline-offset-2 hover:text-foreground"
          >
            Send another message
          </button>
        </div>

      ) : phase.kind === "feedback_sent" ? (
        <div role="status" aria-live="polite" className="space-y-3">
          <div>
            <p className="text-base font-semibold text-foreground">
              Feedback submitted. Thank you.
            </p>
            <p className="mt-1 text-sm leading-6 text-muted-foreground">
              {businessName} appreciates you letting them know.
            </p>
          </div>
          {wasResolved !== null && (
            <div className="rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-canvas)] px-4 py-3 space-y-2">
              <p className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">Your feedback</p>
              <span className={`inline-flex items-center rounded-full border px-3 py-1 text-xs font-semibold ${
                wasResolved
                  ? "border-[var(--keep-accent)] bg-[var(--keep-accent-bg)] text-[var(--keep-accent)]"
                  : "border-[var(--ophalo-border)] bg-card text-foreground"
              }`}>
                {wasResolved ? "Yes, resolved" : "No, I still need help"}
              </span>
              {comment && (
                <p className="text-sm leading-6 text-foreground italic">&ldquo;{comment}&rdquo;</p>
              )}
              {feedbackSubmittedAtUtc && (
                <p className="text-xs text-muted-foreground">Submitted {formatDate(feedbackSubmittedAtUtc)}</p>
              )}
            </div>
          )}
        </div>

      ) : hasFeedbackAction ? (
        <>
          <p className="text-base font-semibold text-foreground">Was your request resolved?</p>
          <div className="mt-3 flex flex-wrap gap-2">
            {[true, false].map((value) => (
              <button
                key={String(value)}
                aria-pressed={wasResolved === value}
                disabled={isSubmitting}
                onClick={() => onWasResolvedChange(value)}
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
              onChange={(e) => onCommentChange(e.target.value)}
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
              onClick={onSubmitFeedback}
              className="w-full rounded-xl bg-[var(--keep-accent)] px-4 py-3 text-sm font-semibold text-white transition hover:bg-[var(--keep-accent-hover)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-50"
            >
              {isSubmitting ? "Submitting…" : "Submit feedback"}
            </button>
          </div>
        </>

      ) : selectedAction ? (
        <>
          <div className="mb-4 flex items-center justify-between">
            <label htmlFor="tracker-message" className="text-sm font-semibold text-foreground">
              {ACTION_COMPOSER_LABELS[selectedAction] ?? "Send a message"}
            </label>
            <button
              onClick={onBackToIdle}
              className="text-xs text-muted-foreground underline underline-offset-2 hover:text-foreground"
            >
              ← Back
            </button>
          </div>
          <textarea
            id="tracker-message"
            value={message}
            onChange={(e) => onMessageChange(e.target.value)}
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
              onClick={() => onSubmitMessage(selectedAction)}
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
        <>
          <p className="text-sm font-semibold text-foreground">
            Need to make a change or ask something?
          </p>

          {hasPrimaryAction && (
            <button
              onClick={() => onOpenAction(PRIMARY_ACTION)}
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
                    onClick={() => onOpenAction(action)}
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
                onClick={() => onOpenAction("cancellation_requested")}
                className="text-sm font-semibold text-[var(--ophalo-danger)] underline-offset-2 hover:underline focus-visible:outline-none"
              >
                Cancel request
              </button>
            </div>
          )}
        </>
      )}

    </div>
  );
}
