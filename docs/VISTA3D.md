# Vista 3D (fase 4 de UNIFICACAO.md)

Código: `codigo/Estructto/Vista3D.cs`. Capturas: `40-vista3d-*` (`Capturas.cs`).

## Decisão (21/10/2026): Realista no HelixToolkit.Wpf.SharpDX; analítico e fallback no Viewport3D nativo

Reavaliado com a meta "absurdamente bonito" (docs/v3/RENDER.md): o Viewport3D não faz sombra entre peças, oclusão ambiente nem
MSAA nas capturas, e os truques (sombra projetada, casca invertida) tinham chegado ao teto. Medido num protótipo e depois no
Estructto (`Render3D.cs`):

| | Viewport3D nativo | HelixToolkit.Wpf.SharpDX 3.1.2 (D3D11) |
|---|---|---|
| Pacote | nada | HelixToolkit.Wpf.SharpDX + HelixToolkit.SharpDX/Maths/Geometry + SharpDX 4.2 (net8.0-windows; ~7 MB) |
| Sombra, SSAO, AA | só no chão; sem SSAO; sem MSAA no RenderTargetBitmap | shadow map 4096², SSAO, MSAA 4× + FXAA, PBR, pós-efeito de contorno |
| Captura (`RenderTargetBitmap` da janela) | funciona | **funciona**: o D3DImage entra na captura depois de 2 quadros (`Render3D.PrepararCaptura`), com fundo opaco (transparente sai branco) |
| Sem placa de vídeo | funciona (software) | **não**: `EnableSoftwareRendering` (WARP) estoura a pilha ao criar o D3D9Ex do D3DImage (testado). `Render3D.Disponivel` só aceita adaptador de hardware com nível 10.0+ (DXGI), senão a Vista3D usa o Realista nativo deste arquivo. `ESTRUCTTO_3D=nativo` força. |
| 2.430 barras | montar 150–166 ms | montar 25–34 ms, quadro 9–10 ms (RTX 4060), picking 5 ms (com octree, 21/10: montar 50 ms, picking 0,8 ms) |

Resultado: o modo Realista vai para o Render3D quando há GPU; o analítico, a entrada (snap, desenho, seleção), as cargas e os
diagramas continuam no Viewport3D nativo, que fica por cima do DX. Integração mínima na `Vista3D.cs` (`RealismoDx`, câmera,
`Acertar`, `PrepararCaptura`); o resto em `Render3D*.cs`.

## Decisão original (18/10): Viewport3D nativo, sem HelixToolkit

O que a vista precisa (câmera ortográfica, órbita, pan, zoom no cursor, picking, prismas, diagramas planos) cabe em ~450 linhas
com `Viewport3D`, `OrthographicCamera` e `VisualTreeHelper.HitTest`. HelixToolkit.Wpf traria um pacote a mais e uma convenção de mouse
própria que teria de ser desligada para ficar igual à vista 2D. Rever se for preciso texto 3D, sombras ou modelos com milhares de barras
(aí o HelixToolkit.SharpDX compensa).

## Entrada neutra

`Modelo3D` = `No3D {Id, X, Y, Z, Apoios[6], Desl[3]?}` + `Barra3D {Id, Ni, Nj, Secao, Rot, N[], V[], M[]?, Aprov?}`.
Eixos globais X e Y horizontais, Z para cima (igual ao motor Python). N/V/M: valores em estações igualmente espaçadas de ni a nj,
desenhados no eixo local y (`Modelo3D.EixoY`: vertical global projetada no plano normal à barra, girada de `Rot` graus).

Adaptador `Modelo3D.De(Modelo, verificar)`: o plano XY do 2D vira o plano XZ (Y para cima → Z para cima); apoios 2D (X, Y, giro) →
(ux, uz, ry). Usa só o resultado atual de cada barra (sem envoltória).

## Uso

Navegação de AutoCAD/Plant 3D (Shift + arrastar orbita, meio = pan, roda = zoom no cursor, duplo clique do meio = enquadrar),
ViewCube, perspectiva, grips e pedidos de ponto/objeto: ver [MODELO-ESPACIAL.md](MODELO-ESPACIAL.md) ("Navegação", "Grips").
Código: `Vista3D.Navegacao.cs` (câmera, projeção `Tela`/`Raio`, mouse), `ViewCube.cs`, `Vista3D.Grips.cs`; `Vista3D.cs` guarda
dados, cena, Realista, entrada e sobreposição (os worktrees de render e snap mexem lá). Câmera ortográfica (padrão) ou
`PerspectiveCamera` de 45° na mesma largura visível no plano do alvo.

