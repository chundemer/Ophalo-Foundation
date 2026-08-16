import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ProposedScopeComposer } from "../ProposedScopeComposer";
import { ApiError, type ProposedScopeDetailResult } from "../../../lib/apiClient";
import {
  PROPOSED_SCOPE_CONFLICT_NOTICE,
  PROPOSED_SCOPE_RECONCILE_RELOAD_FAILURE_NOTICE,
} from "../useProposedScopeCapture";

const mockGetFieldCatalogItems = vi.fn();
const mockFieldSelectProposedScopeLine = vi.fn();
const mockGetFieldQuickScopeActions = vi.fn();

vi.mock("../../../lib/apiClient", async () => {
  const actual = await vi.importActual<typeof import("../../../lib/apiClient")>("../../../lib/apiClient");
  return {
    ...actual,
    api: {
      ...actual.api,
      getFieldCatalogItems: (...args: unknown[]) => mockGetFieldCatalogItems(...args),
      fieldSelectProposedScopeLine: (...args: unknown[]) => mockFieldSelectProposedScopeLine(...args),
      getFieldQuickScopeActions: (...args: unknown[]) => mockGetFieldQuickScopeActions(...args),
    },
  };
});

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
        {...overrides}
      />
    </QueryClientProvider>,
  );
  return { ...utils, onClose, onCommitted, onConflict, onDismissNotice, onRetryReconciliation };
}

beforeEach(() => {
  vi.clearAllMocks();
  mockGetFieldCatalogItems.mockResolvedValue({ items: [], limit: 20, hasMore: false, nextCursor: null });
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

    expect(await screen.findByRole("textbox", { name: "Search catalog items" })).toBeInTheDocument();
  });

  it("renders a known catalog result and the explicit custom-add action from the same search", async () => {
    mockGetFieldCatalogItems.mockResolvedValue({
      items: [
        {
          item: { id: "item-1", type: "Material", displayName: "Filter", externalKey: null, categoryId: null, unitOfMeasure: "each" },
          matchRank: "DisplayName",
          matchReason: null,
        },
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
    mockGetFieldCatalogItems.mockResolvedValue({
      items: [
        {
          item: { id: "item-1", type: "Material", displayName: "Filter", externalKey: null, categoryId: null, unitOfMeasure: "each" },
          matchRank: "DisplayName",
          matchReason: null,
        },
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
    await waitFor(() => expect(onCommitted).toHaveBeenCalled());
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
    mockGetFieldCatalogItems.mockResolvedValue({
      items: [
        {
          item: { id: "item-1", type: "Material", displayName: "Filter", externalKey: null, categoryId: null, unitOfMeasure: "each" },
          matchRank: "DisplayName",
          matchReason: null,
        },
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
    expect(screen.getByLabelText("Quantity (each)")).not.toHaveAttribute("aria-invalid");
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

    await waitFor(() => expect(onCommitted).toHaveBeenCalled());
    await waitFor(() => expect(screen.getByPlaceholderText("Search by name, SKU, or alias…")).toHaveValue(""));

    // Reopen the custom-item entry to confirm quantity and note reverted to their defaults —
    // resetAfterSuccess only fires after the confirmed POST success above, not before.
    await user.type(screen.getByPlaceholderText("Search by name, SKU, or alias…"), "Rag two");
    await user.click(await screen.findByRole("button", { name: 'Add “Rag two” as custom item' }));
    expect(screen.getByLabelText("Quantity")).toHaveValue(1);
    expect(screen.getByLabelText("Note")).toHaveValue("");
  });

  it("renders the live Draft with existing lines and a disabled submit footer", () => {
    renderComposer({
      scope: {
        ...scope,
        lines: [
          {
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
          },
        ],
      },
    });

    expect(screen.getByText("Filter")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Submit scope to office" })).toBeDisabled();
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
    await waitFor(() => expect(onCommitted).toHaveBeenCalled());
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
