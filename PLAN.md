# QualityLock v1 Plan

## Summary
Construir la solución en este orden y con esta estructura:

1. **Solución y capas**
   - `QualityLock.sln`
   - `src/QualityLock.Shared`: DTOs, enums y contratos comunes
   - `src/QualityLock.Domain`: entidades y reglas de negocio
   - `src/QualityLock.Application`: casos de uso, validaciones e interfaces
   - `src/QualityLock.Infrastructure`: Dapper, MySQL, repositorios, logging, reloj/sistema
   - `src/QualityLock.Api`: ASP.NET Core Web API
   - `src/QualityLock.Client.WinForms`: lock screen, caché local, hooks, safe mode
   - `tests/QualityLock.Application.Tests`
   - `tests/QualityLock.Api.IntegrationTests`
   - `database/mysql`: esquema y seeds
   - `tools`: script para generar `bypass.json`
   - `README.md`

2. **Enfoque funcional**
   - La API valida gafetes contra MySQL.
   - La estación se registra por `StationCode` en config local y se valida también contra BD.
   - Si no hay conectividad, el cliente permite desbloqueo solo a operadores presentes en una **réplica local firmada de operadores permitidos para esa estación**.
   - La hotkey `Ctrl+Shift+Alt+F12` abre panel admin para `override` online; si no hay backend, el único escape local es `C:\ProgramData\QualityLock\bypass.json`.
   - No se intercepta ni se intenta bloquear `Ctrl+Alt+Supr`.

## Key Changes

### 1. Estructura inicial de solución
- Crear solución .NET 8 con referencias limpias:
  - `Shared` referenciado por API y cliente.
  - `Application` depende de `Domain`.
  - `Infrastructure` depende de `Application` y `Domain`.
  - `Api` depende de `Application`, `Infrastructure`, `Shared`.
  - `Client.WinForms` depende de `Shared`.
- Configuración centralizada:
  - API: `appsettings.json`, `appsettings.Development.json`.
  - Cliente: `appsettings.json` más estado en `C:\ProgramData\QualityLock`.
- Logging:
  - API con `Serilog` a consola y archivo.
  - Cliente con log de archivo local para soporte.

### 2. Biblioteca compartida
- Enums:
  - `StationType`: `ICT`, `FCT`, `Packing`
  - `SessionStatus`: `Open`, `Closed`, `ForcedClosed`, `OfflinePending`
  - `StationEventType`: `LockShown`, `BadgeScanned`, `UnlockGranted`, `UnlockDenied`, `AutoLock`, `AdminPanelOpened`, `AdminOverrideApproved`, `AdminOverrideRejected`, `BypassUsed`, `SafeModeEntered`, `HeartbeatSent`, `ClientRecovered`
  - `OverrideReasonType`
  - `ValidationDecision`: `Allowed`, `Denied`, `OfflineAllowed`
- DTOs:
  - `BadgeValidationRequest/Response`
  - `StartSessionRequest/Response`
  - `EndSessionRequest`
  - `StationEventRequest`
  - `AdminOverrideRequest/Response`
  - `HeartbeatRequest`
  - `StationBootstrapResponse`
  - `CachedOperatorDto`
  - `ApiErrorDto`
- Contratos compartidos:
  - nombres de headers/correlation id
  - rutas de archivos locales
  - constantes de expiración de caché y heartbeat

### 3. API ASP.NET Core
- Capas internas:
  - `Domain`: `Operator`, `Station`, `StationPermission`, `StationSession`, `StationEvent`, `AdminOverride`, `ClientHeartbeat`
  - `Application`: servicios `BadgeValidationService`, `SessionService`, `EventService`, `AdminOverrideService`, `HeartbeatService`, `StationBootstrapService`
  - `Infrastructure`: repositorios Dapper y fábrica `MySqlConnection`
- Endpoints públicos:
  - `POST /api/badges/validate`
  - `POST /api/sessions/start`
  - `POST /api/sessions/end`
  - `POST /api/events`
  - `POST /api/admin/override`
  - `POST /api/heartbeats`
  - `GET /api/stations/{stationCode}/bootstrap`
- Reglas de negocio:
  - validar operador activo
  - validar estación activa
  - validar permiso operador-estación
  - impedir más de una sesión abierta por estación
  - cerrar sesión previa si aplica solo mediante override o recuperación explícita
  - registrar auditoría de toda decisión, incluso rechazos
- Logging y observabilidad:
  - middleware de exception handling
  - request logging con `correlationId`
  - health endpoint
- Persistencia:
  - consultas Dapper con SQL explícito, sin ORM pesado
  - transacciones para `start/end session` + eventos asociados

### 4. Cliente WinForms
- UI principal:
  - formulario fullscreen, borderless, `TopMost`
  - reloj visible
  - nombre de estación visible
  - textbox oculto/enfocado para scanner tipo teclado
  - mensajes de estado claros: bloqueado, validando, desbloqueado, offline, safe mode
- Control de teclado:
  - hook de bajo nivel para suprimir `Alt+Tab`, `Alt+F4`, `LWin`, `RWin`, `Ctrl+Esc`
  - no tocar `Ctrl+Alt+Supr`
  - permitir cierre desde Task Manager; no usar mecanismos que secuestren sesión de Windows
- Flujo de desbloqueo:
  - leer gafete
  - validar online si hay backend
  - si backend no responde, validar contra réplica local de operadores permitidos para la estación
  - iniciar sesión remota si hay conectividad; si no, guardar sesión/eventos en cola local para sincronización posterior
  - re-bloquear por inactividad
