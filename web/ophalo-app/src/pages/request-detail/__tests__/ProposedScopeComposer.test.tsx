import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, act } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ProposedScopeComposer } from "../ProposedScopeComposer";
import { ApiError, type ProposedScopeDetailResult } from "../../../lib/apiClient";
import {
  PROPOSED_SCOPE_CONFLICT_NOTICE,
  PROPOSED_SCOPE_RECONCILE_RELOAD_FAILURE_NOTICE,
  type ProposedScopeNudgeState,
} from "../useProposedScopeCapture";

const mockGetFieldScopeSearch = vi.fn();
const mockFieldSelectProposedScopeLine = vi.fn();
const mockExpandProposedScopeAssembly = vi.fn();
const mockGetFieldQuickScopeActions = vi.fn();
const mockUpdateProposedScopeLine = vi.fn();
const mockRemoveProposedScopeLine = vi.fn();
const mockRestoreProposedScopeLine = vi.fn();
const mockSubmitProposedScope = vi.fn();

vi.mock("../../../lib/apiClient", async () => {
  const actual = await vi.importActual<typeof import("../../../lib/apiClient")>("../../../lib/apiClient");
  return {
    ...actual,
    api: {
      ...actual.api,
      getFieldScopeSearch: (...args: unknown[]) => mockGetFieldScopeSearch(...args),
      fieldSelectProposedScopeLine: (...args: unknown[]) => mockFieldSelectProposedScopeLine(...args),
      expandProposedScopeAssembly: (...args: unknown[]) => mockExpandProposedScopeAssembly(...args),
      getFieldQuickScopeActions: (...args: unknown[]) => mockGetFieldQuickScopeActions(...args),
      updateProposedScopeLine: (...args: unknown[]) => mockUpdateProposedScopeLine(...args),
      removeProposedScopeLine: (...args: unknown[]) => mockRemoveProposedScopeLine(...args),
      restoreProposedScopeLine: (...args: unknown[]) => mockRestoreProposedScopeLine(...args),
      submitProposedScope: (...args: unknown[]) => mockSubmitProposedScope(...args),
    },
  };
});

const associatedItemLine = {
  id: "line-1",
  lineType: "KnownCatalogItem",
  catalogItemId: "item-1",
  offeringAssemblyId: null,
  quantity: 2,
  isException: false,
  offCatalogDescription: null,
  offCatalogQuantity: null,
  note: null,
  displayOrder: 0,
  displayNameSnapshot: "Filter",
  unitOfMeasureSnapshot: "each",
  offeringAssemblyNameSnapshot: null,
  defaultQuantitySnapshot: null,
};

const customItemLine = {
  ...associatedItemLine,
  id: "line-custom",
  lineType: "OffCatalogItem",
  catalogItemId: null,
  offCatalogDescription: "bolt 3/4\"",
  offCatalogQuantity: 4,
  quantity: 4,
  displayNameSnapshot: "bolt 3/4\"",
  unitOfMeasureSnapshot: null,
};

const scope: ProposedScopeDetailResult = {
  id: "scope-1",
  requestId: "request-1",
  status: "Draft",
  concurrencyVersion: "v1",
  lines: [],
};

function renderComposer(overrides: Partial<React.ComponentProps<typeof ProposedScopeComposer>> = {}) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const onClose = vi.fn();
  const onCommitted = vi.fn();
  const onConflict = vi.fn();
  const onDismissNotice = vi.fn();
  const onRetryReconciliation = vi.fn();
  const onAcceptNudge = vi.fn();
  const onDismissNudge = vi.fn();
  const onNudgeAcceptConflict = vi.fn();
  const utils = render(
    <QueryClientProvider client={queryClient}>
      <ProposedScopeComposer
        scope={scope}
        conflictNotice={null}
        onClose={onClose}
        onCommitted={onCommitted}
        onConflict={onConflict}
        onDismissNotice={onDismissNotice}
        onRetryReconciliation={onRetryReconciliation}
        nudge={null}
        onAcceptNudge={onAcceptNudge}
        onDismissNudge={onDismissNudge}
        onNudgeAcceptConflict={onNudgeAcceptConflict}
        {...overrides}
      />
    </QueryClientProvider>,
  );
  return {
    ...utils,
    onClose,
    onCommitted,
    onConflict,
    onDismissNotice,
    onRetryReconciliation,
    onAcceptNudge,
    onDismissNudge,
    onNudgeAcceptConflict,
  };
}

