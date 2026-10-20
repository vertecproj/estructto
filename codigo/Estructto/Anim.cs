// Anim: o UNICO helper de animacao da interface (regras e lugares de uso em docs/ANIMACOES.md).
// So anima Opacity, RenderTransform e cor (baratos para a composicao). Toda animacao grava o valor FINAL como valor base
// e usa FillBehavior.Stop: se a animacao for cortada, desligada (Windows sem animacoes) ou estiver no modo --capturas,
// o elemento fica sempre no estado final. Nada aqui bloqueia entrada nem atrasa resultado de calculo.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace NovoFtool
{
    public static class Anim
    {
        // ---------- tokens ----------
        public const int Rapido = 120;     // hover, dicas, realce de selecao
        public const int Medio = 160;      // paineis, dialogos, toasts
        public const int Espacial = 250;   // diagramas crescendo, movimento na vista
        public const int Lento = 400;      // contagem de numeros, limite de qualquer movimento
        public const int Passo = 30, PassoMax = 300;   // escalonamento de listas

        public static readonly IEasingFunction Saida = Congelar(EasingMode.EaseOut);
        public static readonly IEasingFunction EntradaSaida = Congelar(EasingMode.EaseInOut);
        static IEasingFunction Congelar(EasingMode m) { var e = new CubicEase { EasingMode = m }; e.Freeze(); return e; }

        // Windows "Mostrar animacoes" desligado (SystemParameters.ClientAreaAnimation) ou modo --capturas: tudo desliga.
        public static bool Ligado => SystemParameters.ClientAreaAnimation && !Capturas.Ativo;

        // ---------- primitiva ----------

        // Anima p de "de" ate "ate" (com atraso opcional, segurando "de" ate comecar). O valor base vira "ate".
        public static void Valor(DependencyObject alvo, DependencyProperty p, double de, double ate, int ms, int atraso = 0, IEasingFunction ease = null)
        {
            var an = alvo as IAnimatable;
            alvo.SetValue(p, ate);
            if (an == null) return;
            if (!Ligado) { an.BeginAnimation(p, null); return; }
            var k = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
            k.KeyFrames.Add(new DiscreteDoubleKeyFrame(de, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            if (atraso > 0) k.KeyFrames.Add(new DiscreteDoubleKeyFrame(de, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(atraso))));
            k.KeyFrames.Add(new EasingDoubleKeyFrame(ate, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(atraso + ms)), ease ?? Saida));
            an.BeginAnimation(p, k);
        }

        static double OpacidadeBase(UIElement el) => (double)el.GetAnimationBaseValue(UIElement.OpacityProperty);

        // Usa a transformacao do elemento se for do tipo T; cria uma se nao houver nenhuma; null se ja houver outra (nao mexe).
        static T Transf<T>(UIElement el) where T : Transform, new()
        {
            if (el.RenderTransform is T t && !t.IsFrozen) return t;
            if (el.RenderTransform == null || (el.RenderTransform is MatrixTransform m && m.Matrix.IsIdentity))
            {
                var n = new T(); el.RenderTransform = n; return n;
            }
            return null;
        }

        // ---------- entradas ----------

        // Fade-in (opacidade 0 -> a do elemento) + deslize vertical dy -> 0. Paineis, toasts, itens de lista.
        public static void Entrar(UIElement el, double dy = 0, int ms = Medio, int atraso = 0)
        {
            if (el == null) return;
            Valor(el, UIElement.OpacityProperty, 0, OpacidadeBase(el), ms, atraso);
            if (dy != 0) { var t = Transf<TranslateTransform>(el); if (t != null) Valor(t, TranslateTransform.YProperty, dy, 0, ms, atraso); }
        }

        // Fade + escala 0,98 -> 1 a partir do centro. Dialogos, paleta, flyouts.
        public static void Surgir(UIElement el, int ms = Medio)
        {
            if (el == null) return;
            Valor(el, UIElement.OpacityProperty, 0, OpacidadeBase(el), ms);
            var s = Transf<ScaleTransform>(el);
            if (s == null) return;
            el.RenderTransformOrigin = new Point(0.5, 0.5);
            Valor(s, ScaleTransform.ScaleXProperty, 0.98, 1, ms);
            Valor(s, ScaleTransform.ScaleYProperty, 0.98, 1, ms);
        }

        // Janela nova: anima o conteudo (o Window em si nao tem opacidade sem AllowsTransparency).
        public static void Abrir(Window w) { if (w != null) Surgir(w.Content as UIElement); }

        // Toda vez que o elemento ficar visivel, entra com fade (+ deslize opcional).
        public static void AoMostrar(UIElement el, double dy = 0, int ms = Medio)
        {
            if (el == null) return;
            el.IsVisibleChanged += (s, e) => { if ((bool)e.NewValue) Entrar(el, dy, ms); };
        }

        // Filhos entram em sequencia (Passo ms cada, ate PassoMax).
        public static void Escalonar(Panel p, double dy = 6)
        {
            if (p == null) return;
            for (int i = 0; i < p.Children.Count; i++) Entrar(p.Children[i], dy, Medio, Math.Min(i * Passo, PassoMax));
        }

        // ---------- cor ----------

        // Troca o fundo com transicao a partir do transparente (realce suave de item ativo/selecionado).
        public static void Fundo(Border b, Brush alvo, int ms = Rapido)
        {
            if (!(alvo is SolidColorBrush sc) || !Ligado) { b.Background = alvo; return; }
            var nb = new SolidColorBrush(sc.Color);
            b.Background = nb;
            var de = sc.Color; de.A = 0;
            nb.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(de, sc.Color, TimeSpan.FromMilliseconds(ms)) { EasingFunction = Saida, FillBehavior = FillBehavior.Stop });
        }

        // ---------- numeros ----------

        static readonly Regex numDec = new Regex(@"\d+,\d+");
        static readonly CultureInfo BR = CultureInfo.GetCultureInfo("pt-BR");

        // Os numeros com casas decimais do texto (medidas: kN, mm...) contam de 0 ate o valor. O texto final ja esta no
        // TextBlock antes de comecar (log, leitura) e volta a ele no fim; se alguem trocar o texto no meio, para.
        public static void Contar(TextBlock tb, int ms = Lento)
        {
            string fim = tb?.Text;
            if (fim == null || !Ligado || !numDec.IsMatch(fim)) return;
            var sw = Stopwatch.StartNew();
            string ultimo = fim;
            EventHandler passo = null;
            passo = (s, e) =>
            {
                if (tb.Text != ultimo) { CompositionTarget.Rendering -= passo; return; }
                double t = Math.Min(1, sw.Elapsed.TotalMilliseconds / ms), k = 1 - Math.Pow(1 - t, 3);
                ultimo = t >= 1 ? fim : numDec.Replace(fim, m =>
                {
                    int casas = m.Value.Length - m.Value.IndexOf(',') - 1;
                    return (double.Parse(m.Value, NumberStyles.Number, BR) * k).ToString("F" + casas, BR);
                });
                tb.Text = ultimo;
                if (t >= 1) CompositionTarget.Rendering -= passo;
            };
            CompositionTarget.Rendering += passo;
        }

        // ---------- progresso indeterminado ----------

        // Trilho fino com um brilho de acento passando (calculo longo). Sem animacao: trilho cheio, estatico.
        public static FrameworkElement Brilho(double larg = 90, double alt = 4)
        {
            var cor = ((SolidColorBrush)TemaPlant.BAcento).Color;
            var trilho = new Border { Width = larg, Height = alt, CornerRadius = new CornerRadius(alt / 2), Background = TemaPlant.BSup3, ClipToBounds = true };
            if (!Ligado) { trilho.Child = new Border { Background = TemaPlant.BAcento, Opacity = 0.6, CornerRadius = trilho.CornerRadius }; return trilho; }
            var transp = cor; transp.A = 0;
            var faixa = new Border
            {
                Width = larg * 0.4, HorizontalAlignment = HorizontalAlignment.Left,
                Background = new LinearGradientBrush(new GradientStopCollection { new GradientStop(transp, 0), new GradientStop(cor, 0.5), new GradientStop(transp, 1) }, 0)
            };
            var tt = new TranslateTransform();
            faixa.RenderTransform = tt;
            trilho.Child = faixa;
            tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-larg * 0.4, larg, TimeSpan.FromMilliseconds(1100)) { RepeatBehavior = RepeatBehavior.Forever, EasingFunction = EntradaSaida });
            return trilho;
        }

        // ---------- globais: botoes (hover/press) e dicas ----------

        static readonly List<UIElement> raizes = new List<UIElement>();

        // Liga uma vez: dicas (ToolTip) com fade e botoes dentro das raizes dadas (faixa, inspector) com hover/press suaves.
        public static void Iniciar(params UIElement[] raizesBotoes)
        {
            if (raizes.Count == 0)
            {
                EventManager.RegisterClassHandler(typeof(ToolTip), ToolTip.OpenedEvent, new RoutedEventHandler((s, e) => Entrar((ToolTip)s, 0, Rapido)));
                EventManager.RegisterClassHandler(typeof(ButtonBase), UIElement.MouseEnterEvent, new MouseEventHandler((s, e) =>
                { if (Dentro(s)) Valor((UIElement)s, UIElement.OpacityProperty, 0.85, OpacidadeBase((UIElement)s), Rapido); }));
                EventManager.RegisterClassHandler(typeof(ButtonBase), UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler((s, e) => Pressionar(s, true)), true);
                EventManager.RegisterClassHandler(typeof(ButtonBase), UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler((s, e) => Pressionar(s, false)), true);
                EventManager.RegisterClassHandler(typeof(ButtonBase), UIElement.MouseLeaveEvent, new MouseEventHandler((s, e) => Pressionar(s, false)));
            }
            raizes.AddRange(raizesBotoes);
        }

        static bool Dentro(object s)
        {
            if (!Ligado || !(s is DependencyObject d)) return false;
            foreach (var r in raizes) if (r.IsAncestorOf(d)) return true;
            return false;
        }

        // Aperto: escala 0,97 enquanto pressionado; volta a 1 ao soltar/sair.
        static void Pressionar(object s, bool baixo)
        {
            var el = (UIElement)s;
            if (!baixo) { if (el.RenderTransform is ScaleTransform st0 && !st0.IsFrozen && st0.HasAnimatedProperties) Soltar(st0); return; }
            if (!Dentro(s)) return;
            var st = Transf<ScaleTransform>(el);
            if (st == null) return;
            el.RenderTransformOrigin = new Point(0.5, 0.5);
            var a = new DoubleAnimation(0.97, TimeSpan.FromMilliseconds(80)) { EasingFunction = Saida };   // HoldEnd enquanto apertado
            st.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }

        static void Soltar(ScaleTransform st)
        {
            var a = new DoubleAnimation(1, TimeSpan.FromMilliseconds(Rapido)) { EasingFunction = Saida, FillBehavior = FillBehavior.Stop };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }
    }
}
