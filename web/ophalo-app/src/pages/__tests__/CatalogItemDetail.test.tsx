import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { CatalogItemDetail } from "../CatalogItemDetail";
import { ApiError } from "../../lib/apiClient";
import type { CatalogItemDetailResult } from "../../lib/apiClient";

const mockGetCatalogItem = vi.fn();
const mockGetCatalogCategories = vi.fn();
const mockCreateCatalogCategory = vi.fn();
const mockUpdateCatalogItemHeader = vi.fn();
const mockReactivateCatalogItem = vi.fn();
const mockInactivateCatalogItem = vi.fn();
const mockAddCatalogItemAlias = vi.fn();
const mockActivateCatalogItemAlias = vi.fn();
const mockInactivateCatalogItemAlias = vi.fn();
const mockPublishCatalogItemPrice = vi.fn();
const mockGetActiveAssemblyDependencies = vi.fn();

vi.mock("../../lib/apiClient", async () => {
  const actual = await vi.importActual<typeof import("../../lib/apiClient")>("../../lib/apiClient");
  return {
    ...actual,
    api: {
      ...actual.api,
      getCatalogItem: (...args: unknown[]) => mockGetCatalogItem(...args),
      getCatalogCategories: (...args: unknown[]) => mockGetCatalogCategories(...args),
      createCatalogCategory: (...args: unknown[]) => mockCreateCatalogCategory(...args),
      updateCatalogItemHeader: (...args: unknown[]) => mockUpdateCatalogItemHeader(...args),
      reactivateCatalogItem: (...args: unknown[]) => mockReactivateCatalogItem(...args),
      inactivateCatalogItem: (...args: unknown[]) => mockInactivateCatalogItem(...args),
      addCatalogItemAlias: (...args: unknown[]) => mockAddCatalogItemAlias(...args),
      activateCatalogItemAlias: (...args: unknown[]) => mockActivateCatalogItemAlias(...args),
      inactivateCatalogItemAlias: (...args: unknown[]) => mockInactivateCatalogItemAlias(...args),
      publishCatalogItemPrice: (...args: unknown[]) => mockPublishCatalogItemPrice(...args),
      getActiveAssemblyDependencies: (...args: unknown[]) => mockGetActiveAssemblyDependencies(...args),
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
  return { ...utils, onBack, onRetryEntitlement, queryClient };
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
    mockCreateCatalogCategory.mockReset();
    mockUpdateCatalogItemHeader.mockReset();
    mockReactivateCatalogItem.mockReset();
    mockInactivateCatalogItem.mockReset();
    mockAddCatalogItemAlias.mockReset();
    mockActivateCatalogItemAlias.mockReset();
    mockInactivateCatalogItemAlias.mockReset();
    mockPublishCatalogItemPrice.mockReset();
    mockGetActiveAssemblyDependencies.mockReset();
    mockGetActiveAssemblyDependencies.mockResolvedValue({ count: 0, assemblies: [] });
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

  it("returns focus to the Edit trigger after the drawer is dismissed", async () => {
    const user = userEvent.setup();
    mockGetCatalogItem.mockResolvedValue(baseItem);
    renderDetail();

    await waitFor(() => expect(screen.getByText("Condensate Pump")).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: "Edit" }));
    await user.keyboard("{Escape}");

    await waitFor(() => expect(screen.getByRole("button", { name: "Edit" })).toHaveFocus());
  });

  it("moves focus to the conflict banner when a version conflict sends the user back to review", async () => {
    const user = userEvent.setup();
    const updatedByOtherEditor: CatalogItemDetailResult = {
      ...baseItem,
      item: { ...baseItem.item, displayName: "Condensate Pump (renamed by teammate)", concurrencyVersion: "v2" },
    };
    mockGetCatalogItem.mockResolvedValueOnce(baseItem).mockResolvedValue(updatedByOtherEditor);
    mockUpdateCatalogItemHeader.mockRejectedValueOnce(
      new ApiError(409, "CatalogItem.VersionMismatch", "conflict"),
    );
    renderDetail();

    await waitFor(() => expect(screen.getByText("Condensate Pump")).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: "Edit" }));
    await user.type(screen.getByLabelText("Name"), "!");
    await user.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() =>
      expect(screen.getByText(/This item was changed by someone else/)).toHaveFocus(),
    );
  });

  it("editing the header offers the same shared, creatable CategoryCombobox as the create drawer", async () => {
    const user = userEvent.setup();
    mockGetCatalogItem.mockResolvedValue(baseItem);
    mockUpdateCatalogItemHeader.mockResolvedValue({ concurrencyVersion: "v2" });
    renderDetail();

    await waitFor(() => expect(screen.getByText("Condensate Pump")).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: "Edit" }));

    const categoryField = screen.getByLabelText("Category");
    expect(categoryField).toHaveValue("Refrigerant");
    await user.click(categoryField);
    await user.click(screen.getByRole("option", { name: "Fittings" }));
    await user.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(mockUpdateCatalogItemHeader).toHaveBeenCalledWith(
      "item-1",
      { displayName: "Condensate Pump", externalKey: "COP-34", categoryId: "cat-2", isCommonItem: false },
      "v1",
    ));
  });

  it("creating a category from the edit form blocks Save until it resolves, then saves the header with the new category", async () => {
    const user = userEvent.setup();
    mockGetCatalogItem.mockResolvedValue(baseItem);
    mockUpdateCatalogItemHeader.mockResolvedValue({ concurrencyVersion: "v2" });
    let resolveCreate: (v: unknown) => void = () => {};
    mockCreateCatalogCategory.mockReturnValue(new Promise((resolve) => (resolveCreate = resolve)));
    renderDetail();

    await waitFor(() => expect(screen.getByText("Condensate Pump")).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: "Edit" }));

    await user.clear(screen.getByLabelText("Category"));
    await user.type(screen.getByLabelText("Category"), "Ductwork");
    await user.click(screen.getByText('+ Create "Ductwork"'));

    await waitFor(() => expect(screen.getByRole("button", { name: "Save" })).toBeDisabled());
    await user.click(screen.getByRole("button", { name: "Save" }));
    expect(mockUpdateCatalogItemHeader).not.toHaveBeenCalled();

    resolveCreate({ id: "cat-3", name: "Ductwork", displayOrder: 2, activeState: "Active", concurrencyVersion: "v1" });
    await waitFor(() => expect(screen.getByLabelText("Category")).toHaveValue("Ductwork"));
    await waitFor(() => expect(screen.getByRole("button", { name: "Save" })).toBeEnabled());

    await user.click(screen.getByRole("button", { name: "Save" }));
    await waitFor(() => expect(mockUpdateCatalogItemHeader).toHaveBeenCalledWith(
      "item-1",
      { displayName: "Condensate Pump", externalKey: "COP-34", categoryId: "cat-3", isCommonItem: false },
      "v1",
    ));
  });

  it("a category-name race in the edit form resolves by selecting the concurrently created category, and preserves the rest of the edited draft", async () => {
    const user = userEvent.setup();
    mockGetCatalogItem.mockResolvedValue(baseItem);
    mockUpdateCatalogItemHeader.mockResolvedValue({ concurrencyVersion: "v2" });
    mockCreateCatalogCategory.mockRejectedValue(
      new ApiError(409, "CatalogCategory.NameAlreadyExists", "conflict"),
    );
    // The parent's initial load reflects only cat-1/cat-2 — "Ductwork" is created concurrently by
    // someone else and only surfaces once the combobox's own conflict-recovery refetch runs.
    mockGetCatalogCategories.mockResolvedValueOnce({
      categories: [
        { id: "cat-1", name: "Refrigerant", displayOrder: 0, activeState: "Active", concurrencyVersion: "v1" },
        { id: "cat-2", name: "Fittings", displayOrder: 1, activeState: "Active", concurrencyVersion: "v1" },
      ],
    });
    mockGetCatalogCategories.mockResolvedValue({
      categories: [
        { id: "cat-1", name: "Refrigerant", displayOrder: 0, activeState: "Active", concurrencyVersion: "v1" },
        { id: "cat-2", name: "Fittings", displayOrder: 1, activeState: "Active", concurrencyVersion: "v1" },
        { id: "cat-3", name: "Ductwork", displayOrder: 2, activeState: "Active", concurrencyVersion: "v1" },
      ],
    });
    renderDetail();

    await waitFor(() => expect(screen.getByText("Condensate Pump")).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: "Edit" }));

    // Preserve the rest of the edited draft while the category race resolves in the background.
    const nameInput = screen.getByLabelText("Name") as HTMLInputElement;
    await user.clear(nameInput);
    await user.type(nameInput, "Condensate Pump Mk2");
    await user.clear(screen.getByLabelText("Category"));
    await user.type(screen.getByLabelText("Category"), "Ductwork");
    await user.click(screen.getByText('+ Create "Ductwork"'));

    await waitFor(() => expect(screen.getByLabelText("Category")).toHaveValue("Ductwork"));
    expect(screen.queryByText(/couldn't add that category/i)).not.toBeInTheDocument();
    expect((screen.getByLabelText("Name") as HTMLInputElement).value).toBe("Condensate Pump Mk2");

    await user.click(screen.getByRole("button", { name: "Save" }));
    await waitFor(() => expect(mockUpdateCatalogItemHeader).toHaveBeenCalledWith(
      "item-1",
      { displayName: "Condensate Pump Mk2", externalKey: "COP-34", categoryId: "cat-3", isCommonItem: false },
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

  it("reactivates an inactive item and re-enables Edit once the refresh lands", async () => {
    const user = userEvent.setup();
    const inactiveItem: CatalogItemDetailResult = {
      ...baseItem,
      item: { ...baseItem.item, activeState: "Inactive" },
    };
    const reactivatedItem: CatalogItemDetailResult = {
      ...baseItem,
      item: { ...baseItem.item, activeState: "Active", concurrencyVersion: "v2" },
    };
    mockGetCatalogItem.mockResolvedValueOnce(inactiveItem).mockResolvedValue(reactivatedItem);
    mockReactivateCatalogItem.mockResolvedValue({ concurrencyVersion: "v2" });
    renderDetail();

    const reactivateButton = await screen.findByRole("button", { name: "Reactivate" });
    await user.click(reactivateButton);

    await waitFor(() => expect(mockReactivateCatalogItem).toHaveBeenCalledWith("item-1", "v1"));
    await waitFor(() => expect(screen.queryByRole("button", { name: "Reactivate" })).not.toBeInTheDocument());
    expect(screen.getByRole("button", { name: "Edit" })).toBeEnabled();
  });

  it("shows an already-active conflict and refreshes without crashing", async () => {
    const user = userEvent.setup();
    const inactiveItem: CatalogItemDetailResult = {
      ...baseItem,
      item: { ...baseItem.item, activeState: "Inactive" },
    };
    mockGetCatalogItem.mockResolvedValueOnce(inactiveItem).mockResolvedValue(baseItem);
    mockReactivateCatalogItem.mockRejectedValueOnce(new ApiError(409, "CatalogItem.AlreadyActive", "conflict"));
    renderDetail();

    const reactivateButton = await screen.findByRole("button", { name: "Reactivate" });
    await user.click(reactivateButton);

    await waitFor(() => expect(screen.getByText("This item is already active.")).toBeInTheDocument());
    await waitFor(() => expect(screen.queryByRole("button", { name: "Reactivate" })).not.toBeInTheDocument());
  });

  it("inactivates an active item after confirmation", async () => {
    const user = userEvent.setup();
    const inactivatedItem: CatalogItemDetailResult = {
      ...baseItem,
      item: { ...baseItem.item, activeState: "Inactive", concurrencyVersion: "v2" },
    };
    mockGetCatalogItem.mockResolvedValueOnce(baseItem).mockResolvedValue(inactivatedItem);
    mockInactivateCatalogItem.mockResolvedValue({ concurrencyVersion: "v2" });
    renderDetail();

    await waitFor(() => expect(screen.getByText("Condensate Pump")).toBeInTheDocument());
    // Clicking Inactivate alone must not fire the mutation — it only reveals the confirmation.
    await user.click(screen.getByRole("button", { name: "Inactivate" }));
    expect(mockInactivateCatalogItem).not.toHaveBeenCalled();

    await user.click(screen.getByRole("button", { name: "Confirm inactivate" }));

    await waitFor(() => expect(mockInactivateCatalogItem).toHaveBeenCalledWith("item-1", "v1"));
    await waitFor(() => expect(screen.getByRole("button", { name: "Reactivate" })).toBeInTheDocument());
    expect(screen.queryByRole("button", { name: "Inactivate" })).not.toBeInTheDocument();
  });

  it("keeps the normal confirmation path when no assemblies depend on the item", async () => {
    const user = userEvent.setup();
    mockGetCatalogItem.mockResolvedValue(baseItem);
    mockGetActiveAssemblyDependencies.mockResolvedValue({ count: 0, assemblies: [] });
    renderDetail();

    await waitFor(() => expect(screen.getByText("Condensate Pump")).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: "Inactivate" }));

    await waitFor(() => expect(mockGetActiveAssemblyDependencies).toHaveBeenCalledWith("item-1"));
    await waitFor(() => expect(screen.getByText("Remove from selection?")).toBeInTheDocument());
    expect(screen.getByRole("button", { name: "Confirm inactivate" })).toBeEnabled();
  });

  it("names the affected assemblies and warns they become unavailable for new selection", async () => {
    const user = userEvent.setup();
    mockGetCatalogItem.mockResolvedValue(baseItem);
    mockGetActiveAssemblyDependencies.mockResolvedValue({
      count: 2,
      assemblies: [
        { id: "assembly-1", name: "Seasonal Tune-Up" },
        { id: "assembly-2", name: "Full System Replacement" },
      ],
    });
    renderDetail();

    await waitFor(() => expect(screen.getByText("Condensate Pump")).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: "Inactivate" }));

    await waitFor(() => expect(screen.getByText("Seasonal Tune-Up")).toBeInTheDocument());
    expect(screen.getByText("Full System Replacement")).toBeInTheDocument();
    expect(screen.getByText(/unavailable for new selection/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Confirm inactivate" })).toBeEnabled();
  });

  it("does not allow a blind inactivation when the dependency check fails", async () => {
    const user = userEvent.setup();
    mockGetCatalogItem.mockResolvedValue(baseItem);
    mockGetActiveAssemblyDependencies.mockRejectedValue(new Error("network error"));
    renderDetail();

    await waitFor(() => expect(screen.getByText("Condensate Pump")).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: "Inactivate" }));

    await waitFor(() =>
      expect(screen.getByText(/Couldn't check whether this item is used/)).toBeInTheDocument(),
    );
    expect(screen.getByRole("button", { name: "Confirm inactivate" })).toBeDisabled();
    expect(mockInactivateCatalogItem).not.toHaveBeenCalled();
  });

  it("keeps Confirm inactivate disabled while a reopened confirmation is still refetching stale-cached dependencies", async () => {
    const user = userEvent.setup();
    mockGetCatalogItem.mockResolvedValue(baseItem);
    let resolveSecondFetch: (value: { count: number; assemblies: { id: string; name: string }[] }) => void;
    const deferredSecondFetch = new Promise<{ count: number; assemblies: { id: string; name: string }[] }>((resolve) => {
      resolveSecondFetch = resolve;
    });
    mockGetActiveAssemblyDependencies
      .mockResolvedValueOnce({ count: 0, assemblies: [] })
      .mockReturnValueOnce(deferredSecondFetch);
    renderDetail();

    await waitFor(() => expect(screen.getByText("Condensate Pump")).toBeInTheDocument());

    // First open resolves normally.
    await user.click(screen.getByRole("button", { name: "Inactivate" }));
    await waitFor(() => expect(screen.getByText("Remove from selection?")).toBeInTheDocument());

    // Cancel, then reopen — React Query serves the cached (stale) result immediately
    // (isLoading false) while a background refetch is still in flight (isFetching true).
    await user.click(screen.getByRole("button", { name: "Cancel" }));
    await user.click(screen.getByRole("button", { name: "Inactivate" }));

    await waitFor(() => expect(mockGetActiveAssemblyDependencies).toHaveBeenCalledTimes(2));
    expect(screen.getByRole("button", { name: "Confirm inactivate" })).toBeDisabled();

    await user.click(screen.getByRole("button", { name: "Confirm inactivate" }));
    expect(mockInactivateCatalogItem).not.toHaveBeenCalled();

    resolveSecondFetch!({ count: 0, assemblies: [] });

    await waitFor(() => expect(screen.getByRole("button", { name: "Confirm inactivate" })).toBeEnabled());
  });

  it("holds Inactivate disabled through a version conflict until the refetch lands", async () => {
    const user = userEvent.setup();
    const refreshed: CatalogItemDetailResult = {
      ...baseItem,
      item: { ...baseItem.item, concurrencyVersion: "v2" },
    };
    let resolveRefetch: (value: CatalogItemDetailResult) => void;
    const deferredRefetch = new Promise<CatalogItemDetailResult>((resolve) => {
      resolveRefetch = resolve;
    });
    mockGetCatalogItem.mockResolvedValueOnce(baseItem).mockReturnValueOnce(deferredRefetch);
    mockInactivateCatalogItem.mockRejectedValueOnce(new ApiError(409, "CatalogItem.VersionMismatch", "conflict"));
    renderDetail();

    await waitFor(() => expect(screen.getByText("Condensate Pump")).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: "Inactivate" }));
    await user.click(screen.getByRole("button", { name: "Confirm inactivate" }));

    await waitFor(() =>
      expect(screen.getByText("This item was changed elsewhere. Refreshing…")).toBeInTheDocument(),
    );
    expect(screen.getByRole("button", { name: "Refreshing…" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Inactivate" })).toBeDisabled();

    resolveRefetch!(refreshed);

    await waitFor(() => expect(screen.getByRole("button", { name: "Edit" })).toBeEnabled());
    expect(screen.getByRole("button", { name: "Inactivate" })).toBeEnabled();
  });

  it("inactivate-then-reactivate round trip returns the item to Active with the latest version", async () => {
    const user = userEvent.setup();
    const inactivatedItem: CatalogItemDetailResult = {
      ...baseItem,
      item: { ...baseItem.item, activeState: "Inactive", concurrencyVersion: "v2" },
    };
    const reactivatedItem: CatalogItemDetailResult = {
      ...baseItem,
      item: { ...baseItem.item, activeState: "Active", concurrencyVersion: "v3" },
    };
    mockGetCatalogItem
      .mockResolvedValueOnce(baseItem)
      .mockResolvedValueOnce(inactivatedItem)
      .mockResolvedValue(reactivatedItem);
    mockInactivateCatalogItem.mockResolvedValue({ concurrencyVersion: "v2" });
    mockReactivateCatalogItem.mockResolvedValue({ concurrencyVersion: "v3" });
    renderDetail();

    await waitFor(() => expect(screen.getByText("Condensate Pump")).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: "Inactivate" }));
    await user.click(screen.getByRole("button", { name: "Confirm inactivate" }));

    const reactivateButton = await screen.findByRole("button", { name: "Reactivate" });
    await waitFor(() => expect(mockInactivateCatalogItem).toHaveBeenCalledWith("item-1", "v1"));
    await user.click(reactivateButton);

    await waitFor(() => expect(mockReactivateCatalogItem).toHaveBeenCalledWith("item-1", "v2"));
    await waitFor(() => expect(screen.getByRole("button", { name: "Inactivate" })).toBeInTheDocument());
  });

  it("adds an alias, clearing the input on success", async () => {
    const user = userEvent.setup();
    const withNewAlias: CatalogItemDetailResult = {
      ...baseItem,
      aliases: [...baseItem.aliases, { id: "alias-2", aliasText: "cond pump", activeState: "Active" }],
      item: { ...baseItem.item, concurrencyVersion: "v2" },
    };
    mockGetCatalogItem.mockResolvedValueOnce(baseItem).mockResolvedValue(withNewAlias);
    mockAddCatalogItemAlias.mockResolvedValue({
      id: "alias-2",
      catalogItemId: "item-1",
      aliasText: "cond pump",
      activeState: "Active",
      catalogItemConcurrencyVersion: "v2",
    });
    renderDetail();

    await waitFor(() => expect(screen.getByText("Condensate Pump")).toBeInTheDocument());
    const aliasInput = screen.getByLabelText("New alias") as HTMLInputElement;
    await user.type(aliasInput, "cond pump");
    await user.click(screen.getByRole("button", { name: "Add" }));

    await waitFor(() => expect(mockAddCatalogItemAlias).toHaveBeenCalledWith(
      "item-1", { aliasText: "cond pump" }, "v1",
    ));
    await waitFor(() => expect(screen.getByText("cond pump")).toBeInTheDocument());
    expect((screen.getByLabelText("New alias") as HTMLInputElement).value).toBe("");
  });

  it("preserves the typed alias text when adding an alias fails", async () => {
    const user = userEvent.setup();
    mockGetCatalogItem.mockResolvedValue(baseItem);
    mockAddCatalogItemAlias.mockRejectedValueOnce(
      new ApiError(409, "CatalogItem.AliasAlreadyExists", "conflict"),
    );
    renderDetail();

    await waitFor(() => expect(screen.getByText("Condensate Pump")).toBeInTheDocument());
    const aliasInput = screen.getByLabelText("New alias") as HTMLInputElement;
    await user.type(aliasInput, "condensate pump");
    await user.click(screen.getByRole("button", { name: "Add" }));

    await waitFor(() =>
      expect(screen.getByText("This catalog item already has an alias with this text.")).toBeInTheDocument(),
    );
    expect(aliasInput.value).toBe("condensate pump");
  });

  it("deactivates and reactivates an existing alias, using the refreshed version on each step", async () => {
    const user = userEvent.setup();
    const deactivated: CatalogItemDetailResult = {
      ...baseItem,
      aliases: [{ ...baseItem.aliases[0], activeState: "Inactive" }],
      item: { ...baseItem.item, concurrencyVersion: "v2" },
    };
    const reactivated: CatalogItemDetailResult = {
      ...baseItem,
      aliases: [{ ...baseItem.aliases[0], activeState: "Active" }],
      item: { ...baseItem.item, concurrencyVersion: "v3" },
    };
    mockGetCatalogItem
      .mockResolvedValueOnce(baseItem)
      .mockResolvedValueOnce(deactivated)
      .mockResolvedValue(reactivated);
    mockInactivateCatalogItemAlias.mockResolvedValue({ catalogItemConcurrencyVersion: "v2" });
    mockActivateCatalogItemAlias.mockResolvedValue({ catalogItemConcurrencyVersion: "v3" });
    renderDetail();

    await waitFor(() => expect(screen.getByText("Condensate Pump")).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: "Deactivate" }));

    await waitFor(() => expect(mockInactivateCatalogItemAlias).toHaveBeenCalledWith("item-1", "alias-1", "v1"));
    await waitFor(() => expect(screen.getByRole("button", { name: "Activate" })).toBeInTheDocument());

    await user.click(screen.getByRole("button", { name: "Activate" }));

    await waitFor(() => expect(mockActivateCatalogItemAlias).toHaveBeenCalledWith("item-1", "alias-1", "v2"));
    await waitFor(() => expect(screen.getByRole("button", { name: "Deactivate" })).toBeInTheDocument());
  });

  it("disables Edit and alias controls until an alias version conflict's refetch lands", async () => {
    const user = userEvent.setup();
    const refreshed: CatalogItemDetailResult = {
      ...baseItem,
      item: { ...baseItem.item, concurrencyVersion: "v2" },
    };
    let resolveRefetch: (value: CatalogItemDetailResult) => void;
    const deferredRefetch = new Promise<CatalogItemDetailResult>((resolve) => {
      resolveRefetch = resolve;
    });
    mockGetCatalogItem.mockResolvedValueOnce(baseItem).mockReturnValueOnce(deferredRefetch);
    mockInactivateCatalogItemAlias.mockRejectedValueOnce(
      new ApiError(409, "CatalogItem.VersionMismatch", "conflict"),
    );
    renderDetail();

    await waitFor(() => expect(screen.getByText("Condensate Pump")).toBeInTheDocument());
    // The seeded alias is Active, so its toggle reads "Deactivate", which calls
    // inactivateCatalogItemAlias — exercising the shared version-conflict handling.
    await user.click(screen.getByRole("button", { name: "Deactivate" }));

    await waitFor(() =>
      expect(screen.getByText("This item was changed elsewhere. Refreshing…")).toBeInTheDocument(),
    );
    expect(screen.getByRole("button", { name: "Refreshing…" })).toBeDisabled();

    resolveRefetch!(refreshed);

    await waitFor(() => expect(screen.getByRole("button", { name: "Edit" })).toBeEnabled());
  });

  it("updates a price with a guided reason, no version header, refreshing item and list caches", async () => {
    const user = userEvent.setup();
    const updated: CatalogItemDetailResult = {
      ...baseItem,
      currentCost: 100,
      currentSellPrice: 300,
    };
    mockGetCatalogItem.mockResolvedValueOnce(baseItem).mockResolvedValue(updated);
    mockPublishCatalogItemPrice.mockResolvedValue({
      versionNumber: 2,
      priceBookVersionId: "pbv-2",
      priceBookVersionLineId: "pbvl-2",
      cost: 100,
      sellPrice: 300,
    });
    const { queryClient } = renderDetail();
    queryClient.setQueryData(["catalogItems"], { items: [] });

    await waitFor(() => expect(screen.getByText("Condensate Pump")).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: "Update pricing & cost" }));

    const sellPriceInput = screen.getByLabelText("Sell price") as HTMLInputElement;
    const costInput = screen.getByLabelText("Internal cost (optional)") as HTMLInputElement;
    // Prefilled from the current price; Sell price renders before Cost.
    expect(sellPriceInput.value).toBe("250");
    expect(costInput.value).toBe("125");

    await user.clear(sellPriceInput);
    await user.type(sellPriceInput, "300");
    await user.clear(costInput);
    await user.type(costInput, "100");
    await user.selectOptions(screen.getByLabelText("Why are you updating this?"), "supplier-cost-changed");
    await user.click(screen.getByRole("button", { name: "Update pricing & cost" }));

    await waitFor(() => expect(mockPublishCatalogItemPrice).toHaveBeenCalledWith(
      "item-1",
      { cost: 100, sellPrice: 300, reason: "Supplier cost changed" },
    ));
    // No version/token argument — ADR-470's lock is account-scoped, not item-scoped.
    expect(mockPublishCatalogItemPrice.mock.calls[0]).toHaveLength(2);

    await waitFor(() => expect(screen.queryByRole("button", { name: "Cancel" })).not.toBeInTheDocument());
    await waitFor(() => expect(screen.getByText("$300.00")).toBeInTheDocument());
    expect(queryClient.getQueryState(["catalogItems"])?.isInvalidated).toBe(true);
  });

  it("requires typed text when Other is selected as the reason", async () => {
    const user = userEvent.setup();
    mockGetCatalogItem.mockResolvedValue(baseItem);
    mockPublishCatalogItemPrice.mockResolvedValue({
      versionNumber: 2,
      priceBookVersionId: "pbv-2",
      priceBookVersionLineId: "pbvl-2",
      cost: 125,
      sellPrice: 275,
    });
    renderDetail();

    await waitFor(() => expect(screen.getByText("Condensate Pump")).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: "Update pricing & cost" }));

    const sellPriceInput = screen.getByLabelText("Sell price") as HTMLInputElement;
    await user.clear(sellPriceInput);
    await user.type(sellPriceInput, "275");
    await user.selectOptions(screen.getByLabelText("Why are you updating this?"), "other");
    await user.click(screen.getByRole("button", { name: "Update pricing & cost" }));

    expect(screen.getByText("Enter a reason.")).toBeInTheDocument();
    expect(mockPublishCatalogItemPrice).not.toHaveBeenCalled();

    await user.type(screen.getByLabelText("Reason"), "Vendor rebate ended");
    await user.click(screen.getByRole("button", { name: "Update pricing & cost" }));

    await waitFor(() => expect(mockPublishCatalogItemPrice).toHaveBeenCalledWith(
      "item-1",
      { cost: 125, sellPrice: 275, reason: "Vendor rebate ended" },
    ));
  });

  it("keeps No standalone price inside collapsed Advanced options and preserves clear-sell-price behavior", async () => {
    const user = userEvent.setup();
    mockGetCatalogItem.mockResolvedValue(baseItem);
    mockPublishCatalogItemPrice.mockResolvedValue({
      versionNumber: 2,
      priceBookVersionId: "pbv-2",
      priceBookVersionLineId: "pbvl-2",
      cost: 125,
      sellPrice: null,
    });
    renderDetail();

    await waitFor(() => expect(screen.getByText("Condensate Pump")).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: "Update pricing & cost" }));

    // Collapsed by default since the item currently has a standalone sell price.
    expect(screen.getByText("Advanced options").closest("details")).not.toHaveAttribute("open");
    await user.click(screen.getByText("Advanced options"));

    await user.click(screen.getByLabelText(/This item doesn't have its own sell price/));
    expect(screen.queryByLabelText("Sell price")).not.toBeInTheDocument();

    await user.selectOptions(screen.getByLabelText("Why are you updating this?"), "correcting-a-price");
    await user.click(screen.getByRole("button", { name: "Update pricing & cost" }));

    await waitFor(() => expect(mockPublishCatalogItemPrice).toHaveBeenCalledWith(
      "item-1",
      { cost: 125, sellPrice: null, reason: "Correcting a price" },
    ));
  });

  it("disables Update pricing & cost when nothing changed, enabling only after a real price change", async () => {
    const user = userEvent.setup();
    mockGetCatalogItem.mockResolvedValue(baseItem);
    renderDetail();

    await waitFor(() => expect(screen.getByText("Condensate Pump")).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: "Update pricing & cost" }));

    // Selecting a reason alone — with no price/mode change — must not enable Update pricing & cost.
    await user.selectOptions(screen.getByLabelText("Why are you updating this?"), "promotion-or-seasonal-pricing");
    expect(screen.getByRole("button", { name: "Update pricing & cost" })).toBeDisabled();
    expect(screen.getByText("Change a price or pricing option to update this item.")).toBeInTheDocument();

    const sellPriceInput = screen.getByLabelText("Sell price") as HTMLInputElement;
    await user.clear(sellPriceInput);
    await user.type(sellPriceInput, "260");

    expect(screen.getByRole("button", { name: "Update pricing & cost" })).toBeEnabled();
    expect(
      screen.queryByText("Change a price or pricing option to update this item."),
    ).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Update pricing & cost" }));

    await waitFor(() => expect(mockPublishCatalogItemPrice).toHaveBeenCalledWith(
      "item-1",
      { cost: 125, sellPrice: 260, reason: "Promotion or seasonal pricing" },
    ));
  });

  it("renders the pricing & cost form before the Aliases section, directly after the header/summary, when open", async () => {
    const user = userEvent.setup();
    mockGetCatalogItem.mockResolvedValue(baseItem);
    renderDetail();

    await waitFor(() => expect(screen.getByText("Condensate Pump")).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: "Update pricing & cost" }));

    const formHeading = await screen.findByRole("heading", { name: "Update pricing & cost" });
    const aliasesHeading = screen.getByRole("heading", { name: "Search aliases" });
    // DOCUMENT_POSITION_FOLLOWING on aliasesHeading (relative to formHeading) means the form
    // comes first — the repair form must be the first actionable content, not placed after
    // unrelated alias management.
    expect(formHeading.compareDocumentPosition(aliasesHeading) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it("shows a sticky save bar with Cancel and Update pricing & cost while the form is open", async () => {
    const user = userEvent.setup();
    mockGetCatalogItem.mockResolvedValue(baseItem);
    renderDetail();

    await waitFor(() => expect(screen.getByText("Condensate Pump")).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: "Update pricing & cost" }));

    const submitButton = await screen.findByRole("button", { name: "Update pricing & cost" });
    const cancelButton = screen.getByRole("button", { name: "Cancel" });
    const bar = submitButton.closest(".sticky");
    expect(bar).not.toBeNull();
    expect(bar).toContainElement(cancelButton);
    expect(bar?.className).toContain("bottom-0");
  });

  it("blocks the update on a below-cost price until explicitly confirmed", async () => {
    const user = userEvent.setup();
    mockGetCatalogItem.mockResolvedValue(baseItem);
    mockPublishCatalogItemPrice.mockResolvedValue({
      versionNumber: 2,
      priceBookVersionId: "pbv-2",
      priceBookVersionLineId: "pbvl-2",
      cost: 125,
      sellPrice: 50,
    });
    renderDetail();

    await waitFor(() => expect(screen.getByText("Condensate Pump")).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: "Update pricing & cost" }));

    const sellPriceInput = screen.getByLabelText("Sell price") as HTMLInputElement;
    await user.clear(sellPriceInput);
    await user.type(sellPriceInput, "50");
    await user.selectOptions(screen.getByLabelText("Why are you updating this?"), "correcting-a-price");
    await user.click(screen.getByRole("button", { name: "Update pricing & cost" }));

    expect(screen.getByText(/Sell price is below cost/)).toBeInTheDocument();
    expect(mockPublishCatalogItemPrice).not.toHaveBeenCalled();

    await user.click(screen.getByLabelText("I understand this item is priced below cost"));
    await user.click(screen.getByRole("button", { name: "Update pricing & cost" }));

    await waitFor(() => expect(mockPublishCatalogItemPrice).toHaveBeenCalledWith(
      "item-1",
      { cost: 125, sellPrice: 50, reason: "Correcting a price" },
    ));
  });

  it("holds the draft and does not auto-resubmit on a pricing conflict", async () => {
    const user = userEvent.setup();
    const refreshedAfterConflict: CatalogItemDetailResult = {
      ...baseItem,
      currentCost: 130,
      currentSellPrice: 260,
    };
    mockGetCatalogItem.mockResolvedValueOnce(baseItem).mockResolvedValue(refreshedAfterConflict);
    mockPublishCatalogItemPrice.mockRejectedValueOnce(
      new ApiError(409, "PriceBookVersion.PublishLockConflict", "conflict"),
    );
    renderDetail();

    await waitFor(() => expect(screen.getByText("Condensate Pump")).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: "Update pricing & cost" }));

    const sellPriceInput = screen.getByLabelText("Sell price") as HTMLInputElement;
    await user.clear(sellPriceInput);
    await user.type(sellPriceInput, "275");
    await user.selectOptions(screen.getByLabelText("Why are you updating this?"), "correcting-a-price");
    await user.click(screen.getByRole("button", { name: "Update pricing & cost" }));

    await waitFor(() =>
      expect(
        screen.getByText(
          "Someone else updated pricing a moment ago. We refreshed the latest price—review your changes and try again.",
        ),
      ).toBeInTheDocument(),
    );
    // No mention of locks, versions, or replay in the surfaced copy.
    expect(screen.queryByText(/lock/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/version/i)).not.toBeInTheDocument();
    expect(mockPublishCatalogItemPrice).toHaveBeenCalledTimes(1);
    // The draft is retained exactly as typed — no automatic resubmit, no clearing.
    expect((screen.getByLabelText("Sell price") as HTMLInputElement).value).toBe("275");
    expect((screen.getByLabelText("Internal cost (optional)") as HTMLInputElement).value).toBe("125");

    await waitFor(() => expect(mockGetCatalogItem).toHaveBeenCalledTimes(2));
    expect(mockPublishCatalogItemPrice).toHaveBeenCalledTimes(1);
  });
});
