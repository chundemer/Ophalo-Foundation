# Session Log — OpHalo Foundation

**Next-session pointer only.** Read this first, then the canonical [workboard](workboard.md) for
scope, sequencing, gates, and deferrals. Locked decisions are in
[decision-index](decisions/decision-index.md); completed-work evidence is in `docs/build-log/`.

**Updated 2026-09-15.** The foundation-first sequence is active. GAP-039 Batch 4, GAP-038,
GAP-093, GAP-095, GAP-098, GAP-073, and GAP-094 are complete. The canonical scope, order, gates,
and deferrals are in the [workboard](workboard.md).

## Start here — commit BL158, then resume GAP-099 main scope

GAP-073 and GAP-094 are both reviewed and merged (2026-09-15, no findings on either). GAP-099 is
active; two slices delivered but **uncommitted**:

- [BL157](build-log/157-gap-099-invited-member-cancel-invite.md) — cancel a pending invite
  (Invited row gains Remove). Verified live and working.
- [BL158](build-log/158-gap-099-removed-member-action-contract-fix.md) — `hasAcceptedBefore`
  contract fix so a Removed row shows the correct action (Resend invite vs. Reactivate). Code
  complete, tests passing (backend 35/35, frontend 43/43). Live-verification blocker **resolved
  2026-09-15**: the earlier "This invite link is no longer valid" was traced to a local-dev
  terminal copy/paste of the console-only invite link (Manual Share reproduced a clean success),
  not a defect in `ResendInviteAsync`/`RestoreInvite()`. No code change required. **Still
  uncommitted — pending Christian's explicit commit approval.**

Found while verifying BL158 (not a BL158 defect, separate minor gap — not yet scoped or
prioritized): `CommitAcceptInviteAsync` (`EfInvitePersistence.cs:109-110`) returns the generic
`Invite.InvalidToken` for a re-clicked, already-accepted invite link, the same as for a truly
invalid one. `Invite.AlreadyActive` already exists and gives a more accurate message
("You are already a member. Sign in...") but today only fires from `SendInviteService`, never
from accept. Functional impact is low — the member can still recover via normal sign-in, which
re-prompts for name through the same continuation mechanism — this is copy polish only. Ask
Christian whether to log this as a deferred workboard item before scoping it.

Open decision (not yet resolved, raised 2026-09-15): should a mistakenly-entered invite email have
an explicit delete/purge action, or is "Removed rows persist forever, visible only behind 'Show
removed members'" acceptable? Current architecture never hard-deletes `AccountUser` rows (Suspended/
Removed are terminal but reversible states, consistent with the app's audit-trail posture). A typo'd
email doesn't block inviting the *correct* address (separate row, no conflict) — the open question
is purely whether Removed-row clutter from typos needs its own cleanup action. No decision made yet;
raise with Christian before scoping any further work here.

Once BL157/BL158 are committed, continue GAP-099's main scope: verify the deployed Team list with
Owner and Admin accounts and reproduce the original Suspend/Reactivate discoverability failure
(context not yet gathered — ask Christian for specifics before attempting repro). Then take up the
next Next item, GAP-063.

## Next several sessions

1. Continue the workboard's foundation-first order: GAP-099, GAP-063, GAP-048, GAP-049, then
   GAP-092.
2. Run the **Proposed Work & Commercial Quotes** decision session. Its first deliverable is a
   decision record, not code; use its workboard Decision Queue entry and the cited ADRs/build logs.
3. Begin **GAP-069** only at the release-readiness trigger: about two weeks before Keep becomes the
   authoritative pilot record, after Railway Pro daily backups/PITR are enabled and the first PITR
   recovery window exists. See [authoritative-pilot-release-readiness.md](runbook/authoritative-pilot-release-readiness.md).

## Pilot posture

The controlled parallel field pilot keeps the existing system authoritative for estimates,
invoices, payments, and accounting; Keep is the factual field record. See
[BL131](build-log/131-next-week-parallel-field-pilot-plan.md).

## Current hot blockers

- No current platform blocker. BL158's live-verification blocker (see Start Here) is resolved as a
  local-dev terminal copy/paste artifact, not a code defect; it remains uncommitted only pending
  Christian's approval. The release-readiness trigger for GAP-069 is documented in the
  workboard and the authoritative-pilot release runbook.
- Native GAP-091 review, S18, and S19 are deferred; they must not displace the foundation-first
  closed-loop sequence.
