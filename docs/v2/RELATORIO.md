# Estructto v2 — relatório da madrugada (20/10/2026)

**Para abrir:** `estructto-v2\Estructto-v2.exe` (cópia separada; a versão de ontem continua em `ftool\Estructto.exe`, intacta).
Nada foi para o GitHub. Código em `estructto-v2\codigo`, git só local (branch `v2`, um commit por etapa).
Telas de todas as janelas: `C:\dev\build\EstructtoV2\capturas` (regenerar: `codigo\Estructto\capturar.ps1`).
Cálculo não mudou: os 196 testes antigos passam + 4 novos (diagnóstico da solda) = 200.

## O que mudou
| Etapa | Resultado |
|---|---|
| Tema | Paleta nova (GUIA-DESIGN.md), todas as cores num lugar só (`TemaPlant.cs`), estilos modernos para todos os controles, ribbon/painéis/barra de título no tema. |
| Ícones | Motor de ícones refeito (traço arredondado, duotone, cores do desenho). Estruturais desenhados à mão (apoios, cargas, N/V/M, placa, parafuso, solda, medir…); gerais do Tabler (MIT). Folha: `capturas\00-icones.png`. |
| Propriedades | PropertyGrid do WinForms substituído por painel WPF: cabeçalho com aproveitamento, campos com unidade e expressão (2,5+0,3), seletor de perfil com miniatura, Sim/Não em pílula, multi-seleção, ações rápidas (apoios, verificar, trocar perfil). WinForms removido do programa. |
| Vista | Enquadra ao abrir/calcular; diagramas com gradiente e rótulos sem sobreposição; HUD no canto (Deformada/N/V/M/% e escala); mapa de calor de aproveitamento; inspeção passando o mouse na barra; linha de comando fora do desenho; toasts; pílulas Snap/Grade/Orto/Polar. |
| Placa de base | Refeita: veredito + chips no topo; Planta / 3D girável (concreto, hef, cones, solda) / Forças / Ficha / Cálculo em tabela; 3 passos ① Fixação ② Chapa ③ Solda; coeficiente com a origem escrita; solda que reprova diz por quê e o que fazer; Sugerir; Aplicar também em… (pior caso). |
| Janelas | Tela inicial nova (miniaturas dos assistentes e dos recentes), árvore com ícones, tabelas com barra de capacidade e "só críticos", verificação/casos/relatório/assistentes/ajuda/mensagens refeitos, ribbon reorganizada, plurais corretos. |
| Teclado | Ctrl+K: busca de comandos (tolera erro de digitação), perfis e objetos ("nó 3"). Ao desenhar barra/medir: caixas Comprimento/Ângulo no cursor (Tab, Enter), como no AutoCAD. |

## Decisões que tomei sozinho (veja todas em STATUS.md → "Decisões tomadas")
- Placa: o cartão "Coeficiente de segurança" acabou — n do chumbador fica no ① (com a origem) e o da chapa no ②. "Melhores opções" → botão Sugerir. "Outras fixações" → "Aplicar também em…". "Mais leve/Furo menor" só escolhe a preferência; quem dimensiona é "Dimensionar tudo".
- Ribbon: Assistentes foi para Desenhar; Medir saiu da aba Vista (continua em Início, menu e comando); nomes encurtados.
- Inspetor: "Rolete" e "Móvel" unificados como "Móvel"; categoria Resultados começa recolhida.
- Tabela de verificação em telas estreitas esconde colunas secundárias (a janela de verificação mostra tudo).

## Para você conferir / decidir
1. A usabilidade real com mouse: tudo foi testado por capturas e testes automáticos (sem usar sua tela) — falta seu teste de mão, principalmente
   girar o 3D da placa, o seletor de perfil e a entrada dinâmica.
2. As decisões acima (se alguma não fizer sentido para vocês, é reversível — um commit por etapa).
3. Pendências pequenas: rótulo do perfil encosta no símbolo de momento no nó 3; rótulos apertados na prévia da treliça; sem ícone de busca no campo da Ajuda.
4. Se aprovar, levar a v2 para o `ftool` e o GitHub (eu não fiz isso, como você pediu).

## Como foi feito
Pesquisa e guia de design (agy/Gemini até a cota acabar à 1h; depois subagentes Claude), skills baixadas: taste-skill, web-design-guidelines,
awesome-design-md, frontend-design, ui-ux-pro-max, dotnet-wpf-modern, wpf-best-practices (em `docs/v2/refs`, fora do git). Cada etapa: briefing →
subagente → build + testes + revisão das capturas com o checklist do guia → commit. Documentos: PESQUISA.md, GUIA-DESIGN.md, PLANO-V2.md, STATUS.md.
