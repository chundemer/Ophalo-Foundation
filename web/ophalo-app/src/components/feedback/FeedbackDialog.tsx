import { useId, useRef, useState } from "react";
import { KeepModal } from "../keep/KeepModal";
import { KeepButton } from "../keep/KeepButton";
import { api, ApiError, type FeedbackCategory } from "../../lib/apiClient";

// GAP-038 / BL149 (038-2d-ii). The single lightweight feedback surface, opened from the two v1
// shell entry points ("Report a problem" on #/help, "Send feedback" in the account menu). It never
// shows a fabricated "sent": a 200 (delivered) and a 202 (queued for retry) are thanked
// identically, and any delivery failure the server could not absorb surfaces as a retryable error.
//
// The retention line is the locked ADR-500 wording, verbatim — the founder owns final approval and
// there is no separate privacy surface in the app for it to live on.
const RETENTION_NOTICE =
  "If feedback cannot be delivered immediately, its message and limited submission context may be " +
  "retained for up to 30 days for recovery. Successfully delivered feedback is minimized promptly, " +
  "and its remaining delivery metadata is deleted after seven days.";

const MESSAGE_MAX_LENGTH = 4_000;

const CATEGORY_OPTIONS: ReadonlyArray<{ value: FeedbackCategory; label: string }> = [
  { value: "bug", label: "Something is broken" },
  { value: "confusing", label: "Something is confusing" },
  { value: "missing_thing", label: "Something I need is missing" },
  { value: "too_slow", label: "Something is too slow" },
  { value: "other", label: "Other" },
];

interface FeedbackDialogProps {
  onClose: () => void;
  /** Opaque non-PII client context (route, app build, platform, timestamp), assembled by the
   *  shell at open time. The server caps it at 4 KiB and drops it silently if larger. */
  context?: Record<string, unknown>;
}

function errorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 413 || error.code === "feedback.message_too_long")
      return `That is longer than ${MESSAGE_MAX_LENGTH.toLocaleString()} characters — please shorten it.`;
    if (error.code === "feedback.message_required") return "Please enter a message.";
    if (error.status === 429)
      return "You have sent a lot of feedback recently. Please try again in a little while.";
    if (error.status === 503) return "We could not save that just now. Please try again.";
  }
  return "Something went wrong. Please try again.";
}

export function FeedbackDialog({ onClose, context }: FeedbackDialogProps) {
  const titleId = useId();
  const messageId = useId();
  const categoryId = useId();
  const textareaRef = useRef<HTMLTextAreaElement>(null);

  const [message, setMessage] = useState("");
  const [category, setCategory] = useState<FeedbackCategory | "">("");
  const [status, setStatus] = useState<"idle" | "submitting" | "sent">("idle");
  const [error, setError] = useState<string | null>(null);

  const trimmed = message.trim();
  const overLimit = message.length > MESSAGE_MAX_LENGTH;
  const canSubmit = trimmed.length > 0 && !overLimit && status !== "submitting";

  async function submit() {
    if (!canSubmit) return;
    setStatus("submitting");
    setError(null);
    try {
      await api.submitFeedback({
        message: trimmed,
        ...(category ? { category } : {}),
        ...(context ? { context } : {}),
      });
      setStatus("sent");
    } catch (e) {
      setStatus("idle");
      setError(errorMessage(e));
    }
  }

  return (
    <KeepModal
      onClose={onClose}
      labelledBy={titleId}
      initialFocus={textareaRef}
      overlayClassName="flex items-start justify-center overflow-y-auto p-4 sm:items-center"
      backdropClassName="bg-black/30"
      panelClassName="w-full max-w-lg rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] p-5 shadow-xl"
    >
      {status === "sent" ? (
        <div>
          <h2 id={titleId} className="font-serif text-lg font-semibold text-[var(--ophalo-ink)]">
            Thanks for the feedback
          </h2>
          {/* Neutral for both outcomes: a 202 only guarantees durable queueing, never delivery —
              claiming "it went to the team" would be a fabricated "sent". */}
          <p className="mt-2 text-sm text-[var(--ophalo-ink)]">
            Thanks for taking the time to share this. We read every note.
          </p>
          <div className="mt-5 flex justify-end">
            <KeepButton type="button" variant="primary" onClick={onClose}>
              Close
            </KeepButton>
          </div>
        </div>
      ) : (
        <form
          onSubmit={(e) => {
            e.preventDefault();
            void submit();
          }}
        >
          <h2 id={titleId} className="font-serif text-lg font-semibold text-[var(--ophalo-ink)]">
            Send feedback
          </h2>

          <label
            htmlFor={messageId}
            className="mt-4 block text-sm font-medium text-[var(--ophalo-ink)]"
          >
            What got in your way?
          </label>
          <textarea
            ref={textareaRef}
            id={messageId}
            value={message}
            onChange={(e) => setMessage(e.target.value)}
            rows={5}
            required
            aria-invalid={overLimit || undefined}
            className="mt-1.5 w-full resize-y rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-canvas)] px-3 py-2 text-sm text-[var(--ophalo-ink)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)]"
          />
          {overLimit && (
            <p className="mt-1 text-[0.8125rem] text-[var(--ophalo-attention)]">
              {(message.length - MESSAGE_MAX_LENGTH).toLocaleString()} character
              {message.length - MESSAGE_MAX_LENGTH === 1 ? "" : "s"} over the{" "}
              {MESSAGE_MAX_LENGTH.toLocaleString()} limit.
            </p>
          )}

          <label
            htmlFor={categoryId}
            className="mt-4 block text-sm font-medium text-[var(--ophalo-ink)]"
          >
            Category <span className="font-normal text-[var(--ophalo-muted)]">(optional)</span>
          </label>
          <select
            id={categoryId}
            value={category}
            onChange={(e) => setCategory(e.target.value as FeedbackCategory | "")}
            className="mt-1.5 w-full rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-canvas)] px-3 py-2 text-sm text-[var(--ophalo-ink)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)]"
          >
            <option value="">No category</option>
            {CATEGORY_OPTIONS.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>

          <p className="mt-4 text-[0.8125rem] leading-relaxed text-[var(--ophalo-muted)]">
            {RETENTION_NOTICE}
          </p>

          {error && (
            <p role="alert" className="mt-3 text-[0.875rem] text-[var(--ophalo-attention)]">
              {error}
            </p>
          )}

          <div className="mt-5 flex justify-end gap-2">
            <button
              type="button"
              onClick={onClose}
              className="rounded-lg px-4 py-2 text-sm font-medium text-[var(--ophalo-muted)] hover:bg-[var(--ophalo-canvas)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2"
            >
              Cancel
            </button>
            <KeepButton type="submit" variant="primary" disabled={!canSubmit}>
              {status === "submitting" ? "Sending…" : "Send feedback"}
            </KeepButton>
          </div>
        </form>
      )}
    </KeepModal>
  );
}
