import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ClearAttentionSheet } from "../DetailPanels";
import { mockRequestDetails } from "../../../mocks/fixtures";

const mockAcknowledgeAttention = vi.fn();

vi.mock("../../../lib/apiClient", () => ({
  api: {
    acknowledgeAttention: (...args: unknown[]) => mockAcknowledgeAttention(...args),
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
  mockAcknowledgeAttention.mockResolvedValue(mockRequestDetails["mock-req-001"]);
});

function renderSheet() {
  const onClose = vi.fn();
  const onDetailUpdated = vi.fn();
  render(
    <ClearAttentionSheet
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

describe("ClearAttentionSheet — dirty-close contract", () => {
  it("closes immediately when the reason field is empty (clean)", async () => {
    const user = userEvent.setup();
    const { onClose } = renderSheet();

    await user.click(screen.getByRole("button", { name: "Close" }));

    expect(onClose).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument();
  });

  it("opens the discard confirmation instead of closing, via Escape, backdrop, and Close, once dirty", async () => {
    const user = userEvent.setup();
    const { container, onClose } = { ...renderSheet(), container: document.body };

    await user.type(screen.getByLabelText("Brief note before clearing"), "Reviewed, no action needed");

    // Close button
    await user.click(screen.getByRole("button", { name: "Close" }));
    expect(screen.getByRole("alertdialog")).toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();
    await user.click(screen.getByRole("button", { name: "Keep editing" }));

    // Escape
    await user.keyboard("{Escape}");
    expect(screen.getByRole("alertdialog")).toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();
    await user.click(screen.getByRole("button", { name: "Keep editing" }));

    // Backdrop click
    clickBackdrop(container);
    expect(screen.getByRole("alertdialog")).toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();
  });

  it("Keep editing restores focus to the trigger and preserves the entered draft", async () => {
    const user = userEvent.setup();
    renderSheet();

    const textarea = screen.getByLabelText("Brief note before clearing");
    await user.type(textarea, "Reviewed, no action needed");
    const closeButton = screen.getByRole("button", { name: "Close" });
    await user.click(closeButton);

    expect(screen.getByRole("button", { name: "Keep editing" })).toHaveFocus();
    await user.click(screen.getByRole("button", { name: "Keep editing" }));

    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument();
    expect(screen.getByLabelText("Brief note before clearing")).toHaveValue("Reviewed, no action needed");
    expect(closeButton).toHaveFocus();
  });

  it("Discard closes the sheet", async () => {
    const user = userEvent.setup();
    const { onClose } = renderSheet();

    await user.type(screen.getByLabelText("Brief note before clearing"), "Reviewed");
    await user.click(screen.getByRole("button", { name: "Close" }));
    await user.click(screen.getByRole("button", { name: "Discard" }));

    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it("traps Tab between the two confirm buttons and Escape closes only the confirmation", async () => {
    const user = userEvent.setup();
    const { onClose } = renderSheet();

    await user.type(screen.getByLabelText("Brief note before clearing"), "Reviewed");
    await user.click(screen.getByRole("button", { name: "Close" }));

    expect(screen.getByRole("button", { name: "Keep editing" })).toHaveFocus();
    await user.tab();
    expect(screen.getByRole("button", { name: "Discard" })).toHaveFocus();
    await user.tab();
    expect(screen.getByRole("button", { name: "Keep editing" })).toHaveFocus();

    await user.keyboard("{Escape}");
    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();
    expect(screen.getByLabelText("Brief note before clearing")).toHaveValue("Reviewed");
  });
});
