# Catálogo de perfis (frente K, tarefa K1)

Pasta: `codigo/motor/prossen_estrutural/dados/perfis/`. Colunas: `fabricante_norma;familia;serie;nome;forma;massa;h;b;tw;tf;r;A;Ix;Iy;fonte`
(mm, kg/m, cm², cm⁴; Ix = eixo forte). Separador `;`, ponto decimal.

| Arquivo | Família | Linhas | Origem |
|---|---|---|---|
| w_hp | W/HP Gerdau | 88 | `perfis_gerdau.csv` (folder Gerdau 10/18) |
| ipe, hea, heb, upn | IPE, HEA, HEB, UPN | 18, 24, 24, 20 | ArcelorMittal Orange Book (node 6, 16, 265) |
| l_iguais, u_laminado, tubo_* | L, U, tubos | 50, 31, 278/397/427 | `codigo/portico/perfis-outros.csv` |
| l_desiguais | L desigual (EN 10056) | 32 | Orange Book node 225 |
| tubos_vallourec | Tubos Vallourec TC/TQ/TR | 616 | brochura Vallourec (extração parcial) |
| t_laminado | T Gerdau | 10 | lâmina Perfil T Gerdau |
| barra_chata | Barra chata | 73 | geometria nominal (A = b·t, massa = 7,85·A) |
| u_pff, ue_pff | U e Ue NBR 6355 | 57, 54 | dimensões de `perfis_pff.csv`; A, I por `secoes.py` |

Teste: `testes/test_catalogo.py` (massa x A·7850 ≤ 4 %, sem duplicados, contagens mínimas). Fontes e desvios: `dados/FONTES.md`.

Regenerar: `python codigo/motor/ferramentas/gerar_catalogo_perfis.py <brochura_vallourec.pdf>`. Pendências: UPN 30/40 sem conferência online; L iguais, U laminado, tubos metalon e barra chata sem fonte primária (ver `fonte` de cada linha); Vallourec com ~78 linhas descartadas por rótulo ambíguo.

## Motor (K2)

- `secoes.catalogo(nome)`: tenta primeiro os CSV antigos (`perfis_gerdau.csv`, `perfis_pff.csv`; nomes já salvos em modelos, como "W 200 x 26,6", seguem iguais) e depois estes CSV. A comparação ignora espaços, vírgula/ponto e caixa. Formas novas: `secao_T`, tubo retangular com raio externo (`re`, metalon = 1,5t), L desigual, chata (retangular), tubo redondo.
- Ix/Iy da tabela prevalecem sobre os calculados em `tubos_vallourec` (brochura usa raio < 2t), `u_laminado`/`upn` (mesas inclinadas: a geometria de mesa constante erra Iy em até +19 %) e `l_desiguais` (raios de canto não modelados); W, Wx, Zx, rx etc. são reescalados. Demais famílias: Ix/Iy calculados dentro de 3 % da tabela (exceções em `test_catalogo_secoes.py`).
- `GET /api/catalogo?formato=arvore` (MCP/py: `projeto.catalogo_arvore()`): `[{nivel1, familias:[{nome, series:[{nome, perfis:[nome]}]}]}]` com os níveis "Gerdau (ABNT)", "Europeu (EN/DIN)", "Tubos Vallourec", "Metalon", "Formados a frio NBR 6355". Sem o parâmetro a rota segue devolvendo a lista plana (usada pelo WPF atual), agora incluindo os perfis novos; `familia` aceita qualquer nome de família.
- T e L não têm Zx/Zy: a verificação de flexão desses perfis não está implementada.

## Seletor em árvore (K3)

A janela "Catálogo de perfis" ganhou árvore nível 1 > família > série (de `GET /api/catalogo?formato=arvore`) à esquerda; escolher um nó filtra a tabela pelos perfis abaixo dele, e a busca continua valendo. Sem árvore (motor antigo) cai no filtro por família. Captura `46-catalogo-arvore`.
