import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ProposedScopeComposer } from "../ProposedScopeComposer";
import { type ProposedScopeDetailResult } from "../../../lib/apiClient";

const mockGetFieldCatalogItems = vi.fn();
const mockFieldSelectProposedScopeLine = vi.fn();

vi.mock("../../../lib/apiClient", async () => {
  const actual = await vi.importActual<typeof import("../../../lib/apiClient")>("../../../lib/apiClient");
  return {
    ...actual,
    api: {
      ...actual.api,
      getFieldCatalogItems: (...args: unknown[]) => mockGetFieldCatalogItems(...args),
      fieldSelectProposedScopeLine: (...args: unknown[]) => mockFieldSelectProposedScopeLine(...args),
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
  const utils = render(
    <QueryClientProvider client={queryClient}>
      <ProposedScopeComposer
        scope={scope}
        conflictNotice={null}
        onClose={onClose}
        onCommitted={onCommitted}
        onConflict={onConflict}
        onDismissNotice={onDismissNotice}
        {...overrides}
      />
    </QueryClientProvider>,
  );
  return { ...utils, onClose, onCommitted, onConflict, onDismissNotice };
}

beforeEach(() => {
  vi.clearAllMocks();
  mockGetFieldCatalogItems.mockResolvedValue({ items: [], limit: 20, hasMore: false, nextCursor: null });
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

  it("adds an explicit custom item and preserves the description on a failed add", async () => {
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
    expect(screen.getByText('“Shop rag” (custom item)')).toBeInTheDocument();
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
});
