import { describe, it, expect, vi, afterEach } from "vitest";
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

// GAP-060: the Views menu rendered `position: absolute` inside the queue-panel header, so an
// `overflow-hidden` / `h-dvh` ancestor clipped its option labels outside the visible bounds.
// It now renders `position: fixed` (still a DOM child of the trigger's container) with a
// viewport-bounded placement. jsdom has no layout engine, so geometry is asserted on the
// inline style the menu receives from a stubbed trigger rect; the placement math itself is
// covered in viewsPopoverPosition.test.ts.
describe("RequestListToolbar Views menu clipping (GAP-060)", () => {
  const MARGIN = 8;

  function stubTriggerRect(rect: Partial<DOMRect>) {
    const full: DOMRect = {
      top: 120, bottom: 148, left: 900, right: 960, width: 60, height: 28,
      x: 900, y: 120, toJSON: () => ({}), ...rect,
    } as DOMRect;
    const spy = vi
      .spyOn(HTMLElement.prototype, "getBoundingClientRect")
      .mockReturnValue(full);
    return spy;
  }

  function setViewport(width: number, height: number) {
    Object.defineProperty(window, "innerWidth", { configurable: true, value: width });
    Object.defineProperty(window, "innerHeight", { configurable: true, value: height });
  }

  afterEach(() => {
    vi.restoreAllMocks();
    setViewport(1024, 768);
  });

  function menuStyle() {
    const menu = screen.getByRole("group", { name: "Views" }) as HTMLElement;
    return {
      menu,
      left: parseFloat(menu.style.left),
      top: parseFloat(menu.style.top),
      width: parseFloat(menu.style.width),
      maxHeight: menu.style.maxHeight ? parseFloat(menu.style.maxHeight) : 0,
      position: menu.style.position,
    };
  }

  it("opens with every option label and the Apply control present and within desktop viewport bounds", async () => {
    setViewport(1280, 800);
    stubTriggerRect({ top: 120, bottom: 148, left: 1180, right: 1240 });
    render(
      <RequestListToolbar
        {...baseProps({
          officeReview: { status: "ready", aggregate: 1, members: { readyToClose: 1, feedbackReview: 0, actualWorkReview: 0 } },
        })}
      />,
    );

    await userEvent.click(screen.getByRole("button", { name: "Views" }));

    for (const label of ["Watching", "Ready to Close", "History Log"]) {
      expect(screen.getByText(label)).toBeInTheDocument();
    }
    expect(screen.getByRole("button", { name: "Apply" })).toBeInTheDocument();

    const s = menuStyle();
    expect(s.menu).toHaveClass("fixed");
    expect(s.left).toBeGreaterThanOrEqual(MARGIN);
    expect(s.left + s.width).toBeLessThanOrEqual(1280 - MARGIN);
    expect(s.top + s.maxHeight).toBeLessThanOrEqual(800 - MARGIN);
  });

  it("stays within a narrow viewport / high-zoom equivalent (360x740)", async () => {
    setViewport(360, 740);
    stubTriggerRect({ top: 150, bottom: 178, left: 300, right: 344 });
    render(<RequestListToolbar {...baseProps()} paneMode />);

    await userEvent.click(screen.getByRole("button", { name: "Views" }));

    const s = menuStyle();
    expect(s.left).toBeGreaterThanOrEqual(MARGIN);
    expect(s.width).toBeLessThanOrEqual(360 - MARGIN * 2);
    expect(s.left + s.width).toBeLessThanOrEqual(360 - MARGIN);
  });

  it("introduces no horizontal document overflow while the menu is open", async () => {
    setViewport(360, 740);
    stubTriggerRect({ top: 150, bottom: 178, left: 320, right: 344 });
    const { container } = render(
      <div style={{ width: 360, overflow: "hidden" }}>
        <RequestListToolbar {...baseProps()} paneMode />
      </div>,
    );

    await userEvent.click(screen.getByRole("button", { name: "Views" }));

    const s = menuStyle();
    expect(s.left + s.width).toBeLessThanOrEqual(360 - MARGIN);
    expect(container.querySelector('[role="group"]')).not.toBeNull();
  });

  it("supports keyboard open, option selection, Apply, Escape and focus restoration", async () => {
    const onSelectTab = vi.fn();
    const onStatusFilterChange = vi.fn();
    stubTriggerRect({});
    render(<RequestListToolbar {...baseProps({ onSelectTab, onStatusFilterChange })} />);

    const trigger = screen.getByRole("button", { name: "Views" });
    trigger.focus();
    await userEvent.keyboard("{Enter}");
    expect(screen.getByRole("group", { name: "Views" })).toBeInTheDocument();

    await userEvent.keyboard("{Escape}");
    expect(screen.queryByRole("group", { name: "Views" })).not.toBeInTheDocument();
    expect(trigger).toHaveFocus();

    await userEvent.keyboard("{Enter}");
    await userEvent.click(screen.getByRole("radio", { name: "Waiting on Customer" }));
    await userEvent.click(screen.getByRole("button", { name: "Apply" }));
    expect(onStatusFilterChange).toHaveBeenCalledWith("pending_customer");
    expect(trigger).toHaveFocus();
  });
});
