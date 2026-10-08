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
- [ ] **13. Consistent logs and timing measurements — partially complete.** Extraction scopes, progress, page/attempt counts and heartbeat exist. Shared login/portal still use console messages, complete duration metrics are missing, and empty profile/business-name reads need correction.
- [x] **14. Failure, volume and regression tests.** Pagination, duplicates, malformed content, cultures, partial failures, interruption/recovery, streaming volume, storage adapters, HTTP and issued regressions. Real received XML/PDF acceptance exceeds 50 documents; future portal changes still require renewed acceptance.

## Additional completed work

- [x] Local/S3/R2 storage adapters and deterministic company/taxpayer/year/month/direction/type/access-key paths.
- [x] Persistent asynchronous API, separate Hangfire worker, progress polling and optional bounded synchronous wait.
- [x] PostgreSQL documents/files, versioned JSON conversions and incremental extraction results.
- [x] Private Node parser using the pinned `taxo-sri-xml-2-json` dependency; original signed XML retained.
- [x] Encrypted transient SRI credentials, idempotent acceptance, recoverable outbox and restart recovery.
- [x] Docker Compose processes, health checks, migration workflow and local setup documentation.

## Recommended next item

**Item 13: consistent logs and timing measurements.** Add stage/download/validation/storage/parser duration measurements and replace the remaining shared console messages with structured logging. Correct empty profile/business-name reads. Use the resulting evidence to address item 9's remaining waits/navigation.

Authentication, nightly scheduling, webhooks and frontend integration remain separate blocks; authentication is deliberately deferred during local development.

## Verification evidence

The metadata/file-validation block passed 269 .NET tests and 11 Node tests. Real May 2026 acceptance completed 306 XML and 306 PDF documents across seven pages per extraction; all 612 local files matched their persisted hashes and typed results. The final validator independently rechecked those 612 files without failures. Temporary acceptance infrastructure was removed. Real S3/R2 writes were not repeated in this block; the opt-in remote-storage test was skipped. See [the validation record](RECEIVED_DOCUMENT_VALIDATION.md) and [the asynchronous extraction guide](ASYNC_RECEIVED_DOCUMENTS.md).

The reuse block passed the full 291-test .NET suite plus the candidate-ordering regression and 11 Node tests. Real consecutive queries discovered 306 XML and 306 PDF each, then reused all 612 with zero new downloads or failures. All file hashes/revisions and result links were checked. Atomic interruption checks passed on macOS/Linux. Live S3/R2 acceptance remains pending; adapter substitutions passed. See [the reuse record](DOCUMENT_REUSE.md).
