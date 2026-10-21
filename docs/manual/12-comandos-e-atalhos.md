# 12. Linha de comando e atalhos

Digite com o foco na vista e confirme por **Enter ou Espaço**. Com a linha vazia, Enter/Espaço repete o último comando. A paleta **Ctrl+K** permite procurar ações pelo nome. A tabela corresponde ao registro de `codigo/Estructto/Comandos3D.cs` da v3; comandos sem alias são digitados pelo nome completo.

## Comandos e aliases

| Comando | Aliases | Ação |
|---|---|---|
| `BARRA` | `L`, `B`, `LINE`, `VG`, `VIGA`, `MEMBRO` | Barra (perfil das Configurações) em cadeia de cliques no plano de trabalho |
| `PILAR` | `PI`, `PL`, `COLUNA` | Pilar: clique o ponto; a altura vem dos níveis |
| `SELECIONAR` | `S`, `SEL` | Selecionar (clique, janela, cruzamento) |
| `MOVER` | `M`, `MV`, `MOVE` | Mover a seleção: ponto base e segundo ponto |
| `COPIAR` | `CO`, `CP`, `COPY` | Copiar a seleção: ponto base e destinos (vários; Enter termina) |
| `ESPELHAR` | `MI`, `MIRROR` | Cópia espelhada: dois pontos da linha do espelho (em planta) |
| `ROTACIONAR` | `RO`, `ROTATE` | Girar a seleção em torno do eixo vertical: ponto base e ângulo (digite) ou direção (clique) |
| `MATRIZ` | `AR`, `ARRAY` | Matriz retangular da seleção (painel) |
| `APARAR` | `TR`, `TRIM` | Aparar barras na barra limite (o lado clicado sai) |
| `ESTENDER` | `EX`, `EXTEND` | Estender barras até a barra limite (a ponta mais perto do clique) |
| `DIVIDIR` | `DIV` | Dividir barra (painel) |
| `UNIR` | `J`, `JOIN` | Unir nós coincidentes (painel) |
| `APAGAR` | `E`, `ERASE`, `DEL` | Apagar a seleção |
| `PROPRIEDADES` | `PR`, `PROPS` | Propriedades da escolha (com várias: em lote) |
| `COPIARPROPRIEDADES` | `MA`, `MATCHPROP` | Copiar perfil, material e giro de uma barra para outras |
| `MEDIR` | `DI`, `DIST`, `ME`, `MED`, `MEA` | Medir distâncias, ângulos, áreas, raio e cadeia |
| `ID` | ? | Coordenadas de um ponto |
| `ZOOM` | `Z`, `ZE` | Z = enquadrar tudo; Z O = a seleção |
| `ZO` | ? | Zoom na seleção |
| `GRADE` | `GR`, `GRID` | Grade estrutural: eixos, filas e níveis (0,@6,@6) |
| `PLACA` | ? | Placa de base no nó do pé do pilar |
| `ENGASTE` | ? | Engastar os nós escolhidos |
| `ROTULA` | `FIXO` | Apoio articulado nos nós escolhidos |
| `LIVRE` | ? | Tirar o apoio dos nós escolhidos |
| `ROTULAR` | `RB` | Rótula nas duas pontas das barras escolhidas (liga/desliga) |
| `CALCULAR` | `CALC` | Calcular (F5) |
| `DIMENSIONAR` | ? | Ajustar perfis na família por estimativa de massa; conferir o resultado |
| `DIMSEL` | ? | Dimensionar só as barras escolhidas |
| `RELATORIO` | `RELATÓRIO` | Relatório NBR do motor + placas |
| `TABELAS` | ? | Tabelas de nós, barras e verificação |
| `CASOS` | ? | Casos de carga |
| `NORMAL` | `N` | Diagrama N |
| `CORTANTE` | `Q` | Diagrama V |
| `MOMENTO` | ? | Diagrama M |
| `DEFORMADA` | `DEF` | Deformada |
| `CALOR` | `MAPA`, `APROV` | Mapa de aproveitamento |
| `TOPO` | `PLANTA` | Vista de topo (planta) |
| `FRENTE` | ? | Vista de frente (XZ) |
| `LADO` | `DIREITA` | Vista lateral direita (YZ) |
| `ISO` | `HOME` | Vista isométrica |
| `PERSPECTIVA` | `PERSP` | Liga/desliga a perspectiva |
| `REALISTA` | ? | Perfis reais (liga/desliga) |
| `NIVEL` | ? | Plano de trabalho no nível acima (+) ou abaixo (-): NIVEL +  /  NIVEL - |
| `ORTO` | `ORTHO` | ORTO (F8) |
| `POLAR` | ? | Polar (F10) |
| `OSNAP` | `OS` | Snap a objetos (F3) |
| `SELTODAS` | `TODOS` | Selecionar tudo (Ctrl+A) |
| `SELNOS` | ? | Selecionar todos os nós |
| `SELBASE` | ? | Selecionar os nós da cota mais baixa |
| `DESFAZER` | `U`, `UNDO` | Desfazer (Ctrl+Z) |
| `REFAZER` | `REDO` | Refazer (Ctrl+Y) |
| `NOVO` | `NEW` | Modelo em branco (Ctrl+N) |
| `ABRIR` | `OPEN` | Abrir arquivo (Ctrl+O) |
| `SALVAR` | `SAVE`, `QSAVE` | Salvar (Ctrl+S) |
| `ASSISTENTE` | ? | Gerar estrutura pelo assistente |
| `ABA` | ? | Ir para a aba da faixa: ABA nome |
| `AJUDA` | `?` | Lista dos comandos |

