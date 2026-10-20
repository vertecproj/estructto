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
| q perpendicular linear (início, fim) | 20 `carga_pontual` no meio de cada vigésimo (o motor só tem q global uniforme) |
| Peso próprio (modelo + caso marcado) | `carga_distribuida` −Z no caso, com a massa do catálogo (ou densidade × A), igual ao Nucleo.cs; `peso_proprio` do motor = null |
| Barra B | `bB`, tipo aço; `rot` = π se a barra aponta para a esquerda (eixo local z do motor = y local do Estructto) + π/2 se "deitado" |
| Rótulas | `params.rotula_i/j`; Timoshenko → `params.cortante` |
| Seção | criada pelo motor pela forma (TC, TR, I, U, L, RET) para ter todas as propriedades da NBR 8800; depois A, Iy (= Ix forte), Iz e Ay/Az trocados pelos do catálogo do Estructto (`PATCH /api/modelo/secao/...`). Seção genérica: explícita, J = Ix + Iy |
| Material | E, G, peso; fy e fu (fu: 450 MPa A572, 400 MPa A36/A500, senão 1,25·fy — o Estructto não guarda fu) |
| Casos | mesmo nome e tipo (G, Q, V, E). As combinações são as do motor (NBR 8681); as combinações digitadas no Estructto não vão |

Ordem: `PUT /api/modelo` (nós, materiais, casos) → `POST /api/modelo/lote` (seções, barras, cargas) → `PATCH` das seções.
Leitura: `POST /api/calcular`, `GET /api/resultados?alvo=deslocamentos|reacoes|esforcos&combinacao=...` para cada combinação e
`GET /api/verificacao` → `MotorResultado` (convenções do Estructto: ux, uy, giro, Rx, Ry, Rm; N tração +, V e M como `ResBarra`;
em pé M = −My e V = −Vz; deitado M = Mz e V = −Vy).

Não aceito ainda (erro claro, usar o motor interno): molas e recalques. Ignorado: carga giratória (só a posição lançada vai ao motor)
e o ψ informativo.

## Calcular (F5)

Opção **Motor** em Análise › Opções: "Interno (Nucleo.cs)" (padrão) ou "Motor: Python (NBR completas)". O interno sempre roda (é ele que
alimenta vista, diagramas, tabelas e placa). Com Python, o F5 também envia o modelo ao motor em segundo plano, com barra de progresso na
barra de status; no fim a barra de status e o histórico mostram as combinações, quantas barras reprovam e a taxa de cada barra
(critério, item da NBR 8800, combinação). O resultado fica em `MainWindow.ResMotor` para as próximas telas. `Motor.Desligar` roda ao
fechar o programa.

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
| sintético, caso G (com q perpendicular linear) | 3,3·10⁻³ (as 20 cargas pontuais) |

Exemplo, guincho-com-placa, caso G: Rx = 2,5396 kN, Ry = 8,4103 kN, M = −1,4091 kN·m no nó 1; topo da coluna ux = 12,2829 mm nos dois;
barra 1 em x = 0: N = −8,410, V = −2,540, M = 1,409 nos dois.

**A análise confere.** A única diferença é a da carga perpendicular linear, que o motor ainda não tem.

### Verificação de aço: métodos diferentes (esperado, não corrigido)

| Barra | Interno (Nucleo.cs) | Motor (NBR 8800) |
|---|---|---|
| escorada, coluna (b1/b2) | 1,344 — esbeltez KL/r ≤ 200 | 0,224 — interação 5.5.1.2, ELU G |
| escorada, escora (b3) | 1,925 — esbeltez L/r ≤ 300 | 1,918 — esbeltez 5.2.8 |
| escorada-teste, coluna / escora | 1,319 / 1,297 | 0,273 / 1,291 |
| com-placa, coluna b1 / b2 / escora | 0,709 (esbeltez) / 0,983 (flecha) / 1,454 | 0,165 / 0,162 / 1,448 |

Por que diferem:
1. **Segurança:** o interno usa tensões admissíveis com um "n" global (1,5 nos guinchos) sobre as cargas de serviço; o motor usa
   combinações ELU da NBR 8681 (γ e ψ) contra resistências de cálculo com γa1 = 1,10.
2. **Comprimento de flambagem:** o interno trata a barra contínua (b1 + b2 = coluna inteira) e o travamento fora do plano, com K de
   balanço e L do membro inteiro; o motor verifica cada barra com Lx = Ly = Lb = L da própria barra e k = 1 (`params.kx/ky/Lb` ainda não
   são enviados). Por isso a coluna reprova por esbeltez no interno (KL/r > 200) e passa folgada no motor.
3. **Esbeltez de barra tracionada:** os dois usam L/r ≤ 300; a pequena diferença (1,454 × 1,448) é o r da seção montada pelo motor (cantos
   vivos) contra o do catálogo.
4. **Flecha:** o interno põe a flecha (L/limite) no aproveitamento; o motor verifica deslocamentos à parte (ELS, `deslocamentos.verticais`,
   só vigas), fora da linha de verificação.
5. **Resistências:** o motor faz FLT/FLM/FLA, interação 5.5.1.2 e cortante pela NBR 8800; o interno faz σ = N/A + M/W, τ e flambagem de Euler.

## O que falta

- Mandar kx/ky/Lb da barra contínua e do travamento fora do plano (`params`) para o motor usar os mesmos comprimentos de flambagem.
- Carga perpendicular linear e carga giratória (posição a 180°) no motor; molas e recalques.
- Telas que mostrem o `MotorResultado` (diagramas, tabelas e verificação do motor no lugar dos do interno) — hoje só barra de status e histórico.
- Combinações digitadas pelo usuário (o motor só gera as da NBR 8681).
