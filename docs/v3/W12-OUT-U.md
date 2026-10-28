# W12-OUT — perfis U nas outras ligações (placa de base, gusset, perfil em tubo)

Data: 28/10/2026. Branch `w12-out` (do `unificacao`, v0.6.18). Pedido do usuário: "as outras ligações não têm os U e UPN
também, revise isso". Briefing comum: `W12-U-comum.md`. Referência de método: [W11-CHAPA-TOPO-U.md](W11-CHAPA-TOPO-U.md).

Código: `aco/placa_base.py` (`_secao`, `_xg_u`, `_contorno_u_mm`, `_grupo_u`, `_solda`, `verificar_placa_base`,
`verificar_solda_contorno`), `aco/ligacoes_contraventamento.py` (`perfil_barra`, `_membro`, `coef_Ct_frio`,
`_contato_fino`, `_solda_fina`, `_face_u`, `_de_catalogo`, `_diametro_frio`, `_buscar`), `aco/ligacoes_tubos.py`
(`_parede_chs`, `_ler_pt`). Testes: `testes/test_w12_out_u.py` (26 testes, contas à mão na docstring). Capturas:
`ESTRUCTTO_CAPTURAS=w12out` (`CapturasW12Out.cs`, `93-w12out-*.png` + `93-w12out.txt`; modelo `exemplos/base-pilar-u.json`).

## 1. Placa de base com pilar U

Antes: o pilar U entrava como I. Nada de errado na espessura da chapa (os fatores 0,95·h e 0,8·b do DG1 valem para a caixa
do U), mas a solda de contorno era o retângulo fechado h·(b+d) e o N atuava no centro da placa.

- **Solda** (`_grupo_u`). O contorno é o polígono real do U, em volta das duas faces das chapas: costas da alma, 2 faces
  externas das mesas, 2 pontas, 2 faces internas das mesas e face interna da alma (Ue: com os lábios D). Grupo elástico
  (NBR 8800 6.2.5; AISC 360 J2.4): comprimento L, momento de inércia por garganta unitária em torno do centroide do grupo
  (∫ q² ds exato por segmento) e fibra extrema c. Área = 0,707·h·L, I = 0,707·h·iu. Placa menor que o perfil: os vértices
  são recortados à placa (como o min(perfil, chapa) do contorno fechado). I e tubo: as mesmas contas de antes.
- **Placa centrada na caixa do perfil (b × h)**, não no centroide. Motivo: é o que o desenho (`PlacaDesenho.cs`, `JanelaPlaca.cs`:
  RefIn/RefOut, "pelo perfil", sobras) já supõe, e dá o mesmo balanço dos dois lados (os fatores 0,8·b do DG1 valem à ponta
  das mesas; do lado da alma, mais rígido, é a favor). O custo é a excentricidade do centroide: N e M atuam no centroide, a
  e = |b/2 − xg| do centro da placa (UPN 200: 15,49 mm).
- **Excentricidade.** No eixo fraco (a flexão no plano da largura b), e cai no plano: M_placa = M + |N|·e (o sinal que
  aumenta |M|: a orientação do U na placa não está nos dados). Aviso com o valor. No eixo forte, e é fora do plano da
  flexão: só aviso (o método da placa é uniaxial, como a nota do biaxial). A solda no eixo fraco soma N·e_w, e_w = distância do
  centroide do grupo ao centroide do perfil (UPN 200: 0,92 mm), ao M da barra.
- **Orientação.** Alma do U em −u e mesas para +u (a mesma de `pontas.contorno`). O desenho 2D da placa já traça o U.
- **Torção pelo centro de cisalhamento** do pilar (V·e) não é verificada: aviso na placa e na solda.
- **C#**: `Solda.cs` (cartão de solda e "perna auto" da janela da placa, também compilado pelo `portico/compilar.ps1`, C# 5)
  ganhou `GrupoU`, o espelho do `_grupo_u` do motor (contorno aberto, iu pelo ∫q² ds exato, N·e no eixo fraco, xg das chapas
  retas); sem isso a janela mostraria o retângulo (550 mm no UPN 200) e uma perna "auto" diferente da do motor (683 mm). O 3D
  da placa (`PlacaDesenho.cs`) desenha o U (alma de um lado, mesas para o outro, como na planta) e não mais uma caixa. A
  planta já desenhava o U. Não mudam I, tubo e demais formas.

Contas à mão (UPN 200: h 200, b 75, tw 8,5, tf 11,5, xg 22,01): contorno = 200 + 2·75 + 2·11,5 + 2·66,5 + 177 = **683 mm**;
iu = 666 667 + 462 103 + 1 500 000 + 204 564 + 1 041 689 = **3 875 023 mm³**, c = 100; centroide do contorno em u = −16,405 mm
(perfil −15,49: e_w = 0,92). Ue 150x60x20x3,00: 2·154 + 150 + 144 = **602 mm**. U dobrado 150x50x3,00: 494 mm.

