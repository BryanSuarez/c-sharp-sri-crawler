# Descarga de Comprobantes SRI 🇪🇨

API REST en **ASP.NET Core 8** que automatiza consultas en el portal del SRI Ecuador con **Playwright + Chrome** para descargar comprobantes electrónicos **recibidos** y **emitidos**.

> Estado actual: recibidos soporta descarga **XML o PDF**; emitidos soporta descarga **PDF**.

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
| Recibidos | `POST /api/ConsultaComprobantes/consultar` | Consulta comprobantes recibidos y descarga XML o PDF según `descargarXml`. |
| Emitidos | `POST /api/SriEmitidos/consultar` | Consulta comprobantes emitidos autorizados y descarga PDFs. |
| Descargar PDF emitido guardado | `GET /api/SriEmitidos/descargar/{ruc}/{claveAcceso}` | Devuelve un PDF emitido previamente descargado. |

La aplicación recibe credenciales del SRI y filtros de consulta, abre una sesión automatizada de navegador, llena los formularios JSF del portal, lee la tabla de resultados, descarga los archivos y responde con metadatos de los comprobantes.

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
├── DescagaCompronanteSRI.slnx
├── DescagaCompronanteSRI/
│   ├── Program.cs
│   ├── Controllers/
│   │   ├── DescargaComprobantesController.cs
│   │   └── SriEmitidosController.cs
│   ├── Helpers/
│   │   ├── PlaywrightSession.cs
│   │   └── XmlHelper.cs
│   ├── Models/
│   ├── Service/
│   │   ├── ConsultaComprobantes.cs
│   │   ├── ConsultaComprobantesEmitidosService.cs
│   │   └── modelos XML por tipo de comprobante
│   ├── appsettings.json
│   └── DescagaCompronanteSRI.csproj
├── TwoCaptcha/
├── Dockerfile
├── docker-compose.yml
├── entrypoint.sh
└── README.md
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
  -> Guardado en wwwroot
  -> Respuesta JSON
```

### Flujo de recibidos

```text
POST /api/ConsultaComprobantes/consultar
  -> sanitiza usuario como RUC
  -> crea destino wwwroot/recibidos/{ruc}
  -> login SRI
  -> sesión JSF de recibidos
  -> aplica filtros año/mes/día/tipo
  -> lee tabla de comprobantes recibidos
  -> descarga XML o PDF
  -> retorna UsuarioSri con comprobantes descargados
```

Archivos generados:

```text
wwwroot/recibidos/{ruc}/{numeroAutorizacion}.xml
wwwroot/recibidos/{ruc}/{numeroAutorizacion}.pdf
```

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
POST /api/ConsultaComprobantes/consultar
Content-Type: application/json
```

Body:

```json
{
  "usuario": "1234567890001",
  "usuarioAdicional": null,
  "password": "TU_PASSWORD_SRI",
  "dia": 1,
  "anio": "2026",
  "mes": 1,
  "comprobante": 1,
  "descargarXml": true
}
```

Ejemplo `curl`:

```bash
curl --location 'http://localhost:5276/api/ConsultaComprobantes/consultar' \
  --header 'Content-Type: application/json' \
  --data '{
    "usuario": "1234567890001",
    "usuarioAdicional": null,
    "password": "TU_PASSWORD_SRI",
    "dia": 1,
    "anio": "2026",
    "mes": 1,
    "comprobante": 1,
    "descargarXml": true
  }'
```

Campo clave:

- `descargarXml: true` descarga XML.
- `descargarXml: false` descarga PDF.

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

El endpoint de recibidos devuelve un objeto con datos del contribuyente y lista de comprobantes descargados.

Ejemplo simplificado:

```json
{
  "ruc": "1234567890001",
  "razonSocial": "EMPRESA EJEMPLO S.A.",
  "totalComprobantes": 2,
  "comprobantes": [
    {
      "razonSocial": "PROVEEDOR EJEMPLO",
      "tipoDocumento": "FACTURA",
      "numeroAutorizacion": "...",
      "fechaEmision": "01/01/2026",
      "importeTotal": 100.00,
      "rutaArchivo": "/app/wwwroot/recibidos/1234567890001/....xml"
    }
  ]
}
```

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
docker compose up --build -d
```

### Ver logs

```bash
docker compose logs -f sri-descarga
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
wwwroot/recibidos/{ruc}/
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
- No se observaron tests automatizados versionados en el repositorio.
- Swagger está habilitado siempre en `Program.cs`.

## Troubleshooting

### Chrome no inicia en Docker

Verifica que `docker-compose.yml` tenga suficiente memoria compartida:

```yaml
shm_size: '1gb'
```

También revisa logs:

```bash
docker compose logs -f sri-descarga
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
