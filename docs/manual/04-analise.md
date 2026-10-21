# 4. Calcular e interpretar resultados

## Preparar e calcular

Revise nós conectados, apoios, rótulas, materiais, seções, casos e combinações. Consulte **Avisos**; clique num item para localizar o elemento e corrija erros de validação. Use **Calcular**, **F5**, **Ctrl+F9** ou `CALC`. Um mecanismo ou matriz singular exige revisar vínculos e conectividade antes de aproveitar resultados.

O cálculo é feito pelo motor Python. Alterações do modelo ou das ações invalidam o cálculo anterior: calcule novamente antes de verificar barras, ligações ou emitir relatório.

## Resultado mostrado

Na lista **Mostrar**, escolha uma combinação ou **Envoltória ELU (motor)**. Na envoltória, cada estação do diagrama mostra o extremo de maior módulo, preservando o sinal. Não são desenhadas simultaneamente as curvas máxima e mínima. A deformada da envoltória usa ELS.

Uma envoltória pode reunir extremos de combinações diferentes. Para examinar equilíbrio e simultaneidade, selecione uma combinação específica e confira as reações e os esforços correspondentes.

| Resultado | Leitura |
|---|---|
| Normal (`N`) | kN; tração positiva |
| Cortante (`Q`) | Vz local, em kN |
| Momento (`MOMENTO`) | My local, em kN·m; diagrama do lado tracionado |
| Deformada (`DEF`) | deslocamentos com escala visual ajustável |
| Aproveitamento (`CALOR`) | pior taxa da verificação da barra |

O eixo de desenho do diagrama corresponde ao z local do motor. Para barras com giro e esforços nos demais eixos, confira os resultados tabulados e a verificação completa. A deformada desenhada liga os nós deslocados por segmentos; ela não mostra a elástica interna da barra.

## Tabelas e estabilidade

**Tabelas** reúne nós, barras e verificação. Confira deslocamentos, esforços extremos, combinação crítica e reações nos resultados do cálculo. A escala do diagrama muda apenas sua apresentação.

Em **Modos de vibração**, examine frequências, períodos, massa participante e formas modais disponíveis. Uma análise modal não substitui a avaliação dinâmica da ação.

Em **Estabilidade**, examine os indicadores e avisos do motor. O critério γz para edifícios tem domínio de aplicação, incluindo a indicação de quatro pavimentos ou mais. Um valor alto exige que o projetista avalie efeitos de segunda ordem e o método apropriado. Não trate o indicador como uma análise global completa de segunda ordem.

Continue em [Verificação e dimensionamento](05-dimensionamento.md). A aprovação numérica depende da validade das hipóteses e das [limitações](11-limitacoes.md).
