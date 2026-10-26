# Arquivos do Estructto: .pstr, .pcon e reabertura do último

Data: 23/10/2026 (branch `wt-arquivos`). Pedido do usuário: (1) o programa abria sempre num arquivo novo; (2) uma "calculadora de conexão" como arquivo próprio,
com extensões novas (".estructto é muito ruim"), como o Inventor separa peça e montagem, e esse arquivo de conexão pode ser aberto dentro de uma ligação de um arquivo normal.

## Extensões (escolha do usuário)
| Extensão | O que é | Formato |
|---|---|---|
| `.pstr` | modelo (o "arquivo normal") | o mesmo contêiner do `.estructto` v3 (modelo do motor + placas + grade); salvar grava `.pstr` por padrão |
| `.pcon` | conexão (calculadora de ligação, sem modelo) | JSON `{"formato":"estructto-conexao","versao":1,"nome","tipo","dados","esforcos"}` (`ConexaoArquivo.cs`) |
| `.estructto`, `.portico`, `.ftl`, `.json`, `.ed3` | continuam abrindo como antes | — |

## Ao abrir o programa
Abre na **tela inicial** (Novo projeto, **Nova conexão**, Abrir, Importar, Recentes), sem arquivo novo e sem reabrir o último (pedido de 23/10/2026:
"não queria um arquivo novo" e depois "não quero que abra o último que mexi"). Antes abria `NovoEmBranco` (MainWindow.xaml.cs, construtor).
Com um arquivo na linha de comando (duplo clique em `.pstr`/`.pcon`), abre esse arquivo.

## Calculadora de conexão
- Criada pela tela inicial (**Nova conexão**) ou pelo Arquivo. Janela: o próprio projetista de ligações (`JanelaLigacaoProjeto`), sem grupo = calculadora isolada. Rodapé: **Abrir .pcon** e **Salvar .pcon** (num grupo de ligações do modelo o botão vira **Importar .pcon**) (só com tipo escolhido).
- A verificação não é gravada: o motor refaz ao abrir. O arquivo guarda só a entrada (tipo, dados, esforços).
- **Dentro de um modelo:** abrindo um `.pcon` no projetista de um grupo (gerenciador de ligações), tipo/dados/esforços preenchem aquela ligação; "Aplicar ao grupo" grava. É a "importação" pedida.
- Abrir/duplo clique num `.pcon` pelo Abrir do programa abre a calculadora (`AbrirTarefa` desvia antes de tratar como modelo). `.pcon` não entra em Recentes.
- Tipo desconhecido, versão futura ou JSON inválido: mensagem no rodapé, nada é alterado.

## Testes
`TestesFluxo.cs` > Fluxo E: 6 conferências do `.pcon` (ida e volta, 4 arquivos inválidos, extensão). Rodar: `Estructto.exe --testes-fluxo <pasta> E` (precisa do motor).
Fluxo E em 23/10: 36 ok, 0 falhas. Abertura conferida: sem argumentos o título é "Sem título" e a tela inicial mostra Nova conexão.

## Pendente
- Ícone próprio para `.pstr`/`.pcon` (hoje usam o ícone do exe). A associação (duplo clique) já está em `codigo/instalador.iss` ([Registry], HKCU); só vale depois de gerar um instalador novo (`publicar.ps1`).
- Teste automático da interface de Abrir/Salvar `.pcon` (os diálogos de arquivo são do Windows).
- Migração de modelos existentes para `.pstr` (não obrigatória: `.estructto` segue válido).
