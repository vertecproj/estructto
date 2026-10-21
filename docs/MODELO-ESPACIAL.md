# Modelo 3D (o documento único do Estructto)

Data: 21/10/2026. Código: `codigo/Estructto/DocumentoEspacial.cs`, `Comandos3D.cs`, `Legado3D.cs`, `Vista3D*.cs`, `ViewCube.cs`.
Capturas: `41-*`, `54-a1-*` (`Capturas.cs`, `CapturasA1.cs`).

## Decisão (A1, "3D-only")

O usuário: "quero poder modelar em 3D sem nada do 0; para sair do 2D para o 3D seja igual no CAD — segurar Shift e
rotacionar já é 3D; não tem 2D, é só 3D, igual o Plant".

- **Todo documento é o modelo do motor Python** (o JSON de `codigo/motor`, ESPEC §5). O WPF guarda só uma cópia do último
  `GET /api/modelo`. Não existe mais documento 2D nem vista 2D: planta, frente e lado são câmeras (`Vista3D.MostrarVista`).
- O `Modelo` do `Nucleo.cs` continua compilado: testes do motor interno (`compilar.ps1 -Testes`), conversão de arquivos
  antigos, a ponte da placa de base (abaixo) e o `--conferir-motor`/`--teste-motor` (o seletor "Interno/Python" saiu da faixa;
  fica escondido só para esses testes). O controle `Vista.cs` fica oculto e serve só de estado do HUD (diagrama e escala).
- Modelo novo (Ctrl+N e a abertura do programa) nasce **em planta, no térreo**: plano de trabalho z = 0, vista de topo,
  ortográfica. Desenha-se como numa folha de CAD até orbitar (Shift + arrastar).

## Arquivos

| Abrir | Como |
|---|---|
| `.estructto` novo | contêiner JSON (abaixo) |
| `.estructto` antigo (XML), `.portico`, `.ftl` | lido pelo `Nucleo.cs`/`Ftl.cs` e convertido por `MotorModelo.Enviar` (X → X, Y do 2D → Z, uy/rx/rz travados em todos os nós, seções e flambagem do 2D). As placas de base vêm junto (nó N → `nN`). Aviso (toast) uma vez por sessão; `DocumentoEspacial.Origem` guarda o arquivo antigo |
| `.json` | modelo do motor puro (`POST /api/modelo/abrir`) |
| `.ed3` | CYPE 3D (`POST /api/importar_cype`) |
| assistentes (viga, pórtico, treliça, galpão) | geram um `Modelo` 2D e passam pela mesma conversão |

**Salvar** (`SalvarEspacial`): `.estructto` = contêiner `{"formato": "estructto", "versao": 3, "modelo": <JSON do motor>,
"placas": {nó: PlacaBase}}` (placas antigas: só migração, ver PLACA-BASE-V2). A grade fica em `config.grade` e o ponto
cardinal em `params.cardinal` da barra, dentro do modelo do motor (Ctrl+Z desfaz pelo motor). Contêiner antigo com
`"grade"`/`"cardinal"` ao lado do modelo é migrado ao abrir (`DocumentoEspacial.Migrar`; cardinal inválido vira aviso, não trava).
`.json` = só o modelo do motor. Gravação atômica (`Config.GravarAtomico`: temporário + `File.Replace`). Escolha: um arquivo do usuário continua um arquivo só e com a extensão de sempre; o JSON
do motor fica intacto dentro dele (abre em qualquer ferramenta do motor) e o que o motor não guarda vai ao lado.
Um documento convertido nunca sobrescreve o antigo sozinho: Salvar pede o nome e sugere "nome (3D).estructto".
Recuperação automática (2 min): uma cópia por documento alterado (`%AppData%\NovoFtool\recuperacao\<Chave>.estructto`);
salvar ou fechar apaga só a cópia daquele documento. Ao abrir o programa cada cópia é oferecida; ela só sai depois de o documento
recuperado abrir (antes ele grava a cópia dele); se não abrir, fica. A cópia de slot único antiga (`recuperacao.portico`, inclusive
XML) ainda é lida.

