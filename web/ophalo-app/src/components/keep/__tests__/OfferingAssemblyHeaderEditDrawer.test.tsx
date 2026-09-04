import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { OfferingAssemblyHeaderEditDrawer } from "../OfferingAssemblyHeaderEditDrawer";
import { ApiError } from "../../../lib/apiClient";
import type { CatalogItemListResult, OfferingAssemblyDetailResult } from "../../../lib/apiClient";

const mockUpdateOfferingAssemblyHeader = vi.fn();
const mockGetCatalogItems = vi.fn();

vi.mock("../../../lib/apiClient", async () => {
  const actual = await vi.importActual<typeof import("../../../lib/apiClient")>("../../../lib/apiClient");
  return {
    ...actual,
    api: {
      ...actual.api,
      updateOfferingAssemblyHeader: (...args: unknown[]) => mockUpdateOfferingAssemblyHeader(...args),
      getCatalogItems: (...args: unknown[]) => mockGetCatalogItems(...args),
    },
  };
});

const assembly: OfferingAssemblyDetailResult = {
  id: "assembly-1",
  name: "Furnace Tune-Up",
  primaryCatalogItemId: "item-primary",
  primaryCatalogItemDisplayName: "Furnace Inspection",
  priceTreatment: "Summed",
  activeState: "Active",
  concurrencyVersion: "v4",
  items: [],
  isOperationallyEligible: true,
  eligibilityReasons: [],
  pricing: {
    priceStatus: "Priced",
    calculatedSellPrice: 100,
    marginStatus: "Ready",
    missingCostLineCount: 0,
    priceReasons: [],
    marginReasons: [],
  },
};

const catalogPage: CatalogItemListResult = {
  items: [
    {
      item: { id: "item-alt", type: "Service", displayName: "Boiler Inspection", externalKey: null, categoryId: null, unitOfMeasure: "each", currency: "USD", isCommonItem: false, activeState: "Active", concurrencyVersion: "v1" },
      currentPricingMode: "StandalonePrice",
      currentSellPrice: 120,
      currentCost: 60,
      matchRank: "DisplayName",
      matchReason: null,
    },
  ],
  limit: 20,
  hasMore: false,
  nextCursor: null,
};

type DrawerProps = React.ComponentProps<typeof OfferingAssemblyHeaderEditDrawer>;

