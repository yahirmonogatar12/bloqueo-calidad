<#
.SYNOPSIS
    Arranca la API de QualityLock en primer plano (para el MES Control Center).

.DESCRIPTION
    Pensado para ejecutarse como un "comando PowerShell" dentro del MES Control Center,
    junto al resto de los procesos del stack (MES web, n8n, DMS_API, ...). NO instala un
    servicio de Windows: simplemente carga la configuracion desde server.env y lanza el
    ejecutable publicado, que se queda escuchando hasta que el Control Center lo detenga.

    La configuracion (connection string, claves JWT/Auth, Admin) vive en server.env, NO
    en este script ni en la UI del Control Center.

.PARAMETER ApiDir
    Carpeta con QualityLock.Api.exe. Por defecto la subcarpeta 'Api' junto a este script.

.PARAMETER EnvFile
    Archivo .env con pares CLAVE=VALOR. Por defecto 'server.env' junto a este script.

.EXAMPLE
    # En el MES Control Center, comando PowerShell:
    powershell -NoProfile -ExecutionPolicy Bypass -File "C:\...\QualityLock-Server\Start-QualityLockApi.ps1"
#>
[CmdletBinding()]
param(
    [string]$ApiDir,
    [string]$EnvFile,
    [string]$ListenUrl = "http://0.0.0.0:5080"
)
$ErrorActionPreference = "Stop"

# Rutas por defecto: relativas a la carpeta de este script.
$base = $PSScriptRoot
if (-not $ApiDir)  { $ApiDir  = Join-Path $base "Api" }
if (-not $EnvFile) { $EnvFile = Join-Path $base "server.env" }

$exe = Join-Path $ApiDir "QualityLock.Api.exe"
if (-not (Test-Path $exe))     { throw "No se encontro $exe" }
if (-not (Test-Path $EnvFile)) { throw "No se encontro $EnvFile" }

# Cargar server.env como variables de entorno del proceso.
foreach ($line in Get-Content $EnvFile) {
    $t = $line.Trim()
    if ($t -eq "" -or $t.StartsWith("#")) { continue }
    $i = $t.IndexOf("=")
    if ($i -lt 1) { continue }
    $key = $t.Substring(0, $i).Trim()
    $val = $t.Substring($i + 1).Trim()
    [Environment]::SetEnvironmentVariable($key, $val, "Process")
}

# Forzar URL de escucha y entorno aunque no vengan en el .env.
$env:ASPNETCORE_URLS = $ListenUrl
if ([string]::IsNullOrWhiteSpace($env:ASPNETCORE_ENVIRONMENT)) {
    $env:ASPNETCORE_ENVIRONMENT = "Production"
}

Write-Host "Iniciando QualityLock API en $ListenUrl (entorno: $env:ASPNETCORE_ENVIRONMENT)..."

# Ejecutar EN PRIMER PLANO: el Control Center sigue el proceso y captura su salida.
& $exe
