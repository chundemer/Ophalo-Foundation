import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { AuthGuard } from "./AuthGuard";
import { ApiError } from "../lib/apiClient";

const mockGetMe = vi.fn();
const mockRedirect = vi.fn();

vi.mock("../lib/apiClient", async () => {
  const actual = await vi.importActual<typeof import("../lib/apiClient")>("../lib/apiClient");
  return {
    ...actual,
    api: { ...actual.api, getMe: (...args: unknown[]) => mockGetMe(...args) },
  };
});

vi.mock("../lib/redirectToSignIn", () => ({
  redirectToSignInOnce: (...args: unknown[]) => mockRedirect(...args),
}));

function renderGuard() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <AuthGuard>
        <div>protected content</div>
      </AuthGuard>
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  mockGetMe.mockReset();
  mockRedirect.mockReset();
});

describe("AuthGuard", () => {
  it("renders children for an authenticated session and does not redirect", async () => {
    mockGetMe.mockResolvedValue({ isAuthenticated: true, accountRole: "owner" });

    renderGuard();

    expect(await screen.findByText("protected content")).toBeInTheDocument();
    expect(mockRedirect).not.toHaveBeenCalled();
  });

  it("redirects via the shared guarded function when /auth/me returns 401", async () => {
    mockGetMe.mockRejectedValue(new ApiError(401, undefined, "API 401 /auth/me"));

    renderGuard();

    await waitFor(() => expect(mockRedirect).toHaveBeenCalledTimes(1));
    expect(screen.queryByText("protected content")).not.toBeInTheDocument();
  });
});
