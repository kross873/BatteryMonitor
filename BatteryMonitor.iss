; Battery Monitor per-user installer. No scripts or compilation on recipient PC.
[Setup]
AppId=BatteryMonitor.094.Release
AppName=Battery Monitor
AppVersion=0.11.1
DefaultDirName={localappdata}\Programs\BatteryMonitor
DefaultGroupName=Battery Monitor
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
OutputDir=release
OutputBaseFilename=BatteryMonitor-0.11.1-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ShowLanguageDialog=no
SetupIconFile=BatteryMonitor.ico
UninstallDisplayIcon={app}\BatteryMonitor.exe
AppMutex=Local\BatteryMonitor.App.094
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "autostart"; Description: "{cm:AutoStartApp}"; Flags: unchecked

[CustomMessages]
english.AutoStartApp=Start Battery Monitor with Windows
english.ModeTitle=Widget mode
english.ModeDescription=Choose how Battery Monitor displays charge on the taskbar.
english.ModeHint=Standalone needs no external app. Windhawk requires the separate Windhawk app and the supplied local mod (one-time manual compilation).
english.ModeStandalone=Without Windhawk (standalone)
english.ModeWindhawk=With Windhawk (native taskbar integration)
english.OpenGuide=Open Windhawk setup instructions
english.GuideIcon=Windhawk setup instructions

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

Source: "Windhawk\battery-monitor-native-0.4.0-multi-device.wh.cpp"; DestDir: "{app}\Windhawk"; Flags: ignoreversion; Check: IsWindhawkMode
Source: "Windhawk\SETUP.txt"; DestDir: "{app}\Windhawk"; Flags: ignoreversion; Check: IsWindhawkMode

[Icons]
Name: "{group}\{cm:GuideIcon}"; Filename: "{app}\Windhawk\SETUP.txt"; Check: IsWindhawkMode
Name: "{group}\Battery Monitor"; Filename: "{app}\BatteryMonitor.exe"; WorkingDir: "{app}"
Name: "{group}\{cm:UninstallProgram,Battery Monitor}"; Filename: "{uninstallexe}"

[Registry]
Root: HKCU; Subkey: "Software\BatteryMonitor\Settings"; ValueType: string; ValueName: "InstallationMode"; ValueData: "{code:SelectedMode}"; Flags: uninsdeletevalue
; Reset fallback only when choosing a different widget mode.
Root: HKCU; Subkey: "Software\BatteryMonitor\Settings"; ValueType: dword; ValueName: "TrayOnly"; ValueData: "0"; Check: ShouldResetTrayOnly
; Never allow previous readings to survive a mode switch or uninstall.
Root: HKCU; Subkey: "Software\BatteryMonitor\NativeWidget"; ValueType: none; ValueName: "MouseReading"; Flags: deletevalue uninsdeletevalue
Root: HKCU; Subkey: "Software\BatteryMonitor\NativeWidget"; ValueType: none; ValueName: "DeviceReadingsV2"; Flags: deletevalue uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "BatteryMonitor"; ValueData: """{app}\BatteryMonitor.exe"""; Tasks: autostart; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "BatteryMonitor"; Tasks: not autostart; Flags: deletevalue

[Run]
Filename: "{app}\Windhawk\SETUP.txt"; Description: "{cm:OpenGuide}"; Flags: shellexec nowait postinstall skipifsilent; Check: IsWindhawkMode
Filename: "{app}\BatteryMonitor.exe"; Description: "{cm:LaunchProgram,Battery Monitor}"; Flags: nowait postinstall skipifsilent

[Code]
var
  ModePage: TInputOptionWizardPage;
  InitialMode: String;

procedure InitializeWizard;
var
  PreviousMode: String;
begin
  ModePage := CreateInputOptionPage(wpSelectDir, CustomMessage('ModeTitle'),
    CustomMessage('ModeDescription'), CustomMessage('ModeHint'), True, False);
  ModePage.Add(CustomMessage('ModeStandalone'));
  ModePage.Add(CustomMessage('ModeWindhawk'));
  ModePage.SelectedValueIndex := 0;
  InitialMode := '';
  if RegQueryStringValue(HKCU, 'Software\BatteryMonitor\Settings', 'InstallationMode', PreviousMode) then
  begin
    InitialMode := PreviousMode;
    if PreviousMode = 'windhawk' then
      ModePage.SelectedValueIndex := 1;
  end;
end;

function IsWindhawkMode: Boolean;
begin
  Result := ModePage.SelectedValueIndex = 1;
end;

function SelectedMode(Param: String): String;
begin
  if IsWindhawkMode then Result := 'windhawk' else Result := 'standalone';
end;

function ShouldResetTrayOnly: Boolean;
begin
  Result := InitialMode <> SelectedMode('');
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo,
  MemoTypeInfo, MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
var
  ModeText: String;
begin
  if IsWindhawkMode then ModeText := CustomMessage('ModeWindhawk')
  else ModeText := CustomMessage('ModeStandalone');
  Result := MemoUserInfoInfo + MemoDirInfo + NewLine + NewLine +
    CustomMessage('ModeTitle') + ':' + NewLine + Space + ModeText + NewLine + NewLine +
    MemoTypeInfo + MemoComponentsInfo + MemoGroupInfo + MemoTasksInfo;
end;
