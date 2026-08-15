import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ProposedScopeCaptureModal } from "../ProposedScopeCaptureModal";
import { ApiError, type ProposedScopeDetailResult } from "../../../lib/apiClient";

const mockGetFieldOfferingAssemblies = vi.fn();
const mockGetFieldOfferingAssembly = vi.fn();
const mockGetFieldCatalogItems = vi.fn();
const mockGetFieldCatalogCategories = vi.fn();
const mockFieldSelectProposedScopeLine = vi.fn();
const mockExpandProposedScopeAssembly = vi.fn();

vi.mock("../../../lib/apiClient", async () => {
  const actual = await vi.importActual<typeof import("../../../lib/apiClient")>("../../../lib/apiClient");
  return {
    ...actual,
    api: {
      ...actual.api,
      getFieldOfferingAssemblies: (...args: unknown[]) => mockGetFieldOfferingAssemblies(...args),
      getFieldOfferingAssembly: (...args: unknown[]) => mockGetFieldOfferingAssembly(...args),
      getFieldCatalogItems: (...args: unknown[]) => mockGetFieldCatalogItems(...args),
      getFieldCatalogCategories: (...args: unknown[]) => mockGetFieldCatalogCategories(...args),
      fieldSelectProposedScopeLine: (...args: unknown[]) => mockFieldSelectProposedScopeLine(...args),
      expandProposedScopeAssembly: (...args: unknown[]) => mockExpandProposedScopeAssembly(...args),
    },
  };
});

const scope: ProposedScopeDetailResult = {
  id: "scope-1",
  requestId: "request-1",
  status: "Draft",
  concurrencyVersion: "v1",
  lines: [],
};

function renderModal(overrides: Partial<React.ComponentProps<typeof ProposedScopeCaptureModal>> = {}) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const onClose = vi.fn();
  const onRefetch = vi.fn().mockResolvedValue(undefined);
  const utils = render(
    <QueryClientProvider client={queryClient}>
      <ProposedScopeCaptureModal scope={scope} onClose={onClose} onRefetch={onRefetch} {...overrides} />
    </QueryClientProvider>,
  );
  return { ...utils, onClose, onRefetch };
}

beforeEach(() => {
  vi.clearAllMocks();
  mockGetFieldOfferingAssemblies.mockResolvedValue({ items: [], limit: 50, hasMore: false, nextCursor: null });
  mockGetFieldCatalogItems.mockResolvedValue({ items: [], limit: 50, hasMore: false, nextCursor: null });
  mockGetFieldCatalogCategories.mockResolvedValue({ categories: [] });
});

