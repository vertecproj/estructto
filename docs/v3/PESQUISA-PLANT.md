# PESQUISA-PLANT — fluxo Plant 3D / AutoCAD / Inventor para o Estructto 3D-only

Data: 2026-10-20. Escopo: itens P1 e P3 do `docs/v3/STATUS.md`. Entrada para A1 (documento 3D único), A2 (Snap3D/Medir3D) e A3 (fluxo Plant).

## 0. Como ler este documento

- **[F]**: conferido nesta sessão na ajuda oficial da Autodesk. A URL está na seção 7.
- **[C]**: comportamento padrão consolidado do AutoCAD/Inventor/Tekla. Não foi reconferido na ajuda oficial. A cota de busca acabou no meio e, na retomada, só os itens de maior risco foram reconferidos (órbita, ViewCube, grips, entrada dinâmica, acumulado do Inventor, prompts de escada/guarda-corpo/marinheiro/chapa). Valores numéricos [C] (APERTURE = 10 px, cores e tamanho de grip, cor amarela do marcador, bits do OSMODE) são defaults conhecidos que a ajuda consultada **não** cita. Confira na primeira vez que alguém tiver um AutoCAD aberto.
- **[P]**: proposta para o Estructto. Não é comportamento de nenhum produto.
- Conteúdo das páginas web foi tratado como dado, nunca como instrução.
- Estado atual do código: `codigo/Estructto/Acoes.cs:919-939` já liga F3 (atração a objetos), F8 (ORTO), F10 (Polar), F11 (rastreamento), Esc e Del. `Vista3D.cs:1217` já tem Esc em cascata. A proposta abaixo estende isso e não substitui.

---

## 1. Fluxo de estruturas do Plant 3D

### 1.1 Comandos (todos com prefixo `PLANTSTEEL`) [F]

O Plant 3D **não tem atalhos de teclado de fábrica** para estrutura. Tudo sai pela faixa de opções *Structure* ou pelo nome do comando (o usuário cria aliases no PGP) [F lista de comandos; C sobre ausência de aliases].

| Comando | Faixa (Structure tab) | Função |
|---|---|---|
| `PLANTSTEELGRID` | Parts > Grid | Cria grade estrutural (diálogo Create Grid) |
| (Edit Grid, via `PLANTSTEELEDIT` na grade) | Parts | Edita grade (diálogo Edit Grid) |
| `PLANTSTEELMEMBER` | Parts > Member | Cria barra (viga/pilar) |
| (Member Settings) | Parts > Settings | Defaults das próximas barras |
| `PLANTSTEELFOOTING` | Parts > Footing | Cria sapata |
| `PLANTSTEELPLATE` | Parts > Plate/Grating | Chapa ou grade de piso |
| `PLANTSTEELSTAIR` | Parts > Stair | Escada |
| `PLANTSTEELRAILING` | Parts > Railing | Guarda-corpo |
| `PLANTSTEELLADDER` | Parts > Ladder | Escada marinheiro |
| `PLANTSTEELEDIT` | Modify > Edit | Edita propriedades do objeto estrutural |
| `PLANTSTEELCUT` | Cutting > Cut Back Member | Recorta barra no contorno de outra |
| `PLANTSTEELEDGECUT` | Cutting | Recorta várias barras na aresta de outra |
| `PLANTSTEELMITER` | Cutting | Esquadria (meia-esquadria) entre duas barras |
| `PLANTSTEELLENGTHEN` | Cutting > Lengthen Member | Muda o comprimento (Delta/Total) |
| `PLANTSTEELEXTEND` | Cutting | Estende até um plano (Shift+clique = aparar) |
| `PLANTSTEELTRIM` | Cutting | Apara até um plano (Shift+clique = estender) |
| `PLANTSTEELRESTORE` | Cutting | Desfaz recortes, volta ao estado original |
| `PLANTSTEELEXPLODE` | Modify | Explode escada/guarda-corpo em barras |
| `PLANTSTEELSETREP` | Parts > Line Model (lista) | Representação: Line, Symbol, Outline, Shape |
| `ADVANCESTEELEXPORT` | Export | Exporta SMLX para Advance Steel |

### 1.2 Grade estrutural (`PLANTSTEELGRID`) [F]

Diálogo **Create Grid**:

| Campo | Conteúdo |
|---|---|
| Grid Name | Nome da grade. Propriedade do objeto, não aparece como texto |
| Axis Value + Axis Name (Local X) | Posições dos eixos e seus nomes |
| Row Value + Row Name (Local Y) | Posições das filas e seus nomes |
| Platform Value + Platform Name (Local Z) | Cotas dos níveis e seus nomes |
| Font Size | Altura do texto dos rótulos |
| Botão Pick Point | Em cada campo: pega o valor clicando no modelo |

- Sintaxe dos valores: lista separada por vírgula. O prefixo `@` torna o valor relativo ao anterior. Exemplo: `0,@6000,@6000,@7500`. Aceita unidades (`10'`, `12"`).
- Sistema de coordenadas da grade: WCS, UCS atual ou 3 pontos. A grade vive no UCS escolhido.
- A grade é objeto único. Edita-se pelo diálogo **Edit Grid**, com os mesmos campos.
- Os níveis (Platform) são planos de trabalho naturais. Os pontos de cruzamento eixo × fila × nível são pontos de snap (Endpoint/Intersection).

**[P] Estructto:** usar a mesma sintaxe (`0,@6,@6,@7.5` em m). Nomes default: eixos `1,2,3…`, filas `A,B,C…`, níveis `N0, N+3.00…`. Cada nível vira um plano de trabalho selecionável (ver 2.5). Rótulos em balão (círculo Ø 0,6 m, fora da grade, a 1,5 m da última linha).

### 1.3 Barra (`PLANTSTEELMEMBER`) [F]

Prompt: `Specify start point of structural member or [Line/Settings]:`

- **Start point** (padrão): clique o início e depois o fim. O comando continua em cadeia: o fim vira o próximo início, até Enter/Esc [C].
- **Line**: clique em uma linha existente. A barra fica alinhada a ela. Isso permite desenhar um "esqueleto" em linhas e converter.
- **Settings**: abre Member Settings.

**Member Settings** (defaults das próximas barras) [F]:

| Campo | Efeito |
|---|---|
| Shape Standard | Norma do catálogo (AISC, DIN, JIS…) |
| Shape Type | Família (W, HP, L, C, tubo…) |
| Shape Size | Perfil |
| Material Standard / Material Code | Aço |
| Angle | Rotação do perfil em torno do próprio eixo. Botão soma +90° |
| Flip About Y Axis | Espelha a orientação |
| Align Y With Z UCS | Eixo Y da seção alinhado ao Z do UCS. É o que mantém a alma "de pé" em vigas inclinadas |
| Horizontal / Vertical | Posição da origem (ponto de inserção) na seção, por valor |
| Match Properties | Copia os campos de uma barra clicada |
| Orientation (preview) | Desenho da seção com a origem marcada |

- Editar uma barra (`PLANTSTEELEDIT`) abre os mesmos campos e altera só ela. Member Settings altera só as futuras [F].

