# W10 — Solda direta: contorno inteiro, viga–viga de tamanhos diferentes, sem "no limite" pela perna mínima

Data: 27/10/2026. Branch `wt10-sd` (do `unificacao`). Tipo `solda_direta_alma` ("Solda direta").
Pedido do usuário, com duas capturas (UPN 50 na alma de UPN 50 e de UPN 100, apoio "alma viga", esforços do modelo
N −0,35, Fy 0,25, Fz 0,04, Mx −0,04, Mz −0,16, perna 3 mm):
"1. só conta a solda da alma, tem que soldar tudo" e "2. não funciona com dois tamanhos diferentes".

Código: `codigo/motor/prossen_estrutural/aco/ligacoes_flexiveis.py` (`_solda_direta`, `_posicao_sd`, `_contorno`,
`_cordoes`, `_grupo`, `_q_lado`, `_b_min_sd`, `_esforcos`, `_ler`, `_sug_sd`), `aco/ligacoes.py` (`otimizar`: descida e
`ALVO_DISP`), `codigo/Estructto/LigacaoDesenho.cs` (`z_topo_suporte`, rótulo do apoio).
Testes: `testes/test_w10_solda_direta.py` (novo, 12 testes, contas à mão na docstring).
Capturas: `ESTRUCTTO_CAPTURAS=w10sd` (`CapturasW10Sd.cs`, `82-w10sd-*.png` + `82-w10sd.txt`).
Antes: `C:\dev\wt\w10-antes\`. Depois: `C:\dev\wt\w10-depois\`.

## Causas raiz

| Sintoma | Causa |
|---|---|
| Só a alma soldada no U; "Soldar as mesas: auto" | `auto` = sim no I e **não no U** (regra de 26/10 para não mudar projetos antigos de UPN 50/65), sem aviso nem valor resolvido na tela |
| Com as mesas soldadas, o cálculo não era o do contorno | V só na alma e N/L num contorno sem as pontas das mesas; Vz, M, Mz e T **ignorados** ("rótula") mesmo com a ligação passando a transmitir momento |
| UPN 50 em UPN 100: 124 % "abaixo da mesa da viga suporte", Sugerir/Otimizar "sem solução" | a verificação comparava tf + r **da viga apoiada** com tf + r **do suporte**, topos sempre alinhados: com suporte mais alto sempre reprova, e nenhum campo a corrige. No apoio U (costas da alma para a viga, como o desenho W7-UPN) nem há mesa do lado da viga |
| UPN 50 em UPN 50: 100 % na mesma verificação | a mesma conta (14,5 = 14,5) |
| "Passa no limite" com esforços minúsculos | perna 3 mm = mínima da tabela 10 (1,00); o `otimizar` desce cada item "enquanto passa" e sempre termina na mínima |
| Apoio U em "alma viga" parece um L na elevação | o rótulo "UPN 100" ia dentro do retângulo do apoio, com fundo opaco, em cima da mesa de cima |
| Viga–viga com desnível desenhada com topos alinhados | o desenho punha o topo do suporte no topo da viga, sempre |

## Critério

### 1. Contorno inteiro (grupo de solda)

- **Padrão: "Soldar as mesas: sim"** (opções `sim` / `não`). `auto` saiu; um `.pcon` antigo com `auto` é lido como `sim`
  (`legado`). `não` continua possível: só as duas faces da alma, como antes (rótula, √(V² + N²)/(2·Lw), Vz/M/Mz/T
  ignorados com aviso) e o aviso "só a alma soldada".
- Cordão = contorno da seção da viga na face do apoio (I: 12 lados; U: 8), dos dois lados de cada chapa, inclusive as
  pontas das mesas. Lados sem face do apoio para a perna ficam sem solda, com aviso (mesa por fora no topo de um apoio
  da mesma altura).
- **Método elástico** (linhas de garganta unitária; Salmon & Johnson, Blodgett), NBR 8800 6.2.5 com a resultante:
  - L, centroide (yc, zc), Iy = Σ∫(z − zc)², Iz = Σ∫(y − yc)², Ip = Iy + Iz;
  - normal: |N|/L + |M|·|z − zc|/Iy + |Mz|·|y − yc|/Iz;
  - V só nos lados da alma (Lv) e Vz só nos lados horizontais (Lh): cada força pelas chapas no plano dela (como antes
    "a alma leva V");
  - T: |T|·r/Ip nas duas direções do plano;
  - termos somados em valor absoluto (a favor), em cada ponta de cada lado → maior q ≤ FwRd.
- 6.1.5.2 (mínimo de 45 kN): a resultante passa a incluir Vz (V, N e Vz escalados; momentos não).
- Outras verificações: metal base do apoio (q máximo), metal base da alma entre os filetes (V pela altura da alma na face,
  como o Aw = h·tw), escoamento da alma (h na face), tração na seção da face (A·fy) e, com recorte, as da viga recortada
  ([GD] 3.4.2 / AISC p. 9-7, as mesmas da viga–viga).
- **Perna mínima (tabela 10)**: a da junta mais grossa entre alma–apoio e mesa–apoio (cada uma pela menor espessura).
  W310 na mesa do W250: mesa 9,7 → 5 mm (antes 3, só pela alma).
- **Perna máxima de borda (6.2.6.2.2) saiu**: a ponta da viga contra a face do apoio é junta em T (mesma decisão do
  perfil–tubo, W8-TUBO).

### 2. Viga–viga (apoio alma de viga) de tamanhos diferentes

- Face do apoio: apoio I = altura livre entre as mesas, tf + máx(r, 10 mm) de cada lado ([GD] 2.4.6); apoio U = alma
  inteira (as costas da alma ficam para a viga, como o desenho W7-UPN).
- Campo novo **`desnivel`** ("Topo viga abaixo do apoio", mm, padrão `auto`; texto na tela): só na alma de viga.
  - `auto`: se a viga + 2b cabe na faixa, desce o mínimo (obstáculo + b, para a perna por cima da mesa superior),
    arredondado a 5 mm quando ainda cabe: UPN 50 em UPN 100 → 5 mm; W200 em W310 → 9,7 + 10 + 4 = 23,7 → 25 mm. Aviso
    com o valor. Se não cabe: topos alinhados (0).
  - número: o informado (0 = topos alinhados).
- Com o desnível final, o que da viga fica fora da faixa no apoio I é **recortado** (cope): altura = o que passa + b
  (o retorno do cordão na borda do recorte fica na face), no mínimo a mesa inteira com o raio; comprimento = bf/2 − tw/2 +
  10 − folga ([GD] 3.3). W310 em W310: 23,7 / 23,7 mm × 87,6 mm; só a alma que sobra é soldada (filete fechado).
- Apoio U mais baixo que a viga ou da mesma altura: nada avança para a viga; o que fica fora da face não é soldado (aviso).
- A verificação antiga "solda: abaixo da mesa da viga suporte" saiu: a geometria final (desnível + recorte) é que define o
  cordão.
- Desenho: `geometria.z_topo_suporte` (mm, contrato) põe o apoio na altura certa. A viga–viga com recorte e desnível também
  manda a chave (antes o suporte saía alinhado com a viga). O rótulo do apoio vai acima dele quando há espaço.

### 3. Esforços pequenos e "no limite"

- Como no W8-TUBO: o padrão (`sugerir`) usa a menor perna comercial **acima** da mínima que passa; a mínima digitada
  continua "no limite" (é verdade).
- `otimizar` (todos os tipos, `ligacoes.py`): a descida "menos material enquanto passa" não aceita um passo que leve uma
  disposição construtiva acima de 0,90 (`ALVO_DISP`, = ALVO do W8-RIG/W8-FLEX) quando ela estava abaixo; no fim, uma
  disposição mínima no limite com `parametro` (ex.: perna) sobe um degrau se isso a tira do limite e continua passando.
  Na captura do usuário, Otimizar leva 3 → 4 mm (0,75).

## Resultado

| Caso | Antes | Depois |
|---|---|---|
| UPN 50 / UPN 50, alma viga, esforços do modelo, 3 mm | 100 % "dimensão mínima" (passa no limite; só a alma) | 117 % (contorno: N 36,5 + Vz 26 kN do mínimo de 45 kN agora entram no grupo, Mz e T também); Sugerir/Otimizar → 4 mm, **88 %**, topos alinhados, mesas por fora sem solda (aviso) |
| UPN 50 / UPN 100, idem, 3 mm | **124 %** "abaixo da mesa", Sugerir 10 mm (200 %), Otimizar sem solução | 100 % (mínima digitada); Sugerir/Otimizar → 4 mm, **75 %**, desnível 5 mm, contorno inteiro fechado |
| W200×15 / W310×38,7, alma viga, V 50 | 130 % "abaixo da mesa"; Sugerir 10 mm (233 % na perna máxima) | 75 %, desnível 25 mm, contorno do I inteiro |
| W310 / W310, alma viga, V 50 | 100 % ("abaixo da mesa" = 19,7) | 75 %, mesas recortadas 23,7 mm, filete fechado na alma |
| Padrão de fábrica W310 / W250×73 mesa pilar, V 100 | 75 % (perna 4, mínima 3 pela alma) | **83 %** (perna 6, mínima 5 pela mesa; contorno 19 %) |

Contas à mão em `test_w10_solda_direta.py`: I com momento (L 1268,4 mm, Iy 19 615 506 mm³, q 254,5 kN/m no topo da alma)
e o caso do usuário (L 242 mm, q 307,2 kN/m na ponta da mesa, 0,50 com 4 mm).

## Testes antigos trocados (com justificativa escrita em cada um)

- `test_solda_mesas.py`: contorno com as pontas das mesas (1229,6 → 1268,4 mm), nome da verificação do contorno, `auto`
  no U agora = sim.
- `test_w8_tubo.py`: padrão 4 → 6 mm e 0,75 → 0,83 (mínima da mesa); sem perna máxima (UPN 120 vai a 6 mm).
- `test_w8_flex.py`: solda direta ≤ 0,83 (era ≤ 0,75).
- `test_ligacoes_u.py`: o filete do padrão contorna o U inteiro (3 y, 4 z; com `não` o antigo fechado na alma); U em
  todos os tipos: a solda direta vai das costas à ponta das mesas; W310 na alma de W360 com `x_max` 60: o sugerir (V 50)
  troca para a solda direta recortada; o lote (N 50, V 50, M 30) continua "barra curta" (o M agora entra no grupo).
- `test_ligacoes_ui.py`: "nenhum item desce sem reprovar" passa a aceitar a parada da descida no limite de 0,90 das
  disposições.

## Limites

- Apoio U na alma de viga sempre com as costas para a viga (o desenho também). Viga chegando pelo lado das mesas do U:
  o 3D do modelo já manda a face (`x_face`) nas pontas das mesas; o recorte para entrar no U não é calculado.
- Momento na viga e no apoio (com as mesas soldadas a ligação é rígida) não é verificado: só a solda (aviso).
- O mínimo de 45 kN (6.1.5.2) escala V, N e Vz pela resultante: com Vz do modelo maior que V, a força lateral mínima
  domina a solda das mesas (é o caso do usuário).
- `comprimento_solda` só vale com "soldar as mesas: não" (com as mesas, o cordão é o contorno; aviso).