## Funções que nasceram no 2D (`Legado3D.cs`)

| Função | Agora |
|---|---|
| Placa de base | `AbrirPlacaEsp(nó)`: o "pé de pilar" (nó com apoio e uma só barra) vira um `Modelo` mínimo de 2 nós no plano da barra e do eixo forte; a `ResBarra` reproduz N, V e M exatos nas duas pontas a partir de N, Vz e My do motor em cada combinação ELU (uma consulta `/api/resultados?alvo=esforcos&combinacao=…&ids=…` por combinação). A janela `JanelaPlaca` e o `PlacaCalc` (o mesmo método do `placa_base.py`) não mudaram; a combinação crítica vai no título. Placas ficam em `DocumentoEspacial.Placas`. ponytail: só flexão no eixo forte (Mz/Vy ignorados, como a placa sempre foi); pilar inclinado com o eixo forte fora do plano vertical é recusado; "Aplicar também em" só no próprio nó. Quando o motor expuser a placa por rota HTTP, trocar o `PlacaCalc` por ela neste arquivo |
| Relatório | `RelatorioMotor` (relatório NBR do motor) + anexo das placas pela mesma ponte (`AnexoPlacasEsp`, `PlacaPior` desviado) |
| Dimensionar | `DimensionarEspacial`: por barra com perfil do catálogo do motor, taxa > 1 sobe na família e taxa < 0,75 tenta o mais leve com massa ≥ massa·taxa/0,9; um PUT + F5 por rodada (até 8). Cada rodada é um Ctrl+Z. ponytail: palpite por massa, não ótimo global |
| Verificação de barras, Verificar barra, Estabilidade | as janelas do motor sobre o cálculo do documento (`GarantirCalculoEsp`: F5 se ainda não calculou) |

Removido da interface (só fazia sentido no 2D): grupos Desenhar 2D, Barra contínua, Apoios 2D, Seções e materiais 2D,
cargas Fx/Fy/Mz e qx/qy, Peso próprio e Timoshenko do 2D, seletor de motor, critérios "n" e flecha L/, eletrodo das soldas
2D, Exportar CSV, Mostrar Reações/Valores/Nomes, botão "Vista 3D", abas contextuais Barra e Nó, menu de contexto 2D,
Inspector 2D (sobrou só quando nenhum modelo está aberto) e as ferramentas 2D de `Acoes.cs`.

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
  pseudovetor (no espelho troca o sinal). `rot` e cardinal seguem a transformação (`GeometriaEdicao.GirarSecao`):
  rotação soma o ângulo no pilar vertical (sinal pelo sentido ni→nj; barra não vertical não muda); espelho (det −1) leva o z local
  espelhado e inverte o y (seção espelhada em u: exato em I, caixão, tubo, retângulo), U +180°, L +90° (exato em abas iguais).
  Testes: os pontos da seção desenhada da barra transformada = os pontos originais transformados.
- Nó novo a 1 mm ou menos de um existente funde com ele; barra que repetiria outra (mesmos nós) não é criada. Ids novos: `N<k>`, `B<k>`.
- Seleção: `IFonteSelecao` (nós e barras por id + `Mudou`). `FonteSelecaoEsp` lê a seleção múltipla da Vista3D (`Selecao.Nos`/
  `.Barras` + `SelecaoMudou`): clique, janela, cruzamento, Shift/Ctrl e também a escolha pela árvore, tabelas e avisos
  (`SelecionarEsp` passa por `Vista3D.Selecionar`). **Nada escolhido = modelo inteiro** (o painel diz "Seleção: modelo inteiro").
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
| **Níveis** (painel) | nome, cota, altura até o próximo; criar (+3 m), renomear (um PUT com o pavimento renomeado: diafragma mantido, um Ctrl+Z; `Niveis.Renomear`), mudar z (PATCH), apagar, **Detectar pela geometria** (um nível por cota de nós sem nível). Tudo em `/api/modelo/pavimento` dentro do `Motor.Exclusivo` |
| **Copiar pavimento para cima** | barras do andar do nível escolhido, nós novos (reaproveita nó que já está na posição) e cargas (nodais dos nós de cima do andar; distribuídas e pontuais das barras) para N andares de altura h. A cota de cada nó é mapeada em escala: o pé vai para o topo da cópia anterior. Cria pavimento em cada cota nova. **Um** `POST /api/modelo/lote`: um Ctrl+Z desfaz tudo |
| **Gerar pilares entre níveis** | um pilar sob cada nó do nível escolhido até o nível de baixo (nó novo embaixo se faltar; pula se já houver barra ligando os dois). Seção e material do primeiro pilar do modelo |

