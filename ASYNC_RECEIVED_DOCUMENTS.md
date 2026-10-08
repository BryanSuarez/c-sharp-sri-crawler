# Received document extractions

Received queries are persistent jobs. The API accepts them, Hangfire executes them in a separate .NET worker, and a private Node.js service converts XML with `taxo-sri-xml-2-json@1.8.0`. PostgreSQL stores progress, results and versioned JSON. Local/S3/R2 stores the XML/PDF files.

## Local setup in Warp

Requirements: .NET 8 SDK/runtime (the existing TwoCaptcha project also targets .NET 7), Chrome, PostgreSQL 17, Node.js 22+ and pnpm 11.19.0. Existing Playwright downloads remain sequential. Start with one extraction worker.

1. Copy `.env.jobs.example` to `.env.jobs`. Set the PostgreSQL connection string and password. Generate a permanent encryption key with `openssl rand -base64 32` and assign it to `ExtractionJobs__EncryptionKey`. Do not regenerate it while jobs are pending. Development loads `.env.jobs` from the project directory or its immediate parent, without modifying process variables; environment variables take precedence. Keep the existing `.env.storage` configuration.
2. Start PostgreSQL. For local Compose PostgreSQL only, add a loopback port binding using an override or use your local PostgreSQL installation. Production Compose intentionally keeps its database private.
3. Install/build/start the parser in one terminal:

```sh
cd sri-document-parser
pnpm install --frozen-lockfile
pnpm run build
pnpm start
```

4. Apply database migrations once from the repository root:

```sh
DOTNET_ENVIRONMENT=Development ASPNETCORE_ENVIRONMENT=Development dotnet run --project DescagaCompronanteSRI --no-launch-profile -- --migrate
```

5. Start API and worker in separate terminals from the repository root:

```sh
ASPNETCORE_ENVIRONMENT=Development dotnet run --project DescagaCompronanteSRI
```

```sh
DOTNET_ENVIRONMENT=Development dotnet run --project SriCrawler.Worker
```

The API process does not execute Hangfire jobs. The worker must remain running to dispatch and execute them. `/health` checks database availability. The Hangfire dashboard is disabled by default; enabling it retains Hangfire's local-request access filter. It is an operational interface, not the client progress API.

## Docker Compose

Set `.env.jobs` values and `POSTGRES_PASSWORD`. Run from the repository root:

```sh
docker compose --env-file .env.jobs up --build -d

docker compose --env-file .env.jobs logs -f sri-descarga worker parser
```

Compose overrides the connection hostname to `postgres` and the parser URL to `http://parser:3000`. It starts PostgreSQL, applies migrations through the one-shot `migrate` service, and then starts API and worker. The parser has no published port, database access or SRI/storage credentials. Local documents use a shared named volume; downloaded historical files are not migrated into it. Attach your optional private VPN configuration to the worker if required.

The worker and API images target Linux amd64 because issued documents still run synchronously in the API and require Chrome/Xvfb. The parser and migration image can run on arm64 independently. API listens on host port 8080 in Compose. Browser concurrency, rather than customer count alone, determines worker memory requirements.

Native compilation on ARM hosts explicitly sets `PlaywrightPlatform=linux-x64` for Docker publishing. Playwright's executable driver must match the runtime container, even when the .NET assemblies are portable. Docker builds verify its presence and CI launches Chrome against a synthetic page without contacting SRI.

Docker runs Chrome with `SRI_BROWSER_HEADLESS=false` on its private Xvfb display; no desktop browser window is opened. Native runs retain headless mode by default. CAPTCHA rejection remains an explicit SRI query failure if bounded portal retries are exhausted.

The login accepts a trusted SRI authorization redirect when Keycloak has returned a nonempty authorization code but the Angular profile menu has not rendered. It rejects foreign hosts, authorization errors and unrelated paths; access to the protected received-documents application is still verified independently. Temporary authorization codes are excluded from login logs.

The container entrypoint forwards shutdown to .NET, waits for graceful completion and cleans its owned Xvfb display. On restart, it removes stale display locks/sockets only after verifying that no display server is reachable. This also recovers the display after an abrupt container stop.

Compose checks PostgreSQL, parser, API database connectivity and the worker's recent Hangfire heartbeat. A worker process that cannot register/maintain its PostgreSQL heartbeat becomes unhealthy. It does not probe the SRI or open a browser for health checks.

Stop services without deleting persistent data:

```sh
docker compose --env-file .env.jobs down
```

Do not use `down -v` unless intentionally deleting the database and local documents.

## Request and polling

