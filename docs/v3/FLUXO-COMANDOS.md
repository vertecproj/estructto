# Fluxo dos comandos — as duas ordens, como no AutoCAD/Plant 3D

Pedido do usuário (21/10/2026): "o engaste só funciona se eu selecionar primeiro o nó e depois clicar no botão, esse não é o
fluxo que quero". Agora **todo comando que age sobre objetos funciona nas duas ordens**:

- **Substantivo→verbo** (escolha antes): com seleção, o comando age nela na hora. Só vale o tipo que o comando aceita (ENGASTE
  com nó + barra escolhidos engasta só o nó).
- **Verbo→substantivo** (escolha depois): sem seleção válida, o comando pede na vista e na linha de comando, ex.
  `ENGASTE — selecione os nós (Enter termina, Esc cancela)`.
  - Clique soma; **Shift + clique tira**. Janela (esq.→dir.) e cruzamento (dir.→esq.) somam; **Shift + arrastar tira** (durante
    o pedido Shift + esquerdo não orbita; Shift + meio continua orbitando).
  - **Enter, Espaço ou botão direito** aplica; **Esc** cancela (e limpa a escolha).
  - **Pré-realce**: o objeto que o clique pegaria fica marcado (traço sobre a barra, anel no nó), filtrado pelo tipo: nós para
    apoios e molas; barras para seções, rótulas e cargas em barra. Para nós, vale também a ponta da barra a até 14 px.
  - Comandos de vários passos perguntam o resto depois: ponto base, destino, valor. Perguntas de valor (`PedirValor`) mostram o
    padrão entre `< >` (a última resposta vira o padrão); Enter/Espaço vazio ou botão direito aceita; resposta inválida
    pergunta de novo, com o motivo no histórico. O valor também pode vir na linha (`SECAO W310x38.7`, `GIRO 90`, `CD 0,0,-2`).
- A linha de comando mostra o mesmo pedido da etiqueta da vista (antes mostrava a dica do antigo 2D).

Código: `Comandos3D.cs` (`ComEscolha`, `PedirValor`, comandos), `Vista3D.Grips.cs` (`PedirSelecao`, `PedirValor`, filtro e
pré-realce), `GeometriaEdicao.Apoio/Rotulas/Cardinal` (`EdicaoEspacial.cs`, testadas em `--testes-edicao`).

## Tabela

