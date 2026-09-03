import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { RequestDetailAnchor } from "../RequestDetailAnchor";
import { mockRequestDetails, OWNER_ACTIONS } from "../../../mocks/fixtures";
import type { KeepRequestDetailResult } from "../../../lib/apiClient";

beforeEach(() => vi.restoreAllMocks());

function baseDetail(): KeepRequestDetailResult {
  return {
    ...mockRequestDetails["mock-req-001"],
    effectiveAttention: { ...mockRequestDetails["mock-req-001"].effectiveAttention },
  };
}

function renderAnchor(
  detail: KeepRequestDetailResult = baseDetail(),
  overrides: Partial<React.ComponentProps<typeof RequestDetailAnchor>> = {},
) {
  const callbacks = {
    onContactLaunched: vi.fn(),
    onOpenShareDrawer: vi.fn(),
    onActualWork: vi.fn(),
    onFinancialReview: vi.fn(),
  };
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const result = render(
    <QueryClientProvider client={queryClient}>
      <RequestDetailAnchor
        requestId="req-1"
        detail={detail}
        highlights={{}}
        showProminentFeedbackCard={false}
        onDetailUpdated={vi.fn()}
        onContactLaunched={callbacks.onContactLaunched}
        onEditLocation={vi.fn()}
        onOpenReassignOwner={vi.fn()}
        onOpenWatchers={vi.fn()}
        onRecordFollowUp={vi.fn()}
        onCreateFollowUp={vi.fn()}
        onReviewSuccess={vi.fn()}
        canRecordShareIntent
        needsShare
        onOpenShareDrawer={callbacks.onOpenShareDrawer}
        onOpenClearAttention={vi.fn()}
        onActivateCustomerUpdateComposer={vi.fn()}
        actualWorkShortcut={{ label: "Record Actual Work", onClick: callbacks.onActualWork }}
        financialReviewShortcut={{ label: "Review financials (1)", onClick: callbacks.onFinancialReview, tone: "ready" }}
        businessPageUrl="https://example.test/keep/s/demo"
        {...overrides}
      />
    </QueryClientProvider>,
  );
  return { ...result, callbacks };
}

