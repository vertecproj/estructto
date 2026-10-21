# Changelog do projeto

Uma entrada por dia de trabalho: o que mudou, quem pediu, onde está o detalhe e o que ficou pendente.

## 21/10/2026 — Realista "absurdo": Render3D em DX11 com recortes e ligações (branch wt-render)

- Modo Realista em HelixToolkit.Wpf.SharpDX 3.1.2 (`Render3D*.cs`): sombra entre peças (shadow map 4096²), SSAO, PBR com cor por
  vértice (aço galvanizado/pintado/primer, concreto), MSAA 4× + FXAA, arestas de 1 px, chão com degradê e grade, halo de seleção,
  aproveitamento tingindo a peça. Sem GPU D3D11 (WARP) cai no Realista nativo (`ESTRUCTTO_3D=nativo` força). Capturas funcionam.
- Recortes nas juntas: pilar contínuo, viga para na face do pilar + folga, diagonal recua de todas as peças maiores, esquadria
  na cumeeira/canto. Extrusão com planos de corte; `GeometriaPerfis.CriarContorno` unificado (a cópia da Vista3D saiu).
- Ligações no 3D pelo contrato (`Ligacao3D`): chapas, cantoneiras, parafusos (cabeça, arruelas, porca), soldas, recortes.
  Fontes: grupos do motor com projeto (`Render3DMotor.cs`, `verificar_ligacao` por grupo), placa de base do 2D
  (`No.Placa` + `PlacaCalc`), exemplos do contrato nas capturas. Motor: tipos das 3 famílias registrados em
  `ligacoes_modelo.VERIFICADORES` (`_registrar_contrato`).
- Antes/depois, look, medições (2.430 barras: cena 25–34 ms, quadro 9–10 ms) e limites: [v3/RENDER.md](v3/RENDER.md);
  decisão do renderizador: [VISTA3D.md](VISTA3D.md).

## 20/10/2026 — Snap igual ao AutoCAD e Medir AutoCAD + Inventor na Vista 3D (branch wt-snap)

- Snap 3D novo (`Snap3D.cs`, motor puro): extremidade, meio, centro, nó, interseção real e aparente, perpendicular, extensão,
  paralelo, mais próximo e grade; rastreio por pausa de 0,5 s (F11), polar com incremento (F10), ORTO (F8), F3; one-off por
  Shift+botão direito ou END/MID/INT/PER/NEA… na linha de comando. Marcadores do AutoSnap em amarelo (`SnapOverlay.cs`).
- Coordenada digitada como no AutoCAD: com 1º ponto `x,y,z` é relativo; `#` absoluto, `@` relativo, `L<a` polar.
- Medir 3D (Ctrl+M, ME/MEDIR, botão em Desenhar 3D e em Vista): automático (ponto, barra, 2 pontos, 2 barras = distância mínima
  e ângulo, 3 pontos = ângulo) + Distância, Ângulo, Área, Raio, Cadeia (+/−); painel com acumulado e histórico.
- Testes `--testes-snap` (68) e capturas 50–53 (`ESTRUCTTO_CAPTURAS=snap`). Detalhe: [docs/v3/SNAP-MEDIR.md](v3/SNAP-MEDIR.md).

## 20/10/2026 — Exportar o modelo 3D para DWG, DXF e STEP (branch wt-export)

- Arquivo › Exportar (DWG…, DXF…, STEP…) e paleta (Ctrl+K), para o modelo espacial e o 2D. DWG/DXF AC1032 em mm com ACadSharp 3.8.0:
  uma POLYFACE MESH por barra na camada da seção (`ACO_`/`CONCRETO_`) + linhas na camada `ANALITICO`. STEP AP214 escrito à mão:
  um PRODUCT por barra (FACETED_BREP de faces planas) numa montagem. Perfis = os do modo Realista (cópia em `GeometriaPerfis.cs`).
- Testes `Estructto.exe --testes-exportar <pasta>` (38 asserções, guincho + edifício). Detalhe: [docs/v3/EXPORTAR.md](v3/EXPORTAR.md).
## 20/10/2026 — Modelagem 3D ligada: seleção, planos, cargas e seção (branch wt-ligar3d)

