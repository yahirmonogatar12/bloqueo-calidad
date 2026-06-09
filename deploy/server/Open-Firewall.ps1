<#
.SYNOPSIS
    Abre el puerto de la API de QualityLock en el Firewall de Windows del servidor.
.DESCRIPTION
    Crea una regla de entrada para TCP 5080 (por defecto). Ejecutar COMO ADMINISTRADOR.
    Limita el alcance a la subred local con -LocalSubnet para no exponerlo a internet.
.EXAMPLE
    .\Open-Firewall.ps1
.EXAMPLE
    .\Open-Firewall.ps1 -Port 5080 -RemoteAddress 192.168.1.0/24
#>
[CmdletBinding()]
param(
    [int]$Port = 5080,
    [string]$RemoteAddress = "LocalSubnet",
    [string]$RuleName = "QualityLock API (TCP $Port)"
)
$ErrorActionPreference = "Stop"
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
            ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { throw "Ejecuta COMO ADMINISTRADOR." }

Get-NetFirewallRule -DisplayName $RuleName -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue

New-NetFirewallRule -DisplayName $RuleName -Direction Inbound -Action Allow `
    -Protocol TCP -LocalPort $Port -RemoteAddress $RemoteAddress -Profile Any | Out-Null

Write-Host "Regla creada: '$RuleName' (TCP $Port, desde $RemoteAddress)." -ForegroundColor Green
