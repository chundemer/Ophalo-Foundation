import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ClassifyRequestCard } from "../ClassifyRequestCard";
import { ApiError } from "../../../lib/apiClient";
import { mockRequestDetails } from "../../../mocks/fixtures";
import type { KeepRequestDetailResult } from "../../../lib/apiClient";

const mockClassifyRequest = vi.fn();

vi.mock("../../../lib/apiClient", async () => {
  const actual = await vi.importActual<typeof import("../../../lib/apiClient")>("../../../lib/apiClient");
  return {
    ...actual,
    api: {
      ...actual.api,
      classifyRequest: (...args: unknown[]) => mockClassifyRequest(...args),
    },
  };
});

function baseDetail(canClassify = true): KeepRequestDetailResult {
  return {
    ...mockRequestDetails["mock-req-001"],
    version: "v1",
    availableActions: {
      ...mockRequestDetails["mock-req-001"].availableActions,
      canClassify,
    },
  };
}

function renderCard(
  detail: KeepRequestDetailResult,
  onDetailUpdated = vi.fn(),
  onRefreshDetail = vi.fn(),
) {
  render(
    <ClassifyRequestCard
      requestId="req-1"
      detail={detail}
      onDetailUpdated={onDetailUpdated}
      onRefreshDetail={onRefreshDetail}
    />,
  );
  return { onDetailUpdated, onRefreshDetail };
}

async function openMenuAndSelect(user: ReturnType<typeof userEvent.setup>, label: "Mark as spam" | "Mark as test") {
  await user.click(screen.getByRole("button", { name: "Admin actions" }));
  await user.click(within(screen.getByRole("menu")).getByRole("menuitem", { name: label }));
}

beforeEach(() => {
  mockClassifyRequest.mockReset();
});

