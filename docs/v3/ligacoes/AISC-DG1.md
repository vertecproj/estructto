# Placa de base e chumbadores: leitura da referência "AISC DG1" e lacunas do motor

Data: 20/10/2026. Pesquisa e extração. Este documento não altera código.

## 0. A fonte NÃO é o AISC Design Guide 1

Abri `referencias/livros/2ndEditionGuide.pdf` com PyMuPDF. Ele tem 352 páginas e o arquivo de origem se chama "Chapter1-18.doc". O conteúdo é **Kulak, Fisher & Struik, *Guide to Design Criteria for Bolted and Riveted Joints*, 2ª ed.**, publicado pelo RCSC/AISC (copyright de 2001, texto de 1987). O livro não é o *Design Guide 1 — Base Plate and Anchor Rod Design* (Fisher & Kloiber, 2ª ed., 2006).

Consequências:

- O livro **não trata de placa de base**. Ele cita chumbadores uma única vez, como exemplo de peça roscada tracionada (cap. 1, p. 5 impressa, p. 15 do PDF).
- O que serve dele para placa de base: a resistência do parafuso à tração e ao corte e a interação entre os dois (cap. 4), e o efeito alavanca, *prying* (cap. 17). O efeito alavanca é o mesmo mecanismo que aparece na placa tracionada pelos chumbadores. Ver §2.
- O livro **não tem exemplos numéricos resolvidos**. Procurei por "example" e "worked" e não encontrei nada.
- Os casos de projeto do DG1 (§1) foram escritos **de memória técnica, não extraídos de fonte**. Eles estão marcados com **[DG1-memória]** e não têm página. Confira-os no DG1 real antes de usá-los como critério.
- Para os testes de regressão (§3), usei duas fontes que estão na mesma pasta e trazem números no padrão brasileiro: o *Manual de Ligações* do CBCA/Gerdau e Pfeil & Pfeil. Recalculei os dois exemplos e **os resultados conferem**.
- Pendência: obter o PDF real do DG1 (AISC, 2ª ed. 2006, ou 3ª ed. 2024) e substituir as marcas [DG1-memória].

Notação: N é a dimensão da placa no plano do momento (no motor, `H`). B é a dimensão transversal. d e bf são a altura e a largura da mesa do pilar. f é a distância do centro da placa à linha de chumbadores tracionados. Pu, Mu e Vu são os esforços de cálculo.

## 1. Casos de projeto e procedimento, com tradução para NBR

### 1.1 Diferenças de coeficientes LRFD → NBR (vale para todo o documento)

