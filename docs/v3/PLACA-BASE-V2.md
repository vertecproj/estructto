# Placa de base v2: método NBR no motor Python

Data: 20/10/2026. Código: `codigo/motor/prossen_estrutural/aco/placa_base.py`. Testes: `testes/test_placa_base.py` e `testes/test_placa_base_verif.py` (classe `MetodoNBR`).

## 1. Por que mudou

O porte do `Nucleo.cs` gira a placa em torno da borda e não tem bloco de compressão no concreto (AISC-DG1.md §4.1). Esse modelo tem os seguintes problemas:

- subestima a tração nos chumbadores; no caso de teste, ela sai cerca de 3,3 vezes menor;
- traciona chumbadores do lado comprimido;
- não verifica a pressão no concreto;
- aplica um coeficiente global n sobre esforços que podem já estar majorados;
- divide o corte igualmente entre os chumbadores;
- deixa a chapa comprimida cerca de 2,7 vezes mais grossa que o necessário.

`verificar_placa_base` agora usa o método NBR. O cálculo do C# continua inteiro no campo `legado`, que alimenta `passa_legado`.

## 2. Contrato

- **N, V e M são esforços de cálculo (ELU)**, já majorados pelo solver. O método NBR **não aplica coeficiente global**. `seguranca` e `seguranca_modelo` só afetam `legado` e `passa_legado`, além da comparação de fixações (`fixacoes.py`, que segue o C# N:2087).
- A convenção é a mesma do legado: N > 0 é tração nas fixações. M ≥ 0 comprime o lado +y da placa. V é o módulo no plano.
- A API é compatível com a anterior. Os novos campos do resultado são: `concreto`, `cisalhamento`, `legado`, `metodo` e, em `chapa`, os campos `t_cbca`, `t_pfeil`, `governa` e `pressao_cbca`. O campo `forcas` agora traz `modelo`, `Y`, `C`, `fp`, `f`, `e`, `e_crit`, `T_total` e `taxa_concreto`.
- Novas entradas (esquema) do pilar/fundação: `fu_chapa`, `bloco_H`, `bloco_B`, `bloco_altura`, `hef`, `abrg`, `fissurado`, `fub_chumbador`, `fyb_chumbador`.
- Novas entradas (esquema) do corte: `atrito`, `lug_b`, `lug_t`, `lug_h`, `graute`, `arruela_soldada`.

## 3. Método

### 3.1 Concreto (NBR 8800:2008 6.6.5, CBCA p. 204)

A resistência de cálculo à pressão de contato é σc,Rd = fck/(γc·γn)·√(A2/A1) ≤ fck, com γc = γn = 1,4 e √(A2/A1) ≤ 2.

A2 é o maior retângulo homotético à placa que cabe no bloco `bloco_H × bloco_B`, suposto centrado na placa. Sem bloco informado, o motor adota A2 = A1, que é o lado seguro.

O pedido citava "6.6.1". O item da pressão de contato citado pelo CBCA é o 6.6.5, e é esse o adotado aqui.

### 3.2 Forças (`forcas_bloco`): bloco retangular, AISC DG1 com resistências NBR

O motor testa três casos, nesta ordem:

1. **Tração sem contato.** Se N > 0 e a distribuição elástica deixa todos os chumbadores tracionados, não há contato com o concreto.
2. **Compressão sem tração.** Com P = −N e e = |M|/P, o bloco tem comprimento Y = H − 2e e pressão fp = P/(Y·B). O caso vale se fp ≤ σc,Rd.
3. **Bloco a σc,Rd com chumbadores tracionados.**
   - qmax = σc,Rd·B.
   - f é a distância do centro da placa à fileira tracionada mais afastada.
   - Y = (f + H/2) − √[(f + H/2)² − 2(|M| + P·f)/qmax].
   - A tração total é T = qmax·Y − P, repartida igualmente entre os chumbadores dessa fileira.
   - Fileiras internas e chumbadores do lado comprimido ficam com T = 0.
   - A taxa do concreto é (|M| + P·f)/(qmax·(f + H/2)²/2). Ela fica ≤ 1 se e só se o equilíbrio tem solução.

### 3.3 Chapa (`espessura_placa_nbr`)

O balanço da chapa no lado comprimido é medido a partir de 0,95·d e 0,8·bf para perfis I. Tubo retangular usa 0,95 nas duas direções e tubo circular usa 0,80. Daí m = (H − k·d)/2 e n = (B − k'·bf)/2.

**Método primário: CBCA (cap. 10/11, p. 204 e 237).** A espessura é t ≥ √[6·Mmax/(1,25·fy/γa1)], com Mmax por unidade de largura.

- No lado comprimido, a pressão segue a distribuição elástica do CBCA PBE (p. 236, casos 1 a 4).
  - Mm = m²(2fmax + fm)/6, com fm = pressão na face do perfil; se o comprimento comprimido x for menor que m, Mm = fmax·x/2·(m − x/3).
  - Mn = fmax·n²/2.
  - Na compressão centrada isso se reduz a fc·ℓ²/2, com ℓ = max(m, n), exatamente como na PBR.
- No lado tracionado, M = T_fileira·x/b_ef, com x = |y| − d/2 + tf/2 (DG1) e b_ef = min(B, q·(2x + Ø)).

**Conferência: Pfeil/DG1 plástico.** A espessura é t ≥ √(4·M·γa1/fy). O motor usa o bloco retangular com fp = max(pressão do bloco, 0,85·fck/γc). É a regra do Pfeil 9.10.5, que dimensiona a chapa pela resistência do concreto.

**Qual governa.** A verificação `chapa: espessura` usa **sempre o CBCA**. O campo `governa` só informa qual dos dois métodos dá a chapa mais grossa.

Com cargas leves, o Pfeil sai bem mais conservador, porque usa a pressão resistente do concreto e não a atuante. No guincho, o CBCA pede 8,2 mm e o Pfeil 37,4 mm.

### 3.4 Corte (`cisalhamento_base`)

Os mecanismos não se somam. O motor usa o primeiro que se aplica:

1. **Atrito.** Fat,Rd = μd·Nc, com μd = `atrito` = 0,10 por padrão (CBCA: 10% de Nd; Pfeil eq. 9.17).
2. **Barra de cisalhamento**, se `lug_*` estiverem preenchidos. Verificações:
   - pressão no concreto à frente: σc,Rd(A2 = A1)·b·h;
   - flexão: V·(G + h/2) ≤ 1,25·b·t²/6·fy/γa1.

   O cone ACI 349 e a solda da barra **não** são verificados; o motor emite um aviso.
3. **Chumbadores.** Com furo alargado, só 2 chumbadores contam (DG1). Com `arruela_soldada`, todos contam.

### 3.5 Chumbador pré-concretado (`chumbador` vazio)

**Aço: NBR 8800 6.3.3.**

| Verificação | Item |
|---|---|
| Tração: 0,75·Ab·fub/γa2 | 6.3.3.1 |
| Escoamento: Ab·fy/γa1, como no CBCA p. 237 | 5.2.2 a) |
| Corte: 0,4 (ou 0,5)·Ab·fub/γa2 | 6.3.3.2 |
| Pressão no furo da chapa, com lf = borda − (Ø + 1,5 mm)/2 | 6.3.3.3 |
| Interação: (Ft/FtRd)² + (Fv/FvRd)² ≤ 1 | 6.3.3.4 |
| Espaçamento mínimo de 4·Ø | ACI 17.9.2 |

fub e fy vêm da classe ISO informada; `fub_chumbador` e `fyb_chumbador` têm prioridade sobre a classe.

**Concreto: ACI 318-19 cap. 17 (`concreto_chumbador_aci`).**

- Convenções de cálculo:
  - unidades SI (N, mm, MPa);
  - f'c = fck, limitado a 69 MPa (17.3.1);
  - φ = 0,70, para chumbador pré-concretado na condição B (17.5.3).
- Tração, cone (17.6.2):
  - Ncbg = ANc/ANco·ψec,N·ψed,N·ψc,N·Nb;
  - Nb = 10·√f'c·hef^1,5;
  - ANco = 9·hef²;
  - ψed = 0,7 + 0,3·ca,min/(1,5·hef);
  - ψc = 1,0 se fissurado e 1,25 se não fissurado;
  - ψec = 1/(1 + e'N/(1,5·hef)), aplicado em cada eixo;
  - com 3 ou mais bordas a menos de 1,5·hef, usa h'ef = max(ca,max/1,5; s/3).
- Arrancamento da cabeça (17.6.3): Npn = ψc,P·8·Abrg·f'c, com ψc,P = 1,0 se fissurado e 1,4 se não.
- Fendilhamento lateral (17.6.4), só quando hef > 2,5·ca1:
  - Nsb = 13·ca1·√Abrg·√f'c;
  - fator de canto (1 + ca2/ca1)/4 quando ca2 < 3·ca1;
  - para grupo com s < 6·ca1, Nsbg = (1 + s/(6·ca1))·Nsb, comparado com a soma das trações.
- Corte na borda (17.7.2), direção ±y:
  - Vcbg = AVc/AVco·ψed,V·ψc,V·ψh,V·Vb;
  - Vb = min[0,6·(le/da)^0,2·√da; 3,7]·√f'c·ca1^1,5, com le = min(hef, 8·da);
  - AVco = 4,5·ca1²;
  - casos de cálculo: todo o corte na fileira de trás, ou a parcela da fileira da frente.
- Alavanca (*pryout*, 17.7.3): Vcpg = kcp·Ncpg, com kcp = 1 se hef < 65 mm e 2 nos demais casos.
- Interação (17.8): se V/φVn ≤ 0,2, verifica só a tração; se N/φNn ≤ 0,2, só o corte; nos demais casos, (N/φNn + V/φVn)/1,2 ≤ 1.
- Valores adotados quando faltam dados:
  - hef = 12·Ø (CBCA p. 237);
  - Abrg = 1,163·Ø², porca hexagonal pesada com s ≈ 1,5·Ø, que confere com a tabela do DG1 para 3/4";
  - sem bloco informado, ψed = 1 e o corte na borda não é verificado, sempre com aviso.

Os fatores de carga da NBR (1,4 G + 1,5 Q) diferem dos do ASCE 7, mas são da mesma ordem. Os φ do ACI foram mantidos sem recalibração; registrar isso no memorial.

### 3.6 Chumbador de catálogo pós-instalado (`chumbador` preenchido)

O caminho do catálogo é mantido: `chumbadores.csv`, `avaliar_chumbador`, interação 5/3, √fck e ψ de cones.

As forças agora vêm do bloco de compressão. Esse caminho continua sem coeficiente global: a chamada usa NSeg = 1.

Na haste química entra também a verificação do aço pela NBR 6.3.3, com `classe_barra`. O coeficiente de ruptura do fabricante (NExig) é aplicado sobre esforços de cálculo, o que é conservador, e o motor emite aviso disso.

## 4. Antes × depois

| Caso | Legado (Nucleo.cs) | NBR v2 |
|---|---|---|
| AISC-DG1 §4.1.1: N = −500, M = 150, placa 0,50 × 0,40, fck 20 | 27,44 kN por chumbador tracionado; 3,05 kN nos comprimidos | **91,14 kN** (Y = 0,167 m, T = 182,3 kN); **0** nos comprimidos |
| Chapa comprimida: N = −500, placa 0,40 × 0,40, d = bf = 0,20, n = 1 | EspNecC = 54,8 mm | **t_CBCA = 21,8 mm** (Pfeil 39,2 mm) |
| Balanço T:162 (N = 0, M = 30, 2 × 2 M16) | Tmax = 42,00 kN | 44,72 kN (Y = 29 mm) |
| Guincho, nó 1 em posição oposta, WB 5/8" (N = −2,99, V = 1,81, M = −2,37) | Tmax = 3,38, Fv = 0,45 (V/4), NComb = 6,51 < 15, chapa 11,3 mm, passa_legado = falso | Tmax = 3,52 (2 tracionados, T = 7,04, Y = 3,1 mm), Fv = 0,90 (2 efetivos), NComb = 8,20 < 15 (taxa 1,83), concreto 0,023, chapa CBCA 8,2 mm (Pfeil 37,4 mm), ok = falso |

### Casos de livro (testes `MetodoNBR`, tolerância de 1%)

| Fonte | Esperado | Obtido |
|---|---|---|
| CBCA PBR p. 219, W310×52, 450 × 300, Nd 1833 | t = 31,50 mm | 31,50 |
| CBCA PBR p. 219, W310×52, 350 × 250, Nd 883 | 19,00 | 19,00 |
| CBCA PBR p. 219, W310×38,7, 350 × 300, Nd 1398 | 31,50 | 31,50 |
| CBCA PBE p. 266, W250×73, 500 × 350, (1966; 49,6) | t = 50 mm; Mmax = 118,35 kN·cm/cm | 50,00; 118,34 |
| CBCA PBE p. 266, (1843; 62,0) | 50 mm | 50,00 |
| Pfeil 9.10.5 (p. 260–261) | A1 > 1098 cm²; t > 1,79 cm; Ag > 3,6 cm² (3,63) | 1097,6; 1,798; 3,635 |

A linha W150×29,8 / 25 mm / 857 kN do CBCA não foi usada, porque não se reproduz (CBCA.md §3.3).

## 5. Divergências encontradas

- **A2 homotético × área do bloco.** Na linha W310×38,7 (placa 350 × 300, bloco 450 × 500), o A2 homotético da NBR dá k = 450/350 = 1,286 e σc,Rd = 13,12 MPa. Isso fica abaixo da pressão atuante de 13,31 MPa, com taxa 1,015. O CBCA parece usar a área inteira do bloco. O teste aceita taxa até 1,02 nessa linha; a tabela é governada pela chapa.
- **Pfeil × CBCA na chapa.** O Pfeil usa a pressão resistente 0,85·fck/γc e Z = t²/4. O CBCA usa a pressão atuante e 1,25·W. Com carga leve, o Pfeil sai muito mais grosso. Por isso ele fica só como conferência.

## 6. Testes antigos alterados

| Teste | Mudança | Justificativa |
|---|---|---|
| `test_9_1_balanco` | lê `r['legado']` em vez de `r['forcas']`, `r['fixacao']` e `r['chapa']` | os números (Tmax = 42 etc.) são do C# legado (Testes.cs T:162–191); o resultado principal agora é o bloco de compressão (44,72 kN) |
| `test_9_2_guincho_eixos` | `Fv·n = |Rx|` lido de `r['legado']['forcas']` | o corte igual em todas as fixações é regra do legado, que era insegura; a v2 usa atrito e depois 2 chumbadores efetivos |
| `test_agregacao_imutabilidade_comparacao` | `passa_legado == legado.fixacao.ChOk` | `fixacao` agora é a da v2 |
| `test_V_PB_02` | `dimensionar_chumbador(..., metodo='legado')` | o teste protege a equivalência com o C# |

## 7. WPF (`JanelaPlaca.cs`)

A janela continua calculando com o `Nucleo.cs`, sem nenhuma alteração nas contas, para continuar batendo com a planilha.

O cabeçalho do resultado ganhou o aviso: "Método legado (pivô na borda) — use o motor Python para o método NBR com bloco de compressão".

Diferenças entre o que a janela mostra e o motor v2:

- pivô na borda × bloco de compressão;
- coeficiente n global × esforços de cálculo;
- corte V/n × atrito, barra e 2 efetivos;
- chapa por faixas elásticas × CBCA;
- sem concreto × NBR 6.6.5 e ACI 17.

## 8. Pendências

- Flexão biaxial.
- Enrijecedores e efeito alavanca (Kulak cap. 17).
- λn' de Thornton.
- Cone ACI 349 e solda da barra de cisalhamento.
- Armadura de ancoragem (NBR 6118).
- Solda pela NBR 6.2.5.
- Ligar a janela WPF ao motor Python.
- Conferir os exemplos do DG1 real (AISC-DG1.md §0).
