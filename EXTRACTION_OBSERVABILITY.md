# Extraction logs and timing diagnostics

Received extractions record bounded diagnostics per attempt. Measurements describe elapsed operation time, including I/O and existing retry waits; they are not CPU measurements. No document content or per-document timing history is persisted by this feature.

## API

The status endpoint and completed synchronous response include `timings`:

- `elapsedMs`: wall time from acceptance to completion, or the current status request.
- `initialQueueMs`: wall time before the first attempt, or confirmed completion/current time when no attempt started.
- `recordedActiveMs`: sum of confirmed monotonic attempt durations. It is `null` when no measurements exist.
- `coverage`: `notAvailable`, `recording`, `complete` or `incomplete`. Coverage describes measurements, independently of extraction success.
- `updatedAt`: timestamp of the newest confirmed snapshot.
- `stages` and `operations`: fixed-catalog aggregates, or `null` for uninstrumented history.

Each aggregate includes `name`, `count`, `totalDurationMs`, `averageDurationMs`, `maxDurationMs`, outcome counts and `inProgressDurationMs`. Completed observations determine averages/maxima. In-progress duration is captured at the checkpoint, not extrapolated by the API. Missing operations were not measured/executed; a reused file does not acquire imaginary download/write durations.

Stages contain their operations. Some operations contain other operations (for example authentication/profile and query/table reading). Do not add stages and operations, or parent and child operations, to estimate elapsed time. Waiting between attempts is outside recorded active time and can be inspected through attempt dates. Wall-clock changes can affect date-derived global durations; active measurements use a monotonic clock.

```sh
curl 'http://localhost:8080/api/received-documents/extractions/EXTRACTION_ID?companyId=acme-sa'
curl 'http://localhost:8080/api/received-documents/extractions/EXTRACTION_ID/attempts?companyId=acme-sa&limit=100'
```

Attempt history contains `attemptId`, `number`, dates, `errorCode`, `timings` and measurement `coverage`. `nextCursor` is the last returned attempt number; pass it as `cursor` for the next page. Default limit is 100, maximum 500. The existing company ownership check applies; nonexistent/foreign extractions return 404. No additional headers are required.

Historical attempts keep `notAvailable` coverage and absent timings. Their document results are not rewritten, and stage timings are not reconstructed from old dates. The JSON snapshot carries a schema version for later evolution.

## Console logs

API and worker use JSON console output with UTC timestamps and scopes. Stable event IDs/names identify acceptance, idempotent matches, dispatch, attempt start, busy taxpayer, stage changes, page progress, transport/job retries, interruption and completion. Stage transitions include the completed stage duration; final events include recorded active duration.

Scopes identify extraction, company and attempt, adding format/page/row where available. Information logs report lifecycle/stages/pages, Warning reports recoverable problems, Error reports terminal worker failures, and Debug reports individual operations/documents. A profile read with missing business name is explicitly reported; this feature does not repair profile navigation or selectors.

Logs exclude passwords and their length, additional users, XML, document JSON, cookies, form bodies, connection strings and authorization URL parameters/fragments. External exception messages are replaced by controlled codes and exception types. The received pipeline identifies rows without logging access keys. Automatic framework SQL/HTTP-request diagnostics that could expose parameters are suppressed; application diagnostic events remain available. Issued-specific legacy logging is outside this block.

Follow the running processes from Warp:

```sh
docker compose --env-file .env.jobs logs -f --no-log-prefix sri-descarga worker
```

With `jq` available, extract events for one extraction:

```sh
docker compose --env-file .env.jobs logs --no-log-prefix worker \
  | jq -R 'fromjson? | select(any(.Scopes[]?; .extractionId? == "EXTRACTION_ID"))'
```

For a native worker, enable document diagnostics before starting it:

```sh
export Logging__LogLevel__DescagaCompronanteSRI=Debug
dotnet run --project SriCrawler.Worker
```

