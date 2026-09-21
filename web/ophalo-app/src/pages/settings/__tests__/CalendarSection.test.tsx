import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider, useQuery } from "@tanstack/react-query";
import { CalendarSection } from "../CalendarSection";
import { ApiError, type KeepSetupResult } from "../../../lib/apiClient";

const mockGetSetup = vi.fn();
const mockUpdateCalendar = vi.fn();

vi.mock("../../../lib/apiClient", async () => {
  const actual = await vi.importActual<typeof import("../../../lib/apiClient")>("../../../lib/apiClient");
  return {
    ...actual,
    api: {
      ...actual.api,
      getSetup: (...args: unknown[]) => mockGetSetup(...args),
      updateCalendar: (...args: unknown[]) => mockUpdateCalendar(...args),
    },
  };
});

function setupWith(overrides: Partial<KeepSetupResult> = {}): KeepSetupResult {
  return {
    businessName: "Apex",
    timeZone: "America/Chicago",
    customerFacingPhone: null,
    customerFacingEmail: null,
    logoUrl: null,
    websiteUrl: null,
    responsePolicy: {
      firstResponseTargetMinutes: 60,
      standardResponseTargetMinutes: 240,
      priorityResponseTargetMinutes: 60,
      statusCheckThresholdDays: 5,
    },
    settingsVersion: "v1",
    calendar: {
      weeklyIntervals: [{ weekday: "Monday", opensAt: "08:00", closesAt: "17:00" }],
      closureDates: ["2026-12-25"],
    },
    ...overrides,
  };
}

function Harness() {
  const { data } = useQuery({ queryKey: ["setup"], queryFn: () => mockGetSetup(), staleTime: Infinity });
  return data ? <CalendarSection setup={data} /> : null;
}

function renderSection() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <Harness />
    </QueryClientProvider>,
  );
}

const saveButton = () => screen.getByRole("button", { name: "Save hours" });