| Grandeza | AISC/ACI (LRFD) | NBR 8800:2008 / NBR 6118 | Fator equivalente |
|---|---|---|---|
| Pressão no concreto | φc = 0,65 sobre 0,85 f'c √(A2/A1) ≤ 1,7 f'c | 6.6.5: σc,Rd = fck/(γc·γn)·√(A2/A1) ≤ fck, com γc = γn = 1,4 | AISC 0,553 f'c contra NBR 0,510 fck (para A2 = A1). O teto difere: 1,105 f'c contra fck |
| Flexão da chapa | φb = 0,90 sobre Fy·Z | Mrd = Z·fy/γa1, com γa1 = 1,10 | 0,90 contra 0,909. O CBCA limita Z a 1,25 W (ver §3.1) |
| Tração no chumbador | φ = 0,75 sobre 0,75 Fu·Ab | 6.3.3.1: Ft,Rd = 0,75·Ab·fub/γa2, com γa2 = 1,35 | 0,5625 contra 0,556 |
| Corte no chumbador (rosca no plano) | φ = 0,75 sobre 0,40 Fu (AISC 2005) ou 0,45 Fu (2010+) | 6.3.3.2: Fv,Rd = 0,4·Ab·fub/γa2 | 0,30 ou 0,34 contra 0,296 |
| Tração + corte | AISC J3.7 (F'nt modificado) | 6.3.3.4: (Ft,Sd/Ft,Rd)² + (Fv,Sd/Fv,Rd)² ≤ 1 | Elipse. O Kulak (cap. 4) mostra que a elipse representa os ensaios |
| Concreto do chumbador (arrancamento) | ACI 318 cap. 17 (Apêndice D na época do DG1), φ = 0,70 a 0,75 | Não há norma brasileira equivalente para ancoragem. A NBR 6118 9.4 trata só da ancoragem por aderência | Usar ACI 318 cap. 17 ou EN 1992-4, declarando a norma adotada |
| Combinações | 1,2D + 1,6L etc. (ASCE 7) | NBR 8681 / NBR 8800 4.7: 1,25 a 1,4 G + 1,5 Q | Os esforços já chegam majorados do solver; não aplicar fator global sobre eles |

### 1.2 Compressão axial (pilar rotulado)  [DG1-memória]

1. Área necessária. A1 ≥ Pu / σc,Rd. Com A2 = A1 (lado seguro), σc,Rd = fck/1,96.
2. Dimensões. Escolher N ≥ 0,95 d + 2m e B ≥ 0,8 bf + 2n, de preferência com m ≈ n.
3. Balanços. m = (N − 0,95 d)/2 e n = (B − 0,8 bf)/2. Para pilar leve, calcular também o balanço da região entre mesas (Thornton):
   - X = [4 d bf / (d + bf)²] · Pu / Pp,Rd;
   - λ = 2√X / (1 + √(1 − X)) ≤ 1;
   - λn' = λ·√(d bf)/4;
   - ℓ = max(m, n, λn').
4. Espessura. Igualar o momento por unidade de largura, fp·ℓ²/2 com fp = Pu/(B·N), ao resistente da chapa, t²/4·fy/γa1. Daí t ≥ ℓ·√(2 fp γa1 / fy).
5. Corte. Ver §1.5. Mínimo construtivo: 4 chumbadores (OSHA 1926.755), exceto em postes leves.

### 1.3 Momento com pequena excentricidade (e = Mu/Pu ≤ e_crit)  [DG1-memória]

1. Calcular qmax = σc,Rd·B (força por unidade de comprimento) e e_crit = N/2 − Pu/(2 qmax).
2. Se e ≤ e_crit, não há tração nos chumbadores. O bloco de pressão tem comprimento Y = N − 2e e intensidade q = Pu/Y.
3. Espessura no lado comprimido, com balanço m:
   - se Y ≥ m: t ≥ 1,5 m·√(fp γa1/fy);
   - se Y < m: t ≥ 2,11·√(fp·Y·(m − Y/2)·γa1/fy).
   Aqui fp = q/B, já majorado.

### 1.4 Momento com grande excentricidade (e > e_crit), tração e arrancamento  [DG1-memória]

1. Verificar se existe solução: (f + N/2)² ≥ 2 Pu (e + f)/qmax. Se não existir, aumentar a placa.
2. Bloco de pressão: Y = (f + N/2) − √[(f + N/2)² − 2 Pu (e + f)/qmax].
3. Tração total nos chumbadores: Tu = qmax·Y − Pu.
4. Chapa no lado comprimido: como em §1.3, com fp = σc,Rd.
5. Chapa no lado tracionado. Braço x = f − d/2 + tf/2. Largura efetiva por chumbador ≈ 2x, com espraiamento a 45°, limitada a B. Espessura t ≥ 2,11·√(Tu·x·γa1/(B·fy)). Nos chumbadores fora do contorno do pilar, cabe a análise de efeito alavanca do §2.
6. Arrancamento puro (Pu de tração). A placa trabalha só com os chumbadores, em balanço a partir das faces do pilar ou entre mesas. Verificar a chapa como acima e cada chumbador à tração (§1.6).

### 1.5 Corte  [DG1-memória]

Caminhos possíveis, sem somar caminhos de rigidez diferente:

| Mecanismo | Resistência de cálculo | Notas |
|---|---|---|
| Atrito chapa–graute | 0,75·μ·Pu,compressão, com μ = 0,55 (aço sobre graute, ACI 349) | Só com compressão concomitante da mesma combinação. Pfeil (eq. 9.17, p. 252) traz Fh,máx = μP sem fixar μ. O CBCA (PBR, p. 203) adota resistência horizontal = 10% de Nd |
| Barra de cisalhamento (*shear lug*) | (a) Pressão no concreto à frente da barra, sobre a área embutida abaixo do graute. (b) Flexão da barra em balanço, Mu = Vu·(G + h_emb/2), t ≥ √(4 Mu γa1/(fy·W)). (c) Cone de corte do concreto à frente da barra, com tensão uniforme 4φ√f'c (psi) na área projetada (ACI 349). (d) Solda barra–placa | Os coeficientes de (a) e (c) estão **a conferir** no DG1/ACI 349 |
| Chumbadores ao corte | Fv,Rd por chumbador (NBR 6.3.3.2). Com furo alargado, o DG1 recomenda contar **só 2 chumbadores**, ou usar arruela-chapa soldada à placa para que todos trabalhem. Se houver graute, considerar a flexão da haste com braço ≈ espessura do graute + t/2 | Interação com a tração pela NBR 6.3.3.4 |

Furos alargados para chumbadores (DG1 2ª ed. tab. 2.3 = AISC Manual 15ª ed. tab. 14-2), diâmetro do chumbador → furo
na placa: 3/4 → 1 5/16; 7/8 → 1 9/16; 1 → 1 13/16; 1¼ → 2 1/16; 1½ → 2 5/16; 1¾ → 2¾; 2 → 3¼; 2½ → 3¾ pol
(acréscimo de 9/16" a 1¼"). Arruela-chapa espessa sobre o furo. O `placa_base.py` usa esses acréscimos
(+14,3 / 17,5 / 20,6 / 25,4 / 31,8 mm). Esses furos excedem o furo alargado da NBR 8800 tab. 12 (db + 5 mm até
24 mm); a 6.3.6.1 então exige arruelas especialmente dimensionadas soldadas à placa, e o motor avisa quando
`arruela_soldada` não está marcada.

### 1.6 Chumbador no concreto (ACI 318 cap. 17)  [DG1-memória]

Verificar nesta ordem e tomar a menor resistência:

1. Aço da haste à tração e ao corte (NBR 8800 6.3.3; Ab é a área bruta, o fator 0,75 já cobre a rosca).
2. Arrancamento de cone (*breakout*):
   - Ncb = (ANc/ANco)·ψed·ψc·ψcp·Nb;
   - Nb = kc·√f'c·hef^1,5, com kc = 24 para chumbador pré-concretado (unidades psi/in);
   - ANco = 9·hef² (cone a 1,5 hef);
   - ψc = 1,25 para concreto não fissurado.
3. Arrancamento da ancoragem (*pullout*):
   - cabeça ou chapa: Np = 8·Abrg·f'c, com ψc,P = 1,4 se não fissurado;
   - gancho: Np = 0,9·f'c·eh·da (desaconselhado pelo DG1);
   - deslizamento por aderência não vale para haste lisa.
4. Fendilhamento lateral (*side-face blowout*), quando a distância à borda c < 0,4 hef.
5. Corte do concreto na borda, Vcb, e arrancamento por alavanca (*pryout*), Vcp = kcp·Ncb.
6. Interação: (Nua/φNn)^(5/3) + (Vua/φVn)^(5/3) ≤ 1, ou o trilinear 1,2.
7. Alternativa preferida pelo DG1 para grande tração: armadura de ancoragem (grampos) que recebe a força do cone. Essa armadura pode ser verificada pela NBR 6118 (ancoragem 9.4 e tirante).

Espaçamento mínimo: 4 da para pré-concretado, 6 da para chumbador de expansão por torque (ACI 318 17.9).

## 2. O que o Kulak & Fisher contribui (extraído, com página)

Páginas no formato "impressa / PDF". A diferença é de +14 páginas no corpo do livro.

| Tema | Conteúdo resumido | Página |
|---|---|---|
| Chumbador como peça roscada | Resistência à tração calculada pela área efetiva da rosca (*stress area*) | 5 / 15 |
| Tração de parafuso | Bu = As·σu. Em área nominal, ≈ 0,75·Ab·σu, porque As/Ab vai de 0,75 a 0,79 | 70 / 84, eqs. 4.2–4.3 |
| Corte | τu = 0,62 σu. Com rosca no plano, Su = 0,62 × 0,70 × Ab σu ≈ 0,43·Ab·σu por plano | 71 / 85, eqs. 4.4–4.7 |
| Tração + corte | Elipse x²/0,62² + y² = 1 (tensões/σu, na área efetiva). Base da NBR 6.3.3.4 | 50–51 / 64–65, eq. 4.1; 71 / 85 |
| Torque | Relação antiga ≈ 0,0167 lb·ft por polegada de diâmetro por libra de força, ou seja, K ≈ 0,20. Dispersão de ±30% na pré-carga | 52 / 66 |
| Pré-carga e separação | Antes da separação das chapas, o parafuso recebe só 5% a 10% da carga externa, porque kb/kp ≈ 0,05 a 0,10. A separação ocorre em T = B0(1 + kb/kp) | 263–266 / 278–281, eqs. 17.1–17.4 |
| Efeito alavanca | Modelo de Struik & de Back com a e b corrigidos: b' = b − d/2 e a' = a + d/2. Usar a ≤ 1,25 b | 277–281 / 292–296 |
| Equações | Q·a = α·δ·w·t²·σy/4; B = T + Q; B = T·[1 + δα/(1+δα)·b'/a']; t = √{4 B a' b' / (w σy [a' + αδ(a'+b')])}. δ = área líquida na linha de furos / área bruta na face da alma; α ≤ 1. Para α ≥ 1, usar α = 1 e B̄ = 2T[1 + δ/(1+δ)·b'/a'] | 279 / 294, eqs. 17.10–17.12; 284–285 / 299–300 |
| Coeficientes | Em tensões admissíveis, B_all = 0,75 Ab σu / 2. Em LRFD, φ = 0,85 com fator de carga 1,7 | 283–285 / 298–300 |
| Fadiga | Q ≤ 0,6 T. Limites de B_all, 0,90 B_all e 0,75 B_all para < 2×10⁴, de 2×10⁴ a 5×10⁵ e > 5×10⁵ ciclos | 287 / 302 |
| Mais de 2 linhas de parafusos | As linhas externas são pouco eficazes sem enrijecedor. A eficiência medida ficou entre 45% e 80% | 271–272 / 286–287 |

Aplicação direta ao motor: o chumbador fora do contorno do pilar, numa placa sem enrijecedor, é um "T-stub" sobre base rígida, que aqui é o concreto. A força no chumbador cresce de Q. Para equipamento rotativo, o que já é o caso do guincho dos testes do motor, vale o limite de fadiga Q ≤ 0,6 T e a recomendação de placa rígida.

## 3. Exemplos resolvidos para teste de regressão (unidades SI, mantidas como no original)

Nenhum vem do arquivo pedido, porque ele não tem exemplos. Os dois casos abaixo foram **recalculados por mim e reproduzem o livro**.

### 3.1 CBCA/Gerdau, *Manual de Ligações* — bases rotuladas PBR (cap. 10)

Fórmulas (p. 204):

- fc = Nd/(Apb·Bpb) ≤ fck·√(A2/A1)/(1,4·1,4) ≤ fck;
- t ≥ √[6·Mmax/(1,25·fy/1,1)], com Mmax = fc·ℓ²/2 por unidade de largura;
- ℓ = max(m, n), com m = (Apb − 0,95 d)/2 e n = (Bpb − 0,8 bf)/2.

O ℓ foi deduzido e confere com a tabela. O manual não traz o termo λn'.

Material: chapa ASTM A36 (fy = 250 MPa), 2 chumbadores Ø1" ASTM A36 entre mesas, E70XX, fck ≥ 20 MPa.

Exemplo do livro (p. 203): pilar W 310×52,0, CP = 500 kN, SC = 400 kN, Nd = 1,4·500 + 1,5·400 = 1300 kN. A tabela (p. 219) dá Nd,max = 1833 kN, então o detalhe atende.

Linhas da tabela p. 219 usadas como teste. W310×52: d = 317, bf = 167. W310×38,7: d = 310, bf = 165. Dimensões em mm.

| Perfil | Apb×Bpb | Bloco Abc×Bbc | t | Nd,max (livro) | fc (MPa) | m / n | Mmax (N·mm/mm) | t recalculado |
|---|---|---|---|---|---|---|---|---|
| W310×52,0 | 450×300 | 650×450 | 31,5 | 1833 kN | 13,578 | 74,4 / 83,2 | 46 994 | **31,50** |
| W310×52,0 | 350×250 | 450×350 | 19,0 | 883 kN | 10,091 | 24,4 / 58,2 | 17 091 | **19,00** |
| W310×38,7 | 350×300 | 450×500 | 31,5 | 1398 kN | 13,314 | 27,8 / 84,0 | 46 973 | **31,50** |

Para a primeira linha, o concreto não governa. σc,Rd = 20·√(292 500/135 000)/1,96 = 15,02 MPa, o que dá Nd ≤ 2028 kN, acima de 1833 kN. O valor tabelado é governado pela flexão da chapa. Tolerância sugerida: 1%.

Observação: o CBCA usa 1,25·W·fy (W = t²/6) e não Z = t²/4. Isso equivale a limitar o fator de forma a 1,25, coerente com o limite de 1,25 W na NBR para chapa fletida no eixo de menor inércia. É uma escolha de método a registrar no motor.

### 3.2 Pfeil & Pfeil, *Estruturas de Aço — dimensionamento prático*, Probl. 9.10.5 (p. 260–261 impressa, 281–282 do PDF)

Dados: pilar CS 300×62 (d = bf = 300 mm), P = 800 kN e H = 60 kN característicos, γ = 1,4, fck = 20 MPa, chapa MR250, chumbadores SAE 1020 (fu = 390 MPa).

| Passo | Livro | Recalculado |
|---|---|---|
| Nd | 1120 kN | 1120 |
| A1 com A2/A1 = 1: Nd ≤ fck·A1/1,96 | A1 > 1098 cm², adota 35×35 = 1225 cm² | 1097,6 cm² |
| Tensão de projeto adotada | 0,85 fck/1,4 = 12,1 MPa. O livro usa a **resistência**, não a pressão atuante | 12,14 MPa |
| Balanços | 3,25 cm (0,95 d) e 5,5 cm (0,8 bf) | m = 32,5 mm, n = 55 mm |
| Md por faixa unitária | 18,3 kN·cm/cm | 18,4 |
| t ≥ √(4 Md γa1/fy), plástico com Z = t²/4 | t > 1,79 cm, adota 20 mm | 1,798 cm |
| Corte: 2·0,40·Ag·fu/1,35 ≥ 1,4·60 = 84 kN | Ag > 3,6 cm², adota 2 Ø22 (3,8 cm²) | 3,63 cm² |

Os dois exemplos divergem de propósito no método da chapa. O CBCA usa a pressão atuante e 1,25 W. O Pfeil usa a pressão resistente e Z. Os testes devem fixar o método escolhido.

### 3.3 Exemplos do DG1 real

Não foram extraídos, porque não tenho a fonte. Os exemplos 4.1 a 4.x do DG1 (compressão, pequena e grande excentricidade, barra de cisalhamento, arrancamento) devem entrar quando o PDF correto chegar. Converter para SI e manter o original imperial na tabela do teste.

## 4. Lacunas de `codigo/motor/prossen_estrutural/aco/placa_base.py`

O próprio módulo se declara "legado simplificado" (`placa_base.py:15-27`). As lacunas abaixo estão em ordem de risco.

### 4.1 Arriscado (pode ficar contra a segurança)

1. **Pivô na borda da chapa, sem bloco de compressão do concreto.** Ver `placa_base.py:191-198`, com `a = H/2` na linha 192. O braço de alavanca fica maior que o real, f + N/2 − Y/2, então a tração nos chumbadores **sai subestimada**.
   - Teste que fiz: N = −500 kN (compressão), M = 150 kN·m, placa de 0,50×0,40 m, 4 chumbadores em y = ±0,20 m, fck = 20 MPa.
   - `forcas_fixacoes` deu 27,4 kN por chumbador tracionado, ou 61 kN no total.
   - Pelo bloco do DG1 com σc,Rd = fck/1,96: Y = 0,167 m e Tu = 182 kN, ou seja, **≈ 91 kN por chumbador, 3,3 vezes mais**.
2. **Chumbadores do lado comprimido recebem tração.** Pela linha 193, `d_i = max(0, u + a)` é positivo para todo chumbador dentro da chapa. No teste acima, eles receberam 3,05 kN cada. O efeito é pequeno, mas a distribuição deixa de ser física.
3. **Pressão no concreto não é verificada.** Ver `LIMITACOES`, `placa_base.py:19`. Uma placa pequena pode passar com o concreto esmagado. O motor não aplica NBR 8800 6.6.5 nem √(A2/A1).
4. **Coeficiente global sobre esforço possivelmente já majorado.** A segurança n (`seguranca`, de 1,5 a 2) é aplicada sobre a tensão de prova Sp com von Mises (`placa_base.py:234-238`) e sobre fy na chapa (`placa_base.py:267, 272`). Se o solver entregar combinações NBR de ELU, a segurança conta duas vezes. Se entregar combinações de serviço, falta coerência com a NBR 8800 6.3.3, que usa fub/γa2. O contrato não diz qual é o caso.
5. **Corte dividido igualmente** entre todos os chumbadores (`Fv = V/len(u)`, `placa_base.py:200`). O motor não tem atrito, nem barra de cisalhamento, nem a regra de furo alargado do DG1 (só 2 chumbadores efetivos), nem flexão da haste sobre o graute.
6. **Concreto do chumbador só por catálogo pós-instalado.** O método usa interação 5/3, correção √fck e ψ como produto de projeções 1D (`placa_base.py:310-336, 370-378`). Faltam distância à borda do bloco, concreto fissurado, *pullout* e *side-face blowout*, *pryout* e corte de borda. Chumbador pré-concretado (barra com porca e chapa, o caso padrão do DG1, do CBCA e do Pfeil) não pode ser verificado.

### 4.2 Diferente do DG1/NBR (tende ao lado seguro, mas não é norma)

7. **Compressão pura.** Ver `placa_base.py:270-272`. A compressão inteira é posta no pivô, como carga concentrada com braço `abs(pivo_y) − ref_in/2`. Usa a altura do perfil e não 0,95 d, ignora n = (B − 0,8 bf)/2 e não tem λn'.
   - Teste com N = −500 kN, placa de 0,40×0,40 m e d = 0,20 m, n = 1: `EspNecC` deu 54,8 mm. A pressão uniforme no mesmo critério elástico dá 20,3 mm.
   - O resultado é 2,7 vezes conservador e superdimensiona as bases rotuladas.
   - Com M = 0, o lado do pivô é arbitrário (linha 185).
8. **Chapa tracionada.** Faixa em balanço com largura `q·(2e + d)` e módulo elástico 6/t² (`placa_base.py:265-267`). O DG1 e a NBR usam módulo plástico (Z, ou 1,25 W no CBCA) e largura de 45° ≈ 2x. O motor não tem efeito alavanca (§2) nem chapa entre mesas.
9. **Parafuso.** Tensão de prova Sp e von Mises (`placa_base.py:221-243`) no lugar de Ft,Rd/Fv,Rd e da interação elíptica da NBR 6.3.3.4, que é a validada pelo Kulak (eq. 4.1).
10. **Solda.** Tensão admissível de eletrodo (`placa_base.py:35, 445-447`) no lugar de NBR 8800 6.2.5 (metal base e solda). Não há limites de 4b/40 mm nem t − 1,5 mm (`placa_base.py:22`).
11. **Espaçamento 6d aplicado a todo chumbador** (`placa_base.py:391`). Para pré-concretado, o ACI pede 4d. A distância à borda do concreto nunca é verificada, porque `borda` é medida até a borda da chapa (`placa_base.py:82`).

### 4.3 Falta de cobertura

12. Momento só no plano (`forcas_fixacoes` usa só y, `placa_base.py:186`). Não há flexão biaxial nem corte em duas direções.
13. Não há enrijecedores, barra de cisalhamento, porca de nivelamento ou graute (espessura e fck do graute), nem placa com chumbadores fora do contorno.
14. Furos e arruelas: não há tabela de furo alargado nem espessura de arruela-chapa.
15. Mínimo de 4 chumbadores (OSHA) e chumbadores entre mesas (padrão CBCA, PBR p. 203) não são tratados. A malha só põe chumbadores no contorno (`placa_base.py:136-138`).
16. `dimensionar_chumbador` aceita a solução sem conferir chapa nem solda (`placa_base.py:584`).

### 4.4 O que está correto e deve ser mantido

- Torque com K = 0,2 (`placa_base.py:241`), coerente com o Kulak, p. 52/66. Registrar a dispersão de ±30%.
- A agregação `ok` exige chapa e solda (`placa_base.py:565`).
- Avisos explícitos para barra inclinada e corte não informado.
- O modo legado deve continuar como `METODO`. O novo método (DG1 + NBR) entra como método separado, sem apagar a compatibilidade (PORTAR-PLACA §10).

## 5. Variantes recomendadas para a biblioteca de ligações

Cada variante tem a lista de verificações obrigatórias. "NBR" significa NBR 8800:2008.

| Código sugerido | Uso | Geometria | Verificações |
|---|---|---|---|
| PB-R2 | Rotulada leve (galpão, mezanino) | 2 chumbadores entre mesas, sobre a alma (padrão CBCA PBR) | 6.6.5 concreto; chapa com ℓ = max(m, n, λn'); corte por atrito (10% de Nd, CBCA) ou chumbador; montagem (momento de prumo, OSHA) com aviso |
| PB-R4 | Rotulada padrão | 4 chumbadores fora do contorno do pilar | Como PB-R2, mais o corte nos chumbadores com regra de furo alargado (2 efetivos ou arruela soldada) |
| PB-E4 | Engastada, pequena excentricidade | 4 chumbadores, 2 por lado, sem enrijecedor | §1.3/§1.4 com bloco de compressão; tração e efeito alavanca (§2); 6.3.3.4; concreto do chumbador (§1.6) |
| PB-E4R / PB-E6R | Engastada, grande excentricidade | 2 ou 3 chumbadores por lado afastados do pilar, **com enrijecedores** soldados ao pilar e à placa | Chapa como placa apoiada em 3 lados entre enrijecedores; enrijecedor à flexão e flambagem local (b/t); soldas pela NBR 6.2.5; chumbador com pré-carga se houver equipamento rotativo (fadiga, §2) |
| PB-E-CAD | Engastada em pilar alto, ou ponte rolante | Cadeira com chapa superior e chumbadores longos (*anchor chair*) | Chapa superior à flexão e enrijecedores da cadeira; fora do primeiro lote |
| +SL | Opção de corte para qualquer variante | Barra de cisalhamento (perfil ou chapa) soldada sob a placa | Pressão do concreto à frente da barra; flexão da barra; cone de corte (ACI 349); solda; nicho e graute |
| +ARR | Opção | Arruela-chapa soldada sobre furo alargado | Solda da arruela; todos os chumbadores passam a contar no corte |
| Chumbador | Atributo de todas as variantes | (a) pré-concretado com porca e chapa de ancoragem, ASTM F1554 Gr36 ou SAE 1020; (b) gancho, com aviso de desaconselhado; (c) pós-instalado de catálogo, o legado atual | (a) e (b) por ACI 318 cap. 17 com a norma declarada, ou armadura de ancoragem pela NBR 6118; (c) mantém o método legado |

Ordem sugerida de implementação: PB-R4 e PB-R2 primeiro, que têm teste pronto em §3.1 e §3.2. Depois PB-E4 com bloco de compressão, que resolve o risco 4.1.1. Depois +SL. Por último as variantes enrijecidas.

## 6. Pendências

- Obter o AISC DG1 verdadeiro e substituir todas as marcas [DG1-memória], com páginas e exemplos.
- Decidir o método da chapa: 1,25 W (CBCA) ou Z (Pfeil e DG1).
- Definir a norma do concreto do chumbador: ACI 318 cap. 17 ou EN 1992-4.
- Definir no contrato de `placa_base.py` se N/V/M chegam majorados (ELU) ou de serviço, porque o risco 4.1.4 depende disso.
