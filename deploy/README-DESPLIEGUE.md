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
| `station/Build-StationInstaller.ps1` | maquina de build | Compila instaladores EXE x64/x86 con Inno Setup |
| `station/QualityLockStation.iss` | maquina de build | Script Inno Setup del instalador de estacion |
| `server/Build-ServerPackage.ps1` | maquina de build | Crea paquete portable de servidor |
| `New-Secrets.ps1` | una vez | Genera `Jwt:SigningKey` y `Auth:ClientApiKey` |
| `server/Install-Server-Service.ps1` | servidor (admin) | Instala la API como servicio de Windows |
| `server/Open-Firewall.ps1` | servidor (admin) | Abre el puerto 5080 en el firewall |
| `server/Uninstall-Server-Service.ps1` | servidor (admin) | Quita el servicio |
| `station/appsettings.station.template.json` | referencia | Plantilla de config por estacion |
| `station/Install-Station.ps1` | cada estacion | Instalacion script legacy; preferir el instalador EXE |
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
.\Publish-All.ps1 -OutDir C:\QualityLock\publish -AllClientRuntimes
```
Genera `C:\QualityLock\publish\Api`, `...\Client-win-x64` y `...\Client-win-x86`.
Self-contained por defecto (no hace falta instalar .NET en el servidor ni en las
estaciones).

Si falta Inno Setup para el paso de instaladores:

```powershell
winget install JRSoftware.InnoSetup
```

---

## Paso 2 — Generar las claves (una vez)

```powershell
cd deploy
.\New-Secrets.ps1 -OutFile C:\QualityLock\server.env
```
Edita `C:\QualityLock\server.env` y pon la **contrasena real (rotada) de MySQL** en
`ConnectionStrings__MySQL`. Anota el valor de `Auth__ClientApiKey`: lo necesitaras en
cada estacion si compilas instaladores sin `-SecretsFile`. El mismo archivo incluye
`BypassHmacSecret` para firmar bypass locales. **No subas `server.env` ni las claves a
git.**

---

## Paso 3 — Compilar instaladores de estacion

```powershell
cd deploy
.\station\Build-StationInstaller.ps1 `
    -PublishRoot C:\QualityLock\publish `
    -OutDir C:\QualityLock\installers `
    -SecretsFile C:\QualityLock\server.env
```

Esto genera `QualityLockStation-win-x64.exe` y `QualityLockStation-win-x86.exe`.
El builder toma `Auth__ClientApiKey` y `BypassHmacSecret` del `.env` para que el
instalador los muestre prellenados. Trata esos EXE como sensibles: contienen claves
compartidas de estaciones.

---

## Paso 4 — Instalar la API en el servidor (192.168.1.10)

Opcion recomendada: crear un paquete portable para copiar al servidor o ejecutar desde
la carpeta de control:

```powershell
cd deploy\server
.\Build-ServerPackage.ps1 `
    -PublishDir C:\QualityLock\publish\Api `
    -EnvFile C:\QualityLock\server.env `
    -OutDir C:\tmp\QualityLock-Server `
    -Zip
```

En el servidor, abre una **PowerShell como Administrador**:

```powershell
cd C:\tmp\QualityLock-Server
.\Install-QualityLockServer.ps1
.\Test-QualityLockServer.ps1
```

Alternativa manual: copia `C:\QualityLock\publish\Api` y `server.env` al servidor. En una
**PowerShell como Administrador**:

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

## Paso 5 — Instalar el cliente en cada estacion

Copia a la estacion el instalador que corresponda:

- Windows 64 bits: `C:\QualityLock\installers\QualityLockStation-win-x64.exe`
- Windows 32 bits: `C:\QualityLock\installers\QualityLockStation-win-x86.exe`

Ejecutalo como Administrador. El instalador pedira:

