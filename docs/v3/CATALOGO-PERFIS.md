# Catálogo de perfis (frente K, tarefa K1)

Pasta: `codigo/motor/prossen_estrutural/dados/perfis/`. Colunas: `fabricante_norma;familia;serie;nome;forma;massa;h;b;tw;tf;r;A;Ix;Iy;fonte`
(mm, kg/m, cm², cm⁴; Ix = eixo forte). Separador `;`, ponto decimal.

| Arquivo | Família | Linhas | Origem |
|---|---|---|---|
| w_hp | W/HP Gerdau | 88 | `perfis_gerdau.csv` (folder Gerdau 10/18) |
| ipe, hea, heb, upn | IPE, HEA, HEB, UPN | 18, 24, 24, 20 | ArcelorMittal Orange Book (node 6, 16, 265) |
| l_iguais, u_laminado, tubo_* | L, U, tubos | 50, 31, 278/397/427 | `codigo/portico/perfis-outros.csv` |
| l_desiguais | L desigual (EN 10056) | 32 | Orange Book node 225 |
| t_laminado | T Gerdau | 10 | lâmina Perfil T Gerdau |
| barra_chata | Barra chata | 73 | geometria nominal (A = b·t, massa = 7,85·A) |
| u_pff, ue_pff | U e Ue NBR 6355 | 57, 54 | dimensões de `perfis_pff.csv`; A, I por `secoes.py` |

Teste: `testes/test_catalogo.py` (massa x A·7850 ≤ 4 %, sem duplicados, contagens mínimas). Fontes e desvios: `dados/FONTES.md`.
