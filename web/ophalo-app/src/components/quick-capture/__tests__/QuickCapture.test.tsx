import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { QuickCapture } from "../../QuickCapture";
import type { IntakeStatusResult } from "../../../lib/apiClient";

// BL142 Session 3 Slice B: Owner/Admin lands on an explicit two-choice decision
// instead of the "Text a Link" handoff panel; each choice reaches its own existing flow.

const mockGetIntake = vi.fn();

vi.mock("../../../lib/apiClient", async () => {
  const actual = await vi.importActual<typeof import("../../../lib/apiClient")>(
    "../../../lib/apiClient",
  );
  return {
    ...actual,
    api: {
      ...actual.api,
      getIntake: (...args: unknown[]) => mockGetIntake(...args),
    },
  };
});

const activeIntake: IntakeStatusResult = {
  hasActiveLink: true,
  publicSlug: "apex-home-services",
  createdAtUtc: "2026-07-01T00:00:00Z",
};

function renderCapture(isOwnerOrAdmin: boolean) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <QuickCapture onClose={() => {}} isOwnerOrAdmin={isOwnerOrAdmin} />
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  mockGetIntake.mockReset();
  mockGetIntake.mockResolvedValue(activeIntake);
});

describe("QuickCapture entry stage", () => {
  it("shows the two-choice decision for Owner/Admin instead of the handoff panel directly", () => {
    renderCapture(true);

    expect(screen.getByRole("button", { name: /let the customer submit it/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /record it yourself/i })).toBeInTheDocument();
    expect(screen.queryByLabelText(/mobile number for a text link/i)).not.toBeInTheDocument();
  });

  it("routes 'let the customer submit it' to the text-a-link handoff panel", async () => {
    const user = userEvent.setup();
    renderCapture(true);

    await user.click(screen.getByRole("button", { name: /let the customer submit it/i }));

    expect(await screen.findByLabelText(/mobile number for a text link/i)).toBeInTheDocument();
  });

  it("routes 'record it yourself' to the phone lookup gate", async () => {
    const user = userEvent.setup();
    renderCapture(true);

    await user.click(screen.getByRole("button", { name: /record it yourself/i }));

    expect(await screen.findByLabelText(/customer phone number/i)).toBeInTheDocument();
  });

  it("skips the choice screen for a non-Owner/Admin caller", () => {
    renderCapture(false);

    expect(screen.getByLabelText(/customer phone number/i)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /let the customer submit it/i })).not.toBeInTheDocument();
  });
});
