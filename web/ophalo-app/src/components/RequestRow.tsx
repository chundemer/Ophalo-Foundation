import { AlertTriangle, Clock, MessageSquare, Link, ChevronRight, UserRound, CheckCircle2 } from "lucide-react";
import { KeepBadge, type KeepBadgeVariant } from "./keep/KeepBadge";
import type { KeepRequestSummary, KeepRequestAvailableItem, KeepQuickAction } from "../lib/apiClient";

const ACTION_FOCUS_MAP: Record<string, string> = {
  contact_customer: "contact",
  post_customer_update: "update",
  acknowledge_attention: "attention",
  review_feedback: "feedback_review",
  close_request: "closeout",
};

const MODAL_ACTION_CODES = new Set([
  "post_customer_update",
  "contact_customer",
  "acknowledge_attention",
  "add_internal_note",
]);

const ACTION_NAV_LABELS: Record<string, string> = {
  contact_customer: "Log contact",
  post_customer_update: "Update customer",
  acknowledge_attention: "Clear attention",
  review_feedback: "Review feedback",
  close_request: "Review closeout",
};

const NEXT_ACTION_MAP: Record<string, string> = {
  post_customer_update: "Update customer",
  contact_customer: "Log contact",
  acknowledge_attention: "Clear attention",
  review_feedback: "Review feedback",
  close_request: "Close request",
};

function nextActionCue(quickActions: KeepQuickAction[]): string | null {
  if (quickActions.some((a) => a.code === "close_request")) {
    return "Close request";
  }
  const labels = quickActions
    .filter((a) => a.code !== "open_detail" && NEXT_ACTION_MAP[a.code] !== undefined)
    .slice(0, 2)
    .map((a) => NEXT_ACTION_MAP[a.code]);
  return labels.length > 0 ? labels.join(" or ") : null;
}

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
    : status === "resolved"
      ? "Work completed"
      : status.replace(/_/g, " ").replace(/\b\w/g, (c) => c.toUpperCase());
  return <KeepBadge variant={statusBadgeVariant(status)}>{label}</KeepBadge>;
}

