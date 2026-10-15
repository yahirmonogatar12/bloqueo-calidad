<#
.SYNOPSIS
    Publica la API y el cliente de QualityLock en modo Release, listos para desplegar.

.DESCRIPTION
    Genera carpetas bajo -OutDir:
      <OutDir>\Api                 -> servidor (QualityLock.Api.exe)
      <OutDir>\Client              -> estacion Win10/11, si se publica un solo runtime
      <OutDir>\Client-win-x64      -> estacion Win10/11 64 bits, con -AllClientRuntimes
      <OutDir>\Client-win-x86      -> estacion Win10/11 32 bits, con -AllClientRuntimes
      <OutDir>\Client-win7         -> estacion Windows 7, si se publica un solo runtime
      <OutDir>\Client-win7-x64     -> estacion Windows 7 64 bits, con -AllClientRuntimes
      <OutDir>\Client-win7-x86     -> estacion Windows 7 32 bits, con -AllClientRuntimes

    El cliente se publica en dos versiones porque .NET 9 no arranca en Windows 7:
      net9.0-windows -> Windows 10/11 (build de siempre)
      net6.0-windows -> Windows 7 SP1 (ultimo .NET que arranca ahi)
    Usa -SkipWin7Client si solo necesitas la version moderna.

    Por defecto publica self-contained (incluye el runtime .NET, no requiere instalar
    .NET en el servidor ni en las estaciones). Usa -FrameworkDependent si prefieres que
    cada maquina tenga el runtime .NET instalado (paquetes mas pequenos); ojo que en ese
    modo la estacion Windows 7 necesita el runtime .NET 6 Desktop instalado a mano.

.EXAMPLE
    .\Publish-All.ps1 -OutDir C:\QualityLock\publish

.EXAMPLE
    .\Publish-All.ps1 -OutDir C:\QualityLock\publish -ClientRuntime win-x86

.EXAMPLE
    .\Publish-All.ps1 -OutDir C:\QualityLock\publish -AllClientRuntimes

.EXAMPLE
    .\Publish-All.ps1 -OutDir .\publish -FrameworkDependent
#>
[CmdletBinding()]
param(
    [string]$OutDir = ".\publish",
    [string]$Runtime = "win-x64",
    [string]$ApiRuntime = "",
    [string]$ClientRuntime = "",
    [switch]$AllClientRuntimes,
    [switch]$FrameworkDependent,
    [switch]$SkipWin7Client
)
$ErrorActionPreference = "Stop"

$validRuntimes = @("win-x64", "win-x86", "win-arm64")
if (-not $ApiRuntime) { $ApiRuntime = $Runtime }
if (-not $ClientRuntime) { $ClientRuntime = $Runtime }
$clientRuntimes = if ($AllClientRuntimes) { @("win-x64", "win-x86") } else { @($ClientRuntime) }

foreach ($rid in @($ApiRuntime) + $clientRuntimes) {
    if ($validRuntimes -notcontains $rid) {
        throw "Runtime no soportado '$rid'. Usa uno de: $($validRuntimes -join ', ')."
    }
}

# La solucion esta un nivel arriba de /deploy
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    $selfContained = (-not $FrameworkDependent)
    $apiOut    = Join-Path $OutDir "Api"
    $clientOutputs = @()

    $apiCommon = @(
        "-c", "Release",
        "-r", $ApiRuntime,
        "--self-contained", "$($selfContained.ToString().ToLower())"
    )

    Write-Host "Publicando API ($ApiRuntime) -> $apiOut" -ForegroundColor Cyan
    dotnet publish "src/QualityLock.Api/QualityLock.Api.csproj" @apiCommon -o $apiOut
    if ($LASTEXITCODE -ne 0) { throw "Fallo el publish de la API." }

    # Cada variante = un TFM. El proyecto es multi-target, asi que -f es obligatorio.
    $clientVariants = @(
        [pscustomobject]@{ Label = "Win10/11"; Framework = "net9.0-windows"; Folder = "Client" }
    )
    if (-not $SkipWin7Client) {
        $clientVariants += [pscustomobject]@{ Label = "Windows 7"; Framework = "net6.0-windows"; Folder = "Client-win7" }
    }

    foreach ($variant in $clientVariants) {
        foreach ($rid in $clientRuntimes) {
            # Win7 usa "Client-win7-x64"; Win10 conserva "Client-win-x64" de siempre.
            $folderName = $variant.Folder
            if ($AllClientRuntimes) {
                $suffix = if ($variant.Folder -eq "Client") { $rid } else { $rid.Replace("win-", "") }
                $folderName = "$folderName-$suffix"
            }
            $clientOut = Join-Path $OutDir $folderName

            $clientCommon = @(
                "-c", "Release",
                "-f", $variant.Framework,
                "-r", $rid,
                "--self-contained", "$($selfContained.ToString().ToLower())"
            )

            Write-Host "Publicando Cliente $($variant.Label) ($rid, $($variant.Framework)) -> $clientOut" -ForegroundColor Cyan
            dotnet publish "src/QualityLock.Client.WinForms/QualityLock.Client.WinForms.csproj" @clientCommon -o $clientOut
            if ($LASTEXITCODE -ne 0) { throw "Fallo el publish del cliente $($variant.Label) $rid." }
            $clientOutputs += [pscustomobject]@{ Label = $variant.Label; Runtime = $rid; Path = $clientOut }
        }
    }

    Write-Host ""
    Write-Host "Publicacion completa:" -ForegroundColor Green
    Write-Host "  API     : $apiOut"
    foreach ($output in $clientOutputs) {
        Write-Host "  Cliente $($output.Label) $($output.Runtime): $($output.Path)"
    }
    Write-Host ""
    Write-Host "Siguiente: ver deploy\README-DESPLIEGUE.md" -ForegroundColor Yellow
}
finally {
    Pop-Location
}
