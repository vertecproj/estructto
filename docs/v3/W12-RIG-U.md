# W12-RIG — perfil U nas ligações rígidas e emendas

Data: 28/10/2026. Branch `w12-rig`. Pedido do usuário: "as outras ligações não têm os U e UPN também, revise isso".
Briefing comum: `C:\dev\wt\W12-U-comum.md`. Padrão de referência: W11 ([W11-CHAPA-TOPO-U.md](W11-CHAPA-TOPO-U.md)).

Código: `codigo/motor/prossen_estrutural/aco/ligacoes_rigidas.py` (`_emenda`, `_linha_mesa_u`, `_sem_linha_u`,
`_perfil_emenda`, `verificar_flange_plate`, `verificar_painel_alma`, `verificar_chapa_topo_estendida`, `_sugerir_emenda`,
`_sugerir_flange`, `_E_FLANGE`, `_E_PAINEL`, `_e_emenda`), `aco/ligacoes_modelo.py` (`_membro`, `_solda_direta_padrao`),
`codigo/Estructto/Render3DMotor.cs` (`TipoProposto`). Testes: `codigo/motor/testes/test_w12_rig_u.py` (11 testes, contas
à mão na docstring). Capturas: `ESTRUCTTO_CAPTURAS=w12rig` (`CapturasW12Rig.cs`, `93-w12rig-*.png` + `93-w12rig.txt`).

## 1. Decisão por tipo

| Tipo | U | Por quê |
|---|---|---|
| emenda_viga, emenda_pilar | **aceito** (UPN, U laminado, U dobrado); Ue recusado | talas por fora das mesas e na alma funcionam com U; o enrijecedor do Ue fica na frente da porca do parafuso da mesa |
| flange_plate | **aceito** com viga U e pilar I/H | as chapas de mesa levam M/(H + t) como no I; pilar U recusado (a chapa soldaria na alma do U fora do plano) |
| painel_alma_pilar | **viga U aceita**, pilar U recusado | as mesas do U entregam M/(H − tf) à mesa do pilar I/H como as de um I. Pilar U: a 5.7 (e a EN 1993-1-8 6.2.6) é de seção I/H; a mesa do U tem a alma numa borda (5.7.2 supõe a alma no meio) |
| chapa_topo_estendida | **recusado** (viga e pilar U) | a linha fora da mesa precisa de 2 parafusos dos dois lados da alma, com a mesa como "alma" do T-stub; o U tem mesa de um lado só. A mensagem aponta a chapa de topo ajustada (W11) ou a solda direta |
| chapa_topo_ajustada, viga I em pilar U | **continua recusado** | a chapa do I tem 2 colunas de parafusos e a mesa comprimida larga: a alma fina do U trabalharia fora do plano como placa entre as mesas, sem modelo na norma. A analogia da face de tubo do W11 vale para 1 linha no meio da alma |

## 2. Emenda com talas com U (viga e pilar)

Referencial do contrato: x ao longo da barra (origem no eixo da emenda), z = altura, o U abre para +y·ab (`abertura_y`),
centroide em y = 0, costas da alma em y = −ab·xg (o mesmo da emenda flangeada W9 e do desenho `membros`).

