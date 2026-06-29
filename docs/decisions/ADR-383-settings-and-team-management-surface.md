# ADR-383 — Settings And Team Management Surface

**Date:** 2026-06-28  
**Status:** Locked  
**Source:** Session 13 S13g settings/team-management review; ADR-377; ADR-378; build-log 067

## Context

Session 13 must let Owner/Admin users manage enough setup and team state in the authenticated
workbench that daily pilot operations do not require founder database intervention.

The backend already exposes Keep setup contracts, onboarding checklist/mark contracts, public intake
setup contracts, and member-management contracts. The frontend decision is therefore primarily about
scope, layout, naming, and which server-permitted controls are safe to expose for the first public
workbench.

## Decision

S13g uses one Owner/Admin-only `Settings` navigation item with these sections:

- `Company`;
- `Team`;
- `Onboarding`.

Do not create separate top-level Company and Team nav items.

The Company section includes:

- business name;
- account timezone;
- customer-facing phone;
- customer-facing email;
- response policy;
- public intake link status/ensure/replace.

Timezone is editable because it is part of `GET /keep/setup` and `PUT /keep/setup/profile`. The UI
must prefill the current value and use a deliberate timezone selector/input. S13g does not add any
request-detail timing behavior beyond saving the setup profile.

For 1-2 person businesses, Team setup must not feel mandatory. Company setup remains prominent; Team
is available inside Settings but secondary and low-pressure.

The Team section uses product language:

- section: `Team`;
- individual: `team member`;
- action: `Invite team member`.

Visible UI copy must not expose backend route vocabulary such as `members`.

S13g surfaces the server-supported team controls:

- invite team member with role `admin`, `operator`, or `viewer`;
- list active/invited/suspended team members;
- optionally include removed rows;
- resend invite;
- change role;
- suspend;
- reactivate;
- remove.

Invite does not allow Owner. Role management may expose Owner where appropriate for Owner callers, but
the backend remains authoritative for self-change, primary-owner protection, owner limits, last-owner
guards, Admin-vs-Owner constraints, and seat limits.

Do not add frontend-only rollout gates that hide controls the server permits. The server is the
control plane for account state, role, membership, and commercial limits.

S13g should add server-authoritative `seatUsage` metadata to `GET /accounts/me/members` before the
Team UI is implemented:

- `occupiedSeats`;
- `maxSeats`;
- `atLimit`;
- `limitApplies`.

The frontend must not infer seat availability by counting visible rows. When limits apply, the Team
section shows compact usage such as `Team seats: 1 of 2 used`. When `atLimit == true`, disable the
primary invite action with a clear reason such as `Team limit reached`. Seat-limit API errors remain
the authoritative fallback because state may change between load and submit.

New invites use email delivery. Success copy:

`Invite sent to {email}. They'll receive an email link to set up their account.`

Do not explain `ophalo-web`, auth handoff, or redirect mechanics inside the workbench. Public invite
accept UX belongs outside `ophalo-app`, backed by the API accept contract.

Resend invite defaults to email delivery. Because invite email may be blocked, delayed, or routed to
spam, manual-share resend is available as a deliberate fallback. It returns a raw invite URL, so the UI
must show it only immediately after the explicit manual-share action, never persist it in roster state,
and never log it.

Manual-share copy:

`Use this only if the invite email was not received. Anyone with this link can accept the invite.`

The Onboarding section reads `GET /keep/setup/onboarding` and exposes only the existing manual mark
actions:

- quick-capture exercise;
- tracker review;
- spam classification explanation.

The `operatorInvited` checklist item is derived from live account state. A sent invite alone does not
complete it; a non-owner active team member must exist.

The first public workbench must not block solo businesses because no non-owner active member exists.
For S13g, present team onboarding as optional/add-later for solo businesses. A later backend/onboarding
policy slice may formalize this as a skippable checklist item or account-size-aware rule.

## Rationale

Settings and team administration are both administrative account work. A single Settings entry keeps
the workbench navigation focused on operations while still giving Owner/Admin users the controls they
need.

Using the backend contracts directly avoids creating a separate frontend policy system. Member
management already has server-side protections for risky paths such as primary-owner protection,
owner promotion/demotion, self-modification, removed-member recovery, and seat limits.

The timezone correction is important: timezone is not backend-only in the current setup contract.
However, S13g should keep it as deliberate setup data, not allow casual timing behavior to leak into
request workbench flows.

Keeping invite-accept details out of the admin workbench avoids exposing implementation routing to
business users. They need confirmation that an invite was sent, not an auth topology explanation.

Email invite deliverability is not guaranteed. Keeping manual-share as a deliberate fallback gives the
business a recovery path without making raw invite URLs the default happy path.

## Consequences

- S13g frontend work should add a Settings route/surface with Company, Team, and Onboarding sections.
- Owner/Admin users get the full settings/team surface the server permits.
- Operators/Viewers do not get editable Settings navigation.
- Timezone is included in the Company profile form and must be preserved/submitted with profile
  updates.
- Team invite limits require server-provided seat usage metadata; the UI disables invites at the
  server-reported limit and still handles seat-limit conflicts after submit.
- Team setup remains available but low-pressure for solo/tiny businesses.
- Error handling should map member/invite error codes to inline row/form messages.
- The UI should not manually complete onboarding items that the checklist derives from live state.

## Deferred

- Primary-owner transfer.
- Billing/plan management.
- Internal admin/support tooling.
- Invite accept implementation in `ophalo-app`.
- Broad account lifecycle controls.
