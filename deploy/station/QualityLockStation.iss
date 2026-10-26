#define AppName "QualityLock Station"
#define AppPublisher "QualityLock"
#define AppExeName "QualityLock.Client.WinForms.exe"
#ifndef SourceDir
#define SourceDir "..\..\publish\Client"
#endif
#ifndef OutputDir
#define OutputDir ".\installer"
#endif
#ifndef AppArch
#define AppArch "win-x64"
#endif
#ifndef AppVersion
#define AppVersion "1.0.0"
#endif
; AppArch decide la arquitectura; AppSuffix solo el nombre del EXE de salida, para
; poder distinguir el paquete de Windows 7 (win7-x64) del de Win10/11 (win-x64).
#ifndef AppSuffix
#define AppSuffix AppArch
#endif
; Los dos paquetes solo se distinguen por un "7" en el nombre, y Inno valida
; arquitectura pero NO version de SO: sin esto, el paquete .NET 9 se instala sin
; quejarse en Windows 7 y la app nunca abre. 10.0.14393 = Win10 1607, piso de .NET 9.
#ifndef MinWindows
  #if Pos("win7", LowerCase(AppSuffix)) > 0
    #define MinWindows "6.1sp1"
  #else
    #define MinWindows "10.0.14393"
  #endif
#endif
#ifndef DefaultStationCode
#define DefaultStationCode "ICT-01"
#endif
#ifndef DefaultLinea
#define DefaultLinea "M1"
#endif
#ifndef DefaultApiBaseUrl
#define DefaultApiBaseUrl "http://192.168.1.10:5080/"
#endif
#ifndef DefaultClientApiKey
#define DefaultClientApiKey ""
#endif
#ifndef DefaultBypassHmacSecret
#define DefaultBypassHmacSecret ""
#endif
#ifndef DefaultAdminPin
#define DefaultAdminPin "ISEMM2026"
#endif
#ifndef DefaultAutoLockSeconds
#define DefaultAutoLockSeconds "300"
#endif
#ifndef DefaultScanMaxAvgKeyMs
#define DefaultScanMaxAvgKeyMs "40"
#endif
#ifndef DefaultGuardProcessName
#define DefaultGuardProcessName "inctest"
#endif
#ifndef DefaultGuardWindowTitle
#define DefaultGuardWindowTitle "Error View"
#endif

