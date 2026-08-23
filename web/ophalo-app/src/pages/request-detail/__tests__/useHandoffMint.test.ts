import { describe, it, expect, vi } from "vitest";
import { renderHook, act } from "@testing-library/react";
import { useHandoffMint } from "../useHandoffMint";

// A stale mint (superseded by a newer one, or resolving after unmount) must never win the
// race and overwrite state with an outdated or unmounted-component result.

describe("useHandoffMint — stale-request protection", () => {
  it("ignores an older overlapping mint that resolves after a newer one has started", async () => {
    let resolveFirst!: (v: { handoffUrl: string }) => void;
    let resolveSecond!: (v: { handoffUrl: string }) => void;
    const mint = vi.fn()
      .mockImplementationOnce(() => new Promise((resolve) => { resolveFirst = resolve; }))
      .mockImplementationOnce(() => new Promise((resolve) => { resolveSecond = resolve; }));

    const { result } = renderHook(() => useHandoffMint(true, mint, "failed"));
    expect(mint).toHaveBeenCalledTimes(1);

    // Start a second, newer mint (e.g. via retry) before the first has resolved.
    act(() => {
      void result.current.retry();
    });
    expect(mint).toHaveBeenCalledTimes(2);

    // Resolve the newer (second) mint first, then the stale first mint out of order — the
    // stale one must not clobber the newer result.
    await act(async () => {
      resolveSecond({ handoffUrl: "https://example.com/second" });
    });
    expect(result.current.handoffUrl).toBe("https://example.com/second");

    await act(async () => {
      resolveFirst({ handoffUrl: "https://example.com/first" });
    });
    expect(result.current.handoffUrl).toBe("https://example.com/second");
  });

  it("does not update state after unmount", async () => {
    let resolveMint!: (v: { handoffUrl: string }) => void;
    const mint = vi.fn(() => new Promise<{ handoffUrl: string }>((resolve) => { resolveMint = resolve; }));

    const { result, unmount } = renderHook(() => useHandoffMint(true, mint, "failed"));
    expect(result.current.isLoading).toBe(true);

    unmount();

    // Resolving after unmount must not throw or trigger a state update on the unmounted hook.
    await act(async () => {
      resolveMint({ handoffUrl: "https://example.com/post-unmount" });
    });
  });
});
