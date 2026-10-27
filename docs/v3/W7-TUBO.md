# W7 — Tubo–tubo soldado (boca de peixe) refeito

Data: 27/10/2026 (branch `wt7-tubo`). Pedido do usuário sobre o padrão de fábrica (Fz 100 kN, My 60 kN·m, Ø114,3×6 a 45° no
Ø168,3×6,4 → "Não passa, 770 % na solda"): "essa aqui é surreal, cadê o 3d cadê tudo".

Código: `codigo/motor/prossen_estrutural/aco/ligacoes_tubos.py`; gancho `exemplo` em `projeto._exemplo_lig`; projetista
(`JanelaLigacaoProjeto.cs`): campo `secao`, lista `perfis` no seletor de perfil, chips "ramal"/"receptor" e as capturas
`51-projetista-tubo-tubo-soldado` e `-perfil-tubo-soldado`. Testes: `codigo/motor/testes/test_ligacoes_tubos.py`
(`TestNoTubular`). O desenho dos tubos (`LigacaoDesenho.cs`) é de outro agente: o motor só manda a lista `tubos`
(contrato em `LIGACOES-CONTRATO.md`).

## O que estava errado

| Problema | Causa | Agora |
|---|---|---|
| 770 % na solda | Exemplo genérico de `projeto._exemplo_lig` (V = 100 kN, M = 60 kN·m, feito para viga W310) | O registro fixa o próprio `exemplo`: N = 100 kN de tração e My = 5 kN·m. Os 60 kN·m passam do momento plástico do ramal (MRd = 20,8 kN·m no TC 114,3x5,6) |
| Só solda e β | O nó tubular (NBR 16239) não estava implementado | Validade, plastificação da face, punção, momentos, interação, ramal, garganta da solda |
| Seções "CHAPA" / "PARAFUSOS" | A tela deduz a seção pelo nome do campo ("Diâmetro" → Parafusos; resto → Chapa) | Tubos no passo 1 (seletor de perfil); `secao: "Tubos"` para θ e os esforços do receptor |
| Diâmetros e paredes digitados | Entradas `d_ramal`, `t_ramal`… | `perfil_viga` / `perfil_suporte` do catálogo, só tubos circulares (`perfis`, 569 nomes) |
| Nenhum tubo na geometria | Só `soldas` (a curva da sela) | Lista `tubos`: receptor e ramal com o corte em boca de peixe |
| Chip "Chapa 68 %" | O chip vem das palavras do nome; "geometria" caía em "Chapa" | Verificações nomeadas "ramal: …" (chip Viga) e "receptor: …" (chip Apoio) |

`d_ramal`, `t_ramal`, `D_receptor`, `t_receptor` (mm) continuam aceitos e prevalecem sobre o catálogo: o Realista
(`Render3DMotor.cs`) manda as medidas da seção do modelo, que pode não estar no catálogo.

Extras opcionais do contrato de campos, que qualquer tipo pode usar:
- `entradas[campo].secao`: seção do passo 2 do projetista (ex.: `"Tubos"`). Sem ela, a tela deduz a seção pelo nome.
- `entradas[campo].perfis`: nomes do catálogo aceitos por um campo `perfil_*`. O seletor mostra só esses.
- `TIPOS[tipo].exemplo = {dados, esforcos}`: padrão de fábrica do tipo. Fica no lugar das amostras genéricas de
  `projeto._exemplo_lig` (W310/W250, V 100 kN, M 60 kN·m).

## Verificações e fontes

Fontes: NBR 16239:2013 (extração textual, `C:\dev\wt\pesquisa-w7\ligacoes.md` §3, conferir no exemplar licenciado),
EN 1993-1-8:2005 cap. 7 (tabelas 7.1, 7.2, 7.5; 7.4.2), CIDECT Design Guide 1. A NBR 16239 traz os coeficientes da EN
× 1,10 com γa1 = 1,10: a resistência é numericamente a da EN com γM5 = 1,0 (teste `test_formulas_a_mao`).

Notação: d0, t0 = receptor; d1, t1 = ramal; β = d1/d0; γ = d0/(2t0); θ entre os eixos; fy0 do receptor.

