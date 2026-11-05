# Torre do elevador (F6) — teste de uso real e correções (05/11/2026)

Pedido do usuário: "o estructto é com base em linhas mas ela tá pegando snap nos perfis… o sistema de juntar e dividir perfis
não funciona direito… se eu boto um nó no meio da viga ou coluna não consigo dividir… revise o sistema de nível, deixe mais
simples… teste nesse projeto e ache problemas" (trabalho da faculdade: estrutura metálica externa anexa ao bloco F6, elevador
para 10 pessoas com cadeirante; foto de referência da torre existente entre F1 e F2).

## O modelo (medidas inventadas)

| Item | Valor |
|---|---|
| Poço (eixos dos pilares) | 2,20 m (X) × 2,40 m (Y) |
| Níveis | Térreo 0 · N+3,50 · N+7,00 · N+10,50 · N+13,50 (casa de máquinas, pé-direito 3 m) |
| Pilares e vigas de borda | W150x22.5, A-572 GR.50, base engastada |
| Passarela até o F6 | 2 vigas de 1,6 m em cada pavimento, apoio articulado na face do prédio (y = 4,0 m) |

Montado pelo fluxo E2E **G** (`Estructto.exe --testes-fluxo <pasta> G`, arquivo `codigo/Estructto/TestesFluxoTorre.cs`), que
dirige a janela real como o engenheiro: novo projeto com grade e níveis → Pilar com clique nos 4 cantos → vigas digitadas (L,
`#0,0,3.5`, `@2.2,0,0`…) → PgUp → Pilar/vigas do andar 2 → copiar pavimento → topo → passarela → snap no Realista → NÓ no meio
da viga e do pilar → DIVIDIR num nó solto → JUNTAR → mudar a cota do topo → F5. Fotos: `fluxo-G-*.png`.

## Problemas achados (antes: 13 ok, 14 falhas) e correção (depois: 27 ok, 0 falhas)

| # | Problema | Causa | Correção |
|---|---|---|---|
| 1 | Realista: snap nas arestas do perfil (33 de 34 atrações fora do eixo: "Ponto médio" na mesa do W) | `Vista3D.Geometria()` punha arestas e centros do sólido no snap | O sólido não atrai: só nós, eixos das barras e cruzamentos da grade (`Vista3D.Snap.cs`) |
| 2 | Em planta, o Pilar no nível de cima nascia no andar errado (clique no canto pegava a Extremidade do pilar de baixo) | No mesmo pixel, a prioridade (Extremidade > Nó) ganhava do cruzamento da grade que está no plano | Mesmo pixel: o ponto no plano de trabalho vence, de qualquer tipo; depois prioridade; depois o mais perto do olho (`Snap3D.Objetos`) |
| 3 | PgUp/PgDn (NIVEL +/-) trocava o plano, mas a vista continuava isolada no andar antigo e a janela achava que o nível era outro | `TrocarNivel` só mexia em `PlanoAtual` | Um caminho só: `AtivarNivel(z, camera)` (menu, painel, PgUp, NIVEL); PgUp não mexe na câmera |
| 4 | NÓ no meio da viga/pilar ficava solto por cima da linha (não ligava; o F5 dava "mecanismo: nó livre em uz") | `CmdNo` só criava o nó | Nó a 1 mm do eixo de uma barra divide a barra nele (mesmo membro), como a ponta de barra desenhada no vão (`LigarNoVao`) |
| 5 | DIVIDIR não tinha como dividir no nó que já está sobre a barra (só partes iguais ou distância digitada) | — | Modo "Nos nós sobre a barra" (`GeometriaEdicao.DividirNosNos`); o painel já abre nele quando a barra tem nó em cima; campos que não valem ficam desabilitados |
| 6 | JUNTAR com 1 trecho escolhido recusava; barra nula na escolha derrubava com "Value cannot be null (propertyName)" | `Juntar` exigia ≥ 2 e não filtrava null | 1 trecho de peça dividida (DIVIDIR, NÓ, ligar no vão) junta a peça inteira (`Trechos`); a peça inteira volta sem `params.membro` |
| 7 | Mudar a cota de um nível não levava os nós (o pavimento ia, os pilares ficavam) | O painel só fazia PATCH do pavimento | `Niveis.MudarCota`: pavimento + nós da cota num PUT (um Ctrl+Z); recusa se os nós cairiam sobre nós existentes; nome automático (N+13,50) segue a cota |
| 8 | Painel Níveis tinha duas seleções (a linha "escolhida" e o nível "ativo") e a faixa tinha 3 botões que abriam o mesmo painel | — | Uma seleção: clicar na linha ativa o nível; as ações (copiar, pilares) valem para o ativo; faixa só com "Níveis" |

## Regras que ficam

- O Estructto é de **linhas**: snap só em nós, eixos e grade. O Realista é desenho.
- Nó sobre uma linha é **ligação**: NÓ, ponta de barra no vão e DIVIDIR "nos nós" sempre dividem a barra.
- Em planta, o **plano de trabalho manda**: entre pontos no mesmo pixel vale o que está no nível ativo.
- **Nível ativo** é um só, trocado só por `AtivarNivel`. Mudar a cota do nível leva os nós dele.

## Testes

- `--testes-snap`: novo "torre: planta no nível z=9, o nó da grade no plano vence a Extremidade do andar de baixo"; E2E-04 z=3
  agora espera o ponto médio do pilar (está no plano).
- `--testes-edicao`: juntar 1 trecho, barra nula, dividir nos nós (e recusa), mudar cota (e recusa).
- `--testes-fluxo … G`: o fluxo inteiro acima.
