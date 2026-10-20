# Modelo espacial (documento 3D do motor)

Data: 20/10/2026. Código: `codigo/Estructto/DocumentoEspacial.cs`. Capturas: `41-*` (`Capturas.cs`, `CapturasEspacial`).

## Decisão

O `Modelo` do WPF (`Nucleo.cs`) continua só 2D. Um segundo tipo de documento, **Modelo espacial**, tem como fonte da verdade o
modelo do motor Python (o JSON de `codigo/motor`, ESPEC §5). O WPF guarda só uma cópia do último `GET /api/modelo`.
Os documentos 2D não mudaram.

## De onde vem

| Origem | Como |
|---|---|
| Gerar 3D (aba Modelo › Gerar, tela inicial) | `JanelaGerar3D` → `POST /api/modelo/novo` + `POST /api/gerar-estrutura` (edifício, galpão, pórtico plano) + `POST /api/modelo/lote` com G e Q uniformes (kN/m) nas barras de papel "viga" |
| Abrir `.json` | arquivo copiado para a pasta de trabalho do motor e `POST /api/modelo/abrir` (o motor valida) |
| Abrir `.ed3` (CYPE 3D) | `POST /api/importar_cype` com `conteudo_base64`; os avisos vão para o histórico |
| Exemplos do motor (Arquivo › Exemplos 3D, tela inicial) | `GET /api/exemplos` e `POST /api/exemplos/{nome}` |

## O que faz

- Vista3D sempre ligada (o botão Vista 3D não volta ao 2D nesse documento). Escolher nó ou barra mostra o painel do motor em Propriedades:
  nó (x, y, z, 6 apoios, deslocamento), barra (seção, material, giro, papel, verificação). Editar = `PATCH /api/modelo/{tipo}/{id}`.
- Desfazer/Refazer (Ctrl+Z/Ctrl+Y) = `POST /api/desfazer` / `POST /api/refazer`.
- Calcular (F5) = `POST /api/calcular`. Taxa de cada barra = pior linha da verificação do motor. Diagramas N, Vz, My e deformada da
  envoltória ELU ou da combinação escolhida (ver abaixo; `GET /api/resultados?alvo=esforcos|deslocamentos&combinacao=...`). O eixo local y da Vista3D é o z
  local do motor (mesma regra de `solver.eixos_locais`), então My < 0 no vão de viga sai para baixo (lado tracionado).
- Salvar = escreve o JSON do motor (cópia do último `GET /api/modelo`, atualizada depois de cada mudança).

## Um modelo por servidor

O servidor guarda um modelo só. `Motor.Dono` diz quem deixou o modelo lá; o documento espacial ativo se recoloca (arquivo
`_espacial.json` na pasta de trabalho + `abrir`) antes de cada operação se outro documento (ou a ponte 2D, que usa `PUT`) trocou.
Consequência: trocar de aba entre dois modelos espaciais perde a pilha de desfazer do outro.

Ao ativar a aba, o documento se recoloca no motor na hora. Se ele tinha histórico, o log avisa que o desfazer recomeçou
(o desfazer vale só para o documento ativo; o painel do modelo diz o mesmo).

## Painéis no documento espacial

| Painel | Mostra |
|---|---|
| Projeto (árvore) | nós (com apoio/carga), barras (seção e taxa), seções, materiais, casos, total de cargas; clique escolhe na vista |
| Tabelas › Nós | X, Y, Z, apoio (gl travados), ux/uy/uz (mm) do resultado mostrado |
| Tabelas › Barras | nós, perfil, material, L, N/Vz/My mín./máx. ao longo da barra |
| Tabelas › Verificação | pior linha da verificação do motor por barra (taxa, critério, norma, item, combinação) |
| Avisos | `GET /api/validar` + avisos da importação .ed3 + barras reprovadas |
| HUD | "Mostrando: envoltória ELU…" ou o nome da combinação; legenda com máx./mín. do diagrama do motor |

## Editar no 3D (painel Propriedades)

- Sem escolha: **Novo nó** (X, Y, Z) → `POST /api/modelo/no`.
- Nó: **Barra até outro nó** (clica no outro nó na vista), **Nó + barra** (nó deslocado dX, dY, dZ + barra), **Apagar nó**
  (`DELETE …?cascata=true`, leva barras e cargas), **cargas nodais** Fx, Fy, Fz no caso escolhido (lista com ×).
