; Inno Setup Script for Fast Ethernet File Transfer v2.0
#define MyAppName "Fast Ethernet File Transfer"
#define MyAppVersion "2.0.0"
#define MyAppPublisher "FastEthernetTransfer"
#define MyAppExeName "FastEthernetTransfer.exe"

[Setup]
AppId={{D9B78B81-54E1-4E6D-A81E-4731308365D0}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\FastEthernetTransfer
DefaultGroupName={#MyAppName}
OutputBaseFilename=FastEthernetTransfer-Setup-win-x64
OutputDir=..\dist
SetupIconFile=..\newappicon.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
; Allows installing per-user without admin privileges or per-machine if elevated
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\publish\win-x64\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\newappicon.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; Automatically configure Windows Defender Firewall exception if running elevated
Filename: "netsh"; Parameters: "advfirewall firewall add rule name=""{#MyAppName}"" dir=in action=allow program=""{app}\{#MyAppExeName}"" enable=yes profile=any"; Flags: runhidden; Check: IsAdminLoggedOn
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "netsh"; Parameters: "advfirewall firewall delete rule name=""{#MyAppName}"""; Flags: runhidden; Check: IsAdminLoggedOn
