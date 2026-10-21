# 3. Cargas, vento e combinações

## Casos e cargas

Abra **Ações › Casos de carga** ou `CASOS`. Defina tipo e subtipo de cada caso conforme a ação. O **Caso ativo** recebe os lançamentos e determina as setas mostradas na vista. Confira-o antes de adicionar cargas.

| Carga | Onde e como informar |
|---|---|
| Nodal | selecione o nó; Fx, Fy e Fz globais em kN |
| Pontual em barra | barra; posição a desde ni, em m, e componentes globais em kN |
| Distribuída | barra; qx, qy, qz globais em kN/m nos extremos ni e nj |
| Em área | sem seleção, Propriedades › Carga em área; nível, vigas em X ou Y, pressão em kN/m² e caso |
Os casos têm tipo G (permanente), Q (variável), V (vento), S (sismo) ou E (excepcional). **Caso ativo** recebe as cargas que você lança.
A janela de casos é a do motor (captura 32-acoes-casos).

Valores iguais nas pontas produzem carga uniforme; diferentes, trapezoidal. Edite os campos da carga existente e **Gravar**, ou apague-a. Revise o peso próprio configurado no projeto para evitar contagem duplicada com cargas permanentes manuais.

A carga em área usa faixas de influência de uma direção: a pressão é convertida em cargas lineares nas vigas paralelas escolhidas. A borda recebe só o lado interno. Não há detecção de laje, aberturas ou balanços; sobreposições parciais são consideradas inteiras. Confira a soma das cargas e a área efetivamente carregada.

As setas representam o caso ativo, sem os fatores da combinação de resultados. Momentos nodais não têm seta e as forças não têm rótulos de valor; confira-os nas tabelas e nos campos.

## Vento: escolher o modelo de ação

Em **Ações › Vento**, selecione o modo apropriado. **Gerar ações de vento** grava parâmetros e cria ou recria os casos do modo escolhido; revise a prévia por direção, as cargas geradas e eventuais casos de outra configuração antes de calcular.

### 6123 · edificação

Para a ação global, informe V0, S1, S3, categoria de rugosidade, classe, dimensões de planta e direções ±X/±Y. Confira os pavimentos antes de gerar. Escolha coeficientes automáticos ou informe Ca e Cpi quando aplicável; revise permeabilidade e geometria de cobertura.

A prévia permite conferir S2, velocidade característica, pressão e forças por pavimento. O motor usa `Vk = V0·S1·S2·S3` e `q = 0,613·Vk²` em N/m². Para galpões, confira pressões externas/internas e casos por direção e Cpi. A base é NBR 6123:2023, com coeficientes/tabelas identificados no motor que ainda vêm da edição de 1988.

### 6123 · por barra

Use para barras prismáticas expostas de estruturas abertas. Informe V0, S1, S3, categoria, classe automática ou A/B/C, fator de comprimento finito K e direções. A classe automática depende da caixa do modelo em cada direção.

A carga distribuída normal à barra é `w = Cf·q(z)·K·c·sen²α`. A prévia barra a barra mostra S2, q, Cf, largura exposta c, K, ângulo e carga em kN/m, com a fonte da tabela. Confira a orientação das seções: ela altera Cf e a largura exposta. Barras paralelas ao vento não recebem carga. Desabilitar K adota K = 1.

Esse modo não acrescenta fechamentos, equipamentos nem proteção entre barras de reticulados. Lance as ações que faltam no caso adequado e revise a resultante. Não some automaticamente vento de edificação e vento por barra sobre a mesma superfície exposta.

### 8400 · equipamentos

O modo implementa **NBR 8400:1984** para equipamentos de levantamento. Informe velocidade de serviço, direções, grupo (Mx) e ψ. O programa gera vento de serviço `VS±X/±Y` e máximo `VM±X/±Y`; a prévia informa pressão, coeficiente e carga por barra.

Marque **usar as combinações da NBR 8400** quando esse for o critério do projeto. Em **Ações › Combinações**, confirme NBR 8400, grupo, ψ e casos I, II e III. O motor converte esforços para sua verificação em estados limites pelo fator ν/γa1; a equivalência é exata para escoamento e aproximada para flambagem. Essa seleção não modela automaticamente toda a operação ou a dinâmica do equipamento.

## Pavimentos, sismo e combinações

Em **Pavimentos**, confira cotas e diafragma rígido. Em **Sismo**, o método usa forças horizontais equivalentes da NBR 15421:2006 para estruturas regulares; não há análise modal espectral.

Em **Combinações**, escolha **NBR 8681** (padrão) ou **NBR 8400**. Na NBR 8681, revise uso, coeficientes e tipos/subtipos dos casos; filtre ELU e ELS e confira os fatores de cada caso. Direções alternativas de vento são mutuamente exclusivas, inclusive serviço/máximo. A tabela é gerada: corrija casos e configuração para mudar o conjunto.

Após mudar geometria, cargas ou configuração, rode **F5** novamente. Consulte as [limitações](11-limitacoes.md), inclusive as ações que precisam ser lançadas manualmente.
Gerar vento e sismo cria casos V (vento) e S (sismo: SX, SY e as torções acidentais SX_TOR, SY_TOR). Depois rode **Calcular** (F5): as combinações incluem esses casos automaticamente.
