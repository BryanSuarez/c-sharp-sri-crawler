# Received document metadata and file validation

The request body, asynchronous execution, pagination and storage keys are unchanged. Received files now pass validation before storage. A new downloaded result requires valid content, confirmed storage and a committed PostgreSQL result. Rejected replacements never reach storage and cannot overwrite an earlier valid file.

## Independent results

| Field | Values | Meaning |
| --- | --- | --- |
| `validation.status` | `notChecked`, `valid`, `invalid`, `unsupported`, `failed` | File checks, independent of parsing and storage. |
| `validation.identityStatus` | `notVerified`, `verified`, `mismatch` | Whether the file's key could be identified and matched. |
| `storageStatus` | `notAttempted`, `stored`, `failed` | Confirmed storage outcome. |
| `metadataParseStatus` | `notChecked`, `parsed`, `partial`, `failed` | Conversion of the five table amount/date fields. |
| `parseStatus` / `jsonParseStatus` | Existing parsing states | C# interpretation and Node JSON conversion remain separate. |

Invalid, unsupported or unfinished validation produces a failed download with storage `notAttempted`. Valid files can have failed parsing or incomplete metadata and still be downloaded. Storage failures retain valid validation evidence. Database failures after storage stop the attempt; recovery can overwrite the deterministic key and commit its result.

Summary fields `validationIssueCount` and `metadataIssueCount` count affected results, not individual errors. PostgreSQL calculates them without loading documentary JSON. Validation issues contribute to `failedCount`; metadata and parsing issues do not. `discoveredCount = downloadedCount + failedCount` remains true.

## Amounts and dates

`amount`, `taxes` and `total` are nullable decimals. Missing, invalid, overflowing or ambiguous values produce `null` and an `invalidMetadata` error containing `field`, `pageNumber` and `rowIndex`. Actual zero remains zero. Values are neither rounded nor recalculated, and table metadata is never silently replaced with XML values.

The portal's dot-decimal convention is explicit: `1234.56` and correctly grouped `1,234.56` are accepted. Unambiguous alternate formats such as `1.234,56` and `12,34` are accepted. A lone comma followed by three digits, such as `1,234`, is rejected as ambiguous. `1.234` follows the portal decimal convention. Scientific notation, currency symbols and malformed grouping are rejected. Machine culture has no effect.

The portal places authorization timestamp before issue date; the previous reversed column mapping has been corrected. `issuedAt` and `authorizedAt` retain the original text for their respective fields. `sourceValues` preserves the original amount/date strings. New fields:

- `issuedDate`: ISO date or `null`, parsed exactly from `dd/MM/yyyy`.
- `authorizedAtIso`: ISO timestamp with offset or `null`, parsed from `dd/MM/yyyy HH:mm:ss`, optionally with fractional seconds. Explicit offsets and ISO timestamps are also accepted.
- `authorizationTimeZone`: applied zone identifier, `explicitOffset`, or `null` on failure.

Authorization timestamps without offsets use `DocumentValidation:PortalTimeZone`, default `America/Guayaquil`, rather than the machine zone. Invalid dates and ambiguous/nonexistent local times are rejected. PostgreSQL stores timestamps in UTC (`timestamp with time zone`); response snapshots preserve the interpreted original offset.

## XML

.NET securely reads the authorization response and extracted receipt with DTDs and external entities disabled. It checks root, `codDoc`, requested type, 49 ASCII-digit access key and authorization number when present. The envelope must contain the same receipt as the stored stream. HTML, portal errors and malformed documents are rejected.

Bundled official schemas cover invoices 1.0.0/1.1.0/2.0.0/2.1.0, purchase settlements 1.0.0/1.1.0, credit notes 1.0.0/1.1.0, debit notes 1.0.0, withholdings 1.0.0/2.0.0 and remission guides 1.0.0/1.1.0. Unknown versions are `unsupported`. No schema dependencies are fetched during extraction. Provenance, hashes and the limited XML 1.1 schema-declaration compatibility treatment are in [the schema catalog](SriCrawler.Core/Validation/Schemas/README.md).

Validation does not reserialize the stored receipt or remove signatures. Only the Node conversion copy removes XMLDSig signatures, as before. A valid guide can have unsupported JSON parsing. These checks do not verify cryptographic signatures or certify tax validity.

