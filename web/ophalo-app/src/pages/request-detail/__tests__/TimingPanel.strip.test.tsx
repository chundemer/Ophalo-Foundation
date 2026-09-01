import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { TimingPanel } from "../TimingPanel";
import { mockRequestDetails } from "../../../mocks/fixtures";
import type { KeepRequestDetailResult } from "../../../lib/apiClient";

// RD-059A: the Anchor's compact Internal Planning row must be readable and fully
// keyboard-recoverable — focus lands in the editor on open, Escape/Cancel restore the
// trigger, errors are announced, and configured/empty/read-only states are unambiguous.

const mockSetPlannedFor = vi.fn();
const mockClearPlannedFor = vi.fn();
const mockSetFollowUpOn = vi.fn();

vi.mock("../../../lib/apiClient", () => ({
  api: {
    setPlannedFor: (...a: unknown[]) => mockSetPlannedFor(...a),
    clearPlannedFor: (...a: unknown[]) => mockClearPlannedFor(...a),
    setFollowUpOn: (...a: unknown[]) => mockSetFollowUpOn(...a),
  },
  ApiError: class ApiError extends Error {
    status: number;
    constructor(status: number, _code: string | undefined, message: string) {
      super(message);
      this.status = status;
    }
  },
}));

function detailWith(overrides: Partial<KeepRequestDetailResult> = {}): KeepRequestDetailResult {
  return {
    ...mockRequestDetails["mock-req-001"],
    plannedForDate: null,
    followUpOnDate: null,
    followUpOnReason: null,
    followUpOnNote: null,
    ...overrides,
  };
}

function renderStrip(detail: KeepRequestDetailResult) {
  return render(
    <TimingPanel requestId={detail.requestId} detail={detail} onDetailUpdated={vi.fn()} strip />,
  );
}

beforeEach(() => vi.clearAllMocks());

describe("TimingPanel strip — presentation states", () => {
  it("renames the follow-up control to 'Internal follow-up (optional)'", () => {
    renderStrip(detailWith());
    expect(screen.getByText("Internal follow-up (optional)")).toBeInTheDocument();
  });

  it("shows a configuration checkmark for a persisted planned date, but not for an empty one", () => {
    const { rerender } = renderStrip(detailWith());
    const emptyBtn = screen.getByRole("button", { name: /planned work date: not set/i });
    expect(within(emptyBtn).queryByText(/check/i)).not.toBeInTheDocument();
    // lucide renders an svg with class lucide-check; assert none present in the empty control
    expect(emptyBtn.querySelector("svg.lucide-check")).toBeNull();

    rerender(
      <TimingPanel
        requestId="mock-req-001"
        detail={detailWith({ plannedForDate: "2026-09-10" })}
        onDetailUpdated={vi.fn()}
        strip
      />,
    );
    const setBtn = screen.getByRole("button", { name: /planned work date: .*2026/i });
    expect(setBtn.querySelector("svg.lucide-check")).not.toBeNull();
  });

  it("internal follow-up never shows a checkmark — empty or set — and no required/overdue cue", () => {
    const { rerender } = renderStrip(detailWith());
    const emptyBtn = screen.getByRole("button", { name: /internal follow-up \(optional\): not set/i });
    expect(emptyBtn.querySelector("svg.lucide-check")).toBeNull();
    expect(screen.queryByText(/overdue|required/i)).not.toBeInTheDocument();

    rerender(
      <TimingPanel
        requestId="mock-req-001"
        detail={detailWith({ followUpOnDate: "2026-09-12", followUpOnReason: "reminder" })}
        onDetailUpdated={vi.fn()}
        strip
      />,
    );
    const setBtn = screen.getByRole("button", { name: /internal follow-up \(optional\): .*2026/i });
    expect(setBtn.querySelector("svg.lucide-check")).toBeNull();
  });

  it("enabled empty controls use normal-contrast action copy with a calendar cue and no ellipsis", () => {
    renderStrip(detailWith());

    const planned = screen.getByRole("button", { name: /planned work date: not set/i });
    expect(planned).toHaveTextContent("Set planned date");
    expect(planned).not.toHaveTextContent("…");
    expect(planned.querySelector("svg.lucide-calendar-days")).not.toBeNull();
    expect(planned.className).toContain("text-[var(--ophalo-ink)]");
    expect(planned.className).not.toContain("text-[var(--ophalo-muted)]");

    const followUp = screen.getByRole("button", { name: /internal follow-up \(optional\): not set/i });
    expect(followUp).toHaveTextContent("Set follow-up date");
    expect(followUp).not.toHaveTextContent("…");
    expect(followUp.querySelector("svg.lucide-calendar-days")).not.toBeNull();
    expect(followUp.className).toContain("text-[var(--ophalo-ink)]");
    expect(followUp.className).not.toContain("text-[var(--ophalo-muted)]");
  });

  it("renders read-only planning values as a muted 'Read only' caption with no button semantics", () => {
    const detail = detailWith({
      plannedForDate: "2026-09-10",
      followUpOnDate: "2026-09-12",
      availableActions: {
        ...mockRequestDetails["mock-req-001"].availableActions,
        canSetPlannedFor: false,
        canSetFollowUpOn: false,
      },
    });
    renderStrip(detail);
    expect(screen.queryByRole("button", { name: /planned work date/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /internal follow-up/i })).not.toBeInTheDocument();
    expect(screen.getAllByText("Read only")).toHaveLength(2);
  });
});

