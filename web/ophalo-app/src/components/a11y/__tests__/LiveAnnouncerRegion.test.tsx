import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, waitFor, act } from "@testing-library/react";
import { LiveAnnouncerRegion } from "../LiveAnnouncerRegion";
import { announcePolite, subscribeLiveAnnouncer } from "../../../lib/liveAnnouncer";

afterEach(() => {
  vi.useRealTimers();
});

describe("LiveAnnouncerRegion", () => {
  it("renders one visually-hidden, polite status region that starts empty", () => {
    render(<LiveAnnouncerRegion />);
    const region = screen.getByRole("status");
    expect(region).toHaveClass("sr-only");
    expect(region).toHaveAttribute("aria-live", "polite");
    expect(region).toHaveTextContent("");
  });

  it("announces a message published via announcePolite while mounted", async () => {
    render(<LiveAnnouncerRegion />);
    announcePolite("Retry succeeded.");
    await waitFor(() => expect(screen.getByRole("status")).toHaveTextContent("Retry succeeded."));
  });

  it("auto-clears the announcement after the timeout and cleans up the timer on unmount", async () => {
    vi.useFakeTimers();
    const { unmount } = render(<LiveAnnouncerRegion />);
    act(() => announcePolite("Retry succeeded."));
    await act(async () => {
      await vi.advanceTimersByTimeAsync(20);
    });
    expect(screen.getByRole("status")).toHaveTextContent("Retry succeeded.");

    await act(async () => {
      await vi.advanceTimersByTimeAsync(5000);
    });
    expect(screen.getByRole("status")).toHaveTextContent("");

    // No pending timer callback should fire (and no error thrown) after unmount.
    unmount();
    await act(async () => {
      await vi.advanceTimersByTimeAsync(10000);
    });
  });

  it("cancels a pending post-announce animation frame on immediate unmount, before it can call setState on the unmounted component", async () => {
    vi.useFakeTimers();
    const cancelSpy = vi.spyOn(window, "cancelAnimationFrame");
    const errorSpy = vi.spyOn(console, "error").mockImplementation(() => {});
    const { unmount } = render(<LiveAnnouncerRegion />);

    // Publish, then unmount immediately — before the scheduled requestAnimationFrame callback
    // (queued to move the message from "" to "Retry succeeded.") has had a chance to run.
    act(() => announcePolite("Retry succeeded."));
    unmount();

    expect(cancelSpy).toHaveBeenCalled();

    // Advancing past when the frame/timeout would have fired must not call setState on the
    // unmounted component (which would log React's "state update on an unmounted component"
    // warning) or throw.
    await act(async () => {
      await vi.advanceTimersByTimeAsync(10000);
    });
    expect(errorSpy).not.toHaveBeenCalled();

    errorSpy.mockRestore();
  });

  it("unsubscribes on unmount so a later announcePolite call has no listener left to notify", () => {
    const { unmount } = render(<LiveAnnouncerRegion />);
    unmount();
    // Publishing after unmount must not throw even though no region is mounted to receive it.
    expect(() => announcePolite("Retry succeeded.")).not.toThrow();
  });

  it("subscribeLiveAnnouncer's returned unsubscribe stops further notifications", () => {
    const listener = vi.fn();
    const unsubscribe = subscribeLiveAnnouncer(listener);
    announcePolite("first");
    expect(listener).toHaveBeenCalledWith("first");
    unsubscribe();
    announcePolite("second");
    expect(listener).toHaveBeenCalledTimes(1);
  });
});
