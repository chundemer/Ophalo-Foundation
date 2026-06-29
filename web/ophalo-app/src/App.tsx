import { useState } from "react";
import { AuthGuard } from "./components/AuthGuard";
import { QuickCapture } from "./components/QuickCapture";
import { Home } from "./pages/Home";
import { Plus } from "lucide-react";

// Shell-level access flags (isReadOnly, isPastDue) are intentionally not derived here.
// GET /keep/setup/onboarding checks Keep.SettingsManage before account access, so Operators
// always get 403 from that endpoint regardless of commercial state. No endpoint in the current
// system reliably returns a role-neutral 402 or 403 that distinguishes commercial-block from
// permission-denied. Both flags are props on QuickCapture for a future caller that has a
// reliable source (e.g. role in session claims, a dedicated access endpoint).

function AppShell() {
  const [captureOpen, setCaptureOpen] = useState(false);

  function openCapture() {
    setCaptureOpen(true);
  }

  return (
    <div className="flex min-h-screen bg-slate-50">
      {/* Left sidebar - desktop */}
      <aside className="hidden md:flex md:flex-col md:w-56 lg:w-64 md:shrink-0 bg-white border-r border-slate-200">
        <div className="px-4 py-5 border-b border-slate-100">
          <span className="font-serif text-base font-semibold text-slate-900">OpHalo Keep</span>
        </div>
        <div className="flex-1" />
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
        <Home onStartCapture={openCapture} />
      </main>

      {/* Sticky FAB - mobile only */}
      <button
        type="button"
        onClick={openCapture}
        aria-label="New Request"
        className="md:hidden fixed bottom-6 right-6 z-30 flex h-14 w-14 items-center justify-center rounded-full bg-slate-900 text-white shadow-lg hover:bg-slate-700 focus:outline-none focus:ring-2 focus:ring-slate-500 focus:ring-offset-2"
      >
        <Plus className="h-6 w-6" />
      </button>

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
