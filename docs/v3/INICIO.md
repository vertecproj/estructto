# Tela inicial, novo projeto e menu Arquivo (v3)

Data: 21/10/2026. Branch `wt-inicio`. Pedido do usuário: "arrume a tela de início e como está funcionando para começar a usar;
o sistema de criar novos arquivos não é bom; revise a tela do menu Arquivo, é bem feia". Tudo 3D: nada mais fala em pórticos
planos ou 2D. Capturas `01-inicio*`, `01-backstage-*`, `02-novo-projeto-*` (só elas: `ESTRUCTTO_CAPTURAS=inicio`).

## Onde está

| Peça | Arquivo |
|---|---|
| Tela inicial, miniaturas 3D, modelos (templates), adaptador `Inicio.Acoes` | `codigo/Estructto/Inicio.cs` |
| Diálogo Novo projeto (3 passos, prévia ao vivo) | `codigo/Estructto/JanelaNovoProjeto.cs` |
| Menu Arquivo (páginas do Backstage) | `codigo/Estructto/Backstage.cs` (antes em `MainWindow.xaml.cs`) |
| Recentes fixados, Opções (`%AppData%\NovoFtool\fixados.txt`, `opcoes.txt`) | `codigo/Estructto/Config.cs` (`Config.Opcoes`) |
| Acento escolhido (recursos dinâmicos) | `TemaPlant.AplicarAcento`, chamado em `App.OnStartup` |
| Motor: `config.projeto` e `config.grade` | `codigo/motor/prossen_estrutural/projeto.py` (`CONFIG_PADRAO`) |

A `TelaInicial` saiu de dentro da vista e virou uma camada sobre o `DockingManager` inteiro (`MainWindow.xaml`): ocupa a janela
toda abaixo da faixa, como a Home do Revit/Fusion, em vez de ficar espremida entre Projeto, Propriedades e Tabelas.

## Tela inicial (`MontarTelaInicial`)

- **Coluna esquerda** (`Surface.1`): selo 48 px + "Estructt**o**" (o final em acento, lockup da LOGO.md), tagline, normas e
  unidades; ações principais como cartões de linha: **Novo projeto** (primário, Ctrl+N), Abrir… (Ctrl+O), Importar CYPE 3D (.ed3),
  Importar Ftool (.ftl); embaixo, **Aprender**: Exemplos prontos, Atalhos e ajuda (F1), Paleta de comandos (Ctrl+K).
- **Direita**: "Começar com um modelo" — galeria com miniatura **real** (prévia do gerador do motor, `POST /api/gerar-estrutura/previa?vazio=1`,
  desenhada em isométrica por `Miniatura3D`): Vazio com grade, Galpão, Edifício, Pórtico, Treliça/cobertura, Mezanino. Clique =
  abre o Novo projeto já com o modelo. Depois, **Recentes** (fixados primeiro) em 2 colunas: miniatura do `.json` (lida do
  arquivo), nome, data, pasta, pino (fixar/desafixar) e ✕ (remover da lista; o arquivo fica). Arquivo sumido vira chip de alerta.
- Teclado: Tab percorre os cartões (todos são `Button`), Enter abre; Ctrl+N / Ctrl+O globais.
- Compacto (altura < 560 px, ex. 1280×720): sem subtítulos nos cartões, miniaturas 128×72, 4 recentes.
- Sem motor: cartões ficam com o ícone (nunca trava a tela); as miniaturas entram quando a prévia responde (`MiniaturasInicio`).

## Novo projeto (`JanelaNovoProjeto`, 1000×640)

Substitui a `JanelaGerar3D` como caminho principal (ela continua em Modelo › Gerar 3D). Ctrl+N, "+" da barra de abas,
Arquivo › Novo e os cartões chamam `MainWindow.NovoProjeto(modelo)`.

| Passo | Campos | Prévia (direita) |
|---|---|---|
| 1 Modelo | galeria dos 6 modelos (seleção realçada) | modelo com a grade padrão |
| 2 Projeto | Nome, Pasta (`OpenFolderDialog`), Cliente, Engenheiro, Empresa, Unidades (fixas kN·m, chip), Normas (NBR 8800 / 6118 / 14762), vento e sismo = depois em Ações | idem |
| 3 Grade e níveis | Eixos X e Y como `3x6 2x5` (três vãos de 6 m e dois de 5 m), alturas dos níveis `3x3,5`; resumo "eixos 1–6 (0 6 12…) · filas A–C · níveis N0, N+3,50"; Material / Pilares / Vigas quando há gerador | ao vivo (300 ms após digitar), contagem de nós/barras/níveis |

Separador de lista é espaço ou `;` (vírgula é decimal). Nos modelos de galpão e treliça os rótulos mudam: vão, módulos
(espaçamento), pé-direito + acréscimo até a cumeeira. Esc/Cancelar sempre fecha; Enter avança/cria; erros aparecem no rodapé
(nome inválido, pasta relativa, arquivo já existente, vão não numérico).

