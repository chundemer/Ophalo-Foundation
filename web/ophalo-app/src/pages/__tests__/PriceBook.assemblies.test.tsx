import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { PriceBook } from "../PriceBook";
import type { OfferingAssemblyListResult } from "../../lib/apiClient";

const mockGetCatalogItems = vi.fn();
const mockGetCatalogCategories = vi.fn();
const mockGetOfferingAssemblies = vi.fn();

vi.mock("../../lib/apiClient", async () => {
  const actual = await vi.importActual<typeof import("../../lib/apiClient")>("../../lib/apiClient");
  return {
    ...actual,
    api: {
      ...actual.api,
      getCatalogItems: (...args: unknown[]) => mockGetCatalogItems(...args),
      getCatalogCategories: (...args: unknown[]) => mockGetCatalogCategories(...args),
      getOfferingAssemblies: (...args: unknown[]) => mockGetOfferingAssemblies(...args),
    },
  };
});

function renderPriceBook(props: Partial<React.ComponentProps<typeof PriceBook>> = {}) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const onSelectAssembly = vi.fn();
  const utils = render(
    <QueryClientProvider client={queryClient}>
      <PriceBook
        role="owner"
        entitled={true}
        entitlementLoading={false}
        entitlementError={false}
        onRetryEntitlement={vi.fn()}
        onSelectItem={vi.fn()}
        onSelectAssembly={onSelectAssembly}
        {...props}
      />
    </QueryClientProvider>,
  );
  return { ...utils, onSelectAssembly };
}

const activeAssemblies: OfferingAssemblyListResult = {
  items: [
    {
      id: "assembly-1",
      name: "Furnace Tune-Up",
      primaryCatalogItemId: "item-primary",
      primaryCatalogItemDisplayName: "Furnace Inspection",
      priceTreatment: "Summed",
      activeState: "Active",
      concurrencyVersion: "v1",
      isOperationallyEligible: true,
    },
  ],
  limit: 50,
  hasMore: false,
  nextCursor: null,
};

const inactiveAssemblies: OfferingAssemblyListResult = {
  items: [
    {
      id: "assembly-2",
      name: "Retired Bundle",
      primaryCatalogItemId: "item-old",
      primaryCatalogItemDisplayName: "Old Primary",
      priceTreatment: "AllInclusive",
      activeState: "Inactive",
      concurrencyVersion: "v1",
      isOperationallyEligible: false,
    },
  ],
  limit: 50,
  hasMore: false,
  nextCursor: null,
};

