# Estructto v2 — estado do trabalho autônomo (madrugada de 20/10/2026)

> **HISTÓRICO (encerrado).** Em 20/10 às 9h30 o Bruno aprovou a v2 e ela virou a versão oficial em `ftool` (GitHub `vertecproj/estructto`,
> Release v0.2). As regras abaixo ("não subir no git", pasta `estructto-v2`) valiam só para aquela madrugada.

> Quem retomar: leia isto inteiro, depois `PLANO-V2.md` (quando existir) e `PESQUISA.md`. Atualize ESTE arquivo ao fim de cada etapa
> (marque [x], escreva o próximo passo exato) e toque o arquivo `LOCK` (escreva a data/hora atual nele) a cada ~15 min de trabalho.

## Regras (do Bruno — não negociar)
- Trabalhar SÓ em `G:\Meu Drive\3. UPWORK\10000\7. AGENTES\estructto-v2`. Git local (branch `v2`, SEM remoto). Commit local a cada etapa.
  NUNCA `git push`, nunca criar remoto, nunca mexer na pasta `ftool` nem no `ftool\Estructto.exe` (ele usa a versão antiga amanhã).
- NUNCA pedir nada ao Bruno nem pedir acesso (sem computer-use/controle da tela, sem perguntas). Decidir sozinho e registrar a decisão aqui.
- QA visual: `codigo\Estructto\capturar.ps1` (o programa gera PNGs em `C:\dev\build\EstructtoV2\capturas`); ver os PNG com a ferramenta Read.
- Economizar tokens: delegar o grosso ao agy (Gemini): `agy --dangerously-skip-permissions --add-dir "<pasta>" -p "<briefing>"`
  em segundo plano; briefing fechado (arquivos, o que não mexer, como provar). Conferir sempre: build, `..\portico\compilar.ps1 -Testes`, capturas.
  Detalhes: `ftool\docs\DELEGAR-AGY.md`. Só um agy por vez editando os mesmos arquivos.
- Build: `cd codigo\Estructto; dotnet build -c Release` (saída em C:\dev\build\EstructtoV2). Testes do motor têm que continuar passando.
- Objetivo: interface moderna, bonita, impressionante, dinâmica e fácil, mantendo a usabilidade de CAD (ribbon, linha de comando, snaps,
  atalhos, painéis). Cuidar de cada tela, botão e ícone. Questionar o que não faz sentido. Placa de base refeita sólida e simples.

- Design: TODA etapa lê `docs/v2/GUIA-DESIGN.md` (síntese das skills taste-skill, frontend-design, web-design-guidelines, ui-ux-pro-max, dotnet-wpf-modern,
  wpf-best-practices e DESIGN.md do awesome-design-md, baixadas em `docs/v2/refs`, fora do git) e usa o checklist dele para revisar as capturas.

- **agy SEM COTA até ~26/10** (erro 429). Usar subagentes do Claude (ferramenta Agent, model sonnet) com os briefings `docs/v2/v2-E*.txt`,
  um de cada vez (mesmos arquivos). Eu reviso: build, testes, capturas.

## Checklist
- [x] Cópia `estructto-v2` + git local (commit `base v2`).
- [x] Modo `--capturas` (`codigo\Estructto\capturar.ps1`, 61 PNG em C:\dev\build\EstructtoV2\capturas).
- [x] Pesquisa `docs/v2/PESQUISA.md`.
- [x] `docs/v2/PLANO-V2.md`: etapas E1–E8.
- [x] E1 tokens + estilos globais (TemaPlant.cs fonte unica; App.xaml so estilos)
- [x] E2 ícones (a8973f9; pendente: ícones na árvore e no menu de contexto → E6)
- [x] E3 Inspector WPF (sai o PropertyGrid)
- [x] E4 vista: diagramas, mapa de calor, HUD, barra de status
- [x] E5 placa de base refeita (JanelaPlaca.cs + PlacaDesenho.cs; capturas 20/21/22/29 em 1600×900 e 1280×720)
- [x] E6 diálogos + tela inicial + ribbon/painéis
- [x] E7 command palette + entrada dinâmica (64682ef, 5fe598c)
- [x] E8 QA, exe estructto-v2\Estructto-v2.exe, docs/v2/RELATORIO.md (c52b4dc)