- Estado local en `C:\ProgramData\QualityLock`:
  - `client-state.json`: crash counter, última sesión, safe mode
  - `operator-cache.json`: réplica local firmada de operadores autorizados para la estación
  - `event-queue.jsonl`: eventos/sesiones pendientes de sincronizar
  - `bypass.json`: contingencia local
  - `logs\`
- Safe mode:
  - activar tras `3` fallas anormales dentro de `10` minutos
  - en safe mode no aplica bloqueo agresivo; muestra pantalla de recuperación, evidencia del fallo y opción de revisar `bypass.json`
  - al siguiente arranque limpio, reinicia contador
- Panel admin:
  - hotkey secreta `Ctrl+Shift+Alt+F12`
  - solicita credenciales/admin badge + motivo
  - llama a `POST /api/admin/override`
  - si backend no está disponible, solo informa que el escape local es `bypass.json`
- Bypass local:
  - archivo `C:\ProgramData\QualityLock\bypass.json`
  - esquema con `stationCode`, `enabled`, `reason`, `expiresAtUtc`, `issuedBy`, `signature`
  - validación HMAC con secreto local configurado
  - script PowerShell en `tools/` para generarlo

### 5. SQL MySQL
- Script `001_init.sql` con tablas:
  - `operators`
  - `stations`
  - `station_permissions`
  - `station_sessions`
  - `station_events`
  - `admin_overrides`
  - `client_heartbeats`
- Columnas mínimas:
  - `operators`: `id`, `badge_code`, `employee_number`, `display_name`, `is_active`, `is_admin`, timestamps
  - `stations`: `id`, `station_code`, `station_name`, `station_type`, `host_name`, `is_active`, timestamps
  - `station_permissions`: `operator_id`, `station_id`, `can_operate`, timestamps
  - `station_sessions`: `id`, `station_id`, `operator_id`, `started_at_utc`, `ended_at_utc`, `status`, `started_online`, `ended_online`, `correlation_id`
  - `station_events`: `id`, `station_id`, `operator_id nullable`, `session_id nullable`, `event_type`, `event_at_utc`, `details_json`, `source`, `correlation_id`
  - `admin_overrides`: `id`, `station_id`, `admin_operator_id`, `target_operator_id nullable`, `reason`, `comments`, `approved`, `created_at_utc`
  - `client_heartbeats`: `id`, `station_id`, `sent_at_utc`, `client_version`, `is_safe_mode`, `last_activity_at_utc`, `details_json`
- Índices y restricciones:
  - `badge_code` y `station_code` únicos
  - índices por `station_id`, `operator_id`, fechas y sesiones abiertas
  - FKs con borrado restringido
- Script `002_seed.sql` con estaciones y operadores demo

### 6. Documentación
- `README.md` con:
  - arquitectura
  - prerequisitos
  - creación de BD
  - configuración API/cliente
  - orden de arranque
  - publicación y despliegue local
  - operación de bypass y safe mode
- Incluir ejemplos de:
  - `appsettings.json` de API
  - `appsettings.json` del cliente
  - `bypass.json`
  - flujo nominal online y contingencia offline

## Public APIs / Interfaces
- API HTTP:
  - `POST /api/badges/validate`: recibe `stationCode`, `badgeCode`, `clientUtc`; devuelve operador, decisión, permisos, motivo y snapshot de estación
  - `POST /api/sessions/start`: abre sesión auditada
  - `POST /api/sessions/end`: cierra sesión por unlock end, autolock, logout o recovery
  - `POST /api/events`: inserta eventos unitarios o en lote desde cola offline
  - `POST /api/admin/override`: aprueba o rechaza override y lo audita
  - `POST /api/heartbeats`: latido periódico del cliente
  - `GET /api/stations/{stationCode}/bootstrap`: entrega metadatos de estación y catálogo local de operadores permitidos
- Interfaces internas:
  - `IOperatorRepository`, `IStationRepository`, `ISessionRepository`, `IEventRepository`, `IAdminOverrideRepository`, `IHeartbeatRepository`
  - `IBadgeValidationService`, `ISessionService`, `IClientQueueService`, `IKeyboardBlockService`, `IBypassService`, `ISafeModeService`

## Test Plan
- API:
  - valida gafete activo y autorizado
  - rechaza operador inactivo o sin permiso
  - rechaza estación inexistente/inactiva
  - no permite doble sesión abierta en la misma estación
  - registra auditoría en validaciones, sesiones, eventos y overrides
  - heartbeat inserta registro correcto
- Cliente:
  - bloqueo inicial fullscreen/topmost
  - teclado bloquea `Alt+Tab`, `Alt+F4`, `Win`, `Ctrl+Esc`
  - hotkey admin abre panel
  - autolock dispara cierre de sesión y regreso a lock screen
  - offline permite acceso a operador contenido en la réplica local de esa estación
  - offline niega operador fuera de la réplica local
  - reconexión sincroniza cola local de sesiones/eventos
  - safe mode entra tras 3 fallas anormales en 10 minutos
  - `bypass.json` válido permite recuperación; inválido o expirado se rechaza
- SQL/integración:
  - migración limpia sobre esquema vacío
  - seeds cargan datos base coherentes
  - índices soportan búsquedas por badge, estación y sesiones abiertas

## Assumptions
- v1 no integra AD/LDAP ni ERP; la fuente de verdad es MySQL.
- La política offline es: si no hay API, puede desbloquear cualquier operador incluido en la réplica local de operadores autorizados para esa estación.
- El panel admin depende del backend; sin backend, la contingencia es solo `bypass.json`.
- `StationCode` se configura localmente y debe existir en BD; el nombre de host se usa para auditoría y diagnóstico.
- Safe mode no desbloquea automáticamente la máquina; solo reduce el riesgo de dejarla inutilizable y habilita recuperación controlada.
- No se intentará interceptar Secure Attention Sequence (`Ctrl+Alt+Supr`) en ningún punto.
