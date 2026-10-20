// Janelas auxiliares (tema escuro): pergunta simples, verificacao da barra com comparacao de perfis,
// placa de base, assistentes de estruturas tipicas e ajuda.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Portico;

namespace NovoFtool
{
    public class JanelaEscura : Window
    {
        // paleta dos dialogos = tokens do TemaPlant (mesmos nomes de antes)
        public static readonly Brush BFundo = TemaPlant.BSup1, BPainel = TemaPlant.BSup0, BTexto = TemaPlant.BTexto1,
            BFraco = TemaPlant.BTexto2, BBorda = TemaPlant.BBorda, BOk = TemaPlant.BSucessoFundo, BRuim = TemaPlant.BErroFundo,
            BCinza = TemaPlant.BSup3, BVerde = TemaPlant.BSucesso, BAmarelo = TemaPlant.BAlerta, BVermelho = TemaPlant.BErro, BAzul = TemaPlant.BAcento;

        public static SolidColorBrush Pincel(byte r, byte g, byte b) { var x = new SolidColorBrush(Color.FromRgb(r, g, b)); x.Freeze(); return x; }   // legado: nao usar para UI; use TemaPlant.B*

        public JanelaEscura()
        {
            Background = BFundo; Foreground = BTexto; FontFamily = TemaPlant.Fonte; FontSize = 12;
            SourceInitialized += (s, e) => TituloEscuro(this);
            WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
            KeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); };
            Loaded += (s, e) => { if (!(this is JanelaRelatorio)) Anim.Abrir(this); };   // relatorio: documento, sem movimento
        }

        public static void TituloEscuro(Window w) { TemaPlant.TituloEscuro(w); }

        public static TextBlock T(string s, double tam = 12, Brush cor = null, FontWeight? peso = null)
        {
            return new TextBlock { Text = s, FontSize = tam, Foreground = cor ?? BTexto, FontWeight = peso ?? FontWeights.Normal, TextWrapping = TextWrapping.Wrap };
        }

        public static Button B(string s, Action a, bool principal = false, string dica = null)
        {
            var b = new Button
            {
                Content = s, Padding = new Thickness(16, 4, 16, 4), Margin = new Thickness(6, 0, 0, 0), Foreground = principal ? TemaPlant.BTextoSobreAcento : TemaPlant.BTexto1, MinWidth = 80,
                Background = principal ? BAzul : TemaPlant.BSup3, BorderBrush = principal ? BAzul : BBorda, Cursor = Cursors.Hand, IsDefault = principal,
                ToolTip = dica
            };
            b.Click += (s2, e) => a();
            return b;
        }

        public static TextBox Campo(string valor, double larg = 90, string dica = null)
        {
            return new TextBox { Text = valor, Width = larg, Background = TemaPlant.BSup3, Foreground = BTexto, BorderBrush = BBorda, CaretBrush = BTexto, Padding = new Thickness(3, 2, 3, 2), ToolTip = dica };
        }

        // medidor de aproveitamento: verde ate 90%, amarelo ate 100%, vermelho acima
        public static UIElement Medidor(string titulo, double ap, string detalhe, bool aplicavel = true)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 12), ToolTip = titulo + ": " + (aplicavel ? (ap * 100).ToString("0") + "% — " : "") + detalhe };
            var topo = new DockPanel();
            var pct = T(aplicavel ? (ap * 100).ToString("0") + "%" : "—", 12, aplicavel ? (ap > 1 ? BVermelho : ap > 0.9 ? BAmarelo : BVerde) : BFraco, FontWeights.SemiBold);
            DockPanel.SetDock(pct, Dock.Right);
            topo.Children.Add(pct);
            topo.Children.Add(T(titulo, 12, BTexto, FontWeights.SemiBold));
            sp.Children.Add(topo);
            var trilho = new Grid { Height = 8, Margin = new Thickness(0, 4, 0, 4) };
            trilho.Children.Add(new Border { Background = BPainel, CornerRadius = new CornerRadius(4) });
            if (aplicavel)
            {
                double f = Math.Max(0, Math.Min(1, ap));
                trilho.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(f, GridUnitType.Star) });
                trilho.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - f, GridUnitType.Star) });
                var barra = new Border { Background = ap > 1 ? BVermelho : ap > 0.9 ? BAmarelo : BVerde, CornerRadius = new CornerRadius(4) };
                Grid.SetColumnSpan(trilho.Children[0], 2);
                trilho.Children.Add(barra);
            }
            sp.Children.Add(trilho);
            sp.Children.Add(T(detalhe, 11, BFraco));
            return sp;
        }

        public static DataGrid Tabela()
        {
            var dg = new DataGrid
            {
                AutoGenerateColumns = false, IsReadOnly = true, HeadersVisibility = DataGridHeadersVisibility.Column, SelectionMode = DataGridSelectionMode.Single,
                Background = BPainel, RowBackground = BPainel, AlternatingRowBackground = TemaPlant.BSupAlt, Foreground = BTexto, BorderThickness = new Thickness(0),
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, HorizontalGridLinesBrush = TemaPlant.BBordaSutil, CanUserAddRows = false
            };
            var h = new Style(typeof(DataGridColumnHeader));
            h.Setters.Add(new Setter(Control.BackgroundProperty, TemaPlant.BSup2));
            h.Setters.Add(new Setter(Control.ForegroundProperty, BFraco));
            h.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 4, 6, 4)));
            dg.ColumnHeaderStyle = h;
            return dg;
        }

        public static DataGridTextColumn Col(string titulo, string prop, double larg = double.NaN)
        {
            var c = new DataGridTextColumn { Header = titulo, Binding = new System.Windows.Data.Binding(prop) };
            if (!double.IsNaN(larg)) c.Width = larg;
            return c;
        }
    }

    // ---------- pergunta simples ----------

    public class Pergunta : JanelaEscura
    {
        TextBox campo; string resposta;

        public static string Mostrar(Window dono, string titulo, string texto, string padrao)
        {
            var p = new Pergunta { Owner = dono, Title = titulo, Width = 380, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize };
            var sp = new StackPanel { Margin = new Thickness(16) };
            sp.Children.Add(T(texto));
            p.campo = Campo(padrao, 340, texto); p.campo.Margin = new Thickness(0, 8, 0, 12);
            sp.Children.Add(p.campo);
            var bt = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            bt.Children.Add(B("OK", () => { p.resposta = p.campo.Text; p.Close(); }, true, "Confirma o valor digitado (Enter)."));
            bt.Children.Add(B("Cancelar", p.Close, false, "Cancela a operação e fecha a janela (Esc)."));
            sp.Children.Add(bt);
            p.Content = sp;
            p.Loaded += (s, e) => { p.campo.Focus(); p.campo.SelectAll(); };
            p.ShowDialog();
            return p.resposta;
        }
    }

    // Caixa de mensagem no visual do programa (no lugar do MessageBox cinza do Windows).
    public class Caixa : JanelaEscura
    {
        MessageBoxResult resp = MessageBoxResult.Cancel;

        // "Salvar alteracoes?": Sim = salvar, Nao = descartar, Cancel = voltar
        public static MessageBoxResult SalvarAlteracoes(Window dono, string nome)
        {
            return Mostrar(dono, "Salvar alterações?", "O modelo \"" + nome + "\" tem alterações que ainda não foram salvas.", "Se não salvar, as alterações feitas desde o último salvamento serão perdidas.",
                new[] { ("Salvar", MessageBoxResult.Yes, true), ("Não salvar", MessageBoxResult.No, false), ("Cancelar", MessageBoxResult.Cancel, false) }, "!", BAmarelo);
        }

        public static void Erro(Window dono, string titulo, string texto)
        {
            Mostrar(dono, titulo, texto, null, new[] { ("OK", MessageBoxResult.OK, true) }, "✕", BVermelho);
        }

        public static Caixa Criar(Window dono, string titulo, string texto, string detalhe, (string nome, MessageBoxResult r, bool principal)[] botoes, string icone, Brush corIcone)
        {
            var w = new Caixa { Owner = dono, Title = "Estructto", Width = 470, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize };
            var raiz = new DockPanel();
            // rodape com os botoes, numa faixa mais escura
            var rod = new Border { Background = BPainel, Padding = new Thickness(16, 12, 16, 12) };
            var bt = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            foreach (var b in botoes)
            {
                var bb = b;
                string dicaBt = bb.r == MessageBoxResult.Yes ? "Confirma e executa a ação proposta." : bb.r == MessageBoxResult.No ? "Prossegue sem salvar as alterações atuais." : bb.r == MessageBoxResult.Cancel ? "Cancela a operação e volta ao modelo (Esc)." : "Fecha esta mensagem (Enter).";
                var x = B(bb.nome, () => { w.resp = bb.r; w.Close(); }, bb.principal, dicaBt);
                x.MinWidth = 96; x.Padding = new Thickness(16, 6, 16, 6); bt.Children.Add(x);
            }
            rod.Child = bt; DockPanel.SetDock(rod, Dock.Bottom); raiz.Children.Add(rod);
            // corpo: icone redondo + titulo grande + texto
            var corpo = new DockPanel { Margin = new Thickness(20, 20, 20, 18) };
            string nomeIc = icone == "!" ? "aviso" : icone == "✕" ? "erro" : icone == "?" ? "info" : icone == "✓" ? "ok" : "info";
            Brush fundoIc = corIcone == BAmarelo ? TemaPlant.BAlertaFundo : corIcone == BVermelho ? TemaPlant.BErroFundo : corIcone == BVerde ? TemaPlant.BSucessoFundo : TemaPlant.BInfoFundo;
            var ic = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(20), Background = fundoIc, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 16, 0),
                Child = new ContentControl { Content = Icones.Criar(nomeIc, 24), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            DockPanel.SetDock(ic, Dock.Left); corpo.Children.Add(ic);
            var txt = new StackPanel();
            txt.Children.Add(T(titulo, 16, BTexto, FontWeights.SemiBold));
            var t1 = T(texto, 12.5, BTexto); t1.TextWrapping = TextWrapping.Wrap; t1.Margin = new Thickness(0, 6, 0, 0); txt.Children.Add(t1);
            if (!string.IsNullOrEmpty(detalhe)) { var t2 = T(detalhe, 11.5, BFraco); t2.TextWrapping = TextWrapping.Wrap; t2.Margin = new Thickness(0, 6, 0, 0); txt.Children.Add(t2); }
            corpo.Children.Add(txt);
            raiz.Children.Add(corpo);
            w.Content = raiz;
            return w;
        }

        public static MessageBoxResult Mostrar(Window dono, string titulo, string texto, string detalhe, (string nome, MessageBoxResult r, bool principal)[] botoes, string icone, Brush corIcone)
        {
            var w = Criar(dono, titulo, texto, detalhe, botoes, icone, corIcone);
            w.ShowDialog();
            return w.resp;
        }
    }

    // ---------- verificacao da barra ----------

    public class JanelaVerificacao : JanelaEscura
    {
        public string PerfilEscolhido;
        readonly Modelo m; readonly Barra b;
        DataGrid dg; ComboBox filtro; Secao atual;

        class Linha
        {
            public Secao Sec; public string Perfil { get; set; } public string Massa { get; set; } public string Ap { get; set; } public double ApNum { get; set; }
            public Brush CorAp { get; set; } = TemaPlant.BTextoMudo; public Visibility BarraVis { get; set; } = Visibility.Collapsed;
            public string Critico { get; set; } public string SitTxt { get; set; } public string Marca { get; set; } = "";
            public Brush SitFg { get; set; } = TemaPlant.BTexto2; public Brush SitBg { get; set; } = TemaPlant.BSup2; public Brush SitBd { get; set; } = TemaPlant.BBordaSutil;
            public bool Passa; public bool Aplicavel; public bool Atual; public bool Leve;
        }

        public JanelaVerificacao(Modelo m, Barra b)
        {
            this.m = m; this.b = b;
            Title = "Verificação da barra " + b.Id; Width = 1200; Height = 640; MinWidth = 760; MinHeight = 480;
            atual = Biblioteca.AcharSecao(b.Secao);

            var raiz = new DockPanel();
            // cabecalho colorido
            cab = new Border { Padding = new Thickness(18, 12, 18, 12) };
            DockPanel.SetDock(cab, Dock.Top);
            raiz.Children.Add(cab);

            // rodape
            var rod = new DockPanel { Margin = new Thickness(14, 8, 14, 12) };
            var bts = new StackPanel { Orientation = Orientation.Horizontal };
            bts.Children.Add(B("Usar o perfil selecionado", Usar, true, "Aplica o perfil selecionado na tabela à barra (e aos demais trechos se for uma barra contínua) e recalcula a estrutura."));
            bts.Children.Add(B("Fechar", Close, false, "Fecha a janela sem trocar o perfil da barra (Esc)."));
            DockPanel.SetDock(bts, Dock.Right);
            rod.Children.Add(bts);
            rod.Children.Add(T("Flecha máx. L/" + Num.Fmt(m.LimiteFlecha, 0) + " (aba Dimensionamento). Verificação simplificada: não substitui a NBR 8800 (sem flambagem lateral nem local).", 11, BFraco));
            DockPanel.SetDock(rod, Dock.Bottom);
            raiz.Children.Add(rod);

            var corpo = new Grid { Margin = new Thickness(18, 14, 18, 0) };
            corpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(310) });
            corpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            corpo.ColumnDefinitions.Add(new ColumnDefinition());
            var colEsq = new DockPanel();
            // coeficiente de seguranca usado em todas as verificacoes (o mesmo da aba Analise)
            var linSeg = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
            linSeg.Children.Add(T("Coeficiente de segurança n =", 13, BTexto, FontWeights.SemiBold));
            var tbSeg = new TextBox { Width = 60, Margin = new Thickness(8, 0, 0, 0), Text = Num.Fmt(m.Seguranca, 2), VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "Coeficiente de segurança n que divide a tensão de escoamento fy e a carga crítica de flambagem Ncr em todas as barras do modelo. Pressione Enter após editar." };
            Action aplicarSeg = () =>
            {
                double n;
                if (!Num.Ler(tbSeg.Text, out n) || n <= 0) { tbSeg.Text = Num.Fmt(m.Seguranca, 2); return; }
                if (n == m.Seguranca) return;
                m.Seguranca = n; MudouSeguranca = true;
                Atualizar(); Preencher();
            };
            tbSeg.LostFocus += (s, e) => aplicarSeg();
            tbSeg.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) aplicarSeg(); };
            linSeg.Children.Add(tbSeg);
            DockPanel.SetDock(linSeg, Dock.Top);
            colEsq.Children.Add(linSeg);
            esq = new StackPanel();
            colEsq.Children.Add(esq);
            Atualizar();
            corpo.Children.Add(colEsq);
            CorpoResto(corpo, raiz);
        }

        Border cab; StackPanel esq;
        public bool MudouSeguranca;

        void Atualizar()
        {
            Verif v = Verificador.Barra(m, b, atual);
            cab.Background = !v.Aplicavel ? BCinza : v.Passa ? BOk : BRuim;
            var cs = new StackPanel();
            cs.Children.Add(T(!v.Aplicavel ? "SEM VERIFICAÇÃO" : v.Passa ? "PASSA" : "NÃO PASSA", 20, TemaPlant.BTexto1, FontWeights.SemiBold));
            cs.Children.Add(T((atual != null ? atual.Descricao : b.Secao) + (b.EixoFraco ? " (deitado)" : "") + " · " + b.Material + " · L = " + Num.Fmt(b.Res.L, 2) + " m · n = " + Num.Fmt(m.Seguranca, 2) +
                (v.Aplicavel ? "   —   aproveitamento " + (v.Ap * 100).ToString("0") + "%, limitado por " + v.Critico : "   —   " + v.Motivo), 12, TemaPlant.BTexto1));
            cab.Child = cs;
            esq.Children.Clear();
            if (v.Aplicavel)
            {
                esq.Children.Add(Medidor("Tensão normal", v.ApTensao, "σ = |N|/A + |M|/W = " + Num.Fmt(v.Sigma, 1) + " MPa  ≤  fy/n = " + Num.Fmt(v.SigmaAdm, 1) + " MPa"));
                esq.Children.Add(Medidor("Corte", v.ApCorte, "τ = Q/As = " + Num.Fmt(v.Tau, 1) + " MPa  ≤  0,577·fy/n = " + Num.Fmt(v.TauAdm, 1) + " MPa"));
                esq.Children.Add(Medidor(v.Balanco ? "Flecha (deslocamento total da ponta livre)" : "Flecha", v.ApFlecha, "f = " + Num.Fmt(v.Flecha * 1000, 2) + " mm  ≤  " + (v.Balanco ? "H = " + Num.Fmt(v.FlechaAdm * m.LimiteFlecha, 2) + " m até o apoio, H/" : "L/") + Num.Fmt(m.LimiteFlecha, 0) + " = " + Num.Fmt(v.FlechaAdm * 1000, 2) + " mm"));
                esq.Children.Add(v.Nc > 1e-6
                    ? Medidor("Flambagem (Euler/Johnson, K = " + Num.Fmt(v.K, 0) + (v.Balanco ? ", balanço" : "") + ")", v.ApFlamb, "Nc = " + Num.Fmt(v.Nc, 1) + " kN · n·Nc ≤ Ncr = " + Num.Fmt(v.Ncr, 1) + " kN · esbeltez " + Num.Fmt(v.Esbeltez, 0))
                    : Medidor("Flambagem", 0, "Barra sem compressão.", false));
                if (v.Aviso != "") { var av = T("⚠ " + v.Aviso, 12, BAmarelo); av.TextWrapping = TextWrapping.Wrap; av.Margin = new Thickness(0, 10, 0, 0); esq.Children.Add(av); }
            }
            else esq.Children.Add(T(v.Motivo, 13, BFraco));
        }

        void CorpoResto(Grid corpo, DockPanel raiz)
        {
            var dir = new DockPanel();
            var topo = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var tituloTab = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            tituloTab.Children.Add(T("Perfis da família", 13, BTexto, FontWeights.SemiBold));
            Resumo = T("", 11, TemaPlant.BTexto3); tituloTab.Children.Add(Resumo);
            topo.Children.Add(tituloTab);
            filtro = new ComboBox { Width = 150, Margin = new Thickness(6, 0, 0, 0), ToolTip = "Filtra os perfis por série (altura/diâmetro nominal) dentro da família escolhida." };
            familia = new ComboBox { Width = 190, ToolTip = "Escolhe a família de perfis do catálogo para comparar o aproveitamento e o peso por metro." };
            montaSeg = () =>
            {
                var novo = UiPlaca.Segmentado(new List<(string, bool, Action, string)>
                {
                    ("Todos", !soPassam, () => { soPassam = false; montaSeg(); Preencher(); }, "Mostra todos os perfis da família."),
                    ("Só os que passam", soPassam, () => { soPassam = true; montaSeg(); Preencher(); }, "Mostra só os perfis que passam (mais o perfil atual).")
                }, 26, 11.5);
                novo.Margin = new Thickness(0, 0, 8, 0); DockPanel.SetDock(novo, Dock.Right);
                if (segHost != null) topo.Children.Remove(segHost);
                segHost = novo; topo.Children.Insert(2, novo);
            };
            DockPanel.SetDock(filtro, Dock.Right); DockPanel.SetDock(familia, Dock.Right);
            topo.Children.Insert(0, filtro);
            topo.Children.Insert(1, familia);
            montaSeg();
            DockPanel.SetDock(topo, Dock.Top);
            dir.Children.Add(topo);
            dg = Tabela();
            dg.ToolTip = "Perfis da família selecionada ordenados pelos que passam (do mais leve ao mais pesado). Dê duplo clique numa linha ou clique em \"Usar o perfil selecionado\" para aplicá-lo à barra.";
            dg.Columns.Add(UiTabela.PerfilMarca("Perfil", 360)); dg.Columns.Add(UiTabela.Texto("kg/m", "Massa", 64, true)); dg.Columns.Add(UiTabela.Capacidade("Aproveitamento", 150));
            dg.Columns.Add(UiTabela.Texto("Limitado por", "Critico", 170)); dg.Columns.Add(UiTabela.Chip("Situação", "SitTxt"));
            dg.MouseDoubleClick += (s, e) => { if (ItemsControl.ContainerFromElement(dg, e.OriginalSource as DependencyObject) is DataGridRow) Usar(); };
            dg.LoadingRow += (s, e) =>
            {
                var l = (Linha)e.Row.Item;
                e.Row.Foreground = l.Passa || !l.Aplicavel ? TemaPlant.BTexto1 : TemaPlant.BTexto3;
                e.Row.FontWeight = l.Atual ? FontWeights.SemiBold : FontWeights.Normal;
            };
            dir.Children.Add(dg);
            Grid.SetColumn(dir, 2);
            corpo.Children.Add(dir);
            raiz.Children.Add(corpo);
            Content = raiz;

            // qualquer familia do catalogo, e dentro dela todas as series ou uma so
            foreach (string f in Biblioteca.Familias) familia.Items.Add(f);
            bool temFam = atual != null && atual.Familia != "" && familia.Items.Contains(atual.Familia);
            if (!temFam && atual != null) familia.Items.Insert(0, "Só a seção atual");
            familia.SelectedIndex = temFam ? familia.Items.IndexOf(atual.Familia) : 0;
            CarregarSeries();
            familia.SelectionChanged += (s, e) => { CarregarSeries(); Preencher(); };
            filtro.SelectionChanged += (s, e) => { if (!carregando) Preencher(); };
            Preencher();
        }

        Action montaSeg; ComboBox familia; bool carregando, soPassam; TextBlock Resumo; Border segHost;

        string FamiliaSel { get { string f = familia.SelectedItem as string; return f != null && Biblioteca.Familias.Contains(f) ? f : null; } }

        void CarregarSeries()
        {
            carregando = true;
            filtro.Items.Clear();
            string fam = FamiliaSel;
            if (fam == null) { filtro.Visibility = Visibility.Collapsed; carregando = false; return; }
            filtro.Visibility = Visibility.Visible;
            filtro.Items.Add("Todas as séries");
            var todos = Biblioteca.DaFamilia(fam, null);
            foreach (string se in todos.Select(s => s.Serie).Where(s => !string.IsNullOrEmpty(s)).Distinct()) filtro.Items.Add(se);
            int i = atual != null && atual.Familia == fam && !string.IsNullOrEmpty(atual.Serie) ? filtro.Items.IndexOf(atual.Serie) : -1;
            filtro.SelectedIndex = 0;   // sempre a familia inteira, como o Dimensionar; a serie e so um filtro opcional
            carregando = false;
        }

        void Preencher()
        {
            var lista = new List<Secao>();
            string fam = FamiliaSel;
            if (fam != null) lista = Biblioteca.DaFamilia(fam, filtro.SelectedIndex > 0 ? (string)filtro.SelectedItem : null);
            else if (atual != null) lista.Add(atual);
            Material mat = Biblioteca.AcharMaterial(b.Material);
            Func<Secao, double> massa = s => s.Massa > 0 ? s.Massa : (mat != null ? mat.Densidade * s.A : 0);
            // organizado por serie (dimensao) e espessura; os que passam sobem para o topo, do mais leve ao mais pesado
            var linhas = lista.OrderBy(s => s.H).ThenBy(s => s.Bf).ThenBy(massa).Select(s =>
            {
                // barra continua: vale o pior trecho com esse perfil
                Verif v = Verificador.Barra(m, b, s);
                if (b.Membro != 0)
                    foreach (Barra t in m.Trechos(b.Membro))
                    {
                        if (t == b) continue;
                        Verif vt = Verificador.Barra(m, t, s);
                        if (vt.Aplicavel && (!v.Aplicavel || vt.Ap > v.Ap)) v = vt;
                    }
                return new Linha
                {
                    Sec = s, Perfil = s.Descricao, Massa = Num.Fmt(massa(s), 1), Passa = v.Passa,
                    Ap = v.Aplicavel ? UiTabela.PctTxt(v.Ap) : "—", ApNum = v.Aplicavel ? Math.Min(v.Ap * 100, 100) : 0, Critico = v.Aplicavel ? v.Critico : "",
                    CorAp = UiPlaca.CorAp(v.Ap, v.Passa), BarraVis = v.Aplicavel ? Visibility.Visible : Visibility.Collapsed, Aplicavel = v.Aplicavel, Atual = s == atual,
                    SitTxt = v.Aplicavel ? (v.Passa ? "Passa" : "Não passa") : v.Motivo,
                    SitFg = UiPlaca.Cores(v.Passa ? (v.Ap > 0.9 ? UiPlaca.Nivel.Alerta : UiPlaca.Nivel.Ok) : UiPlaca.Nivel.Erro).fg,
                    SitBg = UiPlaca.Cores(v.Passa ? (v.Ap > 0.9 ? UiPlaca.Nivel.Alerta : UiPlaca.Nivel.Ok) : UiPlaca.Nivel.Erro).bg,
                    SitBd = UiPlaca.Cores(v.Passa ? (v.Ap > 0.9 ? UiPlaca.Nivel.Alerta : UiPlaca.Nivel.Ok) : UiPlaca.Nivel.Erro).bd
                };
            }).ToList();
            linhas = linhas.Where(l => l.Passa).OrderBy(l => massa(l.Sec)).Concat(linhas.Where(l => !l.Passa)).ToList();
            var leve = linhas.FirstOrDefault(l => l.Passa);
            if (leve != null) { leve.Leve = true; leve.Marca = "mais leve"; }
            var at = linhas.FirstOrDefault(l => l.Sec == atual);
            if (at != null) at.Marca = (at.Leve ? at.Marca + " · " : "") + "atual";
            int total = linhas.Count, passam = linhas.Count(l => l.Passa);
            Resumo.Text = total + (total == 1 ? " perfil" : " perfis") + "  ·  " + passam + (passam == 1 ? " passa" : " passam");
            if (soPassam) linhas = linhas.Where(l => l.Passa || l.Atual).ToList();
            dg.ItemsSource = linhas;
            if (leve != null) { dg.SelectedItem = leve; dg.ScrollIntoView(leve); }
        }

        public void SoPassam(bool v) { soPassam = v; montaSeg(); Preencher(); }

        void Usar()
        {
            var l = dg.SelectedItem as Linha;
            if (l == null || l.Sec == atual) { Close(); return; }
            PerfilEscolhido = l.Sec.Nome;
            DialogResult = true;
        }
    }

    // ---------- placa de base ----------

    public class ListaDiametros : StringConverter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext c) { return true; }
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext c) { return true; }
        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext c) { return new StandardValuesCollection(Parafusos.D.Select(d => "M" + d).ToArray()); }
    }

    public class ListaClasses : StringConverter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext c) { return true; }
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext c) { return true; }
        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext c) { return new StandardValuesCollection(Parafusos.Classes); }
    }

    public class ListaFixacao : StringConverter
    {
        public const string Parafuso = "Parafuso métrico (classe)";
        public override bool GetStandardValuesSupported(ITypeDescriptorContext c) { return true; }
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext c) { return true; }
        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext c)
        { var l = new List<string> { Parafuso }; l.AddRange(Chumbadores.Linhas().Select(x => "Chumbador " + x)); return new StandardValuesCollection(l); }
    }

    public class ListaChumbadores : StringConverter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext c) { return true; }
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext c) { return true; }
        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext c)
        {
            var pe = c != null ? c.Instance as PlacaEdit : null;
            Chumbador at = pe != null ? Chumbadores.Achar(pe.P.Chumbador) : null;
            return new StandardValuesCollection((at != null ? Chumbadores.DaLinha(at.Linha) : Chumbadores.Lista).Select(x => x.Pol + " hef " + x.Hef.ToString("0")).ToList());
        }
    }

    public class ListaCoefChumb : StringConverter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext c) { return true; }
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext c) { return false; }
        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext c) { return new StandardValuesCollection(Chumbadores.CoefSugeridos.Select(x => Num.Fmt(x, 0)).ToList()); }
    }

    public class ListaRisco : StringConverter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext c) { return true; }
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext c) { return true; }
        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext c) { return new StandardValuesCollection(new[] { "automático", "baixo", "alto" }); }
    }

    public class ListaTipoCarga : StringConverter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext c) { return true; }
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext c) { return true; }
        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext c) { return new StandardValuesCollection(new[] { "automático" }.Concat(Chumbadores.NomesTipos).ToList()); }
    }

    // Fachada da PlacaBase com nomes e unidades para a grade de propriedades
    public class PlacaEdit
    {
        public PlacaBase P;
        public PlacaEdit(PlacaBase p) { P = p; }

        [Category("2. Parafusos"), DisplayName("Fixação na base"), TypeConverter(typeof(ListaFixacao)),
         Description("Parafuso métrico (verificado pela classe do aço) ou chumbador de expansão de catálogo (verificado pela carga de ruptura do fabricante ÷ coeficiente do chumbador).")]
        public string Fixacao
        {
            get { var ch = Chumbadores.Achar(P.Chumbador); return ch == null ? ListaFixacao.Parafuso : "Chumbador " + ch.Linha; }
            set
            {
                if (value == ListaFixacao.Parafuso) { P.Chumbador = null; return; }
                string linha = value.Replace("Chumbador ", "");
                var atual = Chumbadores.Achar(P.Chumbador);
                if (atual != null && atual.Linha == linha) return;
                var l = Chumbadores.DaLinha(linha);
                var perto = l.OrderBy(x => Math.Abs(x.Rosca - P.Diametro)).ThenBy(x => x.Hef).FirstOrDefault();   // diametro parecido com o parafuso atual
                if (perto != null) P.Chumbador = perto.Nome;
            }
        }

        [Category("2. Parafusos"), DisplayName("Chumbador (Ø e embutimento)"), TypeConverter(typeof(ListaChumbadores)),
         Description("Diâmetro da rosca e profundidade de embutimento (hef) do catálogo. Só vale com fixação por chumbador.")]
        public string Chumb
        {
            get { var ch = Chumbadores.Achar(P.Chumbador); return ch == null ? "—" : ch.Pol + " hef " + ch.Hef.ToString("0"); }
            set
            {
                var atual = Chumbadores.Achar(P.Chumbador);
                var l = atual != null ? Chumbadores.DaLinha(atual.Linha) : Chumbadores.Lista;
                var novo = l.FirstOrDefault(x => x.Pol + " hef " + x.Hef.ToString("0") == value);
                if (novo != null) P.Chumbador = novo.Nome;
            }
        }

        [Category("2. Parafusos"), DisplayName("Coef. segurança do chumbador"), TypeConverter(typeof(ListaCoefChumb)),
         Description("Divide a carga de ruptura do catálogo. 0 = pela tabela da Walsywa (tipo de carga + grau de risco). Baixo / alto risco: estática 3 / 5; variável 4 / 5; dinâmica 7 / 15; choque 5 / 10.")]
        public string CoefChumb
        {
            get { return P.CoefChumbador > 0 ? Num.Fmt(P.CoefChumbador, 1) : "0 (pelo tipo de carga)"; }
            set { double v; if (Num.Ler(value, out v) && v >= 0) P.CoefChumbador = v; }
        }
        [Category("2. Parafusos"), DisplayName("Tipo de carga (chumbador)"), TypeConverter(typeof(ListaTipoCarga)),
         Description("Define o coeficiente do chumbador quando o campo do coeficiente é 0: estática 3, variável 4, dinâmica 7, choque 5 (mínimos da faixa Walsywa). Automático = dinâmica se houver carga giratória, senão variável.")]
        public string TipoCarga
        {
            get { int i = Array.IndexOf(Chumbadores.Tipos, (P.TipoCarga ?? "").ToLowerInvariant()); return i < 0 ? "automático" : Chumbadores.NomesTipos[i]; }
            set { int i = Array.IndexOf(Chumbadores.NomesTipos, value); P.TipoCarga = i < 0 ? "" : Chumbadores.Tipos[i]; }
        }
        [Category("2. Parafusos"), DisplayName("Grau de risco (chumbador)"), TypeConverter(typeof(ListaRisco)),
         Description("Tabela da Walsywa: baixo risco = estática 3, variável 4, dinâmica 7, choque 5; alto risco = estática 5, variável 5, dinâmica 15, choque 10. Alto risco = a falha pode ferir pessoas ou derrubar o equipamento. Automático = alto com carga giratória, senão baixo (com aviso).")]
        public string Risco
        {
            get { string r = (P.Risco ?? "").ToLowerInvariant(); return r == "alto" ? "alto" : r == "baixo" ? "baixo" : "automático"; }
            set { P.Risco = value == "alto" || value == "baixo" ? value : ""; }
        }
        [Category("1. Medidas"), DisplayName("Medir pelo perfil"), TypeConverter(typeof(SimNao)), Description("Sim = perfil do pilar + sobra de cada lado (arredonda de 10 em 10 mm). Não = medidas digitadas.")]
        public bool PeloPerfil { get { return P.PeloPerfil; } set { P.PeloPerfil = value; } }
        [Category("1. Medidas"), DisplayName("Sobra de cada lado (mm)"), Description("Folga da placa além do contorno do perfil em cada lado (mm), usada quando \"Medir pelo perfil\" = Sim.")] public double Sobra { get { return P.Sobra; } set { P.Sobra = value; } }
        [Category("1. Medidas"), DisplayName("Comprimento no plano (mm)"), Description("Só vale com \"Medir pelo perfil\" = Não.")] public double H { get { return P.H; } set { P.H = value; } }
        [Category("1. Medidas"), DisplayName("Largura (mm)"), Description("Só vale com \"Medir pelo perfil\" = Não.")] public double B { get { return P.B; } set { P.B = value; } }
        [Category("1. Medidas"), DisplayName("Espessura (mm)"), Description("Espessura adotada da chapa de base (mm), comparada com a espessura necessária calculada por flexão.")] public double Esp { get { return P.Esp; } set { P.Esp = value; } }
        [Category("1. Medidas"), DisplayName("fy da chapa (MPa)"), Description("0 = 250 (ASTM A36).")] public double Fy { get { return P.FyChapa; } set { P.FyChapa = value; } }
        [Category("1. Medidas"), DisplayName("Concreto fck (MPa)"), Description("fck do concreto da laje de apoio. 0 = 20 MPa. Quando fck < fck de ensaio do chumbador, a carga no concreto é reduzida por √(fck/fck_ensaio).")]
        public double Fck { get { return P.Fck; } set { P.Fck = Math.Max(0, value); } }
        [Category("2. Parafusos"), DisplayName("Fileiras (no plano)"), Description("Número de linhas de parafusos/chumbadores ao longo da direção do plano do pórtico (mínimo 1).")] public int Fileiras { get { return P.Fileiras; } set { P.Fileiras = Math.Max(1, value); } }
        [Category("2. Parafusos"), DisplayName("Parafusos por fileira"), Description("Quantidade de parafusos/chumbadores em cada fileira, na direção da largura B da placa.")] public int PorFileira { get { return P.PorFileira; } set { P.PorFileira = Math.Max(1, value); } }
        [Category("2. Parafusos"), DisplayName("Borda: centro do furo (mm)"), Description("Distância do centro dos furos externos até a borda da placa (mm).")] public double Borda { get { return P.Borda; } set { P.Borda = value; } }
        [Category("2. Parafusos"), DisplayName("Diâmetro"), TypeConverter(typeof(ListaDiametros)), Description("Diâmetro nominal do parafuso métrico (M10 a M36). Só vale quando a fixação é por parafuso métrico.")]
        public string Diametro { get { return "M" + P.Diametro; } set { double d; if (Num.Ler(value.TrimStart('M', 'm'), out d)) P.Diametro = d; } }
        [Category("2. Parafusos"), DisplayName("Classe"), TypeConverter(typeof(ListaClasses)), Description("Classe de resistência do parafuso métrico ISO 898-1 (ex.: 4.6, 5.8, 8.8, 10.9).")] public string Classe { get { return P.Classe; } set { P.Classe = value; } }
        [Category("2. Parafusos"), DisplayName("Classe da barra roscada (químico)"), TypeConverter(typeof(ListaClasses)),
         Description("Só para chumbador químico (WQA, WQI, WQE): aço da barra verificado por von Mises com Sp da Tabela 6 (Aula 2). Padrão 4.6 = barra comum, lado seguro.")]
        public string ClasseBarra { get { return string.IsNullOrEmpty(P.ClasseBarra) ? "4.6" : P.ClasseBarra; } set { P.ClasseBarra = value; } }
        [Category("2. Parafusos"), DisplayName("Rosca no plano de corte"), TypeConverter(typeof(SimNao)), Description("Sim = o plano de cisalhamento passa pela parte roscada (usa área resistente As); Não = passa pelo fuste liso.")] public bool Rosca { get { return P.RoscaNoCorte; } set { P.RoscaNoCorte = value; } }
        [Category("3. Modelo"), DisplayName("Giro na mesa do perfil"), TypeConverter(typeof(SimNao)), Description("Sim = a placa gira na mesa comprimida; Não = na borda comprimida da placa.")]
        public bool Pivo { get { return P.PivoNaMesa; } set { P.PivoNaMesa = value; } }
        [Category("4. Solda pilar-placa"), DisplayName("Eletrodo"), TypeConverter(typeof(ListaEletrodos)), Description("Eletrodo AWS do filete em volta do perfil (tabela de resistência do eletrodo).")]
        public string Eletrodo { get { return string.IsNullOrEmpty(P.Eletrodo) ? "E70" : P.Eletrodo; } set { P.Eletrodo = value; } }
        [Category("4. Solda pilar-placa"), DisplayName("Perna do filete (mm)"), Description("0 = usar a menor perna da tabela que passa.")]
        public double Solda { get { return P.Solda; } set { P.Solda = Math.Max(0, value); } }
    }

    public class ListaEletrodos : StringConverter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext c) { return true; }
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext c) { return true; }
        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext c) { return new StandardValuesCollection(Solda.Eletrodos); }
    }

    // ---------- assistentes ----------

    public class JanelaAssistente : JanelaEscura
    {
        public Modelo Resultado;
        readonly Vista previa = new Vista { IsHitTestVisible = false };
        readonly StackPanel campos = new StackPanel();
        readonly TextBlock erro = T("", 11, BVermelho);
        readonly TextBlock stats = T("", 11, TemaPlant.BTexto3);
        readonly ListBox tipos = new ListBox();
        readonly Dictionary<string, TextBox> c = new Dictionary<string, TextBox>();
        readonly Dictionary<string, ComboBox> o = new Dictionary<string, ComboBox>();
        readonly string sec, mat;

        static readonly string[][] Tipos = {
            new[] { "viga", "Viga contínua" }, new[] { "portico", "Pórtico de vários andares" }, new[] { "trelica", "Treliça" }, new[] { "galpao", "Galpão (duas águas)" } };

        public JanelaAssistente(string tipo, string sec, string mat)
        {
            this.sec = SecoesValida(sec) ? sec : "W 200 x 26,6"; this.mat = mat;
            Title = "Assistente de estruturas"; Width = 1060; Height = 640; MinWidth = 860; MinHeight = 500;
            var g = new Grid { Margin = new Thickness(14) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            ScrollViewer.SetHorizontalScrollBarVisibility(tipos, ScrollBarVisibility.Disabled); tipos.Background = BPainel; tipos.Foreground = BTexto; tipos.BorderThickness = new Thickness(0); tipos.FontSize = 13;
            tipos.ToolTip = "Escolha o tipo de estrutura parametrizada que deseja gerar.";
            foreach (var t in Tipos)
            {
                string dicaTipo = t[0] == "viga" ? "Gera uma viga contínua de vários vãos com apoios e carga distribuída."
                    : t[0] == "portico" ? "Gera um pórtico plano de vários vãos e andares com cargas verticais e de vento."
                    : t[0] == "trelica" ? "Gera uma treliça plana (Pratt, Howe ou Warren) com barras biarticuladas e forças nodais."
                    : "Gera um pórtico de galpão de duas águas com pilares, tesouras/vigas inclinadas e carga na cobertura.";
                var thumb = MainWindow.AssistentesInicio().FirstOrDefault(a => a.tipo == t[0]);
                var item = new DockPanel();
                var im = new Border { Width = 58, Height = 38, Background = TemaPlant.BSup1, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 10, 0), Child = new Image { Source = Miniatura.Gerar("ass-" + t[0], thumb.modelo, 54, 34), Width = 54, Height = 34 } };
                DockPanel.SetDock(im, Dock.Left); item.Children.Add(im);
                item.Children.Add(new TextBlock { Text = t[1], VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
                tipos.Items.Add(new ListBoxItem { Content = item, Tag = t[0], Padding = new Thickness(8, 7, 8, 7), ToolTip = dicaTipo });
            }
            tipos.SelectionChanged += (s, e) => MontarCampos();
            g.Children.Add(tipos);
            var meio = new DockPanel { Margin = new Thickness(14, 0, 14, 0) };
            var bt = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            bt.Children.Add(B("Criar modelo", Criar, true, "Cria a estrutura parametrizada na vista principal com as medidas e cargas informadas (Enter)."));
            bt.Children.Add(B("Cancelar", Close, false, "Fecha o assistente sem criar nem alterar o modelo (Esc)."));
            DockPanel.SetDock(bt, Dock.Bottom); meio.Children.Add(bt);
            DockPanel.SetDock(erro, Dock.Bottom); erro.Margin = new Thickness(0, 0, 0, 8); meio.Children.Add(erro);
            meio.Children.Add(new ScrollViewer { Content = campos, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            Grid.SetColumn(meio, 1); g.Children.Add(meio);
            var gp = new Grid(); gp.Children.Add(previa);
            stats.Margin = new Thickness(10, 0, 0, 8); stats.HorizontalAlignment = HorizontalAlignment.Left; stats.VerticalAlignment = VerticalAlignment.Bottom; stats.IsHitTestVisible = false; gp.Children.Add(stats);
            var bp = new Border { BorderBrush = BBorda, BorderThickness = new Thickness(1), Child = gp, ToolTip = "Pré-visualização em tempo real da geometria, apoios e cargas da estrutura que será gerada." };
            Grid.SetColumn(bp, 2); g.Children.Add(bp);
            Content = g;
            previa.MostrarValores = false;
            previa.SizeChanged += (s, e) => previa.ZoomTudo();
            int idx = Array.FindIndex(Tipos, t => t[0] == tipo);
            tipos.SelectedIndex = idx < 0 ? 0 : idx;
        }

        static bool SecoesValida(string s) { SecoesExtras.Garantir(s); return Biblioteca.AcharSecao(s) != null; }

        string Tipo { get { return (string)((ListBoxItem)tipos.SelectedItem).Tag; } }

        void Linha(string chave, string rotulo, string valor, string dica)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 9) };
            sp.Children.Add(T(rotulo, 12, BTexto, FontWeights.SemiBold));
            var tb = Campo(valor, 290, dica ?? rotulo); tb.Margin = new Thickness(0, 3, 0, 2);
            tb.TextChanged += (s, e) => Gerar();
            c[chave] = tb;
            sp.Children.Add(tb);
            if (dica != null) sp.Children.Add(T(dica, 10.5, TemaPlant.BTexto3));
            campos.Children.Add(sp);
        }

        void Opcao(string chave, string rotulo, string dica, params string[] ops)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            sp.Children.Add(T(rotulo, 12, BTexto, FontWeights.SemiBold));
            var cb = new ComboBox { Width = 290, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 3, 0, 0), ToolTip = dica ?? rotulo };
            foreach (string op in ops) cb.Items.Add(op);
            cb.SelectedIndex = 0;
            cb.SelectionChanged += (s, e) => Gerar();
            o[chave] = cb;
            sp.Children.Add(cb);
            campos.Children.Add(sp);
        }

        void MontarCampos()
        {
            campos.Children.Clear(); c.Clear(); o.Clear();
            switch (Tipo)
            {
                case "viga":
                    Linha("vaos", "Vãos (m)", "5; 5; 5", "Separe por ponto e vírgula. \"3x5\" = três vãos de 5 m.");
                    Linha("q", "Carga distribuída (kN/m)", "-10", "Negativo = para baixo.");
                    Opcao("ext", "Extremidades", "Define a vinculação nos apoios extremos da viga contínua (apoiados ou engastados).", "Apoiadas (fixo + móveis)", "Engastadas");
                    Linha("sec", "Seção", sec, "Nome do perfil do catálogo ou seção digitada (ex.: W 200 x 26,6, Ret 20x40).");
                    break;
                case "portico":
                    Linha("vaos", "Vãos (m)", "6; 6", "Separe por ponto e vírgula.");
                    Linha("alt", "Altura dos pavimentos (m), de baixo para cima", "3,5; 3; 3", "Alturas entre pisos separadas por ponto e vírgula (ex.: 3,5; 3; 3 ou 4x3).");
                    Opcao("base", "Bases", "Define o tipo de apoio nos pés dos pilares da base (engastados ou rotulados).", "Engastadas", "Rotuladas (fixas)");
                    Linha("q", "Carga nas vigas (kN/m)", "-15", "Carga vertical distribuída uniforme em todas as vigas (negativo = para baixo).");
                    Linha("vento", "Vento por pavimento (kN, no pilar da esquerda)", "5", "0 = sem vento.");
                    Linha("secp", "Seção dos pilares", sec, "Perfil aplicado a todos os pilares do pórtico.");
                    Linha("secv", "Seção das vigas", sec, "Perfil aplicado a todas as vigas do pórtico.");
                    break;
                case "trelica":
                    Opcao("tipo", "Tipo", "Geometria das diagonais e montantes da treliça (Pratt, Howe ou Warren).", "Pratt", "Howe", "Warren");
                    Linha("vao", "Vão (m)", "12", "Comprimento total entre os apoios extremos da treliça (m).");
                    Linha("alt", "Altura (m)", "1,5", "Distância vertical entre o banzo inferior e o banzo superior (m).");
                    Linha("pan", "Número de painéis", "6", "Pratt/Howe: diagonais simétricas com número par. Warren leva montantes.");
                    Linha("p", "Carga em cada nó do banzo superior (kN)", "-10", "Força vertical concentrada aplicada em cada nó do banzo superior (negativo = para baixo).");
                    Linha("sec", "Seção", "L 2\" x 1/4\"", "Todas as barras com rótula nas duas pontas.");
                    break;
                case "galpao":
                    Linha("vao", "Vão (m)", "20", "Distância livre horizontal entre os eixos dos dois pilares (m).");
                    Linha("hp", "Altura dos pilares (m)", "6", "Altura vertical do pé direito dos pilares até o beiral (m).");
                    Linha("hc", "Altura da cumeeira acima dos pilares (m)", "2", "Desnível vertical do beiral até a cumeeira central (m).");
                    Linha("q", "Carga na cobertura (kN/m, por metro de barra)", "-3", "Carga vertical distribuída ao longo das duas águas da cobertura (negativo = para baixo).");
                    Opcao("base", "Bases", "Define o tipo de apoio na base dos pilares do galpão (engastados ou rotulados).", "Engastadas", "Rotuladas (fixas)");
                    Linha("secp", "Seção dos pilares", sec, "Perfil aplicado aos pilares do galpão.");
                    Linha("secv", "Seção das vigas", sec, "Perfil aplicado às vigas inclinadas da cobertura.");
                    break;
            }
            Gerar();
        }

        double D(string k) { double v; if (!Num.Ler(c[k].Text, out v)) { c[k].BorderBrush = BVermelho; throw new FormatException(Rotulos[k] + ": digite um número (ex.: " + Exemplo[k] + ")."); } c[k].BorderBrush = BBorda; return v; }
        static readonly Dictionary<string, string> Rotulos = new Dictionary<string, string> { ["q"] = "Carga", ["vento"] = "Vento", ["vao"] = "Vão", ["alt"] = "Altura", ["p"] = "Carga por nó", ["hp"] = "Altura dos pilares", ["hc"] = "Altura da cumeeira", ["pan"] = "Painéis" };
        static readonly Dictionary<string, string> Exemplo = new Dictionary<string, string> { ["q"] = "-10", ["vento"] = "5", ["vao"] = "12", ["alt"] = "1,5", ["p"] = "-10", ["hp"] = "6", ["hc"] = "2", ["pan"] = "6" };
        string S(string k) { string s = c[k].Text.Trim(); if (!SecoesValida(s)) throw new FormatException("Seção não reconhecida: " + s); return s; }

        void Gerar()
        {
            if (tipos.SelectedItem == null) return;
            try
            {
                Modelo m = null;
                switch (Tipo)
                {
                    case "viga":
                    {
                        var v = Assistentes.Lista(c["vaos"].Text) ?? throw new FormatException("Vãos: números positivos separados por ponto e vírgula, ex.: 5; 5; 6 ou 3x5.");
                        m = Assistentes.Viga(v, D("q"), o["ext"].SelectedIndex == 1, S("sec"), mat);
                        break;
                    }
                    case "portico":
                    {
                        var v = Assistentes.Lista(c["vaos"].Text) ?? throw new FormatException("Vãos: números positivos separados por ponto e vírgula, ex.: 6; 6.");
                        var h = Assistentes.Lista(c["alt"].Text) ?? throw new FormatException("Alturas: números positivos separados por ponto e vírgula, ex.: 3,5; 3; 3.");
                        m = Assistentes.Portico(v, h, o["base"].SelectedIndex == 0, D("q"), D("vento"), S("secp"), S("secv"), mat);
                        break;
                    }
                    case "trelica":
                    {
                        int p; if (!int.TryParse(c["pan"].Text, out p) || p < 2 || p > 60) throw new FormatException("Painéis: de 2 a 60.");
                        if (D("vao") <= 0 || D("alt") <= 0) throw new FormatException("Vão e altura devem ser positivos.");
                        m = Assistentes.Trelica((string)o["tipo"].SelectedItem, D("vao"), D("alt"), p, D("p"), S("sec"), mat);
                        break;
                    }
                    case "galpao":
                        if (D("vao") <= 0 || D("hp") <= 0 || D("hc") < 0) throw new FormatException("Medidas devem ser positivas.");
                        m = Assistentes.Galpao(D("vao"), D("hp"), D("hc"), D("q"), o["base"].SelectedIndex == 0, S("secp"), S("secv"), mat);
                        break;
                }
                Resultado = m;
                erro.Text = "";
                stats.Text = m.Nos.Count + " nós · " + m.Barras.Count + " barras";
                previa.M = m; previa.Resolvido = false; previa.ZoomTudo();
            }
            catch (Exception ex) { Resultado = null; erro.Text = "⚠ " + ex.Message; }
        }

        void Criar()
        {
            Gerar();
            if (Resultado == null) return;
            DialogResult = true;
        }

        public void EnquadrarPrevia() { previa.ZoomTudo(); previa.InvalidateVisual(); }
    }

    // ---------- ajuda ----------

    public class JanelaAjuda : JanelaEscura
    {
        // (secao, atalho/comando, descricao)
        static readonly (string sec, string k, string d)[] Itens =
        {
            ("Seleção (como no AutoCAD)", "Clique", "seleciona um nó ou barra; Shift/Ctrl soma ou tira"),
            ("Seleção (como no AutoCAD)", "Arrastar → (esq. p/ dir.)", "janela: só o que estiver inteiro dentro (azul)"),
            ("Seleção (como no AutoCAD)", "Arrastar ← (dir. p/ esq.)", "cruzamento: tudo o que tocar (verde)"),
            ("Seleção (como no AutoCAD)", "Nó selecionado", "arraste o quadradinho azul para mover o nó"),
            ("Seleção (como no AutoCAD)", "Com itens selecionados", "os botões de apoio/carga aplicam direto neles; Propriedades edita todos de uma vez"),
            ("Seleção (como no AutoCAD)", "Botão direito", "menu do item; durante um comando, encerra"),
            ("Seleção (como no AutoCAD)", "Duplo clique", "barra: verificação; nó com placa: placa de base"),
            ("Desenho", "B + Enter", "barra: clique os pontos em sequência (todo comando executa no Enter ou Espaço, como no AutoCAD)"),
            ("Desenho", "Digitar 5 + Enter", "comprimento exato na direção do mouse"),
            ("Desenho", "x,y   @dx,dy   @L<ângulo", "coordenada absoluta, relativa ou polar (decimal com ponto)"),
            ("Desenho", "2,5;3", "com ponto e vírgula a vírgula vira decimal"),
            ("Desenho", "Shift / F8", "ortogonal; F10 polar (prende perto de 0°/90°)"),
            ("Desenho", "F3 / F9 / F7", "atração a nó/meio/barra · snap na grade · mostrar grade"),
            ("Desenho", "NO", "inserir nó (divide a barra)"),
            ("Desenho", "Ctrl+M · DI · MEDIR", "medir distância, ΔX, ΔY e ângulo entre dois pontos ou nós"),
            ("Desenho", "MOVER · COPIAR · ESPELHAR", "sobre a seleção: MOVER dx dy · COPIAR dx dy vezes · ESPELHAR X valor"),
            ("Desenho", "QD qy [qx] · FN fx fy [mz]", "carga distribuída nas barras / carga nos nós selecionados"),
            ("Desenho", "SELBASE · SELNOS · SELTODAS", "seleciona os nós da base · todos os nós · todas as barras"),
            ("Geral", "Ctrl+K", "paleta de comandos: busca difusa em comandos, perfis (W 200) e objetos (nó 3, barra 2)"),
            ("Desenho", "Tab · número · Enter", "entrada dinâmica ao desenhar barra/medir: digite o comprimento, Tab alterna para o ângulo, Enter confirma"),
            ("Desenho", "F2", "histórico completo da linha de comando"),
            ("Dimensionamento", "DIMENSIONAR · DIMSEL", "perfil mais leve da família que passa (todas as barras / só as selecionadas); recalcula até estabilizar"),
            ("Dimensionamento", "SOLDAS · ELETRODO E70", "tabela da perna mínima de filete em cada ponta de barra · eletrodo das soldas"),
            ("Dimensionamento", "Placa → Dimensionar", "menor parafuso, menor chapa comercial e perna mínima da solda pilar–placa"),
            ("Análise", "D · N · Q · M · V (+ Enter)", "deformada · normal · cortante · momento · verificação (repetir desliga)"),
            ("Análise", "+ / −", "escala do diagrama"),
            ("Análise", "Passar o mouse", "lê o valor no ponto da barra"),
            ("Análise", "F5", "calcular (também é automático)"),
            ("Motor Python", "Análise › Opções › Motor", "Python (NBR completas): Casos e Verificar barra passam a abrir as janelas do motor"),
            ("Motor Python", "Ações › Vento · Sismo · Pavimentos · Combinações", "NBR 6123 · NBR 15421 · cotas e diafragma · NBR 8681 (o modelo é enviado ao motor antes)"),
            ("Motor Python", "Análise › Modos de vibração · Estabilidade", "análise modal · γz e deslocamentos-limite (estabilidade calcula antes)"),
            ("Motor Python", "Dimensionamento › Verificação de barras · Catálogo", "todas as barras pela NBR 8800 (calcula antes) · propriedades dos perfis"),
            ("Motor Python", "Ligações e bases › Ligações", "parafusos, filete de solda, chapa simples e ligação de tração"),
            ("Níveis e planos (modelo espacial)", "Modelo › Planos › Nível", "ativa o nível: plano de trabalho em planta, só o andar dele (entre ele e o nível de baixo), o resto em fantasma"),
            ("Níveis e planos (modelo espacial)", "Elevação X · Elevação Y", "plano vertical no valor escolhido: só as barras desse plano, vista de lado/frente"),
            ("Níveis e planos (modelo espacial)", "3D completo", "volta ao modelo inteiro em isométrica"),
            ("Níveis e planos (modelo espacial)", "Modelo › Planos › Níveis", "criar, renomear, mudar a cota, apagar e detectar níveis pela geometria"),
            ("Níveis e planos (modelo espacial)", "Copiar pavimento para cima", "barras, nós e cargas do andar para N níveis acima com a altura dada (um Ctrl+Z desfaz)"),
            ("Níveis e planos (modelo espacial)", "Gerar pilares entre níveis", "um pilar sob cada nó do nível escolhido até o nível de baixo"),
            ("Motor Python", "Modelo › Importar CYPE", "importa .ed3 ou compara com o CYPE (também em Arquivo)"),
            ("Geral", "Ctrl+K + nome", "todos os comandos acima também estão na paleta (ex.: vento, gama z, catálogo)"),
            ("Geral", "Ctrl+N / O / S / Shift+S", "novo · abrir · salvar · salvar como"),
            ("Geral", "Ctrl+Z / Ctrl+Y", "desfazer · refazer"),
            ("Geral", "Del · Ctrl+A · Esc", "apagar · selecionar todas as barras · cancelar (em cascata)"),
            ("Geral", "F4 / roda / botão do meio", "enquadrar · zoom · mover a vista (duplo clique na roda enquadra)"),
            ("Geral", "Ctrl+E", "exportar resultados em CSV"),
            ("Geral", "Linha de comando", "digite o nome do comando (Tab completa); AJUDA lista todos"),
        };

        readonly StackPanel lista = new StackPanel();
        readonly TextBox busca;

        static string Norm(string s)
        {
            string d = (s ?? "").Normalize(System.Text.NormalizationForm.FormD);
            var sb = new System.Text.StringBuilder();
            foreach (char c in d) if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark) sb.Append(c);
            return sb.ToString().ToLowerInvariant();
        }

        public JanelaAjuda()
        {
            Title = "Ajuda e atalhos — Estructto"; Width = 780; Height = 660; MinWidth = 560; MinHeight = 400; ShowInTaskbar = true;
            var raiz = new DockPanel();

            // topo: titulo + busca
            var topo = new StackPanel { Margin = new Thickness(22, 16, 22, 10) };
            topo.Children.Add(T("Ajuda e atalhos", 20, BTexto, FontWeights.Light));
            busca = new TextBox { Margin = new Thickness(0, 10, 0, 0), Height = 28, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(8, 0, 8, 0), ToolTip = "Digite parte do atalho ou da descrição (ex.: barra, F5, zoom, carga). Esc limpa a busca." };
            busca.TextChanged += (s, e) => Montar();
            busca.KeyDown += (s, e) => { if (e.Key == Key.Escape && busca.Text != "") { busca.Text = ""; e.Handled = true; } };
            topo.Children.Add(busca);
            var rotulo = T("Buscar atalho ou comando", 10.5, TemaPlant.BTexto3); rotulo.Margin = new Thickness(2, 3, 0, 0);
            topo.Children.Add(rotulo);
            DockPanel.SetDock(topo, Dock.Top); raiz.Children.Add(topo);

            // rodape: convencoes sempre visiveis
            var conv = new Border { Background = BPainel, BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(22, 8, 22, 8) };
            conv.Child = T("Unidades: m, kN, kN/m, kN·m; deslocamentos em mm. Fx + para a direita, Fy + para cima, Mz anti-horário. N + = tração; M + traciona a fibra de baixo. A verificação é simplificada e não substitui a NBR 8800.", 11, TemaPlant.BTexto3);
            DockPanel.SetDock(conv, Dock.Bottom); raiz.Children.Add(conv);

            raiz.Children.Add(new ScrollViewer { Content = lista, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            Content = raiz;
            Loaded += (s, e) => busca.Focus();
            Montar();
        }

        public void Buscar(string t) { busca.Text = t; }

        void Montar()
        {
            lista.Children.Clear();
            lista.Margin = new Thickness(22, 4, 22, 14);
            string q = Norm(busca.Text.Trim());
            if (q == "")
            {
                // primeiros passos
                var cartao = new Border { Background = TemaPlant.BAcentoSutil, BorderBrush = TemaPlant.BAcento, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(14, 10, 14, 12), Margin = new Thickness(0, 0, 0, 12) };
                var sp = new StackPanel();
                sp.Children.Add(T("Como usar em 4 passos", 13, BTexto, FontWeights.SemiBold));
                int n = 1;
                foreach (string p in new[] { "Desenhe as barras (B) ou use Gerar estrutura.", "Selecione os nós da base e aplique apoios (aba Modelo).", "Aplique cargas (aba Ações).", "Veja os diagramas (aba Análise): o cálculo é refeito a cada alteração." })
                {
                    var l = new DockPanel { Margin = new Thickness(0, 5, 0, 0) };
                    var num = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = TemaPlant.BAcento, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Top, Child = new TextBlock { Text = (n++).ToString(), FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = TemaPlant.BTextoSobreAcento, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
                    DockPanel.SetDock(num, Dock.Left); l.Children.Add(num); l.Children.Add(T(p, 12, BTexto));
                    sp.Children.Add(l);
                }
                cartao.Child = sp; lista.Children.Add(cartao);
            }
            int achados = 0;
            foreach (var g in Itens.GroupBy(i => i.sec))
            {
                var sel = g.Where(i => q == "" || Norm(i.k).Contains(q) || Norm(i.d).Contains(q) || Norm(i.sec).Contains(q)).ToList();
                if (sel.Count == 0) continue;
                achados += sel.Count;
                var card = new Border { Background = TemaPlant.BSup2, BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(14, 8, 14, 10), Margin = new Thickness(0, 0, 0, 10) };
                var sp = new StackPanel();
                sp.Children.Add(UiPlaca.TituloSecao(g.Key, null, 0));
                foreach (var i in sel)
                {
                    var dp = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                    dp.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
                    dp.ColumnDefinitions.Add(new ColumnDefinition());
                    var k = T(i.k, 12, BTexto, FontWeights.SemiBold); k.Margin = new Thickness(0, 0, 10, 0);
                    var d = T(i.d, 12, TemaPlant.BTexto2); Grid.SetColumn(d, 1);
                    dp.Children.Add(k); dp.Children.Add(d);
                    sp.Children.Add(dp);
                }
                card.Child = sp; lista.Children.Add(card);
            }
            if (achados == 0)
            {
                var v = UiTabela.Vazio("busca", "Nada encontrado para \"" + busca.Text.Trim() + "\"", "Tente outra palavra: o nome do comando, uma tecla (F5) ou o que você quer fazer (mover, carga, zoom).");
                v.Margin = new Thickness(0, 40, 0, 0); lista.Children.Add(v);
            }
        }
    }
}
