> Histórico (19/10/2026): prompt para a IA da planilha sobre chumbadores. O catálogo e o critério dos químicos já foram
> aplicados por script (ver docs/CHUMBADORES.md); a parte dos cones sobrepostos (ψ) ainda está pendente na planilha.

## 3. Prompt para a IA do Excel

Copie o bloco abaixo e envie junto com este arquivo, o `walsywa_fix_mec.pdf` e o `PADRAO-MEMORIAL.md`.

```text
Você vai padronizar a verificação dos CHUMBADORES na planilha "Coluna escorada para guincho de coluna" (base da coluna
e pé da escora) pelo catálogo Walsywa "Fixação Mecânica" (anexo), do mesmo jeito que o programa Estructto faz, para os
dois darem o mesmo resultado. Siga o PADRAO-MEMORIAL.md anexo para o memorial (Dados com origem, fórmula → números →
resultado → limite → aproveitamento → selo). Nenhuma verificação pode passar por omissão: o que não for verificado
aparece como ⚠, nunca como ✓.

1. ENTRADAS NOVAS (células de entrada, aparecem na tabela de Dados com a origem):
   - Linha e diâmetro do chumbador (lista do catálogo: CB, CBPL, CBE, CBEPL, WB), P = profundidade de embutimento,
     Tu e Vu em kgf (cargas médias de ruptura, concreto 20 MPa, IPT), furo, torque.
   - Tipo de carga: estática / variável / dinâmica / choque. Para o guincho: DINÂMICA.
   - Grau de risco: baixo / alto. Para o guincho: ALTO (a falha derruba o equipamento com carga suspensa).
   - fck do concreto (aviso se < 20 MPa: as cargas do catálogo são de C20).
   - Distância do chumbador à borda do concreto (medida na obra).

2. COEFICIENTE DE SEGURANÇA = tabela da pág. 07 do catálogo (NÃO é faixa: são duas colunas):
   baixo risco: estática 3, variável 4, dinâmica 7, choque 5
   alto risco:  estática 5, variável 5, dinâmica 15, choque 10
   Guincho = dinâmica + alto risco = 15. Se o usuário digitar outro n menor, mostrar ⚠ com o valor da tabela.
   Hoje a planilha usa nch = 5 "carga variável": trocar.

3. FORÇAS NOS CHUMBADORES nos eixos da PLACA (normal ao concreto), a partir das reações do apoio:
   - Base da coluna: T = Mb/(s·nb/2) + N/nb (N de tração positivo; compressão alivia). Fazer nas posições 0° e 180° do
     guincho e usar a pior. Corte V = |Rx|/nb.
   - Pé da escora (placa na laje): T = F·a/Ls/nb (só quando a escora está TRACIONADA) e V = F·r/Ls/nb (nas duas posições).
   - Conferência no memorial: nb·V = reação horizontal; nb·T (todos tracionados) = reação vertical.

4. VERIFICAÇÃO, nesta ordem, com os números substituídos:
   a) Tu(kN) = Tu(kgf)·0,00980665 ; Vu(kN) = Vu(kgf)·0,00980665
   b) interação: (T/Tu)^(5/3) + (V/Vu)^(5/3) = S ; n_obtido = S^(−3/5) ≥ n ; aproveitamento = n/n_obtido
      (nota: a equação 5/3 é hipótese usual para expansivos; o catálogo Walsywa não traz interação)
   c) cones sobrepostos: se o espaçamento s < 3·P, NÃO reprovar; reduzir Tu por ψ = A/(nt·9·P²), A = área da união dos
      quadrados 3P × 3P centrados nos chumbadores tracionados (para 2×2: A = (s + 3P)²). Usar Tu,ef = ψ·Tu em b).
      Reprovar só se s < 6·d (mínimo para instalar). Borda do concreto ≥ 1,5·P: ⚠ conferir na obra.
      Conferência: 4 × WB 5/8" hef 148 a 220 mm → ψ = 0,559.
   d) escolha automática: testar todos os chumbadores do catálogo (menor rosca, depois menor P), com 4, depois 6, depois 8
      chumbadores, e ficar com o primeiro que passa em b) e c).
   e) selo verde só se b) e c) passam.

5. AVISOS / LIMITAÇÕES no memorial:
   - cargas médias de ruptura de ensaio (IPT, C20); o fabricante recomenda confirmar com ensaio de carga no local;
   - carga dinâmica em chumbador expansivo: reaperto periódico; considerar químico ou chumbador concretado;
   - pressão no concreto, punção e espessura da laje não verificadas.

6. CONFERÊNCIA com o Estructto: lance a mesma placa (nb, s, borda, chumbador) e confira T, V, n_obtido e o selo; devem
   bater em 3 algarismos. Entregue: lista das mudanças na aba, o memorial novo em PDF e a tabela "item | antes | depois".
```
