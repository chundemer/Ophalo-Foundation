import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { CustomerContactStrip } from "../CustomerContactStrip";

describe("CustomerContactStrip — Contact customer shortcuts", () => {
  it("routes call and text through the one contact-drawer entry point", async () => {
    const user = userEvent.setup();
    const onContactLaunched = vi.fn();
    render(<CustomerContactStrip phone="5555550101" email={null} contactPreference={null} onContactLaunched={onContactLaunched} />);
    await user.click(screen.getByRole("button", { name: "Call" }));
    await user.click(screen.getByRole("button", { name: "Text" }));
    expect(onContactLaunched).toHaveBeenNthCalledWith(1, "outbound", "phone");
    expect(onContactLaunched).toHaveBeenNthCalledWith(2, "outbound", "sms");
  });
});
