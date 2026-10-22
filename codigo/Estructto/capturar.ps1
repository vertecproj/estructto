param(
    [string]$PastaSaida = "",
    [string]$Modelo = ""
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
# mesma regra do Directory.Build.props: checkout principal (estructto/ftool) em EstructtoV2, worktree em EstructtoV2-<raiz>
$raizWt = Split-Path -Leaf (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$pastaBuild = if ($raizWt -in @("estructto", "ftool", "")) { "C:\dev\build\EstructtoV2" } else { "C:\dev\build\EstructtoV2-$raizWt" }
if (-not $PastaSaida) { $PastaSaida = Join-Path $pastaBuild "capturas" }

Write-Host "Compilando Estructto (Release)..." -ForegroundColor Cyan
dotnet build -c Release --nologo
if ($LASTEXITCODE -ne 0) {
    throw "Falha ao compilar Estructto (codigo $LASTEXITCODE)."
}

$exe = Join-Path $pastaBuild "bin\Release\net8.0-windows\Estructto.exe"
if (-not (Test-Path $exe)) {
    throw "Executavel nao encontrado em: $exe"
}

if (Test-Path $PastaSaida) {
    Remove-Item (Join-Path $PastaSaida "*.png") -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $PastaSaida "erros.txt") -Force -ErrorAction SilentlyContinue
} else {
    New-Item -ItemType Directory -Path $PastaSaida -Force | Out-Null
}

$argsExe = @("--capturas", $PastaSaida)
if (-not [string]::IsNullOrWhiteSpace($Modelo)) {
    $argsExe += $Modelo
}

Write-Host "Gerando capturas em $PastaSaida ..." -ForegroundColor Cyan
$proc = Start-Process -FilePath $exe -ArgumentList $argsExe -WorkingDirectory $PSScriptRoot -Wait -PassThru
if ($proc.ExitCode -ne 0) {
    $errosTxt = Join-Path $PastaSaida "erros.txt"
    if (Test-Path $errosTxt) {
        Write-Host (Get-Content $errosTxt -Raw) -ForegroundColor Red
    }
    throw "Estructto.exe encerrou com codigo $($proc.ExitCode)."
}

$pngs = @(Get-ChildItem -Path $PastaSaida -Filter "*.png" | Sort-Object Name)
Write-Host ("Concluido: {0} PNG(s) gerados em {1}" -f $pngs.Count, $PastaSaida) -ForegroundColor Green
