# Session Log — OpHalo Foundation

**Last updated:** 2026-07-12 (S24m complete — ADR-437 persistence + tests done, pending commit approval)
**Branch:** `main` tracking `origin/main`
**Last green baseline:** S24m — KeepRequestListServiceTests 167 passed; KeepRequestListB5Tests 25 passed (was 22)
**Next free ADR:** ADR-438
**Current session:** Session 24 — request workbench; S24m ADR-437 default-queue filtering complete, awaiting commit approval; S24n customer-page copy captured next

---

## Session Protocol

This file is the current execution brief, not the historical build archive. Completed implementation
detail lives in `docs/build-log/`; authoritative decisions live in
`docs/decisions/decision-index.md`.

For every implementation slice:

- Classify the work explicitly: discovery when pre-work is incomplete; mechanical implementation
  preflight when the current brief is marked pre-work complete.
- Use targeted `rg` during preflight to confirm named signatures and compile-impact callers. Do not
  rediscover already-locked architecture, scope, tests, or decisions.
- Inspect current signatures, endpoint/persistence patterns, failure modes, and tests before editing.
- Present the file-level gate before writing.
- Keep the hard slice gate unless explicitly split: at most 3 mutation families, 8 production files,
  and 12 total changed files including tests/docs.
- Preserve fail-closed account, row, action, membership, and public-token behavior.
- Add focused authorization/regression tests and run the proportionate broader suite.
- Self-review for policy drift, accidental visibility expansion, token leakage, untested direct-ID
  paths, stale docs, and unrelated scope.
- Commit only after Christian approves the completed diff.

---

## Current Work

**Current build log:** `docs/build-log/081-session-24-request-detail-2-column-workbench.md`
**Latest completed build log:** `docs/build-log/080-session-23-work-completed-closeout-ux.md`
**Last completed prior-session build log:** `docs/build-log/075-session-21-attention-guidance-resolution-metadata.md`
**Readiness working doc:** `docs/pilot-readiness-decision-questions.md`
**Bug/gap tracker:** `docs/pilot-readiness-bug-tracker.md`
**Foundation roadmap:** `docs/build-log/ophalo-foundation-build-plan-greenfield-boundaries-brownfield-behavior.md` section 9.1
**Current session:** Session 24 — request workbench 2-column layout and list quick actions
**Current slice:** S24m — ADR-437 default-queue filtering complete, pending commit approval

### S24m In-Progress Handoff

**What is done (uncommitted, dirty working tree):**
- `src/OpHalo.Keep.Infrastructure/Persistence/KeepRequestListPersistence.cs` — two edits:
  1. `ActiveViewKind.Default` query: added `&& (r.Status != Resolved || r.AttentionLevel != None)` to exclude calm Resolved rows.
  2. `defaultCount` in `GetViewCountsAsync`: same predicate added to keep count in sync.

**S24m is complete and ready to commit.** All tests pass:
- `KeepRequestListServiceTests`: 167 passed
- `KeepRequestListB5Tests`: 25 passed (3 new ADR-437 tests + 1 replaced + 21 unchanged)

**Test changes:**
- Replaced `Resolved_request_included_and_ranked_as_resolved_quiet` with `Calm_resolved_excluded_from_default_list`
- Added `Calm_resolved_present_in_ready_to_close`
- Added `Resolved_with_active_attention_present_in_default_list` (new seed: `_resolvedWithAttentionRequestId`)
- Added `Resolved_with_active_attention_absent_from_ready_to_close`

### S24n Captured Next Issue — Business-Created Customer Page Copy

Customer tracker pages currently use status-only copy, so a business-created request in `received`
state can show:

```text
Your request has been received
```

That wording is appropriate for customer-submitted public intake, but not for team-created requests.
Next slice should make customer-page copy origin-aware. Preferred direction:

- expose a safe public customer-page discriminator such as `origin: "customer" | "business"` if not
  already available on the customer page DTO;
- customer-origin + `received`: keep `Your request has been received`;
- business-origin + `received`: use `A request page has been created for you` or equivalent;
- update subtext so it explains that the business created the private page to keep request details
  and updates in one place;
- add focused public customer-page API/frontend coverage for both origins.

### Completed Context

Completed implementation details live in the build logs and should not be repeated here:

