# Despliegue de QualityLock

Arquitectura: **una API central** en el servidor `192.168.1.10` (la misma maquina que
MySQL) y **un cliente de bloqueo por estacion**. Las estaciones solo hablan con la API
por HTTP; nunca tocan MySQL directamente.

```
  SERVIDOR 192.168.1.10
  ┌───────────────────────────┐
  │ QualityLockApi (servicio) │──► MySQL mes_production (localhost)
  │ http://0.0.0.0:5080       │
  └─────────────▲─────────────┘
                │ HTTP (JWT Bearer)
     ┌──────────┼──────────┐
  ICT-01     FCT-01     PKG-01      ← cada estacion: solo el cliente WinForms
```

---

## Contenido de `deploy/`

| Archivo | Donde se ejecuta | Que hace |
|---|---|---|
| `Publish-All.ps1` | maquina de build | Publica API y cliente en Release |
| `New-Secrets.ps1` | una vez | Genera `Jwt:SigningKey` y `Auth:ClientApiKey` |
| `server/Install-Server-Service.ps1` | servidor (admin) | Instala la API como servicio de Windows |
| `server/Open-Firewall.ps1` | servidor (admin) | Abre el puerto 5080 en el firewall |
| `server/Uninstall-Server-Service.ps1` | servidor (admin) | Quita el servicio |
| `station/appsettings.station.template.json` | referencia | Plantilla de config por estacion |
| `station/Install-Station.ps1` | cada estacion | Copia el cliente y lo deja en autostart |
| `station/Uninstall-Station.ps1` | cada estacion | Quita el cliente |

---

## Requisitos previos (una sola vez, en MySQL)

1. Aplicar el esquema y la migracion de sesion unica:
   ```powershell
   mysql -h 192.168.1.10 -u mes_admin -p mes_production < ..\database\mysql\001_init.sql
   mysql -h 192.168.1.10 -u mes_admin -p mes_production < ..\database\mysql\002_seed.sql
   mysql -h 192.168.1.10 -u mes_admin -p mes_production < ..\database\mysql\003_unique_open_session.sql
   ```
   > Si las tablas `_QA` ya existen, basta con la `003`.

2. **Rotar la contrasena de `mes_admin`** (la anterior fue compartida; cambiala en MySQL
   y usa la nueva en el `.env` del servidor).

3. Verificar que cada estacion exista y este activa en `stations_QA`
   (`ICT-01`, `FCT-01`, ...). Si falta una, registrala desde el cliente con `--setup`.

---

## Paso 1 — Publicar (maquina de build)

```powershell
cd deploy
.\Publish-All.ps1 -OutDir C:\QualityLock\publish
```
Genera `C:\QualityLock\publish\Api` y `...\Client`. Self-contained por defecto (no hace
falta instalar .NET en el servidor ni en las estaciones).

Si alguna estacion corre Windows de 32 bits, publica el cliente como x86:

```powershell
cd deploy
.\Publish-All.ps1 -OutDir C:\QualityLock\publish -ClientRuntime win-x86
```

---

## Paso 2 — Generar las claves (una vez)

```powershell
cd deploy
.\New-Secrets.ps1 -OutFile C:\QualityLock\server.env
```
Edita `C:\QualityLock\server.env` y pon la **contrasena real (rotada) de MySQL** en
`ConnectionStrings__MySQL`. Anota el valor de `Auth__ClientApiKey`: lo necesitaras en
cada estacion. **No subas `server.env` ni las claves a git.**

---

## Paso 3 — Instalar la API en el servidor (192.168.1.10)

Copia `C:\QualityLock\publish\Api` y `server.env` al servidor. En una **PowerShell como
Administrador**:

```powershell
cd deploy\server
.\Install-Server-Service.ps1 -PublishDir C:\QualityLock\Api -EnvFile C:\QualityLock\server.env
.\Open-Firewall.ps1
```

El instalador crea el servicio `QualityLockApi` (arranque automatico, con reinicio ante
fallos), inyecta la configuracion como variables de entorno del servicio (no quedan en
archivos) y verifica `/health`. Comprueba desde otra maquina:

