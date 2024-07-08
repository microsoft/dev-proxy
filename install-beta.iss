; Script generated by the Inno Setup Script Wizard.
; SEE THE DOCUMENTATION FOR DETAILS ON CREATING INNO SETUP SCRIPT FILES!

#define MyAppName "Dev Proxy Beta"
; for local use only. In production replaced by a command line arg
#define MyAppSetupExeName "dev-proxy-installer-win-x64-0.20.0-beta.1"
#define MyAppVersion "0.20.0-beta.1"
#define MyAppPublisher "Microsoft"
#define MyAppURL "https://aka.ms/devproxy"

[Setup]
; NOTE: The value of AppId uniquely identifies this application. Do not use the same AppId value in installers for other applications.
; (To generate a new GUID, click Tools | Generate GUID inside the IDE.)
AppId={{4448FDE7-D519-4009-AFE2-0C5D0AA9C3D3}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
AppVerName={#MyAppName} v{#MyAppVersion}
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\icon-beta.ico
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
; Remove the following line to run in administrative install mode (install for all users.)
PrivilegesRequired=lowest
OutputBaseFilename={#MyAppSetupExeName}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
OutputDir=.
ChangesEnvironment=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: ".\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.iss"

; NOTE: Don't use "Flags: ignoreversion" on any shared system files

[UninstallDelete]
Type:files;Name:"{app}\rootCert.pfx"

[Code]
procedure RemovePath(Path: string);
var
  Paths: string;
begin
  if RegQueryStringValue(HKCU, 'Environment', 'Path', Paths) then
  begin
    StringChangeEx(Paths, ';' + Path + ';', ';', true);
    RegWriteStringValue(HKCU, 'Environment', 'Path', Paths);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    RemovePath(ExpandConstant('{app}'));
  end;
end;

procedure AddPath(Path: string);
var
  Paths: string;
begin
  if RegQueryStringValue(HKCU, 'Environment', 'Path', Paths) then
  begin
    if Pos(';' + Path + ';', Paths) < 1 then
    begin
      Paths := Paths + ';' + Path + ';'
      StringChangeEx(Paths, ';;', ';', true);
      RegWriteStringValue(HKCU, 'Environment', 'Path', Paths);
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    AddPath(ExpandConstant('{app}'));
  end;
end;