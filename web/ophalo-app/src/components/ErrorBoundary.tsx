import { Component, type ReactNode } from "react";
import { captureHandledError } from "../lib/sentry";

interface ErrorBoundaryProps {
  children: ReactNode;
}

interface ErrorBoundaryState {
  hasError: boolean;
}

// Root render-error catch (GAP-031). Deliberately shows no exception message, request/customer
// data, or stack trace, and offers Reload as the only recovery action.
export class ErrorBoundary extends Component<ErrorBoundaryProps, ErrorBoundaryState> {
  state: ErrorBoundaryState = { hasError: false };

  static getDerivedStateFromError(): ErrorBoundaryState {
    return { hasError: true };
  }

  // React swallows errors caught here, so the Sentry SDK's global handlers never see them.
  // Forward the exception through the scrubbed capture path (GAP-039); the user-facing
  // fallback below is unchanged and still shows no exception detail.
  componentDidCatch(error: Error): void {
    captureHandledError(error);
  }

  handleReload = (): void => {
    window.location.reload();
  };

  render(): ReactNode {
    if (this.state.hasError) {
      return (
        <div className="min-h-screen flex items-center justify-center bg-[var(--ophalo-canvas)] px-4">
          <div className="max-w-sm w-full rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] p-6 text-center">
            <p className="text-base font-semibold text-[var(--ophalo-ink)] mb-2">Something went wrong</p>
            <p className="text-sm text-[var(--ophalo-muted)] mb-4">
              Reload the page to continue.
            </p>
            <button
              type="button"
              onClick={this.handleReload}
              className="inline-flex items-center justify-center rounded-lg bg-[var(--keep-accent)] px-4 py-2 text-sm font-semibold text-white hover:bg-[var(--keep-accent-hover)] transition-colors"
            >
              Reload
            </button>
          </div>
        </div>
      );
    }
    return this.props.children;
  }
}
