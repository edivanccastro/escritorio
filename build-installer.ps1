param(
    [string]$Configuration = "Release",
    [string]$Version = "1.0.0",
    [string]$AppUrl = "https://example.com/escritorio",
    [string]$CertPfxPath = "",
    [string]$CertPassword = "",
    [string]$TimestampUrl = "http://timestamp.digicert.com",
    [string]$SignToolPath = "signtool.exe"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$publishDir = Join-Path $root "dist\publish"
$installerOutputDir = Join-Path $root "dist\installer"
$issPath = Join-Path $root "installer\escritorio.iss"

$apps = @(
    @{ Name = "Letrúcio";   Project = "src\Letrucio\Letrucio.csproj";    Exe = "Letrucio.exe"   },
    @{ Name = "Planílson";  Project = "src\Planilson\Planilson.csproj";  Exe = "Planilson.exe"  },
    @{ Name = "Slidney";    Project = "src\Slidney\Slidney.csproj";      Exe = "Slidney.exe"    },
    @{ Name = "Vacinaldo";  Project = "src\Vacinaldo\Vacinaldo.csproj";  Exe = "Vacinaldo.exe"  },
    @{ Name = "Zé Faxina"; Project = "src\ZeFaxina\ZeFaxina.csproj";    Exe = "ZeFaxina.exe"   },
    @{ Name = "EspiaDesk"; Project = "src\EspiaDesk\EspiaDesk.csproj";  Exe = "EspiaDesk.exe"  }
)

function Test-SignerAvailable {
    param([string]$PathHint)
    return $null -ne (Get-Command $PathHint -ErrorAction SilentlyContinue)
}

function Invoke-SignIfRequested {
    param([string]$TargetFile)

    if ([string]::IsNullOrWhiteSpace($CertPfxPath)) {
        return
    }
    if (-not (Test-Path $TargetFile)) {
        throw "Arquivo para assinatura nao encontrado: $TargetFile"
    }
    if (-not (Test-Path $CertPfxPath)) {
        throw "Certificado PFX nao encontrado: $CertPfxPath"
    }
    if (-not (Test-SignerAvailable -PathHint $SignToolPath)) {
        throw "signtool nao encontrado: $SignToolPath"
    }

    Write-Host "Assinando: $TargetFile"
    & $SignToolPath sign /fd SHA256 /f $CertPfxPath /p $CertPassword /tr $TimestampUrl /td SHA256 $TargetFile
}

Write-Host "Limpando saida anterior..."
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
if (Test-Path $installerOutputDir) { Remove-Item -Recurse -Force $installerOutputDir }

# ── Apps .NET: publica na mesma pasta para compartilhar o runtime self-contained ──
foreach ($app in $apps) {
    Write-Host "Publicando $($app.Name) (versao $Version)..."
    dotnet publish (Join-Path $root $app.Project) `
        -c $Configuration `
        -r win-x64 `
        --self-contained true `
        -p:Version=$Version `
        -p:FileVersion="$Version.0" `
        -p:AssemblyVersion="$Version.0" `
        -o $publishDir
}

foreach ($app in $apps) {
    Invoke-SignIfRequested -TargetFile (Join-Path $publishDir $app.Exe)
}

$isccPath = $null
$isccCmd = Get-Command "iscc.exe" -ErrorAction SilentlyContinue
if ($isccCmd) {
    $isccPath = $isccCmd.Source
}
else {
    $candidates = @(
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe")
    )
    $isccPath = $candidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
}

if (-not $isccPath) {
    Write-Warning "Inno Setup (ISCC.exe) nao encontrado."
    Write-Warning "Instale o Inno Setup 6+ (winget install JRSoftware.InnoSetup) e execute novamente."
    exit 0
}

Write-Host "Gerando instalador com $isccPath ..."
& $isccPath "/DMyAppVersion=$Version" "/DMyAppURL=$AppUrl" $issPath

Invoke-SignIfRequested -TargetFile (Join-Path $installerOutputDir "escritorio-setup.exe")

Write-Host ""
Write-Host "====================================="
Write-Host " Build concluido com sucesso!"
Write-Host "====================================="
Write-Host "Instalador gerado em: dist\installer\escritorio-setup.exe"
