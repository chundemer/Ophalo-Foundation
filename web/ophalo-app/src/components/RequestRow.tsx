import { AlertTriangle, Clock, MessageSquare, Link, ChevronRight, UserRound } from "lucide-react";
import { KeepBadge, type KeepBadgeVariant } from "./keep/KeepBadge";
import type { KeepRequestSummary, KeepRequestAvailableItem, KeepQuickAction } from "../lib/apiClient";

// Attention reason → severity mapping (exhaustive; unknown reasons fall back to neutral)
const DANGER_REASONS = new Set([
  "complaint",
  "cancellation_requested",
  "unresolved_feedback",
]);
const URGENT_REASONS = new Set([
  "first_response_due",
  "schedule_change_request",
  "timing_change_requested",
  "call_requested",
]);
const CUSTOMER_WAITING_REASONS = new Set([
  "customer_message",
  "update_request",
  "change_or_cancel_request",
]);

type AttentionTone = "danger" | "urgent" | "waiting" | "neutral";

function attentionTone(reason: string | null): AttentionTone {
  if (!reason) return "neutral";
  if (DANGER_REASONS.has(reason)) return "danger";
  if (URGENT_REASONS.has(reason)) return "urgent";
  if (CUSTOMER_WAITING_REASONS.has(reason)) return "waiting";
  return "neutral";
}

const ATTENTION_REASON_LABELS: Record<string, string> = {
  complaint: "Complaint",
  cancellation_requested: "Cancel requested",
  unresolved_feedback: "Feedback pending",
  first_response_due: "First response due",
  schedule_change_request: "Schedule change",
  timing_change_requested: "Timing change",
  call_requested: "Call requested",
  customer_message: "Customer replied",
  update_request: "Update requested",
  change_or_cancel_request: "Change/cancel",
};

function toneToVariant(tone: AttentionTone): KeepBadgeVariant {
  switch (tone) {
    case "danger": return "danger";
    case "urgent":
    case "waiting": return "attention";
    default: return "default";
  }
}

function AttentionBadge({ reason }: { reason: string | null }) {
  if (!reason) return null;
  const tone = attentionTone(reason);
  const label = ATTENTION_REASON_LABELS[reason] ?? reason;
  const variant = toneToVariant(tone);

  return (
    <KeepBadge variant={variant} className="gap-1">
      {tone === "danger" && <AlertTriangle className="h-3 w-3" />}
      {tone === "urgent" && <Clock className="h-3 w-3" />}
      {tone === "waiting" && <MessageSquare className="h-3 w-3" />}
      {label}
    </KeepBadge>
  );
}

function statusBadgeVariant(status: string): KeepBadgeVariant {
  switch (status) {
    case "in_progress": return "teal";
    case "received":
    case "scheduled": return "info";
    case "resolved": return "success";
    default: return "default";
  }
}

function StatusBadge({ status }: { status: string }) {
  const label = status === "in_progress"
    ? "Active"
    : status.replace(/_/g, " ").replace(/\b\w/g, (c) => c.toUpperCase());
  return <KeepBadge variant={statusBadgeVariant(status)}>{label}</KeepBadge>;
}

function relativeTime(iso: string | null): string | null {
  if (!iso) return null;
  const diffMs = Date.now() - new Date(iso).getTime();
  const mins = Math.floor(diffMs / 60_000);
  if (mins < 1) return "just now";
  if (mins < 60) return `${mins}m ago`;
  const hrs = Math.floor(mins / 60);
  if (hrs < 24) return `${hrs}h ago`;
  const days = Math.floor(hrs / 24);
  if (days < 7) return `${days}d ago`;
  return `${Math.floor(days / 7)}w ago`;
}

function actionPrompt(quickActions: KeepQuickAction[]): string | null {
  if (quickActions.length === 0) return null;
  const clearing = quickActions.find((a) => a.clearsAttention);
  return (clearing ?? quickActions[0]).label;
}

interface RequestRowProps {
  row: KeepRequestSummary;
  onSelect: (requestId: string) => void;
}