`POST /api/received-documents/query` retains the existing fields. `executionMode` defaults to `async`. Explicit `null`, unknown strings and numeric enum values are rejected. `clientRequestId` is an optional UUID, unique per company. Retrying it with identical filters returns the original extraction; conflicting filters return 409. Changing its password does not update the stored credentials.

```json
{
  "companyId": "acme",
  "user": "YOUR_TAXPAYER_ID",
  "password": "YOUR_SRI_PASSWORD",
  "additionalUser": "",
  "year": 2026,
  "month": 9,
  "day": 0,
  "documentType": "invoice",
  "downloadFormat": "xml",
  "clientRequestId": "f227a919-4c5e-49d9-9ca9-06945615c695"
}
```

The API returns 202 with `extractionId`, ownership and relative URLs for status, documents and errors. No SRI login is attempted in the API process. Database acceptance failures return a controlled 503. Async acceptance does not guarantee that supplied SRI credentials are valid.

Poll status every five seconds initially:

```text
GET /api/received-documents/extractions/{id}?companyId=acme
GET /api/received-documents/extractions/{id}/documents?companyId=acme&limit=100
GET /api/received-documents/extractions/{id}/documents?companyId=acme&limit=100&cursor={nextCursor}
GET /api/received-documents/extractions/{id}/documents/{documentId}?companyId=acme
GET /api/received-documents/extractions/{id}/errors?companyId=acme&limit=100
```

Cursors are stable result IDs; default limit is 100 and maximum is 500. The listing includes access keys but excludes large parsed payloads. Detail includes legacy `document.parsedDocument`, new `documentJson`, conversion status and parser/version/hash information. `documentId` in these routes is the extraction-result ID, including unreadable rows. Summary never contains the complete list of documents or errors.

All routes check company ownership. This is not authentication; the API remains intended for local development until the separate authentication block.

For a small account, add `"executionMode": "sync"`. This submits the same persistent job and waits up to 120 seconds. A finished query returns the previous result shape plus `extractionId` (200 after query execution, 500 for pre-query failure). If still pending, it returns 202. Closing the HTTP connection stops the wait and leaves the job intact.

## Persistence and conversion

Canonical identity is `(companyId, taxpayerId, direction, documentType, accessKey)`. `taxpayerId` is the queried user, not the issuer's RUC. A queried 10-digit ID remains a 10-digit ID. The issuer's RUC is stored separately.

Files retain keys:

```text
{companyId}/{taxpayerId}/{year}/{month}/received/{documentType}/{accessKey}.{xml|pdf}
```

XML/PDF are separate file records on the same document. New independent queries retain the existing overwrite/redownload policy. Conversion versions and extraction-result references remain historical. A PDF or a failed conversion does not erase previously valid JSON.

The parser consumes the authorization response, or wraps an isolated receipt without inventing status/date. Only the conversion copy has XMLDSig `Signature` subtrees removed; the stored XML remains unchanged. DTD/entity declarations are rejected in the envelope and embedded XML. The JSON is the library's transformed business representation, not a lossless XML tree. Its Spanish/SRI fields and object/array shapes are deliberately retained.

Supported: invoices, purchase settlements, credit/debit notes and withholdings. Guides are explicitly unsupported by version 1.8.0; their files remain downloadable. PDF conversion is `notApplicable`. Parser identity mismatch or conversion failure preserves the saved file and appears separately from download failure. The internal `DocumentReprocessor` can create a fresh conversion from stored XML without contacting SRI; no public reprocessing endpoint is included.

Known dependency behavior verified by fixtures: several invoice payments are emitted in `infoDocumento.pago` as an object with numeric keys. Product taxes can be an object or an array. Do not assume a normalized accounting schema. Stored XML is the authority for additional normalization or dependency corrections.

## Metadata and file validation

Received downloads now require valid XML/PDF, confirmed storage and persistence. Amounts can be null, normalized dates accompany the original text, and file validation, storage and parsing have independent states. Summary includes `validationIssueCount` and `metadataIssueCount`; lists/detail expose validation evidence and metadata states. See [metadata and file validation](RECEIVED_DOCUMENT_VALIDATION.md) for formats, schema versions, limits, historical compatibility and the additive migration.

## Recovery and limits

An extraction and its outbox entry are committed together. Publishing into Hangfire is retried by the worker; duplicate delivery is expected and guarded. PostgreSQL advisory locks serialize sessions for the same queried taxpayer across companies. Loss of the lock connection cancels the browser execution. Sliding invisibility protects long Hangfire jobs; the database remains essential to operation.

