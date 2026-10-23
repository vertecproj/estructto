# Arquivos do Estructto: .pstr, .pcon e reabertura do último

Data: 23/10/2026 (branch `wt-arquivos`). Pedido do usuário: (1) o programa abria sempre num arquivo novo; (2) uma "calculadora de conexão" como arquivo próprio,
com extensões novas (".estructto é muito ruim"), como o Inventor separa peça e montagem, e esse arquivo de conexão pode ser aberto dentro de uma ligação de um arquivo normal.

## Extensões (escolha do usuário)
| Extensão | O que é | Formato |
|---|---|---|
| `.pstr` | modelo (o "arquivo normal") | o mesmo contêiner do `.estructto` v3 (modelo do motor + placas + grade); salvar grava `.pstr` por padrão |
| `.pcon` | conexão (calculadora de ligação, sem modelo) | JSON `{"formato":"estructto-conexao","versao":1,"nome","tipo","dados","esforcos"}` (`ConexaoArquivo.cs`) |
| `.estructto`, `.portico`, `.ftl`, `.json`, `.ed3` | continuam abrindo como antes | — |

## Reabrir o último arquivo
`MainWindow.xaml.cs` (construtor): sem arquivo na linha de comando e sem `--` (execução automática), abre o primeiro de `Config.Recentes()` que ainda existe;
só abre em branco se não houver nenhum. Antes abria sempre `NovoEmBranco`.

## Calculadora de conexão
- Janela: o próprio projetista de ligações (`JanelaLigacaoProjeto`), sem grupo = calculadora isolada. Rodapé: **Abrir .pcon** e **Salvar .pcon** (só com tipo escolhido).
- A verificação não é gravada: o motor refaz ao abrir. O arquivo guarda só a entrada (tipo, dados, esforços).
- **Dentro de um modelo:** abrindo um `.pcon` no projetista de um grupo (gerenciador de ligações), tipo/dados/esforços preenchem aquela ligação; "Aplicar ao grupo" grava. É a "importação" pedida.
- Abrir/duplo clique num `.pcon` pelo Abrir do programa abre a calculadora (`AbrirTarefa` desvia antes de tratar como modelo). `.pcon` não entra em Recentes.
- Tipo desconhecido, versão futura ou JSON inválido: mensagem no rodapé, nada é alterado.

## Testes
`TestesFluxo.cs` > Fluxo E: 6 conferências do `.pcon` (ida e volta, 4 arquivos inválidos, extensão). Rodar: `Estructto.exe --testes-fluxo <pasta> E` (precisa do motor).
Fluxo E em 23/10: 36 ok, 0 falhas. Reabertura conferida abrindo o programa: o título veio do primeiro recente, não de documento novo.

## Pendente
- Ícone próprio para `.pstr`/`.pcon` (hoje usam o ícone do exe). A associação (duplo clique) já está em `codigo/instalador.iss` ([Registry], HKCU); só vale depois de gerar um instalador novo (`publicar.ps1`).
- Teste automático da interface de Abrir/Salvar `.pcon` (os diálogos de arquivo são do Windows).
- Migração de modelos existentes para `.pstr` (não obrigatória: `.estructto` segue válido).