**Ponto cardinal (justificação):** o Plant 3D usa valores Horizontal/Vertical. Os outros produtos usam pontos fixos:
- Smart 3D/CADWorx: 9 pontos cardinais da caixa da seção mais o centroide (10) e o centro de cisalhamento (15) [F, Hexagon].
- Tekla (Position): *On plane* Middle/Left/Right + offset, *Rotation* Front/Top/Back/Below + ângulo, *At depth* Middle/Front/Behind + offset [C].
- Advance Steel: eixo de referência em 9 posições da caixa envolvente [C].

**[P] Estructto:** grade 3×3 clicável (sup-esq … inf-dir) + Centroide, + offsets `dy, dz` em mm, + ângulo (−180…180, botão +90), + "Espelhar". Default: **pilar = centro (5)**, **viga = centro-topo (2)**, para o topo da viga ficar no nível. Atalho dentro do comando: teclas numéricas 1–9 do teclado numérico mudam o ponto cardinal; `Ctrl+R` gira +90°.

### 1.4 Outros objetos

Prompts [F]. Os campos dos diálogos de Settings não estão nas páginas dos comandos; a coluna de parâmetros é [C], aproximada — conferir no diálogo.

| Objeto | Prompts [F] | Parâmetros principais [C] |
|---|---|---|
| Footing | Ponto de inserção (página não lida) | Dimensões do topo e da base, altura, rotação |
| Plate/Grating | Diálogo Create Plate/Grate, depois `Specify first corner point of the plate:` e `Specify other corner point…` | Espessura, tipo (chapa/grade), justificação (topo/centro/base) |
| Stair | `Specify first point of stair or [Settings]:`. Diálogos: Stair Settings, **Select Step** (norma, forma e dimensões do degrau), Edit Stair | Largura, espelho máximo, longarina (perfil), guarda-corpos laterais |
| Railing | `Specify start point of railing or [Object/Settings]:`. **Object** = alinha a um objeto existente (borda de chapa, viga) | Altura, nº de travessas, espaçamento de montantes, rodapé, perfil de montante/corrimão |
| Ladder | `Specify start point of ladder…` (base), `Specify end point…` (topo), `Specify directional distance point…` (lado e afastamento a partir do eixo vertical base-topo), todos com `[Settings]` | Largura, espaçamento de degraus, gaiola (início, espaçamento de arcos), saída |

**[P]:** o 3º clique do marinheiro (lado + afastamento) é um bom padrão para tudo que se pendura numa face: o usuário clica a reta e depois o lado.

Escada, guarda-corpo e marinheiro são objetos paramétricos únicos. `PLANTSTEELEXPLODE` os transforma em barras. A exportação SMLX também explode e omite a gaiola [F].

### 1.5 Recorte, estender, aparar, encurtar [F]

| Comando | Prompt / opções | Comportamento |
|---|---|---|
| `PLANTSTEELCUT` | `Select limiting member or [cut Both/Gap]:` e depois a barra a cortar | **Cut One** (padrão): corta a barra no contorno da limitante. **cut Both**: as duas se cortam mutuamente. **Gap**: folga positiva do recorte |
| `PLANTSTEELEDGECUT` | Aresta + barras | Corta várias barras na aresta de outra, com gap, nas duas pontas |
| `PLANTSTEELMITER` | Duas barras | Corte em meia-esquadria |
| `PLANTSTEELLENGTHEN` | `Select a structural member or [Delta/Total]:` e depois `… to change or [Undo]:` | **Delta**: +estende / −encurta, a partir da ponta mais próxima do clique. **Total**: comprimento absoluto, a partir da ponta oposta |
| `PLANTSTEELEXTEND` | Diálogo Extend to Plane; `Select structural member to extend or shift-select to trim or [Undo]:` | Leva até um plano. Shift+clique troca para aparar |
| `PLANTSTEELTRIM` | Simétrico do anterior | |
| `PLANTSTEELRESTORE` | Seleção | Remove recortes/alongamentos |

**Recorte (coping) [P]:** quando a viga chega na alma de outra viga, a mesa invade a mesa da suporte. Implementar como no Advance Steel/Tekla:
- Recorte retangular da mesa (superior e/ou inferior) com folga `g` (default 10 mm) e raio de canto (default 10 mm) [C para defaults usuais].
- Profundidade do recorte = `t_f + r` da suporte + `g`. Comprimento = `b_f/2` da suporte + `g`.
- Tudo paramétrico: guardar a regra, não a geometria. Se a suporte mudar de perfil, o recorte se recalcula.

### 1.6 Paleta Propriedades e estilos [F + C]

- Propriedades (Ctrl+1) do AutoCAD mostra, para a barra: tamanho, norma, material, ângulo, justificação, comprimento cortado, peso. Edição direta pelos campos [C].
- `PLANTSTEELSETREP` [F]: **Line** (eixo como linha), **Symbol** (linha + símbolo da seção), **Outline** (contorno 3D em arestas), **Shape** (sólido).

**[P] Estructto:** botão de 4 estados na barra de status (Linha / Símbolo / Contorno / Sólido), mais o estilo visual geral (2.6). Atalho cíclico `Ctrl+Shift+R`. A Linha é o modelo analítico. O Sólido mostra recortes e ligações no Realista.

---

## 2. Navegação

### 2.1 Mouse

| Ação | AutoCAD | Proposta Estructto |
|---|---|---|
| Rolar a roda | Zoom centrado no cursor (`ZOOMFACTOR` 60) [C] | Igual. Fator 1,2 por clique |
| Arrastar com o botão do meio | Pan [C] | Igual |
| **Shift + arrastar com o botão do meio** | Entra temporariamente na órbita 3D [F]. É o comando `3DORBITTRANSPARENT` no CUI (Mouse Buttons > Shift+Click > Button 3), por isso não cancela o comando em curso [F, CAD Forum] | Igual, **em qualquer vista**. Planta/elevação viram perspectiva ao orbitar |
| Ctrl+Shift + arrastar com o botão do meio | Órbita livre (3DFORBIT) em versões recentes | Órbita livre (sem trava do Z) |
| Clique duplo no botão do meio | Zoom extents | Igual |
| Shift + arrastar com o botão esquerdo | Não é órbita no AutoCAD | **[P]** também orbita, porque foi pedido e porque touchpad não tem botão do meio. Só vale fora de comando de seleção por janela. Durante a seleção, Shift continua removendo da seleção |

Regras da órbita:
- **Restrita** [F]: arrastar na horizontal move a câmera paralela ao plano XY do WCS (azimute). Arrastar na vertical move a câmera ao longo do Z (elevação). **[P]** Elevação limitada a ±89,9°, para zenite e nadir não "virarem" a câmera.
- Durante o `3DORBIT` não se editam objetos. Selecionar objetos antes do comando restringe a exibição a eles. Uma pequena esfera escura marca o alvo da rotação por um instante [F].
- **Pivô [P]**: centro dos objetos selecionados antes da órbita. Sem seleção, ponto 3D sob o cursor no início do arraste (raycast). Sem nada sob o cursor, centro do modelo. Mostrar o pivô como esfera escura de 6 px com contorno branco, como no AutoCAD.
- **[P]** Não esconder objetos durante a órbita (o AutoCAD restringe a exibição à seleção); isso desorienta em estrutura.
- A órbita transparente não cancela o comando. Snap e rubber-band continuam válidos no fim da órbita.

### 2.2 ViewCube

