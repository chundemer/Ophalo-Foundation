# ADR-499 — Authenticated app-shell: account menu and navigation altitude

**Status:** Locked
**Date:** 2026-09-07
**Context:** GAP-054; discovery in [BL148](../build-log/148-gap-054-app-shell-navigation-discovery.md)
**Related:** ADR-368 (type/color), ADR-428 (three-section Settings), ADR-496 (Price Book
discoverability), ADR-497 (multi-workspace sign-in — no in-session switch), GAP-038 (Help & Updates),
GAP-053 (row action order — unrelated), F6 (New Request CTA consistency — deferred)

## Decision

The authenticated Keep workbench shell (`web/ophalo-app/src/App.tsx`) separates **workspaces you
operate in** from **account- and business-level concerns**:

1. **Top-bar nav pills** carry only operating workspaces: **Requests** (always) and **Price Book**
   (Owner/Admin + `keep.price_book_quotes_materials` entitled). Price Book stays a first-class
   top-level pill — it is not moved into a menu (preserves ADR-496 §6 "immediately visible").

2. **A new account menu** replaces the static `"{name} · {role}"` text and the loose **Sign out**
   button in the desktop top bar. It is a menu-button (`aria-haspopup="menu"`, `aria-expanded`)
   labelled with the current **workspace (business) name**, opening a popover containing:
   - an identity header: business name + `{userName} · {roleLabel}`;
   - **Business Settings** group (Owner/Admin only) — *Company Profile & Public Link*,
     *Response Policy (SLAs)*, *Team Seats & Permissions* — each deep-linking to the existing
     `#/settings?section=…` route;
   - **Sign out**, visually separated, in the terracotta accent (`--ophalo-accent`).
   Operator/Viewer see only the identity header and Sign out.

3. **The standalone "Settings" nav pill is removed.** Its three sections are reached only through
   the account menu (and its mobile equivalent). The `#/settings` routes, `Settings.tsx`, and the
   three section components are unchanged. The menu trigger takes an active affordance whenever a
   `#/settings` route is open (replacing the removed pill's active styling).

4. **The workspace name is displayed, never switched.** Changing workspace remains sign-out →
   sign-in per ADR-497 rule 8. An in-session switcher is explicitly out of scope for V1 and is a
   candidate only after the native mobile app introduces a multi-context session model.

5. **The mobile overflow menu (`MobileNavMenu`) gains the same Business Settings group** (Owner/
   Admin) and the Price Book entry. This **deliberately reverses** the 2026-08-26 lock that
   unconditionally omitted Settings and Price Book from the phone menu
   (`PHONE_OMITTED_NAV_IDS`). Rationale for the reversal: that lock's stated basis — "no viewport
   where the phone menu opens and desktop nav is also unavailable" — is false for a narrow
   (< 768px) portrait tablet or a phone, where the overflow menu is the *only* navigation surface.
   Under the omission, an Owner on a phone has no path to Team, Response Policy, or Company Profile.

## Scope boundary (what this decision does NOT authorize)

- No subscription, billing, plan, trial, or seat-plan surface in the menu. Entitlement stays on the
  authorized internal path (ADR-496 §6); self-service commercial workflow is GAP-070/071, deferred.
- No "Getting Started" nav entry (removed by ADR-496 §4).
- No redesign of the Settings section pages, including for narrow layouts.
- No change to `getRouteFromLocation`, route shapes, or the RequestWorkbench pane-split behavior.
- The **Help & Updates / feedback** menu entry is added by **GAP-038**, sequenced immediately
  after GAP-054 — not built here (avoids a dead link).
- A **"Your profile"** menu entry (all roles, desktop + mobile) is added by **GAP-072** — a
  self-service page editing `User.Name`. Until then the account menu carries only the identity
  header and Sign out for Operator/Viewer, which is acceptable: no user-profile surface exists in
  V1 and the identity header already resolves finding F7 (workspace/role confirmation). Display
  name is Foundation identity, edited only by its owner; future per-user *product* settings attach
  to `AccountUser`. GAP-072 gets its own discovery ADR.
- The New Request global-CTA suppression inconsistency (BL148 F6) is a separate later slice.

## Consequences

- Settings moves one interaction deeper for Owner/Admin — accepted as a low-frequency
  configuration action; watched in pilot feedback.
- A reusable dropdown pattern already exists in-repo (`KeepSplitButton`: self-contained open state,
  outside-pointer + Escape close, `role="menu"`/`"menuitem"`). The account menu follows that
  pattern rather than adding a new shared primitive, and adds menu keyboard semantics (Escape
  restores focus to the trigger; Up/Down/Home/End move between items).
- Browser verification (desktop + narrow; Owner/Admin/Operator/Viewer; keyboard-only operation;
  active-route treatment) is recorded on the implementing build-log entry per the GAP-054 tracker
  requirement.
