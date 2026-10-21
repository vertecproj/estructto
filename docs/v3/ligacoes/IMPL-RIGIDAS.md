# Ligações rígidas e emendas — implementação (`aco/ligacoes_rigidas.py`)

Data: 20/10/2026. Contrato: `docs/v3/LIGACOES-CONTRATO.md`. Testes: `codigo/motor/testes/test_ligacoes_rigidas.py` (27 testes).

## 1. Tipos registrados

`TIPOS` segue o contrato (`nome`, `familia`, `liga`, `descricao`, `entradas`, `verificar`, `sugerir`). O registro é a inclusão de `"ligacoes_rigidas"` em `ligacoes._MODULOS_CONTRATO`; `tipos_ligacao()` lista os tipos e `verificar_ligacao()` despacha para `TIPOS[tipo]["verificar"]`. Texto longo de cada entrada fica em `ajuda`.

| Tipo | Família | Liga | Resumo |
|---|---|---|---|
| `chapa_topo_ajustada` | rigida | viga-pilar-mesa | Chapa flush. 2 parafusos por linha junto a cada mesa, mais `n_linhas_extras` linhas para dentro |
| `chapa_topo_estendida` | rigida | viga-pilar-mesa | `estendida` = `superior` (assimétrica, CBCA LMPA) ou `ambas` (simétrica, inversão de momento). Enrijecedor opcional na extensão |
| `flange_plate` | rigida | viga-pilar-mesa | Chapas de mesa soldadas ao pilar (penetração total ou filete). Ligação à mesa da viga `parafusada` ou `soldada`. Alma em chapa simples |
| `painel_alma_pilar` | rigida | viga-pilar-mesa | Pilar isolado sob as forças das mesas, com enrijecedores (`t_enr_pilar`) e chapa de reforço (`t_reforco`) |
| `emenda_viga` | emenda | emenda-viga | Talas parafusadas nas mesas (corte simples) e duas talas na alma (corte duplo) |
| `emenda_pilar` | emenda | emenda-pilar | Mesmo arranjo, com `contato` (superfícies usinadas) ou sem contato |

Os tipos com pilar (as três ligações viga-pilar) aceitam os campos do pilar. Se `perfil_pilar` ficar vazio, sai um aviso e o pilar não é verificado.

Perfis: um nome do catálogo (W/HP Gerdau) ou um dict `{d, bf, tw, tf, r, nome}` em mm, para perfis soldados como o VS 600×95 do Pfeil.

Convenções:
- Entradas em mm, kN e kN·m.
- N > 0 é tração.
- M > 0 traciona a mesa superior (z+).
- Vz, Mz e T geram aviso de "não considerados".

## 2. Modelos e itens

### Chapa de topo
- **T-stub por linha**: Struik & de Back (Kulak cap. 17, eqs. 17.10–17.12; AISC-DG1.md §2) com as resistências da NBR.
  - Mp = p·t²/4·fy/γa1, com b' = b − d/2, a' = min(a, 1,25b) + d/2 e δ = 1 − d'/p.
  - Capacidade por parafuso = min[Mp(1+δ)/b' ; Ft,Rd ou (Ft,Rd·a' + Mp)/(a' + b')].
  - A segunda fórmula é a FR,npt do CBCA LT.
  - Itens: 6.3.3.1 e 6.3.5.
- **Momento resistente**: distribuição plástica Σ n·Tcap·h, medindo h até o centro da mesa comprimida (DG4 2ª ed.).
  - Momento atuante nos parafusos: Mef = |M| + N⁺·(H − tf)/2. Com isso o caso 2 do CBCA (Nd = 0,4·Mmax/(H − tf)) dá o mesmo Mef que Mmax.
- **Linha a linha**:
  - flexão da chapa;
  - B = T + Q ≤ Ft,Rd;
  - mesa do pilar como T-stub, com largura tributária ≤ 3,5b por lado. O enrijecedor de continuidade entra como segunda "alma";
  - interação tração + corte (6.3.3.4).
- **Demais verificações**:
  - corte nos parafusos (6.3.3.2), com V dividido por todos;
  - contato na chapa e na mesa do pilar (6.3.3.3);
  - cisalhamento da chapa (6.5.5);
  - momento da viga Zx·fy/γa1 ≤ 1,5·W·fy/γa1 (5.4.2, como o CBCA);
  - solda das mesas: penetração total pelo metal base (tab. 8), ou filetes com metal da solda e metal base;
  - solda da alma na meia altura comprimida (CBCA 6.4).
- **Detalhamento**:
  - borda (tab. 14) e borda máxima (6.3.12);
  - gabarito ≥ 2,7db;
  - (g − tw)/2 ≥ db;
  - pf ≥ 30/35/40 mm (CBCA 6.1.1);
  - perna de filete (tab. 10, 6.2.6.2.2);
  - bp,ef ≤ bf + 25 mm, com aviso;
  - enrijecedor da extensão: t ≥ tw·fyb/fys e hs/t ≤ 0,56√(E/fy).

