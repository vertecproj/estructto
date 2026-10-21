# 2. Modelar, medir e editar

## Grade e níveis

Em **Modelo › Planos**, ative um **Nível** para desenhar na sua cota ou uma **Elevação X/Y** para trabalhar num plano vertical. **3D completo** remove o isolamento e volta à isométrica. As peças fora do plano isolado ficam em fantasma e não participam da seleção nem do snap.

**PgUp/PgDn** troca o nível do plano de trabalho. No painel **Níveis**, crie, renomeie, altere cotas ou detecte níveis pela geometria. **Copiar pavimento para cima** replica o andar e suas cargas; confira altura e quantidade. Os apoios não são copiados. **Gerar pilares entre níveis** cria pilares sob os nós do nível escolhido, quando ainda não há barra.

Execute **GRADE** (`GR`) para editar a grade. Informe posições em metros como `0,@6,@6,@7.5`: ponto decimal, vírgulas entre posições, `@` soma à anterior. Essa sintaxe é diferente da lista de vãos do Novo projeto. Eixos em X recebem 1, 2, 3…; filas em Y recebem A, B, C…; níveis positivos novos viram pavimentos. Grade e níveis ficam salvos com o modelo. F7 mostra a grade do plano; F9 alterna a atração nela.

## Desenhar barras e pilares

1. Ative o nível ou plano correto. Em **Modelo › Configurações**, escolha seção, material, giro e ponto cardinal das próximas barras.
2. Execute **BARRA** (`L`). Clique o primeiro ponto da grade ou um nó existente e os destinos sucessivos. Use snap ou coordenadas para dar precisão.
3. Enter ou botão direito termina a cadeia; Esc cancela em etapas. Orbite com **Shift+arrastar** sem sair do comando.

Exemplo pela linha de comando: `L`, Enter, `#0,0,3`, Enter, `@6,0,0`, Enter. Isso cria uma barra de 6 m na cota 3 m. Com um primeiro ponto definido, coordenadas sem prefixo são relativas; use `#` para absoluto e `@` para relativo explícito. `6<30` informa comprimento e ângulo no plano. A entrada dinâmica permite comprimento, Tab para ângulo e Enter para confirmar.

Execute **PILAR** (`PI`) e clique a posição: num nível com outro abaixo, o pilar desce até ele; no mais baixo, sobe até o próximo. Sem níveis, a altura padrão é 3 m. Confira a prévia e a cota antes de inserir. **Apoio na base** engasta nós novos no nível mais baixo quando habilitado.

**Ctrl+R** durante o desenho soma 90° ao giro das próximas barras. O **Cardinal** posiciona o perfil em relação ao eixo (1–9 como teclado numérico, 10 = centroide). Ele desloca a representação do perfil; a análise continua usando o eixo da barra, sem introduzir automaticamente excentricidade estrutural.

## Snap e precisão

F3 alterna snap a objetos; **Shift+botão direito** abre os modos permanentes e o snap para um único clique. Também é possível digitar o alias e Enter.

| Modo | Alias para um clique | Referência |
|---|---|---|
| Extremidade | `END` | ponta da barra ou canto do sólido |
| Ponto médio | `MID` | meio da barra ou aresta |
| Centro | `CEN` | centro da ponta circular no Realista |
| Nó | `NOD` | nó analítico |
| Interseção | `INT` | cruzamento real no espaço |
| Interseção aparente | `APP` | cruzamento apenas na projeção; não garante conexão estrutural |
| Perpendicular | `PER` | perpendicular a partir do primeiro ponto |
| Extensão | `EXT` | prolongamento após adquirir uma ponta |
| Paralelo | `PAR` | direção de uma barra adquirida |
| Mais próximo | `NEA` | ponto mais próximo na barra; desligado por padrão |
| Nenhum | `NON` | próximo clique sem snap de objeto |

F8 alterna **ORTO**, F10 o **Polar**, F11 o **Rastreio** e F12 a entrada dinâmica. ORTO e Polar se excluem. Para adquirir uma referência de rastreio, extensão ou paralelo, pause cerca de 0,5 s sobre ela. Ajuste o incremento polar no menu de snap. Confira marcador e nome do modo antes de clicar; arestas ocultas também podem atrair.

## Medir

Execute **MEDIR** (`ME`, `DI`, Ctrl+M). No automático, um clique com snap seleciona um ponto; sem snap, a barra sob o cursor é selecionada como entidade.

| Seleção | Leitura |
|---|---|
| Um ponto / uma barra | coordenadas / comprimento e propriedades |
| Dois pontos | distância, ΔX, ΔY, ΔZ e inclinação |
| Barra e ponto | distância perpendicular e posição na barra |
| Duas barras | distância mínima, ângulo entre eixos e relação espacial |
| Três pontos | ângulo no ponto do meio |

