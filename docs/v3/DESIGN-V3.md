# Design v3 — crítica das capturas e polimento final

Base: capturas de `c576d9b` (`Estructto.exe --capturas`, 278 arquivos, sem `erros.txt`), em `C:\dev\build\EstructtoV2-design\capturas-antes`.
Layout (faixa, painéis encaixados, vista 3D, tela inicial) fica. Só o acabamento muda. Imagens curadas (antes/depois) em `docs/v3/design/`.

## 1. Crítica honesta do estado anterior

| Área | O que estava errado | Evidência |
|---|---|---|
| Ícones | Duas famílias convivendo: Tabler (grade 24, traço 1,5) e desenho próprio (grade 32, traço 2) — à mesma escala saem com pesos diferentes (`grade`, `snap`, `busca` mais finos que `barra`, `engaste`). `verif`/`veriflista` são barras coloridas cheias (3 cores saturadas) e `calc` um triângulo quase sólido: pesam mais que todo o resto da faixa. Botões reaproveitando ícone errado: Níveis = tabela, Grade = perfil I, Laje = carga distribuída, Pilares entre níveis = barra, Treliças/3D completo/Isométrica/Gerar 3D = o mesmo cubo, Gerenciar ligações = Projetar ligação = Unir nós. Faltavam: pilar, chapa, ligação, grupos, treliça, contraventamento, modos de atração, DWG/STEP, casa do ViewCube. | `00-icones-ribbon.png`, `09-ribbon-modelo.png` |
| Rodapé | Seis chips azuis contornados (Snap, Grade, Polar, Objetos, Rastreio, Dinâmica) sempre ligados: a barra de status vira a área mais colorida da janela, contra a regra de ≤ 5 % de acento do GUIA §1. | `02-modelo.png` |
| Tipografia | Tudo em Segoe UI, um peso só (SemiBold) para títulos de janela, de cartão e de tela inicial; sem voz própria. Tamanhos espalhados (10 / 10,5 / 11 / 11,5 / 12 / 12,5 / 13 / 13,5 / 14 / 15 / 16) sem escala. | todas |
| Janelas | Três famílias de cabeçalho/rodapé/cartão escritas por agentes diferentes: `UiAcoes` (cabeçalho Surface.0, título 16, rodapé com margem 16/8/16/12), `ComandosAnalise` (Surface.1, título 15, rodapé padding 14/7) e `UiPlaca` (base). Chips e cartões iguais por sorte, não por sistema. | `48-espacial-vento.png` vs `35-hud.png`, placa, modal |
| Faixa | Rótulo do grupo ("Gerar", "Planos") na mesma cor do texto secundário dos botões: não lê como rótulo. Separadores entre grupos pouco visíveis. Caixas combinadas (Seção, Cardinal) com fundo mais claro que os botões ao lado. | `09-ribbon-modelo.png` |
| Hierarquia | Tela inicial: o botão "Novo projeto" em acento cheio (único bloco sólido da tela) compete com os cartões de modelo; "Recentes" com dois cartões idênticos ("Galpao demo" duplicado) e ícones de aviso âmbar grandes. | `01-inicio.png` |
| Vista 3D | Cromo do canvas (HUD, ViewCube, bússola de eixos, legenda de aproveitamento) coerente, mas o ViewCube escreve TOPO/FRENTE/DIREITA em 9,5 px bold cinza sobre face cinza (contraste baixo) e a "casa" é um contorno solto. | `42-realista-galpao_aco.png` |
| Cores | Escada de ardósia (Tailwind slate + sky) correta e consistente, mas `Border.Default #475569` é mais claro que precisa (contornos de input "gritam" no escuro) e `Text.Secondary #94A3B8` fica abaixo do ideal para rótulos lidos o dia todo. | inspector, tabelas |

O que já estava certo e fica: escada de superfícies 0..3 com hairline de 1 px e sem sombra; acento único ciano; semântica verde/âmbar/vermelho só em resultado; números tabulares nas tabelas; HUD de diagramas; cartões numerados ①②③ nas janelas de ações.

## 2. Decisões do polimento

1. **Tokens** (`TemaPlant.cs`): escala tipográfica fechada (`Font.Micro 10 · Caption 11 · Body 12 · Subtitle 13,5 · Title 16 · Display 22`); fonte de exibição **Barlow** (OFL, empacotada em `codigo/Estructto/Fontes/`) para títulos de janela, de tela inicial, cabeçalhos de cartão e números de destaque; Segoe UI Variable continua no corpo (legibilidade em 11–12 px no Windows). Bordas um degrau mais quietas (`Border.Default #3F4B5E`, `Border.Subtle #2F3948`), `Text.Secondary #9FABBD`. Novo `Elevation.Highlight` (branco a 6 %) para a aresta superior de cartões e HUD flutuante. Raios como recurso (`Radius.Control 4 · Card 6 · Window 8`).
2. **Ícones** (`Icones.cs`): uma família só — grade 32, traço 2 (= 2 px a 24/32, 1 px a 16), pontas redondas, cor cheia no traço e duotone a 18 % só no "objeto" da ação; segunda cor (acento) apenas onde carrega significado (o que a ferramenta cria/afeta). Tabler fica só para glifos de UI (setas, busca, fechar, olho, engrenagem) redesenhados no mesmo peso via fator 1,5 → 2 px. Novos: `grade`, `niveis`, `pilar`, `membro`, `chapa`, `laje`, `ligacao`, `grupos`, `trelica`, `contrav`, `copiarpav`, `pilaresniv`, `3dcompleto`, `dwg`, `step`, `casa`, `dinamica`, `verif`/`veriflista`/`calc` redesenhados no peso da família. Folha em `00-icones-ribbon.png`.
3. **Faixa e rodapé**: rótulo de grupo em `Text.Tertiary`; chips do rodapé sem contorno (ligado = fundo Surface.2 + ícone em acento; desligado = texto terciário), acento volta a ≤ 5 % da janela.
4. **Janelas**: `UiAcoes.Cabecalho/Rodape` e `ComandosAnalise.Cabecalho/Rodape` passam a usar os mesmos valores (fundo Surface.1, título Display 16, padding 16/10, rodapé 14/8, botões 28 px); cartões todos `Surface.2 + Border.Subtle + raio 6 + highlight superior`.
5. **Vista 3D**: ViewCube com rótulos `Text.Primary` 10 px e face mais escura; ícone de casa da família; legenda e HUD com o highlight de elevação.
