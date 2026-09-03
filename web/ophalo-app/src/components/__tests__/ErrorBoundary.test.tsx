import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ErrorBoundary } from "../ErrorBoundary";

const captureHandledErrorMock = vi.fn();
vi.mock("../../lib/sentry", () => ({
  captureHandledError: (e: unknown) => captureHandledErrorMock(e),
}));

function Bomb(): never {
  throw new Error("sensitive stack trace / request data that must never reach the UI");
}

afterEach(() => {
  vi.restoreAllMocks();
  captureHandledErrorMock.mockClear();
});

describe("ErrorBoundary", () => {
  it("renders children normally when nothing throws", () => {
    render(
      <ErrorBoundary>
        <p>Workbench content</p>
      </ErrorBoundary>,
    );
    expect(screen.getByText("Workbench content")).toBeInTheDocument();
  });

  it("catches a render throw and shows a plain recovery card with no exception detail", () => {
    // React logs the caught error to the console; suppress that expected noise.
    vi.spyOn(console, "error").mockImplementation(() => {});

    render(
      <ErrorBoundary>
        <Bomb />
      </ErrorBoundary>,
    );

    expect(screen.getByText("Something went wrong")).toBeInTheDocument();
    expect(screen.queryByText(/sensitive stack trace/i)).not.toBeInTheDocument();
    expect(document.body.textContent).not.toMatch(/sensitive stack trace/i);
  });

  it("forwards the caught exception to the scrubbed Sentry capture path", () => {
    vi.spyOn(console, "error").mockImplementation(() => {});

    render(
      <ErrorBoundary>
        <Bomb />
      </ErrorBoundary>,
    );

    // The raw Error (which here carries representative sensitive text) is handed to the
    // capture path; sentryScrub is responsible for stripping it before send.
    expect(captureHandledErrorMock).toHaveBeenCalledTimes(1);
    const forwarded = captureHandledErrorMock.mock.calls[0][0] as Error;
    expect(forwarded).toBeInstanceOf(Error);
    expect(forwarded.message).toMatch(/sensitive stack trace/i);
    // Fallback UI is unchanged.
    expect(screen.getByText("Something went wrong")).toBeInTheDocument();
  });

  it("offers only a Reload action, which reloads the page", async () => {
    vi.spyOn(console, "error").mockImplementation(() => {});
    const reloadSpy = vi.fn();
    Object.defineProperty(window, "location", {
      value: { ...window.location, reload: reloadSpy },
      writable: true,
    });

    render(
      <ErrorBoundary>
        <Bomb />
      </ErrorBoundary>,
    );

    const buttons = screen.getAllByRole("button");
    expect(buttons).toHaveLength(1);
    expect(buttons[0]).toHaveTextContent("Reload");

    await userEvent.click(buttons[0]);
    expect(reloadSpy).toHaveBeenCalledTimes(1);
  });
});
