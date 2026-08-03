# Private Cloudflare R2 Business-Document Storage Setup

Use this runbook to provision or rotate the production storage configuration required by
ADR-471. It creates one private shared Cloudflare R2 bucket for business documents. Object
keys, rather than separate buckets, scope documents to their account and purpose.

## Outcome

The production `Ophalo-Foundation-API` service in Railway receives these four configuration
values:

| Railway variable | Value |
| --- | --- |
| `R2__CloudflareAccountId` | Cloudflare Account ID |
| `R2__BucketName` | `ophalo-business-documents` |
| `R2__AccessKeyId` | R2 Access Key ID |
| `R2__SecretAccessKey` | R2 Secret Access Key |

The API builds its S3-compatible endpoint as
`https://<ACCOUNT_ID>.r2.cloudflarestorage.com`; do not add a separate endpoint variable.

## Before starting

- Sign in to the Cloudflare account that owns `ophalo.com` and make sure R2 is enabled. Cloudflare
  may require a payment method before it permits R2 S3 credentials.
- Sign in to Railway and open the `Ophalo-foundation` project.
- Have a secure place ready to store the Access Key ID and Secret Access Key. A password manager
  secure note is preferred. Never commit either value, put it in a tracked `.env` file, or send it
  in chat/email.
- Prefer an **Account API token** when the operator has Cloudflare Super Administrator access. It
  survives staff access changes. A User API token is an acceptable fallback, but stops working if
  that user loses access.

## 1. Create the private R2 bucket

1. In Cloudflare, select the correct account using the account switcher.
2. Navigate to **Storage & databases** > **R2** > **Overview**.
3. Select **Create bucket**.
4. Set the bucket name to `ophalo-business-documents`.
5. Use the normal/default jurisdiction unless a specific legal requirement requires EU or FedRAMP
   storage. A jurisdictional bucket uses a different endpoint, so do not select one casually.
6. Select the **Standard** storage class.
7. Click **Create bucket**.

R2 buckets are private by default. Preserve that default:

- Do **not** enable an `r2.dev` public development URL.
- Do **not** attach a custom domain.
- Do **not** add a public access binding.

Record the bucket name and the Cloudflare Account ID shown in R2 Overview's **Account Details**.

## 2. Create the least-privilege R2 credential

1. On **R2** > **Overview**, find **API Tokens** in Account Details and select **Manage**.
2. Select **Create Account API Token**. If unavailable, select **Create User API Token** and note
   its dependency on that user's Cloudflare access.
3. Name it `ophalo-production-business-documents`.
4. Under permissions, select **Object Read & Write**.
5. Under the bucket scope, choose **Apply to specific buckets only**.
6. Select only `ophalo-business-documents`.
7. Create the token.
8. Copy the **Access Key ID** and **Secret Access Key** immediately into the password manager.
   Cloudflare displays the secret only once.

Do not choose an account-wide R2 Admin permission. Object Read & Write supplies the object
read/write/list/delete behavior the application needs, including best-effort cleanup on a failed
staged upload.

## 3. Configure CORS

CORS does not make a bucket public. It only restricts which browser origins may use a valid future
presigned URL. V1 uploads go through the authenticated API and do not use this policy yet.

1. Open `ophalo-business-documents` in R2.
2. Select **Settings**.
3. Under **CORS Policy**, select **Add CORS policy**.
4. Open the JSON editor and enter the following exact policy.
5. Select **Save**.

```json
[
  {
    "AllowedOrigins": [
      "https://app.ophalo.com",
      "http://localhost:5173",
      "http://localhost:3000"
    ],
    "AllowedMethods": ["GET", "PUT"],
    "AllowedHeaders": ["*"],
    "MaxAgeSeconds": 3600
  }
]
```

Confirm there is no wildcard origin and no `https://www.ophalo.com` entry. The production origin
must be exactly `https://app.ophalo.com`.

## 4. Add production configuration in Railway

1. In Railway, open project **Ophalo-foundation** and select the **production** environment.
2. Open service **Ophalo-Foundation-API** (not the Postgres service).
3. Open its **Variables** tab.
4. Create these four service variables:

   ```text
   R2__CloudflareAccountId = <Cloudflare Account ID>
   R2__BucketName          = ophalo-business-documents
   R2__AccessKeyId         = <R2 Access Key ID>
   R2__SecretAccessKey     = <R2 Secret Access Key>
   ```

5. Verify the names exactly: `R2` is uppercase and is followed by two underscores. Do not add
   quote marks or leading/trailing spaces to values.
6. Use Railway's variable menu to **Seal** `R2__SecretAccessKey` when available. Sealed values are
   supplied to deployments but cannot later be displayed or retrieved.
7. Review Railway's staged change and select **Deploy**.

Use service variables rather than shared variables: only the API needs the credential.

## 5. Verify the deployment

1. Wait for the `Ophalo-Foundation-API` deployment to finish.
2. Read its deployment logs. The service must remain online and must not report `R2Settings is
   incomplete`, `storage cannot start`, or AWS/S3 authentication/endpoint errors.
3. Check both API health endpoints:

   ```text
   https://api.ophalo.com/health/live
   https://api.ophalo.com/health/ready
   ```

   Both should succeed. Their deliberately minimal public response does not identify R2.
4. Confirm the Railway service does not restart repeatedly.

There is no functional browser upload to test yet. The expected result is simply that production
boots with the real R2 storage adapter registered. No object should be present in the bucket until
the later upload feature ships.

## Local development

R2 is optional in Development. If the settings are unset, the API intentionally uses its local-disk
fake storage instead of production R2.

Only if a developer specifically needs to exercise R2 locally, run these from the repository root:

```bash
dotnet user-secrets set "R2:CloudflareAccountId" "<account-id>" --project src/OpHalo.Api
dotnet user-secrets set "R2:BucketName" "ophalo-business-documents" --project src/OpHalo.Api
dotnet user-secrets set "R2:AccessKeyId" "<access-key-id>" --project src/OpHalo.Api
dotnet user-secrets set "R2:SecretAccessKey" "<secret-access-key>" --project src/OpHalo.Api
```

## Completion checklist

- [ ] Private bucket `ophalo-business-documents` exists.
- [ ] No public URL, public binding, or custom domain is configured.
- [ ] An Object Read & Write credential is scoped only to that bucket.
- [ ] Both credential values are securely retained and are absent from source control.
- [ ] CORS contains only the three approved origins, `GET` and `PUT`, wildcard allowed headers,
      and a 3,600-second max age.
- [ ] The four `R2__...` variables exist on the production API service.
- [ ] The new Railway deployment finishes successfully and stays online.

## References

- [Cloudflare: create R2 buckets](https://developers.cloudflare.com/r2/buckets/create-buckets/)
- [Cloudflare: R2 S3 credentials](https://developers.cloudflare.com/r2/get-started/s3/)
- [Cloudflare: R2 authentication and token scope](https://developers.cloudflare.com/r2/api/tokens/)
- [Cloudflare: configure R2 CORS](https://developers.cloudflare.com/r2/buckets/cors/)
- [Railway: service variables and sealing](https://docs.railway.com/variables)
