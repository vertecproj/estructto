# 5. Verificar e dimensionar barras

A taxa de aproveitamento compara solicitação e resistência ou o limite de cada critério. **Taxa maior que 1 reprova.** A cor ou uma taxa favorável não assegura que todos os fenômenos relevantes tenham sido cobertos.

## Revisar a verificação

1. Calcule com F5 e abra **Verificação de barras** ou **Tabelas › Verificação**.
2. Ordene pela taxa e localize critério, item de norma e combinação que governam.
3. Selecione a barra e abra **Verificar barra** para examinar solicitações, resistências e avisos.
4. Confira seção, material, eixos, rótulas, comprimentos de flambagem e travamentos. Corrija a hipótese ou seção e recalcule.

**Mapa de aproveitamento** (`CALOR`, `MAPA`, `APROV`) localiza barras críticas na vista, inclusive no Realista. Use os valores e critérios detalhados para decidir a intervenção.

O motor cobre conjuntos de verificações de aço laminado/soldado (NBR 8800), perfis formados a frio (NBR 14762) e concreto (NBR 6118). Confira escopo e edição declarados no resultado. Itens classificados como **não verificado** não constituem aprovação.

## Dimensionamento automático

`DIMENSIONAR` tenta ajustar perfis do catálogo na mesma família em todo o modelo. `DIMSEL` limita a operação às barras selecionadas. A rotina calcula, aumenta perfis reprovados e tenta reduzir os pouco aproveitados, repetindo até oito rodadas. Cada rodada pode ser desfeita com Ctrl+Z.

A busca usa uma estimativa por massa e taxa; não garante perfil ótimo nem menor massa global. Mudanças de rigidez redistribuem esforços. Ao terminar, confira novamente cálculo, flechas, estabilidade, travamentos, ligações e bases.

O **Catálogo de perfis** ajuda a consultar propriedades geométricas. Alterações de seção não definem automaticamente todos os parâmetros de flambagem e ligação. Flechas são verificadas em ELS para as situações de vigas cobertas pelo motor; confira o limite adequado ao uso.

Veja [Limitações](11-limitacoes.md), em especial a fórmula simplificada de distorcional de perfis Ue e os tipos de seção fora do escopo.
