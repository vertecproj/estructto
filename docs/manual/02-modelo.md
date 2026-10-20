# 2. Aba Modelo

Gerar, desenhar, modificar, apoiar e dar seção e material à estrutura.

## Gerar

| Botão | Resultado |
|---|---|
| **Gerar estrutura** | Assistente 2D: viga contínua, pórtico de vários andares, treliça, galpão (captura 26-assistente-portico) |
| **Gerar 3D** | Documento espacial no motor: edifício, galpão ou pórtico 3D, com G e Q uniformes (kN/m) nas vigas (captura 41-gerar3d-edificio) |
| **Importar CYPE** | Importa `.ed3` ou compara com o CYPE. Ver [página 9](09-cype.md) |

## Desenhar e modificar (documento 2D)

- **Selecionar**: clique; arrastar da esquerda para a direita seleciona o que está dentro da janela; da direita para a esquerda, o que cruza.
- **Barra**: clique os pontos em sequência. Digite um número para o comprimento exato, ou `x,y`, `@dx,dy`, `@L<ângulo`. Botão direito ou Esc termina.
- **Nó**: insere nó; sobre uma barra, divide a barra.
- **Medir**: distância, ΔX, ΔY e ângulo.
- **Mover**, **Copiar** (matriz linear, com apoios e cargas), **Espelhar** (reta x = valor ou y = valor), **Dividir barra**, **Apagar**.
- Comandos por texto também funcionam (por exemplo `MOVER dx dy`).

## Barra contínua

Peça única dividida por nós: mesmo perfil, dimensionada junta (o comprimento de flambagem usa a barra inteira).
**Unir trechos** e **Separar tudo** na faixa; "Tirar trecho" e "Selecionar a barra" no botão direito.

## Apoios e vínculos

| Botão | Efeito |
|---|---|
| Engaste | Trava X, Y e giro |
| Fixo | Trava X e Y (2º gênero) |
| Móvel | Trava só Y (1º gênero) |
| Móvel horizontal | Trava só X |
| Sem apoio | Remove apoio e molas |
| Rótula | Clique perto da ponta da barra para pôr ou tirar a rótula naquela ponta |

No documento espacial, o painel Propriedades edita os 6 graus de liberdade do apoio do nó.

## Seções e materiais

Escolha **Família** (filtra a lista), **Seção** e **Material**; depois **Aplicar à seleção** ou **Aplicar a todas**.
Sem seleção, a escolha vale para as próximas barras desenhadas. Seção também aceita digitação:
`Ret 20x50` (cm), `Circ 30` (cm), `A=45,7 I=3437` (cm², cm⁴), `TC 48.3x3...`. Material aceita `E=25000` (MPa).

## Abas contextuais

Com barra selecionada aparece a aba **Barra** (Verificar barra, Dimensionar, perfil deitado, rótulas nas pontas, dividir, apagar).
Com nó selecionado, a aba **Nó** (apoios, aplicar carga, placa de base, apagar).

## Documento espacial: edição no painel Propriedades

- Sem escolha: **Novo nó** (X, Y, Z).
- Nó: **Barra até outro nó** (clique no outro nó), **Nó + barra** (deslocamento dX, dY, dZ), **Apagar nó** (leva barras e cargas), cargas nodais Fx, Fy, Fz por caso.
- Barra: seção (do modelo ou do catálogo do motor), **Apagar barra**, carga distribuída qx, qy, qz (global, kN/m) por caso.
- Barra nova copia seção e material da última barra do modelo.
- Ainda sem tela: cargas pontuais em barra e criação de casos novos (o motor aceita; use o MCP).
