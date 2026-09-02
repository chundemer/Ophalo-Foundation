import { useEffect, useMemo, useRef, useState } from "react";
import {
  AlertTriangle,
  ArrowLeft,
  Check,
  CircleAlert,
  Mail,
  MessageSquare,
  Phone,
} from "lucide-react";
import {
  type ActualWorkFinancialDetailResult,
  type ActualWorkFinancialResolutionBody,
  type KeepRequestDetailResult,
} from "../../lib/apiClient";

type ActualWorkFinancialLineEntry =
  ActualWorkFinancialDetailResult["lines"][number];
import { KeepBadge } from "../../components/keep/KeepBadge";
import { KeepButton } from "../../components/keep/KeepButton";
import { statusBadgeVariant, statusLabel } from "../../lib/requestStatus";
import { formatDate } from "./helpers";
import { type FinancialReviewOutcome } from "./useActualWorkFinancialReview";
import { FinancialResolutionForm } from "./FinancialResolutionForm";
import { NoChargeDispositionForm } from "./NoChargeDispositionForm";
import { ReplaceVisitForm } from "./ReplaceVisitForm";

/**
 * BL136 4f-ii successor layout: the wide-viewport, Owner/Admin-only Actual Work financial-review
 * workspace. A two-column desktop screen — a Job & Customer context rail beside the financial
 * audit surface (summary totals, line-item breakdown, reviewer note + actions). Presentation only:
 * every mutation, guard, and copy string is the existing `ActualWorkReviewCard` behavior, which
 * still serves the narrow Request Detail path unchanged. Non-reviewers keep the price-blind
 * `ReadOnlyVisit` render on this route.
 */

const OUTCOME_LABELS: Record<string, string> = {
  DiagnosticOnly: "Diagnostic only — no work performed",
  NoWorkAuthorized: "No work authorized",
  NoAccess: "No access to the site",
};

type MarginTone = "healthy" | "thin" | "negative" | "none";

// ADR-approved thresholds (Christian, 2026-09-01): >=15% healthy, 0%..<15% thin margin worth
// attention, <0% negative. Missing financial data renders a neutral dash, never a tone.
function marginTone(pct: number | null): MarginTone {
  if (pct == null) return "none";
  if (pct < 0) return "negative";
  if (pct < 15) return "thin";
  return "healthy";
}

const TONE_TEXT: Record<MarginTone, string> = {
  healthy: "text-[var(--ophalo-success)]",
  thin: "text-[var(--ophalo-attention)]",
  negative: "text-[var(--ophalo-danger)]",
  none: "text-[var(--ophalo-ink)]",
};

// Soft tinted fill for the margin KPI cards, so the job's profitability reads at a glance without
// a saturated block of color. Hairline stays neutral; a value we can't judge yet stays white.
const TONE_CARD: Record<MarginTone, string> = {
  healthy: "bg-[var(--ophalo-success-bg)]",
  thin: "bg-[var(--ophalo-attention-bg)]",
  negative: "bg-[var(--ophalo-danger-bg)]",
  none: "bg-[var(--ophalo-card)]",
};

function currency(value: number | null) {
  return value == null
    ? "—"
    : value.toLocaleString(undefined, { style: "currency", currency: "USD" });
}

function marginPct(sales: number | null, margin: number | null): number | null {
  if (sales == null || margin == null || sales === 0) return null;
  return (margin / sales) * 100;
}

function formatPct(pct: number | null) {
  return pct == null ? "—" : `${pct.toFixed(1)}%`;
}

function composeAddress(request: KeepRequestDetailResult): string {
  return [
    request.serviceAddressLine1,
    request.serviceAddressLine2,
    request.serviceCity && request.serviceState
      ? `${request.serviceCity}, ${request.serviceState}${request.serviceZip ? ` ${request.serviceZip}` : ""}`
      : null,
  ]
    .filter(Boolean)
    .join(", ");
}

