import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { LogContactModal } from "../../RequestDetail";
import { mockRequestDetails } from "../../../mocks/fixtures";

const mockLogExternalContact = vi.fn();

vi.mock("../../../lib/apiClient", () => ({
  api: {
    logExternalContact: (...args: unknown[]) => mockLogExternalContact(...args),
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
  mockLogExternalContact.mockResolvedValue(mockRequestDetails["mock-req-001"]);
});

function renderModal() {
  const onClose = vi.fn();
  const onDetailUpdated = vi.fn();
  render(
    <LogContactModal
      requestId="mock-req-001"
      detail={mockRequestDetails["mock-req-001"]}
      initialDirection="outbound"
      initialChannel="phone"
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

describe("LogContactModal — dirty-close contract", () => {
  it("closes immediately when the form is untouched (clean)", async () => {
    const user = userEvent.setup();
    const { onClose } = renderModal();

    await user.click(screen.getByRole("button", { name: "Close" }));

    expect(onClose).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument();
  });

  it("opens the discard confirmation instead of closing, via Escape, backdrop, Close, and Cancel, once dirty", async () => {
    const user = userEvent.setup();
    const { onClose } = renderModal();

    await user.type(screen.getByPlaceholderText(/Brief notes about this contact/), "Spoke with customer about timing");

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

    await user.type(screen.getByPlaceholderText(/Brief notes about this contact/), "Spoke with customer about timing");
    const closeButton = screen.getByRole("button", { name: "Close" });
    await user.click(closeButton);

    expect(screen.getByRole("button", { name: "Keep editing" })).toHaveFocus();
    await user.click(screen.getByRole("button", { name: "Keep editing" }));

    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument();
    expect(screen.getByPlaceholderText(/Brief notes about this contact/)).toHaveValue("Spoke with customer about timing");
    expect(closeButton).toHaveFocus();
  });

  it("Discard closes the sheet", async () => {
    const user = userEvent.setup();
    const { onClose } = renderModal();

    await user.type(screen.getByPlaceholderText(/Brief notes about this contact/), "Spoke with customer");
    await user.click(screen.getByRole("button", { name: "Close" }));
    await user.click(screen.getByRole("button", { name: "Discard" }));

    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it("traps Tab between the two confirm buttons and Escape closes only the confirmation", async () => {
    const user = userEvent.setup();
    const { onClose } = renderModal();

    await user.type(screen.getByPlaceholderText(/Brief notes about this contact/), "Spoke with customer");
    await user.click(screen.getByRole("button", { name: "Close" }));

    expect(screen.getByRole("button", { name: "Keep editing" })).toHaveFocus();
    await user.tab();
    expect(screen.getByRole("button", { name: "Discard" })).toHaveFocus();
    await user.tab();
    expect(screen.getByRole("button", { name: "Keep editing" })).toHaveFocus();

    await user.keyboard("{Escape}");
    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();
    expect(screen.getByPlaceholderText(/Brief notes about this contact/)).toHaveValue("Spoke with customer");
  });

  it("routes ExternalContactForm's own dirty state (a field local to the child form) through onDirtyChange to enable the guard", async () => {
    // The default outbound/phone selection already shows the "Requires business follow-up"
    // checkbox (outcome defaults to a follow-up-eligible value). Toggling only that checkbox —
    // no typed text, no direction/channel change — is state that lives entirely inside
    // ExternalContactForm, invisible to LogContactModal except via onDirtyChange. If the guard
    // only fires when it does, onDirtyChange is what's driving it, not some LogContactModal-local
    // guess.
    const user = userEvent.setup();
    const { onClose } = renderModal();

    await user.click(screen.getByRole("button", { name: "Close" }));
    expect(onClose).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument();
    onClose.mockClear();

    await user.click(screen.getByRole("checkbox", { name: /Requires business follow-up/ }));
    await user.click(screen.getByRole("button", { name: "Close" }));

    expect(screen.getByRole("alertdialog")).toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();
  });
});
