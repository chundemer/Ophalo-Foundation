import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { Requests } from "../Requests";
import type {
  IntakeStatusResult,
  KeepBusinessSetupResult,
  KeepRequestListResult,
  KeepSetupResult,
} from "../../lib/apiClient";

const mockGetRequests = vi.fn();
const mockGetAvailableRequests = vi.fn();
const mockGetGuidedSetup = vi.fn();
const mockGetSetup = vi.fn();
const mockGetIntake = vi.fn();

vi.mock("../../lib/apiClient", async () => {
  const actual = await vi.importActual<typeof import("../../lib/apiClient")>(
    "../../lib/apiClient",
  );
  return {
    ...actual,
    api: {
      ...actual.api,
      getRequests: (...args: unknown[]) => mockGetRequests(...args),
      getAvailableRequests: (...args: unknown[]) => mockGetAvailableRequests(...args),
      getGuidedSetup: (...args: unknown[]) => mockGetGuidedSetup(...args),
      getSetup: (...args: unknown[]) => mockGetSetup(...args),
      getIntake: (...args: unknown[]) => mockGetIntake(...args),
    },
  };
});

const emptyList: KeepRequestListResult = {
  requests: [],
  pageInfo: { limit: 20, hasMore: false, nextCursor: null },
  viewCounts: null,
  listContext: { view: "default", isDefaultCommandCenter: true, isHistory: false, isSearch: false },
};

const zeroRequestSetup: KeepBusinessSetupResult = {
  businessInfoComplete: true,
  addFirstRequestComplete: false,
  reviewCustomerPageComplete: false,
  createIntakePageComplete: true,
  shareIntakePageComplete: false,
  buildTeamComplete: false,
  useMobileComplete: false,
  deferredSteps: [],
  intendedTeamSize: null,
};

const hasRequestsSetup: KeepBusinessSetupResult = {
  ...zeroRequestSetup,
  addFirstRequestComplete: true,
};

const liveIntake: IntakeStatusResult = {
  hasActiveLink: true,
  publicSlug: "acme-plumbing",
  createdAtUtc: "2026-08-01T00:00:00Z",
};

const noLinkYetIntake: IntakeStatusResult = {
  hasActiveLink: false,
  publicSlug: null,
  createdAtUtc: null,
};

const mockBusinessSetup: KeepSetupResult = {
  businessName: "Acme Plumbing",
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

function renderRequests(
  role: "owner" | "admin" | "operator" | "viewer" = "owner",
  paneMode = false,
) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const onNavigateSettings = vi.fn();
  const onStartCapture = vi.fn();
  render(
    <QueryClientProvider client={queryClient}>
      <Requests
        role={role}
        viewCounts={null}
        onViewCountsUpdate={() => {}}
        onSelectRequest={() => {}}
        onNavigateSettings={onNavigateSettings}
        onStartCapture={onStartCapture}
        paneMode={paneMode}
      />
    </QueryClientProvider>,
  );
  return { onNavigateSettings, onStartCapture };
}

beforeEach(() => {
  mockGetRequests.mockReset();
  mockGetAvailableRequests.mockReset();
  mockGetGuidedSetup.mockReset();
  mockGetSetup.mockReset();
  mockGetIntake.mockReset();
  mockGetRequests.mockResolvedValue(emptyList);
  mockGetAvailableRequests.mockResolvedValue({ requests: [], pageInfo: emptyList.pageInfo });
  mockGetSetup.mockResolvedValue(mockBusinessSetup);
  mockGetIntake.mockResolvedValue(liveIntake);
});

describe("Requests empty-state panel (BL142 Session 3)", () => {
  it("shows the panel for an Owner with zero requests", async () => {
    mockGetGuidedSetup.mockResolvedValue(zeroRequestSetup);
    renderRequests("owner");

    expect(await screen.findByText("Your public request link is live")).toBeInTheDocument();
  });

  it("does not show the panel for an Operator", async () => {
    mockGetGuidedSetup.mockResolvedValue(zeroRequestSetup);
    renderRequests("operator");

    await waitFor(() => expect(screen.getByText("Requests")).toBeInTheDocument());
    expect(screen.queryByText("Your public request link is live")).not.toBeInTheDocument();
    expect(mockGetGuidedSetup).not.toHaveBeenCalled();
    expect(mockGetSetup).not.toHaveBeenCalled();
  });

  // Viewer never reaches the Requests page component: App.tsx renders AccessLimited
  // for role === "viewer" instead of mounting Requests at all.

  it("opens the customer view on the live link", async () => {
    mockGetGuidedSetup.mockResolvedValue(zeroRequestSetup);
    renderRequests("owner");

    const link = await screen.findByRole("link", { name: "Open customer view" });
    expect(link).toHaveAttribute("href", expect.stringContaining("/keep/s/acme-plumbing"));
  });

  it("routes the Add-your-first-request action to Quick Capture", async () => {
    mockGetGuidedSetup.mockResolvedValue(zeroRequestSetup);
    const user = userEvent.setup();
    const { onStartCapture } = renderRequests("owner");

    const cta = await screen.findByRole("button", { name: "Add your first request" });
    await user.click(cta);

    expect(onStartCapture).toHaveBeenCalled();
  });

  it("directs to Settings when the link isn't ready yet, instead of claiming it's live", async () => {
    mockGetGuidedSetup.mockResolvedValue(zeroRequestSetup);
    mockGetIntake.mockResolvedValue(noLinkYetIntake);
    const user = userEvent.setup();
    const { onNavigateSettings } = renderRequests("owner");

    expect(await screen.findByText("Your public request link is being set up")).toBeInTheDocument();

    const cta = screen.getByRole("button", { name: "Check in Settings" });
    await user.click(cta);

    expect(onNavigateSettings).toHaveBeenCalledWith("public-profile");
    expect(screen.queryByRole("link", { name: "Open customer view" })).not.toBeInTheDocument();
  });

  it("shows a checking heading, not a premature live claim, while the intake query is in flight", async () => {
    mockGetGuidedSetup.mockResolvedValue(zeroRequestSetup);
    let resolveIntake!: (value: IntakeStatusResult) => void;
    mockGetIntake.mockReturnValue(new Promise<IntakeStatusResult>((resolve) => { resolveIntake = resolve; }));
    renderRequests("owner");

    expect(await screen.findByText("Checking your public request link")).toBeInTheDocument();
    expect(screen.queryByText("Your public request link is live")).not.toBeInTheDocument();
    expect(screen.queryByText("Your public request link is being set up")).not.toBeInTheDocument();

    resolveIntake(liveIntake);
    expect(await screen.findByText("Your public request link is live")).toBeInTheDocument();
  });

  it("hides the panel once the business has added its first request", async () => {
    mockGetGuidedSetup.mockResolvedValue(hasRequestsSetup);
    renderRequests("owner");

    await waitFor(() => expect(mockGetGuidedSetup).toHaveBeenCalled());
    expect(screen.queryByText("Your public request link is live")).not.toBeInTheDocument();
  });

  // Layout glitch reported from the pilot demo: pane mode's request-list column is a fixed
  // 360px, so the default full-width panel (built around `sm:` breakpoints) never resolved
  // to its wide layout there and rendered squeezed into the list column. The compact panel
  // is the purpose-built stack-only layout for that column.
  it("renders the compact panel (not the full-width one) in pane mode", async () => {
    mockGetGuidedSetup.mockResolvedValue(zeroRequestSetup);
    renderRequests("owner", true);

    const region = await screen.findByRole("region", { name: "Get your first request" });
    expect(region.className).toContain("rounded-lg");
    expect(region.className).not.toContain("rounded-xl");
  });
});
