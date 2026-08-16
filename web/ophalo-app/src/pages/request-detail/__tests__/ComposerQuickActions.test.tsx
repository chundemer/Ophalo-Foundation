import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ComposerQuickActions } from "../ComposerQuickActions";
import { ApiError } from "../../../lib/apiClient";

const mockGetFieldQuickScopeActions = vi.fn();
const mockFieldSelectProposedScopeLine = vi.fn();
const mockExpandProposedScopeAssembly = vi.fn();

vi.mock("../../../lib/apiClient", async () => {
  const actual = await vi.importActual<typeof import("../../../lib/apiClient")>("../../../lib/apiClient");
  return {
    ...actual,
    api: {
      ...actual.api,
      getFieldQuickScopeActions: (...args: unknown[]) => mockGetFieldQuickScopeActions(...args),
      fieldSelectProposedScopeLine: (...args: unknown[]) => mockFieldSelectProposedScopeLine(...args),
      expandProposedScopeAssembly: (...args: unknown[]) => mockExpandProposedScopeAssembly(...args),
    },
  };
});

function renderQuickActions() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const onCommitted = vi.fn();
  const onConflict = vi.fn();
  const utils = render(
    <QueryClientProvider client={queryClient}>
      <ComposerQuickActions proposedScopeId="scope-1" version="v1" onCommitted={onCommitted} onConflict={onConflict} />
    </QueryClientProvider>,
  );
  return { ...utils, onCommitted, onConflict };
}

beforeEach(() => {
  vi.clearAllMocks();
});

describe("ComposerQuickActions", () => {
  it("renders nothing for an empty Quick action set", async () => {
    mockGetFieldQuickScopeActions.mockResolvedValue({ actions: [] });
    const { container } = renderQuickActions();

    await waitFor(() => expect(mockGetFieldQuickScopeActions).toHaveBeenCalled());
    expect(container.querySelector("button")).toBeNull();
  });

  it("dispatches a catalog-target action through field-select with quantity 1", async () => {
    mockGetFieldQuickScopeActions.mockResolvedValue({
      actions: [{ id: "qa-1", order: 0, catalogItemId: "item-1", offeringAssemblyId: null, targetDisplayName: "Filter" }],
    });
    mockFieldSelectProposedScopeLine.mockResolvedValueOnce({ lineId: "line-1", concurrencyVersion: "v2" });

    const user = userEvent.setup();
    const { onCommitted } = renderQuickActions();
    await user.click(await screen.findByRole("button", { name: "Filter" }));

    await waitFor(() =>
      expect(mockFieldSelectProposedScopeLine).toHaveBeenCalledWith(
        "scope-1",
        { lineType: "KnownCatalogItem", catalogItemId: "item-1", quantity: 1 },
        "v1",
      ),
    );
    await waitFor(() => expect(onCommitted).toHaveBeenCalled());
  });

  it("dispatches an assembly-target action through default-only expand-assembly", async () => {
    mockGetFieldQuickScopeActions.mockResolvedValue({
      actions: [{ id: "qa-2", order: 0, catalogItemId: null, offeringAssemblyId: "assembly-1", targetDisplayName: "Tune-up" }],
    });
    mockExpandProposedScopeAssembly.mockResolvedValueOnce({ lineIds: ["line-1", "line-2"], concurrencyVersion: "v2" });

    const user = userEvent.setup();
    const { onCommitted } = renderQuickActions();
    await user.click(await screen.findByRole("button", { name: "Tune-up" }));

    await waitFor(() =>
      expect(mockExpandProposedScopeAssembly).toHaveBeenCalledWith(
        "scope-1",
        { offeringAssemblyId: "assembly-1", excludedOptionalItemIds: [] },
        "v1",
      ),
    );
    await waitFor(() => expect(onCommitted).toHaveBeenCalled());
  });

  it("maps a catalog-target-unavailable response (actual 404) to the office-updated notice and reconciles", async () => {
    mockGetFieldQuickScopeActions.mockResolvedValue({
      actions: [{ id: "qa-1", order: 0, catalogItemId: "item-1", offeringAssemblyId: null, targetDisplayName: "Filter" }],
    });
    mockFieldSelectProposedScopeLine.mockRejectedValueOnce(
      new ApiError(404, "ProposedScope.LineCatalogItemNotFound", "not found"),
    );

    const user = userEvent.setup();
    const { onConflict } = renderQuickActions();
    await user.click(await screen.findByRole("button", { name: "Filter" }));

    await waitFor(() =>
      expect(onConflict).toHaveBeenCalledWith(
        "The office recently updated this item and it's no longer available — refreshed with the latest scope.",
      ),
    );
  });

  it("maps an assembly-target-unavailable response (actual 409) to the office-updated notice, not the generic conflict notice", async () => {
    mockGetFieldQuickScopeActions.mockResolvedValue({
      actions: [{ id: "qa-2", order: 0, catalogItemId: null, offeringAssemblyId: "assembly-1", targetDisplayName: "Tune-up" }],
    });
    mockExpandProposedScopeAssembly.mockRejectedValueOnce(
      new ApiError(409, "ProposedScope.ExpandAssemblyNotOperationallyEligible", "not eligible"),
    );

    const user = userEvent.setup();
    const { onConflict } = renderQuickActions();
    await user.click(await screen.findByRole("button", { name: "Tune-up" }));

    await waitFor(() =>
      expect(onConflict).toHaveBeenCalledWith(
        "The office recently updated this item and it's no longer available — refreshed with the latest scope.",
      ),
    );
  });

  it("reconciles with the generic notice on a 409 conflict", async () => {
    mockGetFieldQuickScopeActions.mockResolvedValue({
      actions: [{ id: "qa-1", order: 0, catalogItemId: "item-1", offeringAssemblyId: null, targetDisplayName: "Filter" }],
    });
    mockFieldSelectProposedScopeLine.mockRejectedValueOnce(new ApiError(409, "ProposedScope.VersionMismatch", "stale"));

    const user = userEvent.setup();
    const { onConflict } = renderQuickActions();
    await user.click(await screen.findByRole("button", { name: "Filter" }));

    await waitFor(() => expect(onConflict).toHaveBeenCalledWith());
  });
});
