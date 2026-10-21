# Vento — ABNT NBR 6123:2023

Botão **Vento NBR 6123** (aba Cargas, grupo Casos de carga; comando `VENTO` no Ctrl+K). Pedido do Bruno em 20/10/2026.
Norma usada: `referencias/NBR-6123-2023.pdf` (2ª edição, 20/12/2023; fora do git por direito autoral).

## O que o programa faz

Vento **horizontal, no plano do pórtico**, nos dois sentidos. Cria (ou atualiza) os casos do tipo V
`Vento → (0°)` e `Vento ← (180°)` com uma carga distribuída perpendicular em cada barra, e (opcional, ligado por padrão)
regera as combinações NBR 8681/8800. O caso de vento é **regerado inteiro** a cada Aplicar (cargas de barra do caso são
substituídas; cargas de nó lançadas à mão no caso são mantidas). Dados guardados no modelo (`Modelo.Vento`), usados também no memorial.

## Contas (motor: `VentoNbr` em `codigo/portico/src/Nucleo.cs`)

| Grandeza | Fórmula / origem na norma |
|---|---|
| V0 | Figura 1 (isopletas) — digitado pelo usuário |
| S1 | 5.2: 1,00 plano; 0,90 vale protegido; talude/morro: valor calculado digitado (0,90 a 2,50) |
| S2 | 5.3.3: S2 = bm · Fr · (z/10)^p, Tabela 1 (bm, p, zg) e Tabela 2 (Fr). z < 5 m usa 5 m (Tabela 3 começa em "≤ 5"); categoria V constante até 10 m; acima de zg, zg |
| Classe | 5.3.2: A ≤ 20 m < B ≤ 50 m < C, pela maior dimensão (altura acima do terreno ou largura do pórtico) — ou escolhida |
| S3 | Tabela 4, grupos 1 a 5 (1,11 / 1,06 / 1,00 / 0,95 / 0,83) |
| Vk, q | 4.2: Vk = V0·S1·S2·S3; q = 0,613·Vk² (N/m²) |
| Força | 8.1.1 / 8.1.2: w = Cf · q · K · c (kN/m), × sen²α na barra inclinada |
| K | Tabela 28, interpolação linear em l/c; l/c dobrado se a barra toca um nó com apoio (8.1.3); l/c > 100 → K = 1,0 |
| Circular | Tabela 27 por Re = 70 000·Vk·d. Acima do crítico, confere também o subcrítico (Ca = 1,2) com a velocidade logo abaixo da crítica (Re = 4,2·10⁵) e fica com a maior força (8.1.3, último parágrafo) |

## Coeficiente Cf por forma (Tabela 26, coeficientes referidos à dimensão c — Nota 1)

- **Perfil I (W/HP)**, eixo forte no plano (vento normal às mesas, 90°): Cy interpolado em bf/d entre o I estreito
  (bf = 0,48d → 0,9) e o H (bf = d → 1,9); c = d. Perfil deitado (vento paralelo às mesas, 0°): 2,05 → 1,6; c = d.
- **Perfil U**: eixo forte no plano (vento normal às abas, 90°) Cy = 0,6; deitado (vento na alma, 0°) Cx = 2,05; c = altura.
- **Cantoneira**: 2,1 (maior |Cx| entre as direções da cantoneira de abas iguais); c = aba.
- **Tubo/barra retangular**: por profundidade/largura (no sentido do vento / vista pelo vento): 0,5 → 2,1; 1 → 2,0;
  2 → 1,5 (retângulos da Tabela 26), interpolado e limitado; c = largura vista pelo vento. Cantos arredondados de tubo
  tratados como vivos (lado seguro).
- **Seção genérica** (A e I digitados): Cf = 2,0 com c = altura informada.

## Hipóteses do programa (não estão escritas assim na norma)

1. S2 constante na barra, calculado no ponto mais alto dela (lado seguro para pilares).
2. Barra inclinada: só a componente normal, F·sen²α (mesmo critério da norma para fios e cabos, 8.2). Barra horizontal: sem vento no plano.
3. Interpolações da Tabela 26 entre formas (I estreito ↔ H; retângulos) — a norma só dá as formas tabeladas.
4. Terreno = menor cota entre os nós com apoio.
5. Casos de vento das duas direções são alternativos: nunca entram juntos numa combinação (gerador `Analise.GerarNbr`:
   com sobrecarga principal, uma combinação para cada direção de vento secundário).

## Não incluído (avisado na janela e no memorial)

Vento sobre equipamentos, telhas e fechamentos presos à estrutura (lançar como carga no caso de vento), vento fora do plano,
edificações fechadas (coeficientes de pressão externa/interna, seção 6), reticulados (8.3–8.5), efeitos de vizinhança e
efeitos dinâmicos (seções 9 a 11).

## Testes

`codigo/portico/src/Testes.cs`, bloco "vento NBR 6123:2023": S2 contra a Tabela 3 (6 pontos), q, K (Tabela 28), Ca (Tabela 27),
classe, pilar em balanço de tubo (S2, Ca, K, w, reação e momento na base), tubo de 203 mm com o subcrítico governando,
barra horizontal sem força e barra a 45° com sen²α = 0,5.
