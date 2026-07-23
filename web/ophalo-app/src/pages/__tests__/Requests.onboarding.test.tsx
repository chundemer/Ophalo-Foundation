import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { Requests } from "../Requests";
import type { KeepBusinessSetupResult, KeepRequestListResult } from "../../lib/apiClient";

const mockGetRequests = vi.fn();
const mockGetAvailableRequests = vi.fn();
const mockGetGuidedSetup = vi.fn();

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
    },
  };
});

const emptyList: KeepRequestListResult = {
  requests: [],
  pageInfo: { limit: 20, hasMore: false, nextCursor: null },
  viewCounts: null,
  listContext: { view: "default", isDefaultCommandCenter: true, isHistory: false, isSearch: false },
};

const incompleteSetup: KeepBusinessSetupResult = {
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

const completeSetup: KeepBusinessSetupResult = {
  ...incompleteSetup,
  businessInfoComplete: true,
  createIntakePageComplete: true,
  addFirstRequestComplete: true,
};

function renderRequests(role: "owner" | "admin" | "operator" | "viewer" = "owner") {
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
      />
    </QueryClientProvider>,
  );
  return { onNavigateSettings, onStartCapture };
}

beforeEach(() => {
  mockGetRequests.mockReset();
  mockGetAvailableRequests.mockReset();
  mockGetGuidedSetup.mockReset();
  mockGetRequests.mockResolvedValue(emptyList);
  mockGetAvailableRequests.mockResolvedValue({ requests: [], pageInfo: emptyList.pageInfo });
});

describe("Requests onboarding banner", () => {
  it("shows the banner for an Owner with incomplete core setup", async () => {
    mockGetGuidedSetup.mockResolvedValue(incompleteSetup);
    renderRequests("owner");

    expect(await screen.findByText("Set up your customer request page")).toBeInTheDocument();
  });

  it("does not show the banner for an Operator", async () => {
    mockGetGuidedSetup.mockResolvedValue(incompleteSetup);
    renderRequests("operator");

    await waitFor(() => expect(screen.getByText("Requests")).toBeInTheDocument());
    expect(screen.queryByText("Set up your customer request page")).not.toBeInTheDocument();
    expect(mockGetGuidedSetup).not.toHaveBeenCalled();
  });

  // Viewer never reaches the Requests page component: App.tsx renders AccessLimited
  // for role === "viewer" instead of mounting Requests at all.

  it("navigates the primary CTA directly to Settings public-profile", async () => {
    mockGetGuidedSetup.mockResolvedValue(incompleteSetup);
    const user = userEvent.setup();
    const { onNavigateSettings } = renderRequests("owner");

    const cta = await screen.findByRole("button", { name: "Set up request page" });
    await user.click(cta);

    expect(onNavigateSettings).toHaveBeenCalledWith("public-profile");
  });

  it("opens Quick Capture from the first-request checklist item", async () => {
    mockGetGuidedSetup.mockResolvedValue(incompleteSetup);
    const user = userEvent.setup();
    const { onStartCapture } = renderRequests("owner");

    const step = await screen.findByRole("button", { name: /Add your first customer request/ });
    await user.click(step);

    expect(onStartCapture).toHaveBeenCalled();
  });

  it("hides the banner once the real core setup fields are complete", async () => {
    mockGetGuidedSetup.mockResolvedValue(completeSetup);
    renderRequests("owner");

    await waitFor(() => expect(mockGetGuidedSetup).toHaveBeenCalled());
    expect(screen.queryByText("Set up your customer request page")).not.toBeInTheDocument();
  });
});
