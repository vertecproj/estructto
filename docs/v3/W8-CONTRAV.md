# W8-CONTRAV — gusset de contraventamento, nó de treliça com gusset e tirante de barra redonda

Data: 27/10/2026. Branch `wt8-contrav`. Pedido: "corrija os outros padrões ruins" — os três padrões que a W7-LIG
(`docs/v3/W7-LIG.md` §3, linhas 17–19) deixou anotados como não corrigidos. Modelo seguido: a cantoneira simples da W7-LIG.
Arquivo principal: `codigo/motor/prossen_estrutural/aco/ligacoes_contraventamento.py`.

Capturas: `ESTRUCTTO_CAPTURAS=w8contrav` (`CapturasW8Contrav.cs`, `80-w8contrav-*` + `80-w8contrav.txt`). A captura falha
se algum dos três padrões de fábrica passar de 0,90 ou reprovar, e se o tubo escolhido no seletor não for desenhado.

## 1. O que estava errado (padrão de fábrica, `70-w7lig-padroes.txt` de antes)

| Tipo | Antes | Depois |
|---|---|---|
| Gusset de contraventamento | campos soltos zerados: Outra aba 0, mesa do U 0, Área 0, ec 0 (cantos vivos sem mostrar); sem seletor; a diagonal **não era desenhada** (só gusset e parafusos); elevação girada (diagonal na horizontal, gusset em losango); taxa **0,967** "no limite" por borda 30 mm × Tab. 14 = 29 | `perfil_diagonal` do catálogo (`L 3" x 1/4"`) com seletor, arranjo simples/dupla e aba ligada; A = 9,29 cm² tabelada, ec = 21,4 mm, Ct = 0,786; diagonal desenhada com as duas abas na face do gusset, talão a g = 45 mm da linha de furos; viga na horizontal e pilar na vertical; espraiamento de Whitmore tracejado (115 mm); taxa **0,864** (espaçamento 2,7·db = 43,2 ≤ 50) |
| Nó de treliça com gusset | 4 × 7 campos soltos (zerados) por barra; `e_banzo` = 0 (gusset no eixo do banzo); só uma barra (2L 63,5 × 6,35) e as barras e o banzo **não desenhados**; taxa **0,967** pela mesma borda | `perfil_barra_1..4` + `arranjo_i` (dupla/simples/nenhuma) + `perfil_banzo` (I); nó do banzo inferior de uma Pratt: banzo W150×18 contínuo, diagonal 2L 2½"×¼" a 45° tracionada (100 kN) e montante 2L 2½"×¼" a 90° comprimido (−70,7 kN): ΣV = 0, a interface leva ΣH = 70,7 kN; e_banzo = d/2 = 76,5 mm (do perfil); banzo, 2L e Whitmore desenhados, banzo na horizontal; taxa **0,864** |
| Tirante de barra redonda | barra **não desenhada** (só chapa e uma "porca" desenhada como cabeça de parafuso); `comprimento` = 0; chapa desenhada no eixo dela (meia espessura para cada lado) | barra Ø25,4 (tubo maciço) com a linha da rosca (núcleo ≈ 0,85·D), chapa de apoio, arruela, porca pesada e contraporca na ordem real, 3 fios além da contraporca; comprimento padrão 6000 mm; cotas (pilha 6,3 + 4 + 25,4 + 15,2, rosca 112, barra Ø25,4 · L = 6000); taxa **0,888** (tração na rosca, 6.3.3.1) |

## 2. Critério adotado

### Barras do catálogo (gusset e nó)

- `perfil_barra(nome)`: L (pela `cantoneira_catalogo` da W7-LIG: abas, t, A tabelada, ec = x̄), U laminado/UPN (h, bf, tw,
  tf, A tabelada, ec = xg da costa da alma), tubo circular e retangular Vallourec (D/H, B, t, A tabelada; ec calculado
  como antes, 5.2.5 c/d). As medidas entram nas chaves antigas (`dim_a`, `t_perfil`…) e o cálculo segue o mesmo.
- Lista do seletor (`perfis` do campo, sem mudança na tela): L laminadas, U laminado e UPN, tubos Vallourec. Fora: U/Ue
  formados a frio e metalon (parede fina; NBR 14762, o cálculo daqui é NBR 8800). Banzo: perfis I.
- `arranjo`: dupla = 2L costas com costas, gusset entre elas (2 planos de corte, 2 áreas); simples = 1 perfil numa face ou
  tubo com rasgo. Dupla com U/tubo é erro. Trocar o perfil no seletor refaz o pré-dimensionamento (campo `resugerir`).
- Tubo escolhido → ligação soldada (rasgo); perna do filete até 90 % da máxima na borda (6.2.6.2.2): parede de 5 mm →
  4 mm (antes nascia 5 = "no limite").
- **Legado**: dados com `dim_a` e sem perfil seguem o esquema antigo (`_GUSSET_LEG`, `_NO_LEG`) com os mesmos números
  (gusset e nó antigos continuam 0,967); o Realista manda medidas (`Render3DMotor.PerfisGusset`) e não muda.

### Folgas e disposições (só no esquema novo)

