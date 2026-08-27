import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { CatalogItemEditDrawer } from "../CatalogItemEditDrawer";
import { ApiError } from "../../../lib/apiClient";
import type { CatalogCategoryResponse, CatalogItemResponse } from "../../../lib/apiClient";

const mockUpdateCatalogItemHeader = vi.fn();
const mockCreateCatalogCategory = vi.fn();
const mockGetCatalogCategories = vi.fn();

vi.mock("../../../lib/apiClient", async () => {
  const actual = await vi.importActual<typeof import("../../../lib/apiClient")>("../../../lib/apiClient");
  return {
    ...actual,
    api: {
      ...actual.api,
      updateCatalogItemHeader: (...args: unknown[]) => mockUpdateCatalogItemHeader(...args),
      createCatalogCategory: (...args: unknown[]) => mockCreateCatalogCategory(...args),
      getCatalogCategories: (...args: unknown[]) => mockGetCatalogCategories(...args),
    },
  };
});

const categories: CatalogCategoryResponse[] = [
  { id: "cat-1", name: "Refrigerant", displayOrder: 0, activeState: "Active", concurrencyVersion: "v1" },
];

const item: CatalogItemResponse = {
  id: "item-1",
  type: "Material",
  displayName: "Condensate Pump",
  externalKey: "CP20",
  categoryId: "cat-1",
  unitOfMeasure: "each",
  currency: "USD",
  isCommonItem: false,
  activeState: "Active",
  concurrencyVersion: "v7",
};

type DrawerProps = React.ComponentProps<typeof CatalogItemEditDrawer>;

function renderDrawer(props: Partial<DrawerProps> = {}) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const onClose = vi.fn();
  const onSaved = vi.fn();
  const onVersionConflict = vi.fn();
  const onCategoriesChanged = vi.fn();
  const tree = (extra: Partial<DrawerProps>) => (
    <QueryClientProvider client={queryClient}>
      <CatalogItemEditDrawer
        item={item}
        currentCategory={categories[0]}
        categories={categories}
        onCategoriesChanged={onCategoriesChanged}
        onClose={onClose}
        onSaved={onSaved}
        onVersionConflict={onVersionConflict}
        {...props}
        {...extra}
      />
    </QueryClientProvider>
  );
  const utils = render(tree({}));
  return {
    ...utils,
    onClose,
    onSaved,
    onVersionConflict,
    rerenderDrawer: (extra: Partial<DrawerProps>) => utils.rerender(tree(extra)),
  };
}

