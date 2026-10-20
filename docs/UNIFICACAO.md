# Unificação Estructto + PROSSEN Estrutural

Decidido em 20/10/2026. O produto final se chama **Estructto**.

## Decisões

| Item | Decisão |
|---|---|
| Nome | Estructto (o nome PROSSEN Estrutural deixa de existir no produto) |
| Interface | WPF .NET 8 do Estructto (`codigo/Estructto`). A interface web do PROSSEN é descartada. |
| Motor de cálculo | Python do PROSSEN, em `codigo/motor/prossen_estrutural` (pórtico espacial 3D, NBR 8800, 14762, 6118, 6123, 15421, 6120, ligações, importação `.ed3` do CYPE, servidor MCP). |
| Ponte interface ↔ motor | WPF sobe o servidor local do motor (`web/servidor.py`, só 127.0.0.1) e chama as rotas `/api/...` com JSON. Mesmas funções de `projeto.py` que o MCP usa. |
| Repositório | `vertecproj/estructto`, branch `unificacao` até ficar pronto. |
| Referência de cálculo | CYPE 2016 (DLLs descompiladas com o REA) como guia de lógica e de interface; nunca copiar código. |

## Fases

1. **Motor no repositório** — copiar o motor e os testes para `codigo/motor/`; testes passando sem a interface web.
2. **Ponte** — cliente HTTP no WPF; motor empacotado (PyInstaller) junto do exe; iniciar e encerrar o servidor com o programa.
3. **Modelo** — o `Modelo` do WPF passa a espelhar o JSON do motor (nós, barras, seções, materiais, casos, cargas, pavimentos). Conversor `.estructto`/`.ftl` → JSON para abrir arquivos antigos.
4. **Vista 3D** — pórtico espacial no WPF (HelixToolkit ou `Viewport3D`), mantendo a vista 2D atual como planta/elevação.
5. **Telas novas** — verificações por norma (aço, PFF, concreto), vento, sismo, combinações, estabilidade (γz), importação do CYPE, relatório do motor.
6. **Placa de base, chumbadores e solda** — portar de `Nucleo.cs` para Python com os testes; depois aposentar `Nucleo.cs`. Exemplos `guincho-*` têm de bater com o Estructto atual e com a planilha.
7. **Revisão de abas e telas** — arquitetura de informação coerente com o fluxo do engenheiro (Modelo → Ações → Análise → Dimensionamento → Ligações/Bases → Relatório). Proposta em `docs/REVISAO-TELAS.md`.
8. **Polimento final de animações** — transições, entrada de painéis, realce de seleção, câmera 3D suave, diagramas animados, em toda a interface, seguindo `docs/v2/GUIA-DESIGN.md`.

## Regras

- Motor só muda com critério documentado e teste novo; os testes nunca podem quebrar.
- Cada fase termina com build, testes, capturas (`capturar.ps1`) e commit local. Push só quando pedirem.