```powershell
Invoke-WebRequest http://192.168.1.10:5080/health   # -> 200 Healthy
```

---

## Paso 4 — Instalar el cliente en cada estacion

Copia `C:\QualityLock\publish\Client` a la estacion. Luego:

```powershell
cd deploy\station
.\Install-Station.ps1 `
    -SourceDir   C:\QualityLock\publish\Client `
    -InstallDir  C:\QualityLock\Client `
    -StationCode ICT-01 `
    -Linea       M1 `
    -ClientApiKey "<Auth__ClientApiKey del paso 2>" `
    -BypassHmacSecret "<secreto-de-bypass-compartido>"
```

Repite con el `StationCode` y la `Linea` que correspondan en cada equipo (`ICT-01/M1`,
`ICT-01/M2`, `FCT-01/M1`, ...). El cliente queda en **autostart al iniciar sesion**
(tarea programada). Para probarlo sin reiniciar:

```powershell
& C:\QualityLock\Client\QualityLock.Client.WinForms.exe
```

La pantalla se bloquea; se desbloquea **escaneando/tecleando el `username`** de un usuario
activo de `usuarios_sistema`.

---

## Configuracion (resumen)

### Servidor (`server.env` → variables del servicio)

| Variable | Ejemplo / nota |
|---|---|
| `ConnectionStrings__MySQL` | `Server=192.168.1.10;Port=3306;Database=mes_production;Uid=mes_admin;Pwd=...;` |
| `Jwt__SigningKey` | clave aleatoria >= 32 bytes (de `New-Secrets.ps1`) |
| `Auth__ClientApiKey` | clave compartida con las estaciones |
| `Admin__MinRoleLevel` | `3` (admin = rol con nivel >= 3; incluye Calidad/Tecnico QA) |
| `Admin__Roles__0` | opcional: nombre de rol extra que siempre es admin |
| `ASPNETCORE_URLS` | `http://0.0.0.0:5080` |
| `ASPNETCORE_ENVIRONMENT` | `Production` |

### Estacion (`appsettings.json`)

| Clave | Valor |
|---|---|
| `StationCode` | unico por estacion (debe existir en `stations_QA`) |
| `Linea` | linea de produccion; se guarda como `host_name` en `stations_QA` |
| `ApiBaseUrl` | `http://192.168.1.10:5080/` |
| `ClientApiKey` | igual a `Auth:ClientApiKey` del servidor |
| `BypassHmacSecret` | secreto para bypass firmados de contingencia |
| `AdminPin` | opcional; **respaldo offline** del login de admin |

---

## Quien puede hacer que (recordatorio)

- **Desbloquear:** cualquier usuario activo de `usuarios_sistema` (el gafete trae el
  `username`). Sin contrasena.
- **Admin (panel, detener servicio, override):** usuarios con rol de `nivel >= MinRoleLevel`
  (def. 3). Online valida usuario+contrasena contra `usuarios_sistema`; offline acepta el
  `AdminPin` local como respaldo.

---

## Operacion

- **Logs del servidor:** `C:\QualityLock\Api\logs\qualitylock-api-*.log` + Visor de eventos.
- **Reiniciar la API:** `Restart-Service QualityLockApi`.
- **Actualizar la API:** detener el servicio, reemplazar binarios en `C:\QualityLock\Api`,
  arrancar. La config (variables del servicio) se conserva.
- **Actualizar una estacion:** volver a correr `Install-Station.ps1` (reemplaza binarios y
  conserva la configuracion indicada: `StationCode`, `Linea` y secretos).
- **Refrescar usuarios en una estacion sin reiniciar:** menu de la bandeja →
  **Refrescar usuarios** (o esperar el refresco automatico cada 15 min).

---

## Seguridad

- Las estaciones **no** tienen credenciales de MySQL; solo `ClientApiKey` + JWT.
- `server.env` y las claves **nunca** van a git (ya cubierto por `.gitignore`).
- Limita el firewall a la subred local (`Open-Firewall.ps1` usa `LocalSubnet`).
- Rota `Auth:ClientApiKey` si se filtra: cambia el valor en el servidor y en todas las
  estaciones (deben coincidir).
