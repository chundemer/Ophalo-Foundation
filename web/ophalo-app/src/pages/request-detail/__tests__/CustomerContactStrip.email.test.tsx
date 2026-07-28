import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { CustomerContactStrip } from "../CustomerContactStrip";

// GAP-048: the Email quick action is a plain contact shortcut — it must never embed the
// private customer-page URL. Sharing the tracker link happens only through ShareLinkModal's
// explicit prepare/confirm ceremony.

vi.mock("../../../lib/apiClient", () => ({
  api: {
    createSmsHandoff: vi.fn(),
    createCallHandoff: vi.fn(),
  },
}));

describe("CustomerContactStrip — Email quick action (GAP-048)", () => {
  beforeEach(() => {
    vi.stubEnv("VITE_PUBLIC_BASE_URL", "http://localhost:3000");
  });

  it("renders a bare mailto: with no prefilled subject or body, even with a pageToken present", () => {
    render(
      <CustomerContactStrip
        requestId="req-1"
        phone={null}
        email="customer@example.com"
        customerName="Jamie Rivera"
        pageToken="tok_abc123"
        onContactLaunched={vi.fn()}
      />
    );

    const emailLink = screen.getByRole("link", { name: /email/i });
    expect(emailLink).toHaveAttribute("href", "mailto:customer@example.com");
  });
});