Each attempt creates a new SRI session and starts pagination again. Previously confirmed documents within the same extraction are reused; portal JSF identifiers are refreshed. Unseen documents from an earlier attempt prevent claiming pagination completeness. Unreadable rows retain attempt/page/row and previous-attempt rows are excluded from current counts.

`jobStatus` describes execution (`queued`, `running`, `retrying`, `completed`, `failed`). `extractionStatus` describes its business result (`completed`, `partial`, `failed`, `noDocuments`). Parser failure does not change a successful file download. `discoveredCount = downloadedCount + failedCount`; conversion failures/unsupported results and portal duplicates are separate.

Defaults: one worker, three extraction attempts, transient retry delays 30/120 seconds, six-hour attempt deadline, 30-second activity heartbeat, two-minute stale-execution reconciliation, and credentials expiring after 24 hours. Credentials are AES-GCM encrypted with extraction ID as associated data and removed at terminal completion/expiry. The database contains no plaintext SRI password and Hangfire only receives an extraction ID.

Graceful worker shutdowns are recorded separately and do not consume the transient-failure retry budget. The public attempt count includes these interrupted sessions, so it can exceed three after restarts. Credentials and confirmed document results remain available for recovery until completion or expiry.

Parser defaults: 30-second request timeout, one additional transport/5xx retry after two seconds, 20 MiB input cap and one active conversion. The worker image has a 2 GiB cap, parser 512 MiB. Adjust resources from measurements. If parsing is unavailable, files are preserved with explicit conversion errors. If persistence fails, processing stops and recovery runs when PostgreSQL returns.

## Checks and limitations

```sh
dotnet tool restore
dotnet restore DescagaCompronanteSRI.Tests --locked-mode
dotnet restore SriCrawler.Worker --locked-mode
dotnet build SriCrawler.Worker --configuration Release
dotnet test DescagaCompronanteSRI.Tests --configuration Release
SRI_BROWSER_TESTS=1 dotnet test DescagaCompronanteSRI.Tests
cd sri-document-parser && pnpm test
```

Integration tests require an isolated PostgreSQL database in `SRI_TEST_POSTGRES` and a running private parser URL in `SRI_TEST_PARSER`. They apply crawler migrations, use synthetic accounts/documents and never contact SRI or AWS. The optional real S3 suite still uses `SRI_STORAGE_TESTS=1`.

Back up PostgreSQL with `pg_dump` and restore into a separate empty database with `pg_restore`; preserve the external encryption key to recover pending jobs. Back up the local-document volume when not using remote storage. Stop writers during restore and apply explicit migrations before starting workers. XML/PDF and database backups are separate concerns.

## Real acceptance evidence

On October 7, 2026, an asynchronous received-invoice extraction for May 2026 completed with 306 XML documents across seven SRI pages. Extraction `fc1a0987-883c-4a97-a7d4-fb285798b868` was deliberately interrupted after 199 confirmed documents. The worker reopened a session, verified pagination again, reused the confirmed results and completed all 306 without duplicate extraction results or download/conversion failures. Terminal completion removed the encrypted SRI credentials.

The PostgreSQL results each reference a conversion and preserve the queried taxpayer as owner, separately from the invoice issuer. Real S3 object references and a sample XML hash/access key/signature were verified against the persisted JSON. The authorized staging bucket was cleaned before this test, after checking AWS account `991077017871`; no buckets or permissions were created or changed.

The automated container entrypoint probe checks graceful signal forwarding, normal restart and recovery after SIGKILL, without contacting SRI. Run it with `bash scripts/test-container-entrypoint.sh sri-crawler-worker` after building the worker image. SRI availability and CAPTCHA acceptance remain external dependencies; bounded rejection is reported explicitly.

No webhooks, frontend, nightly scheduler, authentication, signed URLs, historical migration or issued-contract changes are included. Real R2 validation requires R2 credentials and remains pending. The earlier recovery acceptance above covers XML. The subsequent metadata/file-validation block also completed real 306-document XML and PDF extractions in isolated local storage; see [the validation acceptance record](RECEIVED_DOCUMENT_VALIDATION.md#real-acceptance-october-7-2026).

## Cross-extraction reuse

New jobs default to `downloadPolicy: reuseValid`; `refresh` bypasses other jobs while retaining same-job recovery. The policy does not change idempotent request identity. Summary includes `newlyDownloadedCount` and `reusedCount`; each result exposes `acquisitionSource`. Existing downloaded counts include confirmed reused files. Apply the additive migration before restarting API/worker. See [reuse, integrity checks, atomic storage and real acceptance](DOCUMENT_REUSE.md).

See [extraction observability](EXTRACTION_OBSERVABILITY.md) for structured logs, persisted timing coverage, attempt history and the additive diagnostics migration.
