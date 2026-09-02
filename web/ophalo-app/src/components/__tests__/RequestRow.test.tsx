import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { RequestRow, buildCollapsedSummary } from "../RequestRow";
import type { KeepRequestSummary, KeepQuickAction } from "../../lib/apiClient";

// GAP-027 / Build 087 §3-§5: one status pill, one deterministically-selected exception pill,
// at most two quick actions (promoted + secondary), no redundant bottom "Open detail" action.

function quickAction(code: string, executionMode: KeepQuickAction["executionMode"] = "modal"): KeepQuickAction {
  return {
    code,
    label: code,
    visibility: "internal",
    requiresVersion: true,
    executionMode,
    clearsAttention: false,
    countsFirstResponse: false,
    changesStatus: false,
    effectSummaryCode: "noop",
  };
}

function buildRow(overrides: Partial<KeepRequestSummary> = {}): KeepRequestSummary {
  return {
    id: "req-1",
    referenceCode: "REQ-001",
    status: "received",
    currentStatusText: null,
    customerName: "Jane Smith",
    customerPhone: "0412345678",
    customerEmail: "jane@example.com",
    lastCustomerActivityAtUtc: null,
    lastBusinessActivityAtUtc: null,
    createdAtUtc: "2026-07-01T00:00:00Z",
    updatedAtUtc: "2026-07-01T00:00:00Z",
    version: "v1",
    isTerminal: false,
    isPostCloseFollowUp: false,
    needsShare: false,
    source: "public_intake",
    intakeUrgency: "routine",
    businessPriority: null,
    contactPreference: "no_preference",
    serviceAddressLine1: null,
    serviceAddressLine2: null,
    serviceCity: null,
    serviceState: null,
    serviceZip: null,
    feedbackWasResolved: null,
    feedbackReviewAgeBucket: null,
    feedbackReviewDueAtUtc: null,
    rowContext: "active_work",
    ranking: {
      rankingGroup: "active",
      rankingOrder: 9,
      rankingReason: "active",
      severity: "muted",
      isOverdue: false,
      elapsedSinceUtc: null,
      dueAtUtc: null,
      isPostClose: false,
    },
    attention: {
      attentionLevel: "none",
      waitingDirection: "none",
      attentionReason: null,
      priorityBand: "standard",
      attentionSinceUtc: null,
      nextAttentionAtUtc: null,
      firstResponseDueAtUtc: null,
      firstRespondedAtUtc: null,
      firstResponsePending: false,
      firstResponseOverdue: false,
    },
    originalSummary: { fullText: "Fix leak" },
    latestActivity: null,
    hasInternalNote: false,
    pendingFinancialReviewCount: 0,
    participation: {
      responsibleCount: 0,
      watchingCount: 0,
      hasResponsible: false,
      isUnassigned: true,
      currentUserParticipationType: "none",
      responsibleDisplayName: null,
    },
    actions: { quickActions: [quickAction("open_detail", "detail")] },
    timing: undefined,
    ...overrides,
  };
}

const noop = () => {};