- Session 13 — PWA workbench: `docs/build-log/067-session-13-pwa-workbench.md`
- Session 14 — web front door: `docs/build-log/068-session-14-ophalo-web-front-door.md`
- Session 15 — pilot readiness: `docs/build-log/069-session-15-pilot-readiness.md`
- Session 16 — native mobile foundation: `docs/build-log/070-session-16-native-mobile-foundation.md`
- Session 19 — Keep workbench UX migration: `docs/build-log/073-session-19-keep-workbench-ux-migration.md` (S19a–S19d complete; S19e + Gate 3 sign-off deferred)
- Session 20 — mobile Owner/Admin control mode: `docs/build-log/074-session-20-mobile-owner-admin-control-mode.md`
- Session 21 — attention guidance resolution metadata: `docs/build-log/075-session-21-attention-guidance-resolution-metadata.md`

Session 16 completed the native mobile foundation, including the Expo app shell, secure token and
installation storage, mobile magic-link handoff, nullable push-token device registration, badge hook,
viewer/unknown mobile role gate, crypto UUID generation, and the S19 store-submission checklist.
Treat these as historical context unless a later discovery step finds a concrete gap.

### Post-S22 State

Session 22 is complete. Historical S22 detail is archived in
`docs/build-log/076-session-22-guided-setup-intake-and-service-location.md`; use this session log as
the current handoff and discussion brief only.

Locked decisions to preserve:

- ADR-428: Keep launches in a day-zero functional state. Settings is split into `Public Link &
  Profile`, `Response Policy`, and `Team`; Getting Started is a lightweight verification/on-ramp.
- ADR-429: ordinary public link-name edits preserve old shared slugs as aliases. Replacement or
  regeneration is the destructive/security action and must warn that old shared links break.
- Public intake URLs use durable slug routing from the configured public web base URL. Do not build
  customer-facing links from `window.location.origin` inside `ophalo-app`.
- Public intake is auto-provisioned by default; owners verify/copy/preview it from Settings.
- Team setup must remain optional and reassuring for solo businesses.
- Public intake form now collects service location, intake urgency, and preferred contact method.
- ADR-430: intake urgency and preferred contact method are persisted customer-reported triage
  metadata. They appear on operator detail and on the operator request list because road operators
  use the list as their primary view. Customer-selected urgency is not a verified system attention
  condition; preferred contact is not a full notification preference/opt-out system.
- ADR-431: public Keep pages use business-first identity hierarchy and neutral public motto copy:
  `The trust and continuity layer between businesses and customers.`
- ADR-432: platform email/Resend is in scope. Public intake may send a narrow, fail-soft tracker-link
  email when the customer supplies email; backend customer SMS and broad automated customer
  notification workflows remain deferred.
- Staff-auth public-intake blocking remains post-submit only for now; pre-submit staff blocking is
  deferred because the public Next.js page has no load-time session context without a new API call.

Implementation status:

- S22 day-zero Settings redesign, slug routing, slug aliases, Settings tab split, backend
  auto-provisioning, service location, business identity header, intake UI polish, intake urgency,
  preferred contact persistence, and operator list/detail display are complete.
- `Settings.tsx` was split into tab files:
  `settings/CompanySection.tsx`, `settings/PolicySection.tsx`,
  `settings/PublicLinkSection.tsx`, and `settings/TeamSection.tsx`.
- Pre-deployment file-decomposition cleanup is parked in
  `docs/build-log/077-pre-deployment-cleanup-and-file-decomposition.md`; do not start that cleanup
  until the customer request page and testing are complete.

Always preserve:

- fail-closed account, membership, action-policy, public-token, and concurrency behavior;
- raw-token non-disclosure in logs, diagnostics, persisted frontend state, or long-lived UI state;
- `ophalo-app` as the authenticated Keep workbench;
- `OpHalo.Api` as the authority for auth, sessions, account creation, rate limiting, email,
  authorization, and persistence.

Topology:

- Production: `ophalo.com`/`www.ophalo.com` -> `ophalo-web`, `app.ophalo.com` -> `ophalo-app`,
  `api.ophalo.com` -> `OpHalo.Api`.
- Local: `ophalo-web` `http://localhost:3000`, `ophalo-app` `http://localhost:5173`,
  `OpHalo.Api` `http://localhost:5092`.
- Pilot cap: `SignupDefaults:MaxPilotAccounts=15`.
- `OperatorBaseUrl` is retired; invite links use `{PublicBaseUrl}/invite/accept`.

