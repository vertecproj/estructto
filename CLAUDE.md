# Como trabalhamos no Estructto (leia antes de qualquer tarefa)

Projeto do Gabriel e do Bruno, em máquinas diferentes: a memória do Claude NÃO é compartilhada — tudo que importa fica aqui e em `docs/`.
Visão geral, pastas e comando de publicar: [README.md](README.md). O que mudou e quando: [docs/CHANGELOG.md](docs/CHANGELOG.md).

## Regras
- Toda decisão, critério ou mudança vai para um `.md` em `docs/` e uma linha no CHANGELOG.
- Fazer, não pedir: o usuário quer o mínimo de esforço. Decisões técnicas/de segurança são dele (perguntar uma vez, com opções).
- **Nunca pedir acesso à tela** (computer-use) nem ficar pedindo permissões. Para ver telas, usar as capturas (abaixo).
- Quando ele pedir algo visual ("me dê opções"), mostrar telas REAIS/fiéis (renderizadas no tamanho e nas cores do programa), não esboços.
- Cálculo (motor `codigo/portico/src/Nucleo.cs`) só muda com critério documentado e teste novo; os testes nunca podem quebrar.

## Ciclo de cada mudança
1. Editar o código (`codigo/Estructto` = interface WPF .NET 8; `codigo/portico` = motor + testes).
   Arquivos com acento: ferramenta Edit/Write (sed/PowerShell corrompem UTF-8).
2. Build: `cd codigo\Estructto; dotnet build -c Release` (saída em `C:\dev\build\EstructtoV2`, fora do Drive).
3. Testes do motor: `cd codigo\portico; .\compilar.ps1 -Testes` (hoje 200 passam).
4. Conferência visual sem abrir o programa: `powershell -ExecutionPolicy Bypass -File codigo\Estructto\capturar.ps1`
   → PNG de todas as telas e diálogos em `C:\dev\build\EstructtoV2\capturas` (`Estructto.exe --capturas <pasta>`); revisar com o
   checklist de [docs/v2/GUIA-DESIGN.md](docs/v2/GUIA-DESIGN.md). Tela nova = acrescentar a captura dela em `Capturas.cs`.
5. Exe: comando `dotnet publish` do README → copiar para `ftool\Estructto.exe`.
6. Commit local com a linha `Co-Authored-By` do ambiente. **Push/Release só quando o usuário pedir.**

## Git e GitHub
- Repositório: `ftool/` ↔ https://github.com/vertecproj/estructto (público, conta `vertecproj`, branch `main`). Identidade do repo já configurada.
- Fora do git (ver `.gitignore`): `referencias/` (PDFs com direito autoral), `_terceiros/`, `*.exe`, `docs/v2/refs/`.
- Google Drive cria `desktop.ini` dentro de `.git` e quebra o git: antes de push, `Get-ChildItem .git -Recurse -Force -Filter desktop.ini | Remove-Item -Force`.
- Push: `git push origin main` (login guardado no Gerenciador de Credenciais; se pedir, `$env:GCM_INTERACTIVE='always'` e o usuário loga uma vez).
- Exe vai na aba **Releases** (70 MB; o navegador só aceita 10 MB): criar a release e subir o asset pela API do GitHub com o token do
  Git Credential Manager (`git credential-manager get`). v0.1 = interface antiga; v0.2 = interface modernizada.

## Ajuda para poupar tokens
- **agy (Antigravity/Gemini)**: delegar o grosso com briefing fechado — [docs/DELEGAR-AGY.md](docs/DELEGAR-AGY.md). Sem cota até ~26/10/2026.
- **Subagentes do Claude** (ferramenta Agent, modelo sonnet; opus para o que for crítico): um por vez nos mesmos arquivos; briefing com regras,
  arquivos, o que não mexer e como provar (build + testes + capturas + commit local). Exemplos de briefing: `docs/v2/v2-E*.txt`.
- Eu (Claude principal) planejo, reviso o diff/capturas, testo e entrego.

## Design (interface)
- Regras e tokens: [docs/v2/GUIA-DESIGN.md](docs/v2/GUIA-DESIGN.md) (cores só em `TemaPlant.cs`; ícones em `Icones.cs` e `IconesTabler.cs`).
- Referências: [docs/v2/PESQUISA.md](docs/v2/PESQUISA.md); skills de design baixadas em `docs/v2/refs/` (fora do git; baixar de novo com
  `git clone` de Leonxlnx/taste-skill, vercel-labs/agent-skills, VoltAgent/awesome-design-md, anthropics/skills, wshaddix/dotnet-skills,
  jcurbelo/skills, nextlevelbuilder/ui-ux-pro-max-skill — achadas com `npx skills find`).
- História da modernização e decisões de produto: [docs/v2/RELATORIO.md](docs/v2/RELATORIO.md) e [docs/v2/STATUS.md](docs/v2/STATUS.md).
- Logo: `docs/logo-estructto.svg` (pórtico); ícone do exe gerado dele em 7 tamanhos (`codigo/Estructto/estructto.ico`).

## Trabalho longo sem o usuário (madrugada)
Arquivo de estado com checklist e "próximo passo exato" + arquivo `LOCK` com a hora (tocar a cada ~15 min) + tarefa agendada que retoma se o
trabalho parar por limite de uso. Modelo: `docs/v2/STATUS.md`.
