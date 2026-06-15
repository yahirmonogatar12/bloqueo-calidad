<#
.SYNOPSIS
    Empaqueta la API de QualityLock para instalarla desde una carpeta portable.

.DESCRIPTION
    Crea una carpeta con:
      - Api\                         binarios publicados de QualityLock.Api
      - server.env                   variables de entorno del servicio
      - Install-QualityLockServer.ps1 instalador principal
      - Uninstall-QualityLockServer.ps1 desinstalador
      - Test-QualityLockServer.ps1    verificacion de /health
      - scripts\                     scripts base de deploy/server

.EXAMPLE
    .\Build-ServerPackage.ps1 `
        -PublishDir C:\QualityLock\publish\Api `
        -EnvFile C:\QualityLock\server.env `
        -OutDir C:\tmp\QualityLock-Server
#>
[CmdletBinding()]
param(
    [string]$PublishDir = "C:\QualityLock\publish\Api",
    [string]$EnvFile = "C:\QualityLock\server.env",
    [string]$OutDir = "C:\tmp\QualityLock-Server",
    [string]$ListenUrl = "http://0.0.0.0:5080",
    [switch]$Zip
)
$ErrorActionPreference = "Stop"

$publishPath = (Resolve-Path -LiteralPath $PublishDir).ProviderPath
$envPath = (Resolve-Path -LiteralPath $EnvFile).ProviderPath
$outPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutDir)
$scriptDir = $PSScriptRoot

$apiExe = Join-Path $publishPath "QualityLock.Api.exe"
if (-not (Test-Path -LiteralPath $apiExe)) {
    throw "No se encontro $apiExe. Publica la API primero."
}

New-Item -ItemType Directory -Force -Path $outPath | Out-Null

$apiOut = Join-Path $outPath "Api"
$scriptsOut = Join-Path $outPath "scripts"

if (Test-Path -LiteralPath $apiOut) {
    Remove-Item -LiteralPath $apiOut -Recurse -Force
}
if (Test-Path -LiteralPath $scriptsOut) {
    Remove-Item -LiteralPath $scriptsOut -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $apiOut, $scriptsOut | Out-Null
Copy-Item -Path (Join-Path $publishPath "*") -Destination $apiOut -Recurse -Force
Copy-Item -LiteralPath $envPath -Destination (Join-Path $outPath "server.env") -Force

Copy-Item -LiteralPath (Join-Path $scriptDir "Install-Server-Service.ps1") -Destination $scriptsOut -Force
Copy-Item -LiteralPath (Join-Path $scriptDir "Uninstall-Server-Service.ps1") -Destination $scriptsOut -Force
Copy-Item -LiteralPath (Join-Path $scriptDir "Open-Firewall.ps1") -Destination $scriptsOut -Force

$installScript = @"
[CmdletBinding()]
param(
    [string]`$ListenUrl = "$ListenUrl",
    [string]`$RemoteAddress = "LocalSubnet"
)
`$ErrorActionPreference = "Stop"
`$root = Split-Path -Parent `$MyInvocation.MyCommand.Path
`$api = Join-Path `$root "Api"
`$env = Join-Path `$root "server.env"
`$scripts = Join-Path `$root "scripts"

& (Join-Path `$scripts "Install-Server-Service.ps1") -PublishDir `$api -EnvFile `$env -ListenUrl `$ListenUrl
& (Join-Path `$scripts "Open-Firewall.ps1") -Port ([Uri](`$ListenUrl -replace '0\.0\.0\.0','localhost')).Port -RemoteAddress `$RemoteAddress

Write-Host ""
Write-Host "Verificacion local:" -ForegroundColor Cyan
& (Join-Path `$root "Test-QualityLockServer.ps1")
"@

$uninstallScript = @"
[CmdletBinding()]
param()
`$ErrorActionPreference = "Stop"
`$root = Split-Path -Parent `$MyInvocation.MyCommand.Path
& (Join-Path `$root "scripts\Uninstall-Server-Service.ps1")
"@

$testScript = @"
[CmdletBinding()]
param(
    [string]`$Url = "http://localhost:5080/health"
)
`$ErrorActionPreference = "Stop"
`$response = Invoke-WebRequest -Uri `$Url -UseBasicParsing -TimeoutSec 5
Write-Host "HTTP `$(`$response.StatusCode): `$(`$response.Content)" -ForegroundColor Green
"@

$readme = @"
QualityLock Server Package
==========================

Contenido:
  Api\                         Binarios publicados de la API
  server.env                   Configuracion del servicio
  Install-QualityLockServer.ps1 Instala servicio + firewall
  Uninstall-QualityLockServer.ps1 Quita el servicio
  Test-QualityLockServer.ps1    Prueba /health local

Uso en el servidor, PowerShell como Administrador:

  cd "$outPath"
  .\Install-QualityLockServer.ps1

Verificar:

  .\Test-QualityLockServer.ps1
  Invoke-WebRequest http://192.168.1.10:5080/health -UseBasicParsing

Nota: server.env contiene secretos. No subir ni compartir fuera de la red interna.
"@

Set-Content -LiteralPath (Join-Path $outPath "Install-QualityLockServer.ps1") -Value $installScript -Encoding UTF8
Set-Content -LiteralPath (Join-Path $outPath "Uninstall-QualityLockServer.ps1") -Value $uninstallScript -Encoding UTF8
Set-Content -LiteralPath (Join-Path $outPath "Test-QualityLockServer.ps1") -Value $testScript -Encoding UTF8
Set-Content -LiteralPath (Join-Path $outPath "README-SERVER.txt") -Value $readme -Encoding UTF8

if ($Zip) {
    $zipPath = "$outPath.zip"
    if (Test-Path -LiteralPath $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }
    $zipItems = @(
        (Join-Path $outPath "Api"),
        (Join-Path $outPath "scripts"),
        (Join-Path $outPath "server.env"),
        (Join-Path $outPath "Install-QualityLockServer.ps1"),
        (Join-Path $outPath "Uninstall-QualityLockServer.ps1"),
        (Join-Path $outPath "Test-QualityLockServer.ps1"),
        (Join-Path $outPath "README-SERVER.txt")
    )
    Compress-Archive -Path $zipItems -DestinationPath $zipPath
}

Write-Host "Paquete generado: $outPath" -ForegroundColor Green
if ($Zip) { Write-Host "ZIP generado: $outPath.zip" -ForegroundColor Green }
