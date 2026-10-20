# Ligações em aço segundo Pfeil & Pfeil, *Estruturas de Aço: Dimensionamento Prático* (NBR 8800:2008)

**Fonte:** Walter Pfeil e Michèle Pfeil, *Estruturas de aço: dimensionamento prático de acordo com a NBR 8800:2008*, 8.ª ed., LTC.
O PDF fica em `referencias/livros/estruturas-de-aco-dimensionamento-pratico.pdf` (382 páginas, fora do git).
**Páginas:** sempre as **do livro**. Página do PDF = página do livro + 21.
**Direito autoral:** este arquivo resume o livro e reproduz apenas os números dos exemplos, que servem como casos de teste. Não traz trechos longos do texto.
**Extração:** feita em 20/10/2026. O texto saiu do PyMuPDF. As equações ilegíveis foram conferidas em imagens renderizadas das páginas.

## 0. Convenções do livro (valem para tudo abaixo)

- **Unidades dos cálculos:** kN e cm, com tensões em kN/cm². Exemplos: fy = 25 kN/cm² (MR250 / A36); fu = 40 kN/cm².
- **Aços dos conectores** (Tabela 3.1, p.69). Valores fu (fy) em MPa:
  - A307: 415.
  - A325: 825 para d ≤ 25,4 mm; 725 para d > 25,4 mm.
  - A490: 1035.
  - Barra A36: 400 (250). Barra A588: 485 (345).
- **Eletrodos:** E60 com fw = 415 MPa; E70 com fw = 485 MPa (p.92).
- **Coeficientes:**
  - γa1 = 1,10 (escoamento) e γa2 = 1,35 (ruptura).
  - Solda: γw1 = 1,25 (1,05 excepcional) e γw2 = 1,35 (1,15 excepcional).
  - Concreto da base: γc = 1,4. O livro chama de γn = 1,4 o fator extra do apoio.
- **Ações (exemplos):** permanente 1,4, ou 1,3 para pequena variabilidade; variável 1,5. Alguns exemplos usam 1,4 para tudo.
- **Área de cálculo do furo:** o furo-padrão tem d + 1,5 mm. Na área líquida desconta-se **d + 3,5 mm**: 1,5 mm de folga mais 2 mm de dano do puncionamento (p.51, p.67). No **cisalhamento** de chapa desconta-se só o d nominal (p.73, p.257).
- **Itens da NBR:** os números do livro são os da equação, por exemplo "Eq. 3.2a". O item da NBR 8800:2008 ao lado foi atribuído por mim. Itens marcados **[confirmar]** precisam ser conferidos no texto da norma antes de ir para o código.

---

## 1. Peças tracionadas na região da ligação (base de todas as ligações)

Cap. 2, p.49–61. É a verificação da barra ligada. Aparece em todas as ligações axiais.

**Verificações:**
1. Escoamento da seção bruta: Ag·fy/γa1. Eq. 2.1b, NBR 5.2.2a.
2. Ruptura da seção líquida efetiva: Ae·fu/γa2. Eq. 2.1a, NBR 5.2.2b.
   - Ae = Ct·An (Eq. 2.5, NBR 5.2.3).
   - Área líquida em zigue-zague (NBR 5.2.4): An = [b − Σ(d + 3,5 mm) + Σ s²/4g]·t (Eq. 2.4). Pesquisar todos os percursos.
3. Coeficiente Ct (NBR 5.2.5):
   - Perfil aberto, parafusado ou soldado: Ct = 1 − ec/ℓ ≥ 0,60 (Eq. 2.6).
     - ec é a excentricidade do plano da ligação ao centroide da parte que transfere a força.
     - ℓ é o comprimento da ligação: entre o 1.º e o último parafuso, ou o comprimento do cordão.
     - Perfil I ligado pelas mesas: dividir em dois T. Ligado pela alma: dividir em dois U.
     - Exige no mínimo 2 parafusos por linha.
     - **O livro não cita o teto Ct ≤ 0,90 da NBR.** O motor deve aplicá-lo.
   - Só soldas transversais: Ct = Ac/Ag (Eq. 2.7).
   - Chapa só com soldas longitudinais (Eq. 2.8): Ct = 1,00 se ℓw ≥ 2b; 0,87 se 1,5b ≤ ℓw < 2b; 0,75 se b ≤ ℓw < 1,5b.
4. Cisalhamento de bloco (NBR 6.5.6), Eq. 2.9:
   - Rd = (0,6·fu·Anv + Cts·fu·Ant)/γa2 ≤ (0,6·fy·Agv + Cts·fu·Ant)/γa2.
   - Cts = 1,0 com tração uniforme; 0,5 com tração não uniforme.
5. Esbeltez ≤ 300 (Tabela 2.1, NBR 5.2.8). Não vale para barras redondas pré-tracionadas.
6. Barra rosqueada (Eq. 2.2): Rd = min(0,75·Ag·fu/γa2; Ag·fy/γa1).

**Exemplos numéricos:**

| Ex. (pág.) | Dados | Resultado |
|---|---|---|
| 2.3.3 (p.55–56) | Chapa 22×300, MR250; 4 furos φ22 na seção; Nk = 300 kN variável → Nd = 450 kN | Ag = 66,6 cm². An = (30 − 4·2,55)·2,22 = 44,04 cm². Escoamento: 1513 kN. Ruptura: 1304 kN. |
| 2.3.4 (p.56–57) | Chapas 280×20, φ20 com furos puncionados (desconto 23,5 mm); s = 75, g = 50; três percursos | An: 46,6 (reto), 48,45 e 55,0 cm². Escoamento 1273 kN (governa); ruptura 1381 kN. |
| 2.3.5 (p.57) | Tirante rosqueado A36, Nk = 150 kN, γ = 1,4 | Ag necessária: 9,45 cm² pela ruptura (contra 9,24 pelo escoamento). Adotado φ 34,9 mm (Ag = 9,58 cm²). |
| 2.3.6 (p.57–58) | L 178×102×12,7; φ22 (desconto 25,4 mm) | Percurso 1-1-1 crítico: largura líquida 216,5 mm (o outro dá 222,6 mm). An = 27,4 cm². ℓmáx = 300·2,21 = 663 cm. |
| 2.3.7 (p.58–59) | U 381×50,4, MR250, φ22, ligado pela alma | Escoamento: 1459 kN. An = 53,8 cm². Ct = 1 − 2,0/7,5 = 0,73; Ae = 39,4 cm²; ruptura 1169 kN. Bloco: Agv = 30,6, Anv = 22,8, Ant = 18,2 cm²; resultado min(944; **879**) = **879 kN**. |
| 2.3.8 (p.60) | Mesmo perfil, ligação soldada | Ct = 1 − 20/100 = 0,80 → 1450 kN. O livro cita "1444 kN" para o escoamento; no 2.3.7 deu 1459. **Inconsistência do livro.** |
| 2.3.9 (p.60–61) | U 381 com 2 furos a mais (força dividida 2/10 e 8/10) | An: 53,8 (1-1), 59,0 (2-2) e 57,6 cm² (2-1-1-2). A seção 1-1 recebe só 8/10 de N: 1454 kN. Seção 2-1-1-2: **1246 kN** (governa). |

---

## 2. Ligação parafusada por corte (tipo apoio / contato)

**Nomes:** ligação por contato, tipo apoio, *bearing*. Inclui emenda axial com talas, ligação de cantoneira em gusset e ligação de perfil U em chapa.
**Peças ligadas:** chapa–chapa, tirante–gusset, diagonal–gusset, emenda de chapas.
**Componentes:**
- Parafusos A307 ou A325/A490, ou barras rosqueadas.
- Talas ou chapas de nó (gusset).
- Furos.

**Dados de entrada:**
- Diâmetro d.
- Classe do parafuso (fub).
- Rosca no plano de corte: sim/não. **Padrão: sim**, o caso desfavorável. O livro manda assumir "sim" quando não houver certeza (p.70).
- Número de planos de corte.
- Espessura t e fu de cada chapa.
- Furação: s (entre furos) e e (até a borda).
- Tipo de furo: padrão por omissão.
- Comprimento de pega.