Docker passes the same setting through `.env.jobs` (`Logging__LogLevel__DescagaCompronanteSRI=Debug`); recreate the worker to apply it. Native environment variables take effect without changing private files. Return the setting to `Information` after diagnosis to reduce volume. Required configuration remains documented in [the async guide](ASYNC_RECEIVED_DOCUMENTS.md).

The private Node parser records controlled JSON diagnostics, with the existing body `requestId` matching the worker's parser-request scope. Worker timing includes HTTP/retries; Node timing describes the conversion itself. Successful per-document Node logs require `LOG_LEVEL=debug`; warnings are logged by default. Configure that environment value only for the parser process when needed. Health requests and request/response bodies are not automatically logged.

## Persistence and recovery

The additive `ExtractionDiagnostics` migration adds nullable snapshot/version/update time and a sequence to attempts. Snapshots contain absolute totals. Older/duplicate writes are ignored, and only the currently owned running attempt can update its snapshot. Concurrent heartbeat/progress writes cannot accumulate the same measurements twice.

Snapshots are saved on stage changes, existing progress checkpoints, 30-second heartbeat and normal/interrupted closure. Operations update only the recorder in memory; they do not insert a database row or issue a diagnostic write individually. Memory/snapshot size is bounded by stages/operation catalog, not document count.

Recovery preserves the last confirmed snapshot and marks unfinished attempts incomplete. A recovery timestamp on an abruptly interrupted attempt is the time recovery was observed, not the exact time the former process died. No offline interval is added to active durations. A new attempt starts its own recorder and may reuse confirmed document results as before.

Logging-sink failures do not change the document result. PostgreSQL progress failures retain the existing interruption policy. Diagnostics never cause documents to be downloaded again just to reconstruct measurements.

Stop API/worker, apply the explicit migration, then rebuild/restart:

```sh
docker compose --env-file .env.jobs stop worker sri-descarga
docker compose --env-file .env.jobs run --rm --build migrate
docker compose --env-file .env.jobs up --build -d
```

For native execution, stop API/worker and use the migration command in [the Warp guide](ASYNC_RECEIVED_DOCUMENTS.md#local-setup-in-warp). No new mandatory secrets/options are required.

## Verification

The final .NET suite passed 305 tests, with the opt-in remote-storage test skipped; all 13 Node tests passed. Automated coverage includes monotonic timing with wall-clock changes, nested operations, errors/cancellation, failing log sinks, bounded 5,000-operation snapshots, absolute/duplicate/stale/concurrent database writes, ownership, interrupted coverage, historical migration, API cursors/company isolation and Node request correlation/redaction. Existing pagination, storage, reuse and issued suites are retained.

Real acceptance on October 7, 2026 used an isolated database, parser and local document directory:

| Query | Discovered | New | Reused | Pages | Failures | Recorded active seconds |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| XML first | 306 | 306 | 0 | 7 | 0 | 122.172 |
| XML repeated | 306 | 0 | 306 | 7 | 0 | 44.379 |
| PDF first | 306 | 306 | 0 | 7 | 0 | 148.113 |
| PDF repeated | 306 | 0 | 306 | 7 | 0 | 44.424 |

All four attempts/extractions completed with complete measurement coverage. Repeated queries recorded zero download/write/JSON-parser operations. PostgreSQL contained 306 documents, 612 files, 306 conversions and 1,224 extraction results; all four encrypted credentials were removed on completion.

The 306 acceptance parser request IDs matched worker/Node logs. All 16 stage durations matched console events, API and stored snapshots. Captured logs were JSON and the sensitive-value/content checks found no leaks. Automated parser traffic was distinguished from acceptance traffic.

For the repeated queries, portal access accounted for approximately 16–17 seconds and login approximately 11–12 seconds of the 44-second recorded active duration. These observations support investigating existing navigation/modal waits next; they are not an SLA. No waits/navigation were changed in this block.

Remote-storage live acceptance is separate; this diagnostics block did not modify buckets, permissions or resources.

Assistant-owned acceptance processes, test containers/databases, transient credentials, logs and downloaded files were removed after verification. Existing local services and private environment files were preserved.
