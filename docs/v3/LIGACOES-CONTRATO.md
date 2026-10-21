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

## Testes
Cada tipo tem pelo menos 1 exemplo resolvido de livro (`docs/v3/ligacoes/*.md`, com página) reproduzido no teste, ou cálculo à mão documentado no próprio teste.

## Lote e face do apoio (G6)
- `POST /api/ligacoes/geometrias` `{"itens": [{"tipo", "dados", "perfis"?}]}` → lista na mesma ordem de
  `{"geometria", "motivo", "avisos"}` ou `{"erro"}`. Com `perfis`: sugerir (N 50, V 50, M 30) e depois verificar com esforços nulos.
- Flexíveis: `x_face` (mm, opcional) = distância do nó à face de contato do apoio; manda na chapa e no plano de corte (sem cope).
  Gusset: `ec`/`eb` informados não são trocados pela meia altura do perfil; `perfil_pilar`/`perfil_viga` aceitam `{d, bf, tw, tf}`.
- `GET /api/ligacoes/grupos`: cada membro traz `apoio` (id da barra de apoio, quando há; não vai para o arquivo).