- **Mesas**: uma tala por fora de cada mesa, das costas da alma até bm (0 = bf), com **1 linha de parafusos** por mesa
  (corte simples). Duas colunas não cabem na mesa do U (UPN 300: 90 mm livres; precisa ≥ 104 mm com ⅝").
  - Posição `g_u` (furo → costas da alma; 0 = auto): mínimo furo → face interna da alma = max(db; cabeça/2 + max(r, ta))
    (a porca fica dentro do U, livre do raio de concordância e da tala interna; CBCA 6.1.1, como `_folgas_u` do W11);
    furo → ponta da mesa ≥ borda da tabela 14. Auto = meio do intervalo com a folga de 10 % (W8-RIG), a 1 mm; sem a folga
    se não cabe; nem assim: recusa com as medidas.
- **Alma**: tala nas costas sempre; por dentro do U opcional (`tala_alma_interna`, padrão sim → corte duplo; não → corte
  simples, aviso da excentricidade (tw + ta)/2 não verificada). Altura: com a tala interna ≤ hw − 2r; só a das costas ≤ d.
- Divisão do momento de Pfeil (9.1–9.3), Af = bf·tf, Aw = (d − 2tf)·tw: as mesmas fórmulas do I.

| Verificação (U) | Item | Como |
|---|---|---|
| Parafusos da mesa | 6.3.3.2 | Fmax/(1·n), corte simples |
| Pressão de contato | 6.3.3.3 | tala e mesa do perfil, soma por furo, 1 linha |
| Colapso por rasgamento | 6.5.6 | 1 plano de cisalhamento (a linha) e tração até a ponta da mesa; na tala também o bloco do lado das costas (o menor). Na mesa do perfil o lado da alma não forma bloco |
| Tala da mesa | 6.5.3 / 6.5.4 | An = (bm − dn)·tm; compressão K = 0,65 |
| Mesa do U, seção líquida | 5.2.2 b) / 5.2.4 | Ft ≤ (bf − dn)·tf·fu/γa2 |
| U inteiro à tração (N > 0) | 5.2.2 b) / 5.2.5 a) | N ≤ An·fu/γa2, An = A − 2·dn·tf − nr·dn·tw, Ct = 1 (mesas e alma ligadas) |
| Viga: furos na mesa tracionada | 5.4.2.5 | Afn = (bf − dn)·tf |
| Parafusos da alma | 6.3.3.2 | grupo excêntrico com M0 = Mw + V·a; 2 ou 1 plano |
| Talas da alma | 6.5.5 / 6.5 / 6.5.3 / 6.5.4 | cisalhamento, flexão, Nw e interação com 1 ou 2 talas |
| Folgas de chave | CBCA 6.1.1 | furo da mesa → face interna da alma ≥ c_min; furo da alma → face interna da mesa ≥ cabeça/2 + r |
| Bordas | tabela 14 / 6.3.12 | lateral da tala (o menor lado), ponta da mesa do U (laminado: borda laminada) |

Avisos: centro de cisalhamento (torção da barra não verificada; as talas levam os esforços pelas mesmas peças dos dois
lados), modelo adotado, U dobrado (parafusos pela NBR 8800, não pela NBR 14762 9.3), UPN (mesa com 8 %: arruela biselada).

Correção junto: 1 parafuso de cada lado na alma (`n_linhas_alma = n_colunas_alma = 1`) com M0 > 0 agora é recusado
(o grupo de 1 parafuso tinha Σr² = 0 e ignorava o momento). Vale para I e U. `h_tala_alma` mínimo 60 → 40 mm (UPN 100).

**Sugerir** (U): ⅝" a 1", depois ½" e M12 (ISO 8.8), como o W11; `g_u` = 0; tala interna primeiro; na alma desde 1
linha (com 2 colunas). O I não mudou (mesma grade).

## 3. Flange plate com viga U

Chapas de mesa das costas da alma à ponta da mesa (largura `b_chapa`, 0 = bf), 1 linha de parafusos por mesa (`g_u`,
mesmo `_linha_mesa_u`), colapso com 1 linha, seção líquida da mesa do U, folga de chave; chapa de alma (chapa simples)
nas costas do U. Pilar I/H com as verificações de sempre (5.7) e mais "a mesa do pilar cobre a chapa de mesa do U" (o
centroide do U fica no eixo do pilar, a chapa fica deslocada). Sugerir: ½"/M12 nos U e chapa de alma com 2 a 4
parafusos.

## 4. Painel de alma com viga U

As forças das mesas e a geometria (solda de penetração nas mesas e filetes na alma) com a seção do U: mesas de −ab·xg a
ab·(bf − xg). Verificação nova: "a mesa do pilar cobre a mesa da viga U".

## 5. Modelo e tela

- `ligacoes_modelo._membro`: a viga U rígida na mesa do pilar não é mais `sem_tipo` (U_RIGIDA). A tela
  (`Render3DMotor.TipoProposto`) propõe `chapa_topo_ajustada` para viga U rígida (antes, chapa simples flexível).
  `_solda_direta_padrao`: se os 2 parafusos não cabem dentro do U (`_sugerir_chapa_u` levanta: UPN 50 a 80), o projeto
  padrão do grupo é a solda direta.
