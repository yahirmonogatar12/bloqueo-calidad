<#
.SYNOPSIS
    Instala el cliente de bloqueo de QualityLock en una estacion y lo configura para
    arrancar automaticamente al iniciar sesion en Windows.

.DESCRIPTION
    - Copia los binarios publicados del cliente a la carpeta destino.
    - Escribe appsettings.json con el StationCode, ApiBaseUrl, ClientApiKey y secreto de
      bypass de ESTA estacion.
    - Registra el arranque automatico (clave Run del usuario, o tarea programada al logon).
    El cliente es una app de escritorio fullscreen: corre en la sesion interactiva del
    usuario, NO como servicio.

.PARAMETER SourceDir
    Carpeta con el cliente publicado (contiene QualityLock.Client.WinForms.exe).

.PARAMETER InstallDir
    Destino de instalacion. Ej: C:\QualityLock\Client

.PARAMETER StationCode
    Codigo unico de la estacion (debe existir y estar activa en stations_QA). Ej: ICT-01

.PARAMETER ApiBaseUrl
    URL del servidor. Por defecto http://192.168.1.10:5080/

.PARAMETER ClientApiKey
    Misma clave configurada en el servidor (Auth:ClientApiKey).

.PARAMETER BypassHmacSecret
    Secreto HMAC para los bypass firmados de contingencia (igual al usado por
    Generate-Bypass.ps1).

.PARAMETER Autostart
    Mecanismo de arranque: 'Run' (clave Run del usuario actual) o 'Task' (tarea
    programada al logon, sobrevive a varios usuarios). Por defecto 'Task'.

.EXAMPLE
    .\Install-Station.ps1 -SourceDir .\publish\Client -InstallDir C:\QualityLock\Client `
        -StationCode ICT-01 -ClientApiKey "<clave>" -BypassHmacSecret "<secreto>"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$SourceDir,
    [Parameter(Mandatory)] [string]$InstallDir,
    [Parameter(Mandatory)] [string]$StationCode,
    [Parameter(Mandatory)] [string]$ClientApiKey,
    [Parameter(Mandatory)] [string]$BypassHmacSecret,
    [string]$ApiBaseUrl = "http://192.168.1.10:5080/",
    [int]$AutoLockSeconds = 300,
    [string]$AdminPin = "ISEMM2026",
    [bool]$RequireScan = $true,
    [int]$ScanMaxAvgKeyMs = 40,
    [ValidateSet('Run','Task')] [string]$Autostart = 'Task'
)
$ErrorActionPreference = "Stop"

$exeName = "QualityLock.Client.WinForms.exe"
$srcExe  = Join-Path $SourceDir $exeName
if (-not (Test-Path $srcExe)) { throw "No se encontro $srcExe. Publica el cliente primero (ver guia)." }

# --- Copiar binarios ---
Write-Host "Copiando binarios a $InstallDir ..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item -Path (Join-Path $SourceDir '*') -Destination $InstallDir -Recurse -Force

# --- Escribir appsettings.json de la estacion ---
$config = [ordered]@{
    StationCode      = $StationCode
    ApiBaseUrl       = $ApiBaseUrl
    BypassHmacSecret = $BypassHmacSecret
    AdminPin         = $AdminPin
    ClientApiKey     = $ClientApiKey
    AutoLockSeconds  = $AutoLockSeconds
    RequireScan      = $RequireScan
    ScanMaxAvgKeyMs  = $ScanMaxAvgKeyMs
}
$cfgPath = Join-Path $InstallDir "appsettings.json"
$config | ConvertTo-Json | Set-Content -Path $cfgPath -Encoding UTF8
Write-Host "Config escrita: $cfgPath (StationCode=$StationCode)" -ForegroundColor Green

$installedExe = Join-Path $InstallDir $exeName

# --- Arranque automatico ---
switch ($Autostart) {
    'Run' {
        $runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
        New-ItemProperty -Path $runKey -Name "QualityLockClient" -Value "`"$installedExe`"" -PropertyType String -Force | Out-Null
        Write-Host "Autostart registrado en la clave Run del usuario actual." -ForegroundColor Green
    }
    'Task' {
        $taskName = "QualityLockClient"
        Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue | Unregister-ScheduledTask -Confirm:$false -ErrorAction SilentlyContinue
        $action  = New-ScheduledTaskAction -Execute $installedExe
        $trigger = New-ScheduledTaskTrigger -AtLogOn
        $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -ExecutionTimeLimit ([TimeSpan]::Zero)
        Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Settings $settings `
            -Description "Pantalla de bloqueo QualityLock para la estacion $StationCode" -Force | Out-Null
        Write-Host "Autostart registrado como tarea programada '$taskName' (al iniciar sesion)." -ForegroundColor Green
    }
}

Write-Host ""
Write-Host "Estacion '$StationCode' instalada." -ForegroundColor Green
Write-Host "Para iniciar ahora sin reiniciar:  & '$installedExe'"
Write-Host "Para configurar/cambiar la estacion:  & '$installedExe' --setup"