Transições de câmera: 300 ms, CubicEase EaseOut (GUIA-DESIGN). O diagrama, a deformada (com a escala do HUD) e o mapa de
aproveitamento (cores `CorCalor1..4` de `TemaPlant.cs`, faixas de `Vista.FaixaCalor`) seguem o que está escolhido no HUD.
A vista 2D não existe mais: `MostrarVista3D` só liga a Vista3D (host em `Desenho3D.cs`).

## Pivô automático da órbita e zoom no cursor (21/10/2026)

Pedido do usuário: o AutoCAD resolve com `OBJECTAUTOTARGET`, mas sem uma chave para ficar trocando; perto do modelo, orbitar em
torno do centro do modelo jogava a peça para fora da tela. Agora é automático, como Fusion 360, Revit e Inventor
(`Vista3D.Navegacao.cs`):

- **Pivô (`PivoSob`)**, escolhido ao começar a órbita (Shift + meio/esquerdo arrastando; arrastar o ViewCube usa o centro da
  vista) e fixo até soltar: (a) o ponto onde o raio do cursor toca a peça (`Acertar`: barras e nós da cena nativa; no
  Render3D também chapas e parafusos, `Render3D.Acertar(p, out ponto)`, o chão não conta); (b) o ponto do nó ou da barra mais
  perto do raio, até 30 px na tela; (c) o plano de trabalho sob o cursor, se cair a até 2 raios do centro do modelo;
  (d) o centro (caixa) dos nós que aparecem na tela, não o do modelo inteiro. A seleção não manda mais no pivô.
- **Órbita sem pulo.** Ao começar, `AncorarAlvo` leva o plano do alvo à profundidade do pivô sem mudar a imagem (perspectiva:
  mesmo olho, `larg` da distância nova). Cada passo gira olho e direção em torno do pivô, que fica no mesmo pixel; o pivô não
  é levado ao centro da tela. Vale na ortográfica e na perspectiva.
- **Roda (`Zoom`)**, no mesmo ponto: ortográfica muda a largura em torno dele (o ponto fica no pixel). Perspectiva: o olho
  anda pelo raio do cursor 1/6 da distância até o ponto e o alvo vai junto, na profundidade dele, então o ponto fica no pixel e
  dá para mergulhar numa junta. Colado no ponto (< 2% do raio do modelo) ou sem nada à frente, anda um passo fixo (17% de
  `max(distância do alvo, 2% do raio)`) e atravessa, sem empacar.
- **Marca.** Ponto ciano com anel (`TemaPlant.CorPivo`, `CorPivoAnel`, borda `CorSnapContorno`) durante o arrasto; some em
  `Anim.Medio` ao soltar (`TerminarOrbita`).
- **Desempenho** (12 × 10 vãos, 6 pavimentos, 2.430 barras): pivô em 1,4–1,7 ms (Render3D, RTX 4060) e 2,0–2,5 ms (analítico).
  O Render3D monta octree nas malhas das peças (`UpdateOctree`) e tira chão e arestas do picking: sem isso o pivô levava
  8,6 ms no Render3D. Custo: montar a cena de 24 para 50 ms (2.430 barras); o picking do clique cai de 2,1 para 0,8 ms
  (com as ligações de exemplo, 4,4 para 0,9 ms). A malha das ligações fica sem octree (montaria em 1,7 s em vez de 0,45 s).
- `Acertar` corrigido: o nó só ganha da barra quando está no mesmo lugar do raio (antes um nó lá atrás, alinhado com a viga
  clicada, ganhava dela).
- Testes: `Estructto.exe --testes-navegacao <saida.txt>` (`TestesNavegacao`, Vista3D fora da janela): pivô na junta e na
  viga sob o cursor, órbita de 30° com o pivô parado (< 1 px) e a junta na tela, começar a órbita sem pulo, plano de trabalho,
  centro do que aparece, zoom no cursor parado (< 1 px), perspectiva que chega na junta e anda no vazio, tempo < 5 ms.
  Capturas `56-orbita-pivo-realista|analitico` e `56-tempos.txt` (`CapturasSnap.cs`; só elas: `ESTRUCTTO_CAPTURAS=navegacao`).

## Parafusos, ligações propostas e linhas do analítico (21/10/2026)

