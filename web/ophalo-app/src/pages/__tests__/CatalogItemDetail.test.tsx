import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { CatalogItemDetail } from "../CatalogItemDetail";
import { ApiError } from "../../lib/apiClient";
import type { CatalogItemDetailResult } from "../../lib/apiClient";

const mockGetCatalogItem = vi.fn();
const mockGetCatalogCategories = vi.fn();
const mockUpdateCatalogItemHeader = vi.fn();

vi.mock("../../lib/apiClient", async () => {
  const actual = await vi.importActual<typeof import("../../lib/apiClient")>("../../lib/apiClient");
  return {
    ...actual,
    api: {
      ...actual.api,
      getCatalogItem: (...args: unknown[]) => mockGetCatalogItem(...args),
      getCatalogCategories: (...args: unknown[]) => mockGetCatalogCategories(...args),
      updateCatalogItemHeader: (...args: unknown[]) => mockUpdateCatalogItemHeader(...args),
    },
  };
});

function renderDetail(props: Partial<React.ComponentProps<typeof CatalogItemDetail>> = {}) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const onBack = vi.fn();
  const onRetryEntitlement = vi.fn();
  const utils = render(
    <QueryClientProvider client={queryClient}>
      <CatalogItemDetail
        catalogItemId="item-1"
        role="owner"
        entitled={true}
        entitlementLoading={false}
        entitlementError={false}
        onRetryEntitlement={onRetryEntitlement}
        onBack={onBack}
        {...props}
      />
    </QueryClientProvider>,
  );
  return { ...utils, onBack, onRetryEntitlement };
}

const baseItem: CatalogItemDetailResult = {
  item: {
    id: "item-1",
    type: "Material",
    displayName: "Condensate Pump",
    externalKey: "COP-34",
    categoryId: "cat-1",
    unitOfMeasure: "each",
    currency: "USD",
    isCommonItem: false,
    activeState: "Active",
    concurrencyVersion: "v1",
  },
  aliases: [{ id: "alias-1", aliasText: "condensate pump", activeState: "Active" }],
  category: { id: "cat-1", name: "Refrigerant", displayOrder: 0, activeState: "Active", concurrencyVersion: "v1" },
  currentPricingMode: "StandalonePrice",
  currentSellPrice: 250,
  currentCost: 125,
};

