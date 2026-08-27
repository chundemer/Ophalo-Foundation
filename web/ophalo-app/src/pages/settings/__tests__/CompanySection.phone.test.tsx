import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { Settings } from "../../Settings";
import type { KeepSetupResult, IntakeStatusResult, MeResponse } from "../../../lib/apiClient";

// GAP-051: the business's own "Customer-facing phone" field in Settings was still
// showing/submitting raw digits — this covers the same as-you-type formatting fix
// applied to the customer-facing Quick Capture surfaces.

const mockGetSetup = vi.fn();
const mockUpdateProfile = vi.fn();
const mockGetIntake = vi.fn();
const mockGetMe = vi.fn();

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
      getMe: (...args: unknown[]) => mockGetMe(...args),
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

const baseMe: MeResponse = {
  accountUserId: "mock-user-1",
  accountId: "mock-account-1",
  isAuthenticated: true,
  isVerified: true,
  accountRole: "owner",
  businessName: "Apex Home Services",
  userName: "Riley Owner",
};

function renderSettings() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <Settings callerRole="owner" />
    </QueryClientProvider>,
  );
  return queryClient;
}

beforeEach(() => {
  mockGetSetup.mockReset();
  mockUpdateProfile.mockReset();
  mockGetIntake.mockReset();
  mockGetMe.mockReset();
  mockGetSetup.mockResolvedValue(baseSetup);
  mockGetIntake.mockResolvedValue(activeIntake);
  mockGetMe.mockResolvedValue(baseMe);
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

describe("CompanySection [\"me\"] cache sync on save (GAP-042)", () => {
  it("updates [\"setup\"], immediately patches [\"me\"].businessName, and invalidates [\"me\"]", async () => {
    const user = userEvent.setup();
    const updated: KeepSetupResult = { ...baseSetup, businessName: "Acme Plumbing Co" };
    mockUpdateProfile.mockResolvedValue(updated);
    const queryClient = renderSettings();
    queryClient.setQueryData(["me"], baseMe);

    const nameInput = await screen.findByLabelText("Business name");
    await user.clear(nameInput);
    await user.type(nameInput, "Acme Plumbing Co");
    await user.click(screen.getByRole("button", { name: "Save company" }));

    await waitFor(() => expect(mockUpdateProfile).toHaveBeenCalled());

    // Existing ["setup"] cache write is preserved.
    expect(queryClient.getQueryData(["setup"])).toEqual(updated);

    // ["me"].businessName is patched immediately, in place, without waiting on a refetch.
    expect((queryClient.getQueryData(["me"]) as MeResponse).businessName).toBe("Acme Plumbing Co");
    expect((queryClient.getQueryData(["me"]) as MeResponse).accountRole).toBe("owner");

    // ["me"] is invalidated to reconfirm server authority.
    expect(queryClient.getQueryState(["me"])?.isInvalidated).toBe(true);
  });
});
