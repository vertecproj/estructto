# Revisão independente de correção — ligações e placa de base

Data: 21/10/2026. Branch: `wt-cx-revisao`. Código examinado no commit `38d2d6ee2e3cb65455eaa10a5d791cb172d37089`.

**Resultado: 11 achados — 6 críticos, 4 altos e 1 médio.** Há aprovações indevidas reproduzidas apesar de a suíte existente passar. Nenhum arquivo de código ou de testes foi alterado nesta revisão.

## Escopo e método

Foram lidos `LIGACOES-CONTRATO.md`, `ITENS-NORMA.md`, `PLACA-BASE-V2.md` e os seis documentos de `docs/v3/ligacoes/`. Foram examinados os oito módulos pedidos: `ligacoes.py`, `ligacoes_flexiveis.py`, `ligacoes_rigidas.py`, `ligacoes_contraventamento.py`, `ligacoes_modelo.py`, `placa_base.py`, `acoes/vento_barras.py` e `acoes/nbr8400.py`. Os caminhos abaixo são relativos a `codigo/motor/prossen_estrutural/`, salvo indicação contrária.

Os exemplos foram executados por Python via entrada padrão, sem criar testes ou modificar o motor. Dimensões das famílias do contrato são em **mm**; as de `placa_base` e dos tipos antigos de `ligacoes` são em **m**. Esforços são de cálculo, em kN e kN·m. Valores não especificados nas reproduções usam os padrões do código revisado.

Não se repetem IN01–IN08, nem se propõe desfazer suas correções. RC04 trata da perda de esforços na integração, RC06 do sinal da demanda do painel e RC09 de um furo fisicamente aberto na borda: são problemas distintos dos mínimos de força, da resistência do painel e do aviso de arruela já registrados.