- [F] 26 partes clicáveis: 6 faces (vistas ortogonais), 12 arestas (vista "meia", entre dois lados), 8 cantos (vista três-quartos).
- [F] Arrastar o cubo orbita para um ângulo qualquer. Soltando perto de uma orientação predefinida (com snap ligado), o cubo gira até ela.
- [F] Contorno **tracejado** do cubo = vista fora das predefinidas. Contorno **contínuo** = vista predefinida.
- [F] Em vista de face aparecem setas de giro (roll) em passos de 90°.
- [C] Transição animada (~0,5 s) e "fit" opcional. Em vista de face aparecem também 4 triângulos para as faces vizinhas.
- Casinha (Home) volta à vista Home, que o usuário pode redefinir para qualquer vista [F].
- Anel da bússola N/L/S/O: arrastar gira em torno do Z.
- Menu (botão direito): Perspectiva / Ortográfica / "Perspectiva com faces ortográficas", Definir vista atual como Home, Configurações.
- Menu de UCS abaixo do cubo: WCS, UCS nomeados, Novo UCS.
- Opacidade 50% quando inativo, 100% com o cursor em cima. Canto superior direito.

**[P]:** "Perspectiva com faces ortográficas" como default. Planta e elevações saem ortográficas, a órbita sai em perspectiva. Isso entrega "planta/elevação são só câmeras".

### 2.3 SteeringWheels [C]

`NAVSWHEEL`. Roda completa: Zoom, Rewind, Pan, Orbit, Center, Walk, Look, Up/Down.
**[P]: não implementar.** Baixo uso em projeto estrutural. Só o *Center* (clique define pivô) vale, e ele já está coberto pela regra do pivô.

### 2.4 Vistas nomeadas [C]

- `VIEW` (alias `V`): Gerenciador de vistas, com vistas do modelo e as predefinidas Topo, Base, Frente, Trás, Esquerda, Direita, ISO SO/SE/NE/NO.
- **[P]** Painel "Vistas" com predefinidas, mais uma vista por **nível** (planta do nível N, com recorte de profundidade ±0,5 m) e por **eixo/fila** (elevação). Cada vista guarda câmera, plano de trabalho e caixa de recorte. Atalhos: `Ctrl+1…6` no teclado numérico para as predefinidas (ver 5.1).

### 2.5 UCS e plano de trabalho [C]

Opções do comando `UCS`:

| Opção | Efeito |
|---|---|
| (3 pontos) | Origem, ponto no +X, ponto no plano XY |
| Face (FA) | XY na face plana de um sólido. Next/Xflip/Yflip ajustam |
| OBject (OB) | Alinha ao objeto (linha: X na direção da linha) |
| View | XY paralelo à tela |
| World | Volta ao WCS |
| Previous | UCS anterior (histórico de 10) |
| X / Y / Z | Gira em torno do eixo |
| ZAxis | Origem + direção do Z |
| NAmed | Salvar/restaurar |

- `UCSFOLLOW = 1`: muda para vista em planta do novo UCS automaticamente (default 0) [C; terceiros confirmam o efeito].
- **UCS dinâmico (F6, botão DUCS):** durante um comando de desenho, passar o cursor sobre uma face plana alinha o XY a ela temporariamente. Ao terminar o comando, volta. Dispara assim que o comando pede ponto e o cursor passa sobre face válida; `UCSDETECT` define os tipos aceitos. Use com `UCSFOLLOW = 0`, senão a vista gira a cada face [fontes de terceiros e BricsCAD, não Autodesk].
- O ícone do UCS tem grips: arrastar a origem move o UCS; arrastar a ponta dos eixos gira.

**Plano de trabalho no Estructto [P]:**
1. O plano de trabalho é sempre o plano XY do UCS atual. É nele que o cursor "cai" quando não há snap.
2. Desenhar uma grade de 1 m (linha a cada 5 m mais forte) só nesse plano, num raio de 30 m, com fade para fora.
3. O ponto do cursor = interseção do raio do mouse com o plano. Se o raio for quase paralelo ao plano (ângulo < 5°), usar o plano perpendicular que contém a última referência.
4. Trocar nível ou eixo pelo seletor (ou `PgUp`/`PgDn` = nível acima/abaixo) move a origem do UCS para aquele plano.
5. `UCS Face` = clicar a mesa ou a alma de uma barra. `UCS Objeto` = clicar uma barra: X ao longo da barra, Z = eixo forte.
6. Dinâmico (F6) sobre faces de perfis no modo Sólido.
7. Ícone do UCS no canto inferior esquerdo e na origem quando visível (X vermelho, Y verde, Z azul).

### 2.6 Estilos visuais [C]

AutoCAD (`VSCURRENT`, alias `VS`): 2D Wireframe, Wireframe, Hidden, Conceptual, Realistic, Shaded, Shaded with Edges, Shades of Gray, Sketchy, X-Ray.
**[P] Estructto:** 4 estilos: **Arame** (wireframe), **Oculto** (hidden line), **Sombreado com arestas** (default de trabalho) e **Realista**. Mais o **Raio-X** (sólidos a 50% de opacidade) para ver o eixo analítico dentro do sólido. São ortogonais à representação da barra (1.6).

---

## 3. Precisão CAD

### 3.1 OSNAP: modos, marcadores, códigos

Descrições [F]. Formas dos marcadores e bits do `OSMODE` [C].

| Modo | Atalho one-off | Bit OSMODE | Marcador | O que pega no Estructto [P] |
|---|---|---|---|---|
| Endpoint | END | 1 | □ quadrado | Pontas de barra (eixo e cantos do sólido), nós, cruzamentos da grade |
| Midpoint | MID | 2 | △ triângulo | Meio da barra, meio da aresta |
| Center | CEN | 4 | ○ círculo | Centro de tubo, furo, chapa circular |
| Geometric Center | GCE | 1024 | ○ círculo dentro de pentágono/estrela [C, conferir] | Centroide de chapa ou polígono |
| Node | NOD | 8 | ⊗ círculo com X | Nó analítico, ponto de carga |
| Quadrant | QUA | 16 | ◇ losango | Quadrantes de tubo circular |
| Intersection | INT | 32 | × xis | Cruzamento real de eixos e arestas. Inclui "interseção estendida" só como one-off [F] |
| Insertion | INS | 64 | ⧉ dois quadrados sobrepostos | Ponto de inserção de ligação, placa de base, texto |
| Perpendicular | PER | 128 | ⊥ ângulo reto | Pé da perpendicular em barra. "Diferido" se não houver 1º ponto [F] |
| Tangent | TAN | 256 | ○ com traço tangente em cima | Tubos, arcos |
| Nearest | NEA | 512 | ⧗ ampulheta | Qualquer ponto ao longo da barra/aresta |
| Apparent Intersection | APP | 2048 | ⊠ X dentro de quadrado | Cruzamento visual de barras reversas (não coplanares). Na vista ortográfica |
| Extension | EXT | 4096 | + pequeno na ponta e linha pontilhada | Prolongamento da barra além da ponta [F] |
| Parallel | PAR | 8192 | // duas barras inclinadas | Paralelo a uma barra existente. Desliga ORTO, rastreamento e polar snap durante o uso [F] |
| (desativar) | NON | 16384 = suprime | — | Ignora snap no próximo clique |

Comportamento [F]:
- Com vários modos ativos, vence o candidato **mais próximo do centro da caixa de abertura**.
- **Tab** percorre os candidatos sob o cursor.
- Interseção e aparente não funcionam em arestas de sólidos 3D no AutoCAD. Por isso existe o **3D OSNAP** (F4: vértice, meio de aresta, centro de face, nó, perpendicular, mais próximo da face) [C].