describe("TimingPanel strip — keyboard recovery", () => {
  it("opens the planned editor on Enter and focuses the date field", async () => {
    const user = userEvent.setup();
    renderStrip(detailWith());
    const trigger = screen.getByRole("button", { name: /planned work date: not set/i });
    trigger.focus();
    await user.keyboard("{Enter}");
    await waitFor(() => expect(screen.getByLabelText("Date", { selector: "#planned-date" })).toHaveFocus());
  });

  it("Escape closes the editor and restores focus to the trigger", async () => {
    const user = userEvent.setup();
    renderStrip(detailWith());
    const trigger = screen.getByRole("button", { name: /planned work date: not set/i });
    trigger.focus();
    await user.keyboard("{Enter}");
    await waitFor(() => expect(screen.getByLabelText("Date", { selector: "#planned-date" })).toHaveFocus());

    await user.keyboard("{Escape}");
    await waitFor(() => expect(trigger).toHaveFocus());
    expect(trigger).toHaveAttribute("aria-expanded", "false");
  });

  it("Cancel closes the editor and restores focus to the trigger", async () => {
    const user = userEvent.setup();
    renderStrip(detailWith());
    const trigger = screen.getByRole("button", { name: /planned work date: not set/i });
    trigger.focus();
    await user.keyboard("{Enter}");
    await waitFor(() => expect(screen.getByLabelText("Date", { selector: "#planned-date" })).toHaveFocus());

    await user.click(screen.getByRole("button", { name: "Cancel" }));
    await waitFor(() => expect(trigger).toHaveFocus());
  });

  it("opens each disclosure on Space as well as Enter", async () => {
    const user = userEvent.setup();
    renderStrip(detailWith());

    const planned = screen.getByRole("button", { name: /planned work date: not set/i });
    planned.focus();
    await user.keyboard(" ");
    await waitFor(() => expect(planned).toHaveAttribute("aria-expanded", "true"));
    await user.keyboard("{Escape}");

    const followUp = screen.getByRole("button", { name: /internal follow-up \(optional\): not set/i });
    followUp.focus();
    await user.keyboard(" ");
    await waitFor(() => expect(followUp).toHaveAttribute("aria-expanded", "true"));
    await waitFor(() => expect(screen.getByLabelText("Date", { selector: "#follow-up-date" })).toHaveFocus());
  });

  it("keeps the planned editor open and disables the field on a 409 conflict", async () => {
    const user = userEvent.setup();
    const { ApiError } = await import("../../../lib/apiClient");
    mockSetPlannedFor.mockRejectedValue(new ApiError(409, "RequestChanged", "conflict"));
    renderStrip(detailWith());
    const trigger = screen.getByRole("button", { name: /planned work date/i });
    await user.click(trigger);
    await user.type(screen.getByLabelText("Date", { selector: "#planned-date" }), "2026-09-15");
    await user.click(screen.getByRole("button", { name: /set date/i }));

    await waitFor(() =>
      expect(screen.getByRole("alert")).toHaveTextContent(/updated by another team member/i),
    );
    expect(trigger).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByLabelText("Date", { selector: "#planned-date" })).toBeDisabled();
  });

  it("shows a loading label on the submit control while the planned mutation is in flight", async () => {
    const user = userEvent.setup();
    let resolve: (v: unknown) => void = () => {};
    mockSetPlannedFor.mockReturnValue(new Promise((r) => { resolve = r; }));
    renderStrip(detailWith());
    await user.click(screen.getByRole("button", { name: /planned work date/i }));
    await user.type(screen.getByLabelText("Date", { selector: "#planned-date" }), "2026-09-15");
    await user.click(screen.getByRole("button", { name: /set date/i }));

    await waitFor(() => expect(screen.getByRole("button", { name: /saving…/i })).toBeDisabled());
    resolve({ ...detailWith(), plannedForDate: "2026-09-15" });
  });

  it("only one planning editor is open at a time", async () => {
    const user = userEvent.setup();
    renderStrip(detailWith());
    await user.click(screen.getByRole("button", { name: /planned work date/i }));
    expect(screen.getByRole("button", { name: /planned work date/i })).toHaveAttribute("aria-expanded", "true");
    await user.click(screen.getByRole("button", { name: /internal follow-up/i }));
    expect(screen.getByRole("button", { name: /planned work date/i })).toHaveAttribute("aria-expanded", "false");
    expect(screen.getByRole("button", { name: /internal follow-up/i })).toHaveAttribute("aria-expanded", "true");
  });

  it("announces a save error through an alert in the editor", async () => {
    const user = userEvent.setup();
    mockSetPlannedFor.mockRejectedValue(new Error("network down"));
    renderStrip(detailWith());
    await user.click(screen.getByRole("button", { name: /planned work date/i }));
    const dateField = screen.getByLabelText("Date", { selector: "#planned-date" });
    await user.type(dateField, "2026-09-15");
    await user.click(screen.getByRole("button", { name: /set date/i }));
    await waitFor(() =>
      expect(screen.getByRole("alert")).toHaveTextContent(/could not set planned date/i),
    );
  });
});

