import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { renderHook, act } from "@testing-library/react";
import { useCopyFeedback } from "../useCopyFeedback";

function mockClipboard(writeText: (text: string) => Promise<void>) {
  Object.defineProperty(navigator, "clipboard", {
    value: { writeText },
    configurable: true,
  });
}

beforeEach(() => {
  vi.useFakeTimers();
});

afterEach(() => {
  vi.useRealTimers();
  vi.restoreAllMocks();
});

describe("useCopyFeedback", () => {
  it("sets copiedId on success and clears it after the reset delay", async () => {
    mockClipboard(() => Promise.resolve());
    const { result } = renderHook(() => useCopyFeedback(2000));

    await act(async () => {
      await result.current.copy("hello", "field-a");
    });

    expect(result.current.copiedId).toBe("field-a");
    expect(result.current.failedId).toBeNull();

    act(() => {
      vi.advanceTimersByTime(2000);
    });

    expect(result.current.copiedId).toBeNull();
  });

  it("sets failedId (not copiedId) on clipboard rejection, without throwing", async () => {
    mockClipboard(() => Promise.reject(new Error("denied")));
    const { result } = renderHook(() => useCopyFeedback(2000));

    let success: boolean | undefined;
    await act(async () => {
      success = await result.current.copy("hello", "field-a");
    });

    expect(success).toBe(false);
    expect(result.current.copiedId).toBeNull();
    expect(result.current.failedId).toBe("field-a");

    act(() => {
      vi.advanceTimersByTime(2000);
    });

    expect(result.current.failedId).toBeNull();
  });

  it("replaces the prior timer on reuse instead of running both", async () => {
    mockClipboard(() => Promise.resolve());
    const { result } = renderHook(() => useCopyFeedback(2000));

    await act(async () => {
      await result.current.copy("first", "field-a");
    });
    expect(result.current.copiedId).toBe("field-a");

    // Advance partway through the first timer, then copy again — this must cancel
    // the first timer rather than let it fire and clear the second copy's state.
    act(() => {
      vi.advanceTimersByTime(1500);
    });
    await act(async () => {
      await result.current.copy("second", "field-b");
    });
    expect(result.current.copiedId).toBe("field-b");

    // At the original timer's would-be fire time (1500 + 500 = 2000ms after the
    // first copy), field-b's state must still be showing — proving the first
    // timer was cleared, not left running alongside the second.
    act(() => {
      vi.advanceTimersByTime(500);
    });
    expect(result.current.copiedId).toBe("field-b");

    // The second timer fires on its own full delay from when it was set.
    act(() => {
      vi.advanceTimersByTime(1500);
    });
    expect(result.current.copiedId).toBeNull();
  });

  it("clears its pending timer on unmount", async () => {
    mockClipboard(() => Promise.resolve());
    const clearTimeoutSpy = vi.spyOn(globalThis, "clearTimeout");
    const { result, unmount } = renderHook(() => useCopyFeedback(2000));

    await act(async () => {
      await result.current.copy("hello", "field-a");
    });
    clearTimeoutSpy.mockClear();

    unmount();

    expect(clearTimeoutSpy).toHaveBeenCalled();
  });

  it("skips state and timer updates when the clipboard promise settles after unmount", async () => {
    let resolveWrite!: () => void;
    const writeText = vi.fn(
      () => new Promise<void>((resolve) => { resolveWrite = resolve; }),
    );
    mockClipboard(writeText);
    const { result, unmount } = renderHook(() => useCopyFeedback(2000));

    let copyPromise!: Promise<boolean>;
    act(() => {
      copyPromise = result.current.copy("hello", "field-a");
    });

    unmount();
    expect(vi.getTimerCount()).toBe(0);

    // Resolve the clipboard write only after the component has already unmounted.
    await act(async () => {
      resolveWrite();
      await copyPromise;
    });

    // No timer should have been scheduled from the post-unmount resolution, and no
    // state-update side effect (verified indirectly: nothing throws/warns and no
    // pending timer exists to eventually mutate state on a gone component).
    expect(vi.getTimerCount()).toBe(0);
  });
});
