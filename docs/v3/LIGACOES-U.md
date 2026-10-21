# Ligações com perfis U e conflitos de nó (Frente G, G5)

Código: `aco/ligacoes_flexiveis.py`, `aco/ligacoes_rigidas.py`, `aco/ligacoes_contraventamento.py`,
`aco/ligacoes_modelo.py`, `aco/ligacoes.py`. Testes: `codigo/motor/testes/test_ligacoes_u.py`.

## Perfis aceitos

Os tipos flexíveis (`chapa_simples_alma`, `cantoneira_dupla_alma`, `chapa_extremidade_flexivel`, `viga_viga_recorte`,
`apoio_cantoneira`) aceitam forma U do catálogo: UPN, U laminado, U e Ue formados a frio.

- Alma `tw`, mesa `bf`, `tf`. No dobrado, `tw = tf = t`.
- Raio `r`: o catálogo do UPN e do U laminado não traz `r`. Vale `r = tf` (DIN 1026: r1 = tf). No dobrado, `r = ri + t`.
- Altura útil da alma: `h − 2tf − 2r`. A chapa e as cantoneiras ficam dentro dela (`_cabe_na_alma`).
- Furação do pré-dimensionamento para viga com `d ≤ 150 mm` (fora da tabela Gerdau): parafuso 1/2", `s = 38`, `e_v = 22`.
  Se nem 2 parafusos cabem (UPN 100: 66 mm úteis), a chapa simples usa 1 parafuso. Com 1 parafuso, o grupo é rótula e
  `V·a` vai só para a chapa.
- UPN 65 e UPN 50 não levam chapa simples. O motor recusa com o motivo "perfil … baixo demais para chapa simples".

## Geometria com U

- Referencial: o U abre para `+y` local. A face externa da alma (costas) fica em `y = −xg`.
- Chapa simples: a chapa fica nas costas da alma. Os parafusos entram de fora para dentro, e a porca fica entre as mesas.
- Cantoneira dupla: as duas cantoneiras abraçam a alma. Plano médio da alma em `y = tw/2 − xg`.

## Verificações a mais

| Tipo | Verificação |
|---|---|
| chapa simples | `parafusos: ponta dentro da mesa do U`: ponta do parafuso além da alma ≤ `bf − tw` |
| cantoneira dupla | `cantoneira interna dentro da mesa do U`: aba ≤ `bf − tw` |

Excentricidade ao centro de cisalhamento: `valores.e_centro_cisalhamento_mm`. O centro de cisalhamento fica a
`|x0| − xg` além das costas da alma. A torção `V·e` na viga sai como aviso e não é verificada.

## Recusas

- Rígidas com U (`chapa_topo_*`, `flange_plate`, emendas rígidas): `ValueError` "ligação rígida com perfil U não
  suportada". No modelo (`ligacoes_modelo.membros`), a viga–pilar na mesa rígida com viga U sai com
  `sem_tipo: true` e `motivo` com esse texto. A alma do pilar e a viga–viga já são propostas flexíveis em qualquer
  condição, então não recebem `sem_tipo`.
- Chapa de extremidade maior que a face do apoio: o `sugerir` recusa. Exemplo: viga UPN 120 na mesa de um UPN 50: "chapa de topo 74 mm > face do apoio
  38 mm (UPN 50): use cantoneira lateral ou outro apoio". A chapa nunca é cortada em silêncio. Chapa mínima =
  `tw + 2·(5 mm + db/2 + borda mínima)`.

## Gusset: orientação

A `geometria` do `gusset_contraventamento` traz:

- `apoio_dir`: vetor unitário do eixo do apoio no referencial local. Vale o eixo do pilar (`pilar`, `viga-pilar`) ou o
  da viga/base.
- `lado`: `+1` ou `−1`. A chapa fica do lado `lado · (normal × apoio_dir)`, com `normal = [0, −1, 0]` da chapa.

## Nós próximos

