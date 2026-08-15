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
const mockUpdateProposedScopeLine = vi.fn();
const mockRemoveProposedScopeLine = vi.fn();
const mockSubmitProposedScope = vi.fn();

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
      updateProposedScopeLine: (...args: unknown[]) => mockUpdateProposedScopeLine(...args),
      removeProposedScopeLine: (...args: unknown[]) => mockRemoveProposedScopeLine(...args),
      submitProposedScope: (...args: unknown[]) => mockSubmitProposedScope(...args),
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
      <ProposedScopeCaptureModal scope={scope} readOnly={false} onClose={onClose} onRefetch={onRefetch} {...overrides} />
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

  it("edits a line's quantity via update-line and re-fetches", async () => {
    const scopeWithLine: ProposedScopeDetailResult = {
      ...scope,
      lines: [
        {
          id: "line-1",
          lineType: "KnownCatalogItem",
          catalogItemId: "item-1",
          offeringAssemblyId: null,
          quantity: 2,
          isException: false,
          offCatalogDescription: null,
          offCatalogQuantity: null,
          note: null,
          displayOrder: 10,
          displayNameSnapshot: "Filter",
          unitOfMeasureSnapshot: "each",
          offeringAssemblyNameSnapshot: null,
          defaultQuantitySnapshot: null,
        },
      ],
    };
    mockUpdateProposedScopeLine.mockResolvedValueOnce({ concurrencyVersion: "v2" });

    const user = userEvent.setup();
    const { onRefetch } = renderModal({ scope: scopeWithLine });

    await user.click(screen.getByRole("button", { name: "Edit" }));
    const quantityInput = screen.getByLabelText(/Quantity/);
    await user.clear(quantityInput);
    await user.type(quantityInput, "5");
    await user.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() =>
      expect(mockUpdateProposedScopeLine).toHaveBeenCalledWith(
        "scope-1",
        "line-1",
        { quantity: 5, isException: false, note: null, displayOrder: 10 },
        "v1",
      ),
    );
    await waitFor(() => expect(onRefetch).toHaveBeenCalled());
  });

  it("removes a line via remove-line and re-fetches", async () => {
    const scopeWithLine: ProposedScopeDetailResult = {
      ...scope,
      lines: [
        {
          id: "line-1",
          lineType: "KnownCatalogItem",
          catalogItemId: "item-1",
          offeringAssemblyId: null,
          quantity: 2,
          isException: false,
          offCatalogDescription: null,
          offCatalogQuantity: null,
          note: null,
          displayOrder: 10,
          displayNameSnapshot: "Filter",
          unitOfMeasureSnapshot: "each",
          offeringAssemblyNameSnapshot: null,
          defaultQuantitySnapshot: null,
        },
      ],
    };
    mockRemoveProposedScopeLine.mockResolvedValueOnce({ concurrencyVersion: "v2" });

    const user = userEvent.setup();
    const { onRefetch } = renderModal({ scope: scopeWithLine });

    await user.click(screen.getByRole("button", { name: "Remove" }));

    await waitFor(() => expect(mockRemoveProposedScopeLine).toHaveBeenCalledWith("scope-1", "line-1", "v1"));
    await waitFor(() => expect(onRefetch).toHaveBeenCalled());
  });

  it("disables Submit when the scope has no lines", () => {
    renderModal({ scope: { ...scope, lines: [] } });

    expect(screen.getByRole("button", { name: "Submit to office" })).toBeDisabled();
    expect(screen.getByText("Add at least one line before submitting.")).toBeInTheDocument();
  });

  it("submits successfully when the scope has at least one line", async () => {
    const scopeWithLine: ProposedScopeDetailResult = {
      ...scope,
      lines: [
        {
          id: "line-1",
          lineType: "OffCatalogItem",
          catalogItemId: null,
          offeringAssemblyId: null,
          quantity: 1,
          isException: false,
          offCatalogDescription: "Custom part",
          offCatalogQuantity: 1,
          note: null,
          displayOrder: 10,
          displayNameSnapshot: "Custom part",
          unitOfMeasureSnapshot: null,
          offeringAssemblyNameSnapshot: null,
          defaultQuantitySnapshot: null,
        },
      ],
    };
    mockSubmitProposedScope.mockResolvedValueOnce({ concurrencyVersion: "v2" });

    const user = userEvent.setup();
    const { onRefetch } = renderModal({ scope: scopeWithLine });

    const submitButton = screen.getByRole("button", { name: "Submit to office" });
    expect(submitButton).not.toBeDisabled();
    await user.click(submitButton);

    await waitFor(() => expect(mockSubmitProposedScope).toHaveBeenCalledWith("scope-1", "v1"));
    await waitFor(() => expect(screen.getByText("Submitted to office.")).toBeInTheDocument());
    await waitFor(() => expect(onRefetch).toHaveBeenCalled());
  });

  it("renders read-only with no rungs, edit/remove, or submit controls", async () => {
    const scopeWithLine: ProposedScopeDetailResult = {
      ...scope,
      status: "SubmittedToOffice",
      lines: [
        {
          id: "line-1",
          lineType: "OffCatalogItem",
          catalogItemId: null,
          offeringAssemblyId: null,
          quantity: 1,
          isException: false,
          offCatalogDescription: "Custom part",
          offCatalogQuantity: 1,
          note: null,
          displayOrder: 10,
          displayNameSnapshot: "Custom part",
          unitOfMeasureSnapshot: null,
          offeringAssemblyNameSnapshot: null,
          defaultQuantitySnapshot: null,
        },
      ],
    };
    renderModal({ scope: scopeWithLine, readOnly: true });

    expect(screen.getByText("Proposed scope — Submitted to office")).toBeInTheDocument();
    expect(screen.getByText("Custom part")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Edit" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Remove" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Submit to office" })).not.toBeInTheDocument();
    expect(screen.queryByText(/Step 1 of 5/)).not.toBeInTheDocument();
  });
});
