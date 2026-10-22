# Parafusos 3D (C4)

Contrato (`geometria.parafusos`, mm): `origem` = face de apoio da cabeça (sob a arruela), `eixo` = direção do fuste (cabeça no lado -eixo, porca em origem + eixo * pega). `pega` = cabeça até o fim da pilha apertada (inclui cantoneiras, que não são chapas).

Causa do "parafuso bugado" na PLATAFORMA: o contraventamento (gusset) não mandava `pega`; o render estimava `comprimento - 0,9d - 14` e a porca ficava dentro do gusset (110 de 170 parafusos). O motor passou a mandar `pega` (cabeça a 4 mm da aba + cantoneiras + gusset).

Render: cabeça e porca com a mesma fase (antes a porca girava 30 graus) e base do hexágono = aresta U da chapa que o parafuso atravessa (normal ∥ eixo), projetada no plano do furo; sem chapa vale a base antiga. Os hexágonos de um nó ficam todos alinhados à chapa.

Auto-teste: `--testes-interferencia` (PLATAFORMA e galpao) roda `VerificarParafusos` (TestesParafusos.cs): eixo ∥ normal (±1 grau), cabeça fora das chapas, fuste atravessa a chapa, porca fora. Conta como falha no código de saída.

## Rodada 1 (revisão)
- Pilha apertada do auto-teste = chapas + cantoneiras do contrato (amostragem do contorno do L) + `pilha` do parafuso (`[t0, t1]` mm: aba da barra ligada, fora de `chapas`; o contraventamento envia).
- O teste usa a pega EFETIVA (`Render3DGeometria.PegaEfetiva`, depois do limite) e reprova se o limite a encurtou; o motor agora dimensiona `comprimento = pega + max(25 mm, 1,2d + 6 mm)`.
- `VerificarApertadas`: o meio de cada peça de `pilha` (no mundo) tem de estar dentro de um sólido de barra. Achou 67 de 110 no antes: a diagonal em U apoiava as pontas das abas no gusset (abertura do U virada para a chapa) e o parafuso apertava o ar. Render3D (`EscolheY`) espelha y' para a ALMA do U encostar no gusset.
- Hexágono: alinhado à aresta do contorno mais próxima do parafuso (`Ligacao3D.ArestaMaisPerto`), serve ao gusset trapezoidal.
