# W7 — Biblioteca e projetista de ligações em aba (sem janela separada)

Pedido (27/10/2026): "não quero mais que isso abra numa janela separada, quero que abra dentro da interface do software"
(a "Biblioteca de ligações · 23 padrões" e o "Projetar ligação" abriam como janelas por cima da principal).

## Como ficou

- A biblioteca e o projetista abrem como **aba na barra de documentos**, depois das abas de modelo:
  `☰ / Início / modelo.pstr ✕  Biblioteca de ligações ✕  Ligação · Chapa de extremidade flexível ✕ / +`.
- A aba cobre a área de trabalho (painéis Projeto/Propriedades/Tabelas e a vista), como a tela Início. A faixa de opções e a
  barra de status continuam.
- Várias ligações = várias abas. Trocar de aba preserva tudo (padrão, dados, esforços, resultado, rolagem): todas as abas ficam
  vivas no `HostLigacoes` e só a ativa fica visível, então `Loaded`/`Unloaded` não disparam a cada troca.
- Voltar ao modelo: clicar na aba do modelo (ou em Início).
- Fechar: ✕ da aba, botão do meio na aba, botão Fechar, Esc (com o foco na aba) ou Ctrl+F4. Fechar a aba ativa mostra a ligação
  vizinha ou, se não houver, o modelo.
- "Biblioteca" e "Trocar padrão" trocam dentro da mesma aba (o nome da aba acompanha).
- Abrir/Salvar .pcon, Gerar memorial e Aplicar ao grupo continuam iguais; os diálogos de arquivo têm como dono a janela principal.
- Abrem em aba: faixa "Ligações e bases" (Projetar ligação, Parafusos, Filete de solda, Chapa simples, Ligação de tração),
  Ctrl+K, Início › Nova conexão, Abrir .pcon, clique numa chapa/parafuso no 3D e Gerenciador de grupos › Projetar.
- O cartão "Placa de base" da biblioteca volta para a aba do modelo antes de pedir o nó (a placa continua janela).
- Teclado com uma aba de ligação na frente: as teclas são da aba (Tab circula dentro dela, Enter nas caixas confirma,
  Esc fecha). Da janela só valem Ctrl+K, Ctrl+S, Ctrl+O e Ctrl+N (O e N voltam ao modelo antes). Digitar não vai mais para a
  linha de comando nem Delete/Ctrl+Z mexem no modelo escondido.

## Abordagem

`JanelaLigacaoProjeto` passou de `JanelaEscura` (Window) para **`UserControl`** (pesquisa em `C:\dev\wt\pesquisa-w7\software.md`,
item C). Reparentar o `Content` de uma Window nunca mostrada foi descartado: a Window fica viva em `Application.Windows`
(com `ShutdownMode` padrão, segura o processo aberto), não serve de `Owner` de diálogo e os eventos de teclado dela não chegam.

Na classe só mudou a casca: base `UserControl`, `using static JanelaEscura` (B, T...), `Title` (compatibilidade), `Close()`
(avisa o host por `Fechou`), `AbaMudou` (chamado no fim de `Montar`), `NomeAba`, `Dono` para os diálogos, Esc/Tab no próprio
controle e o `Loaded` inicial só uma vez. O host está em `LigacaoAbas.cs` (partial de `MainWindow`): `MostrarLigacao(j)`
(null = modelo), `FecharLigacao`, `RedesenharAbasLig`, `TeclaLigacao`. `RedesenharAbas` (Documentos.cs) só ganhou duas linhas:
aba de modelo não aparece ativa com uma ligação na frente e o clique nela volta ao modelo.

Hoje a janela não perguntava nada ao fechar com projeto não aplicado ao grupo; a aba mantém isso (sem pergunta).

## Arquivos

`JanelaLigacaoProjeto.cs` (casca + integração + capturas 51), `LigacaoAbas.cs` (novo), `MainWindow.xaml` (`AbasLig`,
`HostLigacoes`), `Documentos.cs` (2 linhas), `Acoes.cs` (teclas), `MainWindow.xaml.cs` (Início volta ao modelo),
`ComandosAnalise.cs` (AbrirLigacao sempre na janela principal), `TestesFluxo.cs` (projetista em aba), `CapturasW7Aba.cs` (novo),
`Capturas.cs` (2 linhas de chamada).

## Prova

- `ESTRUCTTO_CAPTURAS=w7aba` → `63-aba-biblioteca.png`, `63-aba-projetista-chapa-extremidade.png` (+ 1280x720),
  `63-aba-duas-ligacoes-e-modelo.png`, `63-aba-volta-ao-modelo.png` e `63-aba.txt` (abre pela faixa, troca de aba pelo clique com
  o estado preservado, Biblioteca na mesma aba, volta ao modelo, fecha com o botão do meio e com Esc).
