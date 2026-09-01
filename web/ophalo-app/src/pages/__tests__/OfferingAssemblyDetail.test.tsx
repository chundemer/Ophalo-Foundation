import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { OfferingAssemblyDetail } from "../OfferingAssemblyDetail";
import { ApiError } from "../../lib/apiClient";
import type { OfferingAssemblyDetailResult } from "../../lib/apiClient";

const mockGetOfferingAssembly = vi.fn();
const mockUpdateOfferingAssemblyHeader = vi.fn();
const mockActivateOfferingAssembly = vi.fn();
const mockInactivateOfferingAssembly = vi.fn();
const mockAddOfferingAssemblyItem = vi.fn();
const mockUpdateOfferingAssemblyItem = vi.fn();
const mockRemoveOfferingAssemblyItem = vi.fn();
const mockGetCatalogItems = vi.fn();

vi.mock("../../lib/apiClient", async () => {
  const actual = await vi.importActual<typeof import("../../lib/apiClient")>("../../lib/apiClient");
  return {
    ...actual,
    api: {
      ...actual.api,
      getOfferingAssembly: (...args: unknown[]) => mockGetOfferingAssembly(...args),
      updateOfferingAssemblyHeader: (...args: unknown[]) => mockUpdateOfferingAssemblyHeader(...args),
      activateOfferingAssembly: (...args: unknown[]) => mockActivateOfferingAssembly(...args),
      inactivateOfferingAssembly: (...args: unknown[]) => mockInactivateOfferingAssembly(...args),
      addOfferingAssemblyItem: (...args: unknown[]) => mockAddOfferingAssemblyItem(...args),
      updateOfferingAssemblyItem: (...args: unknown[]) => mockUpdateOfferingAssemblyItem(...args),
      removeOfferingAssemblyItem: (...args: unknown[]) => mockRemoveOfferingAssemblyItem(...args),
      getCatalogItems: (...args: unknown[]) => mockGetCatalogItems(...args),
    },
  };
});

function renderDetail(props: Partial<React.ComponentProps<typeof OfferingAssemblyDetail>> = {}) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const invalidateSpy = vi.spyOn(queryClient, "invalidateQueries");
  const onBack = vi.fn();
  const onRetryEntitlement = vi.fn();
  const onSelectCatalogItem = vi.fn();
  const utils = render(
    <QueryClientProvider client={queryClient}>
      <OfferingAssemblyDetail
        offeringAssemblyId="assembly-1"
        role="owner"
        entitled={true}
        entitlementLoading={false}
        entitlementError={false}
        onRetryEntitlement={onRetryEntitlement}
        onBack={onBack}
        onSelectCatalogItem={onSelectCatalogItem}
        {...props}
      />
    </QueryClientProvider>,
  );
  return { ...utils, onBack, onRetryEntitlement, onSelectCatalogItem, queryClient, invalidateSpy };
}