**Verificações, em ordem:**
1. **Corte do parafuso, por plano** (NBR 6.3.3.2):
   - Rosca no plano: Rnv = (0,7·Ag)(0,6·fu) ≈ **0,40·Ag·fu**. Eq. 3.1a, p.70.
   - Rosca fora do plano (alta resistência): **0,50·Ag·fu**. Eq. 3.1b.
   - Rd = Rnv/γa2.
   - Grupo: n × resistência de 1 parafuso, porque se admite distribuição plástica uniforme (p.75).
2. **Pressão de contato e rasgamento, por parafuso e por chapa** (NBR 6.3.3.3), p.70–71:
   - Rd = min(2,4·d·t·fu; 1,2·a·t·fu)/γa2.
   - a = distância livre do furo à borda, ou entre furos (s − d), medida na direção da força.
   - Se a deformação em serviço não importar: 3,0·d·t·fu e 1,5·a·t·fu.
   - Grupo = **soma** das resistências individuais. Furos de borda e internos são diferentes.
3. **Tração na chapa ou no perfil:** escoamento e ruptura com Ae (item 1).
4. **Cisalhamento de bloco** na chapa ou no perfil (item 1, Eq. 2.9).
5. **Correções:**
   - Pega > 5d: reduzir a resistência 1 % a cada 1,5 mm de excesso (p.70) [confirmar o item].
   - Ligação longa, com L > 1270 mm entre parafusos extremos: multiplicar a força por 1,25 (p.75) [confirmar o item].

**Regras de detalhamento** (p.66–68; NBR 6.3.5 a 6.3.12, Tabela 14) [confirmar a numeração]:
- **Furos:**
  - Puncionamento direto só se t ≤ d + 3 mm.
  - Furo alargado: d + 5 mm (d ≤ 24), d + 6 (d = 27), d + 8 (d ≥ 30).
  - Furo pouco alongado: d + 6 / d + 8 / d + 9,5 mm.
  - Furo muito alongado: 2,5d.
  - Furos alargados ou alongados na direção da força: só em ligação por atrito.
- **Borda mínima**, para borda laminada ou cortada a maçarico: d + 6 mm (d ≤ 19), d + 7 (19 < d < 26), d + 9 (26 ≤ d < 30), d + 10 (30 ≤ d ≤ 36), 1,25d (d > 36). Na Fig. 3.8 a borda cortada com serra ou tesoura tem tabela própria.
- **Espaçamento mínimo:** padrão de 3d (os exemplos usam 3d).
- **Espaçamento máximo:** 24t ≤ 300 mm em peça pintada ou sem corrosão; 14t ≤ 180 mm em aço patinável sem pintura. t é a chapa mais fina.
- **Borda máxima:** 12t ≤ 150 mm.
- **Gabaritos de cantoneira:** padrão americano, Fig. 3.9.

**Exemplos numéricos:**

| Ex. (pág.) | Dados | Valores intermediários | Final |
|---|---|---|---|
| **3.5.1** (p.78–79) Emenda com talas | Chapas 204×12,7, A36; talas 2×9,5; 6 parafusos A307 φ22 de cada lado (3 de borda e 3 internos), corte duplo. Ng = 200 e Nq = 100 → Nd = 1,4·200 + 1,5·100 = 430 kN | Corte: 0,40·3,88·41,5·2·6/1,35 = 572 kN. Borda: a = 51 − 11,7 = 39,3 mm < 2d, dá 177,5 kN/parafuso. Interno: a = 46,5 mm > 2d, dá 2,4dtfu → 198,7 kN. Grupo de contato: 3·177,5 + 3·198,7 = 1128,4 kN. An = [20,4 − 3(2,22 + 0,35)]·1,27 = 16,12 cm² → 477,6 kN. Escoamento: 588,8 kN. Bloco: Agv = 30,7, Anv = 20,9, Ant = 6,4 cm² → min(561; 530) = 530 kN | **Rd = 478 kN** (ruptura da seção líquida) > 430 kN: OK |
| **3.5.2** (p.79–81) Tirante 2L em gusset | 2L 63×6,3, A36; gusset 6,3 mm; 5 parafusos A307 φ12,7 em corte duplo; s = 40, borda 25; Ag = 2·7,68 cm² | Corte: 0,4·1,27·41,5·2·5/1,35 = 156 kN. Borda: a = 17,9 mm → 40,3 kN. Internos: 56,9 kN cada → total 267,9 kN. Ae = 2(7,68 − 0,63·1,62)(1 − 1,8/16) ≈ 11,82 cm² (Ct com ec = 1,8 e ℓ = 16 cm) → 350 kN. Escoamento: 15,3·25/1,1 = 349 kN. Bloco: Agv = 23,3, Anv = 14,2, Ant = 2,90 cm² → 338 kN | **Rd = 156 kN** (corte dos parafusos) |
| **3.5.3** (p.81–82) Perfil U em chapa, A325 tipo apoio | Nd = 640 kN, A36; φ16 (Ag = 1,98 cm²), rosca no plano; 14 parafusos; s = 3d = 48 mm | Corte: 0,4·1,98·82,5/1,35 = 48,4 kN; n = 640/48,4 = 13,2 → 14. U 306×30,7: An = 39,1 − 4(1,6 + 0,35)·0,71 = 33,56 cm²; Ct = 1 − 1,77/14,4 → 872 kN. Escoamento: 889 kN. Contato (a = 2d, as duas fórmulas coincidem): 2,4·1,6·0,71·40·14/1,35 = 1131 kN. Bloco: Ant = 6,1, Agv = 27,3, Anv = 17,6 cm² → **493 kN < 640** | Bloco reprova. O livro resolve com s = 4d. Gusset: t ≥ 640/(2,4·1,6·40·14/1,35) = 0,40 cm; adotar t > 7 mm por causa do bloco |
| **3.5.5** (p.83–84) Chapa com 3 parafusos e momento | 3 parafusos A307 φ19 em linha vertical; braço horizontal 150 mm; parafusos extremos a 160 mm um do outro; chapa de 25 mm | Fv = Q/3; Fh = (1,5/1,6)·Q; F = √(Fv² + Fh²) = 0,99Q. Corte: 2,85·0,4·41,5/1,35 = 35,0 kN | **γQ = 35,4 kN**. Contato: 2,4·1,9·2,5·40/1,35 = 338 kN ≫ 35 |

---

## 3. Ligação parafusada excêntrica no plano (consolo parafusado)

**Nomes:** ligação excêntrica por corte, consolo parafusado, chapa de alma com excentricidade. Método elástico do disco rígido.
**Peças ligadas:** consolo–pilar; alma de viga–chapa ou cantoneira (emenda de alma, ligação flexível).
**Entradas:**
- Coordenadas (x, y) de cada parafuso.
- Força F (com componentes) e excentricidade e ao centroide do grupo.
- Dados do parafuso e da chapa (item 2).

**Verificações** (p.75–76, Eqs. 3.9–3.12):
1. Componente direta: F/n por parafuso.
2. Momento M = F·e. Força no parafuso: R = M·r/Σr², com Rx = M·y/Σr² e Ry = M·x/Σr².
3. Soma vetorial com a componente direta, no parafuso mais solicitado.
4. Verificar corte e contato/rasgamento desse parafuso (item 2).
5. O livro cita o método do centro instantâneo de rotação como alternativa menos conservadora, sem desenvolvê-lo (p.105).

**Exemplos numéricos:**

