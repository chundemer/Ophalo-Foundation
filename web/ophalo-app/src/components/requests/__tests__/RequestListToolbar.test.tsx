import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { RequestListToolbar } from "../RequestListToolbar";
import { getTabsForRole, getSecondaryViewsForRole, getOfficeReviewMembersForRole } from "../../../pages/requestsWorkspace";

// Request Queue header consolidation (locked 2026-08-24): the native status <select> is gone —
// Row 2 is search plus one custom Views popover that bundles saved views (Watching + Owner/Admin
// Office Review destinations), status filtering (draft-select + Reset/Apply), and History Log
// entry. See session-log "Request Queue header consolidation".

function baseProps(overrides: Partial<React.ComponentProps<typeof RequestListToolbar>> = {}) {
  const role = "owner" as const;
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
    activeTab: getTabsForRole(role)[0],
    viewCounts: null,
    onSelectTab: vi.fn(),
    secondaryViews: getSecondaryViewsForRole(role),
    officeReviewMembers: getOfficeReviewMembersForRole(role),
    officeReview: { status: "ready" as const, aggregate: 0, members: { readyToClose: 0, feedbackReview: 0, actualWorkReview: 0 } },
    isOwnerOrAdmin: true,
    onEnterHistory: vi.fn(),
    ...overrides,
  };
}

describe("RequestListToolbar search row", () => {
  it("keeps its accessible label and value contract in pane mode", () => {
    render(<RequestListToolbar {...baseProps({ draftQ: "cruz" })} paneMode />);
    expect(screen.getByRole("textbox", { name: "Search requests" })).toHaveValue("cruz");
  });

  it("keeps its full placeholder unchanged in full-page/narrow mode", () => {
    render(<RequestListToolbar {...baseProps()} />);
    expect(screen.getByPlaceholderText("Search requests…")).toBeInTheDocument();
  });

  it("does not render a native combobox anywhere", () => {
    render(<RequestListToolbar {...baseProps()} />);
    expect(screen.queryByRole("combobox")).not.toBeInTheDocument();
  });

  it("hides the Views trigger in history mode", () => {
    render(<RequestListToolbar {...baseProps({ historyMode: true, presentAsHistory: true })} />);
    expect(screen.queryByRole("button", { name: /^Views/ })).not.toBeInTheDocument();
  });
});

describe("RequestListToolbar Views popover", () => {
  it("shows the plain 'Views' label and no clear button when no status is applied", () => {
    render(<RequestListToolbar {...baseProps()} />);
    expect(screen.getByRole("button", { name: "Views" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Clear status filter" })).not.toBeInTheDocument();
  });

  it("indicates an active status filter on the trigger and offers a one-action reset", async () => {
    const onStatusFilterChange = vi.fn();
    render(<RequestListToolbar {...baseProps({ statusFilter: "pending_customer", onStatusFilterChange })} />);

    expect(screen.getByRole("button", { name: "Views · 1" })).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Clear status filter" }));
    expect(onStatusFilterChange).toHaveBeenCalledWith("");
  });

  it("lists saved views and Office Review destinations, and selecting one closes the popover", async () => {
    const onSelectTab = vi.fn();
    render(
      <RequestListToolbar
        {...baseProps({
          onSelectTab,
          officeReview: { status: "ready", aggregate: 1, members: { readyToClose: 1, feedbackReview: 0, actualWorkReview: 0 } },
        })}
      />,
    );

    await userEvent.click(screen.getByRole("button", { name: "Views" }));
    expect(screen.getByText("Watching")).toBeInTheDocument();
    const readyToClose = screen.getByRole("button", { name: /Ready to Close/ });
    await userEvent.click(readyToClose);
    expect(onSelectTab).toHaveBeenCalledWith(expect.objectContaining({ id: "ready_to_close" }));
    expect(screen.queryByRole("group", { name: "Views" })).not.toBeInTheDocument();
  });

  it("omits Office Review destinations for Operator", async () => {
    render(<RequestListToolbar {...baseProps({ isOwnerOrAdmin: false, officeReviewMembers: [] })} />);
    await userEvent.click(screen.getByRole("button", { name: "Views" }));
    expect(screen.queryByText(/Ready to Close/)).not.toBeInTheDocument();
    expect(screen.queryByText("History Log")).not.toBeInTheDocument();
  });

  it("enters history via History Log and closes the popover", async () => {
    const onEnterHistory = vi.fn();
    render(<RequestListToolbar {...baseProps({ onEnterHistory })} />);
    await userEvent.click(screen.getByRole("button", { name: "Views" }));
    await userEvent.click(screen.getByRole("button", { name: "History Log" }));
    expect(onEnterHistory).toHaveBeenCalled();
    expect(screen.queryByRole("group", { name: "Views" })).not.toBeInTheDocument();
  });

  it("filters by status through draft-select + Apply, not immediately on click", async () => {
    const onStatusFilterChange = vi.fn();
    render(<RequestListToolbar {...baseProps({ onStatusFilterChange })} />);
    await userEvent.click(screen.getByRole("button", { name: "Views" }));

    await userEvent.click(screen.getByRole("radio", { name: "Waiting on Customer" }));
    expect(onStatusFilterChange).not.toHaveBeenCalled();

    await userEvent.click(screen.getByRole("button", { name: "Apply" }));
    expect(onStatusFilterChange).toHaveBeenCalledWith("pending_customer");
    expect(screen.queryByRole("group", { name: "Views" })).not.toBeInTheDocument();
  });

  it("Reset filters commits the clear immediately and closes the popover", async () => {
    const onStatusFilterChange = vi.fn();
    render(<RequestListToolbar {...baseProps({ statusFilter: "pending_customer", onStatusFilterChange })} />);
    await userEvent.click(screen.getByRole("button", { name: "Views · 1" }));

    expect(screen.getByRole("radio", { name: "Waiting on Customer" })).toHaveAttribute("aria-checked", "true");
    await userEvent.click(screen.getByRole("button", { name: "Reset filters" }));

    expect(onStatusFilterChange).toHaveBeenCalledWith("");
    expect(screen.queryByRole("group", { name: "Views" })).not.toBeInTheDocument();
  });
});
