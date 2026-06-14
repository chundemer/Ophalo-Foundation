# Decision Index — OpHalo Foundation

The living ledger of architecture/product decisions for **this** build. One line per
decision. This file is authoritative for the new project.

> The legacy reference app has its own `decision-index` and `decisions/` — those
> contain many decisions that no longer apply and are **not** used here. Consult
> them only as historical reference (`_reference/docs/`), and only after validation.

## Conventions

- Add a one-line entry the moment a decision is made. Expand to a full ADR under
  `docs/decisions/ADR-NNN-*.md` only when the rationale needs more than a line.
- IDs are sequential and never reused. A reversed decision is marked **Superseded**
  (link the successor), not deleted.

**Status legend:** `Implemented` = decided and built/verified in this repo ·
`Locked` = decided (build plan §4), not yet built · `Proposed` = under discussion ·
`Superseded` = replaced.

---

| ID | Decision | Status | Source |
|----|----------|--------|--------|
| ADR-001 | Greenfield boundaries, brownfield behavior — clean structure, port proven behavior; legacy app is reference/fallback until parity | Locked | plan §1, §12 |
| ADR-002 | GitHub repo `ophalo-foundation`; internal solution/namespaces stay `OpHalo.*` (do **not** rename to `OpHalo.Foundation.*` — collides with the Foundation layer) | Implemented | this session |
| ADR-003 | One API host `OpHalo.Api` — collapse the legacy two-host (API + Continuity.API) setup | Locked | plan §4.4 |
| ADR-004 | One Foundation work model, no Hangfire; flexible hosting (in-proc pilot worker / separate `OpHalo.Worker` at scale) | Locked | plan §4.5 |
| ADR-005 | One PostgreSQL DB, one `OpHaloDbContext`, clean migrations (no prod data to preserve) | Locked | plan §4.6 |
| ADR-006 | Magic links for entry/recovery + trusted server-side opaque sessions; **no JWT** | Locked | plan §4.7 |
| ADR-007 | Authorization model: Account entitled · User permitted · Session trusted · Action allowed. Roles Owner/Admin/Operator/Viewer; statuses Invited/Active/Suspended/Removed; **permission keys** over scattered role checks | Locked | plan §4.8 |
| ADR-008 | Product-specific user settings attach to `AccountUser` (e.g. `KeepUserSettings`) — no separate product identity | Locked | plan §4.9 |
| ADR-009 | Entitlements via **feature keys**, not plan-name checks; no generic feature-flag/billing engine in v1 | Locked | plan §4.11 |
| ADR-010 | Admin shell lives in `OpHalo.Api` + `ophalo-app/app/admin`; internal-only, read-only first; writes audited; no impersonation in v1 | Locked | plan §4.18 |
| ADR-011 | Public token posture standardized in Foundation (high-entropy, hashed, expiry, rotation, revocation, public guard, rate limits, no client-trusted AccountId) | Locked | plan §4.19 |
| ADR-012 | Solution skeleton + **real** architecture tests (NetArchTest) before any behavior movement | Implemented | commit `00227b0` |
| ADR-013 | Exclude legacy families from the target: `Signal.*`, `Continuity.*`, `Platform.*`, separate `Auth` project, second API host | Implemented | build-log/001 |
| ADR-014 | Disciplined SharedKernel: `Result`/`Error`/`IClock` only. Canonical `ICurrentUser` + `IEmailSender` live in `Foundation.Application` (forbidden in SharedKernel); duplicate abstractions collapsed | Implemented | build-log/002 |

---

_Next free ID: **ADR-015**._
