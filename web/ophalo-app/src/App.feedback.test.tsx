import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { App } from "./App";
import type { UpdatesFeed } from "./lib/apiClient";

// GAP-038 / BL149 (038-2d-ii): the feedback dialog is reachable from the #/help header only when
// the build carries VITE_FEEDBACK_ENABLED=true, and clicking "Report a problem" opens it.

const mockGetMe = vi.fn();
const mockGetCapabilityPackages = vi.fn();
const mockGetUpdates = vi.fn();
const mockSubmitFeedback = vi.fn();

vi.mock("./lib/apiClient", async () => {
  const actual = await vi.importActual<typeof import("./lib/apiClient")>("./lib/apiClient");
  return {
    ...actual,
    api: {
      ...actual.api,
      getMe: (...a: unknown[]) => mockGetMe(...a),
      getCapabilityPackages: (...a: unknown[]) => mockGetCapabilityPackages(...a),
      getUpdates: (...a: unknown[]) => mockGetUpdates(...a),
      submitFeedback: (...a: unknown[]) => mockSubmitFeedback(...a),
    },
  };
});

const EMPTY_FEED: UpdatesFeed = { schema: 1, entries: [], guides: [] };

function renderApp() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <App />
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  window.location.hash = "#/help";
  mockGetMe.mockReset().mockResolvedValue({
    accountUserId: "u1",
    accountId: "a1",
    isAuthenticated: true,
    isVerified: true,
    accountRole: "owner",
    businessName: "Acme HVAC",
    userName: "Christian",
  });
  mockGetCapabilityPackages.mockReset().mockResolvedValue([]);
  mockGetUpdates.mockReset().mockResolvedValue(EMPTY_FEED);
  mockSubmitFeedback.mockReset();
});

afterEach(() => {
  window.location.hash = "";
  vi.unstubAllEnvs();
});

describe("App — in-product feedback entry point", () => {
  it("hides 'Report a problem' when VITE_FEEDBACK_ENABLED is not set", async () => {
    vi.stubEnv("VITE_FEEDBACK_ENABLED", "");
    renderApp();

    await screen.findByRole("heading", { name: "Help & Updates" });
    expect(screen.queryByRole("button", { name: "Report a problem" })).not.toBeInTheDocument();
  });

  it("opens the feedback dialog from the #/help header when the flag is on", async () => {
    vi.stubEnv("VITE_FEEDBACK_ENABLED", "true");
    renderApp();

    await userEvent.click(await screen.findByRole("button", { name: "Report a problem" }));

    const dialog = await screen.findByRole("dialog");
    expect(dialog).toHaveTextContent("Send feedback");
    expect(screen.getByLabelText("What got in your way?")).toBeInTheDocument();
    expect(mockSubmitFeedback).not.toHaveBeenCalled();
  });

  it("opens the feedback dialog from the account menu 'Send feedback' row", async () => {
    vi.stubEnv("VITE_FEEDBACK_ENABLED", "true");
    renderApp();

    await userEvent.click(await screen.findByRole("button", { name: /account menu/i }));
    await userEvent.click(await screen.findByRole("menuitem", { name: "Send feedback" }));

    expect(await screen.findByRole("dialog")).toHaveTextContent("What got in your way?");
  });

  it("hides the account-menu 'Send feedback' row when the flag is off", async () => {
    vi.stubEnv("VITE_FEEDBACK_ENABLED", "");
    renderApp();

    await userEvent.click(await screen.findByRole("button", { name: /account menu/i }));
    expect(screen.queryByRole("menuitem", { name: "Send feedback" })).not.toBeInTheDocument();
  });
});
