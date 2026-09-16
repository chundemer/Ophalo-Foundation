import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { LogContactModal } from "../../RequestDetail";
import { mockRequestDetails } from "../../../mocks/fixtures";

const mockLogExternalContact = vi.fn();
const mockRecordShareIntent = vi.fn();

vi.mock("../../../lib/apiClient", () => ({
  api: {
    logExternalContact: (...args: unknown[]) => mockLogExternalContact(...args),
    recordShareIntent: (...args: unknown[]) => mockRecordShareIntent(...args),
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
  mockRecordShareIntent.mockResolvedValue(undefined);
});

function renderModal(overrides: { detail?: (typeof mockRequestDetails)["mock-req-001"]; initialChannel?: string } = {}) {
  const onClose = vi.fn();
  const onDetailUpdated = vi.fn();
  const onShareIntentRecorded = vi.fn();
  render(
    <LogContactModal
      requestId="mock-req-001"
      detail={overrides.detail ?? mockRequestDetails["mock-req-001"]}
      initialDirection="outbound"
      initialChannel={overrides.initialChannel ?? "phone"}
      onDetailUpdated={onDetailUpdated}
      onClose={onClose}
      onShareIntentRecorded={onShareIntentRecorded}
    />,
  );
  return { onClose, onDetailUpdated, onShareIntentRecorded };
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

describe("LogContactModal — GAP-048 tracker-email share confirmation", () => {
  it("requires an explicit post-launch confirmation before recording an email share event", async () => {
    const user = userEvent.setup();
    const { onShareIntentRecorded } = renderModal({
      detail: mockRequestDetails["mock-req-002"],
      initialChannel: "email",
    });

    await user.click(screen.getByRole("link", { name: "Open email draft with request link" }));
    expect(mockRecordShareIntent).not.toHaveBeenCalled();

    await user.click(screen.getByRole("button", { name: "I sent it — confirm" }));

    expect(mockRecordShareIntent).toHaveBeenCalledWith("mock-req-001", "email");
    expect(onShareIntentRecorded).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole("button", { name: "I sent it — confirm" })).not.toBeInTheDocument();
  });

  it("dismissing the post-launch confirmation without confirming creates no share event", async () => {
    const user = userEvent.setup();
    const { onShareIntentRecorded } = renderModal({
      detail: mockRequestDetails["mock-req-002"],
      initialChannel: "email",
    });

    await user.click(screen.getByRole("link", { name: "Open email draft with request link" }));
    expect(screen.getByRole("button", { name: "I sent it — confirm" })).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Not sent" }));

    expect(mockRecordShareIntent).not.toHaveBeenCalled();
    expect(onShareIntentRecorded).not.toHaveBeenCalled();
    expect(screen.queryByRole("button", { name: "I sent it — confirm" })).not.toBeInTheDocument();
  });

  it("keeps the confirmation retryable and leaves no share event on a transport failure", async () => {
    const user = userEvent.setup();
    mockRecordShareIntent.mockRejectedValueOnce(new Error("network"));
    const { onShareIntentRecorded } = renderModal({
      detail: mockRequestDetails["mock-req-002"],
      initialChannel: "email",
    });

    await user.click(screen.getByRole("link", { name: "Open email draft with request link" }));
    await user.click(screen.getByRole("button", { name: "I sent it — confirm" }));

    expect(await screen.findByText("Could not record this share. Try again.")).toBeInTheDocument();
    expect(onShareIntentRecorded).not.toHaveBeenCalled();
    expect(screen.getByRole("button", { name: "I sent it — confirm" })).toBeInTheDocument();

    mockRecordShareIntent.mockResolvedValueOnce(undefined);
    await user.click(screen.getByRole("button", { name: "I sent it — confirm" }));
    expect(onShareIntentRecorded).toHaveBeenCalledTimes(1);
  });

  it("plain email (no share permission) has no tracker link and no confirmation step", async () => {
    const user = userEvent.setup();
    const plainDetail = {
      ...mockRequestDetails["mock-req-002"],
      availableActions: { ...mockRequestDetails["mock-req-002"].availableActions, canRecordShareIntent: false },
    };
    renderModal({ detail: plainDetail, initialChannel: "email" });

    const link = screen.getByRole("link", { name: "Open email draft" });
    expect(link.getAttribute("href")).not.toContain("mock-page-token-002");

    await user.click(link);

    expect(screen.queryByRole("button", { name: "I sent it — confirm" })).not.toBeInTheDocument();
    expect(mockRecordShareIntent).not.toHaveBeenCalled();
  });
});