Além dos documentos locais, foi conferido o texto da [NBR 8800:2008, cópia já referenciada em ITENS-NORMA](https://engcivil20142.wordpress.com/wp-content/uploads/2018/03/nbr8800_2008_1.pdf), especialmente 6.2.2.2 d), 6.3.3, 6.4 e 6.5. As contas abaixo são cálculos desta revisão. A edição examinada é a de 2008.

## Achados

### RC01 — CRÍTICO — Compressão cancela indevidamente a parcela excêntrica nos parafusos

**Local:** `aco/ligacoes_flexiveis.py:732`.

**Problema:** `_chapa_simples` calcula apenas `N/n + M·max(|y|)/Σy²`. Para N negativo, essa soma pode anular-se. O parafuso da outra extremidade recebe a soma dos módulos e não é avaliado. O mesmo caminho atende `viga_viga_recorte` com chapa simples.

**Reprodução:** `chapa_simples_alma`, dados `{perfil_viga: "W360x51", perfil_suporte: "W530x92", n: 3, s: 75, a: 75, e_v: 40, dist_topo_furo: 75, t_chapa: 8, b_solda: 5}`, esforços `{V: 100, N: -150}`.

**Conta esperada:** y = −0,075; 0; +0,075 m; Σy² = 0,01125 m². M = 100·0,075 = 7,5 kN·m. As componentes horizontais extremas são −50 ± 50 = **0 e −100 kN**. Assim, Rmáx = √[(100/3)² + 100²] = **105,409 kN**. O A325 de 19,05 mm resiste a **69,672 kN**, dando taxa **1,51293**, reprovado.

**Obtido:** R = **33,333 kN**, taxa do parafuso **0,47843**, resultado geral **`ok=True`, `taxa=1,0`**. Trocar somente N para +150 faz o código obter corretamente 105,409 kN e reprovar.

**Correção sugerida:** somar as componentes com sinal em cada parafuso e selecionar a maior resultante. Para esta linha simétrica, usar `abs(N)/n + abs(M)·max(|y|)/Σy²` na componente extrema também resolve.

### RC02 — CRÍTICO — Chapa de topo sob tração axial aprova acima da capacidade total dos parafusos

**Local:** `aco/ligacoes_rigidas.py:533–565`, especialmente `:535` e `:558`.

**Problema:** a seleção de linhas tracionadas depende apenas do sinal de M. O momento equivalente `|M| + N⁺·braço/2` continua sendo usado quando ambas as mesas estão tracionadas. As linhas inferiores ficam apenas ao corte, mesmo com tração axial pura. Falta equilíbrio de força normal e momento para esse regime.

**Reprodução:** `verificar_chapa_topo_estendida`, perfil informado `{d:400, bf:200, tw:8, tf:12}`, `tp=50`, `bp=220`, `g=100`, `pf=40`, `de=40`, `b_alma=5`, `estendida="superior"`; esforços `{N:900, V:0, M:0}`. O pilar não é informado, para isolar o lado da ligação avaliado.

**Conta esperada:** há **6 parafusos A325 Ø19,05**. Cada um tem Ft,Rd = 0,75·π·19,05²/4·825/1,35/1000 = **130,636 kN**. Mesmo desprezando alavanca, a soma máxima é **783,813 kN < 900 kN**; taxa global de tração ≥ **1,14823**. Nenhuma redistribuição pode equilibrar 900 kN com essa soma de resistências.

**Obtido:** `Mef=174,6 kN·m`, `MRd=202,746 kN·m`; somente 4 parafusos recebem T = **112,5 kN**, somando **450 kN**. Os outros 2 não são verificados à tração. Resultado **`ok=True`, `taxa=1,0`**.

**Correção sugerida:** separar os regimes com contato comprimido e com todas as mesas/linhas tracionadas; impor ΣF = N e ΣM = M. Como bloqueio inicial, recusar o regime fora do modelo e verificar também N ≤ ΣFt,Rd. Esse limite isolado é necessário, mas não substitui a distribuição equilibrada.

### RC03 — CRÍTICO — Uma fileira de chumbadores faz desaparecer o momento da base

**Local:** `aco/placa_base.py:617–622`, especialmente `:619`.

**Problema:** com `fileiras=1`, todos os chumbadores têm y=0. Quando Σy²=0, a parcela do momento é substituída por zero; com N positivo, o algoritmo aceita imediatamente “Todos tracionados”. O esquema aceita esse arranjo.

**Reprodução:** dados `{H:0.5, B:0.4, esp:0.05, diametro_mm:24, borda:0.05, solda:0.005, fileiras:1}`; seção `{tipo:"I", h:0.3, b:0.2, tw:0.008, tf:0.012}`; esforços `{N:100, V:0, M:10, N_barra:100, V_barra:0, M_barra:10}`.

**Conta esperada:** dois chumbadores em y=0 com T=50 kN dão ΣT=100 kN, mas **Σ(T·y)=0**, incapaz de equilibrar **10 kN·m** sem contato. Se houver contato, o braço até a borda é no máximo H/2=0,25 m, exigindo **C ≥ 10/0,25 = 40 kN** e **Ttotal ≥ 140 kN**, ou ≥70 kN por chumbador. O bloco real tem braço menor e exige mais.

**Obtido:** C=0, Ttotal=100 kN, Tmax=50 kN e **`ok=True`, `taxa_max=0,66642`**.

**Correção sugerida:** se Σy²≈0 e |M|>0, impedir a solução elástica sem contato; resolver o equilíbrio com concreto ou devolver uma geometria não suportada. Conferir os resíduos de força e momento em todos os regimes.

### RC04 — CRÍTICO — Integração do nó de treliça perde N2, N3 e N4

**Local:** `aco/ligacoes_modelo.py:402–405` e `:416–418`; destino em `aco/ligacoes_contraventamento.py:713–724`.

**Problema:** o adaptador genérico passa somente N da ponta corrente. `no_trelica_gusset` exige os esforços simultâneos das demais barras, mas N2–N4 ausentes viram zero e são substituídos pelo mínimo. `modelo` e `membro` não são usados para buscá-los. Verificar separadamente outras pontas não recompõe a combinação conjunta nem a interface com o banzo.

**Reprodução:** dados `{dim_a_1:100, dim_a_2:100, t_perfil_1:10, t_perfil_2:10, t_gusset:10, secao_2:"2L", n_por_linha_1:4, n_por_linha_2:4}`. Chamar `ligacoes_modelo.verificador("no_trelica_gusset")(None, None, e, dados)`, com `e={N:100, Vy:0, Vz:0, T:0, My:0, Mz:0}`. Comparar com a chamada direta do nó usando `{N:100, N2:1000}`.

**Conta esperada:** para a barra 2 com 4 parafusos, o corte solicitado é **1000/4=250 kN/parafuso**, contra **98,297 kN** em dois planos: taxa **2,54331**. A integração usa **45/4=11,25 kN**, taxa **0,11445**. A seção bruta da barra 2 também deveria receber 1000 kN, não 45.

**Obtido:** adaptador **`ok=True`, `taxa=1,0`**; chamada direta com N2 real **`ok=False`, `taxa=2,69945`**.

**Correção sugerida:** criar adaptador específico que associe as barras 1–4 às pontas do nó e leia todos os N da mesma combinação. Até existir esse mapeamento, impedir a aprovação pelo gerenciador de grupos de um nó com várias barras ativas.

### RC05 — CRÍTICO — Talas da alma não são verificadas para a parcela axial que recebem

**Local:** `aco/ligacoes_rigidas.py:831–843` e `:889–901`.

**Problema:** a emenda atribui `Nw=N·Aw/A` à alma e inclui essa força nos parafusos. As talas da alma são verificadas somente com V e M0, sem escoamento/ruptura por Nw (6.5.3), compressão (6.5.4) ou suas interações. Com N puro, suas verificações resistentes retornam solicitação zero.

**Reprodução:** `verificar_emenda_pilar`, esforços `{N:2000}`, dados:

```python
dict(perfil_pilar=dict(d=600, bf=300, tw=8, tf=12.5), contato=False,
     t_tala_mesa=25, n_por_linha_mesa=6, d_mesa_mm=25.4,
     e1_mesa=45, e_mesa_perfil=45,
     t_tala_alma=4.75, h_tala_alma=300,
     n_linhas_alma=4, n_colunas_alma=6,
     s_alma=75, sh_alma=70, d_alma_mm=19.05,
     e_emenda_alma=40, e_tala_alma=40)
```

**Conta esperada:** Aw=575·8=4600 mm² e A=2·300·12,5+4600=12100 mm². Logo **Nw=760,331 kN**. Para as duas talas A36, Ag=2·300·4,75=2850 mm²; escoamento **647,727 kN**. Descontando 4 furos de 19,05+1,5+2=22,55 mm, An=2·(300−4·22,55)·4,75=1993,1 mm²; ruptura **590,548 kN**, taxa **1,28750**, reprovada.

**Obtido:** **`ok=True`, `taxa=0,9797`**; V=M0=0 nas verificações das talas. O problema também alcança a emenda de viga e a compressão sem contato.

**Correção sugerida:** verificar Nw nas duas talas, com áreas bruta/líquida e estabilidade correspondentes, além de interação com V e M0. Verificar também a seção ligada do perfil para o carregamento combinado.

### RC06 — ALTO — Cortante negativo do painel é truncado para zero

**Local:** `aco/ligacoes_rigidas.py:433` e `:440–441`.

**Problema:** depois de subtrair o cortante do pilar, `_pilar` usa `max(Vp,0)` como solicitação. Quando o cortante do pilar supera a parcela das mesas, a demanda muda de sinal e é descartada. Isto independe da correção IN02 da resistência do painel.

**Reprodução:** `verificar_painel_alma({perfil_viga:"W360x51", perfil_pilar:"W310x38.7", V_pilar:1000}, {M:20})`.

**Conta esperada:** o braço do perfil é 0,3434 m; 20/0,3434=58,241 kN. A expressão implementada fornece Vp=58,241−1000=**−941,759 kN**. A verificação deve usar o módulo **941,759 kN**, contra VRd=**338,351 kN**, taxa **2,78338**.

**Obtido:** solicitante=0, taxa do painel=0; **`ok=True`, `taxa=0,3158`** no resultado geral.

**Correção sugerida:** preservar os sinais no equilíbrio do painel e comparar o módulo da resultante com VRd. Se a entrada pressupuser um domínio restrito, validá-lo; não zerar uma resultante invertida.

### RC07 — ALTO — Falta redução do comprimento efetivo de filetes longitudinais longos

**Local:** `aco/ligacoes.py:430–437`; `aco/ligacoes_contraventamento.py:475–477`; `aco/ligacoes_rigidas.py:738–740`.

**Problema:** os caminhos usam diretamente o comprimento total para a resistência. Falta β da 6.2.2.2 d) nos filetes longitudinais de ligações extremas axialmente solicitadas. A regra geral da alínea c) não elimina essa exceção.

