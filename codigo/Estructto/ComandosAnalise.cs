// Comandos das janelas de análise sobre o motor Python (REVISAO-TELAS 3.4): Modal, Estabilidade, Verificação de barras,
// Verificar barra, Ligação, Comparar com CYPE e Catálogo de perfis. Cada Abrir...(dono) busca os dados no motor
// (Motor.Get/Post), mostra o erro do motor numa Caixa e abre a janela. A faixa chama estes métodos (MontarFaixa não muda aqui).
// UiAnalise: peças comuns às janelas (cabeçalho de veredito, tabela tabular com cabeçalho fixo, rodapé, estado vazio)
// e Amostras: JSON reais do motor embutidos para as capturas funcionarem sem o motor.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Portico;
using static NovoFtool.UiPlaca;

namespace NovoFtool
{
    public static class ComandosAnalise
    {
        static async Task<JsonNode> Buscar(Window dono, string titulo, Func<Task<JsonNode>> chamada)
        {
            var cursor = Mouse.OverrideCursor;
            Mouse.OverrideCursor = Cursors.Wait;
            try { return await chamada(); }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = cursor;
                Caixa.Erro(dono, titulo, "O motor de cálculo não respondeu: " + MensagemMotor(ex));
                return null;
            }
            finally { Mouse.OverrideCursor = cursor; }
        }

        // Texto do erro: MotorErro já traz o detalhe legível (Motor.cs, MotorErro.Texto).
        public static string MensagemMotor(Exception ex) => ex.Message;

        static void Mostrar(Window dono, Window j) { j.Owner = dono; j.Show(); }

        public static async Task AbrirModal(Window dono)
        {
            var d = await Buscar(dono, "Análise modal", () => Motor.Get("/api/modal?n=6"));
            if (d != null) Mostrar(dono, new JanelaModal(d));
        }

        public static async Task AbrirEstabilidade(Window dono)
        {
            var d = await Buscar(dono, "Estabilidade", () => Motor.Get("/api/estabilidade"));
            if (d != null) Mostrar(dono, new JanelaEstabilidade(d));
        }

        public static async Task AbrirVerificacaoBarras(Window dono)
        {
            var d = await Buscar(dono, "Verificação de barras", () => Motor.Get("/api/verificacao"));
            if (d == null) return;
            var j = new JanelaVerificacaoBarras(d);
            j.AbrirDetalhe = id => _ = AbrirVerificarBarra(j, id);
            Mostrar(dono, j);
        }

        public static async Task AbrirVerificarBarra(Window dono, string barra)
        {
            var d = await Buscar(dono, "Verificar barra", () => Motor.Get("/api/verificacao/" + Uri.EscapeDataString(barra)));
            if (d != null) Mostrar(dono, new JanelaVerificarBarraMotor(d));
        }

        public static async void AbrirLigacao(Window dono) { await AbrirLigacao(dono, null); }

        // Projetista de ligações (JanelaLigacaoProjeto): tipo = abre direto no tipo (null = biblioteca)
        public static async Task AbrirLigacao(Window dono, string tipo)
        {
            if (dono is MainWindow w) { w.AbrirProjetistaLigacao(tipo); return; }
            var d = await BuscarTipos(dono);
            if (d != null) Mostrar(dono, new JanelaLigacaoProjeto(d, tipo));
        }

        public static Task<JsonNode> BuscarTipos(Window dono) => Buscar(dono, "Projetar ligação", () => Motor.Get("/api/ligacoes/tipos"));

        public static void AbrirCype(Window dono)
        {
            var j = new JanelaCype(
                corpo => Motor.Post("/api/comparar_cype", corpo),
                corpo => Motor.Post("/api/importar_cype", corpo));
            // a importação trocou o modelo do motor: abre um documento espacial novo com ele (F5 no 2D perderia a obra)
            if (dono is MainWindow w)
                j.Importado += (nome, imp) =>
                {
                    if (imp?["modelo"] is JsonObject m)
                        _ = w.AbrirEspacial(() => DocumentoEspacial.DoModelo((JsonObject)m.DeepClone(), Path.GetFileNameWithoutExtension(nome) + " (CYPE)", imp["avisos"]), "importando " + nome);
                };
            Mostrar(dono, j);
        }

