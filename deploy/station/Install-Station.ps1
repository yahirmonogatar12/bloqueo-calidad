<#
.SYNOPSIS
    Instala el cliente de bloqueo de QualityLock en una estacion y lo configura para
    arrancar automaticamente al iniciar sesion en Windows.

.DESCRIPTION
    - Copia los binarios publicados del cliente a la carpeta destino.
    - Escribe appsettings.json con el StationCode, Linea, ApiBaseUrl, ClientApiKey y
      secreto de bypass de ESTA estacion.
    - Registra el arranque automatico (clave Run del usuario, o tarea programada al logon).
    El cliente es una app de escritorio fullscreen: corre en la sesion interactiva del
    usuario, NO como servicio.

.PARAMETER SourceDir
    Carpeta con el cliente publicado (contiene QualityLock.Client.WinForms.exe).

.PARAMETER InstallDir
    Destino de instalacion. Ej: C:\QualityLock\Client

.PARAMETER StationCode
    Codigo unico de la estacion (debe existir y estar activa en stations_QA). Ej: ICT-01

.PARAMETER Linea
    Linea de produccion de la estacion. Se guarda como host_name en stations_QA.
    Ej: M1, M2. Tambien acepta el alias -Line.

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
        -StationCode ICT-01 -Linea M1 -ClientApiKey "<clave>" -BypassHmacSecret "<secreto>"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$SourceDir,
    [Parameter(Mandatory)] [string]$InstallDir,
    [Parameter(Mandatory)] [string]$StationCode,
    [Parameter(Mandatory)] [Alias('Line')] [string]$Linea,
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
$sourceDirPath = (Resolve-Path -LiteralPath $SourceDir).ProviderPath
$installDirPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($InstallDir)
$srcExe  = Join-Path $sourceDirPath $exeName
if (-not (Test-Path $srcExe)) { throw "No se encontro $srcExe. Publica el cliente primero (ver guia)." }

function Get-PeMachine {
    param([Parameter(Mandatory)] [string]$Path)

    $fs = [System.IO.File]::OpenRead($Path)
    try {
        $br = New-Object System.IO.BinaryReader($fs)
        if ($br.ReadUInt16() -ne 0x5A4D) { return "unknown" } # MZ

        $fs.Seek(0x3c, [System.IO.SeekOrigin]::Begin) | Out-Null
        $peHeaderOffset = $br.ReadInt32()
        $fs.Seek($peHeaderOffset, [System.IO.SeekOrigin]::Begin) | Out-Null

        if ($br.ReadUInt32() -ne 0x00004550) { return "unknown" } # PE\0\0
        $machine = $br.ReadUInt16()

        switch ($machine) {
            0x014c { "x86"; break }
            0x8664 { "x64"; break }
            0xaa64 { "arm64"; break }
            default { "0x{0:X4}" -f $machine }
        }
    }
    finally {
        $fs.Dispose()
    }
}

$clientArch = Get-PeMachine -Path $srcExe
$osArch = if ([Environment]::Is64BitOperatingSystem) { "x64" } else { "x86" }
Write-Host "Arquitectura detectada: cliente=$clientArch, Windows=$osArch" -ForegroundColor Cyan

if ($clientArch -eq "x64" -and -not [Environment]::Is64BitOperatingSystem) {
    throw "Este paquete de cliente es win-x64, pero esta estacion corre Windows de 32 bits. Publica el cliente con: .\Publish-All.ps1 -ClientRuntime win-x86"
}

# --- Copiar binarios ---
Write-Host "Copiando binarios a $installDirPath ..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $installDirPath | Out-Null
Copy-Item -Path (Join-Path $sourceDirPath '*') -Destination $installDirPath -Recurse -Force

# --- Escribir appsettings.json de la estacion ---
$config = [ordered]@{
    StationCode      = $StationCode
    Linea            = $Linea
    ApiBaseUrl       = $ApiBaseUrl
    BypassHmacSecret = $BypassHmacSecret
    AdminPin         = $AdminPin
    ClientApiKey     = $ClientApiKey
    AutoLockSeconds  = $AutoLockSeconds
    RequireScan      = $RequireScan
    ScanMaxAvgKeyMs  = $ScanMaxAvgKeyMs
}
$cfgPath = Join-Path $installDirPath "appsettings.json"
$config | ConvertTo-Json | Set-Content -Path $cfgPath -Encoding UTF8
Write-Host "Config escrita: $cfgPath (StationCode=$StationCode, Linea=$Linea)" -ForegroundColor Green

$installedExe = Join-Path $installDirPath $exeName

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
