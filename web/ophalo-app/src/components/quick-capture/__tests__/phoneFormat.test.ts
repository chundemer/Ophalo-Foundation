import { describe, it, expect } from "vitest";
import { normalizeNaPhoneInput, formatNaPhone } from "../utils";

// GAP-051 / ADR-444: staff-facing PWA phone entry must format as-you-type like the
// public intake form, while still normalizing to canonical 10-digit NANP values for
// lookup/API calls, including an optional leading "1" or "+1" country-code prefix.

describe("normalizeNaPhoneInput", () => {
  it("passes through plain 10 digits", () => {
    expect(normalizeNaPhoneInput("5555555555")).toBe("5555555555");
  });

  it("handles partial entry", () => {
    expect(normalizeNaPhoneInput("555")).toBe("555");
    expect(normalizeNaPhoneInput("555555")).toBe("555555");
  });

  it("strips formatting punctuation from a full paste", () => {
    expect(normalizeNaPhoneInput("(555) 555-5555")).toBe("5555555555");
  });

  it("drops a leading +1 country-code prefix", () => {
    expect(normalizeNaPhoneInput("+1 (555) 555-5555")).toBe("5555555555");
  });

  it("drops a leading bare 1 country-code prefix", () => {
    expect(normalizeNaPhoneInput("15555555555")).toBe("5555555555");
  });

  it("drops a leading 1 typed digit-by-digit before the rest of the number", () => {
    let value = "";
    for (const ch of "15555555555") {
      value += ch;
    }
    expect(normalizeNaPhoneInput(value)).toBe("5555555555");
  });

  it("caps at 10 digits for excess-length input", () => {
    expect(normalizeNaPhoneInput("555555555599")).toBe("5555555555");
  });

  it("returns empty string for empty input", () => {
    expect(normalizeNaPhoneInput("")).toBe("");
  });
});

describe("formatNaPhone", () => {
  it("formats a complete number", () => {
    expect(formatNaPhone("5555555555")).toBe("(555) 555-5555");
  });

  it("formats partial entry as the user types", () => {
    expect(formatNaPhone("5")).toBe("(5");
    expect(formatNaPhone("555")).toBe("(555");
    expect(formatNaPhone("5555")).toBe("(555) 5");
    expect(formatNaPhone("555555")).toBe("(555) 555");
    expect(formatNaPhone("5555555")).toBe("(555) 555-5");
  });

  it("returns empty string for empty canonical input", () => {
    expect(formatNaPhone("")).toBe("");
  });

  it("ignores any stray non-digit characters already present", () => {
    expect(formatNaPhone("(555) 555-5555")).toBe("(555) 555-5555");
  });
});
