import { type CustomerEventItem, eventFallbackContent, formatDate } from "./tracker-types";

export function TrackerHistoryCard({
  events,
  referenceCode,
  businessName,
  initials,
  latestBusinessUpdate,
}: {
  events: CustomerEventItem[];
  referenceCode: string;
  businessName: string;
  initials: string;
  latestBusinessUpdate: CustomerEventItem | null;
}) {
  return (
    <div className="overflow-hidden rounded-2xl border border-[var(--ophalo-border)] bg-card shadow-sm">
      <div className="flex items-center justify-between border-b border-[var(--ophalo-border)] px-5 py-4">
        <p className="text-sm font-semibold text-foreground">Request history</p>
        <p className="font-mono text-[11px] tracking-widest text-muted-foreground">
          {referenceCode}
        </p>
      </div>

      {events.length === 0 ? (
        <p className="px-5 py-4 text-sm text-muted-foreground">No updates yet.</p>
      ) : (
        <ul className="divide-y divide-[var(--ophalo-border)]">
          {events.map((ev, i) => {
            const isBusiness = ev.actorLabel === "business";
            const isLatestBiz =
              latestBusinessUpdate !== null &&
              isBusiness &&
              ev.occurredAtUtc === latestBusinessUpdate.occurredAtUtc;
            const content = ev.content ?? eventFallbackContent(ev.eventType);
            const actorName = isBusiness ? businessName : "You";

            return (
              <li key={i} className={`flex gap-3 px-5 py-4 ${isBusiness ? "border-l-2 border-l-[var(--keep-accent)] bg-[var(--keep-accent-bg)]" : ""}`}>
                <div
                  className={`flex h-9 w-9 shrink-0 items-center justify-center rounded-full font-bold ${
                    isBusiness
                      ? "bg-[var(--ophalo-navy)] text-[11px] text-white"
                      : "bg-[var(--ophalo-border)] text-[9px] text-[var(--ophalo-ink)]"
                  }`}
                >
                  {isBusiness ? initials : "YOU"}
                </div>
                <div className="min-w-0 flex-1">
                  <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
                    <span className="text-sm font-semibold text-foreground">{actorName}</span>
                    <span className="text-xs text-muted-foreground">
                      {formatDate(ev.occurredAtUtc)}
                    </span>
                    {isLatestBiz && (
                      <span className="rounded-full bg-[var(--keep-accent)] px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-white">
                        Latest business update
                      </span>
                    )}
                  </div>
                  {content && (
                    <p className="mt-1 text-sm leading-6 text-foreground">{content}</p>
                  )}
                </div>
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}