## Próximo passo exato
NADA — trabalho concluído. Aguardar o Bruno avaliar (ver RELATORIO.md).
## Decisões tomadas
- E8 (acabamento + QA final): PLURAIS reais via `Pl.N(n, sing, plur)` (Plural.cs), nenhum "(s)" restou nos textos do programa. RIBBON: já estava sem corte em 1600 e 1280 em todas as abas (conferido nas 9 capturas 09-ribbon-*); mensagem da barra de status encurtada (ΣRx 0,00 · ΣRy 5,88 kN · …) para o aviso "N barra(s) não passa" caber em 1280. 3D DA PLACA: rótulo hef é desenhado por último e desce abaixo do rótulo da solda se colidirem. VERIFICAÇÃO (tabela do painel): cabeçalho "Aprov."; painel <980 px esconde Flambagem e KL/r plano, <1250 px também KL/r fora e Esbeltez (dica no tooltip; a janela Verificação mostra tudo) — sem rolagem horizontal em 1280. TELA INICIAL compacta (<400 px): hero de 1 linha, assistentes sem miniatura (título+subtítulo), 3 recentes numa linha com miniatura 48x32 e rodapé visível. Outros: Forças da placa com rótulos T/V para dentro da chapa e "linha de giro" dentro da chapa; Comparar com filtros quebrando linha ("Informativas" não corta); inspetor "Rolete" → "Móvel" (igual à faixa); resumo da paleta não corta em ".estructto"; "placa rígida: sem tração…" em minúscula.
- E7 (paleta + entrada dinâmica): PALETA = Ctrl+K, janela flutuante sem moldura (Paleta.cs; `PaletaVisual` reutilizada nas capturas). REGISTRO ÚNICO: todo botão da faixa (Botao/Alternar/BotaoFerramenta/BotaoDiagrama) se registra sozinho via `Reg`; comandos só de teclado entram em `RegistrarExtras`; o teste das capturas falha se algum comando de `Comandos` (linha de comando) não tiver item. Busca difusa sem acento (prefixo de palavra > contém > sinônimos > subsequência > dica); "nó 3"/"barra 2" seleciona e enquadra; "w 200"/"tubo 100" lista perfis (Enter aplica à seleção ou às próximas barras); vazio = recentes (6, em %AppData%/NovoFtool/recentes-cmd.txt) + comandos. Comandos com argumento (QD, FN, CASO, NOVOCASO, RESULTADO, ABA) preenchem a linha de comando. ENTRADA DINÂMICA (Vista.cs): em Barra/Medir em andamento, caixinhas Comprimento e Ângulo junto ao cursor; dígitos/ponto vão para o campo ativo, Tab alterna, Backspace apaga, Enter confirma, Esc limpa; valor não digitado vem do mouse (que já respeita snap/orto/polar); vírgula, ; ou < levam o que foi digitado para a linha de comando (coordenadas x,y continuam funcionando). Testes em 38-teste-paleta-entrada.txt (22 verificações OK).
- E6 (diálogos, tela inicial, ribbon, painéis): TELA INICIAL = faixa de destaque "Modelo em branco" (tecla B) + 4 cartões de assistente com miniatura vetorial gerada pelo próprio gerador (Inicio.cs, `Miniatura`) + arquivos recentes com miniatura do modelo (cache por data do arquivo; "arquivo não encontrado" vira chip) + Abrir/Importar .ftl + atalhos; em janela baixa (<400 px) o cabeçalho some. Fundo opaco (o HUD aparecia por trás). Menu Arquivo → Assistentes/Recentes usam os mesmos cartões. ÁRVORE do projeto: ícone por grupo/item, contagens em pílula, coordenadas e sinalizadores em ícones (apoio, carga, placa, rótula) em vez de texto cru; "placa" cortado resolvido. MENU DE CONTEXTO com ícones. TABELAS: números e cabeçalhos à direita; Verificação com barra de capacidade na coluna Aproveitamento, chip Passa/Não passa, ordem pelos mais críticos, filtro "Todas | Só críticos" (reprovadas ou >90 %) e contagem; estados vazios; painel 210→222 px. DIÁLOGOS: Verificação (largura 1200, barra+chip, linha atual/mais leve marcada, filtro "só os que passam", contagem, % acima de 999 vira ">999%"); Casos (títulos de seção, texto de ajuda que quebra linha, chip "ativo", estado vazio com orientação, cabeçalho da tabela só se houver combinações); Relatório (Resumo|Completo em controle segmentado, faixa de ferramentas); Assistente (miniatura em cada tipo, estatística "N nós · M barras" na prévia); Caixa (ícone real tonal no lugar de glifo); Ajuda (busca, cartões por seção, passos numerados, convenções fixas no rodapé, estado vazio). RIBBON: Assistentes passou de "Modificar" para "Desenhar" (é criação); Modificar = Mover/Copiar/Espelhar | Dividir/Apagar; "Medir" saiu da aba Vista (continua em Início, menu de contexto e comando); Ampliar/Reduzir diagrama entraram no grupo Diagramas (grupo "Escala" extinto); nomes curtos (Verificação, Comparar, Dimensionar, Nomes) com a explicação completa na dica — a aba Resultados não estoura mais em 1280. Nenhuma função removida.
- E5 (placa): JanelaPlaca e PlanoPlaca saíram de Janelas.cs para `JanelaPlaca.cs` (janela) e `PlacaDesenho.cs` (UiPlaca = chips/segmentado/barra/campo
  numérico/stepper/tabela, PlanoPlaca e a nova Placa3D). Topo = veredito numa linha + chips de aviso (ligação inclinada, risco não informado/coeficiente
  abaixo, carga no nó) + 3 chips Fixação/Chapa/Solda que rolam e realçam o passo. Esquerda = [Planta | 3D | Forças | Ficha | Cálculo]; a mini-vista
  isométrica dentro da Planta saiu (virou a aba 3D, girável com o mouse, projeção própria sem Viewport3D: concreto translúcido, hef cotado, cones 1,5·hef,
  porcas, solda em laranja/vermelho). Direita = 3 passos com "mais ▾" (ver cálculo + opções raras). O cartão "Coeficiente de segurança" acabou:
  o n do chumbador fica no ① com a origem escrita e os 3 controles (carga, risco, fck) visíveis; o n da placa/parafuso fica no ② (linha "Aço": fy + n).
  O coeficiente digitado do chumbador foi para "mais" do ① (o normal é a tabela). "Melhores opções" virou o botão Sugerir (popup com 3 opções e Usar).
  "Outras fixações" virou o botão do rodapé "Aplicar também em…" (painel com pior caso, marcar, dimensionar pelo pior caso); o botão Aplicar mostra
  "Aplicar em N nós" quando há marcados. "Prefiro Mais leve/Furo menor" agora só escolhe a preferência (não redimensiona sozinho); quem dimensiona é
  "Dimensionar tudo". Borda e "medir pelo perfil/sobra", "giro na mesa" e "sem sobrepor cones" ficam em "mais" do ② (borda também pelas cotas).
  Tipo de fixação = segmentado Mecânico | Químico | Parafuso (ao trocar o tipo escolhe o menor do tipo que passa). Solda reprovada mostra
  "Por quê / O que fazer" via novo `Solda.Diagnosticar` (com testes) e botão "Usar perna X mm / E90XX" quando há solução na tabela.
  JanelaComparar no mesmo padrão: pílulas raio 4, cartão neutro com faixa de status de 3 px e chip, avisos 5.3 viram chips (texto completo na dica e em
  "ver o texto completo"), busca com ícone em vez de emoji. Após "Usar este chumbador" a placa recalcula (antes não recalculava).
