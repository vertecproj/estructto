# Compila o programa com o compilador C# que ja vem no Windows (nao precisa instalar nada).
#   .\compilar.ps1          -> Portico.exe
#   .\compilar.ps1 -Testes  -> roda os testes do motor de calculo
param([switch]$Testes)
$ErrorActionPreference = 'Stop'
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$aqui = Split-Path -Parent $MyInvocation.MyCommand.Path
$res = "/resource:`"$aqui\perfis-gerdau.csv`",perfis-gerdau.csv", "/resource:`"$aqui\perfis-outros.csv`",perfis-outros.csv", "/resource:`"$aqui\chumbadores.csv`",chumbadores.csv", "/resource:`"$aqui\fixacoes.csv`",fixacoes.csv", "/resource:`"$aqui\fixacoes-pares.csv`",fixacoes-pares.csv"
if ($Testes) {
    $saida = Join-Path $env:TEMP 'portico-testes.exe'
    & $csc /nologo /codepage:65001 /optimize+ /target:exe "/out:$saida" $res "$aqui\src\Nucleo.cs" "$aqui\src\Testes.cs" "$aqui\..\Estructto\Solda.cs"
    if ($LASTEXITCODE -ne 0) { exit 1 }
    & $saida
    exit $LASTEXITCODE
}
& $csc /nologo /codepage:65001 /optimize+ /target:winexe "/out:$aqui\Portico.exe" $res `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll "$aqui\src\Nucleo.cs" "$aqui\src\Janela.cs"
if ($LASTEXITCODE -ne 0) { exit 1 }
"Portico.exe gerado em $aqui"
