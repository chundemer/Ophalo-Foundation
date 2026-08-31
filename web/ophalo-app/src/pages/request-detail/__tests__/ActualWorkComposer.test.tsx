import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useState } from "react";
import { ActualWorkComposer } from "../ActualWorkComposer";
import { ApiError } from "../../../lib/apiClient";
import type { ActualWorkHistoryResult } from "../../../lib/apiClient";
import { LiveAnnouncerRegion } from "../../../components/a11y/LiveAnnouncerRegion";

const mockGetFieldScopeSearch = vi.fn();
const mockAddActualWorkLine = vi.fn();
const mockUpdateActualWorkLine = vi.fn();
const mockRemoveActualWorkLine = vi.fn();
const mockSubmitActualWork = vi.fn();
const mockDiscardActualWork = vi.fn();
const mockExpandActualWorkAssembly = vi.fn();
const mockGetActualWorkNudgeFieldSuggestions = vi.fn();
const mockGetActualWorkPerformerCandidates = vi.fn();

vi.mock("../../../lib/apiClient", async () => {
  const actual = await vi.importActual<typeof import("../../../lib/apiClient")>("../../../lib/apiClient");
  return {
    ...actual,
    api: {
      ...actual.api,
      getFieldScopeSearch: (...args: unknown[]) => mockGetFieldScopeSearch(...args),
      addActualWorkLine: (...args: unknown[]) => mockAddActualWorkLine(...args),
      updateActualWorkLine: (...args: unknown[]) => mockUpdateActualWorkLine(...args),
      removeActualWorkLine: (...args: unknown[]) => mockRemoveActualWorkLine(...args),
      submitActualWork: (...args: unknown[]) => mockSubmitActualWork(...args),
      discardActualWork: (...args: unknown[]) => mockDiscardActualWork(...args),
      expandActualWorkAssembly: (...args: unknown[]) => mockExpandActualWorkAssembly(...args),
      getActualWorkNudgeFieldSuggestions: (...args: unknown[]) => mockGetActualWorkNudgeFieldSuggestions(...args),
      getActualWorkPerformerCandidates: (...args: unknown[]) => mockGetActualWorkPerformerCandidates(...args),
    },
  };
});

type ActualWorkDraft = NonNullable<ActualWorkHistoryResult["openDraft"]>;

const draftLine = {
  id: "line-1",
  displayNameSnapshot: "Filter",
  unitOfMeasureSnapshot: "each",
  actualQuantity: 2,
  note: null,
  performedByAccountUserId: "au-self",
  performerDisplayName: "Sam Field",
};

function emptyDraft(overrides: Partial<ActualWorkDraft> = {}): ActualWorkDraft {
  return {
    id: "draft-1",
    status: "Draft",
    outcome: null,
    completionNote: null,
    submittedAtUtc: null,
    concurrencyVersion: "v1",
    isRecorder: true,
    // 4c-i-c-2: a persisted ticket-default performer is the precondition for the add region. Every
    // existing add/assembly/nudge test assumes it is present; the gate tests override it to null.
    defaultPerformedByAccountUserId: "au-self",
    defaultPerformerDisplayName: "Sam Field",
    lines: [],
    ...overrides,
  };
}

function renderComposer(overrides: Partial<React.ComponentProps<typeof ActualWorkComposer>> = {}) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const onClose = vi.fn();
  const onCommitted = vi.fn();
  const onConflict = vi.fn();
  const onDismissNotice = vi.fn();
  const onRetryReconciliation = vi.fn();
  const onSubmitted = vi.fn();
  const onDiscarded = vi.fn();
  const onSetDefaultPerformer = vi.fn().mockResolvedValue("set");
  const onSetVisitNote = vi.fn().mockResolvedValue("set");
  const onSetZeroLineDisposition = vi.fn().mockResolvedValue("set");
  const onHandOffToOffice = vi.fn().mockResolvedValue("handed-off");
  const utils = render(
    <QueryClientProvider client={queryClient}>
      <ActualWorkComposer
        draft={emptyDraft()}
        conflictNotice={null}
        isWide={true}
        onClose={onClose}
        onCommitted={onCommitted}
        onConflict={onConflict}
        onDismissNotice={onDismissNotice}
        onRetryReconciliation={onRetryReconciliation}
        onSubmitted={onSubmitted}
        onDiscarded={onDiscarded}
        onSetDefaultPerformer={onSetDefaultPerformer}
        onSetVisitNote={onSetVisitNote}
        onSetZeroLineDisposition={onSetZeroLineDisposition}
        onHandOffToOffice={onHandOffToOffice}
        {...overrides}
      />
    </QueryClientProvider>,
  );
  return { ...utils, onClose, onCommitted, onConflict, onDismissNotice, onRetryReconciliation, onSubmitted, onDiscarded, onSetDefaultPerformer, onSetVisitNote, onSetZeroLineDisposition, onHandOffToOffice };
}

beforeEach(() => {
  vi.clearAllMocks();
  mockGetFieldScopeSearch.mockResolvedValue({ items: [], limit: 20, hasMore: false, nextCursor: null });
  mockGetActualWorkNudgeFieldSuggestions.mockResolvedValue({
    ruleId: null,
    triggerCatalogItemId: null,
    triggerOfferingAssemblyId: null,
    suggestions: [],
  });
  mockGetActualWorkPerformerCandidates.mockResolvedValue({
    candidates: [
      { accountUserId: "au-tech", displayName: "Dana Tech", role: "operator" },
      { accountUserId: "au-other", displayName: "Lee Field", role: "operator" },
    ],
  });
});

