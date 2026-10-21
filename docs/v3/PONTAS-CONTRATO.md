# Contrato `pontas` (Frente G, G1)

Código: `codigo/motor/prossen_estrutural/aco/pontas.py`. Testes: `codigo/motor/testes/test_pontas.py`.
O motor é a fonte da geometria de pontas e recortes. O render 3D (`Render3DGeometria.Juntas`) passa a só desenhar.

Unidades: metros e radianos. Vetores `[x, y, z]` globais.

## Saída

```
Ponta   = {barra:int, extremo:"i"|"j", plano:{ponto:[x,y,z], normal:[x,y,z]}, folga:float, recortes:[Recorte]}
Recorte = {tipo:"cope"|"angulo"|"esquadria"|"boca_peixe", ...}
  cope       : {face:"sup"|"inf", altura, comprimento, raio}
  esquadria  : {normal}             (bissetriz)
  boca_peixe : {raio, eixo}         (raio e eixo do tubo receptor)
```

`plano.normal` aponta para fora do material da barra (para o nó). A barra é cortada no plano.

## Entrada

`pontas_do_no(no, membros, tipo_ligacao, params) -> list[Ponta]`

- `no`: `[x, y, z]`.
- `membros`: um item por ponta de barra no nó:
  `{barra, extremo, dir (unitário saindo do nó), comprimento, perfil (Secao.props: h, b, tw, tf / t), eixo_d (unitário da altura h), continuo (True = apoio, sem Ponta), desloc (opcional)}`.
- `tipo_ligacao`: `viga_pilar_mesa`, `viga_pilar_alma`, `viga_viga`, `gusset_contraventamento`, `esquadria`, `boca_peixe`.
- `params`: `folga` (padrão 0,012), `raio_cope` (padrão e mínimo 0,010).

## Regras

| Tipo | Distância do plano ao nó, no eixo da barra | Recortes |
|---|---|---|
| viga_pilar_mesa | `h_apoio/2 + folga` | — |
| viga_pilar_alma, viga_viga | `tw_apoio/2 + folga` | `cope` em cada mesa que invade a mesa do apoio: `altura = tf_apoio + folga`, `comprimento = bf_apoio/2 + folga`, `raio = 0,010` |
| gusset_contraventamento | até a face do apoio (borda do gusset) + folga: `ext_apoio/sen θ + (b_diag/2)/tg θ + folga` | — (sem limite 0,3·L) |
| esquadria | plano no nó, normal pela bissetriz, folga 0 | `esquadria` |
| boca_peixe | `√(R² − r²)` (fundo da sela), folga 0 | `boca_peixe` com raio do receptor |

Colisão de mesas (cope): offset `e` da viga no eixo `eixo_d` do apoio + meia largura da viga nesse eixo > `h_apoio/2 − tf_apoio`.

Se `distância + comprimento do recorte ≥ comprimento da barra`: `ValueError("barra curta demais para a ligação")`. O chamador (G2) captura como aviso e mantém a barra.

## Revisão 1

- `angulo` (diagonal no gusset): `{tipo, normal, folga, distancia}`. Corte paralelo à face do apoio, afastado dela pela folga. `distancia` é medida do eixo do apoio até o corte.
- `esquadria` com 3 ou mais tubos: bissetriz com o vizinho mais próximo (maior cosseno), indicado em `recorte.vizinho`.
- `boca_peixe` inclinada: sela calculada em 72 pontos do contorno do ramal. O plano fica no fundo da sela. O recorte traz `angulo` (rad, entre os eixos).
- Barra curta também na esquadria (canto externo `(h/2)·cot(α/2)`) e na boca de peixe (topo da sela).
- Cope: `face` vale `sup`/`inf` (a mesa invadida está acima/abaixo da viga) ou `esq`/`dir` (bordas das mesas da viga mais larga que o vão livre entre as mesas do apoio). Parâmetros `cope_folga_alt` e `cope_folga_comp` (padrão 0,012). `raio_cope` tem mínimo 0,010.
- Erros claros com `ValueError`: nó sem barra contínua; perfil sem `tw`/`tf`/`h`/`b`; esquadria com menos de 2 barras ou com tubos colineares; diagonal paralela ao apoio; ramal maior que o receptor.
- Tubo circular: `h` = diâmetro externo.

## G2: pontas nas ligações

Todo resultado de `verificar` (19 tipos de `tipos_ligacao()` e `placa_base`) traz `geometria["pontas"]`.

