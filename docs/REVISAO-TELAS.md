# Revisão de abas e telas — Estructto unificado (fase 7 de UNIFICACAO.md)

Data: 20/10/2026. Base: build Release ok (0 erros), 134 capturas em `C:\dev\build\EstructtoV2\capturas`, código de `codigo/Estructto`,
`software/ESPEC.md` (operações 1–33 de `projeto.py`) e `aco/ligacoes.py`. Referências: `docs/v2/GUIA-DESIGN.md`,
`docs/v2/refs/ux-designer-skill/SKILL.md`, `dotnet-wpf-modern/SKILL.md`. Nenhum código foi alterado.

---

## 1. Inventário da interface atual

### 1.1 Janela principal (`MainWindow.xaml` + `MainWindow.xaml.cs`, faixa montada em `MontarFaixa()`)

| Zona | O que faz | Código |
|---|---|---|
| Barra de acesso rápido | Novo, Abrir, Salvar, Desfazer, Refazer | `MainWindow.xaml.cs` `MontarFaixa` (início) |
| Arquivo (Backstage) | Novo, Abrir, Salvar, Salvar como, Assistentes (cartões), Recentes (miniaturas), Importar .ftl, Exportar CSV, Ajuda, Sair | `MainWindow.xaml` `Bastidor`; `MainWindow.xaml.cs` ~l.496 (`bsInicio`, `bsRecentes`); `Inicio.cs` |
| Aba **Início** | Desenhar (Selecionar, Barra, Nó, Medir, Assistentes) · Modificar (Mover, Copiar, Espelhar, Dividir, Apagar) · Vista (Enquadrar, Aproximar, Afastar) · Calcular | `MontarFaixa` l.296 |
| Aba **Estrutura** | Apoios (Engaste, Fixo, Móvel, Móvel em X, Sem apoio) · Ligações (Rótula, Placa de base) · Seção e material (Família, Seção, Material, Aplicar à seleção/a todas) · Barra contínua (Unir, Tirar trecho, Separar, Selecionar) | `MontarFaixa` l.339 |
| Aba **Cargas** | Casos de carga (Caso ativo, Casos e combinações) · Carga no nó (Fx, Fy, Mz) · Carga distribuída (qx, qy, q⊥ início/fim) · Outras (Peso próprio, Limpar) | `MontarFaixa` l.356; `Casos.cs` `MontarCasosNaFaixa` |
| Aba **Análise** | Cálculo (Calcular, Deformação por cortante) · Critérios (Segurança n, Flecha L/, Eletrodo) | `MontarFaixa` l.371 |
| Aba **Resultados** | Resultado (Mostrar: caso/comb./envoltória) · Diagramas (Deformada, N, V, M, Ampliar, Reduzir) · Verificar (Verificação, Mapa de calor, Comparar, Dimensionar, Placa de base) · Mostrar (Reações, Valores, Cargas, Nomes) · Relatório (Relatório, Tabelas, Exportar CSV) | `MontarFaixa` l.381; `Casos.cs` |
| Aba **Vista** | Navegar (Enquadrar, Aproximar, Afastar) · Grade · Painéis (Projeto, Propriedades, Tabelas) · Ajuda | `MontarFaixa` l.410 |
| Abas contextuais **Barra** / **Nó** | Barra: Verificar/comparar, Dimensionar, Rótulas, Dividir, Apagar. Nó: Apoios, Aplicar carga, Placa de base, Apagar | `MontarFaixa` l.430–455; `MainWindow.xaml` `CtxBarra`, `CtxNo` |
| Barra de documentos | Início + abas de arquivo abertas (multi-documento) | `MainWindow.xaml` l.24; `Documentos.cs` |
| Painel **Gerenciador do projeto** | Árvore: Modelo, Nós, Barras, Seções, Materiais, Apoios, Com carga | `MainWindow.xaml` `PainelProjeto`; `Paineis.cs` |
| Vista (desenho) | Pórtico 2D, snaps, HUD (Deformada/N/V/M/% Aprov. + escala), mapa de calor, inspeção ao passar o mouse, etiquetas, entrada dinâmica, toasts | `Vista.cs`, `Hud.cs` |
| Linha de comando | Comandos estilo AutoCAD, histórico (F2) | `MainWindow.xaml` l.89; `Acoes.cs` (histórico l.1223) |
| Painel **Tabelas de resultados** | Nós · Barras · Verificação · Soldas | `MainWindow.xaml` `PainelTabelas`; `Paineis.cs` |
| Painel **Avisos** | Lista de avisos do modelo/cálculo | `MainWindow.xaml` `PainelAvisos` |
| Painel **Propriedades** (Inspector) | Modelo/nó/barra/carga/multi-seleção; ações rápidas; seletor de perfil com miniatura | `Inspector.cs` |
| Barra de status | Mensagem do cálculo, selo de avisos, Snap/Grade/Orto/Polar/Objetos/Rastreio, coordenadas, unidades | `MainWindow.xaml` l.37 |
| Paleta Ctrl+K | Busca de comandos, perfis e objetos | `Paleta.cs` |

