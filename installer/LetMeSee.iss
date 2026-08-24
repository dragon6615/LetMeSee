; LetMeSee 安裝程式腳本（Inno Setup）
;
; 本機編譯：
;   "C:\Program Files\Inno Setup 7\ISCC.exe" installer\LetMeSee.iss
; 指定版本（CI 會這樣做）：
;   "C:\Program Files\Inno Setup 7\ISCC.exe" /DAppVersion=1.1.0 installer\LetMeSee.iss
;
; 編譯前必須先 publish：
;   dotnet publish -c Release -r win-x64 --self-contained true

#ifndef AppVersion
  #define AppVersion "1.2.0"
#endif

#define AppName "LetMeSee"
#define AppPublisher "Dragon Huang"
#define AppExeName "LetMeSee.exe"
#define PublishDir "..\bin\Release\net9.0-windows\win-x64\publish"

#if !FileExists(AddBackslash(SourcePath) + PublishDir + "\" + AppExeName)
  #error 找不到 publish 輸出，請先執行 dotnet publish -c Release -r win-x64 --self-contained true
#endif

[Setup]
; 這組 AppId 決定升級與反安裝的識別，永遠不要改。
AppId={{594E855F-D990-4042-B237-F8EAE32D50CC}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; 全機器安裝到 Program Files，需要管理員權限。
; 注意：app 的檔案關聯寫的是 HKCU，所以每個使用者要各自從
; 「設定 > 檔案關聯...」建立，反安裝也只清得掉執行反安裝那個使用者的關聯。
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputBaseFilename={#AppName}-{#AppVersion}-setup
SetupIconFile=..\Assets\AppIcon.ico
UninstallDisplayIcon={app}\{#AppExeName}
LicenseFile=..\LICENSE.md
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Languages]
; 繁中是 Inno Setup 7 內建的語言檔。舊版沒有的話就只留英文，不讓編譯失敗。
#if FileExists(AddBackslash(CompilerPath) + "Languages\ChineseTraditional.isl")
Name: "cht"; MessagesFile: "compiler:Languages\ChineseTraditional.isl"
#endif
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[Registry]
; 安裝程式不建立檔案關聯，那是 app 內「設定 > 檔案關聯...」的工作。
; 但反安裝時要把 app 寫進去的 per-user 關聯清掉，否則會留下指向已刪除執行檔的關聯。
Root: HKCU; Subkey: "Software\Classes\LetMeSee.Image"; Flags: dontcreatekey uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Applications\{#AppExeName}"; Flags: dontcreatekey uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\image\shell\LetMeSee"; Flags: dontcreatekey uninsdeletekey
Root: HKCU; Subkey: "Software\RegisteredApplications"; ValueType: none; ValueName: "{#AppName}"; Flags: dontcreatekey uninsdeletevalue

[UninstallDelete]
; 診斷紀錄是純診斷資料，反安裝時一併移除；
; %APPDATA%\LetMeSee 的 settings.json 保留，重裝後設定還在。
Type: filesandordirs; Name: "{localappdata}\{#AppName}"