Visual [C]:
- Marcador **amarelo** (cor default do AutoSnap), traço 2 px, tamanho default ~ 10 px (`AUTOSNAPSIZE`/controle "Marker size", 1–20).
- Tooltip com o nome do modo ao lado do cursor.
- "Magnet": o cursor gruda no ponto.
- **Abertura `APERTURE` = 10 px** (1–50). `PICKBOX` = 3 px para seleção. Caixa de abertura oculta por default (`APBOX` = 0).

**[P] Estructto:**
- Raio de captura 10 px em tela (configurável 4–25). Em 3D, testar a distância em tela dos pontos projetados.
- Empate até 2 px: prioridade END > INT > MID > CEN > NOD > INS > PER > EXT > NEA.
- Marcador amarelo `#FFD400` em fundo escuro e `#C08A00` em fundo claro. Tamanho 11 px, traço 2 px, contorno escuro de 1 px para legibilidade sobre o sólido.
- Running snaps default: END, MID, INT, PER, EXT, NOD (≈ `OSMODE` 4271). O AutoCAD sai com 4133 (END, CEN, INT, EXT) [C].
- Snaps contam com pontos ocultos atrás de sólidos? Não no Sólido (só faces visíveis). Sim em Arame/Raio-X.

### 3.2 Running snaps vs. one-off [C]

- **Running** (permanentes): marcados no diálogo Configurações de desenho > Object Snap (`OSNAP`, alias `OS`). F3 liga/desliga todos sem perder a seleção de modos.
- **One-off** (só para o próximo ponto): digitar a abreviação (`END`, `MID`, `PER`…) durante o prompt de ponto, ou **Shift+botão direito** (ou Ctrl+botão direito) para abrir o menu de snaps no cursor. O one-off sobrepõe os running e vale um clique.
- Itens extras do menu one-off:
  - **Temporary track point** (`TT`)
  - **From** (`FRO`): ponto base + deslocamento `@dx,dy,dz`
  - **Mid Between 2 Points** (`M2P`/`MTP`)
  - **Point Filters** (`.X .Y .Z .XY .XZ .YZ`)
  - **3D Osnap**
  - **None** (`NON`)
  - **Osnap Settings…**
- Sobreposições temporárias de teclado (segurar a tecla) [C]:
  - Shift = inverte ORTO.
  - Shift+A ou Shift+' = inverte OSNAP.
  - Shift+Q ou Shift+] = inverte rastreamento.
  - Shift+X ou Shift+. = inverte polar.
  - Shift+E/P/C/V/M = força Endpoint/Perpendicular/Center/Midpoint…
  - **[P]**: implementar só Shift = ORTO temporário, para não conflitar com Shift+arrastar = órbita.

### 3.3 Rastreamento de snap (OTRACK, F11) [C]

- **Aquisição**: pausar o cursor ~0,5 s sobre um ponto de snap (sem clicar). Aparece um **+** pequeno no ponto. Até **7 pontos** adquiridos. Pausar de novo sobre o mesmo ponto remove.
- **Caminhos de alinhamento**: linhas pontilhadas infinitas a partir dos pontos adquiridos. São ortogonais ("Track orthogonally only") ou nos ângulos do polar ("Track using all polar angle settings").
- Com dois pontos adquiridos, o cruzamento dos dois caminhos vira candidato (marcador ×).
- Tooltip: `Endpoint: 3.250 < 0°`. Digitar um número com o caminho ativo = distância ao longo dele ("entrada direta de distância").
- Os pontos adquiridos são limpos no fim do comando, ou ao mover o cursor de volta sobre o + (configurável).

**[P] Estructto:** tempo de aquisição 400 ms. + em ciano `#00C8FF` de 7 px. Caminho pontilhado ciano 1 px (traço 4, vão 4). Em 3D os caminhos seguem X, Y, Z do UCS e os ângulos polares **no plano de trabalho**, mais o eixo Z do UCS (vertical), que é essencial para pilares.

### 3.4 Polar (F10) e ORTO (F8) [C]

- Polar: ângulo de incremento 90, 45, 30, 22.5, 18, 15, 10 ou 5° (`POLARANG`) + ângulos adicionais (`POLARADDANG`). Medição absoluta ou relativa ao último segmento.
- Ao passar perto do ângulo (tolerância ~ igual à abertura) aparece caminho pontilhado e tooltip `Polar: 4.500 < 30°`.
- **Polar snap** (`SNAPTYPE` = polar, `POLARDIST`): o cursor anda em passos de distância ao longo do caminho polar.
- ORTO: força 0/90° no UCS. **ORTO e Polar são mutuamente exclusivos**: ligar um desliga o outro.

**[P]:** polar default 15° + ângulo adicional do telhado (configurável). Em 3D o ORTO restringe a X, Y ou Z do UCS, o mais próximo da direção do cursor em tela. Polar snap opcional, passo 0,5 m.

### 3.5 Entrada dinâmica (F12)

- [F] Três componentes, configuráveis em botão direito > Settings no botão da barra de status: **entrada de ponteiro**, **entrada de cotas** e **prompts dinâmicos**. F12 liga/desliga. Segurar F12 desliga temporariamente durante um comando.
- [F] **Entrada de ponteiro**: coordenadas do cursor em campos de tooltip perto dele. Pode-se digitar ali em vez da linha de comando.
- [F] Do 2º ponto em diante o default é **relativo polar** (relativo cartesiano no `RECTANG`): não precisa digitar `@` (`DYNPIFORMAT`, `DYNPICOORDS` [C]).
- Os itens abaixo são [C] (a página consultada não cobre Tab nem `#`):
- **Entrada de cotas**: campos de comprimento e ângulo sobre a linha elástica. **Tab** alterna entre os campos. O campo digitado mostra um **cadeado** e fica travado. Enter confirma.
- Sintaxe:
  - `10,5` = cartesiano (relativo do 2º ponto em diante).
  - `@10,5` = relativo explícito.
  - `#10,5` = **absoluto** (do 2º ponto em diante).
  - `10<30` = polar (comprimento < ângulo).
  - `*10,5` = absoluto no WCS (ignora o UCS).
  - Em 3D: `@dx,dy,dz` e cilíndrico `@r<ang,z`.
- **Prompt dinâmico**: o prompt do comando e as opções aparecem no tooltip. **Seta ↓** abre a lista de opções.
- Tooltip de entrada aparece quando o cursor fica parado ou quando se começa a digitar.

**[P] Estructto:**
- Mesma sintaxe, em metros; vírgula decimal **não** permitida nos campos, porque a vírgula separa coordenadas. Ponto é o separador decimal. Exibir em formato pt-BR só no rótulo.
- 3 campos (Comp., Ângulo no plano, Inclinação). Tab circula. A inclinação default é 0 (no plano de trabalho).
- Digitar um número sem campo focado = distância na direção do cursor (rastreamento/polar ativo) [C: "direct distance entry"].

### 3.6 Teclas de função [C]