export function RequestRow({ row, onSelect }: RequestRowProps) {
  const lastTouch = relativeTime(row.lastBusinessActivityAtUtc ?? row.updatedAtUtc);
  const prompt = row.attention.attentionReason ? actionPrompt(row.actions.quickActions) : null;
  const tone = row.attention.attentionReason ? attentionTone(row.attention.attentionReason) : null;

  const accentBorder = tone === "danger"
    ? "border-l-4 border-l-[var(--ophalo-danger)]"
    : tone !== null
      ? "border-l-4 border-l-[var(--ophalo-attention)]"
      : "";

  return (
    <button
      type="button"
      onClick={() => onSelect(row.id)}
      className={`w-full text-left flex flex-col gap-2 rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-4 py-3 hover:border-[var(--ophalo-navy)] hover:shadow-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2 transition-all min-h-[52px] ${accentBorder}`}
    >
      {/* Row 1: customer name + reference */}
      <div className="flex items-center justify-between gap-2">
        <div className="flex items-center gap-2 min-w-0">
          <span className="font-mono text-[11px] text-[var(--ophalo-muted)] shrink-0">{row.referenceCode}</span>
          <span className="keep-row-title truncate">{row.customerName}</span>
        </div>
        <ChevronRight className="h-4 w-4 text-[var(--ophalo-muted)] shrink-0" />
      </div>

      {/* Row 2: attention badge + status chip + intake urgency — wrap on narrow */}
      <div className="flex flex-wrap items-center gap-1.5">
        {row.attention.attentionReason && (
          <AttentionBadge reason={row.attention.attentionReason} />
        )}
        <StatusBadge status={row.status} />
        {row.source === "public_intake" && row.intakeUrgency === "urgent" && (
          <KeepBadge variant="danger" className="gap-1">
            <AlertTriangle className="h-3 w-3" />
            Urgent
          </KeepBadge>
        )}
        {row.source === "public_intake" && row.intakeUrgency === "soon" && (
          <KeepBadge variant="attention" className="gap-1">
            <Clock className="h-3 w-3" />
            Soon
          </KeepBadge>
        )}
      </div>

      {/* Row 3: action prompt */}
      {prompt && (
        <p className="keep-row-action">
          Next: {prompt}
        </p>
      )}

      {/* Row 4: preview text */}
      {row.preview.previewText && (
        <p className="keep-row-meta line-clamp-2 text-left">
          {row.preview.previewText}
        </p>
      )}

      {/* Row 5: owner · last touch · unshared tracker */}
      <div className="keep-row-meta flex flex-wrap items-center gap-x-3 gap-y-1">
        {row.participation.responsibleDisplayName && (
          <span className="flex items-center gap-1">
            <UserRound className="h-3 w-3" />
            {row.participation.responsibleDisplayName}
          </span>
        )}
        {row.participation.isUnassigned && (
          <span className="font-medium text-[var(--ophalo-attention)]">Unassigned</span>
        )}
        {lastTouch && (
          <span>Last touch {lastTouch}</span>
        )}
        {row.needsShare && (
          <span className="flex items-center gap-1 font-medium text-[var(--ophalo-attention)]">
            <Link className="h-3 w-3" />
            Unshared tracker
          </span>
        )}
        {row.source === "public_intake" && row.contactPreference !== "no_preference" && (
          <span>
            Prefers:{" "}
            {row.contactPreference === "text_message" && "text"}
            {row.contactPreference === "phone_call" && "call"}
            {row.contactPreference === "email" && "email"}
          </span>
        )}
      </div>
    </button>
  );
}

interface AvailableRequestRowProps {
  row: KeepRequestAvailableItem;
  onSelect: (requestId: string) => void;
}

export function AvailableRequestRow({ row, onSelect }: AvailableRequestRowProps) {
  const tone = attentionTone(row.attentionLevel === "none" ? null : row.priorityBand);

  return (
    <button
      type="button"
      onClick={() => onSelect(row.requestId)}
      className="w-full text-left flex flex-col gap-2 rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-4 py-3 hover:border-[var(--ophalo-navy)] hover:shadow-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2 transition-all min-h-[52px]"
    >
      <div className="flex items-center justify-between gap-2">
        <div className="flex items-center gap-2 min-w-0">
          <span className="font-mono text-[11px] text-[var(--ophalo-muted)] shrink-0">{row.referenceCode}</span>
          <span className="keep-row-title truncate">{row.customerName}</span>
        </div>
        <ChevronRight className="h-4 w-4 text-[var(--ophalo-muted)] shrink-0" />
      </div>

      <div className="flex flex-wrap items-center gap-1.5">
        {row.attentionLevel !== "none" && (
          <KeepBadge variant={toneToVariant(tone)} className="gap-1">
            <Clock className="h-3 w-3" />
            Needs attention
          </KeepBadge>
        )}
        <StatusBadge status={row.status} />
      </div>

      {row.descriptionPreview && (
        <p className="keep-row-meta line-clamp-2 text-left">
          {row.descriptionPreview}
        </p>
      )}
    </button>
  );
}
