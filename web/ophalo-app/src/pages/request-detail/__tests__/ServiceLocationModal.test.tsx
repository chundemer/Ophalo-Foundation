import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ServiceLocationModal } from "../../RequestDetail";
import { mockRequestDetails } from "../../../mocks/fixtures";

const mockUpdateServiceLocation = vi.fn();

vi.mock("../../../lib/apiClient", () => ({
  api: {
    updateServiceLocation: (...args: unknown[]) => mockUpdateServiceLocation(...args),
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
  mockUpdateServiceLocation.mockResolvedValue(mockRequestDetails["mock-req-001"]);
});

function renderModal() {
  const onClose = vi.fn();
  const onDetailUpdated = vi.fn();
  render(
    <ServiceLocationModal
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

describe("ServiceLocationModal — dirty-close contract", () => {
  it("closes immediately when no field has changed (clean)", async () => {
    const user = userEvent.setup();
    const { onClose } = renderModal();

    await user.click(screen.getByRole("button", { name: "Cancel" }));

    expect(onClose).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument();
  });

  it("opens the discard confirmation instead of closing, via Escape, backdrop, Close, and Cancel, once dirty", async () => {
    const user = userEvent.setup();
    const { onClose } = renderModal();

    await user.type(screen.getByLabelText(/Address line 1/), "123 Main St");

    await user.click(screen.getByRole("button", { name: "Close" }));
    expect(screen.getByRole("alertdialog")).toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();
    await user.click(screen.getByRole("button", { name: "Keep editing" }));

    await user.click(screen.getByRole("button", { name: "Cancel" }));
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

  it("Keep editing restores focus to the trigger and preserves the entered draft", async () => {
    const user = userEvent.setup();
    renderModal();

    await user.type(screen.getByLabelText(/Address line 1/), "123 Main St");
    const cancelButton = screen.getByRole("button", { name: "Cancel" });
    await user.click(cancelButton);

    expect(screen.getByRole("button", { name: "Keep editing" })).toHaveFocus();
    await user.click(screen.getByRole("button", { name: "Keep editing" }));

    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument();
    expect(screen.getByLabelText(/Address line 1/)).toHaveValue("123 Main St");
    expect(cancelButton).toHaveFocus();
  });

  it("Discard closes the sheet", async () => {
    const user = userEvent.setup();
    const { onClose } = renderModal();

    await user.type(screen.getByLabelText(/Address line 1/), "123 Main St");
    await user.click(screen.getByRole("button", { name: "Cancel" }));
    await user.click(screen.getByRole("button", { name: "Discard" }));

    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it("traps Tab between the two confirm buttons and Escape closes only the confirmation", async () => {
    const user = userEvent.setup();
    const { onClose } = renderModal();

    await user.type(screen.getByLabelText(/Address line 1/), "123 Main St");
    await user.click(screen.getByRole("button", { name: "Cancel" }));

    expect(screen.getByRole("button", { name: "Keep editing" })).toHaveFocus();
    await user.tab();
    expect(screen.getByRole("button", { name: "Discard" })).toHaveFocus();
    await user.tab();
    expect(screen.getByRole("button", { name: "Keep editing" })).toHaveFocus();

    await user.keyboard("{Escape}");
    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();
    expect(screen.getByLabelText(/Address line 1/)).toHaveValue("123 Main St");
  });
});
