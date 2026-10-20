# Especificação do porte: placa de base, chumbadores, solda e comparação de fixações

Data da leitura: 20/10/2026. Destino: `codigo/motor/prossen_estrutural`, com funções puras e resultados em dicionários, seguindo a organização de `aco/ligacoes.py`. Este documento especifica o trabalho; não implementa o motor.

## 1. Fontes e limites do porte

As referências `N:linha` abaixo significam [Nucleo.cs](../codigo/portico/src/Nucleo.cs); `T:linha`, [Testes.cs](../codigo/portico/src/Testes.cs). Linhas contadas a partir de 1 na versão lida.

**A implementação de solda não está em Nucleo.cs.** `PlacaBase` apenas declara `Solda` e `Eletrodo` em N:1019–1020; os testes chamam `NovoFtool.Solda`, implementada em [codigo/Estructto/Solda.cs](../codigo/Estructto/Solda.cs). Para não inventar uma origem em Nucleo.cs, suas fórmulas são referenciadas como `S:linha` nesse arquivo.

Outras fontes lidas: [chumbadores.csv](../codigo/portico/chumbadores.csv), [fixacoes.csv](../codigo/portico/fixacoes.csv), [fixacoes-pares.csv](../codigo/portico/fixacoes-pares.csv), [CHUMBADORES.md](CHUMBADORES.md), [FIXACOES-GUIA.md](FIXACOES-GUIA.md) e [PADRAO-MEMORIAL.md](PADRAO-MEMORIAL.md).

O objetivo inicial é **compatibilidade numérica com o C#**, identificada explicitamente como método legado simplificado. As referências normativas abaixo são as citadas no código e nos documentos locais; este levantamento não constitui conferência independente do texto das normas. Não substituir silenciosamente essas equações pelas funções de resistência de cálculo de `ligacoes.py`.

## 2. Contrato Python proposto

Separar componentes numéricos, catálogo e apresentação, por exemplo em `aco/placa_base.py`, `aco/chumbadores.py` e `aco/fixacoes.py`. Nomes e divisão são proposta; não há criação desses módulos nesta tarefa.

| Função proposta | Responsabilidade e retorno |
|---|---|
| `geometria_placa(dados, secao)` | H, B, dimensões do perfil, coordenadas das fixações e espaçamento mínimo |
| `esforcos_placa(extremidade, direcao_barra)` | reação global, N/V/M da placa, normal, ângulo e indicador de inclinação |
| `forcas_fixacoes(N, V, M, coordenadas, H, ref_in, pivo_na_mesa=False)` | lista y/z/T, Tmax, Fv, modelo e pivô |
| `verificar_parafuso_placa(...)` | áreas, tensões, seguranças, pré-carga, torque e verificações |
| `espessura_placa(...)` | espessuras necessárias à tração/compressão e intermediários |
| `fator_cones(chumbador, fixacoes)` | ψ conforme algoritmo legado |
| `avaliar_chumbador(chumbador, dados, solicitacoes)` | concreto, aço da barra, instalação, aproveitamento e situação |
| `verificar_solda_contorno(secao, dados, esforcos)` | resultado equivalente a ResSolda |
| `perna_minima_contorno(...)`, `diagnosticar_solda(...)` | escolha de perna e diagnóstico equivalente ao C# |
| `verificar_placa_base(dados, esforcos, secao)` | composição das verificações e resultado geral |
| `dimensionar_chumbador(...)` | escolha de item e arranjo; resultado novo, sem modificar entradas |
| `comparar_fixacoes(chaves, base, dados)` | colunas numéricas/informativas, pares pertinentes e avisos |

Seguir `ligacoes.py`: constantes de cobertura e limitações explícitas; esquema de entradas com unidade, descrição, padrão e opções; validação agregada por `ValueError`; componentes puros; saída geral `{taxa_max, criterio, item, ok, verificacoes, avisos}`. Cada verificação deve fornecer `{nome, solicitante, resistente, unidade, taxa, item}`. Acrescentar `intermediarios`, `metodo` e `fontes` para permitir o memorial sem refazer contas.

**Unidades da API proposta:** forças kN, momentos kN·m, comprimentos m, áreas m², inércias m⁴, resistências kN/m², como em `ligacoes.py`; exceções explícitas terminam em `_mm` (diâmetro nominal, conforme o estilo existente). O catálogo conserva seus campos originais com sufixos de unidade. O núcleo de compatibilidade pode converter uma única vez para mm, mm², MPa e N, executar as fórmulas abaixo e converter a saída. Não passar MPa diretamente a funções que recebem kN/m²: `1 MPa = 1000 kN/m²`; `1 mm = 0,001 m`; `1 mm² = 10⁻⁶ m²`; `1 mm⁴ = 10⁻¹² m⁴`.

Esforços **assinados** de placa/solda precisam de validação própria: a validação atual de `ligacoes.py` só aceita esforços não negativos. N é positivo em tração, M tem sinal; V da placa é módulo. Nunca tomar o módulo de N antes de distribuir as forças nas fixações.

Guardar `inf` como estado de solicitação nula no núcleo; na serialização JSON, representá-lo por valor nulo e indicação de ausência de solicitação, sem produzir JSON não finito. Pivô inexistente deve ser nulo na API, em lugar do NaN do C#.

## 3. Entradas e dados de origem

### 3.1 Placa, perfil e análise

Entradas originais de `PlacaBase` (N:998–1023):

| Entrada C# | Unidade / significado / padrão efetivo |
|---|---|
| `H`, `B`, `Esp` | mm; H no plano do pórtico, B fora dele; Esp é espessura fornecida |
| `PeloPerfil`, `Sobra` | booleano e mm; gerar H/B a partir do perfil mais duas sobras |
| `Fileiras`, `PorFileira` | contagens; cada uma elevada a pelo menos 1 |
| `Borda` | mm, centro do furo até a **borda da chapa**, não do concreto |
| `Diametro`, `Classe`, `RoscaNoCorte` | mm, classe métrica e booleano; usados sem chumbador de catálogo |
| `PivoNaMesa` | booleano; giro na face comprimida do perfil em lugar da borda da chapa |
| `FyChapa` | MPa; valor ≤ 0 adota 250 (ASTM A36) |
| `Fck` | MPa; valor ≤ 0 adota 20 |
| `Seguranca` | adimensional; se > 0 prevalece sobre `Modelo.Seguranca` |
| `Chumbador` | código ou nome exato do catálogo; vazio seleciona parafuso métrico |
| `TipoCarga` | estática, variável, dinâmica ou choque; vazio/inválido aciona automático |
| `Risco` | baixo/alto; vazio/inválido aciona automático |
| `CoefChumbador` | adimensional; > 0 substitui tabela, com aviso se menor |
| `ClasseBarra` | classe da haste química; ausente/inválida adota 4.6, independente de `Classe` |
| `Solda`, `Eletrodo` | perna em mm (0 significa dimensionar na integração), identificação (vazio: E70) |

