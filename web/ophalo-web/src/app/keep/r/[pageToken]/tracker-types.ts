import { RefreshCw, Phone, Calendar, Paperclip } from "lucide-react";

// ─── Types ───────────────────────────────────────────────────────────────────

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
  origin: "customer" | "business" | null;
}

export type ComposerPhase =
  | { kind: "idle" }
  | { kind: "composing"; action: string }
  | { kind: "submitting"; action: string }
  | { kind: "sent" }
  | { kind: "feedback" }
  | { kind: "submitting_feedback" }
  | { kind: "feedback_sent" };

// ─── Constants ───────────────────────────────────────────────────────────────

export const PRIMARY_ACTION = "question";

export const SECONDARY_ACTIONS = [
  "update_request",
  "call_requested",
  "timing_change_requested",
  "information_added",
] as const;

export const SECONDARY_ACTION_LABELS: Record<string, string> = {
  update_request: "Request update",
  call_requested: "Ask for a call",
  timing_change_requested: "Share availability",
  information_added: "Add details",
};

export const SECONDARY_ACTION_ICONS: Record<string, typeof RefreshCw> = {
  update_request: RefreshCw,
  call_requested: Phone,
  timing_change_requested: Calendar,
  information_added: Paperclip,
};

export const ACTION_COMPOSER_LABELS: Record<string, string> = {
  question: "Send a message",
  update_request: "Request an update",
  call_requested: "Request a call",
  timing_change_requested: "Update your availability",
  information_added: "Add details",
  cancellation_requested: "Request cancellation",
};

export const trackerCanvasStyle = { backgroundColor: "var(--ophalo-canvas)" };

// ─── Helpers ─────────────────────────────────────────────────────────────────

export function businessInitials(name: string): string {
  const words = name.trim().split(/\s+/).filter(Boolean);
  if (words.length === 0) return "?";
  if (words.length === 1) return words[0].slice(0, 2).toUpperCase();
  return (words[0][0] + words[1][0]).toUpperCase();
}

export function statusHeadline(status: string, origin: string | null): string {
  if (status === "received" && origin === "business") {
    return "We've created a request for you";
  }
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

export function statusSubtext(status: string, businessName: string, origin: string | null): string {
  if (status === "received" && origin === "business") {
    return `${businessName} created this page to keep your request details and updates in one place. Save this link to return anytime.`;
  }
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

export function eventFallbackContent(eventType: string): string {
  switch (eventType) {
    case "request_created":        return "Request created.";
    case "status_changed":         return "Status updated.";
    case "request_closed":         return "Request closed.";
    case "request_cancelled":      return "Request cancelled.";
    case "attention_acknowledged": return "Message acknowledged.";
    default:                       return "";
  }
}

export function formatDate(iso: string): string {
  const d = new Date(iso);
  return d.toLocaleDateString("en-US", { month: "short", day: "numeric", year: "numeric" });
}

export async function parseErrorCode(res: Response): Promise<string | null> {
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

export function errorMessageForCode(code: string | null, status: number): string {
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
