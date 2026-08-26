import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { PrimaryActionSlot } from "../PrimaryActionControl";
import { ApiError } from "../../../lib/apiClient";
import { mockRequestDetails } from "../../../mocks/fixtures";
import type { KeepRequestDetailResult } from "../../../lib/apiClient";

const mockPatchRequestStatus = vi.fn();

vi.mock("../../../lib/apiClient", async () => {
  const actual = await vi.importActual<typeof import("../../../lib/apiClient")>("../../../lib/apiClient");
  return {
    ...actual,
    api: {
      ...actual.api,
      patchRequestStatus: (...args: unknown[]) => mockPatchRequestStatus(...args),
    },
  };
});

function detailWithMarkWorkDone(): KeepRequestDetailResult {
  return {
    ...mockRequestDetails["mock-req-001"],
    version: "v1",
    availableActions: {
      ...mockRequestDetails["mock-req-001"].availableActions,
      primaryAction: {
        key: "mark_work_done",
        label: "Mark work done",
        target: "mutation",
        requiresConfirmation: false,
        confirmationCopy: null,
      },
    },
  };
}

function renderSlot(detail: KeepRequestDetailResult, onDetailUpdated = vi.fn()) {
  render(
    <PrimaryActionSlot
      requestId="req-1"
      detail={detail}
      onDetailUpdated={onDetailUpdated}
      onOpenClearAttention={vi.fn()}
      onRecordFollowUp={vi.fn()}
      onContactLaunched={vi.fn()}
      onActivateCustomerUpdateComposer={vi.fn()}
    />,
  );
  return { onDetailUpdated };
}

async function clickThenConfirm(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole("button", { name: "Mark work done" }));
  await user.click(screen.getByRole("button", { name: "Confirm" }));
}

beforeEach(() => {
  mockPatchRequestStatus.mockReset();
});

describe("PrimaryActionControl — connection recovery", () => {
  it("submits successfully and calls onDetailUpdated", async () => {
    const user = userEvent.setup();
    const detail = detailWithMarkWorkDone();
    const updated = { ...detail, version: "v2" };
    mockPatchRequestStatus.mockResolvedValueOnce(updated);
    const { onDetailUpdated } = renderSlot(detail);

    await clickThenConfirm(user);

    await waitFor(() => expect(onDetailUpdated).toHaveBeenCalledWith(updated));
    expect(mockPatchRequestStatus).toHaveBeenCalledWith("req-1", { status: "resolved" }, "v1");
  });

  it("keeps the existing 409 conflict behavior and disables the control", async () => {
    const user = userEvent.setup();
    const detail = detailWithMarkWorkDone();
    mockPatchRequestStatus.mockRejectedValueOnce(new ApiError(409, "conflict", "Conflict"));
    renderSlot(detail);

    await clickThenConfirm(user);

    expect(await screen.findByText("This request was updated. Refresh to see the latest state.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Mark work done" })).toBeDisabled();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("keeps generic error text for a non-409 ApiError (server/validation failure)", async () => {
    const user = userEvent.setup();
    const detail = detailWithMarkWorkDone();
    mockPatchRequestStatus.mockRejectedValueOnce(new ApiError(400, "invalid", "Invalid"));
    renderSlot(detail);

    await clickThenConfirm(user);

    expect(await screen.findByText("Could not mark work done. Try again.")).toBeInTheDocument();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Mark work done" })).not.toBeDisabled();
  });

  it("shows the connection-failure banner instead of the generic error on a non-ApiError transport failure", async () => {
    const user = userEvent.setup();
    const detail = detailWithMarkWorkDone();
    mockPatchRequestStatus.mockRejectedValueOnce(new TypeError("Failed to fetch"));
    renderSlot(detail);

    await clickThenConfirm(user);

    expect(await screen.findByRole("alert")).toHaveTextContent("Couldn't mark work done.");
    expect(screen.getByRole("button", { name: "Mark work done" })).not.toBeDisabled();
  });

  it("Retry replays the exact original request and clears the banner on success", async () => {
    const user = userEvent.setup();
    const detail = detailWithMarkWorkDone();
    const updated = { ...detail, version: "v2" };
    mockPatchRequestStatus.mockRejectedValueOnce(new TypeError("Failed to fetch"));
    mockPatchRequestStatus.mockResolvedValueOnce(updated);
    const { onDetailUpdated } = renderSlot(detail);

    await clickThenConfirm(user);
    await screen.findByRole("alert");

    await user.click(screen.getByRole("button", { name: "Retry" }));

    await waitFor(() => expect(onDetailUpdated).toHaveBeenCalledWith(updated));
    expect(mockPatchRequestStatus).toHaveBeenCalledTimes(2);
    expect(mockPatchRequestStatus).toHaveBeenNthCalledWith(2, "req-1", { status: "resolved" }, "v1");
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("Retry replays the snapshot taken at the failed attempt, not a version the parent updated afterward", async () => {
    const user = userEvent.setup();
    const detail = detailWithMarkWorkDone();
    mockPatchRequestStatus.mockRejectedValueOnce(new TypeError("Failed to fetch"));
    mockPatchRequestStatus.mockResolvedValueOnce({ ...detail, version: "v3" });
    const { rerender } = render(
      <PrimaryActionSlot
        requestId="req-1"
        detail={detail}
        onDetailUpdated={vi.fn()}
        onOpenClearAttention={vi.fn()}
        onRecordFollowUp={vi.fn()}
        onContactLaunched={vi.fn()}
        onActivateCustomerUpdateComposer={vi.fn()}
      />,
    );

    await clickThenConfirm(user);
    await screen.findByRole("alert");

    // Parent re-renders with a newer server version before the operator presses Retry.
    const refreshedDetail = { ...detail, version: "v2" };
    rerender(
      <PrimaryActionSlot
        requestId="req-1"
        detail={refreshedDetail}
        onDetailUpdated={vi.fn()}
        onOpenClearAttention={vi.fn()}
        onRecordFollowUp={vi.fn()}
        onContactLaunched={vi.fn()}
        onActivateCustomerUpdateComposer={vi.fn()}
      />,
    );

    await user.click(screen.getByRole("button", { name: "Retry" }));

    await waitFor(() => expect(mockPatchRequestStatus).toHaveBeenCalledTimes(2));
    expect(mockPatchRequestStatus).toHaveBeenNthCalledWith(2, "req-1", { status: "resolved" }, "v1");
  });
});