| Verificação | Critério | Item |
|---|---|---|
| Validade | 0,2 ≤ β ≤ 1,0; 10 ≤ d0/t0 ≤ 50; 10 ≤ d1/t1 ≤ 50; θ ≥ 30° | NBR 16239 6.2.1 (θ: EN 1993-1-8 tab. 7.1) |
| Função de tensão | np = σ0p,Sd/fy0 (compressão negativa; fibra mais comprimida: N0/A0 − \|M0\|/W0); np < 0: kp = 1 + 0,3np − 0,3np²; senão 1 | NBR 16239 tabela 2 |
| Plastificação da face (modo A) | N1,Rd = γ^0,2·kp·fy0·t0²·(3,08 + 15,62β²)/(γa1·sen θ) | NBR 16239 tabela 2 |
| Punção (modo D), só se d1 ≤ d0 − 2t0 | N1,Rd = 0,66·fy0·t0·π·d1·(1 + sen θ)/(2·γa1·sen²θ) | NBR 16239 tabela 2 |
| Momentos, modo A | Mip = 5,34·fy0·t0²·d1·√γ·β·kp/(γa1·sen θ); Mop = 2,97·fy0·t0²·d1·kp/(γa1·sen θ·(1 − 0,81β)) | NBR 16239 tabela 4 (√γ da EN 7.5; a extração perdeu o termo) |
| Momentos, modo D | Mip = 0,66·fy0·t0·d1²·(1 + 3 sen θ)/(4·γa1·sen²θ); Mop = 0,66·fy0·t0·d1²·(3 + sen θ)/(4·γa1·sen²θ) | EN 7.5 com o 0,66 da NBR (a conferir) |
| Interação no nó | N/N1,Rd + (Mip/Mip,Rd)² + Mop/Mop,Rd ≤ 1 (Rd = menor entre os modos A e D) | EN 1993-1-8 7.4.2(5) |
| Tensão no receptor (só com N0/M0) | \|N0\|/A0 + \|M0\|/W0 ≤ fy0/γa1 | NBR 8800 (seção) |
| Ramal, seção | NRd = A·fy/γa1 (χ = 1); MRd pelo anexo G (D/t ≤ 0,07E/fy: Z·fy; até 0,31E/fy: (0,021E/(D/t) + fy)·W); interação 5.5.1.2 | NBR 8800 5.2/5.3, anexo G, 5.5.1.2 |
| Ramal, cortante | VRd = 0,5·(0,6fy)·A/γa1 | NBR 8800 5.4.3.5 |
| Garganta da solda | a = 0,707·b ≥ k·t1; k = 1,0 (fy ≤ 280 MPa), 1,1 (≤ 350), 1,5 (≤ 450) | NBR 16239 6.1.5.7 |
| Solda por metro e metal base | q = \|N\|/Lw + M/W_sela ⊕ V/Lw + T/(Lw·r) ≤ FwRd (como antes) | NBR 8800 6.2.5 / tabela 8 |
| Perna mínima | tabela 10 pela parede mais fina | NBR 8800 6.2.6.2.1 |

Decisões:
- **Sem perna máxima (6.2.6.2.2)** no tubo–tubo. A regra é para filete ao longo da borda de uma chapa (junta
  sobreposta). Aqui a ponta do ramal encosta no receptor (junta em T), e a NBR 16239 6.1.5.7 pede garganta ≥ k·t, isto é,
  perna maior que t. Com a regra antiga, o padrão (t1 = 5,6 mm, perna ≥ 8,7 mm) seria impossível.
- **Momentos**: M (My) é o momento no plano do nó (plano xz do referencial local, que contém os dois eixos); Mz é fora do
  plano. No grupo, os esforços vêm nos eixos locais da barra (`esforcos_contrato`): numa treliça plana vertical, com z
  local no plano vertical, My é o momento no plano. Em outra orientação, conferir (o resultado avisa).
- **Receptor**: N0 e M0 são entradas do projetista (padrão 0, kp = 1, com aviso). Informe os valores do lado mais
  comprimido do nó, sem a parcela do ramal. O total fica a favor da segurança.
- **Flambagem do ramal** comprimido: verificada na barra, não na ligação (aviso).
- **Sugerir**: mantém os tubos, porque eles vêm do modelo, e escolhe a menor perna comercial (3…16 mm) que passa nas
  verificações de solda.
- **Otimizar**: genérico (`ligacoes.otimizar`), só mexe na perna. Se o nó reprova (plastificação ou punção), devolve
  "sem solução" com a verificação limitante. A correção é trocar o receptor (parede mais grossa ou diâmetro maior).

## Padrão de fábrica (exemplo do projetista)

TC 114,3x5,6 a 45° no TC 168,3x6,4, A-572 GR.50, E70, perna 10 mm, N = 100 kN de tração, My = 5 kN·m:

| Verificação | Taxa |
|---|---|
| solda: garganta ≥ 1,1·5,6 = 6,16 mm (7,07 mm) — governa | 0,87 |
| solda por metro (709,6 ≤ 1524 kN/m) | 0,47 |
| receptor: interação N + M (100/312,8 + (5/27,01)²) | 0,35 |
| ramal: seção, N + M (0,167/2 + 5/20,77) | 0,32 |
| receptor: plastificação (100 ≤ 312,8 kN) / punção (100 ≤ 812,1 kN) | 0,32 / 0,12 |

O padrão passa com 87 %, governado pela garganta mínima. Essa é uma disposição construtiva: a perna é a menor perna
comercial acima do mínimo. As resistências ficam em 32 % a 47 %. O padrão antigo (Fz 100 kN, My 60 kN·m) reprova na
seção do ramal (teste `test_padrao_de_fabrica`). A conta à mão está na docstring de `TestNoTubular`.

## Perfil–tubo (`perfil_tubo_soldado`)

- Exemplo próprio: V = 60 kN e My = 15 kN·m. Antes eram 100 kN e 60 kN·m, com a parede do tubo sem verificação.
- Seções "Perfil" e "Tubo".
- `tubos` com o receptor circular, e o ramal quando ele também é tubo circular.
- O cálculo não mudou. Continua governado pela perna mínima = máxima (5 mm): W310 com mesa 9,7 mm e alma 5,8 mm num tubo
  de 9,5 mm.
- A parede do tubo continua sem verificação (aviso). As fórmulas de chapa/I em CHS (EN 1993-1-8 tab. 7.3) ficam como
  próximo passo.

## Limites

- Só nó T/Y com um ramal: X, K e N com afastamento não estão implementados.
- Cisalhamento do receptor: é verificação de barra (modelo), não da ligação.
