// Janela principal: monta a faixa (Fluent.Ribbon), os bastidores (menu Arquivo), a barra de status e liga os eventos.
// A logica fica nos arquivos parciais Acoes.cs (comandos e arquivos) e Paineis.cs (arvore, propriedades, tabelas, avisos).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Portico;
using F = Fluent;

namespace NovoFtool
{
    public partial class MainWindow : F.RibbonWindow
    {
        readonly Dictionary<Ferramenta, List<F.ToggleButton>> btnFerr = new Dictionary<Ferramenta, List<F.ToggleButton>>();
        readonly Dictionary<Diagrama, F.ToggleButton> btnDiag = new Dictionary<Diagrama, F.ToggleButton>();
        F.ToggleButton tgReac, tgValores, tgCargas, tgNomes, tgPeso, tgTimo, tgEixoFraco;
        System.Windows.Controls.Primitives.ToggleButton stGrade, stAtrair, stOrto, stPolar, stObj;
        F.TextBox cFx, cFy, cMz, cQx, cQy, cQi, cQf, cGrade, cSeg, cFlecha;
        F.ComboBox cbFamilia, cbSecao, cbMaterial;
        internal HudVista Hud;
        F.ToggleButton tgCalor;
        internal F.RibbonTabItem abaModelo, abaAnalise, abaDimens, ctxBarra, ctxNo;

