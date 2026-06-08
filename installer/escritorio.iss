; ============================================================
;  Instalador da suite Escritorio v{#MyAppVersion}
;  Letrúcio · Planílson · Slidney · Vacinaldo · Zé Faxina · EspiaDesk
; ============================================================
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#ifndef MyAppURL
  #define MyAppURL "https://example.com/escritorio"
#endif
#define MyAppPublisher "Escritorio"
#define MySuiteName    "Escritorio"

; ── Configuração geral ──────────────────────────────────────
[Setup]
AppId={{7A322ED5-6DE3-4870-9D2D-11EBE705D603}
AppName={#MySuiteName}
AppVerName={#MySuiteName} {#MyAppVersion}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
VersionInfoVersion={#MyAppVersion}
VersionInfoProductVersion={#MyAppVersion}
UninstallDisplayName={#MySuiteName} {#MyAppVersion}
UninstallDisplayIcon={app}\Letrucio.exe
DefaultDirName={autopf}\{#MySuiteName}
DefaultGroupName={#MySuiteName}
; Mostra a pagina de selecao de componentes
DisableProgramGroupPage=yes
LicenseFile=LICENSE-ptBR.rtf
OutputDir=..\dist\installer
OutputBaseFilename=escritorio-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Instalacao para TODOS os usuarios (admin)
PrivilegesRequired=admin

; ── Idioma ──────────────────────────────────────────────────
[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

; ── Tipos de instalação ─────────────────────────────────────
; O usuário pode escolher um tipo ou montar a instalação personalizada.
[Types]
Name: "completa";      Description: "Instalação Completa — todos os aplicativos"
Name: "produtividade"; Description: "Suite de Produtividade — Letrúcio, Planílson e Slidney"
Name: "protecao";      Description: "Somente Vacinaldo — Proteção do Sistema"
Name: "remoto";        Description: "Somente EspiaDesk — Acesso Remoto"
Name: "personalizada"; Description: "Instalação Personalizada";  Flags: iscustom

; ── Componentes ─────────────────────────────────────────────
[Components]
Name: "letrucio";   Description: "Letrúcio  —  Editor de Texto (.docx · .odt · .rtf · .txt)";      Types: completa produtividade personalizada
Name: "planilson";  Description: "Planílson  —  Planilha Eletrônica (.xlsx · .ods · .csv)";         Types: completa produtividade personalizada
Name: "slidney";    Description: "Slidney  —  Apresentações (.pptx · .odp · .json)";                Types: completa produtividade personalizada
Name: "vacinaldo";  Description: "Vacinaldo  —  Proteção do Sistema (análise heurística de arquivos)"; Types: completa protecao personalizada
Name: "zefaxina";   Description: "Zé Faxina  —  Limpeza do Sistema (temp, cache, registro, navegadores)"; Types: completa personalizada
Name: "espiadisk";  Description: "EspiaDesk  —  Acesso Remoto Seguro (AES-256 + RSA 2048)";          Types: completa remoto personalizada

; ── Tarefas ─────────────────────────────────────────────────
[Tasks]
Name: "desktopicon";              Description: "Criar atalhos na Área de Trabalho";           GroupDescription: "Atalhos adicionais:"
Name: "desktopicon\letrucio";     Description: "Letrúcio";    Components: letrucio;   GroupDescription: "Área de Trabalho — escolha os apps:"
Name: "desktopicon\planilson";    Description: "Planílson";   Components: planilson;  GroupDescription: "Área de Trabalho — escolha os apps:"
Name: "desktopicon\slidney";      Description: "Slidney";     Components: slidney;    GroupDescription: "Área de Trabalho — escolha os apps:"
Name: "desktopicon\vacinaldo";    Description: "Vacinaldo";   Components: vacinaldo;  GroupDescription: "Área de Trabalho — escolha os apps:"
Name: "desktopicon\zefaxina";     Description: "Zé Faxina";  Components: zefaxina;   GroupDescription: "Área de Trabalho — escolha os apps:"
Name: "desktopicon\espiadisk";    Description: "EspiaDesk";  Components: espiadisk;  GroupDescription: "Área de Trabalho — escolha os apps:"

; ── Arquivos ────────────────────────────────────────────────
[Files]
; Avisos de terceiros (licenças de dependências)
Source: "..\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE";                 DestDir: "{app}"; Flags: ignoreversion

; Arquivos compartilhados: runtime .NET, Escritorio.Shared.dll e demais DLLs.
; Excluímos apenas os executáveis principais de cada app e seus arquivos de configuração
; exclusivos — esses são instalados por componente logo abaixo.
Source: "..\dist\publish\*"; DestDir: "{app}"; Excludes: "Letrucio.exe,Letrucio.dll,Letrucio.deps.json,Letrucio.runtimeconfig.json,Planilson.exe,Planilson.dll,Planilson.deps.json,Planilson.runtimeconfig.json,Slidney.exe,Slidney.dll,Slidney.deps.json,Slidney.runtimeconfig.json,Vacinaldo.exe,Vacinaldo.dll,Vacinaldo.deps.json,Vacinaldo.runtimeconfig.json,ZeFaxina.exe,ZeFaxina.dll,ZeFaxina.deps.json,ZeFaxina.runtimeconfig.json,EspiaDesk.exe,EspiaDesk.dll,EspiaDesk.deps.json,EspiaDesk.runtimeconfig.json"; Flags: recursesubdirs ignoreversion createallsubdirs

; ── Letrúcio ──
Source: "..\dist\publish\Letrucio.exe";               DestDir: "{app}"; Components: letrucio;  Flags: ignoreversion
Source: "..\dist\publish\Letrucio.dll";               DestDir: "{app}"; Components: letrucio;  Flags: ignoreversion skipifsourcedoesntexist
Source: "..\dist\publish\Letrucio.deps.json";         DestDir: "{app}"; Components: letrucio;  Flags: ignoreversion skipifsourcedoesntexist
Source: "..\dist\publish\Letrucio.runtimeconfig.json"; DestDir: "{app}"; Components: letrucio; Flags: ignoreversion skipifsourcedoesntexist

; ── Planílson ──
Source: "..\dist\publish\Planilson.exe";               DestDir: "{app}"; Components: planilson; Flags: ignoreversion
Source: "..\dist\publish\Planilson.dll";               DestDir: "{app}"; Components: planilson; Flags: ignoreversion skipifsourcedoesntexist
Source: "..\dist\publish\Planilson.deps.json";         DestDir: "{app}"; Components: planilson; Flags: ignoreversion skipifsourcedoesntexist
Source: "..\dist\publish\Planilson.runtimeconfig.json"; DestDir: "{app}"; Components: planilson; Flags: ignoreversion skipifsourcedoesntexist

; ── Slidney ──
Source: "..\dist\publish\Slidney.exe";               DestDir: "{app}"; Components: slidney;   Flags: ignoreversion
Source: "..\dist\publish\Slidney.dll";               DestDir: "{app}"; Components: slidney;   Flags: ignoreversion skipifsourcedoesntexist
Source: "..\dist\publish\Slidney.deps.json";         DestDir: "{app}"; Components: slidney;   Flags: ignoreversion skipifsourcedoesntexist
Source: "..\dist\publish\Slidney.runtimeconfig.json"; DestDir: "{app}"; Components: slidney;  Flags: ignoreversion skipifsourcedoesntexist

; ── Vacinaldo ──
Source: "..\dist\publish\Vacinaldo.exe";               DestDir: "{app}"; Components: vacinaldo; Flags: ignoreversion
Source: "..\dist\publish\Vacinaldo.dll";               DestDir: "{app}"; Components: vacinaldo; Flags: ignoreversion skipifsourcedoesntexist
Source: "..\dist\publish\Vacinaldo.deps.json";         DestDir: "{app}"; Components: vacinaldo; Flags: ignoreversion skipifsourcedoesntexist
Source: "..\dist\publish\Vacinaldo.runtimeconfig.json"; DestDir: "{app}"; Components: vacinaldo; Flags: ignoreversion skipifsourcedoesntexist

; ── Zé Faxina ──
Source: "..\dist\publish\ZeFaxina.exe";               DestDir: "{app}"; Components: zefaxina; Flags: ignoreversion
Source: "..\dist\publish\ZeFaxina.dll";               DestDir: "{app}"; Components: zefaxina; Flags: ignoreversion skipifsourcedoesntexist
Source: "..\dist\publish\ZeFaxina.deps.json";         DestDir: "{app}"; Components: zefaxina; Flags: ignoreversion skipifsourcedoesntexist
Source: "..\dist\publish\ZeFaxina.runtimeconfig.json"; DestDir: "{app}"; Components: zefaxina; Flags: ignoreversion skipifsourcedoesntexist

; ── EspiaDesk ──
Source: "..\dist\publish\EspiaDesk.exe";               DestDir: "{app}"; Components: espiadisk; Flags: ignoreversion skipifsourcedoesntexist
Source: "..\dist\publish\EspiaDesk.dll";               DestDir: "{app}"; Components: espiadisk; Flags: ignoreversion skipifsourcedoesntexist
Source: "..\dist\publish\EspiaDesk.deps.json";         DestDir: "{app}"; Components: espiadisk; Flags: ignoreversion skipifsourcedoesntexist
Source: "..\dist\publish\EspiaDesk.runtimeconfig.json"; DestDir: "{app}"; Components: espiadisk; Flags: ignoreversion skipifsourcedoesntexist

; ── Atalhos no Menu Iniciar ────────────────────────────────
[Icons]
Name: "{group}\Letrúcio";   Filename: "{app}\Letrucio.exe";   IconFilename: "{app}\Letrucio.exe";   Components: letrucio
Name: "{group}\Planílson";  Filename: "{app}\Planilson.exe";  IconFilename: "{app}\Planilson.exe";  Components: planilson
Name: "{group}\Slidney";    Filename: "{app}\Slidney.exe";    IconFilename: "{app}\Slidney.exe";    Components: slidney
Name: "{group}\Vacinaldo";  Filename: "{app}\Vacinaldo.exe";  IconFilename: "{app}\Vacinaldo.exe";  Components: vacinaldo
Name: "{group}\Zé Faxina";  Filename: "{app}\ZeFaxina.exe";   IconFilename: "{app}\ZeFaxina.exe";   Components: zefaxina
Name: "{group}\EspiaDesk";  Filename: "{app}\EspiaDesk.exe";  IconFilename: "{app}\EspiaDesk.exe";  Components: espiadisk
Name: "{group}\Desinstalar {#MySuiteName}"; Filename: "{uninstallexe}"

; Área de Trabalho (por tarefa e por componente)
Name: "{autodesktop}\Letrúcio";   Filename: "{app}\Letrucio.exe";   Components: letrucio;   Tasks: desktopicon\letrucio
Name: "{autodesktop}\Planílson";  Filename: "{app}\Planilson.exe";  Components: planilson;  Tasks: desktopicon\planilson
Name: "{autodesktop}\Slidney";    Filename: "{app}\Slidney.exe";    Components: slidney;    Tasks: desktopicon\slidney
Name: "{autodesktop}\Vacinaldo";  Filename: "{app}\Vacinaldo.exe";  Components: vacinaldo;  Tasks: desktopicon\vacinaldo
Name: "{autodesktop}\Zé Faxina";  Filename: "{app}\ZeFaxina.exe";   Components: zefaxina;   Tasks: desktopicon\zefaxina
Name: "{autodesktop}\EspiaDesk";  Filename: "{app}\EspiaDesk.exe";  Components: espiadisk;  Tasks: desktopicon\espiadisk

; ── Executar ao finalizar (opcional, desmarcado por padrão) ─
[Run]
Filename: "{app}\Letrucio.exe";   Description: "Abrir Letrúcio";    Components: letrucio;   Flags: nowait postinstall skipifsilent unchecked
Filename: "{app}\Planilson.exe";  Description: "Abrir Planílson";   Components: planilson;  Flags: nowait postinstall skipifsilent unchecked
Filename: "{app}\Slidney.exe";    Description: "Abrir Slidney";     Components: slidney;    Flags: nowait postinstall skipifsilent unchecked
Filename: "{app}\Vacinaldo.exe";  Description: "Abrir Vacinaldo";   Components: vacinaldo;  Flags: nowait postinstall skipifsilent unchecked
Filename: "{app}\ZeFaxina.exe";   Description: "Abrir Zé Faxina";  Components: zefaxina;   Flags: nowait postinstall skipifsilent unchecked
Filename: "{app}\EspiaDesk.exe";  Description: "Abrir EspiaDesk";  Components: espiadisk;  Flags: nowait postinstall skipifsilent unchecked

