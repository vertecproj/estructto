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
