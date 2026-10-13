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
- [ ] **11. Retry budgets, timeouts and cancellation — partially complete.** Bounded transport/navigation/job retries, execution deadlines, shutdown cancellation and resource release are implemented. Explicit company cancellation is implemented with durable requests, cooperative cleanup and recovery. Finer classification of session/CAPTCHA/transient failures remains pending.
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
- [x] **2. Pending-job and usage limits per company.** Transactional admission limits, rolling 24-hour accepted-work quota, typed 429 responses, idempotency without extra charges, fair bounded publication and company execution locks across workers. See [workload limits and activation](COMPANY_WORKLOAD_LIMITS.md). Verified in isolated infrastructure and activated locally with a database backup and coordinated restart.
- [x] **3a. Public extraction cancellation.** Company-authorized cancellation of queued/retrying/active work, retained confirmed files/results, independent request/completion states, idempotency, restart reconciliation and capacity held through cleanup. See [cancellation and activation](EXTRACTION_CANCELLATION.md). Verified in isolated infrastructure and activated locally; cancellation, idempotency and permissions passed against the running API.
- [x] **3b. Selected-failure retries.** Asynchronous child extractions for selected failed result IDs, independent download/parsing modes, immutable source history, idempotency, company limits and cancellation. Stored-XML parsing never contacts SRI. See [retry contract and activation](SELECTED_FAILURE_RETRIES.md). Verified in isolated infrastructure and activated locally after a private backup, migration and API/worker rebuild.
- [ ] **4. Document catalog independent of extraction history.** Query documents by taxpayer/period/type for Laravel; the secured per-result file endpoint is already available.
- [ ] **5. Reduce measured waits/navigation.** Complete original item 9 using the recorded timings.
- [ ] **6. Beta operations.** Validate backup/restore, alerts, capacity and renewed remote-storage acceptance.
- [ ] **Commercial isolation for issued documents.** Keep the existing contract local until company ownership is implemented.

## Recommended next commercial item

**Commercial item 4: document catalog independent of extraction history.** Let Laravel query documents by company, taxpayer, period and type without selecting an extraction first. Keep this separate from the completed access, workload, cancellation and selected-retry features.

- [ ] **Repair intermittent profile/business-name reads.** Missing profile data is now diagnosed and timed; fixing its selectors/navigation was explicitly deferred from the observability block.

Nightly scheduling, webhooks and frontend integration remain separate blocks. Unauthenticated testing requires explicit local-development mode and the Development environment.

## Verification evidence

The metadata/file-validation block passed 269 .NET tests and 11 Node tests. Real May 2026 acceptance completed 306 XML and 306 PDF documents across seven pages per extraction; all 612 local files matched their persisted hashes and typed results. The final validator independently rechecked those 612 files without failures. Temporary acceptance infrastructure was removed. Real S3/R2 writes were not repeated in this block; the opt-in remote-storage test was skipped. See [the validation record](RECEIVED_DOCUMENT_VALIDATION.md) and [the asynchronous extraction guide](ASYNC_RECEIVED_DOCUMENTS.md).

The reuse block passed the full 291-test .NET suite plus the candidate-ordering regression and 11 Node tests. Real consecutive queries discovered 306 XML and 306 PDF each, then reused all 612 with zero new downloads or failures. All file hashes/revisions and result links were checked. Atomic interruption checks passed on macOS/Linux. Live S3/R2 acceptance remains pending; adapter substitutions passed. See [the reuse record](DOCUMENT_REUSE.md).

The observability block passed 305 .NET tests and 13 Node tests. Real XML/PDF acceptance downloaded 612 files and reused all 612 in subsequent queries. All four attempts had complete timing coverage; 16 stage durations matched logs/API/database and 306 parser calls were correlated across worker/Node. Sensitive log checks passed. See [the observability record](EXTRACTION_OBSERVABILITY.md).

The company-access block passed 315 .NET tests and 13 Node tests. Eleven opt-in tests were skipped (ten browser scenarios and live remote storage). Acceptance used isolated PostgreSQL and HTTP hosts with two companies sharing a taxpayer, protected synthetic file downloads, rotation/revocation, real database connection failure and disconnect cleanup. Remote download adapters were exercised with substituted S3/R2 clients; no SRI/AWS operations were performed. Private configuration generation, Compose validation and migration compatibility passed. See [company access](COMPANY_API_ACCESS.md).

