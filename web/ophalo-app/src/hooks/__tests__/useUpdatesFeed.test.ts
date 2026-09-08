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
  computeBannerState,
  DISMISSED_BANNERS_KEY,
} from "../useUpdatesFeed";
import type { UpdatesFeed, UpdateEntry } from "../../lib/apiClient";

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

describe("computeBannerState", () => {
  const NOW_B = new Date("2026-09-08T00:00:00Z");
  const e = (over: Partial<UpdateEntry> & { id: string }): UpdateEntry => ({
    published_at: "2026-09-01T00:00:00Z",
    section: "whats_new",
    title: `T ${over.id}`,
    body: "x",
    highlight: true,
    ...over,
  });

  it("ignores entries without highlight:true", () => {
    const entries = [e({ id: "a", highlight: false }), e({ id: "b", highlight: undefined })];
    expect(computeBannerState(entries, [], NOW_B)).toEqual({ entry: null, moreCount: 0 });
  });

  it("keeps an active known_issue and a whats_new within 14 days (inclusive), drops the rest", () => {
    const entries = [
      e({ id: "ki-active", section: "known_issue", status: "active", published_at: "2026-09-01T00:00:00Z" }),
      e({ id: "ki-resolved", section: "known_issue", status: "resolved" }),
      e({ id: "nw-fresh", section: "whats_new", published_at: "2026-09-05T00:00:00Z" }),
      e({ id: "nw-boundary", section: "whats_new", published_at: "2026-08-25T00:00:00Z" }),
      e({ id: "nw-stale", section: "whats_new", published_at: "2026-08-20T00:00:00Z" }),
      e({ id: "cs", section: "coming_soon", published_at: "2026-09-06T00:00:00Z" }),
    ];
    const state = computeBannerState(entries, [], NOW_B);
    expect(state.entry?.id).toBe("nw-fresh"); // newest qualifying
    expect(state.moreCount).toBe(2); // ki-active + nw-boundary
  });

  it("treats banner_until as a hard override in both directions", () => {
    const future = e({ id: "cs-future", section: "coming_soon", published_at: "2026-01-01T00:00:00Z", banner_until: "2026-10-01T00:00:00Z" });
    const past = e({ id: "ki-past", section: "known_issue", status: "active", banner_until: "2026-09-01T00:00:00Z" });
    expect(computeBannerState([future, past], [], NOW_B)).toEqual({ entry: future, moreCount: 0 });
  });

  it("excludes dismissed ids", () => {
    const entries = [
      e({ id: "keep", section: "whats_new", published_at: "2026-09-05T00:00:00Z" }),
      e({ id: "gone", section: "whats_new", published_at: "2026-09-06T00:00:00Z" }),
    ];
    expect(computeBannerState(entries, ["gone"], NOW_B).entry?.id).toBe("keep");
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

  const BANNER_FEED: UpdatesFeed = {
    schema: 1,
    entries: [
      { id: "hl-new", published_at: "2026-09-06T00:00:00Z", section: "whats_new", title: "Newer", body: "x", highlight: true },
      { id: "hl-old", published_at: "2026-09-02T00:00:00Z", section: "known_issue", status: "active", title: "Older", body: "x", highlight: true },
      { id: "plain", published_at: "2026-09-07T00:00:00Z", section: "whats_new", title: "No highlight", body: "x" },
    ],
    guides: [],
  };

  it("exposes the newest qualifying highlight as bannerEntry with the remainder as bannerMoreCount", async () => {
    mockGetUpdates.mockResolvedValue(BANNER_FEED);
    const { result } = renderHook(() => useUpdatesFeed({ now: NOW }), { wrapper: wrapper() });

    expect(result.current.bannerEntry).toBeNull(); // still loading
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.bannerEntry?.id).toBe("hl-new");
    expect(result.current.bannerMoreCount).toBe(1);
  });

  it("dismissBanner hides the entry immediately and persists the id; watermark is untouched", async () => {
    mockGetUpdates.mockResolvedValue(BANNER_FEED);
    const { result } = renderHook(() => useUpdatesFeed({ now: NOW }), { wrapper: wrapper() });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    act(() => result.current.dismissBanner("hl-new"));
    expect(result.current.bannerEntry?.id).toBe("hl-old"); // falls through to the next one
    expect(result.current.bannerMoreCount).toBe(0);
    expect(JSON.parse(window.localStorage.getItem(DISMISSED_BANNERS_KEY)!)).toEqual(["hl-new"]);
    expect(window.localStorage.getItem(WATERMARK_KEY)).toBeNull();
  });

  it("starts from previously dismissed ids in storage", async () => {
    window.localStorage.setItem(DISMISSED_BANNERS_KEY, JSON.stringify(["hl-new", "hl-old"]));
    mockGetUpdates.mockResolvedValue(BANNER_FEED);
    const { result } = renderHook(() => useUpdatesFeed({ now: NOW }), { wrapper: wrapper() });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.bannerEntry).toBeNull();
  });
});
