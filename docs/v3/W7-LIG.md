# W7-LIG — padrões de fábrica das ligações: chapa de extremidade flexível, cantoneira simples em treliça e revisão

Data: 27/10/2026. Branch `wt7-lig`. Pedido (imagens 8 e 9): "essa ligação do jeito que vem simplesmente não faz sentido" (chapa de
extremidade flexível) e "que porra de ligação é essa" (cantoneira simples em treliça). Pesquisa de apoio:
`C:\dev\wt\pesquisa-w7\ligacoes.md` (§1, §2, §5).

Capturas: `ESTRUCTTO_CAPTURAS=w7lig` (`CapturasW7Lig.cs`): os 22 tipos do motor no padrão de fábrica (`70-w7lig-NN-<tipo>`,
mais `70-w7lig-padroes.txt` com taxa, dados e esforços de cada um), as quatro vistas grandes dos dois tipos e a troca da
cantoneira no seletor (`70-w7lig-cantoneira-simples-trelica-L4`, com asserção).

## 1. Chapa de extremidade flexível (`ligacoes_flexiveis.py`)

### O que estava errado

Padrão de fábrica (W310×38,7 na mesa do W250×73, V = 100 kN): chapa **6,3 × 200 × 155**, 2 fileiras × 2 colunas de ¾",
g = 120, s = 75. É a tabela Gerdau LCHE 20-B (4 parafusos), mas:

- Lp = 155 mm = **0,50·h**. A chapa curta não trava a viga à torção no apoio; o SCI P358 pede Lp ≥ 0,6·h. A própria Gerdau
  tem a tabela de 6 parafusos (230 mm = 0,74·h) para o W310.
- O `sugerir` pegava a 1ª combinação que passava: sempre a menor chapa (n = 2) e a mais fina (6,3).
- Faltava conferir a folga de montagem. O gabarito só era testado contra "parafuso fora da solda" (> db/2).
- Desenho: a elevação não cotava o topo da chapa ao topo da viga (campo `dist_topo_chapa`). Na lateral, o rótulo do pilar
  caía no meio do desenho, porque a base da `Caixa` é o plano médio.

### Critério adotado

| Item | Regra | Fonte |
|---|---|---|
| Fileiras | menor n com Lp = 2·40 + 75·(n − 1) ≥ 0,6·h que cabe na alma (tf + r + 5 do topo, tf + r da base); se nenhum n chega a 0,6·h, o maior que cabe | SCI P358 §4 (h_p ≥ 0,6·h_b); Gerdau LCHE (tabelas de 4 e 6 parafusos no W310) |
| Espessura | 8 mm, depois 9,5 mm | o usuário: 8–10 mm usuais; AISC parte 10: ¼"–⅜"; SCI P358: 10/12 mm (M20); dúctil se tp ≤ 0,65·db (A325/A36, critério de ductilidade do SCI [conferir]); > 10 mm já tem aviso ([GD] 4.1) |
| Gabarito / largura | 120 / 200 (Gerdau), limitados pela face do apoio (já existia) | Gerdau LCHE |
| Folga de aperto (verificação nova) | (g − tw)/2 − b_solda ≥ 1,25·db e, na mesa do pilar, g/2 − tw_pilar/2 − r_pilar ≥ 1,25·db | regra de oficina: meia diagonal da porca pesada (≈ 0,87·db + 2 mm) + parede do soquete. **Não é da NBR**; a tabela de folgas de aperto do AISC (Manual parte 7) não foi conferida (`ponytail:` no código) |
| Aviso | Lp < 0,6·h → "travamento à torção da viga no apoio duvidoso (SCI P358)" | SCI P358 |
| Solda | filete dos dois lados da alma, só na alma (já era assim); perna 5 mm ≥ tabela 10 | NBR 8800 6.2.6.2.1 |

Resultado no W310×38,7: **8 × 200 × 230, 3 fileiras × 2 colunas de ¾"** (6 parafusos), taxa 0,80 (borda mínima). Por altura:
W250 → 2 fileiras (155 ≥ 150), W310/W360 → 3, W410/W460 → 4, W530/W610 → 5.

### Desenho (2D, `LigacaoDesenho.cs`, fora do 3D isométrico)

- Elevação: cota do topo da chapa (ou da cantoneira) ao topo da viga, fora da cadeia de furos. Vale para todos os tipos
  flexíveis em que a peça fica abaixo do topo da viga.
