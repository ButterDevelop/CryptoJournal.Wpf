#define MyAppName "CryptoJournal"
#define MyAppVersion "1.0.3"
#define MyAppPublisher "ButterDevelop"
#define MyAppURL "https://github.com/ButterDevelop/CryptoJournal.Wpf"
#define MyAppExeName "CryptoJournal.Wpf.exe"
#define MyAppDataFolderName "CryptoJournal_data"

[Setup]
AppId={{5E7D3D4C-6E22-4F54-8C9D-1E3BFEA8A8D1}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}

; By default - Program Files, but the user can choose it himself
; In current-user mode, {autopf} will automatically become userpf
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}

OutputDir=.\Output
OutputBaseFilename=Setup_CryptoJournal_{#MyAppVersion}

Compression=lzma
SolidCompression=yes
WizardStyle=modern

ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64

; Default is admin install, but user will be able to choose
; "just for me" via built-in dialog
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog

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
english.DataFolderCreateInfo=The application data folder will be stored inside the selected installation folder.
english.DesktopIconTask=Create a desktop shortcut

; ----- Russian -----
russian.AppMutexName=CryptoJournal
russian.DataFolderDeletePrompt=Также удалить папку "{#MyAppDataFolderName}"?%n%nДа - удалить программу и данные.%nНет - удалить только программу, данные оставить.%nОтмена - прервать удаление.
russian.DataFolderDeleteTitle=Удаление данных приложения
russian.DataFolderCreateInfo=Папка данных приложения будет храниться внутри выбранной папки установки.
russian.DesktopIconTask=Создать ярлык на рабочем столе

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIconTask}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: ".\bin\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Dirs]
Name: "{app}\{#MyAppDataFolderName}"

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; WorkingDir: "{app}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[Code]
var
  RemoveUserData: Boolean;

function GetUserDataDir: string;
begin
  Result := ExpandConstant('{app}\{#MyAppDataFolderName}');
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