beforeEach(() => {
  vi.clearAllMocks();
  mockGetFieldScopeSearch.mockResolvedValue({ items: [], limit: 20, hasMore: false, nextCursor: null });
  mockGetFieldQuickScopeActions.mockResolvedValue({ actions: [] });
});

describe("ProposedScopeComposer", () => {
  it("focuses the unified search input on open and does not write a line while typing", async () => {
    const user = userEvent.setup();
    renderComposer();

    const input = screen.getByPlaceholderText("Search by name, SKU, or alias…");
    await waitFor(() => expect(input).toHaveFocus());

    await user.type(input, "Filter");
    expect(mockFieldSelectProposedScopeLine).not.toHaveBeenCalled();
  });

  it("gives the unified search input an accessible name via a label, not placeholder text alone", async () => {
    renderComposer();

    expect(await screen.findByRole("textbox", { name: "Search catalog items and assemblies" })).toBeInTheDocument();
  });

  it("renders a known catalog result and the explicit custom-add action from the same search", async () => {
    mockGetFieldScopeSearch.mockResolvedValue({
      items: [
        { kind: "CatalogItem", id: "item-1", displayName: "Filter", defaultItemCount: null, catalogItemType: null, externalKey: null },
      ],
      limit: 20,
      hasMore: false,
      nextCursor: null,
    });

    const user = userEvent.setup();
    renderComposer();
    await user.type(screen.getByPlaceholderText("Search by name, SKU, or alias…"), "Filter");

    expect(await screen.findByRole("button", { name: "Filter" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: 'Add “Filter” as custom item' })).toBeInTheDocument();
  });

  it("adds a known catalog item via field-select and reloads the authoritative scope", async () => {
    mockGetFieldScopeSearch.mockResolvedValue({
      items: [
        { kind: "CatalogItem", id: "item-1", displayName: "Filter", defaultItemCount: null, catalogItemType: null, externalKey: null },
      ],
      limit: 20,
      hasMore: false,
      nextCursor: null,
    });
    mockFieldSelectProposedScopeLine.mockResolvedValueOnce({ lineId: "line-1", concurrencyVersion: "v2" });

    const user = userEvent.setup();
    const { onCommitted } = renderComposer();
    await user.type(screen.getByPlaceholderText("Search by name, SKU, or alias…"), "Filter");
    await user.click(await screen.findByRole("button", { name: "Filter" }));
    await user.click(screen.getByRole("button", { name: "Add to scope" }));

    await waitFor(() =>
      expect(mockFieldSelectProposedScopeLine).toHaveBeenCalledWith(
        "scope-1",
        { lineType: "KnownCatalogItem", catalogItemId: "item-1", quantity: 1, note: null },
        "v1",
      ),
    );
    await waitFor(() => expect(onCommitted).toHaveBeenCalledWith({ catalogItemId: "item-1" }));
  });

  it("groups matching assemblies before matching catalog items, badges each, and keeps custom item last", async () => {
    mockGetFieldScopeSearch.mockResolvedValue({
      items: [
        { kind: "OfferingAssembly", id: "assembly-1", displayName: "Furnace Tune-Up", defaultItemCount: 4, catalogItemType: null, externalKey: null },
        { kind: "CatalogItem", id: "item-1", displayName: "Furnace Inspection", defaultItemCount: null, catalogItemType: "Service", externalKey: null },
      ],
      limit: 20,
      hasMore: false,
      nextCursor: null,
    });

    const user = userEvent.setup();
    renderComposer();
    await user.type(screen.getByPlaceholderText("Search by name, SKU, or alias…"), "furn");

    await screen.findByRole("button", { name: /Furnace Tune-Up/ });
    const buttons = screen.getAllByRole("button").filter((b) => /Furnace|custom item/.test(b.textContent ?? ""));
    expect(buttons.map((b) => b.textContent)).toEqual([
      expect.stringContaining("Furnace Tune-Up"),
      expect.stringContaining("Furnace Inspection"),
      expect.stringContaining('Add “furn” as custom item'),
    ]);
    expect(screen.getByRole("button", { name: /Furnace Tune-Up/ })).toHaveTextContent("Assembly");
    expect(screen.getByRole("button", { name: /Furnace Tune-Up/ })).toHaveTextContent("Expands 4 items");
    expect(screen.getByRole("button", { name: /Furnace Inspection/ })).toHaveTextContent("Service");
    expect(screen.getByText("Matching assemblies")).toBeInTheDocument();
    expect(screen.getByText("Matching catalog items")).toBeInTheDocument();
  });

  it("omits a group heading when that group has no matches", async () => {
    mockGetFieldScopeSearch.mockResolvedValue({
      items: [
        { kind: "CatalogItem", id: "item-1", displayName: "Furnace Inspection", defaultItemCount: null, catalogItemType: "Service", externalKey: null },
      ],
      limit: 20,
      hasMore: false,
      nextCursor: null,
    });

    const user = userEvent.setup();
    renderComposer();
    await user.type(screen.getByPlaceholderText("Search by name, SKU, or alias…"), "furn");

    await screen.findByText("Matching catalog items");
    expect(screen.queryByText("Matching assemblies")).not.toBeInTheDocument();
  });

  it("dispatches an assembly result through expand-assembly immediately, with no quantity step", async () => {
    mockGetFieldScopeSearch.mockResolvedValue({
      items: [
        { kind: "OfferingAssembly", id: "assembly-1", displayName: "Furnace Tune-Up", defaultItemCount: 4, catalogItemType: null, externalKey: null },
      ],
      limit: 20,
      hasMore: false,
      nextCursor: null,
    });
    mockExpandProposedScopeAssembly.mockResolvedValueOnce({ lineIds: ["line-1", "line-2"], concurrencyVersion: "v2" });

    const user = userEvent.setup();
    const { onCommitted } = renderComposer();
    await user.type(screen.getByPlaceholderText("Search by name, SKU, or alias…"), "furn");
    await user.click(await screen.findByRole("button", { name: /Furnace Tune-Up/ }));

    await waitFor(() =>
      expect(mockExpandProposedScopeAssembly).toHaveBeenCalledWith(
        "scope-1",
        { offeringAssemblyId: "assembly-1", excludedOptionalItemIds: [] },
        "v1",
      ),
    );
    expect(mockFieldSelectProposedScopeLine).not.toHaveBeenCalled();
    await waitFor(() => expect(onCommitted).toHaveBeenCalledWith({ offeringAssemblyId: "assembly-1" }));
  });

  it("shows an explicit no-match state only after a successful empty response", async () => {
    mockGetFieldScopeSearch.mockResolvedValue({ items: [], limit: 20, hasMore: false, nextCursor: null });

    const user = userEvent.setup();
    renderComposer();
    await user.type(screen.getByPlaceholderText("Search by name, SKU, or alias…"), "zzz");

    expect(await screen.findByText("No Price Book matches")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: 'Add “zzz” as custom item' })).toBeInTheDocument();
  });

  it("shows a distinct error state when the search request fails, not an empty result", async () => {
    mockGetFieldScopeSearch.mockRejectedValueOnce(new Error("network down"));

    const user = userEvent.setup();
    renderComposer();
    await user.type(screen.getByPlaceholderText("Search by name, SKU, or alias…"), "furn");

    expect(await screen.findByRole("alert")).toHaveTextContent("Search failed. Try again.");
    expect(screen.queryByText("No Price Book matches")).not.toBeInTheDocument();
  });

  it("adds an explicit custom item and keeps the description visible and editable on a failed add", async () => {
    mockFieldSelectProposedScopeLine.mockRejectedValueOnce(new Error("network down"));

    const user = userEvent.setup();
    const { onConflict } = renderComposer();
    await user.type(screen.getByPlaceholderText("Search by name, SKU, or alias…"), "Shop rag");
    await user.click(await screen.findByRole("button", { name: 'Add “Shop rag” as custom item' }));
    await user.click(screen.getByRole("button", { name: "Add to scope" }));

    await waitFor(() =>
      expect(mockFieldSelectProposedScopeLine).toHaveBeenCalledWith(
        "scope-1",
        { lineType: "OffCatalogItem", offCatalogDescription: "Shop rag", quantity: 1, note: null },
        "v1",
      ),
    );
    await waitFor(() => expect(onConflict).toHaveBeenCalled());

    const descriptionInput = screen.getByLabelText("Custom item description") as HTMLInputElement;
    expect(descriptionInput.value).toBe("Shop rag");
    expect(descriptionInput).not.toBeDisabled();
    await user.type(descriptionInput, " (fixed)");
    expect(descriptionInput.value).toBe("Shop rag (fixed)");
  });

  it("associates a description validation error with the description input for assistive tech", async () => {
    mockFieldSelectProposedScopeLine.mockRejectedValueOnce(
      new ApiError(400, "ProposedScope.LineOffCatalogDescriptionInvalidCharacters", "invalid characters"),
    );

    const user = userEvent.setup();
    renderComposer();
    await user.type(screen.getByPlaceholderText("Search by name, SKU, or alias…"), "Baddesc");
    await user.click(await screen.findByRole("button", { name: 'Add “Baddesc” as custom item' }));
    await user.click(screen.getByRole("button", { name: "Add to scope" }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("This description contains characters that aren't allowed.");

    const descriptionInput = screen.getByLabelText("Custom item description");
    expect(descriptionInput).toHaveAttribute("aria-invalid", "true");
    expect(descriptionInput).toHaveAttribute("aria-describedby", alert.id);
  });

  it("announces a non-quantity add failure without marking the quantity field invalid", async () => {
    mockFieldSelectProposedScopeLine.mockRejectedValueOnce(
      new ApiError(404, "ProposedScope.LineCatalogItemNotFound", "not found"),
    );
    mockGetFieldScopeSearch.mockResolvedValue({
      items: [
        { kind: "CatalogItem", id: "item-1", displayName: "Filter", defaultItemCount: null, catalogItemType: null, externalKey: null },
      ],
      limit: 20,
      hasMore: false,
      nextCursor: null,
    });

    const user = userEvent.setup();
    renderComposer();
    await user.type(screen.getByPlaceholderText("Search by name, SKU, or alias…"), "Filter");
    await user.click(await screen.findByRole("button", { name: "Filter" }));
    await user.click(screen.getByRole("button", { name: "Add to scope" }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("This item is no longer available.");
    expect(screen.getByLabelText("Quantity")).not.toHaveAttribute("aria-invalid");
  });

  it("clears the custom description, quantity, and note only after a successful custom add", async () => {
    mockFieldSelectProposedScopeLine.mockResolvedValueOnce({ lineId: "line-1", concurrencyVersion: "v2" });

    const user = userEvent.setup();
    const { onCommitted } = renderComposer();
    await user.type(screen.getByPlaceholderText("Search by name, SKU, or alias…"), "Shop rag");
    await user.click(await screen.findByRole("button", { name: 'Add “Shop rag” as custom item' }));
    const quantityInput = screen.getByLabelText("Quantity");
    await user.clear(quantityInput);
    await user.type(quantityInput, "3");
    await user.type(screen.getByLabelText("Note"), "from the truck");
    await user.click(screen.getByRole("button", { name: "Add to scope" }));

    await waitFor(() => expect(onCommitted).toHaveBeenCalledWith(undefined));
    await waitFor(() => expect(screen.getByPlaceholderText("Search by name, SKU, or alias…")).toHaveValue(""));

    // Reopen the custom-item entry to confirm quantity and note reverted to their defaults —
    // resetAfterSuccess only fires after the confirmed POST success above, not before.
    await user.type(screen.getByPlaceholderText("Search by name, SKU, or alias…"), "Rag two");
    await user.click(await screen.findByRole("button", { name: 'Add “Rag two” as custom item' }));
    expect(screen.getByLabelText("Quantity")).toHaveValue(1);
    expect(screen.getByLabelText("Note")).toHaveValue("");
  });

  it("renders the live Draft with existing lines and an enabled submit footer", () => {
    renderComposer({ scope: { ...scope, lines: [associatedItemLine] } });

    expect(screen.getByText("Filter")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Submit scope to office" })).not.toBeDisabled();
  });

  it("identifies an off-catalog Draft line as a custom item", () => {
    renderComposer({ scope: { ...scope, lines: [customItemLine] } });

    expect(screen.getByText("Custom item")).toBeInTheDocument();
  });

  it("disables submit and explains why for an empty Draft", () => {
    renderComposer();

    expect(screen.getByRole("button", { name: "Submit scope to office" })).toBeDisabled();
    expect(screen.getByText("Add at least one item before submitting.")).toBeInTheDocument();
  });

  it("submits the scope and renders the locked submitted outcome on success", async () => {
    mockSubmitProposedScope.mockResolvedValueOnce({ concurrencyVersion: "v2" });
    const user = userEvent.setup();
    const { onCommitted } = renderComposer({ scope: { ...scope, lines: [associatedItemLine] } });

    await user.click(screen.getByRole("button", { name: "Submit scope to office" }));

    expect(mockSubmitProposedScope).toHaveBeenCalledWith("scope-1", "v1");
    expect(await screen.findByText("Submitted to office — awaiting review")).toBeInTheDocument();
    expect(onCommitted).toHaveBeenCalledWith();
    expect(screen.queryByRole("button", { name: "Submit scope to office" })).not.toBeInTheDocument();
  });

  it("edits a line's quantity, note, and exception flag through the server-authoritative PATCH", async () => {
    mockUpdateProposedScopeLine.mockResolvedValueOnce({ concurrencyVersion: "v2" });
    const user = userEvent.setup();
    const { onCommitted } = renderComposer({
      scope: { ...scope, lines: [{ ...associatedItemLine, lineType: "AssociatedItem" }] },
    });

    await user.click(screen.getByRole("button", { name: "Edit" }));
    const quantityInput = screen.getByLabelText("Quantity (each)");
    await user.clear(quantityInput);
    await user.type(quantityInput, "5");
    await user.type(screen.getByLabelText("Note"), "swap for spare");
    await user.click(screen.getByRole("checkbox", { name: /exception/i }));
    await user.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() =>
      expect(mockUpdateProposedScopeLine).toHaveBeenCalledWith(
        "scope-1",
        "line-1",
        { quantity: 5, isException: true, note: "swap for spare", displayOrder: 0 },
        "v1",
      ),
    );
    await waitFor(() => expect(onCommitted).toHaveBeenCalledWith());
  });

  it("announces a generic edit failure without marking the quantity field invalid", async () => {
    // A non-409 ApiError whose code isn't LineNotFound/LineQuantityMustBePositive falls through to
    // the generic message while staying in the editor — distinct from the ambiguous-failure/409
    // paths, which instead close the editor via onConflict.
    mockUpdateProposedScopeLine.mockRejectedValueOnce(
      new ApiError(400, "ProposedScope.LineDisplayOrderMustNotBeNegative", "unexpected"),
    );
    const user = userEvent.setup();
    renderComposer({ scope: { ...scope, lines: [associatedItemLine] } });

    await user.click(screen.getByRole("button", { name: "Edit" }));
    await user.click(screen.getByRole("button", { name: "Save" }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Something went wrong. Try again.");
    expect(screen.getByLabelText("Quantity (each)")).not.toHaveAttribute("aria-invalid");
    expect(screen.getByLabelText("Quantity (each)")).not.toHaveAttribute("aria-describedby");
  });

  it("marks the quantity field invalid only for the positive-quantity validation error", async () => {
    mockUpdateProposedScopeLine.mockRejectedValueOnce(
      new ApiError(400, "ProposedScope.LineQuantityMustBePositive", "must be positive"),
    );
    const user = userEvent.setup();
    renderComposer({ scope: { ...scope, lines: [associatedItemLine] } });

    await user.click(screen.getByRole("button", { name: "Edit" }));
    await user.click(screen.getByRole("button", { name: "Save" }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Quantity must be greater than zero.");
    const quantityInput = screen.getByLabelText("Quantity (each)");
    expect(quantityInput).toHaveAttribute("aria-invalid", "true");
    expect(quantityInput).toHaveAttribute("aria-describedby", alert.id);
  });

  it("does not show the exception control for a non-AssociatedItem line", async () => {
    const user = userEvent.setup();
    renderComposer({ scope: { ...scope, lines: [associatedItemLine] } });

    await user.click(screen.getByRole("button", { name: "Edit" }));
    expect(screen.queryByRole("checkbox", { name: /exception/i })).not.toBeInTheDocument();
  });

  it("removes a line only after confirmed server success, then offers a five-second Undo", async () => {
    mockRemoveProposedScopeLine.mockResolvedValueOnce({ concurrencyVersion: "v2" });
    const user = userEvent.setup();
    const { onCommitted } = renderComposer({ scope: { ...scope, lines: [associatedItemLine] } });

    await user.click(screen.getByRole("button", { name: "Remove" }));

    await waitFor(() => expect(mockRemoveProposedScopeLine).toHaveBeenCalledWith("scope-1", "line-1", "v1"));
    await waitFor(() => expect(onCommitted).toHaveBeenCalledWith());
    expect(await screen.findByRole("button", { name: "Undo" })).toBeInTheDocument();
  });

  it("restores using the version returned by the delete response, not the scope's pre-delete version", async () => {
    // v1 is the composer's pre-delete `scope.concurrencyVersion`; the delete response returns v2 —
    // Undo must restore against v2, proving it doesn't fall back to the stale pre-delete version.
    mockRemoveProposedScopeLine.mockResolvedValueOnce({ concurrencyVersion: "v2" });
    mockRestoreProposedScopeLine.mockResolvedValueOnce({ concurrencyVersion: "v3" });
    const user = userEvent.setup();
    const { onCommitted } = renderComposer({ scope: { ...scope, lines: [associatedItemLine] } });

    await user.click(screen.getByRole("button", { name: "Remove" }));
    await screen.findByRole("button", { name: "Undo" });
    onCommitted.mockClear();
    await user.click(screen.getByRole("button", { name: "Undo" }));

    await waitFor(() =>
      expect(mockRestoreProposedScopeLine).toHaveBeenCalledWith("scope-1", "line-1", "v2"),
    );
    expect(mockRestoreProposedScopeLine).not.toHaveBeenCalledWith("scope-1", "line-1", "v1");
    await waitFor(() => expect(onCommitted).toHaveBeenCalledWith());
    expect(screen.queryByRole("button", { name: "Undo" })).not.toBeInTheDocument();
  });

  it("dismisses the Undo toast and surfaces the shared notice when restore has expired", async () => {
    mockRemoveProposedScopeLine.mockResolvedValueOnce({ concurrencyVersion: "v2" });
    // ErrorHttpMapper maps ProposedScope.RestoreExpired to 422 (Unprocessable Entity), not 409 — the
    // component routes by error code, not status, but the fixture must mirror the real API contract.
    mockRestoreProposedScopeLine.mockRejectedValueOnce(
      new ApiError(422, "ProposedScope.RestoreExpired", "expired"),
    );
    const user = userEvent.setup();
    const { onConflict } = renderComposer({ scope: { ...scope, lines: [associatedItemLine] } });

    await user.click(screen.getByRole("button", { name: "Remove" }));
    await user.click(await screen.findByRole("button", { name: "Undo" }));

    await waitFor(() =>
      expect(onConflict).toHaveBeenCalledWith("This item can no longer be undone — refreshed with the latest scope."),
    );
    expect(screen.queryByRole("button", { name: "Undo" })).not.toBeInTheDocument();
  });

  it("dismisses the Undo toast and surfaces the same notice when the line was already restored", async () => {
    // ErrorHttpMapper maps ProposedScope.RestoreLineAlreadyExists to 409 (Conflict) — a distinct real
    // status from RestoreExpired's 422, but both are promised to show the same "can no longer be
    // undone" message rather than the generic conflict notice.
    mockRemoveProposedScopeLine.mockResolvedValueOnce({ concurrencyVersion: "v2" });
    mockRestoreProposedScopeLine.mockRejectedValueOnce(
      new ApiError(409, "ProposedScope.RestoreLineAlreadyExists", "already restored"),
    );
    const user = userEvent.setup();
    const { onConflict } = renderComposer({ scope: { ...scope, lines: [associatedItemLine] } });

    await user.click(screen.getByRole("button", { name: "Remove" }));
    await user.click(await screen.findByRole("button", { name: "Undo" }));

    await waitFor(() =>
      expect(onConflict).toHaveBeenCalledWith("This item can no longer be undone — refreshed with the latest scope."),
    );
    expect(screen.queryByRole("button", { name: "Undo" })).not.toBeInTheDocument();
  });

  it("auto-dismisses the Undo toast after five seconds without issuing a restore request", async () => {
    // shouldAdvanceTime keeps the fake clock ticking in step with real elapsed time, so userEvent's
    // click and the mocked mutation's microtask resolution still work normally — only the final
    // advanceTimersByTime call below needs to fast-forward instantly.
    vi.useFakeTimers({ shouldAdvanceTime: true });
    try {
      const user = userEvent.setup({ delay: null });
      mockRemoveProposedScopeLine.mockResolvedValueOnce({ concurrencyVersion: "v2" });
      renderComposer({ scope: { ...scope, lines: [associatedItemLine] } });

      await user.click(screen.getByRole("button", { name: "Remove" }));
      expect(await screen.findByRole("button", { name: "Undo" })).toBeInTheDocument();

      await act(async () => {
        await vi.advanceTimersByTimeAsync(5000);
      });

      expect(screen.queryByRole("button", { name: "Undo" })).not.toBeInTheDocument();
      expect(mockRestoreProposedScopeLine).not.toHaveBeenCalled();
    } finally {
      vi.useRealTimers();
    }
  });

  it("renders Quick actions above search and dispatches through field-select on tap", async () => {
    mockGetFieldQuickScopeActions.mockResolvedValue({
      actions: [{ id: "qa-1", order: 0, catalogItemId: "item-1", offeringAssemblyId: null, targetDisplayName: "Filter" }],
    });
    mockFieldSelectProposedScopeLine.mockResolvedValueOnce({ lineId: "line-1", concurrencyVersion: "v2" });

    const user = userEvent.setup();
    const { onCommitted } = renderComposer();
    await user.click(await screen.findByRole("button", { name: "Filter" }));

    await waitFor(() =>
      expect(mockFieldSelectProposedScopeLine).toHaveBeenCalledWith(
        "scope-1",
        { lineType: "KnownCatalogItem", catalogItemId: "item-1", quantity: 1 },
        "v1",
      ),
    );
    await waitFor(() => expect(onCommitted).toHaveBeenCalledWith({ catalogItemId: "item-1" }));
  });

  it("shows a Retry control for the reload-failure notice and wires it to onRetryReconciliation", async () => {
    const user = userEvent.setup();
    const { onRetryReconciliation } = renderComposer({ conflictNotice: PROPOSED_SCOPE_RECONCILE_RELOAD_FAILURE_NOTICE });

    await user.click(screen.getByRole("button", { name: "Retry" }));
    expect(onRetryReconciliation).toHaveBeenCalledTimes(1);
  });

  it("does not show a Retry control for the generic conflict notice", () => {
    renderComposer({ conflictNotice: PROPOSED_SCOPE_CONFLICT_NOTICE });

    expect(screen.queryByRole("button", { name: "Retry" })).not.toBeInTheDocument();
  });
});

const catalogNudge: ProposedScopeNudgeState = {
  ruleId: "rule-1",
  suggestions: [
    { id: "sugg-1", order: 0, catalogItemId: "item-9", offeringAssemblyId: null, displayName: "Drain pan", targetKind: "CatalogItem" },
  ],
};

const assemblyNudge: ProposedScopeNudgeState = {
  ruleId: "rule-2",
  suggestions: [
    {
      id: "sugg-2",
      order: 0,
      catalogItemId: null,
      offeringAssemblyId: "assembly-9",
      displayName: "Condensate kit",
      targetKind: "OfferingAssembly",
    },
  ],
};

describe("ComposerNudgePanel wiring (build-log/125)", () => {
  it("accepting a catalog suggestion dispatches field-select and retires the rule only after success", async () => {
    mockFieldSelectProposedScopeLine.mockResolvedValueOnce({ lineId: "line-9", concurrencyVersion: "v2" });
    const user = userEvent.setup();
    const { onAcceptNudge } = renderComposer({ nudge: catalogNudge });

    await user.click(screen.getByRole("button", { name: "Drain pan" }));

    await waitFor(() =>
      expect(mockFieldSelectProposedScopeLine).toHaveBeenCalledWith(
        "scope-1",
        { lineType: "KnownCatalogItem", catalogItemId: "item-9", quantity: 1 },
        "v1",
      ),
    );
    await waitFor(() => expect(onAcceptNudge).toHaveBeenCalledWith("rule-1"));
  });

  it("accepting an assembly suggestion dispatches expand-assembly and retires the rule", async () => {
    mockExpandProposedScopeAssembly.mockResolvedValueOnce({ lineIds: ["line-9"], concurrencyVersion: "v2" });
    const user = userEvent.setup();
    const { onAcceptNudge } = renderComposer({ nudge: assemblyNudge });

    await user.click(screen.getByRole("button", { name: "Condensate kit" }));

    await waitFor(() =>
      expect(mockExpandProposedScopeAssembly).toHaveBeenCalledWith(
        "scope-1",
        { offeringAssemblyId: "assembly-9", excludedOptionalItemIds: [] },
        "v1",
      ),
    );
    await waitFor(() => expect(onAcceptNudge).toHaveBeenCalledWith("rule-2"));
  });

  it("dismiss retires the panel with no API write", async () => {
    const user = userEvent.setup();
    const { onDismissNudge } = renderComposer({ nudge: catalogNudge });

    await user.click(screen.getByRole("button", { name: "Dismiss" }));

    expect(onDismissNudge).toHaveBeenCalledWith("rule-1");
    expect(mockFieldSelectProposedScopeLine).not.toHaveBeenCalled();
    expect(mockExpandProposedScopeAssembly).not.toHaveBeenCalled();
  });

  it("an ineligible-assembly 409 shows the unavailable notice and preserves the panel, not conflict reconciliation", async () => {
    // ExpandAssemblyNotOperationallyEligible is itself a real 409 — the unavailable-code check must
    // win over generic status handling, matching ComposerQuickActions' existing ordering.
    mockExpandProposedScopeAssembly.mockRejectedValueOnce(
      new ApiError(409, "ProposedScope.ExpandAssemblyNotOperationallyEligible", "no longer eligible"),
    );
    const user = userEvent.setup();
    const { onAcceptNudge, onNudgeAcceptConflict } = renderComposer({ nudge: assemblyNudge });

    await user.click(screen.getByRole("button", { name: "Condensate kit" }));

    await waitFor(() =>
      expect(screen.getByRole("alert")).toHaveTextContent("The office recently updated this item and it's no longer available."),
    );
    expect(onNudgeAcceptConflict).not.toHaveBeenCalled();
    expect(onAcceptNudge).not.toHaveBeenCalled();
    expect(screen.getByRole("button", { name: "Condensate kit" })).toBeInTheDocument();
  });

  it("a 409 during accept follows reconciliation and does not retire the rule", async () => {
    mockFieldSelectProposedScopeLine.mockRejectedValueOnce(new ApiError(409, "Conflict", "conflict"));
    const user = userEvent.setup();
    const { onAcceptNudge, onNudgeAcceptConflict } = renderComposer({ nudge: catalogNudge });

    await user.click(screen.getByRole("button", { name: "Drain pan" }));

    await waitFor(() => expect(onNudgeAcceptConflict).toHaveBeenCalledTimes(1));
    expect(onAcceptNudge).not.toHaveBeenCalled();
  });

  it("a non-409 accept failure preserves the panel instead of retiring it", async () => {
    mockFieldSelectProposedScopeLine.mockRejectedValueOnce(new Error("network down"));
    const user = userEvent.setup();
    const { onAcceptNudge, onNudgeAcceptConflict } = renderComposer({ nudge: catalogNudge });

    await user.click(screen.getByRole("button", { name: "Drain pan" }));

    await waitFor(() => expect(screen.getByRole("alert")).toHaveTextContent("Something went wrong. Try again."));
    expect(onAcceptNudge).not.toHaveBeenCalled();
    expect(onNudgeAcceptConflict).not.toHaveBeenCalled();
    expect(screen.getByRole("button", { name: "Drain pan" })).toBeInTheDocument();
  });
});
