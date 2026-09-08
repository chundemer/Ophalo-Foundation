import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { Help } from "../Help";
import { WATERMARK_KEY } from "../../hooks/useUpdatesFeed";
import type { UpdatesFeed } from "../../lib/apiClient";

const mockGetUpdates = vi.fn();

// jsdom's localStorage is not reliably available in this vitest environment; use an in-memory one.
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

vi.mock("../../lib/apiClient", async () => {
  const actual = await vi.importActual<typeof import("../../lib/apiClient")>("../../lib/apiClient");
  return { ...actual, api: { ...actual.api, getUpdates: () => mockGetUpdates() } };
});

function renderHelp() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <Help />
    </QueryClientProvider>,
  );
}

const FEED: UpdatesFeed = {
  schema: 1,
  entries: [
    { id: "ki-1", published_at: "2026-09-05T00:00:00Z", section: "known_issue", status: "active", title: "Texts delayed", body: "Some carriers queue messages." },
    { id: "nw-1", published_at: "2026-09-03T00:00:00Z", section: "whats_new", title: "New workspace", body: "Now available." },
    { id: "cs-1", published_at: "2026-09-01T00:00:00Z", section: "coming_soon", title: "Reports", body: "Landing soon." },
  ],
  guides: [{ id: "g-1", updated_at: "2026-09-02T00:00:00Z", title: "Log a visit", body: "1. Open the request." }],
};

beforeEach(() => {
  mockGetUpdates.mockReset();
  Object.defineProperty(window, "localStorage", {
    value: new MemoryStorage(),
    configurable: true,
    writable: true,
  });
});

afterEach(() => vi.restoreAllMocks());

describe("Help page", () => {
  it("renders every section and the feed last-updated time", async () => {
    mockGetUpdates.mockResolvedValue(FEED);
    renderHelp();

    expect(await screen.findByRole("heading", { name: "Known issues" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Updates" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Coming soon" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Guides" })).toBeInTheDocument();
    expect(screen.getByText("Texts delayed")).toBeInTheDocument();
    expect(screen.getByText(/Last updated/)).toBeInTheDocument();
  });

  it("moves the watermark on a successful mount", async () => {
    mockGetUpdates.mockResolvedValue(FEED);
    renderHelp();
    await screen.findByText("Texts delayed");
    await waitFor(() =>
      expect(window.localStorage.getItem(WATERMARK_KEY)).toBe(
        String(Date.parse("2026-09-05T00:00:00Z")),
      ),
    );
  });

  it("shows a calm per-section empty state for a cold empty feed", async () => {
    mockGetUpdates.mockResolvedValue({ schema: 1, entries: [], guides: [] } satisfies UpdatesFeed);
    renderHelp();

    expect(await screen.findByRole("heading", { name: "Known issues" })).toBeInTheDocument();
    expect(screen.getAllByText("No updates yet.").length).toBeGreaterThan(0);
    expect(window.localStorage.getItem(WATERMARK_KEY)).toBeNull();
  });

  it("shows a retry affordance on a network error and recovers", async () => {
    mockGetUpdates.mockRejectedValueOnce(new Error("offline")).mockResolvedValueOnce(FEED);
    renderHelp();

    const retry = await screen.findByRole("button", { name: "Try again" });
    expect(screen.getByText("Couldn't load updates, try again.")).toBeInTheDocument();
    // The page header still rendered — the rest of the app is unaffected.
    expect(screen.getByRole("heading", { name: "Help & Updates" })).toBeInTheDocument();

    await userEvent.click(retry);
    expect(await screen.findByText("Texts delayed")).toBeInTheDocument();
  });
});
