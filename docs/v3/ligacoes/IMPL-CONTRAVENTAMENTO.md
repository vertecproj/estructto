# Ligações de contraventamento e de treliça — implementação

Módulo: `codigo/motor/prossen_estrutural/aco/ligacoes_contraventamento.py`. Testes: `codigo/motor/testes/test_ligacoes_contraventamento.py`.
Segue o contrato `docs/v3/LIGACOES-CONTRATO.md`. Data: 20/10/2026. Norma: NBR 8800:2008.

## Registro

- O módulo expõe `TIPOS` (formato do contrato) e entra em `ligacoes._MODULOS_CONTRATO`, o mecanismo de agregação da família flexível.
  - `tipos_ligacao()` lista os tipos.
  - `verificar_ligacao()` despacha para `TIPOS[tipo]["verificar"]`.
- A descrição longa de cada entrada fica na chave `ajuda`, porque `tipos_ligacao()` gera `descricao` a partir do `rotulo`.

## Convenções

- **Entradas** em mm, kN e graus. Os cálculos usam as primitivas de `ligacoes.py`, em unidades SI (m, kN, kN/m²).
- **Esforços:** `N > 0` é tração. O nó usa `N` (barra 1) e `N2`, `N3`, `N4`.
- **Resultado:** segue o contrato (`ok`, `taxa`, `verificacoes` com `item` = "NBR 8800 x.y" e `ok`, `avisos`, `geometria`).
  - Leva também `taxa_max`, `criterio` e `item`, que a `JanelaLigacao` lê.
  - Extras: `forcas_interface` (gusset e nó), `dica_barra` (cantoneiras) e `Ab_mm2` (tirante).
- **Geometria:** em mm, com o ponto de trabalho na origem e x ao longo da barra 1.
  - O gusset fica no plano x–z, com `normal` = (0, −1, 0) e `u` = (1, 0, 0). O contorno é dado em (u, normal × u) = (x, z).
  - O contorno é a envoltória convexa de três conjuntos de pontos: as interfaces, a região ligada de cada barra e as pontas da seção de Whitmore (com folga de 2·tg).
  - Parafusos têm eixo −y. Soldas são polilinhas nas faces y = ±tg/2. O tubo recebe o recorte `rasgo`.
- **Disposições construtivas** têm unidade "mm" e taxa = mínimo/real ou real/máximo. A taxa geral é a maior de todas.

## Tipos e verificações

### `gusset_contraventamento`
A diagonal pode ser L, 2L, U, tubo circular ou retangular, ou barra redonda. Ela é parafusada (só L, 2L e U) ou soldada ao gusset. O gusset se liga a viga+pilar, só ao pilar, só à viga ou à base.

| Verificação | Item |
|---|---|
| Barra: escoamento da seção bruta | 5.2.2 a) |
| Barra: ruptura da seção líquida, Ae = Ct·An | 5.2.2 b), 5.2.3, 5.2.5 |
| Ct ≥ 0,60 | 5.2.5 |
| Barra: colapso por rasgamento (aba ou alma) | 6.5.6 |
| Parafusos: cisalhamento (2 planos na 2L), contato no gusset e no perfil (soma furo a furo), grupo Σ min(Fv; Fc), ligação longa | 6.3.3.2, 6.3.3.3, 6.3.8 |
| Soldas por linha. Na cantoneira, partilha de momento nulo no centroide: o filete do canto leva F(a − ec)/a | 6.2.5, tabela 8 |
| Soldas: perna mínima, máxima na borda e comprimento mínimo | 6.2.6.2.1 a 6.2.6.2.3 |
| Gusset: Whitmore (espraiamento de 30°), escoamento e ruptura (An ≤ 0,85·Ag na parafusada) | 6.5.3 → 5.2.2 |
| Gusset: colapso por rasgamento (parafusada com ≥ 2 linhas; soldada pelas linhas de solda) | 6.5.6 |
| Gusset comprimido: K·L1/r, com r = t/√12. L1 vai do centro da linha de Whitmore à borda apoiada, ao longo do eixo (Thornton); K = 0,65 por padrão. KL/r ≤ 25 dá escoamento; acima, χ da 5.3.3 com Q = 1 | 6.5.4 → 5.3 |
| Interfaces: filete duplo com fator de ductilidade (padrão 1,25, AISC parte 13), cisalhamento da borda do gusset e força normal + momento | 6.2.5, 6.5.5, 6.5.3 |
| Espaçamentos e bordas | 6.3.9, 6.3.10, 6.3.11.1 (tabela 14), 6.3.12 |
| Geometria: a região ligada não invade a viga nem o pilar | detalhamento |

