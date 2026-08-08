import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { CategoryCombobox } from "../CategoryCombobox";
import { ApiError } from "../../../lib/apiClient";
import type { CatalogCategoryResponse } from "../../../lib/apiClient";

const mockCreateCatalogCategory = vi.fn();
const mockGetCatalogCategories = vi.fn();

vi.mock("../../../lib/apiClient", async () => {
  const actual = await vi.importActual<typeof import("../../../lib/apiClient")>("../../../lib/apiClient");
  return {
    ...actual,
    api: {
      ...actual.api,
      createCatalogCategory: (...args: unknown[]) => mockCreateCatalogCategory(...args),
      getCatalogCategories: (...args: unknown[]) => mockGetCatalogCategories(...args),
    },
  };
});

const categories: CatalogCategoryResponse[] = [
  { id: "cat-1", name: "Refrigerant", displayOrder: 0, activeState: "Active", concurrencyVersion: "v1" },
  { id: "cat-2", name: "Compressors", displayOrder: 1, activeState: "Active", concurrencyVersion: "v1" },
];

function renderCombobox(props: Partial<React.ComponentProps<typeof CategoryCombobox>> = {}) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const onSelect = vi.fn();
  const utils = render(
    <QueryClientProvider client={queryClient}>
      <CategoryCombobox id="cat-combo" categories={categories} currentCategoryId={null} onSelect={onSelect} {...props} />
    </QueryClientProvider>,
  );
  return { ...utils, onSelect };
}

