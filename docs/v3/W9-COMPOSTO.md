# W9 — Perfil duplo (2U caixão / costas com costas) onde se dispõe o perfil

Pedido: "ainda não tem a função de usar perfis U como tubo ou invertidos para ser um I". O usuário mandou a janela
*Disposição do perfil* com um segundo U desenhado à mão, boca com boca (caixão).

## O que já existia (v0.6 Frente D)
- Motor: `secoes.secao_dupla` (arranjos `caixao`, `I` = costas, `cruz`, `lado_a_lado`; ligação soldada / presilhas /
  genérica), spec `{"forma": "dupla"}`, NBR 8800 5.3.4. Ver [PERFIS-COMPOSTOS.md](PERFIS-COMPOSTOS.md).
- Interface: `JanelaComposto`, aberta só por Catálogo › Composto…; ela só criava a seção e a deixava como seção de desenho
  (não trocava a das barras escolhidas). 3D/DWG/STEP desenham os dois perfis (`GeometriaPerfis.Dupla`).

## O que faltava / estava quebrado
1. Escondido: nada na Disposição do perfil nem no painel da barra.
2. U e Ue **dobrados** (formados a frio) davam erro no motor (`secao_dupla` exigia `tw`).
3. O 3D desenhava o componente Ue/U dobrado de uma composta como retângulo (forma `U_pf`/`Ue` não reconhecida em
   `Perfil3D.DoMotor`).
4. Criar a seção e trocar as barras eram duas operações (dois Ctrl+Z) e o tipo da barra (aço/pff) não acompanhava a seção.
5. O nome gerado não levava a distância entre chapas: duas compostas com `a` diferente colidiam (a segunda reaproveitava a
   primeira).
6. A miniatura do perfil não reconhecia nomes compostos; a verificação da barra não mostrava o 5.3.4.

## O que mudou
### Interface
- **Disposição do perfil** (`JanelaDisposicao`, `JanelaPerfilComposto.cs`) ganhou o bloco **Perfil duplo**:
  `Simples | Caixão | Costas com costas` para U (UPN, U laminado, U e Ue dobrados); `Simples | Costas com costas | Cruz` para
  L; `Simples | Lado a lado` para I. Ligação (solda contínua ou chapas espaçadoras), afastamento s e distância entre chapas a
  (mm). O desenho mostra os dois perfis sobre o eixo, com giro/espelho/cardinal, e o nome da seção que vai ser usada. Uma
  linha explica o que acontece (caixão soldado vira tubo; chapas: 5.3.4/9.7.5; flexão em y não verificada).
- **Aplicar** troca a seção das barras escolhidas: a seção nova é calculada pelo motor sem mexer no modelo
  (`POST /api/secao/previa`) e entra no **mesmo PUT** das barras, giro, cardinal e espelho → **um Ctrl+Z desfaz tudo**. Se a
  barra já é composta, a janela abre no arranjo dela e "Simples" volta ao perfil base.
- O **tipo da barra** acompanha a seção: 2U/2Ue dobrados abertos → `pff` (NBR 14762); os demais → `aco`.
- Painel Propriedades da barra: botão **Duplo…** ao lado da Seção (abre a mesma janela); o botão da Seção mostra a miniatura.
- Catálogo › Composto… continua; usa o mesmo nome (`Composto.Nome`).
- Nome: `2<base> <caixao|costas|cruz|lado-a-lado> <soldado|chapas a=…|generica> [s=…]`, ex. `2UPN 200 caixao soldado`,
  `2UPN 200 costas chapas a=500 s=10`. Sem acento de propósito (vira nome de camada no DWG).
- `MiniaturaPerfil` desenha 2U caixão, 2U costas, 2L costas/cruz e 2I a partir do nome.
- Verificar barra: linha "barra composta" com `l/r perfil ≤ ½·KL/r conjunto` (NBR 8800 5.3.4 ou NBR 14762 9.7.5).

### Motor (critério e testes em `testes/test_compostas.py::TestCompostasPFF`)
- `secao_dupla` aceita U simples dobrado (`U_pf`) e `Ue`. Steiner com o centroide de cada U medido das **costas da alma**
  (no PFF `xg` é da linha média: soma-se t/2). Componente guardado como spec `U_pf`/`Ue` com `ri`.
- **2U/2Ue dobrados em caixão soldado com s = 0** = tubo h × 2b de parede t com cantos externos de raio ri + t (os cantos
  alma-mesa dos dois U), como o 2L em caixão; verificado pela NBR 8800 (tubo), como o metalon do catálogo. Enrijecedores
  do Ue desprezados (a favor). A área bate com 2·A do U (teste).