function shortDate(iso: string | null): string | null {
  if (!iso) return null;
  return new Date(iso).toLocaleDateString("en-US", { month: "short", day: "numeric" });
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

const FOCUS_RING = "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-1";

interface RequestRowProps {
  row: KeepRequestSummary;
  onSelect: (requestId: string) => void;
  onSelectFocused?: (requestId: string, focus: string) => void;
  onActionClick?: (row: KeepRequestSummary, action: KeepQuickAction) => void;
  showCloseoutCue?: boolean;
}

export function RequestRow({ row, onSelect, onSelectFocused, onActionClick, showCloseoutCue }: RequestRowProps) {
  const lastTouch = relativeTime(row.lastBusinessActivityAtUtc ?? row.updatedAtUtc);
  const tone = row.attention.attentionReason ? attentionTone(row.attention.attentionReason) : null;
  const isOverdue = row.ranking.isOverdue;
  const dueLabel = isOverdue && row.ranking.dueAtUtc ? `Due ${shortDate(row.ranking.dueAtUtc)}` : null;
  const actionBarItems = row.actions.quickActions.filter(
    (a) =>
      a.code !== "open_detail" &&
      (
        (a.executionMode === "modal" && MODAL_ACTION_CODES.has(a.code)) ||
        ACTION_FOCUS_MAP[a.code] !== undefined
      ),
  );

  const nextCue = nextActionCue(row.actions.quickActions);

  const borderAccent = (tone === "danger" || isOverdue)
    ? "border-l-4 border-l-[var(--ophalo-danger)]"
    : tone !== null
      ? "border-l-4 border-l-[var(--ophalo-attention)]"
      : "";

  return (
    <div className={`rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] hover:shadow-sm transition-shadow ${borderAccent}`}>
      {/* Main clickable area — opens detail */}
      <button
        type="button"
        onClick={() => onSelect(row.id)}
        className={`w-full text-left flex flex-col gap-2 px-4 pt-3 ${actionBarItems.length > 0 ? "pb-2" : "pb-3"} ${FOCUS_RING} rounded-t-xl`}
      >
        {/* Identity: reference + customer name */}
        <div className="flex items-center justify-between gap-2">
          <div className="flex items-center gap-2 min-w-0">
            <span className="font-mono text-[11px] text-[var(--ophalo-muted)] shrink-0">{row.referenceCode}</span>
            <span className="keep-row-title truncate">{row.customerName}</span>
          </div>
          <ChevronRight className="h-4 w-4 text-[var(--ophalo-muted)] shrink-0" />
        </div>

        {/* Signals: overdue, attention, status, priority */}
        <div className="flex flex-wrap items-center gap-1.5">
          {isOverdue && (
            <KeepBadge variant="danger" className="gap-1">
              <AlertTriangle className="h-3 w-3" />
              Response overdue
            </KeepBadge>
          )}
          {row.attention.attentionReason && (
            <AttentionBadge reason={row.attention.attentionReason} />
          )}
          <StatusBadge status={row.status} />
          {showCloseoutCue && row.status === "resolved" && !row.attention.attentionReason && (
            <KeepBadge variant="success" className="gap-1">
              <CheckCircle2 className="h-3 w-3" />
              Ready for closeout
            </KeepBadge>
          )}
          {row.businessPriority === "urgent" && (
            <KeepBadge variant="danger" className="gap-1">
              <AlertTriangle className="h-3 w-3" />
              Internal priority: Urgent
            </KeepBadge>
          )}
          {row.businessPriority === "soon" && (
            <KeepBadge variant="attention" className="gap-1">
              <Clock className="h-3 w-3" />
              Internal priority: Soon
            </KeepBadge>
          )}
          {row.businessPriority !== null &&
            row.businessPriority !== row.intakeUrgency &&
            row.source === "public_intake" &&
            (row.intakeUrgency === "urgent" || row.intakeUrgency === "soon") && (
            <span className="text-[11px] text-[var(--ophalo-muted)]">
              Customer marked {row.intakeUrgency === "urgent" ? "urgent" : "soon follow-up"}
            </span>
          )}
          {row.businessPriority === null && row.source === "public_intake" && row.intakeUrgency === "urgent" && (
            <KeepBadge variant="danger" className="gap-1">
              <AlertTriangle className="h-3 w-3" />
              Customer marked urgent
            </KeepBadge>
          )}
          {row.businessPriority === null && row.source === "public_intake" && row.intakeUrgency === "soon" && (
            <KeepBadge variant="attention" className="gap-1">
              <Clock className="h-3 w-3" />
              Customer asked for soon follow-up
            </KeepBadge>
          )}
          {dueLabel && (
            <span className="text-[11px] text-[var(--ophalo-danger)]">{dueLabel}</span>
          )}
          {nextCue && (
            <span className="text-[11px] text-[var(--ophalo-muted)]">Next: {nextCue}</span>
          )}
        </div>

        {/* Preview text */}
        {row.preview.previewText && (
          <p className="keep-row-meta line-clamp-1 text-left">{row.preview.previewText}</p>
        )}

        {/* Context metadata */}
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
          {lastTouch && <span>Last touch {lastTouch}</span>}
          {row.needsShare && (
            <span className="flex items-center gap-1 font-medium text-[var(--ophalo-attention)]">
              <Link className="h-3 w-3" />
              Unshared tracker
            </span>
          )}
          {row.source === "public_intake" ? (
            <span>
              Customer intake
              {row.contactPreference === "text_message" && " · Prefers text"}
              {row.contactPreference === "phone_call" && " · Prefers call"}
              {row.contactPreference === "email" && " · Prefers email"}
              {row.serviceCity && row.serviceState && (
                <> · {row.serviceCity}, {row.serviceState}{row.serviceZip ? ` ${row.serviceZip}` : ""}</>
              )}
            </span>
          ) : (
            <span className={row.needsShare ? "text-[var(--ophalo-attention)] font-medium" : ""}>
              Team added{row.needsShare && " · Customer page not yet shared"}
            </span>
          )}
        </div>
      </button>

      {/* Quick action bar */}
      {actionBarItems.length > 0 && (
        <div className="border-t border-[var(--ophalo-border)] px-4 py-2 flex items-center gap-2">
          {actionBarItems.map((action) => {
            const isModal = action.executionMode === "modal" && MODAL_ACTION_CODES.has(action.code);
            const focus = ACTION_FOCUS_MAP[action.code];
            return (
              <button
                key={action.code}
                type="button"
                onClick={(e) => {
                  e.stopPropagation();
                  if (isModal && onActionClick) {
                    onActionClick(row, action);
                  } else if (focus && onSelectFocused) {
                    onSelectFocused(row.id, focus);
                  } else {
                    onSelect(row.id);
                  }
                }}
                className={`inline-flex items-center rounded-md px-2.5 py-1 text-xs font-semibold border border-[var(--ophalo-border)] bg-[var(--ophalo-canvas)] text-[var(--ophalo-ink)] hover:border-[var(--keep-accent)] hover:text-[var(--keep-accent)] transition-colors ${FOCUS_RING}`}
              >
                {ACTION_NAV_LABELS[action.code] ?? action.label}
              </button>
            );
          })}
          <button
            type="button"
            onClick={() => onSelect(row.id)}
            className={`ml-auto inline-flex items-center gap-1 text-xs text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] transition-colors rounded ${FOCUS_RING}`}
          >
            Open detail
            <ChevronRight className="h-3.5 w-3.5" />
          </button>
        </div>
      )}
    </div>
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
      className={`w-full text-left flex flex-col gap-2 rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-4 py-3 hover:border-[var(--ophalo-navy)] hover:shadow-sm ${FOCUS_RING} transition-all`}
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
        <p className="keep-row-meta line-clamp-2 text-left">{row.descriptionPreview}</p>
      )}
    </button>
  );
}
