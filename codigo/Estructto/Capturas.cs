// Modo de capturas automaticas de tela para QA visual (--capturas <pasta_saida> [arquivo.estructto]).
// Abre o modelo, calcula (F5), renderiza a janela principal (1600x900 e 1280x720) e todos os dialogos/abas
// fora da tela com RenderTargetBitmap (96 dpi) + PngBitmapEncoder, e encerra com codigo 0 (ou 1 + erros.txt).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Portico;
using F = Fluent;

namespace NovoFtool
{
    public static class Capturas
    {
        public static bool Ativo { get; private set; }
        static string pastaSaida;
        static string arquivoEntrada;

        public static bool Configurar(MainWindow w, string[] args)
        {
            if (args == null) return false;
            for (int i = 1; i < args.Length; i++)
            {
                string a = args[i] ?? "";
                if (string.Equals(a, "--capturas", StringComparison.OrdinalIgnoreCase))
                {
                    pastaSaida = i + 1 < args.Length ? args[i + 1] : null;
                    if (i + 2 < args.Length && !args[i + 2].StartsWith("--"))
                        arquivoEntrada = args[i + 2];
                    Ativo = true;
                    break;
                }
                if (a.StartsWith("--capturas=", StringComparison.OrdinalIgnoreCase))
                {
                    pastaSaida = a.Substring("--capturas=".Length).Trim('"');
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                        arquivoEntrada = args[i + 1];
                    Ativo = true;
                    break;
                }
            }
            if (!Ativo) return false;

            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Left = -10000;
            w.Top = -10000;
            w.Width = 1600;
            w.Height = 900;
            w.ShowActivated = false;
            w.ShowInTaskbar = false;
            w.Loaded += (s, e) => w.Dispatcher.BeginInvoke(new Action(() => Executar(w)), DispatcherPriority.ApplicationIdle);
            return true;
        }

        public static void GravarErro(Exception ex)
        {
            try
            {
                string dir = string.IsNullOrWhiteSpace(pastaSaida) ? Environment.CurrentDirectory : pastaSaida;
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "erros.txt"), ex.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }

        static string ResolverCaminhoModelo(string arq)
        {
            string padraoRel = Path.Combine("..", "..", "exemplos", "guincho-com-placa.estructto");
            if (!string.IsNullOrWhiteSpace(arq))
            {
                if (File.Exists(arq)) return Path.GetFullPath(arq);
                string p1 = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, arq));
                if (File.Exists(p1)) return p1;
                string p2 = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, arq));
                if (File.Exists(p2)) return p2;
                throw new FileNotFoundException("Arquivo de modelo nao encontrado: " + arq, arq);
            }

            var bases = new List<string> { Environment.CurrentDirectory, AppContext.BaseDirectory };
            foreach (string b in bases)
            {
                try
                {
                    string c = Path.GetFullPath(Path.Combine(b, padraoRel));
                    if (File.Exists(c)) return c;
                }
                catch { }

                DirectoryInfo d = new DirectoryInfo(b);
                while (d != null)
                {
                    string c1 = Path.Combine(d.FullName, "exemplos", "guincho-com-placa.estructto");
                    if (File.Exists(c1)) return c1;
                    string c2 = Path.Combine(d.FullName, "codigo", "Estructto", padraoRel);
                    try { if (File.Exists(c2)) return Path.GetFullPath(c2); } catch { }
                    d = d.Parent;
                }
            }
            throw new FileNotFoundException("Modelo padrao guincho-com-placa.estructto nao encontrado.");
        }

        static void EsperarIdle()
        {
            var disp = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
            disp.Invoke(new Action(() => { }), DispatcherPriority.Loaded);
            disp.Invoke(new Action(() => { }), DispatcherPriority.Render);
            disp.Invoke(new Action(() => { }), DispatcherPriority.Background);
            disp.Invoke(new Action(() => { }), DispatcherPriority.ContextIdle);
            disp.Invoke(new Action(() => { }), DispatcherPriority.ApplicationIdle);
        }

        static string Slug(string s)
        {
            if (string.IsNullOrEmpty(s)) return "aba";
            string norm = s.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (char c in norm)
            {
                UnicodeCategory cat = CharUnicodeInfo.GetUnicodeCategory(c);
                if (cat == UnicodeCategory.NonSpacingMark) continue;
                if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
                else if (c == ' ' || c == '-' || c == '_') sb.Append('-');
            }
            return sb.ToString().Trim('-');
        }

        static void SalvarPng(BitmapSource bmp, string caminho)
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var fs = File.Create(caminho))
                enc.Save(fs);
        }

        static void RenderizarJanela(Window w, int larg, int alt, string nomeArquivo, Action aposLayout = null, FrameworkElement sobrepor = null, Func<Point> calcPosSobrepor = null)
        {
            w.Width = larg;
            w.Height = alt;
            EsperarIdle();
            w.Measure(new Size(larg, alt));
            w.Arrange(new Rect(0, 0, larg, alt));
            w.UpdateLayout();
            if (aposLayout != null)
            {
                aposLayout();
                w.UpdateLayout();
            }
            EsperarIdle();
            w.Measure(new Size(larg, alt));
            w.Arrange(new Rect(0, 0, larg, alt));
            w.UpdateLayout();
            if (aposLayout != null)
            {
                aposLayout();
                w.UpdateLayout();
            }

            var rtb = new RenderTargetBitmap(larg, alt, 96, 96, PixelFormats.Pbgra32);
            var fundo = new DrawingVisual();
            using (var dc = fundo.RenderOpen())
                dc.DrawRectangle(w.Background ?? JanelaEscura.BFundo, null, new Rect(0, 0, larg, alt));
            rtb.Render(fundo);
            rtb.Render(w);

            if (sobrepor != null)
            {
                Point posSobrepor = calcPosSobrepor != null ? calcPosSobrepor() : default;
                sobrepor.Measure(new Size(larg, alt));
                Size ds = sobrepor.DesiredSize;
                int sw = Math.Max(1, (int)Math.Ceiling(ds.Width > 1 ? ds.Width : sobrepor.ActualWidth));
                int sh = Math.Max(1, (int)Math.Ceiling(ds.Height > 1 ? ds.Height : sobrepor.ActualHeight));
                sobrepor.Arrange(new Rect(0, 0, sw, sh));
                sobrepor.UpdateLayout();
                var rtbSub = new RenderTargetBitmap(sw, sh, 96, 96, PixelFormats.Pbgra32);
                rtbSub.Render(sobrepor);
                var dvSub = new DrawingVisual();
                using (var dc = dvSub.RenderOpen())
                    dc.DrawImage(rtbSub, new Rect(posSobrepor.X, posSobrepor.Y, sw, sh));
                rtb.Render(dvSub);
            }

            SalvarPng(rtb, Path.Combine(pastaSaida, nomeArquivo));
        }

        static void CapturarPrincipal(MainWindow w, string prefixo, Action aposLayout = null, FrameworkElement sobrepor = null, Func<Point> calcPosSobrepor = null)
        {
            Action prep = () =>
            {
                TemaPlant.AjustarFaixa(w, TemaPlant.Selo());
                if (w.M.Nos.Count > 0) w.Vista.ZoomTudo();
                aposLayout?.Invoke();
                w.Vista.InvalidateVisual();
            };

            prep();
            EsperarIdle();
            RenderizarJanela(w, 1600, 900, prefixo + ".png", prep, sobrepor, calcPosSobrepor);
            RenderizarJanela(w, 1280, 720, prefixo + "-1280x720.png", prep, sobrepor, calcPosSobrepor);

            w.Width = 1600;
            w.Height = 900;
            w.Measure(new Size(1600, 900));
            w.Arrange(new Rect(0, 0, 1600, 900));
            w.UpdateLayout();
            if (w.M.Nos.Count > 0) w.Vista.ZoomTudo();
            EsperarIdle();
        }

        static void PrepararDialogoForaDaTela(Window d)
        {
            d.WindowStartupLocation = WindowStartupLocation.Manual;
            d.Left = -10000;
            d.Top = -10000;
            d.ShowActivated = false;
            d.ShowInTaskbar = false;
        }

        static void CapturarDialogo(Window d, string nomeArquivo, Action aposLayout = null)
        {
            int larg = !double.IsNaN(d.Width) && d.Width > 0 ? (int)Math.Round(d.Width) : Math.Max(400, (int)Math.Round(d.ActualWidth));
            int alt = !double.IsNaN(d.Height) && d.Height > 0 && d.SizeToContent == SizeToContent.Manual
                ? (int)Math.Round(d.Height)
                : Math.Max(180, (int)Math.Round(d.ActualHeight));
            if (d.SizeToContent == SizeToContent.Height || d.SizeToContent == SizeToContent.WidthAndHeight)
            {
                d.Measure(new Size(larg, double.PositiveInfinity));
                alt = Math.Max(180, (int)Math.Ceiling(d.DesiredSize.Height));
            }
            RenderizarJanela(d, larg, alt, nomeArquivo, aposLayout);
        }

        // Folha de icones: nome embaixo, em 32 px e 16 px, sobre o fundo dado. zoom > 1: so os de 16 px, ampliados (vizinho mais proximo).
        static void FolhaIcones(string arquivo, Color fundo, int zoom = 1, int tam = 16)
        {
            const int colunas = 12;
            var painel = new WrapPanel { Width = zoom > 1 ? (tam > 16 ? 12 * 48 : 560) : colunas * 96, Background = new SolidColorBrush(fundo), UseLayoutRounding = true, SnapsToDevicePixels = true };
            foreach (string n in Icones.Nomes)
            {
                var celula = new StackPanel { Width = zoom > 1 ? (tam > 16 ? 48 : 56) : 96, Height = zoom > 1 ? (tam > 16 ? 48 : 32) : 76, Margin = new Thickness(0) };
                if (zoom > 1)
                {
                    var ic = Icones.Criar(n, tam); ((FrameworkElement)ic).HorizontalAlignment = HorizontalAlignment.Center; ((FrameworkElement)ic).Margin = new Thickness(0, 8, 0, 8);
                    celula.Children.Add(ic);
                }
                else
                {
                    var linha = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 6) };
                    linha.Children.Add(Icones.Criar(n, 32));
                    var i16 = Icones.Criar(n, 16); ((FrameworkElement)i16).Margin = new Thickness(14, 8, 0, 0);
                    linha.Children.Add(i16);
                    celula.Children.Add(linha);
                    celula.Children.Add(new TextBlock { Text = n, FontFamily = new FontFamily("Segoe UI"), FontSize = 10, Foreground = TemaPlant.BTexto3, HorizontalAlignment = HorizontalAlignment.Center });
                }
                painel.Children.Add(celula);
            }
            painel.Measure(new Size(painel.Width, 4000));
            painel.Arrange(new Rect(0, 0, painel.Width, painel.DesiredSize.Height));
            painel.UpdateLayout();
            int pw = (int)Math.Ceiling(painel.Width), ph = (int)Math.Ceiling(painel.DesiredSize.Height);
            var rtb = new RenderTargetBitmap(pw, ph, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(painel);
            BitmapSource final = rtb;
            if (zoom > 1)
            {
                var dv = new DrawingVisual();
                RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.NearestNeighbor);
                using (var dc = dv.RenderOpen()) dc.DrawImage(rtb, new Rect(0, 0, pw * zoom, ph * zoom));
                var rz = new RenderTargetBitmap(pw * zoom, ph * zoom, 96, 96, PixelFormats.Pbgra32);
                rz.Render(dv);
                final = rz;
            }
            SalvarPng(final, Path.Combine(pastaSaida, arquivo));
        }

        public static void Executar(MainWindow w)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(pastaSaida))
                    throw new ArgumentException("Informe a pasta de saida: Estructto.exe --capturas <pasta_saida> [arquivo.estructto]");
                pastaSaida = Path.GetFullPath(pastaSaida);
                Directory.CreateDirectory(pastaSaida);
                string arqErros = Path.Combine(pastaSaida, "erros.txt");
                if (File.Exists(arqErros)) File.Delete(arqErros);

                string caminhoModelo = ResolverCaminhoModelo(arquivoEntrada);

                // 00) Folhas de icones (fundo do tema, fundo da ribbon e ampliada 3x)
                FolhaIcones("00-icones.png", TemaPlant.BgCanvas);
                FolhaIcones("00-icones-ribbon.png", TemaPlant.Surface1);
                FolhaIcones("00-icones-16x3.png", TemaPlant.Surface1, 3);
                FolhaIcones("00-icones-32x2.png", TemaPlant.Surface1, 2, 32);

                // 01) Tela inicial e Backstage
                w.MontarTelaInicial();
                w.TelaInicial.Visibility = Visibility.Visible;
                w.Bastidor.IsOpen = false;
                CapturarPrincipal(w, "01-inicio");

                w.Bastidor.IsOpen = true;
                EsperarIdle();
                CapturarPrincipal(w, "01-backstage");
                w.MostrarRecentesBs();
                EsperarIdle();
                CapturarPrincipal(w, "01-backstage-recentes");
                w.Bastidor.IsOpen = false;
                EsperarIdle();

                // Abre o modelo e calcula (F5)
                w.Abrir(caminhoModelo);
                w.TelaInicial.Visibility = Visibility.Collapsed;
                w.Resolver(true);
                EsperarIdle();

                // 02) Modelo aberto
                w.MostrarDiagrama(Diagrama.Nenhum);
                w.Faixa.SelectedTabItem = w.abaModelo;
                CapturarPrincipal(w, "02-modelo");

                // 03) Deformada
                w.MostrarDiagrama(Diagrama.Deformada);
                CapturarPrincipal(w, "03-deformada");

                // 04) Normal
                w.MostrarDiagrama(Diagrama.Normal);
                CapturarPrincipal(w, "04-normal");

                // 05) Cortante
                w.MostrarDiagrama(Diagrama.Cortante);
                CapturarPrincipal(w, "05-cortante");

                // 06) Momento
                w.MostrarDiagrama(Diagrama.Momento);
                CapturarPrincipal(w, "06-momento");

                // 07) Verificacao de barras
                w.MostrarDiagrama(Diagrama.Verificacao);
                w.AbasTabela.SelectedIndex = 2;
                CapturarPrincipal(w, "07-verificacao-barras");
                w.FiltrarCriticos(true);
                CapturarPrincipal(w, "07-verificacao-criticos");
                w.FiltrarCriticos(false);
                w.AbasTabela.SelectedIndex = 0;

                // 08) Ferramenta Medir com uma medida feita entre 2 nos
                w.MostrarDiagrama(Diagrama.Nenhum);
                w.Faixa.SelectedTabItem = w.abaModelo;
                No nMedA = w.M.NoPorId(4) ?? w.M.Nos.FirstOrDefault();
                No nMedB = w.M.NoPorId(3) ?? w.M.Nos.LastOrDefault();
                CapturarPrincipal(w, "08-medir", () =>
                {
                    if (nMedA != null && nMedB != null)
                    {
                        w.UsarFerramenta(Ferramenta.Medir);
                        w.PontoMedida(nMedA.X, nMedA.Y);
                        w.PontoMedida(nMedB.X, nMedB.Y);
                    }
                });
                w.UsarFerramenta(Ferramenta.Selecionar);

                // 35) HUD da vista (momento) + recorte do cartao; 36) mapa de calor; 37) inspecao com o mouse; 38) toast; 39) historico aberto
                w.MostrarDiagrama(Diagrama.Momento);
                w.Faixa.SelectedTabItem = w.abaAnalise;
                CapturarPrincipal(w, "35-hud");
                {
                    w.Hud.Measure(new Size(600, 300)); w.Hud.Arrange(new Rect(0, 0, w.Hud.DesiredSize.Width, w.Hud.DesiredSize.Height)); w.Hud.UpdateLayout();
                    var rh = new RenderTargetBitmap((int)Math.Ceiling(w.Hud.DesiredSize.Width) * 2, (int)Math.Ceiling(w.Hud.DesiredSize.Height) * 2, 192, 192, PixelFormats.Pbgra32);
                    var dvh = new DrawingVisual();
                    using (var dch = dvh.RenderOpen()) { dch.DrawRectangle(TemaPlant.BBgCanvas, null, new Rect(0, 0, 2000, 2000)); }
                    rh.Render(dvh); rh.Render(w.Hud);
                    SalvarPng(rh, Path.Combine(pastaSaida, "35-hud-cartao.png"));
                    w.UpdateLayout();
                }
                w.MostrarDiagrama(Diagrama.Verificacao, true);
                w.AbasTabela.SelectedIndex = 2;
                CapturarPrincipal(w, "36-mapa-calor");
                w.AbasTabela.SelectedIndex = 0;
                foreach (var tipoInsp in new[] { Diagrama.Momento, Diagrama.Deformada })
                {
                    w.MostrarDiagrama(tipoInsp);
                    Barra bInsp = w.M.Barras.Where(x => x.Res != null).OrderByDescending(x => w.M.Comp(x)).FirstOrDefault();
                    if (bInsp == null) break;
                    Action simula = () =>
                    {
                        No ia = w.M.NoPorId(bInsp.N1), ic = w.M.NoPorId(bInsp.N2);
                        w.Vista.SimularMouse(w.Vista.Tela(ia.X + (ic.X - ia.X) * 0.42, ia.Y + (ic.Y - ia.Y) * 0.42));
                    };
                    CapturarPrincipal(w, tipoInsp == Diagrama.Momento ? "37-inspecao-mouse" : "37-inspecao-mouse-deformada", simula);
                    w.Vista.SimularMouse(null);
                }
                w.MostrarDiagrama(Diagrama.Nenhum);
                w.MostrarToast("Calculado — caso G - Permanente. ΣRx = 0,00 kN  ΣRy = 5,88 kN", true);
                CapturarPrincipal(w, "38-toast");
                w.Toast.Visibility = Visibility.Collapsed;
                w.HistoricoCaixa.Visibility = Visibility.Visible;
                CapturarPrincipal(w, "39-historico");
                w.HistoricoCaixa.Visibility = Visibility.Collapsed;
                w.Vista.Etiqueta = "BARRA — próximo ponto: clique, ou digite o comprimento (na direção do mouse), x,y, @dx,dy, @L<ângulo.   Botão direito/Esc termina.";
                CapturarPrincipal(w, "39-etiqueta");
                w.Vista.Etiqueta = null;
                w.MostrarDiagrama(Diagrama.Nenhum);

                // 09) Cada aba da Ribbon (incluindo abas contextuais Barra e No)
                foreach (var tab in w.Faixa.Tabs.ToList())
                {
                    if (tab == w.ctxBarra || tab == w.ctxNo) continue;
                    string slug = Slug(tab.Header as string);
                    w.Vista.Selecionar(new object[0]);
                    w.Faixa.SelectedTabItem = tab;
                    EsperarIdle();
                    CapturarPrincipal(w, "09-ribbon-" + slug, () =>
                    {
                        w.Faixa.SelectedTabItem = tab;
                        TemaPlant.AjustarFaixa(w.Faixa, null);
                    });
                }

                if (w.M.Barras.Count > 0 && w.ctxBarra != null)
                {
                    w.Vista.Selecionar(new object[] { w.M.Barras[0] });
                    w.Faixa.SelectedTabItem = w.ctxBarra;
                    EsperarIdle();
                    CapturarPrincipal(w, "09-ribbon-barra", () =>
                    {
                        w.Vista.Selecionar(new object[] { w.M.Barras[0] });
                        w.Faixa.SelectedTabItem = w.ctxBarra;
                        TemaPlant.AjustarFaixa(w.Faixa, null);
                    });
                }

                if (w.M.Nos.Count > 0 && w.ctxNo != null)
                {
                    w.Vista.Selecionar(new object[] { w.M.Nos[0] });
                    w.Faixa.SelectedTabItem = w.ctxNo;
                    EsperarIdle();
                    CapturarPrincipal(w, "09-ribbon-no", () =>
                    {
                        w.Vista.Selecionar(new object[] { w.M.Nos[0] });
                        w.Faixa.SelectedTabItem = w.ctxNo;
                        TemaPlant.AjustarFaixa(w.Faixa, null);
                    });
                }

                w.Vista.Selecionar(new object[0]);
                w.Faixa.SelectedTabItem = w.abaModelo;
                EsperarIdle();

                // 10) Menu de contexto
                object alvoMenu = (object)w.M.Barras.FirstOrDefault() ?? w.M.Nos.FirstOrDefault();
                if (alvoMenu != null) w.Vista.Selecionar(new[] { alvoMenu });
                ContextMenu cm = w.CriarMenuContexto(alvoMenu);
                cm.PlacementTarget = w.Vista;
                cm.IsOpen = true;
                EsperarIdle();
                Func<Point> posMenu = () =>
                {
                    Point pVista = new Point(w.Vista.ActualWidth * 0.48, w.Vista.ActualHeight * 0.42);
                    if (alvoMenu is Barra bm)
                    {
                        No na = w.M.NoPorId(bm.N1), nb = w.M.NoPorId(bm.N2);
                        if (na != null && nb != null)
                            pVista = w.Vista.Tela((na.X + nb.X) / 2, (na.Y + nb.Y) / 2);
                    }
                    try { return w.Vista.TranslatePoint(pVista, w); }
                    catch { return new Point(520, 320); }
                };
                CapturarPrincipal(w, "10-menu-contexto", null, cm, posMenu);
                cm.IsOpen = false;
                w.Vista.Selecionar(new object[0]);
                EsperarIdle();

                // 30-34) Inspector WPF: modelo, no, barra, carga e multi-selecao (tela inteira + recorte do painel completo)
                {
                    Action<string, IEnumerable<object>> inspetor = (nome, sel) =>
                    {
                        Action prep = () => w.Vista.Selecionar(sel.ToArray());
                        prep();
                        EsperarIdle();
                        CapturarPrincipal(w, nome, prep);
                        w.Insp.UpdateLayout();
                        SalvarPng(w.Insp.Renderizar(), Path.Combine(pastaSaida, nome + "-painel.png"));
                    };
                    inspetor("30-inspector-modelo", new object[0]);
                    No noI = w.M.Nos.FirstOrDefault(x => x.TemApoio && x.Res != null) ?? w.M.Nos.FirstOrDefault();
                    if (noI != null) inspetor("31-inspector-no", new object[] { noI });
                    Barra barI = w.M.Barras.FirstOrDefault(x => x.Res != null) ?? w.M.Barras.FirstOrDefault();
                    if (barI != null) inspetor("32-inspector-barra", new object[] { barI });
                    object cargaI = (object)w.M.Barras.FirstOrDefault(x => x.TemCarga) ?? w.M.Nos.FirstOrDefault(x => x.TemCarga);
                    if (cargaI != null) inspetor("33-inspector-carga", new object[] { cargaI });
                    if (w.M.Barras.Count > 1) inspetor("34-inspector-multi", w.M.Barras.Take(3).Cast<object>().ToList());

                    // seletor de perfil (popup nao entra na captura da janela: renderiza o conteudo a parte)
                    if (barI != null)
                    {
                        w.Vista.Selecionar(new object[] { barI });
                        EsperarIdle();
                        var pc = w.Insp.ConteudoSeletorPerfil("");
                        if (pc != null)
                        {
                            var fundoPop = new Border { Background = TemaPlant.BSup1, Padding = new Thickness(12), Child = pc };
                            fundoPop.Measure(new Size(330, 420)); fundoPop.Arrange(new Rect(0, 0, 330, fundoPop.DesiredSize.Height)); fundoPop.UpdateLayout();
                            var rb = new RenderTargetBitmap(330, Math.Max(1, (int)Math.Ceiling(fundoPop.DesiredSize.Height)), 96, 96, PixelFormats.Pbgra32);
                            rb.Render(fundoPop);
                            SalvarPng(rb, Path.Combine(pastaSaida, "34-inspector-perfil-popup.png"));
                            fundoPop.Child = null;
                            w.Insp.DevolverSeletorPerfil(pc);
                        }
                    }

                    // teste por codigo: editar um campo pelo Inspector muda o modelo (com expressao) e o desfazer volta
                    var log = new StringBuilder();
                    Action<bool, string> confere = (ok, msg) => { log.AppendLine((ok ? "OK    " : "FALHA ") + msg); if (!ok) throw new InvalidOperationException("Teste do Inspector falhou: " + msg); };
                    try
                    {
                        Barra tb = w.M.Barras.First();
                        int idB = tb.Id;
                        double q0 = tb.QGlobalY;
                        w.Vista.Selecionar(new object[] { tb });
                        EsperarIdle();
                        confere(w.Insp.NomesDosCampos.Contains("QGlobalY") && w.Insp.NomesDosCampos.Contains("Secao") && w.Insp.NomesDosCampos.Contains("RotulaInicio"), "campos da barra presentes (" + w.Insp.NomesDosCampos.Count() + ")");
                        w.alterado = false;
                        confere(w.Insp.DefinirTexto("QGlobalY", "-12+2,5"), "DefinirTexto aceitou");
                        confere(Math.Abs(w.M.Barras.First(b => b.Id == idB).QGlobalY - (-9.5)) < 1e-9, "q vertical = -12+2,5 -> -9,5 (expressao e virgula)");
                        confere(w.alterado, "modelo marcado como modificado");
                        w.Desfazer();
                        EsperarIdle();
                        confere(Math.Abs(w.M.Barras.First(b => b.Id == idB).QGlobalY - q0) < 1e-9, "desfazer devolveu q vertical = " + q0);

                        // valor invalido nao altera
                        w.Vista.Selecionar(new object[] { w.M.Barras.First(b => b.Id == idB) });
                        EsperarIdle();
                        w.Insp.DefinirTexto("QGlobalY", "abc");
                        confere(Math.Abs(w.M.Barras.First(b => b.Id == idB).QGlobalY - q0) < 1e-9, "texto invalido nao altera o modelo");

                        // multi-selecao aplica a todos
                        var tres = w.M.Barras.Take(3).Cast<object>().ToList();
                        int[] ids = tres.Cast<Barra>().Select(b => b.Id).ToArray();
                        w.Vista.Selecionar(tres);
                        EsperarIdle();
                        w.Insp.DefinirTexto("QGlobalX", "1,5");
                        confere(ids.All(i => Math.Abs(w.M.Barras.First(b => b.Id == i).QGlobalX - 1.5) < 1e-9), "multi-selecao: q horizontal = 1,5 em " + ids.Length + " barras");
                        w.Desfazer();
                        EsperarIdle();

                        // no: coordenada com expressao
                        No nt = w.M.Nos.First();
                        int idN = nt.Id; double x0 = nt.X;
                        w.Vista.Selecionar(new object[] { nt });
                        EsperarIdle();
                        w.Insp.DefinirTexto("X", "(" + x0.ToString(CultureInfo.InvariantCulture) + "+1)*2/2");
                        confere(Math.Abs(w.M.NoPorId(idN).X - (x0 + 1)) < 1e-9, "no: X = (x+1)*2/2 -> x+1");
                        w.Desfazer();
                        EsperarIdle();
                        confere(Math.Abs(w.M.NoPorId(idN).X - x0) < 1e-9, "desfazer devolveu X do no");
                        log.AppendLine("TUDO OK");
                    }
                    finally
                    {
                        File.WriteAllText(Path.Combine(pastaSaida, "35-teste-inspector.txt"), log.ToString(), new UTF8Encoding(false));
                    }
                    w.alterado = false;
                    w.Vista.Selecionar(new object[0]);
                    EsperarIdle();
                }

                // 38/39) Paleta de comandos (Ctrl+K) e entrada dinamica: testes por codigo + capturas
                {
                    var logP = new StringBuilder();
                    int falhasP = 0;
                    try { falhasP = w.TestesPaletaEDinamica(logP); }
                    catch (Exception exP) { logP.AppendLine("EXCECAO: " + exP); falhasP = 1; }
                    finally { File.WriteAllText(Path.Combine(pastaSaida, "38-teste-paleta-entrada.txt"), logP.ToString(), new UTF8Encoding(false)); }
                    w.alterado = false;
                    w.Vista.Selecionar(new object[0]);
                    EsperarIdle();
                    if (falhasP > 0) throw new InvalidOperationException("Testes da paleta/entrada dinamica falharam (ver 38-teste-paleta-entrada.txt).");

                    foreach (var par in new[] { new[] { "mo", "38-palette" }, new[] { "w 200", "38-palette-perfil" }, new[] { "nó 3", "38-palette-objeto" }, new[] { "", "38-palette-vazia" } })
                    {
                        string consulta = par[0];
                        var pv = new PaletaVisual(w) { };
                        pv.Caixa.Text = consulta;
                        CapturarPrincipal(w, par[1], null, pv, () => new Point(Math.Max(10, (w.Width - pv.Width) / 2), 96));
                    }

                    w.PrepararCapturaDinamica();
                    CapturarPrincipal(w, "39-entrada-dinamica", () => w.AjustarCapturaDinamica());
                    w.EncerrarCapturaDinamica();
                    EsperarIdle();
                }

                // 20) JanelaPlaca para cada no com placa e para o no 4 (pilar inclinado)
                var nosPlaca = w.M.Nos.Where(x => x.Placa != null).ToList();
                No no4 = w.M.NoPorId(4);
                if (no4 != null && !nosPlaca.Contains(no4) && PlacaCalc.Pilar(w.M, no4, out Secao _, out double _, out double _, out string _) != null)
                    nosPlaca.Add(no4);

                bool capturouParafuso = false;
                foreach (No no in nosPlaca)
                {
                    bool tinhaPlaca = no.Placa != null;
                    var jp = new JanelaPlaca(w.M, no);
                    jp.Candidatos = w.M.Nos.Where(x => x != no && (x.Placa != null || x.TemApoio) && PlacaCalc.Pilar(w.M, x, out Secao _, out double _, out double _, out string _) != null).ToList();
                    jp.AvaliarEm = (noCand, pl) =>
                    {
                        var p0 = noCand.Placa; noCand.Placa = pl;
                        try { return w.PlacaPior(noCand); }
                        finally { noCand.Placa = p0; }
                    };
                    PrepararDialogoForaDaTela(jp);
                    jp.Show();
                    EsperarIdle();

                    if (!tinhaPlaca)
                    {
                        jp.UsarModoParafuso(false);
                        EsperarIdle();
                    }

                    // Cada vista do seletor (Planta | 3D | Forças | Ficha | Cálculo) em 1600x900 e 1280x720
                    string[] vistas = { "planta", "3d", "forcas", "ficha", "calculo" };
                    string pre = "20-placa-no" + no.Id;
                    for (int v = 0; v < vistas.Length; v++)
                    {
                        int vv = v;
                        jp.MostrarAba(vv);
                        RenderizarJanela(jp, 1600, 900, pre + "-" + vistas[vv] + ".png", () => jp.MostrarAba(vv));
                        RenderizarJanela(jp, 1280, 720, pre + "-" + vistas[vv] + "-1280x720.png", () => jp.MostrarAba(vv));
                    }
                    jp.MostrarAba(0);
                    RenderizarJanela(jp, 1600, 900, pre + ".png", () => jp.MostrarAba(0));

                    // Painel "Trocar chumbador" aberto
                    RenderizarJanela(jp, 1600, 900, pre + "-trocar.png", () => { jp.MostrarAba(0); jp.AbrirTrocar(); });
                    jp.FecharTrocar();

                    // 29) Sugerir (popup leve) e Aplicar também em… (pior caso)
                    if (no.Id == 1 || nosPlaca.Count == 1)
                    {
                        RenderizarJanela(jp, 1600, 900, "29-placa-sugerir.png", () => { jp.MostrarAba(0); jp.AbrirSugerir(); });
                        RenderizarJanela(jp, 1280, 720, "29-placa-sugerir-1280x720.png", () => { jp.MostrarAba(0); jp.AbrirSugerir(); });
                        RenderizarJanela(jp, 1600, 900, "29-placa-aplicar-em.png", () => { jp.MostrarAba(0); jp.AbrirAplicarEm(); });
                        RenderizarJanela(jp, 1280, 720, "29-placa-aplicar-em-1280x720.png", () => { jp.MostrarAba(0); jp.AbrirAplicarEm(); });
                        jp.FecharFlutuantes();
                    }

                    // 21) Modo parafuso
                    if (!capturouParafuso)
                    {
                        jp.UsarModoParafuso(true);
                        RenderizarJanela(jp, 1600, 900, "21-placa-parafuso.png", () => jp.MostrarAba(0));
                        RenderizarJanela(jp, 1280, 720, "21-placa-parafuso-1280x720.png", () => jp.MostrarAba(1));
                        capturouParafuso = true;
                    }

                    jp.Close();
                    EsperarIdle();
                }

                // 22) JanelaComparar com 3 fixacoes
                No noComp = w.M.Nos.FirstOrDefault(x => x.Placa != null) ?? w.M.Nos.FirstOrDefault();
                if (noComp != null)
                {
                    ResPlaca rpComp = PlacaCalc.Calcular(w.M, noComp);
                    var jc = new JanelaComparar(w.M, noComp, rpComp, noComp.Placa, new[] { "CB 12300", "WB 12400", "WQE 500 1/2\"" });
                    PrepararDialogoForaDaTela(jc);
                    jc.Show();
                    EsperarIdle();
                    RenderizarJanela(jc, 1600, 900, "22-comparar.png");
                    RenderizarJanela(jc, 1280, 720, "22-comparar-1280x720.png");
                    jc.Close();
                    EsperarIdle();
                }

                // 23) JanelaVerificacao para cada barra
                foreach (Barra b in w.M.Barras)
                {
                    var jv = new JanelaVerificacao(w.M, b);
                    PrepararDialogoForaDaTela(jv);
                    jv.Show();
                    EsperarIdle();
                    CapturarDialogo(jv, "23-verificacao-barra" + b.Id + ".png");
                    jv.SoPassam(true);
                    EsperarIdle();
                    CapturarDialogo(jv, "23-verificacao-barra" + b.Id + "-passam.png");
                    jv.Close();
                    EsperarIdle();
                }

                // 24) JanelaCasos
                var jCasos = new JanelaCasos(w.M);
                PrepararDialogoForaDaTela(jCasos);
                jCasos.Show();
                EsperarIdle();
                CapturarDialogo(jCasos, "24-casos.png");
                jCasos.Close();
                EsperarIdle();

                var mComb = Modelo.DeXml(w.M.ParaXml()); mComb.Combinacoes = Analise.GerarNbr(mComb.Casos);
                var jCasos2 = new JanelaCasos(mComb);
                PrepararDialogoForaDaTela(jCasos2);
                jCasos2.Show();
                EsperarIdle();
                CapturarDialogo(jCasos2, "24-casos-combinacoes.png");
                jCasos2.Close();
                EsperarIdle();

                // 25) JanelaRelatorio
                string nomeMod = Path.GetFileNameWithoutExtension(caminhoModelo);
                var jRel = new JanelaRelatorio(w.MontarRelatorio, nomeMod);
                PrepararDialogoForaDaTela(jRel);
                jRel.Show();
                EsperarIdle();
                CapturarDialogo(jRel, "25-relatorio.png", EsperarIdle);
                jRel.Close();
                EsperarIdle();

                // 26) JanelaAssistente para cada tipo
                string matPadrao = Biblioteca.Materiais.Count > 0 ? Biblioteca.Materiais[0].Nome : "Aço ASTM A572 Gr.50";
                foreach (string tipo in new[] { "viga", "portico", "trelica", "galpao" })
                {
                    var ja = new JanelaAssistente(tipo, "W 200 x 26,6", matPadrao);
                    PrepararDialogoForaDaTela(ja);
                    ja.Show();
                    EsperarIdle();
                    CapturarDialogo(ja, "26-assistente-" + tipo + ".png", () => ja.EnquadrarPrevia());
                    ja.Close();
                    EsperarIdle();
                }

                // 27) Caixa de mensagem
                var jCaixa = Caixa.Criar(null, "Salvar alterações?",
                    "O modelo \"" + Path.GetFileName(caminhoModelo) + "\" tem alterações que ainda não foram salvas.",
                    "Se não salvar, as alterações feitas desde o último salvamento serão perdidas.",
                    new[] { ("Salvar", MessageBoxResult.Yes, true), ("Não salvar", MessageBoxResult.No, false), ("Cancelar", MessageBoxResult.Cancel, false) },
                    "!", JanelaEscura.BAmarelo);
                PrepararDialogoForaDaTela(jCaixa);
                jCaixa.Show();
                EsperarIdle();
                CapturarDialogo(jCaixa, "27-caixa-mensagem.png");
                jCaixa.Close();
                EsperarIdle();

                // 28) JanelaAjuda
                var jAjuda = new JanelaAjuda();
                PrepararDialogoForaDaTela(jAjuda);
                jAjuda.Show();
                EsperarIdle();
                CapturarDialogo(jAjuda, "28-ajuda.png");
                jAjuda.Buscar("carga");
                EsperarIdle();
                CapturarDialogo(jAjuda, "28-ajuda-busca.png");
                jAjuda.Buscar("zzzz");
                EsperarIdle();
                CapturarDialogo(jAjuda, "28-ajuda-vazio.png");
                jAjuda.Close();
                EsperarIdle();

                w.alterado = false;
                w.Close();
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                GravarErro(ex);
                Environment.Exit(1);
            }
        }
    }
}
