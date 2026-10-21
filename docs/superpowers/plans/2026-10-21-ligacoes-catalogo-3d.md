# Estructto v0.5 — Ligações que encaixam, catálogo, vista 3D — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ligações donas das pontas das barras (estender/aparar/recortar) com cálculo coerente, otimizador limitado, editor navegável por clique, catálogo completo de perfis, setas/sombra corretas e arquivo novo rápido.

**Architecture:** O motor Python calcula pontas e recortes e devolve `geometria.pontas[]` (contrato aditivo); WPF (render, DWG, STEP) só aplica, com fallback em `Juntas()`. Catálogo vira CSV por família no motor, lido pelo WPF via API. Cargas passam ao pipeline DX.

**Tech Stack:** Python 3 (unittest) em `estructto/codigo/motor`; C# .NET 8 WPF + HelixToolkit.Wpf.SharpDX em `estructto/codigo/Estructto`.

**Spec:** `estructto/docs/superpowers/specs/2026-10-21-ligacoes-catalogo-3d-design.md`

## Global Constraints
- Regras `estructto/CLAUDE.md`: doc em `docs/` + linha no `docs/CHANGELOG.md` por mudança; motor só muda com teste novo; push só se pedido.
- Cada frente em worktree `C:\dev\wt\<frente>` (branch `wt-<frente>`); build `dotnet build -c Release -p:OutDir=C:\dev\build\EstructtoV2-<frente>\`.
- Testes motor: `cd codigo\motor; python -m unittest discover -s testes` — nunca quebrar. UI: `Estructto.exe --testes-fluxo <pasta> A,B,C,D,E,F` verde.
- Unidades no contrato: metros e radianos; mm só em chaves com sufixo `_mm`.
- Folga padrão 12 mm; cope: profundidade `tf+12 mm`, comprimento `bf/2+12 mm`, raio ≥ 10 mm — padrões editáveis.
- `testes-aceite/PLATAFORMA.ed3` nunca commitado (adicionar a `.gitignore`).
- Merges só pelo Claude principal, ordem G → K → V → N → C.

## Review Focus
1. Barra curta onde recuo+recorte ≥ comprimento → hoje some silenciosa; esperado: aviso "barra curta demais para a ligação", barra mantida. (G2)
2. Nó com 2 barras colineares + diagonal em ângulo < 20° com a viga → gusset impossível; esperado: motivo exibido, não crash. (G3)
3. Modelo antigo sem `pontas` abre e renderiza igual à v0.4. (G4)
4. Perfil antigo salvo por nome atual ("W 200 x 26,6") continua resolvendo após catálogo novo. (K2)
5. Otimizador em ligação cujo apoio é menor que a chapa mínima → para, nada muda, mensagem cita o limite. (C2)

---

## Frente 0 — Mapa (Haiku, antes de tudo)

### Task 0: Grafo do código
- [ ] Rodar `/graphify estructto/codigo` (AST). Resultado `estructto/codigo/graphify-out/graph.json`; adicionar `graphify-out/` ao `.gitignore`.
- [ ] Todos os briefings citam: "consulte `graphify query` antes de ler arquivos".

---

## Frente G — Pontas e recortes (Opus, worktree `wt-pontas`)

### Task G1: Contrato `pontas`
**Files:** Create `docs/v3/PONTAS-CONTRATO.md`; Create `codigo/motor/prossen_estrutural/aco/pontas.py`; Test `codigo/motor/testes/test_pontas.py`
**Interfaces — Produces:**
- `Ponta = {barra:int, extremo:"i"|"j", plano:{ponto:[x,y,z], normal:[x,y,z]}, folga:float, recortes:list[Recorte]}`
- `Recorte = {tipo:"cope"|"angulo"|"esquadria"|"boca_peixe", ...}`; cope: `{face:"sup"|"inf", altura:float, comprimento:float, raio:float}`
- `pontas_do_no(no, membros, tipo_ligacao, params) -> list[Ponta]`
- [ ] Teste `test_viga_na_mesa_do_pilar`: pilar W310x38,7 vertical, viga W250x25,3 horizontal na mesa; ponto do plano a `d_pilar/2 + 0,012` do eixo; normal = −direção da viga; pilar sem Ponta.
- [ ] Teste `test_viga_na_alma`: plano a `tw/2 + 0,012`; se mesas da viga colidem com as do pilar → recorte `cope` com `altura = tf_pilar + 0,012`, `comprimento = bf_pilar/2 + 0,012`, `raio = 0,010`.
- [ ] Teste `test_diagonal_no_gusset`: diagonal L 2"x1/4" a 45°; plano normal ao eixo da diagonal, recuo até a borda do gusset + 0,012; sem limite 0,3·L.
- [ ] Teste `test_esquadria_tubos`: dois tubos 100x100x3 a 90° sem contínuo → `esquadria` pela bissetriz.
- [ ] Teste `test_boca_peixe`: tubo Ø50,8 em tubo Ø101,6 → `boca_peixe` com raio do tubo receptor.
- [ ] Teste `test_barra_curta`: recuo > comprimento → `ValueError("barra curta demais para a ligação")` capturado como aviso pelo chamador.
- [ ] Implementar; rodar `python -m unittest testes.test_pontas`; commit `feat(motor): contrato de pontas e recortes`.

### Task G2: Ligações preenchem `geometria.pontas`
**Files:** Modify `ligacoes_flexiveis.py`, `ligacoes_rigidas.py`, `ligacoes_contraventamento.py`, `placa_base.py`, `ligacoes_modelo.py` (`dimensionar_grupo`); Test `testes/test_pontas_ligacoes.py`
**Consumes:** `pontas_do_no`. **Produces:** todo resultado de `verificar` tem `geometria["pontas"]`.
- [ ] Teste parametrizado sobre os 19 tipos em `TIPOS`: `geometria["pontas"]` não vazio exceto `placa_base` (pilar: ponta na face superior da placa, recorte nenhum).
- [ ] Teste `test_calculo_usa_geometria`: chapa de topo com viga recuada — comprimento de solda usado na verificação = largura real da ponta (não do eixo).
- [ ] Teste Review Focus 1 (barra curta → aviso, ligação com `ok=False`, motivo).
- [ ] Implementar; suíte completa do motor verde; commit.

### Task G3: Reconhecimento de nós inclinados e base
**Files:** Modify `ligacoes_modelo.py:57-165`; Test `testes/test_ligacoes_modelo.py`
**Produces:** `identificar(modelo) -> list[Grupo]`; nó sem tipo traz `motivo:str`.
- [ ] Testes: mão-francesa (diagonal entre pilar e viga) → `gusset_contraventamento`; banzo inclinado de treliça (cobertura 10°) → `no_trelica`; viga inclinada (rampa) não vira diagonal quando `papel="viga"`; pilar inclinado com apoio → `base`.
- [ ] Teste Review Focus 2 (diagonal < 20°) → `motivo` contém "ângulo".
- [ ] Teste `test_plataforma_todos_nos`: importar `testes-aceite/PLATAFORMA.ed3` (pular se ausente) → todo nó tem tipo ou `motivo`.
- [ ] Implementar; commit.

### Task G4: WPF aplica pontas
**Files:** Modify `codigo/Estructto/Render3DGeometria.cs:150-395`, `Render3DLigacoes.cs`, exportadores DWG/STEP (os que chamam `Juntas`)
- [ ] `Juntas()` usa `pontas` do motor quando existem; senão fallback atual (Review Focus 3).
- [ ] Recortes `angulo`, `esquadria`, `boca_peixe` como cortes de sólido; `cope` existente reaproveitado; tubos suportados.
- [ ] Auto-teste em `Render3DGeometria.cs:397+`: `--testes-interferencia <arquivo>` → reporta pares sólido-sólido com interseção (AABB + teste de malha); PLATAFORMA e galpão do assistente = 0.
- [ ] Build, `--testes-fluxo` verde, capturas do Realista nos 4 pontos das imagens do usuário; commit; doc em `docs/v3/RENDER.md`.

---

## Frente K — Catálogo (Sonnet + Haiku para extrair tabelas, worktree `wt-catalogo`)

### Task K1: Dados
**Files:** Create `codigo/motor/prossen_estrutural/dados/perfis/<familia>.csv` (uma por família), Modify `dados/FONTES.md`
- Colunas comuns: `fabricante_norma;familia;serie;nome;forma;massa;h;b;tw;tf;r;A;Ix;Iy;fonte` (mm, cm², cm⁴, kg/m).
- Famílias: W/HP (Gerdau), L iguais, L desiguais, U laminado, T laminado, barra chata, IPE, HEA, HEB, UPN (Orange Book ArcelorMittal), tubo quadrado, retangular, redondo (Vallourec + metalon), U e Ue formados a frio (NBR 6355).
- [ ] Haiku extrai cada tabela da URL da pesquisa (salva só CSV, nunca o PDF).
- [ ] Teste `test_catalogo.py::test_massa_vs_area`: |massa − A·7850| ≤ 4 % para todo perfil; `test_sem_duplicados`; `test_contagens_minimas` (W/HP ≥ 88, UPN ≥ 20, IPE ≥ 18).
- [ ] Commit.

### Task K2: Motor e API
**Files:** Modify `secoes.py` (formas T, L desigual, tubo redondo, chata), loader do catálogo, rota `/api/catalogo` (árvore)
**Produces:** `GET /api/catalogo -> [{nivel1, familias:[{nome, series:[{nome, perfis:[nome]}]}]}]`; `perfil(nome) -> Secao` aceita aliases antigos.
- [ ] Testes de propriedades vs tabela (Ix calculado ≈ tabela ±3 %) por forma nova; Review Focus 4 (alias).
- [ ] Commit.

### Task K3: Seletor WPF
**Files:** Modify `codigo/Estructto/JanelaCatalogo.cs` e seletor de seção da Vista3D
- [ ] Árvore nível1 → família → série → perfil, campo de busca; captura nova em `Capturas.cs`.
- [ ] Build + capturas; commit.

---

## Frente V — Vista 3D (Sonnet, worktree `wt-vista`)

### Task V1: Setas e nós com profundidade
**Files:** Modify `Vista3D.cs:479-531,795`, `Render3D.cs`
- [ ] Setas viram geometria no `Viewport3DX` (DX) com depth test; no nativo, mesmas setas no mesmo `Viewport3D` da cena.
- [ ] Opção "Cargas sempre visíveis" (padrão ligada só no modo Analítico).
- [ ] Captura: viga na frente de seta no Realista → seta oculta. Commit.

### Task V2: Sombra
**Files:** Modify `Vista3D.cs:653-790`, `Render3D.cs:115-121`
- [ ] DX: chão recebe `ShadowMap3D`, remover malha de sombra nativa duplicada; nativo: desligar sombra projetada (contato suave só).
- [ ] Opção "Sombra" em Opções. Captura antes/depois. Commit.

---

## Frente N — Arquivo novo (Sonnet, worktree `wt-novo`)

### Task N1
**Files:** Modify `Motor.cs:102-139`, `Inicio.cs:247-300`, `JanelaNovoProjeto.cs:263-290`
- [ ] Reproduzir: medir tempo de `Acoes.Novo` com motor frio/quente e grade 20×20; registrar em `docs/v3/NOVO-ARQUIVO.md`.
- [ ] Motor iniciado em segundo plano ao abrir o programa; `/api/modelo/lote` numa chamada única; prévia com debounce e teto de grade (200 barras na prévia).
- [ ] Auto-teste `--testes-novo`: 10 criações seguidas, cada < 2 s com motor quente, sem exceção. Commit.

---

## Frente C — Cálculo, otimizador, editor, parafusos (Sonnet; verificador Opus; worktree `wt-calculo`, rebase após merge de G)

### Task C1: Limites
**Files:** Create `docs/v3/LIGACOES-LIMITES.md`; Create `codigo/motor/prossen_estrutural/aco/ligacoes_limites.py`
**Produces:** `limites(tipo, membros) -> dict[param, (min, max, passos)]` — chapas comerciais 4,75–50 mm; largura chapa de topo ≤ bf do apoio; gusset limitado pela folga às mesas; placa base ≥ pilar + 2·borda; bordas/espaçamentos NBR 8800 Tab. 14 e 6.3.9.
- [ ] Testes por regra com valores da norma. Commit.

### Task C2: Otimizador limitado
**Files:** Modify `ligacoes.py:707-850`
**Consumes:** `limites`. **Produces:** `otimizar(...) -> {ok, params, motivo?, verificacao_limitante?, limite_atingido?}`
- [ ] Busca só dentro de `limites`, ordem espessura → diâmetro → nº parafusos → chapa maior; disposições construtivas viram restrição (não só reprovação); "geometria impossível" vira candidato descartado.
- [ ] Testes: Review Focus 5; chapa de topo W250 em W310 aprova com t ∈ comerciais; nenhum resultado fora de `limites` em 200 casos aleatórios do catálogo.
- [ ] Verificador-calculos (Opus) confere 3 exemplos dos livros (`docs/v3/LIGACOES-CATALOGO.md`). Commit.

### Task C3: Editor por componente + clique
**Files:** Modify `JanelaLigacaoProjeto.cs:545-616`, `LigacaoDesenho.cs:265-279`, `Render3D.cs:339,349`
**Produces:** cada peça desenhada carrega `Tag = "<secao>.<campo>"`.
- [ ] Seções: Chapa, Parafusos, Soldas, Enrijecedores, Recortes, Folgas (expansíveis).
- [ ] Clique na peça (desenho da ligação e Vista3D principal) → abre seção, rola, destaca campo 1,5 s.
- [ ] Sem solução do otimizador → faixa vermelha com `motivo` e botão para o campo limitante.
- [ ] Capturas + `--testes-fluxo`. Commit.

### Task C4: Parafusos
**Files:** Modify `Render3DGeometria.cs:77-97,266-278`, `Render3D.cs:~350`
- [ ] Base ortonormal a partir do eixo do furo (trata antiparalelo); hexágono alinhado a uma aresta da chapa; cabeça do lado da chapa de origem; conferir mm→m.
- [ ] Auto-teste: todo parafuso da PLATAFORMA tem eixo ∥ normal da chapa (±1°) e fuste atravessa todas as chapas da pega. Commit.

---

## Entrega (Claude principal)
- [ ] Merges G → K → V → N → C em `unificacao`; após cada merge: suíte motor + build + `--testes-fluxo` + `--testes-interferencia PLATAFORMA.ed3`.
- [ ] Revisor-codigo (Sonnet) no diff total; achados voltam ao dono da frente.
- [ ] CHANGELOG, `docs/v3/STATUS.md`, publicar exe local (sem push).
