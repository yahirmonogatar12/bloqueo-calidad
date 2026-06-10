# QualityLock v1

QualityLock es una solucion .NET para bloquear estaciones de manufactura y permitir el acceso mediante gafete. El gafete trae el **usuario del MES** (`usuarios_sistema`): la API valida ese usuario contra MySQL, desbloquea y registra la sesion; el cliente WinForms muestra una pantalla de bloqueo fullscreen y mantiene una cache local para contingencia.

## Estado del proyecto

- Plataforma: .NET 9.
- Backend: ASP.NET Core Web API con Serilog, middleware de correlation id y manejo de errores.
- Persistencia: MySQL 8 + Dapper/MySqlConnector, tablas con sufijo `_QA`.
- Cliente: WinForms `net9.0-windows`, pantalla fullscreen, captura de scanner tipo teclado, hotkey de admin, cache local y bypass HMAC.
- Pruebas: unitarias de Application e integracion basica de `/health`.

Documentacion extendida:

- [Arquitectura y operacion](docs/ARCHITECTURE.md)
- [Revision tecnica del proyecto](docs/PROJECT_REVIEW.md)
- [Plan original](PLAN.md)

## Estructura

```text
src/
  QualityLock.Shared          DTOs, enums, rutas y constantes compartidas
  QualityLock.Domain          Entidades de negocio
  QualityLock.Application     Casos de uso e interfaces
  QualityLock.Infrastructure  Repositorios Dapper, MySQL y DI
  QualityLock.Api             ASP.NET Core Web API
  QualityLock.Client.WinForms Cliente de bloqueo para Windows
tests/
  QualityLock.Application.Tests
  QualityLock.Api.IntegrationTests
database/mysql/
  001_init.sql
  002_seed.sql
tools/
  Generate-Bypass.ps1
```

## Requisitos

| Requisito | Version |
|---|---|
| .NET SDK | 9.0 |
| MySQL | 8.0+ |
| Windows | 10/11 para el cliente WinForms |
| PowerShell | 5.1+ para generar bypass |

## Base de datos

Los scripts actuales crean tablas con sufijo `_QA` y estan orientados a una base MySQL existente.

```bash
mysql -u <usuario> -p <base_datos> < database/mysql/001_init.sql
mysql -u <usuario> -p <base_datos> < database/mysql/002_seed.sql
mysql -u <usuario> -p <base_datos> < database/mysql/003_unique_open_session.sql
```

La migracion `003` agrega un indice unico que refuerza, a nivel de base de datos,
la regla de **una sola sesion abierta por estacion** (cierra la condicion de carrera
donde dos peticiones concurrentes podian abrir sesiones duplicadas).

Tablas principales:

- `usuarios_sistema` — **fuente de verdad de usuarios** (compartida con el resto del MES; solo lectura desde QualityLock)
- `usuario_roles` + `roles` — **roles del usuario** (solo lectura); su `nivel` decide quién es admin
- `operators_QA` — fila *puente* autogenerada por cada usuario que desbloquea (necesaria por las FK de sesiones/eventos)
- `stations_QA`
- `station_permissions_QA`
- `station_sessions_QA`
- `station_events_QA`
- `admin_overrides_QA`
- `client_heartbeats_QA`

### Usuarios y autenticacion de personas

El desbloqueo y el login de administrador se validan contra **`usuarios_sistema`**
(la misma tabla del MES). QualityLock **no** crea ni modifica usuarios ahi.

- **Desbloqueo:** el gafete trae el `username`. Si existe y `activo=1`, la estacion se
  desbloquea y la sesion se atribuye a ese usuario. Para satisfacer las claves foraneas
  de `station_sessions_QA`/`station_events_QA` (que apuntan a `operators_QA`), se crea o
  actualiza de forma idempotente una fila puente en `operators_QA`
  (`badge_code = username`, `employee_number = "USR-{id}"`).
- **Contrasenas:** `usuarios_sistema.password_hash` es `SHA-256(password)` en hex, sin
  sal (compatible con el resto del MES). QualityLock lo verifica tal cual, en tiempo
  constante. El desbloqueo por gafete **no** pide contrasena; el login de admin **si**.
- **Quien es admin:** se define por **rol** (`usuario_roles` → `roles`), no por
  departamento/cargo. Un usuario es admin si tiene algun rol con `nivel >= MinRoleLevel`
  (por defecto `3`, que incluye `calidad` y `Tecnico QA`) o cuyo nombre este en la lista
  `Admin:Roles`. Se configura en la seccion `Admin` de la API; no se toca la base de datos.

  Jerarquia de `roles.nivel` (referencia): superadmin=10, admin=9,
  supervisor_almacen=8, supervisor_produccion=7, Supervisor SMD/Diseño=6,
  operador_almacen/Planeacion=5, supervisor_produccion/embarques=4,
  **calidad/Tecnico QA=3**, consulta/operador_embarques=2, invitado=1.