Dois nós a menos de 200 mm, os dois com chapa (gusset, emenda ou nó de treliça), dão chapas sobrepostas. Fica a ligação
do nó com mais pontas (empate: o primeiro do modelo). O outro nó perde as chapas e recebe em `motivos` o texto
"nós a <200 mm: ligação única (no nó X)". A busca usa uma grade de células de 200 mm.

## Barra curta

Quando a barra é curta demais para a ligação, o resultado traz `excluir_interferencia: true` além do `motivo`. A linha
do membro em `dimensionar_grupo` também traz essa chave. O WPF tira essa ponta da contagem de interferência.

## Rodada 1 (revisão de cálculo)

- Altura útil da alma: vale a alma reta tabelada `d` quando o catálogo traz (`secoes._D_ALMA_UPN`, DIN 1026, em `props.d_alma`; UPN
  120: 82 mm, por causa da mesa inclinada). Sem `d`: `h − 2tf − 2r`. Internamente, `tf + r = (h − d)/2`.
- `abertura_y` (+1/−1, padrão +1): lado local para onde o U abre. O chamador informa; a `geometria` devolve o lado
  usado. Com −1, a chapa, os parafusos e as soldas espelham em y.
- Excentricidade ao centro de cisalhamento medida do plano de corte dos parafusos (costas da alma): `e = |x0| − xg`.
- Centro instantâneo: o coeficiente `C` nunca fica abaixo do método elástico, `1/√((1/n)² + (e·ymáx/Σy²)²)`.
  Com 2 parafusos, o ajuste de Δmáx dava 2 % abaixo do elástico.
- Novo tipo `solda_direta_alma`: alma do U soldada direto no apoio por dois filetes, sem chapa nem parafusos.
  Verifica o filete (6.2.5), o metal base do apoio, o metal base da alma entre os filetes, o cisalhamento da alma
  (`Aw = h·tw`), a perna mínima e a máxima (tabela 10, 6.2.6.2.2) e o comprimento mínimo (4b e 40 mm). Comprimento padrão
  de cada filete `Lw = h`: a face interna continua pelas mesas. A geometria traz só `soldas` (e `pontas`).
  Conta à mão: E70, perna 5 mm, 2 × 84 mm → 2 · 84 · 0,762 = 128 kN. UPN 50 em UPN 100 com 45 kN: a alma governa,
  0,6 · 345 · 50 · 5 / 1,10 = 47,0 kN (taxa 0,957).
- Reserva (fallback):
  - `sugerir` de `chapa_simples_alma`, `cantoneira_dupla_alma` e `viga_viga_recorte` com viga U: se o perfil é baixo
    demais, ou se a parafusada não passa e a solda passa, a resposta traz `tipo_ligacao = "solda_direta_alma"` e
    `motivo`. O chamador passa a verificar esse tipo.
  - `otimizar` desses tipos sem solução com viga U tenta `solda_direta_alma` e devolve `tipo` e `motivo`.
  - `identificar`: grupo de viga U que não leva nem 1 parafuso (UPN 50/65) nasce com projeto `solda_direta_alma`.
- Seções compostas, como 2UPN em caixão, entram como dict `{d, bf, tw, tf}` em mm. O `_perfil` aceita esse formato
  (teste `test_composto_por_dict`).
- PLATAFORMA, com os esforços do WPF (N 50, V 50, M 30): 74 nós com proposta, 40 com proposta ok. As 7 recusas
  restantes são as seções compostas passadas por nome.
- Viga de piso UPN 120 com 45 kN (mínimo de 6.1.5.2, sem isenção): a chapa simples com 2 parafusos de 1/2" reprova
  no cisalhamento dos parafusos. O `sugerir` troca para solda direta.

## Pendências

- O `sugerir` ainda não tenta a cantoneira lateral quando a chapa não cabe. Por enquanto só sai o motivo.
- Seções compostas do modelo, como "2UPN 80 duplo em tubo", não estão no catálogo. Como apoio, dão "não está no
  catálogo". O WPF teria de mandar um dict com `d, bf, tw, tf`.
