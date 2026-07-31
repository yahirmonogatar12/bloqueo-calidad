<#
.SYNOPSIS
    Desinstala el cliente de QualityLock de una estacion: detiene el proceso, quita el
    autostart y elimina la carpeta de instalacion. Tambien restaura el Administrador de
    tareas por si quedo bloqueado.
.EXAMPLE
    .\Uninstall-Station.ps1 -InstallDir C:\QualityLock\Client
#>
[CmdletBinding()]
param([Parameter(Mandatory)] [string]$InstallDir)
$ErrorActionPreference = "SilentlyContinue"

# Detener el proceso
Get-Process -Name "QualityLock.Client.WinForms" -ErrorAction SilentlyContinue |
    Stop-Process -Force -ErrorAction SilentlyContinue

# Quitar autostart (ambos mecanismos)
Remove-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "QualityLockClient" -ErrorAction SilentlyContinue
Remove-ItemProperty -Path "HKLM:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "QualityLockClient" -ErrorAction SilentlyContinue
Remove-ItemProperty -Path "HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run" -Name "QualityLockClient" -ErrorAction SilentlyContinue
Get-ScheduledTask -TaskName "QualityLockClient" -ErrorAction SilentlyContinue |
    Unregister-ScheduledTask -Confirm:$false -ErrorAction SilentlyContinue

# Restaurar Task Manager si quedo deshabilitado
$reg = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Policies\System"
if (Test-Path $reg) { Remove-ItemProperty -Path $reg -Name "DisableTaskMgr" -ErrorAction SilentlyContinue }

# Borrar carpeta
if (Test-Path $InstallDir) { Remove-Item -Path $InstallDir -Recurse -Force -ErrorAction SilentlyContinue }

Write-Host "Cliente desinstalado de la estacion. Task Manager restaurado." -ForegroundColor Green