interface ActualWorkFinancialReviewWorkspaceProps {
  request: KeepRequestDetailResult;
  visit: ActualWorkFinancialDetailResult;
  /** 1-based position of this visit among the request's non-superseded submitted visits. */
  visitNumber: number;
  onExit: () => void;
  /** Opens the shared Contact customer drawer. `channel` is the existing label key —
   *  `"phone"` | `"sms"` | `"email"`; `direction` is `"outbound"` for these shortcuts. */
  onContactLaunch: (direction: string, channel: string) => void;
  onReview: (
    visit: ActualWorkFinancialDetailResult,
    note: string | null,
  ) => Promise<FinancialReviewOutcome>;
  onResolveLine: (
    visit: ActualWorkFinancialDetailResult,
    lineId: string,
    body: ActualWorkFinancialResolutionBody,
  ) => Promise<FinancialReviewOutcome>;
  onRecordNoChargeDisposition: (
    visit: ActualWorkFinancialDetailResult,
    reason: string,
  ) => Promise<FinancialReviewOutcome>;
  onReplace: (
    visit: ActualWorkFinancialDetailResult,
    reason: string,
  ) => Promise<FinancialReviewOutcome>;
  isVisitMutating: (visitId: string) => boolean;
  onReviewSuccess: () => void;
}