describe("RequestDetailAnchor — compact identity and frequent actions", () => {
  it("keeps identity and Customer Need in the sticky-strip payload without duplicating right-rail details", () => {
    const detail = baseDetail();
    const { container } = renderAnchor(detail);

    expect(screen.getByText(detail.referenceCode)).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: detail.customerName })).toBeInTheDocument();
    expect(screen.getByText("Customer need")).toBeInTheDocument();
    expect(screen.getByText(detail.description!)).toHaveClass("line-clamp-2");
    expect(screen.queryByText("Service location")).not.toBeInTheDocument();
    expect(screen.queryByText("Owner")).not.toBeInTheDocument();
    expect(container.querySelector("[aria-label='Frequent request actions']")?.className).toContain("max-h-[88px]");
  });

  it("exposes contact, channel, share, Actual Work, and financial-review actions", async () => {
    const user = userEvent.setup();
    const { callbacks } = renderAnchor({ ...baseDetail(), customerEmail: "marcus@example.test" });

    await user.click(screen.getByRole("button", { name: "Contact customer" }));
    await user.click(screen.getByRole("button", { name: "Call" }));
    await user.click(screen.getByRole("button", { name: "Text" }));
    await user.click(screen.getByRole("button", { name: "Email" }));
    await user.click(screen.getByRole("button", { name: /Customer request page/ }));
    await user.click(screen.getByRole("button", { name: "Record Actual Work" }));
    await user.click(screen.getByRole("button", { name: "Review financials (1)" }));

    expect(callbacks.onContactLaunched.mock.calls).toEqual([
      ["outbound", "phone"],
      ["outbound", "phone"],
      ["outbound", "sms"],
      ["outbound", "email"],
    ]);
    expect(callbacks.onOpenShareDrawer).toHaveBeenCalledOnce();
    expect(callbacks.onActualWork).toHaveBeenCalledOnce();
    expect(callbacks.onFinancialReview).toHaveBeenCalledOnce();
    expect(screen.getByRole("group", { name: "Customer contact actions" })).toBeInTheDocument();
    expect(screen.getByRole("group", { name: "Share pages" })).toBeInTheDocument();
    expect(screen.getByRole("group", { name: "Work and financial actions" })).toHaveClass("ml-auto");
  });

  it("shares the business page through the native share contract when available", async () => {
    const share = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, "share", { configurable: true, value: share });
    const detail = baseDetail();
    renderAnchor(detail);

    await userEvent.setup().click(screen.getByRole("button", { name: "Business page" }));
    expect(share).toHaveBeenCalledWith({ title: detail.businessName, url: "https://example.test/keep/s/demo" });
  });

  it("keeps operational shortcuts available during active attention while suppressing the competing lifecycle primary", () => {
    renderAnchor(mockRequestDetails["mock-req-002"]);

    expect(screen.queryByRole("button", { name: "Respond to customer" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Contact customer" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Record Actual Work" })).toBeInTheDocument();
  });

  it("expands and collapses a long Customer Need accessibly", async () => {
    const user = userEvent.setup();
    const description = "A detailed customer request ".repeat(12);
    const { container } = renderAnchor({ ...baseDetail(), description });
    const need = container.querySelector(".line-clamp-2") as HTMLElement;

    expect(need).toHaveClass("line-clamp-2");
    await user.click(screen.getByRole("button", { name: "Full need" }));
    expect(screen.getByRole("button", { name: "Collapse" })).toHaveAttribute("aria-expanded", "true");
    expect(need).not.toHaveClass("line-clamp-2");
  });

  it("requires confirmation before Mark work done", async () => {
    renderAnchor({ ...baseDetail(), attentionLevel: "none" });
    await userEvent.setup().click(screen.getByRole("button", { name: "Mark work done" }));
    const dialog = screen.getByRole("dialog");
    expect(within(dialog).getByRole("heading", { name: "Mark request as Work completed?" })).toBeInTheDocument();
    expect(within(dialog).getByText(/does not notify the customer/i)).toBeInTheDocument();
  });

  it("demotes Mark work done to a neutral lifecycle control when operational work remains", () => {
    renderAnchor({ ...baseDetail(), attentionLevel: "none" }, { demoteMarkWorkDone: true });
    const button = screen.getByRole("button", { name: "Mark work done" });

    expect(button.className).toContain("border-[var(--ophalo-border)]");
    expect(button.className).not.toContain("bg-[var(--keep-accent)]");
  });

  it("uses an amber financial shortcut when the authoritative visit state is blocked", () => {
    renderAnchor(baseDetail(), {
      financialReviewShortcut: { label: "Resolve cost & price (1)", onClick: vi.fn(), tone: "blocked" },
    });
    const button = screen.getByRole("button", { name: "Resolve cost & price (1)" });
    expect(button.className).toContain("border-[var(--ophalo-attention)]");
    expect(button.className).toContain("bg-[var(--ophalo-attention-bg)]");
  });

  it("uses the server-authored Close action and confirmation", async () => {
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
    expect(within(screen.getByRole("dialog")).getByRole("heading", { name: "Close this request?" })).toBeInTheDocument();
  });

  it("hides permission-gated mutation and share controls for a read-only viewer", () => {
    const detail: KeepRequestDetailResult = {
      ...baseDetail(),
      availableActions: {
        ...OWNER_ACTIONS,
        canChangeStatus: false,
        canClose: false,
        canLogExternalContact: false,
        canAddInternalNote: false,
        primaryAction: null,
      },
    };
    renderAnchor(detail, { canRecordShareIntent: false, businessPageUrl: null, actualWorkShortcut: undefined, financialReviewShortcut: undefined });

    expect(screen.queryByRole("button", { name: "Contact customer" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /request page/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Actual Work/i })).not.toBeInTheDocument();
  });
});
