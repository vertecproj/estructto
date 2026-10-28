# W12-FLEX-U — perfil U como apoio nas ligações flexíveis (e viga U no assento)

Data: 28/10/2026. Branch `w12-flex` (do `unificacao` v0.6.18). Pedido do usuário: "as outras ligações não têm os U e UPN
também, revise isso". Briefing comum: `W12-U-comum.md`. Referência de padrão: [W11-CHAPA-TOPO-U.md](W11-CHAPA-TOPO-U.md).

Código: `codigo/motor/prossen_estrutural/aco/ligacoes_flexiveis.py` (`_apoio_u`, `_cabe_no_apoio`, `_face_apoio`,
`recortes_viga_viga`, `_padroes`, `_recusa_mesa_u`, `_perfil_ou_nada`, ramo U de `_apoio`, `_sug_*`). Testes:
`codigo/motor/testes/test_w12_flex_u.py` (14 testes, contas à mão na docstring). Capturas: `ESTRUCTTO_CAPTURAS=w12flex`
(`CapturasW12Flex.cs`, `93-w12flex-*.png` + `93-w12flex.txt`). Nenhuma mudança em `ligacoes.py` nem na interface: os
tipos flexíveis já listam todos os perfis (sem `SoPerfilI`), então não precisa de `perfis=`.

## 1. Antes

O apoio U (UPN, U laminado, U e Ue dobrados) era medido como um I:
- alma do pilar U: largura ≤ altura livre entre as mesas `d − 2(tf + r)`; viga suporte U: "abaixo da mesa da viga
  suporte" e recorte da viga como no I (Gerdau 3.3 e o cope de `pontas.py`);
- mesa do pilar U: largura ≤ bf centrada e borda `(bf − tw)/2 − g` como se houvesse ponta dos dois lados;
- nenhuma conferência de onde os furos caem: duas linhas de parafusos na mesa de 100 mm do UPN 300 saíam com taxa
  `None` (borda negativa) e o desenho punha parafusos no ar; na alma, a porca podia cair no raio da mesa.
- Viga U no assento (`apoio_cantoneira`): calculada como I, sem aviso de torção nem conferência da mesa sobre o assento.

## 2. Convenção (a do desenho W7-UPN, `LigacaoDesenho.cs`)

| Apoio | Como o U fica | Face de contato |
|---|---|---|
| `alma_pilar`, `alma_viga` | costas da alma para a ligação (+x), mesas para −x; `x_face = tw/2` | plana, altura `d` inteira (as mesas estão do outro lado) |
| `mesa_pilar` | mesa centrada no nó (face em `x = d/2`), alma no lado `−ab·y` (`ab = abertura_y`), ponta livre em `+ab·y` | `bf`, com a alma num dos lados |

A viga **dentro do U** (entre as mesas, do lado aberto) não é tratada: o projetista desenha sempre as costas. No modelo 3D
(`contexto_pontas` presente) o cope que `pontas.py` calcula continua valendo (conservador); sem o modelo, o apoio U não
recorta a viga.

## 3. Critério por apoio

### Costas da alma do U (pilar ou viga suporte)
- Largura da peça (cantoneiras, chapa, assento, solda) ≤ `d` do U (`largura nas costas da alma do U`).
- Viga suporte U: a peça fica dentro da altura do U, `base + desnível ≤ d` (`dentro da altura da viga suporte U`);
  sem a verificação "abaixo da mesa" e **sem recorte** (`recortes_viga_viga` devolve zero).
- Furos na alma: a porca fica entre as mesas. Parte plana = `±(d/2 − tf − r)` (pilar) ou, na viga suporte, de
  `tf + r − desnível` a `d − tf − r − desnível` a partir do topo da viga apoiada (UPN: `tf + r = (h − d_alma)/2` pela alma
  reta tabelada). Folga de aperto até o pé do raio das mesas ≥ `1,25·db` (`FOLGA_APERTO`, a mesma regra da chapa de
  extremidade no pilar I, W7-LIG). Furo (± dh/2) fora da parte plana: **recusa** com o motivo.

### Mesa do pilar U
- Furos: `u = ab·y`; ponta livre em `u = bf/2`, pé do raio da alma em `u = −(bf/2 − tw − r)`.
  - borda até a ponta ≥ tabela 14, borda laminada (6.3.11.1);
  - folga de aperto da porca até o pé do raio da alma ≥ `1,25·db`;
  - furo fora da parte plana: **recusa** (ValueError) com o motivo e o que usar;
  - UPN: aviso da face interna inclinada (8 %, DIN 1026) — arruela de compensação.
