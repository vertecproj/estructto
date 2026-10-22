# Atualização automática do Estructto

Pedido de 21/10/2026: avisar dentro do programa quando houver versão nova, com botão **Instalar** que baixa e instala sozinho.
Código: `codigo/Estructto/Atualizador.cs` (lógica), `AtualizadorUi.cs` (faixa e fluxo), `TestesAtualizacao.cs` (auto-teste).

## Como funciona
1. **Checagem**: ao abrir, em segundo plano, depois da janela aparecer (não atrasa a tela inicial). `GET api.github.com/repos/vertecproj/estructto/releases`,
   timeout de 5 s, falha silenciosa (só `%AppData%\NovoFtool\atualizacao.log`). Escolhe a maior versão semântica maior que a instalada;
   pré-release só aparece para quem já está em pré-release. Rascunhos e tags que não são versão são ignorados.
2. **Versão instalada** = `AssemblyInformationalVersion` do exe = tag da release (`<Version>` no `.csproj`; `publicar.ps1 -Versao X` passa `-p:Version=X`).
   `0.5 < 0.10`, `0.5-beta.1 < 0.5`.
3. **Aviso**: faixa não modal no topo da janela principal: "Nova versão vX disponível — **Instalar** · **Notas** · **Depois**".
   *Depois* esconde até sair uma versão mais nova (`atualizacao_ignorada` em `opcoes.txt`); *Notas* abre a página da release.
4. **Instalar**: baixa o `.zip` da release com barra de progresso e *Cancelar*, baixa `<zip>.sha256` e confere o SHA-256.
   Sem `.sha256`, vazio, inválido, de outro arquivo ou diferente do hash real: **não instala** e mostra o erro.
   Só então extrai em `%TEMP%\Estructto-update\v<versão>\pacote`, pergunta sobre documentos não salvos (a mesma pergunta de fechar o programa;
   cancelar adia a atualização), dispara o script atualizador e fecha o Estructto.
5. **Script** (PowerShell, `%TEMP%\Estructto-update\atualizar-*.ps1`, `Atualizador.ScriptAtualizador`): espera o PID do Estructto terminar (até 120 s;
   se não fechar, não mexe em nada), encerra `estructto-motor` da pasta, faz backup `<pasta>.bak`, apaga `motor\` antigo e copia o pacote novo por cima,
   confere `motor\estructto-motor.exe` e reabre o Estructto. Qualquer erro: restaura o backup e reabre a versão antiga. O log fica em `%TEMP%\Estructto-update\atualizador.log`.
   Configurações do usuário (`%AppData%\NovoFtool`) ficam fora da pasta de instalação e nunca são tocadas; arquivos extras do usuário na pasta são mantidos.
6. **Sem permissão de escrita** (ex.: Program Files): o script se reabre com `Start-Process -Verb RunAs` (UAC) só nesse caso; elevado, reabre o programa
   pelo `explorer.exe` para ele não rodar como administrador. Se o UAC for recusado, nada muda e a versão antiga reabre.

O `.bak` fica ao lado da pasta até a próxima atualização (permite voltar à versão anterior à mão).

## Onde o usuário controla
Arquivo › Opções › **Atualizações**: caixa "Procurar atualizações ao iniciar" (padrão ligada) e botão "Procurar agora".
Arquivo › **Procurar atualizações** faz o mesmo. A busca manual ignora o "Depois" e avisa "já está na versão mais recente" ou que não conseguiu consultar.
Fora do pacote publicado (sem `motor\` ao lado do exe, como em `dotnet build`) o Instalar recusa, para não sobrescrever a pasta de build.

## Segurança
- Nada é instalado nem executado sem SHA-256 conferido; do pacote só se copiam arquivos (o script é nosso, gerado pelo programa).
- Download só de `https` em `github.com`, `api.github.com` e `*.githubusercontent.com` (inclusive após redirecionamento); a URL vinda do JSON não escolhe outro host.
- `ZipFile.ExtractToDirectory` recusa entradas fora da pasta (zip-slip); o pacote tem de conter `Estructto.exe` e `motor\estructto-motor.exe`.
- Limite conhecido: o hash vem da mesma release; protege contra download corrompido/adulterado em trânsito, não contra conta do GitHub comprometida
  (assinatura de código seria o passo seguinte).

## Publicar (publicar.ps1)
`codigo\publicar.ps1 -Versao 0.5` gera `Estructto-0.5-win-x64.zip` **e** `Estructto-0.5-win-x64.zip.sha256` (formato sha256sum). Os dois são assets da release `v0.5`.
`-Enviar` faz `gh release upload` dos dois (a release precisa existir); sem `-Enviar` nada vai para a rede. A tag tem de ser `v<Versao>`.

## Testes
`Estructto.exe --testes-atualizacao <pasta>` (sem rede, `TODOS PASSARAM`, saída 0): versões, parse do JSON de exemplo, hash (correto, errado, ausente, formatos),
zip (válido, sem motor, zip-slip), conteúdo do script e **execução real do script** em pastas temporárias (instalação e restauração após pacote incompleto), e a faixa
(aparece com versão maior, não aparece com igual, "Depois", busca manual). Captura `atualizacao-faixa.png` (também em `--capturas`).
Não testado automaticamente: elevação (UAC) e o download real da release.
