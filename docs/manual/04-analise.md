# 4. Aba Análise

## Calcular

**F5** ou o botão Calcular na barra de acesso rápido. No documento 2D o cálculo interno também é refeito a cada alteração.
Corrija os avisos do modelo antes de calcular; sem isso o cálculo não roda.

## Opções

- **Deformação por cortante**: liga a viga de Timoshenko. Desligado, Euler-Bernoulli.
- **Motor**: Interno ou Python. Ver [primeiros passos](01-primeiros-passos.md).

## Resultado mostrado

A lista **Mostrar** escolhe o caso, a combinação ou a envoltória. No motor Python, "envoltória ELU" é o padrão: cada ponto do
diagrama é o valor de maior módulo entre máximo e mínimo (com sinal). A deformada usa a envoltória ELS.

## Diagramas

**Deformada**, **Normal** (kN, tração positiva), **Cortante** (kN), **Momento** (kN·m, desenhado do lado tracionado).
**Ampliar** e **Reduzir** mudam a escala. Passe o mouse sobre o desenho para ler valores (captura 37-inspecao-mouse).
No documento espacial, o eixo local y da vista é o eixo z local do motor; M_y negativo no vão de viga sai para baixo (lado tracionado).

## Modos de vibração

Botão **Modos de vibração**: frequências, períodos e massa participante dos primeiros modos (captura 40-modal).
Escolher um modo mostra a forma modal (captura 40-modal-modo3).

## Estabilidade

Botão **Estabilidade**: calcula o modelo no motor e mostra γz e deslocamentos-limite (NBR 6118 e 8800), com régua de 1,0 a 1,4
(captura 41-estabilidade). γz acima de 1,1 pede majoração dos esforços ou análise de segunda ordem (captura 41-estabilidade-majorada).
Vale para edifícios com 4 pavimentos ou mais (aviso abaixo disso). P-Δ global de segunda ordem completo não é implementado
no pórtico plano.
