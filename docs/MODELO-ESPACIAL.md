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

## Níveis e planos de trabalho

Código: `codigo/Estructto/PlanosTrabalho.cs` (regras puras em `Niveis`, faixa e adaptador em `MainWindow`, painel `JanelaNiveis`).
Ganchos: `DocumentoEspacial.Executar` (operação composta na fila do motor, conta no desfazer), `FiltrarPlano` no
`AtualizarEspacial3D`, `LimparPlano` ao sair do modo espacial, grupo inserido na aba Modelo, linhas na ajuda F1.
Capturas `43-*` (`Capturas.CapturasPlanos`; só elas: `ESTRUCTTO_CAPTURAS=planos`), que também conferem as contagens.

| Comando | O que faz |
|---|---|
| Modelo › Planos › **Nível ▾** | lista os pavimentos do motor + "Térreo z=0" (se nenhum estiver na cota 0). Ativar = plano de trabalho na cota, só as barras do andar (os dois nós entre o nível de baixo e ele, sem as deitadas na cota de baixo), as outras em fantasma (linhas claras com fade), câmera em planta (transição de 300 ms da Vista3D) |
| **Elevação X ▾ / Y ▾** | cotas distintas dos nós; só as barras do plano vertical, vista Lado (YZ) / Frente (XZ) |
| **3D completo** | tira o filtro, volta à isométrica |
| **Níveis** (painel) | nome, cota, altura até o próximo; criar (+3 m), renomear (= DELETE + POST: o motor não muda o nome), mudar z (PATCH), apagar, **Detectar pela geometria** (um nível por cota de nós sem nível). Tudo em `/api/modelo/pavimento` dentro do `Motor.Exclusivo` |
| **Copiar pavimento para cima** | barras do andar do nível escolhido, nós novos (reaproveita nó que já está na posição) e cargas (nodais dos nós de cima do andar; distribuídas e pontuais das barras) para N andares de altura h. A cota de cada nó é mapeada em escala: o pé vai para o topo da cópia anterior. Cria pavimento em cada cota nova. **Um** `POST /api/modelo/lote`: um Ctrl+Z desfaz tudo |
| **Gerar pilares entre níveis** | um pilar sob cada nó do nível escolhido até o nível de baixo (nó novo embaixo se faltar; pula se já houver barra ligando os dois). Seção e material do primeiro pilar do modelo |

Plano de trabalho = interface `IPlanoAlvo { DefinirPlano(eixo, valor); Isolar(zMin, zMax); }`. Nesta branch o adaptador
`PlanoNaVista` (comentário `ponytail:`) filtra o `Modelo3D` antes do `Vista3D.Carregar` e desenha os fantasmas num `ModelVisual3D`
próprio (a Vista3D ignora no picking os modelos que não são dela). Depois do merge com o `Vista3D.PlanoAtual`, ligar o adaptador nele.

Limites: em planta os fantasmas ficam embaixo das barras isoladas (aparecem na isométrica); renomear nível = dois passos no desfazer;
apoios não são copiados (as cópias ficam sobre o andar de baixo).
## Cargas e casos (`CargasEspacial.cs`)

- **Barra › Carga distribuída**: global (kN/m; o motor só tem direção global), valor em ni e em nj. Iguais = uniforme (sem `q2`);
  diferentes = trapezoidal (`q` em ni, `q2` em nj). Cada carga existente tem os campos editáveis + Gravar (`PATCH`, `q2: null` volta
  a uniforme) e Apagar.
- **Barra › Cargas pontuais**: `cargas_pontuais` (a desde ni em m, Fx, Fy, Fz global em kN); adicionar, gravar (`PATCH`), apagar.
- **Modelo (sem escolha) › Casos e ações**: Casos de carga…, Pavimentos…, Vento…, Sismo… Os mesmos botões da faixa (Ações) e o
  comando `CASOS` também vão para o modelo espacial quando o documento ativo é espacial (`FaixaMotor.cs`: `ComMotor`/`ComMotorExtra`
  desviam para `JanelaMotorEsp`; antes mandavam o Modelo 2D vazio ao motor).
- `DocumentoEspacial.ComJanela`: dentro de **um** `Motor.Exclusivo` recoloca o modelo (Garantir), abre a janela modal (ela chama o
  motor direto), relê `GET /api/modelo` e, se mudou, `Recarregar` (o cálculo anterior é descartado) e marca o documento como alterado.
  Vento, Sismo e Pavimentos não mudaram: leem o modelo do servidor, então mostram o modelo espacial (capturas `48-espacial-*`).
- Combinações (faixa, sem cálculo) também abre sobre o modelo espacial. Janelas que precisam de cálculo (Estabilidade, Verificar
  barra pela faixa) continuam pela ponte 2D: no documento espacial use F5 e os painéis do espacial.

### Carga em área (regra e limites)

Painel do modelo › Carga em área: nível z (cotas com barras horizontais), "Recebem" = vigas em X ou em Y, p (kN/m²), caso.
Uma chamada `POST /api/modelo/lote` com uma carga distribuída uniforme `(0, 0, -q)` por viga.

- Regra de uma direção: só as barras horizontais no nível (|z − z0| ≤ 1 mm), paralelas à direção escolhida, recebem.
  q = p × (d₁/2 + d₂/2), d₁ e d₂ = distância em planta à viga paralela mais próxima de cada lado entre as que se sobrepõem
  ao longo do eixo. Numa grade regular, Σ q·L = p × área da grade (conferido na captura).
- Limites (`ponytail:` em `CargaArea`): viga de borda leva só o lado interno (sem balanço de laje); sobreposição parcial conta como
  inteira; não sabe onde há laje (vão sem laje também carrega); as vigas da outra direção não recebem nada (laje armada em uma
  direção); O(n²) por nível. Laje em duas direções (charneiras/áreas de influência) = trocar `CargaArea`.

### Desenho das cargas na Vista3D (falta; `Vista3D.cs` é de outro agente)

A Vista3D não desenha cargas. Precisa: em `Modelo3D`, listas de cargas por caso (nodal: nó + F; distribuída: barra + q, q2;
pontual: barra + a + F), um filtro de caso (Ações › Caso ativo) e o desenho: seta por força nodal/pontual (comprimento
proporcional, rótulo kN), "pente" de setas ao longo da barra para distribuída (altura linear de q a q2), escala própria e
cor de carga do `TemaPlant`. `DocumentoEspacial.ParaVista` só passaria as listas do JSON.

## Falta

- Desenhar as cargas na Vista3D (ver acima).
- Desenhar max e min juntos na envoltória (hoje uma curva por barra).
- Pilha de desfazer por documento (o servidor guarda uma só).