### 1.2 Janelas e diálogos

| Janela | O que faz | Código | Captura |
|---|---|---|---|
| Tela inicial | Modelo em branco, 4 assistentes com miniatura, recentes | `Inicio.cs` | 01-inicio |
| Assistente de estruturas | Viga contínua, Pórtico, Treliça, Galpão com prévia ao vivo | `Janelas.cs` `JanelaAssistente` (l.564), `Assistentes.cs` | 26-assistente-* |
| Casos de carga e combinações | Casos (tipo, peso próprio), matriz de combinações, "Gerar NBR 8681/8800" | `Casos.cs` `JanelaCasos` (l.220) | 24-casos* |
| Verificação da barra | Veredito, 4 barras de capacidade (tensão, corte, flecha, flambagem), tabela de perfis da família, "Usar o perfil" | `Janelas.cs` `JanelaVerificacao` (l.191) | 23-verificacao-* |
| Placa de base | Veredito + chips; Planta/3D/Forças/Ficha/Cálculo; ① Fixação ② Chapa ③ Solda; Sugerir; Aplicar também em… | `JanelaPlaca.cs`, `PlacaDesenho.cs`, `Solda.cs` | 20/21/29-placa-* |
| Comparar fixações | Catálogo de chumbadores com filtros e avisos | `JanelaComparar.cs` | 22-comparar |
| Relatório | Resumo/Completo, imprimir/PDF | `Relatorio.cs` `JanelaRelatorio`, `RelatorioMemorial.cs` | 25-relatorio |
| Ajuda e atalhos | Busca, seções, convenções | `Janelas.cs` `JanelaAjuda` (l.751) | 28-ajuda* |
| Histórico de comandos | Lista do que foi digitado (F2) | `Acoes.cs` l.1223 | 39-historico |
| Caixa / Pergunta | Mensagens e confirmações | `Janelas.cs` `Caixa`, `Pergunta` | 27-caixa-mensagem |
| Popups | Trocar perfil, Sugerir, Aplicar em… | `Inspector.cs`, `JanelaPlaca.cs` | 34-inspector-perfil-popup, 29-* |

### 1.3 Problemas que as capturas mostram (independentes da unificação)

- **Navegação duplicada:** Enquadrar/Aproximar/Afastar aparecem em Início e em Vista; Calcular em Início e em Análise; Placa de base em Estrutura, Resultados e na aba contextual Nó; Tabelas em Resultados e Vista; Exportar CSV em Resultados e Arquivo.
- **"Resultados" faz quatro trabalhos:** ver esforços, verificar, dimensionar e relatar. É a aba mais cheia e mistura análise com dimensionamento.
- **Critério "Segurança n" na aba Análise** (06-momento): é tensão admissível. Com NBR 8681/8800 a segurança vem de γ e ψ nas combinações e de γa1 na resistência. Continuar mostrando "n" confunde.
- **Verificação da barra** (23-*) é "simplificada, não substitui a NBR 8800". Vai ser substituída pela verificação do motor.
- **Casos e combinações** (24-*) é uma matriz caso × combinação. Com vento em 4 direções, sismo e sobrecargas, o motor gera dezenas de combinações; a matriz editável não escala.
- **Grupos de carga só 2D** (Fx, Fy, Mz; qx, qy). O motor é 3D (6 GL, 3 componentes).

---

## 2. Funções do motor a encaixar

Operações públicas de `projeto.py` (ESPEC 6.2) e onde vão morar:

