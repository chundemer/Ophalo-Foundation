// GAP-041: a fixed, queue-agnostic skeleton — never the previous queue's real rows —
// so a first-time queue selection keeps stable list-region geometry instead of
// collapsing to a small "Loading…" blob.
const SKELETON_ROW_COUNT = 5;

export function RequestRowSkeleton() {
  const pulse = "animate-pulse motion-reduce:animate-none rounded bg-[var(--ophalo-canvas)]";
  return (
    <div aria-hidden="true" className="space-y-2">
      {Array.from({ length: SKELETON_ROW_COUNT }).map((_, i) => (
        <div
          key={i}
          className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-4 py-3 space-y-2"
        >
          <div className="flex items-center gap-2">
            <div className={`h-4 w-28 ${pulse}`} />
            <div className={`h-4 w-16 ${pulse}`} />
          </div>
          <div className={`h-3 w-2/3 ${pulse}`} />
        </div>
      ))}
    </div>
  );
}
