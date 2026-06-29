import { useState, useCallback } from "react";
import { useQuery } from "@tanstack/react-query";
import { AuthGuard } from "./components/AuthGuard";
import { QuickCapture } from "./components/QuickCapture";
import { Home } from "./pages/Home";
import { Requests } from "./pages/Requests";
import { RequestDetail } from "./pages/RequestDetail";
import { AccessLimited } from "./pages/AccessLimited";
import { Plus, Inbox } from "lucide-react";
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
  | { page: "detail"; requestId: string };

interface NavItem {
  id: "home" | "requests";
  label: string;
  icon: React.ReactNode;
}

function getNavItems(role: AccountRole): NavItem[] {
  const items: NavItem[] = [
    { id: "requests", label: "Requests", icon: <Inbox className="h-4 w-4" /> },
  ];
  // Home / getting-started is only surfaced for owner/admin (setup is their concern)
  if (role === "owner" || role === "admin") {
    items.push({ id: "home", label: "Getting Started", icon: null });
  }
  return items;
}

function AppShell() {
  const [captureOpen, setCaptureOpen] = useState(false);
  const [route, setRoute] = useState<AppRoute>({ page: "requests" });
  const [viewCounts, setViewCounts] = useState<KeepRequestViewCounts | null>(null);
  const handleViewCountsUpdate = useCallback(setViewCounts, []);

  const { data: me } = useQuery({
    queryKey: ["me"],
    queryFn: api.getMe,
    staleTime: 5 * 60 * 1000,
  });

  const role: AccountRole = me?.accountRole ?? "unknown";
  const navItems = getNavItems(role);

  function openCapture() {
    setCaptureOpen(true);
  }

  function selectRequest(requestId: string) {
    setRoute({ page: "detail", requestId });
  }

  function backToRequests() {
    setRoute({ page: "requests" });
  }

  const activeNavId: "home" | "requests" =
    route.page === "home" ? "home" : "requests";

  return (
    <div className="flex min-h-screen bg-slate-50">
      {/* Left sidebar — desktop */}
      <aside className="hidden md:flex md:flex-col md:w-56 lg:w-64 md:shrink-0 bg-white border-r border-slate-200">
        <div className="px-4 py-5 border-b border-slate-100">
          <span className="font-serif text-base font-semibold text-slate-900">OpHalo Keep</span>
        </div>
        <nav className="flex-1 px-3 py-4 space-y-0.5">
          {navItems.map((item) => (
            <button
              key={item.id}
              type="button"
              onClick={() => setRoute({ page: item.id })}
              className={`w-full flex items-center gap-2.5 rounded-md px-3 py-2 text-sm font-medium text-left transition-colors ${
                activeNavId === item.id
                  ? "bg-slate-100 text-slate-900"
                  : "text-slate-600 hover:bg-slate-50 hover:text-slate-900"
              }`}
            >
              {item.icon}
              <span>{item.label}</span>
              {item.id === "requests" && viewCounts != null && (() => {
                const total = (role === "owner" || role === "admin")
                  ? viewCounts.default
                  : viewCounts.assignedToMe + viewCounts.needsAttention;
                return total > 0 ? (
                  <span className="ml-auto text-xs font-medium bg-slate-200 text-slate-700 rounded-full px-1.5 py-0.5">
                    {total}
                  </span>
                ) : null;
              })()}
            </button>
          ))}
        </nav>
        <div className="px-3 pb-5">
          <button
            type="button"
            onClick={openCapture}
            className="w-full flex items-center justify-center gap-2 rounded-md bg-slate-900 px-3 py-2.5 text-sm font-medium text-white hover:bg-slate-700"
          >
            <Plus className="h-4 w-4" />
            New Request
          </button>
        </div>
      </aside>

      {/* Main content */}
      <main className="flex-1 min-w-0 flex flex-col">
        {route.page === "requests" && role === "unknown" && (
          <div className="flex flex-1 items-center justify-center">
            <span className="text-slate-400 text-sm">Loading…</span>
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
        {route.page === "home" && <Home onStartCapture={openCapture} />}
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
          className="md:hidden fixed bottom-6 right-6 z-30 flex h-14 w-14 items-center justify-center rounded-full bg-slate-900 text-white shadow-lg hover:bg-slate-700 focus:outline-none focus:ring-2 focus:ring-slate-500 focus:ring-offset-2"
        >
          <Plus className="h-6 w-6" />
        </button>
      )}

      {/* Quick Capture modal/drawer */}
      {captureOpen && <QuickCapture onClose={() => setCaptureOpen(false)} />}
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
