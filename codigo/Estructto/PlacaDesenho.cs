// Placa de base (E5): componentes visuais compartilhados (chips, segmentado, barra de capacidade, campo numérico),
// a vista de cima com cotas editáveis (PlanoPlaca) e a vista 3D girável (Placa3D: concreto, hef, cones, solda).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Portico;

namespace NovoFtool
{
    // ---------- peças de interface no padrão do GUIA-DESIGN (usadas na placa e na comparação) ----------
    public static class UiPlaca
    {
        public enum Nivel { Ok, Alerta, Erro, Neutro, Info }

        public static TextBlock Tx(string s, double tam = 12, Brush cor = null, FontWeight? peso = null, bool quebra = false)
        {
            return new TextBlock
            {
                Text = s ?? "", FontSize = tam, Foreground = cor ?? TemaPlant.BTexto1, FontWeight = peso ?? FontWeights.Normal,
                FontFamily = tam >= 15 ? TemaPlant.FonteTitulo : TemaPlant.Fonte,   // titulos (>= 15 px) na fonte de exibicao (Barlow)
                TextWrapping = quebra ? TextWrapping.Wrap : TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center
            };
        }

        public static T Dica<T>(T e, string dica) where T : FrameworkElement
        {
            if (!string.IsNullOrEmpty(dica))
            {
                e.ToolTip = dica;
                ToolTipService.SetInitialShowDelay(e, 300);
                ToolTipService.SetShowDuration(e, 30000);
            }
            return e;
        }

