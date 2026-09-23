import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { api } from "../../lib/apiClient";
import { KeepButton } from "../../components/keep/KeepButton";

// Maintainability review item 7, slice 2: split out of ActualWorkComposer.tsx (see build-log for
// the composer-family split). No behavior change. INPUT_CLS/FOCUS_RING and the
// SetDefaultPerformerOutcome type are redeclared locally rather than imported, matching the
// established convention in this directory.

const FOCUS_RING =
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2";

const INPUT_CLS =
  `w-full rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] text-sm ` +
  `text-[var(--ophalo-ink)] placeholder:text-[var(--ophalo-muted)] px-3 py-2 ${FOCUS_RING}`;

/** Mirrors `useActualWorkCapture`'s `setDefaultPerformer` return contract (kept local — the hook
 * declares it inline). `set` unmounts this gate on the parent's refetch; the rest stay in place. */
type SetDefaultPerformerOutcome = "set" | "ineligible" | "stale" | "failed";

/** ADR-494 D2: the office-transcription entry point. No add-line / assembly / nudge affordance is
 * mounted while this is showing — the caller must pick the technician the paper ticket belongs to
 * and persist it as the Draft's ticket default first. On a `"set"` outcome the parent refetches and
 * this subtree unmounts; every other outcome keeps the selector in place. */
export function ActualWorkPerformerGate({
  onSetDefaultPerformer,
  onCancel,
  initialSelectedId = "",
}: {
  onSetDefaultPerformer: (performerId: string | null) => Promise<SetDefaultPerformerOutcome>;
  onCancel?: () => void;
  initialSelectedId?: string;
}) {
  const [selected, setSelected] = useState(initialSelectedId);
  const [status, setStatus] = useState<"idle" | "saving" | "ineligible" | "stale" | "failed">("idle");

  const { data, isLoading } = useQuery({
    queryKey: ["actualWorkPerformerCandidates"],
    queryFn: () => api.getActualWorkPerformerCandidates(),
  });
  const candidates = data?.candidates ?? [];

  async function confirm() {
    if (!selected || status === "saving") return;
    setStatus("saving");
    const outcome = await onSetDefaultPerformer(selected);
    setStatus(outcome === "set" ? "idle" : outcome);
  }

  const message =
    status === "ineligible"
      ? "That person can't be recorded as the performer."
      : status === "stale"
        ? "This draft changed elsewhere — reopen it to continue."
        : status === "failed"
          ? "Couldn't save. Try again."
          : null;

  return (
    <div className="rounded-lg border border-[var(--ophalo-border)] p-3 space-y-2">
      <div>
        <p className="flex items-center gap-1 text-sm font-medium text-[var(--ophalo-ink)]">
          Whose work is this?
          <span aria-hidden="true" className="text-[var(--ophalo-danger)]">*</span>
        </p>
        <p className="mt-0.5 text-xs text-[var(--ophalo-muted)]">
          {onCancel
            ? "Confirm the new technician for future items. Existing items keep their recorded performer."
            : "Required — add items after you pick the technician this ticket belongs to."}
        </p>
      </div>
      <select
        value={selected}
        onChange={(e) => setSelected(e.target.value)}
        disabled={isLoading || status === "saving"}
        aria-label="Technician"
        className={INPUT_CLS}
      >
        <option value="">{isLoading ? "Loading…" : "Select a technician"}</option>
        {candidates.map((candidate) => (
          <option key={candidate.accountUserId} value={candidate.accountUserId}>
            {candidate.displayName} — {candidate.role}
          </option>
        ))}
      </select>
      {message && <p className="text-xs text-[var(--ophalo-danger,#c0392b)]">{message}</p>}
      <div className="flex flex-col gap-2 min-[1001px]:flex-row min-[1001px]:justify-end">
        {onCancel && (
          <KeepButton
            variant="secondary"
            disabled={status === "saving"}
            onClick={onCancel}
            className="w-full min-[1001px]:w-auto min-[1001px]:px-6"
          >
            Cancel
          </KeepButton>
        )}
        <KeepButton
          variant="teal"
          disabled={!selected || status === "saving"}
          onClick={() => void confirm()}
          className="w-full min-[1001px]:w-auto min-[1001px]:px-6"
        >
          Confirm technician
        </KeepButton>
      </div>
    </div>
  );
}

