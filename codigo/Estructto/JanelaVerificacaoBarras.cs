// Janela "Verificação de barras" (REVISAO-TELAS 3.4): toda a estrutura numa lista ordenável (clique no cabeçalho),
// filtros Só reprovadas / tipo (aço, formado a frio, concreto) e busca; barra de aproveitamento por linha.
// Duplo clique (ou "Verificar barra") abre o detalhe da barra (AbrirDetalhe). Dados: GET /api/verificacao.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using static NovoFtool.UiPlaca;
using static NovoFtool.UiAnalise;

namespace NovoFtool
{
    public class JanelaVerificacaoBarras : JanelaEscura
    {
        public Action<string> AbrirDetalhe;   // id da barra
        readonly List<JsonNode> todas;
        readonly Border hostFiltros = new Border(), hostTabela = new Border();
        readonly TextBlock lblConta = new TextBlock();
        bool soReprovadas; string tipo = "", busca = "";
        int colOrd = 6; bool desc = true;
        string selId;
        List<JsonNode> vis = new List<JsonNode>();

        static double Taxa(JsonNode n) { double t = D(n, "taxa"); return double.IsNaN(t) ? -1 : t; }
        static bool Reprovada(JsonNode n) { return !Bool(n, "ok") && S(n, "criterio") != "nao_verificada"; }

        public JanelaVerificacaoBarras(JsonNode d)
        {
            todas = Lista(d).ToList();
            Moldar(this, "Verificação de barras", 1240, 720);
            int nRep = todas.Count(Reprovada), nNao = todas.Count(n => S(n, "criterio") == "nao_verificada");
            var pior = todas.OrderByDescending(Taxa).FirstOrDefault();
            Border cab;
            if (todas.Count == 0) cab = Cabecalho(Nivel.Neutro, "–", "Sem verificação", "Calcule (F5) para verificar as barras pelas normas.");
            else
            {
                var nv = nRep > 0 ? Nivel.Erro : Taxa(pior) > 0.9 ? Nivel.Alerta : Nivel.Ok;
                string tit = nRep > 0 ? nRep + (nRep == 1 ? " barra não passa" : " barras não passam") + " de " + todas.Count
                                      : "Todas as " + todas.Count + " barras passam · usa " + Pct(Taxa(pior)) + " na mais carregada";
                string sub = "Mais carregada: " + S(pior, "barra") + " (" + S(pior, "secao") + ") — " + NomeCriterio(S(pior, "criterio")) + ", " + S(pior, "norma") + " " + S(pior, "item") +
                             (nNao > 0 ? " · " + nNao + " não verificadas" : "");
                var chips = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                foreach (var (rot, nvc, qt) in new[] { ("✓ passam", Nivel.Ok, todas.Count(n => Bool(n, "ok"))), (nRep == 1 ? "✕ reprovada" : "✕ reprovadas", Nivel.Erro, nRep), ("– não verificadas", Nivel.Neutro, nNao) })
                {
                    if (qt == 0 && nvc != Nivel.Ok) continue;
                    var c = Chip(qt + "  " + rot, nvc, null, null, 12); c.Margin = new Thickness(6, 0, 0, 0); c.Padding = new Thickness(10, 4, 10, 4); chips.Children.Add(c);
                }
                cab = Cabecalho(nv, nv == Nivel.Erro ? "✕" : nv == Nivel.Alerta ? "!" : "✓", tit, sub, chips);
            }

            var corpo = new DockPanel { Margin = new Thickness(12, 10, 12, 10) };
            var barraF = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var bus = CaixaBusca("Buscar barra ou seção", 220, t => { busca = t.Trim(); Montar(); }, out _);
            DockPanel.SetDock(bus, Dock.Right); barraF.Children.Add(bus);
            lblConta.FontSize = 11.5; lblConta.Foreground = TemaPlant.BTexto3; lblConta.VerticalAlignment = VerticalAlignment.Center; lblConta.Margin = new Thickness(0, 0, 12, 0);
            DockPanel.SetDock(lblConta, Dock.Right); barraF.Children.Add(lblConta);
            barraF.Children.Add(hostFiltros);
            DockPanel.SetDock(barraF, Dock.Top); corpo.Children.Add(barraF);
            corpo.Children.Add(hostTabela);

            var dica = Tx("Duplo clique numa barra abre a verificação detalhada, critério a critério.", 11.5, TemaPlant.BTexto3);
            Content = Raiz(cab, corpo, Rodape(dica,
                B("Fechar", Close, false, "Fecha a janela (Esc)."),
                B("Verificar barra", () => { if (selId != null) AbrirDetalhe?.Invoke(selId); }, true, "Abre a verificação detalhada da barra selecionada.")));
            selId = S(pior, "barra");
            Montar();
        }

