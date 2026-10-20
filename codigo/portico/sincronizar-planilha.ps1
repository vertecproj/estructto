# Copia o catalogo de chumbadores (chumbadores.csv, a mesma fonte que o Estructto embute) para a aba CHUMBADORES
# da planilha, pelo proprio Excel (preserva listas suspensas e formatacao condicional).
#   .\sincronizar-planilha.ps1 -Planilha "C:\...\PLANILHA DE CÁLCULO.xlsx"
# Colunas A-F mantem o significado antigo (codigo, rosca, furo, P, Tu, Vu) para nao quebrar formulas.
# O nome CHUMBADORES_LISTA aponta para os codigos (usado nas listas de modelo da aba de calculo).
param([Parameter(Mandatory = $true)][string]$Planilha)
$ErrorActionPreference = 'Stop'
$aqui = Split-Path -Parent $MyInvocation.MyCommand.Path
$linhas = [IO.File]::ReadAllLines("$aqui\chumbadores.csv", [Text.Encoding]::UTF8) | Where-Object { $_.Trim() -ne '' }
$cab = $linhas[0].Split(';')
$col = @{}; for ($i = 0; $i -lt $cab.Length; $i++) { $col[$cab[$i]] = $i }
# ordem das colunas na aba (A, B, C, ...)
$ordem = 'codigo', 'pol', 'furo_mm', 'hef_mm', 'tu_kgf', 'vu_kgf', 'tipo', 'fabricante', 'linha', 'tu_rec_kgf', 'vu_rec_kgf',
         'fck_mpa', 'torque_kgfm', 's_mm', 'c_mm', 'at_mm2', 'ar_mm2', 'rosca_mm', 'fonte'
$titulos = 'Código', 'Ø rosca', 'Ø furo (mm)', 'Embutimento P (mm)', 'Tração ruptura (kgf)', 'Cisalhamento ruptura (kgf)', 'Tipo',
           'Fabricante', 'Linha', 'Tração recomendada (kgf)', 'Cisalhamento recomendado (kgf)', 'fck do ensaio (MPa)', 'Torque (kgf·m)',
           'Espaçamento mín. s (mm)', 'Distância à borda mín. c (mm)', 'At barra (mm²)', 'Ar barra (mm²)', 'Ø rosca (mm)', 'Fonte'
$texto = 'codigo', 'pol', 'tipo', 'fabricante', 'linha', 'fonte'
$n = $linhas.Count - 1
$dados = New-Object 'object[,]' ($n + 1), $ordem.Count
for ($j = 0; $j -lt $ordem.Count; $j++) { $dados[0, $j] = $titulos[$j] }
for ($i = 1; $i -le $n; $i++) {
    $v = $linhas[$i].Split(';')
    for ($j = 0; $j -lt $ordem.Count; $j++) {
        $s = $v[$col[$ordem[$j]]]
        if ($texto -contains $ordem[$j] -or $s -eq '') { $dados[$i, $j] = $s }
        else { $dados[$i, $j] = [double]::Parse($s, [Globalization.CultureInfo]::InvariantCulture) }
    }
}

$x = New-Object -ComObject Excel.Application
$x.Visible = $false; $x.DisplayAlerts = $false
try {
    $wb = $x.Workbooks.Open($Planilha)
    if ($wb.ReadOnly) { throw "A planilha esta aberta (somente leitura). Feche-a no Excel e rode de novo." }
    $ws = $wb.Worksheets.Item('CHUMBADORES')
    $ws.Cells.Clear()
    $ws.Range($ws.Cells(1, 1), $ws.Cells($n + 1, $ordem.Count)).Value2 = $dados
    $ws.Range($ws.Cells(2, 2), $ws.Cells($n + 1, 2)).NumberFormat = '@'
    $ws.Rows(1).Font.Bold = $true
    $ws.Cells($n + 3, 1).Value2 = 'Fonte única: ftool\codigo\portico\chumbadores.csv (o Estructto usa o mesmo arquivo). Não edite aqui: edite o CSV e rode sincronizar-planilha.ps1. ' +
        'Mecânicos: cargas MÉDIAS de ruptura (Walsywa C20, IPT; Indufix C30). Químicos: WQI/WQE ruptura de tração em C28, sem corte; WQA só carga RECOMENDADA (C20).'
    $ws.Columns.AutoFit() | Out-Null
    $ref = "=CHUMBADORES!`$A`$2:`$A`$" + ($n + 1)
    try { $wb.Names.Item('CHUMBADORES_LISTA').RefersTo = $ref } catch { $wb.Names.Add('CHUMBADORES_LISTA', $ref) | Out-Null }
    $wb.Save()
    "$n chumbadores copiados para a aba CHUMBADORES."
}
finally { if ($wb) { $wb.Close($false) }; $x.Quit(); [Runtime.InteropServices.Marshal]::ReleaseComObject($x) | Out-Null }
