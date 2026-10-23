# Geometria das ligações no Realista: solda, enrijecedores, topo do pilar, placa deitada

Data: 22/10/2026. Branch `wt-geo`. Pedido: capturas do Realista com solda passando da chapa, enrijecedor "oco" e sem solda,
chapa/enrijecedor acima do topo do pilar, chapa sobrando além do perfil e placa de base deitada.

Capturas: `ESTRUCTTO_CAPTURAS=geo` (`CapturasGeo.cs`, `61-geo-*`). As imagens abaixo mostram antes à esquerda e depois à direita.

## 1. Solda só onde existe chapa

**Cálculo (motor, `placa_base._solda`)**

- Antes, o contorno soldado era sempre o perfil inteiro: L = 2·(b + d).
- Agora, com `pelo_perfil` falso e H, B informados, a chapa pode ser menor que o perfil. Nesse caso o retângulo soldado fica limitado a min(perfil, chapa) em cada direção (d ao longo de H, b ao longo de B; no eixo fraco, trocados). Isso vale para L, para a área e para a inércia.
- Tubo circular maior que a chapa: usa-se o círculo inscrito na chapa, raio = min(H, B)/2. É a favor da segurança.
- O resultado ganha `recortada` e um aviso.
- Com `pelo_perfil` verdadeiro, ou com chapa maior que o perfil, nada muda. Todos os casos antigos ficam iguais.
- Critério: a solda só transmite esforço onde assenta na chapa. O trecho no ar não existe.
- Teste: `testes/test_geometria_ligacoes.py`. O exemplo dá 640 mm em vez de 900 mm.

**Desenho (`Ligacao3D.Apoio` + `Render3DGeometria.Cordao`/`Intervalos`)**

- O cordão que assenta na face de uma chapa não paralela à barra (placa de base, chapa de topo) é recortado pelo contorno dela.
- O teste usa o pé da perna, meia perna para fora. Assim some também o filete da borda da mesa quando a chapa é rente ao perfil.

![solda da placa menor que o perfil](render/geo-placa-pequena-perto.png)

## 2. Enrijecedores fechados e soldados

- **Bug da chapa "oca".** `MalhaDx.Chapa` decidia a orientação das tampas pelo sentido do contorno. Mas `Triangular` já devolve os triângulos sempre anti-horários. Resultado: um contorno horário (por exemplo, o enrijecedor do pilar do lado −y) saía com as duas tampas viradas para dentro. Com o descarte de faces de trás, via-se o interior da chapa. Agora as tampas dependem só de (u × w)·n.
  - Autoteste: "chapa de contorno horário… para fora".
- **Solda de borda.** O motor manda o cordão do enrijecedor no plano médio da chapa. Antes, o filete era desenhado ali: ficava dentro da chapa. E o cordão paralelo à barra nem aparecia, porque o produto vetorial com o eixo da barra é nulo.
  - `Ligacao3D.Apoio` reconhece a solda na borda de uma chapa: as pontas e o meio estão no contorno, dentro da espessura.
  - As pernas vão pela chapa (para dentro dela) e pela peça de apoio (±normal).
  - A raiz fica na face da chapa, deslocada meia espessura.
  - Autoteste: "solda de enrijecedor…".
- **Motor.** O `enrijecedor da chapa de topo` (`ligacoes_rigidas`) não tinha solda na geometria. Ganhou dois filetes: um na chapa de topo e um na mesa da viga.
  - Perna: filete mínimo, como no enrijecedor do pilar. É só desenho; a solda não é dimensionada (já era assim, com aviso).
  - Teste: `test_enrijecedor_da_chapa_de_topo_tem_solda`.

## 3 e 4. Topo do pilar e chapa no lugar certo

- **Viga com chapa de topo no topo do pilar (joelho).** O pilar sobe até o topo da chapa de topo, menos `folga_topo`.
  - `folga_topo` é opcional, em mm, na `geometria` do contrato (`Ligacao3D.FolgaTopo`). O padrão é 0, ou seja, alinhado.
  - Antes, o pilar ia só até o canto da viga (v0.6 A1). A extensão da chapa estendida (com parafusos) e o enrijecedor da mesa de cima ficavam soltos acima do pilar (imagens 4 e 5 do pedido).
  - Só vale para pilar vertical que termina no nó e fica abaixo dele. Corte de esquadria ou do motor nesse topo não é alterado.
  - O autoteste `v0.6 A1: joelho` passou a exigir "pilar até o topo da chapa" e continua exigindo que cubra o canto da viga.
