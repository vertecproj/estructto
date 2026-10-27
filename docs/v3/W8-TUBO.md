# W8 — Perfil–tubo soldado com a parede do tubo verificada; perna acima da mínima nos padrões

Data: 27/10/2026 (branch `wt8-tubo`). Pedido: "corrija os outros padrões ruins". Dois padrões de fábrica saíam
"PASSA NO LIMITE 100 %" só por uma disposição construtiva, sem nenhuma resistência perto do limite.

Código: `aco/ligacoes_tubos.py` (perfil–tubo), `aco/ligacoes_flexiveis.py` (`_sug_sd`, só ele), `aco/pontas.py`
(`contato_tubo(…, secao)`), `LigacaoDesenho.cs` (lê `membros[].pecas`; o tubo receptor é desenhado antes dos perfis).
Testes: `testes/test_w8_tubo.py` (novo); ajustes justificados em `test_ligacoes_tubos.py` e `test_onda_final.py`.
Capturas: `ESTRUCTTO_CAPTURAS=w8tubo` (`CapturasW8Tubo.cs`, `80-w8tubo-*.png` + `80-w8tubo.txt`).

## O que estava errado

| Padrão | Antes | Causa | Agora |
|---|---|---|---|
| 22 perfil–tubo soldado (W310 em Ø323,8 × 9,5) | 100 %: "solda: dimensão mínima" 5 ≤ 5 mm; máxima 5 ≤ 5,8 (86 %); solda 20 %; parede do tubo sem verificação (aviso) | perna padrão = mínima da tabela 10; perna máxima de borda (6.2.6.2.2) aplicada numa junta em T; nenhuma verificação do tubo | 83 % (perna 6 mm acima da mínima 5); parede do tubo verificada: interação 20 %, punção 18 %, β 0,51 ≥ 0,4 (78 %); solda 36 % |
| 22 (desenho) | só o cordão e o tubo; o perfil não aparecia (os `membros` não tinham medidas) | `membros` só com o nome; contrato `tubos` só circular | I com mesas e alma, ponta na sela (mesas recortadas no cilindro), em todas as vistas e no 3D; cordão seguindo a sela (antes: corda reta entre os cantos da mesa, dentro do tubo — sumia no 3D) |
| 10 solda direta (W310 na mesa do W250) | 100 %: "dimensão mínima" 3 ≤ 3 mm; resistências 38 % | `_sug_sd` testava 3, 4, 5… e parava na primeira que passa: sempre a mínima | 75 %: perna 4 mm (mínima 3, máxima 5,8); solda 28 % |

## Decisão: perna padrão acima da mínima (não mudar a taxa das disposições)

Uma verificação de dimensão mínima atendida com igualdade está, de fato, no limite da norma, mas não diz nada sobre a
resistência. Alternativas:
1. Mudar a taxa/apresentação das verificações de detalhamento (ex.: taxa 0 quando atende). Mexe em todos os tipos, no
   status "no limite" da tela, no `_governa` e no otimizador (que usa a taxa e as dicas `sentido`). Fora do escopo.
2. **Padrão com a menor perna comercial acima da mínima, quando couber** (escolhida). Mesmo critério já usado: W7-LIG
   (bordas + 3 mm sobre a tabela 14 "para o padrão não nascer no limite") e tubo–tubo (menor perna acima do mínimo, 87 %).

Regra (`_sug_sd` e `_sugerir_pt`): pernas comerciais (3, 4, 5, 6, 8, 10… mm) maiores que a mínima da tabela 10, em ordem;
a primeira que passa em tudo (solda direta) ou nas verificações de solda (perfil–tubo). Se nenhuma maior passa (ex.: UPN 120,
alma 7 mm: mínima 5, máxima 7 − 1,5 = 5,5 → não há perna comercial entre as duas), volta ao comportamento antigo (a
mínima, ou a última tentada). Se o usuário digitar a perna mínima, a tela continua mostrando "no limite" (é verdade).

## Perfil–tubo: verificações da parede do tubo redondo

Fonte: EN 1993-1-8:2005 cap. 7, tabelas 7.1, 7.3 (chapa) e 7.4 (I, H, tubo retangular), 7.4.2 (interação), conferidas na
página da norma (PDF phd.eng.br, p. 110–111). γM5 = 1,0 (valor recomendado; é a forma da NBR 16239, que traz os
coeficientes × 1,10 com γa1 = 1,10, como no tubo–tubo). kp: NBR 16239 tabela 2 = EN 7.2.2 (função `kp`). A NBR 16239
(base EN) não está no repositório para estas tabelas: conferir no exemplar licenciado.

Notação: d0, t0 = tubo; h1 = medida do perfil ao longo do eixo do tubo, b1 = atravessada; β = b1/d0; η = h1/d0;
f = kp·fy0·t0²/γM5.

