import type { PhoneLookupResult } from "../../lib/apiClient";

export type CaptureFormDraft = {
  name: string;
  email: string;
  description: string;
  source: string;
  showAddress: boolean;
  addrLine1: string;
  addrLine2: string;
  addrCity: string;
  addrState: string;
  addrZip: string;
};

export type Stage =
  | { kind: "handoff" }
  | { kind: "lookup" }
  | { kind: "result"; lookup: PhoneLookupResult; lockedPhone: string }
  | { kind: "capture"; prefill: { name?: string; email?: string } | null; lockedPhone: string }
  | { kind: "success"; requestId: string; referenceCode: string; pageToken: string; customerPhone: string; customerEmail: string | null; customerName: string };

export const SOURCE_OPTIONS = [
  { label: "Phone Call", value: "phone" },
  { label: "Voicemail", value: "voicemail" },
  { label: "Text Thread", value: "text" },
  { label: "Email", value: "email" },
  { label: "Walk-In", value: "walk_in" },
  { label: "Referral", value: "referral" },
  { label: "Other", value: "other" },
] as const;

export function stripToDigits(raw: string): string {
  return raw.replace(/\D/g, "");
}

export function isPhoneShaped(text: string): boolean {
  const digits = stripToDigits(text);
  return digits.length >= 7 && digits.length <= 15;
}

/**
 * Normalizes typed/pasted phone input to the canonical 10-digit NANP value
 * used by the API and lookup. A leading "1" (a typed or pasted "+1"/"1"
 * country-code prefix) is dropped — no valid NANP area code starts with 1,
 * so this is unambiguous whether it arrives mid-type or via a full paste.
 */
export function normalizeNaPhoneInput(raw: string): string {
  let digits = stripToDigits(raw);
  if (digits.startsWith("1")) digits = digits.slice(1);
  return digits.slice(0, 10);
}

/** Formats canonical NANP digits as the customer sees them: (555) 555-5555. */
export function formatNaPhone(digits: string): string {
  const d = stripToDigits(digits).slice(0, 10);
  if (d.length === 0) return "";
  if (d.length < 4) return `(${d}`;
  if (d.length < 7) return `(${d.slice(0, 3)}) ${d.slice(3)}`;
  return `(${d.slice(0, 3)}) ${d.slice(3, 6)}-${d.slice(6)}`;
}

export function formatStatus(slug: string): string {
  const map: Record<string, string> = {
    received: "Received",
    scheduled: "Scheduled",
    in_progress: "Active",
    pending_customer: "Waiting on Customer",
    resolved: "Resolved",
    closed: "Closed",
    cancelled: "Cancelled",
    spam: "Spam",
    test: "Test",
  };
  return map[slug] ?? slug;
}
