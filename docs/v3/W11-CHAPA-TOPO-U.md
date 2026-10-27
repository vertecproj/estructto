# W11 — chapa de topo ajustada com viga U

Data: 27/10/2026. Branch `wt11-u` (do `unificacao` v0.6.17). Tipo `chapa_topo_ajustada` ("Chapa de topo ajustada").
Pedido do usuário, sobre a captura da chapa de topo W310×38,7 no W250×73: "adicione VIGA UPN a esse tipo de conexão,
será uma chapa do tamanho da viga escolhida com 5mm de sobra pra cada lado e se as vigas forem do mesmo tamanho não tem
essa folga. 2 parafusos por conexão e dentro da viga".

Código: `codigo/motor/prossen_estrutural/aco/ligacoes_rigidas.py` (`_chapa_topo_u`, `_sugerir_chapa_u`, `_folgas_u`,
`_d_max_u`, `_contorno_u`, `_pf_cbca`, `_cabeca`, `_E_CHAPA_TOPO_AJ`, `PERFIS_CHAPA_U`; `_perfil_iu` aceita Ue com
`ue=True`; `_minimo_6152` aceita a isenção). `codigo/Estructto/JanelaLigacaoProjeto.cs` (o Sugerir manda
`terca_ou_travessa`). Testes: `codigo/motor/testes/test_w11_chapa_topo_u.py` (17 testes, contas à mão na docstring).
Capturas: `ESTRUCTTO_CAPTURAS=w11u` (`CapturasW11U.cs`, `92-w11u-*.png` + `92-w11u.txt`), em `C:\dev\wt\w11-depois\`.

## 1. Perfis aceitos

- Viga: I/H (como antes) e U — UPN, U laminado, U e Ue dobrados. Pilar: I/H e U (com viga U).
- O seletor "Perfil da viga" / "Perfil do pilar" desta ligação recebe a lista do motor (`perfis` no campo, como a emenda
  flangeada do W9). O filtro I/H da tela (`SoPerfilI`) só vale para os campos sem `perfis`. Os dois campos refazem o
  Sugerir ao trocar o perfil (`resugerir`).
- As outras rígidas continuam recusando U (G5): chapa de topo estendida, flange plate, painel de alma, emendas com talas.
- Viga I/H em pilar U: recusa com o motivo ("pilar U nesta ligação só com viga U").
- No modelo (`ligacoes_modelo`), a viga U rígida na mesa do pilar continua `sem_tipo` (não mudou: pendência).

## 2. Geometria

- Referencial do contrato: x ao longo da viga, z para cima, o U abre para +y (`abertura_y` = −1 espelha).
- **Chapa** = envelope da seção do U + 5 mm de cada lado: (d + 10) × (bf + 10), das costas da alma − 5 à ponta das
  mesas + 5. **Viga e apoio do mesmo tamanho** (mesmos d e bf, ±0,5 mm): d × bf, sem sobra.
- **Apoio**:
  - pilar I/H: chapa encostada na mesa (x = d/2 do pilar);
  - pilar U: **as costas da alma do pilar encostam na chapa** (x = xg do pilar, centroide no nó; as mesas do pilar
    para −x). Decisão: é a face plana larga do U (largura = d do pilar); a ponta das mesas não tem face.
- **2 parafusos** numa linha vertical (um em cima do outro), dentro do U: entre as mesas, do lado de dentro da alma.
  Cabeça dentro do U (só segurada); porca e aperto do lado do apoio.
  - furo → face interna da mesa (`pf`) e furo → face interna da alma (`c_alma`, campo novo) ≥ cabeça/2 + perna do
    filete interno e ≥ db. Cabeça sextavada pesada (A325, ASME B18.2.6): F = 1,5d + ⅛", e = F/cos 30°. O canto da cabeça
    fica livre do pé do filete; db é a folga de chave da CBCA 6.1.1 (a mesma da chapa de topo junto à alma).
  - lado do apoio (aperto, CBCA 6.1.1): pilar I → furo a ≥ db da face da alma do pilar; pilar U → furo a ≥ pf
    (30/35/40 mm para ⅝"–1"; abaixo de ⅝", fora da tabela do CBCA, db + 12,7 mm = AISC DG4) da face interna das mesas
    do pilar.
  - 6.3.9 / 6.3.10 (2,7d ≤ s ≤ 24t e 300 mm), borda lateral da chapa do lado aberto (tabela 14, 6.3.12), borda na mesa
    do pilar (laminada), a face do apoio cobre a chapa; Ue: furo → enrijecedor de borda.
  - `pf = 0` e `c_alma = 0` = automático: o mínimo com a folga de 10 % do W8-RIG (para cima a 1 mm); se a folga não cabe,
    o mínimo. O Sugerir grava os valores resolvidos.
- **Não cabem** (U baixo): `ValueError` com o motivo e o diâmetro máximo que cabe (bisseção pelos mínimos, na altura
  2·pf + 2,7d e na largura furo → alma + borda). UPN 50: 4,6 mm; UPN 65: 7,7 mm; UPN 80: 10,9 mm (todos < M12: "use a
  solda direta ou outra ligação"). UPN 100 já leva ½" ou M12.

## 3. Modelo e verificações (NBR 8800:2008)

| Verificação | Item | Como |
|---|---|---|
| Momento nos 2 parafusos | 6.3.3.1 | M_ef = \|M\| + N⁺·(H − tf)/2 (`momento_efetivo`); T = M_ef/h, **h = braço real** do parafuso tracionado ao centro da mesa comprimida (zb + (H − tf)/2). N maior que o binário: os dois tracionados, N/2 ± M/s |
| Chapa (flexão e alavanca) | 6.3.5 (EN 1993-1-8 6.2.4) | T-stub da EN por parafuso (`tstub_en`) com o parafuso no canto alma–mesa: m = c − 0,8·b_alma (até o pé do filete, EN fig. 6.2), m2 = pf − 0,8·b_mesa, e = borda lateral; leff = α·m (EN fig. 6.11, a mesa no lugar do enrijecedor; `leff_junto_enrijecedor`), sem α: min(2πm, 4m + 1,25e). T ≤ modo 1; B = T + Q ≤ Ft,Rd |
| Mz | 6.3.3.1 | binário da linha dos parafusos até a borda mais próxima do contorno do U (os dois parafusos) |
| Cisalhamento | 6.3.3.2 | V/2 por parafuso + torção (T + V·y)/s: V no eixo da barra, parafusos em y (excentricidade no plano da chapa) |
| Tração + cisalhamento | 6.3.3.4 | (B/Ft,Rd)² + (Vb/Fv,Rd)² ≤ 1, B = o maior entre chapa e apoio |
| Pressão de contato | 6.3.3.3 | chapa (lf = min(s − dh, furo → borda)) e apoio (mesa do pilar ou alma do pilar U, lf = s − dh) |
| Chapa ao cisalhamento | 6.5.5 | bp·tp bruta e líquida (e na vertical com Vz) |
| Viga U | 5.4.2 / 5.4.3.1 | MRd = min(Zx; 1,5Wx)·fy/γa1; VRd com Aw = d·tw |
| Solda viga–chapa | 6.2.5 / tabela 8 | contorno do U (8 lados; Ue: 12) pelo grupo elástico do W10 (`_grupo`, `_q_lado`), com a menor das duas pernas; torção T + V·yc. O lado externo (costas, faces externas e pontas das mesas) só é soldado se a perna cabe na sobra; sem sobra (mesmo tamanho), só o contorno interno, com aviso |
| Pernas mínimas | 6.2.6.2.1 / tabela 10 | alma: min(tw, tp); mesas: min(tf, tp). U dobrado: não verificadas (perna = t), aviso, como no W9 |
| Apoio I: mesa | 6.3.5 | T-stub de Struik–de Back (`tstub`): b = \|y\| − tw/2 do pilar, a até a borda, p = min(s; 3,5b)/2 + 3,5b/2 |
| Apoio I: alma e painel | 5.7.3, 5.7.4, 5.7.7.1 | compressão C = T − N na mesa comprimida, ℓn = tf + 2tp + 2b; painel com V = M/h + M_outra/h − V_pilar |
| Apoio U: alma à tração | 6.3.5 (EN 6.2.4) | T-stub da EN entre as mesas do pilar: m = até a face da mesa mais próxima − 0,8r, e = até a outra; leff = min(2πm; 4m + 1,25e) |
| Apoio U: alma à compressão | EN 1993-1-8 tab. 7.11 (NBR 16239) | analogia com a face de um tubo retangular apoiada nas mesas: N1,Rd = fy·tw²/(1 − β)·(2η + 4√(1 − β))/γa1, β = bf da viga / d do pilar (≤ 0,85: a favor), η = (tf + 2tp)/d; kn = 1 |
| 6.1.5.2 | 6.1.5.2 | força da ligação = max(√(V² + N²), M/h) (a tração no parafuso); abaixo de 45 kN os esforços sobem até 45 kN. Campo novo `terca_ou_travessa` (terça/travessa de fechamento): isenta, como nos flexíveis |

Avisos: centro de cisalhamento do U (a torção da barra V·e **não é verificada**; nos parafusos entra V·y), modelo
adotado, contorno sem solda, campos do I não usados (`bp`, `g`, linhas extras, enrijecedores do pilar), penetração
das mesas não usada (filete em volta do U), "momento além da capacidade de 2 parafusos dentro do U" quando o momento, a
chapa ou a interação reprovam.

**Não verificado:** torção da barra pelo centro de cisalhamento; enrijecedores do pilar com viga U (ignorados);
redução kn da alma do pilar U pela compressão do pilar; flambagem das mesas do pilar U (β > 0,85 usado como 0,85).

## 4. Pré-dimensionamento (`_sugerir_chapa_u`)

- Diâmetros ⅝", ¾", ⅞", 1" e depois ½" e M12 (ISO 8.8) nos U baixos; espessuras 8 a 31,5 mm.
- Pernas: a menor comercial ≥ mínima/0,90. Com sobra de 5 mm, a perna do lado externo fica limitada à sobra (o contorno
  inteiro soldado). Se a mínima da tabela 10 já é 5 mm (tw ou tf entre 6,35 e 12,5 — quase todo UPN ≥ 80), a perna é
  5 mm = mínima: a verificação "perna mínima" fica **1,00** e leva `limite_sobra: true`; ela não conta para o alvo de
  0,90 (o resto fica ≤ 0,90). Ver §6.
- A 1ª combinação com taxa ≤ 0,90 (W8-RIG); senão a primeira que passa; senão a de menor taxa (o verificar diz
  "momento além da capacidade de 2 parafusos dentro do U"). Nada cabe: o `ValueError` com o diâmetro máximo.
- `pf` e `c_alma` saem resolvidos; `bp`, `g`, `n_linhas_extras` zerados (vinham do padrão W310).
- A tela manda `terca_ou_travessa` no Sugerir (`JanelaLigacaoProjeto.Sugerir`).

## 5. Resultado

| Caso | Esforços | Sugerir | Taxa |
|---|---|---|---|
| Padrão de fábrica W310×38,7 no W250×73 | V 100, M 60 | ⅞", tp 19 (inalterado) | **0,895** |
| UPN 200 na mesa do W250×73 | V 40, M 8 | ⅝" A325, tp 12,5, pf 23, c 36, filetes 5; chapa 85 × 210 | **0,895** fora a perna mínima = sobra (1,00) |
| UPN 100 nas costas da alma do UPN 100 (sem sobra), terça/travessa | V 10, M 1,5 | ½" A325, tp 8, pf 21, c 19, filetes 4/6 (só dentro); chapa 50 × 100 | **0,897** |
| idem sem a isenção (6.1.5.2: T = 45 kN) | V 10, M 1,5 | — | 1,38 (alma do UPN 100 com 6 mm: modo 1 = 35,5 kN < 45 kN) |
| UPN 200 no W250×73 | V 40, M 40 | ⅞", tp 25 | 2,58: "momento além da capacidade de 2 parafusos dentro do U" |
| UPN 50 no UPN 120 (o caso antigo) | V 5, M 0,5 | — | recusa: "não cabem dentro do U … diâmetro máximo 4,6 mm" |

Contas à mão (docstring de `test_w11_chapa_topo_u.py`): UPN 200 — Ft,Rd 90,72; h 159,75 mm; T 50,08 kN; Vb 21,15 kN;
leff 172,4 mm; B 64,55 kN (0,712); interação 0,697; mesa do pilar 169,6 kN; 5.7.3 526,6 kN; contorno 683 mm.
UPN 100/UPN 100 — h 66,25; T 22,64; alma do pilar U 35,46 kN (0,638; sem isenção 1,269); compressão 74,94 kN.

## 6. Decisão para o usuário: perna mínima × sobra de 5 mm

Com 5 mm de sobra, o filete do lado de fora do U só tem 5 mm de chapa para apoiar a perna. A NBR 8800 (tabela 10) pede
5 mm de perna mínima para metal-base de 6,35 a 12,5 mm (alma/mesa do UPN 80 a UPN 220). Então o filete externo é
exatamente a mínima e a tela mostra "perna mínima 100 %" (passa; é a mínima da norma). Opções:
1. manter (padrão atual): 5 mm em volta do U inteiro, perna = mínima;
2. sobra de 6 mm: filete de 6 mm em volta (≤ 0,90);
3. soldar só por dentro do U com 6 mm (≤ 0,90; o lado de fora sem solda).

## 7. Testes e capturas

- Motor: `testes/test_w11_chapa_topo_u.py`, 17 testes (seletor e recusas; chapa com e sem sobra e espelhada; 2
  parafusos dentro do U; UPN 50/65 sem caber, com o diâmetro máximo; contas à mão dos casos A e B; solda interna sem
  sobra; avisos; momento alto; sugerir UPN 200 e UPN 100/UPN 100; padrão W310 inalterado; desenho com `membros.pecas`,
  Ue com 5 peças, pilar U). Nenhum teste antigo mudou.
- Capturas `w11u`: `92-w11u-{upn200-w250, upn100-upn100, upn50-upn120}-{vistas, elevacao, planta, lateral, 3d}.png`.
  Cotas automáticas do desenho: altura (210 / 100), largura (49,5 + 35,5 = 85 / 25 + 25 = 50) e posição dos parafusos
  (39,5 · 131 · 39,5; 29,5 · 41 · 29,5) nas vistas 2D. O 3D do projetista não tem cotas (como os outros tipos).
  UPN 50 no UPN 120: as vistas ficam vazias e o cabeçalho mostra a recusa com o motivo.

## 8. Pendências

- Modelo 3D (`ligacoes_modelo`): viga U rígida na mesa do pilar continua sem tipo; poderia propor esta ligação.
- Torção da barra U (centro de cisalhamento) só em aviso.
- Enrijecedores do pilar com viga U não tratados (ignorados com aviso).
- Viga U chegando ao pilar U com as mesas para o pilar (ponta das mesas na face): não tratado (a face é sempre as costas).