- Rótulo do apoio pelos cantos do sólido (base ± N·T/2). Na lateral, ele vai para o topo do pilar.

## 2. Cantoneira simples em treliça (`ligacoes_contraventamento.py`)

### O que estava errado

- Campos de medida soltos e zerados: Outra aba 0, Espessura da mesa do U 0, Área 0, Excentricidade 0, Comprimento 0, Seção
  "L" (lista de uma opção). Com 0, o motor usava cantos vivos e a/2 sem mostrar.
- Não havia seletor de perfil.
- Geometria errada: a cantoneira tinha o talão em z = 0, na linha dos parafusos, com `u = +z`. A aba saliente (w = e × u = −y)
  atravessava o gusset, e a aba ligada ficava dentro dele (y = tg/2 − t/2). Os parafusos ficavam no talão, e o gusset era um
  retângulo centrado na linha de furos ("ch 6,3 × 76"). Lateral e 3D com peças soltas.
- O padrão nascia "no limite" (97 %) por 1 mm de borda: e1 = 30 com Tab. 14 = 29.

### Critério adotado

- Campo **`perfil_cantoneira`** (padrão `L 3" x 1/4"`), com o mesmo `SeletorPerfil` da viga e do apoio. Ele lista só as
  famílias "Cantoneira L" e "L desigual": 82 perfis.
- **`aba_ligada`** (maior/menor) só muda algo em abas desiguais.
- Medidas do catálogo (`cantoneira_catalogo`): abas e t. A área é a tabelada (`A_catalogo`): L76,2×6,35 → 9,29 cm².
  - ec = x̄ da face da aba ligada ao centroide, pela geometria do catálogo: t/2 − x0 → 21,4 mm.
  - Ct = 1 − 21,4/100 = 0,786 com 3 furos a 50 mm (NBR 8800 5.2.5).
  - A E.1.4 usa a seção do catálogo.
- Gabarito usual na aba (`gabarito_usual`): a tabela de gabaritos usuais do AISC (Manual tab. 1-7A, [conferir]) arredondada
  para cima a 5 mm. Bate com a Gerdau 2.4.5: 45 mm na L76 e 65 mm na L102.
  - O `sugerir` grava o valor.
  - `g_aba = 0` ainda quer dizer "usual" no verificar.
- Saíram do esquema `secao`, `dim_a`, `dim_b`, `t_perfil`, `tf_perfil`, `A_perfil` e `xg_perfil`.
  - Dados antigos com `dim_a` e sem `perfil_cantoneira` continuam valendo (legado, `_CANT_MEDIDAS`), com os testes antigos.