export function ActualWorkFinancialReviewWorkspace({
  request,
  visit,
  visitNumber,
  onExit,
  onContactLaunch,
  onReview,
  onResolveLine,
  onRecordNoChargeDisposition,
  onReplace,
  isVisitMutating,
  onReviewSuccess,
}: ActualWorkFinancialReviewWorkspaceProps) {
  const headingRef = useRef<HTMLHeadingElement>(null);
  useEffect(() => {
    headingRef.current?.focus();
  }, []);

  const reviewed = visit.reviewedAtUtc != null;
  const busy = isVisitMutating(visit.id);
  const zeroLine = visit.lines.length === 0;

  // Client-side mirror of the domain's fail-closed MarkReviewed gate: a line missing a price/cost
  // snapshot, or a zero-line visit with no no-charge disposition, blocks completion. The server
  // stays authoritative; this just disables the primary action and says why.
  const hasIncompleteLine =
    visit.hasIncompleteFinancialData || visit.lines.some((line) => !line.isFinancialDataComplete);
  const needsNoCharge = zeroLine && !visit.hasNoChargeDisposition;
  const reviewBlocked = hasIncompleteLine || needsNoCharge;

  const recorderName = useMemo(() => {
    const match = request.participants?.find(
      (p) => p.accountUserId === visit.recorderAccountUserId,
    );
    return match?.displayName ?? null;
  }, [request.participants, visit.recorderAccountUserId]);

  const pct = marginPct(visit.totalSalesPrice, visit.totalMargin);
  const totalTone = marginTone(pct);

  const [note, setNote] = useState(visit.reviewNote ?? "");
  const [notice, setNotice] = useState<string | null>(null);

  async function markReviewed() {
    if (busy || reviewBlocked) return;
    setNotice(null);
    const outcome = await onReview(visit, note.trim() || null);
    if (outcome.kind === "success") {
      onReviewSuccess();
      return;
    }
    if (outcome.kind === "hidden") return;
    setNotice(
      outcome.kind === "review-blocked-incomplete"
        ? "Resolve the missing pricing or cost on every line before completing internal financial review."
        : outcome.kind === "review-blocked-zero-line"
          ? "Record this visit as no charge before completing internal financial review."
          : outcome.kind === "reconciled"
            ? "This visit was already reviewed or changed. The latest record is shown below."
            : "Unable to complete internal financial review. Try again.",
    );
  }

  const address = composeAddress(request);
  const outcomeLabel = visit.outcome
    ? (OUTCOME_LABELS[visit.outcome] ?? visit.outcome)
    : null;

  return (
    <div>
      {/* Header sits on a full-bleed white band; the workspace canvas begins below it. */}
      <header className="border-b border-[var(--ophalo-border)] bg-[var(--ophalo-card)]">
        <div className="mx-auto w-full max-w-6xl px-4 py-4 md:px-6">
          <div className="flex flex-wrap items-center gap-x-2 text-sm">
            <button
              type="button"
              onClick={onExit}
              className="inline-flex items-center gap-1 font-medium text-[var(--keep-accent)] hover:underline rounded focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2"
            >
              <ArrowLeft className="h-3.5 w-3.5" />
              Back to Request
            </button>
            <span className="text-[var(--ophalo-muted)]">
              / {request.referenceCode}
            </span>
          </div>
          <div className="mt-2 flex flex-wrap items-start justify-between gap-x-4 gap-y-2">
            <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
              <h1
                ref={headingRef}
                tabIndex={-1}
                className="font-serif text-xl font-semibold text-[var(--ophalo-ink)] focus:outline-none"
              >
                Actual Work Financial Review — Visit #{visitNumber}
              </h1>
              {reviewed ? (
                <KeepBadge variant="success">Review complete</KeepBadge>
              ) : (
                <KeepBadge variant="attention">Pending review</KeepBadge>
              )}
            </div>
            <div className="text-right text-xs text-[var(--ophalo-muted)]">
              <p>Submitted {formatDate(visit.submittedAtUtc)}</p>
              {recorderName && <p className="mt-0.5">by {recorderName}</p>}
            </div>
          </div>
        </div>
      </header>

      <div className="mx-auto w-full max-w-6xl px-4 py-6 md:px-6">
        <div className="grid grid-cols-1 gap-5 min-[1001px]:grid-cols-[32fr_68fr]">
          {/* Context rail — before the financial content in source order for the stacked view. */}
          <div className="space-y-4">
            <ContextCard
              request={request}
              address={address}
              onContactLaunch={onContactLaunch}
            />
            <FieldNotesCard
              visitNote={visit.visitNote ?? null}
              recorderName={recorderName}
            />
          </div>

          {/* Financial audit surface. */}
          <div className="space-y-4">
            <div className="grid grid-cols-2 gap-3 min-[1200px]:grid-cols-4">
              <SummaryCard
                label="Total sales price"
                value={currency(visit.totalSalesPrice)}
              />
              <SummaryCard
                label="Standard direct cost"
                value={currency(visit.totalStandardExpectedDirectCost)}
              />
              <SummaryCard
                label="Expected margin"
                value={currency(visit.totalMargin)}
                tone={totalTone}
              />
              <SummaryCard
                label="Margin %"
                value={formatPct(pct)}
                tone={totalTone}
              />
            </div>

            <section className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] shadow-[var(--ophalo-shadow-card)]">
              <div className="flex flex-wrap items-center justify-between gap-2 border-b border-[var(--ophalo-border)] px-4 py-3">
                <h2 className="text-sm font-semibold text-[var(--ophalo-ink)]">
                  Line Item Breakdown
                  {visit.lines.length
                    ? ` (${visit.lines.length} ${visit.lines.length === 1 ? "item" : "items"})`
                    : ""}
                </h2>
                {!zeroLine && !visit.hasIncompleteFinancialData && (
                  <span className="text-xs text-[var(--ophalo-muted)]">
                    All direct costs snapshot-backed
                  </span>
                )}
              </div>

              {visit.hasIncompleteFinancialData && (
                <p className="mx-4 mt-3 flex items-center gap-1.5 rounded-lg bg-[var(--ophalo-attention-bg)] px-3 py-2 text-xs font-medium text-[var(--ophalo-attention)]">
                  <CircleAlert className="h-4 w-4 shrink-0" />
                  Missing cost data — visit totals and margin are unavailable.
                </p>
              )}

              {zeroLine ? (
                <div className="px-4 py-4">
                  <p className="text-xs text-[var(--ophalo-muted)]">
                    No work lines were recorded for this visit.
                  </p>
                  {visit.hasNoChargeDisposition && (
                    <p className="mt-1.5 flex items-center gap-1 text-xs font-semibold text-[var(--ophalo-success)]">
                      <Check className="h-3.5 w-3.5" /> Recorded as no charge
                    </p>
                  )}
                  {!reviewed && !visit.hasNoChargeDisposition && (
                    <NoChargeDispositionForm
                      busy={busy}
                      onSubmit={(reason) =>
                        onRecordNoChargeDisposition(visit, reason)
                      }
                    />
                  )}
                </div>
              ) : (
                <>
                  <LineItemTable
                    lines={visit.lines}
                    totalSalesPrice={visit.totalSalesPrice}
                    totalStandardExpectedDirectCost={
                      visit.totalStandardExpectedDirectCost
                    }
                    totalPct={pct}
                  />
                  {!reviewed &&
                    visit.blockers.map((blocker) => (
                      <div key={blocker.lineId} className="px-4">
                        <FinancialResolutionForm
                          blocker={blocker}
                          busy={busy}
                          onSubmit={(lineId, body) =>
                            onResolveLine(visit, lineId, body)
                          }
                        />
                      </div>
                    ))}
                  <div className="h-3" />
                </>
              )}
            </section>

            {/* Final review card — reviewer note + corrective / completion actions. */}
            <section className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-4 py-4 shadow-[var(--ophalo-shadow-card)]">
              <h2 className="text-sm font-semibold text-[var(--ophalo-ink)]">
                Internal financial review
              </h2>
              <p className="mt-0.5 text-xs text-[var(--ophalo-muted)]">
                Reviews the submitted visit&rsquo;s financial details. Does not
                change the customer request.
              </p>

              {(outcomeLabel || visit.completionNote) && (
                <div className="mt-3 rounded-lg bg-[var(--ophalo-surface-muted)] px-3 py-2 text-xs">
                  {outcomeLabel && (
                    <p className="font-semibold text-[var(--ophalo-ink)]">
                      {outcomeLabel}
                    </p>
                  )}
                  {visit.completionNote && (
                    <p className="mt-0.5 text-[var(--ophalo-muted)]">
                      {visit.completionNote}
                    </p>
                  )}
                </div>
              )}

              {reviewed ? (
                <div className="mt-3 flex items-start gap-2 text-xs text-[var(--ophalo-muted)]">
                  <Check className="mt-0.5 h-3.5 w-3.5 shrink-0 text-[var(--ophalo-success)]" />
                  <span>
                    <span className="font-semibold text-[var(--ophalo-success)]">
                      Financial review completed
                    </span>{" "}
                    · reviewed {formatDate(visit.reviewedAtUtc!)} by{" "}
                    {visit.reviewedByDisplayName ?? "an authorized reviewer"}
                    {visit.reviewNote ? ` · “${visit.reviewNote}”` : ""}
                  </span>
                </div>
              ) : (
                <>
                  {notice && (
                    <p
                      role="alert"
                      className="mt-3 text-xs text-[var(--ophalo-danger)]"
                    >
                      {notice}
                    </p>
                  )}
                  <div className="mt-3">
                    <label
                      htmlFor={`review-note-${visit.id}`}
                      className="text-xs font-semibold text-[var(--ophalo-ink)]"
                    >
                      Reviewer internal note{" "}
                      <span className="font-normal text-[var(--ophalo-muted)]">
                        (optional — for billing/payroll audit)
                      </span>
                    </label>
                    <textarea
                      id={`review-note-${visit.id}`}
                      value={note}
                      onChange={(event) => setNote(event.target.value)}
                      placeholder="Add internal note regarding pricing, technician hours, or billing clearance…"
                      rows={3}
                      className="mt-1 w-full rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-3 py-2 text-sm text-[var(--ophalo-ink)] placeholder:text-[var(--ophalo-muted)] focus:outline-none focus:ring-2 focus:ring-[var(--keep-accent)] focus:border-[var(--keep-accent)]"
                    />
                  </div>
                  {reviewBlocked && (
                    <p className="mt-3 flex items-center gap-1.5 text-xs font-medium text-[var(--ophalo-attention)]">
                      <AlertTriangle className="h-3.5 w-3.5 shrink-0" />
                      {hasIncompleteLine
                        ? "Resolve every line’s missing price or cost before completing review."
                        : "Record this visit as no charge before completing review."}
                    </p>
                  )}
                  <div className="mt-3 flex flex-wrap items-start justify-between gap-3">
                    <div className="min-w-0">
                      <ReplaceVisitForm
                        presentation="button"
                        busy={busy}
                        onSubmit={(reason) => onReplace(visit, reason)}
                      />
                    </div>
                    <KeepButton
                      onClick={() => void markReviewed()}
                      disabled={busy || reviewBlocked}
                    >
                      {busy ? "Working…" : "Complete internal financial review"}
                    </KeepButton>
                  </div>
                </>
              )}
            </section>
          </div>
        </div>
      </div>
    </div>
  );
}