| Item | Regra | Fonte |
|---|---|---|
| Bordas e1 e e_gusset | Tab. 14 + 3 mm de tolerância de corte, para cima a 5 mm (35 mm com Ø16) | NBR 8800 tab. 14; mesma regra da W7-LIG |
| Gabarito da L | usual da aba (45 mm na L76, 35 na L63) | AISC tab. 1-7A [conferir]; Gerdau 2.4.5 |
| Folga da região ligada às faces do apoio/banzo | max(2·tg, 25 mm) (antes 2·tg = 12,6 → "ligação afastada do pilar" a 0,915) | 2·tg: linha de dobra (AISC parte 13); 25 mm: folga usual de montagem e de chave [conferir] |
| Região ligada da L | cobre a aba inteira: talão a g_aba (+ (n−1)·g/2) da linha de furos, não dim_a/2 (corrigido no ponto comum `_membro`; com g_aba = 0 nada muda) | geometria |
| Nó: barras sem choque | o recuo automático de cada barra cresce de 5 em 5 mm até a região ligada dela ficar a ≥ folga das já postas (separação por eixos, `_cruzam`) | geometria |
| Nó: eixo do banzo à face | d/2 do `perfil_banzo` (gusset soldado na face da mesa, alma no plano da treliça) | geometria |
| Gusset | t ≥ t do perfil (6,3 serve na aba de ¼") | W7-LIG |

### Tirante (desenho; o cálculo não mudou)

Proporções usuais das peças comerciais [conferir no fabricante]: porca sextavada pesada ASTM A563 / ASME B18.2.2
(abertura 1,5·D + 3,2 mm, altura ≈ D), contraporca 0,6·D, arruela ASTM F436 Ø 2·D × 4 mm, 3 fios ≈ 0,4·D além da
contraporca, rosca até 2·D além da chapa (ajuste). Garfo: pino no nó, gusset com a borda `borda_pino` no sentido da
força, orelhas (t_orelha; 0 = 0,5·d só no desenho) com a folga de cada lado e o corpo do garfo.
Comprimento padrão 6000 mm (X de cobertura entre tesouras a ~6 m): só entra no aviso de esbeltez (5.2.8) e na cota.

## 3. Contrato da geometria (só o projetista desenha; o Realista desenha a barra do modelo)

- `perfis_barra`: placas no formato de `chapas` (origem numa face, normal, u, contorno, espessura) com `perfil`, `campo` e
  `camada` opcional (0 = perfil, por baixo do gusset, cor de perfil, rótulo como o do apoio; senão por cima do gusset, como
  a cantoneira da W7-LIG). A L simples encosta na face +y, a 2L nas duas, o U pela costa da alma, o tubo retangular em
  volta do gusset. O nome `barras` já existia no nó (recuo e Whitmore por barra) e não foi reaproveitado.
- `tubos` (contrato W7): tubo circular da diagonal (gusset no plano médio) e a barra do tirante (maciça, t = D/2).
- `linhas`: `{estilo: "tracejada" | "cota", pontos, texto, lado}` — espraiamento de Whitmore (some quando visto de lado) e
  cotas explícitas do motor (traçadas para o lado `lado`, ou para baixo/direita quando ele fica de topo na vista).
- `vista`: `{x, z}` = eixos horizontal e vertical da elevação no referencial local (gusset: viga e pilar; nó: banzo e a
  normal a ele). Planta, lateral e 3D seguem. O referencial do contrato não mudou (x ao longo da barra 1), então o Realista
  e as pontas não mudam.

Desenho (`LigacaoDesenho.cs`, só o indispensável): `GeoLig` virou `partial`; a base das chapas agora respeita a origem na
face (G6) — antes toda chapa saía meia espessura deslocada (gusset sobre a aba da cantoneira, chapa de extremidade entrando
na mesa do pilar); leitura das peças novas e `Base()` com os eixos de `vista`. O resto em `LigacaoDesenhoW8.cs`.
`JanelaLigacaoProjeto.cs`: o campo com `resugerir` refaz o Sugerir ao trocar o perfil (como `perfil_cantoneira`).
`ligacoes.py` (`pontas_de`): lê `perfil_diagonal`/`perfil_barra_1` do catálogo para a ponta. `ligacoes_modelo.py`: o nó
com várias barras pelo modelo continua recusado também no esquema novo (`arranjo_i`).

## 4. Testes

- Motor: `testes/test_w8_contrav.py`, 14 testes novos — esquema sem medidas soltas e listas do seletor; os três padrões
  ≤ 0,90; bordas 35, g 45, comprimento 6000; equilíbrio do nó de Pratt e e_banzo = d/2; propriedades do catálogo (L, 2L,
  U, tubo); erros de perfil; geometria da L sobre o gusset (face, talão, gabarito, gusset cobrindo o talão, Whitmore,
  vista), 2L/U/tubo, banzo e barras do nó sem choque, `_cruzam`; tirante (barra, rosca, ordem das peças, cota); legado e
  recusa do nó com várias barras pelo modelo.
- Alterado com justificativa: `test_ligacoes_contraventamento.TestContrato.test_erros` — com dados vazios `dim_a` deixou
  de ser obrigatório (a diagonal vem do catálogo com padrão); o teste agora exige que falte só `t_gusset`.
- Contagens: motor 1269 OK (9 pulados; eram 1255), pórtico 242.

## 5. Pendências

- O otimizador genérico pode afinar o gusset abaixo da espessura da aba (o `sugerir` não deixa); não há verificação.
- O 3D do projetista enquadra 2,3 × a diagonal do foco; com a barra desenhada a peça fica pequena (igual à W7-LIG).
- Flexão local da mesa do banzo sob a componente normal do gusset não é verificada (no padrão ΣV = 0).
- O nó continua com o referencial na barra 1; `vista` só gira o desenho do projetista.
- Tirante com garfo: o padrão do garfo (t_chapa 6,3, pino = D) reprova (1,20); só o de rosca e porca é padrão de fábrica.
- "apoio sem perfil: ponta aproximada" aparece no tirante, que não tem apoio de perfil (aviso genérico de `pontas_de`).