### Pilar, NBR 8800 5.7
| Item | Verificação |
|---|---|
| 5.7.2 | Flexão local da mesa: 6,25·tf²·fy/γa1. Só com força soldada; com parafusos, a mesa entra no T-stub |
| 5.7.3 | Escoamento local da alma: 1,10(5k + ℓn)·fy·tw/γa1, ou 2,5k no topo |
| 5.7.4 | Enrugamento: 0,66 ou 0,33·tw²[…]√(E·fy·tf/tw)/γa1 |
| 5.7.6 | Flambagem da alma por compressão: 24tw³√(E·fy)/(h·γa1), só se `M_outra` > 0 (forças nas duas mesas) |
| 5.7.7.1 | Painel: VRd da 5.4.3 (Aw = dc·tw, λ = h/tw, kv com a = H − tf da viga), × (1,4 − Nc/Ny) se Nc > 0,4Ny. Demanda Σ(M/(H − tf)) − V_pilar |
| 5.7.7.2 | Chapas de reforço dos dois lados da alma (`t_reforco` = espessura de cada uma), 150 mm além das mesas da viga; cada chapa pela 5.4.3 com λ = h/t (sem o (dz + wz)/t ≤ 90 do AISC 341). Somadas a tw em 5.7.3 a 5.7.6 |
| 5.7.9 | Enrijecedores levam F − Rmín (5.7.9.1 tração; 5.7.9.2 a 5.7.9.4 compressão: faixa de 25tw, 12tw no topo, KL = 0,75h). 5.7.9.5: t ≥ b/15, t ≥ tfb/2, b + tw/2 ≥ bfb/3. Também b/t ≤ 0,56√(E/fy) (Q = 1 na coluna) |
| 6.1.5.2 | Chapa de topo, flange plate e emendas: esforços escalados até max(√(V² + N²), M/braço) = 45 kN |

Numeração conferida no texto da NBR 8800:2008 (docs/v3/ITENS-NORMA.md §2). Os coeficientes conferem com o Pfeil 6.3.6 (p. 180–183).

### Flange plate
- Força na chapa: |M|/(H + t) ± N/2.
- Chapa:
  - escoamento (6.5.3 → 5.2.2a);
  - ruptura: An ≤ 0,85Ag quando parafusada; Ct de filetes longitudinais (5.2.5) quando soldada;
  - compressão (6.5.4), com K = 0,65 e L = folga + borda.
- Parafusada:
  - corte simples;
  - contato na chapa e na mesa;
  - rasgamento (6.5.6);
  - furos na mesa tracionada (Pfeil eqs. 6.8b e 6.9).
- Soldada: filetes, L ≥ distância entre os filetes, 4b e 40 mm.
- Solda chapa–pilar.
- A alma reutiliza `chapa_simples` de `aco.ligacoes`.
- Pilar: 5.7.2 a 5.7.9.

### Emendas
- Divisão do momento (Pfeil eqs. 9.1–9.3): Mw = M·(Aw/6)/(Af + Aw/6).
  - Força na tala: Mf/H, com o plano de corte na face da mesa.
  - N dividido pelas áreas.
- Mesa:
  - corte simples;
  - contato (soma por furo);
  - rasgamento na tala e na mesa;
  - tala tracionada (6.5.3) e comprimida (6.5.4);
  - furos na mesa da viga.
- Alma:
  - M0 = Mw + V·a;
  - grupo excêntrico elástico (Pfeil eqs. 3.9–3.12) em corte duplo;
  - contato nas talas e na alma;
  - cisalhamento das talas;
  - flexão das talas (Z·fy/γa1);
  - ruptura da alma na seção líquida.
- Pilar com `contato`:
  - a compressão passa pelo contato: 1,8·fy·A/γa2 (6.6.1);
  - as talas levam só a tração resultante e o cortante (Pfeil 9.1, p. 242).
- `minimo_50` (viga): M e V ≥ 50% de MRd e VRd (Pfeil p. 244).

### `sugerir()`
Busca em grade (diâmetro × espessura × número de parafusos, e linhas extras na chapa de topo). Devolve a primeira combinação aprovada. Com pilar, tenta também enrijecedores e reforço, do mais leve ao mais pesado. Se nada passar, devolve a combinação de menor taxa, e o `verificar` mostra a falha.

### Geometria
Toda a geometria fica no referencial do contrato: x ao longo da barra, z na alma e mm.
- **Viga–pilar**: origem no eixo do pilar e chapa na face da mesa (x = dc/2).
- **Emendas**: origem no eixo da emenda.
- **Saídas**:
  - chapas: chapa de topo, enrijecedor triangular, enrijecedores do pilar, chapa de reforço, chapas de mesa e de alma, talas;
  - parafusos com comprimento: pega + porca + arruela, arredondado a 5 mm;
  - soldas (polilinhas);
  - recuo da viga na flange plate.

