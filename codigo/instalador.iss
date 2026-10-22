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

[Languages]
Name: "pt"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Files]
Source: "{#Publicado}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autodesktop}\Estructto"; Filename: "{app}\Estructto.exe"
Name: "{group}\Estructto"; Filename: "{app}\Estructto.exe"
Name: "{group}\Desinstalar Estructto"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\Estructto.exe"; Description: "Abrir o Estructto"; Flags: nowait postinstall skipifsilent