Perfil: seção reconhecida, `H`, `Bf`, `Tw`, `Tf` em m, forma e orientação `EixoFraco`; para placa, uma única barra ligada ao nó. Análise: coordenadas globais em m; extremidade N1/N2; comprimento L; cosseno C e seno S da barra; vetor local de forças de extremidade F em kN/kN·m; esforços locais N(x), Q(x), M(x); indicador de carga rotativa e cargas no nó. O cálculo Python dos componentes deve poder receber esforços já resolvidos, sem depender de GUI ou do solver C#.

### 3.2 Catálogo numérico e tabelas métricas

N:900–924 lê cabeçalhos por nome, separador `;`, trim e números com ponto decimal (cultura invariável). Linha vazia é ignorada; coluna ausente, vazia ou número não interpretável vira 0 no legado. No Python, distinguir falta de dado de capacidade nula na apresentação; não transformar corte não informado em resistência zero.

| Campos CSV | Unidade |
|---|---|
| `codigo`, `tipo`, `fabricante`, `linha`, `pol`, `fonte` | texto; `tipo == quimico` define químico |
| `rosca_mm`, `furo_mm`, `hef_mm`, `s_mm`, `c_mm` | mm |
| `tu_kgf`, `vu_kgf`, `tu_rec_kgf`, `vu_rec_kgf` | kgf, ruptura e recomendada separadas |
| `fck_mpa` | MPa do ensaio |
| `torque_kgfm` | kgf·m; vazio não significa torque dispensado |
| `at_mm2`, `ar_mm2` | áreas UNC da haste química, mm² |

São 76 itens: CB/CBPL/CBE/CBEPL (7 por linha), WB (15), WQA (6), WQI 44 (5), WQE 500 (6), Parabolt PB (16). Mecânicos Walsywa: C20; químicos de injeção: C28; WQA: recomendada C20; Indufix: C30. Não criar variantes ausentes por interpolação, nem extrapolar para vergalhões ou bases ocas a partir deste CSV de barras roscadas.

Nome calculado: `linha + " " + pol + " hef " + hef sem casas + " mm"` (N:886). Busca por nome ou código exato, vazio retorna nulo (N:951); preservar ordem do CSV nos filtros e desempates (N:952–953).

Tabelas de parafusos, N:870–875, alinhadas por posição:

| d (mm) | 8 | 10 | 12 | 14 | 16 | 20 | 24 | 30 | 36 |
|---|---|---|---|---|---|---|---|---|---|
| At (mm²) | 36,6 | 58 | 84,3 | 115 | 157 | 245 | 353 | 561 | 817 |
| Ar (mm²) | 32,8 | 52,3 | 76,3 | 104 | 144 | 225 | 324 | 519 | 759 |

| Classe | 4.6 | 4.8 | 5.8 | 8.8 | 9.8 | 10.9 |
|---|---|---|---|---|---|---|
| Sp (MPa) | 225 | 310 | 380 | 600 | 650 | 830 |
| Faixa informada | M5–M36 | M1,6–M16 | M5–M24 | M16–M36 | M1,6–M16 | M5–M36 |

As faixas são metadados: `Calcular` valida presença do diâmetro/classe nas listas, mas não cruza essas faixas.

## 4. Geometria, eixos e forças nas fixações

Todas as fórmulas desta seção reproduzem Nucleo.cs; comprimentos em mm, salvo indicação de m.

| Etapa / origem | Fórmula exata e critério |
|---|---|
| Perfil, N:1069–1080 | Exigir exatamente uma barra incidente e seção reconhecida. `h=1000·sec.H`; `bf=1000·(Bf se Bf>0, senão H)`. Normal: `RefIn=h`, `RefOut=bf`; eixo fraco troca os dois. |
| Normal, N:1095–1102 | `(ex,ey)=outro_nó−nó`, `lb=√(ex²+ey²)`. Se `abs(ey)>=abs(ex)`, normal `(0,sign(ey))`, senão `(sign(ex),0)`, com zero tratado como positivo. Eixo no plano `y=(-NormY,NormX)`. `AngNormal=acos(min(1,abs(ex·NormX+ey·NormY)/lb))·180/π`, ou 0 se lb=0. `Inclinada=AngNormal>2°`. `BarraY=(ex·yx+ey·yy)/lb`, `BarraN=(ex·NormX+ey·NormY)/lb`. |
| Espelhamento, N:1099 | `Espelhar = yx < −1e−9 ou (abs(yx)<=1e−9 e yy<0)`. Apenas representação. |
| Dimensões, N:1103–1107 | `nPl=p.Seguranca se >0, senão m.Seguranca`; pelo perfil: `H=ceil((RefIn+2·Sobra)/10)·10` e analogamente B com RefOut; senão H/B fornecidos. Erro se H≤0, B≤0, 2·Borda≥H ou 2·Borda≥B. |
| Malha, N:1125–1138 | `nf=max(1,Fileiras)`, `np=max(1,PorFileira)`. Percorrer i depois j; excluir pontos com `0<i<nf−1` **e** `0<j<np−1`. `y=0` se nf=1, senão `−H/2+Borda+i·(H−2·Borda)/(nf−1)`; z análogo com B/np/j. 3×3 contém **8**, não 9 fixações. `EspacMin` é a menor distância euclidiana entre pares; um único ponto retorna 0. |
| Reação da barra, N:1151–1162 | Sem resultado da barra: erro. `k0=0` no N1, 3 no N2. `Rx=C·F[k0]−S·F[k0+1]`; `Ry=S·F[k0]+C·F[k0+1]`; `Rm=F[k0+2]`. `N=−(Rx·NormX+Ry·NormY)`; `V=abs(Rx·yx+Ry·yy)`; `M=−Rm`. No piso com normal +Y: N=−Ry, V=abs(Rx). |
| Informativos, N:1152–1153 | `x=0` no N1, senão L; `NBarra=N(x)`, `VBarra=abs(Q(x))`, `MBarra=M(x)`. Não substituir forças da placa por essas forças locais em barra inclinada. |
| Tentativa sem contato, N:1170–1181 | `s=1 se M>=0 senão −1`; `ma=abs(M)`; `u_i=−s·y_i/1000` em m; `S2=Σu_i²`; `T_i=N/nb+(ma·u_i/S2 se S2>0 senão 0)`. Se `min(T_i)>=−1e−9`, modelo todas tracionadas, sem pivô. |
| Giro com contato, N:1184–1192 | Se a tentativa anterior der compressão: `a=(RefIn se PivoNaMesa senão H)/2000` em m; `d_i=max(0,u_i+a)`; `k=(ma+N·a)/Σd_i²` se denominador>0, senão 0; `T_i=k·d_i se k>0 senão 0`; `PivoY=s·a·1000`. Se k≤0, modelo sem tração. |
| Final, N:1194–1195 | Truncar qualquer T negativo para 0; `Tmax=max(T_i)`; `Fv=V/nb`. |

