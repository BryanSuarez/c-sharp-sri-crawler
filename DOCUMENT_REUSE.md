# Received document reuse and atomic local publication

Received extractions reuse valid registered XML/PDF files by default. They still log in to SRI, query the requested period and verify every page to discover new documents. Reuse skips the individual SRI download and storage write; it does not eliminate portal navigation.

## Request and results

`POST /api/received-documents/query` accepts optional `downloadPolicy`:

- `reuseValid` is the default for new requests.
- `refresh` downloads again, validates the new content and publishes it only if accepted.
- Explicit `null`, numeric values and unknown strings return HTTP 400.

The policy is persisted separately from the existing idempotency fingerprint. Repeating `clientRequestId` returns the original extraction and does not change its credentials or policy. Use a new UUID or omit it to start an independent refresh. Jobs accepted before the additive migration retain `refresh`; recovery always checks results already confirmed within that job first.

The summary and synchronous result include:

```text
downloadedCount = newlyDownloadedCount + reusedCount
discoveredCount = downloadedCount + failedCount
```

`downloadedCount` retains its compatibility meaning: files confirmed available for the extraction, including reused files. Each document has `acquisitionSource`: `notAcquired`, `downloaded` or `reused`. A fully reused extraction can complete successfully. `duplicateCount` remains the count of repeated portal rows within one extraction. Recovery preserves the acquisition origin of previously confirmed results.

Example request additions:

```json
{
  "downloadPolicy": "refresh",
  "executionMode": "async"
}
```

These fields accompany the existing company, account, credentials, period, type and format fields. Provider/bucket remain server configuration. Both async and sync modes execute the same persistent worker flow.

## Identity and availability checks

The canonical identity remains `(companyId, taxpayerId, direction, documentType, accessKey)`. The same receipt belongs to separate company records when two firms synchronize it. XML/PDF files are resolved separately. The two portal guide variants share the canonical guide identity.

The resolver batches file candidates for the current page, preferring the requested period and then the latest known storage date. Historical unknown dates sort after known dates. The period is not part of document identity: an existing file from another period keeps its original reference, directory and file record. New downloads retain `{companyId}/{taxpayerId}/{year}/{month}/received/{documentType}/{accessKey}.{extension}`.

Only references readable by the configured provider/bucket are considered. Changing destinations does not migrate files or fall back to historical credentials. Unregistered objects are not imported or discovered by scanning the bucket.

Local inspection opens the file without reading its body and checks size/modification time. S3/R2 inspection uses `HeadObject`. Matching validation evidence, validator/limit profile, size and stored revision permits reuse without reading the document body. Remote ETags are opaque revision tokens, not SHA-256 hashes.

Missing evidence, changed revisions, changed size or changed validation profile require bounded reading and current validation of the stored file. Inspection is repeated after reading to prevent binding validation to a revision that changed mid-read. Missing/rejected files allow a fresh SRI download. Permissions/network failures produce per-document `storageVerificationFailed` and do not masquerade as absence or success. A remote 403 is not treated as 404; S3 can conceal absence when bucket-list permissions are missing. SDK retry settings remain unchanged.

This lightweight policy detects observable changes and avoids repeated body transfers. It does not promise cryptographic integrity checking on every query or protection against an external modification that preserves its change token. Only confirmed current validation evidence permits reuse.

## Persistence and interpretation

Each extraction receives its own result with the current portal metadata, page, row and metadata errors. It links to the existing file and compatible conversion instead of copying previous extraction metadata. Completed historical snapshots are preserved.

Compatible JSON requires the same canonical document and XML hash, successful parsing, `taxo-sri-xml-2-json` name, pinned version `1.8.0` and schema version `1`. Confirmed compatible conversions are reused even when a refresh downloads identical XML. Failed attempts do not erase earlier successful conversions. C# `parsedDocument` remains separate and is recovered from a matching stored result or interpreted from stored XML when needed. PDF has no JSON; guides retain unsupported interpretation.

