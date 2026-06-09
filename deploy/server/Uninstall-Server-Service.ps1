<#
.SYNOPSIS
    Detiene y elimina el servicio de Windows de la API de QualityLock.
.EXAMPLE
    .\Uninstall-Server-Service.ps1
#>
[CmdletBinding()]
param([string]$ServiceName = "QualityLockApi")
$ErrorActionPreference = "Stop"
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
            ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { throw "Ejecuta COMO ADMINISTRADOR." }

$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $svc) { Write-Host "El servicio '$ServiceName' no existe."; return }

if ($svc.Status -ne 'Stopped') { Stop-Service $ServiceName -Force }
sc.exe delete $ServiceName | Out-Null
Write-Host "Servicio '$ServiceName' eliminado." -ForegroundColor Green
