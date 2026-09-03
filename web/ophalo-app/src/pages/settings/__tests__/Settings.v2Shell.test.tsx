import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { Settings } from "../../Settings";
import type { KeepSetupResult, IntakeStatusResult, ListMembersResponse } from "../../../lib/apiClient";

// Section 0 (V2 shell migration): Settings and its sections now use the V2 application
// layout — page title/subtitle, token tab bar, card sections, KeepButton controls.
// These tests pin the migrated shell's rendering quality at desktop and phone widths;
// they do not change any workflow, route, or role gate.

const mockGetSetup = vi.fn();
const mockGetIntake = vi.fn();
const mockListMembers = vi.fn();

vi.mock("../../../lib/apiClient", async () => {
  const actual = await vi.importActual<typeof import("../../../lib/apiClient")>(
    "../../../lib/apiClient",
  );
  return {
    ...actual,
    api: {
      ...actual.api,
      getSetup: (...a: unknown[]) => mockGetSetup(...a),
      getIntake: (...a: unknown[]) => mockGetIntake(...a),
      listMembers: (...a: unknown[]) => mockListMembers(...a),
    },
  };
});

const baseSetup: KeepSetupResult = {
  businessName: "Apex Home Services",
  timeZone: "America/Chicago",
  customerFacingPhone: null,
  customerFacingEmail: null,
  logoUrl: null,
  websiteUrl: null,
  responsePolicy: {
    firstResponseTargetMinutes: 60,
    standardResponseTargetMinutes: 240,
    priorityResponseTargetMinutes: 30,
    statusCheckThresholdDays: 3,
  },
};

const activeIntake: IntakeStatusResult = {
  hasActiveLink: true,
  publicSlug: "apex-home-services",
  createdAtUtc: "2026-07-01T00:00:00Z",
};

const emptyMembers: ListMembersResponse = {
  members: [],
  seatUsage: { occupiedSeats: 1, maxSeats: 5, atLimit: false, limitApplies: true },
};

function renderSettings() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <Settings callerRole="owner" />
    </QueryClientProvider>,
  );
}

function setViewportWidth(width: number) {
  window.innerWidth = width;
  window.matchMedia = ((query: string) => ({
    matches: false,
    media: query,
    onchange: null,
    addListener: vi.fn(),
    removeListener: vi.fn(),
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
    dispatchEvent: vi.fn(),
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
  })) as any;
}

beforeEach(() => {
  mockGetSetup.mockReset().mockResolvedValue(baseSetup);
  mockGetIntake.mockReset().mockResolvedValue(activeIntake);
  mockListMembers.mockReset().mockResolvedValue(emptyMembers);
  setViewportWidth(1280);
});

describe("Settings — V2 shell", () => {
  it("renders the V2 page heading and a token tab bar", async () => {
    renderSettings();

    expect(screen.getByRole("heading", { name: "Settings", level: 1 })).toHaveClass("keep-page-title");
    const tablist = screen.getByRole("tablist", { name: /settings sections/i });
    const tabs = screen.getAllByRole("tab");
    expect(tabs.map((t) => t.textContent)).toEqual([
      "Public Link & Profile",
      "Response Policy",
      "Team",
    ]);
    expect(tablist).toBeInTheDocument();

    // Company section renders as the default tab with a KeepButton submit.
    expect(await screen.findByRole("heading", { name: "Company" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /save company/i })).toBeInTheDocument();
  });

  it("switches tabs to Response Policy and Team, preserving their headings and controls", async () => {
    const user = userEvent.setup();
    renderSettings();

    await user.click(screen.getByRole("tab", { name: "Response Policy" }));
    expect(await screen.findByRole("heading", { name: "Response Policy" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /save policy/i })).toBeInTheDocument();

    await user.click(screen.getByRole("tab", { name: "Team" }));
    expect(await screen.findByRole("heading", { name: "Team" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /invite team member/i })).toBeInTheDocument();
  });

  it("shows a token loading state then the section when setup resolves", async () => {
    let resolveSetup: (v: KeepSetupResult) => void = () => {};
    mockGetSetup.mockReturnValue(new Promise<KeepSetupResult>((r) => { resolveSetup = r; }));
    renderSettings();

    expect(screen.getByRole("status", { name: /loading settings/i })).toBeInTheDocument();
    resolveSetup(baseSetup);
    expect(await screen.findByRole("heading", { name: "Company" })).toBeInTheDocument();
  });

  it("shows a token error state when setup fails", async () => {
    mockGetSetup.mockRejectedValue(new Error("boom"));
    renderSettings();

    expect(await screen.findByText(/couldn't load your settings/i)).toBeInTheDocument();
  });

  it("gives Response Policy inputs the shared keep-field recipe", async () => {
    const user = userEvent.setup();
    renderSettings();

    await user.click(screen.getByRole("tab", { name: "Response Policy" }));
    await screen.findByRole("heading", { name: "Response Policy" });
    for (const input of screen.getAllByRole("spinbutton")) {
      expect(input).toHaveClass("keep-field");
    }
  });

  it("renders the same shell at phone width", async () => {
    setViewportWidth(390);
    const user = userEvent.setup();
    renderSettings();

    expect(screen.getByRole("heading", { name: "Settings", level: 1 })).toBeInTheDocument();
    expect(await screen.findByRole("heading", { name: "Company" })).toBeInTheDocument();

    await user.click(screen.getByRole("tab", { name: "Team" }));
    await waitFor(() =>
      expect(screen.getByRole("button", { name: /invite team member/i })).toBeInTheDocument(),
    );
  });
});
