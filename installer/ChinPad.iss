; Inno Setup 6. Built by tools\release.ps1; no user settings are included.
#ifndef AppVersion
  #error "Pass /DAppVersion=<version> from ChinPad.csproj."
#endif
#ifndef SourceDir
  #error "Pass /DSourceDir=<clean publish staging folder>."
#endif
#ifndef ArtifactDir
  #error "Pass /DArtifactDir=<artifact staging folder>."
#endif
#define HasSerbianLatin FileExists(AddBackslash(CompilerPath) + "Languages\SerbianLatin.isl")

[Setup]
AppId={{0D7755AB-873C-4D5D-ADAC-BEC8C5454C91}
AppName=ChinPad
AppVersion={#AppVersion}
AppPublisher=Marko Galjak
AppCopyright=Copyright 2026 Marko Galjak
DefaultDirName={localappdata}\Programs\ChinPad
DefaultGroupName=ChinPad
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
OutputDir={#ArtifactDir}
OutputBaseFilename=ChinPad-{#AppVersion}-win-x64-setup
LicenseFile={#SourceDir}\LICENSE
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\ChinPad.exe
AppMutex=Local\ChinPad.Background
CloseApplications=no
RestartApplications=no

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
#if HasSerbianLatin
Name: "sr"; MessagesFile: "compiler:Languages\SerbianLatin.isl"
#else
; Some Inno Setup installations omit SerbianLatin.isl: offer English only.
#endif

[CustomMessages]
en.StartAtLogin=Start ChinPad when I sign in
en.LaunchChinPad=Launch ChinPad
en.SettingsKept=ChinPad was removed. Your settings and logs have been kept in: %1
#if HasSerbianLatin
sr.StartAtLogin=Pokreni ChinPad kada se prijavim
sr.LaunchChinPad=Pokreni ChinPad
sr.SettingsKept=ChinPad je uklonjen. Podešavanja i dnevnik sačuvani su ovde: %1
#endif

[Tasks]
Name: "startatlogin"; Description: "{cm:StartAtLogin}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\ChinPad.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\CHANGELOG.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\ChinPad"; Filename: "{app}\ChinPad.exe"; WorkingDir: "{app}"

[Run]
Filename: "{app}\ChinPad.exe"; Parameters: "--enable-start-at-login"; Tasks: startatlogin; Flags: runhidden waituntilterminated
Filename: "{app}\ChinPad.exe"; Description: "{cm:LaunchChinPad}"; Flags: postinstall nowait skipifsilent

[UninstallRun]
Filename: "{app}\ChinPad.exe"; Parameters: "--disable-start-at-login"; RunOnceId: "DisableChinPadStartAtLogin"; Flags: runhidden waituntilterminated skipifdoesntexist

; Only files installed above are removed. There is deliberately no AppData [UninstallDelete].
[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  KeptMessage: String;
begin
  if CurUninstallStep = usDone then
  begin
    KeptMessage := FmtMessage(CustomMessage('SettingsKept'), [ExpandConstant('{localappdata}\ChinPad')]);
    Log(KeptMessage);
    if not UninstallSilent then
      MsgBox(KeptMessage, mbInformation, MB_OK);
  end;
end;

