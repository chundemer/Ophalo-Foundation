import type { ComponentType } from "react";
import { AlertTriangle, Clock, MessageSquare, ChevronRight, UserRound, CheckCircle2, Share2, Phone } from "lucide-react";
import { KeepBadge, type KeepBadgeVariant } from "./keep/KeepBadge";
import type { KeepRequestSummary, KeepRequestAvailableItem, KeepQuickAction } from "../lib/apiClient";

// Build 087 §4 action labels/dispatch — server-authoritative codes only.
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

const ACTION_LABELS: Record<string, string> = {
  contact_customer: "Log contact",
  post_customer_update: "Update customer",
  acknowledge_attention: "Review request",
  review_feedback: "Review feedback",
  close_request: "Close request",
};

const ATTENTION_LABELS: Record<string, string> = {
  complaint: "Complaint",
  cancellation_requested: "Cancel requested",
  schedule_change_request: "Schedule change",
  timing_change_requested: "Timing change",
  call_requested: "Call requested",
  customer_message: "Customer replied",
  update_request: "Update requested",
  change_or_cancel_request: "Change/cancel",
};

// Button Hierarchy Is Locked (docs/ux-design/ux-design-decisions.md): color follows the
// action's brand role, not its position in the row. Amber is a status/attention color
// (KeepBadge), never a button treatment.
type ActionRole = "teal" | "navy-outline" | "neutral" | "danger";

const ACTION_ROLES: Record<string, ActionRole> = {
  post_customer_update: "teal",       // Keep communication primary — customer-visible update.
  contact_customer: "navy-outline",   // Secondary operator action.
  share_link: "navy-outline",         // Customer-page secondary action.
  review_feedback: "navy-outline",    // Secondary operator action.
  acknowledge_attention: "neutral",   // Quiet bookkeeping ("Mark handled").
  review_request: "neutral",          // Quiet bookkeeping — plain navigation to detail.
  close_request: "danger",            // Destructive.
};

function actionButtonClass(code: string): string {
  switch (ACTION_ROLES[code] ?? "neutral") {
    case "teal":
      return "border border-transparent bg-[var(--keep-accent)] text-white hover:bg-[var(--keep-accent-hover)]";
    case "navy-outline":
      return "border border-[var(--ophalo-navy)] bg-[var(--ophalo-canvas)] text-[var(--ophalo-navy)] hover:bg-[var(--ophalo-navy)] hover:text-white";
    case "danger":
      return "border border-[var(--ophalo-danger)] bg-[var(--ophalo-canvas)] text-[var(--ophalo-danger)] hover:bg-[var(--ophalo-danger)] hover:text-white";
    default:
      return "border border-[var(--ophalo-border)] bg-[var(--ophalo-canvas)] text-[var(--ophalo-ink)] hover:border-[var(--ophalo-navy)] hover:text-[var(--ophalo-navy)]";
  }
}

type Tone = "danger" | "attention" | "success";

interface Exception {
  key: string;
  label: string;
  tone: Tone;
  icon: ComponentType<{ className?: string }>;
}

interface PromotedAction {
  code: string;
  label: string;
}

function hasAction(row: KeepRequestSummary, code: string): boolean {
  return row.actions.quickActions.some((a) => a.code === code);
}

function shortDate(iso: string | null): string | null {
  if (!iso) return null;
  if (/^\d{4}-\d{2}-\d{2}$/.test(iso)) {
    const [year, month, day] = iso.split("-").map(Number);
    return new Date(year, month - 1, day).toLocaleDateString("en-US", { month: "short", day: "numeric" });
  }
  return new Date(iso).toLocaleDateString("en-US", { month: "short", day: "numeric" });
}

function withDeadline(label: string, isOverdue: boolean, dueAtUtc: string | null): string {
  if (!isOverdue) return label;
  const dateLabel = shortDate(dueAtUtc);
  return dateLabel ? `${label} · ${dateLabel}` : label;
}

