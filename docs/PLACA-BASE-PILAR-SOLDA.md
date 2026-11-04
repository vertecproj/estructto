# Placa de base: pilar na base, solda e "Dimensionar tudo" (04/11/2026)

Caso real que motivou: pilar **Tubo 175×175×9,50**, N = −63,22 kN, V = 0, M = 89,51 kN·m (eixo forte), placa
300 × 230 × 19 com 4 × WB 5/8" hef 59 (Walsywa, mecânico), fck 20 MPa, chapa fy 250 MPa, E70XX. A janela mostrava
fixação 50×, chapa 3,3× e solda "auto (25 mm)" numa parede de 9,5 mm; "Dimensionar tudo" e "Sugerir" não achavam
nada e a dica "Aumente a placa (Ajustar tamanho)" voltava ao mesmo 300 × 230 (tamanho mínimo pelos espaçamentos).

Código: `codigo/motor/prossen_estrutural/aco/placa_base.py`, `codigo/motor/prossen_estrutural/projeto.py`
(`placa_base_verificar`, `placa_base_dimensionar`), `codigo/Estructto/Solda.cs`, `PlacaMotor.cs`, `JanelaPlaca.cs`.
Testes: `codigo/motor/testes/test_placa_pilar_solda.py` e 4 testes de solda em `codigo/portico/src/Testes.cs`.

## 1. Pilar na base (NBR 8800:2008 5.5.1.2)

Nada no fluxo da placa verificava o próprio pilar. Se a seção do pilar não aguenta N + M no pé, nenhuma placa resolve.

- `placa_base.resistencia_pilar(secao, fy)`: funções já existentes de `aco/nbr8800.py`:
  - NRd = Q·A·fy/γa1 (5.3.2 com **χ = 1**: na base vale a seção; a flambagem da barra é da verificação do pilar). Q pelo
    anexo F (`fator_Q`).
  - MxRd = `flexao_x(sec, mat, Lb = 0)` e MyRd = `flexao_y`: classe do perfil pelo anexo G (FLM/FLA; sem FLT com Lb = 0),
    ≤ 1,5·W·fy, /γa1.
  - E = 200 GPa (4.5.2.9).
- `verificar_pilar_base`: `nbr8800.interacao` (5.5.1.2): NSd/NRd ≥ 0,2 → N/NRd + 8/9·(Mx/MxRd + My/MyRd); senão
  N/(2·NRd) + Mx/MxRd + My/MyRd. Mx = My local do modelo (eixo forte), My = Mz local (eixo fraco); os dois momentos
  entram juntos (`casos_biaxiais` passa `M_forte` e `M_fraco`).
- fy: `dados.fy_pilar` (kN/m²) ou, pelos membros, o fy do material da barra no modelo. Sem fy (esforços + seção sem
  `fy_pilar`), o pilar não é verificado (`pilar` = null).
- Resultado: verificação `pilar: flexo-compressão na base` no resultado da placa (reprova a placa), fora dos 3 passos da
  janela, e `pilar` = {ok, taxa, combinacao, eixo, mensagem, verificacoes} em `/api/placa_base/verificar`. Reprovado:
  "O pilar não resiste aos esforços na base (taxa X): troque o perfil ou o aço — nenhuma placa resolve." Isso aparece no
  topo da janela, num chip "Pilar xx%" e na aba Cálculo.
- Tubo 175×9,5: Z = 390,7 cm³, compacto (b/t = 15,4 < 1,12·√(E/fy)) → MRd = Z·fy/1,10 = **88,8 kN·m (fy 250)** e
  **106,6 kN·m (fy 300)**. NRd = 1394 / 1673 kN. Taxa = 63,22/(2·NRd) + 89,51/MRd = **1,031 (fy 250, reprova)** e
  **0,859 (fy 300)**.

## 2. Solda pilar–placa

### 2.1 Filete automático nunca acima da parede

`perna_minima_contorno` / `Solda.Minima` escolhiam a menor perna da tabela que passava na tensão sem olhar h ≤ t, e daí
vinha "auto (25 mm)" numa parede de 9,5 mm.

