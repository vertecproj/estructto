# Limites de projeto das ligações

`aco/ligacoes_limites.py`: `limites(tipo, membros, dados=None) -> {param: (min, max, passos)}`. Base do otimizador limitado (C2): ele só busca dentro destas faixas. `membros` = lista de `{perfil: barra, perfil_apoio: apoio}` (nome do catálogo, ou dict h, b, tw, tf em m). `dados` opcional (diâmetro atual `d`, `borda` da placa). Unidades do parâmetro (mm nos tipos do catálogo; m na placa de base). Com vários membros vale o apoio mais estreito.

| Regra | Parâmetros | Faixa | Fonte |
|---|---|---|---|
| Chapa comercial | `t_chapa, t_gusset, tp, t_mesa, t_tala_*, t_alma, t_assento, t_enrijecedor` (placa base `esp`) | 4,75; 6,3; 8; 9,5; 12,5; 16; 19; 22,4; 25; 31,5; 37,5; 44,5; 50 mm | Usiminas/Gerdau |
| Parafuso | `d, d_mm, d_alma_mm, d_mesa_mm` | lista `_D_MM` (12,7 a 31,75) | ligacoes.py |
| Chumbador | `diametro_mm` (placa base) | 12, 16, 20, 24, 30, 36 | métrico |
| Espaçamento | `s, s_mesa, s_alma, sh_alma` | 3d a 300 mm | NBR 8800 6.3.9 (nunca < 2,7d), 6.3.12 (24t ≤ 300) |
| Borda | `e_v, e_h, e1, e1_mesa`; `borda` da placa base (m) | Tab. 14 (cortada a serra/tesoura) a 150 mm | NBR 8800 6.3.11 / Tab. 14, 6.3.12 (12t ≤ 150); valores de `ligacoes._BORDA` |
| Chapa de topo / flange plate / extremidade | `bp`, `b_chapa`, `largura_chapa` | 2·borda mín. a bf do apoio | geometria |
| Talas de mesa | `b_tala_mesa` | ≤ bf do perfil emendado | geometria |
| Gusset | `Lh, Lv` | 0 (automático) a h − 2tf − 2·12 mm do apoio | geometria (folga 12 mm) |
| Placa base | `H, B` | ≥ h (b) do pilar + 2·borda | spec |

Decisões: máximos de espaçamento e borda usam os tetos absolutos (300 e 150 mm) porque a espessura da parte mais fina não é conhecida aqui; a regra do gusset aplica a folga livre entre mesas do apoio a `Lh` e `Lv`, de forma conservadora. Parâmetros sem regra não aparecem no resultado; tipo desconhecido devolve `{}`.
