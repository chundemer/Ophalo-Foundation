"use client";

import { useRef, useState } from "react";
import { AlertTriangle, ArrowRight, Clock, ClipboardList, Lock, MapPin, UserRound } from "lucide-react";
import { KeepButton } from "@/components/keep/KeepButton";
import {
  KeepBusinessHeader,
  KeepCardShell,
  KeepPageFooter,
  KeepSectionHeader,
} from "@/components/keep/KeepPublicShell";

type Stage = "form" | "submitting" | "success" | "unavailable" | "staff";

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

type KnownField =
  | "customerName"
  | "customerPhone"
  | "customerEmail"
  | "description"
  | "serviceAddressLine1"
  | "serviceCity"
  | "serviceState";

function mapKnownFieldError(code: string | undefined): { field: KnownField; message: string } | null {
  switch (code) {
    case "KeepRequest.CustomerNameRequired":
      return { field: "customerName", message: "Please enter your name." };
    case "KeepRequest.CustomerNameTooLong":
      return { field: "customerName", message: "Name is too long (max 200 characters)." };
    case "KeepRequest.CustomerPhoneRequired":
      return { field: "customerPhone", message: "Please enter your phone number." };
    case "KeepRequest.CustomerPhoneTooLong":
      return { field: "customerPhone", message: "Phone number is too long." };
    case "KeepRequest.CustomerPhoneInvalidCharacters":
      return { field: "customerPhone", message: "Please enter a valid phone number." };
    case "KeepRequest.CustomerPhoneInvalidFormat":
      return { field: "customerPhone", message: "Please enter a 10-digit phone number." };
    case "KeepRequest.CustomerEmailTooLong":
      return { field: "customerEmail", message: "Email address is too long." };
    case "KeepRequest.CustomerEmailInvalid":
      return { field: "customerEmail", message: "Please enter a valid email address." };
    case "KeepRequest.DescriptionRequired":
      return { field: "description", message: "Please describe what you need help with." };
    case "KeepRequest.DescriptionTooLong":
      return { field: "description", message: "Description is too long (max 4000 characters)." };
    case "KeepRequest.ServiceAddressLine1Required":
      return { field: "serviceAddressLine1", message: "Please enter the service address." };
    case "KeepRequest.ServiceCityRequired":
      return { field: "serviceCity", message: "Please enter the city." };
    case "KeepRequest.ServiceStateRequired":
    case "KeepRequest.ServiceStateInvalid":
      return { field: "serviceState", message: "Please select a state." };
    default:
      return null;
  }
}

const inputClass =
  "w-full rounded-lg border border-[var(--ophalo-border)] bg-card px-4 py-3 text-sm " +
  "placeholder:text-muted-foreground focus:border-[var(--keep-accent)] focus:ring-1 " +
  "focus:ring-[var(--keep-accent)] focus:outline-none disabled:opacity-50";

const invalidInputClass =
  "border-destructive ring-1 ring-destructive focus:border-destructive focus:ring-destructive";