function renderDrawer(props: Partial<DrawerProps> = {}) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const onClose = vi.fn();
  const onSaved = vi.fn();
  const onVersionConflict = vi.fn();
  const tree = (extra: Partial<DrawerProps>) => (
    <QueryClientProvider client={queryClient}>
      <OfferingAssemblyHeaderEditDrawer
        assembly={assembly}
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

describe("OfferingAssemblyHeaderEditDrawer", () => {
  beforeEach(() => {
    mockUpdateOfferingAssemblyHeader.mockReset();
    mockGetCatalogItems.mockReset().mockResolvedValue(catalogPage);
  });

  it("opens prefilled from the assembly", () => {
    renderDrawer();
    expect(screen.getByLabelText("Name")).toHaveValue("Furnace Tune-Up");
    expect(screen.getByRole("radio", { name: "Summed" })).toBeChecked();
  });

  it("renders as a right-side responsive drawer that fills the width on a phone", () => {
    renderDrawer();
    const panel = screen.getByRole("dialog");
    expect(panel.className).toContain("w-full");
    expect(panel.className).toContain("sm:w-[520px]");
  });

  it("blocks Save and reports a missing name from client validation", async () => {
    const user = userEvent.setup();
    renderDrawer();
    await user.clear(screen.getByLabelText("Name"));
    await user.click(screen.getByRole("button", { name: "Save" }));
    expect(screen.getByText("Name is required.")).toBeInTheDocument();
    expect(mockUpdateOfferingAssemblyHeader).not.toHaveBeenCalled();
    expect(screen.getByLabelText("Name")).toHaveFocus();
  });

  it("shows a server field error inline and does not report success", async () => {
    const user = userEvent.setup();
    mockUpdateOfferingAssemblyHeader.mockRejectedValueOnce(
      new ApiError(409, "OfferingAssembly.PrimaryCatalogItemAlreadyClaimed", "conflict"),
    );
    const { onSaved } = renderDrawer();
    await user.type(screen.getByLabelText("Name"), " Plus");
    await user.click(screen.getByRole("button", { name: "Save" }));
    await waitFor(() =>
      expect(
        screen.getByText("Another active offering/assembly already uses this primary catalog item."),
      ).toBeInTheDocument(),
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
    await user.type(screen.getByLabelText("Name"), " Plus");
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
    await user.type(screen.getByLabelText("Name"), " Plus");
    await user.keyboard("{Escape}");

    expect(await screen.findByRole("alertdialog")).toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();
  });

  it("ignores a backdrop click while dirty — no close, no discard prompt", async () => {
    const user = userEvent.setup();
    const { onClose } = renderDrawer();
    await user.type(screen.getByLabelText("Name"), " Plus");

    const overlay = screen.getByRole("dialog").parentElement as HTMLElement;
    await user.click(overlay);

    expect(onClose).not.toHaveBeenCalled();
    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument();
    expect(screen.getByLabelText("Name")).toBeInTheDocument();
  });

  it("saves against the version captured at open even after a rerender bumps concurrencyVersion", async () => {
    const user = userEvent.setup();
    mockUpdateOfferingAssemblyHeader.mockResolvedValueOnce({ ...assembly, concurrencyVersion: "v9" });
    const { rerenderDrawer } = renderDrawer(); // assembly.concurrencyVersion === "v4"

    await user.clear(screen.getByLabelText("Name"));
    await user.type(screen.getByLabelText("Name"), "Furnace Tune-Up Deluxe");

    // A background refetch lands while the drawer is open — same assembly, newer version.
    rerenderDrawer({ assembly: { ...assembly, name: "Renamed elsewhere", concurrencyVersion: "v42" } });
    expect((screen.getByLabelText("Name") as HTMLInputElement).value).toBe("Furnace Tune-Up Deluxe");

    await user.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() =>
      expect(mockUpdateOfferingAssemblyHeader).toHaveBeenCalledWith(
        "assembly-1",
        expect.objectContaining({ name: "Furnace Tune-Up Deluxe" }),
        "v4",
      ),
    );
  });

  it("saves the trimmed header with the current version and reports success", async () => {
    const user = userEvent.setup();
    mockUpdateOfferingAssemblyHeader.mockResolvedValueOnce({ ...assembly, concurrencyVersion: "v5" });
    const { onSaved, onVersionConflict } = renderDrawer();

    await user.clear(screen.getByLabelText("Name"));
    await user.type(screen.getByLabelText("Name"), "  Furnace Tune-Up Deluxe  ");
    await user.click(screen.getByRole("radio", { name: "All-inclusive" }));
    await user.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() =>
      expect(mockUpdateOfferingAssemblyHeader).toHaveBeenCalledWith(
        "assembly-1",
        { primaryCatalogItemId: "item-primary", name: "Furnace Tune-Up Deluxe", priceTreatment: "AllInclusive" },
        "v4",
      ),
    );
    await waitFor(() => expect(onSaved).toHaveBeenCalled());
    expect(onVersionConflict).not.toHaveBeenCalled();
  });

  it("hands a version conflict back to the page with the current draft", async () => {
    const user = userEvent.setup();
    mockUpdateOfferingAssemblyHeader.mockRejectedValueOnce(
      new ApiError(409, "OfferingAssembly.VersionMismatch", "conflict"),
    );
    const { onSaved, onVersionConflict } = renderDrawer();

    await user.type(screen.getByLabelText("Name"), " Deluxe");
    await user.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() =>
      expect(onVersionConflict).toHaveBeenCalledWith(
        expect.objectContaining({ name: "Furnace Tune-Up Deluxe" }),
      ),
    );
    expect(onSaved).not.toHaveBeenCalled();
  });

  it("seeds from a restored conflict draft when one is provided", () => {
    renderDrawer({
      initialDraft: {
        primaryCatalogItemId: "item-primary",
        primaryCatalogItemDisplayName: "Furnace Inspection",
        name: "Restored Name",
        priceTreatment: "AllInclusive",
      },
    });
    expect(screen.getByLabelText("Name")).toHaveValue("Restored Name");
    expect(screen.getByRole("radio", { name: "All-inclusive" })).toBeChecked();
  });

  it("treats a restored conflict draft as unsaved work and guards it on Cancel", async () => {
    const user = userEvent.setup();
    const { onClose } = renderDrawer({
      initialDraft: {
        primaryCatalogItemId: "item-primary",
        primaryCatalogItemDisplayName: "Furnace Inspection",
        name: "Restored Name",
        priceTreatment: "AllInclusive",
      },
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
    await user.type(screen.getByLabelText("Name"), " Plus");
    await user.click(screen.getByRole("button", { name: "Cancel" }));

    const confirm = await screen.findByRole("alertdialog");
    const form = document.querySelector("form")!;
    expect(form.contains(confirm)).toBe(false);
  });

  it("saves a changed primary catalog item", async () => {
    const user = userEvent.setup();
    mockUpdateOfferingAssemblyHeader.mockResolvedValueOnce({ ...assembly, concurrencyVersion: "v5" });
    renderDrawer();

    await user.click(screen.getByRole("combobox"));
    const listbox = await screen.findByRole("listbox");
    await user.click(within(listbox).getByText("Boiler Inspection"));
    await user.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() =>
      expect(mockUpdateOfferingAssemblyHeader).toHaveBeenCalledWith(
        "assembly-1",
        expect.objectContaining({ primaryCatalogItemId: "item-alt" }),
        "v4",
      ),
    );
  });
});
