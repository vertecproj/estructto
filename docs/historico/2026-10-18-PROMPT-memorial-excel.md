> Histórico (18/10/2026): prompt enviado à IA da planilha. Pendências que restarem estão em `docs/CHANGELOG.md`.

# Prompt para a IA da planilha (memorial "Coluna escorada para guincho de coluna")

Cole o texto abaixo para a IA que gera o memorial do Excel. Anexe junto o `PADRAO-MEMORIAL.md`
(`ftool\docs\PADRAO-MEMORIAL.md`) e o PDF atual do memorial do Excel.

---

Você vai corrigir a aba de cálculo e o memorial do Excel "Coluna escorada para guincho de coluna" para que eles sigam o padrão
de memorial anexado (PADRAO-MEMORIAL.md, o mesmo do programa Estructto) e para que os resultados batam com o Estructto.
Regra geral: **nenhuma verificação pode passar por omissão**. O que não é verificado tem de aparecer como ⚠ ou limitação
declarada, nunca como ✓. Não mude o que já está certo (carga N = (Q·ψ + Wg)·g, tensão |N|/A + |M|/W, Euler/Johnson,
interação do chumbador, conversões de unidade).

## A. Erros de cálculo (corrigir na aba de cálculo E no memorial)

1. **Escora sem travamento fora do plano (CRÍTICO).** Hoje: K = 1,0 "biarticulada", λ = 211,9. Fora do plano o topo da
   escora está preso ao nó da coluna, que também é livre fora do plano (a coluna é um balanço). O topo da escora NÃO está
   travado: escora engastada no pé e livre no topo, K = 2,0, λ = 2·Ls/rmin. Só use K = 1 se houver travamento real
   fora do plano no nó da escora (ex.: duas escoras em "V"), e diga isso no memorial como dado de entrada ("travamento
   fora do plano: sim/não"). Padrão = não (lado seguro).
2. **Flambagem da coluna no plano (ERRO, contra a segurança).** Hoje: K = 0,7 em L = a (engaste–apoio). Isso ignora:
   (a) o balanço de (L − a) acima da escora, que desestabiliza o trecho de baixo, e (b) a escora é um apoio ELÁSTICO
   (o nó se desloca u). O Estructto, com análise de flambagem da barra contínua inteira, dá K ≈ 1,56 sobre a = 2000 mm
   (λ ≈ 82,7). Corrija para: no mínimo K = 1,0 sobre a (não contar com engaste perfeito) e escreva que o balanço e o apoio
   elástico não foram considerados; melhor ainda, use K da barra contínua (coluna inteira L com apoio lateral elástico em a).
3. **Inversão da escora (CRÍTICO, guincho gira).** Você já verifica a escora à compressão com |F| (correto). Faça o mesmo
   para TUDO que depende do sinal: com o guincho a 180° a escora comprime, a coluna abaixo da escora fica com
   Nc = N − R·a/r (menos compressão) e o momento troca de sinal. Escreva no memorial as duas posições (0° e 180°) e use a
   pior para cada verificação.
4. **Coeficiente dos chumbadores.** Hoje nch = 5 "carga variável". Guincho é equipamento de içamento que gira: carga
   **dinâmica**, Walsywa 7 a 15. Use nch ≥ 7 ou justifique por escrito por que a carga é variável e não dinâmica.
   O memorial tem de mostrar o TIPO de carga escolhido e a faixa recomendada.
5. **Chumbadores da base: falta o esforço normal.** T = Mb/(s·nb/2) ignora a compressão Nc (alivia) e, na posição 180°,
   a tração. Use o esforço normal da posição crítica: T = Mb/(s·nb/2) + N/nb (N de tração positivo).
6. **Chumbadores do pé da escora: use a reação nos eixos da LAJE.** T = F·a/Ls e V = F·r/Ls está certo (já está nos eixos
   da laje). Mantenha, e acrescente a soma de conferência: nb·T = componente vertical de F e nb·V = componente horizontal.
   Com a escora COMPRIMIDA (180°) o pé não arranca, mas o corte continua: verifique as duas posições.
7. **Seção da escora.** Você usa A = 444 mm² e Imin = 61 812 mm⁴ (cantos vivos). O catálogo do Estructto (tubo com cantos
   arredondados) dá A = 429 mm², Iy = 58 660 mm⁴ (ry = 11,70 mm). Use o catálogo (cantos arredondados) ou diga no Dados que a
   seção é de cantos vivos (contra a segurança, ~3% a mais de A e ~5% a mais de I).
8. **Interação N + M com flambagem (falta).** A coluna é flexo-comprimida. Além de σ = N/A + M/W, acrescente
   n·Nc/Ncr,mín + B·n·|M|máx/(W·fy) ≤ 1, com B = 1/(1 − n·Nc/Ne), Ne = π²EI/Le² no plano, Cm = 1.
9. **Cisalhamento não verificado.** Está como limitação; o padrão pede o bloco: τ = Q/As ≤ 0,577·fy/n (tubo redondo
   As = A/2). Inclua para coluna (Q = R) e escora.
10. **Peso próprio da estrutura.** O equilíbrio ignora o peso da coluna e da escora (~0,7 kN no Estructto). Inclua, ou
    declare que foi desprezado.
11. **Placa de base e solda.** Hoje "não verifica espessura de chapa nem solda". O padrão pede: chapa pelo lado tracionado
    (t = √(6·T·e·n/(b·fy))) e pelo lado comprimido (C = ΣT − N na borda, t = √(6·C·e·n/(B·fy))), e solda de filete com
    perna mínima da NBR 8800 Tab. 10 pela peça mais fina (t ≤ 6,35 → 3 mm; ≤ 12,5 → 5 mm; ≤ 19 → 6 mm; > 19 → 8 mm). Se não
    for fazer agora, tem de aparecer como ⚠ NÃO VERIFICADO no resumo, não só na limitação. Pé da escora: barra inclinada
    sobre a chapa = chapa e solda NÃO VERIFICADAS (cordão chanfrado), avisar.

## B. Fora do padrão (formatação e coerência)

12. **Tabela de dados com colunas trocadas.** "Coeficiente de segurança n: unidade = material"; "fy: unidade = geometria";
    "L: unidade = geometria". A coluna Unidade tem de ter a unidade (—, MPa, mm) e a Origem a origem.
13. **Transição Euler/Johnson.** Use a do padrão, λ1 = √(2π²E/fy) = 125,7 (A36), não π·√(E/fy) = 88,9. Apague as notas
    "Adaptação ao padrão" e calcule direto com λ1.
14. **Selo e texto contraditórios.** "K·L/r = 211,9 ≤ 200" com selo vermelho. Escreva "K·L/r = 211,9 > 200" quando não
    passa. Vale para todo limite: o sinal (≤ / >) tem de concordar com o selo.
15. **Resumo × blocos.** O resumo mostra "tensão combinada 117,47 / 250" e "tensão axial 20,92 / 250", mas os blocos usam
    78,31 / 166,67 e 13,95 / 166,67. O resumo tem de repetir os MESMOS números dos blocos (σ e σadm = fy/n).
16. **Esbeltez como verificação.** Esbeltez > 200 é NÃO PASSA (não só aviso), e o resultado da barra tem de ter as
    colunas do padrão: Tensão | Cisalhamento | Flecha | Flamb. no plano | Flamb. fora | Esbeltez | N + M | Governa | Situação.
17. **Coeficiente dinâmico ψ.** Já aparece nos Dados (bom). Diga a origem: "NBR 8400, coeficiente de impacto de içamento".
18. **Flecha com o fator 1,5·(L − a)/a.** É uma aproximação: declare como aproximação e compare com a flecha da análise
    (Estructto: 6,98 mm; aba: 7,04 mm).
19. **Limitações.** Atualize a seção 8 com o padrão (seção 9 do PADRAO-MEMORIAL): pórtico plano, só posições 0°/180°, sem
    2ª ordem global, sem FLT, pressão no concreto e punção não verificadas, chapa/solda (se continuarem fora).

## C. Valores de referência do Estructto para conferir (mesma geometria: L = 3000, a = 2000, r = 1500, Ø114,3×8, 50×30×3, A36)

Com a carga lançada como Fy = −4,954 kN e Mz = −4,954 kN·m no topo e a escora em 0°/180° (ver a nota no fim):
- Escora: F ≈ 6,0 kN (análise elástica; a fórmula rígida 3M/2a dá 6,19 kN — diferença por o apoio ser elástico, u ≈ 0,32 mm).
- Momento na base: 2,22 kN·m (análise) × 2,48 kN·m (M/2, apoio rígido).
- Flecha no topo: 6,98 mm (análise) × 7,04 mm (aba).
- Coluna fora do plano: K = 2, λ = 6000/37,69 = 159,2 (igual à aba).
- Coluna no plano: K = 1,56 sobre 2000 mm, λ = 82,7 (aba: 37,1 — ver item 2).
- Escora fora do plano: K = 2, λ = 5000/11,70 = 427 → NÃO PASSA (aba: 211,9 com K = 1 — ver item 1).

Entregue: (1) lista do que mudou na aba de cálculo, (2) o memorial novo em PDF, (3) a tabela "item | antes | depois".
