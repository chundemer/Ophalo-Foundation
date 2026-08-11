import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { OfferingAssemblyDrawer } from "../OfferingAssemblyDrawer";
import type { CatalogItemListResult } from "../../../lib/apiClient";

const mockGetCatalogItems = vi.fn();
const mockCreateOfferingAssembly = vi.fn();

vi.mock("../../../lib/apiClient", async () => {
  const actual = await vi.importActual<typeof import("../../../lib/apiClient")>("../../../lib/apiClient");
  return {
    ...actual,
    api: {
      ...actual.api,
      getCatalogItems: (...args: unknown[]) => mockGetCatalogItems(...args),
      createOfferingAssembly: (...args: unknown[]) => mockCreateOfferingAssembly(...args),
    },
  };
});

function renderDrawer(props: Partial<React.ComponentProps<typeof OfferingAssemblyDrawer>> = {}) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const onClose = vi.fn();
  const onCreated = vi.fn();
  const utils = render(
    <QueryClientProvider client={queryClient}>
      <OfferingAssemblyDrawer onClose={onClose} onCreated={onCreated} {...props} />
    </QueryClientProvider>,
  );
  return { ...utils, onClose, onCreated };
}

const catalogPage: CatalogItemListResult = {
  items: [
    {
      item: { id: "item-primary", type: "Service", displayName: "Furnace Inspection", externalKey: null, categoryId: null, unitOfMeasure: "each", currency: "USD", isCommonItem: false, activeState: "Active", concurrencyVersion: "v1" },
      currentPricingMode: "StandalonePrice",
      currentSellPrice: 100,
      matchRank: "DisplayName",
      matchReason: null,
    },
    {
      item: { id: "item-b", type: "Material", displayName: "Filter", externalKey: null, categoryId: null, unitOfMeasure: "each", currency: "USD", isCommonItem: false, activeState: "Active", concurrencyVersion: "v1" },
      currentPricingMode: "StandalonePrice",
      currentSellPrice: 20,
      matchRank: "DisplayName",
      matchReason: null,
    },
    {
      item: { id: "item-c", type: "Material", displayName: "Belt", externalKey: null, categoryId: null, unitOfMeasure: "each", currency: "USD", isCommonItem: false, activeState: "Active", concurrencyVersion: "v1" },
      currentPricingMode: "StandalonePrice",
      currentSellPrice: 15,
      matchRank: "DisplayName",
      matchReason: null,
    },
  ],
  limit: 20,
  hasMore: false,
  nextCursor: null,
};

async function selectFromPicker(user: ReturnType<typeof userEvent.setup>, combobox: HTMLElement, optionText: string) {
  await user.click(combobox);
  const listbox = await screen.findByRole("listbox");
  await user.click(await within(listbox).findByText(optionText));
}

describe("OfferingAssemblyDrawer", () => {
  beforeEach(() => {
    mockGetCatalogItems.mockReset().mockResolvedValue(catalogPage);
    mockCreateOfferingAssembly.mockReset();
  });

  it("excludes the primary item from an associated-item picker's results", async () => {
    const user = userEvent.setup();
    renderDrawer();

    await selectFromPicker(user, screen.getAllByRole("combobox")[0], "Furnace Inspection");
    await user.click(screen.getByRole("button", { name: "+ Add item" }));

    const rowCombobox = screen.getAllByRole("combobox")[1];
    await user.click(rowCombobox);
    const listbox = await screen.findByRole("listbox");
    expect(within(listbox).queryByText("Furnace Inspection")).not.toBeInTheDocument();
    expect(within(listbox).getByText("Filter")).toBeInTheDocument();
    expect(within(listbox).getByText("Belt")).toBeInTheDocument();
  });

  it("excludes an already-selected associated item from a second associated-item row, preventing a duplicate", async () => {
    const user = userEvent.setup();
    renderDrawer();

    // Primary
    await selectFromPicker(user, screen.getAllByRole("combobox")[0], "Furnace Inspection");

    // Row 1 -> Filter
    await user.click(screen.getByRole("button", { name: "+ Add item" }));
    await selectFromPicker(user, screen.getAllByRole("combobox")[1], "Filter");

    // Row 2: Filter must not be offered again, and the primary must still be excluded too
    await user.click(screen.getByRole("button", { name: "+ Add item" }));
    const row2Combobox = screen.getAllByRole("combobox")[2];
    await user.click(row2Combobox);
    const listbox = await screen.findByRole("listbox");
    expect(within(listbox).queryByText("Filter")).not.toBeInTheDocument();
    expect(within(listbox).queryByText("Furnace Inspection")).not.toBeInTheDocument();
    expect(within(listbox).getByText("Belt")).toBeInTheDocument();
  });

  it("submits create-with-items with the selected primary, price treatment, and associated items", async () => {
    const user = userEvent.setup();
    mockCreateOfferingAssembly.mockResolvedValue({
      id: "assembly-1",
      primaryCatalogItemId: "item-primary",
      name: "Furnace Tune-Up",
      priceTreatment: "Summed",
      activeState: "Active",
      concurrencyVersion: "v1",
      items: [],
    });
    const { onCreated, onClose } = renderDrawer();

    await user.type(screen.getByLabelText("Name"), "Furnace Tune-Up");
    await selectFromPicker(user, screen.getAllByRole("combobox")[0], "Furnace Inspection");
    await user.click(screen.getByRole("button", { name: "+ Add item" }));
    await selectFromPicker(user, screen.getAllByRole("combobox")[1], "Filter");

    await user.click(screen.getByRole("button", { name: "Create" }));

    await waitFor(() =>
      expect(mockCreateOfferingAssembly).toHaveBeenCalledWith({
        primaryCatalogItemId: "item-primary",
        name: "Furnace Tune-Up",
        priceTreatment: "Summed",
        items: [{ catalogItemId: "item-b", defaultQuantity: 1, isOptional: false, displayOrder: 0 }],
      }),
    );
    await waitFor(() => expect(onCreated).toHaveBeenCalled());
    await waitFor(() => expect(onClose).toHaveBeenCalled());
  });

  it("requires a name and a primary catalog item before submitting", async () => {
    const user = userEvent.setup();
    renderDrawer();

    await user.click(screen.getByRole("button", { name: "Create" }));

    expect(screen.getByText("Name is required.")).toBeInTheDocument();
    expect(screen.getByText("A primary catalog item is required.")).toBeInTheDocument();
    expect(mockCreateOfferingAssembly).not.toHaveBeenCalled();
  });
});
