<#
.SYNOPSIS
    Instala la API de QualityLock como un Servicio de Windows en el servidor
    (192.168.1.10), escuchando en http://0.0.0.0:5080.

.DESCRIPTION
    - Crea el servicio "QualityLockApi" apuntando al ejecutable publicado.
    - Inyecta la configuracion (connection string, claves JWT/Auth, Admin) como
      variables de entorno DEL SERVICIO (registro), de modo que no quedan en archivos
      versionados.
    - Arranca el servicio y verifica /health.

    Debe ejecutarse en una PowerShell COMO ADMINISTRADOR, en el servidor.

.PARAMETER PublishDir
    Carpeta donde esta publicada la API (contiene QualityLock.Api.exe).
    Ej: C:\QualityLock\Api

.PARAMETER EnvFile
    Archivo .env generado por New-Secrets.ps1 (clave=valor por linea). Sus pares se
    cargan como variables de entorno del servicio. NO se copia al servidor de destino;
    solo se lee aqui.

.EXAMPLE
    .\Install-Server-Service.ps1 -PublishDir C:\QualityLock\Api -EnvFile C:\QualityLock\server.env
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$PublishDir,
    [Parameter(Mandatory)] [string]$EnvFile,
    [string]$ServiceName = "QualityLockApi",
    [string]$DisplayName = "QualityLock API",
    [string]$ListenUrl   = "http://0.0.0.0:5080"
)

$ErrorActionPreference = "Stop"

# --- Requiere admin ---
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
            ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { throw "Ejecuta esta ventana de PowerShell COMO ADMINISTRADOR." }

$exe = Join-Path $PublishDir "QualityLock.Api.exe"
if (-not (Test-Path $exe))     { throw "No se encontro $exe. Publica la API primero (ver guia)." }
if (-not (Test-Path $EnvFile)) { throw "No se encontro el archivo de entorno: $EnvFile" }

# --- Leer pares clave=valor del .env (ignora comentarios y lineas vacias) ---
$envPairs = @{}
foreach ($line in Get-Content $EnvFile) {
    $t = $line.Trim()
    if ($t -eq "" -or $t.StartsWith("#")) { continue }
    $i = $t.IndexOf("=")
    if ($i -lt 1) { continue }
    $envPairs[$t.Substring(0,$i).Trim()] = $t.Substring($i+1).Trim()
}
# Forzamos la URL de escucha y el entorno aunque no vengan en el .env
$envPairs["ASPNETCORE_URLS"] = $ListenUrl
if ([string]::IsNullOrWhiteSpace($envPairs["ASPNETCORE_ENVIRONMENT"])) {
    $envPairs["ASPNETCORE_ENVIRONMENT"] = "Production"
}

if (-not $envPairs.ContainsKey("ConnectionStrings__MySQL")) { throw "Falta ConnectionStrings__MySQL en el .env" }
if (-not $envPairs.ContainsKey("Jwt__SigningKey"))          { throw "Falta Jwt__SigningKey en el .env" }
if (-not $envPairs.ContainsKey("Auth__ClientApiKey"))       { throw "Falta Auth__ClientApiKey en el .env" }

# --- Si ya existe, lo detenemos y borramos para reinstalar limpio ---
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Servicio existente: deteniendo y eliminando..." -ForegroundColor Yellow
    if ($existing.Status -ne 'Stopped') { Stop-Service $ServiceName -Force }
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

# --- Crear el servicio ---
Write-Host "Creando servicio '$ServiceName'..." -ForegroundColor Cyan
New-Service -Name $ServiceName -BinaryPathName "`"$exe`"" -DisplayName $DisplayName `
    -Description "API central de QualityLock (bloqueo de estaciones de calidad)." `
    -StartupType Automatic | Out-Null

# --- Inyectar las variables de entorno del servicio en el registro (REG_MULTI_SZ) ---
$regPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
$envBlock = $envPairs.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }
New-ItemProperty -Path $regPath -Name "Environment" -PropertyType MultiString -Value $envBlock -Force | Out-Null
Write-Host "Configuracion inyectada como variables de entorno del servicio." -ForegroundColor Green

# --- Recuperacion automatica ante fallos ---
sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/5000/restart/10000 | Out-Null

# --- Arrancar y verificar ---
Write-Host "Arrancando servicio..." -ForegroundColor Cyan
Start-Service $ServiceName
Start-Sleep -Seconds 4

$port = ([Uri]($ListenUrl -replace '0\.0\.0\.0','localhost')).Port
$ok = $false
foreach ($i in 1..15) {
    try {
        $r = Invoke-WebRequest -Uri "http://localhost:$port/health" -UseBasicParsing -TimeoutSec 2
        if ($r.StatusCode -eq 200) { $ok = $true; break }
    } catch { Start-Sleep -Milliseconds 800 }
}

if ($ok) {
    Write-Host ""
    Write-Host "OK - QualityLockApi instalado y respondiendo en $ListenUrl" -ForegroundColor Green
    Write-Host "Recuerda abrir el puerto en el firewall (ver Open-Firewall.ps1)." -ForegroundColor Yellow
} else {
    Write-Host ""
    Write-Host "El servicio se creo pero /health no respondio. Revisa el visor de eventos" -ForegroundColor Red
    $logDir = Join-Path $PublishDir "logs"
    Write-Host "y los logs en $logDir" -ForegroundColor Red
}
