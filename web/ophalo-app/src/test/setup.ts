import "@testing-library/jest-dom";

// jsdom has no ResizeObserver — RequestWorkbenchShell (UI-001 Step 3) needs one to measure the
// protected-workspace-minimum width. A no-op stub is enough for tests that don't assert on
// pane-width behavior directly; those tests install their own callback-capturing stub instead.
if (typeof globalThis.ResizeObserver === "undefined") {
  globalThis.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  };
}
