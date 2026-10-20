# Guia de Escolha de Fixações — Estructto

- **Data:** 19/10/2026
- **Fontes consultadas:**
  - `walsywa_fix_mec.pdf` — *Catálogo Técnico de Fixação Mecânica Walsywa* (páginas 01 a 15; páginas técnicas utilizadas: 02, 03, 04, 05, 06, 07, 08, 09, 10, 11, 12, 13, 14, 15).
  - `WALSYWA_fixacao_quimica.pdf` — *Catálogo Técnico de Fixação Química Walsywa* (páginas 01 a 12; páginas técnicas utilizadas: 02, 03, 04, 05, 06, 07, 08, 09, 10, 11).

> **Aviso:** texto baseado nos catálogos; o que não estiver no catálogo está marcado como `[inferência]`.
> Redigido pelo agy (Antigravity) e conferido pelo Claude em 19/10/2026: todas as citações entre aspas foram
> comparadas com o texto dos PDFs.

## 0. Uso no software (Estructto)

- **Objetivo:** mostrar ao usuário, ao escolher a fixação da placa de base, o tipo, o uso recomendado pelo fabricante, os
  usos típicos e os pontos de atenção de cada linha, além do roteiro "Como escolher" (seção 4).
- **Chave:** os títulos da seção 2 (ex.: `Walsywa WB`) são exatamente o campo `linha` de
  [`codigo/portico/chumbadores.csv`](../codigo/portico/chumbadores.csv), que é o nome mostrado na lista de chumbadores.
- **Linhas que estão no catálogo de cálculo:** CB, CBPL, CBE, CBEPL, WB, WQA, WQI 44, WQE 500 (e Indufix Parabolt PB, que
  ainda não tem seção aqui: falta o PDF do catálogo Indufix na pasta REFERÊNCIA).
- **Só informativas (fora do cálculo):** WLT (sem corte tabelado), Golpe de Metal (cargas leves), PU 500 (espuma, sem carga).
- **A FAZER — a função "Comparar fixações" ainda NÃO existe no Estructto.** Ela precisa ser criada: o usuário escolhe 2 ou mais linhas/diâmetros e vê lado a lado os atributos da seção 5.1, o texto do par (5.2), as cargas do catálogo e as ajustadas, a verificação de cada uma com os esforços da placa atual e os avisos da seção 5.3. Este guia é a fonte do conteúdo; os dados embutidos no programa sairão dele (tabela 5.1 → CSV embutido). Pendência registrada no [CHANGELOG](CHANGELOG.md).

---

## 1. Princípios Gerais dos Catálogos

### 1.1 Como a Fixação Mecânica Trabalha (`walsywa_fix_mec.pdf`, págs. 04–12)
- O catálogo apresenta chumbadores mecânicos expansivos para concreto normal e uma síntese de fixadores para outros materiais-base (pág. 04).
- **Atrito / expansão:** nos chumbadores expansivos mecânicos (linhas CB, CBPL, CBE, CBEPL, WB, WLT e Golpe de Metal), a ancoragem ocorre pela força de expansão contra a parede do furo no material-base, gerada por **torque controlado** (cone tracionado para dentro da jaqueta em CB/CBPL/CBE/CBEPL, págs. 08–09, ou corpo roscado/presilha em WB, pág. 10) ou por **percussão** (batedor sobre tolete cônico interno em WLT, pág. 11, ou batida de martelo no pino expandindo corpo em zamak no Golpe de Metal, pág. 12).
- **Forma / travamento:** para materiais ocos ou placas de parede fina de baixa resistência (tijolo estrutural oco, bloco de concreto oco, drywall), o catálogo indica fixadores que "transmitem a carga por travamento" dentro da cavidade, como buchas para oco, *Waltog* e *snaptoggle* (págs. 04–05).
- **Aderência e intertravamento:** nos chumbadores químicos, a resina adere ao material-base ("ação de aderência") e, ao curar, forma um intertravamento mecânico com os sulcos da barra roscada ou saliências do vergalhão ("ação de intertravamento") (`WALSYWA_fixacao_quimica.pdf`, pág. 04).

### 1.2 Tipos de Falha em Ensaios de Arrancamento (`walsywa_fix_mec.pdf`, pág. 06)
1. **Falha na expansão:** o esforço de tração supera a força de expansão entre o chumbador e o material-base, causada por torque ou expansão abaixo da especificação na instalação; o chumbador sai completo do furo quase sem ruptura do concreto (pág. 06).
2. **Falha no material-base (cone de concreto):** a carga aplicada supera a resistência do concreto e arranca um cone do material-base que se inicia na zona de expansão, com ângulo incluso entre 35° e 45° (pág. 06).
3. **Falha no chumbador:** ocorre em concretos de alta resistência com chumbadores corretamente aplicados (expansão suficiente), quando a carga excede a resistência à tração ou ao cisalhamento do aço do fixador (pág. 06).
4. **Trinca(s) presentes no material-base:** ocorre quando a base tem largura, comprimento ou profundidade insuficientes, quando as distâncias entre chumbadores ou até a borda são muito pequenas, ou quando o concreto está submetido a tensões internas que provocam trincas (pág. 06).

### 1.3 Materiais-Base (`walsywa_fix_mec.pdf`, págs. 04–05)
- **Concreto (págs. 04–05):** boa resistência à compressão e baixa resistência à tração (compensada por armaduras). Classificado em pré-moldado e usinado:
  - *Concreto de peso normal* (2,3 a 2,5 kg/dm³): classes C10 a C50 (resistência característica à compressão aos 28 dias em MPa). Aceita fixação à pólvora, fixadores metálicos, fixadores químicos e buchas de nylon (págs. 04–05).
  - *Concreto leve* (~0,5 kg/dm³): menor resistência mecânica (pág. 05).
- **Alvenaria (págs. 04–05):**
  - *Tijolo comum (maciço) / bloco maciço:* por serem maciços, "aceitam fixadores expansivos e químicos de injeção com aplicação de baixas cargas" (pág. 05).
  - *Tijolo estrutural (oco) e bloco de concreto (oco):* resistentes, mas "só aceitam fixações do tipo que se travam dentro da cavidade (buchas para oco) ou chumbadores do tipo químico de injeção" (pág. 05).
- **Placas / Drywall (págs. 04–05):** madeira aglomerada/compensada e gesso acartonado; exigem fixadores por travamento (*Waltog*, *walgips*, *snaptoggle*, buchas para oco/nylon).
- **Pedra natural (págs. 04–05):** aceita fixadores metálicos e químicos (pág. 04).
  - *Rochas sedimentares (menos duras):* chumbadores têm capacidade similar àqueles instalados em concreto de **14 MPa** (pág. 05).
  - *Granito (vulcânica) e mármore (metamórfica):* chumbadores comportam-se como aqueles instalados em concreto de **30 MPa** (pág. 05).

### 1.4 Distanciamentos Mínimos (`walsywa_fix_mec.pdf`, pág. 07)
- O cone de concreto arrancado na tração tem raio aproximado de $1,5 \times P$ ($P$ = profundidade de embutimento) (pág. 07).
- Para evitar interseção entre cones ou com a borda nos chumbadores mecânicos expansivos:
  - **Distância mínima entre dois chumbadores ($D$):** $D = 3 \times P$ (3 vezes a profundidade de embutimento) (pág. 07).
  - **Distância mínima até a borda ($d$):** $d = 1,5 \times P$ (1,5 vez a profundidade de embutimento) (pág. 07).

### 1.5 Coeficientes de Segurança (`walsywa_fix_mec.pdf`, pág. 07)
As tabelas do catálogo mecânico informam cargas médias/limites de ruptura de laboratório (apenas orientativas), devendo ser reduzidas pelo coeficiente de segurança conforme o carregamento e o grau de risco (pág. 07):

| Comportamento da Carga | Coeficiente — Baixo Risco | Coeficiente — Alto Risco |
| :--- | :---: | :---: |
| **Estática** | 3 | 5 |
| **Variável** | 4 | 5 |
| **Dinâmica** | 7 | 15 |
| **Choque** | 5 | 10 |

### 1.6 Princípios da Fixação Química (`WALSYWA_fixacao_quimica.pdf`, pág. 04)
- **Funcionamento:** baseia-se na capacidade de adesão da resina ao material-base. Um componente-base (viniléster, poliéster, epóxi etc.) misturado ao catalisador inicia a cura (endurecimento), aderindo ao furo e criando intertravamento com os sulcos da barra roscada ou saliências do vergalhão (pág. 04).
- **Principais características listadas pelo catálogo (pág. 04):**
  - "alta resistência";
  - "versatilidade para ser usado com diferentes tipos hastes (vergalhões, barras roscadas)";
  - "a aplicação não cria tensões no concreto";
  - "aplicação à prova de umidade e/ou intempéries";
  - "excelente perfil de resistência à cargas dinâmicas e de choque";
  - "possibilidade de distanciamentos menores entre chumbadores".

---

## 2. Linhas de Produto

### Walsywa CB
- **Tipo:** Chumbador mecânico de expansão por torque controlado, com parafuso sextavado (`walsywa_fix_mec.pdf`, pág. 08).
- **Características (`walsywa_fix_mec.pdf`, pág. 08):**
  - "Medidas nominais de 1/4" a 1"";
  - "Fornecidos completos com parafuso, arruela, jaqueta e cone";
  - "Acabamento zincado brilhante" ("Componentes fornecidos em aço zincado brilhante");
  - "Expansão tecnicamente perfeita" e "Expansão por torque controlada";
  - "Maior aderência" e "Recuperabilidade" (permite soltar o parafuso, posicionar a peça e dar o torque final);
  - "Chumbadores de Ø 1/4" oferecidos também com parafuso classe 8.8".
- **Uso recomendado pelo fabricante (`walsywa_fix_mec.pdf`, pág. 08):**
  - "Indicado para fixação de máquinas, equipamentos, estruturas metálicas e outros."
- **Usos típicos:**
  - Fixação de máquinas, equipamentos e estruturas metálicas (pág. 08); bases de equipamentos removíveis que exigem parafuso passante `[inferência]`.
- **Material-base aceito:**
  - Concreto normal (tabelas ensaiadas em concreto 20 MPa, págs. 04 e 08); pedra natural e tijolo comum maciço para baixas cargas (pág. 05).
- **Pontos de atenção / limitações:**
  - Exige pré-expansão no furo antes de posicionar a peça ("Aperte o parafuso com troque suficiente para expandir a jaqueta. Solte o parafuso, posicione a peça a ser fixada e dê o torque final", pág. 08) — o furo na peça a fixar pode ser menor que o furo no concreto (`ØA < ØB`) `[inferência]`.
  - É **recuperável** (o parafuso pode ser removido, permanecendo jaqueta e cone no furo, pág. 08).
  - Respeitar distanciamentos mínimos $D = 3P$ e $d = 1,5P$ e coeficientes de segurança de 3 a 15 conforme carga/risco (pág. 07).
- **Faixa de diâmetros e de cargas (`walsywa_fix_mec.pdf`, pág. 08 — concreto 20 MPa, laudo IPT nº 28 202):**
  - Rosca Ø 1/4" a 1" (furo Ø 3/8" / 10 mm a 1 1/4" / 32 mm; profundidade mínima do furo de 50 mm a 152 mm; espessura máx. a fixar de 7 mm a 35 mm).
  - **Cargas médias de ruptura à tração:** 1.253 kgf (1/4") a 7.790 kgf (1").
  - **Cargas médias de ruptura ao cisalhamento:** 800 kgf (1/4") a 12.080 kgf (1").

### Walsywa CBPL
- **Tipo:** Chumbador mecânico de expansão por torque controlado, com parafuso sextavado e prolongador liso (`walsywa_fix_mec.pdf`, pág. 08).
- **Características (`walsywa_fix_mec.pdf`, pág. 08):**
  - "Medidas nominais de 1/4" a 1"";
  - "Fornecidos completos com parafuso, arruela, jaqueta e cone", acrescidos de prolongador liso (de 22 mm a 79 mm);
  - "Chumbador CBPL fornecido com prolongador liso; maior resistência à tração";
  - "Opção de aplicação com prolongador para maior profundidade de embutimento no material base e consequentemente maior resistência à tração";
  - "Acabamento zincado brilhante", "Expansão por torque controlada", "Maior aderência" e "Recuperabilidade".
- **Uso recomendado pelo fabricante (`walsywa_fix_mec.pdf`, pág. 08):**
  - "Indicado para fixação de máquinas, equipamentos, estruturas metálicas e outros."
  - Recomendado quando se requer "maior profundidade de embutimento no material base e consequentemente maior resistência à tração" (pág. 08).
- **Usos típicos:**
  - Máquinas, equipamentos e estruturas metálicas (pág. 08) com solicitações de tração superiores às atendidas pelo CB padrão `[inferência]`.
- **Material-base aceito:**
  - Concreto normal (ensaiado em concreto 20 MPa, págs. 04 e 08); pedra natural e tijolo maciço para baixas cargas (pág. 05).
- **Pontos de atenção / limitações:**
  - Maior profundidade mínima de furo (76 mm a 228 mm), o que também aumenta as distâncias mínimas entre chumbadores ($D = 3P$) e à borda ($d = 1,5P$) (págs. 07–08) `[inferência]`.
  - Possui **recuperabilidade** (parafuso removível, pág. 08).
  - A carga média de cisalhamento é idêntica à do CB de mesmo diâmetro; o ganho ocorre exclusivamente na tração (pág. 08).
- **Faixa de diâmetros e de cargas (`walsywa_fix_mec.pdf`, pág. 08 — concreto 20 MPa, laudo IPT nº 28 202):**
  - Rosca Ø 1/4" a 1" (furo Ø 3/8" / 10 mm a 1 1/4" / 32 mm; profundidade mínima do furo de 76 mm a 228 mm; espessura máx. a fixar de 7 mm a 30 mm).
  - **Cargas médias de ruptura à tração:** 1.839 kgf (1/4") a 11.676 kgf (1").
  - **Cargas médias de ruptura ao cisalhamento:** 800 kgf (1/4") a 12.080 kgf (1").

### Walsywa CBE
- **Tipo:** Chumbador mecânico de expansão por torque controlado, com prisioneiro roscado e porca (`walsywa_fix_mec.pdf`, pág. 09).
- **Características (`walsywa_fix_mec.pdf`, pág. 09):**
  - "Medidas nominais de 1/4" a 1".";
  - "Fornecido completo com prisioneiro roscado, porca, arruela, jaqueta e cone";
  - "Possui prisioneiro que facilita a fixação";
  - "Acabamento zincado brilhante", "Expansão tecnicamente perfeita", "Expansão por torque controlada", "Maior aderência" e "Recuperabilidade";
  - "Chumbadores de Ø 1/4" oferecidos também com prisioneiro classe 8.8".
