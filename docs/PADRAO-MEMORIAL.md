# Padrão do memorial de cálculo do Estructto

Este documento define como o **relatório completo** (memorial de cálculo) do Estructto é montado: estrutura, formatação,
como cada conta é escrita e o que obrigatoriamente aparece. Toda verificação nova que entrar no programa deve seguir este padrão.

Código: [RelatorioMemorial.cs](../codigo/Estructto/RelatorioMemorial.cs) (memorial) e [Relatorio.cs](../codigo/Estructto/Relatorio.cs) (relatório básico, janela e impressão).

---

## 1. Princípios

1. **Tudo que o programa calcula, o memorial mostra.** Nenhum resultado aparece sem a conta que o gerou.
2. **Conferível à mão.** Cada conta tem: fórmula → valores substituídos → resultado → limite → aproveitamento.
   Quem lê precisa conseguir refazer a conta com uma calculadora, só com o que está na página.
3. **Mesmas fórmulas do programa.** O memorial não tem fórmulas próprias: ele chama `Verificador`, `PlacaCalc`, `Solda` e
   `Chumbadores` e só escreve o desenvolvimento. Se a fórmula do programa mudar, o memorial muda junto.
4. **O relatório não altera o modelo.** Qualquer troca temporária de resultado (ex.: combinação crítica da envoltória)
   é desfeita em `finally`.
5. **Unidades coerentes nas substituições:** forças em **N**, comprimentos em **mm**, logo tensões em **N/mm² = MPa**.
   Esforços de entrada aparecem em kN e kN·m no texto e são convertidos na própria substituição.
6. **Português técnico, sem abreviação obscura.** Toda letra usada numa fórmula aparece na tabela de dados ou numa nota.

---

## 2. Duas versões do relatório

| Versão | Conteúdo | Para quê |
|---|---|---|
| **Básico** | Seções 1 a 8: modelo, casos, combinações, diagramas, reações, esforços, tabela de verificação, tabela de placas | Entregar o resultado |
| **Completo** | Básico + **seção 9, Memorial de cálculo** | Conferir, aprovar, arquivar |

A escolha é feita na janela do relatório (botões *Básico* / *Completo*). `MontarRelatorio(bool completo)` monta os dois.

---

## 3. Estrutura da seção 9

```
9. Memorial de cálculo
   (parágrafo de abertura: unidades, n, limite de flecha, resultado mostrado)
   9.1 Análise estrutural (método da rigidez direta)
       - texto do método
       - tabela de rigidez por barra
       - tabela de esforços nas pontas
   9.2 Equilíbrio global
       - bloco "Reações de apoio" com fechamento ΣFx, ΣFy, ΣM
   9.3 Verificação das barras
       - para cada barra: título, bloco Dados, blocos 1) a 4), aviso (se houver), bloco Resultado
   9.4 Placas de base (se houver)
   9.5 Limitações (o que o memorial NÃO verifica) — sempre
       - para cada placa: título, Dados, 1) forças nas fixações, 2) parafuso ou chumbador, 3) chapa, 4) solda, aviso
```

### 3.1 Parágrafo de abertura
Sempre informa:
- convenção de unidades (N, mm, MPa);
- coeficiente de segurança das barras e da chapa `n`;
- limite de flecha `L/x`;
- qual resultado está sendo relatado (caso, combinação ou envoltória).

### 3.2 Análise estrutural (9.1)
- Texto curto explicando: elemento de pórtico plano, 3 GL por nó (u, v, θ), montagem K·U = F, esforços f = k·u + f₀.
- **Tabela de rigidez:** `Barra | L | EA | EI | EA/L | 12EI/L³ | 6EI/L² | 4EI/L | 2EI/L`, com as unidades das colunas
  explicadas logo abaixo. Se Timoshenko estiver ligado, dizer que os termos de flexão são reduzidos por 1/(1+φ).
- **Tabela de esforços nas pontas:** `Barra | Nó | N | Q | M | Nó | N | Q | M`, em eixos locais, com a convenção de sinais
  (N + tração; M + traciona a fibra de baixo).

### 3.3 Equilíbrio global (9.2)
- Tabela de reações: `Nó | X | Y | Rx | Ry | M | M + x·Ry − y·Rx`.
- Linha de somas: ΣRx, ΣRy, ΣM em relação a (0;0).
- Linha de cargas aplicadas (nodais + distribuídas + peso próprio, multiplicadas pelos fatores do resultado mostrado).
- **Fechamento** (deve dar zero) com selo *✓ EQUILÍBRIO OK* / *✗ NÃO FECHA*.
- Na envoltória não há fechamento único: escrever a nota "o equilíbrio vale combinação a combinação".

