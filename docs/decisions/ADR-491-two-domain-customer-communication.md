# ADR-491 — Two-Domain Customer Communication

**Status:** Locked  
**Date:** 2026-08-23  
**Related:** ADR-421, ADR-432, ADR-451, ADR-489, ADR-490

## Decision

Keep exposes two, and only two, customer-communication domains on Request Detail:

1. **Post customer-page update** — an official, customer-visible update on the private
   request page. It may include a status change. After posting, the owner may prepare a
   linked SMS or email notification and must explicitly confirm it was actually sent.
2. **Contact customer / Log direct contact** — an internal record of a real-world phone,
   SMS, email, in-person, or other contact. It records direction, channel, outcome,
   follow-up obligation, and summary. It is never published to the customer page.

`Scan to call` and `Scan to text` are device-handoff utilities within the second domain,
not independent workflows. The Contact customer drawer supports both an already-completed
contact and a contact initiated now: desktop shows the channel-appropriate opaque QR handoff;
mobile opens the native launcher. Launching or scanning never creates a Keep event. The
operator explicitly logs the outcome afterward.

All outbound native SMS/email drafts include the associated private request-page link where
the platform supports a prefilled draft. This aligns direct-contact handoffs with the existing
customer-update notification contract.

## Truth and attention boundary

The existing ADR-451 attestation boundary remains unchanged. A page post, QR scan, native-app
launch, prepared draft, cancellation, failed launch, no-answer attempt, and voicemail never
claim that the customer was informed. Only a qualifying, explicitly saved direct-contact result
or an explicitly confirmed update notification may resolve applicable customer-waiting
attention. A customer-requested call is resolved only by a logged live phone call.

The interface must state these consequences where staff select an outcome; it must not rely on
owners remembering policy from documentation.

## Refinements

- Canonical UI labels are **Post customer-page update** (public domain), **Contact customer**
  (external-contact entry), and **Log contact** (the final internal-record commit). Queue guidance
  uses `Next: Contact customer`. The public composer says `Visible on the customer page. The
  customer will not be notified unless you send a text or email.` Internal controls sit beneath
  an explicit **Internal planning** boundary.
- After a saved direct contact with a usable summary, Keep offers a quiet, non-blocking
  **Post public update based on this call** action. It opens the inline customer-page composer
  with the summary as an editable draft; it never posts automatically.
- Customer-page-update timeline presentation exposes its notification state truthfully:
  `Live on page · notification skipped` when the owner selects Not now, and
  `Live on page · SMS confirmed` or `Live on page · email confirmed` only after confirmation.
- Changing Phone, Text, or Email in the Contact customer drawer preserves unsaved summary text.
  Channel changes may update only the relevant handoff utility and outcome fields.

## Consequences

The request workbench has one public-transparency surface and one internal-audit surface. It
does not merge private notes into public writing, and it does not require an owner to decide
whether QR launchers are separate communication tools. Existing server-side event and attention
rules remain the authority; this decision reorganizes and clarifies their presentation.
