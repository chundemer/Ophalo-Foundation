import { describe, it, expect, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { createElement } from "react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useBusinessTimeZone } from "../useBusinessTimeZone";
import { mockMeByRole } from "../../mocks/fixtures";

const mockGetMe = vi.fn();

vi.mock("../../lib/apiClient", async () => {
  const actual = await vi.importActual<typeof import("../../lib/apiClient")>("../../lib/apiClient");
  return { ...actual, api: { ...actual.api, getMe: () => mockGetMe() } };
});

function wrapper() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: React.ReactNode }) =>
    createElement(QueryClientProvider, { client }, children);
}

describe("useBusinessTimeZone", () => {
  it("is null while ['me'] is unresolved, then resolves to the account's IANA zone", async () => {
    // GAP-092: sourced from the role-agnostic ['me'] query, not the Owner/Admin-only setup
    // endpoint — this must resolve for an Operator, the role that most needs the correct cue.
    mockGetMe.mockResolvedValue({ ...mockMeByRole.operator, timeZone: "America/Denver" });
    const { result } = renderHook(() => useBusinessTimeZone(), { wrapper: wrapper() });

    expect(result.current.timeZone).toBeNull();
    await waitFor(() => expect(result.current.timeZone).toBe("America/Denver"));
  });

  it("stays null (neutral) rather than guessing when the ['me'] fetch fails", async () => {
    mockGetMe.mockRejectedValue(new Error("network"));
    const { result } = renderHook(() => useBusinessTimeZone(), { wrapper: wrapper() });

    await waitFor(() => expect(mockGetMe).toHaveBeenCalled());
    expect(result.current.timeZone).toBeNull();
  });
});
