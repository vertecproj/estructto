# Integração do `main` do Bruno no produto 3D (21/10/2026)

O `origin/main` recebeu 6 commits do Bruno (f2c443d..1f719d3), todos no programa 2D antigo. O produto agora é só 3D, com o
motor Python (`codigo/motor`). Este documento diz onde cada recurso do Bruno vive agora. Branch `wt-bruno`, juntado com
`origin/main` sem perder nada.

## Commit por commit

| Commit do Bruno | O que era (2D) | Onde vive agora |
|---|---|---|
| 9afea06 Placa: Trocar chumbador por família/diâmetro | Painel **Trocar** agrupado por família → diâmetro → hef; − / + do diâmetro e do hef no cartão; texto da origem do coeficiente e "usar o recomendado" (`JanelaPlaca.cs` 2D) | **JanelaPlaca 3D** (`codigo/Estructto/JanelaPlaca.cs`, motor NBR): `PreencherOverlayTrocar` agrupado por família (a família em uso aberta, as outras com "N passam · menor: …"), chip "menor que passa"; linha **Diâmetro** com − / + na mesma família (hef mais próximo) e − / + do hef; texto do coeficiente do catálogo com a origem, alerta quando o n digitado fica abaixo da Walsywa e link "usar o recomendado". Captura `20-placa-n1-trocar.png`, `20-placa-n1-calculo.png`. |
| 0cefecc Placa: n da solda = 0,577·FEXX/τ ≥ 1,92 | Linha "Segurança" no passo Solda | **Motor**: `aco/placa_base.py` `seguranca_solda` (campo `seguranca` {n, n_min, passa, criterio, informativo} em todo resultado de solda; teste `testes/test_placa_passos.py`). **JanelaPlaca 3D**: linha "Segurança" ao lado da verificação de tensão (informativa: é a mesma condição de τ ≤ 0,30·FEXX). |
| be0a9e2 Placa: cada item na sua pior combinação | `PlacaPior`/`EnvoltoriaPlaca` (Acoes.cs 2D) e `PiorCombinacao` (JanelaPlaca 2D) | **Motor**: `projeto._placa_pior` já escolhia a pior combinação de cada passo; agora cada passo (`passos.fixacao/chapa/solda`) traz também as **verificações dele nessa combinação** (`verificacoes`, e `solda` no passo solda), não as do governante da placa. **JanelaPlaca 3D**: `ResNbr.Verificacoes(passo)` (`PlacaMotor.cs`) lê essas listas; os títulos "Cálculo (motor, pior da fixação/chapa: …)" mostram a combinação de cada item. Teste `test_placa_passos.py`. |
| 5d7133b Vento NBR 6123:2023 nas barras | `VentoNbr` (Nucleo.cs) + janela `Vento.cs` + casos `Vento → (0°)`/`Vento ← (180°)` + gerador sem os dois ventos juntos | **Motor**: `acoes/vento_barras.py` (`config.vento.tipo = "barras"`): w = Cf·q(z)·K·c·sen²α em cada barra, nos sentidos ±X/±Y do 3D (casos `V+X`…), carga distribuída global normal à barra; `projeto.gerar_acoes`. As direções de vento já eram exclusivas no gerador NBR 8681 do motor (grupo `vento`). **UI 3D**: `JanelaVento.cs`, opção **"6123 · por barra"** (V0, S1, S3, categoria, classe automática, K, direções; prévia barra a barra com S2, q, Cf, c, K, sen²α, w e a fonte da tabela). Testes `testes/test_vento_barras_8400.py` (os valores do bloco "vento NBR 6123:2023" do `Testes.cs`). Captura `29-acoes-vento-barras.png`. |
| 1f719d3 Vento NBR 8400:1984 | `Vento8400` (Nucleo.cs), seletor de norma na janela, `Analise.GerarNbr8400` (casos I/II/III) | **Motor**: `acoes/nbr8400.py`: vento (`config.vento.tipo = "nbr8400"`, casos `VS±X/±Y` serviço e `VM±X/±Y` máximo) e combinações (`config.combinacoes = "nbr8400"`, `config.nbr8400 = {grupo, psi}`). **UI 3D**: `JanelaVento.cs` opção **"8400 · equipamentos"** (vw, grupo/Mx, ψ, "usar as combinações da NBR 8400"); `JanelaCombinacoes.cs` com seletor **NBR 8681 / NBR 8400** e o detalhe Mx, ν, ν/γa1. Testes em `test_vento_barras_8400.py` (bloco "vento NBR 8400:1984" do `Testes.cs` + casos I/II/III + cálculo completo). Capturas `29-acoes-vento-8400.png`, `33-acoes-combinacoes-8400.png`. |
| 0decc63 DELEGAR-AGY | situação do agy no PC do Bruno | `docs/DELEGAR-AGY.md` (mantido). |

