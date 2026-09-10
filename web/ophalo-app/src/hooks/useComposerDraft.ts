import { useSyncExternalStore } from "react";

// GAP-073 / ADR-502: per-request, tab-scoped composer draft safety.
//
// The customer-update message + optional status change and the internal-note text on the
// request-detail composer are working text an operator can lose to a Prev/Next navigation, a
// stale-version 409 ("Refresh…"), or a Cmd-R / tab close. This module keeps that text in one
// shared per-`requestId` store backed by `sessionStorage`, so it survives a reload and can never
// bleed into a different request's composer.
//
// One store per request, NOT one stateful hook instance per consumer: `BusinessUpdateSection`
// (message + status) and `UnifiedComposer` (note) both read the same draft object, so a write or
// a clear-on-success from one composer can never clobber the other composer's newer field.
//
// This is display safety only — it restores text into an input. It does not queue, persist
// server-side, or replay mutations (ADR-403 / ADR-484 stay intact).

export interface ComposerDraft {
  message: string;
  status: string;
  note: string;
}

export type ComposerDraftField = keyof ComposerDraft;

const EMPTY: ComposerDraft = { message: "", status: "", note: "" };
const KEY_PREFIX = "keep:composer-draft:";

function storageKey(requestId: string): string {
  return KEY_PREFIX + requestId;
}

function readStorage(requestId: string): ComposerDraft {
  try {
    const raw = window.sessionStorage.getItem(storageKey(requestId));
    if (raw == null) return EMPTY;
    const parsed: unknown = JSON.parse(raw);
    if (parsed == null || typeof parsed !== "object") return EMPTY;
    const record = parsed as Record<string, unknown>;
    return {
      message: typeof record.message === "string" ? record.message : "",
      status: typeof record.status === "string" ? record.status : "",
      note: typeof record.note === "string" ? record.note : "",
    };
  } catch {
    // Private-mode / disabled storage / malformed value — start empty; the in-memory store still
    // protects Prev/Next within this tab session.
    return EMPTY;
  }
}

function writeStorage(requestId: string, draft: ComposerDraft): void {
  try {
    if (!draft.message && !draft.status && !draft.note) {
      window.sessionStorage.removeItem(storageKey(requestId));
      return;
    }
    window.sessionStorage.setItem(storageKey(requestId), JSON.stringify(draft));
  } catch {
    // Storage unavailable — the draft is still held in memory for this tab session; only a full
    // document reload loses it, which is the pre-existing behaviour.
  }
}

interface ComposerDraftStore {
  getSnapshot: () => ComposerDraft;
  subscribe: (listener: () => void) => () => void;
  setField: (field: ComposerDraftField, value: string) => void;
  clearFields: (fields: ComposerDraftField[]) => void;
}

const stores = new Map<string, ComposerDraftStore>();

function getStore(requestId: string): ComposerDraftStore {
  const existing = stores.get(requestId);
  if (existing) return existing;

  let state: ComposerDraft = readStorage(requestId);
  const listeners = new Set<() => void>();

  const commit = (next: ComposerDraft): void => {
    state = next;
    writeStorage(requestId, next);
    for (const listener of listeners) listener();
  };

  const store: ComposerDraftStore = {
    getSnapshot: () => state,
    subscribe: (listener) => {
      listeners.add(listener);
      return () => {
        listeners.delete(listener);
      };
    },
    setField: (field, value) => {
      if (state[field] === value) return;
      commit({ ...state, [field]: value });
    },
    clearFields: (fields) => {
      if (fields.every((field) => state[field] === "")) return;
      const next = { ...state };
      for (const field of fields) next[field] = "";
      commit(next);
    },
  };

  stores.set(requestId, store);
  return store;
}

export interface ComposerDraftHandle {
  draft: ComposerDraft;
  setField: (field: ComposerDraftField, value: string) => void;
  clearFields: (fields: ComposerDraftField[]) => void;
}

export function useComposerDraft(requestId: string): ComposerDraftHandle {
  const store = getStore(requestId);
  const draft = useSyncExternalStore(store.subscribe, store.getSnapshot, store.getSnapshot);
  return { draft, setField: store.setField, clearFields: store.clearFields };
}

// --- test-only helpers -----------------------------------------------------------------------

/** Reset the in-memory store cache so a test starts from `sessionStorage` (or empty). */
export function __resetComposerDrafts(): void {
  stores.clear();
}

/** Prime a request's draft (writes `sessionStorage` and drops any cached store for it). */
export function seedComposerDraft(requestId: string, partial: Partial<ComposerDraft>): void {
  stores.delete(requestId);
  writeStorage(requestId, { ...EMPTY, ...readStorage(requestId), ...partial });
}