- Comprimento da barra: padrão 1500 mm. Na compressão, a barra é verificada por E.1.4 (0 = não verifica).
- Gusset:
  - Espessura ≥ espessura da aba (6,3 serve na aba de ¼"); o `sugerir` não desce abaixo dela.
  - Bordas e1 e e_gusset = Tab. 14 + 3 mm de tolerância de corte, para cima a 5 mm (35 mm com Ø16).
- Mínimo de 2 parafusos: o `sugerir` já começa com 2. Com N = 100 kN dá 3 × Ø16 (A325, corte 49 kN por parafuso).
- Trocar a cantoneira no seletor refaz o pré-dimensionamento: gabarito, bordas, parafusos e gusset vêm do perfil novo
  (`JanelaLigacaoProjeto`, só no campo `perfil_cantoneira`).

### Geometria (contrato)

- Gusset no plano xz, y ∈ [−tg/2, tg/2].
- A aba ligada encosta na face +y. O talão fica em z = g_aba (linha de furos = linha de trabalho, z = 0, NBR 8800 6.1.8.2),
  com `u = −z` (a aba desce do talão) e `w = +y` (a aba saliente sai do gusset).
- `abas`, `espessura`, `talao_z` e `ec` vão na geometria.
- O gusset vai do nó até e_gusset além do último furo e cobre a aba ± 10 mm.
- A cantoneira passa max(150, 2a) além do gusset, porque é a barra.
- Soldada: filetes no talão (l1) e na ponta da aba (l2), na face +y.

### Desenho 2D

- Cadeia de cotas: com empate de furos, a chapa (gusset) vem antes da cantoneira. Entram também as bordas da outra peça furada
  que caem dentro da faixa (exceto as bordas na direção da espessura dela).
  - Elevação: recuo | e1 35 | 2 × 50 | e_gusset 35 = 182,6.
  - Vertical e lateral: 10 | b − g = 31,2 | g = 45 | 10.
- O rótulo do perfil da cantoneira vai na ponta da barra.

## 3. Revisão dos outros padrões (capturas `70-w7lig-NN`)

| # | Tipo | Problema no padrão de fábrica | Ação |
|---|---|---|---|
| 11, 12 | chapa de topo ajustada / estendida | taxa 1,03 (NÃO PASSA) por "solda da alma: perna máxima": filete 6 > tw 5,8 do W310×38,7 | **corrigido** no `sugerir` (`ligacoes_rigidas._sugerir_chapa`): b_alma = min(6, máx. da alma) ≥ mínimo → 5 mm; passa (1,00 no pf mínimo de aperto, disposição) |
| 9 | viga–viga com recorte | taxa 1,08 (NÃO PASSA): a amostra põe a W310 (secundária) numa W250×73 (suporte **mais baixo** que a viga) → recorte duplo que não passa a flexão | não corrigido (a amostra fica em `projeto._AMOSTRA_LIG`, fora dos meus arquivos): usar suporte mais alto, ex. `("viga_viga_recorte", "perfil_suporte"): "W460x52"` |
| 17 | gusset de contraventamento | mesmos campos zerados da cantoneira (Outra aba, mesa do U, Área, ec = 0), sem seletor de perfil; a diagonal não é desenhada, só o gusset e os parafusos; "no limite" 97 % por borda 30 vs 29 | não corrigido: reaproveitar `cantoneira_catalogo` (L/2L) e um `perfil_diagonal` com forma (U, tubo); desenhar a diagonal |
| 18 | nó de treliça com gusset | idem (campos zerados por barra; 2L sem a 2ª cantoneira desenhada; barras não desenhadas); 97 % por borda | não corrigido |
| 19 | tirante de barra redonda | barra não desenhada (só chapa e porca); comprimento 0 | não corrigido |
| 14 | painel de alma do pilar | sem peças de ligação no desenho (só perfis); campo booleano mostra "true/false" em vez de Sim/Não | não corrigido (UI) |
| 15, 16 | emenda de viga / pilar | 97 % por borda mínima da tala da alma (30 vs 29); lateral com cotas da mesa sobrepostas (92,5 / 165) | não corrigido |
| 10 | solda direta da alma | taxa 1,00 = dimensão mínima do filete (disposição, ok) | outro agente renomeia |
| 21 | tubo–tubo soldado | taxa 7,70: M = 60 kN·m num ramal Ø114 (Mpl ≈ 18–25 kN·m) | outro agente (`ligacoes_tubos.py`) |
| 22 | perfil–tubo soldado | o desenho só tem a solda (sem perfil nem tubo) | `ligacoes_tubos.py`, outro agente |
| 1–8, 13, 20 | parafuso, solda, chapa simples (antigos), chapa de ligação tracionada, cantoneira dupla, chapa simples, chapa de extremidade, apoio em cantoneira, flange plate, cantoneira simples | coerentes (taxas 0,24–0,96) | — |

## Testes

- Motor: `testes/test_w7_lig.py`, 9 testes novos.
  - Fileiras pela altura (W250/W310/W410/W530) e padrão do projetista no W310 (3 fileiras, 8 mm, Lp = 230, 6 parafusos, 2 filetes).
  - Folga de aperto: g = 120 passa (52,1 ≥ 23,8); g = 60 reprova.
  - Aviso de chapa < 0,6·h.
  - Campos sem medidas soltas e propriedades do catálogo: A = 9,29 cm², ec = 21,4 mm, Ct = 0,786.
  - Aba menor de L desigual, geometria das duas abas sobre o gusset e legado com dim_a.
- Nenhum teste antigo foi alterado.

## Fontes

- SCI P358, *Joints in Steel Construction: Simple Joints to Eurocode 3*, §4 (chapa de extremidade parcial: h_p ≥ 0,6·h_b,
  t = 10/12 mm, solda só na alma).
- Gerdau, *Ligações para Estruturas de Aço*, 7ª ed.: LCHE (§4), disposições 2.4 e gabaritos 2.4.5.
- AISC Steel Construction Manual, parte 10 (shear end plate: ¼"–⅜") e tab. 1-7A (gabaritos usuais) [conferir na 15ª/16ª ed.].
- NBR 8800:2008, itens 5.2.5 (Ct), 6.1.8.2 (excentricidade em cantoneira), 6.2.6.2.1 / tabela 10, 6.3.9–6.3.12, tabela 14,
  E.1.4.
