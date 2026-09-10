import { useEffect } from "react";

// GAP-073 / ADR-502: a shared registry of "there is unsaved text here" ids driving one
// `beforeunload` prompt.
//
// Only `beforeunload` can raise the browser's native "Leave site?" dialog. `pagehide` /
// `visibilitychange` cannot prompt, and a hard crash runs no handler — draft recovery for those
// paths is the synchronous `sessionStorage` write in `useComposerDraft`, not a listener here.

const dirtyIds = new Set<string>();
let listenerAttached = false;

function handleBeforeUnload(event: BeforeUnloadEvent): void {
  if (dirtyIds.size === 0) return;
  event.preventDefault();
  // Legacy Chrome/Firefox still require a truthy `returnValue` to show the prompt.
  event.returnValue = "";
}

function ensureListener(): void {
  if (listenerAttached || typeof window === "undefined") return;
  window.addEventListener("beforeunload", handleBeforeUnload);
  listenerAttached = true;
}

export function markDirty(id: string): void {
  dirtyIds.add(id);
  ensureListener();
}

export function clearDirty(id: string): void {
  dirtyIds.delete(id);
}

/**
 * Registers `id` as holding unsaved input while `isDirty` is true; deregisters it otherwise and
 * on unmount. The single `beforeunload` listener is installed lazily on first dirty id and left
 * attached (it is a no-op while the set is empty).
 */
export function useUnloadGuard(id: string, isDirty: boolean): void {
  useEffect(() => {
    if (!isDirty) {
      clearDirty(id);
      return undefined;
    }
    markDirty(id);
    return () => clearDirty(id);
  }, [id, isDirty]);
}

// --- test-only helpers -----------------------------------------------------------------------

export function __unloadGuardState(): { dirtyIds: string[]; listenerAttached: boolean } {
  return { dirtyIds: [...dirtyIds], listenerAttached };
}

export function __resetUnloadGuard(): void {
  dirtyIds.clear();
}
