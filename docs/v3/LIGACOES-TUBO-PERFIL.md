# Perfil soldado em tubo e barra que encosta sem nó

Duas mudanças no motor de ligações (`aco/ligacoes_modelo.py`, `aco/ligacoes_tubos.py`, `aco/pontas.py`).

## 1. Barra que chega numa coluna sem nó compartilhado

**Causa raiz.** A diagonal (ou viga inclinada) foi desenhada até a face da coluna, num nó próprio. A coluna passa
direto, sem nó nessa altura. A análise não liga as duas barras: a diagonal fica em balanço a partir da placa de base.
A identificação só olha as pontas de barra em cada nó. Por isso o nó do topo aparecia como "ponta livre: 1 barra, sem apoio".
A regra de identificação de barras inclinadas não tinha erro.

**Agora.**
- `encosto(modelo, nó)`: o nó tem uma barra só e não tem apoio. Ele fica no vão de outra barra de aço, a no máximo
  meia seção + 50 mm do eixo dela. → `{no, barra, ponto, t, distancia}`.
- O motivo no Gerenciador passa a ser: "b3 chega em b1 a 155 mm do eixo, mas b1 não tem nó aqui: as barras não estão
  ligadas na análise — ajuste: ligar n5 em b1".
- `identificar` devolve também `ajustes: {nó: encosto}`.
- `ligar_no(modelo, {no})` leva o nó ao eixo da barra e divide a barra nesse ponto, com `geradores_extra._Obra.dividir`.
  As cargas são reinterpoladas e as rótulas de ponta ficam só nos trechos das pontas. Rota: `POST /api/ligacoes/ligar_no`,
  em `MUTAM`: empilha o desfazer e descarta o cálculo.
  Depois do ajuste, o nó é identificado pelas regras de sempre (diagonal com pilar contínuo = `gusset_contraventamento`).
- No Gerenciador, a lista "Nós sem ligação" mostra o botão **Ligar em b1** quando há ajuste. O botão chama
  `DocumentoEspacial.LigarNo`, recarrega o modelo, redesenha a vista e re-identifica as ligações.
- Fica de fora: nó perto da *ponta* de outra barra (unir nós). Esse caso continua como "ponta livre".

## 2. `perfil_tubo_soldado`: perfil encostado em tubo

Tipo de nó e tipo de ligação novos, da família `tubo`. Liga uma viga ou diagonal (I/H, U, tubo retangular ou circular,
barra retangular) a um pilar ou banzo tubular (circular ou retangular). O caso tubo redondo em tubo redondo maior ou
igual continua `tubo_tubo_soldado`.

**Projeto padrão.** `dados_perfil_tubo` monta o projeto a partir das seções do modelo, com o ângulo θ e o giro. O grupo
já nasce com projeto e é dimensionado direto.

**Entradas (mm, °).**

| Grupo | Entradas |
|---|---|
| Perfil | `forma_ramal`, `h_ramal`, `b_ramal`, `tw_ramal`, `tf_ramal` (tubo: parede em tw e tf) |
| Tubo | `forma_receptor`, `D_receptor` (diâmetro, ou lado do tubo retangular na direção do perfil), `B_receptor` (largura da face; 0 = D), `t_receptor` |
| Ângulos | `theta`: ângulo entre os eixos. `giro`: giro do tubo em torno do perfil; 0 = tubo no plano da altura h |
| Solda | `b_solda`, `eletrodo`, `aco_ramal`, `aco_receptor` |

**Verificações e fontes.**

| Verificação | Critério | Fonte |
|---|---|---|
| Geometria | β = largura do perfil no tubo / largura do tubo ≤ 1 | validade geométrica |
| Solda | Filete em todo o contorno de contato. Solda tratada como linha no plano da seção do perfil (comprimento projetado, conservador). Normal: qn = N/Lw + M·z/I_forte + Mz·y/I_fraco. Cortante: qv = V/Lv_h ⊕ Vz/Lv_b + T·r/Ip, em que Lv = soma das projeções dos trechos na direção do cortante. Combinação: q = √(qn² + qv²) ≤ FwRd | NBR 8800 6.2.5 / tabela 8 (`ligacoes.resistencia_filete`). Método elástico da solda como linha, o mesmo critério de `modulo_sela`. Segmento reto: ∫y² ds = l(y1² + y1y2 + y2²)/3 |
| Metal base do tubo | Junto à solda | NBR 8800 6.2.5 / tabela 8 |
| Filete mínimo | Tabela 10, menor entre a chapa mais grossa do perfil e a parede do tubo | 6.2.6.2.1 |
| Filete máximo | Chapa mais fina do perfil | 6.2.6.2.2 |

**Não verificado.** A parede do tubo (plastificação, punção e cisalhamento; NBR 16239 / CIDECT) não é verificada, porque
o repositório não tem base para essas fórmulas. O resultado traz um aviso.

## Contrato de geometria para o 3D (Render3D* é de outro agente)

`verificar_ligacao("perfil_tubo_soldado", dados, esforcos)["geometria"]`, no referencial local do contrato:
- origem no nó; x = barra que chega, saindo do nó; z = altura h dela; y = z × x;
- eixo do tubo = (cos θ, sen θ·sen giro, sen θ·cos giro).

O motor já devolve `giro` no projeto do grupo. **O 3D não precisa girar nada**, ao contrário de `tubo_tubo_soldado`.

- `pontas[0]` (contrato de PONTAS-CONTRATO.md):
  - tubo circular: `plano` no fundo da sela (menor distância do contorno ao cilindro), `folga` 0 e
    `recortes = [{tipo:"boca_peixe", raio, eixo, angulo}]`. O 3D corta **o sólido do perfil (I, U, caixão…)** pelo
    cilindro de raio `raio` e eixo `eixo` que passa pelo nó. É o mesmo recorte do tubo–tubo, mas aplicado a qualquer seção.
  - tubo retangular: `plano` na face do tubo (normal oblíqua, `-n`), `recortes = []`. O corte plano já é o encaixe.
- `soldas[0]`: `{tipo:"filete", campo:"b_solda", perna (mm), pontos [[x,y,z] mm]}`. É uma polilinha fechada: o contorno
  da seção (I: 12 cantos; U: 8; caixão e retângulo: 4; tubo: 72) projetado ao longo de x até a superfície do tubo. Desenhar
  o cordão nessa polilinha.
- `chapas`, `parafusos` e `cantoneiras` vêm vazios.

**Pendências do render.** `Render3DMotor.TipoProposto` não conhece `perfil_tubo_soldado`. Os grupos já nascem com
projeto, então hoje caem no ramo genérico (`tl != null`), que manda `dados` do projeto e `perfis`. Basta tratar esse tipo
como o tubo–tubo: sem chapa, sem proposta local.