describe("CategoryCombobox", () => {
  beforeEach(() => {
    mockCreateCatalogCategory.mockReset();
    mockGetCatalogCategories.mockReset();
  });

  it("shows the currently selected category's name", () => {
    renderCombobox({ currentCategoryId: "cat-1" });
    expect(screen.getByRole("combobox")).toHaveValue("Refrigerant");
  });

  it("opens on focus and lists 'No category' plus all categories", async () => {
    const user = userEvent.setup();
    renderCombobox();
    await user.click(screen.getByRole("combobox"));

    expect(screen.getByRole("option", { name: "No category" })).toBeInTheDocument();
    expect(screen.getByRole("option", { name: "Refrigerant" })).toBeInTheDocument();
    expect(screen.getByRole("option", { name: "Compressors" })).toBeInTheDocument();
  });

  it("filters options as the user types", async () => {
    const user = userEvent.setup();
    renderCombobox();
    await user.type(screen.getByRole("combobox"), "Comp");

    expect(screen.getByRole("option", { name: "Compressors" })).toBeInTheDocument();
    expect(screen.queryByRole("option", { name: "Refrigerant" })).not.toBeInTheDocument();
  });

  it("selecting an existing category commits it without any network call", async () => {
    const user = userEvent.setup();
    const { onSelect } = renderCombobox();
    await user.click(screen.getByRole("combobox"));
    await user.click(screen.getByRole("option", { name: "Refrigerant" }));

    expect(onSelect).toHaveBeenCalledWith("cat-1");
    expect(screen.getByRole("combobox")).toHaveValue("Refrigerant");
    expect(mockCreateCatalogCategory).not.toHaveBeenCalled();
  });

  it("selecting 'No category' commits null", async () => {
    const user = userEvent.setup();
    const { onSelect } = renderCombobox({ currentCategoryId: "cat-1" });
    await user.click(screen.getByRole("combobox"));
    await user.click(screen.getByRole("option", { name: "No category" }));

    expect(onSelect).toHaveBeenCalledWith(null);
    expect(screen.getByRole("combobox")).toHaveValue("");
  });

  it("typing without selecting never commits — closing reverts the draft text", async () => {
    const user = userEvent.setup();
    const { onSelect } = renderCombobox({ currentCategoryId: "cat-1" });
    await user.click(screen.getByRole("combobox"));
    await user.type(screen.getByRole("combobox"), "zzz");
    await user.keyboard("{Escape}");

    expect(onSelect).not.toHaveBeenCalled();
    expect(screen.getByRole("combobox")).toHaveValue("Refrigerant");
  });

  it("does not offer a create option when creatable is false", async () => {
    const user = userEvent.setup();
    renderCombobox({ creatable: false });
    await user.type(screen.getByRole("combobox"), "Brand New");

    expect(screen.queryByText('+ Create "Brand New"')).not.toBeInTheDocument();
  });

  it("creating a new category selects it and reports pending across the create call", async () => {
    const user = userEvent.setup();
    let resolveCreate: (v: CatalogCategoryResponse) => void = () => {};
    mockCreateCatalogCategory.mockReturnValue(
      new Promise<CatalogCategoryResponse>((resolve) => {
        resolveCreate = resolve;
      }),
    );
    const onPendingChange = vi.fn();
    const onCategoriesChanged = vi.fn();
    const { onSelect } = renderCombobox({ creatable: true, onPendingChange, onCategoriesChanged });

    await user.type(screen.getByRole("combobox"), "Ductwork");
    await user.click(screen.getByText('+ Create "Ductwork"'));

    expect(onPendingChange).toHaveBeenLastCalledWith(true);
    expect(mockCreateCatalogCategory).toHaveBeenCalledWith({ name: "Ductwork", displayOrder: 2 });

    resolveCreate({ id: "cat-3", name: "Ductwork", displayOrder: 2, activeState: "Active", concurrencyVersion: "v1" });

    await waitFor(() => expect(onSelect).toHaveBeenCalledWith("cat-3"));
    expect(onPendingChange).toHaveBeenLastCalledWith(false);
    expect(onCategoriesChanged).toHaveBeenCalled();
    expect(screen.getByRole("combobox")).toHaveValue("Ductwork");
  });

  it("a duplicate-name conflict re-fetches and selects the existing category instead of failing", async () => {
    const user = userEvent.setup();
    mockCreateCatalogCategory.mockRejectedValue(new ApiError(409, "CatalogCategory.NameAlreadyExists", "exists"));
    mockGetCatalogCategories.mockResolvedValue({
      categories: [...categories, { id: "cat-3", name: "Ductwork", displayOrder: 2, activeState: "Active", concurrencyVersion: "v1" }],
    });
    const onPendingChange = vi.fn();
    const { onSelect } = renderCombobox({ creatable: true, onPendingChange });

    await user.type(screen.getByRole("combobox"), "Ductwork");
    await user.click(screen.getByText('+ Create "Ductwork"'));

    await waitFor(() => expect(onSelect).toHaveBeenCalledWith("cat-3"));
    expect(onPendingChange).toHaveBeenLastCalledWith(false);
    expect(screen.getByRole("combobox")).toHaveValue("Ductwork");
  });

  it("stays pending and offers Try again on a generic create failure, blocking silent progress", async () => {
    const user = userEvent.setup();
    mockCreateCatalogCategory.mockRejectedValueOnce(new Error("network down"));
    const onPendingChange = vi.fn();
    const { onSelect } = renderCombobox({ creatable: true, onPendingChange });

    await user.type(screen.getByRole("combobox"), "Ductwork");
    await user.click(screen.getByText('+ Create "Ductwork"'));

    await waitFor(() => expect(screen.getByText("Couldn't add that category. Try again.")).toBeInTheDocument());
    expect(onPendingChange).toHaveBeenLastCalledWith(true);
    expect(onSelect).not.toHaveBeenCalled();

    mockCreateCatalogCategory.mockResolvedValueOnce({
      id: "cat-3",
      name: "Ductwork",
      displayOrder: 2,
      activeState: "Active",
      concurrencyVersion: "v1",
    });
    await user.click(screen.getByRole("button", { name: "Try again" }));

    await waitFor(() => expect(onSelect).toHaveBeenCalledWith("cat-3"));
    expect(onPendingChange).toHaveBeenLastCalledWith(false);
  });

  it("selecting an existing category abandons a stuck create error rather than leaving it pending", async () => {
    const user = userEvent.setup();
    mockCreateCatalogCategory.mockRejectedValueOnce(new Error("network down"));
    const onPendingChange = vi.fn();
    const { onSelect } = renderCombobox({ creatable: true, onPendingChange });

    await user.type(screen.getByRole("combobox"), "Ductwork");
    await user.click(screen.getByText('+ Create "Ductwork"'));
    await waitFor(() => expect(screen.getByText("Couldn't add that category. Try again.")).toBeInTheDocument());

    await user.clear(screen.getByRole("combobox"));
    await user.click(screen.getByRole("combobox"));
    await user.click(screen.getByRole("option", { name: "Refrigerant" }));

    expect(onSelect).toHaveBeenLastCalledWith("cat-1");
    expect(onPendingChange).toHaveBeenLastCalledWith(false);
    expect(screen.queryByText("Couldn't add that category. Try again.")).not.toBeInTheDocument();
  });

  it("is disabled and closed when the disabled prop is set", async () => {
    const user = userEvent.setup();
    renderCombobox({ disabled: true });
    expect(screen.getByRole("combobox")).toBeDisabled();
    await user.click(screen.getByRole("combobox"));
    expect(screen.queryByRole("listbox")).not.toBeInTheDocument();
  });

  // build-log/114 2e.7b UX correction (2026-08-08): the create path was technically present but
  // undiscoverable — these lock the guided placeholder, discovery hint, default-highlighted create
  // option, and Tab-never-creates behavior.
  describe("2e.7b guided-discovery correction", () => {
    it("shows the guided placeholder only when creatable, and the filter's noneLabel otherwise", () => {
      const { unmount } = renderCombobox({ creatable: true });
      expect(screen.getByRole("combobox")).toHaveAttribute("placeholder", "Search or create category…");
      unmount();

      renderCombobox({ creatable: false, noneLabel: "All categories" });
      expect(screen.getByRole("combobox")).toHaveAttribute("placeholder", "All categories");
    });

    it("an explicit placeholder prop overrides the guided default", () => {
      renderCombobox({ creatable: true, placeholder: "Custom…" });
      expect(screen.getByRole("combobox")).toHaveAttribute("placeholder", "Custom…");
    });

    it("shows a non-selectable discovery hint on open with an empty query, only when creatable", async () => {
      const user = userEvent.setup();
      const { unmount } = renderCombobox({ creatable: true });
      await user.click(screen.getByRole("combobox"));
      expect(screen.getByText(/Type a new name to create category/)).toBeInTheDocument();
      expect(screen.queryByRole("option", { name: /Type a new name/ })).not.toBeInTheDocument();
      unmount();

      renderCombobox({ creatable: false });
      await user.click(screen.getByRole("combobox"));
      expect(screen.queryByText(/Type a new name to create category/)).not.toBeInTheDocument();
    });

    it("exposes the discovery hint to assistive technology via aria-describedby even when not visibly shown", () => {
      renderCombobox({ creatable: true });
      const input = screen.getByRole("combobox");
      const describedById = input.getAttribute("aria-describedby");
      expect(describedById).toBeTruthy();
      expect(document.getElementById(describedById!)).toHaveTextContent(/Type a new name to create category/);
    });

    it("hides the visible discovery footer (but keeps the always-on AT description) once the user starts typing", async () => {
      const user = userEvent.setup();
      renderCombobox({ creatable: true });
      await user.click(screen.getByRole("combobox"));
      expect(screen.getByRole("presentation")).toHaveTextContent(/Type a new name to create category/);

      await user.type(screen.getByRole("combobox"), "Duct");
      expect(screen.queryByRole("presentation")).not.toBeInTheDocument();
    });

    it("the create option is the default highlighted option even over a partial category match", async () => {
      const user = userEvent.setup();
      renderCombobox({ creatable: true, categories: [{ id: "cat-3", name: "Ductless split", displayOrder: 0, activeState: "Active", concurrencyVersion: "v1" }] });
      await user.type(screen.getByRole("combobox"), "Duct");

      expect(screen.getByRole("combobox")).toHaveAttribute("aria-activedescendant", "cat-combo-option-create");
      // Enter should invoke the highlighted create option, not the partially-matching category.
      await user.keyboard("{Enter}");
      expect(mockCreateCatalogCategory).toHaveBeenCalledWith({ name: "Duct", displayOrder: 1 });
    });

    it("Tab never creates or changes a category — it closes the popup and reverts the draft like Escape", async () => {
      const user = userEvent.setup();
      const { onSelect } = renderCombobox({ creatable: true, currentCategoryId: "cat-1" });
      await user.click(screen.getByRole("combobox"));
      await user.clear(screen.getByRole("combobox"));
      await user.type(screen.getByRole("combobox"), "Ductwork");
      expect(screen.getByText('+ Create "Ductwork"')).toBeInTheDocument();

      await user.tab();

      expect(onSelect).not.toHaveBeenCalled();
      expect(mockCreateCatalogCategory).not.toHaveBeenCalled();
      expect(screen.getByRole("combobox")).toHaveValue("Refrigerant");
      expect(screen.queryByRole("listbox")).not.toBeInTheDocument();
    });
  });

  // build-log/114 2e.7b scale/ordering correction (2026-08-08): required proof for a 15+
  // (preferably 50) category account — "No category" and the create action must stay reachable
  // without scrolling past a long category list.
  describe("2e.7b scale correction (15-50 categories)", () => {
    function manyCategories(count: number): CatalogCategoryResponse[] {
      return Array.from({ length: count }, (_, i) => ({
        id: `cat-${i}`,
        name: `Category ${String(i).padStart(2, "0")}`,
        displayOrder: i,
        activeState: "Active" as const,
        concurrencyVersion: "v1",
      }));
    }

    it("pins No category above the scrollable region, not inside it, with 50 categories", async () => {
      const user = userEvent.setup();
      renderCombobox({ creatable: true, categories: manyCategories(50) });
      await user.click(screen.getByRole("combobox"));

      const noneOption = screen.getByRole("option", { name: "No category" });
      const firstCategoryOption = screen.getByRole("option", { name: "Category 00" });
      const scrollRegion = firstCategoryOption.parentElement;

      expect(scrollRegion?.className).toContain("overflow-y-auto");
      expect(scrollRegion?.contains(noneOption)).toBe(false);
    });

    it("caps the scrollable category region's height so it cannot overrun the viewport", async () => {
      const user = userEvent.setup();
      renderCombobox({ creatable: true, categories: manyCategories(50) });
      await user.click(screen.getByRole("combobox"));

      const scrollRegion = screen.getByRole("option", { name: "Category 00" }).parentElement;
      expect(scrollRegion?.className).toMatch(/max-h-(56|60|64)/);
    });

    it("pins the create action below the scrollable list rather than appending it after many partial matches", async () => {
      const user = userEvent.setup();
      // Every category partially matches "Category" (no exact match, since none is named exactly
      // "Category") so a naive append-after-matches placement would push the create action far
      // down a 50-row scroll.
      renderCombobox({ creatable: true, categories: manyCategories(50) });
      await user.type(screen.getByRole("combobox"), "Category");

      const createOption = screen.getByRole("option", { name: /\+ Create/ });
      const scrollRegion = screen.getByRole("option", { name: "Category 00" }).parentElement;

      expect(scrollRegion?.contains(createOption)).toBe(false);
      // It's the default-highlighted option regardless of how many partial matches sort above it.
      expect(screen.getByRole("combobox")).toHaveAttribute(
        "aria-activedescendant",
        createOption.id,
      );
    });

    it("remains keyboard-reachable end to end with 50 categories: type, land on Create by default, Enter creates it", async () => {
      const user = userEvent.setup();
      mockCreateCatalogCategory.mockResolvedValue({
        id: "cat-new",
        name: "Brand New",
        displayOrder: 50,
        activeState: "Active",
        concurrencyVersion: "v1",
      });
      const { onSelect } = renderCombobox({ creatable: true, categories: manyCategories(50) });

      await user.type(screen.getByRole("combobox"), "Brand New");
      await user.keyboard("{Enter}");

      await waitFor(() => expect(onSelect).toHaveBeenCalledWith("cat-new"));
      expect(mockCreateCatalogCategory).toHaveBeenCalledWith({ name: "Brand New", displayOrder: 50 });
    });

    it("No category is still reachable by keyboard alone with 50 categories present", async () => {
      const user = userEvent.setup();
      const { onSelect } = renderCombobox({ creatable: true, categories: manyCategories(50), currentCategoryId: "cat-3" });
      await user.click(screen.getByRole("combobox"));
      await user.keyboard("{Enter}");

      expect(onSelect).toHaveBeenCalledWith(null);
    });
  });
});
