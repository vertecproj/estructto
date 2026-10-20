# GUIA DE DESIGN — Estructto v2 (WPF / Dark Technical CAD)

> Síntese prática das diretrizes de interface e engenharia WPF (`taste-skill`, `web-interface-guidelines`, `frontend-design`, `ui-ux-pro-max`, `dotnet-wpf-modern`, `wpf-best-practices`, `awesome-design-md`, `PESQUISA.md` e `PLANO-V2.md`).

---

## 1. Direção Visual e Dials de Calibração

O **Estructto** adota a direção **Dark Technical CAD** (fusão dos arquétipos *Data-Terminal* e *True-Black-Flat* adaptada para sessões de engenharia de 8h): o cromo da aplicação recua em uma escada contínua de ardósia escura (`#141820` → `#364154`) separada apenas por divisores *hairline* de `1 px` sem sombras pesadas, fazendo com que a geometria estrutural, diagramas de esforços ($N, V, M, \delta$) e vereditos normativos (**Passa / Alerta / Não Passa**) dominem a hierarquia visual `[taste-skill/app-screens, PESQUISA.md]`. A cor no cromo é restrita a $\le 5\%$ da área da janela (um único acento ciano técnico `#0EA5E9` para foco/CTA ativo e cores semânticas exclusivamente para resultados de verificação), eliminando ruído decorativo em prol de precisão tabular, leitura instantânea de unidades e resposta imediata ao teclado e mouse `[taste-skill/tells, linear/DESIGN.md]`.

### Dials do Sistema (`taste-skill` / `ui-ux-pro-max`)

| Dial | Valor | Justificativa e Tradução para WPF / CAD |
| :--- | :---: | :--- |
| **`DESIGN_VARIANCE`** | **`2 / 10`** *(Baixa)* | **Geometria ortogonal previsível:** grade estrita de `4 px`, painéis simétricos, colunas fixas com `GridSplitter` e zero layouts assimétricos/editoriais. Em software de cálculo estrutural, o olho precisa encontrar coordenadas, reações e taxas de utilização sempre no mesmo eixo `[ui-ux-pro-max, taste-skill/SKILL]`. |
| **`MOTION_INTENSITY`** | **`2 / 10`** *(Baixa)* | **Micro-interações puramente funcionais:** transições de `120–160 ms` (`CubicEase EaseOut`) só em resposta direta do usuário (hover, foco, largura de barra de capacidade) e loop harmônico de `60 fps` para deformada modal/elástica. Proibido animar abertura de abas, entrada de cartões (*fade-up*) ou troca de ferramentas `[ui-ux-pro-max, wpf-best-practices]`. |
| **`VISUAL_DENSITY`** | **`9 / 10`** *(Alta)* | **Densidade de cockpit técnico:** fonte base `12–13 px`, controles com `28 px` de altura padrão, linhas de tabela de `28–32 px` e espaçamentos internos de `4–12 px`, garantindo que janelas complexas (`JanelaPlaca`, `JanelaVerificacao`) caibam inteiras em `1280×720` sem rolagem vertical obrigatória `[taste-skill/app-screens, PLANO-V2.md]`. |

---

## 2. Referências de Marca (`DESIGN.md`) e Tokens Finais Decididos

### 2.1 Marca de Referência Principal e Comparação
* **Referência primária — Linear (`linear.app/DESIGN.md`) + Raycast (`raycast/DESIGN.md`):** escolhidas pela arquitetura de **elevação por luminância em 4 degraus** sobre fundo escuro e **bordas hairline de `1 px`** (`BorderBrush` sutil em vez de `DropShadowEffect`), onde a cor nunca é decorativa e serve como sinal semântico `[linear/DESIGN.md, raycast/DESIGN.md]`.
* **Referências secundárias — Warp (`warp/DESIGN.md`), Figma (`figma/DESIGN.md`) e Cursor (`cursor/DESIGN.md`):** do **Warp** e **Supabase** vêm os raios técnicos compactos (`4–6 px` em vez de pílulas exageradas); do **Figma** vem a grade compacta `2×2` do Inspector com sufixo de unidade interno (`m`, `cm`, `kN`, `MPa`); do **Cursor** e **Sentry** vêm os badges/chips semânticos de fundo escuro pálido com texto saturado `[warp/DESIGN.md, figma/DESIGN.md, cursor/DESIGN.md, sentry/DESIGN.md]`.
* **Decisão vs. `PESQUISA.md`:** `Linear` (`#010102`) e `Vercel` (`#000000`) usam preto puro de altíssimo contraste para web, mas em CAD com linhas vetoriais claras por horas seguidas o preto `#000` gera *halation* (ofuscamento nas bordas do traço). Já o `TemaPlant` legado (`#3B4453`) era claro e acinzentado demais. **Decisão final:** adotamos integralmente a escada de ardósia fria da `PESQUISA.md` (`#141820` no canvas a `#364154` nos inputs), enriquecida com um token intermediário `Text.Tertiary` (`#7C8B9E`, contraste `4.6:1`) para rótulos de apoio que precisam passar no WCAG AA, reservando `Text.Muted` (`#64748B`) só para desabilitados e placeholders `[PESQUISA.md, taste-skill/app-screens]`.

### 2.2 Paleta de Cores e Superfícies (`Themes/DesignTokens.xaml`)