Mantidos como o Bruno deixou: `codigo/portico/src/Nucleo.cs` (`VentoNbr`, `Vento8400`, `ParamVento`, `Modelo.Vento`) e `Testes.cs`
(`codigo\portico\compilar.ps1 -Testes`: **241 passam**; o CLAUDE.md dele dizia 225, o CHANGELOG já dizia 241), `Analise.cs`
(`Gerar`, `GerarNbr8400`, ventos alternativos no `GerarNbr`), `RelatorioMemorial.cs` (bloco do vento no memorial legado, para
`.estructto` antigo com `Modelo.Vento`), ícone `vento`, `docs/VENTO.md`, `docs/CHANGELOG.md`.

## Conflitos e decisões

- `Acoes.cs`, `Capturas.cs`, `Casos.cs`, `JanelaPlaca.cs`, `Paleta.cs`: ficou a versão só-3D; os recursos do Bruno foram ligados
  pelo código portado (tabela acima). O botão/comando `VENTO` do 2D não volta: no 3D o vento está em Ações › Vento.
- `codigo/Estructto/Vento.cs` (janela 2D do Bruno) **fica no repositório, fora do build** (`<Compile Remove="Vento.cs" />` no
  `Estructto.csproj`): a classe `JanelaVento` dele colide com a janela 3D e o fluxo dele mexe direto no `Modelo` 2D. As constantes
  dos nomes de caso (`CasoVentoDir` …) foram para `RelatorioMemorial.cs`, que ainda as usa.

## Critérios novos (não estavam no 2D)

1. **3D**: o vento é horizontal em ±X/±Y. O vetor da carga é Cf·q·K·c·senα·d⊥ (d⊥ = componente do vento normal à barra): no
   plano do pórtico é a carga perpendicular do 2D, com o mesmo módulo w = Cf·q·K·c·sen²α.
2. **Orientação da seção**: d⊥ projetado nos eixos locais; se cai mais no eixo z local (o da altura h), vale o "vento normal às
   mesas" (perfil em pé no 2D), senão o "paralelo às mesas" (deitado). Vento em Y numa barra do plano XZ pega o eixo fraco.
3. **Classe automática** (5.3.2) por direção: altura × largura perpendicular ao vento (ly para X, lx para Y) da caixa do modelo.
4. **NBR 8400 no motor de estados limites**: cada combinação ELU é multiplicada por ν/γa1 (ν = 1,5 / 1,33 / 1,1 nos casos I/II/III,
   γa1 = 1,10). Sem isso as cargas sem γ da 8400 iriam contra resistências de cálculo (≈ 36% contra a segurança no caso I).
   Resolve a pendência do VENTO.md ("tensões admissíveis por caso não são distinguidas"). Exato no escoamento, aproximado na
   flambagem. ELS: `ELS_RARA` (G + Q e G + Q + vento de serviço); as flechas sem a combinação pedida pelo limite caem na rara.
5. Os ventos de serviço (`vento`) e máximo (`vento_maximo`) da 8400 são do mesmo grupo de exclusão também na NBR 8681.
6. Barra paralela ao vento não recebe carga (não cria carga nula).

## API e MCP

- `PATCH /api/config` / `config_definir`: `vento.tipo` ∈ {`barras`, `nbr8400`} (+ `usar_K`, `v_servico`), `combinacoes`
  ∈ {`nbr8681`, `nbr8400`}, `nbr8400` {grupo, psi}. Descrição da ferramenta MCP atualizada.
- `POST /api/acoes/gerar` / `acoes_gerar`: `forcas[caso]` traz uma linha por barra (z, S2, q ou Pa, Cf/C, c, K, sen²α, w_kN_m,
  w_vet_kN_m, forma, fonte).
- `GET /api/combinacoes` / `combinacoes_listar`: com NBR 8400, campo `nbr8400` {caso_8400, Mx, psi, nu, conversao}.
- `normas_implementadas`: módulos `acoes.vento_barras` e `acoes.nbr8400` com itens e limitações.
- `POST /api/placa_base/verificar`: `passos[p].verificacoes` (e `passos.solda.solda.seguranca`).

## Provas (21/10/2026)

`dotnet build -c Release` 0 erros · `compilar.ps1 -Testes` 241 passam · motor Python todos passam (`PROSSEN_PULAR_LENTOS=1`) ·
`--testes-edicao`, `--testes-snap`, `--testes-exportar` passam · capturas completas com `ESTRUCTTO_MOTOR` = `codigo\motor` do
worktree sem `erros.txt` (amostras novas em `codigo/Estructto/amostras/vento-barras.json`, `vento-8400.json`, `combinacoes-8400.json`,
geradas de um guincho 3D real pelo motor).