Pedido do usuário: parafusos "bugados" (ocos, piscando na órbita), ligações que não apareciam no 3D, barras do analítico
grossas que "parecem cantoneira" de lado e nós em caixa. Capturas `58-*` (`CapturasVisual.cs`; só elas:
`ESTRUCTTO_CAPTURAS=visual`): `58-parafusos-az*-el*` (6 órbitas + perspectiva + longe), `58-analitico-*`, `58-galpao-propostas*`.

**Parafusos e sólidos (Render3D).** Causa principal: as tampas de `MalhaDx.Tubo` (cabeça, arruela, porca) eram um leque do
centro que pulava 2 das n fatias — via-se o interior e, por ele, a face da chapa no mesmo plano da arruela (z-fighting que
piscava ao orbitar). Agora:
- tampas fechadas; parafuso = cabeça + arruela + fuste (passa da porca) + arruela + porca, cada um um sólido fechado;
  arruelas a `FolgaParafuso` = 0,03 d da chapa; a porca encosta na `pega` (contrato: `pega` opcional; sem ela, estimada do
  comprimento como o motor calcula: pega + 0,9 d + 10 mm);
- perfis I/U/L com tampa triangulada pelo próprio contorno (`GeometriaPerfis.CriarContorno`; os retângulos davam vértices em T,
  frestas que cintilam); normal do furo dos tubos redondos para dentro do furo; cordão de solda com as duas pernas (prisma fechado);
- `CullMode.Back` em aço, concreto, seleção, parafusos, soldas e propostas: a face de trás some, então arruela/chapa,
  solda/peça e chapa/pilar não disputam o mesmo plano. Autoverificação `Render3DGeometria.Fechada` (cada aresta com a sua
  inversa e volume > 0) para parafuso, chapa no referencial canhoto, caixa, I com corte oblíquo, tubo e solda (`--testes-exportar`);
- planos perto/longe pela esfera da cena (2,6 raios em volta do centro da caixa: modelo, cargas e chão), na ortográfica e na
  perspectiva (`Vista3D.PlanosRecorte`); antes, longe = distância + 20 raios e perto = 2% da distância;
- nível de detalhe com histerese (arestas acendem em 6 px e apagam abaixo de 4; parafusos/soldas em 40/28 px) e parado
  durante a órbita (atualiza ao soltar).

**Ligações propostas.** Todo grupo de ligação sem projeto é desenhado como proposta (`Render3DMotor.cs`): tipo padrão do
tipo de nó (base → placa de base; viga–pilar na mesa rígida → `chapa_topo_estendida`, rotulada → `chapa_simples_alma`;
viga–pilar na alma e viga–viga → `chapa_simples_alma`; emenda reta → `emenda_viga`/`emenda_pilar`; gusset → 
`gusset_contraventamento` com forma e medidas do perfil), `POST /api/ligacoes/sugerir` com os perfis do grupo e esforços
típicos (N 50, V 50, M 30) e `/verificar` com o que ele sugeriu. Quando o motor não desenha (perfil formado a frio Ue, nó de
treliça, emenda em ângulo), a ligação vai `Pendente` e o Render3D faz a do tipo de nó com as juntas
(`LigacoesAmostra.Proposta`: chapa simples, chapa de topo, gusset; cumeeira/joelho = duas chapas de topo no plano da esquadria
com 8 parafusos). Modelo sem grupos (Identificar nunca rodou): propostas em todas as juntas (`LigacoesAmostra.Gerar`).
Estilo: chapas, parafusos e soldas em `TemaPlant.CorProposta3D` (azul-acinzentado fosco, malhas próprias, sem
transparência: a passagem transparente do Helix ignora a profundidade) e chip "Ligação proposta (sem projeto) · n" no canto
inferior direito. Grupos com projeto continuam sólidos como antes. Desempenho: cache estático por (tipo, dados, perfis) — os
grupos já são por assinatura, então é uma chamada por grupo e nenhuma ao recarregar; no máximo 40 chamadas novas por carga
e 600 propostas desenhadas. `galpao_aco` só com Identificar: 54 propostas + 12 placas de base (`58-galpao-propostas`).
Trocar o estilo de Analítico para Realista busca as ligações (`DefinirEstilo`). O Sombreado (nativo) não desenha ligações.