describe("RequestRow — Build 087 / GAP-027 locked row contract", () => {
  it("received row with overdue first response shows a merged Response overdue exception and promotes direct customer contact", () => {
    const row = buildRow({
      status: "received",
      ranking: { rankingGroup: "overdue_business_waiting", rankingOrder: 1, rankingReason: "overdue_business_waiting", severity: "danger", isOverdue: true, elapsedSinceUtc: null, dueAtUtc: "2026-07-13T12:00:00Z", isPostClose: false },
      attention: { attentionLevel: "none", waitingDirection: "none", attentionReason: null, priorityBand: "standard", attentionSinceUtc: null, nextAttentionAtUtc: null, firstResponseDueAtUtc: "2026-07-13T12:00:00Z", firstRespondedAtUtc: null, firstResponsePending: false, firstResponseOverdue: true },
      actions: { quickActions: [quickAction("open_detail", "detail"), quickAction("post_customer_update"), quickAction("contact_customer")] },
    });

    render(<RequestRow row={row} onSelect={noop} />);

    expect(screen.getByText(/Response overdue · Jul 13/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Contact customer" })).toBeInTheDocument();
    expect(screen.queryByText("Open detail")).not.toBeInTheDocument();
  });

  it("promotes Log contact for an overdue first response regardless of the saved preference", () => {
    const row = buildRow({
      status: "received",
      contactPreference: "phone_call",
      ranking: { rankingGroup: "overdue_business_waiting", rankingOrder: 1, rankingReason: "overdue_business_waiting", severity: "danger", isOverdue: true, elapsedSinceUtc: null, dueAtUtc: null, isPostClose: false },
      attention: { attentionLevel: "none", waitingDirection: "none", attentionReason: null, priorityBand: "standard", attentionSinceUtc: null, nextAttentionAtUtc: null, firstResponseDueAtUtc: "2026-07-13T12:00:00Z", firstRespondedAtUtc: null, firstResponsePending: false, firstResponseOverdue: true },
      actions: { quickActions: [quickAction("open_detail", "detail"), quickAction("post_customer_update"), quickAction("contact_customer")] },
    });

    render(<RequestRow row={row} onSelect={noop} />);

    expect(screen.getByText("Next: Contact customer")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Contact customer" })).toBeInTheDocument();
  });

  it("active row with complaint attention promotes Review request, not an ambiguous choice", () => {
    const row = buildRow({
      status: "in_progress",
      ranking: { rankingGroup: "priority_business_waiting", rankingOrder: 2, rankingReason: "priority_business_waiting", severity: "priority", isOverdue: false, elapsedSinceUtc: null, dueAtUtc: "2026-07-20T12:00:00Z", isPostClose: false },
      attention: { attentionLevel: "needs_attention", waitingDirection: "business", attentionReason: "complaint", priorityBand: "priority", attentionSinceUtc: null, nextAttentionAtUtc: "2026-07-20T12:00:00Z", firstResponseDueAtUtc: null, firstRespondedAtUtc: "2026-07-01T00:00:00Z", firstResponsePending: false, firstResponseOverdue: false },
      actions: { quickActions: [quickAction("open_detail", "detail"), quickAction("acknowledge_attention"), quickAction("post_customer_update")] },
    });

    render(<RequestRow row={row} onSelect={noop} />);

    expect(screen.getByText("Complaint")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Review request" })).toBeInTheDocument();
  });

  it("waiting-on-customer row with no active attention shows only its status pill and no forced action", () => {
    const row = buildRow({ status: "pending_customer" });

    render(<RequestRow row={row} onSelect={noop} />);

    expect(screen.getByText("Pending Customer")).toBeInTheDocument();
    expect(screen.queryByText(/^Next:/)).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Post customer-page update|Log contact|Review request/ })).not.toBeInTheDocument();
  });

  it("GAP-007: routine row with no promoted Next: cue still shows direct customer contact before the customer-page update", () => {
    const row = buildRow({
      status: "in_progress",
      actions: { quickActions: [quickAction("open_detail", "detail"), quickAction("post_customer_update"), quickAction("contact_customer")] },
    });

    render(<RequestRow row={row} onSelect={noop} />);

    expect(screen.queryByText(/^Next:/)).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Contact customer" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Post customer-page update" })).toBeInTheDocument();
  });

  it("GAP-007: routine row exposes only the single server-eligible action it has, without inventing the other", () => {
    const row = buildRow({
      status: "in_progress",
      actions: { quickActions: [quickAction("open_detail", "detail"), quickAction("contact_customer")] },
    });

    render(<RequestRow row={row} onSelect={noop} />);

    expect(screen.getByRole("button", { name: "Contact customer" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Post customer-page update" })).not.toBeInTheDocument();
  });

  it("work-completed (Resolved) row still shows an overdue follow-up alarm — Resolved is not terminal", () => {
    const row = buildRow({
      status: "resolved",
      ranking: { rankingGroup: "due_follow_up_on", rankingOrder: 5, rankingReason: "due_follow_up_on", severity: "attention", isOverdue: false, elapsedSinceUtc: null, dueAtUtc: null, isPostClose: false },
      timing: { followUpOnDate: "2026-07-01", followUpOnReason: "check_in", followUpOnNote: null, followUpOnLabel: "Check in", hasFutureFollowUpOn: false, plannedForDate: null, plannedForLabel: null, hasFuturePlannedFor: false },
      actions: { quickActions: [quickAction("open_detail", "detail"), quickAction("acknowledge_attention")] },
    });

    render(<RequestRow row={row} onSelect={noop} />);

    expect(screen.getByText(/Follow-up overdue/)).toBeInTheDocument();
  });

  it("Closed row suppresses a stale overdue follow-up alarm and response-overdue badge", () => {
    const row = buildRow({
      status: "closed",
      isTerminal: true,
      ranking: { rankingGroup: "closed", rankingOrder: 9, rankingReason: "closed", severity: "muted", isOverdue: true, elapsedSinceUtc: null, dueAtUtc: null, isPostClose: false },
      timing: { followUpOnDate: "2026-06-01", followUpOnReason: "check_in", followUpOnNote: null, followUpOnLabel: "Check in", hasFutureFollowUpOn: false, plannedForDate: null, plannedForLabel: null, hasFuturePlannedFor: false },
      actions: { quickActions: [quickAction("open_detail", "detail")] },
    });

    render(<RequestRow row={row} onSelect={noop} />);

    expect(screen.queryByText(/Response overdue/)).not.toBeInTheDocument();
    expect(screen.queryByText(/Follow-up overdue/)).not.toBeInTheDocument();
    expect(screen.queryByText(/^Next:/)).not.toBeInTheDocument();
  });

  it("Closed row with unresolved negative feedback keeps the Feedback pending exception and Review feedback action", () => {
    const row = buildRow({
      status: "closed",
      isTerminal: true,
      isPostCloseFollowUp: true,
      feedbackWasResolved: false,
      ranking: { rankingGroup: "post_close_unresolved_feedback", rankingOrder: 1, rankingReason: "post_close_unresolved_feedback", severity: "danger", isOverdue: false, elapsedSinceUtc: null, dueAtUtc: null, isPostClose: true },
      actions: { quickActions: [quickAction("open_detail", "detail"), quickAction("review_feedback"), quickAction("contact_customer")] },
    });

    render(<RequestRow row={row} onSelect={noop} />);

    expect(screen.getByText("Feedback pending")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Review feedback" })).toBeInTheDocument();
  });

  it("calm Resolved row on the Ready to Close tab shows Ready for closeout and promotes Close request", () => {
    const row = buildRow({
      status: "resolved",
      actions: { quickActions: [quickAction("open_detail", "detail"), quickAction("close_request"), quickAction("post_customer_update")] },
    });

    render(<RequestRow row={row} onSelect={noop} showCloseoutCue />);

    expect(screen.getByText("Ready for closeout")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Close request" })).toBeInTheDocument();
  });

  it("renders at most two quick action buttons even when three permitted actions exist", () => {
    const row = buildRow({
      status: "in_progress",
      attention: { attentionLevel: "waiting", waitingDirection: "customer", attentionReason: "customer_message", priorityBand: "standard", attentionSinceUtc: null, nextAttentionAtUtc: null, firstResponseDueAtUtc: null, firstRespondedAtUtc: "2026-07-01T00:00:00Z", firstResponsePending: false, firstResponseOverdue: false },
      actions: { quickActions: [quickAction("open_detail", "detail"), quickAction("post_customer_update"), quickAction("contact_customer"), quickAction("add_internal_note")] },
    });

    const { container } = render(<RequestRow row={row} onSelect={noop} />);
    const actionBar = container.querySelector(".border-t");
    expect(actionBar?.querySelectorAll("button").length).toBe(2);
  });

  it("colors action buttons by brand role, not amber — Log contact teal, customer-page update navy-outline", () => {
    // Button Hierarchy Is Locked (ux-design-decisions.md): amber is a status color, never a
    // button treatment; communication actions are Keep teal, secondary operator actions are
    // navy outline.
    const row = buildRow({
      status: "in_progress",
      attention: { attentionLevel: "waiting", waitingDirection: "customer", attentionReason: "customer_message", priorityBand: "standard", attentionSinceUtc: null, nextAttentionAtUtc: null, firstResponseDueAtUtc: null, firstRespondedAtUtc: "2026-07-01T00:00:00Z", firstResponsePending: false, firstResponseOverdue: false },
      actions: { quickActions: [quickAction("open_detail", "detail"), quickAction("post_customer_update"), quickAction("contact_customer")] },
    });

    render(<RequestRow row={row} onSelect={noop} />);

    const contactButton = screen.getByRole("button", { name: "Contact customer" });
    expect(contactButton.className).toContain("bg-[var(--keep-accent)]");
    expect(contactButton.className).not.toMatch(/attention/);

    const updateButton = screen.getByRole("button", { name: "Post customer-page update" });
    expect(updateButton.className).toContain("border-[var(--ophalo-navy)]");
    expect(updateButton.className).not.toMatch(/attention/);
  });

  it("promotes Share Link when the customer page is unshared and no higher-priority state exists", () => {
    const row = buildRow({
      status: "received",
      needsShare: true,
    });
    const onShareClick = vi.fn();

    render(<RequestRow row={row} onSelect={noop} onShareClick={onShareClick} />);

    expect(screen.getByText("Customer page not shared")).toBeInTheDocument();
    const shareButton = screen.getByRole("button", { name: "Share Link" });
    shareButton.click();
    expect(onShareClick).toHaveBeenCalledWith(row);
  });

  it("shows Follow-up overdue (not Customer page not shared) and promotes Review request, which navigates to detail — 'kelley 3'", () => {
    const row = buildRow({
      status: "received",
      needsShare: true,
      ranking: { rankingGroup: "due_follow_up_on", rankingOrder: 5, rankingReason: "due_follow_up_on", severity: "attention", isOverdue: false, elapsedSinceUtc: null, dueAtUtc: null, isPostClose: false },
      timing: { followUpOnDate: "2026-07-12", followUpOnReason: "check_in", followUpOnNote: null, followUpOnLabel: "Check in", hasFutureFollowUpOn: false, plannedForDate: null, plannedForLabel: null, hasFuturePlannedFor: false },
      actions: { quickActions: [quickAction("open_detail", "detail")] },
    });
    const onSelect = vi.fn();

    render(<RequestRow row={row} onSelect={onSelect} />);

    expect(screen.getByText(/Follow-up overdue · Jul 12/)).toBeInTheDocument();
    expect(screen.queryByText("Customer page not shared")).not.toBeInTheDocument();

    const reviewButton = screen.getByRole("button", { name: "Review request" });
    reviewButton.click();
    expect(onSelect).toHaveBeenCalledWith(row.id);
  });

  it("shows a single Follow up today phrase (no duplicated copy) and promotes Review request over Share Link — 'customer test3'", () => {
    const now = new Date();
    const todayIso = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, "0")}-${String(now.getDate()).padStart(2, "0")}`;
    const row = buildRow({
      status: "received",
      needsShare: true,
      ranking: { rankingGroup: "due_follow_up_on", rankingOrder: 5, rankingReason: "due_follow_up_on", severity: "attention", isOverdue: false, elapsedSinceUtc: null, dueAtUtc: null, isPostClose: false },
      timing: { followUpOnDate: todayIso, followUpOnReason: "check_in", followUpOnNote: null, followUpOnLabel: "Follow up today", hasFutureFollowUpOn: false, plannedForDate: "2026-07-24", plannedForLabel: "Planned Fri", hasFuturePlannedFor: true },
      actions: { quickActions: [quickAction("open_detail", "detail")] },
    });
    const onSelect = vi.fn();

    render(<RequestRow row={row} onSelect={onSelect} />);

    const dateLabel = new Date(now.getFullYear(), now.getMonth(), now.getDate())
      .toLocaleDateString("en-US", { month: "short", day: "numeric" });
    expect(screen.getByText(`Follow up today · ${dateLabel}`)).toBeInTheDocument();
    expect(screen.queryByText(/Follow-up due today/)).not.toBeInTheDocument();
    expect(screen.queryByText("Customer page not shared")).not.toBeInTheDocument();

    expect(screen.getByRole("button", { name: "Review request" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Share Link" })).not.toBeInTheDocument();
  });

  it("GAP-007b: labels a customer message with source and relative time", () => {
    const row = buildRow({
      latestActivity: {
        previewText: "Can you come Tuesday?",
        previewSource: "customer_message",
        previewTruncated: false,
        previewAtUtc: new Date(Date.now() - 60 * 60_000).toISOString(),
      },
    });

    render(<RequestRow row={row} onSelect={noop} />);

    expect(screen.getByText(/Customer message · 1h ago ·/)).toBeInTheDocument();
    expect(screen.getByText("Can you come Tuesday?")).toBeInTheDocument();
  });

  it("GAP-007b: labels a business update with source and relative time", () => {
    const row = buildRow({
      latestActivity: {
        previewText: "We'll be there Thursday.",
        previewSource: "business_update",
        previewTruncated: false,
        previewAtUtc: new Date(Date.now() - 2 * 60 * 60_000).toISOString(),
      },
    });

    render(<RequestRow row={row} onSelect={noop} />);

    expect(screen.getByText(/Business update · 2h ago ·/)).toBeInTheDocument();
    expect(screen.getByText("We'll be there Thursday.")).toBeInTheDocument();
  });

  it("GAP-007b: external-contact preview shows only a relative time, no source label — the label text is already neutral", () => {
    const row = buildRow({
      latestActivity: {
        previewText: "Called customer",
        previewSource: "external_contact",
        previewTruncated: false,
        previewAtUtc: new Date(Date.now() - 2 * 60 * 60_000).toISOString(),
      },
    });

    render(<RequestRow row={row} onSelect={noop} />);

    expect(screen.getByText(/^2h ago ·/)).toBeInTheDocument();
    expect(screen.getByText("Called customer")).toBeInTheDocument();
    expect(screen.queryByText(/Customer message/)).not.toBeInTheDocument();
    expect(screen.queryByText(/Business update/)).not.toBeInTheDocument();
  });

  // --- ADR-450: original-summary context, expansion toggle, internal-note cue ---

  it("ADR-450: renders the original summary as stable context and shows no latest-activity block when latestActivity is null", () => {
    const row = buildRow({ latestActivity: null });

    render(<RequestRow row={row} onSelect={noop} />);

    expect(screen.getByText("Fix leak")).toBeInTheDocument();
    expect(screen.queryByText(/ago ·/)).not.toBeInTheDocument();
  });

  it("ADR-450: shows Read full request only when the collapsed form differs from the full text, and expands without navigating", () => {
    const longText = "A".repeat(300);
    const row = buildRow({ originalSummary: { fullText: longText } });
    const onSelect = vi.fn();

    render(<RequestRow row={row} onSelect={onSelect} />);

    const toggle = screen.getByRole("button", { name: "Read full request" });
    expect(toggle).toHaveAttribute("aria-expanded", "false");

    fireEvent.click(toggle);

    expect(onSelect).not.toHaveBeenCalled();
    expect(screen.getByRole("button", { name: "Show less" })).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByText(longText)).toBeInTheDocument();
  });

  it("ADR-450: no expansion toggle for short original summaries", () => {
    const row = buildRow({ originalSummary: { fullText: "Short and simple request" } });

    render(<RequestRow row={row} onSelect={noop} />);

    expect(screen.queryByRole("button", { name: "Read full request" })).not.toBeInTheDocument();
  });

  it("ADR-450: shows a quiet Internal note cue only when hasInternalNote is true", () => {
    const withNote = buildRow({ hasInternalNote: true });
    const { rerender } = render(<RequestRow row={withNote} onSelect={noop} />);
    expect(screen.getByText("Internal note")).toBeInTheDocument();

    const withoutNote = buildRow({ hasInternalNote: false });
    rerender(<RequestRow row={withoutNote} onSelect={noop} />);
    expect(screen.queryByText("Internal note")).not.toBeInTheDocument();
  });

  it("BL138 Slice 3b: shows a quiet financial-review cue only when the server sent a non-zero count, with factual pluralization", () => {
    const none = buildRow({ pendingFinancialReviewCount: 0 });
    const { rerender } = render(<RequestRow row={none} onSelect={noop} />);
    expect(screen.queryByText(/needs? financial review/)).not.toBeInTheDocument();

    rerender(<RequestRow row={buildRow({ pendingFinancialReviewCount: 1 })} onSelect={noop} />);
    expect(screen.getByText("1 visit needs financial review")).toBeInTheDocument();

    rerender(<RequestRow row={buildRow({ pendingFinancialReviewCount: 3 })} onSelect={noop} />);
    expect(screen.getByText("3 visits need financial review")).toBeInTheDocument();
  });

  it("BL138 Slice 3b: the financial-review cue is non-interactive — no link, button, or nested activation target", () => {
    render(<RequestRow row={buildRow({ pendingFinancialReviewCount: 2 })} onSelect={noop} />);
    const cue = screen.getByText("2 visits need financial review");
    expect(cue.closest("a")).toBeNull();
    expect(cue.closest("button")).toBeNull();
    expect(cue).not.toHaveAttribute("role", "button");
  });

  it("BL138 Slice 3b: the financial-review cue is default-row only — the compact pane row omits it", () => {
    render(
      <RequestRow row={buildRow({ pendingFinancialReviewCount: 2 })} onSelect={noop} paneMode />,
    );
    expect(screen.queryByText("2 visits need financial review")).not.toBeInTheDocument();
  });

  it("ADR-450: keyboard activation (Enter) of the real toggle expands without navigating — no interactive ancestor to intercept it", async () => {
    const user = userEvent.setup();
    const longText = "B".repeat(300);
    const row = buildRow({ originalSummary: { fullText: longText } });
    const onSelect = vi.fn();

    render(<RequestRow row={row} onSelect={onSelect} />);

    // user-event simulates real browser keyboard-activation semantics (Tab focuses the button,
    // Enter fires its native click), unlike a raw fireEvent.keyDown which jsdom does not
    // translate into a click by itself. Default buildRow renders no quick-action bar, so the
    // only two focusable elements are the nav button, then the toggle.
    await user.tab(); // focuses the row-navigation button
    await user.tab(); // focuses the Read full request toggle
    expect(screen.getByRole("button", { name: "Read full request" })).toHaveFocus();
    await user.keyboard("{Enter}");

    expect(onSelect).not.toHaveBeenCalled();
    expect(screen.getByRole("button", { name: "Show less" })).toHaveAttribute("aria-expanded", "true");
  });

  it("ADR-450: expansion state resets when the row remounts under a different composite key (tab/filter/search/page change)", () => {
    const longText = "C".repeat(300);
    const row = buildRow({ id: "req-reset", originalSummary: { fullText: longText } });

    const { rerender } = render(<RequestRow key="req-reset-tab-a" row={row} onSelect={noop} />);
    fireEvent.click(screen.getByRole("button", { name: "Read full request" }));
    expect(screen.getByRole("button", { name: "Show less" })).toBeInTheDocument();

    // Simulate Requests.tsx's composite key changing (e.g. a tab/filter/search/cursor change)
    // for the same underlying request — a key change forces React to unmount and remount, not
    // just re-render with new props, exactly as key={`${row.id}-${activeTab.view}-...`} would.
    rerender(<RequestRow key="req-reset-tab-b" row={row} onSelect={noop} />);

    expect(screen.getByRole("button", { name: "Read full request" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Show less" })).not.toBeInTheDocument();
  });

  it("build-log 134 §4: paneMode hides the quick-action footer but keeps the row selectable with its status/exception metadata", () => {
    const row = buildRow({
      status: "received",
      ranking: { rankingGroup: "overdue_business_waiting", rankingOrder: 1, rankingReason: "overdue_business_waiting", severity: "danger", isOverdue: true, elapsedSinceUtc: null, dueAtUtc: "2026-07-13T12:00:00Z", isPostClose: false },
      attention: { attentionLevel: "none", waitingDirection: "none", attentionReason: null, priorityBand: "standard", attentionSinceUtc: null, nextAttentionAtUtc: null, firstResponseDueAtUtc: "2026-07-13T12:00:00Z", firstRespondedAtUtc: null, firstResponsePending: false, firstResponseOverdue: true },
      actions: { quickActions: [quickAction("open_detail", "detail"), quickAction("post_customer_update"), quickAction("contact_customer")] },
    });
    const onSelect = vi.fn();

    render(<RequestRow row={row} onSelect={onSelect} paneMode />);

    expect(screen.queryByRole("button", { name: "Post customer-page update" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Contact customer" })).not.toBeInTheDocument();
    expect(screen.getByText(/Response overdue · Jul 13/)).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: /Jane Smith/ }));
    expect(onSelect).toHaveBeenCalledWith("req-1");
  });

  it("action-first queue redesign (locked 2026-08-24): paneMode renders a compact scan-and-select row — identity, status/exception, Next: cue, and the same conditional action-signal line as the default row, never city/state", () => {
    const row = buildRow({
      status: "received",
      originalSummary: { fullText: "Fix leak" },
      latestActivity: { previewText: "Called customer back", previewAtUtc: "2026-08-20T12:00:00Z", previewSource: "note", previewTruncated: false },
      hasInternalNote: true,
      businessPriority: "urgent",
      contactPreference: "text_message",
      serviceCity: "Brighton",
      serviceState: "TN",
      serviceZip: "38011",
      timing: { followUpOnDate: null, followUpOnReason: null, followUpOnNote: null, followUpOnLabel: null, hasFutureFollowUpOn: false, plannedForDate: "2026-08-29T12:00:00Z", plannedForLabel: null, hasFuturePlannedFor: true },
      participation: {
        responsibleCount: 1,
        watchingCount: 0,
        hasResponsible: true,
        isUnassigned: false,
        currentUserParticipationType: "responsible",
        responsibleDisplayName: "Alex Rivera",
      },
      actions: { quickActions: [quickAction("open_detail", "detail"), quickAction("post_customer_update")] },
    });

    render(<RequestRow row={row} onSelect={noop} paneMode />);

    // Kept: identity, status/exception (Response overdue text also carries the Next: cue path
    // tested above), and the same capped action-signal line as the default row — never city/state.
    expect(screen.getByText("Urgent · Planned Aug 29 · Prefers text")).toBeInTheDocument();
    expect(screen.queryByText(/Brighton/)).not.toBeInTheDocument();
    expect(screen.queryByText(/38011/)).not.toBeInTheDocument();

    // Trimmed for the compact pane row.
    expect(screen.queryByText("Fix leak")).not.toBeInTheDocument();
    expect(screen.queryByText("Called customer back")).not.toBeInTheDocument();
    expect(screen.queryByText("Alex Rivera")).not.toBeInTheDocument();
    expect(screen.queryByText("Internal note")).not.toBeInTheDocument();
    expect(screen.queryByText("Internal priority: Urgent")).not.toBeInTheDocument();
    expect(screen.queryByText("Created by business")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Read full request" })).not.toBeInTheDocument();
  });

  it("action-first queue redesign: paneMode omits the action-signal line entirely when no eligible signal exists", () => {
    const row = buildRow({
      status: "received",
      businessPriority: "routine",
      contactPreference: "no_preference",
    });

    const { container } = render(<RequestRow row={row} onSelect={noop} paneMode />);

    expect(container.querySelector(".keep-row-meta")).toBeNull();
  });

  it("action-first queue redesign (locked 2026-08-24): default row combines priority, planned date, and contact preference into one capped signal line, never city/state", () => {
    const row = buildRow({
      businessPriority: "urgent",
      contactPreference: "text_message",
      serviceCity: "Brighton",
      serviceState: "TN",
      timing: { followUpOnDate: null, followUpOnReason: null, followUpOnNote: null, followUpOnLabel: null, hasFutureFollowUpOn: false, plannedForDate: "2026-08-29T12:00:00Z", plannedForLabel: null, hasFuturePlannedFor: true },
    });

    render(<RequestRow row={row} onSelect={noop} />);

    expect(screen.getByText("Urgent · Planned Aug 29 · Prefers text")).toBeInTheDocument();
    expect(screen.queryByText(/Brighton/)).not.toBeInTheDocument();
    expect(screen.queryByText("Internal priority: Urgent")).not.toBeInTheDocument();
  });

  it("action-first queue redesign: the signal line is unmounted (not a field showing defaults) when no eligible signal exists", () => {
    const row = buildRow({
      businessPriority: "routine",
      contactPreference: "no_preference",
    });

    render(<RequestRow row={row} onSelect={noop} />);

    expect(screen.queryByText(/No preference/)).not.toBeInTheDocument();
    expect(screen.queryByText(/Routine/)).not.toBeInTheDocument();
    expect(screen.queryByText(/Planned/)).not.toBeInTheDocument();
  });

  it("preserves the quick-action footer in the one-pane fallback when paneMode is false/omitted", () => {
    const row = buildRow({
      status: "received",
      ranking: { rankingGroup: "overdue_business_waiting", rankingOrder: 1, rankingReason: "overdue_business_waiting", severity: "danger", isOverdue: true, elapsedSinceUtc: null, dueAtUtc: "2026-07-13T12:00:00Z", isPostClose: false },
      attention: { attentionLevel: "none", waitingDirection: "none", attentionReason: null, priorityBand: "standard", attentionSinceUtc: null, nextAttentionAtUtc: null, firstResponseDueAtUtc: "2026-07-13T12:00:00Z", firstRespondedAtUtc: null, firstResponsePending: false, firstResponseOverdue: true },
      actions: { quickActions: [quickAction("open_detail", "detail"), quickAction("post_customer_update"), quickAction("contact_customer")] },
    });

    render(<RequestRow row={row} onSelect={noop} />);

    expect(screen.getByRole("button", { name: "Contact customer" })).toBeInTheDocument();
  });

  it("backlog item 2: the entire row is a single keyboard-accessible activation target", async () => {
    const user = userEvent.setup();
    const row = buildRow();
    const onSelect = vi.fn();

    render(<RequestRow row={row} onSelect={onSelect} />);

    const rowEl = screen.getByRole("button", { name: /Jane Smith/ });
    expect(rowEl).toHaveAttribute("tabIndex", "0");

    rowEl.focus();
    await user.keyboard("{Enter}");
    expect(onSelect).toHaveBeenCalledWith("req-1");

    onSelect.mockClear();
    await user.keyboard(" ");
    expect(onSelect).toHaveBeenCalledWith("req-1");
  });

  it("backlog item 2: nested interactive controls stay independently operable and never also activate the row", () => {
    const longText = "D".repeat(300);
    const row = buildRow({ originalSummary: { fullText: longText } });
    const onSelect = vi.fn();

    render(<RequestRow row={row} onSelect={onSelect} />);

    fireEvent.click(screen.getByRole("button", { name: "Read full request" }));
    expect(onSelect).not.toHaveBeenCalled();
    expect(screen.getByRole("button", { name: "Show less" })).toBeInTheDocument();
  });

  it("backlog item 2: selected prop marks the row aria-selected without disturbing the exception rail", () => {
    const row = buildRow({
      ranking: { rankingGroup: "overdue_business_waiting", rankingOrder: 1, rankingReason: "overdue_business_waiting", severity: "danger", isOverdue: true, elapsedSinceUtc: null, dueAtUtc: "2026-07-13T12:00:00Z", isPostClose: false },
      attention: { attentionLevel: "none", waitingDirection: "none", attentionReason: null, priorityBand: "standard", attentionSinceUtc: null, nextAttentionAtUtc: null, firstResponseDueAtUtc: "2026-07-13T12:00:00Z", firstRespondedAtUtc: null, firstResponsePending: false, firstResponseOverdue: true },
    });

    const { rerender } = render(<RequestRow row={row} onSelect={noop} />);
    const rowEl = screen.getByRole("button", { name: /Jane Smith/ });
    expect(rowEl).toHaveAttribute("aria-selected", "false");
    expect(rowEl.className).toContain("border-l-[var(--ophalo-danger)]");
    expect(rowEl.className).not.toContain("ring-inset");

    rerender(<RequestRow row={row} onSelect={noop} selected />);
    expect(rowEl).toHaveAttribute("aria-selected", "true");
    expect(rowEl.className).toContain("border-l-[var(--ophalo-danger)]");
    expect(rowEl.className).toContain("ring-inset");
  });

  // Q-027A: red is reserved for genuine overdue/high-risk work. Non-overdue priority/urgent
  // business-waiting (server severity "priority") renders amber, not red.
  it("Q-027A: non-overdue priority business-waiting renders an amber exception and rail, never red", () => {
    const row = buildRow({
      status: "in_progress",
      businessPriority: null,
      ranking: { rankingGroup: "priority_business_waiting", rankingOrder: 2, rankingReason: "priority_business_waiting", severity: "priority", isOverdue: false, elapsedSinceUtc: null, dueAtUtc: "2026-07-30T12:00:00Z", isPostClose: false },
      attention: { attentionLevel: "needs_attention", waitingDirection: "business", attentionReason: null, priorityBand: "priority", attentionSinceUtc: null, nextAttentionAtUtc: "2026-07-30T12:00:00Z", firstResponseDueAtUtc: null, firstRespondedAtUtc: "2026-07-01T00:00:00Z", firstResponsePending: false, firstResponseOverdue: false },
    });

    render(<RequestRow row={row} onSelect={noop} />);

    const rowEl = screen.getByRole("button", { name: /Jane Smith/ });
    expect(rowEl.className).toContain("border-l-[var(--ophalo-attention)]");
    expect(rowEl.className).not.toContain("border-l-[var(--ophalo-danger)]");
    const badge = screen.getByText("Needs response");
    expect(badge.className).toContain("var(--ophalo-attention)");
    expect(badge.className).not.toContain("var(--ophalo-danger)");
  });

  it("Q-027A: genuinely overdue business-waiting keeps the red exception and rail", () => {
    const row = buildRow({
      status: "in_progress",
      ranking: { rankingGroup: "overdue_business_waiting", rankingOrder: 1, rankingReason: "overdue_business_waiting", severity: "danger", isOverdue: true, elapsedSinceUtc: null, dueAtUtc: "2026-07-13T12:00:00Z", isPostClose: false },
      attention: { attentionLevel: "none", waitingDirection: "none", attentionReason: null, priorityBand: "standard", attentionSinceUtc: null, nextAttentionAtUtc: null, firstResponseDueAtUtc: "2026-07-13T12:00:00Z", firstRespondedAtUtc: null, firstResponsePending: false, firstResponseOverdue: true },
    });

    render(<RequestRow row={row} onSelect={noop} />);

    const rowEl = screen.getByRole("button", { name: /Jane Smith/ });
    expect(rowEl.className).toContain("border-l-[var(--ophalo-danger)]");
    const badge = screen.getByText(/Response overdue/);
    expect(badge.className).toContain("var(--ophalo-danger)");
  });

  it("Q-027A: a complaint (server severity danger) stays red as genuine high-risk work", () => {
    const row = buildRow({
      status: "in_progress",
      ranking: { rankingGroup: "priority_business_waiting", rankingOrder: 2, rankingReason: "priority_business_waiting", severity: "danger", isOverdue: false, elapsedSinceUtc: null, dueAtUtc: "2026-07-20T12:00:00Z", isPostClose: false },
      attention: { attentionLevel: "needs_attention", waitingDirection: "business", attentionReason: "complaint", priorityBand: "priority", attentionSinceUtc: null, nextAttentionAtUtc: "2026-07-20T12:00:00Z", firstResponseDueAtUtc: null, firstRespondedAtUtc: "2026-07-01T00:00:00Z", firstResponsePending: false, firstResponseOverdue: false },
      actions: { quickActions: [quickAction("open_detail", "detail"), quickAction("acknowledge_attention")] },
    });

    render(<RequestRow row={row} onSelect={noop} />);

    const rowEl = screen.getByRole("button", { name: /Jane Smith/ });
    expect(rowEl.className).toContain("border-l-[var(--ophalo-danger)]");
    const badge = screen.getByText("Complaint");
    expect(badge.className).toContain("var(--ophalo-danger)");
  });
});

describe("buildCollapsedSummary — ADR-450 word-boundary/whitespace collapse", () => {
  it("normalizes internal whitespace and newlines to single spaces", () => {
    const { collapsed, showToggle } = buildCollapsedSummary("Line one\n\nLine   two\ttabbed");
    expect(collapsed).toBe("Line one Line two tabbed");
    expect(showToggle).toBe(true); // collapsed differs from the raw (un-normalized) full text
  });

  it("leaves short, already-normalized text untouched and does not show the toggle", () => {
    const { collapsed, showToggle } = buildCollapsedSummary("Short and simple request");
    expect(collapsed).toBe("Short and simple request");
    expect(showToggle).toBe(false);
  });

  it("backs up to a word boundary instead of splitting a word mid-way", () => {
    const words = Array.from({ length: 50 }, (_, i) => `word${i}`).join(" "); // well over 240 chars
    const { collapsed, showToggle } = buildCollapsedSummary(words);

    expect(showToggle).toBe(true);
    expect(collapsed.endsWith("…")).toBe(true);
    const withoutEllipsis = collapsed.slice(0, -1);
    // Every character up to the cut must be a real prefix of the source text — i.e. the cut
    // landed on a space, not mid-word.
    expect(words.startsWith(withoutEllipsis)).toBe(true);
    expect(words[withoutEllipsis.length]).toBe(" ");
  });

  it("caps the collapsed length at exactly 240 characters including the ellipsis", () => {
    const singleWord = "A".repeat(300); // no spaces — cannot back up to a word boundary
    const { collapsed, showToggle } = buildCollapsedSummary(singleWord);

    expect(showToggle).toBe(true);
    expect(collapsed.length).toBe(240);
    expect(collapsed.endsWith("…")).toBe(true);
  });

  it("does not truncate text at exactly 240 characters", () => {
    const exactly240 = "D".repeat(240);
    const { collapsed, showToggle } = buildCollapsedSummary(exactly240);

    expect(collapsed).toBe(exactly240);
    expect(showToggle).toBe(false);
  });

  it("truncates text at 241 characters", () => {
    const over = "E".repeat(241);
    const { collapsed, showToggle } = buildCollapsedSummary(over);

    expect(showToggle).toBe(true);
    expect(collapsed.length).toBe(240);
  });
});