- Edição em lote usa a seleção múltipla da Vista3D (janela, cruzamento, Shift/Ctrl, árvore); nada escolhido = modelo inteiro.
- Ativar nível/elevação troca o plano de desenho (`Vista3D.PlanoAtual`); fantasmas desenhados pela Vista3D, fora do picking e da
  atração; Pilar pela altura dos níveis (desce ao nível de baixo).
- Vista3D desenha as cargas do caso ativo (setas nodais/pontuais, pente q→q2), analítico e Realista, Vista › Mostrar › Cargas.
- Desenhar 3D › Seção ▾ (modelo + Do catálogo…) e Apoio na base. Captura de ponta a ponta `49-fluxo3d-*`.
  Detalhe em [MODELO-ESPACIAL.md](MODELO-ESPACIAL.md#fluxo-de-modelagem-3d-captura-de-ponta-a-ponta).
## 20/10/2026 — Treliças, contraventamentos e terças (branch wt-trelicas)

- Motor: tipos `trelica_plana` (Pratt, Howe, Warren, Warren-V, K, Fink; banzos paralelos, uma água, duas águas, arco), `trelicas_serie`,
  `contraventamento` (X, diagonal, K, V, Λ, mão-francesa; entre 2 barras ou 4 nós, divide barras) e `tercas` (com tirantes) no mesmo
  `gerar_estrutura` (API, MCP). Rota nova `POST /api/gerar-estrutura/previa` (cópia do modelo, não muda nada).
- WPF: galeria **Treliças e contraventamentos** (Modelo › Gerar, Ctrl+K) com miniaturas, prévia ao vivo e Inserir (um Ctrl+Z).
  Detalhe em [v3/TRELICAS.md](v3/TRELICAS.md). Capturas `50-*`.
- Pendente: barra só de tração no motor; treliça espacial multiplanar; mísulas no galpão.
## 20/10/2026 — Grupos de ligações: a pior do grupo dimensiona todas (branch wt-grupos)

- Motor `aco/ligacoes_modelo.py`: identifica as ligações nos nós (base, viga–pilar mesa/alma, viga–viga, emenda, gusset, nó de
  treliça), agrupa por assinatura, edições do usuário (renomear, travar, mover, dividir, unir, projeto) que sobrevivem à
  re-identificação, dimensiona o grupo pela pior ligação em todas as combinações ELU. JSON: chave `ligacoes` (`versao_formato` 2 só com grupos).
  Exposto em `projeto.py`, rotas `/api/ligacoes/...` e 4 ferramentas MCP. Interface: **Ligações e bases › Gerenciar ligações**
  (`JanelaLigacoesGrupos.cs`), placa de base do grupo abre na JanelaPlaca com a pior base. Detalhe: [v3/LIGACOES-GRUPOS.md](v3/LIGACOES-GRUPOS.md). Capturas `49-*`.

## 20/10/2026 — Níveis e planos de trabalho no modelo espacial (branch wt-planos)

- Modelo › Planos: **Nível ▾** (ativa e isola o andar em planta, resto em fantasma), **Elevação X/Y ▾**, **3D completo**, painel
  **Níveis** (criar, renomear, cota, apagar, detectar), **Copiar pavimento para cima** (um lote: um Ctrl+Z) e **Gerar pilares entre níveis**.
  Paleta Ctrl+K e F1. Código `PlanosTrabalho.cs`; detalhe em [MODELO-ESPACIAL.md](MODELO-ESPACIAL.md#níveis-e-planos-de-trabalho). Capturas `43-*`.
- Pendente: ligar o adaptador `PlanoNaVista` ao `Vista3D.PlanoAtual` depois do merge (comentário `ponytail:`).
## 20/10/2026 — Cargas e casos no modelo espacial (branch wt-cargas3d)

- Cargas pontuais e distribuídas trapezoidais (q, q2) no painel da barra; carga em área por nível (lote); Casos, Vento, Sismo,
  Pavimentos e Combinações editam o modelo espacial: [MODELO-ESPACIAL.md](MODELO-ESPACIAL.md) ("Cargas e casos"). Capturas `48-espacial-*`.
## 20/10/2026 — Edição em lote do modelo espacial (branch wt-edicao3d)

- Modelo › Modificar no documento espacial: Mover, Copiar (N cópias), Espelhar (X/Y/Z = valor), Rotacionar (eixo vertical, cópias
  polares), Matriz nx×ny×nz, Dividir barra (N partes ou distância), Unir nós (tolerância) e Propriedades em lote; também na paleta.
  Cada operação = um `PUT /api/modelo` = um Ctrl+Z. Nós fundem a 1 mm; cópias levam seção, material, params e cargas.
  Seleção por `IFonteSelecao` (adaptador provisório: escolha única; nada = modelo inteiro). Testes `--testes-edicao`, capturas `48-*`.
  Detalhe: [MODELO-ESPACIAL.md](MODELO-ESPACIAL.md#editar-a-seleção-edição-em-lote).
## 20/10/2026 — Desenhar no 3D (branch wt-desenho3d)

- Modelo espacial: grupo **Desenhar 3D** (Selecionar, Barra, Pilar) e **Modelo 3D vazio**. Plano de trabalho (z, x ou y = c) com
  grade, atração a nó/meio/grade, ortogonal/polar, entrada dinâmica (comprimento, ângulo, x,y,z, @dx,dy,dz), seleção por janela e
  cruzamento; cada clique = um lote do motor (um passo de desfazer). API pública para planos, edição e painéis. Detalhe:
  [MODELO-ESPACIAL.md](MODELO-ESPACIAL.md#desenhar-no-3d-modelo--desenhar-3d). Capturas `43-desenho3d-*`.

## 20/10/2026 — Vista 3D Realista (branch wt-vista3d)

- Botão **Realista** em Vista › Navegar (lembra a escolha): perfis reais extrudados (I, U, L, tubos, caixão, maciços) com as
  dimensões do catálogo ou do JSON do motor, aço metálico/concreto quente, três luzes, contorno de ~1 px, sombra suave no chão e
  chão em degradê; aproveitamento tinge o material e a deformada usa as peças reais. ~2.400 barras em ~150 ms (malhas juntadas).
  HelixToolkit.SharpDX avaliado e não adotado. Detalhe: [VISTA3D.md](VISTA3D.md#modo-realista). Capturas `42-*`.

## 20/10/2026 — Gabriel (início da unificação com o motor Python)

- Ponte 2D ↔ motor: vento, sismo, pavimentos e casos do motor agora ficam no documento (`Modelo.MotorExtra`, JSON opaco no `.estructto`), entram no desfazer/modificado e voltam ao motor em cada `MotorModelo.Enviar`. `--teste-motor` não trava mais (diálogo modal de recuperação), motor sem órfãos (Job Object + `--pai`), partida única do motor, `Motor.Exclusivo`, corpo `{}` em vez de `null`. Detalhe: `docs/UNIFICACAO-MOTOR.md` › "Dados só do motor" e "Processo do motor".

- Manual do usuário em [docs/manual/](manual/README.md): 11 páginas (primeiros passos, abas na ordem do fluxo, CYPE, uso por IA/MCP, limitações e ART). Vista "Realista" não existe no código: registrada como ausente.
- Relatório unificado: com Motor = Python, Relatório abre o relatório NBR do motor num WebView2 (sem script e sem rede), com
  Salvar PDF/HTML, campo Empresa/autor no carimbo (padrão vazio; parâmetro `empresa` novo no motor, com teste) e o anexo das placas de
  base/chumbadores/soldas do Estructto no fim. Gancho `RelatorioMotor.Abrir(Window, titulo)` para o documento espacial.
  Detalhe: [RELATORIO-UNIFICADO.md](RELATORIO-UNIFICADO.md).

- Marca única **Estructto**: relatório (cabeçalho, carimbo, logo `docs/logo-estructto.svg`), servidor MCP `estructto` (+ `codigo/.mcp.json` e teste stdio), CLI/servidor web e textos da interface alinhados às abas novas (Modelo, Ações, Análise, Dimensionamento). Pacote Python segue `prossen_estrutural` (interno).
- Animações (fase 8 adiantada para a casca atual): helper único `Anim.cs` (120/160 ms UI, 250–400 ms espacial, `CubicEase`, desliga com
  "efeitos de animação" do Windows e no modo `--capturas`). Aplicado em faixa, backstage, painéis, diálogos, tela inicial, HUD, Ctrl+K,
  dicas, aviso, diagramas da vista 2D, seleção e barra de status. Detalhe e uso em janelas novas: [ANIMACOES.md](ANIMACOES.md).
- Faixa ligada às janelas do motor (`FaixaMotor.cs`): Modelo › Importar CYPE (também em Arquivo); Ações › Ações automáticas (Vento,
  Sismo, Pavimentos, Combinações); Análise › Modal e estabilidade; Dimensionamento › Verificação de barras e Catálogo de perfis; Ligações
  e bases › Ligações (Parafusos, Filete de solda, Chapa simples, Ligação de tração). Com Motor = Python, "Casos e combinações" e "Verificar
  barra" abrem as janelas do motor. O modelo 2D vai ao motor antes de abrir (Estabilidade e Verificação calculam antes); erro do motor
  vira toast. Paleta Ctrl+K e ajuda (F1) atualizadas; 11 ícones novos. Teste ao vivo: `Estructto.exe --teste-motor=saida.png arquivo`.

- Ponte com o motor Python ([UNIFICACAO-MOTOR.md](UNIFICACAO-MOTOR.md)): `MotorModelo.cs` converte o modelo e lê resultados; opção Motor em
  Análise › Opções (Interno padrão; Python roda no F5 com progresso); `--conferir-motor` (análise confere a 10⁻¹¹ nos guinchos);
  `codigo/motor/empacotar.ps1` + publish põe `motor\` ao lado do exe; motor desligado ao sair.
- Ponte, parte 2 ([UNIFICACAO-MOTOR.md](UNIFICACAO-MOTOR.md)): kx/ky/Lb do `Flambagem` vão ao motor (KL/r das colunas igual nos dois);
  motor ganhou carga distribuída linear (`q2`, engastamento + diagramas, `TestCargaLinear`) e a q perpendicular vai como carga linear
  (conferência 4,5·10⁻¹⁰); com Python, a tela mostra o `MotorResultado` (diagramas, tabelas, reações, % Aprov.) em Mostrar › "Python: ...";
  mensagem clara para molas e recalques. Pendente: esbeltez de tração do motor usa K·L.
- Plano da unificação em [UNIFICACAO.md](UNIFICACAO.md) e revisão de abas/telas em [REVISAO-TELAS.md](REVISAO-TELAS.md) (proposta aprovada).
  Passo 1 feito: faixa nova sobre as funções atuais, abas na ordem do trabalho — Modelo (Início + Estrutura), Ações (ex-Cargas), Análise,
  Dimensionamento, Ligações e bases, Relatório, Vista. Calcular (F5) só na barra de acesso rápido; zoom só em Vista; Mostrar e Tabelas em Vista;
  Exportar CSV só em Relatório (sai do Arquivo, junto com Assistentes). Renomes: Assistentes → Gerar estrutura, Mapa de calor → Mapa de
  aproveitamento (o modo "Verificação" de 3 cores sai; V/VERIFICAR abrem o mapa), Comparar → Verificar barra, Móvel em X → Móvel horizontal,
  painéis "Projeto" e "Tabelas". Tirar trecho e Selecionar a barra contínua vão para o menu do botão direito e a paleta Ctrl+K.
  O "n" global continua (sai com o motor). Indicador "Modelo alterado — Calcular (F5)" não feito: hoje o cálculo é refeito a cada alteração.
  Build ok, 200 testes ok, capturas 09-ribbon-* conferidas.
- Vista 3D (fase 4 de UNIFICACAO.md): `Vista3D.cs` com `Viewport3D` nativo, entrada neutra (nós/barras/esforços/aproveitamento/deslocamentos)
  e adaptador do modelo 2D. Botão "Vista 3D" na aba Vista. Detalhes e decisão (sem HelixToolkit) em [VISTA3D.md](VISTA3D.md). Capturas 40-vista3d-*.
- Janelas novas da aba Ações (REVISAO-TELAS 3.4), sobre o motor Python: `JanelaVento` (V0, S1, S3, categoria/classe, planta, direções,
  coeficientes automáticos ou Ca/Cpi manuais; q(z), S2 e força por pavimento), `JanelaSismo` (zona, terreno, categoria, sistema da tabela 6
  com R/Ω0/Cd, período; Cs, W, H e forças/torção por pavimento), `JanelaPavimentos` (nome/cota/diafragma, detectar pela geometria),
  `JanelaCasosCarga` (G/Q/V/S/E + subtipo; casos automáticos travados) e `JanelaCombinacoes` (só leitura, filtro ELU/ELS, γ·ψ por caso e
  γ e ψ separados da combinação selecionada). Entradas em `ComandosAcoes.Abrir*(dono)`, botões da faixa a ligar. Capturas 29 a 33 com
  amostra real do motor (edificio_concreto). Pendente: espectro desenhado no sismo; q(z) por pavimento é recalculado na janela (cópia da
  tabela 21) e γ/ψ são deduzidos do produto, até o motor devolver esses valores.
- Janelas de análise sobre o motor (REVISAO-TELAS 3.4), cada uma no seu arquivo: Modal (`JanelaModal`, evento ModoEscolhido), Estabilidade
  (`JanelaEstabilidade`, γz/B2 com régua 1,0–1,4), Verificação de barras (`JanelaVerificacaoBarras`, lista ordenável com filtros e duplo clique),
  Verificar barra (`JanelaVerificarBarraMotor`, critério × item NBR × combinação × Sd ≤ Rd), Ligação (`JanelaLigacao`, campos de /api/ligacoes/tipos),
  Comparar/Importar CYPE (`JanelaCype`) e Catálogo de perfis (`JanelaCatalogo`). Abertura por `ComandosAnalise.Abrir...(dono)` (faixa ainda não
  liga os botões). Capturas 40–46 com JSON reais do motor embutidos (`codigo/Estructto/amostras/`).
- Modelo espacial ([MODELO-ESPACIAL.md](MODELO-ESPACIAL.md)): documento 3D cuja fonte é o JSON do motor (`DocumentoEspacial.cs`). Abre
  .json/.ed3/exemplos, Gerar 3D (edifício, galpão, pórtico), edição por PATCH, desfazer/refazer e F5 pelo motor, mapa de aproveitamento e
  diagramas na Vista3D, salva .json. 2D sem mudança. Capturas 41-*.
- Modelo espacial, lista "Falta" fechada: árvore, tabelas (Nós/Barras/Verificação), avisos e legenda do HUD mostram o modelo e os
  resultados do motor; criar/apagar nó e barra, cargas nodais e distribuídas por caso, seção do catálogo (`/api/catalogo`) entra no modelo;
  Resultados › Mostrar escolhe envoltória ELU (padrão) ou uma combinação; .json/.ed3 nos recentes; troca de aba recoloca o modelo e avisa
  que o desfazer é por documento ativo. Capturas 41-espacial-tabela-*, -combinacao, -barra-nova, -no-novo.

## 20/10/2026 — Bruno (v2: modernização da interface)

- Interface inteira modernizada (feita na cópia `estructto-v2` de madrugada e trazida para cá): tema novo, ícones refeitos, painel de
  propriedades WPF (sai o WinForms), vista com HUD/diagramas/mapa de calor, placa de base refeita em 3 passos com 3D, janelas e tela inicial
  novas, Ctrl+K e entrada dinâmica no cursor. Cálculo inalterado (200 testes). Detalhe e decisões: [v2/RELATORIO.md](v2/RELATORIO.md).
- Modo `--capturas` + `capturar.ps1` para conferência visual automática. Build passa a sair em `C:\dev\build\EstructtoV2`.

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
