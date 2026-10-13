# Company API access

The crawler is a private service consumed by Laravel. Company credentials authenticate one accounting company; they are independent of SRI passwords. Laravel manages users, taxpayer permissions, billing and the UI. Never deliver crawler credentials to the frontend.

## Configuration and activation

`ApiSecurity:Mode` defaults to `required`. Every received endpoint requires a valid company credential, and administration requires a separate administrator credential. Missing, invalid, duplicated, expired or revoked keys return 401; a credential of the wrong kind returns 403. Credential database failures return a controlled 503 and deny access.

Generate the initial administrator configuration from the repository directory:

```sh
python3 scripts/setup-api-security.py
```

This writes `.env.security` (mode, public administrator ID, base64 SHA-256 hash and default company-key lifetime) and `.secrets/administrator-api-key` (the actual administrator credential). Files use permissions 600; the secret directory uses 700. Both are ignored by Git and excluded from Docker builds. No secret is printed. The old `XApiKey` setting in appsettings is removed and is not accepted.

The API alone receives `.env.security` through Compose. Workers, parser and migrations do not receive administrator configuration. Native Development loads this file from the project directory or its immediate parent; environment variables and command-line/test overrides take precedence. File loading does not set process variables.

Before activating new code, stop the API/worker, apply the additive `CompanyApiAccess` migration, then rebuild:

```sh
docker compose --env-file .env.jobs stop worker sri-descarga
docker compose --env-file .env.jobs run --rm --build migrate
docker compose --env-file .env.jobs up --build -d
```

This does not migrate documents, change storage keys or import historical files. Back up PostgreSQL before production migrations using the existing async guide. The migration adds `crawler.api_keys`; existing extraction/document records retain their values.

Compose publishes the API on `127.0.0.1:8080`. Connect Laravel through the local/private network; configure HTTPS before sending credentials outside the machine. This feature does not install a reverse proxy or expose the service publicly. Keep S3/R2 private and never configure a proxy to serve the document volume directly.

For native unauthenticated development, set `ApiSecurity__Mode=localDevelopment` in the private file and use `ASPNETCORE_ENVIRONMENT=Development`. This mode is rejected in every other environment. Local development preserves the received and issued contracts. Key administration always requires the administrator credential. Supplied invalid keys are rejected even in local mode.

Swagger and the optional Hangfire dashboard are available only in explicit local-development mode. Dashboard remains disabled by default with its existing local-request filter. In required mode issued routes are absent. `/health` is anonymous and only reports availability. Direct static document URLs are disabled in every mode.

## Administrative API

Use the existing `XApiKey` header. It is not accepted through URLs, cookies or request bodies, and is not forwarded to workers, Node or the SRI. No CORS policy is enabled for frontend access and no additional authentication headers are introduced.

| Method | Route | Result |
| --- | --- | --- |
| POST | `/api/admin/companies/{companyId}/api-keys` | 201; key metadata and a credential returned once |
| GET | `/api/admin/companies/{companyId}/api-keys?cursor=…&limit=…` | Cursor-paginated metadata; no secrets/hashes |
| DELETE | `/api/admin/companies/{companyId}/api-keys/{keyId}` | 204; repeat revocation is safe; unknown/foreign key is 404 |

Create body:

```json
{ "name": "Laravel integration", "expiresAt": "2027-01-01T00:00:00Z" }
```

`name` is required, with at most 100 characters. An omitted/null `expiresAt` uses 90 days by default (`ApiSecurity__DefaultKeyLifetimeDays`, 1–365). Explicit expiration must be in the future and is stored in UTC. Company identifiers retain the existing lowercase letters/numbers/hyphens rule and 64-character limit.

Creation returns:

```json
{
  "key": {
    "keyId": "PUBLIC_UUID",
    "companyId": "acme",
    "name": "Laravel integration",
    "createdAt": "UTC_TIMESTAMP",
    "expiresAt": "UTC_TIMESTAMP",
    "revokedAt": null,
    "status": "active"
  },
  "apiKey": "SECRET_RETURNED_ONLY_AT_CREATION"
}
```

Key states are `active`, `expired`, `revoked`. Key listings default to 100 items, maximum 500; `nextCursor` is an opaque numeric ordering cursor, distinct from the public UUID. Invalid cursors/limits return 400.