Saídas: dimensões e orientação; normal e ângulo; reações e esforços nos dois sistemas; `Paraf=[(y,z,T)]`; `Modelo`, `PivoY`, `Tmax`, `Fv`, `EspacMin`, `TemEsforcos`, erros e avisos (N:1026–1063). Inclinação mantém cálculo de fixações, mas impede aprovação da placa. Cargas aplicadas no próprio nó vão diretamente ao apoio e não entram nas forças de extremidade consideradas; emitir o aviso de N:1164–1167.

## 5. Parafuso métrico e chapa

### 5.1 Parafuso

Entradas: Tmax/Fv em kN, diâmetro em mm, classe, rosca no corte e nPl. Saídas: At/Ar/Ad, Sp, σ, τ, NT, NV, NComb, Fi e torque.

| Origem | Equações |
|---|---|
| N:1146–1148 | `Ad=π·d²/4`; At/Ar/Sp das tabelas; `Fi=0,75·At·Sp/1000` kN; `Torque=0,2·Fi·d` N·m. |
| N:1196–1197 | `σ=Tmax·1000/At`; `τ=Fv·1000/(Ar se RoscaNoCorte senão Ad)` MPa. |
| N:1206–1209 | `NT=Sp/σ` se σ>1e−9, senão ∞; `NV=0,577·Sp/τ` se τ>1e−9, senão ∞; `vm=√(σ²+3τ²)`; `NComb=Sp/vm` se vm>1e−9, senão ∞. Aprova fixação se NComb≥nPl. |
| N:1309–1315 | `SegurancaCom` repete σ, τ e vm usando outro índice de diâmetro/classe, mantendo Tmax e Fv. |

Referência citada em N:864–866: material “Junções não permanentes / Tabela Parafusos”, áreas da Tabela 3 (rosca métrica grossa), classes da Tabela 6, resistência de prova e von Mises. Não é a resistência de cálculo de parafusos da NBR 8800 implementada em `ligacoes.py`: não usar fub no lugar de Sp, nem aplicar GA2 a essas contas.

### 5.2 Espessura da chapa

Entradas: coordenadas/T_i, H/B, RefIn, diâmetro, pivô, N, fy (MPa), nPl, espessura adotada (mm). Modelo simplificado de faixa em balanço, `σ=6·Momento/(largura·t²)≤fy/nPl`; não há item normativo específico atribuído no código.

1. Agrupar fixações por `round(y,3)` mm, somando tração e quantidade q (N:1215–1220).
2. Ignorar fileira com T≤1e−9 ou `abs(y)<RefIn/2`. Para as demais: `e=max(abs(y)−RefIn/2,Dia)`; `b_ef=min(B,q·(2e+Dia))`; `t_T,fileira=√(6·T_fileira·1000·e·nPl/(b_ef·fy))`. Guardar maior valor e sua TLinha/Braco/LargEf; empate conserva primeira encontrada (N:1222–1232).
3. Havendo pivô: `CConc=ΣT_i−N`; `BracoC=abs(PivoY)−RefIn/2`; se CConc>1e−9 e BracoC>0, `t_C=√(6·CConc·1000·BracoC·nPl/(B·fy))`, senão 0 (N:1233–1244).
4. `EspNec=max(t_T,t_C)`. Lista comercial de N:1318, mm: `[6.35,8,9.5,12.7,16,19,22.4,25.4,31.5,38.1,50.8]`.

Saídas: TLinha, Braco, LargEf, FyChapa, EspNecT, CConc, BracoC, EspNecC, EspNec. Proposta de verificação Python: `taxa_chapa=EspNec/Esp`, limite 1; espessura comercial escolhida é a primeira ≥ EspNec, com indicação explícita se nenhuma atende.

**Diferença importante:** `PlacaCalc.Calcular` não usa `Esp` para aprovar chapa e não calcula solda. `ResPlaca.Passa` (N:1062) é apenas `Erro vazio && ChOk && !Inclinada`. Preservar esse resultado como `passa_legado`; o `ok` agregado Python deve exigir também chapa e solda verificadas, expondo a diferença. Não atribuir essa agregação ao algoritmo original.

## 6. Chumbadores

### 6.1 Coeficientes e referências

Entradas: item de catálogo, tipo de carga/risco, indicador de rotativa, coeficiente escolhido, nPl, fck da laje, classe da barra, T/Fv e coordenadas.

| Tipo, índice (N:928–932) | Baixo risco | Alto risco |
|---|---|---|
| estática, 0 | 3 | 5 |
| variável, 1 | 4 | 5 |
| dinâmica, 2 | 7 | 15 |
| choque, 3 | 5 | 10 |

