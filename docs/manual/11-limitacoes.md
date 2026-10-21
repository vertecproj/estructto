# 11. Limitações e responsabilidade técnica

**O Estructto é uma ferramenta de apoio ao projeto.** O engenheiro responsável deve revisar hipóteses, modelo, ações, combinações e resultados, realizar conferências independentes e providenciar a ART pertinente. O software e a IA não assumem a responsabilidade técnica.

Este manual descreve a implementação da v3. A referência por item e edição está nos resultados e na ferramenta MCP `normas_implementadas`. Consulte os textos oficiais e confirme sua aplicabilidade ao projeto; a presença do nome de uma norma na interface não indica cobertura integral nem atualização automática de edição.

## Normas declaradas na implementação

| Referência | Escopo implementado / cuidado |
|---|---|
| NBR 8681:2003 | conjuntos ELU e ELS; confira tipos de ação e coeficientes |
| NBR 8800:2008 | conjuntos de verificações de barras de aço e ligações; há itens fora do domínio |
| NBR 14762:2010 | perfis formados a frio pelo método de largura efetiva |
| NBR 6118:2023, com alguns itens de 2014 | verificações de concreto limitadas aos domínios declarados |
| NBR 6123:2023, com coeficientes/tabelas de 1988 | vento estático de edificação e por barra |
| NBR 8400:1984 | vento e combinações I/II/III para equipamentos; conversão ao motor de estados limites |
| NBR 15421:2006 | forças sísmicas horizontais equivalentes em estruturas regulares |
| NBR 6120:2019 | usos e pesos listados; não cobre toda ação necessária |
| ACI 318-19 cap. 17 | modos de concreto de chumbadores previstos no método de placa de base |

## Modelo, edição e análise

- O modelo é de barras: Laje/carga em área não cria elemento de casca nem distribui em duas direções. A distribuição não reconhece aberturas ou ausência de laje.
- Não há barra só de tração. Tirantes e diagonais de contraventamento também trabalham à compressão.
- O cardinal desloca representação e exportação do perfil; não cria automaticamente excentricidade na análise.
- A deformada visual usa segmentos entre nós. A envoltória exibe um extremo de maior módulo por estação, sem duas curvas simultâneas. Use combinação específica para simultaneidade e equilíbrio.
- Rotacionar gira em torno do eixo vertical; Espelhar por linha usa plano vertical. Aparar/Estender considera os eixos das barras, não suas faces físicas.
- Um motor da interface mantém um modelo ativo; ao alternar abas, o histórico de desfazer pode reiniciar. Painéis de edição sem seleção podem atuar no modelo inteiro.
- Existem molas e recalques no motor; confira unidades, direções, caso REC e coeficientes. O coeficiente γε de recalque da tabela 3 da NBR 8681 consta entre os pontos a conferir no texto oficial.
- Indicadores de estabilidade não substituem análise completa de segunda ordem. Avalie o domínio dos critérios, incluindo o de γz para edifícios com quatro pavimentos ou mais.
- A suíte de fluxo ponta a ponta está documentada como parcial na entrega da v3; os testes existentes não significam validação integral de todos os fluxos.

## Ações

- Vento: sem avaliação completa de efeitos dinâmicos, vizinhança, alta turbulência ou torção por excentricidade. Tabelas de paredes/telhados têm limites geométricos (h/b ≤ 3/2, θ ≤ 60°, a ≥ b).
- Vento por barra não acrescenta proteção de reticulados, equipamentos ou fechamentos. Revise coeficientes, área exposta e cargas complementares.
- Na NBR 8400, a conversão ν/γa1 é exata para escoamento e aproximada para flambagem; não cobre automaticamente fadiga, dinâmica e todas as situações de operação. Grupo e ψ precisam de justificativa.
- Sismo: sem análise modal espectral, classe F, amplificação torcional Ax ou cobertura geral de irregularidades.
- Combinações não cobrem automaticamente todas as fases especiais/de construção. Permanentes favoráveis e desfavoráveis não são separados dentro de um mesmo carregamento.
- Cargas de uso: redução, divisórias, cargas concentradas e guarda-corpos podem exigir lançamento e conferência próprios.