| Função do motor | Operação | Lugar na nova interface |
|---|---|---|
| Modelo novo/abrir/salvar, exemplos | `novo_modelo`, `carregar`, `salvar`, `exemplos`, `abrir_exemplo` | Arquivo + Tela inicial |
| Importar CYPE `.ed3` | `importar_cype` | Arquivo › Importar (junto do `.ftl`) |
| Comparar com CYPE | `comparar_cype` | Análise › Validação › Comparar com CYPE (janela própria) |
| Nós, barras, seções, materiais | `adicionar`, `editar`, `remover`, `listar`, `adicionar_lote` | Aba Modelo + Inspector + árvore |
| Pavimentos e diafragma | `adicionar(pavimento)` | Aba Modelo › Pavimentos |
| Geradores (pórtico plano, edifício, galpão) | `gerar_estrutura` | Aba Modelo › Gerar (substitui Assistentes) |
| Catálogo de perfis, materiais | `catalogo_perfis`, `perfil`, `materiais_disponiveis` | Aba Modelo › Seções e materiais + janela Catálogo |
| Validação do modelo | `validar` | Painel Avisos (sempre vivo) + Análise › Validar |
| Casos de carga, cargas nodais/distribuídas/pontuais, peso próprio | `adicionar(caso/carga_*)`, `configurar(peso_proprio)` | Aba Ações |
| Vento NBR 6123, sismo NBR 15421 | `configurar(vento/sismo)`, `gerar_acoes` | Aba Ações › Ações automáticas |
| Combinações NBR 8681 | `combinacoes`, `configurar(tipo_uso…)` | Aba Ações › Combinações |
| Análise linear, P-Δ | `calcular`, `analisar_caso`, `analisar_combinacao` | Aba Análise |
| Modal | `modal` | Aba Análise › Modal + vista animada |
| Estabilidade γz, deslocamentos-limite | `estabilidade` | Aba Análise › Estabilidade |
| Esforços, deslocamentos, reações, flechas, pavimentos | `resultados` | Aba Análise › Diagramas + Tabelas |
| Aço NBR 8800, PFF NBR 14762, concreto NBR 6118 | `verificacao`, `verificacao_barra` | Aba Dimensionamento |
| Ligações (parafuso, filete, chapa simples, ligação de tração) | `tipos_ligacao`, `verificar_ligacao` | Aba Ligações e bases |
| Normas implementadas | `normas_implementadas` | Ajuda › Normas e limitações |
| Relatório html/md/json | `gerar_relatorio` | Aba Relatório |

Funções próprias do Estructto que continuam: placa de base, chumbadores, solda pilar-placa, comparar fixações, memorial
(fase 6 de UNIFICACAO.md porta o cálculo para Python; a tela continua a mesma).

---

## 3. Arquitetura de informação proposta

### 3.1 Princípios

1. As abas seguem a ordem do trabalho do engenheiro, da esquerda para a direita: **Modelo → Ações → Análise → Dimensionamento → Ligações e bases → Relatório**. Mais **Vista** no fim, como em todo CAD.
2. Cada comando tem **um** lugar na faixa. Atalhos ficam na barra de acesso rápido, na paleta Ctrl+K e no menu de contexto, não repetidos em outras abas.
3. A aba do passo seguinte mostra um estado vazio com a próxima ação ("Calcule (F5) para ver a verificação"), em vez de botões cinzentos sem explicação (ux-designer: "Never disable a button without saying why").
4. Edição de 1 a 3 campos fica no Inspector. Tabelas largas e fluxos com desenho ficam em janela própria (árvore de decisão "Modal vs. side panel" da ux-designer).
5. Nomes: substantivo na aba e no grupo, verbo no botão (GUIA-DESIGN item 29). Sem siglas soltas: "Combinações (NBR 8681)", não "ELU/ELS".

### 3.2 Faixa (ribbon)

**Acesso rápido:** Novo · Abrir · Salvar · Desfazer · Refazer · **Calcular (F5)**. Calcular sai das abas Início e Análise e fica sempre visível.

**Arquivo (Backstage):** Novo · Abrir · Salvar · Salvar como · Recentes · Exemplos (do motor) · **Importar** (CYPE `.ed3`, Ftool `.ftl`, JSON) · **Exportar** (JSON do motor, CSV) · Preferências (unidades, pasta de trabalho) · Ajuda (inclui "Normas implementadas") · Sair.
Assistentes saem do Backstage: viram "Gerar" na aba Modelo e cartões na Tela inicial.