| Categoria | Token (`x:Key`) | Hex | Contraste / Regra de Uso em WPF |
| :--- | :--- | :---: | :--- |
| **Superfícies** | `Bg.Canvas` | `#141820` | Mais escuro que a UI para destacar barras, apoios e diagramas `[PESQUISA.md]`. |
| | `Bg.Window` (`Surface.0`) | `#1B202B` | Fundo base da `MainWindow`, diálogos modais e rodapé `[PESQUISA.md]`. |
| | `Surface.1` | `#242B38` | Painéis laterais (Inspector, Árvore), `Fluent:Ribbon` e `StatusBar` `[linear/DESIGN.md]`. |
| | `Surface.2` | `#2D3646` | Cartões de grupo, cabeçalhos de seção/tabela e barra de ferramentas `[PESQUISA.md]`. |
| | `Surface.3` | `#364154` | Fundo de `TextBox`/`ComboBox`, abas ativas e estado hover de linha/item `[raycast/DESIGN.md]`. |
| | `Surface.Overlay` | `#E6181C24` | HUD flutuante sobre o canvas (90% opacidade + `Border.Subtle` de `1 px`) `[PESQUISA.md]`. |
| **Bordas (`1 px`)** | `Border.Subtle` | `#333E50` | Divisores de painéis, grade interna de tabelas, contorno de cartões `[linear/DESIGN.md]`. |
| | `Border.Default` | `#475569` | Contorno de inputs normais, botões secundários e eixos de referência `[PESQUISA.md]`. |
| | `Border.Focus` | `#38BDF8` | Anel de foco de teclado (`1.5 px`), input ativo e item selecionado `[vercel-guidelines]`. |
| **Texto** | `Text.Primary` | `#F1F5F9` | **15.8:1** — Valores numéricos, títulos, fórmulas e dados editáveis (nunca `#FFFFFF` puro) `[taste-skill/tells]`. |
| | `Text.Secondary` | `#94A3B8` | **6.1:1** — Rótulos de campo, cabeçalhos de coluna e unidades (`kN`, `kN·m`, `cm`) `[PESQUISA.md]`. |
| | `Text.Tertiary` | `#7C8B9E` | **4.6:1 (AA)** — Metadados secundários, legendas de escala e dicas de rodapé `[taste-skill/app-screens]`. |
| | `Text.Muted` | `#64748B` | **3.6:1** — Exclusivo para placeholders, atalhos (`Ctrl+N`) e controles `IsEnabled="False"` `[vercel/DESIGN.md]`. |
| **Acento** | `Accent.Primary` | `#0EA5E9` | Botão primário (máx. 1 por janela/vista), indicador de aba ativa (`2 px`) `[PESQUISA.md]`. |
| | `Accent.Hover` / `Pressed` | `#38BDF8` / `#0284C7` | Estados interativos de hover (mais claro) e clique pressionado (mais escuro) `[ui-ux-pro-max]`. |
| | `Accent.Subtle` | `#2638BDF8` | Fundo de seleção de linha/ferramenta e preenchimento duotone (`15%` opacidade) `[cursor/DESIGN.md]`. |
| **Semânticas** | `Status.Success` / `.Bg` | `#22C55E` / `#143828` | **Passa** ($\text{Ratio} \le 0{,}90$), estrutura estável, reação de apoio (borda `#1E563B`) `[PESQUISA.md]`. |
| | `Status.Warning` / `.Bg` | `#F59E0B` / `#3B2A14` | **Alerta** ($0{,}90 < \text{Ratio} \le 1{,}00$), esbeltez limite, cargas aplicadas (borda `#63451C`) `[PESQUISA.md]`. |
| | `Status.Danger` / `.Bg` | `#F43F5E` / `#3F1A24` | **Não Passa** ($\text{Ratio} > 1{,}00$), hipostática/instável, compressão (borda `#6E2536`) `[sentry/DESIGN.md]`. |
| **Canvas CAD** | `Cad.GridMajor` / `Minor` | `#232A36` / `#1A202A` | Linhas de grade principal (a cada 5 subdivisões) e secundária sobre `#141820` `[PESQUISA.md]`. |
| | `Cad.Member` / `Selected` | `#E2E8F0` / `#38BDF8` | Barra estrutural (`2 px`) e barra selecionada (`3 px` + halo `#3338BDF8` de `8 px`) `[PESQUISA.md]`. |
| | `Cad.Diagram.Tension` | `#3B82F6` | Normal de Tração ($+N$) — linha `2 px` + preenchimento `#403B82F6` → `#143B82F6` `[PESQUISA.md]`. |
| | `Cad.Diagram.Compression`| `#F43F5E` | Normal de Compressão ($-N$) — linha `2 px` + preenchimento `#40F43F5E` → `#14F43F5E` `[PESQUISA.md]`. |
| | `Cad.Diagram.Shear` | `#A855F7` | Esforço Cortante ($V$) — roxo técnico com hachura perpendicular discreta `[PESQUISA.md]`. |
| | `Cad.Diagram.Moment` | `#22C55E` | Momento Fletor ($M$) — desenhado pelo lado da fibra tracionada (convenção BR) `[PESQUISA.md]`. |