Fonte citada: Walsywa “Fixação Mecânica”, pág. 07. Risco é escolha válida após trim/minúsculas; senão automático, alto apenas quando `m != null && m.TemRotativa` (N:935–939). Tipo válido usa índice; senão dinâmica com rotativa, variável sem rotativa, nunca estática por omissão (N:943–948). `NSeg=nPl`; `NExig=CoefChumbador se >0 senão coeficiente tabelado`. Avisar coeficiente abaixo da tabela por mais de 1e−9; ou, sem override e com baixo risco automático, avisar risco presumido (N:1113–1123).

### 6.2 Fórmulas de concreto, interação e aço

| Origem | Cálculo exato |
|---|---|
| N:887–890, 1141–1142 | Cargas kgf → kN multiplicando por **0,00980665**. Torque kgf·m → N·m multiplicando por **9,80665**. Na inicialização do chumbador: Ad=π·Rosca²/4, At=Ar=Ad; químico substitui áreas depois. |
| N:1277–1279 | `FckLaje=p.Fck se >0 senão 20`; `FFck=min(1,√(FckLaje/c.Fck))` se fck de catálogo>0, senão 1; ψ obtido pelo algoritmo de cones. |
| N:1280 | Se Tu>0: `Tref=TuKN·FFck·ψ`; `Vref=VuKN se Vu>0 senão 0`; `NRef=NExig`. |
| N:1281 | Se Tu≤0 (WQA): `Tref=TuRecKN·FFck·ψ`; `Vref=VuRecKN`; `NRef=1`. Não dividir novamente pelo coeficiente de ruptura. |
| N:1282–1285 | `TuEf=Tref`; `NT=Tref/Tmax` se Tmax>1e−9 senão ∞; `NV=Vref/Fv` se Vref>0 e Fv>1e−9 senão ∞; NComb pela interação abaixo. |
| N:957–961 | `A=(max(0,T)/Tref)^(5/3)` se Tref>0, senão ∞ se T>1e−9 ou 0 no restante. `B=(max(0,V)/Vref)^(5/3)` se Vref>0, senão **0**. `NComb=(A+B)^(−3/5)` se A+B>1e−12, senão ∞. |
| N:964–966 | Sobrecarga de `Seguranca(c,T,V,ψ)` usa apenas TuKN·ψ e VuKN; não aplica FFck nem trata recomendada. Não confundi-la com AvaliarChumbador. |
| N:1286–1298 | Químico: classe própria válida ou 4.6; SpBarra da tabela; At=c.AtB e Ar=c.ArB; `σ=Tmax·1000/At`; `τ=Fv·1000/Ar`, sempre área de rosca, independente de RoscaNoCorte; `NAco=SpBarra/√(σ²+3τ²)` se vm>1e−9, senão ∞. Mecânico: NAco=∞. |

A interação equivale a `(T/Tref)^(5/3)+(V/Vref)^(5/3)=(1/n_obtido)^(5/3)`; é **hipótese de projeto**, não equação fornecida pelo catálogo. A correção √fck é hipótese baseada no comportamento de cone citado na ACI 318 cap. 17, limitada a 1 para impedir aumento sem ensaio. Aplicar tabela Walsywa ao Indufix também é decisão de projeto. WQI/WQE sem corte de concreto têm termo de corte omitido nessa interação, mas **mantêm o corte no aço da haste**. Declarar que isso não verifica a resistência de concreto ao corte.

### 6.3 Cones e espaçamento

N:969–991 cita áreas projetadas, ACI 318 cap. 17 / EN 1992-4 e raio 1,5·hef do catálogo Walsywa pág. 07. Algoritmo efetivamente implementado:

1. Item nulo ou lista vazia: ψ=1. Selecionar pontos com T>1e−9; se nenhum, usar **todos** os pontos.
2. `h=1,5·hef`. Para cada eixo, arredondar centros para 3 casas de mm, remover duplicados e ordenar.
3. Somar comprimento da união dos intervalos `[centro−h,centro+h]`: iniciar pelo primeiro; se início do próximo≤fim atual, atualizar fim para centro+h; senão acumular trecho e iniciar outro.
4. `A=uniao(y)·uniao(z)`; `ψ=min(1,A/(nt·9·hef²))`.

**Não é uma união bidimensional geral de quadrados.** O produto das projeções coincide em malhas cartesianas completas, mas pode superestimar a área para subconjuntos não retangulares (inclusive contornos). Manter como `fator_cones_legado` na compatibilidade; uma união geométrica exata exige versão e testes próprios, pois altera resultados. Arredondamento C# é ao par mais próximo; observar diferenças binárias em empates na implementação Python.

`EspacMinimo(c)=6·c.Rosca`, N:994–995, comentário cita ACI 318, 17.9.2. Embora o comentário se refira a expansivos por torque, `EspacOk` aplica o limite a **qualquer** item de catálogo: `EspacMin<=0 ou EspacMin>=6·Rosca−1e−9` (N:1059). `s_mm` e `c_mm` do CSV são metadados de instalação; não substituem esse limite na aprovação. Borda da chapa não é borda do concreto.

### 6.4 Saídas, aprovação, comparação e dimensionamento

Saídas além das forças: Ch, Dia, NExig, NSeg, TipoCarga/TipoAuto, RiscoAlto/RiscoAuto, AvisoCoef, FckLaje, FFck, PsiCone, Tref, Vref, TuEf, NRef, NT/NV/NComb, ClasseBarra, SpBarra, At/Ar, σ/τ, NAco, torque e dados de instalação.

Em N:1059–1062:

```text
ChOk = NComb >= (NRef se Ch != null senão NExig)
       e (sem chumbador químico ou NAco >= NSeg)
       e EspacOk
ApFix = max(ac, aa)
ac = 0 se NComb infinito, senão (NRef se chumbador senão NExig)/NComb
aa = NSeg/NAco se químico e NAco finito, senão 0
Passa = Erro vazio e ChOk e não Inclinada
```

ApFix não inclui o espaçamento; pode ser <1 e ChOk falso. As comparações de segurança são exatas no legado, sem tolerância extra de aprovação. Disposições construtivas devem ficar separadas das taxas resistentes, conforme `ligacoes.py`.

`CandidatoChumbador` copia apenas NExig, NSeg, Tmax, Fv, EspacMin e a lista Paraf, depois chama AvaliarChumbador (N:1301–1305). Não redistribui forças nem recalcula chapa. A lista é compartilhada no C#; a função apenas a lê. Python deve preservar imutabilidade das entradas. O argumento m de AvaliarChumbador não é usado: o coeficiente já está no resultado-base.

