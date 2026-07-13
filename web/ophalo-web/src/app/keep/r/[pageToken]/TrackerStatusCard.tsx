import { Check, Copy, Share2 } from "lucide-react";
import { type CustomerEventItem, statusHeadline, statusSubtext, formatDate } from "./tracker-types";

export function TrackerStatusCard({
  status,
  origin,
  businessName,
  referenceCode,
  latestBusinessUpdate,
  copied,
  canSharePage,
  onShareOrCopy,
}: {
  status: string;
  origin: "customer" | "business" | null;
  businessName: string;
  referenceCode: string;
  latestBusinessUpdate: CustomerEventItem | null;
  copied: boolean;
  canSharePage: boolean;
  onShareOrCopy: () => void;
}) {
  return (
    <div className="rounded-2xl border border-[var(--ophalo-border)] bg-card px-5 py-5 shadow-sm">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0 flex-1">
          <p className="text-[10px] font-semibold uppercase tracking-widest text-[var(--keep-accent)]">
            Current Status
          </p>
          <h1 className="mt-1 text-2xl font-bold leading-tight text-foreground sm:text-[26px]">
            {statusHeadline(status, origin)}
          </h1>
          <p className="mt-1.5 text-sm text-muted-foreground">
            {statusSubtext(status, businessName, origin)}
          </p>
        </div>
        <button
          onClick={onShareOrCopy}
          className="shrink-0 inline-flex items-center gap-1.5 rounded-lg border border-[var(--ophalo-border)] bg-card px-3 py-2 text-xs font-semibold text-foreground transition hover:border-[var(--ophalo-ink)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)]"
        >
          {copied
            ? <Check className="h-3.5 w-3.5 text-[var(--keep-accent)]" aria-hidden />
            : canSharePage
            ? <Share2 className="h-3.5 w-3.5" aria-hidden />
            : <Copy className="h-3.5 w-3.5" aria-hidden />
          }
          {copied ? "Copied!" : canSharePage ? "Share page" : "Copy link"}
        </button>
      </div>
      <div className="mt-4 border-t border-[var(--ophalo-border)] pt-3">
        <p className="text-xs text-muted-foreground">
          Ref:{" "}
          <span className="font-mono tracking-widest">{referenceCode}</span>
          {latestBusinessUpdate && (
            <> · Last update {formatDate(latestBusinessUpdate.occurredAtUtc)}</>
          )}
        </p>
      </div>
    </div>
  );
}
