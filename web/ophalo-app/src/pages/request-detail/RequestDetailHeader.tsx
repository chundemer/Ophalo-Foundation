import { ChevronLeft, ChevronRight } from "lucide-react";
import { FOCUS_RING } from "./helpers";

interface RequestDetailHeaderProps {
  onBack: () => void;
  showBack?: boolean;
  referenceCode?: string;
  businessName?: string | null;
  prevId?: string;
  nextId?: string;
  onNavigate?: (id: string) => void;
}

export function RequestDetailHeader({ onBack, showBack = true, referenceCode, businessName, prevId, nextId, onNavigate }: RequestDetailHeaderProps) {
  return (
    <div className="flex items-center gap-2 px-4 py-3 bg-[var(--ophalo-card)] border-b border-[var(--ophalo-border)] shrink-0 min-w-0">
      {showBack && (
        <button type="button" onClick={onBack} className={`flex items-center gap-1 text-sm text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] -ml-1 transition-colors shrink-0 ${FOCUS_RING}`}>
          <ChevronLeft className="h-4 w-4" />
          Requests
        </button>
      )}
      {businessName && (
        <span className="text-sm text-[var(--ophalo-muted)] truncate min-w-0" title={businessName}>
          · {businessName}
        </span>
      )}
      {referenceCode && <span className="text-sm text-[var(--ophalo-muted)] font-mono ml-1 shrink-0">{referenceCode}</span>}
      {onNavigate && (prevId !== undefined || nextId !== undefined) && (
        <div className="ml-auto flex items-center gap-1">
          <button type="button" disabled={!prevId} onClick={() => prevId && onNavigate(prevId)} aria-label="Previous request" className={`flex items-center gap-0.5 px-2 py-1 text-xs font-medium rounded text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] disabled:opacity-40 disabled:cursor-not-allowed transition-colors ${FOCUS_RING}`}>
            <ChevronLeft className="h-3.5 w-3.5" />
            Prev
          </button>
          <button type="button" disabled={!nextId} onClick={() => nextId && onNavigate(nextId)} aria-label="Next request" className={`flex items-center gap-0.5 px-2 py-1 text-xs font-medium rounded text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] disabled:opacity-40 disabled:cursor-not-allowed transition-colors ${FOCUS_RING}`}>
            Next
            <ChevronRight className="h-3.5 w-3.5" />
          </button>
        </div>
      )}
    </div>
  );
}
