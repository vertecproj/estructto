# 11. Limitações e responsabilidade

> **AVISO.** O Estructto é ferramenta de apoio. Todo resultado exige verificação por engenheiro responsável, com ART.
> O software não substitui as normas, o julgamento profissional nem a conferência independente.

Este manual cita itens de norma por número, sem reproduzir o texto da ABNT. A lista oficial, por item, vem do código:
ferramenta MCP `normas_implementadas` (`projeto.normas_implementadas()`). Esta página resume o que ela e a documentação declaram.

## Normas e edições usadas

| Norma | Edição no código | Cobertura |
|---|---|---|
| NBR 8681 | 2003 | Combinações ELU normais e excepcionais (sismo), ELS quase permanente, frequente e rara; tabela 6 (ψ) |
| NBR 8800 | 2008 | Barras de aço (tração, compressão, flexão FLT/FLM/FLA, cortante, interação, torção), ligações parafusadas e soldadas, estabilidade (4.9.4, anexo D), deslocamentos (anexo C), ações 4.7.6.1 |
| NBR 14762 | 2010 | Perfis formados a frio: largura efetiva, distorcional, compressão, flexão, cortante |
| NBR 6118 | 2023 (alguns itens 2014) | Concreto armado: flexão, cisalhamento, torção, fissuração, flechas, pilares |
| NBR 6123 | 2023 (Ca e tabelas de coeficientes: 1988) | Vento estático equivalente por pavimento |
| NBR 15421 | 2006 | Sismo: forças horizontais equivalentes |
| NBR 6120 | 2019 | Cargas de uso e pesos de materiais |

Edições mais novas (NBR 8800:2024, NBR 8681:2025, NBR 15421:2023) não foram confirmadas em fonte pública; o código mantém a edição anterior
e diz isso em cada item.

## O que não está coberto, por assunto

### Análise
- Arquivos 2D antigos abrem convertidos com o que sai do plano travado (pórtico plano no espaço).
- Vista 3D: deformada em cordas, sem envoltória nos diagramas; o ponto cardinal ainda não desloca o perfil desenhado.
- Equipamento giratório: só posições 0° e 180°; ψ declarado pelo usuário.
- Motor Python ainda não calcula molas (apoios elásticos) nem recalques; carga giratória; carga linear em barra com trecho rígido.
- Placa de base: só flexão no eixo forte do pilar; Dimensionar escolhe por palpite de massa, não pelo ótimo global.
- Um modelo por servidor: pilha de desfazer vale só para o documento ativo.

### Ações
- NBR 8681: sem ações indiretas (tab. 3), sem combinações especiais ou de construção; G favorável e desfavorável não separados dentro do mesmo carregamento.
- NBR 8800 tab. 1 e 2: ações truncadas e cargas móveis/pontes rolantes não tabeladas.
- NBR 6123: sem platibanda, torção de vento, efeitos de vizinhança, vento dinâmico (só aviso), alta turbulência; tabelas de paredes e telhados só para h/b ≤ 3/2, θ ≤ 60°, a ≥ b.
- NBR 15421: só estruturas regulares; sem análise modal espectral; classe F de terreno não coberta; sem fator de amplificação torcional Ax; sem alvenaria estrutural.
- NBR 6120: só os usos listados; sem redução de carga, cargas concentradas, divisórias e forças em guarda-corpos.

### Aço (NBR 8800)
- Verificação do motor Interno é simplificada: sem FLT, sem flambagem local calculada; parede esbelta só avisada.
- Motor Python: alma esbelta (anexo H), cantoneira simples (flexão), T e I monossimétrico → "não verificado", que reprova.
- An e Ct informados pelo usuário em tração; esbeltez de tração (5.2.8) usa K·L no motor (mais conservador que a norma); plano instável com k = 1.
- Tubos: espessura de cálculo 0,93t só quando informada; tubo circular com D/t > 0,45E/fy fora do escopo.
- Terças com sucção, travessas e vigas de rolamento: deslocamentos não cobertos.
- Barras compostas: presilhas maiores que chapa espaçadora e ligação genérica não verificadas.

### Perfis formados a frio (NBR 14762)
- Método da seção efetiva (MSE) não implementado; um só método (largura efetiva) para compressão e flexão.
- FLT em y não calculada; Vx não verificada; alma sem enrijecedores transversais; limite de esbeltez 300 em tração não verificado.
- Torção: critério do CYPE (a norma não traz verificação).

### Concreto (NBR 6118)
- Seção T só com mesa comprimida; armadura simétrica nas faces; 2 camadas de armadura na fissuração (só a inferior).
- Detalhamento não calculado: decalagem al, bitola e espaçamento efetivos do estribo, emendas defasadas.
- Torção tratada como de equilíbrio; banzo comprimido não verificado.
- Pilar: λ > 90 fora do escopo; fluência (15.8.4) não considerada; pilar-parede fora do escopo.
- Estabilidade global válida para 4 pavimentos ou mais.
- Flechas: Ieq na seção de momento máximo; subconjunto da tabela 13.3.
- Lajes: apenas bordos apoiados no item 19.3.3.2.

### Ligações
- Parafusos: efeito de alavanca (6.3.5) e deslizamento por atrito (6.3.4.3) não implementados; furos alargados e alongados não tratados.
- Solda: aumento de resistência com o ângulo θ não usado (a favor da segurança).
- Chapa simples: sem interação V+M, sem ruptura por flexão na seção líquida, sem rasgamento, apoio não verificado.
- Placa de base: no plano do apoio; **não verifica pressão no concreto, punção nem distância do chumbador à borda do concreto**.
- Chumbadores: valores são cargas últimas médias de ensaio; o coeficiente de segurança é do projetista.

### Importação CYPE
- Presilhas e ligações genéricas sem item na norma → não verificado.
- Comparação depende de hipóteses; diferenças de critério são esperadas.

## Diferenças entre os motores

Interno e Python podem dar taxas diferentes para a mesma barra por critérios distintos (tensão admissível × ELU, flecha dentro ou
fora do aproveitamento). Ver [página 1](01-primeiros-passos.md). Use o motor Python para verificação por norma.

## Responsabilidade

1. O engenheiro responsável revisa modelo, ações, combinações, hipóteses e resultados.
2. A ART cobre o projeto; o Estructto não assume essa responsabilidade.
3. Confira à mão ao menos uma conta de cada tipo antes de entregar.
4. Registre, no memorial, o que o software não verifica (seção 9.5 do memorial completo).
5. Resultado de IA (MCP) segue a mesma regra: é rascunho até a verificação.
