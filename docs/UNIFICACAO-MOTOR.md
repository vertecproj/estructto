# Ponte Estructto ↔ motor Python (fase 2 de UNIFICACAO.md)

Data: 20/10/2026. Código: `codigo/Estructto/MotorModelo.cs` (conversão e leitura), `Motor.cs` (servidor HTTP), opção **Motor** na aba
Análise › Opções. `Nucleo.cs` não mudou.

## Como o modelo vai para o motor

| Estructto (pórtico plano XY) | Motor (3D, ESPEC §5) |
|---|---|
| Nó N (X, Y) | `nN` (x = X, y = 0, z = Y) |
| Apoios X, Y, giro | apoio `[ApoioX, true, ApoioY, true, ApoioGiro, true]`: uy, rx e rz travados em todos os nós (fora do plano) |
| Fx, Fy, Mz no nó | `carga_nodal` F = [Fx, 0, Fy, 0, −Mz, 0] (giro anti-horário em XY = −ry) |
| q horizontal / vertical | `carga_distribuida` q = [qx, 0, qy] |
| q perpendicular linear (início, fim) | `carga_distribuida` q = (−S·qi, 0, C·qi), q2 = (−S·qf, 0, C·qf): carga linear do motor (abaixo) |
| Peso próprio (modelo + caso marcado) | `carga_distribuida` −Z no caso, com a massa do catálogo (ou densidade × A), igual ao Nucleo.cs; `peso_proprio` do motor = null |
| Barra B | `bB`, tipo aço; `rot` = π se a barra aponta para a esquerda (eixo local z do motor = y local do Estructto) + π/2 se "deitado" |
| Rótulas | `params.rotula_i/j`; Timoshenko → `params.cortante` |
| Flambagem | `params.kx/ky/Lb` de `Flambagem.Calcular` (Nucleo.cs): kx = Le/L do plano do eixo forte, ky = Le/L do eixo fraco (barra contínua inteira, travamento fora do plano, K ≥ 1); Lb = maior trecho destravado do plano do eixo fraco (fora do plano em pé, no plano se "deitado"). Plano instável (Le = ∞) não vai: o motor fica com k = 1 |
| Seção | criada pelo motor pela forma (TC, TR, I, U, L, RET) para ter todas as propriedades da NBR 8800; depois A, Iy (= Ix forte), Iz e Ay/Az trocados pelos do catálogo do Estructto (`PATCH /api/modelo/secao/...`). Seção genérica: explícita, J = Ix + Iy |
| Material | E, G, peso; fy e fu (fu: 450 MPa A572, 400 MPa A36/A500, senão 1,25·fy — o Estructto não guarda fu) |
| Casos | mesmo nome e tipo (G, Q, V, E). As combinações são as do motor (NBR 8681); as combinações digitadas no Estructto não vão |

Ordem: `PUT /api/modelo` (nós, materiais, casos) → `POST /api/modelo/lote` (seções, barras, cargas) → `PATCH` das seções.
Leitura: `POST /api/calcular`, `GET /api/resultados?alvo=deslocamentos|reacoes|esforcos&combinacao=...` para cada combinação e
`GET /api/verificacao` → `MotorResultado` (convenções do Estructto: ux, uy, giro, Rx, Ry, Rm; N tração +, V e M como `ResBarra`;
em pé M = −My e V = −Vz; deitado M = Mz e V = −Vy).

Molas e recalques (20/10/2026): `molas` = [MolaX, 0, MolaY, 0, MolaGiro, 0] (kN/m, kN·m/rad; giro em ry) e `recalques` =
[RecalqueX, 0, RecalqueY, 0, 0, 0]/1000 (mm → m; só onde o GL está travado, como o Nucleo.cs). No motor (`No.molas`, `No.recalques`,
globais) a mola entra na diagonal de K (estática, P-Δ, modal; reação = −k·u) e o recalque é deslocamento prescrito, ação permanente
do 1º caso tipo G (o Nucleo.cs aplica em todos os casos; com mais de um caso o motor não soma o recalque várias vezes). Recalque em GL
livre → ValueError. Conferência com o Nucleo.cs (balanço em mola de giro, viga em 2 molas, biengastada e contínua com recalque):
diferença relativa máxima 1,4e-15. Ignorado: carga giratória (só a posição lançada vai ao motor) e o ψ informativo.

