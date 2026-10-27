# Perfis compostos e disposição (v0.6 Frente D)

W9: U e Ue dobrados, perfil duplo na janela Disposição do perfil e NBR 14762 em [W9-COMPOSTO.md](W9-COMPOSTO.md).

## Arranjos (`secoes.secao_dupla`, spec `{"forma": "dupla", "base": ..., "arranjo": ...}`)
| arranjo | perfil base | forma |
|---|---|---|
| `caixao` | 2U, 2Ue (2L só soldado, s = 0) | abas para dentro, almas por fora (tubo) |
| `I` | 2U, 2L | costas com costas: almas juntas (afastadas s), abas para fora (2U → I/W; 2L → T) |
| `cruz` | 2L | estrela: quinas opostas, afastadas s nos dois sentidos |
| `lado_a_lado` | 2I | mesas afastadas s |
| `I_chapa` | I | chapa soldada sobre a mesa superior (`secao_I_chapa`) |

**"2U abas para fora" é o mesmo arranjo que "2U costas com costas"** (`arranjo = "I"`). Com dois U as abas só podem apontar
para fora quando as almas estão viradas uma para a outra. O afastamento s entre as almas cobre os dois nomes: s = 0 dá almas em
contato (I soldado), s > 0 dá almas afastadas com chapas espaçadoras/presilhas. No CYPE é o "doblete" (tipo 7/8/9 da peça).

## Verificação
- NBR 8800 5.3.4: l/r de cada perfil entre ligações ≤ ½ do maior KL/r do conjunto. Exige `l_ligacoes` e não aceita
  afastamento sem chapas espaçadoras; fora disso a barra reprova com aviso.
- Z em torno do eixo de simetria: cada perfil fica inteiro de um lado da linha neutra plástica, então Z = 2·A1·d. Não se soma o Z
  próprio de cada perfil, porque ele já está contido em A1·d.
- 2L em cruz: a flexão NÃO é verificada (só esforço normal); use a barra só como escora/tirante ou verifique a flexão à parte.
- I + chapa: flexão fora da tabela G.1, então NotImplementedError (reprova). Q é o do I; chapa mais larga que a mesa também reprova.

## Disposição do perfil (`params` da barra)
- `rot` é o giro (rad) e `cardinal` vai de 1 a 9 mais 10 (centroide). Os dois só afetam o desenho; a análise usa o eixo.
- `espelho: true` reflete a seção em torno do eixo local v (u → −u). Não é um giro de 180°: numa cantoneira, o L refletido e o L
  girado ocupam quadrantes diferentes.
  - No 3D e na exportação, `GeometriaPerfis.Espelhar` reflete o contorno.
  - No motor, `secoes.espelhar` inverte o sinal de Ixy, de x0 e do ângulo principal. A, I, W, Z, Iu/Iv e as resistências não
    mudam, porque a E.1.3 usa (x0/r0)². Por isso o cálculo não lê `espelho`.
  - Ressalva: isso vale enquanto a flexão de L for verificada pelos eixos geométricos. Se um dia for por eixos principais
    (u, v), o espelho troca o sinal do ângulo principal e o momento se decompõe diferente: aí o cálculo terá de ler `espelho`.