| Ex. (pág.) | Dados | Valores intermediários | Final |
|---|---|---|---|
| **3.5.6** (p.84–85) | Consolo com 6 parafusos A325 em 2 colunas (afastadas 100 mm) e 3 linhas (passo 60 mm). Qk = 30 kN permanente, γ = 1,3. Excentricidade ao centroide 200 mm. Chapa de 12 mm | Q/n = 5,0 kN. Σr² = 6·5,0² + 4·6,0² = 294 cm². M = 600 kN·cm. Parafuso 1: Fx = 6/294·600 = 12,2 kN; Fy = 5/294·600 = 10,2 kN. Fd = 1,3·√((5 + 10,2)² + 12,2²) = 25,3 kN | Ag ≥ 25,3·1,35/(0,4·82,5) = 1,04 cm² → **φ12,7 A325**. Contato: 2,4·1,27·1,2·40/1,35 = 108 kN > 25,3 |
| **9.10.1 d** (p.254–255) Emenda de alma | Ver item 8 (emenda de viga) | Σx² + Σy² = 8·3,5² + 4(22,5² + 7,5²) = 2348 cm². Fyd = 9,85 kN, Fxd = 63,4 kN, cortante/8 = 21,0 kN | Fd = 70,5 kN < 101,6 kN |

---

## 4. Ligação parafusada com tração e com tração + corte

**Nomes:** ligação a tração (T-stub ou flange), consolo com cantoneiras de topo, ligação com momento por parafusos tracionados.
**Peças ligadas:** consolo e chapa de topo com pilar; tirante pendurado.

**Verificações:**
1. **Tração no parafuso** (NBR 6.3.3.1): Rnt = 0,75·Ag·fu; Rd = Rnt/γa2 (Eq. 3.4, p.71). Vale para d ≥ 12 mm. Na barra rosqueada, limitar também a Ag·fy/γa1.
2. **Efeito alavanca:** o livro só cita, sem desenvolver (p.76). Remete à NBR, que usa critério simplificado [confirmar 6.3.5?], e a Queiroz (1993). **Lacuna: o motor precisa de outra fonte.**
3. **Interação** (NBR 6.3.3.4): (Vd/Rdv)² + (Td/Rdt)² ≤ 1,0 (Eq. 3.5, p.72).
4. **Distribuição do momento:**
   - Parafuso comum, sem protensão garantida (p.77, Eqs. 3.13–3.15):
     - Seção T invertida: os parafusos tracionados viram uma faixa de largura t' = 2Ai/a; a zona comprimida é a chapa.
     - Linha neutra pela igualdade dos momentos estáticos.
     - ft = M·y/I.
   - Alta resistência protendida (p.78, Eq. 3.16):
     - A seção inteira fica em contato; a linha neutra está a meia altura.
     - ft = 6M/(b·h²) deve ser < fco, a pré-compressão = n·P/(b·h).

**Exemplos numéricos:**

| Ex. (pág.) | Dados | Valores intermediários | Final |
|---|---|---|---|
| **3.5.7** (p.86–87) Consolo com 2 cantoneiras e 12 parafusos A307 φ16 | V = 100 kN em serviço; M = 1500 kN·cm; γ = 1,4. Faixa equivalente de 0,79 cm (= 2·1,98/5). Altura 30 cm; contato com largura de 20 cm | LN: 20y²/2 = 0,79(30 − y)²/2 → **y = 5 cm**. I = 0,79·25³/3 + 20·5³/3 = 4950 cm⁴. ft = 1500/4950·(27,5 − 5) = 6,82 kN/cm². τ = 100/(12·1,98) = 4,21 kN/cm². Resistências: corte 12,3 e tração 23,0 kN/cm² | Interação: (1,4·4,21/12,3)² + (1,4·6,82/23,0)² = **0,40 < 1** |

---

## 5. Ligação por atrito (alta resistência protendida)

**Nomes:** tipo atrito, *slip-critical*.
**Entradas:**
- Protensão mínima P, tirada da Tabela A5.2 do livro. Exemplo: A325 φ16 → P = 85 kN.
- Coeficiente de atrito μ: 0,35 para superfície laminada limpa sem pintura (classe A) ou galvanizada escovada (classe C).
- Ch = 1,0 para furo padrão.
- Número de planos de deslizamento.

**Verificações:**
1. Todas as de ELU da ligação tipo apoio (itens 2 a 4).
2. **Deslizamento** (NBR 6.3.4):
   - Furo padrão, ou alongado perpendicular à força: estado-limite de **serviço**, combinação rara.
   - Rv = 0,80·μ·Ch·P por parafuso e por plano (Eq. 3.6, p.72).
   - Furo alargado, ou alongado na direção da força: estado-limite **último**. Ver NBR 6.3.4.3; o livro não desenvolve.
3. Tração simultânea T: multiplicar Rv por (1 − T/(0,80·P)) (p.72–73).

**Detalhamento:** em edifícios com mais de 40 m de altura, as emendas de pilar devem usar atrito ou solda (p.242).

**Exemplos numéricos:**

| Ex. (pág.) | Dados | Valores intermediários | Final |
|---|---|---|---|
| **3.5.4** (p.82–83) | Mesma ligação do 3.5.3, agora por atrito. Serviço: 640/1,4 = 457 kN | Rv = 14·0,80·0,35·1·85 = 333 kN < 457 | Precisa de n = 457/(0,80·0,35·85) = **20 parafusos** |
| **3.5.8** (p.87–88) | Consolo do 3.5.7 com A325 φ16 por atrito. Contato de 20×30 cm | ft = 6·1500/(20·30²) = 0,50 kN/cm² < fco = 12·85/(20·30) = 1,7 kN/cm². Tração no parafuso de topo: T = (10·5)·0,5 = 25 kN | Rv = 0,80·0,35·85·(1 − 25/(0,8·85)) = 15,0 kN > V = 100/12 = 8,3 kN: OK |

---

## 6. Ligação soldada axial e em T (filete e entalhe)

**Nomes:** emenda axial soldada, ligação de cantoneira em gusset por filetes, ligação em T (chapa normal).
**Componentes:** filetes longitudinais e/ou transversais; solda de entalhe (penetração total ou parcial).
**Entradas:**
- Perna b; comprimento L.
- Eletrodo (fw).
- Espessura das chapas; fy e fu do metal-base.
- Processo: o arco submerso tem garganta maior.

**Resistências** (NBR 6.2.5, Tabela 8) [confirmar a numeração]:
- **Garganta do filete:** t = 0,7b (Eq. 4.1).
  - Arco submerso: te = b se b ≤ 10 mm; te = t + 3 mm se b > 10 mm (Eq. 4.2).
  - Pernas desiguais: t = b1·b2/√(b1² + b2²).
- **Filete:** Rd = Aw·0,60·fw/γw2 (Eq. 4.9a). Por unidade de comprimento: t·0,60·fw/γw2 (Eq. 4.9b).
  - Em E60: 0,6·41,5/1,35 = **18,4·t kN/cm** (t em cm), ou **184 MPa** na garganta.
  - Força em qualquer direção no plano normal ao eixo do cordão é tratada como cisalhamento.
  - Tração ou compressão paralela ao eixo é ignorada.
  - Opção da NBR: Rd em função do ângulo θ. Em ligação concêntrica com filetes longitudinais e transversais, usar o maior entre Rwl + Rwt e 0,85·Rwl + 1,5·Rwt (p.102).
- **Metal-base junto ao filete** (cisalhamento): escoamento Ag·0,6·fy/γa1 e ruptura An·0,6·fu/γa2 (Eqs. 3.8a/b).
- **Entalhe, penetração total, tensão normal ao eixo:** AMB·fy/γa1, com a peça mais fina (Eq. 4.6a). Cisalhamento: AMB·0,6·fy/γa1 (Eq. 4.7a).
- **Entalhe, penetração parcial, normal ao eixo:** o menor entre AMB·fy/γa1 e 0,60·Aw·fw/γw1 (Eqs. 4.6b/c). Cisalhamento: Aw·0,60·fw/γw2 (Eq. 4.7b).
  - Garganta efetiva: y − 3 mm para chanfro em bisel de 45°–60°; y para ângulo > 60°, chanfro em J ou U, ou proteção gasosa/fluxo no núcleo na posição plana ou horizontal.
  - Proibida em peças fletidas.
