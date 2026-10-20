# Ligações flexíveis — implementação (`aco/ligacoes_flexiveis.py`)

Data: 20/10/2026. Segue `docs/v3/LIGACOES-CONTRATO.md`. Testes: `codigo/motor/testes/test_ligacoes_flexiveis.py`.

## Tipos registrados

| Tipo | Liga | Esforços | Fonte de conferência |
|---|---|---|---|
| `cantoneira_dupla_alma` | viga–pilar (mesa/alma), viga–viga | V, N | Pfeil 9.10.2; Gerdau LCPP 30-B |
| `chapa_simples_alma` | viga–pilar (mesa/alma), viga–viga | V, N | Gerdau LCHS (coeficientes C, R20) |
| `chapa_extremidade_flexivel` | viga–pilar (mesa/alma), viga–viga | V, N | Gerdau LCHE (fórmulas; tabela não reproduzida) |
| `apoio_cantoneira` | viga–pilar (mesa/alma) | V | Pfeil 9.5 e 9.10.6 |
| `viga_viga_recorte` | viga–viga | V, N | Gerdau 3.3.1 / 3.3.2 (recortes) |

**Registro.** `ligacoes.py` ganhou `_MODULOS_CONTRATO` e `tipos_contrato()`. `tipos_ligacao()` acrescenta os tipos do contrato no formato de formulário (campos com `rotulo`, `unidade` mm, `opcoes`, `min`, `max`). `verificar_ligacao()` despacha para `TIPOS[tipo]["verificar"]`. Os outros módulos de família entram acrescentando o nome do módulo a `_MODULOS_CONTRATO` (uma linha). Quatro testes que fixavam o conjunto exato dos quatro tipos antigos passaram a exigir só que ele esteja contido.

**Entradas.** Comprimentos em mm. Os perfis vêm pelo nome do catálogo ou por um dict `{d, bf, tw, tf, r}` em mm, para perfis soldados (VS/CS) fora do catálogo. Cantoneira em texto: `"L b1 x b2 x t"`. Na dupla cantoneira b1 é a aba na viga e b2 a aba no apoio. No assento, b1 é o assento e b2 a aba vertical.

**Saída.** É a do contrato (`ok`, `taxa`, `verificacoes`, `avisos`, `geometria`) mais `valores`, com os intermediários: Fd, C, W líquido, alavanca (B, Q, α, Tmáx), a', e, recortes. As disposições construtivas usam a unidade `mm` e entram na `taxa`.

## Estados-limite por tipo (NBR 8800:2008)

**Comum a todos os tipos**
- Corte do parafuso (6.3.3.2).
- Contato e rasgamento (6.3.3.3). Por furo, com a **menor** distância livre relevante, como no formulário Gerdau.
- Tração (6.3.3.1) e interação (6.3.3.4).
- Cisalhamento de chapa e cantoneira (6.5.5): bruto e líquido (dh + 2 mm, 5.2.4.1).
- Colapso por rasgamento (6.5.6), com Cts = 1.
- Filete (6.2.5, tabela 8; mínimo pela tabela 10, máximo por 6.2.6.2.2, comprimento por 6.2.6.2.3).
- Bordas (tabela 14 e 6.3.12) e espaçamentos (6.3.9 e 6.3.10).
- Encaixe geométrico: altura livre da alma, mesa ou alma do apoio, mesa da viga suporte.

**Dupla cantoneira**
- Parafusos na alma em corte duplo, com V e N. A excentricidade é desprezada, como no Gerdau e no AISC.
- Parafusos no apoio com V/2 e momento (V/2)·g_apoio, como no Pfeil 9.10.2.
  - O campo `excentricidade_apoio` (padrão: sim) desliga esse momento. Com ele desligado, o modelo é o do Gerdau.
