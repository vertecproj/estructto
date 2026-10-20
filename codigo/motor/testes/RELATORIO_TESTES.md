# Testes — 2026-10-20

## Comandos e resultado
| Comando (a partir de `software/`) | Total | OK | Falhas | Ignorados | Tempo |
|---|---|---|---|---|---|
| `python -m unittest discover -s testes` (suíte completa) | 707 | 706 | 0 | 1 (capturas, só com `PROSSEN_CAPTURAS=1`) | 180 s |
| `python -m unittest discover -s testes/e2e -t testes` (só e2e) | 69 | 68 | 0 | 1 | 85 s |
| `PROSSEN_PULAR_LENTOS=1 python -m unittest discover -s testes/e2e -t testes` | 69 | 64 | 0 | 5 | 53 s |

Antes dos e2e a suíte tinha 549 testes e 65 s; agora 707 (a descoberta enxerga `e2e/`, que tem `__init__.py`). Meta de 60 s para a suíte completa **não atendida** (já passava de 60 s sem os e2e; os e2e somam ~85 s, dos quais ~25 s são do navegador e ~30 s de subprocessos CLI/stdio). Testes lentos levam `@lento` (pulam com `PROSSEN_PULAR_LENTOS=1`).

Arquivos: `e2e/test_e2e_cli.py` (21), `test_e2e_mcp.py` (21: F5 em processo e via stdio com `.mcp.json`, F1 manual, ligação F4, erros, bordas, avaliação), `test_e2e_web.py` (13 de API + 7 de navegador), `test_e2e_paridade.py` (7), `utilitarios.py`, `navegador/*.js` (roteiros do `playwright-cli`), `modelos/*.json` (7 modelos de borda e de erro), `mcp_avaliacao.xml` (10 perguntas, conferidas por teste).

## Cobertura entregue
- CLI: 4 exemplos (código 0, HTML/MD/JSON, aviso de ART, normas com edição, todas as barras, taxas coerentes e ordenadas, sem NaN/inf, flecha da viga = 5(g+0,6q)L⁴/384EI), `--pdelta`, erros de entrada (código 1, mensagem sem traceback), uso errado (código 2).
- MCP (cliente real): 40 ferramentas exatas; F5 completo (gerar edifício → vento → ações → combinações → calcular → verificação → γz → reações → relatórios → salvar) em processo e via stdio; fluxo manual nó a nó; erros (nó/barra/seção/caso inexistente com lista de ids válidos, lote atômico, hipostático com "suspeito: nó X, GL y", só nós, P-Δ acima da crítica, caminhos fora da pasta, unidades absurdas).
- Web: API (CRUD, lote, desfazer/refazer, calcular, verificação, estabilidade, modal, relatório, ligações, salvar/abrir) e erros 400/403/404/405/409/413/422 sempre em JSON.
- Paridade (1e-9): CLI × web × biblioteca no `to_dict` completo dos 4 exemplos; MCP (processo e stdio) × biblioteca/CLI em resumo, verificação linha a linha, γz/B2, reações, flechas, deslocamentos; P-Δ nos 4 canais; relatórios JSON de MCP e web = CLI; edifício F2 feito por MCP, API e navegador = biblioteca = CLI sobre o modelo exportado.
- Valores independentes: reações qL/2 = 30 kN e qL²/8 = 45 kN·m; ΣRz do edifício gerado = 1 300 kN; ΣRz(G1) do pórtico F1 = 10,58 kN e ΣRz(Q1) = 120 kN; balanço (Rx −5, Rz 50, My −15); treliça (−62,5 / +37,5 kN); balanços com `rot` 0 e π/2 (PL³/3EI com I forte e fraco); parafuso A325 M20 Fv,Rd = 76,79 kN (NBR 8800 6.3.3.2).

## Falhas
Nenhuma aberta na rodada final. Achados feitos por esta ficha e **já corrigidos** pelos donos durante a ficha (os testes estavam como `expectedFailure` e foram promovidos a testes normais):
- E2E-001 (integração): V0 = 3500 m/s aceito sem aviso (reprovava as 116 barras). Hoje: `vento.V0: 3500 m/s é irreal... confira a unidade`.
- E2E-002 (integração): seção de 25 m × 50 m e pilar de 3 000 m passavam em `modelo_validar` sem aviso. Hoje há aviso de dimensão.
- E2E-003 (solver/integração): nó a 1e308 m vazava `numpy.linalg.LinAlgError` ("erro interno" no MCP, 500 na web). Hoje dá erro de entrada.

