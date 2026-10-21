# 2. Aba Modelo

Modelar em 3D como no Plant 3D: grade, barras e pilares, chapas, modificar, apoios e configurações das próximas barras.
O modelo novo abre em planta no térreo; **Shift + arrastar** orbita sem interromper o comando.

## Gerar

| Botão | Resultado |
|---|---|
| **Gerar 3D** | Edifício, galpão ou pórtico gerado pelo motor, com G e Q nas vigas (captura 41-gerar3d-edificio) |
| **Assistente** | Viga contínua, pórtico, treliça ou galpão com prévia (captura 26-assistente-portico); o resultado abre em 3D |
| **Importar CYPE** | `.ed3`; ver [página 9](09-cype.md) |
| **Treliças** | Treliças, tesouras, contraventamentos e terças |

## Grade e planos de trabalho

- **Grade** (`GR`): eixos em X, filas em Y e níveis em Z como `0,@6,@6,@7.5` (m, ponto decimal, @ = a partir da anterior).
  Os cruzamentos atraem o cursor; cada nível vira pavimento e plano de trabalho (captura 54-a1-grade).
- **PgUp / PgDn**: plano de trabalho no nível de cima / de baixo. **Nível**, **Elevação X/Y**, **3D completo**, **Níveis**.

## Barras (desenhar)

| Comando | Uso |
|---|---|
| **Barra** (`L`) | cliques em sequência no plano de trabalho; digite o comprimento (direção do cursor), Tab o ângulo, ou `x,y,z`, `@dx,dy,dz`; Enter, Esc ou botão direito termina |
| **Pilar** (`PI`) | clique o ponto: desce até o nível de baixo (no térreo, sobe até o de cima) |
| **Selecionar** (`S`) | clique; arraste esq.→dir. = janela, dir.→esq. = cruzamento; Shift soma, Ctrl tira |
| **Medir** (`ME`, Ctrl+M) | distância, ângulo, área, raio, cadeia (painel Medir) |

Snap a objetos (F3), ORTO (F8), polar (F10), rastreamento (F11) e entrada dinâmica (F12) na barra de status.

## Configurações (próximas barras)

**Seção** (do modelo ou do catálogo), **Material**, **Giro** (Ctrl+R soma 90° durante o desenho), **Cardinal** (1–9 como o
teclado numérico, 10 = centroide; guardado por barra) e **Apoio na base** (nó novo no nível mais baixo nasce engastado).

## Modificar

Mover (`M`), Copiar (`CO`, várias cópias até Enter), Espelhar (`MI`, dois pontos da linha em planta), Rotacionar (`RO`,
ponto base e ângulo digitado ou direção), Matriz (`AR`), Dividir barra (`DIV`), Aparar (`TR`) e Estender (`EX`) até uma barra
limite, Unir nós (`J`), Apagar (`E`, Del), Copiar propriedades (`MA`), Em lote. Escolha antes ou depois do comando (clique
soma; Enter termina a escolha). Cada operação é um Ctrl+Z.

**Grips:** com barras ou nós escolhidos aparecem quadradinhos azuis. Clique num e clique o destino: a ponta (ou o nó) vai até
lá com as barras ligadas; o do meio move a barra; com **Ctrl** copia (captura 54-a1-grip).

## Apoios

**Engaste**, **Articulado**, **Livre** nos nós escolhidos; **Rótulas na barra** liga/desliga as duas pontas. Graus de
liberdade avulsos no painel Propriedades.

## Chapas

O motor não tem elemento de placa: **Laje** distribui uma carga em área (kN/m²) nas vigas de um nível.

## Painel Propriedades

- Sem escolha: **Novo nó** (X, Y, Z), casos e ações, carga em área.
- Nó: posição, 6 apoios, **Barra até outro nó**, **Nó + barra**, **Apagar nó**, cargas nodais por caso.
- Barra: seção (do modelo ou do catálogo), material, giro, verificação, cargas distribuídas e pontuais por caso.
