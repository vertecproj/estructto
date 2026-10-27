# W8-FLEX — padrões de fábrica das ligações flexíveis abaixo de 0,90

Data: 27/10/2026. Branch `wt8-flex` (de `wt8-tubo`). Pendência do W8-RIG: três flexíveis saíam "passa no limite" (> 0,90)
no padrão de fábrica (W310×38,7 na mesa do W250×73, V = 100 kN, ¾" A325). Arquivo:
`codigo/motor/prossen_estrutural/aco/ligacoes_flexiveis.py` (só o pré-dimensionamento; nenhuma verificação mudou).

## Critério (o mesmo do W8-RIG)

- `_tentar` (busca de todos os `sugerir` deste arquivo): devolve a primeira candidata com taxa ≤ 0,90 em **todas** as
  verificações, inclusive disposições construtivas (`ALVO`, limiar "no limite" da tela). Sem nenhuma, a primeira que passa
  (o comportamento antigo); sem nenhuma que passe, a última verificável (como antes).
- `_com_folga(mínimo)` = mínimo / 0,90, para cima a 5 mm (cópia da função do W8-RIG, `ligacoes_rigidas.py`, que ainda não
  está neste branch: ao juntar, pode ficar uma só).

## O que estava errado e o que mudou

| # | Tipo | Antes | Causa | Mudança no `sugerir` | Depois |
|---|---|---|---|---|---|
| 05 | cantoneira dupla na alma | 0,91 (borda mínima, alma da viga, ponta: 32 ≤ 35) | L76 com gabarito 45 (Gerdau 2.4.5) e folga 10: ponta da viga a 35 mm, tabela 14 (¾", serra) = 32. Na L76 não há gabarito que dê ponta ≥ 32/0,9 = 35,6 **e** aba ≥ 26/0,9 = 28,9 (só 45,6 ≤ g ≤ 47,3) | depois da L76 (Gerdau) a grade tenta a **L102×102×6,35 com o gabarito Gerdau da L102 (65)**: ponta 55, aba 37 | **0,83** (pressão de contato na aba do apoio) |
| 08 | apoio em cantoneira | 0,91 (borda lateral da cantoneira: 32 ≤ 35) | assento de 150 com gabarito 80 entre colunas: (150 − 80)/2 = 35 | comprimento = g + 2 × borda com folga = 80 + 2 × 40 = **160** (≈ bf da W310); depois os 150 antigos (mesa estreita, ex.: W460×52, bf 152) | **0,87** (borda inferior da aba, 26 ≤ 30) |
| 06 | chapa simples na alma | 0,93 (borda **máxima**, alma da viga, ponta: 65 ≤ 12·tw = 69,6, 6.3.12) | a = 75 (Gerdau/AISC) com folga 10 numa alma de 5,8 mm | a = 10 + ⌊0,9·min(12tw, 150)/5⌋·5, no máximo 75 e no mínimo 10 + borda mínima com folga → **70** na W310×38,7 (75 de tw ≥ 6,3 mm em diante) | **0,86** (a mesma borda máxima, 60 ≤ 69,6) |
| 09 | viga–viga com recorte | 0,91 (mesma causa do 05) | usa o `sugerir` da cantoneira dupla | idem 05 | **0,83** |
| 07 | chapa de extremidade flexível | 0,80 | — | nada (a 1ª candidata já é ≤ 0,90) | 0,80 |
| 10 | solda direta | 0,75 | — | nada | 0,75 |

Taxas da captura `w7lig` (`70-w7lig-padroes.txt`). PNGs conferidos (05, 06, 08): a elevação/planta mostram L102 com
gabarito 65 (cotas 37 | 65), a chapa simples com 70 + 40 = 110 e o assento 40 | 80 | 40 = 160.

## Limites conhecidos

- Cantoneira dupla na **alma do pilar** W250×73: a L102 não cabe entre as mesas (209,8 > 200,6 mm) → fica a L76, que passa
  com 0,91 (primeira que passa). Opções (não feitas): cantoneira desigual (aba de 102 na viga, 76 no apoio) ou ⅝".
- Apoio em cantoneira em mesa estreita (bf ≤ 160): volta aos 150 mm e pode ficar acima de 0,90 pela largura na mesa.
- Folga de aperto da chave nos parafusos do assento (gabarito 80 contra alma + raio do pilar) não é verificada neste tipo
  (a chapa de extremidade tem; W7-LIG §1).

## Testes

- Motor: `testes/test_w8_flex.py`, 6 testes novos (taxa de fábrica ≤ 0,90 nos três + viga–viga; 07 e 10 não pioram; escolhas
  L102/g 65, assento 160, a = 70; alma do pilar fica na L76 que passa; alma grossa mantém a = 75; `_com_folga`).
- `test_ligacoes_u.py::test_x_max_sugerir_chapa_simples_compacta`: "sem x_max: nada muda" esperava `a` = 75; agora 70 na
  alma de 5,8 mm (justificativa escrita no teste). O que o teste protege (sem `x_max` não é a chapa compacta, a = 42) continua.
- Suíte: 1272 OK (9 pulados); antes 1266. Pórtico: 242 passam.