- Barra: **Seção** lista as do modelo + o catálogo do motor (`GET /api/catalogo`); perfil fora do modelo entra com
  `POST /api/modelo/secao {perfil}` antes do PATCH. **Apagar barra**, **carga distribuída** qx, qy, qz (global, kN/m) por caso.
- Barra nova copia seção, material da última barra do modelo. Caso das cargas = Ações › Caso ativo (mesmo valor).

## Editar a seleção (edição em lote)

Código: `codigo/Estructto/EdicaoEspacial.cs`. Faixa › Modelo › Modificar: Mover, Copiar, Espelhar e Dividir barra (os botões do 2D
passam para cá no documento espacial) + Rotacionar, Matriz, Unir nós e Propriedades em lote (só aparecem no espacial). Todos também
na paleta (Ctrl+K).

| Operação | Entrada | Faz |
|---|---|---|
| Mover | dx, dy, dz ou ponto base → ponto destino ("x; y; z") | move os nós (e as pontas das barras escolhidas); cargas das barras levadas vão junto |
| Copiar | o mesmo + N cópias | matriz linear: cópia k em k·(dx, dy, dz) |
| Espelhar | plano X = a, Y = b ou Z = c | cópia espelhada (o original fica) |
| Rotacionar | centro (x; y), ângulo (°, anti-horário visto de cima), cópias | eixo vertical; cópias = 0 gira, ≥ 1 cópia polar |
| Matriz | nx, ny, nz (contando a original) + espaçamentos | nx·ny·nz − 1 cópias |
| Dividir barra | N partes, ou distância a partir de ni | nós intermediários; cargas distribuídas reinterpoladas (trapezoidal), pontuais no trecho onde caem; `params` terminados em `_i` só no 1º trecho e `_j` só no último |
| Unir nós | tolerância (mm, padrão 1) | fica o primeiro nó; apoios somam (ou por GL); barras que zeram ou repetem somem com as cargas |
| Propriedades em lote | seção, material, giro (°), apoio (engaste/articulado/livre) | "(manter)"/vazio não muda; trocar para material com fck vira `tipo` concreto (e volta a aço) |

Regras:

- **Uma operação = um desfazer.** A geometria é feita no WPF sobre a cópia do JSON (`GeometriaEdicao`, pura) e vai inteira num
  `PUT /api/modelo` (`DocumentoEspacial.Aplicar`, dentro de `Motor.Exclusivo`). PUT está em `MUTAM`: uma entrada na pilha; Ctrl+Z
  desfaz tudo. Por que não `/api/modelo/lote`: o lote só adiciona; mover, dividir, unir e propriedades precisam mudar/remover.
- Cópias levam seção, material, rot, `params` e cargas (sem id: o motor numera). Forças seguem a transformação; momento nodal é
  pseudovetor (no espelho troca o sinal). O giro `rot` da seção não muda no espelho.
- Nó novo a 1 mm ou menos de um existente funde com ele; barra que repetiria outra (mesmos nós) não é criada. Ids novos: `N<k>`, `B<k>`.
- Seleção: `IFonteSelecao` (nós e barras por id + `Mudou`). Hoje o adaptador `FonteSelecaoEsp` usa a escolha única do documento;
  **nada escolhido = modelo inteiro** (o painel diz). Quando a Vista3D tiver seleção múltipla, só o adaptador muda.
- Painéis: janelas pequenas não modais (`PainelEdicao : JanelaEscura`, `Anim.Abrir`), ficam abertas para repetir; erro de digitação
  aparece no painel, erro do motor na barra de status.
- Seções do "Propriedades em lote" = as do modelo (perfil do catálogo entraria por um POST separado, um segundo desfazer).

Testes: `Estructto.exe --testes-edicao <saida.txt>` (23 asserções: espelho, rotação 90°, matriz 2×3×1, cópia 3×, tolerância 0,5 mm × 2 mm,
fundir ao mover, dividir em 3 e a 1,5 m, unir nós, propriedades). Capturas `48-*`: pórtico plano copiado 3× (e Ctrl+Z/Ctrl+Y conferidos),
espelhado em Y = −3 e rotacionado 30° em torno de (6; 0), depois calculado.

