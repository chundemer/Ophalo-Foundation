import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, fireEvent } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider, useQuery } from "@tanstack/react-query";
import { PolicySection } from "../PolicySection";
import { ApiError, type KeepSetupResult } from "../../../lib/apiClient";

// GAP-100 6a-1 (ADR-506): the policy save echoes settingsVersion; a stale save (409) never
// retries, keeps the draft, and offers an explicit Refresh settings action.

const mockGetSetup = vi.fn();
const mockUpdatePolicy = vi.fn();

vi.mock("../../../lib/apiClient", async () => {
  const actual = await vi.importActual<typeof import("../../../lib/apiClient")>(
    "../../../lib/apiClient",
  );
  return {
    ...actual,
    api: {
      ...actual.api,
      getSetup: (...args: unknown[]) => mockGetSetup(...args),
      updatePolicy: (...args: unknown[]) => mockUpdatePolicy(...args),
    },
  };
});

function setupWith(overrides: Partial<KeepSetupResult> = {}, first = 60): KeepSetupResult {
  return {
    businessName: "Apex",
    timeZone: "America/Chicago",
    customerFacingPhone: null,
    customerFacingEmail: null,
    logoUrl: null,
    websiteUrl: null,
    responsePolicy: {
      firstResponseTargetMinutes: first,
      standardResponseTargetMinutes: 240,
      priorityResponseTargetMinutes: 60,
      statusCheckThresholdDays: 5,
      firstResponseTimingBasis: "Continuous",
      standardResponseTimingBasis: "Continuous",
      priorityResponseTimingBasis: "Continuous",
    },
    settingsVersion: "v1",
    ...overrides,
  };
}

// Mirrors Settings.tsx: the ["setup"] query owns the data and feeds PolicySection.
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

const firstInput = () => screen.getAllByRole("spinbutton")[0] as HTMLInputElement;

beforeEach(() => {
  mockGetSetup.mockReset();
  mockUpdatePolicy.mockReset();
});

describe("PolicySection settingsVersion", () => {
  it("sends the current settingsVersion with the save", async () => {
    mockGetSetup.mockResolvedValue(setupWith());
    mockUpdatePolicy.mockResolvedValue(setupWith({ settingsVersion: "v2" }, 30));
    renderHarness();
    await screen.findByText("Save policy");

    await userEvent.clear(firstInput());
    await userEvent.type(firstInput(), "30");
    await userEvent.click(screen.getByRole("button", { name: "Save policy" }));

    await waitFor(() => expect(mockUpdatePolicy).toHaveBeenCalledTimes(1));
    expect(mockUpdatePolicy).toHaveBeenCalledWith({
      firstResponseTargetMinutes: 30,
      standardResponseTargetMinutes: 240,
      priorityResponseTargetMinutes: 60,
      statusCheckThresholdDays: 5,
      firstResponseTimingBasis: "Continuous",
      standardResponseTimingBasis: "Continuous",
      priorityResponseTimingBasis: "Continuous",
      settingsVersion: "v1",
    });
    expect(await screen.findByText("Saved.")).toBeTruthy();
  });

  it.each([undefined, ""])("blocks the request when settingsVersion is %j", async (version) => {
    mockGetSetup.mockResolvedValue(setupWith({ settingsVersion: version }));
    renderHarness();
    await screen.findByText("Save policy");

    expect((screen.getByRole("button", { name: "Save policy" }) as HTMLButtonElement).disabled).toBe(true);
    fireEvent.submit(firstInput().closest("form")!);

    expect(await screen.findAllByText(/Refresh settings/)).not.toHaveLength(0);
    expect(mockUpdatePolicy).not.toHaveBeenCalled();
  });

  it("on 409 shows recovery, keeps the draft, and does not retry", async () => {
    mockGetSetup.mockResolvedValue(setupWith());
    mockUpdatePolicy.mockRejectedValue(new ApiError(409, undefined, "Conflict."));
    renderHarness();
    await screen.findByText("Save policy");

    await userEvent.clear(firstInput());
    await userEvent.type(firstInput(), "30");
    await userEvent.click(screen.getByRole("button", { name: "Save policy" }));

    expect(await screen.findByText(/Someone else changed these settings/)).toBeTruthy();
    expect(screen.getByRole("button", { name: "Refresh settings" })).toBeTruthy();
    expect(firstInput().value).toBe("30");
    expect(mockUpdatePolicy).toHaveBeenCalledTimes(1);
  });

  it("Refresh settings refetches, keeps the unsaved draft over changed server values, and uses the new version", async () => {
    mockGetSetup.mockResolvedValueOnce(setupWith());
    mockUpdatePolicy.mockRejectedValueOnce(new ApiError(409, undefined, "Conflict."));
    renderHarness();
    await screen.findByText("Save policy");
    await userEvent.clear(firstInput());
    await userEvent.type(firstInput(), "30");
    await userEvent.click(screen.getByRole("button", { name: "Save policy" }));
    await screen.findByText(/Someone else changed these settings/);

    mockGetSetup.mockResolvedValueOnce(setupWith({ settingsVersion: "v2" }, 90));
    await userEvent.click(screen.getByRole("button", { name: "Refresh settings" }));

    expect(await screen.findByText(/Your unsaved edits are still shown/)).toBeTruthy();
    expect(screen.queryByText(/Someone else changed these settings/)).toBeNull();
    expect(firstInput().value).toBe("30");
    expect(mockGetSetup).toHaveBeenCalledTimes(2);
    expect(mockUpdatePolicy).toHaveBeenCalledTimes(1);

    mockUpdatePolicy.mockResolvedValueOnce(setupWith({ settingsVersion: "v3" }, 30));
    await userEvent.click(screen.getByRole("button", { name: "Save policy" }));
    await waitFor(() => expect(mockUpdatePolicy).toHaveBeenCalledTimes(2));
    expect(mockUpdatePolicy.mock.calls[1][0].settingsVersion).toBe("v2");
  });

  it("resyncs an untouched form from a server change but not a dirty field", async () => {
    mockGetSetup.mockResolvedValueOnce(setupWith());
    mockUpdatePolicy.mockRejectedValueOnce(new ApiError(409, undefined, "Conflict."));
    renderHarness();
    await screen.findByText("Save policy");
    const inputs = screen.getAllByRole("spinbutton") as HTMLInputElement[];
    await userEvent.clear(inputs[0]);
    await userEvent.type(inputs[0], "30");
    await userEvent.click(screen.getByRole("button", { name: "Save policy" }));
    await screen.findByText(/Someone else changed these settings/);

    mockGetSetup.mockResolvedValueOnce({
      ...setupWith({ settingsVersion: "v2" }, 90),
      responsePolicy: { ...setupWith().responsePolicy, firstResponseTargetMinutes: 90, standardResponseTargetMinutes: 500 },
    });
    await userEvent.click(screen.getByRole("button", { name: "Refresh settings" }));

    await waitFor(() => expect(inputs[1].value).toBe("500"));
    expect(inputs[0].value).toBe("30");
  });
});
