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

## Limites atuais

- `angulo`: tipo reservado, ainda não gerado.
- `esquadria`: só par de tubos.
- `boca_peixe`: ramal perpendicular ao receptor.
