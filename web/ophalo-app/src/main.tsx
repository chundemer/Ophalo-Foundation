import { StrictMode, type FC } from "react";
import { createRoot } from "react-dom/client";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { App } from "./App";
import { ErrorBoundary } from "./components/ErrorBoundary";
import { ConfigurationError } from "./components/ConfigurationError";
import { publicBaseUrlResult } from "./lib/publicBaseUrl";
import { initSentry } from "./lib/sentry";
import "./styles/app.css";

// Errors-only, no-PII capture (GAP-039). Before any render so a configuration-error screen
// or an early bootstrap throw is still reported. No-op without VITE_SENTRY_DSN.
initSentry();

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      refetchOnWindowFocus: true,
      refetchOnReconnect: true,
    },
  },
});

async function bootstrap() {
  const root = createRoot(document.getElementById("root")!);

  // Fail safe on invalid client configuration (GAP-039 Batch 2a): render a static screen
  // rather than letting a consumer operate on a missing/malformed public base URL.
  if (!publicBaseUrlResult.ok) {
    root.render(
      <StrictMode>
        <ConfigurationError />
      </StrictMode>,
    );
    return;
  }

  let MockOverlay: FC | null = null;

  if (import.meta.env.VITE_OPHALO_MOCK_WORKBENCH === "true") {
    const { installMockApi } = await import("./mocks/mockApiClient");
    installMockApi();
    const mod = await import("./mocks/MockWorkbenchOverlay");
    MockOverlay = mod.MockWorkbenchOverlay;
  }

  root.render(
    <StrictMode>
      <QueryClientProvider client={queryClient}>
        <ErrorBoundary>
          <App />
        </ErrorBoundary>
        {MockOverlay && <MockOverlay />}
      </QueryClientProvider>
    </StrictMode>,
  );
}

void bootstrap();
