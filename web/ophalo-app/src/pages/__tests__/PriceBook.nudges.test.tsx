import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { PriceBook } from "../PriceBook";
import { ApiError } from "../../lib/apiClient";
import type { ScopeNudgeRuleConfigListResponse, CatalogItemListResult, OfferingAssemblyListResult } from "../../lib/apiClient";

const mockGetCatalogItems = vi.fn();
const mockGetCatalogCategories = vi.fn();
const mockGetOfferingAssemblies = vi.fn();
const mockGetScopeNudgeRules = vi.fn();
const mockCreateScopeNudgeRule = vi.fn();
const mockUpdateScopeNudgeRule = vi.fn();
const mockDeleteScopeNudgeRule = vi.fn();

vi.mock("../../lib/apiClient", async () => {
  const actual = await vi.importActual<typeof import("../../lib/apiClient")>("../../lib/apiClient");
  return {
    ...actual,
    api: {
      ...actual.api,
      getCatalogItems: (...args: unknown[]) => mockGetCatalogItems(...args),
      getCatalogCategories: (...args: unknown[]) => mockGetCatalogCategories(...args),
      getOfferingAssemblies: (...args: unknown[]) => mockGetOfferingAssemblies(...args),
      getScopeNudgeRules: (...args: unknown[]) => mockGetScopeNudgeRules(...args),
      createScopeNudgeRule: (...args: unknown[]) => mockCreateScopeNudgeRule(...args),
      updateScopeNudgeRule: (...args: unknown[]) => mockUpdateScopeNudgeRule(...args),
      deleteScopeNudgeRule: (...args: unknown[]) => mockDeleteScopeNudgeRule(...args),
    },
  };
});

function renderPriceBook(props: Partial<React.ComponentProps<typeof PriceBook>> = {}) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const utils = render(
    <QueryClientProvider client={queryClient}>
      <PriceBook
        role="owner"
        entitled={true}
        entitlementLoading={false}
        entitlementError={false}
        onRetryEntitlement={vi.fn()}
        onSelectItem={vi.fn()}
        onSelectAssembly={vi.fn()}
        activeTab="nudges"
        {...props}
      />
    </QueryClientProvider>,
  );
  return utils;
}

const catalogPage: CatalogItemListResult = {
  items: [
    {
      item: { id: "item-primary", type: "Service", displayName: "Furnace Inspection", externalKey: null, categoryId: null, unitOfMeasure: "each", currency: "USD", isCommonItem: false, activeState: "Active", concurrencyVersion: "v1" },
      currentPricingMode: "StandalonePrice",
      currentSellPrice: 100,
      currentCost: 50,
      matchRank: "DisplayName",
      matchReason: null,
    },
    {
      item: { id: "item-filter", type: "Material", displayName: "Filter", externalKey: null, categoryId: null, unitOfMeasure: "each", currency: "USD", isCommonItem: false, activeState: "Active", concurrencyVersion: "v1" },
      currentPricingMode: "StandalonePrice",
      currentSellPrice: 20,
      currentCost: 10,
      matchRank: "DisplayName",
      matchReason: null,
    },
  ],
  limit: 20,
  hasMore: false,
  nextCursor: null,
};

const assembliesPage: OfferingAssemblyListResult = {
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
  limit: 20,
  hasMore: false,
  nextCursor: null,
};

const rulesResponse: ScopeNudgeRuleConfigListResponse = {
  rules: [
    {
      id: "rule-1",
      triggerCatalogItemId: "item-primary",
      triggerOfferingAssemblyId: null,
      triggerDisplayName: "Furnace Inspection",
      triggerIsEligible: true,
      suggestions: [
        {
          id: "sugg-1",
          order: 0,
          suggestedCatalogItemId: "item-filter",
          suggestedOfferingAssemblyId: null,
          targetDisplayName: "Filter",
          isEligible: true,
        },
        {
          id: "sugg-2",
          order: 1,
          suggestedCatalogItemId: null,
          suggestedOfferingAssemblyId: "assembly-old",
          targetDisplayName: "Retired Bundle",
          isEligible: false,
        },
      ],
    },
  ],
};

