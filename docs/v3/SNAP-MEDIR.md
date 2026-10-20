# Snap e Medir na Vista 3D (igual ao AutoCAD + Inventor)

Pedido: "sistema de snap igual do CAD; sistema de medir igual do CAD e do Inventor, uma mistura dos dois".
Spec de origem: [PESQUISA-PLANT.md](PESQUISA-PLANT.md) §3 (precisão CAD) e §4 (medir). Branch `wt-snap`.

## Arquivos

| Arquivo | O quê |
|---|---|
| `codigo/Estructto/Snap3D.cs` | Motor de snap **puro** (sem WPF de tela): geometria + projeção mundo→tela + pixel do cursor + plano de trabalho + relógio → ponto e tipo. Também a leitura da coordenada digitada e os testes `TestesSnap`. |
| `codigo/Estructto/SnapOverlay.cs` | Desenho: marcadores do AutoSnap, caminhos pontilhados, tooltip, cotas/arcos/área do Medir. |
| `codigo/Estructto/Medir3D.cs` | Contas (`Medidas`), estado da medição (`Medidor3D`), painel lateral (`PainelMedir`) e testes `TestesMedir`. |
| `codigo/Estructto/Vista3D.Snap.cs` | Única ligação com a `Vista3D` (partial) e com a `MainWindow` (comando, Ctrl+M, faixa, rodapé). |
| `codigo/Estructto/CapturasSnap.cs` | Capturas 50–53 (`ESTRUCTTO_CAPTURAS=snap`). |

Em `Vista3D.cs` só há chamadas de uma linha para o partial (`AtrairSnap` no lugar do antigo `Atrair`, `TeclaSnap`, `DigitarSnap`,
`MouseSnap`, `PontoSnap`, `MedirClique`, `DesenharSnap`, `IniciarSnap`, `EstadoSnap`, `EtiquetaMedir`, `MontarMedir3D`), para não
brigar com a reorganização da navegação (`wt-so3d`).

## Snap (OSNAP)

Abertura **10 px** (`Snap3D.Abertura`), medida na tela entre o cursor e o ponto projetado.

| Modo | One-off | Marcador | O que pega |
|---|---|---|---|
| Extremidade | `END` | □ | pontas das barras; no Realista também os cantos do sólido |
| Ponto médio | `MID` | △ | meio da barra (e das arestas do sólido no Realista) |
| Centro | `CEN` | ○ | centro da ponta de tubo/barra redonda (Realista): passar na borda do círculo pega o centro |
| Nó | `NOD` | ⊗ | nó analítico |
| Interseção | `INT` | × | duas barras que se cruzam de fato (distância 3D < 0,1 mm) |
| Interseção aparente | `APP` | ⊠ | barras reversas que só se cruzam na vista; vale o ponto da barra mais perto de quem olha |
| Perpendicular | `PER` | ⊥ | pé da perpendicular do 1º ponto na barra sob o cursor (o marcador fica no pé, como no AutoCAD) |
| Extensão | `EXT` | … | prolongamento da barra além da ponta adquirida (pausa de 0,5 s na ponta) |
| Paralelo | `PAR` | // | pausa de 0,5 s sobre uma barra (aparece // nela); afastando o cursor na mesma direção a partir do 1º ponto |
| Mais próximo | `NEA` | ⧖ | qualquer ponto da barra (desligado por padrão) |
| Grade | — | + | nó da grade do plano de trabalho (passo `PassoGrade`) |
| Nenhum | `NON` | — | próximo clique sem snap de objeto |

- **Running** (padrão): END, MID, CEN, NOD, INT, APP, PER, EXT, PAR + grade. F3 liga/desliga todos sem perder a escolha.
- **One-off**: Shift (ou Ctrl) + botão direito abre o menu no cursor; ou digitar `END`, `MID`, `INT`, `PER`, `NEA`, `CEN`, `NOD`,
  `APP`, `EXT`, `PAR`, `NON` + Enter (letras digitadas na vista vão para a linha de comando, como no AutoCAD). Vale um clique.
- O menu também troca os modos permanentes, o ângulo polar (90/45/30/22,5/15/10/5°) e "rastrear em todos os ângulos polares".
- **Escolha entre modos**: vence o ponto mais perto do cursor; empate até **2 px** → prioridade
  END > INT > APP > MID > CEN > NOD > PER > EXT > PAR > NEA. *Mais próximo* só entra se nenhum outro modo achou ponto (senão
  ganharia sempre, por estar a 0 px).
- Ordem geral: snap de objeto > cruzamento de dois caminhos (rastreio/polar/extensão/paralelo) > um caminho (ORTO forçado, senão o
  mais perto) > grade > plano. Extensão e paralelo cruzam com o rastreio antes de valer sozinhos
  (tooltip `Extensão: 3,000, Extremidade: 5,000 < 90°`).

## Rastreamento, polar e ORTO

- **Aquisição (F11)**: cursor parado **0,5 s** sobre um ponto de snap → `+` ciano; até 7 pontos; parar de novo sobre ele remove.
  Caminhos: U e W do plano (ortogonal, padrão do AutoCAD) ou todos os ângulos polares, mais a normal do plano (vertical, essencial
  para pilares). No plano do 1º ponto quando há um. Dois caminhos que se cruzam viram candidato (×), com as duas linhas pontilhadas.
- **Polar (F10)**: incremento 15° (menu: 90/45/30/22,5/15/10/5) no plano de trabalho + ±normal. Ao passar a menos de 10 px do raio
  aparece o caminho e `Polar: 4,500 < 30°`. Com a grade ligada, a distância anda em passos da grade.
- **ORTO (F8)**: força o eixo U, W ou a normal mais alinhado com o cursor na tela; o comprimento é a componente do cursor no plano.
  **ORTO e Polar se excluem.** Shift segurado inverte o ORTO.
