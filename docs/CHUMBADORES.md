# Chumbadores — catálogo único e critério de cálculo (Estructto + planilha)

Última atualização: 19/10/2026. Histórico de mudanças: [CHANGELOG.md](CHANGELOG.md).

## Onde está o catálogo

- **Fonte única:** [`codigo/portico/chumbadores.csv`](../codigo/portico/chumbadores.csv), com 76 itens:
  - Walsywa mecânicos: CB, CBPL, CBE, CBEPL, WB (ruptura média, IPT, C20; catálogo "Fixação Mecânica", pág. 08–10);
  - Walsywa químicos: ampola WQA (só carga RECOMENDADA, C20, pág. 05), WQI 44 e WQE 500 com barra roscada
    (ruptura de tração, C28, sem corte; pág. 07 e 09);
  - Indufix Parabolt PB (ruptura média, C30).
  Os PDFs estão em `ELEMENTOS DE MÁQUINA\REFERÊNCIA` (OneDrive): `walsywa_fix_mec.pdf`, `WALSYWA_fixacao_quimica.pdf`.
- **Estructto:** embute o CSV na compilação (`Estructto.csproj` e `compilar.ps1`). Mudou o CSV → recompilar.
- **Planilha:** a aba CHUMBADORES é gerada do CSV por
  `codigo\portico\sincronizar-planilha.ps1 -Planilha "<caminho da planilha>"` (a planilha precisa estar fechada).
  Não editar a aba à mão. Colunas A–F mantêm o significado antigo (código, Ø, furo, P, Tu, Vu).
- **Fora do catálogo:** PU 500 da Walsywa é espuma de vedação, sem carga — não é chumbador. WLT (sem corte), Golpe de
  Metal e fitas de suspensão foram deixados de fora.

## Critério de cálculo (igual nos dois)

| Item | Mecânicos (CB, CBPL, CBE, CBEPL, WB, Indufix) | WQI 44 / WQE 500 | WQA (ampola) |
|---|---|---|---|
| Carga de referência no concreto | ruptura média | ruptura de tração | carga recomendada |
| Coeficiente exigido n,ref | tabela pág. 07 (guincho = 15) | tabela pág. 07 (guincho = 15) | 1 (coeficiente embutido não informado) |
| Corte no concreto | do catálogo | **não há** (termo = 0) | do catálogo |
| Fator do concreto ψc | min(1; √(fck laje / fck ensaio)) | idem (C28 → 0,845 em C20) | idem |
| Aço da barra | incluído no ensaio | von Mises: n,aço = Sp/√(σ²+3τ²) ≥ n | idem WQI/WQE |
| Distâncias mínimas | 3P e 1,5P (pág. 07) | 3P e 1,5P (regra geral) | distâncias para carga máxima (pág. 05) |

- Interação tração + corte: (T/Tref)^5/3 + (V/Vref)^5/3 = (1/n,obt)^5/3, n,obt ≥ n,ref (hipótese de projeto).
- Barra roscada: σ = T/At, τ = V/Ar (áreas UNC), Sp da Tabela 6 (Aula 2). Classe da barra é um campo próprio
  (padrão **4.6**), separado da classe do parafuso métrico. n = coeficiente das barras do modelo (planilha: 1,5).
- ψc é hipótese nossa (cone ∝ √fck, ACI 318 cap. 17), declarada no memorial. Afeta também o Indufix (C30 → 0,82 em C20).
- **Atenção:** como a WQA é verificada contra a carga recomendada com n = 1, ela tende a vencer os mecânicos
  (n = 15 sobre ruptura) no dimensionamento automático. Decidir caso a caso para o guincho.

## Conferência entre os dois

`codigo\portico\compilar.ps1 -Testes` tem testes "= planilha" que repetem as contas da aba COLUNA ESCORADA
(CBPL 14300, WQE 500 5/8", WQA 1/2"). Se mudar a fórmula num lado, mude no outro e nesses testes.

## Pendente

- Cones sobrepostos: o Estructto reduz Tu por ψ (áreas projetadas) quando s < 3P e só reprova abaixo de 6d;
  a planilha ainda reprova direto quando s < s,min. Alinhar.
- Placa de base atual (CBPL 14300 / CB 12300) não passa: base n = 3,79 e pé 0° n = 7,86 contra 15; chapa 106%.

---

# Análise original do catálogo Walsywa (18/10/2026)
## 1. Minha análise: vale adotar?

**Sim, é uma boa base. O Estructto já usava esse catálogo, mas aplicava a tabela de coeficientes pela metade.**

O que o catálogo dá de fato:

1. **Cargas MÉDIAS de ruptura** (tração = arrancamento, e cisalhamento), em kgf, por diâmetro e profundidade de
   embutimento, em concreto C20. **Não são cargas admissíveis**: precisam ser divididas por um coeficiente de segurança.
2. **Tabela de coeficientes de segurança (pág. 07)** por tipo de carga e **grau de risco**:

   | Comportamento da carga | Baixo risco | Alto risco |
   |---|---|---|
   | Estática | 3 | 5 |
   | Variável | 4 | 5 |
   | Dinâmica | 7 | 15 |
   | Choque | 5 | 10 |

   Antes, o Estructto lia isso como uma "faixa" (dinâmica 7 a 15) e usava sempre o mínimo. **Os dois números são
   colunas diferentes: baixo risco e alto risco.** Corrigido: agora há o campo "Grau de risco".
3. **Distâncias mínimas (pág. 07):** o cone de arrancamento tem raio ≈ 1,5·P (P = profundidade de embutimento).
   Entre chumbadores **≥ 3·P**; do chumbador à borda do concreto **≥ 1,5·P**.
