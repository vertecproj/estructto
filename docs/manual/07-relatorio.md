# 7. Aba Relatório

## Memorial

Botão **Relatório** (captura 25-relatorio). Duas versões, escolhidas na janela:

| Versão | Conteúdo |
|---|---|
| **Básico** | Modelo, casos de carga, combinações, diagramas, reações, esforços, tabela de verificação, tabela de placas |
| **Completo** | Básico + seção 9, Memorial de cálculo: método da rigidez, equilíbrio global (fechamento ΣFx, ΣFy, ΣM), cada barra com fórmula → valores → resultado → limite → aproveitamento, placas de base e limitações |

Imprima ou salve em PDF (impressora "Microsoft Print to PDF"). Cabeçalho, carimbo e logo usam a marca Estructto.
O relatório não altera o modelo. Unidades das substituições: N, mm, MPa. Esforços de entrada aparecem em kN e kN·m.

O memorial declara o que **não** verifica (seção 9.5). Mantenha essa seção ao entregar o documento.
Padrão de redação: [PADRAO-MEMORIAL.md](../PADRAO-MEMORIAL.md).

Com o motor Python, o relatório usa o resultado do motor mostrado em Análise.

## Exportar CSV

**Exportar CSV** (Ctrl+E): nós, barras, reações, esforços e verificação, para abrir no Excel.

## Antes de entregar

1. Confira que o resultado mostrado é o que você quer relatar (caso, combinação ou envoltória).
2. Confira à mão ao menos uma conta de cada tipo (tensão, flecha, flambagem, fixação, chapa, solda).
3. Confirme que o fechamento do equilíbrio global é zero.
4. Submeta ao engenheiro responsável (ART).