## PDF

PDF checks include header and closing structure, strict PdfPig parsing (`UseLenientParsing = false`), a nonempty page tree and reading every page. Truncated, damaged and password-protected files are rejected. Image-only pages do not require text.

An unambiguous 49-digit key following the RIDE access-key/authorization-number label is compared with the expected key. A confirmed mismatch rejects the file; absent or ambiguous evidence remains `notVerified`. Unrelated numbers are not identity evidence. This is structural validation, not PDF/A certification.

Both validators return their version, performed checks, size, SHA-256, time and controlled errors. Streams are rewound after reading, released by the coordinator, and cancellation propagates between stages and PDF pages.

## Configuration and migration

Set server options in `.env.jobs` during development or inject environment variables; no new request fields or headers are required:

```dotenv
DocumentValidation__PortalTimeZone=America/Guayaquil
DocumentValidation__MaxXmlBytes=20971520
DocumentValidation__MaxPdfBytes=20971520
DocumentValidation__MaxPdfPages=1000
```

Options are validated at startup. XML has a maximum supported ceiling of 20 MiB to match Node's decoded XML quota. Node's HTTP body limit separately accounts for JSON escaping overhead. Download strategies enforce byte limits while reading, without adding nested transport retry budgets.

The additive `DocumentValidationAndMetadata` migration adds numeric/date/timestamptz columns, source values and per-result validation evidence. Current file records retain their hash/evidence. Historical metadata and validation remain `notChecked`; historical download status is preserved without retrospectively certifying files. Amounts lacking original text are not reinterpreted.

Rebuild images and apply migrations before starting the updated API/worker:

```sh
docker compose --env-file .env.jobs build migrate sri-descarga worker parser
docker compose --env-file .env.jobs run --rm migrate
docker compose --env-file .env.jobs up -d
```

Native setup/migration commands remain in [the asynchronous extraction guide](ASYNC_RECEIVED_DOCUMENTS.md). Back up the database before migrating an existing environment.

Active extraction recovery revalidates saved files lacking compatible current-validator evidence. Rejected or missing files are downloaded and checked again. Internal XML reprocessing validates stored content before conversion without modifying finalized results. Failed operations preserve earlier valid conversions/file records.

## Verification

Automated tests cover cultures, ambiguity/overflow, leap years/time zones, every bundled schema, envelopes/signatures, unsafe XML, malformed/oversized files, strict PDF parsing including images/encryption, bounded reads, cancellation, rejected replacement storage exclusion, migration/history/recovery, HTTP contracts and multi-page XML/PDF browser scenarios. PostgreSQL/Hangfire and Node checks use isolated infrastructure. Browser fixtures do not contact SRI. Real SRI acceptance is reported separately.

### Real acceptance, October 7, 2026

Two isolated asynchronous invoice extractions for May 2026 completed against SRI: XML extraction `a0bdd0ec-dc04-4641-b243-e5f9a3136a7e` and PDF extraction `e095ceef-6bd8-45f8-917d-a9496ecc9182`. Each traversed seven pages, matched the portal's 306-document total and finished with 306 downloads, zero validation/metadata/conversion issues and no duplicate results.

All 612 local files were compared byte-for-byte through their persisted size and SHA-256 evidence. All 612 extraction results contained typed dates, original values and successful validation/storage/metadata states in PostgreSQL. XML detail returned documentary JSON; PDF detail returned null JSON. This run exposed and corrected the previous reversed authorization/issue-date column mapping.

The acceptance environment used its own database, parser, API/worker and temporary local storage; the user's running Compose stack and S3 bucket were unchanged. Actual S3/R2 writes were not repeated in this block; their adapter regressions passed and the opt-in remote storage test remained skipped.

Final automated verification: 269 .NET tests and 11 Node tests passed; the optional remote-storage test was skipped. The final validator also independently rechecked all 612 real files with zero failures. A populated database upgraded from the previous migration retained original response JSON and null new typed fields, with validation/metadata `notChecked`. Terminal test jobs cleared their encrypted credentials. Temporary processes, database and document copies were removed after acceptance; the extraction IDs above are isolated test evidence, not records in the user’s running API.
