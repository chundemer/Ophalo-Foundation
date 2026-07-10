"use client";

import { useRef, useState } from "react";

type Stage = "form" | "submitting" | "success" | "unavailable" | "staff";

function businessInitials(name: string): string {
  const words = name.trim().split(/\s+/).filter(Boolean);
  if (words.length === 0) return "?";
  if (words.length === 1) return words[0].slice(0, 2).toUpperCase();
  return (words[0][0] + words[1][0]).toUpperCase();
}

const US_STATES = [
  ["AL","Alabama"],["AK","Alaska"],["AZ","Arizona"],["AR","Arkansas"],["CA","California"],
  ["CO","Colorado"],["CT","Connecticut"],["DE","Delaware"],["DC","Washington DC"],["FL","Florida"],
  ["GA","Georgia"],["HI","Hawaii"],["ID","Idaho"],["IL","Illinois"],["IN","Indiana"],
  ["IA","Iowa"],["KS","Kansas"],["KY","Kentucky"],["LA","Louisiana"],["ME","Maine"],
  ["MD","Maryland"],["MA","Massachusetts"],["MI","Michigan"],["MN","Minnesota"],["MS","Mississippi"],
  ["MO","Missouri"],["MT","Montana"],["NE","Nebraska"],["NV","Nevada"],["NH","New Hampshire"],
  ["NJ","New Jersey"],["NM","New Mexico"],["NY","New York"],["NC","North Carolina"],["ND","North Dakota"],
  ["OH","Ohio"],["OK","Oklahoma"],["OR","Oregon"],["PA","Pennsylvania"],["RI","Rhode Island"],
  ["SC","South Carolina"],["SD","South Dakota"],["TN","Tennessee"],["TX","Texas"],["UT","Utah"],
  ["VT","Vermont"],["VA","Virginia"],["WA","Washington"],["WV","West Virginia"],["WI","Wisconsin"],
  ["WY","Wyoming"],
] as const;

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
    case "KeepRequest.ServiceAddressLine1Required":
      return "Please enter the service address.";
    case "KeepRequest.ServiceCityRequired":
      return "Please enter the city.";
    case "KeepRequest.ServiceStateRequired":
    case "KeepRequest.ServiceStateInvalid":
      return "Please select a state.";
    default:
      return null;
  }
}

const inputClass =
  "w-full rounded-lg border border-[var(--ophalo-border)] bg-card px-4 py-3 text-sm " +
  "placeholder:text-muted-foreground focus:border-[var(--keep-accent)] focus:ring-1 " +
  "focus:ring-[var(--keep-accent)] focus:outline-none disabled:opacity-50";

const labelClass = "block text-sm font-medium text-foreground mb-1.5";

function CardSection({ children }: { children: React.ReactNode }) {
  return (
    <div className="rounded-2xl border border-[var(--ophalo-border)] bg-card px-5 py-5 shadow-sm">
      {children}
    </div>
  );
}

function Footer() {
  return (
    <footer className="pb-6 pt-4 text-center">
      <img
        src="/brand/ophalo-lockup-color.svg"
        alt="OpHalo"
        className="mx-auto h-6 w-auto opacity-75"
      />
      <p className="mt-2 text-sm font-semibold text-[var(--ophalo-ink)]">Keep by OpHalo</p>
      <p className="mx-auto mt-1 max-w-md text-sm leading-5 text-[var(--ophalo-muted)]">
        Request tracking for service businesses that work by phone, text, and in person.
      </p>
    </footer>
  );
}

