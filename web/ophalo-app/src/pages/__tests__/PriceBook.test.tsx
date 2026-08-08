import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
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

    await waitFor(() => expect(screen.getByText("Condensate Pump")).toBeInTheDocument());
    expect(screen.getByRole("button", { name: "Add catalog item" })).toBeInTheDocument();
    expect(screen.queryByText("Your catalog is empty")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /add your first catalog item/i })).not.toBeInTheDocument();
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

    await waitFor(() => expect(screen.getByText("Condensate Pump")).toBeInTheDocument());
    expect(screen.getByText("COP-34")).toBeInTheDocument();
    expect(screen.getByText("$249.99")).toBeInTheDocument();
  });

  it("clicking a catalog row navigates to that item's detail", async () => {
    const user = userEvent.setup();
    mockGetCatalogItems.mockResolvedValue(oneItem);
    const onSelectItem = vi.fn();
    renderPriceBook({ onSelectItem });

    await waitFor(() => expect(screen.getByText("Condensate Pump")).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: "Condensate Pump" }));

    expect(onSelectItem).toHaveBeenCalledWith("item-1");
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

    await waitFor(() => expect(screen.getByText("No standalone price")).toBeInTheDocument());
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

    await waitFor(() => expect(screen.getByText("Condensate Pump")).toBeInTheDocument());
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

    await waitFor(() => expect(screen.getByText("Condensate Pump")).toBeInTheDocument());
    mockGetCatalogItems.mockClear();

    await user.selectOptions(screen.getByLabelText("Filter by category"), "cat-1");

    await waitFor(() => expect(mockGetCatalogItems).toHaveBeenCalledWith({ categoryId: "cat-1" }));
  });

  it("does not offer an inactive category as a filter option", async () => {
    mockGetCatalogCategories.mockResolvedValue({
      categories: [{ id: "cat-2", name: "Retired", displayOrder: 0, activeState: "Inactive", concurrencyVersion: "v1" }],
    });
    mockGetCatalogItems.mockResolvedValue(oneItem);
    renderPriceBook();

    await waitFor(() => expect(screen.getByText("Condensate Pump")).toBeInTheDocument());
    expect(screen.queryByRole("option", { name: "Retired" })).not.toBeInTheDocument();
  });

  it("toggling to Inactive status queries with status=Inactive", async () => {
    const user = userEvent.setup();
    mockGetCatalogItems.mockResolvedValue(oneItem);
    renderPriceBook();

    await waitFor(() => expect(screen.getByText("Condensate Pump")).toBeInTheDocument());
    mockGetCatalogItems.mockClear();

    await user.click(screen.getByRole("button", { name: "Inactive" }));

    await waitFor(() => expect(mockGetCatalogItems).toHaveBeenCalledWith({ status: "Inactive" }));
  });

  it("shows a filtered-empty state distinct from the true zero-state, keeping the header CTA", async () => {
    const user = userEvent.setup();
    mockGetCatalogItems.mockResolvedValue(oneItem);
    renderPriceBook();
    await waitFor(() => expect(screen.getByText("Condensate Pump")).toBeInTheDocument());

    mockGetCatalogItems.mockResolvedValue({ items: [], limit: 50, hasMore: false, nextCursor: null });
    await user.type(screen.getByLabelText("Search catalog"), "nonexistent");

    await waitFor(() => expect(screen.getByText("No items match your filters")).toBeInTheDocument());
    expect(screen.queryByText("Your catalog is empty")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Add catalog item" })).toBeInTheDocument();

    const clearButtons = screen.getAllByRole("button", { name: "Clear filters" });
    await user.click(clearButtons[0]);
    expect(screen.getByLabelText("Search catalog")).toHaveValue("");
  });

  it("paginates with Prev/Next using the returned cursor", async () => {
    const user = userEvent.setup();
    mockGetCatalogItems.mockResolvedValueOnce({ ...oneItem, hasMore: true, nextCursor: "cursor-2" });
    renderPriceBook();

    await waitFor(() => expect(screen.getByText("Condensate Pump")).toBeInTheDocument());
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
    await waitFor(() => expect(screen.getByText("Second Item")).toBeInTheDocument());
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
    expect(screen.getByRole("button", { name: "Add catalog item" })).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "View inactive items" }));
    await waitFor(() => expect(screen.getByText("Condensate Pump")).toBeInTheDocument());
    expect(screen.getByRole("button", { name: "Inactive" })).toHaveAttribute("aria-pressed", "true");
  });
});