**Reprodução isolada:** `verificar_ligacao("solda_filete", {b:0.005, L:1, t_base:0.01, eletrodo:"E70", aco_base:"A-572 GR.50"}, {F:650})`, interpretado como um filete longitudinal recebendo carga pela extremidade.

**Conta esperada:** L/b=200; **β=1,2−0,002·200=0,8**. Resistência da solda sem redução = 0,6·0,707·5·1000·485/1,35/1000=**761,989 kN**; com Le=800 mm, **609,591 kN**. Taxa **650/609,591=1,06629**, reprovada. O metal base não governa este exemplo.

**Obtido:** **`ok=True`, `taxa_max=0,85303`**. Nas ligações de barras e nas chapas de mesa soldadas a orientação longitudinal já é conhecida.

**Correção sugerida:** aplicar β por cordão quando a alínea d) for aplicável, com os limites 0,6 e 1,0. No componente genérico, informar o regime de aplicação; não reduzir indiscriminadamente soldas transversais ou de outro regime.

### RC08 — ALTO — Pino do garfo usa resistência de parafuso e omite flexão

**Local:** `aco/ligacoes_contraventamento.py:834–851`, especialmente `:846–849`.

**Problema:** o componente explicitamente chamado “pino” é verificado pela 6.3.3.2, com fub/γa2. A 6.4 tem critérios próprios com fy/γa1, além de flexão e esmagamento do pino. Não há entrada para espessura das orelhas/folgas, necessária para calcular a flexão completa.