describe("CalendarSection", () => {
  beforeEach(() => {
    mockGetSetup.mockReset();
    mockUpdateCalendar.mockReset();
    mockGetSetup.mockResolvedValue(setupWith());
  });

  it("renders the stored calendar and disables Save until edited", async () => {
    renderSection();
    expect(await screen.findByLabelText("Monday opens at")).toHaveValue("08:00");
    expect(screen.queryByLabelText("Tuesday opens at")).not.toBeInTheDocument();
    expect(screen.getByText("2026-12-25")).toBeInTheDocument();
    expect(saveButton()).toBeDisabled();
  });

  it("defaults a newly opened day to 09:00-17:00 and saves the full snapshot with the version", async () => {
    const user = userEvent.setup();
    mockUpdateCalendar.mockResolvedValue(setupWith({ settingsVersion: "v2" }));
    renderSection();
    await screen.findByLabelText("Monday opens at");

    await user.click(screen.getByRole("switch", { name: "Open on Tuesday" }));
    expect(screen.getByLabelText("Tuesday opens at")).toHaveValue("09:00");
    expect(screen.getByLabelText("Tuesday closes at")).toHaveValue("17:00");
    await user.click(saveButton());

    await waitFor(() =>
      expect(mockUpdateCalendar).toHaveBeenCalledWith({
        weeklyIntervals: [
          { weekday: "Monday", opensAt: "08:00", closesAt: "17:00" },
          { weekday: "Tuesday", opensAt: "09:00", closesAt: "17:00" },
        ],
        closureDates: ["2026-12-25"],
        settingsVersion: "v1",
      }),
    );
    expect(await screen.findByText("Saved.")).toBeInTheDocument();
  });

  it("adds closures sorted, blocks duplicates, and removes them", async () => {
    const user = userEvent.setup();
    renderSection();
    await screen.findByLabelText("Closure date");

    await user.type(screen.getByLabelText("Closure date"), "2026-11-26");
    await user.click(screen.getByRole("button", { name: "Add closure" }));
    const items = screen.getAllByRole("listitem").map((li) => li.textContent);
    expect(items[0]).toContain("2026-11-26");
    expect(items[1]).toContain("2026-12-25");

    await user.type(screen.getByLabelText("Closure date"), "2026-12-25");
    await user.click(screen.getByRole("button", { name: "Add closure" }));
    expect(screen.getByText("That closure date is already added.")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Remove closure 2026-11-26" }));
    await user.click(screen.getByRole("button", { name: "Remove closure 2026-12-25" }));
    expect(screen.getByText("No closures scheduled.")).toBeInTheDocument();
  });

  it("blocks saving when closing is not after opening", async () => {
    const user = userEvent.setup();
    renderSection();
    const closes = await screen.findByLabelText("Monday closes at");
    await user.clear(closes);
    await user.type(closes, "07:00");

    expect(screen.getByText("Closing time must be after opening time.")).toBeInTheDocument();
    expect(saveButton()).toBeDisabled();
  });

  it.each([
    ["LastWeeklyIntervalRequired", "At least one weekly open window is required while staffed-hours timing is active."],
    ["StaffedTimingRequiresWeeklyInterval", "Cannot switch to staffed hours without at least one open weekly window."],
    ["StaffedHoursTargetUnreachable", "Your scheduled hours do not provide enough open time to satisfy your configured SLA response target."],
    ["KeepResponsePolicy.DuplicateWeekday", "Each weekday can only have one open window."],
    ["KeepResponsePolicy.DuplicateClosureDate", "That closure date is already added."],
    ["KeepSetup.CalendarValidation", "We couldn't save your business hours. Check your entries and try again."],
    ["KeepResponsePolicy.OverlappingClosureChange", "We couldn't save your business hours. Check your entries and try again."],
  ])("maps %s to friendly copy, never the generic API message", async (code, copy) => {
    const user = userEvent.setup();
    mockUpdateCalendar.mockRejectedValue(new ApiError(422, code, "API 422 /keep/setup/calendar"));
    renderSection();
    await user.click(await screen.findByRole("switch", { name: "Open on Tuesday" }));
    await user.click(saveButton());

    expect(await screen.findByText(copy)).toBeInTheDocument();
    expect(screen.queryByText(/API 422/)).not.toBeInTheDocument();
  });

  it("keeps the draft on 409 and refresh reloads without discarding edits", async () => {
    const user = userEvent.setup();
    mockUpdateCalendar.mockRejectedValue(new ApiError(409, "SettingsVersionMismatch", "API 409"));
    renderSection();
    await user.click(await screen.findByRole("switch", { name: "Open on Tuesday" }));
    await user.click(saveButton());

    const alert = await screen.findByText(/Settings were updated by another user/);
    expect(alert).toBeInTheDocument();
    expect(mockUpdateCalendar).toHaveBeenCalledTimes(1);
    expect(screen.getByLabelText("Tuesday opens at")).toBeInTheDocument();

    mockGetSetup.mockResolvedValue(setupWith({ settingsVersion: "v2" }));
    await user.click(screen.getByRole("button", { name: "Refresh settings" }));
    expect(await screen.findByText(/Your unsaved edits are still shown/)).toBeInTheDocument();
    expect(screen.getByLabelText("Tuesday opens at")).toBeInTheDocument();
    expect(within(document.body).queryByText(/Settings were updated by another user/)).not.toBeInTheDocument();
  });

  it("blocks the save when the settings version is missing", async () => {
    mockGetSetup.mockResolvedValue(setupWith({ settingsVersion: undefined }));
    renderSection();
    await screen.findByLabelText("Monday opens at");

    expect(screen.getByText(/Settings version isn't available/)).toBeInTheDocument();
    expect(saveButton()).toBeDisabled();
    expect(mockUpdateCalendar).not.toHaveBeenCalled();
  });
});
