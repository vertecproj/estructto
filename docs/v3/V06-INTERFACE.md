# v0.6 Frente B: interface

Correções pedidas pelo usuário em 22/10/2026 (capturas 13 e 14 e pedidos de texto).

| Item | O que mudou | Onde |
|---|---|---|
| B1 | O desenho da seção no Catálogo segue a forma (`secao.props.tipo` do motor): I/H, U, Ue, L (igual e desigual), T, barra chata, tubo quadrado/retangular (raio externo 2t, interno t) e tubo redondo. | `DesenhoSecao.cs` |
| B2 | Seção da barra (Propriedades) e botão Seção da faixa: seletor com busca, seções "No modelo" no topo, recentes (8, em `opcoes.txt`) e a árvore do catálogo (nível 1, família, série, perfil; folhas criadas ao expandir). A árvore é a mesma da JanelaCatalogo. | `SeletorSecao.cs`, `DocumentoEspacial.cs`, `Desenho3D.cs` |
| B3 | Novo projeto "Em branco": sem grade, sem níveis, sem barras. A mensagem de modelo vazio saiu do status/avisos do documento. | `Inicio.cs`, `JanelaNovoProjeto.cs` |
| B4 | Painéis Avisos e Tabelas nascem fechados; o estado escolhido fica em `opcoes.txt` (`painel_tabelas`, `painel_avisos`). O painel de avisos não abre mais sozinho (o selo na barra de status abre). | `PaineisEstado.cs` |
| B5 | Botão **Nó** (comando `NO`, aliases `NÓ`, `NODE`, `PT`): clique com atração ou `x,y,z` digitado; Enter termina. | `Comandos3DNo.cs` |
| B6 | A aba Modelo virou quatro: **Estrutura** (Gerar, Planos: Grade, Níveis, Elevações, Copiar pavimento, Pilares entre níveis, Treliças), **Desenhar** (Selecionar, Nó, Barra, Pilar, Medir, Laje, Configurações), **Editar** (Modificar), **Apoios**. Botões, atalhos e comandos iguais. | `MainWindow.xaml.cs` |
| B7 | Campos de carga nascem em 0 (antes: Fz −10 nodal e pontual, −5 distribuída, 3 kN/m² em área). Carga com todos os valores 0 não é adicionada. | `CargasEspacial.cs`, `DocumentoEspacial.cs` |
| B8 | Peso próprio ligado por padrão: modelo novo da interface (`POST /api/modelo/novo`) nasce com o caso `PP`; arquivo sem a chave `peso_proprio` ganha `PP` ao ler (`Modelo.from_dict`). `peso_proprio: null` explícito continua desligado. O motor já calcula só com o PP (combinações ELU/ELS e verificação). | `modelo.py`, `web/servidor.py` |

## Provas
- Motor: `testes/test_peso_proprio_so.py` (4 testes: arquivo antigo, PP desligado de propósito, cálculo só com PP, modelo novo da interface).
- Fluxo D (`--testes-fluxo`): modelo novo sem cargas, `peso_proprio = PP` e `Calculado`.
- Capturas: `ESTRUCTTO_CAPTURAS=v06` gera `46-catalogo-forma-*` e `47-*` (abas da faixa, painéis, seletor, em branco).

## Fora do escopo desta frente
O texto "Modelo vazio: digite L…" desenhado dentro da Vista3D (`Vista3D.cs`) pertence à frente C (C3).
