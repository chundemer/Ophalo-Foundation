import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { RequestQueueNavigation } from "../RequestQueueNavigation";
import { getTabsForRole, getSecondaryViewsForRole, getOfficeReviewMembersForRole } from "../../../pages/requestsWorkspace";

// UI-001 post-Step-4 density refinement (build-log 134 §1/§2, locked 2026-08-21): Row 1 keeps
// the two-row primary tab grid (tabs[0] full width, tabs[1]/tabs[2] share row 2) — a single
// 3-way row with full labels + badge-pill counts was measured (real-browser mock harness) to
// wrap "Needs Attention" at the true 360px pane width, so it was rejected. Row 2 groups Office
// Review (left) with Views/History (right). Automated coverage here is structural/contract only;
// the actual "no clip / no horizontal scroll" render at 360px and 100/125/150% zoom still needs
// manual browser verification.

function baseProps(role: "owner" | "operator") {
  return {
    tabs: getTabsForRole(role),
    activeTab: getTabsForRole(role)[0],
    viewCounts: null,
    secondaryViews: getSecondaryViewsForRole(role),
    officeReviewMembers: getOfficeReviewMembersForRole(role),
    officeReview: { status: "ready" as const, aggregate: 1, members: { readyToClose: 1, feedbackReview: 0, actualWorkReview: 0 } },
    onSelectTab: vi.fn(),
    historyMode: false,
    historyScope: "all_history" as const,
    historyDateScope: "all_time" as const,
    isOwnerOrAdmin: role === "owner",
    onEnterHistory: vi.fn(),
    onExitHistory: vi.fn(),
    onUpdateHistoryScope: vi.fn(),
    onUpdateHistoryDateScope: vi.fn(),
  };
}

describe("RequestQueueNavigation pane mode (UI-001 post-Step-4 density refinement)", () => {
  it("Owner/Admin: tabs[0] (Needs Attention) renders alone in row 1, All Work/My Work share row 2", () => {
    render(<RequestQueueNavigation {...baseProps("owner")} paneMode />);

    const tablist = screen.getByRole("tablist", { name: "Request queues" });
    const tabs = screen.getAllByRole("tab");
    expect(tabs.map((t) => t.textContent)).toEqual([
      expect.stringContaining("Needs Attention"),
      expect.stringContaining("All Work"),
      expect.stringContaining("My Work"),
    ]);

    const rows = tablist.children;
    expect(rows).toHaveLength(2);
    expect(rows[0].children).toHaveLength(1);
    expect(rows[1].children).toHaveLength(2);
  });

  it("Operator: tabs[0] (My Work) renders alone in row 1, Needs Attention/Available share row 2", () => {
    render(<RequestQueueNavigation {...baseProps("operator")} paneMode />);

    const tabs = screen.getAllByRole("tab");
    expect(tabs.map((t) => t.textContent)).toEqual([
      expect.stringContaining("My Work"),
      expect.stringContaining("Needs Attention"),
      expect.stringContaining("Available Work"),
    ]);
  });

  it("pane mode grid does not use horizontal-scroll/clip classes and meets the 44px touch-target minimum", () => {
    render(<RequestQueueNavigation {...baseProps("owner")} paneMode />);

    const tablist = screen.getByRole("tablist", { name: "Request queues" });
    expect(tablist.className).not.toMatch(/overflow-x-auto/);

    for (const tab of screen.getAllByRole("tab")) {
      expect(tab.className).toMatch(/min-h-11/);
      expect(tab.className).not.toMatch(/whitespace-nowrap/);
    }
  });

  it("does not abbreviate primary tab labels in pane mode", () => {
    render(<RequestQueueNavigation {...baseProps("owner")} paneMode />);
    expect(screen.getByText("Needs Attention")).toBeInTheDocument();
    expect(screen.getByText("All Work")).toBeInTheDocument();
    expect(screen.getByText("My Work")).toBeInTheDocument();
  });

  it("renders counts as a badge pill, not inline dot-notation, in pane mode", () => {
    render(<RequestQueueNavigation {...baseProps("owner")} viewCounts={{ needsAttention: 13, default: 16, assignedToMe: 4 } as never} paneMode />);
    expect(screen.getByText("13")).toBeInTheDocument();
    expect(screen.getByText("16")).toBeInTheDocument();
    expect(screen.getByText("4")).toBeInTheDocument();
    expect(screen.queryByText(/^· /)).not.toBeInTheDocument();
  });

  it("Owner/Admin: Office Review and Views/History share row 2, Office Review on the left", () => {
    render(<RequestQueueNavigation {...baseProps("owner")} paneMode />);

    const officeReview = screen.getByRole("button", { name: /Office Review/ });
    const views = screen.getByRole("button", { name: "Views" });
    const history = screen.getByRole("button", { name: "History" });

    // Office Review precedes Views/History in document order (left-to-right).
    expect(officeReview.compareDocumentPosition(views) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(officeReview.compareDocumentPosition(history) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it("Operator pane mode: no Office Review (not an office-review role)", () => {
    render(<RequestQueueNavigation {...baseProps("operator")} paneMode />);
    expect(screen.queryByRole("button", { name: /Office Review/ })).not.toBeInTheDocument();
  });

  it("keeps the current full-width layout unchanged when paneMode is omitted", () => {
    render(<RequestQueueNavigation {...baseProps("owner")} />);

    const tablist = screen.getByRole("tablist", { name: "Request queues" });
    expect(tablist.className).toMatch(/overflow-x-auto/);
    expect(tablist.children).toHaveLength(3);
  });
});
