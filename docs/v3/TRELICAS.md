# Treliças, contraventamentos e terças (documento espacial)

Data: 20/10/2026. Branch `wt-trelicas`. Motor: `codigo/motor/prossen_estrutural/geradores_extra.py` (testes
`testes/test_geradores_extra.py`). Interface: `codigo/Estructto/JanelaTrelicas.cs`. Capturas `50-*` (só elas:
`ESTRUCTTO_CAPTURAS=trelicas`).

## Onde entra

- **Motor**: mesmo `gerador.gerar_estrutura(modelo, dados)` → `projeto.gerar_estrutura` → `POST /api/gerar-estrutura` → MCP
  `estrutura_gerar`. Tipos novos: `trelica_plana`, `trelicas_serie`, `contraventamento`, `tercas` (`gerador.py` despacha para
  `geradores_extra.py`). Tudo ou nada: valida tudo e só depois muta o modelo; erros juntos em `ErroGerador`/`ErroEntrada`.
- **Prévia**: `POST /api/gerar-estrutura/previa` (`?vazio=1` = num modelo vazio). Gera numa cópia do modelo (`projeto.previa_estrutura`) e
  devolve só nós/barras novos e as barras que seriam divididas. Não está em `MUTAM`: não mexe no modelo nem no desfazer.
- **WPF**: Modelo › Gerar › **Treliças e contraventamentos** e, no Ctrl+K, "Treliça plana", "Tesouras em série", "Contraventamento",
  "Terças e tirantes". Gancho único: `MontarTrelicas()` em `MainWindow.xaml.cs` (depois de `MontarDesenho3D`).
- **Um Ctrl+Z**: Inserir = um `POST /api/gerar-estrutura` dentro de `DocumentoEspacial.Executar` (rota em `MUTAM`, uma entrada na pilha).
  Sem documento espacial aberto, Inserir cria um modelo espacial novo com a peça.

## Tipos (campos de `dados`)

| Tipo | Campos | Gera |
|---|---|---|
| `trelica_plana` | `modelo` pratt, howe, warren, warren_v, k, fink; `banzo` paralelo, uma_agua, duas_aguas, arco; `vao`, `altura` (apoios/esquerda; 0 = tesoura triangular), `altura2` (cumeeira/direita/meio do arco), `paineis`; `secoes` {banzo ou banzo_superior + banzo_inferior, diagonal, montante?}; `rotulas` diagonais (padrão) / todas / nenhuma; `apoios` [esq, dir] rotula / movel / engaste / livre; `origem` + `eixo` x/y **ou** `entre` [nóA, nóB]; `travar_plano`; `prefixo` | nós `<p>I<i>` (banzo inferior), `<p>S<i>` (superior), `<p>K<i>` (meio do montante no K); barras `<p>BI<k>`, `<p>BS<k>`, `<p>D<k>`, `<p>M<k>` |
| `trelicas_serie` | o mesmo (sem `entre`) + `n`, `espacamento`, `secoes.terca` opcional | `n` treliças com prefixo `<p><j>-` deslocadas na horizontal perpendicular ao vão; terças `<p>T<k>` rotuladas ligando os nós do banzo superior |
| `contraventamento` | `modelo` x, diagonal, k, v, lambda, mao_francesa; `painel` [n1, n2, n3, n4] (n1-n2 embaixo, n4 sobre n1) **ou** `barras` [bA, bB]; `divisoes` (x, diagonal); `distancia` (m, mão-francesa, padrão 1); `secoes` {contraventamento, escora?} | diagonais `<p>D<k>` (todas `trelica`); escoras `<p>E<k>`/`<p>EI<k>`; nós `<p>N<k>` |
| `tercas` | `barras` (tesouras/vigas da água, 2 ou mais, qualquer ordem: ordenadas pela posição ao longo do prédio), `espacamento` (máx., m), `tirantes` (linhas por vão), `secoes` {terca, tirante?} | nós `<p><k>-<j>` nas tesouras, terças `<p>T<k>` (rótula nas tesouras, contínuas nos tirantes, alma normal à água), tirantes `<p>R<k>` (`trelica`) |

Prefixo padrão: `TR<k>-`, `TS<k>-`, `CV<k>-`, `TC<k>-` com o menor k livre (inserir de novo não colide).

### Regras de geometria

- Pratt: diagonais descem para o meio (S<sub>i</sub>–I<sub>i+1</sub> na metade esquerda); Howe: o contrário; Warren-V alterna; painel do meio
  (n ímpar) segue a metade esquerda. K: as duas diagonais saem do meio do montante interno para o topo e a base do montante de fora.
- Warren sem montantes: nós do banzo superior no meio dos painéis. Fink: W clássico (banzo inferior em 3, superior em 4), só em
  duas águas com altura 0 nos apoios.
- Banzo `uma_agua` = linear de `altura` a `altura2`; `duas_aguas` = triângulo com `altura2` no meio; `arco` = parábola com `altura2` no meio.
- Altura ≤ 1 mm num nó do banzo superior: ele é o nó do banzo inferior (sem montante nulo; diagonal que repetiria o banzo some).
- `entre`: o banzo inferior liga dois nós existentes na mesma cota (vão = distância em planta, direção qualquer); apoios padrão
  livres (a treliça apoia nos pilares); os nós existentes não são alterados.