        // API das capturas
        public void Filtrar(bool reprovadas, string tipoBarra) { soReprovadas = reprovadas; tipo = tipoBarra ?? ""; Montar(); }
        public void Ordenar(int coluna, bool decrescente) { colOrd = coluna; desc = decrescente; Montar(); }

        void Montar()
        {
            var tiposPres = todas.Select(n => S(n, "tipo")).Distinct().OrderBy(x => x).ToList();
            var f1 = Segmentado(new List<(string, bool, Action, string)>
            {
                ("Todas", !soReprovadas, () => { soReprovadas = false; Montar(); }, "Mostra todas as barras."),
                ("Só reprovadas (" + todas.Count(Reprovada) + ")", soReprovadas, () => { soReprovadas = true; Montar(); }, "Mostra só as barras com taxa acima de 100%.")
            });
            var itensT = new List<(string, bool, Action, string)> { ("Todos os tipos", tipo == "", () => { tipo = ""; Montar(); }, null) };
            foreach (var t in tiposPres)
            {
                string tt = t;
                itensT.Add((NomeTipo(t) + " (" + todas.Count(n => S(n, "tipo") == tt) + ")", tipo == t, () => { tipo = tt; Montar(); }, "Só barras de " + NomeTipo(t).ToLowerInvariant() + "."));
            }
            var f2 = Segmentado(itensT); f2.Margin = new Thickness(10, 0, 0, 0);
            hostFiltros.Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { f1, f2 } };

            IEnumerable<JsonNode> q = todas;
            if (soReprovadas) q = q.Where(Reprovada);
            if (tipo != "") q = q.Where(n => S(n, "tipo") == tipo);
            if (busca != "") q = q.Where(n => S(n, "barra").IndexOf(busca, StringComparison.OrdinalIgnoreCase) >= 0 || S(n, "secao").IndexOf(busca, StringComparison.OrdinalIgnoreCase) >= 0);
            Func<JsonNode, IComparable> chave = colOrd switch
            {
                0 => n => S(n, "barra"), 1 => n => NomeTipo(S(n, "tipo")), 2 => n => S(n, "secao"), 3 => n => D(n, "L_m"),
                4 => n => NomeCriterio(S(n, "criterio")), 5 => n => S(n, "combinacao"), _ => n => Taxa(n)
            };
            vis = (desc ? q.OrderByDescending(chave) : q.OrderBy(chave)).ToList();
            lblConta.Text = vis.Count + " de " + todas.Count + " barras";

