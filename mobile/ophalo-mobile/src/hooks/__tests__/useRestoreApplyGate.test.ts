import { describe, expect, it } from 'vitest';
import React from 'react';
import TestRenderer, { act } from 'react-test-renderer';
import { useRestoreApplyGate } from '../useRestoreApplyGate';

// react-test-renderer's act() only suppresses its "not wrapped in act" warnings when this
// global is set; otherwise it still runs correctly but logs noise on every act() call.
(globalThis as unknown as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

/**
 * No JSX (test files are `.ts`, not `.tsx`) — components are built with React.createElement.
 * react-test-renderer needs no DOM, so this runs fine under vitest's plain 'node' environment.
 */
function Harness({
  hydrated,
  restoredDraft,
  onApply,
  onRender,
}: {
  hydrated: boolean;
  restoredDraft: string | null;
  onApply: (draft: string) => void;
  onRender: (gateOpen: boolean) => void;
}) {
  const gateOpen = useRestoreApplyGate(hydrated, restoredDraft, onApply);
  onRender(gateOpen);
  return null;
}

describe('useRestoreApplyGate', () => {
  it('stays closed while not hydrated, and never calls apply', () => {
    const applied: string[] = [];
    const renders: boolean[] = [];
    act(() => {
      TestRenderer.create(
        React.createElement(Harness, {
          hydrated: false,
          restoredDraft: 'draft-value',
          onApply: (d: string) => applied.push(d),
          onRender: (open: boolean) => renders.push(open),
        }),
      );
    });
    expect(renders).toEqual([false]);
    expect(applied).toEqual([]);
  });

  it('opens with no restored draft without calling apply', () => {
    const applied: string[] = [];
    const renders: boolean[] = [];
    act(() => {
      TestRenderer.create(
        React.createElement(Harness, {
          hydrated: true,
          restoredDraft: null,
          onApply: (d: string) => applied.push(d),
          onRender: (open: boolean) => renders.push(open),
        }),
      );
    });
    expect(renders).toEqual([false, true]);
    expect(applied).toEqual([]);
  });

  it('applies the restored draft in the same commit that opens the gate — apply is never observed after gate-open with no apply in between', () => {
    const events: string[] = [];
    act(() => {
      TestRenderer.create(
        React.createElement(Harness, {
          hydrated: true,
          restoredDraft: 'restored-value',
          onApply: (d: string) => events.push(`apply:${d}`),
          onRender: (open: boolean) => events.push(`render:${open}`),
        }),
      );
    });
    // Initial render (gate closed), then the effect fires: apply happens, *then* the state
    // update that opens the gate re-renders — apply is always ordered strictly before the
    // render where the gate reads true. A consumer effect gated on the returned flag can
    // therefore never observe "gate open" without the apply having already run.
    expect(events).toEqual(['render:false', 'apply:restored-value', 'render:true']);
  });

  it('applies exactly once even if hydrated stays true across re-renders (no reapply on unrelated state churn)', () => {
    const applied: string[] = [];
    let renderer: TestRenderer.ReactTestRenderer;
    act(() => {
      renderer = TestRenderer.create(
        React.createElement(Harness, {
          hydrated: true,
          restoredDraft: 'once',
          onApply: (d: string) => applied.push(d),
          onRender: () => {},
        }),
      );
    });
    act(() => {
      renderer.update(
        React.createElement(Harness, {
          hydrated: true,
          restoredDraft: 'once',
          onApply: (d: string) => applied.push(d),
          onRender: () => {},
        }),
      );
    });
    expect(applied).toEqual(['once']);
  });
});