- Contraventamento por `barras`: as pontas das duas barras formam o painel (pareadas pela menor distância; de baixo para cima).
  Pilares de dois pórticos = parede; duas tesouras = cobertura (com `divisoes` e escoras entre os painéis).
  V: pontas de cima até o meio da aresta de baixo; Λ: de baixo até o meio da de cima; K: cantos esquerdos até o meio da aresta direita;
  mão-francesa: nos cantos de cima, de um ponto do pilar a `distancia` do canto a um ponto da viga à mesma distância.
- Ponto novo no meio de uma barra divide a barra: mesma seção, material, giro e params; `rotula_i` só no 1º trecho, `rotula_j` só no
  último; `Lb` sai (o trecho é outro); cargas distribuídas reinterpoladas (trapezoidal), pontuais no trecho onde caem. Ids `<barra>.<k>`.
  Ponto sem barra embaixo (ex.: beiral sem terça): escoras entre os nós da aresta. Mão-francesa exige pilar e viga.
- Ponto a 1 mm ou menos de um nó usa o nó; barra que repetiria outra (mesmos nós) não é criada.

### Rótulas, apoios e estabilidade

- O motor não tem barra só de tração (conferido em `modelo.py`/`solver.py`: só `trelica`, `rotula_i/j`). Por isso todas as diagonais de
  contraventamento e os tirantes são `trelica` (só axial); no X as duas diagonais trabalham também à compressão (aviso no resultado).
- Treliça: `rotulas: diagonais` = banzos contínuos, diagonais e montantes só axiais (padrão); `todas` = treliça ideal; `nenhuma` = nós rígidos.
- Treliça isolada (sem `entre`, em X ou Y): `travar_plano` padrão true — nós novos com o deslocamento fora do plano e os giros fora do
  plano travados (como o `portico_plano`), senão o banzo superior seria mecanismo sem terças. Use `false` quando houver travamento.
- Apoios (em X): rótula = ux, uy, uz, rx, rz presos (ry livre); móvel = rótula com ux livre; engaste; livre.

## Janela (JanelaTrelicas)

- Esquerda: cartões por família (Treliças planas, Em série, Contraventamento, Cobertura) com miniatura: treliças pela prévia do motor num
  modelo vazio (a mesma geometria do cálculo), contraventamentos e terças por esquema fixo.
- Centro: prévia ao vivo (180 ms depois de digitar) sobre o modelo atual em cinza; peças contínuas em acento, só axiais tracejadas.
  Elevação (plano da treliça) ou isométrica; automático = elevação para treliça sem modelo aberto.
- Direita: parâmetros do cartão (valores guardados ao trocar de cartão). Seções: catálogo do motor, `Tubo d x t` (mm), `Ø d` (mm),
  `Ret b x h` (cm). Material vazio = primeiro do modelo (A-572 GR.50 num modelo novo).
- **Usar seleção**: `vista3D.Selecao` (seleção múltipla) ou, sem ela, o nó/barra escolhido. Treliça: 1 nó = origem, 2 nós = `entre`.
  Contraventamento: 2 barras ou 4 nós (preenche sozinho ao abrir o cartão). Terças: as tesouras selecionadas.
- Janela não modal (seleciona na vista com ela aberta). Rodapé: contagem da prévia e avisos do motor; Inserir desabilitado sem prévia válida.

## Provas

- `python -m unittest discover -s testes` (com `PROSSEN_PULAR_LENTOS=1`): 896 OK. `test_geradores_extra.py` (18): contagens de cada
  modelo, tesoura triangular, banzos (arco/uma água), Pratt × Howe, rótulas e apoios, `entre`, **Pratt de 4 painéis pelo método dos nós**
  (D1 = 15√2 kN tração, BS2 = −20 kN, BI2 = 15 kN, BI1 = 0; banzos contínuos a menos de 2 %), todos os tipos × banzos resolvem,
  série com terças (alma normal à água), X/K/Λ/mão-francesa/cobertura com divisões (não é mecanismo), terças e tirantes com cargas
  conservadas, prévia não muda o modelo. `test_web.py`: prévia + inserir + um desfazer.
- Capturas `50-trelicas-*`: galeria (Pratt e K), Pratt inserida num modelo novo, X entre os pilares de dois pórticos do galpão
  (+2 barras; Ctrl+Z tira as duas, Ctrl+Y volta), cobertura de 5 tesouras Howe com terças calculada (Realista, aproveitamento e N).

## Limites (ponytail)

- Treliça espacial multiplanar (seção triangular) e pórtico de galpão com mísulas: não feitos (galpão já existe no gerador; mísula pede
  seção variável).
- Fink só no W de 4 painéis. Sem barra só de tração (ver acima).
- A ordem das tesouras em `tercas` vem da posição; as águas precisam ser escolhidas uma de cada vez.
- Inserir a mesma treliça no mesmo lugar não cria nada (nós e barras já existem; aviso "nenhuma barra criada").