**Reprodução:** `verificar_tirante({D_barra:30, extremidade:"garfo_pino", d_pino:20, tipo_pino:"ISO 4.6", t_chapa:50, largura_chapa:120, borda_pino:40}, {N:65})`.

**Conta esperada:** usando fy=235 MPa da própria tabela do motor, a 6.4.2.2 dá, nos dois planos, **2·0,75·0,6·(π·20²/4)·235/1,10/1000=60,404 kN**, taxa **1,07608**. O código usa **74,467 kN**, 23,3% a mais. Pela 6.4.2.1, MRd=**1,2·(π·20³/32)·235/1,10/10⁶=0,20135 kN·m**. Mesmo tomando as reações do garfo nas faces da chapa central, o menor momento máximo sob contato uniforme é N·t/8=**0,40625 kN·m**; orelhas e folgas aumentam esse valor.

**Obtido:** **`ok=True`, `taxa=0,875`**, sem verificação de flexão do pino.

**Correção sugerida:** implementar 6.4.1/6.4.2 para pinos, incluindo o equilíbrio com pressões distribuídas, e as verificações do olhal aplicáveis. Se a intenção for um parafuso comercial em outro modelo, distinguir esse detalhe e limitar explicitamente seu domínio; o aviso de catálogo do garfo não verifica o pino dimensionado pelo motor.

### RC09 — CRÍTICO — Furo que atravessa a borda da placa é aprovado ao corte

**Local:** `aco/placa_base.py:1026–1029` e `:940–941`.

**Problema:** lf≤0 gera somente aviso; `aco_chumbador_nbr` omite a pressão de contato quando lf não é positivo. Assim, quanto pior a geometria, desaparece a verificação que deveria reprová-la. Não é o aviso de arruela de IN06: falta material entre o furo e a borda livre.

**Reprodução:** dados `{H:0.5, B:0.4, esp:0.05, diametro_mm:16, borda:0.008, solda:0.005}`; seção de RC03; esforços `{N:-100, V:20, M:0, N_barra:-100, V_barra:20, M_barra:0}`.

