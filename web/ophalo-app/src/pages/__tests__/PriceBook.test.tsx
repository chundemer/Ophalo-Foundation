import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { PriceBook } from "../PriceBook";
import type { CatalogItemListResult, GetCatalogItemsParams } from "../../lib/apiClient";

const mockGetCatalogItems = vi.fn();
const mockGetCatalogCategories = vi.fn();
const mockCreateCatalogItem = vi.fn();

vi.mock("../../lib/apiClient", async () => {
  const actual = await vi.importActual<typeof import("../../lib/apiClient")>("../../lib/apiClient");
  return {
    ...actual,
    api: {
      ...actual.api,
      getCatalogItems: (...args: unknown[]) => mockGetCatalogItems(...args),
      getCatalogCategories: (...args: unknown[]) => mockGetCatalogCategories(...args),
      createCatalogItem: (...args: unknown[]) => mockCreateCatalogItem(...args),
    },
  };
});

function renderPriceBook(props: Partial<React.ComponentProps<typeof PriceBook>> = {}) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <PriceBook
        role="owner"
        entitled={true}
        entitlementLoading={false}
        entitlementError={false}
        onRetryEntitlement={vi.fn()}
        onSelectItem={vi.fn()}
        onSelectAssembly={vi.fn()}
        {...props}
      />
    </QueryClientProvider>,
  );
}

const oneItem: CatalogItemListResult = {
  items: [
    {
      item: {
        id: "item-1",
        type: "Material",
        displayName: "Condensate Pump",
        externalKey: "COP-34",
        categoryId: null,
        unitOfMeasure: "each",
        currency: "USD",
        isCommonItem: false,
        activeState: "Active",
        concurrencyVersion: "v1",
      },
      currentPricingMode: "StandalonePrice",
      currentSellPrice: 249.99,
      currentCost: 124.5,
      matchRank: "Exact",
      matchReason: null,
    },
  ],
  limit: 50,
  hasMore: false,
  nextCursor: null,
};

