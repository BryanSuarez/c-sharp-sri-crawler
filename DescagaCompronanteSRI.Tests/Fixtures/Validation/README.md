# Validation fixtures

These synthetic XML templates originate from the official SRI ZIP archives listed in `SriCrawler.Core/Validation/Schemas/sources.json`.
The published examples contain the placeholders `version0` and `codDoc=00`. Test fixtures replace them with the filename version and the correct document code, and remove developer-local `xsi:noNamespaceSchemaLocation` paths. No taxpayer documents or credentials are included.

The invoice 2.0.0 template also fills the two empty Incoterm fields with `A` to satisfy the published minimum length.
