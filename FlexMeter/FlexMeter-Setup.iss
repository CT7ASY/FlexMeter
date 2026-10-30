#define AppVersion "0.69"
#define BuildRoot SourcePath
[Setup]
AppId={{928C576F-65CF-47AC-A84A-10CE9828D3E4}
AppName=Flex Meter
AppVersion={#AppVersion}
AppPublisher=Eduardo Arraia
DefaultDirName=C:\CT7ASY\Flex Meter
UsePreviousAppDir=no
DefaultGroupName=Flex Meter
UninstallDisplayIcon={app}\FlexMeter-flat.ico
OutputDir={#BuildRoot}\dist
OutputBaseFilename=Instalar-Flex-Meter-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
MinVersion=10.0
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
SetupIconFile={#BuildRoot}\FlexMeter.ico
VersionInfoVersion=0.69.0.0
InfoAfterFile={#BuildRoot}\Instalacao-Flex-Meter.txt

[Languages]
Name: "portuguese"; MessagesFile: "compiler:Languages\Portuguese.isl"

[Files]
Source: "{#BuildRoot}\FlexMeter.ico"; DestDir: "{app}"; DestName: "FlexMeter-flat.ico"; Flags: ignoreversion
Source: "{#BuildRoot}\build\FlexMeter.exe"; DestDir: "{app}"; DestName: "FlexMeter.exe"; Flags: ignoreversion
Source: "{#BuildRoot}\FlexMeter.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildRoot}\Instalacao-Flex-Meter.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Flex Meter"; Filename: "{app}\FlexMeter.exe"; IconFilename: "{app}\FlexMeter-flat.ico"
Name: "{autodesktop}\Flex Meter"; Filename: "{app}\FlexMeter.exe"; IconFilename: "{app}\FlexMeter-flat.ico"

[Run]
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""FlexMeter-CT7ASY-Local-UDP"" program=""{app}\FlexMeter.exe"""; Flags: runhidden waituntilterminated; Check: IsAdminInstallMode
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""FlexMeter-CT7ASY-Local-UDP"" dir=in action=allow program=""{app}\FlexMeter.exe"" protocol=UDP remoteip=LocalSubnet profile=any"; Flags: runhidden waituntilterminated; Check: IsAdminInstallMode
Filename: "{app}\FlexMeter.exe"; Parameters: "--configure"; Description: "Abrir o Flex Meter e configurar o indicativo"; Flags: postinstall nowait skipifsilent runasoriginaluser; Check: CanLaunch

[UninstallRun]
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""FlexMeter-CT7ASY-Local-UDP"" program=""{app}\FlexMeter.exe"""; Flags: runhidden waituntilterminated; RunOnceId: "RemoveFlexMeterUDP"; Check: IsAdminInstallMode

[Code]
var
  DotNetRestart: Boolean;
  DownloadPage: TDownloadWizardPage;

function Net48Installed: Boolean;
var Release: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM32, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release) and (Release >= 528040);
  if not Result then
    Result := RegQueryDWordValue(HKLM64, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release) and (Release >= 528040);
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage('Microsoft .NET Framework 4.8', 'A descarregar o componente oficial da Microsoft...', nil);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var Code: Integer;
begin
  Result := '';
  if Net48Installed then begin
    Log('.NET Framework 4.8 ou posterior detetado: download dispensado.');
    exit;
  end;
  if not IsAdminInstallMode then begin
    Result := 'O .NET Framework 4.8 está em falta. Execute o instalador como administrador para o instalar.';
    exit;
  end;
  try
    DownloadPage.Clear;
    DownloadPage.Add('https://go.microsoft.com/fwlink/?LinkId=2085155', 'dotnet48-web.exe', '0bba3094588c4bfec301939985222a20b340bf03431563dec8b2b4478b06fffa');
    DownloadPage.Show;
    try
      DownloadPage.Download;
    finally
      DownloadPage.Hide;
    end;
    WizardForm.StatusLabel.Caption := 'A instalar o Microsoft .NET Framework 4.8...';
    if not Exec(ExpandConstant('{tmp}\dotnet48-web.exe'), '/passive /norestart', '', SW_SHOW, ewWaitUntilTerminated, Code) then begin
      Result := 'Não foi possível iniciar a instalação do .NET Framework.';
      exit;
    end;
    if (Code = 3010) or (Code = 1641) then DotNetRestart := True
    else if Code <> 0 then begin
      Result := Format('A instalação do .NET Framework terminou com o código %d. Verifique a Internet e as atualizações do Windows.', [Code]);
      exit;
    end;
    if (not DotNetRestart) and (not Net48Installed) then
      Result := 'Não foi possível confirmar a instalação do .NET Framework 4.8. Reinicie o Windows e tente novamente.';
  except
    Result := 'Não foi possível descarregar ou instalar o .NET Framework. Verifique a Internet e tente novamente.' + #13#10 + GetExceptionMessage;
  end;
end;

function NeedRestart: Boolean;
begin
  Result := DotNetRestart;
end;

function CanLaunch: Boolean;
begin
  Result := Net48Installed and (not DotNetRestart);
end;