describe("TimingPanel full card — keyboard and error parity", () => {
  function renderFull(detail: KeepRequestDetailResult) {
    return render(
      <TimingPanel requestId={detail.requestId} detail={detail} onDetailUpdated={vi.fn()} />,
    );
  }

  it("focuses the field on open and restores trigger focus on Escape", async () => {
    const user = userEvent.setup();
    renderFull(detailWith());
    const trigger = screen.getByRole("button", { name: /set planned date/i });
    trigger.focus();
    await user.keyboard("{Enter}");
    await waitFor(() => expect(screen.getByLabelText("Date", { selector: "#planned-date" })).toHaveFocus());

    await user.keyboard("{Escape}");
    await waitFor(() => expect(trigger).toHaveFocus());
    expect(trigger).toHaveAttribute("aria-expanded", "false");
  });

  it("opens on Space and restores trigger focus on Cancel", async () => {
    const user = userEvent.setup();
    renderFull(detailWith());
    const trigger = screen.getByRole("button", { name: /set follow-up/i });
    trigger.focus();
    await user.keyboard(" ");
    await waitFor(() => expect(screen.getByLabelText("Date", { selector: "#follow-up-date" })).toHaveFocus());
    await user.click(screen.getByRole("button", { name: "Cancel" }));
    await waitFor(() => expect(trigger).toHaveFocus());
  });

  it("announces a save error through an alert in the full-card editor", async () => {
    const user = userEvent.setup();
    mockSetPlannedFor.mockRejectedValue(new Error("network down"));
    renderFull(detailWith());
    await user.click(screen.getByRole("button", { name: /set planned date/i }));
    await user.type(screen.getByLabelText("Date", { selector: "#planned-date" }), "2026-09-15");
    await user.click(screen.getByRole("button", { name: /set date/i }));
    await waitFor(() =>
      expect(screen.getByRole("alert")).toHaveTextContent(/could not set planned date/i),
    );
  });
});
