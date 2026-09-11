import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import React from 'react';
import TestRenderer, { act } from 'react-test-renderer';

// react-test-renderer's act() only suppresses its "not wrapped in act" warnings when this
// global is set; otherwise it still runs correctly but logs noise on every act() call.
(globalThis as unknown as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

/**
 * Integration-level proof for the GAP-091 review's third blocker: hydration completing must
 * never let autosave see one render of blank state and wipe a just-restored draft. Wires the
 * real `useQuickCaptureDraft` + `useRestoreApplyGate` together — the same pairing `app/modal.tsx`
 * uses — behind a mocked AsyncStorage, and asserts on storage call order rather than trusting
 * the render sequence alone: `removeItem` (or a blank `setItem`) must never happen before the
 * restored fields have been both applied to state and scheduled for persistence.
 *
 * No JSX (test files are `.ts`) — components are built with React.createElement.
 * react-test-renderer needs no DOM, so this runs under vitest's plain 'node' environment.
 */

const { store, calls } = vi.hoisted(() => ({
  store: new Map<string, string>(),
  calls: [] as string[],
}));

vi.mock('@react-native-async-storage/async-storage', () => ({
  default: {
    getItem: vi.fn((key: string) => {
      calls.push(`getItem:${key}`);
      return Promise.resolve(store.get(key) ?? null);
    }),
    setItem: vi.fn((key: string, value: string) => {
      calls.push(`setItem:${key}:${JSON.parse(value).customerName || '<blank>'}`);
      store.set(key, value);
      return Promise.resolve();
    }),
    removeItem: vi.fn((key: string) => {
      calls.push(`removeItem:${key}`);
      store.delete(key);
      return Promise.resolve();
    }),
  },
}));

vi.mock('react-native', () => ({
  AppState: { addEventListener: vi.fn(() => ({ remove: vi.fn() })) },
}));

import { useQuickCaptureDraft } from '../useQuickCaptureDraft';
import { useRestoreApplyGate } from '../useRestoreApplyGate';
import {
  QuickCaptureDraftFields,
  StoredQuickCaptureDraft,
  QUICK_CAPTURE_DRAFT_VERSION,
  quickCaptureDraftKey,
} from '../quickCaptureDraft';

const blankFields: QuickCaptureDraftFields = {
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

const restored: StoredQuickCaptureDraft = {
  ...blankFields,
  phone: '5551234567',
  customerName: 'Restored Customer',
  description: 'Leaking pipe',
  version: QUICK_CAPTURE_DRAFT_VERSION,
  savedAt: '2026-01-01T00:00:00.000Z',
};

/** Mirrors modal.tsx's own hydrate -> restore-gate -> gated-autosave wiring. */
function Harness({ onRender }: { onRender: (fields: QuickCaptureDraftFields) => void }) {
  const { hydrated, restoredDraft, saveDraft } = useQuickCaptureDraft('user-1');
  const [fields, setFields] = React.useState<QuickCaptureDraftFields>(blankFields);

  const restoreApplied = useRestoreApplyGate(hydrated, restoredDraft, (draft) => {
    setFields({ ...draft });
  });

  React.useEffect(() => {
    if (!hydrated || !restoreApplied) return;
    saveDraft(fields);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [hydrated, restoreApplied, fields]);

  onRender(fields);
  return null;
}

describe('useQuickCaptureDraft + useRestoreApplyGate (modal.tsx wiring)', () => {
  beforeEach(() => {
    store.clear();
    calls.length = 0;
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('reopening on a persisted draft never removes/blank-writes it before scheduling the restored values', async () => {
    store.set(quickCaptureDraftKey('user-1'), JSON.stringify(restored));

    const renderedFields: QuickCaptureDraftFields[] = [];
    act(() => {
      TestRenderer.create(React.createElement(Harness, { onRender: (f) => renderedFields.push(f) }));
    });

    // Flush the hydrate() microtask + the restore-gate effect + the autosave effect's own
    // microtask chain, then let the 300ms debounce elapse and the queued write settle.
    await act(async () => {
      await Promise.resolve();
      await Promise.resolve();
    });
    await act(async () => {
      await vi.advanceTimersByTimeAsync(400);
    });

    // The only calls against the key are: the initial hydrate read, then a single write of
    // the restored fields. No removeItem, and no setItem carrying blank fields, ever occurs.
    expect(calls).toEqual([
      'getItem:@ophalo/quickCaptureDraft:user-1:v1',
      'setItem:@ophalo/quickCaptureDraft:user-1:v1:Restored Customer',
    ]);

    // The final rendered state reflects the restored draft, not a discarded/blank form.
    expect(renderedFields.at(-1)?.customerName).toBe('Restored Customer');
  });
});
