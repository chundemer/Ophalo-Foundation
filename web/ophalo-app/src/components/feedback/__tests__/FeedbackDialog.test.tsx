import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { FeedbackDialog } from "../FeedbackDialog";

// A per-test spy is assigned to this holder rather than resetting a shared mock in `beforeEach`:
// the dialog awaits `api.submitFeedback` in a raw try/catch, and a `beforeEach` mock reset makes
// vitest mis-attribute the (caught) rejection as unhandled. Fresh spies sidestep it entirely.
let submitImpl: (body: unknown) => Promise<unknown>;

vi.mock("../../../lib/apiClient", async () => {
  const actual = await vi.importActual<typeof import("../../../lib/apiClient")>(
    "../../../lib/apiClient",
  );
  return {
    ...actual,
    api: { ...actual.api, submitFeedback: (body: unknown) => submitImpl(body) },
  };
});

// Imported after the mock is registered so the real ApiError class is used for `instanceof`.
import { ApiError } from "../../../lib/apiClient";

// Neutral for both 200 and 202 — a queued (202) submission is not confirmed delivery, so the copy
// must never imply "sent to the team".
const NEUTRAL_THANKS = "Thanks for taking the time to share this. We read every note.";

const RETENTION_LINE =
  "If feedback cannot be delivered immediately, its message and limited submission context may be " +
  "retained for up to 30 days for recovery. Successfully delivered feedback is minimized promptly, " +
  "and its remaining delivery metadata is deleted after seven days.";

function renderDialog(props: Partial<React.ComponentProps<typeof FeedbackDialog>> = {}) {
  const onClose = props.onClose ?? vi.fn();
  render(<FeedbackDialog onClose={onClose} {...props} />);
  return { onClose };
}

async function typeMessage(text: string) {
  await userEvent.type(screen.getByLabelText("What got in your way?"), text);
}

const sendButton = () => screen.getByRole("button", { name: "Send feedback" });

describe("FeedbackDialog", () => {
  it("shows the verbatim ADR-500 retention notice and gates Send on a non-blank message", () => {
    submitImpl = vi.fn();
    renderDialog();

    expect(screen.getByText(RETENTION_LINE)).toBeInTheDocument();
    expect(sendButton()).toBeDisabled();
  });

  it("submits the trimmed message, chosen category and context, then thanks the user", async () => {
    const submit = vi.fn().mockResolvedValue({ status: "delivered" });
    submitImpl = submit;
    const { onClose } = renderDialog({ context: { route: "#/help" } });

    await typeMessage("  the schedule is confusing  ");
    await userEvent.selectOptions(screen.getByLabelText(/Category/), "confusing");
    await userEvent.click(sendButton());

    expect(submit).toHaveBeenCalledWith({
      message: "the schedule is confusing",
      category: "confusing",
      context: { route: "#/help" },
    });
    expect(await screen.findByText(NEUTRAL_THANKS)).toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();

    await userEvent.click(screen.getByRole("button", { name: "Close" }));
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it("omits category when none is chosen and shows the same neutral thanks for a queued (202) result", async () => {
    const submit = vi.fn().mockResolvedValue({ status: "queued" });
    submitImpl = submit;
    renderDialog();

    await typeMessage("no category here");
    await userEvent.click(sendButton());

    expect(submit).toHaveBeenCalledWith({ message: "no category here" });
    // A 202 is durable queueing, not confirmed delivery — the copy must not imply "sent".
    expect(await screen.findByText(NEUTRAL_THANKS)).toBeInTheDocument();
    expect(screen.queryByText(/went straight to the team|sent to the team|delivered/i)).not.toBeInTheDocument();
  });

  it("blocks submission and warns when the message is over 4,000 characters", async () => {
    const submit = vi.fn();
    submitImpl = submit;
    renderDialog();

    await userEvent.click(screen.getByLabelText("What got in your way?"));
    await userEvent.paste("x".repeat(4_001));

    expect(screen.getByText(/1 character over the 4,000 limit/)).toBeInTheDocument();
    expect(sendButton()).toBeDisabled();
    expect(submit).not.toHaveBeenCalled();
  });

  it("surfaces a retryable rate-limit message on 429 and keeps the form", async () => {
    submitImpl = vi.fn(async () => {
      throw new ApiError(429, undefined, "API 429 /feedback");
    });
    renderDialog();

    await typeMessage("again and again");
    await userEvent.click(sendButton());

    expect(await screen.findByRole("alert")).toHaveTextContent(/lot of feedback recently/);
    expect(sendButton()).toBeEnabled();
  });

  it("maps 413 to a shorten-it message", async () => {
    submitImpl = vi.fn(async () => {
      throw new ApiError(413, "feedback.message_too_long", "API 413 /feedback");
    });
    renderDialog();

    await typeMessage("long message");
    await userEvent.click(sendButton());

    expect(await screen.findByRole("alert")).toHaveTextContent(/4,000 characters — please shorten/);
  });

  it("maps 503 to a try-again message and keeps the form", async () => {
    submitImpl = vi.fn(async () => {
      throw new ApiError(503, undefined, "API 503 /feedback");
    });
    renderDialog();

    await typeMessage("save this please");
    await userEvent.click(sendButton());

    expect(await screen.findByRole("alert")).toHaveTextContent("We could not save that just now. Please try again.");
    expect(sendButton()).toBeEnabled();
    expect(screen.queryByText(NEUTRAL_THANKS)).not.toBeInTheDocument();
  });

  it("falls back to a generic message on an unexpected failure", async () => {
    submitImpl = vi.fn(async () => {
      throw new Error("network");
    });
    renderDialog();

    await typeMessage("boom");
    await userEvent.click(sendButton());

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Something went wrong. Please try again.",
    );
  });

  it("closes without submitting on Cancel", async () => {
    const submit = vi.fn();
    submitImpl = submit;
    const { onClose } = renderDialog();

    await userEvent.click(screen.getByRole("button", { name: "Cancel" }));
    expect(onClose).toHaveBeenCalledTimes(1);
    expect(submit).not.toHaveBeenCalled();
  });
});
