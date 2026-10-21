# Fluxo de trabalho: Claude coordena, Antigravity (agy) executa

Vale para o Gabriel e para o Bruno: os dois têm o CLI do Antigravity (`agy`) instalado.
Objetivo: **poupar tokens do Claude**. O Claude planeja, delega ao agy, confere o resultado e entrega.

## Regra
1. O usuário pede a tarefa ao Claude.
2. O Claude passa ao agy **tudo que for válido delegar**, mesmo tarefas leves:
   - processamento bruto: ler/extrair PDFs e catálogos, varrer muitos arquivos, transcrever tabelas;
   - gerar ou alterar código seguindo uma especificação fechada;
   - pesquisas longas, rascunhos, conversões, scripts repetitivos.
3. O Claude fica só com: entender o pedido, decidir o critério de engenharia, escrever o briefing,
   **conferir o resultado** e entregar. Decisões técnicas e de segurança são do usuário (o Claude pergunta).
4. Nada do agy é entregue sem conferência: o Claude lê o diff, roda testes/compila, e confere números
   contra a fonte (ex.: página do PDF renderizada como imagem). Se estiver errado, corrige ou manda refazer.
5. Se explicar + revisar custar mais que fazer (uma linha, uma conta rápida), o Claude faz direto.

## Como chamar o agy
- Executável: `agy` (no PATH; no PC do Gabriel em `C:\Users\Gabriel\AppData\Local\agy\bin\agy.exe`).
- Tarefa única, sem perguntas (os dois usuários autorizaram liberar as permissões):
  ```
  agy --dangerously-skip-permissions --add-dir "<pasta da tarefa>" -p "<briefing>"
  ```
  - `--add-dir` limita o agy às pastas da tarefa (repita para cada pasta).
  - `-c` continua a última conversa; `--conversation <id>` retoma uma específica.
  - `--output-format json` quando a saída for lida por script.
- Tarefas longas: rodar em segundo plano e esperar o aviso de término (não ficar consultando).
- Briefing bom (em arquivo `.txt`, passado com `-p "$(cat brief.txt)"`):
  objetivo; arquivos/pastas exatos; formato de saída (ex.: CSV com colunas fixas); o que **não** fazer
  ("não invente valor", "não mexa em outros arquivos"); como provar (ex.: rodar `compilar.ps1 -Testes`);
  e pedir no fim um resumo curto (arquivos alterados, contagens, resultado dos testes).
- Antes de deixar o agy mexer em código: backup da pasta (cópia na scratchpad) para poder comparar com `diff`.

## PC do Bruno (20/10/2026)
- `agy` em `C:\Users\Bruno Nietsche\AppData\Local\agy\bin\agy.exe` (fora do PATH do terminal do Claude).
- `--dangerously-skip-permissions` é **bloqueado** pelo classificador do Claude Code. No lugar, o Bruno escolheu liberar só a pasta:
  `C:\Users\Bruno Nietsche\.gemini\antigravity-cli\settings.json` → `permissions.allow` com `read_file(...ftool\**)` e
  `write_file(...ftool\**)`. Sem regra de comando: compilar e testar fica com o Claude. A escrita com essa regra ainda não foi
  testada até o fim (o Bruno pediu para o Claude finalizar a tarefa direto).

## Cuidados aprendidos
- Saída de PDF por texto pode desalinhar colunas de tabela: conferir com a página renderizada (`pdftoppm -png`).
- CSV do agy pode ter `;` dentro de campos de texto: validar antes de usar.
- O agy pode não ter o ambiente completo (ex.: faltava o .NET SDK): conferir o que ele **não** conseguiu rodar.
- Cada usuário usa as próprias contas (GitHub, Supabase, Google): conferir de quem é a máquina antes de agir.

## Exemplo real (19/10/2026, catálogo de chumbadores)
- agy: extraiu os catálogos Walsywa (mecânico e químico) para CSV e implementou o catálogo no Estructto.
- Claude: conferiu as tabelas contra as páginas do PDF, decidiu o critério com o usuário, revisou o diff,
  corrigiu a classe da barra, escreveu testes cruzados planilha × Estructto e ajustou a planilha.
