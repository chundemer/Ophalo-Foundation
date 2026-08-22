import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { WorkDoneCard, CloseRequestCard } from "../BusinessSection";
import { mockRequestDetails } from "../../../mocks/fixtures";
import type { KeepRequestDetailResult } from "../../../lib/apiClient";

// The sticky Request Anchor renders these as its one compact authorized primary control (locked
// spec: two-to-three-row context strip, not a stack of action-card dashboards). `compact` must
// drop the card chrome (heading, description, badge) while keeping the same authorization gate,
// submit call, and error/conflict handling as the full-card render used elsewhere in the canvas.

vi.mock("../../../lib/apiClient", async () => {
  const actual = await vi.importActual<typeof import("../../../lib/apiClient")>(
    "../../../lib/apiClient",
  );
  return {
    ...actual,
    api: { ...actual.api, patchRequestStatus: vi.fn() },
  };
});

function baseDetail(overrides: Partial<KeepRequestDetailResult> = {}): KeepRequestDetailResult {
  return { ...mockRequestDetails["mock-req-001"], ...overrides };
}

beforeEach(() => {
  vi.clearAllMocks();
});

describe("CloseRequestCard compact — Anchor primary", () => {
  it("renders the authorized Close action as a bare button, with no card heading/badge, when canClose is eligible", () => {
    const detail = baseDetail({
      status: "resolved",
      attentionLevel: "none",
      availableActions: {
        ...mockRequestDetails["mock-req-001"].availableActions,
        canClose: true,
        allowedStatuses: ["closed"],
      },
    });

    render(
      <CloseRequestCard
        requestId="req-1"
        detail={detail}
        onDetailUpdated={vi.fn()}
        compact
      />,
    );

    expect(screen.getByRole("button", { name: "Close request" })).toBeInTheDocument();
    expect(screen.queryByText("Ready to close")).not.toBeInTheDocument();
  });

  it("renders nothing when canClose is not eligible, compact or not", () => {
    const detail = baseDetail({
      status: "in_progress",
      attentionLevel: "none",
      availableActions: {
        ...mockRequestDetails["mock-req-001"].availableActions,
        canClose: false,
        allowedStatuses: [],
      },
    });

    const { container } = render(
      <CloseRequestCard requestId="req-1" detail={detail} onDetailUpdated={vi.fn()} compact />,
    );

    expect(container).toBeEmptyDOMElement();
  });
});

describe("WorkDoneCard compact — Anchor primary", () => {
  it("renders the authorized Mark-work-done action as a bare button, with no card heading/badge", () => {
    const detail = baseDetail({
      status: "in_progress",
      attentionLevel: "none",
      availableActions: {
        ...mockRequestDetails["mock-req-001"].availableActions,
        canChangeStatus: true,
        allowedStatuses: ["resolved"],
      },
    });

    render(
      <WorkDoneCard requestId="req-1" detail={detail} onDetailUpdated={vi.fn()} compact />,
    );

    expect(screen.getByRole("button", { name: "Mark work done" })).toBeInTheDocument();
    expect(screen.queryByText("Work completed")).not.toBeInTheDocument();
  });
});
