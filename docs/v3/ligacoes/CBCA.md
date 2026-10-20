# Catálogo de ligações do manual "Ligações para Estruturas de Aço" (Gerdau/CBCA)

Data: 20/10/2026. Pesquisa e extração. Este documento não altera código.

## 0. A fonte

- Arquivo: `referencias/livros/Manual Ligações para Estruturas de Aço.pdf`. Tem 378 páginas, e a página do PDF coincide com a página impressa.
- Obra: *Ligações para Estruturas de Aço — Guia prático para estruturas com perfis laminados*. Autores: Oswaldo T. Baião Filho e Antonio C. Viana Silva, com consultoria de Gilson Queiroz. 7ª ed., 2018, publicada pela Gerdau. É o manual que circula como "manual do CBCA". O capítulo 12 (estruturas mistas) se apoia no *Manual de Construção em Aço — Estruturas Mistas* do CBCA (2012).
- Natureza: é um **manual de ligações padronizadas por tabela**. Para cada tipo há um texto de considerações, a lista de verificações, o formulário e páginas de tabelas com as resistências de cálculo (Vd, Nd, Md) por perfil Gerdau.
- Os "exemplos de utilização" do manual são **consultas a tabela**: calcula-se o esforço de cálculo e compara-se com o valor tabelado. Não há memória de cálculo passo a passo. Por isso:
  - na §3, os testes de regressão usam os **valores tabelados**;
  - recalculei várias linhas de tabela com o formulário do manual. Cada valor recebe uma marca:
    - **[REPRODUZIDO]**: a minha conta bate com a tabela;
    - **[TABELADO]**: valor apenas lido da tabela, sem recálculo ou sem bater.
- Texto protegido por direito autoral. Aqui há apenas resumo, fórmulas (que são fatos técnicos) e números.

### 0.1 Convenções do manual (valem para todos os tipos)

| Item | Valor no manual | Observação para o motor |
|---|---|---|
| Unidades do formulário | kN e cm (tensões em kN/cm²) | — |
| Perfis | ASTM A572 Gr. 50: fu = 45 kN/cm². No formulário aparece fy = 34,5 kN/cm² | **As tabelas LMSS e LMPA só batem com fy = 35 kN/cm²** (ver §3). Deixar fy do perfil como parâmetro |
| Cantoneiras e chapas | ASTM A36: fy = 25 e fu = 40 kN/cm² | — |
| Parafusos | ASTM A325N tipo 1 (rosca no plano de corte), fub = 82,5 kN/cm². Para aço patinável, A325 tipo 3 | — |
| Eletrodo | E70XX, fw = 48,5 kN/cm². Para aço patinável, E7018W ou E7018G | — |
| Coeficientes | γa1 = 1,10 (escoamento) e γa2 = 1,35 (ruptura, parafusos e solda). Concreto: γc·γn = 1,4 × 1,4 | NBR 8800 tabela 3 e 6.6.5 |
| Ações | Exemplos com γg = 1,4 e γq = 1,5 | Só para montar Vd, Nd e Md dos exemplos |
| Carregamento | Só estático (2.5) | Sem fadiga |
| Elemento suporte | **Não é verificado** pelo manual (1.2). Todas as tabelas trazem a nota "verificar obrigatoriamente o elemento suporte" | O motor deve verificar o suporte à parte: alma do pilar, painel, enrijecedores |

### 0.2 Disposições construtivas gerais (manual 2.4)

- Diâmetros: 16 (5/8"), 19 (3/4"), 22 (7/8") e 25 mm (1").
- Furo padrão: d + 1,5 mm, ou seja, 18, 21, 24 e 27 mm. No cálculo de área líquida o manual usa (dh + 0,2 cm), conforme NBR 8800 5.2.4.
- Espaçamento padrão entre furos: 60 mm para 5/8" e 75 mm para os demais.
- Distância furo–borda padrão: 30 mm para 5/8" e 40 mm para os demais.
- Gabarito de cantoneira: 45 mm na L76×76×6,4 e 65 mm na L102×102×8,0.
- Recorte de viga: dimensões da interferência + 10 mm. Canto reentrante com raio mínimo de cerca de 12 mm (3.1.1).
- Não misturar aço patinável com aço carbono na mesma ligação (corrosão galvânica).
- Regras da NBR que o manual pressupõe e não repete. Conferir na norma antes de codificar:
  - espaçamento mínimo de 3 d, com absoluto 2,7 d (NBR 6.3.9);
  - borda mínima pela tabela 14 (6.3.10);
  - borda máxima de 12 t e 150 mm (6.3.11);
  - espaçamento máximo (6.3.12);
  - filete mínimo pela tabela 10 (6.2.6.2).

### 0.3 Tabelas de resistência de parafusos (manual 2.6, p. 15). [REPRODUZIDO], bons testes unitários

| Grandeza (A325, fub = 82,5) | 5/8" | 3/4" | 7/8" | 1" | Fórmula |
|---|---|---|---|---|---|
| Ab (cm²) | 1,98 | 2,85 | 3,88 | 5,07 | πd²/4 com d em polegadas |
| Ft,Rd (kN), tab. 1 | 90,8 | 130,6 | 177,8 | 232,4 | 0,75·Ab·fub/1,35 (6.3.3.1) |
| Fv,Rd (kN), um plano, rosca no plano, tab. 2 | 48,4 | 69,7 | 94,8 | 123,9 | 0,4·Ab·fub/1,35 (6.3.3.2) |
| Fc,Rd entre furos (kN), t = 1 cm, s = 3d, A36, tab. 3 | 105,3 | 128,5 | 151,7 | 174,9 | 1,2·(3d − dh)·t·fu/1,35 ≤ 2,4·d·t·fu/1,35 (6.3.3.3) |
| idem, A572 Gr. 50 | 118,5 | 144,6 | 170,7 | 196,8 | fu = 45 |
| Fc,Rd na borda (kN), t = 1 cm, e = 1,5d, A36, tab. 4 | 52,6 | 64,3 | 75,9 | 87,5 | 1,2·(1,5d − dh/2)·t·fu/1,35 |
| idem, A572 Gr. 50 | 59,2 | 72,3 | 85,4 | 98,4 | — |

Conferência: 5/8" e tab. 3, A36: lf = 3 × 1,5875 − 1,8 = 2,96 cm, e 1,2 × 2,96 × 40/1,35 = 105,3 kN. Na tab. 4: lf = 2,381 − 0,9 = 1,48 cm, o que dá 52,6 kN. As tabelas confirmam dh = d + 1,5 mm.

---

## 1. Catálogo por tipo

Notação do manual, anexo 13.2:

- Ap, Ab: área do parafuso; Np: número de parafusos na alma da viga; Nps: número de parafusos na chapa de extremidade;
- Lc: comprimento da cantoneira ou chapa; tc: espessura da cantoneira ou chapa; ba: aba da cantoneira; g: gabarito;
- H: altura da viga; Lh e Lv: comprimento e altura do recorte; s: espaçamento; e: distância do furo à borda vertical;
- ya: distância do furo superior ao topo da viga; lf: distância livre entre a borda do furo e a borda (do outro furo ou da peça);
- Wxs: módulo elástico do perfil com um recorte; W1: módulo elástico do perfil com dois recortes; Wxc: módulo elástico da chapa; Wef: módulo elástico líquido.

### 1.1 LCPP: ligação flexível com duas cantoneiras parafusadas nas duas abas (cap. 3, p. 17–44)

