import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  QuickCaptureDraftCoordinator,
  QuickCaptureDraftFields,
  clearQuickCaptureDraft,
  isDraftBlank,
  parseStoredDraft,
  quickCaptureDraftKey,
} from '../quickCaptureDraft';

function blankFields(): QuickCaptureDraftFields {
  return {
    phone: '',
    customerName: '',
    customerEmail: '',
    description: '',
    source: 'phone',
    showAddress: false,
    addrLine1: '',
    addrLine2: '',
    addrCity: '',
    addrState: '',
    addrZip: '',
  };
}

function filledFields(overrides: Partial<QuickCaptureDraftFields> = {}): QuickCaptureDraftFields {
  return {
    ...blankFields(),
    phone: '5551234567',
    customerName: 'Jane Tech',
    description: 'Furnace not heating',
    ...overrides,
  };
}

/** In-memory fake with an artificial delay knob so write/discard ordering races are reproducible. */
function fakeStorage(delayMs = 0) {
  const store = new Map<string, string>();
  const wait = () => (delayMs > 0 ? new Promise((r) => setTimeout(r, delayMs)) : Promise.resolve());
  return {
    store,
    getItem: vi.fn(async (key: string) => {
      await wait();
      return store.has(key) ? store.get(key)! : null;
    }),
    setItem: vi.fn(async (key: string, value: string) => {
      await wait();
      store.set(key, value);
    }),
    removeItem: vi.fn(async (key: string) => {
      await wait();
      store.delete(key);
    }),
  };
}

beforeEach(() => {
  vi.useFakeTimers();
});

afterEach(() => {
  vi.useRealTimers();
});

describe('quickCaptureDraftKey', () => {
  it('scopes the key to the account user id', () => {
    expect(quickCaptureDraftKey('user-1')).toBe('@ophalo/quickCaptureDraft:user-1:v1');
    expect(quickCaptureDraftKey('user-2')).not.toBe(quickCaptureDraftKey('user-1'));
  });
});

describe('isDraftBlank', () => {
  it('is true for an untouched form', () => {
    expect(isDraftBlank(blankFields())).toBe(true);
  });

  it('is false once any meaningful field has content', () => {
    expect(isDraftBlank(filledFields())).toBe(false);
    expect(isDraftBlank({ ...blankFields(), showAddress: true })).toBe(false);
  });
});

describe('parseStoredDraft', () => {
  it('accepts a well-formed v1 payload', () => {
    const raw = JSON.stringify({ ...filledFields(), version: 1, savedAt: '2026-09-11T00:00:00.000Z' });
    expect(parseStoredDraft(raw)).not.toBeNull();
  });

  it('rejects null/empty input', () => {
    expect(parseStoredDraft(null)).toBeNull();
  });

  it('rejects malformed JSON', () => {
    expect(parseStoredDraft('{not json')).toBeNull();
  });

  it('rejects an unknown/future version', () => {
    const raw = JSON.stringify({ ...filledFields(), version: 2, savedAt: '2026-09-11T00:00:00.000Z' });
    expect(parseStoredDraft(raw)).toBeNull();
  });

  it('rejects a payload missing a required field', () => {
    const { phone: _phone, ...rest } = { ...filledFields(), version: 1, savedAt: '2026-09-11T00:00:00.000Z' };
    expect(parseStoredDraft(JSON.stringify(rest))).toBeNull();
  });
});

