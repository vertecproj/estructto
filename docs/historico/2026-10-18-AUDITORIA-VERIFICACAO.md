> Histórico (18/10/2026). Correções já aplicadas no código; diff em `2026-10-18-AUDITORIA-correcoes.diff`. Caminhos citados são relativos à pasta `ftool`.

# Auditoria do motor de verificação (18/10/2026)

Origem: comparação do memorial de `guincho-coluna-escorada` com cálculo manual (o programa aprovava 3 itens que o cálculo
manual reprova) e revisão do memorial em 7 itens. Regra adotada: **nenhuma verificação passa por omissão**.

Modelo de teste: `exemplos/guincho-coluna-escorada-teste.estructto` (coluna Ø114,3×7,30, escora 50×30×2,00, carga giratória,
ψ = 1,15). PNGs do memorial: `exemplos/memorial-guincho-teste/`. Testes: `codigo\portico\compilar.ps1 -Testes` (149, todos passam).

Gravidade: **CRÍTICO** = pode aprovar algo inseguro · **ERRO** = número errado · **AVISO** = faltava declarar.

| # | Local | Categoria | Gravidade | O que acontecia / exemplo | Correção | Status (teste) |
|---|---|---|---|---|---|---|
| 1 | `Verificador.Barra` | 2 hipótese 2D | CRÍTICO | Só flambagem no plano. Coluna fora do plano: λ = 2·5000/37,92 = 263,7 > 200, não aparecia | `Flambagem.Calcular`: dois planos sempre; governa o pior; blocos 4a/4b | ✔ "P1 coluna fora do plano" |
| 2 | `Barra.TravFora` (novo) | 6 default inseguro | CRÍTICO | Não existia travamento fora do plano | Propriedade "Travamento fora do plano (nós)": ids, `pontas`, `todos`; vazio = **nenhum** (só apoios) | ✔ "P3 escora... K = 2" / "travada no nó 2" |
| 3 | `Verificador`, memorial Dados | 3 eixo | CRÍTICO | Escora 50×30: memorial mostrava só Ix = 97 480; flambagem fora do plano precisa de Iy = 43 680 (r = 12,1, λ ≈ 389) | No plano = eixo de "Fletir no eixo fraco"; fora = o outro; Dados com Ix, Iy, rx, ry, orientação | ✔ "P2 escora: inércia no plano/fora" |
| 4 | `Analise.ResolverCasos`, `AplicarResultado`, `No.Rotativa` (novo) | 7 inversão | CRÍTICO | Escora só tracionada (+3,47 kN) → "não há flambagem". Com o guincho a 180° ela comprime | Nó com "Carga giratória": resolve 0° e 180° (Fx, Mz invertidos) e faz envoltória | ✔ "P3 posição 180°: escora comprimida" |
| 5 | `Verificador.Barra` | 1 passa por omissão | CRÍTICO | Barra sem compressão: flambagem "—" e selo verde | `AvisoInversao` quando sem compressão e (carga giratória ou sem combinações); selo "⚠ PASSA COM AVISO"; envoltória limpa o aviso se alguma posição comprime | ✔ "P3 barra pendurada... aviso" |
| 6 | `Verificador.Barra` | 8 limite esquecido | CRÍTICO | KL/r ≤ 200 era só aviso (o motor dizia "passa") | Esbeltez entra no aproveitamento: comprimida ≤ 200; só tracionada L/r ≤ 300 | ✔ "10 KL/r > 200... NÃO PASSA" |
| 7 | `PlacaCalc.Calcular` | 4 local × global | ERRO | Placa do nó 4 usava eixos da barra: V = 0,01 kN; real \|Rx\| = 1,78 kN | Esforços nos eixos da PLACA a partir da reação do nó; placa no plano do apoio (piso/teto/parede, eixo global mais próximo) | ✔ "P4 placa nó 4: V = \|Rx\|", "placa na parede" |
| 8 | `PlacaCalc` | 4 / 1 | CRÍTICO | Barra inclinada sobre a placa: chapa e solda calculadas como perfil perpendicular | `Inclinada` (> 2°): chapa e solda "⚠ NÃO VERIFICADO", placa não aprovada | ✔ "P4 barra inclinada..." |
| 9 | `Flambagem` (novo) | 5 malha | ERRO | Balanço de 1 m no topo da coluna: K = 2 → KL = 2 m (dependia da divisão) | Flambagem por barra contínua (autovalor, ~24 elementos internos); só pontos com condição contam | ✔ "P5 coluna em 2/5 trechos", "balanço em 2/5/10" |
| 10 | `Verificador` | 6 default / P-Δ | ERRO | K = 1 "pontas travadas" com o nó 2 deslocando 0,624 mm | Nó travado por outra barra = mola com a rigidez do resto da estrutura (escora: 3537 kN/m); coluna no plano K = 1,07 (Le = 4,30 m) | ✔ "P6 ... travamento elástico" |
| 11 | `Flambagem` | 6 default | AVISO | Teórico biengastado K = 0,5 seria contra a segurança | K ≥ 1 entre travamentos (não conta com engaste perfeito); K teórico mostrado | ✔ "biengastada: K adotado = 1" |
| 12 | `Verificador` | 7 estabilidade | ERRO | N/A comparado com fy/n; sem interação com flambagem | Bloco 5: n·Nc/Ncr,mín + B·n·M/(W·fy) ≤ 1, B = 1/(1 − nNc/Ne), Cm = 1 | ✔ "7 interação" |
| 13 | `PlacaCalc` | 8 limite esquecido | CRÍTICO | Borda comprimida: C = ΣT − N concentrado a 102,9 mm da face; t não verificado (12,1 mm > 8 mm) | t_C = √(6·C·e·n/(B·fy)); t necessária = máx(t_T, t_C) | ✔ "chapa, lado comprimido" |
| 14 | `Solda` | 8 limite esquecido | CRÍTICO | Perna 3 mm em tubo 7,3 / chapa 8 (mínimo NBR 8800 Tab. 10 = 5 mm) aprovada | `PernaMinimaNorma` pela parte mais fina; `Minima()` nunca abaixo; abaixo do mínimo ou maior que a parede = não passa | ✔ "solda: perna mínima" |
| 15 | `Solda`/memorial | 10 coerência | AVISO | Faltava o fator de segurança equivalente | n_equiv = 0,577·FEXX/τ (FEXX = τadm/0,30) no memorial. N/A e Mc/I têm a mesma direção (normal à garganta, ao longo da barra): somam na fibra mais afastada; mantido | ✔ "solda: n equivalente" |
| 16 | `PlacaBase.TipoCarga` (novo), `Chumbadores` | 6 default inseguro | CRÍTICO | n = 3 (estática) por omissão, também para guincho | n pelo tipo de carga (mínimo da faixa Walsywa); automático = dinâmica com carga giratória, senão **variável** (nunca estática por omissão); aviso se n digitado < faixa | ✔ "P7 ..." |
| 17 | `Modelo.Psi` (novo), memorial | 6 / AVISO | AVISO | ψ = 1,15 embutido na carga, invisível | "Coef. dinâmico ψ já incluído": aparece nos Dados do equipamento; aviso se há carga giratória e ψ não informado. Não é reaplicado | memorial pág. 4–5 |
| 18 | `PlacaCalc` | 1 passa por omissão | AVISO | Carga aplicada no próprio nó da placa sumia das fixações | Aviso | — (texto) |
| 19 | memorial 4) | 10 coerência | ERRO | Memorial recalculava Imín por conta própria | Memorial lê `Verif.NoPlano/ForaPlano` (mesmo número que decide o selo) | ✔ PNG |
| 20 | `Verificador` | 2 hipótese 2D | AVISO | FLT, torção, 2ª ordem global, vento lateral, pressão no concreto, punção: não declarados | Seção 9.5 Limitações | PNG pág. 13 |
| 21 | `Chumbador` | 9 unidades | — | kgf → kN ×0,00980665 conferido; cm⁴ → mm⁴ (×1e4) e kN·m → N·mm (×1e6) conferidos no PNG | sem mudança | — |
| 22 | `Dimensionar` | 1 | AVISO (aberto) | Soldas de ponta de barra (aba Dimensionar) não sabem a espessura da peça de destino: perna mínima só pela parede do perfil | pendente: pedir espessura da peça | aberto |
| 23 | `Flambagem` | 2 | AVISO (aberto) | Compressão uniforme = a maior da barra contínua (conservador para barras com N variável) | declarado em 9.5 | aberto |

