import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { DetailHeroBadges } from "../DetailHero";
import { mockRequestDetails } from "../../../mocks/fixtures";
import type { KeepRequestDetailResult } from "../../../lib/apiClient";

// Bug fix (2026-08-25): DetailHeroBadges hardcoded the amber "attention" badge variant even when
// effectiveAttention.level is legitimately "overdue" (cases 2/3, ADR-489/490), diverging from the
// queue's danger-red presentation for the same underlying condition.

function detailWithAttention(
  level: KeepRequestDetailResult["effectiveAttention"]["level"],
): KeepRequestDetailResult {
  const base = mockRequestDetails["mock-req-001"];
  return {
    ...base,
    effectiveAttention: {
      ...base.effectiveAttention,
      level,
      reason: "customer_message",
    },
  };
}

describe("DetailHeroBadges — exception badge variant", () => {
  it("renders the danger variant when effectiveAttention.level is overdue", () => {
    const { container } = render(<DetailHeroBadges detail={detailWithAttention("overdue")} />);
    const badge = screen.getByText(/customer message/i).closest("span");
    expect(badge?.className).toContain("--ophalo-danger");
    expect(container.querySelector("svg")).not.toBeNull();
  });

  it("renders the amber attention variant when effectiveAttention.level is due (not overdue)", () => {
    render(<DetailHeroBadges detail={detailWithAttention("due")} />);
    const badge = screen.getByText(/customer message/i).closest("span");
    expect(badge?.className).toContain("--ophalo-attention");
    expect(badge?.className).not.toContain("--ophalo-danger");
  });
});