        // Controle segmentado (GUIA 5.3): fundo Surface.0, item ativo Surface.3 com borda
        public static Border Segmentado(IList<(string texto, bool ativo, Action clique, string dica)> itens, double altura = 28, double fonte = 12)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var it in itens)
            {
                bool at = it.ativo; Action ac = it.clique;
                var tb = new TextBlock
                {
                    Text = it.texto, FontSize = fonte, FontWeight = at ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = at ? TemaPlant.BTexto1 : TemaPlant.BTexto2, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center
                };
                var b = new Border
                {
                    CornerRadius = new CornerRadius(3), Padding = new Thickness(10, 0, 10, 0), MinWidth = 30,
                    Background = at ? TemaPlant.BSup3 : Brushes.Transparent, BorderBrush = at ? TemaPlant.BBorda : Brushes.Transparent,
                    BorderThickness = new Thickness(1), Cursor = Cursors.Hand, Child = tb
                };
                b.MouseEnter += (s, e) => { if (!at) { b.Background = TemaPlant.BSup2; tb.Foreground = TemaPlant.BTexto1; } };
                b.MouseLeave += (s, e) => { if (!at) { b.Background = Brushes.Transparent; tb.Foreground = TemaPlant.BTexto2; } };
                b.MouseLeftButtonDown += (s, e) => { e.Handled = true; ac?.Invoke(); };
                AutomationPropertiesNome(b, it.texto);
                Dica(b, it.dica);
                sp.Children.Add(b);
            }
            return new Border
            {
                Background = TemaPlant.BSup0, BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5),
                Padding = new Thickness(2), Height = altura, Child = sp, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center
            };
        }

        static void AutomationPropertiesNome(FrameworkElement e, string nome) { System.Windows.Automation.AutomationProperties.SetName(e, nome ?? ""); }

        public static (Brush fg, Brush bg, Brush bd) Cores(Nivel n)
        {
            switch (n)
            {
                case Nivel.Ok: return (TemaPlant.BSucesso, TemaPlant.BSucessoFundo, TemaPlant.BSucessoBorda);
                case Nivel.Alerta: return (TemaPlant.BAlerta, TemaPlant.BAlertaFundo, TemaPlant.BAlertaBorda);
                case Nivel.Erro: return (TemaPlant.BErro, TemaPlant.BErroFundo, TemaPlant.BErroBorda);
                case Nivel.Info: return (TemaPlant.BRealce, TemaPlant.BInfoFundo, TemaPlant.B(TemaPlant.Realce, 90));
                default: return (TemaPlant.BTexto2, TemaPlant.BSup2, TemaPlant.BBordaSutil);
            }
        }

        // Chip de status (GUIA 5.5): fundo escuro tonal + texto saturado
        public static Border Chip(string texto, Nivel n, string dica = null, Action clique = null, double tam = 11)
        {
            var c = Cores(n);
            var b = new Border
            {
                Background = c.bg, BorderBrush = c.bd, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 2, 6, 2), VerticalAlignment = VerticalAlignment.Center,
                Child = Tx(texto, tam, c.fg, FontWeights.SemiBold)
            };
            if (clique != null)
            {
                b.Cursor = Cursors.Hand;
                b.MouseLeftButtonDown += (s, e) => { e.Handled = true; clique(); };
            }
            return Dica(b, dica);
        }

        public static Nivel NivelAp(double ap, bool ok) { return !ok || ap > 1 + 1e-9 ? Nivel.Erro : ap > 0.9 ? Nivel.Alerta : Nivel.Ok; }

        // Cor da barra por faixa (GUIA 5.6): ≤75 verde · ≤90 ciano · ≤100 âmbar · >100 ou reprovado vermelho
        public static Brush CorAp(double ap, bool ok)
        {
            if (!ok || ap > 1 + 1e-9) return TemaPlant.BErro;
            if (ap > 0.9) return TemaPlant.BAlerta;
            if (ap > 0.75) return TemaPlant.BRealce;
            return TemaPlant.BSucesso;
        }

        public static string Pct(double ap) { return double.IsNaN(ap) || double.IsInfinity(ap) ? "—" : (ap * 100).ToString("0") + "%"; }

        public static FrameworkElement BarraCap(double ap, bool ok, double alt = 6)
        {
            if (double.IsNaN(ap) || double.IsInfinity(ap)) ap = 0;
            var g = new Grid { Height = alt, VerticalAlignment = VerticalAlignment.Center };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, Math.Min(1, ap)), GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0, 1 - Math.Min(1, ap)), GridUnitType.Star) });
            var trilho = new Border { Background = TemaPlant.BSup0, CornerRadius = new CornerRadius(alt / 2) };
            Grid.SetColumnSpan(trilho, 2);
            g.Children.Add(trilho);
            if (ap > 0) g.Children.Add(new Border { Background = CorAp(ap, ok), CornerRadius = new CornerRadius(alt / 2) });
            return g;
        }

        // Botão secundário compacto com ícone opcional (28 px)
        public static Border Botao(string texto, string icone, Action acao, string dica = null, double altura = 28)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (!string.IsNullOrEmpty(icone) && Icones.Existe(icone))
            {
                var ic = (FrameworkElement)Icones.Criar(icone, 14);
                ic.Margin = new Thickness(0, 0, 6, 0); ic.VerticalAlignment = VerticalAlignment.Center;
                sp.Children.Add(ic);
            }
            sp.Children.Add(Tx(texto, 12, TemaPlant.BTexto1, FontWeights.SemiBold));
            var b = new Border
            {
                Background = TemaPlant.BSup3, BorderBrush = TemaPlant.BBorda, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
                Padding = new Thickness(9, 0, 10, 0), Height = altura, Cursor = Cursors.Hand, Child = sp, VerticalAlignment = VerticalAlignment.Center
            };
            b.MouseEnter += (s, e) => { b.Background = TemaPlant.BSupHover; b.BorderBrush = TemaPlant.BTextoMudo; };
            b.MouseLeave += (s, e) => { b.Background = TemaPlant.BSup3; b.BorderBrush = TemaPlant.BBorda; };
            b.MouseLeftButtonDown += (s, e) => { e.Handled = true; acao?.Invoke(); };
            AutomationPropertiesNome(b, texto);
            return Dica(b, dica);
        }

        // Link discreto (texto em Realce)
        public static TextBlock Link(string texto, Action acao, string dica = null, double tam = 12)
        {
            var t = Tx(texto, tam, TemaPlant.BRealce, FontWeights.SemiBold);
            t.Cursor = Cursors.Hand;
            t.MouseEnter += (s, e) => t.TextDecorations = TextDecorations.Underline;
            t.MouseLeave += (s, e) => t.TextDecorations = null;
            t.MouseLeftButtonDown += (s, e) => { e.Handled = true; acao?.Invoke(); };
            return Dica(t, dica);
        }

        // Campo numérico com unidade integrada (GUIA 5.2). Enter ou sair do campo aplica; Esc desfaz.
        public static Border CaixaNum(string valor, string unidade, double larg, Action<double> aplicar, string dica = null, Func<double, bool> valido = null, string prefixo = null)
        {
            var tb = new TextBox
            {
                Text = valor, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = TemaPlant.BTexto1, CaretBrush = TemaPlant.BTexto1,
                TextAlignment = TextAlignment.Right, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(4, 0, 3, 0), FontSize = 12.5,
                MinWidth = 18, VerticalAlignment = VerticalAlignment.Stretch
            };
            Typography.SetNumeralAlignment(tb, FontNumeralAlignment.Tabular);
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(tb, 1);
            g.Children.Add(tb);
            if (!string.IsNullOrEmpty(prefixo))
            {
                var pf = Tx(prefixo, 11, TemaPlant.BTexto2); pf.Margin = new Thickness(7, 0, 0, 0); pf.IsHitTestVisible = false;
                g.Children.Add(pf);
            }
            if (!string.IsNullOrEmpty(unidade))
            {
                var u = Tx(unidade, 11, TemaPlant.BTexto2); u.Margin = new Thickness(0, 0, 7, 0); u.IsHitTestVisible = false;
                Grid.SetColumn(u, 2); g.Children.Add(u);
            }
            var bd = new Border
            {
                Height = 28, Width = larg, CornerRadius = new CornerRadius(4), Background = TemaPlant.BSup3, BorderBrush = TemaPlant.BBorda,
                BorderThickness = new Thickness(1), Child = g, VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.IBeam
            };
            double v0; bool tem0 = Num.Ler(valor, out v0);
            bool feito = false;
            Action ok = () =>
            {
                if (feito) return;
                double v;
                if (Num.Ler(tb.Text, out v) && (valido == null || valido(v)))
                {
                    if (!tem0 || Math.Abs(v - v0) > 1e-9) { feito = true; aplicar(v); }
                }
                else tb.Text = valor;
            };
            tb.GotKeyboardFocus += (s, e) => { bd.BorderBrush = TemaPlant.BBordaFoco; tb.SelectAll(); };
            tb.PreviewMouseLeftButtonDown += (s, e) => { if (!tb.IsKeyboardFocusWithin) { e.Handled = true; tb.Focus(); } };
            tb.LostKeyboardFocus += (s, e) => { bd.BorderBrush = TemaPlant.BBorda; ok(); };
            tb.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { ok(); e.Handled = true; }
                else if (e.Key == Key.Escape) { tb.Text = valor; tb.SelectAll(); e.Handled = true; }
            };
            bd.MouseLeftButtonDown += (s, e) => { tb.Focus(); e.Handled = true; };
            return Dica(bd, dica);
        }

        public static ComboBox Combo(IEnumerable<string> itens, string sel, Action<string> mudou, double larg, string dica = null)
        {
            var cb = new ComboBox { Width = larg, Height = 28, VerticalAlignment = VerticalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
            foreach (string s in itens) cb.Items.Add(s);
            cb.SelectedItem = sel;
            cb.SelectionChanged += (s, e) => { if (cb.SelectedItem is string x && x != sel) mudou(x); };
            return Dica(cb, dica);
        }

        // Stepper compacto [ − | valor | + ] (28 px)
        public static Border Stepper(string valor, Action menos, Action mais, double largMin = 84, Action cliqueCentro = null, string dica = null)
        {
            var g = new Grid { Height = 26 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            Border Bt(string s, int col, Action acao)
            {
                var bt = new Border
                {
                    Background = TemaPlant.BSup3, Cursor = acao != null ? Cursors.Hand : Cursors.Arrow, Opacity = acao != null ? 1.0 : 0.4,
                    Child = new TextBlock { Text = s, FontSize = 14, Foreground = TemaPlant.BTexto1, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                };
                if (acao != null)
                {
                    bt.MouseEnter += (a, e) => bt.Background = TemaPlant.BSupHover;
                    bt.MouseLeave += (a, e) => bt.Background = TemaPlant.BSup3;
                    bt.MouseLeftButtonDown += (a, e) => { e.Handled = true; acao(); };
                }
                AutomationPropertiesNome(bt, s == "−" ? "Diminuir" : "Aumentar");
                Grid.SetColumn(bt, col);
                return bt;
            }
            g.Children.Add(Bt("−", 0, menos));
            var centro = new Border
            {
                Background = TemaPlant.BSup0, MinWidth = largMin, Padding = new Thickness(8, 0, 8, 0),
                Cursor = cliqueCentro != null ? Cursors.Hand : Cursors.Arrow,
                Child = new TextBlock { Text = valor, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = TemaPlant.BTexto1, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            if (cliqueCentro != null) centro.MouseLeftButtonDown += (a, e) => { e.Handled = true; cliqueCentro(); };
            Grid.SetColumn(centro, 1);
            g.Children.Add(centro);
            g.Children.Add(Bt("+", 2, mais));
            var bd = new Border { BorderBrush = TemaPlant.BBorda, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Child = g, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true };
            return Dica(bd, dica);
        }

        // Cabeçalho de seção discreto (GUIA 5.4): barra de 3 px em acento + título 10.5 px caixa alta
        public static FrameworkElement TituloSecao(string titulo, UIElement direita = null, double margemTopo = 10)
        {
            var dp = new DockPanel { Margin = new Thickness(0, margemTopo, 0, 6), LastChildFill = true };
            if (direita != null) { DockPanel.SetDock(direita, Dock.Right); dp.Children.Add(direita); }
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(new Border { Width = 3, Height = 12, Background = TemaPlant.BAcento, CornerRadius = new CornerRadius(1), Margin = new Thickness(0, 0, 7, 0) });
            sp.Children.Add(Tx(titulo.ToUpperInvariant(), 10.5, TemaPlant.BTexto2, FontWeights.SemiBold));
            dp.Children.Add(sp);
            return dp;
        }

        // Tabela de cálculo: Grandeza | Expressão | Valor | ✓✕ (linhas de 26 px, números à direita)
        public static FrameworkElement TabelaCalc(IEnumerable<(string rot, string expr, string val, int est)> linhas, double largRot = 190)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(largRot) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            int i = 0;
            foreach (var l in linhas)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
                var fundo = new Border { Background = i % 2 == 0 ? TemaPlant.BSup0 : TemaPlant.BSup1, MinHeight = 26, BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(0, 0, 0, 1) };
                Grid.SetRow(fundo, i); Grid.SetColumnSpan(fundo, 4); g.Children.Add(fundo);
                var a = Tx(l.rot, 12, TemaPlant.BTexto2, null, true); a.Margin = new Thickness(10, 4, 8, 4);
                Grid.SetRow(a, i); g.Children.Add(a);
                var ex = Tx(l.expr ?? "", 11.5, TemaPlant.BTexto3, null, true); ex.Margin = new Thickness(0, 4, 12, 4); ex.FontFamily = TemaPlant.FonteMono;
                Grid.SetRow(ex, i); Grid.SetColumn(ex, 1); g.Children.Add(ex);
                Brush cor = l.est > 0 ? TemaPlant.BSucesso : l.est < 0 ? TemaPlant.BErro : TemaPlant.BTexto1;
                var v = Tx(l.val, 12, cor, l.est != 0 ? FontWeights.SemiBold : FontWeights.Normal, true);
                v.TextAlignment = TextAlignment.Right; v.HorizontalAlignment = HorizontalAlignment.Right; v.Margin = new Thickness(0, 4, 6, 4); v.MaxWidth = 300;
                Typography.SetNumeralAlignment(v, FontNumeralAlignment.Tabular);
                Grid.SetRow(v, i); Grid.SetColumn(v, 2); g.Children.Add(v);
                if (l.est != 0)
                {
                    var s = Tx(l.est > 0 ? "✓" : "✕", 12, cor, FontWeights.Bold); s.HorizontalAlignment = HorizontalAlignment.Center;
                    Grid.SetRow(s, i); Grid.SetColumn(s, 3); g.Children.Add(s);
                }
                i++;
            }
            return new Border { BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(1, 1, 1, 0), CornerRadius = new CornerRadius(4), Child = g, ClipToBounds = true };
        }
    }

    // ---------- vista de cima da placa (Planta e Forças), com cotas azuis clicáveis ----------
    public class PlanoPlaca : Canvas
    {
        public ResPlaca R;
        public ResNbr Nbr;     // motor: bloco de compressão e chumbadores tracionados (eixos da janela)
        public PlacaBase P;
        public bool ModoForcas;
        public Action AoEditar;

        class CaixaCota { public Rect Ret; public string Valor; public Action<double> Aplicar; }
        readonly List<CaixaCota> caixas = new List<CaixaCota>();
        TextBox editorAtivo;
        CaixaCota sobre;

        public PlanoPlaca()
        {
            ClipToBounds = true;
            MouseMove += (s, e) =>
            {
                Point pt = e.GetPosition(this);
                var cx = caixas.FirstOrDefault(c => c.Ret.Contains(pt));
                Cursor = cx != null ? Cursors.Hand : Cursors.Arrow;
                if (cx != sobre) { sobre = cx; InvalidateVisual(); }
            };
            MouseLeave += (s, e) => { if (sobre != null) { sobre = null; InvalidateVisual(); } };
            MouseLeftButtonDown += (s, e) =>
            {
                Point pt = e.GetPosition(this);
                var cx = caixas.FirstOrDefault(c => c.Ret.Contains(pt));
                if (cx != null) { AbrirEditor(cx); e.Handled = true; }
                else FecharEditor(true);
            };
        }

        void AbrirEditor(CaixaCota cx)
        {
            FecharEditor(false);
            var tb = new TextBox
            {
                Text = cx.Valor,
                Width = Math.Max(56, cx.Ret.Width + 8),
                Height = cx.Ret.Height + 4,
                Background = TemaPlant.BInfoFundo,
                Foreground = TemaPlant.BTexto1,
                BorderBrush = TemaPlant.BBordaFoco,
                BorderThickness = new Thickness(1.5),
                CaretBrush = TemaPlant.BTexto1,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(2, 0, 2, 0)
            };
            SetLeft(tb, cx.Ret.X - 4);
            SetTop(tb, cx.Ret.Y - 2);
            bool aplicado = false;
            Action confirmar = () =>
            {
                if (aplicado) return;
                aplicado = true;
                double v;
                if (Num.Ler(tb.Text, out v) && v >= 0) { cx.Aplicar(v); Children.Remove(tb); editorAtivo = null; AoEditar?.Invoke(); }
                else { Children.Remove(tb); editorAtivo = null; }
            };
            tb.LostFocus += (s, e) => confirmar();
            tb.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { confirmar(); e.Handled = true; }
                else if (e.Key == Key.Escape) { aplicado = true; Children.Remove(tb); editorAtivo = null; e.Handled = true; }
            };
            editorAtivo = tb;
            Children.Add(tb);
            tb.Focus();
            tb.SelectAll();
        }

        public void FecharEditor(bool salvar)
        {
            if (editorAtivo == null) return;
            Children.Clear();
            editorAtivo = null;
        }

        protected override void OnRender(DrawingContext dc)
        {
            caixas.Clear();
            dc.DrawRectangle(TemaPlant.BBgCanvas, null, new Rect(0, 0, ActualWidth, ActualHeight));
            if (R == null || R.H <= 0 || R.B <= 0 || ActualWidth < 100 || ActualHeight < 100) return;

            var ft = new Typeface("Segoe UI");
            double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            FormattedText Txt(string s, double tam, Brush cor, FontWeight? peso = null) =>
                new FormattedText(s ?? "", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    new Typeface(ft.FontFamily, FontStyles.Normal, peso ?? FontWeights.Normal, FontStretches.Normal), tam, cor, dip);

            double padX = 190, padY = ModoForcas ? 190 : 170;
            double esc = Math.Max(0.15, Math.Min((ActualWidth - padX) / R.H, (ActualHeight - padY) / R.B));
            Point c = new Point(ActualWidth / 2 - 12, ActualHeight / 2 + (ModoForcas ? 18 : 6));
            double sx = R.Espelhar ? -1 : 1;
            Func<double, double, Point> T = (y, z) => new Point(c.X + sx * y * esc, c.Y - z * esc);

            Rect rectPlaca = new Rect(T(-R.H / 2, R.B / 2), T(R.H / 2, -R.B / 2));
            dc.DrawRectangle(TemaPlant.BPlacaFundo, null, rectPlaca);
            dc.PushClip(new RectangleGeometry(rectPlaca));
            var penHach = new Pen(TemaPlant.BPlacaHachura, 1);
            double diag = rectPlaca.Width + rectPlaca.Height;
            for (double d = -rectPlaca.Height; d < diag; d += 10)
                dc.DrawLine(penHach, new Point(rectPlaca.Left + d, rectPlaca.Top), new Point(rectPlaca.Left + d - rectPlaca.Height, rectPlaca.Bottom));
            dc.Pop();
            dc.DrawRectangle(null, new Pen(TemaPlant.BBarra, 2), rectPlaca);

            // bloco de compressão do motor (retangular, a σc,Rd ou fp): faixa de comprimento Y no lado comprimido
            bool bloco = Nbr != null && Nbr.Valido && Nbr.TemBloco && R.TemEsforcos;
            Color corBloco = ((SolidColorBrush)JanelaEscura.BAmarelo).Color;
            if (bloco)
            {
                double a = Math.Min(Nbr.BlocoDe, Nbr.BlocoAte), b = Math.Max(Nbr.BlocoDe, Nbr.BlocoAte);
                Rect rb = Nbr.Fraco ? new Rect(T(-R.H / 2, b), T(R.H / 2, a)) : new Rect(T(a, R.B / 2), T(b, -R.B / 2));
                rb.Intersect(rectPlaca);
                dc.DrawRectangle(TemaPlant.B(corBloco, 70), new Pen(TemaPlant.B(corBloco, 230), 1.4) { DashStyle = DashStyles.Dash }, rb);
                var tb = Txt("compressão Y = " + Num.Fmt(Nbr.Y, 0) + " mm · " + Num.Fmt(Nbr.Fp, 2) + " MPa", 10.5, JanelaEscura.BAmarelo, FontWeights.SemiBold);
                Point pb = Nbr.Fraco ? new Point(rb.Left + 6, rb.Top - tb.Height - 4) : new Point(rb.Left + rb.Width / 2 - tb.Width / 2, rectPlaca.Top + 6);
                pb.X = Math.Max(rectPlaca.Left + 4, Math.Min(pb.X, rectPlaca.Right - tb.Width - 4));
                dc.DrawRoundedRectangle(TemaPlant.BFundoTextoCanvas, null, new Rect(pb.X - 4, pb.Y - 1, tb.Width + 8, tb.Height + 2), 3, 3);
                dc.DrawText(tb, pb);
            }

            // eixos de simetria tracejados
            var penEixo = new Pen(TemaPlant.BBorda, 1) { DashStyle = new DashStyle(new double[] { 10, 4, 2, 4 }, 0) };
            dc.DrawLine(penEixo, new Point(rectPlaca.Left - 14, c.Y), new Point(rectPlaca.Right + 14, c.Y));
            dc.DrawLine(penEixo, new Point(c.X, rectPlaca.Top - 10), new Point(c.X, rectPlaca.Bottom + 10));

            // perfil do pilar
            double h = R.RefIn, bf = R.RefOut;
            var azul = new Pen(TemaPlant.BRealce, 1.6);
            var cheio = TemaPlant.BPerfilArea;
            string forma = R.Sec != null ? R.Sec.Forma : "";
            double tfe = R.Sec != null ? Math.Max(R.Sec.Tf * 1000, 1.5) : 0, twe = R.Sec != null ? Math.Max(R.Sec.Tw * 1000, 1.5) : 0;
            Action<double, double, double, double> ret = (y1, z1, y2, z2) => dc.DrawRectangle(cheio, azul, new Rect(T(y1, z1), T(y2, z2)));
            Action<Point[]> poli = pts =>
            {
                var g = new StreamGeometry();
                using (var ctx = g.Open()) { ctx.BeginFigure(pts[0], true, true); ctx.PolyLineTo(pts.Skip(1).ToList(), true, true); }
                g.Freeze();
                dc.DrawGeometry(cheio, azul, g);
            };
            if (forma == "I" && !R.Deitado)
            {
                ret(-h / 2, bf / 2, -h / 2 + tfe, -bf / 2); ret(h / 2 - tfe, bf / 2, h / 2, -bf / 2); ret(-h / 2 + tfe, twe / 2, h / 2 - tfe, -twe / 2);
            }
            else if (forma == "I")
            {
                ret(-h / 2, bf / 2, h / 2, bf / 2 - tfe); ret(-h / 2, -bf / 2 + tfe, h / 2, -bf / 2); ret(-twe / 2, bf / 2 - tfe, twe / 2, -bf / 2 + tfe);
            }
            else if (forma == "TC")
            {
                double r = h / 2, ri = Math.Max(0, r - twe);
                var g = new GeometryGroup { FillRule = FillRule.EvenOdd };
                g.Children.Add(new EllipseGeometry(c, r * esc, r * esc)); g.Children.Add(new EllipseGeometry(c, ri * esc, ri * esc));
                dc.DrawGeometry(cheio, azul, g);
            }
            else if (forma == "TR")
            {
                var g = new GeometryGroup { FillRule = FillRule.EvenOdd };
                g.Children.Add(new RectangleGeometry(new Rect(T(-h / 2, bf / 2), T(h / 2, -bf / 2))));
                g.Children.Add(new RectangleGeometry(new Rect(T(-h / 2 + twe, bf / 2 - twe), T(h / 2 - twe, -bf / 2 + twe))));
                dc.DrawGeometry(cheio, azul, g);
            }
            else if (forma == "L")
            {
                double t = twe;
                poli(new[] { T(-h / 2, -bf / 2), T(h / 2, -bf / 2), T(h / 2, -bf / 2 + t), T(-h / 2 + t, -bf / 2 + t), T(-h / 2 + t, bf / 2), T(-h / 2, bf / 2) });
            }
            else if (forma == "U")
            {
                if (R.Deitado)
                    poli(new[] { T(-h / 2, bf / 2), T(h / 2, bf / 2), T(h / 2, bf / 2 - tfe), T(-h / 2 + twe, bf / 2 - tfe), T(-h / 2 + twe, -bf / 2 + tfe), T(h / 2, -bf / 2 + tfe), T(h / 2, -bf / 2), T(-h / 2, -bf / 2) });
                else
                    poli(new[] { T(-h / 2, bf / 2), T(-h / 2 + tfe, bf / 2), T(-h / 2 + tfe, -bf / 2 + twe), T(h / 2 - tfe, -bf / 2 + twe), T(h / 2 - tfe, bf / 2), T(h / 2, bf / 2), T(h / 2, -bf / 2), T(-h / 2, -bf / 2) });
            }
            else ret(-h / 2, bf / 2, h / 2, -bf / 2);

            if (R.Sec != null && !string.IsNullOrEmpty(R.Sec.Nome))
            {
                var tSec = Txt(R.Sec.Nome.Replace("x", "×"), 11, TemaPlant.BRealceClaro);
                Point pS = new Point(c.X - tSec.Width / 2, c.Y + bf / 2 * esc + 6);
                dc.DrawRoundedRectangle(TemaPlant.BFundoTextoCanvas, null, new Rect(pS.X - 4, pS.Y - 1, tSec.Width + 8, tSec.Height + 2), 3, 3);
                dc.DrawText(tSec, pS);
            }

            if (!double.IsNaN(R.PivoY))
            {
                var tr = new Pen(JanelaEscura.BAmarelo, 1) { DashStyle = DashStyles.Dash };
                dc.DrawLine(tr, T(R.PivoY, R.B / 2 + 10), T(R.PivoY, -R.B / 2 - 10));
                if (ModoForcas)
                {
                    var tg = Txt("linha de giro", 10.5, JanelaEscura.BAmarelo);
                    Point pg = T(R.PivoY, -R.B / 2 - 10);
                    // dentro da chapa, ao lado da linha (embaixo ela colidia com a cota inferior)
                    var rg = new Rect(pg.X + 6, pg.Y - tg.Height - 12, tg.Width + 8, tg.Height + 2);
                    dc.DrawRoundedRectangle(TemaPlant.BFundoTextoCanvas, null, rg, 3, 3);
                    dc.DrawText(tg, new Point(rg.X + 4, rg.Y + 1));
                }
            }

            // furos
            double tmax = R.Paraf.Count > 0 ? Math.Max(1e-9, R.Paraf.Max(p => p[2])) : 1;
            Brush corAnel = R.ChOk ? JanelaEscura.BVerde : JanelaEscura.BVermelho;
            var penAnel = new Pen(corAnel, 2.4);
            double rAnel = Math.Max(9, Math.Min(15, R.Dia / 2 * esc * 1.25));
            foreach (double[] p in R.Paraf)
            {
                Point q = T(p[0], p[1]);
                if (!ModoForcas)
                {
                    bool trac = Nbr != null && Nbr.Valido && p[2] > 1e-9;   // chumbador tracionado (motor): anel vermelho por fora e "T"
                    if (trac) dc.DrawEllipse(null, new Pen(TemaPlant.B(TemaPlant.Erro, 230), 2.2), q, rAnel + 4, rAnel + 4);
                    dc.DrawEllipse(TemaPlant.BBgCanvas, penAnel, q, rAnel, rAnel);
                    dc.DrawEllipse(corAnel, null, q, Math.Max(2.5, rAnel * 0.3), Math.Max(2.5, rAnel * 0.3));
                    if (trac)
                    {
                        var tt = Txt("T " + Num.Fmt(p[2], 1), 10.5, TemaPlant.BErroClaro, FontWeights.SemiBold);
                        double tx = q.X >= c.X ? q.X - rAnel - tt.Width - 9 : q.X + rAnel + 9;
                        dc.DrawRoundedRectangle(TemaPlant.BFundoTextoCanvas, null, new Rect(tx - 3, q.Y - tt.Height / 2 - 1, tt.Width + 6, tt.Height + 2), 3, 3);
                        dc.DrawText(tt, new Point(tx, q.Y - tt.Height / 2));
                    }
                }
                else
                {
                    byte a = (byte)(70 + 185 * Math.Min(1, p[2] / tmax));
                    var br = p[2] > 1e-9 ? TemaPlant.B(TemaPlant.Erro, a) : TemaPlant.B(TemaPlant.Realce, 60);
                    double rf = Math.Max(7, R.Dia / 2 * esc);
                    dc.DrawEllipse(br, new Pen(TemaPlant.BTexto1, 1.2), q, rf, rf);
                    if (R.TemEsforcos)
                    {
                        var tf1 = Txt("T = " + Num.Fmt(p[2], 2) + " kN", 11, TemaPlant.BTexto1, FontWeights.SemiBold);
                        var tf2 = Txt("V = " + Num.Fmt(R.Fv, 2) + " kN", 10.5, JanelaEscura.BFraco);
                        double wmax = Math.Max(tf1.Width, tf2.Width);
                        double tx = q.X >= c.X ? q.X - rf - wmax - 7 : q.X + rf + 7;   // rótulo para dentro da chapa (fora cruzava a borda)
                        dc.DrawRoundedRectangle(TemaPlant.BFundoTextoCanvas, null, new Rect(tx - 4, q.Y - 16, wmax + 8, 32), 3, 3);
                        dc.DrawText(tf1, new Point(tx, q.Y - 15));
                        dc.DrawText(tf2, new Point(tx, q.Y + 1));
                    }
                }
            }

            if (ModoForcas && R.TemEsforcos)
            {
                var tEsf = Txt("No pé do pilar:  N = " + Num.Fmt(R.N, 2) + " kN (" + (R.N >= 0 ? "tração" : "compressão") + ")   V = " + Num.Fmt(R.V, 2) + " kN   M = " + Num.Fmt(R.M, 2) + " kN·m", 12, JanelaEscura.BTexto, FontWeights.SemiBold);
                dc.DrawText(tEsf, new Point(14, 12));
                var tMod = Txt(R.Modelo, 11, TemaPlant.BTexto3);
                dc.DrawText(tMod, new Point(14, 32));
            }

            // cotas e caixinhas azuis clicáveis
            if (P != null)
            {
                var penCota = new Pen(TemaPlant.BTexto2, 1);
                var bgCaixa = TemaPlant.BInfoFundo;
                var bdCaixa = new Pen(TemaPlant.BRealce, 1);
                var bdSobre = new Pen(TemaPlant.BRealceClaro, 1.5);
                var fgCaixa = TemaPlant.BRealce;

                void DesenharCaixa(double cx, double cy, string texto, string valEdicao, Action<double> aplicar)
                {
                    var ftBox = Txt(texto, 12.5, fgCaixa, FontWeights.SemiBold);
                    double w = Math.Max(46, ftBox.Width + 16), hBox = 22;
                    Rect rBox = new Rect(cx - w / 2, cy - hBox / 2, w, hBox);
                    bool hov = sobre != null && Math.Abs(sobre.Ret.X - rBox.X) < 0.5 && Math.Abs(sobre.Ret.Y - rBox.Y) < 0.5;
                    dc.DrawRoundedRectangle(hov ? TemaPlant.BSelecao : bgCaixa, hov ? bdSobre : bdCaixa, rBox, 3, 3);
                    dc.DrawText(ftBox, new Point(cx - ftBox.Width / 2, cy - ftBox.Height / 2));
                    caixas.Add(new CaixaCota { Ret = rBox, Valor = valEdicao, Aplicar = aplicar });
                }

                double xL = rectPlaca.Left, xR = rectPlaca.Right, yT = rectPlaca.Top, yB = rectPlaca.Bottom;

                double yCotaH = yB + 26;
                dc.DrawLine(penCota, new Point(xL, yCotaH), new Point(xR, yCotaH));
                dc.DrawLine(penCota, new Point(xL, yCotaH - 6), new Point(xL, yCotaH + 6));
                dc.DrawLine(penCota, new Point(xR, yCotaH - 6), new Point(xR, yCotaH + 6));
                DesenharCaixa((xL + xR) / 2, yCotaH, Num.Fmt(R.H, 0), Num.Fmt(R.H, 0), v =>
                {
                    if (v <= 0) return;
                    if (P.PeloPerfil) P.Sobra = v > R.RefIn ? Math.Max(0, (v - R.RefIn) / 2) : Math.Max(0, v);
                    else P.H = v;
                });

                double xCotaB = xR + 30;
                dc.DrawLine(penCota, new Point(xCotaB, yT), new Point(xCotaB, yB));
                dc.DrawLine(penCota, new Point(xCotaB - 6, yT), new Point(xCotaB + 6, yT));
                dc.DrawLine(penCota, new Point(xCotaB - 6, yB), new Point(xCotaB + 6, yB));
                DesenharCaixa(xCotaB, (yT + yB) / 2, Num.Fmt(R.B, 0), Num.Fmt(R.B, 0), v =>
                {
                    if (v <= 0) return;
                    if (P.PeloPerfil) P.Sobra = v > R.RefOut ? Math.Max(0, (v - R.RefOut) / 2) : Math.Max(0, v);
                    else P.B = v;
                });

                double yCotaTop = yT - 24;
                double xFuro1 = xL + P.Borda * esc;
                dc.DrawLine(penCota, new Point(xL, yCotaTop), new Point(xFuro1, yCotaTop));
                dc.DrawLine(penCota, new Point(xL, yCotaTop - 6), new Point(xL, yCotaTop + 6));
                dc.DrawLine(penCota, new Point(xFuro1, yCotaTop - 6), new Point(xFuro1, yCotaTop + 6));
                var penChama = new Pen(TemaPlant.BTextoMudo, 1) { DashStyle = new DashStyle(new double[] { 2, 3 }, 0) };
                dc.DrawLine(penChama, new Point(xFuro1, yCotaTop + 6), new Point(xFuro1, yT));
                DesenharCaixa(Math.Min((xL + xFuro1) / 2, xFuro1 - 24), yCotaTop - 16, Num.Fmt(P.Borda, 0), Num.Fmt(P.Borda, 0), v =>
                {
                    if (v > 0 && 2 * v < R.H && 2 * v < R.B) P.Borda = v;
                });

                if (P.Fileiras >= 2)
                {
                    double entre = R.H - 2 * P.Borda;
                    double xFuroFim = xR - P.Borda * esc;
                    dc.DrawLine(penCota, new Point(xFuro1, yCotaTop), new Point(xFuroFim, yCotaTop));
                    dc.DrawLine(penCota, new Point(xFuroFim, yCotaTop - 6), new Point(xFuroFim, yCotaTop + 6));
                    dc.DrawLine(penChama, new Point(xFuroFim, yCotaTop + 6), new Point(xFuroFim, yT));
                    double cxEntre = Math.Max(xFuro1 + 56, (xFuro1 + xFuroFim) / 2);
                    DesenharCaixa(cxEntre, yCotaTop - 16, Num.Fmt(entre, 0), Num.Fmt(entre, 0), v =>
                    {
                        if (v > 0 && v < R.H - 10) P.Borda = Math.Round((R.H - v) / 2, 1);
                    });
                }

                double yPerfB = c.Y + bf / 2 * esc;
                if (yB - yPerfB > 30)
                {
                    var penTr = new Pen(TemaPlant.BTexto2, 1) { DashStyle = new DashStyle(new double[] { 3, 4 }, 0) };
                    dc.DrawLine(penTr, new Point(c.X + 18, yPerfB + 20), new Point(c.X + 18, yB));
                    double sobraVal = P.PeloPerfil ? P.Sobra : Math.Max(0, (R.H - R.RefIn) / 2);
                    var ftSobra = Txt("sobra " + Num.Fmt(sobraVal, 0), 11.5, P.PeloPerfil ? fgCaixa : JanelaEscura.BFraco);
                    double sxBox = c.X + 62, syBox = (yPerfB + 20 + yB) / 2;
                    Rect rSobra = new Rect(sxBox - ftSobra.Width / 2 - 6, syBox - 10, ftSobra.Width + 12, 20);
                    dc.DrawRoundedRectangle(P.PeloPerfil ? bgCaixa : TemaPlant.BFundoTextoCanvas, P.PeloPerfil ? bdCaixa : null, rSobra, 3, 3);
                    dc.DrawText(ftSobra, new Point(sxBox - ftSobra.Width / 2, syBox - ftSobra.Height / 2));
                    caixas.Add(new CaixaCota { Ret = rSobra, Valor = Num.Fmt(sobraVal, 0), Aplicar = v => { P.PeloPerfil = true; P.Sobra = Math.Max(0, v); } });
                }
            }

            // legenda
            double yLeg = ActualHeight - 18, xLeg = 16;
            void Leg(Action<Point> simbolo, string texto)
            {
                simbolo(new Point(xLeg + 6, yLeg));
                var tl = Txt(texto, 11, TemaPlant.BTexto3);
                dc.DrawText(tl, new Point(xLeg + 16, yLeg - tl.Height / 2));
                xLeg += 16 + tl.Width + 18;
            }
            if (!ModoForcas)
            {
                Leg(p => dc.DrawEllipse(null, new Pen(corAnel, 2), p, 5.5, 5.5), "fixação " + (R.ChOk ? "passa" : "não passa"));
                if (Nbr != null && Nbr.Paraf.Any(x => x[2] > 1e-9)) Leg(p => dc.DrawEllipse(null, new Pen(TemaPlant.B(TemaPlant.Erro, 230), 2), p, 5.5, 5.5), "tracionado (kN)");
            }
            else
            {
                Leg(p => dc.DrawEllipse(TemaPlant.B(TemaPlant.Erro, 220), null, p, 5.5, 5.5), "tracionado (mais escuro = mais carga)");
                Leg(p => dc.DrawEllipse(TemaPlant.B(TemaPlant.Realce, 60), null, p, 5.5, 5.5), "sem tração");
            }
            if (bloco) Leg(p => dc.DrawRectangle(TemaPlant.B(corBloco, 90), new Pen(TemaPlant.B(corBloco, 230), 1), new Rect(p.X - 6, p.Y - 4, 12, 8)), "bloco de compressão");
            Leg(p => dc.DrawRectangle(TemaPlant.BPerfilArea, new Pen(TemaPlant.BRealce, 1), new Rect(p.X - 6, p.Y - 4, 12, 8)), "pilar");
            Leg(p => dc.DrawRoundedRectangle(TemaPlant.BInfoFundo, new Pen(TemaPlant.BRealce, 1), new Rect(p.X - 7, p.Y - 5, 14, 10), 2, 2), "cota editável (mm)");
            if (!double.IsNaN(R.PivoY)) Leg(p => dc.DrawLine(new Pen(JanelaEscura.BAmarelo, 1) { DashStyle = DashStyles.Dash }, new Point(p.X - 6, p.Y), new Point(p.X + 6, p.Y)), "linha de giro");
        }
    }

    // ---------- vista 3D girável da placa: concreto translúcido, chumbadores com hef, cones 1,5·hef, solda ----------
    public class Placa3D : FrameworkElement
    {
        public ResPlaca R;
        public PlacaBase P;
        public ResSolda Sd;
        const double Yaw0 = -36, Pitch0 = 24;
        double yaw = Yaw0, pitch = Pitch0, zoom = 1;
        Point? arrasto; double yawIni, pitchIni;

        public Placa3D()
        {
            ClipToBounds = true;
            Cursor = Cursors.SizeAll;
            ToolTip = "Arraste para girar · roda do mouse = zoom · duplo clique = vista inicial";
            ToolTipService.SetInitialShowDelay(this, 900);
        }

        public void VistaInicial() { yaw = Yaw0; pitch = Pitch0; zoom = 1; InvalidateVisual(); }
        public void Girar(double yawGraus, double pitchGraus) { yaw = yawGraus; pitch = pitchGraus; InvalidateVisual(); }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2) { VistaInicial(); e.Handled = true; return; }
            arrasto = e.GetPosition(this); yawIni = yaw; pitchIni = pitch;
            CaptureMouse(); e.Handled = true;
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (arrasto == null || !IsMouseCaptured) return;
            Point p = e.GetPosition(this);
            yaw = yawIni + (p.X - arrasto.Value.X) * 0.45;
            pitch = Math.Max(-20, Math.Min(89, pitchIni + (p.Y - arrasto.Value.Y) * 0.45));
            InvalidateVisual();
        }
        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { arrasto = null; ReleaseMouseCapture(); }
        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            zoom = Math.Max(0.5, Math.Min(4, zoom * Math.Pow(1.12, e.Delta / 120.0)));
            InvalidateVisual(); e.Handled = true;
        }

        struct Item { public double Z; public Action<DrawingContext> Desenhar; }

        protected override void OnRender(DrawingContext dc)
        {
            double W = ActualWidth, Ht = ActualHeight;
            dc.DrawRectangle(TemaPlant.BBgCanvas, null, new Rect(0, 0, W, Ht));
            if (R == null || R.H <= 0 || R.B <= 0 || W < 80 || Ht < 80) return;
            double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            FormattedText Txt(string s, double tam, Brush cor, FontWeight? peso = null) =>
                new FormattedText(s ?? "", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    new Typeface(TemaPlant.Fonte, FontStyles.Normal, peso ?? FontWeights.Normal, FontStretches.Normal), tam, cor, dip);

            // ---- geometria (mm). Eixos: X = no plano do pórtico, Y = normal à placa (para cima), Z = fora do plano
            double sx = R.Espelhar ? -1 : 1;
            double maxDim = Math.Max(R.H, R.B);
            double tv = Math.Max(P != null && P.Esp > 0 ? P.Esp : 10, maxDim * 0.02);
            Chumbador ch = R.Ch;
            double hef = ch != null ? ch.Hef : Math.Max(4 * R.Dia, 40);
            double mBloco = Math.Max(ch != null ? 1.5 * ch.Hef : 0, 0.18 * maxDim);
            double hx = R.H / 2, hz = R.B / 2, bx = hx + mBloco, bz = hz + mBloco;
            double prof = Math.Max(hef * 1.55, 0.38 * maxDim);
            double dX = sx * R.BarraY, dY = R.BarraN, ld = Math.Sqrt(dX * dX + dY * dY);
            if (ld < 1e-9) { dX = 0; dY = 1; } else { dX /= ld; dY /= ld; }
            double hIn = R.RefIn, hOut = R.RefOut;
            double L = Math.Max(Math.Max(hIn, hOut) * 2.2, maxDim * 0.75);

            // seção do pilar (u no plano, v fora do plano)
            var sec = new List<double[]>(); var secIn = new List<double[]>();
            string forma = R.Sec != null ? R.Sec.Forma : "";
            double tfe = R.Sec != null ? Math.Max(R.Sec.Tf * 1000, 1) : 2, twe = R.Sec != null ? Math.Max(R.Sec.Tw * 1000, 1) : 2;
            if (forma == "TC")
            {
                for (int i = 0; i < 28; i++) { double t = 2 * Math.PI * i / 28; sec.Add(new[] { hIn / 2 * Math.Cos(t), hIn / 2 * Math.Sin(t) }); }
                double ri = Math.Max(0, hIn / 2 - twe);
                for (int i = 0; i < 28; i++) { double t = 2 * Math.PI * i / 28; secIn.Add(new[] { ri * Math.Cos(t), ri * Math.Sin(t) }); }
            }
            else if (forma == "I" && !R.Deitado)
            {
                double h = hIn, b = hOut, tf = tfe, tw = twe;
                foreach (var q in new[] { new[] { -h / 2, -b / 2 }, new[] { -h / 2 + tf, -b / 2 }, new[] { -h / 2 + tf, -tw / 2 }, new[] { h / 2 - tf, -tw / 2 }, new[] { h / 2 - tf, -b / 2 }, new[] { h / 2, -b / 2 },
                                          new[] { h / 2, b / 2 }, new[] { h / 2 - tf, b / 2 }, new[] { h / 2 - tf, tw / 2 }, new[] { -h / 2 + tf, tw / 2 }, new[] { -h / 2 + tf, b / 2 }, new[] { -h / 2, b / 2 } }) sec.Add(q);
            }
            else if (forma == "I")
            {
                double h = hIn, b = hOut, tf = tfe, tw = twe;
                foreach (var q in new[] { new[] { -h / 2, -b / 2 }, new[] { h / 2, -b / 2 }, new[] { h / 2, -b / 2 + tf }, new[] { tw / 2, -b / 2 + tf }, new[] { tw / 2, b / 2 - tf }, new[] { h / 2, b / 2 - tf },
                                          new[] { h / 2, b / 2 }, new[] { -h / 2, b / 2 }, new[] { -h / 2, b / 2 - tf }, new[] { -tw / 2, b / 2 - tf }, new[] { -tw / 2, -b / 2 + tf }, new[] { -h / 2, -b / 2 + tf } }) sec.Add(q);
            }
            else if (forma == "U")   // W12-OUT: o U com a alma de um lado e as mesas para o outro (como na planta), não uma caixa
            {
                double h = hIn, b = hOut, tf = tfe, tw = twe;
                if (!R.Deitado)
                    foreach (var q in new[] { new[] { -h / 2, b / 2 }, new[] { -h / 2 + tf, b / 2 }, new[] { -h / 2 + tf, -b / 2 + tw }, new[] { h / 2 - tf, -b / 2 + tw },
                                              new[] { h / 2 - tf, b / 2 }, new[] { h / 2, b / 2 }, new[] { h / 2, -b / 2 }, new[] { -h / 2, -b / 2 } }) sec.Add(q);
                else
                    foreach (var q in new[] { new[] { -h / 2, b / 2 }, new[] { h / 2, b / 2 }, new[] { h / 2, b / 2 - tf }, new[] { -h / 2 + tw, b / 2 - tf },
                                              new[] { -h / 2 + tw, -b / 2 + tf }, new[] { h / 2, -b / 2 + tf }, new[] { h / 2, -b / 2 }, new[] { -h / 2, -b / 2 } }) sec.Add(q);
            }
            else
            {
                foreach (var q in new[] { new[] { -1.0, -1.0 }, new[] { 1.0, -1.0 }, new[] { 1.0, 1.0 }, new[] { -1.0, 1.0 } }) sec.Add(new[] { q[0] * hIn / 2, q[1] * hOut / 2 });
                if (forma == "TR")
                    foreach (var q in new[] { new[] { -1.0, -1.0 }, new[] { 1.0, -1.0 }, new[] { 1.0, 1.0 }, new[] { -1.0, 1.0 } }) secIn.Add(new[] { q[0] * Math.Max(0, hIn / 2 - twe), q[1] * Math.Max(0, hOut / 2 - twe) });
            }
            double e1X = dY, e1Y = -dX;   // eixo u da seção, perpendicular à barra no plano do pórtico
            List<double[]> Base(List<double[]> s)
            {
                var l = new List<double[]>();
                foreach (var q in s) { double X = q[0] * e1X, Y = q[0] * e1Y; double t0 = Math.Abs(dY) > 1e-6 ? -Y / dY : 0; l.Add(new[] { X + t0 * dX, tv, q[1] }); }
                return l;
            }
            List<double[]> Topo(List<double[]> s)
            {
                var l = new List<double[]>();
                foreach (var q in s) l.Add(new[] { q[0] * e1X + L * dX, tv + q[0] * e1Y + L * dY, q[1] });
                return l;
            }
            var baseP = Base(sec); var topoP = Topo(sec); var topoIn = Topo(secIn);

            // ---- projeção ortográfica girável, escala pela esfera envolvente (não "pulsa" ao girar)
            var pts = new List<double[]>();
            foreach (double x in new[] { -bx, bx }) foreach (double y in new[] { -prof, tv }) foreach (double z in new[] { -bz, bz }) pts.Add(new[] { x, y, z });
            pts.AddRange(topoP);
            double cxm = (pts.Min(p => p[0]) + pts.Max(p => p[0])) / 2, cym = (pts.Min(p => p[1]) + pts.Max(p => p[1])) / 2, czm = (pts.Min(p => p[2]) + pts.Max(p => p[2])) / 2;
            double raio = pts.Max(p => Math.Sqrt((p[0] - cxm) * (p[0] - cxm) + (p[1] - cym) * (p[1] - cym) + (p[2] - czm) * (p[2] - czm)));
            double k = Math.Min(W, Ht - 30) * 0.47 / Math.Max(1e-6, raio) * zoom;
            double ox = W / 2, oy = (Ht - 26) / 2 + 4;
            double cyw = Math.Cos(yaw * Math.PI / 180), syw = Math.Sin(yaw * Math.PI / 180), cp = Math.Cos(pitch * Math.PI / 180), spt = Math.Sin(pitch * Math.PI / 180);
            Point Pr(double x, double y, double z, out double prof2)
            {
                x -= cxm; y -= cym; z -= czm;
                double x1 = x * cyw + z * syw, z1 = -x * syw + z * cyw;
                double y2 = y * cp - z1 * spt, z2 = y * spt + z1 * cp;
                prof2 = z2;
                return new Point(ox + x1 * k, oy - y2 * k);
            }
            Point P2(double x, double y, double z) { double d; return Pr(x, y, z, out d); }
            Point Pv(double[] v) { return P2(v[0], v[1], v[2]); }
            double Zv(double x, double y, double z) { double d; Pr(x, y, z, out d); return d; }
            double NzNormal(double nx, double ny, double nz) { double z1 = -nx * syw + nz * cyw; return ny * spt + z1 * cp; }

            StreamGeometry Geo(IList<Point> p, bool fechar = true)
            {
                var g = new StreamGeometry();
                using (var c = g.Open()) { c.BeginFigure(p[0], fechar, fechar); c.PolyLineTo(p.Skip(1).ToList(), true, true); }
                g.Freeze();
                return g;
            }

            // ---- 1) bloco de concreto: faces de trás, chumbadores/cones, faces da frente (translúcidas)
            var corConc = TemaPlant.B(TemaPlant.Texto2, 26);
            var corConcTopo = TemaPlant.B(TemaPlant.Texto2, 34);
            var penConc = new Pen(TemaPlant.B(TemaPlant.Texto2, 110), 1);
            var penConcTr = new Pen(TemaPlant.B(TemaPlant.Texto2, 70), 1) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) };
            var facesBloco = new List<(double[][] q, double[] nrm)>
            {
                (new[] { new[] { -bx, 0, -bz }, new[] { bx, 0, -bz }, new[] { bx, 0, bz }, new[] { -bx, 0, bz } }, new double[] { 0, 1, 0 }),
                (new[] { new[] { -bx, -prof, -bz }, new[] { bx, -prof, -bz }, new[] { bx, -prof, bz }, new[] { -bx, -prof, bz } }, new double[] { 0, -1, 0 }),
                (new[] { new[] { -bx, -prof, bz }, new[] { bx, -prof, bz }, new[] { bx, 0, bz }, new[] { -bx, 0, bz } }, new double[] { 0, 0, 1 }),
                (new[] { new[] { -bx, -prof, -bz }, new[] { bx, -prof, -bz }, new[] { bx, 0, -bz }, new[] { -bx, 0, -bz } }, new double[] { 0, 0, -1 }),
                (new[] { new[] { bx, -prof, -bz }, new[] { bx, -prof, bz }, new[] { bx, 0, bz }, new[] { bx, 0, -bz } }, new double[] { 1, 0, 0 }),
                (new[] { new[] { -bx, -prof, -bz }, new[] { -bx, -prof, bz }, new[] { -bx, 0, bz }, new[] { -bx, 0, -bz } }, new double[] { -1, 0, 0 }),
            };
            foreach (var f in facesBloco.Where(f => NzNormal(f.nrm[0], f.nrm[1], f.nrm[2]) <= 0))
                dc.DrawGeometry(corConc, penConcTr, Geo(f.q.Select(Pv).ToList()));

            Brush corF = R.ChOk ? JanelaEscura.BVerde : JanelaEscura.BVermelho;
            var penHaste = new Pen(corF, Math.Max(1.6, Math.Min(4, R.Dia * k * 0.6)));
            var penCone = new Pen(TemaPlant.B(TemaPlant.Realce, 170), 1) { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) };
            var fillCone = TemaPlant.B(TemaPlant.Realce, 22);
            var furos = R.Paraf.Select(p => new[] { sx * p[0], p[1] }).ToList();
            // cones de arrancamento (só chumbador)
            if (ch != null)
                foreach (var f in furos)
                {
                    double rc = 1.5 * ch.Hef;
                    var circ = new List<Point>();
                    for (int i = 0; i < 36; i++) { double t = 2 * Math.PI * i / 36; circ.Add(P2(f[0] + rc * Math.Cos(t), 0, f[1] + rc * Math.Sin(t))); }
                    Point ap = P2(f[0], -ch.Hef, f[1]), cc = P2(f[0], 0, f[1]);
                    Vector eixo = cc - ap; if (eixo.Length < 1e-6) eixo = new Vector(0, -1); eixo.Normalize();
                    Vector nor = new Vector(-eixo.Y, eixo.X);
                    Point pMin = circ[0], pMax = circ[0]; double dMin = double.MaxValue, dMax = double.MinValue;
                    foreach (var q in circ) { double dd = Vector.Multiply(q - ap, nor); if (dd < dMin) { dMin = dd; pMin = q; } if (dd > dMax) { dMax = dd; pMax = q; } }
                    dc.DrawGeometry(fillCone, null, Geo(new List<Point> { ap, pMin, pMax }));
                    dc.DrawGeometry(fillCone, penCone, Geo(circ));
                    dc.DrawLine(penCone, ap, pMin); dc.DrawLine(penCone, ap, pMax);
                }
            foreach (var f in furos) dc.DrawLine(penHaste, P2(f[0], 0, f[1]), P2(f[0], -hef, f[1]));

            foreach (var f in facesBloco.Where(f => NzNormal(f.nrm[0], f.nrm[1], f.nrm[2]) > 0))
                dc.DrawGeometry(f.nrm[1] > 0 ? corConcTopo : corConc, penConc, Geo(f.q.Select(Pv).ToList()));

            // cota hef no chumbador mais à frente (o rótulo é desenhado por último, com anti-sobreposição)
            FormattedText thHef = null; Rect rHef = Rect.Empty;
            if (furos.Count > 0)
            {
                var fr = furos.OrderByDescending(f => Zv(f[0], -hef / 2, f[1])).First();
                Point a = P2(fr[0], 0, fr[1]), b = P2(fr[0], -hef, fr[1]);
                Vector dir = b - a; Vector off = dir.Length > 1e-6 ? new Vector(-dir.Y, dir.X) : new Vector(1, 0); off.Normalize(); if (off.X < 0) off = -off; off *= 16;
                var penCota = new Pen(TemaPlant.BTexto2, 1);
                dc.DrawLine(penCota, a + off, b + off);
                dc.DrawLine(penCota, a + off * 0.6, a + off * 1.4); dc.DrawLine(penCota, b + off * 0.6, b + off * 1.4);
                var th = Txt((ch != null ? "hef " : "embutido ") + Num.Fmt(hef, 0) + " mm", 11, TemaPlant.BTexto1, FontWeights.SemiBold);
                Point pm = a + off * 1.5 + (b - a) / 2;
                thHef = th; rHef = new Rect(pm.X, pm.Y - th.Height / 2 - 2, th.Width + 10, th.Height + 4);
            }

            // ---- 2) placa, porcas e pilar com ordenação por profundidade
            var itens = new List<Item>();
            var penPl = new Pen(TemaPlant.BPlacaContorno, 1.2);
            var facesPl = new List<(double[][] q, double[] nrm, Brush b)>
            {
                (new[] { new[] { -hx, tv, -hz }, new[] { hx, tv, -hz }, new[] { hx, tv, hz }, new[] { -hx, tv, hz } }, new double[] { 0, 1, 0 }, TemaPlant.BPlacaTopo),
                (new[] { new[] { -hx, 0, -hz }, new[] { hx, 0, -hz }, new[] { hx, 0, hz }, new[] { -hx, 0, hz } }, new double[] { 0, -1, 0 }, TemaPlant.BPlacaFace2),
                (new[] { new[] { -hx, 0, hz }, new[] { hx, 0, hz }, new[] { hx, tv, hz }, new[] { -hx, tv, hz } }, new double[] { 0, 0, 1 }, TemaPlant.BPlacaFace1),
                (new[] { new[] { -hx, 0, -hz }, new[] { hx, 0, -hz }, new[] { hx, tv, -hz }, new[] { -hx, tv, -hz } }, new double[] { 0, 0, -1 }, TemaPlant.BPlacaFace1),
                (new[] { new[] { hx, 0, -hz }, new[] { hx, 0, hz }, new[] { hx, tv, hz }, new[] { hx, tv, -hz } }, new double[] { 1, 0, 0 }, TemaPlant.BPlacaFace2),
                (new[] { new[] { -hx, 0, -hz }, new[] { -hx, 0, hz }, new[] { -hx, tv, hz }, new[] { -hx, tv, -hz } }, new double[] { -1, 0, 0 }, TemaPlant.BPlacaFace2),
            };
            foreach (var f in facesPl)
            {
                if (NzNormal(f.nrm[0], f.nrm[1], f.nrm[2]) <= 0) continue;
                var q = f.q; var br = f.b;
                dc.DrawGeometry(br, penPl, Geo(q.Select(Pv).ToList()));
            }
            // porcas/cabeças sobre a placa
            double hPorca = Math.Max(R.Dia * 0.9, tv * 0.6), rPorca = Math.Max(R.Dia, 4);
            foreach (var f in furos)
            {
                double fx = f[0], fz = f[1];
                itens.Add(new Item
                {
                    Z = Zv(fx, tv, fz),
                    Desenhar = d =>
                    {
                        var hexB = new List<Point>(); var hexT = new List<Point>();
                        for (int i = 0; i < 6; i++) { double t = Math.PI / 6 + Math.PI * i / 3; hexB.Add(P2(fx + rPorca * Math.Cos(t), tv, fz + rPorca * Math.Sin(t))); hexT.Add(P2(fx + rPorca * Math.Cos(t), tv + hPorca, fz + rPorca * Math.Sin(t))); }
                        var penP = new Pen(corF, 1);
                        for (int i = 0; i < 6; i++) { int j = (i + 1) % 6; d.DrawGeometry(TemaPlant.B(TemaPlant.Surface3, 255), penP, Geo(new List<Point> { hexB[i], hexB[j], hexT[j], hexT[i] })); }
                        d.DrawGeometry(TemaPlant.BSup2, penP, Geo(hexT));
                        d.DrawLine(new Pen(corF, 2), P2(fx, tv + hPorca, fz), P2(fx, tv + hPorca * 1.8, fz));
                    }
                });
            }
            // pilar: faces laterais e tampa
            var cheio = TemaPlant.BPilar3DFace;
            var penAz = new Pen(TemaPlant.BRealce, 1);
            for (int i = 0; i < baseP.Count; i++)
            {
                int j = (i + 1) % baseP.Count;
                var q = new[] { baseP[i], baseP[j], topoP[j], topoP[i] };
                double zm = q.Average(v => Zv(v[0], v[1], v[2]));
                // sombreamento simples pela orientação da face em relação à luz (de cima/esquerda)
                double ux = baseP[j][0] - baseP[i][0], uz = baseP[j][2] - baseP[i][2];
                double lum = Math.Abs(Math.Atan2(uz, ux)) / Math.PI;
                byte a = (byte)(150 + 70 * lum);
                Brush fb = TemaPlant.B(TemaPlant.AcentoPressed, a);
                Pen pf = baseP.Count > 12 ? new Pen(fb, 0.8) : penAz;   // seção redonda: sem riscar cada faceta
                itens.Add(new Item { Z = zm, Desenhar = d => d.DrawGeometry(fb, pf, Geo(q.Select(Pv).ToList())) });
            }
            if (baseP.Count > 12)
            {
                // contorno (silhueta) do tubo redondo: geratrizes extremas na tela
                var pb = baseP.Select(Pv).ToList(); var pt = topoP.Select(Pv).ToList();
                Vector ax = (pt[0] - pb[0]); if (ax.Length < 1e-6) ax = new Vector(0, -1); ax.Normalize();
                Vector nr = new Vector(-ax.Y, ax.X);
                int iMin = 0, iMax = 0;
                for (int i = 1; i < pb.Count; i++) { if (Vector.Multiply(pb[i] - pb[0], nr) < Vector.Multiply(pb[iMin] - pb[0], nr)) iMin = i; if (Vector.Multiply(pb[i] - pb[0], nr) > Vector.Multiply(pb[iMax] - pb[0], nr)) iMax = i; }
                double zs = itens.Count > 0 ? itens.Max(x => x.Z) + 1 : 0;
                int a1 = iMin, a2 = iMax;
                itens.Add(new Item { Z = zs, Desenhar = d => { d.DrawLine(penAz, pb[a1], pt[a1]); d.DrawLine(penAz, pb[a2], pt[a2]); } });
            }
            double zTopo = topoP.Average(v => Zv(v[0], v[1], v[2]));
            itens.Add(new Item
            {
                Z = zTopo,
                Desenhar = d =>
                {
                    d.DrawGeometry(TemaPlant.BPilar3DTopo, penAz, Geo(topoP.Select(Pv).ToList()));
                    if (topoIn.Count > 2) d.DrawGeometry(TemaPlant.BBgCanvas, penAz, Geo(topoIn.Select(Pv).ToList()));
                }
            });
            foreach (var it in itens.OrderBy(x => x.Z)) it.Desenhar(dc);

            // solda: cordão em todo o contorno do pé do pilar
            var corSolda = TemaPlant.BMomentoNeg;
            bool sdOk = Sd == null || Sd.Passa;
            var penSolda = new Pen(sdOk ? corSolda : JanelaEscura.BVermelho, 3.2) { LineJoin = PenLineJoin.Round };
            dc.DrawGeometry(null, penSolda, Geo(baseP.Select(Pv).ToList()));
            Rect rSolda = Rect.Empty;
            if (Sd != null && Sd.Erro == "")
            {
                Point pS = baseP.Select(Pv).OrderByDescending(p => p.Y).First();
                rSolda = new Rect(pS.X + 10, pS.Y + 4, 0, 0);
                var ts = Txt("solda " + Num.Fmt(Sd.H, 0) + " mm", 11, sdOk ? corSolda : JanelaEscura.BVermelho, FontWeights.SemiBold);
                rSolda = new Rect(pS.X + 10, pS.Y + 4, ts.Width + 10, ts.Height + 4);
                dc.DrawRoundedRectangle(TemaPlant.BFundoTextoCanvas, null, rSolda, 3, 3);
                dc.DrawText(ts, new Point(pS.X + 15, pS.Y + 6));
            }
            if (thHef != null)
            {
                // anti-sobreposição: se encostar na solda, desce o rótulo hef para baixo dela
                if (!rSolda.IsEmpty && rSolda.Width > 0 && rHef.IntersectsWith(Rect.Inflate(rSolda, 4, 4))) rHef.Y = rSolda.Bottom + 6;
                dc.DrawRoundedRectangle(TemaPlant.BFundoTextoCanvas, new Pen(TemaPlant.BBordaSutil, 1), rHef, 3, 3);
                dc.DrawText(thHef, new Point(rHef.X + 5, rHef.Y + 2));
            }

            // eixo do pilar, normal da placa e ângulo
            dc.DrawLine(new Pen(TemaPlant.BTexto1, 1) { DashStyle = new DashStyle(new double[] { 8, 3, 1, 3 }, 0) }, P2(0, tv, 0), P2(L * dX * 1.12, tv + L * dY * 1.12, 0));
            if (R.Inclinada)
            {
                var cinza = new Pen(TemaPlant.BTexto2, 1) { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) };
                dc.DrawLine(cinza, P2(0, tv, 0), P2(0, tv + L * 1.05, 0));
                double ra = L * 0.5, ang = Math.Atan2(dX, dY);
                var arco = new List<Point>();
                for (int i = 0; i <= 24; i++) { double t = ang * i / 24; arco.Add(P2(ra * Math.Sin(t), tv + ra * Math.Cos(t), 0)); }
                dc.DrawGeometry(null, new Pen(JanelaEscura.BAmarelo, 2), Geo(arco, false));
                var tA = Txt(Num.Fmt(R.AngNormal, 1) + "°", 12.5, JanelaEscura.BAmarelo, FontWeights.SemiBold);
                Point pm = P2(ra * 1.18 * Math.Sin(ang / 2), tv + ra * 1.18 * Math.Cos(ang / 2), 0);
                dc.DrawText(tA, new Point(pm.X - tA.Width / 2, pm.Y - tA.Height / 2));
            }

            // tríade de eixos (canto inferior direito)
            Point o3 = new Point(W - 54, Ht - 62);
            void Eixo(double x, double y, double z, string nome, Brush cor)
            {
                double z1 = -x * syw + z * cyw, x1 = x * cyw + z * syw, y2 = y * cp - z1 * spt;
                Point pe = new Point(o3.X + x1 * 26, o3.Y - y2 * 26);
                dc.DrawLine(new Pen(cor, 1.5), o3, pe);
                var tt = Txt(nome, 10, cor, FontWeights.SemiBold);
                dc.DrawText(tt, new Point(pe.X + (pe.X >= o3.X ? 2 : -tt.Width - 2), pe.Y - tt.Height / 2));
            }
            Eixo(1, 0, 0, "x", TemaPlant.BEixoX); Eixo(0, 1, 0, "y", TemaPlant.BEixoY); Eixo(0, 0, 1, "z", TemaPlant.BRealce);

            // legenda e dica
            double yLeg = Ht - 16, xLeg = 14;
            void Leg(Action<Point> simbolo, string texto)
            {
                simbolo(new Point(xLeg + 7, yLeg));
                var tl = Txt(texto, 11, TemaPlant.BTexto3);
                dc.DrawText(tl, new Point(xLeg + 18, yLeg - tl.Height / 2));
                xLeg += 18 + tl.Width + 16;
            }
            Leg(p => dc.DrawRectangle(corConcTopo, penConc, new Rect(p.X - 7, p.Y - 5, 14, 10)), ch != null ? "concreto" : "base");
            if (ch != null) Leg(p => dc.DrawLine(penCone, new Point(p.X - 7, p.Y + 4), new Point(p.X + 7, p.Y - 4)), "cone de arrancamento (raio 1,5·hef)");
            Leg(p => dc.DrawLine(new Pen(corF, 2.5), new Point(p.X, p.Y - 6), new Point(p.X, p.Y + 6)), "fixação " + (R.ChOk ? "passa" : "não passa"));
            Leg(p => dc.DrawLine(new Pen(corSolda, 3), new Point(p.X - 7, p.Y), new Point(p.X + 7, p.Y)), "solda");
            if (R.Inclinada)
            {
                var tN = Txt("barra inclinada " + Num.Fmt(R.AngNormal, 1) + "° da normal da placa", 11, JanelaEscura.BAmarelo);
                dc.DrawText(tN, new Point(14, 10));
            }
        }
    }
}
