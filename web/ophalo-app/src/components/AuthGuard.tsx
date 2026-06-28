import { useQuery } from "@tanstack/react-query";
import { api, ApiError } from "../lib/apiClient";

const PUBLIC_BASE = import.meta.env.VITE_PUBLIC_BASE_URL;

/**
 * Builds the return_to value for the auth redirect.
 *
 * Always emits a path-only value (never an origin or external URL) so the
 * public auth entry can accept it without open-redirect risk. ophalo-web is
 * responsible for validating that the path is rooted under AppBaseUrl before
 * routing back — see build-log/067 §Return-To Auth Redirects.
 */
function currentReturnTo(): string {
  return window.location.pathname + window.location.search;
}

export function AuthGuard({ children }: { children: React.ReactNode }) {
  const { data, isLoading, error } = useQuery({
    queryKey: ["me"],
    queryFn: api.getMe,
    retry: false,
    staleTime: 60_000,
  });

  if (isLoading) {
    return (
      <div className="flex min-h-screen items-center justify-center">
        <span className="text-slate-500 text-sm">Loading…</span>
      </div>
    );
  }

  if (error) {
    const is401 =
      error instanceof ApiError && error.status === 401;

    if (is401 || !data?.isAuthenticated) {
      const returnTo = currentReturnTo();
      const params = returnTo !== "/" ? `?return_to=${encodeURIComponent(returnTo)}` : "";
      window.location.href = `${PUBLIC_BASE}/auth/signin${params}`;
      return null;
    }
  }

  if (!data) return null;

  return <>{children}</>;
}