## Combinação dos diagramas

Resultados › Mostrar: **Envoltória ELU (motor)** (padrão) ou qualquer combinação do cálculo. Na envoltória, cada ponto do diagrama é o
valor de maior módulo entre max e min (com sinal); a deformada usa a envoltória ELS (padrão de `projeto.resultados`).

## Níveis e planos de trabalho

Código: `codigo/Estructto/PlanosTrabalho.cs` (regras puras em `Niveis`, faixa e adaptador em `MainWindow`, painel `JanelaNiveis`).
Ganchos: `DocumentoEspacial.Executar` (operação composta na fila do motor, conta no desfazer), `FiltrarPlano` no
`AtualizarEspacial3D`, `LimparPlano` ao sair do modo espacial, grupo inserido na aba Modelo, linhas na ajuda F1.
Capturas `43-*` (`Capturas.CapturasPlanos`; só elas: `ESTRUCTTO_CAPTURAS=planos`), que também conferem as contagens.

| Comando | O que faz |
|---|---|
| Modelo › Planos › **Nível ▾** | lista os pavimentos do motor + "Térreo z=0" (se nenhum estiver na cota 0). Ativar = plano de trabalho na cota, só as barras do andar (os dois nós entre o nível de baixo e ele, sem as deitadas na cota de baixo), as outras em fantasma (linhas claras com fade), câmera em planta (transição de 300 ms da Vista3D) |
| **Elevação X ▾ / Y ▾** | cotas distintas dos nós; só as barras do plano vertical, vista Lado (YZ) / Frente (XZ) |
| **3D completo** | tira o filtro, volta à isométrica |
| **Níveis** (painel) | nome, cota, altura até o próximo; criar (+3 m), renomear (= DELETE + POST: o motor não muda o nome), mudar z (PATCH), apagar, **Detectar pela geometria** (um nível por cota de nós sem nível). Tudo em `/api/modelo/pavimento` dentro do `Motor.Exclusivo` |
| **Copiar pavimento para cima** | barras do andar do nível escolhido, nós novos (reaproveita nó que já está na posição) e cargas (nodais dos nós de cima do andar; distribuídas e pontuais das barras) para N andares de altura h. A cota de cada nó é mapeada em escala: o pé vai para o topo da cópia anterior. Cria pavimento em cada cota nova. **Um** `POST /api/modelo/lote`: um Ctrl+Z desfaz tudo |
| **Gerar pilares entre níveis** | um pilar sob cada nó do nível escolhido até o nível de baixo (nó novo embaixo se faltar; pula se já houver barra ligando os dois). Seção e material do primeiro pilar do modelo |

Plano de trabalho = interface `IPlanoAlvo { DefinirPlano(eixo, valor); Isolar(zMin, zMax); }`. Nesta branch o adaptador
`PlanoNaVista` (comentário `ponytail:`) filtra o `Modelo3D` antes do `Vista3D.Carregar` e desenha os fantasmas num `ModelVisual3D`
próprio (a Vista3D ignora no picking os modelos que não são dela). Depois do merge com o `Vista3D.PlanoAtual`, ligar o adaptador nele.

Limites: em planta os fantasmas ficam embaixo das barras isoladas (aparecem na isométrica); renomear nível = dois passos no desfazer;
apoios não são copiados (as cópias ficam sobre o andar de baixo).
## Cargas e casos (`CargasEspacial.cs`)

- **Barra › Carga distribuída**: global (kN/m; o motor só tem direção global), valor em ni e em nj. Iguais = uniforme (sem `q2`);
  diferentes = trapezoidal (`q` em ni, `q2` em nj). Cada carga existente tem os campos editáveis + Gravar (`PATCH`, `q2: null` volta
  a uniforme) e Apagar.
- **Barra › Cargas pontuais**: `cargas_pontuais` (a desde ni em m, Fx, Fy, Fz global em kN); adicionar, gravar (`PATCH`), apagar.
- **Modelo (sem escolha) › Casos e ações**: Casos de carga…, Pavimentos…, Vento…, Sismo… Os mesmos botões da faixa (Ações) e o
  comando `CASOS` também vão para o modelo espacial quando o documento ativo é espacial (`FaixaMotor.cs`: `ComMotor`/`ComMotorExtra`
  desviam para `JanelaMotorEsp`; antes mandavam o Modelo 2D vazio ao motor).