- `pernas_filete(t)` / `Solda.PernasAte(t)`: as pernas da tabela ≤ t mais a própria t arredondada para baixo a 0,5 mm
  (9,5 → …, 8, **9,5**). O limite prático h ≤ t já existia na verificação (`solda: limite prático da parede`).
- Escolha automática (solda = 0): `perna_minima_contorno(..., ate_parede=True)` / `Minima(..., ateParede: true)`. Se
  nenhuma perna que cabe passa, usa a maior que cabe. A verificação reprova com o nome
  `solda: sem filete possível (use penetração total)` e o aviso
  "Sem filete possível: … filete ≤ parede não desenvolve a parede (garganta 0,707·t). Use solda de penetração total."
- `perna_minima_contorno` sem `ate_parede` continua como antes: é só a referência `h_min`, e o teste antigo dela não muda.
  A tabela "Soldas" de `Dimensionar.cs` (pontas de barra) também não mudou.
- Diagnóstico (`diagnosticar_solda`, `Solda.Diagnosticar`): percorre as pernas que cabem (inclui 9,5). Sem solução,
  diz "sem filete possível" e manda usar penetração total (`penetracao` / `Penetracao` = true). A janela mostra o botão
  "Usar penetração total".
- Exibição: `Solda.Mm(h)` põe a casa decimal só quando existe (9,5 mm em vez de "10 mm").

### 2.2 Penetração total (`solda_tipo = "penetracao"`, `PlacaBase.SoldaPenetracao`)

Um filete com h ≤ t nunca desenvolve a parede, porque a garganta é 0,707·t < t. A saída é a penetração total (ou
nervuras, que não foram implementadas).

- NBR 8800 6.2.5.1, tabela 8: na penetração total, para tração/compressão normal ao eixo da solda e para cisalhamento,
  a resistência é a do **metal-base**. Aqui o metal-base é a **parede do pilar** (fy_pilar; sem ele, fy_chapa).
- `verificar_solda_penetracao`: |N|/NRd + |M|/MRd ≤ 1 com NRd e MRd de `resistencia_pilar` (no eixo da placa:
  `eixo_fraco` → MyRd). É a soma linear das tensões na parede, N/A + M/Z ≤ fy/γa1 numa seção compacta (MRd pela classe,
  W efetivo se não compacta). Mais cortante: |V| ≤ VRd (5.4.3).
- **Diferença em relação ao pedido "M/W"**: com o W elástico, o tubo 175×9,5 com fy 300 dá 10,3 + 283,0 = 293 MPa >
  272,7 MPa. Assim nenhum aço até ~325 MPa resolveria, embora a própria seção do pilar passe (5.5.1.2, 0,86). Uma solda
  de penetração total tem a resistência do metal-base, então usa o mesmo MRd da seção (plástico para seção compacta). O
  W elástico seria mais conservador que o próprio pilar.
- Limitação (em `LIMITACOES`): a face da chapa (tração na espessura, lamelar) não é verificada. Use chapa com fy ≥ o do
  pilar ou confira à parte.
- Tubo 175×9,5, M = 89,51: taxa N+M = 0,045 + 1,008 = **1,05 (fy 250)** e 0,038 + 0,840 = **0,878 (fy 300)**.

## 3. "Dimensionar tudo" e "Sugerir" (`placa_base_dimensionar`)

