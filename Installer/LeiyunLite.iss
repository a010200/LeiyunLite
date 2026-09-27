#ifndef BuildSource
  #error BuildSource is required
#endif
#ifndef AppVersion
  #error AppVersion is required
#endif
#ifndef OutputRoot
  #error OutputRoot is required
#endif
#ifdef TestInstall
  #define ProductName "雷云lite 安装测试"
  #define ProductId "LeiyunLite-InstallTest"
  #define AppMutexName "Local\LeiyunLite.InstallTest.App"
#else
  #define ProductName "雷云lite"
  #define ProductId "LeiyunLite"
  #define AppMutexName "Local\LeiyunLite.v1"
#endif

[Setup]
AppId={#ProductId}
AppName={#ProductName}
AppVersion={#AppVersion}
AppPublisher=a010200
AppPublisherURL=https://github.com/a010200/LeiyunLite
AppSupportURL=https://github.com/a010200/LeiyunLite/issues
DefaultDirName={localappdata}\Programs\{#ProductId}
DefaultGroupName={#ProductName}
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
WizardStyle=modern dark includetitlebar
WizardBackColor=$141414
SetupIconFile={#BuildSource}\Lite.ico
UninstallDisplayIcon={app}\LeiyunLite.exe
LicenseFile={#BuildSource}\LICENSE
OutputDir={#OutputRoot}
#ifdef TestInstall
OutputBaseFilename=LeiyunLite-v{#AppVersion}-Setup-TEST-x64
#else
OutputBaseFilename=LeiyunLite-v{#AppVersion}-Setup-x64
#endif
Compression=lzma2
SolidCompression=yes
AppMutex={#AppMutexName}
CloseApplications=no
RestartApplications=no
AlwaysRestart=no
DisableProgramGroupPage=yes
DisableDirPage=no
UsePreviousAppDir=yes
UsePreviousTasks=yes
UninstallLogMode=append

[Languages]
Name: "zh"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
zh.DesktopShortcut=创建桌面快捷方式
en.DesktopShortcut=Create a desktop shortcut
zh.Startup=开机自启动
en.Startup=Launch at sign-in
zh.Launch=运行雷云lite
en.Launch=Launch LeiyunLite
zh.KeepData=卸载默认保留宏和个人设置。
en.KeepData=Uninstall keeps macros and preferences by default.

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopShortcut}"; Flags: unchecked
Name: "autostart"; Description: "{cm:Startup}"; Flags: unchecked

[Files]
Source: "{#BuildSource}\LeiyunLite.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildSource}\install.id"; DestDir: "{app}"; Flags: onlyifdoesntexist uninsneveruninstall
Source: "{#BuildSource}\current.json"; DestDir: "{app}"; Flags: onlyifdoesntexist uninsneveruninstall
Source: "{#BuildSource}\payload\*"; DestDir: "{app}\versions\{#AppVersion}"; Flags: ignoreversion
Source: "{#BuildSource}\LICENSE"; DestDir: "{app}"
Source: "{#BuildSource}\README.md"; DestDir: "{app}"

[Icons]
Name: "{autoprograms}\{#ProductName}"; Filename: "{app}\LeiyunLite.exe"
Name: "{autodesktop}\{#ProductName}"; Filename: "{app}\LeiyunLite.exe"; Tasks: desktopicon

[Run]
#ifndef TestInstall
Filename: "{app}\LeiyunLite.exe"; Description: "{cm:Launch}"; Flags: nowait postinstall skipifsilent; Check: NotActivated
#endif

[Code]
var
  WasUpgrade, ActivationFailed, Activated: Boolean;
  InstallGuard: THandle;
function CreateMutexW(Attributes: Integer; InitialOwner: Boolean; Name: String): THandle;
  external 'CreateMutexW@kernel32.dll stdcall';
function ReleaseMutex(Handle: THandle): Boolean; external 'ReleaseMutex@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): Boolean; external 'CloseHandle@kernel32.dll stdcall';
function GetFileAttributesW(Name: String): Cardinal; external 'GetFileAttributesW@kernel32.dll stdcall';

function SafeDirectory: Boolean;
var
  P, Parent: String;
  Attr: Cardinal;
  Find: TFindRec;
begin
  Result := False;
  P := RemoveBackslashUnlessRoot(ExpandConstant('{app}'));
  if (Length(P) < 8) or (Copy(P, 1, 2) = '\\') or
     (CompareText(P, GetEnv('USERPROFILE')) = 0) or
     (CompareText(P, ExpandConstant('{localappdata}')) = 0) or
     (CompareText(P, ExpandConstant('{win}')) = 0) then Exit;
  while Length(P) > 3 do begin
    Attr := GetFileAttributesW(P);
    if (Attr <> $FFFFFFFF) and ((Attr and $400) <> 0) then Exit;
    Parent := ExtractFileDir(P); if Parent = P then Exit; P := Parent;
  end;
  if DirExists(ExpandConstant('{app}')) and not FileExists(ExpandConstant('{app}\install.id')) then begin
    if FindFirst(ExpandConstant('{app}\*'), Find) then begin
      try
        repeat
          if (Find.Name <> '.') and (Find.Name <> '..') then Exit;
        until not FindNext(Find);
      finally FindClose(Find); end;
    end;
  end;
  Result := True;
end;

procedure Unlock;
begin
  if InstallGuard <> 0 then begin
    ReleaseMutex(InstallGuard); CloseHandle(InstallGuard); InstallGuard := 0;
  end;
end;

function InitializeSetup: Boolean;
var
  Release: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release) and (Release >= 528040);
  if not Result then
    SuppressibleMsgBox('需要 Microsoft .NET Framework 4.8。请从微软官网下载并安装后重试。安装器不会静默安装系统组件或重启电脑。', mbError, MB_OK, IDOK);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExitCode: Integer;
begin
  Result := '';
  if not SafeDirectory then begin Result := '请选择新的空目录或已有雷云lite安装目录，不支持系统目录或目录联接。'; Exit; end;
  if InstallGuard = 0 then begin
    InstallGuard := CreateMutexW(0, True, 'Local\LeiyunLite.Install');
    if (InstallGuard = 0) or (DLLGetLastError = 183) then begin
      if InstallGuard <> 0 then CloseHandle(InstallGuard);
      InstallGuard := 0; Result := '其他安装或更新正在进行，请稍后重试。'; Exit;
    end;
  end;
  WasUpgrade := FileExists(ExpandConstant('{app}\current.json'));
  if WasUpgrade then begin
#ifndef TestInstall
    if not Exec(ExpandConstant('{app}\LeiyunLite.exe'), '--preflight "{#AppVersion}"', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) or (ExitCode <> 0) then
      Result := '安装状态异常或目标版本较旧。请先退出雷云lite，检查当前版本后重试。';
#endif
  end;
end;

function NotActivated: Boolean;
begin
  Result := not Activated and not ActivationFailed;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ExitCode: Integer;
  StartupCommand: String;
begin
  if CurStep = ssPostInstall then begin
    Unlock;
#ifndef TestInstall
    if WasUpgrade then begin
      Activated := True;
      ActivationFailed := not Exec(ExpandConstant('{app}\LeiyunLite.exe'), '--activate "{#AppVersion}"', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) or (ExitCode <> 0);
      if ActivationFailed then SuppressibleMsgBox('新版未能启用，旧版本保留。请查看安装目录中的 last-update-error.log。', mbError, MB_OK, IDOK);
    end;
    if not ActivationFailed then begin
      if WizardIsTaskSelected('autostart') or RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'RazerBatteryTray', StartupCommand) then
        RegWriteStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'RazerBatteryTray', ExpandConstant('"{app}\LeiyunLite.exe" --autostart'));
    end;
#endif
  end;
end;

function GetCustomSetupExitCode: Integer;
begin
  if ActivationFailed then Result := 20 else Result := 0;
end;

procedure DeinitializeSetup;
begin
  Unlock;
end;

function InitializeUninstall: Boolean;
begin
  Result := not CheckForMutexes('{#AppMutexName}') and not CheckForMutexes('Local\LeiyunLite.Install');
  if not Result then begin
    SuppressibleMsgBox('请先退出雷云lite并等待更新完成。不会强制结束程序。', mbError, MB_OK, IDOK); Exit;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ExitCode: Integer;
  StartupCommand: String;
begin
  if CurUninstallStep = usUninstall then begin
#ifndef TestInstall
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'RazerBatteryTray', StartupCommand) and
      (CompareText(StartupCommand, ExpandConstant('"{app}\LeiyunLite.exe" --autostart')) = 0) then
      RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'RazerBatteryTray');
    if not Exec(ExpandConstant('{app}\LeiyunLite.exe'), '--uninstall-clean', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) or (ExitCode <> 0) then
      RaiseException('清理失败；未删除个人宏和设置。请检查目录权限与更新状态。');
    if not UninstallSilent then
      if MsgBox('默认保留个人数据。是否同时删除宏、绑定、偏好及更新备份？此操作不可撤销。', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        if not Exec(ExpandConstant('{app}\LeiyunLite.exe'), '--purge-user-data', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) or (ExitCode <> 0) then
          MsgBox('个人数据未能全部删除，请手动检查。', mbError, MB_OK);
#endif
  end;
  if CurUninstallStep = usPostUninstall then begin
    DeleteFile(ExpandConstant('{app}\current.json'));
    DeleteFile(ExpandConstant('{app}\install.id'));
    RemoveDir(ExpandConstant('{app}'));
  end;
end;
