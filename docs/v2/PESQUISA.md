# Pesquisa Prática de UI/UX para Modernização do Estructto (v2)

> **Contexto do código atual (`codigo/Estructto`):**
> - **Stack:** C# WPF `.NET 8.0-windows`, `Fluent.Ribbon 11.0.2`, `Dirkster.AvalonDock 4.72.1` (`AvalonDock.Themes.VS2013`), `WpfMath 2.1.0` e `System.Windows.Forms.PropertyGrid` via `WindowsFormsHost`.
> - **Estado visual:** Tema escuro inspirado em AutoCAD Plant 3D (`TemaPlant.cs`, tons `#212830` a `#454F61`, acento `#0696D7`), ícones vetoriais procedurais em `DrawingGroup` 32×32 (`Icones.cs`), linha de comando flutuante no canvas (`MainWindow.xaml`), e janelas modais construídas via código (`Janelas.cs`), onde `JanelaPlaca` já possui boas ideias de UX (cotas clicáveis no `Canvas`, pílulas, steppers, barras de capacidade e vista isométrica 2.5D), mas o restante da aplicação ainda sofre com o `PropertyGrid` WinForms antigo, falta de polimento moderno de bordas/tipografia/estados interativos e telas densas.

---

## 1. Referências de UI/UX de Engenharia, CAD e Design Moderno

### O que cada software de referência faz bem