            if (vis.Count == 0)
            {
                hostTabela.Child = soReprovadas && busca == ""
                    ? Vazio("ok", "Nenhuma barra reprovada", "Todas as barras" + (tipo != "" ? " de " + NomeTipo(tipo).ToLowerInvariant() : "") + " passam nas verificações da norma.")
                    : Vazio("busca", "Nenhuma barra encontrada", "Mude a busca ou os filtros.");
                return;
            }
            var cols = new[]
            {
                new Col("Barra", 96), new Col("Tipo", 112), new Col("Seção", 0), new Col("L [m]", 64, true),
                new Col("Critério", 170), new Col("Combinação determinante", 0), new Col("Aproveitamento", 160, false, "Taxa da verificação mais desfavorável (solicitante / resistente).")
            };
            int sel = vis.FindIndex(n => S(n, "barra") == selId);
            // linhas criadas só quando visíveis (GradeVirtual); clique só repinta as visíveis, não remonta a lista
            GradeVirtual gv = null;
            gv = new GradeVirtual(cols, vis.Count, Linha, sel,
                i => { selId = S(vis[i], "barra"); gv.Selecionar(i); },
                i => { selId = S(vis[i], "barra"); gv.Selecionar(i); AbrirDetalhe?.Invoke(selId); },
                c => { if (colOrd == c) desc = !desc; else { colOrd = c; desc = c == 3 || c == 6; } Montar(); }, colOrd, desc,
                i => string.Join("\n", Lista(vis[i]["avisos"]).Select(a => "⚠ " + a)) is string av && av != "" ? av : null);
            hostTabela.Child = gv;
        }

