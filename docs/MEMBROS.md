# Membros (peça física contínua)

Decisão de 04/11/2026, depois da revisão geral pedida pelo usuário ("a viga é dividida e isso atrapalha para pôr conexões").

## O problema

O solver só liga barras pelas pontas. Uma viga principal que recebe outra no meio precisa virar dois elementos. Antes desta
mudança cada elemento era tratado como uma peça:

- **flambagem e FLT** com o comprimento do pedaço: pilar W200x46.1 de 6 m dividido a 3 m passava com taxa 0,78 quando a peça
  inteira reprova com 1,67; viga W310x38.7 de 8 m com nó no meio passava com 0,77 contra 2,30;
- **Cb** tirado do diagrama de meia viga;
- **flecha** medida em relação à corda do pedaço e limite L/350 do pedaço (8 m: 6,4 mm / limite 11,4 mm, contra 29,8 mm /
  22,9 mm da viga inteira);
- **ligações**: o nó interno de duas metades colineares virava "emenda" (no PLATAFORMA_REV4 do CYPE, 8 das 15 emendas
  eram falsas: as duas metades eram a mesma peça do CYPE).

## O critério

`params.membro` (texto) na barra. Barras colineares, encadeadas pelas pontas e com o mesmo id formam **um membro**
(`codigo/motor/prossen_estrutural/membros.py`). O solver não muda.

- **Travamento por eixo**: num nó interno do membro, outra barra trava o deslocamento no eixo local e_k (y ou z) se
  |cos(barra, e_k)| ≥ 0,25; apoio de translação trava e_k se |e_k[g]| ≥ 0,25. Os comprimentos são os trechos do membro
  entre nós travados naquele eixo.
- **Verificação (NBR 8800/14762)**: KLx (eixo forte; desloca em z local) = kx · trecho travado em z; KLy, Lz e Lb =
  ky/kz · trecho travado em y; esbeltez de tração (5.2.8) pelos mesmos trechos sem K. `params.Lb`, `params.Cb`,
  `kx/ky/kz` continuam valendo. Cb: diagrama do trecho destravado inteiro (barras do trecho em sequência).
- **Flecha**: vão = trecho travado em z (apoios reais da peça; viga secundária que chega horizontal não é apoio da
  principal no eixo vertical), deslocamentos em relação à corda desse vão. Balanço: ponta livre em relação ao nó travado.
- **Ligações** (`aco/ligacoes_modelo.py`): duas pontas do mesmo membro, colineares e de mesma seção, passam pelo nó.
  Só elas no nó → sem ligação (motivo "peça contínua"). Com outras barras → a peça é o apoio contínuo delas
  (viga–viga, perfil–tubo...), mesmo com pilar no nó. Seção diferente dentro do membro = emenda de verdade.
- Cadeia torta ou com ramificação: aviso `membro_invalido` e as barras ficam verificadas uma a uma.
- Linha da verificação ganha `membro`, `L_membro_m`, `Lfl_x_m`, `Lfl_y_m`.

ponytail: qualquer barra que chega trava o eixo, mesmo que a outra ponta dela esteja solta. Contraventamento sem
continuidade até um ponto fixo não trava de verdade: nesse caso informe `Lb`/`kx`/`ky`.

## Quem grava o membro

- `geradores_extra._Obra.dividir` (gerador de terças, contraventamento, "ligar nó" do gerenciador de ligações):
  `membro = original.membro ou original.id`; parâmetros de ponta (`*_i`, `*_j`: rótula, trecho rígido) só no primeiro /
  último trecho (antes o trecho rígido ia para todos os pedaços).
- `importar_cype`: peça do CYPE com mais de uma barra → `membro = "P<id da peça>"`.
- Interface (WPF): ver a seção abaixo.

## Outras mudanças da mesma revisão (motor)

- `validar`: aviso `no_solto_sobre_barra` quando uma barra termina no vão de outra sem nó ali (antes a secundária ficava
  em balanço na análise sem nenhum aviso).
- B1 (P-δ local, NBR 8800 anexo D) quando a análise é de 1ª ordem (P-Δ desligado), Cm = 1,0 (a favor).
- Galpão sem pavimentos: deslocamento do topo de cada prumada de pilares em relação à base, H/300 (tabela C.1), e aviso
  `segunda_ordem_sem_pavimentos` com P-Δ desligado.
- Cantoneira simples fletida: AISC 360-16 F10 por analogia (a NBR 8800:2008 não tem item); antes a diagonal em L com
  peso próprio reprovava como "não verificada".
- `papel` aceita `diagonal` (só aço).
- Importação CYPE: painel de carga **inclinado** (escada, rampa) com caso que não é de vento recebe a carga gravitacional
  (−Z) na área real. Antes ia pela normal com componente dominante positiva e a carga da escada **subia**: no
  PLATAFORMA_REV4 a sobrecarga PESSOAS somava −109 kN contra −145 kN do CYPE; agora as reações batem a ~2 %.