No painel, escolha **Distância**, **Ângulo**, **Área**, **Raio** ou **Cadeia**. No submodo Ângulo por pontos, clique primeiro o vértice. Em Área, contorne o polígono e Enter fecha; uma barra fornece a área da seção. Em Cadeia, clique os trechos e use `+`/`−` para somar/subtrair; Backspace remove o último ponto, Enter fecha. O acumulado separa comprimento, ângulo e área. Clique numa leitura para copiar, ou use Ctrl+C, Copiar e Copiar tudo. Esc limpa a seleção de medida; outro Esc sai.

As medidas são consultas, sem cotas permanentes. Peso considera aço com 7850 kg/m³; área de perfil usa o contorno sem raios de concordância. Para precisão geométrica, prefira câmera ortográfica: o snap tem limitações de projeção em perspectiva.

## Selecionar e editar em lote

Clique para selecionar; arraste da esquerda para a direita para conter objetos e da direita para a esquerda para cruzá-los. Shift soma e Ctrl retira. A árvore e as tabelas também selecionam na vista.

Em **Modelo › Modificar**, use as operações abaixo. **Confira a indicação de seleção:** nos painéis de edição, sem itens selecionados a operação pode atingir o modelo inteiro.

| Operação | Como usar |
|---|---|
| Mover / Copiar (`M` / `CO`) | ponto base e destino com snap ou `@dx,dy,dz`; Copiar aceita destinos sucessivos até Enter |
| Espelhar (`MI`) | dois pontos da linha em planta; cria cópia em plano vertical e mantém a origem |
| Rotacionar (`RO`) | centro e ângulo em graus ou direção clicada; gira em torno do eixo vertical |
| Matriz (`AR`) | quantidades X/Y/Z contando o original e espaçamentos |
| Dividir (`DIV`) | número de partes ou distância desde a ponta inicial |
| Aparar / Estender (`TR` / `EX`) | barra limite e depois as barras; em Aparar, o lado clicado sai |
| Unir nós (`J`) | tolerância; revise apoios, barras duplicadas e cargas removidas |
| Propriedades (`PR`, Ctrl+1) | seção, material, giro e apoios em lote; vazio ou manter preserva o campo |
| Copiar propriedades (`MA`) | barra de origem e destinos; transfere seção, material e giro |

As operações levam as cargas associadas; Dividir redistribui cargas pelos trechos. Cada operação composta admite Ctrl+Z. Nos grips azuis, mova ponta/nó ou meio da barra; **Ctrl no destino copia**. Mover um nó também move as barras ligadas. Apagar um nó remove barras e cargas dependentes.

## Apoios e propriedades

Selecione nós e use **ENGASTE**, **ROTULA** (também `FIXO`, articulado com as três translações presas) ou **LIVRE**. Edite os seis graus de liberdade individualmente em Propriedades. **ROTULAR** (`RB`) alterna rótulas nas duas pontas das barras escolhidas; confira se isso representa a ligação projetada.

Sem seleção, Propriedades oferece Novo nó, casos e ações e carga em área. Com nó, edite posição, apoios e cargas; com barra, seção, material, giro e cargas. O comando Laje distribui carga em vigas; não cria elemento de casca.

## Treliças, contraventamentos, terças e tirantes

Abra **Modelo › Gerar › Treliças e contraventamentos** ou procure a ferramenta em Ctrl+K.

1. Escolha Treliça plana, Tesouras em série, Contraventamento ou Terças e tirantes.
2. Defina geometria, perfis, material, rótulas e apoios. **Usar seleção** toma dois nós para apoiar uma treliça, duas barras ou quatro nós para um painel de contraventamento e as vigas/tesouras para terças.
3. Confira a prévia sobre o modelo, contagens e avisos; clique **Inserir**. Um Ctrl+Z desfaz a inserção.

Há Pratt, Howe, Warren, Warren com montantes, K e Fink; banzos paralelos, uma água, duas águas e arco parabólico. Fink está limitado ao W de quatro painéis. Em série, informe quantidade e espaçamento; terças podem ligar os nós superiores. Contraventamentos incluem X, diagonal, K, V, Λ e mão-francesa. Para terças, escolha uma água de cada vez.

Pontos intermediários podem dividir barras existentes. Revise comprimentos de flambagem e travamentos após gerar. Treliças isoladas podem receber restrições fora do plano; revise-as ao integrar ao conjunto espacial. Diagonais e tirantes são elementos axiais que também resistem à compressão: o motor não oferece barra só de tração. No X, as duas diagonais trabalham, inclusive à compressão.