- `DocumentoEspacial.ComJanela`: dentro de **um** `Motor.Exclusivo` recoloca o modelo (Garantir), abre a janela modal (ela chama o
  motor direto), relê `GET /api/modelo` e, se mudou, `Recarregar` (o cálculo anterior é descartado) e marca o documento como alterado.
  Vento, Sismo e Pavimentos não mudaram: leem o modelo do servidor, então mostram o modelo espacial (capturas `48-espacial-*`).
- Combinações (faixa, sem cálculo) também abre sobre o modelo espacial. Janelas que precisam de cálculo (Estabilidade, Verificar
  barra pela faixa) continuam pela ponte 2D: no documento espacial use F5 e os painéis do espacial.

### Carga em área (regra e limites)

Painel do modelo › Carga em área: nível z (cotas com barras horizontais), "Recebem" = vigas em X ou em Y, p (kN/m²), caso.
Uma chamada `POST /api/modelo/lote` com uma carga distribuída uniforme `(0, 0, -q)` por viga.

- Regra de uma direção: só as barras horizontais no nível (|z − z0| ≤ 1 mm), paralelas à direção escolhida, recebem.
  q = p × (d₁/2 + d₂/2), d₁ e d₂ = distância em planta à viga paralela mais próxima de cada lado entre as que se sobrepõem
  ao longo do eixo. Numa grade regular, Σ q·L = p × área da grade (conferido na captura).
- Limites (`ponytail:` em `CargaArea`): viga de borda leva só o lado interno (sem balanço de laje); sobreposição parcial conta como
  inteira; não sabe onde há laje (vão sem laje também carrega); as vigas da outra direção não recebem nada (laje armada em uma
  direção); O(n²) por nível. Laje em duas direções (charneiras/áreas de influência) = trocar `CargaArea`.

### Desenho das cargas na Vista3D (falta; `Vista3D.cs` é de outro agente)

A Vista3D não desenha cargas. Precisa: em `Modelo3D`, listas de cargas por caso (nodal: nó + F; distribuída: barra + q, q2;
pontual: barra + a + F), um filtro de caso (Ações › Caso ativo) e o desenho: seta por força nodal/pontual (comprimento
proporcional, rótulo kN), "pente" de setas ao longo da barra para distribuída (altura linear de q a q2), escala própria e
cor de carga do `TemaPlant`. `DocumentoEspacial.ParaVista` só passaria as listas do JSON.
## Desenhar no 3D (Modelo › Desenhar 3D)

Código: camada de entrada de `codigo/Estructto/Vista3D.cs` (o host fica no `partial MainWindow` do mesmo arquivo; em
`DocumentoEspacial.cs` só a palavra `partial`). Capturas: `43-desenho3d-*` (`Capturas.CapturasDesenho3D`; só elas:
`ESTRUCTTO_CAPTURAS=desenho3d`), que desenham um pórtico de 2 vãos chamando a API com pontos de tela e conferem nós, barras,
seleção e desfazer.

No documento espacial o grupo **Desenhar 3D** (Selecionar, Barra, Pilar) substitui o Desenhar do 2D; na paleta (Ctrl+K):
"Selecionar 3D", "Barra 3D", "Pilar 3D". **Modelo 3D vazio** (Modelo › Gerar) abre um modelo espacial em branco.

| Ferramenta | Uso |
|---|---|
| Navegar (nenhuma ligada) | clique escolhe (Propriedades), arrastar com o esquerdo orbita |
| Selecionar | clique escolhe; arrastar da esquerda p/ direita = janela (só o que está inteiro dentro), da direita p/ esquerda = cruzamento (o que tocar); Shift soma, Ctrl tira, Esc limpa |
| Barra | cliques em sequência no plano de trabalho; a corrente segue do último ponto; Esc, Enter vazio ou botão direito termina |
| Pilar | clique a base: barra vertical de z até z + altura do pavimento (próximo `pavimentos[].z` acima; no topo, a do último andar; sem pavimentos, 3 m) |