- Flexão da aba no apoio pela seção líquida (Pfeil).
- Cantoneiras ao cisalhamento e ao bloco, nas duas abas quando parafusadas.
- Alma da viga ao cisalhamento, com a altura reduzida pelos recortes.
- Bloco na alma recortada.
- Solda em C pelo método elástico (Pfeil 9.10.2: V e V·e1).
- Metal base da alma junto às soldas das duas cantoneiras.
- Com N > 0: efeito de alavanca nas abas do apoio (Kulak & Fisher cap. 17, ver abaixo) e interação no parafuso.

**Chapa simples**
- Grupo excêntrico pelo centro instantâneo (Crawford–Kulak, Δmáx = 0,34 pol). Reproduz os C do Gerdau: 0,88; 1,75; 2,81; 3,90; 4,98.
- Com N ≠ 0, o grupo é verificado pelo método elástico (V, N e V·a). Há aviso.
- Chapa ao cisalhamento, ao bloco e à flexão V·a (escoamento na seção bruta e ruptura na seção líquida).
- Tração da chapa com N.
- Solda para V e N, com a rótula na linha de parafusos. Exige perna ≥ 5t/8 (AISC).
- Ductilidade: t da chapa ou tw ≤ db/2 + 2 mm.

**Chapa de extremidade**
- Parafusos ao corte por V/2n.
- Chapa ao cisalhamento em 2 seções e ao bloco em 2 blocos.
- Alma da viga ao cisalhamento junto à solda.
- Solda para V e N.
- Com N > 0: efeito de alavanca da chapa (b = (g − tw)/2, a = (largura − g)/2).

