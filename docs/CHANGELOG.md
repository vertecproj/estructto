# Changelog do projeto

Uma entrada por dia de trabalho: o que mudou, quem pediu, onde está o detalhe e o que ficou pendente.

## 20/10/2026 — Bruno (v2: modernização da interface)

- Interface inteira modernizada (feita na cópia `estructto-v2` de madrugada e trazida para cá): tema novo, ícones refeitos, painel de
  propriedades WPF (sai o WinForms), vista com HUD/diagramas/mapa de calor, placa de base refeita em 3 passos com 3D, janelas e tela inicial
  novas, Ctrl+K e entrada dinâmica no cursor. Cálculo inalterado (200 testes). Detalhe e decisões: [v2/RELATORIO.md](v2/RELATORIO.md).
- Modo `--capturas` + `capturar.ps1` para conferência visual automática. Build passa a sair em `C:\dev\build\EstructtoV2`.
- Placa → Fixação (pedido do Bruno): painel **Trocar chumbador** agrupado por família (linha do fabricante) → diâmetro → hef; a família
  em uso abre, as outras ficam fechadas mostrando quantos passam e o menor que passa. No cartão, linha **Diâmetro** com − / + (troca o
  diâmetro na mesma família, mantendo o hef mais próximo) e − / + do hef. Texto do coeficiente explica a origem ("recomendado pela
  Walsywa para carga dinâmica com alto risco") e avisa quando o n digitado fica abaixo, com link "usar o recomendado". Só interface
  (`JanelaPlaca.cs`); cálculo inalterado.
- Placa → Solda (pedido do Bruno): linha **Segurança** "n = 0,577·FEXX / τ ≥ 1,92". Critério: a norma (AWS/AISC, tensões admissíveis)
  não pede n separado; o limite τ ≤ 0,30·FEXX já embute n = 0,577/0,30 ≈ 1,92, então "passa" e "n ≥ 1,92" são a mesma condição.
  É o mesmo `NEquiv` que o memorial já mostrava. Só interface.
- **Correção (bug relatado pelo Bruno): a placa "aprovava" e ao reabrir voltava "não passa".** Causa: a janela fixava UMA combinação
  ao abrir (a pior para a placa daquele momento, ex. carga giratória a 0°) e recalculava tudo nela; trocando chumbador/chapa, outra
  combinação (180°) passava a mandar sem a janela perceber. Critério novo: com envoltória, **cada item é verificado na sua pior
  combinação, reavaliada a cada mudança** — fixação = maior ApFix, chapa = maior espessura necessária, solda = maior τ — e o título
  mostra qual combinação manda em cada um. Vale também para Trocar, régua de diâmetros, Sugerir, Dimensionar, "Aplicar também em",
  desenho, relatório e memorial (`PlacaPior`/`EnvoltoriaPlaca` em `Acoes.cs`; `PiorCombinacao` em `JanelaPlaca.cs`). Motor inalterado
  (200 testes). Conferido com cópia do modelo do Bruno: CBE 3/8" hef 47 em 230×250×16 agora já aparece "não passa" (fixação 176% a 180°). Build, capturas e 200 testes ok (SDK .NET 8 instalado no PC do Bruno).

## 19/10/2026 — Bruno

- **Função "Comparar fixações" ([TAREFA-COMPARAR-FIXACOES.md](TAREFA-COMPARAR-FIXACOES.md)):**
  - **Dados embutidos:** `codigo/portico/fixacoes.csv` (12 linhas de produto: 9 calculáveis de `chumbadores.csv` + 3 informativas `Walsywa WLT`, `Walsywa Golpe de Metal`, `Walsywa PU 500`, com atributos da tabela 5.1 e textos da seção 2 de [FIXACOES-GUIA.md](FIXACOES-GUIA.md)) e `codigo/portico/fixacoes-pares.csv` (11 pares comparativos da seção 5.2). Embutidos em `Estructto.csproj` e `compilar.ps1`.
  - **Motor (`codigo/portico/src/Nucleo.cs`):** acrescentadas no fim do arquivo as classes `FixacaoLinha`, `ParFixacao`, `ColunaComparacao` e `Fixacoes`, reutilizando `Biblioteca.Linhas` e `PlacaCalc.CandidatoChumbador` (sem alterar nada existente).
  - **Interface (`codigo/Estructto/JanelaComparar.cs`):** nova janela `JanelaComparar : JanelaEscura` com construtor `JanelaComparar(Modelo m, No n, ResPlaca r, PlacaBase p, IEnumerable<string> iniciais)`, busca, filtros, lista de todas as linhas, colunas lado a lado com rolagem horizontal, barras "Capacidade usada", textos de uso/limitações, comparativo de pares (5.2) e avisos obrigatórios (5.3). `Janelas.cs` e `Acoes.cs` não foram editados (edição concorrente; botão "Comparar…" será ligado em `JanelaPlaca`).
  - **Testes (`codigo/portico/src/Testes.cs`):** 26 verificações novas (total: 196 testes passando em `compilar.ps1 -Testes`; `dotnet build` com 0 erros e 0 avisos).
  - Botão "Comparar…" ligado no cartão do chumbador da `JanelaPlaca` (abre com o chumbador atual + o menor de outra linha que passa).
- **Janela da placa refeita** (`Janelas.cs`, classes `JanelaPlaca`/`PlanoPlaca`): resposta pronta ao abrir, cartões Fixação
  (chumbador ou parafuso), Chapa e Solda com − / + e régua; "Trocar chumbador" agrupado; cotas editáveis no desenho; vista 3D
  com o ângulo real da barra; "Ver cálculo" em tabela; dicas no mouse.
- **Coeficiente de segurança por placa** (`PlacaBase.Seguranca`, 0 = o do modelo) + "Melhores opções com esses coeficientes".
- **Outras fixações:** mostra cada pé de pilar com esta placa na sua pior combinação, aponta o pior caso, "Dimensionar pelo
  pior caso" e copia a placa para os nós marcados ao Aplicar (`Acoes.AbrirPlaca` passa `AvaliarEm`/`Candidatos`).
- **Ajustar tamanho da chapa:** borda ≥ 1,5·d, passo ≥ 3·d (chumbador também ≥ 6·d; opcional ≥ 3·hef), furo até o perfil
  ≥ 1,5·d + 10 mm. Os slides não trazem passo/margem (só porca 2·d entre quinas e arruela 1,5·d, Aula 2 págs. 18 e 49):
  1,5·d e 3·d são prática usual adotada.
- **Correção no motor:** `Chumbadores.Achar("")` devolvia um chumbador sem código (agora null = parafuso).
- **Ferramenta Medir** (Ctrl+M / botão na aba Início) e dicas no mouse em todo o programa (feitos pelo agy, conferidos).
- **Repositório git** criado em `ftool/` (sem `referencias/`, `_terceiros/` nem `.exe`; ver `.gitignore`).

## 19/10/2026 — Gabriel

- **Catálogo único de chumbadores** (`codigo/portico/chumbadores.csv`, 76 itens): Walsywa mecânico (CB, CBPL, CBE, CBEPL, WB),
  Walsywa químico (WQA, WQI 44, WQE 500 com barra roscada) e Indufix. Estructto embute o CSV; planilha sincroniza por
  `sincronizar-planilha.ps1`. Detalhe: [CHUMBADORES.md](CHUMBADORES.md).
- **Químicos** com critério decidido pelo Gabriel: ruptura ÷ tabela (WQI/WQE), recomendada com n = 1 (WQA), corte só no aço
  da barra quando o catálogo não traz, ψc = √(fck/fck ensaio), aço da barra por von Mises (classe própria, padrão 4.6).
- **Estructto:** campos novos na placa (fck do concreto, classe da barra roscada); memorial mostra origem da carga, ψc e aço.
  170 testes passam (inclui testes "= planilha"). Estructto.exe recompilado; antigo em `C:\dev\build\Estructto\Estructto-antes-2026-10-19.exe`.
  .NET 8 SDK instalado no PC do Gabriel.
- **Planilha:** aba CHUMBADORES gerada do CSV; aba de cálculo com Tref/Vref, ψc, n,ref, aço da barra e distâncias do
  catálogo; MEMORIAL atualizado. Resultados dos mecânicos atuais idênticos aos de antes.
  Backup: `PLANILHA DE CÁLCULO - backup 2026-10-19 antes catalogo unico.xlsx`.
- **Fluxo com Antigravity:** [DELEGAR-AGY.md](DELEGAR-AGY.md) (vale para Gabriel e Bruno).
- **Guia de escolha de fixações:** [FIXACOES-GUIA.md](FIXACOES-GUIA.md) — tipo, uso recomendado pelo fabricante, usos típicos,
  limitações, tabela comparativa e roteiro "como escolher" para cada linha Walsywa (citações conferidas com os PDFs).
  Base para uma ajuda de escolha dentro do Estructto. Seção 5 (diferenças entre fixações): atributos padronizados,
  11 comparações em pares e regras para comparar no software.
- **Organização:** documentação movida para `docs/` (antes na raiz e em `codigo/Estructto/`); índice no `README.md`.

**Pendente**
- Planilha: cones sobrepostos (ψ) como no Estructto, em vez de reprovar direto com s < s,min.
- Placa de base atual não passa (base n = 3,79, pé 0° n = 7,86 contra 15; chapa 106%).
- Decidir se a ampola WQA (n = 1 sobre a recomendada) é aceitável para o guincho.
- Testar pela interface do Estructto uma placa com chumbador químico.
- **Função "Comparar fixações" no Estructto:** implementada em `JanelaComparar.cs`, `fixacoes.csv`, `fixacoes-pares.csv` e `Nucleo.cs` ([TAREFA-COMPARAR-FIXACOES.md](TAREFA-COMPARAR-FIXACOES.md)); falta apenas ligar o botão "Comparar…" em `Janelas.cs` e incluir o PDF do catálogo Indufix quando disponível.
- Levar o guia de fixações para o Estructto (ajuda na escolha do chumbador) e incluir o Indufix (falta o PDF do catálogo).

## 18/10/2026

- Auditoria do motor de verificação (13 itens, 7 críticos) e correções: [historico/2026-10-18-AUDITORIA-VERIFICACAO.md](historico/2026-10-18-AUDITORIA-VERIFICACAO.md).
- Padrão do memorial: [PADRAO-MEMORIAL.md](PADRAO-MEMORIAL.md). Prompt de correção da planilha: [historico/2026-10-18-PROMPT-memorial-excel.md](historico/2026-10-18-PROMPT-memorial-excel.md).
- Análise do catálogo Walsywa (coeficiente baixo/alto risco, cones sobrepostos): [CHUMBADORES.md](CHUMBADORES.md).