### 2.3 Tipografia, Espaçamento, Raios, Elevação e Movimento
* **Famílias Tipográficas:** UI em `Segoe UI Variable Text` (fallback `Segoe UI`); Dados Numéricos/Tabelas/Coordenadas em `Segoe UI` com `Typography.NumeralAlignment="Tabular"` e `Typography.NumeralStyle="Lining"`, ou `Cascadia Mono` / `JetBrains Mono` (`FontFamily="Cascadia Mono, Consolas, Segoe UI"`); Fórmulas NBR 6118 / NBR 8800 via `WpfMath` (`FormulaControl` com `Foreground="#F1F5F9"`) `[PESQUISA.md, vercel-guidelines]`.
* **Escala Tipográfica (Desktop Compacto):**
  * `Font.Micro` (`10 px`, SemiBold, Tracking `+0.04em`): cabeçalhos de seção do Inspector em CAIXA ALTA e badges `[linear/DESIGN.md]`.
  * `Font.Caption` (`11 px`, Regular): unidades (`kN/m`, `MPa`), barra de status, cotas do canvas e dicas `[PESQUISA.md]`.
  * `Font.Body` (`12.5 px`, Regular/Medium): padrão de toda a UI, `TextBox`, `DataGrid` e árvores `[figma/DESIGN.md]`.
  * `Font.Subtitle` (`14 px`, SemiBold): título de painel, cabeçalho de cartão ou diálogo `[PESQUISA.md]`.
  * `Font.Title` (`18 px`, Bold, Tabular): KPI de utilização máxima (`Ratio = 0,84`) e veredito principal `[PESQUISA.md]`.
* **Grade de Espaçamento (Base `4 px`):** `2 px` (gap ícone-texto interno), `4 px` (padding vertical de input/chip), `8 px` (gap entre controles e padding horizontal de input), `12 px` (padding interno de cartões e seções do Inspector), `16 px` (margem externa de diálogos e separação de blocos principais) `[ui-ux-pro-max, PLANO-V2.md]`.
* **Raios de Borda (`CornerRadius`):** `0 px` para painéis encaixados (`AvalonDock`, `StatusBar`, `DataGrid`); `3 px` para badges/chips compactos; `4 px` para `Button`, `TextBox`, `ComboBox` e itens de menu; `6 px` para cartões de resumo, HUD flutuante e toasts; `8 px` exclusivo para janelas modais/diálogos `[warp/DESIGN.md, PESQUISA.md]`.
* **Elevação:** Sem sombra em controles e cartões encaixados (usar degrau `Surface.1` → `Surface.2` + `BorderBrush` de `1 px`). Sombra (`DropShadowEffect` com `BlurRadius="16"`, `ShadowDepth="4"`, `Opacity="0.45"`, `Color="#000000"`) restrita a elementos flutuantes sobre o canvas (HUD, popups, tooltips, diálogos) `[linear/DESIGN.md, wpf-best-practices]`.

---

## 3. Checklist de Revisão de Tela (Verificável em Captura PNG)

Use esta lista de **42 itens** ao inspecionar qualquer captura de tela (PNG) gerada pelo comando `--screenshot` `[PLANO-V2.md]`:

### A. Hierarquia Visual e Arquitetura de Superfícies (7 itens)
1. [ ] **Escada tonal correta:** O canvas (`#141820`) é visivelmente mais escuro que os painéis (`#242B38`), que são mais escuros que cartões (`#2D3646`) e inputs (`#364154`) `[linear/DESIGN.md]`.
2. [ ] **Ponto focal único:** Ao bater o olho na janela, o elemento mais chamativo é o modelo estrutural/desenho técnico ou o veredito (**Passa / Não Passa**), nunca o cromo ao redor `[taste-skill/app-screens]`.
3. [ ] **Máximo 1 botão primário (`#0EA5E9`) por vista:** Demais ações ("Exportar", "Limpar", "Fechar") usam estilo secundário ou ghost `[ui-ux-pro-max]`.
4. [ ] **Orçamento de cor $\le 5\%$ no cromo:** Nenhum painel lateral, cabeçalho ou barra de ferramentas usa fundo colorido saturado `[taste-skill/app-screens]`.
5. [ ] **Agrupamento por fluxo de trabalho (máx. 3 zonas visíveis):** Ex.: na `JanelaPlaca`, Coluna Esquerda = Entradas em 3 passos, Centro = Desenho 2D/3D, Direita = Veredito + Verificações `[PLANO-V2.md]`.
6. [ ] **Resumo executivo no topo dos resultados:** Antes de qualquer lista de fórmulas, há um banner claro com status geral e utilização máxima (`84%`) `[PESQUISA.md, PLANO-V2.md]`.
7. [ ] **Cabeçalhos de seção discretos:** Títulos de grupo usam `10–11 px` SemiBold em `Text.Secondary` ou barra lateral de `3 px` em `Accent.Primary`, sem faixas pesadas `[PESQUISA.md]`.