Antes, com chumbador de catálogo, a busca percorria só o mesmo tipo (mecânico ou químico) e os arranjos 2×2/2×3/3×3,
sem mexer em H, B ou hef e sem passar ao pré-concretado. Quando a fixação não passava, parava. O "Sugerir" (C#) fazia o
mesmo, sem pré-concretado e sem crescer a placa.

Agora "Dimensionar tudo" e "Sugerir" usam a mesma rota (`/api/placa_base/dimensionar`; `opcoes = 3` no Sugerir):

1. **Pilar** reprovado → `achou = false` com a mensagem do §1 (não finge solução). A **penetração total** também não
   depende da placa: se nem ela passa, a mensagem é "A solda pilar–placa não resiste nem com penetração total … nenhuma
   placa resolve".
2. **Geometrias**: a placa atual e as maiores, **H e B de 50 em 50 mm até 700 mm**, arranjos 2×2, 2×3 e 3×3 (≥ o atual,
   mais o atual). A borda é a atual. Para cada geometria, no caso governante, sai t_CBCA (não depende da fixação) e a
   chapa comercial ≥ t. Ficam fora as que não têm chapa comercial ou em que o concreto sob a placa não passa.
3. **Critério = mais leve**: as geometrias vão em ordem de massa da chapa H·B·t (empate: menos fixações).
4. Em cada geometria, as **famílias de fixação**: o **tipo atual primeiro** (preferência, não trava), depois os outros tipos
   de catálogo e o **pré-concretado** (tabela métrica a partir da classe atual; hef = o informado ou 12·Ø e, com a altura
   do bloco informada, também hef até o bloco, altura − 75 mm, cobrimento do ACI 318-19 20.5.1.3). Dentro da família vale
   a ordem da tela: "Mais leve" = menor diâmetro/classe, "Furo menor" = menor furo. Triagem: se a mais forte da família
   não passa na fixação do caso governante, a família é pulada nesta geometria (heurística marcada `ponytail:` no código).
5. **Completar** (`_completar_placa`): chapa comercial ≥ t_CBCA de todos os casos e solda (filete automático ≤ parede;
   eletrodo mais forte com perna que cabe; senão penetração total).
6. **Verificação final em todos os casos e nos dois eixos**: só entra solução com `ok` em tudo. O Sugerir junta até 3
   soluções com fixações diferentes.
7. Nada passa → `achou = false`: "Nenhuma placa até 700 × 700 mm passa … aumente o bloco de concreto ou reveja o pilar."

A janela aplica tudo o que o motor devolve (`JanelaPlaca.Usar`): fixação, H × B, hef, chapa, solda e tipo de solda. A
mensagem do "Ajustar tamanho" passou a dizer que é o tamanho MÍNIMO pelos espaçamentos e manda usar Dimensionar tudo.
A dica do Sugerir vazio mostra a explicação do motor e não aponta mais para o Ajustar tamanho.

Tempo: ~0,5 s com solução e ~1,5 s sem nenhuma solução (caso único; a triagem usa só o caso governante).

## 4. Resultados do caso do usuário

| | fy pilar 250 | fy pilar 300 |
|---|---|---|
| Pilar 5.5.1.2 | **1,03, reprova** | 0,86 |
| Placa 300 × 230 × 19, 4 × WB 5/8" hef 59 | fixação 51,9×, chapa 2,77×, solda "sem filete possível" (9,5 mm) | idem |
| Dimensionar tudo | "O pilar não resiste aos esforços na base (taxa 1,03): troque o perfil ou o aço — nenhuma placa resolve." | **placa 400 × 250 × 50,8**, 4 × Ø36 classe 8.8 pré-concretado, hef 12·Ø, solda de penetração total: fixação 0,92, chapa 0,82, solda 0,88, pilar 0,86 (39,9 kg de chapa) |

A investigação anterior (500 × 400 × 38,1, 2×3 Ø24 cl. 5.8) também passa, mas pesa 59,8 kg. Pelo critério "mais leve",
o motor fica com a 400 × 250 × 50,8 (39,9 kg).

Capturas: `55-placa-tubo175-fy250*.png` e `55-placa-tubo175-fy300*.png` (`ESTRUCTTO_CAPTURAS=placa`, ou só elas com
`ESTRUCTTO_CAPTURAS=placatubo`, que pula as telas iniciais; números em `55-placa-tubo175.txt`), modelos
`exemplos/base-tubo175-fy250.json` e `exemplos/base-tubo175-fy300.json` (pilar de 1 m engastado; carga G no topo que dá,
na ELU, exatamente os esforços do usuário).
