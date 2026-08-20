import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ActualWorkComposer } from "../ActualWorkComposer";
import { ApiError } from "../../../lib/apiClient";
import type { ActualWorkHistoryResult } from "../../../lib/apiClient";

const mockGetFieldScopeSearch = vi.fn();
const mockAddActualWorkLine = vi.fn();
const mockUpdateActualWorkLine = vi.fn();
const mockRemoveActualWorkLine = vi.fn();
const mockSubmitActualWork = vi.fn();
const mockDiscardActualWork = vi.fn();

vi.mock("../../../lib/apiClient", async () => {
  const actual = await vi.importActual<typeof import("../../../lib/apiClient")>("../../../lib/apiClient");
  return {
    ...actual,
    api: {
      ...actual.api,
      getFieldScopeSearch: (...args: unknown[]) => mockGetFieldScopeSearch(...args),
      addActualWorkLine: (...args: unknown[]) => mockAddActualWorkLine(...args),
      updateActualWorkLine: (...args: unknown[]) => mockUpdateActualWorkLine(...args),
      removeActualWorkLine: (...args: unknown[]) => mockRemoveActualWorkLine(...args),
      submitActualWork: (...args: unknown[]) => mockSubmitActualWork(...args),
      discardActualWork: (...args: unknown[]) => mockDiscardActualWork(...args),
    },
  };
});

type ActualWorkDraft = NonNullable<ActualWorkHistoryResult["openDraft"]>;

const draftLine = {
  id: "line-1",
  displayNameSnapshot: "Filter",
  unitOfMeasureSnapshot: "each",
  actualQuantity: 2,
  note: null,
};

function emptyDraft(overrides: Partial<ActualWorkDraft> = {}): ActualWorkDraft {
  return {
    id: "draft-1",
    status: "Draft",
    outcome: null,
    completionNote: null,
    submittedAtUtc: null,
    concurrencyVersion: "v1",
    lines: [],
    ...overrides,
  };
}

function renderComposer(overrides: Partial<React.ComponentProps<typeof ActualWorkComposer>> = {}) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const onClose = vi.fn();
  const onCommitted = vi.fn();
  const onConflict = vi.fn();
  const onDismissNotice = vi.fn();
  const onRetryReconciliation = vi.fn();
  const onSubmitted = vi.fn();
  const onDiscarded = vi.fn();
  const utils = render(
    <QueryClientProvider client={queryClient}>
      <ActualWorkComposer
        draft={emptyDraft()}
        conflictNotice={null}
        onClose={onClose}
        onCommitted={onCommitted}
        onConflict={onConflict}
        onDismissNotice={onDismissNotice}
        onRetryReconciliation={onRetryReconciliation}
        onSubmitted={onSubmitted}
        onDiscarded={onDiscarded}
        {...overrides}
      />
    </QueryClientProvider>,
  );
  return { ...utils, onClose, onCommitted, onConflict, onDismissNotice, onRetryReconciliation, onSubmitted, onDiscarded };
}

beforeEach(() => {
  vi.clearAllMocks();
  mockGetFieldScopeSearch.mockResolvedValue({ items: [], limit: 20, hasMore: false, nextCursor: null });
});