Plano de trabalho = interface `IPlanoAlvo { DefinirPlano(eixo, valor); Isolar(zMin, zMax); }`, ligada à Vista3D por `PlanoNaVista`:

- `DefinirPlano` troca o `Vista3D.PlanoAtual` (nível → `PlanoTrabalho.Horizontal(z, "P1 (z = 3,00 m)")`, elevação → `EmX`/`EmY`,
  3D completo → z = 0): ativar um nível faz dele o plano de desenho de Barra e Pilar; a câmera vai para planta/elevação.
- `Isolar` filtra o `Modelo3D` (`FiltrarPlano`, antes do `Vista3D.Carregar`); o que sai vai em `Modelo3D.Fantasmas`, que a Vista3D
  desenha (prismas finos translúcidos, fade na entrada) **fora** do picking, da seleção por caixa e da atração do desenho: clicar,
  selecionar e desenhar só veem o andar/plano isolado.
- Altura do Pilar pelos níveis (`Niveis.Lista`): clicado num nível que tem outro abaixo, o pilar **desce** até ele (é o andar que a
  vista isolada mostra); no nível mais baixo, sobe até o próximo; sem níveis, 3 m. A barra sai sempre com ni embaixo.
- Trocar para um documento 2D (`LimparPlano`) volta o plano de desenho a z = 0.

Limites: em planta os fantasmas ficam embaixo das barras isoladas (aparecem na isométrica);
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

### Desenho das cargas na Vista3D

- `Modelo3D.Cargas` (`Carga3D`): nodal (nó + F), pontual (barra + T = a/L + F) e distribuída (barra + q em ni + q2 em nj; sem q2 =
  uniforme), em eixos globais. `DocumentoEspacial.ParaVista` só põe as do **caso ativo** (Ações › Caso ativo ou o Caso do painel;
  trocar o caso redesenha). Momentos nodais não têm seta.
- Desenho (`Vista3D.DesenharCargas`): seta (haste + ponta piramidal) com a ponta no ponto de aplicação; nodal e pontual com
  comprimento proporcional ao módulo (a maior força = 11% do tamanho do modelo), distribuída em "pente" de setas ao longo da
  barra com altura linear de q a q2 (a maior q = 8%) e a linha das caudas. Escala própria (`EscalaCargas`), cor `TemaPlant.CorCarga`.
  Fora de `mapa`: não atrapalham o clique.
- Vista › Mostrar › **Cargas** liga/desliga no 2D e na Vista3D. Funciona no analítico e no **Realista** (lá a ponta para na face do
  perfil, meia largura da seção para fora do eixo). No plano isolado, só as cargas do que está na vista.
- Sem rótulo de valor (kN) nas setas: os valores ficam no painel Propriedades.
## Desenhar no 3D (Modelo › Barras)

Código: camada de entrada de `codigo/Estructto/Vista3D.cs` (o host fica no `partial MainWindow` do mesmo arquivo; em
`DocumentoEspacial.cs` só a palavra `partial`). Capturas: `43-desenho3d-*` (`Capturas.CapturasDesenho3D`; só elas:
`ESTRUCTTO_CAPTURAS=desenho3d`), que desenham um pórtico de 2 vãos chamando a API com pontos de tela e conferem nós, barras,
seleção e desfazer.

