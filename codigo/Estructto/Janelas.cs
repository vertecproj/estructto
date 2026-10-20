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
        // cores das paletas/dialogos do Plant 3D (TemaPlant): fundo #3B4453, campos #4E5A6E, faixas #2E3440
        public static readonly Brush BFundo = Pincel(0x3B, 0x44, 0x53), BPainel = Pincel(0x2E, 0x34, 0x40), BTexto = Pincel(0xF0, 0xF2, 0xF5),
            BFraco = Pincel(0xB4, 0xBC, 0xC8), BBorda = Pincel(0x5A, 0x67, 0x7C), BOk = Pincel(0x22, 0x60, 0x3E), BRuim = Pincel(0x80, 0x2C, 0x2C),
            BCinza = Pincel(0x4A, 0x54, 0x63), BVerde = Pincel(0x4C, 0xD9, 0x64), BAmarelo = Pincel(0xE8, 0xB4, 0x48), BVermelho = Pincel(0xF2, 0x4B, 0x4B), BAzul = Pincel(0x06, 0x96, 0xD7);

        public static SolidColorBrush Pincel(byte r, byte g, byte b) { var x = new SolidColorBrush(Color.FromRgb(r, g, b)); x.Freeze(); return x; }

        public JanelaEscura()
        {
            Background = BFundo; Foreground = BTexto; FontFamily = TemaPlant.Fonte; FontSize = 12;
            SourceInitialized += (s, e) => TituloEscuro(this);
            WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
            KeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); };
        }

        // Barra de titulo do Windows 11 no tom do Plant 3D (modo escuro + cor ardosia #454F61).
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        public static void TituloEscuro(Window w)
        {
            try
            {
                IntPtr h = new System.Windows.Interop.WindowInteropHelper(w).Handle;
                int um = 1; DwmSetWindowAttribute(h, 20, ref um, 4);                       // DWMWA_USE_IMMERSIVE_DARK_MODE
                int cor = 0x00614F45; DwmSetWindowAttribute(h, 35, ref cor, 4);            // DWMWA_CAPTION_COLOR (0x00BBGGRR) = #454F61
                int txt = 0x00F5F2F0; DwmSetWindowAttribute(h, 36, ref txt, 4);            // DWMWA_TEXT_COLOR
                int borda = 0x00332922; DwmSetWindowAttribute(h, 34, ref borda, 4);        // DWMWA_BORDER_COLOR = #222933
            }
            catch { }
        }

        public static TextBlock T(string s, double tam = 12, Brush cor = null, FontWeight? peso = null)
        {
            return new TextBlock { Text = s, FontSize = tam, Foreground = cor ?? BTexto, FontWeight = peso ?? FontWeights.Normal, TextWrapping = TextWrapping.Wrap };
        }

        public static Button B(string s, Action a, bool principal = false, string dica = null)
        {
            var b = new Button
            {
                Content = s, Padding = new Thickness(16, 4, 16, 4), Margin = new Thickness(6, 0, 0, 0), Foreground = Brushes.White, MinWidth = 80,
                Background = principal ? BAzul : Pincel(0x4E, 0x5A, 0x6E), BorderBrush = principal ? BAzul : BBorda, Cursor = Cursors.Hand, IsDefault = principal,
                ToolTip = dica
            };
            b.Click += (s2, e) => a();
            return b;
        }

        public static TextBox Campo(string valor, double larg = 90, string dica = null)
        {
            return new TextBox { Text = valor, Width = larg, Background = Pincel(0x4E, 0x5A, 0x6E), Foreground = BTexto, BorderBrush = BBorda, CaretBrush = BTexto, Padding = new Thickness(3, 2, 3, 2), ToolTip = dica };
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
                Background = BPainel, RowBackground = BPainel, AlternatingRowBackground = Pincel(0x2A, 0x31, 0x3B), Foreground = BTexto, BorderThickness = new Thickness(0),
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, HorizontalGridLinesBrush = Pincel(0x34, 0x3C, 0x48), CanUserAddRows = false
            };
            var h = new Style(typeof(DataGridColumnHeader));
            h.Setters.Add(new Setter(Control.BackgroundProperty, Pincel(0x2E, 0x35, 0x40)));
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

        public static MessageBoxResult Mostrar(Window dono, string titulo, string texto, string detalhe, (string nome, MessageBoxResult r, bool principal)[] botoes, string icone, Brush corIcone)
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
            var ic = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(20), Background = corIcone, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 16, 0),
                Child = new TextBlock { Text = icone, Foreground = Brushes.White, FontSize = 22, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            DockPanel.SetDock(ic, Dock.Left); corpo.Children.Add(ic);
            var txt = new StackPanel();
            txt.Children.Add(T(titulo, 16, BTexto, FontWeights.SemiBold));
            var t1 = T(texto, 12.5, BTexto); t1.TextWrapping = TextWrapping.Wrap; t1.Margin = new Thickness(0, 6, 0, 0); txt.Children.Add(t1);
            if (!string.IsNullOrEmpty(detalhe)) { var t2 = T(detalhe, 11.5, BFraco); t2.TextWrapping = TextWrapping.Wrap; t2.Margin = new Thickness(0, 6, 0, 0); txt.Children.Add(t2); }
            corpo.Children.Add(txt);
            raiz.Children.Add(corpo);
            w.Content = raiz;
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
            public Secao Sec; public string Perfil { get; set; } public string Massa { get; set; } public string Ap { get; set; }
            public string Critico { get; set; } public string Situacao { get; set; } public bool Passa;
        }

        public JanelaVerificacao(Modelo m, Barra b)
        {
            this.m = m; this.b = b;
            Title = "Verificação da barra " + b.Id; Width = 1120; Height = 640; MinWidth = 760; MinHeight = 480;
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
            rod.Children.Add(T("Flecha máx. L/" + Num.Fmt(m.LimiteFlecha, 0) + " (aba Análise). Verificação simplificada: não substitui a NBR 8800 (sem flambagem lateral nem local).", 11, BFraco));
            DockPanel.SetDock(rod, Dock.Bottom);
            raiz.Children.Add(rod);

            var corpo = new Grid { Margin = new Thickness(18, 14, 18, 0) };
            corpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
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
            cs.Children.Add(T(!v.Aplicavel ? "SEM VERIFICAÇÃO" : v.Passa ? "PASSA" : "NÃO PASSA", 20, Brushes.White, FontWeights.SemiBold));
            cs.Children.Add(T((atual != null ? atual.Descricao : b.Secao) + (b.EixoFraco ? " (deitado)" : "") + " · " + b.Material + " · L = " + Num.Fmt(b.Res.L, 2) + " m · n = " + Num.Fmt(m.Seguranca, 2) +
                (v.Aplicavel ? "   —   aproveitamento " + (v.Ap * 100).ToString("0") + "%, limitado por " + v.Critico : "   —   " + v.Motivo), 12, Brushes.White));
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
            var topo = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            topo.Children.Add(T("Perfis: os que passam primeiro (mais leve no topo), depois por dimensão", 12, BTexto, FontWeights.SemiBold));
            filtro = new ComboBox { Width = 150, Margin = new Thickness(6, 0, 0, 0), ToolTip = "Filtra os perfis por série (altura/diâmetro nominal) dentro da família escolhida." };
            familia = new ComboBox { Width = 190, ToolTip = "Escolhe a família de perfis do catálogo para comparar o aproveitamento e o peso por metro." };
            DockPanel.SetDock(filtro, Dock.Right); DockPanel.SetDock(familia, Dock.Right);
            topo.Children.Insert(0, filtro);
            topo.Children.Insert(1, familia);
            DockPanel.SetDock(topo, Dock.Top);
            dir.Children.Add(topo);
            dg = Tabela();
            dg.ToolTip = "Perfis da família selecionada ordenados pelos que passam (do mais leve ao mais pesado). Dê duplo clique numa linha ou clique em \"Usar o perfil selecionado\" para aplicá-lo à barra.";
            dg.Columns.Add(Col("Perfil", "Perfil", 290)); dg.Columns.Add(Col("kg/m", "Massa", 60)); dg.Columns.Add(Col("Aproveitamento", "Ap", 110));
            dg.Columns.Add(Col("Limitado por", "Critico", 100)); dg.Columns.Add(Col("Situação", "Situacao", 200));
            dg.MouseDoubleClick += (s, e) => { if (ItemsControl.ContainerFromElement(dg, e.OriginalSource as DependencyObject) is DataGridRow) Usar(); };
            dg.LoadingRow += (s, e) =>
            {
                var l = (Linha)e.Row.Item;
                e.Row.Foreground = l.Passa ? BVerde : BVermelho;
                e.Row.FontWeight = l.Sec == atual ? FontWeights.Bold : FontWeights.Normal;
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

        ComboBox familia; bool carregando;

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
                    Ap = v.Aplicavel ? (v.Ap * 100).ToString("0") + "%" : "—", Critico = v.Aplicavel ? v.Critico : "",
                    Situacao = v.Aplicavel ? (v.Passa ? "passa" : "não passa") : v.Motivo
                };
            }).ToList();
            linhas = linhas.Where(l => l.Passa).OrderBy(l => massa(l.Sec)).Concat(linhas.Where(l => !l.Passa)).ToList();
            var leve = linhas.FirstOrDefault(l => l.Passa);
            if (leve != null) leve.Situacao = "passa (o mais leve)";
            var at = linhas.FirstOrDefault(l => l.Sec == atual);
            if (at != null) at.Situacao += "  ◄ atual";
            dg.ItemsSource = linhas;
            if (leve != null) { dg.SelectedItem = leve; dg.ScrollIntoView(leve); }
        }

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

    // desenho da placa vista de cima (aba Medidas e aba Forças nos furos, com cotas azuis clicáveis)
    public class PlanoPlaca : Canvas
    {
        public ResPlaca R;
        public PlacaBase P;
        public bool ModoForcas;
        public Action AoEditar;

        class CaixaCota { public Rect Ret; public string Valor; public Action<double> Aplicar; }
        readonly List<CaixaCota> caixas = new List<CaixaCota>();
        TextBox editorAtivo;

        public PlanoPlaca()
        {
            ClipToBounds = true;
            MouseMove += (s, e) =>
            {
                Point pt = e.GetPosition(this);
                Cursor = caixas.Any(c => c.Ret.Contains(pt)) ? Cursors.Hand : Cursors.Arrow;
            };
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
                Width = Math.Max(52, cx.Ret.Width + 6),
                Height = cx.Ret.Height + 2,
                Background = JanelaEscura.Pincel(0x1D, 0x31, 0x44),
                Foreground = Brushes.White,
                BorderBrush = JanelaEscura.Pincel(0x3F, 0xB6, 0xEE),
                BorderThickness = new Thickness(1.5),
                CaretBrush = Brushes.White,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(2, 0, 2, 0)
            };
            SetLeft(tb, cx.Ret.X - 3);
            SetTop(tb, cx.Ret.Y - 1);
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
            dc.DrawRectangle(JanelaEscura.BPainel, null, new Rect(0, 0, ActualWidth, ActualHeight));
            if (R == null || R.H <= 0 || R.B <= 0 || ActualWidth < 100 || ActualHeight < 100) return;

            var ft = new Typeface("Segoe UI");
            double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            FormattedText Txt(string s, double tam, Brush cor, FontWeight? peso = null) =>
                new FormattedText(s ?? "", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    new Typeface(ft.FontFamily, FontStyles.Normal, peso ?? FontWeights.Normal, FontStretches.Normal), tam, cor, dip);

            // vista isométrica auxiliar à esquerda; a vista de cima ocupa o resto
            double isoW = ActualWidth >= 640 ? Math.Min(260, ActualWidth * 0.34) : 0;
            double padX = 180, padY = ModoForcas ? 175 : 155;
            double esc = Math.Max(0.15, Math.Min((ActualWidth - isoW - padX) / R.H, (ActualHeight - padY) / R.B));
            Point c = new Point(isoW + (ActualWidth - isoW) / 2 - 14, ActualHeight / 2 + (ModoForcas ? 14 : 4));
            if (isoW > 0) DesenharIso(dc, new Rect(12, ModoForcas ? 34 : 10, isoW - 20, Math.Min(ActualHeight - (ModoForcas ? 70 : 46), 300)), Txt);
            double sx = R.Espelhar ? -1 : 1;
            Func<double, double, Point> T = (y, z) => new Point(c.X + sx * y * esc, c.Y - z * esc);

            Rect rectPlaca = new Rect(T(-R.H / 2, R.B / 2), T(R.H / 2, -R.B / 2));
            dc.DrawRectangle(JanelaEscura.Pincel(0x32, 0x39, 0x46), null, rectPlaca);
            // hachura diagonal suave como no v3.html
            dc.PushClip(new RectangleGeometry(rectPlaca));
            var penHach = new Pen(JanelaEscura.Pincel(0x3E, 0x47, 0x57), 1.5);
            double diag = rectPlaca.Width + rectPlaca.Height;
            for (double d = -rectPlaca.Height; d < diag; d += 9)
                dc.DrawLine(penHach, new Point(rectPlaca.Left + d, rectPlaca.Top), new Point(rectPlaca.Left + d - rectPlaca.Height, rectPlaca.Bottom));
            dc.Pop();
            dc.DrawRectangle(null, new Pen(JanelaEscura.Pincel(0xD8, 0xDE, 0xE6), 2), rectPlaca);

            // perfil do pilar
            double h = R.RefIn, bf = R.RefOut;
            var azul = new Pen(JanelaEscura.Pincel(0x3F, 0xB6, 0xEE), 2.2);
            var cheio = new SolidColorBrush(Color.FromArgb(90, 0x1E, 0x90, 0xE0));
            string forma = R.Sec != null ? R.Sec.Forma : "";
            double tfe = R.Sec != null ? Math.Max(R.Sec.Tf * 1000, 1.5) : 0, twe = R.Sec != null ? Math.Max(R.Sec.Tw * 1000, 1.5) : 0;
            Action<double, double, double, double> ret = (y1, z1, y2, z2) => dc.DrawRectangle(cheio, azul, new Rect(T(y1, z1), T(y2, z2)));
            Action<Point[]> poli = pts =>
            {
                var g = new StreamGeometry();
                using (var ctx = g.Open()) { ctx.BeginFigure(pts[0], true, true); ctx.PolyLineTo(pts.Skip(1).ToList(), true, true); }
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
                var tSec = Txt(R.Sec.Nome.Replace("x", "×"), 11, JanelaEscura.Pincel(0x7F, 0xC8, 0xF0));
                dc.DrawText(tSec, new Point(c.X - tSec.Width / 2, c.Y + bf / 2 * esc + 6));
            }

            if (!double.IsNaN(R.PivoY))
            {
                var tr = new Pen(JanelaEscura.BAmarelo, 1) { DashStyle = DashStyles.Dash };
                dc.DrawLine(tr, T(R.PivoY, R.B / 2 + 10), T(R.PivoY, -R.B / 2 - 10));
            }

            // furos
            double tmax = R.Paraf.Count > 0 ? Math.Max(1e-9, R.Paraf.Max(p => p[2])) : 1;
            Brush corAnel = R.ChOk ? JanelaEscura.BVerde : JanelaEscura.BVermelho;
            var penAnel = new Pen(corAnel, 2.8);
            double rAnel = Math.Max(9, Math.Min(15, R.Dia / 2 * esc * 1.25));
            foreach (double[] p in R.Paraf)
            {
                Point q = T(p[0], p[1]);
                if (!ModoForcas)
                {
                    dc.DrawEllipse(JanelaEscura.BPainel, penAnel, q, rAnel, rAnel);
                    dc.DrawEllipse(corAnel, null, q, Math.Max(3, rAnel * 0.32), Math.Max(3, rAnel * 0.32));
                }
                else
                {
                    byte a = (byte)(80 + 175 * Math.Min(1, p[2] / tmax));
                    var br = new SolidColorBrush(Color.FromArgb(a, 0xF2, 0x4B, 0x4B));
                    double rf = Math.Max(6, R.Dia / 2 * esc);
                    dc.DrawEllipse(br, new Pen(Brushes.White, 1.2), q, rf, rf);
                    if (R.TemEsforcos)
                    {
                        var tf1 = Txt("T=" + Num.Fmt(p[2], 1) + " kN", 10.5, Brushes.White, FontWeights.SemiBold);
                        var tf2 = Txt("V=" + Num.Fmt(R.Fv, 1) + " kN", 10, JanelaEscura.BFraco);
                        double tx = q.X >= c.X ? q.X + rf + 6 : q.X - rf - Math.Max(tf1.Width, tf2.Width) - 6;
                        dc.DrawText(tf1, new Point(tx, q.Y - 14));
                        dc.DrawText(tf2, new Point(tx, q.Y));
                    }
                }
            }

            if (ModoForcas && R.TemEsforcos)
            {
                var tEsf = Txt("Esforços no pé: N = " + Num.Fmt(R.N, 2) + " kN (" + (R.N >= 0 ? "tração" : "compressão") + ") · V = " + Num.Fmt(R.V, 2) + " kN · M = " + Num.Fmt(R.M, 2) + " kN·m   ·   " + R.Modelo, 11.5, JanelaEscura.BTexto);
                dc.DrawText(tEsf, new Point(16, 10));
            }

            // cotas e caixinhas azuis clicáveis
            if (P != null)
            {
                var penCota = new Pen(JanelaEscura.Pincel(0x6F, 0x7A, 0x8A), 1);
                var bgCaixa = JanelaEscura.Pincel(0x24, 0x38, 0x4A);
                var bdCaixa = new Pen(JanelaEscura.Pincel(0x3F, 0xB6, 0xEE), 1);
                var fgCaixa = JanelaEscura.Pincel(0x3F, 0xB6, 0xEE);

                void DesenharCaixa(double cx, double cy, string texto, string valEdicao, Action<double> aplicar)
                {
                    var ftBox = Txt(texto, 12.5, fgCaixa, FontWeights.SemiBold);
                    double w = Math.Max(46, ftBox.Width + 16), hBox = 22;
                    Rect rBox = new Rect(cx - w / 2, cy - hBox / 2, w, hBox);
                    dc.DrawRoundedRectangle(bgCaixa, bdCaixa, rBox, 3, 3);
                    dc.DrawText(ftBox, new Point(cx - ftBox.Width / 2, cy - ftBox.Height / 2));
                    caixas.Add(new CaixaCota { Ret = rBox, Valor = valEdicao, Aplicar = aplicar });
                }

                double xL = rectPlaca.Left, xR = rectPlaca.Right, yT = rectPlaca.Top, yB = rectPlaca.Bottom;

                // Cota inferior: largura no plano (H)
                double yCotaH = yB + 26;
                dc.DrawLine(penCota, new Point(xL, yCotaH), new Point(xR, yCotaH));
                dc.DrawLine(penCota, new Point(xL, yCotaH - 7), new Point(xL, yCotaH + 7));
                dc.DrawLine(penCota, new Point(xR, yCotaH - 7), new Point(xR, yCotaH + 7));
                DesenharCaixa((xL + xR) / 2, yCotaH + 20, Num.Fmt(R.H, 0), Num.Fmt(R.H, 0), v =>
                {
                    if (v <= 0) return;
                    if (P.PeloPerfil) P.Sobra = v > R.RefIn ? Math.Max(0, (v - R.RefIn) / 2) : Math.Max(0, v);
                    else P.H = v;
                });

                // Cota à direita: altura / largura fora do plano (B)
                double xCotaB = xR + 28;
                dc.DrawLine(penCota, new Point(xCotaB, yT), new Point(xCotaB, yB));
                dc.DrawLine(penCota, new Point(xCotaB - 7, yT), new Point(xCotaB + 7, yT));
                dc.DrawLine(penCota, new Point(xCotaB - 7, yB), new Point(xCotaB + 7, yB));
                DesenharCaixa(xCotaB + 34, (yT + yB) / 2, Num.Fmt(R.B, 0), Num.Fmt(R.B, 0), v =>
                {
                    if (v <= 0) return;
                    if (P.PeloPerfil) P.Sobra = v > R.RefOut ? Math.Max(0, (v - R.RefOut) / 2) : Math.Max(0, v);
                    else P.B = v;
                });

                // Cotas superiores: borda (centro do furo) e entre furos
                double yCotaTop = yT - 22;
                double xFuro1 = xL + P.Borda * esc;
                dc.DrawLine(penCota, new Point(xL, yCotaTop), new Point(xFuro1, yCotaTop));
                dc.DrawLine(penCota, new Point(xL, yCotaTop - 7), new Point(xL, yCotaTop + 7));
                dc.DrawLine(penCota, new Point(xFuro1, yCotaTop - 7), new Point(xFuro1, yCotaTop + 7));
                DesenharCaixa((xL + xFuro1) / 2, yCotaTop - 18, Num.Fmt(P.Borda, 0), Num.Fmt(P.Borda, 0), v =>
                {
                    if (v > 0 && 2 * v < R.H && 2 * v < R.B) P.Borda = v;
                });

                if (P.Fileiras >= 2)
                {
                    double entre = R.H - 2 * P.Borda;
                    double xFuroFim = xR - P.Borda * esc;
                    dc.DrawLine(penCota, new Point(xFuro1, yCotaTop), new Point(xFuroFim, yCotaTop));
                    dc.DrawLine(penCota, new Point(xFuroFim, yCotaTop - 7), new Point(xFuroFim, yCotaTop + 7));
                    double cxEntre = Math.Max(xFuro1 + 52, (xFuro1 + xFuroFim) / 2);
                    DesenharCaixa(cxEntre, yCotaTop - 18, Num.Fmt(entre, 0), Num.Fmt(entre, 0), v =>
                    {
                        if (v > 0 && v < R.H - 10) P.Borda = Math.Round((R.H - v) / 2, 1);
                    });
                }

                // Sobra (linha tracejada do perfil até a borda inferior da placa, clicável)
                double yPerfB = c.Y + bf / 2 * esc;
                if (yB - yPerfB > 24)
                {
                    var penTr = new Pen(JanelaEscura.Pincel(0x6F, 0x7A, 0x8A), 1) { DashStyle = new DashStyle(new double[] { 3, 4 }, 0) };
                    dc.DrawLine(penTr, new Point(c.X, yPerfB + 18), new Point(c.X, yB));
                    double sobraVal = P.PeloPerfil ? P.Sobra : Math.Max(0, (R.H - R.RefIn) / 2);
                    var ftSobra = Txt("sobra " + Num.Fmt(sobraVal, 0), 11.5, P.PeloPerfil ? fgCaixa : JanelaEscura.BFraco);
                    double sxBox = c.X + 44, syBox = (yPerfB + 18 + yB) / 2;
                    Rect rSobra = new Rect(sxBox - ftSobra.Width / 2 - 6, syBox - 10, ftSobra.Width + 12, 20);
                    if (P.PeloPerfil) dc.DrawRoundedRectangle(bgCaixa, bdCaixa, rSobra, 3, 3);
                    dc.DrawText(ftSobra, new Point(sxBox - ftSobra.Width / 2, syBox - ftSobra.Height / 2));
                    caixas.Add(new CaixaCota { Ret = rSobra, Valor = Num.Fmt(sobraVal, 0), Aplicar = v => { P.PeloPerfil = true; P.Sobra = Math.Max(0, v); } });
                }
            }

            // Legenda curta embaixo
            double yLeg = ActualHeight - 20;
            dc.DrawEllipse(null, new Pen(corAnel, 2), new Point(26, yLeg), 6, 6);
            var tLeg1 = Txt((R.Ch != null ? "chumbador " : "parafuso ") + (R.ChOk ? "ok" : "não passa"), 11.5, JanelaEscura.BFraco);
            dc.DrawText(tLeg1, new Point(38, yLeg - tLeg1.Height / 2));
        }

        // Vista isométrica: placa com espessura, chumbadores e o trecho do pilar com a inclinação real
        // (a barra só inclina no plano do pórtico). Eixos 3D: X = y da placa (no plano), Y = normal, Z = fora do plano.
        void DesenharIso(DrawingContext dc, Rect a, Func<string, double, Brush, FontWeight?, FormattedText> Txt)
        {
            dc.DrawRoundedRectangle(JanelaEscura.Pincel(0x29, 0x2F, 0x3A), new Pen(JanelaEscura.Pincel(0x4A, 0x54, 0x63), 1), a, 6, 6);
            dc.DrawText(Txt("Vista 3D", 11, JanelaEscura.BFraco, null), new Point(a.X + 10, a.Y + 7));
            double sx = R.Espelhar ? -1 : 1;
            double dX = sx * R.BarraY, dY = R.BarraN;
            double ld = Math.Sqrt(dX * dX + dY * dY);
            if (ld < 1e-9) { dX = 0; dY = 1; } else { dX /= ld; dY /= ld; }
            double hIn = R.RefIn, hOut = R.RefOut, L = Math.Max(Math.Max(hIn, hOut) * 2.4, Math.Max(R.H, R.B) * 0.9);
            double tv = Math.Max(P != null ? P.Esp : 10, Math.Max(R.H, R.B) * 0.035);

            // seção do pilar perpendicular ao eixo: e1 no plano do pórtico, e2 = Z
            double e1X = dY, e1Y = -dX;
            var sec = new List<double[]>();
            if (R.Sec != null && R.Sec.Forma == "TC")
                for (int i = 0; i < 28; i++) { double t = 2 * Math.PI * i / 28; sec.Add(new[] { hIn / 2 * Math.Cos(t), hIn / 2 * Math.Sin(t) }); }
            else
                foreach (var q in new[] { new[] { -1.0, -1.0 }, new[] { 1.0, -1.0 }, new[] { 1.0, 1.0 }, new[] { -1.0, 1.0 } }) sec.Add(new[] { q[0] * hIn / 2, q[1] * hOut / 2 });
            var baseP = new List<double[]>(); var topoP = new List<double[]>();
            foreach (var s in sec)
            {
                double X = s[0] * e1X, Y = s[0] * e1Y, Z = s[1];
                double t0 = Math.Abs(dY) > 1e-6 ? -Y / dY : 0;     // desce pelo eixo até a face da placa (corte em bisel)
                baseP.Add(new[] { X + t0 * dX, 0, Z });
                topoP.Add(new[] { X + L * dX, Y + L * dY, Z });
            }

            Func<double[], Point> raw = v => new Point(v[0] - v[2] * 0.55, -v[1] + v[2] * 0.33 + v[0] * 0.08);   // perspectiva com o plano do pórtico quase de frente: o ângulo da barra aparece como é
            var todos = new List<double[]>();
            foreach (double yy in new[] { 0, -tv }) foreach (double x in new[] { -R.H / 2, R.H / 2 }) foreach (double z in new[] { -R.B / 2, R.B / 2 }) todos.Add(new[] { x, yy, z });
            todos.AddRange(topoP); todos.AddRange(baseP);
            double minX = todos.Min(v => raw(v).X), maxX = todos.Max(v => raw(v).X), minY = todos.Min(v => raw(v).Y), maxY = todos.Max(v => raw(v).Y);
            Rect area = new Rect(a.X + 14, a.Y + 28, Math.Max(10, a.Width - 28), Math.Max(10, a.Height - 44));
            double k = Math.Min(area.Width / Math.Max(1e-6, maxX - minX), area.Height / Math.Max(1e-6, maxY - minY));
            double ox = area.X + (area.Width - (maxX - minX) * k) / 2 - minX * k, oy = area.Y + (area.Height - (maxY - minY) * k) / 2 - minY * k;
            Func<double, double, double, Point> I = (x, y, z) => { var p = raw(new[] { x, y, z }); return new Point(ox + p.X * k, oy + p.Y * k); };
            Func<double[], Point> Iv = v => I(v[0], v[1], v[2]);
            void Poli(Brush f, Pen pen, params Point[] pts)
            {
                var g = new StreamGeometry();
                using (var ctx = g.Open()) { ctx.BeginFigure(pts[0], f != null, true); ctx.PolyLineTo(pts.Skip(1).ToList(), true, true); }
                dc.DrawGeometry(f, pen, g);
            }

            // placa: faces laterais da frente e topo
            double hx = R.H / 2, hz = R.B / 2;
            var penPl = new Pen(JanelaEscura.Pincel(0xD8, 0xDE, 0xE6), 1.2);
            Poli(JanelaEscura.Pincel(0x3A, 0x42, 0x50), penPl, I(hx, 0, -hz), I(hx, 0, hz), I(hx, -tv, hz), I(hx, -tv, -hz));
            Poli(JanelaEscura.Pincel(0x33, 0x3A, 0x47), penPl, I(-hx, 0, hz), I(hx, 0, hz), I(hx, -tv, hz), I(-hx, -tv, hz));
            Poli(JanelaEscura.Pincel(0x4A, 0x54, 0x66), penPl, I(-hx, 0, -hz), I(hx, 0, -hz), I(hx, 0, hz), I(-hx, 0, hz));

            // chumbadores / parafusos
            Brush corF = R.ChOk ? JanelaEscura.BVerde : JanelaEscura.BVermelho;
            var penF = new Pen(corF, 2);
            double hF = tv * 1.6;
            foreach (double[] p in R.Paraf)
            {
                double X = sx * p[0], Z = p[1];
                dc.DrawLine(penF, I(X, 0, Z), I(X, hF, Z));
                Point cc = I(X, hF, Z);
                double rr = Math.Max(2.5, R.Dia / 2 * k * 1.3);
                dc.DrawEllipse(JanelaEscura.BPainel, penF, cc, rr, rr * 0.55);
            }

            // normal da placa (referência) e arco do ângulo
            var cinza = new Pen(JanelaEscura.Pincel(0x8A, 0x94, 0xA4), 1.2) { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) };
            dc.DrawLine(cinza, I(0, 0, 0), I(0, L * 1.05, 0));

            // pilar: faces do prisma do fundo para a frente
            var faces = new List<Point[]>(); var prof = new List<double>();
            for (int i = 0; i < baseP.Count; i++)
            {
                int j = (i + 1) % baseP.Count;
                faces.Add(new[] { Iv(baseP[i]), Iv(baseP[j]), Iv(topoP[j]), Iv(topoP[i]) });
                prof.Add(baseP[i][2] + baseP[j][2] + topoP[i][2] + topoP[j][2] + 0.1 * (baseP[i][0] + baseP[j][0]));
            }
            var cheio = new SolidColorBrush(Color.FromArgb(150, 0x1E, 0x6E, 0xB0));
            var penAz = new Pen(JanelaEscura.Pincel(0x3F, 0xB6, 0xEE), 1.2);
            foreach (int i in Enumerable.Range(0, faces.Count).OrderBy(i => prof[i])) Poli(cheio, penAz, faces[i]);
            Poli(new SolidColorBrush(Color.FromArgb(200, 0x2A, 0x86, 0xC8)), penAz, topoP.Select(Iv).ToArray());
            dc.DrawLine(new Pen(Brushes.White, 1) { DashStyle = new DashStyle(new double[] { 6, 3, 1, 3 }, 0) }, I(0, 0, 0), I(L * dX * 1.12, L * dY * 1.12, 0));

            if (R.Inclinada)
            {
                double ra = L * 0.55, ang = Math.Atan2(dX, dY);
                var arco = new List<Point>();
                for (int i = 0; i <= 24; i++) { double t = ang * i / 24; arco.Add(I(ra * Math.Sin(t), ra * Math.Cos(t), 0)); }
                var penArc = new Pen(JanelaEscura.BAmarelo, 2);
                for (int i = 1; i < arco.Count; i++) dc.DrawLine(penArc, arco[i - 1], arco[i]);
                var tA = Txt(Num.Fmt(R.AngNormal, 1) + "°", 12.5, JanelaEscura.BAmarelo, FontWeights.SemiBold);
                Point pm = I(ra * 1.15 * Math.Sin(ang / 2), ra * 1.15 * Math.Cos(ang / 2), 0);
                dc.DrawText(tA, new Point(pm.X - tA.Width / 2, pm.Y - tA.Height / 2));
                var tN = Txt("inclinada " + Num.Fmt(R.AngNormal, 1) + "° da normal da placa", 10.5, JanelaEscura.BAmarelo, null);
                dc.DrawText(tN, new Point(a.X + 10, a.Bottom - 18));
            }
            else
            {
                var tN = Txt("pilar perpendicular à placa", 10.5, JanelaEscura.BFraco, null);
                dc.DrawText(tN, new Point(a.X + 10, a.Bottom - 18));
            }
        }
    }

    public class JanelaPlaca : JanelaEscura
    {
        public bool Mudou;
        readonly Modelo m; readonly No n;
        readonly PlacaBase original, edit, refInicial;
        readonly PlacaEdit pe;
        readonly PlanoPlaca plano = new PlanoPlaca();
        readonly StackPanel faixaTopo = new StackPanel();
        readonly Border areaAba = new Border();
        readonly StackPanel colDir = new StackPanel();
        readonly Grid overlayTrocar = new Grid { Visibility = Visibility.Collapsed };
        readonly StackPanel listaTrocar = new StackPanel();
        readonly StackPanel rodPref = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        // Outras fixações: quem abre a janela informa os nós possíveis e como avaliar uma placa num nó (pior combinação)
        public Func<No, PlacaBase, ResPlaca> AvaliarEm;
        public List<No> Candidatos = new List<No>();
        public readonly List<No> AplicarEm = new List<No>();   // nós que recebem a mesma placa ao Aplicar
        readonly HashSet<int> marcados = new HashSet<int>();

        int abaAtiva;                  // 0 = Medidas, 1 = Forças nos furos, 2 = Ficha de fabricação, 3 = Cálculo
        bool prefFuroMenor;            // false = Mais leve, true = Furo menor
        bool mostrarOutroArranjo;      // linha para digitar Fileiras x PorFileira
        bool avFixAberto, avRodAberto; // estado dos expanders Avançado
        bool calcFixAberto, calcChAberto, calcSdAberto; // expanders Ver cálculo nos cartões da direita
        string filtroBuscaCh = "";
        int filtroTipoCh;              // 0 = Todos, 1 = Mecânico, 2 = Químico
        bool verTodosNaoServem;
        string chumbAntes = "";        // chumbador anterior (ao abrir a janela ou ao abrir o painel Trocar)
        string msgDim = "";
        TextBlock lblSubAba;
        StackPanel btsAbas;
        StackPanel btsFiltroTipoCh;

        // Solda do pilar na placa com os esforços do pé do pilar; perna digitada ou, com 0, a mínima da tabela.
        ResSolda SoldaPilar(ResPlaca r)
        {
            if (!r.TemEsforcos || r.Sec == null) return null;
            double h = edit.Solda > 0 ? edit.Solda : Solda.Minima(r.Sec, r.Deitado, r.NBarra, r.VBarra, r.MBarra, edit.Eletrodo, edit.Esp);
            if (h <= 0) h = 25;
            return Solda.Calcular(r.Sec, r.Deitado, r.NBarra, r.VBarra, r.MBarra, h, edit.Eletrodo, edit.Esp);
        }

        static string NomeCurtoCh(Chumbador c)
        {
            if (c == null) return "—";
            return c.Linha.Replace("Walsywa ", "") + " " + c.Pol + " hef " + c.Hef.ToString("0");
        }

        static string FmtEsp(double e)
        {
            if (Math.Abs(e - Math.Round(e)) < 1e-6) return Num.Fmt(e, 0);
            if (Math.Abs(e * 10 - Math.Round(e * 10)) < 1e-6) return Num.Fmt(e, 1);
            return Num.Fmt(e, 2);
        }

        // Dimensiona segundo a preferência ativa ("Mais leve" ou "Furo menor").
        void Dimensionar()
        {
            ResPlaca r = PlacaCalc.Calcular(m, n);
            if (!r.TemEsforcos) return;
            bool achou = false;
            if (r.Ch != null || !string.IsNullOrEmpty(edit.Chumbador))
            {
                if (!prefFuroMenor)
                {
                    string esc = PlacaCalc.DimensionarChumbador(m, n);
                    achou = esc != "";
                    msgDim = achou ? "Escolhido automaticamente: " + esc + "." : "Nenhum chumbador do catálogo passa com até 8 chumbadores nesta placa: aumente a placa (sobra) ou use chumbador químico/concretado.";
                }
                else
                {
                    // Furo menor: prioriza o menor diâmetro de furo/rosca que passa
                    string ch0 = edit.Chumbador; int f0 = edit.Fileiras, q0 = edit.PorFileira;
                    var cands = Chumbadores.Lista.OrderBy(c => c.Furo).ThenBy(c => c.Rosca).ThenBy(c => c.Hef).ToList();
                    foreach (Chumbador c in cands)
                    {
                        foreach (int[] arr in new[] { new[] { 2, 2 }, new[] { 2, 3 }, new[] { 3, 3 } })
                        {
                            if (arr[0] * arr[1] < f0 * q0) continue;
                            edit.Chumbador = c.Nome; edit.Fileiras = Math.Max(arr[0], f0); edit.PorFileira = Math.Max(arr[1], q0);
                            ResPlaca t = PlacaCalc.Calcular(m, n);
                            if (t.Erro == "" && t.TemEsforcos && t.ChOk)
                            {
                                achou = true;
                                msgDim = "Escolhido automaticamente (furo menor): " + t.Paraf.Count + " × " + c.Nome + " (" + c.Fabricante + ").";
                                break;
                            }
                        }
                        if (achou) break;
                    }
                    if (!achou) { edit.Chumbador = ch0; edit.Fileiras = f0; edit.PorFileira = q0; msgDim = "Nenhum chumbador do catálogo passa nesta placa."; }
                }
            }
            else
            {
                int ic0 = Math.Max(0, Array.IndexOf(Parafusos.Classes, edit.Classe));
                if (!prefFuroMenor)
                {
                    for (int ic = ic0; ic < Parafusos.Classes.Length && !achou; ic++)
                        for (int id = 0; id < Parafusos.D.Length && !achou; id++)
                        {
                            edit.Classe = Parafusos.Classes[ic]; edit.Diametro = Parafusos.D[id];
                            ResPlaca t = PlacaCalc.Calcular(m, n);
                            if (t.Erro == "" && t.ChOk) achou = true;
                        }
                }
                else
                {
                    // Furo menor: menor diâmetro primeiro, subindo a classe se necessário
                    for (int id = 0; id < Parafusos.D.Length && !achou; id++)
                        for (int ic = ic0; ic < Parafusos.Classes.Length && !achou; ic++)
                        {
                            edit.Classe = Parafusos.Classes[ic]; edit.Diametro = Parafusos.D[id];
                            ResPlaca t = PlacaCalc.Calcular(m, n);
                            if (t.Erro == "" && t.ChOk) achou = true;
                        }
                }
            }
            r = PlacaCalc.Calcular(m, n);
            double ch = PlacaCalc.Chapas.FirstOrDefault(e => e >= r.EspNec - 1e-9);
            if (ch > 0) edit.Esp = ch;
            if (r.Sec != null) { double h = Solda.Minima(r.Sec, r.Deitado, r.NBarra, r.VBarra, r.MBarra, edit.Eletrodo, edit.Esp); edit.Solda = h > 0 ? h : 25; }
        }

        public JanelaPlaca(Modelo m, No n)
        {
            this.m = m; this.n = n;
            original = n.Placa;
            edit = n.Placa != null ? n.Placa.Copiar() : new PlacaBase { PeloPerfil = true, Sobra = 100, H = 400, B = 300, Esp = 19, Fileiras = 2, PorFileira = 2, Borda = 50, Diametro = 16, Classe = "8.8", FyChapa = 250 };
            n.Placa = edit;
            pe = new PlacaEdit(edit);
            chumbAntes = original != null && !string.IsNullOrEmpty(original.Chumbador) ? original.Chumbador : "";
            if (original == null) Dimensionar();
            refInicial = (original ?? edit).Copiar();

            Title = "Placa de base — nó " + n.Id; Width = 1240; Height = 860; MinWidth = 960; MinHeight = 620;
            plano.AoEditar = Recalcular;

            var camadaRaiz = new Grid();
            var raiz = new DockPanel();

            // 3) Rodapé
            var rod = new Border
            {
                Background = BPainel,
                BorderBrush = Pincel(0x22, 0x29, 0x33),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(16, 10, 16, 10)
            };
            var dpRod = new DockPanel();
            var btsDir = new StackPanel { Orientation = Orientation.Horizontal };
            DockPanel.SetDock(btsDir, Dock.Right);
            if (original != null) btsDir.Children.Add(B("Remover placa", () => { n.Placa = null; Mudou = true; Close(); }));
            btsDir.Children.Add(B("Cancelar", () => { n.Placa = original; Close(); }));
            btsDir.Children.Add(B("Aplicar", () => { AplicarEm.Clear(); AplicarEm.AddRange(Candidatos.Where(x => marcados.Contains(x.Id))); Mudou = true; Close(); }, true));
            dpRod.Children.Add(btsDir);
            dpRod.Children.Add(rodPref);
            rod.Child = dpRod;
            DockPanel.SetDock(rod, Dock.Bottom);
            raiz.Children.Add(rod);

            // Corpo principal: Esquerda (larga) + Direita (410 px)
            var corpo = new Grid();
            corpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            corpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(410) });

            // 1) Esquerda
            var esq = new DockPanel { Margin = new Thickness(16, 14, 16, 14) };
            DockPanel.SetDock(faixaTopo, Dock.Top);
            esq.Children.Add(faixaTopo);

            var cardDesenho = new Border
            {
                Background = BPainel,
                BorderBrush = Pincel(0x4A, 0x54, 0x63),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6)
            };
            var dpDesenho = new DockPanel();
            var topoDesenho = new DockPanel { Margin = new Thickness(14, 10, 14, 6) };
            btsAbas = new StackPanel { Orientation = Orientation.Horizontal };
            DockPanel.SetDock(btsAbas, Dock.Right);
            topoDesenho.Children.Add(btsAbas);
            lblSubAba = T("Vista de cima · clique numa cota azul para editar", 12, BFraco);
            lblSubAba.VerticalAlignment = VerticalAlignment.Center;
            topoDesenho.Children.Add(lblSubAba);
            DockPanel.SetDock(topoDesenho, Dock.Top);
            dpDesenho.Children.Add(topoDesenho);
            dpDesenho.Children.Add(areaAba);
            cardDesenho.Child = dpDesenho;
            esq.Children.Add(cardDesenho);
            Grid.SetColumn(esq, 0);
            corpo.Children.Add(esq);

            // 2) Direita (410 px, fundo BPainel)
            var dirBorder = new Border
            {
                Background = BPainel,
                BorderBrush = Pincel(0x22, 0x29, 0x33),
                BorderThickness = new Thickness(1, 0, 0, 0)
            };
            var svDir = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(16, 14, 16, 14),
                Content = colDir
            };
            dirBorder.Child = svDir;
            Grid.SetColumn(dirBorder, 1);
            corpo.Children.Add(dirBorder);

            raiz.Children.Add(corpo);
            camadaRaiz.Children.Add(raiz);
            MontarOverlayTrocar();
            camadaRaiz.Children.Add(overlayTrocar);
            Content = camadaRaiz;

            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape && overlayTrocar.Visibility == Visibility.Visible)
                {
                    overlayTrocar.Visibility = Visibility.Collapsed;
                    e.Handled = true;
                }
            };
            Closing += (s, e) => { if (!Mudou) n.Placa = original; };
            Loaded += (s, e) => Recalcular();   // Candidatos/AvaliarEm chegam depois do construtor
            Recalcular();
        }

        // Pílula clicável no estilo do v3.html (.pill / .pill.on)
        static readonly Dictionary<string, string> DicasPlaca = new Dictionary<string, string>
        {
            { "Medidas", "Vista de cima da placa com as cotas. Clique numa cota azul para mudar a medida." },
            { "Forças nos furos", "Tração (T) e corte (V) em cada furo na combinação crítica, com os esforços no pé do pilar." },
            { "Ficha de fabricação", "Tudo o que a oficina precisa: chapa, furos, chumbadores, torque e solda." },
            { "Cálculo", "Todas as verificações com as fórmulas e os valores." },
            { "Chumbador", "Fixação por chumbador de catálogo (mecânico ou químico) instalado no concreto pronto." },
            { "Parafuso", "Fixação por parafuso métrico, verificado pela classe do aço (Sp)." },
            { "Mais leve", "Ao dimensionar, prefere a menor fixação e a chapa mais fina que passam." },
            { "Furo menor", "Ao dimensionar, prefere o menor furo no concreto (broca menor)." },
            { "2 × 2", "2 fileiras × 2 fixações = 4 no contorno da placa." },
            { "2 × 3", "2 fileiras × 3 fixações = 6 no contorno da placa." },
            { "3 × 3", "3 fileiras × 3 fixações = 8 no contorno (o centro fica livre)." },
            { "outro…", "Digitar outra quantidade de fileiras e de fixações por fileira." },
            { "Todos", "Mostrar chumbadores mecânicos e químicos." },
            { "Mecânico", "Chumbadores de expansão: instalação imediata." },
            { "Químico", "Chumbadores com resina: mais carga, precisa de cura." },
            { "Dimensionar novamente", "Escolhe sozinho a menor fixação, a chapa e a solda que passam neste nó, segundo a preferência ao lado." },
            { "Dimensionar pelo pior caso", "Escolhe a menor fixação e chapa que passam em todos os nós escolhidos, cada um na sua pior combinação." },
            { "Remover placa", "Tira a placa de base deste nó." },
            { "Aplicar", "Grava esta placa no nó (e nos nós marcados em Outras fixações)." },
            { "Cancelar", "Fecha sem mudar nada." },
        };

        static Border Pilula(string texto, bool ativo, Action clique)
        {
            var b = new Border
            {
                Background = ativo ? BAzul : Brushes.Transparent,
                BorderBrush = ativo ? BAzul : BBorda,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(11, 3, 11, 3),
                Margin = new Thickness(0, 0, 5, 0),
                Cursor = Cursors.Hand,
                Child = new TextBlock { Text = texto, FontSize = 12, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center }
            };
            b.MouseLeftButtonDown += (s, e) => { clique(); e.Handled = true; };
            if (DicasPlaca.TryGetValue(texto, out string dica)) Dica(b, dica);
            return b;
        }

        // Stepper [ − | valor | + ] no estilo do v3.html (.step)
        static Border Stepper(string valor, Action menos, Action mais, double largMin = 84, Action cliqueCentro = null)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            Border Botao(string s, int col, Action acao)
            {
                var bt = new Border
                {
                    Background = Pincel(0x4E, 0x5A, 0x6E),
                    Cursor = acao != null ? Cursors.Hand : Cursors.Arrow,
                    Opacity = acao != null ? 1.0 : 0.45,
                    Child = new TextBlock { Text = s, FontSize = 16, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                };
                if (acao != null) bt.MouseLeftButtonDown += (s2, e) => { acao(); e.Handled = true; };
                Grid.SetColumn(bt, col);
                return bt;
            }
            g.Children.Add(Botao("−", 0, menos));
            var centro = new Border
            {
                Background = BPainel,
                MinWidth = largMin,
                Padding = new Thickness(10, 5, 10, 5),
                Cursor = cliqueCentro != null ? Cursors.Hand : Cursors.Arrow,
                Child = new TextBlock { Text = valor, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = BTexto, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            if (cliqueCentro != null) centro.MouseLeftButtonDown += (s2, e) => { cliqueCentro(); e.Handled = true; };
            Grid.SetColumn(centro, 1);
            g.Children.Add(centro);
            g.Children.Add(Botao("+", 2, mais));
            return new Border
            {
                BorderBrush = BBorda,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Child = g,
                HorizontalAlignment = HorizontalAlignment.Left
            };
        }

        // Barra "Capacidade usada" (.bar do v3.html)
        static StackPanel BarraCapacidade(double ap, bool ok)
        {
            if (double.IsNaN(ap) || double.IsInfinity(ap)) ap = 0;
            var sp = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            var dp = new DockPanel();
            Brush cor = !ok || ap > 1 ? BVermelho : ap > 0.9 ? BAmarelo : BVerde;
            var pct = T((ap * 100).ToString("0") + "%", 12, cor, FontWeights.SemiBold);
            DockPanel.SetDock(pct, Dock.Right);
            dp.Children.Add(pct);
            dp.Children.Add(T("Capacidade usada", 12, BFraco));
            sp.Children.Add(dp);
            var trilho = new Grid { Height = 6, Margin = new Thickness(0, 4, 0, 0) };
            trilho.Children.Add(new Border { Background = BCinza, CornerRadius = new CornerRadius(3) });
            double f = Math.Max(0, Math.Min(1, ap));
            if (f > 0)
            {
                trilho.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.02, f), GridUnitType.Star) });
                trilho.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0, 1 - f), GridUnitType.Star) });
                Grid.SetColumnSpan(trilho.Children[0], 2);
                trilho.Children.Add(new Border { Background = cor, CornerRadius = new CornerRadius(3) });
            }
            sp.Children.Add(trilho);
            return sp;
        }

        // Cabeçalho de cartão da direita: selo redondo ✓/✕ + título em MAIÚSCULAS + conteúdo opcional à direita
        static DockPanel CabecalhoCartao(string titulo, bool ok, UIElement direita = null)
        {
            var dp = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            if (direita != null) { DockPanel.SetDock(direita, Dock.Right); dp.Children.Add(direita); }
            var esq = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            esq.Children.Add(new Border
            {
                Width = 16, Height = 16, CornerRadius = new CornerRadius(8),
                Background = ok ? BOk : BRuim,
                Margin = new Thickness(0, 0, 6, 0),
                Child = new TextBlock { Text = ok ? "✓" : "✕", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = ok ? BVerde : BVermelho, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            });
            esq.Children.Add(new TextBlock { Text = titulo.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = BFraco, VerticalAlignment = VerticalAlignment.Center });
            dp.Children.Add(esq);
            return dp;
        }

        static Border CartaoDireita(UIElement conteudo)
        {
            return new Border
            {
                Background = BFundo,
                BorderBrush = BCinza,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 12),
                Child = conteudo
            };
        }

        // A espessura necessária já inclui n e a flexão cresce com t², então a segurança real de uma chapa t é n·(t/tnec)²
        string SegChapa(ResPlaca r, double t)
        {
            if (r.EspNec <= 0) return "∞";
            return Num.Fmt(r.NSeg * (t / r.EspNec) * (t / r.EspNec), 2);
        }

        // Ficha de fabricação: tudo o que precisa para cortar, furar, soldar e apertar
        FrameworkElement Ficha(ResPlaca r, ResSolda sd)
        {
            var gr = new Grid { Margin = new Thickness(14, 6, 14, 14) };
            gr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            gr.ColumnDefinitions.Add(new ColumnDefinition());
            Action<string, string, bool> lin = (k, v, titulo) =>
            {
                gr.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var a = T(k, titulo ? 12.5 : 12, titulo ? BAzul : BFraco, titulo ? FontWeights.SemiBold : FontWeights.Normal);
                a.Margin = new Thickness(0, titulo ? 8 : 1, 8, 1);
                Grid.SetRow(a, gr.RowDefinitions.Count - 1); gr.Children.Add(a);
                if (v == null) { Grid.SetColumnSpan(a, 2); return; }
                var b = T(v, 12, BTexto); b.Margin = new Thickness(0, 1, 0, 1); b.TextWrapping = TextWrapping.Wrap;
                Grid.SetRow(b, gr.RowDefinitions.Count - 1); Grid.SetColumn(b, 1); gr.Children.Add(b);
            };
            lin("FICHA DA PLACA", null, true);
            lin("Esforços no pé", "N = " + Num.Fmt(r.N, 2) + " kN · V = " + Num.Fmt(r.V, 2) + " kN · M = " + Num.Fmt(r.M, 2) + " kN·m", false);
            lin("Coef. de segurança", "n = " + Num.Fmt(r.NSeg, 2) + (edit.Seguranca > 0 ? " (desta placa)" : " (aba Análise)"), false);
            lin("Chapa", null, true);
            lin("Dimensões", Num.Fmt(r.H, 0) + " × " + Num.Fmt(r.B, 0) + " × " + Num.Fmt(edit.Esp, 2) + " mm (no plano × fora × espessura)", false);
            lin("Aço / fy", Num.Fmt(r.FyChapa, 0) + " MPa", false);
            lin("Massa", Num.Fmt(r.H * r.B * edit.Esp * 7850e-9, 2) + " kg", false);
            lin("Espessura necessária", Num.Fmt(r.EspNec, 1) + " mm · segurança " + SegChapa(r, edit.Esp), false);
            lin("Parafusos / chumbadores", null, true);
            double furo = r.Dia + (r.Dia <= 24 ? 2 : 3);
            if (r.Ch != null)
            {
                lin("Quantidade", r.Paraf.Count + " × chumbador " + r.Ch.Nome + " (" + r.Ch.Fabricante + ")" + (r.Ch.Quimico ? " · barra classe " + r.ClasseBarra : ""), false);
                lin("Furo no concreto", "Ø" + Num.Fmt(r.Ch.Furo, 1) + " mm × " + Num.Fmt(r.Ch.Hef, 0) + " mm de embutimento", false);
                lin("Cargas do catálogo", "tração ref. " + Num.Fmt(r.Tref, 1) + " kN · corte ref. " + (r.Vref > 0 ? Num.Fmt(r.Vref, 1) + " kN" : "no aço da barra") + " · concreto ensaio " + Num.Fmt(r.Ch.Fck, 0) + " MPa (laje " + Num.Fmt(r.FckLaje, 0) + " MPa)", false);
                lin("Distâncias", "entre chumbadores: " + Num.Fmt(r.EspacMin, 0) + " mm na placa (sem redução a partir de 3·hef = " + Num.Fmt(r.Ch.S > 0 ? r.Ch.S : 3 * r.Ch.Hef, 0) + " mm; mínimo para instalar 6·d = " + Num.Fmt(Chumbadores.EspacMinimo(r.Ch), 0) + " mm) · até a borda do concreto ≥ " + Num.Fmt(r.Ch.C > 0 ? r.Ch.C : 1.5 * r.Ch.Hef, 0) + " mm (conferir na obra)", false);
                lin("Coef. do chumbador", "n = " + Num.Fmt(r.NRef, 1) + (r.Ch.Tu > 0 ? " (aplicado à carga última)" : " (carga recomendada do fabricante)") + (r.Ch.Quimico ? " · aço da barra n = " + Num.Fmt(r.NSeg, 1) : ""), false);
            }
            else lin("Quantidade", r.Paraf.Count + " × M" + Num.Fmt(edit.Diametro, 0) + " classe " + edit.Classe + (edit.RoscaNoCorte ? " (rosca no plano de corte)" : ""), false);
            lin("Furo na chapa", "Ø" + Num.Fmt(furo, 0) + " mm (folga padrão)", false);
            lin("Arranjo", edit.Fileiras + " fileira(s) × " + edit.PorFileira + " · borda " + Num.Fmt(edit.Borda, 0) + " mm até o centro do furo", false);
            lin("Posições (y; z) mm", string.Join("   ", r.Paraf.Select(p => "(" + Num.Fmt(p[0], 0) + "; " + Num.Fmt(p[1], 0) + ")")), false);
            lin("Maior tração", Num.Fmt(r.Tmax, 2) + " kN · corte " + Num.Fmt(r.Fv, 2) + " kN por parafuso", false);
            lin("Aperto", r.Ch != null ? (r.Torque > 0 ? "torque " + Num.Fmt(r.Ch.Torque, 1) + " kgf·m (" + Num.Fmt(r.Torque, 0) + " N·m), do catálogo" : "conforme o fabricante (catálogo não informa)")
                                       : "pré-carga " + Num.Fmt(r.Fi, 1) + " kN · torque " + Num.Fmt(r.Torque, 0) + " N·m", false);
            lin("Segurança", double.IsInfinity(r.NComb) ? "∞" : Num.Fmt(r.NComb, 2), false);
            if (sd != null && sd.Erro == "")
            {
                lin("Solda pilar–placa", null, true);
                lin("Tipo", "filete contínuo em todo o contorno do perfil (" + sd.Grupo + ")", false);
                lin("Eletrodo", (string.IsNullOrEmpty(edit.Eletrodo) ? "E70" : edit.Eletrodo) + "XX · τ adm " + Num.Fmt(sd.TauAdm, 0) + " MPa", false);
                lin("Perna / garganta", Num.Fmt(sd.H, 0) + " mm / " + Num.Fmt(0.707 * sd.H, 1) + " mm" + (sd.HMin > 0 ? " · perna mínima " + Num.Fmt(sd.HMin, 0) + " mm" : ""), false);
                lin("Comprimento total", Num.Fmt(sd.L, 0) + " mm de cordão", false);
                lin("Comprimento necessário", Num.Fmt(sd.LNec, 0) + " mm com esta perna (" + (sd.Ap * 100).ToString("0") + "% do contorno)", false);
                lin("Tensão", "τ = " + Num.Fmt(sd.Tau, 0) + " MPa ≤ " + Num.Fmt(sd.TauAdm, 0) + " MPa", false);
                lin("Metal depositado", "≈ " + Num.Fmt(sd.Massa, 2) + " kg", false);
            }
            return new ScrollViewer { Content = gr, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        string TextoDetalheFixacao(ResPlaca r)
        {
            if (!r.TemEsforcos) return r.Erro;
            if (r.Ch != null)
            {
                string nTxt = double.IsInfinity(r.NComb) ? "∞" : Num.Fmt(r.NComb, 2);
                string porque = r.Ch.Tu > 0 ? "carga " + Chumbadores.NomesTipos[r.TipoCarga] + ", " + (r.RiscoAlto ? "alto" : "baixo") + " risco" + (edit.CoefChumbador > 0 ? ", digitado" : ", tabela Walsywa") : "carga recomendada do fabricante";
                string acoTxt = r.Ch.Quimico ? "\nAço da barra (classe " + r.ClasseBarra + ", Sp = " + Num.Fmt(r.SpBarra, 0) + " MPa): σ = " + Num.Fmt(r.Sigma, 0) + " MPa, τ = " + Num.Fmt(r.Tau, 0) + " MPa · segurança " +
                    (double.IsInfinity(r.NAco) ? "∞" : Num.Fmt(r.NAco, 2)) + (r.NAco >= r.NSeg ? " ≥ " : " < ") + Num.Fmt(r.NSeg, 1) + " exigida." : "";
                return "Tração: " + Num.Fmt(r.Tmax, 2) + " kN. Resiste " + Num.Fmt(r.Tref, 1) + " kN (concreto laje " + Num.Fmt(r.FckLaje, 0) + " MPa / ensaio " + Num.Fmt(r.Ch.Fck, 0) + " MPa" +
                    (r.FFck < 0.999 ? ", f_fck = " + Num.Fmt(r.FFck, 2) : "") +
                    (r.PsiCone < 0.999 ? ", ψ = " + Num.Fmt(r.PsiCone, 2) + " por cones sobrepostos a " + Num.Fmt(r.EspacMin, 0) + " mm" : "") + ").\n" +
                    "Corte: " + Num.Fmt(r.Fv, 2) + " kN. " + (r.Vref > 0 ? "Resiste " + Num.Fmt(r.Vref, 1) + " kN." : "Corte verificado no aço da barra.") + "\n" +
                    "Segurança no concreto " + nTxt + (r.NComb >= r.NRef ? " ≥ " : " < ") + Num.Fmt(r.NRef, 1) + " exigida (" + porque + ")." + acoTxt +
                    (r.EspacOk ? "" : "\n✗ Chumbadores a " + Num.Fmt(r.EspacMin, 0) + " mm: menos que o mínimo para instalar (6·d = " + Num.Fmt(Chumbadores.EspacMinimo(r.Ch), 0) + " mm).") +
                    (string.IsNullOrEmpty(msgDim) ? "" : "\n" + msgDim);
            }
            return "Tração " + Num.Fmt(r.Tmax, 2) + " kN (σ = " + Num.Fmt(r.Sigma, 0) + " MPa) · corte " + Num.Fmt(r.Fv, 2) + " kN (τ = " + Num.Fmt(r.Tau, 0) + " MPa) · segurança Sp/√(σ²+3τ²) = " +
                (double.IsInfinity(r.NComb) ? "∞" : Num.Fmt(r.NComb, 2)) + (r.NComb >= r.NExig ? " ≥ " : " < ") + Num.Fmt(r.NExig, 2) + " · pré-carga " + Num.Fmt(r.Fi, 1) + " kN, torque " + Num.Fmt(r.Torque, 0) + " N·m";
        }

        string TextoDetalheChapa(ResPlaca r)
        {
            return r.EspNec > 0
                ? "Segurança " + SegChapa(r, edit.Esp) + (r.EspNec <= edit.Esp ? " ≥ " : " < ") + Num.Fmt(r.NSeg, 2) + " · necessária " + Num.Fmt(r.EspNec, 1) + " mm (fileira com " + Num.Fmt(r.TLinha, 1) + " kN, braço " + Num.Fmt(r.Braco, 0) + " mm, largura " + Num.Fmt(r.LargEf, 0) + " mm, fy " + Num.Fmt(r.FyChapa, 0) + " MPa)"
                : "Sem tração nos parafusos: a chapa não trabalha à flexão neste modelo.";
        }

        string TextoDetalheSolda(ResSolda sd)
        {
            if (sd == null) return "Sem esforços.";
            if (sd.Erro != "") return sd.Erro;
            return sd.Grupo + " · τ = √(τ'² + τ''²) = " + Num.Fmt(sd.Tau, 0) + " MPa ≤ " + Num.Fmt(sd.TauAdm, 0) + " MPa · perna mínima " + (sd.HMin > 0 ? Num.Fmt(sd.HMin, 0) + " mm" : "> 25 mm")
                + " · cordão total " + Num.Fmt(sd.L, 0) + " mm, necessário " + Num.Fmt(sd.LNec, 0) + " mm"
                + (sd.Espessura > 0 && sd.H > sd.Espessura ? " · ⚠ perna maior que a parede do perfil (" + Num.Fmt(sd.Espessura, 1) + " mm)" : "")
                + (sd.AbaixoMinimo ? " · ⚠ perna abaixo do mínimo de norma (" + Num.Fmt(sd.HMinNorma, 0) + " mm)" : "");
        }

        FrameworkElement AbaCalculo(ResPlaca r, ResSolda sd)
        {
            var sp = new StackPanel { Margin = new Thickness(16, 8, 16, 16) };
            if (!r.TemEsforcos) { sp.Children.Add(T(r.Erro, 13, BFraco)); return new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; }
            sp.Children.Add(T("Esforços no pé do pilar: N = " + Num.Fmt(r.N, 2) + " kN (" + (r.N >= 0 ? "tração" : "compressão") + ") · V = " + Num.Fmt(r.V, 2) + " kN · M = " + Num.Fmt(r.M, 2) + " kN·m", 12, BTexto, FontWeights.SemiBold));
            sp.Children.Add(T(r.Modelo, 11.5, BFraco));
            sp.Children.Add(new Border { Height = 12 });
            sp.Children.Add(Medidor(r.Ch != null ? "Chumbador mais carregado — " + r.Ch.Nome + " (" + r.Ch.Fabricante + ")" : "Parafuso mais solicitado", r.ApFix, TextoDetalheFixacao(r)));
            if (r.Aviso != "") { var av = T("⚠ " + r.Aviso, 12, BAmarelo); av.Margin = new Thickness(0, 0, 0, 12); sp.Children.Add(av); }
            sp.Children.Add(Medidor("Espessura da chapa", edit.Esp > 0 ? r.EspNec / edit.Esp : 0, TextoDetalheChapa(r)));
            if (sd != null)
                sp.Children.Add(Medidor("Solda pilar–placa (filete " + Num.Fmt(sd.H, 0) + " mm, " + (string.IsNullOrEmpty(edit.Eletrodo) ? "E70" : edit.Eletrodo) + "XX)", sd.Ap, TextoDetalheSolda(sd)));
            sp.Children.Add(T(r.Ch != null ? "Cargas de catálogo valem para o concreto indicado e as distâncias mínimas do fabricante; não verifica pressão no concreto." : "Não verifica pressão no concreto nem ancoragem.", 11, BFraco));
            return new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        // Overlay "Trocar chumbador" à direita com fundo escurecido
        void MontarOverlayTrocar()
        {
            var sombra = new Border { Background = new SolidColorBrush(Color.FromArgb(145, 10, 12, 16)) };
            sombra.MouseLeftButtonDown += (s, e) => overlayTrocar.Visibility = Visibility.Collapsed;
            overlayTrocar.Children.Add(sombra);

            var painel = new Border
            {
                Width = 560,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 24, 18, 24),
                Background = BPainel,
                BorderBrush = BBorda,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6)
            };
            var dp = new DockPanel();
            var cabOv = new StackPanel { Margin = new Thickness(16, 14, 16, 12) };
            var linTit = new DockPanel();
            var btFechar = T("✕", 15, BFraco); btFechar.Cursor = Cursors.Hand;
            btFechar.MouseLeftButtonDown += (s, e) => overlayTrocar.Visibility = Visibility.Collapsed;
            DockPanel.SetDock(btFechar, Dock.Right);
            linTit.Children.Add(btFechar);
            linTit.Children.Add(T("Trocar chumbador", 16, BTexto, FontWeights.SemiBold));
            cabOv.Children.Add(linTit);

            // Busca por texto
            var boxBusca = new Grid { Margin = new Thickness(0, 10, 0, 10) };
            var tbBusca = new TextBox
            {
                Background = BFundo, Foreground = BTexto, BorderBrush = BBorda, CaretBrush = BTexto,
                Padding = new Thickness(8, 6, 8, 6), FontSize = 12.5
            };
            var dicaBusca = T("🔍 Buscar modelo, fabricante ou diâmetro…", 12, BFraco);
            dicaBusca.Margin = new Thickness(10, 6, 0, 0); dicaBusca.IsHitTestVisible = false;
            tbBusca.TextChanged += (s, e) =>
            {
                filtroBuscaCh = tbBusca.Text ?? "";
                dicaBusca.Visibility = string.IsNullOrEmpty(filtroBuscaCh) ? Visibility.Visible : Visibility.Collapsed;
                PreencherOverlayTrocar();
            };
            boxBusca.Children.Add(tbBusca);
            boxBusca.Children.Add(dicaBusca);
            cabOv.Children.Add(boxBusca);

            // Pílulas Mecânico / Químico
            btsFiltroTipoCh = new StackPanel { Orientation = Orientation.Horizontal };
            cabOv.Children.Add(btsFiltroTipoCh);

            var bdCab = new Border { BorderBrush = BFundo, BorderThickness = new Thickness(0, 0, 0, 1), Child = cabOv };
            DockPanel.SetDock(bdCab, Dock.Top);
            dp.Children.Add(bdCab);

            var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = listaTrocar };
            dp.Children.Add(sv);
            painel.Child = dp;
            overlayTrocar.Children.Add(painel);
        }

        void AbrirOverlayTrocar()
        {
            if (string.IsNullOrEmpty(chumbAntes) && !string.IsNullOrEmpty(edit.Chumbador)) chumbAntes = edit.Chumbador;
            verTodosNaoServem = false;
            PreencherOverlayTrocar();
            overlayTrocar.Visibility = Visibility.Visible;
        }

        void PreencherOverlayTrocar()
        {
            btsFiltroTipoCh.Children.Clear();
            btsFiltroTipoCh.Children.Add(Pilula("Todos", filtroTipoCh == 0, () => { filtroTipoCh = 0; PreencherOverlayTrocar(); }));
            btsFiltroTipoCh.Children.Add(Pilula("Mecânico", filtroTipoCh == 1, () => { filtroTipoCh = filtroTipoCh == 1 ? 0 : 1; PreencherOverlayTrocar(); }));
            btsFiltroTipoCh.Children.Add(Pilula("Químico", filtroTipoCh == 2, () => { filtroTipoCh = filtroTipoCh == 2 ? 0 : 2; PreencherOverlayTrocar(); }));

            listaTrocar.Children.Clear();
            ResPlaca r = PlacaCalc.Calcular(m, n);
            string q = (filtroBuscaCh ?? "").Trim().ToLowerInvariant();

            var todos = Chumbadores.Lista.Where(c =>
            {
                if (filtroTipoCh == 1 && c.Quimico) return false;
                if (filtroTipoCh == 2 && !c.Quimico) return false;
                if (q != "" && !(c.Nome.ToLowerInvariant().Contains(q) || c.Fabricante.ToLowerInvariant().Contains(q) || c.Pol.ToLowerInvariant().Contains(q) || c.Codigo.ToLowerInvariant().Contains(q)))
                    return false;
                return true;
            }).Select(c =>
            {
                ResPlaca tc = PlacaCalc.CandidatoChumbador(r, edit, m, c);
                return new { Ch = c, Res = tc, Ok = tc.ChOk };
            }).ToList();

            var passam = todos.Where(x => x.Ok).OrderBy(x => x.Ch.Rosca).ThenBy(x => x.Ch.Hef).ThenBy(x => x.Res.ApFix).ToList();
            var recom = passam.Take(3).ToList();
            var maiores = passam.Skip(3).ToList();
            var naoServem = todos.Where(x => !x.Ok).OrderBy(x => x.Ch.Rosca).ThenBy(x => x.Ch.Hef).ToList();

            void TituloGrupo(string s)
            {
                var t = T(s.ToUpperInvariant(), 11, BFraco, FontWeights.SemiBold);
                t.Margin = new Thickness(16, 12, 16, 4);
                listaTrocar.Children.Add(t);
            }

            void LinhaCandidato(Chumbador c, ResPlaca tc, bool ok)
            {
                bool emUso = r.Ch == c;
                bool eraAntes = !emUso && !string.IsNullOrEmpty(chumbAntes) && (c.Nome == chumbAntes || c.Codigo == chumbAntes);
                var bd = new Border
                {
                    Background = emUso ? Pincel(0x36, 0x50, 0x6A) : Brushes.Transparent,
                    BorderBrush = BFundo,
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Padding = new Thickness(16, 10, 16, 10),
                    Cursor = Cursors.Hand
                };
                var dp = new DockPanel();
                var dir = T(emUso ? "✓ em uso" : "Usar", 12, emUso ? BVerde : Pincel(0x3F, 0xB6, 0xEE), FontWeights.SemiBold);
                dir.VerticalAlignment = VerticalAlignment.Center;
                dir.Margin = new Thickness(12, 0, 0, 0);
                DockPanel.SetDock(dir, Dock.Right);
                dp.Children.Add(dir);

                var diam = T(c.Pol, 15, ok ? BTexto : Pincel(0x7A, 0x84, 0x94), FontWeights.SemiBold);
                diam.Width = 48; diam.VerticalAlignment = VerticalAlignment.Center;
                DockPanel.SetDock(diam, Dock.Left);
                dp.Children.Add(diam);

                var meio = new StackPanel();
                var lin1 = new WrapPanel { Orientation = Orientation.Horizontal };
                string modeloCurto = c.Linha.Replace("Walsywa ", "") + " hef " + c.Hef.ToString("0");
                lin1.Children.Add(T(modeloCurto, 12.5, ok ? BTexto : Pincel(0x9A, 0xA4, 0xB4), FontWeights.SemiBold));
                string furoTxt = "  " + c.Fabricante + " · " + (c.Quimico ? "químico" : "mecânico") + " · furo Ø" + Num.Fmt(c.Furo, c.Furo == Math.Floor(c.Furo) ? 0 : 1) + " × " + Num.Fmt(c.Hef, 0);
                lin1.Children.Add(T(furoTxt, 12, BFraco));
                if (eraAntes) lin1.Children.Add(T(" · antes", 12, BAmarelo, FontWeights.SemiBold));

                if (!ok)
                {
                    string motivo;
                    if (!tc.EspacOk) motivo = " · não cabe (espaçamento " + Num.Fmt(tc.EspacMin, 0) + " < mín. " + Num.Fmt(Chumbadores.EspacMinimo(c), 0) + " mm)";
                    else if (c.Quimico && tc.NAco < tc.NSeg && tc.NComb >= tc.NRef) motivo = " · aço da barra: segurança " + Num.Fmt(tc.NAco, 1) + " < " + Num.Fmt(tc.NSeg, 1) + " exigida";
                    else motivo = " · segurança " + (double.IsInfinity(tc.NComb) ? "∞" : Num.Fmt(tc.NComb, 1)) + " < " + Num.Fmt(tc.NRef, 1) + " exigida";
                    lin1.Children.Add(T(motivo, 12, BVermelho));
                }
                meio.Children.Add(lin1);

                if (ok)
                {
                    var tr = new Grid { Height = 5, Margin = new Thickness(0, 5, 0, 0) };
                    tr.Children.Add(new Border { Background = BCinza, CornerRadius = new CornerRadius(2.5) });
                    double f = Math.Max(0.02, Math.Min(1, double.IsNaN(tc.ApFix) || double.IsInfinity(tc.ApFix) ? 1 : tc.ApFix));
                    tr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(f, GridUnitType.Star) });
                    tr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0, 1 - f), GridUnitType.Star) });
                    Grid.SetColumnSpan(tr.Children[0], 2);
                    tr.Children.Add(new Border { Background = BVerde, CornerRadius = new CornerRadius(2.5) });
                    meio.Children.Add(tr);
                }

                dp.Children.Add(meio);
                bd.Child = dp;
                bd.MouseLeftButtonDown += (s, e) =>
                {
                    edit.Chumbador = c.Nome;
                    overlayTrocar.Visibility = Visibility.Collapsed;
                    Recalcular();
                    e.Handled = true;
                };
                listaTrocar.Children.Add(bd);
            }

            if (recom.Count > 0)
            {
                TituloGrupo("Recomendados");
                foreach (var x in recom) LinhaCandidato(x.Ch, x.Res, true);
            }
            if (maiores.Count > 0)
            {
                TituloGrupo("Maiores (passam com mais folga)");
                foreach (var x in maiores) LinhaCandidato(x.Ch, x.Res, true);
            }
            if (naoServem.Count > 0)
            {
                TituloGrupo("Não servem aqui");
                var mostrar = verTodosNaoServem ? naoServem : naoServem.Take(6).ToList();
                foreach (var x in mostrar) LinhaCandidato(x.Ch, x.Res, false);
                if (!verTodosNaoServem && naoServem.Count > mostrar.Count)
                {
                    var mais = T("+ " + (naoServem.Count - mostrar.Count) + " modelos ocultos · ver todos", 12, Pincel(0x3F, 0xB6, 0xEE));
                    mais.Margin = new Thickness(16, 10, 16, 14); mais.Cursor = Cursors.Hand;
                    mais.MouseLeftButtonDown += (s, e) => { verTodosNaoServem = true; PreencherOverlayTrocar(); e.Handled = true; };
                    listaTrocar.Children.Add(mais);
                }
            }
        }

        // Régua de valores clicáveis (vermelho = não passa, verde = passa, branco = atual)
        static Grid Regua(int count, Func<int, (string rotulo, bool passa, bool atual, string dica, Action clicar)> item)
        {
            var g = new Grid { Margin = new Thickness(0, 8, 0, 2) };
            for (int i = 0; i < count; i++)
            {
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var inf = item(i);
                var sp = new StackPanel { Cursor = Cursors.Hand, Background = Brushes.Transparent, ToolTip = inf.dica, Margin = new Thickness(1.5, 0, 1.5, 0) };
                sp.Children.Add(new Border
                {
                    Height = inf.atual ? 9 : 7,
                    CornerRadius = new CornerRadius(2),
                    Margin = new Thickness(3, inf.atual ? 0 : 1, 3, 3),
                    Background = inf.atual ? Brushes.White : inf.passa ? BVerde : BRuim
                });
                sp.Children.Add(new TextBlock
                {
                    Text = inf.rotulo,
                    FontSize = 10,
                    FontWeight = inf.atual ? FontWeights.Bold : FontWeights.Normal,
                    Foreground = inf.atual ? Brushes.White : inf.passa ? BFraco : Pincel(0x7A, 0x84, 0x94),
                    HorizontalAlignment = HorizontalAlignment.Center
                });
                Action ac = inf.clicar;
                sp.MouseLeftButtonDown += (s, e) => { ac(); e.Handled = true; };
                Grid.SetColumn(sp, i);
                g.Children.Add(sp);
            }
            return g;
        }

        UIElement ConstruirLinhaQuantidade()
        {
            var sp = new StackPanel { Margin = new Thickness(0, 10, 0, 4) };
            var dp = new DockPanel();
            var pils = new WrapPanel { Orientation = Orientation.Horizontal };
            DockPanel.SetDock(pils, Dock.Right);
            bool e22 = edit.Fileiras == 2 && edit.PorFileira == 2;
            bool e23 = edit.Fileiras == 2 && edit.PorFileira == 3;
            bool e33 = edit.Fileiras == 3 && edit.PorFileira == 3;
            bool eOutro = !e22 && !e23 && !e33;
            pils.Children.Add(Pilula("2 × 2", e22, () => { edit.Fileiras = 2; edit.PorFileira = 2; mostrarOutroArranjo = false; Recalcular(); }));
            pils.Children.Add(Pilula("2 × 3", e23, () => { edit.Fileiras = 2; edit.PorFileira = 3; mostrarOutroArranjo = false; Recalcular(); }));
            pils.Children.Add(Pilula("3 × 3", e33, () => { edit.Fileiras = 3; edit.PorFileira = 3; mostrarOutroArranjo = false; Recalcular(); }));
            pils.Children.Add(Pilula(eOutro ? edit.Fileiras + " × " + edit.PorFileira : "outro…", eOutro || mostrarOutroArranjo, () => { mostrarOutroArranjo = !mostrarOutroArranjo; Recalcular(); }));
            dp.Children.Add(pils);
            var lbl = T("Quantidade", 12, BFraco); lbl.VerticalAlignment = VerticalAlignment.Center;
            dp.Children.Add(lbl);
            sp.Children.Add(dp);

            if (mostrarOutroArranjo || eOutro)
            {
                var lin = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 2), HorizontalAlignment = HorizontalAlignment.Right };
                lin.Children.Add(T("Fileiras: ", 11.5, BFraco));
                var tbF = Campo(edit.Fileiras.ToString(), 42);
                lin.Children.Add(tbF);
                lin.Children.Add(T("   Por fileira: ", 11.5, BFraco));
                var tbP = Campo(edit.PorFileira.ToString(), 42);
                lin.Children.Add(tbP);
                Action aplicarArr = () =>
                {
                    int f, q;
                    if (int.TryParse(tbF.Text.Trim(), out f) && f >= 1 && int.TryParse(tbP.Text.Trim(), out q) && q >= 1)
                    {
                        if (edit.Fileiras != f || edit.PorFileira != q) { edit.Fileiras = f; edit.PorFileira = q; Recalcular(); }
                    }
                };
                tbF.LostFocus += (s, e) => aplicarArr(); tbP.LostFocus += (s, e) => aplicarArr();
                tbF.KeyDown += (s, e) => { if (e.Key == Key.Enter) aplicarArr(); };
                tbP.KeyDown += (s, e) => { if (e.Key == Key.Enter) aplicarArr(); };
                sp.Children.Add(lin);
            }
            return sp;
        }

        static Expander CriarExpander(string titulo, bool aberto, Action<bool> aoMudar, UIElement conteudo)
        {
            var exp = new Expander
            {
                Header = T(titulo, 11.5, BFraco),
                Foreground = BFraco,
                IsExpanded = aberto,
                Margin = new Thickness(0, 6, 0, 0),
                Content = new Border { Padding = new Thickness(4, 6, 0, 2), Child = conteudo }
            };
            exp.Expanded += (s, e) => aoMudar(true);
            exp.Collapsed += (s, e) => aoMudar(false);
            return exp;
        }

        void Recalcular()
        {
            plano.FecharEditor(false);
            ResPlaca r = PlacaCalc.Calcular(m, n);
            var sd = SoldaPilar(r);
            plano.R = r; plano.P = edit;

            // 1a) Faixa de aviso no topo da esquerda
            faixaTopo.Children.Clear();
            bool fixCapOk = r.NComb >= (r.Ch != null ? r.NRef : r.NExig) && (r.Ch == null || !r.Ch.Quimico || r.NAco >= r.NSeg);
            bool espacOk = r.EspacOk;
            bool parOk = r.ChOk;
            bool chOk = r.EspNec <= edit.Esp + 1e-9; // separado de r.Inclinada!
            bool sdOk = sd == null || sd.Passa;

            if (r.Erro != "" && !r.TemEsforcos)
            {
                var bCinza = new Border
                {
                    Background = BCinza, BorderBrush = BBorda, BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 12)
                };
                var spErr = new StackPanel();
                spErr.Children.Add(T("Sem verificação", 14, Brushes.White, FontWeights.SemiBold));
                spErr.Children.Add(T(r.Erro, 12, BFraco));
                bCinza.Child = spErr;
                faixaTopo.Children.Add(bCinza);
            }
            else
            {
                var falhas = new List<string>();
                if (!fixCapOk) falhas.Add(r.Ch != null ? "chumbador insuficiente" : "parafuso insuficiente");
                if (!espacOk) falhas.Add("espaçamento insuficiente");
                if (!chOk) falhas.Add("chapa fina demais");
                if (!sdOk) falhas.Add("solda insuficiente");

                if (falhas.Count > 0)
                {
                    var bRuim = new Border
                    {
                        Background = BRuim, BorderBrush = BVermelho, BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(6), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 10)
                    };
                    var dpR = new DockPanel();
                    var ic = T("✕", 18, BVermelho, FontWeights.Bold); ic.Margin = new Thickness(0, 0, 12, 0); ic.VerticalAlignment = VerticalAlignment.Center;
                    DockPanel.SetDock(ic, Dock.Left); dpR.Children.Add(ic);
                    var spR = new StackPanel();
                    spR.Children.Add(T("Não passa — " + string.Join(", ", falhas), 13.5, Brushes.White, FontWeights.SemiBold));
                    string fixRes = r.Ch != null ? r.Paraf.Count + " × " + NomeCurtoCh(r.Ch) : r.Paraf.Count + " × M" + Num.Fmt(edit.Diametro, 0) + " cl. " + edit.Classe;
                    spR.Children.Add(T(Num.Fmt(r.H, 0) + " × " + Num.Fmt(r.B, 0) + " × " + FmtEsp(edit.Esp) + " mm · " + fixRes + (r.Sec != null ? " · pilar " + r.Sec.Nome : ""), 12, BFraco));
                    dpR.Children.Add(spR);
                    bRuim.Child = dpR;
                    faixaTopo.Children.Add(bRuim);
                }

                if (r.Inclinada)
                {
                    var bInc = new Border
                    {
                        Background = Pincel(0x4A, 0x3F, 0x2A), BorderBrush = Pincel(0x8A, 0x6A, 0x2A), BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(6), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 12)
                    };
                    var dpI = new DockPanel();
                    var ic = T("⚠", 20, BAmarelo); ic.Margin = new Thickness(0, 0, 12, 0); ic.VerticalAlignment = VerticalAlignment.Center;
                    DockPanel.SetDock(ic, Dock.Left); dpI.Children.Add(ic);
                    var spI = new StackPanel();
                    spI.Children.Add(T((falhas.Count == 0 ? "Placa resolvida · " : "") + "falta detalhar a ligação da barra inclinada (" + Num.Fmt(r.AngNormal, 1) + "°)", 13, BTexto, FontWeights.Bold));
                    spI.Children.Add(T(!string.IsNullOrEmpty(r.Aviso) ? r.Aviso : "O cálculo considera a reação no nó; gusset ou cantoneira precisa ser desenhado à parte.", 11.5, BFraco));
                    dpI.Children.Add(spI);
                    bInc.Child = dpI;
                    faixaTopo.Children.Add(bInc);
                }
                else if (falhas.Count == 0)
                {
                    var bOk = new Border
                    {
                        Background = BOk, BorderBrush = BVerde, BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 0, 0, 12)
                    };
                    var dpOk = new DockPanel();
                    string fixRes = r.Ch != null ? r.Paraf.Count + " × " + NomeCurtoCh(r.Ch) : r.Paraf.Count + " × M" + Num.Fmt(edit.Diametro, 0) + " cl. " + edit.Classe;
                    dpOk.Children.Add(T("✓  Passa", 13, BVerde, FontWeights.Bold));
                    var sub = T("   ·   " + Num.Fmt(r.H, 0) + " × " + Num.Fmt(r.B, 0) + " × " + FmtEsp(edit.Esp) + " mm · " + fixRes + (r.Sec != null ? " · pilar " + r.Sec.Nome : "") + (r.Aviso != "" ? "   ⚠ " + r.Aviso : ""), 12, BTexto);
                    sub.VerticalAlignment = VerticalAlignment.Center;
                    dpOk.Children.Add(sub);
                    bOk.Child = dpOk;
                    faixaTopo.Children.Add(bOk);
                }
            }

            // 1b) Abas do cartão da esquerda
            btsAbas.Children.Clear();
            btsAbas.Children.Add(Pilula("Medidas", abaAtiva == 0, () => { abaAtiva = 0; Recalcular(); }));
            btsAbas.Children.Add(Pilula("Forças nos furos", abaAtiva == 1, () => { abaAtiva = 1; Recalcular(); }));
            btsAbas.Children.Add(Pilula("Ficha de fabricação", abaAtiva == 2, () => { abaAtiva = 2; Recalcular(); }));
            btsAbas.Children.Add(Pilula("Cálculo", abaAtiva == 3, () => { abaAtiva = 3; Recalcular(); }));

            if (abaAtiva == 0 || abaAtiva == 1)
            {
                plano.ModoForcas = abaAtiva == 1;
                lblSubAba.Text = abaAtiva == 0
                    ? "Vista de cima · clique numa cota azul para editar"
                    : "Tração (T) e corte (V) em cada furo · clique numa cota azul para editar";
                areaAba.Child = plano;
                plano.InvalidateVisual();
            }
            else if (abaAtiva == 2)
            {
                lblSubAba.Text = "Ficha de fabricação — cortar, furar, soldar e apertar";
                areaAba.Child = Ficha(r, sd);
            }
            else
            {
                lblSubAba.Text = "Detalhes completos das verificações";
                areaAba.Child = AbaCalculo(r, sd);
            }

            // 2) Coluna da direita: 3 cartões + Comparado ao que estava + Avançado
            colDir.Children.Clear();
            MontarCartaoSeguranca(r);
            MontarCartaoFixacao(r);
            MontarCartaoChapa(r);
            MontarCartaoSolda(r, sd);
            MontarCartaoComparacao(r);
            MontarCartaoOutras(r);
            MontarExpanderRodape(r);

            // 3) Rodapé: Prefiro [Mais leve] [Furo menor] + Dimensionar novamente
            rodPref.Children.Clear();
            var lblPref = T("Prefiro:", 12, BFraco); lblPref.VerticalAlignment = VerticalAlignment.Center; lblPref.Margin = new Thickness(0, 0, 8, 0);
            rodPref.Children.Add(lblPref);
            rodPref.Children.Add(Pilula("Mais leve", !prefFuroMenor, () => { prefFuroMenor = false; Dimensionar(); Recalcular(); }));
            rodPref.Children.Add(Pilula("Furo menor", prefFuroMenor, () => { prefFuroMenor = true; Dimensionar(); Recalcular(); }));
            var btDim = B("Dimensionar novamente", () => { Dimensionar(); Recalcular(); });
            btDim.Margin = new Thickness(10, 0, 0, 0);
            rodPref.Children.Add(btDim);
        }

        // 2a) Cartão FIXAÇÃO ([Chumbador] [Parafuso])
        void MontarCartaoFixacao(ResPlaca r)
        {
            Chumbador chAt = r.Ch ?? Chumbadores.Achar(edit.Chumbador);
            bool modoChumb = chAt != null;
            var selModo = new StackPanel { Orientation = Orientation.Horizontal };
            selModo.Children.Add(Pilula("Chumbador", modoChumb, () =>
            {
                if (modoChumb) return;
                string esc = PlacaCalc.DimensionarChumbador(m, n);
                if (string.IsNullOrEmpty(edit.Chumbador))
                {
                    var perto = Chumbadores.Lista.OrderBy(x => Math.Abs(x.Rosca - edit.Diametro)).ThenBy(x => x.Hef).FirstOrDefault();
                    if (perto != null) edit.Chumbador = perto.Nome;
                }
                Recalcular();
            }));
            selModo.Children.Add(Pilula("Parafuso", !modoChumb, () =>
            {
                if (!modoChumb) return;
                edit.Chumbador = null;
                Recalcular();
            }));

            colDir.Children.Add(CabecalhoCartao(modoChumb ? "Chumbador" : "Parafuso", r.ChOk, selModo));

            var sp = new StackPanel();
            if (modoChumb && chAt != null)
            {
                var menorPassa = r.TemEsforcos
                    ? Chumbadores.Lista.OrderBy(c => c.Rosca).ThenBy(c => c.Hef).FirstOrDefault(c => PlacaCalc.CandidatoChumbador(r, edit, m, c).ChOk)
                    : null;
                bool ehMenor = menorPassa == chAt;

                var topo = new DockPanel();
                var btTrocar = T("Trocar ›", 12.5, Pincel(0x3F, 0xB6, 0xEE), FontWeights.SemiBold);
                btTrocar.Cursor = Cursors.Hand; btTrocar.VerticalAlignment = VerticalAlignment.Top;
                btTrocar.MouseLeftButtonDown += (s, e) => { AbrirOverlayTrocar(); e.Handled = true; };
                var pilhaLinks = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
                pilhaLinks.Children.Add(btTrocar);
                var btComparar = T("Comparar…", 12.5, Pincel(0x3F, 0xB6, 0xEE), FontWeights.SemiBold);
                btComparar.Cursor = Cursors.Hand; btComparar.Margin = new Thickness(0, 4, 0, 0); btComparar.HorizontalAlignment = HorizontalAlignment.Right;
                Dica(btComparar, "Compara 2 ou mais chumbadores lado a lado: atributos do catálogo, cargas, verificação com os esforços desta placa, usos recomendados e avisos.");
                btComparar.MouseLeftButtonDown += (s, e) =>
                {
                    e.Handled = true;
                    var rc = PlacaCalc.Calcular(m, n);
                    var ini = new List<string> { chAt.Nome };
                    var alt = Chumbadores.Lista.Where(c => c != chAt && c.Linha != chAt.Linha && PlacaCalc.CandidatoChumbador(rc, edit, m, c).ChOk).OrderBy(c => c.Rosca).ThenBy(c => c.Hef).Select(c => c.Nome).FirstOrDefault();
                    if (alt != null) ini.Add(alt);
                    new JanelaComparar(m, n, rc, edit, ini) { Owner = this }.ShowDialog();
                };
                pilhaLinks.Children.Add(btComparar);
                DockPanel.SetDock(pilhaLinks, Dock.Right);
                topo.Children.Add(pilhaLinks);

                var info = new StackPanel();
                var linNome = new WrapPanel { Orientation = Orientation.Horizontal };
                linNome.Children.Add(T(NomeCurtoCh(chAt), 15, BTexto, FontWeights.Bold));
                if (ehMenor)
                {
                    linNome.Children.Add(new Border
                    {
                        Background = BOk, CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(6, 1, 0, 0),
                        Child = new TextBlock { Text = "menor que passa", FontSize = 11, Foreground = Pincel(0xBF, 0xF5, 0xC8) }
                    });
                }
                info.Children.Add(linNome);

                string tq = chAt.Torque > 0 ? " · torque " + Num.Fmt(chAt.Torque, 1) + " kgf·m" : "";
                string det = chAt.Fabricante + " · " + (chAt.Quimico ? "químico" : "mecânico") + " · furo Ø" +
                             Num.Fmt(chAt.Furo, chAt.Furo == Math.Floor(chAt.Furo) ? 0 : 1) + " × " + Num.Fmt(chAt.Hef, 0) + tq;
                var lblDet = T(det, 11.5, BFraco); lblDet.Margin = new Thickness(0, 2, 0, 0);
                info.Children.Add(lblDet);
                topo.Children.Add(info);
                sp.Children.Add(topo);

                // Se químico: Barra roscada classe [4.6 ▾]
                if (chAt.Quimico)
                {
                    var dpCl = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
                    var cbCl = new ComboBox { Width = 86 };
                    foreach (string cl in Parafusos.Classes) cbCl.Items.Add(cl);
                    cbCl.SelectedItem = pe.ClasseBarra;
                    cbCl.SelectionChanged += (s, e) => { if (cbCl.SelectedItem is string sCl && sCl != pe.ClasseBarra) { pe.ClasseBarra = sCl; Recalcular(); } };
                    DockPanel.SetDock(cbCl, Dock.Right);
                    dpCl.Children.Add(cbCl);
                    var lblCl = T("Barra roscada classe", 12, BFraco); lblCl.VerticalAlignment = VerticalAlignment.Center;
                    dpCl.Children.Add(lblCl);
                    sp.Children.Add(dpCl);
                }

                sp.Children.Add(ConstruirLinhaQuantidade());
                sp.Children.Add(BarraCapacidade(r.ApFix, r.ChOk));

                // Expander Avançado do chumbador
                var spAv = new StackPanel();
                void LinhaAv(string rot, UIElement ctrl, string dica = null)
                {
                    var d = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
                    if (dica != null) Dica(d, dica);
                    DockPanel.SetDock(ctrl, Dock.Right); d.Children.Add(ctrl);
                    var l = T(rot, 11.5, BFraco); l.VerticalAlignment = VerticalAlignment.Center; d.Children.Add(l);
                    spAv.Children.Add(d);
                }
                var tbCoef = Campo(edit.CoefChumbador > 0 ? Num.Fmt(edit.CoefChumbador, 1) : "0", 76);
                tbCoef.ToolTip = "0 = automático pela tabela da Walsywa (tipo de carga + grau de risco)";
                Action aplCoef = () => { double v; if (Num.Ler(tbCoef.Text, out v) && v >= 0 && v != edit.CoefChumbador) { edit.CoefChumbador = v; Recalcular(); } };
                tbCoef.LostFocus += (s, e) => aplCoef();
                tbCoef.KeyDown += (s, e) => { if (e.Key == Key.Enter) aplCoef(); };
                LinhaAv("Coef. segurança (0 = auto)", tbCoef, "Divide a carga de ruptura do catálogo do chumbador. 0 = pela tabela da Walsywa (tipo de carga + grau de risco). Baixo / alto risco: estática 3 / 5; variável 4 / 5; dinâmica 7 / 15; choque 5 / 10. Também dá para mudar no cartão Coeficiente de segurança, no topo.");

                var cbTipo = new ComboBox { Width = 120 };
                foreach (string op in new[] { "automático" }.Concat(Chumbadores.NomesTipos)) cbTipo.Items.Add(op);
                cbTipo.SelectedItem = pe.TipoCarga;
                cbTipo.SelectionChanged += (s, e) => { if (cbTipo.SelectedItem is string st && st != pe.TipoCarga) { pe.TipoCarga = st; Recalcular(); } };
                LinhaAv("Tipo de carga", cbTipo, "Define o coeficiente do chumbador quando ele está em automático: estática, variável, dinâmica ou choque. Automático = dinâmica se houver carga giratória no modelo, senão variável.");

                var cbRisco = new ComboBox { Width = 120 };
                foreach (string op in new[] { "automático", "baixo", "alto" }) cbRisco.Items.Add(op);
                cbRisco.SelectedItem = pe.Risco;
                cbRisco.SelectionChanged += (s, e) => { if (cbRisco.SelectedItem is string sr && sr != pe.Risco) { pe.Risco = sr; Recalcular(); } };
                LinhaAv("Grau de risco", cbRisco, "Alto risco = a falha pode ferir pessoas ou derrubar o equipamento (coeficiente maior). Automático = alto com carga giratória, senão baixo (com aviso).");

                var tbFck = Campo(Num.Fmt(edit.Fck > 0 ? edit.Fck : 20, 0), 76);
                Action aplFck = () => { double v; if (Num.Ler(tbFck.Text, out v) && v >= 0 && v != edit.Fck) { edit.Fck = v; Recalcular(); } };
                tbFck.LostFocus += (s, e) => aplFck();
                tbFck.KeyDown += (s, e) => { if (e.Key == Key.Enter) aplFck(); };
                LinhaAv("Concreto fck (MPa)", tbFck, "Resistência do concreto da laje onde o chumbador é instalado. Se for menor que a do ensaio do catálogo, a carga no concreto é reduzida por √(fck / fck do ensaio).");

                sp.Children.Add(CriarExpander("Avançado", avFixAberto, b => avFixAberto = b, spAv));
            }
            else
            {
                // Modo PARAFUSO (obrigatório)
                int idAt = Array.IndexOf(Parafusos.D, edit.Diametro);
                if (idAt < 0) idAt = 4;
                int icAt = Array.IndexOf(Parafusos.Classes, edit.Classe);
                if (icAt < 0) icAt = 3;

                sp.Children.Add(T(r.Paraf.Count + " × M" + Num.Fmt(edit.Diametro, 0) + " classe " + edit.Classe, 15, BTexto, FontWeights.Bold));
                var detPar = T("pré-carga " + Num.Fmt(r.Fi, 1) + " kN · torque " + Num.Fmt(r.Torque, 0) + " N·m · seg. " + (double.IsInfinity(r.NComb) ? "∞" : Num.Fmt(r.NComb, 2)), 11.5, BFraco);
                detPar.Margin = new Thickness(0, 2, 0, 8);
                sp.Children.Add(detPar);

                Action menosD = idAt > 0 ? (Action)(() => { edit.Diametro = Parafusos.D[idAt - 1]; Recalcular(); }) : null;
                Action maisD = idAt + 1 < Parafusos.D.Length ? (Action)(() => { edit.Diametro = Parafusos.D[idAt + 1]; Recalcular(); }) : null;
                sp.Children.Add(Stepper("M" + Num.Fmt(edit.Diametro, 0), menosD, maisD, 84));

                // Régua com todos os diâmetros usando PlacaCalc.SegurancaCom
                sp.Children.Add(Regua(Parafusos.D.Length, i =>
                {
                    double d = Parafusos.D[i];
                    double seg = r.TemEsforcos ? PlacaCalc.SegurancaCom(r, edit, i, icAt) : double.PositiveInfinity;
                    bool passa = seg >= r.NExig - 1e-9;
                    bool atual = Math.Abs(d - edit.Diametro) < 1e-6;
                    string dica = "M" + Num.Fmt(d, 0) + " · segurança " + (double.IsInfinity(seg) ? "∞" : Num.Fmt(seg, 2));
                    return ("M" + Num.Fmt(d, 0), passa, atual, dica, () => { edit.Diametro = d; Recalcular(); });
                }));

                // Pílulas de classe (Parafusos.Classes)
                var dpCl = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
                var wpCl = new WrapPanel { Orientation = Orientation.Horizontal };
                DockPanel.SetDock(wpCl, Dock.Right);
                foreach (string cl in Parafusos.Classes)
                {
                    string cLocal = cl;
                    wpCl.Children.Add(Pilula(cLocal, edit.Classe == cLocal, () => { edit.Classe = cLocal; Recalcular(); }));
                }
                dpCl.Children.Add(wpCl);
                var lblCl = T("Classe", 12, BFraco); lblCl.VerticalAlignment = VerticalAlignment.Center;
                dpCl.Children.Add(lblCl);
                sp.Children.Add(dpCl);

                // Caixa "Rosca no plano de corte"
                var cbRosca = new CheckBox
                {
                    Content = T("Rosca no plano de corte", 12, BTexto),
                    IsChecked = edit.RoscaNoCorte,
                    Margin = new Thickness(0, 6, 0, 0),
                    Cursor = Cursors.Hand
                };
                cbRosca.Checked += (s, e) => { edit.RoscaNoCorte = true; Recalcular(); };
                cbRosca.Unchecked += (s, e) => { edit.RoscaNoCorte = false; Recalcular(); };
                sp.Children.Add(cbRosca);

                sp.Children.Add(ConstruirLinhaQuantidade());
                sp.Children.Add(BarraCapacidade(r.ApFix, r.ChOk));
            }

            sp.Children.Add(CriarExpander("Ver cálculo", calcFixAberto, b => calcFixAberto = b, TabelaCalculo(LinhasFixacao(r))));
            colDir.Children.Add(CartaoDireita(sp));
        }

        // ---------- Ver cálculo: tabela grandeza | valor (verde = atende, vermelho = não atende) ----------
        static UIElement TabelaCalculo(List<(string rot, string val, int est)> linhas)
        {
            var g = new Grid { Margin = new Thickness(0, 6, 0, 2) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            int i = 0;
            foreach (var l in linhas)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var fundo = new Border { Background = i % 2 == 0 ? Pincel(0x34, 0x3C, 0x4A) : Brushes.Transparent, CornerRadius = new CornerRadius(3) };
                Grid.SetRow(fundo, i); Grid.SetColumnSpan(fundo, 2); g.Children.Add(fundo);
                var a = T(l.rot, 11.5, BFraco); a.Margin = new Thickness(8, 4, 8, 4); a.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetRow(a, i); g.Children.Add(a);
                Brush cor = l.est > 0 ? BVerde : l.est < 0 ? BVermelho : BTexto;
                var b = T((l.est > 0 ? "✓ " : l.est < 0 ? "✕ " : "") + l.val, 11.5, cor, l.est != 0 ? FontWeights.SemiBold : FontWeights.Normal);
                b.TextAlignment = TextAlignment.Right; b.HorizontalAlignment = HorizontalAlignment.Right; b.Margin = new Thickness(0, 4, 8, 4); b.MaxWidth = 210;
                Grid.SetRow(b, i); Grid.SetColumn(b, 1); g.Children.Add(b);
                i++;
            }
            return g;
        }

        static string FmtN(double v, int c = 2) { return double.IsInfinity(v) ? "∞" : Num.Fmt(v, c); }

        List<(string, string, int)> LinhasFixacao(ResPlaca r)
        {
            var l = new List<(string, string, int)>();
            if (!r.TemEsforcos) { l.Add(("Sem verificação", r.Erro, 0)); return l; }
            l.Add(("Tração no mais carregado", Num.Fmt(r.Tmax, 2) + " kN", 0));
            l.Add(("Corte por fixação", Num.Fmt(r.Fv, 2) + " kN", 0));
            if (r.Ch != null)
            {
                l.Add(("Tração resistente (catálogo)", Num.Fmt(r.Tref, 1) + " kN", 0));
                l.Add(("Corte resistente", r.Vref > 0 ? Num.Fmt(r.Vref, 1) + " kN" : "no aço da barra", 0));
                l.Add(("Concreto laje / ensaio", Num.Fmt(r.FckLaje, 0) + " / " + Num.Fmt(r.Ch.Fck, 0) + " MPa", 0));
                if (r.FFck < 0.999) l.Add(("Redução pelo fck", "× " + Num.Fmt(r.FFck, 2), 0));
                if (r.PsiCone < 0.999) l.Add(("Cones sobrepostos (ψ)", "× " + Num.Fmt(r.PsiCone, 2), 0));
                l.Add(("Segurança no concreto", FmtN(r.NComb) + (r.NComb >= r.NRef ? " ≥ " : " < ") + Num.Fmt(r.NRef, 1), r.NComb >= r.NRef ? 1 : -1));
                if (r.Ch.Quimico) l.Add(("Aço da barra (cl. " + r.ClasseBarra + ")", FmtN(r.NAco) + (r.NAco >= r.NSeg ? " ≥ " : " < ") + Num.Fmt(r.NSeg, 1), r.NAco >= r.NSeg ? 1 : -1));
                l.Add(("Espaçamento entre furos", Num.Fmt(r.EspacMin, 0) + " mm (mín. " + Num.Fmt(Chumbadores.EspacMinimo(r.Ch), 0) + ")", r.EspacOk ? 1 : -1));
            }
            else
            {
                l.Add(("Tensão normal σ", Num.Fmt(r.Sigma, 0) + " MPa", 0));
                l.Add(("Tensão de corte τ", Num.Fmt(r.Tau, 0) + " MPa", 0));
                l.Add(("Segurança Sp/√(σ²+3τ²)", FmtN(r.NComb) + (r.NComb >= r.NExig ? " ≥ " : " < ") + Num.Fmt(r.NExig, 2), r.NComb >= r.NExig ? 1 : -1));
                l.Add(("Pré-carga / torque", Num.Fmt(r.Fi, 1) + " kN / " + Num.Fmt(r.Torque, 0) + " N·m", 0));
            }
            return l;
        }

        List<(string, string, int)> LinhasChapa(ResPlaca r)
        {
            var l = new List<(string, string, int)>();
            if (r.EspNec <= 0) { l.Add(("Flexão da chapa", "sem tração nos furos", 1)); return l; }
            l.Add(("Espessura necessária", Num.Fmt(r.EspNec, 1) + " mm", 0));
            l.Add(("Segurança da chapa", SegChapa(r, edit.Esp) + (r.EspNec <= edit.Esp ? " ≥ " : " < ") + Num.Fmt(r.NSeg, 2), r.EspNec <= edit.Esp + 1e-9 ? 1 : -1));
            l.Add(("Tração da fileira crítica", Num.Fmt(r.TLinha, 1) + " kN", 0));
            l.Add(("Braço até o perfil", Num.Fmt(r.Braco, 0) + " mm", 0));
            l.Add(("Largura efetiva", Num.Fmt(r.LargEf, 0) + " mm", 0));
            l.Add(("Aço da chapa fy", Num.Fmt(r.FyChapa, 0) + " MPa", 0));
            return l;
        }

        List<(string, string, int)> LinhasSolda(ResSolda sd)
        {
            var l = new List<(string, string, int)>();
            if (sd == null) { l.Add(("Solda", "sem esforços", 0)); return l; }
            if (sd.Erro != "") { l.Add(("Solda", sd.Erro, -1)); return l; }
            l.Add(("Tensão τ = √(τ'² + τ''²)", Num.Fmt(sd.Tau, 0) + " ≤ " + Num.Fmt(sd.TauAdm, 0) + " MPa", sd.Tau <= sd.TauAdm + 1e-9 ? 1 : -1));
            l.Add(("Perna mínima", sd.HMin > 0 ? Num.Fmt(sd.HMin, 0) + " mm" : "> 25 mm", 0));
            l.Add(("Cordão total / necessário", Num.Fmt(sd.L, 0) + " / " + Num.Fmt(sd.LNec, 0) + " mm", sd.LNec <= sd.L + 1e-9 ? 1 : -1));
            if (sd.Espessura > 0 && sd.H > sd.Espessura) l.Add(("Perna × parede do perfil", Num.Fmt(sd.H, 0) + " > " + Num.Fmt(sd.Espessura, 1) + " mm", -1));
            if (sd.AbaixoMinimo) l.Add(("Mínimo de norma", Num.Fmt(sd.HMinNorma, 0) + " mm", -1));
            return l;
        }

        // ---------- Segurança escolhida e melhores opções ----------
        static readonly double[] ValoresN = { 0, 1.5, 1.67, 2, 2.5, 3, 4, 5 };
        static readonly double[] ValoresNCh = { 0, 2, 3, 4, 5, 7, 10, 15 };

        ResPlaca Testar(PlacaBase pl) { n.Placa = pl; try { return PlacaCalc.Calcular(m, n); } finally { n.Placa = edit; } }

        static string DescFix(PlacaBase p, int qt)
        {
            var c = Chumbadores.Achar(p.Chumbador);
            return qt + " × " + (c != null ? NomeCurtoCh(c) : "M" + Num.Fmt(p.Diametro, 0) + " cl. " + p.Classe);
        }

        // Até 3 combinações fixação + chapa que passam com os coeficientes atuais, da mais leve/menor para a maior
        List<(PlacaBase pl, string txt, double kg)> MelhoresOpcoes(ResPlaca r)
        {
            var res = new List<(PlacaBase, string, double)>();
            if (!r.TemEsforcos) return res;
            void Add(PlacaBase pl, ResPlaca t)
            {
                double esp = PlacaCalc.Chapas.FirstOrDefault(e => e >= t.EspNec - 1e-9);
                if (esp <= 0) return;
                pl.Esp = esp;
                res.Add((pl, DescFix(pl, t.Paraf.Count) + " · chapa " + FmtEsp(esp) + " mm", t.H * t.B * esp * 7850e-9));
            }
            if (!string.IsNullOrEmpty(edit.Chumbador))
            {
                var cands = (prefFuroMenor ? Chumbadores.Lista.OrderBy(c => c.Furo).ThenBy(c => c.Rosca) : Chumbadores.Lista.OrderBy(c => c.Rosca)).ThenBy(c => c.Hef).ToList();
                foreach (int[] arr in new[] { new[] { 2, 2 }, new[] { 2, 3 }, new[] { 3, 3 } })
                {
                    foreach (Chumbador c in cands)
                    {
                        if (res.Count >= 3) break;
                        var pl = edit.Copiar(); pl.Chumbador = c.Nome; pl.Fileiras = arr[0]; pl.PorFileira = arr[1];
                        var t = Testar(pl);
                        if (t.Erro == "" && t.TemEsforcos && t.ChOk && !res.Any(x => x.Item1.Chumbador == c.Nome)) Add(pl, t);
                    }
                    if (res.Count >= 3) break;
                }
            }
            else
            {
                for (int id = 0; id < Parafusos.D.Length && res.Count < 3; id++)
                    for (int ic = 0; ic < Parafusos.Classes.Length; ic++)
                    {
                        var pl = edit.Copiar(); pl.Diametro = Parafusos.D[id]; pl.Classe = Parafusos.Classes[ic];
                        var t = Testar(pl);
                        if (t.Erro == "" && t.TemEsforcos && t.ChOk) { Add(pl, t); break; }
                    }
            }
            return res;
        }

        static UIElement Dica(FrameworkElement e, string dica) { e.ToolTip = dica; ToolTipService.SetInitialShowDelay(e, 300); ToolTipService.SetShowDuration(e, 20000); return e; }

        void MontarCartaoSeguranca(ResPlaca r)
        {
            bool modoCh = !string.IsNullOrEmpty(edit.Chumbador);
            colDir.Children.Add(CabecalhoCartao("Coeficiente de segurança", true));
            var sp = new StackPanel();

            void Linha(string rot, string dica, double[] vals, double atual, string txtAuto, Action<double> set)
            {
                int i = Array.FindIndex(vals, v => Math.Abs(v - atual) < 1e-6);
                Action menos = i > 0 ? (Action)(() => { set(vals[i - 1]); Recalcular(); }) : (i < 0 && atual > 0 ? (Action)(() => { set(vals.LastOrDefault(v => v < atual)); Recalcular(); }) : null);
                Action mais = i >= 0 && i + 1 < vals.Length ? (Action)(() => { set(vals[i + 1]); Recalcular(); }) : (i < 0 && atual < vals.Last() ? (Action)(() => { set(vals.First(v => v > atual)); Recalcular(); }) : null);
                var dp = new DockPanel { Margin = new Thickness(0, 2, 0, 6) };
                var st = Stepper(atual > 0 ? "n = " + Num.Fmt(atual, 2) : txtAuto, menos, mais, 120, () =>
                {
                    string resp = Pergunta.Mostrar(this, rot, rot + " (0 = automático):", Num.Fmt(atual, 2));
                    double v; if (resp != null && Num.Ler(resp, out v) && v >= 0) { set(v); Recalcular(); }
                });
                DockPanel.SetDock(st, Dock.Right); dp.Children.Add(st);
                var l = T(rot, 12, BTexto); l.VerticalAlignment = VerticalAlignment.Center; dp.Children.Add(l);
                Dica(dp, dica);
                sp.Children.Add(dp);
            }
            Linha("Placa e " + (modoCh ? "barra" : "parafuso"), "Coeficiente usado na chapa" + (modoCh ? " e no aço da barra roscada (químico)" : " e no parafuso (Sp/√(σ²+3τ²))") + ". Auto = o do modelo (aba Análise, n = " + Num.Fmt(m.Seguranca, 2) + "). Clique no valor para digitar.",
                ValoresN, edit.Seguranca, "auto (" + Num.Fmt(m.Seguranca, 2) + ")", v => edit.Seguranca = v);
            if (modoCh)
            {
                double nTab = Chumbadores.Coef(r.TipoCarga, r.RiscoAlto);
                Linha("Chumbador no concreto", "Divide a carga de ruptura do catálogo. Auto = tabela da Walsywa pelo tipo de carga e grau de risco (agora n = " + Num.Fmt(nTab, 0) + "). Clique no valor para digitar.",
                    ValoresNCh, edit.CoefChumbador, "Walsywa (" + Num.Fmt(nTab, 0) + ")", v => edit.CoefChumbador = v);
            }

            var ops = MelhoresOpcoes(r);
            var tit = T("Melhores opções com esses coeficientes", 11.5, BFraco, FontWeights.SemiBold); tit.Margin = new Thickness(0, 6, 0, 4);
            sp.Children.Add(tit);
            if (ops.Count == 0) sp.Children.Add(T(r.TemEsforcos ? "Nenhuma opção do catálogo passa. Aumente a placa ou a quantidade." : "Sem esforços: calcule a estrutura.", 11.5, BAmarelo));
            foreach (var o in ops)
            {
                bool atual = DescFix(o.pl, 0) == DescFix(edit, 0) && o.pl.Fileiras == edit.Fileiras && o.pl.PorFileira == edit.PorFileira && Math.Abs(o.pl.Esp - edit.Esp) < 1e-4;
                var lin = new Border { Background = atual ? Pincel(0x2A, 0x45, 0x5E) : Pincel(0x34, 0x3C, 0x4A), CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 0, 0, 4) };
                var dp = new DockPanel();
                var dir = atual ? T("em uso", 11.5, BVerde) : T("Usar", 12, Pincel(0x3F, 0xB6, 0xEE), FontWeights.SemiBold);
                DockPanel.SetDock(dir, Dock.Right); dir.VerticalAlignment = VerticalAlignment.Center; dp.Children.Add(dir);
                var st = new StackPanel();
                st.Children.Add(T(o.txt, 12, BTexto));
                st.Children.Add(T(Num.Fmt(o.kg, 1) + " kg de chapa", 10.5, BFraco));
                dp.Children.Add(st);
                lin.Child = dp;
                if (!atual)
                {
                    var pl = o.pl;
                    lin.Cursor = Cursors.Hand;
                    lin.MouseLeftButtonDown += (s, e) => { edit.Chumbador = pl.Chumbador; edit.Diametro = pl.Diametro; edit.Classe = pl.Classe; edit.Fileiras = pl.Fileiras; edit.PorFileira = pl.PorFileira; edit.Esp = pl.Esp; Recalcular(); e.Handled = true; };
                    Dica(lin, "Troca a fixação e a chapa por esta combinação.");
                }
                sp.Children.Add(lin);
            }
            colDir.Children.Add(CartaoDireita(sp));
        }

        // ---------- Outras fixações: mesmo padrão em outros nós e pior caso ----------
        static bool PassaEm(ResPlaca t, PlacaBase pl) { return t.TemEsforcos ? t.Erro == "" && t.ChOk && t.EspNec <= pl.Esp + 1e-9 : t.Erro.StartsWith("Sem esforços"); }
        static double NotaEm(ResPlaca t, PlacaBase pl) { return !t.TemEsforcos ? 0 : Math.Max(t.ApFix, pl.Esp > 0 ? t.EspNec / pl.Esp : 0); }

        void MontarCartaoOutras(ResPlaca r)
        {
            if (AvaliarEm == null || Candidatos.Count == 0) return;
            var avals = Candidatos.Select(no => (no, t: AvaliarEm(no, edit.Copiar()))).ToList();
            var todos = new List<(No no, double nota, bool passa)> { (n, NotaEm(r, edit), PassaEm(r, edit)) };
            todos.AddRange(avals.Select(x => (x.no, NotaEm(x.t, edit), PassaEm(x.t, edit))));
            var pior = todos.OrderByDescending(x => x.nota).First();

            colDir.Children.Add(CabecalhoCartao("Outras fixações (mesmo padrão)", todos.Where(x => x.no == n || marcados.Contains(x.no.Id)).All(x => x.passa)));
            var sp = new StackPanel();
            var lp = T("Pior caso: nó " + pior.no.Id + (pior.no == n ? " (este)" : "") + " — usa " + (pior.nota * 100).ToString("0") + "%" + (pior.passa ? "" : ", não passa"), 12.5, pior.passa ? BTexto : BVermelho, FontWeights.SemiBold);
            sp.Children.Add(Dica(lp, "Maior capacidade usada (fixação ou chapa) com ESTA placa, na pior combinação de cada nó."));
            var sub = T("Com esta placa em cada nó, na pior combinação de cargas:", 11, BFraco); sub.Margin = new Thickness(0, 2, 0, 6);
            sp.Children.Add(sub);
            foreach (var x in todos)
            {
                var no = x.no;
                var dp = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
                var dir = new StackPanel { Orientation = Orientation.Horizontal };
                if (no == pior.no) { var tg = new Border { Background = BRuim, CornerRadius = new CornerRadius(3), Padding = new Thickness(5, 0, 5, 0), Margin = new Thickness(0, 0, 6, 0), Child = T("pior caso", 10.5, Brushes.White) }; dir.Children.Add(tg); }
                dir.Children.Add(T((x.nota * 100).ToString("0") + "%", 12, x.passa ? (x.nota > 0.9 ? BAmarelo : BVerde) : BVermelho, FontWeights.SemiBold));
                DockPanel.SetDock(dir, Dock.Right); dp.Children.Add(dir);
                string rot = "Nó " + no.Id + (no == n ? " (este)" : no.Placa != null ? " · já tem placa" : " · sem placa");
                if (no == n) dp.Children.Add(T("   " + rot, 12, BTexto));
                else
                {
                    var cb = new CheckBox { Content = T(rot, 12, BTexto), IsChecked = marcados.Contains(no.Id), Cursor = Cursors.Hand };
                    cb.Checked += (s, e) => { marcados.Add(no.Id); Recalcular(); };
                    cb.Unchecked += (s, e) => { marcados.Remove(no.Id); Recalcular(); };
                    Dica(cb, "Marque para copiar esta placa para o nó " + no.Id + " ao clicar em Aplicar" + (no.Placa != null ? " (substitui a placa que ele tem)." : "."));
                    dp.Children.Add(cb);
                }
                sp.Children.Add(dp);
            }
            var bts = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            bool todosMarc = Candidatos.All(c => marcados.Contains(c.Id));
            var pm = Pilula(todosMarc ? "Desmarcar todos" : "Marcar todos", false, () => { if (todosMarc) marcados.Clear(); else foreach (No c in Candidatos) marcados.Add(c.Id); Recalcular(); });
            bts.Children.Add(Dica(pm, "Marca ou desmarca todos os nós de apoio onde cabe uma placa."));
            var bd = B("Dimensionar pelo pior caso", () => { DimensionarGrupo(); Recalcular(); });
            bd.Margin = new Thickness(0, 0, 0, 0);
            bts.Children.Add(Dica(bd, marcados.Count > 0 ? "Escolhe a menor fixação e chapa que passam neste nó E nos marcados, cada um na sua pior combinação."
                                                        : "Escolhe a menor fixação e chapa que passam em TODOS os nós da lista, cada um na sua pior combinação."));
            sp.Children.Add(bts);
            if (marcados.Count > 0)
            {
                var av = T("Ao aplicar, esta placa vai também para: " + string.Join(", ", Candidatos.Where(c => marcados.Contains(c.Id)).Select(c => "nó " + c.Id)) + ".", 11.5, BAmarelo);
                av.Margin = new Thickness(0, 8, 0, 0);
                sp.Children.Add(av);
            }
            colDir.Children.Add(CartaoDireita(sp));
        }

        // Menor fixação (na preferência ativa) que passa em todos os nós do grupo; chapa pela maior espessura necessária
        void DimensionarGrupo()
        {
            if (AvaliarEm == null) { Dimensionar(); return; }
            var grupo = new List<No> { n };
            grupo.AddRange(marcados.Count > 0 ? Candidatos.Where(c => marcados.Contains(c.Id)) : Candidatos);
            Func<PlacaBase, bool> fixOk = pl => grupo.All(no => { var t = AvaliarEm(no, pl); return t.TemEsforcos ? t.Erro == "" && t.ChOk : t.Erro.StartsWith("Sem esforços"); });
            PlacaBase achou = null;
            if (!string.IsNullOrEmpty(edit.Chumbador))
            {
                var cands = (prefFuroMenor ? Chumbadores.Lista.OrderBy(c => c.Furo).ThenBy(c => c.Rosca) : Chumbadores.Lista.OrderBy(c => c.Rosca)).ThenBy(c => c.Hef).ToList();
                foreach (int[] arr in new[] { new[] { 2, 2 }, new[] { 2, 3 }, new[] { 3, 3 } })
                {
                    foreach (Chumbador c in cands) { var pl = edit.Copiar(); pl.Chumbador = c.Nome; pl.Fileiras = arr[0]; pl.PorFileira = arr[1]; if (fixOk(pl)) { achou = pl; break; } }
                    if (achou != null) break;
                }
            }
            else
            {
                for (int id = 0; id < Parafusos.D.Length && achou == null; id++)
                    for (int ic = 0; ic < Parafusos.Classes.Length; ic++) { var pl = edit.Copiar(); pl.Diametro = Parafusos.D[id]; pl.Classe = Parafusos.Classes[ic]; if (fixOk(pl)) { achou = pl; break; } }
            }
            if (achou == null) { msgDim = "Nenhuma fixação do catálogo passa em todos os nós escolhidos."; return; }
            double espNec = grupo.Max(no => { var t = AvaliarEm(no, achou); return t.TemEsforcos ? t.EspNec : 0; });
            double ch = PlacaCalc.Chapas.FirstOrDefault(e => e >= espNec - 1e-9);
            achou.Esp = ch > 0 ? ch : PlacaCalc.Chapas.Last();
            edit.Chumbador = achou.Chumbador; edit.Diametro = achou.Diametro; edit.Classe = achou.Classe; edit.Fileiras = achou.Fileiras; edit.PorFileira = achou.PorFileira; edit.Esp = achou.Esp;
            var r = PlacaCalc.Calcular(m, n);
            if (r.Sec != null) { double h = Solda.Minima(r.Sec, r.Deitado, r.NBarra, r.VBarra, r.MBarra, edit.Eletrodo, edit.Esp); edit.Solda = h > 0 ? h : 25; }
            msgDim = "Dimensionado pelo pior caso entre os nós " + string.Join(", ", grupo.Select(g => g.Id)) + ".";
        }

        // ---------- Ajustar tamanho da chapa (comprimento, largura e borda mínimos) ----------
        // Regras de espaçamento das aulas de Elementos de Máquina (valores em múltiplos do diâmetro d da fixação)
        // e do catálogo de chumbadores (6·d para instalar; 3·hef sem sobrepor cones).
        const double KBorda = 1.5;      // centro do furo até a borda da chapa ≥ KBorda·d   (preencher com a regra dos slides)
        const double KPasso = 3.0;      // entre centros de furos ≥ KPasso·d                (preencher com a regra dos slides)
        const double KChave = 1.5;      // centro do furo até a face do perfil ≥ KChave·d + folga, espaço para a chave/porca
        const double FolgaChave = 10;   // mm
        bool semSobreporCones;

        string TextoRegrasTamanho()
        {
            return "Calcula o menor comprimento, largura e borda da chapa, arredondados de 10 em 10 mm:\n" +
                   "• centro do furo até a borda da chapa ≥ " + Num.Fmt(KBorda, 1) + "·d\n" +
                   "• entre centros de furos ≥ " + Num.Fmt(KPasso, 1) + "·d (chumbador: também ≥ 6·d para instalar" + (semSobreporCones ? " e ≥ 3·hef" : "") + ")\n" +
                   "• centro do furo até a face do perfil ≥ " + Num.Fmt(KChave, 1) + "·d + " + Num.Fmt(FolgaChave, 0) + " mm (espaço para a chave)\n" +
                   "Mantém o número de fileiras e de fixações por fileira. Depois confira a espessura.\n" +
                   "Fonte: os slides de Elementos de Máquina (Aula 2) não trazem passo nem margem mínimos; dão a porca com 2·d entre quinas\n" +
                   "e a arruela com 1,5·d (págs. 18 e 49), de onde vem o espaço para a chave. Borda 1,5·d e passo 3·d são a prática usual adotada.";
        }

        void AjustarTamanho()
        {
            ResPlaca r = PlacaCalc.Calcular(m, n);
            if (r.RefIn <= 0) return;
            Chumbador c = Chumbadores.Achar(edit.Chumbador);
            double d = c != null ? c.Rosca : edit.Diametro;
            double borda = Math.Ceiling(KBorda * d / 5) * 5;
            double passo = KPasso * d;
            if (c != null) { passo = Math.Max(passo, Chumbadores.EspacMinimo(c)); if (semSobreporCones) passo = Math.Max(passo, 3 * c.Hef); }
            double aoPerfil = KChave * d + FolgaChave;   // centro do furo até a face do perfil
            int nf = Math.Max(1, edit.Fileiras), np = Math.Max(1, edit.PorFileira);
            // no plano (H): fileiras fora do perfil, com espaço para a chave
            double h = Math.Max(r.RefIn + 2 * (aoPerfil + borda), 2 * borda + (nf - 1) * passo);
            // fora do plano (B): passo entre as fixações da fileira e chapa mais larga que o perfil
            double b = Math.Max(r.RefOut + 2 * borda, 2 * borda + (np - 1) * passo);
            if (nf == 1) h = Math.Max(h, r.RefIn + 2 * borda);
            edit.PeloPerfil = false;
            edit.H = Math.Ceiling(h / 10) * 10; edit.B = Math.Ceiling(b / 10) * 10; edit.Borda = borda;
            msgDim = "Tamanho ajustado: " + Num.Fmt(edit.H, 0) + " × " + Num.Fmt(edit.B, 0) + " mm, borda " + Num.Fmt(borda, 0) + " mm (d = " + Num.Fmt(d, 1) + " mm, passo ≥ " + Num.Fmt(passo, 0) + " mm).";
        }

        // 2b) Cartão CHAPA
        void MontarCartaoChapa(ResPlaca r)
        {
            bool chOk = r.EspNec <= edit.Esp + 1e-9;
            colDir.Children.Add(CabecalhoCartao("Chapa", chOk));

            var sp = new StackPanel();
            int idx = Array.FindIndex(PlacaCalc.Chapas, e => Math.Abs(e - edit.Esp) < 1e-4);
            Action menosCh = idx > 0 ? (Action)(() => { edit.Esp = PlacaCalc.Chapas[idx - 1]; Recalcular(); })
                : (edit.Esp > PlacaCalc.Chapas[0] ? (Action)(() => { edit.Esp = PlacaCalc.Chapas.LastOrDefault(e => e < edit.Esp); if (edit.Esp <= 0) edit.Esp = PlacaCalc.Chapas[0]; Recalcular(); }) : null);
            Action maisCh = idx >= 0 && idx + 1 < PlacaCalc.Chapas.Length ? (Action)(() => { edit.Esp = PlacaCalc.Chapas[idx + 1]; Recalcular(); })
                : (edit.Esp < PlacaCalc.Chapas.Last() ? (Action)(() => { edit.Esp = PlacaCalc.Chapas.First(e => e > edit.Esp); Recalcular(); }) : null);

            var dpTopo = new DockPanel();
            var dirInfo = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
            DockPanel.SetDock(dirInfo, Dock.Right);

            var linFy = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            linFy.Children.Add(T("aço fy ", 12, BTexto));
            var linkFy = T(Num.Fmt(r.FyChapa, 0) + " MPa", 12, Pincel(0x3F, 0xB6, 0xEE), FontWeights.SemiBold);
            linkFy.Cursor = Cursors.Hand;
            linkFy.ToolTip = "Clique para editar fy da chapa (MPa)";
            linkFy.MouseLeftButtonDown += (s, e) =>
            {
                string resp = Pergunta.Mostrar(this, "Aço da chapa", "Tensão de escoamento fy da chapa (MPa):", Num.Fmt(r.FyChapa, 0));
                double v;
                if (resp != null && Num.Ler(resp, out v) && v > 0) { edit.FyChapa = v; Recalcular(); }
                e.Handled = true;
            };
            linFy.Children.Add(linkFy);
            dirInfo.Children.Add(linFy);

            double massaKg = r.H * r.B * edit.Esp * 7850e-9;
            var lblMassa = T(Num.Fmt(massaKg, 1) + " kg", 12, BFraco);
            lblMassa.HorizontalAlignment = HorizontalAlignment.Right;
            dirInfo.Children.Add(lblMassa);
            dpTopo.Children.Add(dirInfo);

            dpTopo.Children.Add(Stepper(FmtEsp(edit.Esp) + " mm", menosCh, maisCh, 92));
            sp.Children.Add(dpTopo);

            // Régua com todas as espessuras comerciais
            sp.Children.Add(Regua(PlacaCalc.Chapas.Length, i =>
            {
                double t = PlacaCalc.Chapas[i];
                bool passa = t >= r.EspNec - 1e-9;
                bool atual = Math.Abs(t - edit.Esp) < 1e-4;
                string dica = FmtEsp(t) + " mm · segurança " + SegChapa(r, t);
                return (FmtEsp(t), passa, atual, dica, () => { edit.Esp = t; Recalcular(); });
            }));

            double apCh = edit.Esp > 0 ? r.EspNec / edit.Esp : 0;
            sp.Children.Add(BarraCapacidade(apCh, chOk));
            var dpTam = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            var cbCone = new CheckBox { Content = T("sem sobrepor cones (3·hef)", 11.5, BFraco), IsChecked = semSobreporCones, VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand, Visibility = r.Ch != null ? Visibility.Visible : Visibility.Collapsed };
            cbCone.Checked += (s, e) => semSobreporCones = true; cbCone.Unchecked += (s, e) => semSobreporCones = false;
            Dica(cbCone, "Marcado: o botão usa espaçamento ≥ 3·hef entre chumbadores, para o catálogo valer sem redução por cones sobrepostos (placa maior). Desmarcado: usa o mínimo para instalar e aplica a redução ψ.");
            DockPanel.SetDock(cbCone, Dock.Right); dpTam.Children.Add(cbCone);
            var btTam = B("Ajustar tamanho da chapa", () => { AjustarTamanho(); Recalcular(); });
            btTam.Margin = new Thickness(0);
            Dica(btTam, TextoRegrasTamanho());
            dpTam.Children.Add(btTam);
            sp.Children.Add(dpTam);
            sp.Children.Add(CriarExpander("Ver cálculo", calcChAberto, b => calcChAberto = b, TabelaCalculo(LinhasChapa(r))));
            colDir.Children.Add(CartaoDireita(sp));
        }

        // 2c) Cartão SOLDA PILAR–PLACA
        void MontarCartaoSolda(ResPlaca r, ResSolda sd)
        {
            bool sdOk = sd == null || sd.Passa;
            colDir.Children.Add(CabecalhoCartao("Solda pilar–placa", sdOk));

            var sp = new StackPanel();
            double hEf = sd != null ? sd.H : (edit.Solda > 0 ? edit.Solda : 3);
            string txtVal = edit.Solda <= 0 ? "auto (" + Num.Fmt(hEf, 0) + " mm)" : Num.Fmt(edit.Solda, 0) + " mm";

            // Lista de opções do stepper: 0 (auto) seguido de Solda.Pernas
            int idxP = edit.Solda <= 0 ? -1 : Array.FindIndex(Solda.Pernas, p => Math.Abs(p - edit.Solda) < 1e-4);
            Action menosSd = edit.Solda <= 0 ? null : (Action)(() =>
            {
                if (idxP <= 0) edit.Solda = 0;
                else edit.Solda = Solda.Pernas[idxP - 1];
                Recalcular();
            });
            Action maisSd = () =>
            {
                if (edit.Solda <= 0)
                {
                    double prox = Solda.Pernas.FirstOrDefault(p => p >= hEf - 1e-9);
                    edit.Solda = prox > 0 ? prox : Solda.Pernas[0];
                }
                else if (idxP >= 0 && idxP + 1 < Solda.Pernas.Length) edit.Solda = Solda.Pernas[idxP + 1];
                Recalcular();
            };

            var dpTopo = new DockPanel();
            var dirInfo = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
            DockPanel.SetDock(dirInfo, Dock.Right);

            var linEl = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            linEl.Children.Add(T("filete · ", 12, BTexto));
            string elAt = string.IsNullOrEmpty(edit.Eletrodo) ? "E70" : edit.Eletrodo;
            var linkEl = T(elAt, 12, Pincel(0x3F, 0xB6, 0xEE), FontWeights.SemiBold);
            linkEl.Cursor = Cursors.Hand;
            linkEl.ToolTip = "Clique para escolher o eletrodo";
            linkEl.MouseLeftButtonDown += (s, e) =>
            {
                var cm = new ContextMenu();
                foreach (string el in Solda.Eletrodos)
                {
                    string elLocal = el;
                    var mi = new MenuItem { Header = elLocal + "XX", IsChecked = elLocal == elAt };
                    mi.Click += (s2, e2) => { edit.Eletrodo = elLocal; Recalcular(); };
                    cm.Items.Add(mi);
                }
                cm.PlacementTarget = linkEl;
                cm.IsOpen = true;
                e.Handled = true;
            };
            linEl.Children.Add(linkEl);
            dirInfo.Children.Add(linEl);

            var lblCont = T("contorno todo · " + (sd != null ? Num.Fmt(sd.L, 0) + " mm" : "—"), 12, BFraco);
            lblCont.HorizontalAlignment = HorizontalAlignment.Right;
            dirInfo.Children.Add(lblCont);
            dpTopo.Children.Add(dirInfo);

            dpTopo.Children.Add(Stepper(txtVal, menosSd, maisSd, 88, () => { edit.Solda = 0; Recalcular(); }));
            sp.Children.Add(dpTopo);

            sp.Children.Add(BarraCapacidade(sd != null ? sd.Ap : 0, sdOk));
            sp.Children.Add(CriarExpander("Ver cálculo", calcSdAberto, b => calcSdAberto = b, TabelaCalculo(LinhasSolda(sd))));
            colDir.Children.Add(CartaoDireita(sp));
        }

        // 2d) Cartão verde "Comparado ao que estava"
        void MontarCartaoComparacao(ResPlaca r)
        {
            if (refInicial == null || r.H <= 0 || r.B <= 0) return;
            double h0 = refInicial.PeloPerfil ? Math.Ceiling((r.RefIn + 2 * refInicial.Sobra) / 10) * 10 : refInicial.H;
            double b0 = refInicial.PeloPerfil ? Math.Ceiling((r.RefOut + 2 * refInicial.Sobra) / 10) * 10 : refInicial.B;
            double m0 = h0 * b0 * refInicial.Esp * 7850e-9;
            double m1 = r.H * r.B * edit.Esp * 7850e-9;
            double dm = m1 - m0;

            var muds = new List<string>();
            if (Math.Abs(refInicial.Esp - edit.Esp) > 1e-4)
                muds.Add("chapa " + FmtEsp(refInicial.Esp) + " → " + FmtEsp(edit.Esp) + " mm");
            if (Math.Abs(h0 - r.H) > 1e-4 || Math.Abs(b0 - r.B) > 1e-4)
                muds.Add("placa " + Num.Fmt(h0, 0) + "×" + Num.Fmt(b0, 0) + " → " + Num.Fmt(r.H, 0) + "×" + Num.Fmt(r.B, 0) + " mm");

            string FixDesc(PlacaBase p)
            {
                var c = Chumbadores.Achar(p.Chumbador);
                return c != null ? "chumbador " + NomeCurtoCh(c) : "parafuso M" + Num.Fmt(p.Diametro, 0) + " cl. " + p.Classe;
            }
            string f0 = FixDesc(refInicial), f1 = FixDesc(edit);
            if (f0 != f1 || refInicial.Fileiras != edit.Fileiras || refInicial.PorFileira != edit.PorFileira)
            {
                var c0 = Chumbadores.Achar(refInicial.Chumbador);
                var c1 = Chumbadores.Achar(edit.Chumbador);
                if (c0 != null && c1 != null)
                    muds.Add("chumbador " + c0.Pol + " → " + c1.Pol + (c1.Furo < c0.Furo ? " (furo menor)" : ""));
                else
                    muds.Add(f0 + " → " + f1);
            }
            if (muds.Count == 0) return;

            var card = new Border
            {
                Background = Pincel(0x2A, 0x3A, 0x33),
                BorderBrush = BOk,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 2, 0, 10)
            };
            var sp = new StackPanel();
            var dp = new DockPanel();
            string sinal = dm < -0.05 ? "−" : dm > 0.05 ? "+" : "";
            var lblDm = T(sinal + Num.Fmt(Math.Abs(dm), 1) + " kg de aço", 12, dm <= 0.05 ? BVerde : BAmarelo, FontWeights.SemiBold);
            DockPanel.SetDock(lblDm, Dock.Right);
            dp.Children.Add(lblDm);
            dp.Children.Add(T("Comparado ao que estava", 12, BFraco));
            sp.Children.Add(dp);
            var det = T(string.Join(" · ", muds), 11.5, BFraco);
            det.Margin = new Thickness(0, 4, 0, 0);
            sp.Children.Add(det);
            card.Child = sp;
            colDir.Children.Add(card);
        }

        // 2e) "Medir pelo perfil" e "Giro na mesa do perfil" num expander "Avançado" no rodapé da coluna direita
        void MontarExpanderRodape(ResPlaca r)
        {
            var sp = new StackPanel();
            var cbPerfil = new CheckBox
            {
                Content = T("Medir pelo perfil (pilar + sobra)", 12, BTexto),
                IsChecked = edit.PeloPerfil,
                Margin = new Thickness(0, 2, 0, 6),
                Cursor = Cursors.Hand
            };
            cbPerfil.Checked += (s, e) => { edit.PeloPerfil = true; Recalcular(); };
            cbPerfil.Unchecked += (s, e) => { edit.PeloPerfil = false; edit.H = r.H; edit.B = r.B; Recalcular(); };
            sp.Children.Add(cbPerfil);

            void CampoMed(string rot, double val, Action<double> aplicar)
            {
                var dp = new DockPanel { Margin = new Thickness(0, 2, 0, 4) };
                var tb = Campo(Num.Fmt(val, 0), 76);
                Action ok = () => { double v; if (Num.Ler(tb.Text, out v) && v > 0 && Math.Abs(v - val) > 1e-4) { aplicar(v); Recalcular(); } };
                tb.LostFocus += (s, e) => ok();
                tb.KeyDown += (s, e) => { if (e.Key == Key.Enter) ok(); };
                DockPanel.SetDock(tb, Dock.Right); dp.Children.Add(tb);
                var l = T(rot, 11.5, BFraco); l.VerticalAlignment = VerticalAlignment.Center; dp.Children.Add(l);
                sp.Children.Add(dp);
            }

            if (edit.PeloPerfil) CampoMed("Sobra de cada lado (mm)", edit.Sobra, v => edit.Sobra = v);
            else
            {
                CampoMed("Comprimento no plano H (mm)", edit.H, v => edit.H = v);
                CampoMed("Largura B (mm)", edit.B, v => edit.B = v);
            }
            CampoMed("Borda até o centro do furo (mm)", edit.Borda, v => edit.Borda = v);

            var cbPivo = new CheckBox
            {
                Content = T("Giro na mesa do perfil", 12, BTexto),
                IsChecked = edit.PivoNaMesa,
                Margin = new Thickness(0, 4, 0, 2),
                Cursor = Cursors.Hand,
                ToolTip = "Sim = a placa gira na mesa comprimida do perfil; Não = na borda comprimida da placa."
            };
            cbPivo.Checked += (s, e) => { edit.PivoNaMesa = true; Recalcular(); };
            cbPivo.Unchecked += (s, e) => { edit.PivoNaMesa = false; Recalcular(); };
            sp.Children.Add(cbPivo);

            colDir.Children.Add(CriarExpander("Avançado (medidas e modelo de giro)", avRodAberto, b => avRodAberto = b, sp));
        }
    }

    // ---------- assistentes ----------

    public class JanelaAssistente : JanelaEscura
    {
        public Modelo Resultado;
        readonly Vista previa = new Vista { IsHitTestVisible = false };
        readonly StackPanel campos = new StackPanel();
        readonly TextBlock erro = T("", 11, BVermelho);
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
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            tipos.Background = BPainel; tipos.Foreground = BTexto; tipos.BorderThickness = new Thickness(0); tipos.FontSize = 13;
            tipos.ToolTip = "Escolha o tipo de estrutura parametrizada que deseja gerar.";
            foreach (var t in Tipos)
            {
                string dicaTipo = t[0] == "viga" ? "Gera uma viga contínua de vários vãos com apoios e carga distribuída."
                    : t[0] == "portico" ? "Gera um pórtico plano de vários vãos e andares com cargas verticais e de vento."
                    : t[0] == "trelica" ? "Gera uma treliça plana (Pratt, Howe ou Warren) com barras biarticuladas e forças nodais."
                    : "Gera um pórtico de galpão de duas águas com pilares, tesouras/vigas inclinadas e carga na cobertura.";
                tipos.Items.Add(new ListBoxItem { Content = t[1], Tag = t[0], Padding = new Thickness(10, 8, 10, 8), ToolTip = dicaTipo });
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
            var bp = new Border { BorderBrush = BBorda, BorderThickness = new Thickness(1), Child = previa, ToolTip = "Pré-visualização em tempo real da geometria, apoios e cargas da estrutura que será gerada." };
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
            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            sp.Children.Add(T(rotulo, 12, BTexto, FontWeights.SemiBold));
            var tb = Campo(valor, 290, dica ?? rotulo); tb.Margin = new Thickness(0, 3, 0, 2);
            tb.TextChanged += (s, e) => Gerar();
            c[chave] = tb;
            sp.Children.Add(tb);
            if (dica != null) sp.Children.Add(T(dica, 11, BFraco));
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
                previa.M = m; previa.Resolvido = false; previa.ZoomTudo();
            }
            catch (Exception ex) { Resultado = null; erro.Text = ex.Message; }
        }

        void Criar()
        {
            Gerar();
            if (Resultado == null) return;
            DialogResult = true;
        }
    }

    // ---------- ajuda ----------

    public class JanelaAjuda : JanelaEscura
    {
        public JanelaAjuda()
        {
            Title = "Ajuda — Estructto"; Width = 760; Height = 640; ShowInTaskbar = true;
            var sp = new StackPanel { Margin = new Thickness(22) };
            void Sec(string t) { sp.Children.Add(T(t, 15, BAzul, FontWeights.SemiBold)); sp.Children[sp.Children.Count - 1].SetValue(MarginProperty, new Thickness(0, 12, 0, 6)); }
            void L(string k, string d)
            {
                var dp = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                var a = T(k, 12, BTexto, FontWeights.SemiBold); a.Width = 190; DockPanel.SetDock(a, Dock.Left);
                dp.Children.Add(a); dp.Children.Add(T(d, 12, BFraco)); sp.Children.Add(dp);
            }
            sp.Children.Add(T("Como usar em 4 passos", 20, BTexto, FontWeights.Light));
            sp.Children.Add(T("1) Desenhe as barras (B) ou use um Assistente.  2) Selecione os nós da base e aplique apoios (aba Estrutura).  3) Aplique cargas (aba Cargas).  4) Veja os diagramas (aba Resultados) — o cálculo é refeito a cada alteração.", 12, BFraco));
            Sec("Seleção (como no AutoCAD)");
            L("Clique", "seleciona um nó ou barra; Shift/Ctrl soma ou tira");
            L("Arrastar → (esq. p/ dir.)", "janela: só o que estiver inteiro dentro (azul)");
            L("Arrastar ← (dir. p/ esq.)", "cruzamento: tudo o que tocar (verde)");
            L("Nó selecionado", "arraste o quadradinho azul para mover o nó");
            L("Com itens selecionados", "os botões de apoio/carga aplicam direto neles; Propriedades edita todos de uma vez");
            L("Botão direito", "menu do item; durante um comando, encerra");
            L("Duplo clique", "barra: verificação; nó com placa: placa de base");
            Sec("Desenho");
            L("B + Enter", "barra: clique os pontos em sequência (todo comando executa no Enter ou Espaço, como no AutoCAD)");
            L("Digitar 5 + Enter", "comprimento exato na direção do mouse");
            L("x,y   @dx,dy   @L<ângulo", "coordenada absoluta, relativa ou polar (decimal com ponto)");
            L("2,5;3", "com ponto e vírgula a vírgula vira decimal");
            L("Shift / F8", "ortogonal; F10 polar (prende perto de 0°/90°)");
            L("F3 / F9 / F7", "atração a nó/meio/barra · snap na grade · mostrar grade");
            L("NO", "inserir nó (divide a barra)");
            L("Ctrl+M · DI · MEDIR", "medir distância, ΔX, ΔY e ângulo entre dois pontos ou nós");
            L("MOVER · COPIAR · ESPELHAR", "sobre a seleção: MOVER dx dy · COPIAR dx dy vezes · ESPELHAR X valor");
            L("QD qy [qx] · FN fx fy [mz]", "carga distribuída nas barras / carga nos nós selecionados");
            L("SELBASE · SELNOS · SELTODAS", "seleciona os nós da base · todos os nós · todas as barras");
            L("F2", "histórico completo da linha de comando");
            Sec("Dimensionamento");
            L("DIMENSIONAR · DIMSEL", "perfil mais leve da família que passa (todas as barras / só as selecionadas); recalcula até estabilizar");
            L("SOLDAS · ELETRODO E70", "tabela da perna mínima de filete em cada ponta de barra · eletrodo das soldas");
            L("Placa → Dimensionar", "menor parafuso, menor chapa comercial e perna mínima da solda pilar–placa");
            Sec("Resultados");
            L("D · N · Q · M · V (+ Enter)", "deformada · normal · cortante · momento · verificação (repetir desliga)");
            L("+ / −", "escala do diagrama");
            L("Passar o mouse", "lê o valor no ponto da barra");
            L("F5", "calcular (também é automático)");
            Sec("Geral");
            L("Ctrl+N / O / S / Shift+S", "novo · abrir · salvar · salvar como");
            L("Ctrl+Z / Ctrl+Y", "desfazer · refazer");
            L("Del · Ctrl+A · Esc", "apagar · selecionar todas as barras · cancelar (em cascata)");
            L("F4 / roda / botão do meio", "enquadrar · zoom · mover a vista (duplo clique na roda enquadra)");
            L("Ctrl+E", "exportar resultados em CSV");
            L("Linha de comando", "digite o nome do comando (Tab completa); AJUDA lista todos");
            Sec("Convenções e unidades");
            sp.Children.Add(T("m, kN, kN/m, kN·m; deslocamentos em mm. Fx positivo para a direita, Fy para cima, Mz anti-horário. N positivo = tração; M positivo traciona a fibra de baixo (o diagrama é desenhado do lado tracionado). Seções digitadas em cm (Ret/Circ) ou mm (TC/TR). A verificação é simplificada (tensão, corte, flecha e flambagem de Euler/Johnson) e não substitui a NBR 8800.", 12, BFraco));
            Content = new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }
    }
}
