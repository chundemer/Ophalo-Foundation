// Framework-free Quick Capture draft persistence: schema, per-user key, and a debounced
// autosave coordinator whose storage operations are serialized through a per-key promise
// queue (see QuickCaptureDraftCoordinator below — not a generation counter). Kept free of
// React/React Native imports so the coordination logic (the part with real race conditions) is
// directly unit-testable, mirroring phoneUtils.ts's split from useQuickCapture.ts.
//
// GAP-091 (F5.1) — explicit, short-lived, per-user, authenticated-device local draft for the
// pilot field-capture workflow. Cleared on logout and on the 401 forced-sign-out path
// (see src/auth/AuthContext.tsx) so a shared field device never surfaces a prior user's
// captured customer PII.

export const QUICK_CAPTURE_DRAFT_VERSION = 1;

export type QuickCaptureDraftFields = {
  phone: string;
  customerName: string;
  customerEmail: string;
  description: string;
  source: string;
  showAddress: boolean;
  addrLine1: string;
  addrLine2: string;
  addrCity: string;
  addrState: string;
  addrZip: string;
};

export type StoredQuickCaptureDraft = QuickCaptureDraftFields & {
  version: typeof QUICK_CAPTURE_DRAFT_VERSION;
  savedAt: string;
};

export function quickCaptureDraftKey(accountUserId: string): string {
  return `@ophalo/quickCaptureDraft:${accountUserId}:v1`;
}

type StringFieldKey = Exclude<keyof QuickCaptureDraftFields, 'showAddress'>;

const STRING_FIELDS: StringFieldKey[] = [
  'phone',
  'customerName',
  'customerEmail',
  'description',
  'source',
  'addrLine1',
  'addrLine2',
  'addrCity',
  'addrState',
  'addrZip',
];

// `source` always carries a selected default ('phone') and isn't itself evidence of a capture
// in progress — excluded from the blankness check (still validated by parseStoredDraft below).
const CONTENT_FIELDS: StringFieldKey[] = STRING_FIELDS.filter((f) => f !== 'source');

/** No saved-draft-worth content — an all-blank form is not persisted. */
export function isDraftBlank(fields: QuickCaptureDraftFields): boolean {
  return (
    !fields.showAddress &&
    CONTENT_FIELDS.every((f) => fields[f].trim() === '')
  );
}

/** Validates shape and version; malformed or unknown-version payloads yield null (discard). */
export function parseStoredDraft(raw: string | null): StoredQuickCaptureDraft | null {
  if (!raw) return null;
  let parsed: unknown;
  try {
    parsed = JSON.parse(raw);
  } catch {
    return null;
  }
  if (!parsed || typeof parsed !== 'object') return null;
  const v = parsed as Record<string, unknown>;
  if (v.version !== QUICK_CAPTURE_DRAFT_VERSION) return null;
  if (typeof v.savedAt !== 'string') return null;
  if (typeof v.showAddress !== 'boolean') return null;
  for (const f of STRING_FIELDS) {
    if (typeof v[f] !== 'string') return null;
  }
  return v as StoredQuickCaptureDraft;
}

export type DraftStorage = {
  getItem(key: string): Promise<string | null>;
  setItem(key: string, value: string): Promise<void>;
  removeItem(key: string): Promise<void>;
};

export type QuickCaptureDraftCoordinatorOptions = {
  storage: DraftStorage;
  accountUserId: string;
  debounceMs?: number;
  now?: () => Date;
};

/**
 * Debounced autosave whose storage operations (writes and removals) are serialized through a
 * single per-key promise queue, executed strictly in invocation order. This is what actually
 * prevents corruption under races — not a "check a generation counter after the write lands"
 * heuristic, which only proves one narrow case: it cannot stop a *new* save enqueued after a
 * discard from being wiped out by that discard's own removal if the removal is still resolving
 * when the new save's write lands (ordering, not counting, is the guarantee that's needed).
 * Every operation (hydrate's cleanup-on-malformed-read included) goes through the same queue.
 */
export class QuickCaptureDraftCoordinator {
  private readonly storage: DraftStorage;
  private readonly key: string;
  private readonly debounceMs: number;
  private readonly now: () => Date;
  private timer: ReturnType<typeof setTimeout> | null = null;
  /** Tail of the per-key operation chain; every write/removal chains onto this in call order. */
  private queue: Promise<void> = Promise.resolve();

  constructor(options: QuickCaptureDraftCoordinatorOptions) {
    this.storage = options.storage;
    this.key = quickCaptureDraftKey(options.accountUserId);
    this.debounceMs = options.debounceMs ?? 300;
    this.now = options.now ?? (() => new Date());
  }

  /** Enqueues `op` after every previously enqueued operation and returns its own result. */
  private enqueue<T>(op: () => Promise<T>): Promise<T> {
    const result = this.queue.then(op);
    this.queue = result.then(
      () => undefined,
      () => undefined,
    );
    return result;
  }

  /** Reads and validates any persisted draft; clears it in place if malformed/unknown-version. */
  hydrate(): Promise<StoredQuickCaptureDraft | null> {
    return this.enqueue(async () => {
      const raw = await this.storage.getItem(this.key).catch(() => null);
      const draft = parseStoredDraft(raw);
      if (raw && !draft) {
        await this.storage.removeItem(this.key).catch(() => {});
      }
      return draft;
    });
  }

  /** Cancels a pending debounced save without touching persisted storage. */
  cancelPendingSave(): void {
    if (this.timer) {
      clearTimeout(this.timer);
      this.timer = null;
    }
  }

  /** Schedules a debounced save. An all-blank form clears any existing draft immediately. */
  scheduleSave(fields: QuickCaptureDraftFields): void {
    this.cancelPendingSave();
    if (isDraftBlank(fields)) {
      void this.discard();
      return;
    }
    this.timer = setTimeout(() => {
      this.timer = null;
      void this.enqueueWrite(fields);
    }, this.debounceMs);
  }

  private enqueueWrite(fields: QuickCaptureDraftFields): Promise<void> {
    const payload: StoredQuickCaptureDraft = {
      ...fields,
      version: QUICK_CAPTURE_DRAFT_VERSION,
      savedAt: this.now().toISOString(),
    };
    return this.enqueue(() =>
      this.storage.setItem(this.key, JSON.stringify(payload)).catch(() => {}),
    );
  }

  /** Writes immediately, bypassing the debounce (used for the best-effort background-flush
   *  attempt), still serialized behind any operation already queued ahead of it. */
  async flush(fields: QuickCaptureDraftFields): Promise<void> {
    this.cancelPendingSave();
    if (isDraftBlank(fields)) return;
    await this.enqueueWrite(fields);
  }

  /**
   * Cancels any pending (not-yet-fired) save timer, then queues the removal behind whatever
   * write is already in flight — so discard() only resolves once that prior write has landed
   * *and* been removed, and any save scheduled after discard() is called is itself queued
   * behind this removal, never racing ahead of it.
   */
  async discard(): Promise<void> {
    this.cancelPendingSave();
    await this.enqueue(() => this.storage.removeItem(this.key).catch(() => {}));
  }
}

/**
 * Removes a signed-out (or 401'd) user's persisted draft. Storage is injected so this stays
 * usable from AuthContext.tsx without adding a second AsyncStorage import path — callers pass
 * the real AsyncStorage module (it satisfies the DraftStorage shape as-is).
 */
export async function clearQuickCaptureDraft(
  storage: DraftStorage,
  accountUserId: string,
): Promise<void> {
  await storage.removeItem(quickCaptureDraftKey(accountUserId)).catch(() => {});
}
