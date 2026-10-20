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

| Ação | Mouse / teclado |
|---|---|
| Zoom no cursor | roda |
| Pan | botão do meio ou direito arrastando (igual ao 2D) |
| Órbita | botão esquerdo arrastando, ou Shift + meio |
| Enquadrar | duplo clique no meio, F4 |
| Escolher barra/nó | clique (evento `Escolheu`; na janela vira a seleção do 2D e abre o Inspector) |
| Topo / Frente / Lado / Iso | seletor no canto superior direito da vista; `MostrarVista(VistaPadrao)` |

Transições de câmera: 300 ms, CubicEase EaseOut (GUIA-DESIGN). O diagrama, a deformada (com a escala do HUD) e o mapa de
aproveitamento (cores `CorCalor1..4` de `TemaPlant.cs`, faixas de `Vista.FaixaCalor`) seguem o que está escolhido no HUD.
Alternância: `MainWindow.AlternarVista3D()` / `MostrarVista3D(bool)`, botão "Vista 3D" na aba Vista.

## Limites conhecidos

- Deformada desenhada como cordas entre nós deslocados (sem a elástica dentro da barra).
- Seção desenhada como prisma quadrado fino (não extrudada com o perfil real).
- Sem envoltória nos diagramas 3D.
