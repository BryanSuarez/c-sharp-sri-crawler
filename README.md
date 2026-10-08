# Descarga de Comprobantes SRI 🇪🇨

API REST en **ASP.NET Core 8** que automatiza consultas en el portal del SRI Ecuador con **Playwright + Chrome** para descargar comprobantes electrónicos **recibidos** y **emitidos**.

> Estado actual: recibidos se ejecuta **asíncronamente por defecto** con Hangfire/PostgreSQL, worker .NET y parser Node.js. Soporta **XML o PDF**, con espera síncrona opcional. Emitidos conserva su contrato **PDF**.

Consulta [ASYNC_RECEIVED_DOCUMENTS.md](ASYNC_RECEIVED_DOCUMENTS.md) para configuración, migraciones, API/worker/parser, progreso y recuperación. PostgreSQL y el worker son necesarios también al solicitar `executionMode: "sync"`.

## ⚠️ Importante: no subir `wwwroot` a GitHub

La carpeta `wwwroot` contiene comprobantes tributarios reales (PDF/XML) con datos sensibles de contribuyentes (RUC, razón social, montos, claves de acceso). **Nunca debe subirse al repositorio.**

Asegúrate de que tu `.gitignore` incluya:

```gitignore
wwwroot/recibidos/
wwwroot/emitidos/
sri_cookies.json
vpn/client.ovpn
```

Si ya se subió por error, elimínala del historial (no solo del working tree):

```bash
git rm -r --cached wwwroot/recibidos wwwroot/emitidos
git commit -m "chore: quitar wwwroot del control de versiones"
```

## Resumen rápido

| Flujo | Endpoint | Qué hace |
|---|---|---|
| Recibidos | `POST /api/received-documents/query` | Recorre las páginas verificables de recibidos y descarga XML o PDF según `downloadFormat`. |
| Emitidos | `POST /api/SriEmitidos/consultar` | Consulta comprobantes emitidos autorizados y descarga PDFs. |
| Descargar PDF emitido guardado | `GET /api/SriEmitidos/descargar/{ruc}/{claveAcceso}` | Devuelve un PDF emitido previamente descargado. |

Para recibidos, la API persiste la solicitud y responde `202` con un identificador. El worker abre la sesión SRI, recorre páginas y guarda archivos y resultados incrementalmente. Laravel puede consultar progreso y resultados paginados; el detalle incluye JSON de la librería. Los ejemplos de respuesta final siguientes corresponden al modo `sync` o al resultado de la extracción.

## Funcionalidades

- Consulta de comprobantes electrónicos **recibidos**.
- Consulta de comprobantes electrónicos **emitidos**.
- Descarga de recibidos como **XML** o **PDF**.
- Descarga de emitidos como **PDF**.
- Extracción de XML recibido desde respuestas JSF.
- Deserialización parcial de XML recibido por tipo de comprobante.
- Validación básica de PDFs descargados mediante encabezado `%PDF`.
- Persistencia local de archivos descargados por RUC.
- Swagger UI para inspeccionar endpoints.
- Ejecución local con .NET o contenerizada con Docker.
- Soporte operativo para Chrome, Xvfb y OpenVPN opcional en Docker.

## Stack tecnológico

| Área | Tecnología |
|---|---|
| Framework | ASP.NET Core 8 |
| Lenguaje | C# |
| Automatización | Microsoft Playwright 1.60 + Chrome/Chromium |
| API docs | Swagger / Swashbuckle |
| XML | `System.Xml`, `XDocument`, `XmlSerializer` |
| Contenedores | Docker, Docker Compose |
| Runtime navegador en contenedor | Chrome + Xvfb |
| Red opcional | OpenVPN dentro del contenedor |

## Estructura del proyecto

