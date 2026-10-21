// Relatório do motor Python dentro do programa (docs/RELATORIO-UNIFICADO.md).
// O motor grava o HTML (POST /api/relatorio, formato html, com a empresa do carimbo); o anexo das placas de base,
// chumbadores e soldas (Nucleo.cs, memorial do Estructto) entra no fim do corpo como páginas em imagem.
// Mostrado num WebView2 sem script, sem rede (só o arquivo local e data:) e sem DevTools. Salvar PDF = PrintToPdfAsync.
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace NovoFtool
{
    public static class RelatorioMotor
    {
        // Empresa/autor do carimbo: guardada em empresa.txt na pasta do Config (%AppData%\NovoFtool; na execução automática,
        // %TEMP%\Estructto\config-automacao — E2E-11). Padrão vazio. Só regrava quando muda.
        static string ArqEmpresa => Path.Combine(Config.Pasta, "empresa.txt");
        // Empresa do projeto aberto (config.projeto.empresa, tela de novo projeto): tem prioridade sobre a guardada. Abrir a lê do documento ativo.
        public static string EmpresaProjeto;
        public static string Empresa
        {
            get { try { return File.Exists(ArqEmpresa) ? File.ReadAllText(ArqEmpresa).Trim() : ""; } catch { return ""; } }
            set { string v = (value ?? "").Trim(); if (v == Empresa) return; try { Directory.CreateDirectory(Path.GetDirectoryName(ArqEmpresa)); File.WriteAllText(ArqEmpresa, v); } catch { } }
        }

        // Gancho para o documento espacial (e para o 2D com motor Python): o modelo e o cálculo já estão no motor.
        public static JanelaRelatorioMotor Abrir(Window dono, string tituloDoc) => Abrir(dono, tituloDoc, null);

        // anexo: FlowDocument do Estructto acrescentado ao fim (placas de base); null = só o relatório do motor.
        public static JanelaRelatorioMotor Abrir(Window dono, string tituloDoc, Func<FlowDocument> anexo)
        {
            EmpresaProjeto = (string)(dono as MainWindow)?.EspacialAtivo?.Json?["config"]?["projeto"]?["empresa"];   // E2E-02: a do projeto no carimbo
            var j = new JanelaRelatorioMotor(tituloDoc, anexo) { Owner = dono };
            j.Show();
            return j;
        }

        // HTML do motor com o anexo, gravado num arquivo temporário (o WebView2 abre arquivo; NavigateToString tem limite de 2 MB).
        internal static async Task<string> Gerar(string empresa, Func<FlowDocument> anexo)
        {
            const string rel = "relatorio-estructto.html";
            await Motor.Post("/api/relatorio", new { formato = "html", caminho = rel, empresa = empresa ?? "" });
            string caminho = Path.Combine(Motor.Pasta, rel);
            string html = File.ReadAllText(caminho);
            var doc = anexo?.Invoke();
            if (doc != null)
            {
                const string fim = "\n</td></tr></tbody><tfoot>";   // fim do corpo (relatorio.py, html())
                int i = html.LastIndexOf(fim, StringComparison.Ordinal);
                if (i < 0) throw new InvalidOperationException("formato do relatório do motor mudou: não achei o fim do corpo");
                html = html.Insert(i, AnexoHtml(doc));
                File.WriteAllText(caminho, html);
            }
            return caminho;
        }

        // Página útil do relatório impresso: 175 mm (A4 − 20 − 15) × 229 mm (297 − 30 − 38 da reserva do carimbo); a imagem usa
        // 175 × 180 mm (px a 96 dpi) para caber com o título do anexo na primeira folha.
        const double LargPag = 661, AltPag = 680, Escala = 2;   // 2× = 192 dpi na impressão

        static string AnexoHtml(FlowDocument doc)
        {
            doc.PageWidth = LargPag; doc.PageHeight = AltPag; doc.PagePadding = new Thickness(0); doc.ColumnWidth = double.PositiveInfinity;
            var pag = ((IDocumentPaginatorSource)doc).DocumentPaginator;
            pag.PageSize = new Size(LargPag, AltPag);
            pag.ComputePageCount();
            var sb = new System.Text.StringBuilder("\n<h2 class=\"quebra\"><span class=\"n\">A</span>Anexo — placas de base, chumbadores e soldas</h2>\n" +
                "<p>Memorial do Estructto (mesmas fórmulas da janela Placa de base), com os esforços do resultado mostrado no programa.</p>\n");
            for (int k = 0; k < pag.PageCount; k++)
            {
                var p = pag.GetPage(k);
                var dv = new DrawingVisual();   // fundo branco e depois a página (o dpi maior amplia sem esticar)
                using (var dc = dv.RenderOpen()) dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, LargPag, AltPag));
                var bmp = new RenderTargetBitmap((int)(LargPag * Escala), (int)(AltPag * Escala), 96 * Escala, 96 * Escala, PixelFormats.Pbgra32);
                bmp.Render(dv);
                bmp.Render(p.Visual);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                using var ms = new MemoryStream();
                enc.Save(ms);
                sb.Append("<img class=\"" + (k > 0 ? "quebra" : "") + "\" style=\"display:block;width:100%;break-inside:avoid\" alt=\"Anexo, página " + (k + 1) +
                          "\" src=\"data:image/png;base64," + Convert.ToBase64String(ms.ToArray()) + "\">\n");
            }
            return sb.ToString();
        }
    }

    public class JanelaRelatorioMotor : JanelaEscura
    {
        readonly WebView2 web = new WebView2();
        readonly TextBox cEmpresa;
        readonly TextBlock estado;
        readonly Func<FlowDocument> anexo;
        readonly string titulo;
        string arquivo;   // HTML gerado (pasta de trabalho do motor)
        public Task Pronto => pronto.Task;
        readonly TaskCompletionSource<bool> pronto = new TaskCompletionSource<bool>();

        public JanelaRelatorioMotor(string titulo, Func<FlowDocument> anexo)
        {
            this.titulo = titulo; this.anexo = anexo;
            Title = "Relatório NBR — " + titulo;
            Width = 980; Height = 900; ShowInTaskbar = true;
            var raiz = new DockPanel();
            var barra = new DockPanel { LastChildFill = true };
            var faixa = new Border { Background = BPainel, BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(14, 8, 14, 8), Child = barra };
            var dir = new StackPanel { Orientation = Orientation.Horizontal };
            dir.Children.Add(B("Salvar PDF…", () => Salvar(true), true, "Grava o relatório em PDF (A4, com o carimbo em cada página)."));
            dir.Children.Add(B("Salvar HTML…", () => Salvar(false), false, "Grava o HTML autônomo (abre em qualquer navegador, sem internet)."));
            dir.Children.Add(B("Fechar", Close, false, "Fecha o relatório (Esc)."));
            DockPanel.SetDock(dir, Dock.Right); barra.Children.Add(dir);
            var esq = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            esq.Children.Add(new TextBlock { Text = "Empresa / autor", Foreground = BFraco, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            cEmpresa = Campo(string.IsNullOrWhiteSpace(RelatorioMotor.EmpresaProjeto) ? RelatorioMotor.Empresa : RelatorioMotor.EmpresaProjeto, 220, "Nome que sai no campo Empresa do carimbo. Vazio = em branco, para preencher à mão. Fica guardado para os próximos relatórios.");
            cEmpresa.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) Atualizar(); };
            esq.Children.Add(cEmpresa);
            esq.Children.Add(B("Aplicar", Atualizar, false, "Gera o relatório de novo com esta empresa (Enter)."));
            estado = T("", 11, TemaPlant.BTexto3); estado.Margin = new Thickness(14, 0, 0, 0); estado.VerticalAlignment = VerticalAlignment.Center; estado.TextWrapping = TextWrapping.NoWrap; estado.TextTrimming = TextTrimming.CharacterEllipsis;
            esq.Children.Add(estado);
            barra.Children.Add(esq);
            DockPanel.SetDock(faixa, Dock.Top);
            raiz.Children.Add(faixa);
            raiz.Children.Add(web);
            Content = raiz;
            KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Escape) Close(); };
            Loaded += async (s, e) => { await Iniciar(); Atualizar(); };
        }

        async Task Iniciar()
        {
            var amb = await CoreWebView2Environment.CreateAsync(null,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Estructto", "WebView2"));
            await web.EnsureCoreWebView2Async(amb);
            var c = web.CoreWebView2;
            c.Settings.IsScriptEnabled = false;            // o relatório não tem JS; dado do modelo nunca executa
            c.Settings.IsWebMessageEnabled = false;
            c.Settings.AreDevToolsEnabled = false;
            c.Settings.AreDefaultContextMenusEnabled = false;
            c.Settings.IsStatusBarEnabled = false;
            c.Settings.AreBrowserAcceleratorKeysEnabled = false;
            // sem rede: só o arquivo do relatório e data: (fontes e imagens embutidas)
            c.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            c.WebResourceRequested += (s, e) =>
            {
                var u = e.Request.Uri;
                if (!u.StartsWith("file:", StringComparison.OrdinalIgnoreCase) && !u.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                    e.Response = c.Environment.CreateWebResourceResponse(null, 403, "Bloqueado", "");
            };
            c.NavigationStarting += (s, e) => { if (arquivo == null || !e.Uri.Equals(new Uri(arquivo).AbsoluteUri, StringComparison.OrdinalIgnoreCase)) e.Cancel = true; };
            c.NewWindowRequested += (s, e) => e.Handled = true;
            c.NavigationCompleted += (s, e) => { estado.Text = e.IsSuccess ? "Relatório do motor" + (anexo != null ? " + anexo das placas de base" : "") + "." : "Falha ao mostrar o relatório."; pronto.TrySetResult(e.IsSuccess); };
        }

        async void Atualizar()
        {
            RelatorioMotor.Empresa = cEmpresa.Text;
            estado.Text = "Gerando o relatório…";
            try
            {
                arquivo = await RelatorioMotor.Gerar(cEmpresa.Text, anexo);
                web.CoreWebView2.Navigate(new Uri(arquivo).AbsoluteUri);
            }
            catch (Exception ex) { estado.Text = "⚠ " + ComandosAnalise.MensagemMotor(ex); pronto.TrySetResult(false); }
        }

        async void Salvar(bool pdf)
        {
            if (arquivo == null) return;
            var dlg = new Microsoft.Win32.SaveFileDialog { FileName = titulo, Filter = pdf ? "PDF (*.pdf)|*.pdf" : "HTML (*.html)|*.html", DefaultExt = pdf ? ".pdf" : ".html" };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                if (pdf) await GravarPdf(dlg.FileName);
                else File.Copy(arquivo, dlg.FileName, true);
                estado.Text = "Salvo: " + dlg.FileName;
            }
            catch (Exception ex) { estado.Text = "⚠ Não salvou: " + ex.Message; }
        }

        public async Task GravarPdf(string caminho)
        {
            var cfg = web.CoreWebView2.Environment.CreatePrintSettings();
            cfg.ShouldPrintBackgrounds = true; cfg.ShouldPrintHeaderAndFooter = false;   // margens e numeração vêm do @page do relatório
            cfg.MarginTop = cfg.MarginBottom = cfg.MarginLeft = cfg.MarginRight = 0;
            if (!await web.CoreWebView2.PrintToPdfAsync(caminho, cfg)) throw new IOException("o WebView2 não gravou o PDF");
        }

        // Captura (Capturas.cs): faixa pela árvore WPF + página pelo CapturePreviewAsync (o WebView2 é uma janela nativa).
        public async Task SalvarPng(string caminho)
        {
            UpdateLayout();
            var raiz = (FrameworkElement)Content;
            int w = (int)raiz.ActualWidth, h = (int)raiz.ActualHeight;
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render((Visual)Content);
            using var ms = new MemoryStream();
            await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, ms);
            ms.Position = 0;
            var pag = BitmapFrame.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var pos = web.TranslatePoint(new Point(0, 0), (UIElement)Content);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawImage(rtb, new Rect(0, 0, w, h));
                dc.DrawImage(pag, new Rect(pos.X, pos.Y, web.ActualWidth, web.ActualHeight));
            }
            var fim = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            fim.Render(dv);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(fim));
            using var fs = File.Create(caminho);
            enc.Save(fs);
        }
    }
}
