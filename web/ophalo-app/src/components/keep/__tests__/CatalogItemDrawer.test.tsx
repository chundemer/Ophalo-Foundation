import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { CatalogItemDrawer } from "../CatalogItemDrawer";
import { ApiError } from "../../../lib/apiClient";
import type { CatalogCategoryResponse, CreateAndActivateCatalogItemResult } from "../../../lib/apiClient";

const mockCreateCatalogItem = vi.fn();
const mockCreateCatalogCategory = vi.fn();
const mockGetCatalogCategories = vi.fn();

vi.mock("../../../lib/apiClient", async () => {
  const actual = await vi.importActual<typeof import("../../../lib/apiClient")>("../../../lib/apiClient");
  return {
    ...actual,
    api: {
      ...actual.api,
      createCatalogItem: (...args: unknown[]) => mockCreateCatalogItem(...args),
      createCatalogCategory: (...args: unknown[]) => mockCreateCatalogCategory(...args),
      getCatalogCategories: (...args: unknown[]) => mockGetCatalogCategories(...args),
    },
  };
});

const categories: CatalogCategoryResponse[] = [
  { id: "cat-1", name: "Refrigerant", displayOrder: 0, activeState: "Active", concurrencyVersion: "v1" },
];

const createdResult: CreateAndActivateCatalogItemResult = {
  item: {
    id: "item-1",
    type: "Material",
    displayName: "Condensate Pump",
    externalKey: null,
    categoryId: null,
    unitOfMeasure: "each",
    currency: "USD",
    isCommonItem: false,
    activeState: "Active",
    concurrencyVersion: "v2",
  },
  versionNumber: 1,
  priceBookVersionId: "pbv-1",
  priceBookVersionLineId: "pbvl-1",
  cost: null,
  sellPrice: 100,
  pricingMode: "StandalonePrice",
};

function renderDrawer(props: Partial<React.ComponentProps<typeof CatalogItemDrawer>> = {}) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const onClose = vi.fn();
  const onCreated = vi.fn();
  const onCategoriesChanged = vi.fn();
  const utils = render(
    <QueryClientProvider client={queryClient}>
      <CatalogItemDrawer
        categories={categories}
        onCategoriesChanged={onCategoriesChanged}
        onClose={onClose}
        onCreated={onCreated}
        {...props}
      />
    </QueryClientProvider>,
  );
  return { ...utils, onClose, onCreated, onCategoriesChanged };
}

async function fillRequiredFields(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText("Name"), "Condensate Pump");
  await user.type(screen.getByLabelText("Sell price"), "100");
}