        public static async void AbrirCatalogo(Window dono)
        {
            var d = await Buscar(dono, "Catálogo de perfis", () => Motor.Get("/api/catalogo"));
            if (d != null) Mostrar(dono, new JanelaCatalogo(d, nome => Motor.Get("/api/catalogo/" + Uri.EscapeDataString(nome))));
        }
    }

    // JSON reais do motor (amostras/*.json, recurso embutido "amostra-<nome>.json"), usados pelas capturas.
    public static class Amostras
    {
        public static JsonNode Ler(string nome)
        {
            using var s = typeof(Amostras).Assembly.GetManifestResourceStream("amostra-" + nome + ".json")
                ?? throw new FileNotFoundException("amostra não embutida: " + nome);
            return JsonNode.Parse(s);
        }
    }

    public static class UiAnalise
    {
        // ---------- leitura tolerante do JSON do motor ----------
        public static double D(JsonNode n, string chave = null)
        {
            var v = chave == null ? n : n?[chave];
            if (v is JsonValue jv)
            {
                if (jv.TryGetValue(out double x)) return x;
                if (jv.TryGetValue(out bool b)) return b ? 1 : 0;
                // valores criados em C# (JsonValue<int>, <long>...) não convertem direto para double
                if (double.TryParse(jv.ToJsonString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out x)) return x;
            }
            return double.NaN;
        }

        public static string S(JsonNode n, string chave)
        {
            var v = n?[chave];
            return v == null ? "" : v is JsonValue jv && jv.TryGetValue(out string s) ? s : v.ToJsonString();
        }

        public static bool Bool(JsonNode n, string chave) { return n?[chave] is JsonValue v && v.TryGetValue(out bool b) && b; }

        public static IEnumerable<JsonNode> Lista(JsonNode n) { return n is JsonArray a ? a.Where(x => x != null) : Enumerable.Empty<JsonNode>(); }

        public static string F(double v, int casas, string unidade = null)
        {
            if (double.IsNaN(v)) return "—";
            return Num.Fmt(v, casas) + (string.IsNullOrEmpty(unidade) ? "" : " " + unidade);
        }

        // ---------- moldura: cabeçalho de veredito / corpo / rodapé (mesmo esqueleto da JanelaPlaca) ----------
        public static void Moldar(Window w, string titulo, double larg, double alt)
        {
            w.Title = titulo;
            Rect wa = SystemParameters.WorkArea;
            w.Width = Math.Min(larg, wa.Width - 40); w.Height = Math.Min(alt, wa.Height - 40);
            w.MinWidth = 720; w.MinHeight = 480;
            w.UseLayoutRounding = true; w.SnapsToDevicePixels = true;
            TextOptions.SetTextFormattingMode(w, TextFormattingMode.Ideal);
        }

        public static Grid Raiz(UIElement cabecalho, UIElement corpo, UIElement rodape)
        {
            var g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.Children.Add(cabecalho);
            Grid.SetRow(corpo, 1); g.Children.Add(corpo);
            Grid.SetRow(rodape, 2); g.Children.Add(rodape);
            return g;
        }

        public static Border Cabecalho(Nivel nv, string glifo, string titulo, string sub, UIElement direita = null)
        {
            var c = Cores(nv);
            var dp = new DockPanel { LastChildFill = true };
            if (direita != null) { DockPanel.SetDock(direita, Dock.Right); dp.Children.Add(direita); }
            var icone = new Border
            {
                Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Background = c.bg, BorderBrush = c.bd, BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = glifo, FontSize = 15, FontWeight = FontWeights.Bold, Foreground = c.fg, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            DockPanel.SetDock(icone, Dock.Left); dp.Children.Add(icone);
            var txt = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            txt.Children.Add(Tx(titulo, 16, nv == Nivel.Ok || nv == Nivel.Alerta ? c.fg : TemaPlant.BTexto1, FontWeights.SemiBold));
            if (!string.IsNullOrEmpty(sub))
            {
                var t = Tx(sub, 11.5, TemaPlant.BTexto2); t.Margin = new Thickness(0, 2, 0, 0); t.TextTrimming = TextTrimming.CharacterEllipsis;
                txt.Children.Add(Dica(t, sub));
            }
            dp.Children.Add(txt);
            return new Border { Background = TemaPlant.BSup1, BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(16, 10, 16, 10), Child = dp };
        }

        // Rodapé: controles à esquerda, botões à direita (o último é o principal se principal = true).
        public static Border Rodape(UIElement esquerda, params Button[] direita)
        {
            var dp = new DockPanel { LastChildFill = true };
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (var b in direita) { b.Height = 28; b.MinWidth = 92; sp.Children.Add(b); }
            DockPanel.SetDock(sp, Dock.Right); dp.Children.Add(sp);
            if (esquerda != null) dp.Children.Add(esquerda);
            else dp.Children.Add(new Border());
            return new Border { Background = TemaPlant.BSup1, BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(14, 8, 14, 8), Child = dp };
        }

        // Cartão técnico (GUIA 5.4) com título de seção
        public static Border Cartao(string titulo, UIElement conteudo, UIElement direita = null, Thickness? margem = null)
        {
            var dp = new DockPanel();
            var t = TituloSecao(titulo, direita, 0);
            DockPanel.SetDock(t, Dock.Top); dp.Children.Add(t);
            dp.Children.Add(conteudo);
            return new Border
            {
                Background = TemaPlant.BSup2, BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 10, 12, 10), Margin = margem ?? new Thickness(0, 0, 0, 10), Child = dp
            };
        }

        // Estado vazio (GUIA 5.11)
        public static FrameworkElement Vazio(string icone, string titulo, string sub)
        {
            var sp = new StackPanel { MaxWidth = 320, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 24, 16, 24) };
            if (Icones.Existe(icone))
            {
                var ic = (FrameworkElement)Icones.Criar(icone, 32); ic.Opacity = 0.6; ic.HorizontalAlignment = HorizontalAlignment.Center; ic.Margin = new Thickness(0, 0, 0, 10);
                sp.Children.Add(ic);
            }
            var t = Tx(titulo, 13, TemaPlant.BTexto2, FontWeights.SemiBold); t.HorizontalAlignment = HorizontalAlignment.Center;
            sp.Children.Add(t);
            var s = Tx(sub, 11.5, TemaPlant.BTexto3, null, true); s.TextAlignment = TextAlignment.Center; s.Margin = new Thickness(0, 4, 0, 0);
            sp.Children.Add(s);
            return sp;
        }

        public static TextBlock Num1(string s, Brush cor = null, bool negrito = false)
        {
            var t = Tx(s, 12, cor ?? TemaPlant.BTexto1, negrito ? FontWeights.SemiBold : FontWeights.Normal);
            Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
            t.HorizontalAlignment = HorizontalAlignment.Right;
            return t;
        }

        // Barra de taxa + percentual numa célula (taxa > 1 ou !ok = vermelho)
        public static FrameworkElement CelTaxa(double taxa, bool ok, double largBarra = 90)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(largBarra) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
            var b = BarraCap(taxa, ok, 6); g.Children.Add(b);
            var p = Num1(double.IsNaN(taxa) ? "—" : Pct(taxa), double.IsNaN(taxa) ? TemaPlant.BTexto3 : Cores(NivelAp(taxa, ok)).fg, true);
            Grid.SetColumn(p, 1); g.Children.Add(p);
            return g;
        }

        public class Col
        {
            public string Titulo; public double Larg; public bool Direita; public string Dica;
            public Col(string titulo, double larg, bool direita = false, string dica = null) { Titulo = titulo; Larg = larg; Direita = direita; Dica = dica; }
        }

        // Tabela tabular (GUIA 5.7): cabeçalho fixo, linhas de 28 px alternadas, hover, linha selecionada em Accent.Subtle.
        // Larg = 0 → coluna elástica. ordenar(c) torna o cabeçalho clicável; colOrd/desc desenham ▴/▾.
        // ponytail: linhas num StackPanel (sem virtualização); trocar por ListBox virtualizado se passar de ~2000 barras.
        public static FrameworkElement Grade(Col[] cols, IList<UIElement[]> linhas, int sel = -1, Action<int> clique = null, Action<int> duplo = null,
            Action<int> ordenar = null, int colOrd = -1, bool desc = false, Func<int, string> dicaLinha = null)
        {
            Grid Linha()
            {
                var g = new Grid();
                foreach (var c in cols) g.ColumnDefinitions.Add(new ColumnDefinition { Width = c.Larg > 0 ? new GridLength(c.Larg) : new GridLength(1, GridUnitType.Star) });
                return g;
            }
            var cab = Linha();
            for (int i = 0; i < cols.Length; i++)
            {
                int k = i;
                string seta = colOrd == i ? (desc ? " ▾" : " ▴") : "";
                var t = Tx(cols[i].Titulo + seta, 11, colOrd == i ? TemaPlant.BTexto1 : TemaPlant.BTexto2, FontWeights.SemiBold);
                t.HorizontalAlignment = cols[i].Direita ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                var bc = new Border { Padding = new Thickness(8, 0, 8, 0), Height = 30, Background = Brushes.Transparent, Child = t };
                if (ordenar != null)
                {
                    bc.Cursor = Cursors.Hand;
                    bc.MouseEnter += (s, e) => t.Foreground = TemaPlant.BTexto1;
                    bc.MouseLeave += (s, e) => t.Foreground = colOrd == k ? TemaPlant.BTexto1 : TemaPlant.BTexto2;
                    bc.MouseLeftButtonDown += (s, e) => { e.Handled = true; ordenar(k); };
                }
                Dica(bc, cols[i].Dica ?? (ordenar != null ? "Clique para ordenar por " + cols[i].Titulo.ToLowerInvariant() + "." : null));
                Grid.SetColumn(bc, i); cab.Children.Add(bc);
            }
            var cabB = new Border { Background = TemaPlant.BSup1, BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(0, 0, 0, 1), Child = cab };

            var corpo = new StackPanel();
            for (int r = 0; r < linhas.Count; r++)
            {
                int ir = r;
                var g = Linha();
                for (int i = 0; i < cols.Length && i < linhas[r].Length; i++)
                {
                    var el = linhas[r][i];
                    if (el == null) continue;
                    if (el is FrameworkElement fe)
                    {
                        fe.VerticalAlignment = VerticalAlignment.Center;
                        if (fe is TextBlock tb && cols[i].Direita) { tb.HorizontalAlignment = HorizontalAlignment.Right; tb.TextAlignment = TextAlignment.Right; }
                        if (fe is TextBlock tb2) tb2.TextTrimming = TextTrimming.CharacterEllipsis;
                        if (fe is Border) fe.HorizontalAlignment = cols[i].Direita ? HorizontalAlignment.Right : HorizontalAlignment.Left;   // chips
                    }
                    var cel = new Border { Padding = new Thickness(8, 4, 8, 4), Child = el };
                    Grid.SetColumn(cel, i); g.Children.Add(cel);
                }
                Brush fundo = r == sel ? TemaPlant.BAcentoSutil : r % 2 == 0 ? TemaPlant.BSup0 : TemaPlant.BSupAlt;
                var lb = new Border
                {
                    MinHeight = 28, Background = fundo, BorderBrush = r == sel ? TemaPlant.BAcento : TemaPlant.BBordaSutil,
                    BorderThickness = r == sel ? new Thickness(2, 0, 0, 1) : new Thickness(0, 0, 0, 1), Child = g
                };
                if (clique != null || duplo != null)
                {
                    lb.Cursor = Cursors.Hand;
                    lb.MouseEnter += (s, e) => { if (ir != sel) lb.Background = TemaPlant.BSup3; };
                    lb.MouseLeave += (s, e) => { if (ir != sel) lb.Background = fundo; };
                    lb.MouseLeftButtonDown += (s, e) =>
                    {
                        e.Handled = true;
                        if (e.ClickCount == 2 && duplo != null) duplo(ir);
                        else clique?.Invoke(ir);
                    };
                }
                if (dicaLinha != null) Dica(lb, dicaLinha(r));
                corpo.Children.Add(lb);
            }
            var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = corpo };
            sv.ScrollChanged += (s, e) => cabB.Padding = new Thickness(0, 0, Math.Max(0, sv.ActualWidth - sv.ViewportWidth), 0);
            var dp = new DockPanel { Background = TemaPlant.BSup0 };
            DockPanel.SetDock(cabB, Dock.Top); dp.Children.Add(cabB);
            dp.Children.Add(sv);
            return new Border { BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), ClipToBounds = true, Child = dp };
        }

        // Campo de busca (28 px) com ícone e texto de exemplo; mudou(texto) a cada tecla.
        public static Border CaixaBusca(string exemplo, double larg, Action<string> mudou, out TextBox caixa)
        {
            var tb = new TextBox
            {
                Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = TemaPlant.BTexto1, CaretBrush = TemaPlant.BTexto1,
                VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(2, 0, 4, 0), FontSize = 12.5
            };
            var ph = Tx(exemplo, 12, TemaPlant.BTextoMudo); ph.IsHitTestVisible = false; ph.Margin = new Thickness(4, 0, 0, 0);
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (Icones.Existe("busca")) { var ic = (FrameworkElement)Icones.Criar("busca", 14); ic.HorizontalAlignment = HorizontalAlignment.Center; ic.VerticalAlignment = VerticalAlignment.Center; ic.Opacity = 0.7; g.Children.Add(ic); }
            Grid.SetColumn(tb, 1); g.Children.Add(tb);
            Grid.SetColumn(ph, 1); g.Children.Add(ph);
            var bd = new Border { Height = 28, Width = larg, CornerRadius = new CornerRadius(4), Background = TemaPlant.BSup3, BorderBrush = TemaPlant.BBorda, BorderThickness = new Thickness(1), Child = g, VerticalAlignment = VerticalAlignment.Center };
            tb.TextChanged += (s, e) => { ph.Visibility = tb.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; mudou(tb.Text); };
            tb.GotKeyboardFocus += (s, e) => bd.BorderBrush = TemaPlant.BBordaFoco;
            tb.LostKeyboardFocus += (s, e) => bd.BorderBrush = TemaPlant.BBorda;
            System.Windows.Automation.AutomationProperties.SetName(tb, exemplo);
            caixa = tb;
            return bd;
        }

        // Nome legível do critério do motor (fissuracao_wk → fissuração wk)
        static readonly Dictionary<string, string> Criterios = new Dictionary<string, string>
        {
            ["interacao"] = "interação N + M", ["esbeltez"] = "esbeltez", ["normal_compressao"] = "força normal", ["normal"] = "força normal", ["cortante_y"] = "cortante em y", ["cortante_x"] = "cortante em x", ["compressao"] = "compressão",
            ["tracao"] = "tração", ["flexao_x"] = "flexão em x", ["flexao_y"] = "flexão em y", ["cortante"] = "cortante",
            ["momento_cortante"] = "momento + cortante", ["distorcional"] = "flambagem distorcional", ["torcao"] = "torção",
            ["fissuracao_wk"] = "fissuração (wk)", ["x_d"] = "ductilidade (x/d)", ["torcao_interacao"] = "torção + cortante",
            ["cisalhamento_VRd2"] = "cisalhamento (VRd2)", ["cisalhamento_VRd3"] = "cisalhamento (VRd3)", ["As_max"] = "armadura máxima", ["As_min"] = "armadura mínima",
            ["flecha"] = "flecha", ["cobrimento"] = "cobrimento", ["nao_verificada"] = "não verificada"
        };
        public static string NomeCriterio(string c) { return string.IsNullOrEmpty(c) ? "—" : Criterios.TryGetValue(c, out var s) ? s : c.Replace('_', ' '); }

        static readonly Dictionary<string, string> Tipos = new Dictionary<string, string> { ["aco"] = "Aço", ["pff"] = "Formado a frio", ["concreto"] = "Concreto" };
        public static string NomeTipo(string t) { return Tipos.TryGetValue(t ?? "", out var s) ? s : t; }

        // Troca a tabela do host mantendo a rolagem (clicar numa linha remonta a tabela).
        public static void TrocarTabela(Border host, FrameworkElement nova)
        {
            static ScrollViewer Sv(object t) { return ((t as Border)?.Child as DockPanel)?.Children.OfType<ScrollViewer>().FirstOrDefault(); }
            double y = Sv(host.Child)?.VerticalOffset ?? 0;
            host.Child = nova;
            var sv = Sv(nova);
            if (sv != null && y > 0) sv.Loaded += (s, e) => sv.ScrollToVerticalOffset(y);
        }

        // Pares rótulo / valor em 2 colunas (ficha de propriedades)
        public static FrameworkElement Ficha(IList<(string rot, string val, string dica)> pares, double largRot = 120)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(largRot) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(largRot) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < pares.Count; i++)
            {
                int lin = i / 2, col = (i % 2) * 3;
                while (g.RowDefinitions.Count <= lin) g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(26) });
                var r = Tx(pares[i].rot, 12, TemaPlant.BTexto2); Dica(r, pares[i].dica);
                Grid.SetRow(r, lin); Grid.SetColumn(r, col); g.Children.Add(r);
                var v = Num1(pares[i].val);
                Grid.SetRow(v, lin); Grid.SetColumn(v, col + 1); g.Children.Add(v);
            }
            return g;
        }
    }
}
