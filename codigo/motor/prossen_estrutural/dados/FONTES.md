# Fontes dos catálogos (`dados/`)

## perfis_gerdau.csv — perfis laminados W e HP

- **Fonte:** Gerdau, folder *Perfis Estruturais Gerdau – Segmento Industrial*, edição 10/18, página "Tabela de Bitolas".
  URL: https://gsn.gerdau.com/sites/gsn_gerdau/files/downloadable_files/Folder%20Perfis%20Estruturais%20Gerdau%20-%20Segmento%20Industrial.pdf
- **Acesso:** 2026-10-20 (engenheiro-aco, ficha 002).
- **Colunas:** `nome,d,bf,tw,tf,R,massa,A,Ix,Iy` — mm, kg/m, cm², cm⁴, transcritas da tabela (texto extraído do PDF com `pdftotext -layout` e conferido com a imagem da página).
  A tabela não traz R; ela traz h (altura interna) e d' (altura plana da alma), então **R = (h − d')/2**.
- **Cobertura:** todas as 88 bitolas da tabela: W 150 a W 610 e HP 200 x 53 a HP 310 x 125, incluindo as séries H.
- **Conferência** (`testes/test_secoes.py::test_catalogo_area_confere`): para cada perfil, `secoes.secao_I` (com concordâncias) é comparado com a tabela.
  A = 2tf·bf + (d − 2tf)tw + (4 − π)R² fica a no máximo 0,2 % da área tabelada; Ix e Iy ficam a no máximo 0,5 %.
- **Correções em relação ao CSV anterior** (53 linhas, sem fonte registrada):
  - As dimensões das 53 linhas coincidem com a tabela.
  - Dois nomes foram corrigidos para a designação Gerdau: `W360x44` passou a `W360x44.6`, e `W360x57.8` passou a `W360x58`.
  - Foram acrescentadas 35 bitolas, entre elas W 200 x 59/71/86, W 250 x 80–115, W 310 x 97–117, HP 250 x 85, HP 310 x 110/125, W 360 x 64–122, W 410 x 75/85, W 460 x 74–106, W 530 x 82–109 e W 610 x 113–174.
- **Não constam da fonte:** Zx, Zy, J (It) e Cw. Eles são calculados por `secoes.secao_I` a partir das dimensões. J e Cw usam as fórmulas de parede fina sem as concordâncias.
- **Outras fontes da Gerdau sem tabela:** o folder *Perfis Estruturais Gerdau – Informações Técnicas* (10/18) e o catálogo de 2016 reproduzido em blog.famsteel.com.br. Nenhum dos dois tem tabela de bitolas legível.

## perfis_pff.csv — perfis formados a frio U simples e U enrijecido (NBR 6355)

- **Fontes** (dimensões nominais; acesso em 2026-10-20, engenheiro-aco, ficha 003):
  1. Marcegaglia do Brasil, catálogo *Perfis estruturais / Cold formed sections*, set/2020, tabelas "Perfil U simples" e "Perfil U enrijecido" (referência ABNT NBR 6355:2012): U 50x25 a 150x50 e Ue 75x40x15 a 127x50x17, t = 2,00 a 3,75.
     URL: https://marcegagliadobrasil.com/wp-content/uploads/2020/09/MMdoBrasil_Settembre2020_cold_formed_sections.pdf
  2. Gerdau, *Perfis Estruturais Gerdau — Tabela de comparações entre perfis*, tabelas "Perfis Ue formados a frio (NBR 6355)" e "Perfis U formados a frio (NBR 6355)": U 200x75, 250x100, 300x100 e Ue 150x60x20 a 300x85x30, t = 2,65 a 6,30.
     Cópia pública: https://www.skylightestruturas.com.br/downloads/perfil-estrutural-tabela-de-comparacoes-entre-perfis.pdf
  3. Isoeste Metálica, *Catálogo — Perfis Estruturais*, tabela "Dados técnicos perfis U enrijecido" (NBR 6355 e NBR 14762:2010): Ue 150x60x20 e Ue 300x85x25 com t = 2,00 a 2,65.
     URL: https://api.aecweb.com.br/cls/catalogos/115/25404/perfis_estruturais.pdf
- **Colunas:** `nome,tipo,h,b,D,t` em mm, `tipo` ∈ {Ue, U}; `nome` na designação da NBR 6355 (U bw x bf x t; Ue bw x bf x D x t).
  Exceção: a série **Ue 127x50x17** é designação do fabricante (Marcegaglia); a NBR 6355 anexo A.3 lista a família 125x50x17 (VC-pff-006).
