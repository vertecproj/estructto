# Modelo espacial (documento 3D do motor)

Data: 20/10/2026. Código: `codigo/Estructto/DocumentoEspacial.cs`. Capturas: `41-*` (`Capturas.cs`, `CapturasEspacial`).

## Decisão

O `Modelo` do WPF (`Nucleo.cs`) continua só 2D. Um segundo tipo de documento, **Modelo espacial**, tem como fonte da verdade o
modelo do motor Python (o JSON de `codigo/motor`, ESPEC §5). O WPF guarda só uma cópia do último `GET /api/modelo`.
Os documentos 2D não mudaram.

## De onde vem

| Origem | Como |
|---|---|
| Gerar 3D (aba Modelo › Gerar, tela inicial) | `JanelaGerar3D` → `POST /api/modelo/novo` + `POST /api/gerar-estrutura` (edifício, galpão, pórtico plano) + `POST /api/modelo/lote` com G e Q uniformes (kN/m) nas barras de papel "viga" |
| Abrir `.json` | arquivo copiado para a pasta de trabalho do motor e `POST /api/modelo/abrir` (o motor valida) |
| Abrir `.ed3` (CYPE 3D) | `POST /api/importar_cype` com `conteudo_base64`; os avisos vão para o histórico |
| Exemplos do motor (Arquivo › Exemplos 3D, tela inicial) | `GET /api/exemplos` e `POST /api/exemplos/{nome}` |

## O que faz

- Vista3D sempre ligada (o botão Vista 3D não volta ao 2D nesse documento). Escolher nó ou barra mostra o painel do motor em Propriedades:
  nó (x, y, z, 6 apoios, deslocamento), barra (seção, material, giro, papel, verificação). Editar = `PATCH /api/modelo/{tipo}/{id}`.
- Desfazer/Refazer (Ctrl+Z/Ctrl+Y) = `POST /api/desfazer` / `POST /api/refazer`.
- Calcular (F5) = `POST /api/calcular`. Taxa de cada barra = pior linha da verificação do motor. Diagramas N, Vz, My e deformada da
  envoltória ELU ou da combinação escolhida (ver abaixo; `GET /api/resultados?alvo=esforcos|deslocamentos&combinacao=...`). O eixo local y da Vista3D é o z
  local do motor (mesma regra de `solver.eixos_locais`), então My < 0 no vão de viga sai para baixo (lado tracionado).
- Salvar = escreve o JSON do motor (cópia do último `GET /api/modelo`, atualizada depois de cada mudança).

## Um modelo por servidor

O servidor guarda um modelo só. `Motor.Dono` diz quem deixou o modelo lá; o documento espacial ativo se recoloca (arquivo
`_espacial.json` na pasta de trabalho + `abrir`) antes de cada operação se outro documento (ou a ponte 2D, que usa `PUT`) trocou.
Consequência: trocar de aba entre dois modelos espaciais perde a pilha de desfazer do outro.

Ao ativar a aba, o documento se recoloca no motor na hora. Se ele tinha histórico, o log avisa que o desfazer recomeçou
(o desfazer vale só para o documento ativo; o painel do modelo diz o mesmo).

## Painéis no documento espacial

| Painel | Mostra |
|---|---|
| Projeto (árvore) | nós (com apoio/carga), barras (seção e taxa), seções, materiais, casos, total de cargas; clique escolhe na vista |
| Tabelas › Nós | X, Y, Z, apoio (gl travados), ux/uy/uz (mm) do resultado mostrado |
| Tabelas › Barras | nós, perfil, material, L, N/Vz/My mín./máx. ao longo da barra |
| Tabelas › Verificação | pior linha da verificação do motor por barra (taxa, critério, norma, item, combinação) |
| Avisos | `GET /api/validar` + avisos da importação .ed3 + barras reprovadas |
| HUD | "Mostrando: envoltória ELU…" ou o nome da combinação; legenda com máx./mín. do diagrama do motor |

## Editar no 3D (painel Propriedades)

- Sem escolha: **Novo nó** (X, Y, Z) → `POST /api/modelo/no`.
- Nó: **Barra até outro nó** (clica no outro nó na vista), **Nó + barra** (nó deslocado dX, dY, dZ + barra), **Apagar nó**
  (`DELETE …?cascata=true`, leva barras e cargas), **cargas nodais** Fx, Fy, Fz no caso escolhido (lista com ×).
- Barra: **Seção** lista as do modelo + o catálogo do motor (`GET /api/catalogo`); perfil fora do modelo entra com
  `POST /api/modelo/secao {perfil}` antes do PATCH. **Apagar barra**, **carga distribuída** qx, qy, qz (global, kN/m) por caso.
- Barra nova copia seção, material da última barra do modelo. Caso das cargas = Ações › Caso ativo (mesmo valor).

## Combinação dos diagramas

Resultados › Mostrar: **Envoltória ELU (motor)** (padrão) ou qualquer combinação do cálculo. Na envoltória, cada ponto do diagrama é o
valor de maior módulo entre max e min (com sinal); a deformada usa a envoltória ELS (padrão de `projeto.resultados`).

## Desenhar no 3D (Modelo › Desenhar 3D)

Código: camada de entrada de `codigo/Estructto/Vista3D.cs` (o host fica no `partial MainWindow` do mesmo arquivo; em
`DocumentoEspacial.cs` só a palavra `partial`). Capturas: `43-desenho3d-*` (`Capturas.CapturasDesenho3D`; só elas:
`ESTRUCTTO_CAPTURAS=desenho3d`), que desenham um pórtico de 2 vãos chamando a API com pontos de tela e conferem nós, barras,
seleção e desfazer.

