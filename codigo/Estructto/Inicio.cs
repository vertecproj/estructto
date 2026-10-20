// Tela inicial (modelo vazio): cartoes com miniatura vetorial do modelo, recentes com miniatura, atalhos.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Portico;
using Ui = NovoFtool.UiPlaca;

namespace NovoFtool
{
    // Desenho vetorial reduzido de um modelo (barras, apoios, cargas), usado nas miniaturas.
    public static class Miniatura
    {
        static readonly Dictionary<string, ImageSource> cache = new Dictionary<string, ImageSource>();

        public static ImageSource Gerar(string chave, Func<Modelo> fonte, double w, double h)
        {
            string k = chave + "|" + w + "x" + h;
            ImageSource r;
            if (cache.TryGetValue(k, out r)) return r;
            r = Desenhar(fonte, w, h);
            cache[k] = r;
            return r;
        }

        static ImageSource Desenhar(Func<Modelo> fonte, double w, double h)
        {
            Modelo m;
            try { m = fonte(); } catch { return null; }
            if (m == null || m.Nos.Count == 0) return null;
            double x0 = m.Nos.Min(n => n.X), x1 = m.Nos.Max(n => n.X), y0 = m.Nos.Min(n => n.Y), y1 = m.Nos.Max(n => n.Y);
            double dx = Math.Max(x1 - x0, 1e-6), dy = Math.Max(y1 - y0, 1e-6), pad = Math.Max(7, Math.Min(w, h) * 0.13);
            double e = Math.Min((w - 2 * pad) / dx, (h - 2 * pad - 4) / dy);
            double ox = (w - dx * e) / 2, oy = (h - dy * e) / 2 - 2;
            Func<No, Point> P = n => new Point(ox + (n.X - x0) * e, h - oy - (n.Y - y0) * e);
            var barraPen = new Pen(TemaPlant.BTexto1, 1.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            var cargaPen = new Pen(TemaPlant.BAlerta, 1.2);
            var apoioPen = new Pen(TemaPlant.BApoio, 1.3) { LineJoin = PenLineJoin.Round };
            barraPen.Freeze(); cargaPen.Freeze(); apoioPen.Freeze();
            var tudo = new GeometryGroup(); var cargas = new GeometryGroup(); var apoios = new GeometryGroup();
            foreach (Barra b in m.Barras)
            {
                No a = m.NoPorId(b.N1), c = m.NoPorId(b.N2);
                if (a == null || c == null) continue;
                Point p = P(a), q = P(c);
                tudo.Children.Add(new LineGeometry(p, q));
                if (Math.Abs(b.QGlobalY) > 1e-9 || Math.Abs(b.QGlobalX) > 1e-9)
                    cargas.Children.Add(new LineGeometry(new Point(p.X, p.Y - 5), new Point(q.X, q.Y - 5)));
            }
            double s = Math.Max(3.5, Math.Min(6, Math.Min(w, h) * 0.07));
            foreach (No n in m.Nos)
            {
                if (!n.TemApoio) continue;
                Point p = P(n);
                if (n.ApoioX && n.ApoioY && n.ApoioGiro)
                {
                    apoios.Children.Add(new LineGeometry(new Point(p.X - s, p.Y + 1), new Point(p.X + s, p.Y + 1)));
                    apoios.Children.Add(new LineGeometry(new Point(p.X - s, p.Y + 1 + s * 0.7), new Point(p.X + s, p.Y + 1 + s * 0.7)));
                }
                else
                {
                    var f = new StreamGeometry();
                    using (var cx = f.Open()) { cx.BeginFigure(p, false, true); cx.LineTo(new Point(p.X - s, p.Y + s * 1.4), true, false); cx.LineTo(new Point(p.X + s, p.Y + s * 1.4), true, false); }
                    f.Freeze(); apoios.Children.Add(f);
                }
            }
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, w, h))));
            g.Children.Add(new GeometryDrawing(null, apoioPen, apoios));
            g.Children.Add(new GeometryDrawing(null, cargaPen, cargas));
            g.Children.Add(new GeometryDrawing(null, barraPen, tudo));
            g.Freeze();
            var di = new DrawingImage(g); di.Freeze();
            return di;
        }
    }

    public partial class MainWindow
    {
        static string QuandoTxt(DateTime t)
        {
            var d = DateTime.Now.Date - t.Date;
            return d.Days == 0 ? "hoje " + t.ToString("HH:mm") : d.Days == 1 ? "ontem " + t.ToString("HH:mm") : t.ToString("dd/MM/yyyy");
        }

        static Border Quadro(ImageSource img, double w, double h, string vazioIcone = "barra")
        {
            FrameworkElement conteudo;
            if (img != null) conteudo = new Image { Source = img, Width = w, Height = h, Stretch = Stretch.None, SnapsToDevicePixels = true };
            else
            {
                var ic = (FrameworkElement)Icone(vazioIcone, 20); ic.HorizontalAlignment = HorizontalAlignment.Center; ic.VerticalAlignment = VerticalAlignment.Center; ic.Opacity = 0.5;
                var g = new Grid { Width = w, Height = h }; g.Children.Add(ic); conteudo = g;
            }
            return new Border { Background = TemaPlant.BSup0, BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Child = conteudo, HorizontalAlignment = HorizontalAlignment.Center };
        }

        Button CartaoBotao(UIElement conteudo, Action acao, string dica, Thickness pad, string nomeAcessivel = null)
        {
            var b = new Button { Content = conteudo, Style = (Style)FindResource("CartaoInicio"), Padding = pad, ToolTip = dica };
            b.Click += (s, e) => { Bastidor.IsOpen = false; acao(); };
            System.Windows.Automation.AutomationProperties.SetName(b, nomeAcessivel ?? dica);
            return b;
        }

        Button CartaoAssistente(string titulo, string sub, string tipo, Func<Modelo> modelo, bool compacto = false)
        {
            if (compacto)
            {
                var spc = new StackPanel();
                spc.Children.Add(new TextBlock { Text = titulo, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = TemaPlant.BTexto1, TextTrimming = TextTrimming.CharacterEllipsis });
                spc.Children.Add(new TextBlock { Text = sub, FontSize = 11, Foreground = TemaPlant.BTexto3, TextTrimming = TextTrimming.CharacterEllipsis });
                return CartaoBotao(spc, () => AbrirAssistente(tipo), titulo + " (" + sub + "): abre o assistente e mostra a estrutura ao vivo enquanto você muda vãos, alturas e cargas.", new Thickness(10, 5, 10, 5), titulo);
            }
            var sp = new StackPanel();
            sp.Children.Add(Quadro(Miniatura.Gerar("ass-" + tipo, modelo, 132, 72), 132, 72));
            sp.Children.Add(new TextBlock { Text = titulo, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = TemaPlant.BTexto1, Margin = new Thickness(0, 8, 0, 1), TextTrimming = TextTrimming.CharacterEllipsis });
            sp.Children.Add(new TextBlock { Text = sub, FontSize = 11, Foreground = TemaPlant.BTexto3, TextTrimming = TextTrimming.CharacterEllipsis });
            return CartaoBotao(sp, () => AbrirAssistente(tipo), titulo + " (" + sub + "): abre o assistente e mostra a estrutura ao vivo enquanto você muda vãos, alturas e cargas.", new Thickness(10), titulo);
        }

        internal static List<(string titulo, string sub, string tipo, Func<Modelo> modelo)> AssistentesInicio()
        {
            return new List<(string, string, string, Func<Modelo>)>
            {
                ("Viga contínua", "vãos e apoios", "viga", () => Assistentes.Viga(new List<double> { 5, 5, 5 }, -10, false, "", "")),
                ("Pórtico", "vários andares", "portico", () => Assistentes.Portico(new List<double> { 5, 5 }, new List<double> { 3.5, 3 }, true, -15, 5, "", "", "")),
                ("Treliça", "Pratt, Howe, Warren", "trelica", () => Assistentes.Trelica("Pratt", 12, 2, 6, -5, "", "")),
                ("Galpão", "duas águas", "galpao", () => Assistentes.Galpao(12, 5, 2, -8, true, "", "", "")),
            };
        }

        Button CartaoRecente(string caminho, bool compacto = false)
        {
            double tw = compacto ? 48 : 76, th = compacto ? 32 : 50;
            bool existe = File.Exists(caminho);
            string nome = Path.GetFileNameWithoutExtension(caminho), pasta = Path.GetDirectoryName(caminho) ?? "";
            ImageSource img = null;
            if (existe)
            {
                var info = new FileInfo(caminho);
                img = Miniatura.Gerar(caminho + info.LastWriteTimeUtc.Ticks, () => Modelo.DeXml(File.ReadAllText(caminho)), tw, th);
            }
            var dp = new DockPanel();
            var q = Quadro(img, tw, th, "abrir"); q.Margin = new Thickness(0, 0, 10, 0); DockPanel.SetDock(q, Dock.Left); dp.Children.Add(q);
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(new TextBlock { Text = nome, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = existe ? TemaPlant.BTexto1 : TemaPlant.BTexto3, TextTrimming = TextTrimming.CharacterEllipsis });
            if (existe)
                sp.Children.Add(new TextBlock { Text = QuandoTxt(File.GetLastWriteTime(caminho)) + "  ·  " + Path.GetFileName(pasta), FontSize = 11, Foreground = TemaPlant.BTexto3, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) });
            else
            {
                var chip = Ui.Chip("arquivo não encontrado", Ui.Nivel.Alerta, "O arquivo foi movido ou apagado.");
                chip.HorizontalAlignment = HorizontalAlignment.Left; chip.Margin = new Thickness(0, 3, 0, 0);
                sp.Children.Add(chip);
            }
            dp.Children.Add(sp);
            return CartaoBotao(dp, () => Abrir(caminho), (existe ? "Abre " : "Tenta abrir ") + caminho, compacto ? new Thickness(6) : new Thickness(8), nome);
        }

        static TextBlock Rotulo(string t, Thickness m) { return new TextBlock { Text = t, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = TemaPlant.BTexto2, Margin = m }; }

        static Border Tecla(string t)
        {
            return new Border
            {
                Background = TemaPlant.BSup3, BorderBrush = TemaPlant.BBorda, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 1, 6, 1), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = t, FontSize = 10.5, Foreground = TemaPlant.BTexto2, FontFamily = TemaPlant.FonteMono }
            };
        }

        bool inicioCompacto, inicioHookado;

        internal void MontarTelaInicial()
        {
            if (!inicioHookado)
            {
                inicioHookado = true;
                // em janelas baixas (ex.: 1280x720) o topo da tela inicial fica mais enxuto
                ConteudoInicio.SizeChanged += (s, e) =>
                {
                    bool c = ConteudoInicio.ActualHeight > 0 && ConteudoInicio.ActualHeight < 400;
                    if (c != inicioCompacto && TelaInicial.Visibility == Visibility.Visible) { inicioCompacto = c; MontarTelaInicial(); }
                    else inicioCompacto = c;
                };
            }
            bool comp = inicioCompacto;
            var raiz = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 940, Margin = new Thickness(24, comp ? 8 : 16, 24, comp ? 6 : 12) };

            // cabecalho (so quando ha altura)
            if (!comp)
            {
                var cab = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
                var logo = new Image { Source = TemaPlant.Selo(), Width = 36, Height = 36, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
                DockPanel.SetDock(logo, Dock.Left); cab.Children.Add(logo);
                var tit = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                tit.Children.Add(new TextBlock { Text = "Estructto", FontSize = 24, FontWeight = FontWeights.Light, Foreground = TemaPlant.BTexto1 });
                tit.Children.Add(new TextBlock { Text = "Análise e verificação de pórticos planos", FontSize = 12, Foreground = TemaPlant.BTexto2 });
                cab.Children.Add(tit);
                raiz.Children.Add(cab);
            }

            // faixa de destaque: modelo em branco
            var hero = new DockPanel { LastChildFill = true };
            var ib = (FrameworkElement)Icone("barra", 28); ib.Margin = new Thickness(2, 0, 12, 0); ib.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(ib, Dock.Left); hero.Children.Add(ib);
            var atalhoB = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 2, 0) };
            atalhoB.Children.Add(Tecla("B"));
            atalhoB.Children.Add(new TextBlock { Text = "  e clique dois pontos", FontSize = 11, Foreground = TemaPlant.BTexto2, VerticalAlignment = VerticalAlignment.Center });
            DockPanel.SetDock(atalhoB, Dock.Right); hero.Children.Add(atalhoB);
            var ht = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            ht.Children.Add(new TextBlock { Text = "Modelo em branco", FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = TemaPlant.BTexto1 });
            if (!comp) ht.Children.Add(new TextBlock { Text = "Desenhe barras com o mouse ou digite as coordenadas.", FontSize = 11.5, Foreground = TemaPlant.BTexto2, TextTrimming = TextTrimming.CharacterEllipsis });
            hero.Children.Add(ht);
            var bh = CartaoBotao(hero, () => { TelaInicial.Visibility = Visibility.Collapsed; AcionarFerramenta(Ferramenta.Barra); }, "Começa um modelo em branco e ativa o desenho de barras (tecla B).", comp ? new Thickness(14, 6, 14, 6) : new Thickness(14, 10, 14, 10), "Modelo em branco");
            bh.Style = EstiloHero();
            bh.Margin = new Thickness(0, 0, 0, comp ? 8 : 16);
            raiz.Children.Add(bh);

            // assistentes
            raiz.Children.Add(Rotulo("OU PARTA DE UM ASSISTENTE", new Thickness(0, 0, 0, 6)));
            var ass = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, comp ? 6 : 14) };
            foreach (var a in AssistentesInicio()) { var c = CartaoAssistente(a.titulo, a.sub, a.tipo, a.modelo, comp); c.Margin = new Thickness(0, 0, 8, 0); ass.Children.Add(c); }
            raiz.Children.Add(ass);

            // recentes
            var rec = Config.Recentes().Take(comp ? 3 : 6).ToList();
            raiz.Children.Add(Rotulo("ARQUIVOS RECENTES", new Thickness(0, 0, 0, 6)));
            if (rec.Count == 0)
            {
                var vazio = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 10) };
                var iv = (FrameworkElement)Icone("abrir", 20); iv.Opacity = 0.6; iv.Margin = new Thickness(0, 0, 8, 0); vazio.Children.Add(iv);
                vazio.Children.Add(new TextBlock { Text = "Nenhum arquivo aberto ainda. Os modelos que você abrir ou salvar aparecem aqui.", FontSize = 12, Foreground = TemaPlant.BTexto3, VerticalAlignment = VerticalAlignment.Center });
                raiz.Children.Add(vazio);
            }
            else
            {
                var g = new UniformGrid { Columns = 3 };
                foreach (string c in rec) { var cartao = CartaoRecente(c, comp); cartao.Margin = new Thickness(0, 0, 8, comp ? 4 : 8); g.Children.Add(cartao); }
                raiz.Children.Add(g);
            }

            // rodape: abrir/importar + atalhos
            var rod = new DockPanel { Margin = new Thickness(0, comp ? 0 : 4, 0, 0) };
            var botoes = new StackPanel { Orientation = Orientation.Horizontal };
            botoes.Children.Add(Ui.Botao("Abrir arquivo…", "abrir", AbrirDialogo, "Abre um .estructto (ou .portico antigo) ou importa um .ftl do Ftool original (Ctrl+O)."));
            var imp = Ui.Botao("Importar .ftl", "importar", ImportarDialogo, "Importa a geometria, seções, materiais e casos de um arquivo .ftl do Ftool original."); imp.Margin = new Thickness(8, 0, 0, 0);
            botoes.Children.Add(imp);
            DockPanel.SetDock(botoes, Dock.Left); rod.Children.Add(botoes);
            var atalhos = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            foreach (var kd in new[] { ("F5", "calcular"), ("F4", "enquadrar"), ("F1", "ajuda") })
            {
                atalhos.Children.Add(Tecla(kd.Item1));
                atalhos.Children.Add(new TextBlock { Text = " " + kd.Item2, FontSize = 11, Foreground = TemaPlant.BTexto3, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) });
            }
            rod.Children.Add(atalhos);
            raiz.Children.Add(rod);

            ConteudoInicio.Content = raiz;
            Anim.Escalonar(raiz);
        }

        static Style estiloHero;
        static Style EstiloHero()
        {
            if (estiloHero != null) return estiloHero;
            // igual ao CartaoInicio, mas com moldura de acento e fundo tonal
            var t = new ControlTemplate(typeof(Button));
            var bd = new FrameworkElementFactory(typeof(Border), "b");
            bd.SetValue(Border.BackgroundProperty, TemaPlant.BAcentoSutil);
            bd.SetValue(Border.BorderBrushProperty, TemaPlant.BAcento);
            bd.SetValue(Border.BorderThicknessProperty, new Thickness(1.5));
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            bd.SetValue(Border.PaddingProperty, new Thickness(14));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter)); bd.AppendChild(cp);
            t.VisualTree = bd;
            var hov = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hov.Setters.Add(new Setter(Border.BackgroundProperty, TemaPlant.B(TemaPlant.Acento, 70), "b"));
            hov.Setters.Add(new Setter(Border.BorderBrushProperty, TemaPlant.BBordaFoco, "b"));
            t.Triggers.Add(hov);
            var foc = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
            foc.Setters.Add(new Setter(Border.BorderBrushProperty, TemaPlant.BBordaFoco, "b"));
            t.Triggers.Add(foc);
            estiloHero = new Style(typeof(Button));
            estiloHero.Setters.Add(new Setter(Control.TemplateProperty, t));
            estiloHero.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand));
            estiloHero.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
            return estiloHero;
        }
    }
}