### B. Alinhamento, Grade e Densidade CAD (7 itens)
8. [ ] **Zero rolagem vertical na abertura (`1280×720`):** Todos os campos de entrada principais cabem na janela padrão sem `ScrollBar` ativa `[PLANO-V2.md]`.
9. [ ] **Alinhamento de bordas verticais:** Rótulos e inputs de seções empilhadas compartilham exatamente a mesma linha de início (`SharedSizeGroup` ou colunas fixas) `[figma/DESIGN.md]`.
10. [ ] **Alturas de controle uniformes:** Botões, `TextBox` e `ComboBox` na mesma linha têm exatamente `28 px` de altura `[PESQUISA.md]`.
11. [ ] **Grade 2×2 para pares geométricos:** Coordenadas $(X, Y)$, dimensões $(B \times L)$ e espessuras usam 2 pares por linha em vez de 1 campo por linha de ponta a ponta `[PLANO-V2.md]`.
12. [ ] **Inputs numéricos com largura proporcional:** Campos de 3–6 dígitos têm `72–110 px` de largura; nenhum campo numérico estica por `300+ px` `[vercel-guidelines]`.
13. [ ] **Alinhamento tabular em listas e tabelas:** Colunas de texto alinhadas à esquerda; colunas numéricas e ratios alinhadas à direita com decimais alinhados verticalmente `[taste-skill/app-screens]`.
14. [ ] **Respiro estrutural consistente:** Margem externa de `12–16 px` nas bordas da janela e `8–12 px` entre cartões — sem controles colados na borda da janela `[ui-ux-pro-max]`.

### C. Contraste WCAG AA e Legibilidade em Tema Escuro (6 itens)
15. [ ] **Texto primário legível sem ofuscar (`#F1F5F9`):** Nenhum texto longo usa `#FFFFFF` puro nem cinza escuro ilegível sobre fundo `#242B38` `[taste-skill/tells]`.
16. [ ] **Rótulos e unidades com contraste $\ge 4.5:1$ (`#94A3B8` ou `#7C8B9E`):** Unidades (`cm`, `MPa`, `kN`) são imediatamente legíveis ao lado do número `[taste-skill/app-screens]`.
17. [ ] **Chips semânticos em fundo escuro pálido:** Nunca usar texto branco sobre verde/amarelo neon puro; usar texto claro saturado (`#22C55E`, `#F59E0B`, `#F43F5E`) sobre fundo escuro tonal (`#143828`, `#3B2A14`, `#3F1A24`) `[taste-skill/app-screens, sentry/DESIGN.md]`.
18. [ ] **Fórmulas matemáticas nítidas:** Expressões `WpfMath` ou texto técnico renderizados em `#F1F5F9` sobre fundo `#1B202B`/`#242B38` sem serrilhado nem corte vertical `[PESQUISA.md]`.
19. [ ] **Desenho técnico com hierarquia de espessura de linha:** Contorno principal (`2–2.5 px` `#E2E8F0`), perfil/furos (`1.5 px` `#38BDF8`), linhas de cota (`1 px` `#94A3B8`) e eixos tracejados (`1 px` `#475569`) `[PLANO-V2.md]`.
20. [ ] **Cotas sem colisão no canvas:** Textos de cota têm fundo opaco/semi-opaco (`#141820`) sob os algarismos e nunca cruzam linhas do perfil ou furos `[PLANO-V2.md]`.

### D. Estados de Interface e Feedback (7 itens)
21. [ ] **Foco de teclado visível:** O controle focado exibe borda `#38BDF8` (`1.5 px`) nítida `[vercel-guidelines, dotnet-wpf-modern]`.
22. [ ] **Seleção ativa inequívoca:** Ferramenta ativa, aba selecionada ou linha de tabela possui indicador claro (fundo `#2638BDF8` + barra `2–3 px` `#0EA5E9`) `[cursor/DESIGN.md]`.
23. [ ] **Campos somente-leitura vs. editáveis distinguíveis:** Resultados calculados (`As,nec`, `Ratio`) parecem rótulos ou pílulas travadas, nunca `TextBox` editável comum `[PLANO-V2.md]`.
24. [ ] **Estado vazio (*Empty State*) instrutivo:** Painel de resultados antes de calcular ou Inspector sem seleção exibe ícone sutil + texto curto do que fazer (ex.: *"Selecione uma barra ou clique em Calcular"*), nunca um buraco preto vazio `[PESQUISA.md, vercel-guidelines]`.
25. [ ] **Estado de erro inline e construtivo:** Valor inválido (ex.: borda mínima violada) destaca o campo em `#F43F5E` e explica o limite normativo logo abaixo `[vercel-guidelines]`.
26. [ ] **Status semântico duplamente codificado (Cor + Ícone/Texto):** Além de verde/amarelo/vermelho, há sempre `✓ OK`, `⚠ Alerta` ou `✕ Falha` para acessibilidade a daltônicos `[ui-ux-pro-max, PESQUISA.md]`.
27. [ ] **Controles desabilitados (`IsEnabled="False"`) discretos:** Opacidade `0.45` ou texto `#64748B` sem borda brilhante `[ui-ux-pro-max]`.