`DimensionarChumbador`, N:1252–1270: filtrar linha se fornecida; ordenar estavelmente por Rosca e depois Hef; tentar arranjos `(2,2)`, `(2,3)`, `(3,3)`, nessa ordem. Pular arranjo cujo produto seja menor que o produto original; dimensões tentadas são `max(arr[0],f0)` e `max(arr[1],q0)`. Recalcular placa para cada candidato. Aceitar primeiro com erro vazio, esforços disponíveis e ChOk, **sem exigir Passa, chapa ou solda**. Retornar descrição `quantidade × nome (fabricante)`. No C# modifica placa em sucesso; em falha restaura item/contagens e retorna vazio. Python deve retornar escolha explícita sem mutação. “Mais barato” é apenas heurística de quantidade/rosca/embutimento, sem preços; 3×3 significa oito fixações no contorno.

## 7. Solda de contorno pilar–placa

### 7.1 Entradas, fórmulas e critérios

Origem **S**, não N: seção com forma/H/Bf/Tw/Tf em m, deitado booleano, N/V em kN, M em kN·m, perna h e tChapa em mm, eletrodo. Usar esforços **locais da barra** para solda (PADRAO-MEMORIAL §6.2). Barra inclinada: ligação não verificada por este modelo.

Referências citadas em S:1–10: Shigley tabelas 9-1/9-2, área de garganta e segundo momento unitário; tabela de resistência de eletrodos/AISC por tensão admissível. S:88 cita NBR 8800 tabela 10 / AWS D1.1 tabela 5.8 para mínimo pela parte mais fina. A edição não é indicada no C#. Não atribuir a esta rotina a verificação completa da NBR 8800:2008 presente em `ligacoes.py`.

| Origem | Fórmula / regra exata |
|---|---|
| S:34–41 | Eletrodos E60/E70/E80/E90/E100/E110/E120; τadm=124/145/165/186/207/228/248 MPa. Vazio ou desconhecido usa E70; converter para maiúsculas e remover `XX`. |
| S:48–50 | Seção nula ou H≤0 gera erro. `d=1000·(Bf positivo ou H)` se deitado, senão 1000·H; b usa a dimensão oposta. |
| S:52–63 | TC/CIRC: `r=1000·sec.H/2`; `A=1,414·π·h·r`; `Iu=π·r³`; `c=r`. Outras formas, inclusive I/U/L: contorno retangular, `A=1,414·h·(b+d)`; `Iu=d²·(3b+d)/6`; `c=d/2`. Para ambos, `I=0,707·h·Iu`. |
| S:64–67 | `τ1=abs(V)·1000/A`; `τ2=abs(N)·1000/A+abs(M)·10⁶·c/I`; `τ=√(τ1²+τ2²)`; `Ap=τ/τadm`. Usar módulos separadamente, inclusive compressão de N. |
| S:68–71 | Espessura do perfil: `1000·min(Tf,Tw)` para I/U; demais `1000·Tw` se Tw>0, senão 0. `t_min=min(espessura positiva ou 10⁹, tChapa positiva ou 10⁹)`. HMinNorma pela tabela abaixo. |
| S:90–94 | `PernaMinimaNorma(t)=0` se t≤0 ou t≥10⁸; senão 3 se t≤6,35; 5 se t≤12,5; 6 se t≤19; 8 no restante (mm). |
| S:73–75 | `L=A/(0,707·h)` mm; `LNec=L·min(1,Ap)` mm; `Massa=(h²/2)·L·7850·10⁻⁹` kg. LNec é estimativa, não redimensionamento geométrico do grupo. |
| S:26–29 | AbaixoMinimo: HMinNorma>0 e h<HMinNorma−1e−9. MaiorQueParede: espessura>0 e h>espessura+1e−9. `NEquiv=0,577·(τadm/0,30)/τ` se τ>1e−12, senão ∞. `Passa=Erro vazio && Ap<=1 && !AbaixoMinimo && !MaiorQueParede`. |

Manter constantes **1,414**, **0,707** e **0,577** sem substituí-las por raízes exatas no modo compatível. E70 usa τadm=145 MPa e FEXX equivalente `145/0,30`, que não é exatamente o `fw=485 MPa` de `ligacoes.py`. O coeficiente NSeg não multiplica essa tensão admissível: a tabela já incorpora segurança.

Saídas ResSolda (S:17–29): erro, grupo, H, A, I, Tau1, Tau2, Tau, TauAdm, Ap, L, LNec, Massa, HMin, Espessura, TChapa, HMinNorma, AbaixoMinimo, MaiorQueParede, NEquiv e Passa.

### 7.2 Escolha de perna e diagnóstico

S:36: pernas comerciais `[2,3,5,6,8,10,11,12,16,20,22,25]` mm.

`Minima`, S:97–103, procura primeira perna ≥ mínimo normativo−1e−9 e com Ap≤1, calculado pelas mesmas fórmulas de tensões (S:79–85). Retorna 0 se nenhuma passa. **Não limita a perna à espessura do perfil**, portanto HMin sozinho não garante Passa. Não chamar com seção nula; o wrapper deve validar antes. O cálculo direto também não valida h>0; a API Python deve rejeitar perna não positiva, deixando h=0 apenas como pedido explícito de dimensionamento no wrapper.

`Diagnosticar`, S:109–149: erro de perfil → sem solução; solda que passa → diagnóstico vazio; parede menor que mínimo → sem solução e PernaMax=parede; demais falhas têm razão por prioridade: perna maior que parede, abaixo do mínimo, tensão acima do admissível. Busca primeiro no eletrodo atual, depois nos superiores; em cada um, pernas crescentes que respeitem mínimo e parede e tenham Ap≤1. Retorna primeira sugestão. Se não houver, SemSolucao=true, PernaMax=min(parede,25) se parede>0, senão 25. Saídas DiagSolda: Porque, OQueFazer, Eletrodo, Perna, PernaMax, SemSolucao; `Passa=(Porque vazio)` (S:153–159).

A rotina legada não verifica metal base como `resistencia_filete`, comprimento mínimo 4b/40 mm nem máximo normativo `t−1,5 mm`; seu máximo é o limite prático da parede. Não incorporar essas regras ao modo legado sem identificar mudança de método.

