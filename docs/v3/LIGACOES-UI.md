# Projetista de ligações metálicas

Data: 21/10/2026 (branch `wt-lig-ui`). Pedido: "padrões de ligações metálicas para eu escolher, dos tipos mais variados; ela
calcula, eu escolho e ele calcula pra mim e posso ir mexendo, tipo com a placa base. Chapas, parafusos, soldas, simples e
personalizado."

Código: `codigo/Estructto/JanelaLigacaoProjeto.cs` (janela, integração com a faixa, Ctrl+K, gerenciador de grupos e capturas
51), `codigo/Estructto/LigacaoDesenho.cs` (geometria e desenho). Motor: `aco/ligacoes.py` (seção "interface do projetista"),
`projeto.py` (`tipos_ligacao`, `verificar_ligacao`, `sugerir_ligacao`, `otimizar_ligacao`, `memorial_ligacao`),
`aco/ligacoes_modelo.py` (`verificador`, `esforcos_contrato`), testes `testes/test_ligacoes_ui.py`.
A antiga `JanelaLigacao.cs` foi retirada: os botões Parafusos, Filete de solda, Chapa simples e Ligação de tração abrem o
projetista direto no tipo.

## 1. Tudo vem do motor (`GET /api/ligacoes/tipos`)

A tela não conhece nenhum tipo: lê de cada item `tipo`, `nome`, `familia`, `liga`, `descricao`, `campos` (ou `entradas`),
`esforcos`, `sugerir` (bool) e `exemplo` (`{dados, esforcos}`). Tipo novo num módulo de família aparece sozinho na biblioteca.

`projeto.tipos_ligacao()` completa o esquema:
- tipos antigos (`parafuso`, `solda_filete`, `chapa_simples`, `ligacao_tracao`) ganham `nome`/`familia`/`liga` de `ligacoes.META`
  e `min`/`max`/`inteiro` do esquema interno (`ligacoes.esquema`);
- `sugerir` = o tipo tem `sugerir()` no registro do contrato (`ligacoes.registro(tipo)`);
- `exemplo` = padrões do esquema + valores de amostra (`_AMOSTRA_LIG`: W310x38.7 na viga, W250x73 no apoio, cantoneira 76 × 6,35,
  dimensões dos tipos antigos) + `sugerir()` com esforço típico (o 1º esforço = 100 kN; com M: V = 100 kN e M = 60 kN·m).

`projeto.verificar_ligacao()` devolve sempre `taxa` e `taxa_max`, e `geometria` com `membros` (perfis `perfil_*` dos dados com
d, bf, tw, tf em mm, para desenhar a viga e o apoio). Tipos antigos ganham geometria esquemática (`ligacoes.geometria_basica`).

## 2. Biblioteca

Cartões agrupados por família (Flexíveis, Rígidas, Emendas, Contraventamento e treliça, Bases, Componentes; família desconhecida
vira seção nova). Cada cartão: miniatura isométrica da `geometria` do exemplo (o projetista chama `verificar` com o exemplo de
cada tipo; cache por sessão), nome, descrição, etiquetas `liga` e "pré-dimensiona" quando há `sugerir`. Busca (sem acento, por
nome, descrição, família e liga) e filtro por família. Aberto por um grupo: filtro "Só para <tipo de nó do grupo>" (liga do tipo
comparada ao tipo do grupo, `-` = `_`). O cartão "Placa de base" abre a janela da placa de base (no gerenciador, a placa do pior
membro do grupo; fora dele, a ferramenta Placa de base no nó selecionado).

## 3. Projeto (3 passos, como a placa de base)

- **Passo 1 · Barras e esforços**: perfis (`perfil_*`, catálogo W/HP editável) e `apoio`; esforços do tipo em kN/kN·m. Com grupo,
  lista "Esforços de" com cada ligação do grupo (pior primeiro); o membro escolhido preenche os esforços pela mesma conversão do
  motor: tipos antigos `V = √(Vy²+Vz²)`, `N = |N|`, `Ft = max(N,0)`, `Fv`, `F`; contrato `V = |Vz|` (plano da alma), `Vz = |Vy|`,
  `M = My`, `Mz`, `T`, `N` com sinal (`ligacoes_modelo.esforcos_contrato`). Perfis do grupo vêm da assinatura (barra → viga,
  apoio → suporte/pilar) e `apoio` pelo tipo de nó (mesa/alma do pilar, viga).
- **Passo 2 · Parâmetros**: gerados dos campos, em seções recolhíveis (Parafusos, Chapa e geometria, Solda, Recortes, Materiais e
  opções; campos `_1`…`_4` viram "Barra n"). Controle por campo: `opcoes` → segmentado (≤ 3 curtas) ou lista; booleano → Sim/Não;
  `d`/`d_mm` → seletor de diâmetros usuais (⅝", M16, ¾", M20, ⅞", M24, 1", M27, dentro de min/max); espessuras (`t`, `t_chapa`,
  `t_gusset`…) → passo pela lista comercial; perna de solda → passo 3…16 mm; inteiro sem unidade → passo ±1 com min/max; número →
  caixa com unidade (campo em m aparece em mm). Dica = `ajuda` (ou `descricao` longa) + faixa. Botões **Sugerir** e **Otimizar**.
- **Passo 3 · Verificações**: veredito (PASSA / NO LIMITE / NÃO PASSA e taxa), a que governa, e cada verificação com item da NBR
  (o contrato já traz "NBR 8800 …"; item só com número ganha o prefixo), Sd ≤ Rd (disposições: "exigido x ≤ y mm" / "x ≤ máx y")
  e barra de taxa; avisos. Chips do topo por componente (parafusos, chapa, solda, cantoneiras, viga, apoio — pelas palavras do
  nome da verificação) filtram a lista.
