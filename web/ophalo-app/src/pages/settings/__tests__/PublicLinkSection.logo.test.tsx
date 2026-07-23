import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { PublicLinkSection } from "../PublicLinkSection";
import type { IntakeStatusResult } from "../../../lib/apiClient";

// Business logos are wordmarks, not avatars — the customer preview must render them with
// object-contain at a bounded max footprint (never object-cover/crop), falling back to
// initials only when no logo URL is present or the image fails to load.

const mockGetIntake = vi.fn();

vi.mock("../../../lib/apiClient", async () => {
  const actual = await vi.importActual<typeof import("../../../lib/apiClient")>(
    "../../../lib/apiClient",
  );
  return {
    ...actual,
    api: {
      ...actual.api,
      getIntake: (...args: unknown[]) => mockGetIntake(...args),
    },
  };
});

const activeIntake: IntakeStatusResult = {
  hasActiveLink: true,
  publicSlug: "apex-home-services",
  createdAtUtc: "2026-07-01T00:00:00Z",
};

function renderSection(logoUrl: string) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <PublicLinkSection businessName="Apex Home Services" logoUrl={logoUrl} />
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  mockGetIntake.mockReset();
  mockGetIntake.mockResolvedValue(activeIntake);
});

describe("PublicLinkSection customer-preview logo rendering", () => {
  it("renders a horizontal wordmark logo uncropped with object-contain", async () => {
    renderSection("https://cdn.example.com/wordmark-logo.png");

    const img = await screen.findByAltText("Apex Home Services logo");
    expect(img).toHaveClass("object-contain");
    expect(img).not.toHaveClass("object-cover");
    expect(img.className).toMatch(/max-h-8/);
    expect(img.className).toMatch(/max-w-\[96px\]/);
  });

  it("renders a square logo uncropped with object-contain", async () => {
    renderSection("https://cdn.example.com/square-logo.png");

    const img = await screen.findByAltText("Apex Home Services logo");
    expect(img).toHaveClass("object-contain");
    expect(img).not.toHaveClass("rounded-full");
  });

  it("shows initials fallback when no logo URL is present", async () => {
    renderSection("");

    expect(await screen.findByText("AH")).toBeInTheDocument();
    expect(screen.queryByAltText("Apex Home Services logo")).not.toBeInTheDocument();
  });

  it("falls back to initials when the logo URL fails to load", async () => {
    renderSection("https://cdn.example.com/broken-logo.png");

    const img = await screen.findByAltText("Apex Home Services logo");
    fireEvent.error(img);

    expect(await screen.findByText("AH")).toBeInTheDocument();
    expect(screen.queryByAltText("Apex Home Services logo")).not.toBeInTheDocument();
  });
});
