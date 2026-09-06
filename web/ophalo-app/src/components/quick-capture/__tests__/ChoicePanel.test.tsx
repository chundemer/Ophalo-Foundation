import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ChoicePanel } from "../ChoicePanel";

describe("ChoicePanel", () => {
  it("explains both outcomes and routes to the matching handler", async () => {
    const user = userEvent.setup();
    const onChooseCustomerLink = vi.fn();
    const onChooseRecordMyself = vi.fn();
    render(
      <ChoicePanel
        onChooseCustomerLink={onChooseCustomerLink}
        onChooseRecordMyself={onChooseRecordMyself}
      />,
    );

    expect(screen.getByText(/no request is created until the customer submits/i)).toBeInTheDocument();
    expect(screen.getByText(/creates the request now/i)).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /let the customer submit it/i }));
    expect(onChooseCustomerLink).toHaveBeenCalledTimes(1);
    expect(onChooseRecordMyself).not.toHaveBeenCalled();

    await user.click(screen.getByRole("button", { name: /record it yourself/i }));
    expect(onChooseRecordMyself).toHaveBeenCalledTimes(1);
  });
});