- As capturas 51) do projetista (`ESTRUCTTO_CAPTURAS=projetista`) agora fotografam a janela principal com a aba, inclusive o
  clique real no seletor de perfil (v0.6.12: a janela principal vai para 0,0 por cima) e o clique na chapa/parafuso do 3D.

## Outras janelas que podem seguir o mesmo caminho (não convertidas)

`JanelaPlaca` (placa de base), `JanelaLigacoesGrupos` (gerenciador de grupos: ao projetar um grupo ele fica por cima da aba),
`JanelaVerificacaoBarras`/`JanelaVerificarBarraMotor`, `JanelaComparar`, `JanelaTrelicas`, `JanelaCype`, `JanelaRelatorio`,
`JanelaCatalogo`. Todas herdam de `JanelaEscura` e montam o `Content` em código: a receita é a mesma (base `UserControl`,
`Close()` → evento, `ShowDialog(this)` → janela principal, `Loaded` uma vez, `MostrarLigacao` genérico).

## Calcular na aba de ligação (W7-CALC)

Reclamação: "não consigo de fato clicar em calcular automaticamente no projetista". A ligação calculava ao vivo (edição → 250 ms →
`CalcularAgora`), mas não havia botão, F5 era engolido pela aba e "Calcular" da faixa recalculava o modelo escondido (ou dizia
"Nada para calcular: o modelo ainda não tem barras" com a aba aberta por Início › Nova conexão ou .pcon).

| Caso (aba de ligação ativa) | F5 / Calcular da faixa / botão Calcular da aba |
|---|---|
| Grupo do modelo | recalcula o modelo se estiver desatualizado, relê o grupo, redimensiona (`/dimensionar`), recarrega os esforços do membro selecionado (ou o governante) e verifica de novo. Tipo e dados editados na aba ficam. Esforços "digitados": só verifica. |
| Calculadora solta / .pcon | só verifica (`CalcularAgora`). |
| Biblioteca aberta | mensagem "Escolha um padrão na biblioteca para calcular." (nunca "Nada para calcular"). |
| Falha ao recalcular | erro na própria aba (cabeçalho e passo 3), sem apagar os dados. |

- `MainWindow.Calcular()` (usado por F5, faixa, barra de acesso rápido, F9 e comando CALCULAR) começa por `LigacaoAtiva.CalcularTudo()`;
  `TeclaLigacao` deixa passar F5 (antes devolvia `true` para toda tecla ≠ Ctrl+K/S).
- Botão **Calcular** (secundário; o primário continua sendo "Aplicar ao grupo"/"Gerar memorial") ao lado do indicador no topo do
  desenho. Indicador legível: `calculando…` (com barra de brilho), `atualizado · ao vivo · N verificações`, `erro · corrija os dados`.
- Grupo **sem projeto**: `UsarGrupo` aplica o tipo sugerido pelo motor (tabela `Sugeridos`, espelho de `TIPOS` em
  `aco/ligacoes_modelo.py`: viga–pilar/viga–viga → `chapa_simples`, emenda → `parafuso`, gusset/treliça → `ligacao_tracao`, tubos) e a
  aba já abre calculada, com perfis e esforços do pior membro. "Trocar padrão" leva à biblioteca.
- `/dimensionar` falhou ao abrir: a aba mostra o erro ("Não foi possível dimensionar o grupo…", "Sem esforços do modelo"), com
  **Tentar de novo** (F5) e **Biblioteca** (esforços de exemplo só por escolha explícita). Antes: só um toast e esforços de exemplo.
- Código: `JanelaLigacaoProjeto.cs` (`CalcularTudo`, `UsarGrupo(recarga)`, `MontarErroGrupo`, `MontarStatus`, `Recarregar`,
  `CarregarGrupo`/`CriarProjetistaGrupo`), `LigacaoAbas.cs` (`TeclaLigacao`), `MainWindow.xaml.cs` (`Calcular`).
- Prova: `ESTRUCTTO_CAPTURAS=w7calc` → `64-calc.txt` (grupo sem projeto abre com tipo e 18 verificações; Calcular da faixa e F5 de
  verdade pela janela recalculam a aba do grupo; F5 não desfaz dados; digitados e calculadora solta só verificam; falha do
  `/dimensionar` vira erro na aba) e `64-calc-*.png`. Não coberto por teste automático: modelo desatualizado (editado) disparando
  `CalcularEspacial` dentro do F5 (o caminho é o mesmo de `CriarProjetistaGrupo`).