No documento espacial o grupo **Desenhar 3D** (Selecionar, Barra, Pilar) substitui o Desenhar do 2D; na paleta (Ctrl+K):
"Selecionar 3D", "Barra 3D", "Pilar 3D". **Modelo 3D vazio** (Modelo › Gerar) abre um modelo espacial em branco.

| Ferramenta | Uso |
|---|---|
| Navegar (nenhuma ligada) | clique escolhe (Propriedades); arrastar = janela/cruzamento; Shift + arrastar orbita |
| Selecionar | clique escolhe; arrastar da esquerda p/ direita = janela (só o que está inteiro dentro), da direita p/ esquerda = cruzamento (o que tocar); Shift soma, Ctrl tira, Esc limpa |
| Barra | cliques em sequência no plano de trabalho; a corrente segue do último ponto; Esc, Enter vazio ou botão direito termina |
| Pilar | clique o ponto: num nível com outro abaixo, desce até ele; no nível mais baixo, sobe até o próximo; no topo sem nível acima, a altura do último andar; sem níveis, 3 m (ver "Níveis e planos") |

- **Plano de trabalho** (`PlanoTrabalho`): horizontal z = c (padrão z = 0) ou vertical x = c / y = c. A grade do plano
  (passo `PassoGrade`, 1 m) aparece enquanto Barra/Pilar estão ligados; o mouse é projetado no plano pelo raio da câmera.
  Plano de perfil na vista (raio paralelo) avisa para girar a vista.
- **Atração**: snap igual ao AutoCAD (extremidade, meio, centro, nó, interseção real e aparente, perpendicular, extensão, paralelo,
  mais próximo; rastreio F11, polar F10, ORTO F8, F3; one-off por Shift+botão direito ou END/MID...) — ver
  [v3/SNAP-MEDIR.md](v3/SNAP-MEDIR.md). Marcador animado (`Anim.Entrar`, 120 ms) a cada troca.
- **Entrada dinâmica**: elástico até o cursor + caixinhas Comprimento/Ângulo (ângulo no plano, do eixo U para o W); digitar
  número trava o comprimento, Tab o ângulo, Enter confirma. Vírgula, `;` ou `@` passam para coordenada: `x,y,z` / `u,w` (com 1º
  ponto: relativa a ele, como no AutoCAD), `@dx,dy,dz` (relativa), `#x,y,z` (absoluta) ou `L<a` (polar no plano).
- **Criação**: cada clique = um `POST /api/modelo/lote` dentro de `Motor.Exclusivo` (nós novos + barra; atômico, um passo de
  `/api/desfazer`). Ponta a até 1 mm de um nó existente reaproveita o nó. Material: o da última barra (modelo sem material ganha
  A-572 GR.50 no mesmo lote).
- **Seção** (Desenhar 3D › Seção ▾): seções do modelo + **Do catálogo…** (abre o Catálogo de perfis do motor; "Usar este perfil"
  põe a seção no modelo com `POST /api/modelo/secao`, um passo de desfazer, e a deixa escolhida). Sem escolha (ou se a escolhida
  sumiu do modelo): a da última barra; modelo vazio: W310x38.7 no mesmo lote. Limite: o material não segue a seção (perfil de
  concreto herda o aço da última barra; trocar em Propriedades em lote).
- **Apoio na base** (botão): nó **novo** na cota do nível mais baixo (`Niveis.Lista`, Térreo z = 0 incluso) nasce engastado
  (6 GL). Nó que já existia não muda.
- **Esc em cascata**: texto digitado > comprimento/ângulo travado > barra em andamento > ferramenta (volta a Selecionar) > seleção.
  A janela passa Esc/Enter/Tab/Backspace/F8 para a Vista3D antes dos atalhos dela (`Acoes.cs › Tecla`, `Tecla3D`).

