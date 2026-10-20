# Plano Estructto v2 — modernização (madrugada 20/10/2026)

Base: `PESQUISA.md` (seção 6 = lista P1/P2/P3). Regras e estado: `STATUS.md`. Uma etapa por vez (os arquivos se cruzam).
Cada etapa = 1 briefing ao agy + revisão minha (diff, build, `portico\compilar.ps1 -Testes`, `capturar.ps1` + olhar PNGs) + commit local.

## Princípios (decididos)
- Sem dependências novas (só Fluent.Ribbon, AvalonDock, WpfMath). Remover WinForms quando o Inspector WPF existir.
- Usabilidade de CAD intacta: ribbon, linha de comando, atalhos, snaps, painéis encaixáveis, seleção na vista.
- Tema escuro moderno com tokens (PESQUISA 4.1–4.3), grade de 4 px, raio 4–6, animações 150 ms, Segoe UI Variable, números tabulares.
- Ícones: motor próprio `Icones.cs` (32×32, cantos/juntas arredondados, duotone) para estrutura; glifos Tabler (MIT) 16/20 px para UI geral.
- Motor de cálculo NÃO muda (resultados idênticos; testes do motor sempre passando).
- Nada some: toda função atual continua acessível (pode mudar de lugar, com dica no mouse).

## Etapas
| # | Etapa | Arquivos | Pronto quando |
|---|---|---|---|
| E1 | Tokens + estilos globais (TextBox, ComboBox, Button, Toggle, Check, Tab, DataGrid, TreeView, ScrollBar fina, Expander, ToolTip, Menu) + ribbon/AvalonDock retintados | TemaPlant.cs, App.xaml, MainWindow.xaml | capturas: todas as telas com o tema novo, sem controle "Windows clássico" sobrando |
| E2 | Ícones: cap/join arredondados, duotone, todos os ícones da ribbon revistos 1 a 1, novos (momento nodal, parafuso, solda, medir, snap, grade, ortho, comparar, placa) + glifos 16 px | Icones.cs, MainWindow.xaml.cs | captura de todas as abas da ribbon + folha de ícones (PNG com todos, gerada pelo --capturas) |
| E3 | Inspector WPF nativo no lugar do PropertyGrid (cartões, seletor de perfil com miniatura, campos com expressão, apoio rápido, resumo de aproveitamento) | MainWindow.xaml(.cs), Paineis.cs, novo Inspector.cs | edita tudo que o PropertyGrid editava (nó, barra, carga, modelo); capturas com nó/barra/carga selecionados |
| E4 | Vista: diagramas com gradiente translúcido, legenda/escala, mapa de calor de aproveitamento, HUD de resultados (N V M δ %Ap + escala), inspeção ao passar o mouse, barra de status com pílulas (Snap/Grade/Ortho/unidades) | Vista.cs, MainWindow | capturas 03–07 bonitas e legíveis |
| E5 | Placa de base refeita (ver abaixo) | Janelas.cs (JanelaPlaca, PlanoPlaca), JanelaComparar.cs | fluxo em 3 decisões, sem rolagem obrigatória na coluna direita em 1280×720; testes do motor iguais |
| E6 | Diálogos no padrão novo: verificação, casos/combinações, relatório, assistentes (preview ao vivo), ajuda, mensagens, comparar; tela inicial nova | Janelas.cs, Casos.cs, Relatorio.cs, Documentos.cs, Assistentes.cs, MainWindow | capturas 01, 22–28 |
| E7 | Command palette Ctrl+K (comandos + perfis), entrada dinâmica no cursor ao desenhar barra (comprimento/ângulo, Tab alterna) | MainWindow, Vista.cs | uso pelo teclado; captura da palette |
| E8 | QA geral (capturas 1600×900 e 1280×720), revisão crítica tela a tela, exe `estructto-v2\Estructto-v2.exe`, RELATORIO.md | — | tudo marcado no STATUS |

## Placa de base (E5) — crítica e redesenho
Problemas atuais: muita coisa ao mesmo tempo (5 cartões + rodapé), coeficiente separado da fixação, "melhores opções" e "trocar" competem,
"Avançado" esconde coisas que importam (tipo de carga/risco definem n), aviso de barra inclinada ocupa espaço demais, solda reprova sem dizer o que fazer.
Redesenho:
1. Topo: **veredito em 1 linha** + 3 chips (Fixação / Chapa / Solda, % e ✓✕) clicáveis que levam ao item. Aviso da barra inclinada vira chip amarelo "Ligação inclinada: detalhar" com dica.
2. Esquerda: desenho grande (vista de cima com cotas editáveis) + vista 3D maior com bloco de concreto, hef e cones (alternar Planta | 3D | Forças | Ficha | Cálculo).
3. Direita em 3 passos numerados:
   **① Fixação** — tipo (Chumbador mecânico | químico | Parafuso), o escolhido e "Trocar/Comparar"; quantidade; condições que mudam o n (tipo de carga, risco, fck) visíveis, não escondidas.
   **② Chapa** — espessura (régua) + tamanho (botão "Ajustar tamanho" com as regras na dica) + aço.
   **③ Solda** — perna (auto) + eletrodo; se reprovar, dizer o porquê e o que fazer (ex.: "parede de 1,1 mm: use perna ≤ 1 mm ou chapa de reforço").
4. Rodapé: "Dimensionar tudo" (com preferência Mais leve/Furo menor), "Aplicar também em…" (nós) e pior caso, Cancelar/Aplicar.
5. Coeficiente de segurança fica dentro de ① (chumbador) e de ② (placa/parafuso), com o valor e a origem ("tabela Walsywa: dinâmica, alto risco").
