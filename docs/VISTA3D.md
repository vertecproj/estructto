# Vista 3D (fase 4 de UNIFICACAO.md)

Código: `codigo/Estructto/Vista3D.cs`. Capturas: `40-vista3d-*` (`Capturas.cs`).

## Decisão: Viewport3D nativo, sem HelixToolkit

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

## Modo Realista

Botão **Realista** em Vista › Navegar, ao lado de "Vista 3D" (`MainWindow.DefinirRealista`). A escolha fica em
`%AppData%\NovoFtool\vista3d.txt` (`Config.Realista3D`). Desligado = vista analítica de sempre (barras finas, grade).
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

### Sombras de verdade: HelixToolkit.Wpf.SharpDX? (avaliado, não adotado)

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
- Modo analítico: seção desenhada como prisma quadrado fino (o Realista extruda o perfil real).
- Realista: nós sem acabamento (as peças se cruzam), Ue sem enrijecedores, 2L costas com costas desenhado como I, material pela
  forma da seção (aço maciço apareceria como concreto).
- Sem envoltória nos diagramas 3D.
