# Company workload limits

Received extraction admission is bounded per company, in both `async` and `sync` modes. Authentication and company authorization run first. The request body, existing `XApiKey` header, document processing, storage and download policies are unchanged.

## Configuration

Set these options in `.env.jobs`, identically on every API and worker replica:

| Option | Default | Meaning |
|---|---:|---|
| `ExtractionJobs__MaxPendingPerCompany` | 25 | Unexpired queued, running and retrying extractions per company |
| `ExtractionJobs__MaxAcceptedPerCompany24Hours` | 1000 | Accepted extractions in a rolling 24-hour window |
| `ExtractionJobs__MaxConcurrentPerCompany` | 1 | Concurrent extraction sessions per company, across workers |
| `ExtractionJobs__MaxDispatchedExtractions` | 1 | Outstanding, unexpired extraction reservations published to Hangfire across dispatchers |
| `ExtractionJobs__WorkerCount` | 1 | Hangfire execution threads per worker process |

Positive values are required. Maximums are 100,000 pending, 1,000,000 accepted per 24 hours, and 64 for each concurrency/publication option. Invalid configuration fails startup. Values are operational defaults, not subscription plans or measured server capacity.

Completed, failed and cancelled work releases pending capacity. `cancellationRequested` retains capacity until acknowledged, even after credential expiry. All accepted work, including failed, expired, cancelled and completely reused extractions, counts toward the rolling quota. Status polling, document downloads, worker retries and idempotent repeats do not consume additional acceptance quota. Expired jobs stop occupying pending capacity even before the existing cleanup reconciler updates their terminal state, except cancellation requests awaiting acknowledgement. Existing accepted jobs are not cancelled when a limit is lowered.

The 24-hour window is based on persisted UTC acceptance timestamps; it does not reset at midnight. Keep server clocks synchronized. This is admission control, not HTTP rate limiting, document-count billing or protection against an unlimited number of new companies.

## Responses and idempotency

A new request exceeding a limit returns HTTP `429`, before any new extraction, outbox entry or encrypted credentials are persisted:

```json
{
  "code": "companyPendingLimitReached",
  "message": "The company has reached its pending extraction limit. Retry after an extraction finishes.",
  "limit": 25,
  "current": 25,
  "retryAt": null
}
```

The rolling quota uses `code: "companyUsageLimitReached"`. Its `retryAt` is the earliest UTC instant when sufficient older acceptances leave the window, assuming no further acceptances or configuration changes. A pending-capacity rejection has no reliable completion time, so `retryAt` is `null`. No new HTTP headers are introduced. Laravel should retain the request, use delayed retries with backoff, and continue polling existing extractions; avoid an immediate retry loop.

Repeating an existing `clientRequestId` with matching parameters returns the existing extraction before checking quotas. Different parameters still return `409`. Execution mode, password and download policy retain their existing idempotency behavior. A rejected request has no persisted identity and may be retried with the same ID after capacity becomes available. If persistence is unavailable, acceptance retains its controlled `503` response.

## Fair dispatch and recovery

The dispatcher reserves only available publication slots, leaving the remaining jobs in the persistent outbox. It selects the company whose last dispatch is oldest (companies without a previous dispatch come first), then the oldest accepted request within that company. This rotates turns between companies without preempting a running extraction. One large extraction can still occupy the default single worker until it ends; document-level time slicing is outside this feature.

A PostgreSQL transaction advisory lock serializes dispatch reservations across replicas. Separate company transaction locks serialize quota checks and acceptance. A dedicated worker connection holds a company session lock alongside the existing cross-company taxpayer lock. A busy company slot defers delivery for 30 seconds without opening a browser or consuming an attempt. Connection loss releases the lock; existing heartbeat cancellation and recovery remain in effect. See [PostgreSQL advisory locks](https://www.postgresql.org/docs/current/explicit-locking.html#ADVISORY-LOCKS).

Retries retain their publication reservation during backoff. Deliberately undispatched jobs are excluded from stale Hangfire recovery. Published stale deliveries, interrupted work and expiry continue through the existing reconciler. Publication and crawler persistence use separate transactions: a crash after enqueue but before committing the reservation can produce duplicate deliveries, so worker locks and confirmed-result recovery remain necessary. The publication limit counts extraction reservations, not individual Hangfire delivery records.

For multiple worker processes, configure publication capacity to fit the total available execution capacity, and keep per-company concurrency lower when sharing capacity between customers. Increasing only `WorkerCount` will not bypass the publication limit. Previously published jobs may temporarily exceed a newly lowered publication limit; no new reservations are published until capacity is available. The worker company lock still controls active sessions. During expiry, the existing heartbeat may take up to 30 seconds to stop an old attempt; this setting is not a hard global browser-process limit.

## Apply and verify

The additive `CompanyWorkloadIndexes` migration adds indexes for company/time, company/status/expiry and dispatch reservations. It does not rewrite document records or historical results. Existing acceptances within the window count immediately. Apply it during a maintenance window: regular index creation can briefly block writers.

Back up PostgreSQL using the existing procedure. Add the options above to the private `.env.jobs` if defaults are unsuitable. From the repository root, with the existing private configuration:

```sh
docker compose --env-file .env.jobs stop sri-descarga worker
docker compose --env-file .env.jobs build sri-descarga worker migrate
docker compose --env-file .env.jobs run --rm migrate
docker compose --env-file .env.jobs up -d sri-descarga worker
```

For native execution, stop API/worker, run the documented migration command in [the async guide](ASYNC_RECEIVED_DOCUMENTS.md), then restart both with matching settings. Do not regenerate the credential encryption key. The API and worker must both run the new version; old API replicas could otherwise bypass admission checks.

Use an isolated test database with `MaxPendingPerCompany=1` and no running worker to test rejection safely: accept one valid request (`202`), repeat its `clientRequestId` (`202`, same ID), then submit a different ID (`429`). A second authorized company should still be accepted. To test the usage quota, complete the accepted job and set `MaxAcceptedPerCompany24Hours=1`; another new request still returns `429`, now with the usage code and `retryAt`. Do not fill the live queue solely to exercise the limit.

Events `CompanyAdmissionRejected` (1410, Warning) and `CompanyExecutionDeferred` (1411, Information), together with existing dispatch/attempt events, explain admission and execution waits. Events contain controlled codes/counts and company IDs, never credentials or request bodies.

## Verification scope

Automated tests use isolated PostgreSQL databases and simulated extraction runners, including concurrent admission, rolling-window boundaries, idempotency, company separation, cancellation of a blocked admission, fair dispatch, simultaneous dispatchers, failed publication, expiry, company-lock release and a 1,000-job backlog. The protected HTTP tests exercise both execution modes and verify no extra headers or rejected rows are created. Existing crawler regressions remain required.

This block does not contact SRI/AWS or open browsers. Actual server throughput, customer-specific plans and selective retries remain separate work. Public cancellation is now covered by [its own feature](EXTRACTION_CANCELLATION.md). Deployment requires the migration and coordinated API/worker restart above.

Acceptance evidence: 334 .NET tests and 13 Node tests passed. The real Hangfire/PostgreSQL test completed three simulated extractions in company A/B/A order, with one attempt per job and cleared credentials. Eleven opt-in browser/remote-storage tests were skipped. The worker build and EF model/migration consistency check passed. Temporary PostgreSQL/parser containers were removed after verification; the user’s running API/worker/database were not changed.
