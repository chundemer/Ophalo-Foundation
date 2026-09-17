import { useQuery } from "@tanstack/react-query";
import { api } from "../lib/apiClient";

// GAP-092 — single source of truth for the account's business IANA timezone. Reads the
// role-agnostic `['me']` query (every authenticated role can read it, unlike the Owner/Admin-only
// `/keep/setup` endpoint that GAP-042 already excludes Operators from) — the first business-time
// consumer initiates the fetch, concurrent consumers join it, later consumers reuse a fresh cached
// value. Never falls back to viewer-local time or a fixed zone — while unresolved, callers must
// render neutral (no guessed) urgency.
export function useBusinessTimeZone(): { timeZone: string | null } {
  const { data } = useQuery({
    queryKey: ["me"],
    queryFn: api.getMe,
    staleTime: 5 * 60_000,
  });

  return { timeZone: data?.timeZone ?? null };
}
