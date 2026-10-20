# 8. Aba Vista

## Navegar

| Ação | Como |
|---|---|
| Enquadrar | Botão, F4, ou duplo clique na roda |
| Aproximar / Afastar | Botões, ou roda do mouse (zoom no cursor) |
| Pan | Botão do meio ou direito arrastando |
| **Vista 3D** | Alterna entre o desenho 2D e a vista 3D |

## Vista 3D

Câmera ortográfica (captura 40-vista3d-iso).

| Ação | Mouse / teclado |
|---|---|
| Órbita | Botão esquerdo arrastando, ou Shift + meio |
| Pan | Botão do meio ou direito |
| Zoom | Roda |
| Escolher barra ou nó | Clique (abre o Inspector) |
| Topo / Frente / Lado / Iso | Seletor no canto superior direito |

O HUD escolhe o que aparece: diagrama (N, V, M), deformada com escala, mapa de aproveitamento
(capturas 40-vista3d-momento, 40-vista3d-deformada, 40-vista3d-aproveitamento). No documento espacial a vista 3D está sempre ligada
e mostra o resultado do motor, com legenda de máximo e mínimo (capturas 41-espacial-aproveitamento, 41-espacial-combinacao).

Limites: a deformada é desenhada em cordas entre os nós deslocados (sem a elástica dentro da barra); a seção é um prisma
quadrado fino, não o perfil real; os diagramas 3D não mostram envoltória (uma curva por barra).

## Vista Realista

Não existe na versão atual. O código e a documentação em `docs/VISTA3D.md` não registram um modo Realista (seção real
extrudada, sombras). Quando existir, esta seção será atualizada.

## Mostrar

**Reações**, **Valores** (nos diagramas), **Cargas**, **Nomes** (perfil e comprimento). Todos ligados por padrão.

## Grade

**Passo da grade (m)**: espaçamento da grade de desenho (padrão 0,5).

## Painéis

**Projeto** (árvore de nós, barras, seções, materiais, casos; clique escolhe na vista), **Propriedades** (inspetor do item),
**Tabelas** (nós, barras, verificação). **Atalhos e ajuda** (F1).

## Animações

Movimento discreto (120 a 400 ms), nunca atrasa um resultado. Desligue em Windows › Acessibilidade › Efeitos visuais ›
Efeitos de animação. Ver [ANIMACOES.md](../ANIMACOES.md).
