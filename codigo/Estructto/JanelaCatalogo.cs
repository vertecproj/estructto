// Janela "Catálogo de perfis" (REVISAO-TELAS 3.4): busca, filtro por família (W, HP, U, Ue...), lista ordenável e,
// à direita, o desenho da seção e as propriedades do perfil escolhido. "Usar este perfil" dispara PerfilEscolhido.
// Dados: GET /api/catalogo (lista) e GET /api/catalogo/{nome} (propriedades, carregadas ao escolher).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using static NovoFtool.UiPlaca;
using static NovoFtool.UiAnalise;

namespace NovoFtool
{
    public class JanelaCatalogo : JanelaEscura
    {
        public event Action<string> PerfilEscolhido;
        readonly List<JsonNode> todos;
        readonly Func<string, Task<JsonNode>> buscarPerfil;
        readonly Dictionary<string, JsonNode> cache = new Dictionary<string, JsonNode>();
        readonly Border hostFiltro = new Border(), hostTabela = new Border(), hostPerfil = new Border();
        readonly TextBlock lblConta = new TextBlock();
        string familia = "", busca = "", sel;
        int colOrd = 2; bool desc;
        List<JsonNode> vis = new List<JsonNode>();

        readonly JsonNode arvore;
        readonly TreeView tv = new TreeView { Width = 250, Margin = new Thickness(0, 0, 10, 0), Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        HashSet<string> noPerfis; // perfis do nó escolhido na árvore (null = todos)

        // arvore: GET /api/catalogo?formato=arvore (nivel1 > familias > series > perfis); null = só o filtro por família.
        public JanelaCatalogo(JsonNode lista, Func<string, Task<JsonNode>> buscarPerfil, JsonNode arvore = null)
        {
            this.arvore = arvore;
            todos = Lista(lista).ToList();
            this.buscarPerfil = buscarPerfil;
            Moldar(this, "Catálogo de perfis", 1180, 700);
            var fams = todos.Select(n => S(n, "familia")).Distinct().ToList();
            var cab = Cabecalho(Nivel.Info, "≡", "Catálogo de perfis · " + todos.Count + " perfis", fams.Count + " famílias: " + string.Join(", ", fams) + ". Escolha um perfil para ver o desenho e as propriedades.");

            var corpo = new Grid { Margin = new Thickness(12, 10, 12, 10) };
            corpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            corpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(400) });
            var esq = new DockPanel { Margin = new Thickness(0, 0, 10, 0) };
            var barraF = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var bus = CaixaBusca("Buscar perfil (ex.: W250)", 220, t => { busca = t.Trim(); Montar(); }, out _);
            DockPanel.SetDock(bus, Dock.Right); barraF.Children.Add(bus);
            lblConta.FontSize = 11.5; lblConta.Foreground = TemaPlant.BTexto3; lblConta.VerticalAlignment = VerticalAlignment.Center; lblConta.Margin = new Thickness(0, 0, 12, 0);
            DockPanel.SetDock(lblConta, Dock.Right); barraF.Children.Add(lblConta);
            barraF.Children.Add(hostFiltro);
            DockPanel.SetDock(barraF, Dock.Top); esq.Children.Add(barraF);
            if (arvore != null) { hostFiltro.Visibility = Visibility.Collapsed; MontarArvore(); DockPanel.SetDock(tv, Dock.Left); esq.Children.Insert(0, tv); }
            esq.Children.Add(hostTabela);
            corpo.Children.Add(esq);
            Grid.SetColumn(hostPerfil, 1); corpo.Children.Add(hostPerfil);

            Content = Raiz(cab, corpo, Rodape(Tx("Propriedades geométricas calculadas pelo motor a partir das dimensões do catálogo.", 11.5, TemaPlant.BTexto3),
                B("Fechar", Close, false, "Fecha a janela (Esc)."),
                B("Usar este perfil", () => { if (sel != null) { PerfilEscolhido?.Invoke(sel); Close(); } }, true, "Aplica o perfil escolhido (à seleção, se houver).")));
            Montar();
        }

        // API das capturas
        public Task Escolher(string nome) { sel = nome; Montar(); return Carregar(nome); }
        public void Filtrar(string fam, string texto) { familia = fam ?? ""; busca = texto ?? ""; Montar(); }

