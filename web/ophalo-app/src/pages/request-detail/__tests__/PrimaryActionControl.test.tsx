import { useState } from "react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { PrimaryActionSlot, MarkWorkDoneSecondarySlot } from "../PrimaryActionControl";
import { ApiError } from "../../../lib/apiClient";
import { mockRequestDetails } from "../../../mocks/fixtures";
import type { KeepRequestDetailResult } from "../../../lib/apiClient";
import { LiveAnnouncerRegion } from "../../../components/a11y/LiveAnnouncerRegion";

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

describe("MarkWorkDoneSecondarySlot — quiet contextual lifecycle action (RD-058B-2)", () => {
  function detailWithSecondary(): KeepRequestDetailResult {
    const base = detailWithMarkWorkDone();
    return {
      ...base,
      availableActions: {
        ...base.availableActions,
        primaryAction: null,
        markWorkDoneSecondary: { label: "Mark work done", target: "mutation", consequence: "attention_remains" },
      },
    };
  }

  it("renders nothing without server authorization", () => {
    const { container } = render(
      <MarkWorkDoneSecondarySlot requestId="req-1" detail={detailWithMarkWorkDone()} onDetailUpdated={vi.fn()} />,
    );
    expect(container).toBeEmptyDOMElement();
  });

  it("is a quiet button whose visible text states the consequence and whose confirm carries the full advisory", async () => {
    const user = userEvent.setup();
    render(<MarkWorkDoneSecondarySlot requestId="req-1" detail={detailWithSecondary()} onDetailUpdated={vi.fn()} />);

    const trigger = screen.getByRole("button", { name: "Mark work done, attention remains" });
    expect(trigger.className).not.toContain("border");

    await user.click(trigger);
    expect(
      screen.getByText(
        "This marks the request as Work completed. It does not notify the customer, does not complete internal financial review, and leaves any active attention or open Actual Work draft unresolved.",
      ),
    ).toBeInTheDocument();
  });

  it("patches the request to resolved on confirm", async () => {
    const user = userEvent.setup();
    const onDetailUpdated = vi.fn();
    const detail = detailWithSecondary();
    mockPatchRequestStatus.mockResolvedValueOnce({ ...detail, version: "v2" });
    render(<MarkWorkDoneSecondarySlot requestId="req-1" detail={detail} onDetailUpdated={onDetailUpdated} />);

    await user.click(screen.getByRole("button", { name: "Mark work done, attention remains" }));
    await user.click(screen.getByRole("button", { name: "Confirm" }));

    await waitFor(() => expect(mockPatchRequestStatus).toHaveBeenCalledWith("req-1", { status: "resolved" }, "v1"));
    await waitFor(() => expect(onDetailUpdated).toHaveBeenCalled());
  });
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

  // Slice 5c-2A: `PrimaryActionSlot` returns null once `detail.availableActions.primaryAction`
  // is gone — a retry success that removes the primary action unmounts `PrimaryActionControl` in
  // the same commit as `onDetailUpdated`. This host actually re-renders with that updated
  // `detail` (unlike `renderSlot`'s static detail) to prove the announcement survives via the
  // root-mounted `LiveAnnouncerRegion`, not local component state.
  function RetrySuccessHost() {
    const [detail, setDetail] = useState(detailWithMarkWorkDone());
    return (
      <>
        <LiveAnnouncerRegion />
        <PrimaryActionSlot
          requestId="req-1"
          detail={detail}
          onDetailUpdated={setDetail}
          onOpenClearAttention={vi.fn()}
          onRecordFollowUp={vi.fn()}
          onContactLaunched={vi.fn()}
          onActivateCustomerUpdateComposer={vi.fn()}
        />
      </>
    );
  }

  it("announces 'Retry succeeded.' via the persistent live region after a retry succeeds and the primary action (and control) disappears", async () => {
    const user = userEvent.setup();
    const detail = detailWithMarkWorkDone();
    mockPatchRequestStatus.mockRejectedValueOnce(new TypeError("Failed to fetch"));
    mockPatchRequestStatus.mockResolvedValueOnce({
      ...detail,
      version: "v2",
      availableActions: { ...detail.availableActions, primaryAction: null },
    });
    render(<RetrySuccessHost />);

    await clickThenConfirm(user);
    await screen.findByRole("alert");

    await user.click(screen.getByRole("button", { name: "Retry" }));

    await waitFor(() => expect(mockPatchRequestStatus).toHaveBeenCalledTimes(2));
    // The control (and its local banner) is gone — the only way this text can be in the DOM is
    // via the root-mounted region, which outlived it.
    await waitFor(() => expect(screen.queryByRole("button", { name: "Mark work done" })).not.toBeInTheDocument());
    await waitFor(() => expect(screen.getByRole("status")).toHaveTextContent("Retry succeeded."));
  });

  it("does not announce 'Retry succeeded.' for an ordinary first-attempt success", async () => {
    const user = userEvent.setup();
    const detail = detailWithMarkWorkDone();
    mockPatchRequestStatus.mockResolvedValueOnce({
      ...detail,
      version: "v2",
      availableActions: { ...detail.availableActions, primaryAction: null },
    });
    render(<RetrySuccessHost />);

    await clickThenConfirm(user);

    await waitFor(() => expect(mockPatchRequestStatus).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(screen.queryByRole("button", { name: "Mark work done" })).not.toBeInTheDocument());
    expect(screen.getByRole("status")).toHaveTextContent("");
  });
});