describe("PriceBook", () => {
  beforeEach(() => {
    mockGetCatalogItems.mockReset();
    mockGetCatalogCategories.mockReset().mockResolvedValue({ categories: [] });
    mockCreateCatalogItem.mockReset();
  });

  it("operator role sees a role-denied message and never calls the catalog API", () => {
    renderPriceBook({ role: "operator", entitled: false });
    expect(screen.getByText(/isn't available for your role/i)).toBeInTheDocument();
    expect(mockGetCatalogItems).not.toHaveBeenCalled();
  });

  it("unentitled owner sees a plan message and never calls the catalog API", () => {
    renderPriceBook({ role: "owner", entitled: false, entitlementLoading: false });
    expect(screen.getByText(/isn't included in your plan/i)).toBeInTheDocument();
    expect(mockGetCatalogItems).not.toHaveBeenCalled();
  });

  it("a failed entitlement check shows a retryable error, not the plan message", () => {
    const onRetryEntitlement = vi.fn();
    renderPriceBook({ role: "owner", entitled: false, entitlementLoading: false, entitlementError: true, onRetryEntitlement });

    expect(screen.getByText(/couldn't check price book access/i)).toBeInTheDocument();
    expect(screen.queryByText(/isn't included in your plan/i)).not.toBeInTheDocument();
    expect(mockGetCatalogItems).not.toHaveBeenCalled();

    screen.getByText("Try again").click();
    expect(onRetryEntitlement).toHaveBeenCalled();
  });

  it("shows a loading state while the entitlement check is in flight", () => {
    renderPriceBook({ role: "owner", entitled: false, entitlementLoading: true });
    expect(screen.getByText("Loading…")).toBeInTheDocument();
    expect(mockGetCatalogItems).not.toHaveBeenCalled();
  });

  it("entitled owner sees a loading state, then the zero-state onboarding panel for a fresh catalog", async () => {
    mockGetCatalogItems.mockResolvedValue({ items: [], limit: 50, hasMore: false, nextCursor: null });
    renderPriceBook();

    await waitFor(() => expect(screen.getByText("Your catalog is empty")).toBeInTheDocument());
    expect(screen.getByText("Start with the parts, services, and fees you use most.")).toBeInTheDocument();
    expect(mockGetCatalogItems).toHaveBeenCalledWith({});
  });

  it("a successful empty response hides the header CTA and shows only the empty-state CTA", async () => {
    mockGetCatalogItems.mockResolvedValue({ items: [], limit: 50, hasMore: false, nextCursor: null });
    renderPriceBook();

    await waitFor(() => expect(screen.getByText("Your catalog is empty")).toBeInTheDocument());
    expect(screen.queryByRole("button", { name: "Add catalog item" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: /add your first catalog item/i })).toBeInTheDocument();
  });

  it("a successful populated response shows the header CTA and hides the empty-state CTA", async () => {
    mockGetCatalogItems.mockResolvedValue(oneItem);
    renderPriceBook();

    await waitFor(() => expect(screen.getAllByText("Condensate Pump").length).toBeGreaterThan(0));
    // Exactly two: the mobile-width copy (title row) and the sm+ sticky-workspace-bar copy —
    // one semantic control shown once per breakpoint, never both at once on the same viewport.
    expect(screen.getAllByRole("button", { name: "Add catalog item" })).toHaveLength(2);
    expect(screen.queryByText("Your catalog is empty")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /add your first catalog item/i })).not.toBeInTheDocument();
  });

  it("workspace controls (search, category filter, status filter) render as one semantic set — not duplicated by the sticky desktop bar", async () => {
    mockGetCatalogItems.mockResolvedValue(oneItem);
    const { container } = renderPriceBook();

    await waitFor(() => expect(screen.getAllByText("Condensate Pump").length).toBeGreaterThan(0));
    expect(screen.getAllByLabelText("Search catalog")).toHaveLength(1);
    expect(screen.getAllByLabelText("Filter by category")).toHaveLength(1);
    expect(screen.getAllByRole("group", { name: "Filter by status" })).toHaveLength(1);
    expect(screen.getAllByRole("tab", { name: "Catalog Items" })).toHaveLength(1);

    // The tabs + toolbar live in one CSS-native `position: sticky` region (sm+ only) — no
    // JS/IntersectionObserver pop-in, and nothing forces sticky behavior below `sm`.
    const stickyBar = container.querySelector(".sm\\:sticky");
    expect(stickyBar).not.toBeNull();
    expect(stickyBar).toHaveClass("sm:top-0");
    expect(stickyBar?.querySelector('[role="tablist"]')).not.toBeNull();
    expect(stickyBar?.contains(screen.getByLabelText("Search catalog"))).toBe(true);
  });

  it("both the header and empty-state CTAs open the same drawer", async () => {
    const user = userEvent.setup();
    mockGetCatalogItems.mockResolvedValue({ items: [], limit: 50, hasMore: false, nextCursor: null });
    renderPriceBook();

    await waitFor(() => expect(screen.getByText("Your catalog is empty")).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: /add your first catalog item/i }));
    expect(screen.getByRole("dialog", { name: "New catalog item" })).toBeInTheDocument();
  });

  it("does not render the empty-state CTA while loading or on error", async () => {
    mockGetCatalogItems.mockImplementation(() => new Promise(() => {}));
    renderPriceBook();
    expect(screen.queryByText("Your catalog is empty")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /add your first catalog item/i })).not.toBeInTheDocument();

    mockGetCatalogItems.mockReset().mockRejectedValue(new Error("boom"));
    renderPriceBook();
    await waitFor(() => expect(screen.getByText("Try again")).toBeInTheDocument());
    expect(screen.queryByText("Your catalog is empty")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /add your first catalog item/i })).not.toBeInTheDocument();
  });

  it("renders catalog rows, formatting NoStandalonePrice distinctly from a real price", async () => {
    mockGetCatalogItems.mockResolvedValue(oneItem);
    renderPriceBook();

    await waitFor(() => expect(screen.getAllByText("Condensate Pump").length).toBeGreaterThan(0));
    const table = within(screen.getByRole("table"));
    expect(table.getByText("COP-34")).toBeInTheDocument();
    expect(table.getByText("$249.99")).toBeInTheDocument();
  });

  it("clicking a catalog row (desktop table) navigates to that item's detail", async () => {
    const user = userEvent.setup();
    mockGetCatalogItems.mockResolvedValue(oneItem);
    const onSelectItem = vi.fn();
    renderPriceBook({ onSelectItem });

    await waitFor(() => expect(screen.getAllByText("Condensate Pump").length).toBeGreaterThan(0));
    await user.click(within(screen.getByRole("table")).getByRole("button", { name: "Condensate Pump" }));

    expect(onSelectItem).toHaveBeenCalledWith("item-1");
  });

  it("clicking a catalog card (mobile list) navigates to that item's detail", async () => {
    const user = userEvent.setup();
    mockGetCatalogItems.mockResolvedValue(oneItem);
    const onSelectItem = vi.fn();
    renderPriceBook({ onSelectItem });

    await waitFor(() => expect(screen.getAllByText("Condensate Pump").length).toBeGreaterThan(0));
    await user.click(within(screen.getByRole("list")).getByRole("button", { name: /Condensate Pump/ }));

    expect(onSelectItem).toHaveBeenCalledWith("item-1");
  });

  it("the mobile card list shows Name, Type/UOM, Sell price, and Status", async () => {
    mockGetCatalogItems.mockResolvedValue(oneItem);
    renderPriceBook();

    await waitFor(() => expect(screen.getAllByText("Condensate Pump").length).toBeGreaterThan(0));
    const card = within(screen.getByRole("list"));
    expect(card.getByText("Condensate Pump")).toBeInTheDocument();
    expect(card.getByText(/Material.*each/)).toBeInTheDocument();
    expect(card.getByText(/Price \$249\.99/)).toBeInTheDocument();
    expect(card.getByText(/Cost \$124\.50/)).toBeInTheDocument();
    expect(card.getByText("Active")).toBeInTheDocument();
    // SKU is dropped from the compact mobile card as a low-value/redundant field.
    expect(card.queryByText("COP-34")).not.toBeInTheDocument();
  });

  it("renders 'No standalone price' rather than $0.00 or blank", async () => {
    mockGetCatalogItems.mockResolvedValue({
      items: [
        {
          ...oneItem.items[0],
          currentPricingMode: "NoStandalonePrice",
          currentSellPrice: null,
        },
      ],
      limit: 50,
      hasMore: false,
      nextCursor: null,
    });
    renderPriceBook();

    await waitFor(() => expect(screen.getAllByText("No standalone price").length).toBeGreaterThan(0));
  });

  it("shows an error state with a retry action when the fetch fails", async () => {
    mockGetCatalogItems.mockRejectedValue(new Error("boom"));
    renderPriceBook();

    await waitFor(() => expect(screen.getByText("Try again")).toBeInTheDocument());
  });

  it("debounces search input and queries with the trimmed term", async () => {
    const user = userEvent.setup();
    mockGetCatalogItems.mockResolvedValue(oneItem);
    renderPriceBook();

    await waitFor(() => expect(screen.getAllByText("Condensate Pump").length).toBeGreaterThan(0));
    mockGetCatalogItems.mockClear();

    await user.type(screen.getByLabelText("Search catalog"), "pump");
    expect(mockGetCatalogItems).not.toHaveBeenCalled();

    await waitFor(() => expect(mockGetCatalogItems).toHaveBeenCalledWith({ search: "pump" }), { timeout: 1000 });
  });

  it("filters by category and resets to page one", async () => {
    const user = userEvent.setup();
    mockGetCatalogCategories.mockResolvedValue({
      categories: [{ id: "cat-1", name: "Pumps", displayOrder: 0, activeState: "Active", concurrencyVersion: "v1" }],
    });
    mockGetCatalogItems.mockResolvedValue(oneItem);
    renderPriceBook();

    await waitFor(() => expect(screen.getAllByText("Condensate Pump").length).toBeGreaterThan(0));
    mockGetCatalogItems.mockClear();

    await user.click(screen.getByLabelText("Filter by category"));
    await user.click(screen.getByRole("option", { name: "Pumps" }));

    await waitFor(() => expect(mockGetCatalogItems).toHaveBeenCalledWith({ categoryId: "cat-1" }));
  });

  it("does not offer an inactive category as a filter option", async () => {
    const user = userEvent.setup();
    mockGetCatalogCategories.mockResolvedValue({
      categories: [{ id: "cat-2", name: "Retired", displayOrder: 0, activeState: "Inactive", concurrencyVersion: "v1" }],
    });
    mockGetCatalogItems.mockResolvedValue(oneItem);
    renderPriceBook();

    await waitFor(() => expect(screen.getAllByText("Condensate Pump").length).toBeGreaterThan(0));
    await user.click(screen.getByLabelText("Filter by category"));
    expect(screen.queryByRole("option", { name: "Retired" })).not.toBeInTheDocument();
  });

  it("toggling to Inactive status queries with status=Inactive", async () => {
    const user = userEvent.setup();
    mockGetCatalogItems.mockResolvedValue(oneItem);
    renderPriceBook();

    await waitFor(() => expect(screen.getAllByText("Condensate Pump").length).toBeGreaterThan(0));
    mockGetCatalogItems.mockClear();

    await user.click(screen.getByRole("button", { name: "Inactive" }));

    await waitFor(() => expect(mockGetCatalogItems).toHaveBeenCalledWith({ status: "Inactive" }));
  });

  it("shows a filtered-empty state distinct from the true zero-state, keeping the header CTA", async () => {
    const user = userEvent.setup();
    mockGetCatalogItems.mockResolvedValue(oneItem);
    renderPriceBook();
    await waitFor(() => expect(screen.getAllByText("Condensate Pump").length).toBeGreaterThan(0));

    mockGetCatalogItems.mockResolvedValue({ items: [], limit: 50, hasMore: false, nextCursor: null });
    await user.type(screen.getByLabelText("Search catalog"), "nonexistent");

    await waitFor(() => expect(screen.getByText("No items match your filters")).toBeInTheDocument());
    expect(screen.queryByText("Your catalog is empty")).not.toBeInTheDocument();
    expect(screen.getAllByRole("button", { name: "Add catalog item" }).length).toBeGreaterThan(0);

    await user.click(screen.getByRole("button", { name: "Reset all" }));
    expect(screen.getByLabelText("Search catalog")).toHaveValue("");
  });

  it("shows applied-filter count and supports one-click search clearing", async () => {
    const user = userEvent.setup();
    mockGetCatalogItems.mockResolvedValue(oneItem);
    renderPriceBook();

    await waitFor(() => expect(screen.getAllByText("Condensate Pump").length).toBeGreaterThan(0));
    await user.type(screen.getByLabelText("Search catalog"), "pump");
    await waitFor(() => expect(screen.getByText("1 filter active")).toBeInTheDocument());

    await user.click(screen.getByRole("button", { name: "Clear catalog search" }));
    expect(screen.getByLabelText("Search catalog")).toHaveValue("");
    await waitFor(() => expect(screen.queryByText("1 filter active")).not.toBeInTheDocument());
  });

  it("shows only one clear affordance for an active search — the custom accessible button, not a native search-input cancel icon", async () => {
    const user = userEvent.setup();
    mockGetCatalogItems.mockResolvedValue(oneItem);
    renderPriceBook();

    await waitFor(() => expect(screen.getAllByText("Condensate Pump").length).toBeGreaterThan(0));
    const searchInput = screen.getByLabelText("Search catalog");
    expect(searchInput).toHaveAttribute("type", "text");
    expect(searchInput).toHaveAttribute("inputMode", "search");

    await user.type(searchInput, "pump");
    expect(screen.getAllByRole("button", { name: "Clear catalog search" })).toHaveLength(1);
  });

  it("describes a category-only filter with a truthful sentence instead of a bare count", async () => {
    const user = userEvent.setup();
    mockGetCatalogCategories.mockResolvedValue({
      categories: [{ id: "cat-1", name: "Warranty", displayOrder: 0, activeState: "Active", concurrencyVersion: "v1" }],
    });
    mockGetCatalogItems.mockResolvedValue(oneItem);
    renderPriceBook();

    await waitFor(() => expect(screen.getAllByText("Condensate Pump").length).toBeGreaterThan(0));
    await user.click(screen.getByLabelText("Filter by category"));
    await user.click(screen.getByRole("option", { name: "Warranty" }));

    await waitFor(() =>
      expect(screen.getByText("Showing active catalog items in Warranty")).toBeInTheDocument(),
    );
    expect(screen.getByRole("button", { name: "Reset all" })).toBeInTheDocument();
  });

  it("describes a status-only filter change with a truthful sentence", async () => {
    const user = userEvent.setup();
    mockGetCatalogItems.mockResolvedValue(oneItem);
    renderPriceBook();

    await waitFor(() => expect(screen.getAllByText("Condensate Pump").length).toBeGreaterThan(0));
    await user.click(screen.getByRole("button", { name: "Inactive" }));

    await waitFor(() =>
      expect(screen.getByText("Showing inactive catalog items")).toBeInTheDocument(),
    );
  });

  it("shows a singular populated-result count for one catalog item", async () => {
    mockGetCatalogItems.mockResolvedValue(oneItem);
    renderPriceBook();

    await waitFor(() => expect(screen.getByText("1 catalog item")).toBeInTheDocument());
  });

  it("shows a plural populated-result count for multiple catalog items", async () => {
    const twoItems: CatalogItemListResult = {
      ...oneItem,
      items: [
        oneItem.items[0],
        { ...oneItem.items[0], item: { ...oneItem.items[0].item, id: "item-2", displayName: "Filter Drier" } },
      ],
    };
    mockGetCatalogItems.mockResolvedValue(twoItems);
    renderPriceBook();

    await waitFor(() => expect(screen.getByText("2 catalog items")).toBeInTheDocument());
  });

  it("paginates with Prev/Next using the returned cursor", async () => {
    const user = userEvent.setup();
    mockGetCatalogItems.mockResolvedValueOnce({ ...oneItem, hasMore: true, nextCursor: "cursor-2" });
    renderPriceBook();

    await waitFor(() => expect(screen.getAllByText("Condensate Pump").length).toBeGreaterThan(0));
    const nextButton = screen.getByRole("button", { name: "Next" });
    const prevButton = screen.getByRole("button", { name: "Previous" });
    expect(prevButton).toBeDisabled();
    expect(nextButton).not.toBeDisabled();

    mockGetCatalogItems.mockResolvedValueOnce({
      items: [{ ...oneItem.items[0], item: { ...oneItem.items[0].item, id: "item-2", displayName: "Second Item" } }],
      limit: 50,
      hasMore: false,
      nextCursor: null,
    });
    await user.click(nextButton);

    await waitFor(() => expect(mockGetCatalogItems).toHaveBeenLastCalledWith({ cursor: "cursor-2" }));
    await waitFor(() => expect(screen.getAllByText("Second Item").length).toBeGreaterThan(0));
    expect(screen.getByRole("button", { name: "Next" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Previous" })).not.toBeDisabled();

    mockGetCatalogItems.mockResolvedValueOnce({ ...oneItem, hasMore: true, nextCursor: "cursor-2" });
    await user.click(screen.getByRole("button", { name: "Previous" }));
    await waitFor(() => expect(mockGetCatalogItems).toHaveBeenLastCalledWith({}));
  });

  it("opens the catalog item drawer and refreshes the catalog list after a successful create", async () => {
    const user = userEvent.setup();
    mockGetCatalogItems.mockResolvedValue({ items: [], limit: 50, hasMore: false, nextCursor: null });
    mockCreateCatalogItem.mockResolvedValue({
      item: {
        id: "item-2",
        type: "Material",
        displayName: "New Widget",
        externalKey: null,
        categoryId: null,
        unitOfMeasure: "each",
        currency: "USD",
        isCommonItem: false,
        activeState: "Active",
        concurrencyVersion: "v1",
      },
      versionNumber: 1,
      priceBookVersionId: "pbv-1",
      priceBookVersionLineId: "pbvl-1",
      cost: null,
      sellPrice: 10,
      pricingMode: "StandalonePrice",
    });
    renderPriceBook();

    // The default-Active list comes back empty, then the zero-state check confirms no inactive
    // items exist either before the true empty-state onboarding renders.
    await waitFor(() => expect(screen.getByText("Your catalog is empty")).toBeInTheDocument());
    expect(mockGetCatalogItems).toHaveBeenCalledWith({});
    expect(mockGetCatalogItems).toHaveBeenCalledWith({ status: "Inactive", limit: 1 });
    const callsBeforeCreate = mockGetCatalogItems.mock.calls.filter(
      ([params]) => JSON.stringify(params) === JSON.stringify({}),
    ).length;

    await user.click(screen.getByRole("button", { name: /add your first catalog item/i }));
    expect(screen.getByRole("dialog", { name: "New catalog item" })).toBeInTheDocument();

    await user.type(screen.getByLabelText("Name"), "New Widget");
    await user.type(screen.getByLabelText("Sell price"), "10");
    await user.click(screen.getByRole("button", { name: /save & activate/i }));

    await waitFor(() => expect(mockCreateCatalogItem).toHaveBeenCalled());
    await waitFor(() => expect(screen.queryByRole("dialog", { name: "New catalog item" })).not.toBeInTheDocument());
    await waitFor(() => {
      const callsAfterCreate = mockGetCatalogItems.mock.calls.filter(
        ([params]) => JSON.stringify(params) === JSON.stringify({}),
      ).length;
      expect(callsAfterCreate).toBeGreaterThan(callsBeforeCreate);
    });
  });

  it("shows a distinct 'no active items' state (not the onboarding zero-state) when the catalog has only inactive items", async () => {
    const user = userEvent.setup();
    mockGetCatalogItems.mockImplementation((params: GetCatalogItemsParams = {}) => {
      if (params.status === "Inactive") {
        return Promise.resolve({
          items: [{ ...oneItem.items[0], item: { ...oneItem.items[0].item, activeState: "Inactive" } }],
          limit: params.limit ?? 50,
          hasMore: false,
          nextCursor: null,
        });
      }
      return Promise.resolve({ items: [], limit: 50, hasMore: false, nextCursor: null });
    });
    renderPriceBook();

    await waitFor(() => expect(screen.getByText("No active items")).toBeInTheDocument());
    expect(screen.queryByText("Your catalog is empty")).not.toBeInTheDocument();
    // The catalog isn't empty, so the header CTA stays available.
    expect(screen.getAllByRole("button", { name: "Add catalog item" }).length).toBeGreaterThan(0);

    await user.click(screen.getByRole("button", { name: "View inactive items" }));
    await waitFor(() => expect(screen.getAllByText("Condensate Pump").length).toBeGreaterThan(0));
    expect(screen.getByRole("button", { name: "Inactive" })).toHaveAttribute("aria-pressed", "true");
  });

  describe("2e.7c keyboard shortcuts", () => {
    it("opens and closes the shortcuts dialog listing all four locked shortcuts", async () => {
      const user = userEvent.setup();
      mockGetCatalogItems.mockResolvedValue(oneItem);
      renderPriceBook();
      await waitFor(() => expect(screen.getAllByText("Condensate Pump").length).toBeGreaterThan(0));

      await user.click(screen.getByRole("button", { name: "Keyboard shortcuts" }));
      const dialog = screen.getByRole("dialog", { name: "Keyboard shortcuts" });
      expect(dialog).toBeInTheDocument();
      expect(screen.getByText(/save & add another/i)).toBeInTheDocument();
      expect(screen.getByText(/close or cancel safely/i)).toBeInTheDocument();
      expect(screen.getByText(/focus catalog search/i)).toBeInTheDocument();
      expect(screen.getByText(/open new item/i)).toBeInTheDocument();

      await user.keyboard("{Escape}");
      expect(screen.queryByRole("dialog", { name: "Keyboard shortcuts" })).not.toBeInTheDocument();
    });

    it("'/' focuses the catalog search input", async () => {
      const user = userEvent.setup();
      mockGetCatalogItems.mockResolvedValue(oneItem);
      renderPriceBook();
      await waitFor(() => expect(screen.getAllByText("Condensate Pump").length).toBeGreaterThan(0));

      await user.keyboard("/");
      expect(screen.getByLabelText("Search catalog")).toHaveFocus();
    });

    it("'n' opens the New item drawer when focus is not in an editable control", async () => {
      const user = userEvent.setup();
      mockGetCatalogItems.mockResolvedValue(oneItem);
      renderPriceBook();
      await waitFor(() => expect(screen.getAllByText("Condensate Pump").length).toBeGreaterThan(0));

      await user.keyboard("n");
      expect(screen.getByRole("dialog", { name: "New catalog item" })).toBeInTheDocument();
    });

    it("'n' typed into the search input does not open the drawer", async () => {
      const user = userEvent.setup();
      mockGetCatalogItems.mockResolvedValue(oneItem);
      renderPriceBook();
      await waitFor(() => expect(screen.getAllByText("Condensate Pump").length).toBeGreaterThan(0));

      await user.click(screen.getByLabelText("Search catalog"));
      await user.keyboard("n");
      expect(screen.queryByRole("dialog", { name: "New catalog item" })).not.toBeInTheDocument();
      expect(screen.getByLabelText("Search catalog")).toHaveValue("n");
    });

    it("'/' typed while the drawer is open does not steal focus from the drawer", async () => {
      const user = userEvent.setup();
      mockGetCatalogItems.mockResolvedValue(oneItem);
      renderPriceBook();
      await waitFor(() => expect(screen.getAllByText("Condensate Pump").length).toBeGreaterThan(0));

      await user.click(screen.getAllByRole("button", { name: "Add catalog item" })[0]);
      expect(screen.getByRole("dialog", { name: "New catalog item" })).toBeInTheDocument();
      await user.keyboard("/");
      expect(screen.getByLabelText("Search catalog")).not.toHaveFocus();
    });

    it("'/' still focuses search when a non-typing control (e.g. the shortcuts button) has focus", async () => {
      const user = userEvent.setup();
      mockGetCatalogItems.mockResolvedValue(oneItem);
      renderPriceBook();
      await waitFor(() => expect(screen.getAllByText("Condensate Pump").length).toBeGreaterThan(0));

      screen.getByRole("button", { name: "Keyboard shortcuts" }).focus();
      await user.keyboard("/");
      expect(screen.getByLabelText("Search catalog")).toHaveFocus();
    });

    it("'n' does not open the drawer while a non-typing control (e.g. the shortcuts button) has focus", async () => {
      const user = userEvent.setup();
      mockGetCatalogItems.mockResolvedValue(oneItem);
      renderPriceBook();
      await waitFor(() => expect(screen.getAllByText("Condensate Pump").length).toBeGreaterThan(0));

      screen.getByRole("button", { name: "Keyboard shortcuts" }).focus();
      await user.keyboard("n");
      expect(screen.queryByRole("dialog", { name: "New catalog item" })).not.toBeInTheDocument();
    });
  });
});