## 2. Gusset de contraventamento

- **Diagonal em U dobrado** (U 50x25 a 300x100; `perfil_barra` lia `p["tw"]`, que não existe nele: `KeyError`). Agora
  tw = tf = t, `formado = True`, e o U dobrado entra em `_PERFIS_BARRA`. O perfil é ligado pela alma.
  - Escoamento da seção bruta: NBR 8800 5.2.2 a) (γ = 1,10 = NBR 14762 9.6.2 a)); ruptura fora da ligação A·fu/1,35 (9.6.2 b).
  - Seção líquida na ligação: Rd = Ct·An·fu/1,65 (9.6.2 c), Ct = 1 − 1,2·x̄/ℓ ≤ 0,90 (9.6.3; AISI S100 E5.2), x̄ = xg
    (face da alma ao centroide) e ℓ = distância entre o primeiro e o último parafuso (ou comprimento médio dos filetes).
    Um parafuso por linha em tração: recusado (ℓ = 0; "use 2 ou mais"). Ct < 0,60: aviso, e "Ct ≥ 0,60" como limite adotado.
  - Parafusos na parede fina: contato Fc = mf·C·db·t·fu/1,65 (AISI S100 E3.3: C = 3,0 se db/t < 10; 4 − 0,1·db/t até 22;
    1,8 acima; mf = 0,75, sem arruelas) e rasgamento Fe = e·t·fu/1,65 (E3.1, e do centro do furo à borda do furo vizinho ou à
    ponta). O menor por parafuso, somado: verificação "contato e rasgamento na parede fina". A de contato da NBR 8800 6.3.3.3
    continua e vale também.
  - Solda na parede fina: filete longitudinal Pn = (1 − 0,01·L/t)·t·L·fu (L/t < 100) ou 0,75·t·L·fu, γ = 1,65 (AISI S100 J2.2 b).
  - **A conferir** (marcados nos itens): as constantes γ = 1,65, mf, C e as fórmulas do AISI foram tomadas por equivalência;
    o texto da NBR 14762:2010 9.5 e 9.6 não foi conferido. A conta é a favor da segurança nos três pontos (γ de φ = 0,60;
    mf sem arruela).
  - Aviso do U dobrado: compressão, flambagem local/distorcional e esbeltez da barra não entram aqui (NBR 14762).
- **Ue (enrijecido) recusado**, com mensagem: lábios de borda e distorção não são tratados; use U simples, 2L ou tubo.
- **Apoio U (`_de_catalogo`)**: `eb`/`ec` = distância do ponto de trabalho (centroide) à face soldada. Padrão "mesa": h/2 (como
  o I; o gusset solda na face externa da mesa). Opção `face_u_viga` / `face_u_pilar` = "alma": xg (costas da alma voltadas ao
  gusset; UPN 200: 22,01 mm). Chaves lidas só no Sugerir (como `perfil_viga`); `eb`/`ec` informados vencem. Valor fora de
  mesa/alma: `ValueError`. Não há verificação local da mesa/alma do U sob o filete do gusset (nem no I).
- **Sugerir do U dobrado**: parede de 3 mm tem janela de borda estreita (tabela 14: emin 29 mm para ⅝"; 6.3.12: máx 12t =
  36 mm), que com a folga de 10 % não admite borda em mm inteiros. O Sugerir tenta ⅝" e cai para ½" (emin 22 mm), com
  e1 = ⌈max(emin/0,9; emin + 3)⌉ (25 mm). O diâmetro informado pelo projetista vale. A busca (`_buscar` com alvo 0,90) pega o
  primeiro passo com taxa ≤ 0,90; só no U dobrado (o resto: o primeiro que passa, como antes).
- **U dobrado soldado**: perna mínima (tabela 10) = máxima (t = 3 mm): a taxa de "dimensão mínima" e "dimensão máxima" é
  1,00, intrínseca (como a perna mínima do W11). Passa; o resto fica ≤ 0,90.

## 3. Perfil soldado em tubo (`perfil_tubo_soldado`)

- Receptor **tubo circular**: a parede é a da EN 1993-1-8 tabela 7.4 (caso "I"). Antes dizia "tratado como I". Com h ao longo
  do tubo, as mesas do U (largura b em ±h/2, duas linhas transversais ao tubo, afastadas de h) são as do I, e N, Mip e Mop
  da tabela dependem só delas (β = b1/d0, η = h1/d0); a alma, na borda das mesas em vez de no eixo, não entra nas fórmulas.
  Logo a conta vale pelas mesas, mas o efeito da alma deslocada é caso fora da tabela (a norma não cobre U): aviso explícito
  (CIDECT DG1, conferir; torção pelo centro de cisalhamento não verificada). A solda e a punção já usavam o contorno real do U.
  Não é afirmação de conservadorismo.
- Receptor **tubo retangular**: parede não verificada (EN 1993-1-8 tab. 7.13/7.14), para qualquer perfil (limite anterior, o
  aviso continua). Só β, solda e metal base.
