# Chapa de topo com esforços fora do plano forte (Fy, Mz, Mx)

26/10/2026, pedido do usuário: o projetista tem os 6 esforços da ponta da barra e a chapa de topo (ajustada e estendida)
precisa verificá-los, não só avisar. Código: `codigo/motor/prossen_estrutural/aco/ligacoes_rigidas.py` (`_chapa_topo`).
Testes: `codigo/motor/testes/test_chapa_topo_3d.py` (conta à mão no cabeçalho).

## Eixos e chaves

| Tela | Chave no motor | Sentido |
|---|---|---|
| Fx | `N` | eixo da viga (tração +) |
| Fy | `Vz` | horizontal, fora do plano da alma |
| Fz | `V` | vertical, plano da alma |
| Mx | `T` | torção (eixo da viga) |
| My | `M` | eixo forte |
| Mz | `Mz` | eixo fraco (vertical) |

## Critério (elástico e conservador)

1. **Parafusos ao cortante**: grupo elástico no plano da chapa, em torno do centroide dos parafusos (zg: com a chapa
   estendida só de um lado o grupo é assimétrico). Para o parafuso em (y, z), com Ip = Σ(y² + (z − zg)²):
   fy = Fy/n − Mx·(z − zg)/Ip, fz = Fz/n + Mx·y/Ip, e Vb = máx √(fy² + fz²). Sem Fy nem Mx, o resultado é V/n como antes.
   Vb entra no cisalhamento (6.3.3.2), na pressão de contato (6.3.3.3) e na interação tração + cisalhamento (6.3.3.4).
   Na pressão de contato, lf é o menor entre furos, borda inferior, borda superior e, com Fy, borda lateral.
2. **Mz**: binário nas duas colunas de parafusos com braço g. O contato na borda da chapa não é considerado, o que é
   conservador. Cada parafuso recebe dT = 2·|Mz| / (g·n) a mais. Nas linhas tracionadas pelo My, dT soma-se ao T do
   T-stub (chapa, alavanca e mesa do pilar). As linhas comprimidas recebem a verificação "tração por Mz" com a
   interação com o cortante.
3. **Fy na chapa**: escoamento e ruptura por cisalhamento na seção vertical da chapa (altura × tp, descontados os
   furos de cada linha), conforme 6.5.5.
4. **Viga**: interação 5.5.1.2 com N, My e Mz (NRd = A·fy/γa1 na seção da ponta, sem flambagem; MRd = min(Z, 1,5·W)·fy/γa1):
   N/NRd ≥ 0,2 → N/NRd + 8/9·(My/MRd,x + Mz/MRd,y) ≤ 1; senão N/(2·NRd) + (…) ≤ 1.
5. **Solda das mesas**: normal Ff + 3·|Mz|/bf (cada mesa leva Mz/2 no próprio plano, σ = 6·(Mz/2)/bf² por comprimento)
   e cisalhamento |Fy|/2 + |Mx|/(H − tf). Filete: as duas parcelas somadas em módulo contra 2·bf·FwRd (conservador).
   Penetração total (tabela 8): normal contra bf·tf·fy/γa1 e cisalhamento contra 0,6·bf·tf·fy/γa1.

## Posição da chapa estendida

`estendida`: `superior` = estende em cima, alinhada embaixo (CBCA LMPA); `ambas` = centralizada (inversão de momento);
`inferior` = estende embaixo, alinhada em cima (para M < 0). `inferior` espelha `superior` em z (mesmo MRd com M
trocado de sinal; teste `test_inferior_espelha_superior`). Na tela: "Centralizada", "Alinhada em cima", "Alinhada embaixo".

## Revisão independente (26/10/2026)

O verificador apontou e foi corrigido: Ip tomado no eixo da viga em vez do centroide (torção ~15 % menor); penetração
somando cisalhamento à normal contra fy; interação da viga sem N; pressão de contato sem a borda superior/lateral.

## Limites

- O pilar (painel, mesa, enrijecedores) continua verificado só com My e N. O aviso do motor diz isso.
- Não há empenamento (torção não uniforme): a torção é tratada só como binário nas mesas e no grupo de parafusos.
- A flexão da chapa de topo pelo Mz entra apenas pelo aumento do T de cada parafuso no T-stub. Não há charneira
  vertical própria.
