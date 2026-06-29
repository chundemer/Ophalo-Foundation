import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import {
  ChevronLeft,
  Copy,
  Check,
  Share2,
  AlertTriangle,
  Clock,
  MessageSquare,
  Phone,
  Mail,
  User,
} from "lucide-react";
import {
  api,
  type KeepRequestDetailResult,
  type KeepRequestEventItem,
  type ShareIntentMethod,
} from "../lib/apiClient";
import { ApiError } from "../lib/apiClient";
import { NeedsShareBanner } from "../components/NeedsShareBanner";

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const EVENT_TYPE_LABELS: Record<string, string> = {
  request_created: "Request Created",
  status_changed: "Status Changed",
  message_added: "Business Update",
  request_closed: "Request Closed",
  request_cancelled: "Request Cancelled",
  internal_note_added: "Internal Note",
  attention_acknowledged: "Attention Acknowledged",
  external_contact_logged: "External Contact",
  participation_changed: "Participation Changed",
  feedback_reviewed: "Feedback Reviewed",
  follow_up_on_changed: "Follow-Up Date Updated",
  planned_for_changed: "Planned Date Updated",
  request_classified: "Request Classified",
  share_intent_recorded: "Tracker Link Shared",
};

function eventTypeLabel(type: string): string {
  return EVENT_TYPE_LABELS[type] ?? type.replace(/_/g, " ").replace(/\b\w/g, (c) => c.toUpperCase());
}

function formatEventTime(isoUtc: string): string {
  const d = new Date(isoUtc);
  const now = new Date();
  const diffMs = now.getTime() - d.getTime();
  const diffMins = Math.floor(diffMs / 60_000);
  if (diffMins < 1) return "just now";
  if (diffMins < 60) return `${diffMins}m ago`;
  const diffHours = Math.floor(diffMins / 60);
  if (diffHours < 24) return `${diffHours}h ago`;
  const sameYear = d.getFullYear() === now.getFullYear();
  return d.toLocaleDateString("en-US", {
    month: "short",
    day: "numeric",
    year: sameYear ? undefined : "numeric",
  });
}

function formatDate(isoUtc: string): string {
  return new Date(isoUtc).toLocaleDateString("en-US", {
    month: "short",
    day: "numeric",
    year: "numeric",
    hour: "numeric",
    minute: "2-digit",
  });
}

function statusLabel(status: string): string {
  return status.replace(/_/g, " ").replace(/\b\w/g, (c) => c.toUpperCase());
}

// ---------------------------------------------------------------------------
// Timeline
// ---------------------------------------------------------------------------

function timelineEventSummary(event: KeepRequestEventItem): string | null {
  if (event.eventType === "status_changed" && event.statusAfter) {
    return `Status changed to ${statusLabel(event.statusAfter)}`;
  }
  if (event.eventType === "participation_changed") {
    const action = event.participationAction ?? "";
    const target = event.participationTargetDisplayName ?? "someone";
    if (action === "assigned") return `Assigned to ${target}`;
    if (action === "unassigned") return `Unassigned from ${target}`;
    if (action === "watching_added") return `${target} started watching`;
    if (action === "watching_removed") return `${target} stopped watching`;
    if (action === "muted") return `${target} muted notifications`;
    if (action === "unmuted") return `${target} unmuted notifications`;
    return `Participation updated`;
  }
  if (event.eventType === "external_contact_logged") {
    const dir = event.externalContactDirection ?? "";
    const ch = event.externalContactChannel ?? "";
    const outcome = event.externalContactOutcome;
    const label = `${dir === "inbound" ? "Inbound" : "Outbound"} ${ch}`;
    return outcome ? `${label} — ${outcome.replace(/_/g, " ")}` : label;
  }
  if (event.eventType === "attention_acknowledged") return "Attention acknowledged";
  if (event.eventType === "share_intent_recorded") return "Tracker link shared with customer";
  return null;
}

interface TimelineEventProps {
  event: KeepRequestEventItem;
}