- **Solda longa** (L > 100b): ℓef = β·L, com β = 1,2 − 0,002·L/b e 0,6 ≤ β ≤ 1,0 (Eq. 4.3).
  - Os filetes longitudinais de barra com força axial usam ℓef = β·L.
  - Nos outros casos, ℓ = comprimento total, incluindo os retornos.
- **Cantoneira com filetes balanceados:** escolher ℓ1 e ℓ2 de modo que a resultante passe pelo centroide do perfil (ΣM = 0, ΣF = 0). Assim não surge flexão (p.110).

**Detalhamento** (Tabelas 4.1 e 4.2; Fig. 4.11; Eq. 4.4):
- **Perna mínima do filete**, pela chapa mais fina: ≤ 6,3 mm → 3 mm; 6,3–12,5 → 5; 12,5–19 → 6; > 19 → 8 mm. Não precisa passar da espessura da chapa mais fina.
- **Perna máxima:** b = t se t < 6,3 mm; b = t − 1,5 mm se t ≥ 6,3 mm (na borda).
- **Comprimento mínimo:** L ≥ 4b e ≥ 40 mm.
- **Só filetes longitudinais em extremidade tracionada:** L ≥ a, a distância entre os filetes.
- **Transpasse com filetes transversais:** ≥ 5t e ≥ 25 mm (Fig. 4.14c).
- **Garganta mínima do entalhe parcial**, pela chapa mais fina: ≤ 6,3 → 3; 6,3–12,5 → 5; 12,5–19 → 6; 19–37,5 → 8; 37,5–57 → 10; 57–152 → 13; > 152 → 16 mm.
- **Chapas de espessuras diferentes:** se o degrau passar de 10 mm, fazer chanfro de transição 1:2,5.
- **Fim de filete em borda tracionada:** recuar o início do cordão (Fig. 4.12).
- **Solda com parafusos** (p.107): em obra nova não somam, exceto parafusos por contato com filetes longitudinais; aí os parafusos entram com no máximo 50 %. Em reforço de obra existente, os parafusos ou rebites existentes levam a carga permanente já aplicada.

**Exemplos numéricos:**

| Ex. (pág.) | Dados | Valores intermediários | Final |
|---|---|---|---|
| **4.6.1** (p.107–108) T com chapa 12 mm | Nk = 40 kN variável → Sd = 60 kN; A36, E60; 2 filetes b = 5 mm, L = 100 mm | Filete: 2·10·0,5·0,7·0,6·41,5/1,35 = **129 kN**. Entalhe total: 10·1,2·25/1,1 = **272 kN** | OK, com folga |
| **4.6.2** (p.108–109) Emenda com 4 filetes longitudinais | Chapa central 12×127 e duas 10×75; Nk = 180 kN → Sd = 1,4·180 = 252 kN; A36, E60; b = 5 mm | Aw = 4·0,7·0,5·ℓ = 1,4ℓ; Rd = 25,8ℓ → ℓ = 9,76 cm → **100 mm** (≥ distância entre filetes). Metal-base 12 mm: 360 kN; 10 mm: 273 kN | ℓ = 100 mm. **O livro omite γa1 na chapa de 12 mm**: o valor certo é 327 kN, ainda OK |
| **4.6.3** (p.109–110) L 127×24,1 em gusset, filetes balanceados | Nk = 150 kN, γ = 1,3; MR250; E70; b = 5 mm; centroide a 3,63 cm da borda; aba de 12,7 cm | F1 = 150·3,63/12,7 = 42,8 kN; F2 = 107,2 kN. Rd por cm = 0,7·0,5·0,6·48,5/1,35 = 7,54 kN/cm. Metal-base: 16,4ℓ | **ℓ1 = 80 mm, ℓ2 = 190 mm**. Com b = 8 mm: ℓ1 = 50 mm, ℓ2 = 120 mm |
| **4.6.4** (p.110–111) Idem, com filete transversal na ponta | F3d = 0,7·0,5·12,7·0,6·48,5/1,35 = 95,8 kN; ΣF e ΣM no filete inferior | ℓ1 + ℓ2 = 13,16 cm; ℓ1 = 1,04 cm | Adotados **ℓ1 = 20 mm, ℓ2 = 130 mm** |
| **4.6.8** (p.115–116) I 76×8,5 em gusset (mesas com filete, alma com entalhe) | Nk = 120 kN, γ = 1,4; filetes b = 4 mm × 120 mm (4 filetes); Af = 5,92·0,66; Ag = 10,8 cm²; tw = 0,432 cm | Aw = 13,4 cm². Força nas mesas: 1,4·(2·5,92·0,66/10,8)·120 = 122 kN. Resistência dos filetes: 248 kN. Gusset ao cisalhamento: 2·0,5·12·0,6·25/1,1 = 164 kN | Alma: 46 kN < (7,6 − 2·0,66)·0,432·25/1,1 = **62 kN** |

---

## 7. Ligação soldada excêntrica e solda com momento (consolo soldado, chapa de topo, solda mesa–alma)

**Nomes:** consolo soldado, solda de chapa de topo (*end plate*), solda de composição de perfil soldado.

**Verificações:**
1. **Excentricidade no plano** (p.104–105, Eqs. 4.10–4.12):
   - Área das gargantas rebatida no plano.
   - τF = F/Σtℓ.
   - τM = F·e·r/Ip, decomposta em τx = Fe·y/Ip e τy = Fe·x/Ip.
   - Ip = Ix + Iy. Com t = 1, usar a Tabela A11 do livro e multiplicar por t.
   - Somar vetorialmente no ponto mais distante do centroide G. Comparar com a Eq. 4.9b.
2. **Momento fora do plano**, chapa de topo ou consolo:
   - Cortante só nos cordões da alma: τ = V/(2·t·h0) (Eq. 4.16).
   - Momento: σ = M·y/I, com I das gargantas rebatidas.
   - Combinação: τd,máx = √(σ² + τ²) ≤ τRd (Eq. 4.17).
