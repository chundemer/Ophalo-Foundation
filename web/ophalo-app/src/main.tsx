import { StrictMode, type FC } from "react";
import { createRoot } from "react-dom/client";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { App } from "./App";
import "./styles/app.css";

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      refetchOnWindowFocus: true,
      refetchOnReconnect: true,
    },
  },
});

async function bootstrap() {
  let MockOverlay: FC | null = null;

  if (import.meta.env.VITE_OPHALO_MOCK_WORKBENCH === "true") {
    const { installMockApi } = await import("./mocks/mockApiClient");
    installMockApi();
    const mod = await import("./mocks/MockWorkbenchOverlay");
    MockOverlay = mod.MockWorkbenchOverlay;
  }

  createRoot(document.getElementById("root")!).render(
    <StrictMode>
      <QueryClientProvider client={queryClient}>
        <App />
        {MockOverlay && <MockOverlay />}
      </QueryClientProvider>
    </StrictMode>,
  );
}

void bootstrap();