## Resultados de modelos antigos que mudam (e por quê)
- **Qualquer barra comprimida sem apoio ou travamento declarado fora do plano** (vigas de pórtico comprimidas, banzos e
  diagonais de treliça) passa a ser verificada fora do plano com o comprimento da barra contínua inteira, ou dá
  "sem travamento (mecanismo)" → **NÃO PASSA até declarar** o travamento. É o padrão do lado seguro pedido.
- **KL/r > 200** (e L/r > 300 em barra só tracionada) agora reprova no motor, não só no selo do memorial.
- **Balanço no topo de coluna**: K não é mais 2 sobre o trecho; usa a barra contínua (pode subir ou descer).
- **Barra entre nós travados por outras barras (pórticos deslocáveis)**: K pela rigidez real do resto; pode passar de 1.
- **Chumbador sem coeficiente digitado**: n = 4 (variável) ou 7 (com carga giratória) em vez de 3.
- **Placa**: espessura pode subir (borda comprimida); barra inclinada não aprova mais chapa/solda; placa de parede usa os eixos da parede.
- **Solda**: pernas abaixo da Tabela 10 ou maiores que a parede reprovam.
- **Barra com compressão e momento**: novo critério de interação pode governar.
- **Modelo sem combinações**: barras só tracionadas mostram "⚠ PASSA COM AVISO".