Esta seção é a prova de que o cálculo estrutural está certo; não pode ser omitida.

---

## 4. Blocos de verificação (formatação)

Cada verificação é um **bloco** (`class Bloco`):

| Elemento | Aparência | Método |
|---|---|---|
| Faixa lateral | 3 px à esquerda: **verde** passa, **vermelho** não passa, cinza informativo, dourado aviso | `Selo(...)` |
| Fundo | cinza-azulado claro `#F6F8FB` | — |
| Título | negrito 11,5 pt + selo **✓ PASSA** / **✗ NÃO PASSA** em verde/vermelho | `new Bloco("...")`, `Selo(ok)` |
| Equação | LaTeX desenhado (WpfMath), azul escuro `#143D6B`, escala 15 | `Eq(latex)` |
| Nota | 9,5 pt cinza, para explicar hipóteses, origem de constantes, símbolos | `Nota(texto)` |
| Resultado | negrito, verde/vermelho conforme passa | `Resultado(texto, ok)` |
| Tabela | tabela padrão do relatório (cabeçalho azul-claro, bordas finas) | `Tab(Tabela(...))` |

### 4.1 Ordem obrigatória dentro de um bloco de verificação
1. **Nota de contexto** (opcional): de onde vêm os esforços, hipótese adotada.
2. **Equação simbólica** com o limite: ex. `σ = |N|/A + |M|/W ≤ σadm = fy/n`.
3. **Equação com os números substituídos**, terminando no resultado com unidade.
4. **Equação do limite** com números (se não coube na anterior).
5. **Nota** explicando constantes "mágicas" (ex.: 0,577 = 1/√3; 0,707 = cos 45°; expoente 5/3).
6. **Resultado**: `Aproveitamento = solicitante / resistente = xx%`.
7. **Selo** no título.

### 4.2 Bloco de dados (antes das verificações de cada elemento)
Tabela com 5 colunas: **Grandeza | Símbolo | Valor | Unidade | Origem**.
A coluna *Origem* diz de onde o número veio: `catálogo`, `geometria`, `material`, `análise`, `aba Análise`, ou a própria conta
(ex.: `W = I/c` → origem `11688180 / 101,6`).

### 4.3 Bloco de resultado (fim de cada barra)
Tabela de uma linha: `Tensão | Cisalhamento | Flecha | Flambagem | Governa | Situação`, com selo geral.
O selo geral só é verde se **passa e não tem aviso**.

### 4.4 Avisos
Qualquer `Verif.Aviso` ou `ResPlaca.Aviso` vira um bloco **⚠ Atenção** com faixa dourada, logo após as verificações.
Exemplos: parede esbelta (D/t ou b/t acima do limite), KL/r > 200, distância entre chumbadores menor que a do fabricante.

---

## 5. Equações (LaTeX)

As equações são escritas em LaTeX e desenhadas pelo **WpfMath** (pacote NuGet `WpfMath` 2.1.0, já embutido no exe).

### 5.1 Regras de escrita
- Números sempre por `L(valor, casas)`: troca a vírgula decimal por `{,}` (sem espaço extra) e ∞ por `\infty`.
- Unidades por `Un("MPa")` → `\;\mathrm{MPa}` (espaço fino + texto reto).
- Separar duas equações na mesma linha com `\;\;\;\;\;\;` (**não usar `\qquad`**, o WpfMath não reconhece).
- Índices descritivos sem acento: `\sigma_{adm}`, `N_{cr}`, `I_{min}`, `n_{obtido}`, `\delta_{lim}`.
- Variáveis em itálico (padrão do LaTeX); unidades e palavras em `\mathrm{}`.
- Frações sempre com `\frac{}{}`; raízes com `\sqrt{}`; parênteses grandes com `\left( \right)`.
- Se a fórmula der erro de sintaxe, o bloco mostra o LaTeX como texto (não perde a conta). Antes de publicar, **gerar o
  relatório e olhar todas as páginas**: um ponto vermelho no lugar da equação indica comando não suportado.

### 5.2 Fórmulas padrão (exatamente como aparecem)

**Tensão normal (flexo-compressão / flexo-tração)**
$$\sigma = \frac{|N|}{A} + \frac{|M|}{W} \leq \sigma_{adm} = \frac{f_y}{n}$$

