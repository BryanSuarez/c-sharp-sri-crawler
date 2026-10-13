# Company-scoped extraction cancellation

This feature adds public cancellation only. Selective retries remain a separate checklist item. It does not delete files, remove document history, contact the SRI from the API, or add HTTP headers.

## Request and outcomes

Use the existing company credential and extraction ID:

```sh
curl --request POST \
  'http://localhost:8080/api/received-documents/extractions/EXTRACTION_ID/cancel?companyId=acme-sa' \
  --header 'XApiKey: YOUR_COMPANY_API_KEY'
```

There is no request body. The company must own the extraction. Administrative credentials cannot cancel company work; missing/invalid credentials return `401`, the wrong credential role returns `403`, and foreign or nonexistent resources return `404`.

| Current state | HTTP | Result |
| --- | --- | --- |
| `queued` / `retrying` | 200 | `cancelled`, without starting another attempt |
| `running` | 202 | Durable `cancellationRequested`; the worker stops cooperatively |
| `cancellationRequested` | 202 | Same request and original request timestamp |
| `cancelled` | 200 | Same terminal result and timestamps |
| `completed` / `failed` | 409 | `extractionAlreadyFinished`; history remains unchanged |
| Persistence unavailable | 503 | `persistenceUnavailable`; cancellation is not confirmed |

Example for active work:

```json
{
  "extractionId": "b089d510-419a-4631-aec9-86e462de9169",
  "jobStatus": "cancellationRequested",
  "cancellationRequestedAt": "2026-10-13T04:00:00+00:00",
  "cancelledAt": null
}
```

Poll the existing status endpoint, initially every five seconds, until `jobStatus` becomes `cancelled`. Both timestamps also appear in the summary and synchronous result. The default cancellation polling interval in the worker is two seconds, configured through `ExtractionJobs__CancellationPollIntervalSeconds` (integer 1–30). This is the interval between database checks, not a maximum cleanup time. A network request, storage write or browser startup already in progress may take longer to settle.

A sync request waiting on the same job returns `200` with `jobStatus: cancelled` once cancellation finishes, including when no SRI query ran. It still returns `202` if its wait limit expires first. Disconnecting an HTTP client only stops that HTTP wait; use the cancellation endpoint to cancel the extraction.

## Preserved data, quota and idempotency

Confirmed document results and their registered XML/PDF files remain readable. Counters retain `discoveredCount = downloadedCount + failedCount`. Cancellation does not manufacture failed document rows for files that were never processed. `extractionStatus` is `partial` when any file was confirmed, or `failed` when none was confirmed; `jobStatus` explains that the cause was cancellation. Pagination becomes `incomplete` after an attempt, or remains `notStarted` for work cancelled before its first attempt.

Encrypted SRI credentials are cleared as soon as cancellation is persisted. An active worker may already hold credentials in process memory until its attempt ends. `cancellationRequested` retains pending/publication capacity until acknowledged, even if credentials expire during cleanup. `cancelled` releases that capacity. Accepted-work usage is never refunded from the rolling 24-hour quota.

Repeating the original `clientRequestId` returns the cancelled extraction and does not restart it. A new extraction requires a new ID or omission of `clientRequestId`. Existing download/reuse policies apply normally to that new extraction; this feature does not add a public resume or selective-retry endpoint.

Storage and PostgreSQL are separate systems: if cancellation or failure happens after an object is stored but before its result commits, the file can exist without being confirmed for this extraction. Cancellation performs no bucket cleanup. Later normal downloads can reuse a compatible registered file or overwrite the same deterministic path under the existing validation policy.

## Coordination and recovery

Acceptance of cancellation, attempt startup, result commits, checkpoints and terminal transitions serialize on a short PostgreSQL row lock. No portal or parser HTTP request runs while that lock is held. The first committed terminal transition wins: cancellation after completed/failed work returns `409`; a committed cancellation request prevents a later completion/retry from overwriting it.

The worker checks cancellation independently of the 30-second heartbeat and propagates its token through the crawler. Session disposal is awaited before normal cancellation acknowledgement, including a browser that finishes starting after cancellation was requested. A company cancellation takes precedence over shutdown recovery and transport retries. Existing Hangfire deliveries are retained as harmless no-ops once the extraction is terminal.

If the worker disappears before acknowledgement, the dispatcher checks up to 100 pending cancellations per pass. It can finalize a request only after acquiring the dedicated taxpayer lock, so it does not take ownership from a worker that still holds that lock. It never opens another SRI session for this recovery. Connection loss releases PostgreSQL locks; cancellation and ownership checks fence subsequent result writes. As with existing crash recovery, a lost process/network connection cannot provide proof of physical browser cleanup on another host.

Unfinished attempts retain their last confirmed diagnostics and end with `extractionCancelled`; already finished attempts (including retry history) remain unchanged; interrupted timing coverage is `incomplete`. Cancelled work with no attempt has no invented timing aggregates. The request records the public API-key ID for audit (`null` in explicit local development mode), never its secret. Events 1420–1422 report requests, worker acknowledgement and recovered cancellations with controlled identifiers and statuses.

## Migration and activation

`20261013035840_ExtractionCancellation` adds nullable request/completion timestamps, the public requesting-key ID, and a partial index for pending cancellations. Historical rows retain `null` cancellation fields. Existing status numbers are unchanged; new values are appended.

Back up PostgreSQL using the existing procedure. Stop **all API and worker replicas**, apply migrations, then restart both on this version. Older workers do not understand the new states; do not mix old and new replicas during activation. PostgreSQL, parser and document volumes remain in place.

```sh
docker compose --env-file .env.jobs stop sri-descarga worker
docker compose --env-file .env.jobs build sri-descarga worker migrate
docker compose --env-file .env.jobs run --rm migrate
docker compose --env-file .env.jobs up -d sri-descarga worker
```

For Warp/native execution, stop API/worker, use the migration command in [the async guide](ASYNC_RECEIVED_DOCUMENTS.md), then restart both. Keep the existing encryption key and `.env.security`/`.env.storage`. The new polling option can be omitted to use its default.

The public [Postman/Apidog collection](postman/sri-crawler.postman_collection.json) includes an optional cancellation request. Import/update it manually; do not run the full collection as a batch. Cancelling an existing completed extraction returns the expected `409`; select a pending or running ID to exercise cancellation.

## Verification

Automated acceptance uses PostgreSQL and Hangfire with simulated extraction runners and real local PDF storage. It covers repeated/concurrent cancellations, company isolation, queued/retrying/running states, cancellation versus startup/completion/shutdown, protected HTTP and sync results, quota retention/release, credential removal, blocked late result writes, recovery after loss of the worker lock, additive migration history, unavailable persistence, and delayed browser creation through substituted interfaces.

No live SRI or AWS acceptance is required for this control-plane change. Actual portal cancellation remains a manual acceptance option after activating the new API/worker. The user's running stack is not upgraded automatically by implementing this feature.

Acceptance evidence: **352 .NET tests and 13 Node tests passed**, including 18 new .NET cases; 11 opt-in browser/remote-storage tests were skipped. Worker compilation and EF model/migration consistency passed. The public collection validates with 30 requests. Temporary PostgreSQL/parser containers were removed after verification. The existing local API/worker/database and private environment files were left unchanged; live portal cancellation has not been exercised.
