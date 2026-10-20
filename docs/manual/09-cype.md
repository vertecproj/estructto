# 9. Importar e comparar CYPE (.ed3)

O Estructto lê arquivos `.ed3` do CYPE 3D. A leitura é só leitura: a obra do CYPE não é alterada.

## Importar

1. Aba **Modelo** › **Importar CYPE** (ou Arquivo › Abrir, escolhendo `.ed3`).
2. Escolha o arquivo (captura 45-cype-importar).
3. O modelo vira um **documento espacial** e substitui o modelo do motor (Desfazer volta).
4. Leia os avisos da importação: vão para o painel Avisos e para o histórico.

## Comparar

Na mesma janela, **Comparar** roda o cálculo do Estructto e confronta com os resultados gravados pelo CYPE no arquivo
(captura 45-cype-comparar). A tabela traz hipótese × grandeza × CYPE × Estructto × diferença em %, cobrindo
deslocamentos, reações e esforços, além das taxas de aproveitamento e dos avisos.

Como ler:
- Diferença pequena em deslocamentos e reações indica o mesmo modelo e a mesma análise.
- Diferença em taxas é esperada quando o critério de norma difere (edição da norma, hipóteses). Veja o aviso da linha.
- Use a comparação como conferência de lógica, não como prova de projeto.

O CYPE 2016 é referência de lógica e de interface. Nenhum código dele é copiado.

## Pela IA

`modelo_importar_cype` importa; `cype_comparar` compara. Ver [página 10](10-uso-com-ia.md). Caminhos: pasta de trabalho do
servidor ou `C:\CYPE Ingenieros\Projetos\`.