## Barras

- Aço: há seções e modos fora do escopo, como certas almas esbeltas, cantoneiras em flexão e seções monossimétricas. Confira os itens não verificados. An, Ct e travamentos dependem dos dados fornecidos.
- A distorcional de Ue usa fórmula simplificada com casos conhecidos fora do domínio. Reprovações ou aprovações exigem conferência específica; não contorne um aviso apenas trocando parâmetros.
- Perfis formados a frio: sem método da seção efetiva, com limitações de FLT, cisalhamento e enrijecedores descritas pelo motor.
- Concreto: há limitações em detalhamento, fluência, pilares com λ > 90, pilares-parede, armaduras e flechas. A geração não fornece detalhamento executivo completo.
- Dimensionar usa busca por família e estimativa de massa; não garante ótimo global. Verifique novamente o conjunto após mudar rigidez.

## Ligações e placa de base

- A cobertura varia por tipo. Confira avisos de efeito alavanca, deslizamento, furos, apoio, recortes e chapas. Os itens NBR 8800 5.7.x, mínimo de 45 kN de 6.1 e folgas de furos permanecem pontos documentados para conferência oficial.
- Grupos precisam de revisão de equivalência construtiva. A pior ligação depende da taxa do projeto aplicado e de todas as combinações; a indicação sem projeto é uma estimativa de demanda.
- Ações diretamente no nó do apoio não entram como esforço da ponta da barra. A rotina genérica de ligação de tração não verifica flambagem de chapa em compressão.
- Placa NBR: verifica pressão e bloco de compressão no concreto. My e Mz são verificados separadamente, adotando o pior, sem interação biaxial simultânea.
- Não há cobertura completa de enrijecedores, efeito alavanca, armadura de ancoragem, cone ACI 349 e solda da barra de cisalhamento. Há limites na interação dos modos de aço do ACI e na verificação sísmica de ancoragem; leia os avisos específicos.
- A espessura de chapa é verificada pelo CBCA; Pfeil é conferência. Catálogos de fixação e coeficientes do fabricante exigem validação para substrato, instalação, bordas, hef e condição de carregamento reais.
- O método legado pode subestimar tração de chumbadores em cerca de 3,3 vezes no caso documentado. Revise bases calculadas por ele, inclusive a do guincho; sua comparação não é critério de aprovação da v3.

## Visualização, exportação e importação

- Snap tem limitações em perspectiva e pode atrair arestas ocultas. Interseção aparente não comprova nó comum. Os modos de snap escolhidos voltam ao padrão na próxima abertura.
- Medir não cria cotas permanentes. Peso supõe aço; áreas de seção usam contornos sem raios de concordância.
- Realista pode simplificar perfis e ocultar detalhes à distância; setas/diagramas passam à frente das peças. Uma ligação visível pode usar dados sem aprovação.
- DWG/DXF usa malha, sem 3DSOLID. STEP é facetado. As exportações não incluem ligações, recortes do Realista, cargas ou resultados; confirme compatibilidade no programa receptor.
- Importação e comparação CYPE dependem do conteúdo gravado e das hipóteses. Diferenças de norma, vínculos e parâmetros exigem investigação.
- O relatório principal ainda não incorpora automaticamente o memorial das ligações em geral; gere e entregue os memoriais separados. As placas têm anexo próprio.

## Responsabilidade na entrega

Registre no memorial hipóteses, edições normativas, verificações não cobertas e complementações efetuadas. Confira ações, equilíbrio, esforços, flechas, estabilidade, barras e ligações por meios independentes adequados. O engenheiro responsável aprova resultados e detalhamento e emite a ART pertinente. Resultados obtidos via MCP/IA seguem a mesma exigência de revisão.
