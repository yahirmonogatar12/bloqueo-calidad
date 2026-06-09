<#
.SYNOPSIS
    Generates a signed bypass.json for QualityLock emergency access.

.DESCRIPTION
    Creates a bypass token with HMAC-SHA256 signature that allows the
    QualityLock client to unlock a station without backend connectivity.
    The secret must match BypassHmacSecret in the client appsettings.json.

.PARAMETER StationCode
    Station code to authorize (e.g. ICT-01).

.PARAMETER IssuedBy
    Name or badge of the administrator issuing the bypass.

.PARAMETER Reason
    Human-readable reason for the bypass.

.PARAMETER ValidHours
    Number of hours the bypass is valid. Default: 8.

.PARAMETER HmacSecret
    HMAC secret key. Must match the client configuration.
    WARNING: keep this secret secure.

.PARAMETER OutputPath
    Path where bypass.json will be written.
    Default: C:\ProgramData\QualityLock\bypass.json

.EXAMPLE
    .\Generate-Bypass.ps1 -StationCode ICT-01 -IssuedBy "ADMIN001" `
        -Reason "Backend maintenance" -ValidHours 4 `
        -HmacSecret "CHANGE-THIS-SECRET-IN-PRODUCTION"
#>

[CmdletBinding()]
param (
    [Parameter(Mandatory)]
    [string]$StationCode,

    [Parameter(Mandatory)]
    [string]$IssuedBy,

    [Parameter(Mandatory)]
    [string]$Reason,

    [int]$ValidHours = 8,

    [Parameter(Mandatory)]
    [string]$HmacSecret,

    [string]$OutputPath = "C:\ProgramData\QualityLock\bypass.json"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$expiresAt = [System.DateTime]::UtcNow.AddHours($ValidHours)
$expiresAtIso = $expiresAt.ToString("O")

$payload = "$StationCode|$IssuedBy|$expiresAtIso|$Reason"

$secretBytes = [System.Text.Encoding]::UTF8.GetBytes($HmacSecret)
$payloadBytes = [System.Text.Encoding]::UTF8.GetBytes($payload)

$hmac = New-Object System.Security.Cryptography.HMACSHA256
$hmac.Key = $secretBytes
$hashBytes = $hmac.ComputeHash($payloadBytes)
$signature = [System.Convert]::ToBase64String($hashBytes)

$token = [ordered]@{
    stationCode  = $StationCode
    enabled      = $true
    reason       = $Reason
    expiresAtUtc = $expiresAtIso
    issuedBy     = $IssuedBy
    signature    = $signature
}

$outputDir = Split-Path $OutputPath -Parent
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

$json = $token | ConvertTo-Json -Depth 3
$json | Set-Content -Path $OutputPath -Encoding UTF8

Write-Host ""
Write-Host "bypass.json generated:" -ForegroundColor Green
Write-Host "  Path:       $OutputPath"
Write-Host "  Station:    $StationCode"
Write-Host "  Issued by:  $IssuedBy"
Write-Host "  Reason:     $Reason"
Write-Host "  Expires:    $expiresAtIso (UTC)"
Write-Host "  Signature:  $signature"
Write-Host ""
Write-Host "Copy bypass.json to the target machine's C:\ProgramData\QualityLock\ folder." -ForegroundColor Yellow
