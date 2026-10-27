# Contrato da biblioteca de ligações (motor Python)

Todos os tipos novos seguem este contrato, para a interface, o agrupamento (`ligacoes_modelo.py`) e o 3D (Realista) tratarem todos iguais.

## Módulos
- `aco/ligacoes.py` — primitivas (parafuso, solda, chapa, bordas) e tipos já existentes. Não duplicar primitivas: importar daqui.
- `aco/ligacoes_flexiveis.py` — cantoneira dupla (alma), chapa simples (shear tab), chapa de extremidade flexível, apoio em cantoneira/consolo, viga-viga com recorte (cope).
- `aco/ligacoes_rigidas.py` — chapa de topo ajustada e estendida (com/sem enrijecedor), flange plate soldada/parafusada, painel de alma do pilar, enrijecedores e chapa de reforço, emenda de viga e de pilar.
- `aco/ligacoes_contraventamento.py` — gusset de contraventamento (Whitmore, flambagem da chapa, ligação gusset-viga/pilar), nó de treliça com gusset, tirante/barra redonda com olhal ou rosca, ligação de cantoneira simples/dupla em treliça.
- `aco/placa_base.py` — bases (já existe; v2 em correção).

## Registro
Cada módulo expõe `TIPOS: dict[str, dict]` e o `ligacoes.py` agrega tudo em `tipos_ligacao()`:
```python
TIPOS = {
  "cantoneira_dupla_alma": {
     "nome": "Cantoneira dupla na alma",           # PT, curto
     "familia": "flexivel" | "rigida" | "contraventamento" | "base" | "emenda",
     "liga": ["viga-pilar-mesa", "viga-pilar-alma", "viga-viga"],   # tipos de nó do agrupamento
     "descricao": "...",
     "entradas": {campo: {"rotulo": str, "unidade": "mm"|"kN"|"MPa"|"-", "padrao": valor, "opcoes": [...]?, "min": x?, "max": y?}},
     "verificar": fn,          # fn(dados: dict, esforcos: dict) -> Resultado
     "sugerir": fn | None,     # fn(perfis: dict, esforcos: dict) -> dados  (pré-dimensionamento a partir dos perfis)
  }}
```
`esforcos` = esforços de CÁLCULO (ELU, já majorados), na convenção da ponta da barra que chega à ligação:
`{"N": kN (+tração), "V": kN (cortante no plano da alma), "Vz": kN, "M": kN·m (plano forte), "Mz": kN·m, "T": kN·m}`.
`dados` inclui os perfis ligados como nomes do catálogo (`"perfil_viga": "W310x38.7"`, `"perfil_pilar": ...`) — propriedades via catálogo do motor.

## Resultado
```python
{
 "ok": bool, "taxa": float,                    # máx. das verificações
 "verificacoes": [{"nome": str, "item": "NBR 8800 6.3.3.2", "solicitante": x, "resistente": y, "unidade": "kN", "taxa": x/y, "ok": bool}],
 "avisos": [str],
 "geometria": {...}                            # abaixo
}
```

### Dicas do otimizador (opcionais, nas disposições construtivas)
Verificações de detalhamento (mínimos e máximos de perna de solda, borda, espaçamento, b/t…) levam também:
- `"sentido"`: `"sobe"` (mínimo: o valor verificado precisa crescer) ou `"desce"` (máximo: precisa diminuir). Os helpers
  `minimo`/`maximo` de cada módulo (`ligacoes._minimo`/`_maximo`, `_R.minimo`/`maximo`) já põem o sentido.
- `"parametro"`: campo de `entradas` que controla o valor (ex.: `"b_alma"` na perna máxima da solda da alma), quando é um só.

`ligacoes.otimizar` sobe qualquer item, mas só desce um item quando um máximo reprovado pede (o seu `parametro`); máximo
reprovado sem `parametro` (ou com campo que o otimizador não mexe) libera a descida de todos. Perna acima do máximo: o otimizador
desce a perna ou, em dois degraus juntos, desce a perna e engrossa a chapa — o que der a menor taxa com menos material.
Verificações de resistência não levam dica.

## Geometria (para o 3D e a exportação)
Em mm, no **referencial local da ligação**: origem no nó; eixo x ao longo da barra que chega (sentido saindo do nó), z = eixo forte/alto do perfil
(para cima na alma), y = x × z. Listas:
- `chapas`: `[{"nome", "contorno": [[x,y],...] (polígono no plano da chapa), "origem": [x,y,z], "normal": [nx,ny,nz], "u": [ux,uy,uz] (eixo x do contorno), "espessura"}]`
  (G6: `origem` é uma face da chapa; o sólido vai de `origem` até `origem + espessura·normal`.)
