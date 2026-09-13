# Founder Guide — Feedback, Help & Updates

Use this guide to publish Help & Updates content and operate incoming feedback during the pilot.
It is the practical checklist. The complete operating contract, access setup, and incident detail are
in the [Feedback, Help & Updates operations guide](../runbook/feedback-help-updates-operations.md).

## Non-negotiable rules

- The repository copy, `docs/content/updates.json`, is the editorial source of truth. Do not make
  the R2 dashboard your only copy or edit a live object without updating the repository.
- Publish only after schema validation succeeds. Valid JSON alone is not enough.
- Take a rollback backup and commit it before replacing the live feed.
- Never put customer data, tokens, webhook URLs, or raw request content into an update, guide,
  screenshot, or operational log.
- Announce only behavior that is already shipped and observable in production.

## Before every publish

1. Start from the current committed `docs/content/updates.json`.
2. Copy it to `docs/content/archive/updates.json`. This is the immediate rollback copy; Git history
   retains older versions.
3. Edit `docs/content/updates.json`.
4. Run the validator from the repository root:

   ```sh
   pnpm --dir web/ophalo-app validate:updates
   ```

5. Stop if validation fails. Correct the source file and run it again; do not upload a workaround.
6. Review the change, then commit both `docs/content/updates.json` and
   `docs/content/archive/updates.json` together.

## Publish and verify

1. Upload `docs/content/updates.json` to the configured platform-content bucket using the exact key
   `platform/updates.json`, replacing the existing object.
2. Wait up to five minutes for the API cache.
3. Sign in with a safe test account and open `#/help`.
4. Confirm the new entry or guide renders correctly, links work, and any screenshot is visible.
5. Check that no new `content_source_failure` alert appeared in Railway.

If the change is urgent, these same steps still apply. A rushed, unvalidated feed risks an empty
Help surface on a cold API instance.

## Roll back a bad publish

1. Upload `docs/content/archive/updates.json` to `platform/updates.json`.
2. Wait up to five minutes and verify `#/help` again.
3. Record what went wrong in the next commit or the relevant work item before attempting a corrected
   publish.

## Writing content safely

- Use `known_issue` for a current, customer-relevant problem; mark it `resolved` when fixed.
- Use `whats_new` only for a released change.
- Use `coming_soon` for bounded direction, never dates, prices, promises, or unshipped behavior.
- Keep guides task-focused: one goal, 3–7 current UI steps, expected result, and one recovery path.
- Use sanitized Markdown only. Guide images must be PNG, JPEG, or WebP, free of real customer data,
  use an immutable hashed filename, and be referenced through `/updates/guides/img/<name>`.
- Ask a second person to walk through a guide in production before publishing it.

## Feedback daily routine

1. Review new founder-channel feedback on each business day.
2. Create a restricted feedback-register row with the correlation ID, time, business, submitter,
   category, owner, status, and follow-up outcome. Do not copy the full message or phone number.
3. Reply by email when appropriate and record the response date, method, and outcome.
4. For a recurring issue, redact any PII before creating a workboard item or engineering report.
5. Communicate broad-impact issues with a `known_issue` or `whats_new` entry after the facts are
   confirmed.

The founder channel is at-least-once delivery: duplicate delivery IDs are possible. Treat a `503` as
not persisted; a `200` or `202` means it was accepted and may be delivered after automatic retry.

## Pilot checks

Before treating this loop as live, confirm that Help & Updates is visible on desktop and mobile, the
highlight banner can be dismissed, one harmless `other` feedback test reaches the founder channel,
and the founder explicitly accepts the single-webhook outage posture (or records an independent
fallback). Keep the API at one replica until the documented worker/cache hardening is completed.
