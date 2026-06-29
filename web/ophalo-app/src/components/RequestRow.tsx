import { AlertTriangle, Clock, MessageSquare, Link, ArrowRight } from "lucide-react";
import type { KeepRequestSummary, KeepRequestAvailableItem, KeepQuickAction } from "../lib/apiClient";

// Attention reason → badge tone mapping (exhaustive; unknown reasons fall back to neutral)
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

function attentionBadgeTone(reason: string | null): "danger" | "urgent" | "waiting" | "neutral" {
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

function AttentionBadge({ reason }: { reason: string | null }) {
  if (!reason) return null;
  const tone = attentionBadgeTone(reason);
  const label = ATTENTION_REASON_LABELS[reason] ?? reason;

  const cls = {
    danger: "bg-red-100 text-red-800 border border-red-200",
    urgent: "bg-orange-100 text-orange-800 border border-orange-200",
    waiting: "bg-yellow-100 text-yellow-800 border border-yellow-200",
    neutral: "bg-slate-100 text-slate-600 border border-slate-200",
  }[tone];

  return (
    <span className={`inline-flex items-center gap-1 rounded px-1.5 py-0.5 text-xs font-medium ${cls}`}>
      {tone === "danger" && <AlertTriangle className="h-3 w-3" />}
      {tone === "urgent" && <Clock className="h-3 w-3" />}
      {tone === "waiting" && <MessageSquare className="h-3 w-3" />}
      {label}
    </span>
  );
}

function actionPrompt(quickActions: KeepQuickAction[]): string | null {
  if (quickActions.length === 0) return null;
  const clearing = quickActions.find((a) => a.clearsAttention);
  return (clearing ?? quickActions[0]).label;
}

function StatusPill({ status }: { status: string }) {
  const label = status
    .replace(/_/g, " ")
    .replace(/\b\w/g, (c) => c.toUpperCase());
  return (
    <span className="text-xs text-slate-500 bg-slate-100 rounded px-1.5 py-0.5">
      {label}
    </span>
  );
}

interface RequestRowProps {
  row: KeepRequestSummary;
  onSelect: (requestId: string) => void;
}

export function RequestRow({ row, onSelect }: RequestRowProps) {
  return (
    <button
      type="button"
      onClick={() => onSelect(row.id)}
      className="w-full text-left flex flex-col gap-1.5 rounded-lg border border-slate-200 bg-white px-4 py-3 hover:border-slate-300 hover:bg-slate-50 focus:outline-none focus:ring-2 focus:ring-slate-400 focus:ring-offset-1 transition-colors"
    >
      <div className="flex items-start justify-between gap-2">
        <div className="flex items-center gap-2 min-w-0">
          <span className="font-mono text-xs text-slate-400 shrink-0">{row.referenceCode}</span>
          <span className="text-sm font-medium text-slate-900 truncate">{row.customerName}</span>
        </div>
        <div className="flex items-center gap-1.5 shrink-0">
          {row.attention.attentionReason && (
            <AttentionBadge reason={row.attention.attentionReason} />
          )}
          <StatusPill status={row.status} />
        </div>
      </div>

      {row.attention.attentionReason && (() => {
        const prompt = actionPrompt(row.actions.quickActions);
        return prompt ? (
          <p className="flex items-center gap-1 text-sm font-medium text-slate-700">
            <ArrowRight className="h-3.5 w-3.5 shrink-0 text-slate-400" />
            {prompt}
          </p>
        ) : null;
      })()}

      {row.preview.previewText && (
        <p className="text-sm text-slate-600 line-clamp-2 text-left">
          {row.preview.previewText}
        </p>
      )}

      <div className="flex items-center gap-3 text-xs text-slate-400">
        {row.participation.responsibleDisplayName && (
          <span>{row.participation.responsibleDisplayName}</span>
        )}
        {row.participation.isUnassigned && (
          <span className="text-amber-600 font-medium">Unassigned</span>
        )}
        {row.needsShare && (
          <span className="flex items-center gap-1 text-amber-700 font-medium">
            <Link className="h-3 w-3" />
            Unshared tracker link
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
  const tone = attentionBadgeTone(row.attentionLevel === "none" ? null : row.priorityBand);

  return (
    <button
      type="button"
      onClick={() => onSelect(row.requestId)}
      className="w-full text-left flex flex-col gap-1.5 rounded-lg border border-slate-200 bg-white px-4 py-3 hover:border-slate-300 hover:bg-slate-50 focus:outline-none focus:ring-2 focus:ring-slate-400 focus:ring-offset-1 transition-colors"
    >
      <div className="flex items-start justify-between gap-2">
        <div className="flex items-center gap-2 min-w-0">
          <span className="font-mono text-xs text-slate-400 shrink-0">{row.referenceCode}</span>
          <span className="text-sm font-medium text-slate-900 truncate">{row.customerName}</span>
        </div>
        <div className="flex items-center gap-1.5 shrink-0">
          {row.attentionLevel !== "none" && (
            <span className={`inline-flex items-center rounded px-1.5 py-0.5 text-xs font-medium ${
              tone === "danger" ? "bg-red-100 text-red-800 border border-red-200"
              : tone === "urgent" ? "bg-orange-100 text-orange-800 border border-orange-200"
              : "bg-slate-100 text-slate-600 border border-slate-200"
            }`}>
              <Clock className="h-3 w-3 mr-1" />
              Needs attention
            </span>
          )}
          <StatusPill status={row.status} />
        </div>
      </div>

      {row.descriptionPreview && (
        <p className="text-sm text-slate-600 line-clamp-2 text-left">{row.descriptionPreview}</p>
      )}
    </button>
  );
}
