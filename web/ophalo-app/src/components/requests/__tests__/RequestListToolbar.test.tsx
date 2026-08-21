import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { RequestListToolbar } from "../RequestListToolbar";

// UI-001 post-Step-4 density refinement (build-log 134 §3, locked 2026-08-21): in the Queue
// pane, Search and the status filter share one row. The filter stays a native <select> (same
// options/values/onChange contract) but its collapsed-state display becomes a compact "Filter"
// label with a one-tap clear when a status is applied. Undefined/false must render the exact
// full-page/narrow toolbar unchanged.

function baseProps(overrides: Partial<React.ComponentProps<typeof RequestListToolbar>> = {}) {
  return {
    isAvailableTab: false,
    historyMode: false,
    presentAsHistory: false,
    searchInputRef: { current: null },
    draftQ: "",
    onDraftQChange: vi.fn(),
    onSubmitSearch: vi.fn(),
    onClearSearch: vi.fn(),
    statusFilter: "",
    onStatusFilterChange: vi.fn(),
    showStalenessNotice: false,
    onManualRefresh: vi.fn(),
    appliedLineText: null,
    ...overrides,
  };
}

describe("RequestListToolbar pane mode (UI-001 post-Step-4 density refinement)", () => {
  it("pane mode: collapsed filter reads 'Filter' and shows no clear button when no status is applied", () => {
    render(<RequestListToolbar {...baseProps()} paneMode />);

    const select = screen.getByRole("combobox", { name: "Filter by status" });
    expect(select).toHaveValue("");
    expect(screen.getByText("Filter")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Clear status filter" })).not.toBeInTheDocument();
  });

  it("pane mode: an applied status shows a one-tap clear that resets the filter", () => {
    const onStatusFilterChange = vi.fn();
    render(<RequestListToolbar {...baseProps({ statusFilter: "pending_customer", onStatusFilterChange })} paneMode />);

    const clearButton = screen.getByRole("button", { name: "Clear status filter" });
    clearButton.click();
    expect(onStatusFilterChange).toHaveBeenCalledWith("");
  });

  it("pane mode: search input keeps its accessible label and value contract", () => {
    render(<RequestListToolbar {...baseProps({ draftQ: "cruz" })} paneMode />);
    expect(screen.getByRole("textbox", { name: "Search requests" })).toHaveValue("cruz");
  });

  it("full-page/narrow mode: filter select keeps its full option label unchanged", () => {
    render(<RequestListToolbar {...baseProps()} />);

    expect(screen.getByRole("option", { name: "All active statuses" })).toBeInTheDocument();
    expect(screen.queryByText("Filter")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Clear status filter" })).not.toBeInTheDocument();
  });

  it("full-page/narrow mode: search keeps its full placeholder unchanged", () => {
    render(<RequestListToolbar {...baseProps()} />);
    expect(screen.getByPlaceholderText("Search requests…")).toBeInTheDocument();
  });
});