        public MainWindow()
        {
            InitializeComponent();
            FontFamily = TemaPlant.Fonte;
            TitleBackground = TemaPlant.B(TemaPlant.Titulo);          // titulo + acesso rapido na faixa ardosia
            TitleForeground = TemaPlant.B(TemaPlant.TextoFraco);
            Background = TemaPlant.B(TemaPlant.Abas);
            NonActiveBorderBrush = TemaPlant.B(TemaPlant.Abas);
            BorderBrush = TemaPlant.B(TemaPlant.Abas);
            // selecao azul nas listas/arvore (no lugar do branco padrao do Windows)
            foreach (var res in new[] { Arvore.Resources, ListaAvisos.Resources })
            {
                res[SystemColors.HighlightBrushKey] = TemaPlant.BSelecao;
                res[SystemColors.HighlightTextBrushKey] = TemaPlant.BTexto1;
                res[SystemColors.InactiveSelectionHighlightBrushKey] = TemaPlant.BSup2;
                res[SystemColors.InactiveSelectionHighlightTextBrushKey] = TemaPlant.BTexto1;
            }
            Loaded += (s, e) => { if (TitleBar != null) { TitleBar.Background = TemaPlant.B(TemaPlant.Titulo); TitleBar.Foreground = TemaPlant.B(TemaPlant.TextoFraco); } };
            TitleBarHeight = 32;                                       // linha do titulo do Plant 3D: 32 px
            // barra das abas de arquivo (a do AvalonDock fica escondida; ver EsconderAbaDocumento)
            ArqMenu.Click += (s, e) => Bastidor.IsOpen = true;
            ArqInicio.Click += (s, e) => { MontarTelaInicial(); TelaInicial.Visibility = TelaInicial.Visibility == Visibility.Visible && M.Barras.Count > 0 ? Visibility.Collapsed : Visibility.Visible; };
            ArqNovo.Click += (s, e) => Novo();
            Loaded += (s, e) => Dispatcher.BeginInvoke(new Action(EsconderAbaDocumento), System.Windows.Threading.DispatcherPriority.Loaded);
            var selo = TemaPlant.Selo();
            Loaded += (s, e) => Dispatcher.BeginInvoke(new Action(() => TemaPlant.AjustarFaixa(this, selo)), System.Windows.Threading.DispatcherPriority.Loaded);
            Faixa.SelectedTabChanged += (s, e) => Dispatcher.BeginInvoke(new Action(() => TemaPlant.AjustarFaixa(Faixa, null)), System.Windows.Threading.DispatcherPriority.Loaded);
            TemaPlant.AplicarDock(Docas);
            Bastidor.Header = "Arquivo";                               // o selo do app fica na barra de titulo (como o "A P3D")
            Bastidor.Background = TemaPlant.B(TemaPlant.Abas);              // "Arquivo" como mais uma aba, sem botao colorido
            Comando.TextChanged += (s, e) => DicaComando.Visibility = Comando.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            MontarFaixa();
            MontarBastidores();
            MontarStatus();
            LigarEventos();
            Hud = new HudVista(Vista, (d, calor) => MostrarDiagrama(d, calor), () => { try { return Vista.Resolvido ? "Mostrando: " + NomeResultado() : ""; } catch { return ""; } }, f => Escala(f));
            GridVista.Children.Add(Hud);
            Vista.SizeChanged += (s, e) => Hud.Compacto = Vista.ActualHeight < 340;
            HistBtn.Click += (s, e) => { bool abrir = HistoricoCaixa.Visibility != Visibility.Visible; HistoricoCaixa.Visibility = abrir ? Visibility.Visible : Visibility.Collapsed; HistBtn.Content = abrir ? "▾" : "▴"; Comando.Focus(); };
            NovoModelo(new Modelo(), null);
            Loaded += (s, e) => { Vista.ZoomTudo(); Vista.Focus(); if (!Capturas.Ativo) OferecerRecuperacao(); };
            Log("Bem-vindo. Escolha uma ferramenta na faixa, use um assistente ou digite um comando (AJUDA lista todos). F1 = atalhos.", false);

            string[] args = Environment.GetCommandLineArgs();
            if (Capturas.Configurar(this, args)) return;
            string arqInicial = args.Skip(1).FirstOrDefault(a => !a.StartsWith("--") && File.Exists(a));
            if (arqInicial != null)
                Loaded += (s, e) => Dispatcher.BeginInvoke(new Action(() => Abrir(arqInicial)), System.Windows.Threading.DispatcherPriority.Loaded);
            foreach (string a in args)
                if (a == "--tela2")
                {
                    // testes automaticos: abre no monitor que NAO e o principal, para nao atrapalhar quem esta usando o PC
                    var outra = OutroMonitor();
                    if (outra != null)
                    {
                        WindowStartupLocation = WindowStartupLocation.Manual;
                        double dpi = 1;
                        Left = outra.Value.Left / dpi + 40; Top = outra.Value.Top / dpi + 40;
                        ShowActivated = false;
                        if (args.Contains("--1920")) { Left = outra.Value.Left; Top = outra.Value.Top; Width = 1920; Height = 1032; }   // comparacao pixel a pixel
                    }
                }
            // --png=arquivo : o app desenha a propria janela num PNG e fecha (comparacao visual sem mexer na tela do usuario)
            foreach (string a in args)
                if (a.StartsWith("--png="))
                {
                    string saida = a.Substring(6);
                    var tm = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
                    tm.Tick += (s, e) =>
                    {
                        tm.Stop();
                        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
                        rtb.Render(this);
                        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                        using (var fs = File.Create(saida)) enc.Save(fs);
                        alterado = false;
                        Close();
                    };
                    Loaded += (s, e) => tm.Start();
                }
            // --relpng=pasta : abre o arquivo, grava o relatorio COMPLETO em PNG (A4) e fecha (teste automatico)
            foreach (string a in args)
                if (a.StartsWith("--relpng="))
                {
                    string pasta = a.Substring(9);
                    var tm = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                    tm.Tick += (s, e) => { tm.Stop(); SalvarRelatorioPng(pasta, true); alterado = false; foreach (var d in docs) d.Alterado = false; Close(); };
                    Loaded += (s, e) => tm.Start();
                }
            foreach (string a in args)
                if (a.StartsWith("--arvore="))
                    Loaded += (s, e) => Dispatcher.BeginInvoke(new Action(() => TemaPlant.DespejarArvore(this, a.Substring(9))), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            foreach (string a in args)
                if (a == "--nada")
                {
                    {
                    }
                }
            foreach (string a in args)
                if (a.StartsWith("--cmd="))
                    Loaded += (s, e) => Dispatcher.BeginInvoke(new Action(() => { foreach (string c in a.Substring(6).Split('|')) Executar(c); }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        // ================= icones =================

        static readonly Dictionary<string, string> Icones = new Dictionary<string, string>
        {
            ["novo"] = "M8,3 H19 L25,9 V29 H8 Z M19,3 V9 H25",
            ["abrir"] = "M3,9 H12 L14,12 H27 V26 H3 Z M3,15 H27",
            ["salvar"] = "M5,4 H23 L27,8 V28 H5 Z M10,4 V11 H21 V4 M9,17 H23 V28 H9 Z",
            ["importar"] = "M8,3 H19 L25,9 V29 H8 Z M16,12 V24 M11,19 L16,24 L21,19",
            ["exportar"] = "M8,3 H19 L25,9 V29 H8 Z M16,24 V12 M11,17 L16,12 L21,17",
            ["desfazer"] = "M10,8 L4,14 L10,20 M4,14 H20 A7,7 0 0 1 20,28 H12",
            ["refazer"] = "M22,8 L28,14 L22,20 M28,14 H12 A7,7 0 0 0 12,28 H20",
            ["sel"] = "M9,4 L9,26 L14,21 L18,29 L21,27.5 L17,20 L24,20 Z",
            ["barra"] = "M6,25 L26,7 M3,22 A3,3 0 1 0 9,28 A3,3 0 1 0 3,22 M23,4 A3,3 0 1 0 29,10 A3,3 0 1 0 23,4",
            ["no"] = "M3,16 H29 M12,16 A4,4 0 1 0 20,16 A4,4 0 1 0 12,16",
            ["apagar"] = "M7,9 H25 M12,9 V5 H20 V9 M9,9 L10,28 H22 L23,9 M14,13 V24 M18,13 V24",
            ["dividir"] = "M3,16 H29 M10,10 V22 M22,10 V22 M16,12 V20",
            ["engaste"] = "M16,4 V17 M5,17 H27 M8,17 L4,23 M14,17 L10,23 M20,17 L16,23 M26,17 L22,23",
            ["rotula"] = "M16,7 L8,21 H24 Z M5,25 H27 M8,25 L5,29 M14,25 L11,29 M20,25 L17,29 M26,25 L23,29",
            ["rolo"] = "M16,5 L9,17 H23 Z M6,21 H26 M6,26 H26",
            ["roloh"] = "M27,16 L15,9 V23 Z M11,6 V26 M6,6 V26",
            ["livre"] = "M8,8 L24,24 M24,8 L8,24",
            ["rotulab"] = "M2,16 H11 M21,16 H30 M11,16 A5,5 0 1 0 21,16 A5,5 0 1 0 11,16",
            ["placa"] = "M16,3 V18 M4,18 H28 V23 H4 Z M8,23 V28 M24,23 V28",
            ["cargano"] = "M16,3 V25 M10,18 L16,26 L22,18 M8,29 H24",
            ["cargadist"] = "M3,5 H29 M6,5 V20 M3.5,16 L6,21 L8.5,16 M16,5 V20 M13.5,16 L16,21 L18.5,16 M26,5 V20 M23.5,16 L26,21 L28.5,16 M3,25 H29",
            ["peso"] = "M10,12 H22 L26,28 H6 Z M13,12 A3,3 0 1 1 19,12",
            ["limpar"] = "M6,26 L20,8 L26,14 L14,28 Z M10,21 L17,28 M4,29 H28",
            ["calc"] = "M9,5 L26,16 L9,27 Z",
            ["def"] = "M3,10 H29 M3,10 C10,28 22,28 29,10",
            ["normal"] = "M3,22 H29 M3,22 V10 H29 V22",
            ["cortante"] = "M3,16 H29 M3,16 V6 H16 V26 H29 V16",
            ["momento"] = "M3,8 H29 M3,8 Q16,40 29,8",
            ["verif"] = "M5,17 L12,24 L27,8 M3,29 H29",
            ["comparar"] = "M4,26 V14 H10 V26 M13,26 V8 H19 V26 M22,26 V18 H28 V26 M2,28 H30",
            ["reac"] = "M16,29 V9 M10,15 L16,8 L22,15 M6,3 H26",
            ["valores"] = "M5,10 H27 M5,16 H21 M5,22 H24",
            ["nomes"] = "M4,8 H20 M12,8 V24 M22,14 H28 M25,14 V24",
            ["tabela"] = "M4,6 H28 V26 H4 Z M4,12 H28 M4,19 H28 M13,6 V26",
            ["zoomtudo"] = "M4,10 V4 H10 M22,4 H28 V10 M28,22 V28 H22 M10,28 H4 V22 M11,11 H21 V21 H11 Z",
            ["zoommais"] = "M13,4 A9,9 0 1 0 13,22 A9,9 0 1 0 13,4 M19.5,19.5 L28,28 M9,13 H17 M13,9 V17",
            ["zoommenos"] = "M13,4 A9,9 0 1 0 13,22 A9,9 0 1 0 13,4 M19.5,19.5 L28,28 M9,13 H17",
            ["mais"] = "M16,6 V26 M6,16 H26",
            ["menos"] = "M6,16 H26",
            ["painel"] = "M4,5 H28 V27 H4 Z M11,5 V27 M21,5 V27",
            ["aplicar"] = "M5,17 L12,24 L27,8",
            ["assist"] = "M4,26 L14,16 M16,4 L18,10 L24,12 L18,14 L16,20 L14,14 L8,12 L14,10 Z",
            ["ajuda"] = "M16,3 A13,13 0 1 0 16,29 A13,13 0 1 0 16,3 M12,12 A4,4 0 1 1 17,16 V19 M16,23 V24",
            ["eixo"] = "M10,4 H22 M10,28 H22 M16,4 V28",
        };

        public static UIElement Icone(string nome, double tam = 32)
        {
            // icones coloridos preenchidos no estilo da faixa do Plant 3D (ver Icones.cs)
            return NovoFtool.Icones.Criar(nome, tam);
        }

        static F.ScreenTip Dica(string titulo, string texto, string atalho = null)
        {
            return new F.ScreenTip { Title = titulo + (atalho != null ? "  (" + atalho + ")" : ""), Text = texto, Width = 300 };
        }

        // O Fluent so respeita o tamanho com a "regra de tamanho": pequeno = icone de 16 px com o nome ao lado, em pilhas
        // de 3 (como Mover/Copiar/Esticar no AutoCAD); grande = icone de 32 px com o nome embaixo.
        static void Tamanho(DependencyObject b, bool pequeno)
        {
            F.RibbonProperties.SetSizeDefinition(b, new F.RibbonControlSizeDefinition(pequeno ? "Middle, Middle, Small" : "Large, Large, Large"));
        }

        F.Button Botao(string icone, string texto, Action acao, string dica, string atalho = null, bool pequeno = false, string keytip = null)
        {
            var b = new F.Button
            {
                Header = texto, LargeIcon = Icone(icone, 32), Icon = Icone(icone, 16),
                Size = pequeno ? F.RibbonControlSize.Middle : F.RibbonControlSize.Large, ToolTip = Dica(texto, dica, atalho)
            };
            Tamanho(b, pequeno);
            if (keytip != null) F.KeyTip.SetKeys(b, keytip);
            b.Click += (s, e) => { acao(); Vista.Focus(); };
            Reg(icone, texto, atalho, dica, acao);
            return b;
        }

        F.ToggleButton Alternar(string icone, string texto, Action<bool> acao, string dica, bool pequeno = false)
        {
            var b = new F.ToggleButton
            {
                Header = texto, LargeIcon = Icone(icone, 32), Icon = Icone(icone, 16),
                Size = pequeno ? F.RibbonControlSize.Middle : F.RibbonControlSize.Large, ToolTip = Dica(texto, dica)
            };
            Tamanho(b, pequeno);
            b.Click += (s, e) => { acao(b.IsChecked == true); Vista.Focus(); };
            Reg(icone, texto, null, dica, () => { b.IsChecked = !(b.IsChecked == true); acao(b.IsChecked == true); });
            return b;
        }

        F.ToggleButton BotaoFerramenta(string icone, string texto, Ferramenta f, string dica, string atalho = null, bool pequeno = false)
        {
            var b = new F.ToggleButton
            {
                Header = texto, LargeIcon = Icone(icone, 32), Icon = Icone(icone, 16), Tag = f,
                Size = pequeno ? F.RibbonControlSize.Middle : F.RibbonControlSize.Large, ToolTip = Dica(texto, dica, atalho)
            };
            Tamanho(b, pequeno);
            b.Click += (s, e) => { AcionarFerramenta(f); Vista.Focus(); };
            Reg(icone, texto, atalho, dica, () => AcionarFerramenta(f));
            if (!btnFerr.ContainsKey(f)) btnFerr[f] = new List<F.ToggleButton>();
            btnFerr[f].Add(b);
            return b;
        }

        F.ToggleButton BotaoDiagrama(string icone, string texto, Diagrama d, string dica, string atalho)
        {
            var b = new F.ToggleButton { Header = texto, LargeIcon = Icone(icone, 32), Icon = Icone(icone, 16), Size = F.RibbonControlSize.Large, ToolTip = Dica(texto, dica, atalho) };
            b.Click += (s, e) => { MostrarDiagrama(b.IsChecked == true ? d : Diagrama.Nenhum); Vista.Focus(); };
            Reg(icone, texto, atalho, dica, () => AlternarDiagrama(d));
            btnDiag[d] = b;
            return b;
        }

        F.ToggleButton BotaoCalor()
        {
            var b = new F.ToggleButton { Header = "Mapa de aproveitamento", LargeIcon = Icone("verif", 32), Icon = Icone("verif", 16), Size = F.RibbonControlSize.Middle, ToolTip = Dica("Mapa de aproveitamento", "Colore as barras pelo aproveitamento: verde até 75%, ciano até 90%, âmbar até 100%, vermelho acima, com selo ⚠/✕ nas críticas.", "V ou CALOR + Enter") };
            Tamanho(b, true);
            b.Click += (s, e) => { MostrarDiagrama(b.IsChecked == true ? Diagrama.Verificacao : Diagrama.Nenhum, b.IsChecked == true); Vista.Focus(); };
            Reg("verif", "Mapa de aproveitamento", "V + Enter", "Colore as barras pelo aproveitamento.", () => Executar("CALOR"));
            return b;
        }

        static F.RibbonGroupBox Grupo(string titulo, params object[] itens)
        {
            var g = new F.RibbonGroupBox { Header = titulo };
            foreach (var i in itens) g.Items.Add(i);
            return g;
        }

        F.RibbonTabItem Aba(string titulo, string keytip, params F.RibbonGroupBox[] grupos)
        {
            var t = new F.RibbonTabItem { Header = titulo };
            F.KeyTip.SetKeys(t, keytip);
            foreach (var g in grupos) t.Groups.Add(g);
            Faixa.Tabs.Add(t);
            return t;
        }

        F.TextBox Campo(string rotulo, string valor, string dica, double larg = 64)
        {
            return new F.TextBox { Header = rotulo, Text = valor, Width = larg + 78, ToolTip = Dica(rotulo, dica) };
        }

        // ================= faixa =================

        void MontarFaixa()
        {
            // barra de acesso rapido (Calcular fica sempre visivel aqui, nao em abas)
            var qNovo = Botao("novo", "Novo", Novo, "Cria um novo modelo em branco em uma nova aba.", "Ctrl+N");
            var qAbrir = Botao("abrir", "Abrir", AbrirDialogo, "Abre um arquivo de modelo (.estructto, .portico ou .ftl do Ftool).", "Ctrl+O");
            var qSalvar = Botao("salvar", "Salvar", () => Salvar(false), "Salva as alterações do modelo atual no arquivo.", "Ctrl+S");
            var qDesf = Botao("desfazer", "Desfazer", Desfazer, "Desfaz a última alteração feita no modelo.", "Ctrl+Z");
            var qRef = Botao("refazer", "Refazer", Refazer, "Refaz a última alteração desfeita.", "Ctrl+Y");
            var qCalc = Botao("calc", "Calcular", () => Resolver(true), "Calcula a estrutura. O cálculo também é refeito automaticamente a cada alteração.", "F5");
            foreach (var q in new[] { qNovo, qAbrir, qSalvar, qDesf, qRef, qCalc })
                Faixa.QuickAccessItems.Add(new F.QuickAccessMenuItem { Target = q, IsChecked = true });

            // Abas na ordem do trabalho (docs/REVISAO-TELAS.md 3.2): Modelo, Ações, Análise, Dimensionamento, Ligações e bases, Relatório, Vista.
            // MODELO: gerar, desenhar, modificar, barra continua, apoios e vinculos, secao e material
            cbSecao = new F.ComboBox { Header = "Seção", IsEditable = true, Width = 240, ToolTip = Dica("Seção", "Perfil do catálogo, ou digite: \"Ret 20x50\" (cm), \"Circ 30\" (cm), \"A=45,7 I=3437\" (cm², cm4), \"TC 48.3x3.7\" ou \"TR 100x50x3\" (mm).") };
            // familia (classe) filtra a lista de secoes: evita uma lista unica com todos os perfis
            cbFamilia = new F.ComboBox { Header = "Família", IsEditable = false, Width = 240, ToolTip = Dica("Família", "Tipo de perfil (W, tubos, cantoneiras, U...). Filtra a lista de seções ao lado.") };
            cbFamilia.Items.Add("Todas");
            foreach (string f in Biblioteca.Familias) cbFamilia.Items.Add(f);
            cbFamilia.SelectionChanged += (s, e) =>
            {
                string txt = cbSecao.Text;
                cbSecao.Items.Clear();
                string fam = cbFamilia.SelectedIndex > 0 ? (string)cbFamilia.SelectedItem : null;
                foreach (Secao sc in Biblioteca.Perfis) if (fam == null || sc.Familia == fam) cbSecao.Items.Add(sc.Nome);
                Secao at = Biblioteca.AcharSecao(txt);
                cbSecao.Text = fam == null || (at != null && at.Familia == fam) || cbSecao.Items.Count == 0 ? txt : (string)cbSecao.Items[0];
            };
            cbSecao.Text = Biblioteca.Perfis.Count > 0 ? "W 200 x 26,6" : "Ret 20x50";
            Secao ini = Biblioteca.AcharSecao(cbSecao.Text);
            cbFamilia.SelectedIndex = ini != null && ini.Familia != "" ? Math.Max(0, cbFamilia.Items.IndexOf(ini.Familia)) : 0;
            if (cbSecao.Items.Count == 0) foreach (Secao s in Biblioteca.Perfis) cbSecao.Items.Add(s.Nome);
            cbMaterial = new F.ComboBox { Header = "Material", IsEditable = true, Width = 240, ToolTip = Dica("Material", "Material da lista, ou digite \"E=25000\" (MPa).") };
            foreach (Material m in Biblioteca.Materiais) cbMaterial.Items.Add(m.Nome);
            cbMaterial.Text = Biblioteca.Materiais[0].Nome;
            abaModelo = Aba("Modelo", "M",
                Grupo("Gerar",
                    Botao("assist", "Gerar estrutura", () => AbrirAssistente(null), "Gera vigas contínuas, pórticos de vários andares, treliças e galpões a partir de poucas medidas.")),
                Grupo("Desenhar",
                    BotaoFerramenta("sel", "Selecionar", Ferramenta.Selecionar, "Clique num nó ou barra. Arraste da esquerda p/ direita = janela (só o que está dentro); da direita p/ esquerda = cruzamento (o que tocar). Shift/Ctrl soma.", "Esc"),
                    BotaoFerramenta("barra", "Barra", Ferramenta.Barra, "Clique os pontos em sequência. Digite um número para o comprimento exato na direção do mouse, ou x,y  @dx,dy  @L<ângulo. Botão direito ou Esc termina.", "B + Enter"),
                    BotaoFerramenta("no", "Nó", Ferramenta.No, "Insere um nó; se cair sobre uma barra, divide a barra.", "NO + Enter", true),
                    BotaoFerramenta("medir", "Medir", Ferramenta.Medir, "Mede a distância total, ΔX, ΔY e o ângulo entre dois pontos (encaixa nos nós e na grade). Clique no 1º e no 2º ponto; o 3º clique inicia nova medida e Esc cancela.", "Ctrl+M / DI", true)),
                Grupo("Modificar",
                    Botao("mover", "Mover", () => Transformar("mover", new[] { "MOVER" }), "Move os nós/barras selecionados por um deslocamento (dx, dy).", "MOVER dx dy", true),
                    Botao("copiar", "Copiar", () => Transformar("copiar", new[] { "COPIAR" }), "Copia as barras selecionadas, uma ou várias vezes (matriz linear), com apoios e cargas.", "COPIAR dx dy vezes", true),
                    Botao("espelhar", "Espelhar", () => Transformar("espelhar", new[] { "ESPELHAR" }), "Cria a cópia espelhada das barras selecionadas em relação a uma reta x = valor ou y = valor.", "ESPELHAR X valor", true),
                    Botao("dividir", "Dividir barra", DividirSelecao, "Divide as barras selecionadas em partes iguais.", "DIVIDIR", true),
                    Botao("apagar", "Apagar", Apagar, "Apaga os nós ou barras selecionados.", "Del", true)),
                Grupo("Barra contínua",
                    Botao("unir", "Unir trechos", UnirMembro, "Une os trechos selecionados numa barra contínua (uma peça só, mesmo dividida por nós): mesmo perfil, dimensionada junto. Selecione um trecho dela + barras novas para adicionar trechos.", "UNIR"),
                    Botao("separar", "Separar tudo", SepararMembro, "Desfaz a barra contínua: cada trecho volta a ser uma barra independente. Tirar trecho e Selecionar a barra ficam no menu do botão direito.", "SEPARAR", true)),
                Grupo("Apoios e vínculos",
                    BotaoFerramenta("engaste", "Engaste", Ferramenta.ApoioEngaste, "Trava X, Y e giro. Com nós selecionados aplica neles; senão, clique nos nós."),
                    BotaoFerramenta("rotula", "Fixo", Ferramenta.ApoioRotula, "Apoio de 2º gênero: trava X e Y."),
                    BotaoFerramenta("rolo", "Móvel", Ferramenta.ApoioRoloV, "Apoio de 1º gênero: trava só Y (rolete)."),
                    BotaoFerramenta("roloh", "Móvel horizontal", Ferramenta.ApoioRoloH, "Trava só o deslocamento horizontal (X).", null, true),
                    BotaoFerramenta("livre", "Sem apoio", Ferramenta.ApoioLivre, "Remove o apoio e as molas do nó.", null, true),
                    BotaoFerramenta("rotulab", "Rótula", Ferramenta.Rotula, "Clique perto da ponta da barra para pôr/tirar a rótula naquela ponta.", null, true)),
                Grupo("Seções e materiais",
                    cbFamilia, cbSecao, cbMaterial,
                    Botao("pincel", "Aplicar à seleção", () => AplicarSecao(false), "Aplica seção e material às barras selecionadas (ou, sem seleção, passa a valer para as próximas barras desenhadas).", null, true),
                    Botao("pincel", "Aplicar a todas", () => AplicarSecao(true), "Aplica seção e material a todas as barras.", null, true)));
            abaModelo.ToolTip = "Gerar e desenhar a estrutura, modificar, apoios e rótulas, seções e materiais, barras contínuas.";

            // ACOES (antes "Cargas"): casos de carga (inseridos por MontarCasosNaFaixa), cargas no no e na barra, peso proprio
            cFx = Campo("Fx (kN)", "0", "Positivo para a direita."); cFy = Campo("Fy (kN)", "0", "Positivo para cima. Gravidade = negativo."); cMz = Campo("Mz (kN·m)", "0", "Positivo anti-horário.");
            cQx = Campo("qx (kN/m)", "0", "Uniforme horizontal, por metro de barra."); cQy = Campo("qy (kN/m)", "0", "Uniforme vertical, por metro de barra. Gravidade = negativo.");
            cQi = Campo("q⊥ início", "0", "Perpendicular à barra, linear do início ao fim (kN/m)."); cQf = Campo("q⊥ fim", "0", "Perpendicular à barra no fim (kN/m).");
            tgPeso = Alternar("peso", "Peso próprio", v => { M.PesoProprio = v; Confirmar("Peso próprio " + (v ? "ligado" : "desligado") + "."); }, "Soma o peso das barras (massa do catálogo ou densidade × área).");
            var abaAcoes = Aba("Ações", "A",
                Grupo("Carga no nó", cFx, cFy, cMz,
                    BotaoFerramenta("cargano", "Aplicar no nó", Ferramenta.CargaNo, "Aplica Fx, Fy e Mz (substitui a carga existente). Com nós selecionados aplica neles; senão clique nos nós.")),
                Grupo("Carga distribuída", cQx, cQy, cQi, cQf,
                    BotaoFerramenta("cargadist", "Aplicar na barra", Ferramenta.CargaBarra, "Aplica as cargas distribuídas (substitui as existentes). Com barras selecionadas aplica nelas; senão clique nas barras.")),
                Grupo("Outras", tgPeso, Botao("limpar", "Limpar cargas", LimparCargas, "Remove as cargas dos itens selecionados, ou de todo o modelo se nada estiver selecionado.", null, true)));
            abaAcoes.ToolTip = "Casos de carga e combinações, forças e momentos nos nós, cargas distribuídas nas barras e peso próprio.";

            // ANALISE: resultado mostrado (inserido por MontarCasosNaFaixa), opcoes e diagramas
            tgTimo = Alternar("eixo", "Deformação por cortante", v => { M.Timoshenko = v; Confirmar("Timoshenko " + (v ? "ligado" : "desligado") + "."); }, "Viga de Timoshenko (considera a deformação por cortante). Desligado = Euler-Bernoulli.");
            abaAnalise = Aba("Análise", "N",
                Grupo("Opções", tgTimo),
                Grupo("Diagramas",
                    BotaoDiagrama("def", "Deformada", Diagrama.Deformada, "Configuração deformada (ampliada). Passe o mouse para ler os deslocamentos.", "D + Enter"),
                    BotaoDiagrama("normal", "Normal", Diagrama.Normal, "Esforço normal N (kN). Positivo = tração.", "N + Enter"),
                    BotaoDiagrama("cortante", "Cortante", Diagrama.Cortante, "Esforço cortante Q (kN).", "Q + Enter"),
                    BotaoDiagrama("momento", "Momento", Diagrama.Momento, "Momento fletor M (kN·m), desenhado do lado tracionado.", "M + Enter"),
                    Botao("mais", "Ampliar", () => Escala(1.4), "Aumenta a escala do diagrama ou da deformada.", "+", true),
                    Botao("menos", "Reduzir", () => Escala(1 / 1.4), "Diminui a escala do diagrama ou da deformada.", "-", true)));
            abaAnalise.ToolTip = "Resultado mostrado (caso, combinação ou envoltória), opções do modelo de análise e diagramas de esforços e deformada.";

            // DIMENSIONAMENTO: verificar, otimizar e criterios (o "n" global sai quando o motor Python entrar — REVISAO-TELAS 3.5)
            cSeg = Campo("Segurança", "2", "Coeficiente de segurança mínimo exigido nas verificações de barra e placa.", 48);
            cFlecha = Campo("Flecha L/", "250", "Flecha máxima admissível = vão / este número.", 48);
            cSeg.LostFocus += (s, e) => MudarCriterio(); cFlecha.LostFocus += (s, e) => MudarCriterio();
            cSeg.KeyDown += (s, e) => { if (e.Key == Key.Enter) MudarCriterio(); }; cFlecha.KeyDown += (s, e) => { if (e.Key == Key.Enter) MudarCriterio(); };
            abaDimens = Aba("Dimensionamento", "D",
                Grupo("Verificar",
                    (tgCalor = BotaoCalor()),
                    Botao("comparar", "Verificar barra", () => AbrirVerificacao(null), "Abre a verificação da barra selecionada com a lista de perfis da mesma família.", "COMPARAR", true)),
                Grupo("Otimizar",
                    Botao("dimens", "Dimensionar", () => DimensionarPerfis(false), "Troca cada barra pelo perfil mais leve da mesma família que passa na verificação (considera a envoltória). Recalcula e repete até estabilizar. Ctrl+Z desfaz.", "DIMENSIONAR")),
                Grupo("Critérios", cSeg, cFlecha));
            abaDimens.ToolTip = "Mapa de aproveitamento, verificação de cada barra, perfil mais leve que passa e critérios de segurança e flecha.";

            // LIGACOES E BASES
            var abaLigacoes = Aba("Ligações e bases", "L",
                Grupo("Bases",
                    BotaoFerramenta("placa", "Placa de base", Ferramenta.Placa, "Placa de base com chumbadores no pé do pilar (nó onde chega uma só barra). Com um nó selecionado, abre a verificação da placa dele.")),
                Grupo("Solda", CriarCbEletrodo()));
            abaLigacoes.ToolTip = "Placas de base com chumbadores e eletrodo das soldas das barras.";

            // RELATORIO
            var abaRelatorio = Aba("Relatório", "R",
                Grupo("Memorial",
                    Botao("relatorio", "Relatório", AbrirRelatorio, "Memorial com modelo, casos de carga, combinações, diagramas, reações, esforços, verificação e placas. Imprime ou salva em PDF (impressora \"Microsoft Print to PDF\").", "Ctrl+P")),
                Grupo("Exportar",
                    Botao("exportar", "Exportar CSV", ExportarCsv, "Exporta nós, barras, reações, esforços e verificação para CSV (abre no Excel).", "Ctrl+E")));
            abaRelatorio.ToolTip = "Memorial de cálculo (imprimir ou PDF) e exportação dos resultados.";
            MontarCasosNaFaixa(abaAcoes, abaAnalise);

            // VISTA
            tgReac = Alternar("reac", "Reações", v => { Vista.MostrarReacoes = v; Vista.InvalidateVisual(); }, "Mostra as reações de apoio.", true);
            tgValores = Alternar("valores", "Valores", v => { Vista.MostrarValores = v; Vista.InvalidateVisual(); }, "Escreve os valores nos diagramas.", true);
            tgCargas = Alternar("cargano", "Cargas", v => { Vista.MostrarCargas = v; Vista.InvalidateVisual(); }, "Mostra as cargas no desenho.", true);
            tgNomes = Alternar("nomes", "Nomes", v => { Vista.MostrarNomes = v; Vista.InvalidateVisual(); }, "Escreve perfil e comprimento ao lado das barras.", true);
            tgValores.IsChecked = true; tgCargas.IsChecked = true; tgReac.IsChecked = true; tgNomes.IsChecked = true;   // tudo visivel por padrao
            cGrade = Campo("Passo da grade (m)", "0,5", "Espaçamento da grade de desenho.", 50);
            cGrade.LostFocus += (s, e) => MudarGrade(cGrade.Text);
            cGrade.KeyDown += (s, e) => { if (e.Key == Key.Enter) MudarGrade(cGrade.Text); };
            var abaVista = Aba("Vista", "V",
                Grupo("Navegar",
                    Botao("zoomtudo", "Enquadrar", () => Vista.ZoomTudo(), "Mostra o modelo inteiro (ou duplo clique na roda do mouse).", "F4"),
                    Botao("zoommais", "Aproximar", () => Vista.ZoomFator(1.4), "Aproxima a visualização em torno do centro da tela (ou use a roda do mouse).", null, true),
                    Botao("zoommenos", "Afastar", () => Vista.ZoomFator(1 / 1.4), "Afasta a visualização para enxergar uma área maior do modelo.", null, true)),
                Grupo("Mostrar", tgReac, tgValores, tgCargas, tgNomes),
                Grupo("Grade", cGrade),
                Grupo("Painéis",
                    Botao("painel", "Projeto", () => Alternar(PainelProjeto), "Mostra/oculta o painel Projeto.", null, true),
                    Botao("painel", "Propriedades", () => Alternar(PainelProp), "Mostra/oculta o painel Propriedades.", null, true),
                    Botao("tabela", "Tabelas", () => Alternar(PainelTabelas), "Mostra/oculta as tabelas de nós, barras e verificação.", "TABELAS", true)),
                Grupo("Ajuda", Botao("ajuda", "Atalhos e ajuda", MostrarAjuda, "Como usar e lista de atalhos.", "F1")));
            abaVista.ToolTip = "Zoom, o que aparece no desenho (reações, valores, cargas, nomes), passo da grade, painéis e ajuda.";

            // abas contextuais (aparecem com a selecao)
            ctxBarra = new F.RibbonTabItem { Header = "Barra", Group = CtxBarra, ToolTip = "Ações rápidas para as barras selecionadas." };
            tgEixoFraco = Alternar("eixo", "Perfil deitado", v => { foreach (Barra b in Vista.Sel.OfType<Barra>()) b.EixoFraco = v; SincronizarMembros(Vista.Sel.OfType<Barra>()); Confirmar(v ? "Perfil deitado: menor inércia (Iy) no plano." : "Perfil em pé: maior inércia (Ix) no plano."); }, "Ligado = perfil deitado: a menor inércia (Iy) fica no plano do pórtico. Desligado = em pé (Ix no plano). Fora do plano vale o outro eixo.");
            ctxBarra.Groups.Add(Grupo("Verificação",
                Botao("comparar", "Verificar barra", () => AbrirVerificacao(Vista.Sel.OfType<Barra>().FirstOrDefault()), "Abre a verificação detalhada da barra selecionada e compara perfis alternativos da família."),
                Botao("dimens", "Dimensionar", () => DimensionarPerfis(true), "Perfil mais leve da família que passa, só nas barras selecionadas.", "DIMSEL", true), tgEixoFraco));
            ctxBarra.Groups.Add(Grupo("Ligações",
                Botao("rotulab", "Rótula no início", () => RotularSel(true, false), "Liga/desliga a rótula no início das barras selecionadas.", null, true),
                Botao("rotulab", "Rótula no fim", () => RotularSel(false, true), "Liga/desliga a rótula no fim das barras selecionadas.", null, true),
                Botao("rotulab", "Nas duas pontas", () => RotularSel(true, true), "Barra de treliça: liga/desliga rótula nas duas pontas.", null, true)));
            ctxBarra.Groups.Add(Grupo("Modificar",
                Botao("dividir", "Dividir", DividirSelecao, "Divide cada barra selecionada em partes iguais.", null, true),
                Botao("apagar", "Apagar", Apagar, "Apaga as barras selecionadas.", "Del", true)));
            Faixa.Tabs.Add(ctxBarra);

            ctxNo = new F.RibbonTabItem { Header = "Nó", Group = CtxNo, ToolTip = "Ações rápidas para os nós selecionados." };
            ctxNo.Groups.Add(Grupo("Apoio",
                Botao("engaste", "Engaste", () => AplicarApoio(Ferramenta.ApoioEngaste, Vista.Sel.OfType<No>()), "Engasta os nós selecionados (trava X, Y e giro)."),
                Botao("rotula", "Fixo", () => AplicarApoio(Ferramenta.ApoioRotula, Vista.Sel.OfType<No>()), "Apoio fixo de 2º gênero (trava X e Y, giro livre)."),
                Botao("rolo", "Móvel", () => AplicarApoio(Ferramenta.ApoioRoloV, Vista.Sel.OfType<No>()), "Apoio móvel de 1º gênero (trava Y)."),
                Botao("livre", "Sem apoio", () => AplicarApoio(Ferramenta.ApoioLivre, Vista.Sel.OfType<No>()), "Remove o apoio, as molas e a placa de base dos nós selecionados.", null, true)));
            ctxNo.Groups.Add(Grupo("Carga e placa",
                Botao("cargano", "Aplicar carga", () => AplicarCargaNo(Vista.Sel.OfType<No>()), "Aplica Fx/Fy/Mz da aba Ações aos nós selecionados."),
                Botao("placa", "Placa de base", () => AbrirPlaca(Vista.Sel.OfType<No>().FirstOrDefault()), "Configura e verifica a placa de base com chumbadores/parafusos.", null, true)));
            ctxNo.Groups.Add(Grupo("Modificar", Botao("apagar", "Apagar", Apagar, "Apaga os nós selecionados e as barras ligadas a eles.", "Del")));
            Faixa.Tabs.Add(ctxNo);

            Faixa.SelectedTabItem = abaModelo;
        }

        // A aba do documento do AvalonDock e substituida pela barra de arquivos de largura toda (padrao Plant 3D).
        void EsconderAbaDocumento()
        {
            RedesenharAbas();
            void Ir(DependencyObject d)
            {
                if (d.GetType().Name == "DocumentPaneTabPanel" && VisualTreeHelper.GetParent(d) is FrameworkElement pai)
                {
                    // some so a linha do cabecalho (abas + botao de lista); o conteudo continua
                    pai.Visibility = Visibility.Collapsed;
                    return;
                }
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) Ir(VisualTreeHelper.GetChild(d, i));
            }
            Ir(Docas);
        }

        F.ComboBox cbEletrodo;
        F.ComboBox CriarCbEletrodo()
        {
            cbEletrodo = new F.ComboBox { Header = "Eletrodo", IsEditable = false, Width = 140, ToolTip = Dica("Eletrodo das soldas", "Eletrodo AWS das soldas de filete nas pontas das barras (aba Soldas das tabelas). Tensão admissível: E60 124 · E70 145 · E80 165 · E90 186 · E100 207 · E110 228 · E120 248 MPa.") };
            foreach (string e in Solda.Eletrodos) cbEletrodo.Items.Add(e + "XX");
            cbEletrodo.SelectedIndex = 1;
            cbEletrodo.SelectionChanged += (s, e) =>
            {
                if (cbEletrodo.SelectedIndex < 0) return;
                string el = Solda.Eletrodos[cbEletrodo.SelectedIndex];
                if ((string.IsNullOrEmpty(M.Eletrodo) ? "E70" : M.Eletrodo) == el) return;
                M.Eletrodo = el; Confirmar("Eletrodo das soldas: " + el + "XX.");
            };
            return cbEletrodo;
        }

        static void Alternar(AvalonDock.Layout.LayoutAnchorable p) { if (p.IsVisible) p.Hide(); else p.Show(); }

        // ================= bastidores (menu Arquivo) =================

        F.BackstageTabItem bsRecentes;

        void MontarBastidores()
        {
            BsAbas.Items.Clear();
            BsAbas.Items.Add(BotaoBs("Novo", "Ctrl+N", Novo, "Cria um novo modelo em branco em uma nova aba."));
            BsAbas.Items.Add(BotaoBs("Abrir...", "Ctrl+O", AbrirDialogo, "Abre um modelo salvo (.estructto, .portico) ou importa um arquivo .ftl do Ftool."));
            BsAbas.Items.Add(BotaoBs("Salvar", "Ctrl+S", () => Salvar(false), "Salva as alterações do modelo atual no disco."));
            BsAbas.Items.Add(BotaoBs("Salvar como...", "Ctrl+Shift+S", () => Salvar(true), "Salva uma cópia do modelo atual com outro nome ou em outra pasta."));
            BsAbas.Items.Add(new F.SeparatorTabItem());
            bsRecentes = new F.BackstageTabItem { Header = "Recentes", ToolTip = "Lista os últimos arquivos de modelo abertos para acesso rápido." };
            BsAbas.Items.Add(bsRecentes);
            BsAbas.Items.Add(new F.SeparatorTabItem());
            BsAbas.Items.Add(BotaoBs("Importar .ftl do Ftool...", null, ImportarDialogo, "Importa a geometria, seções, materiais e casos de carga de um arquivo .ftl do Ftool original."));
            BsAbas.Items.Add(new F.SeparatorTabItem());
            BsAbas.Items.Add(BotaoBs("Ajuda e atalhos", "F1", MostrarAjuda, "Abre a janela de ajuda com o passo a passo, convenções de sinais e lista de atalhos."));
            BsAbas.Items.Add(BotaoBs("Sair", "Alt+F4", Close, "Fecha o programa (pergunta antes caso existam alterações não salvas)."));
            BsAbas.SelectedItem = bsRecentes;
            Bastidor.IsOpenChanged += (s, e) => { if (Bastidor.IsOpen) AtualizarRecentesBs(); };
        }

        F.Button BotaoBs(string texto, string atalho, Action acao, string dica = null)
        {
            var b = new F.Button { Header = texto, ToolTip = dica != null ? (object)Dica(texto, dica, atalho) : atalho };
            b.Click += (s, e) => { Bastidor.IsOpen = false; acao(); };
            return b;
        }

        internal void MostrarRecentesBs() { Bastidor.IsOpen = true; AtualizarRecentesBs(); BsAbas.SelectedItem = bsRecentes; }

        void AtualizarRecentesBs()
        {
            var sp = new StackPanel { Margin = new Thickness(30, 20, 30, 20), MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left };
            sp.Children.Add(new TextBlock { Text = "Arquivos recentes", FontSize = 22, FontWeight = FontWeights.Light, Margin = new Thickness(0, 0, 0, 14) });
            var lista = Config.Recentes();
            if (lista.Count == 0) sp.Children.Add(new TextBlock { Text = "Nenhum arquivo aberto ainda.", Foreground = TemaPlant.BTexto3 });
            foreach (string c in lista) { var ca = CartaoRecente(c); ca.Margin = new Thickness(0, 0, 0, 8); sp.Children.Add(ca); }
            bsRecentes.Content = new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        Button Cartao(string titulo, string sub, Action acao, string icone = null, string dica = null)
        {
            var dp = new DockPanel();
            if (icone != null) { var ic = Icone(icone, 26); DockPanel.SetDock(ic, Dock.Left); ((FrameworkElement)ic).Margin = new Thickness(0, 0, 12, 0); dp.Children.Add(ic); }
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = titulo, FontSize = 14, Foreground = TemaPlant.BTexto1 });
            if (sub != null) sp.Children.Add(new TextBlock { Text = sub, FontSize = 11, Foreground = TemaPlant.BTexto2, TextWrapping = TextWrapping.Wrap });
            dp.Children.Add(sp);
            var b = new Button { Content = dp, Style = (Style)FindResource("Cartao"), HorizontalContentAlignment = HorizontalAlignment.Stretch, ToolTip = dica ?? (sub != null ? titulo + " — " + sub : titulo) };
            b.Click += (s, e) => acao();
            return b;
        }

        // ================= status =================

        // Rodape no padrao do Plant 3D: botoes-icone agrupados em blocos #3B4453; ligado = icone azul.
        StackPanel blocoStatus;

        void MontarStatus()
        {
            NovoBloco();
            stAtrair = StatusBtn("M10,3 V17 M3,10 H17 M3,3 H6 M14,3 H17 M3,17 H6 M14,17 H17 M3,3 V6 M17,3 V6 M3,14 V17 M17,14 V17", "Snap — prender o cursor à grade (F9)", v => M.AtrairGrade = v);
            stGrade = StatusBtn("M2,2 H18 V18 H2 Z M2,8 H18 M2,13 H18 M8,2 V18 M13,2 V18", "Grade — mostrar a grade (F7)", v => { M.MostrarGrade = v; Vista.InvalidateVisual(); });
            NovoBloco();
            stOrto = StatusBtn("M4,3 V16 H17", "Orto — só horizontal/vertical (F8, ou segure Shift)", v => Vista.Orto = v);
            stPolar = StatusBtn("M3,17 H18 M3,17 L15,5 M10,17 A7,7 0 0 0 8.2,12.2", "Polar — prende em 0°/90° quando perto (F10)", v => Vista.Polar = v); stPolar.IsChecked = true;
            stObj = StatusBtn("M6,6 H14 V14 H6 Z M10,1 V6 M10,14 V19 M1,10 H6 M14,10 H19", "Atração a objetos (F3) — botão direito escolhe: nó, meio, interseção, perpendicular, sobre a barra, extensão", v => Vista.AtrairObjetos = v); stObj.IsChecked = true;
            stObj.ContextMenu = MenuSnaps();
            stRastro = StatusBtn("M10,2 V18 M2,10 H18 M7,10 A3,3 0 1 0 13,10 A3,3 0 1 0 7,10", "Rastreamento (F11) — passe o mouse num nó e afaste: aparecem linhas alinhadas com ele (horizontal/vertical)", v => { Vista.Rastrear = v; if (!v) Vista.LimparRastreio(); }); stRastro.IsChecked = true;
            NovoBloco();
            var tela = new Button { Content = IconeStatus("M2,7 V2 H7 M13,2 H18 V7 M18,13 V18 H13 M7,18 H2 V13"), ToolTip = "Enquadrar o modelo (F4)", Style = EstiloBotaoStatus() };
            tela.Click += (s, e) => Vista.ZoomTudo();
            blocoStatus.Children.Add(tela);
        }

        void NovoBloco()
        {
            // grupos separados por um divisor fino (sem caixas)
            if (StatusBotoes.Children.Count > 0)
                StatusBotoes.Children.Add(new Border { Width = 1, Height = 14, Background = TemaPlant.BBordaSutil, Margin = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
            blocoStatus = StatusBotoes;
        }

        static System.Windows.Shapes.Path IconeStatus(string d)
        {
            return new System.Windows.Shapes.Path { Data = Geometry.Parse(d), StrokeThickness = 1.6, Width = 18, Height = 18, Stretch = Stretch.Uniform, StrokeLineJoin = PenLineJoin.Round, StrokeEndLineCap = PenLineCap.Round, StrokeStartLineCap = PenLineCap.Round,
                Stroke = TemaPlant.BTexto2 };
        }

        static Style estiloStatus;
        static Style EstiloBotaoStatus()
        {
            if (estiloStatus != null) return estiloStatus;
            // botao/alternador do rodape: sem moldura; mouse por cima = fundo mais claro; ligado = icone azul
            var t = new ControlTemplate(typeof(ButtonBase));
            var bd = new FrameworkElementFactory(typeof(Border), "b");
            bd.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            bd.SetValue(Border.PaddingProperty, new Thickness(5, 2, 5, 2));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter)); bd.AppendChild(cp);
            t.VisualTree = bd;
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, TemaPlant.B(TemaPlant.Hover), "b"));
            t.Triggers.Add(hover);
            estiloStatus = new Style(typeof(ButtonBase));
            estiloStatus.Setters.Add(new Setter(Control.TemplateProperty, t));
            estiloStatus.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
            return estiloStatus;
        }

