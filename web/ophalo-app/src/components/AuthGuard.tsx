import { useQuery } from "@tanstack/react-query";
import { api, ApiError } from "../lib/apiClient";
import { redirectToSignInOnce } from "../lib/redirectToSignIn";

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
    const is401 = error instanceof ApiError && error.status === 401;

    if (is401 || !data?.isAuthenticated) {
      // Same guarded redirect the apiClient wrappers use, so a 401 that surfaces
      // through both the initial /auth/me and this guard navigates only once.
      redirectToSignInOnce();
      return null;
    }
  }

  if (!data) return null;

  return <>{children}</>;
}