export default function IntakeForm({
  token,
  slug,
  businessName,
}: {
  token?: string;
  slug?: string;
  businessName?: string | null;
}) {
  const [stage, setStage] = useState<Stage>("form");
  const [referenceCode, setReferenceCode] = useState<string>("");
  const [pageToken, setPageToken] = useState<string>("");
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
    const serviceAddressLine1 = String(data.get("serviceAddressLine1") ?? "").trim();
    const serviceAddressLine2Raw = String(data.get("serviceAddressLine2") ?? "").trim();
    const serviceAddressLine2 = serviceAddressLine2Raw.length > 0 ? serviceAddressLine2Raw : null;
    const serviceCity = String(data.get("serviceCity") ?? "").trim();
    const serviceState = String(data.get("serviceState") ?? "").trim();
    const serviceZipRaw = String(data.get("serviceZip") ?? "").trim();
    const serviceZip = serviceZipRaw.length > 0 ? serviceZipRaw : null;

    const intakeUrl = slug
      ? `${process.env.NEXT_PUBLIC_API_BASE_URL}/keep/public-intake/slug/${encodeURIComponent(slug)}`
      : `${process.env.NEXT_PUBLIC_API_BASE_URL}/keep/public-intake/token/${encodeURIComponent(token ?? "")}`;

    let res: Response;
    try {
      res = await fetch(intakeUrl, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          customerName, customerPhone, customerEmail, description,
          serviceAddressLine1, serviceAddressLine2, serviceCity, serviceState, serviceZip,
        }),
      });
    } catch {
      setError("Unable to connect. Please check your connection and try again.");
      setStage("form");
      submitInFlight.current = false;
      return;
    }

    if (res.ok) {
      const body = await res.json().catch(() => null);
      setReferenceCode(typeof body?.referenceCode === "string" ? body.referenceCode : "");
      setPageToken(typeof body?.pageToken === "string" ? body.pageToken : "");
      setStage("success");
      return;
    }

    const body = await res.json().catch(() => null);
    const code = parseErrorCode(body);

    if (res.status === 422 && code === "keep.public_intake.unavailable") {
      setStage("unavailable");
      return;
    }

    if (res.status === 422 && code === "keep.public_intake.staff_not_permitted") {
      setStage("staff");
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

  if (stage === "staff") {
    return (
      <main className="min-h-screen bg-[var(--ophalo-canvas)] px-4 py-6 sm:py-10">
        <div className="mx-auto w-full max-w-2xl space-y-4 sm:space-y-5">
          <CardSection>
            <h1 className="text-base font-semibold text-foreground">
              You&apos;re signed in to this account.
            </h1>
            <p className="mt-2 text-sm text-muted-foreground">
              This is the customer intake form. Use Quick Capture or Create Request in the app
              to submit a request on behalf of a customer.
            </p>
            <a
              href={process.env.NEXT_PUBLIC_APP_BASE_URL ?? "/"}
              className="mt-4 inline-flex items-center rounded-lg bg-[var(--keep-accent)] px-4 py-2.5 text-sm font-medium text-white hover:bg-[var(--keep-accent-hover)]"
            >
              Open the app
            </a>
          </CardSection>
          <Footer />
        </div>
      </main>
    );
  }

  if (stage === "unavailable") {
    return (
      <main className="min-h-screen bg-[var(--ophalo-canvas)] px-4 py-6 sm:py-10">
        <div className="mx-auto w-full max-w-2xl space-y-4 sm:space-y-5">
          <CardSection>
            <h1 className="text-base font-semibold text-foreground">
              This link is not available.
            </h1>
            <p className="mt-2 text-sm text-muted-foreground">
              This intake link is no longer active. If you were sent this link by a business,
              please contact them directly for assistance.
            </p>
          </CardSection>
          <Footer />
        </div>
      </main>
    );
  }

  if (stage === "success") {
    const trackerUrl = pageToken ? `/keep/r/${pageToken}` : null;

    return (
      <main className="min-h-screen bg-[var(--ophalo-canvas)] px-4 py-6 sm:py-10">
        <div className="mx-auto w-full max-w-2xl space-y-4 sm:space-y-5">
          <CardSection>
            <h1 className="text-base font-semibold text-foreground">Request submitted.</h1>
            <p className="mt-2 text-sm text-muted-foreground">
              Your request has been received. Your reference code is:
            </p>
            <p className="mt-2 rounded-lg bg-[var(--ophalo-canvas)] px-4 py-3 text-sm font-mono font-semibold text-foreground">
              {referenceCode}
            </p>
            <p className="mt-3 text-sm text-muted-foreground">
              You can check the status of your request and send additional details from your request page.
            </p>
            {trackerUrl && (
              <div className="mt-4 flex flex-col gap-2 sm:flex-row">
                <a
                  href={trackerUrl}
                  className="inline-flex items-center justify-center rounded-lg bg-[var(--keep-accent)] px-4 py-2.5 text-sm font-medium text-white hover:bg-[var(--keep-accent-hover)]"
                >
                  View your request page
                </a>
                <a
                  href={trackerUrl}
                  className="inline-flex items-center justify-center rounded-lg border border-[var(--ophalo-border)] bg-card px-4 py-2.5 text-sm font-medium text-foreground hover:bg-muted"
                >
                  Save this link
                </a>
              </div>
            )}
            <p className="mt-4 text-xs text-muted-foreground">
              Tip: save this page to your bookmarks, home screen, or messages so you can find it again.
            </p>
          </CardSection>
          <Footer />
        </div>
      </main>
    );
  }

  const submitting = stage === "submitting";

  return (
    <main className="min-h-screen bg-[var(--ophalo-canvas)] px-4 py-6 sm:py-10">
      <div className="mx-auto w-full max-w-2xl space-y-4 sm:space-y-5">
        {/* Header */}
        {businessName ? (
          <div className="flex items-center gap-3 px-1 pb-1">
            <div className="flex h-12 w-12 shrink-0 items-center justify-center rounded-xl bg-[var(--ophalo-navy)] text-sm font-bold tracking-wide text-white">
              {businessInitials(businessName)}
            </div>
            <div className="min-w-0">
              <p className="text-[10px] font-semibold uppercase tracking-widest text-muted-foreground">
                New Request
              </p>
              <p className="truncate text-lg font-bold leading-tight text-foreground">
                {businessName}
              </p>
              <p className="mt-1 text-sm text-muted-foreground">
                Fill out the form below and {businessName} will follow up with you.
              </p>
            </div>
          </div>
        ) : (
          <CardSection>
            <h1 className="text-lg font-semibold text-foreground">Submit a request</h1>
            <p className="mt-1 text-sm text-muted-foreground">
              Fill out the form below and the business will follow up with you.
            </p>
          </CardSection>
        )}

        <form onSubmit={handleSubmit} className="space-y-4 sm:space-y-5">
          {/* Contact details */}
          <CardSection>
            <h2 className="mb-4 text-sm font-semibold text-foreground">Your contact details</h2>
            <div className="space-y-4">
              <div>
                <label htmlFor="customerName" className={labelClass}>
                  Your name <span className="text-destructive">*</span>
                </label>
                <input
                  id="customerName"
                  name="customerName"
                  type="text"
                  autoComplete="name"
                  required
                  disabled={submitting}
                  className={inputClass}
                  placeholder="Full name"
                />
              </div>

              <div>
                <label htmlFor="customerPhone" className={labelClass}>
                  Phone number <span className="text-destructive">*</span>
                </label>
                <input
                  id="customerPhone"
                  name="customerPhone"
                  type="tel"
                  autoComplete="tel"
                  inputMode="tel"
                  required
                  disabled={submitting}
                  className={inputClass}
                  placeholder="(555) 000-0000"
                />
              </div>

              <div>
                <label htmlFor="customerEmail" className={labelClass}>
                  Email address <span className="text-muted-foreground font-normal">(optional)</span>
                </label>
                <input
                  id="customerEmail"
                  name="customerEmail"
                  type="email"
                  autoComplete="email"
                  inputMode="email"
                  disabled={submitting}
                  className={inputClass}
                  placeholder="you@example.com"
                />
              </div>
            </div>
          </CardSection>

          {/* Service location */}
          <CardSection>
            <h2 className="mb-1 text-sm font-semibold text-foreground">Service location</h2>
            <p className="mb-4 text-xs text-muted-foreground">
              Shared with this business only. Not shown on your request page.
            </p>
            <div className="space-y-4">
              <div>
                <label htmlFor="serviceAddressLine1" className={labelClass}>
                  Address <span className="text-destructive">*</span>
                </label>
                <input
                  id="serviceAddressLine1"
                  name="serviceAddressLine1"
                  type="text"
                  autoComplete="address-line1"
                  required
                  disabled={submitting}
                  className={inputClass}
                  placeholder="Street address"
                />
              </div>

              <div>
                <label htmlFor="serviceAddressLine2" className={labelClass}>
                  Apt, suite, unit <span className="text-muted-foreground font-normal">(optional)</span>
                </label>
                <input
                  id="serviceAddressLine2"
                  name="serviceAddressLine2"
                  type="text"
                  autoComplete="address-line2"
                  disabled={submitting}
                  className={inputClass}
                  placeholder="Apt 4B"
                />
              </div>

              <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
                <div className="sm:col-span-1">
                  <label htmlFor="serviceCity" className={labelClass}>
                    City <span className="text-destructive">*</span>
                  </label>
                  <input
                    id="serviceCity"
                    name="serviceCity"
                    type="text"
                    autoComplete="address-level2"
                    required
                    disabled={submitting}
                    className={inputClass}
                    placeholder="City"
                  />
                </div>

                <div className="sm:col-span-1">
                  <label htmlFor="serviceState" className={labelClass}>
                    State <span className="text-destructive">*</span>
                  </label>
                  <select
                    id="serviceState"
                    name="serviceState"
                    required
                    disabled={submitting}
                    defaultValue=""
                    className={inputClass}
                  >
                    <option value="" disabled>Select state</option>
                    {US_STATES.map(([code, name]) => (
                      <option key={code} value={code}>{name}</option>
                    ))}
                  </select>
                </div>

                <div className="sm:col-span-1">
                  <label htmlFor="serviceZip" className={labelClass}>
                    ZIP <span className="text-muted-foreground font-normal">(optional)</span>
                  </label>
                  <input
                    id="serviceZip"
                    name="serviceZip"
                    type="text"
                    autoComplete="postal-code"
                    inputMode="numeric"
                    disabled={submitting}
                    className={inputClass}
                    placeholder="00000"
                  />
                </div>
              </div>
            </div>
          </CardSection>

          {/* Request details */}
          <CardSection>
            <h2 className="mb-4 text-sm font-semibold text-foreground">Request details</h2>
            <div>
              <label htmlFor="description" className={labelClass}>
                What do you need help with? <span className="text-destructive">*</span>
              </label>
              <textarea
                id="description"
                name="description"
                rows={4}
                required
                disabled={submitting}
                className={inputClass + " resize-none"}
                placeholder="Describe the issue or what you need…"
              />
            </div>
          </CardSection>

          {error && (
            <div className="rounded-lg border border-destructive/30 bg-destructive/5 px-4 py-3">
              <p className="text-sm text-destructive">{error}</p>
            </div>
          )}

          <button
            type="submit"
            disabled={submitting}
            className="w-full rounded-lg bg-[var(--keep-accent)] px-4 py-3 text-sm font-semibold text-white hover:bg-[var(--keep-accent-hover)] disabled:opacity-50"
          >
            {submitting ? "Submitting…" : "Submit request"}
          </button>
        </form>

        <Footer />
      </div>
    </main>
  );
}
