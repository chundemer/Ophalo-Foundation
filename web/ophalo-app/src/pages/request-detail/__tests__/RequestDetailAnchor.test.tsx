import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { RequestDetailAnchor } from "../RequestDetailAnchor";
import { mockRequestDetails, OWNER_ACTIONS } from "../../../mocks/fixtures";
import type { KeepRequestDetailResult } from "../../../lib/apiClient";

beforeEach(() => {
  vi.stubEnv("VITE_PUBLIC_BASE_URL", "http://localhost:3000");
});

// Three-row desktop Anchor hierarchy (locked correction, 2026-08-22): one outer bordered/rounded
// card with reference/status/attention (row 1 left), Log contact + primary action (row 1 right),
// full-width customer identity (row 2), a divider, then three stable context columns (row 3).

function baseDetail(): KeepRequestDetailResult {
  return {
    ...mockRequestDetails["mock-req-001"],
    effectiveAttention: { ...mockRequestDetails["mock-req-001"].effectiveAttention },
  };
}

function renderAnchor(detail: KeepRequestDetailResult) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <RequestDetailAnchor
        requestId="req-1"
        detail={detail}
        highlights={{}}
        showProminentFeedbackCard={false}
        onDetailUpdated={vi.fn()}
        onContactLaunched={vi.fn()}
        onEditLocation={vi.fn()}
        onOpenReassignOwner={vi.fn()}
        onRecordFollowUp={vi.fn()}
        onCreateFollowUp={vi.fn()}
        onReviewSuccess={vi.fn()}
        canRecordShareIntent={false}
        needsShare={false}
        onOpenShareDrawer={vi.fn()}
      />
    </QueryClientProvider>,
  );
}