function SummaryCard({
  label,
  value,
  tone = "none",
}: {
  label: string;
  value: string;
  tone?: MarginTone;
}) {
  return (
    <div
      className={`rounded-xl border border-[var(--ophalo-border)] px-4 py-3 shadow-[var(--ophalo-shadow-card)] ${TONE_CARD[tone]}`}
    >
      <p className="text-[10px] font-bold uppercase tracking-[0.1em] text-[var(--ophalo-muted)]">
        {label}
      </p>
      <p className={`mt-1 text-lg font-semibold ${TONE_TEXT[tone]}`}>{value}</p>
    </div>
  );
}

function SectionLabel({ children }: { children: React.ReactNode }) {
  return (
    <p className="text-[10px] font-bold uppercase tracking-[0.1em] text-[var(--ophalo-muted)]">
      {children}
    </p>
  );
}

function ContactButton({
  icon: Icon,
  label,
  onClick,
}: {
  icon: typeof Phone;
  label: string;
  onClick: () => void;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className="inline-flex items-center gap-1 rounded-lg border border-[var(--ophalo-border)] px-2.5 py-1 text-xs font-medium text-[var(--ophalo-ink)] hover:bg-[var(--ophalo-surface-muted)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2"
    >
      <Icon className="h-3.5 w-3.5" /> {label}
    </button>
  );
}

