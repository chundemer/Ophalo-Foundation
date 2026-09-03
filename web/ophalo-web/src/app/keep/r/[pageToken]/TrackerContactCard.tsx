import { KeepConfiguredContact } from "@/components/keep/KeepPublicShell";

export function TrackerContactCard({
  businessName,
  phone,
  websiteUrl,
}: {
  businessName: string;
  phone: string | null;
  websiteUrl: string | null;
}) {
  const hasPhone = phone != null && phone.trim().length > 0;
  const hasWebsite = websiteUrl != null && websiteUrl.startsWith("https://");
  if (!hasPhone && !hasWebsite) return null;

  return (
    <div className="rounded-2xl border border-[var(--ophalo-border)] bg-card px-5 py-5 shadow-sm">
      <p className="text-[10px] font-semibold uppercase tracking-widest text-muted-foreground">
        Contact
      </p>
      <p className="mt-2 text-sm font-semibold text-foreground">{businessName}</p>
      <p className="mt-0.5 text-sm text-muted-foreground">
        Reach {businessName} directly about this request.
      </p>
      <KeepConfiguredContact phone={phone} websiteUrl={websiteUrl} className="mt-3" />
    </div>
  );
}
