# QualityLock - Arquitectura y Operación

Este documento detalla el diseño de software, la base de datos, los flujos de datos en línea y fuera de línea, y los mecanismos de seguridad/recuperación del sistema **QualityLock**.

---

## 1. Estructura de Proyectos (Capas)

QualityLock está estructurado bajo principios de arquitectura en capas desacopladas, lo que permite separar la interfaz de usuario de las reglas de negocio y del acceso a datos.

```mermaid
graph TD
    classDef client fill:#e1f5fe,stroke:#039be5,stroke-width:2px;
    classDef api fill:#e8f5e9,stroke:#43a047,stroke-width:2px;
    classDef db fill:#fff3e0,stroke:#fb8c00,stroke-width:2px;
    classDef ext fill:#eceff1,stroke:#546e7a,stroke-width:2px;

    Operator[Operador de Estación] -->|Escanea Gafete / Hotkeys| WinClient[WinForms Client<br/>QualityLock.Client.WinForms]:::client
    Admin[Administrador / Calidad] -->|Login con PIN / bypass.json| WinClient

    WinClient -->|1. Valida API Key/JWT / HTTPS / JSON| WebApi[ASP.NET Core Web API<br/>QualityLock.Api]:::api
    WinClient -->|2. Escribe archivos locales| LocalFS[(Filesystem Local<br/>C:\ProgramData\QualityLock)]:::client
    WinClient -->|3. Automatización Win32| ExtApp[Software de Prueba MES<br/>e.g., inctest.exe]:::ext

    WebApi -->|Acceso Dapper / MySQL| MySQL[(Base de Datos MySQL<br/>mes_production)]:::db
    MySQL -->|usuarios_sistema / roles| SharedMES[BD MES Compartida]:::db

    subgraph Estación de Trabajo Windows
        WinClient
        LocalFS
        ExtApp
    end
```