- **Aliases**: dupla cantoneira de alma, *double angle all-bolted*, "clip angle". Tabelas LCPP 20-A … 62-C. O sufixo -A, -B ou -C indica o grupo de bitola; os índices 0, 1 e 2 indicam sem recorte, um recorte ou dois recortes.
- **Membros**: viga–viga (alma da viga suporte) ou viga–pilar (mesa ou alma). A viga apoiada pode ter 0, 1 (mesa superior) ou 2 recortes.
- **Componentes**: 2 cantoneiras, um parafuso por fila na alma da viga em corte duplo e parafusos nas abas externas ligadas ao suporte. A face externa das cantoneiras fica a 10 mm da ponta da viga.
- **Parâmetros e padrões**:

  | Perfil | Parafuso | Cantoneira | Furação |
  |---|---|---|---|
  | W150, W200 | 5/8" | L76×76×6,4 | centrada; s = 60 e borda 30 mm |
  | W250 a W530 | 3/4" | L76×76×6,4 | 1º furo a 75 mm do topo; s = 75 e borda 40 mm |
  | W610 | 7/8" | L102×102×8,0 | idem |

  - Comprimento da cantoneira ≥ H/2.
  - Distância do furo superior ao topo ≤ distância do furo inferior à face inferior.
  - Recorte horizontal Lh,máx = 170 mm. Lv,máx de 20 mm (W150) ou 30 mm (demais).
  - Nd tabelado atua no centro da ligação e gera momento na viga apoiada.
