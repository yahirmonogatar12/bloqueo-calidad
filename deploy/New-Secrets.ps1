<#
.SYNOPSIS
    Genera claves criptograficamente fuertes para QualityLock:
      - Jwt:SigningKey   (firma de los tokens JWT, >= 32 bytes)
      - Auth:ClientApiKey (clave que cada estacion presenta para obtener token)
      - BypassHmacSecret  (firma de bypass locales de contingencia)

.DESCRIPTION
    Las claves se generan con RNGCryptoServiceProvider (no Random). Por defecto solo
    se imprimen en pantalla; con -OutFile se guardan en un archivo .env (que NO debe
    subirse a git). La misma ClientApiKey debe configurarse en el servidor y en TODAS
    las estaciones.

.EXAMPLE
    .\New-Secrets.ps1
    # Imprime las claves para copiarlas manualmente.

.EXAMPLE
    .\New-Secrets.ps1 -OutFile C:\QualityLock\server.env
    # Genera y guarda un archivo .env listo para el servidor.
#>
[CmdletBinding()]
param(
    [int]$SigningKeyBytes = 48,      # 48 bytes -> 64 chars base64, holgado sobre el minimo de 32
    [int]$ClientKeyBytes  = 32,
    [int]$BypassKeyBytes  = 32,
    [string]$OutFile
)

function New-RandomBase64([int]$bytes) {
    $buf = New-Object byte[] $bytes
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($buf)
    # base64url-ish: sin '/' ni '+' para que sea seguro en env vars y JSON
    return [Convert]::ToBase64String($buf).Replace('+','-').Replace('/','_').TrimEnd('=')
}

$signingKey = New-RandomBase64 $SigningKeyBytes
$clientKey  = New-RandomBase64 $ClientKeyBytes
$bypassKey  = New-RandomBase64 $BypassKeyBytes

Write-Host ""
Write-Host "=== Claves generadas para QualityLock ===" -ForegroundColor Cyan
Write-Host "Jwt:SigningKey      = $signingKey"
Write-Host "Auth:ClientApiKey   = $clientKey"
Write-Host "BypassHmacSecret    = $bypassKey"
Write-Host ""
Write-Host "IMPORTANTE:" -ForegroundColor Yellow
Write-Host "  * Jwt:SigningKey  -> SOLO en el servidor."
Write-Host "  * Auth:ClientApiKey -> en el servidor Y en cada estacion (debe coincidir)."
Write-Host "  * BypassHmacSecret -> en cada estacion y al generar bypass.json."
Write-Host "  * NUNCA subas estas claves a git."
Write-Host ""

if ($OutFile) {
    $content = @"
# QualityLock - variables del SERVIDOR (NO subir a git)
# Generado: $(Get-Date -Format s)
ConnectionStrings__MySQL=Server=192.168.1.10;Port=3306;Database=mes_production;Uid=mes_admin;Pwd=CAMBIA-ESTA-PASSWORD;
Jwt__SigningKey=$signingKey
Jwt__Issuer=QualityLock.Api
Jwt__Audience=QualityLock.Clients
Auth__ClientApiKey=$clientKey
BypassHmacSecret=$bypassKey
Admin__MinRoleLevel=3
ASPNETCORE_URLS=http://0.0.0.0:5080
ASPNETCORE_ENVIRONMENT=Production
"@
    Set-Content -Path $OutFile -Value $content -Encoding UTF8
    Write-Host "Archivo escrito: $OutFile" -ForegroundColor Green
    Write-Host "  Edita la password de MySQL antes de usarlo." -ForegroundColor Yellow
}
