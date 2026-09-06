# BL145 — GAP-033 public-intake trust and tracker event-feed allowlist

**Status:** Complete. Two slices, both merged to `main` (`11c19d3d`, `89a776d8`). Closes the
GAP-033 pilot gate ("Required before enabling public customer intake"). Real-phone review is
folded into the BL089 launch verification pass.

**Authority:** ADR-446 (brand presence and first-visit trust), ADR-431 (public identity
hierarchy / neutral bridge copy), ADR-432 (transactional tracker-link email allowed but
fail-soft), pilot-readiness-bug-tracker GAP-033.

## Prior work (context)

Business-first identity and configured-contact display on public intake, the public-safe identity
projection that never returns customer email, and known-business terminal-state identity retention
landed earlier under R90b-1/2a/2b (`75f472f`, migration `20260718013055`). This build log covers
only the remaining GAP-033 scope.

## Slice A — truthful public intake confirmation and copy (`11c19d3d`)

`ophalo-web`, frontend only. `ophalo-web` has no test runner; verified via `tsc --noEmit`,
production build, and desktop + business-identity browser review of both intake routes and the
confirmation/tracker hand-off.

- **Confirmation screen.** `IntakeForm.tsx` gains a `"submitted"` stage. On a 201 the form no
  longer calls `router.push` to the tracker — it renders a stable confirmation card: business
  identity header, "Request sent", the reference code, an explicit "Track this request" link to
  `/keep/r/{pageToken}?welcome=1`, and the persistent footer. This satisfies ADR-446's rule that
  no render-time redirect may prevent the person from reading confirmation or retaining return
  access. The tracker's `?welcome=1` banner still shows when they continue.
- **Copy.** The three strings that called the tracker a "private page" / "private link" —
  overstating the link-token model — are replaced with neutral request-tracking-link language.
- **Footer.** A Terms link was added beside Privacy policy in `KeepPageFooter` (the shared public
  footer used by intake, tracker, and all handoff pages), so every public Keep entry point exposes
  both.

Files: `web/ophalo-web/src/app/keep/intake/[token]/IntakeForm.tsx`,
`web/ophalo-web/src/components/keep/KeepPublicShell.tsx`. Both `/keep/intake/[token]` and
`/keep/s/[slug]` render the same `IntakeForm`.

## Slice B — default-deny allowlist for the public event feed (`89a776d8`)

`OpHalo.Keep.Application` + tests. No migration, no domain change, no API contract change.

`KeepCustomerPageMapper.BuildActiveResult` filtered the request-event feed on the stored
`Visibility == All` flag alone — "all events minus a blocklist." It now applies
`IsCustomerVisibleEvent`, an explicit default-deny gate:

- `Visibility == All` is kept as the first AND-condition (defense in depth), then
- a type switch: `StatusChanged` → shown; `MessageAdded` → shown only when `ActorType` is
  `Customer` or `AccountUser` **and** `MessageIntent` is in the explicit
  `CustomerVisibleMessageIntents` set (all eleven current intents, listed by name); `_ => false`.

Any other current event type, and any unknown or future `KeepRequestEventType` value, is excluded
until it is deliberately added. `MapEventType` is narrowed to the two reachable types and throws
for anything else (unreachable past the filter). The customer-safe `MessageIntent` set is
full-explicit including `Complaint` and `ChangeOrCancelRequest` — a customer may always see their
own submitted messages.

Coverage:

- `tests/OpHalo.UnitTests/Keep/KeepCustomerPageMapperTests.cs` (new) — positive cases, internal
  events never visible, source/intent edge cases, and an exhaustive theory over every
  `KeepRequestEventType` asserting only `StatusChanged` / `MessageAdded` pass.
- `tests/OpHalo.IntegrationTests/Api/KeepCustomerPageTests.cs` — new test seeds an internal note
  (with a distinctive money phrase) + an attention-ack + a business update and asserts the feed
  contains only the business update.

## Verification

Full unit suite 1820/1820; architecture 14/14; `KeepCustomerPageTests` 18/18; event-adjacent
integration (CustomerMessage, SubmitFeedback, AddBusinessUpdate, AddInternalNote, ExternalContact,
ChangeStatus, RequestDetailB4) 140/140. `git diff --check` clean. `ophalo-web` `tsc --noEmit` and
production build clean.
