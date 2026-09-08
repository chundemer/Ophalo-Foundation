import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { renderHook, waitFor, act } from "@testing-library/react";
import { createElement } from "react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import {
  useUpdatesFeed,
  WATERMARK_KEY,
  groupSections,
  computeFeedMax,
  computeUnseenCount,
} from "../useUpdatesFeed";
import type { UpdatesFeed } from "../../lib/apiClient";

const mockGetUpdates = vi.fn();

// jsdom's localStorage is not reliably available in this vitest environment (no other app code
// uses it yet), so install a plain in-memory implementation for these tests.
class MemoryStorage {
  private store = new Map<string, string>();
  get length() {
    return this.store.size;
  }
  clear() {
    this.store.clear();
  }
  getItem(key: string) {
    return this.store.has(key) ? this.store.get(key)! : null;
  }
  setItem(key: string, value: string) {
    this.store.set(key, String(value));
  }
  removeItem(key: string) {
    this.store.delete(key);
  }
  key(index: number) {
    return Array.from(this.store.keys())[index] ?? null;
  }
}

function installStorage(impl: object = new MemoryStorage()) {
  Object.defineProperty(window, "localStorage", { value: impl, configurable: true, writable: true });
}

vi.mock("../../lib/apiClient", async () => {
  const actual = await vi.importActual<typeof import("../../lib/apiClient")>("../../lib/apiClient");
  return { ...actual, api: { ...actual.api, getUpdates: () => mockGetUpdates() } };
});

function wrapper() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: React.ReactNode }) =>
    createElement(QueryClientProvider, { client }, children);
}

const NOW = new Date("2026-09-08T00:00:00Z");

const FEED: UpdatesFeed = {
  schema: 1,
  entries: [
    { id: "ki-old", published_at: "2026-08-01T00:00:00Z", section: "known_issue", status: "resolved", title: "Old issue", body: "x" },
    { id: "ki-new", published_at: "2026-09-05T00:00:00Z", section: "known_issue", status: "active", title: "New issue", body: "x" },
    { id: "nw-1", published_at: "2026-09-03T00:00:00Z", section: "whats_new", title: "Shipped", body: "x" },
    { id: "cs-1", published_at: "2026-09-07T00:00:00Z", section: "coming_soon", title: "Soon", body: "x" },
    { id: "future", published_at: "2026-12-01T00:00:00Z", section: "whats_new", title: "Scheduled", body: "x" },
    { id: "misc-1", published_at: "2026-09-02T00:00:00Z", section: "policy_change", title: "Other", body: "x" },
  ],
  guides: [
    { id: "g-1", updated_at: "2027-01-01T00:00:00Z", title: "Guide", body: "1. step" },
  ],
};

beforeEach(() => {
  mockGetUpdates.mockReset();
  installStorage();
});

afterEach(() => {
  vi.restoreAllMocks();
});

describe("groupSections", () => {
  it("orders sections known_issue → whats_new → coming_soon → fallback, and sorts known issues active-before-resolved", () => {
    const sections = groupSections(FEED.entries);
    expect(sections.map((s) => s.heading)).toEqual([
      "Known issues",
      "Updates",
      "Coming soon",
      "More updates",
    ]);
    expect(sections[0].entries.map((e) => e.id)).toEqual(["ki-new", "ki-old"]);
  });
});

describe("computeFeedMax", () => {
  it("returns the newest non-future published_at and ignores guides", () => {
    expect(computeFeedMax(FEED.entries, NOW)).toBe(Date.parse("2026-09-07T00:00:00Z"));
  });
});

describe("computeUnseenCount", () => {
  it("counts every entry when there is no watermark", () => {
    expect(computeUnseenCount(FEED.entries, null)).toBe(FEED.entries.length);
  });

  it("counts only entries published strictly after the watermark", () => {
    // ki-new (09-05), cs-1 (09-07), future (12-01) are newer than 09-04.
    expect(computeUnseenCount(FEED.entries, Date.parse("2026-09-04T00:00:00Z"))).toBe(3);
  });
});