## 3. Exemplos de livro reproduzidos (tolerância de 1%)

| Teste | Fonte | Esperado | Obtido |
|---|---|---|---|
| Tração nos parafusos, LMPA W460×89 | CBCA p. 132, R17 | 401 kN·m | 400,6 (Ab de 25 mm = 4,909 cm², fub = 82,5; chapa de 31,5 sem alavanca) |
| Caso 2 com Nd | CBCA R18 | Nd = 360 kN, mesmo Mef = 401 | 360,2 / 401,0 |
| Altura da chapa assimétrica / simétrica | CBCA p. 114 | 563 / 623 mm | 563 / 623 |
| Solda das mesas, W150×13 (S1 = 5) | CBCA R15 | 27 kN·m | 27,3 (metal base com fy = 35) |
| Momento da viga, W610×155 | CBCA R12 | 1511 kN·m | 1511 (AR-350) |
| Divisão do momento, VS 600×95 | Pfeil 9.10.1, p. 254 | Mw = 55,9 kN·m; F = 453 kN | 55,9 / 454 |
| Ruptura da tala da mesa | Pfeil p. 254 | 920,6 kN | 920,6 |
| Σr² da alma | Pfeil p. 254 | 2348 cm² | 2348 |
| Força no parafuso da alma | Pfeil p. 255 | 71,2 kN (o texto também cita 70,5) | 71,1 |
| Ruptura da alma na seção líquida | Pfeil p. 255 | 711 kN | 707,6 |
| Flexão das talas da alma | Pfeil p. 255 | 186 kN·m | 186,2 |
| Escoamento local / enrugamento, W530×92 | Pfeil 6.5.9, p. 196 | 431 / 353 kN | 431 / 354 |
| T-stub (cálculo à mão no teste) | Kulak 17.10–17.12 | Q = 7,551; B = 67,55; Tchapa = 85,65; Tparaf = 99,03 kN | idem |

### Hipóteses explícitas
- As tabelas LMPA e LMSS do CBCA só batem com **fy = 35 kN/cm²** (os testes usam AR-350) e com o **parafuso de 1" calculado com a área de d = 25 mm**. O teste R17 usa Ft,Rd = 0,75·π·25²/4·825/1,35 = 225,0 kN de forma explícita.
- Com d = 25,4 mm, o motor dá MRd ≈ 409 kN·m. O valor fica abaixo dos 413,9 kN·m sem alavanca porque bp,ef = bf + 25 = 217 mm e porque aparece um pequeno Q.

## 4. Divergências em relação aos livros (de propósito)

- Pfeil 9.10.1 usa 0,7·0,6 = 0,42·Ab·fu no corte do parafuso. O motor usa 0,40 (NBR 6.3.3.2): 94,8 kN contra 99,6 no 7/8" e 96,8 contra 101,6 no 5/8" em corte duplo.
- O exemplo do Pfeil, verificado no motor, **reprova a pressão de contato na alma da viga**: 71,0 kN contra 60,6 kN. O motor toma lf até a ponta da viga, a 30 mm do eixo da emenda menos meio furo. O livro não verifica esse ponto.
- O CBCA calcula a solda das mesas só pelo metal base. O motor verifica também o metal da solda, que governa no filete de 5 mm E70: 21,8 contra 27,3 kN·m.
- O CBCA não verifica o elemento suporte. O motor verifica a mesa do pilar (T-stub), a alma e o painel (5.7).
- A chapa de topo é verificada pelo T-stub de Struik, e não pelo αm do DG4 1ª ed. que o CBCA usa (p. 115).

## 5. Integração e pendências

- `verificar_ligacao()` devolve, para estes tipos, o resultado do contrato (`ok/taxa/verificacoes/geometria`), não o formato antigo (`taxa_max/criterio`).
- `projeto.normas_implementadas()` não lista `aco.ligacoes_rigidas`. O módulo já expõe `COBERTURA`. O `COBERTURA` de `ligacoes.py` ainda diz que "ligações rígidas não estão implementadas".
- Falta implementar:
  - charneiras da mesa do pilar e da chapa estendida enrijecida (DG4 2ª ed., yield lines);
  - ligação por atrito;
  - emenda de perfis diferentes (chapas de enchimento);
  - talas internas da mesa;
  - dimensionamento das soldas de enrijecedor e de reforço;
  - painel com a parcela de deformação (5.7.7 b);
  - fadiga.
- Conferir no texto da NBR 8800 a numeração 5.7.2 a 5.7.9 e o item de "furos na mesa tracionada", citado como 5.4.2.