- **Verificações, caso 1 (Nd = 0)**. Vd,máx é o menor dos valores abaixo (p. 23–26):
  1. Corte dos parafusos (NBR 6.3.3.2). Na viga, em corte duplo: Fv,Rd = 2·0,4·Ap·fub·Np/1,35.
  2. Pressão de contato nas cantoneiras (6.3.3.3):
     - sem rasgamento: 2·2,4·d·tc·fuc·Np/1,35;
     - com rasgamento: 2·1,2·lf·tc·fuc·Np/1,35.
  3. Pressão de contato na alma da viga: 2,4·d·tw·fu·Np/1,35 e 1,2·lf·tw·fu·Np/1,35.
  4. Cisalhamento das cantoneiras (6.5.5):
     - escoamento: 2·0,6·Lc·tc·fyc/1,1;
     - ruptura: 2·0,6·[Lc − Np(dh + 0,2)]·tc·fuc/1,35.
  5. Cisalhamento da alma da viga (6.5.5), com H' = H, H − Lv ou H − 2Lv (0, 1 ou 2 recortes):
     - escoamento: 0,6·H'·tw·fy/1,1;
     - ruptura: 0,6·[H' − Np(dh + 0,2)]·tw·fu/1,35.
  6. Colapso por rasgamento das cantoneiras (6.5.6). O manual assume 0,6·fu·Anv > fu·Ant:
     Fr,Rdc = 2(0,6·Anvc·fuc + Antc·fuc)/1,35 ≤ 2(0,6·Agvc·fyc + Antc·fuc)/1,35, com
     - Agvc = [(Np − 1)s + e]·tc;
     - Anvc = Agvc − (Np − 0,5)(dh + 0,2)·tc;
     - Agtc = (ba − g)·tc;
     - Antc = Agtc − 0,5(dh + 0,2)·tc.
  7. Colapso por rasgamento da alma da viga recortada (6.5.6). O Cts não aparece na fórmula (vale 1). Termos:
     - Agvw = (Lc − 2e + ya − Lv)·tw;
     - Anvw = Agvw − (Np − 0,5)(dh + 0,2)·tw;
     - Agtw = (g − 1,0)·tw;
     - Antw = Agtw − 0,5(dh + 0,2)·tw.
  8. Início de escoamento por flexão no perfil recortado. A excentricidade é Lh + 1,0 cm:
     - 1 recorte: FR,d = fy·Wxs/[(Lh + 1)·1,1];
     - 2 recortes: FR,d = fy·W1/[(Lh + 1)·1,1].
  9. Flambagem local da alma recortada (AISC 13ª ed., p. 9-7a). Não existe na NBR:
     - 2 recortes: φFbc = 35060·[tw²/(Lh·h0)]·[3,5 − 7,5(Lv/H)], com h0 = H − 2Lv, e FR,d = φFbc·W1/(Lh + 1);
     - 1 recorte, com Lh ≤ h0: φFbc = 16243·(tw/h0)²·f·k, com h0 = H − Lv, f = 2(Lh/H) e k = 2,2(h0/Lh)^1,65. Então FR,d = φFbc·Wxs/(Lh + 1).
     - Recortes tabelados: 80 mm (W150–W310); 80 e 130 mm (W360–W530); 80, 130 e 170 mm (W610).
- **Verificações, caso 2 (Nd com 0,5 Vd)**. Plastificação da cantoneira por interação de Drucker (p. 26–27):
  - Vz = Q/(2Lc), com Q = 50% do Vd do caso 1;
  - t'c = Vz/(0,54·fyc);
  - (V0)x = 0,54·fyc·(tc − t'c);
  - (M0)z = 0,225·fyc·(tc − t'c)(tc + t'c);
  - (M)z = (V)x·(g − tc − R)/2, com R = raio interno da cantoneira;
  - resolver (M)z/(M0)z + [(V)x/(V0)x]⁴ = 1 para (V)x e fazer Nd = 2·Lc·(V)x.
  - Observação: aqui 0,54 e 0,225 já embutem γ. O manual não explica a origem; é calibração.
- **Detalhamento**: §0.2 e as regras acima. As vigas devem ser verificadas à flambagem lateral considerando o recorte (nota 4 das tabelas).

### 1.2 LCSP: duas cantoneiras soldadas na viga apoiada e parafusadas no suporte (cap. 3, p. 45–62)

- **Aliases**: dupla cantoneira soldada–parafusada, *double angle welded–bolted*. Tabelas LCSP 20 … 62.
- **Membros**: os mesmos da LCPP.
- **Componentes**: 2 cantoneiras soldadas em "C" na alma da viga (filete de 5 mm nas tabelas) e parafusadas no suporte, com gabarito de 45 mm na aba externa.
- **Parâmetros**: os mesmos da LCPP, com Lv,máx de 30 mm e cantoneira L76×76×6,4×(n·75 + 80).
- **Verificações**: a lista da LCPP (corte dos parafusos, contato, cisalhamento e rasgamento das cantoneiras e da alma, flexão e flambagem do recorte, caso 2 de Drucker) mais a solda (p. 26):
  - Método do centro instantâneo, AISC LRFD 13ª ed. tabela 8-8: não existe na NBR. Coeficiente c tirado da tabela AISC.
  - Ruptura da solda: Fw,Rd = c·(b/0,15876)·(Lc/2,54)·4,54·2·0,75. Aqui b é a perna em cm; b/0,15876 dá o número de dezesseisavos de polegada e Lc/2,54 dá o comprimento em polegadas.
  - Escoamento do metal base junto à solda: a mesma fórmula com fator adicional 0,8748.
  - Implementação sugerida: usar a NBR 6.2.5 (tabela 8) com solda excêntrica pelo método elástico ou do centro instantâneo, e calibrar contra os valores tabelados.

### 1.3 LCHE: ligação flexível com chapa de extremidade (cap. 4, p. 63–92)

- **Aliases**: *end plate* flexível, chapa de topo flexível, *shear end plate*. Tabelas LCHE 20-A … 50-C.
- **Membros**: viga apoiada na mesa ou na alma do pilar. A viga não tem recorte.
- **Componentes**:
  - chapa de extremidade com a altura da alma da viga, largura 200 mm e gabarito horizontal gch = 120 mm;
  - espessura de 6,3 mm nas tabelas (a pequena espessura dá a flexibilidade);
  - solda de filete dupla na alma (5 mm nas tabelas);
  - 2 colunas de parafusos.
  - A chapa trava lateralmente a mesa comprimida no apoio.
- **Parâmetros**:
  - Nps: total de parafusos (4 a 10, isto é, n + 1 fileiras × 2);
  - s = 60 ou 75 mm; e (borda vertical) = 45 mm nas tabelas W150;
  - tch, fyc e fuc da chapa; b (perna da solda).
- **Verificações** (p. 66–67):
  1. Corte dos parafusos: 0,4·Ab·fub·Nps/1,35 (6.3.3.2).
  2. Contato na chapa (6.3.3.3):
     - sem rasgamento: 2,4·db·tch·fuc·Nps/1,35;
     - entre furos: 1,2(s − dh)·tch·fuc·Nps/1,35;
     - na borda: 1,2(e − dh/2)·tch·fuc·Nps/1,35.
  3. Cisalhamento da chapa (6.5.5), com Lfch = (Nps/2 − 1)s + 2e:
     - escoamento: VRd = 2·0,6·fyc·Lfch·tch/1,1;
     - ruptura: VRd = 2·0,6·fuc·{Lfch·tch − (Nps/2)(dh + 0,2)·tch}/1,35.
  4. Cisalhamento da alma da viga (6.5.5a): VRd = 0,6·fy·Lfch·tw/1,1.
  5. Flexão da chapa: FRd = (4·tch·Lfch²/(6·gch))·fyc/1,1.
  6. Nd com Vdm = Vd/2 (Drucker):
     - Vz = Vdm/(2Lfch);
     - tlich = Vz/(0,6·fyc/1,1);
     - (V0)x = 0,6·fyc(tch − tlich)/1,1;
     - (M0)z = 0,25(tch² − tlich²)·fyc/1,1;
     - (M)z = [(V)x/2]·[(gch − tw)/2];
     - resolver (M)z/(M0)z + [(V)x/(V0)x]⁴ = 1 e fazer Nd = 2·Lfch·(V)x.
  7. Solda (6.2.5.1). Ruptura: √(Nd² + Vd²) ≤ 41,14·Lfch·b/1,35. O fator 41,14 vem de 0,6·48,5·0,707·2. Escoamento do metal base: √(Nd² + Vd²) ≤ 30·Lfch·b/1,1, com 30 = 0,6·25·2.
- **Detalhamento**: chapa da altura da alma; sem recorte.

### 1.4 LCHS: ligação flexível com chapa simples (cap. 5, p. 93–110)

- **Aliases**: *single plate*, *shear tab*, *fin plate*, chapa de alma. Tabelas LCHS 20-A …
- **Membros**: viga–pilar (chapa soldada no suporte) e também viga–viga.
- **Componentes**:
  - chapa vertical soldada no suporte com filete duplo (5 mm);
  - uma fila vertical de parafusos na alma da viga;
  - folga de 10 mm entre a viga e o suporte;
  - linha de parafusos a ec = 75 mm da solda (AISC); borda horizontal de 40 mm.
  - A excentricidade é considerada; a semi-soma das espessuras é desprezada.
- **Limitações** (AISC 13ª ed.):
  - altura da chapa ≥ 2·ec, sempre que possível;
  - tc ≥ 6,3 mm para 2 a 5 parafusos e tc ≥ 8 mm para 6 parafusos;
  - tc ≤ db/2 + 2 mm;
  - tc ≥ máx(6,3 mm; Lc/64), contra a flambagem.
- **Verificações, caso 1** (p. 96–99). O coeficiente c do grupo excêntrico vem do AISC 13ª ed., tabela 7-7, p. 7-32:
  - para 3/4", 7/8" e 1": Np = 2, 3, 4, 5 e 6 dão c = 0,88, 1,75, 2,81, 3,90 e 4,98;
  - para 5/8": c = 0,74.

  1. Corte dos parafusos: Fv,Rd = 0,40·Ab·fub·c/1,35.
  2. Contato na chapa: 2,4·db·tc·fuc·c/1,35 e 1,2·lf·tc·fuc·c/1,35.
  3. Contato na alma da viga: as mesmas fórmulas com tw e fuw.
  4. Rasgamento da chapa (6.5.6), sem a excentricidade. Termos:
     - Agvc = [(Np − 1)s + e]·tc;
     - Anvc = Agvc − (Np − 0,5)(dh + 0,2)·tc;
     - Agtc = (b − g)·tc;
     - Antc = Agtc − 0,5(dh + 0,2)·tc.
  5. Rasgamento da alma (6.5.6). Termos:
     - Agvw = (Np − 1)s·tw;
     - Anvw = Agvw − (Np − 1)(dh + 0,2)·tw;
     - Agtw = (g − 1,0)·2tw;
     - Antw = Agtw − (dh + 0,2)·tw.
  6. Cisalhamento da chapa:
     - escoamento: 0,6·Lc·tc·fyc/1,1;
     - ruptura: 0,6·fuc·[Lc − Np(dh + 0,2)]·tc/1,35.
  7. Cisalhamento da alma da viga:
     - escoamento: 0,6·H·tw·fyw/1,1;
     - ruptura: 0,6·fu·[H − Np(dh + 0,2)]·tw/1,35.
  8. Tensão normal na chapa por momento Vd·ec:
     - início de escoamento: Fn,Rdc ≤ (fyc·Wxc/1,1)/ec;
     - ruptura: Fn,Rdc ≤ (fuc·Wef/1,35)/ec.
  9. Metal base junto à solda (6.2.5.1, tabela 8): Fn,Rdc ≤ c·(b/0,159)·(Lc/2,54)·4,54·0,87·0,75. Aqui c é da tabela 8-4 do AISC.
  10. Flambagem local da chapa: limites de tc acima.
- **Verificações, caso 2**: Vd = 0,5·Vdmax e Nd,max adotado = 25% de Vdmax. Verificações da chapa:
  - escoamento: Fn ≤ (fyc/1,1 − Vd·ec/Wxc)·Lc·tc;
  - ruptura: Fn ≤ (fuc/1,35 − Vd·ec/Wxc)·[Lc − Np(dh + 0,2)]·tc;
  - solda com 50% da resistência para V e 50% para N:
    - Fv,Sd ≤ c(b/0,159)(Lc/2,54)·4,54·0,87·0,5;
    - Ft,Rd ≤ 0,9·0,6·2·fyc·Lc·b·0,5.

### 1.5 LMPA: ligação rígida com chapa de cabeça parafusada assimétrica (cap. 6, p. 111–140)

- **Aliases**: *extended end plate* (estendida só no lado tracionado), chapa de topo rígida, ligação a momento parafusada.
- **Membros**: viga–pilar, rígida.
- **Componentes**:
  - chapa de cabeça estendida acima da mesa tracionada;
  - 4 parafusos acima e abaixo da mesa tracionada e 2 junto à mesa comprimida, 6 no total;
  - mesas com solda S1 (filete ou penetração total) e alma com filete S2.
  - O momento sempre traciona os parafusos superiores.
  - **Se o momento puder inverter**, repetir o detalhe superior embaixo e tornar a ligação simétrica (8 parafusos).
- **Parâmetros e padrões**:
  - distância da face da mesa ao furo: 30 mm (5/8" e 3/4"), 35 mm (7/8") e 40 mm (1");
  - largura da chapa bp ≈ bf + 25 mm;
  - tc tabelado (16 a 31,5 mm); gabarito; S1 e S2.
- **Casos tabelados** (p. 114):
  1. Md,max com Nd = 0 e Vd;
  2. 0,80·Md,max com Nd = 2·(0,2·Md,max)/(H − tf) e Vd;
  3. 0,80·Md,max com Nd = 0 e Vd (a 3ª linha da tabela traz o Vd maior).
- **Verificações** (p. 114–115):
  1. Momento resistente do perfil (NBR 5.4.2): MRd ≤ Zx·fy/1,1 e ≤ 1,5·Wx·fy/1,1.
  2. Tração nos parafusos (6.3.3.1): MRd ≤ (Nps/1,5)·T·(H − tf), com T = 0,75·Ab·fub/1,35.
  3. Flexão da chapa de cabeça (AISC Steel Design Guide 4): MRd ≤ (H − tf)·fy·bp·tc²/(αm·pe·1,1), com
     - αm = Ca·Cb·(Af/Aw)^(1/3)·(pe/d)^(1/4), Ca = 1,45 e Cb = (bf/bp)^(1/2);
     - pe = pf − d/4 − wt, onde pf é a distância do furo à face da mesa e wt a garganta ou o reforço da solda.
  4. Solda das mesas (6.2.5.1, tabela 8): MRd ≤ 2·0,6·b·fy·bf·(H − tf)/1,1.
  5. Corte dos parafusos: VRd ≤ 0,4·fub·Ab·Nps/1,35.
  6. Corte com tração (6.3.3.4): VRd ≤ {√[1 − ((Nd/(Np/1,5))/Ft,Rd)²]·Fv,Rd + 0,33·Nps·Fv,Rd}.
  7. Solda da alma: VRd ≤ 2·0,6·fy·b·ℓ/1,1, com ℓ ≤ (H − 2tf)/2 e ℓ ≤ H − 2tf − e2 − 2dh.
- **Não verificado pelo manual** (o motor precisa cobrir): alma do pilar, painel de alma, enrijecedores do pilar e flexão da mesa do pilar.

### 1.6 LMSS: ligação rígida soldada simétrica (cap. 7, p. 141–154)

- **Aliases**: mesas soldadas e alma parafusada, *welded flange – bolted web*, WFBW.
- **Membros**: viga–pilar, rígida.
- **Componentes**:
  - mesas da viga soldadas ao pilar com penetração total (chanfro de 30°, abertura de 10 mm, mata-junta e furo de acesso R = 15 mm);
  - chapa simples na alma, como a LCHS, parafusada e soldada ao pilar com filete duplo.
  - A chapa posiciona a viga na montagem.
- **Parâmetros**: os da LCHS para a alma (ec = 75 mm, borda 40 mm, s = 75 mm, chapa de 6,3 a 12,5 mm).
- **Casos**:
  1. Nd = 0, Md e Vd;
  2. Md = 0,80·Md,max e Nd = 2·0,20·Md,max/(H − tf).
- **Verificações** (p. 144):
  1. Momento do perfil (NBR 5.4.2): Md ≤ Zx·fy/1,1 e ≤ 1,50·W·fy/1,1.
  2. Solda das mesas: com penetração total, a verificação é dispensada (AISC 13ª ed., p. 12-7).
  3. Alma: as verificações da LCHS (§1.4) para Vd.
- **Não verificado**: pilar (alma, painel, enrijecedores de continuidade).

### 1.7 LT: resistência do perfil "T" laminado à tração e compressão (cap. 8, p. 155–181)

- **Aliases**: *tee stub*, T recortado de W, T-stub em tração. Serve também para pendurais.
- **Componentes**:
  - "T" cortado de um W no centro da alma (série TC, preferida, só de W com d ≥ 310 mm) ou fora do centro (série TM);
  - mesa do T parafusada ao suporte (Np2 parafusos) e alma do T parafusada na mesa da viga (Np1 parafusos);
  - comprimento Lt de 140, 170, 200 ou 230 mm, limitado a bf − 10 ≤ Lt ≤ bf + 40;
  - não se usa T de perfil com bf < 130 mm.
- **Parâmetros**: perfil de origem; Lt; diâmetro (3/4"); X, Y, e, s; gabarito G na mesa do T; Ht e A.
- **Verificações** (p. 158–161):
  1. Flexão da mesa do T com efeito alavanca (NBR 6.3.5): FR,nf ≤ (δ·MR,d + MR,d)·Np2/b', com
     - MR,d = p·tf²·fyt/4,44;
     - a = (bft − G)/2;
     - b1 = (bft − twt)/2 − a;
     - b' = b1 − 0,5d e a' = a + 0,5d;
     - d' = db + 0,2;
     - p = mín(p1 = y; p2 = 2(b1 + 0,5d); p3 = y/2 + x; p4 = b1 + 0,5d + x);
     - δ = (p − d')(fu/1,35)/[p·(fy/1,1)] ≤ 1.
  2. Parafusos da alma do T: corte 0,4·Ab·fub·Np1/1,35; contato 1,2·lf·twt·fut·Np1/1,35 e 2,4·d·twt·fut·Np1/1,35.
  3. Parafusos tracionados com alavanca: FR,npt = (FR,nt·a' + MR,d)·Np2/(a' + b'), com FR,nt = 0,67·0,75·Ab·fub/1,35.
  4. Escoamento da alma do T (6.5.3a): FR,ny = fyt·Lt·twt/1,1.
  5. Ruptura da alma do T (6.5.3b): FR,nu = fut·[Lt − 2(db + 0,2)]·twt/1,35.
  6. Rasgamento da alma do T (6.5.6), em dois blocos nas pontas ou num bloco central: FR ≤ (0,6·Anv·fu + fu·Ant)/1,35 e ≤ (0,6·Agv·fy + Ant·fu)/1,35.
- **Regras de projeto**:
  - Td = menor entre FR,ny e FR,nf. Para garantir ductilidade, Td deve ser menor que FR,npt, FR,npv, FR,nu e FR,nb.
  - Cd = menor entre FR,npv e FR,ny.
  - Mesa da viga: em tração, FR,nvt = mín(0,9·bf·tf·fy; rasgamento da mesa); em compressão, FR,nvc = escoamento, sem flambagem local.

### 1.8 LMTS: ligação rígida simétrica com dois perfis "T" (cap. 8, p. 182–186)

- **Aliases**: *split-tee moment connection*, ligação com T-stubs.
- **Membros**: viga–pilar, rígida.
- **Componentes**:
  - 2 perfis T (de LT), um em cada mesa da viga;
  - cortante resistido por 2 cantoneiras L102×102×8,0 (gabarito 65 mm) na alma, verificadas como a LCPP (3.2.1).
- **Verificações**:
  - Md/d + Nd/2 ≤ Td e Md/d − Nd/2 ≤ Cd, com d = altura do perfil da viga;
  - Vd ≤ V tabelado (alma, como na LCPP);
  - Td e Cd vêm da tabela LT.

### 1.9 EP1 / EP2: emendas de pilares (cap. 9, p. 187–200)

- **Aliases**: emenda de pilar com chapa de topo; *column splice with butt plate*.
- **Membros**: pilar–pilar. EP1: mesmo h entre faces internas das mesas. EP2: alturas nominais diferentes até 50 mm.
- **Componentes**:
  - chapa de topo: 12,5 mm na EP1 da tabela; 37,5 mm nos W150 e W200; 50 mm nos demais;
  - soldas de penetração total sob esforço (S1), ou filete onde a tabela indica;
  - cantoneira auxiliar L102×102×9,5 com 4 parafusos de 3/4", gabarito de 65 mm, **só para posicionar**: não garante a estabilidade na montagem.
- **Limitações**: bfi ≥ bfs e tfi ≥ tfs (perfil inferior ≥ superior). Diferença de altura > 50 mm segue o critério AISC 13ª ed., p. 14-40 e 14-41.
- **Verificações**: nenhuma no manual. O detalhe é prescritivo e as soldas de penetração total transmitem a resistência do perfil.
- **Exemplos** (p. 190), sem número:
  - W310×117 inferior com HP310×79 superior: EP1;
  - W410×75 inferior com W360×32,9 superior: diferença de 50 mm, EP2 com chapa de 50 mm.

### 1.10 PBR: base rotulada (cap. 10, p. 201–232)

- **Aliases**: placa de base rotulada, *pinned base plate*.
- **Membros**: pilar–bloco de concreto. Transmite compressão e cortante; a cortante resistente é 10% de Nd.
- **Componentes**:
  - placa A36 Apb × Bpb × tpb, com Apb na direção de d;
  - 2 chumbadores A36 entre as mesas, a 150 mm um do outro, com porca de nivelamento opcional;
  - soldas S1 e S2: filete ou penetração total. Almas ou mesas de 16 mm ou mais são soldadas com solda K;
  - bloco com dimensões mínimas Abc × Bbc; grauteamento de 50 mm.
  - Chumbadores: 1" para tpb ≤ 31,5 mm e 1½" para tpb > 31,5 mm.
- **Verificações** (p. 204):
  1. fc = Nd/(Apb·Bpb).
  2. Concreto (NBR 6.6.5): fc ≤ fck·√(A2/A1)/(1,4·1,4) ≤ fck.
  3. Flexão da placa: tpb ≥ √[6·Mmax/(1,25·fy/1,1)]. O fator 1,25 corresponde a Z = 1,25W (limite da NBR 5.4.2.2 para chapa).
- **Fórmula que reproduz as tabelas** (dedução minha, conferida em 4 linhas):
  - Mmax = fc·ℓ²/2 por unidade de largura, com ℓ = máx(m, n), m = (Apb − 0,95·d)/2 e n = (Bpb − 0,8·bf)/2. O manual não escreve m e n, mas as tabelas batem.
  - Portanto Nd,max(placa) = Apb·Bpb·tpb²·(1,25·fy/1,1)/(3ℓ²).
- **Exemplo** (p. 203): W310×52,0, Nd = 500 × 1,4 + 400 × 1,5 = 1300 kN. A tabela (p. 219) dá 1833 kN, então OK.

### 1.11 PBE: base engastada (cap. 11, p. 233–344)

- **Aliases**: placa de base engastada, *fixed base plate*, base com momento.
- **Membros**: pilar–bloco. Compressão sempre. Cortante resistente de 10% de Nd.
- **Componentes**:
  - placa A36 maior que o perfil;
  - 4 chumbadores A36, de 3/4" a 1½", fora das mesas, a distância ≤ bf sempre que possível;
  - solda de filete ou K;
  - bloco mínimo indicado na figura; graute de 50 mm.
- **Tabelas**: 21 pares (Nd, Md,max), de 0 até a resistência do pilar à compressão com Lfl = 2800 mm. Para Nd intermediário, usar o menor Md,max dos dois vizinhos.
- **Distribuição de pressão** (p. 236), com e = Md/Nd:
  - Caso 1, e < Apb/6: fc = (Nd/(Apb·Bpb))·(Apb + 6e)/Apb, x = Apb e Td = 0.
  - Caso 2, Apb/6 ≤ e < Apb/3: x = 3(Apb/2 − e), Td = 0 e fc = 4Nd/[3(Apb − 2e)·Bpb].
  - Caso 3, e > Apb/3: x = Apb/2, fc = (4Nd/(Apb·Bpb))·(e + a)/(Apb/3 + a) e Td = fc·Apb·Bpb/4 − Nd. Aqui a é a distância entre os pontos de aplicação de Nd e Td.
  - Caso 4, Nd = 0: x = Apb/2, fc = (4Md/(Apb·Bpb))·1/(a + Apb/3) e Td = fc·Apb·Bpb/4.
- **Verificações** (p. 237):
  1. Concreto (6.6.5): fc ≤ fck·√(A2/A1)/1,96 ≤ fck.
  2. Placa: tpb ≥ √[6·Mmax/(1,25·fy/1,1)].
  3. Chumbador (NBR 5.2):
     - dc ≥ √[4Td/((nc·π·fych)/1,1)], com fych = 25;
     - dc ≥ √[4Td/(0,75·nc·π·fuch/1,35)], com fuch = 40;
     - nc = chumbadores do lado tracionado.
  4. Embutimento: Lc ≥ √[4Td/(nc·√fck)] ≥ 12·dc. Borda do bloco ≥ 5·dc (AISC Design Guide 1).
- **Fórmula que reproduz a tabela** (minha, conferida em 2 linhas):
  - a placa é tratada como balanço m = (Apb − 0,95·d)/2 sob pressão trapezoidal;
  - Mmax = m²·(2·fmax + fm)/6, com fm = pressão na seção da face da mesa;
  - igualar a t²·(1,25·fy/1,1)/6.
- **Exemplo** (p. 236): W250×73,0; Nd = 800 × 1,4 + 520 × 1,5 = 1900 kN; Md = 15 × 1,4 + 16 × 1,5 = 45 kNm.
  - 1900 kN fica entre 1843 kN (Md,max = 62,0 kNm) e 1966 kN (49,6 kNm), na tabela da p. 266.
  - 45 < 49,6, então OK.

### 1.12 LMMI: ligação mista semirrígida viga–suporte (cap. 12.1, p. 347–354)

- **Aliases**: ligação mista de momento negativo, *composite semi-rigid connection*. Segue o Anexo R da NBR 8800.
- **Membros**: viga mista intermediária ligada a viga ou pilar suporte, com laje dos dois lados. Simétrica; não transmite momento ao suporte; fora do sistema de contraventamento.
- **Componentes**:
  - armadura longitudinal CA-50 Ø12,5 na laje (4, 6 ou 8 barras), com cobrimento de 35 mm ao eixo e abaixo do topo dos studs;
  - armadura transversal Ø8;
  - studs Ø19 (nc na região de momento negativo, 1 por nervura), com QRd = 70,7 kN;
  - cantoneira de assento L152×102 (×10 ou ×12,5) parafusada na mesa inferior, com parafusos sem protensão;
  - 2 cantoneiras de alma L102×102×6,4. Elas não entram na resistência, só no item O.2.4.2.
- **Limites de uso**:
  - largura do suporte entre 200 e 314 mm;
  - tramo ≤ 1,5 × o adjacente e tramo externo ≤ 1,2 × o adjacente; Nd = 0;
  - laje com fôrma de 75 mm e nervuras perpendiculares; vão da laje de 1,9 a 4,24 m; concreto sobre a fôrma de 65 a 80 mm;
  - fck = 20 MPa; grau de interação ≥ 0,6 no momento positivo;
  - bef = (L1 + L2)/16.
- **Componentes e fórmulas** (Anexo R da NBR):
  1. Armadura (R.2.3):
     - ks = 2·Asl·Es/ha e Fs,Rd = fys·Asl/1,15;
     - capacidade de deformação Δus = L·εsmu, com εsmu = εsy − βt·Δεsr + δ0(1 − σsr1/fys)(εsu − εsy);
     - βt = 0,4; Δεsr = fctm·kc/(ρ·Es); fctm = 0,3·fck^(2/3); ρ = Asl/Ac; kc = 1/(1 + tc/(2y0)) ≥ 0,7.
  2. Conectores (R.2.4):
     - kcs = n·kr/α, com kr = 100 kN/mm;
     - α = v − (v − 1)(d + y)/[ds(ξ + 1)];
     - v = [(ξ + 1)·n·kr·L1·ds²/(Ea·Ia)]^(1/2);
     - ξ = Ia/(ds²·As);
     - FcRd = n·QRd.
     - Capacidade de deformação: S(A) = 0,7·Qrk/kr; S(B) = 2·S(A)·Fs(B)/Fs(A); Fs(B) = fys·Asl; Fs(A) = kcs·S(A).
  3. Ligação da mesa inferior (R.2.5.2.3):
     - ki = nl/(1/kp1 + 1/kp2 + 1/kb), com nl = 2;
     - kp1 = 24·ks·kt1·db·fu1, com ks = S/(4db) + 0,375 ≤ 1,25 e kt1 = 1,5·tp1/dm ≤ 2,5;
     - dm = 16 mm, fu1 = 45 e fu2 = 46 kN/cm²;
     - kb = 16·fub·db²/dm.
     - Fi,Rd = mín(nb·Fb,Rd; 1,25·fyd·Afi; fL,yd·AL) ≥ Fs,Rd, com Fb,Rd ≤ 6.3.3.2 e 6.3.3.3. Δui = 3 mm.
  4. Ligação mista:
     - rigidez Si = (d + y)²/(1/ks + 1/kcs + 1/ki);
     - **MRd⁻ = Fs,Rd·(d + y)**;
     - θu = (Δus + Δui + s(B))/(d + y), que pode subir 10% em construção não escorada;
     - yLNP = (d + y)·Δui/(Δus + Δui + s(B)).
     - O critério do item R.4a pode ser trocado por MRd ≥ 0,30·βvm·MRd (O.2.3).

### 1.13 Chapa de extremidade para viga mista com laje alveolar em pilar de aço (cap. 12.2.1, p. 355–359)

- **Aliases**: *flexible end plate* com charneiras plásticas; ligação de viga de borda torcida.
- **Membros**: viga de perfil simples W610 ligada a pilar de aço. Tem torção na fase de construção.
- **Componentes**:
  - chapa de extremidade A36, um pouco mais alta que a viga, com filetes externos e internos nas mesas;
  - 10 parafusos A325 sem protensão (5 fileiras × 2, s = 90 mm), com 7/8" ou 1";
  - geometria fixa. **Não alterar fy nem as dimensões da chapa** (a capacidade de rotação depende delas).
- **Verificações** (p. 356–358):
  - **(a)** Força de tração nos parafusos superiores pelo mecanismo de charneiras da fig. 12.2.1:
    - a = 12 − b − d/2, c = (9 + d)/2, f = bch/2 − e1 + d/2 e g = bch/2 − tw/2 − b − f, onde d = diâmetro externo da porca;
    - linhas: l1 = f + g, l2 = a + c, l3 = f, l4 = c e l5 = √(a² + g²);
    - rotações: θ1 = θ3 = δ/a, θ2 = θ4 = δ/g e θ5 = δ·l5/(ag);
    - Fd·δ = 1,25·m·Σ(li·θi), com m = fy·tch²/4, fy = 25 e fator de ajuste 1,25.
  - **(b)** Corte com tração: (Ft,Sd/Ft,Rd)² + (Fv,Sd/Fv,Rd)² ≤ 1, com Ft,Sd = Fd.
  - **(c)** Contato: Fc,Rd = 1,2·lf·tch·40/1,35 ≤ 2,4·db·tch·40/1,35, com dfuro = db + 0,36 cm. lf vale 9 − dfuro entre furos e 9,3 − dfuro/2 na borda inferior.
  - **(d)** Flambagem da chapa: (bch/2)/tch ≤ 0,56·√(E/fy) = 15,84.
  - **(e)** Cisalhamento da chapa junto à alma e à mesa, com 60% da espessura: [(F1d)² + (Fy,Rd)²]^½ = (0,6·25/1,1)(0,6·tch).
  - **(f)** Ruptura: Fu,Rd = (0,6·40/1,35)(57,3 − 5·dfuro)·tch.
  - **(g)** Rasgamento: Fr,Rd = (0,6·fy·Agv + Cts·fu·Ant)/1,35, com Cts = 1.
  - **(h)** Solda da alma.
  - **(i)** Solda das mesas.
  - **(j)** e **(k)** Escoamento de alma e mesa da viga por von Mises, com o limite 34,5/1,1.
  - Máximas reações: Vd = mín(…) para Td = 0. Com Td ≠ 0, usar a excentricidade e = bf/2 − 3 cm e Td = e·Vd/100 (kNm).

### 1.14 Apoio em console de pilar de concreto, viga simples ou dupla (cap. 12.2.2 e 12.2.3, p. 360–370)

- **Aliases**: apoio de viga em consolo; *seated connection on concrete corbel*.
- **Membros**: viga de perfil simples W610, ou dupla 2W530 e 2W610, apoiada em consolo de concreto.
- **Componentes**:
  - placa de apoio t1 (37,5 a 44 mm) com furos padrão, que serve de gabarito;
  - 2 chumbadores A36 de 7/8" ou 1", com porca pesada, posicionados para não sofrer a rotação da viga;
  - enrijecedores verticais t2 (12,5 a 16 mm) soldados na viga;
  - furo na mesa 8 mm maior que o chumbador, com arruela especial soldada;
  - concreto com fck ≥ 30 MPa.
- **Verificações**:
  - **Fase de construção.** Pressão no concreto com área A1 = 6·(2c) e A2 = 11·2(c + 2,5): σRd = (fck/1,96)·√(A2/A1) ≤ fck.
  - Enrijecedor:
    - b/t ≤ 0,56·√(E/fy) = 15,8;
    - cisalhamento vertical: (h't2)(0,6·25/1,1), com h' = h − 3 cm;
    - cisalhamento horizontal.
  - Flexão da placa: m = σ·c²/2 ≤ (t1²/5)(25/1,1).
  - Soldas: filete 2·0,7·b·h'·(0,6·48,5/1,35). Na viga dupla, penetração parcial (S − 0,3)·h'·(…).
  - Td = Vd·(bf/2 − 3)/100. Ftd = 20 kN, por segurança.
  - **Fase final**: as mesmas verificações, com áreas maiores e Td = 0.

---

## 2. Regras de detalhamento consolidadas

| Regra | Valor no manual | Fonte |
|---|---|---|
| Furo | padrão, dh = d + 1,5 mm; área líquida com dh + 2 mm | 2.4.2; NBR 5.2.4 |
| Espaçamento | 60 mm (5/8") e 75 mm (demais), que dá ≥ 3d | 2.4.3; NBR 6.3.9 |
| Borda | 30 mm (5/8") e 40 mm (demais) | 2.4.4; NBR tab. 14 |
| Gabarito de cantoneira | 45 mm (L76) e 65 mm (L102) | 2.4.5 |
| Folga da viga | 10 mm até o suporte | 3.1.2 e 5.1.1 |
| Recorte | interferência + 10 mm; raio ≥ 12 mm; Lh ≤ 170 mm | 2.4.6 e 3.1.1 |
| Cantoneira de alma | Lc ≥ H/2 | 3.1.2 |
| Chapa simples | 6,3 ≤ tc ≤ db/2 + 2 mm; tc ≥ Lc/64; altura ≥ 2·ec | 5.1.2 |
| Chapa de cabeça | furo a 30/35/40 mm da mesa; bp ≈ bf + 25 mm | 6.1.1 |
| Perfil T | bf − 10 ≤ Lt ≤ bf + 40; TC só de W com d ≥ 310 e bf ≥ 130 mm | 8.1 |
| Emenda | bfi ≥ bfs e tfi ≥ tfs; Δh ≤ 50 mm na EP2 | 9.1.2 |
| Base | chumbador de 1" se tpb ≤ 31,5 mm, senão 1½"; embutimento ≥ 12dc; borda do bloco ≥ 5dc | 10.4 e 11.4 |
| Filete | 5 mm usual nas flexíveis; tabelas de 4 a 9 mm | tabelas |

---

## 3. Exemplos numéricos para regressão

Unidades: kN, kNm, mm. A página é a do PDF, igual à impressa. "Esperado" é o valor do manual. A tolerância sugerida é ±1 unidade do último dígito impresso, porque as tabelas são arredondadas para kN inteiro.

### 3.1 Exemplos de utilização (consulta a tabela)

| # | Tipo | Entrada | Resultado do manual | Pág. | Status |
|---|---|---|---|---|---|
| E1 | LCPP | W360×51,0 apoiada em W530×92,0 (bf = 209, tf = 15,6, tw = 10,2); CP = 80 e SC = 60 | Vd = 202. Recorte Lh = bf/2 + 10 − tw/2 − 10 = **99 mm**; Lv = tf + 10 = **26 mm**. Tabela com 3 × 3/4", 1 recorte: Lh,max = 130, Lv,max = 30, **Vd,max = 266 > 202** | 22 | Lh e Lv [REPRODUZIDO]; 266 [TABELADO] |
| E2 | LCSP | W360×39,0 em W360×39,0 (bf = 128, tf = 10,7, tw = 6,5); CP = 100 e SC = 70 | Vd = 245. **Lh = 61 mm**, **Lv = 21 mm**, 2 recortes. Tabela: Lh,max = 80, Lv,max = 30, **Vd,max = 301** | 22 | Lh e Lv [REPRODUZIDO]; 301 [TABELADO] |
| E3 | LCHE | W250×28,4 na mesa de pilar; Vd = 20 × 1,4 + 34 × 1,5 = 79 e Nd = 3 × 1,4 + 3,4 × 1,5 = 9,3 | chapa de 6,3 com 4 × 3/4": **Vd,tab = 93** e **Nd,tab = 22** | 65 | [TABELADO] |
| E4 | LCHS | W310; Vd = 30 × 1,4 + 40 × 1,5 = 102 | 2 × 3/4": **60 kN**, não atende. 3 × 3/4": **120 kN**, atende. Chapa de 6,3 com filete de 5 | 96 | ver §3.2 |
| E5 | LMPA | W460×89,0; Nd = 245 (100/70), Vd = 102 (30/40), Md = 290 (100/100) | tabela (p. 132): **Nd = 360, Vd = 240, Md = 321** (linha 0,8M). Chapa de 31,5 × 220 × 563, 6 × 1" e penetração total | 113–114 | ver §3.2 |
| E6 | LMPA com inversão | o mesmo E5 com ±Md | ligação simétrica: 8 × 1"; chapa de 31,5 × 220 × **623** | 114 | prescritivo |
| E7 | LMSS | W610×155,0; Nd = 0, Vd = 70 × 1,4 + 68 × 1,5 = 200, Md = 500 × 1,4 + 370 × 1,5 = 1255 | tabela: **Vd = 598, Md = 1511**. 6 × 1", chapa de 12,5 e filete de 10 na alma | 143 | Md [REPRODUZIDO com fy = 35]; Vd [TABELADO] |
| E8 | LT/LMTS | W310×44,5; Nd = 7 × 1,4 + 6,8 × 1,5 = 20, Md = 7 × 1,4 + 13,5 × 1,5 = 30, Vd = 35 × 1,4 + 34 × 1,5 = 100 | **Td = 30/0,313 + 20/2 = 105,85**; **Cd = 85,85**. Opções: TC-W460×68 com Lt = 170 (Td = 301, Cd = 372); Lt = 200 (312 e 480); TC-W530×85 com Lt = 170 (307 e 480). Cortante tabelada de **201**; cantoneiras 2L102×102×8 com 6 × 3/4" | 159 | Td e Cd [REPRODUZIDO]; tabelas [TABELADO] |
| E9 | PBR | W310×52,0; Nd = 500 × 1,4 + 400 × 1,5 = 1300; fck = 20 | **Nd,max = 1833** (placa de 450 × 300 × 31,5; bloco de 650 × 450) | 203 e 219 | [REPRODUZIDO] |
| E10 | PBE | W250×73,0; Nd = 1900, Md = 45; fck = 20 | entre (1843; 62,0) e (1966; 49,6), logo Md,max = 49,6 > 45 | 236 e 266 | [REPRODUZIDO] |
| E11 | Chapa de extremidade mista | W610×155,0; construção Vd = 160 e Td = 19,5; final Vd = 719 | tabela 12.2.1.5: construção **Vd = 379,7 e Td = ±50,6**; final **864,2**; **Fd = 55,1** por parafuso | 355 e 359 | [TABELADO]. Charneiras com d da porca de 4,1 a 4,8 cm dão Fd de 52 a 54 (falta conhecer o d usado) |
| E12 | Console, viga simples | W610×155,0; construção 160 kN e Td/Vd = 0,122 m; final 719 | tabela 12.2.2.5: construção **Vd = 218** e **Td/Vd = ±0,132 m**; final **976**; Fcd = Vd e Ftd = 20 | 360 e 363 | [TABELADO] |
| E13 | Console, viga dupla | 2W530×82,0; construção 154 kN e 0,157 m; final 681,3 | construção **Vd = 170** e **±0,177 m**; final **1090** | 365 e 369 | [TABELADO] |
| E14 | EP1/EP2 | W310×117 + HP310×79 e W410×75 + W360×32,9 | EP1; EP2 com chapa de 50 mm | 190 | qualitativo |

### 3.2 Linhas de tabela recalculadas (bons testes de unidade do motor)

| # | Tipo | Dados | Conta | Manual | Pág. |
|---|---|---|---|---|---|
| R1 | PBR, flexão da placa | W150×22,5 (d = 152, bf = 152); placa de 250 × 250 × 16; A36 | m = (25 − 0,95 × 15,2)/2 = 5,28; n = (25 − 0,8 × 15,2)/2 = **6,42** = ℓ; fc = t²(1,25 × 25/1,1)/(3ℓ²) = 0,588 kN/cm²; Nd = **367,7** | **367** | 205 |
| R2 | PBR | a mesma de R1, com t = 19 | Nd = **518,4** | **518** | 205 |
| R3 | PBR | W310×52,0 (d = 317, bf = 167); placa de 450 × 300 × 31,5 | m = 7,44 e n = **8,32**; fc = 1,358; Nd = **1833**. Concreto: A2/A1 = 2925/1350, limite de 1,50 kN/cm², não governa | **1833** | 219 |
| R4 | PBR | W310×52,0; placa de 350 × 250 × 19 | n = 5,82; Nd = **883** | **883** | 219 |
| R5 | PBE | W250×73,0 (d = 253); placa de 500 × 350 × **50**; A36 | e = 4960/1966 = 2,523 cm < 50/6 (caso 1); fmax = 1,4636; fmin = 0,7832; m = (50 − 0,95 × 25,3)/2 = 12,98; fm = 1,287; M = m²(2fmax + fm)/6 = **118,35 kNcm/cm** = t²·28,41/6 = 118,4 | **(1966; 49,6)** | 266 |
| R6 | PBE | idem, Nd = 1843 e Md = 62,0 | e = 3,364; fmax = 1,4783; fm = 1,2576; M = **118,35** | **(1843; 62,0)** | 266 |
| R7 | LMMI | W250×32,7 (d = 258); 4 Ø12,5 (Asl = 4,909 cm²); Y = 105 | Fs,Rd = 50 × 4,909/1,15 = 213,4; MRd = 213,4 × (25,8 + 10,5) = **77,47 kNm** | **77,49** | 352 |
| R8 | LMMI | idem, Y = 120 | 213,4 × 0,378 = **80,67** | **80,69** | 352 |
| R9 | LMMI | W250×38,5 (d = 262), Y = 105 | **78,33** | **78,35** | 352 |
| R10 | LMMI | W310×38,7 (d = 310); 6 Ø12,5; Y = 105 | 320,1 × 0,415 = **132,86** | **132,8** | 353 |
| R11 | LMSS, Md | W150×13,0 (Zx = 96,4 cm³) | Zx·fy/1,1 com **fy = 35** dá **30,7**; com fy = 34,5 daria 30,2 | **31** | 145 |
| R12 | LMSS, Md | W610×155,0 (Zx = 4749 cm³) | 4749 × 35/1,1 = **1511** | **1511** | 143 |
| R13 | LMSS, Nd caso 2 | W150×13,0 (H = 148, tf = 4,9); Md,max = 30,7 | Nd = 2 × 0,2 × 30,7/(0,148 − 0,0049) = **85,8**; Md = 0,8 × 31 = 24,8 | **86 e 25** | 145 |
| R14 | LMPA, Nd caso 2 | W150×13,0; Md,max = 27 | Nd = 0,4 × 27/0,1431 = **75,5**; 0,8 × 27 = 21,6 | **76 e 22** | 116 |
| R15 | LMPA, solda das mesas | W150×13,0 (bf = 100, tf = 4,9); S1 = 5 mm | 2 × 0,6 × 0,5 × fy × 10 × 14,31/1,1 com fy = 35 dá **27,3 kNm**; com 34,5 dá 26,9 | **27** | 116 |
| R16 | LMPA, solda das mesas | W150×18,0 (bf = 102, tf = 7,1, H = 153); S1 = 7 | com fy = 35 dá **39,9**; com 34,5 dá 39,3 | **40** | 116 |
| R17 | LMPA, tração nos parafusos | W460×89,0 (H = 463, tf = 17,7); 6 × 1" | (6/1,5)·T·(H − tf) com T = 0,75 × 4,909 × 82,5/1,35 = 225,0 (Ab com **d = 25 mm**) dá **400,6**. Com Ab = 5,07 daria 413,9 | **401** | 132 |
| R18 | LMPA, Nd caso 2 | W460×89,0; Md,max = 401 | Nd = 0,4 × 401/0,4453 = **360,2** | **360** | 132 |
| R19 | LMPA, tração | W460×74,0 (457 / 14,5) e W460×106,0 (469 / 20,6) | 4 × 225 × 0,4425 = **398,2**; 4 × 225 × 0,4484 = **403,6** | **398 e 404** | 132 |
| R20 | LCHS, corte dos parafusos | W310; 2 e 3 × 3/4"; c = 0,88 e 1,75 | 0,4 × 2,85 × 82,5 × c/1,35 = **61,3 e 121,9** | **60 e 120** | 96 |
| R21 | LCSP, rasgamento da cantoneira | W460 (n = 2: 3 × 3/4"); L76×76×6,4×230; s = 75; e = 40; g = 45 | Agv = 12,07; Anv = 8,41; Agt = 1,97; Ant = 1,24; o limite com Agv·fy dá **341,5** | **339** (0,7%) | 53 |

Leitura das reproduções:

- **fy do perfil = 35 kN/cm²** nas tabelas LMSS e LMPA (R11, R12, R15 e R16). Com 34,5 os valores ficam cerca de 1,5% abaixo. O texto do manual diz "A572 Gr. 50", mas as contas foram feitas com 350 MPa.
- **Parafuso de 1" com Ab de 25 mm métrico** (4,909 cm²) na LMPA (R17 a R19). A tabela 1 do próprio manual usa 5,07 cm². Para o teste, aceitar ~3% ou parametrizar Ab.
- R20 dá cerca de 2% acima da tabela, também compatível com área métrica de 3/4" ≈ 19 mm, ou com arredondamento.

### 3.3 Valores tabelados que não consegui reproduzir (não usar como teste duro antes de investigar)

| Tipo | Linha | Manual | Observação |
|---|---|---|---|
| LCPP | W150×13,0, 2 × 5/8", L76×6,4×110, sem recorte | Vd = **72** (Nd = 29 com Vd = 36) | 72,2 = 1,2 × (6 − 1,8) × 0,43 × 45/1,35, isto é, rasgamento entre furos na alma para **um** parafuso. Não bate com W150×18 (tabela 100; conta 97,4). Investigar se Np = n (número de espaços) no formulário |
| LCHE | W150×13, ×18 e ×24 (4 × 5/8", chapa de 6,3) | **97, 114 e 115** | nenhuma das 7 verificações do formulário isoladamente dá esses valores com Lfch = 15 cm |
| LCHS | W150/W200, 2 × 5/8" | **28** (Nd = 7 com 14) | corte com c = 0,74 dá 35,8; rasgamento na borda da chapa com c dá 26,5 |
| LT | TC-W310×44,5 | Cd = **216** e Td = 147…168 | Cd proporcional a twt (190/216 = 5,8/6,6), logo é verificação na alma do T; contato com lf ≈ 2,0 cm × 4 parafusos dá 216 |
| PBR | W150×29,8, placa de 250 × 250 × 25 | **857** | a fórmula R1 dá 909; o concreto dá 893 |
| Chapa de extremidade mista | Fd | 55,1 e 49,5 | depende do diâmetro externo da porca usado |

---

## 4. Lacunas para o motor (o que o manual não cobre)

- Elemento suporte: alma do pilar ao cortante, painel de alma, escoamento e enrugamento local da alma do pilar (NBR 5.7.3 e 5.7.4), flexão da mesa do pilar e necessidade de enrijecedor. O manual manda "verificar obrigatoriamente" e não verifica.
- Ligações de contraventamento (gusset, cantoneira tracionada, Whitmore) não existem no manual. Buscar em outra fonte.
- Emenda de viga parafusada com talas e emenda de pilar parafusada não existem. Só há emenda soldada com chapa de topo (EP).
- Fórmulas que vêm do AISC, sem equivalente direto na NBR:
  - flambagem do recorte;
  - coeficiente c de grupo excêntrico (tabelas 7-7, 8-4 e 8-8);
  - αm do Design Guide 4.
  - Precisam de tabela própria ou de método do centro instantâneo implementado.

---

## 5. Tabela-resumo

| Tipo | Categoria | Complexidade | Prioridade |
|---|---|---|---|
| PBR: base rotulada | base | baixa (concreto + flexão da placa; fórmula reproduzida) | **1** |
| PBE: base engastada | base | média (4 casos de pressão, chumbador, embutimento; reproduzido) | **1** |
| LCHS: chapa simples | viga–pilar/viga, flexível | média (grupo excêntrico c do AISC) | **1** |
| LCPP: dupla cantoneira parafusada | viga–viga/pilar, flexível | média-alta (recortes, flambagem do recorte, Drucker) | **2** |
| LCHE: chapa de extremidade flexível | viga–pilar, flexível | média (tabelas não reproduzidas) | **2** |
| LMPA: chapa de cabeça estendida | viga–pilar, rígida | alta (DG4 αm, tração + corte; reproduzido com fy = 35 e Ab métrica) | **2** |
| LMSS: mesas soldadas + alma com chapa | viga–pilar, rígida | baixa (Zx·fy + LCHS) | **2** |
| LCSP: dupla cantoneira soldada–parafusada | viga–viga/pilar, flexível | alta (solda excêntrica, AISC 8-8) | 3 |
| LT: perfil T em tração | componente (T-stub, pendural) | alta (efeito alavanca 6.3.5) | 3 |
| LMTS: ligação rígida com 2 T | viga–pilar, rígida | média (depende de LT e LCPP) | 3 |
| EP1/EP2: emenda de pilar com chapa de topo | emenda | baixa (só detalhamento) | 3 |
| LMMI: ligação mista semirrígida | viga mista–suporte | alta (Anexo R completo; MRd reproduzido) | 4 |
| Chapa de extremidade mista (12.2.1) | viga mista–pilar de aço | alta (charneiras plásticas) | 5 |
| Apoio em consolo (12.2.2 e 12.2.3) | viga–pilar de concreto | média | 5 |

Critério de prioridade: (1) é comum em galpão e edifício e tem número reproduzido para teste; (2) é comum, com parte das tabelas sem reprodução; (3) e acima são menos frequentes ou dependem de métodos AISC sem NBR equivalente.
