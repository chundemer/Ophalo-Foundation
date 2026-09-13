import { Megaphone, AlertTriangle, X } from "lucide-react";
import type { UpdateEntry } from "../../lib/apiClient";

// GAP-038 / BL149 (038-1b-iii) — the Requests-list / two-pane-workbench highlight banner.
//
// One calm attention strip showing the single most-recent qualifying highlight entry (see
// `computeBannerState` in useUpdatesFeed). Dismissing it adds the entry id to
// `keep_dismissed_banners` and never advances the seen-watermark. "View Help & Updates" always
// navigates to `#/help`; when more than one highlight qualifies the CTA also names the count.
//
// Tone is semantic, not decorative (Christian, 2026-09-13, same defect as the two-pane
// reachability fix): amber `--ophalo-attention` is reserved for an active `known_issue` — anything
// else (a shipped feature, a guide, a coming-soon note) is positive product news and gets the
// informational teal `--keep-accent` / `--keep-accent-bg` treatment instead, so "In-app feedback"
// reads as news, not as a warning. `role="status"` (not `alert`) either way — it is informational,
// never urgent.

interface UpdatesBannerProps {
  entry: UpdateEntry;
  /** Count of other qualifying highlight entries beyond `entry`. */
  moreCount: number;
  onDismiss: () => void;
  /** Navigate to the Help & Updates page (`#/help`). */
  onViewAll: () => void;
}

export function UpdatesBanner({ entry, moreCount, onDismiss, onViewAll }: UpdatesBannerProps) {
  const isKnownIssue = entry.section === "known_issue";
  const tone = isKnownIssue
    ? {
        border: "border-[var(--ophalo-attention)]",
        bg: "bg-[var(--ophalo-attention-bg)]",
        icon: "text-[var(--ophalo-attention)]",
        eyebrow: "text-[var(--ophalo-attention)]",
        link: "text-[var(--ophalo-attention)]",
        ring: "focus-visible:ring-[var(--ophalo-attention)]",
      }
    : {
        border: "border-[var(--keep-accent)]",
        bg: "bg-[var(--keep-accent-bg)]",
        icon: "text-[var(--keep-accent)]",
        eyebrow: "text-[var(--keep-accent)]",
        link: "text-[var(--keep-accent)]",
        ring: "focus-visible:ring-[var(--keep-accent)]",
      };
  const eyebrow = isKnownIssue ? "Known issue" : "New in Help & Updates";
  const Icon = isKnownIssue ? AlertTriangle : Megaphone;
  const viewAllLabel =
    moreCount > 0
      ? `View Help & Updates — ${moreCount} more update${moreCount === 1 ? "" : "s"} →`
      : "View Help & Updates →";

  return (
    <div
      role="status"
      className={`mb-4 flex items-start gap-3 rounded-md border ${tone.border} ${tone.bg} px-3 py-2.5 text-[var(--ophalo-ink)]`}
    >
      <Icon className={`h-4 w-4 shrink-0 mt-0.5 ${tone.icon}`} aria-hidden />
      <div className="min-w-0 flex-1">
        <p className={`text-xs font-semibold uppercase tracking-wide ${tone.eyebrow}`}>{eyebrow}</p>
        <p className="mt-0.5 text-sm line-clamp-2">
          <span className="font-medium">{entry.title}</span>
          <span className="text-[var(--ophalo-muted)]"> — {entry.body}</span>
        </p>
        <button
          type="button"
          onClick={onViewAll}
          className={`mt-1 text-[0.8125rem] font-semibold underline underline-offset-2 hover:opacity-80 focus-visible:outline-none focus-visible:ring-2 ${tone.ring} focus-visible:ring-offset-1 rounded ${tone.link}`}
        >
          {viewAllLabel}
        </button>
      </div>
      <button
        type="button"
        onClick={onDismiss}
        aria-label="Dismiss this update"
        className="-mr-1 shrink-0 rounded p-0.5 hover:opacity-80 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--ophalo-attention)] focus-visible:ring-offset-1"
      >
        <X className="h-4 w-4" aria-hidden />
      </button>
    </div>
  );
}
