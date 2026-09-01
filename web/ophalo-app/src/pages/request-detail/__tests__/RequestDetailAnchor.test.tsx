import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { RequestDetailAnchor } from "../RequestDetailAnchor";
import { mockRequestDetails, OWNER_ACTIONS } from "../../../mocks/fixtures";
import type { KeepRequestDetailResult } from "../../../lib/apiClient";

beforeEach(() => {
  vi.stubEnv("VITE_PUBLIC_BASE_URL", "http://localhost:3000");
});

// Three-row desktop Anchor hierarchy (locked correction, 2026-08-22): one outer bordered/rounded
// card with reference/status/attention (row 1 left) and the no-attention lifecycle primary action
// only (row 1 right, RD-058B-2), full-width customer identity (row 2), a divider, then three
// stable context columns (row 3). The inner card is wrapped to `max-w-4xl mx-auto` so its content
// shares the Work Canvas reading boundary (RD-058B-2).

function baseDetail(): KeepRequestDetailResult {
  return {
    ...mockRequestDetails["mock-req-001"],
    effectiveAttention: { ...mockRequestDetails["mock-req-001"].effectiveAttention },
  };
}

function renderAnchor(detail: KeepRequestDetailResult) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <RequestDetailAnchor
        requestId="req-1"
        detail={detail}
        highlights={{}}
        showProminentFeedbackCard={false}
        onDetailUpdated={vi.fn()}
        onContactLaunched={vi.fn()}
        onEditLocation={vi.fn()}
        onOpenReassignOwner={vi.fn()}
        onOpenWatchers={vi.fn()}
        onRecordFollowUp={vi.fn()}
        onCreateFollowUp={vi.fn()}
        onReviewSuccess={vi.fn()}
        canRecordShareIntent={false}
        needsShare={false}
        onOpenShareDrawer={vi.fn()}
        onOpenClearAttention={vi.fn()}
        onActivateCustomerUpdateComposer={vi.fn()}
      />
    </QueryClientProvider>,
  );
}