| Caso (giro) | N1,Rd | Mip,Rd | Mop,Rd | Punção (σmax·t1 ≤) |
|---|---|---|---|---|
| I/H/U, giro 0/180 (alma no plano do eixo do tubo, mesas atravessadas; h1 = h, b1 = b) — tab. 7.4 | f·(4 + 20β²)(1 + 0,25η) | h1·N1,Rd/(1 + 0,25η) | 0,5·b1·N1,Rd | 2·t0·fv (t0·fv se η > 2 com compressão ou Mop), t1 = tf |
| Tubo retangular, giro 0 ou 90 — tab. 7.4 | igual ao I | h1·N1,Rd | 0,5·b1·N1,Rd | t0·fv, t1 = parede |
| Chapa longitudinal (retangular, giro 0) — tab. 7.3 | 5f·(1 + 0,25η) | h1·N1,Rd | 0 | 2·t0·fv, t1 = b |
| Chapa transversal (retangular, giro 90) — tab. 7.3 | f·(4 + 20β²) | 0 | 0,5·b1·N1,Rd | 2·t0·fv |
| Tubo circular | `no_tubular` (NBR 16239 tabelas 2 e 4, igual ao tubo–tubo) | | | modo D |

- fv = 0,66·fy0/γa1 (NBR 16239, o mesmo do modo D do tubo–tubo; a EN usa fy0/√3/γM5, 4 % menor).
- σmax = N/A1 + M/Wy1 + Mz/Wz1 da seção do perfil feita de retângulos (raios ignorados, a favor).
- Plastificação da face: N·sen θ ≤ N1,Rd. As tabelas são de nó T (θ = 90°); com θ < 90° usa a componente normal ao tubo e
  η sem o alongamento da sela (a favor), com aviso.
- Interação (EN 7.4.2, eq. 7.3): N·sen θ/N1,Rd + (Mip/Mip,Rd)² + Mop/Mop,Rd ≤ 1. No referencial do contrato, M (My,
  eixo forte) é Mip quando h está ao longo do tubo (giro 0); com giro 90, Mz é Mip. Resistência 0 com momento ≠ 0 reprova
  ("receptor: Mop numa chapa longitudinal").
- Validade: 10 ≤ d0/t0 ≤ 50 e θ ≥ 30° (tab. 7.1); β ≥ 0,4 (menos na chapa longitudinal) e η ≤ 4 (menos na chapa
  transversal) (tab. 7.3/7.4).
- Fora das tabelas (aviso, sem verificação da parede): I/U com giro diferente de 0/180 (± 10°), chapa e tubo retangular
  fora de 0/90/180. Tubo retangular **receptor**: tabelas 7.13/7.14 não implementadas (aviso, como antes).
- N e M do tubo no nó (`N_receptor`, `M_receptor`, seção "Tubo"): kp e tensão no tubo, como no tubo–tubo.
- Cortante: a EN 7.4 não tem critério de V para a parede; V passa pela solda (verificada) e o cisalhamento do tubo é
  verificação da barra (aviso).

Solda (sem mudança no cálculo por metro): tirada a perna máxima 6.2.6.2.2 (junta em T, mesma decisão do tubo–tubo);
perfil tubular ganha a garganta da NBR 16239 6.1.5.7; perfil aberto leva aviso da EN 7.3.1(6) (solda com a resistência da
chapa, "normalmente").

### Padrão de fábrica (conta à mão na docstring de `test_w8_tubo.py`)

W310 (310 × 165 × 5,8 × 9,7) no tubo Ø323,8 × 9,5, A-572 GR.50, E70, θ = 90°, V = 60 kN e M = 40 kN·m (viga de 6 m
com ligação rígida, ~q·L²/12 com q ≈ 13 kN/m; antes V 60 e M 15):

| Verificação | Valor | Taxa |
|---|---|---|
| solda: dimensão mínima (5 ≤ 6 mm) — governa | | 0,83 |
| receptor: β = b1/d0 = 0,51 ≥ 0,4 | | 0,78 |
| solda perfil–tubo (330,6 ≤ 914,4 kN/m) | | 0,36 |
| receptor: interação (40/88,74)² | N1,Rd = 354,8 kN; Mip,Rd = 88,7 kN·m; Mop,Rd = 29,3 kN·m | 0,20 |
| receptor: punção (715,5 ≤ 3933 kN/m) | σmax = 73,8 MPa | 0,18 |

## Desenho

- `geometria.membros[viga].pecas` (novo, opcional, contrato em `LIGACOES-CONTRATO.md`): chapas do perfil no plano médio
  (`base` [[x,y,z] mm], `normal`, `espessura`), já com a ponta encostada no tubo (cada borda levada ao longo de x até a
  superfície, a mesma conta da solda). O desenho (`GeoLig.De`) as transforma em prismas de perfil (camada 0, grupo
  viga/suporte, falha e clique pelo `campo`). Polígono côncavo: nas vistas de frente vai como está; de lado, o casco.
- Tubo retangular receptor: o contrato `tubos` só tem tubo circular, então as 4 paredes vão em
  `membros[pilar].pecas`.
- Ordem nas vistas: o tubo receptor é desenhado antes dos perfis (a viga que encosta nele fica na frente na lateral); o
  ramal tubular continua junto das chapas (tubo–tubo igual).
- Realista (3D do modelo): sem mudança; continua pelas `pontas` (recorte boca_peixe do sólido do perfil).
- Fica de fora: perfil tubo circular num receptor retangular (o ramal não aparece; o corte plano não está no contrato
  `tubos`).

## Limites

- Só nó T/Y com um perfil. I com a alma atravessada (giro 90) e receptor retangular: parede não verificada (aviso).
- Interação sem sinal dos momentos (valores absolutos), a favor.
