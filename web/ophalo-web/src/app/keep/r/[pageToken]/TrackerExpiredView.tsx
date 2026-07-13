import { trackerCanvasStyle } from "./tracker-types";

export function TrackerExpiredView({
  businessName,
  referenceCode,
}: {
  businessName: string;
  referenceCode: string;
}) {
  return (
    <main className="min-h-screen px-4 py-6 sm:py-10" style={trackerCanvasStyle}>
      <div className="mx-auto w-full max-w-2xl rounded-2xl border border-[var(--ophalo-border)] bg-card px-5 py-6 shadow-sm">
        <p className="text-base font-semibold text-foreground">This tracker link has expired.</p>
        <p className="mt-2 text-sm leading-6 text-muted-foreground">
          The tracker for your request with{" "}
          <strong className="text-foreground">{businessName}</strong> is no longer active.
        </p>
        {referenceCode && (
          <p className="mt-2 text-sm text-muted-foreground">
            Reference:{" "}
            <span className="font-mono text-[13px] tracking-widest text-foreground">
              {referenceCode}
            </span>
          </p>
        )}
      </div>
    </main>
  );
}