4. **Aviso do próprio fabricante:** os valores são indicativos; a decisão e o coeficiente são do engenheiro, e
   "devem ser verificados sempre que possível por ensaios práticos de carga na aplicação".

**Para o guincho de coluna:** a carga é **dinâmica** (içamento, arranque, frenagem, giro). E é **alto risco**: a falha
derruba o equipamento com carga suspensa, com gente por perto. Pela tabela, **n = 15**. Com baixo risco, n = 7.
O Estructto agora usa automaticamente dinâmica + alto risco quando há "carga giratória", e mostra isso no memorial.

**Cuidados que o catálogo não cobre** (declarar no memorial, não esconder):

- **Concreto diferente de 20 MPa:** as cargas são de C20. Em concreto mais fraco, a carga de arrancamento cai. Não
  extrapolar para cima sem dado do fabricante.
- **Espaçamento menor que 3·P:** os cones se sobrepõem e a carga real é menor, mas o catálogo não dá o fator. Reprovar
  tudo abaixo de 3·P travava a escolha (chumbador mais fundo resiste mais, porém exige espaçamento maior). Adotado o
  **método das áreas projetadas (ACI 318 cap. 17 / EN 1992-4)**: Tu é multiplicado por ψ = A / (nt·9·P²), A = área da união
  dos quadrados 3P × 3P centrados nos chumbadores tracionados. Mínimo físico para instalar: **6·d** (abaixo disso reprova).
- **Distância à borda menor que 1,5·P:** também reduz o cone, mas o modelo não conhece a laje: declarar e conferir na obra.
- **Tração + corte juntos:** o catálogo não traz equação de interação. O Estructto usa
  (T/Tu)^5/3 + (V/Vu)^5/3 = (1/n)^5/3, que é a prática usual para chumbadores expansivos (mais conservadora que a soma linear
  apenas quando os dois esforços são pequenos). Declarar como hipótese, não como dado da Walsywa.
- **Carga dinâmica em chumbador de expansão:** com vibração e ciclos, o expansivo pode afrouxar. Para guincho,
  recomendar reaperto periódico e, se possível, **chumbador químico ou chumbador concretado (barra com gancho/placa)**.
- **Concreto da laje:** pressão sob a placa, punção e espessura mínima da laje (≥ 2·P, prática) não são verificadas.
- **Indufix Parabolt:** o Estructto também tem o catálogo Indufix (C30). A tabela de coeficientes da Walsywa é aplicada a
  ele também, por coerência; isso é decisão de projeto, declarar.

---

## 2. Padrão de cálculo (igual no Estructto e no Excel)

Entradas (todas aparecem na tabela de Dados, com origem):

| Grandeza | Símbolo | Origem |
|---|---|---|
| Linha e diâmetro do chumbador | — | catálogo Walsywa (código, ex.: CB 12300) |
| Profundidade de embutimento | P (hef) | catálogo |
| Carga média de ruptura à tração | Tu (kgf → kN) | catálogo, C20 |
| Carga média de ruptura ao corte | Vu (kgf → kN) | catálogo, C20 |
| Tipo de carga | estática / variável / dinâmica / choque | escolha do projetista (guincho = dinâmica) |
| Grau de risco | baixo / alto | escolha do projetista (guincho = alto) |
| Coeficiente de segurança | n | tabela Walsywa pág. 07 (ou digitado, com aviso se menor) |
| Resistência do concreto | fck | projeto (aviso se < 20 MPa) |

Contas, nesta ordem (padrão do memorial: fórmula → números → resultado → limite → aproveitamento → selo):

1. **Conversão:** Tu (kN) = Tu (kgf) × 0,00980665 · Vu (kN) = Vu (kgf) × 0,00980665.
2. **Forças em cada chumbador**, nos eixos da placa (normal ao concreto), a partir da reação do apoio:
   - placa rígida girando na borda comprimida: Tᵢ = k·dᵢ, k = (M + N·a)/Σdᵢ²; todos tracionados: Tᵢ = N/nb + M·uᵢ/Σuⱼ²;
   - corte: V = (reação paralela à placa)/nb;
   - conferência: Σ cortes = reação paralela; Σ trações − compressão no concreto = N.
3. **Verificação isolada:** n_T = Tu/Tmáx ≥ n e n_V = Vu/V ≥ n.
4. **Interação:** (Tmáx/Tu)^5/3 + (V/Vu)^5/3 = (1/n_obtido)^5/3, n_obtido = (soma)^(−3/5) ≥ n.
5. **Aproveitamento** = n_exigido / n_obtido (≤ 100% passa).
6. **Cones sobrepostos (antes do passo 3):** se o espaçamento s < 3·P, Tu,ef = ψ·Tu, ψ = A/(nt·9·P²), A = área da união
   dos quadrados 3P × 3P dos chumbadores tracionados. Ex.: 4 chumbadores a 220 mm, P = 148 → lado da união = 220 + 444 =
   664 mm, A = 664² = 440 896 mm², ψ = 440 896/(4·9·148²) = 0,559. Use Tu,ef nos passos 3 e 4.
   Espaçamento mínimo para instalar: 6·d (✗ abaixo). Borda do concreto ≥ 1,5·P: conferir na obra (⚠ declarado).
7. **Instalação:** furo Ø, profundidade mínima, torque (do catálogo).

Selos: o selo do chumbador só é verde se 4 **e** 6 passam. Coeficiente digitado menor que o da tabela = ⚠.