**Conta esperada:** dh=16+14,3=**30,3 mm**; lf=8−30,3/2=**−7,15 mm**. O furo abre na borda: não existe ligamento resistente ao rasgamento nessa direção. Atrito=0,10·100=10 kN <20 kN, portanto o motor seleciona os chumbadores, com **10 kN por efetivo**. Para esse caminho, deve haver reprovação geométrica; capacidade do ligamento≤0, nunca uma omissão favorável.

**Obtido:** **`ok=True`, `taxa_max=0,20982`**, sem pressão de contato na lista de verificações, embora exista aviso de lf≤0.

**Correção sugerida:** rejeitar a geometria ou inserir falha resistente explícita quando o caminho de corte depende desse furo. Considerar o ligamento positivo mínimo e o detalhamento real da arruela, se houver, antes de permitir outro modelo resistente.

### RC10 — MÉDIO — Chumbador na borda do bloco produz ZeroDivisionError e HTTP 500

**Local:** `aco/placa_base.py:861–877`, especialmente `:868`; validação em `:974–998`.

**Problema:** as dimensões do bloco são aceitas sem garantir que os chumbadores estejam dentro do concreto. No fendilhamento lateral, c1=0 chega a `c2/c1`. A exceção não é convertida em erro de entrada: `projeto.py:2687` captura ValueError/KeyError/TypeError, e `web/servidor.py:54–66` mapeia a divisão por zero para HTTP 500. A requisição falha; não foi observado encerramento do processo servidor.

**Reprodução:** dados `{H:0.5, B:0.4, esp:0.05, diametro_mm:16, borda:0.05, solda:0.005, bloco_H:0.4, bloco_B:0.6}`; seção de RC03; esforços `{N:10, V:0, M:0, N_barra:10, V_barra:0, M_barra:0}`.

**Conta esperada:** yext=H/2−borda=0,20 m; meia dimensão do bloco=0,20 m. Logo **c1=0,20−0,20=0**. A geometria deve ser recusada antes das fórmulas de ancoragem, com mensagem sobre a distância à borda e resposta de entrada inválida, não uma resistência numérica.

**Obtido:** **`ZeroDivisionError: division by zero`**. Também foi reproduzido diretamente em `concreto_chumbador_aci` com um chumbador em y=0,20 m num bloco H=0,40 m.

**Correção sugerida:** validar distâncias estritamente positivas às quatro faces e a compatibilidade da ancoragem com o bloco. Tratar capacidade nula como reprovação explícita, sem divisões. Não corrigir apenas capturando a exceção e prosseguindo.

### RC11 — ALTO — Adaptador antigo transforma compressão em tração sem verificar flambagem

**Local:** `aco/ligacoes_modelo.py:373`; verificações chamadas em `aco/ligacoes.py:493–535`.

**Problema:** o tipo `ligacao_tracao`, sugerido automaticamente para gussets e nós de treliça, recebe `abs(N)`. O sinal de compressão é perdido e o resultado é apresentado como aprovação após verificar somente tração, corte dos parafusos e rasgamento. Não há comprimento livre nem verificação de compressão da chapa nesse tipo.

**Reprodução:** `ligacoes_modelo.verificador("ligacao_tracao")(None, None, e, dados)`, com `e={N:-50, Vy:0, Vz:0, T:0, My:0, Mz:0}` e `dados={b:0.1, t:0.006, d_mm:16, n_linhas:1, n_por_linha:2, e1:0.05, s:0.05}`.

**Conta esperada:** a transmissão deve preservar **N=−50 kN**, e não fornecer +50 kN a um verificador de tração. Sem os dados de estabilidade, o resultado esperado é “compressão não verificada”/não aprovado. Como ilustração da diferença física, a mesma faixa 100×6 mm com KL=500 mm tem I=100·6³/12=1800 mm⁴; Ne=π²·200000·1800/500²/1000=**14,212 kN**, já menor que 50 kN antes da redução resistente. KL=500 mm é uma hipótese ilustrativa, não um parâmetro existente na chamada.

**Obtido:** o adaptador fornece `{N:50}` e retorna **`ok=True`, `taxa_max=0,50866`**, sem aviso de compressão não verificada.

