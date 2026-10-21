# Estructto v3 — trabalho da noite (20→21/10/2026)

Estado e próximo passo exato. Quem retomar (Claude em sessão nova): leia isto, `CLAUDE.md`, `docs/UNIFICACAO.md`, depois continue
pelo primeiro item `[ ]` ou `[~]`. Branch de trabalho: `unificacao` (checkout principal). Worktrees de agentes em `C:\dev\wt\<nome>`
(branch `wt-<nome>`, build em `C:\dev\build\EstructtoV2-<nome>`, `Directory.Build.props`/`capturar.ps1` com skip-worktree — nunca commitar).
Merge: `git merge --no-edit wt-<nome>` no checkout principal; conflitos de CHANGELOG/MODELO-ESPACIAL = manter os dois lados.
Build de conferência: `dotnet build -c Release -p:OutDir=C:\dev\build\EstructtoV2-check\` (o bin normal pode estar travado por uma janela aberta).

## Pedido do usuário (resumo fiel)
1. Modelar em 3D do zero; **não existe 2D**: igual Plant 3D/AutoCAD — Shift + arrastar orbita; planta/elevação são só câmeras.
2. Snap igual ao CAD (OSNAP completo, rastreamento, polar, orto, F3/F8/F10/F11) e medir igual CAD + Inventor.
3. Ligações metálicas variadas (livros: Manual de Ligações CBCA, "Estruturas de aço — dimensionamento prático", AISC Design Guide 1 2ª ed.),
   calculadas (chapas, parafusos, soldas), editáveis como a placa de base; **agrupar ligações iguais**, dimensionar a pior do grupo e aplicar ao grupo
   (usuário pode editar grupos). Interface fantástica.
4. Modo Realista absurdamente bonito, com ligações e recortes de perfis aparecendo. Exportar **DWG** e **STEP**.
5. Fluxo de modelagem inspirado no Plant 3D (pesquisar), atalhos reaproveitados.
6. Logo nova (refinar a enviada: "E" estrutural em perspectiva + "Estructto" com "o" azul) e aplicar.
7. Ferramentas fáceis de treliças, contraventamentos e afins.
8. Testar tudo, fluxos de trabalho, comparar cálculo com CYPE (DLLs descompiladas).
9. No fim: polimento de design (skills), publicar (push + Release) pela conta bruno.nietsche@prossen.com.br (navegador logado) se o git não bastar.
10. Não pedir nada ao usuário. Usar Fable com cautela para o que for difícil (render realista, design).

## Plano (fases) — dono / status
### Fase 0 — terminar o que estava em curso
- [x] ligar3d (merge 7b6ee94): seleção real → edição; nível → plano de desenho; setas de carga; seletor de seção (worktree `ligar3d`, parcial em Vista3D.cs)
- [ ] publicar: exe + motor empacotado, smoke test (refazer no fim, Fase 6)

### Fase 1 — pesquisa e insumos (paralelo, só docs)
- [x] P1 Plant 3D: fluxo de modelagem de estruturas, comandos, atalhos, snaps, grips, UCS → `docs/v3/PESQUISA-PLANT.md`
- [x] P2 Catálogo de ligações dos 3 livros → `docs/v3/LIGACOES-CATALOGO.md` (tipos, verificações, itens de norma, exemplos numéricos p/ testes)
- [ ] P3 Snap/medir CAD + Inventor → dentro de P1
- [x] P4 Logo (c8bd9d8) nova (SVG + ico) → `docs/logo-estructto.svg`, `codigo/Estructto/estructto.ico/png`

### Fase 2 — núcleo 3D-only (sequencial no Vista3D/MainWindow)
- [x] A1 (merge) Documento único 3D: todo documento é espacial; .estructto/.ftl abrem convertidos; Shift+arrastar orbita em qualquer vista;
      Vista 2D antiga só como câmera planta/elevação. Placa de base/memorial continuam funcionando.
- [x] A2 (wt-snap, merge) Snap CAD completo + rastreamento + entrada dinâmica; Medir (distância, ângulo, área, mínima entre objetos, cadeia) — arquivos Snap3D.cs / Medir3D.cs
- [x] A3 (no A1: Comandos3D, grips, ViewCube) Fluxo Plant: comandos e atalhos do PESQUISA-PLANT (grips, UCS/plano, copiar com ponto base, propriedades)

### Fase 3 — ligações (paralelo: motor Python + UI)
- [x] L1 (19 tipos: flex/rig/contrav) Motor: tipos de ligação (cantoneira dupla, chapa de extremidade, chapa simples/shear tab, emendas, flange plate, gusset de contraventamento,
      base articulada/engastada, ligação de terça, mísula) com verificações NBR 8800 cap. 6; testes com exemplos dos livros
- [x] L2 Agrupamento: identificar ligações iguais (perfis, ângulos, tipo), grupos editáveis, pior do grupo, aplicar
- [x] L2 Agrupamento: identificar ligações iguais (perfis, ângulos, tipo), grupos editáveis, pior do grupo, aplicar
      (branch `wt-grupos`, [LIGACOES-GRUPOS.md](LIGACOES-GRUPOS.md); tipos novos do L1 entram em `VERIFICADORES`)
- [x] L3 (merge) UI: biblioteca de ligações (cards com desenho), janela de dimensionamento estilo placa de base (3 passos, desenho ao vivo)
- [ ] L4 3D: ligação e recortes aparecem no Realista

### Fase 4 — visual 3D e exportação
- [x] R1 (wt-render, Fable) Realista "absurdo": Render3D em HelixToolkit.Wpf.SharpDX (sombra, SSAO, MSAA, PBR, arestas), recortes nas
      juntas e ligações do contrato (motor, placa 2D, exemplos) → [RENDER.md](RENDER.md); fallback nativo sem GPU
- [x] E1 Exportar (merge c5e3f92) DWG (ACadSharp) e STEP (AP214, sólidos facetados/extrusões)

### Fase 5 — ferramentas de produtividade
- [x] T1 (merge 6782233 + fix 3829a72) Assistentes de treliça (Pratt, Howe, Warren, K, banzo paralelo/trapezoidal/arco), contraventamento (X, K, V, chevron, mãos-francesas),
      terças e tirantes, escadas/guarda-corpo simples (se der)

### Fase 6 — qualidade e entrega
- [~] Q1 (e2e parcial em wt-e2e c76b797) Testes de fluxo ponta a ponta (capturas + asserts), revisão de código, verificador de cálculo das ligações (vs livros e CYPE)
- [x] Q2 (wt-design) Polimento de design (skills de design, Fable com cautela), animações
- [x] Q3 (pré-release v0.3-unificado no branch unificacao; main NÃO avançado — ver ENTREGA.md) Publicar: exe + motor, README/manual, merge em main, push, Release no GitHub (conta bruno.nietsche@prossen.com.br pelo navegador se preciso)

## Diário
- 20/10 18:10: plano escrito; tarefa agendada `estructto-retomar-noite` (30 min, LOCK > 45 min = retoma).
- 18:15: disparados: ligar3d (retomada), P1 Plant, P2 x3 livros (docs/v3/ligacoes/), P4 logo (Fable, checkout principal), E1 export (wt-export), T1 treliças (wt-trelicas).

- 18:20: LIMITE DE USO atingido na sessão principal. Agentes acima seguem rodando (verificar worktrees/ docs ao retomar).
  PRÓXIMO PASSO EXATO: (1) conferir resultados de ligar3d, export, trelicas, logo, docs/v3/PESQUISA-PLANT.md e docs/v3/ligacoes/*.md; merge no unificacao;
  (2) disparar L2 agrupamento de ligações no worktree já criado `C:\dev\wt\grupos` (motor: identificar ligações por nó/perfis/ângulos, grupos editáveis, pior do grupo pelos esforços, aplicar; testes);
  (3) depois A1 3D-only (dividir Vista3D em partials: Render/Entrada/Snap/Medir para permitir paralelo), A2 snap/medir, R1 render (Fable), L1/L3/L4 ligações.
- 20:02: limite liberado; 8 agentes retomados; L2 agrupamento disparado em wt-grupos.
- 20:06: ALERTA SEGURANÇA placa_base.py (pivô na borda, tração em chumbador subestimada ~3,3x). Agente wt-placa2 corrigindo (bloco de compressão NBR, CBCA + Pfeil, ACI 318 cap.17). Nucleo.cs (C#) mantém o método legado com aviso na tela — afeta o projeto do guincho/planilha: avisar usuário no relatório final.
- 20:08: L1 disparado em 3 famílias (wt-lig-flex, wt-lig-rig, wt-lig-contrav) seguindo docs/v3/LIGACOES-CONTRATO.md. Placa v2 em wt-placa2. Pendentes de disparo: A1 3D-only+A2 snap/medir (após ligar3d), R1 render Fable (após A1), L3 UI biblioteca (após L1), L4 3D ligações (após R1+L1).
- 20:26: T1 treliças merge. BACKLOG QA: Ue 150x60x20x2,65 reprova distorcional (fórmula simplificada do motor fora do domínio) — rever nbr14762 distorcional.
- 20:28: placa v2 merge (3137145), verificador rodando. Treliças e logo no unificacao. REGRA: nenhum agente edita código no checkout principal (bloqueia merges) — só worktrees. Polimento final: tela inicial ainda diz "pórticos planos" e tem assistentes 2D (A1 deve refazer).
- 20:41: L1 flexíveis merge (693e4bf, 5 tipos, 35 testes, exemplos Pfeil/Gerdau). Registro de módulos = _MODULOS_CONTRATO em ligacoes.py. Placa v2: verificador achou V2-01/V2-02 críticos → wt-placa3 corrigindo.
- 20:50: merges: L1 x3 (19 tipos), placa v2+v3 (V2-01..07 corrigidos), snap/medir. Rodando: A1 so3d, R1 render (Fable), L3 lig-ui. Backlog QA: itens NBR 5.7.x/6.1 a conferir; folgas furo alargado; Ue distorcional.
- 20:57: USUÁRIO pediu: refazer tela inicial, fluxo de novo arquivo e menu Arquivo (feios) → wt-inicio (Fable). E no fim: redesign geral (Q2).
- 01:01: limite liberado; retomados so3d, render, lig-ui, inicio. Usuário reforçou: no FINAL, redesign refinado incluindo ÍCONES.
- 01:20: merges A1 so3d + L3 lig-ui (+fix 8fc65ed). Placa 3D ainda no legado → wt-placa-ui ligando à v2 do motor. Rodando: render (Fable), inicio (Fable), placa-ui.
- 01:27: R1 render DX11 (HelixToolkit.SharpDX) merge 188c571. Fica para Q2: mapa de ambiente/aço mais metálico, enquadramento das vistas gerais, cope, solda triangular, exportação passar cardinal.
- 01:37: revisão v3 REPROVADA (1 bloq + 11 imp + 8 menores). Distribuição: #4,#9 → wt-placa-ui; #3,#5,#6,#12,#13,#14,#20 → wt-fix-render; PENDENTE disparar após merge do wt-inicio: #1(bloq recuperação),#2,#7,#8,#10,#11,#15,#16,#17,#18,#19 → wt-fix-doc.
- 01:40: wt-inicio merge (e4f0183) — tela inicial, novo projeto 3 passos, backstage, opções. Disparado wt-fix-doc (revisão #1,#2,#7,#8,#10,#11,#15-19 + desenho da grade). Restam: placa-ui, fix-render, fix-doc → depois Q1 testes de fluxo, Q2 design/ícones (Fable), Q3 publicar.
- 01:59: merges placa-ui (81796af) e fix-render (c576d9b). Disparados Q2 design+ícones (wt-design, Fable) e Q1 testes de fluxo (wt-e2e). Rodando fix-doc. Depois: corrigir BUGS-E2E, merge design, Q3 publicar.
- 02:12: fix-doc merge (ecfa505). Corrigido registro duplicado de verificadores de ligação (taxa None em grupos) — 1021 testes OK.
- 07:48: design merge (ad91cea). Publicado C:\devuildstructtoV3\publish (Estructto.exe 78 MB + motor\ PyInstaller, 202 MB total). Smoke sem python/ESTRUCTTO_MOTOR: exit 0 em 6 s, sem órfãos. Agentes parados por limite SEMANAL (volta 25/10). Resta: capturas finais, merge em main, push, release.
- 07:54: PUBLICADO: push origin/unificacao + pré-release https://github.com/vertecproj/estructto/releases/tag/v0.3-unificado (zip 123 MB). main do Bruno avançou (1f719d3) com vento por barra/NBR 8400/placa legada: integração pendente (portar ao motor). Próximo (após 25/10, limite semanal): terminar --testes-fluxo, portar trabalho do Bruno, juntar main.
