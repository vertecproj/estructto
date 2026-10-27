# W9-FLANGE-PERFIL — emenda flangeada de perfil aberto (I, H, W, HP, IPE, HE, U)

Data: 27/10/2026. Branch `wt9-fi`. Pedido: "falta a conexão de flange entre vigas e entre pilares — i, u, h, w, quadrado ou
retangular". Esta frente faz os perfis abertos; metalon e tubo redondo são da frente paralela (`ligacoes_tubos.py`, W9-FT).
Arquivo principal: `codigo/motor/prossen_estrutural/aco/ligacoes_rigidas.py` (seção "emenda flangeada").

Capturas: `ESTRUCTTO_CAPTURAS=w9fi` (`CapturasW9Fi.cs`): busca "flange" na biblioteca, viga W, pilar H e viga UPN 200 (pelo
seletor, com o Sugerir automático) nas vistas juntas, elevação, planta, lateral e 3D (`90-w9fi-*`) e `90-w9fi.txt`.

## 1. Decisões

| Decisão | Escolha | Por quê |
|---|---|---|
| Um tipo com campo "uso" ou dois tipos | **Dois tipos**: `emenda_flangeada_viga` (liga `emenda-viga`) e `emenda_flangeada_pilar` (liga `emenda-pilar`), família `emenda` | igual ao par `emenda_viga`/`emenda_pilar`: o filtro "Só para <nó>" da biblioteca, o campo `perfil_viga`/`perfil_pilar` dos grupos e os padrões (pilar comprimido, contato usinado e em pé) saem certos sem campo extra. Os nomes ("Emenda flangeada de viga/pilar (perfil I/H/U)") e a descrição têm "flange": a busca acha os dois |
| Perfis | I/H do catálogo (W, HP, IPE, HEA, HEB, soldado por dict) e U (UPN, U laminado, U dobrado simples) no seletor (`perfis`, `resugerir`) | Ue fora: os enrijecedores de borda impedem a chapa; L e tubos recusados com o motivo |
| Modelo | **chapa de topo dos dois lados**: os componentes da chapa de topo (`tstub` Struik–de Back, `momento_efetivo`, grupo elástico, `_minimo_6152`) reaproveitados | as duas chapas são iguais e simétricas: a verificação de uma vale para a outra (o "pilar" da chapa de topo vira a outra chapa) |
| Forma da chapa | `forma` retangular (padrão: perfil + `sobra` e furos + borda com folga) ou quadrada (lado maior nas duas direções); `b_chapa`/`h_chapa` informados crescem em volta do mesmo centro | pedido "quadrado ou retangular" |
| Parafusos | `estendida` nao/superior/inferior/ambas (linhas fora das mesas a pf da face externa), `linhas_dentro` por mesa (a pf da face interna e depois a cada `s_linhas`), `n_colunas` 2 ou 4 (`g`, `g2`) | "dentro e fora das mesas, como chapa de topo estendida dos dois lados quando o momento pede" |
| U | chapa e colunas **simétricas ao plano médio da alma** (uma coluna dentro do U, outra atrás da alma), chapa retangular centrada na alma e cobrindo a ponta da mesa; `abertura_y` = −1 espelha | o grupo de parafusos fica no plano da alma, onde chega o cortante; a excentricidade ao centro de cisalhamento (|x0| − xg + tw/2) sai em aviso (torção da barra não verificada, como em LIGACOES-U) |
| Modelo 3D (`ligacoes_modelo`) | **só a biblioteca** | o nó de emenda do modelo propõe `parafuso`; `emenda_viga`/`emenda_pilar` também não entram por lá. Não há caminho de alternativas para registrar |

## 2. Verificações (NBR 8800:2008)