**Apoio em cantoneira e consolo**
- Alma da viga: escoamento local (5.7.3) e enrugamento (5.7.4) com a força na extremidade. O a' necessário é obtido por bisseção.
- Cantoneira:
  - flexão na seção A-A, a t + 10 mm do dorso: M = V·(folga + a'/2 − t − 10 mm), MRd = ℓt²/4·fy/γa1 (Pfeil eq. 9.4);
  - cisalhamento da seção A-A;
  - parafusos sem excentricidade (prática AISC), ou 2 filetes verticais (elástico, V e V·e).
- Consolo enrijecido (Pfeil 9.10.6):
  - t0 ≥ tw e b/t0 ≤ 16;
  - cisalhamento do enrijecedor na face soldada;
  - solda (cordão horizontal + 2 verticais), com tensão no topo e e = b − a'/2 (eq. 9.5).

**Viga recortada** (dupla cantoneira e chapa simples)
- Os recortes viga–viga seguem o Gerdau 3.3: altura tf + 10 mm e comprimento bf/2 − tw/2 + 10 mm − folga. Recorte inferior quando a viga invade a mesa inferior da suporte.
- Início de escoamento na seção T ou retangular, com braço folga + c (Gerdau 3.4.2).
- Flambagem local da alma (AISC 13ª ed. p. 9-7, **sem item na NBR**). Fora do campo de validade (c ≤ 2d, dc ≤ d/2; dois recortes com dc ≤ 0,2d), o motor emite aviso explícito em vez de número.

## Exemplos de livro (esperado × obtido)

| Exemplo | Grandeza | Livro | Motor |
|---|---|---|---|
| Pfeil 9.10.2 (p. 256–258) | Fd no parafuso do apoio | 58,0 kN | 57,96 |
| | Fv,Rd (A325, rosca fora, 5/8") | 60,5 | 60,48 |
| | Contato 2,4dtfu | 91,0 | 90,31 |
| | e1 | 63 mm | 63,2 |
| | Solda (resultante) | 5,75 kN/cm | 5,78 |
| | Escoamento por cantoneira | 218 | 218,2 |
| | Bloco por cantoneira | 186,7 | 186,2 |
| | W líquido | 45,4 cm³ | 45,44 |
| Gerdau LCPP 30-B (p. 33), sem recorte | W360×32,9 | 265 | 265,1 (contato na alma) |
| | W360×51 | 329 | 329,2 (contato na alma) |
| | W360×79 | 339 | 342,3 (bloco nas cantoneiras, 0,98 %) |
| Gerdau 3.3.1 / 3.3.2 (p. 22) | Recortes E1 | 99 × 26 mm | 99,4 × 25,6 |
| | Recortes E2 | 61 × 21 mm | 60,75 × 20,7 |
| Gerdau LCHS (R20) | C, 2 a 6 parafusos | 0,88 … 4,98 | 0,878 … 4,984 |
| | Corte de 3 × 3/4" | 121,9 (tabela 120) | 122,2 |
| Pfeil 9.10.6 (p. 262–263) | a' pelo escoamento local | 60 mm | 59,7 |
| | ȳ | 7,59 cm | 7,59 |
| | τv | 2,47 kN/cm | 2,47 |
| | Cisalhamento do enrijecedor | 341 | 340,9 |

## Divergências com os livros (não copiadas)

- **Pfeil 9.10.2, ruptura da cantoneira:** o livro desconta o d nominal e obtém 216. O motor desconta dh + 2 mm (NBR 5.2.4.1, como o Gerdau) e obtém 201,8.
- **Pfeil 9.10.2, contato na aba do apoio:** o livro só olha a direção vertical. O motor usa também a borda da ponta da aba (a componente horizontal do momento é a maior) e obtém 66,3 kN > 58.
- **Pfeil 9.10.2, metal base da alma:** o motor verifica o metal base da alma (VS 450, tw = 6,3) junto às soldas das duas cantoneiras e reprova: 2q = 1156 > 859 kN/m. O livro não faz essa verificação. O AISC (tabela 10-2) pediria tw ≥ 8,5 mm para o filete de 5 mm em E60.
- **Pfeil 9.10.6, momento de inércia da solda:** o livro usa 2·20·1,2² no lugar de 2·20·(10 − 7,59)². O livro obtém I = 2122t e 4,08/t; o correto é 2297t e 3,89/t.
- **Pfeil 9.10.6, enrugamento:** o livro diz a' = 40 mm. Pela 5.7.4 (extremidade, 0,33) são precisos cerca de 96 mm. Com o assento de 100 mm o motor reprova o enrugamento por 2,5 %.
- **Pfeil 9.10.6, escoamento local sem γa1:** o "1,10" da NBR 5.7.3 cancela o γa1. A nota do DIMENSIONAMENTO-PRATICO §18.1 não é erro do livro.
- **Gerdau, linhas não reproduzidas:** as linhas da CBCA.md §3.3 (LCPP W150, LCHE, LCHS 5/8") e os Vd com recorte (266, 301) não viraram teste. O bloco da alma recortada dá 263 contra 266 da tabela (1,2 %) e não é usado como teste.

## Limitações e pendências

- **Apoio:** a alma ou mesa do pilar e a viga suporte são verificadas só ao contato dos parafusos e ao metal base da solda. Faltam flexão local da mesa, punção da alma e cortante da viga suporte. O motor emite aviso.
- **Plastificação com N:** a interação de Drucker do Gerdau (caso 2, com 50 % de Vd) não foi implementada. Com N > 0 vale o efeito de alavanca (Kulak), com M = p·t²·fy/(4γa1).
  - A NBR 8800:2008 6.3.5 não traz o procedimento; o da 2024 não foi confirmado.
- **Solda da LCSP:** a solda pelo centro instantâneo (AISC 8-8) não foi feita. O motor usa o método elástico, que é conservador.
- **Chapa simples estendida** (a > 89 mm): só aviso. Faltam interação V+M e flambagem da chapa.
- **Consolo enrijecido parafusado:** não implementado; é verificado como soldado, com aviso.
- **Cantoneira superior do assento:** só geometria.
- **FLT da viga recortada:** só aviso.
- **Geometria:**
  - `chapas` trazem `v`, além de `u`, e a normal é u × v por componentes;
  - `cantoneiras` trazem `w` (direção da segunda aba), `abas` e `espessura`;
  - `parafusos` trazem `pega`. O `comprimento` é a pega + 1,2db + 6 mm, arredondado a 5 mm (aproximação);
  - origem no nó, que é o eixo do apoio; z = 0 no eixo da viga.
