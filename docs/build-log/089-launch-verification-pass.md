# Build Log 089 — Launch Verification Pass

**Status:** Deferred until active launch gaps are resolved or explicitly deferred  
**Date:** 2026-07-17  
**Prerequisites:** Active items in `docs/pilot-readiness-bug-tracker.md` are reviewed and their
selected corrective slices are committed; no open critical desktop workflow regression remains.

## Purpose

This is a manual acceptance pass, not an implementation slice. It occurs only after the current
bug/gap work is complete enough to provide a stable target. Findings are recorded as pass, fail, or
new tracked issue; a failure does not expand this pass into an unbounded fix session.

The pass is deliberately split so the desktop operational workspace is stable before real-device
mobile testing begins.

## Pre-Work Discussion And Setup

Before either gate begins, agree on the exact accounts, roles, requests, browsers, device, and
network topology to use. Confirm the local three-service stack or a preview environment is reachable
from the test phone when a QR/deep-link check is required.

- Desktop workbench: `ophalo-app`.
- Public/customer pages and handoff pages: `ophalo-web`.
- API/auth/persistence: `OpHalo.Api`.
- For a local phone test, do not use `localhost` in public/app/API URLs: it refers to the phone.
  Configure reachable LAN or tunnel origins consistently, including API CORS.

Record the build/commit under test, browser/device versions, test account role, and any test data
created. Reset or clearly label test data after the pass.

Before the workflow gates, perform the ADR-446 first-visit trust review for every externally
reachable page under test. On desktop and a real phone, verify that page ownership/product identity,
value and next outcome, data-use explanation before sensitive entry, recovery/help route, safe
browser-title identity, controllable success/return access, visible keyboard focus/announced errors,
and visual composition are clear to a skeptical first-time visitor. Include start, sign-in,
check-email, magic-link success/error, invite success/error, public intake, customer tracker, and
known-business terminal states where applicable. Treat an anonymous-looking or unfinished-looking
entry surface as a launch finding, not cosmetic polish.

## Gate 1 — Desktop Operational Verification

Run after request-list changes and the selected desktop-facing gaps are committed.

- Request list: search clear behavior; lifecycle/status hierarchy; queue/count agreement; long
  names, locations, and descriptions; 200% zoom; keyboard navigation; loading/error/retry states.
- Request detail: contact/share actions, timing/location clarity, closeout/feedback paths, and
  no customer/internal visibility leakage.
- New Request: Owner/Admin customer-text handoff; one staff QR only; confirmed caller number;
  manual-entry fallback; Operator staff-entry path; captured-request draft/error retention.
- Public intake: durable `/keep/s/{slug}` form, business-first identity with secondary OpHalo Keep
  endorsement, pre-entry data-use clarity, validation, submission, private-page return/recovery, and
  return to the business workbench path. Verify known-business post-submit/expired/unavailable/
  OffSeason states retain safe identity and recovery while unknown tokens remain non-enumerating.
- Account start and auth entry: visible OpHalo Keep identity, accurate pilot/magic-link outcome copy,
  desktop/laptop composition, time-zone confirmation/correction, sign-in recovery, pilot-full and
  duplicate-email states, Privacy/Terms/support routes where appropriate, associated/announced
  errors, and visible keyboard focus. Verify invite and magic-link terminal states use the same
  intentional browser-auth pattern; preserve the sterile mobile authorization handoff constraints.
- Desktop QR: scan the opaque staff QR with a phone and confirm it opens a pre-addressed draft to
  the intended test number; do not send a real customer message during verification.

Any desktop failure is triaged and fixed in its own bounded slice before Gate 2 starts.

## Gate 2 — Mobile PWA And Real-Device Verification

Run only after Gate 1 passes. Use a real phone for browser, camera, SMS, and browser-to-native-app
handoff behavior; desktop viewport emulation is useful but is not a substitute.

- Authenticate and navigate the mobile PWA without layout clipping or broken back behavior.
- Owner/Admin: enter/confirm a mobile number, prepare the text, and use the explicit **Open Text
  Message** action. Confirm iOS `&body` or other-platform `?body` URI behavior and recipient
  prefill.
- Operator: confirm direct staff-entry behavior and absence of Owner/Admin-only handoff controls.
- Quick Capture: validation, Change-phone behavior, service-address disclosure, draft preservation,
  errors, and manual-entry fallback.
- Request list/detail: touch targets, overflow/wrapping, keyboard/screen-reader checks where
  available, long data, loading/error states, and external `tel:`/`sms:`/email intents.
- Public pages: open the durable public intake link from the phone and complete a test request;
  verify business-first identity, OpHalo Keep endorsement, privacy clarity before address/contact
  entry, and private-page return/recovery. Review account-start/auth entry in its stacked mobile
  presentation as well, including known-business terminal states and safe browser/document titles.

Record platform-specific failures separately from general PWA defects.

## Completion

The verification pass is complete only when both gates have a dated result record, all blocking
findings are resolved or explicitly deferred by Christian, and the tracker/session log identify the
tested commit. Production deployment verification remains a separate HTTPS/domain/cookie/DNS gate.