| Verificação | Item | Como |
|---|---|---|
| Momento: parafusos + chapas | 6.3.3.1 / 6.3.5 | Mef = M + N⁺·braço/2 (braço = H − tf) ≤ Σ Tcap·h; Tcap por parafuso = T-stub de Struik–de Back (Kulak cap. 17): b até a mesa (pf) ou até a alma, o mais perto; fora da largura da mesa b soma a distância (a favor); p = largura/colunas. **A compressão N não alivia** a tração (a favor, como a chapa de topo) |
| Tração axial maior que o binário | 6.3.3.1 / 6.3.5 | linhas da outra mesa com N/2 − M/braço |
| Flexão das chapas e alavanca por linha | 6.3.5 / 6.3.3.1 | parafuso mais solicitado da linha: T ≤ T_chapa, B = T + Q ≤ Ft,Rd |
| Tração + cisalhamento | 6.3.3.4 | (B/Ft,Rd)² + (Vb/Fv,Rd)² ≤ 1 por linha; também as linhas comprimidas tracionadas por Mz |
| Cortante nos parafusos | 6.3.3.2 | grupo elástico no plano da chapa com Fz (V), Fy (Vz) e Mx (T); só V: V/n |
| Mz | 6.3.3.1 | distribuição linear em torno do plano médio da alma (sem contar contato: a favor); na solda das mesas 3·Mz/bf |
| Atrito (opção `atrito`, `mu`) | 6.3.4.3 | Ff,Rd = 1,13·μ·Ch·FTb·ns·(1 − Ft,Sd/(1,13·FTb))/γe, Ch = 1, ns = 1, γe = 1,20; FTb ≈ 0,70·fub·0,75·Ab (tabela 15 dá até 4 % mais: a favor) [conferir γe no exemplar licenciado]; só alta resistência |
| Pressão de contato nos furos | 6.3.3.3 | lf = menor distância livre na direção da força (linhas, bordas; laterais com Vz) |
| Chapa ao cisalhamento | 6.5.5 | vertical (V) e horizontal (Vz), bruta e líquida |
| Solda das mesas | 6.2.5 / tabela 8 | filete nas duas faces (bf + bf − tw) ou penetração total (metal base bf·tf·fy); força = tração da mesa ou, **sem** faces usinadas, max(tração; M/braço + N⁻/2) |
| Solda da alma | 6.2.5 / tabela 8 | V em 2 filetes na meia altura (CBCA 6.4, como a chapa de topo) |
| Pernas mínima e máxima | 6.2.6.2.1 / tabela 10, 6.2.6.2.2 | I/H, UPN, U laminado. U dobrado (parede fina): não verificadas, perna = t, aviso (NBR 14762) |
| Contato usinado (opção `contato`, padrão no pilar) | 6.6.1 | mesa comprimida M/braço + N⁻/2 ≤ 1,8·fy·Apb/γa2 (Apb = bf·tf, fy da chapa ou do perfil, o menor). Com faces usinadas a compressão das mesas passa da ponta do perfil à chapa por contato e sai da solda (prática de emenda de pilar: Pfeil 9.1, AISC J1.4). Entre as duas chapas a compressão é sempre por contato |
| Perfil | 5.4.2.2 | M ≤ min(Zx; 1,5Wx)·fy/γa1 |
| Bordas | 6.3.11.1 / tabela 14, 6.3.12 | lateral e extremidade (linhas fora das mesas) |
| Espaçamentos | 6.3.9, 6.3.10 | entre colunas e entre linhas: mínimo 2,7db, máximo 24t ≤ 300 mm |
| Folga de chave e aperto | CBCA 6.1.1 | (g − tw)/2 ≥ db; pf ≥ 30/35/40 mm |
| Chapa informada | geometria | `b_chapa`/`h_chapa` cobrem o perfil |
| Mínimo de 45 kN | 6.1.5.2 | `_minimo_6152` (como as rígidas) |

## 3. Pré-dimensionamento (critério do W8-RIG)