### API pública (para PlanosTrabalho.cs, EdicaoEspacial.cs e painéis)

| Membro de `Vista3D` | |
|---|---|
| `PlanoTrabalho PlanoAtual { get; set; }` + `event Action<PlanoTrabalho> PlanoMudou` | plano atual; `PlanoTrabalho.Horizontal(z)`, `EmX(x)`, `EmY(y)`, `new PlanoTrabalho('Z', z, nome)`; `N`, `U`, `W`, `Projetar(p)`, `Ponto(u, w)` |
| `Ferramenta3D Ferramenta { get; set; }` + `event Action FerramentaMudou` | `Navegar`, `Selecionar`, `Barra`, `Pilar`, `Medir`; `MainWindow.UsarFerramenta3D(f)` faz o mesmo pela faixa |
| `Selecao3D Selecao` + `event Action SelecaoMudou` | `Selecao.Nos` / `Selecao.Barras`: ids do motor (`HashSet<string>`) |
| `Selecionar(nos, barras, mods)`, `LimparSelecao()`, `SelecionarCaixa(a, b, mods)` | trocar/somar (Shift)/tirar (Ctrl) por id ou por caixa de tela |
| `Func<IList<(Point3D A, Point3D B)>, Task> CriarBarras` | quem cria as barras (MainWindow → `DocumentoEspacial.DesenharBarras`) |
| `Func<double, double> AlturaAndar`, `double PassoGrade`, `bool Orto` | altura do pilar por z da base, passo da grade, ortogonal |
| `Point3D? NoPlano(Point tela, PlanoTrabalho pl = null)`, `Point Tela(Point3D)` | raio do pixel até o plano; ponto 3D → pixel |
| `ClicarTela(Point, mods)`, `MoverTela(Point)`, `Tecla(Key)`, `Digitar(string)` | entrada sem mouse (testes/capturas); `InicioCorrente`, `PontoCursor`, `TipoAtracao` para conferir |

`DocumentoEspacial.DesenharBarras(segmentos, secao, apoioBase)` (lote, devolve os ids das barras) e `DocumentoEspacial.Vazio(titulo)`.

## Fluxo de modelagem 3D (captura de ponta a ponta)

`Capturas.CapturasFluxo3D` (PNGs `49-fluxo3d-*`, resumo `49-fluxo3d.txt`; só ela: `ESTRUCTTO_CAPTURAS=fluxo3d`) faz pelo caminho
do usuário e confere as contagens em cada passo:

| Passo | Confere |
|---|---|
| Modelo 3D vazio | 0 nós, 0 barras |
| Nível P1 (z = 3) + caso G | 2 níveis (Térreo, P1), caso ativo G |
| Ativar P1 (plano de desenho z = 3), grade 2×1 vãos (6 m × 5 m) com Barra | 6 nós, 7 vigas, todas em z = 3 |
| Seção do catálogo (W250…), Apoio na base, Pilar nos 6 nós do P1 | 12 nós, 13 barras, 6 apoios; pilares de z = 0 a 3 com a seção escolhida, vigas W310x38.7 |
| Copiar pavimento 2× para cima (h = 3 m) | 24 nós, 39 barras, 6 apoios, 4 níveis |
| Elevação y = 0, janela no pórtico, Espelhar em Y = −2,5 | janela pega 12 nós e 15 barras (a seleção chega ao painel); depois 36 nós, 54 barras, 9 apoios |
| Carga em área 4 kN/m² no nível 9 (vigas em X) + 1 nodal + 1 pontual | 6 vigas, Σ q·L = 480 kN = p·A; 8 setas do caso G na vista |
| F5, aproveitamento no analítico e no Realista com as setas | 54 barras verificadas |

### 54-a1: produto 3D único (`CapturasA1.cs`; só ela: `ESTRUCTTO_CAPTURAS=a1`; resumo `54-a1.txt`)

