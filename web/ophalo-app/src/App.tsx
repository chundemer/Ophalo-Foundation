import { useState, useCallback, useEffect } from "react";
import { useQuery } from "@tanstack/react-query";
import { AuthGuard } from "./components/AuthGuard";
import { QuickCapture } from "./components/QuickCapture";
import { KeepButton } from "./components/keep/KeepButton";
import { Home } from "./pages/Home";
import { Requests } from "./pages/Requests";
import { RequestDetail } from "./pages/RequestDetail";
import { AccessLimited } from "./pages/AccessLimited";
import { Settings } from "./pages/Settings";
import { Plus, Inbox, Settings as SettingsIcon } from "lucide-react";
import { api, type AccountRole, type KeepRequestViewCounts } from "./lib/apiClient";

// Shell-level access flags (isReadOnly, isPastDue) are intentionally not derived here.
// GET /keep/setup/onboarding checks Keep.SettingsManage before account access, so Operators
// always get 403 from that endpoint regardless of commercial state. No endpoint in the current
// system reliably returns a role-neutral 402 or 403 that distinguishes commercial-block from
// permission-denied. Both flags are props on QuickCapture for a future caller that has a
// reliable source (e.g. role in session claims, a dedicated access endpoint).

type AppRoute =
  | { page: "home" }
  | { page: "requests" }
  | { page: "settings"; section?: "public-profile" | "policy" | "team" }
  | { page: "detail"; requestId: string };

