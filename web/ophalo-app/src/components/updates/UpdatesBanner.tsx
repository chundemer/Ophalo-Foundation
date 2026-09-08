import { X } from "lucide-react";
import type { UpdateEntry } from "../../lib/apiClient";

// GAP-038 / BL149 (038-1b-iii) — the Requests-list highlight banner.
//
// One calm attention strip at the top of the Requests list showing the single most-recent
// qualifying highlight entry (see `computeBannerState` in useUpdatesFeed). Dismissing it adds the
// entry id to `keep_dismissed_banners` and never advances the seen-watermark. When more than one
// highlight qualifies, a "N more updates →" link points at the Help & Updates page.
//
// Rendered as a slot node by App.tsx and passed down through the Requests workbench; this component
// owns only presentation. `role="status"` (not `alert`) — it is informational, never urgent.

interface UpdatesBannerProps {
  entry: UpdateEntry;
  /** Count of other qualifying highlight entries beyond `entry`. */
  moreCount: number;
  onDismiss: () => void;
  /** Navigate to the Help & Updates page (`#/help`). */
  onViewAll: () => void;
}

export function UpdatesBanner({ entry, moreCount, onDismiss, onViewAll }: UpdatesBannerProps) {
  return (
    <div
      role="status"
      className="mb-4 flex items-start gap-3 rounded-md border border-[var(--ophalo-attention)] bg-[var(--ophalo-attention-bg)] px-3 py-2.5 text-[var(--ophalo-attention)]"
    >
      <div className="min-w-0 flex-1">
        <p className="text-sm font-medium">{entry.title}</p>
        {moreCount > 0 && (
          <button
            type="button"
            onClick={onViewAll}
            className="mt-1 text-[0.8125rem] font-semibold underline underline-offset-2 hover:opacity-80 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--ophalo-attention)] focus-visible:ring-offset-1 rounded"
          >
            {moreCount} more update{moreCount === 1 ? "" : "s"} →
          </button>
        )}
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
