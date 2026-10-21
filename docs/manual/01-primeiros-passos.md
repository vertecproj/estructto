# 1. Primeiros passos

## Um documento só, sempre 3D

Todo modelo é espacial: X e Y horizontais, Z para cima. Não existe mais desenho 2D separado; planta e elevações são só
posições da câmera (como no AutoCAD/Plant 3D). O modelo novo abre **em planta, no térreo** (z = 0): desenha-se como numa
folha de CAD e, quando quiser ver em 3D, **Shift + arrastar** (botão do meio ou esquerdo) orbita.

| | |
|---|---|
| Novo | Ctrl+N: modelo em branco (o programa já abre com um) |
| Abrir | `.estructto` (formato novo), `.estructto`/`.portico` antigos e `.ftl` do Ftool (convertidos ao abrir), `.json` do motor, `.ed3` do CYPE 3D |
| Salvar | `.estructto` (modelo do motor + placas de base + grade estrutural) ou `.json` (só o modelo do motor) |
| Cálculo | Sempre pelo motor Python (F5): combinações NBR 8681 e verificação pela NBR 8800, 14762 e 6118 |

**Arquivos antigos.** Um `.estructto` da versão 2D abre convertido: o X do desenho continua X, o Y do desenho vira Z e o que
sai do plano fica travado (uy, rx, rz em todos os nós). As placas de base vêm junto. O arquivo antigo não é alterado: Salvar
pede o nome e sugere "nome (3D).estructto". Um aviso aparece uma vez por sessão (captura 02-modelo-convertido).

O servidor do motor guarda um modelo só. Ao trocar de aba, o Estructto recoloca o modelo do documento no motor e a pilha de
desfazer recomeça.

## Unidades e convenções

| Grandeza | Unidade |
|---|---|
| Força | kN |
| Comprimento | m |
| Momento | kN·m |
| Carga distribuída | kN/m |
| Tensão e módulo no modelo do motor | kN/m² |
| Materiais (entrada) | MPa |
| Deslocamento | m (tabelas em mm); giro em rad |

Eixos globais: X e Y horizontais, Z para cima. Gravidade tem Fz negativo. N positivo em tração; diagramas N, Vz e My no eixo
local z da barra, momento desenhado do lado tracionado.

## Atalhos principais

Como no AutoCAD: digite o comando (sem clicar em lugar nenhum) e Enter ou Espaço; Enter/Espaço com a linha vazia repete o
último comando; Esc cancela em cascata. Lista completa na [página 2](02-modelo.md) e em F1.

Ctrl+N, Ctrl+O, Ctrl+S, Ctrl+Z (ou U), Ctrl+Y, **F5** calcular, Ctrl+K paleta, F1 ajuda, F3 snap, F8 orto, F10 polar,
F11 rastreio, F12 entrada dinâmica, PgUp/PgDn nível do plano de trabalho, Del apagar.