        // Árvore nível 1 > família > série; escolher um nó filtra a tabela pelos perfis abaixo dele.
        void MontarArvore()
        {
            TreeViewItem No(string txt, HashSet<string> ps, bool aberto) => new TreeViewItem { Header = Tx(txt + " (" + ps.Count + ")", 12, TemaPlant.BTexto1), Tag = ps, IsExpanded = aberto, Foreground = TemaPlant.BTexto1 };
            IEnumerable<string> Pf(JsonNode n) => Lista(n).Select(x => x.ToString());
            var raiz = new TreeViewItem { Header = Tx("Todos os perfis (" + todos.Count + ")", 12, TemaPlant.BTexto1, FontWeights.SemiBold), Tag = null, IsExpanded = true, Foreground = TemaPlant.BTexto1 };
            foreach (var n1 in Lista(arvore))
            {
                var fams = Lista(n1["familias"]).ToList();
                var a = No(S(n1, "nivel1"), new HashSet<string>(fams.SelectMany(f => Lista(f["series"]).SelectMany(se => Pf(se["perfis"])))), true);
                foreach (var f in fams)
                {
                    var ss = Lista(f["series"]).ToList();
                    var b = No(S(f, "nome"), new HashSet<string>(ss.SelectMany(se => Pf(se["perfis"]))), false);
                    foreach (var se in ss) b.Items.Add(No(S(se, "nome"), new HashSet<string>(Pf(se["perfis"])), false));
                    a.Items.Add(b);
                }
                raiz.Items.Add(a);
            }
            tv.Items.Add(raiz);
            tv.SelectedItemChanged += (o, e) => { noPerfis = (tv.SelectedItem as TreeViewItem)?.Tag as HashSet<string>; Montar(); };
        }

        // API das capturas: seleciona o nó pelo caminho de títulos (sem a contagem), ex.: EscolherNo("Gerdau (ABNT)", "W/HP", "W")
        public void EscolherNo(params string[] caminho)
        {
            ItemsControl c = tv.Items[0] as TreeViewItem;
            foreach (var t in caminho)
            {
                var it = c.Items.OfType<TreeViewItem>().First(x => ((TextBlock)x.Header).Text.StartsWith(t + " ("));
                (c as TreeViewItem).IsExpanded = true; c = it;
            }
            ((TreeViewItem)c).IsSelected = true;
        }

        async Task Carregar(string nome)
        {
            if (nome == null || cache.ContainsKey(nome) || buscarPerfil == null) { MontarPerfil(); return; }
            MontarPerfil();
            try { cache[nome] = await buscarPerfil(nome); }
            catch (Exception ex) { cache[nome] = new JsonObject { ["erro"] = ComandosAnalise.MensagemMotor(ex) }; }
            if (nome == sel) MontarPerfil();
        }

        void Montar()
        {
            var itens = new List<(string, bool, Action, string)> { ("Todas", familia == "", () => { familia = ""; Montar(); }, null) };
            foreach (var f in todos.Select(n => S(n, "familia")).Distinct())
            {
                string ff = f;
                itens.Add((f + " (" + todos.Count(n => S(n, "familia") == ff) + ")", familia == f, () => { familia = ff; Montar(); }, "Só perfis da família " + f + "."));
            }
            hostFiltro.Child = Segmentado(itens);

            IEnumerable<JsonNode> q = todos;
            if (familia != "") q = q.Where(n => S(n, "familia") == familia);
            if (noPerfis != null) q = q.Where(n => noPerfis.Contains(S(n, "nome")));
            if (busca != "")
            {
                string b = busca.Replace(" ", "").Replace(",", ".");
                q = q.Where(n => S(n, "nome").Replace(" ", "").IndexOf(b, StringComparison.OrdinalIgnoreCase) >= 0);
            }
            Func<JsonNode, IComparable> chave = colOrd switch
            {
                0 => n => S(n, "nome"), 1 => n => S(n, "familia"), 3 => n => D(n, "d_mm"), 4 => n => D(n, "bf_mm"), 5 => n => D(n, "A_cm2"), _ => n => D(n, "massa_kg_m")
            };
            vis = (desc ? q.OrderByDescending(chave) : q.OrderBy(chave)).ToList();
            lblConta.Text = vis.Count + " de " + todos.Count;
            if (vis.Count == 0) { hostTabela.Child = Vazio("busca", "Nenhum perfil encontrado", "Mude a busca ou o nó da árvore."); }
            else
            {
                var cols = new[] { new Col("Perfil", 0), new Col("Família", 70), new Col("Massa [kg/m]", 104, true), new Col("d [mm]", 76, true), new Col("bf [mm]", 76, true), new Col("A [cm²]", 80, true) };
                var linhas = vis.Select(n => new UIElement[]
                {
                    Tx(Nome(S(n, "nome")), 12, TemaPlant.BTexto1, FontWeights.SemiBold), Tx(S(n, "familia"), 12, TemaPlant.BTexto2),
                    Num1(F(D(n, "massa_kg_m"), 1)), Num1(F(D(n, "d_mm"), 0)), Num1(F(D(n, "bf_mm"), 0)), Num1(F(D(n, "A_cm2"), 2))
                }).ToList();
                TrocarTabela(hostTabela, Grade(cols, linhas, vis.FindIndex(n => S(n, "nome") == sel),
                    i => { sel = S(vis[i], "nome"); Montar(); _ = Carregar(sel); },
                    i => { sel = S(vis[i], "nome"); PerfilEscolhido?.Invoke(sel); Close(); },
                    c => { if (colOrd == c) desc = !desc; else { colOrd = c; desc = false; } Montar(); }, colOrd, desc));
            }
            MontarPerfil();
        }