describe("CatalogItemEditDrawer", () => {
  beforeEach(() => {
    mockUpdateCatalogItemHeader.mockReset();
    mockCreateCatalogCategory.mockReset();
    mockGetCatalogCategories.mockReset().mockResolvedValue({ categories });
  });

  it("opens prefilled from the item", () => {
    renderDrawer();
    expect(screen.getByLabelText("Name")).toHaveValue("Condensate Pump");
    expect(screen.getByLabelText("SKU")).toHaveValue("CP20");
    expect(screen.getByRole("checkbox", { name: "Common item" })).not.toBeChecked();
  });

  it("renders as a right-side responsive drawer that fills the width on a phone", () => {
    renderDrawer();
    const panel = screen.getByRole("dialog");
    expect(panel.className).toContain("w-full");
    expect(panel.className).toContain("sm:w-[480px]");
  });

  it("blocks Save and reports a missing name from client validation", async () => {
    const user = userEvent.setup();
    renderDrawer();
    await user.clear(screen.getByLabelText("Name"));
    await user.click(screen.getByRole("button", { name: "Save" }));
    expect(screen.getByText("Display name is required.")).toBeInTheDocument();
    expect(mockUpdateCatalogItemHeader).not.toHaveBeenCalled();
    expect(screen.getByLabelText("Name")).toHaveFocus();
  });

  it("shows a server field error inline and does not report success", async () => {
    const user = userEvent.setup();
    mockUpdateCatalogItemHeader.mockRejectedValueOnce(
      new ApiError(409, "CatalogItem.ExternalKeyAlreadyExists", "conflict"),
    );
    const { onSaved } = renderDrawer();
    await user.clear(screen.getByLabelText("SKU"));
    await user.type(screen.getByLabelText("SKU"), "DUP");
    await user.click(screen.getByRole("button", { name: "Save" }));
    await waitFor(() =>
      expect(screen.getByText("A catalog item with this SKU already exists.")).toBeInTheDocument(),
    );
    expect(onSaved).not.toHaveBeenCalled();
  });

  it("closes immediately from a pristine Cancel", async () => {
    const user = userEvent.setup();
    const { onClose } = renderDrawer();
    await user.click(screen.getByRole("button", { name: "Cancel" }));
    expect(onClose).toHaveBeenCalled();
    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument();
  });

  it("guards a dirty Cancel behind a discard confirmation", async () => {
    const user = userEvent.setup();
    const { onClose } = renderDrawer();
    await user.type(screen.getByLabelText("Name"), " Mk2");
    await user.click(screen.getByRole("button", { name: "Cancel" }));

    await screen.findByRole("alertdialog");
    await user.click(screen.getByRole("button", { name: "Keep editing" }));
    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();

    await user.click(screen.getByRole("button", { name: "Cancel" }));
    await screen.findByRole("alertdialog");
    await user.click(screen.getByRole("button", { name: "Discard changes" }));
    expect(onClose).toHaveBeenCalled();
  });

  it("routes a dirty Escape through the discard confirmation instead of closing", async () => {
    const user = userEvent.setup();
    const { onClose } = renderDrawer();
    await user.type(screen.getByLabelText("Name"), " Mk2");
    await user.keyboard("{Escape}");

    expect(await screen.findByRole("alertdialog")).toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();
  });

  it("ignores a backdrop click while dirty — no close, no discard prompt", async () => {
    const user = userEvent.setup();
    const { onClose } = renderDrawer();
    await user.type(screen.getByLabelText("Name"), " Mk2");

    const overlay = screen.getByRole("dialog").parentElement as HTMLElement;
    await user.click(overlay);

    expect(onClose).not.toHaveBeenCalled();
    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument();
    expect(screen.getByLabelText("Name")).toBeInTheDocument();
  });

  it("saves the trimmed header with the current version and reports success", async () => {
    const user = userEvent.setup();
    mockUpdateCatalogItemHeader.mockResolvedValueOnce({ concurrencyVersion: "v8" });
    const { onSaved, onVersionConflict } = renderDrawer();

    await user.clear(screen.getByLabelText("Name"));
    await user.type(screen.getByLabelText("Name"), "  Condensate Pump Mk2  ");
    await user.click(screen.getByRole("checkbox", { name: "Common item" }));
    await user.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() =>
      expect(mockUpdateCatalogItemHeader).toHaveBeenCalledWith(
        "item-1",
        { displayName: "Condensate Pump Mk2", externalKey: "CP20", categoryId: "cat-1", isCommonItem: true },
        "v7",
      ),
    );
    await waitFor(() => expect(onSaved).toHaveBeenCalled());
    expect(onVersionConflict).not.toHaveBeenCalled();
  });

  it("hands a version conflict back to the page with the current draft", async () => {
    const user = userEvent.setup();
    mockUpdateCatalogItemHeader.mockRejectedValueOnce(
      new ApiError(409, "CatalogItem.VersionMismatch", "conflict"),
    );
    const { onSaved, onVersionConflict } = renderDrawer();

    await user.clear(screen.getByLabelText("Name"));
    await user.type(screen.getByLabelText("Name"), "Condensate Pump Mk2");
    await user.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() =>
      expect(onVersionConflict).toHaveBeenCalledWith(
        expect.objectContaining({ displayName: "Condensate Pump Mk2" }),
      ),
    );
    expect(onSaved).not.toHaveBeenCalled();
  });

  it("saves against the version captured at open even after a rerender bumps concurrencyVersion", async () => {
    const user = userEvent.setup();
    mockUpdateCatalogItemHeader.mockResolvedValueOnce({ concurrencyVersion: "v99" });
    const { rerenderDrawer } = renderDrawer(); // item.concurrencyVersion === "v7"

    await user.clear(screen.getByLabelText("Name"));
    await user.type(screen.getByLabelText("Name"), "Condensate Pump Mk2");

    // A background refetch lands while the drawer is open — same item, newer version.
    rerenderDrawer({ item: { ...item, displayName: "Renamed elsewhere", concurrencyVersion: "v42" } });
    expect((screen.getByLabelText("Name") as HTMLInputElement).value).toBe("Condensate Pump Mk2");

    await user.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() =>
      expect(mockUpdateCatalogItemHeader).toHaveBeenCalledWith(
        "item-1",
        expect.objectContaining({ displayName: "Condensate Pump Mk2" }),
        "v7",
      ),
    );
  });

  it("seeds from a restored conflict draft when one is provided", () => {
    renderDrawer({
      initialDraft: {
        displayName: "Restored Name",
        externalKey: "RX1",
        categoryId: "cat-1",
        isCommonItem: true,
      },
    });
    expect(screen.getByLabelText("Name")).toHaveValue("Restored Name");
    expect(screen.getByLabelText("SKU")).toHaveValue("RX1");
    expect(screen.getByRole("checkbox", { name: "Common item" })).toBeChecked();
  });

  it("treats a restored conflict draft as unsaved work and guards it on Cancel", async () => {
    const user = userEvent.setup();
    const { onClose } = renderDrawer({
      initialDraft: { displayName: "Restored Name", externalKey: "RX1", categoryId: "cat-1", isCommonItem: true },
    });

    await user.click(screen.getByRole("button", { name: "Cancel" }));
    await screen.findByRole("alertdialog");
    expect(onClose).not.toHaveBeenCalled();

    await user.click(screen.getByRole("button", { name: "Discard changes" }));
    expect(onClose).toHaveBeenCalled();
  });

  it("keeps the discard confirmation outside the form it disables", async () => {
    const user = userEvent.setup();
    renderDrawer();
    await user.type(screen.getByLabelText("Name"), " Mk2");
    await user.click(screen.getByRole("button", { name: "Cancel" }));

    const confirm = await screen.findByRole("alertdialog");
    const form = document.querySelector("form")!;
    expect(form.contains(confirm)).toBe(false);
  });

  it("disables Save while a new category is mid-creation", async () => {
    const user = userEvent.setup();
    mockCreateCatalogCategory.mockReturnValue(new Promise(() => {}));
    renderDrawer();

    const categoryInput = screen.getByLabelText("Category");
    await user.click(categoryInput);
    await user.clear(categoryInput);
    await user.type(categoryInput, "Ductwork");
    await user.click(await screen.findByText('+ Create "Ductwork"'));

    await waitFor(() => expect(screen.getByRole("button", { name: "Save" })).toBeDisabled());
  });
});
