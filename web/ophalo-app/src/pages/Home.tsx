import { type AccountRole } from "../lib/apiClient";
import { AccessLimited } from "./AccessLimited";

interface HomeProps {
  onStartCapture: () => void;
  role: AccountRole;
  onNavigateSettings: (section?: "public-profile" | "policy" | "team") => void;
  onNavigateRequests: () => void;
}

function OperatorHome() {
  return (
    <div className="py-12 px-4">
      <div className="mx-auto max-w-lg">
        <h1 className="font-serif text-2xl font-semibold text-slate-900 mb-2">
          Your workspace
        </h1>
        <p className="text-slate-500 text-sm">
          Head to Requests to view and manage customer requests.
        </p>
      </div>
    </div>
  );
}

function OwnerHome({ onStartCapture, onNavigateSettings }: Omit<HomeProps, "role" | "onNavigateRequests">) {
  return (
    <div className="py-12 px-4">
      <div className="mx-auto max-w-lg space-y-6">
        <div>
          <h1 className="font-serif text-2xl font-semibold text-slate-900 mb-2">
            Getting started
          </h1>
          <p className="text-slate-500 text-sm">
            Keep is ready. Verify your public request link, add your first customer request, and invite teammates when you need them.
          </p>
        </div>
        <div className="space-y-3">
          <button
            type="button"
            onClick={() => onNavigateSettings("public-profile")}
            className="w-full text-left rounded-lg border border-slate-200 bg-white px-4 py-3 hover:border-slate-300 hover:bg-slate-50 transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)]"
          >
            <p className="text-sm font-medium text-slate-900">Verify your public request link</p>
            <p className="text-xs text-slate-500 mt-0.5">Your intake link is ready — copy and share it from Settings.</p>
          </button>
          <button
            type="button"
            onClick={onStartCapture}
            className="w-full text-left rounded-lg border border-slate-200 bg-white px-4 py-3 hover:border-slate-300 hover:bg-slate-50 transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)]"
          >
            <p className="text-sm font-medium text-slate-900">Add your first customer request</p>
            <p className="text-xs text-slate-500 mt-0.5">Use Quick Capture to log a request and see how Keep works.</p>
          </button>
          <button
            type="button"
            onClick={() => onNavigateSettings("team")}
            className="w-full text-left rounded-lg border border-slate-200 bg-white px-4 py-3 hover:border-slate-300 hover:bg-slate-50 transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)]"
          >
            <p className="text-sm font-medium text-slate-900">Invite teammates — when you're ready</p>
            <p className="text-xs text-slate-500 mt-0.5">Keep works great for solo businesses. Add team members in Settings when you need them.</p>
          </button>
        </div>
      </div>
    </div>
  );
}

export function Home({ onStartCapture, role, onNavigateSettings, onNavigateRequests: _ }: HomeProps) {
  if (role === "viewer") return <AccessLimited />;
  if (role === "operator") return <OperatorHome />;
  if (role === "unknown") return null;
  return (
    <OwnerHome
      onStartCapture={onStartCapture}
      onNavigateSettings={onNavigateSettings}
    />
  );
}
