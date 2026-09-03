import { describe, it, expect } from "vitest";
import type { Event as SentryEvent } from "@sentry/react";
import { safePathname, scrubBrowserEvent } from "../sentryScrub";

describe("safePathname", () => {
  it("drops query string and fragment", () => {
    expect(safePathname("https://app.example.com/requests?email=jane@x.com#/request/abc")).toBe(
      "/requests",
    );
  });

  it("redacts an opaque token segment", () => {
    expect(safePathname("/keep/r/Ab_3xQ9zLmN0pQrStUvWxYz1")).toBe("/keep/r/[redacted]");
    expect(
      safePathname("/request/3f1c9a2b-7d44-4e0a-9b1c-2a2b3c4d5e6f/detail"),
    ).toBe("/request/[redacted]/detail");
  });

  it("returns undefined for an unparseable value and '/' for root", () => {
    expect(safePathname(undefined)).toBeUndefined();
    expect(safePathname("https://app.example.com/")).toBe("/");
  });
});

describe("scrubBrowserEvent", () => {
  // Realistic Sentry-generated identifiers: a 32-hex event id and a 40-hex Git release SHA.
  // Both match OPAQUE_SEGMENT and must NOT cause the event to be discarded.
  const EVENT_ID = "9f8e7d6c5b4a39281706f5e4d3c2b1a0";
  const RELEASE_SHA = "1a2b3c4d5e6f7a8b9c0d1e2f3a4b5c6d7e8f9a0b";

  function fullEvent(): SentryEvent {
    return {
      event_id: EVENT_ID,
      release: RELEASE_SHA,
      environment: "production",
      level: "error",
      platform: "javascript",
      user: { id: "acct_9", email: "owner@contractor.example", ip_address: "203.0.113.7" },
      request: {
        url: "https://app.example.com/request/abc?token=SECRETVALUE#/request/xyz",
        headers: { Cookie: "session=abcdef" },
        data: { note: "customer Jane Doe at 12 Elm St" },
      },
      breadcrumbs: [{ message: "clicked customer 12 Elm St", timestamp: 1 }],
      contexts: { device: { name: "owner-iphone" } },
      extra: { customerPhone: "+1 555 0100" },
      tags: { customerId: "cust_42" },
      exception: {
        values: [
          {
            type: "TypeError",
            value: "Cannot read 'phone' of customer Jane Doe (12 Elm St, 90210)",
            mechanism: { type: "generic", handled: true, synthetic: false },
            stacktrace: {
              frames: [
                {
                  filename: "https://app.example.com/assets/index-abc.js?v=1",
                  function: "renderCustomer",
                  lineno: 42,
                  colno: 7,
                  in_app: true,
                  context_line: "  const phone = customer.phone; // Jane Doe",
                  pre_context: ["// 12 Elm St"],
                  post_context: ["}"],
                  vars: { customer: { name: "Jane Doe" } },
                },
              ],
            },
          },
        ],
      },
    } as SentryEvent;
  }

  it("rebuilds the event with only allowlisted fields", () => {
    const scrubbed = scrubBrowserEvent(fullEvent());
    expect(scrubbed).not.toBeNull();

    expect(scrubbed!.event_id).toBe(EVENT_ID);
    expect(scrubbed!.release).toBe(RELEASE_SHA);
    expect(scrubbed!.environment).toBe("production");
    expect(scrubbed!.request?.url).toBe("/request/abc");

    // Nothing identifying or customer-bearing survives.
    expect(scrubbed!.user).toBeUndefined();
    expect(scrubbed!.breadcrumbs).toBeUndefined();
    expect(scrubbed!.contexts).toBeUndefined();
    expect(scrubbed!.extra).toBeUndefined();
    expect(scrubbed!.tags).toBeUndefined();
    expect((scrubbed!.request as Record<string, unknown>).headers).toBeUndefined();
    expect((scrubbed!.request as Record<string, unknown>).data).toBeUndefined();
  });

  it("keeps exception type and frame metadata but drops message, source lines and locals", () => {
    const scrubbed = scrubBrowserEvent(fullEvent());
    const ex = scrubbed!.exception!.values![0];

    expect(ex.type).toBe("TypeError");
    expect(ex.value).toBeUndefined();
    expect(ex.mechanism).toEqual({ type: "generic", handled: true, synthetic: false });

    const frame = ex.stacktrace!.frames![0] as Record<string, unknown>;
    expect(frame.function).toBe("renderCustomer");
    expect(frame.lineno).toBe(42);
    expect(frame.in_app).toBe(true);
    expect(frame.filename).toBe("/assets/index-abc.js");
    expect(frame.context_line).toBeUndefined();
    expect(frame.pre_context).toBeUndefined();
    expect(frame.vars).toBeUndefined();
  });

  it("serializes to JSON with no customer text anywhere", () => {
    const json = JSON.stringify(scrubBrowserEvent(fullEvent()));
    expect(json).not.toMatch(/Jane Doe/);
    expect(json).not.toMatch(/Elm St/);
    expect(json).not.toMatch(/owner@contractor/);
    expect(json).not.toMatch(/SECRETVALUE/);
    expect(json).not.toMatch(/[?#]/);
  });

  it("retains a normal event whose Sentry-generated id and release SHA are hex tokens", () => {
    const evt = {
      event_id: EVENT_ID,
      release: RELEASE_SHA,
      environment: "production",
      level: "error",
      exception: {
        values: [
          {
            type: "RangeError",
            value: "invalid array length",
            stacktrace: {
              frames: [
                { filename: "https://app.example.com/assets/index-abc.js", function: "load", lineno: 3 },
              ],
            },
          },
        ],
      },
    } as SentryEvent;

    const scrubbed = scrubBrowserEvent(evt);
    expect(scrubbed).not.toBeNull();
    expect(scrubbed!.event_id).toBe(EVENT_ID);
    expect(scrubbed!.release).toBe(RELEASE_SHA);
    expect(scrubbed!.exception!.values![0].type).toBe("RangeError");
  });

  it("discards the event entirely if an opaque token still appears after rebuild", () => {
    const evt = {
      release: "r",
      environment: "production",
      exception: {
        values: [{ type: "Error", stacktrace: { frames: [{ function: "AbCdEf0123456789abcdef" }] } }],
      },
    } as SentryEvent;
    expect(scrubBrowserEvent(evt)).toBeNull();
  });
});