        System.Windows.Controls.Primitives.ToggleButton stRastro;

        // modos de atracao a objetos, um a um (como o menu do OSNAP)
        ContextMenu MenuSnaps()
        {
            var cm = new ContextMenu();
            void Item(string nome, string dica, Func<bool> ler, Action<bool> por)
            {
                var mi = new MenuItem { Header = nome, ToolTip = dica, IsCheckable = true, IsChecked = ler(), StaysOpenOnClick = true };
                mi.Click += (s, e) => { por(mi.IsChecked); Vista.InvalidateVisual(); };
                cm.Items.Add(mi);
            }
            Item("Nó (extremidade)", "Encaixa o cursor nos nós existentes nas extremidades das barras.", () => Vista.SnapNo, v => Vista.SnapNo = v);
            Item("Meio da barra", "Encaixa o cursor exatamente no ponto médio de uma barra.", () => Vista.SnapMeio, v => Vista.SnapMeio = v);
            Item("Interseção", "Encaixa o cursor no ponto de cruzamento geométrico entre duas barras.", () => Vista.SnapIntersecao, v => Vista.SnapIntersecao = v);
            Item("Perpendicular", "Ao desenhar ou medir a partir de um ponto, encaixa no pé da perpendicular sobre outra barra.", () => Vista.SnapPerpendicular, v => Vista.SnapPerpendicular = v);
            Item("Sobre a barra (mais próximo)", "Encaixa o cursor no ponto mais próximo ao longo do eixo de uma barra.", () => Vista.SnapProximo, v => Vista.SnapProximo = v);
            Item("Extensão", "Encaixa o cursor na linha de prolongamento além da extremidade de uma barra.", () => Vista.SnapExtensao, v => Vista.SnapExtensao = v);
            return cm;
        }

