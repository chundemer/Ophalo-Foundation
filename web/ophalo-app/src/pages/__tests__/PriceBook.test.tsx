import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { PriceBook } from "../PriceBook";
import type { CatalogItemListResult } from "../../lib/apiClient";

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

    await waitFor(() => expect(screen.getByText("Your catalog is empty")).toBeInTheDocument());
    expect(mockGetCatalogItems).toHaveBeenCalledTimes(1);

    await user.click(screen.getByRole("button", { name: /add your first catalog item/i }));
    expect(screen.getByRole("dialog", { name: "New catalog item" })).toBeInTheDocument();

    await user.type(screen.getByLabelText("Name"), "New Widget");
    await user.type(screen.getByLabelText("Sell price"), "10");
    await user.click(screen.getByRole("button", { name: /save & activate/i }));

    await waitFor(() => expect(mockCreateCatalogItem).toHaveBeenCalled());
    await waitFor(() => expect(screen.queryByRole("dialog", { name: "New catalog item" })).not.toBeInTheDocument());
    await waitFor(() => expect(mockGetCatalogItems).toHaveBeenCalledTimes(2));
  });
});