describe("RequestDetailAnchor — three-row desktop hierarchy", () => {
  it("renders one outer bordered card with row 1 badges, row 2 full-width identity, a divider, and three row-3 context columns", () => {
    const detail = baseDetail();
    const { container } = renderAnchor(detail);

    // Row 1 left: reference/status
    expect(screen.getByText(detail.referenceCode)).toBeInTheDocument();
    // Row 2: customer identity as its own full-width row
    expect(screen.getByRole("heading", { name: detail.customerName })).toBeInTheDocument();
    // Divider between row 2 and row 3
    expect(container.querySelector(".border-t")).not.toBeNull();
    // Row 3: three stable context columns
    expect(screen.getByText("Customer contact")).toBeInTheDocument();
    expect(screen.getByText("Service location")).toBeInTheDocument();
    expect(screen.getByText("Owner")).toBeInTheDocument();
    // Row 3 renders as a three-column grid, not a flattened single-line strip
    const grid = container.querySelector(".grid.sm\\:grid-cols-3");
    expect(grid).not.toBeNull();

    // Not a bare full-width strip — the Anchor is one rounded, bordered outer card
    const card = container.querySelector(".rounded-xl.border");
    expect(card).not.toBeNull();
    // Inner content is bounded to the shared Work Canvas reading frame (RD-058B-2).
    expect(card!.className).toContain("max-w-4xl");
    expect(card!.className).toContain("mx-auto");
  });

  it("shows the filled primary action for an eligible, non-attention, non-Received request", () => {
    const detail: KeepRequestDetailResult = { ...baseDetail(), attentionLevel: "none" };
    renderAnchor(detail);
    expect(screen.getByRole("button", { name: "Mark work done" })).toBeInTheDocument();
  });

  it("requires a confirm step before submitting Mark work done, even though the server's requiresConfirmation is false (regression, 2026-08-25)", async () => {
    // Mark work done predates and is independent of PrimaryActionMetadata.RequiresConfirmation —
    // it must always confirm before submitting, matching the app's existing convention. RD-058B-2:
    // the confirm step is a focused dialog, never an inline row that expands the Anchor.
    const detail: KeepRequestDetailResult = { ...baseDetail(), attentionLevel: "none" };
    renderAnchor(detail);

    await userEvent.setup().click(screen.getByRole("button", { name: "Mark work done" }));
    const dialog = screen.getByRole("dialog");
    expect(within(dialog).getByRole("heading", { name: "Mark request as Work completed?" })).toBeInTheDocument();
    // The confirm dialog carries the full factual advisory (RD-058B-2) — for this no-attention
    // Anchor primary too, not only the demoted active-attention control.
    expect(
      within(dialog).getByText(
        "This marks the request as Work completed. It does not notify the customer, does not complete internal financial review, and leaves any active attention or open Actual Work draft unresolved.",
      ),
    ).toBeInTheDocument();
    expect(within(dialog).getByRole("button", { name: "Mark work done" })).toBeInTheDocument();
    expect(within(dialog).getByRole("button", { name: "Cancel" })).toBeInTheDocument();
  });

  it("carries no action at all while active attention exists — no primary slot, no Contact customer, no demoted Mark work done (RD-058B-2)", () => {
    // mock-req-002: effectiveAttention.level is active (guidanceKey "respond_to_customer"). The
    // Anchor must not render detail.availableActions.primaryAction at all in this state (Session
    // 0A attention/no-attention mount split, 2026-08-25). RD-058B-2 also removes the standalone
    // Contact customer action unconditionally and relocates the demoted Mark work done to the
    // Work Canvas after Actual Work — so the Anchor's Row 1 right side is empty during attention.
    const detail = mockRequestDetails["mock-req-002"];
    renderAnchor(detail);

    expect(screen.queryByRole("button", { name: "Respond to customer" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Contact customer" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /mark work done/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Close request" })).not.toBeInTheDocument();
  });

  it("shows Close as the primary action when resolved, attention-free, and authorized", () => {
    const detail: KeepRequestDetailResult = {
      ...baseDetail(),
      status: "resolved",
      attentionLevel: "none",
      availableActions: {
        ...OWNER_ACTIONS,
        primaryAction: { key: "close_request", label: "Close request", target: "mutation", requiresConfirmation: true, confirmationCopy: "Close this request?" },
      },
    };
    renderAnchor(detail);

    expect(screen.getByRole("button", { name: "Close request" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /mark work done/i })).not.toBeInTheDocument();
  });

  it("requires confirmation before closing, using the server-authored confirmation copy", async () => {
    const detail: KeepRequestDetailResult = {
      ...baseDetail(),
      status: "resolved",
      attentionLevel: "none",
      availableActions: {
        ...OWNER_ACTIONS,
        primaryAction: { key: "close_request", label: "Close request", target: "mutation", requiresConfirmation: true, confirmationCopy: "Close this request?" },
      },
    };
    renderAnchor(detail);

    await userEvent.setup().click(screen.getByRole("button", { name: "Close request" }));
    const dialog = screen.getByRole("dialog");
    expect(within(dialog).getByRole("heading", { name: "Close this request?" })).toBeInTheDocument();
    expect(within(dialog).getByRole("button", { name: "Close request" })).toBeInTheDocument();
    expect(within(dialog).getByRole("button", { name: "Cancel" })).toBeInTheDocument();
  });

  it("fails safely with factual unavailable feedback for an unrecognized target, never falling back to capability-flag inference", () => {
    const detail: KeepRequestDetailResult = {
      ...baseDetail(),
      availableActions: {
        ...OWNER_ACTIONS,
        // Malformed/future value outside the closed client vocabulary.
        primaryAction: {
          key: "close_request",
          label: "Close request",
          target: "unknown_future_target",
          requiresConfirmation: false,
          confirmationCopy: null,
        } as unknown as KeepRequestDetailResult["availableActions"]["primaryAction"],
      },
    };
    renderAnchor(detail);

    expect(screen.getByText("Primary action unavailable")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Close request" })).not.toBeInTheDocument();
  });

  it("Row 4 (locked correction, 2026-08-24): renders three persistently labeled controls in locked order — Internal priority, Planned work date, Set internal follow-up", () => {
    const detail: KeepRequestDetailResult = {
      ...baseDetail(),
      businessPriority: null,
      plannedForDate: null,
      followUpOnDate: null,
    };
    const { container } = renderAnchor(detail);

    const grid = container.querySelector(".mt-3.grid")!;
    expect(grid).not.toBeNull();
    // Only the three top-level field labels — not the nested date-editor popover's own labels.
    const labels = Array.from(grid.querySelectorAll(":scope > div > label")).map((el) => el.textContent);
    expect(labels).toEqual(["Internal priority", "Planned work date", "Set internal follow-up"]);

    // Not a passive metadata strip — no card chrome, one compact three-column row on desktop.
    expect(grid.className).toContain("sm:grid-cols-3");
  });

  it("Row 4: exact empty-state control copy — never 'Not planned' or 'No follow-up'", () => {
    const detail: KeepRequestDetailResult = {
      ...baseDetail(),
      businessPriority: null,
      plannedForDate: null,
      followUpOnDate: null,
    };
    renderAnchor(detail);

    expect(screen.getByRole("combobox", { name: "Internal priority" })).toBeInTheDocument();
    expect((screen.getByRole("combobox", { name: "Internal priority" }) as HTMLSelectElement).value).toBe("");
    expect(screen.getByText("Set planned work date…")).toBeInTheDocument();
    expect(screen.getByText("Set internal follow-up…")).toBeInTheDocument();
    expect(screen.queryByText("Not planned")).not.toBeInTheDocument();
    expect(screen.queryByText("No follow-up")).not.toBeInTheDocument();
  });

  it("Row 4: renders the formatted date when planned/follow-up are set (authorized interaction path)", () => {
    const detail: KeepRequestDetailResult = {
      ...baseDetail(),
      businessPriority: "urgent",
      plannedForDate: "2026-08-29",
      followUpOnDate: "2026-08-26",
      followUpOnReason: "reminder",
    };
    const { container } = renderAnchor(detail);

    const prioritySelect = screen.getByRole("combobox", { name: "Internal priority" });
    expect((prioritySelect as HTMLSelectElement).value).toBe("urgent");
    expect(prioritySelect.className).toContain("text-[var(--ophalo-danger)]");

    expect(screen.getByRole("button", { name: "Planned work date: Aug 29, 2026" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Set internal follow-up: Aug 26, 2026" })).toBeInTheDocument();
    expect(screen.queryByText("Set planned work date…")).not.toBeInTheDocument();
    expect(screen.queryByText("Set internal follow-up…")).not.toBeInTheDocument();

    // Still a single outer Anchor card — Row 4 adds a top separator, not a nested bordered box.
    expect(container.querySelectorAll(".rounded-xl.border").length).toBe(1);
  });

  it("Row 4 correction (locked 2026-08-24): a set planned/follow-up date stays visible as a read-only labeled value even when the viewer lacks the edit permission — never hidden", () => {
    const detail: KeepRequestDetailResult = {
      ...baseDetail(),
      plannedForDate: "2026-08-29",
      followUpOnDate: "2026-08-26",
      followUpOnReason: "reminder",
      availableActions: { ...baseDetail().availableActions, canSetPlannedFor: false, canSetFollowUpOn: false },
    };
    renderAnchor(detail);

    expect(screen.getByText("Planned work date")).toBeInTheDocument();
    expect(screen.getByText("Set internal follow-up")).toBeInTheDocument();
    expect(screen.getByText("Aug 29, 2026")).toBeInTheDocument();
    expect(screen.getByText("Aug 26, 2026")).toBeInTheDocument();
    // Read-only: not an interactive trigger.
    expect(screen.queryByRole("button", { name: /Aug 29, 2026/ })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Aug 26, 2026/ })).not.toBeInTheDocument();
  });

  it("Row 4: omits an unauthorized, unset planned/follow-up field rather than rendering a dead control", () => {
    const detail: KeepRequestDetailResult = {
      ...baseDetail(),
      plannedForDate: null,
      followUpOnDate: null,
      availableActions: { ...baseDetail().availableActions, canSetPlannedFor: false, canSetFollowUpOn: false },
    };
    const { container } = renderAnchor(detail);

    expect(screen.queryByText("Planned work date")).not.toBeInTheDocument();
    expect(screen.queryByText("Set internal follow-up")).not.toBeInTheDocument();
    // Priority always renders (Routine is a real value, not an unset state).
    expect(screen.getByText("Internal priority")).toBeInTheDocument();
    const grid = container.querySelector(".mt-3.grid")!;
    expect(grid.children.length).toBe(1);
  });

  it("renders no primary/Log-contact controls for a read-only/unauthorized viewer", () => {
    const detail: KeepRequestDetailResult = {
      ...baseDetail(),
      attentionLevel: "none",
      availableActions: {
        ...OWNER_ACTIONS,
        canChangeStatus: false,
        canClose: false,
        canLogExternalContact: false,
        canAssignResponsible: false,
        canAddInternalNote: false,
      },
    };
    renderAnchor(detail);

    expect(screen.queryByRole("button", { name: "Contact customer" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /mark work done/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Close request" })).not.toBeInTheDocument();
    // Factual context remains visible even when no mutation is authorized
    expect(screen.getByText("Customer contact")).toBeInTheDocument();
    expect(screen.getByText("Service location")).toBeInTheDocument();
  });

  // Desktop closeout (2026-08-25): one-click Watch/Watching toggle + Watchers·N disclosure
  // promoted into the Owner & team column.
  it("shows a one-click Watch toggle and a Watchers·N disclosure trigger in the Owner & team column", () => {
    const detail = baseDetail(); // OWNER_ACTIONS: canWatch true, canUnwatch false; 0 watchers
    renderAnchor(detail);

    expect(screen.getByRole("button", { name: "Watch" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Watchers · 0" })).toBeInTheDocument();
  });

  it("shows 'Watching' (pressed) instead of 'Watch' once the viewer is already watching", () => {
    const detail: KeepRequestDetailResult = {
      ...baseDetail(),
      availableActions: { ...OWNER_ACTIONS, canWatch: false, canUnwatch: true },
    };
    renderAnchor(detail);

    const button = screen.getByRole("button", { name: "Watching" });
    expect(button).toHaveAttribute("aria-pressed", "true");
    expect(screen.queryByRole("button", { name: "Watch" })).not.toBeInTheDocument();
  });

  it("invokes onOpenWatchers when the Watchers·N trigger is clicked", async () => {
    const detail = baseDetail();
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const onOpenWatchers = vi.fn();
    render(
      <QueryClientProvider client={queryClient}>
        <RequestDetailAnchor
          requestId="req-1"
          detail={detail}
          highlights={{}}
          showProminentFeedbackCard={false}
          onDetailUpdated={vi.fn()}
          onContactLaunched={vi.fn()}
          onEditLocation={vi.fn()}
          onOpenReassignOwner={vi.fn()}
          onOpenWatchers={onOpenWatchers}
          onRecordFollowUp={vi.fn()}
          onCreateFollowUp={vi.fn()}
          onReviewSuccess={vi.fn()}
          canRecordShareIntent={false}
          needsShare={false}
          onOpenShareDrawer={vi.fn()}
          onOpenClearAttention={vi.fn()}
          onActivateCustomerUpdateComposer={vi.fn()}
        />
      </QueryClientProvider>,
    );

    await userEvent.setup().click(screen.getByRole("button", { name: "Watchers · 0" }));
    expect(onOpenWatchers).toHaveBeenCalledTimes(1);
  });
});
