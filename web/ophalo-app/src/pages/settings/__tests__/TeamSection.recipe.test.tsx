import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { TeamSection } from "../TeamSection";
import type { ListMembersResponse } from "../../../lib/apiClient.types";

// Settings & Getting Started V2 UI upgrade — Slice C (Team).
// Contract: docs/ux-design/v2/settings-and-getting-started-ui-upgrade.md §3.7 / §6.
// Pure visual restyle: the invite row moves to the shared `keep-field` recipe,
// member rows keep a consistent tokenized list-row treatment, solo-owner
// reassurance copy stays, and seat usage is shown from the server value only.

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

const twoMembers: ListMembersResponse = {
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
    {
      accountUserId: "au-2",
      email: "op@apex.example",
      role: "operator",
      status: "active",
      isCurrentUser: false,
      isPrimaryOwner: false,
      activatedAtUtc: "2026-02-01T00:00:00Z",
      inviteExpiresAtUtc: null,
    },
  ],
  seatUsage: { occupiedSeats: 2, maxSeats: 5, atLimit: false, limitApplies: true },
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
  mockListMembers.mockResolvedValue(twoMembers);
});

describe("TeamSection — V2 restyle (Slice C)", () => {
  it("puts the invite email + role controls on the shared keep-field recipe", async () => {
    renderSection();

    const email = await screen.findByPlaceholderText("Email address");
    expect(email).toHaveClass("keep-field");

    const role = screen.getByRole("combobox");
    expect(role).toHaveClass("keep-field");
  });

  it("renders member rows in a single tokenized list-row container (no slate/emerald)", async () => {
    const { container } = renderSection();

    await screen.findByText("owner@apex.example");
    expect(screen.getByText("op@apex.example")).toBeInTheDocument();

    const list = container.querySelector(".divide-y");
    expect(list).not.toBeNull();
    expect(container.innerHTML).not.toMatch(/slate-|emerald-|teal-600/);
  });

  it("keeps the solo-owner reassurance copy", async () => {
    renderSection();
    expect(
      await screen.findByText(/Keep works great for solo businesses/i),
    ).toBeInTheDocument();
  });

  it("shows seat usage from the server value, not a row count", async () => {
    renderSection();
    expect(await screen.findByText("Team seats: 2 of 5 used")).toBeInTheDocument();
  });
});
