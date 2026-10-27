# W9-FT — Emenda flangeada de tubo (redondo e quadrado/retangular, viga e pilar)

Data: 27/10/2026. Branch `wt9-ft`. Pedido: "falta a conexão de flange entre vigas e entre pilares… i, u, h, w quadrado ou
retangular. para metalon também e para tubo com redondo". Esta frente faz os **tubos**; os perfis abertos (I/H/U) são da
frente W9-FI (`ligacoes_rigidas.py`).

Código: `codigo/motor/prossen_estrutural/aco/ligacoes_tubos.py` (seção "W9-FT"). Testes: `testes/test_w9_flange_tubo.py`.
Captura: `ESTRUCTTO_CAPTURAS=w9ft` (`CapturasW9Ft.cs`, `90-w9ft-*.png` + `90-w9ft.txt`).

## 1. Decisões

| Ponto | Decisão | Por quê |
|---|---|---|
| Quantos tipos | **dois**: `emenda_flangeada_tubo_circular` ("Emenda flangeada de tubo redondo") e `emenda_flangeada_tubo_retangular` ("Emenda flangeada de tubo quadrado/retangular (metalon)") | entradas diferentes (círculo de furação × parafusos por lado, 2 ou 4 lados) e seletor de perfil restrito a uma forma só |
| Viga × pilar | campo `uso` (viga/pilar) no mesmo tipo | o cálculo é o mesmo (esforços no referencial da barra); `uso` só gira o desenho (`vista`: pilar em pé, como a W8-CONTRAV) |
| Família / liga | `emenda`, `liga = [emenda-viga, emenda-pilar]` | aparece em "Emendas" junto das emendas com talas; a busca "flange" acha os dois pelo nome |
| Perfil | `perfil_viga` com `perfis` = tubos TC (Vallourec + metalon redondo) ou TR (metalon quadrado/retangular + Vallourec TQ/TR) e `resugerir` | mesmo mecanismo do tubo–tubo; trocar o tubo refaz o Sugerir; `perfil_viga` recebe a seção do grupo no projetista |
| Círculo de furação | **diâmetro** (`D_furacao`), não raio | é como o flange é cotado e comprado (PCD) e casa com `D_flange` |
| Aço padrão | tubo redondo A-572 Gr. 50 (como o tubo–tubo); retangular CF-26 (metalon, NBR 6650); flange A-36 | editáveis |

## 2. Cálculo

Esforços do contrato: N (+tração), V (z, plano de h), Vz (y), M = My (fibras em ±z), Mz, T.
**NBR 8800 6.1.5.2** (como nas emendas rígidas): força da ligação = max(√(V² + N²), M/braço), braço = Dp (redondo) ou
distância entre as linhas de parafusos (retangular); abaixo de 45 kN todos os esforços sobem até 45 kN (aviso).

### Tubo redondo — Igarashi (CIDECT DG1, 1991; Packer & Henderson; Cao & Packer, AISC EJ 1997 1º trim., tabela 1)

- k1 = ln(Dp/(D − t)), k3 = k1 + 2, f3 = [k3 + √(k3² − 4k1)]/(2k1).
- **Flange**: N_eq ≤ π·f3·tp²·fy/(2·γa1) (de tp ≥ √(2T/(π·fy·f3))).
- **Parafusos** (com alavanca): N_eq·[1 − 1/f3 + 1/(f3·ln(Df/Dp))] ≤ n·Ft,Rd (NBR 8800 6.3.3.1).
- O método foi feito com Df − Dp = Dp − D; Df maior conta como 2Dp − D (Cao & Packer 4.1: o flange maior quase não ajuda;
  a favor). Df menor entra como é (mais alavanca).
- **Momento → tração equivalente**: N_eq = N + 4·√(M² + Mz²)/Dp = n × (força máxima por parafuso na distribuição elástica
  no círculo, Σy² = n·rp²/2). Compressão (N < 0) alivia, como na emenda de pilar com contato (Pfeil 9.1). N_eq ≤ 0: sem
  tração (aviso), só cortante.