## 8. Comparação de fixações

### 8.1 Entradas e bases textuais

Entradas: dois ou mais códigos/nomes/linhas, resultado-base da placa, dados da placa e contexto. O componente de avaliação individual aceita também um único item. Saídas: colunas com item, informações de linha e avaliação estrutural opcional; pares explicativos; avisos. Não há uma nova equação de resistência: reutilizar `avaliar_chumbador`.

`fixacoes.csv` tem 12 linhas, nove calculáveis e três informativas. Campos mapeados em N:2152–2176: `linha`, `fabricante`, `informativa`, `sem_catalogo`, `familia`, `acionamento`, `elemento`, `faixa_diametro`, `fck_ensaio`, `natureza_carga`, `tracao_tabelada`, `corte_tabelado`, `carga_dinamica`, `distancias_reduzidas`, `materiais_base`, `recuperavel`, `tempo_cura`, `acabamento`, `paginas`, `tipo_desc`, `caracteristicas`, `uso_recomendado`, `usos_tipicos`, `pontos_atencao`, `faixa_cargas`. Todos textos, exceto indicadores `sim` exato. Textos com listas usam `|`. Não converter faixas textuais em capacidades numéricas.

`fixacoes-pares.csv` tem 11 pares; N:2181–2212 mapeia `id`, `titulo`, `linhas_a`, `linhas_b`, `em_que_diferem`, `escolha_a_quando`, `escolha_b_quando`, `mesmo_desempenho`, `fonte`. Título separado por `×` fornece NomeA/NomeB, com fallback A/B. Os conjuntos de linhas são separados por `|`.

Pares existentes: (1) CB/CBE; (2) CB/CBPL; (3) CBE/CBEPL, com CBPL também no conjunto A; (4) família CB/WB; (5) WB/WLT; (6) WLT/Golpe de Metal; (7) WQA/WQI 44; (8) WQI 44/WQE 500; (9) WQA/WQE 500; (10) mecânicos por torque/químicos; (11) químicos/PU 500. Preservar os conjuntos efetivos do CSV: os títulos resumidos não bastam para selecionar pares.

### 8.2 Seleção e avaliação (N:2087–2319)

- `AcharLinha`, N:2216–2225: trim; comparar sem distinção de caixa com linha ou nome curto (remove `Walsywa `); se falhar, resolver chumbador por código/nome e buscar sua linha. `Informativas` retorna as marcadas (N:2228–2230).
- `Avaliar(item)`, N:2233–2238: nulo retorna nulo; associa metadados; só calcula CandidatoChumbador quando rBase existe e TemEsforcos. Sem esforços: coluna informativa de catálogo, Res nulo.
- `Avaliar(texto)`, N:2241–2258: trim; tentar busca exata, depois case-insensitive por código/nome ou nome curto `linha sem Walsywa + pol + hef número` (sem sufixo mm). Depois resolver linha; linha calculável usa **primeiro item na ordem do catálogo**, não o melhor. Linha informativa retorna Ch/Res nulos. Identificador desconhecido retorna nulo.
- `ParFixacao.Envolve`, N:2102–2109: falsidade se linha vazia ou iguais; verdade se uma pertence ao conjunto A e a outra ao B, em qualquer ordem. Pertencimento é exato. `ParesPara`, N:2261–2274: remover vazios e duplicados, testar todos os pares de linhas e retornar cada registro aplicável uma vez, na ordem do CSV.
- Título/chave de coluna calculável usam código; subtítulo informa linha, Ø em polegadas e hef; coluna informativa usa linha e indicação fora do cálculo (N:2113–2120).

Avisos de N:2277–2319, em ordem, sem ranking automático por ruptura bruta:

| Gatilho | Conteúdo obrigatório |
|---|---|
| Mistura de ruptura/recomendada | Não comparar diretamente; ruptura precisa do coeficiente aplicável. WQI/WQE têm ambas e acionam essa distinção. |
| Classes de concreto distintas ou presença de C28/C30 | Informar classes de ensaio e FFck para a laje, sem extrapolar resistência para cima. |
| `corte_tabelado == não` | Corte não informado no concreto; WQI/WQE verificam corte apenas no aço. |
| WQA | nRef=1 sobre recomendada; favorecimento frente a mecânicos com n=15; informar reduções de catálogo. |
| Linhas informativas | WLT sem corte, Golpe de Metal para aplicações leves, PU 500 espuma sem resistência estrutural. |
| `sem_catalogo` | Indufix sem PDF/descrição completa no guia; apresentar atributos básicos e cálculo da tabela. |

WQA: catálogo químico pág. 05 cita −30% nas cargas no espaçamento mínimo, −40% na tração e −50% no corte na borda mínima, −30% entre 100 e 120 °C e −20% na tração em C15. **Essas reduções são avisadas, não calculadas por AvaliarChumbador**; o modelo não recebe borda do concreto nem temperatura. A tabela C40 do guia também não é uma variante no CSV. Não apresentar a verificação legada como se cobrisse essas condições.

A comparação deve mostrar separadamente cargas originais e natureza, fck de ensaio, FFck/ψ, cargas de referência e coeficiente, cargas admissíveis `Tref/NRef` e `Vref/NRef` quando informadas, verificação de aço e espaçamento. São propostas de apresentação conforme FIXACOES-GUIA §5.3; não substituir os campos originais. WQI/WQE são avaliados por ruptura/NExig no motor, apesar de também terem coluna recomendada no CSV. Nunca mostrar ausência de corte como resistência infinita disponível ao usuário.

## 9. Casos existentes para converter em testes Python

Não foram executados testes, compiladores ou sincronizadores neste levantamento. Abaixo estão as expectativas escritas no código; expressões são valores esperados tão válidos quanto constantes e evitam inventar resultados do solver.

**Tolerância original (T:15–19):** `abs(obtido−esperado) <= relTol·max(1 se relTol==1e−6 senão 1e−9,abs(esperado))`. Padrão relTol=1e−6. Reproduzir explicitamente `abs_tol` e `rel_tol`; booleanos de `Sim` devem ser verdadeiros. Testar infinitos por predicado, não por subtração.

