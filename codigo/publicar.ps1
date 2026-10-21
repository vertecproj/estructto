# Gera a release do Estructto num comando: empacota o motor Python, confere o pacote, publica o Estructto.exe,
# testa o exe publicado com o motor empacotado e monta o .zip.
#   codigo\publicar.ps1 -Versao 0.4  [-Saida C:\dev\build\EstructtoRelease\publish]
# Precisa: python com numpy e pyinstaller; .NET 8 SDK. Qualquer falha para o script com codigo de saida 1.
param(
    [Parameter(Mandatory = $true)][string]$Versao,
    [string]$Saida = 'C:\dev\build\EstructtoRelease\publish'
)
$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $MyInvocation.MyCommand.Path          # codigo\
$motorFonte = Join-Path $raiz 'motor'
$dist = Join-Path $motorFonte 'dist'
$csproj = Join-Path $raiz 'Estructto\Estructto.csproj'
$exemplo = Join-Path (Split-Path -Parent $raiz) 'exemplos\guincho-com-placa.estructto'
$zip = Join-Path (Split-Path -Parent $Saida) "Estructto-$Versao-win-x64.zip"
$tmp = Join-Path ([IO.Path]::GetTempPath()) ('estructto-publicar-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $tmp | Out-Null

function Falha([string]$msg) { throw "FALHOU: $msg" }
function Etapa([string]$msg) { Write-Host "`n== $msg" -ForegroundColor Cyan }
function Tamanho([string]$p) {
    $b = if (Test-Path $p -PathType Container) { (Get-ChildItem $p -Recurse -File | Measure-Object Length -Sum).Sum } else { (Get-Item $p).Length }
    '{0:N1} MB' -f ($b / 1MB)
}
# arquivos de $de (relativos) que faltam ou tem tamanho diferente em $para
function Faltando([string]$de, [string]$para, [scriptblock]$filtro = { $true }) {
    Get-ChildItem $de -Recurse -File | Where-Object $filtro | ForEach-Object {
        $rel = $_.FullName.Substring($de.Length).TrimStart('\')
        $alvo = Join-Path $para $rel
        if (-not (Test-Path $alvo) -or (Get-Item $alvo).Length -ne $_.Length) { $rel }
    }
}
function MotoresEm([string]$pasta) {
    Get-Process estructto-motor -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($pasta.TrimEnd('\') + '\',[StringComparison]::OrdinalIgnoreCase) }
}
# sobe o estructto-motor.exe numa porta livre e espera /api/versao (ate 60 s, como o Motor.cs); deixa $srv e $url
function SubirMotor([string]$exeMotor) {
    $l = New-Object Net.Sockets.TcpListener ([Net.IPAddress]::Loopback, 0); $l.Start(); $porta = $l.LocalEndpoint.Port; $l.Stop()
    $script:url = "http://127.0.0.1:$porta"
    $trab = Join-Path $tmp 'trabalho'; New-Item -ItemType Directory $trab -Force | Out-Null
    $err = Join-Path $tmp "motor-stderr-$porta.txt"
    $script:srv = Start-Process $exeMotor -ArgumentList '--porta', $porta, '--pasta', "`"$trab`"" -PassThru -WindowStyle Hidden -RedirectStandardError $err
    for ($i = 0; $i -lt 600; $i++) {
        if ($srv.HasExited) { Falha "$exeMotor encerrou ao iniciar: $(Get-Content $err -Raw)" }
        try { if ((Invoke-WebRequest "$url/api/versao" -UseBasicParsing -TimeoutSec 2).StatusCode -eq 200) { return } } catch { Start-Sleep -Milliseconds 100 }
    }
    Falha "$exeMotor nao respondeu /api/versao em 60 s"
}
function PararMotor { if ($script:srv) { Stop-Process -Id $srv.Id -Force -ErrorAction SilentlyContinue; $srv.WaitForExit(); $script:srv = $null } }

$srv = $null
try {
    if (-not (Test-Path $exemplo)) { Falha "exemplo nao encontrado: $exemplo" }

    Etapa '1/6 empacotar o motor (empacotar.ps1)'
    & (Join-Path $motorFonte 'empacotar.ps1') -Motor $motorFonte -Saida $dist
    if (-not (Test-Path (Join-Path $dist 'estructto-motor.exe'))) { Falha "empacotar.ps1 nao gerou $dist\estructto-motor.exe" }

    Etapa '2/6 conferir o pacote do motor'
    $pk = Join-Path $motorFonte 'prossen_estrutural'
    $int = Join-Path $dist '_internal'
    foreach ($d in 'dados', 'relatorio_estilo') {
        $f = @(Faltando (Join-Path $pk $d) (Join-Path $int "prossen_estrutural\$d") { $_.Extension -ne '.py' -and $_.FullName -notmatch '\\__pycache__\\' })
        if ($f) { Falha "pacote sem prossen_estrutural\$d\: $($f -join ', ')" }
    }
    $f = @(Faltando (Join-Path $motorFonte 'exemplos') (Join-Path $int 'exemplos') { $_.Extension -eq '.json' })
    if ($f) { Falha "pacote sem exemplos: $($f -join ', ')" }
    Write-Host 'dados, relatorio_estilo e exemplos presentes'

    # motor empacotado no ar: rotas que dependem dos dados, dos exemplos e dos modulos carregados por importlib
    SubirMotor (Join-Path $dist 'estructto-motor.exe')
    function Api([string]$metodo, [string]$rota, [string]$corpo = $null) {
        $p = @{ Uri = "$url$rota"; Method = $metodo; UseBasicParsing = $true; TimeoutSec = 600 }
        if ($metodo -ne 'GET') {
            $p.Headers = @{ Origin = $url }; $p.ContentType = 'application/json'
            $p.Body = [Text.Encoding]::UTF8.GetBytes($(if ($corpo) { $corpo } else { '{}' }))
        }
        try { $r = Invoke-WebRequest @p } catch {
            $det = if ($_.Exception.Response) { (New-Object IO.StreamReader($_.Exception.Response.GetResponseStream())).ReadToEnd() } else { $_.Exception.Message }
            Falha "$metodo $rota -> $det"
        }
        if ($r.StatusCode -ne 200) { Falha "$metodo $rota -> HTTP $($r.StatusCode)" }
        Write-Host "  200  $metodo $rota"
        $j = [Text.Encoding]::UTF8.GetString($r.RawContentStream.ToArray()) | ConvertFrom-Json
        $j   # devolver a variavel desenrola a lista (o ConvertFrom-Json do PowerShell 5.1 manda a lista inteira como 1 objeto)
    }
    $v = Api GET '/api/versao'
    $ex = Api GET '/api/exemplos'
    if (@($ex).Count -lt 1) { Falha '/api/exemplos voltou vazio' }
    Api POST '/api/exemplos/galpao_aco' | Out-Null
    Api POST '/api/calcular' | Out-Null
    $cat = Api GET '/api/catalogo?filtro=W310'
    if (@($cat).Count -lt 1) { Falha '/api/catalogo?filtro=W310 sem perfis' }
    $tipos = @(Api GET '/api/ligacoes/tipos')
    if ($tipos.Count -lt 19) { Falha "/api/ligacoes/tipos com $($tipos.Count) tipos (esperado 19 ou mais)" }
    $ed3 = Get-ChildItem (Join-Path $motorFonte 'testes\dados_cype') -Filter *.ed3 | Sort-Object Length | Select-Object -First 1
    if (-not $ed3) { Falha 'nenhum .ed3 em codigo\motor\testes\dados_cype' }
    $corpo = @{ nome = $ed3.Name; conteudo_base64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($ed3.FullName)) } | ConvertTo-Json -Compress
    Api POST '/api/importar_cype' $corpo | Out-Null
    Write-Host "motor $($v.versao): $(@($ex).Count) exemplos, $(@($cat).Count) perfis W310, $($tipos.Count) tipos de ligacao, $($ed3.Name) importado"
    PararMotor

    Etapa "3/6 dotnet publish em $Saida"
    if (Test-Path $Saida) {
        # so apaga pasta de publish anterior (motor\ velho ficaria misturado com o novo)
        if ((Get-ChildItem $Saida -Force | Measure-Object).Count -and -not (Test-Path (Join-Path $Saida 'Estructto.exe'))) { Falha "$Saida existe e nao e uma pasta de publish do Estructto" }
        if (MotoresEm $Saida) { Falha "ha estructto-motor rodando de $Saida; feche o Estructto de la" }
        Remove-Item -Recurse -Force $Saida
    }
    & dotnet publish $csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o $Saida
    if ($LASTEXITCODE -ne 0) { Falha "dotnet publish ($LASTEXITCODE)" }
    $exe = Join-Path $Saida 'Estructto.exe'
    if (-not (Test-Path $exe)) { Falha "sem $exe" }
    $f = @(Faltando $dist (Join-Path $Saida 'motor'))
    if ($f) { Falha "motor\ ao lado do exe incompleto ($($f.Count) arquivos), ex.: $($f[0..4] -join ', ')" }
    Write-Host 'Estructto.exe e motor\ completo publicados'

    Etapa '4/6 teste do exe publicado (--teste-motor, sem ESTRUCTTO_MOTOR)'
    # ponytail: aquece o motor\ publicado (1a partida de arquivos novos: antivirus + PyInstaller frio). Sem isso o
    # --teste-motor falha as vezes: o TesteMotor (FaixaMotor.cs) roda antes de o Abrir do .estructto terminar, cai no
    # caminho 2D com modelo vazio e sai 1. Corrigir no app (TesteMotor esperar o AbrirTarefa) e tirar este aquecimento.
    SubirMotor (Join-Path $Saida 'motor\estructto-motor.exe'); PararMotor
    $png = Join-Path $tmp 'teste-motor.png'
    $antes = $env:ESTRUCTTO_MOTOR; $env:ESTRUCTTO_MOTOR = $null
    try { $p = Start-Process $exe -ArgumentList "--teste-motor=`"$png`"", "`"$exemplo`"" -Wait -PassThru }
    finally { $env:ESTRUCTTO_MOTOR = $antes }
    $txt = [IO.Path]::ChangeExtension($png, '.txt')
    $log = if (Test-Path $txt) { (Get-Content $txt -Raw -Encoding UTF8).Trim() } else { '(sem saida .txt)' }
    $orfaos = @(MotoresEm $Saida)
    if ($orfaos) { $orfaos | Stop-Process -Force; Falha "$($orfaos.Count) estructto-motor orfao(s) de $Saida apos o teste" }
    if ($p.ExitCode -ne 0) { Falha "Estructto.exe --teste-motor saiu com $($p.ExitCode): $log" }
    if (-not (Test-Path $png)) { Falha 'Estructto.exe --teste-motor nao gravou o PNG' }
    Write-Host "saida 0, sem motor orfao: $log"

    Etapa "5/6 zip $zip"
    if (Test-Path $zip) { Remove-Item -Force $zip }
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    $z = [IO.Compression.ZipFile]::Open($zip, 'Create')
    try {
        $arqs = @(Get-Item $exe) + @(Get-ChildItem (Join-Path $Saida 'motor') -Recurse -File)
        foreach ($a in $arqs) {
            $nome = 'Estructto/' + $a.FullName.Substring($Saida.TrimEnd('\').Length + 1).Replace('\', '/')
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($z, $a.FullName, $nome, 'Optimal')
        }
    }
    finally { $z.Dispose() }

    Etapa '6/6 resumo'
    Write-Host "versao          $Versao (motor $($v.versao))"
    Write-Host "Estructto.exe   $(Tamanho $exe)"
    Write-Host "motor\          $(Tamanho (Join-Path $Saida 'motor')) ($((Get-ChildItem (Join-Path $Saida 'motor') -Recurse -File).Count) arquivos)"
    Write-Host "zip             $(Tamanho $zip)  $zip"
    Write-Host 'RELEASE OK' -ForegroundColor Green
}
catch {
    Write-Host "`n$($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
finally {
    if ($srv -and -not $srv.HasExited) { Stop-Process -Id $srv.Id -Force -ErrorAction SilentlyContinue }
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
}