### Active Build Log 081 — Request Workbench 2-Column Layout And List Quick Actions

Build-log 081 is the current decision/handoff document:
`docs/build-log/081-session-24-request-detail-2-column-workbench.md`.

### Completed S24 Slices

- **S24a — Workbench top nav** (`7f9d7e1`): replaced persistent left sidebar with horizontal top
  nav on `requests`/`detail` routes. Top nav: Keep logo, Requests (with count badge), Getting
  Started, Settings (owner/admin), role label, New Request. Sidebar preserved for home/settings.
  Mobile unchanged.

- **S24b — 70/30 desktop grid** (`7f9d7e1`): `RequestDetail` content wrapper switches from `flex`
  to CSS grid (`minmax(0,7fr) minmax(320px,3fr)`) on `md:`. Fixed-width rail removed; grid manages
  sizing. Mobile unaffected.

- **S24c — Context split** (`7f9d7e1`): `OriginalRequestCard` slimmed to description-only.
  Extracted `CustomerPanel` (phone/email + copy/call/email links + log contact), `ServiceLocationPanel`
  (address + add/edit modal), `TriagePanel` (ADR-433 customer signal + internal priority),
  `SourceMetaPanel`. Desktop: panels in side rail. Mobile: panels stacked after activity per
  Decision 14 order.

- **S24d — Composer in main work loop**: `BusinessUpdateSection` moved to main column directly
  above Activity (all screens). Removed from mobile primary actions and desktop rail. Activity
  filter renamed **Conversation & notes** to clarify internal notes won't appear customer-visible.

- **S24e — Rebalanced actions + internal notes**: `LogContactCard` attention gate removed — now
  shows whenever `canLogExternalContact`. New `InternalNoteCard` component (`canAddInternalNote`
  gate, "Not visible to customer" label, 409 conflict preservation). `addInternalNote` API helper
  added. New **Internal** section in side panel. Mock stubs added. Admin classification explicitly
  deferred.

- **S24f — Timing controls**: `setFollowUpOn`, `clearFollowUpOn`, `setPlannedFor`,
  `clearPlannedFor` API helpers added (all versioned). Mock stubs added. New `TimingPanel`
  component: shows set/clear forms for follow-up (date + reason + optional note) and planned date;
  gates on `canSetFollowUpOn`/`canSetPlannedFor`; 409 draft preservation; internal-only label.
  Wired into desktop side panel and mobile stack.

- **S24g — Request list quick actions** (temporary fallback only): Quick actions currently work as
  navigation-intent deep links because the end-to-end list/action contract is incomplete for
  executable row actions. Backend `KeepRequestSummary` already includes `Version`, but the PWA list
  type/mocks still need to consume `version`, and `KeepQuickAction` needs execution metadata such as
  `requiresVersion` and `executionMode`. This fallback is safe but does **not** satisfy the
  request-list action-cockpit goal. ADR-435 locks the product boundary: the request list must handle
  low-risk, high-frequency actions inline once the row contract can do so safely; request detail
  remains the depth/accountability surface. GAP-007 tracks the required contract completion.

- **S24h — Polish** (complete): `keep-row-title` now uses `font-serif` for customer names in the
  list. Quick-action pills no longer show effect sub-labels. `QuickActionEffectLabel` component
  removed.

### Next

1. **S24g2 — Request-list action contract**: complete safe row-mutation metadata for request
   summaries. Confirm backend/API serialization of `KeepRequestSummary.version`, add it to PWA
   TypeScript types/mocks if missing, and add quick-action metadata: `requiresVersion`,
   `executionMode`, `customerVisible`, `internalOnly`, `clearsAttention`, and `changesStatus`.
   Update mapping, API tests, TypeScript types, mocks, and docs.
2. **S24g3 — True inline list actions**: replace eligible deep-link shortcuts with one reusable
   row action modal/sheet shell for the currently emitted modal actions: send customer update, log
   external contact, and simple attention acknowledgement. Use `executionMode`, `requiresVersion`,
   and `row.version`; preserve drafts on 409; refetch the list after success. Keep feedback review,
   cancellation, classification, service-location edits, timing controls, and generic status changes
   detail-owned.
3. **S24g4 — PWA list internal note quick action**: add server-emitted `add_internal_note` as a
   pilot-priority PWA owner/admin cockpit action using the same modal/sheet shell with an
   internal-only variant. Do not let this slip behind native mobile work.
4. **GAP-008 — Request-list urgency/priority context:** resolved in S24h.
5. **GAP-009 / ADR-436 — Staff signal clarity audit:** resolved in S24i.
6. **GAP-010 — Ready to Close row action leak:** resolved in S24j after screenshot review; closeable
   work-completed rows now emit `close_request`, show `Next: Close request`, and display a neutral
   `Review closeout` detail shortcut instead of communication next-actions.
7. **GAP-011 / S24k — Shared external-contact form + post-work row action correction:** complete
   (`0d03822`). `ExternalContactForm` shared component enforces full ADR-216 payload rules.
   `BuildQuickActions` early-return removed; calm resolved rows emit `contact_customer`,
   `post_customer_update`, `add_internal_note`, then `close_request` rightmost. `nextActionCue`
   checks for `close_request` by presence. Stale mock fixture corrected.
8. **S24 closeout — Responsive QA and polish:** next — resume and close build-log 081.
9. After S24 complete: **077** pre-deployment file decomposition or **078** tracker-link email.

### Surface Boundary

- PWA request list = owner/admin triage cockpit for queue scanning, customer updates, internal team
  memory, attention handling, and desk/tablet operational control.
- Native mobile request list = field execution surface for call/text/email launch, contact logging,
  map/location actions, self-assignment, and phone-sized promise-loop work.
- Both surfaces share server-owned action metadata, but their quick-action layouts and priority order
  may differ by surface.

### Active Gap

- **GAP-007 — Request-list quick actions lack complete row-level action contract**:
  `docs/pilot-readiness-bug-tracker.md`.
- **GAP-008 — Request-list urgency/priority pills lack source and next-action context**:
  `docs/pilot-readiness-bug-tracker.md`.
- **GAP-010 — Ready to Close rows leaked communication next-actions**:
  `docs/pilot-readiness-bug-tracker.md`.
- **GAP-011 — External contact logging duplicated and closeout rows over-prune communication actions**:
  `docs/pilot-readiness-bug-tracker.md`.
- **ADR-435 — Request List Action Cockpit Boundary**:
  `docs/decisions/ADR-435-request-list-action-cockpit-boundary.md`.
- **ADR-436 — Staff Operational Signal Clarity**:
  `docs/decisions/ADR-436-staff-operational-signal-clarity.md`.

Resolved before next session:

- Request detail external-contact action: use the generic label `Log external contact`, remove the
  duplicate inline `Log phone contact` pill beside the customer phone number, keep the right-rail
  `Handled outside Keep?` card as the canonical external-contact logging action, and retitle the
  modal `Log external contact`.
- Request detail command cleanup: removed the separate lower-right `Change status` card; kept one
  status selector in the `Send customer update` composer; made composer behavior explicit for
  message-only, status-only, and message+status cases; moved customer-page actions into quiet hero
  links; kept the customer-name heading in `font-serif`; and replaced `Already handled?` with
  quieter `Clear attention` semantics.
- Request queue navigation: add detail-page navigation so users can move to the next request without
  scrolling back to the list. Preferred direction is `Previous` / `Next` or `Next needing attention`
  near the hero/top bar, plus a post-action route to the next request if the current workflow supports
  it. **Complete** — Prev/Next buttons in breadcrumb bar; client-side list context passed from
  `Requests` through `App` to `RequestDetail`; disabled at list boundaries; no nav shown on direct
  open (QuickCapture, deep link). Committed and pushed (`14347ce`).

Completed S22 Claude slices:

- **S22p9 — Urgency-aware default queue ordering:** **Complete.** Added `customer_urgent_active`
  ranking bucket (order 3) covering any urgent active intake not yet terminal, pending-customer, or
  resolved. New order: overdue (1) → priority-biz-waiting (2) → customer-urgent-active (3) →
  post-close (4) → standard-biz-waiting (5) → first-response-pending (6) → waiting-on-customer (7)
  → resolved-quiet (8) → active (9). `isPostClose` guard preserved before priority and urgent checks.
  UI clarification pass: `KeepRequestRankingInfo` type added to `apiClient.ts`; `Overdue` badge now
  shown first in badge row when `ranking.isOverdue`; danger left-border for overdue rows; `Due [date]`
  compact text from `ranking.dueAtUtc`; mock fixtures updated. TypeScript clean.