function ContextCard({
  request,
  address,
  onContactLaunch,
}: {
  request: KeepRequestDetailResult;
  address: string;
  onContactLaunch: (direction: string, channel: string) => void;
}) {
  const phone = request.customerPhone?.trim();
  const email = request.customerEmail?.trim();

  return (
    <section className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] shadow-[var(--ophalo-shadow-card)]">
      <div className="flex items-center justify-between gap-2 border-b border-[var(--ophalo-border)] px-4 py-3">
        <h2 className="text-[10px] font-bold uppercase tracking-[0.1em] text-[var(--ophalo-muted)]">
          Job &amp; Customer Context
        </h2>
        {request.status && (
          <KeepBadge variant={statusBadgeVariant(request.status)}>
            {statusLabel(request.status)}
          </KeepBadge>
        )}
      </div>

      <div className="space-y-3 px-4 py-3 text-sm">
        <div>
          <SectionLabel>Customer</SectionLabel>
          <p className="mt-1 font-semibold text-[var(--ophalo-ink)]">
            {request.customerName}
          </p>
        </div>

        <div className="border-t border-[var(--ophalo-border-subtle)] pt-3">
          <SectionLabel>Request</SectionLabel>
          <p className="mt-1 text-[var(--ophalo-ink)]">{request.referenceCode}</p>
        </div>

        <div className="border-t border-[var(--ophalo-border-subtle)] pt-3">
          <SectionLabel>Service location</SectionLabel>
          <p className="mt-1 text-[var(--ophalo-ink)]">
            {address || (
              <span className="text-[var(--ophalo-muted)]">Not on file</span>
            )}
          </p>
        </div>

        {(phone || email) && (
          <div className="border-t border-[var(--ophalo-border-subtle)] pt-3">
            <SectionLabel>Customer contact</SectionLabel>
            {phone && <p className="mt-1 text-[var(--ophalo-ink)]">{phone}</p>}
            {email && (
              <p className="text-xs break-words text-[var(--ophalo-muted)]">
                {email}
              </p>
            )}
            <div className="mt-2 flex flex-wrap gap-2">
              {phone && (
                <>
                  <ContactButton
                    icon={Phone}
                    label="Call"
                    onClick={() => onContactLaunch("outbound", "phone")}
                  />
                  <ContactButton
                    icon={MessageSquare}
                    label="Text"
                    onClick={() => onContactLaunch("outbound", "sms")}
                  />
                </>
              )}
              {email && (
                <ContactButton
                  icon={Mail}
                  label="Email"
                  onClick={() => onContactLaunch("outbound", "email")}
                />
              )}
            </div>
          </div>
        )}

        {request.description && (
          <div className="border-t border-[var(--ophalo-border-subtle)] pt-3">
            <SectionLabel>Customer need</SectionLabel>
            <p className="mt-1 whitespace-pre-wrap rounded-lg bg-[var(--ophalo-attention-bg)] px-3 py-2 text-sm leading-6 text-[var(--ophalo-ink)]">
              {request.description}
            </p>
          </div>
        )}
      </div>
    </section>
  );
}