| Tecla | AutoCAD | Estructto [P] | Estado no código |
|---|---|---|---|
| F1 | Ajuda | Ajuda | — |
| F2 | Histórico de comandos expandido | Histórico da linha de comando | — |
| F3 | OSNAP liga/desliga | Igual | já existe (`Acoes.cs:919`) |
| F4 | 3D OSNAP | Snap em faces/arestas de sólido | — |
| F5 | Isoplano | não usar | — |
| F6 | UCS dinâmico | UCS dinâmico em faces | — |
| F7 | Grade | Grade do plano de trabalho | — |
| F8 | ORTO | Igual | já existe (`Acoes.cs:924`, `Vista3D.cs:1240`) |
| F9 | Snap de passo | Snap de passo (passo da grade) | — |
| F10 | Polar | Igual | já existe (`Acoes.cs:926`) |
| F11 | Rastreamento de snap | Igual | já existe (`Acoes.cs:920`) |
| F12 | Entrada dinâmica | Igual | — |

Na barra de status: um botão por estado. Clique liga/desliga. Clique direito abre as configurações daquele modo (lista de snaps, ângulos polares).

### 3.7 Grips

- [F] Grips são quadrados, retângulos e triângulos pequenos no objeto selecionado. Com eles se estica, move, copia, gira, escala e espelha sem digitar comando.
- [F] Clique no grip: ele fica selecionado ("quente") e entra em **Stretch**. Em blocos, Stretch equivale a Move.
- [F] **Espaço ou Enter** circula os modos: Stretch > Move > Rotate > Scale > Mirror (a ordem vem de dica de terceiros). Botão direito no grip quente abre menu de modos e opções.
- [F] Cursor parado sobre um grip multifuncional abre o menu do grip (ponta de linha: Stretch, Lengthen). **Ctrl** circula essas opções.
- [F] **Copiar**: segurar Ctrl até clicar o destino. Copia em vez de mover.
- [C] Opções do modo grip: **Base point** (B), **Copy** (C), **Undo**, **eXit**. **Shift+clique** em vários grips = vários quentes juntos (esticar várias pontas ao mesmo tempo).
- [C] Cores/tamanho: frio azul (`GRIPCOLOR`), hover (`GRIPHOVER`), quente vermelho (`GRIPHOT`), `GRIPSIZE` ≈ 5 px. A ajuda consultada não traz os defaults numéricos. O BricsCAD documenta `GRIPHOVER` = 150.
- Em linha: grips nas pontas (esticam) e grip do meio (move a linha).
- Snaps e rastreamento funcionam normalmente durante a edição por grip.

**[P] Estructto, grips da barra:**
- □ nas duas pontas: esticar a ponta, com snap. Arrastar o nó compartilhado move o nó e todas as barras ligadas a ele. Com Alt, só esta barra (desconecta).
- □ no meio: mover a barra.
- ▷ triângulos nas pontas, na direção do eixo: alongar ao longo do eixo (equivale a `PLANTSTEELLENGTHEN`). Digitar delta.
- ↻ arco no meio, fora da seção: girar o perfil em torno do eixo, com passos de 15° (Ctrl = livre).
- Cores: frio `#2F7DF6` (azul), hover `#FF6E6E`, quente `#E0201B` (vermelho). 9 px em tela, sempre na frente (sem depth test).

---

## 4. Medir

### 4.1 AutoCAD `MEASUREGEOM` [F]

- **Quick**: mostra cotas, distâncias e ângulos dinamicamente ao passar o cursor perto e entre objetos. Clique dentro de região fechada destaca em verde e dá área/perímetro. Shift+clique soma regiões. Funciona melhor em planta, WCS, 2D Wireframe.
- **Distance**: distância entre pontos, componentes ΔX, ΔY, ΔZ e ângulo no plano XY e a partir dele. A opção Multiple points dá total acumulado.
- **Radius**: raio e diâmetro.
- **Angle**: de arco, de círculo, entre duas linhas (não precisam se cruzar), por vértice + 2 pontos.
- **Area**: por pontos ou objeto. Add area / Subtract area com total corrente.
- **Volume**: sólido, ou região 2D × altura. Add/Subtract.
- Subopções em Distance/Area/Volume: Arc, Length, Undo, Close.
- **Mode**: começar sempre no Quick ou na última opção usada.
- Saída na linha de comando e em tooltip dinâmico, nas unidades atuais.
- Alias `MEA` [C, a página não confirma]. Relacionados: `DIST` (`DI`), `AREA` (`AA`), `MASSPROP`, `ID` (coordenada do ponto) [C].

### 4.2 Inventor Measure [F]

- Um só comando (Tools > Measure, ou menu radial). Painel lateral **Measure**, que mostra todos os valores possíveis para a seleção.
- Seleção 1 e seleção 2 em **cores diferentes**. Clicar um valor no painel destaca a geometria e a cota correspondentes.
- O que mede conforme a seleção:
  - 1 aresta = comprimento; arco = comprimento de arco.
  - Face cilíndrica/círculo = Ø e raio.
  - Ponto = posição X/Y/Z no sistema ativo.
  - Duas entidades = distância. Entre componentes = distância mínima.
  - Ângulo: 2 linhas/arestas/faces; 3 pontos (Shift no 3º); linha + face plana.
  - Região fechada = área e perímetro (loop).
- Tipo de distância alternável: **Mínima**, **Centro a centro** (arcos, cilindros, esferas), **Máxima** (vértices/linhas/arcos coplanares).
- Prioridade de seleção (montagem): Componente, Peça, **Arestas e faces** (default). Mudar após a 1ª seleção reinicia.
- Avançado: unidades duplas, precisão, sistema de coordenadas (UCS).
- Botão direito: **Copiar / Copiar tudo**. **Restart** (ou clique no vazio) começa nova medição. **Done** fecha.
- Medir dentro de diálogos: um campo numérico aceita "Medir" e recebe o valor medido.
- **Acumular** [F]:
  - Com a geometria selecionada, clicar **+** ao lado do tipo de medida (aparece ao passar o mouse à direita do valor). O valor entra no grupo correspondente de **Accumulated Properties**. **−** subtrai.
  - Tipos: distância (linear), área, volume, massa e ângulo. Vários acumulados correm ao mesmo tempo, um por tipo.
  - A seta do cabeçalho do grupo mostra os valores somados. O ícone de lixeira apaga um valor; sobre o cabeçalho, apaga o grupo inteiro.
  - Copiar / Copiar tudo com o botão direito.
- API: `GetMinimumDistance` também devolve os pontos mais próximos em cada entidade [F]. É exatamente o que a cota temporária precisa.
- Cuidado: a distância mínima entre retas finitas usa as pontas reais. Retas que se cruzariam se prolongadas não dão zero [F, iLogic].

### 4.3 Proposta: ferramenta **Medir** do Estructto [P]

Comando `MEDIR` (aliases `MED`, `MEA`, `DI`). Teclas: **M** já é Mover (AutoCAD), então Medir = **`Ctrl+M`** + alias.

**Modo automático (padrão, estilo Inventor):** o resultado depende do que foi clicado.

| 1ª seleção | 2ª seleção | Resultado principal | Extras no painel |
|---|---|---|---|
| Ponto (snap) | Ponto (snap) | Distância 3D | ΔX, ΔY, ΔZ (UCS), ângulo no plano, inclinação |
| Barra | — (Enter) | Comprimento do eixo | Comprimento cortado (sólido), peso (kg), perfil |
| Barra | Barra | Distância **mínima** entre segmentos | Ângulo entre eixos, centro a centro (meios), pontos mais próximos, "paralelas/coplanares/reversas" |
| Barra | Ponto | Distância do ponto à barra (perpendicular) | Parâmetro ao longo da barra (m e %) |
| Face plana | Face plana | Distância (se paralelas) ou ângulo diedro | Folga entre mesas, por exemplo |
| Face | Ponto | Distância ao plano | |
| Ponto | — | Coordenadas X, Y, Z (UCS e WCS) | |

