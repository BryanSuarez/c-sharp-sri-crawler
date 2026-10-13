# Received document improvement checklist

This checklist follows the original 14-item received-document review. Checked entries are complete within the received-document scope. Unchecked entries describe the remaining work, including items already partially implemented. Issued-document behavior is unchanged.

## Original review

- [x] **1. Complete pagination.** Verified page transitions, portal totals, duplicate detection, page limits and incomplete traversal reporting.
- [x] **2. Wait for the current SRI response.** Correlate query/navigation responses with table updates before taking a page snapshot.
- [x] **3. Reliable amounts and dates.** Nullable decimals, explicit number formats, typed dates, configured portal time zone, raw values and field errors. Corrected authorization/issue-date column order.
- [x] **4. Validate XML/PDF before success.** Secure XML, bundled official XSDs, identity checks, strict PDF structure/page reading, size limits and independent validation/storage/parsing states. Unknown XML versions are rejected before storage under the latest agreed policy.
- [ ] **5. Typed per-document results and targeted retries — partially complete.** Typed results, counters, error history and completion states are implemented. Retrying only selected failures after a finished extraction remains pending.
- [x] **6. Separate responsibilities.** Session adapter, Page Object, download strategies, metadata parser, file validators, document parser and interchangeable storage behind interfaces.
- [x] **7. English names and enums for received documents.** English API and received models/flow; explicit enums replace the previous Spanish request aliases and download boolean. Shared legacy login/issued logs are tracked separately below.
- [x] **8. Reduce repeated browser reads.** One atomic row/state snapshot per page; JSF references refresh after transitions.
- [ ] **9. Remove unnecessary waits/navigation — pending.** Avoid waiting for absent modals and trying an alternate portal URL after access is already established. Measure changes against real and simulated portal flows.
- [x] **10. Reuse valid files across extractions and publish local files atomically.** Tenant/owner/type/format isolation, metadata inspection, compatible validation/conversion reuse, explicit refresh, acquisition counters and same-job recovery. Temporary local publication preserves prior files across failures and interruption. See [reuse acceptance](DOCUMENT_REUSE.md).
- [ ] **11. Retry budgets, timeouts and cancellation — partially complete.** Bounded transport/navigation/job retries, execution deadlines, shutdown cancellation and resource release are implemented. Explicit public cancellation and finer classification of session/CAPTCHA/transient failures remain pending.
- [x] **12. Concurrency limits and isolated sessions.** Configurable workers, one worker by default, dedicated session per attempt and PostgreSQL taxpayer exclusion across companies. Browser-process reuse is an optional later optimization.
- [x] **13. Consistent logs and timing measurements.** JSON logs with controlled fields, stage/operation durations, bounded persistent per-attempt snapshots, status/sync timing summaries and scoped cursor-paginated attempt history. Profile availability is diagnosed; the separate profile-reading repair below remains pending. See [observability](EXTRACTION_OBSERVABILITY.md).
- [x] **14. Failure, volume and regression tests.** Pagination, duplicates, malformed content, cultures, partial failures, interruption/recovery, streaming volume, storage adapters, HTTP and issued regressions. Real received XML/PDF acceptance exceeds 50 documents; future portal changes still require renewed acceptance.

## Additional completed work

- [x] Local/S3/R2 storage adapters and deterministic company/taxpayer/year/month/direction/type/access-key paths.
- [x] Persistent asynchronous API, separate Hangfire worker, progress polling and optional bounded synchronous wait.
- [x] PostgreSQL documents/files, versioned JSON conversions and incremental extraction results.
- [x] Private Node parser using the pinned `taxo-sri-xml-2-json` dependency; original signed XML retained.
- [x] Encrypted transient SRI credentials, idempotent acceptance, recoverable outbox and restart recovery.
- [x] Docker Compose processes, health checks, migration workflow and local setup documentation.

## Commercial beta checklist

