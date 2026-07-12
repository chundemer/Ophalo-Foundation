import { useState } from "react";
import type { LogExternalContactBody } from "../lib/apiClient";

// ---------------------------------------------------------------------------
// Constants (ADR-216)
// ---------------------------------------------------------------------------

const OUTBOUND_CHANNELS = [
  { value: "phone", label: "Phone call" },
  { value: "sms", label: "Text/SMS" },
  { value: "email", label: "Email" },
];

const INBOUND_CHANNELS = [
  ...OUTBOUND_CHANNELS,
  { value: "in_person", label: "In person" },
  { value: "other", label: "Other" },
];

const PHONE_OUTCOMES = [
  { value: "spoke_with_customer", label: "Spoke with customer" },
  { value: "left_voicemail", label: "Left voicemail" },
  { value: "no_answer", label: "No answer" },
  { value: "wrong_number", label: "Wrong number" },
];

const FOLLOW_UP_OUTCOMES = new Set(["spoke_with_customer", "left_voicemail"]);

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export interface ExternalContactFormProps {
  initialDirection?: "outbound" | "inbound";
  initialChannel?: string;
  maxSummaryLength?: number;
  loading: boolean;
  disabled: boolean;
  error: string | null;
  onSubmit: (body: LogExternalContactBody) => void;
  onCancel: () => void;
  onChannelChange?: (channel: string) => void;
}

const FOCUS_RING =
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-1";