## Seguridad — IMPORTANTE

> **Rotacion de credencial:** versiones previas de `appsettings.json` contenian una
> cadena MySQL real (usuario `mes_admin`). Esa contrasena debe considerarse
> **comprometida** y **rotarse** en el servidor MySQL. Los archivos versionados ya
> no contienen secretos; las credenciales y claves se leen de variables de entorno
> o de `appsettings.Development.json` (ignorado por git).

La API ahora exige **autenticacion JWT** en todos los endpoints salvo `/api/auth/token`
y `/health`. El cliente WinForms obtiene un token presentando una API key compartida
(`Auth:ClientApiKey`) y lo envia como `Bearer` en cada peticion.

## Configuracion

### API

La API requiere, en arranque, una cadena de conexion y una clave de firma JWT.
Define estas claves como variables de entorno (produccion) o en
`appsettings.Development.json` (desarrollo; ignorado por git). Usa
`appsettings.json.example` como plantilla.

```powershell
$env:ConnectionStrings__MySQL = "Server=<host>;Port=3306;Database=<db>;Uid=<user>;Pwd=<password>;"
$env:Jwt__SigningKey          = "<clave-aleatoria-de-al-menos-32-bytes>"
$env:Auth__ClientApiKey       = "<clave-de-cliente-fuerte>"
dotnet run --project src/QualityLock.Api
```

| Clave | Env var | Descripcion |
|---|---|---|
| `ConnectionStrings:MySQL` | `ConnectionStrings__MySQL` | Cadena de conexion MySQL |
| `Jwt:SigningKey` | `Jwt__SigningKey` | Clave HMAC de firma (>= 32 bytes) |
| `Jwt:Issuer` / `Jwt:Audience` | `Jwt__Issuer` / `Jwt__Audience` | Emisor / audiencia del token |
| `Jwt:AccessTokenMinutes` | `Jwt__AccessTokenMinutes` | Vigencia del token (def. 720 min) |
| `Auth:ClientApiKey` | `Auth__ClientApiKey` | Clave que las estaciones presentan para obtener token |
| `Admin:MinRoleLevel` | `Admin__MinRoleLevel` | Nivel minimo de `roles.nivel` para ser admin (def. 3) |
| `Admin:Roles` | `Admin__Roles__0` … | Nombres de rol que siempre conceden admin (independiente del nivel) |

La seccion `Admin` decide quien puede abrir el panel, detener el servicio y registrar
overrides, **por rol** (`usuario_roles` → `roles`). Ejemplo:

```json
"Admin": {
  "MinRoleLevel": 3,
  "Roles": []
}
```

Con `MinRoleLevel: 3` son admin desde `calidad`/`Tecnico QA` (nivel 3) hacia arriba.
Para restringir solo a administradores, sube a `9`. Para forzar un rol concreto sin
bajar el umbral, agregalo a `Roles` (ej. `[ "Tecnico QA" ]`).

El perfil HTTP de desarrollo usa `http://localhost:5080`.

### Cliente

`src/QualityLock.Client.WinForms/appsettings.json`:

```json
{
  "StationCode": "ICT-01",
  "Linea": "M1",
  "ApiBaseUrl": "http://localhost:5080/",
  "BypassHmacSecret": "CHANGE-THIS-SECRET-IN-PRODUCTION",
  "AdminPin": "",
  "ClientApiKey": "",
  "AutoLockSeconds": 300
}
```

- `AdminPin`: **respaldo offline**. Las acciones de admin (panel, detener servicio) se
  validan con usuario+contrasena contra `usuarios_sistema` cuando hay backend; si la
  estacion esta sin red, se acepta este PIN local para no impedir la recuperacion.
  Tambien puede definirse en `QUALITYLOCK_ADMIN_PIN`. Si no se configura ninguno, se usa
  un PIN por defecto inseguro (`admin1234`) — **configurelo antes de desplegar**.
- `ClientApiKey`: debe coincidir con `Auth:ClientApiKey` de la API. Tambien puede
  definirse en `QUALITYLOCK_CLIENT_API_KEY`.
- `Linea`: linea de produccion de la estacion (`M1`, `M2`, ...). El backend usa
  `StationCode + Linea` para distinguir estaciones con el mismo codigo en lineas
  distintas.