```text
.
├── DescagaCompronanteSRI/       # API, controllers and Swagger
├── SriCrawler.Core/            # Shared contracts, crawler, storage, jobs and persistence
├── SriCrawler.Worker/          # Hangfire server and recoverable dispatcher
├── sri-document-parser/        # Private Fastify service and real-library fixtures
├── DescagaCompronanteSRI.Tests/
├── TwoCaptcha/
├── .env.jobs.example
├── .env.storage.example
├── docker-compose.yml
└── ASYNC_RECEIVED_DOCUMENTS.md
```

> Nota: el proyecto `TwoCaptcha` existe como referencia local, pero el flujo principal actual no depende de él directamente.

## Cómo funciona

```text
Cliente HTTP
  -> Controller ASP.NET Core
  -> DTO de consulta
  -> Servicio de consulta SRI
  -> PlaywrightSession
  -> Portal SRI / JSF
  -> Lectura de tabla
  -> Descarga XML/PDF
  -> Guardado en el proveedor configurado (recibidos) o wwwroot (emitidos)
  -> Respuesta JSON
```

### Flujo de recibidos

```text
POST /api/received-documents/query
  -> ReceivedDocumentsController: valida la solicitud en inglés
  -> ReceivedDocumentsService: coordina una extracción
  -> ReceivedDocumentsSession: reutiliza login y acceso al portal; libera la sesión
  -> ReceivedDocumentsPage: aplica filtros, toma instantáneas y verifica cada avance de página
  -> DocumentDownloader: elige la estrategia XML o PDF por DownloadFormat
  -> DocumentParser: extrae XML e informa el resultado de interpretación
  -> IDocumentStorage: guarda y abre contenido mediante LocalDocumentStorage o S3CompatibleDocumentStorage
  -> ReceivedDocumentsResponse: contadores, resultados por documento y errores tipados
```

`ReceivedDocumentsPage` encapsula el DOM y las referencias JSF. Reconoce los widgets del registro moderno `PrimeFaces.widgets` y las variables globales de versiones antiguas (como `wdvTablaRecibidos`); omite las ventanas de iframes de otro origen, como reCAPTCHA, que bloquean la inspección de propiedades. Esos identificadores no se incluyen en la respuesta pública. Los modelos XML están en `Models/Documents`; sus identificadores C# son ingleses y los atributos de serialización conservan los nombres del SRI.

El procesamiento es síncrono y secuencial, reutiliza una sesión y recorre todas las páginas cuya navegación puede confirmar. Cada archivo se libera después de guardarlo. Los identificadores JSF se renuevan por página; las claves de acceso y huellas permiten detectar repeticiones dentro de la extracción. Los resultados interpretados siguen acumulándose en la respuesta. Corrección regional de importes, validación estricta de archivos, deduplicación entre consultas y concurrencia siguen pendientes.

Nuevas descargas de recibidos:

```text
# Clave dentro del bucket S3/R2
{companyId}/{taxpayerId}/{year}/{month}/received/{documentType}/{accessKey}.xml
{companyId}/{taxpayerId}/{year}/{month}/received/{documentType}/{accessKey}.pdf

# Proveedor local
wwwroot/documents/{companyId}/{taxpayerId}/{year}/{month}/received/{documentType}/{accessKey}.{extension}
```

Los archivos anteriores en `wwwroot/recibidos/{ruc}/` permanecen intactos. No se migran automáticamente.

### Flujo de emitidos

```text
POST /api/SriEmitidos/consultar
  -> sanitiza usuario como RUC
  -> crea destino wwwroot/emitidos/{ruc}
  -> login SRI
  -> sesión JSF de emitidos
  -> aplica fecha exacta, estado AUT y tipo
  -> lee tabla de comprobantes emitidos
  -> descarga PDF
  -> retorna UsuarioSriEmitidos con comprobantes descargados
```

Archivos generados:

```text
wwwroot/emitidos/{ruc}/{claveAcceso}.pdf
```

## Tipos de comprobante

| Valor | Tipo |
|---:|---|
| `1` | Factura |
| `2` | Liquidación de compra |
| `3` | Nota de crédito |
| `4` | Nota de débito |
| `6` | Retención |
| `7` | Guía de remisión |

