import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { Requests } from "../Requests";
import { mockViewCounts } from "../../mocks/fixtures";

// GAP-092 3: proves the Requests.tsx page-entry path actually supplies the resolved business
// timezone to ActualWorkReviewQueueList — not just that the child renders correctly when given
// one directly (that's covered in ActualWorkReviewQueueList.test.tsx). The child is mocked to
// capture the exact timeZone prop it receives from the real page.

const mockGetRequests = vi.fn();
const mockGetAvailableRequests = vi.fn();
const mockGetGuidedSetup = vi.fn();
const mockGetSetup = vi.fn();
const mockGetMe = vi.fn();
const mockGetActualWorkReviewQueue = vi.fn();
const mockGetActualWorkReviewQueueCount = vi.fn();

vi.mock("../../lib/apiClient", async () => {
  const actual = await vi.importActual<typeof import("../../lib/apiClient")>("../../lib/apiClient");
  return {
    ...actual,
    api: {
      ...actual.api,
      getRequests: (...args: unknown[]) => mockGetRequests(...args),
      getAvailableRequests: (...args: unknown[]) => mockGetAvailableRequests(...args),
      getGuidedSetup: (...args: unknown[]) => mockGetGuidedSetup(...args),
      getSetup: (...args: unknown[]) => mockGetSetup(...args),
      getMe: (...args: unknown[]) => mockGetMe(...args),
      getActualWorkReviewQueue: (...args: unknown[]) => mockGetActualWorkReviewQueue(...args),
      getActualWorkReviewQueueCount: (...args: unknown[]) => mockGetActualWorkReviewQueueCount(...args),
    },
  };
});

let capturedTimeZone: string | null | undefined = "not-called";
vi.mock("../../components/requests/ActualWorkReviewQueueList", () => ({
  ActualWorkReviewQueueList: ({ timeZone }: { timeZone: string | null }) => {
    capturedTimeZone = timeZone;
    return <div data-testid="captured-review-queue">captured</div>;
  },
}));

function renderRequests() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <Requests
        role="owner"
        viewCounts={mockViewCounts}
        onViewCountsUpdate={() => {}}
        onSelectRequest={() => {}}
        onNavigateSettings={() => {}}
        onStartCapture={() => {}}
      />
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  capturedTimeZone = "not-called";
  mockGetRequests.mockReset();
  mockGetAvailableRequests.mockReset();
  mockGetGuidedSetup.mockReset();
  mockGetSetup.mockReset();
  mockGetMe.mockReset();
  mockGetActualWorkReviewQueue.mockReset();
  mockGetActualWorkReviewQueueCount.mockReset();

  mockGetRequests.mockResolvedValue({
    requests: [],
    pageInfo: { limit: 50, hasMore: false, nextCursor: null },
    viewCounts: null,
    listContext: { view: "default", isDefaultCommandCenter: true, isHistory: false, isSearch: false },
  });
  mockGetAvailableRequests.mockResolvedValue({ requests: [], pageInfo: { limit: 50, hasMore: false, nextCursor: null } });
  mockGetGuidedSetup.mockResolvedValue({
    businessInfoComplete: true,
    addFirstRequestComplete: true,
    reviewCustomerPageComplete: true,
    createIntakePageComplete: true,
    shareIntakePageComplete: true,
    buildTeamComplete: true,
    useMobileComplete: true,
    deferredSteps: [],
    intendedTeamSize: null,
  });
  mockGetActualWorkReviewQueue.mockResolvedValue([]);
  mockGetActualWorkReviewQueueCount.mockResolvedValue({ count: 1 });
});

describe("Requests — GAP-092 business-timezone page-entry wiring", () => {
  it("passes the resolved business zone (from ['me']) down to ActualWorkReviewQueueList on the Actual Work Review tab", async () => {
    mockGetMe.mockResolvedValue({
      accountUserId: "mock-user-1",
      accountId: "mock-account-1",
      isAuthenticated: true,
      isVerified: true,
      accountRole: "owner",
      businessName: "Acme Plumbing",
      userName: "Riley Owner",
      timeZone: "America/Los_Angeles",
    });

    renderRequests();

    fireEvent.click(await screen.findByRole("button", { name: "Views" }));
    fireEvent.click(await screen.findByRole("button", { name: /Actual Work Review/ }));
    await screen.findByTestId("captured-review-queue");

    expect(capturedTimeZone).toBe("America/Los_Angeles");
  });

  it("passes null (never a device-local guess) while ['me'] has not yet resolved a zone", async () => {
    mockGetMe.mockResolvedValue({
      accountUserId: "mock-user-1",
      accountId: "mock-account-1",
      isAuthenticated: true,
      isVerified: true,
      accountRole: "owner",
      businessName: "Acme Plumbing",
      userName: "Riley Owner",
      timeZone: null,
    });

    renderRequests();

    fireEvent.click(await screen.findByRole("button", { name: "Views" }));
    fireEvent.click(await screen.findByRole("button", { name: /Actual Work Review/ }));
    await screen.findByTestId("captured-review-queue");

    expect(capturedTimeZone).toBeNull();
  });
});