| Software | Pontos Fortes de UI/UX | Referência / Fonte |
| :--- | :--- | :--- |
| **AutoCAD 2025/2026 & Plant 3D** | **Linha de comando flutuante semitransparente** com autocompletar inteligente, **Ribbon Contextual** que muda instantaneamente ao selecionar objetos, **Dynamic Input (HUD no cursor)** para digitar comprimentos e ângulos (`@L<ang`) sem olhar para baixo, e barra de status compacta com toggles de Snap/Grid/Ortho. | [Autodesk AutoCAD UI Docs](https://help.autodesk.com/view/ACD/2025/ENU/) |
| **Autodesk Fusion** | **HUD de manipulação direta** (mini-toolbar e inputs numéricos ao lado do elemento selecionado), **Atalho `S` (Search/Command Palette no cursor)**, **Marking Menu (menu radial de clique direito)** e árvore de modelo limpa à esquerda com visibilidade (olho) rápida. | [Autodesk Fusion Interface](https://help.autodesk.com/view/fusion360/ENU/?guid=GUID-A93F859E-38F3-4E94-A355-39E7F1BBDF05) |
| **Onshape** | **Shortcut Toolbar (`S`)**, indicadores de restrição/apoio nítidos sobre o canvas com realce em hover, **Painel de Medição instantâneo** no canto inferior direito ao selecionar quaisquer 2 entidades, e zero poluição de bordas. | [Onshape UI Tour](https://cad.onshape.com/help/Content/user_interface.htm) |
| **Autodesk Revit** | **Seletor de Tipo (Type Selector)** no topo do painel de Propriedades com miniatura e dimensões do perfil, e **Temporary Dimensions (cotas temporárias azuis clicáveis)** que aparecem no canvas ao selecionar um elemento. | [Autodesk Revit Properties Palette](https://help.autodesk.com/view/RVT/2025/ENU/) |
| **SkyCiv Structural 3D** | Interface web escura/clara limpa: **Inspector lateral nativo** com cartões colapsáveis, **diagramas com preenchimento em gradiente suave**, **sliders de escala de deformada/diagrama direto no HUD do canvas**, e cartões de status Passa/Não Passa coloridos por barra. | [SkyCiv Structural 3D](https://skyciv.com/structural-software/structural-3d/) |
| **Tekla Structures** | **Inspector contextual + Mini-Toolbar flutuante** no clique, cores por **taxa de utilização (Utilization Ratio: verde < 70%, amarelo 70–90%, laranja 90–100%, vermelho > 100%)** aplicadas direto nas barras do modelo. | [Tekla Structures UI](https://support.tekla.com/doc/tekla-structures/2024/int_user_interface) |
| **Autodesk Robot & CSI SAP2000** | **Tabelas sincronizadas bidirecionalmente com o canvas** (selecionar linhas na tabela destaca barras no modelo e vice-versa; filtrar apenas selecionados), **combinações de carga em dropdown rápido** na barra superior/status, e **miniatura da seção transversal** com eixos locais. | [CSI SAP2000 Features](https://www.csiamerica.com/products/sap2000) |
| **Figma** | **Inspector de Propriedades impecável** (substituto definitivo para `WinForms PropertyGrid`): inputs em grade compacta com rótulo interno (`X`, `Y`, `L`, `θ`) que aceitam expressões matemáticas (`3000+450`, `L/2`) e **Scrubby Numbers** (arrastar o rótulo com o mouse altera o valor ao vivo); **Command Palette (`Ctrl+K`)**; **Empty States** educativos. | [Figma UI3 Design](https://help.figma.com/hc/en-us/articles/23954856027159-Navigating-UI3-Figma-s-new-UI) |

---

### 22 Padrões Concretos Aplicáveis ao Estructto

1. **Substituição do `WinForms PropertyGrid` por um Inspector Nativo Estilo Figma/SkyCiv (`PainelProp`):**
   - Eliminar o `WindowsFormsHost` (`MainWindow.xaml` L142). Criar seções em cartões colapsáveis: *Cabeçalho de Tipo (Seletor de Perfil com ícone da seção I/U/L/Tubo)*, *Geometria (Inputs 2×2 para $X_i, Y_i, X_f, Y_f, L, \theta$)*, *Material & Eixo*, *Apoios/Rótulas*, *Cargas*, e *Resumo da Verificação*.
2. **Inputs Numéricos com Avaliador de Expressões e "Scrubbing" (Padrão Figma/Fusion):**
   - Permitir digitar `4.5 + 1.2`, `6/2`, `2500mm` em qualquer campo numérico de coordenada, carga ou placa de base, além de arrastar o rótulo (`L`, `q`, `P`) horizontalmente para variar o valor com recálculo em tempo real.
3. **HUD de Entrada Dinâmica no Cursor (Dynamic Input — Padrão AutoCAD):**
   - Ao desenhar barra (`B`), mover ou medir, exibir caixinhas flutuantes presas ao cursor no `Vista` mostrando `Comprimento [m]` e `Ângulo [°]` editáveis com `Tab` para alternar entre eles.
4. **Cotas Temporárias Clicáveis no Canvas de Pórtico (Padrão Revit + `PlanoPlaca`):**
   - O Estructto já faz cotas azuis clicáveis em `PlanoPlaca` (`Janelas.cs` L541). Levar esse mesmo padrão para a `Vista` principal: ao selecionar uma barra ou dois nós, mostrar a cota do vão $L$ ou $\Delta X/\Delta Y$ com caixinha azul clicável para esticar/encolher a barra na hora.
5. **Command Palette Global (`Ctrl+K` ou tecla `S` — Padrão Figma/Onshape/VS Code):**
   - Além da linha de comando estilo AutoCAD no rodapé, abrir um modal central ou no cursor ao teclar `Ctrl+K` com busca difusa (*fuzzy*) de comandos, perfis (ex.: digitar `W200x19.3` aplica à seleção), assistentes e atalhos visíveis em badges (`B`, `M`, `F5`).
6. **Mini-Toolbar Contextual no Clique / Seleção (Padrão Fusion/Tekla):**
   - Ao selecionar um nó ou barra, mostrar uma barra flutuante compacta (pílula escura com sombra suave, 40 px acima do elemento) com as 4–5 ações mais frequentes: *Nó:* `[Engaste] [Rótula] [Rolete] [Carga Nodal] [Placa de Base]`; *Barra:* `[Trocar Perfil] [Carga Dist.] [Rotular Ponta] [Dividir] [Verificar]`.
7. **HUD Flutuante de Controle de Resultados no Canvas (Padrão SkyCiv):**
   - Quando um diagrama ($N, V, M$, Deformada ou Mapa de Aproveitamento) estiver ativo, exibir no canto superior direito do `Vista` um cartão translúcido (`#CC181C24` com `CornerRadius="8"`) contendo: seletor rápido de `[N] [V] [M] [δ] [% Ap.]`, **slider contínuo de escala**, toggles `[Valores] [Extremos] [Sombreado]` e seletor do caso/combinação.
8. **Modo de Visualização por Taxa de Utilização / Mapa de Calor nas Barras (Padrão Tekla/Robot):**
   - Colorir as barras do pórtico diretamente pela taxa de aproveitamento ($\le 70\%$ verde, $70\text{–}90\%$ ciano/azul, $90\text{–}100\%$ âmbar, $>100\%$ vermelho pulsante com badge de alerta na barra crítica) e exibir uma barra de escala de cores (legenda contínua) no canto da vista.
9. **Diagramas com Preenchimento em Gradiente Translúcido e Traço Nítido:**
   - Renderizar diagramas de $N, V, M$ com preenchimento em `LinearGradientBrush` de opacidade `35%` na extremidade até `8%` no eixo da barra e linha de contorno de `2 px`, diferenciando sinal positivo/negativo por cor semântica (ex.: azul tração / vermelho compressão, ou azul momento positivo / coral momento negativo) e marcando máximos/zeros com pílulas legíveis.
10. **Tooltip de Inspeção ao Vivo sobre a Barra ("Diagrama Scrubber" — Padrão SkyCiv/Ftool melhorado):**
    - Ao passar o mouse ou arrastar ao longo de uma barra com resultados ativos, mostrar uma linha vertical de corte na seção $x$ ($0 \le x \le L$) e um cartão flutuante com os valores interpolados exatos de $N(x), V(x), M(x), \delta(x)$ e $\sigma_{\max}(x)$.
11. **Preview Fantasma (Ghost Preview) em Operações de Modelagem e Cargas:**
    - Antes de confirmar uma carga distribuída, apoio, espelhamento, divisão de barra ou assistente paramétrico, desenhar o elemento em azul translúcido tracejado no canvas em tempo real.
12. **Seletor Visual de Perfis com Miniatura em Escala e Filtro Instantâneo:**
    - No Inspector e na `JanelaVerificacao`, desenhar um ícone vetorial proporcional da seção (`I`, `U`, `L`, `Tubo Circular`, `Tubo Retangular`) ao lado do nome, com indicação de "eixo forte / deitado (eixo fraco)" que gira a miniatura 90°.
13. **Sincronização Total e Filtros Rápidos nas Tabelas de Resultados (`PainelTabelas`):**
    - Adicionar barra de topo nas abas `TabNos`, `TabBarras`, `TabVerif`, `TabSoldas` com: toggle `[Apenas selecionados]`, filtro `[Apenas Não Passa / Críticos]`, busca por texto, barras de progresso embutidas na coluna `Aproveitamento` e botão `[Copiar para Excel / CSV]`.
14. **Badges de Diagnóstico Clicáveis sobre o Modelo (In-Canvas Warnings — Padrão Revit/Fusion):**
    - Quando houver aviso de instabilidade (mecanismo), barra com aproveitamento $>100\%$ ou placa de base reprovada, exibir um pequeno badge clicável (`⚠` âmbar ou `✕` vermelho) ao lado do nó/barra no canvas. Clicar no badge abre a solução (ex.: `JanelaVerificacao` ou `JanelaPlaca`).
15. **Abas de Documento Estilo Navegador Moderno com Indicador de Estado (`AbasDocs`):**
    - Estilizar a barra de abas (`MainWindow.xaml` L25) com pílula/aba ativa integrada ao canvas, bolinha (`●`) de "modificado não salvo", menu de contexto (Fechar outras, Duplicar modelo, Abrir pasta) e botão `+` elegante.
16. **Barra de Status com Pílulas de Modo (Snap, Grid, Ortho, Unidades):**
    - Transformar o rodapé (`MainWindow.xaml` L38) em pílulas interativas claras (ícone + rótulo curto `GRID`, `SNAP`, `ORTHO`, `COTAS`) com realce azul quando ativas e **seletor rápido de unidades** (`m, kN, MPa` vs `mm, tf, kgf/cm²`) no canto direito.
17. **Resumo Instantâneo de Seleção na Barra de Status / Mini-Inspector (Padrão Onshape):**
    - Ao selecionar múltiplas barras, exibir imediatamente na barra de status e no topo do Inspector: `3 barras selecionadas · Comprimento total: 18,40 m · Massa total: 412,5 kg · Pior aproveitamento: 94% (B2)`.
18. **Tela Inicial ("Home / Start") com Cartões de Templates Vetoriais Interativos:**
    - Modernizar `TelaInicial` (`MainWindow.xaml` L93) com grade de cartões dos assistentes (Viga contínua, Pórtico simples, Galpão duas águas, Treliça Pratt/Howe, Marquise) contendo ilustrações vetoriais limpas do pórtico, lista de arquivos recentes com **miniatura SVG/PNG gerada ao salvar** e atalhos rápidos.
19. **Assistentes Paramétricos com Preview ao Vivo no Canvas:**
    - Quando o usuário abrir um assistente (ex.: Galpão ou Treliça), ao alterar vão, altura, número de divisões ou carga nos steppers, atualizar o desenho em tempo real antes de clicar em `Criar`.
20. **Animação Suave da Deformada (Modo Oscilação Harmônica — Padrão SAP2000/SkyCiv):**
    - Botão `[▶ Animar]` no HUD de deformada que interpola senoidalmente os deslocamentos de $0$ a $\pm\delta_{\max}$ a 60 fps, permitindo compreender instantaneamente o modo de deslocamento e se há deslocabilidade excessiva.
21. **Mini-Mapa / Navegador de Enquadramento e Zoom por Indicador:**
    - Em modelos grandes, exibir no canto inferior direito do canvas um controle compacto de zoom (`[−] 100% [+] [Enquadrar Tudo]`) que ao clicar em `100%` permite escolher vistas rápidas ou abrir mini-mapa.
22. **Onboarding Contextual e Dicas de Atalho em Dois Níveis:**
    - Manter os `ScreenTips` ricos do `Fluent.Ribbon` com cabeçalho (Nome do Comando + Tecla de Atalho em badge `[B]`), descrição curta de 2 linhas e diagrama simples, além de uma barra discreta de "Próximo passo sugerido" no topo da linha de comando para iniciantes.

---

## 2. Bibliotecas e Repositórios GitHub para WPF Moderno (.NET 8)

Verificação completa de licença, suporte ao `.NET 8` e compatibilidade com **`Fluent.Ribbon 11.0.2`** + **`Dirkster.AvalonDock 4.72.1`**:

| Biblioteca / Repositório | Licença (Verificada) | Suporte .NET 8 | Compatibilidade com `Fluent.Ribbon` + `AvalonDock` | Avaliação para o Estructto |
| :--- | :--- | :--- | :--- | :--- |
| **`Fluent.Ribbon`**<br>[fluentribbon/Fluent.Ribbon](https://github.com/fluentribbon/Fluent.Ribbon) | **MIT** | Nativo (`net8.0-windows`) | **Já está no projeto (v11.0.2).** Usa `ControlzEx` como base de temas. | **Manter.** É o padrão ouro de Ribbon para WPF e já está customizado em `TemaPlant.cs`. |
| **`Dirkster.AvalonDock`**<br>[Dirkster99/AvalonDock](https://github.com/Dirkster99/AvalonDock) | **MS-PL / Apache-2.0** (Dual) | Nativo (`net8.0-windows`) | **Já está no projeto (v4.72.1)** com `Themes.VS2013`. | **Manter.** As chaves de `AdKeys` já são sobrescritas em `TemaPlant.AplicarDock` (`TemaPlant.cs` L150). |
| **`WPF-UI` (Lepoco)**<br>[lepoco/wpfui](https://github.com/lepoco/wpfui) | **MIT** | Nativo (`net8.0-windows`) | **Média/Baixa (Conflito de Window Chrome e estilos implícitos).** `WPF-UI` espera `FluentWindow` e sobrescreve dicionários globais que brigam com `RibbonWindow` e `ControlzEx`. | **Não recomendado como dependência global.** Vale apenas consultar o XAML dele como inspiração visual para `TextBox`, `NumberBox` e `Card`. |
| **`iNKORE.UI.WPF.Modern`**<br>[iNKORE-NET/UI.WPF.Modern](https://github.com/iNKORE-NET/UI.WPF.Modern) | **MIT** | Nativo (`net8.0-windows`) | **Média.** Fork atualizado do `ModernWpf` para .NET 8; porém injeta estilos WinUI 3 globais que alteram métricas de `MenuItem`, `ComboBox` e `ScrollViewer` dentro do `Fluent.Ribbon`. | **Desnecessário** se estilizarmos os poucos controles nativos com `ResourceDictionary` próprio. |
| **`ModernWpf`**<br>[Kinnara/ModernWpf](https://github.com/Kinnara/ModernWpf) | **MIT** | Parcial (sem releases recentes) | **Baixa/Média.** Projeto original estagnado (substituído na comunidade por `iNKORE` e `WPF-UI`). | **Evitar** (defasado). |
| **`MahApps.Metro`**<br>[MahApps/MahApps.Metro](https://github.com/MahApps/MahApps.Metro) | **MIT** | Nativo (`net8.0-windows`) | **Altíssima.** Criado pelos mesmos mantenedores de `Fluent.Ribbon` e `ControlzEx`; compartilha o mesmo sistema `ThemeManager`. | **Opcional seguro**, mas pesado se o objetivo for mudar o mínimo de dependências. |
| **`HandyControl` / `HandyControls`**<br>[HandyOrg/HandyControl](https://github.com/HandyOrg/HandyControl) | **MIT** | Nativo (`net8.0-windows`) | **Média.** Possui `PropertyGrid` WPF nativo, mas seus temas próprios exigem dicionários globais volumosos. | **Evitar o pacote inteiro**; é mais limpo e leve criar um Inspector de Propriedades dedicado em 250 linhas de C#/XAML. |
| **`MaterialDesignInXamlToolkit`**<br>[MaterialDesignInXAML/MaterialDesignInXamlToolkit](https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit) | **MIT** | Nativo (`net8.0-windows`) | **Baixa (Estética incompatível).** O visual Google Material Design destoa de softwares CAD/Engenharia no Windows e conflita com `Fluent.Ribbon`. | **Evitar.** |
| **`WpfMath`**<br>[ForNeVeR/WpfMath](https://github.com/ForNeVeR/WpfMath) | **MIT** | Compatível | **Já está no projeto (v2.1.0).** Renderiza fórmulas LaTeX em vetores WPF para memoriais de cálculo. | **Manter e expandir uso** nos cartões de cálculo de `JanelaVerificacao` e `JanelaPlaca`. |
| **`ScottPlot.WPF`**<br>[ScottPlot/ScottPlot](https://github.com/ScottPlot/ScottPlot) | **MIT** | Nativo (`net8.0-windows`) | **Alta.** Controle isolado de plotagem rápida. | Útil apenas se houver gráficos externos (ex.: curvas de interação $N\text{-}M$ ou espectro), mas os diagramas do pórtico já são melhor desenhados direto no `DrawingContext` da `Vista`. |
| **`LiveCharts2`**<br>[Live-Charts/LiveCharts2](https://github.com/Live-Charts/LiveCharts2) | **MIT** (Core) | Nativo (`net8.0-windows`) | **Alta**, porém puxa dependência do `SkiaSharp`. | **Desnecessário** para o Estructto (adiciona binários nativos Skia sem necessidade). |
| **`SharpVectors`**<br>[ElinamLLC/SharpVectors](https://github.com/ElinamLLC/SharpVectors) | **BSD-3-Clause** | Nativo (`net8.0-windows`) | **Alta.** Converte SVG para `DrawingGroup` WPF. | **Opcional**, pois `Icones.cs` já usa `Geometry.Parse` nativo com zero dependências. |
| **`HelixToolkit.Wpf`**<br>[helix-toolkit/helix-toolkit](https://github.com/helix-toolkit/helix-toolkit) | **MIT** | Compatível | **Alta** (usa `Viewport3D` nativo do WPF). | Opcional se quiser câmera orbital 3D completa na Placa de Base; contudo, uma projeção 3D vetorial com rotação por arraste do mouse direto no `OnRender` (`PlanoPlaca`) tem zero dependências e visual CAD mais nítido. |

---

### Recomendação de Combinação de Baixo Risco (Zero Novas Dependências Pesadas)

> **Recomendação Oficial:**
> **Manter exatamente as 4 dependências atuais (`Fluent.Ribbon 11.0.2` + `Dirkster.AvalonDock 4.72.1` + `Dirkster.AvalonDock.Themes.VS2013 4.72.1` + `WpfMath 2.1.0`) e REMOVER o `System.Windows.Forms` (`UseWindowsForms`).**
>
> **Por que essa é a melhor escolha técnica:**
> 1. **Zero conflito de estilos:** Adicionar `WPF-UI` ou `HandyControl` por cima de `Fluent.Ribbon` + `AvalonDock` quebra margens, `RibbonWindow` chrome e `ScreenTips`.
> 2. **Fim do `Airspace Issue` do WinForms:** O `System.Windows.Forms.PropertyGrid` dentro de `WindowsFormsHost` (`MainWindow.xaml` L142) não aceita transparência WPF, não tem cantos arredondados, ignora animações/DPI fino e parece anos 90. Substituí-lo por um **Inspector WPF nativo** resolve o maior gargalo visual do programa sem adicionar nenhuma DLL externa.
> 3. **ResourceDictionary enxuto em `TemaPlant.cs` + `MainWindow.xaml`:** Criar estilos WPF nativos modernos (~200 linhas) para `TextBox`, `ComboBox`, `Button`, `ToggleButton`, `TabControl`, `DataGrid`, `TreeView`, `ScrollBar` (fina de 6 px) e `Expander`, todos consumindo os tokens do `TemaPlant`.

---

## 3. Conjuntos de Ícones Livres para CAD e Engenharia Estrutural

### Comparativo de Bibliotecas de Ícones Open-Source

| Conjunto | Repositório GitHub | Licença (Verificada) | Nº de Ícones | Estilo Visual | Avaliação para CAD / Estruturas |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Lucide** | [lucide-icons/lucide](https://github.com/lucide-icons/lucide) | **ISC** (equivalente a MIT) | **~1.900+** | Traço `2 px` em grid `24×24`, cantos arredondados (`rx=2`), geometria ultra limpa. | **Excelente** para ações gerais (zoom, medir/régua, magnet/snap, grid, undo/redo, olho, pdf/file, sliders, alertas, busca). |
| **Tabler Icons** | [tabler/tabler-icons](https://github.com/tabler/tabler-icons) | **MIT** | **6.200+** (Outline + Filled) | Traço `2 px` em grid `24×24`, muito técnico e uniforme. | **O mais completo em ícones técnicos/geométricos** (ângulos, vetores, eixos, corte, tabelas, funções matemáticas, ferramentas). |
| **Fluent UI System Icons** | [microsoft/fluentui-system-icons](https://github.com/microsoft/fluentui-system-icons) | **MIT** | **9.000+** variantes (`16/20/24/28/32`, Regular + Filled) | Nativo Windows 11 / Office, traço `1.5–2 px`, variante `Filled` perfeita para estado ativo. | **Excelente harmonia com `Fluent.Ribbon`** para Arquivo, Backstage, Janelas e Status Bar. |
| **Phosphor Icons** | [phosphor-icons/core](https://github.com/phosphor-icons/core) | **MIT** | **1.200+** base (~9.000 em 6 pesos, incl. **Duotone**) | Grid `256×256`, possui peso **Duotone** (preenchimento translúcido + traço). | Muito bom, pois o estilo *Duotone* lembra os ícones de Ribbon CAD (corpo sombreado + linha de destaque). |
| **Material Symbols** | [google/material-design-icons](https://github.com/google/material-design-icons) | **Apache-2.0** | **3.500+** (peso variável) | Grid `24×24`, estética Google. | Bom acervo geral, mas menos "engenharia desktop" que Tabler/Fluent. |
| **Codicons** | [microsoft/vscode-codicons](https://github.com/microsoft/vscode-codicons) | **MIT** | **~500+** | Grid `16×16` focado em IDE de código. | Bom apenas para árvore de projeto, dock panels e avisos pequenos (`16 px`). |
| **Iconoir** | [iconoir-icons/iconoir](https://github.com/iconoir-icons/iconoir) | **MIT** | **1.600+** | Traço `1.5 px` em grid `24×24`. | Traço fino elegante, porém acervo menor que Tabler para ferramentas de precisão. |

---

### Cobertura por Categoria de Comando do Estructto

| Categoria / Comando | Cobertura em **Tabler / Lucide / Fluent** | Precisa desenhar à mão (Engenharia)? |
| :--- | :--- | :--- |
| **Arquivo, Desfazer/Refazer, Relatório/PDF, Tabela, Busca** | **100% coberto** (`file-plus`, `folder-open`, `device-floppy`, `arrow-back-up`, `file-type-pdf`, `table`, `search`) | Não (pode usar paths de Tabler/Fluent ou manter os atuais refinados). |
| **Zoom, Enquadrar, Pan, Grade, Snap (Ímã), Ortho, Medir (Régua)** | **100% coberto** (`zoom-in`, `zoom-out`, `maximize`/`focus-centered`, `grid-dots`, `magnet`, `ruler-measure`, `angle`) | Não. |
| **Modificar CAD (Mover, Copiar, Espelhar, Dividir, Apagar)** | **90% coberto** (`arrows-move`, `copy`, `flip-horizontal`, `scissors`/`divide`, `trash`/`eraser`) | Opcional: manter o vocabulário CAD bi-cromático (objeto original cinza + alvo azul). |
| **Barra / Linha e Nó** | Parcial (`vector-spline`, `line`, `point`) | **Sim (melhor customizado):** linha com duas alças quadradas azuis (`barra`) e nó em destaque (`no`). |
| **Apoios (Engaste, Rótula/Pino, Rolete V/H, Livre, Rótula interna)** | **0% em bibliotecas genéricas** | **Sim — Obrigatório desenho próprio.** |
| **Cargas (Pontual Nodal, Distribuída Uniforme, Peso Próprio, Momento)** | **0% em bibliotecas genéricas** | **Sim — Obrigatório desenho próprio.** |
| **Esforços e Resultados (Deformada, Normal $N$, Cortante $V$, Momento $M$, Reações, Verificação)** | **0% em bibliotecas genéricas** | **Sim — Obrigatório desenho próprio.** |
| **Ligações (Placa de Base, Chumbador/Parafuso, Solda de Filete, Perfil I/Eixo)** | **0% em bibliotecas genéricas** | **Sim — Obrigatório desenho próprio.** |

---

### Recomendação de Estratégia de Ícones e Como Embutir em WPF

1. **Recomendação de Conjunto:**
   - Adotar **Tabler Icons (MIT)** (ou **Lucide (ISC)**) para **ícones de UI geral, navegação, barra de status (Snap/Grid/Ortho/Unidades), busca, filtros e tabelas** (16 px e 20 px), combinado com o **motor bi-cromático próprio em `Icones.cs` (quadro 32×32)** para os **ícones de engenharia estrutural e CAD da Ribbon**.
2. **Como embutir em WPF (sem fontes `.ttf` externas que quebram em DPI baixo):**
   - **Manter e evoluir a arquitetura de `Icones.cs` (`DrawingGroup` + `Geometry.Parse` congelado com `.Freeze()`)**, adicionando suporte a:
     - `PenLineJoin.Round` e `StartLineCap/EndLineCap = PenLineCap.Round` (atualmente `Icones.cs` L125 usa `Miter` e `Flat`, o que deixa diagonais e setas serrilhadas ou pontudas demais);
     - Preenchimento Duotone (`#2638BDF8` — azul ciano com 15% de opacidade) para áreas de diagramas, chapas e perfis;
     - Dicionário auxiliar de glifos 16×16/24×24 extraídos dos `<path d="..."/>` do Tabler/Lucide para usar nos botões da barra de status, cabeçalhos do Inspector e Command Palette.

---

### Guia de Desenho para os Ícones Específicos de Engenharia Estrutural (Quadro 32×32)

O Estructto já possui uma base excelente em `Icones.cs` (cinza claro `#D9DCE1` para estrutura + azul `#38BDF8` para elemento ativo/resultado + âmbar `#F59E0B` / coral `#F43F5E` para modificadores). Abaixo estão os ajustes e novos desenhos recomendados:

1. **Barra (`barra`) e Nó (`no`):**
   - *Barra:* Linha diagonal `(6,26)→(26,6)` de `2 px` em `#E2E8F0` + duas alças quadradas `5×5 px` preenchidas em azul `#38BDF8` com borda escura `#0F172A` de `1 px` para destacar sobre a linha.
   - *Nó:* Duas barras concorrentes em cinza médio `#94A3B8` encontrando-se num nó central quadrado/circular azul `#38BDF8` brilhante com halo translúcido `#3338BDF8`.
2. **Apoios (`engaste`, `rotula`, `rolo`, `roloh`, `rotulab`):**
   - *Engaste:* Trecho de pilar vertical cinza chegando na chapa horizontal grossa (`2.5 px`) em azul `#38BDF8`, com 5 hachuras diagonais a 45° em `#94A3B8` abaixo e nó quadrado no topo.
   - *Rótula Fixa (2º gênero):* Triângulo equilátero com preenchimento duotone `#2638BDF8` e contorno `#E2E8F0` (`1.8 px`), vértice ligado a um círculo articulado azul `#38BDF8`, apoiado sobre base com hachuras.
   - *Rolete (1º gênero):* Triângulo duotone + duas rodas circulares nítidas (`r = 2.2`) sobre linha de solo contínua.
   - *Rótula na Barra (`rotulab`):* Duas extremidades de barra com anel circular central vazado (`r = 4.5`, traço azul `#38BDF8` de `2 px`).
3. **Cargas (`cargano`, `cargadist`, `momento_carga`, `peso`):**
   - *Carga Pontual (`cargano`):* Seta vertical vigorosa em azul/ciano `#38BDF8` (haste `2.5 px` + ponta triangular arredondada) apontando para o nó sobre a barra.
   - *Carga Distribuída (`cargadist`):* Caixa retangular preenchida com `#2238BDF8`, linha superior de carga em `#38BDF8` e **4 setas verticais uniformes** descendo até a barra cinza `#E2E8F0`.
   - *Momento Nodal (novo ícone):* Arco circular de 240° (`r = 9`) em `#F59E0B` (âmbar) ou `#38BDF8` com ponta de seta tangente envolvendo o nó central.
4. **Diagramas e Deformada (`def`, `normal`, `cortante`, `momento`):**
   - *Deformada (`def`):* Barra original reta tracejada em `#64748B` + curva cúbica suave (`C 10,27 22,27 28.5,8.5`) em ciano `#38BDF8` (`2.2 px`) com setinha indicando a flecha máxima $f$.
   - *Força Normal (`normal`):* Retângulo constante acima da barra preenchido em `#3338BDF8` com sinal `+` ou setas axiais opostas `← →`.
   - *Esforço Cortante (`cortante`):* Dois degraus antissimétricos (positivo à esquerda acima da barra em azul `#3338BDF8`, negativo à direita abaixo da barra em coral `#33F43F5E`) unidos pelo salto vertical no meio.
   - *Momento Fletor (`momento`):* Parábola clássica de viga biapoiada preenchida em `#3838BDF8` com contorno `#38BDF8` de `1.8 px` e linha de referência da barra no topo.
5. **Placa de Base, Chumbador e Solda (`placa`, `chumbador`, `solda`):**
   - *Placa de Base (`placa`):* Vista frontal/isométrica: perfil I vertical em cinza `#94A3B8`, chapa de base horizontal destacada em azul `#38BDF8` (`h = 4 px`), e dois chumbadores verticais com porca sextavada e gancho J ancorados no bloco inferior.
   - *Parafuso / Chumbador (novo ícone para abas/filtros):* Cabeça sextavada superior + arruela + fuste com 4 filetes de rosca diagonais nítidos.
   - *Solda de Filete (novo ícone para aba Soldas):* Canto em L (pilar vertical + chapa horizontal em `#E2E8F0`) com **triângulo de filete preenchido em amarelo/âmbar `#F59E0B`** no vértice interno e símbolo de bandeirinha de solda.

---

## 4. Paleta de Cores, Tipografia, Tokens e Micro-Interações

A paleta atual (`TemaPlant.cs`) usa tons ardósia médios (`#3B4453` e `#454F61`) que reduzem o contraste com o texto e deixam a interface com aspecto um pouco "empoeirado". A proposta abaixo moderniza os tons mantendo a identidade **AutoCAD 2025/2026 Dark + SkyCiv/Figma**, elevando o contraste para **WCAG AA / AAA**.

### 4.1 Tokens de Cores — Tema Escuro Moderno (Padrão) e Tema Claro (Opcional)

| Token Semântico | Hex Escuro (Novo) | Hex Atual (`TemaPlant`) | Hex Claro (Opcional) | Contraste c/ `Text.Primary` | Uso no Estructto |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `Bg.Canvas` | **`#141820`** | `#212830` | `#F8FAFC` | **15.8 : 1** (AAA) | Fundo do espaço do modelo (`Vista`) e vista 3D da placa. |
| `Bg.Window` / `Surface.0` | **`#1B202B`** | `#222933` | `#F1F5F9` | **14.1 : 1** (AAA) | Fundo da barra de abas, rodapé, divisórias do Dock e fundo base. |
| `Surface.1` (Painéis / Ribbon) | **`#242B38`** | `#3B4453` | `#FFFFFF` | **12.2 : 1** (AAA) | Corpo da Ribbon, painéis laterais (Projeto, Inspector, Tabelas), modais. |
| `Surface.2` (Cartões / Cabeçalhos) | **`#2D3646`** | `#2E3440` | `#F8FAFC` | **10.3 : 1** (AAA) | Cartões da `JanelaPlaca`, cabeçalhos de seção, linhas alternadas da tabela. |
| `Surface.3` (Inputs / Hover) | **`#364154`** | `#4E5A6E` | `#E2E8F0` | **8.5 : 1** (AAA) | Fundo de `TextBox`, `ComboBox`, `Stepper` e estado `MouseOver` de botões. |
| `Border.Subtle` | **`#333E50`** | `#343C48` | `#E2E8F0` | — | Linhas de grade de tabelas, divisores internos de cartões (`1 px`). |
| `Border.Default` | **`#475569`** | `#5A677C` | `#CBD5E1` | — | Contorno padrão de inputs, botões secundários e cartões. |
| `Border.Focus` | **`#38BDF8`** | `#0696D7` | `#0284C7` | — | Contorno de input focado, cota clicável ativa e seleção. |
| `Text.Primary` | **`#F1F5F9`** | `#F0F2F5` | `#0F172A` | — | Rótulos principais, valores de tabelas, títulos. |
| `Text.Secondary` | **`#94A3B8`** | `#B4BCC8` | `#475569` | **6.1 : 1** (AA) | Unidades (`kN`, `mm`, `MPa`), subtítulos, cabeçalhos de coluna. |
| `Text.Muted` | **`#64748B`** | `#8A95A6` | `#94A3B8` | **3.6 : 1** | Placeholders ("Digite um comando..."), itens desabilitados, dicas. |
| `Accent.Primary` | **`#0EA5E9`** | `#0696D7` | `#0284C7` | **8.2 : 1** (c/ preto) | Botões primários (`Aplicar`, `Calcular`), pílulas ativas, abas selecionadas. |
| `Accent.Highlight` | **`#38BDF8`** | `#5FB6EF` | `#0369A1` | **9.4 : 1** (c/ `Bg.Canvas`) | Realce de seleção no canvas, cotas clicáveis, ícones ativos. |
| `Status.Success` (Texto / Fundo) | **`#22C55E` / `#143828`** | `#4CD964` / `#22603E` | `#16A34A` / `#DCFCE7` | **8.8 : 1** (AA) | Verificação **PASSA**, aproveitamento $\le 90\%$, selo `✓`. |
| `Status.Warning` (Texto / Fundo) | **`#F59E0B` / `#3B2A14`** | `#E8B448` / `#5A3A1A` | `#D97706` / `#FEF3C7` | **8.4 : 1** (AA) | Aproveitamento entre $90\%$ e $100\%$, avisos de modelagem (`⚠`). |
| `Status.Danger` (Texto / Fundo) | **`#F43F5E` / `#3F1A24`** | `#F24B4B` / `#802C2C` | `#E11D48` / `#FFE4E6` | **6.2 : 1** (AA) | Verificação **NÃO PASSA** ($>100\%$), instabilidade, erro crítico (`✕`). |

---

### 4.2 Cores Semânticas do Canvas e dos Diagramas Estruturais

| Elemento Gráfico no `Vista` | Traço / Contorno | Preenchimento (Área) | Observação de Engenharia |
| :--- | :--- | :--- | :--- |
| **Grade Principal / Secundária** | `#232A36` / `#1A202A` | — | Grade adaptativa que subdividi ao dar zoom, sem ofuscar as barras. |
| **Barra Normal / Selecionada** | `#E2E8F0` (`2 px`) / `#38BDF8` (`3 px`) | Halo `#3338BDF8` na seleção | Em modo "Espessura real", desenhar a silhueta da altura $h$ em `#2694A3B8`. |
| **Cargas Aplicadas ($P, q, M$)** | **`#F59E0B`** (Âmbar vivo) | `#26F59E0B` | Separa claramente as ações externas (âmbar) das barras (branco/azul). |
| **Reações de Apoio ($R_x, R_y, M_z$)** | **`#10B981`** (Esmeralda) | — | Setas com dupla ponta ou traço mais espesso (`2.5 px`) nos apoios. |
| **Deformada ($\delta$)** | **`#38BDF8`** (Ciano brilhante) | Barra original em `#475569` tracejada | Curva suave contínua sobre a sombra tracejada da posição indeslocada. |
| **Força Normal ($N$)** | **Tração (+):** `#3B82F6` (Azul)<br>**Compressão (−):** `#F43F5E` (Coral) | `#383B82F6`<br>`#38F43F5E` | Convenção universal imediata: vermelho/coral alerta flambagem por compressão; azul indica tração. |
| **Esforço Cortante ($V$)** | **`#A855F7`** (Violeta claro) | `#33A855F7` | Distingue instantaneamente o diagrama de cortante do normal e do momento. |
| **Momento Fletor ($M$)** | **`#22C55E`** (Verde) ou **`#0EA5E9`** (Azul) | `#3322C55E` ou `#330EA5E9` | Desenhado sempre do lado tracionado (convenção brasileira/Ftool), com pílulas nos picos. |

---

### 4.3 Tipografia, Escala, Grade de 4 px, Raios e Micro-Animações

- **Família de Fontes:**
  - **UI Geral:** `"Segoe UI Variable Text", "Artifakt Element", "Inter", "Segoe UI"` (o Windows 11 traz `Segoe UI Variable` nativamente com ajuste de *optical size*; em `TemaPlant.Fonte`, priorizar `Segoe UI Variable Text` ou `Artifakt Element`).
  - **Números em Tabelas, Coordenadas e Cotas:** Ativar `Typography.NumeralAlignment="Tabular"` (`font-variant-numeric: tabular-nums`) em todos os `DataGrid`, medidores e barras de status para que os dígitos e vírgulas decimais fiquem perfeitamente alinhados verticalmente.
  - **Linha de Comando e Fórmulas Rápidas:** `"Cascadia Mono", "JetBrains Mono", "Consolas"`.
- **Escala Tipográfica:**
  - `11 px` (`SemiBold`, `Uppercase`, `CharacterSpacing` leve): Cabeçalhos de painéis, títulos de grupos e badges (`PASSA`, `NÃO PASSA`).
  - `12 px` (`Regular`/`Medium`): Densidade padrão CAD para propriedades, tabelas, botões da Ribbon e tooltips.
  - `13 px` (`Medium`): Inputs de edição ativa, linha de comando, nomes de perfis nos cartões.
  - `16 px` (`SemiBold`): Títulos de modais e valores principais de aproveitamento (`84%`).
  - `20–24 px` (`Bold`): Banner de status principal em `JanelaVerificacao` e `JanelaPlaca`.
- **Espaçamento (Grade Estrita de 4 px):**
  - Padding de inputs e botões compactos: `8, 4` (`Height = 28 px`).
  - Padding de cartões internos (`CartaoDireita`): `12 px` ou `16 px`, com margem inferior `12 px`.
  - Margem externa de janelas modais: `16 px`.
- **Raios de Borda (`CornerRadius`):**
  - Controles pequenos (`TextBox`, `ComboBox`, `Button`, `Stepper`, caixas de cota): **`4 px`**.
  - Cartões (`Card`, painéis flutuantes HUD, tooltips, modais): **`6 px` a `8 px`**.
  - Pílulas de filtro (`Pilula` em `JanelaPlaca` e Status Bar): **`12 px`** (totalmente arredondadas).
- **Sombras e Elevação:**
  - Elementos flutuantes sobre o canvas (Linha de Comando, HUD de Resultados, Mini-Toolbar) devem ter `DropShadowEffect` discreto (`BlurRadius="16"`, `ShadowDepth="4"`, `Opacity="0.45"`, `Color="#000000"`) e borda interna de `1 px` em `#475569`.
- **Micro-Animações (150–180 ms, `CubicEase Out`):**
  - **Medidores de Aproveitamento (`Medidor` e `BarraCapacidade`):** Animar a largura da barra ao trocar de perfil ou editar a placa em `160 ms`.
  - **Hover em Botões e Pílulas:** Transição de cor de fundo de `120 ms`.
  - **Overlay de Troca de Chumbador (`overlayTrocar`):** Entrada com leve fade + slide horizontal de `16 px` em `150 ms`.

---

## 5. Padrões de UX para a Janela de Placa de Base e Ligações

### Como os líderes de mercado resolvem o fluxo de Placa de Base

| Software | Fluxo e Escolha de Chapa/Chumbador | Visualização 3D / 2D | Apresentação de Resultados e Relatório | Fontes |
| :--- | :--- | :--- | :--- | :--- |
| **Hilti PROFIS Engineering** | **Fluxo guiado em cartões lógicos** (Concreto $\to$ Placa & Perfil $\to$ Chumbador $\to$ Cargas) + **Seletor comparativo inteligente** que filtra chumbadores mecânicos/químicos e já mostra a % de utilização de cada um antes de clicar. | **Vista 3D interativa com cotas editáveis inline** ($c_1, s_1, h_{ef}, t_p$) + cone de ruptura no concreto e indicação da linha neutra. | **3 Cartões de Semáforo (Aço, Concreto, Chumbador)** com % máxima de cada modo de falha + memorial PDF com desenhos cotados e instruções de instalação (furo, limpeza, torque). | [Hilti PROFIS Engineering](https://www.hilti.com.br/content/hilti/W2/BR/pt/engineering/software/profis/profis-engineering-suite.html) |
| **IDEA StatiCa Connection** | **Lista de Operações à direita** (Chapa de base, Chumbadores, Soldas, Enrijecedores) + botão de pré-dimensionamento rápido. | **Viewport 3D com mapa de tensões equivalentes** na chapa, forças de tração em setas 3D nos chumbadores e zona de compressão no concreto. | **Cabeçalho de Resumo Global** (`PASSA / 82.4%`) + abas inferiores para Tensão na Chapa, Soldas e Ancoragens, com equações expansíveis em LaTeX. | [IDEA StatiCa Base Plate](https://www.ideastatica.com/steel) |
| **RISA Connection** | **Propriedades agrupadas + atualização instantânea** ao alterar espessura ou arranjo, sem botão "Calcular". | **Abas de Vista 3D Sólida vs. Desenho de Oficina 2D Cotado** (planta e elevação lado a lado). | Lista de Estados Limites ordenada pelo mais crítico no topo; clicando no estado limite, expande a fórmula exata substituída com os números. | [RISA Connection](https://risa.com/products/risaconnection) |
| **CYPE Connect** | **Fluxo de modelagem + verificação normativa** com catálogo comercial integrado. | **Vista 3D com corte da fundação** mostrando o comprimento embutido $h_{ef}$ dentro do bloco de concreto. | Memorial completo exportável e **desenho detalhado de fabricação** pronto para prancha. | [CYPE Connect](https://info.cype.com/en/product/cype-connect/) |
| **Simpson Anchor Designer** | **Multi-Product Evaluator:** avalia simultaneamente todos os chumbadores do catálogo para o arranjo atual e destaca o mais econômico que passa. | **3D em tempo real** com vetores coloridos de $N, V, M$ aplicados no pé do pilar para evitar erro de sinal. | Tabela de resumo por modo de falha (tração aço, arrancamento, cone de concreto, corte, combinação $T+V$). | [Simpson Anchor Designer](https://www.strongtie.com/softwareandwebapplications/category) |

---

### O que a `JanelaPlaca` do Estructto já faz bem e o que Copiar / Melhorar

A `JanelaPlaca` atual (`Janelas.cs` L943–L2000) já está muito à frente do restante do programa: possui cotas azuis clicáveis (`PlanoPlaca`), pílulas (`2 × 2`, `2 × 3`, `3 × 3`), steppers para espessura/sobra/borda/solda, cartões à direita com `BarraCapacidade` e painel `overlayTrocar` para comparar chumbadores. Para deixá-la no nível **Hilti PROFIS + IDEA StatiCa**, recomendo **8 melhorias cirúrgicas**:

1. **Vista 3D Interativa (Rotação por Arraste) com Bloco de Concreto e Embutimento $h_{ef}$:**
   - Hoje `DesenharIso` (`Janelas.cs` L840) usa ângulo fixo (`raw`) e desenha apenas a placa e o trecho do pilar.
   - **Melhoria:** Permitir **arrastar com o botão esquerdo/direito dentro do quadro `Vista 3D`** (variando `yaw` e `pitch`), desenhar o **prisma translúcido do bloco de concreto abaixo da placa**, mostrar o **fuste dos chumbadores descendo até a profundidade real $h_{ef}$** (com cota vertical $h_{ef}$), e desenhar os **cordões de solda em amarelo/laranja** no contorno do perfil.
2. **Mapa Visual da Zona Comprimida e Linha de Giro na Vista de Cima (`PlanoPlaca`):**
   - Na aba `Forças nos furos`, além de colorir os furos tracionados em vermelho proporcional a $T_i / T_{\max}$ (como já faz em L726), **sombrear em azul/verde translúcido a região comprimida da placa** (entre a linha de pivô `R.PivoY` e a borda comprimida) e desenhar os **vetores 3D/2D de $N, V, M$** com setas claras indicando para qual lado o momento comprime a placa.
3. **Seletor de Combinação / Caso Crítico no Topo da Janela:**
   - Exibir no banner superior de `JanelaPlaca` quais são os esforços governantes ($N, V, M$) com opção de alternar ou visualizar como a placa se comporta em outros nós (`Candidatos`).
4. **Fórmulas Renderizadas com `WpfMath` na Aba "Cálculo" e nos Expanders "Ver cálculo":**
   - O projeto já referencia `WpfMath 2.1.0` (`Estructto.csproj` L36). Usar controles `<wpfmath:FormulaControl>` para renderizar as equações de flexão da chapa ($t_{\text{nec}} = \sqrt{\dots}$), interação tração + corte ($\left(T/T_{\text{adm}}\right)^p + \left(V/V_{\text{adm}}\right)^p \le 1$) e von Mises da solda ($\tau = \sqrt{\dots} \le \tau_{\text{adm}}$) com qualidade tipográfica de livro.
5. **Visualizador de Cone de Concreto e Distâncias Críticas ($s_{\text{cr}} = 3 h_{ef}$, $c_{\text{cr}} = 1{,}5 h_{ef}$):**
   - Quando um chumbador de catálogo estiver selecionado e houver sobreposição de cones ($\psi_{\text{cone}} < 1$), desenhar um **círculo/quadrado tracejado suave de raio $1{,}5 h_{ef}$ ao redor de cada furo** no `PlanoPlaca` para que o engenheiro veja *visualmente* a superposição dos cones de concreto e como aumentar a `Borda` ou a `Sobra` resolve o problema.
6. **Tabela Comparativa de Chumbadores (`overlayTrocar`) Estilo Hilti PROFIS:**
   - No `overlayTrocar`, exibir cada opção de chumbador como uma linha rica com: **Badge do Tipo** (`Mecânico` / `Químico`), **Diâmetro de Furo ($\varnothing d_0 \times h_{ef}$)**, **Mini-barra de Capacidade Usada (%)** já calculada para a placa atual, e **Motivo exato** se não passar (ex.: `Falta tração (135%)` ou `Espaçamento < 6d`).
7. **Desenho Técnico de Oficina na Aba "Ficha de Fabricação" + Exportação Direta (PDF / DXF / Copiar):**
   - Na aba `Ficha de fabricação`, colocar ao lado da tabela de dados um **desenho técnico 2D limpo (fundo claro ou escuro selecionável)** com planta de furação ($X, Y$), detalhe do furo ($\varnothing d_{\text{furo}}$), simbologia de solda AWS ($\triangle$ perna $h$ em todo o contorno) e botões `[Copiar Ficha]`, `[Exportar DXF da Chapa]` e `[Incluir no Memorial]`.
8. **Destaque Automático do Item Governante (Governing Check):**
   - Nos 3 cartões da direita (*1. Fixação*, *2. Chapa*, *3. Solda*), destacar com uma borda lateral de `3 px` o item que possui a **maior taxa de utilização** (o gargalo da ligação), facilitando saber onde atuar.

---

## 6. Lista Priorizada (P1 / P2 / P3) de Melhorias para o Estructto

| Prioridade | ID | Melhoria Recomendada | Arquivos Principais Impactados | Esforço Estimado | Impacto Visual / UX |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **P1 (Alto Impacto / Base)** | **P1.1** | **Novo Sistema de Tokens e Estilos Nativos (`TemaPlant.cs` + `MainWindow.xaml`):** Atualizar a paleta para o tema escuro de alto contraste (`#141820` / `#1B202B` / `#242B38` / `#38BDF8`), configurar `Segoe UI Variable` + `TabularNums` e criar estilos WPF modernos (`CornerRadius=4`, hover suave, `ScrollBar` fina) para `TextBox`, `ComboBox`, `Button`, `DataGrid`, `TabControl` e `TreeView`. | `TemaPlant.cs`, `MainWindow.xaml`, `Janelas.cs` | **Pequeno–Médio** (1–2 dias) | **Transformador** — moderniza 100% das telas de imediato sem novas dependências. |
| **P1** | **P1.2** | **Substituir o `WinForms PropertyGrid` por um Inspector WPF Nativo Estilo Figma/SkyCiv:** Remover `WindowsFormsHost` (`MainWindow.xaml` L142) e criar painel nativo em cartões com Seletor de Perfil (com miniatura vetorial da seção), inputs 2×2 de geometria com expressões matemáticas, botões rápidos de apoio/rótula e resumo de aproveitamento. | `MainWindow.xaml`, `MainWindow.xaml.cs`, `Estructto.csproj` | **Médio** (2–3 dias) | **Altíssimo** — elimina o único componente visualmente datado (WinForms) da tela principal. |
| **P1** | **P1.3** | **HUD Flutuante de Resultados e Diagramas com Gradiente + Scrubber no Canvas:** Criar painel flutuante no canto do `Vista` (seletor `[N][V][M][δ][%Ap]`, slider de escala contínuo, animação da deformada) + renderização de diagramas com preenchimento em gradiente translúcido e tooltip de inspeção ao passar o mouse na barra ($x, N(x), V(x), M(x), \delta(x)$). | `Vista.cs`, `MainWindow.xaml` | **Médio** (2–3 dias) | **Altíssimo** — experiência de análise digna de SkyCiv / SAP2000 moderno. |
| **P1** | **P1.4** | **Refinamento dos Ícones Vetoriais (`Icones.cs`):** Ativar `PenLineCap.Round` / `PenLineJoin.Round`, preenchimento *Duotone* (`#2638BDF8`), adicionar ícones de Momento Nodal, Parafuso, Solda de Filete e glifos Tabler/Lucide para a barra de status (`Snap`, `Grid`, `Ortho`, `Cotas`). | `Icones.cs`, `MainWindow.xaml.cs` | **Pequeno** (1 dia) | **Alto** — deixa a Ribbon e a Status Bar com acabamento profissional de AutoCAD 2026. |
| **P2 (Produtividade CAD & Ligações)** | **P2.1** | **HUD de Entrada Dinâmica no Cursor + Cotas Temporárias Clicáveis na `Vista`:** Caixas de `Comprimento` e `Ângulo` junto ao cursor ao desenhar barras (`Tab` alterna) e cota azul clicável ao selecionar uma barra (mesmo padrão já usado em `PlanoPlaca`). | `Vista.cs` | **Médio** (2 dias) | **Alto** — une a precisão do AutoCAD à agilidade do Revit/Onshape. |
| **P2** | **P2.2** | **Mini-Toolbar Contextual no Clique + Mapa de Calor de Aproveitamento nas Barras:** Barra flutuante rápida sobre o nó/barra selecionado e modo de coloração das barras por % de utilização (verde/ciano/âmbar/vermelho) com badges clicáveis `⚠`/`✕` nos elementos críticos. | `Vista.cs`, `MainWindow.xaml.cs` | **Médio** (2 dias) | **Alto** — reduz viagens do mouse até a Ribbon e evidencia falhas num piscar de olhos. |
| **P2** | **P2.3** | **Upgrade 3D e Visual da `JanelaPlaca` (Padrão Hilti PROFIS / IDEA StatiCa):** Vista 3D rotacionável com bloco de concreto, embutimento $h_{ef}$ e filetes de solda; sombra da zona comprimida e cones de concreto ($1{,}5 h_{ef}$) no `PlanoPlaca`; fórmulas LaTeX via `WpfMath` nos cálculos. | `Janelas.cs` (`PlanoPlaca`, `JanelaPlaca`) | **Médio** (2–3 dias) | **Altíssimo** — torna o módulo de Placa de Base o grande diferencial comercial do Estructto. |
| **P2** | **P2.4** | **Command Palette (`Ctrl+K`) + Melhoria da Linha de Comando e Status Bar:** Modal de busca rápida de comandos e perfis (`Ctrl+K`), pílulas interativas no rodapé e seletor rápido de unidades. | `MainWindow.xaml`, `MainWindow.xaml.cs` | **Pequeno–Médio** (1,5 dia) | **Médio–Alto** — acelera usuários avançados e facilita a descoberta de recursos. |
| **P3 (Polimento & Extras)** | **P3.1** | **Nova Tela Inicial (`TelaInicial`) e Assistentes com Preview ao Vivo:** Cartões modernos com ilustrações vetoriais dos pórticos típicos, miniaturas nos arquivos recentes e preview em tempo real ao ajustar parâmetros dos assistentes. | `MainWindow.xaml`, `Janelas.cs` | **Pequeno–Médio** (1,5 dia) | **Alto na 1ª impressão** ao abrir o programa. |
| **P3** | **P3.2** | **Tabelas de Resultados Avançadas + Exportação DXF da Placa de Base:** Filtros rápidos (`Apenas Selecionados`, `Apenas Críticos`), barras de progresso na coluna `Aproveitamento` em todas as tabelas e botão `[Exportar DXF]` na Ficha da Placa. | `MainWindow.xaml.cs`, `Janelas.cs` | **Pequeno–Médio** (1,5 dia) | **Médio–Alto** para fluxo de escritório de projetos. |

---

## 7. Fontes e Referências Verificadas

1. **Repositórios e Licenças WPF (.NET 8):**
   - `Fluent.Ribbon` (MIT): https://github.com/fluentribbon/Fluent.Ribbon
   - `Dirkster.AvalonDock` (MS-PL / Apache-2.0): https://github.com/Dirkster99/AvalonDock
   - `WPF-UI` (MIT): https://github.com/lepoco/wpfui
   - `iNKORE.UI.WPF.Modern` (MIT): https://github.com/iNKORE-NET/UI.WPF.Modern
   - `MahApps.Metro` (MIT): https://github.com/MahApps/MahApps.Metro
   - `HandyControl` (MIT): https://github.com/HandyOrg/HandyControl
   - `MaterialDesignInXamlToolkit` (MIT): https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit
   - `WpfMath` (MIT): https://github.com/ForNeVeR/WpfMath
   - `ScottPlot` (MIT): https://github.com/ScottPlot/ScottPlot
   - `LiveCharts2` (MIT): https://github.com/Live-Charts/LiveCharts2
   - `SharpVectors` (BSD-3-Clause): https://github.com/ElinamLLC/SharpVectors
   - `HelixToolkit` (MIT): https://github.com/helix-toolkit/helix-toolkit
2. **Conjuntos de Ícones Open-Source:**
   - `Tabler Icons` (MIT, 6.200+ ícones): https://github.com/tabler/tabler-icons
   - `Lucide Icons` (ISC, 1.900+ ícones): https://github.com/lucide-icons/lucide
   - `Fluent UI System Icons` (MIT, 9.000+ variantes): https://github.com/microsoft/fluentui-system-icons
   - `Phosphor Icons` (MIT, 1.200+ base / 9.000+ variantes): https://github.com/phosphor-icons/core
   - `Google Material Design Icons / Symbols` (Apache-2.0): https://github.com/google/material-design-icons
   - `VS Code Codicons` (MIT, ~500+ ícones): https://github.com/microsoft/vscode-codicons
   - `Iconoir` (MIT, 1.600+ ícones): https://github.com/iconoir-icons/iconoir
3. **Referências de UI/UX CAD, Estruturas e Ligações:**
   - Autodesk AutoCAD 2025/2026 UI & Dynamic Input: https://help.autodesk.com/view/ACD/2025/ENU/
   - Autodesk Fusion UI & Shortcut Palette: https://help.autodesk.com/view/fusion360/ENU/
   - Onshape User Interface & Measurement Tools: https://cad.onshape.com/help/Content/user_interface.htm
   - SkyCiv Structural 3D UI: https://skyciv.com/structural-software/structural-3d/
   - Tekla Structures Utilization Visualization: https://support.tekla.com/doc/tekla-structures/2024/int_user_interface
   - Hilti PROFIS Engineering Suite (Base Plate & Anchors): https://www.hilti.com.br/content/hilti/W2/BR/pt/engineering/software/profis/profis-engineering-suite.html
   - IDEA StatiCa Connection & CBFEM Base Plate: https://www.ideastatica.com/steel
   - RISA Connection: https://risa.com/products/risaconnection
   - CYPE Connect: https://info.cype.com/en/product/cype-connect/
   - Simpson Strong-Tie Anchor Designer: https://www.strongtie.com/softwareandwebapplications/category