[Setup]
AppId={{B0A04A5C-0E87-4C94-99C8-1F5154A8C7A5}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\QualityLock\Client
DefaultGroupName=QualityLock
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=QualityLockStation-{#AppSuffix}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
UninstallDisplayName={#AppName}
MinVersion={#MinWindows}
#if AppArch == "win-x64"
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#else
ArchitecturesAllowed=x86compatible
#endif

[Dirs]
Name: "{commonappdata}\QualityLock"; Permissions: users-modify
Name: "{commonappdata}\QualityLock\logs"; Permissions: users-modify

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "Register-StationAutostart.ps1"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\QualityLock"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Configurar QualityLock"; Filename: "{app}\{#AppExeName}"; Parameters: "--setup"

[Run]
; El registro del autostart NO va aqui: [Run] ignora el codigo de salida, asi que un
; fallo dejaba la estacion instalada y sin arrancar nunca. Se hace en CurStepChanged.
; schtasks.exe en vez de Start-ScheduledTask: esa cmdlet no existe en Windows 7.
; unchecked a proposito: primero se configura la estacion (--setup), luego se arranca.
Filename: "{sys}\schtasks.exe"; Parameters: "/Run /TN ""QualityLockClient"""; Description: "Iniciar QualityLock ahora"; Flags: runhidden postinstall skipifsilent unchecked

[UninstallRun]
Filename: "{cmd}"; Parameters: "/C taskkill /IM {#AppExeName} /F"; Flags: runhidden waituntilterminated; RunOnceId: "StopQualityLockClient"
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File ""{app}\Register-StationAutostart.ps1"" -Unregister"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveQualityLockClientTask"

[Code]
var
  ConfigPage: TInputQueryWizardPage;
  GuardPage: TInputQueryWizardPage;
  ScanPage: TInputOptionWizardPage;
  // Actualizacion: ya hay config en ProgramData -> no se pregunta nada y se conserva.
  ExistingConfig: Boolean;
  ExistingStation: string;
  ExistingLinea: string;

function ConfigFilePath(): string;
begin
  Result := ExpandConstant('{commonappdata}\QualityLock\appsettings.json');
end;

// Valor de una clave string de primer nivel ("StationCode": "ICT-01"). Sin parser JSON:
// basta para mostrar estacion/linea en el resumen; '' si no la encuentra.
function JsonStringValue(Json, Key: string): string;
var
  P: Integer;
begin
  Result := '';
  P := Pos('"' + Key + '"', Json);
  if P = 0 then exit;
  Json := Copy(Json, P + Length(Key) + 2, MaxInt);
  P := Pos(':', Json);
  if P = 0 then exit;
  Json := Copy(Json, P + 1, MaxInt);
  P := Pos('"', Json);
  if (P = 0) or (Trim(Copy(Json, 1, P - 1)) <> '') then exit;
  Json := Copy(Json, P + 1, MaxInt);
  P := Pos('"', Json);
  if P > 0 then
    Result := Copy(Json, 1, P - 1);
end;

function JsonEscape(Value: string): string;
begin
  Result := Value;
  StringChangeEx(Result, '\', '\\', True);
  StringChangeEx(Result, '"', '\"', True);
  StringChangeEx(Result, #13#10, '\n', True);
  StringChangeEx(Result, #13, '\n', True);
  StringChangeEx(Result, #10, '\n', True);
end;

function IsPositiveInteger(Value: string): Boolean;
var
  Number: Integer;
begin
  Number := StrToIntDef(Trim(Value), -1);
  Result := Number > 0;
end;

function RequiredValue(Index: Integer; LabelText: string): Boolean;
begin
  Result := Trim(ConfigPage.Values[Index]) <> '';
  if not Result then
    MsgBox(LabelText + ' es obligatorio.', mbError, MB_OK);
end;

function BuildConfigJson(): string;
var
  RequireScanValue: string;
begin
  if ScanPage.Values[0] then
    RequireScanValue := 'true'
  else
    RequireScanValue := 'false';

  Result :=
    '{' + #13#10 +
    '  "StationCode": "' + JsonEscape(Trim(ConfigPage.Values[0])) + '",' + #13#10 +
    '  "Linea": "' + JsonEscape(Trim(ConfigPage.Values[1])) + '",' + #13#10 +
    '  "ApiBaseUrl": "' + JsonEscape(Trim(ConfigPage.Values[2])) + '",' + #13#10 +
    '  "BypassHmacSecret": "' + JsonEscape(ConfigPage.Values[4]) + '",' + #13#10 +
    '  "AdminPin": "' + JsonEscape(ConfigPage.Values[5]) + '",' + #13#10 +
    '  "ClientApiKey": "' + JsonEscape(ConfigPage.Values[3]) + '",' + #13#10 +
    '  "AutoLockSeconds": ' + Trim(ConfigPage.Values[6]) + ',' + #13#10 +
    '  "RequireScan": ' + RequireScanValue + ',' + #13#10 +
    '  "ScanMaxAvgKeyMs": ' + Trim(ConfigPage.Values[7]) + ',' + #13#10 +
    '  "WindowAccessGuard": {' + #13#10 +
    '    "Enabled": true,' + #13#10 +
    '    "PollMilliseconds": 500,' + #13#10 +
    '    "Rules": [' + #13#10 +
    '      {' + #13#10 +
    '        "Name": "Ventana protegida ' + JsonEscape(Trim(GuardPage.Values[1])) + '",' + #13#10 +
    '        "ProcessName": "' + JsonEscape(Trim(GuardPage.Values[0])) + '",' + #13#10 +
    '        "WindowTitle": "' + JsonEscape(Trim(GuardPage.Values[1])) + '",' + #13#10 +
    '        "MatchMode": "Exact",' + #13#10 +
    '        "BlockAction": "PromptAuthorization",' + #13#10 +
    '        "AllowedRoles": [ "superadmin", "Tecnico QA" ],' + #13#10 +
    '        "AllowedUsers": []' + #13#10 +
    '      }' + #13#10 +
    '    ]' + #13#10 +
    '  },' + #13#10 +
    '  "QrInputFocus": {' + #13#10 +
    '    "Enabled": false,' + #13#10 +
    '    "DelayMilliseconds": 500,' + #13#10 +
    '    "RetryCount": 3,' + #13#10 +
    '    "ProcessName": "",' + #13#10 +
    '    "WindowTitle": "",' + #13#10 +
    '    "MatchMode": "Exact",' + #13#10 +
    '    "Input": {' + #13#10 +
    '      "AutomationId": "",' + #13#10 +
    '      "Name": "",' + #13#10 +
    '      "ClassName": "",' + #13#10 +
    '      "ControlType": "",' + #13#10 +
    '      "ControlIndex": 0,' + #13#10 +
    '      "NativeWindowHandle": 0,' + #13#10 +
    '      "UseFallbackClick": false,' + #13#10 +
    '      "FallbackClickX": -1,' + #13#10 +
    '      "FallbackClickY": -1' + #13#10 +
    '    }' + #13#10 +
    '  }' + #13#10 +
    '}' + #13#10;
end;

procedure InitializeWizard();
var
  Json: AnsiString;
begin
  ExistingConfig := FileExists(ConfigFilePath());
  if ExistingConfig and LoadStringFromFile(ConfigFilePath(), Json) then
  begin
    ExistingStation := JsonStringValue(String(Json), 'StationCode');
    ExistingLinea := JsonStringValue(String(Json), 'Linea');
  end;

  ConfigPage := CreateInputQueryPage(
    wpSelectDir,
    'Configuracion inicial de estacion',
    'Datos que se guardaran en C:\ProgramData\QualityLock\appsettings.json',
    'Capture la configuracion de esta estacion. Las claves prellenadas vienen del paquete de distribucion.');

  ConfigPage.Add('StationCode:', False);
  ConfigPage.Add('Linea:', False);
  ConfigPage.Add('ApiBaseUrl:', False);
  ConfigPage.Add('ClientApiKey:', True);
  ConfigPage.Add('BypassHmacSecret:', True);
  ConfigPage.Add('AdminPin:', True);
  ConfigPage.Add('AutoLockSeconds:', False);
  ConfigPage.Add('ScanMaxAvgKeyMs:', False);

  ConfigPage.Values[0] := '{#DefaultStationCode}';
  ConfigPage.Values[1] := '{#DefaultLinea}';
  ConfigPage.Values[2] := '{#DefaultApiBaseUrl}';
  ConfigPage.Values[3] := '{#DefaultClientApiKey}';
  ConfigPage.Values[4] := '{#DefaultBypassHmacSecret}';
  ConfigPage.Values[5] := '{#DefaultAdminPin}';
  ConfigPage.Values[6] := '{#DefaultAutoLockSeconds}';
  ConfigPage.Values[7] := '{#DefaultScanMaxAvgKeyMs}';

  GuardPage := CreateInputQueryPage(
    ConfigPage.ID,
    'Ventana protegida',
    'Bloqueo de ventana externa por rol',
    'QualityLock cerrara esta ventana si el usuario actual no es superadmin o Tecnico QA.');

  GuardPage.Add('BlockWindowProcessName:', False);
  GuardPage.Add('BlockWindowTitle:', False);

  GuardPage.Values[0] := '{#DefaultGuardProcessName}';
  GuardPage.Values[1] := '{#DefaultGuardWindowTitle}';

  ScanPage := CreateInputOptionPage(
    GuardPage.ID,
    'Entrada por escaner',
    'Configure la validacion de escaneo.',
    'QualityLock puede rechazar tecleo manual y aceptar solo entrada rapida de scanner.',
    False,
    False);
  ScanPage.Add('Exigir scanner QR para desbloqueo');
  ScanPage.Values[0] := True;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;

  if CurPageID = ConfigPage.ID then
  begin
    if not RequiredValue(0, 'StationCode') then begin Result := False; exit; end;
    if not RequiredValue(1, 'Linea') then begin Result := False; exit; end;
    if not RequiredValue(2, 'ApiBaseUrl') then begin Result := False; exit; end;

    // Sin esquema, la app no puede construir el BaseAddress y no arranca.
    if (Pos('http://', Lowercase(Trim(ConfigPage.Values[2]))) <> 1) and
       (Pos('https://', Lowercase(Trim(ConfigPage.Values[2]))) <> 1) then
    begin
      MsgBox('ApiBaseUrl debe empezar con http:// o https://' + #13#10#13#10 +
             'Ejemplo: http://192.168.1.10:5080/', mbError, MB_OK);
      Result := False;
      exit;
    end;
    if not RequiredValue(3, 'ClientApiKey') then begin Result := False; exit; end;
    if not RequiredValue(4, 'BypassHmacSecret') then begin Result := False; exit; end;
    if not RequiredValue(5, 'AdminPin') then begin Result := False; exit; end;

    if not IsPositiveInteger(ConfigPage.Values[6]) then
    begin
      MsgBox('AutoLockSeconds debe ser un entero mayor a cero.', mbError, MB_OK);
      Result := False;
      exit;
    end;

    if not IsPositiveInteger(ConfigPage.Values[7]) then
    begin
      MsgBox('ScanMaxAvgKeyMs debe ser un entero mayor a cero.', mbError, MB_OK);
      Result := False;
      exit;
    end;
  end;

  if CurPageID = GuardPage.ID then
  begin
    Result := Trim(GuardPage.Values[0]) <> '';
    if not Result then
    begin
      MsgBox('BlockWindowProcessName es obligatorio.', mbError, MB_OK);
      exit;
    end;

    Result := Trim(GuardPage.Values[1]) <> '';
    if not Result then
    begin
      MsgBox('BlockWindowTitle es obligatorio.', mbError, MB_OK);
      exit;
    end;
  end;
end;

// Al actualizar, las paginas de configuracion no se muestran: sus valores no se usarian
// (CurStepChanged solo escribe la config si no existe) y confundian.
function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := ExistingConfig and
    ((PageID = ConfigPage.ID) or (PageID = GuardPage.ID) or (PageID = ScanPage.ID));
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo,
  MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  Result := MemoDirInfo + NewLine + NewLine;
  if ExistingConfig then
    Result := Result + 'Configuracion: se conserva la actual (no se modifica)' + NewLine +
      Space + 'Estacion: ' + ExistingStation + '   Linea: ' + ExistingLinea + NewLine +
      Space + ConfigFilePath()
  else
    Result := Result + 'Configuracion: nueva' + NewLine +
      Space + 'Estacion: ' + Trim(ConfigPage.Values[0]) + '   Linea: ' + Trim(ConfigPage.Values[1]) + NewLine +
      Space + ConfigFilePath();
end;

function PrepareToInstall(var NeedsRestart: Boolean): string;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{cmd}'), '/C taskkill /IM {#AppExeName} /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  // schtasks.exe en vez de Unregister-ScheduledTask: esa cmdlet no existe en Windows 7.
  Exec(ExpandConstant('{sys}\schtasks.exe'), '/Delete /TN "QualityLockClient" /F',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;

procedure RegisterAutostart();
var
  ResultCode: Integer;
  Ok: Boolean;
begin
  Ok := Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "' +
      ExpandConstant('{app}\Register-StationAutostart.ps1') + '" -ExecutablePath "' +
      ExpandConstant('{app}\{#AppExeName}') + '"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  if Ok and (ResultCode = 0) then
    exit;

  MsgBox('No se pudo registrar el inicio automatico de QualityLock (codigo ' +
    IntToStr(ResultCode) + ').' + #13#10#13#10 +
    'La estacion queda instalada pero NO arrancara sola. Ejecute como admin:' + #13#10#13#10 +
    'powershell -ExecutionPolicy Bypass -File "' +
      ExpandConstant('{app}\Register-StationAutostart.ps1') + '" -ExecutablePath "' +
      ExpandConstant('{app}\{#AppExeName}') + '"',
    mbError, MB_OK);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ConfigDir: string;
  ConfigPath: string;
begin
  if CurStep = ssPostInstall then
  begin
    ConfigDir := ExpandConstant('{commonappdata}\QualityLock');
    ConfigPath := ConfigFilePath();
    ForceDirectories(ConfigDir);
    // Solo escribir la config en la PRIMERA instalacion. Al reinstalar/actualizar, la
    // config existente es la fuente de verdad (se edita con --setup, que guarda TODO:
    // QrInputFocus/autoclick, reglas del guard, etc.) y el asistente del instalador no
    // captura esos campos -> sobrescribir aqui los borraba.
    if not FileExists(ConfigPath) then
      SaveStringToFile(ConfigPath, BuildConfigJson(), False);

    RegisterAutostart();
  end;
end;