Subsequent local-stack acceptance applied the additive migration after a private database backup. Health, authorized status/documents and rejection checks passed against the running containers. A registered XML was streamed successfully from S3 after confirming AWS account `991077017871`; no object was uploaded or deleted. Apidog imported 29 requests with local environment values, and live health/status/documents returned 200 while missing credentials, the administrator document request and foreign-company access returned 401, 403 and 404 respectively. New SRI work was not started during this acceptance. Live R2 acceptance remains pending.

The workload-limits block passed 334 .NET tests and 13 Node tests; 11 opt-in browser/remote-storage scenarios were skipped. Nineteen new cases cover quota/concurrency bounds, real PostgreSQL admission races and rolling windows, idempotency, protected HTTP 429 responses, dispatcher fairness/recovery and a 1,000-job backlog. Real Hangfire/PostgreSQL acceptance completed three jobs in company A/B/A order with one attempt each and cleared credentials. Existing security, parser integration, pagination, validation, reuse, storage and issued regressions passed. The new migration was applied only to isolated test databases; no browser, SRI, AWS or running local-stack operation was required. See [workload limits](COMPANY_WORKLOAD_LIMITS.md).

The cancellation block passed **352 .NET tests and 13 Node tests** (18 new .NET cases). Eleven opt-in browser/remote-storage scenarios remained skipped. Isolated PostgreSQL/Hangfire covered concurrent/idempotent requests, startup/completion/shutdown races, preservation of retry history, cancellation recovery after lock loss, guarded result commits, company HTTP permissions, sync results, quotas, cleared credentials, real local PDF preservation, additive migration compatibility, and unavailable persistence with safe logs. Delayed browser startup/cleanup used substituted interfaces, with no browser launch. The worker build, EF model/migration check and public 30-request collection validation passed. No SRI/AWS call or live-stack upgrade was performed. Selective retries remain pending. See [cancellation](EXTRACTION_CANCELLATION.md).

Local activation after cancellation applied both workload and cancellation migrations after a private backup. API/worker/parser/PostgreSQL were healthy; cancellation and idempotency passed through the running API with no SRI calls. All 663 existing documents and their recorded content were preserved. This activation preceded the selected-retry implementation.

The selected-retry block passed **367 .NET tests and 13 Node tests**, including 15 new .NET cases. Eleven opt-in browser/live-storage tests remained skipped. New acceptance covers PostgreSQL admission races, protected HTTP and idempotency, limits/cancellation, Hangfire delivery, selected XML/PDF traversal, recovery without a second download, real stored-XML validation, parsing failure/success and conversion reuse, preserved source history/bytes, migration compatibility, expiry and controlled persistence failures. The public collection contains 32 requests. No browser, SRI, AWS or running-stack upgrade was performed. See [selected retries](SELECTED_FAILURE_RETRIES.md).

Selected-retry local activation applied `20261013044123_SelectedFailureRetries` after a private PostgreSQL backup. The running API accepted/replayed a synthetic download retry, enforced 401/403/404 and invalid-selection checks, and cancelled it before any attempt. Hangfire executed a credential-free synthetic parsing retry and reported its unavailable file correctly. Synthetic crawler records were removed; all nine historical table counts/content fingerprints matched the pre-activation baseline. API, worker, parser and PostgreSQL were healthy. No browser, SRI or AWS operation occurred. Private evidence and the backup are under `.secrets/backups/`.

Apidog desktop acceptance executed the new endpoint through a temporary HTTP request using the existing private local environment. Invalid mode returned 400; selecting a successful download for redownload returned 409; parsing acceptance and its idempotent repeat returned 202 with the same child ID. Status/documents returned 200, with one completed attempt and the deliberately absent synthetic file reported as `storedRetryFileUnavailable`. Missing, administrative and foreign-company keys returned 401, 403 and 404; restoring the company key returned 200. Health returned 200/ready. Synthetic crawler records were removed and historical table fingerprints/counts remained unchanged. No SRI/AWS operation or browser launch occurred; the UI's older imported collection was not replaced.
