# Postman curl requests

Import the collection and private environment first. The following requests can also be pasted individually into Postman Import > Raw text. Variables use the selected Postman environment. Individually imported curls do not contain the collection scripts that capture credentials and IDs.

For real extraction requests, enter sriPassword locally. Poll status manually and run revocation last.

## 01 - Readiness and credentials

### Health (200)

```bash
curl --location --request GET '{{baseUrl}}/health'
```

### Create company credential (201; captures key)

```bash
curl --location --request POST '{{baseUrl}}/api/admin/companies/{{companyId}}/api-keys' \
  --header 'XApiKey: {{adminApiKey}}' \
  --header 'Content-Type: application/json' \
  --data-raw '{
  "name": "Postman local - company"
}'
```

### List company credentials (200)

```bash
curl --location --request GET '{{baseUrl}}/api/admin/companies/{{companyId}}/api-keys?limit=100' \
  --header 'XApiKey: {{adminApiKey}}'
```

### Create second company credential (201; captures key)

```bash
curl --location --request POST '{{baseUrl}}/api/admin/companies/{{otherCompanyId}}/api-keys' \
  --header 'XApiKey: {{adminApiKey}}' \
  --header 'Content-Type: application/json' \
  --data-raw '{
  "name": "Postman local - isolation"
}'
```

## 02 - Submit real received extractions

### Submit XML asynchronously (202)

```bash
curl --location --request POST '{{baseUrl}}/api/received-documents/query' \
  --header 'XApiKey: {{companyApiKey}}' \
  --header 'Content-Type: application/json' \
  --data-raw '{
  "companyId": "{{companyId}}",
  "user": "{{sriUser}}",
  "additionalUser": "",
  "password": "{{sriPassword}}",
  "day": 0,
  "year": {{year}},
  "month": {{month}},
  "documentType": "invoice",
  "downloadFormat": "xml",
  "downloadPolicy": "reuseValid",
  "executionMode": "async",
  "clientRequestId": "{{clientRequestId}}"
}'
```

### Repeat same XML request (idempotency)

```bash
curl --location --request POST '{{baseUrl}}/api/received-documents/query' \
  --header 'XApiKey: {{companyApiKey}}' \
  --header 'Content-Type: application/json' \
  --data-raw '{
  "companyId": "{{companyId}}",
  "user": "{{sriUser}}",
  "additionalUser": "",
  "password": "{{sriPassword}}",
  "day": 0,
  "year": {{year}},
  "month": {{month}},
  "documentType": "invoice",
  "downloadFormat": "xml",
  "downloadPolicy": "reuseValid",
  "executionMode": "async",
  "clientRequestId": "{{clientRequestId}}"
}'
```

### Submit PDF asynchronously (202)

```bash
curl --location --request POST '{{baseUrl}}/api/received-documents/query' \
  --header 'XApiKey: {{companyApiKey}}' \
  --header 'Content-Type: application/json' \
  --data-raw '{
  "companyId": "{{companyId}}",
  "user": "{{sriUser}}",
  "additionalUser": "",
  "password": "{{sriPassword}}",
  "day": 0,
  "year": {{year}},
  "month": {{month}},
  "documentType": "invoice",
  "downloadFormat": "pdf",
  "downloadPolicy": "reuseValid",
  "executionMode": "async",
  "clientRequestId": "{{clientRequestId}}"
}'
```

### Submit XML with bounded synchronous wait (200/202)

```bash
curl --location --request POST '{{baseUrl}}/api/received-documents/query' \
  --header 'XApiKey: {{companyApiKey}}' \
  --header 'Content-Type: application/json' \
  --data-raw '{
  "companyId": "{{companyId}}",
  "user": "{{sriUser}}",
  "additionalUser": "",
  "password": "{{sriPassword}}",
  "day": 0,
  "year": {{year}},
  "month": {{month}},
  "documentType": "invoice",
  "downloadFormat": "xml",
  "downloadPolicy": "reuseValid",
  "executionMode": "sync",
  "clientRequestId": "{{clientRequestId}}"
}'
```

### Refresh XML from SRI (202)

```bash
curl --location --request POST '{{baseUrl}}/api/received-documents/query' \
  --header 'XApiKey: {{companyApiKey}}' \
  --header 'Content-Type: application/json' \
  --data-raw '{
  "companyId": "{{companyId}}",
  "user": "{{sriUser}}",
  "additionalUser": "",
  "password": "{{sriPassword}}",
  "day": 0,
  "year": {{year}},
  "month": {{month}},
  "documentType": "invoice",
  "downloadFormat": "xml",
  "downloadPolicy": "refresh",
  "executionMode": "async",
  "clientRequestId": "{{clientRequestId}}"
}'
```

## 03 - Progress, results and downloads

### Extraction status and timings (200)

```bash
curl --location --request GET '{{baseUrl}}/api/received-documents/extractions/{{extractionId}}?companyId={{companyId}}' \
  --header 'XApiKey: {{companyApiKey}}'
```