- **Propriedades:** calculadas por `secoes.secao_Ue` / `secao_U_pf` pela linha média, com as dobras em arco de raio ri + t/2 e **ri = t** (NBR 6355:2012 anexo A; 1,5t para t > 6,3 mm — `secoes.ri_nbr6355`). As áreas conferem com as tabeladas na NBR 6355 a menos de 0,1 % (`testes/regressao/test_reg_pff.py::test_VC_pff_001_areas_NBR6355`). A massa de `lista_catalogo` é A·7850 kg/m³.
- **Conferência:** `testes/test_secoes.py::test_catalogo_pff` e os testes de regressão citados.

## perfis/*.csv — catálogo ampliado (frente K, K1; acesso 2026-10-21)

- **Colunas:** `fabricante_norma;familia;serie;nome;forma;massa;h;b;tw;tf;r;A;Ix;Iy;fonte` (mm, kg/m, cm², cm⁴; tubos: tw = tf = t; barra chata: h = largura, b = tw = tf = espessura).
- **W/HP:** cópia de `perfis_gerdau.csv` (folder Gerdau 10/18, ver acima).
- **IPE, HEA, HEB, UPN:** ArcelorMittal Orange Book, https://orangebook.arcelormittal.com/node/6 (IPE), /node/16 (HE), /node/265 (UPN). HTML baixado e lido por script; nenhuma PDF salva. IPE: só a série padrão (80 a 600); HEA/HEB: 100 a 1000. A fonte dá r = r1. O link /node/265 da tarefa só traz UPN; IPE/HE estão nos nós acima.
- **UPN 30 (30x33) e UPN 40 (40x35):** não constam do Orange Book. Valores da DIN 1026-1 digitados de memória; **PENDENTE de conferência** (stad.fr só começa em UPN 80; buscas deram valores divergentes). Mantidos para chegar a 20 UPN; A·7,85 confere.
- **L desiguais:** Orange Book node 225 (EN 10056), h x b x t, 32 bitolas. Ix = eixo y-y (forte).
- **L iguais, U laminado:** bitolas, massa, A e I da tabela pública de terceiros fabricadoprojeto.com.br (Cantoneira de Abas Iguais e Viga U padrão americano, https://www.fabricadoprojeto.com.br/tabelas-de-laminados-trefilados-e-itens-comerciais/), via `codigo/portico/gerar-perfis.ps1`, conferidas pela geometria. Não foi achada tabela primária (Gerdau/famsteel/AECweb só trazem texto ou imagem): marcadas na coluna `fonte` como terceiros, sem fonte primária.
- **Tubos quadrado/retangular/redondo (metalon, `tubo_*`):** só as bitolas vêm de fabricadoprojeto.com.br (tubo industrial); massa, A e I são **calculados** pela geometria (raio externo 1,5 t). Marcados `calculado, sem fonte primaria`. Catálogos Açotubo/Perfilor/Aço Cearense não localizados em texto.
- **tubos_vallourec (616 linhas):** brochura Vallourec *Tubos Estruturais, Seção Circular, Quadrada e Retangular*, https://www.vallourec.com/app/uploads/sites/2/2023/10/Structural-Tubes_brochure.pdf (pp. 35-54; TC 142, TQ 170, TR 304). A coluna de dimensões é célula mesclada; `ferramentas/extrair_vallourec.py` atribui cada linha ao rótulo cuja área/inércia teórica combina (tabelado <= teórico). Linhas repetidas ou que não combinam foram descartadas (a brochura tem ~694 linhas; 78 descartadas), então a cobertura é parcial. Conferir amostras antes de uso de projeto.
- **T laminado:** lâmina Gerdau "Perfil T", https://blog.famsteel.com.br/wp-content/uploads/2017/01/barras-e-perfis-lamina-perfil-t.pdf (o catálogo-barras-e-perfis.pdf só tem texto descritivo, sem tabela). Correção: massa do T 7/8 x 1/8" lida 0,99 kg/m na fonte, inconsistente com A = 1,34 cm²; gravado 1,05 (= A·7,85). Omitido "3/4" serr." (sem propriedades).
- **Barra chata:** lista comercial Gerdau não encontrada (o catálogo é só texto; a lâmina famsteel é de barra longarina 130 mm). Bitolas em polegadas usuais (escolha minha) com seção retangular nominal: `calculado, sem fonte primaria`.
- **U e Ue formados a frio:** dimensões em `perfis_pff.csv` (fontes acima); A e I calculados por `secoes.secao_U_pf`/`secao_Ue` com ri da NBR 6355; massa = A·7850.

- **Regeneração:** `codigo/motor/ferramentas/gerar_catalogo_perfis.py [brochura_vallourec.pdf]` (baixa o Orange Book; usa `extrair_vallourec.py`; requer pdfplumber).