## Uso de la API

### Consultar comprobantes recibidos

```http
POST /api/received-documents/query
Content-Type: application/json
```

```json
{
  "companyId": "acme",
  "user": "1234567890001",
  "additionalUser": null,
  "password": "YOUR_SRI_PASSWORD",
  "year": 2026,
  "month": 1,
  "day": 0,
  "documentType": "invoice",
  "downloadFormat": "xml"
}
```

`day: 0` consulta todo el mes. `downloadFormat` acepta `xml` o `pdf`. `documentType` acepta `invoice`, `purchaseSettlement`, `creditNote`, `debitNote`, `remissionGuide`, `withholding` o `remissionGuideAlternative`; sus valores internos siguen siendo los códigos del portal. Los enums no aceptan valores numéricos. Despacho (`companyId`), usuario, contraseña, año, mes, día, tipo y formato son obligatorios. `companyId` acepta de 1 a 64 letras minúsculas, números y guiones; debe empezar y terminar con una letra o número. No se acepta `provider` ni `bucket` en el body.

La ruta `/api/ConsultaComprobantes/consultar` fue retirada. El contrato de recibidos ya no acepta campos JSON en español. Emitidos conserva su contrato anterior.

```bash
curl --location 'http://localhost:5276/api/received-documents/query' \
  --header 'Content-Type: application/json' \
  --data '{
    "companyId": "acme",
    "user": "1234567890001",
    "additionalUser": null,
    "password": "YOUR_SRI_PASSWORD",
    "year": 2026,
    "month": 1,
    "day": 0,
    "documentType": "invoice",
    "downloadFormat": "xml"
  }'
```

### Consultar comprobantes emitidos

```http
POST /api/SriEmitidos/consultar
Content-Type: application/json
```

Body:

```json
{
  "usuario": "1234567890001",
  "usuarioAdicional": null,
  "password": "TU_PASSWORD_SRI",
  "comprobante": 1,
  "anio": 2026,
  "mes": 1,
  "dia": 15
}
```

Ejemplo `curl`:

```bash
curl --location 'http://localhost:5276/api/SriEmitidos/consultar' \
  --header 'Content-Type: application/json' \
  --data '{
    "usuario": "1234567890001",
    "usuarioAdicional": null,
    "password": "TU_PASSWORD_SRI",
    "comprobante": 1,
    "anio": 2026,
    "mes": 1,
    "dia": 15
  }'
```

### Descargar PDF emitido previamente guardado

```http
GET /api/SriEmitidos/descargar/{ruc}/{claveAcceso}
```

Ejemplo:

```bash
curl --location 'http://localhost:5276/api/SriEmitidos/descargar/1234567890001/CLAVE_ACCESO_49_DIGITOS' \
  --output comprobante.pdf
```

## Respuestas esperadas

### Recibidos

La respuesta incluye los resultados de todas las páginas procesadas, incluso filas que no pudieron leerse o descargarse:

```json
{
  "companyId": "acme",
  "taxpayerId": "1234567890001",
  "businessName": "EXAMPLE COMPANY",
  "status": "completed",
  "discoveredCount": 1,
  "downloadedCount": 1,
  "failedCount": 0,
  "pagination": {
    "status": "completed",
    "pagesProcessed": 1,
    "reportedTotalCount": 1,
    "duplicateCount": 0
  },
  "documents": [
    {
      "pageNumber": 1,
      "rowIndex": 0,
      "metadata": {
        "supplierBusinessName": "EXAMPLE SUPPLIER",
        "documentTypeName": "Factura",
        "authorizationNumber": "...",
        "issuedAt": "01/01/2026",
        "authorizedAt": "01/01/2026",
        "amount": 100.00,
        "taxes": 15.00,
        "total": 115.00,
        "relatedDocuments": ""
      },
      "downloadFormat": "xml",
      "downloadStatus": "downloaded",
      "parseStatus": "parsed",
      "storage": {
        "provider": "s3",
        "bucket": "configured-bucket",
        "key": "acme/1234567890001/2026/01/received/invoice/1111111111111111111111111111111111111111111111111.xml"
      },
      "filePath": null,
      "parsedDocument": {},
      "errors": []
    }
  ],
  "errors": []
}
```

