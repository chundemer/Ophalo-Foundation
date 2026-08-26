import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { MobileContactLocationCard } from "../MobileContactLocationCard";
import { mockRequestDetails } from "../../../mocks/fixtures";
import type { KeepRequestDetailResult } from "../../../lib/apiClient";

// Slice 3 (2026-08-26), field-operations decision: Call/Text/Maps are native anchors reachable
// directly from the mobile canvas — no audit modal in the click path. Log Contact stays a
// separate, explicit affordance for recording contact activity.

function baseDetail(): KeepRequestDetailResult {
  // mock-req-002 has a phone; fixture detail records carry no service address, so tests that
  // need one apply an explicit override.
  return mockRequestDetails["mock-req-002"];
}

function withAddress(detail: KeepRequestDetailResult): KeepRequestDetailResult {
  return {
    ...detail,
    serviceAddressLine1: "1234 Oak Street",
    serviceAddressLine2: null,
    serviceCity: "Memphis",
    serviceState: "TN",
    serviceZip: "38117",
  };
}

function renderCard(detail: KeepRequestDetailResult) {
  return render(
    <MobileContactLocationCard detail={detail} onContactLaunched={vi.fn()} onEditLocation={vi.fn()} />,
  );
}

describe("MobileContactLocationCard", () => {
  it("renders native tel: and sms: anchors when a phone is present", () => {
    renderCard(withAddress(baseDetail()));
    const call = screen.getByRole("link", { name: /call/i });
    const text = screen.getByRole("link", { name: /text/i });
    expect(call.tagName).toBe("A");
    expect(text.tagName).toBe("A");
    expect(call).toHaveAttribute("href", "tel:+15555550102");
    expect(text).toHaveAttribute("href", "sms:+15555550102");
    // No onClick-driven audit modal in the handoff path itself.
    expect(call).not.toHaveAttribute("onclick");
    expect(text).not.toHaveAttribute("onclick");
  });

  it("normalizes a stored E.164 phone value rather than misdialing an extra leading 1", () => {
    const detail: KeepRequestDetailResult = { ...baseDetail(), customerPhone: "+1 (555) 555-0102" };
    renderCard(detail);
    const call = screen.getByRole("link", { name: /call/i });
    const text = screen.getByRole("link", { name: /text/i });
    expect(call).toHaveAttribute("href", "tel:+15555550102");
    expect(text).toHaveAttribute("href", "sms:+15555550102");
  });

  it("builds the Maps query from whichever address parts are independently present (city without state)", () => {
    const detail: KeepRequestDetailResult = {
      ...baseDetail(),
      serviceAddressLine1: null,
      serviceAddressLine2: null,
      serviceCity: "Memphis",
      serviceState: null,
      serviceZip: null,
    };
    renderCard(detail);
    // Regression guard: a query built only from a "city && state" pair would previously be
    // empty here (state missing) while still rendering the link — gating must follow the
    // composed query, not the broader "hasAddress" (line1 || city) check.
    const maps = screen.getByRole("link", { name: /maps/i });
    expect(maps).toHaveAttribute("href", `https://maps.google.com/?q=${encodeURIComponent("Memphis")}`);
  });

  it("omits Call/Text and leaves no empty action-row chrome when phone is missing but address is present", () => {
    const detail: KeepRequestDetailResult = { ...withAddress(baseDetail()), customerPhone: "" };
    renderCard(detail);
    expect(screen.queryByRole("link", { name: /call/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /text/i })).not.toBeInTheDocument();
    // Maps still renders since an address exists.
    expect(screen.getByRole("link", { name: /maps/i })).toBeInTheDocument();
  });

  it("omits the action row entirely when phone and address are both missing", () => {
    const detail: KeepRequestDetailResult = {
      ...baseDetail(),
      customerPhone: "",
      serviceAddressLine1: null,
      serviceAddressLine2: null,
      serviceCity: null,
      serviceState: null,
      serviceZip: null,
    };
    const { container } = renderCard(detail);
    expect(screen.queryByRole("link", { name: /call/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /text/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /maps/i })).not.toBeInTheDocument();
    expect(container.querySelectorAll("a").length).toBe(0);
    expect(screen.getByText("Not on file")).toBeInTheDocument();
  });

  it("renders a Maps link with an encoded address, opened in a new tab without replacing the PWA document", () => {
    renderCard(withAddress(baseDetail()));
    const maps = screen.getByRole("link", { name: /maps/i });
    expect(maps.tagName).toBe("A");
    expect(maps).toHaveAttribute("target", "_blank");
    expect(maps).toHaveAttribute("rel", "noopener noreferrer");
    const href = maps.getAttribute("href")!;
    expect(href.startsWith("https://maps.google.com/?q=")).toBe(true);
    expect(href).toContain(encodeURIComponent("1234 Oak Street"));
  });

  it("shows a graceful Not on file state and no Maps link when the address is missing", () => {
    const detail: KeepRequestDetailResult = {
      ...baseDetail(),
      serviceAddressLine1: null,
      serviceAddressLine2: null,
      serviceCity: null,
      serviceState: null,
      serviceZip: null,
    };
    renderCard(detail);
    expect(screen.getByText("Not on file")).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /maps/i })).not.toBeInTheDocument();
  });

  it("keeps a separate Log external contact affordance distinct from Call/Text", () => {
    renderCard(baseDetail());
    expect(screen.getByRole("button", { name: /log external contact/i })).toBeInTheDocument();
  });
});
