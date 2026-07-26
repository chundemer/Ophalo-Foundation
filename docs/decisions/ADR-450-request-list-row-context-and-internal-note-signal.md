# ADR-450 — Request List Row Context And Internal-Note Signal

**Status:** Locked  
**Date:** 2026-07-25  
**Related:** ADR-435, ADR-447, GAP-007

## Decision

A Request List row must let an authorized staff member understand the original customer need and
recognize whether internal team context exists without requiring Request Detail for either answer.

- The original request summary is always rendered as stable row context.
- When truncated, it supports inline `Read full request` / `Show less` expansion without
  navigation or mutation.
- Latest safe customer-visible activity is a separate secondary cue; it never replaces original
  request context.
- A quiet `Internal note` indicator is shown only when the server says the current viewer may know
  an internal note exists. The list never renders note text or feedback-review text.

## Interaction And Safety Rules

- Expansion renders only for actual truncation, uses `aria-expanded`, and is separate from the
  card's detail-navigation target.
- Expansion is local and resets on queue, search, filter, or cursor-page change.
- Note presence and current-viewer permission are server-owned. The cue is neutral context, never
  an attention/severity signal.

## Rationale

The list is the speed/action cockpit. Requiring detail merely to discover the original request or
whether teammates left context is needless interruption; permanently expanding every long request
would instead turn a high-volume queue into an activity feed.
