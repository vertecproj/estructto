# Estructto

Programa de cálculo estrutural: interface WPF (.NET 8) em `codigo/Estructto` + motor Python em `codigo/motor`
(pórtico espacial 3D, NBR 8800, 14762, 6118, 6123, 15421, 6120, ligações, importação `.ed3` do CYPE e servidor MCP).
Unificação e fases: [docs/UNIFICACAO.md](docs/UNIFICACAO.md). Nasceu no projeto da **coluna para guincho CSM 400 kgf**
(UNIVALI); a planilha `ELEMENTOS DE MÁQUINA\PLANILHA DE CÁLCULO.xlsx` (OneDrive) tem de dar o mesmo resultado.

**Regra do projeto:** toda decisão, critério ou mudança vai para um `.md` aqui (e uma linha no [CHANGELOG](docs/CHANGELOG.md)).
Gabriel e Bruno trabalham em máquinas diferentes; a memória do Claude não é compartilhada.

## Pastas

| Caminho | O que é | Pode mover? |
|---|---|---|
| `Estructto.exe` | programa pronto (arquivo único, não precisa de .NET instalado) | não |
| `codigo/Estructto/` | interface WPF do Estructto (C#, .NET 8) | não |
| `codigo/motor/` | motor Python (`prossen_estrutural`, nome interno do pacote), testes, exemplos e `empacotar.ps1` | não |
| `codigo/.mcp.json` | servidor MCP `estructto` para clientes de IA | não |
| `codigo/portico/` | motor de cálculo (`src/Nucleo.cs`), testes, tabelas embutidas (`perfis-*.csv`, `chumbadores.csv`) e scripts | não |
| `exemplos/` | modelos `.estructto` / `.ftl` de teste e PNGs de memorial | não (citados nos docs) |
| `referencias/` | PDFs das aulas e tabelas | sim |
| `_terceiros/` | Ftool original e material de terceiros | não |
| `docs/` | documentação do projeto (abaixo) | — |

## Documentação

- [docs/CHUMBADORES.md](docs/CHUMBADORES.md) — catálogo único de chumbadores (Walsywa mecânico/químico + Indufix) e critério.
- [docs/FIXACOES-GUIA.md](docs/FIXACOES-GUIA.md) — guia de escolha de fixações (mecânico, químico): uso recomendado e limitações.
- [docs/TAREFA-COMPARAR-FIXACOES.md](docs/TAREFA-COMPARAR-FIXACOES.md) — a fazer: função "Comparar fixações" no Estructto.
- [docs/PADRAO-MEMORIAL.md](docs/PADRAO-MEMORIAL.md) — como o memorial de cálculo é escrito (Estructto e planilha).
- [CLAUDE.md](CLAUDE.md) — **como trabalhamos**: ciclo de cada mudança, capturas de tela, git/GitHub/Releases, agy e subagentes, design.
- [docs/v2/](docs/v2/) — modernização da interface (relatório, guia de design, pesquisa, decisões).
- [docs/DELEGAR-AGY.md](docs/DELEGAR-AGY.md) — fluxo Claude + Antigravity (agy) para poupar tokens.
- [docs/CHANGELOG.md](docs/CHANGELOG.md) — o que mudou, quando e o que está pendente.
- `docs/historico/` — auditorias, diffs e prompts já executados (só consulta).

## Comandos

```
cd codigo\motor; $env:PROSSEN_PULAR_LENTOS=1; python -m unittest discover -s testes   # testes do motor Python (sem a variável: inclui os lentos)
cd codigo\Estructto; dotnet build -c Release                                          # interface (0 erros)
codigo\portico\compilar.ps1 -Testes        # testes do motor (todos têm de passar)
codigo\portico\compilar.ps1                # Portico.exe (versão simples, WinForms)
codigo\portico\sincronizar-planilha.ps1 -Planilha "<caminho>\PLANILHA DE CÁLCULO.xlsx"   # aba CHUMBADORES = CSV
```

Estructto.exe (precisa do .NET 8 SDK; bin/obj ficam em `C:\dev\build\EstructtoV2`, fora do Drive):

```
dotnet publish codigo\Estructto\Estructto.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o C:\dev\build\EstructtoV2\publish
```

Antes do publish, empacotar o motor Python (precisa de python com numpy e pyinstaller). O publish copia a pasta gerada para
`publish\motor\`, ao lado do `Estructto.exe`; sem ela o programa só tem o motor interno (ou o da variável `ESTRUCTTO_MOTOR`):

```
codigo\motor\empacotar.ps1 -Motor <pasta que contém prossen_estrutural>   # gera codigo\motor\dist\estructto-motor.exe
```

Depois copiar `C:\dev\build\EstructtoV2\publish\Estructto.exe` **e a pasta `motor\`** para esta pasta (feche o programa antes).
Em desenvolvimento, sem empacotar: `$env:ESTRUCTTO_MOTOR = "<pasta que contém prossen_estrutural>"` (roda `python -m prossen_estrutural.web`).

Conferência visual sem abrir o programa: `codigo\Estructto\capturar.ps1` gera PNG de todas as telas em `C:\dev\build\EstructtoV2\capturas`
(`Estructto.exe --capturas <pasta>`). Regras de interface: `docs\v2\GUIA-DESIGN.md`; histórico da modernização: `docs\v2\RELATORIO.md`.

## MCP (uso por IA)

`codigo/.mcp.json` registra o servidor `estructto` (stdio, `python -m prossen_estrutural.servidor_mcp`, `PYTHONPATH=motor`).
Abra o cliente MCP (ex.: Claude Code) na pasta `codigo`, ou rode à mão: `cd codigo; $env:PYTHONPATH="motor"; python -m prossen_estrutural.servidor_mcp --pasta <trabalho>`.
Precisa de python com `numpy` e `mcp`. Arquivos só dentro da pasta de trabalho; todo resultado exige verificação por engenheiro responsável.
