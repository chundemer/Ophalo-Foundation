import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { TeamSection } from "../TeamSection";
import type { ListMembersResponse } from "../../../lib/apiClient.types";

// BL142 Session 3b — Team invite clarity.
// Contract: docs/build-log/142-pilot-onboarding-upgrade-handoff.md §Session 3b.

const mockListMembers = vi.fn();
const mockInviteMember = vi.fn();

vi.mock("../../../lib/apiClient", async () => {
  const actual = await vi.importActual<typeof import("../../../lib/apiClient")>(
    "../../../lib/apiClient",
  );
  return {
    ...actual,
    api: {
      ...actual.api,
      listMembers: (...a: unknown[]) => mockListMembers(...a),
      inviteMember: (...a: unknown[]) => mockInviteMember(...a),
    },
  };
});

const oneMember: ListMembersResponse = {
  members: [
    {
      accountUserId: "au-1",
      email: "owner@apex.example",
      role: "owner",
      status: "active",
      isCurrentUser: true,
      isPrimaryOwner: true,
      activatedAtUtc: "2026-01-01T00:00:00Z",
      inviteExpiresAtUtc: null,
    },
  ],
  seatUsage: { occupiedSeats: 1, maxSeats: 5, atLimit: false, limitApplies: true },
};

function renderSection() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <TeamSection callerRole="owner" />
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  mockListMembers.mockResolvedValue(oneMember);
});

describe("TeamSection — invite clarity (Session 3b)", () => {
  it("states the invitation lifecycle before submission", async () => {
    renderSection();
    expect(
      await screen.findByText(/They do not have access until they accept it/i),
    ).toBeInTheDocument();
  });

  it("does not offer Owner as an invite role", async () => {
    renderSection();
    await screen.findByPlaceholderText("Email address");
    const role = screen.getByRole("combobox") as HTMLSelectElement;
    const optionValues = Array.from(role.options).map((o) => o.value);
    expect(optionValues).not.toContain("owner");
    expect(optionValues).toEqual(["admin", "operator", "viewer"]);
  });

  it("defaults to Operator and shows its business-outcome description", async () => {
    renderSection();
    const role = screen.getByRole("combobox") as HTMLSelectElement;
    await waitFor(() => expect(role.value).toBe("operator"));
    expect(
      await screen.findByText(/cannot manage team, settings, or the Price Book/i),
    ).toBeInTheDocument();
  });

  it("updates the description when a different role is chosen", async () => {
    renderSection();
    const role = screen.getByRole("combobox") as HTMLSelectElement;
    await screen.findByPlaceholderText("Email address");
    fireEvent.change(role, { target: { value: "admin" } });
    expect(
      await screen.findByText(/can manage settings, team members, and the Price Book/i),
    ).toBeInTheDocument();

    fireEvent.change(role, { target: { value: "viewer" } });
    expect(await screen.findByText(/Read-only visibility into requests/i)).toBeInTheDocument();
  });

  it("reports invite success as pending acceptance, not a promised delivery", async () => {
    mockInviteMember.mockResolvedValue(undefined);
    renderSection();

    const email = await screen.findByPlaceholderText("Email address");
    fireEvent.change(email, { target: { value: "new.hire@apex.example" } });
    fireEvent.click(screen.getByRole("button", { name: /invite team member/i }));

    expect(
      await screen.findByText(/pending acceptance/i),
    ).toBeInTheDocument();
    expect(screen.queryByText(/receive an email link/i)).not.toBeInTheDocument();
  });
});
