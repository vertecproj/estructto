# W8-RIG — padrões de fábrica das rígidas e emendas: painel de alma, emendas, chapas de topo

Data: 27/10/2026. Branch `wt8-rig`. Pedido: "corrija os outros padrões ruins" (pendências da revisão W7-LIG §3, linhas 11,
12, 14, 15 e 16). Arquivo principal: `codigo/motor/prossen_estrutural/aco/ligacoes_rigidas.py`.

Capturas: `ESTRUCTTO_CAPTURAS=w8rig` (`CapturasW8Rig.cs`): os cinco padrões nas vistas juntas e nas quatro vistas grandes
(`80-w8rig-<tipo>[-elevacao|-planta|-lateral|-3d]`), o painel com enrijecedores e chapas de reforço
(`-painel-alma-pilar-enrijecido`), a janela alta com o campo sim/não (`-painel-alma-pilar-campos`) e `80-w8rig.txt`.
Antes: `C:\dev\wt\w8-antes\70-w7lig-*`. Depois: `C:\dev\wt\w8-depois\80-w8rig-*`.

## O que estava errado (padrão de fábrica: W310×38,7 na mesa do W250×73, V = 100 kN, M = 60 kN·m)

| Tipo | Antes | Causa |
|---|---|---|
| Chapa de topo ajustada / estendida | 1,00 em "furo → face da mesa (pf) ≥ mínimo de aperto" | `pf = 0` (auto) é o próprio mínimo CBCA 6.1.1 (30/35/40 mm): todo padrão nascia no limite |
| Emenda de viga / de pilar | 0,97 em "borda mínima (tala da alma)": 30 contra 29 mm | borda do sugerir = tabela 14 só arredondada a 5 mm; tala da alma a 0,96 da altura livre; tala da mesa 9,70 / 14,20 mm (= tf, espessura que não existe); alma com 5 linhas a 50 mm (⅝") |
| Emendas (desenho) | lateral com "92,5" e "165" (viga) / "137" e "254" (pilar) no meio do perfil; elevação com a cadeia "55 80 55" de total 270 | a cadeia da tala de mesa ia para baixo da tala **de cima** (empate entre as duas talas); a cadeia da tala da alma contava os furos da mesa; a cadeia ficava em cima da tala de baixo e das porcas |
| Painel de alma do pilar | desenho só com os perfis, viga a 12 mm do pilar; "Mesa da viga soldada" como `true`/`false` | o tipo não punha nada na `geometria`; os sim/não das rígidas vinham com `opcoes = [True, False]` (a tela faz segmentado com o texto e grava a string `"false"`, que o `bool()` do `_ler` lia como **verdadeiro**) |
| Flange plate (mesmo arquivo) | 0,96 (parafusos da mesa) | as bordas fixas de 35 mm da chapa de alma com ¾" davam 0,91: nenhuma combinação chegava a ≤ 0,90 e a busca parava na primeira aprovada |

## Critério

- **Alvo do pré-dimensionamento**: `_buscar` devolve a primeira combinação da grade com taxa ≤ 0,90 em **todas** as
  verificações (resistência e disposições). Sem nenhuma, a primeira aprovada (como antes); sem aprovada, a de menor taxa.
  0,90 é o limiar de "no limite" da interface (`UiPlaca.NivelAp`).
- **Folga de 10 % nas disposições**: valor do sugerir = mínimo / 0,90, para cima a 5 mm (`_com_folga`).
  - Borda (`_borda`): tabela 14 (serra/tesoura) → ⅝" 35, ¾" 40, ⅞" 45, 1" 50 mm. Bate com o padrão CBCA 2.4.4 (40 mm,
    ¾") e fica acima dele onde ele cai no limite da tabela 14 (⅝" 30 contra 29; ⅞"/1" 40 contra 38/44).
  - Passo (`_passo`): CBCA 2.4.3 (60 mm ⅝", 75 mm os demais), no mínimo 3d (= 2,7d/0,9, o "preferível" da NBR 6.3.9) →
    60, 75, 75, 80 mm.
  - pf da chapa de topo: 30 → 35 (⅝", ¾"), 35 → 40 (⅞"), 40 → 45 (1"). Só o sugerir; `pf = 0` continua sendo o mínimo.
- **Emendas** (`_sugerir_emenda`; Pfeil 9.10.1, CBCA 2.4):
  - o mesmo diâmetro nas mesas e na alma; talas de mesa com largura bf e espessura comercial ≥ tf (lista do otimizador:
    W310×38,7 → 12,5; W250×73 → 16);
  - gabarito da mesa = bf/2 + 10, para baixo a 5 mm, sem passar da borda lateral com folga (W310: 90);
  - tala da alma até 0,9·(hw − 2r) (folga com os raios e a tolerância de laminação; "cabe entre as mesas" ≤ 0,90),
    1 ou 2 colunas por lado, linhas espalhadas na altura disponível (mais braço para M0 = Mw + V·a), passo vertical até
    0,9·min(24t, 300 mm) (6.3.10); tala da alma com espessura comercial ≥ max(0,6tw; 6,3);
  - busca: diâmetro ↑, parafusos da mesa ↑, espessura da tala da mesa, colunas e linhas da alma ↑.
- **Chapa de topo**: só o pf acima; a busca com alvo achou ⅞" (ajustada, tp 19, sem enrijecedor no pilar) e ⅝" (estendida, tp 12,5).
- **Flange plate**: o sugerir põe o diâmetro também na chapa de alma, com 3 ou 4 parafusos, passo `_passo` e altura
  (n − 1)·s + 2·borda (as bordas fixas de 35 mm do verificar não mudaram).
- **Sim/não** das rígidas (`rosca_no_plano`, `topo_pilar`, `borda_laminada`, `enrijecedor_chapa`, `mesa_soldada`,
  `minimo_50`, `contato`): sem `opcoes`, como nos outros módulos; a tela já faz Sim/Não para padrão booleano. O `_ler`
  aceita bool e texto ("true", "sim", "1" = sim; o resto = não).
- **Painel de alma** com `mesa_soldada` (o padrão): viga encostada na face da mesa do pilar (`pontas` com folga 0), mesas
  com penetração total (4 cordões) e alma com filete dos dois lados (perna max(tabela 10; 5 mm) ≤ tw, só desenho), todos
  com `campo = mesa_soldada`; aviso de que a solda da viga não é dimensionada neste tipo. Enrijecedores e chapas de reforço
  já eram desenhados quando há (`t_enr_pilar`, `t_reforco`). Parafusada: só o pilar (a ligação é outro tipo).

## Resultado (padrões de fábrica)

| Tipo | Antes | Depois | Padrão novo |
|---|---|---|---|
| chapa de topo ajustada | 1,00 (pf) | **0,89** (momento, T-stub) | ⅞" A325, tp 19, pf 40, de 45, g 80, sem enrijecedor |
| chapa de topo estendida | 1,00 (pf) | **0,88** (momento, T-stub) | ⅝", tp 12,5, pf 35, de 40 |
| flange plate | 0,96 (parafusos da mesa) | **0,83** (filete mínimo da chapa de alma) | ⅝", 3 por linha, alma 4 × ⅝" a 60, h 250 |
| painel de alma | 0,51 | 0,51 (desenho e campo) | viga soldada desenhada |
| emenda de viga | 0,97 (borda) | **0,89** (tala da alma: altura) | ⅝"; mesa 2 × 2 por lado, tala 12,5 × 165 × 290, g 90, s 60; alma 3 linhas × 2 colunas, tala 6,3 × 240 (s 85, bordas 35) |
| emenda de pilar | 0,97 (borda) | **0,90** (0,897; tala da alma: altura) | ⅝"; mesa 3 × 2 por lado, tala 16 × 254, g 135; alma 2 × 2, tala 6,3 × 180 (s 110) |

## Desenho 2D (`LigacaoDesenho.cs`, só `Cotas`, mudanças localizadas)

- Empate final na escolha da chapa da cadeia: a peça mais para fora do lado da cadeia (mais embaixo na horizontal, mais à
  direita na vertical). Lateral da emenda: a cadeia "37,5 | 90 | 37,5 / 165" vai para baixo da tala de baixo.
- Só entram na cadeia os furos que atravessam a chapa escolhida (fuste dentro dela nos outros dois eixos).
- A cadeia fica fora também das outras peças e parafusos da ligação que caem na mesma faixa (antes: só fora da chapa).
  Vale para todos os tipos; conferido nas capturas `w7lig` 05, 07, 08, 13, 17 e 20: cadeias iguais ou mais afastadas, sem
  sobreposição nova. O total cortado na borda do quadro pequeno (gusset, "217,9") já era assim antes e não foi mexido.

## Testes

- Motor: `testes/test_w8_rig.py`, 10 testes novos (regras de borda/passo/pf; busca sem alvo = comportamento antigo; os seis
  padrões ≤ 0,90; emenda de viga e de pilar coerentes; pf com folga; sim/não sem `opcoes` e "false" = não; painel com a
  viga soldada na face e com enrijecedores e reforço). Suíte: 1265 OK (9 pulados). Nenhum teste antigo alterado.
- Pórtico: 242 passam.

## Pendências

- A emenda de pilar é desenhada deitada (x ao longo da barra, como toda emenda no contrato). Vertical na tela pediria tratar
  a emenda de pilar no desenho (`LigacaoDesenho`), que outros agentes editam.
- Flexíveis ainda "no limite" (fora deste arquivo): cantoneira dupla 0,91 e apoio em cantoneira 0,91 (borda), chapa simples
  na alma 0,93.
- Elevação da flange plate: rótulo "ch 9,5 × 250" por cima do rótulo da viga (`Rotulos`, não mexido).
- Solda da viga no painel de alma e solda das talas: desenhadas, não dimensionadas (aviso).

## Fontes

- ABNT NBR 8800:2008: 5.7.2 a 5.7.9, 6.2.6 / tabela 10, 6.3.9, 6.3.10, 6.3.11.1 / tabela 14, 6.3.12.
- Gerdau/CBCA, *Ligações para Estruturas de Aço*, 7ª ed.: 2.4.3 (passo 60/75), 2.4.4 (borda 30/40), 6.1.1 (pf 30/35/40,
  bp ≈ bf + 25) — resumo em `docs/v3/ligacoes/CBCA.md`.
- Pfeil & Pfeil, *Estruturas de Aço*, 8ª ed., 9.10.1 (emenda de viga: tala de mesa com a espessura da mesa, talas de alma,
  M0 = Mw + V·a) — `docs/v3/ligacoes/DIMENSIONAMENTO-PRATICO.md` §8–9.