        UIElement[] Linha(int i)
        {
            var n = vis[i];
            bool nao = S(n, "criterio") == "nao_verificada";
            var crit = new StackPanel { Orientation = Orientation.Horizontal };
            crit.Children.Add(Tx(NomeCriterio(S(n, "criterio")), 12, TemaPlant.BTexto1));
            if (!nao) crit.Children.Add(Tx("  " + S(n, "item"), 11, TemaPlant.BTexto3));
            return new UIElement[]
            {
                Tx(S(n, "barra"), 12, TemaPlant.BTexto1, FontWeights.SemiBold), Tx(NomeTipo(S(n, "tipo")), 12, TemaPlant.BTexto2),
                Tx(S(n, "secao"), 12, TemaPlant.BTexto1), Num1(F(D(n, "L_m"), 2)), crit, Tx(S(n, "combinacao"), 12, TemaPlant.BTexto2),
                nao ? (UIElement)Tx("não verificada", 12, TemaPlant.BTexto3) : CelTaxa(D(n, "taxa"), Bool(n, "ok"), 94)
            };
        }
    }

    // Mesma tabela de UiAnalise.Grade (cabeçalho fixo, linhas de 28 px alternadas, hover, seleção em Accent.Subtle),
    // mas virtualizada: ItemsControl + VirtualizingStackPanel em modo Recycling; cada linha é montada por celulas(i)
    // só quando entra na área visível. Usada pela verificação de barras e pela análise modal.
    public class GradeVirtual : Border
    {
        readonly Col[] cols;
        readonly Func<int, UIElement[]> celulas;
        readonly Action<int> clique, duplo;
        readonly Func<int, string> dicaLinha;
        readonly HashSet<ContentControl> vivas = new HashSet<ContentControl>();
        readonly ListaV lista;
        int sel;

        class ListaV : ItemsControl
        {
            public GradeVirtual Dono;
            protected override DependencyObject GetContainerForItemOverride() => new ContentControl { Focusable = false };
            protected override bool IsItemItsOwnContainerOverride(object item) => false;
            protected override void PrepareContainerForItemOverride(DependencyObject el, object item)
            {
                var c = (ContentControl)el; c.Tag = item; c.Content = Dono.MontarLinha((int)item); Dono.vivas.Add(c);
            }
            protected override void ClearContainerForItemOverride(DependencyObject el, object item)
            {
                var c = (ContentControl)el; c.Content = null; Dono.vivas.Remove(c);
            }
        }

        public GradeVirtual(Col[] cols, int n, Func<int, UIElement[]> celulas, int sel = -1, Action<int> clique = null, Action<int> duplo = null,
            Action<int> ordenar = null, int colOrd = -1, bool desc = false, Func<int, string> dicaLinha = null)
        {
            this.cols = cols; this.celulas = celulas; this.sel = sel; this.clique = clique; this.duplo = duplo; this.dicaLinha = dicaLinha;
            var cab = NovaLinha();
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

            var sv = new FrameworkElementFactory(typeof(ScrollViewer));
            sv.SetValue(ScrollViewer.CanContentScrollProperty, true);
            sv.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
            sv.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
            sv.SetValue(FocusableProperty, false);
            sv.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
            lista = new ListaV
            {
                Dono = this, Template = new ControlTemplate(typeof(ItemsControl)) { VisualTree = sv },
                ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel))),
                ItemsSource = Enumerable.Range(0, n).ToList()
            };
            VirtualizingPanel.SetIsVirtualizing(lista, true);
            VirtualizingPanel.SetVirtualizationMode(lista, VirtualizationMode.Recycling);
            VirtualizingPanel.SetScrollUnit(lista, ScrollUnit.Pixel);
            lista.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((s, e) =>
            {
                if (e.OriginalSource is ScrollViewer v) cabB.Padding = new Thickness(0, 0, Math.Max(0, v.ActualWidth - v.ViewportWidth), 0);
            }));
            // linha selecionada fora da área visível (ex.: depois de reordenar): rola até ela
            lista.Loaded += (s, e) =>
            {
                if (this.sel < 0 || lista.ItemContainerGenerator.ContainerFromIndex(this.sel) != null) return;
                var v = Sv(lista); v?.ScrollToVerticalOffset(Math.Max(0, this.sel * 28 - v.ViewportHeight / 2));
            };

            var dp = new DockPanel { Background = TemaPlant.BSup0 };
            DockPanel.SetDock(cabB, Dock.Top); dp.Children.Add(cabB);
            dp.Children.Add(lista);
            BorderBrush = TemaPlant.BBordaSutil; BorderThickness = new Thickness(1); CornerRadius = new CornerRadius(4); ClipToBounds = true; Child = dp;
        }

        static ScrollViewer Sv(DependencyObject d)
        {
            if (d is ScrollViewer s) return s;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) { var r = Sv(VisualTreeHelper.GetChild(d, i)); if (r != null) return r; }
            return null;
        }

        // Muda a linha selecionada repintando só as linhas visíveis.
        public void Selecionar(int i)
        {
            sel = i;
            foreach (var c in vivas) c.Content = MontarLinha((int)c.Tag);
        }

        Grid NovaLinha()
        {
            var g = new Grid();
            foreach (var c in cols) g.ColumnDefinitions.Add(new ColumnDefinition { Width = c.Larg > 0 ? new GridLength(c.Larg) : new GridLength(1, GridUnitType.Star) });
            return g;
        }

        Border MontarLinha(int r)
        {
            var linha = celulas(r);
            var g = NovaLinha();
            for (int i = 0; i < cols.Length && i < linha.Length; i++)
            {
                var el = linha[i];
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
            bool s = r == sel;
            Brush fundo = s ? TemaPlant.BAcentoSutil : r % 2 == 0 ? TemaPlant.BSup0 : TemaPlant.BSupAlt;
            var lb = new Border
            {
                Height = 28, Background = fundo, BorderBrush = s ? TemaPlant.BAcento : TemaPlant.BBordaSutil,
                BorderThickness = s ? new Thickness(2, 0, 0, 1) : new Thickness(0, 0, 0, 1), Child = g
            };
            if (clique != null || duplo != null)
            {
                lb.Cursor = Cursors.Hand;
                lb.MouseEnter += (o, e) => { if (r != sel) lb.Background = TemaPlant.BSup3; };
                lb.MouseLeave += (o, e) => { if (r != sel) lb.Background = fundo; };
                lb.MouseLeftButtonDown += (o, e) =>
                {
                    e.Handled = true;
                    if (e.ClickCount == 2 && duplo != null) duplo(r);
                    else clique?.Invoke(r);
                };
            }
            if (dicaLinha != null) { var d = dicaLinha(r); if (d != null) Dica(lb, d); }
            return lb;
        }
    }
}
