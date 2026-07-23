import { useCallback, useEffect, useState } from "react";

export type SessionCheck = "checking" | "unauthenticated" | "authenticated" | "error";

/**
 * Shared by /start and /signin: checks the auth cookie against /auth/me and,
 * once confirmed authenticated, redirects to the app rather than showing the
 * form. `unauthenticated` and `error` states are left to the caller to render.
 */
export function useSessionRedirect(): { sessionCheck: SessionCheck; retry: () => void } {
  const [sessionCheck, setSessionCheck] = useState<SessionCheck>("checking");

  const checkSession = useCallback(async () => {
    setSessionCheck("checking");
    try {
      const res = await fetch(`${process.env.NEXT_PUBLIC_API_BASE_URL}/auth/me`, {
        credentials: "include",
      });
      if (res.ok) {
        setSessionCheck("authenticated");
      } else if (res.status === 401) {
        setSessionCheck("unauthenticated");
      } else {
        setSessionCheck("error");
      }
    } catch {
      setSessionCheck("error");
    }
  }, []);

  useEffect(() => {
    checkSession();
  }, [checkSession]);

  useEffect(() => {
    if (sessionCheck === "authenticated") {
      window.location.assign(
        process.env.NEXT_PUBLIC_APP_BASE_URL ?? "http://localhost:5173",
      );
    }
  }, [sessionCheck]);

  return { sessionCheck, retry: checkSession };
}
