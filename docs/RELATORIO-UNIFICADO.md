# Relatório unificado (motor Python + memorial do Estructto)

## Quando abre o quê

| Documento | Botão Relatório › Relatório |
|---|---|
| 2D com Motor = Interno | Janela antiga (`Relatorio.cs`, `JanelaRelatorio`, FlowDocument): Resumo/Completo, imprimir |
| 2D com Motor = Python | Envia e calcula no motor (`PrepararMotor(true)`), abre `JanelaRelatorioMotor` com o relatório NBR do motor **e o anexo das placas de base** |
| Espacial | O líder chama `RelatorioMotor.Abrir(Window dono, string tituloDoc)` (modelo e cálculo já estão no motor; sem anexo, o espacial não tem placa do Estructto) |

Código: `codigo/Estructto/RelatorioMotor.cs` (gancho estático, geração do HTML e janela). Em `Relatorio.cs` só mudou o
`AbrirRelatorio` (desvio para o motor), `AnexoPlacas()` e a tabela das placas virou `TabelaPlacas` (usada pelos dois).

## Visualizador: WebView2

`Microsoft.Web.WebView2` 1.0.2957.106. O runtime do WebView2 (Edge/Chromium) vem instalado no Windows 11 e é atualizado pelo
Windows, então não há nada a distribuir. Motivos para não usar o `WebBrowser` do WPF: ele é o motor do Internet Explorer 11
(desativado no Windows 11), não entende o CSS do relatório (grid do carimbo, `@page`, fontes em data URI) e não grava PDF.
O WebView2 grava o PDF direto (`PrintToPdfAsync`), com o mesmo layout da impressão: margens, numeração "Página x de y" e carimbo
em cada folha vêm do `@page`/`@media print` do `relatorio.css`.

Segurança (o relatório não tem JS e não acessa rede):
- `IsScriptEnabled = false`, sem DevTools, sem menu de contexto, sem web messages, sem novas janelas.
- Filtro `WebResourceRequested` em tudo: só `file:` (o próprio relatório) e `data:` (fontes e imagens embutidas); o resto recebe 403.
- `NavigationStarting` cancela qualquer navegação que não seja o arquivo do relatório (links não saem do programa).
- Dados do usuário do WebView2 em `%LocalAppData%\Estructto\WebView2` (a pasta do exe pode não ser gravável).

O HTML é gravado pelo motor na pasta de trabalho (`POST /api/relatorio`, `formato: html`, `caminho: relatorio-estructto.html`) e o
WebView2 abre o arquivo (`NavigateToString` tem limite de 2 MB; as fontes embutidas já passam disso).

Botões: **Salvar PDF…** (`PrintToPdfAsync`, fundos ligados, cabeçalho/rodapé do navegador desligados) e **Salvar HTML…** (cópia do
arquivo: autônomo, abre sem internet).

## Junção com o memorial do Estructto (placas, chumbadores, soldas)

O motor Python não calcula placa de base, chumbador nem a solda pilar–placa: isso continua em `Nucleo.cs` e no memorial do
Estructto (`RelatorioMemorial.cs`, `MemorialPlaca`). Escolha: **anexar ao fim do corpo do HTML do motor, como páginas em imagem**.

- `AnexoPlacas()` monta um FlowDocument só com a tabela das placas (`TabelaPlacas`) e `MemorialPlaca` de cada nó com placa
  (mesmas fórmulas e mesmo visual da janela Placa de base e do relatório completo; equações do WpfMath).
- `RelatorioMotor.Gerar` pagina esse FlowDocument em folhas de 175 × 180 mm (largura útil do A4 do relatório; altura que cabe com o
  título do anexo acima da reserva do carimbo), rende cada folha em PNG a 192 dpi e insere, antes de `</td></tr></tbody><tfoot>`,
  um `<h2 class="quebra">A Anexo — placas de base, chumbadores e soldas</h2>` + `<img src="data:image/png;base64,…">` por folha.
- Resultado: um documento só (tela, PDF e HTML), com o carimbo e a numeração do motor em todas as folhas.

Por que imagem e não converter o FlowDocument para HTML: o memorial tem equações renderizadas (WpfMath), blocos com borda e tabelas
com estilo próprio; converter seria reescrever o memorial (e mantê-lo em dois lugares). A imagem é exatamente o que o Estructto já
imprime. Limite: o texto do anexo não é selecionável/pesquisável no PDF. Quando incomodar, gerar o anexo em HTML no próprio memorial.

Os esforços do anexo são os do resultado mostrado no programa (o mesmo critério da janela Placa de base). Modelo sem placa: sem anexo.

## Empresa/autor no carimbo

Campo **Empresa / autor** na barra da janela do relatório (Enter ou **Aplicar** gera de novo). Padrão vazio: o carimbo sai com o campo
em branco para preencher à mão (antes saía "PROSSEN ENGENHARIA" fixo). Guardado em `%AppData%\NovoFtool\empresa.txt`
(`RelatorioMotor.Empresa`).

Motor: parâmetro opcional `empresa` em `relatorio.html(modelo_d, calculo_d, empresa="")`, `projeto.relatorio_html`,
`projeto.gerar_relatorio` e nas rotas `GET /api/relatorio.html?empresa=…` e `POST /api/relatorio` (`{"empresa": …}`). Texto escapado.
Teste: `testes/test_relatorio.py::test_empresa_no_carimbo_escapada` (e o teste do carimbo agora exige que o padrão não traga empresa).

## Conferência

Captura `41-relatorio-motor.png` e `41-relatorio-motor.pdf` (em `Capturas.cs`, junto das capturas 40 do motor, modelo
guincho-com-placa): a janela real com o relatório do motor e o PDF gravado pelo botão, com o anexo das placas.