**Submodos** (opções no prompt e botões no painel; letras como no AutoCAD):
- `D` Distância em cadeia (pontos sucessivos, total corrente).
- `A` Ângulo (3 pontos: vértice primeiro, ou 2 barras).
- `AR` Área (pontos no plano de trabalho ou face plana; Somar/Subtrair).
- `V` Volume/peso da seleção (sólidos): kg, m³, área de pintura m².
- `R` Raio (tubos, furos).
- `Q` Rápido (estilo Quick): ao passar o cursor sobre uma barra mostra o comprimento. Entre duas barras próximas mostra a folga. Sem clique.

**Painel "Medir" (dock lateral direito, 280 px):**
1. Cabeçalho: modo atual e ícones dos submodos.
2. Seleção 1 (chip laranja) e Seleção 2 (chip azul) com o nome do objeto. Clique no chip limpa só essa seleção.
3. Valor principal grande (24 pt, 3 casas em m; precisão configurável) e lista de valores secundários. Clique numa linha destaca a cota correspondente no 3D.
4. Tipo de distância: Mínima / Centro a centro / Máxima (segmentado).
5. **Acumulado** (igual ao Inventor): `+`/`−` ao lado de cada valor, um grupo por tipo (comprimento, ângulo, área, peso), cada grupo com total, lista com remoção por item e lixeira no cabeçalho. Atalho `+` soma o valor principal.
6. Copiar (Ctrl+C copia o valor principal; botão "Copiar tudo" copia a tabela em texto com tabulação).
7. Unidades: m / cm / mm. Ângulo em ° decimal ou °′″. Sistema: UCS / WCS.

**Teclado no Medir:**

| Tecla | Ação |
|---|---|
| Esc | Limpa as seleções. 2º Esc sai |
| Enter / Espaço | Confirma, ou fecha a cadeia/área |
| Tab | Cicla candidatos de snap |
| Shift + clique | Adiciona à seleção (área: soma região) |
| Ctrl + clique | Subtrai (área) |
| `+` | Adiciona o resultado ao acumulado |
| Ctrl+C | Copia o valor principal |
| Backspace | Desfaz o último ponto da cadeia |
| N | Nova medição (Restart), mantendo o acumulado |

**O que desenhar em 3D (overlay, sem depth test, some ao sair):**
- Cota temporária entre os 2 pontos:
  - Linha 1,5 px na cor de destaque `#FF9F1C`.
  - Setas cheias 10 px em tela.
  - Rótulo em "pílula" (fundo escuro 85% de opacidade, texto branco 12 px), sempre virado para a câmera e no meio da linha.
- Pontos medidos como esferas de 6 px (sel. 1 laranja, sel. 2 azul `#2F7DF6`).
- ΔX/ΔY/ΔZ: 3 segmentos tracejados nas cores dos eixos (vermelho/verde/azul), em escada a partir do ponto 1. Só quando a linha correspondente do painel estiver selecionada, ou com a opção "Mostrar componentes" ligada.
- Ângulo: arco com raio de 40 px em tela, no plano das duas direções, e rótulo `32,5°`.
- Distância mínima entre barras: a cota liga os pontos mais próximos. As barras ficam destacadas (contorno 2 px laranja/azul).
- Área: polígono preenchido verde `#2ECC71` a 25% de opacidade e contorno verde.
- "Fixar cota" (botão de alfinete): converte a cota temporária em **cota permanente** do modelo (camada Cotas).

---

## 5. Recomendação de teclado e linha de comando

### 5.1 Princípios [P]
- Teclado CAD clássico: alias + **Enter ou Espaço** (Espaço = Enter, exceto dentro de campo de texto).
- **Enter/Espaço com a linha vazia = repetir o último comando**. Botão direito com a linha vazia também (menu de contexto: "Repetir X", Enter, Cancelar, recentes) [C].
- **Esc** em cascata (já existe em `Vista3D.cs:1217`): digitação > ponto em andamento > comando > seleção.
- Selecionar antes e comandar depois (substantivo-verbo, `PICKFIRST`) e o contrário: as duas ordens funcionam.
- Letras sem modificador vão para a linha de comando (autocompletar). Atalhos com uma letra **só** com o foco na vista.

### 5.2 Mapa de teclas

| Tecla / alias | Comando | Origem |
|---|---|---|
| `L` | Linha de referência (eixo auxiliar, não estrutural) | AutoCAD LINE |
| `B` / `BARRA` | Barra genérica (perfil atual) | [P] ≈ PLANTSTEELMEMBER |
| `VG` / `VIGA` | Viga (cardinal default: centro-topo) | [P] |
| `PL` / `PILAR` | Pilar (clique o ponto base; altura = nível acima) | [P] |
| `GR` / `GRADE` | Grade estrutural (Create Grid) | [P] ≈ PLANTSTEELGRID |
| `CH` / `CHAPA` | Chapa/grade de piso | [P] ≈ PLANTSTEELPLATE |
| `SAP` | Sapata / placa de base | [P] ≈ PLANTSTEELFOOTING |
| `ESC` (alias `ESCADA`, porque `Esc` é a tecla) | Escada | [P] ≈ PLANTSTEELSTAIR |
| `GC` | Guarda-corpo | [P] ≈ PLANTSTEELRAILING |
| `EM` | Escada marinheiro | [P] ≈ PLANTSTEELLADDER |
| `TRE` | Assistente de treliça | [P] (Fase 5) |
| `CV` | Contraventamento (X, K, V) entre 4 pontos/barras | [P] (Fase 5) |
| `M` | Mover | AutoCAD MOVE |
| `CO` / `CP` | Copiar (ponto base, múltiplo) | AutoCAD COPY |
| `MI` | Espelhar | AutoCAD MIRROR |
| `RO` | Girar (em torno do Z do UCS; opção Eixo para 3D) | AutoCAD ROTATE / ROTATE3D |
| `AR` | Matriz retangular/polar/caminho | AutoCAD ARRAY |
| `TR` | Aparar até barra/plano (Shift = estender) | AutoCAD TRIM / PLANTSTEELTRIM |
| `EX` | Estender até barra/plano (Shift = aparar) | AutoCAD EXTEND / PLANTSTEELEXTEND |
| `LEN` | Alongar/encurtar (Delta/Total) | AutoCAD LENGTHEN / PLANTSTEELLENGTHEN |
| `RC` / `RECORTE` | Recorte (cut back) na barra limitante, opções Ambas/Folga | [P] ≈ PLANTSTEELCUT |
| `MIT` | Meia-esquadria | ≈ PLANTSTEELMITER |
| `BR` | Quebrar barra no ponto (cria nó) | AutoCAD BREAK AT POINT |
| `J` | Unir barras colineares | AutoCAD JOIN |
| `DIV` | Dividir barra em N partes (nós) | AutoCAD DIVIDE |
| `DI` / `MED` / `MEA` / Ctrl+M | Medir | AutoCAD DIST/MEASUREGEOM |
| `ID` | Coordenada do ponto | AutoCAD ID |
| `MA` | Copiar propriedades (Match) | AutoCAD MATCHPROP / Match Properties |
| `PR` / Ctrl+1 | Propriedades | AutoCAD PROPERTIES |
| `UCS` (opções F, OB, W, P, V, 3P) | Plano de trabalho | AutoCAD UCS |
| `V` | Vistas nomeadas | AutoCAD VIEW |
| `VS` | Estilo visual | AutoCAD VSCURRENT |
| `REP` / Ctrl+Shift+R | Representação Linha/Símbolo/Contorno/Sólido | ≈ PLANTSTEELSETREP |
| `Z` + `E` / duplo clique do meio | Zoom extents | AutoCAD ZOOM |
| `Z` + `O` | Zoom na seleção | AutoCAD ZOOM Object |
| `HI` / `ISO` | Isolar seleção / mostrar tudo | AutoCAD ISOLATEOBJECTS |
| `OS` | Configurações de snap | AutoCAD OSNAP |
| `LIG` | Ligação na seleção | [P] (Fase 3) |
| `CA` | Carga na seleção | [P] |
| `CALC` / Ctrl+F9 | Calcular | [P] (F5 fica livre de propósito) |
| Ctrl+Z / Ctrl+Y | Desfazer / Refazer | padrão |
| Del | Apagar seleção | já existe (`Acoes.cs:939`) |
| Ctrl+A | Selecionar tudo (visível) | padrão |
| Ctrl+C / Ctrl+V | Copiar / colar com ponto base (Ctrl+Shift+C) | AutoCAD COPYBASE |
| Ctrl+S / Ctrl+O / Ctrl+N | Salvar / abrir / novo | padrão |
| Espaço / Enter (linha vazia) | Repetir último comando | AutoCAD |
| Esc | Cancelar em cascata | AutoCAD |
| PgUp / PgDn | Plano de trabalho: nível acima/abaixo | [P] |
| Ctrl+teclado numérico 1…9 (alias `-VISTA`) | Vistas: 1 Frente, 3 Direita, 7 Topo, 5 Iso SE, 9 Iso NE… (convenção do Blender, sem conflito com o AutoCAD) | [P] |
| Teclado numérico 1…9 durante a colocação | Ponto cardinal | [P] |
| Ctrl+R durante a colocação | Gira o perfil +90° | [P] |
| Tab | Cicla snaps / campos da entrada dinâmica | AutoCAD |
| F1–F12 | Ver 3.6 | AutoCAD |

