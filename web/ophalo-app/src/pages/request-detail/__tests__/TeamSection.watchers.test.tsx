import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { WatchersSheet } from "../TeamSection";
import { mockRequestDetails, mockMembers, MOCK_USER_ID } from "../../../mocks/fixtures";

const mockListMembers = vi.fn();

vi.mock("../../../lib/apiClient", () => ({
  api: {
    listMembers: (...args: unknown[]) => mockListMembers(...args),
    addWatcher: vi.fn(),
    removeWatcher: vi.fn(),
  },
  ApiError: class ApiError extends Error {
    status: number;
    constructor(status: number, _code: string | undefined, message: string) {
      super(message);
      this.status = status;
    }
  },
}));

beforeEach(() => {
  vi.clearAllMocks();
  mockListMembers.mockResolvedValue(mockMembers);
});

function renderSheet() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <WatchersSheet
        requestId="mock-req-001"
        detail={mockRequestDetails["mock-req-001"]}
        onDetailUpdated={vi.fn()}
        onClose={vi.fn()}
      />
    </QueryClientProvider>,
  );
}

// Regression: Responsible and Watching are mutually exclusive (ADR-224/230) — the backend
// rejects adding the current owner as a watcher, so the "Add watcher…" list must exclude them
// too, matching OwnerReassignmentSheet's "Reassign to" exclusion of the same person.
describe("WatchersSheet — add-watcher candidate list", () => {
  it("excludes the current owner (Responsible) from the Add watcher options", async () => {
    // mock-req-001's sole participant is MOCK_USER_ID as Responsible ("Jamie Reyes").
    renderSheet();

    const select = await screen.findByLabelText("Add watcher");
    const optionEmails = Array.from(select.querySelectorAll("option")).map((o) => (o as HTMLOptionElement).value);

    const owner = mockMembers.members.find((m) => m.accountUserId === MOCK_USER_ID)!;
    expect(optionEmails).not.toContain(owner.accountUserId);

    // Other active members remain selectable.
    const others = mockMembers.members.filter((m) => m.accountUserId !== MOCK_USER_ID && m.status === "active");
    for (const m of others) {
      expect(screen.getByRole("option", { name: m.email })).toBeInTheDocument();
    }
  });
});
