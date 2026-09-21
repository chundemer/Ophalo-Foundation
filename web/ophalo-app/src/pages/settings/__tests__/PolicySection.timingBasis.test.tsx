import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider, useQuery } from "@tanstack/react-query";
import { PolicySection } from "../PolicySection";
import { ApiError, type KeepSetupResult } from "../../../lib/apiClient";

const mockGetSetup = vi.fn();
const mockUpdatePolicy = vi.fn();

vi.mock("../../../lib/apiClient", async () => {
  const actual = await vi.importActual<typeof import("../../../lib/apiClient")>("../../../lib/apiClient");
  return {
    ...actual,
    api: {
      ...actual.api,
      getSetup: (...args: unknown[]) => mockGetSetup(...args),
      updatePolicy: (...args: unknown[]) => mockUpdatePolicy(...args),
    },
  };
});

const MONDAY = { weekday: "Monday", opensAt: "08:00", closesAt: "17:00" };

function setupWith(
  policy: Partial<KeepSetupResult["responsePolicy"]> = {},
  overrides: Partial<KeepSetupResult> = {},
): KeepSetupResult {
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
      firstResponseTimingBasis: "Continuous",
      standardResponseTimingBasis: "Continuous",
      priorityResponseTimingBasis: "Continuous",
      ...policy,
    },
    settingsVersion: "v1",
    calendar: { weeklyIntervals: [MONDAY], closures: [] },
    ...overrides,
  };
}

function Harness() {
  const { data } = useQuery({ queryKey: ["setup"], queryFn: () => mockGetSetup(), staleTime: Infinity });
  return data ? <PolicySection setup={data} /> : null;
}

function renderHarness() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <Harness />
    </QueryClientProvider>,
  );
}

const first = () => screen.getByLabelText("First response timing") as HTMLSelectElement;
const standard = () => screen.getByLabelText("Standard reply timing") as HTMLSelectElement;
const priority = () => screen.getByLabelText("Priority reply timing") as HTMLSelectElement;
const save = () => screen.getByRole("button", { name: "Save policy" });

beforeEach(() => {
  mockGetSetup.mockReset();
  mockUpdatePolicy.mockReset();
  mockGetSetup.mockResolvedValue(setupWith());
});