- [x] **1. Secure company API access.** Per-company API keys, separate private administrative API, hashed secrets, expiry/revocation, tenant authorization, protected Local/S3/R2 file streaming and removal of static document access. Issued routes are excluded from required mode. The local stack and Postman/Apidog collection are configured and verified. See [company access](COMPANY_API_ACCESS.md) and [manual API testing](postman/README.md).
- [ ] **2. Pending-job and usage limits per company.** Bound persistent queue growth and prevent one customer from monopolizing worker capacity.
- [ ] **3. Public cancellation and selected-failure retries.** Complete original items 5 and 11 without repeating successful work.
- [ ] **4. Document catalog independent of extraction history.** Query documents by taxpayer/period/type for Laravel; the secured per-result file endpoint is already available.
- [ ] **5. Reduce measured waits/navigation.** Complete original item 9 using the recorded timings.
- [ ] **6. Beta operations.** Validate backup/restore, alerts, capacity and renewed remote-storage acceptance.
- [ ] **Commercial isolation for issued documents.** Keep the existing contract local until company ownership is implemented.

## Recommended next commercial item

**Commercial item 2: pending-job and usage limits per company.** Bound admitted work using persistent counts and protect worker capacity. Authentication is now implemented; admission limits remain separate from HTTP request throttling.

Then address commercial item 3 (cancellation and selected-failure retries), followed by item 4 (the document catalog for Laravel). Each remains a separate feature with its own acceptance checks; none is included in the company-access delivery.

- [ ] **Repair intermittent profile/business-name reads.** Missing profile data is now diagnosed and timed; fixing its selectors/navigation was explicitly deferred from the observability block.

Nightly scheduling, webhooks and frontend integration remain separate blocks. Unauthenticated testing requires explicit local-development mode and the Development environment.

## Verification evidence

The metadata/file-validation block passed 269 .NET tests and 11 Node tests. Real May 2026 acceptance completed 306 XML and 306 PDF documents across seven pages per extraction; all 612 local files matched their persisted hashes and typed results. The final validator independently rechecked those 612 files without failures. Temporary acceptance infrastructure was removed. Real S3/R2 writes were not repeated in this block; the opt-in remote-storage test was skipped. See [the validation record](RECEIVED_DOCUMENT_VALIDATION.md) and [the asynchronous extraction guide](ASYNC_RECEIVED_DOCUMENTS.md).

The reuse block passed the full 291-test .NET suite plus the candidate-ordering regression and 11 Node tests. Real consecutive queries discovered 306 XML and 306 PDF each, then reused all 612 with zero new downloads or failures. All file hashes/revisions and result links were checked. Atomic interruption checks passed on macOS/Linux. Live S3/R2 acceptance remains pending; adapter substitutions passed. See [the reuse record](DOCUMENT_REUSE.md).

The observability block passed 305 .NET tests and 13 Node tests. Real XML/PDF acceptance downloaded 612 files and reused all 612 in subsequent queries. All four attempts had complete timing coverage; 16 stage durations matched logs/API/database and 306 parser calls were correlated across worker/Node. Sensitive log checks passed. See [the observability record](EXTRACTION_OBSERVABILITY.md).

The company-access block passed 315 .NET tests and 13 Node tests. Eleven opt-in tests were skipped (ten browser scenarios and live remote storage). Acceptance used isolated PostgreSQL and HTTP hosts with two companies sharing a taxpayer, protected synthetic file downloads, rotation/revocation, real database connection failure and disconnect cleanup. Remote download adapters were exercised with substituted S3/R2 clients; no SRI/AWS operations were performed. Private configuration generation, Compose validation and migration compatibility passed. See [company access](COMPANY_API_ACCESS.md).

Subsequent local-stack acceptance applied the additive migration after a private database backup. Health, authorized status/documents and rejection checks passed against the running containers. A registered XML was streamed successfully from S3 after confirming AWS account `991077017871`; no object was uploaded or deleted. Apidog imported 29 requests with local environment values, and live health/status/documents returned 200 while missing credentials, the administrator document request and foreign-company access returned 401, 403 and 404 respectively. New SRI work was not started during this acceptance. Live R2 acceptance remains pending.
