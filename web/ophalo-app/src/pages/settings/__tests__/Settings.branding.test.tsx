import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, fireEvent } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { Settings } from "../../Settings";
import type { KeepSetupResult, IntakeStatusResult } from "../../../lib/apiClient";

// GAP-036a: branding settings draft is lifted into Settings.tsx and shared with the
// public-link preview, which must never present unsaved edits as live.

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

describe("Settings branding draft and preview", () => {
  it("reflects unsaved business-name and logo edits in the preview without saving", async () => {
    const user = userEvent.setup();
    renderSettings();

    await screen.findByLabelText("Business name");

    // Preview starts from initials fallback for the loaded business name.
    expect(await screen.findByText("AH")).toBeInTheDocument();

    const nameInput = screen.getByLabelText("Business name");
    await user.clear(nameInput);
    await user.type(nameInput, "Zenith Plumbing Co");

    // Preview updates live from the draft — new initials, no save fired.
    expect(await screen.findByText("ZP")).toBeInTheDocument();
    expect(screen.getByText("Zenith Plumbing Co")).toBeInTheDocument();
    expect(mockUpdateProfile).not.toHaveBeenCalled();
  });

  it("shows the configured logo instead of initials once a logo URL is drafted", async () => {
    const user = userEvent.setup();
    renderSettings();

    await screen.findByLabelText("Business name");
    expect(await screen.findByText("AH")).toBeInTheDocument();

    const logoInput = screen.getByLabelText("Logo URL");
    await user.type(logoInput, "https://example.com/logo.png");

    await waitFor(() => {
      expect(screen.queryByText("AH")).not.toBeInTheDocument();
    });
    expect(screen.getByRole("img", { name: /logo/i })).toHaveAttribute(
      "src",
      "https://example.com/logo.png",
    );
  });

  it("falls back to initials when the drafted logo URL fails to load, and retries on a new URL", async () => {
    const user = userEvent.setup();
    renderSettings();

    await screen.findByLabelText("Business name");
    const logoInput = screen.getByLabelText("Logo URL");
    await user.type(logoInput, "https://example.com/broken.png");

    const img = await screen.findByRole("img", { name: /logo/i });
    fireEvent.error(img);

    // Failed image is replaced by the initials fallback, not left hidden.
    expect(await screen.findByText("AH")).toBeInTheDocument();
    expect(screen.queryByRole("img", { name: /logo/i })).not.toBeInTheDocument();

    // A different drafted URL is retried rather than staying stuck on the fallback.
    await user.clear(logoInput);
    await user.type(logoInput, "https://example.com/working.png");

    expect(await screen.findByRole("img", { name: /logo/i })).toHaveAttribute(
      "src",
      "https://example.com/working.png",
    );
    expect(screen.queryByText("AH")).not.toBeInTheDocument();
  });

  it("resyncs the draft to the trimmed saved values after a successful save", async () => {
    const user = userEvent.setup();
    const saved: KeepSetupResult = { ...baseSetup, businessName: "Trimmed Co" };
    mockUpdateProfile.mockResolvedValue(saved);
    renderSettings();

    const nameInput = await screen.findByLabelText("Business name");
    await user.clear(nameInput);
    await user.type(nameInput, "  Trimmed Co  ");
    await user.click(screen.getByRole("button", { name: /save company/i }));

    await waitFor(() => expect(mockUpdateProfile).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(nameInput).toHaveValue("Trimmed Co"));
  });

  it("surfaces the server's invalid-URL error while preserving the typed draft", async () => {
    const user = userEvent.setup();
    const { ApiError } = await vi.importActual<typeof import("../../../lib/apiClient")>(
      "../../../lib/apiClient",
    );
    mockUpdateProfile.mockRejectedValue(
      new ApiError(400, "logo_url_invalid", "Logo URL must be an HTTPS URL."),
    );
    renderSettings();

    // Syntactically valid per the input's own type="url" constraint (so the browser
    // lets the submit through) but non-HTTPS, which only the server rejects.
    const logoInput = await screen.findByLabelText("Logo URL");
    await user.type(logoInput, "http://example.com/logo.png");
    await user.click(screen.getByRole("button", { name: /save company/i }));

    expect(await screen.findByText("Logo URL must be an HTTPS URL.")).toBeInTheDocument();
    expect(logoInput).toHaveValue("http://example.com/logo.png");
  });
});
