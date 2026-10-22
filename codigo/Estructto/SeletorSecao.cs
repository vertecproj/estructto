// B2 v0.6: seletor de seção com busca + árvore (nível 1 > família > série > perfil), "No modelo" no topo e recentes.
// Usado no painel Propriedades (barra) e no botão Seção da faixa. A árvore vem de GET /api/catalogo?formato=arvore, a mesma da JanelaCatalogo.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using static NovoFtool.UiPlaca;
using static NovoFtool.UiAnalise;

namespace NovoFtool
{
    // Montagem da árvore do catálogo, compartilhada com a JanelaCatalogo.
    public static class ArvoreCatalogo
    {
        static IEnumerable<string> Pf(JsonNode n) => Lista(n).Select(x => x.ToString());

        // Nós: Tag = HashSet de nomes de perfil abaixo (filtro da JanelaCatalogo). Com aoPerfil, as séries ganham os perfis
        // como folhas (criadas ao expandir) e aoPerfil é chamado ao escolher uma folha.
        public static TreeViewItem Montar(JsonNode arvore, int total, Action<string> aoPerfil = null)
        {
            TreeViewItem No(string txt, HashSet<string> ps, bool aberto) => new TreeViewItem { Header = Tx(txt + " (" + ps.Count + ")", 12, TemaPlant.BTexto1), Tag = ps, IsExpanded = aberto, Foreground = TemaPlant.BTexto1 };
            var raiz = new TreeViewItem { Header = Tx("Todos os perfis (" + total + ")", 12, TemaPlant.BTexto1, FontWeights.SemiBold), Tag = null, IsExpanded = true, Foreground = TemaPlant.BTexto1 };
            foreach (var n1 in Lista(arvore))
            {
                var fams = Lista(n1["familias"]).ToList();
                var a = No(S(n1, "nivel1"), new HashSet<string>(fams.SelectMany(f => Lista(f["series"]).SelectMany(se => Pf(se["perfis"])))), aoPerfil == null);
                foreach (var f in fams)
                {
                    var ss = Lista(f["series"]).ToList();
                    var b = No(S(f, "nome"), new HashSet<string>(ss.SelectMany(se => Pf(se["perfis"]))), false);
                    foreach (var se in ss)
                    {
                        var c = No(S(se, "nome"), new HashSet<string>(Pf(se["perfis"])), false);
                        if (aoPerfil != null) PerfisPreguicosos(c, Pf(se["perfis"]).ToList(), aoPerfil);
                        b.Items.Add(c);
                    }
                    a.Items.Add(b);
                }
                raiz.Items.Add(a);
            }
            return raiz;
        }

        // Folhas só ao expandir (o catálogo tem ~2200 perfis).
        static void PerfisPreguicosos(TreeViewItem serie, List<string> perfis, Action<string> aoPerfil)
        {
            serie.Items.Add(new TreeViewItem());   // marcador: faz aparecer a seta de expandir
            serie.Expanded += (s, e) =>
            {
                if (e.OriginalSource != serie) return;
                if (serie.Items.Count == 1 && serie.Items[0] is TreeViewItem m && m.Header == null)
                {
                    serie.Items.Clear();
                    foreach (var p in perfis)
                    {
                        string nome = p;
                        var it = new TreeViewItem { Header = Tx(p, 12, TemaPlant.BTexto1), Foreground = TemaPlant.BTexto1, Tag = nome, Cursor = Cursors.Hand };
                        it.Selected += (o, ev) => { ev.Handled = true; aoPerfil(nome); };
                        serie.Items.Add(it);
                    }
                }
            };
        }
    }

    public class SeletorSecao : Border
    {
        static JsonNode lista, arvore;
        static Task carga;
        const string ChaveRecentes = "secoes_recentes";

        readonly Func<IEnumerable<string>> noModelo;
        readonly Action<string> escolher;
        string atual;
        readonly StackPanel corpo = new StackPanel();
        readonly TextBox busca;
        string termo = "";

        public static List<string> Recentes() => Config.Opcoes.Get(ChaveRecentes).Split('|', StringSplitOptions.RemoveEmptyEntries).ToList();
        static void Lembrar(string nome)
        {
            var r = Recentes(); r.Remove(nome); r.Insert(0, nome);
            Config.Opcoes.Set(ChaveRecentes, string.Join("|", r.Take(8)));
        }

        // Catálogo do motor, uma vez por sessão (as capturas injetam com Injetar).
        public static Task Carregar() => carga ??= Task.Run(async () =>
        {
            try { lista = await Motor.Get("/api/catalogo"); } catch { carga = null; return; }
            try { arvore = await Motor.Get("/api/catalogo?formato=arvore"); } catch { }
        });
        public static void Injetar(JsonNode l, JsonNode a) { lista = l; arvore = a; carga = Task.CompletedTask; }

