import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { RequestRow } from "../RequestRow";
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
    description: "Fix leak",
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
    preview: { previewText: "Customer needs a leak fixed.", previewSource: "description", previewTruncated: false },
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
  it("received row with overdue first response shows a merged Response overdue exception and promotes Update customer", () => {
    const row = buildRow({
      status: "received",
      ranking: { rankingGroup: "overdue_business_waiting", rankingOrder: 1, rankingReason: "overdue_business_waiting", severity: "danger", isOverdue: true, elapsedSinceUtc: null, dueAtUtc: "2026-07-13T12:00:00Z", isPostClose: false },
      attention: { attentionLevel: "none", waitingDirection: "none", attentionReason: null, priorityBand: "standard", attentionSinceUtc: null, nextAttentionAtUtc: null, firstResponseDueAtUtc: "2026-07-13T12:00:00Z", firstRespondedAtUtc: null, firstResponsePending: false, firstResponseOverdue: true },
      actions: { quickActions: [quickAction("open_detail", "detail"), quickAction("post_customer_update"), quickAction("contact_customer")] },
    });

    render(<RequestRow row={row} onSelect={noop} />);

    expect(screen.getByText(/Response overdue · Jul 13/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Update customer" })).toBeInTheDocument();
    expect(screen.queryByText("Open detail")).not.toBeInTheDocument();
  });

  it("promotes Log contact instead of Update customer when the customer prefers a phone call", () => {
    const row = buildRow({
      status: "received",
      contactPreference: "phone_call",
      ranking: { rankingGroup: "overdue_business_waiting", rankingOrder: 1, rankingReason: "overdue_business_waiting", severity: "danger", isOverdue: true, elapsedSinceUtc: null, dueAtUtc: null, isPostClose: false },
      attention: { attentionLevel: "none", waitingDirection: "none", attentionReason: null, priorityBand: "standard", attentionSinceUtc: null, nextAttentionAtUtc: null, firstResponseDueAtUtc: "2026-07-13T12:00:00Z", firstRespondedAtUtc: null, firstResponsePending: false, firstResponseOverdue: true },
      actions: { quickActions: [quickAction("open_detail", "detail"), quickAction("post_customer_update"), quickAction("contact_customer")] },
    });

    render(<RequestRow row={row} onSelect={noop} />);

    // Build 087 §4 step 6: a phone-call preference promotes Log contact ahead of Update customer.
    expect(screen.getByText("Next: Log contact")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Log contact" })).toBeInTheDocument();
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
    expect(screen.queryByRole("button", { name: /Update customer|Log contact|Review request/ })).not.toBeInTheDocument();
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

  it("shows the overdue follow-up exception, not Customer page not shared, when both are true — canonical urgency wins", () => {
    const row = buildRow({
      status: "received",
      needsShare: true,
      ranking: { rankingGroup: "due_follow_up_on", rankingOrder: 5, rankingReason: "due_follow_up_on", severity: "attention", isOverdue: false, elapsedSinceUtc: null, dueAtUtc: null, isPostClose: false },
      timing: { followUpOnDate: "2026-07-12", followUpOnReason: "check_in", followUpOnNote: null, followUpOnLabel: "Check in", hasFutureFollowUpOn: false, plannedForDate: null, plannedForLabel: null, hasFuturePlannedFor: false },
    });

    render(<RequestRow row={row} onSelect={noop} />);

    expect(screen.getByText(/Follow-up overdue · Jul 12/)).toBeInTheDocument();
    expect(screen.queryByText("Customer page not shared")).not.toBeInTheDocument();
  });

  it("renders a follow-up due today as an attention-tone alert, not an overdue one", () => {
    const now = new Date();
    const todayIso = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, "0")}-${String(now.getDate()).padStart(2, "0")}`;
    const row = buildRow({
      status: "received",
      ranking: { rankingGroup: "due_follow_up_on", rankingOrder: 5, rankingReason: "due_follow_up_on", severity: "attention", isOverdue: false, elapsedSinceUtc: null, dueAtUtc: null, isPostClose: false },
      timing: { followUpOnDate: todayIso, followUpOnReason: "check_in", followUpOnNote: null, followUpOnLabel: "Check in", hasFutureFollowUpOn: false, plannedForDate: null, plannedForLabel: null, hasFuturePlannedFor: false },
    });

    render(<RequestRow row={row} onSelect={noop} />);

    expect(screen.getByText(/Follow-up due today/)).toBeInTheDocument();
    expect(screen.queryByText(/Follow-up overdue/)).not.toBeInTheDocument();
  });
});