describe("ActualWorkComposer", () => {
  it("submitting a zero-line draft is disabled until an outcome and completion note are both provided", async () => {
    const user = userEvent.setup();
    renderComposer();

    const submitButton = screen.getByRole("button", { name: "Submit visit to office" });
    expect(submitButton).toBeDisabled();

    await user.selectOptions(screen.getByLabelText("Visit outcome"), "DiagnosticOnly");
    expect(submitButton).toBeDisabled();

    await user.type(screen.getByPlaceholderText(/Completion note/), "Checked unit, no faults found.");
    expect(submitButton).toBeEnabled();
  });

  it("submits a zero-line visit with the chosen outcome and note", async () => {
    const user = userEvent.setup();
    mockSubmitActualWork.mockResolvedValueOnce({ concurrencyVersion: "v2" });
    const { onSubmitted } = renderComposer();

    await user.selectOptions(screen.getByLabelText("Visit outcome"), "NoAccess");
    await user.type(screen.getByPlaceholderText(/Completion note/), "Gate locked, no one home.");
    await user.click(screen.getByRole("button", { name: "Submit visit to office" }));

    await waitFor(() =>
      expect(mockSubmitActualWork).toHaveBeenCalledWith(
        "draft-1",
        { outcome: "NoAccess", completionNote: "Gate locked, no one home." },
        "v1",
      ),
    );
    await waitFor(() => expect(onSubmitted).toHaveBeenCalled());
    expect(screen.getByText("Submitted to office — awaiting review")).toBeInTheDocument();
  });

  it("submitting a draft with at least one line requires neither outcome nor note", () => {
    renderComposer({ draft: emptyDraft({ lines: [draftLine] }) });

    expect(screen.getByRole("button", { name: "Submit visit to office" })).toBeEnabled();
    expect(screen.queryByLabelText("Visit outcome")).not.toBeInTheDocument();
  });

  it("adds a custom off-catalog line with quantity and note", async () => {
    const user = userEvent.setup();
    mockAddActualWorkLine.mockResolvedValueOnce({ lineId: "line-2", actualWorkConcurrencyVersion: "v1" });
    const { onCommitted } = renderComposer();

    await user.type(screen.getByPlaceholderText("Search by name or SKU..."), "gasket");
    await waitFor(() => expect(screen.getByText("Add as custom item")).toBeInTheDocument());
    await user.click(screen.getByText("Add as custom item"));

    await user.type(screen.getByPlaceholderText("Describe the item"), "Rubber gasket");
    await user.clear(screen.getByLabelText("Quantity"));
    await user.type(screen.getByLabelText("Quantity"), "3");
    await user.type(screen.getByPlaceholderText("Note (optional)"), "From truck stock");
    await user.click(screen.getByRole("button", { name: "Add item" }));

    await waitFor(() =>
      expect(mockAddActualWorkLine).toHaveBeenCalledWith(
        "draft-1",
        { offCatalogDescription: "Rubber gasket", actualQuantity: 3, note: "From truck stock" },
        "v1",
      ),
    );
    await waitFor(() => expect(onCommitted).toHaveBeenCalled());
  });

  it("edits an existing line's quantity and note", async () => {
    const user = userEvent.setup();
    mockUpdateActualWorkLine.mockResolvedValueOnce({ concurrencyVersion: "v2" });
    const { onCommitted } = renderComposer({ draft: emptyDraft({ lines: [draftLine] }) });

    await user.click(screen.getByRole("button", { name: "Edit" }));
    const quantityInput = screen.getByLabelText("Quantity");
    await user.clear(quantityInput);
    await user.type(quantityInput, "5");
    await user.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() =>
      expect(mockUpdateActualWorkLine).toHaveBeenCalledWith("draft-1", "line-1", { actualQuantity: 5, note: null }, "v1"),
    );
    await waitFor(() => expect(onCommitted).toHaveBeenCalled());
  });

  it("removes an existing line", async () => {
    const user = userEvent.setup();
    mockRemoveActualWorkLine.mockResolvedValueOnce({ concurrencyVersion: "v2" });
    const { onCommitted } = renderComposer({ draft: emptyDraft({ lines: [draftLine] }) });

    await user.click(screen.getByRole("button", { name: "Remove" }));

    await waitFor(() => expect(mockRemoveActualWorkLine).toHaveBeenCalledWith("draft-1", "line-1", "v1"));
    await waitFor(() => expect(onCommitted).toHaveBeenCalled());
  });

  it("keeps the remove control disabled until the awaited onCommitted refresh resolves, preventing a second action against a stale version", async () => {
    const user = userEvent.setup();
    mockRemoveActualWorkLine.mockResolvedValueOnce({ concurrencyVersion: "v2" });
    let resolveCommitted!: () => void;
    const onCommitted = vi.fn(() => new Promise<void>((resolve) => (resolveCommitted = resolve)));
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={queryClient}>
        <ActualWorkComposer
          draft={emptyDraft({ lines: [draftLine] })}
          conflictNotice={null}
          onClose={vi.fn()}
          onCommitted={onCommitted}
          onConflict={vi.fn()}
          onDismissNotice={vi.fn()}
          onRetryReconciliation={vi.fn()}
          onSubmitted={vi.fn()}
          onDiscarded={vi.fn()}
        />
      </QueryClientProvider>,
    );

    const removeButton = screen.getByRole("button", { name: "Remove" });
    await user.click(removeButton);

    await waitFor(() => expect(mockRemoveActualWorkLine).toHaveBeenCalled());
    // The write itself has settled, but onCommitted's refresh is still in flight — the control must
    // stay disabled so a rapid second click cannot fire against the pre-refresh concurrencyVersion.
    await waitFor(() => expect(screen.getByRole("button", { name: "Remove" })).toBeDisabled());

    resolveCommitted();
    await waitFor(() => expect(screen.getByRole("button", { name: "Remove" })).toBeEnabled());
  });

  it("discards the draft and calls onDiscarded", async () => {
    const user = userEvent.setup();
    mockDiscardActualWork.mockResolvedValueOnce(undefined);
    const { onDiscarded } = renderComposer({ draft: emptyDraft({ lines: [draftLine] }) });

    await user.click(screen.getByRole("button", { name: "Discard this visit" }));

    await waitFor(() => expect(mockDiscardActualWork).toHaveBeenCalledWith("draft-1", "v1"));
    await waitFor(() => expect(onDiscarded).toHaveBeenCalled());
  });

  it("a 409 on line add surfaces onConflict rather than a field-level error", async () => {
    const user = userEvent.setup();
    mockAddActualWorkLine.mockRejectedValueOnce(new ApiError(409, "ActualWork.VersionMismatch", "conflict"));
    const { onConflict } = renderComposer();

    await user.type(screen.getByPlaceholderText("Search by name or SKU..."), "gasket");
    await waitFor(() => expect(screen.getByText("Add as custom item")).toBeInTheDocument());
    await user.click(screen.getByText("Add as custom item"));
    await user.type(screen.getByPlaceholderText("Describe the item"), "Rubber gasket");
    await user.click(screen.getByRole("button", { name: "Add item" }));

    await waitFor(() => expect(onConflict).toHaveBeenCalled());
  });

  it("renders the conflict notice with a dismiss action", async () => {
    const user = userEvent.setup();
    const { onDismissNotice } = renderComposer({ conflictNotice: "This visit changed elsewhere — refreshed with the latest draft. Try again." });

    expect(screen.getByRole("status")).toHaveTextContent("This visit changed elsewhere");
    await user.click(screen.getByRole("button", { name: "Dismiss" }));

    expect(onDismissNotice).toHaveBeenCalled();
  });
});
