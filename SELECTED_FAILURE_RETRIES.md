# Selected-failure retries

Create a separate asynchronous extraction for failures selected from a terminal extraction (`completed`, `failed` or `cancelled`). The original results, errors and attempts remain unchanged. Each child has its own quota admission, outbox entry, attempts, cancellation and status URLs. Its summary exposes `retry.sourceExtractionId`, `retry.mode` and `retry.selectedCount`.

## API

```http
POST /api/received-documents/extractions/{sourceExtractionId}/retries?companyId=acme-sa
XApiKey: <company credential>
Content-Type: application/json

{
  "mode": "download",
  "documentIds": [123, 125],
  "password": "<enter the SRI password locally>",
  "clientRequestId": "a8188030-7594-4a5a-8a12-d70a2be046de"
}
```

`documentIds` are **result IDs from the source extraction's documents endpoint**, not database document/file IDs. Select 1–500 distinct positive IDs. Every result must belong to that extraction and have a usable 49-digit access key. Selection is all-or-nothing. A successful result cannot be selected for a download retry. Unreadable rows without a usable key and general login/pagination failures require a new regular query; this endpoint does not guess their identity.

A download retry inherits taxpayer, additional user, period, type and format. It requires a fresh password because terminal jobs erase their credentials. Only the selected files are downloaded. Portal pages still have to be traversed to renew JSF references and locate the selection; newly encountered or previously successful documents are not downloaded or copied into the child. The portal's reported total refers to all query rows, while child document counters refer only to selected results.

Unidentifiable portal rows make the traversal incomplete, because they could conceal a selected document. Missing selections produce `selectedDocumentNotFound` after a confirmed full traversal, or `selectedDocumentNotReached` after an incomplete traversal. Their page/row refers to the source extraction, as stated in the error. Files confirmed by this child before an interruption are preserved even if a subsequent traversal does not observe them; pagination then remains incomplete.

For stored XML with failed C# parsing or failed JSON conversion:

```json
{
  "mode": "parsing",
  "documentIds": [127],
  "clientRequestId": "3b0dd7f3-39ea-4aa7-ad10-02400df0b580"
}
```

Parsing mode rejects passwords, stores no SRI credentials and never invokes the crawler. It uses the registered file of each selected source result, verifies storage and validation evidence, and reads/revalidates bytes as required. It never silently downloads a missing file or chooses another file/period. Missing, inaccessible or rejected files produce a failed result. XML bytes are not rewritten. Existing compatible conversions may be reused; a failed conversion never removes an older valid conversion. PDF, unsupported parsing and successful parsing without a failure are not eligible.

Parsing retries use source metadata/page/row, not newly queried portal metadata. `pagination.status` remains `notStarted`; no portal pages were traversed. `downloadedCount` retains its existing meaning of available confirmed files, and successful file acquisitions are `reused`. A parsing retry with available files but unresolved parsing reports `extractionStatus: partial`, even if `failedCount` is zero; inspect `jsonParseStatus`, `parseStatus` and errors for the distinction.

The endpoint always returns **202** with the standard accepted response. Follow its existing status/documents/errors URLs; the existing cancellation endpoint applies. It does not add headers or a synchronous execution mode.

| Response | Meaning |
|---|---|
| 400 | Invalid/missing mode or selection, duplicates, missing download credentials, credentials supplied for parsing |
| 401 / 403 | Missing/invalid key or wrong credential class |
| 404 | Source nonexistent or outside the authorized company |
| 409 | Source still active, non-retryable selection or idempotency conflict |
| 429 | Existing company workload/usage limit |
| 503 | Persistence unavailable; acceptance not confirmed |

`clientRequestId` is optional and company-scoped, sharing the ordinary query namespace. Its retry identity is source extraction + mode + sorted selected result IDs. Order and password do not affect identity. Repeating the same identity returns the same child, even after completion/cancellation, without updating credentials or charging another slot. A new retry needs a new UUID or no UUID. Never reuse the source query's UUID for its child.

## Persistence and recovery

Migration `20261013044123_SelectedFailureRetries` adds nullable source extraction, mode and selected-result JSON columns and a restrictive parent foreign key. Historical jobs remain ordinary jobs with null retry fields. The bounded selection stores only result IDs; it does not embed document JSON or credentials. Admission and outbox persistence retain the company transaction lock and quota rules.

The worker reuses taxpayer/company execution locks, cancellation polling, retry budgets and expiry. Parsing-only jobs are dispatchable and expire without requiring encrypted credentials. During recovery, a download retry keeps `refresh` for not-yet-confirmed selections and reuses files already confirmed by the child. No parent result or attempt is modified. Acceptance emits controlled event 1430 with source ID, mode, count and idempotent-match flag; no password or access key is logged.

## Activation from Warp

Back up PostgreSQL first, then stop API/worker, build both and apply the migration before starting them:

```sh
docker compose --env-file .env.jobs stop sri-descarga worker
docker compose --env-file .env.jobs build sri-descarga worker migrate
docker compose --env-file .env.jobs run --rm migrate
docker compose --env-file .env.jobs up -d sri-descarga worker
docker compose --env-file .env.jobs logs -f sri-descarga worker
```

The shared parser/storage configuration and existing `XApiKey` remain unchanged. Do not run the new binaries against an unmigrated database. This feature's implementation tests use isolated infrastructure; they do not upgrade the running local stack.

## Verification

The final suite passed **367 .NET tests and 13 Node tests** (15 new .NET cases); 11 opt-in browser/live-storage tests were skipped. Worker build, EF model/migration consistency and the public 32-request collection checks passed.

Automated acceptance exercises real PostgreSQL transactions/migrations and the HTTP API with two companies, bounded selection, idempotency races, quota/cancellation behavior, missing selections, recovery after confirmed writes, real stored XML validation, failed and successful conversions, unchanged original bytes/history, and safe unavailable-database responses. Simulated multi-page traversal verifies that only selected XML/PDF files are downloaded. Existing pagination, validation, reuse, Local/S3/R2, parser and issued regressions remain required.

Real SRI and live S3/R2 retry acceptance are pending; no browser or AWS operation is needed for the isolated checks. No commits, PR or deployment are included.

## Local activation evidence

The local stack was upgraded after a private PostgreSQL backup. Migration `20261013044123_SelectedFailureRetries` is applied; API and worker run the rebuilt images and all four services are healthy. Live HTTP checks verified admission, idempotency, summary linkage, cancellation before execution, credential roles, company isolation and invalid selections. Hangfire executed a parsing retry without credentials and returned the expected unavailable-file result. Synthetic records were removed, and all nine historical crawler table counts/content fingerprints matched the pre-activation baseline (663 documents/files retained). No browser, SRI or AWS operation occurred. Real SRI/live-storage retry acceptance remains pending.