describe("RequestDetailAnchor — three-row desktop hierarchy", () => {
  it("renders one outer bordered card with row 1 badges, row 2 full-width identity, a divider, and three row-3 context columns", () => {
    const detail = baseDetail();
    const { container } = renderAnchor(detail);

    // Row 1 left: reference/status
    expect(screen.getByText(detail.referenceCode)).toBeInTheDocument();
    // Row 2: customer identity as its own full-width row
    expect(screen.getByRole("heading", { name: detail.customerName })).toBeInTheDocument();
    // Divider between row 2 and row 3
    expect(container.querySelector(".border-t")).not.toBeNull();
    // Row 3: three stable context columns
    expect(screen.getByText("Customer contact")).toBeInTheDocument();
    expect(screen.getByText("Service location")).toBeInTheDocument();
    expect(screen.getByText("Owner")).toBeInTheDocument();
    // Row 3 renders as a three-column grid, not a flattened single-line strip
    const grid = container.querySelector(".grid.sm\\:grid-cols-3");
    expect(grid).not.toBeNull();

    // Not a bare full-width strip — the Anchor is one rounded, bordered outer card
    const card = container.querySelector(".rounded-xl.border");
    expect(card).not.toBeNull();
  });

  it("shows the filled primary action for an eligible, non-attention, non-Received request", () => {
    const detail: KeepRequestDetailResult = { ...baseDetail(), attentionLevel: "none" };
    renderAnchor(detail);
    expect(screen.getByRole("button", { name: "Mark work done" })).toBeInTheDocument();
  });

  it("demotes Mark work done and hides Close when active attention exists", () => {
    const detail = mockRequestDetails["mock-req-002"];
    renderAnchor(detail);

    expect(screen.getByRole("button", { name: "Mark work done, attention remains" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Close request" })).not.toBeInTheDocument();
  });

  it("shows Close as the primary action when resolved, attention-free, and authorized", () => {
    const detail: KeepRequestDetailResult = {
      ...baseDetail(),
      status: "resolved",
      attentionLevel: "none",
      availableActions: { ...OWNER_ACTIONS },
    };
    renderAnchor(detail);

    expect(screen.getByRole("button", { name: "Close request" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /mark work done/i })).not.toBeInTheDocument();
  });

  it("Row 4 (locked correction, 2026-08-24): renders three persistently labeled controls in locked order — Internal priority, Planned work date, Set internal follow-up", () => {
    const detail: KeepRequestDetailResult = {
      ...baseDetail(),
      businessPriority: null,
      plannedForDate: null,
      followUpOnDate: null,
    };
    const { container } = renderAnchor(detail);

    const grid = container.querySelector(".mt-3.grid")!;
    expect(grid).not.toBeNull();
    // Only the three top-level field labels — not the nested date-editor popover's own labels.
    const labels = Array.from(grid.querySelectorAll(":scope > div > label")).map((el) => el.textContent);
    expect(labels).toEqual(["Internal priority", "Planned work date", "Set internal follow-up"]);

    // Not a passive metadata strip — no card chrome, one compact three-column row on desktop.
    expect(grid.className).toContain("sm:grid-cols-3");
  });

  it("Row 4: exact empty-state control copy — never 'Not planned' or 'No follow-up'", () => {
    const detail: KeepRequestDetailResult = {
      ...baseDetail(),
      businessPriority: null,
      plannedForDate: null,
      followUpOnDate: null,
    };
    renderAnchor(detail);

    expect(screen.getByRole("combobox", { name: "Internal priority" })).toBeInTheDocument();
    expect((screen.getByRole("combobox", { name: "Internal priority" }) as HTMLSelectElement).value).toBe("");
    expect(screen.getByText("Set planned work date…")).toBeInTheDocument();
    expect(screen.getByText("Set internal follow-up…")).toBeInTheDocument();
    expect(screen.queryByText("Not planned")).not.toBeInTheDocument();
    expect(screen.queryByText("No follow-up")).not.toBeInTheDocument();
  });

  it("Row 4: renders the formatted date when planned/follow-up are set (authorized interaction path)", () => {
    const detail: KeepRequestDetailResult = {
      ...baseDetail(),
      businessPriority: "urgent",
      plannedForDate: "2026-08-29",
      followUpOnDate: "2026-08-26",
      followUpOnReason: "reminder",
    };
    const { container } = renderAnchor(detail);

    const prioritySelect = screen.getByRole("combobox", { name: "Internal priority" });
    expect((prioritySelect as HTMLSelectElement).value).toBe("urgent");
    expect(prioritySelect.className).toContain("text-[var(--ophalo-danger)]");

    expect(screen.getByRole("button", { name: "Planned work date: Aug 29, 2026" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Set internal follow-up: Aug 26, 2026" })).toBeInTheDocument();
    expect(screen.queryByText("Set planned work date…")).not.toBeInTheDocument();
    expect(screen.queryByText("Set internal follow-up…")).not.toBeInTheDocument();

    // Still a single outer Anchor card — Row 4 adds a top separator, not a nested bordered box.
    expect(container.querySelectorAll(".rounded-xl.border").length).toBe(1);
  });

  it("Row 4 correction (locked 2026-08-24): a set planned/follow-up date stays visible as a read-only labeled value even when the viewer lacks the edit permission — never hidden", () => {
    const detail: KeepRequestDetailResult = {
      ...baseDetail(),
      plannedForDate: "2026-08-29",
      followUpOnDate: "2026-08-26",
      followUpOnReason: "reminder",
      availableActions: { ...baseDetail().availableActions, canSetPlannedFor: false, canSetFollowUpOn: false },
    };
    renderAnchor(detail);

    expect(screen.getByText("Planned work date")).toBeInTheDocument();
    expect(screen.getByText("Set internal follow-up")).toBeInTheDocument();
    expect(screen.getByText("Aug 29, 2026")).toBeInTheDocument();
    expect(screen.getByText("Aug 26, 2026")).toBeInTheDocument();
    // Read-only: not an interactive trigger.
    expect(screen.queryByRole("button", { name: /Aug 29, 2026/ })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Aug 26, 2026/ })).not.toBeInTheDocument();
  });

  it("Row 4: omits an unauthorized, unset planned/follow-up field rather than rendering a dead control", () => {
    const detail: KeepRequestDetailResult = {
      ...baseDetail(),
      plannedForDate: null,
      followUpOnDate: null,
      availableActions: { ...baseDetail().availableActions, canSetPlannedFor: false, canSetFollowUpOn: false },
    };
    const { container } = renderAnchor(detail);

    expect(screen.queryByText("Planned work date")).not.toBeInTheDocument();
    expect(screen.queryByText("Set internal follow-up")).not.toBeInTheDocument();
    // Priority always renders (Routine is a real value, not an unset state).
    expect(screen.getByText("Internal priority")).toBeInTheDocument();
    const grid = container.querySelector(".mt-3.grid")!;
    expect(grid.children.length).toBe(1);
  });

  it("renders no primary/Log-contact controls for a read-only/unauthorized viewer", () => {
    const detail: KeepRequestDetailResult = {
      ...baseDetail(),
      attentionLevel: "none",
      availableActions: {
        ...OWNER_ACTIONS,
        canChangeStatus: false,
        canClose: false,
        canLogExternalContact: false,
        canAssignResponsible: false,
        canAddInternalNote: false,
      },
    };
    renderAnchor(detail);

    expect(screen.queryByRole("button", { name: "Contact customer" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /mark work done/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Close request" })).not.toBeInTheDocument();
    // Factual context remains visible even when no mutation is authorized
    expect(screen.getByText("Customer contact")).toBeInTheDocument();
    expect(screen.getByText("Service location")).toBeInTheDocument();
  });
});