| Comando (alias) | Antes | Agora | Pedido (verbo primeiro) |
|---|---|---|---|
| ENGASTE | só com nós já escolhidos ("Escolha os nós e repita") | as duas ordens; tira molas | `ENGASTE — selecione os nós (Enter termina, Esc cancela)` |
| ROTULA / FIXO / ARTICULADO | idem | idem | `ARTICULADO — selecione os nós (…)` |
| MOVEL / ROLETE | **não existia** (a paleta mandava `MOVEL` e dava "comando desconhecido") | novo: só uz travado; botão Móvel | `MÓVEL — selecione os nós (…)` |
| LIVRE | só com nós escolhidos; deixava molas | as duas ordens; tira apoio e molas | `LIVRE — selecione os nós (…)` |
| MOLAS / MOLA | não existia (só no 2D antigo) | novo: `molas` do motor (kN/m, kN·m/rad); GL com mola fica livre; botão Molas | `MOLAS — selecione os nós (…)` → `MOLAS em N nós — kx,ky,kz[,krx,kry,krz] … <0,0,10000>` |
| ROTULAR / RB | só com barras escolhidas | as duas ordens (liga/desliga pela 1ª) | `ROTULAR — selecione as barras (…)` |
| APAGAR / E / Del | pedia por clique, um objeto por vez, sem janela | seleção completa (janela, Shift tira) | `APAGAR — selecione os objetos (…)` |
| MOVER / M | idem | idem + ponto base, destino | `MOVER — selecione os objetos (…)` → `MOVER — ponto base` → `MOVER — segundo ponto` |
| COPIAR / CO | idem | idem; várias cópias até Enter | `COPIAR — selecione os objetos (…)` → base → destinos |
| ESPELHAR / MI | idem | idem | `ESPELHAR — selecione os objetos (…)` → 1º e 2º ponto da linha |
| ROTACIONAR / RO | idem; Esc deixava o ângulo pendente (um número digitado depois girava) | idem; Esc limpa | `ROTACIONAR — selecione os objetos (…)` → base → ângulo ou direção |
| MATRIZ / AR | abria o painel; sem seleção agia **no modelo inteiro** | pede a escolha antes do painel | `MATRIZ — selecione os objetos (…)` |
| DIVIDIR / DIV | idem (sem seleção dividia **todas** as barras) | pede as barras antes do painel | `DIVIDIR — selecione as barras (…)` |
| UNIR / J | abria o painel | pede os nós; Enter sem nada = todos (de propósito) | `UNIR — selecione os nós (Enter termina; Enter sem nada = todos, Esc cancela)` |
| APARAR / TR | 1 barra limite por clique; pré-seleção ignorada | limites = os escolhidos antes, ou escolha + Enter (vários); depois as barras | `APARAR — selecione as barras limite (…)` → `APARAR (N limites) — clique a barra a aparar …` |
| ESTENDER / EX | idem | idem; vale o limite do lado da ponta clicada | `ESTENDER — selecione as barras limite (…)` → `ESTENDER (N limites) — clique a barra a estender …` |
| COPIARPROPRIEDADES / MA | só origem + destinos por clique; seleção ignorada; não levava o cardinal | 1 escolhida = origem; várias = destinos; leva seção, material, giro e cardinal; um Ctrl+Z | `COPIAR PROPRIEDADES — clique a barra de origem` → `COPIAR PROPRIEDADES de Bx — selecione as barras que recebem …` |
| LOTE / EMLOTE (botão Em lote) | o botão abria o painel; sem seleção = **modelo inteiro** | pede a escolha antes do painel | `EM LOTE — selecione os objetos (…)` |
| SECAO / SEC / PERFIL | não existia (só painel de 1 barra ou Em lote) | novo: perfil do modelo ou do catálogo; Enter = o das Configurações | `SEÇÃO — selecione as barras (…)` → `SEÇÃO de N barras — perfil do modelo ou do catálogo <…>` |
| MATERIAL / MAT | idem | novo (nomes do modelo, maiúsc./minúsc. tanto faz) | `MATERIAL — selecione as barras (…)` → `MATERIAL de N barras — A-572 GR.50 / … <…>` |
| GIRO | idem | novo | `GIRO — selecione as barras (…)` → `GIRO da seção de N barras (°) <0>` |
| CARDINAL / CARD | só para as próximas barras (Configurações) | novo para as existentes (1..9, 10) | `CARDINAL — selecione as barras (…)` → `CARDINAL de N barras — 1 a 9 … <5>` |
| APLICARCONFIG / APLICAR | não existia | Configurações (seção, material, giro, cardinal) nas barras, um Ctrl+Z | `APLICAR CONFIGURAÇÕES — selecione as barras (…)` |
| CARGANO / CN / CARGA (botão Nodal) | botão só abria o painel com um aviso; 1 nó por vez | as duas ordens, vários nós, um Ctrl+Z | `CARGA NODAL — selecione os nós (…)` → `CARGA NODAL (caso) em N nós — Fx,Fy,Fz[,Mx,My,Mz] … <0,0,-10>` |
| CARGADIST / CD (botão Distribuída) | só no painel, 1 barra por vez | idem; trapezoidal `q1/q2` | `CARGA DISTRIBUÍDA — selecione as barras (…)` → `… qx,qy,qz (kN/m, global); trapezoidal: q em ni / q em nj <0,0,-5>` |
| CARGAPONTUAL / CPT (botão Pontual) | idem | idem; `a` em m ou `M` (meio de cada barra); confere a ≤ L | `CARGA PONTUAL — selecione as barras (…)` → `… distância a desde ni (m) ou M (meio) <M>` → `… Fx,Fy,Fz <0,0,-10>` |
| CARGAAREA / LAJE / CAREA (botões Laje e Carga em área) | botões só abriam o painel sem escolha | vigas escolhidas (antes ou depois; um nível, uma direção) **ou** a cota digitada (pergunta X/Y); depois p | `CARGA EM ÁREA — selecione as vigas ou digite a cota z do nível (…)` → `… [X/Y] <X>` → `… p (kN/m², para baixo) <3>` |
| VERIFICAR / VB (botão Verificar barra) | só com barra escolhida (toast "selecione e repita") | as duas ordens | `VERIFICAR — clique a barra (Esc cancela)` |
| PLACA | sem seleção escolhia sozinho o primeiro pé | as duas ordens; Enter sem clicar = o primeiro pé (como antes) | `PLACA — clique o nó do pé do pilar (Enter = o primeiro pé, Esc cancela)` |
| DIMSEL | sem seleção dimensionava **o modelo inteiro** | pede as barras | `DIMSEL — selecione as barras (…)` |
| MEDIR / ME / DI | verbo primeiro | igual (mantido) | `MEDIR (…) — …` |