function FieldNotesCard({
  visitNote,
  recorderName,
}: {
  visitNote: string | null;
  recorderName: string | null;
}) {
  return (
    <section className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-4 py-3 shadow-[var(--ophalo-shadow-card)]">
      <h2 className="text-[10px] font-bold uppercase tracking-[0.1em] text-[var(--ophalo-muted)]">
        Technician Field Notes
      </h2>
      {visitNote?.trim() ? (
        <p className="mt-2 whitespace-pre-wrap rounded-lg bg-[var(--ophalo-surface-muted)] px-3 py-2 text-sm italic leading-6 text-[var(--ophalo-ink)]">
          {visitNote}
        </p>
      ) : (
        <p className="mt-2 text-sm text-[var(--ophalo-muted)]">
          No field notes recorded.
        </p>
      )}
      <p className="mt-2 text-xs text-[var(--ophalo-muted)]">
        Recorded by {recorderName ?? "a team member"}
      </p>
    </section>
  );
}

function LineMarginCell({ line }: { line: ActualWorkFinancialLineEntry }) {
  if (!line.isFinancialDataComplete) {
    return (
      <span className="inline-flex items-center gap-1 rounded-full bg-[var(--ophalo-attention-bg)] px-2 py-0.5 text-[11px] font-semibold text-[var(--ophalo-attention)]">
        <AlertTriangle className="h-3 w-3 shrink-0" />
        Resolve cost
      </span>
    );
  }
  const pct = marginPct(line.lineSalesTotal, line.lineMargin);
  const resolved = line.sellPriceResolved || line.directCostResolved;
  return (
    <span className={`font-semibold ${TONE_TEXT[marginTone(pct)]}`}>
      {formatPct(pct)}
      {resolved && (
        <span className="ml-1 text-[10px] font-medium text-[var(--ophalo-muted)]">
          (resolved)
        </span>
      )}
    </span>
  );
}