**Correção sugerida:** recusar N<0 nesse adaptador ou migrar para um tipo que preserve o sinal e disponha dos dados para 6.5.4. Não usar o verificador de elemento tracionado como verificador de compressão.

## Testes e conferências sem novo achado

Comando executado em `codigo/motor`, com `PROSSEN_PULAR_LENTOS=1` e `PYTHONDONTWRITEBYTECODE=1`:

```text
python -m unittest discover -s testes

Ran 1050 tests in 119.113s
OK (skipped=6)
```

As mensagens de rejeição de entradas emitidas durante testes negativos não foram falhas da suíte. As reproduções deste relatório foram executadas separadamente, sem adicionar arquivos de teste. A aprovação da suíte não cobre os casos novos demonstrados acima.

Nas primitivas de parafusos, chapas e filetes examinadas, os valores básicos γa1=1,10, γa2=1,35 e γw2=1,35 estão coerentes com os regimes implementados. No caminho principal da placa não foi encontrada nova aplicação do coeficiente global legado sobre N/V/M. RC08 é uma troca de modelo normativo, que também troca indevidamente a resistência de material e o γ aplicável ao pino.

Em `vento_barras.py`, a conversão q=0,613·Vk²/1000 e a reconstrução vetorial com módulo proporcional a sen²α conferiram. Exemplo: Vk=35 m/s, Cf=1,5, K=0,81, c=0,1 m, sen²α=0,5 dão q=0,750925 kN/m² e w=**0,04561869375 kN/m**, iguais à execução. Em `nbr8400.py`, 20²/1,6=250 Pa; com C=1,6, c=0,1 m e sen²α=0,5, w=**0,020 kN/m**, também reproduzido. A conversão ν/γa1 das combinações é explicitamente uma equivalência de escoamento no contrato do módulo; não foi confundida com uma segunda majoração global nas ligações. Não há novo achado confirmado nesses dois arquivos nesta revisão; isso não constitui validação integral de suas tabelas aerodinâmicas ou das hipóteses declaradas.

Não foram reabertas como achados as limitações já declaradas de flexão biaxial simultânea, fadiga, armadura de ancoragem, soldas de reforços/enrijecedores ou calibração ACI/NBR. As correções sugeridas acima não foram implementadas.

## Verificação e correção

Data: 21/10/2026. Branch `wt-rc`. Cada achado foi reproduzido com o script da revisão e recalculado à mão contra o texto da NBR 8800:2008 (6.2.2.2 d, 6.3.3, 6.4, 6.5). As regressões estão em `codigo/motor/testes/test_revisao_rc.py`. Os 11 testes falham no código anterior (9 falhas e 2 erros) e passam com as correções.