describe("PriceBook — Offerings & Assemblies tab", () => {
  beforeEach(() => {
    mockGetCatalogItems.mockReset().mockResolvedValue({ items: [], limit: 50, hasMore: false, nextCursor: null });
    mockGetCatalogCategories.mockReset().mockResolvedValue({ categories: [] });
    mockGetOfferingAssemblies.mockReset();
  });

  it("does not query offering-assemblies while on the Catalog Items tab", async () => {
    mockGetCatalogItems.mockResolvedValue({ items: [], limit: 50, hasMore: false, nextCursor: null });
    renderPriceBook();

    await waitFor(() => expect(mockGetCatalogItems).toHaveBeenCalled());
    expect(mockGetOfferingAssemblies).not.toHaveBeenCalled();
  });

  it("switching to the Offerings & Assemblies tab lists active assemblies and clicking a row navigates to it", async () => {
    const user = userEvent.setup();
    mockGetOfferingAssemblies.mockResolvedValue(activeAssemblies);
    const { onSelectAssembly } = renderPriceBook();

    await user.click(screen.getByRole("tab", { name: "Offerings & Assemblies" }));

    await waitFor(() => expect(mockGetOfferingAssemblies).toHaveBeenCalledWith({ status: "Active" }));
    await waitFor(() => expect(screen.getAllByText("Furnace Tune-Up").length).toBeGreaterThan(0));

    await user.click(screen.getAllByText("Furnace Tune-Up")[0]);
    expect(onSelectAssembly).toHaveBeenCalledWith("assembly-1");
  });

  it("the Add assembly CTA renders as one semantic control (mobile-width + sm+ sticky-bar copy), and the status filter stays singular", async () => {
    const user = userEvent.setup();
    mockGetOfferingAssemblies.mockResolvedValue(activeAssemblies);
    const { container } = renderPriceBook();

    await user.click(screen.getByRole("tab", { name: "Offerings & Assemblies" }));
    await waitFor(() => expect(screen.getAllByText("Furnace Tune-Up").length).toBeGreaterThan(0));

    // Exactly two: the mobile-width copy (title row) and the sm+ sticky-workspace-bar copy —
    // one semantic control shown once per breakpoint, matching the Catalog Items CTA pattern.
    expect(screen.getAllByRole("button", { name: "Add assembly" })).toHaveLength(2);
    expect(screen.getAllByRole("group", { name: "Filter by status" })).toHaveLength(1);

    const stickyBar = container.querySelector(".sm\\:sticky");
    expect(stickyBar).not.toBeNull();
    expect(stickyBar?.querySelector('[role="tablist"]')).not.toBeNull();
  });

  it("an assembly with no eligibility carries a Needs review badge in the list", async () => {
    const user = userEvent.setup();
    mockGetOfferingAssemblies.mockResolvedValue({
      ...activeAssemblies,
      items: [{ ...activeAssemblies.items[0], isOperationallyEligible: false }],
    });
    renderPriceBook();

    await user.click(screen.getByRole("tab", { name: "Offerings & Assemblies" }));
    await waitFor(() => expect(screen.getAllByText("Needs review").length).toBeGreaterThan(0));
  });

  it("an inactivated assembly disappears from the default Active view but is reachable via the Inactive status filter", async () => {
    const user = userEvent.setup();
    mockGetOfferingAssemblies.mockImplementation(({ status }: { status: string }) =>
      Promise.resolve(status === "Inactive" ? inactiveAssemblies : activeAssemblies),
    );
    renderPriceBook();

    await user.click(screen.getByRole("tab", { name: "Offerings & Assemblies" }));
    await waitFor(() => expect(screen.getAllByText("Furnace Tune-Up").length).toBeGreaterThan(0));
    expect(screen.queryByText("Retired Bundle")).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Inactive" }));

    await waitFor(() => expect(mockGetOfferingAssemblies).toHaveBeenCalledWith({ status: "Inactive" }));
    await waitFor(() => expect(screen.getAllByText("Retired Bundle").length).toBeGreaterThan(0));
    expect(screen.queryByText("Furnace Tune-Up")).not.toBeInTheDocument();
  });

  it("shows an empty state scoped to the active status filter, with the create CTA only on Active", async () => {
    const user = userEvent.setup();
    mockGetOfferingAssemblies.mockResolvedValue({ items: [], limit: 50, hasMore: false, nextCursor: null });
    renderPriceBook();

    await user.click(screen.getByRole("tab", { name: "Offerings & Assemblies" }));
    await waitFor(() => expect(screen.getByText("No active offerings/assemblies")).toBeInTheDocument());
    expect(screen.getByRole("button", { name: /add your first assembly/i })).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Inactive" }));
    await waitFor(() => expect(screen.getByText("No inactive offerings/assemblies")).toBeInTheDocument());
    expect(screen.queryByRole("button", { name: /add your first assembly/i })).not.toBeInTheDocument();
  });

  it("paginates with Prev/Next using the returned cursor, tracking each status filter's page independently", async () => {
    const user = userEvent.setup();
    const activePage1: OfferingAssemblyListResult = {
      items: [{ ...activeAssemblies.items[0], id: "assembly-1", name: "Furnace Tune-Up" }],
      limit: 1,
      hasMore: true,
      nextCursor: "cursor-page-2",
    };
    const activePage2: OfferingAssemblyListResult = {
      items: [{ ...activeAssemblies.items[0], id: "assembly-3", name: "AC Tune-Up" }],
      limit: 1,
      hasMore: false,
      nextCursor: null,
    };
    mockGetOfferingAssemblies.mockImplementation(({ status, cursor }: { status: string; cursor?: string }) => {
      if (status === "Inactive") return Promise.resolve(inactiveAssemblies);
      return Promise.resolve(cursor === "cursor-page-2" ? activePage2 : activePage1);
    });
    renderPriceBook();

    await user.click(screen.getByRole("tab", { name: "Offerings & Assemblies" }));
    await waitFor(() => expect(screen.getAllByText("Furnace Tune-Up").length).toBeGreaterThan(0));
    expect(screen.getByRole("button", { name: "Previous" })).toBeDisabled();

    await user.click(screen.getByRole("button", { name: "Next" }));
    await waitFor(() => expect(mockGetOfferingAssemblies).toHaveBeenCalledWith({ status: "Active", cursor: "cursor-page-2" }));
    await waitFor(() => expect(screen.getAllByText("AC Tune-Up").length).toBeGreaterThan(0));
    expect(screen.queryByText("Furnace Tune-Up")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Next" })).toBeDisabled();

    // Switching to Inactive and back to Active must not lose the Active page-2 position.
    await user.click(screen.getByRole("button", { name: "Inactive" }));
    await waitFor(() => expect(screen.getAllByText("Retired Bundle").length).toBeGreaterThan(0));

    await user.click(screen.getByRole("button", { name: "Active" }));
    await waitFor(() => expect(screen.getAllByText("AC Tune-Up").length).toBeGreaterThan(0));

    await user.click(screen.getByRole("button", { name: "Previous" }));
    await waitFor(() => expect(screen.getAllByText("Furnace Tune-Up").length).toBeGreaterThan(0));
  });

  it("the desktop assembly name is a keyboard-focusable button, not a bare clickable row", async () => {
    const user = userEvent.setup();
    mockGetOfferingAssemblies.mockResolvedValue(activeAssemblies);
    const { onSelectAssembly } = renderPriceBook();

    await user.click(screen.getByRole("tab", { name: "Offerings & Assemblies" }));
    await waitFor(() => expect(screen.getAllByText("Furnace Tune-Up").length).toBeGreaterThan(0));

    const nameButton = screen.getByRole("button", { name: "Furnace Tune-Up" });
    nameButton.focus();
    expect(nameButton).toHaveFocus();

    await user.keyboard("{Enter}");
    expect(onSelectAssembly).toHaveBeenCalledWith("assembly-1");
  });
});
