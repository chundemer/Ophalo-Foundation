import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ActualWorkFinancialReviewWorkspace } from "../ActualWorkFinancialReviewWorkspace";
import type {
  ActualWorkFinancialDetailResult,
  ActualWorkRequestPendingReviewEntry,
  KeepRequestDetailResult,
} from "../../../lib/apiClient";
import type { FinancialReviewOutcome } from "../useActualWorkFinancialReview";

// BL138 Slice 2: the wide financial-review workspace's pending-visit switcher, the post-success
// "Review next pending visit" continuation, and dirty-switch protection.

const REQUEST = {
  referenceCode: "R-100",
  status: "InProgress",
  customerName: "Jane Doe",
  serviceAddressLine1: "42 Elm Street",
  serviceCity: "Springfield",
  serviceState: "OR",
  serviceZip: "97403",
  participants: [],
} as unknown as KeepRequestDetailResult;

function visitDetail(overrides: Partial<ActualWorkFinancialDetailResult> = {}): ActualWorkFinancialDetailResult {
  return {
    id: "aw-42",
    submittedAtUtc: "2026-08-20T10:00:00Z",
    reviewedAtUtc: null,
    reviewedByDisplayName: null,
    reviewNote: null,
    outcome: null,
    completionNote: null,
    concurrencyVersion: "v1",
    hasIncompleteFinancialData: false,
    hasNoChargeDisposition: false,
    visitNote: null,
    recorderAccountUserId: "u-tech",
    totalSalesPrice: 100,
    totalStandardExpectedDirectCost: 40,
    totalMargin: 60,
    lines: [
      {
        id: "l1",
        actualQuantity: 1,
        displayNameSnapshot: "Capacitor",
        performerDisplayName: "Dana Tech",
        isFinancialDataComplete: true,
        lineSalesTotal: 100,
        lineStandardExpectedDirectCostTotal: 40,
        lineMargin: 60,
      },
    ],
    blockers: [],
    ...overrides,
  } as unknown as ActualWorkFinancialDetailResult;
}

function pending(
  id: string,
  reviewStatus: ActualWorkRequestPendingReviewEntry["reviewStatus"] = "ReadyToReview",
): ActualWorkRequestPendingReviewEntry {
  return {
    actualWorkId: id,
    submittedAtUtc: "2026-08-20T10:00:00Z",
    lineCount: 1,
    recorderDisplayName: "Dana Tech",
    reviewStatus,
  };
}

const ok: FinancialReviewOutcome = { kind: "success" };

function renderWorkspace(props: Partial<React.ComponentProps<typeof ActualWorkFinancialReviewWorkspace>> = {}) {
  const onSwitchVisit = vi.fn();
  const onExit = vi.fn();
  render(
    <ActualWorkFinancialReviewWorkspace
      request={REQUEST}
      visit={props.visit ?? visitDetail()}
      visitNumber={1}
      onExit={onExit}
      onContactLaunch={vi.fn()}
      onReview={vi.fn(() => Promise.resolve(ok))}
      onResolveLine={vi.fn(() => Promise.resolve(ok))}
      onRecordNoChargeDisposition={vi.fn(() => Promise.resolve(ok))}
      onReplace={vi.fn(() => Promise.resolve(ok))}
      isVisitMutating={() => false}
      onReviewSuccess={vi.fn()}
      pendingItems={props.pendingItems ?? []}
      onSwitchVisit={onSwitchVisit}
      nextPendingVisitId={props.nextPendingVisitId ?? null}
    />,
  );
  return { onSwitchVisit, onExit };
}

describe("ActualWorkFinancialReviewWorkspace — BL138 Slice 2", () => {
  it("hides the switcher for a single pending visit", () => {
    renderWorkspace({ pendingItems: [pending("aw-42")] });
    expect(screen.queryByRole("navigation", { name: /pending financial reviews on this request/i })).toBeNull();
  });

  it("shows the switcher for 2+ pending visits and switches on click", async () => {
    const { onSwitchVisit } = renderWorkspace({
      pendingItems: [pending("aw-42"), pending("aw-77", "NeedsCostPriceResolution")],
    });
    const nav = screen.getByRole("navigation", { name: /pending financial reviews on this request/i });
    expect(nav).toBeTruthy();
    await userEvent.click(screen.getByRole("button", { name: /Visit #2/ }));
    expect(onSwitchVisit).toHaveBeenCalledWith("aw-77");
  });

  it("guards a switch behind a discard confirm when the reviewer note is dirty", async () => {
    const { onSwitchVisit } = renderWorkspace({
      pendingItems: [pending("aw-42"), pending("aw-77")],
    });
    await userEvent.type(screen.getByLabelText(/Reviewer internal note/i), "checked hours");
    await userEvent.click(screen.getByRole("button", { name: /Visit #2/ }));
    expect(onSwitchVisit).not.toHaveBeenCalled();
    const dialog = screen.getByRole("alertdialog");
    expect(dialog).toBeTruthy();

    await userEvent.click(screen.getByRole("button", { name: /Discard and continue/i }));
    expect(onSwitchVisit).toHaveBeenCalledWith("aw-77");
  });

  it("guards a switch when an inline resolution form holds unsaved input", async () => {
    const { onSwitchVisit } = renderWorkspace({
      visit: visitDetail({
        blockers: [
          {
            lineId: "l1",
            displayNameSnapshot: "Capacitor",
            sellPriceMissing: false,
            standardExpectedDirectCostMissing: true,
          },
        ],
      }),
      pendingItems: [pending("aw-42"), pending("aw-77")],
    });
    await userEvent.selectOptions(screen.getByLabelText(/How was this determined/), "Other");
    await userEvent.click(screen.getByRole("button", { name: /Visit #2/ }));
    expect(onSwitchVisit).not.toHaveBeenCalled();
    expect(screen.getByRole("alertdialog")).toBeTruthy();
  });

  it("keeps editing when the discard confirm is dismissed", async () => {
    const { onExit } = renderWorkspace({ pendingItems: [] });
    await userEvent.type(screen.getByLabelText(/Reviewer internal note/i), "wip");
    await userEvent.click(screen.getByRole("button", { name: "Back to Request" }));
    await userEvent.click(screen.getByRole("button", { name: /Keep editing/i }));
    expect(onExit).not.toHaveBeenCalled();
    expect(screen.queryByRole("alertdialog")).toBeNull();
  });

  it("offers 'Review next pending visit' after review, targeting the next server-ordered visit", async () => {
    const { onSwitchVisit } = renderWorkspace({
      visit: visitDetail({ reviewedAtUtc: "2026-08-21T09:00:00Z", reviewedByDisplayName: "Sam Owner" }),
      pendingItems: [pending("aw-77")],
      nextPendingVisitId: "aw-77",
    });
    await userEvent.click(screen.getByRole("button", { name: /Review next pending visit/i }));
    expect(onSwitchVisit).toHaveBeenCalledWith("aw-77");
  });

  it("omits 'Review next' with no remaining pending visit (no wraparound)", () => {
    renderWorkspace({
      visit: visitDetail({ reviewedAtUtc: "2026-08-21T09:00:00Z", reviewedByDisplayName: "Sam Owner" }),
      pendingItems: [],
      nextPendingVisitId: null,
    });
    expect(screen.queryByRole("button", { name: /Review next pending visit/i })).toBeNull();
    expect(screen.getByRole("button", { name: "Back to request" })).toBeTruthy();
  });
});
