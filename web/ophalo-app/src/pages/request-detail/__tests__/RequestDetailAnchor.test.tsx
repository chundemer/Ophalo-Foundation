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

    expect(screen.queryByRole("button", { name: "Log contact" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /mark work done/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Close request" })).not.toBeInTheDocument();
    // Factual context remains visible even when no mutation is authorized
    expect(screen.getByText("Customer contact")).toBeInTheDocument();
    expect(screen.getByText("Service location")).toBeInTheDocument();
  });
});
