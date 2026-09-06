# OpHalo Workboard

**Canonical forward-looking board.** Update it in the same commit as the work it records. ADRs own decisions and contracts; build logs are append-only delivery history. The legacy [pilot readiness tracker](pilot-readiness-bug-tracker.md) is frozen as an archive.

## Operating rules

- An item enters **Now** only with a written decision, acceptance criteria, owner, and no unresolved prerequisite. Move its status in the delivery commit.
- **Next** is ordered but unstarted. **Decision queue** is unschedulable. **Deferred** is deliberately outside the current pilot sequence. **Done** needs evidence.
- Keep `session-log.md` to a short handoff; it does not duplicate this board or build-log history.

## Now

| Item | Owner | Outcome / acceptance |
| --- | --- | --- |
| GAP-039 Batch 4 | Founder | Configure Railway/Vercel/Sentry DSNs, `/health/ready`, release/environment identity and founder alert; verify delivery and run the production-candidate/redaction gate. [ADR-495](decisions/ADR-495-gap-039-redacted-error-capture-and-release-safety.md), [BL140](build-log/140-gap-039-sentry-implementation-handoff.md). |
| GAP-069 | Founder decision → engineering | Choose Railway persistent volume or external key store, record ownership/rotation/restore, then persist/protect the API key ring and narrowly trust the Railway proxy. Redeploy preserves keys; spoofed forwarded headers fail; startup warnings are gone. |

## Next

1. **GAP-040 — marketing-site accuracy.** Public copy, claims, and visuals truthfully match shipped V1 before the intake link is marketed.
2. **GAP-063 — Spam/Test action.** Owner/Admin can make the existing authorized terminal classification from Request Detail, with accessible confirmation, optional ≤500-character reason, and truthful post-action state. [ADR-296](decisions/decision-index.md).
3. **GAP-048 — share intent.** Private-page email goes through informed share confirmation; `mailto:` is never delivery evidence.
4. **GAP-049 — follow-up truncation.** Reserve provenance-prefix space and safely truncate copied text so a max-length closed request always yields a valid follow-up.

These are supervised-pilot gates alongside GAP-039 and GAP-069. GAP-047 is required only if staff uses Internal priority for triage; it remains behind its Request Detail foundation sequence.

## Decision queue

| Item | Decision required before scheduling |
| --- | --- |
| GAP-064 | Minimum accountable staff-alert policy: recipient/fallback, channel, failure/escalation, quiet-hours, and privacy posture. Interim: founder watches the queue. |
| GAP-025 | Customer phone-number lifecycle. ADR-492 remains only the narrow request-phone continuity guardrail; no inferred identity matching. |
| GAP-043 | V1 request-list scale model and verification threshold. |
| GAP-054 | App-shell/top-bar review: business/account menu, persistent workspace switcher, entitled Price Book settings attachment, and narrow/mobile navigation. |
| GAP-070 / GAP-071 | Optional-module commercial workflow and truthful discovery/handoff; no self-service entitlement write or guided setup. |

## Deferred / pilot learning

- **GAP-037, GAP-038:** founder value report and in-product feedback/help loop.
- **GAP-041, GAP-046, GAP-026, GAP-053:** request-list selection, filter, search, and action-order refinements.
- **GAP-044:** completed/cancelled-work discoverability.
- **GAP-047:** internal-priority failure feedback; only if the triage workflow relies on it.
- **GAP-051 native parity:** Session 14; public-web delivery is done.
- **GAP-066:** Catalog Item financial/operational-impact workspace.
- **GAP-067:** request-workspace presentation pass; blocks GAP-042 until screenshot acceptance.
- **GAP-070, GAP-071:** P2 optional-module work, pending their Decision Queue entry.
- **4g close advisory, Price Book direct-cost visibility, workbench brand alignment:** carry-forward items; not pilot blockers.
- **Acceptance passes:** Request UI Upgrade 1.1 ([BL139](build-log/139-request-ui-upgrade-1.1-implementation.md)) and Settings & Getting Started V2 §5 ([BL144](build-log/144-settings-and-getting-started-v2-upgrade.md)).
- **Minimum Office Closeout:** resumes after controlled-pilot and rehearsal gates ([BL135](build-log/135-minimum-office-closeout-mechanical-preflight.md)).

## Done / evidence index

| Item | Evidence |
| --- | --- |
| GAP-033 | Public-intake trust/event allowlist: `11c19d3d`, `89a776d8`; [BL145](build-log/145-gap-033-public-intake-trust-and-event-feed-allowlist.md). |
| GAP-039 implementation | API/PWA Sentry code/runbooks: `d7d0ee22`, `fd34af34`, `baf07265`, `a69a8edf`, `70e75a3f`; operational Batch 4 remains Now. |
| GAP-065 / GAP-065A | Financial-review discovery: `faf7b64`, `e27c48c`, `6ab880b`, `baaeff1`, `f231126`, `606203d`; [BL138](build-log/138-gap-065-owner-admin-financial-review-discovery-and-delivery-plan.md). |
| GAP-068 | Multi-workspace sign-in/invited name: `755c7eaf`, verification `2cda916d`; [BL143](build-log/143-multi-workspace-signin-and-invited-name-handoff.md). |
| GAP-016 / GAP-021 | ADR-444 phone path; native parity separately deferred. |
| GAP-051 public web | Configured business-phone display; [BL147](build-log/147-gap-051-public-web-business-phone-display.md). |

## Pilot gate checklist

Before a supervised customer-facing pilot: GAP-039 Batch 4, GAP-069, GAP-040, GAP-063, GAP-048 before sharing private pages, and GAP-049 before relying on closed-request follow-ups. GAP-064 needs a written alert-policy decision; until then the founder deliberately watches the queue.
