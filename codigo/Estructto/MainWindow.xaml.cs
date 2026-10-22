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
        F.ToggleButton tgCargas, tgCargasTopo, tgSombra;
        System.Windows.Controls.Primitives.ToggleButton stGrade, stAtrair, stOrto, stPolar, stObj;
        F.TextBox cGrade;
        internal HudVista Hud;
        F.ToggleButton tgCalor;
        internal F.RibbonTabItem abaModelo, abaAnalise, abaDimens;

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
            ArqNovo.Click += (s, e) => NovoProjeto();
            Loaded += (s, e) => Dispatcher.BeginInvoke(new Action(EsconderAbaDocumento), System.Windows.Threading.DispatcherPriority.Loaded);
            var selo = TemaPlant.Selo(24);
            Loaded += (s, e) => Dispatcher.BeginInvoke(new Action(() => TemaPlant.AjustarFaixa(this, selo)), System.Windows.Threading.DispatcherPriority.Loaded);
            Faixa.SelectedTabChanged += (s, e) => Dispatcher.BeginInvoke(new Action(() => TemaPlant.AjustarFaixa(Faixa, null)), System.Windows.Threading.DispatcherPriority.Loaded);
            TemaPlant.AplicarDock(Docas);
            Bastidor.Header = "Arquivo";                               // o selo do app fica na barra de titulo (como o "A P3D")
            Bastidor.Background = TemaPlant.B(TemaPlant.Abas);              // "Arquivo" como mais uma aba, sem botao colorido
            Comando.TextChanged += (s, e) => DicaComando.Visibility = Comando.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            MontarFaixa();
            MontarBastidores();
            MontarStatus();
            MontarEstiloVisual();   // seletor na vista e chip na barra de status (SeletorEstilo.cs)
            LigarEventos();
            // animacoes da interface (Anim.cs, docs/ANIMACOES.md)
            Anim.Iniciar(Faixa, Insp);
            Faixa.SelectedTabChanged += (s, e) => { if (Faixa.SelectedTabItem != null) foreach (var g in Faixa.SelectedTabItem.Groups) Anim.Entrar(g, 0, Anim.Rapido); };
            Anim.AoMostrar(Aviso, 8);
            foreach (var la in AvalonDock.Layout.Extensions.Descendents(Docas.Layout).OfType<AvalonDock.Layout.LayoutAnchorable>()) Anim.AoMostrar(la.Content as UIElement, 0, Anim.Rapido);
            Loaded += (s, e) => Bastidor.AreAnimationsEnabled = Anim.Ligado;
            Hud = new HudVista(Vista, (d, calor) => MostrarDiagrama(d, calor), () => { try { return Vista.Resolvido ? "Mostrando: " + NomeResultado() : ""; } catch { return ""; } }, f => Escala(f));
            GridVista.Children.Add(Hud);
            Vista.SizeChanged += (s, e) => Hud.Compacto = Vista.ActualHeight < 340;
            HistBtn.Click += (s, e) => { bool abrir = HistoricoCaixa.Visibility != Visibility.Visible; HistoricoCaixa.Visibility = abrir ? Visibility.Visible : Visibility.Collapsed; HistBtn.Content = abrir ? "▾" : "▴"; Comando.Focus(); };
            NovoModelo(new Modelo(), null);
            Loaded += (s, e) => { Vista.ZoomTudo(); FocarVista(); };
            // recuperação só depois da janela aparecer: diálogo antes do primeiro quadro deixava a janela principal invisível
            ContentRendered += (s, e) => { if (!Capturas.Ativo && !Environment.GetCommandLineArgs().Any(a => a.StartsWith("--teste-"))) Dispatcher.BeginInvoke(new Action(OferecerRecuperacao), System.Windows.Threading.DispatcherPriority.ApplicationIdle); };   // execução automática: sem diálogo modal (travava o --teste-motor quando havia cópia de recuperação)
            ContentRendered += (s, e) => { if (!Capturas.Ativo && !Environment.GetCommandLineArgs().Any(a => a.StartsWith("--teste"))) Dispatcher.BeginInvoke(new Action(IniciarChecagemAtualizacao), System.Windows.Threading.DispatcherPriority.ApplicationIdle); };   // AtualizadorUi.cs: em segundo plano, falha silenciosa
            Log("Bem-vindo. Escolha uma ferramenta na faixa, use Modelo › Gerar estrutura ou digite um comando (AJUDA lista todos). F1 = atalhos.", false);

            string[] args = Environment.GetCommandLineArgs();
            Loaded += (s, e) => AplicarModoEspacial();   // só existe vista 3D (docs/MODELO-ESPACIAL.md)
            AbaSoldas.Visibility = Visibility.Collapsed;   // soldas das pontas das barras eram do cálculo 2D (Nucleo.cs)
            if (Capturas.Configurar(this, args)) return;
            string arqInicial = args.Skip(1).FirstOrDefault(a => !a.StartsWith("--") && File.Exists(a));
            if (arqInicial != null)
                Loaded += (s, e) => Dispatcher.BeginInvoke(new Action(() => Abrir(arqInicial)), System.Windows.Threading.DispatcherPriority.Loaded);
            else if (!args.Any(a => a.StartsWith("--")))   // como o AutoCAD: abre com um desenho em branco (em planta, no térreo)
                Loaded += (s, e) => Dispatcher.BeginInvoke(new Action(() => _ = NovoEmBranco(true)), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            foreach (string a in args)   // --teste-motor=<saida.png>: Verificação de barras ao vivo pelo botão da faixa (FaixaMotor.cs)
                if (a.StartsWith("--teste-motor=")) Loaded += (s, e) => Dispatcher.BeginInvoke(new Action(() => TesteMotor(a.Substring(14).Trim('"'))), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
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
            ["juntar"] = "M3,16 H13 M19,16 H29 M13,11 V21 M19,11 V21 M13,16 H19",
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
            b.Click += (s, e) => { acao(); FocarVista(); };
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
            b.Click += (s, e) => { acao(b.IsChecked == true); FocarVista(); };
            Reg(icone, texto, null, dica, () => { b.IsChecked = !(b.IsChecked == true); acao(b.IsChecked == true); });
            return b;
        }

        F.ToggleButton BotaoDiagrama(string icone, string texto, Diagrama d, string dica, string atalho)
        {
            var b = new F.ToggleButton { Header = texto, LargeIcon = Icone(icone, 32), Icon = Icone(icone, 16), Size = F.RibbonControlSize.Large, ToolTip = Dica(texto, dica, atalho) };
            b.Click += (s, e) => { MostrarDiagrama(b.IsChecked == true ? d : Diagrama.Nenhum); FocarVista(); };
            Reg(icone, texto, atalho, dica, () => AlternarDiagrama(d));
            btnDiag[d] = b;
            return b;
        }

        F.ToggleButton BotaoCalor()
        {
            var b = new F.ToggleButton { Header = "Mapa de aproveitamento", LargeIcon = Icone("verif", 32), Icon = Icone("verif", 16), Size = F.RibbonControlSize.Middle, ToolTip = Dica("Mapa de aproveitamento", "Colore as barras pelo aproveitamento: verde até 75%, ciano até 90%, âmbar até 100%, vermelho acima, com selo ⚠/✕ nas críticas.", "V ou CALOR + Enter") };
            Tamanho(b, true);
            b.Click += (s, e) => { MostrarDiagrama(b.IsChecked == true ? Diagrama.Verificacao : Diagrama.Nenhum, b.IsChecked == true); FocarVista(); };
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
            var qNovo = Botao("novo", "Novo", () => NovoProjeto(), "Novo projeto 3D: modelo, dados e grade de eixos e níveis.", "Ctrl+N");
            var qAbrir = Botao("abrir", "Abrir", AbrirDialogo, "Abre .estructto (os antigos, 2D, são convertidos para 3D), .json do motor, .ed3 do CYPE ou .ftl do Ftool.", "Ctrl+O");
            var qSalvar = Botao("salvar", "Salvar", () => Salvar(false), "Salva no formato .estructto (modelo do motor + placas de base + grade).", "Ctrl+S");
            var qDesf = Botao("desfazer", "Desfazer", Desfazer, "Desfaz a última alteração feita no modelo.", "Ctrl+Z / U");
            var qRef = Botao("refazer", "Refazer", Refazer, "Refaz a última alteração desfeita.", "Ctrl+Y");
            var qCalc = Botao("calc", "Calcular", Calcular, "Calcula no motor Python: combinações NBR 8681 e verificação por norma.", "F5");
            foreach (var q in new[] { qNovo, qAbrir, qSalvar, qDesf, qRef, qCalc })
                Faixa.QuickAccessItems.Add(new F.QuickAccessMenuItem { Target = q, IsChecked = true });

            // Abas na ordem do trabalho (docs/REVISAO-TELAS.md 3.2): Modelo, Ações, Análise, Dimensionamento, Ligações e bases, Relatório, Vista.
            // MODELO, no fluxo de estruturas do Plant 3D (docs/v3/PESQUISA-PLANT.md 1): Grade, Barras (Member), Chapas (Plate),
            // Modificar (Edit/Cut/Extend/Trim) e Configurações (Member Settings: seção, material, giro, ponto cardinal).
            abaModelo = Aba("Modelo", "M",
                Grupo("Gerar",
                    Botao("3d", "Gerar 3D", () => AbrirGerador3D(), "Edifício, galpão ou pórtico gerado pelo motor (abre numa aba nova)."),
                    Botao("assist", "Assistente", () => AbrirAssistente(null), "Viga contínua, pórtico, treliça ou galpão a partir de poucas medidas, com prévia; o resultado abre em 3D.", null, true),
                    Botao("cype", "Importar CYPE", () => ComandosAnalise.AbrirCype(this), "Importa uma obra do CYPE 3D (.ed3) ou compara os resultados com os do CYPE.", null, true)));
            var planos = GrupoPlanos();   // níveis e planos de trabalho (PlanosTrabalho.cs) + a grade estrutural (GR)
            planos.Items.Insert(0, Botao("eixos", "Grade", () => ExecutarComando("GRADE"), "Grade estrutural como no Plant 3D: eixos em X (1, 2, 3…), filas em Y (A, B, C…) e níveis (0,@6,@6). Os cruzamentos atraem o cursor; cada nível vira um plano de trabalho (PgUp/PgDn).", "GR", true));
            abaModelo.Groups.Add(planos);
            MontarDesenho3D();                       // Barras (Selecionar, Barra, Pilar, Medir) e Configurações (Desenho3D.cs)
            abaModelo.Groups.Insert(abaModelo.Groups.Count - 1, Grupo("Chapas",
                Botao("laje", "Laje", () => ExecutarComando("CARGAAREA"),
                      "Laje/chapa de piso como carga em área (kN/m²) nas vigas escolhidas ou de um nível digitado (o motor não tem elemento de placa).", "LAJE", true)));
            abaModelo.Groups.Insert(abaModelo.Groups.Count - 1, Grupo("Modificar",
                Botao("mover", "Mover", () => ExecutarComando("MOVER"), "Ponto base e segundo ponto (atração, ORTO, @dx,dy,dz). Escolha antes ou depois (Enter termina a escolha, Shift tira).", "M", true),
                Botao("copiar", "Copiar", () => ExecutarComando("COPIAR"), "Ponto base e destinos, várias cópias até Enter.", "CO", true),
                Botao("espelhar", "Espelhar", () => ExecutarComando("ESPELHAR"), "Cópia espelhada: dois pontos da linha do espelho (em planta).", "MI", true),
                Botao("rotacionar", "Rotacionar", () => ExecutarComando("ROTACIONAR"), "Em torno do eixo vertical: ponto base e ângulo (digite) ou direção (clique).", "RO", true),
                Botao("matriz", "Matriz", () => ExecutarComando("MATRIZ"), "Matriz retangular nx × ny × nz: escolha antes ou depois, depois o painel.", "AR", true),
                Botao("dividir", "Dividir barra", () => ExecutarComando("DIVIDIR"), "Em partes iguais ou a uma distância: escolha as barras antes ou depois, depois o painel.", "DIV", true),
                Botao("juntar", "Juntar barras", () => ExecutarComando("JUNTARBARRAS"), "Barras colineares em cadeia viram uma só: escolha antes ou depois. Recusa se mudar seção/material/giro, se o nó do meio tem outra barra, apoio, mola ou carga nodal.", "JB", true),
                Botao("aparar", "Aparar", () => ExecutarComando("APARAR"), "Barras limite (escolhidas antes, ou escolha + Enter) e depois as barras: o lado clicado sai.", "TR", true),
                Botao("estender", "Estender", () => ExecutarComando("ESTENDER"), "Barras limite (escolhidas antes, ou escolha + Enter) e depois as barras: a ponta perto do clique vai até o limite.", "EX", true),
                Botao("unir", "Unir nós", () => ExecutarComando("UNIR"), "Funde nós coincidentes: escolha os nós (Enter sem nada = todos), depois o painel.", "J", true),
                Botao("apagar", "Apagar", () => ExecutarComando("APAGAR"), "Apaga os objetos escolhidos antes ou depois (nós levam as barras ligadas).", "E / Del", true),
                Botao("pincel", "Copiar props.", () => ExecutarComando("COPIARPROPRIEDADES"), "Seção, material, giro e cardinal de uma barra (a escolhida ou um clique) para outras.", "MA", true),
                Botao("propriedades", "Em lote", () => ExecutarComando("LOTE"), "Seção, material, giro e apoio dos objetos escolhidos antes ou depois (painel).", "LOTE", true)));
            abaModelo.Groups.Insert(abaModelo.Groups.Count - 1, Grupo("Apoios",
                Botao("engaste", "Engaste", () => ExecutarComando("ENGASTE"), "Trava os 6 graus de liberdade dos nós (escolha antes ou depois).", "ENGASTE", true),
                Botao("rotula", "Articulado", () => ExecutarComando("ROTULA"), "Trava as translações dos nós, giros livres (escolha antes ou depois).", "ROTULA", true),
                Botao("rolo", "Móvel", () => ExecutarComando("MOVEL"), "Rolete: só uz travado (escolha antes ou depois).", "MOVEL", true),
                Botao("valores", "Molas", () => ExecutarComando("MOLAS"), "Apoio elástico: kx,ky,kz[,krx,kry,krz] em kN/m e kN·m/rad (escolha antes ou depois).", "MOLAS", true),
                Botao("livre", "Livre", () => ExecutarComando("LIVRE"), "Tira apoio e molas dos nós (escolha antes ou depois).", "LIVRE", true),
                Botao("rotulab", "Rótulas na barra", () => ExecutarComando("ROTULAR"), "Liga/desliga a rótula nas duas pontas das barras (escolha antes ou depois).", "RB", true)));
            abaModelo.ToolTip = "Gerar, grade estrutural, planos de trabalho, barras e pilares, chapas, modificar, apoios e configurações das próximas barras.";
            MontarTrelicas();    // Treliças e contraventamentos (Modelo › Gerar + paleta), JanelaTrelicas.cs

            // ACOES: casos de carga (inseridos por MontarCasosNaFaixa), cargas nos nós e barras (painel Propriedades), ações automáticas
            var abaAcoes = Aba("Ações", "A",
                Grupo("Cargas",
                    Botao("cargano", "Nodal", () => ExecutarComando("CARGANO"), "Carga nodal Fx,Fy,Fz[,Mx,My,Mz] no caso ativo, nos nós escolhidos antes ou depois.", "CN"),
                    Botao("cargadist", "Distribuída", () => ExecutarComando("CARGADIST"), "qx,qy,qz (kN/m, global; trapezoidal q1/q2) no caso ativo, nas barras escolhidas antes ou depois.", "CD", true),
                    Botao("cargano", "Pontual", () => ExecutarComando("CARGAPONTUAL"), "Distância a desde ni (ou M = meio) e Fx,Fy,Fz no caso ativo, nas barras escolhidas antes ou depois.", "CPT", true),
                    Botao("laje", "Carga em área", () => ExecutarComando("CARGAAREA"), "Laje: p (kN/m²) nas vigas escolhidas (antes ou depois) ou nas de um nível digitado.", "LAJE", true)),
                Grupo("Ações automáticas",
                    Botao("vento", "Vento", () => ComMotorExtra(ComandosAcoes.AbrirVento), "Vento estático pela NBR 6123 (V0, S1, S2, S3, coeficientes) e geração das forças por pavimento (motor Python).", null, true),
                    Botao("sismo", "Sismo", () => ComMotorExtra(ComandosAcoes.AbrirSismo), "Forças sísmicas horizontais equivalentes pela NBR 15421 (zona, classe do terreno, categoria de utilização) (motor Python).", null, true),
                    Botao("pavimentos", "Pavimentos", () => ComMotorExtra(ComandosAcoes.AbrirPavimentos), "Cotas dos pavimentos e diafragma rígido, usados pelo vento, sismo e deslocamentos entre pavimentos (motor Python).", null, true),
                    Botao("combinacoes", "Combinações", () => ComMotor(false, ComandosAcoes.AbrirCombinacoes), "Uso da edificação e combinações ELU/ELS geradas pela NBR 8681 com γ e ψ (motor Python).")));
            abaAcoes.ToolTip = "Casos de carga, cargas nos nós e barras, carga em área, vento, sismo, pavimentos e combinações.";

            // ANALISE: resultado mostrado (inserido por MontarCasosNaFaixa), modal/estabilidade e diagramas
            abaAnalise = Aba("Análise", "N",
                Grupo("Modal e estabilidade",
                    Botao("modal", "Modos de vibração", () => _ = ComMotor(false, ComandosAnalise.AbrirModal), "Análise modal: frequências, períodos e massa participante dos primeiros modos (motor Python)."),
                    Botao("estabilidade", "Estabilidade", () => _ = ComMotor(true, ComandosAnalise.AbrirEstabilidade), "γz e deslocamentos-limite (NBR 6118/8800). Calcula o modelo no motor Python antes de abrir.")),
                Grupo("Diagramas",
                    BotaoDiagrama("def", "Deformada", Diagrama.Deformada, "Configuração deformada (ampliada).", "DEF + Enter"),
                    BotaoDiagrama("normal", "Normal", Diagrama.Normal, "Esforço normal N (kN). Positivo = tração.", "N + Enter"),
                    BotaoDiagrama("cortante", "Cortante", Diagrama.Cortante, "Esforço cortante Vz (kN).", "Q + Enter"),
                    BotaoDiagrama("momento", "Momento", Diagrama.Momento, "Momento fletor My (kN·m), desenhado do lado tracionado.", "MOMENTO + Enter"),
                    Botao("mais", "Ampliar", () => Escala(1.4), "Aumenta a escala do diagrama ou da deformada.", "+", true),
                    Botao("menos", "Reduzir", () => Escala(1 / 1.4), "Diminui a escala do diagrama ou da deformada.", "-", true)));
            abaAnalise.ToolTip = "Resultado mostrado (envoltória ou combinação), modos de vibração, estabilidade e diagramas.";

            // DIMENSIONAMENTO
            abaDimens = Aba("Dimensionamento", "D",
                Grupo("Verificar",
                    Botao("veriflista", "Verificação de barras", () => _ = ComMotor(true, ComandosAnalise.AbrirVerificacaoBarras), "Lista de todas as barras com a taxa, o critério determinante e o item da norma. Calcula antes, se preciso."),
                    (tgCalor = BotaoCalor()),
                    Botao("comparar", "Verificar barra", () => ExecutarComando("VERIFICAR"), "Verificação detalhada (NBR 8800) da barra escolhida antes ou clicada depois.", "VB", true)),
                Grupo("Otimizar",
                    Botao("dimens", "Dimensionar", () => DimensionarPerfis(false), "Perfil mais leve da mesma família que passa, por iterações no motor (cada rodada um Ctrl+Z).", "DIMENSIONAR"),
                    Botao("catalogo", "Catálogo de perfis", () => ComandosAnalise.AbrirCatalogo(this), "Perfis do catálogo do motor com todas as propriedades geométricas (A, I, W, Z, r, J, Cw).")));
            abaDimens.ToolTip = "Verificação de todas as barras, mapa de aproveitamento, verificação de cada barra, perfil mais leve e catálogo.";

            // LIGACOES E BASES
            var abaLigacoes = Aba("Ligações e bases", "L",
                Grupo("Bases",
                    Botao("placa", "Placa de base", () => ExecutarComando("PLACA"), "Placa de base com chumbadores no pé do pilar escolhido (nó com apoio e uma só barra), com os esforços ELU do motor.", "PLACA")),
                Grupo("Grupos",
                    Botao("grupos", "Gerenciar ligações", AbrirGerenciadorLigacoes, "Identifica as ligações iguais e agrupa; dimensiona a pior de cada grupo e aplica o detalhe a todas (JanelaLigacoesGrupos.cs).")),
                Grupo("Ligações",
                    Botao("ligacao", "Projetar ligação", () => AbrirProjetistaLigacao(), "Biblioteca de padrões de ligação metálica (flexíveis, rígidas, emendas, contraventamento): escolha, o motor calcula chapas, parafusos e soldas e você ajusta vendo o desenho e as verificações (JanelaLigacaoProjeto.cs)."),
                    Botao("parafuso", "Parafusos", () => AbrirLigacaoTipo("parafuso"), "Ligação parafusada: cisalhamento, pressão de contato e tração nos parafusos (NBR 8800, motor Python)."),
                    Botao("solda", "Filete de solda", () => AbrirLigacaoTipo("solda_filete"), "Solda de filete: resistência do metal de solda e do metal-base (NBR 8800, motor Python)."),
                    Botao("chapa", "Chapa simples", () => AbrirLigacaoTipo("chapa_simples"), "Ligação flexível com chapa simples (shear tab): parafusos, chapa e solda (NBR 8800, motor Python)."),
                    Botao("tracao", "Ligação de tração", () => AbrirLigacaoTipo("ligacao_tracao"), "Ligação de barra tracionada: área líquida efetiva, ruptura e escoamento (NBR 8800, motor Python).")));
            abaLigacoes.ToolTip = "Placas de base com chumbadores, grupos de ligações e ligações parafusadas e soldadas (NBR 8800).";

            // RELATORIO
            var abaRelatorio = Aba("Relatório", "R",
                Grupo("Memorial",
                    Botao("relatorio", "Relatório", AbrirRelatorio, "Relatório NBR do motor (modelo, combinações, esforços, verificação) com o anexo das placas de base. Salva em PDF/HTML.", "Ctrl+P")));
            abaRelatorio.ToolTip = "Memorial de cálculo do motor e das placas de base. Exportar DWG/DXF/STEP: Arquivo › Exportar.";
            MontarCasosNaFaixa(abaAcoes, abaAnalise);

            // VISTA: câmeras (planta, elevações, iso), projeção, estilo visual, o que aparece, passo da grade do plano, painéis
            tgCargas = Alternar("cargano", "Cargas", v => { if (vista3D != null) { vista3D.MostrarCargas = v; vista3D.Reconstruir(); } }, "Mostra as setas das cargas do caso ativo.", true);
            tgCargas.IsChecked = true;
            tgCargasTopo = Alternar("cargano", "Cargas sempre visíveis", v => { if (vista3D != null) { vista3D.CargasSempre = v; vista3D.Reconstruir(); } }, "Setas das cargas por cima das peças. Desligado, uma viga na frente esconde a seta (Realista). Padrão: ligado só no Analítico.", false);
            tgCargasTopo.IsChecked = Config.Estilo3D == EstiloVisual.Analitico;
            tgSombra = Alternar("3d", "Sombra", v => { if (vista3D != null) { vista3D.Sombra = v; vista3D.Reconstruir(); } }, "Sombra projetada das peças sobre o chão e entre si (Realista com GPU). O estilo Sombreado só tem o contato suave no pé.", true);
            tgSombra.IsChecked = true;
            cGrade = Campo("Passo da grade (m)", "1", "Espaçamento da grade do plano de trabalho (atração F9).", 50);
            cGrade.LostFocus += (s, e) => MudarPasso3D(cGrade.Text);
            cGrade.KeyDown += (s, e) => { if (e.Key == Key.Enter) MudarPasso3D(cGrade.Text); };
            tgPerspectiva = Alternar("3d", "Perspectiva", v => { if (vista3D != null) vista3D.Perspectiva = v; }, "Câmera em perspectiva (desligado = ortográfica, como planta e elevação de CAD). Também no ViewCube.", true);
            var abaVista = Aba("Vista", "V",
                Grupo("Navegar",
                    Botao("zoomtudo", "Enquadrar", () => vista3D?.Enquadrar(), "Mostra o modelo inteiro (ou duplo clique na roda do mouse).", "Z + Enter"),
                    Botao("zoommais", "Zoom na seleção", () => vista3D?.EnquadrarSelecao(), "Aproxima o que está escolhido.", "ZO", true),
                    Botao("topo", "Topo", () => ExecutarComando("TOPO"), "Planta (de cima).", "Ctrl+7 num.", true),
                    Botao("frente", "Frente", () => ExecutarComando("FRENTE"), "Elevação XZ.", "Ctrl+1 num.", true),
                    Botao("lado", "Lado", () => ExecutarComando("LADO"), "Elevação YZ (direita).", "Ctrl+3 num.", true),
                    Botao("iso", "Isométrica", () => ExecutarComando("ISO"), "Vista isométrica (Home do ViewCube).", "Ctrl+5 num.", true),
                    tgPerspectiva,
                    BotaoEstilo()),
                Grupo("Mostrar", tgCargas, tgCargasTopo, tgSombra),
                Grupo("Grade", cGrade),
                Grupo("Painéis",
                    Botao("painel", "Projeto", () => Alternar(PainelProjeto), "Mostra/oculta o painel Projeto.", null, true),
                    Botao("painel", "Propriedades", () => Alternar(PainelProp), "Mostra/oculta o painel Propriedades.", "Ctrl+1", true),
                    Botao("tabela", "Tabelas", () => Alternar(PainelTabelas), "Mostra/oculta as tabelas de nós, barras e verificação.", "TABELAS", true)),
                Grupo("Ajuda", Botao("ajuda", "Atalhos e ajuda", MostrarAjuda, "Como usar e lista de atalhos.", "F1")));
            abaVista.ToolTip = "Enquadrar, planta/elevações/isométrica, perspectiva, estilo visual, cargas, passo da grade, painéis e ajuda.";

            Faixa.SelectedTabItem = abaModelo;
        }

        F.ToggleButton tgPerspectiva;

        void MudarPasso3D(string txt)
        {
            if (!Num.Ler(txt, out double g) || g <= 0) { cGrade.Text = Num.Fmt(vista3D?.PassoGrade ?? 1, 3); return; }
            if (vista3D != null) { vista3D.PassoGrade = g; vista3D.InvalidateVisual(); }
            Log("Passo da grade do plano: " + Num.Fmt(g, 3) + " m.", false);
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

        // Motor do Calcular (F5): interno (Nucleo.cs, sempre roda e alimenta a vista) ou, a mais, o motor Python (docs/UNIFICACAO-MOTOR.md)
        F.ComboBox cbMotor;
        internal MotorResultado ResMotor;
        bool motorRodando;
        F.ComboBox CriarCbMotor()
        {
            cbMotor = new F.ComboBox { Header = "Motor", IsEditable = false, Width = 220, ToolTip = Dica("Motor de cálculo", "Interno (Nucleo.cs): pórtico plano e verificação simplificada, refeito a cada alteração. Python (NBR completas): ao calcular (F5) também envia o modelo ao motor Python, que gera as combinações da NBR 8681 e verifica as barras pela NBR 8800 completa.", "F5") };
            cbMotor.Items.Add("Interno (Nucleo.cs)");
            cbMotor.Items.Add("Motor: Python (NBR completas)");
            cbMotor.SelectedIndex = 0;
            return cbMotor;
        }

        internal void Calcular()
        {
            if (Esp != null) { _ = CalcularEspacial(); return; }   // modelo espacial: motor Python
            Resolver(true);
            if (cbMotor != null && cbMotor.SelectedIndex == 1 && Vista.Resolvido && !motorRodando) _ = CalcularMotor();
        }

        async System.Threading.Tasks.Task CalcularMotor()
        {
            motorRodando = true;
            var barra = Anim.Brilho(90, 4); barra.Margin = new Thickness(0, 0, 10, 0); barra.VerticalAlignment = VerticalAlignment.Center;
            StatusBotoes.Children.Insert(0, barra);
            var antes = Mouse.OverrideCursor; Mouse.OverrideCursor = Cursors.AppStarting;
            var prog = new Progress<string>(s => StatusMsg.Text = "Motor Python: " + s + "…");
            try
            {
                string xml = M.ParaXml();
                var r = await MotorModelo.Calcular(Modelo.DeXml(xml), prog);
                if (xml != M.ParaXml()) { StatusMsg.Text = "Motor Python: o modelo mudou durante o cálculo; calcule de novo (F5)."; return; }
                MostrarMotor(r, xml);
                int reprov = r.Verificacao.Count(v => !v.Ok);
                var pior = r.Verificacao.Where(v => v.Taxa.HasValue).OrderByDescending(v => v.Taxa.Value).FirstOrDefault();
                StatusMsg.Text = "Motor Python: " + Pl.N(r.Combinacoes.Count, "combinação", "combinações") + " NBR 8681 · NBR 8800: " +
                    (reprov == 0 ? "todas as barras passam" : "⚠ " + Pl.N(reprov, "barra não passa", "barras não passam")) +
                    (pior != null ? " · pior: barra " + pior.Barra + " " + Num.Fmt(pior.Taxa.Value * 100, 0) + "% (" + pior.Criterio + ")" : "");
                Log(StatusMsg.Text);
                foreach (var v in r.Verificacao)
                    Log("  barra " + v.Barra + ": " + (v.Taxa.HasValue ? Num.Fmt(v.Taxa.Value * 100, 0) + "%" : "não verificada") + " · " + v.Criterio + " · " + v.Item + " · " + v.Combinacao, false);
            }
            catch (Exception ex) { ErroMotor(ex); }
            finally
            {
                StatusBotoes.Children.Remove(barra);
                Mouse.OverrideCursor = antes;
                motorRodando = false;
            }
        }

        // Passa a mostrar o resultado do motor (envoltoria ELU): diagramas, tabelas, reacoes e % Aprov. vem do MotorResultado.
        internal void MostrarMotor(MotorResultado r, string xml)
        {
            ResMotor = r; motorXml = xml;
            AtualizarListasCasos();
            EscolherResultado(r.EstadoDe.Values.Any(e => e.StartsWith("ELU")) ? "pyenv:ELU" : "py:" + r.Combinacoes.FirstOrDefault());
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

        // ================= bastidores (menu Arquivo): Backstage.cs =================

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
            // estados do desenho na Vista3D (docs/v3/PESQUISA-PLANT.md 3.6); a Vista3D lê os mesmos valores (Comandos3D.LigarComandos3D)
            NovoBloco();
            stAtrair = StatusBtn("snap", "Snap — prender o cursor à grade do plano de trabalho (F9)", v => V3(x => { if (x.Snap.Modos.HasFlag(ModoSnap.Grade) != v) x.Snap.Modos ^= ModoSnap.Grade; })); stAtrair.IsChecked = true;
            stGrade = StatusBtn("grade", "Grade — mostrar a grade do plano de trabalho ao desenhar (F7)", v => V3(x => x.MostrarGradePlano = v)); stGrade.IsChecked = true;
            NovoBloco();
            stOrto = StatusBtn("ortho", "Orto — só nas direções do plano de trabalho (F8, ou segure Shift)", v => StatusSnap3D());
            stPolar = StatusBtn("polar", "Polar — prende em 0°/90° quando perto (F10)", v => StatusSnap3D()); stPolar.IsChecked = true;
            stObj = StatusBtn("objsnap", "Atração a objetos (F3): nós, meios de barra e cruzamentos da grade estrutural", v => StatusSnap3D()); stObj.IsChecked = true;
            stRastro = StatusBtn("rastro", "Rastreamento (F11) — guias a partir dos pontos de atração (Snap3D)", v => StatusSnap3D()); stRastro.IsChecked = true;
            stDin = StatusBtn("dinamica", "Dinâmica — caixas de comprimento e ângulo junto ao cursor (F12)", v => V3(x => x.EntradaDinamica = v)); stDin.IsChecked = true;
            NovoBloco();
            var tela = new Button { Content = IconeStatus("M2,7 V2 H7 M13,2 H18 V7 M18,13 V18 H13 M7,18 H2 V13"), ToolTip = "Enquadrar o modelo (Z + Enter, duplo clique na roda)", Style = EstiloBotaoStatus() };
            tela.Click += (s, e) => vista3D?.Enquadrar();
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

        System.Windows.Controls.Primitives.ToggleButton stRastro, stDin;

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
            lig.Setters.Add(new Setter(Border.BackgroundProperty, TemaPlant.BSup2, "b"));
            lig.Setters.Add(new Setter(Border.BorderBrushProperty, TemaPlant.BBordaSutil, "b"));
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
            var ic = new Image { Width = 16, Height = 16, Stretch = Stretch.None, SnapsToDevicePixels = true, UseLayoutRounding = true, VerticalAlignment = VerticalAlignment.Center };
            var tx = new TextBlock { Text = nome, FontSize = 11, Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = TemaPlant.BTexto3 };
            var sp = new StackPanel { Orientation = Orientation.Horizontal }; sp.Children.Add(ic); sp.Children.Add(tx);
            var b = new System.Windows.Controls.Primitives.ToggleButton { Content = sp, ToolTip = dica, Style = EstiloPilula() };
            System.Windows.Automation.AutomationProperties.SetName(b, nome);
            void Cor() { bool on = b.IsChecked == true; ic.Source = NovoFtool.Icones.Mono(icone, 16, on ? TemaPlant.Realce : TemaPlant.Texto3); tx.Foreground = on ? TemaPlant.BTexto1 : TemaPlant.BTexto3; }
            b.Checked += (s, e) => Cor(); b.Unchecked += (s, e) => Cor(); Cor();
            b.Click += (s, e) => { acao(b.IsChecked == true); FocarVista(); };
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
            sp.Children.Add(new Image { Source = TemaPlant.Selo(24), Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Center });
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
