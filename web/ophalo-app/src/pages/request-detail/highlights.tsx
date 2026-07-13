import { type KeepRequestDetailResult } from "../../lib/apiClient";
import { KeepBadge } from "../../components/keep/KeepBadge";

export type HighlightLevel = "primary" | "secondary";

export interface AttentionHighlights {
  sendUpdate?: HighlightLevel;
  logContact?: HighlightLevel;
  workControls?: HighlightLevel;
  feedbackReview?: HighlightLevel;
  markHandled?: HighlightLevel;
}

export function getAttentionResolutionHighlights(detail: KeepRequestDetailResult): AttentionHighlights {
  if (detail.attentionLevel === "none" || !detail.attentionReason) return {};
  const canSendUpdate = detail.availableActions.canSendBusinessUpdate;
  const canLogContact = detail.availableActions.canLogExternalContact;
  const canMarkHandled = detail.availableActions.canAcknowledgeAttention;
  switch (detail.attentionReason) {
    case "customer_message":
    case "update_request":
    case "first_response_due":
    case "complaint":
      return {
        sendUpdate: canSendUpdate ? "primary" : undefined,
        logContact: canLogContact ? (canSendUpdate ? "secondary" : "primary") : undefined,
      };
    case "call_requested":
      return {
        logContact: canLogContact ? "primary" : undefined,
        sendUpdate: canSendUpdate ? "secondary" : undefined,
      };
    case "schedule_change_request":
    case "timing_change_requested":
      return {
        sendUpdate: canSendUpdate ? "primary" : undefined,
        logContact: canLogContact ? (canSendUpdate ? "secondary" : "primary") : undefined,
      };
    case "cancellation_requested":
    case "change_or_cancel_request":
      return {
        sendUpdate: canSendUpdate ? "primary" : undefined,
        logContact: (!canSendUpdate && canLogContact) ? "secondary" : undefined,
      };
    case "unresolved_feedback":
      return { feedbackReview: "primary" };
    default: {
      if (canSendUpdate) return { sendUpdate: "primary", logContact: canLogContact ? "secondary" : undefined };
      if (canLogContact) return { logContact: "primary" };
      if (canMarkHandled) return { markHandled: "primary" };
      return {};
    }
  }
}

export function highlightBorderCls(level?: HighlightLevel): string {
  if (level === "primary") return "border-[var(--keep-accent)]";
  if (level === "secondary") return "border-[var(--ophalo-navy)]";
  return "border-[var(--ophalo-border)]";
}

export function highlightBgCls(): string {
  return "bg-[var(--ophalo-card)]";
}

export function highlightBoxShadow(level?: HighlightLevel): string | undefined {
  if (level === "primary") return "0 0 0 3px color-mix(in srgb, var(--keep-accent) 18%, transparent)";
  if (level === "secondary") return "0 0 0 3px color-mix(in srgb, var(--ophalo-navy) 10%, transparent)";
  return undefined;
}

export function RecommendedActionBadge({ level }: { level?: HighlightLevel }) {
  if (level !== "primary") return null;
  return <KeepBadge variant="teal">Recommended next step</KeepBadge>;
}

export function maxHighlight(...levels: (HighlightLevel | undefined)[]): HighlightLevel | undefined {
  if (levels.includes("primary")) return "primary";
  if (levels.includes("secondary")) return "secondary";
  return undefined;
}
