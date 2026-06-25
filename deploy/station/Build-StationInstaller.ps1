<#
.SYNOPSIS
    Compila instaladores EXE de estacion QualityLock con Inno Setup.

.DESCRIPTION
    Por defecto busca clientes publicados en:
      <PublishRoot>\Client-win-x64
      <PublishRoot>\Client-win-x86

    y genera:
      QualityLockStation-win-x64.exe
      QualityLockStation-win-x86.exe

    Si ISCC.exe no esta instalado, instale Inno Setup con:
      winget install JRSoftware.InnoSetup

.EXAMPLE
    .\Build-StationInstaller.ps1 -PublishRoot C:\QualityLock\publish -OutDir C:\QualityLock\installers

.EXAMPLE
    .\Build-StationInstaller.ps1 -PublishRoot C:\QualityLock\publish `
        -OutDir C:\QualityLock\installers `
        -SecretsFile C:\QualityLock\server.env

.EXAMPLE
    .\Build-StationInstaller.ps1 -PublishRoot C:\QualityLock\publish `
        -ClientApiKey "<Auth__ClientApiKey>" `
        -BypassHmacSecret "<secreto-bypass>"

.EXAMPLE
    .\Build-StationInstaller.ps1 -Runtime win-x64 -SourceDir C:\QualityLock\publish\Client-win-x64
#>
[CmdletBinding()]
param(
    [string]$PublishRoot = "",
    [string]$SourceDir = "",
    [string]$OutDir = ".\installer",
    [ValidateSet("win-x64", "win-x86", "both")] [string]$Runtime = "both",
    [string]$InnoCompiler = "",
    [string]$AppVersion = "",
    [string]$SecretsFile = "",
    [string]$ClientApiKey = "",
    [string]$BypassHmacSecret = "",
    [string]$StationCode = "ICT-01",
    [Alias("Line")] [string]$Linea = "M1",
    [string]$ApiBaseUrl = "http://192.168.1.10:5080/",
    [string]$AdminPin = "ISEMM2026",
    [int]$AutoLockSeconds = 300,
    [int]$ScanMaxAvgKeyMs = 40,
    [string]$GuardProcessName = "inctest",
    [string]$GuardWindowTitle = "Error View"
)
$ErrorActionPreference = "Stop"

$scriptDir = $PSScriptRoot
$deployRoot = Split-Path -Parent $scriptDir
$repoRoot = Split-Path -Parent $deployRoot
$issPath = Join-Path $scriptDir "QualityLockStation.iss"

if (-not (Test-Path -LiteralPath $issPath)) {
    throw "No se encontro $issPath."
}

if (-not $PublishRoot) {
    $PublishRoot = Join-Path $repoRoot "publish"
}

function Read-KeyValueFile {
    param([Parameter(Mandatory)] [string]$Path)

    $pairs = @{}
    foreach ($line in Get-Content -LiteralPath $Path) {
        $text = $line.Trim()
        if ($text -eq "" -or $text.StartsWith("#")) { continue }
        $separator = $text.IndexOf("=")
        if ($separator -lt 1) { continue }

        $key = $text.Substring(0, $separator).Trim()
        $value = $text.Substring($separator + 1).Trim()
        $pairs[$key] = $value
    }
    return $pairs
}

function Get-FirstValue {
    param(
        [Parameter(Mandatory)] [hashtable]$Pairs,
        [Parameter(Mandatory)] [string[]]$Keys
    )

    foreach ($key in $Keys) {
        if ($Pairs.ContainsKey($key) -and -not [string]::IsNullOrWhiteSpace($Pairs[$key])) {
            return [string]$Pairs[$key]
        }
    }
    return ""
}

function Assert-InnoDefineValue {
    param(
        [Parameter(Mandatory)] [string]$Name,
        [AllowNull()] [string]$Value
    )

    if ($null -eq $Value) { return }
    if ($Value.Contains("'") -or $Value.Contains("`r") -or $Value.Contains("`n")) {
        throw "$Name contiene comillas simples o saltos de linea; no se puede incrustar de forma segura en el instalador."
    }
}