function getRouteFromLocation(): AppRoute {
  const match = window.location.hash.match(/^#\/request\/(.+)$/);
  if (match?.[1]) return { page: "detail", requestId: match[1] };
  return { page: "requests" };
}

interface NavItem {
  id: "home" | "requests" | "settings";
  label: string;
  icon: React.ReactNode;
}

function getNavItems(role: AccountRole): NavItem[] {
  const items: NavItem[] = [
    { id: "requests", label: "Requests", icon: <Inbox className="h-4 w-4" /> },
  ];
  if (role === "owner" || role === "admin") {
    items.push({ id: "home", label: "Getting Started", icon: null });
    items.push({ id: "settings", label: "Settings", icon: <SettingsIcon className="h-4 w-4" /> });
  }
  return items;
}

function roleLabel(role: AccountRole): string {
  switch (role) {
    case "owner": return "Owner";
    case "admin": return "Admin";
    case "operator": return "Operator";
    case "viewer": return "Viewer";
    default: return "";
  }
}

function AppShell() {
  const [captureOpen, setCaptureOpen] = useState(false);
  const [route, setRoute] = useState<AppRoute>(getRouteFromLocation);
  const [viewCounts, setViewCounts] = useState<KeepRequestViewCounts | null>(null);
  const handleViewCountsUpdate = useCallback(setViewCounts, []);

  useEffect(() => {
    function onPopState() {
      setRoute(getRouteFromLocation());
    }
    window.addEventListener("popstate", onPopState);
    return () => window.removeEventListener("popstate", onPopState);
  }, []);

  const { data: me } = useQuery({
    queryKey: ["me"],
    queryFn: api.getMe,
    staleTime: 5 * 60 * 1000,
  });

  const role: AccountRole = me?.accountRole ?? "unknown";
  const navItems = getNavItems(role);

  function navigate(newRoute: AppRoute) {
    const base = window.location.pathname + window.location.search;
    if (newRoute.page === "detail") {
      history.pushState(null, "", `${base}#/request/${newRoute.requestId}`);
    } else {
      history.pushState(null, "", base);
    }
    setRoute(newRoute);
  }

  function openCapture() {
    setCaptureOpen(true);
  }

  function navigateToSettings(section?: "public-profile" | "policy" | "team") {
    navigate({ page: "settings", section });
  }

  function selectRequest(requestId: string) {
    navigate({ page: "detail", requestId });
  }

  function backToRequests() {
    navigate({ page: "requests" });
  }

  const activeNavId: "home" | "requests" | "settings" =
    route.page === "home" ? "home"
    : route.page === "settings" ? "settings"
    : "requests";

  return (
    <div className="flex min-h-screen bg-[var(--ophalo-canvas)]">
      {/* Left sidebar — desktop */}
      <aside className="hidden md:flex md:flex-col md:w-56 lg:w-64 md:shrink-0 bg-[var(--ophalo-card)] border-r border-[var(--ophalo-border)]">
        <div className="px-4 py-4 border-b border-[var(--ophalo-border)]">
          <button
            type="button"
            onClick={() => navigate({ page: "requests" })}
            aria-label="Go to requests"
            className="block focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2 rounded"
          >
            <img
              src="/brand/ophalo-keep-lockup-color.svg"
              alt="OpHalo Keep"
              className="h-8 w-auto"
              draggable={false}
            />
          </button>
        </div>
        <nav className="flex-1 px-3 py-4 space-y-0.5">
          {navItems.map((item) => (
            <button
              key={item.id}
              type="button"
              onClick={() => navigate({ page: item.id })}
              className={`w-full flex items-center gap-2.5 rounded-md px-3 py-2.5 text-sm text-left transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2 ${
                activeNavId === item.id
                  ? "font-semibold bg-[var(--keep-accent-bg)] text-[var(--ophalo-navy)]"
                  : "font-medium text-[var(--ophalo-muted)] hover:bg-[var(--ophalo-canvas)] hover:text-[var(--ophalo-ink)]"
              }`}
            >
              {item.icon}
              <span>{item.label}</span>
              {item.id === "requests" && viewCounts != null && (() => {
                const total = (role === "owner" || role === "admin")
                  ? viewCounts.default
                  : viewCounts.assignedToMe + viewCounts.needsAttention;
                return total > 0 ? (
                  <span className={`ml-auto text-xs font-semibold rounded-full px-1.5 py-0.5 ${
                    activeNavId === "requests"
                      ? "bg-[var(--keep-accent)] text-white"
                      : "bg-[var(--keep-accent-bg)] text-[var(--keep-accent)]"
                  }`}>
                    {total}
                  </span>
                ) : null;
              })()}
            </button>
          ))}
        </nav>
        <div className="px-3 pb-4">
          <KeepButton
            variant="primary"
            onClick={openCapture}
            className="w-full gap-2"
          >
            <Plus className="h-4 w-4" />
            New Request
          </KeepButton>
        </div>
        {/* Quiet role identity — no business name available from /auth/me */}
        {role !== "unknown" && (
          <div className="px-4 py-3 border-t border-[var(--ophalo-border)]">
            <p className="text-xs text-[var(--ophalo-muted)]">{roleLabel(role)}</p>
          </div>
        )}
      </aside>

      {/* Main content */}
      <main className="flex-1 min-w-0 flex flex-col">
        {route.page === "requests" && role === "unknown" && (
          <div className="flex flex-1 items-center justify-center">
            <span className="text-[var(--ophalo-muted)] text-sm">Loading…</span>
          </div>
        )}
        {route.page === "requests" && role === "viewer" && <AccessLimited />}
        {route.page === "requests" && role !== "unknown" && role !== "viewer" && (
          <Requests
            role={role}
            viewCounts={viewCounts}
            onViewCountsUpdate={handleViewCountsUpdate}
            onSelectRequest={selectRequest}
          />
        )}
        {route.page === "home" && (
          <Home
            onStartCapture={openCapture}
            role={role}
            onNavigateSettings={navigateToSettings}
            onNavigateRequests={() => navigate({ page: "requests" })}
          />
        )}
        {route.page === "settings" && <Settings callerRole={role} scrollToSection={route.section} />}
        {route.page === "detail" && (
          <RequestDetail requestId={route.requestId} onBack={backToRequests} />
        )}
      </main>

      {/* Sticky FAB — mobile only */}
      {route.page !== "detail" && (
        <button
          type="button"
          onClick={openCapture}
          aria-label="New Request"
          className="md:hidden fixed bottom-6 right-6 z-30 flex h-14 w-14 items-center justify-center rounded-full bg-[var(--ophalo-navy)] text-white shadow-lg hover:opacity-90 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2"
        >
          <Plus className="h-6 w-6" />
        </button>
      )}

      {/* Quick Capture modal/drawer */}
      {captureOpen && (
        <QuickCapture
          onClose={() => setCaptureOpen(false)}
          onSelectRequest={(id) => { selectRequest(id); setCaptureOpen(false); }}
        />
      )}
    </div>
  );
}

export function App() {
  return (
    <AuthGuard>
      <AppShell />
    </AuthGuard>
  );
}
