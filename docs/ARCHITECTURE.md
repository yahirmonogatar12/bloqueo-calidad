# QualityLock - Arquitectura y operacion

## Proposito

QualityLock controla el acceso a estaciones de produccion. El cliente Windows bloquea la estacion, lee gafetes por scanner tipo teclado y consulta a la API. La API decide si el operador puede usar la estacion y registra auditoria en MySQL.

## Capas

```text
QualityLock.Client.WinForms
        |
        | HTTP JSON
        v
QualityLock.Api
        |
        v
QualityLock.Application
        |
        +--> QualityLock.Domain
        |
        v
QualityLock.Infrastructure
        |
        v
MySQL

QualityLock.Shared es referenciado por API y cliente.
```

| Proyecto | Responsabilidad |
|---|---|
| `QualityLock.Shared` | DTOs, enums, constantes, rutas y paths locales |
| `QualityLock.Domain` | Entidades: operador, estacion, permiso, sesion, evento, override, heartbeat |
| `QualityLock.Application` | Reglas de negocio y contratos de servicios/repositorios |
| `QualityLock.Infrastructure` | Repositorios Dapper sobre tablas MySQL `_QA` |
| `QualityLock.Api` | Controladores HTTP, DI, logging, health check y middleware |
| `QualityLock.Client.WinForms` | Lock screen, scanner, cache local, bypass, safe mode y setup |

## Modelo de datos

| Tabla | Uso |
|---|---|
| `operators_QA` | Operadores por gafete, empleado, nombre, activo/admin |
| `stations_QA` | Estaciones por codigo, nombre, tipo, host y estado |
| `station_permissions_QA` | Relacion operador-estacion autorizada |
| `station_sessions_QA` | Sesiones abiertas/cerradas por estacion y operador |
| `station_events_QA` | Auditoria de decisiones y eventos de cliente |
| `admin_overrides_QA` | Overrides aprobados por admin |
| `client_heartbeats_QA` | Latidos del cliente, safe mode y actividad |

El esquema esta en `database/mysql/001_init.sql` y los datos demo en `database/mysql/002_seed.sql`.

## API

| Metodo | Ruta | Servicio |
|---|---|---|
| `POST` | `/api/badges/validate` | `BadgeValidationService.ValidateAsync` |
| `POST` | `/api/sessions/start` | `SessionService.StartAsync` |
| `POST` | `/api/sessions/end` | `SessionService.EndAsync` |
| `POST` | `/api/events` | `EventService.RecordBatchAsync` |
| `POST` | `/api/admin/override` | `AdminOverrideService.ProcessAsync` |
| `POST` | `/api/heartbeats` | `HeartbeatService.RecordAsync` |
| `GET` | `/api/stations/{stationCode}/bootstrap` | `StationBootstrapService.GetBootstrapAsync` |
| `PUT` | `/api/stations/{stationCode}` | `StationRegistrationService.RegisterAsync` |
| `GET` | `/health` | ASP.NET Core health checks |

La API usa `X-Correlation-Id`. Si el cliente no lo manda, `CorrelationIdMiddleware` genera uno y lo regresa en la respuesta.

## Flujo online

1. El cliente inicia, lee `C:\ProgramData\QualityLock\appsettings.json` (con fallback
   al `appsettings.json` junto al EXE en desarrollo) y crea `HttpClient`.
2. Si falta `StationCode` o se usa `--setup`, abre `StationSetupForm`.
3. Al arrancar intenta `GET /api/stations/{stationCode}/bootstrap`.
4. Si bootstrap responde, guarda `operator-cache.json`.
5. El lock screen queda fullscreen, topmost y enfoca el textbox oculto del scanner.
6. Al leer un gafete:
   - consulta `/health`;
   - si hay API, llama `/api/badges/validate`;
   - si la decision es `Allowed`, llama `/api/sessions/start`;
   - oculta el lock screen y arranca timer de inactividad.
7. Al expirar inactividad, llama `/api/sessions/end` y vuelve a bloquear.
8. Cada 60 segundos envia `/api/heartbeats` como best effort.

## Flujo offline actual

Cuando `/health` falla:

1. El cliente carga `C:\ProgramData\QualityLock\operator-cache.json`.
2. Si el gafete existe en la cache, concede acceso local.
3. Registra una linea en `event-queue.jsonl` para el inicio offline.

Limitaciones actuales:

- No hay sincronizador automatico que drene `event-queue.jsonl` hacia `/api/events`.
- El cierre de una sesion offline intenta llamar `/api/sessions/end` con `IsOnline = true`; si la API no esta disponible, la llamada se descarta.
- La cache se refresca en arranque y al registrar estacion, no en cada heartbeat.

## Recuperacion y bypass

El bypass se valida con `BypassService` leyendo `C:\ProgramData\QualityLock\bypass.json`. La firma usa HMAC-SHA256 con este payload:

```text
stationCode|issuedBy|expiresAtUtc|reason
```

El script `tools/Generate-Bypass.ps1` genera un archivo compatible.

Limitacion actual: el panel admin offline valida el bypass y muestra el resultado, pero no desbloquea automaticamente el `LockForm` ni registra un evento `BypassUsed`.

## Seguridad local

Mientras el lock screen esta activo:

- el form es fullscreen, borderless y topmost;
- un hook de teclado bloquea `Alt+Tab`, `Alt+F4`, teclas Windows y `Ctrl+Esc`;
- se escribe `DisableTaskMgr` en HKCU para bloquear Task Manager durante el lock;
- `Ctrl+Alt+Del` no se intercepta.

El panel de setup desde tray usa un PIN local hardcodeado (`admin1234`) en el codigo actual. Ese punto debe cambiarse antes de produccion.

## Configuracion

API:

- `ConnectionStrings:MySQL`
- `Serilog`
- `AllowedHosts`

Cliente:

- `StationCode`
- `Linea`
- `ApiBaseUrl`
- `ClientApiKey`
- `BypassHmacSecret`
- `AdminPin`
- `WindowAccessGuard` (reglas locales configurables desde setup para cerrar ventanas externas por rol/usuario)
- `QrInputFocus` (foco unico al desbloquear sobre un input text externo, con fallback por click relativo)

Paths locales definidos en `QualityLock.Shared.Constants.AppConstants`:

- `C:\ProgramData\QualityLock\appsettings.json`
- `C:\ProgramData\QualityLock\client-state.json`
- `C:\ProgramData\QualityLock\operator-cache.json`
- `C:\ProgramData\QualityLock\event-queue.jsonl`
- `C:\ProgramData\QualityLock\bypass.json`
- `C:\ProgramData\QualityLock\logs`

## Pruebas

Cobertura actual:

- `BadgeValidationService`: permitido, denegado por permiso, operador inactivo/desconocido, estacion desconocida, auditoria.
- `SessionService`: inicio correcto, sesion abierta duplicada, estacion desconocida, cierre.
- API: `/health`.

Comando:

```bash
dotnet test QualityLock.slnx
```

Resultado de esta revision: 11 pruebas superadas.
