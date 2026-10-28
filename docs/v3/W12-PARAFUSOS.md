# W12: catálogo de parafusos (A307 e A325 em polegada) e vista 3D do projetista

Pedido do usuário (28/10/2026): "o sistema de escolher parafusos não tá muito bom, use esses catálogos, bote os A307 e A325 para
imperial" e "tá vibrando a vista 3D da ferramenta de ligações".

## Catálogo (`codigo/motor/prossen_estrutural/aco/parafusos.py`)

As fontes ficam em `9. CONSULTOR DE ENGENHARIA/referencias/fixadores/md`:

| ID | Conteúdo |
|---|---|
| IDX-P007 | Indufix, linha pesada, A325/A490 (ASME B18.2.6, rosca UNC): ½" a 1½"; chave; altura da cabeça; comprimento de rosca; comprimentos de linha de 1" (½"…¾"), 1¼" (⅞", 1"), 1½" (1⅛", 1¼") e 1¾" (1⅜", 1½") até 6", a cada ¼". |
| IDX-P008 | Indufix A307 B (ASME B18.2.1): ½" a 2"; comprimentos de 1¼" (½") até 3½" (2"), a cada ¼", até 6". |
| IDX-P011, IDX-P012 | Altura máxima da porca pesada A194 2H / A563 (½" 12,80 mm … 2" 51,13 mm). |
| IDX-P014 | Arruela F436: espessura máxima de 4,49 mm. |
| ALX-P012…P020 | Allenfix: confirma as normas dimensionais (A325 RP/RI e A490 por ASME B18.2.6; A307 Gr. A/B por ASME B18.2.1). As tabelas desse catálogo não foram transcritas. |

- **Especificação ↔ diâmetro**:
  - A307, A325 e A490 são em polegada: ½", ⅝", ¾", ⅞", 1", 1⅛" e 1¼" no seletor; o catálogo vai até 1½" (2" no A307).
  - As classes ISO (4.6, 8.8, 10.9) são métricas: M12, M16, M20, M22, M24, M27, M30.
  - Um diâmetro métrico com A325 gera o aviso "A325 é especificação em polegada…". A NBR 8800 tab. A.3 aceita A325 em mm (A325M), então não é erro.
- **Comprimento comercial**:
  - L ≥ pega + arruela F436 (4,49 mm) + porca pesada + 2 fios (2 × passo UNC), arredondado para o próximo comprimento de linha (¼").
  - Abaixo do menor comprimento de linha, usa o menor. Acima de 6", arredonda a cada ½" e marca "sob encomenda".
  - Métrico: pega + arruela ISO 7089 + porca ISO 4032 + 2 passos, a cada 5 mm até 80 mm e a cada 10 mm acima. Não houve catálogo de fabricante para métricos.
  - Exemplo: ⅞" com pega de 25 mm → 57,6 mm = 2,27" → **⅞" × 2½" A325**.
- **Todas as geometrias** (rígidas, flexíveis, contraventamento, tubos, tipos antigos) usam o mesmo `comprimento` e mandam a `pega`. O 3D põe a porca na pega; sem ela, um parafuso mais comprido empurraria a porca para fora da chapa.
- **Resultado**:
  - `lista_parafusos` agrupa por diâmetro e comprimento: `{descricao, quantidade, d_mm, comprimento_mm, sob_encomenda}`. Ausente quando não há parafuso; pinos ficam fora.
  - O painel de verificações mostra a seção "Parafusos" com a lista de compra.
- **Otimizador**:
  - `_da_familia` restringe os diâmetros à família da especificação: A325 nunca pula de ⅝" para M20.
  - `_D_MM` ganhou ⅝" (15,875), M12 e M22.
  - A faixa de `ligacoes_limites` usa a mesma regra.
- **Padrões de fábrica**: o tipo "parafuso" e o gusset/nó de treliça/cantoneira simples passaram de M16 para ⅝" (A325). Taxas: parafuso 0,712 → 0,723; gusset, nó de treliça e cantoneira simples 0,864 → 0,857. Os 26 padrões seguem ≤ 0,90.

## Interface (`JanelaLigacaoProjeto.cs`)

- O seletor de diâmetro (campos `d`, `d_mm`, `d_alma_mm`, `d_mesa_mm` e `d_pino` quando > 0) mostra só a família da especificação do campo: `tipo_parafuso` (ou `tipo_pino`).
- Trocar A325 ↔ ISO leva cada diâmetro ao mais próximo da nova família (⅞" → M22, ⅝" → M16) e destaca o campo.

## Vista 3D "vibrando"

Diagnóstico (`CapturasVibra.cs`, chave `vibra`, com a animação ligada), em todos os 27 tipos:

| Medição | Resultado |
|---|---|
| Vista parada | câmera, tamanho e 3D principal sem nenhuma mudança em 2 s; o 3D principal (Direct3D) não desenha nenhum quadro atrás da aba |
| Recálculo com os mesmos dados | quadros idênticos pixel a pixel, mas 5 a 20 reconstruções da cena (a animação de troca recriava a geometria) |
| Interface | cartão do desenho recriado; o Viewport3D saía e voltava à árvore visual a cada resultado |

A reconstrução custa no máximo 6 ms. A causa provável do tremor visto é a vista sair e voltar à árvore a cada recálculo (um quadro sem desenho), mais a reanimação.

Correção:
- `MontarDesenho` reaproveita o cartão quando a vista ativa não mudou e só troca a geometria (`AtualizarVistas`).
- Uma geometria igual à anterior (JSON + falhas) não reanima nem reconstrói o 3D.

Pendente: se o tremor continuar, falta saber em que situação aparece (parado, girando ou ao digitar). O diagnóstico não reproduziu tremor de câmera.

## Testes
- `testes/test_w12_parafusos.py`:
  - comprimento (polegada, mínimo de linha, sob encomenda, métrico) e descrição;
  - famílias do otimizador;
  - lista e pega em todos os padrões de fábrica (taxa ≤ 0,90);
  - aviso de A325 métrico.
- Capturas `w12paraf` (93-*): seletor em polegada e métrico e lista de compra, em chapa de topo estendida e chapa simples.
