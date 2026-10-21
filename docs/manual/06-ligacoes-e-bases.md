# 6. Ligações e placas de base

## Biblioteca e projetista

Abra **Ligações e bases › Ligações › Projetar ligação** ou procure **Projetar ligação** em Ctrl+K. A biblioteca permite buscar e filtrar por família: Flexíveis, Rígidas, Emendas, Contraventamento e treliça, Bases e Componentes. Há 19 tipos das famílias de ligações, além da placa de base e componentes de cálculo. Os cartões mostram descrição, desenho e indicação de pré-dimensionamento quando disponível.

Exemplos incluem cantoneiras, chapa simples de alma, chapas de topo, emendas, gussets e ligações de terças. Escolha um tipo compatível com os membros e com a rigidez assumida na análise.

O projetista tem três passos:

1. **Barras e esforços:** informe perfis e apoio e os esforços de cálculo, em kN e kN·m. Num grupo, confira o membro e a combinação carregados. Fora do grupo, os esforços são de responsabilidade do usuário.
2. **Parâmetros:** ajuste chapas, parafusos, furos, soldas, materiais e recortes. **Sugerir** pré-dimensiona os tipos que oferecem essa função. **Otimizar** busca uma solução que passe e tenta reduzir material.
3. **Verificações:** confira veredito, taxa, critério governante, Sd/Rd, item da norma e avisos. Use os filtros por componente para localizar falhas.

O desenho se atualiza com os campos e oferece planta, elevação, lateral e vista 3D. Componentes reprovados aparecem em vermelho. Sugerir e Otimizar são auxiliares; a otimização é uma busca local e não garante mínimo global. Confira montagem, acesso de solda/parafusos e verificações não cobertas pelo tipo escolhido.

## Grupos: dimensionar pela pior ligação

Abra **Ligações e bases › Grupos › Gerenciar ligações** e **Identificar/Re-identificar**. O programa reúne pontas de barras semelhantes por tipo de nó, perfis da barra e apoio, material, ângulo e condição rígida/rotulada.

1. Revise membros e assinatura de cada grupo; selecione-o para localizar os elementos na vista.
2. Renomeie, separe, mova ou una membros conforme o detalhe construtivo. Trave grupos que devem preservar a composição. Grupos manuais conservam membros existentes ao re-identificar.
3. Use **Dimensionar pela pior** ou **Projetar no projetista…**. O programa calcula antes quando necessário.
4. Escolha o tipo, ajuste o projeto e clique em **Aplicar ao grupo**. O mesmo detalhe é verificado para todos os membros em todas as combinações ELU abrangidas pelo cálculo.
5. Revise a aba **Grupo**, taxas, erros e combinação de cada membro. A ligação governante é a de maior taxa; ela pode mudar ao alterar o detalhe.

Antes de existir projeto, a indicação de pior ligação é uma estimativa pela demanda. Só a verificação do projeto permite identificar a pior taxa. Erros em membros precisam ser resolvidos mesmo que outros passem. Após alterar geometria, perfis ou ações, recalcule e re-identifique/reverifique os grupos.

O agrupamento é geométrico e deve ser revisto pelo projetista: uma terça pode ser classificada pelo pilar que chega ao nó, embora seu apoio construtivo seja a viga. Esforços vêm da ponta da barra; ações diretamente no nó de apoio não são automaticamente incorporadas como esforço da barra.

## Placa de base pelo método NBR

Selecione o nó do pé do pilar e abra **Placa de base** (`PLACA`), ou abra a placa do grupo de bases. A placa usa o motor Python, com bloco de compressão e pressão no concreto conforme o método NBR, dimensionamento da chapa pelo CBCA e conferência Pfeil/DG1. As verificações de concreto dos chumbadores pré-concretados incluem modos do ACI 318 cap. 17.

Na janela de três passos, revise:

- **Fixação:** família, diâmetro, classe e arranjo, profundidade efetiva hef, condição de fissuração e dados do bloco de concreto. Para catálogo, confirme origem dos coeficientes e recomendação do fabricante.
- **Chapa:** H, B, espessura, bordas, sobras, fck, dimensões/altura do bloco, graute, arruela soldada e barra de cisalhamento quando usada.
- **Solda:** perna e eletrodo, solicitações e limites apresentados.

**Dimensionar tudo**, **Sugerir**, **Trocar** e a régua de diâmetros usam o motor. Em Trocar, navegue por família, diâmetro e hef; o menor que passa depende dos dados e casos verificados. **Aplicar** grava o projeto no grupo; desmarcar a aplicação ao grupo separa o nó num grupo próprio. Salve o documento após aplicar.

A janela verifica todos os membros solicitados e combinações ELU. **Fixação, chapa e solda podem ter combinações críticas diferentes**; leia verificações e esforços de cada passo. O desenho mostra bloco comprimido e chumbadores tracionados. A espessura verificada é a do CBCA; o valor Pfeil é uma conferência, ainda que seja maior.

**Flexão nos dois eixos:** o motor verifica My e Mz separadamente, girando a mesma placa física, e adota o pior resultado. Não há interação simultânea dos dois momentos. Bases com flexão biaxial relevante exigem conferência complementar.

O corte usa o mecanismo aplicável (atrito, barra de cisalhamento ou chumbadores), sem somar resistências desses mecanismos. Furos alargados podem limitar o número efetivo de chumbadores no corte. Confira as hipóteses mostradas.

**Comparar com método legado** é apenas referência histórica. O método antigo pode subestimar tração nos chumbadores em cerca de 3,3 vezes no caso documentado; revise bases dimensionadas por ele, inclusive as do projeto do guincho. O coeficiente global dessa comparação não substitui a verificação NBR com esforços ELU já majorados.

## Memorial e representação

**Memorial** no projetista gera uma página HTML autônoma para imprimir/salvar em PDF; as ligações em geral ainda não são incorporadas automaticamente ao relatório principal. Placas de base têm anexo por grupo no relatório, com verificação do governante.

O **Realista** mostra ligações com projeto aplicado aos grupos. A geometria visível não comprova aprovação. Veja [Vista e exportação](08-vista.md) e as [limitações](11-limitacoes.md), incluindo armadura de ancoragem, efeito alavanca e domínios de cada ligação.