### 9.1 Fixture de placa em balanço (T:157–191)

Nós (0,0)/(0,3) m; base engastada; perfil W 200 x 46,1, A572 Gr.50; carga Fx=10 kN no topo. Placa H=400, B=300, Esp=19, borda=50 mm, 2×2 M16 8.8, rosca no corte; n do modelo=2. RefIn=203 mm. Pode-se testar núcleo sem solver com N=0, V=10, abs(M)=30.

| Origem T | Esperado |
|---|---|
| 162–163 | abs(M)=30 kN·m; Tmax=42 kN |
| 164–166 | Σ(T_i·d_i)=30 kN·m em torno do pivô; a=0,2 m, distâncias 0,35 e 0,05 m |
| 167–170 | Fv=2,5 kN; σ=42000/157 MPa; NT=600/(42000/157); NV=0,577·600/(2500/144) |
| 171 | Torque=0,2·0,75·157·600/1000·16 = 226,08 N·m |
| 172–175 | Trocar Fx=0 e Fy=+100 kN: Tmax=25 kN |
| 176–179 | Placa pelo perfil com sobra=100 mm: H=B=410 mm |
| 186–188 | e=150−203/2=48,5 mm; bEf=2·(2e+16)=226 mm; EspNecT=√(6·84000·e·2/(bEf·250)) mm |
| 189 | EspNecC=√(6·(ΣT−N)·1000·(200−203/2)·2/(300·250)) mm |
| 190 | EspNec=max(EspNecT,EspNecC) |
| 191 | SegurancaCom com índices (2,3), M12 classe 8.8, é menor que NComb de M16 |

Os testes não fixam constante numérica para NComb nem EspNec. Para conferir fixture de forças, das próprias fórmulas resultam T=42/6 kN por fixação das duas fileiras, duas de cada; isso é valor derivado para orientar a implementação, não uma asserção adicional existente.

### 9.2 Fixture de guincho e eixos (T:287–305, 353–383)

ModeloGuincho: nós 1=(0,0), 2=(0,4), 3=(0,5), 4=(−2,5,0) m. Nós 1/4 engastados; coluna 1–2–3 Tubo Ø114,3x7,30, escora 4–2 Tubo 50x30x2,00; A572 Gr.50; peso próprio ligado; NSeg=1,5; limite de flecha 400. Nó 3: Fy=−4,95 kN, Mz=−4,95 kN·m e Rotativa=true. Nó 4: Parabolt PB 1/4" hef 35 mm, pelo perfil, sobra 100, Esp=19, 2×2, borda 50 mm. `PosicaoOposta` copia modelo e inverte Fx/Mz de nós/cargas rotativas, mantendo Fy (N:584–593).

| Origem T | Asserção esperada |
|---|---|
| 355–357 | No nó 4: V=abs(reação Rx), N=−reação Ry, M=−reação Rm; relTol=1e−6 |
| 358 | V>1 kN e VBarra<0,1 kN |
| 359–362 | Fv·nb=abs(Rx); se todas tracionadas, ΣT=N; senão ΣT≥N−1e−9 |
| 363 | Inclinada=true, Passa=false, aviso contém `inclinada` |
| 365–366 | Copiar placa para nó 1 e retirar do 4: N/NBarra, V/VBarra e M/MBarra diferem <1e−9, sem inclinação |
| 369–372 | Barra horizontal (0,0)–(2,0), Tubo 50x50x2,00 A36, engaste à esquerda e Fy=−3 no nó livre: sem inclinação, abs(N+Rx)<1e−9, abs(V−3)<1e−6 |
| 375 | Rotativa e escolhas automáticas: TipoAuto=true, TipoCarga=2, alto risco e NExig=15 |
| 376–377 | Risco escolhido baixo: NExig=7, RiscoAuto=false |
| 379–380 | Sem rotativa e sem escolhas: tipo variável índice 1, baixo risco, NExig=4, aviso contém `risco` |
| 381–383 | Tipo dinâmica, coeficiente digitado 3: NExig=3 e aviso não vazio |

Os esforços completos dessa fixture dependem do solver e das propriedades do catálogo de perfis. Para testes de integração, portar a fixture e análise, ou obter futuramente um registro de referência do solver; não substituir esses valores por números presumidos.

### 9.3 Catálogo, cones, químicos e dimensionamento (T:387–429)

Fixture: guincho com placa retirada do nó 4 e aplicada ao nó 1; posição oposta; WB 5/8" hef 148 mm, pelo perfil, sobra=100, Esp=12,7, 2×2 e borda=50. Resolver antes de avaliar.

| Origem T | Esperado e tolerância explícita quando diferente do padrão |
|---|---|
| 387–388 | 76 itens; código `CB 12300` e nome `Walsywa CB 1/2" hef 76 mm` resolvem o mesmo item não nulo |
| 394–395 | Se quatro fixações tracionadas: ψ=(EspacMin+444)²/(4·9·148²), relTol=1e−9. Caso contrário, a asserção original compara ψ com ele próprio e não valida a fórmula. |
| 396 | Espaçamento de 220 mm <444: ψ<1 e EspacOk=true |
| 397–398 | TuEf=WB.TuKN·ψ; NComb=((Tmax/(WB.TuKN·ψ))^(5/3)+(Fv/WB.VuKN)^(5/3))^(−3/5), relTol=1e−12 |
| 399 | Pontos (−300,0,1) e (300,0,1), mm/kN, WB hef=148: ψ=1, relTol=1e−12 |
| 400–403 | Dimensionamento retorna descrição não vazia e resultado escolhido ChOk=true; não há código vencedor fixado no teste |
| 406–411 | WQE 500 5/8", borda 50: FFck=√(20/28); TuEf=6595·0,00980665·√(20/28)·ψ; NAco=225/√((Tmax·1000/145,8)²+3·(Fv·1000/130,3)²), relTol=1e−12 |
| 414–417 | WQA 1/2", borda 100: NRef=1; Tref/ψ=1019,72·0,00980665 kN, relTol=1e−12 |

Casos sem solver, iguais à planilha (T:420–429): placa com `Classe=8.8`, sem ClasseBarra; resultado-base NExig=15, NSeg=1,5, um ponto `(0,0,T)` para ψ=1, EspacMin=0 e fck padrão 20 MPa.

