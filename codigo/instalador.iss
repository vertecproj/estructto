; Instalador do Estructto (Inno Setup 6). Empacota a saída do publicar.ps1.
;   ISCC.exe /DVersao=0.6.2 codigo\instalador.iss  -> C:\dev\build\EstructtoRelease\Estructto-Setup-<versao>.exe
; Instala por usuário (sem administrador) em %LOCALAPPDATA%\Programs\Estructto, com atalho na área de trabalho
; e no Menu Iniciar e desinstalação em "Aplicativos instalados".
#ifndef Versao
  #define Versao "0.0.0"
#endif
#define Publicado "C:\dev\build\EstructtoRelease\publish"

[Setup]
AppId={{7E3C2B9A-4F1D-4C8E-9B2A-E57C0A1D6F42}
AppName=Estructto
AppVersion={#Versao}
AppPublisher=Vertec
DefaultDirName={localappdata}\Programs\Estructto
DefaultGroupName=Estructto
PrivilegesRequired=lowest
DisableProgramGroupPage=yes
OutputDir=C:\dev\build\EstructtoRelease
OutputBaseFilename=Estructto-Setup-{#Versao}
SetupIconFile=Estructto\estructto.ico
UninstallDisplayIcon={app}\Estructto.exe
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
ChangesAssociations=yes

[Languages]
Name: "pt"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Files]
Source: "{#Publicado}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; Duplo clique abre o arquivo no Estructto: .pstr = modelo, .pcon = calculadora de conexão (docs/ARQUIVOS.md). Por usuário (HKCU).
[Registry]
Root: HKCU; Subkey: "Software\Classes\.pstr"; ValueType: string; ValueData: "Estructto.Modelo"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\Estructto.Modelo"; ValueType: string; ValueData: "Modelo do Estructto"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Estructto.Modelo\DefaultIcon"; ValueType: string; ValueData: "{app}\Estructto.exe,0"
Root: HKCU; Subkey: "Software\Classes\Estructto.Modelo\shell\open\command"; ValueType: string; ValueData: """{app}\Estructto.exe"" ""%1"""
Root: HKCU; Subkey: "Software\Classes\.pcon"; ValueType: string; ValueData: "Estructto.Conexao"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\Estructto.Conexao"; ValueType: string; ValueData: "Conexão do Estructto"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Estructto.Conexao\DefaultIcon"; ValueType: string; ValueData: "{app}\Estructto.exe,0"
Root: HKCU; Subkey: "Software\Classes\Estructto.Conexao\shell\open\command"; ValueType: string; ValueData: """{app}\Estructto.exe"" ""%1"""

[Icons]
Name: "{autodesktop}\Estructto"; Filename: "{app}\Estructto.exe"
Name: "{group}\Estructto"; Filename: "{app}\Estructto.exe"
Name: "{group}\Desinstalar Estructto"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\Estructto.exe"; Description: "Abrir o Estructto"; Flags: nowait postinstall skipifsilent
