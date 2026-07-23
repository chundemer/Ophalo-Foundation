import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { Settings } from "../../Settings";
import type { KeepSetupResult, IntakeStatusResult } from "../../../lib/apiClient";

// GAP-051: the business's own "Customer-facing phone" field in Settings was still
// showing/submitting raw digits — this covers the same as-you-type formatting fix
// applied to the customer-facing Quick Capture surfaces.

const mockGetSetup = vi.fn();
const mockUpdateProfile = vi.fn();
const mockGetIntake = vi.fn();

vi.mock("../../../lib/apiClient", async () => {
  const actual = await vi.importActual<typeof import("../../../lib/apiClient")>(
    "../../../lib/apiClient",
  );
  return {
    ...actual,
    api: {
      ...actual.api,
      getSetup: (...args: unknown[]) => mockGetSetup(...args),
      updateProfile: (...args: unknown[]) => mockUpdateProfile(...args),
      getIntake: (...args: unknown[]) => mockGetIntake(...args),
    },
  };
});

const baseSetup: KeepSetupResult = {
  businessName: "Apex Home Services",
  timeZone: "America/Chicago",
  customerFacingPhone: "5555550100",
  customerFacingEmail: "hello@apexhomeservices.example",
  logoUrl: null,
  websiteUrl: null,
  responsePolicy: {
    firstResponseTargetMinutes: 60,
    standardResponseTargetMinutes: 240,
    priorityResponseTargetMinutes: 30,
    statusCheckThresholdDays: 3,
  },
};

const activeIntake: IntakeStatusResult = {
  hasActiveLink: true,
  publicSlug: "apex-home-services",
  createdAtUtc: "2026-07-01T00:00:00Z",
};

function renderSettings() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <Settings callerRole="owner" />
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  mockGetSetup.mockReset();
  mockUpdateProfile.mockReset();
  mockGetIntake.mockReset();
  mockGetSetup.mockResolvedValue(baseSetup);
  mockGetIntake.mockResolvedValue(activeIntake);
});

describe("CompanySection customer-facing phone formatting", () => {
  it("displays the existing saved phone formatted, not as raw digits", async () => {
    renderSettings();

    const input = await screen.findByLabelText("Customer-facing phone");
    expect(input).toHaveValue("(555) 555-0100");
  });

  it("formats a newly typed number and saves the canonical digits", async () => {
    const user = userEvent.setup();
    mockUpdateProfile.mockResolvedValue({ ...baseSetup, customerFacingPhone: "5555559999" });
    renderSettings();

    const input = await screen.findByLabelText("Customer-facing phone");
    await user.clear(input);
    await user.type(input, "5555559999");
    expect(input).toHaveValue("(555) 555-9999");

    await user.click(screen.getByRole("button", { name: "Save company" }));

    await waitFor(() =>
      expect(mockUpdateProfile).toHaveBeenCalledWith(
        expect.objectContaining({ customerFacingPhone: "5555559999" }),
      ),
    );
  });

  it("drops a leading +1 typed into the field before saving", async () => {
    const user = userEvent.setup();
    mockUpdateProfile.mockResolvedValue(baseSetup);
    renderSettings();

    const input = await screen.findByLabelText("Customer-facing phone");
    await user.clear(input);
    await user.type(input, "+15555559999");
    expect(input).toHaveValue("(555) 555-9999");
  });
});
