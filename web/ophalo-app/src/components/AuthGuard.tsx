import { useQuery } from "@tanstack/react-query";
import { api, ApiError } from "../lib/apiClient";

const PUBLIC_BASE = import.meta.env.VITE_PUBLIC_BASE_URL;

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
      window.location.href = `${PUBLIC_BASE}/signin`;
      return null;
    }
  }

  if (!data) return null;

  return <>{children}</>;
}