describe("ActualWorkComposer", () => {
  it("shows the icon-only close control at isWide and calls onClose", async () => {
    const user = userEvent.setup();
    const { onClose } = renderComposer({ isWide: true });

    expect(screen.queryByText("← Back to Request")).not.toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Close" }));
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it("shows a Back to Request control below isWide and calls the same onClose handler", async () => {
    const user = userEvent.setup();
    const { onClose } = renderComposer({ isWide: false });

    expect(screen.queryByRole("button", { name: "Close" })).not.toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "← Back to Request" }));
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it("pads the full-bleed header/footer for the notch/home-indicator below isWide, and leaves the isWide drawer unpadded (Slice 5c)", () => {
    const { rerender } = renderComposer({ isWide: false });

    const narrowHeader = screen.getByRole("button", { name: "← Back to Request" }).closest("div.shrink-0");
    expect(narrowHeader?.className).toContain("safe-area-inset-top");
    const narrowFooter = screen.getByLabelText("Visit outcome").closest("div.shrink-0");
    expect(narrowFooter?.className).toContain("safe-area-inset-bottom");

    rerender(
      <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
        <ActualWorkComposer
          draft={emptyDraft()}
          conflictNotice={null}
          isWide={true}
          onClose={vi.fn()}
          onCommitted={vi.fn()}
          onConflict={vi.fn()}
          onDismissNotice={vi.fn()}
          onRetryReconciliation={vi.fn()}
          onSubmitted={vi.fn()}
          onDiscarded={vi.fn()}
          onSetDefaultPerformer={vi.fn().mockResolvedValue("set")}
          onSetVisitNote={vi.fn().mockResolvedValue("set")}
          onSetZeroLineDisposition={vi.fn().mockResolvedValue("set")}
          onHandOffToOffice={vi.fn().mockResolvedValue("handed-off")}
        />
      </QueryClientProvider>,
    );

    const wideHeader = screen.getByRole("button", { name: "Close" }).closest("div.shrink-0");
    expect(wideHeader?.className).not.toContain("safe-area-inset-top");
    const wideFooter = screen.getByLabelText("Visit outcome").closest("div.shrink-0");
    expect(wideFooter?.className).not.toContain("safe-area-inset-bottom");
  });

  it("submitting a zero-line draft is disabled until an outcome and completion note are both provided", async () => {
    const user = userEvent.setup();
    renderComposer();

    const submitButton = screen.getByRole("button", { name: "Submit visit to office" });
    expect(submitButton).toBeDisabled();

    await user.selectOptions(screen.getByLabelText("Visit outcome"), "DiagnosticOnly");
    expect(submitButton).toBeDisabled();

    await user.type(screen.getByPlaceholderText(/Completion note/), "Checked unit, no faults found.");
    expect(submitButton).toBeEnabled();
  });

  it("submits a zero-line visit with the chosen outcome and note", async () => {
    const user = userEvent.setup();
    mockSubmitActualWork.mockResolvedValueOnce({ concurrencyVersion: "v2" });
    const { onSubmitted } = renderComposer();

    await user.selectOptions(screen.getByLabelText("Visit outcome"), "NoAccess");
    await user.type(screen.getByPlaceholderText(/Completion note/), "Gate locked, no one home.");
    await user.click(screen.getByRole("button", { name: "Submit visit to office" }));

    await waitFor(() =>
      expect(mockSubmitActualWork).toHaveBeenCalledWith(
        "draft-1",
        { outcome: "NoAccess", completionNote: "Gate locked, no one home." },
        "v1",
      ),
    );
    await waitFor(() => expect(onSubmitted).toHaveBeenCalled());
    expect(screen.getByText("Submitted to office — awaiting review")).toBeInTheDocument();
  });

  it("submitting a draft with at least one line requires neither outcome nor note", () => {
    renderComposer({ draft: emptyDraft({ lines: [draftLine] }) });

    expect(screen.getByRole("button", { name: "Submit visit to office" })).toBeEnabled();
    expect(screen.queryByLabelText("Visit outcome")).not.toBeInTheDocument();
  });

  it("prefills the zero-line outcome and completion note from the draft (BL136 4e-iii)", () => {
    renderComposer({
      draft: emptyDraft({ outcome: "NoAccess", completionNote: "Gate locked, no one home." }),
    });

    expect(screen.getByLabelText("Visit outcome")).toHaveValue("NoAccess");
    expect(screen.getByPlaceholderText(/Completion note/)).toHaveValue("Gate locked, no one home.");
    expect(screen.getByRole("button", { name: "Submit visit to office" })).toBeEnabled();
  });

  it("persists the zero-line disposition on blur once a valid outcome exists, sending outcome + note together", async () => {
    const user = userEvent.setup();
    const { onSetZeroLineDisposition } = renderComposer();

    // A completion-note blur with no outcome yet does not persist (the route rejects a blank outcome).
    await user.type(screen.getByPlaceholderText(/Completion note/), "Checked unit.");
    await user.tab();
    expect(onSetZeroLineDisposition).not.toHaveBeenCalled();

    await user.selectOptions(screen.getByLabelText("Visit outcome"), "DiagnosticOnly");
    await user.tab();
    await waitFor(() =>
      expect(onSetZeroLineDisposition).toHaveBeenCalledWith("DiagnosticOnly", "Checked unit."),
    );
  });

  it("surfaces an invalid-outcome rejection inline without disturbing the fields", async () => {
    const user = userEvent.setup();
    const { onSetZeroLineDisposition } = renderComposer();
    onSetZeroLineDisposition.mockResolvedValue("invalid");

    await user.type(screen.getByPlaceholderText(/Completion note/), "note");
    await user.tab();
    await user.selectOptions(screen.getByLabelText("Visit outcome"), "NoAccess");
    await user.tab();

    await waitFor(() =>
      expect(screen.getByText("The visit outcome is not a valid value.")).toBeInTheDocument(),
    );
    expect(screen.getByLabelText("Visit outcome")).toHaveValue("NoAccess");
  });

  it("clicking Submit from the completion-note field submits only, never the disposition route", async () => {
    const user = userEvent.setup();
    mockSubmitActualWork.mockResolvedValueOnce({ concurrencyVersion: "v2" });
    const { onSetZeroLineDisposition } = renderComposer();

    await user.selectOptions(screen.getByLabelText("Visit outcome"), "NoAccess");
    await user.type(screen.getByPlaceholderText(/Completion note/), "Gate locked.");
    // Ignore any field-to-field persist from the select→textarea blur; assert only about the click.
    onSetZeroLineDisposition.mockClear();
    // Focus is in the textarea; the click's blur must not start a disposition write.
    await user.click(screen.getByRole("button", { name: "Submit visit to office" }));

    await waitFor(() =>
      expect(mockSubmitActualWork).toHaveBeenCalledWith(
        "draft-1",
        { outcome: "NoAccess", completionNote: "Gate locked." },
        "v1",
      ),
    );
    expect(onSetZeroLineDisposition).not.toHaveBeenCalled();
  });

  it("adds a custom off-catalog line with quantity and note", async () => {
    const user = userEvent.setup();
    mockAddActualWorkLine.mockResolvedValueOnce({ lineId: "line-2", actualWorkConcurrencyVersion: "v1" });
    const { onCommitted } = renderComposer();

    await user.type(screen.getByPlaceholderText("Search by name or SKU..."), "gasket");
    await waitFor(() => expect(screen.getByText("Add as custom item")).toBeInTheDocument());
    await user.click(screen.getByText("Add as custom item"));

    await user.type(screen.getByPlaceholderText("Describe the item"), "Rubber gasket");
    await user.clear(screen.getByLabelText("Quantity"));
    await user.type(screen.getByLabelText("Quantity"), "3");
    await user.type(screen.getByPlaceholderText("Note (optional)"), "From truck stock");
    await user.click(screen.getByRole("button", { name: "Add item" }));

    await waitFor(() =>
      expect(mockAddActualWorkLine).toHaveBeenCalledWith(
        "draft-1",
        { offCatalogDescription: "Rubber gasket", actualQuantity: 3, note: "From truck stock" },
        "v1",
      ),
    );
    await waitFor(() => expect(onCommitted).toHaveBeenCalled());
  });

  it("expands an assembly with optional items defaulted out and reports skipped components", async () => {
    const user = userEvent.setup();
    mockGetFieldScopeSearch.mockResolvedValueOnce({
      items: [{ kind: "OfferingAssembly", id: "assembly-1", displayName: "Furnace tune-up", defaultItemCount: 3, catalogItemType: null, externalKey: null }],
      limit: 20,
      hasMore: false,
      nextCursor: null,
    });
    mockExpandActualWorkAssembly.mockResolvedValueOnce({
      lineIds: ["line-2"], skippedCatalogItemIds: ["item-1"], actualWorkConcurrencyVersion: "v2",
    });
    const { onCommitted } = renderComposer();

    await user.type(screen.getByPlaceholderText("Search by name or SKU..."), "furnace");
    await user.click(await screen.findByRole("button", { name: /Furnace tune-up/ }));

    await waitFor(() =>
      expect(mockExpandActualWorkAssembly).toHaveBeenCalledWith(
        "draft-1", { offeringAssemblyId: "assembly-1", includedOptionalItemIds: [] }, "v1",
      ),
    );
    await waitFor(() => expect(onCommitted).toHaveBeenCalled());
    expect(screen.getByRole("status")).toHaveTextContent("1 assembly item added; 1 already on this visit.");
  });

  it("reconciles after a stale-version conflict while expanding an assembly", async () => {
    const user = userEvent.setup();
    mockGetFieldScopeSearch.mockResolvedValueOnce({
      items: [{ kind: "OfferingAssembly", id: "assembly-1", displayName: "Furnace tune-up", defaultItemCount: 3, catalogItemType: null, externalKey: null }],
      limit: 20,
      hasMore: false,
      nextCursor: null,
    });
    mockExpandActualWorkAssembly.mockRejectedValueOnce(new ApiError(409, "ActualWork.VersionMismatch", "conflict"));
    const { onConflict } = renderComposer();

    await user.type(screen.getByPlaceholderText("Search by name or SKU..."), "furnace");
    await user.click(await screen.findByRole("button", { name: /Furnace tune-up/ }));

    await waitFor(() => expect(onConflict).toHaveBeenCalled());
  });

  it("fetches and renders nudge suggestions after adding a catalog line", async () => {
    const user = userEvent.setup();
    mockGetFieldScopeSearch.mockResolvedValueOnce({
      items: [{ kind: "CatalogItem", id: "item-1", displayName: "Filter", defaultItemCount: null, catalogItemType: "Part", externalKey: null }],
      limit: 20,
      hasMore: false,
      nextCursor: null,
    });
    mockAddActualWorkLine.mockResolvedValueOnce({ lineId: "line-2", actualWorkConcurrencyVersion: "v1" });
    mockGetActualWorkNudgeFieldSuggestions.mockResolvedValueOnce({
      ruleId: "rule-1",
      triggerCatalogItemId: "item-1",
      triggerOfferingAssemblyId: null,
      suggestions: [{ id: "sugg-1", order: 1, catalogItemId: "item-2", offeringAssemblyId: null, displayName: "Belt" }],
    });
    renderComposer();

    await user.type(screen.getByPlaceholderText("Search by name or SKU..."), "filter");
    await user.click(await screen.findByText("Filter"));
    await user.click(screen.getByRole("button", { name: "Add item" }));

    await waitFor(() =>
      expect(mockGetActualWorkNudgeFieldSuggestions).toHaveBeenCalledWith("draft-1", { triggerCatalogItemId: "item-1" }),
    );
    expect(await screen.findByText("Often added together")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Belt" })).toBeInTheDocument();
  });

  it("fetches nudge suggestions after expanding an assembly", async () => {
    const user = userEvent.setup();
    mockGetFieldScopeSearch.mockResolvedValueOnce({
      items: [{ kind: "OfferingAssembly", id: "assembly-1", displayName: "Furnace tune-up", defaultItemCount: 3, catalogItemType: null, externalKey: null }],
      limit: 20,
      hasMore: false,
      nextCursor: null,
    });
    mockExpandActualWorkAssembly.mockResolvedValueOnce({
      lineIds: ["line-2"], skippedCatalogItemIds: [], actualWorkConcurrencyVersion: "v2",
    });
    renderComposer();

    await user.type(screen.getByPlaceholderText("Search by name or SKU..."), "furnace");
    await user.click(await screen.findByRole("button", { name: /Furnace tune-up/ }));

    await waitFor(() =>
      expect(mockGetActualWorkNudgeFieldSuggestions).toHaveBeenCalledWith("draft-1", { triggerOfferingAssemblyId: "assembly-1" }),
    );
  });

  it("tapping a nudge suggestion adds it via the existing add-line path and clears the panel", async () => {
    const user = userEvent.setup();
    mockGetFieldScopeSearch.mockResolvedValueOnce({
      items: [{ kind: "CatalogItem", id: "item-1", displayName: "Filter", defaultItemCount: null, catalogItemType: "Part", externalKey: null }],
      limit: 20,
      hasMore: false,
      nextCursor: null,
    });
    mockAddActualWorkLine.mockResolvedValueOnce({ lineId: "line-2", actualWorkConcurrencyVersion: "v1" });
    mockGetActualWorkNudgeFieldSuggestions.mockResolvedValueOnce({
      ruleId: "rule-1",
      triggerCatalogItemId: "item-1",
      triggerOfferingAssemblyId: null,
      suggestions: [{ id: "sugg-1", order: 1, catalogItemId: "item-2", offeringAssemblyId: null, displayName: "Belt" }],
    });
    mockAddActualWorkLine.mockResolvedValueOnce({ lineId: "line-3", actualWorkConcurrencyVersion: "v1" });
    const { onCommitted } = renderComposer();

    await user.type(screen.getByPlaceholderText("Search by name or SKU..."), "filter");
    await user.click(await screen.findByText("Filter"));
    await user.click(screen.getByRole("button", { name: "Add item" }));
    await screen.findByText("Often added together");

    await user.click(screen.getByRole("button", { name: "Belt" }));

    await waitFor(() =>
      expect(mockAddActualWorkLine).toHaveBeenLastCalledWith("draft-1", { catalogItemId: "item-2", actualQuantity: 1, note: null }, "v1"),
    );
    await waitFor(() => expect(onCommitted).toHaveBeenCalledTimes(2));
    expect(screen.queryByText("Often added together")).not.toBeInTheDocument();
  });

  it("dismissing a nudge panel clears it without adding anything", async () => {
    const user = userEvent.setup();
    mockGetFieldScopeSearch.mockResolvedValueOnce({
      items: [{ kind: "CatalogItem", id: "item-1", displayName: "Filter", defaultItemCount: null, catalogItemType: "Part", externalKey: null }],
      limit: 20,
      hasMore: false,
      nextCursor: null,
    });
    mockAddActualWorkLine.mockResolvedValueOnce({ lineId: "line-2", actualWorkConcurrencyVersion: "v1" });
    mockGetActualWorkNudgeFieldSuggestions.mockResolvedValueOnce({
      ruleId: "rule-1",
      triggerCatalogItemId: "item-1",
      triggerOfferingAssemblyId: null,
      suggestions: [{ id: "sugg-1", order: 1, catalogItemId: "item-2", offeringAssemblyId: null, displayName: "Belt" }],
    });
    renderComposer();

    await user.type(screen.getByPlaceholderText("Search by name or SKU..."), "filter");
    await user.click(await screen.findByText("Filter"));
    await user.click(screen.getByRole("button", { name: "Add item" }));
    await screen.findByText("Often added together");

    await user.click(screen.getByRole("button", { name: "Dismiss" }));

    expect(screen.queryByText("Often added together")).not.toBeInTheDocument();
    expect(mockAddActualWorkLine).toHaveBeenCalledTimes(1);
  });

  it("a 409 while accepting a nudge suggestion clears the panel and surfaces onConflict", async () => {
    const user = userEvent.setup();
    mockGetFieldScopeSearch.mockResolvedValueOnce({
      items: [{ kind: "CatalogItem", id: "item-1", displayName: "Filter", defaultItemCount: null, catalogItemType: "Part", externalKey: null }],
      limit: 20,
      hasMore: false,
      nextCursor: null,
    });
    mockAddActualWorkLine.mockResolvedValueOnce({ lineId: "line-2", actualWorkConcurrencyVersion: "v1" });
    mockGetActualWorkNudgeFieldSuggestions.mockResolvedValueOnce({
      ruleId: "rule-1",
      triggerCatalogItemId: "item-1",
      triggerOfferingAssemblyId: null,
      suggestions: [{ id: "sugg-1", order: 1, catalogItemId: "item-2", offeringAssemblyId: null, displayName: "Belt" }],
    });
    mockAddActualWorkLine.mockRejectedValueOnce(new ApiError(409, "ActualWork.VersionMismatch", "conflict"));
    const { onConflict } = renderComposer();

    await user.type(screen.getByPlaceholderText("Search by name or SKU..."), "filter");
    await user.click(await screen.findByText("Filter"));
    await user.click(screen.getByRole("button", { name: "Add item" }));
    await screen.findByText("Often added together");

    await user.click(screen.getByRole("button", { name: "Belt" }));

    await waitFor(() => expect(onConflict).toHaveBeenCalled());
    expect(screen.queryByText("Often added together")).not.toBeInTheDocument();
  });

  it("edits an existing line's quantity and note", async () => {
    const user = userEvent.setup();
    mockUpdateActualWorkLine.mockResolvedValueOnce({ concurrencyVersion: "v2" });
    const { onCommitted } = renderComposer({ draft: emptyDraft({ lines: [draftLine] }) });

    await user.click(screen.getByRole("button", { name: "Edit" }));
    const quantityInput = screen.getByLabelText("Quantity");
    await user.clear(quantityInput);
    await user.type(quantityInput, "5");
    await user.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() =>
      expect(mockUpdateActualWorkLine).toHaveBeenCalledWith("draft-1", "line-1", { actualQuantity: 5, note: null }, "v1"),
    );
    await waitFor(() => expect(onCommitted).toHaveBeenCalled());
  });

  it("removes an existing line", async () => {
    const user = userEvent.setup();
    mockRemoveActualWorkLine.mockResolvedValueOnce({ concurrencyVersion: "v2" });
    const { onCommitted } = renderComposer({ draft: emptyDraft({ lines: [draftLine] }) });

    await user.click(screen.getByRole("button", { name: "Remove" }));

    await waitFor(() => expect(mockRemoveActualWorkLine).toHaveBeenCalledWith("draft-1", "line-1", "v1"));
    await waitFor(() => expect(onCommitted).toHaveBeenCalled());
  });

  it("keeps the remove control disabled until the awaited onCommitted refresh resolves, preventing a second action against a stale version", async () => {
    const user = userEvent.setup();
    mockRemoveActualWorkLine.mockResolvedValueOnce({ concurrencyVersion: "v2" });
    let resolveCommitted!: () => void;
    const onCommitted = vi.fn(() => new Promise<void>((resolve) => (resolveCommitted = resolve)));
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={queryClient}>
        <ActualWorkComposer
          draft={emptyDraft({ lines: [draftLine] })}
          conflictNotice={null}
          isWide={true}
          onClose={vi.fn()}
          onCommitted={onCommitted}
          onConflict={vi.fn()}
          onDismissNotice={vi.fn()}
          onRetryReconciliation={vi.fn()}
          onSubmitted={vi.fn()}
          onDiscarded={vi.fn()}
          onSetDefaultPerformer={vi.fn().mockResolvedValue("set")}
          onSetVisitNote={vi.fn().mockResolvedValue("set")}
          onSetZeroLineDisposition={vi.fn().mockResolvedValue("set")}
          onHandOffToOffice={vi.fn().mockResolvedValue("handed-off")}
        />
      </QueryClientProvider>,
    );

    const removeButton = screen.getByRole("button", { name: "Remove" });
    await user.click(removeButton);

    await waitFor(() => expect(mockRemoveActualWorkLine).toHaveBeenCalled());
    // The write itself has settled, but onCommitted's refresh is still in flight — the control must
    // stay disabled so a rapid second click cannot fire against the pre-refresh concurrencyVersion.
    await waitFor(() => expect(screen.getByRole("button", { name: "Remove" })).toBeDisabled());

    resolveCommitted();
    await waitFor(() => expect(screen.getByRole("button", { name: "Remove" })).toBeEnabled());
  });

  describe("discard visit", () => {
    it("renders discard as a visible button in the normal composer and the performer-gated state", () => {
      const { unmount } = renderComposer({ draft: emptyDraft({ lines: [draftLine] }) });
      expect(screen.getByRole("button", { name: "Discard this visit" })).toBeVisible();
      unmount();

      renderComposer({
        draft: emptyDraft({ defaultPerformedByAccountUserId: null, defaultPerformerDisplayName: null }),
      });
      expect(screen.getByText("Whose work is this?")).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Discard this visit" })).toBeVisible();
    });

    it("first click opens the confirmation dialog and does not call discard", async () => {
      const user = userEvent.setup();
      const { onDiscarded } = renderComposer({ draft: emptyDraft({ lines: [draftLine] }) });

      await user.click(screen.getByRole("button", { name: "Discard this visit" }));

      const dialog = screen.getByRole("alertdialog");
      expect(dialog).toHaveTextContent("Discard this visit?");
      expect(dialog).toHaveTextContent("This permanently removes this unfinished visit and its recorded work.");
      expect(mockDiscardActualWork).not.toHaveBeenCalled();
      expect(onDiscarded).not.toHaveBeenCalled();
    });

    it("Keep editing dismisses the dialog and leaves the draft untouched", async () => {
      const user = userEvent.setup();
      const { onDiscarded } = renderComposer({ draft: emptyDraft({ lines: [draftLine] }) });

      await user.click(screen.getByRole("button", { name: "Discard this visit" }));
      await user.click(screen.getByRole("button", { name: "Keep editing" }));

      expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument();
      expect(mockDiscardActualWork).not.toHaveBeenCalled();
      expect(onDiscarded).not.toHaveBeenCalled();
      expect(screen.getByText("Filter")).toBeInTheDocument();
    });

    it("Discard visit confirms and calls the existing discard action once", async () => {
      const user = userEvent.setup();
      mockDiscardActualWork.mockResolvedValueOnce(undefined);
      const { onDiscarded } = renderComposer({ draft: emptyDraft({ lines: [draftLine] }) });

      await user.click(screen.getByRole("button", { name: "Discard this visit" }));
      await user.click(screen.getByRole("button", { name: "Discard visit" }));

      await waitFor(() => expect(mockDiscardActualWork).toHaveBeenCalledWith("draft-1", "v1"));
      await waitFor(() => expect(onDiscarded).toHaveBeenCalled());
      expect(mockDiscardActualWork).toHaveBeenCalledTimes(1);
    });

    it("prevents duplicate discard submits while the mutation is in flight", async () => {
      const user = userEvent.setup();
      let resolveDiscard!: () => void;
      mockDiscardActualWork.mockReturnValueOnce(new Promise<void>((resolve) => (resolveDiscard = resolve)));
      renderComposer({ draft: emptyDraft({ lines: [draftLine] }) });

      await user.click(screen.getByRole("button", { name: "Discard this visit" }));
      const confirm = screen.getByRole("button", { name: "Discard visit" });
      await user.click(confirm);
      await waitFor(() => expect(confirm).toBeDisabled());
      await user.click(confirm);

      resolveDiscard();
      await waitFor(() => expect(mockDiscardActualWork).toHaveBeenCalledTimes(1));
    });
  });

  it("a 409 on line add surfaces onConflict rather than a field-level error", async () => {
    const user = userEvent.setup();
    mockAddActualWorkLine.mockRejectedValueOnce(new ApiError(409, "ActualWork.VersionMismatch", "conflict"));
    const { onConflict } = renderComposer();

    await user.type(screen.getByPlaceholderText("Search by name or SKU..."), "gasket");
    await waitFor(() => expect(screen.getByText("Add as custom item")).toBeInTheDocument());
    await user.click(screen.getByText("Add as custom item"));
    await user.type(screen.getByPlaceholderText("Describe the item"), "Rubber gasket");
    await user.click(screen.getByRole("button", { name: "Add item" }));

    await waitFor(() => expect(onConflict).toHaveBeenCalled());
  });

  it("a network failure on remove shows the composer-level connection banner instead of onConflict", async () => {
    const user = userEvent.setup();
    mockRemoveActualWorkLine.mockRejectedValueOnce(new TypeError("Failed to fetch"));
    const { onConflict } = renderComposer({ draft: emptyDraft({ lines: [draftLine] }) });

    await user.click(screen.getByRole("button", { name: "Remove" }));

    const banner = await screen.findByRole("alert");
    expect(banner).toHaveTextContent("Couldn't remove this item. Check your connection and retry.");
    expect(onConflict).not.toHaveBeenCalled();
  });

  it("Retry on the connection banner re-invokes the exact failed operation and clears the banner on success", async () => {
    const user = userEvent.setup();
    mockRemoveActualWorkLine.mockRejectedValueOnce(new TypeError("Failed to fetch"));
    mockRemoveActualWorkLine.mockResolvedValueOnce({ concurrencyVersion: "v2" });
    const { onCommitted } = renderComposer({ draft: emptyDraft({ lines: [draftLine] }) });

    await user.click(screen.getByRole("button", { name: "Remove" }));
    await screen.findByRole("alert");

    await user.click(screen.getByRole("button", { name: "Retry" }));

    await waitFor(() => expect(mockRemoveActualWorkLine).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(onCommitted).toHaveBeenCalled());
    await waitFor(() => expect(screen.queryByRole("alert")).not.toBeInTheDocument());
  });

  it("Retry replays the original payload even if the technician edited fields after the failure", async () => {
    const user = userEvent.setup();
    mockUpdateActualWorkLine.mockRejectedValueOnce(new TypeError("Failed to fetch"));
    mockUpdateActualWorkLine.mockResolvedValueOnce({ concurrencyVersion: "v2" });
    renderComposer({ draft: emptyDraft({ lines: [draftLine] }) });

    await user.click(screen.getByRole("button", { name: "Edit" }));
    const quantityInput = screen.getByLabelText("Quantity");
    await user.clear(quantityInput);
    await user.type(quantityInput, "5");
    await user.click(screen.getByRole("button", { name: "Save" }));

    await screen.findByRole("alert");
    // The failed Save exits editing mode, so re-enter it and change the field before retrying —
    // Retry must still replay the quantity that was actually submitted (5), not the edited one (9).
    await user.click(screen.getByRole("button", { name: "Edit" }));
    const quantityInputAfterFailure = screen.getByLabelText("Quantity");
    await user.clear(quantityInputAfterFailure);
    await user.type(quantityInputAfterFailure, "9");

    await user.click(screen.getByRole("button", { name: "Retry" }));

    await waitFor(() => expect(mockUpdateActualWorkLine).toHaveBeenCalledTimes(2));
    expect(mockUpdateActualWorkLine).toHaveBeenLastCalledWith("draft-1", "line-1", { actualQuantity: 5, note: null }, "v1");
  });

  it("a later connection failure on a different action replaces the earlier banner", async () => {
    const user = userEvent.setup();
    mockAddActualWorkLine.mockRejectedValueOnce(new TypeError("Failed to fetch"));
    mockRemoveActualWorkLine.mockRejectedValueOnce(new TypeError("Failed to fetch"));
    renderComposer({ draft: emptyDraft({ lines: [draftLine] }) });

    await user.type(screen.getByPlaceholderText("Search by name or SKU..."), "gasket");
    await waitFor(() => expect(screen.getByText("Add as custom item")).toBeInTheDocument());
    await user.click(screen.getByText("Add as custom item"));
    await user.type(screen.getByPlaceholderText("Describe the item"), "Rubber gasket");
    await user.click(screen.getByRole("button", { name: "Add item" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Couldn't add actual work.");

    await user.click(screen.getByRole("button", { name: "Remove" }));
    await waitFor(() => expect(screen.getByRole("alert")).toHaveTextContent("Couldn't remove this item."));
    expect(screen.getAllByRole("alert")).toHaveLength(1);
  });

  // Slice 5c-2A: submit's retry-success closes the composer (`onSubmitted`) in the same commit
  // that clears the connection failure, so this uses a wrapper that actually unmounts
  // `ActualWorkComposer` on `onSubmitted` (unlike `renderComposer`'s inert stub) to prove the
  // root-mounted `LiveAnnouncerRegion` — not local composer state — is what carries the
  // announcement.
  function RetrySuccessHost() {
    const [open, setOpen] = useState(true);
    return (
      <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
        <LiveAnnouncerRegion />
        {open && (
          <ActualWorkComposer
            draft={emptyDraft()}
            conflictNotice={null}
            isWide={true}
            onClose={vi.fn()}
            onCommitted={vi.fn()}
            onConflict={vi.fn()}
            onDismissNotice={vi.fn()}
            onRetryReconciliation={vi.fn()}
            onSubmitted={() => setOpen(false)}
            onDiscarded={vi.fn()}
            onSetDefaultPerformer={vi.fn().mockResolvedValue("set")}
            onSetVisitNote={vi.fn().mockResolvedValue("set")}
            onSetZeroLineDisposition={vi.fn().mockResolvedValue("set")}
          onHandOffToOffice={vi.fn().mockResolvedValue("handed-off")}
          />
        )}
      </QueryClientProvider>
    );
  }

  it("announces 'Retry succeeded.' via the persistent live region after a submit retry succeeds and closes the composer", async () => {
    const user = userEvent.setup();
    mockSubmitActualWork.mockRejectedValueOnce(new TypeError("Failed to fetch"));
    mockSubmitActualWork.mockResolvedValueOnce({ concurrencyVersion: "v2" });
    render(<RetrySuccessHost />);

    await user.selectOptions(screen.getByLabelText("Visit outcome"), "NoAccess");
    await user.type(screen.getByPlaceholderText(/Completion note/), "Gate locked, no one home.");
    await user.click(screen.getByRole("button", { name: "Submit visit to office" }));
    await screen.findByRole("alert");

    await user.click(screen.getByRole("button", { name: "Retry" }));

    await waitFor(() => expect(mockSubmitActualWork).toHaveBeenCalledTimes(2));
    // The composer (and its local connection-failure banner) is gone — the announcement can only
    // have reached the DOM through the root-mounted region, which outlived it.
    await waitFor(() => expect(screen.queryByRole("alert")).not.toBeInTheDocument());
    await waitFor(() => expect(screen.getByRole("status")).toHaveTextContent("Retry succeeded."));
  });

  it("does not announce 'Retry succeeded.' for an ordinary first-attempt submit success", async () => {
    const user = userEvent.setup();
    mockSubmitActualWork.mockResolvedValueOnce({ concurrencyVersion: "v2" });
    render(<RetrySuccessHost />);

    await user.selectOptions(screen.getByLabelText("Visit outcome"), "NoAccess");
    await user.type(screen.getByPlaceholderText(/Completion note/), "Gate locked, no one home.");
    await user.click(screen.getByRole("button", { name: "Submit visit to office" }));

    await waitFor(() => expect(mockSubmitActualWork).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(screen.queryByText(/Record completed work/)).not.toBeInTheDocument());
    expect(screen.getByRole("status")).toHaveTextContent("");
  });

  it("renders the conflict notice with a dismiss action", async () => {
    const user = userEvent.setup();
    const { onDismissNotice } = renderComposer({ conflictNotice: "This visit changed elsewhere — refreshed with the latest draft. Try again." });

    expect(screen.getByRole("status")).toHaveTextContent("This visit changed elsewhere");
    await user.click(screen.getByRole("button", { name: "Dismiss" }));

    expect(onDismissNotice).toHaveBeenCalled();
  });

  describe("performer gate (transcribe path)", () => {
    const noDefaultDraft = () =>
      emptyDraft({ defaultPerformedByAccountUserId: null, defaultPerformerDisplayName: null });

    it("blocks the entire add region — search, assembly, nudge — until a default performer is persisted", () => {
      renderComposer({ draft: noDefaultDraft() });

      expect(screen.queryByPlaceholderText("Search by name or SKU...")).not.toBeInTheDocument();
      expect(screen.getByText("Whose work is this?")).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Confirm technician" })).toBeDisabled();
    });

    it("persists the selected technician, then un-gates add-line and expand-assembly which both inherit it", async () => {
      const user = userEvent.setup();
      mockGetFieldScopeSearch.mockResolvedValue({
        items: [
          { kind: "CatalogItem", id: "item-1", displayName: "Filter", defaultItemCount: null, catalogItemType: "Part", externalKey: null },
          { kind: "OfferingAssembly", id: "assembly-1", displayName: "Furnace tune-up", defaultItemCount: 3, catalogItemType: null, externalKey: null },
        ],
        limit: 20,
        hasMore: false,
        nextCursor: null,
      });
      mockAddActualWorkLine.mockResolvedValue({ lineId: "line-2", actualWorkConcurrencyVersion: "v1" });
      mockExpandActualWorkAssembly.mockResolvedValue({ lineIds: ["line-3"], skippedCatalogItemIds: [], actualWorkConcurrencyVersion: "v1" });

      // The composer re-renders with a populated default once the parent hook refetches; model that
      // by swapping the draft prop after onSetDefaultPerformer resolves.
      const onSetDefaultPerformer = vi.fn().mockResolvedValue("set");
      function Host() {
        const [draft, setDraft] = useState(noDefaultDraft());
        return (
          <ActualWorkComposer
            draft={draft}
            conflictNotice={null}
            isWide
            onClose={vi.fn()}
            onCommitted={vi.fn().mockResolvedValue(undefined)}
            onConflict={vi.fn()}
            onDismissNotice={vi.fn()}
            onRetryReconciliation={vi.fn()}
            onSubmitted={vi.fn()}
            onDiscarded={vi.fn()}
            currentAccountUserId="au-recorder"
            onSetDefaultPerformer={async (id) => {
              const outcome = await onSetDefaultPerformer(id);
              setDraft(emptyDraft({ defaultPerformedByAccountUserId: id, defaultPerformerDisplayName: "Dana Tech" }));
              return outcome;
            }}
            onSetVisitNote={vi.fn().mockResolvedValue("set")}
            onSetZeroLineDisposition={vi.fn().mockResolvedValue("set")}
          onHandOffToOffice={vi.fn().mockResolvedValue("handed-off")}
          />
        );
      }
      const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
      render(
        <QueryClientProvider client={queryClient}>
          <Host />
        </QueryClientProvider>,
      );

      await screen.findByRole("option", { name: /Dana Tech/ });
      await user.selectOptions(screen.getByLabelText("Technician"), "au-tech");
      await user.click(screen.getByRole("button", { name: "Confirm technician" }));
      expect(onSetDefaultPerformer).toHaveBeenCalledWith("au-tech");

      // Add region is now live and attributes to the persisted performer.
      await screen.findByText(/Recording work for/);
      await user.type(await screen.findByPlaceholderText("Search by name or SKU..."), "furnace");
      await user.click(await screen.findByText("Filter"));
      await user.click(screen.getByRole("button", { name: "Add item" }));
      await waitFor(() => expect(mockAddActualWorkLine).toHaveBeenCalledWith("draft-1", expect.any(Object), "v1"));

      await user.type(screen.getByPlaceholderText("Search by name or SKU..."), "furnace");
      await user.click(await screen.findByRole("button", { name: /Furnace tune-up/ }));
      await waitFor(() => expect(mockExpandActualWorkAssembly).toHaveBeenCalled());
    });

    it("surfaces an ineligible outcome without leaving the gate", async () => {
      const user = userEvent.setup();
      renderComposer({
        draft: noDefaultDraft(),
        onSetDefaultPerformer: vi.fn().mockResolvedValue("ineligible"),
      });

      await screen.findByRole("option", { name: /Dana Tech/ });
      await user.selectOptions(screen.getByLabelText("Technician"), "au-tech");
      await user.click(screen.getByRole("button", { name: "Confirm technician" }));

      expect(await screen.findByText("That person can't be recorded as the performer.")).toBeInTheDocument();
      expect(screen.queryByPlaceholderText("Search by name or SKU...")).not.toBeInTheDocument();
    });
  });

  describe("inline (workspace-route) presentation", () => {
    it("renders as a plain region with no dialog chrome and no header close/back control", () => {
      renderComposer({ presentation: "inline", isWide: false });

      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "Close" })).not.toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "← Back to Request" })).not.toBeInTheDocument();
      // Capture surface and its autosave state are still present.
      expect(screen.getByRole("heading", { name: "Record completed work" })).toBeInTheDocument();
      expect(screen.getByText("Auto-saved")).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Submit visit to office" })).toBeInTheDocument();
    });

    it("is a single bounded capture scroll surface with no dialog chrome", () => {
      const { container } = renderComposer({ presentation: "inline", isWide: false });

      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
      expect(container.querySelectorAll(".overflow-y-auto")).toHaveLength(1);
    });

    it("starts an empty draft in the neutral choice with no expanded zero-line fields", () => {
      renderComposer({ presentation: "inline", isWide: false });

      expect(screen.getByText("Choose how to record this visit")).toBeInTheDocument();
      expect(screen.getByRole("button", { name: /Add work\/material lines/ })).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Record a zero-line outcome" })).toBeInTheDocument();
      // No outcome/note form, and no search field, until a path is chosen.
      expect(screen.queryByLabelText("Visit outcome")).not.toBeInTheDocument();
      expect(screen.queryByPlaceholderText(/Search by name or SKU/)).not.toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Submit visit to office" })).toBeDisabled();
    });

    it("shows the zero-line outcome/note form only after choosing that path", async () => {
      const user = userEvent.setup();
      renderComposer({ presentation: "inline", isWide: false });

      await user.click(screen.getByRole("button", { name: "Record a zero-line outcome" }));

      expect(screen.getByLabelText("Visit outcome")).toBeInTheDocument();
      expect(screen.getByPlaceholderText(/Completion note/)).toBeInTheDocument();
      expect(screen.queryByText("Choose how to record this visit")).not.toBeInTheDocument();
    });

    it("switches to work mode from the neutral choice and shows search, not the zero-line form", async () => {
      const user = userEvent.setup();
      renderComposer({ presentation: "inline", isWide: false });

      await user.click(screen.getByRole("button", { name: /Add work\/material lines/ }));

      expect(screen.getByPlaceholderText(/Search by name or SKU/)).toBeInTheDocument();
      expect(screen.queryByLabelText("Visit outcome")).not.toBeInTheDocument();
    });

    it("collapses the zero-line form when the technician activates search", async () => {
      const user = userEvent.setup();
      renderComposer({ presentation: "inline", isWide: false });

      await user.click(screen.getByRole("button", { name: "Record a zero-line outcome" }));
      expect(screen.getByLabelText("Visit outcome")).toBeInTheDocument();

      // Search stays reachable in zero-line mode; touching it selects work mode.
      await user.type(screen.getByPlaceholderText(/Search by name or SKU/), "filt");

      expect(screen.queryByLabelText("Visit outcome")).not.toBeInTheDocument();
    });

    it("keeps the first search results within the non-scrolled capture area", async () => {
      const user = userEvent.setup();
      mockGetFieldScopeSearch.mockResolvedValue({
        items: [
          { id: "c1", kind: "CatalogItem", displayName: "Air filter 20x25", sku: "AF-1", defaultItemCount: null },
        ],
        limit: 20,
        hasMore: false,
        nextCursor: null,
      });
      const { container } = renderComposer({ presentation: "inline", isWide: false });

      await user.click(screen.getByRole("button", { name: /Add work\/material lines/ }));
      await user.type(screen.getByPlaceholderText(/Search by name or SKU/), "filter");

      const result = await screen.findByRole("button", { name: /Air filter 20x25/ });
      const scrollRegion = container.querySelector(".overflow-y-auto");
      // The result renders inside the single capture scroll region and that region is not scrolled.
      expect(scrollRegion?.contains(result)).toBe(true);
      expect(scrollRegion?.scrollTop ?? 0).toBe(0);
    });

    it("hides the zero-line form and blocks submission while the performer gate is re-opened", async () => {
      const user = userEvent.setup();
      renderComposer({ presentation: "inline", isWide: false });

      await user.click(screen.getByRole("button", { name: "Record a zero-line outcome" }));
      expect(screen.getByLabelText("Visit outcome")).toBeInTheDocument();

      await user.click(screen.getByRole("button", { name: "Change" }));

      // Performer gate is open → mode surface is suspended: no outcome/note, no submit.
      expect(await screen.findByLabelText("Technician")).toBeInTheDocument();
      expect(screen.queryByLabelText("Visit outcome")).not.toBeInTheDocument();
      expect(screen.queryByPlaceholderText(/Completion note/)).not.toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Submit visit to office" })).toBeDisabled();

      // Cancelling the change restores the chosen zero-line mode.
      await user.click(screen.getByRole("button", { name: "Cancel" }));
      expect(screen.getByLabelText("Visit outcome")).toBeInTheDocument();
    });

    it("returns the empty-draft surface to neutral after the last line is removed", async () => {
      const user = userEvent.setup();
      mockRemoveActualWorkLine.mockResolvedValue(undefined);

      function Harness() {
        const [lines, setLines] = useState([draftLine]);
        return (
          <ActualWorkComposer
            draft={emptyDraft({ lines })}
            presentation="inline"
            conflictNotice={null}
            isWide={false}
            onClose={vi.fn()}
            onCommitted={async () => setLines([])}
            onConflict={vi.fn()}
            onDismissNotice={vi.fn()}
            onRetryReconciliation={vi.fn()}
            onSubmitted={vi.fn()}
            onDiscarded={vi.fn()}
            onSetDefaultPerformer={vi.fn().mockResolvedValue("set")}
            onSetVisitNote={vi.fn().mockResolvedValue("set")}
            onSetZeroLineDisposition={vi.fn().mockResolvedValue("set")}
            onHandOffToOffice={vi.fn().mockResolvedValue("handed-off")}
          />
        );
      }

      render(
        <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
          <Harness />
        </QueryClientProvider>,
      );

      expect(screen.queryByText("Choose how to record this visit")).not.toBeInTheDocument();
      await user.click(screen.getByRole("button", { name: "Remove Filter" }));

      expect(await screen.findByText("Choose how to record this visit")).toBeInTheDocument();
      expect(screen.queryByLabelText("Visit outcome")).not.toBeInTheDocument();
    });

    it("removes every zero-line and mode control from the DOM once a line exists", () => {
      renderComposer({
        presentation: "inline",
        isWide: false,
        draft: emptyDraft({ lines: [draftLine] }),
      });

      expect(screen.queryByLabelText("Visit outcome")).not.toBeInTheDocument();
      expect(screen.queryByText("Choose how to record this visit")).not.toBeInTheDocument();
      expect(
        screen.queryByRole("button", { name: "Record a zero-line outcome" }),
      ).not.toBeInTheDocument();
    });

    it("shows a compact confirmed-performer summary with a safe Change path", async () => {
      const user = userEvent.setup();
      const { onSetDefaultPerformer } = renderComposer({ presentation: "inline", isWide: false });

      expect(screen.getByText(/Performed by/)).toBeInTheDocument();
      expect(screen.getByText("Sam Field")).toBeInTheDocument();

      await user.click(screen.getByRole("button", { name: "Change" }));

      // Explicit gate re-opens; nothing auto-saved just by opening it.
      const select = await screen.findByLabelText("Technician");
      expect(onSetDefaultPerformer).not.toHaveBeenCalled();
      // Line entry is blocked until re-confirmation.
      expect(screen.queryByPlaceholderText(/Search by name or SKU/)).not.toBeInTheDocument();

      await user.selectOptions(select, "au-tech");
      await user.click(screen.getByRole("button", { name: "Confirm technician" }));
      expect(onSetDefaultPerformer).toHaveBeenCalledWith("au-tech");
    });

    it("lets Change be cancelled back to the summary without saving", async () => {
      const user = userEvent.setup();
      const { onSetDefaultPerformer } = renderComposer({ presentation: "inline", isWide: false });

      await user.click(screen.getByRole("button", { name: "Change" }));
      await user.click(await screen.findByRole("button", { name: "Cancel" }));

      expect(onSetDefaultPerformer).not.toHaveBeenCalled();
      expect(screen.getByRole("button", { name: "Change" })).toBeInTheDocument();
    });

    it("collapses an empty visit note to an affordance that expands and still autosaves on blur", async () => {
      const user = userEvent.setup();
      const { onSetVisitNote } = renderComposer({ presentation: "inline", isWide: false });

      expect(screen.queryByLabelText("Visit note")).not.toBeInTheDocument();
      await user.click(screen.getByRole("button", { name: "Add visit note" }));

      const textarea = screen.getByLabelText("Visit note");
      await user.type(textarea, "Replaced capacitor");
      await user.tab();

      await waitFor(() => expect(onSetVisitNote).toHaveBeenCalledWith("Replaced capacitor"));
    });

    it("keeps a non-empty visit note visible with no affordance", () => {
      renderComposer({
        presentation: "inline",
        isWide: false,
        draft: emptyDraft({ visitNote: "Prior note" }),
      });

      expect(screen.getByLabelText("Visit note")).toHaveValue("Prior note");
      expect(screen.queryByRole("button", { name: "Add visit note" })).not.toBeInTheDocument();
    });

    it("keeps key line-item actions accessible in the compact row and reveals detail on expand", async () => {
      const user = userEvent.setup();
      renderComposer({
        presentation: "inline",
        isWide: false,
        draft: emptyDraft({ lines: [{ ...draftLine, note: "torn seal" }] }),
      });

      expect(screen.getByRole("button", { name: "Edit Filter" })).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Remove Filter" })).toBeInTheDocument();

      await user.click(screen.getByRole("button", { name: "Show details for Filter" }));
      expect(screen.getByText("torn seal")).toBeInTheDocument();

      await user.click(screen.getByRole("button", { name: "Edit Filter" }));
      expect(screen.getByLabelText("Quantity")).toHaveValue(2);
    });
  });

  describe("desktop composer formatting", () => {
    it("shows an inline empty-state cue pointing to lines or a zero-line outcome when the draft has no lines", () => {
      renderComposer();

      expect(
        screen.getByText(/record a zero-line outcome .* in the submit area below/i),
      ).toBeInTheDocument();
    });

    it("drops the empty-state cue once a line exists", () => {
      renderComposer({ draft: emptyDraft({ lines: [draftLine] }) });

      expect(screen.queryByText(/record a zero-line outcome/i)).not.toBeInTheDocument();
    });

    it("marks the zero-line outcome and completion note as required", () => {
      renderComposer();

      expect(screen.getByLabelText("Visit outcome")).toHaveAttribute("aria-required", "true");
      expect(screen.getByPlaceholderText(/Completion note/)).toHaveAttribute("aria-required", "true");
    });

    it("labels the visit note as optional", () => {
      renderComposer();

      expect(screen.getByText("Optional")).toBeInTheDocument();
      expect(screen.getByLabelText("Visit note")).toBeInTheDocument();
    });
  });

  describe("visit note + per-line performer (4c-iii)", () => {
    it("autosaves the visit note on blur, trimming to null when emptied", async () => {
      const user = userEvent.setup();
      const { onSetVisitNote } = renderComposer();

      const field = screen.getByLabelText("Visit note");
      await user.type(field, "  Gate code 4321  ");
      await user.tab();
      await waitFor(() => expect(onSetVisitNote).toHaveBeenCalledWith("Gate code 4321"));
    });

    it("does not write the visit note on blur when it is unchanged", async () => {
      const user = userEvent.setup();
      const { onSetVisitNote } = renderComposer({
        draft: emptyDraft({ visitNote: "Existing note" }),
      });

      const field = screen.getByLabelText("Visit note");
      expect(field).toHaveValue("Existing note");
      await user.click(field);
      await user.tab();
      expect(onSetVisitNote).not.toHaveBeenCalled();
    });

    it("surfaces the too-long outcome under the textarea", async () => {
      const user = userEvent.setup();
      const { onSetVisitNote } = renderComposer();
      onSetVisitNote.mockResolvedValueOnce("too-long");

      await user.type(screen.getByLabelText("Visit note"), "x");
      await user.tab();

      expect(
        await screen.findByText("The visit note must be 2,000 characters or fewer."),
      ).toBeInTheDocument();
    });

    it("sends an explicit per-line performer override when one is picked in the add panel", async () => {
      const user = userEvent.setup();
      mockAddActualWorkLine.mockResolvedValueOnce({ lineId: "line-2", actualWorkConcurrencyVersion: "v1" });
      renderComposer();

      await user.type(screen.getByPlaceholderText("Search by name or SKU..."), "gasket");
      await waitFor(() => expect(screen.getByText("Add as custom item")).toBeInTheDocument());
      await user.click(screen.getByText("Add as custom item"));
      await user.type(screen.getByPlaceholderText("Describe the item"), "Rubber gasket");

      await screen.findByRole("option", { name: "Dana Tech" });
      await user.selectOptions(screen.getByLabelText("Performed by"), "au-tech");
      await user.click(screen.getByRole("button", { name: "Add item" }));

      await waitFor(() =>
        expect(mockAddActualWorkLine).toHaveBeenCalledWith(
          "draft-1",
          { offCatalogDescription: "Rubber gasket", actualQuantity: 1, note: null, performedByAccountUserId: "au-tech" },
          "v1",
        ),
      );
    });

    it("omits the performer field when the add panel keeps the ticket default", async () => {
      const user = userEvent.setup();
      mockAddActualWorkLine.mockResolvedValueOnce({ lineId: "line-2", actualWorkConcurrencyVersion: "v1" });
      renderComposer();

      await user.type(screen.getByPlaceholderText("Search by name or SKU..."), "gasket");
      await waitFor(() => expect(screen.getByText("Add as custom item")).toBeInTheDocument());
      await user.click(screen.getByText("Add as custom item"));
      await user.type(screen.getByPlaceholderText("Describe the item"), "Rubber gasket");
      await user.click(screen.getByRole("button", { name: "Add item" }));

      await waitFor(() =>
        expect(mockAddActualWorkLine).toHaveBeenCalledWith(
          "draft-1",
          { offCatalogDescription: "Rubber gasket", actualQuantity: 1, note: null },
          "v1",
        ),
      );
    });

    it("shows each existing line's performer read-only, with a fallback for an unresolved id", () => {
      renderComposer({
        draft: emptyDraft({
          lines: [
            { ...draftLine, id: "l1", displayNameSnapshot: "Filter", performerDisplayName: "Dana Tech" },
            { ...draftLine, id: "l2", displayNameSnapshot: "Coil", performerDisplayName: null },
          ],
        }),
      });

      expect(screen.getByText("Dana Tech")).toBeInTheDocument();
      expect(screen.getByText("Unknown performer")).toBeInTheDocument();
    });
  });

  describe("hand off to office (Slice 4d)", () => {
    it("hands the visit to a chosen office member and does not send a reason", async () => {
      const user = userEvent.setup();
      const onHandOffToOffice = vi.fn().mockResolvedValue("handed-off");
      renderComposer({ currentAccountUserId: "au-self", onHandOffToOffice });

      await user.click(screen.getByRole("button", { name: "Hand off to office" }));
      const dialog = await screen.findByRole("alertdialog");
      await user.selectOptions(screen.getByLabelText("Hand off to"), "au-tech");
      await user.click(within(dialog).getByRole("button", { name: "Hand off" }));

      expect(onHandOffToOffice).toHaveBeenCalledWith("au-tech");
      expect(onHandOffToOffice).toHaveBeenCalledTimes(1);
    });

    it("keeps the dialog open with an error when the target is ineligible", async () => {
      const user = userEvent.setup();
      const onHandOffToOffice = vi.fn().mockResolvedValue("ineligible");
      renderComposer({ currentAccountUserId: "au-self", onHandOffToOffice });

      await user.click(screen.getByRole("button", { name: "Hand off to office" }));
      await user.selectOptions(await screen.findByLabelText("Hand off to"), "au-tech");
      await user.click(screen.getByRole("button", { name: "Hand off" }));

      expect(await screen.findByText(/can't take over this visit/i)).toBeInTheDocument();
      expect(screen.getByRole("alertdialog")).toBeInTheDocument();
    });

    it("excludes the current recorder from the candidate list", async () => {
      const user = userEvent.setup();
      mockGetActualWorkPerformerCandidates.mockResolvedValue({
        candidates: [
          { accountUserId: "au-self", displayName: "Sam Field", role: "operator" },
          { accountUserId: "au-tech", displayName: "Dana Tech", role: "operator" },
        ],
      });
      renderComposer({ currentAccountUserId: "au-self", onHandOffToOffice: vi.fn() });

      await user.click(screen.getByRole("button", { name: "Hand off to office" }));
      await screen.findByLabelText("Hand off to");

      expect(screen.getByRole("option", { name: "Dana Tech" })).toBeInTheDocument();
      expect(screen.queryByRole("option", { name: "Sam Field" })).not.toBeInTheDocument();
    });
  });
});