describe("useUpdatesFeed", () => {
  it("exposes grouped sections, guides, and the feed last-updated time (entries + guides)", async () => {
    mockGetUpdates.mockResolvedValue(FEED);
    const { result } = renderHook(() => useUpdatesFeed({ now: NOW }), { wrapper: wrapper() });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.sections.map((s) => s.key)).toEqual([
      "known_issue",
      "whats_new",
      "coming_soon",
      "__other__",
    ]);
    expect(result.current.guides).toHaveLength(1);
    // Guide updated_at (2027) is the newest timestamp anywhere in the feed.
    expect(result.current.feedLastUpdated).toBe("2027-01-01T00:00:00Z");
  });

  it("exposes unseenCount: all entries with no watermark, 0 while not successful, the subset after markSeen", async () => {
    mockGetUpdates.mockResolvedValue(FEED);
    const { result, rerender } = renderHook(() => useUpdatesFeed({ now: NOW }), {
      wrapper: wrapper(),
    });

    expect(result.current.unseenCount).toBe(0); // still loading
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.unseenCount).toBe(FEED.entries.length);

    // markSeen writes the newest past entry (cs-1, 09-07); ki-new/nw-1/misc-1/ki-old are then seen,
    // only the future entry stays unseen. The watermark is read per-render, so the count reflects
    // the write on the next render (the shell re-renders on route change).
    act(() => result.current.markSeen());
    rerender();
    expect(result.current.unseenCount).toBe(1);
  });

  it("reports unseenCount 0 on a transport error", async () => {
    mockGetUpdates.mockRejectedValue(new Error("offline"));
    const { result } = renderHook(() => useUpdatesFeed({ now: NOW }), { wrapper: wrapper() });
    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(result.current.unseenCount).toBe(0);
  });

  it("reports no watermark when the key is absent", async () => {
    mockGetUpdates.mockResolvedValue(FEED);
    const { result } = renderHook(() => useUpdatesFeed({ now: NOW }), { wrapper: wrapper() });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.watermark).toBeNull();
  });

  it("markSeen writes the newest past-entry timestamp (never a guide, never a future entry)", async () => {
    mockGetUpdates.mockResolvedValue(FEED);
    const { result } = renderHook(() => useUpdatesFeed({ now: NOW }), { wrapper: wrapper() });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    act(() => result.current.markSeen());
    expect(window.localStorage.getItem(WATERMARK_KEY)).toBe(
      String(Date.parse("2026-09-07T00:00:00Z")),
    );
  });

  it("markSeen does not write on loading, error, or an empty feed", async () => {
    mockGetUpdates.mockRejectedValue(new Error("offline"));
    const { result } = renderHook(() => useUpdatesFeed({ now: NOW }), { wrapper: wrapper() });
    await waitFor(() => expect(result.current.isError).toBe(true));

    act(() => result.current.markSeen());
    expect(window.localStorage.getItem(WATERMARK_KEY)).toBeNull();
  });

  it("markSeen does not write for a successful but empty feed", async () => {
    mockGetUpdates.mockResolvedValue({ schema: 1, entries: [], guides: [] } satisfies UpdatesFeed);
    const { result } = renderHook(() => useUpdatesFeed({ now: NOW }), { wrapper: wrapper() });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    act(() => result.current.markSeen());
    expect(window.localStorage.getItem(WATERMARK_KEY)).toBeNull();
  });

  it("treats a corrupt stored value as no watermark", async () => {
    window.localStorage.setItem(WATERMARK_KEY, "not-a-number");
    mockGetUpdates.mockResolvedValue(FEED);
    const { result } = renderHook(() => useUpdatesFeed({ now: NOW }), { wrapper: wrapper() });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.watermark).toBeNull();
  });

  it("treats a throwing localStorage as no watermark and swallows write failures", async () => {
    installStorage({
      getItem: () => {
        throw new Error("denied");
      },
      setItem: () => {
        throw new Error("denied");
      },
    });
    mockGetUpdates.mockResolvedValue(FEED);
    const { result } = renderHook(() => useUpdatesFeed({ now: NOW }), { wrapper: wrapper() });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.watermark).toBeNull();
    expect(() => act(() => result.current.markSeen())).not.toThrow();
  });

  it("honors the injected now when picking the feed maximum", async () => {
    mockGetUpdates.mockResolvedValue(FEED);
    // A now earlier than cs-1 means the newest *past* entry is nw-1 (2026-09-03).
    const earlier = new Date("2026-09-04T00:00:00Z");
    const { result } = renderHook(() => useUpdatesFeed({ now: earlier }), { wrapper: wrapper() });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    act(() => result.current.markSeen());
    expect(window.localStorage.getItem(WATERMARK_KEY)).toBe(
      String(Date.parse("2026-09-03T00:00:00Z")),
    );
  });
});