- `status`: `completed` exige recorrido completo y todos los documentos descubiertos guardados; `partial` indica que se guardó al menos uno, pero hubo descargas fallidas o recorrido incompleto; `failed` indica que ninguno se guardó y existen fallos o recorrido incompleto; `noDocuments` exige una consulta vacía confirmada.
- `pagination.status`: `notStarted` si no se ejecutó la consulta, `completed` si se verificó todo el recorrido, `incomplete` si faltó evidencia o hubo anomalías. Es independiente del éxito de las descargas.
- `pagination.pagesProcessed`: páginas distintas con filas procesadas; una consulta vacía confirmada devuelve cero. `reportedTotalCount` conserva el primer total anunciado disponible, o `null`; cualquier cambio posterior produce inconsistencia.
- `pagination.duplicateCount`: filas repetidas omitidas. Se conserva el primer resultado de cada clave y se procesan los documentos nuevos, pero el recorrido queda `incomplete`.
- `discoveredCount = downloadedCount + failedCount`: incluye un resultado por clave distinta y uno por fila ilegible. Excluye duplicados omitidos; el total del portal se contrasta con las filas recorridas, incluidos esos duplicados.
- `downloadStatus`: `downloaded` o `failed`. Descargado significa obtenido y guardado con las comprobaciones actuales; no certifica validez contable ni validación XML estricta.
- `parseStatus`: `parsed`, `failed`, `unsupported` o `notApplicable` (PDF). Un error de interpretación conserva el archivo descargado y se reporta en `errors` del documento; no cambia su estado de descarga.
- `parsedDocument`: contenido interpretado con propiedades JSON en inglés. Las guías siguen sin interpretación conectada y producen `unsupported`.
- `storage`: referencia estable con `provider` (`local`, `s3` o `r2`), `bucket` y `key`. En local, `bucket` es `null` y `filePath` contiene la ruta física; en remoto, `filePath` es `null`. Sin almacenamiento confirmado, `storage` es `null`. No se devuelven URLs firmadas.
- `metadata` puede ser `null` si la fila no pudo leerse. `pageNumber` comienza en uno y `rowIndex` comienza en cero dentro de cada página.
- Cada error contiene `code`, `message`, `pageNumber` y `rowIndex` (ubicaciones `null` cuando no corresponden). Los códigos existentes son `loginFailed`, `portalAccessFailed`, `queryFailed`, `rowReadFailed`, `downloadFailed`, `parsingFailed`, `unsupportedDocumentType`, `storageFailed` y `unexpectedError`.

Los errores de paginación son `paginationNavigationFailed`, `repeatedPage`, `duplicateDocument`, `paginationStateUnknown`, `paginationInconsistent` y `paginationLimitReached`.

En modo `sync`, si termina antes de la espera máxima, una consulta ejecutada devuelve HTTP `200`, incluso si falla una transición posterior o todos sus documentos fallaron; el resultado lo indica `status`. Errores previos a la ejecución de la consulta devuelven `500` con resultado tipado. Solicitudes inválidas devuelven `400`.

### Configuración y verificación de paginación

No cambia el body de la solicitud. Estas opciones pertenecen al servidor, en `appsettings.json` o mediante variables de entorno con separador `__` (por ejemplo `ReceivedDocumentsPagination__MaxPages=1000`):

```json
{
  "ReceivedDocumentsPagination": {
    "MaxPages": 1000,
    "NavigationAttempts": 3,
    "TransitionTimeoutMilliseconds": 60000,
    "RetryDelayMilliseconds": 2000
  }
}
```

