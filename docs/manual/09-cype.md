# 9. Importar e comparar CYPE 3D

O Estructto lê arquivos **.ed3** do CYPE 3D. A importação é de leitura: o arquivo de origem permanece preservado.

## Importar

1. Use **Importar CYPE 3D** na tela inicial, **Arquivo › Importar** ou **Arquivo › Abrir** com `.ed3`.
2. Escolha o arquivo. O modelo importado abre como documento espacial numa nova aba.
3. Leia os avisos no histórico e no painel Avisos. Confira geometria, seções, materiais, apoios, rótulas, cargas e parâmetros de verificação.
4. Salve o resultado como `.estructto` ou `.json` e calcule com F5.

A conversão não assegura equivalência de todas as hipóteses. Ligações, presilhas e parâmetros fora do escopo exigem revisão e verificações complementares.

## Comparar

Na ferramenta **Comparar com CYPE**, selecione a obra e clique em **Comparar**. O programa calcula no motor e confronta os resultados disponíveis no arquivo: hipótese, grandeza, CYPE, Estructto e diferença percentual. A comparação pode incluir deslocamentos, reações, esforços e taxas, conforme os dados gravados.

Confira primeiro unidades, orientação dos eixos, cargas, vínculos e hipóteses de análise. Depois examine diferenças de critérios e edições de norma. Coincidência de resultados em um caso não valida todo o projeto; discrepâncias precisam ser explicadas antes do uso.

Pela IA, `modelo_importar_cype` e `cype_comparar` atendem esse fluxo; veja [Uso com IA](10-uso-com-ia.md). Consulte as [limitações](11-limitacoes.md).