- E4: HUD = cartão no canto superior esquerdo da vista (Hud.cs; substitui a legenda solta); "% Aprov." do HUD e o botão "Mapa de calor" (aba Resultados, comando CALOR) usam o modo Verificação com 4 faixas (≤75 verde, ≤90 ciano, ≤100 âmbar, >100 vermelho) e selo ⚠/✕; o botão "Barras" segue com o modo antigo de 3 cores. V negativo = rosa (#F472B6) e M negativo = laranja (#FB923C) (tokens novos em TemaPlant). Rótulos viram pílulas com anti-sobreposição (busca em anéis; os que não cabem são omitidos, exceto reações/%, que são sempre mostradas); nomes dos perfis somem enquanto N/V/M está ativo. Enquadrar automático ao abrir/calcular (F5/diagrama) até o usuário dar zoom/pan; F4 ou duplo clique na roda volta a enquadrar. Linha de comando saiu de cima do desenho (linha própria sob a vista, histórico recolhível ▴); mensagens do Log viram toast de 4 s no canto superior direito. Barra de status: pílulas Snap/Grade/Orto/Polar/Objetos/Rastreio com ícone + nome, coordenadas e unidades.
- E3: o Inspector mostra as propriedades na ordem de declaração dentro de cada categoria; "Resultados" começa recolhida (o resumo com aproveitamento/N/V/M fica no cabeçalho); editores de texto longo (perfil, família, material, posição, travamento) ficam com rótulo em cima e largura toda para não cortar nomes; pares curtos (X|Y, Fx|Fy) em 2 colunas; Esc cancela, Enter aplica, setas incrementam (Shift x10, Ctrl /10). Troca de perfil: popup com busca (aceita "Ret 20x50"). WinForms removido por completo (UseWindowsForms off; Screen do modo --tela2 virou P/Invoke user32).

## Registro
- 00:10 início; cópia feita; agy de pesquisa e de capturas disparados.
- 00:20 pesquisa pronta; plano escrito. Aguardando agy das capturas para iniciar E1.
- 00:30 capturas ok (commit c5ade08); E1 disparada.
- 00:45 skills de design baixadas em docs/v2/refs (find-skills); agy sintetizando GUIA-DESIGN.md em paralelo à E1.
- 01:00 agy sem cota (138 h). E1 parcial commitada; GUIA-DESIGN.md pronto. Subagente Claude assume E1.
- 02:20 E1 concluída por subagente (9c9cb7a; build ok, 196 testes, capturas revisadas). E2 disparada.
- 02:30 briefings prontos: docs/v2/v2-E3.txt … v2-E7.txt (E8 = QA final + publish + RELATORIO). Cada etapa: subagente Claude sonnet com o prompt-padrão (regras + ler v2-EN.txt + GUIA + verificação build/testes/capturas + commit local + tocar LOCK).
- 02:45 E2 concluída (a8973f9; folha 00-icones.png revisada: boa). E3 disparada.
- 03:05 E3 concluída (6795e71; Inspector revisado: muito bom). E4 disparada.
- 04:10 E4 concluída (build 0 erros, 196 testes, capturas 02–08/35–39 revisadas). Falta só o commit.
- 03:25 E4 concluída (366357d; 06-momento revisado: ótimo). E5 disparada (opus).
- E5 concluída: build 0 erros/0 avisos, 200 testes (4 novos de Solda.Diagnosticar), capturas da placa revisadas (no1/no4 × 5 vistas × 2 tamanhos,
  trocar, parafuso, sugerir, aplicar-em, comparar). Pendências menores: na 3D do nó 4 o rótulo "hef" encosta no rótulo da solda; em 1280×720 com
  solda reprovada a coluna da direita chega ao limite (o "mais" do ③ fica rente ao rodapé).
- 04:00 E5 concluída (bbce7b9; 3D revisado: muito bom). E6 disparada.
- E6 concluída: build 0 erros\/0 avisos, 200 testes, capturas 01, 07, 09, 10, 22–28 (e novas 01-backstage-recentes, 07-verificacao-criticos, 23-*-passam, 24-casos-combinacoes, 28-ajuda-busca\/vazio) revisadas nas duas resoluções.
- 04:25 E6 concluída (1a73342; 01-inicio revisado: ótimo). E7 disparada.
- E7 concluída: build 0 erros/0 avisos, 200 testes, capturas 38-palette* e 39-entrada-dinamica revisadas.
- 07:20 E7 concluída. E8 disparada.
- 07:50 E8 concluída (c52b4dc); RELATORIO.md escrito. FIM.
