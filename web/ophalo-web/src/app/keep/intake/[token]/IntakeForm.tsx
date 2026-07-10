"use client";

import { useRef, useState } from "react";
import { AlertTriangle, ArrowRight, ClipboardList, Lock, MapPin, UserRound } from "lucide-react";
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
  const [showAptUnit, setShowAptUnit] = useState(false);
  const [showEmail, setShowEmail] = useState(false);
  const [contactPreference, setContactPreference] = useState("NoPreference");
  const submitInFlight = useRef(false);

  const biz = businessName ?? null;

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
    const trackerUrl = pageToken ? `/keep/r/${pageToken}` : null;

    return (
      <main className="min-h-screen bg-[var(--ophalo-canvas)] px-4 py-6 sm:py-10">
        <div className="mx-auto w-full max-w-2xl space-y-4 sm:space-y-5">
          {biz && (
            <KeepBusinessHeader
              businessName={biz}
              label="New request form"
              className="pb-1"
            />
          )}
          <KeepCardShell accentTop>
            <h1 className="font-serif text-base font-semibold text-foreground">Request submitted.</h1>
            <p className="mt-2 text-sm text-muted-foreground">
              Your request has been received. Your reference code is:
            </p>
            <p className="mt-2 rounded-lg bg-[var(--ophalo-canvas)] px-4 py-3 font-mono text-sm font-semibold text-foreground">
              {referenceCode}
            </p>
            <p className="mt-3 text-sm text-muted-foreground">
              You can check the status of your request and send additional details from your
              request page.
            </p>
            {trackerUrl && (
              <div className="mt-4">
                <KeepButton
                  variant="teal"
                  className="gap-2"
                  onClick={() => { window.location.href = trackerUrl; }}
                >
                  View your request page
                  <ArrowRight className="h-4 w-4" aria-hidden />
                </KeepButton>
              </div>
            )}
            <p className="mt-4 text-xs text-muted-foreground">
              Tip: bookmark this page or save the link to check back anytime — no account required.
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
            label="New request form"
            className="mb-4 sm:mb-5"
          />
        ) : (
          <div className="mb-4 px-1 sm:mb-5">
            <p className="text-[10px] font-semibold uppercase tracking-widest text-muted-foreground">
              New request form
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
                ? `Share a few details and ${biz} will follow up with your private request link.`
                : "Share a few details and the business will follow up with your private request link."}
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
                className={inputClass + " resize-none"}
                placeholder="Example: My AC stopped blowing cold air last night. The fan is running but no cool air is coming out."
              />
            </section>

            {/* ── Section 2: Urgency ── */}
            <section>
              <KeepSectionHeader
                icon={<AlertTriangle className="h-4 w-4" />}
                label="How urgent is this?"
              />
              <select
                id="urgency"
                name="urgency"
                defaultValue="Routine"
                disabled={submitting}
                className={inputClass + " text-base"}
              >
                <option value="Routine">Routine — schedule when convenient</option>
                <option value="Soon">Soon — I'd like this handled in the next day or two</option>
                <option value="Urgent">Urgent — active issue affecting my home or business</option>
              </select>
              <p className="mt-2 text-xs text-muted-foreground">
                Examples of urgent: active leak, no heat or A/C, safety concern, lockout, or service interruption.
              </p>
              <p className="mt-1 text-xs text-muted-foreground">
                If this is an immediate safety emergency, call emergency services first.
              </p>
            </section>

            {/* ── Section 3: Service location ── */}
            <section>
              <KeepSectionHeader
                icon={<MapPin className="h-4 w-4" />}
                label="Where is the service needed?"
              />

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
                    className={inputClass}
                    placeholder="123 Main St"
                  />
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
                      className={inputClass}
                      placeholder="City"
                    />
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
                      className={inputClass + " text-base"}
                    >
                      <option value="" disabled>State</option>
                      {US_STATES.map(([code, name]) => (
                        <option key={code} value={code}>{name}</option>
                      ))}
                    </select>
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

              <div className="mt-3 flex items-center gap-1.5 text-xs text-muted-foreground">
                <Lock className="h-3 w-3 shrink-0" aria-hidden />
                <span>
                  Shared with {biz ?? "this business"} only. Not shown on your request page.
                </span>
              </div>
            </section>

            {/* ── Section 4: Contact ── */}
            <section>
              <KeepSectionHeader
                icon={<UserRound className="h-4 w-4" />}
                label="Who should we contact?"
              />

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
                      className={inputClass}
                      placeholder="Your name"
                    />
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
                      className={inputClass}
                      placeholder="(555) 000-0000"
                    />
                  </div>
                </div>

                <div>
                  <label htmlFor="contactPreference" className={labelClass}>
                    Preferred contact method
                  </label>
                  <select
                    id="contactPreference"
                    value={contactPreference}
                    onChange={(e) => {
                      setContactPreference(e.target.value);
                      if (e.target.value === "Email") setShowEmail(true);
                    }}
                    disabled={submitting}
                    className={inputClass + " text-base"}
                  >
                    <option value="NoPreference">No preference</option>
                    <option value="TextMessage">Text message</option>
                    <option value="PhoneCall">Phone call</option>
                    <option value="Email">Email</option>
                  </select>
                </div>

                {(showEmail || contactPreference === "Email") ? (
                  <div>
                    <label htmlFor="customerEmail" className={labelClass}>
                      Email{" "}
                      {contactPreference === "Email" ? (
                        <span className="text-destructive">*</span>
                      ) : (
                        <span className="font-normal text-muted-foreground">(optional)</span>
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
                      className={inputClass}
                      placeholder="you@example.com"
                    />
                  </div>
                ) : (
                  <button
                    type="button"
                    onClick={() => setShowEmail(true)}
                    className="text-xs font-medium text-[var(--ophalo-navy)] underline-offset-2 hover:underline focus-visible:outline-none"
                  >
                    + Add optional email for backup tracking
                  </button>
                )}
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