## Carga distribuída linear no motor

`CargaDistribuida.q2` (opcional, global, kN/m): q vale `q` em ni e `q2` em nj, linear. Sem `q2` nada muda (nem o JSON gravado).
Solver (`solver.py`): o elemento guarda a parcela triangular local `dq` (0 em ni, dq em nj) ao lado da uniforme `q`.
- Engastamento perfeito `_engaste_linear`: giros da biapoiada travados pela rigidez de Timoshenko, M_i = wL²(12 + 15Φ)/(360(1 + Φ)),
  M_j = wL²(18 + 15Φ)/(360(1 + Φ)) (Φ = 0: wL²/30 e wL²/20), R_i = wL/6 + (M_i − M_j)/L (3wL/20), axial wL/6 e wL/3. Treliça: wL/6 e wL/3.
- Esforços (`_secoes`): resultante x²/(2L) e momento x³/(6L) da parcela triangular em [0, x]. Flechas: `_flecha_linear` (biapoiada
  x(7L⁴ − 10L²x² + 3x⁴)/(360L) + cortante, menos os giros travados; Φ = 0 dá x²(L − x)²(x + 2L)/(120L)); axial w·x(L² − x²)/(6L·EA).
- Barra com trecho rígido e `q2` → ValueError claro (não suportado).
- `projeto.py`: aceita `q2` no lote/JSON; forças horizontais por pavimento usam as reações da biapoiada do trapézio.
Testes (`testes/test_solver.py`, `TestCargaLinear`, valores à mão): biengastada triangular (R 10,8 e 25,2 kN, M 14,4 e 21,6 kN·m,
flecha wL⁴/768EI), biapoiada trapezoidal 4 → 10 kN/m (R 18 e 24 kN, M(3) = 31,5 kN·m, V(3) = 1,5 kN, flecha), axial linear, elemento
exato (uma barra × duas barras, Euler e Timoshenko), treliça e erro do trecho rígido, JSON ida e volta.

## Calcular (F5) e telas

Opção **Motor** em Análise › Opções: "Interno (Nucleo.cs)" (padrão) ou "Motor: Python (NBR completas)". O interno sempre roda. Com Python,
o F5 também envia o modelo ao motor em segundo plano, com barra de progresso na barra de status; no fim a tela passa a mostrar
**"Python: envoltória ELU (NBR 8681/8800)"** na lista Análise › Resultado › Mostrar, que também ganha "Python: envoltória ELS" e cada
combinação do motor ("Python ELU: ..."). Com essas escolhas, diagramas (deformada, N, V, M), tabelas de nós e barras, reações, inspetor
e relatório usam o `MotorResultado` (`MotorModelo.ParaResBarra` remonta o `ResBarra` a partir de N e V do motor em x = 0, L/2 e L e dos
deslocamentos dos nós), e o % Aprov./mapa de calor e a tabela de verificação mostram a taxa NBR 8800 do motor (pior linha da barra:
critério, item, combinação crítica; as colunas Tensão/Corte/Flecha/Flambagem do interno ficam "—"). Qualquer edição do modelo descarta o
resultado do motor (volta para o caso ativo; calcular de novo). Placa de base, soldas e "Dimensionar" continuam no interno.
Captura: `40-motor-python-momento`, `40-motor-python-mapa-calor`, `40-motor-python-reacoes` (guincho-com-placa). `Motor.Desligar` roda
ao fechar o programa.

## Conferência interno × motor

`Estructto.exe --conferir-motor <saida.txt> <arquivos.estructto>`: para cada caso de carga, reações, deslocamentos e N/V/M em 5 pontos de
cada barra (`GET /api/analise/caso/...`), e a verificação das duas formas. Rodado com os três `exemplos/guincho-*.estructto` e um pórtico
sintético (barra para a esquerda, barra deitada, rótula, q vertical, q perpendicular linear, dois casos):

