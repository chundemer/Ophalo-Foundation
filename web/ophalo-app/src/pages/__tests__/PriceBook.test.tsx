import { describe, it, expect, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { PriceBook } from "../PriceBook";
import type { CatalogItemListResult } from "../../lib/apiClient";

const mockGetCatalogItems = vi.fn();

vi.mock("../../lib/apiClient", async () => {
  const actual = await vi.importActual<typeof import("../../lib/apiClient")>("../../lib/apiClient");
  return {
    ...actual,
    api: {
      ...actual.api,
      getCatalogItems: (...args: unknown[]) => mockGetCatalogItems(...args),
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

  it("entitled owner sees a loading state, then the empty state for a fresh catalog", async () => {
    mockGetCatalogItems.mockResolvedValue({ items: [], limit: 50, hasMore: false, nextCursor: null });
    renderPriceBook();

    await waitFor(() => expect(screen.getByText("No catalog items yet")).toBeInTheDocument());
    expect(mockGetCatalogItems).toHaveBeenCalledWith({});
  });

  it("renders catalog rows, formatting NoStandalonePrice distinctly from a real price", async () => {
    mockGetCatalogItems.mockResolvedValue(oneItem);
    renderPriceBook();

    await waitFor(() => expect(screen.getByText("Condensate Pump")).toBeInTheDocument());
    expect(screen.getByText("COP-34")).toBeInTheDocument();
    expect(screen.getByText("$249.99")).toBeInTheDocument();
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
});
