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
- [~] ligar3d: seleção real → edição; nível → plano de desenho; setas de carga; seletor de seção (worktree `ligar3d`, parcial em Vista3D.cs)
- [ ] publicar: exe + motor empacotado, smoke test (refazer no fim, Fase 6)

### Fase 1 — pesquisa e insumos (paralelo, só docs)
- [ ] P1 Plant 3D: fluxo de modelagem de estruturas, comandos, atalhos, snaps, grips, UCS → `docs/v3/PESQUISA-PLANT.md`
- [ ] P2 Catálogo de ligações dos 3 livros → `docs/v3/LIGACOES-CATALOGO.md` (tipos, verificações, itens de norma, exemplos numéricos p/ testes)
- [ ] P3 Snap/medir CAD + Inventor → dentro de P1
- [ ] P4 Logo nova (SVG + ico) → `docs/logo-estructto.svg`, `codigo/Estructto/estructto.ico/png`

### Fase 2 — núcleo 3D-only (sequencial no Vista3D/MainWindow)
- [ ] A1 Documento único 3D: todo documento é espacial; .estructto/.ftl abrem convertidos; Shift+arrastar orbita em qualquer vista;
      Vista 2D antiga só como câmera planta/elevação. Placa de base/memorial continuam funcionando.
- [ ] A2 Snap CAD completo + rastreamento + entrada dinâmica; Medir (distância, ângulo, área, mínima entre objetos, cadeia) — arquivos Snap3D.cs / Medir3D.cs
- [ ] A3 Fluxo Plant: comandos e atalhos do PESQUISA-PLANT (grips, UCS/plano, copiar com ponto base, propriedades)

### Fase 3 — ligações (paralelo: motor Python + UI)
- [ ] L1 Motor: tipos de ligação (cantoneira dupla, chapa de extremidade, chapa simples/shear tab, emendas, flange plate, gusset de contraventamento,
      base articulada/engastada, ligação de terça, mísula) com verificações NBR 8800 cap. 6; testes com exemplos dos livros
- [x] L2 Agrupamento: identificar ligações iguais (perfis, ângulos, tipo), grupos editáveis, pior do grupo, aplicar
      (branch `wt-grupos`, [LIGACOES-GRUPOS.md](LIGACOES-GRUPOS.md); tipos novos do L1 entram em `VERIFICADORES`)
- [ ] L3 UI: biblioteca de ligações (cards com desenho), janela de dimensionamento estilo placa de base (3 passos, desenho ao vivo)
- [ ] L4 3D: ligação e recortes aparecem no Realista

### Fase 4 — visual 3D e exportação
- [ ] R1 Realista "absurdo" (Fable): avaliar HelixToolkit.Wpf.SharpDX (sombras reais, SSAO, MSAA) vs nativo; recortes e ligações
- [ ] E1 Exportar DWG (ACadSharp) e STEP (AP214, sólidos facetados/extrusões)

### Fase 5 — ferramentas de produtividade
- [ ] T1 Assistentes de treliça (Pratt, Howe, Warren, K, banzo paralelo/trapezoidal/arco), contraventamento (X, K, V, chevron, mãos-francesas),
      terças e tirantes, escadas/guarda-corpo simples (se der)

### Fase 6 — qualidade e entrega
- [ ] Q1 Testes de fluxo ponta a ponta (capturas + asserts), revisão de código, verificador de cálculo das ligações (vs livros e CYPE)
- [ ] Q2 Polimento de design (skills de design, Fable com cautela), animações
- [ ] Q3 Publicar: exe + motor, README/manual, merge em main, push, Release no GitHub (conta bruno.nietsche@prossen.com.br pelo navegador se preciso)

## Diário
- 20/10 noite: plano escrito; Fase 0/1 em disparo.
