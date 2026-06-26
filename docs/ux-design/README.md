# UX Design Reference

**Status:** Active reference
**Date:** 2026-06-07
**Domain tag:** `ux-design`

---

## Purpose

This folder is the active UX, brand, and styling-system reference for OpHalo and the
Keep application. Use it before changing brand tokens, Keep styling, shared UI
primitives, or the public/operator Keep surfaces.

---

## Read This First — Authority Map

These docs form a layered system. Each layer answers a different question, and **each
fact has exactly one home.** A token value, a type size, a shadow rule lives in one
doc; every other doc links to it instead of restating it. Restating a value in two
places is how sessions drift — do not do it.

| Layer | Doc | Owns (the single source for…) |
|---|---|---|
| **Principles** | `ux-design-model-v1.md` | Brand, voice, tokens, color rules, the **Production Richness Floor**, surface contracts |
| **Specification** | `keep-component-spec.md` | Closed component recipes to the class, foundation scales (type/elevation/radius/spacing), composition structure, state/feedback patterns |
| **Review gate** | `keep-review-rubric.md` | Whether a screen is production-ready: visual judgment, readiness checklist, and the public-availability release gate |
| **Rationale** | `ux-design-decisions.md` | The locked decisions and *why* behind the system (ADR log) |

### Precedence — when two docs disagree

Resolve conflicts in this order. A higher layer wins; report the contradiction so the
loser gets fixed rather than carried.

1. **`keep-component-spec.md`** — operative truth. Code follows the spec.
2. **`ux-design-model-v1.md`** — tokens, voice, and the Richness Floor govern the spec.
3. **`keep-review-rubric.md`** — the gate a finished surface must pass.
4. **`ux-design-decisions.md`** — rationale; update it when a decision is superseded.

If you find two docs stating the same value differently, the **spec or model wins**
and the other is the bug — fix it in the same change, do not average them.

### Session entry order

- **Polishing or building a Keep surface:** read `keep-component-spec.md` first, then
  check the screen against `keep-review-rubric.md`. Touch `model-v1` only for tokens,
  voice, or the Richness Floor.
- **Changing tokens, brand, voice, or a surface contract:** start in `model-v1`, then
  propagate to the spec.
- **Recording or revisiting a decision:** `ux-design-decisions.md`.

---

## Files

| File | Use when |
|---|---|
| `ux-design-model-v1.md` | You need brand, voice, tokens, color rules, the Production Richness Floor, or a surface contract |
| `keep-component-spec.md` | You need the closed component recipes, foundation scales, composition structure, or state patterns before building or polishing a Keep surface |
| `keep-review-rubric.md` | You need to judge whether a surface is too plain / over-designed and whether it clears the production + release gate |
| `ux-design-decisions.md` | You need the locked decisions and rationale behind the system |

Retired (historical only — do not treat as current doctrine):

- `ux-design-harden-pass.md` — superseded styling-drift work log; live rules folded into the spec and model
- `docs/v1/ophalo-brand-v1.2.md`, `docs/v1/product/ophalo-brand-v1.2.md`, `docs/v1/product/OpHalo Brand Guide.md`

---

## Related References

- `docs/reference/product/what-is-keep.md` — product positioning for Keep
- `docs/reference/new-request-customer/new-request-model-v1.md` — operator-created request surface
- `docs/reference/new-request-customer/customer-intake-decisions.md` — public intake decisions
- `docs/reference/customer-request-page_6_2/customer-request-page-model-v1.md` — customer request page contract
- `docs/reference/request-list_6_3/request-list-model-v1.md` — request list contract
- `docs/reference/request-list_6_3/request-list-decisions.md` — request list styling and action decisions
