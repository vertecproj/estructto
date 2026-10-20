# Logo do Estructto (v3)

Retrabalho da referência enviada pelo usuário (`docs/v3/logo-referencia.webp`): um "E" como pórtico 3D em perspectiva, com nós, e o nome
"Estructto" em sans geométrica pesada com o "o" final azul. Pedido: "mais ou menos o que eu quero, retrabalhe, deixe melhor e aplique".

## O que mudou em relação à referência
| Referência | Decisão | Motivo |
|---|---|---|
| E extrudado em chapas com gradiente, diagonais e nós espalhados | E em **linhas de espessura única** (coluna frontal + 3 vigas em balanço + coluna de fundo fina), 3 nós nas pontas livres | Lê como estrutura de barras (o que o programa calcula), não como letra 3D de clip-art; sem gradiente, igual ao resto da interface (GUIA-DESIGN §1: cor nunca decorativa). |
| Vigas a ~22° e coluna de fundo fechando um paralelogramo | Vigas a **21,5°** (dx 28 / dy −11), **viga do meio mais curta** (dx 20), coluna de fundo com 40% da espessura | Testes lado a lado (`variantes.png`): a 30° e com a caixa fechada o símbolo vira escada/"Z"; a viga curta no meio é o que faz ler "E" a 16 px. |
| Gradiente azul | Azuis chapados da paleta do app: `#2563EB` membros, `#0EA5E9` nós (fundo claro); `#38BDF8` / `#0EA5E9` (fundo escuro); tile do ícone `#1D4ED8` com E branco | Tokens de `TemaPlant.cs` / GUIA-DESIGN §2.2 (Accent.Primary / Accent.Hover). |
| Fonte pesada genérica | **Barlow SemiBold** (OFL 1.1), contornada em paths (sem dependência de fonte) com traço de 2,5% do em para peso visual de Bold; "o" final em `#0EA5E9` | Já empacotada no projeto (`software/prossen_estrutural/web/estatico/fontes/`, licença `OFL-Barlow.txt`); DIN-like, cara de engenharia, não de startup. |

Nós têm anel na cor do fundo (1,6 px) para não "colar" na barra. Pontas arredondadas (`stroke-linecap: round`) em tudo.

## Arquivos
- `docs/logo-estructto.svg` — símbolo primário, 64×64, fundo claro.
- `docs/logo-estructto-horizontal.svg` — símbolo + "Estructto", fundo claro (texto `#0F1B33`).
- `docs/logo-estructto-escuro.svg` — idem sobre `#141820` (texto `#F1F5F9`).
- `docs/logo-estructto-icone.svg` — símbolo sobre tile (o que vira o .ico).
- `codigo/Estructto/estructto.ico` — 16/24/32/48/64/128/256 (PNG embutido). **16, 24 e 32 desenhados à mão** em coordenadas inteiras,
  sem nós (16/24) ou com nós de 1,9 px (32); ≥ 48 é o SVG do tile.
- `codigo/Estructto/estructto.png` — 256 px (quadro maior do ico).
- `codigo/motor/prossen_estrutural/relatorio_estilo/icones/logo.svg` — símbolo (cabeçalho do relatório, 11 mm).
- `.../icones/marca.svg` — lockup monocromático em `currentColor` (carimbo NBR 10582, 15 mm; o CSS pinta com `--r-tinta`).

Gerador: `gerar_logo.py` (fontTools para contornar o texto, resvg para rasterizar, Pillow para o ICO). Fica fora do repo; a geometria do E
está toda nos SVGs acima — para refazer, basta re-rasterizar.

## Onde aparece no app
- Barra de título e botão "Arquivo" da faixa: `TemaPlant.Selo(24)` — quadro de 24 px do .ico (`AjustarFaixa` força 24×24).
- Tela inicial (`Inicio.cs`): `TemaPlant.Selo(32)`, 32 px.
- `Selo(px)` escolhe o quadro do .ico com tamanho ≥ px, sem reescalar: antes o PNG de 256 era reduzido pelo WPF para 22/36 px e ficava
  borrado. `Selo()` sem argumento continua devolvendo o PNG de 256.
- Relatório HTML: `logo.svg` no cabeçalho, `marca.svg` no carimbo.

## Conferência
Build separado em `C:\dev\build\EstructtoV2-logo\` + `Estructto.exe --capturas` → `01-inicio.png` (título + tela inicial) e
`00-icones-*.png`. Ícone 16 px: E legível, coluna nítida em 2 px, vigas suaves. 24/32: idem, 32 com nós.