        // Pilula alternavel do rodape: icone + nome; ligada = fundo de acento suave, borda e texto de destaque.
        static Style estiloPilula;
        static Style EstiloPilula()
        {
            if (estiloPilula != null) return estiloPilula;
            var t = new ControlTemplate(typeof(System.Windows.Controls.Primitives.ToggleButton));
            var bd = new FrameworkElementFactory(typeof(Border), "b");
            bd.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            bd.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
            bd.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
            bd.SetValue(Border.PaddingProperty, new Thickness(7, 2, 8, 2));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter)); cp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); bd.AppendChild(cp);
            t.VisualTree = bd;
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, TemaPlant.BSup3, "b"));
            t.Triggers.Add(hover);
            var lig = new Trigger { Property = System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, Value = true };
            lig.Setters.Add(new Setter(Border.BackgroundProperty, TemaPlant.BAcentoSutil, "b"));
            lig.Setters.Add(new Setter(Border.BorderBrushProperty, TemaPlant.B(TemaPlant.Acento, 0x99), "b"));
            t.Triggers.Add(lig);
            estiloPilula = new Style(typeof(System.Windows.Controls.Primitives.ToggleButton));
            estiloPilula.Setters.Add(new Setter(Control.TemplateProperty, t));
            estiloPilula.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
            estiloPilula.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
            return estiloPilula;
        }

        System.Windows.Controls.Primitives.ToggleButton StatusBtn(string icone, string dica, Action<bool> acao)
        {
            string nome = dica.StartsWith("Atração") ? "Objetos" : dica.StartsWith("Rastreamento") ? "Rastreio" : dica.Split(' ')[0];
            var ic = IconeStatus(icone); ic.Width = 14; ic.Height = 14; ic.StrokeThickness = 1.7;
            var tx = new TextBlock { Text = nome, FontSize = 11, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = TemaPlant.BTexto2 };
            var sp = new StackPanel { Orientation = Orientation.Horizontal }; sp.Children.Add(ic); sp.Children.Add(tx);
            var b = new System.Windows.Controls.Primitives.ToggleButton { Content = sp, ToolTip = dica, Style = EstiloPilula() };
            System.Windows.Automation.AutomationProperties.SetName(b, nome);
            void Cor() { var c = b.IsChecked == true ? TemaPlant.BRealce : TemaPlant.BTexto2; ic.Stroke = c; tx.Foreground = c; }
            b.Checked += (s, e) => Cor(); b.Unchecked += (s, e) => Cor(); Cor();
            b.Click += (s, e) => { acao(b.IsChecked == true); Vista.Focus(); };
            blocoStatus.Children.Add(b);
            return b;
        }

        void AlternarStatus(System.Windows.Controls.Primitives.ToggleButton b, Action<bool> acao, string nome)
        {
            b.IsChecked = b.IsChecked != true;
            acao(b.IsChecked == true);
            Log(nome + (b.IsChecked == true ? " ligado." : " desligado."));
        }

        // Botao do aplicativo (canto superior esquerdo): selo vermelho com a letra do programa + etiqueta, como o "A P3D".
        object BotaoAplicativo()
        {
            Bastidor.Background = TemaPlant.B(TemaPlant.Titulo);
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(new Image { Source = TemaPlant.Selo(), Width = 22, Height = 22, VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(new TextBlock { Text = " ▾", Foreground = TemaPlant.B(TemaPlant.Texto), FontSize = 10, VerticalAlignment = VerticalAlignment.Center });
            return sp;
        }

        // Area de trabalho do primeiro monitor que nao e o principal (so para o modo de testes --tela2). Sem WinForms: user32.
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] struct RectI { public int L, T, R, B; }
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] struct InfoMon { public int Tam; public RectI Mon, Trab; public int Flags; }
        delegate bool EnumMon(IntPtr h, IntPtr dc, ref RectI r, IntPtr dados);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, EnumMon cb, IntPtr dados);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr h, ref InfoMon info);

        static Rect? OutroMonitor()
        {
            Rect? r = null;
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr dc, ref RectI rc, IntPtr d) =>
            {
                var info = new InfoMon { Tam = System.Runtime.InteropServices.Marshal.SizeOf(typeof(InfoMon)) };
                if (GetMonitorInfo(h, ref info) && (info.Flags & 1) == 0 && r == null)
                    r = new Rect(info.Trab.L, info.Trab.T, info.Trab.R - info.Trab.L, info.Trab.B - info.Trab.T);
                return true;
            }, IntPtr.Zero);
            return r;
        }
    }
}
