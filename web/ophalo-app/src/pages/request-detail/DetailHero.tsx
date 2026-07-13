import { useMemo } from "react";
import { AlertTriangle, Clock, Eye, ExternalLink, Share2 } from "lucide-react";
import { type KeepRequestDetailResult } from "../../lib/apiClient";
import { KeepBadge } from "../../components/keep/KeepBadge";
import {
  isDateOnlyToday,
  isDateOnlyPast,
  FOLLOW_UP_REASON_LABELS,
  formatEventTime,
  formatDateOnly,
  reasonLabel,
  statusLabel,
  statusBadgeVariant,
  FOCUS_RING,
} from "./helpers";

// ---------------------------------------------------------------------------
// Customer page sharing (hero links)
// ---------------------------------------------------------------------------

interface CustomerPageHeroActionsProps {
  pageToken: string;
  canRecordShareIntent: boolean;
  needsShare: boolean;
  onOpenShareDrawer: () => void;
}

export function CustomerPageHeroActions({
  pageToken,
  canRecordShareIntent,
  needsShare,
  onOpenShareDrawer,
}: CustomerPageHeroActionsProps) {
  const publicBaseUrl = import.meta.env.VITE_PUBLIC_BASE_URL as string;
  const customerPageUrl = `${publicBaseUrl}/keep/r/${pageToken}`;

  return (
    <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-xs">
      {canRecordShareIntent && needsShare && <KeepBadge variant="attention">Not shared</KeepBadge>}
      <a
        href={customerPageUrl}
        target="_blank"
        rel="noreferrer"
        className={`inline-flex items-center gap-1 font-semibold text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] transition-colors ${FOCUS_RING}`}
      >
        <ExternalLink className="h-3.5 w-3.5 shrink-0" />
        View customer page
      </a>
      {canRecordShareIntent && (
        <button
          type="button"
          onClick={onOpenShareDrawer}
          className={`inline-flex items-center gap-1 font-semibold transition-colors ${FOCUS_RING} ${
            needsShare
              ? "text-[var(--ophalo-attention)] hover:text-[var(--ophalo-ink)]"
              : "text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)]"
          }`}
        >
          <Share2 className="h-3.5 w-3.5 shrink-0" />
          Share Link
        </button>
      )}
    </div>
  );
}

// ---------------------------------------------------------------------------
// Hero card — identity anchor
// ---------------------------------------------------------------------------

interface TodayPromiseBannerProps {
  detail: KeepRequestDetailResult;
  onRecordFollowUp?: () => void;
}

export function TodayPromiseBanner({ detail, onRecordFollowUp }: TodayPromiseBannerProps) {
  const followUpToday = isDateOnlyToday(detail.followUpOnDate);
  const followUpOverdue = isDateOnlyPast(detail.followUpOnDate);
  const plannedToday = isDateOnlyToday(detail.plannedForDate);
  const canRecordFollowUp = !!onRecordFollowUp && detail.availableActions.canSetFollowUpOn;

  const hasFollowUpSignal = followUpToday || followUpOverdue;
  if (!hasFollowUpSignal && !plannedToday) return null;

  if (followUpOverdue) {
    const reason = detail.followUpOnReason
      ? FOLLOW_UP_REASON_LABELS[detail.followUpOnReason] ?? detail.followUpOnReason
      : null;
    const dateLabel = detail.followUpOnDate ? formatDateOnly(detail.followUpOnDate) : null;
    return (
      <div className="rounded-xl border border-[var(--ophalo-danger)] bg-[var(--ophalo-danger-bg)] px-4 py-3 flex items-center justify-between gap-3">
        <div>
          <p className="text-sm font-semibold text-[var(--ophalo-danger)]">
            Overdue follow-up{dateLabel ? ` · ${dateLabel}` : ""}
          </p>
          {reason && <p className="text-xs text-[var(--ophalo-danger)] mt-0.5">{reason}</p>}
        </div>
        {canRecordFollowUp && (
          <button
            type="button"
            onClick={onRecordFollowUp}
            className={`shrink-0 rounded-lg border border-[var(--ophalo-danger)] px-3 py-1.5 text-xs font-semibold text-[var(--ophalo-danger)] hover:bg-[var(--ophalo-danger)] hover:text-white transition-colors ${FOCUS_RING}`}
          >
            Record follow-up
          </button>
        )}
      </div>
    );
  }

  const items: string[] = [];
  if (followUpToday) {
    const reason = detail.followUpOnReason
      ? FOLLOW_UP_REASON_LABELS[detail.followUpOnReason] ?? detail.followUpOnReason
      : null;
    items.push(reason ? `Follow up today: ${reason}` : "Follow up today");
  }
  if (plannedToday) {
    items.push("Planned for today");
  }

  return (
    <div className="rounded-xl border border-[var(--keep-accent)] bg-[var(--keep-accent-bg)] px-4 py-3 flex items-center justify-between gap-3">
      <p className="text-sm font-semibold text-[var(--keep-accent)]">{items.join(" · ")}</p>
      {followUpToday && canRecordFollowUp && (
        <button
          type="button"
          onClick={onRecordFollowUp}
          className={`shrink-0 rounded-lg border border-[var(--keep-accent)] px-3 py-1.5 text-xs font-semibold text-[var(--keep-accent)] hover:bg-[var(--keep-accent)] hover:text-white transition-colors ${FOCUS_RING}`}
        >
          Record follow-up
        </button>
      )}
    </div>
  );
}