function TimelineEvent({ event }: TimelineEventProps) {
  const isCustomerVisible = event.visibility === "all";
  const summary = timelineEventSummary(event);

  if (isCustomerVisible) {
    return (
      <div className="flex gap-3">
        <div className="flex flex-col items-center">
          <div className="h-2 w-2 rounded-full bg-slate-700 mt-1.5 shrink-0" />
          <div className="w-px flex-1 bg-slate-200 mt-1" />
        </div>
        <div className="pb-4 min-w-0 flex-1">
          <div className="flex items-baseline justify-between gap-2 mb-1">
            <span className="text-xs font-medium text-slate-700">{eventTypeLabel(event.eventType)}</span>
            <span className="text-xs text-slate-400 shrink-0">{formatEventTime(event.occurredAtUtc)}</span>
          </div>
          {summary && (
            <p className="text-sm text-slate-600">{summary}</p>
          )}
          {event.content && (
            <p className="text-sm text-slate-800 mt-0.5 whitespace-pre-wrap">{event.content}</p>
          )}
          {event.actorDisplayName && (
            <p className="text-xs text-slate-400 mt-1">{event.actorDisplayName}</p>
          )}
        </div>
      </div>
    );
  }

  // Internal / system event — quieter treatment
  return (
    <div className="flex gap-3">
      <div className="flex flex-col items-center">
        <div className="h-1.5 w-1.5 rounded-full bg-slate-300 mt-1.5 shrink-0" />
        <div className="w-px flex-1 bg-slate-100 mt-1" />
      </div>
      <div className="pb-3 min-w-0 flex-1">
        <div className="flex items-baseline justify-between gap-2">
          <span className="text-xs text-slate-400">{eventTypeLabel(event.eventType)}</span>
          <span className="text-xs text-slate-300 shrink-0">{formatEventTime(event.occurredAtUtc)}</span>
        </div>
        {summary && (
          <p className="text-xs text-slate-500 mt-0.5">{summary}</p>
        )}
        {event.content && event.visibility === "internal" && (
          <p className="text-sm text-slate-500 mt-0.5 italic whitespace-pre-wrap">{event.content}</p>
        )}
        {event.actorDisplayName && (
          <p className="text-xs text-slate-300 mt-0.5">{event.actorDisplayName}</p>
        )}
      </div>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Desktop share section (right rail)
// ---------------------------------------------------------------------------

interface DesktopShareSectionProps {
  requestId: string;
  pageToken: string;
  canRecordShareIntent: boolean;
  needsShare: boolean;
  onCleared: () => void;
}

function DesktopShareSection({ requestId, pageToken, canRecordShareIntent, needsShare, onCleared }: DesktopShareSectionProps) {
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [copied, setCopied] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const publicBaseUrl = import.meta.env.VITE_PUBLIC_BASE_URL as string;
  const trackerUrl = `${publicBaseUrl}/keep/r/${pageToken}`;
  const canNativeShare = typeof navigator !== "undefined" && typeof navigator.share === "function";

  if (!canRecordShareIntent) return null;

  async function submit(method: ShareIntentMethod, browserAction?: () => Promise<void>) {
    if (isSubmitting) return;
    setIsSubmitting(true);
    setError(null);
    try {
      if (browserAction) await browserAction();
      await api.recordShareIntent(requestId, method);
      onCleared();
    } catch (e) {
      if (e instanceof DOMException && e.name === "AbortError") {
        // user cancelled native share
      } else if (e instanceof ApiError) {
        setError("Could not record share. Try again.");
      } else if (!(e instanceof DOMException)) {
        setError("Could not complete share. Try again.");
      }
    } finally {
      setIsSubmitting(false);
    }
  }

  async function handleCopyLink() {
    await submit("copy_link", async () => {
      await navigator.clipboard.writeText(trackerUrl);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    });
  }

  async function handleNativeShare() {
    await submit("native_share", async () => {
      await navigator.share({ url: trackerUrl, title: "Track Your Request" });
    });
  }

  async function handleMarkShared() {
    await submit("manual_mark_shared");
  }

  return (
    <div className={`rounded-lg border p-4 ${needsShare ? "border-amber-200 bg-amber-50" : "border-slate-100 bg-slate-50"}`}>
      <p className="text-xs font-semibold text-slate-700 mb-2 uppercase tracking-wide">Tracker Link</p>
      {needsShare && (
        <p className="text-xs text-amber-700 font-medium mb-3">
          Not yet shared with customer.
        </p>
      )}
      <div className="flex flex-col gap-2">
        {canNativeShare && (
          <button
            type="button"
            onClick={() => void handleNativeShare()}
            disabled={isSubmitting}
            className="flex items-center gap-2 rounded-md bg-amber-600 px-3 py-2 text-sm font-medium text-white hover:bg-amber-700 disabled:opacity-60"
          >
            <Share2 className="h-3.5 w-3.5" />
            Share Link
          </button>
        )}
        <button
          type="button"
          onClick={() => void handleCopyLink()}
          disabled={isSubmitting}
          className="flex items-center gap-2 rounded-md border border-slate-300 px-3 py-2 text-sm font-medium text-slate-700 bg-white hover:bg-slate-50 disabled:opacity-60"
        >
          {copied ? <Check className="h-3.5 w-3.5 text-emerald-600" /> : <Copy className="h-3.5 w-3.5" />}
          {copied ? "Copied!" : "Copy Link"}
        </button>
        {needsShare && (
          <button
            type="button"
            onClick={() => void handleMarkShared()}
            disabled={isSubmitting}
            className="text-xs text-slate-500 hover:text-slate-700 text-left disabled:opacity-60"
          >
            Mark as shared manually
          </button>
        )}
      </div>
      {error && <p className="mt-2 text-xs text-red-600">{error}</p>}
    </div>
  );
}

// ---------------------------------------------------------------------------
// Detail header
// ---------------------------------------------------------------------------

interface DetailHeaderProps {
  detail: KeepRequestDetailResult;
}

function DetailHeader({ detail }: DetailHeaderProps) {
  const hasAttention = detail.attentionLevel !== "none";

  return (
    <div className="mb-6">
      <div className="flex items-center gap-2 mb-1">
        <span className="font-mono text-xs text-slate-400">{detail.referenceCode}</span>
        <span className="text-xs bg-slate-100 text-slate-600 rounded px-1.5 py-0.5">
          {statusLabel(detail.status)}
        </span>
        {detail.attentionReason && (
          <span className={`inline-flex items-center gap-1 rounded px-1.5 py-0.5 text-xs font-medium ${
            hasAttention ? "bg-orange-100 text-orange-800 border border-orange-200" : "bg-slate-100 text-slate-600"
          }`}>
            {detail.attentionLevel === "overdue"
              ? <AlertTriangle className="h-3 w-3" />
              : detail.attentionLevel === "needs_attention"
                ? <Clock className="h-3 w-3" />
                : <MessageSquare className="h-3 w-3" />
            }
            {detail.attentionReason.replace(/_/g, " ")}
          </span>
        )}
      </div>

      <h1 className="text-lg font-semibold text-slate-900 mb-1">{detail.customerName}</h1>

      {/* Inert contact info — display only, not launchers */}
      <div className="flex flex-wrap gap-x-4 gap-y-1 text-sm text-slate-600 mb-2">
        {detail.customerPhone && (
          <span className="flex items-center gap-1">
            <Phone className="h-3.5 w-3.5 text-slate-400" />
            {detail.customerPhone}
          </span>
        )}
        {detail.customerEmail ? (
          <span className="flex items-center gap-1">
            <Mail className="h-3.5 w-3.5 text-slate-400" />
            {detail.customerEmail}
          </span>
        ) : (
          <span className="flex items-center gap-1 text-slate-400 text-xs">
            <Mail className="h-3.5 w-3.5" />
            No customer email provided
          </span>
        )}
      </div>

      {/* Contact launchers — rendered from server contactActions, not raw field inference */}
      {detail.contactActions.filter((a) => a.available).length > 0 && (
        <div className="flex items-center gap-2 mb-3">
          {detail.contactActions.filter((a) => a.available).map((action) => (
            <a
              key={action.type}
              href={action.type === "call" ? `tel:${action.target}` : `mailto:${action.target}`}
              className="flex items-center gap-1.5 rounded-md border border-slate-300 px-3 py-1.5 text-sm font-medium text-slate-700 hover:bg-slate-50"
            >
              {action.type === "call"
                ? <Phone className="h-3.5 w-3.5" />
                : <Mail className="h-3.5 w-3.5" />}
              {action.type === "call" ? "Call" : "Email"}
            </a>
          ))}
        </div>
      )}

      {detail.description && (
        <p className="text-sm text-slate-700 mb-3 whitespace-pre-wrap">{detail.description}</p>
      )}

      {detail.currentStatusText && (
        <p className="text-sm text-slate-600 italic mb-3">"{detail.currentStatusText}"</p>
      )}

      <p className="text-xs text-slate-400">Created {formatDate(detail.createdAtUtc)}</p>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Read-only metadata (right rail / below header on mobile)
// ---------------------------------------------------------------------------

interface MetadataSectionProps {
  detail: KeepRequestDetailResult;
}

function MetadataSection({ detail }: MetadataSectionProps) {
  const responsible = detail.participants.find((p) => p.participationType === "responsible" && !p.detachedAtUtc);
  const watchers = detail.participants.filter((p) => p.participationType === "watching" && !p.detachedAtUtc);

  return (
    <div className="space-y-4">
      {/* Participation */}
      <div>
        <p className="text-xs font-semibold text-slate-500 uppercase tracking-wide mb-2">Assigned / Watching</p>
        {responsible ? (
          <div className="flex items-center gap-2 text-sm text-slate-700 mb-1">
            <User className="h-3.5 w-3.5 text-slate-400 shrink-0" />
            <span>{responsible.displayName}</span>
            <span className="text-xs text-slate-400">responsible</span>
          </div>
        ) : (
          <p className="text-sm text-amber-600 font-medium">Unassigned</p>
        )}
        {watchers.length > 0 && (
          <p className="text-xs text-slate-500 mt-1">
            {watchers.map((w) => w.displayName).join(", ")} watching
          </p>
        )}
      </div>

      {/* Attention */}
      {detail.attentionLevel !== "none" && (
        <div>
          <p className="text-xs font-semibold text-slate-500 uppercase tracking-wide mb-2">Attention</p>
          <p className="text-sm text-slate-700">
            {detail.attentionReason?.replace(/_/g, " ") ?? detail.attentionLevel}
          </p>
          {detail.attentionSinceUtc && (
            <p className="text-xs text-slate-400 mt-0.5">Since {formatEventTime(detail.attentionSinceUtc)}</p>
          )}
        </div>
      )}

      {/* Follow-up / planned */}
      {(detail.followUpOnDate || detail.plannedForDate) && (
        <div>
          <p className="text-xs font-semibold text-slate-500 uppercase tracking-wide mb-2">Scheduling</p>
          {detail.followUpOnDate && (
            <p className="text-sm text-slate-700">Follow up: {detail.followUpOnDate}</p>
          )}
          {detail.plannedForDate && (
            <p className="text-sm text-slate-700">Planned: {detail.plannedForDate}</p>
          )}
        </div>
      )}

      {/* Customer page activity */}
      {detail.customerPageLastViewedAtUtc && (
        <div>
          <p className="text-xs font-semibold text-slate-500 uppercase tracking-wide mb-1">Customer Tracker</p>
          <p className="text-xs text-slate-500">
            Last viewed {formatEventTime(detail.customerPageLastViewedAtUtc)}
            {detail.customerPageViewedAfterLatestUpdate === false && (
              <span className="text-amber-600 font-medium"> · Not seen since last update</span>
            )}
          </p>
        </div>
      )}
    </div>
  );
}

// ---------------------------------------------------------------------------
// RequestDetail page
// ---------------------------------------------------------------------------

interface RequestDetailProps {
  requestId: string;
  onBack: () => void;
}

export function RequestDetail({ requestId, onBack }: RequestDetailProps) {
  const [shareCleared, setShareCleared] = useState(false);

  const { data: detail, isLoading, isError, error } = useQuery({
    queryKey: ["request-detail", requestId],
    queryFn: () => api.getRequestDetail(requestId),
  });

  const needsShareEffective = !shareCleared && (detail?.needsShare ?? false);
  const canShare = detail?.availableActions.canRecordShareIntent ?? false;

  function handleShareCleared() {
    setShareCleared(true);
  }

  return (
    <div className="flex flex-col h-full">
      {/* Mobile NeedsShare banner — sticky, above everything */}
      {detail && needsShareEffective && canShare && (
        <NeedsShareBanner
          requestId={requestId}
          pageToken={detail.pageToken}
          onCleared={handleShareCleared}
        />
      )}

      {/* Back breadcrumb + page header */}
      <div className="flex items-center gap-2 px-4 py-3 bg-white border-b border-slate-200 shrink-0">
        <button
          type="button"
          onClick={onBack}
          className="flex items-center gap-1 text-sm text-slate-500 hover:text-slate-900 -ml-1"
        >
          <ChevronLeft className="h-4 w-4" />
          Requests
        </button>
        {detail && (
          <span className="text-sm text-slate-400 font-mono ml-1">{detail.referenceCode}</span>
        )}
      </div>

      {/* Loading */}
      {isLoading && (
        <div className="flex flex-1 items-center justify-center">
          <span className="text-slate-400 text-sm">Loading…</span>
        </div>
      )}

      {/* Error */}
      {isError && (
        <div className="flex flex-1 items-center justify-center">
          <span className="text-slate-500 text-sm">
            {error instanceof ApiError && error.status === 403
              ? "You don't have access to this request."
              : error instanceof ApiError && error.status === 404
                ? "Request not found."
                : "Something went wrong. Try going back and reopening."}
          </span>
        </div>
      )}

      {/* Main content */}
      {detail && (
        <div className="flex flex-1 min-h-0 overflow-hidden">
          {/* Left / main column */}
          <div className="flex-1 overflow-y-auto px-4 md:px-6 py-5">
            <DetailHeader detail={detail} />

            {/* Timeline */}
            <div>
              <p className="text-xs font-semibold text-slate-500 uppercase tracking-wide mb-4">Timeline</p>
              {detail.events.length === 0 ? (
                <p className="text-sm text-slate-400">No activity yet.</p>
              ) : (
                <div>
                  {detail.events.map((event) => (
                    <TimelineEvent key={event.id} event={event} />
                  ))}
                </div>
              )}
            </div>

            {/* Mobile metadata and share controls — below timeline */}
            <div className="md:hidden mt-6 space-y-6 pb-6">
              {canShare && (
                <DesktopShareSection
                  requestId={requestId}
                  pageToken={detail.pageToken}
                  canRecordShareIntent={canShare}
                  needsShare={needsShareEffective}
                  onCleared={handleShareCleared}
                />
              )}
              <MetadataSection detail={detail} />
            </div>
          </div>

          {/* Right action rail — desktop only */}
          <aside className="hidden md:flex md:flex-col md:w-72 lg:w-80 md:shrink-0 border-l border-slate-200 bg-white overflow-y-auto px-4 py-5 gap-6">
            <DesktopShareSection
              requestId={requestId}
              pageToken={detail.pageToken}
              canRecordShareIntent={canShare}
              needsShare={needsShareEffective}
              onCleared={handleShareCleared}
            />
            <MetadataSection detail={detail} />
          </aside>
        </div>
      )}
    </div>
  );
}