3. **Solda mesa–alma de perfil soldado:**
   - τ = V·S/(I·b) (Eq. 4.13). b = soma das gargantas, ou tw se a solda for de entalhe.
   - As tensões normais paralelas ao cordão são ignoradas (NBR).
   - Com carga concentrada, somar τ = P/(2t(a' + 2tf)) (Eq. 4.15).

**Exemplos numéricos:**

| Ex. (pág.) | Dados | Valores intermediários | Final |
|---|---|---|---|
| **4.6.5** (p.111–113) Consolo soldado em C (2 filetes de 20 cm + 1 vertical de 30 cm) | Pk = 60 kN variável, γ = 1,4; MR250; E60; chapa de 19 mm; carga a 40 cm do filete vertical; b = 6 mm | xg = 5,71 cm. V/ℓ = 60/70 = 0,857 kN/cm. M = 60(40 − 5,71) = 2057,4 kN·cm. Ip/t = 14.297 cm³ (Ip = 6004 cm⁴). pMx = 2,16 e pMy = 2,06 kN/cm. Resultante p = √((0,86 + 2,06)² + 2,16²) = 3,62 kN/cm | pd = 5,07 kN/cm < **7,75** kN/cm (solda) e < 25,9 (metal-base): OK |
| **4.6.6** (p.113–114) Solda mesa–alma VS 500×61 | V = 200 kN, γ = 1,4; filetes b = 5 mm (2 filetes); S = 25·0,95·24,525 = 582,5 cm³; I = 34.416 cm⁴ | τd = 1,4·200·582,5/(2·0,7·0,5·34.416) = **6,77 kN/cm² = 67,7 MPa** | < 184 MPa: OK |
| **4.6.7** (p.114–115) VS 850×120 em chapa de topo, só filetes | M = 10.000 kN·cm; V = 250 kN; E60; b = 5 mm; γ = 1,4 | Ix = 117.744 cm⁴. Ponto A: τ = 3,61 kN/cm². Ponto B: τV = 250/(2·0,5·0,7·82,5) = 4,33 kN/cm² e τM = 3,50 kN/cm² → 55,7 MPa | τd = 1,4·55,7 = **78,0 MPa** < 184: OK |
| **9.10.6 c** (p.262–263) Consolo enrijecido, solda | Rd = 130 kN; e = 70 mm; cordões 12,7 + 2·20 cm | τv = 2,47/t; ȳ = 7,59 cm; I = 2122·t; τh = 3,25/t; resultante 4,08/t | t ≥ 0,22 cm → b ≥ 3,2 mm → **b = 5 mm** (mínimo para 12,5 mm) |

---

## 8. Emenda de viga (soldada ou com talas parafusadas)

**Nomes:** emenda de viga, emenda de campo com talas (cobrejuntas), *splice*.
**Componentes:** talas de mesa; 2 talas de alma; parafusos; ou solda de entalhe de penetração total.
**Entradas e padrões:**
- M e V na seção da emenda. **Padrão do livro: 50 % da capacidade à flexão e ao cisalhamento** quando a emenda está fora do ponto crítico, como nos quartos de vão (p.244).
- Tala de mesa com a mesma espessura da mesa.
- Tala de alma com cerca de 0,8·tw cada.
- Distância a do centroide dos parafusos da alma até a seção da emenda.

**Verificações:**
1. **Divisão do momento** (Eqs. 9.1–9.3):
   - Mf = M·Af/(Af + Aw/6); Mw = M·(Aw/6)/(Af + Aw/6).
   - Força na mesa: F = Mf/h, com h entre os planos de corte.
2. **Mesa:**
   - Parafusos em corte simples, força F/n.
   - Tala tracionada: An·fu/γa2.
   - Contato e rasgamento.
3. **Alma:**
   - Parafusos em corte duplo.
   - Momento na alma: M0 = Mw + V·a. Distribuição excêntrica pelo item 3; somar V/n.
4. **Chapas da alma:**
   - Rasgamento.
   - Cisalhamento líquido da alma: An·0,6·fu/γa2.
   - Flexão: Z·fy/γa1.
5. **Emenda soldada:**
   - Penetração total, com sequência: mesas, depois alma, depois filete mesa–alma.
   - Solda simétrica; evitar sobrecabeça.
   - Prefere-se penetração total sem talas, por causa da fadiga (p.243–244).

**Exemplo numérico — 9.10.1** (p.253–255):

**Dados:**
- VS 600×95, MR250. Alma 8×575 (Aw = 46,0 cm²); mesa 12,5×300 (Af = 37,5 cm²); Zx = 2889 cm³.
- A325 por contato, rosca no plano de corte.
- Vk = 120 kN → Vd = 168 kN.
- Md = 0,5·2889·25/1,1 = **328 kN·m**.

**Divisão do momento:**
- Mwd = 328·7,7/(37,5 + 7,7) = **55,9 kN·m**.
- Mfd = 272,1 kN·m.
- Força na mesa: 272,1/0,60 = **453 kN**.

**Emenda da mesa:**
- Tala de 12,5 mm; 6 parafusos em corte simples.
- Ag ≥ 453·1,35/(6·0,7·0,6·82,5) = 2,94 cm² → **φ22**.
- An = 1,25[30 − 2(2,22 + 0,35)] = 31,07 cm² → 920,6 kN.
- Rasgamento: 1,2·(4,0 − 1,3)·1,25·40·6/1,35 = 720 kN.

**Emenda da alma:**
- φ16 em corte duplo: 2(0,7·1,98)·0,6·82,5/1,35 = **101,6 kN**.
- Espessura mínima da tala: t ≥ 0,45 cm → adotado 6,3 mm.
- 8 parafusos por tala.
- M0d = 55,2 + 168·0,065 = **66,1 kN·m**. O livro usa 55,2 aqui e 55,9 acima: **inconsistência**.
- Σr² = 2348 cm². Forças: 9,85 kN (y), 63,4 kN (x), 21,0 kN (cortante).
- **Fd = 70,5 kN < 101,6**.
- Rasgamento da tala: 1,2·(3,0 − 1,0)·0,63·2·40/1,35 = 89,6 kN.
- Alma: An = (57,5 − 4·1,9)·0,8 = 40,0 cm² → 711 kN.
- Flexão das talas: 2·0,63·45²/4·25/1,1 = 186 kN·m > 55,2.

---

## 9. Emenda de pilar

**Nomes:** emenda de coluna; por contato (superfície usinada) ou por talas.
**Tipos** (Fig. 9.4, p.241–243):
- (a) Talas parafusadas, para perfis iguais.
- (b) Solda de topo, para pequena variação de espessura.
- (c) Chapa de extremidade, para variação moderada.
- (d) Contato usinado mais talas.

**Critérios:**
- Superfície usinada: a compressão passa por contato. As talas e os parafusos ficam com o cortante, a tração de momento ou de reversão, e o posicionamento.
- Superfície não usinada: as talas ou a solda transmitem tudo.
- Pôr a emenda perto de um ponto de contenção lateral.
- Prédio com mais de 40 m: atrito ou solda.

**Exemplo numérico:** nenhum. **Lacuna:** falta caso de teste; buscar em outra fonte (CBCA, Bellei).

---

## 10. Ligações flexíveis viga–pilar e viga–viga

**Nomes:** ligação flexível, rotulada, simples, de cisalhamento.
**Tipos:**
1. **Dupla cantoneira de alma**, também chamada *double clip angle* (Fig. 9.7a).
   - Em geral: soldada na alma da viga e parafusada na mesa do pilar.
   - A rótula fica na face do pilar.
   - **A solda** leva R e o torsor R·a, onde a é a distância da rótula ao centroide das linhas de solda.
   - **Os parafusos** levam R com a excentricidade e2.
2. **Cantoneira de apoio** (assento, *seated*), com cantoneira superior fina para estabilidade (Fig. 9.7b).
   - Espessura: Md,res = Z·fy/γa1 = (ℓ·t²/4)·fy/γa1 (Eq. 9.4).
   - O comprimento de apoio a' deve evitar escoamento local e enrugamento da alma (itens 6.3.6 do livro e 5.7 da NBR).
   - Verificar também o cisalhamento na seção A-A.
3. **Consolo enrijecido** (Fig. 9.8):
   - tenrijecedor ≥ tw da viga, com b/t0 < 16.
   - Reação uniforme em a' a partir da borda livre: e = b − a'/2 (Eq. 9.5).
   - Solda pelo item 7.
4. **Viga–viga** (Fig. 9.9):
   - Recorte da mesa da secundária.
   - Verificar **cisalhamento de bloco** na viga recortada, sobretudo com poucos parafusos.

**Verificações da dupla cantoneira:**
1. Parafusos: Fd direto + Fd do momento V·e2 → corte e contato/rasgamento.
2. Solda: centroide, Ip, resultante na extremidade livre ≤ 18,4·t.
3. Cantoneiras:
   - Ruptura por cisalhamento: An desconta o d nominal.
   - Escoamento por cisalhamento.
   - Bloco.
   - Flexão com W líquido.

**Exemplo numérico — 9.10.2** (p.255–258):

**Dados:**
- V = 150 kN → Vd = **210 kN**.
- 2 cantoneiras L 76×9,1 (t = 0,8 cm, lado de 6,5 cm na solda), A36.
- A325 rosca fora do plano, E60.
- Pilar CS 300×62; viga VS 450×51.
- 3 parafusos por cantoneira, s = 50 mm; e2 = 44 mm.

**Parafusos:**
- Ag ≥ 210·1,35/(6·0,5·82,5) = 1,15 cm² → **φ16**.
- Fdy = 35 kN; Fdx = (210/2)·4,4/10 = 46,2 kN; **Fd = 58,0 kN**.
- Corte: 1,98·0,5·82,5/1,35 = **60,5 kN**.
- Contato: 2,4·1,6·0,8·40/1,35 = 91,0 kN.
- Rasgamento: 1,2(5,0 − 1,9/2)·0,8·40/1,35 = 115,2 kN.

**Solda:**
- Centroide: 2·6,5²/2/(2·6,5 + 20) = 1,30 cm; e1 = 76 − 13 = 63 mm.
- Ip = t·2095 cm⁴.
- Esforços por unidade de comprimento: τy·t = 210/(2·33) = 3,18; τx·t = 3,15; τy(M)·t = 1,64 kN/cm.
- Resultante: **5,75 kN/cm**.
- 18,4·t = 5,75 → t = 0,31 cm (o livro escreve "mm"). b = 4,5 mm → **adotar b = 5 mm**.

**Cantoneiras** (Vd = 105 kN cada; Md = 105·4,4 = 462 kN·cm):
- Ruptura por cisalhamento: (20 − 3·1,6)·0,8·0,6·40/1,35 = **216 kN**.
- Escoamento: 20·0,8·0,6·25/1,1 = **218 kN**.
- Bloco: [0,6·25·15·0,8 + 40·(3,2 − 0,95)·0,8]/1,35 = **186,7 kN**.
- Flexão: Ilíq = 453,8 cm⁴; W = 45,4 cm³; σ = 10,2 < 22,7 kN/cm².

**Exemplo numérico — 9.10.6, consolo enrijecido** (p.261–263):

**Dados:**
- Viga W 360×32,9 (tw = 5,8 mm, tf = 8,5 mm, d = 36 cm); Rd = 130 kN; MR250.
- Enrijecedor: t0 > 5,8 mm; b/t0 < 16 com b = 100 → t0 > 6,3 mm → **t0 = 12,5 mm**.

**Verificações:**
- Escoamento por cisalhamento na face soldada: 20·1,25·0,6·40/1,10 = 341 kN. O livro usa fu = 40 numa expressão de escoamento: **provável erro**; com fy dá 205 kN, ainda OK.
- Escoamento local da alma: (2,5·1,2 + a')·25·0,58 ≥ 130 → **a' ≈ 60 mm**. **Sem γa1**, ver as divergências.
- Enrugamento: a' ≈ 40 mm.
- Excentricidade: e = 100 − 60/2 = **70 mm**.
- Solda: ver item 7 → **b = 5 mm**.

---

## 11. Ligações rígidas viga–pilar

**Nomes:** ligação rígida, de momento.
**Tipos** (Fig. 9.10):
- (a) Mesas e alma soldadas direto na mesa do pilar.
- (b) Chapa de topo soldada na viga e parafusada no pilar.
- (c) Talas de mesa (força das mesas) + cantoneiras de alma (cortante).

**Verificações citadas** (p.247–248; sem desenvolvimento numérico):
1. Força nas mesas: M/h. Cortante na alma.
2. Componentes de ligação (parafusos e solda): itens 2 a 7.
3. **Pilar sob forças localizadas** (NBR 5.7), pelos critérios das forças concentradas em vigas (livro, itens 6.3.6 e 6.3.7):
   - Flexão local da mesa do pilar, no lado tracionado.
   - Escoamento local da alma e enrugamento, no lado comprimido.
   - Flambagem da alma.
   - Se algum falhar, pôr pares de enrijecedores horizontais no nível das mesas da viga.
4. O exemplo 6.5.2 (p.~186) verifica um I 254×37,7 com Md = 83 kN·m junto a uma ligação rígida do tipo Fig. 9.10c, com furos na mesa tracionada.

**Lacunas:** sem exemplo completo de chapa de topo (alavanca, T-stub) e sem painel de alma do pilar. Usar outra fonte (CBCA "Ligações em estruturas de aço"; Queiroz).

---

## 12. Ligação com pino

**Nomes:** rótula com pino, olhal (*eyebar*).
**Entradas:**
- Diâmetro do pino d; furo d' ≥ d + 1 mm.
- Espessura t das chapas.
- Largura b e distância a do furo à borda.
- fy e fu.

**Verificações** (p.248–249):
1. Flexão do pino: Md,res = 1,5·W·fy/γa1, com W = πd³/32 (Eq. 9.6). O exemplo usa 1,2·πd³/32 = 1,5·W·… [confirmar].
2. Esmagamento da chapa: Rd = t·d·(1,8·fy)/γa2 (Eq. 9.7). fy = o menor das partes.
3. Corte do pino: Vd,res = 0,75·Ag·0,6·fu/γa1 (Eq. 9.8). O exemplo usa fy = 25 e γa1: **ambíguo**, conferir na NBR.
4. Seção A-A (escoamento bruto). Seção B-B: Nd ≤ An,ef·fu/γa2, com An,ef = 2t·bef ≤ (b − d')·t e bef = 2t + 16 mm (Eq. 9.9).
5. Seção C-C (rasgamento): Nd ≤ 0,60·Av·fu/γa2, com Av = 2t(a + d/2) (Eq. 9.10).
6. Geometria: a ≥ 1,33·bef (Eq. 9.11).

**Exemplo numérico — 9.10.4** (p.258–260):

**Dados:** Nk = 600 kN variável, γ = 1,4; chapa central de 50,8 mm; chapas laterais de 25,4 mm (o texto diz "25,4 e 50,8"); MR250.

**Pino:**
- Md = 1,4·(300/2,54)·6,45 = 1066 kN·cm → d ≥ 7,36 cm → **d = 8 cm**.
- Corte: 0,75·π·8²/4·0,6·25/1,1 = **583 kN** > 420 kN.
- Esmagamento: 5,08·8·1,8·25/1,35 = **1355 kN** > 840 kN.

**Chapa:**
- d' = 8,08 cm.
- An,ef = 5,08(2·5,08 + 1,6) = 59,8 cm² → **b ≥ 19,9 cm** (20 cm); 59,8·40/1,35 = 1772 kN.
- Borda: a ≥ 0,65 cm. Geometria: c ≥ 1,33(2·5,08 + 1,6) = 15,6 cm → 16 cm.
- Fora da região do pino: t ≥ 1,85 cm → 25,4 mm (central) e 12,7 mm (laterais).

---

## 13. Apoio móvel com rolo

**Verificações** (p.250–251):
1. **Pressão de contato** (Eq. 9.12; força em N e dimensões em mm):
   - d ≤ 635 mm: Fd = 1,2·(fy − 90)·a·d/(20·γa2).
   - d > 635 mm: Fd = 6,0·(fy − 90)·a·√(d·25,4)/(20·γa2).
2. **Com força horizontal H** (Eq. 9.13): σmáx = Fd/(a·d) + 6·Md/(a·d²) = Fd/(a·d) + 6·Hd/a².
3. **Placa de apoio** (Eqs. 9.14 e 9.15): pressão Fd/(b·a); espessura pela flexão.
4. **Detalhamento:**
   - Sistema isostático; chapas de contato usinadas.
   - Para reduzir a altura: 2 rolos com rótula.
   - Capa de aço inoxidável ao cromo, 5 a 10 mm, para pressões maiores.

**Exemplo numérico — 9.10.3** (p.258):
- Dados: F = 250 kN, H = 50 kN, permanentes, γ = 1,3; MR250; b = 25 cm; a = 35 cm.
- Limite: 1,2(250 − 90)/(1,35·20) = 7,11 MPa = 0,711 kN/cm².
- 1,3·(250/(35d) + 6·50/35²) ≤ 0,711 → **d ≥ 23,7 cm**. Adotado d = 240 mm.
- Placa: pressão média 0,524 kN/cm²; balanço de 12,5 cm; Md = 40,96 kN·cm → t ≥ 2,7 cm → **t = 28,6 mm**.

---

## 14. Base de pilar (placa de base + chumbadores)

**Nomes:** placa de base, *base plate*, ancoragem.
**Categorias:**
- (a) Rotulada: só P e H. Pressão uniforme; chumbadores apenas construtivos.
- (b) Engastada: P, H e M.
  - Excentricidade pequena: compressão em toda a placa.
  - Excentricidade grande: chumbadores tracionados, como numa seção de concreto armado.
  - Enrijecedores verticais na placa.

**Entradas:**
- fck; A1 (placa) e A2 (superfície de concreto concêntrica, ≤ 4·A1).
- P, H e M.
- Aço dos chumbadores: SAE 1020 com fy = 240 e fu = 390 MPa, ou A36.
- Coeficiente de atrito μ.
- Grout de 25 a 50 mm.

**Verificações** (p.251–253):
1. **Concreto** (Eq. 9.16; NBR 6.6.5?) [confirmar]: σc,Rd = fck/(γc·γn)·√(A2/A1) ≤ fck, com γc = γn = 1,4.
   - Na base de cálculo da espessura, o livro usa 0,85·fck/1,4 = 12,1 MPa.
2. **Espessura da placa:** balanços em duas direções (seções A-A e B-B, Fig. 9.15a).
   - Momento por faixa unitária: σ·ℓ²/2.
   - Resistência: (t²/4)·fy/γa1. A Fig. 9.15a usa 0,95h e 0,80bf.
3. **Cisalhamento horizontal:** atrito Fh,máx = μ·P (Eq. 9.17), ou barra de cisalhamento soldada, ou corte dos chumbadores (algumas normas).
4. **Chumbadores:** tração e corte pelos critérios do item 2/4. Ancoragem por gancho ou por chapa embutida.

**Exemplo numérico — 9.10.5** (p.260–261):
- Dados: CS 300×62; Pk = 800 kN, Hk = 60 kN; γ = 1,4; fck = 20 MPa; chumbadores SAE 1020.
- Área: 1120 ≤ 2,0·A1/(1,4·1,4) → **A1 ≥ 1098 cm²** (A2/A1 = 1, conservador). Placa **35×35** (1225 cm²).
- Espessura: σ = 12,1 MPa; maior balanço 5,5 cm; Md = 1,21·5,5²/2 = **18,3 kN·cm/cm**; (t²/4)·25/1,1 ≥ 18,3 → t ≥ 1,79 cm → **t = 20 mm**.
- Chumbadores: 2·0,40·Ag·39/1,35 ≥ 84 kN → Ag ≥ 3,6 cm² → **2 φ22** (Ag = 3,8 cm²).

---

## 15. Contraventamento (força e rigidez das contenções)

**Nomes:** contenção nodal, contenção relativa, diagonais de contraventamento (X ou diagonal simples), contraventamento de cobertura.
**Peças ligadas:** pilar, banzo comprimido de treliça, terças.

**Verificações** (p.215–218; NBR 4.11):
1. **Contenção nodal:**
   - Fbr = 0,010·Nd (Eq. 7.21a). Equivale à imperfeição ℓb/200.
   - Rigidez: k = 2(4 − 2/n)·Nd/ℓb·γr, com γr = 1,35 (Eq. 7.21b).
2. **Contenção relativa:** Fbr = 0,004·Nd (Eq. 7.22a); k = 2·Nd/ℓb·γr (Eq. 7.22b).
3. **Diagonal tracionada:** item 1.
   - O exercício proposto 2.4.5 é uma cantoneira L 50×50×6 com φ9,5, sem resposta.
   - Ligação no gusset: itens 2 e 6.
4. **Classificação da deslocabilidade** (p.215):
   - B2 ≤ 1,1: pequena.
   - 1,1 < B2 ≤ 1,4: média (rigidez × 0,8).
   - B2 > 1,4: grande.
   - Desaprumo h/333.

**Exemplo numérico:** não há exemplo numérico de contraventamento. **Lacuna.**

---

## 16. Nó de treliça com gusset (ligações de treliça)

Cap. 8, p.231–239.

**Regras:**
- Eixos das barras concorrentes num ponto.
- Ligação parafusada de cantoneira: a NBR permite fazer concorrer as linhas de furação e desprezar o momento, se não houver fadiga.
- Mínimos de banzo: L 50×50, chapa de 6 mm, parafuso de 12,5 mm.
- **Resistência mínima da ligação axial: 45 kN** (NBR) [confirmar o item].
- Nós compactos.
- Modelo treliça (K = 1) para barras esbeltas sem fadiga. Modelo pórtico se houver excentricidade.

**Verificações do gusset:**
1. Filetes balanceados por cantoneira (item 6), com força/2 por cantoneira.
2. **Seção de Whitmore** (espraiamento de 30° para cada lado, setor de 60°): Nd,res = Aef·fy/γa1.
3. Bloco ao longo da linha r-r.

**Exemplo numérico — 8.7.1** (p.233–239):
- Treliça de 18 m, MR250, soldada, E60.
- Combinações:
  - C1 = 1,3g + 1,4p + 1,4·0,6V1.
  - C2 = 1,3g + 1,4V1 + 1,4·0,5p.
  - C3 = 1,0g + 1,4V2.
- **Tabela de esforços** (Tabela 8.1, p.234): B2: −92,2 / +104,5 kN; D1: 72,6 / −85,2 kN; …
- **Banzo 2L 76×9,1** (espaçador de 8 mm), compressão:
  - ix = 2,33 cm; Iy = 280,0 cm⁴; iy = 3,5 cm.
  - Esbeltez: 128,7 no plano; 171 fora do plano.
  - λ0 = 1,90 → fc = 0,243·fy = 60,7 MPa.
  - **Nd,res = 126,7 kN** > 104 kN.
  - Espaçamento das chapas de ligação: ℓ1 ≤ 1,5/2·171 ≈ 130 cm.
- **Banzo em tração:** Ct = 1 − 2,21/7,0 = 0,68; Ae = 15,7 cm² → **465 kN**.
- **D1 2L 64×7,4:**
  - ix = 1,93; iy = 2,98; λ = 164 → χ = 0,265.
  - **114,2 kN** > 85,2 kN; ℓ1 ≤ 100 cm.
- **2L 44×3,15 em tração:** Ct = 1 − 1,29/7,0 = 0,82 → **48,6 kN** > 42,2 kN.
- **Ligação de B6** (b = 5 mm):
  - Equações: 5,39·ℓ1 − 2,21·ℓ2 = 0; (ℓ1 + ℓ2)·0,7b·0,6·415/1,35 = 104.000/2.
  - Resultado: ℓ1 = 57 mm, ℓ2 = 23 mm → adotados **100 e 40 mm**.
- **Ligação de D2** (b = 3 mm; força mínima de 45 kN): ℓ1 = 41 mm, ℓ2 = 16 mm → adotados 100 e 40 mm.
- **Gusset de 8 mm:**
  - Whitmore: 15,7·0,8·25/1,1 = **285 kN**.
  - Bloco: (0,60·25·2·10 + 40·7,5)·0,80/1,35 = **355 kN**.

---

## 17. Exemplos de barras (NBR 8800) úteis como teste de regressão do motor

| Ex. (pág.) | Verificação | Dados | Esperado |
|---|---|---|---|
| 5.8.1 (p.138–140) | Compressão, flambagem por flexão | W 150×37,1, A36, L = 3 m rotulado; A = 47,8 cm²; ix = 6,85, iy = 3,84 cm | Eixo x: λ = 43,8; λ0 = 0,49; χ = 0,904 → **982,1 kN**. Eixo y: λ = 78,1; λ0 = 0,88; χ = 0,723 → **786 kN**. Esbeltezes locais: mesa 6,6 < 15,8; alma 17,2 < 42,1 (Q = 1) |
| 5.8.2 (p.140) | Compressão, K | Mesmo perfil | Engaste–livre (K = 2): λ = 156; λ0 = 1,76; fc = 71 MPa → **308 kN**. Engaste–rótula (K = 0,7): λ = 54,7; λ0 = 0,62; fc = 212 MPa → **921 kN** |
| 5.8.5 (p.141) | Compressão, esbelta | W 310×21,0; KL = 3 m; imín = 1,90 cm; A = 27,2 cm² | λ = 157. MR250: λ0 = 1,77; χ = 0,280 → **173,1 kN**. AR350: λ0 = 2,09; χ = 0,201; fc = 70,4 MPa (igual ao MR250) |
| 5.8.3, 5.8.4, 5.8.6–5.8.10 (p.141–151) | Seções múltiplas, cantoneiras duplas, perfil CS, flambagem local (Q) | ver livro | Não extraídos em detalhe. O 5.8.10 (H soldado 8×300 / 8×400, KL = 9,8 m) testa Q < 1 |
| 6.5.1 (p.184) | Flexão, viga contida | W 530×85 (Z = 2100 cm³; bf/2tf = 5,0; h/tw = 46,4) e VS 500×86 (Z = 2281 cm³) | **477,3 kN·m** e **518,4 kN·m** |
| 6.5.7 (p.185–193) | FLT com Cb | VS 400×49; viga contínua de 4 vãos de 8 m; Lb = 800 cm; Wx = 870 cm³; Iy = 1267 cm⁴; J = 14,6 cm⁴; Cw = 483.188 cm⁶ | β1 = 0,0521; Lr = 617 cm < Lb (viga longa). Cb = 12,5·0,17/(2,5·0,17 + 3·0,085 + 4·0,17 + 3·0,004) = **1,55**. Mcr = 1,55·10.360 = **16.058 kN·cm**; Md,res = **14.598 kN·cm**. Com Md = 1,3·0,170·P·800: **P = 82,5 kN**. Flecha 15 mm < L/350 = 23 mm |
| 6.5.9 (p.195–196) | Flexão, cortante, forças localizadas | W 530×92, MR250, vão 5 m; Z = 2359,8 cm³; a' = 100 mm; h = 53,3 cm; tw = 1,02 cm; tf = 1,56 cm | Md = **536,1 kN·m** → qd = **171,5 kN/m**. Vd = 429 kN; Vd,res = 53,3·1,02·0,6·25/1,1 = **741 kN** (h/tw = 46,6 < 69,6). Escoamento local da alma: 431 kN (**sem γa1**; com γa1 dá 392 kN). Enrugamento: **353 kN** → precisa de enrijecedor |
| 6.5.10, 6.5.11 (p.196–204) | Enrijecedor de apoio, viga soldada alta, FLT | VS 500×61 (vão 8 m, 25 kN/m); viga 2 m×22 m | Não extraídos em detalhe |
| 7.6.1 (p.219–220) | Flexocompressão com B1 | CVS 450×116, MR250; Pk = 800 kN; Mk = 50 kN·m; KLx = 6 m; contenção lateral contínua; A = 148,3 cm²; ix = 18,88 cm; Zx = 2629 cm³ | Nd = 1120 kN; Ne = 28.948 kN; B1 = 1,039; Md = **72,7 kN·m**. λ0 = 0,36; χ = 0,947 → **3192 kN**. Md,res = 2629·25/1,1 = 59.750 kN·cm = **597,5 kN·m** (o texto extraído mostra "591,7"; conferir na página) |
| 7.6.2 (p.221) | Flexocompressão com FLT, interpolação entre Lp e Lr | Idem, sem contenção | Eixo y: λ = 86; χ = 0,685 → **2309 kN**. Lp = 348 cm; Lr = 1342 cm; Mr = 2348·17,5 = 41.090; Mp = 65.725 kN·cm. Mn = **594,7 kN·m**; Md,res = **540,6 kN·m** |
| 7.6.3 (p.221–222) | Flexão oblíqua | + excentricidade de 2 cm no eixo y | My = 16 → Ne,y = 3958 kN → Mdy = 31,2 kN·m. Zy = 720 cm³ → 164 kN·m. **Termos**: 0,49 + 0,13 + 0,19 |
| 7.6.4 (p.222–223) | Escora de duplo I soldado (celular) | 2× I 381×63,3; L = 20 m (x) / 10 m (y); A = 161,2 cm²; Zx = 2184 cm³ | M1d = 147 kN·m; Ne = 1799 kN; Mdx = 192 kN·m. λ0 = 1,49; χ = 0,400 → 1465 kN; Md,res = 496,4 kN·m. **Termos**: 0,28 + 0,38 |

**Atenção ao usar o Cap. 7 como teste:** a soma final do livro é linear, N/NR + M/MR, e não aplica o 8/9 da NBR 5.5.1.2 (Nsd/NRd ≥ 0,2). Exemplo: 0,49 + 0,13 = 0,62, quando a NBR daria 0,485 + (8/9)·0,134 = 0,60. **Testar só os termos (NRd, MRd, B1, Ne), não a soma.**

---

## 18. Divergências e erros do livro encontrados (não copiar para o motor)

1. **Escoamento local da alma sem γa1:** 6.5.9 (p.196) e 9.10.6 (p.262). O livro calcula (2,5k + a')·fy·tw sem dividir por 1,10.
2. **4.6.2 (p.109):** resistência ao cisalhamento da chapa de 12 mm (360 kN) sem γa1. Correto: 327 kN.
3. **9.10.6 (p.262):** "escoamento por cisalhamento" calculado com 40 kN/cm² (fu) e γa1.
4. **Interação flexocompressão** no Cap. 7: soma linear sem 8/9 (ver item 17).
5. **Ct:** o livro não cita o limite superior de 0,90 da NBR 5.2.5.
6. **Inconsistências de cópia:**
   - Escoamento do U 381: 1459 contra 1444 kN (2.3.7 e 2.3.8).
   - Momento na alma: 55,9 contra 55,2 kN·m (9.10.1).
   - Resultante 70,5 comparada a 71,2 kN no rasgamento (9.10.1).
   - Força por unidade: 3,62 contra 3,63 kN/cm (4.6.5).
   - tmín = "0,31 mm", que deveria ser cm (9.10.2).
   - "462 kN/cm", que deveria ser kN·cm (9.10.2).
   - Md,res: o texto extraído mostra 591,7 kN·m, mas 2629·25/1,1 dá 597,5 kN·m (7.6.1; conferir na página).
7. **Atrito:** o livro só trata o deslizamento como estado-limite de serviço (0,80·μ·Ch·P). O ELU com furo alargado (NBR 6.3.4.3) não é desenvolvido.
8. **Efeito alavanca, painel de alma do pilar e chapa de topo (T-stub):** o livro não traz exemplos. Buscar em outra fonte antes de implementar.

---

## 19. Resumo

Complexidade: B = baixa, M = média, A = alta. Prioridade para o motor do Estructto (galpões e pórticos): 1 = primeiro.

| Tipo | Categoria | Complexidade | Prioridade |
|---|---|---|---|
| Peça tracionada na ligação (An, Ct, bloco) | Base / elemento | B | 1 |
| Parafuso por contato: corte + contato/rasgamento (talas, gusset) | Parafusada | B | 1 |
| Parafuso a tração e interação tração + corte | Parafusada | M | 1 |
| Solda de filete axial / em T (incluindo filetes balanceados em cantoneira) | Soldada | B | 1 |
| Dupla cantoneira de alma (viga–pilar, viga–viga) | Flexível | M | 1 |
| Placa de base rotulada (concreto, espessura, chumbadores ao corte) | Base | M | 1 |
| Nó de treliça com gusset (Whitmore, bloco, 45 kN mín.) | Treliça / contraventamento | M | 2 |
| Contraventamento: força e rigidez (0,010/0,004·Nd) + diagonal tracionada | Contraventamento | B | 2 |
| Grupo de parafusos excêntrico (consolo parafusado) | Parafusada | M | 2 |
| Solda excêntrica / chapa de topo soldada / solda mesa–alma | Soldada | M | 2 |
| Emenda de viga com talas parafusadas | Emenda | M | 2 |
| Cantoneira de apoio e consolo enrijecido | Flexível | M | 3 |
| Ligação rígida viga–pilar (chapa de topo, talas; forças localizadas no pilar) | Rígida | A (falta fonte numérica) | 2 |
| Placa de base engastada (grande excentricidade, chumbadores tracionados) | Base | A (o livro não desenvolve) | 3 |
| Emenda de pilar (contato usinado / talas) | Emenda | M (sem exemplo) | 3 |
| Ligação por atrito (deslizamento em serviço) | Parafusada | B | 3 |
| Emenda de viga soldada (penetração total) | Emenda | B | 4 |
| Ligação com pino | Articulação | M | 4 |
| Apoio móvel com rolo | Aparelho de apoio | B | 5 |