### E. Textos, Unidades e Microcopy Técnico (6 itens)
28. [ ] **Sentence case em botões e rótulos:** Usar *"Calcular placa"* ou *"Verificar barra"* em vez de *"CALCULAR PLACA"* gritado (CAIXA ALTA restrita a cabeçalhos de seção `10 px` e siglas normativas `ELU`, `ELS`, `NBR`) `[linear/DESIGN.md]`.
29. [ ] **Botões começam com verbo de ação:** *"Calcular"*, *"Aplicar seção"*, *"Exportar DXF"* — nunca rótulos vagos como *"OK"* ou *"Processar"* `[vercel-guidelines]`.
30. [ ] **Toda grandeza física exibe unidade explícita:** Nenhum número aparece órfão; sempre acompanhado de `cm`, `mm`, `kN`, `kN·m`, `MPa` ou `%` `[PESQUISA.md, PLANO-V2.md]`.
31. [ ] **Precisão decimal consistente por coluna:** Coordenadas com 2 casas (`2,50 m`), tensões com 1–2 casas (`25,0 MPa`), taxas de trabalho em inteiro percentual (`84%`) ou 2 casas (`0,84`) `[taste-skill/app-screens]`.
32. [ ] **Separador decimal e símbolos corretos:** Símbolo de multiplicação `×` (`25 × 30 cm`), sinal de menos real `−` (ou `-` tabular), subscritos legíveis ($f_{ck}, f_{yd}, N_{Sd}, M_{Rd}$) `[vercel-guidelines, PLANO-V2.md]`.
33. [ ] **Zero truncamento com reticências (`...`) em dados críticos:** Rótulos e valores de verificação nunca aparecem cortados no meio `[vercel-guidelines]`.