| Modelo | Maior diferença relativa (reações, deslocamentos, N, V, M) |
|---|---|
| guincho-coluna-escorada | 7,1·10⁻¹² |
| guincho-coluna-escorada-teste | 7,1·10⁻¹² |
| guincho-com-placa | 3,6·10⁻¹² (também com o `estructto-motor.exe` empacotado) |
| sintético, caso Q (sem q perpendicular) | 2,3·10⁻¹⁰ |
| sintético, caso G (com q perpendicular linear) | 4,5·10⁻¹⁰ (antes 3,3·10⁻³ com as 20 cargas pontuais) |

Exemplo, guincho-com-placa, caso G: Rx = 2,5396 kN, Ry = 8,4103 kN, M = −1,4091 kN·m no nó 1; topo da coluna ux = 12,2829 mm nos dois;
barra 1 em x = 0: N = −8,410, V = −2,540, M = 1,409 nos dois.

**A análise confere**, agora também com a carga perpendicular linear (carga linear do motor no lugar das 20 cargas pontuais).

### Verificação de aço (com kx/ky/Lb do Nucleo.cs enviados ao motor)

| Barra | Interno (Nucleo.cs) | Motor (NBR 8800), antes (k = 1, L da barra) | Motor, agora |
|---|---|---|---|
| escorada, coluna (b1/b2) | 1,344 — esbeltez KL/r ≤ 200 | 0,224 — interação 5.5.1.2 | **1,344** — esbeltez 5.3.4 |
| escorada, escora (b3) | 1,925 — esbeltez L/r ≤ 300 | 1,918 — esbeltez 5.2.8 | 3,835 — esbeltez 5.2.8 |
| escorada-teste, coluna / escora | 1,319 / 1,297 | 0,273 / 1,291 | **1,319** / 2,581 |
| com-placa, coluna b1 / b2 / escora | 0,709 (esbeltez) / 0,983 (flecha) / 1,454 | 0,165 / 0,162 / 1,448 | 0,187 / 0,177 (interação 5.5.1.2) / 2,897 |
| sintético, b1 / b2 / b3 / b4 | 0,966 / 0,281 / 99,99 / 99,99 | — | 0,283 / 0,315 / 0,078 / 0,035 |

Os dois motores usam agora os mesmos comprimentos de flambagem: a esbeltez KL/r das colunas sai idêntica (1,344 e 1,319). O que ainda
difere, e por quê:
1. **Segurança:** o interno usa tensões admissíveis com um "n" global (1,5 nos guinchos) sobre as cargas de serviço; o motor usa
   combinações ELU da NBR 8681 (γ e ψ) contra resistências de cálculo com γa1 = 1,10. Na com-placa a coluna passa por esbeltez KL/r
   = 0,709·200 = 142 nos dois, e o motor fica com a interação (0,187), que é o critério de esforço.
2. **Esbeltez de barra tracionada (escoras):** o interno usa L/r com L = maior trecho destravado (regra de tração, sem K); o motor
   (`nbr8800.verificar_barra_aco`) usa max(Lx/rx, Ly/ry) = K·L/r também na tração. Com o K ≈ 2 da escora fora do plano enviado agora,
   a taxa do motor dobrou (1,918 → 3,835). Pela 5.2.8, L é o comprimento destravado (sem K): o interno está certo; corrigir no motor
   é mudança de cálculo de aço fora do escopo desta tarefa (fica no "O que falta").
3. **Plano instável:** nas barras b3/b4 do sintético o interno acha um plano sem travamento (K = ∞, taxa 99,99); esse plano não é enviado
   e o motor usa k = 1 nele.
4. **Flecha:** o interno põe a flecha (L/limite) no aproveitamento; o motor verifica deslocamentos à parte (ELS, `deslocamentos.verticais`,
   só vigas), fora da linha de verificação.
5. **Resistências:** o motor faz FLT/FLM/FLA, interação 5.5.1.2 e cortante pela NBR 8800; o interno faz σ = N/A + M/W, τ e flambagem de Euler.

## O que falta

- Motor: esbeltez de tração (5.2.8) com L destravado em vez de K·L; plano instável (K = ∞) como reprovação.
- Carga giratória (posição a 180°) no motor; molas e recalques; carga linear em barra com trecho rígido.
- Verificação detalhada do motor na janela "Verificar barra" (hoje: só taxa e critério na tabela e no mapa).
- Combinações digitadas pelo usuário (o motor só gera as da NBR 8681).
