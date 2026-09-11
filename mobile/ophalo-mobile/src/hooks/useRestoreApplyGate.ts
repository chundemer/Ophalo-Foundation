import { useEffect, useState } from 'react';

/**
 * Runs `applyRestoredDraft` exactly once, synchronously within the same effect/render pass
 * that flips the returned flag to `true` — whether or not there was anything to restore.
 *
 * This is what closes the hydration/autosave race caught in GAP-091 review: without it, a
 * consumer's autosave effect keyed on `hydrated` alone would fire once on the first
 * post-hydration render, before a *separate* restore effect (queued behind it) had copied the
 * restored fields into state — discarding the just-restored draft with a blank-state write.
 * Gating autosave on this hook's return value instead means the render where the gate first
 * turns `true` already carries the restored values, because React batches this hook's
 * `applyRestoredDraft` state updates together with the `true` flip into one commit.
 */
export function useRestoreApplyGate<T>(
  hydrated: boolean,
  restoredDraft: T | null,
  applyRestoredDraft: (draft: T) => void,
): boolean {
  const [applied, setApplied] = useState(false);

  useEffect(() => {
    if (!hydrated) return;
    if (restoredDraft) applyRestoredDraft(restoredDraft);
    setApplied(true);
    // Intentionally hydrated-only: restoredDraft/applyRestoredDraft must not re-run this effect
    // on every subsequent change (restoredDraft is set once by the hydrate hook; re-applying it
    // later would stomp in-progress edits).
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [hydrated]);

  return applied;
}
