; Inno Setup script for Docs2MD — per-user installer (no admin required).
; Build:  "%LocalAppData%\Programs\Inno Setup 6\ISCC.exe" installer\Docs2MD.iss
; Produces: dist\Docs2MD-Setup.exe
;
; Packages the published single-file exe from dist\ (run build.cmd first), installs
; per-user to %LocalAppData%\Programs\Docs2MD, and creates: a Start Menu shortcut,
; the "Convert to MD" Send To entry, right-click "Convert to MD here" registry verbs
; for PDF/DOCX/XLSX, and an Add/Remove Programs uninstall entry. Checks for the
; .NET 8 Desktop Runtime first.

#define AppName "Docs2MD"
#define AppVersion "1.0.0"
#define AppExe "Docs2MD.exe"
#define AppPublisher "Wise & Agile Solutions Limited"

[Setup]
AppId={{7C4A8D09-CA34-4E1B-9E7F-2B3C5D6E8F10}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={localappdata}\Programs\{#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename={#AppName}-Setup
SetupIconFile=..\Docs2MD.Desktop\app.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Files]
Source: "..\dist\{#AppExe}";   DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\tessdata\*";  DestDir: "{app}\tessdata"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}";   Filename: "{app}\{#AppExe}"
Name: "{usersendto}\Convert to MD";  Filename: "{app}\{#AppExe}"; IconFilename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}";    Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Tasks]
Name: desktopicon; Description: "Create a &desktop shortcut"; Flags: unchecked

[Registry]
; Right-click "Convert to MD here" for the supported types (shown on standard shells).
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.pdf\shell\Docs2MDConvert";          ValueType: string; ValueName: ""; ValueData: "Convert to MD here"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.pdf\shell\Docs2MDConvert";          ValueType: string; ValueName: "Icon"; ValueData: "{app}\{#AppExe}"
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.pdf\shell\Docs2MDConvert\command";  ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.docx\shell\Docs2MDConvert";         ValueType: string; ValueName: ""; ValueData: "Convert to MD here"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.docx\shell\Docs2MDConvert";         ValueType: string; ValueName: "Icon"; ValueData: "{app}\{#AppExe}"
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.docx\shell\Docs2MDConvert\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.xlsx\shell\Docs2MDConvert";         ValueType: string; ValueName: ""; ValueData: "Convert to MD here"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.xlsx\shell\Docs2MDConvert";         ValueType: string; ValueName: "Icon"; ValueData: "{app}\{#AppExe}"
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.xlsx\shell\Docs2MDConvert\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.xls\shell\Docs2MDConvert";          ValueType: string; ValueName: ""; ValueData: "Convert to MD here"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.xls\shell\Docs2MDConvert";          ValueType: string; ValueName: "Icon"; ValueData: "{app}\{#AppExe}"
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.xls\shell\Docs2MDConvert\command";  ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[Code]
function DotNet8DesktopPresent(): Boolean;
var
  fd: TFindRec;
begin
  Result := False;
  if FindFirst(ExpandConstant('{commonpf}\dotnet\shared\Microsoft.WindowsDesktop.App\8.*'), fd) then
  begin
    Result := True;
    FindClose(fd);
  end;
end;

function InitializeSetup(): Boolean;
var
  ec: Integer;
begin
  Result := True;
  if not DotNet8DesktopPresent() then
  begin
    if MsgBox('Docs2MD needs the .NET 8 Desktop Runtime, which was not found.' + #13#10 + #13#10 +
              'Open the download page now? Install it, then run this setup again.',
              mbConfirmation, MB_YESNO) = IDYES then
    begin
      ShellExec('open',
        'https://dotnet.microsoft.com/en-us/download/dotnet/8.0/runtime?cid=getdotnetcore&os=windows&arch=x64',
        '', '', SW_SHOW, ewNoWait, ec);
      Result := False;
    end;
  end;
end;