Storage availability, file validation, C# parsing and JSON parsing remain independent. Parser failure can coexist with a successfully reused file. PostgreSQL failures interrupt the worker; it does not continue without confirmed progress. Taxpayer exclusion, attempt ownership, shutdown handling and visibility renewal remain active.

The additive `DocumentReuse` migration adds acquisition source, storage revision/date, validation profile and persisted job policy. Historical downloaded results are classified as original downloads without inventing validation evidence. Old files are validated lazily; old amounts/dates are not reinterpreted.

## Atomic local writes and operation

Local storage writes an exclusive `.sri-write-<guid>.tmp` in the destination directory, finishes and flushes it to disk, closes it and checks cancellation before same-filesystem publication. It never deletes or truncates the previous destination before replacement. Readers permit replacement while retaining their original complete file handle.

Copy/publish failure or cancellation cleans only that operation's temporary file. Abrupt process termination can leave an unpublished temporary; it is ignored during discovery/reuse. With all workers stopped, inspect orphan candidates using:

```sh
rg --files --hidden --no-ignore wwwroot/documents -g '.sri-write-*.tmp'
```

Remove only confirmed orphan temporaries. Do not remove receipt files. This guarantee covers process interruptions on tested local filesystems; it is not a universal hardware/power-loss durability guarantee.

Stop API/worker before applying the migration to an existing installation. From the repository root with existing private environment files:

```sh
docker compose --env-file .env.jobs stop worker sri-descarga
docker compose --env-file .env.jobs run --rm --build migrate
docker compose --env-file .env.jobs up --build -d
docker compose --env-file .env.jobs logs -f sri-descarga worker
```

For native Warp execution, stop both processes, run the explicit migration command in [the asynchronous guide](ASYNC_RECEIVED_DOCUMENTS.md#local-setup-in-warp), and restart them. No new environment settings are required for reuse; the request policy controls refresh.

## Acceptance evidence — October 7, 2026

Automated verification passed 291 .NET tests in the full suite, with the real remote-storage test skipped; the subsequent candidate-ordering regression also passed. Eleven real Node parser tests passed. Coverage includes PostgreSQL, historical migration, tenant/owner/type/format isolation, missing/changed/rejected files, access failures, parser/profile changes, refresh/recovery/idempotency, page-scoped reuse of 1,000 documents, browser fixtures and issued regression.

Four local-storage atomic tests passed on macOS and Linux. Separate actual process-kill checks on both platforms paused a writer mid-copy and terminated it: the old final file remained complete and the orphan temporary remained unpublished.

Real May 2026 invoice acceptance used isolated PostgreSQL, a private parser and local storage. The normal API/worker/storage were not reconfigured.

| Run | Discovered | New downloads | Reused | Pages | Failures | Observed elapsed seconds |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| XML first query | 306 | 306 | 0 | 7 | 0 | 130.308 |
| XML repeated query | 306 | 0 | 306 | 7 | 0 | 66.357 |
| PDF first query | 306 | 306 | 0 | 7 | 0 | 144.763 |
| PDF repeated query | 306 | 0 | 306 | 7 | 0 | 48.232 |

All four results were completed, with zero validation, metadata or conversion failures and zero portal duplicates. Acceptance returned within 0.475 seconds for the first request and within 0.028 seconds for subsequent requests. These local observations include queue/login/navigation time and are not a performance SLA.

All 612 physical files matched their persisted SHA-256 and revision tokens. All 612 reused results retained their original file/conversion links. Only 306 XML conversion records existed after both XML queries and both PDF queries. All four accepted jobs had deleted their encrypted credentials on completion. Assistant-owned acceptance processes, databases, credentials and downloaded files were removed after verification.

Real S3/R2 operations were not repeated in this block. Their adapter metadata/error behavior was verified with substitute clients; live remote acceptance remains pending. No buckets, permissions or deployed resources were modified.
