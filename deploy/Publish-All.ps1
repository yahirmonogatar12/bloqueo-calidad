<#
.SYNOPSIS
    Publica la API y el cliente de QualityLock en modo Release, listos para desplegar.

.DESCRIPTION
    Genera dos carpetas bajo -OutDir:
      <OutDir>\Api     -> servidor (QualityLock.Api.exe)
      <OutDir>\Client  -> estaciones (QualityLock.Client.WinForms.exe)

    Por defecto publica self-contained (incluye el runtime .NET, no requiere instalar
    .NET en el servidor ni en las estaciones). Usa -FrameworkDependent si prefieres que
    cada maquina tenga el runtime .NET 9 instalado (paquetes mas pequenos).

.EXAMPLE
    .\Publish-All.ps1 -OutDir C:\QualityLock\publish

.EXAMPLE
    .\Publish-All.ps1 -OutDir C:\QualityLock\publish -ClientRuntime win-x86

.EXAMPLE
    .\Publish-All.ps1 -OutDir .\publish -FrameworkDependent
#>
[CmdletBinding()]
param(
    [string]$OutDir = ".\publish",
    [string]$Runtime = "win-x64",
    [string]$ApiRuntime = "",
    [string]$ClientRuntime = "",
    [switch]$FrameworkDependent
)
$ErrorActionPreference = "Stop"

$validRuntimes = @("win-x64", "win-x86", "win-arm64")
if (-not $ApiRuntime) { $ApiRuntime = $Runtime }
if (-not $ClientRuntime) { $ClientRuntime = $Runtime }

foreach ($rid in @($ApiRuntime, $ClientRuntime)) {
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
    $clientOut = Join-Path $OutDir "Client"

    $apiCommon = @(
        "-c", "Release",
        "-r", $ApiRuntime,
        "--self-contained", "$($selfContained.ToString().ToLower())"
    )

    $clientCommon = @(
        "-c", "Release",
        "-r", $ClientRuntime,
        "--self-contained", "$($selfContained.ToString().ToLower())"
    )

    Write-Host "Publicando API ($ApiRuntime) -> $apiOut" -ForegroundColor Cyan
    dotnet publish "src/QualityLock.Api/QualityLock.Api.csproj" @apiCommon -o $apiOut
    if ($LASTEXITCODE -ne 0) { throw "Fallo el publish de la API." }

    Write-Host "Publicando Cliente ($ClientRuntime) -> $clientOut" -ForegroundColor Cyan
    dotnet publish "src/QualityLock.Client.WinForms/QualityLock.Client.WinForms.csproj" @clientCommon -o $clientOut
    if ($LASTEXITCODE -ne 0) { throw "Fallo el publish del cliente." }

    Write-Host ""
    Write-Host "Publicacion completa:" -ForegroundColor Green
    Write-Host "  API     : $apiOut"
    Write-Host "  Cliente : $clientOut"
    Write-Host ""
    Write-Host "Siguiente: ver deploy\README-DESPLIEGUE.md" -ForegroundColor Yellow
}
finally {
    Pop-Location
}