**Distribuição de força nas interfaces:**
- **viga-pilar:** método da força uniforme, caso especial: β = Lv/2 e α pela condição α − β·tgθ = eb·tgθ − ec. Quando α ≠ ᾱ = Lh/2, surge Mb = Vb·|α − ᾱ|.
- **pilar, viga e base:** divisão direta. As componentes passam pelo ponto em que o eixo da diagonal cruza a face, e o momento vem da distância desse ponto ao centro da interface.

**Automático (valor 0 na entrada):**
- **recuo:** a região ligada fica a 2·tg das faces, e a seção de Whitmore cabe no quadrante.
- **Lh e Lv:** cobrem a região ligada e a seção de Whitmore.
- **L1:** calculado pela geometria.

### `no_trelica_gusset`
- Até 4 barras (sufixos `_1` a `_4`), cada uma com seu ângulo em relação ao banzo.
- **Banzo contínuo:** gusset soldado à face, a `e_banzo` do eixo. A solda e a borda do gusset recebem ΣN·cosθ (paralela), ΣN·senθ (normal) e o momento H·e_banzo.
- **Banzo emendado:** os pedaços do banzo entram como barras a 0° e 180°, e a soma das forças é conferida.
- Cada barra recebe as verificações da diagonal acima, com no mínimo 45 kN.
- Eixos concorrentes: o momento no nó é desprezado (sem fadiga; Pfeil p.231–233).

### `tirante_barra_redonda`
- Barra: tração na área efetiva da rosca, 0,75·Ab·fu/γa2, limitada a Ab·fy/γa1 (6.3.3.1 e 5.2.2 a). Compressão reprova.
- **Rosca e porca:** a porca, a arruela e a flexão da chapa sob a porca ficam em aviso.
- **Garfo e pino:**
  - Pino tratado como parafuso em 2 planos (6.3.3.2).
  - Contato e rasgamento no furo do gusset (6.3.3.3).
  - Seção do gusset no pino (6.5.3 → 5.2.2).
  - Borda além do pino (6.3.11.1).
  - O garfo e o esticador são peças de catálogo (aviso).
- 5.2.8: barra redonda pré-tracionada dispensa L/r ≤ 300 (aviso).

### `cantoneira_simples_trelica`
- Ligação por uma aba, com parafusos ou filetes. O mínimo é de 45 kN.
- A excentricidade é desprezada na ligação (NBR 8800 6.1, ação estática; sob fadiga, aviso para usar modelo de pórtico).
- Na barra, a excentricidade entra assim:
  - **Tração:** pelo Ct = 1 − ec/lc, com ec = ȳ da aba ligada.
  - **Compressão:** pela E.1.4, pela função `nbr8800.ne_cantoneira_uma_aba` já existente.
- `dica_barra` = `{ligacao_uma_aba: {aba, trelica, L}, Ct, ec_mm, E14?}`, pronto para `nbr8800.compressao(..., ligacao_uma_aba=...)`.
- Com `comprimento` > 0 e N < 0, a ligação já verifica a barra pela E.1.4.

### `sugerir()`
- Parte das propriedades dos perfis. No gusset, `perfil_viga` e `perfil_pilar` do catálogo dão eb e ec pela meia altura.
- Fixa o padrão de bordas e de espaçamento (3d) e limita a perna do filete à máxima na borda.
- Depois procura o primeiro conjunto aprovado, crescendo nesta ordem: t_gusset (6,3 a 25 mm), número de parafusos por linha ou comprimento do filete, e perna da interface.
- No tirante, procura o primeiro diâmetro comercial, de 3/8" a 2 1/2".