interface DetailHeroProps {
  detail: KeepRequestDetailResult;
  canRecordShareIntent: boolean;
  needsShare: boolean;
  onOpenShareDrawer: () => void;
}

export function DetailHero({
  detail,
  canRecordShareIntent,
  needsShare,
  onOpenShareDrawer,
}: DetailHeroProps) {
  const hasAttention = detail.attentionLevel !== "none";

  // ADR-150: customer page viewed info shown in header badges
  const pageViewedInfo = useMemo(() => {
    if (detail.customerPageLastViewedAtUtc) {
      return {
        text: `Viewed ${formatEventTime(detail.customerPageLastViewedAtUtc)}`,
        isAmber: detail.customerPageViewedAfterLatestUpdate === false,
      };
    }
    if (!detail.needsShare) {
      return { text: "Not yet viewed", isAmber: true };
    }
    return null;
  }, [detail.customerPageLastViewedAtUtc, detail.customerPageViewedAfterLatestUpdate, detail.needsShare]);

  return (
    <div className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-5 py-5 shadow-sm">
      {/* Badge row */}
      <div className="flex flex-wrap items-center gap-2 mb-3">
        <span className="font-mono text-xs text-[var(--ophalo-muted)]">{detail.referenceCode}</span>
        <KeepBadge variant={statusBadgeVariant(detail.status)}>{statusLabel(detail.status)}</KeepBadge>
        {hasAttention && detail.attentionReason && (
          <KeepBadge variant="attention">
            {detail.attentionLevel === "overdue" ? (
              <AlertTriangle className="h-3 w-3 mr-1 shrink-0" />
            ) : (
              <Clock className="h-3 w-3 mr-1 shrink-0" />
            )}
            {reasonLabel(detail.attentionReason)}
          </KeepBadge>
        )}
        {pageViewedInfo && (
          <span
            className={`inline-flex items-center gap-1 text-xs ${
              pageViewedInfo.isAmber ? "text-[var(--ophalo-attention)]" : "text-[var(--ophalo-muted)]"
            }`}
          >
            <Eye className="h-3 w-3 shrink-0" />
            {pageViewedInfo.text}
          </span>
        )}
      </div>

      <div className="flex flex-wrap items-end justify-between gap-3">
        {/* Customer name — page type anchor */}
        <h1 className="font-serif text-[26px] font-semibold leading-tight text-[var(--ophalo-ink)]">
          {detail.customerName}
        </h1>
        <CustomerPageHeroActions
          pageToken={detail.pageToken}
          canRecordShareIntent={canRecordShareIntent}
          needsShare={needsShare}
          onOpenShareDrawer={onOpenShareDrawer}
        />
      </div>
    </div>
  );
}