- `cantoneiras`: `[{"perfil": "L76x6.3", "origem", "eixo", "u", "comprimento"}]`
- `parafusos`: `[{"d", "comprimento", "origem", "eixo"}]` (eixo = direção do fuste, da cabeça para a porca)
- `soldas`: `[{"tipo": "filete"|"penetracao", "perna", "pontos": [[x,y,z],...]}]` (cordão como polilinha)
- `recortes`: `[{"barra": "viga"|"pilar", "tipo": "cope"|"chanfro"|"recuo", "parametros": {...}}]` — cope: altura e comprimento do recorte no topo/base; recuo: folga da ponta.
- `tubos` (W7, `ligacoes_tubos.py`: `tubo_tubo_soldado` e `perfil_tubo_soldado` com tubo circular): tubos circulares
  ligados, para o desenho mostrar as barras e não só a solda. Tipos sem tubo mandam `[]` ou não mandam a chave.
  ```
  {"nome": "ramal" | "receptor",
   "grupo": "viga" (ramal = barra que chega) | "suporte" (receptor = barra que passa),
   "campo": campo editável ("perfil_viga", "perfil_suporte", "D_receptor", "h_ramal"…) ou "",
   "origem": [x,y,z] (mm, ponto no eixo do tubo), "eixo": [ex,ey,ez] (unitário),
   "comprimento": L (mm; o tubo vai de origem até origem + eixo·L),
   "d": diâmetro externo (mm), "t": parede (mm),
   "corte": null | {"origem": [x,y,z], "eixo": [ax,ay,az], "r": raio (mm)}}
  ```
  `corte` = boca de peixe: só há material do tubo onde a distância ao eixo do cilindro de corte é ≥ `r` (o cilindro
  externo do receptor). Receptor: um trecho de cada lado do nó (projeção da curva de contato no eixo + d0), sem corte.
  Ramal: começa no ponto mais baixo da curva de contato e vai até 2·d1 além do mais alto; a curva da solda (`soldas[0]`)
  fica sobre a superfície do receptor e sobre o ramal cortado (teste `test_ligacoes_tubos.TestNoTubular.test_contrato_tubos`).
  No `perfil_tubo_soldado`, o receptor aparece se for circular e o ramal só se também for tubo circular.
- `z_topo_suporte` (W10, opcional, mm): topo do apoio viga (alma de viga) no referencial da viga, quando a viga apoiada
  não tem o topo alinhado (desnível). Sem a chave, o desenho alinha os topos. Solda direta e viga–viga com desnível.
- `membros` (W8, opcional, só para o desenho do projetista): `{"barra": "viga"|"pilar", "perfil", "campo",
  "pecas": [{"base": [[x,y,z],...] (mm, polígono no plano MÉDIO da chapa), "normal": [nx,ny,nz], "espessura"}]}`.
  Com `pecas`, o desenho usa essas chapas como o perfil (camada de perfil, cor/falha do grupo viga ou suporte) em vez de
  montar o I pelas medidas `d`, `bf`, `tw`, `tf`. `perfil_tubo_soldado` manda o perfil com a ponta na sela e o tubo
  retangular receptor (4 paredes). Não vai para o Realista nem para listas de material (não é `chapas`).

## Testes
Cada tipo tem pelo menos 1 exemplo resolvido de livro (`docs/v3/ligacoes/*.md`, com página) reproduzido no teste, ou cálculo à mão documentado no próprio teste.

## Lote e face do apoio (G6)
- `POST /api/ligacoes/geometrias` `{"itens": [{"tipo", "dados", "perfis"?}]}` → lista na mesma ordem de
  `{"geometria", "motivo", "avisos"}` ou `{"erro"}`. Com `perfis`: sugerir (N 50, V 50, M 30) e depois verificar com esforços nulos.
- Flexíveis: `x_face` (mm, opcional) = distância do nó à face de contato do apoio; manda na chapa e no plano de corte (sem cope).
  Gusset: `ec`/`eb` informados não são trocados pela meia altura do perfil; `perfil_pilar`/`perfil_viga` aceitam `{d, bf, tw, tf}`.
- `GET /api/ligacoes/grupos`: cada membro traz `apoio` (id da barra de apoio, quando há; não vai para o arquivo).
- `x_max` (mm, opcional; flexíveis e gusset): alcance máximo da ligação ao longo da barra (chapas, cantoneiras, parafusos);
  verificação "geometria: comprimento disponível na barra". No lote, se reprova: motivo "barra curta demais para a ligação: …".
  O sugerir dos flexíveis já escolhe com `x_max` (chapa simples compacta pela Tab. 14; sem parafusada que caiba →
  `solda_direta_alma` com `tipo_ligacao` e `motivo`, que o lote passa a verificar e desenhar). Nesse caso o item do lote
  traz `troca` {tipo, dados (sem x_max/x_face/abertura_y), motivo}, e o WPF grava isso no projeto do grupo.

## Peças só do projetista (W8-CONTRAV, docs/v3/W8-CONTRAV.md §3)
`perfis_barra` (placas da barra ligada no formato de `chapas`, com `perfil`, `campo` e `camada` 0 = perfil), `linhas`
(`estilo` "tracejada" | "cota", `pontos`, `texto`, `lado`) e `vista` ({x, z}: eixos da elevação no referencial local).
O Realista não lê essas chaves (desenha as barras do modelo). Campo de perfil com `resugerir`: trocar o perfil refaz o
Sugerir. `chapas`: o desenho do projetista respeita a origem na face (G6).