- **Uso recomendado pelo fabricante (`walsywa_fix_mec.pdf`, pág. 09):**
  - "Indicado para fixação de máquinas, equipamentos, estruturas metálicas e outros".
- **Usos típicos:**
  - Fixação de máquinas, equipamentos e estruturas metálicas (pág. 09), especialmente onde o prisioneiro saliente facilita o posicionamento e alinhamento da peça antes de colocar a porca `[inferência]`.
- **Material-base aceito:**
  - Concreto normal (ensaiado em concreto 20 MPa, págs. 04 e 09); pedra natural e tijolo maciço para baixas cargas (pág. 05).
- **Pontos de atenção / limitações:**
  - Possui **recuperabilidade** (pág. 09).
  - Respeitar distanciamentos mínimos $D = 3P$ e $d = 1,5P$ e aplicar coeficiente de segurança sobre as cargas de ruptura (pág. 07).
- **Faixa de diâmetros e de cargas (`walsywa_fix_mec.pdf`, pág. 09 — concreto 20 MPa, laudo IPT nº 28 202):**
  - Rosca Ø 1/4" a 1" (furo Ø 3/8" / 10 mm a 1 1/4" / 32 mm; profundidade mínima do furo de 41 mm a 118 mm; espessura máx. a fixar de 16 mm a 35 mm).
  - **Cargas médias de ruptura à tração:** 1.253 kgf (1/4") a 7.790 kgf (1").
  - **Cargas médias de ruptura ao cisalhamento:** 800 kgf (1/4") a 12.080 kgf (1").

### Walsywa CBEPL
- **Tipo:** Chumbador mecânico de expansão por torque controlado, com prisioneiro roscado, porca e prolongador liso (`walsywa_fix_mec.pdf`, pág. 09).
- **Características (`walsywa_fix_mec.pdf`, pág. 09):**
  - "Medidas nominais de 1/4" a 1".";
  - "Fornecido completo com prisioneiro roscado, porca, arruela, jaqueta e cone", acrescido de prolongador liso (22 mm a 79 mm);
  - "Chumbador CBEPL fornecido com prolongador liso; maior resistência à tração";
  - "Opção de aplicação com prolongador para maior profundidade de embutimento no material base e consequentemente maior resistência à tração";
  - "Possui prisioneiro que facilita a fixação", "Acabamento zincado brilhante", "Expansão por torque controlada", "Maior aderência" e "Recuperabilidade".
- **Uso recomendado pelo fabricante (`walsywa_fix_mec.pdf`, pág. 09):**
  - "Indicado para fixação de máquinas, equipamentos, estruturas metálicas e outros", quando se deseja "maior profundidade de embutimento no material base e consequentemente maior resistência à tração".
- **Usos típicos:**
  - Fixação de máquinas, equipamentos e estruturas metálicas (pág. 09) com prisioneiro e alta exigência de tração `[inferência]`.
- **Material-base aceito:**
  - Concreto normal (ensaiado em concreto 20 MPa, págs. 04 e 09); pedra natural e tijolo maciço para baixas cargas (pág. 05).
- **Pontos de atenção / limitações:**
  - Exige furos mais profundos (66 mm a 204 mm), elevando proporcionalmente os distanciamentos mínimos $D = 3P$ e $d = 1,5P$ (págs. 07 e 09) `[inferência]`.
  - Possui **recuperabilidade** (pág. 09).
  - Mesma carga média de cisalhamento do CBE; ganho concentrado na tração (pág. 09).
- **Faixa de diâmetros e de cargas (`walsywa_fix_mec.pdf`, pág. 09 — concreto 20 MPa, laudo IPT nº 28 202):**
  - Rosca Ø 1/4" a 1" (furo Ø 3/8" / 10 mm a 1 1/4" / 32 mm; profundidade mínima do furo de 66 mm a 204 mm; espessura máx. a fixar de 20 mm a 50 mm).
  - **Cargas médias de ruptura à tração:** 1.839 kgf (1/4") a 11.676 kgf (1").
  - **Cargas médias de ruptura ao cisalhamento:** 800 kgf (1/4") a 12.080 kgf (1").