describe("CatalogItemDrawer", () => {
  beforeEach(() => {
    mockCreateCatalogItem.mockReset();
    mockCreateCatalogCategory.mockReset();
    mockGetCatalogCategories.mockReset();
  });

  it("submits Save & activate with the entered fields and closes on success", async () => {
    const user = userEvent.setup();
    mockCreateCatalogItem.mockResolvedValue(createdResult);
    const { onClose, onCreated } = renderDrawer();

    await fillRequiredFields(user);
    await user.click(screen.getByRole("button", { name: /save & activate/i }));

    await waitFor(() => expect(mockCreateCatalogItem).toHaveBeenCalledWith(
      expect.objectContaining({
        displayName: "Condensate Pump",
        unitOfMeasure: "each",
        currency: "USD",
        pricingMode: "StandalonePrice",
        sellPrice: 100,
      }),
    ));
    await waitFor(() => expect(onCreated).toHaveBeenCalledWith(createdResult));
    await waitFor(() => expect(onClose).toHaveBeenCalled());
  });

  it("blocks submit, shows a field error, and focuses the field when the name is missing", async () => {
    const user = userEvent.setup();
    renderDrawer();

    await user.type(screen.getByLabelText("Sell price"), "100");
    await user.click(screen.getByRole("button", { name: /save & activate/i }));

    const nameError = screen.getByText("Name is required.");
    expect(nameError).toBeInTheDocument();
    const nameInput = screen.getByLabelText("Name");
    expect(nameInput).toHaveFocus();
    // 2e.7c: the error is programmatically associated with its field, not just visually adjacent.
    expect(nameInput).toHaveAttribute("aria-describedby", nameError.id);
    expect(mockCreateCatalogItem).not.toHaveBeenCalled();
  });

  it("maps a server SKU-conflict error to the SKU field and focuses it", async () => {
    const user = userEvent.setup();
    mockCreateCatalogItem.mockRejectedValue(
      new ApiError(409, "CatalogItem.ExternalKeyAlreadyExists", "conflict"),
    );
    renderDrawer();

    await fillRequiredFields(user);
    await user.type(screen.getByLabelText(/SKU \/ internal code/), "CP20");
    await user.click(screen.getByRole("button", { name: /save & activate/i }));

    await waitFor(() => expect(screen.getByText("This SKU is already in use.")).toBeInTheDocument());
    expect(screen.getByLabelText(/SKU \/ internal code/)).toHaveFocus();

    // Values are preserved after a server-validation failure, not cleared.
    expect(screen.getByLabelText("Name")).toHaveValue("Condensate Pump");
    expect(screen.getByLabelText(/SKU \/ internal code/)).toHaveValue("CP20");
    expect(screen.getByLabelText("Sell price")).toHaveValue(100);
  });

  it("focuses the first invalid field in visual order when both name and sell price are missing", async () => {
    const user = userEvent.setup();
    renderDrawer();

    await user.click(screen.getByRole("button", { name: /save & activate/i }));

    expect(screen.getByText("Name is required.")).toBeInTheDocument();
    expect(screen.getByText("Enter a sell price, or choose No standalone price.")).toBeInTheDocument();
    expect(screen.getByLabelText("Name")).toHaveFocus();
  });

  it("Save & add another clears identity fields but retains category, type, UOM, and price mode", async () => {
    const user = userEvent.setup();
    mockCreateCatalogItem.mockResolvedValue(createdResult);
    const { onClose } = renderDrawer();

    await user.selectOptions(screen.getByLabelText("Type"), "Equipment");
    await user.click(screen.getByLabelText(/Category/));
    await user.click(screen.getByRole("option", { name: "Refrigerant" }));
    await user.click(screen.getByRole("checkbox", { name: "This item doesn't have its own sell price" }));
    await user.type(screen.getByLabelText("Name"), "Condensate Pump");
    await user.type(screen.getByLabelText(/SKU \/ internal code/), "CP20");
    await user.click(screen.getByRole("checkbox", { name: "Common item" }));

    await user.click(screen.getByRole("button", { name: /save & add another/i }));

    await waitFor(() => expect(screen.getByText("Condensate Pump added.")).toBeInTheDocument());
    expect(onClose).not.toHaveBeenCalled();

    // Cleared: item identity.
    expect(screen.getByLabelText("Name")).toHaveValue("");
    expect(screen.getByLabelText(/SKU \/ internal code/)).toHaveValue("");
    expect(screen.getByRole("checkbox", { name: "Common item" })).not.toBeChecked();

    // Retained: category, type, UOM, and price mode (build-log/112).
    expect(screen.getByLabelText("Type")).toHaveValue("Equipment");
    expect(screen.getByLabelText(/Category/)).toHaveValue("Refrigerant");
    expect(screen.getByLabelText("Unit of measure")).toHaveValue("each");
    expect(screen.getByRole("checkbox", { name: "This item doesn't have its own sell price" })).toBeChecked();

    // Focus returns to Display Name.
    expect(screen.getByLabelText("Name")).toHaveFocus();
  });

  it("Ctrl+Enter from within the drawer triggers Save & add another", async () => {
    const user = userEvent.setup();
    mockCreateCatalogItem.mockResolvedValue(createdResult);
    renderDrawer();

    await fillRequiredFields(user);
    await user.keyboard("{Control>}{Enter}{/Control}");

    await waitFor(() => expect(mockCreateCatalogItem).toHaveBeenCalled());
    await waitFor(() => expect(screen.getByText("Condensate Pump added.")).toBeInTheDocument());
  });

  it("Cmd+Enter from within the drawer triggers Save & add another", async () => {
    const user = userEvent.setup();
    mockCreateCatalogItem.mockResolvedValue(createdResult);
    renderDrawer();

    await fillRequiredFields(user);
    await user.keyboard("{Meta>}{Enter}{/Meta}");

    await waitFor(() => expect(mockCreateCatalogItem).toHaveBeenCalled());
    await waitFor(() => expect(screen.getByText("Condensate Pump added.")).toBeInTheDocument());
  });

  it("defaults unit of measure to each, and quick-fill chips write literal values", async () => {
    const user = userEvent.setup();
    renderDrawer();

    expect(screen.getByLabelText("Unit of measure")).toHaveValue("each");

    await user.click(screen.getByRole("button", { name: "box" }));
    expect(screen.getByLabelText("Unit of measure")).toHaveValue("box");

    await user.click(screen.getByRole("button", { name: "lot" }));
    expect(screen.getByLabelText("Unit of measure")).toHaveValue("lot");
  });

  it("sends the deliberate USD-only pilot currency on create (ADR-468 amendment)", async () => {
    const user = userEvent.setup();
    mockCreateCatalogItem.mockResolvedValue(createdResult);
    renderDrawer();

    await fillRequiredFields(user);
    await user.click(screen.getByRole("button", { name: /save & activate/i }));

    await waitFor(() => expect(mockCreateCatalogItem).toHaveBeenCalledWith(
      expect.objectContaining({ currency: "USD" }),
    ));
  });

  it("keeps the header and action footer as non-scrolling siblings of a viewport-height-capped, independently scrolling form body", () => {
    renderDrawer();

    const dialog = screen.getByRole("dialog", { name: "New catalog item" });
    expect(dialog.className).toContain("h-[100dvh]");
    expect(dialog.className).toContain("max-h-[100dvh]");

    const header = screen.getByRole("heading", { name: "New catalog item" }).closest("div");
    expect(header?.className).toContain("shrink-0");

    const footer = screen.getByRole("button", { name: /save & activate/i }).closest("div");
    expect(footer?.className).toContain("shrink-0");

    const nameField = screen.getByLabelText("Name");
    const scrollBody = nameField.closest('[class*="overflow-y-auto"]');
    expect(scrollBody).not.toBeNull();
    expect(scrollBody?.className).toContain("min-h-0");
    expect(scrollBody?.className).toContain("flex-1");
  });

  it("shows a quiet Prices in USD note instead of a dedicated Currency field", () => {
    renderDrawer();

    expect(screen.getByText("Prices in USD")).toBeInTheDocument();
    expect(screen.queryByLabelText("Currency")).not.toBeInTheDocument();
  });

  it("switching to No standalone price hides the sell price field and clears it", async () => {
    const user = userEvent.setup();
    renderDrawer();

    await user.type(screen.getByLabelText("Sell price"), "50");
    await user.click(screen.getByRole("checkbox", { name: "This item doesn't have its own sell price" }));

    expect(screen.queryByLabelText("Sell price")).not.toBeInTheDocument();
  });

  it("sends a null sellPrice, never zero, when No standalone price is checked", async () => {
    const user = userEvent.setup();
    mockCreateCatalogItem.mockResolvedValue(createdResult);
    renderDrawer();

    await user.type(screen.getByLabelText("Name"), "Condensate Pump");
    await user.click(screen.getByRole("checkbox", { name: "This item doesn't have its own sell price" }));
    await user.click(screen.getByRole("button", { name: /save & activate/i }));

    await waitFor(() => expect(mockCreateCatalogItem).toHaveBeenCalledWith(
      expect.objectContaining({ pricingMode: "NoStandalonePrice", sellPrice: null }),
    ));
  });

  it("shows a below-cost warning requiring confirmation before submit", async () => {
    const user = userEvent.setup();
    mockCreateCatalogItem.mockResolvedValue(createdResult);
    renderDrawer();

    await user.type(screen.getByLabelText("Name"), "Condensate Pump");
    await user.type(screen.getByLabelText(/^Cost/), "100");
    await user.type(screen.getByLabelText("Sell price"), "50");

    expect(screen.getByText(/sell price is below cost/i)).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /save & activate/i }));
    expect(mockCreateCatalogItem).not.toHaveBeenCalled();

    await user.click(screen.getByLabelText(/I understand this item is priced below cost/i));
    await user.click(screen.getByRole("button", { name: /save & activate/i }));
    await waitFor(() => expect(mockCreateCatalogItem).toHaveBeenCalled());
  });

  it("prompts to discard changes when closing a dirty form, and keeps editing on cancel", async () => {
    const user = userEvent.setup();
    const { onClose } = renderDrawer();

    await user.type(screen.getByLabelText("Name"), "Condensate Pump");
    await user.click(screen.getByRole("button", { name: "Close" }));

    expect(screen.getByText("Discard your changes to this item?")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Keep editing" })).toHaveFocus();
    expect(onClose).not.toHaveBeenCalled();

    await user.click(screen.getByRole("button", { name: "Keep editing" }));
    expect(screen.queryByText("Discard your changes to this item?")).not.toBeInTheDocument();
    expect(screen.getByLabelText("Name")).toHaveValue("Condensate Pump");

    await user.click(screen.getByRole("button", { name: "Close" }));
    await user.click(screen.getByRole("button", { name: "Discard changes" }));
    expect(onClose).toHaveBeenCalled();
  });

  it("traps Tab between the two discard-confirm buttons and keeps the drawer form inert", async () => {
    const user = userEvent.setup();
    renderDrawer();

    await user.type(screen.getByLabelText("Name"), "Condensate Pump");
    await user.click(screen.getByRole("button", { name: "Close" }));

    expect(screen.getByLabelText("Name")).not.toHaveFocus();
    expect(screen.getByRole("button", { name: "Keep editing" })).toHaveFocus();

    await user.tab();
    expect(screen.getByRole("button", { name: "Discard changes" })).toHaveFocus();

    await user.tab();
    expect(screen.getByRole("button", { name: "Keep editing" })).toHaveFocus();
  });

  it("closes immediately with no confirmation when the form is clean", async () => {
    const user = userEvent.setup();
    const { onClose } = renderDrawer();

    await user.click(screen.getByRole("button", { name: "Close" }));
    expect(onClose).toHaveBeenCalled();
    expect(screen.queryByText("Discard your changes to this item?")).not.toBeInTheDocument();
  });

  it("inline category creation (via the shared CategoryCombobox) selects the new category and refreshes the list", async () => {
    const user = userEvent.setup();
    mockCreateCatalogCategory.mockResolvedValue({
      id: "cat-2",
      name: "Compressors",
      displayOrder: 1,
      activeState: "Active",
      concurrencyVersion: "v1",
    });
    const { onCategoriesChanged } = renderDrawer();

    await user.type(screen.getByLabelText(/Category/), "Compressors");
    await user.click(screen.getByText('+ Create "Compressors"'));

    await waitFor(() => expect(mockCreateCatalogCategory).toHaveBeenCalledWith({ name: "Compressors", displayOrder: 1 }));
    await waitFor(() => expect(onCategoriesChanged).toHaveBeenCalled());
    await waitFor(() => expect(screen.getByLabelText(/Category/)).toHaveValue("Compressors"));
  });

  it("a category-name race resolves by refetching directly and selecting the match, without an error", async () => {
    const user = userEvent.setup();
    mockCreateCatalogCategory.mockRejectedValue(
      new ApiError(409, "CatalogCategory.NameAlreadyExists", "conflict"),
    );
    mockGetCatalogCategories.mockResolvedValue({
      categories: [
        ...categories,
        { id: "cat-2", name: "Compressors", displayOrder: 1, activeState: "Active", concurrencyVersion: "v1" },
      ],
    });
    const { onCategoriesChanged } = renderDrawer();

    await user.type(screen.getByLabelText(/Category/), "Compressors");
    await user.click(screen.getByText('+ Create "Compressors"'));

    await waitFor(() => expect(mockGetCatalogCategories).toHaveBeenCalled());
    await waitFor(() => expect(screen.getByLabelText(/Category/)).toHaveValue("Compressors"));
    expect(onCategoriesChanged).toHaveBeenCalled();
    expect(screen.queryByText(/couldn't add that category/i)).not.toBeInTheDocument();
  });

  it("a failed conflict refetch shows a retryable error instead of stalling silently", async () => {
    const user = userEvent.setup();
    mockCreateCatalogCategory.mockRejectedValue(
      new ApiError(409, "CatalogCategory.NameAlreadyExists", "conflict"),
    );
    mockGetCatalogCategories.mockRejectedValueOnce(new Error("network down"));
    mockGetCatalogCategories.mockResolvedValueOnce({
      categories: [
        ...categories,
        { id: "cat-2", name: "Compressors", displayOrder: 1, activeState: "Active", concurrencyVersion: "v1" },
      ],
    });
    renderDrawer();

    await user.type(screen.getByLabelText(/Category/), "Compressors");
    await user.click(screen.getByText('+ Create "Compressors"'));

    await waitFor(() => expect(screen.getByText("Couldn't confirm the category. Try again.")).toBeInTheDocument());

    await user.click(screen.getByRole("button", { name: "Try again" }));
    await waitFor(() => expect(screen.getByLabelText(/Category/)).toHaveValue("Compressors"));
  });

  it("disables Save & activate and Save & add another while a new category is being created", async () => {
    const user = userEvent.setup();
    mockCreateCatalogCategory.mockReturnValue(new Promise(() => {}));
    renderDrawer();

    await fillRequiredFields(user);
    await user.type(screen.getByLabelText(/Category/), "Compressors");
    await user.click(screen.getByText('+ Create "Compressors"'));

    await waitFor(() => expect(screen.getByRole("button", { name: /^save & activate$/i })).toBeDisabled());
    expect(screen.getByRole("button", { name: /^save & add another$/i })).toBeDisabled();
  });

  it("blocks Ctrl+Enter save while a new category is mid-creation, so it can never race an uncommitted category", async () => {
    const user = userEvent.setup();
    mockCreateCatalogCategory.mockReturnValue(new Promise(() => {}));
    renderDrawer();

    await fillRequiredFields(user);
    await user.type(screen.getByLabelText(/Category/), "Compressors");
    await user.click(screen.getByText('+ Create "Compressors"'));
    await waitFor(() => expect(screen.getByText("Adding…")).toBeInTheDocument());

    await user.keyboard("{Control>}{Enter}{/Control}");

    expect(mockCreateCatalogItem).not.toHaveBeenCalled();
  });

  it("typing a category name without committing it never blocks or silently affects save", async () => {
    const user = userEvent.setup();
    mockCreateCatalogItem.mockResolvedValue(createdResult);
    renderDrawer();

    await fillRequiredFields(user);
    // The user starts typing a new category but never presses Enter or clicks Create.
    await user.type(screen.getByLabelText(/Category/), "Compressors");
    await user.click(screen.getByRole("button", { name: /save & activate/i }));

    await waitFor(() => expect(mockCreateCatalogItem).toHaveBeenCalledWith(
      expect.objectContaining({ categoryId: null }),
    ));
    expect(mockCreateCatalogCategory).not.toHaveBeenCalled();
  });

  it("keeps Save & activate disabled while the new category is being created, then releases it once resolved", async () => {
    const user = userEvent.setup();
    mockCreateCatalogItem.mockResolvedValue(createdResult);
    let resolveCreate: (v: unknown) => void = () => {};
    mockCreateCatalogCategory.mockReturnValue(new Promise((resolve) => (resolveCreate = resolve)));
    const { onClose } = renderDrawer();

    await fillRequiredFields(user);
    await user.type(screen.getByLabelText(/Category/), "Compressors");
    await user.click(screen.getByText('+ Create "Compressors"'));

    await waitFor(() => expect(screen.getByRole("button", { name: /^save & activate$/i })).toBeDisabled());
    expect(mockCreateCatalogItem).not.toHaveBeenCalled();
    expect(onClose).not.toHaveBeenCalled();

    resolveCreate({ id: "cat-2", name: "Compressors", displayOrder: 1, activeState: "Active", concurrencyVersion: "v1" });

    await waitFor(() => expect(screen.getByLabelText(/Category/)).toHaveValue("Compressors"));
    await waitFor(() => expect(screen.getByRole("button", { name: /^save & activate$/i })).toBeEnabled());
  });
});