`_buscar`: a primeira combinação com taxa ≤ 0,90 em todas as verificações. Ordem: sem linhas fora das mesas → estendida do
lado tracionado (viga) → as duas; 2 e depois 4 colunas; linhas dentro 1 a 3 (0 só com as duas estendidas); diâmetro ⅝"…1";
espessura comercial. Folga de 10 %: borda `_borda`, passo `_passo`, pf `_com_folga`, gabarito g ≥ max(2d/0,9 + tw; 2,7d/0,9)
(I: no mínimo bf/2). Pernas pela força com 10 % de folga e o mínimo da tabela 10 / 0,9 (`_perna_folga`); se não cabe em 90 %
do máximo, penetração nas mesas. `_buscar` só tenta enrijecedores de pilar quando o tipo tem esse campo (antes repetia a
mesma combinação 4 vezes nas emendas de pilar, com o mesmo resultado).

| Padrão | Esforços | Resultado |
|---|---|---|
| viga W310×38,7 | V = 100 kN, M = 60 kN·m | ⅞" A325, chapa 19 × 195 × 340, ajustada, 2 × 2, g 80, pf 40, filetes 6/4 — **0,875** (pf com folga; momento 0,869) |
| pilar W250×73 | N = −400 kN, V = 40 kN, M = 60 kN·m | ⅞", chapa 25 × 284 × 283, ajustada, 2 × 2, g 125, filetes 8/6, contato — **0,885** (momento) |
| viga UPN 200 (seletor) | V = 40 kN, M = 15 kN·m | ⅝", chapa 12,5 × 171,5 × 230, g 50, 2 × 2 — **0,857** (espaçamento entre colunas) |

## 4. Geometria (contrato)

x ao longo da barra, origem no plano da junta: chapa A de −tp a 0, chapa B de 0 a tp (`chapas`, nome "chapa de flange",
contorno em y, z, normal +x, campo `tp`); parafusos com a cabeça na face de A (origem x = −tp, eixo +x, porca do lado de B,
comprimento pela pega 2tp); soldas em volta do perfil nas faces x = ∓tp (mesas: face externa e interna; alma dos dois lados;
U: costas da alma na altura toda); `membros` com `pecas` (alma e mesas reais, também para o U) — trecho A até −tp, trecho B
a partir de +tp. Pilar: `vista` {x: [0, 0, −1], z: [1, 0, 0]} (W8-CONTRAV): a barra fica em pé na elevação e no 3D e a
planta olha de cima. O Realista não muda (referencial do contrato igual).

Desenho (C#, mínimo): `LigacaoDesenho.cs` — membros com `pecas` não contam como "tipo antigo sem apoio" (a viga ganhava uma
parede "apoio" no meio); `JanelaLigacaoProjeto.cs` — o segmentado de `estendida` mostra também "Ajustada" (nao) quando o
tipo tem essa opção (a chapa de topo estendida não tem: não muda).

## 5. Testes

- Motor: `testes/test_w9_flange.py`, 21 testes — registro e busca "flange", seletor I/H/U (sem Ue, L, tubos), caso A conferido
  só com as fórmulas (Ft,Rd, Fv,Rd, T-stub, MRd = 2·Tcap·h, solda das mesas), itens de norma, padrões ≤ 0,90, sugerir UPN 200 e
  U 200×75×3,00 ≤ 0,90, U centrado na alma e espelhado, recusas, geometria (chapas encostadas, parafusos, soldas, trechos,
  pilar em pé), 4 colunas, quadrada, linhas dentro/fora, contato, atrito, Mz e torção.
- Alterado com justificativa: `test_pontas_ligacoes.test_19_tipos_tem_pontas` conta 24 tipos (eram 22: + as duas emendas).
- Contagens: motor 1317 OK (9 pulados; eram 1296); pórtico 242.

## 6. Pendências

- U dobrado pequeno: a solda das mesas leva toda a força da mesa (a alma não entra no momento) e o mínimo de 45 kN pesa —
  U 100×50×2,65 não passa nas soldas com a perna = t; U 200×75×3,00 passa.
- Centro de cisalhamento do U: torção da barra só em aviso.
- O segmentado de `estendida` com 4 opções corta "Alinhada embaixo" no painel estreito.
- Alívio da compressão nos parafusos (contato) não considerado; mínimo de 50 % de NRd (6.1.5.3) não implementado.
- γe e a tabela 15 de 6.3.4.3 conferir no exemplar licenciado.