- Recalcula 250 ms depois da última mudança (`DispatcherTimer`; resposta antiga é descartada pelo número de sequência).

## 4. Desenho ao vivo (`LigacaoDesenho.cs`)

`GeoLig.De(geometria, falhas)`: chapa = contorno × espessura (normal, u, v = n × u quando falta), cantoneira = duas abas,
parafuso (origem, eixo, d, comprimento, pega), solda (polilinha, perna), recortes (recuo, cope topo/base), membros posicionados
pelo contrato (viga a partir do recuo; apoio `mesa_pilar` com a altura em x, `alma_pilar` com a altura em y, `alma_viga` viga
ao longo de y com o topo alinhado; emenda = a barra passa pelo nó). Tipo antigo sem perfil de apoio: parede hachurada em x da chapa.

Vistas: Elevação (x, z), Planta (x, y), Lateral (y, z) e 3D (girável), juntas ou uma grande. Ortogonais: chapa de frente com
contorno, de topo com hachura da espessura; parafuso de frente = círculo com cruz (sextavado da cabeça), de lado = fuste, cabeça e
porca; solda = cordão com triângulos do filete e chamada (linha de referência + triângulo + perna); cotas em cadeia borda →
furos → borda nos dois eixos da chapa com mais furos (trechos iguais viram "n × s"; texto que não cabe sai e a cota total vai uma
linha para fora); rótulos dos perfis e da chapa (espessura × altura). 3D: pintor com faces sombreadas, perfis translúcidos atrás,
chapas por cima e parafusos só do lado do observador. Enquadramento na ligação com folga; os perfis seguem além da borda.
Componentes reprovados em vermelho. Troca de geometria animada (`Anim.Valor` em `Prog`, 260 ms): mesma estrutura = posições
interpoladas, senão fade.

## 5. Sugerir, Otimizar, Memorial

- `POST /api/ligacoes/sugerir {tipo, perfis, esforcos}` → dados (o `sugerir` do tipo; 422 se o tipo não tem). Com grupo, escolher
  um tipo já pré-dimensiona. Campos mudados ficam em destaque.
- `POST /api/ligacoes/otimizar {tipo, dados, esforcos}` → `{dados, resultado, ok, passos, mudancas}` — `ligacoes.otimizar`:
  enquanto não passa, aplica o degrau (um item ou dois juntos, ex.: chapa mais grossa só passa com solda maior) que mais reduz
  a pior taxa; empate em 2 %: menos material (solda < chapa < diâmetro < quantidade). Sobe qualquer item; desce o item que um
  máximo reprovado pede (dica `sentido`/`parametro`, `LIGACOES-CONTRATO.md`; ex.: perna acima do máximo da chapa). Tipo antigo
  com altura `h` e passo `s`: um parafuso a mais cresce a chapa `s`. Depois de passar, desce cada item enquanto continuar
  passando (mínimo local); item sem efeito nas verificações não muda.
  Itens: quantidades (`n`, `n_linhas`, `n_por_linha`, `n_colunas`…), diâmetro (`d`, `d_mm`), espessura (`t`, `t_chapa`,
  `t_gusset`, `tp`, `t_mesa`, `t_alma`…), perna (`b_solda`, `perna`, `b_mesa`, `b_alma`, `b_solda_mesa`…). `ponytail:` busca gulosa, não global.
- `POST /api/ligacoes/memorial {tipo, dados, esforcos}` → `{html}`: `<section class="memorial-ligacao">` com veredito, esforços,
  dados (rótulos), verificações (item, expressão da norma pelo item, solicitante, resistente, taxa) e avisos.
  **Onde entra**: o relatório do motor (`relatorio.py`) não tem gancho de ligações; a tela grava a seção numa página HTML
  autônoma (`%TEMP%\Estructto\memorial-ligacao-<tipo>.html`) e abre no navegador (imprimir/PDF). A seção já está pronta para o
  relatório quando ele ganhar o capítulo de ligações.

## 6. Grupos

Gerenciador › cartão do projeto › **Projetar no projetista…**: dimensiona o grupo (calcula antes se preciso), abre o projetista
com o pior membro e os perfis do grupo (biblioteca filtrada, ou direto no tipo do projeto atual). **Aplicar ao grupo** grava o
projeto (`POST /api/ligacoes/grupos/editar`, acao `projeto`) e verifica todos os membros em todas as combinações ELU
(`/dimensionar`); a aba "Grupo" mostra a taxa por ligação (a que governa em destaque, clique carrega os esforços dela).
`ligacoes_modelo.verificador(tl)`: além dos `VERIFICADORES`, qualquer tipo do contrato é aceito no grupo com os esforços do
contrato (`esforcos_contrato`).

Faixa **Ligações e bases › Ligações › Projetar ligação** (também no Ctrl+K "Projetar ligação").

## 7. Capturas

`ESTRUCTTO_CAPTURAS=projetista` (com `ESTRUCTTO_MOTOR`): `51-projetista-biblioteca`, `-chapa-simples` (V = 100 kN, mais
`-elevacao` e `-3d`), `-falha` (V = 165 kN, em vermelho), `-otimizado`, uma de cada família (chapa de topo estendida, emenda de
viga, gusset, cantoneira dupla) e, no galpão, `-grupo-biblioteca`, `-grupo-projeto`, `-grupo-aplicado`. Também na rodada completa.

## Limites

- Grupo de componente (cor e chips) pelas palavras do nome da verificação.
- Tipo sem `membros` (contraventamento) desenha só as peças da ligação; a diagonal não aparece.
- 3D por pintor (sem z-buffer): perfis sempre atrás das peças da ligação, de propósito.
- Placa de base continua na janela própria (núcleo C#), não no esquema do motor.
