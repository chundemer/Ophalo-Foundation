import { describe, expect, it } from "vitest";
import { mockRequestDetails, mockRequestSummaries } from "../../../mocks/fixtures";
import { buildAttentionGuidance } from "../helpers";

// ADR-489/490: each condition that admits a row to Needs Attention must have one
// server-ranked effective-attention reason that produces Request Detail guidance.
describe("Needs Attention row matches detail guidance", () => {
  const cases = [
    {
      name: "persisted attention",
      detail: {
        ...mockRequestDetails["mock-req-001"],
        effectiveAttention: {
          level: "needs_attention",
          reason: "customer_message",
          dueAtUtc: "2026-07-01T15:00:00Z",
          dueOnDate: null,
          guidanceKey: "respond_to_customer",
        },
      },
      expectedLabel: "Customer message",
      expectedResolveBy: "Send a customer-page update, or log contact if you handle it by phone, text, email, or in person.",
    },
    {
      name: "due Follow Up On promise",
      detail: {
        ...mockRequestDetails["mock-req-001"],
        effectiveAttention: {
          level: "overdue",
          reason: "follow_up_due",
          dueAtUtc: null,
          dueOnDate: "2026-07-01",
          guidanceKey: "resolve_follow_up",
        },
      },
      expectedLabel: "Follow up due",
      expectedResolveBy: "Resolve by Jul 1, 2026.",
    },
    {
      name: "overdue first response",
      detail: {
        ...mockRequestDetails["mock-req-001"],
        effectiveAttention: {
          level: "overdue",
          reason: "first_response_due",
          dueAtUtc: "2026-07-01T15:00:00Z",
          dueOnDate: null,
          guidanceKey: "respond_to_customer",
        },
      },
      expectedLabel: "First response due",
      expectedResolveBy: "Send the first customer-page update, or log a real external contact if you respond outside Keep.",
    },
    {
      name: "time-sensitive timing request",
      detail: {
        ...mockRequestDetails["mock-req-001"],
        effectiveAttention: {
          level: "needs_attention",
          reason: "timing_change_requested",
          dueAtUtc: "2026-07-01T15:00:00Z",
          dueOnDate: null,
          guidanceKey: "log_external_contact",
        },
      },
      expectedLabel: "Timing change requested",
      expectedResolveBy: "Contact the customer by phone, text, or email, then save what happened in Keep. A customer-page update alone does not notify them.",
    },
  ];

  it.each(cases)("guides the detail for a Needs Attention row: $name", ({ detail, expectedLabel, expectedResolveBy }) => {
    const row = { ...mockRequestSummaries[0], rowContext: "needs_attention" };
    const guidance = buildAttentionGuidance(detail);

    expect(row.rowContext).toBe("needs_attention");
    expect(guidance).not.toBeNull();
    expect(guidance).toMatchObject({ label: expectedLabel, resolveBy: expectedResolveBy });
  });
});