| Aba | Grupos (botões) | Origem | Motor |
|---|---|---|---|
| **Modelo** | **Gerar**: Pórtico plano, Edifício, Galpão, Viga contínua, Treliça · **Desenhar**: Selecionar, Nó, Barra, Medir · **Modificar**: Mover, Copiar, Espelhar, Dividir, Apagar · **Barra contínua**: Unir, Separar (Tirar trecho e Selecionar a barra vão para o menu de contexto) · **Apoios e vínculos**: Engaste, Fixo, Móvel, Livre, Rótula, Molas · **Seções e materiais**: Família, Seção, Material, Aplicar, Catálogo… · **Pavimentos**: Pavimentos…, Diafragma | Início + Estrutura (fundidas) | `gerar_estrutura`, `adicionar`, `editar`, `remover`, `catalogo_perfis` |
| **Ações** | **Casos**: Caso ativo, Casos… · **Cargas**: No nó (Fx Fy Fz Mx My Mz), Na barra (distribuída/pontual, direção global/local), Peso próprio, Limpar · **Ações automáticas**: Vento (NBR 6123)…, Sismo (NBR 15421)…, Gerar ações · **Combinações**: Uso da edificação, Combinações (NBR 8681)… | Cargas (renomeada) | `adicionar(caso, carga_*)`, `configurar`, `gerar_acoes`, `combinacoes` |
| **Análise** | **Validar**: Validar modelo (abre Avisos) · **Opções**: P-Δ, Deformação por cortante, Nº de modos · **Resultado**: Mostrar (caso / combinação / envoltória ELU/ELS) · **Diagramas**: Deformada, N, Vy, Vz, T, My, Mz, Escala ± · **Modal**: Modos (lista), Animar · **Estabilidade**: γz e deslocamentos-limite… · **Validação**: Comparar com CYPE… | Análise + metade de Resultados | `calcular`, `resultados`, `modal`, `estabilidade`, `comparar_cype` |
| **Dimensionamento** | **Normas**: Aço (NBR 8800), Perfil formado a frio (NBR 14762), Concreto (NBR 6118) — parâmetros globais (γa1, Lb padrão, cobrimento, CAA, aço da armadura) · **Verificar**: Mapa de aproveitamento, Verificação de barras…, Só reprovadas · **Otimizar**: Dimensionar (perfil mais leve), Aplicar à seleção · **Limites**: Flecha L/, Deslocamento horizontal H/ | Outra metade de Resultados + critérios da Análise | `verificacao`, `verificacao_barra`, `configurar` |
| **Ligações e bases** | **Bases**: Placa de base…, Comparar fixações…, Aplicar a vários nós · **Ligações**: Parafuso, Filete de solda, Chapa simples, Ligação de tração (cada uma abre a mesma janela "Ligação" com o tipo escolhido) · **Solda**: Eletrodo, Soldas das barras (tabela) | Placa/Comparar/Eletrodo espalhados hoje + novo | `tipos_ligacao`, `verificar_ligacao` + placa do Estructto |
| **Relatório** | **Memorial**: Resumo, Completo, Escolher capítulos… · **Exportar**: PDF, HTML, Markdown, JSON, CSV · **Tabelas**: Abrir tabelas | Relatório que estava em Resultados | `gerar_relatorio` |
| **Vista** | **Navegar**: Enquadrar, Aproximar, Afastar · **Projeção**: 3D, Planta, Elevação X, Elevação Y, Pavimento… · **Mostrar**: Nomes, Cargas, Reações, Valores, Eixos locais, Seções extrudadas · **Grade** · **Painéis**: Projeto, Propriedades, Tabelas, Avisos · **Ajuda** | Vista + Mostrar de Resultados | `geometria` |

Abas contextuais (continuam, são boas):
- **Barra selecionada**: Verificar barra, Dimensionar, Rótulas (início/fim/ambas), Eixo local / giro da seção, Ligação na ponta…, Apagar.
- **Nó selecionado**: Apoio, Molas, Carga no nó, Placa de base, Apagar.
- **Nova — Pavimento selecionado**: Diafragma, Mostrar só este pavimento, Forças de vento/sismo do pavimento.

### 3.3 Painéis