No documento espacial o grupo **Desenhar 3D** (Selecionar, Barra, Pilar) substitui o Desenhar do 2D; na paleta (Ctrl+K):
"Selecionar 3D", "Barra 3D", "Pilar 3D". **Modelo 3D vazio** (Modelo › Gerar) abre um modelo espacial em branco.

| Ferramenta | Uso |
|---|---|
| Navegar (nenhuma ligada) | clique escolhe (Propriedades), arrastar com o esquerdo orbita |
| Selecionar | clique escolhe; arrastar da esquerda p/ direita = janela (só o que está inteiro dentro), da direita p/ esquerda = cruzamento (o que tocar); Shift soma, Ctrl tira, Esc limpa |
| Barra | cliques em sequência no plano de trabalho; a corrente segue do último ponto; Esc, Enter vazio ou botão direito termina |
| Pilar | clique a base: barra vertical de z até z + altura do pavimento (próximo `pavimentos[].z` acima; no topo, a do último andar; sem pavimentos, 3 m) |

- **Plano de trabalho** (`PlanoTrabalho`): horizontal z = c (padrão z = 0) ou vertical x = c / y = c. A grade do plano
  (passo `PassoGrade`, 1 m) aparece enquanto Barra/Pilar estão ligados; o mouse é projetado no plano pelo raio da câmera.
  Plano de perfil na vista (raio paralelo) avisa para girar a vista.
- **Atração** (como o 2D): nó a 10 px na tela (em qualquer cota) > meio de barra > ortogonal/polar 0/90° nos eixos do plano a partir
  do 1º ponto (F8 liga, Shift força) > ponto da grade a 10 px > plano. Marcador animado (`Anim.Entrar`, 120 ms) a cada troca.
- **Entrada dinâmica**: elástico até o cursor + caixinhas Comprimento/Ângulo (ângulo no plano, do eixo U para o W); digitar
  número trava o comprimento, Tab o ângulo, Enter confirma. Vírgula, `;` ou `@` passam para coordenada: `x,y,z`, `@dx,dy,dz`
  (relativa ao 1º ponto) ou `u,w` (2 valores = no plano).
- **Criação**: cada clique = um `POST /api/modelo/lote` dentro de `Motor.Exclusivo` (nós novos + barra; atômico, um passo de
  `/api/desfazer`). Ponta a até 1 mm de um nó existente reaproveita o nó. Seção e material: os da última barra do modelo (igual
  ao painel); modelo sem seção/material ganha W310x38.7 e A-572 GR.50 no mesmo lote.
- **Esc em cascata**: texto digitado > comprimento/ângulo travado > barra em andamento > ferramenta (volta a Selecionar) > seleção.
  A janela passa Esc/Enter/Tab/Backspace/F8 para a Vista3D antes dos atalhos dela (`Acoes.cs › Tecla`, `Tecla3D`).

### API pública (para PlanosTrabalho.cs, EdicaoEspacial.cs e painéis)

| Membro de `Vista3D` | |
|---|---|
| `PlanoTrabalho PlanoAtual { get; set; }` + `event Action<PlanoTrabalho> PlanoMudou` | plano atual; `PlanoTrabalho.Horizontal(z)`, `EmX(x)`, `EmY(y)`, `new PlanoTrabalho('Z', z, nome)`; `N`, `U`, `W`, `Projetar(p)`, `Ponto(u, w)` |
| `Ferramenta3D Ferramenta { get; set; }` + `event Action FerramentaMudou` | `Navegar`, `Selecionar`, `Barra`, `Pilar`; `MainWindow.UsarFerramenta3D(f)` faz o mesmo pela faixa |
| `Selecao3D Selecao` + `event Action SelecaoMudou` | `Selecao.Nos` / `Selecao.Barras`: ids do motor (`HashSet<string>`) |
| `Selecionar(nos, barras, mods)`, `LimparSelecao()`, `SelecionarCaixa(a, b, mods)` | trocar/somar (Shift)/tirar (Ctrl) por id ou por caixa de tela |
| `Func<IList<(Point3D A, Point3D B)>, Task> CriarBarras` | quem cria as barras (MainWindow → `DocumentoEspacial.DesenharBarras`) |
| `Func<double, double> AlturaAndar`, `double PassoGrade`, `bool Orto` | altura do pilar por z da base, passo da grade, ortogonal |
| `Point3D? NoPlano(Point tela, PlanoTrabalho pl = null)`, `Point Tela(Point3D)` | raio do pixel até o plano; ponto 3D → pixel |
| `ClicarTela(Point, mods)`, `MoverTela(Point)`, `Tecla(Key)`, `Digitar(string)` | entrada sem mouse (testes/capturas); `InicioCorrente`, `PontoCursor`, `TipoAtracao` para conferir |

`DocumentoEspacial.DesenharBarras(segmentos)` (lote, devolve os ids das barras) e `DocumentoEspacial.Vazio(titulo)`.

## Falta

- Cargas pontuais e casos novos pelo painel (o motor aceita; não há tela).
- Desenhar max e min juntos na envoltória (hoje uma curva por barra).
- Pilha de desfazer por documento (o servidor guarda uma só).
- Desenhar 3D: sem atração a interseção/perpendicular/extensão nem rastreamento (o 2D tem); a seção da barra nova é a da
  última barra (não há seletor de seção do motor na faixa); nós da base não recebem apoio automático.