function isDateOnlyToday(isoDate: string): boolean {
  const now = new Date();
  const todayStr = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, "0")}-${String(now.getDate()).padStart(2, "0")}`;
  return isoDate === todayStr;
}

function severityToTone(severity: string): Tone | null {
  switch (severity) {
    case "danger":
    case "priority": return "danger";
    case "attention": return "attention";
    default: return null; // "neutral" | "muted" — no ranked signal worth a pill.
  }
}

/**
 * Build 087 §3: one deterministically-selected exception pill, driven by the server's canonical
 * ranking (rankingGroup/severity) rather than the action-priority order — a follow-up promise
 * outranks an administrative gap like an unshared customer page. GAP-027: Closed/Cancelled rows
 * suppress every branch except the unresolved-feedback exception, since FollowUpOn/PlannedFor
 * dates are not cleared server-side on terminal transition and would otherwise read as zombie
 * alarms.
 */
function resolveException(row: KeepRequestSummary, isCalmCloseout: boolean): Exception | null {
  if (row.isPostCloseFollowUp) {
    return { key: "feedback_pending", label: "Feedback pending", tone: "danger", icon: AlertTriangle };
  }

  const isClosedOrCancelled = row.status === "closed" || row.status === "cancelled";
  if (isClosedOrCancelled) return null;

  const reason = row.attention.attentionReason;
  const group = row.ranking.rankingGroup;
  const tone = severityToTone(row.ranking.severity);

  if (tone) {
    switch (group) {
      case "overdue_business_waiting": {
        const label = reason && ATTENTION_LABELS[reason]
          ? withDeadline(ATTENTION_LABELS[reason], true, row.ranking.dueAtUtc)
          : withDeadline("Response overdue", true, row.ranking.dueAtUtc);
        return { key: group, label, tone: "danger", icon: reason === "call_requested" ? Phone : AlertTriangle };
      }
      case "priority_business_waiting":
      case "customer_urgent_active":
      case "standard_business_waiting": {
        const label = reason && ATTENTION_LABELS[reason]
          ? ATTENTION_LABELS[reason]
          : group === "customer_urgent_active"
            ? (row.businessPriority === "urgent" ? "Internal priority: Urgent" : "Customer marked urgent")
            : "Needs response";
        const icon = reason === "call_requested" ? Phone : reason ? MessageSquare : AlertTriangle;
        return { key: group, label, tone, icon };
      }
      case "due_follow_up_on": {
        const date = row.timing?.followUpOnDate ?? null;
        const dueToday = !!(date && isDateOnlyToday(date));
        // One phrase, not "Follow-up due today · Follow up today" — the server label already
        // reads naturally (e.g. "Follow up today"), so just append the date once.
        const label = dueToday
          ? `${row.timing?.followUpOnLabel ?? "Follow-up due today"}${date ? ` · ${shortDate(date)}` : ""}`
          : `Follow-up overdue${date ? ` · ${shortDate(date)}` : ""}`;
        return { key: group, label, tone: dueToday ? "attention" : "danger", icon: Clock };
      }
      default:
        break;
    }
  }

  // No ranked operational signal — the only remaining candidate is an unshared customer page.
  if (row.needsShare) {
    return { key: "needs_share", label: "Customer page not shared", tone: "attention", icon: Share2 };
  }

  if (isCalmCloseout) {
    return { key: "ready_for_closeout", label: "Ready for closeout", tone: "success", icon: CheckCircle2 };
  }

  return null;
}

/**
 * Build 087 §4: the list may promote one permitted row action using this fixed priority order.
 * A due-or-overdue follow-up is active operational work — a standing promise — so it outranks
 * the administrative "Share Link" step even though §4 lists Share Link earlier; Share Link only
 * applies when "no higher-priority workflow state" exists, and a due follow-up is one. There is
 * no persisted attention to acknowledge for a pure due-follow-up-on row (it exists precisely
 * because AttentionLevel is None), so its action navigates straight to detail instead of
 * depending on the acknowledge_attention quick action.
 */
function selectPromotedAction(row: KeepRequestSummary, isCalmCloseout: boolean): PromotedAction | null {
  const reason = row.attention.attentionReason;
  const isClosedOrCancelled = row.status === "closed" || row.status === "cancelled";

  if (row.isPostCloseFollowUp) {
    return hasAction(row, "review_feedback") ? { code: "review_feedback", label: ACTION_LABELS.review_feedback } : null;
  }
  if (isClosedOrCancelled) return null;

  if (reason === "complaint" || reason === "cancellation_requested" || reason === "schedule_change_request" || reason === "timing_change_requested") {
    return hasAction(row, "acknowledge_attention") ? { code: "acknowledge_attention", label: ACTION_LABELS.acknowledge_attention } : null;
  }
  if (reason === "call_requested") {
    return hasAction(row, "contact_customer") ? { code: "contact_customer", label: ACTION_LABELS.contact_customer } : null;
  }
  const followUpDate = row.timing?.followUpOnDate ?? null;
  const followUpNotFuture = !!(followUpDate && !row.timing?.hasFutureFollowUpOn);
  if (followUpNotFuture) {
    return { code: "review_request", label: "Review request" };
  }
  if (row.needsShare) {
    return { code: "share_link", label: "Share Link" };
  }
  if (reason === "customer_message" || reason === "update_request" || reason === "change_or_cancel_request") {
    return hasAction(row, "post_customer_update") ? { code: "post_customer_update", label: ACTION_LABELS.post_customer_update } : null;
  }
  if (row.ranking.isOverdue) {
    const promoteContact = row.contactPreference === "phone_call" && hasAction(row, "contact_customer");
    return promoteContact
      ? { code: "contact_customer", label: ACTION_LABELS.contact_customer }
      : hasAction(row, "post_customer_update")
        ? { code: "post_customer_update", label: ACTION_LABELS.post_customer_update }
        : null;
  }
  if (isCalmCloseout) {
    return hasAction(row, "close_request") ? { code: "close_request", label: ACTION_LABELS.close_request } : null;
  }
  return null;
}

function secondaryAction(row: KeepRequestSummary, promoted: PromotedAction | null): PromotedAction | null {
  if (!promoted) return null;
  const candidates = promoted.code === "contact_customer" ? ["post_customer_update"] : ["contact_customer", "post_customer_update"];
  for (const code of candidates) {
    if (code !== promoted.code && hasAction(row, code)) {
      return { code, label: ACTION_LABELS[code] };
    }
  }
  return null;
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

function exceptionVariant(tone: Tone): KeepBadgeVariant {
  switch (tone) {
    case "danger": return "danger";
    case "success": return "success";
    default: return "attention";
  }
}

// Retained for AvailableRequestRow, which is unchanged by GAP-027's row-hierarchy scope.
const DANGER_REASONS = new Set(["complaint", "cancellation_requested", "unresolved_feedback"]);
const URGENT_REASONS = new Set(["first_response_due", "schedule_change_request", "timing_change_requested", "call_requested"]);
const CUSTOMER_WAITING_REASONS = new Set(["customer_message", "update_request", "change_or_cancel_request"]);

function attentionTone(reason: string | null): "danger" | "urgent" | "waiting" | "neutral" {
  if (!reason) return "neutral";
  if (DANGER_REASONS.has(reason)) return "danger";
  if (URGENT_REASONS.has(reason)) return "urgent";
  if (CUSTOMER_WAITING_REASONS.has(reason)) return "waiting";
  return "neutral";
}

function availableToneToVariant(tone: "danger" | "urgent" | "waiting" | "neutral"): KeepBadgeVariant {
  switch (tone) {
    case "danger": return "danger";
    case "urgent":
    case "waiting": return "attention";
    default: return "default";
  }
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

function timingChipText(
  label: string,
  displayLabel: string | null | undefined,
  date: string | null | undefined,
): string | null {
  if (!date) return null;
  const dateLabel = shortDate(date);
  if (!dateLabel) return null;
  return `${label}: ${displayLabel ? `${displayLabel} · ` : ""}${dateLabel}`;
}

const FOCUS_RING = "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-1";

interface RequestRowProps {
  row: KeepRequestSummary;
  onSelect: (requestId: string) => void;
  onSelectFocused?: (requestId: string, focus: string) => void;
  onActionClick?: (row: KeepRequestSummary, action: KeepQuickAction) => void;
  onShareClick?: (row: KeepRequestSummary) => void;
  showCloseoutCue?: boolean;
}

export function RequestRow({ row, onSelect, onSelectFocused, onActionClick, onShareClick, showCloseoutCue }: RequestRowProps) {
  const lastTouch = relativeTime(row.lastBusinessActivityAtUtc ?? row.updatedAtUtc);
  const isClosedOrCancelled = row.status === "closed" || row.status === "cancelled";
  const isCalmCloseout = showCloseoutCue === true && row.status === "resolved" && !row.attention.attentionReason;

  const exception = resolveException(row, isCalmCloseout);
  const promoted = selectPromotedAction(row, isCalmCloseout);
  const secondary = secondaryAction(row, promoted);

  // Quiet metadata: future or already-exceptioned timing stays out of the exception pill's way.
  // Planned-for dates never become the exception (server ranking never promotes them), so they
  // always render here when present.
  const showFollowUpMeta = !isClosedOrCancelled && exception?.key !== "due_follow_up_on";
  const followUpMeta = showFollowUpMeta ? timingChipText("Follow-up", row.timing?.followUpOnLabel, row.timing?.followUpOnDate) : null;
  const plannedMeta = !isClosedOrCancelled ? timingChipText("Planned", row.timing?.plannedForLabel, row.timing?.plannedForDate) : null;

  const borderAccent = exception
    ? exception.tone === "danger"
      ? "border-l-4 border-l-[var(--ophalo-danger)]"
      : exception.tone === "attention"
        ? "border-l-4 border-l-[var(--ophalo-attention)]"
        : ""
    : "";

  function runAction(action: PromotedAction) {
    if (action.code === "share_link") {
      onShareClick?.(row);
      return;
    }
    if (action.code === "review_request") {
      onSelect(row.id);
      return;
    }
    const quickAction = row.actions.quickActions.find((a) => a.code === action.code);
    if (!quickAction) return;
    const isModal = quickAction.executionMode === "modal" && MODAL_ACTION_CODES.has(quickAction.code);
    const focus = ACTION_FOCUS_MAP[quickAction.code];
    if (isModal && onActionClick) {
      onActionClick(row, quickAction);
    } else if (focus && onSelectFocused) {
      onSelectFocused(row.id, focus);
    } else {
      onSelect(row.id);
    }
  }

  const quickActionButtons = [promoted, secondary].filter((a): a is PromotedAction => a !== null);

  return (
    <div className={`rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] hover:shadow-sm transition-shadow ${borderAccent}`}>
      {/* Main clickable area — opens detail */}
      <button
        type="button"
        onClick={() => onSelect(row.id)}
        className={`w-full text-left flex flex-col gap-2 px-4 pt-3 ${quickActionButtons.length > 0 ? "pb-2" : "pb-3"} ${FOCUS_RING} rounded-t-xl`}
      >
        {/* Identity: reference + customer name */}
        <div className="flex items-center justify-between gap-2">
          <div className="flex items-center gap-2 min-w-0">
            <span className="font-mono text-[11px] text-[var(--ophalo-muted)] shrink-0">{row.referenceCode}</span>
            <span className="keep-row-title truncate">{row.customerName}</span>
          </div>
          <ChevronRight className="h-4 w-4 text-[var(--ophalo-muted)] shrink-0" />
        </div>

        {/* One status pill, one highest-priority exception pill (Build 087 §3) */}
        <div className="flex flex-wrap items-center gap-1.5">
          <StatusBadge status={row.status} />
          {exception && (
            <KeepBadge variant={exceptionVariant(exception.tone)} className="gap-1">
              <exception.icon className="h-3 w-3" />
              {exception.label}
            </KeepBadge>
          )}
          {promoted && (
            <span className="text-sm text-[var(--ophalo-muted)]">Next: {promoted.label}</span>
          )}
        </div>

        {/* Preview text */}
        {row.preview.previewText && (
          <p className="keep-row-meta line-clamp-1 text-left">{row.preview.previewText}</p>
        )}

        {/* Context metadata — quiet, unbordered; no competing alert badges */}
        <div className="keep-row-meta flex flex-wrap items-center gap-x-3 gap-y-1">
          {row.participation.responsibleDisplayName && (
            <span className="flex items-center gap-1">
              <UserRound className="h-3 w-3" />
              {row.participation.responsibleDisplayName}
            </span>
          )}
          {row.participation.isUnassigned && (
            <span>Unassigned</span>
          )}
          {lastTouch && <span>Last touch {lastTouch}</span>}
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
            <span>Created by business</span>
          )}
          {followUpMeta && <span>{followUpMeta}</span>}
          {plannedMeta && <span>{plannedMeta}</span>}
          {row.feedbackWasResolved === true && !row.isPostCloseFollowUp && (
            <span className="flex items-center gap-1">
              <CheckCircle2 className="h-3 w-3" />
              Customer confirmed resolved
            </span>
          )}
          {row.businessPriority === "urgent" && (
            <span>Internal priority: Urgent</span>
          )}
          {row.businessPriority === "soon" && (
            <span>Internal priority: Soon</span>
          )}
        </div>
      </button>

      {/* Quick action bar — at most one promoted action and one relevant secondary (Build 087 §5) */}
      {quickActionButtons.length > 0 && (
        <div className="border-t border-[var(--ophalo-border)] px-4 py-2 flex items-center gap-2 flex-wrap">
          {quickActionButtons.map((action) => (
            <button
              key={action.code}
              type="button"
              onClick={(e) => {
                e.stopPropagation();
                runAction(action);
              }}
              className={`inline-flex items-center justify-center gap-1 rounded-md px-3 min-h-10 text-sm font-semibold transition-colors ${FOCUS_RING} ${actionButtonClass(action.code)}`}
            >
              {action.code === "share_link" && <Share2 className="h-3.5 w-3.5" />}
              {action.label}
            </button>
          ))}
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
          <KeepBadge variant={availableToneToVariant(tone)} className="gap-1">
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
