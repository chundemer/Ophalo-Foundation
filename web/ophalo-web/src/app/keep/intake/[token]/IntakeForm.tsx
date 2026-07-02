"use client";

import { useRef, useState } from "react";

type Stage = "form" | "submitting" | "success" | "unavailable";

function parseErrorCode(body: unknown): string | undefined {
  if (body == null || typeof body !== "object") return undefined;
  const b = body as Record<string, unknown>;
  const ext = b["extensions"];
  if (ext != null && typeof ext === "object") {
    const code = (ext as Record<string, unknown>)["code"];
    if (typeof code === "string") return code;
  }
  const code = b["code"];
  if (typeof code === "string") return code;
  return undefined;
}

function fieldError(code: string | undefined): string | null {
  switch (code) {
    case "KeepRequest.CustomerNameRequired":
      return "Please enter your name.";
    case "KeepRequest.CustomerNameTooLong":
      return "Name is too long (max 200 characters).";
    case "KeepRequest.CustomerPhoneRequired":
      return "Please enter your phone number.";
    case "KeepRequest.CustomerPhoneTooLong":
      return "Phone number is too long.";
    case "KeepRequest.CustomerPhoneInvalidCharacters":
    case "KeepRequest.CustomerPhoneInvalidFormat":
      return "Please enter a valid phone number.";
    case "KeepRequest.CustomerEmailTooLong":
      return "Email address is too long.";
    case "KeepRequest.CustomerEmailInvalid":
      return "Please enter a valid email address.";
    case "KeepRequest.DescriptionRequired":
      return "Please describe what you need help with.";
    case "KeepRequest.DescriptionTooLong":
      return "Description is too long (max 4000 characters).";
    default:
      return null;
  }
}

export default function IntakeForm({ token }: { token: string }) {
  const [stage, setStage] = useState<Stage>("form");
  const [referenceCode, setReferenceCode] = useState<string>("");
  const [error, setError] = useState<string | null>(null);
  const submitInFlight = useRef(false);

  async function handleSubmit(e: React.FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (submitInFlight.current) return;
    submitInFlight.current = true;
    setError(null);
    setStage("submitting");

    const data = new FormData(e.currentTarget);
    const customerName = String(data.get("customerName") ?? "").trim();
    const customerPhone = String(data.get("customerPhone") ?? "").trim();
    const customerEmailRaw = String(data.get("customerEmail") ?? "").trim();
    const customerEmail = customerEmailRaw.length > 0 ? customerEmailRaw : null;
    const description = String(data.get("description") ?? "").trim();

    let res: Response;
    try {
      res = await fetch(
        `${process.env.NEXT_PUBLIC_API_BASE_URL}/keep/public-intake/token/${encodeURIComponent(token)}`,
        {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ customerName, customerPhone, customerEmail, description }),
        },
      );
    } catch {
      setError("Unable to connect. Please check your connection and try again.");
      setStage("form");
      submitInFlight.current = false;
      return;
    }

    if (res.ok) {
      const body = await res.json().catch(() => null);
      setReferenceCode(
        typeof body?.referenceCode === "string" ? body.referenceCode : "",
      );
      setStage("success");
      return;
    }

    const body = await res.json().catch(() => null);
    const code = parseErrorCode(body);

    if (res.status === 422 && code === "keep.public_intake.unavailable") {
      setStage("unavailable");
      return;
    }

    const known = fieldError(code);
    if (known) {
      setError(known);
      setStage("form");
      submitInFlight.current = false;
      return;
    }

    setError("Something went wrong. Please try again.");
    setStage("form");
    submitInFlight.current = false;
  }

  if (stage === "unavailable") {
    return (
      <div className="auth-page">
        <div className="container">
          <h1>This link is not available.</h1>
          <p>
            This intake link is no longer active. If you were sent this link by
            a business, please contact them directly for assistance.
          </p>
        </div>
      </div>
    );
  }

  if (stage === "success") {
    return (
      <div className="auth-page">
        <div className="container">
          <h1>Request submitted.</h1>
          <p>
            {"Your request has been received. Keep this reference code for your records:"}
          </p>
          <p>
            <strong className="auth-reference-code">
              {referenceCode}
            </strong>
          </p>
          <p>The business will follow up with you.</p>
        </div>
      </div>
    );
  }

  const submitting = stage === "submitting";

  return (
    <div className="auth-page">
      <div className="container">
        <h1>Submit a request</h1>
        <p>Fill out the form below and the business will follow up with you.</p>

        <form className="auth-form" onSubmit={handleSubmit}>
          <div className="auth-form-field">
            <label htmlFor="customerName">Your name</label>
            <input
              id="customerName"
              name="customerName"
              type="text"
              autoComplete="name"
              required
              disabled={submitting}
            />
          </div>

          <div className="auth-form-field">
            <label htmlFor="customerPhone">Phone number</label>
            <input
              id="customerPhone"
              name="customerPhone"
              type="tel"
              autoComplete="tel"
              inputMode="tel"
              required
              disabled={submitting}
            />
          </div>

          <div className="auth-form-field">
            <label htmlFor="customerEmail">Email address (optional)</label>
            <input
              id="customerEmail"
              name="customerEmail"
              type="email"
              autoComplete="email"
              inputMode="email"
              disabled={submitting}
            />
          </div>

          <div className="auth-form-field">
            <label htmlFor="description">What do you need help with?</label>
            <textarea
              id="description"
              name="description"
              rows={4}
              required
              disabled={submitting}
            />
          </div>

          {error && <p className="auth-error">{error}</p>}

          <button type="submit" className="auth-submit" disabled={submitting}>
            {submitting ? "Submitting…" : "Submit request"}
          </button>
        </form>
      </div>
    </div>
  );
}