| Passo | Confere |
|---|---|
| Abre `exemplos/guincho-com-placa.estructto` (XML antigo) | documento 3D convertido, 4 nós, 3 barras, placa trazida |
| `L`, Shift + arrastar (`SimularOrbita`) | a Barra continua ligada depois da órbita |
| `TOPO`, `L`, `0,0,5`, `@2,0,0` | barra de 2,000 m criada pela linha de comando; Esc em cascata |
| grip da ponta + `@0,0,1` | nó a z = 6 |
| `GR` (3 × 2, níveis 0; 2,5; 5), `NIVEL +` | 2 pavimentos novos, plano em z = 2,5 |
| ViewCube (canto), perspectiva, F5 | vista "Topo frente direita", calculado |
| salvar `.estructto` e reabrir | contêiner v3 com modelo, placas e grade |

## Falta

- Desenhar max e min juntos na envoltória (hoje uma curva por barra).
- Pilha de desfazer por documento (o servidor guarda uma só).
- Desenhar 3D: sem atração a interseção/perpendicular/extensão nem rastreamento (o 2D tem); o material da barra nova não
  segue a seção escolhida.
- Setas das cargas sem rótulo de valor; cargas da combinação mostrada (fatoradas) não são desenhadas, só as do caso ativo.
- A1 (3D único): `RO` gira só em torno do eixo vertical e `MI` espelha em plano vertical (linha em planta); `TR`/`EX` usam
  as retas das barras (não planos nem o contorno do perfil); F4 não é o 3D OSNAP e F6 (UCS dinâmico) não existe; o ponto
  cardinal desloca o perfil no Realista (Render3D e nativo; docs/v3/RENDER.md); Dimensionar = um Ctrl+Z por rodada; a placa de base só
  vê flexão no eixo forte e "Aplicar também em" só no próprio nó.

## Navegação (como AutoCAD/Plant 3D) — `Vista3D.Navegacao.cs`, `ViewCube.cs`

| Ação | Mouse / teclado |
|---|---|
| Órbita | **Shift + botão do meio** ou **Shift + botão esquerdo** (touchpad) arrastando. Transparente: não cancela o comando em curso (elástico e atração continuam). Restrita: horizontal = azimute em torno do Z, vertical = elevação (±90°). Pivô automático, sem chave para trocar: o ponto da peça sob o cursor (fixo durante o arrasto; ponto ciano com anel que some ao soltar). Ver [VISTA3D.md](VISTA3D.md) "Pivô automático" |
| Pan | botão do meio arrastando |
| Zoom | roda, rumo ao ponto do modelo sob o cursor (o mesmo do pivô); na perspectiva o olho anda até ele sem empacar; **duplo clique do meio** = enquadrar (também `Z`, F4) |
| Escolher | clique; esquerdo arrastando = janela (esq.→dir.) ou cruzamento (dir.→esq.); Shift soma, Ctrl tira |
| Botão direito (sem arrastar) | Enter: termina a corrente da barra ou o comando que pede objetos. Shift/Ctrl + direito = menu de snap único (Snap3D) |
| ViewCube | canto superior direito: 26 regiões (faces = vistas ortogonais, arestas, cantos), casinha = isométrica, arrastar o cubo orbita (pivô: o que está no centro da vista) e encaixa a 10° de uma das 26 vistas; embaixo o nome da vista e **Ortográfica/Perspectiva** |
| Vistas | `TOPO`/`PLANTA`, `FRENTE`, `LADO`, `ISO`; Ctrl + teclado numérico 7/1/3/5; aba Vista. Transição de 300 ms (CubicEase, desligada com `Anim.Ligado`) |
| Projeção | `PERSP` ou Vista › Perspectiva ou o ViewCube. `Tela`, `Raio` e `NoPlano` valem nas duas; o Snap3D projeta com `Tela` na perspectiva |

## Linha de comando e teclado — `Comandos3D.cs`

