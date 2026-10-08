# Official SRI schemas

The 13 document schemas are unmodified downloads from https://www.sri.gob.ec/facturacion-electronica. `sources.json` records the original URLs and SHA-256 checksums. XMLDSig is supplied by W3C; its DTD was removed and its namespace entity expanded, preserving schema declarations and the upstream license notice.

The resources are embedded and imports are satisfied exclusively by the bundled XMLDSig schema. Runtime network resolution and schema locations supplied by downloaded XML are disabled.

Two SRI schemas (`GuiaRemision_V1.1.0.xsd`, `NotaCredito_V1.1.0.xsd`) declare XML 1.1. `SriSchemaCatalog` changes only that trusted declaration to XML 1.0 in memory for the .NET XML reader. Their XSD declarations remain unchanged. This compatibility preparation never applies to downloaded documents.

Validation covers structure and identity, not cryptographic verification or accounting reconciliation. Future document versions must be explicitly added with official provenance and tests.