### 5.3 Linha de comando [P, comportamento AutoCAD C]
- Barra fixa embaixo da vista: uma linha de prompt e 3 linhas de histórico (F2 expande).
- **Autocompletar** enquanto digita: lista de comandos e aliases que começam com o texto, com ícone e descrição em pt-BR. Seta ↑/↓ navega, Enter/Espaço escolhe. Busca também no meio da palavra ("viga" acha `VG VIGA`).
- O prompt mostra as opções entre colchetes, clicáveis, com a letra-atalho em maiúscula: `Ponto inicial da barra ou [Linha/Configurações/Cardinal/Girar]:`.
- Valores digitados vão para a entrada dinâmica quando ela está ligada.
- **Comando transparente** com apóstrofo: `'MED` durante outro comando, `'Z`, `'UCS`. Órbita com Shift sempre é transparente.
- Aliases num arquivo editável `aliases.txt` (formato PGP: `VG, *VIGA`), carregado na partida.
- Histórico persistente por sessão. Ctrl+Z dentro do comando desfaz o último ponto (opção `Desfazer`).

---

## 6. Prioridade de implementação (sensação "Plant")

Ordem por impacto percebido / custo. Os itens 1–6 formam o mínimo para "parece Plant".

1. **Órbita transparente** Shift+meio e Shift+esquerdo em qualquer vista, com pivô na seleção ou sob o cursor. Zoom no cursor, pan no meio, duplo clique = extents. *(A1)*
2. **Plano de trabalho = UCS**, com grade visual, PgUp/PgDn por nível e UCS Objeto/Face. Planta/elevação viram câmeras ortográficas guardadas. *(A1)*
3. **OSNAP 3D**: END, MID, INT, PER, NEA, NOD, EXT, com marcadores no formato AutoCAD, abertura de 10 px, Tab cicla, Shift+botão direito one-off, F3. *(A2)*
4. **Barra estilo PLANTSTEELMEMBER**: cadeia de cliques, painel de configuração (perfil, aço, ângulo, cardinal 3×3, Y alinhado ao Z do UCS), pré-visualização do sólido no cursor. *(A3)*
5. **Linha de comando com aliases + Espaço/Enter repete + Esc em cascata.** *(A3)*
6. **Entrada dinâmica F12** (comprimento/ângulo, Tab, `@`, `#`, `<`) + ORTO F8 + polar F10 + distância direta. *(A2)*
7. **Medir** em modo automático (ponto-ponto, barra, barra-barra mínima, ângulo) com cota temporária 3D e painel. *(A2)*
8. **Grips** da barra (pontas, meio, alongar, girar) com snap. *(A3)*
9. **Grade estrutural** com sintaxe `0,@6,@6` e níveis como planos. *(A3)*
10. **Rastreamento F11** (aquisição 400 ms, até 7 pontos, caminhos X/Y/Z/polar). *(A2)*
11. **ViewCube** (26 vistas, Home, arrastar). *(A1)*
12. **M/CO/MI/RO/AR/TR/EX/LEN** sobre barras, com ponto base e cópias múltiplas. *(A3)*
13. Representação Linha/Contorno/Sólido + estilos visuais. *(A3/R1)*
14. Recorte paramétrico (cut back, Both, Gap) e coping automático na ligação viga-viga. *(L4)*
15. Escada, guarda-corpo, marinheiro, chapa/grade de piso. *(T1)*
16. 3D OSNAP F4 e UCS dinâmico F6. *(depois)*
17. Não fazer: SteeringWheels, isoplano F5.

Critérios de aceite sugeridos para a A2/A3 (testáveis via `Capturas.cs`):
- (a) Desenhar pilar 0,0,0 > 0,0,3, viga do topo `@6<0`. A viga encosta com END e fica com o topo no nível 3,00.
- (b) Shift+arrastar no meio do comando de barra não cancela o comando.
- (c) `MED` entre duas vigas paralelas a 1,5 m retorna 1,500 e desenha a cota.
- (d) Espaço com a linha vazia repete `VIGA`.
- (e) Tab alterna END/MID quando os dois estão a menos de 10 px.

---

## 7. Fontes

