import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { Home } from "../Home";
import type { IntakeStatusResult } from "../../lib/apiClient";

// Getting Started V2 restyle
// (docs/ux-design/v2/settings-and-getting-started-ui-upgrade.md §3.2): the Owner surface
// is a lightweight readiness panel + optional adjustments, NOT a setup checklist. These
// tests pin that contract — no progress meter, score, step number, or access gate.

const mockGetIntake = vi.fn();

vi.mock("../../lib/apiClient", async () => {
  const actual = await vi.importActual<typeof import("../../lib/apiClient")>("../../lib/apiClient");
  return {
    ...actual,
    api: { ...actual.api, getIntake: (...a: unknown[]) => mockGetIntake(...a) },
  };
});

const activeIntake: IntakeStatusResult = {
  hasActiveLink: true,
  publicSlug: "apex-home-services",
  createdAtUtc: "2026-07-01T00:00:00Z",
};

function renderHome(props?: Partial<Parameters<typeof Home>[0]>) {
  const onStartCapture = vi.fn();
  const onNavigateSettings = vi.fn();
  const onNavigateRequests = vi.fn();
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <Home
        role="owner"
        onStartCapture={onStartCapture}
        onNavigateSettings={onNavigateSettings}
        onNavigateRequests={onNavigateRequests}
        {...props}
      />
    </QueryClientProvider>,
  );
  return { onStartCapture, onNavigateSettings, onNavigateRequests };
}

beforeEach(() => {
  mockGetIntake.mockReset().mockResolvedValue(activeIntake);
});

describe("Getting Started — owner readiness panel", () => {
  it("states the business is live and surfaces the public link with copy/open", async () => {
    renderHome();

    expect(screen.getByRole("heading", { name: "Getting started", level: 1 })).toBeInTheDocument();
    expect(
      await screen.findByRole("heading", { name: /your business is live on keep/i }),
    ).toBeInTheDocument();

    const link = await screen.findByText(/keep\/s\/apex-home-services/);
    expect(link).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /copy link/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /open/i })).toHaveAttribute(
      "href",
      expect.stringContaining("/keep/s/apex-home-services"),
    );
  });

  it("shows a structured loading placeholder, not a bare string, while the link resolves", async () => {
    let resolve: (v: IntakeStatusResult) => void = () => {};
    mockGetIntake.mockReturnValue(new Promise<IntakeStatusResult>((r) => { resolve = r; }));
    renderHome();

    expect(screen.getByRole("status", { name: /loading your public request link/i })).toBeInTheDocument();
    resolve(activeIntake);
    expect(await screen.findByText(/keep\/s\/apex-home-services/)).toBeInTheDocument();
  });

  it("routes the optional rows to their Settings sections and the primary to Quick Capture", async () => {
    const user = userEvent.setup();
    const { onStartCapture, onNavigateSettings } = renderHome();

    await user.click(screen.getByRole("button", { name: /business profile/i }));
    expect(onNavigateSettings).toHaveBeenCalledWith("public-profile");

    await user.click(screen.getByRole("button", { name: /response targets/i }));
    expect(onNavigateSettings).toHaveBeenCalledWith("policy");

    await user.click(screen.getByRole("button", { name: /invite teammates/i }));
    expect(onNavigateSettings).toHaveBeenCalledWith("team");

    await user.click(screen.getByRole("button", { name: /add your first customer request/i }));
    expect(onStartCapture).toHaveBeenCalledTimes(1);
  });

  it("falls back to a Settings pointer when no active link is present — never a constructed URL", async () => {
    mockGetIntake.mockResolvedValue({ hasActiveLink: false, publicSlug: null, createdAtUtc: null });
    const { onNavigateSettings } = renderHome();
    const user = userEvent.setup();

    expect(await screen.findByText(/your link is being set up/i)).toBeInTheDocument();
    expect(screen.queryByText(/keep\/s\//)).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /check in settings/i }));
    expect(onNavigateSettings).toHaveBeenCalledWith("public-profile");
  });

  it("has no checklist, progress meter, completion score, or step numbering", async () => {
    renderHome();
    await screen.findByRole("heading", { name: /your business is live on keep/i });

    expect(screen.queryByRole("progressbar")).not.toBeInTheDocument();
    expect(screen.queryByText(/\d+\s*of\s*\d+/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/%\s*complete/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/step\s*\d/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/finish setup|complete setup/i)).not.toBeInTheDocument();
  });

  it("does not fetch the intake link for a non-owner", async () => {
    renderHome({ role: "operator" });
    await waitFor(() => expect(screen.getByRole("heading", { level: 1 })).toBeInTheDocument());
    expect(mockGetIntake).not.toHaveBeenCalled();
  });
});
