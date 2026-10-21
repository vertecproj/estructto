# Estructto v3 — entrega (21/10/2026)

Produto unificado: interface WPF do Estructto + motor Python (ex-PROSSEN Estrutural), **só 3D**, no fluxo do AutoCAD Plant 3D.
Branch `unificacao`. Detalhes de cada parte nos `.md` citados.

## O que há de novo
| Área | Resumo | Doc |
|---|---|---|
| Só 3D | Todo documento é espacial; `.estructto` antigo, `.ftl`, `.portico` convertem ao abrir (sem sobrescrever). Shift+arrastar orbita sem cancelar comando, ViewCube, perspectiva, grips (Ctrl copia), linha de comando com aliases do AutoCAD, PgUp/PgDn troca nível | `MODELO-ESPACIAL.md` |
| Modelar do zero | Novo projeto em 3 passos (modelo, dados, grade `3x6 2x5` + níveis), grade estrutural com bolhas, níveis/planos, Barra/Pilar com snap, edição em lote (mover, copiar, espelhar, rotacionar, matriz, dividir, aparar/estender), cargas pontual/trapezoidal/área | `INICIO.md`, `MODELO-ESPACIAL.md` |
| Snap e medir | OSNAP completo do AutoCAD (fim, meio, centro, nó, interseção, aparente, perpendicular, extensão, paralelo, mais próximo), rastreamento, polar, orto, F3/F8/F10/F11, Shift+botão direito; Medir CAD+Inventor com acumulado | `SNAP-MEDIR.md` |
| Ligações | 19 tipos (flexíveis, rígidas/emendas, contraventamento/treliça) + placa de base, NBR 8800 cap. 6, exemplos dos livros reproduzidos; projetista em 3 passos com desenho ao vivo, Sugerir, Otimizar; **grupos**: identifica ligações iguais, dimensiona pela pior, aplica ao grupo | `ligacoes/IMPL-*.md`, `LIGACOES-UI.md`, `LIGACOES-GRUPOS.md` |
| Placa de base v2 | Método NBR com bloco de compressão, CBCA + Pfeil, ACI 318 cap. 17, pior combinação ELU, biaxial; verificada de forma independente (2 rodadas) | `PLACA-BASE-V2.md` |
| Treliças e contraventos | Pratt, Howe, Warren, K, Fink, banzos paralelos/água/arco, em série com terças; contraventos X, K, V, Λ, mão-francesa; terças e tirantes | `TRELICAS.md` |
| Realista | DirectX 11 (HelixToolkit.SharpDX): sombras, SSAO, MSAA, reflexo metálico, recortes nas juntas, chapas/parafusos/soldas das ligações projetadas; recai no nativo sem GPU | `RENDER.md` |
| Exportar | DWG/DXF (ACadSharp, malhas por barra, camadas por seção) e STEP AP214 (sólidos por barra) | `EXPORTAR.md` |
| Motor | molas e recalques (caso REC com γε da NBR 8681), carga linear, esbeltez de tração sem K (5.2.8.1), pior esbeltez entre combinações | `UNIFICACAO-MOTOR.md` |
| Visual | logo nova, família única de ícones, tokens refinados, janelas unificadas, tela inicial e menu Arquivo novos, Opções | `LOGO.md`, `DESIGN-V3.md`, `design/*.png` |

## ⚠ Segurança — leia
- O método antigo da placa de base (C#, `Nucleo.cs`, usado no projeto da coluna do guincho e na planilha) gira a placa na borda e **subestima a tração
  nos chumbadores (até ~3,3×)**. O programa agora usa o método NBR do motor; o legado só aparece como comparação. **Revise os chumbadores do guincho.**
- Itens de norma a conferir no texto oficial (marcados no código): NBR 8681 tab. 3 (γε recalque), NBR 8800 5.7.x (painel/enrijecedores),
  6.1 (mínimo 45 kN), folgas de furo alargado; perfil Ue reprovando na distorcional com fórmula simplificada.

## Testes (última rodada)
- Motor Python: suíte completa (`python -m unittest discover -s testes`, `PROSSEN_PULAR_LENTOS=1`).
- C#: 200 testes do núcleo; `--testes-edicao`, `--testes-snap`, `--testes-exportar` passam.
- Capturas automáticas de todas as telas sem erro.

## Pendências conhecidas
- Suíte de fluxo ponta a ponta (`--testes-fluxo`) ficou **parcial** (branch `wt-e2e`, commit c76b797): o agente parou no limite semanal. Retomar e
  rodar antes de considerar o produto validado por fluxo completo.
- Ver "Limites" em cada doc (ex.: RO só em torno do eixo vertical, solda sem esquadria no canto, DWG sem 3DSOLID, sem barra só de tração).

## Integração com o `main` do Bruno (feita em 21/10/2026, branch `wt-bruno`: ver [INTEGRACAO-BRUNO.md](INTEGRACAO-BRUNO.md))
O `origin/main` recebeu 6 commits do Bruno (1f719d3) no programa 2D: vento NBR 6123:2023 nas barras (V0, S1, S2, S3, Cf/Ca, K) com casos,
vento NBR 8400:1984 (seletor de norma, combinações I/II/III), e melhorias na placa legada (pior combinação por item, n da solda, troca de
chumbador por família). Eles conflitam com o produto só-3D (Acoes.cs, Capturas.cs, Casos.cs, JanelaPlaca.cs, Paleta.cs) e trazem cálculo
novo em `Nucleo.cs` que precisa ser **portado para o motor Python** (vento por barra e NBR 8400) para existir no 3D.
Por isso a v3 foi publicada no branch `unificacao` (pré-release), **sem sobrescrever o `main`**. Próximo passo: portar esses recursos para o
motor e as janelas 3D de Vento, depois juntar `origin/main` e só então avançar o `main`.