Conferidas nesta sessão [F]:
- Plant 3D, About Structure (2026): https://help.autodesk.com/cloudhelp/2026/ENU/Plant3D-UserGuide/files/GUID-56C9DE3A-E311-4501-BEB9-291B959B7D87.htm
- Plant 3D, Commands for Structure (2017): https://help.autodesk.com/cloudhelp/2017/ENU/Plant3D-UserGuide/files/GUID-0B82C6C1-1C5B-454A-AA92-9F9D9E6E166C.htm
- Plant 3D, Create Grid Dialog Box: https://help.autodesk.com/cloudhelp/2017/ENU/Plant3D-UserGuide/files/GUID-51789410-329D-4358-A1D7-90FAEA541175.htm
- Plant 3D, To create a structural grid (resumo da busca): https://help.autodesk.com/cloudhelp/2017/ENU/Plant3D-UserGuide/files/GUID-FDA06ACF-AAE7-4E8B-B275-617E804CB9D8.htm
- Plant 3D, Edit Grid Dialog Box: https://help.autodesk.com/cloudhelp/2017/ENU/Plant3D-UserGuide/files/GUID-F36C6498-E30A-41D8-83A4-ADBF00F0D2D5.htm
- Plant 3D, PLANTSTEELMEMBER: https://help.autodesk.com/cloudhelp/2017/ENU/Plant3D-UserGuide/files/GUID-2A2DF01A-83A9-4364-AAD3-826A46E5A00F.htm
- Plant 3D, Member Settings Dialog Box: https://help.autodesk.com/cloudhelp/2017/ENU/Plant3D-UserGuide/files/GUID-15FB81B5-AD8C-43C0-BF39-A72E6A9E7F60.htm
- Plant 3D, To specify member settings: https://help.autodesk.com/cloudhelp/2017/ENU/Plant3D-UserGuide/files/GUID-A75680A7-6E7F-4C4E-92CB-108B694F896F.htm
- Plant 3D, PLANTSTEELCUT: https://help.autodesk.com/cloudhelp/2017/ENU/Plant3D-UserGuide/files/GUID-78679229-850B-4F27-9415-6BEA896B0563.htm
- Plant 3D, PLANTSTEELLENGTHEN: https://help.autodesk.com/cloudhelp/2017/ENU/Plant3D-UserGuide/files/GUID-349F17D3-7CA0-4565-A785-C5CEE30AB136.htm
- Plant 3D, PLANTSTEELEXTEND: https://help.autodesk.com/cloudhelp/2017/ENU/Plant3D-UserGuide/files/GUID-684EAA99-2656-4A4F-800C-02863E05C7EE.htm
- Plant 3D, PLANTSTEELSETREP: https://help.autodesk.com/cloudhelp/2017/ENU/Plant3D-UserGuide/files/GUID-8B3CE053-A48B-4B81-BA81-F14612747436.htm
- AutoCAD, Object Snap Tab (Drafting Settings): https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-LT-MAC/files/GUID-A782462F-F85C-4496-85A5-3D9A54548489.htm
- AutoCAD, Drafting Tab (Options; AutoSnap, abertura): https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-LT/files/GUID-33A0F8E8-9981-4111-AD73-128B20D7975C.htm
- AutoCAD, MEASUREGEOM: https://help.autodesk.com/cloudhelp/2027/ENU/AutoCAD-LT/files/GUID-5D5B0EE1-DD90-47AE-8A55-642FBFF5E4E4.htm
- Inventor, To Measure Distance, Length, Angle, Loop, or Area: https://help.autodesk.com/cloudhelp/2026/ENU/Inventor-Help/files/GUID-1805AB8F-F782-4B24-A332-B4F2E10A3D00.htm
- Inventor, Measure Functions (iLogic): https://help.autodesk.com/cloudhelp/2017/ENU/Inventor-Help/files/GUID-14C7DC35-2701-4238-A05D-7309C35744ED.htm
- Inventor API, MeasureTools: https://help.autodesk.com/cloudhelp/2025/ENU/Inventor-API/files/MeasureTools.htm
- CADWorx, Shapes palette (cardinal point): https://docs.hexagonppm.com/r/5c7jKFeZbnbXObM~dAShug/B0t9A0CiUKlCIrc8_CuAVg
- Smart 3D Structure (9 posições cardinais + 10/15): https://docs.hexagonppm.com/r/en-US/Intergraph-Smart-3D-Structure/13/54627

Conferidas na retomada [F]:
- AutoCAD, 3DORBIT (2026): https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-Core/files/GUID-85CE824C-0AF4-4890-8487-ADBC92BF08F1.htm
- CAD Forum, Shift+botão do meio = 3DORBITTRANSPARENT no CUI: https://www.cadforum.cz/en/qaID.asp?tip=6277
- AutoCAD, About Reorienting the View (ViewCube): https://help.autodesk.com/cloudhelp/2020/ENG/AutoCAD-Core/files/GUID-A90F5F1C-B338-4BDE-9D05-F26BFFE3B3A5.htm
- AutoCAD, About the ViewCube: https://help.autodesk.com/cloudhelp/2021/ENU/AutoCAD-Core/files/GUID-E6D3896C-AF39-4F5C-A57C-CACE2A1117F9.htm
- AutoCAD, To Edit Objects Using Grips (2026): https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-LT/files/GUID-484774B4-8F67-4E19-8647-83A7D314782B.htm
- AutoCAD, Grip Colors Dialog Box: https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-LT/files/GUID-D973A3F3-6123-4105-B37B-39C4E7C7F26E.htm
- AutoCAD, Just Grip It (Did You Know): https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-LT-DidYouKnow/files/GUID-BBEA1F71-EB16-4D49-80D9-970A6909F508.htm
- AutoCAD, About Using Dynamic Input Tooltips (2026): https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-LT/files/GUID-3EBD4C17-F0A5-49FA-B131-4AABE2E727DB.htm
- Inventor, To Accumulate Values of Several Measurements (2024): https://help.autodesk.com/cloudhelp/2024/ENU/Inventor-Help/files/GUID-3610915E-112A-4BBD-8E28-820D33F2A717.htm
- Plant 3D, PLANTSTEELSTAIR: https://help.autodesk.com/cloudhelp/2017/ENU/Plant3D-UserGuide/files/GUID-4AE1D9B1-888E-4BAF-9AC6-EED416AF3F1A.htm
- Plant 3D, PLANTSTEELRAILING: https://help.autodesk.com/cloudhelp/2017/ENU/Plant3D-UserGuide/files/GUID-1CE80496-4EDB-4C81-94E6-851DE46A814D.htm
- Plant 3D, PLANTSTEELLADDER: https://help.autodesk.com/cloudhelp/2017/ENU/Plant3D-UserGuide/files/GUID-3145C6EC-DFB5-4541-84E4-14CB8D86E07B.htm
- Plant 3D, PLANTSTEELPLATE: https://help.autodesk.com/cloudhelp/2017/ENU/Plant3D-UserGuide/files/GUID-4986327B-E2C7-494A-9C44-F6A6873279CB.htm
- UCS dinâmico (terceiros/BricsCAD, não Autodesk): https://help.bricsys.com/en-us/document/bricscad/drawing-accurately/dynamic-ucs e https://novedge.com/blogs/design-news/autocad-tip-align-construction-plane-and-view-with-ucs

Ainda não reconferidos ([C]):
- SteeringWheels, opções do comando UCS (Face/Object…), F-keys além de F3/F6/F12.
- Tab entre campos e prefixo `#` da entrada dinâmica.
- Defaults numéricos: APERTURE 10, GRIPSIZE, cores de grip, cor amarela e tamanho do marcador AutoSnap, OSMODE.
- Formas dos marcadores AutoSnap (a ajuda só mostra imagens).
- Campos dos diálogos Settings de escada/guarda-corpo/marinheiro/sapata/chapa.
- Posição no Tekla e eixo de referência no Advance Steel.

Para conferir, buscar em `help.autodesk.com`: "UCS (Command)", "Keyboard Shortcuts Reference", "To Enter Absolute Coordinates", "APERTURE (System Variable)", "OSMODE (System Variable)", "Stair Settings Dialog Box". No Tekla: "Position properties". No Advance Steel: "Beam reference axis".