- **Plano de trabalho** (`PlanoTrabalho`): horizontal z = c (padrão z = 0) ou vertical x = c / y = c. A grade do plano
  (passo `PassoGrade`, 1 m) aparece enquanto Barra/Pilar estão ligados; o mouse é projetado no plano pelo raio da câmera.
  Plano de perfil na vista (raio paralelo) avisa para girar a vista.
- **Atração** (como o 2D): nó a 10 px na tela (em qualquer cota) > meio de barra > ortogonal/polar 0/90° nos eixos do plano a partir
  do 1º ponto (F8 liga, Shift força) > ponto da grade a 10 px > plano. Marcador animado (`Anim.Entrar`, 120 ms) a cada troca.
- **Entrada dinâmica**: elástico até o cursor + caixinhas Comprimento/Ângulo (ângulo no plano, do eixo U para o W); digitar
  número trava o comprimento, Tab o ângulo, Enter confirma. Vírgula, `;` ou `@` passam para coordenada: `x,y,z`, `@dx,dy,dz`
  (relativa ao 1º ponto) ou `u,w` (2 valores = no plano).
- **Criação**: cada clique = um `POST /api/modelo/lote` dentro de `Motor.Exclusivo` (nós novos + barra; atômico, um passo de
  `/api/desfazer`). Ponta a até 1 mm de um nó existente reaproveita o nó. Seção e material: os da última barra do modelo (igual
  ao painel); modelo sem seção/material ganha W310x38.7 e A-572 GR.50 no mesmo lote.
- **Esc em cascata**: texto digitado > comprimento/ângulo travado > barra em andamento > ferramenta (volta a Selecionar) > seleção.
  A janela passa Esc/Enter/Tab/Backspace/F8 para a Vista3D antes dos atalhos dela (`Acoes.cs › Tecla`, `Tecla3D`).

### API pública (para PlanosTrabalho.cs, EdicaoEspacial.cs e painéis)

| Membro de `Vista3D` | |
|---|---|
| `PlanoTrabalho PlanoAtual { get; set; }` + `event Action<PlanoTrabalho> PlanoMudou` | plano atual; `PlanoTrabalho.Horizontal(z)`, `EmX(x)`, `EmY(y)`, `new PlanoTrabalho('Z', z, nome)`; `N`, `U`, `W`, `Projetar(p)`, `Ponto(u, w)` |
| `Ferramenta3D Ferramenta { get; set; }` + `event Action FerramentaMudou` | `Navegar`, `Selecionar`, `Barra`, `Pilar`; `MainWindow.UsarFerramenta3D(f)` faz o mesmo pela faixa |
| `Selecao3D Selecao` + `event Action SelecaoMudou` | `Selecao.Nos` / `Selecao.Barras`: ids do motor (`HashSet<string>`) |
| `Selecionar(nos, barras, mods)`, `LimparSelecao()`, `SelecionarCaixa(a, b, mods)` | trocar/somar (Shift)/tirar (Ctrl) por id ou por caixa de tela |
| `Func<IList<(Point3D A, Point3D B)>, Task> CriarBarras` | quem cria as barras (MainWindow → `DocumentoEspacial.DesenharBarras`) |
| `Func<double, double> AlturaAndar`, `double PassoGrade`, `bool Orto` | altura do pilar por z da base, passo da grade, ortogonal |
| `Point3D? NoPlano(Point tela, PlanoTrabalho pl = null)`, `Point Tela(Point3D)` | raio do pixel até o plano; ponto 3D → pixel |
| `ClicarTela(Point, mods)`, `MoverTela(Point)`, `Tecla(Key)`, `Digitar(string)` | entrada sem mouse (testes/capturas); `InicioCorrente`, `PontoCursor`, `TipoAtracao` para conferir |

`DocumentoEspacial.DesenharBarras(segmentos)` (lote, devolve os ids das barras) e `DocumentoEspacial.Vazio(titulo)`.

## Falta

- Desenhar as cargas na Vista3D (ver acima).
- Desenhar max e min juntos na envoltória (hoje uma curva por barra).
- Pilha de desfazer por documento (o servidor guarda uma só).
- Desenhar 3D: sem atração a interseção/perpendicular/extensão nem rastreamento (o 2D tem); a seção da barra nova é a da
  última barra (não há seletor de seção do motor na faixa); nós da base não recebem apoio automático.