**Cisalhamento** (0,577 = 1/√3, von Mises)
$$\tau = \frac{Q}{A_s} \leq \tau_{adm} = \frac{0{,}577\, f_y}{n}$$

**Flecha de barra entre nós** (afastamento da elástica em relação à corda)
$$\delta = \max_x \left| v(x) - \left[ v_1 + (v_2 - v_1)\frac{x}{L} \right] \right| \leq \delta_{lim} = \frac{L}{\text{limite}}$$

**Flecha de barra em balanço** (deslocamento total da ponta livre)
$$\delta \leq \delta_{lim} = \frac{H}{\text{limite}}$$
com a conferência do trecho:
$$\delta_{trecho} = \frac{M L^2}{2 E I}$$

**Flambagem**
$$r = \sqrt{\frac{I_{min}}{A}} \qquad \lambda = \frac{K L}{r} \qquad \lambda_1 = \sqrt{\frac{2\pi^2 E}{f_y}}$$
Euler (λ > λ₁):
$$\sigma_{cr} = \frac{\pi^2 E}{\lambda^2}$$
Johnson (λ ≤ λ₁):
$$\sigma_{cr} = f_y - \frac{1}{E}\left(\frac{f_y \lambda}{2\pi}\right)^2$$
$$N_{cr} = \sigma_{cr} A \qquad n\,N_c \leq N_{cr} \qquad \frac{KL}{r} \leq 200$$

**Forças nas fixações da placa** (placa rígida girando no eixo comprimido)
$$T_i = k\, d_i \qquad k = \frac{M + N a}{\sum d_i^2} \qquad F_v = \frac{V}{n_b}$$
(todas tracionadas: $T_i = \frac{N}{n_b} + \frac{M\,u_i}{\sum u_j^2}$)

**Parafuso** (resistência de prova Sp, von Mises)
$$\sigma = \frac{T_{max}}{A_t} \qquad \tau = \frac{F_v}{A_r \text{ ou } A_d} \qquad \sigma_{vm} = \sqrt{\sigma^2 + 3\tau^2} \qquad n_{obtido} = \frac{S_p}{\sigma_{vm}} \geq n$$

**Chumbador** (cargas de ruptura do catálogo, interação 5/3)
$$\left(\frac{T}{T_u}\right)^{5/3} + \left(\frac{V}{V_u}\right)^{5/3} = \left(\frac{1}{n_{obtido}}\right)^{5/3} \qquad n_{obtido} \geq n_{chumbador}$$
com $T_u, V_u$ convertidos de kgf para kN (× 0,00981).

**Espessura da chapa** (faixa em balanço da fileira tracionada até a face do perfil)
$$t = \sqrt{\frac{6\,T\,e\,n}{b\,f_y}}$$

**Solda de filete pilar–placa**
$$a = 0{,}707\,h \qquad A = a\,L_c \qquad I = a\,I_u$$
$$\tau' = \frac{V}{A} \qquad \tau'' = \frac{N}{A} + \frac{M\,c}{I} \qquad \tau = \sqrt{\tau'^2 + \tau''^2} \leq \tau_{adm}$$

---

## 6. Conteúdo obrigatório por elemento

### 6.1 Barra
| Item | Conteúdo |
|---|---|
| Título | `Barra N · trecho da barra contínua Bk — descrição do perfil · material` |
| Combinação crítica | Na envoltória, nota com o nome da combinação que governa a barra |
| Dados | L, A, orientação da seção, Ix, Iy, rx, ry, I usada (com o eixo na coluna Origem), c, W = I/c, As, fy, E, n |
| 1) Tensão normal | ponto crítico x, N e M nesse ponto, fórmula, substituição, σadm, aproveitamento |
| 2) Cisalhamento | posição do maior cortante, fórmula, substituição, τadm, nota do 0,577 |
| 3) Flecha | tipo (entre nós ou balanço), fórmula, valor, limite com H ou L, conferência ML²/2EI no balanço |
| 4a) Flambagem no plano / 4b) fora do plano | barra contínua inteira (autovalor, independe da malha); pontos travados (rígidos e elásticos com k); Le, L (maior trecho entre travamentos), K (teórico e adotado, K ≥ 1), r do eixo do plano, λ, λ₁, Euler ou Johnson, Ncr, n·Nc, KL/r ≤ 200; sem compressão: L/r ≤ 300 e aviso "flambagem não verificada" |
| 5) Interação N + M | n·Nc/Ncr,mín + B·n·|M|máx/(W·fy) ≤ 1, B = 1/(1 − n·Nc/Ne), Ne no plano, Cm = 1 |
| Aviso | parede esbelta, KL/r > 200, L/r > 300, mecanismo (sem travamento), inversão de esforço |
| Resultado | tabela: tensão, corte, flecha, flamb. no plano, flamb. fora, esbeltez, N + M, governa, situação; selo verde só sem aviso ("⚠ PASSA COM AVISO") |

