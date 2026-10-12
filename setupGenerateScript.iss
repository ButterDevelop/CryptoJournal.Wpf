#define MyAppName "CryptoJournal"
#ifndef MyAppVersion
#define MyAppVersion "1.0.6"
#endif
#define MyAppPublisher "ButterDevelop"
#define MyAppURL "https://github.com/ButterDevelop/CryptoJournal.Wpf"
#define MyAppExeName "CryptoJournal.Wpf.exe"
#define MyAppDataFolderName "CryptoJournal"
#ifndef MyPublishDir
#define MyPublishDir ".\bin\publish\win-x64"
#endif
#ifndef MyOutputDir
#define MyOutputDir ".\Output"
#endif

[Setup]
AppId={{5E7D3D4C-6E22-4F54-8C9D-1E3BFEA8A8D1}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
AppMutex=CryptoJournal-5E7D3D4C-6E22-4F54-8C9D-1E3BFEA8A8D1

; By default - Program Files, but the user can choose it himself
; In current-user mode, {autopf} will automatically become userpf
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
UsePreviousAppDir=yes

OutputDir={#MyOutputDir}
OutputBaseFilename=Setup_CryptoJournal_{#MyAppVersion}

Compression=lzma
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Default is admin install, but user will be able to choose
; "just for me" via built-in dialog
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog

; The application can be installed for all users, while each Windows account keeps
; its own portfolio and settings in that account's LocalAppData directory.
UsedUserAreasWarning=no

UninstallDisplayIcon={app}\{#MyAppExeName}
DisableProgramGroupPage=yes

VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=CryptoJournal installer
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}

; if there is a signature here:
; SignTool=mycustom
; SignedUninstaller=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[CustomMessages]
; ----- English -----
english.AppMutexName=CryptoJournal
english.DataFolderDeletePrompt=Also delete the "{#MyAppDataFolderName}" folder?%n%nYes - remove the program and its data.%nNo - remove only the program and keep the data.%nCancel - abort uninstall.
english.DataFolderDeleteTitle=Remove application data
english.DataFolderCreateInfo=Application data will be stored in the current user's LocalAppData folder.
english.DesktopIconTask=Create a desktop shortcut
english.NewerVersionInstalled=CryptoJournal %1 is already installed. Uninstall it before installing an older version.

; ----- Russian -----
russian.AppMutexName=CryptoJournal
russian.DataFolderDeletePrompt=Также удалить папку "{#MyAppDataFolderName}"?%n%nДа - удалить программу и данные.%nНет - удалить только программу, данные оставить.%nОтмена - прервать удаление.
russian.DataFolderDeleteTitle=Удаление данных приложения
russian.DataFolderCreateInfo=Данные приложения будут храниться в LocalAppData текущего пользователя.
russian.DesktopIconTask=Создать ярлык на рабочем столе
russian.NewerVersionInstalled=Уже установлена более новая версия CryptoJournal %1. Удалите её перед установкой старой версии.

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIconTask}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#MyPublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Dirs]
Name: "{localappdata}\{#MyAppDataFolderName}"

[InstallDelete]
Type: filesandordirs; Name: "{app}\cs"
Type: filesandordirs; Name: "{app}\de"
Type: filesandordirs; Name: "{app}\es"
Type: filesandordirs; Name: "{app}\fr"
Type: filesandordirs; Name: "{app}\it"
Type: filesandordirs; Name: "{app}\ja"
Type: filesandordirs; Name: "{app}\ko"
Type: filesandordirs; Name: "{app}\pl"
Type: filesandordirs; Name: "{app}\pt-BR"
Type: filesandordirs; Name: "{app}\ru"
Type: filesandordirs; Name: "{app}\tr"
Type: filesandordirs; Name: "{app}\zh-Hans"
Type: filesandordirs; Name: "{app}\zh-Hant"

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; WorkingDir: "{app}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[Code]
var
  RemoveUserData: Boolean;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  InstalledVersion: Int64;
  SetupVersion: Int64;
  InstalledFile: string;
begin
  Result := '';
  InstalledFile := AddBackslash(WizardDirValue) + '{#MyAppExeName}';

  if GetPackedVersion(InstalledFile, InstalledVersion) and
     StrToVersion('{#MyAppVersion}', SetupVersion) and
     (ComparePackedVersion(InstalledVersion, SetupVersion) > 0) then
  begin
    Result := FmtMessage(CustomMessage('NewerVersionInstalled'), [VersionToStr(InstalledVersion)]);
  end;
end;

function GetUserDataDir: string;
begin
  Result := ExpandConstant('{localappdata}\{#MyAppDataFolderName}');
end;

function InitializeUninstall: Boolean;
var
  Answer: Integer;
begin
  Answer :=
    MsgBox(
      CustomMessage('DataFolderDeletePrompt'),
      mbConfirmation,
      MB_YESNOCANCEL
    );

  if Answer = IDCANCEL then
  begin
    Result := False;
    Exit;
  end;

  RemoveUserData := (Answer = IDYES);
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and RemoveUserData then
  begin
    if DirExists(GetUserDataDir) then
      DelTree(GetUserDataDir, True, True, True);
  end;
end;