- **S22p10 — Business-editable request priority:** **Complete.** Backend: `BusinessPriority` enum
  (Routine/Soon/Urgent), `SetBusinessPriority` domain method, `BusinessPriorityChanged = 17` event
  type, `SetBusinessPriorityService`, `PUT /keep/requests/{requestId}/priority` endpoint, EF migration
  (`business_priority` nullable varchar(50)), effective-priority ranking override in list service.
  ADR-433 locked. Frontend: `KeepRequestSummary`/`KeepRequestDetailResult` updated, dual-signal badge
  in `RequestRow.tsx`, Priority selector in `RequestDetail.tsx` (`canAddInternalNote` gate), mock
  fixtures (10 locations) and mockApiClient inline details + stub updated. TypeScript clean.
  Committed at `743a664` (backend + tests + ADR) and frontend pending commit.
- Full Claude-ready details and locked decisions are in
  `docs/build-log/076-session-22-guided-setup-intake-and-service-location.md`.

Current recommended slice order:

1. S23a — PWA status language and attention-safe Mark Work Done.
2. S23b — PWA Owner/Admin Close Request card.
3. S23c — Ready-to-close row/detail polish.
4. Decide whether S23d mobile carry-forward is implementation or deferred audit only.
5. Resume build-log 078 or build-log 077 after 080 is closed or explicitly paused.

### Completed S22 Summary

Session 22 is complete. Shipped work included day-zero Settings redesign, durable slug-based public
intake, slug aliases, backend intake-link auto-provisioning, business-first public identity, intake
urgency, preferred contact method, service location collection/exposure, PWA operator display, mobile
request-detail carry-forward, mobile Open in Maps, and documentation reconciliation.

Detailed S22 slice notes live in
`docs/build-log/076-session-22-guided-setup-intake-and-service-location.md`.

Remaining pre-deployment work lives in separate build logs:

1. **Customer tracker-link email / Resend:** `docs/build-log/078-customer-tracker-link-email-and-resend-configuration.md` — locks tracker-link retention decisions, Resend configuration checks, public-intake tracker-link email, confirmation-flow copy, and operator correspondence prefill.
2. **Pre-deployment cleanup:** `docs/build-log/077-pre-deployment-cleanup-and-file-decomposition.md` — deferred until customer request page work and testing are complete.

Historical mobile context lives in `docs/build-log/071-session-17-review-safe-native-product-foundation.md`.

## Carry-Forward Boundaries

- Real APNs/FCM provider implementation remains future work.
- Demo scenario packs, demo reset UI, and admin/internal classification management remain deferred.
- Classification is operational/reporting/safety posture, separate from commercial lifecycle.
- Public signup cannot create Demo/InternalTest accounts.
- Production push delivery must stay suppressed for Demo/InternalTest accounts.
- Keep does not send backend customer SMS or ingest SMS replies in V1. Platform email/Resend is in
  scope for auth/member flows, and ADR-432 allows one narrow fail-soft public-intake tracker-link
  email when the customer supplies email. Broad automated customer email/SMS notification workflows,
  notification preferences, quiet hours, proof-of-send, and delivery ledgers remain deferred.

---

## Deferred Decisions

- **Mobile package manager standardisation (S19 candidate):** `mobile/ophalo-mobile` was scaffolded
  with Expo's default npm and has used `package-lock.json` since S16b. `web/ophalo-app` uses
  `pnpm@11.9.0`. The monorepo root has no shared workspace lock file. Standardising mobile to pnpm
  would require deleting `package-lock.json`, adding `"packageManager": "pnpm@x.x.x"` to
  `mobile/ophalo-mobile/package.json`, regenerating `pnpm-lock.yaml`, and switching `expo install`
  calls to `pnpm exec expo install`. Low urgency — npm is consistent within the mobile project —
  but worth aligning before S19 EAS Build configuration introduces CI/CD package-manager assumptions.

---

## Operational Watch-Outs

- GitHub remote `origin` is configured; push local commits daily when green.
- Integration tests reset PostgreSQL schema and run migrations.
- Testing environment intentionally skips runtime rate limiting; production-like proof exists in
  `RateLimitTesting` (G8a/S7b).
- Deployment still requires correct Cloudflare/Vercel/Railway topology, trusted-proxy posture, and
  token-redaction configuration even though application-level proofs are complete.
- Persistent local PostgreSQL setup/migration/smoke runbook is verified against local `ophalo_local`
  in Docker; guarded reset remains documented but was not exercised.