- **Ue recusado** em `_ler_pt` (antes: erro genérico de opção): os lábios não têm campo (`D`) nem contorno na solda aqui e a
  tabela não os cobre. Fica fora de `FORMAS_RAMAL`.
- UPN 100 (b = 50) num Ø 114,3: β = 0,44 → 0,4/0,44 = 0,91: a validade da tabela (β ≥ 0,4) deixa a taxa acima de 0,90; a
  geometria manda (como no I). Tubos mais estreitos (Ø 101,6) ou ramais mais largos saem ≤ 0,90.

## 4. Pré-dimensionamento (taxa ≤ 0,90 em todas as verificações)

| Caso | Esforços | Sugerir | Taxa |
|---|---|---|---|
| Placa de base, pilar UPN 200 (placa 400 × 280) | N = −200, V = 5, My = 10 | 4 × Ø8 classe 8.8, chapa 22,4, solda 6 mm E70 | **0,896** (solda; eixo fraco 0,752) |
| Gusset, diagonal UPN 160, ⅝" A325 | N = 150 | 4 parafusos s = 50, e1 = 35, gusset 8 | **0,857** (espaçamento mínimo) |
| Gusset, diagonal UPN 160 | N = −150 | idem | 0,857 |
| Gusset, diagonal U 150x50x3,00 | N = 60 / −60 | ½" A325, 4 parafusos, e1 = 25, gusset 6,3 | **0,880** (borda mínima) |
| Gusset, U 150x50x3,00 soldado | N = 60 | l1 = 80, perna 3 mm | 1,00 (perna mínima = máxima, intrínseco) |
| Perfil–tubo, UPN 100 em Ø 101,6 × 5 | V 6, M 1,0 | perna 4 mm | **0,813** (β ≥ 0,4) |
| Perfil–tubo, UPN 100 em 150x150x6 | V 15, M 4 | perna 4 mm | **0,750** (solda mínima; parede não verificada) |
| Perfil–tubo, U 150x50x3 em 150x150x6 | V 8, M 1 | perna 4 mm | 0,750 |

## 5. Recusas

- Ue na barra do gusset e como ramal em tubo (lábios/distorção; sem contorno nem campo D). Mensagem diz o motivo e o que usar.
- U dobrado tracionado e parafusado com 1 parafuso por linha (Ct pede ℓ > 0).
- Placa de base com Ue: aceita (contorno com lábios, 602 mm no 150x60x20x3,00), mas a parede fina do pilar é limite do
  método (perna ≤ t), como já era.

## 6. Testes e capturas

- `test_w12_out_u.py`: 26 testes. Placa: contorno 683/602/494, inércia e centroide à mão, N·e_w no eixo fraco, recorte pela placa
  menor, excentricidade da placa (M = 3,098 kN·m), I/tubo inalterados, Sugerir UPN 200. Gusset: `perfil_barra`, Ue recusado,
  Ct e ruptura à mão, contato fino à mão, 1 parafuso recusado, UPN inalterado, Sugerir (UPN 160, U dobrado ±N, soldado),
  `face_u`. Tubo: N1,Rd = 95,05 kN à mão, aviso, mesmas mesas do I, Ue recusado, Sugerir (circular e retangular).
- Capturas `w12out`: `93-w12out-{gusset-upn160, gusset-u-dobrado, perfil-tubo-u}-{vistas, elevacao, planta, lateral, 3d}.png`
  e `93-w12out-placa-u-{planta, forcas, calculo, 3d}.png` (pilar UPN 200 da `exemplos/base-pilar-u.json`: 4 × Ø16, chapa 19,
  400 × 280, solda 5 mm, taxas 0,97 / 0,65 / 0,38). Na placa, a janela é lenta na bateria (minutos por aba) e a captura
  "planta" é o primeiro quadro, antes do resultado do motor; "forças", "cálculo" e "3D" têm o resultado.
- Portico: 242 testes passam com o `Solda.cs` novo. Motor: 1394 testes OK (1368 + 26).

## 7. Pendências

- Torção pelo centro de cisalhamento do U (placa, gusset, perfil–tubo): só aviso.
- NBR 14762:2010 9.5 e 9.6: conferir os itens, γ e coeficientes das verificações marcadas "a conferir".
- Tubo retangular receptor (tabelas 7.13/7.14): parede não verificada para nenhum perfil.
- `Solda.cs` (C#): o xg do U vem das chapas retas (no U dobrado 1,3 mm de diferença do catálogo); Ue ainda no retângulo.
- Texto fixo "A = 1,414·h·(b+d)" na ficha de cálculo da placa continua para todas as formas (o valor mostrado é o do U).
- Placa de base: no eixo forte, a excentricidade do centroide (fora do plano) não entra (método uniaxial).
- Gusset: sem verificação local da mesa/alma do U sob o filete do gusset (nem no I).