Cada avance tiene hasta tres intentos de espera, de hasta 60 segundos cada uno, con pausas de 2 y 4 segundos. Si una respuesta sigue pendiente o el paginador ya avanzó, se espera esa misma operación sin volver a pulsar «siguiente». Se exige respuesta correlacionada, actualización de tabla y posición verificable. Una página de menos de 50 filas no implica final; sin paginador se necesita confirmación del widget de página única o paginación deshabilitada. El límite solo interrumpe si aún quedan páginas pendientes. Las opciones inválidas impiden arrancar.

Un retroceso, salto o página completa repetida detiene el recorrido. Un total cambiante, una página vacía inesperada, un final prematuro o documentos repetidos impiden declarar `completed`. Los archivos ya guardados se conservan. Esto verifica las señales del portal, sin ofrecer una instantánea transaccional de datos que el SRI pudiera cambiar durante la consulta.

Pruebas locales con Chrome, sin entrar al SRI ni usar el almacenamiento remoto real:

```bash
SRI_STORAGE_TESTS=0 SRI_BROWSER_TESTS=1 dotnet test DescagaCompronanteSRI.Tests/DescagaCompronanteSRI.Tests.csproj --configuration Release
```

La validación real de paginación requiere un período con más de 50 documentos: contrastar el total mostrado por el SRI con `reportedTotalCount`, las páginas procesadas, los conteos y los objetos efectivamente guardados. Ejecutar XML y PDF por separado. **Validación real realizada con PDF:** 361 documentos anunciados, 8 páginas procesadas (7 de 50 filas y una de 11), 361 documentos guardados, cero fallos y cero duplicados; `status` y `pagination.status` devolvieron `completed`. Una lectura independiente de S3 confirmó los 361 objetos PDF esperados, ninguno vacío, y recuperó un PDF por página. También se recorrieron las ocho páginas en otra sesión y se descargó e interpretó correctamente un XML por página (8 muestras; no una extracción XML completa). El portal simulado cubre además múltiples páginas, respuestas tardías, referencias JSF renovadas y la variante antigua de PrimeFaces con un iframe de otro origen. Las pruebas automatizadas Local/S3/R2 utilizan sustitutos; la prueba real S3 continúa siendo opcional.

### Emitidos

Ejemplo simplificado:

```json
{
  "ruc": "1234567890001",
  "razonSocial": "EMPRESA EJEMPLO S.A.",
  "total": 1,
  "mensaje": "1/1 comprobante(s) descargado(s) en: emitidos/1234567890001/",
  "comprobantes": [
    {
      "tipoSerie": "Factura 001-001-000000001",
      "numeroFactura": "001-001-000000001",
      "claveAcceso": "...",
      "fechaEmision": "15/01/2026",
      "importeTotal": 100.00,
      "rutaArchivo": "/app/wwwroot/emitidos/1234567890001/....pdf"
    }
  ]
}
```

## Verificar la refactorización de recibidos

Las pruebas de coordinación y contrato HTTP no acceden al SRI:

```bash
dotnet test DescagaCompronanteSRI.Tests/DescagaCompronanteSRI.Tests.csproj --configuration Release
```

La prueba de navegador usa un portal simulado local y necesita Google Chrome. Para ejecutarla junto con toda la suite:

```bash
SRI_BROWSER_TESTS=1 dotnet test DescagaCompronanteSRI.Tests/DescagaCompronanteSRI.Tests.csproj --configuration Release
```

En Swagger, prueba el nuevo endpoint con credenciales introducidas localmente y repite la consulta con `downloadFormat: "xml"` y `downloadFormat: "pdf"`. Esta comprobación real es independiente de las pruebas simuladas. La API continúa sin autenticación en esta etapa de desarrollo.

## Almacenamiento de recibidos: Local, S3 y R2

El destino se configura exclusivamente en el servidor. Copia `.env.storage.example` a `.env.storage` si aún no tienes este archivo; no sobrescribas credenciales existentes. `.env.storage` está excluido de Git y del contexto de construcción Docker.