| Painel | Mudança | Por quê |
|---|---|---|
| Gerenciador do projeto | Grupos novos: Pavimentos, Casos de carga, Combinações, Ligações, Bases. Contagens e selo de status (✓ / ⚠ / ✕) por grupo após o cálculo | A árvore vira o índice do modelo inteiro, não só de nós e barras |
| Propriedades (Inspector) | Campos 3D (Z, 6 apoios, giro da seção); categoria "Norma" por barra (Lb, K, Cb, tipo aço/PFF/concreto); categoria "Armadura" para concreto | Os parâmetros por barra do motor (`params`) precisam de um lugar |
| Tabelas de resultados | Abas: Nós · Barras · Verificação · **Pavimentos** (deslocamentos, drift, forças de vento/sismo) · **Modal** · Soldas. Seletor de caso/combinação sincronizado com "Mostrar" | Resultados novos do motor (`resultados(alvo=pavimentos)`, `modal`) |
| Avisos | Ligado ao `validar` ao vivo; cada aviso com "Ir para" (seleciona o objeto) | Hoje o selo de avisos existe, mas sem ação |
| HUD da vista | Segmentado: Deformada · N · V · M · T · % · **Modo** (com nº do modo e frequência) | Modal e 3D |
| Linha de comando, Paleta Ctrl+K | Mantêm. Registrar os comandos novos (VENTO, SISMO, MODAL, GAMAZ, CYPE) | Registro único já existe (`Reg`) |

### 3.4 Janelas e diálogos

| Janela | Ação | Motivo |
|---|---|---|
| Tela inicial | **Manter**; cartões Galpão/Edifício/Pórtico/Viga/Treliça + "Exemplos" + "Importar do CYPE" | É a primeira impressão e já está boa |
| Assistente de estruturas | **Renomear** para "Gerar estrutura"; incluir Edifício (pavimentos, vãos X/Y) e Galpão do motor; prévia 3D | `gerar_estrutura` cobre edifício/galpão 3D |
| Casos de carga e combinações | **Dividir** em "Casos de carga" (lista simples: nome, tipo G/Q/W/E, subtipo) e "Combinações (NBR 8681)" (lista gerada, só leitura, com fatores γ·ψ por caso; botão Editar manual para exceções) | A matriz editável não escala para dezenas de combinações |
| **Nova** — Vento (NBR 6123) | V0, S1, S2 (categoria, classe), S3, Ca por direção; prévia das forças por pavimento | `config.vento`, `gerar_acoes` |
| **Nova** — Sismo (NBR 15421) | Zona, classe do terreno, categoria de utilização, R, Cd; espectro desenhado; forças por pavimento | `config.sismo` |
| **Nova** — Pavimentos | Tabela nome/cota/diafragma; detectar pela geometria | `pavimento` |
| **Nova** — Análise modal | Lista de modos (T, f, participação X/Y/Z); clicar anima o modo na vista | `modal` |
| **Nova** — Estabilidade | γz por direção com limites 1,1/1,3, deslocamentos-limite por pavimento | `estabilidade` |
| Verificação da barra | **Refazer sobre o motor**: veredito + barras de capacidade por critério da norma (item da norma ao lado), combinação determinante, "por combinação"; aba "Trocar perfil" mantém a tabela da família | Hoje é verificação simplificada; o motor dá `detalhes` e `por_combinacao` |
| **Nova** — Verificação de barras (lista) | Toda a estrutura, ordenada pela taxa, filtros por norma/tipo/reprovadas | `verificacao(filtro)`; hoje só existe dentro do painel Tabelas |
| Placa de base | **Manter** (melhor tela do produto); trocar a fonte dos esforços pela envoltória do motor e escolher combinação determinante | Fase 6 porta o cálculo |
| Comparar fixações | **Manter**; abrir só a partir da Placa e de Ligações e bases | |
| **Nova** — Ligação | Mesmo esqueleto da Placa: veredito + 2 a 3 passos + desenho + memória. Tipo escolhido no segmentado (Parafuso, Filete, Chapa simples, Tração) | `verificar_ligacao` |
| **Nova** — Comparar com CYPE | Escolher `.ed3`; tabela hipótese × grandeza × CYPE × Estructto × dif %; taxas | `comparar_cype` |
| **Nova** — Catálogo de perfis | Busca, famílias W/HP/Ue/U/tubos, propriedades, miniatura | `catalogo_perfis`, `perfil`; hoje só popup |
| Relatório | **Manter a janela**, conteúdo vem de `gerar_relatorio` (html) + capítulos do memorial da placa/ligações; escolher capítulos | Um relatório só |
| Ajuda | **Manter**; nova seção "Normas implementadas e limitações" | `normas_implementadas` |
| Caixa, Pergunta, Histórico | Manter | |