- Demais PFF (costas, chapas espaçadoras, afastados): tipo `dupla`, barra `pff`, NBR 14762 com:
  - compressão: χ da flambagem global do **conjunto** (Ne com J = 2J₁ e Cw = 2Cw₁, a favor) e Aef = 2 × Aef do perfil
    isolado a σ = χ·fy; distorcional do perfil isolado × 2;
  - **9.7.5** (barras compostas comprimidas): l/r de cada perfil entre chapas < ½ do KL/r máximo do conjunto — mesmo
    critério da NBR 8800 5.3.4. Solda contínua com s = 0 dispensa. Sem `a`, ligação genérica ou afastamento sem chapas:
    a compressão não é verificada e a barra reprova (`nao_verificado`) com o motivo. A numeração 9.7.5 foi conferida só
    indiretamente (literatura que cita "recomendações para composição e limitação do índice de esbeltez" da NBR 14762:2010);
    confira na norma;
  - flexão em x: 2 × perfil isolado (como na NBR 8800); cortante: 2 almas;
  - flexão em torno do eixo de simetria (My): **não verificada**, igual à decisão da Frente D na NBR 8800; My acima de
    1e-6·Wy·fy reprova com o motivo (ruído numérico não).
- `projeto._tipo_padrao` reconhece a composta de PFF como `pff`; `validar` não acusa mais `secao_incompativel` nela.
- `projeto.previa_secao` + rota `POST /api/secao/previa` (não muda o modelo, não entra no desfazer).

## Ligações com barra composta (fora do escopo)
O motor de ligações recebe as props equivalentes (h, b, tw, tf; no caixão, `Forma = "TR"` e as chapas encostam nas almas
externas, como num tubo). Não há ligação própria de perfil duplo (chapa entre os dois U, gusset entre 2L etc.). Hoje o
projetista: (1) usa a ligação proposta só como ponto de partida e confere à mão a transferência para os **dois** perfis;
(2) dimensiona as chapas espaçadoras/presilhas e suas soldas/parafusos à parte (NBR 8800 5.3.4 / NBR 14762 9.7.5 pedem
as ligações de extremidade e intermediárias); (3) em 2U costas com chapa de ligação entre as almas, modela a chapa como
gusset no projetista de ligações.

## Limite que aparece em pórtico espacial (decisão pendente)
Barra composta **aberta** (2U costas com chapas, 2Ue, 2L) com qualquer My sai `nao_verificado` (decisão da Frente D,
mantida aqui e copiada na NBR 14762). No pórtico das capturas, com um balanço que tira a simetria, as vigas 2U costas têm
My = 0,017 kN·m contra 2 × MRd,y do UPN isolado = 22 kN·m (0,08 %) e mesmo assim reprovam. Opções para o usuário:
(a) manter (hoje); (b) verificar My como 2 × perfil isolado (limite inferior: ignora a ação conjunta pelas chapas, a favor
da segurança), nas duas normas, mudando o teste `test_presilhas_flexao_x_dobro_e_y_reprova`. Enquanto isso, a janela avisa
e sugere 2U laminado costas soldado (s = 0), que vira I e verifica My.

## Outros limites
- Analítico desenha barras como linhas (por projeto da Vista3D); os dois perfis aparecem no Realista e no Sombreado.
- Ue aparece sem os enrijecedores no 3D e na prévia (como o Ue simples já aparecia).
- O inspetor 2D antigo (`Inspector`/`LinhaPerfil`, só sem modelo 3D aberto) não ganhou o atalho: o modelo 2D (Nucleo.cs)
  não tem seção composta. No 3D o painel da barra é o do motor (`DocumentoEspacial`), onde está o **Duplo…**.

## Provas
- Motor: `python -m unittest discover -s testes` (PROSSEN_PULAR_LENTOS=1): 1302 testes OK (9 pulados), 6 novos em
  `TestCompostasPFF`. Pórtico: `compilar.ps1 -Testes` 242 OK. `--testes-exportar`: todos passaram (4 compostos).
- Capturas `ESTRUCTTO_CAPTURAS=w9comp` (`CapturasW9Comp.cs`): pórtico 6 × 5 × 4 m; pilares UPN 200 → caixão soldado,
  vigas X UPN 200 → costas com chapas (s 10, a 500), vigas Y Ue 150x60x20x2,00 → costas com chapas (NBR 14762).
  Asserções: seção e tipo de cada barra, um Ctrl+Z desfaz a troca das 4 barras (refazer volta), .pstr salvo e reaberto
  com as composições, cálculo com norma certa por barra, 5.3.4 na verificação da viga. Log em `w9-comp.txt`.