PostgreSQL stores an independent public identifier, SHA-256 hash of a 32-byte random secret, company, name, timestamps and administrator identifiers for creation/revocation. Authentication compares hashes in constant time and checks PostgreSQL on each company request; no authentication cache is used. The secret cannot be recovered from the listing/database. If creation's response is lost, find/revoke that key and create another; do not automatically retry creation indefinitely.

To rotate company credentials, create a replacement, update Laravel and revoke the previous key. Overlap is supported. Revocation/expiration stops subsequent requests; it does not cancel accepted jobs or an already authorized stream. Administrator credentials only manage keys and cannot read/download/create document extractions. Company credentials cannot use administration.

Rotate the administrator with `python3 scripts/setup-api-security.py --rotate`, update its credential in Laravel and recreate the API. Workers do not need that credential. Store administrator credentials only in the trusted Laravel server/operator secret configuration.

Curl examples use placeholders; real secrets belong in private local configuration:

```sh
curl --request POST 'http://localhost:8080/api/admin/companies/acme/api-keys' \
  --header 'XApiKey: ADMINISTRATOR_API_KEY' \
  --header 'Content-Type: application/json' \
  --data '{"name":"Laravel integration"}'

curl 'http://localhost:8080/api/admin/companies/acme/api-keys?limit=100' \
  --header 'XApiKey: ADMINISTRATOR_API_KEY'

curl --request DELETE 'http://localhost:8080/api/admin/companies/acme/api-keys/KEY_UUID' \
  --header 'XApiKey: ADMINISTRATOR_API_KEY'
```

## Company authorization and downloads

Keep `companyId` in received bodies/queries. The credential determines authorization; the supplied value must match. Creating an extraction for another company returns 403 before acceptance. Foreign/nonexistent extraction resources return 404, including when identifiers are combined with the authenticated company's own `companyId`. Existing SQL company filters remain in place.

```sh
curl 'http://localhost:8080/api/received-documents/extractions/EXTRACTION_UUID?companyId=acme' \
  --header 'XApiKey: COMPANY_API_KEY'

curl 'http://localhost:8080/api/received-documents/extractions/EXTRACTION_UUID/documents/RESULT_ID/file?companyId=acme' \
  --header 'XApiKey: COMPANY_API_KEY' --output document.xml
```

The file route uses the existing detail's `documentId`: the extraction-result ID, not the global document ID. It verifies extraction/result/document/file relationships, company, owner taxpayer, direction and access key. The provider/bucket/key are resolved from the database and checked against the document identity; caller-supplied storage paths/URLs are never accepted. Downloads stream through the configured Local/S3/R2 adapter and release the stream on completion/disconnection.

Only registered files associated with successful results are exposed. Missing files return 404; inconsistent references, incompatible storage destinations, permissions/transport/database failures before streaming return a controlled 503. A failure after streaming has started may interrupt the response; clients must not treat an incomplete response as a complete file. The endpoint serves the current file associated with the record, not immutable historical bytes. No signed URLs or public bucket permissions are created.

Logs record controlled authentication/key-lifecycle events using public IDs and authorized company identifiers. Keys, hashes, bodies, SRI passwords and external exception messages are excluded. Framework body/SQL logging remains disabled.

## Verification

Acceptance uses an isolated PostgreSQL instance, synthetic documents and HTTP test hosts. Two companies with the same taxpayer can accept/query/download their own results; identifier/company manipulation cannot cross the boundary. Tests exercise company-key issuance, cursor listing, rotation, revocation/expiration, role separation, static/issued restrictions, configuration priority, actual database connection failure, stream cancellation and Local/S3/R2 adapter reads with substituted remote clients.

The setup script is checked for matching hashes, private permissions, no stdout secrets, overwrite refusal and explicit rotation. Migration compatibility preserves historical records. Live SRI, browser and cloud storage acceptance is not required for this security block; no AWS permissions/resources are changed.

Final verification: 315 .NET tests passed; 11 opt-in tests were skipped (10 browser scenarios and live remote storage). All 13 Node tests passed. Worker compilation and the migration command succeeded without administrator configuration in the migration process; EF reports no pending model changes. Compose validation passed. Test-host processes, synthetic files, parser and PostgreSQL test containers/volumes were removed; existing user services/database and storage were preserved.
