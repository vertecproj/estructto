# Manual do usuário — Estructto v3

Modelagem, análise e verificação de estruturas em um único ambiente 3D, com navegação e comandos inspirados no AutoCAD Plant 3D.

**Responsabilidade técnica:** resultados exigem revisão por engenheiro responsável e ART pertinente. Leia [Limitações e responsabilidade](11-limitacoes.md) antes de usar o software em projeto.

## Percurso de uso

| Página | Tarefas |
|---|---|
| [1. Começar](01-primeiros-passos.md) | novo projeto em três passos, arquivos, recuperação e unidades |
| [2. Modelar, medir e editar](02-modelo.md) | grade, níveis, barras e pilares com snap, medir, edição em lote, treliças e contraventamentos |
| [3. Cargas e vento](03-acoes.md) | casos, cargas, NBR 6123 edificação/por barra, NBR 8400 e combinações |
| [4. Análise](04-analise.md) | calcular, combinações, envoltórias, diagramas e estabilidade |
| [5. Verificação](05-dimensionamento.md) | aproveitamento, critérios e dimensionamento automático |
| [6. Ligações e bases](06-ligacoes-e-bases.md) | biblioteca, projetista, grupos, pior do grupo e placa pelo método NBR |
| [7. Relatório](07-relatorio.md) | PDF/HTML, anexo de bases e memoriais de ligações |
| [8. Vista e exportação](08-vista.md) | pivô automático, ViewCube, perspectiva, Realista, DWG/DXF e STEP |
| [9. CYPE](09-cype.md) | importar .ed3 e comparar resultados |
| [10. Uso com IA](10-uso-com-ia.md) | servidor MCP e fluxo do motor |
| [11. Limitações e responsabilidade](11-limitacoes.md) | cobertura, hipóteses, conferências e ART |
| [12. Comandos e atalhos](12-comandos-e-atalhos.md) | tabela de aliases do código, coordenadas e teclado |

Para começar do zero, crie **Vazio com grade**, ative o nível e desenhe com **BARRA/PILAR**. **Shift+arrastar** orbita sem encerrar o comando. Em seguida, revise apoios e ações, calcule com F5, verifique barras e ligações e gere os documentos da entrega.

## Referências da versão

Manual revisado para a implementação disponível em 21/10/2026. Fontes: [Entrega](../v3/ENTREGA.md), [Status](../v3/STATUS.md), [Modelo espacial](../MODELO-ESPACIAL.md), [Vista 3D](../VISTA3D.md), [Início](../v3/INICIO.md), [Snap e medir](../v3/SNAP-MEDIR.md), [Projetista de ligações](../v3/LIGACOES-UI.md), [Grupos](../v3/LIGACOES-GRUPOS.md), [Placa de base](../v3/PLACA-BASE-V2.md), [Treliças](../v3/TRELICAS.md), [Render](../v3/RENDER.md), [Exportar](../v3/EXPORTAR.md) e [Integração de vento e placa](../v3/INTEGRACAO-BRUNO.md).

Esses registros contêm etapas históricas. Para os recursos integrados, este manual considera também o código atual; por exemplo, vento por barra/NBR 8400 já estão disponíveis e a placa usa o motor NBR. Os limites de aplicação continuam declarados nas páginas de cada tarefa.