### 6.2 Placa de base
| Item | Conteúdo |
|---|---|
| Dados | reação do nó (Rx, Ry, M), esforços nos eixos da PLACA (N, V, M) e nos eixos da barra; H × B × t, fy, fixações; chumbador com tipo de carga e n (origem) |
| 1) Forças | modelo (giro na borda / na mesa / todas tracionadas), fórmula, **tabela com y, z e T de cada fixação**, Fv |
| 2a) Parafuso | σ, τ (Ar ou Ad, com nota), σvm, n obtido ≥ n, aperto (pré-carga e torque) |
| 2b) Chumbador | Tu e Vu convertidos, interação 5/3 com números, n obtido ≥ coef. do chumbador, instalação (furo, embutimento, torque), distâncias mínimas |
| 3) Chapa | lado tracionado t_T e lado comprimido t_C (C = ΣT − N na linha de giro); t = máx; barra inclinada: "⚠ NÃO VERIFICADO" |
| 4) Solda | esforços nos eixos da barra; a, A, I, τ′, τ″, τ, τadm = 0,30·FEXX, n_equiv (von Mises), perna mínima NBR 8800 Tab. 10 pela parte mais fina; barra inclinada: "⚠ NÃO VERIFICADO" |
| Aviso | espaçamento entre chumbadores, perna maior que a parede |

---

## 7. Convenções de números

| Grandeza | Casas | Exemplo |
|---|---|---|
| Comprimentos em mm | 0 | 2500 |
| Áreas, módulos, inércias em mm², mm³, mm⁴ | 0 | 115041 |
| Raio de giração, distâncias pequenas | 1–2 | 70,53 |
| Tensões em MPa | 1–2 | 45,33 |
| Esforços em kN, kN·m (texto) | 2 | -4,95 |
| Forças nas fixações | 3 | 0,635 |
| Esbeltez | 1 | 35,4 |
| Interação 5/3 | 5 | 0,00131 |
| Aproveitamento | 0, em % | 98% |

Separador decimal: vírgula. Sinais: negativo com "−" nos textos e "-" nas tabelas.

---

## 8. Como conferir antes de entregar uma versão

1. Gerar o relatório completo de um modelo de teste sem abrir a janela:
   ```
   Estructto.exe "exemplos\guincho-com-placa.estructto" --tela2 --relpng=PASTA
   ```
   Grava uma PNG por página A4 em `PASTA` e fecha.
2. Olhar **todas** as páginas: nenhuma equação faltando (ponto vermelho), nenhuma tabela cortada sem cabeçalho,
   selos coerentes com os números.
3. Conferir à mão pelo menos uma conta de cada tipo (tensão, flecha, flambagem, fixação, chapa, solda).
4. O bloco **9.2 Equilíbrio global** deve mostrar fechamento 0 (selo verde).
5. Rodar os testes do motor: `codigo\portico\compilar.ps1 -Testes` (todos devem passar).

---

## 9. Limitações que o memorial deve declarar

O memorial não esconde o que o programa **não** verifica. Estas frases aparecem no texto quando se aplicam:
- Verificação simplificada; **não substitui a NBR 8800** (sem flambagem lateral com torção, sem flambagem local calculada;
  parede esbelta apenas avisada).
- Placa: no plano do apoio (piso, teto ou parede); **não verifica pressão no concreto, punção nem distância do chumbador à borda do concreto**.
- Pórtico plano: flambagem fora do plano verificada, mas cargas fora do plano, FLT, torção e 2ª ordem global (P-Δ) não.
- Equipamento giratório: só posições 0° e 180°; ψ é declarado pelo usuário (não reaplicado).
- Cargas de chumbador são **cargas últimas médias de ensaio** do fabricante; o coeficiente de segurança é do projetista
  (Walsywa, baixo / alto risco: estática 3 / 5, variável 4 / 5, dinâmica 7 / 15, choque 5 / 10).