Instabilidade observada (não é defeito de teste): enquanto outros donos editavam os motores, duas rodadas dos e2e tiveram falhas transitórias (CLI com código ≠ 0 em `viga_biapoiada`/`portico_aco` e chave `deslocamentos.sismicos` presente num canal e ausente noutro, por processos que importaram versões diferentes do código). Repetidas sem edição no meio, ficaram verdes. Os testes não fixam valores que mudam com regra de norma (a armadura "4 ϕ 16" virou "6 ϕ 12.5" e o teste compara com a API); comparam sempre com `projeto` direto e invariantes.

## Observações de baixa gravidade (pedidos aos donos; nada bloqueante)
- programador-interface: (1) o campo "Nome base" do relatório volta ao padrão `memoria_memoria` depois de cada gravação (HTML grava com o nome digitado, MD e JSON seguintes não); (2) gerar estrutura sobre modelo não vazio devolve só a lista de ids em colisão, sem dizer "crie um modelo novo antes"; (3) critério de concreto aparece como "fissuracao wk" (sem acento nem formatação, ao contrário de "Flexo-compressão / interação"); (4) a interface batiza a seção "R 25x50" e a API/MCP "R 250x500" (só rótulo; números idênticos); (5) o aviso "Tecle F5 para calcular" continua na tela depois de calcular; (6) rótulos de carga sobrepostos na vista 3D do edifício.
- engenheiro-integracao: (7) `carregar` com referência inexistente (arquivo) diz barra e nó, mas não lista os ids válidos como o MCP faz; (8) a CLI imprime `?` no lugar de γ e acentos quebrados no console cp1252 padrão do Windows (`errors="replace"`); com `PYTHONIOENCODING=utf-8` sai correto. (9) criar o primeiro caso G pela interface não liga o peso próprio (precisa escolher G1 no seletor "Peso próprio"); só há o aviso "caso G1 não tem cargas".

## Navegador (playwright-cli 0.1.22 + Chromium headless, 1440×900, servido em http://127.0.0.1:<porta livre>)
- Roteiros: `carga.js`, `f1_portico.js`, `f2_edificio.js`, `f3_exemplo.js`, `f4_ligacao.js`, `teclado_tema.js`, `capturas.js`. Todos terminam com console sem erros, nenhuma requisição fora de 127.0.0.1 e nenhuma requisição falha; `playwright-cli console` confirma 0 erros.
- F1 pórtico do zero (6 nós por formulário, 5 barras por seleção + B, perfis do catálogo, G1 com peso próprio, Q1 distribuída, F5, My, verificação, relatório): tela = API; ΣRz = 120 e 10,58 kN.
- F2 edifício pelo assistente + vento IV + ações + F5: γz da tela = API (1,01 em X e Y), 116 linhas na tabela = API, filtro "reprovadas" = 0, detalhe do pilar com a armadura da API, HTML/MD/JSON gravados; resultado = biblioteca = CLI sobre o modelo exportado.
- F3 galpão: 39 linhas da tabela iguais à API (barra, taxa, estado, item, combinação); detalhe da barra crítica com itens da NBR 8800.
- F4 chapa simples (V = 120 kN, 3 × M20 A325): requisição e resposta iguais à API; Fv,Rd = 76,8 kN.
- Teclado: Tab inicia no link "Ir para a vista do modelo" com foco visível; teclas 1–6; F5; N, Enter, Ctrl+Z e Ctrl+Y (6 → 7 → 6 → 7 → 6 nós). Tema: segue o sistema e o botão (Vegetal/Ardósia); contraste texto/fundo 14:1 nos dois temas.
- Capturas em `software/docs/imagens/`: `etapa1-claro.png` … `etapa6-claro.png` e `etapa1-escuro.png` … `etapa6-escuro.png` (edifício de concreto calculado). Regravam com `PROSSEN_CAPTURAS=1 python -m unittest testes.e2e.test_e2e_web.TestNavegador.test_capturas_das_seis_etapas_claro_e_escuro`. Também conferido no navegador embutido do app (carga sem erro de console).

## Lacunas de cobertura
- Seleção de barras por clique no canvas 3D (usa-se a árvore) e arrasto/caixa; animação modal; torção e ligações de tração/solda isolada na interface (só chapa simples).
- Só Chromium; Firefox e WebKit não instalados. Sem regressão visual por pixel (as capturas são documentação, não oráculo).
- Impressão/PDF do relatório pela interface.
- Pico de memória e arquivos grandes (corpo de 10 MB real) não medidos; o 413 usa Content-Length declarado.
- Meta de tempo da suíte (60 s) depende de decisão do orquestrador: reduzir testes de outros donos ou rodar `PROSSEN_PULAR_LENTOS=1` no ciclo curto.