- Referencial local da ligação, em metros: origem no nó, `x` ao longo da barra que chega, `z` = altura dela.
  `pontas.pontas_locais(tipo_no, barra, apoio, comprimento, theta, recuo, desloc, params)` monta o apoio nesse referencial e chama `pontas_do_no`.
- Contexto opcional em `dados["contexto_pontas"] = {tipo_no, comprimento, desloc, params}`. O `dimensionar_grupo` passa o tipo do nó do grupo e o comprimento real da barra.
- Tipo do nó: contexto; senão o campo `apoio` (`mesa_pilar`/`alma_pilar`/`alma_viga`); senão a família (contraventamento = gusset, emenda = plano no nó); senão o 1º item de `liga`.
- Sem apoio conhecido (emenda, componentes, diagonais sem perfil de apoio): plano a `folga` do nó, sem recortes. Diagonal no gusset sem perfil de apoio: apoio tratado como linha.
- Placa de base: ponta do pilar na face superior da placa (`x = esp`), sem recortes.
- O cálculo usa a geometria: na chapa de topo, a chapa encosta no apoio e a viga é cortada a `tp` dele (`folga = tp`). A solda das mesas usa a largura real da ponta, `largura_util = b − Σ altura` dos copes `esq`/`dir`.
- Barra curta (`recuo + recorte ≥ comprimento`): não lança exceção. O cálculo segue sem o limite, e o resultado sai com `ok = False`, `motivo = "barra curta demais para a ligação"` e aviso. No grupo, o membro traz `motivo`.

### G2 rodada 1

- `pontas.BarraCurta(ValueError)` é a exceção de barra curta. `com_pontas` captura a classe, não o texto.
- Comprimento livre (grupo): `dist(nós) − desloc_i − desloc_j` (params da barra, m). Desse valor ainda sai o recuo da outra ponta, tirado do projeto do grupo dela. A barra é curta quando `recuo_i + recuo_j (+ recorte) ≥ comprimento livre`.
- O grupo passa `contexto_pontas.perfil_apoio`: a outra barra do nó de maior altura. Se o tipo pede apoio e não há perfil, sai o aviso `apoio sem perfil: ponta aproximada`.
- Os copes `sup`/`inf` da ponta vão para o cálculo dos flexíveis (`chapa_simples_alma`, `cantoneira_dupla_alma`, `viga_viga_recorte`) como `recorte_*_h/_c`. Valem o maior entre a entrada e a ponta. No `viga_viga_recorte`, altura e comprimento valem `max(Gerdau 3.3, cope da ponta)`. Os mesmos valores voltam para a ponta (`geometria.pontas`), então o desenho é igual ao cálculo, do lado conservador. O exemplo (W310x38.7 em W250x73, V = 100 kN) passa de 1,046 a 1,197 porque o cope da ponta é mais comprido. Verificações que passam a rodar na viga recortada:
  - início de escoamento por flexão na seção do fim do recorte (NBR 8800 5.4 / 6.5; Gerdau 3.4.2);
  - flambagem local da alma recortada (AISC Manual 13ª ed. p. 9-7, Cheng–Yura; sem item na NBR);
  - colapso por rasgamento com a altura de alma reduzida (NBR 8800 6.5.6).
- Exemplo (teste `TestCopeNoCalculo`): W310x38.7 na alma de W250x73 (tf = 14,2 mm), V = 100 kN.
  - Na mesa do pilar, sem recorte, passa (chapa simples 0,934; cantoneira dupla 0,914).
  - Com `cope_folga_alt` = 40 mm, a ponta dá dois copes de 54,2 mm. O início de escoamento da seção recortada chega a 1,21 e a ligação reprova.
- Gusset e nó de treliça: o recuo automático nunca fica antes da ponta da diagonal (face do apoio + folga). A ponta desenhada é o recuo usado, então a seção de Whitmore, a flambagem do gusset e as bordas seguem a ponta.
- Placa de base: o resultado do caminho de triagem parcial (`_placa_pior`) também traz `geometria.pontas`.

### Dívida

- Flange plate e emendas: a distância da borda até a ponta da barra (furos e soldas contra o plano de corte) não é conferida pela ponta. Os módulos continuam com a geometria própria.
- Comprimento do cope no G1 (`bf_apoio/2 + folga`, medido do plano de corte) sai maior que o do Gerdau 3.3 (`bf/2 − tw/2 + 10 − folga`, medido da ponta). O `viga_viga_recorte` usa o maior dos dois, do lado conservador. Ainda falta alinhar as duas regras, para não recortar mais que o necessário.