| RC | Veredito | Causa e correção | Valor à mão (teste) |
|---|---|---|---|
| RC01 | Confirmado | `_chapa_simples` somava N com sinal à parcela de V·a do mesmo parafuso. Agora usa `abs(N)/n + abs(M)·ymáx/Σy²`, o parafuso extremo do lado em que as parcelas se somam (linha simétrica). | R = 105,409 kN > 69,672 kN; reprova |
| RC02 | Confirmado | Com N/2 > \|M\|/braço, a mesa "comprimida" também fica tracionada, e as suas linhas só eram verificadas ao corte. Agora cada mesa recebe N/2 ± \|M\|/braço (ΣF = N, ΣM = M). As linhas da mesa oposta resistem a F_op = N/2 − \|M\|/braço, com a mesma análise de T-stub e alavanca por linha. | 450 kN > 2·130,636 = 261,27 kN; reprova |
| RC03 | Confirmado | Com Σu² = 0 (uma fileira), a parcela de M era zerada e o modelo "todos tracionados" era aceito. Agora esse modelo só vale com Σu² > 0 ou M = 0; senão o cálculo segue para o bloco de compressão. | T = q·Y + N = 140,82 kN; Tmáx = 70,41 kN; ΣM = 10 kN·m |
| RC04 | Confirmado | O adaptador genérico só tem o N da ponta corrente. Bloqueio: `ligacoes_modelo.verificador("no_trelica_gusset")` recusa, com ValueError, um nó com as barras 2–4 ativas. O gerenciador de grupos registra o erro e não aprova. Pendente: um adaptador que leia N1–N4 da mesma combinação. | ValueError "N2–N4" |
| RC05 | Confirmado | Nw = N·Aw/A ia aos parafusos, mas não às talas da alma. Agora as talas verificam tração (6.5.3: escoamento e ruptura com An ≤ 0,85Ag) ou compressão sem contato (6.5.4, K = 0,65, L = 2·e_emenda), mais a interação Nw + M0 (5.5.1.2). | Nw = 760,33 kN; ruptura 590,55 kN; escoamento 647,73 kN; reprova |
| RC06 | Confirmado | `max(Vp, 0)` descartava o painel invertido (V_pilar > ΣF das mesas). Agora usa \|Vp\|. | \|Vp\| = 941,76 kN > 338,35 kN |
| RC07 | Parcialmente | Confirmado nos filetes longitudinais das barras de contraventamento/treliça e da chapa de mesa soldada, onde a orientação é conhecida. Nesses casos passa a valer β = 1,2 − 0,002·l/b (0,6 ≤ β ≤ 1,0). O componente genérico `solda_filete` não é bug: o seu L é a soma dos cordões, sem orientação, e reduzir sempre seria indevido. Ganhou o campo opcional `L_longitudinal` para aplicar a alínea d). | β(1000; 5) = 0,8 → taxa 1,0663 |
| RC08 | Confirmado | O pino do garfo usava 6.3.3.2 (fub/γa2), sem flexão. Agora segue a 6.4: corte 0,6·0,75·Ag·fy/γa1 em 2 planos, flexão 1,2·W·fy/γa1 com M = F/2·(t_orelha/2 + folga + t/4) (6.4.1, contato uniforme) e esmagamento 1,5·fy/γa1. Novos campos: `t_orelha` e `folga_garfo`; com t_orelha = 0 vale o mínimo N·t/8, com aviso. Na tabela PARAFUSOS, fyb do A490 passou a 895 MPa (tab. A.3). | Fv,Rd = 60,404 kN; M = 0,40625 > MRd = 0,20135 kN·m; Pfeil 9.10.4 (p. 259): M = 1066 kN·cm, d ≥ 7,36 cm |
| RC09 | Confirmado | Com lf ≤ 0, `aco_chumbador_nbr` omitia a pressão de contato. Agora entra com resistência nula (taxa infinita, reprova) e um aviso explícito. | lf = −7,15 mm → Rd = 0; reprova |
| RC10 | Confirmado | `concreto_chumbador_aci` agora recusa, com ValueError, um chumbador sem distância positiva às faces do bloco. Isso acontece antes de qualquer fórmula ACI e não há mais ZeroDivisionError. | c1 = 0 → ValueError "bloco" |
| RC11 | Confirmado | `ligacao_tracao` recebia \|N\|. Agora N < 0 levanta ValueError ("compressão não verificada... 6.5.4"), e a tração segue igual. | N = −50 → ValueError; N = +50 aprova |

Valores de teste alterados: `test_ligacoes_contraventamento.TestTirante.test_garfo_pino` era um valor à mão, não de livro. Ele usava a resistência de parafuso 2·0,4·Ab·fub/γa2 = 139,3 kN. Passou a usar a 6.4.2.2, 2·0,6·0,75·Ag·fy/γa1 = 148,1 kN com fy = 635 MPa do A325, e a conferir também a flexão. Nenhum valor de livro mudou, e os testes de livro continuam passando.

Pendências:
- RC04: falta o adaptador de nó com N1–N4.
- RC05: falta a verificação da seção ligada do perfil sob o carregamento combinado.
- RC08: faltam as verificações próprias do olhal (garfo comercial: catálogo).
- RC11: `JanelaLigacaoProjeto.cs` (`EsforcosDoMembro`) ainda envia \|N\| aos tipos antigos. O motor recusa N < 0 só pelo gerenciador de grupos; a janela WPF precisa enviar N com sinal.
