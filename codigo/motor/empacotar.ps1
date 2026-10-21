# Empacota o motor Python (prossen_estrutural.web) em estructto-motor.exe com o PyInstaller (pasta, nao arquivo unico:
# arquivo unico se descompacta a cada partida e passaria dos 10 s que o Motor.cs espera).
#   .\empacotar.ps1 -Motor <pasta que contem prossen_estrutural>  [-Saida <pasta>]
# Saida padrao: codigo\motor\dist (estructto-motor.exe + _internal). O dotnet publish do Estructto copia essa pasta
# para motor\ ao lado do Estructto.exe (Estructto.csproj, item MotorDist). Precisa: python com numpy e pyinstaller.
param(
    [Parameter(Mandatory = $true)][string]$Motor,
    [string]$Saida = ''
)
$ErrorActionPreference = 'Stop'
if (-not $Saida) { $Saida = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'dist' }
$Motor = (Resolve-Path $Motor).Path
if (-not (Test-Path (Join-Path $Motor 'prossen_estrutural\web\servidor.py'))) { throw "Nao achei prossen_estrutural\web em $Motor" }

$tmp = Join-Path ([IO.Path]::GetTempPath()) ('estructto-motor-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $tmp | Out-Null
try {
    $entrada = Join-Path $tmp 'estructto_motor.py'
    Set-Content -Encoding ascii $entrada "from prossen_estrutural.web.__main__ import main`nmain()"
    # dados do pacote (gramática do .ed3, catálogos, estilo do relatório): --collect-data não os acha porque o pacote
    # entra por --paths (não instalado), então cada pasta vai explícita com --add-data.
    $pk = Join-Path $Motor 'prossen_estrutural'
    $dados = @()
    foreach ($d in 'dados', 'relatorio_estilo') { if (Test-Path (Join-Path $pk $d)) { $dados += '--add-data'; $dados += ((Join-Path $pk $d) + ';prossen_estrutural\' + $d) } }
    & python -m PyInstaller --noconfirm --clean --console --name estructto-motor `
        --paths $Motor --collect-submodules prossen_estrutural @dados `
        --distpath (Join-Path $tmp 'dist') --workpath (Join-Path $tmp 'build') --specpath $tmp $entrada
    if ($LASTEXITCODE -ne 0) { throw "PyInstaller falhou ($LASTEXITCODE)" }
    if (Test-Path $Saida) { Remove-Item -Recurse -Force $Saida }
    Copy-Item -Recurse (Join-Path $tmp 'dist\estructto-motor') $Saida
    "estructto-motor.exe em $Saida"
}
finally { Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue }
