// Galeria "Treliças e contraventamentos" (docs/v3/TRELICAS.md): cartões com miniatura de cada tipo à esquerda, prévia ao vivo
// no centro (a mesma geometria do gerador do motor: POST /api/gerar-estrutura/previa numa cópia do modelo) e parâmetros à
// direita. "Inserir" = um POST /api/gerar-estrutura no documento espacial ativo (um Ctrl+Z desfaz tudo) ou, sem documento,
// um modelo espacial novo. Contraventamento e terças usam a seleção da Vista3D (2 barras, 4 nós ou as tesouras).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using static NovoFtool.UiPlaca;

namespace NovoFtool
{
    public partial class DocumentoEspacial
    {
        // Prévia do gerador sobre ESTE modelo (o servidor não muda: a rota trabalha numa cópia).
        public Task<JsonNode> Previa(JsonObject dados) => Motor.Exclusivo(async () =>
        {
            await Garantir();
            return await Motor.Post("/api/gerar-estrutura/previa", dados);
        });

        public static Task<JsonNode> PreviaVazia(JsonObject dados) => Motor.Exclusivo(() => Motor.Post("/api/gerar-estrutura/previa?vazio=1", dados));

        // Gerador num modelo vazio: vira um documento novo.
        public static Task<DocumentoEspacial> NovoComGerador(JsonObject dados, string titulo) => Criar(async () =>
        {
            await Motor.Post("/api/modelo/novo", new JsonObject { ["nome"] = titulo });
            await Motor.Post("/api/gerar-estrutura", dados);
        }, titulo, null);
    }

    public partial class MainWindow
    {
        JanelaTrelicas janTrelicas;

        // Gancho único (MainWindow.xaml.cs, depois de MontarDesenho3D): botão em Modelo › Gerar + entradas da paleta (Ctrl+K).
        internal void MontarTrelicas()
        {
            var gerar = abaModelo.Groups.FirstOrDefault(g => (g.Header as string) == "Gerar");
            gerar?.Items.Add(Botao("trelica", "Treliças", () => AbrirTrelicas(null),
                "Galeria de treliças planas (Pratt, Howe, Warren, K, Fink), tesouras em série, contraventamentos (X, K, V, Λ, mão-francesa) e terças, com prévia ao vivo. Insere no modelo espacial ativo (um Ctrl+Z desfaz).", null, true));
            foreach (var (nome, item, dica) in new[]
            {
                ("Treliça plana", "pratt", "Pratt, Howe, Warren, K ou Fink; banzos paralelos, uma água, duas águas ou em arco."),
                ("Tesouras em série", "serie", "Tesouras repetidas ao longo de um eixo, com espaçamento e terças."),
                ("Contraventamento", "cx", "X, diagonal, K, V, Λ ou mão-francesa entre dois pórticos selecionados (2 barras ou 4 nós)."),
                ("Terças e tirantes", "tercas", "Terças sobre as tesouras selecionadas, com espaçamento máximo e linhas de tirantes."),
            })
                Reg("3d", nome, null, dica, () => AbrirTrelicas(item));
        }

        internal JanelaTrelicas AbrirTrelicas(string item)
        {
            Bastidor.IsOpen = false;
            if (janTrelicas == null || !janTrelicas.IsLoaded)
            {
                janTrelicas = new JanelaTrelicas(this) { Owner = this };
                janTrelicas.Show();
            }
            else janTrelicas.Activate();
            if (item != null) janTrelicas.Escolher(item);
            return janTrelicas;
        }

        internal DocumentoEspacial EspTrelicas => Esp;
        internal Task RefazerTrelicas() => TrocarEspacial(false);

        // Ids da seleção da Vista3D (clique, janela, árvore e tabelas caem todos nela).
        internal (List<string> Nos, List<string> Barras) SelecaoTrelicas()
        {
            var nos = vista3D?.Selecao.Nos.ToList() ?? new List<string>();
            var barras = vista3D?.Selecao.Barras.ToList() ?? new List<string>();
            return (nos, barras);
        }

        internal async Task<bool> InserirTrelica(JsonObject dados, string titulo)
        {
            if (Esp == null)
            {
                await AbrirEspacial(() => DocumentoEspacial.NovoComGerador(dados, titulo), "gerando " + titulo);
                return Esp != null;
            }
            bool ok = false;
            await OperarEspacial(async d => { await d.Executar(() => Motor.Post("/api/gerar-estrutura", dados)); ok = true; }, titulo + " inserido (Ctrl+Z desfaz).");
            return ok;
        }
    }

    public class JanelaTrelicas : JanelaEscura
    {
        // cartões: chave, família, modelo do motor, nome
        static readonly (string Chave, string Familia, string Modelo, string Nome)[] Itens =
        {
            ("pratt", "trelica", "pratt", "Pratt"), ("howe", "trelica", "howe", "Howe"), ("warren", "trelica", "warren", "Warren"),
            ("warren_v", "trelica", "warren_v", "Warren c/ montantes"), ("k", "trelica", "k", "K"), ("fink", "trelica", "fink", "Fink"),
            ("serie", "serie", "howe", "Tesouras em série"),
            ("cx", "contra", "x", "X"), ("cd", "contra", "diagonal", "Diagonal"), ("ck", "contra", "k", "K"),
            ("cv", "contra", "v", "V"), ("cl", "contra", "lambda", "Λ (chevron)"), ("cm", "contra", "mao_francesa", "Mão-francesa"),
            ("cob", "contra", "x", "Cobertura (X)"), ("tercas", "tercas", "", "Terças e tirantes"),
        };
        static readonly string[] Grupos = { "trelica", "serie", "contra", "tercas" };
        static readonly Dictionary<string, string> NomeGrupo = new Dictionary<string, string>
            { ["trelica"] = "Treliças planas", ["serie"] = "Em série", ["contra"] = "Contraventamento", ["tercas"] = "Cobertura" };
        static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

        readonly MainWindow w;
        readonly Dictionary<string, Border> cartoes = new Dictionary<string, Border>();
        readonly Dictionary<string, Canvas> miniaturas = new Dictionary<string, Canvas>();
        readonly Dictionary<string, string> valores = new Dictionary<string, string>();   // campos guardados entre cartões
        readonly Dictionary<string, Control> campos = new Dictionary<string, Control>();
        readonly StackPanel colParam = new StackPanel { Margin = new Thickness(16, 14, 16, 14) };
        readonly Canvas tela = new Canvas { ClipToBounds = true };
        readonly Border hostSeg = new Border { Margin = new Thickness(12, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        readonly TextBlock lblInfo = Tx("", 11.5, TemaPlant.BTexto2), lblStatus = Tx("", 11.5, TemaPlant.BTexto2, null, true),
                           lblDestino = Tx("", 11.5, TemaPlant.BTexto2), lblSel = Tx("", 11, TemaPlant.BTexto2, null, true);
        readonly Button btInserir;
        readonly DispatcherTimer tmPrevia = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        string item = "pratt";
        bool? iso;                 // null = automático (elevação para treliça isolada)
        JsonNode previa;           // última prévia válida
        int pedido;                // ignora respostas velhas
        bool montando;

        public string Item => item;
        public JsonNode UltimaPrevia => previa;
        public string Status => lblStatus.Text;

        public JanelaTrelicas(MainWindow w)
        {
            this.w = w;
            Title = "Treliças e contraventamentos";
            Rect wa = SystemParameters.WorkArea;
            Width = Math.Max(1080, Math.Min(1280, wa.Width - 80)); Height = Math.Max(660, Math.Min(760, wa.Height - 60));
            MinWidth = 1000; MinHeight = 600; ShowInTaskbar = false;
            UseLayoutRounding = true; SnapsToDevicePixels = true;
            tmPrevia.Tick += async (s, e) => { tmPrevia.Stop(); await Atualizar(); };

            var raiz = new Grid();
            raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            raiz.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // topo: título + destino
            var topo = new DockPanel { Margin = new Thickness(16, 9, 16, 9) };
            DockPanel.SetDock(lblDestino, Dock.Right);
            topo.Children.Add(lblDestino);
            topo.Children.Add(Tx("Treliças e contraventamentos", 14, TemaPlant.BTexto1, FontWeights.SemiBold));
            raiz.Children.Add(new Border { Background = TemaPlant.BSup1, BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(0, 0, 0, 1), Child = topo });

            var corpo = new Grid();
            corpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
            corpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            corpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });
            Grid.SetRow(corpo, 1); raiz.Children.Add(corpo);

            // esquerda: galeria
            var gal = new StackPanel { Margin = new Thickness(12, 8, 4, 12) };
            foreach (string g in Grupos)
            {
                gal.Children.Add(new TextBlock { Text = NomeGrupo[g].ToUpperInvariant(), FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = TemaPlant.BTexto2, Margin = new Thickness(2, 8, 0, 6) });
                var wp = new WrapPanel();
                foreach (var it in Itens.Where(x => x.Familia == g || (g == "tercas" && x.Chave == "cob")))
                {
                    if (g == "contra" && it.Chave == "cob") continue;
                    wp.Children.Add(Cartao(it.Chave, it.Nome));
                }
                gal.Children.Add(wp);
            }
            var svGal = new ScrollViewer { Content = gal, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            corpo.Children.Add(new Border { Background = TemaPlant.BSup1, BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(0, 0, 1, 0), Child = svGal });

            // centro: prévia
            var centro = new Grid { Background = TemaPlant.BBgCanvas };
            centro.Children.Add(tela);
            centro.Children.Add(hostSeg);
            lblInfo.Margin = new Thickness(14, 0, 14, 10); lblInfo.VerticalAlignment = VerticalAlignment.Bottom; lblInfo.HorizontalAlignment = HorizontalAlignment.Left;
            centro.Children.Add(lblInfo);
            tela.SizeChanged += (s, e) => Desenhar();
            Grid.SetColumn(centro, 1); corpo.Children.Add(centro);

            // direita: parâmetros
            var svPar = new ScrollViewer { Content = colParam, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var dir = new Border { Background = TemaPlant.BSup1, BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(1, 0, 0, 0), Child = svPar };
            Grid.SetColumn(dir, 2); corpo.Children.Add(dir);

            // rodapé
            var rod = new DockPanel { Margin = new Thickness(16, 9, 16, 9) };
            var bts = new StackPanel { Orientation = Orientation.Horizontal };
            bts.Children.Add(B("Fechar", Close));
            btInserir = B("Inserir", () => _ = Inserir(), true, "Insere no modelo espacial ativo (ou num modelo novo). Um Ctrl+Z desfaz tudo.");
            bts.Children.Add(btInserir);
            DockPanel.SetDock(bts, Dock.Right);
            rod.Children.Add(bts);
            rod.Children.Add(lblStatus);
            var bRod = new Border { Background = TemaPlant.BSup1, BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(0, 1, 0, 0), Child = rod };
            Grid.SetRow(bRod, 2); raiz.Children.Add(bRod);
            Content = raiz;

            if (w.vista3D != null) w.vista3D.SelecaoMudou += AoMudarSelecao;
            Closed += (s, e) => { if (w.vista3D != null) w.vista3D.SelecaoMudou -= AoMudarSelecao; };
            Loaded += async (s, e) => await Miniaturas();
            Escolher("pratt");
        }

        void AoMudarSelecao() => Dispatcher.BeginInvoke(new Action(MostrarSel));

        // ---------------- galeria ----------------

        Border Cartao(string chave, string nome)
        {
            var cv = new Canvas { Width = 114, Height = 50, ClipToBounds = true };
            miniaturas[chave] = cv;
            var sp = new StackPanel();
            sp.Children.Add(cv);
            sp.Children.Add(new TextBlock { Text = nome, FontSize = 11.5, Foreground = TemaPlant.BTexto1, Margin = new Thickness(0, 5, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
            var b = new Border
            {
                Width = 136, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(10, 8, 10, 7), CornerRadius = new CornerRadius(4),
                Background = TemaPlant.BSup2, BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(1), Child = sp, Cursor = System.Windows.Input.Cursors.Hand,
            };
            System.Windows.Automation.AutomationProperties.SetName(b, nome);
            b.MouseEnter += (s, e) => { if (item != chave) b.Background = TemaPlant.BSup3; };
            b.MouseLeave += (s, e) => { if (item != chave) b.Background = TemaPlant.BSup2; };
            b.MouseLeftButtonDown += (s, e) => { e.Handled = true; Escolher(chave); };
            cartoes[chave] = b;
            return b;
        }

        // Miniaturas: treliças pela prévia do motor num modelo vazio; contraventamentos e terças por esquema fixo.
        async Task Miniaturas()
        {
            foreach (var it in Itens)
            {
                var cv = miniaturas[it.Chave];
                if (it.Familia == "trelica" || it.Familia == "serie")
                {
                    var d = new JsonObject
                    {
                        ["tipo"] = it.Familia == "serie" ? "trelicas_serie" : "trelica_plana", ["material"] = "A-572 GR.50", ["modelo"] = it.Modelo,
                        ["secoes"] = new JsonObject { ["banzo"] = "W150x13", ["diagonal"] = "W150x13" },
                        ["banzo"] = it.Modelo == "fink" || it.Familia == "serie" ? "duas_aguas" : "paralelo",
                        ["vao"] = 12.0, ["altura"] = it.Modelo == "fink" || it.Familia == "serie" ? 0.0 : 2.0,
                    };
                    if ((string)d["banzo"] != "paralelo") d["altura2"] = 3.0;
                    if (it.Modelo != "fink") d["paineis"] = 6;
                    if (it.Familia == "serie") { d["n"] = 3; d["espacamento"] = 5.0; }
                    try { var p = await DocumentoEspacial.PreviaVazia(d); DesenharEm(cv, p, null, it.Familia == "serie", 1.2, 0.8); }
                    catch (Exception) { /* sem motor: cartão sem miniatura */ }
                }
                else Esquema(cv, it.Chave);
            }
        }

        // Esquema de painel (pilares e viga em cinza, peças novas em acento), coordenadas 0..1.
        static void Esquema(Canvas cv, string chave)
        {
            double W = cv.Width, H = cv.Height, m = 6;
            Point P(double x, double y) => new Point(m + x * (W - 2 * m), H - m - y * (H - 2 * m));
            void L(double x1, double y1, double x2, double y2, bool novo) => Linha(cv, P(x1, y1), P(x2, y2), novo ? TemaPlant.BAcento : TemaPlant.BTexto3, novo ? 1.4 : 1.2);
            if (chave == "tercas" || chave == "cob")
            {
                // planta da água: tesouras verticais (cinza), terças/diagonais em acento
                for (int i = 0; i < 4; i++) L(i / 3.0, 0, i / 3.0, 1, false);
                if (chave == "tercas") for (int j = 0; j <= 4; j++) L(0, j / 4.0, 1, j / 4.0, true);
                else for (int j = 0; j < 2; j++) { L(0, j / 2.0, 1 / 3.0, (j + 1) / 2.0, true); L(1 / 3.0, j / 2.0, 0, (j + 1) / 2.0, true); }
                if (chave == "tercas") for (int i = 0; i < 3; i++) L((i + 0.5) / 3.0, 0, (i + 0.5) / 3.0, 1, true);
                return;
            }
            L(0.25, 0, 0.25, 1, false); L(0.75, 0, 0.75, 1, false); L(0.25, 1, 0.75, 1, false);
            switch (chave)
            {
                case "cx": L(0.25, 0, 0.75, 1, true); L(0.75, 0, 0.25, 1, true); break;
                case "cd": L(0.25, 0, 0.75, 1, true); break;
                case "ck": L(0.25, 0, 0.75, 0.5, true); L(0.25, 1, 0.75, 0.5, true); break;
                case "cv": L(0.25, 1, 0.5, 0, true); L(0.75, 1, 0.5, 0, true); break;
                case "cl": L(0.25, 0, 0.5, 1, true); L(0.75, 0, 0.5, 1, true); break;
                case "cm": L(0.25, 0.7, 0.4, 1, true); L(0.75, 0.7, 0.6, 1, true); break;
            }
        }

        static void Linha(Canvas cv, Point a, Point b, Brush cor, double esp, bool tracejado = false)
        {
            var l = new Line { X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y, Stroke = cor, StrokeThickness = esp, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            if (tracejado) l.StrokeDashArray = new DoubleCollection { 3, 2 };
            cv.Children.Add(l);
        }

        public void Escolher(string chave)
        {
            if (!cartoes.ContainsKey(chave)) return;
            GuardarValores();
            item = chave;
            foreach (var kv in cartoes)
            {
                bool at = kv.Key == chave;
                kv.Value.Background = at ? TemaPlant.BSup3 : TemaPlant.BSup2;
                kv.Value.BorderBrush = at ? TemaPlant.BAcento : TemaPlant.BBordaSutil;
            }
            iso = null;
            MontarParametros();
            if (Familia != "trelica" && Familia != "serie" && string.IsNullOrWhiteSpace(Texto("alvo"))) UsarSelecao(false);
            AgendarPrevia();
        }

        (string Chave, string Familia, string Modelo, string Nome) It => Itens.First(x => x.Chave == item);
        string Familia => It.Familia;

        // ---------------- parâmetros ----------------

        string V(string k, string padrao) => valores.TryGetValue(k, out var v) ? v : padrao;

        void GuardarValores()
        {
            foreach (var kv in campos)
                valores[kv.Key] = kv.Value is TextBox t ? t.Text : kv.Value is ComboBox c ? c.SelectedIndex.ToString(Ci) : "";
        }

        void Secao(string t) => colParam.Children.Add(new TextBlock { Text = t.ToUpperInvariant(), FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = TemaPlant.BTexto2, Margin = new Thickness(0, 12, 0, 4) });

        void Linha(string k, string rot, Control ed, string dica = null)
        {
            var g = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(132) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.Children.Add(Dica(Tx(rot, 12, TemaPlant.BTexto2), dica));
            ed.HorizontalAlignment = HorizontalAlignment.Stretch;
            Grid.SetColumn(ed, 1); g.Children.Add(ed);
            colParam.Children.Add(g);
            campos[k] = ed;
        }

        void Num(string k, string rot, string padrao, string dica = null)
        {
            var t = Campo(V(k, padrao), 160, dica);
            t.TextChanged += (s, e) => AgendarPrevia();
            Linha(k, rot, t, dica);
        }

        void Opcao(string k, string rot, string[] nomes, int padrao, string dica = null)
        {
            var c = new ComboBox();
            foreach (string n in nomes) c.Items.Add(n);
            int i = int.TryParse(V(k, padrao.ToString(Ci)), NumberStyles.Integer, Ci, out int x) ? x : padrao;
            c.SelectedIndex = Math.Max(0, Math.Min(nomes.Length - 1, i));
            c.SelectionChanged += (s, e) => { if (!montando) { if (k == "banzo") { GuardarValores(); MontarParametros(); } AgendarPrevia(); } };
            Linha(k, rot, c, dica);
        }

        static readonly string[] Banzos = { "paralelo", "uma_agua", "duas_aguas", "arco" };
        static readonly string[] NomesBanzo = { "Paralelos", "Uma água (trapézio)", "Duas águas", "Arco" };
        static readonly string[] Rotulas = { "diagonais", "todas", "nenhuma" };
        static readonly string[][] Apoios = { new[] { "rotula", "movel" }, new[] { "rotula", "rotula" }, new[] { "engaste", "engaste" }, new[] { "livre", "livre" } };

        void MontarParametros()
        {
            montando = true;
            campos.Clear();
            colParam.Children.Clear();
            var it = It;
            colParam.Children.Add(Tx(it.Nome, 15, TemaPlant.BTexto1, FontWeights.SemiBold));
            colParam.Children.Add(new TextBlock { Text = Descricao(it.Chave), FontSize = 11.5, Foreground = TemaPlant.BTexto2, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 2) });
            if (Familia == "trelica" || Familia == "serie")
            {
                bool fink = it.Modelo == "fink";
                Secao("Geometria");
                if (!fink) Opcao("banzo", "Banzo superior", NomesBanzo, Familia == "serie" ? 2 : 0);
                string bz = fink ? "duas_aguas" : Banzo();
                Num("vao", "Vão (m)", Familia == "serie" ? "15" : "12", "Distância entre apoios. Com \"Entre os nós\" o vão é a distância entre eles.");
                if (!fink) Num("altura", bz == "paralelo" ? "Altura (m)" : "Altura nos apoios (m)", bz == "duas_aguas" ? "0" : "1,5", "0 nos apoios = tesoura triangular.");
                if (bz != "paralelo") Num("altura2", bz == "uma_agua" ? "Altura à direita (m)" : bz == "arco" ? "Altura no meio (m)" : "Altura na cumeeira (m)", fink ? "2,5" : "2,5");
                if (!fink) Num("paineis", "Painéis", "6", "Divisões do banzo inferior.");
                if (Familia == "serie")
                {
                    Num("n", "Quantidade", "4");
                    Num("espacamento", "Espaçamento (m)", "6", "Distância entre tesouras, na horizontal perpendicular ao vão.");
                }
                Secao("Seções");
                Num("s_banzo", "Banzos", "Ue 150x60x20x3,00", "Catálogo do motor (W, HP, U, Ue), \"Tubo d x t\" (mm), \"Ø d\" (mm) ou \"Ret b x h\" (cm).");
                Num("s_diag", "Diagonais", "U 75x40x3,00");
                Num("s_mont", "Montantes", "U 75x40x3,00");
                if (Familia == "serie") Num("s_terca", "Terças (vazio = sem)", "Ue 150x60x20x2,65");
                Secao("Ligações e apoios");
                Opcao("rotulas", "Rótulas", new[] { "Diagonais e montantes", "Todas as barras", "Nenhuma (nós rígidos)" }, 0, "Barras rotuladas só têm esforço axial.");
                Opcao("apoios", "Apoios", new[] { "Rótula + móvel", "Rótula + rótula", "Engaste + engaste", "Livre (sobre pilares)" }, 0);
                Secao("Onde");
                Num("origem", "Origem x; y; z (m)", "0; 0; 0", "Ponto do apoio esquerdo (início do banzo inferior).");
                Opcao("eixo", "Vão ao longo de", new[] { "X", "Y" }, 0);
                if (Familia == "trelica")
                {
                    Num("alvo", "Entre os nós", "", "Dois ids de nós existentes (ex.: topos de pilares): o banzo inferior liga os dois e o vão é a distância entre eles. Vazio = usa a origem.");
                    BotaoSel("Usar seleção");
                }
            }
            else
            {
                Secao(Familia == "tercas" ? "Tesouras" : "Painel");
                Num("alvo", Familia == "tercas" ? "Tesouras (barras)" : "Barras ou nós", "",
                    Familia == "tercas" ? "Ids das tesouras/vigas da água (2 ou mais, em qualquer ordem)." : "2 barras paralelas (pilares de dois pórticos, duas tesouras) ou 4 nós n1 n2 n3 n4 (n1-n2 embaixo, n4 sobre n1).");
                BotaoSel("Usar seleção");
                colParam.Children.Add(lblSel);
                Secao("Geometria");
                if (Familia == "tercas")
                {
                    Num("espacamento", "Espaçamento máx. (m)", "1,5", "As terças ficam igualmente espaçadas, com o maior espaçamento até este valor.");
                    Num("tirantes", "Linhas de tirantes", "1", "Por vão entre tesouras (0 = sem tirantes).");
                    Secao("Seções");
                    Num("s_terca", "Terças", "Ue 150x60x20x2,65");
                    Num("s_tirante", "Tirantes", "Ø 12,5");
                }
                else
                {
                    if (it.Modelo == "x" || it.Modelo == "diagonal") Num("divisoes", "Painéis", it.Chave == "cob" ? "2" : "1", "Divide o vão em painéis ao longo das barras (escoras entre eles).");
                    if (it.Modelo == "mao_francesa") Num("distancia", "Distância do canto (m)", "0,8");
                    Secao("Seções");
                    Num("s_contra", "Diagonais", "Tubo 60,3x3,6");
                    if (it.Modelo != "x" && it.Modelo != "diagonal" || it.Chave == "cob") Num("s_escora", "Escoras (vazio = igual)", "");
                }
            }
            Secao("Material");
            Num("material", "Material", "", "Vazio = o primeiro material do modelo (ou A-572 GR.50 num modelo novo). Ex.: A-36, A-572 GR.50.");
            montando = false;
            MostrarSel();
        }

        static string Descricao(string k)
        {
            switch (k)
            {
                case "pratt": return "Diagonais descendo para o meio (tracionadas sob gravidade), montantes comprimidos.";
                case "howe": return "Diagonais subindo para o meio (comprimidas), montantes tracionados.";
                case "warren": return "Triângulos isósceles alternados, sem montantes.";
                case "warren_v": return "Diagonais alternadas com montantes em todos os nós.";
                case "k": return "Duas diagonais por painel encontrando o meio do montante.";
                case "fink": return "Tesoura triangular em W (4 painéis no banzo superior).";
                case "serie": return "Tesouras repetidas ao longo do prédio, com terças rotuladas no banzo superior.";
                case "cob": return "X no plano da cobertura entre duas tesouras selecionadas, dividido em painéis.";
                case "tercas": return "Terças sobre as tesouras (dividem as tesouras nos apoios) e tirantes entre terças.";
                default: return "Entre dois pórticos (2 pilares selecionados) ou num vão (4 nós). Diagonais só axiais; pontos no meio de barras dividem a barra.";
            }
        }

        void BotaoSel(string t)
        {
            var b = Botao(t, "sel", () => UsarSelecao(true));
            b.Margin = new Thickness(132, 6, 0, 0); b.HorizontalAlignment = HorizontalAlignment.Left;
            colParam.Children.Add(b);
        }

        string Texto(string k) => campos.TryGetValue(k, out var c) && c is TextBox t ? t.Text.Trim() : null;
        int Indice(string k) => campos.TryGetValue(k, out var c) && c is ComboBox cb ? Math.Max(0, cb.SelectedIndex) : 0;
        string Banzo() => Banzos[Math.Min(3, int.TryParse(V("banzo", Familia == "serie" ? "2" : "0"), NumberStyles.Integer, Ci, out int i) ? i : 0)];

        void MostrarSel()
        {
            var (nos, barras) = w.SelecaoTrelicas();
            lblDestino.Text = w.EspTrelicas != null ? "Inserir em: " + w.EspTrelicas.Nome + (nos.Count + barras.Count > 0 ? " · seleção: " + Pl.N(nos.Count, "nó", "nós") + ", " + Pl.N(barras.Count, "barra", "barras") : "")
                                                    : "Sem modelo espacial aberto: Inserir cria um modelo novo";
            lblSel.Text = Familia == "contra" ? "Pilares de dois pórticos vizinhos: o painel é formado pelas pontas. Para a cobertura, selecione duas tesouras." : Familia == "tercas" ? "Selecione as tesouras de uma água." : "";
            lblSel.Margin = new Thickness(0, 6, 0, 0);
        }

        void UsarSelecao(bool avisar)
        {
            var (nos, barras) = w.SelecaoTrelicas();
            if (!(campos.GetValueOrDefault("alvo") is TextBox t)) return;
            if (Familia == "trelica")
            {
                if (nos.Count == 1 && w.EspTrelicas?.Json["nos"]?[nos[0]] is JsonNode n)
                {
                    ((TextBox)campos["origem"]).Text = F((double)n["x"]) + "; " + F((double)n["y"]) + "; " + F((double)n["z"]);
                    t.Text = "";
                }
                else if (nos.Count == 2) t.Text = string.Join(" ", nos);
                else if (avisar) lblStatus.Text = "Selecione 1 nó (origem) ou 2 nós (topos dos pilares) na vista 3D.";
                return;
            }
            var ids = barras.Count >= 2 ? barras : nos.Count == 4 && Familia == "contra" ? nos : null;
            if (ids == null) { if (avisar) lblStatus.Text = Familia == "tercas" ? "Selecione 2 ou mais tesouras na vista 3D." : "Selecione 2 barras ou 4 nós na vista 3D."; return; }
            t.Text = string.Join(" ", ids);
        }

        static string F(double v) => v.ToString("0.###", CultureInfo.GetCultureInfo("pt-BR"));
        static double D(string s) => double.Parse(s.Trim().Replace(',', '.'), NumberStyles.Float, Ci);
        static string[] Ids(string s) => (s ?? "").Split(new[] { ' ', ';', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries);

        // "Tubo 60,3x3,6" (mm) → tubo circular; "Ø 12,5" (mm) → barra redonda; "Ret 25x50" (cm) → retângulo; o resto = catálogo.
        internal static JsonNode SecaoDe(string s)
        {
            s = (s ?? "").Trim();
            string[] P(string r) => r.Split(new[] { 'x', 'X', '×' }, StringSplitOptions.RemoveEmptyEntries);
            if (s.StartsWith("Tubo", StringComparison.OrdinalIgnoreCase)) { var p = P(s.Substring(4)); return new JsonObject { ["forma"] = "tubo_circular", ["d"] = D(p[0]) / 1000, ["t"] = D(p[1]) / 1000 }; }
            if (s.StartsWith("Ø")) return new JsonObject { ["forma"] = "circular", ["d"] = D(s.Substring(1)) / 1000 };
            if (s.StartsWith("Ret", StringComparison.OrdinalIgnoreCase)) { var p = P(s.Substring(3)); return new JsonObject { ["forma"] = "retangular", ["b"] = D(p[0]) / 100, ["h"] = D(p[1]) / 100 }; }
            return s;
        }

        // Dados do gerador a partir dos campos; erro = mensagem para o rodapé.
        internal JsonObject Montar(out string erro)
        {
            erro = null;
            try
            {
                var it = It;
                var d = new JsonObject();
                string mat = Texto("material");
                if (!string.IsNullOrEmpty(mat)) d["material"] = mat;
                else if (w.EspTrelicas == null || !(w.EspTrelicas.Json["materiais"] is JsonObject mo) || mo.Count == 0) d["material"] = "A-572 GR.50";
                if (Familia == "trelica" || Familia == "serie")
                {
                    d["tipo"] = Familia == "serie" ? "trelicas_serie" : "trelica_plana";
                    d["modelo"] = it.Modelo;
                    string bz = it.Modelo == "fink" ? "duas_aguas" : Banzos[Indice("banzo")];
                    d["banzo"] = bz;
                    var alvo = Ids(Texto("alvo"));
                    if (alvo.Length == 2) d["entre"] = new JsonArray(alvo.Select(x => (JsonNode)x).ToArray());
                    else if (alvo.Length != 0) { erro = "Entre os nós: dois ids (ou vazio)."; return null; }
                    else d["vao"] = D(Texto("vao"));
                    d["altura"] = it.Modelo == "fink" ? 0.0 : D(Texto("altura"));
                    if (bz != "paralelo") d["altura2"] = D(Texto("altura2"));
                    if (it.Modelo != "fink") d["paineis"] = (int)D(Texto("paineis"));
                    var sec = new JsonObject { ["banzo"] = SecaoDe(Texto("s_banzo")), ["diagonal"] = SecaoDe(Texto("s_diag")) };
                    if (Texto("s_mont") != "") sec["montante"] = SecaoDe(Texto("s_mont"));
                    if (Familia == "serie")
                    {
                        d["n"] = (int)D(Texto("n")); d["espacamento"] = D(Texto("espacamento"));
                        if (Texto("s_terca") != "") sec["terca"] = SecaoDe(Texto("s_terca"));
                    }
                    d["secoes"] = sec;
                    d["rotulas"] = Rotulas[Indice("rotulas")];
                    var ap = Apoios[Indice("apoios")];
                    if (alvo.Length == 0 || Indice("apoios") != 0) d["apoios"] = new JsonArray(ap[0], ap[1]);
                    if (alvo.Length == 0)
                    {
                        var o = Texto("origem").Split(';').Select(D).ToArray();
                        if (o.Length != 3) { erro = "Origem: x; y; z."; return null; }
                        d["origem"] = new JsonArray(o[0], o[1], o[2]);
                        d["eixo"] = Indice("eixo") == 0 ? "x" : "y";
                    }
                    return d;
                }
                var ids = Ids(Texto("alvo"));
                if (Familia == "tercas")
                {
                    d["tipo"] = "tercas";
                    if (ids.Length < 2) { erro = "Selecione 2 ou mais tesouras (ou digite os ids)."; return null; }
                    d["barras"] = new JsonArray(ids.Select(x => (JsonNode)x).ToArray());
                    d["espacamento"] = D(Texto("espacamento"));
                    d["tirantes"] = (int)D(Texto("tirantes"));
                    var sec = new JsonObject { ["terca"] = SecaoDe(Texto("s_terca")) };
                    if ((int)d["tirantes"] > 0) sec["tirante"] = SecaoDe(Texto("s_tirante"));
                    d["secoes"] = sec;
                    return d;
                }
                d["tipo"] = "contraventamento";
                d["modelo"] = it.Modelo;
                if (ids.Length == 2) d["barras"] = new JsonArray(ids.Select(x => (JsonNode)x).ToArray());
                else if (ids.Length == 4) d["painel"] = new JsonArray(ids.Select(x => (JsonNode)x).ToArray());
                else { erro = "Selecione 2 barras (pilares de dois pórticos ou duas tesouras) ou 4 nós do painel."; return null; }
                if (Texto("divisoes") is string dv && dv != "") d["divisoes"] = (int)D(dv);
                if (Texto("distancia") is string ds && ds != "") d["distancia"] = D(ds);
                var sc = new JsonObject { ["contraventamento"] = SecaoDe(Texto("s_contra")) };
                if (Texto("s_escora") is string se && se != "") sc["escora"] = SecaoDe(se);
                d["secoes"] = sc;
                return d;
            }
            catch (FormatException) { erro = "Confira os números (vírgula ou ponto decimal)."; }
            catch (IndexOutOfRangeException) { erro = "Seção: \"Tubo 60,3x3,6\", \"Ø 12,5\" (mm) ou \"Ret 25x50\" (cm)."; }
            catch (OverflowException) { erro = "Número fora do intervalo."; }
            return null;
        }

        // ---------------- prévia ----------------

        void AgendarPrevia() { if (montando) return; tmPrevia.Stop(); tmPrevia.Start(); }

        // Pede a prévia ao motor e desenha (público para as capturas).
        public async Task Atualizar()
        {
            tmPrevia.Stop();
            int meu = ++pedido;
            var d = Montar(out string erro);
            if (d == null) { previa = null; lblStatus.Text = "⚠ " + erro; lblStatus.Foreground = TemaPlant.BAlerta; btInserir.IsEnabled = false; Desenhar(); return; }
            var doc = w.EspTrelicas;
            if (doc == null && Familia != "trelica" && Familia != "serie")
            {
                previa = null; lblStatus.Text = "Contraventamento e terças precisam de um modelo espacial aberto (selecione os pórticos ou as tesouras)."; lblStatus.Foreground = TemaPlant.BTexto2;
                btInserir.IsEnabled = false; Desenhar(); return;
            }
            try
            {
                var p = doc != null ? await doc.Previa(d) : await DocumentoEspacial.PreviaVazia(d);
                if (meu != pedido) return;
                previa = p;
                int nn = (int)p["nos_criados"], nb = (int)p["barras_criadas"], nr = p["removidas"]?.AsArray().Count ?? 0;
                string av = string.Join(" · ", (p["avisos"]?.AsArray() ?? new JsonArray()).Select(a => (string)a["mensagem"]).Take(2));
                lblStatus.Text = Pl.N(nn, "nó novo", "nós novos") + " · " + Pl.N(nb, "barra nova", "barras novas") + (nr > 0 ? " · " + Pl.N(nr, "barra dividida", "barras divididas") : "") + (av != "" ? " — " + av : "");
                lblStatus.Foreground = TemaPlant.BTexto2;
                btInserir.IsEnabled = nb > 0;
            }
            catch (Exception ex)
            {
                if (meu != pedido) return;
                previa = null; lblStatus.Text = "⚠ " + (ex is MotorErro ? ex.Message : "Motor: " + ex.Message); lblStatus.Foreground = TemaPlant.BAlerta; btInserir.IsEnabled = false;
            }
            Desenhar();
        }

        bool Iso => iso ?? !(Familia == "trelica" && w.EspTrelicas == null);

        void Desenhar()
        {
            tela.Children.Clear();
            hostSeg.Child = Segmentado(new (string, bool, Action, string)[]
            {
                ("Elevação", !Iso, () => { iso = false; Desenhar(); }, "Vista no plano da treliça (ao longo do maior lado em planta)."),
                ("Isométrica", Iso, () => { iso = true; Desenhar(); }, "Modelo inteiro em isométrica, peças novas em destaque."),
            });
            if (previa == null || tela.ActualWidth < 10) { lblInfo.Text = ""; return; }
            DesenharEm(tela, previa, w.EspTrelicas?.Json, Iso, 2.2, 1.6);
            var ns = previa["nos"].AsObject().Select(kv => kv.Value.AsArray().Select(x => (double)x).ToArray()).ToList();
            if (ns.Count > 0)
            {
                double dx = ns.Max(p => p[0]) - ns.Min(p => p[0]), dy = ns.Max(p => p[1]) - ns.Min(p => p[1]), dz = ns.Max(p => p[2]) - ns.Min(p => p[2]);
                lblInfo.Text = "Peças novas: " + F(dx) + " × " + F(dy) + " × " + F(dz) + " m (X × Y × Z)" + (w.EspTrelicas != null ? " · em cinza, o modelo atual" : "");
            }
        }

        // Desenho das barras da prévia (acento: banzos/peças contínuas; realce tracejado: só axiais) sobre o modelo (cinza).
        static void DesenharEm(Canvas cv, JsonNode p, JsonObject modelo, bool iso, double espCont, double espAx)
        {
            cv.Children.Clear();
            double W = double.IsNaN(cv.Width) ? cv.ActualWidth : cv.Width, H = double.IsNaN(cv.Height) ? cv.ActualHeight : cv.Height;
            if (W < 10 || H < 10) return;
            var novos = p["nos"].AsObject().ToDictionary(kv => kv.Key, kv => kv.Value.AsArray().Select(x => (double)x).ToArray());
            var removidas = new HashSet<string>((p["removidas"]?.AsArray() ?? new JsonArray()).Select(x => (string)x));
            var ctx = new List<(double[] A, double[] B)>();
            if (modelo != null)
            {
                var nm = modelo["nos"].AsObject();
                foreach (var kv in modelo["barras"].AsObject())
                {
                    if (removidas.Contains(kv.Key)) continue;
                    var a = nm[(string)kv.Value["ni"]]; var b = nm[(string)kv.Value["nj"]];
                    if (a == null || b == null) continue;
                    ctx.Add((new[] { (double)a["x"], (double)a["y"], (double)a["z"] }, new[] { (double)b["x"], (double)b["y"], (double)b["z"] }));
                }
            }
            var bs = p["barras"].AsArray().Select(b => (A: novos[(string)b["ni"]], B: novos[(string)b["nj"]], Ax: (bool)b["trelica"])).ToList();
            var todos = novos.Values.Concat(ctx.SelectMany(c => new[] { c.A, c.B })).ToList();
            if (novos.Count == 0 || todos.Count == 0) return;
            bool emX = novos.Values.Max(q => q[0]) - novos.Values.Min(q => q[0]) >= novos.Values.Max(q => q[1]) - novos.Values.Min(q => q[1]);
            const double c30 = 0.8660254, s30 = 0.5;
            // isométrica vista de (+x, −y, +z): X desce para a direita, Y sobe para a direita
            Point Proj(double[] q) => iso ? new Point((q[0] + q[1]) * c30, q[2] + (q[1] - q[0]) * s30)
                                          : new Point(emX ? q[0] : q[1], q[2]);
            var pts = todos.Select(Proj).ToList();
            double x0 = pts.Min(q => q.X), x1 = pts.Max(q => q.X), y0 = pts.Min(q => q.Y), y1 = pts.Max(q => q.Y);
            double m = Math.Min(W, H) * 0.08 + 4, esc = Math.Min((W - 2 * m) / Math.Max(1e-6, x1 - x0), (H - 2 * m) / Math.Max(1e-6, y1 - y0));
            double ox = (W - (x1 - x0) * esc) / 2, oy = (H - (y1 - y0) * esc) / 2;
            Point T(double[] q) { var r = Proj(q); return new Point(ox + (r.X - x0) * esc, H - oy - (r.Y - y0) * esc); }
            foreach (var c in ctx) Linha(cv, T(c.A), T(c.B), TemaPlant.BTexto3, 1);
            foreach (var b in bs.OrderBy(b => !b.Ax)) Linha(cv, T(b.A), T(b.B), b.Ax ? TemaPlant.BRealce : TemaPlant.BAcento, b.Ax ? espAx : espCont, b.Ax && espAx > 1);
            if (espCont > 2)
                foreach (var q in novos.Values)
                {
                    var t = T(q);
                    var e = new Ellipse { Width = 4, Height = 4, Fill = TemaPlant.BTexto1 };
                    Canvas.SetLeft(e, t.X - 2); Canvas.SetTop(e, t.Y - 2);
                    cv.Children.Add(e);
                }
        }

        // ---------------- inserir ----------------

        public async Task<bool> Inserir()
        {
            var d = Montar(out string erro);
            if (d == null) { lblStatus.Text = "⚠ " + erro; return false; }
            btInserir.IsEnabled = false;
            bool ok = await w.InserirTrelica(d, It.Nome);
            if (ok)
            {
                if (Familia == "contra" || Familia == "tercas") ((TextBox)campos["alvo"]).Text = "";
                MostrarSel();
                await Atualizar();
                lblStatus.Text = "Inserido: " + It.Nome + ". Ctrl+Z na janela principal desfaz. " + lblStatus.Text;
            }
            else { btInserir.IsEnabled = true; lblStatus.Text = "⚠ " + w.StatusMsg.Text; }
            return ok;
        }
    }

    public static partial class Capturas
    {
        // 50) Treliças e contraventamentos (JanelaTrelicas.cs, docs/v3/TRELICAS.md): galeria, Pratt inserida num modelo novo,
        // X entre os pilares de dois pórticos de um galpão (um Ctrl+Z desfaz), cobertura de tesouras em série calculada (Realista).
        // Só elas: ESTRUCTTO_CAPTURAS=trelicas.
        static void CapturasTrelicas(MainWindow w)
        {
            void Falha(string s) => throw new InvalidOperationException("Treliças: " + s + " | " + w.StatusMsg.Text);
            w.FecharEspaciaisSemPerguntar();
            EsperarIdle();

            // galeria sem modelo espacial: Pratt
            var j = new JanelaTrelicas(w);
            PrepararDialogoForaDaTela(j);
            j.Show();
            EsperarIdle();
            Esperar(j.Atualizar());
            System.Threading.Thread.Sleep(300); EsperarIdle();   // miniaturas (prévias do motor) terminam
            if (j.UltimaPrevia == null || (int)j.UltimaPrevia["barras_criadas"] != 25) Falha("prévia da Pratt de 6 painéis deveria ter 25 barras");
            CapturarDialogo(j, "50-trelicas-galeria.png");
            j.Escolher("k");
            Esperar(j.Atualizar());
            CapturarDialogo(j, "50-trelicas-galeria-k.png");
            j.Escolher("pratt");
            Esperar(j.Atualizar());
            if (!Esperar2(j.Inserir())) Falha("Inserir Pratt");
            j.Close();
            var d = w.EspacialAtivo ?? throw new InvalidOperationException("Treliças: modelo novo não abriu");
            if (d.Json["barras"].AsObject().Count != 25) Falha("Pratt inserida: " + d.Json["barras"].AsObject().Count + " barras");
            w.vista3D.MostrarVista(VistaPadrao.Frente, false);
            CapturarPrincipal(w, "50-trelicas-pratt", () => w.vista3D.MostrarVista(VistaPadrao.Frente, false));
            w.FecharEspaciaisSemPerguntar();
            EsperarIdle();

            // galpão gerado + X entre os pilares P1-0-0 e P1-0-1 (seleção da Vista3D)
            var gal = new JsonObject
            {
                ["tipo"] = "galpao", ["material"] = "A-572 GR.50", ["apoio"] = "engaste",
                ["secoes"] = new JsonObject { ["pilar"] = "W360x51", ["viga"] = "HP250x62", ["terca"] = new JsonObject { ["perfil"] = "Ue 250x85x25x3,00" } },
                ["vao"] = 20.0, ["altura_pilar"] = 6.0, ["altura_cumeeira"] = 8.0, ["n_modulos"] = 4, ["espacamento"] = 6.0,
            };
            Esperar(w.AbrirEspacial(() => DocumentoEspacial.Gerar(gal, "Galpão 20 m", 1.5, 1.5), "gerando galpão"));
            d = w.EspacialAtivo ?? throw new InvalidOperationException("Treliças: galpão não abriu");
            int nb0 = d.Json["barras"].AsObject().Count;
            w.vista3D.Selecionar(new string[0], new[] { "P1-0-0", "P1-0-1" });
            EsperarIdle();
            j = w.AbrirTrelicas("cx");
            PrepararDialogoForaDaTela(j);
            EsperarIdle();
            Esperar(j.Atualizar());
            if (j.UltimaPrevia == null || (int)j.UltimaPrevia["barras_criadas"] != 2) Falha("prévia do X deveria ter 2 barras: " + j.Status);
            CapturarDialogo(j, "50-trelicas-contravento.png");
            if (!Esperar2(j.Inserir())) Falha("Inserir X");
            j.Close();
            if (d.Json["barras"].AsObject().Count != nb0 + 2) Falha("X: barras " + d.Json["barras"].AsObject().Count + " (esperado " + (nb0 + 2) + ")");
            w.vista3D.LimparSelecao();
            CapturarPrincipal(w, "50-trelicas-contravento-inserido", () => w.vista3D.MostrarVista(VistaPadrao.Iso, false));
            Esperar(w.DesfazerEspacial());
            if (w.EspacialAtivo.Json["barras"].AsObject().Count != nb0) Falha("Ctrl+Z não tirou o X inteiro");
            Esperar(w.RefazerTrelicas());
            if (w.EspacialAtivo.Json["barras"].AsObject().Count != nb0 + 2) Falha("Ctrl+Y não refez o X");
            w.FecharEspaciaisSemPerguntar();
            EsperarIdle();

            // cobertura: 5 tesouras Howe em série com terças + X nos vãos extremos, cargas nas terças, calculada, Realista
            var serie = new JsonObject
            {
                ["tipo"] = "trelicas_serie", ["material"] = "A-572 GR.50", ["modelo"] = "howe", ["banzo"] = "duas_aguas",
                ["vao"] = 15.0, ["altura"] = 0.0, ["altura2"] = 2.5, ["paineis"] = 6, ["n"] = 5, ["espacamento"] = 6.0,
                ["secoes"] = new JsonObject { ["banzo"] = "W150x18", ["diagonal"] = "U 75x40x3,00", ["terca"] = "W150x18" },
            };
            Esperar(w.AbrirEspacial(() => DocumentoEspacial.NovoComGerador(serie, "Cobertura em tesouras"), "gerando cobertura"));
            d = w.EspacialAtivo ?? throw new InvalidOperationException("Treliças: cobertura não abriu");
            var itens = new JsonArray();
            foreach (var kv in d.Json["barras"].AsObject().Where(kv => kv.Key.Contains("-T")))
                foreach (var (caso, q) in new[] { ("G1", 0.3), ("Q1", 0.75) })
                    itens.Add(new JsonObject { ["tipo"] = "carga_distribuida", ["dados"] = new JsonObject { ["caso"] = caso, ["barra"] = kv.Key, ["q"] = new JsonArray(0.0, 0.0, -q) } });
            if (itens.Count == 0) Falha("terças da série não vieram");
            Esperar(w.OperarEspacial(dd => dd.Executar(() => Motor.Post("/api/modelo/lote", new JsonObject { ["itens"] = itens })), "cargas nas terças"));
            Esperar(w.CalcularEspacial());
            if (!w.EspacialAtivo.Calculado) Falha("cálculo da cobertura");
            bool antes = Config.Realista3D;
            w.DefinirRealista(true);
            w.MostrarDiagrama(Diagrama.Verificacao, true);
            EsperarIdle();
            CapturarPrincipal(w, "50-trelicas-cobertura-realista", () => w.vista3D.MostrarVista(VistaPadrao.Iso, false));
            w.MostrarDiagrama(Diagrama.Normal);
            EsperarIdle();
            CapturarPrincipal(w, "50-trelicas-cobertura-normal", () => w.vista3D.MostrarVista(VistaPadrao.Iso, false));
            w.DefinirRealista(antes);
            System.IO.File.AppendAllText(System.IO.Path.Combine(pastaSaida, "50-trelicas.txt"),
                "ok: Pratt 25 barras; X entre pórticos +2 barras, Ctrl+Z/Ctrl+Y; cobertura " + w.EspacialAtivo.Json["barras"].AsObject().Count + " barras calculada: " + w.EspacialAtivo.Resumo() + Environment.NewLine);
            w.FecharEspaciaisSemPerguntar();
            EsperarIdle();
        }

        static bool Esperar2(Task<bool> t) { Esperar(t); return t.Result; }
    }
}
