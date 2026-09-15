# Authoritative Pilot Release Readiness

Use this runbook when OpHalo Keep is about to become the pilot business's authoritative record for
live requests, completed work, or customer follow-up. It is not required while the current lightly
used pilot remains evaluative and its records are recoverable from the founder's local backup.

## Trigger

Begin this process about two weeks before the agreed operational cutover. Do it earlier if a released
customer-approval flow will issue protected links that must survive an API redeploy.

## Before the GAP-069 production migration

1. Upgrade the Railway workspace from Hobby to Pro.
2. On the production Postgres service, enable daily scheduled backups and point-in-time recovery
   (PITR). Do not claim a recovery window until Railway shows that its first PITR base backup is
   complete.
3. Keep the existing local `pg_dump` backup available until a restore drill has passed. Take a new
   manual backup before any consequential migration or production cutover.
4. Record the backup schedule, PITR retention window, and the founder responsible for the Railway
   workspace, database restore, and production cutover.

## GAP-069 deployment and proof

1. Persist ASP.NET Data Protection keys in the existing production PostgreSQL database, using a
   dedicated table and a stable non-secret production application discriminator.
2. Encrypt the stored key-ring entries with a dedicated certificate. Keep its PFX/password in
   Railway production secrets and an independently controlled founder password manager; neither
   belongs in Git, the database, logs, or chat.
3. Configure forwarded headers to accept only `X-Forwarded-Proto` from the measured Railway ingress
   boundary. Reuse that boundary configuration for the existing `ClientIpResolver`; do not broadly
   trust forwarded headers or replace the resolver's rate-limit protections.
4. Before a controlled API redeploy, use the approved temporary founder-only diagnostic to protect a
   harmless value and retain the resulting ciphertext outside the process. After the redeploy, submit
   that ciphertext to the second diagnostic call and prove it unprotects. Remove diagnostic code in
   the same session that introduced it.
5. Prove a direct or untrusted request cannot spoof its scheme or client address with forwarded
   headers.

## Restore drill

Restore to a separate Railway Postgres service or other isolated target; never overwrite production
while testing. Verify the restored database, capture the actual recovery time and point, then record
the cutover or rollback procedure. PITR restore creates a sibling database, so production remains
untouched until the founder deliberately changes the connection/copies recovered records.

## Ongoing rules

- Daily backups and PITR protect routine data mistakes. They are not an off-platform disaster
  recovery copy; keep a deliberate `pg_dump` export strategy for project-level loss.
- Rotate the key-encryption certificate with old and new certificates trusted for decryption during
  the transition; never remove the old certificate before all affected key-ring entries can be read.
- A database restore also requires access to the certificate/password. Neither backup alone is a
  complete Data Protection recovery path.