### 3.5 O que fundir, renomear, remover e criar

**Fundir**
- Início + Estrutura → **Modelo**. Hoje Início só tem desenhar/modificar e Estrutura só vínculos/seções: as duas modelam.
- Zoom de Início e de Vista → só em **Vista** (e roda do mouse, F4).
- Calcular de Início e de Análise → só na **barra de acesso rápido** (F5).
- Placa de base em 3 lugares → **Ligações e bases** + aba contextual Nó.
- Tabelas e Exportar CSV em 2 lugares cada → **Relatório** (exportar) e **Vista › Painéis** (mostrar).
- "Mostrar" (Reações/Valores/Cargas/Nomes) de Resultados → **Vista › Mostrar**.

**Renomear**
- Cargas → **Ações** (vento e sismo não são "cargas" lançadas à mão; a NBR 8681 fala em ações).
- Resultados → dividida em **Análise** (esforços) e **Dimensionamento** (normas).
- Assistentes → **Gerar** / "Gerar estrutura".
- "Verificação" (botão de pintar barras) → **Mapa de aproveitamento**. Hoje existem "Verificação" e "Mapa de calor" lado a lado fazendo quase o mesmo.
- "Comparar" (aba Resultados) → **Verificar barra**. "Comparar" fica reservado para "Comparar fixações" e "Comparar com CYPE".
- "Gerenciador do projeto" → **Projeto**; "Tabelas de resultados" → **Tabelas** (títulos de painel em caixa alta longos ocupam a faixa).
- "Móvel em X" → **Móvel horizontal** (com 3D, "X" é ambíguo).

**Remover**
- **Coeficiente de segurança n** global da aba Análise e o "n" da Verificação da barra: substituídos por γ/ψ (NBR 8681) e γa1/γc/γs nos parâmetros de norma. O "n" do chumbador na Placa **fica** (é critério do fabricante/ensaio, não de norma de barras).
- Botão "Verificação" (modo 3 cores) — o Mapa de aproveitamento (4 faixas) já cobre.
- "Barra contínua › Tirar trecho" e "Selecionar a barra" da faixa → menu de contexto (uso raro).
- Exportar CSV do Backstage → fica só em Relatório › Exportar.

**Criar**
- Abas **Ações**, **Dimensionamento**, **Ligações e bases**, **Relatório**; janelas Vento, Sismo, Pavimentos, Modal, Estabilidade, Ligação, Comparar com CYPE, Catálogo, Verificação de barras (lista).
- Indicador de etapa na faixa: um ponto de status em cada aba (Modelo ✓, Ações ✓, Análise ⚠ desatualizada, Dimensionamento ✕ 2 reprovadas). Mostra "onde estou" e "o que falta" (ux-designer: "current location is always visible").
- Estado "resultado desatualizado": editar o modelo descarta o cálculo no motor (ESPEC). A vista e as tabelas mostram faixa "Modelo alterado — Calcular (F5)" em vez de resultados velhos.
- Projeções 3D/Planta/Elevação na Vista (fase 4).

### 3.6 Fluxo resumido

```
Tela inicial ─► Modelo (gerar/desenhar, seções, apoios, pavimentos)
            ─► Ações (casos, cargas, vento, sismo, combinações)
            ─► F5 Calcular ─► Análise (diagramas, modal, γz, CYPE)
            ─► Dimensionamento (mapa, lista, verificar barra, otimizar)
            ─► Ligações e bases (placa, chumbadores, solda, ligações)
            ─► Relatório (memorial, exportar)
```

---

## 4. Oportunidades de animação (fase 8 — polimento)

Regra do GUIA-DESIGN: `MOTION_INTENSITY 2/10`, 120–160 ms `CubicEase EaseOut`, só `Opacity`/`RenderTransform`/cor; nada de animar abertura de aba ou entrada de cartões. A ux-designer aceita 150–300 ms e até 500 ms para movimento espacial. Proposta: 120–160 ms para cromo, 250–400 ms só para câmera e diagramas (movimento espacial), e respeitar `SystemParameters.ClientAreaAnimation` (equivalente a reduced-motion no Windows).