## Exemplos de livro (teste × livro, tolerância 1 %)

| Exemplo | Grandeza | Livro | Motor |
|---|---|---|---|
| Pfeil 3.5.2 (p.79–81), 2L 63×6,3 com 5 A307 φ12,7 em gusset de 6,3 mm | corte dos parafusos | 156 kN | 155,8 |
| | contato no gusset | 267,9 | 267,7 |
| | escoamento | 349 | 349,1 |
| | ruptura (Ct = 0,887) | 350 | 350,2 |
| | bloco | 338 | 336,6 |
| Pfeil 8.7.1 (p.238–239), gusset de 8 mm | Whitmore | 285 kN | 285,1 |
| | Ct do banzo e ruptura | 0,68; 465 kN | 0,684; 465,3 |
| | filetes balanceados B6 | 57 e 23 mm | 56,6 e 23,2 |
| | filetes balanceados D2 | 41 e 16 mm | 41,2 e 16,3 |
| | bloco, com a fórmula do livro | 355 kN | 355,6 |
| Pfeil 2.3.5 (p.57), tirante A36 com Nd = 210 kN | diâmetro | φ 34,9 mm (Ag = 9,58 cm²) | `sugerir` → 34,925 mm; Ft,Rd = 212,9 kN |
| Pfeil 2.3.7 e 3.5.3, Ct do U | Ct | 0,73 e 0,877 | idem |

Também há cálculos à mão no teste: UFM (equilíbrio e condição de α), flambagem do gusset (KL/r e χ), garfo e pino, Thornton L1 e momento na interface do pilar.

## Divergências do livro (não copiadas)

1. **Bloco do gusset em 8.7.1:** o livro usa 2·l1 = 2·100 mm nas duas linhas de cisalhamento, mas os filetes têm 100 e 40 mm. O motor usa l1 + l2 e Ant = largura da aba (76 mm), o que dá 304,6 kN. É a favor da segurança.
2. **2L 44 em 8.7.1:** "0,82·2·4,0·40/1,35 = 48,6 kN", mas a conta dá 194 kN. Não foi usado.
3. **Filetes de D2:** o livro dá as distâncias 2,29 e 0,91 cm, que somam 3,2 cm, e não a aba de 44 mm. O teste usa os números do livro só para a partilha.
4. **Garganta da solda:** o livro usa 0,7b; o motor usa 0,707b (primitiva de `ligacoes.py`), uma diferença de cerca de 1 %.

## Limitações e pendências

- **[confirmar] na NBR 8800:2008:**
  - O subitem da 6.1 com a resistência mínima de 45 kN e a dispensa da excentricidade de cantoneiras. Só a Pfeil, p.232, cita "NBR 8800" sem número.
  - A 6.5.4 (KL/r ≤ 25).
  - O teto de 0,90 do Ct, aplicado só a perfis abertos e barra redonda.
- **Solda de barra redonda:** garganta 5/16·R (superfície curva), tirada do AISC 360 tab. J2.2, com aviso.
- **Rasgo do tubo:** t_gusset + 2 mm.
- **Não verificados (em aviso):**
  - alma da viga e do pilar sob Vb, Hc e Hb, e a ligação viga–pilar;
  - flambagem da borda livre do gusset e fadiga;
  - placa de base e chumbadores (`placa_base.py`);
  - porca, garfo e esticador;
  - Whitmore recortada pela chapa: só há aviso, sem redução da largura.
- **Fora do escopo:**
  - furação alternada;
  - furos alargados ou alongados;
  - atrito;
  - efeito de alavanca;
  - tubo ou barra parafusados (dão ValueError);
  - filete transversal de ponta.
- **Interface:** a `JanelaLigacao` prefixa "8800 " ao `item`. Com o formato do contrato aparece "8800 NBR 8800 …", e quem mantém a janela precisa ajustar.
