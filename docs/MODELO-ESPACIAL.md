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

## Editar a seleção (edição em lote)

Código: `codigo/Estructto/EdicaoEspacial.cs`. Faixa › Modelo › Modificar: Mover, Copiar, Espelhar e Dividir barra (os botões do 2D
passam para cá no documento espacial) + Rotacionar, Matriz, Unir nós e Propriedades em lote (só aparecem no espacial). Todos também
na paleta (Ctrl+K).

| Operação | Entrada | Faz |
|---|---|---|
| Mover | dx, dy, dz ou ponto base → ponto destino ("x; y; z") | move os nós (e as pontas das barras escolhidas); cargas das barras levadas vão junto |
| Copiar | o mesmo + N cópias | matriz linear: cópia k em k·(dx, dy, dz) |
| Espelhar | plano X = a, Y = b ou Z = c | cópia espelhada (o original fica) |
| Rotacionar | centro (x; y), ângulo (°, anti-horário visto de cima), cópias | eixo vertical; cópias = 0 gira, ≥ 1 cópia polar |
| Matriz | nx, ny, nz (contando a original) + espaçamentos | nx·ny·nz − 1 cópias |
| Dividir barra | N partes, ou distância a partir de ni | nós intermediários; cargas distribuídas reinterpoladas (trapezoidal), pontuais no trecho onde caem; `params` terminados em `_i` só no 1º trecho e `_j` só no último |
| Unir nós | tolerância (mm, padrão 1) | fica o primeiro nó; apoios somam (ou por GL); barras que zeram ou repetem somem com as cargas |
| Propriedades em lote | seção, material, giro (°), apoio (engaste/articulado/livre) | "(manter)"/vazio não muda; trocar para material com fck vira `tipo` concreto (e volta a aço) |

Regras:

- **Uma operação = um desfazer.** A geometria é feita no WPF sobre a cópia do JSON (`GeometriaEdicao`, pura) e vai inteira num
  `PUT /api/modelo` (`DocumentoEspacial.Aplicar`, dentro de `Motor.Exclusivo`). PUT está em `MUTAM`: uma entrada na pilha; Ctrl+Z
  desfaz tudo. Por que não `/api/modelo/lote`: o lote só adiciona; mover, dividir, unir e propriedades precisam mudar/remover.
- Cópias levam seção, material, rot, `params` e cargas (sem id: o motor numera). Forças seguem a transformação; momento nodal é
  pseudovetor (no espelho troca o sinal). O giro `rot` da seção não muda no espelho.
- Nó novo a 1 mm ou menos de um existente funde com ele; barra que repetiria outra (mesmos nós) não é criada. Ids novos: `N<k>`, `B<k>`.
- Seleção: `IFonteSelecao` (nós e barras por id + `Mudou`). Hoje o adaptador `FonteSelecaoEsp` usa a escolha única do documento;
  **nada escolhido = modelo inteiro** (o painel diz). Quando a Vista3D tiver seleção múltipla, só o adaptador muda.
- Painéis: janelas pequenas não modais (`PainelEdicao : JanelaEscura`, `Anim.Abrir`), ficam abertas para repetir; erro de digitação
  aparece no painel, erro do motor na barra de status.
- Seções do "Propriedades em lote" = as do modelo (perfil do catálogo entraria por um POST separado, um segundo desfazer).

Testes: `Estructto.exe --testes-edicao <saida.txt>` (23 asserções: espelho, rotação 90°, matriz 2×3×1, cópia 3×, tolerância 0,5 mm × 2 mm,
fundir ao mover, dividir em 3 e a 1,5 m, unir nós, propriedades). Capturas `48-*`: pórtico plano copiado 3× (e Ctrl+Z/Ctrl+Y conferidos),
espelhado em Y = −3 e rotacionado 30° em torno de (6; 0), depois calculado.

## Combinação dos diagramas

Resultados › Mostrar: **Envoltória ELU (motor)** (padrão) ou qualquer combinação do cálculo. Na envoltória, cada ponto do diagrama é o
valor de maior módulo entre max e min (com sinal); a deformada usa a envoltória ELS (padrão de `projeto.resultados`).

## Falta

- Cargas pontuais e casos novos pelo painel (o motor aceita; não há tela).
- Desenhar max e min juntos na envoltória (hoje uma curva por barra).
- Pilha de desfazer por documento (o servidor guarda uma só).
