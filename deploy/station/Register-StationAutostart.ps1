<#
.SYNOPSIS
    Registra QualityLock para iniciar elevado al iniciar sesión.

.DESCRIPTION
    Sustituye las claves Run heredadas por una tarea programada interactiva con
    RunLevel Highest. La tarea usa el grupo local Administradores, identificado por SID
    para funcionar sin depender del idioma de Windows.

    Se usa schtasks.exe + XML (esquema 1.2) en lugar del módulo ScheduledTasks porque
    ese módulo no existe en Windows 7 (PowerShell 2.0): ahí el script fallaba y la
    estación quedaba instalada pero sin arrancar nunca. Por lo mismo el script se
    mantiene en sintaxis PS 2.0 / .NET 2.0: sin ::new(), sin IsNullOrWhiteSpace.

.EXAMPLE
    .\Register-StationAutostart.ps1 -ExecutablePath "C:\QualityLock\Client\QualityLock.Client.WinForms.exe"

.EXAMPLE
    # Comprobacion: valida el XML contra el propio Task Scheduler sin admin ni registrar nada.
    .\Register-StationAutostart.ps1 -ValidateOnly
#>
[CmdletBinding()]
param(
    [string]$ExecutablePath = "",
    [string]$TaskName = "QualityLockClient",
    [switch]$Unregister,
    [switch]$StartNow,
    [switch]$ValidateOnly
)
$ErrorActionPreference = "Stop"

$schtasks = Join-Path $env:SystemRoot "System32\schtasks.exe"

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

# Con $ErrorActionPreference = "Stop", PowerShell <= 5.1 convierte el stderr de un
# comando nativo en NativeCommandError y aborta. schtasks escribe a stderr en casos
# normales (consultar una tarea inexistente), asi que hay que relajar la preferencia
# alrededor de cada llamada y decidir por el codigo de salida.
function Invoke-Schtasks {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $output = & $schtasks $args 2>&1
        return New-Object PSObject -Property @{
            ExitCode = $LASTEXITCODE
            Output   = ($output | Out-String)
        }
    }
    finally {
        $ErrorActionPreference = $previous
    }
}

function Test-TaskExists {
    param([string]$Name)
    return ((Invoke-Schtasks /Query /TN $Name).ExitCode -eq 0)
}

function New-TaskXml {
    param([string]$ExePath)

    # Esquema 1.2 = Windows 7 en adelante. No agregar elementos de 1.3/1.4
    # (UseUnifiedSchedulingEngine, Volatile, ...): Win7 rechaza el XML entero.
    $exeXml = $ExePath.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
    return @"
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo>
    <Description>QualityLock elevado para proteger ventanas de la estacion.</Description>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id="Author">
      <GroupId>S-1-5-32-544</GroupId>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings>
      <StopOnIdleEnd>false</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context="Author">
    <Exec>
      <Command>$exeXml</Command>
    </Exec>
  </Actions>
</Task>
"@
}

# Comprobacion sin efectos secundarios: el propio Task Scheduler parsea y valida el XML
# al asignar XmlText, asi que un elemento fuera del esquema 1.2 revienta aqui y no en
# la estacion Windows 7. No necesita permisos de administrador.
if ($ValidateOnly) {
    $probeExe = $ExecutablePath
    if ($probeExe -eq $null -or $probeExe.Trim() -eq "") {
        $probeExe = "C:\QualityLock\Client\QualityLock.Client.WinForms.exe"
    }

    $service = New-Object -ComObject "Schedule.Service"
    $service.Connect()
    $definition = $service.NewTask(0)
    $definition.XmlText = (New-TaskXml -ExePath $probeExe)

    if ($definition.Principal.RunLevel -ne 1) {
        throw "El XML no quedo con RunLevel HighestAvailable (obtenido: $($definition.Principal.RunLevel))."
    }
    if ($definition.Actions.Item(1).Path -ne $probeExe) {
        throw "El XML no apunta al ejecutable esperado."
    }

    Write-Host "XML de la tarea valido (esquema 1.2, RunLevel Highest)." -ForegroundColor Green
    return
}

Remove-LegacyRunEntries

if (Test-TaskExists -Name $TaskName) {
    Invoke-Schtasks /Delete /TN $TaskName /F | Out-Null
}

if ($Unregister) {
    Write-Host "Autostart '$TaskName' eliminado." -ForegroundColor Green
    return
}

if ($ExecutablePath -eq $null -or $ExecutablePath.Trim() -eq "") {
    throw "ExecutablePath es obligatorio al registrar el autostart."
}

$resolvedExe = (Resolve-Path -LiteralPath $ExecutablePath).ProviderPath
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principalCheck = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principalCheck.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Ejecute este script como administrador para registrar la tarea elevada."
}

# schtasks /XML exige el archivo en UTF-16; en UTF-8 lo rechaza como XML no válido.
$xmlPath = Join-Path $env:TEMP "QualityLockClient.task.xml"
Set-Content -LiteralPath $xmlPath -Value (New-TaskXml -ExePath $resolvedExe) -Encoding Unicode

try {
    $created = Invoke-Schtasks /Create /TN $TaskName /XML $xmlPath /F
    if ($created.ExitCode -ne 0) {
        throw "schtasks no pudo crear la tarea '$TaskName': $($created.Output.Trim())"
    }
}
finally {
    Remove-Item -LiteralPath $xmlPath -Force -ErrorAction SilentlyContinue
}

if (-not (Test-TaskExists -Name $TaskName)) {
    throw "La tarea '$TaskName' no quedó registrada."
}

Write-Host "Autostart '$TaskName' registrado con RunLevel Highest." -ForegroundColor Green

if ($StartNow) {
    Invoke-Schtasks /Run /TN $TaskName | Out-Null
}