- Conferência (teste `TestIgarashi`): a fórmula do nº de parafusos reproduz a tabela 3 do artigo (J-1 9,3; J-2 8,4; J-4
  5,8). A tf da tabela 3 sai ~1,8 % maior que √(2T/(π·Fy·f3)) em todos os casos (3,6 % em t²), diferença que o texto não
  explica — o teste aceita 2,5 %. Conferir no exemplar do CIDECT DG1.

### Tubo quadrado/retangular — T-stub com alavanca (CIDECT DG3 2ª ed. cap. 8; Struik/Kulak)

- Parafusos fora do tubo: **2 lados** = linhas em z = ±(h/2 + e_parede) (as faces que levam M); **4 lados** = mais as
  linhas em y = ±(b/2 + e_parede). Em cada linha, n parafusos no centro de faixas iguais da face (face/n).
- Força de cada parafuso pelo grupo elástico com rotação no centroide (a favor: o contato na borda comprimida não conta):
  F = N/n + |M|·|z|/Σz² + |Mz|·|y|/Σy².
- Cada parafuso é um T-stub (`ligacoes_flexiveis.alavanca`, Kulak eqs. 17.10–17.12): largura p = flange/n (2 lados) ou
  face/n (4 lados, sem os cantos — a favor; Willibald, Packer & Puthli 2003: conferir), **b' = e_parede − db/2 + t0**
  (DG3: alavanca medida até a face interna da parede; maior que o b' do perfil aberto, a favor — conferir no exemplar),
  a' = min(e_borda; 1,25b) + db/2. Verificações: F ≤ Tmax (flange) e B = F + Q ≤ Ft,Rd (parafuso).

### Comum

- Parafusos: cisalhamento 6.3.3.2 (V/n + T·r/Σr²), pressão de contato no flange 6.3.3.3 (lf = min(borda − dh/2; s − dh)),
  tração + cisalhamento 6.3.3.4 (tração com alavanca).
- Disposições: borda mínima/máxima do flange (6.3.11.1 tabela 14 / 6.3.12), espaçamento 2,7db e 24t ≤ 300 mm (6.3.9 /
  6.3.10), folga de aperto até a solda = 1,25·db (regra de oficina da W7-LIG) ≤ e_parede − perna.
- Solda tubo–flange (fluxo por metro no contorno do tubo como linha): redondo q = |N|/(πD) + M/(πr²) ⊕ V/(πr) + T/(2πr²);
  retangular pelo `_linha_solda` do perfil–tubo. Filete: metal da solda e metal base (6.2.5 tabela 8), garganta ≥ k·t0
  (NBR 16239 6.1.5.7, "resistência ≥ parede"), mínima da tabela 10. Penetração total: parede do tubo (tabela 8).
- Compressão: contato da ponta do tubo no flange, σ = |N|/A + M/W ≤ 1,8·fy/γa2 (6.6.1).
- **Parede fina / metalon**: validade CIDECT t0 ≥ 2,5 mm (EN 1993-1-8 7.1.1(5)) — metalon de 2 mm reprova. Com t0 ≤
  4,75 mm: + "parede fina junto ao filete" q ≤ t0·fu/1,65 (NBR 14762:2010, filete transversal em chapa fina = AISI S100
  J2.2; **item a conferir** no exemplar). A barra de metalon é da NBR 14762 (aviso).

## 3. Pré-dimensionamento (W8-RIG)

Mantém o tubo e o uso; procura do mais leve ao mais pesado — lados (2 antes de 4), nº de parafusos (redondo 4…24;
retangular ≥ 4 no total), parafuso (⅝"…1"; ½" só no retangular), espessura (8…50) — a 1ª combinação com taxa ≤ 0,90 em
todas as verificações; senão a 1ª aprovada; senão a de menor taxa. Folga de 10 % nas disposições, para cima a 5 mm:
e_parede = (1,25·db + perna)/0,9; borda = tabela 14/0,9; Dp = D + 2·e_parede; Df = Dp + 2·borda. Perna = menor comercial
≥ max(tabela 10; k·t0/0,707)/0,9.

### Padrões de fábrica

| Tipo | Tubo / uso | Esforços (ELU) | Detalhe sugerido | Taxa (governa) |
|---|---|---|---|---|
| redondo | TC 168,3 × 6,4, pilar | N −150 kN, V 15 kN, M 25 kN·m (≈ 48 % do MRd) | 4 × ¾" A325 em Ø250, flange Ø330 × 12,5 A-36, filete 12 | **0,87** (flange, Igarashi: N_eq = 250 kN) |
| retangular | metalon 100 × 100 × 3,00 (CF-26), viga | V 6 kN, M 6 kN·m (≈ 60 % do MRd); 6.1.5.2 → × 1,125 | 2 lados × 2 ½" A325, e_parede 25, borda 25, flange 150 × 200 × 12,5, filete 5 | **0,88** (borda mínima 22 ≤ 25) |

## 4. Desenho (contrato, sem mudança no C#)

- Redondo: `tubos` (dois trechos, x ≥ tp e x ≤ −tp, sem corte); retangular: `membros[viga|pilar].pecas` (4 paredes de cada
  lado, `_pecas_rhs` com início em tp). `membros` sempre com viga e pilar (senão o desenho punha a "parede" genérica).
- Flanges em `chapas` (origem no plano de contato, normal ±x): contorno circular de 72 lados ou retângulo. Furos não são
  desenhados (limite do desenho).
- Parafusos com a cabeça em x = −tp, pega 2·tp; cordões nas faces externas dos flanges em volta do tubo.
- Cotas: a cadeia automática (borda, furação, total) e, no redondo, "Øflange" e "Øfuração" (`linhas`) nos lados livres da
  vista de frente do flange (lateral na viga, planta no pilar).
- Pilar: `vista` {x: [0,0,1], z: [1,0,0]} (tubo na vertical na elevação e no 3D; planta = vista de frente do flange).

## 5. Testes

- Motor: `test_w9_flange_tubo.py`, 17 testes (Igarashi × Cao & Packer tabela 3; contas à mão do redondo e do T-stub;
  N_eq; só compressão; padrões ≤ 0,90; geometria e vista; parede fina e limites; 4 lados; lista do seletor; busca
  "flange"; sugerir em 4 tubos ≤ 0,90).
- Alterado com justificativa: `test_pontas_ligacoes.test_19_tipos_tem_pontas` contava exatamente 22 tipos; agora ≥ 24 (as
  outras frentes da W9 também acrescentam tipos; o que o teste protege é que todo tipo tem pontas).
- Contagens: motor 1313 OK (9 pulados), pórtico 242. Capturas `w9ft` (20 PNG + txt), `w7lig` (padrões 23 e 24: 0,87 e
  0,88) e `projetista` (o "CliqueRealNoSeletor" falhou uma vez com outra captura rodando; passou sozinho).

## 6. Limites e pendências

- Flange sem enrijecedores. Nenhuma verificação da barra (tubo) junto à emenda.
- Retangular 4 lados: sem a ajuda dos cantos; flange 2 lados só com linhas em ±z (as que levam M).
- Igarashi feito para tração axial uniforme; com M a tração equivalente uniforme é conservadora para o flange.
- Item da NBR 14762 para o filete em chapa fina a conferir; b' do DG3 (+ t0) a conferir no exemplar.
- Fontes: Cao, J.; Packer, J. A. "Design of tension circular flange joints in tubular structures", AISC Engineering
  Journal, 1º trim. 1997, p. 17–25 (tabelas 1 e 3); CIDECT DG1 (1991) / Wardenier et al. 2ª ed. 2008; Packer et al.,
  CIDECT DG3 2ª ed. 2009, cap. 8; Kulak, Fisher & Struik, cap. 17; NBR 8800:2008 6.1.5.2, 6.2.5, 6.2.6, 6.3.3, 6.3.9–6.3.12,
  6.6.1; NBR 16239:2013 6.1.5.7; EN 1993-1-8:2005 7.1.1(5).