| Variable | Uso |
|---|---|
| `DOCUMENT_STORAGE_PROVIDER` | `Local` (por defecto), `S3` o `R2`; nombres sin distinción entre mayúsculas y minúsculas. |
| `DOCUMENT_STORAGE_BUCKET` | Bucket existente, obligatorio para S3/R2. |
| `DOCUMENT_STORAGE_SERVICE_URL` | Vacío para AWS estándar; endpoint HTTPS obligatorio para R2. |
| `DOCUMENT_STORAGE_FORCE_PATH_STYLE` | `true` o `false`; por defecto `false`. |
| `AWS_REGION` | Región AWS obligatoria para S3; R2 utiliza `auto`. |
| `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY` | Credenciales del proveedor elegido, obligatorias para S3/R2. |
| `AWS_SESSION_TOKEN` | Opcional para credenciales temporales de AWS. |

En `Development`, la aplicación busca `.env.storage` primero en su directorio de contenido y después en su padre inmediato. No modifica las variables globales del proceso. Prioridad: configuración JSON, archivo local, variables de entorno y argumentos de ejecución. En otros entornos se utilizan las variables inyectadas; Docker Compose carga `.env.storage` mediante `env_file` y no lo copia a la imagen. Para utilizar Compose sin almacenamiento remoto, crea el archivo a partir del ejemplo con `DOCUMENT_STORAGE_PROVIDER=Local`.

`DOCUMENT_STORAGE_DEFAULT_ORGANIZATION_ID` se ignora. El despacho siempre llega como `companyId` obligatorio en el body. Debe ser un identificador estable del backend que coordina las descargas; cuando exista autenticación, su resolución se trasladará al contexto autenticado. Actualmente es una convención de organización, no una comprobación de pertenencia a un despacho.

Las claves no empiezan con `/`. Después del contribuyente se incluyen el año y mes de la consulta (`year` y `month` del body), con cuatro y dos dígitos respectivamente, por ejemplo `acme/1719956854001/2026/09/received/invoice/{accessKey}.xml`. Se utiliza el período solicitado, no la fecha de descarga; no se añaden campos nuevos al body. XML y PDF comparten directorio y se distinguen por extensión. Las dos variantes de guía usan `remissionGuide`; los demás tipos conservan sus nombres camelCase. Se utiliza el usuario normalizado (10 o 13 dígitos) como contribuyente, sin convertir una cédula a RUC.

El archivo se sube desde el stream descargado, sin una copia local definitiva cuando se usa S3/R2. El SDK usa reintentos estándar, con un máximo de dos reintentos. Un error del proveedor produce `storageFailed` y permite continuar con otros documentos; no existe fallback silencioso al disco. Una clave de acceso inválida (distinta de 49 dígitos ASCII) también produce un error de almacenamiento. Repetir una descarga sobrescribe la misma clave; si el bucket tiene versionado, conserva sus versiones según su configuración.

Una configuración incompleta o un proveedor desconocido impide el arranque con un mensaje controlado. La aplicación no crea buckets, modifica permisos ni solicita acceso público. Las credenciales requieren permiso de escritura y lectura de objetos en el destino configurado.

Para cambiar S3 por R2, actualiza proveedor, endpoint, bucket y credenciales, y reinicia la API. Las nuevas operaciones usarán el nuevo destino. Los archivos anteriores no se transfieren automáticamente; sus referencias conservan el proveedor y bucket originales. Esta implementación no resuelve lecturas simultáneas de proveedores anteriores ni añade endpoints de descarga. Emitidos continúa utilizando su almacenamiento local actual.

### Prueba real del almacenamiento

Las pruebas normales usan sustitutos del cliente S3 y no acceden al bucket. Para probar el proveedor configurado con archivos sintéticos XML/PDF:

```bash
SRI_STORAGE_TESTS=1 dotnet test DescagaCompronanteSRI.Tests/DescagaCompronanteSRI.Tests.csproj --configuration Release --filter FullyQualifiedName~StorageIntegrationTests --logger "console;verbosity=normal"
```

Esta prueba requiere permisos de escritura, lectura y eliminación (incluida eliminación de versiones si el bucket tiene versionado). Utiliza un prefijo único `storage-test-<uuid>/`, compara bytes y tipo de contenido, y limpia únicamente sus propios objetos. No accede al SRI ni usa documentos de contribuyentes.

## Ejecutar localmente

### Requisitos

- .NET SDK 8.0+
- Google Chrome estable
- Dependencias de Playwright instaladas
- Acceso a Internet
- Credenciales válidas del SRI

### Restaurar dependencias

```bash
dotnet restore DescagaCompronanteSRI/DescagaCompronanteSRI.csproj
```

### Instalar browsers de Playwright

```bash
dotnet tool install --global Microsoft.Playwright.CLI
playwright install chromium
```

Si ya tienes la herramienta instalada, puedes omitir el primer comando.

### Ejecutar API

```bash
cd DescagaCompronanteSRI
dotnet run
```

Por configuración local, la API suele quedar disponible en:

```text
http://localhost:5276
```

Swagger UI:

```text
http://localhost:5276/swagger
```

## Ejecutar con Docker

Docker es útil porque empaqueta .NET, Chrome, dependencias de sistema, Xvfb y OpenVPN opcional.

### Construir y levantar

```bash
docker compose --env-file .env.jobs up --build -d
```

### Ver logs

```bash
docker compose --env-file .env.jobs logs -f sri-descarga
```

La API queda disponible en:

```text
http://localhost:8080
```

Swagger UI:

```text
http://localhost:8080/swagger
```

### Detener

```bash
docker compose down
```

### Reiniciar

```bash
docker compose restart
```

## Volúmenes y archivos descargados

`docker-compose.yml` monta:

```yaml
./wwwroot:/app/wwwroot
./vpn/client.ovpn:/app/vpn/client.ovpn:ro
./sri_cookies.json:/app/sri_cookies.json
```

Los comprobantes quedan bajo:

```text
wwwroot/documents/{companyId}/{taxpayerId}/{year}/{month}/received/{documentType}/
wwwroot/recibidos/{ruc}/  # Descargas anteriores
wwwroot/emitidos/{ruc}/
```

> Importante: `wwwroot` está servido como archivos estáticos por la API. No expongas esta aplicación a Internet sin proteger autenticación, autorización y acceso a documentos. **Tampoco la subas a GitHub** (ver sección al inicio del README).

## Configuración

Archivos relevantes:

| Archivo | Uso |
|---|---|
| `DescagaCompronanteSRI/appsettings.json` | Logging, `AllowedHosts`, valor `XApiKey` actual. |
| `DescagaCompronanteSRI/appsettings.Development.json` | Logging de desarrollo. |
| `DescagaCompronanteSRI/Properties/launchSettings.json` | Puertos locales y perfiles de ejecución. |
| `docker-compose.yml` | Puertos, volúmenes, memoria, VPN y variables del contenedor. |
| `entrypoint.sh` | Arranque de Xvfb, OpenVPN opcional y API .NET. |

### Nota de seguridad sobre `XApiKey`

Swagger declara un esquema `XApiKey`, pero en el código actual no se encontró middleware o filtro que lo haga cumplir. No asumas que los endpoints están protegidos solo porque Swagger muestre ese header.

Antes de exponer la API fuera de local o red privada, implementa autenticación/autorización real.

## Seguridad y limitaciones conocidas

Prioridades antes de producción:

