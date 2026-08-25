import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { CustomerContactStrip } from "../CustomerContactStrip";

// Locked 2026-08-25: header contact preference is source-agnostic — shown whenever a real
// preference is set, omitted for "no_preference" and unset/unknown values.
describe("CustomerContactStrip — contact preference", () => {
  it("renders the preference label when a real preference is set", () => {
    render(
      <CustomerContactStrip
        phone="5555550101"
        email={null}
        contactPreference="text_message"
        onContactLaunched={vi.fn()}
      />,
    );
    expect(screen.getByText("Prefers text")).toBeInTheDocument();
  });

  it("omits the line for no_preference", () => {
    render(
      <CustomerContactStrip
        phone="5555550101"
        email={null}
        contactPreference="no_preference"
        onContactLaunched={vi.fn()}
      />,
    );
    expect(screen.queryByText(/prefers|no preference/i)).not.toBeInTheDocument();
  });

  it("omits the line for an unset preference", () => {
    render(
      <CustomerContactStrip
        phone="5555550101"
        email={null}
        contactPreference={null}
        onContactLaunched={vi.fn()}
      />,
    );
    expect(screen.queryByText(/prefers|no preference/i)).not.toBeInTheDocument();
  });
});
