# Modelos de exemplo

Gerados por `python exemplos/gerar_exemplos.py` (a partir de `software/`), saída determinística. Não edite os `.json` à mão: altere o script e gere de novo. Eixos globais: X, Y horizontais, Z para cima; kN, m. O `nome` de cada modelo é a descrição mostrada em `projeto.exemplos()`.

| Arquivo | Descrição |
|---|---|
| `viga_biapoiada.json` | W360x51, A-572 GR.50, vão de 6 m, G1 (peso próprio + 5 kN/m) e Q1 (10 kN/m) |
| `portico_aco.json` | Pórtico plano XZ de 2 vãos de 6 m e 1 pavimento de 4 m (pilares W310x38.7, vigas W360x51), G1, Q1 e vento lateral manual V1 |
| `galpao_aco.json` | Galpão de 20 m de vão, pilar de 6 m, cumeeira a 8 m, 5 módulos de 6 m, pilares W360x51, vigas HP250x62, terças Ue 250x85x25x3,00 rotuladas, contraventamento em tubo circular 219,1x6,35 (diagonais-treliça nos vãos extremos) |
| `edificio_concreto.json` | Edifício de 3×2 vãos de 5 m, 4 pavimentos de 3 m, C30, pilares 25×50 cm (50 cm em X), vigas 20×50 cm, vento e sismo configurados |

As cargas de norma vêm de `acoes.cargas` (NBR 6120:2019): concreto armado 25 kN/m³ (5.3, tabela 1), escritório (sala de uso geral) 2,5 kN/m² (6.2, tabela 10), cobertura leve com inclinação ≥ 3 % 0,25 kN/m² (6.4). Os demais valores são hipóteses do exemplo, não de norma.

## Valores de conferência (cálculo manual)

### viga_biapoiada

Apoio fixo em `n1`, móvel em `n2`. Seção do catálogo (W360x51): A = 64,80 cm², Iy = 14 221 cm⁴ (eixo forte); aço E = 200 GPa, γ = 77 kN/m³.

- Q1: q = 10 kN/m, L = 6 m. M_máx = qL²/8 = 10·6²/8 = **45,00 kN·m** (meio do vão); reações qL/2 = **30,00 kN**; flecha 5qL⁴/(384·E·I) = 5,93 mm.
- G1: g = 5 + 77·A = 5 + 0,4990 = 5,4990 kN/m. M_máx = gL²/8 = **24,75 kN·m**; reações gL/2 = **16,50 kN**.
- Verificação (NBR 8800, ELU, Lb = 6 m, Cb pelo diagrama): taxa de interação 0,82 (item 5.5.1.2, combinação 1,4·G1 + 1,5·Q1), aprovada.

### portico_aco

- V1 (10 kN em +X no nó `N1-0-0`): ΣRx = **−10,00 kN** (soma das reações horizontais nas 3 bases).
- Q1 (5 kN/m em 2 vigas de 6 m): ΣRz = **60,00 kN**.
- G1: ΣRz = 8·12 + peso próprio = 96 + 77·(3·4·A_pilar + 12·A_viga) = 96 + 10,58 = **106,58 kN**.
- Verificação: nenhuma barra reprovada, taxa máxima 0,30 (viga VX1-0-0).
- Uy, Rx e Rz de todos os nós estão restringidos (pórtico plano, evita mecanismo fora do plano).

### galpao_aco

- Q1 = 0,25 kN/m² (NBR 6120 6.4) sobre 20 m × 30 m de projeção horizontal: ΣRz = 0,25·20·30 = **150,00 kN**. O carregamento está aplicado às vigas inclinadas, com 6 m de largura de influência (3 m nos pórticos de extremidade), por metro de projeção horizontal convertido para metro de barra (× cos θ, tan θ = 2/10).
- G1: só peso próprio: ΣRz = 77·(12 pilares·6 m·A_W360x51 + 12 vigas·10,198 m·A_HP250x62 + 15 terças·6 m·A_Ue + 4 DC·11,832 m·A_tubo + 4 DP·8,485 m·A_tubo) = 35,92 + 75,01 + 9,36 + 15,47 + 11,09 = **146,85 kN** (6 pórticos × 2 pilares; 6 pórticos × 2 águas; 5 vãos × 3 linhas de terça; 4 diagonais de cobertura e 4 de parede). O peso do telhamento e o vento não estão incluídos (a definir pelo usuário).
- As vigas são HP250x62 porque a água de 10,2 m não tem travamento intermediário (terças só no beiral e na cumeeira): KL/r = 10,2/0,0613 = 166 ≤ 200 (NBR 8800 5.3.4); um W360x44.6 daria 271 e reprovaria. As terças são Ue 250x85x25x3,00 (ry = 3,07 cm → 6/0,0307 = 195 ≤ 200): as da cumeeira trabalham como escoras do contraventamento e ficam levemente comprimidas. Verificação: nenhuma barra reprovada, taxa máxima 0,48 (terça T9).
- É um modelo 3D real: só as bases têm apoio (engaste). `contraventamento: true` cria `DC1…DC4` (cobertura: beiral→cumeeira de cada água, ligando o pórtico 0 ao 1 e o 5 ao 4) e `DP1…DP4` (parede: base→beiral em x = 0 e x = 20 m, nos mesmos vãos), todas com `trelica` (só esforço axial). Uma carga em Y num nó do beiral aparece inteira nas reações: ΣRy das bases = −Fy (conferido em `test_gerador`).
- Sem `contraventamento` o gerador não cria diagonais e emite aviso (cada pórtico só resiste a Y pela flexão dos pilares fora do plano). Com apoio `rotula` e sem contraventamento, rx fica preso nas bases (senão seria mecanismo); com contraventamento a base `rotula` só prende ux, uy, uz.

### edificio_concreto

- Painéis de 5×5 m. G1 de área = laje de 12 cm (0,12·25 = 3,0 kN/m²) + revestimento de 1,0 kN/m² (hipótese) = 4,0 kN/m²; Q1 de área = 2,5 kN/m². Cada painel descarrega q·a·b/(2(a+b)) por metro nas 4 vigas de borda (conserva a carga total).
- Q1: ΣRz = 2,5·15·10·4 = **1 500 kN**.
- G1: lajes e revestimento 4,0·150·4 = 2 400 kN; pilares 12·(4·3 m)·0,25·0,50·25 = 450 kN; vigas (9 de 5 m em X + 8 de 5 m em Y por pavimento = 85 m)·4·0,20·0,50·25 = 850 kN. ΣRz = **3 700 kN**.
- `config.vento`: V0 = 35 m/s, S1 = 1,0, categoria IV, classe B, S3 = 1,0, dimensões lx = 15 m e ly = 10 m. `config.sismo`: zona 0 (dispensado). Q1 com `subtipo` "escritorio" (ψ da NBR 8681).
- Verificação (sem vento nem sismo): nenhuma barra reprovada, taxa máxima 0,57 (VY1-1-0).
- Os casos de vento e sismo são criados por `gerar_acoes`; o arquivo traz só G1 e Q1.