| Capa | Carpeta / Link | Responsabilidad |
|---|---|---|
| **Shared** | [QualityLock.Shared](file:///c:/Users/yahir/OneDrive/Escritorio/MES/Bloqueo%20calidad/src/QualityLock.Shared) | Contiene DTOs, enums, constantes físicas, nombres de rutas y constantes compartidas. |
| **Domain** | [QualityLock.Domain](file:///c:/Users/yahir/OneDrive/Escritorio/MES/Bloqueo%20calidad/src/QualityLock.Domain) | Define las entidades puras del negocio: operadores, estaciones, sesiones de uso, auditoría, eventos. |
| **Application** | [QualityLock.Application](file:///c:/Users/yahir/OneDrive/Escritorio/MES/Bloqueo%20calidad/src/QualityLock.Application) | Casos de uso prácticos e interfaces (reglas de negocio de inicio/fin de sesión, validación de gafete). |
| **Infrastructure** | [QualityLock.Infrastructure](file:///c:/Users/yahir/OneDrive/Escritorio/MES/Bloqueo%20calidad/src/QualityLock.Infrastructure) | Implementaciones físicas de acceso a datos utilizando **MySQL 8** + **Dapper**. |
| **Api** | [QualityLock.Api](file:///c:/Users/yahir/OneDrive/Escritorio/MES/Bloqueo%20calidad/src/QualityLock.Api) | Backend HTTP REST construido en **ASP.NET Core 9**, seguridad JWT y Serilog. |
| **Client.WinForms** | [QualityLock.Client.WinForms](file:///c:/Users/yahir/OneDrive/Escritorio/MES/Bloqueo%20calidad/src/QualityLock.Client.WinForms) | Cliente gráfico de escritorio que actúa como pantalla de bloqueo agresivo, lectura de escáner y bypass local. |

---

## 2. Diagrama de Entidad-Relación (ERD) y Diseño de Base de Datos

Este diagrama representa la estructura de las tablas de base de datos MySQL (con el sufijo `_QA`) y la vista de historial calculada, además de sus relaciones y derivaciones.

```mermaid
erDiagram
    operators_QA {
        int id PK
        varchar badge_code UK
        varchar employee_number UK
        varchar display_name
        tinyint is_active
        tinyint is_admin
        datetime created_at_utc
        datetime updated_at_utc
    }
    stations_QA {
        int id PK
        varchar station_code UK
        varchar station_name
        varchar station_type
        varchar host_name UK "e.g., Linea M1, M2"
        tinyint is_active
        datetime created_at_utc
        datetime updated_at_utc
    }
    station_permissions_QA {
        int operator_id PK, FK
        int station_id PK, FK
        tinyint can_operate
        datetime created_at_utc
        datetime updated_at_utc
    }
    station_sessions_QA {
        char-36 id PK
        int station_id FK
        int operator_id FK
        datetime started_at_utc
        datetime ended_at_utc
        varchar status "Open|Closed|ForcedClosed|OfflinePending"
        tinyint started_online
        tinyint ended_online
        varchar correlation_id
        int open_station_id UK "Generated: station_id if Open, else NULL"
    }
    station_events_QA {
        char-36 id PK
        int station_id FK
        int operator_id FK
        char-36 session_id FK
        varchar event_type
        datetime event_at_utc
        text details_json
        varchar source "API|Client"
        varchar correlation_id
    }
    admin_overrides_QA {
        char-36 id PK
        int station_id FK
        int admin_operator_id FK
        int target_operator_id FK
        varchar reason
        text comments
        tinyint approved
        datetime created_at_utc
    }
    client_heartbeats_QA {
        char-36 id PK
        int station_id FK
        datetime sent_at_utc
        varchar client_version
        tinyint is_safe_mode
        datetime last_activity_at_utc
        text details_json
    }
    historial_estaciones_QA {
        varchar session_id PK
        varchar estacion
        varchar linea
        varchar tipo
        varchar nombre_estacion
        varchar usuario
        varchar username
        date fecha
        time hora_entrada
        time hora_salida
        int duracion_seg
        varchar duracion
        varchar estado
        tinyint inicio_online
        tinyint fin_online
        datetime inicio_local
        datetime fin_local
    }

    operators_QA ||--o{ station_permissions_QA : "tiene"
    stations_QA ||--o{ station_permissions_QA : "asociada_a"
    operators_QA ||--o{ station_sessions_QA : "inicia"
    stations_QA ||--o{ station_sessions_QA : "hospeda"
    operators_QA ||--o{ station_events_QA : "genera"
    stations_QA ||--o{ station_events_QA : "registra"
    station_sessions_QA ||--o{ station_events_QA : "agrupa"
    stations_QA ||--o{ admin_overrides_QA : "aplica_en"
    operators_QA ||--o{ admin_overrides_QA : "autoriza"
    stations_QA ||--o{ client_heartbeats_QA : "recibe"
    station_sessions_QA ||..o{ historial_estaciones_QA : "derivada_de"
    station_events_QA ||..o{ historial_estaciones_QA : "derivada_de"
```

> [!NOTE]
> La tabla `usuarios_sistema` es la fuente de verdad del MES (de solo lectura para QualityLock). Para mantener la integridad referencial de llaves foráneas (`FK`), la API crea o actualiza filas de forma idempotente en `operators_QA` vinculándolas mediante el `username` del MES.
>
> [003_unique_open_session.sql](file:///c:/Users/yahir/OneDrive/Escritorio/MES/Bloqueo%20calidad/database/mysql/003_unique_open_session.sql) introduce la columna autogenerada `open_station_id` con un índice único para garantizar que a nivel físico no existan múltiples sesiones abiertas concurrentes para la misma estación.
>
> La unicidad de la estación se define como `(station_code, host_name)` según [005_station_unique_code_line.sql](file:///c:/Users/yahir/OneDrive/Escritorio/MES/Bloqueo%20calidad/database/mysql/005_station_unique_code_line.sql), de modo que un mismo código de máquina puede convivir en diferentes líneas físicas.

### Vista de Historial Unificado (historial_estaciones_QA)

La vista [historial_estaciones_QA](file:///c:/Users/yahir/OneDrive/Escritorio/MES/Bloqueo%20calidad/database/mysql/004_historial_view.sql) unifica dos orígenes de datos en el sistema para proporcionar un reporte consolidado de tiempo de uso y tiempos de ajuste en piso de manufactura:
1. **Sesiones Ordinarias**: Registros en `station_sessions_QA` (inicios de sesión estándar online u offline).
2. **Sesiones de Ajuste**: Provenientes de eventos de tipo `WindowClosed` en `station_events_QA`. Estos se interpretan como periodos de "Ajuste" donde la estación estuvo temporalmente abierta o modificada y la duración se extrae dinámicamente de la columna JSON `details_json` (propiedad `OpenSeconds`).

```mermaid
graph TD
    classDef table fill:#fff3e0,stroke:#fb8c00,stroke-width:2px;
    classDef view fill:#e8f5e9,stroke:#43a047,stroke-width:2px;
    classDef proc fill:#eceff1,stroke:#546e7a,stroke-width:2px;

    SS[station_sessions_QA]:::table -->|1. Sesiones estándar<br/>started_at / ended_at| Union[UNION ALL]
    
    SE[station_events_QA]:::table -->|2. Eventos filtrados<br/>event_type = 'WindowClosed'| Filter{Filtro WindowClosed}:::proc
    Filter -->|details_json| JT[JSON_TABLE]:::proc
    JT -->|Parsea $.OpenSeconds| Calc[Calcular inicio_local:<br/>event_at_utc - OpenSeconds]:::proc
    Calc -->|Mapeado como 'Ajuste'| Union
    
    ST[stations_QA]:::table -->|Relación station_id| Joint[JOIN Estación e Info Operador]:::proc
    OP[operators_QA]:::table -->|Relación operator_id| Joint
    
    Union --> Joint
    Joint -->|Convertir a hora local CDMX<br/>CONVERT_TZ UTC a -06:00| HistView[historial_estaciones_QA<br/>Vista de Base de Datos]:::view
```

* **Cálculo de Tiempos**: Para los registros de "Ajuste", la fecha y hora de entrada se calculan restando la duración del ajuste (`OpenSeconds`) al instante en que ocurrió el evento (`event_at_utc`), mientras que la hora de salida corresponde a `event_at_utc`.
* **Conversión de Zonas Horarias**: La vista utiliza `CONVERT_TZ` para transformar los valores de hora guardados en UTC a hora local de la planta (por defecto UTC-6, CDMX).

---

## 3. Flujo de Inicialización del Cliente WinForms

Al arrancar en Windows, el cliente realiza chequeos de salud previa, valida configuraciones básicas y determina si se requiere inicializar la interfaz de bloqueo completo.

```mermaid
sequenceDiagram
    autonumber
    actor Admin as Administrador
    participant OS as Sistema Operativo
    participant Client as QualityLock Client (Main)
    participant Reg as Registro Windows
    participant Config as appsettings.json
    participant API as QualityLock API
    participant FS as Filesystem Local

    Admin->>OS: Inicia QualityLock.exe
    OS->>Client: Main(args)
    Client->>Client: Mutex check (Single Instance)
    Client->>Reg: Restaura DisableTaskMgr (evita bloqueo huérfano)
    Client->>Config: Lee configuración (StationCode, ApiBaseUrl, etc.)
    Client->>Client: SafeModeService: Carga estado / evalúa crashes previos
    
    alt Setup mode (arg --setup o falta StationCode)
        Client->>Admin: Muestra StationSetupForm
        Admin->>Client: Guarda configuración
        Client->>Config: Escribe appsettings.json
    end

    Client->>API: Task.Run: RefreshNowAsync() (Get bootstrap data - 5s timeout)
    alt API Responde (Online)
        API-->>Client: Lista de operadores activos y permisos
        Client->>FS: Guarda operator-cache.json
    else API Timeout / Caída (Offline)
        Client->>Client: Usa operator-cache.json previo
    end

    Client->>OS: Lanza LockForm (pantalla completa, topmost)
    Client->>Reg: Escribe DisableTaskMgr = 1 (Bloquea Administrador de Tareas)
    Client->>Client: Activa Keyboard Hooks (Alt+Tab, Alt+F4, Win)
    Client->>Client: Inicia timers (Heartbeat, AutoLock, WindowGuard)
```

- **Restauración Inicial**: Al arrancar, el cliente siempre borra cualquier rastro de `DisableTaskMgr` en el registro. Si la aplicación crasheó a mitad de sesión, esto asegura que el Administrador de tareas no quede permanentemente bloqueado.
- **Safe Mode**: Si el sistema detecta que el cliente se cae de manera iterativa al inicio (3 crashes consecutivos en menos de 5 min), no instala el hook de teclado agresivo ni modifica el registro, facilitando que el administrador del sistema cierre o repare el programa. Ver [SafeModeService.cs](file:///c:/Users/yahir/OneDrive/Escritorio/MES/Bloqueo%20calidad/src/QualityLock.Client.WinForms/Services/SafeModeService.cs).

---

## 4. Ciclo de Vida de Sesión (Validación Online vs. Cache Offline)

El escaneo del gafete es interceptado por un control de texto invisible en el formulario de bloqueo. Tras medir los intervalos del teclado (asegurando que es una entrada rápida simulada por un escáner y no entrada humana manual), se valida al operador.

```mermaid
sequenceDiagram
    autonumber
    actor Operador
    participant Client as LockForm / Client
    participant API as QualityLock API
    participant DB as MySQL DB
    participant Cache as operator-cache.json
    participant Queue as event-queue.jsonl
    participant Ext as Software de Prueba (e.g., inctest.exe)

    Operador->>Client: Desliza Gafete (Scanner emula teclado)
    Client->>Client: Valida velocidad del tecleo (RequireScan)
    Client->>Client: Verifica estado de red (GET /health)

    alt RED ONLINE
        Client->>API: POST /api/badges/validate { BadgeCode, StationCode }
        API->>DB: Consulta usuario_sistema y permisos_QA
        DB-->>API: Usuario activo y con permisos
        API-->>Client: Resultado: Allowed (Usuario, Nivel, etc.)
        
        Client->>API: POST /api/sessions/start { StationCode, OperatorId }
        API->>DB: Registra sesión en station_sessions_QA (Estado: Open)
        API-->>Client: Retorna SessionId
        
        Client->>Client: Oculta LockForm y detiene bloqueo de teclado
        Client->>Client: Restaura DisableTaskMgr = 0
        Client->>Ext: Configura Foco QR / Cierra ventanas (WindowAccessGuard)
        
        Note over Client, Operador: Operador trabaja en la máquina. Timer de inactividad corre.

        alt Inactividad detectada o Clic en "Cerrar Sesión"
            Client->>API: POST /api/sessions/end { SessionId }
            API->>DB: Cierra sesión (ended_at_utc, Estado: Closed)
            API-->>Client: Confirmación
            Client->>Client: Reactiva LockForm, hooks de teclado y DisableTaskMgr = 1
        end

    else RED OFFLINE
        Client->>Cache: Busca Badge en operator-cache.json
        alt Encontrado en Caché Local
            Cache-->>Client: Usuario válido (Offline)
            Client->>Queue: Encola evento UnlockGranted / LocalSessionStart
            
            Client->>Client: Oculta LockForm y detiene bloqueo de teclado
            Client->>Client: Restaura DisableTaskMgr = 0
            Client->>Ext: Aplica Foco QR / Guarda reglas locales
            
            Note over Client, Operador: Sesión Offline en curso.

            alt Inactividad detectada o Clic en "Cerrar Sesión"
                Client->>Queue: Encola evento SessionEndOffline / AutoLock
                Client->>Client: Reactiva LockForm, hooks y DisableTaskMgr = 1
            end
        else No encontrado en Caché Local
            Cache-->>Client: No encontrado / Inactivo
            Client->>Client: Muestra mensaje "Gafete no registrado o sin permisos"
        end
    end
```

---

## 5. Sincronización Offline y Flujo de Heartbeats

Cuando la conexión con la API falla, los eventos de desbloqueo, bloqueo automático y uso de bypass se escriben inmediatamente como líneas JSON independientes en `event-queue.jsonl`. Un servicio en segundo plano sincroniza estos eventos una vez detectada la red en línea.

```mermaid
sequenceDiagram
    autonumber
    participant Timer as Timer (60s tick)
    participant Sync as OfflineSyncService
    participant Local as LocalStateService / Filesystem
    participant API as QualityLock API
    participant DB as MySQL DB

    Timer->>Sync: Dispara FlushAsync()
    Sync->>API: GET /health (Verifica disponibilidad)
    
    alt API disponible (Reconexión exitosa)
        API-->>Sync: Status: OK
        Sync->>Local: DrainEventQueue() (Obtiene y vacía event-queue.jsonl)
        Local-->>Sync: Lista de eventos offline serializados
        
        Sync->>API: POST /api/events (Envía lote de eventos)
        API->>DB: Inserta en station_events_QA y crea/actualiza operators_QA
        API->>DB: Sincroniza/cierra sesiones pendientes offline
        API-->>Sync: Retorna 200 OK (Sincronizado)
        
        Note over Sync, Local: Si falla el envío, re-encola eventos en event-queue.jsonl.
        
        Sync->>API: GET /api/stations/{stationCode}/bootstrap (Actualiza caché)
        API->>DB: Consulta operadores activos y permisos
        DB-->>API: Lista de operadores
        API-->>Sync: Datos de Bootstrap
        Sync->>Local: Guarda operator-cache.json actualizado
    else API no disponible
        Sync->>Sync: Cancela sincronización (espera al siguiente tick)
    end
```

- La lógica de lectura y purga atómica de eventos encolados se maneja en [OfflineSyncService.cs](file:///c:/Users/yahir/OneDrive/Escritorio/MES/Bloqueo calidad/src/QualityLock.Client.WinForms/Services/OfflineSyncService.cs).

---

## 6. Recuperación Local y Bypass de Contingencia

En situaciones extremas donde no hay conexión y un operador requiere desbloquear la estación, se puede colocar el archivo de firma local `bypass.json` en `C:\ProgramData\QualityLock\`. El cliente validará digitalmente el archivo antes de liberar el sistema.

```mermaid
sequenceDiagram
    autonumber
    actor Admin as Administrador
    participant Panel as AdminPanelForm (Offline UI)
    participant BypassSvc as BypassService
    participant LocalFS as Filesystem (bypass.json)
    participant Lock as LockForm

    Admin->>Lock: Presiona Hotkey Admin (Ctrl+Alt+A o Shift+F10)
    Lock->>Panel: Abre AdminPanelForm (modo offline o backend caído)
    
    alt Uso de bypass.json
        Admin->>LocalFS: Coloca bypass.json (generado vía PowerShell)
        Admin->>Panel: Clic en "Validar Bypass"
        Panel->>BypassSvc: ValidateBypass(stationCode)
        BypassSvc->>LocalFS: Lee bypass.json
        LocalFS-->>BypassSvc: bypass.json { StationCode, ExpiresAtUtc, Signature, Reason, IssuedBy }
        BypassSvc->>BypassSvc: Calcula HMAC-SHA256 y valida firma, expiración y estación
        BypassSvc-->>Panel: Retorna (Valido, Motivo)
        
        alt Firma Válida
            Panel->>Lock: Ordena Desbloqueo por Bypass
            Lock->>Lock: Desactiva LockForm, hooks de teclado y DisableTaskMgr
            Lock->>Lock: Registra evento BypassUsed en event-queue.jsonl (para auditoría posterior)
            Panel->>Admin: Estación desbloqueada temporalmente
        else Firma Inválida / Expirado
            BypassSvc-->>Panel: Retorna (Invalido, Explicación)
            Panel->>Admin: Muestra error de bypass
        end
    end
```

- El script en [Generate-Bypass.ps1](file:///c:/Users/yahir/OneDrive/Escritorio/MES/Bloqueo%20calidad/tools/Generate-Bypass.ps1) genera firmas HMAC válidas usando la misma clave configurada en el cliente (`BypassHmacSecret`).

---

## 7. Máquina de Estados del Cliente

El estado de la aplicación gráfica del cliente transiciona en base al siguiente diagrama:

```mermaid
stateDiagram-v2
    [*] --> Startup
    Startup --> SetupMode : Faltan datos / arg --setup
    Startup --> SafeMode : >3 crashes en arranque
    Startup --> Locked : Arranque normal y configurado
    
    SetupMode --> Locked : Configuración guardada con éxito
    SetupMode --> [*] : Cancelar / Salir
    
    Locked --> Unlocked : Gafete Válido (Online/Offline)
    Locked --> Unlocked : Bypass Válido (HMAC-SHA256)
    Locked --> Unlocked : PIN de Admin Correcto
    
    Unlocked --> Locked : Inactividad (AutoLockSeconds)
    Unlocked --> Locked : Clic en "Cerrar Sesión"
    Unlocked --> Locked : Desconexión forzada / evento remoto
    
    SafeMode --> Locked : Reseteo de safe mode por Admin
    SafeMode --> [*] : Apagar servicio
```

---

## 8. Automatización de Foco QR y Guardián de Ventanas

Cuando la estación está desbloqueada y el operador está trabajando, QualityLock automatiza y asegura la estación mediante llamadas de bajo nivel a la API de Windows (Win32 P/Invoke).

```mermaid
graph TD
    classDef action fill:#fff3e0,stroke:#fb8c00,stroke-width:2px;
    classDef dec fill:#f3e5f5,stroke:#8e24aa,stroke-width:2px;
    classDef start fill:#e8f5e9,stroke:#43a047,stroke-width:2px;

    Start([Estación Desbloqueada & Sesión Activa]):::start --> QR[QR Input Focus Service]
    
    subgraph QR Input Focus
        QR --> IsQR{¿Configurado?}:::dec
        IsQR -- Sí --> FindWin{¿Busca control Edit?}:::dec
        FindWin -- Encontrado --> FocusEdit[Usa Win32 SetFocus al control textbox]:::action
        FindWin -- No Encontrado --> RelativeClick[Usa coordenadas relativas y envía clic físico]:::action
        IsQR -- No --> NoAction[No realiza acción de foco]
    end

    Start --> GuardTimer[Window Guard Timer - Cada 1-2s]
    
    subgraph Window Access Guard
        GuardTimer --> EnumWindows[Enumera Ventanas y Procesos Abiertos]:::action
        EnumWindows --> MatchRule{¿Coincide con regla de ventana protegida?}:::dec
        MatchRule -- Sí --> CheckRole{¿Usuario actual tiene rol permitido?<br/>(e.g., Tecnico QA, superadmin)}:::dec
        CheckRole -- No --> ActionRule{Acción configurada}:::dec
        ActionRule -- Close --> CloseWindow[Cierra o destruye ventana externa]:::action
        ActionRule -- Overlay --> ShowOverlay[Muestra pantalla de overlay restrictiva]:::action
        CheckRole -- Sí --> AllowWindow[Permite que la ventana siga abierta]
        MatchRule -- No --> AllowWindow
    end
```

- **Focus QR**: El servicio [ExternalInputFocusService.cs](file:///c:/Users/yahir/OneDrive/Escritorio/MES/Bloqueo%20calidad/src/QualityLock.Client.WinForms/Services/ExternalInputFocusService.cs) maximiza la ventana de la aplicación de prueba, busca el control text-box objetivo para hacer foco, o realiza un clic relativo sobre las coordenadas configuradas por el usuario.
- **Window Access Guard**: El servicio [ExternalWindowGuardService.cs](file:///c:/Users/yahir/OneDrive/Escritorio/MES/Bloqueo%20calidad/src/QualityLock.Client.WinForms/Services/ExternalWindowGuardService.cs) monitorea constantemente el árbol de procesos de Windows y cierra o cubre con una advertencia roja (`WindowAuthorizationOverlay`) cualquier pantalla prohibida (como el visor de errores en bypass del software de pruebas).