- **Proposta de cumeeira (`LigacoesAmostra.Cumeeira`).** A chapa fica ajustada ao perfil + `SobraCumeeira` (10 mm, parâmetro), com parafusos só entre as mesas.
  - Antes, a chapa era estendida 60–80 mm acima e abaixo da viga, sem viga atrás (imagem 3).
  - A chapa estendida do motor (`chapa_topo_estendida`) continua como o motor calcula. O que muda é que agora o pilar a acompanha.

![topo do pilar](render/geo-pilar-topo.png)
![joelho visto de trás](render/geo-chapa-topo-tras.png)
![cumeeira proposta](render/geo-cumeeira.png)

## 5. Placa de base deitada (eixo fraco)

O que ficou suportado:

| Onde | O quê |
|---|---|
| Motor | `eixo_fraco` (já existia): H na largura do perfil, chapa e solda giradas. A solda recortada (§1) respeita a troca. Teste `test_eixo_fraco_troca_as_direcoes` |
| 3D (`Render3DMotor`) | Lê `eixo_fraco` do projeto do grupo → `PlacaDeBase(…, deitado)`. Furos por `fileiras` × `por_fileira`, como em `geometria_placa`, em vez de 4 cantos fixos. `pelo_perfil` ausente no projeto = falso, como no motor; antes o 3D supunha verdadeiro e desenhava outra placa. Proposta sem projeto = verdadeiro, como o sugerir |
| Janela da placa | Caixa "Placa deitada (girada 90°…)". `PlacaBase.EixoFraco` ↔ `eixo_fraco` em `PlacaMotor` |
| Nucleo.cs (legado/desenho 2D) | `ResPlaca.Deitado = pilar.EixoFraco ≠ placa.EixoFraco` e `RefIn`/`RefOut` trocados. Padrão falso: os resultados antigos não mudam. Teste novo no `Testes.cs` (242 passam) |
| Barra inclinada | `AssentarPlaca` com a placa deitada: as sobras continuam pareadas com B e H do perfil |
| Placa vertical contra parede | Barra horizontal com apoio: a placa já é ⟂ à barra no referencial local (vertical) e os chumbadores vão ao longo da barra |

![placa deitada com 3 fileiras](render/geo-placa-eixo-fraco-perto.png)

## 6. Gusset de diagonal desenhado pelo Estructto (gusset local)

O gusset local é o `LigacoesAmostra.Gusset`. Ele é usado nas propostas sem projeto, nas capturas e no projeto `ligacao_tracao` de um nó de gusset ou treliça. A geometria do motor para `ligacao_tracao` é esquemática: uma chapa que começa no nó, sem perfil. Por isso o Render3DMotor troca essa geometria pelo gusset local, com t, d, nº de parafusos e passo do projeto.

A referência é o Advance Steel. A chapa fica no plano formado pela diagonal e pelo apoio, soldada em toda a borda reta na face do apoio. A diagonal encosta numa face dela, e os parafusos ficam só no trecho em que a diagonal está sobre a chapa, contados a partir da ponta da diagonal.

Regras aprendidas (23/10/2026, cada uma veio de um defeito real):