- O rodapé (Orto, Polar, Objetos, Rastreio) é um estado só para o 2D e o 3D: F3/F8/F10/F11 na Vista3D atualizam os botões e
  clicar nos botões com a Vista3D espacial na frente muda o snap 3D.

## Entrada dinâmica

As caixas Comprimento/Ângulo junto ao cursor continuam (digitar trava o comprimento, Tab o ângulo, Enter confirma). Coordenada
digitada (`Snap3D.LerCoordenada`), em metros, ponto decimal (a vírgula separa coordenadas):

| Texto | Significado |
|---|---|
| `x,y,z` / `u,w` | **com 1º ponto: relativo a ele** (padrão do AutoCAD do 2º ponto em diante); sem 1º ponto: absoluto |
| `@dx,dy,dz` | relativo explícito |
| `#x,y,z` | absoluto |
| `L<a` | polar no plano de trabalho (a de U para W, graus), relativo |

## Desenho

- Marcador amarelo `CorSnapAuto` `#FFD400`, 11 px, traço 2 px, contorno escuro de 1 px para ler sobre o sólido; aparece com fade a
  cada troca (`Anim.Entrar`). Tooltip com o nome do modo ao lado (acima do cursor quando as caixas da entrada dinâmica estão embaixo).
- Caminhos pontilhados ciano `CorRastreio` `#00C8FF`, 1 px, traço 4/4; pontos adquiridos `+` ciano.
- Medir: cota laranja `CorMedida` `#FF9F1C` 1,5 px com setas cheias de 10 px e rótulo em pílula (fundo escuro 85%, texto branco
  12 px); seleção 2 azul `CorMedida2` `#2F7DF6`; ΔX/ΔY/ΔZ em escada tracejada nas cores dos eixos; arco do ângulo com raio de 40 px
  (maior quando o ângulo projetado é estreito); área em verde `CorMedidaArea` 25%. Sem teste de profundidade (sobreposição 2D).

## Medir (Ctrl+M, `ME`/`MEDIR`/`MED`/`MEA`/`DI`, Modelo › Desenhar 3D › Medir, Vista › Navegar › Medir, paleta "Medir 3D")

**Automático (Inventor)** — o resultado depende do que se clica. Clique com snap de objeto = ponto; sem snap, a barra sob o cursor
= entidade; no vazio, o ponto do plano.

| Seleção | Resultado | No painel |
|---|---|---|
| 1 ponto | coordenadas | X, Y, Z |
| 1 barra | comprimento | seção, área da seção, raio/Ø (redonda), peso (aço), ΔX/ΔY/ΔZ |
| 2 pontos | distância | ΔX, ΔY, ΔZ, ângulo no plano XY, inclinação |
| barra + ponto | distância perpendicular | posição ao longo da barra (m e %) |
| 2 barras | distância **mínima** (pontos mais próximos) | ângulo entre eixos, centro a centro, paralelas/coplanares/reversas |
| 3 pontos | ângulo no ponto do meio | replemento, lados |

**Submodos** (botões no painel): Distância (2 pontos), Ângulo (vértice primeiro + 2 pontos, ou 2 barras), Área (pontos; Enter fecha;
1 barra = área da seção), Raio (tubo/redondo), Cadeia (pontos sucessivos; `+` soma / `−` subtrai os próximos trechos, como o
acumulado do Inventor; Backspace desfaz; Enter fecha).

**Painel** (280 px à direita): modo, chips laranja/azul das barras escolhidas, valor principal 24 pt, linhas (clique copia),
Copiar / Copiar tudo (texto com tabulação), `+`/`−` levam o valor ao **Acumulado** (um grupo por tipo: comprimento, ângulo, área,
com lixeira), **Histórico** das últimas medidas (clique copia).

**Teclado**: Esc limpa as seleções, 2º Esc sai; Enter fecha área/cadeia (ou confirma a coordenada digitada); Backspace tira o último
ponto; `+`/`−` acumulam; Ctrl+C copia o valor. A medida rápida aparece ao vivo: cota tracejada do último ponto até o cursor.

## Testes e provas

- `Estructto.exe --testes-snap <saida.txt>`: 68 asserções (modos, empate, one-off, F3, polar, ORTO, rastreio, extensão × rastreio,
  paralelo, grade, segmentos 3D, coordenada digitada e as medidas).
- Capturas `ESTRUCTTO_CAPTURAS=snap` (com `ESTRUCTTO_MOTOR` = `codigo\motor`): `50-snap-<modo>` (+ `-zoom` 2×) para cada modo,
  `51-snap-rastreio-intersecao`, `52-snap-polar`, `52-snap-orto`, `53-medir-{distancia,angulo,minima,cadeia,area,barra-realista}`;
  confere o tipo de snap e o valor medido de cada uma (resumo em `50-snap.txt`).

## Limites (ponytail)

- Projeção afim (câmera ortográfica): o parâmetro ao longo de uma reta na tela vale no 3D. Câmera em perspectiva exigiria raio→reta.
- Sólido: arestas atrás de outras peças também atraem (sem teste de oclusão); interseção só entre os eixos/arestas a até 10 px do
  cursor (no máximo 60 por vez).
- Sem "From", "Mid between 2 points", filtros de ponto e "Temporary track point" do menu do AutoCAD; sem Tab para ciclar candidatos.
- Os modos escolhidos não são gravados em `Config` (voltam ao padrão ao abrir).
- Medir: área da seção pelo contorno do Realista (tubos e redondos exatos; o resto é o polígono do perfil, sem raios de concordância);
  peso supõe aço 7850 kg/m³. Sem cota permanente ("fixar cota") nem distância máxima.