### Walsywa WB
- **Tipo:** Chumbador mecânico de expansão por torque, tipo pino/corpo roscado com presilha expansiva (*wedge anchor* `[inferência]`) (`walsywa_fix_mec.pdf`, pág. 10).
- **Características (`walsywa_fix_mec.pdf`, pág. 10):**
  - "Medidas nominais de 1/4" a 1"" (a tabela da pág. 10 lista códigos de 1/4" a 3/4", e a pág. 14 cita também 5/16");
  - "Fornecido com corpo roscado, porca, arruela e presilha" ("Fornecido com porca e arruela pré-montados");
  - "Acabamento zincado brilhante ou aço inoxidável" (único do catálogo mecânico com opção em aço inox informada);
  - "Diâmetro da broca igual ao nominal do chumbador";
  - "Pode ser instalado através da peça a ser fixada".
- **Uso recomendado pelo fabricante (`walsywa_fix_mec.pdf`, pág. 10):**
  - "Indicado para aplicações onde é necessária alta resistência ao cisalhamento como por exemplo, em fixação de placas de granito em fachadas e estruturas porta pallets."
  - "Chumbador ideal para esforço de cisalhamento".
- **Usos típicos:**
  - Fixação de placas de granito em fachadas e estruturas porta-pallets (pág. 10); fixações passantes em chapas de base onde se deseja furar concreto e peça com a mesma broca `[inferência]`.
- **Material-base aceito:**
  - Concreto normal (ensaiado em concreto 20 MPa, pág. 10); pedra natural como granito/mármore (págs. 05 e 10).
- **Pontos de atenção / limitações:**
  - O catálogo **não** menciona recuperabilidade para o WB (o corpo roscado é introduzido com martelo e não pode ser extraído após expandido; apenas a porca e a peça podem ser retiradas, ficando o pino saliente `[inferência]`).
  - Na tabela da pág. 10 há 3 comprimentos por bitola (de 1/4" a 3/4"), mas não consta linha de dados para 1" na tabela de cargas (embora o texto cite "Medidas nominais de 1/4" a 1"").
  - Respeitar distanciamentos mínimos $D = 3P$ e $d = 1,5P$ e coeficientes de segurança (pág. 07).
- **Faixa de diâmetros e de cargas (`walsywa_fix_mec.pdf`, pág. 10 — concreto 20 MPa, laudo IPT nº 28 202):**
  - Diâmetro nominal (= diâmetro do furo) de 1/4" a 3/4" na tabela (profundidade mínima do furo de 30 mm a 185 mm; espessura máx. a fixar de 1,6 mm a 19,7 mm).
  - **Cargas médias de ruptura à tração:** 476 kgf (1/4" x 1 3/4") a 7.708 kgf (3/4" x 8 1/2").
  - **Cargas médias de ruptura ao cisalhamento:** 744 kgf (1/4") a 9.691 kgf (3/4").

### Walsywa WLT
- **Tipo:** Chumbador mecânico de expansão por percussão, com rosca interna e tolete cativo (*drop-in anchor* `[inferência]`) (`walsywa_fix_mec.pdf`, pág. 11).
- **Características (`walsywa_fix_mec.pdf`, pág. 11):**
  - "Medidas nominais de 1/4" a 3/4"";
  - "Fornecido com corpo de rosca interna e tolete cativo" ("A bucha já vem montada dentro do chumbador");
  - "Acabamento zincado brilhante";
  - "Dos chumbadores por percussão é o que possui melhor expansibilidade pois internamente é cônico e seu tolete tem uma adaptação perfeita";
  - "É aplicado com batedor especial que não danifica a rosca".
- **Uso recomendado pelo fabricante (`walsywa_fix_mec.pdf`, pág. 11):**
  - "Indicado para aplicações onde há combinações de cargas de tração e cisalhamento com em suspensão de tubulações".
- **Usos típicos:**
  - Suspensão de tubulações (pág. 11) com barras roscadas ou parafusos (pág. 15) `[inferência]`.
- **Material-base aceito:**
  - Concreto normal (ensaiado em concreto 20 MPa, pág. 11).
- **Pontos de atenção / limitações:**
  - **Sem dados de cisalhamento na tabela:** embora o texto recomende para "combinações de cargas de tração e cisalhamento", a tabela da pág. 11 apresenta **somente cargas médias de tração (arrancamento)**.
  - Exige ferramenta específica ("batedor especial", códigos WLT 1/4 a WLT 3/4, pág. 11) e martelo para expandir antes de rosquear o parafuso.
  - O catálogo não lista "recuperabilidade" nos bullets, mas por ter rosca interna permite retirar o parafuso deixando a bucha embutida rente à superfície `[inferência]`.
- **Faixa de diâmetros e de cargas (`walsywa_fix_mec.pdf`, pág. 11 — concreto 20 MPa, laudo IPT nº 28 202):**
  - Rosca interna Ø 1/4", 3/8", 1/2" (UNC ou WW), 5/8" e 3/4" (furo Ø 3/8" / 10 mm a 1" / 25 mm; comprimento do corpo de 30 mm a 80 mm; profundidade do furo de 30–40 mm a 80–100 mm).
  - **Cargas médias de ruptura à tração:** 570 kgf (1/4") a 4.290 kgf (3/4").
  - **Cargas de cisalhamento:** não informadas na tabela (pág. 11).

### Walsywa Golpe de Metal
- **Tipo:** Chumbador mecânico de expansão por percussão (pino batido a martelo) para cargas leves (`walsywa_fix_mec.pdf`, pág. 12).
- **Características (`walsywa_fix_mec.pdf`, pág. 12):**
  - "Medidas nominais de 3/16" e 1/4"";
  - "Pino em aço zincado e corpo expansível em zamak";
  - "Excelente acabamento após instalado";
  - "Instalação fácil e rápida, através da peça a fixar" (bate-se na cabeça do pino com martelo até alojar totalmente na cabeça do chumbador);
  - "Aplicação em concreto ou alvenaria".
- **Uso recomendado pelo fabricante (`walsywa_fix_mec.pdf`, pág. 12):**
  - "Ideal para fixações de equipamentos elétricos, suspensão de tubulações e aplicações leves em geral".
- **Usos típicos:**
  - Fixações de equipamentos elétricos, suspensão de tubulações e aplicações leves em geral (pág. 12).
- **Material-base aceito:**
  - "Aplicação em concreto ou alvenaria" (pág. 12; tabela considerada para concreto $f_{ck} = 20\text{ MPa}$).
- **Pontos de atenção / limitações:**
  - **Não é recuperável / não permite desmontagem fácil:** o pino liso é cravado até ficar totalmente alojado na cabeça do chumbador (pág. 12) `[inferência]`.
  - Restrito a aplicações leves e apenas duas bitolas pequenas (3/16" x 7/8" e 1/4" x 1 1/4", pág. 12).
- **Faixa de diâmetros e de cargas (`walsywa_fix_mec.pdf`, pág. 12 — concreto $f_{ck} = 20\text{ MPa}$):**
  - Furo Ø 3/16" (corpo Ø 4,8 mm, prof. furo 26 mm, esp. máx. 6,5 mm) e Ø 1/4" (corpo Ø 6,4 mm, prof. furo 36 mm, esp. máx. 13 mm).
  - **Cargas de ruptura à tração:** 150 kgf (3/16") e 350 kgf (1/4").
  - **Cargas de ruptura ao cisalhamento:** 200 kgf (3/16") e 710 kgf (1/4").

### Walsywa WQA
- **Tipo:** Chumbador químico de ampola (cápsula de vidro com resina aplicada com prisioneiro WQA introduzido com martelete) (`WALSYWA_fixacao_quimica.pdf`, pág. 05).
- **Características (`WALSYWA_fixacao_quimica.pdf`, págs. 04–05):**
  - "medidas nominais de 5/16" a 1"";
  - "alta resistência à tração";
  - "não cria tensão no concreto, o que permite menores distâncias entre chumbadores";
  - Aplicado com prisioneiro WQA (rosca UNC) e adaptador/soquete SDS Plus (códigos 490 040 e 490 041) acoplado ao martelete (pág. 05).
- **Uso recomendado pelo fabricante (`WALSYWA_fixacao_quimica.pdf`, pág. 05):**
  - "indicado para cargas dinâmicas" (e de choque, conforme princípios gerais da pág. 04).
- **Usos típicos:**
  - Ancoragens estruturais submetidas a cargas dinâmicas, vibrações ou choques, ou próximas a bordas/entre chumbadores em concreto (págs. 04–05); bases de máquinas pesadas e equipamentos vibratórios `[inferência]`.
- **Material-base aceito:**
  - Concreto seco ou úmido ($f_{ck} \ge 20\text{ MPa}$, $f_{ck} \ge 40\text{ MPa}$, ou $f_{ck} = 15\text{ MPa}$ com redução de 20% na tração, pág. 05). Não indicado para furos ocos pois depende do rompimento e confinamento da ampola pelo martelete `[inferência]`.
- **Pontos de atenção / limitações (`WALSYWA_fixacao_quimica.pdf`, pág. 05):**
  - **Reduções obrigatórias de carga (notas 1 a 4 e texto, pág. 05):**
    - Distância mínima entre chumbadores (4 a 13 cm vs. 16 a 42 cm para carga máxima): **reduzir cargas em 30%**;
    - Distância mínima da borda (4 a 13 cm vs. 8 a 25 cm para carga máxima): **reduzir tração em 40%** e **cisalhamento em 50%**;
    - Concreto com $f_{ck} = 15\text{ MPa}$: **reduzir carga de tração em 20%**;
    - Temperatura do material-base entre 100 °C e 120 °C: **reduzir cargas em 30%**.
  - **Tempo de cura (pág. 05):** varia de **20 min** (concreto seco > 20 °C) a **5 horas** (-5 a 0 °C seco), dobrando em **concreto úmido** (**40 min** acima de 20 °C até **10 horas** entre -5 e 0 °C).
  - "Para garantia das resistências apresentadas na tabela acima, deve-se sempre utilizar o sistema completo Walsywa" (ampola + prisioneiro WQA) (pág. 05).
  - Haste colada permanentemente (não recuperável) `[inferência]`.
- **Faixa de diâmetros e de cargas (`WALSYWA_fixacao_quimica.pdf`, pág. 05 — cargas recomendadas em kN):**
  - Prisioneiro rosca UNC de 5/16" a 1" (furo Ø 10 mm x 80 mm a Ø 28 mm x 210 mm; torque máx. de 10 N·m a 200 N·m).
  - **Carga de tração recomendada:** 5 kN a 43 kN ($f_{ck} \ge 20\text{ MPa}$) e 6,2 kN a 52,5 kN ($f_{ck} \ge 40\text{ MPa}$).
  - **Carga de cisalhamento recomendada ($f_{ck} \ge 20\text{ MPa}$):** 4,7 kN (5/16") a 37 kN (1").

### Walsywa WQI 44
- **Tipo:** Chumbador químico de injeção bicomponente de cura rápida (`WALSYWA_fixacao_quimica.pdf`, págs. 06–07).
- **Características (`WALSYWA_fixacao_quimica.pdf`, págs. 06–07):**
  - "alta resistência à tração";
  - "não cria tensão no concreto, o que permite menores distâncias entre chumbadores";
  - "embalagens de 345 ml e 300 ml (para utilizar com ferramenta profissional de silicone)";
  - Aplicado com bico misturador, inserindo o prisioneiro/vergalhão com movimento giratório; para tijolo oco usa camisa (tabela "fixação de barra roscada ou prisioneiro WQI em tijolo oco": pequena Ø 15 x 85 mm e grande Ø 15 x 130 mm) (págs. 06–07).
- **Uso recomendado pelo fabricante (`WALSYWA_fixacao_quimica.pdf`, pág. 06):**
  - "indicado para aplicações em concreto ou bloco oco" (e tijolo furado/oco com camisa, págs. 06–07).
- **Usos típicos:**
  - Fixação de vergalhões (8 a 32 mm) e barras roscadas/prisioneiros (5/16" a 1") em concreto, e barras roscadas/prisioneiros em tijolo furado / bloco oco com camisa (págs. 06–07).
- **Material-base aceito:**
  - Concreto (tabelas em concreto 28 MPa, pág. 07) e alvenaria oca / bloco oco / tijolo furado (com camisa, págs. 05–07), além de tijolo maciço e pedra natural (catálogo mecânico, págs. 04–05).
- **Pontos de atenção / limitações (`WALSYWA_fixacao_quimica.pdf`, pág. 07):**
  - **Sem dados de cisalhamento e sem dados de carga em tijolo oco:** as tabelas da pág. 07 informam apenas **cargas de tração em concreto 28 MPa** (ruptura e recomendada); para tijolo oco, a tabela informa apenas medidas da camisa, furo (16 x 90 mm e 16 x 135 mm) e rendimento (8 a 12 aplicações por cartucho). Além disso, na tabela de 5/16" e vergalhão 8 mm as células de carga estão em branco (pág. 07).
  - **Tempo de manipulação curto em calor:** tempo para aplicação de 40 min (0–5 °C) a apenas **3 min** (30–40 °C); tempo final de cura de 4 horas (0–5 °C) a **30 min** (30–40 °C) (pág. 07).
  - "Ensaios de arrancamento devem ser feitos após 24 horas do tempo final de cura" (pág. 07).
  - Haste não recuperável após cura `[inferência]`.
- **Faixa de diâmetros e de cargas (`WALSYWA_fixacao_quimica.pdf`, pág. 07 — concreto 28 MPa):**
  - **Vergalhão:** Ø 8 mm a 32 mm (furo Ø 12 x 80 mm a Ø 40 x 290 mm). Tração (de 10 mm a 32 mm): ruptura de **3.530 kg a 32.467 kg**; recomendada de **1.625 kg a 15.020 kg**.
  - **Barra roscada / prisioneiro:** Ø 5/16" a 1" (furo Ø 10 x 80 mm a Ø 28 x 210 mm). Tração (de 3/8" a 1"): ruptura de **2.471 kg a 16.230 kg**; recomendada de **1.138 kg a 7.508 kg**.

### Walsywa WQE 500
- **Tipo:** Chumbador químico de injeção bicomponente de cura lenta (maior tempo de trabalho) (`WALSYWA_fixacao_quimica.pdf`, págs. 08–09).
- **Características (`WALSYWA_fixacao_quimica.pdf`, págs. 08–09):**
  - "alta resistência à tração";
  - "não cria tensão no concreto, o que permite menores distâncias entre chumbadores";
  - "maior tempo de curo (facilita ajustes)";
  - "baixo odor";
  - "embalagem de 400 ml" (aplicada com bico misturador e ferramenta para embalagem 400 ml).
- **Uso recomendado pelo fabricante (`WALSYWA_fixacao_quimica.pdf`, pág. 08):**
  - "indicado para aplicações em concreto" especialmente onde se requer "maior tempo de curo (facilita ajustes)" e "baixo odor".
- **Usos típicos:**
  - Ancoragem de vergalhões (8 a 32 mm) e barras roscadas/prisioneiros (5/16" a 1") em concreto (pág. 09), sobretudo em ambientes internos/fechados (pelo baixo odor) ou montagens que exigem tempo prolongado para alinhamento/ajuste `[inferência]`.
- **Material-base aceito:**
  - Concreto (tabelas para concreto 28 MPa, págs. 08–09). **Não** possui indicação nem tabela para bloco/tijolo oco.
- **Pontos de atenção / limitações (`WALSYWA_fixacao_quimica.pdf`, pág. 09):**
  - **Sem dados de cisalhamento:** as tabelas da pág. 09 trazem apenas cargas de tração (ruptura e recomendada).
  - **Tempo final de cura longo:** requer de **3 horas** (30–40 °C) a **30 horas** (0–5 °C) para receber carga (sendo 4 horas entre 20–30 °C e 12 horas entre 10–20 °C), com tempo para aplicação de 15 min (30–40 °C) a 4 horas (0–5 °C) (pág. 09).
  - "Ensaios de arrancamento devem ser feitos após 24 horas do tempo final de cura" (pág. 09).
  - Para diâmetros de 8 mm a 25 mm (e todas as barras roscadas de 3/8" a 1"), as cargas de tração recomendadas do WQE 500 são inferiores às do WQI 44 no mesmo diâmetro/furo (págs. 07 e 09).
  - Haste não recuperável `[inferência]`.
- **Faixa de diâmetros e de cargas (`WALSYWA_fixacao_quimica.pdf`, pág. 09 — concreto 28 MPa):**
  - **Vergalhão:** Ø 8 mm a 32 mm (furo Ø 12 x 80 mm a Ø 40 x 290 mm). Tração: ruptura de **2.338 kg a 32.940 kg**; recomendada de **779 kg a 13.018 kg**.
  - **Barra roscada / prisioneiro:** Ø 5/16" a 1" (furo Ø 10 x 80 mm a Ø 28 x 210 mm). Tração: ruptura de **2.338 kg a 17.352 kg**; recomendada de **779 kg a 5.744 kg**.

### Walsywa PU 500
- **Tipo:** Espuma expansiva de poliuretano para fixação não estrutural `[inferência]`, assentamento, preenchimento e vedação (`WALSYWA_fixacao_quimica.pdf`, págs. 10–11).
- **Características (`WALSYWA_fixacao_quimica.pdf`, págs. 10–11):**
  - "embalagens de 340 g e 480 g com gatilho e bico aplicador" (rendimento até 18 L e 26 L de espuma curada);
  - "rápida cura", "estanqueidade à água e umidade", "isolação acústica", "retardante de fogo";
  - "inofensivo ao meio a ambiente por não ser poluente", "odor neutro após a secagem", "possui boa estabilidade térmica";
  - Após secagem, "permite trabalhos como serrar, cortar, lixar, rebocar e até pintar" (pág. 10);
  - Expande de 3 a 4 vezes o volume aplicado (preencher até 1/3 do vão sobre superfície umedecida, pág. 11).
- **Uso recomendado pelo fabricante (`WALSYWA_fixacao_quimica.pdf`, pág. 10):**
  - "A Espuma **Walsywa PU 500** é indicada para aplicações domésticas ou profissionais em assentamentos de portas, janelas, aparelhos de ar condicionado, preenchimento de vãos para isolamento e fixação de dutos de ar, tubos e canos, preenchimento de juntas dedilatação e espaços vazios além de pequenos reparos."
- **Usos típicos (`WALSYWA_fixacao_quimica.pdf`, págs. 10–11):**
  - "Fixação e vedação de portas", "Fixação e vedação de janelas", "Fixação e vedação de produtos em áreas úmidas", "Fixação e vedação de objetos diversos", "Vedação", "Fixação e vedação de tubos e canos" (pág. 11).
- **Material-base aceito:**
  - Alvenaria, concreto e vãos de construção civil em geral (superfícies umedecidas com água limpa antes da aplicação, págs. 10–11).
- **Pontos de atenção / limitações (`WALSYWA_fixacao_quimica.pdf`, págs. 10–11):**
  - **Não é chumbador estrutural / sem dados de carga mecânica:** não possui tabela de tração ou cisalhamento em kgf/kN `[inferência]`.
  - **Sensível à radiação solar direta:** "É resistente à água e ao calor, mas não deve ficar exposto diretamente ao sol. Para tanto é necessário que tenha um revestimento" (pág. 10).
  - **Tempos e temperaturas (págs. 10–11):** livre para toque após 10 min; livre para corte após 25 min (instrução da pág. 11 recomenda cortar o excesso após no mínimo 2 h); livre para receber cargas e abrir/fechar portas após **3 horas**; cura total entre **12 e 24 horas**. Temperatura de trabalho: **5 °C a 30 °C**; limites de temperatura após curada: **-30 °C a 80 °C**.
- **Faixa de diâmetros e de cargas (`WALSYWA_fixacao_quimica.pdf`, pág. 10):**
  - Não aplicável (fornecida em latas de 340 g / 18 L e 480 g / 26 L; sem valores de carga em kgf ou kN no catálogo).

---

## 3. Tabela Comparativa Final

| Linha | Tipo | Tração | Cisalhamento | Carga Dinâmica / Vibração | Concreto Fissurado / Borda Próxima | Recuperável | Observação |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **Walsywa CB** | Mecânico — expansão por torque (parafuso) | 1.253 a 7.790 kgf (ruptura, 20 MPa) | 800 a 12.080 kgf (ruptura, 20 MPa) | Admite com CS = 7 (baixo risco) ou 15 (alto risco) (pág. 07) | Fissurado: desfavorável (trincas causam falha, pág. 06). Borda mín.: $1,5 \times P$ | Sim ("Recuperabilidade", pág. 08) | Rosca 1/4" a 1"; aço zincado brilhante (1/4" também em classe 8.8). |
| **Walsywa CBPL** | Mecânico — expansão por torque c/ prolongador (parafuso) | 1.839 a 11.676 kgf (ruptura, 20 MPa) | 800 a 12.080 kgf (ruptura, 20 MPa) | Admite com CS = 7 ou 15 (pág. 07) | Fissurado: desfavorável (pág. 06). Borda mín.: $1,5 \times P$ | Sim ("Recuperabilidade", pág. 08) | Prolongador liso para maior embutimento e maior tração (pág. 08). |
| **Walsywa CBE** | Mecânico — expansão por torque (prisioneiro) | 1.253 a 7.790 kgf (ruptura, 20 MPa) | 800 a 12.080 kgf (ruptura, 20 MPa) | Admite com CS = 7 ou 15 (pág. 07) | Fissurado: desfavorável (pág. 06). Borda mín.: $1,5 \times P$ | Sim ("Recuperabilidade", pág. 09) | Prisioneiro facilita a fixação; cargas iguais às do CB (pág. 09). |
| **Walsywa CBEPL** | Mecânico — expansão por torque c/ prolongador (prisioneiro) | 1.839 a 11.676 kgf (ruptura, 20 MPa) | 800 a 12.080 kgf (ruptura, 20 MPa) | Admite com CS = 7 ou 15 (pág. 07) | Fissurado: desfavorável (pág. 06). Borda mín.: $1,5 \times P$ | Sim ("Recuperabilidade", pág. 09) | Prisioneiro + prolongador liso; cargas iguais às do CBPL (pág. 09). |
| **Walsywa WB** | Mecânico — expansão por torque (pino/presilha) | 476 a 7.708 kgf (ruptura, 20 MPa, 1/4"–3/4") | 744 a 9.691 kgf (ruptura, 20 MPa, 1/4"–3/4") | Admite com CS = 7 ou 15 (pág. 07) | Fissurado: desfavorável (pág. 06). Borda mín.: $1,5 \times P$ | Não informado (pino não extraível `[inferência]`) | "Chumbador ideal para esforço de cisalhamento"; furo = Ø nominal; opção inox (pág. 10). |
| **Walsywa WLT** | Mecânico — expansão por percussão (rosca interna) | 570 a 4.290 kgf (ruptura, 20 MPa, 1/4"–3/4") | Não informado na tabela (embora texto cite tração + cisalhamento) | Admite com CS = 7 ou 15 (pág. 07) | Fissurado: desfavorável (pág. 06). Borda mín.: $1,5 \times P$ | Não informado (parafuso removível `[inferência]`) | Usa batedor especial; indicado para suspensão de tubulações (pág. 11). |
| **Walsywa Golpe de Metal** | Mecânico — expansão por percussão (pino + corpo zamak) | 150 e 350 kgf (ruptura, 20 MPa, 3/16" e 1/4") | 200 e 710 kgf (ruptura, 20 MPa, 3/16" e 1/4") | Admite com CS = 7 ou 15 (pág. 07) | Fissurado: desfavorável (pág. 06). Borda mín.: $1,5 \times P$ | Não informado (permanente `[inferência]`) | Cargas leves em concreto ou alvenaria; instalação rápida a martelo (pág. 12). |
| **Walsywa WQA** | Químico — ampola (c/ prisioneiro WQA) | 5 a 43 kN (20 MPa) / 6,2 a 52,5 kN (40 MPa) recomendadas | 4,7 a 37 kN recomendadas (20 MPa) | "indicado para cargas dinâmicas" e choque (págs. 04–05) | Concreto fissurado: não informado. Borda próxima: 4 a 13 cm (reduzir tração 40% e corte 50%) | Não informado (colado `[inferência]`) | Não cria tensão no concreto; reduzir 30% entre 100–120 °C ou dist. mín. entre pinos (pág. 05). |
| **Walsywa WQI 44** | Químico — injeção (300 / 345 ml) | Recomendada (28 MPa): 1.138–7.508 kg (barra) / 1.625–15.020 kg (verg.) | Não informado | Excelente perfil para cargas dinâmicas e de choque (pág. 04) | Concreto fissurado: não informado. Borda/entre pinos: permite menores distâncias (pág. 06) | Não informado (colado `[inferência]`) | Concreto ou bloco/tijolo oco (com camisa); cura rápida (30 min a 4 h, pág. 07). |
| **Walsywa WQE 500** | Químico — injeção (400 ml) | Recomendada (28 MPa): 779–5.744 kg (barra) / 779–13.018 kg (verg.) | Não informado | Excelente perfil para cargas dinâmicas e de choque (pág. 04) | Concreto fissurado: não informado. Borda/entre pinos: permite menores distâncias (pág. 08) | Não informado (colado `[inferência]`) | Só concreto; baixo odor; maior tempo de cura/aplicação para ajustes (3 h a 30 h, págs. 08–09). |
| **Walsywa PU 500** | Químico — espuma de poliuretano (não estrutural `[inferência]`) | Não informado | Não informado | Não informado | Não informado | Não informado | Assentamento/vedação de portas, janelas e tubulações; proteger do sol direto (pág. 10). |

---

## 4. Como Escolher (Árvore de Decisão)

1. **Qual é a natureza da aplicação (estrutural/mecânica vs. vedação/assentamento)?**
   - Assentamento e vedação de portas, janelas, ar-condicionado, preenchimento de vãos, juntas de dilatação ou fixação/isolamento de tubos sem função estrutural → **Walsywa PU 500** (`WALSYWA_fixacao_quimica.pdf`, págs. 10–11).
   - Ancoragem de elementos com requisito de carga mecânica → seguir para o passo 2 `[inferência]`.

2. **Qual é o material-base?**
   - **Tijolo estrutural oco / bloco de concreto oco / tijolo furado:** não usar chumbadores mecânicos expansivos de alto torque; usar chumbador químico de injeção com camisa → **Walsywa WQI 44** (`walsywa_fix_mec.pdf`, pág. 05; `WALSYWA_fixacao_quimica.pdf`, págs. 06–07).
   - **Tijolo comum maciço / alvenaria em cargas leves:**
     - Equipamentos elétricos, suspensão leve de tubulações (até 150–350 kgf de ruptura à tração) → **Walsywa Golpe de Metal** (`walsywa_fix_mec.pdf`, pág. 12).
     - Baixas cargas com expansivos ou químicos de injeção → **Walsywa WQI 44** ou mecânicos leves (`walsywa_fix_mec.pdf`, pág. 05).
   - **Pedra natural (granito/mármore ~ concreto 30 MPa; sedimentares ~ concreto 14 MPa):** aceita fixadores metálicos e químicos (`walsywa_fix_mec.pdf`, págs. 04–05); para placas de granito em fachadas, o fabricante recomenda especificamente o **Walsywa WB** (pág. 10).
   - **Concreto normal:** aceita todas as linhas mecânicas e químicas (seguir para o passo 3).

3. **Há distâncias pequenas à borda ou entre chumbadores (menores que $1,5P$ da borda ou $3P$ entre pinos)?**
   - **Sim:** chumbadores mecânicos expansivos geram tensão e risco de trinca/interseção de cones (`walsywa_fix_mec.pdf`, págs. 06–07). Escolher **fixação química** (**Walsywa WQA**, **Walsywa WQI 44** ou **Walsywa WQE 500**), pois "não cria tensão no concreto, o que permite menores distâncias entre chumbadores" (`WALSYWA_fixacao_quimica.pdf`, págs. 04–08).
     - Se precisar de valores tabelados explícitos de distância mínima e fator de redução de carga no software → **Walsywa WQA** (traz distâncias mínimas de 4 a 13 cm e reduções de 30%/40%/50% na pág. 05) `[inferência]`.

4. **O carregamento é predominantemente dinâmico, de vibração ou de choque?**
   - **Sim:** priorizar fixação química ("excelente perfil de resistência à cargas dinâmicas e de choque", pág. 04), especialmente **Walsywa WQA** ("indicado para cargas dinâmicas", pág. 05). Caso se use chumbador mecânico, aplicar obrigatoriamente coeficiente de segurança **7 a 15** (dinâmica) ou **5 a 10** (choque) (`walsywa_fix_mec.pdf`, pág. 07).

5. **Há solicitação crítica de cisalhamento (corte) ou ambiente corrosivo (necessidade de inox)?**
   - **Alta resistência ao cisalhamento / fixação passante (furo na peça = furo na broca) / opção em aço inoxidável:** escolher **Walsywa WB** ("Chumbador ideal para esforço de cisalhamento", "Acabamento zincado brilhante ou aço inoxidável", `walsywa_fix_mec.pdf`, pág. 10).
   - **Nota para cálculo de cisalhamento no software `[inferência]`:** somente **CB**, **CBPL**, **CBE**, **CBEPL**, **WB**, **Golpe de Metal** e **WQA** possuem cargas de cisalhamento tabeladas nos catálogos; **WLT**, **WQI 44** e **WQE 500** não informam cargas de cisalhamento nas tabelas.

6. **Qual é a exigência de tração, montagem/remoção (recuperabilidade) ou tipo de haste?**
   - **Fixação de vergalhão de construção (8 a 32 mm):** usar químico de injeção **Walsywa WQI 44** ou **Walsywa WQE 500** (`WALSYWA_fixacao_quimica.pdf`, págs. 07 e 09).
   - **Suspensão de tubulações no teto com rosca interna:** **Walsywa WLT** (cargas médias, rosca 1/4" a 3/4", pág. 11) ou **Walsywa Golpe de Metal** (aplicações leves, 3/16" e 1/4", pág. 12).
   - **Máquinas, equipamentos e estruturas metálicas com recuperabilidade (sem tempo de cura):**
     - Com **parafuso sextavado**: **Walsywa CB** (tração normal) ou **Walsywa CBPL** (com prolongador para maior tração) (`walsywa_fix_mec.pdf`, pág. 08).
     - Com **prisioneiro e porca** ("facilita a fixação"): **Walsywa CBE** (tração normal) ou **Walsywa CBEPL** (com prolongador para maior tração) (`walsywa_fix_mec.pdf`, pág. 09).

7. **Entre os químicos (WQA vs. WQI 44 vs. WQE 500), qual tempo de cura e condição de aplicação se aplica?**
   - **Cura mais rápida em concreto (20 a 30 min acima de 10 °C seco) + dados de tração e cisalhamento + concreto úmido tabelado:** **Walsywa WQA** (pág. 05).
   - **Cura rápida em injeção (30 min a 4 h) + maiores cargas recomendadas em barra/vergalhão ou uso em bloco oco:** **Walsywa WQI 44** (págs. 06–07).
   - **Necessidade de maior tempo em aberto para ajustes (15 min a 4 h para aplicar; 3 h a 30 h de cura) e baixo odor:** **Walsywa WQE 500** (págs. 08–09).

---

## 5. Diferenças entre fixações (para comparação no software)

### 5.1 Atributos padronizados

| linha | família | acionamento | elemento de fixação | faixa de diâmetro | concreto do ensaio (MPa) | natureza da carga tabelada | tração tabelada | corte tabelado | carga dinâmica | distâncias reduzidas | materiais-base | recuperável | tempo de cura | acabamento | páginas |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :---: | :---: | :--- | :--- | :--- | :---: | :--- | :--- | :--- |
| **Walsywa CB** | mecânico | torque | parafuso | 1/4" a 1" | 20 | ruptura média | sim | sim | admite com coeficiente | não | concreto normal; pedra natural e tijolo comum maciço (baixas cargas) | sim | nenhum | aço zincado brilhante (Ø 1/4" também em classe 8.8) | `walsywa_fix_mec.pdf`, págs. 04–08 |
| **Walsywa CBPL** | mecânico | torque | parafuso | 1/4" a 1" | 20 | ruptura média | sim | sim | admite com coeficiente | não | concreto normal; pedra natural e tijolo comum maciço (baixas cargas) | sim | nenhum | aço zincado brilhante (c/ prolongador liso; Ø 1/4" também em classe 8.8) | `walsywa_fix_mec.pdf`, págs. 04–08 |
| **Walsywa CBE** | mecânico | torque | prisioneiro | 1/4" a 1" | 20 | ruptura média | sim | sim | admite com coeficiente | não | concreto normal; pedra natural e tijolo comum maciço (baixas cargas) | sim | nenhum | aço zincado brilhante (Ø 1/4" também em classe 8.8) | `walsywa_fix_mec.pdf`, págs. 04–07, 09 |
| **Walsywa CBEPL** | mecânico | torque | prisioneiro | 1/4" a 1" | 20 | ruptura média | sim | sim | admite com coeficiente | não | concreto normal; pedra natural e tijolo comum maciço (baixas cargas) | sim | nenhum | aço zincado brilhante (c/ prolongador liso; Ø 1/4" também em classe 8.8) | `walsywa_fix_mec.pdf`, págs. 04–07, 09 |
| **Walsywa WB** | mecânico | torque | corpo roscado | 1/4" a 1" (tabela: 1/4" a 3/4"; pág. 14 cita 5/16") | 20 | ruptura média | sim | sim | admite com coeficiente | não | concreto normal; pedra natural (granito/mármore); tijolo comum maciço (baixas cargas) | não informado | nenhum | zincado brilhante ou aço inoxidável | `walsywa_fix_mec.pdf`, págs. 04–07, 10, 14 |
| **Walsywa WLT** | mecânico | percussão | rosca interna | 1/4" a 3/4" | 20 | ruptura média | sim | não | admite com coeficiente | não | concreto normal; pedra natural e tijolo comum maciço (baixas cargas) | não informado | nenhum | zincado brilhante | `walsywa_fix_mec.pdf`, págs. 04–07, 11 |
| **Walsywa Golpe de Metal** | mecânico | percussão | pino | 3/16" e 1/4" | 20 | ruptura | sim | sim | admite com coeficiente | não | concreto ou alvenaria | não informado | nenhum | pino em aço zincado e corpo expansível em zamak | `walsywa_fix_mec.pdf`, págs. 04–07, 12 |
| **Walsywa WQA** | químico | ampola | prisioneiro | 5/16" a 1" | ≥ 20 e ≥ 40 (e 15 MPa c/ redução de 20% na tração) | recomendada | sim | sim | indicado | sim | concreto seco ou úmido | não informado | 20 min a 5 h (seco); 40 min a 10 h (úmido) | não informado (prisioneiro rosca UNC) | `WALSYWA_fixacao_quimica.pdf`, págs. 04–05 |
| **Walsywa WQI 44** | químico | injeção | barra roscada ou vergalhão | barra/prisioneiro 5/16" a 1" (cargas de 3/8" a 1"); vergalhão 8 a 32 mm (cargas de 10 a 32 mm) | 28 | ruptura e recomendada | sim | não | indicado | sim | concreto; bloco oco / tijolo furado (com camisa); tijolo maciço e pedra natural | não informado | 30 min a 4 h (aplicação: 3 a 40 min) | não informado | `WALSYWA_fixacao_quimica.pdf`, págs. 04, 06–07; `walsywa_fix_mec.pdf`, págs. 04–05 |
| **Walsywa WQE 500** | químico | injeção | barra roscada ou vergalhão | barra/prisioneiro 5/16" a 1"; vergalhão 8 a 32 mm | 28 | ruptura e recomendada | sim | não | indicado | sim | concreto (e pedra natural / tijolo maciço pelos princípios gerais) | não informado | 3 h a 30 h (aplicação: 15 min a 4 h) | não informado | `WALSYWA_fixacao_quimica.pdf`, págs. 04, 08–09; `walsywa_fix_mec.pdf`, págs. 04–05 |
| **Walsywa PU 500** | não estrutural `[inferência]` | espuma | nenhum | não aplicável (340 g / 480 g) | não aplicável | nenhuma | não | não | não informado | não informado | alvenaria, concreto e vãos em geral (superfícies umedecidas) | não informado | 3 h p/ receber cargas; 12 a 24 h cura total (toque: 10 min; corte: 25 min / mín. 2 h) | espuma amarelada `[inferência]` (aceita cortar, lixar, rebocar e pintar; exige revestimento ao sol) | `WALSYWA_fixacao_quimica.pdf`, págs. 10–11 |

#### Legenda do vocabulário fechado

- **família:**
  - `mecânico`: chumbador expansivo metálico que trabalha por atrito/expansão contra a parede do furo (`walsywa_fix_mec.pdf`, págs. 04–12).
  - `químico`: fixador à base de resina + catalisador que trabalha por aderência ao material-base e intertravamento com a haste (`WALSYWA_fixacao_quimica.pdf`, pág. 04).
  - `não estrutural` `[inferência]`: produto para assentamento, vedação e preenchimento de vãos sem tabela de capacidade de carga mecânica em kgf/kN (`WALSYWA_fixacao_quimica.pdf`, págs. 10–11).
- **acionamento:**
  - `torque`: expansão gerada pelo aperto controlado do parafuso ou da porca tracionando o cone ou o corpo roscado (`walsywa_fix_mec.pdf`, págs. 08–10).
  - `percussão`: expansão gerada por golpe de martelo (sobre batedor especial em WLT ou diretamente sobre o pino em Golpe de Metal, `walsywa_fix_mec.pdf`, págs. 11–12).
  - `ampola`: cápsula de vidro contendo resina introduzida no furo e rompida pelo prisioneiro acionado com martelete e adaptador SDS Plus (`WALSYWA_fixacao_quimica.pdf`, pág. 05).
  - `injeção`: cartucho bicomponente aplicado no furo (ou em camisa para oco) através de bico misturador antes de inserir a haste girando (`WALSYWA_fixacao_quimica.pdf`, págs. 06–09).
  - `espuma`: aerossol de poliuretano aplicado com gatilho que expande de 3 a 4 vezes em contato com superfície umedecida (`WALSYWA_fixacao_quimica.pdf`, págs. 10–11).
- **elemento de fixação:**
  - `parafuso`: parafuso cabeça sextavada fornecido com arruela, jaqueta e cone (e prolongador liso no CBPL) (`walsywa_fix_mec.pdf`, pág. 08).
  - `prisioneiro`: barra roscada (prisioneiro) fornecida com porca, arruela, jaqueta e cone (e prolongador no CBEPL) ou prisioneiro UNC específico do sistema WQA (`walsywa_fix_mec.pdf`, pág. 09; `WALSYWA_fixacao_quimica.pdf`, pág. 05).
  - `corpo roscado`: pino inteiriço com ponta cônica, presilha expansiva, porca e arruela pré-montadas (`walsywa_fix_mec.pdf`, pág. 10).
  - `rosca interna`: bucha cilíndrica com rosca fêmea e tolete cônico cativo interno; recebe parafuso ou barra roscada avulsa (`walsywa_fix_mec.pdf`, pág. 11).
  - `pino`: pino liso de aço cravado a martelo dentro de corpo expansível em zamak (`walsywa_fix_mec.pdf`, pág. 12).
  - `barra roscada ou vergalhão`: permite ancorar tanto barra roscada/prisioneiro (5/16" a 1") quanto vergalhão de construção (8 a 32 mm) (`WALSYWA_fixacao_quimica.pdf`, págs. 04, 07, 09).
  - `nenhum`: produto sem haste metálica (`WALSYWA_fixacao_quimica.pdf`, págs. 10–11).
- **natureza da carga tabelada:**
  - `ruptura média`: tabela intitulada "Cargas médias*" de tração e/ou cisalhamento obtidas em ensaio no IPT (`walsywa_fix_mec.pdf`, págs. 08–11).
  - `ruptura`: tabela intitulada "cargas de ruptura*" (Golpe de Metal, `walsywa_fix_mec.pdf`, pág. 12) ou coluna "carga de ruptura (kg)" nos químicos de injeção (`WALSYWA_fixacao_quimica.pdf`, págs. 07, 09).
  - `recomendada`: tabela intitulada "Carga de tração/cisalhamento recomendada (kN)" (WQA, pág. 05) ou coluna "carga recomendada (kg)" já minorada pelo fabricante (WQI 44 e WQE 500, págs. 07, 09).
  - `nenhuma`: sem valores de carga mecânica tabelados (`WALSYWA_fixacao_quimica.pdf`, págs. 10–11).
- **tração tabelada / corte tabelado:**
  - `sim`: o catálogo fornece valores numéricos na tabela da linha.
  - `não`: o catálogo não fornece valores numéricos de tração ou de cisalhamento (corte) na tabela da linha.
- **carga dinâmica:**
  - `indicado`: o catálogo declara expressamente "indicado para cargas dinâmicas" (WQA, pág. 05) ou "excelente perfil de resistência à cargas dinâmicas e de choque" para o sistema químico (`WALSYWA_fixacao_quimica.pdf`, pág. 04).
  - `admite com coeficiente`: o catálogo mecânico prevê aplicação sob carga dinâmica e de choque desde que a carga limite de ruptura seja dividida pelo coeficiente de segurança 7 (baixo risco) ou 15 (alto risco) para carga dinâmica, e 5 ou 10 para choque (`walsywa_fix_mec.pdf`, pág. 07).
  - `não informado`: sem menção no catálogo.
- **distâncias reduzidas:**
  - `sim`: não cria tensão de expansão no concreto, permitindo menores distâncias entre chumbadores e à borda (`WALSYWA_fixacao_quimica.pdf`, págs. 04–08; WQA tabela distâncias mínimas reduzidas com fatores de redução de 30%/40%/50% na pág. 05).
  - `não`: exige os distanciamentos mínimos padrão de chumbadores expansivos ($D = 3P$ entre chumbadores e $d = 1,5P$ até a borda) para não interceptar cones nem trincar o concreto (`walsywa_fix_mec.pdf`, págs. 06–07).
  - `não informado`: não aplicável / sem menção.
- **recuperável:**
  - `sim`: o catálogo lista explicitamente "Recuperabilidade" nas Características Técnicas (`walsywa_fix_mec.pdf`, págs. 08–09).
  - `não` / `não informado`: o catálogo não atribui "Recuperabilidade" ao produto (em WB, Golpe de Metal e químicos a haste ou corpo permanece fixo no furo `[inferência]`; em WLT o parafuso sai, mas a bucha fica cravada `[inferência]`).
- **tempo de cura:**
  - `nenhum`: fixação mecânica apta a receber carga imediatamente após a instalação/torque `[inferência]`.
  - `faixa`: intervalo do tempo final de cura conforme temperatura e umidade informados nas tabelas (`WALSYWA_fixacao_quimica.pdf`, págs. 05, 07, 09, 10–11).

---

### 5.2 Comparações em pares

#### 1. Walsywa CB × Walsywa CBE
- **Em que diferem:**
  - **Elemento de fixação:** o **CB** usa **parafuso sextavado** + arruela + jaqueta + cone (pág. 08); o **CBE** usa **prisioneiro roscado + porca** + arruela + jaqueta + cone ("Possui prisioneiro que facilita a fixação", pág. 09).
  - **Profundidade mínima do furo ($F$) e comprimento ($C$):** o **CB** pede furos de **50 a 152 mm** (parafuso de 50 a 152 mm, pág. 08), enquanto o **CBE** pede furos menores na tabela, de **41 a 118 mm** (prisioneiro de 65 a 175 mm, pág. 09). Como $D = 3P$ e $d = 1,5P$ (pág. 07), a menor profundidade tabelada do CBE resulta em menores distanciamentos mínimos calculados a partir de $F$ `[inferência]`.
  - **Espessura máxima a fixar ($G$):** o **CBE** admite peças mais espessas em quase todas as bitolas (**16 a 35 mm**, pág. 09) do que o **CB** (**7 a 35 mm**, sendo 7–19 mm de 1/4" a 3/4", pág. 08).
- **Escolha CB quando:**
  - Preferir fixação por cabeça de parafuso sextavado e quiser poder remover o parafuso deixando a superfície livre de prisioneiro saliente `[inferência]` (pág. 08).
- **Escolha CBE quando:**
  - Quiser o prisioneiro saliente ("facilita a fixação", posicionamento e alinhamento da peça antes de pôr a porca, pág. 09), quando a espessura da peça a fixar superar o limite do CB (ex.: em 1/2", CBE aceita 26 mm vs. 18 mm do CB) ou quando a espessura da base exigir a menor profundidade mínima de furo tabelada para o CBE (ex.: em 1/2", furo mín. 60 mm no CBE vs. 76 mm no CB).
- **Mesmo desempenho em (comparação numérica direta — mesmo concreto 20 MPa e mesma natureza de carga: ruptura média):**
  - Ambos têm **exatamente as mesmas cargas médias de ruptura à tração e ao cisalhamento** em todos os 7 diâmetros (1/4" a 1"), mesmo diâmetro de furo ($\varnothing B$), mesmo comprimento de jaqueta ($D$), mesmo acabamento zincado brilhante (e opção classe 8.8 em 1/4") e mesma recuperabilidade:
    - **1/4"** (furo 3/8" / 10 mm): tração **1.253 kgf**, corte **800 kgf** (furo mín. CB 50 mm vs. CBE 41 mm);
    - **5/16"** (furo 1/2" / 13 mm): tração **1.600 kgf**, corte **1.190 kgf** (furo mín. CB 57 mm vs. CBE 44 mm);
    - **3/8"** (furo 9/16" / 14 mm): tração **2.107 kgf**, corte **1.617 kgf** (furo mín. CB 63 mm vs. CBE 47 mm);
    - **1/2"** (furo 3/4" / 19 mm): tração **2.523 kgf**, corte **2.897 kgf** (furo mín. CB 76 mm vs. CBE 60 mm);
    - **5/8"** (furo 7/8" / 22 mm): tração **3.333 kgf**, corte **4.967 kgf** (furo mín. CB 89 mm vs. CBE 70 mm);
    - **3/4"** (furo 1" / 26 mm): tração **4.320 kgf**, corte **6.573 kgf** (furo mín. CB 114 mm vs. CBE 92 mm);
    - **1"** (furo 1 1/4" / 32 mm): tração **7.790 kgf**, corte **12.080 kgf** (furo mín. CB 152 mm vs. CBE 118 mm).
- **Fonte:** `walsywa_fix_mec.pdf`, págs. 07, 08 e 09.

#### 2. Walsywa CB × Walsywa CBPL
- **Em que diferem:**
  - **Prolongador e profundidade mínima do furo ($F$):** o **CBPL** acrescenta um **prolongador liso** ($E = 22\text{ a }79\text{ mm}$) à jaqueta, exigindo furos mais profundos (**76 a 228 mm** no CBPL vs. **50 a 152 mm** no CB, pág. 08).
  - **Resistência à tração (comparação direta — ruptura média em concreto 20 MPa):** o **CBPL** tem tração **29,6% a 91,2% maior** que o CB no mesmo diâmetro:
    - **1/4"**: CB **1.253 kgf** ($F = 50\text{ mm}$) vs. CBPL **1.839 kgf** ($F = 76\text{ mm}$, $+46,8\%$);
    - **5/16"**: CB **1.600 kgf** ($F = 57\text{ mm}$) vs. CBPL **2.073 kgf** ($F = 82\text{ mm}$, $+29,6\%$);
    - **3/8"**: CB **2.107 kgf** ($F = 63\text{ mm}$) vs. CBPL **2.958 kgf** ($F = 89\text{ mm}$, $+40,4\%$);
    - **1/2"**: CB **2.523 kgf** ($F = 76\text{ mm}$) vs. CBPL **4.824 kgf** ($F = 114\text{ mm}$, $+91,2\%$);
    - **5/8"**: CB **3.333 kgf** ($F = 89\text{ mm}$) vs. CBPL **5.841 kgf** ($F = 127\text{ mm}$, $+75,2\%$);
    - **3/4"**: CB **4.320 kgf** ($F = 114\text{ mm}$) vs. CBPL **6.657 kgf** ($F = 165\text{ mm}$, $+54,1\%$);
    - **1"**: CB **7.790 kgf** ($F = 152\text{ mm}$) vs. CBPL **11.676 kgf** ($F = 228\text{ mm}$, $+49,9\%$).
  - **Distanciamentos mínimos ($D = 3P$, $d = 1,5P$, pág. 07):** por ter maior embutimento, o CBPL exige maiores distâncias entre chumbadores e até a borda que o CB `[inferência]`.
  - **Espessura máxima a fixar ($G$):** ligeiramente diferente em algumas bitolas (ex.: em 3/4", CB aceita 19 mm vs. 10 mm no CBPL; em 1/2", CB aceita 18 mm vs. 20 mm no CBPL, pág. 08).
- **Escolha CB quando:**
  - A carga de tração do CB já atender ao projeto, a espessura de concreto for limitada (furo mais raso) ou as distâncias entre chumbadores/borda forem restritas ao embutimento padrão `[inferência]` (págs. 07–08).
- **Escolha CBPL quando:**
  - Precisar de "maior profundidade de embutimento no material base e consequentemente maior resistência à tração" mantendo o mesmo diâmetro de rosca e de furo (pág. 08).
- **Mesmo desempenho em:**
  - **Cisalhamento idêntico** em todas as bitolas (**800, 1.190, 1.617, 2.897, 4.967, 6.573 e 12.080 kgf** de 1/4" a 1"), mesmo diâmetro de rosca e de furo, mesmo comprimento da jaqueta ($D$), acionamento por torque com parafuso e recuperabilidade (pág. 08).
- **Fonte:** `walsywa_fix_mec.pdf`, págs. 07 e 08.

#### 3. Walsywa CBE × Walsywa CBEPL
- **Em que diferem:**
  - **Prolongador e profundidade mínima do furo ($F$):** o **CBEPL** adiciona prolongador liso ($E = 22\text{ a }79\text{ mm}$), elevando a profundidade mínima do furo de **41–118 mm** (CBE) para **66–204 mm** (CBEPL) e o comprimento do prisioneiro de **65–175 mm** para **90–270 mm** (pág. 09).
  - **Resistência à tração (comparação direta — ruptura média em concreto 20 MPa):** o **CBEPL** entrega exatamente os mesmos ganhos de tração do CBPL sobre o CB:
    - **1/4"**: **1.253 kgf** ($F = 41\text{ mm}$) vs. **1.839 kgf** ($F = 66\text{ mm}$);
    - **5/16"**: **1.600 kgf** ($F = 44\text{ mm}$) vs. **2.073 kgf** ($F = 73\text{ mm}$);
    - **3/8"**: **2.107 kgf** ($F = 47\text{ mm}$) vs. **2.958 kgf** ($F = 76\text{ mm}$);
    - **1/2"**: **2.523 kgf** ($F = 60\text{ mm}$) vs. **4.824 kgf** ($F = 99\text{ mm}$);
    - **5/8"**: **3.333 kgf** ($F = 70\text{ mm}$) vs. **5.841 kgf** ($F = 120\text{ mm}$);
    - **3/4"**: **4.320 kgf** ($F = 92\text{ mm}$) vs. **6.657 kgf** ($F = 162\text{ mm}$);
    - **1"**: **7.790 kgf** ($F = 118\text{ mm}$) vs. **11.676 kgf** ($F = 204\text{ mm}$).
  - **Espessura máxima a fixar ($G$):** o **CBEPL** permite fixar peças mais grossas na maioria das bitolas (**20 a 50 mm** no CBEPL vs. **16 a 35 mm** no CBE; ex.: em 3/4" e 1", 50 mm no CBEPL vs. 32 e 35 mm no CBE, pág. 09).
  - **Distanciamentos mínimos ($D = 3P$, $d = 1,5P$, pág. 07):** maiores no CBEPL devido ao maior embutimento `[inferência]`.
- **Escolha CBE quando:**
  - A tração padrão atender e se desejar furo mais raso e menores distanciamentos mínimos $3P$ / $1,5P$ `[inferência]` (págs. 07 e 09).
- **Escolha CBEPL quando:**
  - Necessitar de prisioneiro com maior resistência à tração e/ou maior espessura máxima de peça a fixar (até 50 mm) (pág. 09).
- **Mesmo desempenho em:**
  - **Cisalhamento idêntico** em todas as bitolas (**800 a 12.080 kgf**), mesmo diâmetro de rosca e de furo, mesma jaqueta ($D$), prisioneiro + porca e recuperabilidade (pág. 09). (Nota: os pares **CBPL × CBEPL** também têm exatamente as mesmas cargas de tração e cisalhamento entre si, diferindo apenas em parafuso vs. prisioneiro, profundidade mínima de furo 76–228 mm vs. 66–204 mm e espessura máxima a fixar 7–30 mm vs. 20–50 mm, págs. 08–09.)
- **Fonte:** `walsywa_fix_mec.pdf`, págs. 07, 08 e 09.

#### 4. Walsywa CB × Walsywa WB
- **Em que diferem:**
  - **Mecanismo e elemento de fixação:** o **CB** expande jaqueta sobre cone com **parafuso sextavado** (pág. 08); o **WB** expande uma **presilha** na ponta de um **corpo roscado** inteiriço com porca e arruela pré-montadas (pág. 10).
  - **Diâmetro do furo e instalação:** no **WB**, o "Diâmetro da broca [é] igual ao nominal do chumbador" e ele "Pode ser instalado através da peça a ser fixada" com martelo (pág. 10). No **CB**, o furo no concreto ($\varnothing B$) é maior que a rosca ($\varnothing A$) (ex.: rosca 1/2" exige furo de 3/4" / 19 mm no CB vs. furo de 1/2" no WB) e é preciso expandir a jaqueta antes, soltar o parafuso, posicionar a peça e dar o torque final (pág. 08).
  - **Acabamento / corrosão:** o **WB** tem opção em **aço inoxidável** além do zincado brilhante (pág. 10); o **CB** é fornecido apenas em aço zincado brilhante (pág. 08).
  - **Recuperabilidade:** o **CB** tem "Recuperabilidade" (parafuso removível, pág. 08); o **WB** não informa recuperabilidade (o pino fica cravado no concreto `[inferência]`, pág. 10).
  - **Faixa tabelada e opções de comprimento:** o **CB** tabela 7 bitolas de **1/4" a 1"** (1 comprimento por bitola, ou 2 contando o CBPL, pág. 08); o **WB** tabela 5 bitolas de **1/4" a 3/4"** (sem 5/16" e sem 1" na tabela da pág. 10, embora o texto cite 1/4" a 1" e a pág. 14 liste WB 5/16"), com **3 comprimentos / profundidades por bitola** (pág. 10).
  - **Comparação numérica por diâmetro (comparável diretamente: ambos são ruptura média em concreto 20 MPa, laudo IPT nº 28 202):**
    - **1/4"**:
      - Tração: **CB** ($F = 50\text{ mm}$) = **1.253 kgf** (CBPL $F = 76\text{ mm}$ = 1.839 kgf) **>** **WB** = **476 kgf** ($F = 30\text{ mm}$), **761 kgf** ($F = 39\text{ mm}$) e **825 kgf** ($F = 65\text{ mm}$).
      - Corte: **CB** = **800 kgf** **>** **WB** = **744 kgf**.
    - **3/8"**:
      - Tração: **CB** ($F = 63\text{ mm}$) = **2.107 kgf** (CBPL $F = 89\text{ mm}$ = 2.958 kgf) **>** **WB** = **1.250 kgf** ($F = 45\text{ mm}$), **1.236 kgf** ($F = 51\text{ mm}$) e **1.919 kgf** ($F = 70\text{ mm}$).
      - Corte: **WB** = **1.695 kgf** **>** **CB** = **1.617 kgf** ($+4,8\%$).
    - **1/2"**:
      - Tração: **CB** ($F = 76\text{ mm}$) = **2.523 kgf** vs. **WB** = **1.730 kgf** ($F = 50\text{ mm}$), **2.260 kgf** ($F = 78\text{ mm}$) e **3.193 kgf** ($F = 110\text{ mm}$). (Nota: com profundidade similar de ~76–78 mm, o CB tem 2.523 kgf vs. 2.260 kgf do WB 12400; já o WB longo 12514 com $F = 110\text{ mm}$ atinge 3.193 kgf, ainda abaixo do CBPL 12412 com $F = 114\text{ mm}$ e 4.824 kgf.)
      - Corte: **WB** = **3.346 kgf** **>** **CB** = **2.897 kgf** ($+15,5\%$).
    - **5/8"**:
      - Tração: **CB** ($F = 89\text{ mm}$) = **3.333 kgf** vs. **WB** = **2.372 kgf** ($F = 59\text{ mm}$), **4.525 kgf** ($F = 97\text{ mm}$) e **4.026 kgf** ($F = 148\text{ mm}$). (O CBPL com $F = 127\text{ mm}$ chega a 5.841 kgf.)
      - Corte: **WB** = **5.494 kgf** **>** **CB** = **4.967 kgf** ($+10,6\%$).
    - **3/4"**:
      - Tração: **CB** ($F = 114\text{ mm}$) = **4.320 kgf** vs. **WB** = **3.901 kgf** ($F = 78\text{ mm}$), **5.606 kgf** ($F = 128\text{ mm}$) e **7.708 kgf** ($F = 185\text{ mm}$, superando até o CBPL de $F = 165\text{ mm}$ com 6.657 kgf).
      - Corte: **WB** = **9.691 kgf** **>** **CB** = **6.573 kgf** ($+47,4\%$).
- **Escolha CB quando:**
  - Precisar de recuperabilidade (parafuso removível), das bitolas **5/16"** ou **1"** tabeladas, de maior espessura útil a fixar em bitolas médias/grandes (ate 35 mm no CB vs. máx. 19,7 mm no WB) ou de maior tração em bitolas de 1/4" a 3/8" (pág. 08).
- **Escolha WB quando:**
  - A solicitação principal for **cisalhamento** de 3/8" a 3/4" ("Chumbador ideal para esforço de cisalhamento", pág. 10), quando precisar de **aço inoxidável**, de instalação passante através da peça com **broca de mesmo diâmetro nominal** do chumbador (ex.: placas de granito em fachadas, estruturas porta-pallets) ou de furo mais raso nas versões curtas (ex.: 1/2" com $F = 50\text{ mm}$ no WB 12234 vs. $76\text{ mm}$ no CB 12300) (pág. 10).
- **Mesmo desempenho em:**
  - Mesma família (mecânico expansivo por torque), mesmo concreto de ensaio (20 MPa, laudo IPT nº 28 202), mesmos coeficientes de segurança (3 a 15, pág. 07) e mesma regra de distanciamento ($D = 3P$ e $d = 1,5P$, pág. 07).
- **Fonte:** `walsywa_fix_mec.pdf`, págs. 07, 08, 10 e 14.

#### 5. Walsywa WB × Walsywa WLT
- **Em que diferem:**
  - **Acionamento e elemento de fixação:** o **WB** é expandido por **torque** na porca sobre **corpo roscado** com presilha (pág. 10); o **WLT** é expandido por **percussão** (martelo + "batedor especial que não danifica a rosca") sobre tolete cônico cativo e possui **rosca interna** (fêmea) que recebe parafuso ou barra roscada (pág. 11).
  - **Diâmetro do furo ($\varnothing B$):** no **WB**, o furo é igual ao diâmetro nominal (1/4" a 3/4", pág. 10); no **WLT**, o furo é maior que a rosca interna (de 3/8" / 10 mm para rosca 1/4" até 1" / 25 mm para rosca 3/4", pág. 11).
  - **Cisalhamento tabelado:** o **WB** é o "Chumbador ideal para esforço de cisalhamento" e tabela cortes de **744 a 9.691 kgf** (pág. 10); o **WLT** **não possui coluna de cisalhamento na tabela** (embora o texto o indique para "combinações de cargas de tração e cisalhamento com em suspensão de tubulações", pág. 11).
  - **Acabamento:** o **WB** oferece zincado brilhante ou **aço inoxidável** (pág. 10); o **WLT** apenas zincado brilhante (pág. 11).
  - **Comparação numérica de tração (comparável diretamente: ambos são ruptura média em concreto 20 MPa, laudo IPT nº 28 202):**
    - **1/4"**: **WLT** ($F = 30\text{–}40\text{ mm}$) = **570 kgf** vs. **WB** = **476 kgf** ($F = 30\text{ mm}$), **761 kgf** ($F = 39\text{ mm}$) e **825 kgf** ($F = 65\text{ mm}$);
    - **3/8"**: **WLT** ($F = 40\text{–}55\text{ mm}$) = **1.207 kgf** vs. **WB** = **1.250 kgf** ($F = 45\text{ mm}$), **1.236 kgf** ($F = 51\text{ mm}$) e **1.919 kgf** ($F = 70\text{ mm}$);
    - **1/2"**: **WLT** ($F = 50\text{–}60\text{ mm}$, rosca UNC ou WW) = **2.197 kgf** vs. **WB** = **1.730 kgf** ($F = 50\text{ mm}$), **2.260 kgf** ($F = 78\text{ mm}$) e **3.193 kgf** ($F = 110\text{ mm}$);
    - **5/8"**: **WLT** ($F = 65\text{–}80\text{ mm}$) = **3.037 kgf** vs. **WB** = **2.372 kgf** ($F = 59\text{ mm}$), **4.525 kgf** ($F = 97\text{ mm}$) e **4.026 kgf** ($F = 148\text{ mm}$);
    - **3/4"**: **WLT** ($F = 80\text{–}100\text{ mm}$) = **4.290 kgf** vs. **WB** = **3.901 kgf** ($F = 78\text{ mm}$), **5.606 kgf** ($F = 128\text{ mm}$) e **7.708 kgf** ($F = 185\text{ mm}$).
- **Escolha WB quando:**
  - Houver cálculo de **cisalhamento** no software (o WLT não tem corte tabelado), fixação de chapas de base/fachadas passantes, necessidade de **aço inox** ou maiores cargas de tração nos comprimentos médios/longos (pág. 10).
- **Escolha WLT quando:**
  - A aplicação exigir **rosca interna** embutida rente ao concreto (ex.: "suspensão de tubulações" no teto com barras roscadas de 1 m da pág. 15, ou quando o parafuso precisar ser retirado sem deixar pino saliente `[inferência]`) ou rosca 1/2" padrão **WW** (além de UNC) (pág. 11).
- **Mesmo desempenho em:**
  - Mesma faixa de diâmetros tabelados (**1/4", 3/8", 1/2", 5/8" e 3/4"**), mesmo concreto de ensaio (20 MPa, laudo IPT nº 28 202) e cargas de tração próximas quando comparados na menor profundidade do WB (ex.: em 3/8" com $F \approx 45\text{–}55\text{ mm}$, WLT tem 1.207 kgf e WB tem 1.236–1.250 kgf) (págs. 10–11).
- **Fonte:** `walsywa_fix_mec.pdf`, págs. 07, 10, 11 e 15.

#### 6. Walsywa WLT × Walsywa Golpe de Metal
- **Em que diferem:**
  - **Elemento de fixação e instalação:** embora ambos sejam de **percussão**, o **WLT** é uma bucha de aço com **rosca interna** e tolete cativo expandida no furo antes de posicionar a peça e o parafuso (usando batedor especial, pág. 11); o **Golpe de Metal** tem **corpo expansível em zamak e pino liso em aço zincado**, sendo instalado **através da peça a fixar** batendo-se com martelo na cabeça do pino até alojar-se na cabeça do chumbador (sem rosca e sem desmontagem, pág. 12).
  - **Faixa de diâmetros:** o **WLT** vai de **1/4" a 3/4"** (rosca interna, furo de 3/8" a 1", pág. 11); o **Golpe de Metal** existe apenas em **3/16" x 7/8"** e **1/4" x 1 1/4"** (diâmetro do furo = 3/16" e 1/4", pág. 12).
  - **Cisalhamento tabelado:** o **Golpe de Metal** informa cargas de ruptura ao cisalhamento (**200 kgf** em 3/16" e **710 kgf** em 1/4", pág. 12); o **WLT** não informa cisalhamento na tabela (pág. 11).
  - **Material-base:** o **Golpe de Metal** explicita "Aplicação em concreto ou alvenaria" nos bullets da linha (pág. 12); o **WLT** é apresentado para concreto normal (pág. 11; alvenaria maciça apenas nos princípios gerais para baixas cargas, pág. 05).
  - **Comparação numérica em 1/4" (ambos em concreto 20 MPa; WLT é "Carga média* tração" laudo IPT e Golpe de Metal é "carga de ruptura*"):**
    - **WLT 1/4"** (rosca 1/4", furo Ø 3/8" / 10 mm, prof. furo 30–40 mm): tração = **570 kgf**; corte = não informado (pág. 11).
    - **Golpe de Metal 1/4" x 1 1/4"** (furo Ø 1/4" / corpo 6,4 mm, prof. furo 36 mm): tração = **350 kgf** (**38,6% menor** que o WLT 1/4"); corte = **710 kgf** (pág. 12).
- **Escolha WLT quando:**
  - Precisar de rosca interna para parafusos ou barras roscadas (de 1/4" a 3/4") e maior capacidade de tração em suspensão de tubulações (pág. 11).
- **Escolha Golpe de Metal quando:**
  - For uma fixação leve e rápida, passante através da peça, apenas a martelo (sem batedor especial e sem chave de torque), em concreto ou alvenaria (ex.: equipamentos elétricos, fitas/abraçadeiras leves e tubulações leves em 3/16" ou 1/4", pág. 12).
- **Mesmo desempenho em:**
  - Mesma família e acionamento (**mecânico por percussão**), mesmo concreto de referência (20 MPa) e indicação compartilhada para **suspensão de tubulações** (págs. 11–12).
- **Fonte:** `walsywa_fix_mec.pdf`, págs. 11 e 12.

#### 7. Walsywa WQA × Walsywa WQI 44
- **Em que diferem:**
  - **Acionamento e fornecimento:** o **WQA** usa **ampola** (cápsula) rompida pela introdução do **prisioneiro WQA** com martelete e adaptador SDS Plus ("deve-se sempre utilizar o sistema completo Walsywa", pág. 05); o **WQI 44** é resina bicomponente de **injeção** (cartuchos de 300 ml e 345 ml com bico misturador), na qual a haste é inserida manualmente com movimento giratório (pág. 06).
  - **Elemento de fixação:** o **WQA** tabela exclusivamente **prisioneiro WQA rosca UNC de 5/16" a 1"** (pág. 05); o **WQI 44** tabela tanto **barra roscada/prisioneiro de 5/16" a 1"** quanto **vergalhão de 8 a 32 mm** (pág. 07).
  - **Material-base (bloco/tijolo oco vs. concreto úmido):** o **WQI 44** é "indicado para aplicações em concreto ou bloco oco" (usa camisa pequena Ø 15 x 85 mm ou grande Ø 15 x 130 mm em tijolo furado/oco, págs. 06–07); o **WQA** é apenas para concreto, mas traz tabela explícita de cura para **concreto seco e úmido** e fatores de redução para concreto 15 MPa, temperatura 100–120 °C e distâncias mínimas (pág. 05).
  - **Cisalhamento tabelado:** o **WQA** tabela cargas recomendadas de cisalhamento (**4,7 a 37 kN**, pág. 05); o **WQI 44** **não possui dados de cisalhamento** (pág. 07).
  - **Natureza das cargas e concreto do ensaio (ATENÇÃO: NÃO comparáveis diretamente sem ajuste):** o **WQA** apresenta apenas **cargas recomendadas em kN** para concreto **$f_{ck} \ge 20\text{ MPa}$** e **$f_{ck} \ge 40\text{ MPa}$** (pág. 05); o **WQI 44** apresenta **carga de ruptura (kg) e carga recomendada (kg)** para concreto **28 MPa** (e deixa em branco as cargas de 5/16" e 8 mm, pág. 07).
  - **Comparação numérica por diâmetro (mesmo furo e mesma profundidade em todas as bitolas de 5/16" a 1", mas concretos diferentes — 20/40 MPa no WQA vs. 28 MPa no WQI 44):**
    - **5/16"** (furo Ø 10 x 80 mm): **WQA** tração rec. = **5 kN** (~510 kgf `[inferência]` em 20 MPa) / **6,2 kN** (40 MPa) e corte rec. = **4,7 kN**; **WQI 44** = sem carga informada na tabela.
    - **3/8"** (furo Ø 12 x 90 mm): **WQA** tração rec. = **7 kN** (20 MPa) / **8,7 kN** (40 MPa) e corte rec. = **7 kN**; **WQI 44** (28 MPa) tração rec. = **1.138 kg** (ruptura = 2.471 kg).
    - **1/2"** (furo Ø 14 x 110 mm): **WQA** tração rec. = **10 kN** (20 MPa) / **12,5 kN** (40 MPa) e corte rec. = **10,5 kN**; **WQI 44** (28 MPa) tração rec. = **2.002 kg** (ruptura = 4.340 kg).
    - **5/8"** (furo Ø 18 x 125 mm): **WQA** tração rec. = **15 kN** (20 MPa) / **18,7 kN** (40 MPa) e corte rec. = **15 kN**; **WQI 44** (28 MPa) tração rec. = **3.185 kg** (ruptura = 6.895 kg).
    - **3/4"** (furo 25 x 170 mm no WQA vs. 24 x 170 mm no WQI 44): **WQA** tração rec. = **27 kN** (20 MPa) / **33,7 kN** (40 MPa) e corte rec. = **27 kN**; **WQI 44** (28 MPa) tração rec. = **4.683 kg** (ruptura = 10.126 kg).
    - **1"** (furo Ø 28 x 210 mm): **WQA** tração rec. = **43 kN** (20 MPa) / **52,5 kN** (40 MPa) e corte rec. = **37 kN**; **WQI 44** (28 MPa) tração rec. = **7.508 kg** (ruptura = 16.230 kg).
    - *(Nota: os valores recomendados do WQI 44 em 28 MPa são numericamente superiores aos do WQA em 20 MPa e 40 MPa em todas as bitolas de 3/8" a 1", mas os ensaios estão em classes de concreto distintas e o WQA não informa sua carga de ruptura nem o fator de segurança embutido `[inferência]`.)*
- **Escolha WQA quando:**
  - Precisar de **cisalhamento tabelado**, de dimensionamento na bitola **5/16"**, de regras explícitas para **borda próxima / distância mínima entre pinos / concreto úmido / concreto 15 MPa / temperatura 100–120 °C**, de dosagem individual exata por furo (ampola) ou de cura rápida em concreto seco acima de 10 °C (20–30 min vs. 60 min–2 h do WQI 44 entre 10–30 °C) (págs. 05 e 07).
- **Escolha WQI 44 quando:**
  - For ancorar **vergalhão (10 a 32 mm)**, fixar em **bloco oco / tijolo furado** (com camisa), precisar de **maiores cargas de tração recomendadas** (em concreto 28 MPa, de 3/8" a 1") ou preferir aplicação com pistola (inclusive cartucho de 300 ml em aplicador profissional de silicone) sem depender de adaptador SDS Plus (págs. 06–07).
- **Mesmo desempenho em:**
  - Mesmos princípios químicos (aderência + intertravamento, sem tensão no concreto, menores distâncias entre chumbadores) e **mesmas dimensões de furo (diâmetro e profundidade)** para barras/prisioneiros de 5/16", 3/8", 1/2", 5/8" e 1" (e 170 mm de profundidade em 3/4") (págs. 04, 05 e 07).
- **Fonte:** `WALSYWA_fixacao_quimica.pdf`, págs. 04, 05, 06 e 07.

#### 8. Walsywa WQI 44 × Walsywa WQE 500
- **Em que diferem:**
  - **Tempos de aplicação e de cura:** o **WQI 44** tem cura rápida (**30 min a 4 h**, com tempo de manipulação de apenas **3 min a 40 min**, pág. 07); o **WQE 500** tem "maior tempo de curo (facilita ajustes)" (**3 h a 30 h** de cura final, com tempo de aplicação de **15 min a 4 h**, págs. 08–09).
  - **Odor e embalagem:** o **WQE 500** destaca **"baixo odor"** e vem em cartucho de **400 ml** (pág. 08); o **WQI 44** vem em **345 ml e 300 ml** (este último compatível com ferramenta profissional de silicone, pág. 06).
  - **Material-base (tijolo/bloco oco):** o **WQI 44** é indicado para **concreto ou bloco oco / tijolo furado** com camisa (págs. 06–07); o **WQE 500** é indicado **apenas para concreto** (págs. 08–09).
  - **Preenchimento das tabelas em 5/16" e 8 mm:** no **WQI 44**, as linhas de barra 5/16" e vergalhão 8 mm têm cargas em branco (pág. 07); no **WQE 500**, ambas trazem carga de ruptura de **2.338 kg** e recomendada de **779 kg** (pág. 09).
  - **Comparação numérica das cargas de tração (COMPARÁVEL DIRETAMENTE: ambos no mesmo concreto 28 MPa, mesmos diâmetros e mesmas dimensões de furo):**
    - *Barra roscada / prisioneiro:*
      - **5/16"** (furo 10 x 80 mm): WQI 44 = sem dados vs. WQE 500 = **2.338 kg** (ruptura) / **779 kg** (rec.);
      - **3/8"** (furo 12 x 90 mm): WQI 44 = **2.471 kg** (rupt.) / **1.138 kg** (rec.) vs. WQE 500 = **3.133 kg** (rupt.) / **1.045 kg** (rec.);
      - **1/2"** (furo 14 x 110 mm): WQI 44 = **4.340 kg** (rupt.) / **2.002 kg** (rec.) vs. WQE 500 = **4.495 kg** (rupt.) / **1.498 kg** (rec.);
      - **5/8"** (furo 18 x 125 mm): WQI 44 = **6.895 kg** (rupt.) / **3.185 kg** (rec.) vs. WQE 500 = **6.595 kg** (rupt.) / **2.187 kg** (rec.);
      - **3/4"** (furo 24 x 170 mm): WQI 44 = **10.126 kg** (rupt.) / **4.683 kg** (rec.) vs. WQE 500 = **11.958 kg** (rupt.) / **3.986 kg** (rec.);
      - **1"** (furo 28 x 210 mm): WQI 44 = **16.230 kg** (rupt.) / **7.508 kg** (rec.) vs. WQE 500 = **17.352 kg** (rupt.) / **5.744 kg** (rec.).
    - *Vergalhão:*
      - **8 mm** (furo 12 x 80 mm): WQI 44 = sem dados vs. WQE 500 = **2.338 kg** (rupt.) / **779 kg** (rec.);
      - **10 mm** (furo 13 x 90 mm): WQI 44 = **3.530 / 1.625 kg** **>** WQE 500 = **3.133 / 1.045 kg**;
      - **12,5 mm** (furo 16 x 120 mm): WQI 44 = **6.200 / 2.860 kg** **>** WQE 500 = **4.495 / 1.498 kg**;
      - **16 mm** (furo 20 x 145 mm): WQI 44 = **9.850 / 4.550 kg** **>** WQE 500 = **6.595 / 2.187 kg**;
      - **20 mm** (furo 25 x 170 mm): WQI 44 = **14.465 / 6.690 kg** **>** WQE 500 = **11.958 / 3.986 kg**;
      - **25 mm** (furo 32 x 225 mm): WQI 44 = **23.185 / 10.725 kg** **>** WQE 500 = **17.352 / 5.744 kg**;
      - **32 mm** (furo 40 x 290 mm): WQI 44 = **32.467 kg** (rupt.) / **15.020 kg** (rec.) vs. WQE 500 = **32.940 kg** (rupt.) / **13.018 kg** (rec.).
    - *(Nota `[inferência]`: embora o WQE 500 tenha carga de ruptura ligeiramente maior em barras de 3/8", 1/2", 3/4", 1" e vergalhão 32 mm, a **carga recomendada** do **WQI 44 é maior em todas as bitolas** porque a tabela do WQI 44 adota razão ruptura/recomendada $\approx 2,16$, enquanto a do WQE 500 adota $\approx 3,0$ de 5/16" a 1" e 2,53 em 32 mm.)*
- **Escolha WQI 44 quando:**
  - Quiser **liberação rápida de carga** (30 min a 4 h), **maiores cargas de tração recomendadas** (de 3/8" a 1" e vergalhões 10 a 32 mm), aplicação em **bloco oco / tijolo furado** ou uso de pistola de silicone comum (cartucho 300 ml) (págs. 06–07).
- **Escolha WQE 500 quando:**
  - Precisar de **mais tempo aberto para posicionar e ajustar** a peça (15 min a 4 h, especialmente em dias quentes onde o WQI 44 dá apenas 3 min a 30–40 °C), de **baixo odor** (ambientes internos/fechados `[inferência]`) ou de carga tabelada para **barra 5/16"** ou **vergalhão 8 mm** (págs. 08–09).
- **Mesmo desempenho em:**
  - Mesma família e acionamento (químico de injeção bicomponente), mesmo concreto de ensaio (28 MPa), **exatamente os mesmos diâmetros e profundidades de furo** em todas as bitolas de barra e vergalhão, ausência de corte tabelado e exigência de aguardar 24 h após a cura final para ensaios de arrancamento (págs. 07 e 09).
- **Fonte:** `WALSYWA_fixacao_quimica.pdf`, págs. 06, 07, 08 e 09.

#### 9. Walsywa WQA × Walsywa WQE 500
- **Em que diferem:**
  - **Acionamento:** o **WQA** é **ampola** batida com martelete + adaptador SDS Plus (pág. 05); o **WQE 500** é **injeção** em cartucho de 400 ml com bico misturador (pág. 08).
  - **Tempo de cura e ajustes:** o **WQA** tem cura rápida (**20 min a 5 h** em concreto seco; 40 min a 10 h em úmido) e o prisioneiro é cravado de imediato no rompimento da ampola (pág. 05); o **WQE 500** tem **cura lenta (3 h a 30 h)** e longo tempo de manipulação (**15 min a 4 h**) que "facilita ajustes", além de "baixo odor" (págs. 08–09).
  - **Elemento de fixação:** o **WQA** usa apenas **prisioneiro WQA (5/16" a 1")** (pág. 05); o **WQE 500** aceita **barra roscada/prisioneiro (5/16" a 1")** e **vergalhão (8 a 32 mm)** (pág. 09).
  - **Cisalhamento e regras de redução:** o **WQA** tabela **cisalhamento recomendado (4,7 a 37 kN)** e fatores de redução para borda/espaçamento/temperatura/15 MPa (pág. 05); o **WQE 500** **não tabela cisalhamento** nem fatores numéricos de redução (págs. 08–09).
  - **Comparação numérica por diâmetro (ATENÇÃO: NÃO comparáveis diretamente sem ajuste — WQA traz carga recomendada em kN para $f_{ck} \ge 20\text{ MPa}$ e $\ge 40\text{ MPa}$; WQE 500 traz ruptura e recomendada em kg para concreto 28 MPa):**
    - **5/16"** (furo 10 x 80 mm): **WQA** rec. = **5 kN** (20 MPa) / **6,2 kN** (40 MPa), corte = **4,7 kN**; **WQE 500** (28 MPa) rec. = **779 kg** (ruptura = 2.338 kg).
    - **3/8"** (furo 12 x 90 mm): **WQA** rec. = **7 kN** (20 MPa) / **8,7 kN** (40 MPa), corte = **7 kN**; **WQE 500** (28 MPa) rec. = **1.045 kg** (ruptura = 3.133 kg).
    - **1/2"** (furo 14 x 110 mm): **WQA** rec. = **10 kN** (20 MPa) / **12,5 kN** (40 MPa), corte = **10,5 kN**; **WQE 500** (28 MPa) rec. = **1.498 kg** (ruptura = 4.495 kg).
    - **5/8"** (furo 18 x 125 mm): **WQA** rec. = **15 kN** (20 MPa) / **18,7 kN** (40 MPa), corte = **15 kN**; **WQE 500** (28 MPa) rec. = **2.187 kg** (ruptura = 6.595 kg).
    - **3/4"** (furo 25 x 170 mm no WQA vs. 24 x 170 mm no WQE 500): **WQA** rec. = **27 kN** (20 MPa) / **33,7 kN** (40 MPa), corte = **27 kN**; **WQE 500** (28 MPa) rec. = **3.986 kg** (ruptura = 11.958 kg).
    - **1"** (furo 28 x 210 mm): **WQA** rec. = **43 kN** (20 MPa) / **52,5 kN** (40 MPa), corte = **37 kN**; **WQE 500** (28 MPa) rec. = **5.744 kg** (ruptura = 17.352 kg).
- **Escolha WQA quando:**
  - Precisar de **cisalhamento tabelado**, cura rápida em minutos, instalação em concreto úmido com tempo tabelado ou verificação explícita de redução por distância à borda/entre chumbadores (pág. 05).
- **Escolha WQE 500 quando:**
  - For ancorar **vergalhão**, trabalhar em local que exija **baixo odor** ou precisar de **15 min a 4 horas de tempo aberto** para alinhar/ajustar os chumbadores antes do endurecimento (págs. 08–09).
- **Mesmo desempenho em:**
  - Mesma família (fixação química sem tensão de expansão no concreto, indicada apenas para concreto maciço — nenhum dos dois tabela bloco oco) e mesmas dimensões de furo de 5/16" a 1" (exceto diâmetro do furo em 3/4": 25 mm no WQA vs. 24 mm no WQE 500) (págs. 05, 08 e 09).
- **Fonte:** `WALSYWA_fixacao_quimica.pdf`, págs. 04, 05, 08 e 09.

#### 10. Mecânico por torque (CB / CBPL / CBE / CBEPL / WB) × Químico (WQA / WQI 44 / WQE 500)
- **Em que diferem:**
  - **Princípio de ancoragem e tensão no concreto:** os **mecânicos por torque** ancoram por **atrito/expansão** contra a parede do furo, gerando tensões internas no concreto (`walsywa_fix_mec.pdf`, págs. 06–10); os **químicos** ancoram por **aderência e intertravamento** da resina, e "a aplicação não cria tensões no concreto" (`WALSYWA_fixacao_quimica.pdf`, pág. 04).
  - **Distanciamentos mínimos:** os **mecânicos** exigem $D = 3P$ entre chumbadores e $d = 1,5P$ até a borda (`walsywa_fix_mec.pdf`, pág. 07); os **químicos** permitem "menores distâncias entre chumbadores" e à borda (`WALSYWA_fixacao_quimica.pdf`, págs. 04–08; no WQA, $D_{\text{mín}}$ e $d_{\text{mín}}$ caem para 4 a 13 cm aplicando reduções de 30% a 50%, pág. 05).
  - **Tempo de cura vs. carga imediata:** os **mecânicos** não têm tempo de cura (recebem carga imediatamente após o torque `[inferência]`); os **químicos** exigem aguardar o tempo de cura de **20 min a 30 horas** conforme linha, temperatura e umidade (`WALSYWA_fixacao_quimica.pdf`, págs. 05, 07, 09).
  - **Recuperabilidade:** **CB, CBPL, CBE e CBEPL** possuem "Recuperabilidade" (`walsywa_fix_mec.pdf`, págs. 08–09); nos **químicos** a haste fica colada permanentemente `[inferência]`.
  - **Carga dinâmica e de choque:** os **químicos** têm "excelente perfil de resistência à cargas dinâmicas e de choque" (pág. 04) e o WQA é "indicado para cargas dinâmicas" (pág. 05); os **mecânicos** exigem dividir a carga de ruptura por coeficientes elevados (**7 a 15** para dinâmica e **5 a 10** para choque, `walsywa_fix_mec.pdf`, pág. 07).
  - **Elementos e materiais-base especiais:** apenas os **químicos de injeção (WQI 44 / WQE 500)** ancoram **vergalhões (8 a 32 mm)**, e apenas o **WQI 44** fixa em **bloco/tijolo oco com camisa** (`WALSYWA_fixacao_quimica.pdf`, págs. 06–09); por outro lado, todos os **mecânicos por torque** têm **cisalhamento tabelado**, enquanto nos químicos **apenas o WQA** tabela cisalhamento.
  - **Comparação numérica em 1/2" (ATENÇÃO: NÃO comparáveis diretamente sem ajuste — mistura ruptura média vs. carga recomendada e concretos de 20 MPa vs. 28 MPa):**
    - *Mecânicos (ruptura média em concreto 20 MPa — precisam ser divididos por CS = 3 a 5 para carga estática, pág. 07):*
      - **CB 12300 / CBE 12095** (1/2", furo 19 mm, prof. 76 / 60 mm): tração ruptura = **2.523 kgf** (admissível com CS = 3: **~841 kgf** `[inferência]`); corte ruptura = **2.897 kgf** (com CS = 3: **~966 kgf** `[inferência]`).
      - **CBPL 12412 / CBEPL 12135** (1/2", furo 19 mm, prof. 114 / 99 mm): tração ruptura = **4.824 kgf** (com CS = 3: **~1.608 kgf** `[inferência]`); corte ruptura = **2.897 kgf**.
      - **WB 1/2"** (furo 1/2" / 13 mm, prof. 50 / 78 / 110 mm): tração ruptura = **1.730 / 2.260 / 3.193 kgf** (com CS = 3: **~577 a 1.064 kgf** `[inferência]`); corte ruptura = **3.346 kgf** (com CS = 3: **~1.115 kgf** `[inferência]`).
    - *Químicos (em 1/2", furo 14 x 110 mm):*
      - **WQA 1/2"** (concreto $\ge 20\text{ MPa}$): tração **recomendada** = **10 kN** (~1.020 kgf `[inferência]`, ou 12,5 kN em $\ge 40\text{ MPa}$); corte **recomendado** = **10,5 kN** (~1.071 kgf `[inferência]`).
      - **WQI 44 1/2"** (concreto **28 MPa**): tração ruptura = **4.340 kg** e **recomendada** = **2.002 kg**; sem corte tabelado.
      - **WQE 500 1/2"** (concreto **28 MPa**): tração ruptura = **4.495 kg** e **recomendada** = **1.498 kg**; sem corte tabelado.
- **Escolha Mecânico por torque quando:**
  - Precisar de **aplicação imediata de carga** (sem tempo de cura), **recuperabilidade** (CB/CBPL/CBE/CBEPL), **cálculo de cisalhamento** sem depender exclusivamente do WQA, instalação passante simples ou **aço inox** tabelado (WB) (`walsywa_fix_mec.pdf`, págs. 08–10).
- **Escolha Químico quando:**
  - As distâncias à borda ou entre chumbadores forem **reduzidas** (menores que $1,5P$ ou $3P$), o carregamento for **dinâmico/choque**, a haste for **vergalhão**, a base for **bloco/tijolo oco** (WQI 44), a fixação for à prova de umidade/intempéries ou se exigirem altas cargas de tração sem induzir tensão de expansão no concreto (`WALSYWA_fixacao_quimica.pdf`, págs. 04–09).
- **Mesmo desempenho em:**
  - Cobertura das bitolas estruturais de **5/16" a 1"** (com 1/4" adicional nos mecânicos) para ancoragem em concreto normal (`walsywa_fix_mec.pdf`, págs. 08–10; `WALSYWA_fixacao_quimica.pdf`, págs. 05–09).
- **Fonte:** `walsywa_fix_mec.pdf`, págs. 04–10; `WALSYWA_fixacao_quimica.pdf`, págs. 04–09.

#### 11. Químico (WQA / WQI 44 / WQE 500) × Walsywa PU 500
- **Em que diferem:**
  - **Função (estrutural vs. não estrutural `[inferência]`):** **WQA, WQI 44 e WQE 500** são **chumbadores químicos de ancoragem** (resina + catalisador em furos perfurados para fixar barras roscadas, prisioneiros ou vergalhões com cargas tabeladas em kN ou kg, págs. 04–09); a **PU 500** é uma **espuma expansiva de poliuretano** em aerossol (340 g / 480 g) para **assentamento, vedação, isolamento acústico/térmico e preenchimento de vãos**, sem haste metálica e **sem nenhuma carga mecânica tabelada** (págs. 10–11).
  - **Modo de aplicação:** os chumbadores químicos exigem furo calibrado, limpeza com soprador e introdução de ampola/resina + haste metálica (págs. 05–08); a **PU 500** é aplicada com gatilho sobre superfície umedecida com água limpa, preenchendo até 1/3 do vão (expande 3 a 4 vezes, pág. 11).
  - **Exposição ao sol e acabamento:** os chumbadores químicos têm "aplicação à prova de umidade e/ou intempéries" embutida no furo (pág. 04); a **PU 500** "não deve ficar exposto diretamente ao sol. Para tanto é necessário que tenha um revestimento" (aceita serrar, cortar, lixar, rebocar e pintar após curada, pág. 10).
- **Escolha Químico (WQA / WQI 44 / WQE 500) quando:**
  - Houver qualquer **ancoragem mecânica ou estrutural** de placas de base, máquinas, estruturas metálicas, barras roscadas ou vergalhões com exigência de resistência à tração e/ou cisalhamento `[inferência]` (págs. 04–09).
- **Escolha PU 500 quando:**
  - A finalidade for **assentar e vedar portas e janelas**, aparelhos de ar-condicionado, fixar/isolar dutos de ar, tubos e canos sem função estrutural, preencher juntas de dilatação, espaços vazios ou pequenos reparos (págs. 10–11).
- **Mesmo desempenho em:**
  - Nenhum desempenho mecânico comparável (compartilham apenas o mesmo catálogo de Fixação Química e a existência de tempo de cura antes de solicitar o material, págs. 03–11).
- **Fonte:** `WALSYWA_fixacao_quimica.pdf`, págs. 04 a 11.

---

### 5.3 Regras para comparar no software `[inferência]`

1. **Grandezas comparáveis diretamente (sem ajuste):**
   - **Entre mecânicos (`CB`, `CBPL`, `CBE`, `CBEPL`, `WB`, `WLT`, `Golpe de Metal`):** todas as cargas de tração e cisalhamento estão na mesma unidade (**kgf**), no mesmo concreto (**$f_{ck} = 20\text{ MPa}$**) e representam **ruptura** (média de ensaio IPT nas págs. 08–11 ou ruptura na pág. 12), podendo ser comparadas diretamente entre si ou após divisão pelo mesmo coeficiente de segurança da pág. 07 (`[inferência]`).
   - **Entre químicos de injeção (`WQI 44` × `WQE 500`):** ambos estão em **kg**, no mesmo concreto (**28 MPa**) e com as mesmas geometrias de furo, permitindo comparar diretamente tanto a coluna **carga de ruptura** quanto a coluna **carga recomendada** (págs. 07 e 09) `[inferência]`.
   - **Dimensões geométricas:** diâmetro da rosca/haste, diâmetro do furo ($\varnothing B$), profundidade mínima do furo ($F$), espessura máxima a fixar ($G$) e distanciamentos mínimos ($D$ e $d$) são diretamente comparáveis em milímetros `[inferência]`.

2. **Grandezas que NÃO são comparáveis direto e precisam de ajuste/conversão `[inferência]`:**
   - **Ruptura × Recomendada:** `CB`, `CBPL`, `CBE`, `CBEPL`, `WB`, `WLT` e `Golpe de Metal` informam **apenas ruptura** (`walsywa_fix_mec.pdf`, págs. 08–12), enquanto `WQA` informa **apenas carga recomendada** (`WALSYWA_fixacao_quimica.pdf`, pág. 05) e `WQI 44` / `WQE 500` informam **ambas** (págs. 07 e 09). O software **nunca** deve comparar a carga de ruptura de um mecânico diretamente com a carga recomendada de um químico; deve primeiro dividir a ruptura do mecânico pelo coeficiente de segurança aplicável (3 a 15 conforme tipo de carga e risco, pág. 07) para obter a carga admissível/recomendada `[inferência]`.
   - **Unidades diferentes (`kgf` / `kg` × `kN`):** a tabela do `WQA` está em **kN** (pág. 05) e as demais em **kgf** ou **kg**. O software deve converter para a mesma unidade (usar a mesma conversão do motor: $1\text{ kgf} = 9{,}80665\text{ N}$, logo $1\text{ kN} = 101{,}97\text{ kgf}$) antes de exibir lado a lado `[inferência]`.
   - **Classes de concreto diferentes (`20 MPa` × `28 MPa` × `40 MPa` × `15 MPa`):**
     - Mecânicos e `WQA` (linha base) estão em **20 MPa**; `WQI 44` e `WQE 500` estão em **28 MPa**; `WQA` tem segunda linha para **$\ge 40\text{ MPa}$** e nota de redução de 20% na tração para **15 MPa**; pedras naturais equivalem a **14 MPa** (sedimentares) ou **30 MPa** (granito/mármore) (`walsywa_fix_mec.pdf`, pág. 05).
     - Os catálogos **não fornecem fórmula de conversão entre 20, 28 e 30 MPa**. O cálculo do projeto já adota $\psi_c = \min(1;\sqrt{f_{ck}/f_{ck,\text{ensaio}}})$ (hipótese declarada em [CHUMBADORES.md](CHUMBADORES.md)). Na comparação, o software deve mostrar **os dois valores**: o do catálogo (com a classe do concreto do ensaio ao lado) e o ajustado por $\psi_c$ para o fck da laje, que é o usado na verificação.
   - **Falta de cisalhamento tabelado (`WLT`, `WQI 44`, `WQE 500`, `PU 500`):** quando a comparação envolver esforço cortante, o software não deve assumir corte zero nem inventar valores para `WLT`, `WQI 44` ou `WQE 500`, devendo marcá-los como *"Cisalhamento não informado pelo fabricante no catálogo"* `[inferência]`.

3. **Avisos obrigatórios que o software deve exibir ao usuário na tela de comparação `[inferência]`:**
   - **Aviso de base de comparação (Ruptura vs. Recomendada):** informar claramente se os valores exibidos são **cargas de projeto/recomendadas** (já reduzidas pelo CS nas linhas mecânicas e usando a coluna "recomendada" nas químicas) ou **cargas de ruptura** (indisponível para `WQA`).
   - **Aviso de diferença de concreto ($f_{ck}$):** ao comparar qualquer chumbador de **20 MPa** (`CB`, `CBPL`, `CBE`, `CBEPL`, `WB`, `WLT`, `Golpe de Metal`, `WQA`) com um de **28 MPa** (`WQI 44`, `WQE 500`), alertar: *"Atenção: os ensaios de catálogo do WQI 44 / WQE 500 foram realizados em concreto 28 MPa, enquanto os demais estão em concreto 20 MPa."*
   - **Aviso de ausência de corte ou de bitola em branco:** alertar quando uma das opções comparadas não possuir cisalhamento tabelado (`WLT`, `WQI 44`, `WQE 500`) ou quando a bitola escolhida estiver sem carga na tabela (`WQI 44` em 5/16" e vergalhão 8 mm; `WB` em 5/16" e 1").
   - **Aviso de distanciamento e reduções de borda:** exibir $D_{\text{mín}} = 3P$ e $d_{\text{mín}} = 1,5P$ para os mecânicos e alertar que, no `WQA`, o uso de distâncias mínimas reduzidas exige **reduzir todas as cargas em 30%** (entre chumbadores) e **reduzir tração em 40% e corte em 50%** (borda), além de **-30%** entre 100 °C e 120 °C e **-20% na tração** em concreto 15 MPa (`WALSYWA_fixacao_quimica.pdf`, pág. 05).
   - **Aviso de item fora de cálculo estrutural:** se o usuário selecionar `WLT`, `Golpe de Metal` ou `PU 500` na comparação, avisar que são linhas apenas informativas no Estructto (sem corte tabelado, para cargas leves ou espuma não estrutural sem carga mecânica).