Letras digitadas com o mouse na vista vão para a linha de comando; Enter ou Espaço executa. **Enter/Espaço com a linha vazia
repete o último comando.** Com uma ferramenta ou pedido de ponto em curso, números, vírgula e `@` vão para a entrada dinâmica;
digitados na linha de comando, valem como ponto (`x,y,z`, `@dx,dy,dz`, `#x,y,z`, `L<ângulo` — `Snap3D.LerCoordenada`) ou
comprimento na direção do cursor. `END`, `MID`, `INT`… = snap único (Vista3D.Snap.cs).

| Alias | Comando |
|---|---|
| `L`, `B`, `LINE`, `VG` | Barra (cadeia de cliques; Enter/Esc/direito termina) |
| `PI`, `PL` | Pilar (altura pelos níveis) |
| `S`, `SEL` | Selecionar |
| `M`, `CO`, `MI`, `RO` | Mover / Copiar (vários, base fixa) / Espelhar (dois pontos da linha, em planta) / Rotacionar (base + ângulo digitado ou direção clicada). Escolha antes ou depois (clique soma; Enter termina a escolha) |
| `AR`, `DIV`, `J` | Matriz, Dividir barra, Unir nós (painéis de `EdicaoEspacial.cs`) |
| `TR`, `EX` | Aparar / Estender: barra limite e depois as barras (cruzamento das retas a até 1 cm; aparar tira o lado clicado; estender leva a ponta mais perto do clique). Nó compartilhado ganha nó novo para só esta barra mudar |
| `E`, `DEL`, Del | Apagar a seleção (nó leva as barras; pontas soltas somem) |
| `PR`, Ctrl+1 | Propriedades (várias escolhidas: em lote) |
| `MA` | Copiar perfil, material e giro de uma barra para outras |
| `DI`, `ME`, `MEDIR`, Ctrl+M | Medir 3D (Medir3D.cs) |
| `ID` | Coordenadas de um ponto |
| `Z`, `ZE`, `Z O`/`ZO` | Enquadrar; zoom na seleção |
| `GR` | Grade estrutural |
| `U`, `REFAZER` | Desfazer / refazer (Ctrl+Z / Ctrl+Y) |
| `CALC` (F5, Ctrl+F9), `DIMENSIONAR`, `PLACA`, `RELATORIO`, `CASOS`, `TABELAS` | |
| `ENGASTE`, `ROTULA`, `LIVRE`, `ROTULAR`/`RB` | Apoio nos nós escolhidos; rótulas nas duas pontas das barras |
| `N`, `Q`, `MOMENTO`, `DEF`, `CALOR` | Diagramas |
| `ORTO`, `POLAR`, `OSNAP`, `NIVEL +`/`NIVEL -`, `PERSP`, `REALISTA`, `SELTODAS`, `SELNOS`, `SELBASE`, `ABA`, `AJUDA`/`?` | |

**Esc em cascata:** texto digitado > comprimento/ângulo travado > grip quente > comando que pede ponto/objeto > corrente da
barra > ferramenta (volta a Selecionar) > seleção.

**Teclas de função:** F1 ajuda, F2 histórico, F3 snap a objetos, F4 enquadrar (ponytail: no AutoCAD é o 3D OSNAP), F5
calcular (o atalho que o usuário já usa; Ctrl+F9 também), F6 avisa que o UCS dinâmico não existe, F7 grade do plano, F8 ORTO,
F9 snap na grade, F10 polar, F11 rastreamento, F12 entrada dinâmica. **PgUp/PgDn**: plano de trabalho no nível acima/abaixo
(pavimentos + Térreo + níveis da grade). Ctrl+R durante o desenho: +90° no giro das próximas barras.

## Grips — `Vista3D.Grips.cs`