**Analítico: linhas e pontos na tela** (`Vista3D.Linhas.cs`). Barras = linhas de 1,5 px anti-serrilhadas (aproveitamento
2,25 px; escolhida = branca 2,25 px com halo `CorBarraSel`; com diagrama/deformada = `TextoMudo`), nós = pontos de 6 px
(4 px quando as barras ficam curtas na tela), apoios = símbolos na tela pelo "para baixo" do mundo (engaste: base hachurada;
articulado: triângulo + base; móvel: triângulo + base afastada). Desenhados pela sobreposição 2D (`Sobre`) a cada quadro de
câmera, com a mesma projeção do snap, grips, setas e cotas; na perspectiva o trecho atrás do plano perto é cortado. A
alternativa (linhas do Helix DX11) só existiria com GPU e no Realista; a sobreposição vale em todos os estilos e máquinas.
A deformada, os fantasmas, a posição original no Realista (tracejada) e o nó escolhido nos estilos sólidos também passaram
para a sobreposição; o Viewport3D nativo do analítico ficou só com grade, setas e diagramas. Picking (`Acertar`) pela tela:
nó a 8 px, barra a 6 px do eixo; ganha o mais perto do olho, e o nó ganha da barra que chega nele; `ponto` = ponto do eixo
mais perto do raio (pivô da órbita, zoom no cursor). `--testes-navegacao`, `--testes-snap` e `--testes-edicao` passam.

Limites: a sobreposição desenha as linhas por cima das setas e diagramas (sem profundidade), como os CADs; a cumeeira
proposta não tem solda nem enrijecedor; o gusset do motor fica no plano vertical da diagonal (contraventamento de cobertura
sai em pé). Sem Raio-X ainda (pede material transparente com profundidade no Render3D).

## Modo Realista

**Estilo visual** (`SeletorEstilo.cs`, 21/10/2026), onde os programas 3D põem: seletor "Realista ▾" na vista, abaixo do
ViewCube e do "Ortográfica ▾" (controles de viewport do AutoCAD/Plant 3D), chip na barra de status (View Control Bar do Revit)
e lista "Estilo visual" em Vista › Navegar (substitui o antigo botão Realista). Os três abrem o mesmo menu; o comando
`ESTILO` / `VS` passa para o próximo (`VS sombreado`, `ESTILO 3` escolhem). Ctrl+K: "Realista" (liga/desliga), "Estilo
visual" e um item por estilo. Estilos: **Analítico** (barras finas, grade), **Sombreado** (o Realista nativo descrito abaixo,
`Vista3D.SemDx`, mesmo com GPU) e **Realista** (Render3D quando há GPU; sem GPU fica igual ao Sombreado). Sem Raio-X por
enquanto (pede transparência nos materiais). A escolha fica em `%AppData%\NovoFtool\vista3d.txt` (`Config.Estilo3D`:
`analitico`, `sombreado` ou `realista`; os arquivos antigos continuam valendo; `Config.Realista3D` = sólidos). Capturas
`57-estilo-*` (`CapturasEstilo.cs`; só elas: `ESTRUCTTO_CAPTURAS=estilo`).
Capturas `42-realista-*` lado a lado com `42-analitico-*` (`Capturas.CapturasRealista`; só elas: `ESTRUCTTO_CAPTURAS=3d`).

- **Geometria real.** Cada barra é a extrusão do contorno da seção: I/H, U/C (Ue sem os enrijecedores), L, tubo retangular
  (e caixão), tubo circular, retângulo e círculo maciços. Dimensões: catálogo do Estructto (`Secao` do Nucleo.cs: H, Bf, Tw, Tf)
  ou `props` da seção no JSON do motor (`tipo`, `h`, `b`, `tw`, `tf`, `t`), via `Vista3D.Perfil` (resolvido em
  `MainWindow.PerfilDaSecao`; DocumentoEspacial.cs não mudou). Sem forma conhecida: caixa com a mesma A e I forte (√(12I/A) × A/h).
  A altura h fica no eixo local y da Vista3D (= z local do motor), girado por `rot`; no 2D, "perfil deitado" vira
  `Barra3D.RotPerfil = 90` (só o desenho; os diagramas continuam no plano). Pontas com tampa simples; nós sem acabamento.
- **Material e luz.** Aço cinza metálico frio com brilho especular discreto; seção maciça (RET/CIRC) é tratada como concreto,
  cinza claro quente. Luz chave (sol, fixa no mundo, vem da esquerda-alto da Iso), enchimento frio e ambiente. Normais suaves
  nos tubos redondos, planas nas chapas. Cores em `TemaPlant.cs` (`CorAco3D`, `CorConcreto3D`, `CorLuz*3D`, `CorChao3D`, `CorAresta3D`).
