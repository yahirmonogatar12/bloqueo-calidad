<#
.SYNOPSIS
    Registra QualityLock para iniciar elevado al iniciar sesión.

.DESCRIPTION
    Sustituye las claves Run heredadas por una tarea programada interactiva con
    RunLevel Highest. La tarea usa el grupo local Administradores, identificado por SID
    para funcionar sin depender del idioma de Windows.
#>
[CmdletBinding()]
param(
    [string]$ExecutablePath = "",
    [string]$TaskName = "QualityLockClient",
    [switch]$Unregister,
    [switch]$StartNow
)
$ErrorActionPreference = "Stop"

function Remove-LegacyRunEntries {
    $runPaths = @(
        "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run",
        "HKLM:\Software\Microsoft\Windows\CurrentVersion\Run",
        "HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"
    )

    foreach ($runPath in $runPaths) {
        if (Test-Path -LiteralPath $runPath) {
            Remove-ItemProperty -LiteralPath $runPath -Name "QualityLockClient" -ErrorAction SilentlyContinue
        }
    }
}

Remove-LegacyRunEntries
Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue |
    Unregister-ScheduledTask -Confirm:$false -ErrorAction SilentlyContinue

if ($Unregister) {
    Write-Host "Autostart '$TaskName' eliminado." -ForegroundColor Green
    return
}

if ([string]::IsNullOrWhiteSpace($ExecutablePath)) {
    throw "ExecutablePath es obligatorio al registrar el autostart."
}

$resolvedExe = (Resolve-Path -LiteralPath $ExecutablePath).ProviderPath
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principalCheck = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principalCheck.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Ejecute este script como administrador para registrar la tarea elevada."
}

$action = New-ScheduledTaskAction -Execute $resolvedExe
$trigger = New-ScheduledTaskTrigger -AtLogOn
$principal = New-ScheduledTaskPrincipal `
    -GroupId "S-1-5-32-544" `
    -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -StartWhenAvailable `
    -MultipleInstances IgnoreNew `
    -ExecutionTimeLimit ([TimeSpan]::Zero)

$task = New-ScheduledTask `
    -Action $action `
    -Trigger $trigger `
    -Principal $principal `
    -Settings $settings `
    -Description "QualityLock elevado para proteger ventanas de la estación."

Register-ScheduledTask -TaskName $TaskName -InputObject $task -Force | Out-Null

$registered = Get-ScheduledTask -TaskName $TaskName -ErrorAction Stop
if ($registered.Principal.RunLevel -ne "Highest") {
    throw "La tarea '$TaskName' se registró sin RunLevel Highest."
}

Write-Host "Autostart '$TaskName' registrado con RunLevel Highest." -ForegroundColor Green

if ($StartNow) {
    Start-ScheduledTask -TaskName $TaskName
}