### List document results (captures first downloadable result)

```bash
curl --location --request GET '{{baseUrl}}/api/received-documents/extractions/{{extractionId}}/documents?companyId={{companyId}}&limit=100' \
  --header 'XApiKey: {{companyApiKey}}'
```

### Next document page

```bash
curl --location --request GET '{{baseUrl}}/api/received-documents/extractions/{{extractionId}}/documents?companyId={{companyId}}&limit=100&cursor={{documentsNextCursor}}' \
  --header 'XApiKey: {{companyApiKey}}'
```

### Document detail and JSON (200)

```bash
curl --location --request GET '{{baseUrl}}/api/received-documents/extractions/{{extractionId}}/documents/{{documentId}}?companyId={{companyId}}' \
  --header 'XApiKey: {{companyApiKey}}'
```

### Download registered XML/PDF (200)

```bash
curl --location --request GET '{{baseUrl}}/api/received-documents/extractions/{{extractionId}}/documents/{{documentId}}/file?companyId={{companyId}}' \
  --header 'XApiKey: {{companyApiKey}}'
```

### Extraction errors (200)

```bash
curl --location --request GET '{{baseUrl}}/api/received-documents/extractions/{{extractionId}}/errors?companyId={{companyId}}&limit=100' \
  --header 'XApiKey: {{companyApiKey}}'
```

### Attempt history and timings (200)

```bash
curl --location --request GET '{{baseUrl}}/api/received-documents/extractions/{{extractionId}}/attempts?companyId={{companyId}}&cursor=0&limit=100' \
  --header 'XApiKey: {{companyApiKey}}'
```

## 04 - Security checks (no SRI work)

### Missing API key (401)

```bash
curl --location --request GET '{{baseUrl}}/api/received-documents/extractions/{{extractionId}}?companyId={{companyId}}'
```

### Company key cannot administer credentials (403)

```bash
curl --location --request GET '{{baseUrl}}/api/admin/companies/{{companyId}}/api-keys' \
  --header 'XApiKey: {{companyApiKey}}'
```

### Administrator cannot read extraction (403)

```bash
curl --location --request GET '{{baseUrl}}/api/received-documents/extractions/{{extractionId}}?companyId={{companyId}}' \
  --header 'XApiKey: {{adminApiKey}}'
```

### Foreign company cannot read extraction (404)

```bash
curl --location --request GET '{{baseUrl}}/api/received-documents/extractions/{{extractionId}}?companyId={{companyId}}' \
  --header 'XApiKey: {{otherApiKey}}'
```

### Foreign company ID does not grant access (404)

```bash
curl --location --request GET '{{baseUrl}}/api/received-documents/extractions/{{extractionId}}?companyId={{otherCompanyId}}' \
  --header 'XApiKey: {{otherApiKey}}'
```

### Foreign company cannot download file (404)

```bash
curl --location --request GET '{{baseUrl}}/api/received-documents/extractions/{{extractionId}}/documents/{{documentId}}/file?companyId={{companyId}}' \
  --header 'XApiKey: {{otherApiKey}}'
```

### Cannot create extraction for a foreign company (403)

```bash
curl --location --request POST '{{baseUrl}}/api/received-documents/query' \
  --header 'XApiKey: {{companyApiKey}}' \
  --header 'Content-Type: application/json' \
  --data-raw '{
  "companyId": "{{otherCompanyId}}",
  "user": "1790012345001",
  "password": "not-used-for-security-check",
  "year": 2026,
  "month": 5,
  "day": 0,
  "documentType": "invoice",
  "downloadFormat": "xml"
}'
```

### Static document URL is disabled (404)

```bash
curl --location --request GET '{{baseUrl}}/documents/{{storageKey}}' \
  --header 'XApiKey: {{companyApiKey}}'
```

### Issued routes disabled in required mode (404)

```bash
curl --location --request POST '{{baseUrl}}/api/SriEmitidos/consultar' \
  --header 'XApiKey: {{companyApiKey}}' \
  --header 'Content-Type: application/json' \
  --data-raw '{}'
```

### Swagger disabled in required mode (404)

```bash
curl --location --request GET '{{baseUrl}}/swagger/index.html' \
  --header 'XApiKey: {{companyApiKey}}'
```

## 05 - Revoke the test credential (run last)

### Revoke current company credential (204)

```bash
curl --location --request DELETE '{{baseUrl}}/api/admin/companies/{{companyId}}/api-keys/{{companyKeyId}}' \
  --header 'XApiKey: {{adminApiKey}}'
```

### Revoked company credential is rejected (401)

```bash
curl --location --request GET '{{baseUrl}}/api/received-documents/extractions/{{extractionId}}?companyId={{companyId}}' \
  --header 'XApiKey: {{companyApiKey}}'
```

### Repeat revocation safely (204)

```bash
curl --location --request DELETE '{{baseUrl}}/api/admin/companies/{{companyId}}/api-keys/{{companyKeyId}}' \
  --header 'XApiKey: {{adminApiKey}}'
```
