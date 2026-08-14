# Capability Package Enrollment Runbook

Use this runbook to grant the Price Book capability to a known account during the controlled
rollout. The capability key is `keep.price_book_quotes_materials`.

## Preferred operator path

The deployed API exposes an internal-only operator surface:

```text
GET  /internal/accounts/{accountId}/capability-packages/{featureKey}
POST /internal/accounts/{accountId}/capability-packages/{featureKey}/enroll
POST /internal/accounts/{accountId}/capability-packages/{featureKey}/disable
POST /internal/accounts/{accountId}/capability-packages/{featureKey}/reenable
```

It requires an authenticated Admin or Owner in an `Internal`-purpose account with
`internal.entitlements.manage`. A normal business Owner cannot grant their own account access.
Use the response's `concurrencyVersion` in the JSON body for `disable` or `reenable`:

```json
{ "concurrencyVersion": "the-current-version" }
```

There is not yet a supported bootstrap path for the first Internal-purpose operator account. Do
not create a dummy trade business through public onboarding merely to use this API.

## One-time founder bootstrap through Railway

Until the Internal-purpose operator bootstrap exists, use the Railway Postgres service's query
console for a controlled founder-account enrollment. Do not expose a production database URL to a
desktop client, browser console, source control, or chat. If a credential has been exposed, rotate
it in Railway before continuing.

Replace `<account-id>` with the known business account UUID. First check whether the enrollment
already exists:

```sql
SELECT
  account_id,
  feature_key,
  status,
  enabled_at,
  disabled_at
FROM account_capability_package_enrollments
WHERE account_id = '<account-id>'
  AND feature_key = 'keep.price_book_quotes_materials';
```

If a row exists, enable (or re-enable) it:

```sql
UPDATE account_capability_package_enrollments
SET
  status = 'Enrolled',
  enabled_at = now(),
  disabled_at = NULL,
  changed_by_account_user_id = (
    SELECT primary_owner_account_user_id
    FROM accounts
    WHERE id = '<account-id>'
  ),
  concurrency_version = gen_random_uuid(),
  updated_at_utc = now()
WHERE account_id = '<account-id>'
  AND feature_key = 'keep.price_book_quotes_materials';
```

If no row exists, create it:

```sql
INSERT INTO account_capability_package_enrollments (
  id, account_id, feature_key, status,
  enabled_at, disabled_at, changed_by_account_user_id,
  concurrency_version, created_at_utc, updated_at_utc
)
SELECT
  gen_random_uuid(),
  id,
  'keep.price_book_quotes_materials',
  'Enrolled',
  now(),
  NULL,
  primary_owner_account_user_id,
  gen_random_uuid(),
  now(),
  now()
FROM accounts
WHERE id = '<account-id>';
```

The `changed_by_account_user_id` in this one-time bootstrap records the business primary owner
because no Internal operator identity exists yet. Once that operator bootstrap is available, use
the API path above so the changed-by field reflects the internal actor.

## Verify and recover

Re-run the read-only query above. It must return exactly one row with `status = Enrolled`. Reload
the staff app so its capability query refreshes, then verify the Price Book entry point and the
intended catalog/assembly workflow.

If a direct SQL operation fails, stop and use the read-only query before retrying; do not assume
the write failed. If an internal API transition returns `409 Conflict`, fetch its current status
and retry only with the returned current `concurrencyVersion`.
