# Local Postman and Apidog testing

The service runs at `http://localhost:8080` in required mode. Import `sri-crawler.postman_collection.json` and the private environment `.secrets/sri-crawler.local.postman_environment.json`; select **SRI Crawler - Private Local**. The public environment example contains placeholders only.

Apidog can import the same collection using **Settings → Import Data → Postman**. Keep credentials and taxpayer/result values in the environment's **Local value** column, with shared values empty. The collection uses structured Postman URLs with explicit query parameters so `companyId`, `cursor` and `limit` survive import. The prepared local Apidog project contains all 29 requests; health, authenticated status/documents and 401/403/404 security checks passed. Complete `sriPassword` locally before starting a new extraction.

The private file contains live administrator and company credentials. Keep it in your private workspace and do not share/export it publicly. The SRI password is intentionally empty; set `sriPassword` locally before submitting real queries. The taxpayer and May 2026 period are prefilled. No real SRI extraction was started while preparing this collection.

The private environment also selects an existing completed extraction and downloadable XML, so you can immediately exercise status, documents, detail, errors, attempts and file download. For a new query, run **Submit XML asynchronously** or **Submit PDF asynchronously**, then poll **Extraction status and timings** every five seconds until terminal. Status `completed` concerns job execution; inspect extractionStatus, pagination and failedCount as well.

The collection captures new keys, extraction IDs and the first downloadable result automatically. A new submission clears the old document ID. The idempotency request must follow the XML submission with unchanged parameters. PDF/sync/refresh requests start independent work and replace the selected extraction. An empty or still running extraction may have no file yet; list results again later.

The readiness/administration folder is optional when using the prepared private environment: both company keys already exist. Sending a creation request generates a new credential and replaces the environment value; it does not revoke older keys. Only the **Revoke current company credential** request revokes the selected key.

Expected security statuses: missing/revoked key 401; wrong credential role or foreign-company creation 403; foreign resources, static paths and disabled issued/Swagger routes 404 when using a valid credential. The security-check folder does not start SRI work. Run revocation last; to continue afterward create a new company credential.

Use Postman **Send and Download** on the file request to save XML/PDF. The document ID is an extraction-result ID. The next-page request requires a populated documentsNextCursor. Do not run the entire collection automatically: async work will not necessarily finish before later requests, and the extraction folder contains multiple real submissions.

[CURLS.md](CURLS.md) includes all requests for individual Raw text imports. See [company access](../COMPANY_API_ACCESS.md) for credential administration and [Postman variables](https://learning.postman.com/docs/use/send-requests/variables/variables/) for environment values.

Follow service logs from Warp:

```sh
docker compose --env-file .env.jobs logs -f --no-log-prefix sri-descarga worker
```

The migration was applied after stopping API/worker and creating a private database backup under `.secrets/backups/`. PostgreSQL and the documents volume were preserved. Health/authentication/ownership probes passed against the running containers. An existing protected XML was read successfully after verifying AWS account `991077017871`; no upload, deletion or bucket change was performed.

New extraction submissions can return **429** when the company pending or rolling 24-hour quota is full. Read `code`, `limit`, `current` and `retryAt`; a rejection creates no extraction. Continue polling accepted work, and retry with backoff. Existing matching `clientRequestId` values return the original extraction without consuming quota. See [safe isolated limit testing](../COMPANY_WORKLOAD_LIMITS.md); do not run the whole collection to fill the live queue.
