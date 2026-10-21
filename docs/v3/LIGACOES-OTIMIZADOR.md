# Otimizador de ligações limitado (Frente C, C2)

Código: `codigo/motor/prossen_estrutural/aco/ligacoes.py` (`otimizar`, `_botoes_limitados`). Rota: `POST /api/ligacoes/otimizar`
(`projeto.otimizar_ligacao`). Testes: `testes/test_ligacoes_otimizador.py`. Faixas: [LIGACOES-LIMITES.md](LIGACOES-LIMITES.md).

## Por que "quase sempre dava erro pela medida das chapas"

1. **Mais parafusos não cabiam.** Subir `n` com `s` e `dist_topo_furo` fixos alongava a chapa ou as cantoneiras além
   da alma. A disposição "chapa acima da mesa" dava taxa infinita. O candidato perdia sempre, e nada encurtava o
   espaçamento. A busca parava com a pior taxa na resistência dos parafusos.
2. **Campos fora da busca.** `n_linhas_extras`, os diâmetros e contagens da emenda (`d_mesa_mm`, `d_alma_mm`,
   `n_colunas_alma`...) e as dimensões de chapa não eram itens do otimizador.
3. **Dados iniciais impossíveis** (`geometria impossível`) levantavam erro: a rota respondia 422.
4. **Sem solução, devolvia o "melhor encontrado"** já alterado, muitas vezes com disposições construtivas reprovadas.

## Como busca agora

- Só valores dentro de `limites(tipo, membro, dados)`. `membro` vem dos perfis de `dados` (`perfil_viga`/`perfil_pilar`,
  apoio `perfil_suporte` ou o pilar), ou do argumento `membro`. Espessuras e diâmetros só nos passos comerciais.
  Um valor inicial fora da lista (ex.: `tp = 20 mm`) sobe ao degrau seguinte.
- Ordem de material no desempate (pior taxa dentro de 2 %): solda → espessura → diâmetro → nº de parafusos →
  chapa maior (dimensões de chapa com máximo em `limites`, degraus de 10 mm, só se o valor atual não é o automático 0).
- **Disposição construtiva é restrição.** Um candidato não pode reprovar uma disposição que o detalhe atual não reprova.
  Antes de descartá-lo, o otimizador põe os espaçamentos no mínimo de `limites` (3d). Espaçamentos e bordas que ele
  não move ficam presos à faixa do candidato: o mínimo 3d sobe com o diâmetro.
- `geometria impossível` (ValueError) só descarta o candidato.
- Depois de passar, desce cada item enquanto continuar passando (mínimo local de material).
- **Último degrau, com pilar (tipos com `t_enr_pilar`):** se ainda não passa e o usuário não pôs enrijecedor, refaz a
  busca com `t_enr_pilar` 12,5 → 16 + `t_reforco` 9,5 → 19 + reforço 16 (como `ligacoes_rigidas._buscar`).
  A espessura é sempre comercial e ≥ tf da viga (faixa em `limites`). A largura do enrijecedor é a do cálculo,
  (bf − tw)/2 − 10 mm do pilar, então nunca passa de (bf − tw)/2.

## Resultado

`{ok, params, motivo, verificacao_limitante, limite_atingido, dados, resultado, passos, mudancas}`. Os campos antigos
continuam: o C# (`JanelaLigacaoProjeto.Otimizar`) não mudou.

- `ok = True`: `params` = campos alterados com o valor novo, e `motivo = None`.
- `ok = False`: **nada muda**. `motivo` e `verificacao_limitante` citam a verificação que governa os dados
  ORIGINAIS, com a taxa deles (a mesma de `resultado`), e a taxa a que a busca chegou. `dados` = os dados de entrada, `mudancas = {}`, `passos = []` e `resultado` = verificação
  dos dados originais (`None` se os dados são inválidos). `motivo` cita a verificação limitante, a taxa a que se chegou
  e o limite atingido: item no topo da faixa ou "próximo degrau viola disposição construtiva ou geometria".
- Faixa vazia (ex.: apoio mais estreito que a chapa mínima, `bp` mínimo > `bf`): para antes de buscar, e o motivo
  traz os dois valores.

## Exemplos dos livros conferidos (teste `TestExemplosLivros`)

| Exemplo | Esforços | Detalhe do livro (taxa no motor) | Otimizado |
|---|---|---|---|
| Pfeil 9.10.2, 2 cantoneiras | V = 210 kN | 1,35 | n = 4, s = 3d, taxa ≤ 0,97 |
| Pfeil 9.10.1, emenda VS600×95 | M = 328 kN·m, V = 168 kN | 1,17 (contato na alma do perfil) | d_mesa 20, tala 8, d_alma 1/2", 3 colunas; ≤ 0,99 |
| CBCA LMPA W460×89 em W310×97 (estendida) | M = 250 kN·m, V = 100 kN | — | tp 25 (comercial), bp 178 ≤ bf; ≤ 1,0 |

## Limitações

- Busca gulosa (um ou dois degraus por vez), não global.
- Na chapa de topo estendida, o enrijecedor da chapa (`t_enrijecedor`, padrão vazio) pode ser criado com 4,75 mm.

## Mesa do pilar enrijecida (ligacoes_rigidas.py, rodadas 1 e 2)

Com a linha de parafusos junto ao enrijecedor de continuidade, o modelo antigo só contava o enrijecedor quando
bs < bc. Agora há também o T-stub da EN 1993-1-8:

- **m e m2 (fig. 6.8):** m = bc − 0,8·r, com bc = furo → face da alma. m2 = bs − 0,8·a·√2, com bs = furo → face
  do enrijecedor e a = garganta da solda = 5 mm. A solda não é dimensionada: `A_SOLDA_ENR`. e = (bf − g)/2.
- **α (fig. 6.11):** pela aproximação de Jaspart, com λ1 = m/(m + e) e λ2 = m2/(m + e).
  - λ1,lim = 1,25/(α − 2,75); λ2,lim = α·λ1,lim/2.
  - Para λ1 < λ1,lim: λ2 = λ2,lim + (1 − λ2,lim)·((λ1,lim − λ1)/λ1,lim)^(α/√2).
  - Vale o maior α em [4,45; 8] cuja curva contém o ponto. Sem curva, o enrijecedor não conta.
- **leff (tab. 6.4, linha adjacente a enrijecedor, isolada):** leff = min(2π·m; α·m).
- **Resistência por parafuso (6.2.4, tab. 6.2):** Mpl = 0,25·leff·tf²·fy/γa1.
  - Modo 1 = 2·Mpl/m.
  - Modo 2 = (Mpl + n·Ft,Rd)/(m + n), com n = min(e; 1,25m).
  - Modo 3 = Ft,Rd.
  - A verificação da alavanca usa B = T·Ft,Rd/min(modo 2; modo 3).
- Vale o maior entre este padrão e o T-stub AISC sem enrijecedor. O padrão AISC mantém a convenção dele, b' = bc − d/2,
  e não muda.

Exemplo do teste: W250x25.3 em W310x38.7, M = 40 kN·m, t_enr = 9,5 mm.
- m = 17,8 mm; leff = 2πm = 111,8 mm; Mpl = 0,825 kN·m.
- Modo 1 = 92,7 kN por parafuso: a taxa da mesa vai de 1,29 para 1,01. O enrijecedor de 9,5 mm sozinho não basta.
- O otimizador resolve com enrijecedor de 12,5 mm, tp 12,5 mm, parafusos de 16 mm e 1 linha extra (taxa máxima 1,00).
