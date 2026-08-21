# Keep UI Design V2

**Status:** Active working documentation set  
**Purpose:** One place for the user-centered decisions and implementation contracts governing the
production UI upgrade. V2 does not silently erase or rewrite prior records.

## Document authority

| Document | Owns |
|---|---|
| [Decision Register](keep-ui-production-decision-register.md) | Decisions awaiting lock, workflow scenarios, scope protection, and document reconciliation |
| [Design Model V2](keep-ui-design-model-v2.md) | Active cross-surface design doctrine after a rule is locked |
| [Component Spec V2](keep-component-spec-v2.md) | Exact reusable component recipes under locked V2 decisions |
| [Review Rubric V2](keep-review-rubric-v2.md) | Production-review and release criteria for V2 surfaces |

## Migration posture

The V2 set is being built from, not blindly replacing, these existing sources:

- `../ux-design-model-v1.md` — current token, typography, brand, and surface rules;
- `../keep-component-spec.md` — current primitive recipes;
- `../keep-review-rubric.md` — current production review gate;
- `../pwa-ui-quality-system.md` — current correction program and its decision status;
- `../ux-design-decisions.md` — historical locked UX decisions;
- `../../build-log/081-session-24-request-detail-2-column-workbench.md` — prior Request workbench
  direction;
- `../../decisions/ADR-380-request-detail-workbench-contract.md` and
  `../../decisions/ADR-435-request-list-action-cockpit-boundary.md` — request-data, action, and
  route boundaries.

Existing sources remain in place until a V2 rule explicitly records whether it is retained,
superseded, or deferred. Do not implement from a V2 draft rule marked **Decision required**.

## Current handoff point

UI-001 through UI-013 are locked: desktop Queue + Workbench, durable request routing, unselected
Priority Preview, role queues, selected-request hierarchy, action semantics, adaptive field posture,
form containment, state/recovery, public intake, customer-page trust, the production quality gate,
and migration. The next artifact is the implementation build guide; it must translate these rules
without reopening them.