const labelClass = "block text-sm font-medium text-foreground mb-1.5";

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
  const [emailProvided, setEmailProvided] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [fieldError, setFieldError] = useState<{ field: KnownField; message: string } | null>(null);
  const [showAptUnit, setShowAptUnit] = useState(false);
  const [contactPreference, setContactPreference] = useState("NoPreference");
  const [urgency, setUrgency] = useState("Routine");
  const submitInFlight = useRef(false);
  const fieldRefs = useRef<Partial<Record<KnownField, HTMLElement | null>>>({});

  const biz = businessName ?? null;
  const trackerUrl = pageToken ? `/keep/r/${pageToken}` : null;

  async function handleSubmit(e: React.FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (submitInFlight.current) return;
    submitInFlight.current = true;
    setError(null);
    setFieldError(null);
    setStage("submitting");

    const data = new FormData(e.currentTarget);
    const customerName = String(data.get("customerName") ?? "").trim();
    const customerPhone = String(data.get("customerPhone") ?? "").trim();
    const customerEmailRaw = String(data.get("customerEmail") ?? "").trim();
    const customerEmail = customerEmailRaw.length > 0 ? customerEmailRaw : null;
    const description = String(data.get("description") ?? "").trim();
    const urgency = (data.get("urgency") as string) || "Routine";
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
          urgency,
          serviceAddressLine1, serviceAddressLine2, serviceCity, serviceState, serviceZip,
          contactPreference,
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
      setEmailProvided(!!customerEmail);
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

    const known = mapKnownFieldError(code);
    if (known) {
      setFieldError(known);
      setStage("form");
      submitInFlight.current = false;
      const el = fieldRefs.current[known.field];
      el?.focus();
      el?.scrollIntoView({ behavior: "smooth", block: "center" });
      return;
    }

    setError("Something went wrong. Please try again.");
    setStage("form");
    submitInFlight.current = false;
  }

  // ─── Terminal states ────────────────────────────────────────────────────────

  if (stage === "staff") {
    return (
      <main className="min-h-screen bg-[var(--ophalo-canvas)] px-4 py-6 sm:py-10">
        <div className="mx-auto w-full max-w-2xl space-y-4 sm:space-y-5">
          <KeepCardShell accentTop>
            <h1 className="font-serif text-base font-semibold text-foreground">
              You&apos;re signed in to this account.
            </h1>
            <p className="mt-2 text-sm text-muted-foreground">
              This is the customer intake form. Use Quick Capture or Create Request in the app
              to submit a request on behalf of a customer.
            </p>
            <div className="mt-4">
              <KeepButton
                variant="teal"
                onClick={() => { window.location.href = process.env.NEXT_PUBLIC_APP_BASE_URL ?? "/"; }}
              >
                Open the app
              </KeepButton>
            </div>
          </KeepCardShell>
          <KeepPageFooter />
        </div>
      </main>
    );
  }

  if (stage === "unavailable") {
    return (
      <main className="min-h-screen bg-[var(--ophalo-canvas)] px-4 py-6 sm:py-10">
        <div className="mx-auto w-full max-w-2xl space-y-4 sm:space-y-5">
          <KeepCardShell accentTop>
            <h1 className="font-serif text-base font-semibold text-foreground">
              This link is not available.
            </h1>
            <p className="mt-2 text-sm text-muted-foreground">
              This intake link is no longer active. If you were sent this link by a business,
              please contact them directly for assistance.
            </p>
          </KeepCardShell>
          <KeepPageFooter />
        </div>
      </main>
    );
  }

  if (stage === "success") {
    return (
      <main className="min-h-screen bg-[var(--ophalo-canvas)] px-4 py-6 sm:py-10">
        <div className="mx-auto w-full max-w-2xl space-y-4 sm:space-y-5">
          {biz && (
            <KeepBusinessHeader
              businessName={biz}
              label="Request submitted"
              className="pb-1"
            />
          )}
          <KeepCardShell accentTop>
            <h1 className="font-serif text-base font-semibold text-foreground">Request submitted.</h1>
            <p className="mt-2 text-sm text-muted-foreground">
              {biz ? `${biz} has received your request.` : "Your request has been received."}
            </p>

            {trackerUrl && (
              <p className="mt-3 text-sm text-muted-foreground">
                Your private request page is ready whenever you are. Save its link once you open
                it — it's the only way back to it.
              </p>
            )}

            {emailProvided && (
              <p className="mt-3 text-sm text-muted-foreground">
                A link to your request page has also been sent to your email address.
                Replies to that email may not be monitored — use your request page to send messages
                directly to {biz ?? "the business"}.
              </p>
            )}

            {trackerUrl && (
              <div className="mt-4">
                <KeepButton
                  variant="teal"
                  className="gap-2"
                  onClick={() => { window.location.href = trackerUrl; }}
                >
                  Open private request page
                  <ArrowRight className="h-4 w-4" aria-hidden />
                </KeepButton>
              </div>
            )}

            <p className="mt-4 text-xs text-muted-foreground">
              Reference: <span className="font-mono">{referenceCode}</span>
            </p>
          </KeepCardShell>
          <KeepPageFooter />
        </div>
      </main>
    );
  }

  // ─── Form ───────────────────────────────────────────────────────────────────

  const submitting = stage === "submitting";

  return (
    <main className="min-h-screen bg-[var(--ophalo-canvas)] px-4 py-6 sm:py-10">
      <div className="mx-auto w-full max-w-2xl">

        {/* Business identity header */}
        {biz ? (
          <KeepBusinessHeader
            businessName={biz}
            label="New request"
            className="mb-4 sm:mb-5"
          />
        ) : (
          <div className="mb-4 px-1 sm:mb-5">
            <p className="text-[10px] font-semibold uppercase tracking-widest text-muted-foreground">
              New request
            </p>
          </div>
        )}

        <form onSubmit={handleSubmit}>
          {/* Main card with teal accent */}
          <KeepCardShell accentTop>
            {/* Card headline */}
            <h1 className="font-serif text-2xl font-semibold leading-tight text-foreground sm:text-[28px]">
              {biz ? `What can ${biz} help with?` : "How can we help?"}
            </h1>
            <p className="mt-1.5 text-sm text-muted-foreground">
              {biz
                ? `Share a few details. After you submit, you'll get a private page to track this request and message ${biz}.`
                : "Share a few details. After you submit, you'll get a private page to track this request and message the business."}
            </p>

            <div className="mt-6 space-y-7">

            {/* ── Section 1: Request details ── */}
            <section>
              <KeepSectionHeader
                icon={<ClipboardList className="h-4 w-4" />}
                label="What do you need help with?"
              />
              <p className="mb-2 text-sm text-muted-foreground">
                Include what happened, when it started, and anything urgent.
              </p>
              <textarea
                id="description"
                name="description"
                rows={5}
                required
                disabled={submitting}
                ref={(el) => { fieldRefs.current.description = el; }}
                aria-invalid={fieldError?.field === "description"}
                aria-describedby={fieldError?.field === "description" ? "description-error" : undefined}
                className={inputClass + " resize-none" + (fieldError?.field === "description" ? " " + invalidInputClass : "")}
                placeholder="Example: My AC stopped blowing cold air last night. The fan is running but no cool air is coming out."
              />
              {fieldError?.field === "description" && (
                <p id="description-error" role="alert" className="mt-1.5 text-xs font-medium text-destructive">
                  {fieldError.message}
                </p>
              )}
            </section>

            {/* ── Section 2: Urgency ── */}
            <section>
              <KeepSectionHeader
                icon={<Clock className="h-4 w-4" />}
                label="How urgent is this?"
              />
              <select
                id="urgency"
                name="urgency"
                value={urgency}
                onChange={(e) => setUrgency(e.target.value)}
                disabled={submitting}
                className={inputClass + " text-base"}
              >
                <option value="Routine">Routine</option>
                <option value="Soon">Soon</option>
                <option value="Urgent">Urgent</option>
              </select>
              {urgency === "Urgent" ? (
                <div className="mt-2 flex gap-2 rounded-lg bg-[var(--ophalo-attention-bg)] px-3 py-2.5">
                  <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-[var(--ophalo-attention)]" />
                  <div>
                    <p className="text-xs font-semibold text-[var(--ophalo-attention)]">Marked urgent</p>
                    <p className="mt-0.5 text-xs text-[var(--ophalo-attention)]">
                      This request will be highlighted for the business. If there's an immediate safety risk, call emergency services first.
                    </p>
                  </div>
                </div>
              ) : urgency === "Soon" ? (
                <p className="mt-2 text-xs text-muted-foreground">
                  Use Soon when you'd like help in the next day or two.
                </p>
              ) : (
                <p className="mt-2 text-xs text-muted-foreground">
                  Use Routine for work that can be scheduled when convenient.
                </p>
              )}
            </section>

            {/* ── Section 3: Service location ── */}
            <section>
              <KeepSectionHeader
                icon={<MapPin className="h-4 w-4" />}
                label="Where is the service needed?"
              />
              <div className="mb-3 flex items-center gap-1.5 text-xs text-muted-foreground">
                <Lock className="h-3 w-3 shrink-0" aria-hidden />
                <span>
                  Shared with {biz ?? "this business"} only. Not shown on your request page.
                </span>
              </div>

              <div className="space-y-3">
                <div>
                  <label htmlFor="serviceAddressLine1" className={labelClass}>
                    Street address <span className="text-destructive">*</span>
                  </label>
                  <input
                    id="serviceAddressLine1"
                    name="serviceAddressLine1"
                    type="text"
                    autoComplete="address-line1"
                    required
                    disabled={submitting}
                    ref={(el) => { fieldRefs.current.serviceAddressLine1 = el; }}
                    aria-invalid={fieldError?.field === "serviceAddressLine1"}
                    aria-describedby={fieldError?.field === "serviceAddressLine1" ? "serviceAddressLine1-error" : undefined}
                    className={inputClass + (fieldError?.field === "serviceAddressLine1" ? " " + invalidInputClass : "")}
                    placeholder="123 Main St"
                  />
                  {fieldError?.field === "serviceAddressLine1" && (
                    <p id="serviceAddressLine1-error" role="alert" className="mt-1.5 text-xs font-medium text-destructive">
                      {fieldError.message}
                    </p>
                  )}
                </div>

                {showAptUnit ? (
                  <div>
                    <label htmlFor="serviceAddressLine2" className={labelClass}>
                      Apt / unit{" "}
                      <span className="font-normal text-muted-foreground">(optional)</span>
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
                ) : (
                  <button
                    type="button"
                    onClick={() => setShowAptUnit(true)}
                    className="text-xs font-medium text-[var(--ophalo-navy)] underline-offset-2 hover:underline focus-visible:outline-none"
                  >
                    + Add apartment / unit
                  </button>
                )}

                <div className="grid grid-cols-1 gap-3 sm:grid-cols-[1fr_120px_100px]">
                  <div>
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
                      ref={(el) => { fieldRefs.current.serviceCity = el; }}
                      aria-invalid={fieldError?.field === "serviceCity"}
                      aria-describedby={fieldError?.field === "serviceCity" ? "serviceCity-error" : undefined}
                      className={inputClass + (fieldError?.field === "serviceCity" ? " " + invalidInputClass : "")}
                      placeholder="City"
                    />
                    {fieldError?.field === "serviceCity" && (
                      <p id="serviceCity-error" role="alert" className="mt-1.5 text-xs font-medium text-destructive">
                        {fieldError.message}
                      </p>
                    )}
                  </div>

                  <div>
                    <label htmlFor="serviceState" className={labelClass}>
                      State <span className="text-destructive">*</span>
                    </label>
                    <select
                      id="serviceState"
                      name="serviceState"
                      required
                      disabled={submitting}
                      defaultValue=""
                      ref={(el) => { fieldRefs.current.serviceState = el; }}
                      aria-invalid={fieldError?.field === "serviceState"}
                      aria-describedby={fieldError?.field === "serviceState" ? "serviceState-error" : undefined}
                      className={inputClass + " text-base" + (fieldError?.field === "serviceState" ? " " + invalidInputClass : "")}
                    >
                      <option value="" disabled>State</option>
                      {US_STATES.map(([code, name]) => (
                        <option key={code} value={code}>{name}</option>
                      ))}
                    </select>
                    {fieldError?.field === "serviceState" && (
                      <p id="serviceState-error" role="alert" className="mt-1.5 text-xs font-medium text-destructive">
                        {fieldError.message}
                      </p>
                    )}
                  </div>

                  <div>
                    <label htmlFor="serviceZip" className={labelClass}>
                      ZIP{" "}
                      <span className="font-normal text-muted-foreground">(opt.)</span>
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
            </section>

            {/* ── Section 4: Contact ── */}
            <section>
              <KeepSectionHeader
                icon={<UserRound className="h-4 w-4" />}
                label="Who should we contact?"
              />
              <p className="mb-3 text-xs text-muted-foreground">
                Shared only with {biz ?? "this business"} to respond to this request. Not used for
                marketing or sold.
              </p>

              <div className="space-y-3">
                <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                  <div>
                    <label htmlFor="customerName" className={labelClass}>
                      Name <span className="text-destructive">*</span>
                    </label>
                    <input
                      id="customerName"
                      name="customerName"
                      type="text"
                      autoComplete="name"
                      required
                      disabled={submitting}
                      ref={(el) => { fieldRefs.current.customerName = el; }}
                      aria-invalid={fieldError?.field === "customerName"}
                      aria-describedby={fieldError?.field === "customerName" ? "customerName-error" : undefined}
                      className={inputClass + (fieldError?.field === "customerName" ? " " + invalidInputClass : "")}
                      placeholder="Your name"
                    />
                    {fieldError?.field === "customerName" && (
                      <p id="customerName-error" role="alert" className="mt-1.5 text-xs font-medium text-destructive">
                        {fieldError.message}
                      </p>
                    )}
                  </div>

                  <div>
                    <label htmlFor="customerPhone" className={labelClass}>
                      Phone <span className="text-destructive">*</span>
                    </label>
                    <input
                      id="customerPhone"
                      name="customerPhone"
                      type="tel"
                      autoComplete="tel"
                      inputMode="tel"
                      required
                      disabled={submitting}
                      ref={(el) => { fieldRefs.current.customerPhone = el; }}
                      aria-invalid={fieldError?.field === "customerPhone"}
                      aria-describedby={fieldError?.field === "customerPhone" ? "customerPhone-error" : undefined}
                      className={inputClass + (fieldError?.field === "customerPhone" ? " " + invalidInputClass : "")}
                      placeholder="(555) 000-0000"
                    />
                    {fieldError?.field === "customerPhone" && (
                      <p id="customerPhone-error" role="alert" className="mt-1.5 text-xs font-medium text-destructive">
                        {fieldError.message}
                      </p>
                    )}
                  </div>
                </div>

                <div>
                  <label htmlFor="contactPreference" className={labelClass}>
                    Preferred contact method
                  </label>
                  <select
                    id="contactPreference"
                    value={contactPreference}
                    onChange={(e) => setContactPreference(e.target.value)}
                    disabled={submitting}
                    className={inputClass + " text-base"}
                  >
                    <option value="NoPreference">No preference</option>
                    <option value="TextMessage">Text message</option>
                    <option value="PhoneCall">Phone call</option>
                    <option value="Email">Email</option>
                  </select>
                </div>

                <div>
                  <label htmlFor="customerEmail" className={labelClass}>
                    Email{" "}
                    {contactPreference === "Email" ? (
                      <span className="text-destructive">*</span>
                    ) : (
                      <span className="font-normal text-muted-foreground">(optional, recommended)</span>
                    )}
                  </label>
                  <input
                    id="customerEmail"
                    name="customerEmail"
                    type="email"
                    autoComplete="email"
                    inputMode="email"
                    required={contactPreference === "Email"}
                    disabled={submitting}
                    ref={(el) => { fieldRefs.current.customerEmail = el; }}
                    aria-invalid={fieldError?.field === "customerEmail"}
                    aria-describedby={fieldError?.field === "customerEmail" ? "customerEmail-error" : undefined}
                    className={inputClass + (fieldError?.field === "customerEmail" ? " " + invalidInputClass : "")}
                    placeholder="you@example.com"
                  />
                  {fieldError?.field === "customerEmail" && (
                    <p id="customerEmail-error" role="alert" className="mt-1.5 text-xs font-medium text-destructive">
                      {fieldError.message}
                    </p>
                  )}
                  <p className="mt-1.5 text-xs text-muted-foreground">
                    We&apos;ll send a link to your private request page so you can find your way
                    back to it. This isn&apos;t used for marketing.
                  </p>
                </div>
              </div>
            </section>

            </div>{/* end space-y-7 sections */}

            {/* ── Submit ── */}
            <div className="mt-6">
              {error && (
                <div className="mb-4 rounded-lg border border-destructive/30 bg-destructive/5 px-4 py-3">
                  <p className="text-sm text-destructive">{error}</p>
                </div>
              )}

              <KeepButton
                type="submit"
                variant="teal"
                disabled={submitting}
                className="w-full min-h-[42px] gap-2"
              >
                {submitting ? (
                  "Submitting…"
                ) : (
                  <>
                    Submit request
                    <ArrowRight className="h-4 w-4" aria-hidden />
                  </>
                )}
              </KeepButton>

              <p className="mt-3 text-center text-xs text-muted-foreground">
                You&apos;ll get a private request page after submitting.
              </p>
            </div>
          </KeepCardShell>
        </form>

        <KeepPageFooter />
      </div>
    </main>
  );
}