function Find-InnoCompiler {
    param([string]$PreferredPath)

    if ($PreferredPath) {
        if (Test-Path -LiteralPath $PreferredPath) {
            return (Resolve-Path -LiteralPath $PreferredPath).ProviderPath
        }
        throw "No se encontro ISCC.exe en '$PreferredPath'."
    }

    $command = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }

    $candidates = @()
    foreach ($root in @(${env:ProgramFiles(x86)}, $env:ProgramFiles)) {
        if ($root) {
            $candidates += (Join-Path $root "Inno Setup 6\ISCC.exe")
            $candidates += (Join-Path $root "Inno Setup 7\ISCC.exe")
        }
    }

    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate)) {
            return (Resolve-Path -LiteralPath $candidate).ProviderPath
        }
    }

    throw "No se encontro ISCC.exe. Instale Inno Setup con: winget install JRSoftware.InnoSetup"
}

function Get-PeMachine {
    param([Parameter(Mandatory)] [string]$Path)

    $fs = [System.IO.File]::OpenRead($Path)
    try {
        $br = New-Object System.IO.BinaryReader($fs)
        if ($br.ReadUInt16() -ne 0x5A4D) { return "unknown" }

        $fs.Seek(0x3c, [System.IO.SeekOrigin]::Begin) | Out-Null
        $peHeaderOffset = $br.ReadInt32()
        $fs.Seek($peHeaderOffset, [System.IO.SeekOrigin]::Begin) | Out-Null

        if ($br.ReadUInt32() -ne 0x00004550) { return "unknown" }
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

function Resolve-ClientSource {
    param([Parameter(Mandatory)] [string]$TargetRuntime)

    if ($SourceDir) {
        return (Resolve-Path -LiteralPath $SourceDir).ProviderPath
    }

    $publishRootPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($PublishRoot)
    $candidates = @(
        (Join-Path $publishRootPath "Client-$TargetRuntime")
    )

    if ($TargetRuntime -eq "win-x64") {
        $candidates += (Join-Path $publishRootPath "Client")
    }

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath (Join-Path $candidate "QualityLock.Client.WinForms.exe")) {
            return (Resolve-Path -LiteralPath $candidate).ProviderPath
        }
    }

    throw "No se encontro cliente publicado para $TargetRuntime. Ejecute: .\deploy\Publish-All.ps1 -OutDir $publishRootPath -AllClientRuntimes"
}

if ($Runtime -eq "both" -and $SourceDir) {
    throw "No use -SourceDir con -Runtime both. Publique ambos clientes y use -PublishRoot."
}

if (-not $SecretsFile) {
    $defaultSecretsFile = "C:\QualityLock\server.env"
    if (Test-Path -LiteralPath $defaultSecretsFile) {
        $SecretsFile = $defaultSecretsFile
    }
}

if ($SecretsFile) {
    if (-not (Test-Path -LiteralPath $SecretsFile)) {
        throw "No se encontro el archivo de secretos: $SecretsFile"
    }

    $secretPairs = Read-KeyValueFile -Path $SecretsFile
    if (-not $ClientApiKey) {
        $ClientApiKey = Get-FirstValue -Pairs $secretPairs -Keys @(
            "Auth__ClientApiKey",
            "ClientApiKey",
            "QUALITYLOCK_CLIENT_API_KEY"
        )
    }
    if (-not $BypassHmacSecret) {
        $BypassHmacSecret = Get-FirstValue -Pairs $secretPairs -Keys @(
            "BypassHmacSecret",
            "QUALITYLOCK_BYPASS_HMAC_SECRET",
            "QualityLock__BypassHmacSecret",
            "Station__BypassHmacSecret"
        )
    }
}

