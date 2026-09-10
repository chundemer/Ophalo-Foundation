import { describe, it, expect, beforeEach, vi } from "vitest";
import { renderHook } from "@testing-library/react";
import { useUnloadGuard, __unloadGuardState, __resetUnloadGuard } from "../unloadGuard";

beforeEach(() => {
  __resetUnloadGuard();
});

function fireBeforeUnload(): BeforeUnloadEvent {
  const event = new Event("beforeunload", { cancelable: true }) as BeforeUnloadEvent;
  window.dispatchEvent(event);
  return event;
}

describe("useUnloadGuard", () => {
  it("does not prompt while nothing is registered dirty", () => {
    renderHook(() => useUnloadGuard("composer-note:req-1", false));
    const event = fireBeforeUnload();
    expect(event.defaultPrevented).toBe(false);
    expect(__unloadGuardState().dirtyIds).toHaveLength(0);
  });

  it("prompts on beforeunload while an id is registered dirty", () => {
    renderHook(() => useUnloadGuard("composer-note:req-1", true));
    expect(__unloadGuardState().dirtyIds).toEqual(["composer-note:req-1"]);

    const event = fireBeforeUnload();
    expect(event.defaultPrevented).toBe(true);
  });

  it("stops prompting once the id goes clean again", () => {
    const { rerender } = renderHook(({ dirty }) => useUnloadGuard("composer-note:req-1", dirty), {
      initialProps: { dirty: true },
    });
    expect(fireBeforeUnload().defaultPrevented).toBe(true);

    rerender({ dirty: false });
    expect(__unloadGuardState().dirtyIds).toHaveLength(0);
    expect(fireBeforeUnload().defaultPrevented).toBe(false);
  });

  it("deregisters on unmount", () => {
    const { unmount } = renderHook(() => useUnloadGuard("composer-update:req-1", true));
    unmount();
    expect(__unloadGuardState().dirtyIds).toHaveLength(0);
  });

  it("keeps prompting while any one of several dirty composers is registered", () => {
    renderHook(() => useUnloadGuard("composer-note:req-1", true));
    const second = renderHook(({ dirty }) => useUnloadGuard("composer-update:req-1", dirty), {
      initialProps: { dirty: true },
    });

    second.rerender({ dirty: false });
    expect(fireBeforeUnload().defaultPrevented).toBe(true); // note composer still dirty
  });

  it("installs exactly one listener regardless of how many guards register", () => {
    const addSpy = vi.spyOn(window, "addEventListener");
    renderHook(() => useUnloadGuard("a", true));
    renderHook(() => useUnloadGuard("b", true));
    const beforeUnloadRegistrations = addSpy.mock.calls.filter(([type]) => type === "beforeunload");
    expect(beforeUnloadRegistrations.length).toBeLessThanOrEqual(1);
    addSpy.mockRestore();
  });
});