1. Implementar autenticación real para todos los endpoints.
2. Mover PDFs/XMLs fuera de `wwwroot` y servirlos solo mediante endpoints autorizados.
3. Validar estrictamente RUC, año, mes, día, tipo de comprobante y clave de acceso.
4. Evitar devolver `ex.Message` al cliente en producción.
5. Redactar credenciales, identificadores y rutas sensibles en logs.
6. Agregar rate limiting y control de concurrencia para requests que abren Playwright.
7. Crear pruebas automatizadas para validación, XML, parsing de tablas y descarga.
8. Revisar si la referencia a `TwoCaptcha` debe mantenerse, aislarse o removerse.
9. Confirmar que `wwwroot/recibidos`, `wwwroot/emitidos`, `sri_cookies.json` y `vpn/client.ovpn` estén excluidos del repositorio (`.gitignore`).

Limitaciones actuales:

- Emitidos descarga PDF; no hay XML emitido implementado en el flujo actual.
- Recibidos no tiene endpoint dedicado de descarga por archivo guardado.
- La automatización depende de selectores internos del portal SRI; cambios del portal pueden romper el flujo.
- Existen pruebas automatizadas de validación, modelos XML, coordinación, almacenamiento y contratos HTTP; las pruebas reales del SRI siguen requiriendo credenciales introducidas localmente.
- Swagger está habilitado siempre en `Program.cs`.

## Troubleshooting

### Chrome no inicia en Docker

Verifica que `docker-compose.yml` tenga suficiente memoria compartida:

```yaml
shm_size: '1gb'
```

También revisa logs:

```bash
docker compose --env-file .env.jobs logs -f sri-descarga
```

### No se encuentran comprobantes

Revisa:

- RUC correcto.
- Credenciales válidas.
- Año, mes y día correctos.
- Tipo de comprobante correcto.
- Si el portal SRI está disponible.

### La sesión JSF falla

Puede ocurrir si el SRI cambia el flujo o los selectores. Revisa logs relacionados con:

```text
JSF
ViewState
frmPrincipal
tablaCompRecibidos
tablaCompEmitidos
```

### La descarga falla

Si la consulta/descarga deja de funcionar (ej. el portal bloquea o limita la IP actual), **la solución es cambiar de dirección IP usando la VPN** (conectar/reconectar OpenVPN para obtener una IP distinta). Con eso normalmente se resuelve.

Si el problema persiste después de cambiar de IP, revisa además:

- Permisos de escritura en `wwwroot`.
- Que el archivo descargado sea PDF válido cuando corresponde.
- Que el comprobante tenga link XML/PDF disponible en la tabla del SRI.
- Que no se esté saturando el portal con consultas consecutivas.

## Desarrollo recomendado

Para cambios futuros, prioriza:

- Tests unitarios con fixtures HTML/XML.
- Storage privado fuera de `wwwroot`.
- Middleware de autenticación.
- Configuración tipada para URLs, timeouts y rutas.
- `ILogger` estructurado en lugar de `Console.WriteLine`.
- Jobs asíncronos para consultas largas.
- Health checks y métricas operativas.

## Documentación adicional

Hay un análisis más profundo del estado actual del proyecto en:

```text
docs/project-analysis.md
```

> Nota: en el estado actual del repositorio, `/docs/` está ignorado por `.gitignore`.

## Validación de recibidos

Los importes inválidos o ambiguos devuelven `null`; las fechas conservan su texto original y añaden valores ISO. Los archivos XML/PDF se validan antes de almacenarse, con estados independientes de validación, almacenamiento y parsing. Los resultados históricos conservan validación `notChecked`.

Consulta [los formatos, estados, límites y pasos de migración](RECEIVED_DOCUMENT_VALIDATION.md). Las rutas de almacenamiento se conservan. La consulta admite `downloadPolicy`: `reuseValid` por defecto y `refresh` para volver a descargar.

La [reutilización entre consultas y el guardado local atómico](DOCUMENT_REUSE.md) evitan descargas repetidas, mantienen los metadatos actuales y agregan `newlyDownloadedCount`, `reusedCount` y `acquisitionSource`.

El [checklist de mejoras](IMPROVEMENT_CHECKLIST.md) conserva los 14 puntos originales, su estado y el siguiente bloque recomendado.