- `StationCode`
- `Linea`
- `ApiBaseUrl`
- `ClientApiKey` (prellenado si se compilo con `-SecretsFile`)
- `BypassHmacSecret` (prellenado si se compilo con `-SecretsFile`)
- `AdminPin`
- `AutoLockSeconds`
- `RequireScan`
- `ScanMaxAvgKeyMs`
- `BlockWindowProcessName` (por defecto `inctest`)
- `BlockWindowTitle` (por defecto `Error View`)

Al terminar, el cliente queda instalado con la tarea programada `QualityLockClient`.
La tarea se dispara al iniciar sesion y usa `RunLevel Highest`, de modo que QualityLock
puede cerrar ventanas elevadas como `Error View`. El instalador elimina las entradas
heredadas de `HKCU/HKLM\...\Run` para impedir que una instancia sin elevacion tome el
mutex antes que la tarea programada.

Para probarlo sin cerrar sesion:

```powershell
& "$env:ProgramFiles\QualityLock\Client\QualityLock.Client.WinForms.exe"
```

En equipos de 32 bits instalados en `Program Files (x86)`, usa:

```powershell
& "${env:ProgramFiles(x86)}\QualityLock\Client\QualityLock.Client.WinForms.exe"
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

### Estacion (`C:\ProgramData\QualityLock\appsettings.json`)

| Clave | Valor |
|---|---|
| `StationCode` | unico por estacion (debe existir en `stations_QA`) |
| `Linea` | linea de produccion; se guarda como `host_name` en `stations_QA` |
| `ApiBaseUrl` | `http://192.168.1.10:5080/` |
| `ClientApiKey` | igual a `Auth:ClientApiKey` del servidor |
| `BypassHmacSecret` | secreto para bypass firmados de contingencia |
| `AdminPin` | opcional; **respaldo offline** del login de admin |
| `AutoLockSeconds` | segundos de inactividad antes de bloquear |
| `RequireScan` | `true` para exigir escaner QR; `false` permite tecleo directo |
| `ScanMaxAvgKeyMs` | umbral de velocidad del escaner en ms por tecla |
| `WindowAccessGuard` | reglas para cerrar ventanas externas por rol/usuario |
| `QrInputFocus` | foco unico al iniciar sesion sobre el input QR de un programa externo |

La regla incluida para AT-01 cierra la ventana configurada si el usuario actual no tiene
rol `superadmin` o `Tecnico QA`. Por defecto vigila `Error View` del proceso
`inctest.exe`; si el proveedor cambia el nombre del EXE o de la ventana, ajusta
`ProcessName` y `WindowTitle` en `C:\ProgramData\QualityLock\appsettings.json`.
Tambien puedes abrir el setup de estacion y usar **Ventanas protegidas > Detectar** para
tomar el proceso/titulo desde las ventanas abiertas. Para permitir a un usuario concreto
sin cambiar su rol, agregalo en `AllowedUsers`.

Para enfocar el campo QR del programa externo al desbloquear, abre el setup de estacion y
usa **Input QR externo**: detecta la ventana, presiona **Usar**, luego **Inputs**,
selecciona el campo text y prueba con **Probar**. El foco solo se aplica una vez al
iniciar sesion; no redirige los QR ni mantiene un timer robando foco. Si no aparecen
inputs, usa **Punto**: el setup se minimiza, haces click sobre el input del programa
externo, toma automaticamente esa ventana como destino y guarda una coordenada relativa
para hacer click como respaldo.

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
- **Actualizar una estacion:** volver a correr el instalador x64/x86 correspondiente
  (reemplaza binarios y vuelve a pedir la configuracion local).
- **Refrescar usuarios en una estacion sin reiniciar:** menu de la bandeja →
  **Refrescar usuarios** (o esperar el refresco automatico cada 15 min).

---

## Seguridad

- Las estaciones **no** tienen credenciales de MySQL; solo `ClientApiKey` + JWT.
- `server.env` y las claves **nunca** van a git (ya cubierto por `.gitignore`).
- Limita el firewall a la subred local (`Open-Firewall.ps1` usa `LocalSubnet`).
- Rota `Auth:ClientApiKey` si se filtra: cambia el valor en el servidor y en todas las
  estaciones (deben coincidir).