- Campos com `perfis` (I/H + U, sem Ue: `PERFIS_FLANGE`) e `resugerir`: `perfil_viga`/`perfil_pilar` das emendas com
  talas, `perfil_viga` da flange plate e do painel. Continuam só I/H pelo filtro da tela (`SoPerfilI`): chapa de topo
  estendida, `perfil_pilar` da flange plate e do painel.
- Campos novos: `g_u` (emendas e flange plate), `tala_alma_interna` (emendas). No I não são usados.

## 6. Resultados (Sugerir, taxa governante; todos com ok)

| Caso | Esforços | Sugerir | Taxa |
|---|---|---|---|
| emenda_viga UPN 200 | V 30, M 15 | ⅝", 2/lado/mesa, tala da mesa 12,5, almas 6,3 ×2 (2 linhas), g_u 41 | **0,877** (tala da alma cabe entre as mesas) |
| emenda_viga UPN 300 | V 60, M 40 | ⅝", 3/lado/mesa, tala 16, almas 8 ×2, g_u 56 | **0,890** |
| emenda_pilar UPN 240 (UPN 250 não está no catálogo) | N +300, M 20 | ⅝", 4/lado/mesa, tala 16, almas 6,3 ×2 (2 × 2) | **0,878** (na tela, vindo de outro padrão: 0,892) |
| emenda_pilar UPN 260 | N +300, M 20 | idem | **0,882** |
| emenda_pilar UPN 240 | N −300, M 20 (contato) | ⅝", 2/lado/mesa | **0,878** |
| emenda_viga U 200x75x4,75 | V 20, M 8 | ⅝", tala 6,3, g_u 38 | **0,875** |
| emenda_pilar U 200x75x4,75 | N −80, M 5 | idem | **0,875** |
| flange_plate UPN 240 / UPN 260 no W250×73 | V 40, M 25 | ⅝", 3 por mesa, chapa 12,5, alma 2 parafusos | **0,833** |
| flange_plate UPN 200 no W250×73 | V 30, M 15 | ⅝", 2 por mesa | **0,853** |
| painel_alma_pilar W250×73 com UPN 200 | V 40, M 30 | — | **0,417** |
| emenda_viga UPN 100 | V 10, M 3 | ½", 1 linha na alma | 1,00 (passa no limite da borda lateral da tala: a mesa de 50 mm não tem folga de 10 %) |

Contas à mão (docstring de `test_w12_rig_u.py`): UPN 200 — Mf 11,621 kN·m; Ft 58,11 kN; 29,05 kN por parafuso (Fv,Rd
48,38); g_u 41; seção líquida da mesa 213,2 kN; colapso da mesa 256,4 kN.

## 7. Testes e capturas

- Motor: 1368 + 11 novos (`test_w12_rig_u.py`). Mudados por mudança de comportamento pedida: `test_ligacoes_u`
  (`test_rigida_u_sem_tipo`: a estendida aponta a ajustada e o membro não é mais `sem_tipo`; espelho do `TipoProposto`)
  e `test_w11_chapa_topo_u` (seletor e recusas: só a estendida continua sem U).
- Pórtico: 242 OK.
- Capturas `w12rig`: `93-w12rig-{emenda-upn200, emenda-upn300, emenda-pilar-upn240, emenda-u200-dobrado,
  flange-upn240-w250, painel-upn200-w250}-{vistas, elevacao, planta, 3d}.png`. Conferido: na lateral a alma do U fica
  de um lado com as mesas para o lado aberto, a tala das costas por fora e a interna por dentro, a tala da mesa das
  costas à ponta, os parafusos da mesa dentro da mesa (cotas 41 · 34 no UPN 200, 47 · 38 no UPN 240); na flange plate a
  chapa de alma fica nas costas do U.

## 8. Pendências

- Torção da barra U pelo centro de cisalhamento: só aviso (como no W11).
- U dobrado: parafusos e chapas pela NBR 8800; a NBR 14762 9.3 (ligações parafusadas de chapa fina) não é aplicada.
- UPN 100 na emenda sai a 1,00 (borda lateral da tala sem folga).
- Pilar U no painel, na flange plate e na chapa de topo com viga I: recusados (ver §1).
- O desenho do painel de alma põe a viga a partir do eixo do pilar (já era assim com o I).