async function selectFromPicker(user: ReturnType<typeof userEvent.setup>, combobox: HTMLElement, optionText: string) {
  await user.click(combobox);
  const listbox = await screen.findByRole("listbox");
  await user.click(await within(listbox).findByText(optionText));
}

describe("PriceBook — Nudges tab", () => {
  beforeEach(() => {
    mockGetCatalogItems.mockReset().mockResolvedValue(catalogPage);
    mockGetCatalogCategories.mockReset().mockResolvedValue({ categories: [] });
    mockGetOfferingAssemblies.mockReset().mockResolvedValue(assembliesPage);
    mockGetScopeNudgeRules.mockReset().mockResolvedValue(rulesResponse);
    mockCreateScopeNudgeRule.mockReset();
    mockUpdateScopeNudgeRule.mockReset();
    mockDeleteScopeNudgeRule.mockReset();
  });

  it("lists rules with trigger, ordered suggestions, and repair-needed indicators for ineligible targets", async () => {
    renderPriceBook();

    await waitFor(() => expect(mockGetScopeNudgeRules).toHaveBeenCalled());
    await screen.findByText("Furnace Inspection");
    expect(screen.getByText("Filter")).toBeInTheDocument();
    expect(screen.getByText("Retired Bundle")).toBeInTheDocument();
    // Only the ineligible suggestion (not the eligible trigger/suggestion) shows a repair badge.
    expect(screen.getAllByText("Needs repair")).toHaveLength(1);
  });

  it("creating a rule picks one trigger and one suggestion, then calls createScopeNudgeRule", async () => {
    const user = userEvent.setup();
    mockGetScopeNudgeRules.mockResolvedValue({ rules: [] });
    mockCreateScopeNudgeRule.mockResolvedValue({ ...rulesResponse.rules[0] });
    renderPriceBook();

    await user.click(await screen.findByRole("button", { name: "Add your first nudge rule" }));
    await screen.findByRole("heading", { name: "Add nudge rule" });

    await selectFromPicker(user, screen.getAllByRole("combobox")[0], "Furnace Inspection");
    await selectFromPicker(user, screen.getAllByRole("combobox")[1], "Filter");

    await user.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() =>
      expect(mockCreateScopeNudgeRule).toHaveBeenCalledWith({
        triggerCatalogItemId: "item-primary",
        triggerOfferingAssemblyId: null,
        suggestions: [{ catalogItemId: "item-filter", offeringAssemblyId: null }],
      }),
    );
  });

  it("editing a rule shows the trigger as read-only and submits only the suggestion list", async () => {
    const user = userEvent.setup();
    mockUpdateScopeNudgeRule.mockResolvedValue({ ...rulesResponse.rules[0] });
    renderPriceBook();

    await user.click(await screen.findByRole("button", { name: "Edit" }));
    await screen.findByRole("heading", { name: "Edit nudge rule" });

    // Trigger is displayed but not an editable combobox.
    expect(screen.getByText("Trigger can't be changed after creation.")).toBeInTheDocument();
    expect(screen.queryAllByRole("combobox").length).toBeGreaterThan(0);

    await user.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(mockUpdateScopeNudgeRule).toHaveBeenCalledTimes(1));
    const [ruleId, body] = mockUpdateScopeNudgeRule.mock.calls[0];
    expect(ruleId).toBe("rule-1");
    expect(body.suggestions).toHaveLength(2);
  });

  it("deleting a rule shows a confirmation naming the trigger before calling deleteScopeNudgeRule", async () => {
    const user = userEvent.setup();
    mockDeleteScopeNudgeRule.mockResolvedValue(undefined);
    renderPriceBook();

    await user.click(await screen.findByRole("button", { name: "Delete" }));

    const dialog = await screen.findByRole("dialog", { name: "Delete nudge rule" });
    expect(within(dialog).getByText(/Delete the nudge rule for/)).toBeInTheDocument();
    expect(within(dialog).getByText("Furnace Inspection")).toBeInTheDocument();

    await user.click(within(dialog).getByRole("button", { name: "Delete" }));

    await waitFor(() => expect(mockDeleteScopeNudgeRule).toHaveBeenCalledWith("rule-1"));
  });

  it("surfaces a duplicate-trigger error from the server without a client-side duplicate check", async () => {
    const user = userEvent.setup();
    mockGetScopeNudgeRules.mockResolvedValue({ rules: [] });
    mockCreateScopeNudgeRule.mockRejectedValue(
      new ApiError(409, "ScopeNudgeRule.DuplicateTrigger", "conflict"),
    );
    renderPriceBook();

    await user.click(await screen.findByRole("button", { name: "Add your first nudge rule" }));
    await selectFromPicker(user, screen.getAllByRole("combobox")[0], "Furnace Inspection");
    await selectFromPicker(user, screen.getAllByRole("combobox")[1], "Filter");
    await user.click(screen.getByRole("button", { name: "Save" }));

    await screen.findByText("A rule for this trigger already exists.");
  });

  it("picking an assembly trigger and assembly suggestion via the browse-only picker sends offeringAssemblyId, and Next pages through cursor results", async () => {
    const user = userEvent.setup();
    const assembliesPage1: OfferingAssemblyListResult = {
      items: [{ ...assembliesPage.items[0] }],
      limit: 20,
      hasMore: true,
      nextCursor: "cursor-2",
    };
    const assembliesPage2: OfferingAssemblyListResult = {
      items: [
        {
          id: "assembly-2",
          name: "Filter Kit",
          primaryCatalogItemId: "item-filter",
          primaryCatalogItemDisplayName: "Filter",
          priceTreatment: "Summed",
          activeState: "Active",
          concurrencyVersion: "v1",
          isOperationallyEligible: true,
        },
      ],
      limit: 20,
      hasMore: false,
      nextCursor: null,
    };
    mockGetOfferingAssemblies.mockImplementation((params: { cursor?: string } = {}) =>
      Promise.resolve(params.cursor === "cursor-2" ? assembliesPage2 : assembliesPage1),
    );
    mockGetScopeNudgeRules.mockResolvedValue({ rules: [] });
    mockCreateScopeNudgeRule.mockResolvedValue({ ...rulesResponse.rules[0] });
    renderPriceBook();

    await user.click(await screen.findByRole("button", { name: "Add your first nudge rule" }));
    await screen.findByRole("heading", { name: "Add nudge rule" });

    // Switch the trigger to Assembly and page through the browse-only picker to the second page.
    const triggerAssemblyRadio = screen.getAllByRole("radio", { name: "Assembly" })[0];
    await user.click(triggerAssemblyRadio);

    const triggerCombobox = screen.getAllByRole("combobox")[0];
    await user.click(triggerCombobox);
    let listbox = await screen.findByRole("listbox");
    expect(within(listbox).getByText("Furnace Tune-Up")).toBeInTheDocument();

    await user.click(within(listbox).getByRole("button", { name: "Next" }));
    await waitFor(() =>
      expect(mockGetOfferingAssemblies).toHaveBeenCalledWith({ status: "Active", cursor: "cursor-2", limit: 20 }),
    );
    listbox = await screen.findByRole("listbox");
    await user.click(await within(listbox).findByText("Filter Kit"));

    // Switch the (default) suggestion row to Assembly and select the first-page assembly.
    const suggestionAssemblyRadio = screen.getAllByRole("radio", { name: "Assembly" })[1];
    await user.click(suggestionAssemblyRadio);
    await selectFromPicker(user, screen.getAllByRole("combobox")[1], "Furnace Tune-Up");

    await user.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() =>
      expect(mockCreateScopeNudgeRule).toHaveBeenCalledWith({
        triggerCatalogItemId: null,
        triggerOfferingAssemblyId: "assembly-2",
        suggestions: [{ catalogItemId: null, offeringAssemblyId: "assembly-1" }],
      }),
    );
  });
});