## Funções que não funcionavam direito (achadas e corrigidas)

1. **Apoios e rótulas só com pré-seleção** (ENGASTE, ROTULA, LIVRE, ROTULAR): sem seleção só diziam "escolha e repita".
2. **Móvel não existia**: a paleta mapeava "Móvel" para `MOVEL`, que dava "Comando desconhecido". Molas só no 2D antigo.
3. **Linha de comando mostrava a dica do 2D**: `AtualizarDica` lia a ferramenta da vista 2D escondida; no 3D era sempre
   "Digite um comando…". Agora mostra o pedido em curso.
4. **Estado velho no ROTACIONAR**: Esc no ângulo não limpava `numeroPendente` (o Esc ia para a vista, não para a janela); um
   número digitado depois, em outro contexto, girava a seleção antiga. Agora o cancelamento limpa e o número só vale com o pedido vivo.
5. **Matriz, Dividir, Em lote e DIMSEL sem seleção agiam no modelo inteiro** (Dividir partia todas as barras) sem perguntar.
   Agora pedem a escolha (os painéis abertos por código continuam com "nada = modelo inteiro", rotulado no painel).
6. **Ctrl+Z no meio de um comando** deixava o pedido vivo com ids que podiam ter sumido; agora o desfazer/refazer cancela o comando.
7. **`ClicarTela` (API de teste) ignorava o pedido de objeto** (fazia seleção comum): agora segue o mesmo caminho do mouse.
8. **Botões que só abriam um painel com aviso** (Ações › No nó ou na barra, Carga em área, Chapas › Laje): viraram comandos.
9. **MA não levava o ponto cardinal** e ignorava a pré-seleção; **TR/EX** ignoravam a pré-seleção e só aceitavam um limite.
10. Código morto: `MontarEdicao3D` (botões de edição nunca adicionados) removido.

## Testes

- `Estructto.exe --testes-comandos <pasta>` (`TestesComandos.cs`, precisa do motor): cada comando acima nas duas ordens, com
  cliques simulados (`ClicarTela`, `SelecionarCaixa` com e sem Shift, `Tecla(Enter/Esc)`), respostas na linha de comando,
  pré-realce, texto do pedido na vista e na linha de comando, resultado no modelo e um Ctrl+Z voltando ao modelo de antes.
  Última rodada: 216 ok, 0 falhas.
- `--testes-edicao`: `GeometriaEdicao.Apoio` (móvel, molas, livre), `Rotulas`, `Cardinal`, `LerLista`.
- `--testes-fluxo D` ajustado: TR/EX agora têm Enter depois do limite.
