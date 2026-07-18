import { KeepBusinessHeader, KeepConfiguredContact } from "@/components/keep/KeepPublicShell";
import { trackerCanvasStyle } from "./tracker-types";

export function TrackerExpiredView({
  businessName,
  logoUrl,
  websiteUrl,
  phone,
  referenceCode,
}: {
  businessName: string;
  logoUrl?: string | null;
  websiteUrl?: string | null;
  phone?: string | null;
  referenceCode: string;
}) {
  return (
    <main className="min-h-screen px-4 py-6 sm:py-10" style={trackerCanvasStyle}>
      <div className="mx-auto w-full max-w-2xl space-y-4">
        <KeepBusinessHeader
          businessName={businessName}
          logoUrl={logoUrl}
          label="Request tracker expired"
          className="pb-1"
        />
        <div className="rounded-2xl border border-[var(--ophalo-border)] bg-card px-5 py-6 shadow-sm">
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
          <KeepConfiguredContact websiteUrl={websiteUrl} phone={phone} className="mt-4" />
        </div>
      </div>
    </main>
  );
}
