import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { UnifiedComposer } from "../UnifiedComposer";
import { mockRequestDetails } from "../../../mocks/fixtures";
import type { KeepRequestDetailResult } from "../../../lib/apiClient";
import { __resetComposerDrafts, seedComposerDraft } from "../../../hooks/useComposerDraft";
import { __resetUnloadGuard } from "../../../lib/unloadGuard";

// GAP-073 / ADR-502 regression coverage: per-request draft isolation and accessible
// (readOnly, not disabled) 409 preservation + version-advance recovery, for BOTH composer modes.

const mockPostBusinessUpdate = vi.fn();
const mockAddInternalNote = vi.fn();

vi.mock("../../../lib/apiClient", () => ({
  api: {
    postBusinessUpdate: (...args: unknown[]) => mockPostBusinessUpdate(...args),
    addInternalNote: (...args: unknown[]) => mockAddInternalNote(...args),
  },
  ApiError: class ApiError extends Error {
    status: number;
    constructor(status: number, message: string) {
      super(message);
      this.status = status;
    }
  },
}));

vi.mock("../NotifyCustomerPanel", () => ({
  NotifyCustomerPanel: () => <div data-testid="notify-panel" />,
}));

function detailFor(requestId: string, version: string): KeepRequestDetailResult {
  return { ...mockRequestDetails["mock-req-001"], requestId, version, pendingNotification: null };
}

function detailWithStatusChange(requestId: string, version: string): KeepRequestDetailResult {
  const base = detailFor(requestId, version);
  return {
    ...base,
    availableActions: { ...base.availableActions, canChangeStatus: true },
  };
}

beforeEach(() => {
  vi.clearAllMocks();
  sessionStorage.clear();
  __resetComposerDrafts();
  __resetUnloadGuard();
  vi.stubGlobal("matchMedia", vi.fn().mockReturnValue({ matches: false }));
  if (!HTMLElement.prototype.scrollTo) {
    HTMLElement.prototype.scrollTo = vi.fn() as typeof HTMLElement.prototype.scrollTo;
  }
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("composer draft safety — per-request isolation", () => {
  it("a draft typed for request A is not shown on request B, and is still there on return to A", async () => {
    const user = userEvent.setup();
    const { rerender } = render(
      <UnifiedComposer requestId="req-A" detail={detailFor("req-A", "vA")} onDetailUpdated={vi.fn()} />,
    );

    const messageA = screen.getByLabelText("Customer update message") as HTMLTextAreaElement;
    await user.type(messageA, "Hi Jane, we'll be there Tuesday");

    rerender(
      <UnifiedComposer requestId="req-B" detail={detailFor("req-B", "vB")} onDetailUpdated={vi.fn()} />,
    );
    expect((screen.getByLabelText("Customer update message") as HTMLTextAreaElement).value).toBe("");

    rerender(
      <UnifiedComposer requestId="req-A" detail={detailFor("req-A", "vA")} onDetailUpdated={vi.fn()} />,
    );
    expect((screen.getByLabelText("Customer update message") as HTMLTextAreaElement).value).toBe(
      "Hi Jane, we'll be there Tuesday",
    );
  });
});

describe("composer draft safety — 409 preserves text as readOnly and recovers on version advance", () => {
  it("customer-update composer: 409 → readOnly (not disabled), text kept, submit blocked; recovers when detail.version advances", async () => {
    const user = userEvent.setup();
    const { ApiError } = await import("../../../lib/apiClient");
    mockPostBusinessUpdate.mockRejectedValueOnce(new (ApiError as unknown as new (s: number, m: string) => Error)(409, "conflict"));

    seedComposerDraft("req-1", { message: "A carefully written 600-word reply", status: "scheduled" });
    const { rerender } = render(
      <UnifiedComposer requestId="req-1" detail={detailWithStatusChange("req-1", "v1")} onDetailUpdated={vi.fn()} />,
    );

    await user.click(screen.getByRole("button", { name: "Post & prepare text" }));

    const textarea = screen.getByLabelText("Customer update message") as HTMLTextAreaElement;
    await waitFor(() => expect(textarea).toHaveAttribute("readonly"));
    expect(textarea).not.toBeDisabled(); // still selectable / copyable
    expect(textarea.value).toBe("A carefully written 600-word reply");
    expect(screen.getByRole("button", { name: "Post & prepare text" })).toBeDisabled();

    // D3 select-specific rule: aria-disabled (not disabled), and a selection attempt is ignored
    // so the drafted status is preserved and visible.
    const statusSelect = screen.getByLabelText("Also change status (optional)") as HTMLSelectElement;
    expect(statusSelect).toHaveAttribute("aria-disabled", "true");
    expect(statusSelect).not.toBeDisabled();
    await user.selectOptions(statusSelect, "in_progress").catch(() => {});
    expect(statusSelect.value).toBe("scheduled");

    rerender(
      <UnifiedComposer requestId="req-1" detail={detailWithStatusChange("req-1", "v2")} onDetailUpdated={vi.fn()} />,
    );

    await waitFor(() =>
      expect(screen.getByLabelText("Customer update message")).not.toHaveAttribute("readonly"),
    );
    expect(screen.getByRole("button", { name: "Post & prepare text" })).toBeEnabled();
    expect((screen.getByLabelText("Customer update message") as HTMLTextAreaElement).value).toBe(
      "A carefully written 600-word reply",
    );

    // The status select recovers too: no longer aria-disabled, value intact, selectable again.
    const recoveredSelect = screen.getByLabelText("Also change status (optional)") as HTMLSelectElement;
    expect(recoveredSelect).not.toHaveAttribute("aria-disabled", "true");
    expect(recoveredSelect.value).toBe("scheduled");
    await user.selectOptions(recoveredSelect, "in_progress");
    expect(recoveredSelect.value).toBe("in_progress");
  });

  it("internal-note composer: 409 → readOnly (not disabled), text kept, submit blocked; recovers when detail.version advances", async () => {
    const user = userEvent.setup();
    const { ApiError } = await import("../../../lib/apiClient");
    mockAddInternalNote.mockRejectedValueOnce(new (ApiError as unknown as new (s: number, m: string) => Error)(409, "conflict"));

    seedComposerDraft("req-1", { note: "team: disconnect switch was mislabeled" });
    const { rerender } = render(
      <UnifiedComposer requestId="req-1" detail={detailFor("req-1", "v1")} onDetailUpdated={vi.fn()} />,
    );

    await user.click(screen.getByRole("tab", { name: "Internal note" }));
    await user.click(screen.getByRole("button", { name: "Save internal note" }));

    const note = screen.getByLabelText("Internal note — not visible to customer") as HTMLTextAreaElement;
    await waitFor(() => expect(note).toHaveAttribute("readonly"));
    expect(note).not.toBeDisabled();
    expect(note.value).toBe("team: disconnect switch was mislabeled");
    expect(screen.getByRole("button", { name: "Save internal note" })).toBeDisabled();

    rerender(
      <UnifiedComposer requestId="req-1" detail={detailFor("req-1", "v2")} onDetailUpdated={vi.fn()} />,
    );

    await waitFor(() =>
      expect(screen.getByLabelText("Internal note — not visible to customer")).not.toHaveAttribute("readonly"),
    );
    expect(screen.getByRole("button", { name: "Save internal note" })).toBeEnabled();
  });
});