describe("CatalogItemDetail", () => {
  beforeEach(() => {
    mockGetCatalogItem.mockReset();
    mockGetCatalogCategories.mockReset();
    mockGetCatalogCategories.mockResolvedValue({
      categories: [
        { id: "cat-1", name: "Refrigerant", displayOrder: 0, activeState: "Active", concurrencyVersion: "v1" },
        { id: "cat-2", name: "Fittings", displayOrder: 1, activeState: "Active", concurrencyVersion: "v1" },
      ],
    });
    mockUpdateCatalogItemHeader.mockReset();
  });

  it("shows a loading state before the fetch resolves", () => {
    mockGetCatalogItem.mockReturnValue(new Promise(() => {}));
    renderDetail();

    expect(screen.getByText("Loading…")).toBeInTheDocument();
  });

  it("an operator sees a role-denied message and never calls the API", () => {
    renderDetail({ role: "operator" });

    expect(screen.getByText("Price Book isn't available for your role")).toBeInTheDocument();
    expect(mockGetCatalogItem).not.toHaveBeenCalled();
  });

  it("a direct-route Owner/Admin without the entitlement sees the plan message and never calls the API", () => {
    renderDetail({ entitled: false });

    expect(screen.getByText("Price Book isn't included in your plan")).toBeInTheDocument();
    expect(mockGetCatalogItem).not.toHaveBeenCalled();
  });

  it("a direct-route entitlement check in flight shows loading and never calls the API", () => {
    renderDetail({ entitled: false, entitlementLoading: true });

    expect(screen.getByText("Loading…")).toBeInTheDocument();
    expect(mockGetCatalogItem).not.toHaveBeenCalled();
  });

  it("a direct-route failed entitlement check shows a retryable error, not the plan message", () => {
    const { onRetryEntitlement } = renderDetail({ entitled: false, entitlementError: true });

    expect(screen.getByText("Couldn't check Price Book access")).toBeInTheDocument();
    expect(screen.queryByText("Price Book isn't included in your plan")).not.toBeInTheDocument();
    expect(mockGetCatalogItem).not.toHaveBeenCalled();

    screen.getByRole("button", { name: "Try again" }).click();
    expect(onRetryEntitlement).toHaveBeenCalled();
  });

  it("a 403 from the server (stale client-side entitlement) shows a generic error, not the not-found message", async () => {
    mockGetCatalogItem.mockRejectedValue(new ApiError(403, "CatalogItem.Forbidden", "forbidden"));
    renderDetail();

    await waitFor(() => expect(screen.getByText("Couldn't load this catalog item.")).toBeInTheDocument());
    expect(screen.queryByText("This catalog item couldn't be found.")).not.toBeInTheDocument();
  });

  it("a 404 shows a not-found message", async () => {
    mockGetCatalogItem.mockRejectedValue(new ApiError(404, "CatalogItem.NotFound", "not found"));
    renderDetail();

    await waitFor(() => expect(screen.getByText("This catalog item couldn't be found.")).toBeInTheDocument());
  });

  it("a non-404 failure shows a generic error, not the not-found message", async () => {
    mockGetCatalogItem.mockRejectedValue(new ApiError(500, undefined, "server error"));
    renderDetail();

    await waitFor(() => expect(screen.getByText("Couldn't load this catalog item.")).toBeInTheDocument());
    expect(screen.queryByText("This catalog item couldn't be found.")).not.toBeInTheDocument();
  });

  it("renders header, current price, and derived profitability from Cost/Sell Price", async () => {
    mockGetCatalogItem.mockResolvedValue(baseItem);
    renderDetail();

    await waitFor(() => expect(screen.getByText("Condensate Pump")).toBeInTheDocument());
    expect(screen.getByText("Material · Refrigerant")).toBeInTheDocument();
    expect(screen.getByText("$250.00")).toBeInTheDocument();
    // Cost ($125.00) and gross profit (250 - 125 = $125.00) both render this value.
    expect(screen.getAllByText("$125.00")).toHaveLength(2);
    expect(screen.getByText("50%")).toBeInTheDocument();
    expect(screen.getByText("100%")).toBeInTheDocument();
  });

  it("renders 'No standalone price', not $0.00, when the item has no standalone price", async () => {
    mockGetCatalogItem.mockResolvedValue({
      ...baseItem,
      currentPricingMode: "NoStandalonePrice",
      currentSellPrice: null,
    });
    renderDetail();

    await waitFor(() => expect(screen.getByText("No standalone price")).toBeInTheDocument());
  });

  it("shows profitability as unavailable when Cost is missing", async () => {
    mockGetCatalogItem.mockResolvedValue({ ...baseItem, currentCost: null });
    renderDetail();

    await waitFor(() =>
      expect(
        screen.getByText("Profitability is unavailable until both Cost and Sell Price are set."),
      ).toBeInTheDocument(),
    );
  });

  it("keeps gross profit and margin valid but marks markup unavailable when Cost is zero", async () => {
    mockGetCatalogItem.mockResolvedValue({ ...baseItem, currentCost: 0, currentSellPrice: 100 });
    renderDetail();

    await waitFor(() => expect(screen.getByText("Condensate Pump")).toBeInTheDocument());
    // Sell price ($100.00) and gross profit (100 - 0 = $100.00) both render this value.
    expect(screen.getAllByText("$100.00")).toHaveLength(2);
    expect(screen.getByText("100%")).toBeInTheDocument();
    expect(screen.getAllByText("Unavailable")).toHaveLength(1);
  });

  it("editing the header pre-fills the form and saves the mutable fields with the current version", async () => {
    const user = userEvent.setup();
    mockGetCatalogItem.mockResolvedValue(baseItem);
    mockUpdateCatalogItemHeader.mockResolvedValue({ concurrencyVersion: "v2" });
    renderDetail();

    await waitFor(() => expect(screen.getByText("Condensate Pump")).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: "Edit" }));

    const nameInput = screen.getByLabelText("Name") as HTMLInputElement;
    expect(nameInput.value).toBe("Condensate Pump");
    await user.clear(nameInput);
    await user.type(nameInput, "Condensate Pump Mk2");
    await user.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(mockUpdateCatalogItemHeader).toHaveBeenCalledWith(
      "item-1",
      { displayName: "Condensate Pump Mk2", externalKey: "COP-34", categoryId: "cat-1", isCommonItem: false },
      "v1",
    ));
  });

  it("a version conflict on save shows the refreshed values read-only, then restores the draft on re-Edit and saves with the fresh version", async () => {
    const user = userEvent.setup();
    const updatedByOtherEditor: CatalogItemDetailResult = {
      ...baseItem,
      item: { ...baseItem.item, displayName: "Condensate Pump (renamed by teammate)", concurrencyVersion: "v2" },
    };
    mockGetCatalogItem.mockResolvedValueOnce(baseItem).mockResolvedValue(updatedByOtherEditor);
    mockUpdateCatalogItemHeader
      .mockRejectedValueOnce(new ApiError(409, "CatalogItem.VersionMismatch", "conflict"))
      .mockResolvedValue({ concurrencyVersion: "v3" });
    renderDetail();

    await waitFor(() => expect(screen.getByText("Condensate Pump")).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: "Edit" }));

    const nameInput = screen.getByLabelText("Name") as HTMLInputElement;
    await user.clear(nameInput);
    await user.type(nameInput, "Condensate Pump Mk2");
    await user.click(screen.getByRole("button", { name: "Save" }));

    // Conflict: the form unmounts, and the read-only view shows the teammate's latest value —
    // the user must see it before any resave, not resubmit blind against a stale form.
    await waitFor(() =>
      expect(screen.getByText("Condensate Pump (renamed by teammate)")).toBeInTheDocument(),
    );
    expect(screen.getByText(/This item was changed by someone else/)).toBeInTheDocument();
    expect(screen.queryByLabelText("Name")).not.toBeInTheDocument();

    // Re-entering Edit restores the unsaved draft rather than re-seeding from the refreshed item.
    await user.click(screen.getByRole("button", { name: "Edit" }));
    const restoredNameInput = screen.getByLabelText("Name") as HTMLInputElement;
    expect(restoredNameInput.value).toBe("Condensate Pump Mk2");

    await user.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(mockUpdateCatalogItemHeader).toHaveBeenLastCalledWith(
      "item-1",
      { displayName: "Condensate Pump Mk2", externalKey: "COP-34", categoryId: "cat-1", isCommonItem: false },
      "v2",
    ));
  });

  it("disables Edit until the conflict-triggered refetch lands, blocking a resave against the stale version", async () => {
    const user = userEvent.setup();
    const updatedByOtherEditor: CatalogItemDetailResult = {
      ...baseItem,
      item: { ...baseItem.item, displayName: "Condensate Pump (renamed by teammate)", concurrencyVersion: "v2" },
    };
    let resolveRefetch: (value: CatalogItemDetailResult) => void;
    const deferredRefetch = new Promise<CatalogItemDetailResult>((resolve) => {
      resolveRefetch = resolve;
    });
    mockGetCatalogItem.mockResolvedValueOnce(baseItem).mockReturnValueOnce(deferredRefetch);
    mockUpdateCatalogItemHeader
      .mockRejectedValueOnce(new ApiError(409, "CatalogItem.VersionMismatch", "conflict"))
      .mockResolvedValue({ concurrencyVersion: "v3" });
    renderDetail();

    await waitFor(() => expect(screen.getByText("Condensate Pump")).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: "Edit" }));

    const nameInput = screen.getByLabelText("Name") as HTMLInputElement;
    await user.clear(nameInput);
    await user.type(nameInput, "Condensate Pump Mk2");
    await user.click(screen.getByRole("button", { name: "Save" }));

    const editButton = await screen.findByRole("button", { name: "Refreshing…" });
    expect(editButton).toBeDisabled();

    await user.click(editButton);
    expect(mockUpdateCatalogItemHeader).toHaveBeenCalledTimes(1);
    expect(screen.queryByLabelText("Name")).not.toBeInTheDocument();

    resolveRefetch!(updatedByOtherEditor);

    await waitFor(() => expect(screen.getByRole("button", { name: "Edit" })).toBeEnabled());
    expect(screen.getByText("Condensate Pump (renamed by teammate)")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Edit" }));
    await user.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(mockUpdateCatalogItemHeader).toHaveBeenLastCalledWith(
      "item-1",
      { displayName: "Condensate Pump Mk2", externalKey: "COP-34", categoryId: "cat-1", isCommonItem: false },
      "v2",
    ));
  });
});