| # | Regra | Defeito que ela corrige |
|---|---|---|
| 1 | O plano da chapa é o da diagonal com o eixo do apoio. O Render3D gira o referencial em torno da diagonal (y' = x × apoio⊥) e não desloca a origem: o gusset local já vem na face da peça. | A chapa ficava no referencial da diagonal, girada e no eixo do pilar. Depois, o deslocamento do contrato do motor (que supõe a chapa centrada) a punha no meio da alma do W e afundava os parafusos. |
| 2 | A borda soldada fica ∥ ao eixo do apoio (`x = xb − w·sx/sz`). Os filetes acompanham. | A borda ⟂ à diagonal tocava o pilar só por um canto. |
| 3 | `FolgaGusset` empurra a chapa para fora das peças do nó, e as soldas da chapa vão junto (`deslGusset`). | As soldas ficavam onde a chapa estava antes do empurrão, soltas dentro do pilar. |
| 4 | A ponta da diagonal vem da regra das juntas (`Corte.Recuo`, contado do nó). O gusset não grava recorte. | O recuo ia como `Folga`, que é o vão a partir da face: a diagonal recuava a face a mais e os parafusos ficavam fora dela. |
| 5 | A diagonal recua a seção inteira, não só o eixo: `recuo += Saliencia`, que é o máximo de `−p·f/(d·f)` no contorno, com f a normal da face que o eixo cruza. Vale na `Juntas` e na `JuntasV04`, e o teste compara as duas. | Com o corte ⟂ à barra, o canto da mesa de baixo entrava no pilar. |
| 6 | Face onde a chapa encosta: se o plano do gusset é ⟂ a u, vale `FaceChapa` (alma do I, costas do U e do L, lado do tubo). Se é ⟂ a v (`NormalEmV`), vale `FaceChapaV` (mesa do I e do U, lado H do tubo, costas da outra aba do L) e o Render3D lê o y do gusset como v (`FaceV`). | A chapa ia num lado fixo do perfil, qualquer que fosse a posição da diagonal. |
| 7 | I/W com a alma no plano: a chapa encosta na alma e, a partir da ponta da diagonal, tem meia largura h/2 − tf − 10. Ela só alarga antes da ponta, até a borda soldada. I/W com as mesas no plano: a chapa encosta na face externa da mesa, com a linha de parafusos a B/4 do eixo, fora da alma, e a pega = tf. | Por fora da ponta das mesas não há alma para os parafusos, e com a largura do perfil todo a chapa atravessava as mesas. |

Provas:
- `42-realista-ligacao-gusset-pilar-meio{,-w,-u,-tubo}` (frente, iso e trás): diagonal de L, W, UPN e tubo chegando no meio de um pilar W.
- `ESTRUCTTO_CAPTURAS=modelo` mais `--capturas <pasta> <arquivo>` renderiza um modelo qualquer: vista iso, mais frente, iso e três vistas de perto da borda soldada em cada nó com ligação.
- Asserção `ligacoes3d: gusset ...` no `--testes-exportar`.

Ainda falta:
- O gusset do motor (`gusset_contraventamento`, com `apoio_dir`) segue o contrato do motor. Nenhuma das regras acima foi aplicada a ele.
- Parafusos de `ligacao_tracao` com várias linhas saem numa linha só ao longo da diagonal.
- W como diagonal com as mesas no plano do pórtico (captura `-w90`): a chapa encosta na mesa, a h/2 do plano, e passa da largura da mesa do pilar. Fica ao lado do pilar, sem onde soldar. O detalhe certo é outro: chapa no plano central, com rasgo na alma da diagonal ou a diagonal girada. Não implementado.
- O recorte da mesa da diagonal em W para a chapa entrar mais (o snipe do Advance Steel) não é feito. A chapa estreita entre as mesas.

## Provas

- Motor: `python -m unittest discover -s testes`, com 1211 testes OK, 9 pulados, 5 deles novos em `test_geometria_ligacoes.py`.
- Pórtico: `compilar.ps1 -Testes`, com 242 testes passando.
- `Estructto.exe --testes-exportar`: sem falhas. As asserções novas são `render3d:` chapa horária, solda recortada e cordão recortado, e `ligacoes3d:` solda de enrijecedor, placa menor e placa deitada.
- `--testes-interferencia galpao` com `ESTRUCTTO_MOTOR` do worktree: 0 pares.

## Pendências

- A placa vertical contra a parede não tem teste próprio. O motor calcula com os esforços locais da ponta e não diferencia piso de parede no bloco de concreto.
- O enrijecedor do pilar que fique acima de um pilar sem chapa de topo, por exemplo uma ligação só com enrijecedores, não é recortado. Hoje ele só sai do ar quando o pilar sobe junto com a chapa de topo.
- Solda da nervura da placa de base: o motor não tem nervuras na placa (PLACA-BASE-V2 §8, "Enrijecedores").
- `folga_topo` ainda não é campo do esquema do motor, só leitura opcional da `geometria`.