        public SeletorSecao(Func<IEnumerable<string>> noModelo, string atual, Action<string> escolher)
        {
            this.noModelo = noModelo; this.atual = atual; this.escolher = escolher;
            Width = 330; Height = 420; Background = TemaPlant.BSup1; BorderBrush = TemaPlant.BBorda; BorderThickness = new Thickness(1); CornerRadius = new CornerRadius(6);
            var dp = new DockPanel { Margin = new Thickness(8) };
            var cx = CaixaBusca("Buscar perfil (ex.: W250, Tubo 50)", 300, t => { termo = t.Trim(); Montar(); }, out busca);
            DockPanel.SetDock(cx, Dock.Top); cx.Margin = new Thickness(0, 0, 0, 6); dp.Children.Add(cx);
            dp.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = corpo });
            Child = dp;
            Loaded += (s, e) => busca.Focus();
            Montar();
            if (lista == null) _ = Carregar().ContinueWith(_ => Dispatcher.BeginInvoke(new Action(Montar)));
        }

        void Escolheu(string nome) { Lembrar(nome); atual = nome; escolher(nome); }

        FrameworkElement Item(string nome)
        {
            bool at = nome == atual;
            var b = new Border { Padding = new Thickness(8, 3, 8, 3), CornerRadius = new CornerRadius(3), Cursor = Cursors.Hand, Background = at ? TemaPlant.BAcentoSutil : Brushes.Transparent, ToolTip = nome,
                Child = Tx(nome, 12, TemaPlant.BTexto1, at ? FontWeights.SemiBold : (FontWeight?)null) };
            b.MouseEnter += (s, e) => { if (!at) b.Background = TemaPlant.BSup3; };
            b.MouseLeave += (s, e) => { if (!at) b.Background = Brushes.Transparent; };
            b.MouseLeftButtonUp += (s, e) => Escolheu(nome);
            return b;
        }

        void Titulo(string t)
        {
            var tb = Tx(t, 10.5, TemaPlant.BTexto3, FontWeights.SemiBold); tb.Margin = new Thickness(4, 8, 0, 3);
            corpo.Children.Add(tb);
        }

        public void Montar()
        {
            corpo.Children.Clear();
            var modelo = noModelo().ToList();
            var catalogo = Lista(lista).Select(n => S(n, "nome")).ToList();
            if (termo != "")
            {
                string b = termo.Replace(" ", "").Replace(",", ".");
                bool Casa(string n) => n.Replace(" ", "").Replace(",", ".").IndexOf(b, StringComparison.OrdinalIgnoreCase) >= 0;
                var r = modelo.Where(Casa).Concat(catalogo.Where(Casa).Where(n => !modelo.Contains(n))).ToList();
                Titulo(r.Count + " resultado" + (r.Count == 1 ? "" : "s") + (r.Count > 80 ? " (mostrando 80)" : ""));
                foreach (var n in r.Take(80)) corpo.Children.Add(Item(n));
                if (r.Count == 0) corpo.Children.Add(Tx(lista == null ? "Carregando o catálogo…" : "Nenhum perfil encontrado.", 12, TemaPlant.BTexto3, null, true));
                return;
            }
            Titulo("NO MODELO");
            foreach (var n in modelo) corpo.Children.Add(Item(n));
            if (modelo.Count == 0) corpo.Children.Add(Tx("Nenhuma seção ainda.", 11.5, TemaPlant.BTexto3));
            var rec = Recentes().Where(n => !modelo.Contains(n)).ToList();
            if (rec.Count > 0) { Titulo("RECENTES"); foreach (var n in rec) corpo.Children.Add(Item(n)); }
            Titulo("CATÁLOGO");
            if (arvore == null) { corpo.Children.Add(Tx(lista == null ? "Carregando o catálogo…" : "Árvore indisponível: use a busca.", 11.5, TemaPlant.BTexto3)); return; }
            var tv = new TreeView { Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
            tv.Items.Add(ArvoreCatalogo.Montar(arvore, catalogo.Count, Escolheu));
            corpo.Children.Add(tv);
        }
    }

    // Botão que mostra a seção atual e abre o SeletorSecao num popup (substitui o ComboBox plano).
    public class BotaoSecao : Button
    {
        readonly Popup pop = new Popup { StaysOpen = false, AllowsTransparency = true, Placement = PlacementMode.Bottom };
        public readonly SeletorSecao Seletor;
        public BotaoSecao(Func<IEnumerable<string>> noModelo, string atual, Action<string> aoEscolher)
        {
            Content = Tx(atual ?? "(escolher)", 12, TemaPlant.BTexto1); HorizontalContentAlignment = HorizontalAlignment.Left; Padding = new Thickness(8, 4, 8, 4);
            ToolTip = "Escolha o perfil: busca, seções do modelo, recentes e a árvore do catálogo.";
            pop.PlacementTarget = this;
            Seletor = new SeletorSecao(noModelo, atual, n => { pop.IsOpen = false; Content = Tx(n, 12, TemaPlant.BTexto1); aoEscolher(n); });
            pop.Child = Seletor;
            Click += (s, e) => { Seletor.Montar(); pop.IsOpen = true; };
        }
        public void Abrir() { Seletor.Montar(); pop.IsOpen = true; }
    }
}
