import { describe, it, expect, vi, afterEach } from "vitest";
import type { ReactElement } from "react";

// `main.tsx` runs `void bootstrap()` on import, so each case sets up the environment,
// re-imports the module, and waits for the async bootstrap to settle. `App` and
// `ConfigurationError` are stubbed so the assertion is a plain identity check on which
// screen was handed to `root.render`.

const renderMock = vi.fn();

vi.mock("react-dom/client", () => ({
  createRoot: () => ({ render: renderMock }),
}));
vi.mock("../App", () => ({ App: () => null }));
vi.mock("../components/ConfigurationError", () => ({
  ConfigurationError: () => null,
}));

async function bootWith(publicBaseUrl: string) {
  vi.resetModules();
  vi.stubEnv("VITE_PUBLIC_BASE_URL", publicBaseUrl);
  renderMock.mockClear();
  document.body.innerHTML = '<div id="root"></div>';
  await import("../main");
  await Promise.resolve();
  await Promise.resolve();
}

function renderedTypes(): unknown[] {
  const tree = renderMock.mock.calls[0]?.[0] as ReactElement | undefined;
  const types: unknown[] = [];
  const walk = (node: unknown) => {
    if (!node || typeof node !== "object") return;
    const el = node as ReactElement;
    if (el.type) types.push(el.type);
    const children = (el.props as { children?: unknown } | undefined)?.children;
    if (Array.isArray(children)) children.forEach(walk);
    else walk(children);
  };
  walk(tree);
  return types;
}

afterEach(() => {
  vi.unstubAllEnvs();
});

describe("main bootstrap — configuration gate", () => {
  it("renders the configuration-error screen when VITE_PUBLIC_BASE_URL is missing", async () => {
    await bootWith("");
    const { ConfigurationError } = await import("../components/ConfigurationError");
    const { App } = await import("../App");

    expect(renderMock).toHaveBeenCalledTimes(1);
    const types = renderedTypes();
    expect(types).toContain(ConfigurationError);
    expect(types).not.toContain(App);
  });

  it("mounts the app when VITE_PUBLIC_BASE_URL is valid", async () => {
    await bootWith("https://app.example.com");
    const { ConfigurationError } = await import("../components/ConfigurationError");
    const { App } = await import("../App");

    expect(renderMock).toHaveBeenCalledTimes(1);
    const types = renderedTypes();
    expect(types).toContain(App);
    expect(types).not.toContain(ConfigurationError);
  });
});
