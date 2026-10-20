# ftool — Estructto (Gabriel + Bruno)

Programa de pórtico plano (clone do Ftool) com verificação e memorial, usado no projeto da **coluna para guincho CSM 400 kgf**
(Elementos de Máquina I / Mecânica dos Sólidos, UNIVALI). A planilha de cálculo fica no OneDrive:
`ELEMENTOS DE MÁQUINA\PLANILHA DE CÁLCULO.xlsx`; os dois têm de dar o mesmo resultado.

**Regra do projeto:** toda decisão, critério ou mudança vai para um `.md` aqui (e uma linha no [CHANGELOG](docs/CHANGELOG.md)).
Gabriel e Bruno trabalham em máquinas diferentes; a memória do Claude não é compartilhada.

## Pastas

| Caminho | O que é | Pode mover? |
|---|---|---|
| `Estructto.exe` | programa pronto (arquivo único, não precisa de .NET instalado) | não |
| `codigo/Estructto/` | interface WPF do Estructto (C#, .NET 8) | não |
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
codigo\portico\compilar.ps1 -Testes        # testes do motor (todos têm de passar)
codigo\portico\compilar.ps1                # Portico.exe (versão simples, WinForms)
codigo\portico\sincronizar-planilha.ps1 -Planilha "<caminho>\PLANILHA DE CÁLCULO.xlsx"   # aba CHUMBADORES = CSV
```

Estructto.exe (precisa do .NET 8 SDK; bin/obj ficam em `C:\dev\build\EstructtoV2`, fora do Drive):

```
dotnet publish codigo\Estructto\Estructto.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o C:\dev\build\EstructtoV2\publish
```

Depois copiar `C:\dev\build\EstructtoV2\publish\Estructto.exe` para esta pasta (feche o programa antes).

Conferência visual sem abrir o programa: `codigo\Estructto\capturar.ps1` gera PNG de todas as telas em `C:\dev\build\EstructtoV2\capturas`
(`Estructto.exe --capturas <pasta>`). Regras de interface: `docs\v2\GUIA-DESIGN.md`; histórico da modernização: `docs\v2\RELATORIO.md`.
