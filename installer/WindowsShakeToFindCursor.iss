; Windows Shake to Find Cursor installer.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef Arch
  #define Arch "x64"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish\win-" + Arch
#endif

[Setup]
AppId={{1F1E3E1D-A8F8-4E10-B474-3C90E4A0C63F}
AppName=Windows Shake to Find Cursor
AppVersion={#AppVersion}
AppPublisher=Bradley Wyatt
AppPublisherURL=https://github.com/bwya77/Windows-shake-to-find-cursor
AppSupportURL=https://github.com/bwya77/Windows-shake-to-find-cursor/issues
AppUpdatesURL=https://github.com/bwya77/Windows-shake-to-find-cursor/releases
DefaultDirName={autopf}\Windows Shake to Find Cursor
DefaultGroupName=Windows Shake to Find Cursor
DisableProgramGroupPage=yes
DisableDirPage=auto
UninstallDisplayIcon={app}\WindowsShakeToFindCursor.exe
UninstallDisplayName=Windows Shake to Find Cursor
SetupIconFile=..\Assets\icon.ico
OutputBaseFilename=WindowsShakeToFindCursorSetup-{#AppVersion}-win-{#Arch}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
CloseApplications=yes
RestartApplications=no
#if Arch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
#endif

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{group}\Windows Shake to Find Cursor"; Filename: "{app}\WindowsShakeToFindCursor.exe"
Name: "{autodesktop}\Windows Shake to Find Cursor"; Filename: "{app}\WindowsShakeToFindCursor.exe"; Tasks: desktopicon

[Tasks]
Name: "startupwithwindows"; Description: "Start Windows Shake to Find Cursor when I sign in to Windows"; GroupDescription: "Startup:"
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Run]
Filename: "{app}\WindowsShakeToFindCursor.exe"; Parameters: "--enable-startup"; Description: "Launch Windows Shake to Find Cursor"; Tasks: startupwithwindows; Flags: nowait postinstall skipifsilent runasoriginaluser
Filename: "{app}\WindowsShakeToFindCursor.exe"; Description: "Launch Windows Shake to Find Cursor"; Tasks: not startupwithwindows; Flags: nowait postinstall skipifsilent runasoriginaluser
Filename: "{app}\WindowsShakeToFindCursor.exe"; Flags: nowait runasoriginaluser; Check: WizardSilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'WindowsShakeToFindCursor');
end;
