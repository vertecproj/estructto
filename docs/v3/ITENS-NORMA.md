# Itens de norma "a conferir": verificação (2026-10-21)

Este documento confere os itens marcados "a conferir" em `ENTREGA.md` e `STATUS.md`. O código do motor não foi alterado. Os achados
estão em `codigo/motor/testes/test_itens_norma_verif.py`: os testes `IN0x` são `expectedFailure` e falham hoje.

## Fontes

| Sigla | Fonte | Observação |
|---|---|---|
| [8681] | ABNT NBR 8681:2003, texto integral (cópia web, galaxcms.com.br, "cópia não autorizada") | 5.1.4.1 b) p. 8; tabelas 1 a 6, p. 9–11 |
| [8800] | ABNT NBR 8800:2008, 2ª ed., texto integral (cópia web, engcivil20142.wordpress.com) | páginas da norma (PDF = norma + 10) |
| [CYPE] | CYPE 2016: `unmetcomp.dll`, `unionbase.dll` (textos UTF-16); `cype-analise/.../21-sismo-combinacoes-nbr.md` (`nb1.dll`) | strings lidas como dados |
| [Pfeil] | Pfeil & Pfeil, *Estruturas de aço: dimensionamento prático*, 8ª ed. (`referencias/livros`) | páginas do livro |
| [DG1] | AISC Design Guide 1, 2ª ed., tabela 2.3 = AISC Manual, 15ª ed., tabela 14-2, reproduzida em AISC *Engineering Journal*, 2º trim. 2024, p. 92 | |
| [UFMG] | L. P. Martins, monografia de especialização, UFMG 2022 (compara a NBR 8800:2008 com o PR 2022) | |
| [ABCEM] | Construmetal 2016, "Formulação do modo de flambagem distorcional em perfis de aço" | |

As cópias web das normas são usadas só para conferência. Antes de citar em memorial, confira no exemplar licenciado.

## 1. NBR 8681:2003, tab. 3: γε de recalques: **confirmado**

- [8681] 5.1.4.1 b), p. 8: "efeitos de recalques de apoio e de retração dos materiais: neste caso adotam-se os valores indicados na tabela 3".
- Tabela 3 (p. 10), desfavorável / favorável:
  - normal: 1,2 / 0;
  - especial ou de construção: 1,2 / 0;
  - excepcional: 0 / 0.
- A nota da tab. 5 permite considerar separadamente os recalques (tab. 3) mesmo com as ações agrupadas. É o que o motor faz.
- O `GAMA_RECALQUE` de `combinacoes.py` confere. O teste `test_tabela3_gama_epsilon` protege esses valores.
- Os números das tabelas 2, 4, 5 e 6 citados em `combinacoes.py` também conferem.
- Conferência cruzada: [CYPE] `nbac03cmb` dá 1,2 (normal) e 0 (excepcional).
- [Pfeil] tab. 1.5 (p. 41) traz "(1,00)" como valor favorável. Isso diverge da norma. Vale a norma (0).
- A observação "sem o texto da NBR 8681 à mão" no código pode sair.

## 2. NBR 8800:2008, 5.7.x e 5.4.2: numeração **corrigida em parte**

Sumário e texto de [8800], com conferência em [CYPE] `unmetcomp.dll` ("ABNT NBR 8800:2008, 5.7.2 Flexão local da mesa", "5.7.3", "5.7.4", "5.7.7") e em [UFMG] p. 13 e 29:

| Item | Título na norma | Página | Uso no motor |
|---|---|---|---|
| 5.7.2 | Flexão local da mesa (6,25·tf²·fy/γa1; metade se a força está a menos de 10tf da extremidade) | 58 | confere |
| 5.7.3 | Escoamento local da alma (1,10(5k + ℓn) ou (2,5k + ℓn)) | 58 | confere |
| 5.7.4 | Enrugamento da alma (0,66 / 0,33) | 59 | confere |
| 5.7.5 | Flambagem lateral da alma | 59 | não verificada (a viga chega ao pilar com as mesas travadas) |
| 5.7.6 | Flambagem da alma por compressão (24tw³√(E·fy)/(h·γa1)) | 60 | confere |
| 5.7.7 | Cisalhamento do painel de alma; **chapas de reforço em 5.7.7.2** | 61 | ver IN02 e IN05 |
| 5.7.8 | **Apoios ou extremidades de vigas sem restrição à rotação** | 61 | o motor usa 5.7.8 para os enrijecedores: errado (IN04) |
| 5.7.9 | **Exigências adicionais para enrijecedores** (5.7.9.1 tração; 5.7.9.2–4 compressão, faixa 12tw/25tw, KL = 0,75h; 5.7.9.5 b + tw/2 ≥ bf/3, t ≥ tf/2, **t ≥ b/15**) | 61–62 | o motor usa 5.7.9 para a chapa de reforço: errado |
| 5.4.2.5 | Furos na mesa tracionada (fu·Afn ≥ Yt·fy·Afg; senão MRd = fu·Afn·Wt/(Afg·γa1)) | 49 | fórmula confere; o motor cita "5.4.2" (BAIXO) |
| 5.4.2.2 | MRd ≤ 1,50·W·fy/γa1 | 47 | confere |

## 3. NBR 8800:2008, 6.1 (45 kN e excentricidade) e 6.5.4: **subitens confirmados; falta aplicar o mínimo**

- **6.1.5.2** (p. 63), "Resistência mínima de ligações": ligações com força de cálculo menor que 45 kN são dimensionadas para 45 kN.
  - Exceções: diagonais e montantes de travejamento de barras compostas, tirantes de barras redondas, travessas de fechamento e terças.
  - [CYPE] `unionbase.dll` cita o mesmo item: "solicitação mínima de cálculo de %s (item 6.1.5.2 ABNT NBR 8800:2008)".
  - A 6.1.5.3 recomenda, a critério do projetista, 50 % de NRd da barra.
  - O livro só cita "NBR 8800": [Pfeil] p. 238 (não p. 232).
- **6.1.8.2** (p. 64): em cantoneiras simples ou duplas, a excentricidade da ligação pode ser desprezada sob ação estática. Sob fadiga, deve ser considerada.
- **6.5.4** (p. 87), "Elementos comprimidos": FRd = Ag·fy/γa1 se KL/r ≤ 25; acima, vale a 5.3. Confere com [Pfeil] 3.3.8, p. 73.
- **Achado IN01**: `verificar_gusset` não aplica o mínimo de 45 kN. O motor só o aplica em `no_trelica_gusset` e `cantoneira_simples_trelica`. Com N = 10 kN, o gusset é dimensionado para 10 kN.
- `no_trelica_gusset` aplica os 45 kN também a barra redonda, que está entre as exceções. Isso fica a favor da segurança.
- `ligacoes_flexiveis` e `ligacoes_rigidas` também não aplicam o mínimo (pendência).

## 4. Furo alargado da placa de base (V2-03): **confirmado**

- O `placa_base.py` usa estes acréscimos ao diâmetro: ≤ 3/4" +14,3; ≤ 7/8" +17,5; ≤ 1½" +20,6; ≤ 1¾" +25,4; acima +31,8 mm.
- Esses valores são exatamente os da [DG1] tab. 2.3 / Manual tab. 14-2: 3/4→1 5/16; 7/8→1 9/16; 1→1 13/16; 1¼→2 1/16; 1½→2 5/16; 1¾→2¾; 2→3¼; 2½→3¾ pol.
- O `AISC-DG1.md` §1.5 ("d + 5/16" a d + 1/2"") está desatualizado e deve ser corrigido.
- [8800] tab. 12 (p. 83), furo alargado para parafusos: db + 5 (db ≤ 24), 33 (db = 27), db + 8 (db ≥ 30) mm. [Pfeil] fig. 3.7, p. 67, traz o mesmo.
- [8800] 6.3.6.1 (p. 82) permite furos maiores em placas de base, **com arruelas especialmente dimensionadas soldadas à placa**.
- Os furos do AISC excedem a tab. 12. A NBR então exige a arruela soldada, mas o motor aceita `arruela_soldada = False` só com o aviso do DG1. Isso é a pendência IN06 (aviso).

## 5. NBR 14762: distorcional do Ue 150x60x20x2,65: **a fórmula não é usada fora da validade; o motor recusa**

