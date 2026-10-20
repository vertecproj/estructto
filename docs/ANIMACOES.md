# Animações da interface (`Anim.cs`)

Um único helper, `codigo/Estructto/Anim.cs`, sem biblioteca nova. Regra do [GUIA-DESIGN](v2/GUIA-DESIGN.md): movimento discreto
(ferramenta de engenharia), só `Opacity`, `RenderTransform` e cor. Nada bloqueia entrada e nenhuma animação atrasa resultado de cálculo:
o valor final é gravado antes de a animação começar.

## Tokens

| Token | Valor | Uso |
|---|---|---|
| `Anim.Rapido` | 120 ms | hover, dicas, realce de seleção, troca de aba, painéis |
| `Anim.Medio` | 160 ms | diálogos, paleta, toasts, itens da tela inicial |
| `Anim.Espacial` | 250 ms | diagramas crescendo na vista |
| `Anim.Lento` | 400 ms | contagem de números na barra de status (teto de qualquer movimento) |
| `Anim.Passo` / `Anim.PassoMax` | 30 / 300 ms | atraso por item no escalonamento |
| `Anim.Saida` | `CubicEase EaseOut` | padrão de tudo |
| `Anim.EntradaSaida` | `CubicEase EaseInOut` | brilho de progresso |

## Desligar

`Anim.Ligado` = `SystemParameters.ClientAreaAnimation` (Windows › Acessibilidade › Efeitos visuais › Efeitos de animação) **e** fora do
modo `--capturas`. Desligado, toda chamada só grava o estado final. Toda animação usa `FillBehavior.Stop` com o valor final como base,
então as capturas (estáticas) sempre saem no estado final.

## API

| Chamada | Efeito |
|---|---|
| `Anim.Abrir(janela)` | conteúdo da janela entra com fade + escala 0,98 → 1 (160 ms) |
| `Anim.Surgir(elemento)` | o mesmo para popup/flyout |
| `Anim.Entrar(el, dy, ms, atraso)` | fade-in + deslize vertical `dy` → 0 |
| `Anim.AoMostrar(el, dy, ms)` | `Entrar` toda vez que o elemento ficar visível |
| `Anim.Escalonar(painel)` | filhos entram em sequência (30 ms cada, até 300 ms) |
| `Anim.Fundo(border, pincel)` | troca de fundo com transição a partir do transparente |
| `Anim.Contar(textBlock)` | números com casas decimais do texto contam de 0 até o valor (400 ms) |
| `Anim.Brilho(larg, alt)` | trilho de progresso indeterminado com brilho de acento |
| `Anim.Valor(obj, prop, de, ate, ms)` | primitiva: qualquer `double` animável (ex.: propriedade de render) |
| `Anim.Iniciar(raízes…)` | uma vez: dicas com fade e hover/aperto dos botões dentro das raízes |

## Onde está aplicado

| Lugar | Código | Animação |
|---|---|---|
| Faixa — troca de aba | `MainWindow.xaml.cs` (`SelectedTabChanged`) | grupos da aba nova com fade 120 ms |
| Faixa e Inspector — botões | `Anim.Iniciar(Faixa, Insp)` | hover: opacidade 0,85 → 1 (120 ms); aperto: escala 0,97 |
| Backstage (Arquivo) | `Bastidor.AreAnimationsEnabled = Anim.Ligado` | animação própria do Fluent, ligada só quando permitido |
| Painéis encaixados | `AoMostrar` no conteúdo de cada `LayoutAnchorable` | fade 120 ms ao mostrar (ocultar é imediato) |
| Diálogos (`JanelaEscura` e derivadas) | `Janelas.cs`, `Loaded` | `Anim.Abrir` — exceto `JanelaRelatorio` |
| Tela inicial | `Inicio.cs`, fim de `MontarTelaInicial` | blocos entram escalonados |
| HUD — seletor segmentado | `Hud.cs`, `Atualizar` | segmento ativo novo com fundo em transição |
| Paleta Ctrl+K | `Paleta.cs`, `PaletaJanela.Loaded` | `Anim.Abrir` |
| Dicas (ToolTip) | handler de classe em `Anim.Iniciar` | fade 120 ms |
| Aviso da vista (toast) | `AoMostrar(Aviso, 8)` | fade + TranslateY 8 px |
| Vista 2D — diagramas | `Vista.Crescer()` chamado em `MostrarDiagrama` quando o tipo muda | deformada/N/V/M crescem de 0 (250 ms); legenda e inspeção mostram o valor real |
| Vista 2D — seleção | `Vista.Avisar()` | halo da seleção com fade 120 ms |
| Barra de status — após calcular (F5) | `Acoes.cs`, `Resolver(avisar)` | ΣRx, ΣRy, desl. máx. contam até o valor (o log recebe o texto final) |
| Barra de status — motor Python | `CalcularMotor` | `Anim.Brilho` no lugar da ProgressBar |
| Relatório / memorial | — | nenhuma (documento) |

## Janela nova

```csharp
public class JanelaNova : JanelaEscura { … }   // já anima ao abrir (Loaded → Anim.Abrir)
// Window que não herda JanelaEscura:
Loaded += (s, e) => Anim.Abrir(this);
// elemento que aparece/some:
Anim.AoMostrar(meuPainel, 8);
// lista de resultados nova:
Anim.Escalonar(painelDeLinhas);
```

Não animar abertura de abas de conteúdo, relatório ou nada que segure um resultado. Captura nova em `Capturas.cs` continua valendo
(no modo de capturas tudo já sai no estado final).