const INPUT_CLS =
  `w-full rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] text-base ` +
  `text-[var(--ophalo-ink)] placeholder:text-[var(--ophalo-muted)] px-3 py-2 ${FOCUS_RING}`;

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export function ExternalContactForm({
  initialDirection = "outbound",
  initialChannel = "phone",
  maxSummaryLength = 4000,
  loading,
  disabled,
  error,
  onSubmit,
  onCancel,
  onChannelChange,
}: ExternalContactFormProps) {
  const [direction, setDirection] = useState<"outbound" | "inbound">(initialDirection);
  const [channel, setChannel] = useState(initialChannel);
  const [outcome, setOutcome] = useState(PHONE_OUTCOMES[0].value);
  const [requiresFollowUp, setRequiresFollowUp] = useState(false);
  const [summary, setSummary] = useState("");

  const channels = direction === "outbound" ? OUTBOUND_CHANNELS : INBOUND_CHANNELS;
  const showOutcome = direction === "outbound" && channel === "phone";
  const followUpFromOutcome = showOutcome && FOLLOW_UP_OUTCOMES.has(outcome);
  const showFollowUp =
    direction === "inbound" ||
    (direction === "outbound" && (channel === "sms" || channel === "email")) ||
    followUpFromOutcome;
  const isSummaryRequired =
    direction === "inbound" || channel === "sms" || channel === "email";

  function handleDirectionChange(next: "outbound" | "inbound") {
    setDirection(next);
    const nextChannels = next === "outbound" ? OUTBOUND_CHANNELS : INBOUND_CHANNELS;
    if (!nextChannels.some((c) => c.value === channel)) {
      setChannel("phone");
      onChannelChange?.("phone");
    }
    setRequiresFollowUp(false);
  }

  function handleChannelChange(next: string) {
    setChannel(next);
    onChannelChange?.(next);
  }

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (loading || disabled) return;
    const trimmed = summary.trim();
    if (isSummaryRequired && !trimmed) return;

    const body: LogExternalContactBody = { direction, channel };
    if (showOutcome && outcome) body.outcome = outcome;
    if (showFollowUp) body.requiresBusinessFollowUp = requiresFollowUp;
    if (trimmed) body.summary = trimmed;

    onSubmit(body);
  }

  return (
    <form onSubmit={handleSubmit} className="flex flex-col gap-4">
      {/* Direction */}
      <div className="flex flex-col gap-1.5">
        <span className="text-sm font-medium text-[var(--ophalo-ink)]">Direction</span>
        <div className="flex rounded-lg border border-[var(--ophalo-border)] overflow-hidden">
          {(["outbound", "inbound"] as const).map((d) => (
            <button
              key={d}
              type="button"
              onClick={() => handleDirectionChange(d)}
              disabled={disabled}
              className={`flex-1 py-2 text-sm font-medium transition-colors ${FOCUS_RING} ${
                direction === d
                  ? "bg-[var(--ophalo-navy)] text-white"
                  : "bg-[var(--ophalo-card)] text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)]"
              }`}
            >
              {d === "outbound" ? "We contacted them" : "They contacted us"}
            </button>
          ))}
        </div>
      </div>

      {/* Channel */}
      <div className="flex flex-col gap-1.5">
        <label className="text-sm font-medium text-[var(--ophalo-ink)]">Channel</label>
        <select
          value={channel}
          onChange={(e) => handleChannelChange(e.target.value)}
          className={INPUT_CLS}
          disabled={loading || disabled}
        >
          {channels.map((c) => (
            <option key={c.value} value={c.value}>{c.label}</option>
          ))}
        </select>
      </div>

      {/* Outcome — outbound phone only */}
      {showOutcome && (
        <div className="flex flex-col gap-1.5">
          <label className="text-sm font-medium text-[var(--ophalo-ink)]">Outcome</label>
          <select
            value={outcome}
            onChange={(e) => setOutcome(e.target.value)}
            className={INPUT_CLS}
            disabled={loading || disabled}
          >
            {PHONE_OUTCOMES.map((o) => (
              <option key={o.value} value={o.value}>{o.label}</option>
            ))}
          </select>
        </div>
      )}

      {/* Follow-up — inbound, outbound SMS/email, outbound phone spoke/voicemail */}
      {showFollowUp && (
        <label className="flex items-center gap-2 text-sm text-[var(--ophalo-ink)]">
          <input
            type="checkbox"
            checked={requiresFollowUp}
            onChange={(e) => setRequiresFollowUp(e.target.checked)}
            disabled={loading || disabled}
            className={`rounded border-[var(--ophalo-border)] ${FOCUS_RING}`}
          />
          Requires business follow-up
        </label>
      )}

      {/* Summary */}
      <div className="flex flex-col gap-1.5">
        <label className="text-sm font-medium text-[var(--ophalo-ink)]">
          Summary
          {!isSummaryRequired && (
            <span className="text-[var(--ophalo-muted)] font-normal"> (optional)</span>
          )}
        </label>
        <textarea
          value={summary}
          onChange={(e) => setSummary(e.target.value)}
          maxLength={maxSummaryLength}
          placeholder={isSummaryRequired ? "Brief summary of this contact…" : "Brief notes about this contact…"}
          rows={3}
          className={`${INPUT_CLS} resize-none`}
          disabled={loading || disabled}
          required={isSummaryRequired}
        />
      </div>

      {error && (
        <p className={`text-sm rounded-lg px-3 py-2 ${
          disabled
            ? "text-[var(--ophalo-attention)] bg-[var(--ophalo-attention-bg)]"
            : "text-[var(--ophalo-danger)] bg-[var(--ophalo-danger-bg)]"
        }`}>
          {error}
        </p>
      )}

      <div className="flex items-center justify-end gap-3 pt-1">
        <button
          type="button"
          onClick={onCancel}
          className={`text-sm text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] transition-colors rounded ${FOCUS_RING}`}
        >
          Cancel
        </button>
        <button
          type="submit"
          disabled={loading || disabled || (isSummaryRequired && !summary.trim())}
          className={`px-4 py-2 rounded-lg text-sm font-medium bg-[var(--ophalo-navy)] text-white
            hover:opacity-90 transition-opacity disabled:opacity-50 disabled:cursor-not-allowed ${FOCUS_RING}`}
        >
          {loading ? "Logging…" : "Log contact"}
        </button>
      </div>
    </form>
  );
}