function LineItemTable({
  lines,
  totalSalesPrice,
  totalStandardExpectedDirectCost,
  totalPct,
}: {
  lines: ActualWorkFinancialLineEntry[];
  totalSalesPrice: number | null;
  totalStandardExpectedDirectCost: number | null;
  totalPct: number | null;
}) {
  const totalQty = lines.reduce((sum, l) => sum + l.actualQuantity, 0);
  const showTotals = totalSalesPrice != null;

  return (
    <>
      {/* Desktop / wide table. */}
      <div className="hidden overflow-x-auto min-[900px]:block">
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-[var(--ophalo-border)] text-left text-[10px] font-bold uppercase tracking-[0.08em] text-[var(--ophalo-muted)]">
              <th scope="col" className="px-4 py-2 font-bold">
                Item description
              </th>
              <th scope="col" className="px-4 py-2 font-bold">
                Performed by
              </th>
              <th scope="col" className="px-4 py-2 text-right font-bold">
                Qty
              </th>
              <th scope="col" className="px-4 py-2 text-right font-bold">
                Sell price
              </th>
              <th scope="col" className="px-4 py-2 text-right font-bold">
                Direct cost
              </th>
              <th scope="col" className="px-4 py-2 text-right font-bold">
                Margin (%)
              </th>
            </tr>
          </thead>
          <tbody className="divide-y divide-[var(--ophalo-border-subtle)]">
            {lines.map((line) => (
              <tr key={line.id}>
                <td className="px-4 py-2.5 font-medium text-[var(--ophalo-ink)]">
                  {line.displayNameSnapshot}
                </td>
                <td className="px-4 py-2.5 text-[var(--ophalo-muted)]">
                  {line.performerDisplayName ?? "Unknown"}
                </td>
                <td className="px-4 py-2.5 text-right tabular-nums text-[var(--ophalo-ink)]">
                  {line.actualQuantity}
                </td>
                <td className="px-4 py-2.5 text-right tabular-nums text-[var(--ophalo-ink)]">
                  {currency(line.lineSalesTotal)}
                </td>
                <td className="px-4 py-2.5 text-right tabular-nums text-[var(--ophalo-ink)]">
                  {currency(line.lineStandardExpectedDirectCostTotal)}
                </td>
                <td className="px-4 py-2.5 text-right tabular-nums">
                  <LineMarginCell line={line} />
                </td>
              </tr>
            ))}
          </tbody>
          {showTotals && (
            <tfoot>
              <tr className="border-t border-[var(--ophalo-border)] text-[10px] font-bold uppercase tracking-[0.08em] text-[var(--ophalo-muted)]">
                <td className="px-4 py-2.5">Totals</td>
                <td className="px-4 py-2.5" />
                <td className="px-4 py-2.5 text-right tabular-nums">
                  {totalQty}
                </td>
                <td className="px-4 py-2.5 text-right tabular-nums text-[var(--ophalo-ink)]">
                  {currency(totalSalesPrice)}
                </td>
                <td className="px-4 py-2.5 text-right tabular-nums text-[var(--ophalo-ink)]">
                  {currency(totalStandardExpectedDirectCost)}
                </td>
                <td
                  className={`px-4 py-2.5 text-right tabular-nums ${TONE_TEXT[marginTone(totalPct)]}`}
                >
                  {formatPct(totalPct)}
                </td>
              </tr>
            </tfoot>
          )}
        </table>
      </div>

      {/* Narrow stacked rows — no forced horizontal overflow. */}
      <ul className="divide-y divide-[var(--ophalo-border-subtle)] min-[900px]:hidden">
        {lines.map((line) => (
          <li key={line.id} className="px-4 py-3 text-sm">
            <div className="flex items-baseline justify-between gap-2">
              <span className="font-medium text-[var(--ophalo-ink)]">
                {line.displayNameSnapshot}
              </span>
              <span className="tabular-nums text-xs text-[var(--ophalo-muted)]">
                ×{line.actualQuantity}
              </span>
            </div>
            <p className="mt-0.5 text-xs text-[var(--ophalo-muted)]">
              {line.performerDisplayName ?? "Unknown"}
            </p>
            <dl className="mt-1.5 grid grid-cols-3 gap-2 text-xs">
              <div>
                <dt className="text-[var(--ophalo-muted)]">Sell</dt>
                <dd className="tabular-nums text-[var(--ophalo-ink)]">
                  {currency(line.lineSalesTotal)}
                </dd>
              </div>
              <div>
                <dt className="text-[var(--ophalo-muted)]">Cost</dt>
                <dd className="tabular-nums text-[var(--ophalo-ink)]">
                  {currency(line.lineStandardExpectedDirectCostTotal)}
                </dd>
              </div>
              <div>
                <dt className="text-[var(--ophalo-muted)]">Margin</dt>
                <dd className="tabular-nums">
                  <LineMarginCell line={line} />
                </dd>
              </div>
            </dl>
          </li>
        ))}
        {showTotals && (
          <li className="flex items-center justify-between px-4 py-3 text-xs font-bold uppercase tracking-[0.08em] text-[var(--ophalo-muted)]">
            <span>Totals</span>
            <span className={`tabular-nums ${TONE_TEXT[marginTone(totalPct)]}`}>
              {currency(totalSalesPrice)} · {formatPct(totalPct)}
            </span>
          </li>
        )}
      </ul>
    </>
  );
}