const baseAssembly: OfferingAssemblyDetailResult = {
  id: "assembly-1",
  name: "Furnace Tune-Up",
  primaryCatalogItemId: "item-primary",
  primaryCatalogItemDisplayName: "Furnace Inspection",
  priceTreatment: "Summed",
  activeState: "Active",
  concurrencyVersion: "v1",
  items: [
    { id: "line-1", catalogItemId: "item-b", catalogItemDisplayName: "Filter", defaultQuantity: 1, isOptional: false, displayOrder: 0 },
  ],
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

function invalidatedOfferingAssemblies(spy: ReturnType<typeof vi.spyOn>): boolean {
  return spy.mock.calls.some(([arg]: [unknown]) => {
    const key = (arg as { queryKey?: unknown[] })?.queryKey;
    return Array.isArray(key) && key[0] === "offeringAssemblies";
  });
}

describe("OfferingAssemblyDetail", () => {
  beforeEach(() => {
    mockGetOfferingAssembly.mockReset();
    mockUpdateOfferingAssemblyHeader.mockReset();
    mockActivateOfferingAssembly.mockReset();
    mockInactivateOfferingAssembly.mockReset();
    mockAddOfferingAssemblyItem.mockReset();
    mockUpdateOfferingAssemblyItem.mockReset();
    mockRemoveOfferingAssemblyItem.mockReset();
    mockGetCatalogItems.mockReset().mockResolvedValue({ items: [], limit: 20, hasMore: false, nextCursor: null });
  });

  it("renders the assembly header, primary item, price treatment, and its associated item", async () => {
    mockGetOfferingAssembly.mockResolvedValue(baseAssembly);
    renderDetail();

    await waitFor(() => expect(screen.getByText("Furnace Tune-Up")).toBeInTheDocument());
    expect(screen.getByText(/Furnace Inspection/)).toBeInTheDocument();
    expect(screen.getByText("Filter")).toBeInTheDocument();
    expect(screen.queryByText("Needs review")).not.toBeInTheDocument();
  });

  it("keeps the assembly detail visible behind the header edit drawer", async () => {
    const user = userEvent.setup();
    mockGetOfferingAssembly.mockResolvedValue(baseAssembly);
    renderDetail();

    await user.click(await screen.findByRole("button", { name: "Edit" }));

    expect(screen.getByRole("dialog", { name: "Edit offering/assembly" })).toBeInTheDocument();
    expect(screen.getByText("Furnace Tune-Up")).toBeInTheDocument();
    expect(screen.getByText("Filter")).toBeInTheDocument();
  });

  it("shows a Needs review badge and the eligibility reasons when the assembly is not operationally eligible", async () => {
    mockGetOfferingAssembly.mockResolvedValue({
      ...baseAssembly,
      isOperationallyEligible: false,
      eligibilityReasons: [{ code: "ComponentInactive", componentCatalogItemId: "item-b" }],
    });
    renderDetail();

    await waitFor(() => expect(screen.getAllByText("Needs review").length).toBeGreaterThan(0));
    expect(screen.getByText("An associated item is inactive.")).toBeInTheDocument();
  });

  it("renders separate actionable price and margin groups, and marks every affected associated item", async () => {
    mockGetOfferingAssembly.mockResolvedValue({
      ...baseAssembly,
      items: [
        { id: "line-price", catalogItemId: "item-price", catalogItemDisplayName: "Price item", defaultQuantity: 1, isOptional: false, displayOrder: 0 },
        { id: "line-margin", catalogItemId: "item-margin", catalogItemDisplayName: "Margin item", defaultQuantity: 1, isOptional: false, displayOrder: 1 },
        { id: "line-both", catalogItemId: "item-both", catalogItemDisplayName: "Both item", defaultQuantity: 1, isOptional: false, displayOrder: 2 },
      ],
      pricing: {
        priceStatus: "NeedsReview",
        calculatedSellPrice: null,
        marginStatus: "NeedsCostReview",
        missingCostLineCount: 2,
        priceReasons: [
          { code: "RequiredComponentMissingStandaloneSellPrice", catalogItemId: "item-price", catalogItemDisplayName: "Price item" },
          { code: "RequiredComponentMissingStandaloneSellPrice", catalogItemId: "item-both", catalogItemDisplayName: "Both item" },
        ],
        marginReasons: [
          { code: "RequiredComponentMissingBusinessCost", catalogItemId: "item-margin", catalogItemDisplayName: "Margin item" },
          { code: "RequiredComponentMissingBusinessCost", catalogItemId: "item-both", catalogItemDisplayName: "Both item" },
        ],
      },
    });
    const user = userEvent.setup();
    const { onSelectCatalogItem } = renderDetail();

    await screen.findAllByText("Price needs review");
    expect(screen.getByText("Margin needs cost review (2)")).toBeInTheDocument();
    expect(screen.getByText("Price")).toBeInTheDocument();
    expect(screen.getByText("Margin")).toBeInTheDocument();
    expect(screen.getAllByText("Price needs review")).toHaveLength(3);
    expect(screen.getAllByText("Margin needs cost review")).toHaveLength(2);

    // Margin reasons are explicitly cost-oriented ("Review cost"), distinct from price reasons
    // ("Review price") — a generic "Review" no longer tells the operator which fix to make.
    const priceReviewButtons = screen.getAllByRole("button", { name: "Review price" });
    const marginReviewButtons = screen.getAllByRole("button", { name: "Review cost" });
    expect(priceReviewButtons).toHaveLength(2);
    expect(marginReviewButtons).toHaveLength(2);

    await user.click(priceReviewButtons[0]);
    await user.click(marginReviewButtons[0]);
    expect(onSelectCatalogItem).toHaveBeenNthCalledWith(1, "item-price", "price");
    expect(onSelectCatalogItem).toHaveBeenNthCalledWith(2, "item-margin", "margin");
  });

  it("saving a header edit calls updateOfferingAssemblyHeader and invalidates both the detail and list queries", async () => {
    const user = userEvent.setup();
    mockGetOfferingAssembly.mockResolvedValue(baseAssembly);
    mockUpdateOfferingAssemblyHeader.mockResolvedValue({ concurrencyVersion: "v2" });
    const { invalidateSpy } = renderDetail();

    await waitFor(() => expect(screen.getByText("Furnace Tune-Up")).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: "Edit" }));

    const nameInput = screen.getByLabelText("Name") as HTMLInputElement;
    await user.clear(nameInput);
    await user.type(nameInput, "Furnace Tune-Up Deluxe");
    await user.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() =>
      expect(mockUpdateOfferingAssemblyHeader).toHaveBeenCalledWith(
        "assembly-1",
        { primaryCatalogItemId: "item-primary", name: "Furnace Tune-Up Deluxe", priceTreatment: "Summed" },
        "v1",
      ),
    );
    await waitFor(() => expect(invalidatedOfferingAssemblies(invalidateSpy)).toBe(true));
  });

  it("a version conflict on header save exits edit mode and refreshes without losing the read-only view", async () => {
    const user = userEvent.setup();
    const updatedElsewhere: OfferingAssemblyDetailResult = { ...baseAssembly, name: "Furnace Tune-Up (renamed)", concurrencyVersion: "v2" };
    mockGetOfferingAssembly.mockResolvedValueOnce(baseAssembly).mockResolvedValue(updatedElsewhere);
    mockUpdateOfferingAssemblyHeader.mockRejectedValueOnce(new ApiError(409, "OfferingAssembly.VersionMismatch", "conflict"));
    renderDetail();

    await waitFor(() => expect(screen.getByText("Furnace Tune-Up")).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: "Edit" }));
    await user.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(screen.getByText("Furnace Tune-Up (renamed)")).toBeInTheDocument());
    expect(screen.queryByLabelText("Name")).not.toBeInTheDocument();
    await waitFor(() =>
      expect(screen.getByText(/was changed by someone else/)).toHaveFocus(),
    );
  });

  it("returns focus to the Edit trigger after the header drawer is dismissed", async () => {
    const user = userEvent.setup();
    mockGetOfferingAssembly.mockResolvedValue(baseAssembly);
    renderDetail();

    await waitFor(() => expect(screen.getByText("Furnace Tune-Up")).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: "Edit" }));
    await user.keyboard("{Escape}");

    await waitFor(() => expect(screen.getByRole("button", { name: "Edit" })).toHaveFocus());
  });

  it("activating an inactive assembly calls activateOfferingAssembly and invalidates the list", async () => {
    const user = userEvent.setup();
    mockGetOfferingAssembly.mockResolvedValue({ ...baseAssembly, activeState: "Inactive" });
    mockActivateOfferingAssembly.mockResolvedValue({ concurrencyVersion: "v2" });
    const { invalidateSpy } = renderDetail();

    await waitFor(() => expect(screen.getByText("Furnace Tune-Up")).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: "Activate" }));

    await waitFor(() => expect(mockActivateOfferingAssembly).toHaveBeenCalledWith("assembly-1", "v1"));
    await waitFor(() => expect(invalidatedOfferingAssemblies(invalidateSpy)).toBe(true));
  });

  it("inactivating requires an inline confirmation, then calls inactivateOfferingAssembly and invalidates the list", async () => {
    const user = userEvent.setup();
    mockGetOfferingAssembly.mockResolvedValue(baseAssembly);
    mockInactivateOfferingAssembly.mockResolvedValue({ concurrencyVersion: "v2" });
    const { invalidateSpy } = renderDetail();

    await waitFor(() => expect(screen.getByText("Furnace Tune-Up")).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: "Inactivate" }));
    expect(mockInactivateOfferingAssembly).not.toHaveBeenCalled();

    await user.click(screen.getByRole("button", { name: "Confirm inactivate" }));
    await waitFor(() => expect(mockInactivateOfferingAssembly).toHaveBeenCalledWith("assembly-1", "v1"));
    await waitFor(() => expect(invalidatedOfferingAssemblies(invalidateSpy)).toBe(true));
  });

  it("removing an item calls removeOfferingAssemblyItem and invalidates the list, not just the detail", async () => {
    const user = userEvent.setup();
    mockGetOfferingAssembly.mockResolvedValue(baseAssembly);
    mockRemoveOfferingAssemblyItem.mockResolvedValue({ concurrencyVersion: "v2" });
    const { invalidateSpy } = renderDetail();

    await waitFor(() => expect(screen.getByText("Filter")).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: "Remove" }));

    await waitFor(() => expect(mockRemoveOfferingAssemblyItem).toHaveBeenCalledWith("assembly-1", "line-1", "v1"));
    await waitFor(() => expect(invalidatedOfferingAssemblies(invalidateSpy)).toBe(true));
  });

  it("explains Required vs Optional item behavior and that the base price excludes optional components", async () => {
    mockGetOfferingAssembly.mockResolvedValue(baseAssembly);
    renderDetail();

    await waitFor(() => expect(screen.getByText("Filter")).toBeInTheDocument());
    expect(
      screen.getByText(
        "Required items are included in the base assembly and added to Actual Work by default. Optional items are excluded from the base price and added only when needed.",
      ),
    ).toBeInTheDocument();
    expect(screen.getByText("Base price excludes optional components.")).toBeInTheDocument();
  });

  it("gives the Optional checkbox an accessible name that explains it is added only when needed", async () => {
    mockGetOfferingAssembly.mockResolvedValue(baseAssembly);
    renderDetail();

    await waitFor(() => expect(screen.getByText("Filter")).toBeInTheDocument());
    expect(screen.getByRole("checkbox", { name: "Optional — add only when needed" })).toBeInTheDocument();
  });

  it("shows the full associated-item name without truncation or a tooltip fallback, even when it is long", async () => {
    const longName =
      "Premium two-stage variable-speed condensing gas furnace with ECM blower motor and 10-year parts warranty";
    mockGetOfferingAssembly.mockResolvedValue({
      ...baseAssembly,
      items: [
        { id: "line-1", catalogItemId: "item-b", catalogItemDisplayName: longName, defaultQuantity: 1, isOptional: false, displayOrder: 0 },
      ],
    });
    renderDetail();

    const nameEl = await screen.findByText(longName);
    expect(nameEl).not.toHaveClass("truncate");
    expect(nameEl).toHaveClass("break-words");
    expect(nameEl).not.toHaveAttribute("title");
  });

  it("renders inside the shared Price Book workspace width, not a bespoke narrow strip", async () => {
    mockGetOfferingAssembly.mockResolvedValue(baseAssembly);
    const { container } = renderDetail();

    await waitFor(() => expect(screen.getByText("Furnace Tune-Up")).toBeInTheDocument());
    expect(container.querySelector(".max-w-\\[1440px\\]")).not.toBeNull();
    expect(container.querySelector(".max-w-2xl")).toBeNull();
  });
});