describe("ProposedScopeCaptureModal", () => {
  it("starts on the Primary Offering rung and advances through the ladder via 'Not here'", async () => {
    const user = userEvent.setup();
    renderModal();

    expect(screen.getByText("Step 1 of 5: Primary offering")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Not here →" }));
    expect(screen.getByText("Step 2 of 5: Common items")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Not here →" }));
    expect(screen.getByText("Step 3 of 5: Categories")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Not here →" }));
    expect(screen.getByText("Step 4 of 5: Search")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Not here →" }));
    expect(screen.getByText("Step 5 of 5: Off-catalog")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Not here →" })).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Back" }));
    expect(screen.getByText("Step 4 of 5: Search")).toBeInTheDocument();
  });

  it("commits a Common Items pick via field-select and re-fetches the authoritative scope", async () => {
    mockGetFieldCatalogItems.mockResolvedValue({
      items: [
        {
          item: { id: "item-1", type: "Material", displayName: "Filter", externalKey: null, categoryId: null, unitOfMeasure: "each" },
          matchRank: "DisplayName",
          matchReason: null,
        },
      ],
      limit: 50,
      hasMore: false,
      nextCursor: null,
    });
    mockFieldSelectProposedScopeLine.mockResolvedValueOnce({ lineId: "line-1", concurrencyVersion: "v2" });

    const user = userEvent.setup();
    const { onRefetch } = renderModal();

    await user.click(screen.getByRole("button", { name: "Not here →" }));
    await user.click(await screen.findByRole("button", { name: "Filter" }));
    await user.click(screen.getByRole("button", { name: "Add to scope" }));

    await waitFor(() =>
      expect(mockFieldSelectProposedScopeLine).toHaveBeenCalledWith(
        "scope-1",
        { lineType: "KnownCatalogItem", catalogItemId: "item-1", quantity: 1 },
        "v1",
      ),
    );
    await waitFor(() => expect(onRefetch).toHaveBeenCalled());
  });

  it("commits an Off-Catalog line and requires a description before submitting", async () => {
    mockFieldSelectProposedScopeLine.mockResolvedValueOnce({ lineId: "line-2", concurrencyVersion: "v2" });

    const user = userEvent.setup();
    const { onRefetch } = renderModal();

    for (let i = 0; i < 4; i++) {
      await user.click(screen.getByRole("button", { name: "Not here →" }));
    }
    expect(screen.getByText("Step 5 of 5: Off-catalog")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Add to scope" }));
    expect(screen.getByText("A description is required.")).toBeInTheDocument();
    expect(mockFieldSelectProposedScopeLine).not.toHaveBeenCalled();

    await user.type(screen.getByLabelText("Description"), "Custom bracket");
    await user.click(screen.getByRole("button", { name: "Add to scope" }));

    await waitFor(() =>
      expect(mockFieldSelectProposedScopeLine).toHaveBeenCalledWith(
        "scope-1",
        { lineType: "OffCatalogItem", offCatalogDescription: "Custom bracket", quantity: 1 },
        "v1",
      ),
    );
    await waitFor(() => expect(onRefetch).toHaveBeenCalled());
  });

  it("on a 409 conflict, shows a non-blocking notice and re-fetches without auto-retrying", async () => {
    mockGetFieldCatalogItems.mockResolvedValue({
      items: [
        {
          item: { id: "item-1", type: "Material", displayName: "Filter", externalKey: null, categoryId: null, unitOfMeasure: "each" },
          matchRank: "DisplayName",
          matchReason: null,
        },
      ],
      limit: 50,
      hasMore: false,
      nextCursor: null,
    });
    mockFieldSelectProposedScopeLine.mockRejectedValueOnce(
      new ApiError(409, "ProposedScope.VersionMismatch", "conflict"),
    );

    const user = userEvent.setup();
    const { onRefetch } = renderModal();

    await user.click(screen.getByRole("button", { name: "Not here →" }));
    await user.click(await screen.findByRole("button", { name: "Filter" }));
    await user.click(screen.getByRole("button", { name: "Add to scope" }));

    await waitFor(() =>
      expect(
        screen.getByText("This proposed scope changed elsewhere — refreshed with the latest scope. Try again."),
      ).toBeInTheDocument(),
    );
    expect(onRefetch).toHaveBeenCalledTimes(1);
    expect(mockFieldSelectProposedScopeLine).toHaveBeenCalledTimes(1);
  });

  it("expands a primary offering, excluding an unchecked optional item", async () => {
    mockGetFieldOfferingAssemblies.mockResolvedValue({
      items: [{ id: "assembly-1", name: "Furnace Tune-Up", primaryCatalogItemId: "primary-1", primaryCatalogItemDisplayName: "Furnace Inspection" }],
      limit: 50,
      hasMore: false,
      nextCursor: null,
    });
    mockGetFieldOfferingAssembly.mockResolvedValueOnce({
      id: "assembly-1",
      name: "Furnace Tune-Up",
      primaryCatalogItemId: "primary-1",
      primaryCatalogItemDisplayName: "Furnace Inspection",
      items: [
        { id: "req-item-1", catalogItemId: "cat-1", catalogItemDisplayName: "Filter", defaultQuantity: 1, isOptional: false, displayOrder: 0 },
        { id: "opt-item-1", catalogItemId: "cat-2", catalogItemDisplayName: "Belt", defaultQuantity: 1, isOptional: true, displayOrder: 1 },
      ],
    });
    mockExpandProposedScopeAssembly.mockResolvedValueOnce({ lineIds: ["line-1", "line-2"], concurrencyVersion: "v2" });

    const user = userEvent.setup();
    const { onRefetch } = renderModal();

    await user.click(await screen.findByRole("button", { name: "Furnace Tune-Up" }));
    await screen.findByLabelText(/Belt/);
    await user.click(screen.getByLabelText(/Belt/));
    await user.click(screen.getByRole("button", { name: "Add to scope" }));

    await waitFor(() =>
      expect(mockExpandProposedScopeAssembly).toHaveBeenCalledWith(
        "scope-1",
        { offeringAssemblyId: "assembly-1", excludedOptionalItemIds: ["opt-item-1"] },
        "v1",
      ),
    );
    await waitFor(() => expect(onRefetch).toHaveBeenCalled());
  });
});
