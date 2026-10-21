# 8. Navegar, usar o Realista e exportar

## Navegação

| Ação | Controle |
|---|---|
| Orbitar | **Shift+arrastar com botão do meio ou esquerdo**; mantém o comando em curso |
| Pan | botão do meio arrastando |
| Zoom no cursor | roda do mouse |
| Enquadrar tudo | duplo clique no botão do meio, F4, `Z` ou `ZE` |
| Enquadrar seleção | `ZO` ou `Z O` |
| Topo / Frente / Lado / Isométrica | botões, `TOPO` / `FRENTE` / `LADO` / `ISO`; Ctrl+teclado numérico 7/1/3/5 |
| Perspectiva | `PERSP`, botão Perspectiva ou seletor sob o ViewCube |

**O pivô é automático sob o cursor.** Ao iniciar a órbita, o programa usa o ponto da peça sob o mouse e o mantém fixo durante o arrasto, marcado por ponto ciano com anel. Se não houver acerto, procura nó/barra próximos, o plano de trabalho ou o centro dos nós visíveis. A seleção não define o pivô. Posicione o cursor na junta que quer examinar antes de orbitar.

Na ortográfica e na perspectiva, o ponto de referência permanece sob o cursor ao girar ou usar a roda. Na perspectiva, o zoom aproxima o observador da peça e permite avançar pela cena. Para recuperar uma vista geral, enquadre tudo.

O **ViewCube**, no canto superior direito, oferece 26 regiões de faces, arestas e cantos. Clique para uma vista orientada, use a casinha para isométrica ou arraste o cubo para orbitar em torno do centro da vista. O seletor abaixo alterna **Ortográfica/Perspectiva**. Planta e elevações continuam mostrando o mesmo modelo espacial.

## Realista

Ative **Vista › Realista** ou `REALISTA` para ver perfis extrudados, materiais, recortes nas juntas e componentes das ligações. Com GPU compatível, o modo DirectX 11 oferece sombras entre peças, oclusão ambiente, suavização e reflexos metálicos. Sem GPU compatível, o programa usa automaticamente a representação nativa, com menos efeitos e acabamento.

Ligações aparecem depois de identificar os grupos e aplicar um projeto. Chapas, parafusos e soldas podem ser simplificados ou ocultados à distância para manter o desempenho. Gussets precisam dos dados geométricos exigidos pelo tipo. Não deduza resistência ou detalhamento de fabricação pela aparência; dados padrão também podem produzir geometria sem aprovação.

O ponto cardinal e o giro alteram a posição visual dos perfis. Perfis sem forma conhecida têm representação aproximada. Setas e diagramas podem aparecer por cima das peças. A deformada usa segmentos entre nós deslocados, inclusive no Realista.

## Painéis e exibição

**Projeto** reúne nós, barras, seções, materiais e casos; **Propriedades** edita a seleção; **Tabelas** consulta nós, barras e verificação; **Avisos** localiza problemas. Restaure painéis ocultos em **Arquivo › Opções**. F1 abre ajuda; F2 mostra o histórico. O HUD controla diagrama, escala, deformada e aproveitamento.

As setas em **Mostrar › Cargas** são do caso ativo. F7 mostra a grade do plano, F9 alterna snap nela e o passo da grade define seu espaçamento em metros.

## Exportar DWG, DXF e STEP

1. Salve e confira o modelo, perfis, giros e pontos cardinais.
2. Abra **Arquivo › Exportar** e escolha **DWG…**, **DXF…** ou **STEP…**; as mesmas ações estão em Ctrl+K.
3. Escolha o destino e confira escala, orientação e peças no programa receptor. A exportação converte metros do modelo para **milímetros**.

| Formato | Conteúdo |
|---|---|
| DWG / DXF | AutoCAD 2018 (AC1032); uma malha POLYFACE por barra, camadas por seção e linhas de eixo na camada ANALITICO |
| STEP AP214 | montagem com uma peça por barra e sólidos facetados fechados |

O exportador considera giro e cardinal. **DWG usa malhas, sem entidades 3DSOLID.** STEP tem faces planas; tubos circulares usam 24 facetas e não levam cor/material.

Os arquivos exportam a geometria das barras, sem cargas, resultados, chapas, parafusos, soldas ou ligações. As pontas são retas e não levam os recortes/acabamento do Realista. Ue sai sem enrijecedores e 2L costas com costas é aproximado por I. Verifique a forma de seções sem dados geométricos. O DWG foi relido pelo próprio exportador; confira a compatibilidade no CAD de destino. Esses arquivos não substituem desenhos de fabricação.