`Z O` enquadra a seleção. `NIVEL +` sobe e `NIVEL -` desce o plano. `ABA nome` ativa uma aba da faixa pelo início do nome. Para novos projetos, prefira Ctrl+N e o fluxo de três passos; ASSISTENTE é um acesso adicional a geradores.

Em Mover, Copiar e operações que pedem objetos, selecione antes ou marque quando solicitado e Enter encerra a seleção. ENGASTE, ROTULA, LIVRE, ROTULAR e DIMSEL exigem selecionar previamente os elementos apropriados. Apagar nó também remove suas barras e cargas.

## Coordenadas e snap único

Use metros e **ponto decimal**; vírgula separa componentes. `#x,y,z` é absoluto; `@dx,dy,dz` é relativo explícito. Sem prefixo, o primeiro ponto é absoluto e os seguintes são relativos ao primeiro ponto do pedido. `L<ângulo` informa distância e ângulo no plano. Comprimento simples segue a direção do cursor; Tab alterna comprimento/ângulo na entrada dinâmica.

Os aliases `END`, `MID`, `CEN`, `NOD`, `INT`, `APP`, `PER`, `EXT`, `PAR`, `NEA` e `NON` valem para o próximo clique. Também estão no menu **Shift+botão direito**. Veja [Snap e Medir](02-modelo.md).

## Teclado

| Tecla | Ação |
|---|---|
| Ctrl+N / Ctrl+O / Ctrl+S | novo projeto / abrir / salvar |
| Ctrl+Shift+S | salvar como |
| Ctrl+Z / Ctrl+Y | desfazer / refazer |
| Ctrl+A / Del | selecionar tudo / apagar |
| Ctrl+1 / Ctrl+M / Ctrl+K | propriedades / medir / paleta |
| Ctrl+P | relatório |
| Ctrl+Tab / Ctrl+Shift+Tab | próximo documento / anterior |
| Ctrl+F4 | fechar documento |
| Ctrl+R durante desenho | giro das próximas barras +90° |
| F1 / F2 | ajuda / histórico |
| F3 | snap a objetos |
| F4 | enquadrar tudo |
| F5 ou Ctrl+F9 | calcular |
| F6 | informa que UCS dinâmico não está disponível |
| F7 | mostrar grade do plano |
| F8 | ORTO |
| F9 | atração à grade |
| F10 | Polar |
| F11 | rastreamento de objetos |
| F12 | entrada dinâmica |
| PgUp / PgDn | plano no nível acima / abaixo |
| Ctrl+teclado numérico 7/1/3/5 | topo / frente / lado / isométrica |
| Shift+arrastar | orbitar sem cancelar comando |
| Botão direito | concluir corrente ou pedido de objetos |

**Esc cancela em cascata:** texto digitado, comprimento/ângulo travado, grip ativo, pedido de ponto/objeto, corrente de barra, ferramenta e seleção. Pode ser preciso pressionar mais de uma vez para sair por completo. No Medir, Esc limpa as escolhas e outro Esc sai.

Os atalhos são os do Estructto: F4 enquadra e F5 calcula. Consulte [Navegação](08-vista.md) para pivô automático e zoom no cursor.
