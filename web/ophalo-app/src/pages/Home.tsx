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
    <div className="mx-auto w-full max-w-[1440px] px-4 pt-6 pb-10 sm:px-6 sm:pt-8">
      <div className="max-w-xl">
        <h1 className="keep-page-title tracking-tight">Your workspace</h1>
        <p className="mt-1.5 keep-page-subtitle">
          Head to Requests to view and manage customer requests.
        </p>
      </div>
    </div>
  );
}

function OwnerHome({ onStartCapture, onNavigateSettings }: Omit<HomeProps, "role" | "onNavigateRequests">) {
  const cardClass =
    "w-full text-left rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-4 py-3.5 shadow-sm transition-colors hover:bg-[var(--keep-accent-bg)]/35 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)]";
  return (
    <div className="mx-auto w-full max-w-[1440px] px-4 pt-6 pb-10 sm:px-6 sm:pt-8">
      <div className="max-w-xl space-y-6">
        <div>
          <h1 className="keep-page-title tracking-tight">Getting started</h1>
          <p className="mt-1.5 keep-page-subtitle">
            Keep is ready. Verify your public request link, add your first customer request, and invite teammates when you need them.
          </p>
        </div>
        <div className="space-y-3">
          <button type="button" onClick={() => onNavigateSettings("public-profile")} className={cardClass}>
            <p className="text-sm font-medium text-[var(--ophalo-ink)]">Verify your public request link</p>
            <p className="text-xs text-[var(--ophalo-muted)] mt-0.5">Your intake link is ready — copy and share it from Settings.</p>
          </button>
          <button type="button" onClick={onStartCapture} className={cardClass}>
            <p className="text-sm font-medium text-[var(--ophalo-ink)]">Add your first customer request</p>
            <p className="text-xs text-[var(--ophalo-muted)] mt-0.5">Use Quick Capture to log a request and see how Keep works.</p>
          </button>
          <button type="button" onClick={() => onNavigateSettings("team")} className={cardClass}>
            <p className="text-sm font-medium text-[var(--ophalo-ink)]">Invite teammates — when you're ready</p>
            <p className="text-xs text-[var(--ophalo-muted)] mt-0.5">Keep works great for solo businesses. Add team members in Settings when you need them.</p>
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