- **Arestas.** Casca invertida: cópia do perfil afastada ~0,85 px (miter nas quinas) que só desenha as faces de trás em
  quase-preto → contorno nas silhuetas e nas bordas do perfil. A largura acompanha o zoom: a cena é refeita 180 ms depois do
  zoom se a escala mudou mais de ~35%.
- **Sombra.** Sem shadow map: cada barra projeta no chão, pela direção da luz chave, uma faixa com pincel de alfa em bitmap
  (borda borrada; mais clara e mais larga quanto mais alta a peça). Mancha radial escura ("oclusão ambiente") no pé dos pilares.
  Chão com degradê radial que some no fundo escuro do canvas, no nível do pé dos blocos de apoio (dimensionados pela peça).
- **Resultados.** O mapa de aproveitamento tinge o material (72% da cor da faixa, mantém o sombreado); a seleção tinge de azul.
  A deformada desenha as peças reais na posição deformada e a posição original em linhas finas.
- **Desempenho.** Forma por nome de seção calculada uma vez (cache por carga do modelo); todas as barras juntadas em uma malha
  por (cor, material) + uma casca + uma malha de sombras, tudo congelado. O picking acha a barra pelo índice do vértice
  (`faixas`, busca binária) e os nós (não desenhados) pela distância na tela (8 px). Medido nas capturas (12 × 10 vãos,
  6 pavimentos, 2.430 barras): montar a cena 150 ms (analítico: 70–100 ms, mas com 2.430 modelos separados), picking 8 ms.

### Render3D (DX11)

Quando `Render3D.Disponivel`, tudo o que esta seção descreve (geometria, material, arestas, sombra, chão) é feito pelo
`Render3D.cs` com sombra entre peças, SSAO, PBR, arestas de 1 px, recortes nas juntas e ligações: ver
[v3/RENDER.md](v3/RENDER.md). O Realista nativo abaixo continua como fallback sem GPU.

### Sombras de verdade: HelixToolkit.Wpf.SharpDX? (avaliação de 18/10, superada pela decisão acima)

A sombra projetada nativa ficou boa o bastante (capturas `42-realista-*`): a leitura de profundidade vem do chão + sombra +
contorno. O que ela não faz: sombra de uma peça sobre outra, sombra sobre peças inclinadas, oclusão ambiente real e
anti-serrilhado nas capturas (RenderTargetBitmap do WPF 3D não tem MSAA; na tela a placa de vídeo suaviza).

| | Viewport3D nativo (hoje) | HelixToolkit.Wpf.SharpDX 2.x |
|---|---|---|
| Pacote | nada | HelixToolkit.SharpDX.Core + .Wpf + SharpDX (~6–8 MB de DLLs no exe publicado de ~70 MB) |
| GPU | qualquer (WPF cai em software) | Direct3D 11 (feature level 10+); em VM/RDP sem GPU cai no WARP, lento |
| Sombra | projeção no chão (este modo) | shadow map da luz direcional, SSAO, MSAA/FXAA, contorno por pós-processo |
| Esforço de portar | — | ~2–3 dias: trocar Viewport3D/Model3DGroup por Viewport3DX/Element3D, câmera e mouse próprios (desligar os do Helix para ficar igual ao 2D), picking (HitTest do Helix), diagramas e capturas (RenderTargetBitmap não lê o D3D: usar o `RenderToBitmap` do Helix) |
| Risco | — | mais um pacote grande; o projeto SharpDX está arquivado (o Helix 3 migra para Vortice) |

Recomendação: ficar no nativo. Rever se o usuário pedir sombra entre peças, modelos acima de ~10 mil barras ou texto 3D.

## Limites conhecidos

- Deformada desenhada como cordas entre nós deslocados (sem a elástica dentro da barra), inclusive no Realista.
- Modo analítico: barras em linhas e nós em pontos na tela (sem seção; o Sombreado/Realista extrudam o perfil real).
- Realista nativo (fallback): nós sem acabamento (as peças se cruzam; o Render3D recorta); Ue sem enrijecedores, 2L costas com costas desenhado como I, material pela
  forma da seção (aço maciço apareceria como concreto).
- Sem envoltória nos diagramas 3D.
