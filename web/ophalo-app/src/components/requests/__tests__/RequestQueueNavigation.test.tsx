import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { RequestQueueNavigation } from "../RequestQueueNavigation";
import { getTabsForRole } from "../../../pages/requestsWorkspace";

// Locked decision (2026-08-24): pane mode renders one equal three-tab row using compact visual
// labels (Attention/All/Mine/Available) plus dense inline counts, so all three tabs fit on one
// row at the ~360px pane width. Accessible names stay the full TabDef.label via aria-label.
// Request Queue header consolidation (locked 2026-08-24) moved Office Review/Views/History out of
// this component into RequestListToolbar's Views popover — this file now covers the primary-tabs
// row and the history sub-header only.

function baseProps(role: "owner" | "operator") {
  return {
    tabs: getTabsForRole(role),
    activeTab: getTabsForRole(role)[0],
    viewCounts: null,
    onSelectTab: vi.fn(),
    historyMode: false,
    historyScope: "all_history" as const,
    historyDateScope: "all_time" as const,
    onExitHistory: vi.fn(),
    onUpdateHistoryScope: vi.fn(),
    onUpdateHistoryDateScope: vi.fn(),
  };
}

describe("RequestQueueNavigation pane mode (single-row compact tabs, locked 2026-08-24)", () => {
  it("Owner/Admin: renders exactly one row with all three tabs, accessible names intact", () => {
    render(<RequestQueueNavigation {...baseProps("owner")} paneMode />);

    const tablist = screen.getByRole("tablist", { name: "Request queues" });
    const tabs = screen.getAllByRole("tab");
    expect(tabs).toHaveLength(3);
    expect(tablist.children).toHaveLength(3);
    expect(tabs.map((t) => t.getAttribute("aria-label"))).toEqual([
      "Needs Attention",
      "All Work",
      "My Work",
    ]);
  });

  it("Operator: renders one row with role-ordered accessible names intact", () => {
    render(<RequestQueueNavigation {...baseProps("operator")} paneMode />);

    const tabs = screen.getAllByRole("tab");
    expect(tabs.map((t) => t.getAttribute("aria-label"))).toEqual([
      "My Work",
      "Needs Attention",
      "Available Work",
    ]);
  });

  it("pane mode row does not use horizontal-scroll/clip classes and meets the 44px touch-target minimum", () => {
    render(<RequestQueueNavigation {...baseProps("owner")} paneMode />);

    const tablist = screen.getByRole("tablist", { name: "Request queues" });
    expect(tablist.className).not.toMatch(/overflow-x-auto/);

    for (const tab of screen.getAllByRole("tab")) {
      expect(tab.className).toMatch(/min-h-11/);
      expect(tab.className).not.toMatch(/whitespace-nowrap/);
    }
  });

  it("uses compact visual labels in pane mode while accessible names stay full", () => {
    render(<RequestQueueNavigation {...baseProps("owner")} paneMode />);
    expect(screen.getByText("Attention")).toBeInTheDocument();
    expect(screen.getByText("All")).toBeInTheDocument();
    expect(screen.getByText("Mine")).toBeInTheDocument();
    expect(screen.queryByText("Needs Attention")).not.toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Needs Attention" })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "All Work" })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "My Work" })).toBeInTheDocument();
  });

  it("renders counts as a badge pill, not inline dot-notation, in pane mode", () => {
    render(<RequestQueueNavigation {...baseProps("owner")} viewCounts={{ needsAttention: 13, default: 16, assignedToMe: 4 } as never} paneMode />);
    expect(screen.getByText("13")).toBeInTheDocument();
    expect(screen.getByText("16")).toBeInTheDocument();
    expect(screen.getByText("4")).toBeInTheDocument();
    expect(screen.queryByText(/^· /)).not.toBeInTheDocument();
  });

  it("keeps the current full-width layout unchanged when paneMode is omitted", () => {
    render(<RequestQueueNavigation {...baseProps("owner")} />);

    const tablist = screen.getByRole("tablist", { name: "Request queues" });
    expect(tablist.className).toMatch(/overflow-x-auto/);
    expect(tablist.children).toHaveLength(3);
  });
});

describe("RequestQueueNavigation history mode", () => {
  it("renders the history sub-header with Back to queues, scope, and date-range controls", () => {
    render(<RequestQueueNavigation {...baseProps("owner")} historyMode />);

    expect(screen.getByRole("button", { name: /Back to queues/ })).toBeInTheDocument();
    expect(screen.getByRole("group", { name: "History scope" })).toBeInTheDocument();
    expect(screen.getByRole("group", { name: "Date range" })).toBeInTheDocument();
    expect(screen.queryByRole("tablist")).not.toBeInTheDocument();
  });
});
