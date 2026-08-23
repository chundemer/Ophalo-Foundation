import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { CustomerContactStrip } from "../CustomerContactStrip";

describe("CustomerContactStrip — email shortcut", () => {
  it("opens the shared contact workflow instead of bypassing its audit record", async () => {
    const user = userEvent.setup();
    const onContactLaunched = vi.fn();
    render(<CustomerContactStrip phone={null} email="customer@example.com" onContactLaunched={onContactLaunched} />);
    await user.click(screen.getByRole("button", { name: "Email" }));
    expect(onContactLaunched).toHaveBeenCalledWith("outbound", "email");
  });
});
