import { describe, it, expect, vi, beforeEach } from "vitest";
import { useState } from "react";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider, useQuery } from "@tanstack/react-query";
import { CompanySection, draftFromSetup, type ProfileDraft } from "../CompanySection";
import { ApiError, type KeepSetupResult } from "../../../lib/apiClient";

// GAP-100 6a-2a (ADR-506): the profile save echoes settingsVersion; a stale save (409) never
// retries, keeps the draft, and offers an explicit Refresh settings action. A version is
// required client-side only when the timezone differs from the stored one.

const mockGetSetup = vi.fn();
const mockUpdateProfile = vi.fn();

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
    ...overrides,
  };
}

// Mirrors Settings.tsx: the ["setup"] query owns the data; the draft lives in the parent.
function Harness() {
  const { data } = useQuery({ queryKey: ["setup"], queryFn: () => mockGetSetup(), staleTime: Infinity });
  const [draft, setDraft] = useState<ProfileDraft | null>(null);
  if (!data) return null;
  const current = draft ?? draftFromSetup(data);
  return (
    <CompanySection
      draft={current}
      onDraftChange={(patch) => setDraft({ ...current, ...patch })}
      onSaved={(saved) => setDraft(draftFromSetup(saved))}
      settingsVersion={data.settingsVersion}
      savedTimeZone={data.timeZone}
    />
  );
}

function renderHarness() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <Harness />
    </QueryClientProvider>,
  );
}

const nameInput = () => screen.getByLabelText("Business name") as HTMLInputElement;
const save = () => userEvent.click(screen.getByRole("button", { name: /^save/i }));

beforeEach(() => {
  mockGetSetup.mockReset();
  mockUpdateProfile.mockReset();
});

describe("CompanySection settingsVersion", () => {
  it("sends the current settingsVersion with the save", async () => {
    mockGetSetup.mockResolvedValue(setupWith());
    mockUpdateProfile.mockResolvedValue(setupWith({ businessName: "Apex 2", settingsVersion: "v2" }));
    renderHarness();
    await screen.findByLabelText("Business name");

    await userEvent.type(nameInput(), " 2");
    await save();

    await waitFor(() => expect(mockUpdateProfile).toHaveBeenCalledTimes(1));
    expect(mockUpdateProfile.mock.calls[0][0]).toMatchObject({ settingsVersion: "v1" });
  });

  it("blocks a timezone change when no settingsVersion is available", async () => {
    mockGetSetup.mockResolvedValue(setupWith({ settingsVersion: undefined }));
    renderHarness();
    await screen.findByLabelText("Business name");

    await userEvent.selectOptions(screen.getByLabelText("Timezone"), "America/New_York");
    await save();

    expect(await screen.findByText(/Settings version isn't available/)).toBeTruthy();
    expect(mockUpdateProfile).not.toHaveBeenCalled();
  });

  it("does not block a profile-only save when no settingsVersion is available", async () => {
    mockGetSetup.mockResolvedValue(setupWith({ settingsVersion: undefined }));
    mockUpdateProfile.mockResolvedValue(setupWith({ businessName: "Apex 2", settingsVersion: undefined }));
    renderHarness();
    await screen.findByLabelText("Business name");

    await userEvent.type(nameInput(), " 2");
    await save();

    await waitFor(() => expect(mockUpdateProfile).toHaveBeenCalledTimes(1));
  });

  it("on 409 shows conflict copy, keeps the draft, does not retry, and Refresh clears it", async () => {
    mockGetSetup.mockResolvedValue(setupWith());
    mockUpdateProfile.mockRejectedValue(new ApiError(409, "KeepResponsePolicy.SettingsVersionMismatch", "stale"));
    renderHarness();
    await screen.findByLabelText("Business name");

    await userEvent.type(nameInput(), " 2");
    await save();

    expect(await screen.findByText(/Someone else changed these settings/)).toBeTruthy();
    expect(nameInput().value).toBe("Apex 2");
    expect(mockUpdateProfile).toHaveBeenCalledTimes(1);

    mockGetSetup.mockResolvedValue(setupWith({ settingsVersion: "v9" }));
    await userEvent.click(screen.getByRole("button", { name: "Refresh settings" }));

    await waitFor(() => expect(screen.queryByText(/Someone else changed these settings/)).toBeNull());
    expect(nameInput().value).toBe("Apex 2");
    expect(mockUpdateProfile).toHaveBeenCalledTimes(1);
  });

  it("shows a non-409 ApiError message", async () => {
    mockGetSetup.mockResolvedValue(setupWith());
    mockUpdateProfile.mockRejectedValue(new ApiError(422, "KeepResponsePolicy.InvalidTimeZone", "Bad timezone"));
    renderHarness();
    await screen.findByLabelText("Business name");

    await userEvent.type(nameInput(), " 2");
    await save();

    expect(await screen.findByText("Bad timezone")).toBeTruthy();
    expect(screen.queryByText(/Someone else changed these settings/)).toBeNull();
  });
});
