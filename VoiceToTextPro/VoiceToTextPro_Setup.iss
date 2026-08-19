; ════════════════════════════════════════════════════════════
; VoiceToText Pro v3.0 — Inno Setup Installer Script
; ════════════════════════════════════════════════════════════

#define MyAppName      "VoiceToText Pro"
#define MyAppVersion   "3.0.0"
#define MyAppPublisher "VoiceToText Team"
#define MyAppExeName   "VoiceToTextPro.exe"
#define MyAppID        "{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}"

[Setup]
AppId={{#MyAppID}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputBaseFilename=VoiceToTextPro_Setup_v3.0
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; Require Windows 10+
MinVersion=10.0
PrivilegesRequired=admin
SetupIconFile=Resources\icon.ico
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "persian"; MessagesFile: "compiler:Default.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "ایجاد آیکون روی دسکتاپ"; GroupDescription: "آیکون‌های اضافی:"

[Files]
; Main executable (published self-contained)
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; Python workers folder (entire tree)
Source: "..\workers\*"; DestDir: "{app}\workers"; Flags: ignoreversion recursesubdirs createallsubdirs

; FFmpeg binaries (must be present in build\ffmpeg\)
Source: "build\ffmpeg\ffmpeg.exe"; DestDir: "{app}\workers"; Flags: ignoreversion
Source: "build\ffmpeg\ffprobe.exe"; DestDir: "{app}\workers"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\حذف نصب {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; 1. Install Python dependencies
Filename: "{code:GetPythonPath}"; \
  Parameters: "-m pip install -r ""{app}\workers\requirements.txt"" --quiet"; \
  WorkingDir: "{app}"; \
  StatusMsg: "در حال نصب وابستگی‌های پایتون (pydub, yt-dlp, ...)"; \
  Flags: runhidden waituntilterminated

; 2. Launch app after install
Filename: "{app}\{#MyAppExeName}"; \
  Description: "{cm:LaunchProgram,{#MyAppName}}"; \
  Flags: nowait postinstall skipifsilent

[Code]
var
  PythonPath: string;

// ── Detect Python (py > python > python3) ──────────────────────────────────
function DetectPython(): string;
var
  Candidates: array[0..2] of string;
  i: Integer;
  ExitCode: Integer;
begin
  Candidates[0] := 'py';
  Candidates[1] := 'python';
  Candidates[2] := 'python3';
  Result := '';
  for i := 0 to 2 do
  begin
    if Exec(Candidates[i], '--version', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
    begin
      if ExitCode = 0 then
      begin
        Result := Candidates[i];
        Exit;
      end;
    end;
  end;
end;

function GetPythonPath(Param: string): string;
begin
  Result := PythonPath;
end;

// ── Pre-install check ──────────────────────────────────────────────────────
function InitializeSetup(): Boolean;
begin
  PythonPath := DetectPython();
  if PythonPath = '' then
  begin
    if MsgBox(
      'پایتون روی این سیستم شناسایی نشد.' + #13#10 +
      'بدون پایتون برنامه قادر به انجام رونویسی و دانلود نخواهد بود.' + #13#10#13#10 +
      'آیا می‌خواهید نصب را ادامه دهید؟',
      mbConfirmation, MB_YESNO) = IDNO then
    begin
      Result := False;
      Exit;
    end;
    PythonPath := 'python'; // fallback, user will configure manually
  end;
  Result := True;
end;

// ── Save detected python path to appsettings.json after install ────────────
procedure SavePythonSetting();
var
  SettingsPath, Content: string;
begin
  SettingsPath := ExpandConstant('{app}\appsettings.json');
  Content := '{"PythonPath":"' + PythonPath + '",' +
             '"OutputDirectory":"' + ExpandConstant('{userdocs}') + '\\VoiceToTextPro\\output",' +
             '"DownloadDirectory":"' + ExpandConstant('{userdocs}') + '\\VoiceToTextPro\\downloads",' +
             '"DefaultLanguage":"fa-IR",' +
             '"PreferredEngine":"google",' +
             '"Theme":"Dark"}';
  SaveStringToFile(SettingsPath, Content, False);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    SavePythonSetting();
end;