- A compressão é dispensada pela tab. 11: bf/bw = 0,4 e bw/t = 56,6 dão D/bw mín. = 0,075, e D/bw = 0,133.
- A flexão em x **não** é dispensada pela tab. 14: o mínimo é 0,25 − 0,13·(6,6/50) = 0,233, maior que 0,133.
- O motor então tenta a expressão simplificada do AISI (S100-07 C3.1.4(a)/C4.2(a)). Os limites dela estão certos no código, e bo/t = 60/2,65 = 22,6 fica abaixo de 25.
- Fora dos limites, a expressão não é usada: o motor devolve `nao_calculado` e a barra reprova como "não verificada". A reprovação é de cobertura, não de resistência.
- A NBR 14762:2010 9.7.3/9.8.2.3 não traz expressão própria de σdist. Ela exige Ndist/Mdist de análise de estabilidade elástica, ou a dispensa pelas tabelas 11/14. [ABCEM] confirma que não há fórmula prática na norma.
- As tabelas 11 e 14 do código conferem nas linhas usadas, e o χdist (0,561 / 0,673) confere.
- Os valores de tabela vêm das fontes já citadas no módulo. Não há texto oficial da NBR 14762 à mão.
- Pendência (MÉDIO): calcular Mdist por faixas finitas (tipo CUFSM) ou pela expressão racional do AISI S100-07 C3.1.4(b), que não tem limites geométricos. Enquanto isso, o usuário informa `Mdist`.
- O teste `test_ue150_flexao_nao_dispensada_e_fora_do_aisi` protege esse comportamento.

## Achados

| Id | Sev. | Onde | Esperado × obtido | Fonte |
|---|---|---|---|---|
| IN01 | CRÍTICO | `ligacoes_contraventamento.verificar_gusset` | ligação com N = 10 kN dimensionada para 45 kN × 10 kN | [8800] 6.1.5.2 p. 63 |
| IN02 | CRÍTICO | `ligacoes_rigidas.cisalhamento_painel` | VRd do painel pela 5.4.3 (λ, kv com a = distância entre mesas da viga) × sempre 0,6·fy·dc·tw/γa1; pilar 400×4,75 A572-50 + W610x155: 317,5 × 357,5 kN (+12,6 %) | [8800] 5.7.7.1 p. 61, 5.4.3.1.1 p. 50 |
| IN03 | CRÍTICO (impacto baixo) | `ligacoes_rigidas._pilar` | t ≥ b/15 (5.7.9.5 b) × b/t ≤ 0,56√(E/fy), que vale 15,8 com fy = 250; t = 9,0 mm, b = 137,5 mm passa no motor | [8800] 5.7.9.5 p. 62 |
| IN04 | BAIXO | `ligacoes_rigidas` (COBERTURA, `_pilar`) | enrijecedores = 5.7.9; chapa de reforço = 5.7.7.2; furos na mesa = 5.4.2.5; mínimo 45 kN = 6.1.5.2; excentricidade = 6.1.8.2 × 5.7.8 / 5.7.9 / 5.4.2 / 6.1 | [8800] |
| IN05 | MÉDIO | `ligacoes_rigidas._pilar` (chapa de reforço) | chapas "dispostas dos dois lados da alma", estendidas 150 mm além das forças e dimensionadas pela 5.4 × uma chapa, só na altura da viga; (dz + wz)/t ≤ 90 é do AISC 341, não da NBR | [8800] 5.7.7.2 p. 61 |
| IN06 | MÉDIO | `placa_base.cisalhamento_base` | aviso de que furo maior que a tab. 12 exige arruela soldada × só o aviso do DG1 | [8800] 6.3.6.1 p. 82 |
| IN07 | BAIXO | `ligacoes_rigidas.cisalhamento_painel` | a 5.7.7.1 de 2008 escreve FSd na condição de 0,4·Npℓ; o motor usa a compressão do pilar (AISC J10.6, PR 2022), que é a intenção. Documentar | [8800], [UFMG] p. 29 |
| IN08 | MÉDIO | `ligacoes_flexiveis`, `ligacoes_rigidas` | mínimo de 45 kN (6.1.5.2) não aplicado | [8800] 6.1.5.2 |

## Sem verificar
- Texto oficial da NBR 14762:2010. As tabelas 11/14 foram conferidas só contra as fontes do módulo.
- Valor numérico usado pelo CYPE com o 6.1.5.2: só o texto foi lido, não o `%g`.
- Mdist real do Ue 150x60x20x2,65 (não foi rodado FSM).