- **Duas linhas de parafusos não cabem** (cantoneira dupla, chapa de extremidade): `bf ≥ g + 2(tw + r + 1,25db)` pede
  ~145 mm e o maior U do catálogo tem 110 mm. O sugerir recusa: "duas linhas de parafusos não cabem na mesa do U … use
  chapa_simples_alma ou solda_direta_alma soldadas na mesa, apoio em cantoneira com 1 coluna, ou ligue nas costas da alma
  do U (apoio alma_pilar)". O verificar com dados à mão recusa pela posição dos furos.
- Chapa simples e solda direta: soldadas na face externa (plana) da mesa, largura ≤ `bf`, como antes. Solda direta com a
  viga mais larga que a mesa (`bf_viga + 2b > bf`): o sugerir passa a `soldar_mesas = "não"` (só a alma, rótula).
- Apoio em cantoneira: o sugerir tenta antes **1 coluna** no eixo da mesa (⅝" e ½"), assento `5·⌊0,9·bf/5⌋`.

### Viga U no assento (`apoio_cantoneira`, `perfil_viga` U)
- A mesa inferior do U apoia no assento e a força vai para a alma: 5.7.3 (escoamento local) e 5.7.4 (enrugamento) com
  `tw`, `tf` e `k = tf + r` do U, como no I (a força entra pela mesa ligada à alma).
- `assento cobre a mesa do U`: o assento é centrado no nó (centroide do U), costas em `y = −ab·xg`, ponta em
  `y = ab·(bf − xg)` → comprimento ≥ `2·máx(xg, bf − xg)`.
- Torção: aviso com `e = x_cc + bf/2` (centro de cisalhamento → meio da mesa apoiada, limite superior) e `V·e`; não
  verificada (a cantoneira superior trava a rotação), como no W11.
- U formado a frio: aviso — 5.7.3/5.7.4 da NBR 8800 usados; conferir o esmagamento da alma pela NBR 14762 / AISI S100 G5.
- `geometria.abertura_y` vai no resultado (o desenho espelha o U).

## 4. Pré-dimensionamento (Sugerir)

- `_padroes`: com U (viga ou apoio) e viga > 150 mm, depois do padrão Gerdau vêm furações com o mesmo diâmetro e os
  maiores até ¾": `s = 3db` para cima a 5 mm (6.3.9) e borda = mínima da tabela 14 / 0,90. U de 150–215 mm em que 2
  parafusos ⅝" não cabem na alma reta (UPN 160: 115 mm) começa no padrão ½". I com apoio I: só o padrão (os padrões de
  fábrica não mudam). U ≤ 150 mm: como antes (sem 2 parafusos, troca para a solda direta, G5).
- Chapa simples nas furações a mais: perna = máx(5t/8, mínima da tabela 10) / 0,90 (com 5t/8 exato as duas verificações
  da solda saíam 1,00). Chapa de extremidade: chapa ≤ 0,90 × face e perna = mínima / 0,90.
- Troca da viga–viga para a solda direta: a perna agora é escolhida com `apoio = alma_viga` (antes, como na mesa do pilar).

## 5. Resultado (Sugerir → verificar, A-572 GR.50, A325; V abaixo de 45 kN sobe a 45, 6.1.5.2)

| Caso | V (kN) | Saída | Taxa | Governa |
|---|---|---|---|---|
| W310×38,7 nas costas da alma do UPN 300, chapa simples | 100 | chapa simples | 0,862 | borda máxima (ponta da viga) |
| idem, cantoneira dupla (antes 0,914: "largura entre as mesas") | 100 | L102, ¾", 2 parafusos | 0,829 | contato na aba do apoio |
| idem, chapa de extremidade | 100 | chapa de extremidade | 0,800 | borda vertical |
| idem, apoio em cantoneira | 100 | assento L102×152 | 0,867 | borda inferior da aba |
| idem, solda direta | 100 | solda direta | 0,833 | perna mínima |
| W310×38,7 na alma do UPN 300, viga–viga (antes 0,971, com recorte 28 × 45) | 100 | sem recorte | 0,829 | contato na aba do apoio |
| UPN 200 na mesa do W250×73, chapa simples (antes: trocava p/ solda direta, parafusos 1,28) | 40 | ¾", chapa 8, perna 6 | 0,889 | borda vertical |
| UPN 200 na mesa do W250×73, apoio em cantoneira | 40 | L102×152×9,5 | 0,867 | borda inferior da aba |
| UPN 200 nas costas da alma do UPN 300, apoio em cantoneira | 40 | L102×152×9,5 | 0,867 | borda inferior da aba |
| UPN 200 nas costas da alma do UPN 300, chapa de extremidade (antes 1,00) | 40 | ⅝", e_v 33, perna 6 | 0,879 | borda vertical |
| W200×26,6 na mesa do UPN 300, chapa simples | 40 | chapa simples | 0,898 | chapa acima da mesa inferior |
| W200×26,6 na mesa do UPN 300, apoio em cantoneira (antes 1,50) | 40 | 1 coluna ½", assento 90 | 0,900 | largura na mesa |
| W200×26,6 na mesa do UPN 300, solda direta (antes 1,53) | 40 | só a alma | 0,750 | perna mínima |
| W200×26,6 na mesa do UPN 300, cantoneira dupla / chapa de extremidade | 40 | **recusa** | — | 2 linhas não cabem |
| U 200×75×4,75 (dobrado) na mesa do W250×73, chapa simples | 30 | chapa simples | 0,879 | borda vertical |
| U 200×75×4,75 (dobrado) na mesa do W250×73, apoio em cantoneira | 30 | assento | 0,867 | borda inferior da aba |
| W250×32,7 nas costas da alma do U 300×100×4,75 (dobrado), cantoneira dupla | 40 | cantoneira dupla | 0,800 | borda vertical |
| idem, chapa simples | 40 | chapa simples | 0,888 | borda máxima (ponta da viga) |
| W200×26,6 nas costas da alma do U 150×50×3,75, chapa simples | 40 | chapa simples | 0,898 | chapa acima da mesa inferior |
| UPN 160 na alma do UPN 200, cantoneira dupla / viga–viga / chapa simples | 30 | **solda direta** (motivo) | 0,833 | perna mínima |
| U 150×50×3,75 no UPN 200 (pilar, chapa simples; viga–viga) | 20 | **solda direta** (motivo) | 0,750 | perna mínima |