- `AutoLockSeconds`: segundos de **inactividad real** (mouse/teclado en todo el sistema)
  antes de que la estacion se bloquee sola. Por defecto `300` (5 min). Si el operador
  esta trabajando, no se bloquea. El indicador de sesion tambien tiene un boton
  **"Cerrar sesion"** para bloquear manualmente.

Si `StationCode` esta vacio o se ejecuta con `--setup`, abre el panel de configuracion de estacion.

## Endpoints

Todos los endpoints requieren `Authorization: Bearer <token>` salvo
`/api/auth/token` y `/health`.

| Metodo | Ruta | Funcion |
|---|---|---|
| `POST` | `/api/auth/token` | Emite un JWT a cambio de la API key de cliente |
| `POST` | `/api/auth/admin-login` | Valida usuario+contrasena de `usuarios_sistema` y si es admin (requiere token de estacion) |
| `POST` | `/api/badges/validate` | Valida el `username` del gafete contra `usuarios_sistema` |
| `POST` | `/api/sessions/start` | Inicia sesion auditada |
| `POST` | `/api/sessions/end` | Cierra sesion |
| `POST` | `/api/events` | Inserta lote de eventos |
| `POST` | `/api/admin/override` | Registra override admin |
| `POST` | `/api/heartbeats` | Registra heartbeat del cliente |
| `GET` | `/api/stations/{stationCode}/bootstrap` | Metadata de estacion y operadores permitidos |
| `PUT` | `/api/stations/{stationCode}` | Registra o actualiza estacion |
| `GET` | `/health` | Health check |

## Flujo de arranque

1. Crear o actualizar el esquema MySQL con los scripts en `database/mysql`.
2. Configurar la cadena `ConnectionStrings:MySQL` de la API.
3. Ejecutar la API:

   ```bash
   dotnet run --project src/QualityLock.Api
   ```

4. Configurar el cliente con `StationCode`, `ApiBaseUrl` y `BypassHmacSecret`.
5. Ejecutar el cliente:

   ```bash
   dotnet run --project src/QualityLock.Client.WinForms -- --setup
   dotnet run --project src/QualityLock.Client.WinForms
   ```

## Bypass

Genera un `bypass.json` firmado para contingencia local:

```powershell
.\tools\Generate-Bypass.ps1 `
  -StationCode "ICT-01" `
  -IssuedBy "ADMIN001" `
  -Reason "Backend maintenance" `
  -ValidHours 4 `
  -HmacSecret "<mismo secreto del cliente>"
```

El archivo se escribe por defecto en `C:\ProgramData\QualityLock\bypass.json`.

## Estado offline

El cliente valida un gafete contra la cache local (`operator-cache.json`) cuando la API
no responde, y encola los eventos offline (`UnlockGranted`, `AutoLock`, `BypassUsed`)
como `StationEventRequest` serializados en `event-queue.jsonl`.

La cache se llena desde el **bootstrap** con **todos los usuarios activos de
`usuarios_sistema`** (no solo los que ya habian desbloqueado), de modo que cualquier
usuario valido puede desbloquear la estacion aunque este sin conexion. Al reproducir los
eventos encolados, el servidor vuelve a puentear el username a `operators_QA` para
atribuirlos correctamente.

La cache se **refresca automaticamente** cada
`AppConstants.OperatorCacheRefreshMinutes` (def. 15 min), evaluado en el tick del
heartbeat, para incluir usuarios nuevos sin reiniciar la estacion. Si el backend no
responde, se conserva la cache anterior. Tambien puede refrescarse **bajo demanda** desde
el menu de la bandeja del sistema (**🔄 Refrescar usuarios**), que confirma con un globo
cuantos usuarios quedaron en cache.

`OfflineSyncService` drena esa cola y la reproduce contra `/api/events` cuando vuelve
la conectividad (al arrancar y en cada heartbeat). El drenado es atomico (rename) y
re-encola lo que no logre enviar, de modo que nada se pierde entre reinicios.

El bypass local firmado (`bypass.json`) ahora **desbloquea efectivamente** la pantalla
cuando es valido y registra un evento `BypassUsed` para auditoria. En **modo seguro**
(tras varios crashes de arranque seguidos) el cliente no instala el bloqueo agresivo
—ni hook de teclado ni bloqueo del Administrador de tareas— para permitir la
recuperacion por un administrador.

## Pruebas

```bash
dotnet test QualityLock.slnx
```

Resultado verificado en esta revision: 11 pruebas superadas, 0 fallidas.