describe("ClassifyRequestCard (admin-actions trigger)", () => {
  it("renders nothing when canClassify is false", () => {
    const { container } = render(
      <ClassifyRequestCard
        requestId="req-1"
        detail={baseDetail(false)}
        onDetailUpdated={vi.fn()}
        onRefreshDetail={vi.fn()}
      />,
    );
    expect(container).toBeEmptyDOMElement();
  });

  it("opens a menu with both classification options, not a permanent card", () => {
    renderCard(baseDetail());
    expect(screen.queryByRole("menu")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Admin actions" })).toBeInTheDocument();
  });

  it("shows explicit target/consequence copy for Mark as spam", async () => {
    const user = userEvent.setup();
    renderCard(baseDetail());

    await openMenuAndSelect(user, "Mark as spam");
    const dialog = screen.getByRole("dialog");
    expect(within(dialog).getByRole("heading", { name: "Mark this request as spam?" })).toBeInTheDocument();
    expect(
      within(dialog).getByText(
        "This removes the request from active queues and metrics, and disables further activity on the customer page. This is final and cannot be undone from here.",
      ),
    ).toBeInTheDocument();
  });

  it("shows explicit target/consequence copy for Mark as test", async () => {
    const user = userEvent.setup();
    renderCard(baseDetail());

    await openMenuAndSelect(user, "Mark as test");
    const dialog = screen.getByRole("dialog");
    expect(within(dialog).getByRole("heading", { name: "Mark this request as test?" })).toBeInTheDocument();
  });

  it("submits spam classification with the trimmed reason and current version on confirm", async () => {
    const user = userEvent.setup();
    const detail = baseDetail();
    const updated = { ...detail, version: "v2" };
    mockClassifyRequest.mockResolvedValueOnce(updated);
    const { onDetailUpdated } = renderCard(detail);

    await openMenuAndSelect(user, "Mark as spam");
    await user.type(screen.getByLabelText("Internal reason (optional)"), "  obvious junk  ");
    await user.click(within(screen.getByRole("dialog")).getByRole("button", { name: "Mark as spam" }));

    await waitFor(() =>
      expect(mockClassifyRequest).toHaveBeenCalledWith(
        "req-1",
        { targetStatus: "spam", reason: "obvious junk" },
        "v1",
      ),
    );
    await waitFor(() => expect(onDetailUpdated).toHaveBeenCalledWith(updated));
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("submits test classification with no reason as undefined", async () => {
    const user = userEvent.setup();
    const detail = baseDetail();
    mockClassifyRequest.mockResolvedValueOnce({ ...detail, version: "v2" });
    renderCard(detail);

    await openMenuAndSelect(user, "Mark as test");
    await user.click(within(screen.getByRole("dialog")).getByRole("button", { name: "Mark as test" }));

    await waitFor(() =>
      expect(mockClassifyRequest).toHaveBeenCalledWith("req-1", { targetStatus: "test", reason: undefined }, "v1"),
    );
  });

  it("caps the reason at 500 characters and shows a live remaining-character count", async () => {
    const user = userEvent.setup();
    renderCard(baseDetail());

    await openMenuAndSelect(user, "Mark as spam");
    const textarea = screen.getByLabelText("Internal reason (optional)") as HTMLTextAreaElement;
    expect(textarea).toHaveAttribute("maxLength", "500");
    expect(screen.getByText("0/500")).toBeInTheDocument();

    await user.type(textarea, "hello");
    expect(screen.getByText("5/500")).toBeInTheDocument();
  });

  it("Cancel closes the dialog without submitting", async () => {
    const user = userEvent.setup();
    renderCard(baseDetail());

    await openMenuAndSelect(user, "Mark as spam");
    await user.click(within(screen.getByRole("dialog")).getByRole("button", { name: "Cancel" }));

    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(mockClassifyRequest).not.toHaveBeenCalled();
  });

  it("Escape or an outside click closes the menu without opening a dialog", async () => {
    const user = userEvent.setup();
    renderCard(baseDetail());

    await user.click(screen.getByRole("button", { name: "Admin actions" }));
    expect(screen.getByRole("menu")).toBeInTheDocument();

    await user.keyboard("{Escape}");
    expect(screen.queryByRole("menu")).not.toBeInTheDocument();
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("on 409, preserves the entered reason and refreshes detail, keeping the dialog open", async () => {
    const user = userEvent.setup();
    const detail = baseDetail();
    mockClassifyRequest.mockRejectedValueOnce(new ApiError(409, "conflict", "Conflict"));
    const { onRefreshDetail } = renderCard(detail);

    await openMenuAndSelect(user, "Mark as spam");
    await user.type(screen.getByLabelText("Internal reason (optional)"), "keep me");
    await user.click(within(screen.getByRole("dialog")).getByRole("button", { name: "Mark as spam" }));

    expect(
      await screen.findByText("This request was updated elsewhere. Reloading the latest state — your reason is kept."),
    ).toBeInTheDocument();
    expect(onRefreshDetail).toHaveBeenCalled();
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(screen.getByLabelText("Internal reason (optional)")).toHaveValue("keep me");
  });

  it("on 403, refreshes detail; once the refreshed detail no longer authorizes it, the action disappears", async () => {
    const user = userEvent.setup();
    const detail = baseDetail();
    mockClassifyRequest.mockRejectedValueOnce(new ApiError(403, "auth.forbidden", "Forbidden"));
    const onRefreshDetail = vi.fn();
    render(
      <ClassifyRequestCard requestId="req-1" detail={detail} onDetailUpdated={vi.fn()} onRefreshDetail={onRefreshDetail} />,
    );

    await openMenuAndSelect(user, "Mark as spam");
    await user.click(within(screen.getByRole("dialog")).getByRole("button", { name: "Mark as spam" }));

    expect(await screen.findByText("You no longer have permission to do this. Reloading the latest state.")).toBeInTheDocument();
    expect(onRefreshDetail).toHaveBeenCalled();

    // Parent refetches and finds the caller is no longer eligible — the whole trigger, menu, and
    // dialog disappear rather than leaving a stale action visible.
    const { container } = render(
      <ClassifyRequestCard requestId="req-1" detail={baseDetail(false)} onDetailUpdated={vi.fn()} onRefreshDetail={onRefreshDetail} />,
    );
    expect(container).toBeEmptyDOMElement();
  });

  it("keeps an ordinary (non-409/403) failure recoverable via the still-open dialog", async () => {
    const user = userEvent.setup();
    const detail = baseDetail();
    mockClassifyRequest.mockRejectedValueOnce(new ApiError(422, "KeepRequest.InvalidClassification", "Invalid"));
    mockClassifyRequest.mockResolvedValueOnce({ ...detail, version: "v2" });
    const { onDetailUpdated } = renderCard(detail);

    await openMenuAndSelect(user, "Mark as spam");
    await user.click(within(screen.getByRole("dialog")).getByRole("button", { name: "Mark as spam" }));

    expect(await screen.findByText("Could not mark as spam. Try again.")).toBeInTheDocument();
    expect(screen.getByRole("dialog")).toBeInTheDocument();

    await user.click(within(screen.getByRole("dialog")).getByRole("button", { name: "Mark as spam" }));
    await waitFor(() => expect(onDetailUpdated).toHaveBeenCalled());
  });
});