foreach ($define in @(
    @{ Name = "ClientApiKey"; Value = $ClientApiKey },
    @{ Name = "BypassHmacSecret"; Value = $BypassHmacSecret },
    @{ Name = "StationCode"; Value = $StationCode },
    @{ Name = "Linea"; Value = $Linea },
    @{ Name = "ApiBaseUrl"; Value = $ApiBaseUrl },
    @{ Name = "AdminPin"; Value = $AdminPin },
    @{ Name = "AutoLockSeconds"; Value = "$AutoLockSeconds" },
    @{ Name = "ScanMaxAvgKeyMs"; Value = "$ScanMaxAvgKeyMs" },
    @{ Name = "GuardProcessName"; Value = $GuardProcessName },
    @{ Name = "GuardWindowTitle"; Value = $GuardWindowTitle }
)) {
    Assert-InnoDefineValue -Name $define.Name -Value $define.Value
}

if ([string]::IsNullOrWhiteSpace($ClientApiKey)) {
    Write-Warning "ClientApiKey no se prellenara. Use -ClientApiKey o -SecretsFile con Auth__ClientApiKey."
}
if ([string]::IsNullOrWhiteSpace($BypassHmacSecret)) {
    Write-Warning "BypassHmacSecret no se prellenara. Use -BypassHmacSecret o -SecretsFile con BypassHmacSecret."
}

$iscc = Find-InnoCompiler -PreferredPath $InnoCompiler
$outDirPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutDir)
New-Item -ItemType Directory -Force -Path $outDirPath | Out-Null

$runtimes = if ($Runtime -eq "both") { @("win-x64", "win-x86") } else { @($Runtime) }
$built = @()

foreach ($targetRuntime in $runtimes) {
    $sourcePath = Resolve-ClientSource -TargetRuntime $targetRuntime
    $exePath = Join-Path $sourcePath "QualityLock.Client.WinForms.exe"
    if (-not (Test-Path -LiteralPath $exePath)) {
        throw "No se encontro $exePath."
    }

    $machine = Get-PeMachine -Path $exePath
    if ($targetRuntime -eq "win-x64" -and $machine -ne "x64") {
        throw "El paquete $targetRuntime requiere EXE x64, pero se detecto '$machine' en $exePath."
    }
    if ($targetRuntime -eq "win-x86" -and $machine -ne "x86") {
        throw "El paquete $targetRuntime requiere EXE x86, pero se detecto '$machine' en $exePath."
    }

    $version = $AppVersion
    if (-not $version) {
        $version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exePath).FileVersion
    }
    if (-not $version) { $version = "1.0.0" }

    Write-Host "Compilando instalador $targetRuntime desde $sourcePath ..." -ForegroundColor Cyan
    & $iscc `
        "/DSourceDir=$sourcePath" `
        "/DOutputDir=$outDirPath" `
        "/DAppArch=$targetRuntime" `
        "/DAppVersion=$version" `
        "/DDefaultStationCode=$StationCode" `
        "/DDefaultLinea=$Linea" `
        "/DDefaultApiBaseUrl=$ApiBaseUrl" `
        "/DDefaultClientApiKey=$ClientApiKey" `
        "/DDefaultBypassHmacSecret=$BypassHmacSecret" `
        "/DDefaultAdminPin=$AdminPin" `
        "/DDefaultAutoLockSeconds=$AutoLockSeconds" `
        "/DDefaultScanMaxAvgKeyMs=$ScanMaxAvgKeyMs" `
        "/DDefaultGuardProcessName=$GuardProcessName" `
        "/DDefaultGuardWindowTitle=$GuardWindowTitle" `
        $issPath

    if ($LASTEXITCODE -ne 0) {
        throw "Fallo la compilacion del instalador $targetRuntime."
    }

    $installerPath = Join-Path $outDirPath "QualityLockStation-$targetRuntime.exe"
    $built += $installerPath
}

Write-Host ""
Write-Host "Instaladores generados:" -ForegroundColor Green
foreach ($path in $built) {
    Write-Host "  $path"
}