describe('QuickCaptureDraftCoordinator', () => {
  it('hydrate() returns a valid persisted draft', async () => {
    const storage = fakeStorage();
    const key = quickCaptureDraftKey('user-1');
    storage.store.set(key, JSON.stringify({ ...filledFields(), version: 1, savedAt: '2026-09-11T00:00:00.000Z' }));
    const coordinator = new QuickCaptureDraftCoordinator({ storage, accountUserId: 'user-1' });

    const draft = await coordinator.hydrate();

    expect(draft?.customerName).toBe('Jane Tech');
  });

  it('hydrate() discards and returns null for a malformed/unknown-version payload', async () => {
    const storage = fakeStorage();
    const key = quickCaptureDraftKey('user-1');
    storage.store.set(key, JSON.stringify({ version: 999, garbage: true }));
    const coordinator = new QuickCaptureDraftCoordinator({ storage, accountUserId: 'user-1' });

    const draft = await coordinator.hydrate();

    expect(draft).toBeNull();
    expect(storage.store.has(key)).toBe(false);
  });

  it('scopes storage per account user id (isolation)', async () => {
    const storage = fakeStorage();
    const c1 = new QuickCaptureDraftCoordinator({ storage, accountUserId: 'user-1', debounceMs: 10 });
    const c2 = new QuickCaptureDraftCoordinator({ storage, accountUserId: 'user-2' });

    c1.scheduleSave(filledFields({ customerName: 'User One Draft' }));
    await vi.advanceTimersByTimeAsync(10);

    expect(await c1.hydrate()).not.toBeNull();
    expect(await c2.hydrate()).toBeNull();
  });

  it('scheduleSave() debounces and persists after the delay', async () => {
    const storage = fakeStorage();
    const coordinator = new QuickCaptureDraftCoordinator({ storage, accountUserId: 'user-1', debounceMs: 300 });

    coordinator.scheduleSave(filledFields());
    await vi.advanceTimersByTimeAsync(299);
    expect(storage.setItem).not.toHaveBeenCalled();

    await vi.advanceTimersByTimeAsync(1);
    expect(storage.setItem).toHaveBeenCalledTimes(1);
  });

  it('scheduleSave() with blank fields clears an existing draft instead of writing', async () => {
    const storage = fakeStorage();
    const key = quickCaptureDraftKey('user-1');
    storage.store.set(key, JSON.stringify({ ...filledFields(), version: 1, savedAt: '2026-09-11T00:00:00.000Z' }));
    const coordinator = new QuickCaptureDraftCoordinator({ storage, accountUserId: 'user-1' });

    coordinator.scheduleSave(blankFields());
    await vi.advanceTimersByTimeAsync(0);

    expect(storage.store.has(key)).toBe(false);
  });

  it('discard() cancels a pending queued save so it never lands', async () => {
    const storage = fakeStorage();
    const key = quickCaptureDraftKey('user-1');
    const coordinator = new QuickCaptureDraftCoordinator({ storage, accountUserId: 'user-1', debounceMs: 300 });

    coordinator.scheduleSave(filledFields());
    await coordinator.discard();
    await vi.advanceTimersByTimeAsync(500);

    expect(storage.setItem).not.toHaveBeenCalled();
    expect(storage.store.has(key)).toBe(false);
  });

  it('a write already in flight when discard() runs is unwound instead of resurrecting the draft', async () => {
    // 20ms artificial storage latency: scheduleSave's timer fires and calls setItem, whose
    // promise is still pending when discard() calls removeItem — reproducing the exact race
    // the review flagged ("an already queued setItem recreating a draft after removeItem").
    const storage = fakeStorage(20);
    const key = quickCaptureDraftKey('user-1');
    const coordinator = new QuickCaptureDraftCoordinator({ storage, accountUserId: 'user-1', debounceMs: 10 });

    coordinator.scheduleSave(filledFields());
    await vi.advanceTimersByTimeAsync(10); // debounce timer fires, the write's setItem begins (20ms in flight)

    // discard() queues its removeItem behind the write already in the queue, so it only
    // resolves once that write has landed *and* been removed.
    const discardDone = coordinator.discard();
    // 40ms: the write's remaining ~20ms, then the queued removal's own ~20ms — both must
    // settle within one advance window, or the removal's timer (scheduled only once the
    // write resolves) falls outside a narrower window and the test deadlocks.
    await vi.advanceTimersByTimeAsync(40);
    await discardDone;

    expect(storage.store.has(key)).toBe(false);
  });

  it('successful-submit cleanup is not resurrected by a late-landing autosave write', async () => {
    const storage = fakeStorage(20);
    const key = quickCaptureDraftKey('user-1');
    const coordinator = new QuickCaptureDraftCoordinator({ storage, accountUserId: 'user-1', debounceMs: 10 });

    coordinator.scheduleSave(filledFields());
    await vi.advanceTimersByTimeAsync(10); // the write's setItem begins (20ms in flight)

    // Simulate handleCreate succeeding mid-flight and running its cleanup.
    const cleanupDone = coordinator.discard();
    await vi.advanceTimersByTimeAsync(40); // write's remaining ~20ms + the queued removal's ~20ms
    await cleanupDone;

    expect(storage.store.has(key)).toBe(false);
  });

  it('a new capture begun right after discard is not erased by the old write/removal settling late', async () => {
    // Reproduces the review's exact scenario: an old save begins, the user discards, then
    // immediately starts a new capture — all while the old write is still in flight. The
    // per-key serialized queue (not a generation counter) is what guarantees the new
    // capture's write can only land after the old write AND its removal have both resolved.
    const storage = fakeStorage(20);
    const key = quickCaptureDraftKey('user-1');
    const coordinator = new QuickCaptureDraftCoordinator({ storage, accountUserId: 'user-1', debounceMs: 10 });

    // Old capture: debounce fires, its 20ms setItem begins.
    coordinator.scheduleSave(filledFields({ customerName: 'Old Capture' }));
    await vi.advanceTimersByTimeAsync(10);

    // Discard while that write is still in flight — its removal is queued behind it.
    const discardDone = coordinator.discard();

    // New capture starts immediately; its debounced write is scheduled behind discard's call
    // but its own timer fires before either the old write or the removal has resolved.
    coordinator.scheduleSave(filledFields({ customerName: 'New Capture' }));
    await vi.advanceTimersByTimeAsync(10); // new capture's debounce fires; its write enqueues

    // Let the full chain settle: old write (~20ms) → removal (~20ms) → new write (~20ms).
    await vi.advanceTimersByTimeAsync(60);
    await discardDone;

    const stored = parseStoredDraft(storage.store.get(key) ?? null);
    expect(stored?.customerName).toBe('New Capture');
  });
});

describe('clearQuickCaptureDraft', () => {
  it('removes only the named user\'s draft', async () => {
    const storage = fakeStorage();
    storage.store.set(quickCaptureDraftKey('user-1'), 'draft-1');
    storage.store.set(quickCaptureDraftKey('user-2'), 'draft-2');

    await clearQuickCaptureDraft(storage, 'user-1');

    expect(storage.store.has(quickCaptureDraftKey('user-1'))).toBe(false);
    expect(storage.store.has(quickCaptureDraftKey('user-2'))).toBe(true);
  });
});