- Ligações: pré-dimensionamento sem opção que passe devolve a **menor** (antes a maior da lista: gusset 617x639x25 mm com
  9 parafusos para uma L 1.1/2"); o mínimo de 45 kN (6.1.5.2) vale para parafusos, soldas e gusset, e os estados-limites
  da própria barra usam a força real dela; o mínimo fica limitado a Ag·fy/γa1 da barra.
- Realista (`POST /api/ligacoes/geometrias`): com cálculo e o `grupo`/`membros` do item, propõe e verifica com a
  envoltória ELU real das pontas (antes N 50, V 50, M 30 fixos para tudo); terça/travessa PFF isenta do mínimo.
- Viga sobre apoio externo (laje do prédio, consolo) não vira "Base de pilar".

## Interface

Tudo no modelo espacial (WPF), gravado em `params.membro` das barras; um Ctrl+Z desfaz cada operação.

- **Desenhar ligando no vão**: ponta de barra desenhada (snap Próximo, Médio, Perpendicular, Interseção ou clique a ≤ 1 mm do
  eixo) no meio de outra barra, sem nó ali, divide a hospedeira nesse ponto. Os dois trechos ficam com
  `membro = membro da hospedeira ?? id dela`, e a barra nova liga no nó. Desenho e divisão vão num PUT só (um Ctrl+Z). A barra
  de status diz, por exemplo, "b1 dividida em B1/B2 (mesmo membro)". Se a seção ou o material ainda não estão no modelo
  (lote), não divide; aí o aviso `no_solto_sobre_barra` aparece e "Ligar todos" resolve. `Desenho3D.DesenharBarras`,
  `GeometriaEdicao.LigarNoVao`.
- **Aparar/Estender** até o vão de um limite: o limite (ou outro trecho do mesmo membro) divide no ponto e a ponta liga no
  nó novo. Os trechos substituem o limite na lista, e o próximo aparo no mesmo limite continua valendo.
- **Dividir** (DIVIDIR e o painel): os trechos herdam `membro = membro da original ?? id dela`. Os params `_i` ficam no 1º
  trecho e os `_j` no último.
- **Unir em membro** (Editar › Modificar, comando `MEMBRO`, alias `UNIRMEMBRO`): as barras escolhidas e os outros trechos dos
  membros que elas já tocam recebem o mesmo id. Recusa com o motivo, sem mudar o modelo, se não forem uma cadeia simples, se
  não forem colineares (±0,5°) ou se tiverem seções diferentes. O nó do meio pode ter outras barras (secundárias).
  **Separar membro** (`SEPARARMEMBRO`) tira `params.membro` das escolhidas. O JUNTARBARRAS recusado por "outra barra no nó"
  sugere Unir em membro.
- **Propriedades da barra**: a linha Membro mostra o id, o número de trechos e o comprimento total. Os campos kx, ky, Lb e Cb
  aceitam vazio = automático (o motor calcula entre os travamentos do membro) e valem para todos os trechos do membro. Em lote
  (LOTE): vazio = manter e `auto` = tirar. A verificação mostra Lfl x / y e o L do membro.
- **Avisos**: `no_solto_sobre_barra` vira uma linha com o botão **Ligar todos**, que chama `POST /api/ligacoes/ligar_no` por nó
  (um passo de desfazer por nó). O Gerenciador de ligações › Nós sem ligação também tem "Ligar todos (n)".
- **Verificação de barras**: o botão "Selecionar na vista" seleciona a lista filtrada; sem filtro, seleciona as reprovadas. O
  comando `SELREPROV` seleciona as reprovadas. A dica da linha mostra o membro, o L dele e Lfl x / y.
- **Dimensionar / Dimensionar seleção** (botão novo; comando DIMSEL): os trechos de um membro recebem o mesmo perfil, e o pior
  trecho governa. Com mais de uma rodada, o resultado vira um passo só de desfazer: as rodadas voltam e o final entra num PUT.
- **Realista**: cada item de `/api/ligacoes/geometrias` leva `membros` (as pontas {barra, ponta} do grupo) quando há cálculo.
  A chave do cache inclui os membros e a identidade do cálculo. `ok = false` desenha a ligação esmaecida, como proposta, com
  " · não passa" e uma linha "sem ligação válida" no gerenciador. O chip mostra "· n não passam". As flexíveis levam `apoio`
  pelo tipo de nó (mesa_pilar, alma_pilar ou alma_viga). O gusset vai pelo nome do catálogo (`perfil_diagonal`) quando o
  perfil está lá. Emenda rígida acima de 5° vira a chapa de topo de cumeeira local, em vez de chapa simples.