| Tela / elemento | Animação | Duração | Observação |
|---|---|---|---|
| Faixa — indicador de aba | Sublinhado de 2 px desliza até a aba nova (TranslateX) | 140 ms | Conteúdo da aba troca sem animação |
| Faixa — ponto de status por aba | Cor muda com transição ao calcular | 160 ms | |
| Abas contextuais Barra/Nó | Fade do cabeçalho colorido ao selecionar | 120 ms | |
| Vista 3D — câmera | Enquadrar, trocar projeção (3D ↔ Planta ↔ Elevação), "Ir para objeto" com interpolação de câmera | 300–400 ms | Maior ganho de percepção; nunca no pan/zoom do mouse |
| Vista — seleção | Halo da barra selecionada aparece com fade + leve espessura | 120 ms | |
| Vista — hover | Realce da barra sob o mouse e cartão de inspeção com fade | 100 ms | Dentro do limite de 100 ms de resposta |
| Vista — diagramas N/V/M/T | Diagrama "cresce" do eixo da barra (escala 0 → 1 perpendicular) ao trocar de esforço | 250 ms | Ao trocar caso/combinação, interpolar valores em vez de redesenhar |
| Vista — deformada | Interpolar da posição indeformada até a deformada | 300 ms | |
| Vista — modo de vibração | Loop harmônico contínuo a 60 fps, botão Pausar | contínuo | Já previsto no GUIA |
| Vista — mapa de aproveitamento | Cores das barras transicionam ao recalcular | 200 ms | Mostra o que mudou após Dimensionar |
| Vista — vento/sismo | Setas de força por pavimento entram em sequência de baixo para cima | 40 ms por pavimento | Só ao abrir a janela Vento/Sismo |
| HUD | Valor máx./mín. conta até o número novo | 160 ms | |
| Painéis encaixados | Mostrar/ocultar com fade | 120 ms | Sem slide (custo de layout) |
| Inspector | Barra de aproveitamento do cabeçalho cresce até o valor; troca de objeto com crossfade | 160 ms | |
| Árvore do projeto | Selo de status por grupo faz fade ao terminar o cálculo | 160 ms | |
| Tabelas | Barras de capacidade crescem da esquerda; linha alterada pisca o fundo `Accent.Subtle` uma vez | 160 ms / 400 ms | Só nas linhas visíveis (virtualização) |
| Barra de status | Progresso do cálculo (motor Python pode levar segundos): barra fina indeterminada sob a faixa | contínuo | Hoje não há feedback de cálculo longo |
| Toast | Entra com fade + TranslateY 8 px; sai com fade | 160 ms | Avisos com ação não somem sozinhos |
| Placa de base | Barras ①②③ crescem; chips do topo trocam de cor; 3D gira até a vista padrão ao abrir a aba 3D; cotas editadas piscam | 160–300 ms | |
| Placa — Sugerir / Aplicar em… | Popup com fade + escala 0,98 → 1 | 120 ms | |
| Verificação da barra | Barras por critério crescem; ao escolher outro perfil, valores interpolam | 160 ms | |
| Combinações | Ao gerar, linhas novas entram com fade escalonado | 20 ms por linha, máx. 300 ms | |
| Modal (janela) | Clicar num modo anima na vista sem fechar a janela | contínuo | |
| Estabilidade | Barra de γz cresce até o valor com marcas 1,1 e 1,3 | 200 ms | |
| Comparar com CYPE | Coluna dif % colore ao chegar o resultado | 160 ms | |
| Relatório | Nenhuma | — | Documento: sem movimento |
| Tela inicial | Hover dos cartões: borda `Accent` + miniatura com escala 1,02 | 120 ms | Sem entrada animada dos cartões |
| Paleta Ctrl+K | Fade + escala 0,98 → 1 ao abrir | 120 ms | |
| Backstage | Nenhuma além da padrão do Fluent | — | |

Implementação: um único helper `Anim.cs` (Fade, Crescer, Interpolar) com `CubicEase`, chamado pelas telas. Nada de biblioteca nova.

---

## 5. Ordem sugerida

1. Faixa nova (renomear/fundir abas) sobre as funções atuais, sem motor — muda só `MontarFaixa` e registros da paleta.
2. Remover "n" global junto com a entrada da verificação do motor (fase 5).
3. Janelas novas na ordem do fluxo: Casos → Combinações → Vento → Sismo → Pavimentos → Verificação → Modal → Estabilidade → Ligação → Comparar com CYPE.
4. Animações por último (fase 8), com capturas em `Capturas.cs` para cada janela nova.