### F. Ícones, Consistência e Anti-Padrões "AI Slop" (9 itens)
34. [ ] **Ícones vetoriais consistentes (`PathIcon` / `Geometry`):** Traço uniforme de `1.5 px`, tamanho `14–16 px` em botões/árvore; zero emojis coloridos (`🚀`, `✅`, `🔥`) na interface `[taste-skill/tells, ui-ux-pro-max]`.
35. [ ] **Zero mistura de temas Claro/Escuro:** Nenhum controle nativo WPF (ex.: `ComboBox` popup, cabeçalho de `DataGrid`, `GroupBox` ou `ScrollBar`) escapa com fundo branco/cinza claro padrão do Windows `[PESQUISA.md]`.
36. [ ] **Zero `GroupBox` legado com linha branca arredondada:** Substituído por cartão escuro plano (`Surface.2`, borda `#333E50` de `1 px`, `CornerRadius="6"`) `[PESQUISA.md]`.
37. [ ] **Zero gradientes decorativos ou *glow* neon:** Sem botões com gradiente roxo-azul, sem sombras coloridas esfumaçadas e sem efeito vidro (*glassmorphism*) em painéis fixos `[taste-skill/tells, frontend-design]`.
38. [ ] **Zero cantos excessivamente redondos (`CornerRadius > 8`):** Proibido usar cartões de `16 px` ou inputs em formato pílula completa que desperdiçam área útil `[warp/DESIGN.md, taste-skill/tells]`.
39. [ ] **Zero gráficos circulares (*gauges*/*donuts*) para taxas de engenharia:** Taxas $N_{Sd}/N_{Rd}$ usam **barras de capacidade horizontais** compactas de `6 px` de altura, que permitem comparar 6 verificações de relance `[taste-skill/tells, PESQUISA.md]`.
40. [ ] **Zero caixas dentro de caixas (aninhamento > 2 níveis):** Não colocar `Border` com borda dentro de `GroupBox` dentro de `TabControl` com borda `[taste-skill/app-screens]`.
41. [ ] **Barras de rolagem finas e escuras (`6–8 px`):** `ScrollBar` customizada discreta que não rouba `17 px` cinza-claro de cada painel `[PESQUISA.md]`.
42. [ ] **Sem texto de ajuda prolixo ocupando tela:** Fórmulas extensas e memória passo-a-passo ficam em `Expander` recolhido por padrão (*"Ver memória de cálculo detalhada"*) `[PLANO-V2.md]`.

---

## 4. Regras Específicas de WPF (`dotnet-wpf-modern` + `wpf-best-practices` + `wpf.csv`)

1. **Arquitetura de Recursos e Estilos Implícitos (`App.xaml`):**
   * Centralize cores, fontes e métricas em `Themes/DesignTokens.xaml` e templates em `Themes/Controls.xaml`, carregados via `MergedDictionaries` `[dotnet-wpf-modern]`.
   * Defina **estilos implícitos** (`TargetType="{x:Type Button}"` sem `x:Key`) baseados em estilos nomeados (`BasedOn="{StaticResource BaseControlStyle}"`) para que nenhum controle caia no visual Aero/Win7 padrão `[wpf.csv]`.
2. **`StaticResource` vs. `DynamicResource`:**
   * Use **`StaticResource`** em $95\%$ dos casos (geometrias, estilos base, conversores, margens e brushes fixos) para menor custo de lookup na árvore visual `[wpf-best-practices]`.
   * Use **`DynamicResource`** apenas nos pincéis de superfície/texto (`Surface.*`, `Text.*`, `Accent.*`) que precisem responder a troca de tema em runtime (ex.: modo de impressão claro vs. tela escura) `[dotnet-wpf-modern, wpf.csv]`.
3. **Congelamento (`Freeze()`) e Renderização Vetorial de Alta Performance:**
   * Adicione `po:Freeze="True"` (`xmlns:po="http://schemas.microsoft.com/winfx/2006/xaml/presentation/options"`) em todos os `SolidColorBrush`, `LinearGradientBrush`, `Pen` e `Geometry` nos dicionários XAML `[wpf-best-practices]`.
   * Em C# (desenho do canvas, diagramas de esforços, desenho da placa de base), chame `.Freeze()` em todo `Brush`, `Pen` e `StreamGeometry` após criá-lo. Objetos `Freezable` congelados são thread-safe e eliminam alocações por frame no `OnRender` `[wpf-best-practices, wpf.csv]`.
   * Prefira `StreamGeometry` (mais leve que `PathGeometry`) ou `DrawingVisual` / `DrawingContext` para o canvas estrutural e diagramas com centenas de barras `[wpf-best-practices]`.
4. **Nitidez de Pixel e Escala High-DPI (`PerMonitorV2`):**
   * Nos painéis e janelas raiz, defina sempre: `UseLayoutRounding="True"`, `SnapsToDevicePixels="True"`, `TextOptions.TextFormattingMode="Ideal"` (ou `"Display"` em `12 px` a 100% DPI) e `TextOptions.TextRenderingMode="ClearType"` `[PESQUISA.md]`.
   * Ao desenhar linhas horizontais/verticais de `1 px` em `Canvas` ou `DrawingContext`, alinhe às meias-coordenadas de dispositivo ou use `GuidelineSet` para evitar linhas borradas de `2 px` em monitores 125%/150% (`PerMonitorV2` no `app.manifest`) `[dotnet-wpf-modern, wpf-best-practices]`.
5. **Virtualização de UI para Tabelas e Listas Grandes:**
   * Em `DataGrid`, `ListView` e `ItemsControl` de nós/barras/combinações, ative `VirtualizingPanel.IsVirtualizing="True"`, `VirtualizingPanel.VirtualizationMode="Recycling"` e `VirtualizingPanel.ScrollUnit="Pixel"` `[dotnet-wpf-modern, wpf-best-practices]`.
   * **Regra crítica:** Nunca coloque um `DataGrid` ou `ListBox` dentro de um `ScrollViewer` externo ou `StackPanel` vertical sem altura limitada — isso destrói a virtualização e instancia milhares de linhas na memória `[wpf-best-practices, wpf.csv]`.
6. **Animações Leves com `Storyboard`:**
   * Anime apenas propriedades baratas para a thread de composição (`Opacity`, `RenderTransform.ScaleTransform`/`TranslateTransform`, `SolidColorBrush.Color`) por `120–160 ms` com `CubicEase EaseOut` `[wpf-best-practices]`.
   * Evite animar `Width`/`Height` em layouts complexos ou animar `DropShadowEffect.BlurRadius` (força re-rasterização via software) `[wpf-best-practices]`.
7. **Acessibilidade (`AutomationProperties`) e Navegação por Teclado:**
   * Todo botão que contém apenas ícone (`Path`) deve definir `AutomationProperties.Name="Descrição da ação"` e `ToolTip="Descrição (Atalho)"` `[dotnet-wpf-modern, vercel-guidelines]`.
   * Defina `KeyboardNavigation.TabNavigation="Local"` por grupo lógico, `IsDefault="True"` no botão primário de cálculo/confirmação, `IsCancel="True"` no botão Fechar/Cancelar, e selecione todo o conteúdo (`SelectAll()`) quando um `TextBox` numérico receber foco via `GotKeyboardFocus` `[vercel-guidelines, PESQUISA.md]`.

---

## 5. Padrões de Componente para o Estructto (12 Padrões WPF)

### 5.1 Botões (Primário, Secundário e Ghost/Toolbar) `[ui-ux-pro-max, linear/DESIGN.md]`
* **Métricas comuns:** `Height="28"`, `Padding="12,0"`, `CornerRadius="4"`, `FontSize="12.5"`, `FontWeight="SemiBold"`, ícone opcional `14×14 px` com margem direita `6 px`.
* **Primário (`PrimaryButtonStyle`):** Fundo `Accent.Primary` (`#0EA5E9`), texto `#0F172A` (ou `#F8FAFC` Bold), borda `#38BDF8` (`1 px`). Hover: `#38BDF8`. Pressed: `#0284C7`. Disabled: fundo `#2D3646`, texto `#64748B`.
* **Secundário (`SecondaryButtonStyle`):** Fundo `Surface.2` (`#2D3646`), borda `Border.Default` (`#475569`, `1 px`), texto `Text.Primary` (`#F1F5F9`). Hover: fundo `Surface.3` (`#364154`), borda `#64748B`.
* **Ghost / Toolbar (`GhostButtonStyle`):** Fundo `Transparent`, borda `Transparent`, texto `Text.Secondary` (`#94A3B8`). Hover: fundo `Surface.2`, texto `Text.Primary`. Ativo (`ToggleButton.IsChecked="True"`): fundo `Accent.Subtle` (`#2638BDF8`), borda `#0EA5E9` (`1 px`), texto `#38BDF8`.

### 5.2 Campo Numérico com Unidade Integrada (`NumericUnitBox`) `[figma/DESIGN.md, PESQUISA.md]`
* **Estrutura XAML:** `Border` (`Height="28"`, `CornerRadius="4"`, `Background="{DynamicResource Surface.3}"`, `BorderBrush="{DynamicResource Border.Default}"`, `BorderThickness="1"`) contendo um `Grid` de 2 colunas (`*` e `Auto`).
* **Coluna 0 (`TextBox` sem borda):** `Background="Transparent"`, `Foreground="#F1F5F9"`, `TextAlignment="Right"`, `VerticalContentAlignment="Center"`, `Padding="6,0,4,0"`, `Typography.NumeralAlignment="Tabular"`. Aceita ponto ou vírgula como separador decimal e expressões simples (`250/2`).
* **Coluna 1 (Unidade):** `TextBlock` (`Text="cm"`, `FontSize="11"`, `Foreground="#94A3B8"`, `VerticalAlignment="Center"`, `Margin="0,0,6,0"`, `IsHitTestVisible="False"`).
* **Estados:** `IsKeyboardFocusWithin="True"` muda `BorderBrush` para `#38BDF8` (`1.5 px`) e seleciona todo o texto; erro de validação muda `BorderBrush` para `#F43F5E`.

### 5.3 Controle Pílula / Segmentado (`SegmentedControl`) `[warp/DESIGN.md, PLANO-V2.md]`
* **Uso no Estructto:** Alternar vista `"2D Planta"` / `"2D Corte"` / `"3D Isométrico"` na `JanelaPlaca`, ou alternar diagrama `"N"` / `"V"` / `"M"` / `"δ"`.
* **Estrutura:** `Border` externo com `Background="#1B202B"`, `BorderBrush="#333E50"`, `BorderThickness="1"`, `CornerRadius="5"`, `Padding="2"`, `Height="28"`.
* **Itens (`RadioButton` customizados):** `CornerRadius="3"`, `Padding="10,0"`, `FontSize="12"`. Inativo: `Background="Transparent"`, `Foreground="#94A3B8"`. Ativo (`IsChecked="True"`): `Background="#2D3646"`, `BorderBrush="#475569"` (`1 px`), `Foreground="#F1F5F9"`, `FontWeight="SemiBold"` (ou `Background="#0EA5E9"` com `Foreground="#0F172A"` quando for filtro de esforço ativo).

### 5.4 Cartão Técnico (`CardBorder`) `[linear/DESIGN.md, PLANO-V2.md]`
* **Estrutura:** `Border` com `Background="{DynamicResource Surface.2}"` (`#2D3646`), `BorderBrush="{DynamicResource Border.Subtle}"` (`#333E50`), `BorderThickness="1"`, `CornerRadius="6"`, `Padding="12"`.
* **Cabeçalho do Cartão:** Barra superior compacta com `Border` lateral esquerda de `3 px` em `Accent.Primary` (`#0EA5E9`), título em `11 px` SemiBold CAIXA ALTA (`#94A3B8`, `Margin="6,0,0,8"`), seguido pelo `Grid` `2×2` de propriedades. Substitui 100% dos `GroupBox` legados.

### 5.5 Chip de Status e Veredito (`StatusChip`) `[sentry/DESIGN.md, PESQUISA.md]`
* **Métricas:** `Border` com `CornerRadius="3"`, `Padding="6,2"`, `BorderThickness="1"`, `VerticalAlignment="Center"`.
* **Variantes semânticas:**
  * **Passa (`OK`):** `Background="#143828"`, `BorderBrush="#1E563B"`, `Foreground="#22C55E"`, texto `"✓ PASSA"`.
  * **Alerta (`Atenção`):** `Background="#3B2A14"`, `BorderBrush="#63451C"`, `Foreground="#F59E0B"`, texto `"⚠ ALERTA"`.
  * **Não Passa (`Falha`):** `Background="#3F1A24"`, `BorderBrush="#6E2536"`, `Foreground="#F43F5E"`, texto `"✕ NÃO PASSA"`.

### 5.6 Barra de Capacidade / Taxa de Utilização (`CapacityBar`) `[PESQUISA.md, PLANO-V2.md]`
* **Uso no Estructto:** Cada verificação normativa na `JanelaPlaca` (Concreto, Flexão da Placa, Chumbadores, Solda, Cisalhamento) e na `JanelaVerificacao` ($N_{Sd}/N_{Rd}$, $M_{Sd}/M_{Rd}$, Flecha $L/250$).
* **Layout (Altura total `44 px` por verificação):**
  * **Linha 1 (`Grid` 3 colunas):** Nome da verificação à esquerda (`12.5 px` Medium `#F1F5F9`), demanda vs. resistência no centro (`"σ_Sd = 14,2 ≤ f_cd = 17,9 MPa"`, `11.5 px` Tabular `#94A3B8`), percentual + chip à direita (`"79%"` Bold Tabular + cor do status).
  * **Linha 2 (Trilho):** `Border` de fundo (`Height="6"`, `CornerRadius="3"`, `Background="#1B202B"`) + `Border` de preenchimento proporcional (`Width = Ratio * LarguraTotal`, limitado a `100%`, `CornerRadius="3"`).
  * **Cor dinâmica por faixa:** $\le 75\%$ `#22C55E` (Verde) | $75\%\text{–}90\%$ `#38BDF8` (Ciano) | $90\%\text{–}100\%$ `#F59E0B` (Âmbar) | $> 100\%$ `#F43F5E` (Coral).

### 5.7 Tabela de Dados (`DataGrid` de Engenharia) `[taste-skill/app-screens, wpf.csv]`
* **Estilo Global:** `Background="#1B202B"`, `RowBackground="#1B202B"`, `AlternatingRowBackground="#1F2532"`, `GridLinesVisibility="Horizontal"`, `HorizontalGridLinesBrush="#293242"`, `HeadersVisibility="Column"`, `RowHeight="28"`, `ColumnHeaderHeight="30"`.
* **Cabeçalho (`DataGridColumnHeader`):** `Background="#242B38"`, `Foreground="#94A3B8"`, `FontSize="11"`, `FontWeight="SemiBold"`, `BorderBrush="#333E50"`, `BorderThickness="0,0,1,1"`, `Padding="8,0"`.
* **Células (`DataGridCell`):** Sem pontilhado de foco preto; seleção usa `Background="#2638BDF8"` e `Foreground="#F1F5F9"`. Colunas de ID/Perfil alinhadas à esquerda; colunas de esforços ($N, V, M$) e ratios alinhadas à direita com `Tabular` numerals e unidade no cabeçalho (`N [kN]`, `M [kN·m]`).

### 5.8 Inspector Contextual (Substituto Nativo do WinForms `PropertyGrid`) `[figma/DESIGN.md, PESQUISA.md]`
* **Largura:** `280–320 px` ancorado à direita.
* **Cabeçalho Contextual:** Exibe ícone do elemento + tipo e ID (`"Barra B12 — W 250 × 32,7"`) + `StatusChip` se já calculada.
* **Corpo:** `ScrollViewer` fino com seções recolhíveis (`Expander` minimalista sem botão redondo legado). Dentro de cada seção, pares dispostos em grade `2×2` (ex.: `Nó Inicial [N1]` | `Nó Final [N4]`, `Comprimento [4,50 m]` | `Material [ASTM A572]`) usando `NumericUnitBox` e `ComboBox` escuros de `28 px`.

### 5.9 Toast / Banner de Aviso Não-Bloqueante `[vercel-guidelines, sentry/DESIGN.md]`
* **Uso:** Feedback imediato para ações rápidas (*"Esforços importados da Barra B4"*, *"Estrutura hipostática: adicione 1 vínculo"*).
* **Layout:** `Border` no topo do painel ou canto inferior direito do canvas (`CornerRadius="6"`, `Padding="12,8"`, `BorderThickness="1"`), usando o par semântico correspondente (`Status.Warning.Bg` `#3B2A14` + borda `#63451C` + ícone `#F59E0B` + texto `#F1F5F9`). Auto-dispensa em `4 s` para avisos informativos; permanece fixo com botão de ação rápida para alertas estruturais.

### 5.10 Diálogo Modal e Janelas de Cálculo (`JanelaPlaca`, `JanelaVerificacao`) `[PLANO-V2.md, PESQUISA.md]`
* **Dimensões e Estrutura:** `1180×700` (mínimo `1024×640`), `Background="#1B202B"`, sem barra de rolagem na janela raiz.
* **Cabeçalho de Contexto (`48 px`):** Fundo `#242B38`, borda inferior `#333E50` (`1 px`), contendo seletor de elemento/caso de carga, resumo dos esforços atuantes ($N_{Sd}, M_{Sd}, V_{Sd}$) em pílulas de leitura rápida e controles de vista (`SegmentedControl`).
* **Corpo em 3 Colunas (`Grid` com `GridSplitter` de `4 px`):**
  1. **Esquerda (`330 px`):** Parâmetros de entrada organizados em 3 cartões sequenciais (Esforços/Materiais → Geometria → Furos/Soldas).
  2. **Centro (`*`, mín. `420 px`):** Canvas CAD (`#141820`) com desenho técnico cotado, legenda de cores no rodapé e botões de zoom/corte no topo.
  3. **Direita (`340 px`):** Banner de Veredito no topo (`PASSA / NÃO PASSA` + maior ratio em `18 px` Bold) + lista de `CapacityBar` das verificações normativas + `Expander` recolhido para a memória de cálculo detalhada.
* **Rodapé (`44 px`):** Fundo `#242B38`, borda superior `#333E50`, botões secundários à esquerda (*"Memorial PDF"*, *"Exportar DXF"*) e primário à direita (*"Aplicar ao Modelo"*).

### 5.11 Estado Vazio (`EmptyStateView`) `[vercel-guidelines, PESQUISA.md]`
* **Estrutura:** Centralizado vertical e horizontalmente no painel ou canvas (`MaxWidth="280"`), composto por:
  1. Ícone vetorial `Path` (`32×32 px`, traço `1.5 px` em `#475569`).
  2. Título curto em `13 px` SemiBold `#94A3B8` (ex.: *"Nenhuma verificação executada"*).
  3. Subtítulo de 1 linha em `11.5 px` `#7C8B9E` indicando a próxima ação (ex.: *"Preencha as dimensões ou pressione F5 para calcular"*).

### 5.12 HUD Flutuante no Canvas (`CanvasHudOverlay`) `[PESQUISA.md, raycast/DESIGN.md]`
* **Canto Superior Esquerdo (Pílula de Modo e Escala de Diagramas):** `Border` com `Background="#E6181C24"`, `BorderBrush="#333E50"`, `BorderThickness="1"`, `CornerRadius="6"`, `Padding="8,4"`. Contém `SegmentedControl` para esforços (`[N] [V] [M] [δ]`) + `Slider` compacto (`Width="90"`) de fator de escala do diagrama + `CheckBox` *"Valores extremos"*.
* **Canto Inferior Esquerdo (Coordenadas e Snap):** Pílula compacta (`Height="24"`, `FontFamily="Cascadia Mono, Consolas"`, `FontSize="11"`, `Foreground="#94A3B8"`) exibindo `X: 4,50 m | Y: 3,00 m | Snap: Nó | Escala: 1:50` com `IsHitTestVisible="False"` para nunca bloquear cliques no modelo.