**Criar** (`Inicio.Acoes.Novo`): `POST /api/modelo/novo` → `POST /api/gerar-estrutura` (se o modelo tem barras) → `POST /api/modelo/lote`
com os níveis da grade que o gerador não criou (pavimentos `N+3,50`…) e as cargas G/Q nas vigas → `PATCH /api/config`
`{projeto: {cliente, engenheiro, empresa, normas, modelo}, grade: {x, y, nomes_x, nomes_y}}` → grava `pasta\nome.json`
(`DocumentoEspacial.Criar`, agora `internal`) → abre a aba já salva (sem "Sem título") e ativa a **planta do 1º nível**
(`AtivarNivel`). O projeto entra nos recentes. Captura `02-novo-projeto-criado` confere arquivo, `config.grade`, `config.projeto`,
4 níveis e recentes.

`config.projeto` e `config.grade` são chaves novas de `CONFIG_PADRAO` (informativas; não entram no cálculo). `Propriedades do
projeto` edita `projeto` por `PATCH /api/config`; o relatório usa `projeto.empresa` no carimbo quando existe
(`RelatorioMotor.EmpresaProjeto`), senão a empresa das Opções.

## Menu Arquivo (`Backstage.cs`)

Continua o `Fluent.Backstage` (Esc fecha, animação própria), mas cada item da esquerda abre uma **página** montada na hora
(`RemontarBs`, com `Anim.Entrar` ao trocar): Início (3 cartões de ação + recentes em 2 colunas), Novo (galeria com miniaturas),
Abrir (Procurar…, Exemplos prontos, recentes com fixados primeiro), Salvar e Salvar como… (ações), Exportar (cartões DWG/DXF/STEP),
Importar (CYPE .ed3 / Ftool .ftl), Relatório (ação), Propriedades do projeto (formulário editável; estado vazio sem projeto),
Opções, Sobre (selo 64 px, versão, créditos), Sair. Largura de leitura 1040 px; cartões `Surface.2` com borda `Border.Subtle`.

Ficou: o retângulo branco de foco do item selecionado é do template do Fluent (`FocusVisualStyle=null` não o remove);
se incomodar, sobrescrever o `ControlTemplate` de `BackstageTabItem` em `AplicarFluent`.

## Opções (`Config.Opcoes`, `%AppData%\NovoFtool\opcoes.txt`)

| Opção | Chave | Efeito |
|---|---|---|
| Cor de acento (ciano, azul, verde, âmbar, violeta) | `acento` | `TemaPlant.AplicarAcento`: troca os recursos `Accent.*` e os do Fluent na hora; pincéis estáticos (`BAcento`) só ao reiniciar |
| Engenheiro / Empresa padrão | `autor`, `empresa` | preenchem o Novo projeto; empresa também grava `empresa.txt` do relatório |
| Pasta dos projetos | `pasta` | sugerida no passo 2 (atualizada ao criar) |
| Unidades exibidas (kN · m / tf · m) | `unidades` | só o texto da barra de status (ponytail: o modelo é sempre kN, m) |
| Salvamento automático (1–30 min) | `autosalvar` | intervalo do `autoSalvar` (cópia de recuperação) |
| Restaurar painéis | — | mostra de novo os `LayoutAnchorable` ocultos |

## Adaptador para o merge do wt-so3d

`Inicio.Acoes` (em `Inicio.cs`, marcado `ponytail:`) concentra tudo que a tela chama: `Recentes/Fixar/Remover`, `Abrir`,
`AbrirDialogo`, `ImportarCype`, `ImportarFtool`, `Exemplos`, `Previa`, `Novo`. Quando o documento único 3D expuser
`NovoDocumento(template)`, `AbrirArquivo(path)` e `ArquivosRecentes`, trocar só ali.

## O que saiu

- Assistentes 2D (viga contínua, pórtico plano…) e o texto "Análise e verificação de pórticos planos" da tela inicial;
  `LinhaEspacialInicio` do `DocumentoEspacial.cs`; a lista de botões do Backstage antigo (`MontarBastidores` em
  `MainWindow.xaml.cs`); `Exemplos 3D…` virou cartão em Abrir e item de Aprender.
- `Miniatura` 2D ficou só para a `JanelaAssistente` (legado, `ponytail:` — sai com o wt-so3d) e agora desenha pela `Miniatura3D`.

## Conferência

`dotnet build -c Release` 0 erros; `codigo\portico\compilar.ps1 -Testes`; `capturar.ps1` completo sem `erros.txt`.
Melhores capturas: `01-inicio.png`, `01-inicio-1280x720.png`, `02-novo-projeto-galpao-passo3.png`, `02-novo-projeto-vazio-passo1.png`,
`02-novo-projeto-criado.png`, `01-backstage-inicio.png`, `01-backstage-opcoes-1280x720.png`, `01-backstage-propriedades-1280x720.png`.