        // "W250x32.7" → "W 250 × 32,7"
        static string Nome(string n)
        {
            int i = 0; while (i < n.Length && char.IsLetter(n[i])) i++;
            return (i > 0 && i < n.Length ? n.Substring(0, i) + " " + n.Substring(i).TrimStart() : n).Replace("x", " × ").Replace(".", ",");
        }

        void MontarPerfil()
        {
            if (sel == null) { hostPerfil.Child = Cartao("Perfil", Vazio("tabela", "Nenhum perfil escolhido", "Clique num perfil da lista para ver o desenho e as propriedades."), null, new Thickness(0)); return; }
            cache.TryGetValue(sel, out var p);
            var resumo = todos.FirstOrDefault(n => S(n, "nome") == sel);
            var sp = new StackPanel();
            var topo = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var fam = Chip("família " + S(resumo, "familia"), Nivel.Info); DockPanel.SetDock(fam, Dock.Right); topo.Children.Add(fam);
            topo.Children.Add(Tx(Nome(sel), 18, TemaPlant.BTexto1, FontWeights.Bold));
            sp.Children.Add(topo);
            if (p == null) { sp.Children.Add(Vazio("info", "Carregando propriedades…", "")); hostPerfil.Child = Cartao("Perfil", sp, null, new Thickness(0)); return; }
            if (p["erro"] != null) { sp.Children.Add(Vazio("erro", "Propriedades indisponíveis", S(p, "erro"))); hostPerfil.Child = Cartao("Perfil", sp, null, new Thickness(0)); return; }
            var props = p["secao"]?["props"];
            var des = new Border { Height = 170, Background = TemaPlant.BBgCanvas, CornerRadius = new CornerRadius(4), BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 10), Child = Desenho(p, props) };
            sp.Children.Add(des);
            string mm(string k) => F(D(p, k), 1, "mm");
            var dims = new List<(string, string, string)> { ("d", mm("d_mm"), "Altura total"), ("bf", mm("bf_mm"), "Largura da mesa") };
            if (!double.IsNaN(D(p, "tw_mm"))) { dims.Add(("tw", mm("tw_mm"), "Espessura da alma")); dims.Add(("tf", mm("tf_mm"), "Espessura da mesa")); }
            if (!double.IsNaN(D(p, "t_mm"))) dims.Add(("t", mm("t_mm"), "Espessura da chapa"));
            dims.Add(("Massa", F(D(p, "massa_kg_m"), 1, "kg/m"), null));
            sp.Children.Add(TituloSecao("Dimensões", null, 0));
            sp.Children.Add(Ficha(dims, 50));
            sp.Children.Add(TituloSecao("Propriedades", null, 10));
            sp.Children.Add(Ficha(new List<(string, string, string)>
            {
                ("A", F(D(p, "A_cm2"), 2, "cm²"), "Área da seção"), ("J", F(D(p, "J_cm4"), 2, "cm⁴"), "Constante de torção"),
                ("Ix", F(D(p, "Ix_cm4"), 0, "cm⁴"), "Momento de inércia (eixo forte)"), ("Iy", F(D(p, "Iy_cm4"), 1, "cm⁴"), "Momento de inércia (eixo fraco)"),
                ("Wx", F(D(p, "Wx_cm3"), 1, "cm³"), "Módulo elástico"), ("Wy", F(D(p, "Wy_cm3"), 1, "cm³"), null),
                ("Zx", F(D(p, "Zx_cm3"), 1, "cm³"), "Módulo plástico"), ("Zy", F(D(p, "Zy_cm3"), 1, "cm³"), null),
                ("rx", F(D(p, "rx_cm"), 2, "cm"), "Raio de giração"), ("ry", F(D(p, "ry_cm"), 2, "cm"), null),
                ("Cw", F(D(p, "Cw_cm6"), 0, "cm⁶"), "Constante de empenamento"), ("b/t · h/tw", F(D(props, "bt_mesa"), 1) + " / " + F(D(props, "ht_alma"), 1), "Esbeltez b/t da mesa e h/t da alma")
            }, 50));
            hostPerfil.Child = Cartao("Perfil", new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = sp }, null, new Thickness(0));
        }

        // Desenho da seção (I ou C/U), em escala, centrado, com cotas d e bf.
        static FrameworkElement Desenho(JsonNode p, JsonNode props)
        {
            double h = D(p, "d_mm"), b = D(p, "bf_mm"), tw = D(p, "tw_mm"), tf = D(p, "tf_mm"), t = D(p, "t_mm");
            string fam = S(p, "familia");
            if (double.IsNaN(h) || double.IsNaN(b)) return new Border();
            if (double.IsNaN(tw)) tw = double.IsNaN(t) ? h / 40 : t;
            if (double.IsNaN(tf)) tf = double.IsNaN(t) ? h / 30 : t;
            double esc = Math.Min(130 / h, 220 / b);
            double W = b * esc, H = h * esc, Tw = Math.Max(1.5, tw * esc), Tf = Math.Max(1.5, tf * esc);
            var pts = new List<Point>();
            if (fam == "U" || fam == "Ue")
            {
                // enrijecedor do Ue: 3º número do nome (Ue 127x50x17x2,65 → 17 mm)
                var partes = S(p, "nome").Split('x');
                double lab = fam == "Ue" ? (partes.Length >= 4 && Portico.Num.Ler(partes[2], out double dl) ? dl * esc : Math.Min(H * 0.25, W * 0.35)) : 0;
                pts.AddRange(new[] { new Point(0, 0), new Point(W, 0) });
                if (lab > 0) pts.AddRange(new[] { new Point(W, lab), new Point(W - Tf, lab), new Point(W - Tf, Tf) }); else pts.Add(new Point(W, Tf));
                pts.AddRange(new[] { new Point(Tw, Tf), new Point(Tw, H - Tf) });
                if (lab > 0) pts.AddRange(new[] { new Point(W - Tf, H - Tf), new Point(W - Tf, H - lab), new Point(W, H - lab), new Point(W, H) }); else pts.AddRange(new[] { new Point(W, H - Tf), new Point(W, H) });
                pts.Add(new Point(0, H));
            }
            else
            {
                double xa = (W - Tw) / 2;
                pts.AddRange(new[] { new Point(0, 0), new Point(W, 0), new Point(W, Tf), new Point(xa + Tw, Tf), new Point(xa + Tw, H - Tf), new Point(W, H - Tf), new Point(W, H),
                    new Point(0, H), new Point(0, H - Tf), new Point(xa, H - Tf), new Point(xa, Tf), new Point(0, Tf) });
            }
            var geo = new StreamGeometry();
            using (var c = geo.Open()) { c.BeginFigure(pts[0], true, true); c.PolyLineTo(pts.Skip(1).ToList(), true, true); }
            geo.Freeze();
            var cv = new Canvas { Width = W + 70, Height = H + 34 };
            double x0 = 46, y0 = 8;
            var forma = new Path { Data = geo, Fill = TemaPlant.BAcentoSutil, Stroke = TemaPlant.BRealce, StrokeThickness = 1.5, StrokeLineJoin = PenLineJoin.Miter };
            Canvas.SetLeft(forma, x0); Canvas.SetTop(forma, y0); cv.Children.Add(forma);
            // cotas
            cv.Children.Add(new Line { X1 = x0 - 12, Y1 = y0, X2 = x0 - 12, Y2 = y0 + H, Stroke = TemaPlant.BTexto2, StrokeThickness = 1 });
            var td = Tx("d = " + F(h, 0), 11, TemaPlant.BTexto2); td.RenderTransform = new RotateTransform(-90);
            Canvas.SetLeft(td, x0 - 30); Canvas.SetTop(td, y0 + H / 2 + 22); cv.Children.Add(td);
            cv.Children.Add(new Line { X1 = x0, Y1 = y0 + H + 10, X2 = x0 + W, Y2 = y0 + H + 10, Stroke = TemaPlant.BTexto2, StrokeThickness = 1 });
            var tb = Tx("bf = " + F(b, 0) + " mm", 11, TemaPlant.BTexto2);
            Canvas.SetLeft(tb, x0 + W / 2 - 30); Canvas.SetTop(tb, y0 + H + 12); cv.Children.Add(tb);
            return new Viewbox { Stretch = Stretch.None, Child = cv, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        }
    }
}