"U 150×50×4,75" do pedido não existe no catálogo; os casos de U dobrado usam U 150×50×3,75, U 200×75×4,75 e
U 300×100×4,75.

**UPN 160 com cantoneira dupla não chega a 0,90 (nem passa)**: a cantoneira de dentro tem de caber entre as mesas
(`b1 ≤ bf − tw = 57,5 mm` → L51), e com ½" a ponta da viga (`g − folga = 30 − 10 = 20` contra 22 mm da tabela 14, borda
cortada) e a borda da aba (`51 − g ≥ 19`) não fecham juntas (pediria folga ≤ 5 mm). O sugerir troca para a solda direta
(0,833), com o motivo. A chapa simples do UPN 160 reprova na flexão `V·a` da chapa (Lc = 90 mm); também troca.

## 6. Recusas

| Caso | Mensagem (resumo) | Usar |
|---|---|---|
| Duas linhas de parafusos na mesa do U (cantoneira dupla, chapa de extremidade, assento com 2 colunas) | "os furos … caem fora da mesa do UPN 300 (bf = 100 mm, alma num dos lados)" / sugerir: "duas linhas de parafusos não cabem na mesa do U" | chapa simples ou solda direta na mesa, assento com 1 coluna, ou as costas da alma (`alma_pilar`) |
| Furo fora da parte plana da alma do U (no raio ou na mesa) | "os furos … caem fora da alma do UPN 120 entre as mesas (parte plana 52 mm)" | menos parafusos, gabarito menor, parafuso menor ou solda direta |

## 7. Testes e capturas

- Motor: `testes/test_w12_flex_u.py`, 14 testes: casos A–D à mão (folga de aperto 48,1/23,81; largura 209,8/300; mesa
  22/15,875 e 50/19; viga–viga 190/300 e 41; 5.7.3 449,41 kN; assento 105,98; e 59,95), recusas (verificar e sugerir),
  solda direta só na alma, aviso de U dobrado, assento estreito, tabela de taxas do Sugerir (15 casos ≤ 0,90),
  `_padroes`. Nenhum teste antigo mudou.
- Capturas `w12flex`: `93-w12flex-{cd-w310-alma-upn300, cs-upn200-mesa-w250, vv-w310-upn300, ap-w200-mesa-upn300,
  ap-upn200-mesa-w250, ce-upn200-alma-upn300}-{elevacao, planta, 3d}.png` e `93-w12flex-cd-w200-mesa-upn300-recusa.png`
  (o motivo na tela). Log `93-w12flex.txt` com tipo, taxa, piores verificações, parafusos e avisos.

## 8. Pendências

- Viga dentro do U (lado aberto do apoio, entre as mesas): não tratada; o projetista sempre usa as costas.
- `pontas.py` não sabe o lado das mesas do U; no modelo 3D o cope dele continua (conservador).
- Flexão local da alma/mesa do apoio U (punção, T-stub) não verificada, como no apoio I dos flexíveis (aviso existente).
- Esmagamento da alma de U formado a frio pela NBR 14762 / AISI S100: só aviso.
- UPN 160 com cantoneira dupla: sem combinação ≤ 0,90 com folga de 10 mm (ver §5).