| Código | Tmax (kN) | Fv (kN) | Esperado | relTol |
|---|---|---|---|---|
| WQE 500 5/8" | 3,9677 | 0,9289 | NComb=13,78 | 5e−4 |
| mesmo | mesmo | mesmo | NAco=7,53 | 5e−4 |
| WQA 1/2" | 2,4770 | 1,8578 | NComb=3,08, com NRef=1 | 2e−3 |
| mesmo | mesmo | mesmo | NAco=4,68 | 2e−3 |
| CBPL 14300 | 3,9677 | 0,9289 | NComb=3,786 | 2e−4 |

Também exigir ClasseBarra=4.6 e SpBarra=225 para WQE, apesar de Classe=8.8.

### 9.4 Solda e diagnóstico (T:498–512)

`sc` é a seção Tubo Ø114,3x7,30 (T:287,315), orientação normal. Fixture principal: N=−8,87 kN, V=0,5 kN, M=2,32 kN·m, tChapa=8 mm, E70.

| Origem T | Entrada / esperado |
|---|---|
| 499 | PernaMinimaNorma(7,3)=5 mm |
| 500–501 | h=3 mm: Ap<1, AbaixoMinimo=true, Passa=false |
| 502 | Minima(fixture)≥5 mm |
| 503 | NEquiv=0,577·(145/0,30)/Tau; relTol=1e−9 |
| 505–506 | Diagnóstico com h=3: Passa=false, Perna=5 mm, Eletrodo=E70, Porque contém `mínimo` |
| 507 | Recalcular com sugestão: Passa=true |
| 508–510 | Tubo 30x20x1,06, N=0,5, V=0,3, M=0,05, h=3, E70, tChapa=6,35: SemSolucao=true, abs(PernaMax−1,06)<1e−9; Porque contém `1,06` |
| 511–512 | Fixture principal com h=5: diagnóstico Passa=true e OQueFazer vazio |

Não há nesses testes constantes esperadas para A/I/Massa/LNec nem cobertura de todos os formatos de contorno. As fórmulas da seção 7 fornecem a base para novos testes, identificados separadamente dos existentes.

### 9.5 Comparação (T:530–570)

| Origem T | Esperado |
|---|---|
| 530–532 | 12 linhas, 11 pares e 3 informativas |
| 534–539 | Toda linha de chumbadores resolve metadado não informativo e família não vazia; faltantes=0 |
| 540–541 | Parabolt PB SemCatalogo=true; WLT, Golpe de Metal, PU 500 Informativa=true |
| 544–549 | Fixture: ModeloGuincho em posição oposta, sem placa no nó 4; nó 1 com CB 12300, pelo perfil, sobra 100, Esp 12,7, 2×2, borda 50; resolver e gerar base |
| 550–561 | Para CB 12300, WB 12400, WQE 500 1/2", WQA 1/2", PB 1/2" hef 80: Avaliar tem NComb, Tref e ApFix iguais a CandidatoChumbador; químicos também NAco igual; relTol=1e−12 |
| 563–564 | Avaliar Walsywa WLT retorna coluna não nula com Info.Informativa e Res nulo |
| 565–567 | Pares de CB/WB/WQE incluem título `Walsywa CB × Walsywa WB` e título contendo `Mecânico por torque` |
| 568–570 | Avisos das três primeiras colunas (CB/WB/WQE): quantidade≥3, algum contém `Ruptura`, algum `28 MPa`, algum `cisalhamento` |

Os testes de comparação são de equivalência entre caminhos, não fornecem números absolutos novos. Preservar o texto exigido pelas asserções ou testar códigos estáveis de aviso com uma camada de compatibilidade textual.

## 10. Memorial, limitações e critérios de conclusão do porte

Conforme PADRAO-MEMORIAL, o retorno deve permitir montar cada conta na ordem **fórmula → substituição → resultado → limite → aproveitamento → situação**, com tabela `Grandeza | Símbolo | Valor | Unidade | Origem`. Não recalcular no relatório. Mostrar N/V/M da placa e da barra, normal, reação, y/z/T de cada fixação, modelo de contato e equilíbrio, fatores de concreto/cones, coeficientes e sua origem, aço da haste, espessuras dos dois lados e solda. Usar N/mm/MPa nas substituições; forças de entrada continuam em kN/kN·m. Números internos não são arredondados para exibição; usar vírgula decimal no texto. Converter com 0,00980665, apesar do exemplo abreviado 0,00981 no padrão de memorial.

Declarar as limitações que efetivamente se aplicam: placa rígida e chapa por faixas simplificadas; ausência de pressão no concreto, punção, geometria/espessura/armadura da laje e corte de cone na borda; distância ao concreto não conhecida; solda de contorno idealizado; barra inclinada fora do modelo de chapa/solda; corte de concreto ausente em químicos de injeção; interação 5/3 e √fck como hipóteses; coeficientes de ruptura definidos pelo projetista; condições especiais/reduções WQA não modeladas. Indicação de espessura de laje ≥2·hef e reaperto sob dinâmica nos guias são orientações, não verificações implementadas.

Divergências documentais a conservar no registro: FIXACOES-GUIA §0 diz que comparar ainda não existe, mas N:2087–2319 e T:528–570 já implementam/testam o componente; CHUMBADORES registra que a planilha ainda reprova s<s_min, enquanto o C# usa ψ e mínimo 6d. A referência do porte é o comportamento do código lido, com esses desvios declarados.

Além da conversão dos testes existentes, a implementação futura deve acrescentar casos de borda para erros de entrada, nó N2, normal para teto/parede, pivô na mesa, compressão sem tração, malha 3×3 com oito pontos, limiar de 2°, todos sem tração no fator de cones, espaçamento 6d, ausência de corte, limites da tabela de perna, Minima acima da parede e falha de dimensionamento sem mutação. Estes são **testes propostos**, sem resultados de execução neste levantamento.

Concluir o porte somente quando os resultados legados estiverem cobertos pelas expectativas acima, todas as diferenças de agregação/validação estiverem identificadas e os resultados gerais não aprovarem chapa/solda ausentes ou fora de aplicação. Atualizações normativas ou troca do algoritmo de cones devem constituir mudança explícita de método, sem apagar a referência de compatibilidade.