describe("PolicySection timing bases (GAP-100 6c)", () => {
  it("renders three independent selectors from the stored bases, with the priority caveat", async () => {
    mockGetSetup.mockResolvedValue(setupWith({ standardResponseTimingBasis: "StaffedHours" }));
    renderHarness();
    await screen.findByText("Save policy");

    expect(first().value).toBe("Continuous");
    expect(standard().value).toBe("StaffedHours");
    expect(priority().value).toBe("Continuous");
    expect(screen.getByText(/not on-call or emergency coverage/)).toBeInTheDocument();
  });

  it("sends the full snapshot with all three bases and the version", async () => {
    const user = userEvent.setup();
    mockUpdatePolicy.mockResolvedValue(setupWith({ priorityResponseTimingBasis: "StaffedHours" }, { settingsVersion: "v2" }));
    renderHarness();
    await screen.findByText("Save policy");

    await user.selectOptions(priority(), "StaffedHours");
    expect(first().value).toBe("Continuous");
    expect(standard().value).toBe("Continuous");
    await user.click(save());

    await waitFor(() =>
      expect(mockUpdatePolicy).toHaveBeenCalledWith({
        firstResponseTargetMinutes: 60,
        standardResponseTargetMinutes: 240,
        priorityResponseTargetMinutes: 60,
        statusCheckThresholdDays: 5,
        firstResponseTimingBasis: "Continuous",
        standardResponseTimingBasis: "Continuous",
        priorityResponseTimingBasis: "StaffedHours",
        settingsVersion: "v1",
      }),
    );
    expect(await screen.findByText("Saved.")).toBeInTheDocument();
  });

  it("shows the helper only for staffed hours with no weekly interval, and still allows the save", async () => {
    const user = userEvent.setup();
    mockGetSetup.mockResolvedValue(setupWith({}, { calendar: { weeklyIntervals: [], closures: [] } }));
    mockUpdatePolicy.mockResolvedValue(setupWith());
    renderHarness();
    await screen.findByText("Save policy");
    const helper = /Staffed hours needs at least one open day\. Add your hours in Business Hours & Closures\./;

    expect(screen.queryByText(helper)).not.toBeInTheDocument();
    await user.selectOptions(first(), "StaffedHours");
    expect(screen.getByText(helper)).toBeInTheDocument();
    expect(save()).toBeEnabled();
    await user.click(save());
    await waitFor(() => expect(mockUpdatePolicy).toHaveBeenCalledTimes(1));
  });

  it("does not show the helper when the calendar has an interval", async () => {
    const user = userEvent.setup();
    renderHarness();
    await screen.findByText("Save policy");

    await user.selectOptions(first(), "StaffedHours");

    expect(screen.queryByText(/Staffed hours needs at least one open day/)).not.toBeInTheDocument();
  });

  it.each([
    [422, "KeepResponsePolicy.StaffedTimingRequiresWeeklyInterval",
      "Add at least one open day in Business Hours & Closures before switching a target to staffed hours."],
    [422, "KeepResponsePolicy.StaffedHoursTargetUnreachable",
      "Your scheduled hours don't provide enough open time to meet that target. Shorten the target or add open hours."],
    [400, "KeepSetup.PolicyValidation", "Response times must be whole numbers greater than zero."],
    [422, "KeepResponsePolicy.SomethingNew", "We couldn't save your response policy. Check your entries and try again."],
  ])("maps %i %s to friendly copy, never the raw API message", async (status, code, copy) => {
    const user = userEvent.setup();
    mockUpdatePolicy.mockRejectedValue(new ApiError(status, code, "API 422 /keep/setup/policy"));
    renderHarness();
    await screen.findByText("Save policy");
    await user.selectOptions(first(), "StaffedHours");
    await user.click(save());

    expect(await screen.findByText(copy)).toBeInTheDocument();
    expect(screen.queryByText(/API \d+/)).not.toBeInTheDocument();
  });

  it("keeps a changed basis on a 409 and does not retry", async () => {
    const user = userEvent.setup();
    mockUpdatePolicy.mockRejectedValue(new ApiError(409, "KeepResponsePolicy.SettingsVersionMismatch", "API 409"));
    renderHarness();
    await screen.findByText("Save policy");
    await user.selectOptions(standard(), "StaffedHours");
    await user.click(save());

    expect(await screen.findByText(/Someone else changed these settings/)).toBeInTheDocument();
    expect(standard().value).toBe("StaffedHours");
    expect(mockUpdatePolicy).toHaveBeenCalledTimes(1);

    mockGetSetup.mockResolvedValue(setupWith({ firstResponseTimingBasis: "StaffedHours" }, { settingsVersion: "v2" }));
    await user.click(screen.getByRole("button", { name: "Refresh settings" }));
    await waitFor(() => expect(first().value).toBe("StaffedHours")); // untouched field resyncs
    expect(standard().value).toBe("StaffedHours"); // edited field keeps the draft
  });

  it.each([
    ["firstResponseTimingBasis"],
    ["standardResponseTimingBasis"],
    ["priorityResponseTimingBasis"],
  ])("blocks the save and never defaults to Continuous when %s is missing", async (field) => {
    mockGetSetup.mockResolvedValue(setupWith({ [field]: undefined }));
    renderHarness();
    await screen.findByText("Save policy");

    expect(save()).toBeDisabled();
    expect(screen.getByText(/Current timing settings aren't available/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Refresh settings" })).toBeInTheDocument();
    expect(mockUpdatePolicy).not.toHaveBeenCalled();
  });
});
