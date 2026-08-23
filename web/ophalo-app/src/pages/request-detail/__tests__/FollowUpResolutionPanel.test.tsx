import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { FollowUpResolutionPanel } from "../FollowUpResolutionPanel";
import { mockRequestDetails } from "../../../mocks/fixtures";

const mockResolveFollowUp = vi.fn();

vi.mock("../../../lib/apiClient", () => ({
  api: {
    resolveFollowUp: (...args: unknown[]) => mockResolveFollowUp(...args),
  },
  ApiError: class ApiError extends Error {
    status: number;
    constructor(status: number, _code: string | undefined, message: string) {
      super(message);
      this.status = status;
    }
  },
}));

beforeEach(() => {
  vi.clearAllMocks();
  mockResolveFollowUp.mockResolvedValue(mockRequestDetails["mock-req-001"]);
});

function renderPanel() {
  const onClose = vi.fn();
  const onDetailUpdated = vi.fn();
  render(
    <FollowUpResolutionPanel
      requestId="mock-req-001"
      detail={mockRequestDetails["mock-req-001"]}
      onDetailUpdated={onDetailUpdated}
      onClose={onClose}
    />,
  );
  return { onClose, onDetailUpdated };
}

function clickBackdrop(container: HTMLElement) {
  const backdrop = container.querySelector('[aria-hidden="true"]');
  if (!backdrop) throw new Error("backdrop not found");
  fireEvent.click(backdrop);
}

describe("FollowUpResolutionPanel — dirty-close contract", () => {
  it("closes immediately when no outcome or note is entered (clean)", async () => {
    const user = userEvent.setup();
    const { onClose } = renderPanel();

    await user.click(screen.getByRole("button", { name: "Close" }));

    expect(onClose).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument();
  });

  it("opens the discard confirmation instead of closing, via Escape, backdrop, and Close, once an outcome is selected", async () => {
    const user = userEvent.setup();
    const { onClose } = renderPanel();

    await user.click(screen.getByRole("button", { name: /Keep active/ }));

    await user.click(screen.getByRole("button", { name: "Close" }));
    expect(screen.getByRole("alertdialog")).toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();
    await user.click(screen.getByRole("button", { name: "Keep editing" }));

    await user.keyboard("{Escape}");
    expect(screen.getByRole("alertdialog")).toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();
    await user.click(screen.getByRole("button", { name: "Keep editing" }));

    clickBackdrop(document.body);
    expect(screen.getByRole("alertdialog")).toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();
  });

  it("Keep editing restores focus to the trigger and preserves the selected outcome", async () => {
    const user = userEvent.setup();
    renderPanel();

    await user.click(screen.getByRole("button", { name: /Mark complete/ }));
    const closeButton = screen.getByRole("button", { name: "Close" });
    await user.click(closeButton);

    expect(screen.getByRole("button", { name: "Keep editing" })).toHaveFocus();
    await user.click(screen.getByRole("button", { name: "Keep editing" }));

    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument();
    // The outcome selection is preserved — the completion-reason follow-up question renders.
    expect(screen.getByText(/Why is this complete/)).toBeInTheDocument();
    expect(closeButton).toHaveFocus();
  });

  it("Discard closes the sheet", async () => {
    const user = userEvent.setup();
    const { onClose } = renderPanel();

    await user.click(screen.getByRole("button", { name: /Mark complete/ }));
    await user.click(screen.getByRole("button", { name: "Close" }));
    await user.click(screen.getByRole("button", { name: "Discard" }));

    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it("traps Tab between the two confirm buttons and Escape closes only the confirmation", async () => {
    const user = userEvent.setup();
    const { onClose } = renderPanel();

    await user.click(screen.getByRole("button", { name: /Mark complete/ }));
    await user.click(screen.getByRole("button", { name: "Close" }));

    expect(screen.getByRole("button", { name: "Keep editing" })).toHaveFocus();
    await user.tab();
    expect(screen.getByRole("button", { name: "Discard" })).toHaveFocus();
    await user.tab();
    expect(screen.getByRole("button", { name: "Keep editing" })).toHaveFocus();

    await user.keyboard("{Escape}");
    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();
    expect(screen.getByText(/Why is this complete/)).toBeInTheDocument();
  });
});
