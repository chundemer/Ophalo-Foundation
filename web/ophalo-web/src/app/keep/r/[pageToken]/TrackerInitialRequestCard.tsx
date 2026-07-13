import { AlertTriangle } from "lucide-react";

export function TrackerInitialRequestCard({
  description,
  intakeUrgency,
  businessName,
}: {
  description: string | null;
  intakeUrgency: string | null;
  businessName: string;
}) {
  return (
    <div className="rounded-2xl border border-[var(--ophalo-border)] bg-card px-5 py-5 shadow-sm">
      <p className="text-[10px] font-semibold uppercase tracking-widest text-muted-foreground">
        Initial Request
      </p>
      <p className="mt-2 text-sm font-semibold text-foreground">Your original message</p>
      {description && (
        <div className="mt-2 rounded-lg border border-[var(--ophalo-border)] px-4 py-3">
          <p className="text-sm leading-6 text-foreground">{description}</p>
        </div>
      )}
      {intakeUrgency && (
        <div className="mt-3 flex gap-2 rounded-lg px-3 py-2.5" style={{ background: "var(--ophalo-attention-bg)" }}>
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" style={{ color: "var(--ophalo-attention)" }} />
          <div>
            <p className="text-xs font-semibold" style={{ color: "var(--ophalo-attention)" }}>
              {intakeUrgency === "urgent" ? "Marked urgent" : "Marked as soon"}
            </p>
            <p className="mt-0.5 text-xs" style={{ color: "var(--ophalo-attention)" }}>
              {intakeUrgency === "urgent"
                ? `Your request has been flagged as urgent for ${businessName}.`
                : `You requested a quick turnaround from ${businessName}.`}
            </p>
          </div>
        </div>
      )}
    </div>
  );
}