Com barras ou nós escolhidos aparecem □ azuis nas pontas e nos nós e □ menores no meio das barras (sob o mouse vermelho
claro; quente vermelho). Clique (ou arraste) no grip e clique o destino — com atração, ORTO e `@dx,dy,dz`/`x,y,z` digitados.
Ponta/nó: move o nó (as barras ligadas vão junto; funde com nó a 1 mm). Meio: move a barra. **Ctrl** no destino copia: da
ponta sai uma barra nova da outra ponta até o destino; do meio, uma cópia da barra; de nó solto, um nó novo. Um PUT = um
Ctrl+Z (`GeometriaEdicao.Mover/Copiar`).

## Grade estrutural — `GR`

Diálogo com eixos em X, filas em Y e níveis em Z na sintaxe do Plant 3D (`0,@6,@6,@7.5`; `GeometriaEdicao.LerCotas`).
Nomes 1, 2, 3… e A, B, C…. Fonte única: `config.grade` do motor (`{x: [vãos], y: [vãos], nomes_x, nomes_y, x0?, y0?}`), a mesma
que o Novo projeto grava; GR grava a grade e os pavimentos novos num PUT (um Ctrl+Z). `Vista3D.Grade.cs` desenha como o Structure
Grid do Plant: linhas traço-ponto com balões em **cada nível** (pavimentos + térreo), o do plano de trabalho mais forte; os
cruzamentos de todos os níveis atraem o cursor (os do plano ganham no empate).

## Configurações (Member Settings) e Chapas

Aba Modelo › Configurações: **Seção** (modelo + catálogo do motor), **Material**, **Giro (°)** (`rot` da barra) e **Cardinal**
(1..9 da caixa da seção como o teclado numérico, 10 = centroide; guardado em `params.cardinal` da barra no motor, no mesmo lote da barra, e
entregue ao desenho em `Barra3D.Cardinal` — o Realista desloca o perfil (`GeometriaPerfis.Cardinal`); a análise usa o eixo),
**Apoio na base**. Chapas: o motor não tem elemento de placa; **Laje** abre a carga em área (painel Propriedades sem escolha).

## Snap e Medir (worktree wt-snap)

`Vista3D.Snap.cs`, `Snap3D.cs`, `Medir3D.cs`: `AtualizarCursor` chama `AtrairSnap`. O estado OSNAP/ORTO/Polar/Rastreio é do
`Snap3D` (`AplicarStatus`); F3/F8/F10/F11, os comandos e os botões do rodapé (Orto, Polar, Objetos, Rastreio: `StatusSnap3D`) passam por ele. Os cruzamentos da grade
estrutural entram na geometria de atração. `MostrarGradePlano` (F7) e `EntradaDinamica` (F12) são da Vista3D.

## Pontos de entrada (tela inicial, novo arquivo, Arquivo)

Para quem monta a tela inicial, o fluxo de "novo arquivo" e o menu Arquivo (worktree `wt-inicio`). Todos em `MainWindow`
(`DocumentoEspacial.cs`); cada documento abre numa aba nova da barra de arquivos.

| Membro | Faz |
|---|---|
| `Task NovoDocumento(JsonObject modelo = null, string titulo = null, bool silencioso = false)` | documento novo. `modelo` = JSON do motor (mesmo formato de `GET /api/modelo`; serve de gabarito) ou `null` = em branco. Abre em planta (vista de topo), plano de trabalho no térreo, ferramenta Selecionar. `silencioso` = erro do motor só na barra de status |
| `Task AbrirArquivo(string caminho)` | abre qualquer arquivo como documento 3D: `.estructto` (contêiner novo ou XML antigo, convertido), `.portico`, `.ftl`, `.json` do motor, `.ed3` do CYPE. Se já estiver aberto, só troca de aba |
| `IReadOnlyList<string> ArquivosRecentes` | últimos arquivos abertos/salvos, o mais recente primeiro (`Config.Recentes`) |

Também existem `NovoEmBranco()` (= `NovoDocumento()`), `AbrirGerador3D(tipo)` e `AbrirExemplosEspaciais()`.
