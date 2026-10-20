// Janela principal no fluxo do Ftool: ferramentas a esquerda, atributos em cima, ficha do atributo a direita
// ("aplicar aos selecionados"), grade embaixo. Acrescimos: medida digitada, biblioteca Gerdau, tema escuro.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace Portico
{
    // Escala da tela e fonte da interface. O programa se declara ciente da escala do Windows (125%, 150%...)
    // para o texto sair nitido, e por isso toda medida fixa em pixel passa por Ui.Z().
    static class Ui
    {
        public static float S = 1;
        public static string Familia = "Segoe UI";

        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        public static void Iniciar()
        {
            try { SetProcessDPIAware(); } catch { }
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) S = g.DpiX / 96f;
            // fonte da interface do AutoCAD, se estiver instalada na maquina
            using (System.Drawing.Text.InstalledFontCollection fc = new System.Drawing.Text.InstalledFontCollection())
                foreach (FontFamily ff in fc.Families) if (ff.Name == "Artifakt Element") Familia = ff.Name;
        }

        public static int Z(double v) { return (int)Math.Round(v * S); }
        public static Font F(float pt) { return new Font(Familia, pt); }
        public static Font F(float pt, FontStyle estilo) { return new Font(Familia, pt, estilo); }
    }

    static class Programa
    {
        [STAThread]
        static void Main(string[] args)
        {
            Ui.Iniciar();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            // Portico.exe [arquivo.portico] [vista 0..4] [ficha 0..6]
            int v = 0, f = 0;
            if (args.Length > 1) int.TryParse(args[1], out v);
            if (args.Length > 2) int.TryParse(args[2], out f);
            Janela j = new Janela(args.Length > 0 ? args[0] : null, Math.Max(0, Math.Min(5, v)), Math.Max(0, Math.Min(9, f)));
            // 4o argumento (teste): medidas "a;b|a;b|..." digitadas na faixa de medida, uma por ponto
            if (args.Length > 3) j.Load += delegate { j.Digitar(args[3]); };
            Application.Run(j);
        }
    }

    class Tema
    {
        public bool Escuro;
        public Color Fundo, GradeMenor, GradeMaior, Barra, Sel, Apoio, Carga, Texto, TextoFraco, Elastico, Guia;
        public Color[] Vista;
        public Color Ui, UiClaro, UiFundo, UiBorda, UiTitulo, UiTexto, UiTextoFraco, Destaque, DestaqueTexto, Acento, Campo;

        static Color C(int r, int g, int b) { return Color.FromArgb(r, g, b); }

        public static Tema Criar(bool escuro)
        {
            Tema t = new Tema();
            t.Escuro = escuro;
            if (escuro)
            {
                // paleta do tema escuro das fitas da Autodesk
                t.Fundo = C(33, 40, 48); t.GradeMenor = C(38, 46, 55); t.GradeMaior = C(49, 58, 69);
                t.Barra = C(236, 238, 242); t.Sel = C(255, 170, 60);
                t.Apoio = C(70, 210, 160); t.Carga = C(255, 110, 100);
                t.Texto = C(220, 225, 230); t.TextoFraco = C(145, 154, 167);
                t.Elastico = C(110, 185, 255); t.Guia = C(70, 225, 95);
                t.Vista = new[] { t.Barra, C(200, 150, 255), C(100, 225, 140), C(100, 175, 255), C(255, 120, 195), C(232, 235, 240) };
                // cores medidas na tela do AutoCAD (tema escuro)
                t.Ui = C(59, 68, 83); t.UiClaro = C(84, 96, 117); t.UiFundo = C(59, 68, 83); t.UiBorda = C(34, 41, 51); t.UiTitulo = C(46, 52, 64);
                t.UiTexto = C(232, 235, 240); t.UiTextoFraco = C(165, 174, 188);
                t.Destaque = C(44, 100, 146); t.DestaqueTexto = Color.White; t.Acento = C(20, 138, 204); t.Campo = C(41, 48, 60);
            }
            else
            {
                t.Fundo = C(255, 255, 255); t.GradeMenor = C(241, 243, 246); t.GradeMaior = C(224, 228, 234);
                t.Barra = C(28, 34, 46); t.Sel = C(225, 95, 0);
                t.Apoio = C(0, 125, 90); t.Carga = C(200, 30, 30);
                t.Texto = C(40, 45, 55); t.TextoFraco = C(110, 118, 130);
                t.Elastico = C(0, 110, 220); t.Guia = C(0, 150, 40);
                t.Vista = new[] { t.Barra, C(120, 60, 170), C(20, 140, 70), C(30, 90, 200), C(190, 30, 120), C(30, 34, 42) };
                t.Ui = C(245, 245, 246); t.UiClaro = C(214, 228, 242); t.UiFundo = C(250, 250, 251); t.UiBorda = C(214, 217, 222); t.UiTitulo = C(231, 233, 237);
                t.UiTexto = C(30, 34, 42); t.UiTextoFraco = C(96, 104, 117);
                t.Destaque = C(197, 224, 243); t.DestaqueTexto = C(10, 40, 90); t.Acento = C(6, 125, 190); t.Campo = Color.White;
            }
            return t;
        }
    }

    class Cores : ProfessionalColorTable
    {
        readonly Tema t;
        public Cores(Tema t) { this.t = t; UseSystemColors = false; }
        public override Color ToolStripGradientBegin { get { return t.Ui; } }
        public override Color ToolStripGradientMiddle { get { return t.Ui; } }
        public override Color ToolStripGradientEnd { get { return t.Ui; } }
        public override Color ToolStripBorder { get { return t.Ui; } }
        public override Color ToolStripDropDownBackground { get { return t.UiFundo; } }
        public override Color MenuStripGradientBegin { get { return t.UiBorda; } }
        public override Color MenuStripGradientEnd { get { return t.UiBorda; } }
        public override Color StatusStripGradientBegin { get { return t.UiBorda; } }
        public override Color StatusStripGradientEnd { get { return t.UiBorda; } }
        public override Color ImageMarginGradientBegin { get { return t.UiFundo; } }
        public override Color ImageMarginGradientMiddle { get { return t.UiFundo; } }
        public override Color ImageMarginGradientEnd { get { return t.UiFundo; } }
        public override Color MenuItemSelected { get { return t.UiClaro; } }
        public override Color MenuItemSelectedGradientBegin { get { return t.UiClaro; } }
        public override Color MenuItemSelectedGradientEnd { get { return t.UiClaro; } }
        public override Color MenuItemPressedGradientBegin { get { return t.UiFundo; } }
        public override Color MenuItemPressedGradientMiddle { get { return t.UiFundo; } }
        public override Color MenuItemPressedGradientEnd { get { return t.UiFundo; } }
        public override Color MenuItemBorder { get { return t.UiClaro; } }
        public override Color MenuBorder { get { return t.UiBorda; } }
        public override Color ButtonSelectedHighlight { get { return t.UiClaro; } }
        public override Color ButtonSelectedGradientBegin { get { return t.UiClaro; } }
        public override Color ButtonSelectedGradientMiddle { get { return t.UiClaro; } }
        public override Color ButtonSelectedGradientEnd { get { return t.UiClaro; } }
        public override Color ButtonSelectedBorder { get { return t.UiClaro; } }
        public override Color ButtonSelectedHighlightBorder { get { return t.UiClaro; } }
        public override Color ButtonPressedGradientBegin { get { return t.Destaque; } }
        public override Color ButtonPressedGradientMiddle { get { return t.Destaque; } }
        public override Color ButtonPressedGradientEnd { get { return t.Destaque; } }
        public override Color ButtonPressedBorder { get { return t.Destaque; } }
        public override Color ButtonPressedHighlight { get { return t.Destaque; } }
        public override Color ButtonCheckedGradientBegin { get { return t.Destaque; } }
        public override Color ButtonCheckedGradientMiddle { get { return t.Destaque; } }
        public override Color ButtonCheckedGradientEnd { get { return t.Destaque; } }
        public override Color ButtonCheckedHighlight { get { return t.Destaque; } }
        public override Color ButtonCheckedHighlightBorder { get { return t.Destaque; } }
        public override Color CheckBackground { get { return t.Destaque; } }
        public override Color CheckSelectedBackground { get { return t.Destaque; } }
        public override Color CheckPressedBackground { get { return t.Destaque; } }
        public override Color SeparatorDark { get { return t.UiBorda; } }
        public override Color SeparatorLight { get { return t.Ui; } }
        public override Color GripDark { get { return t.UiBorda; } }
        public override Color GripLight { get { return t.Ui; } }
    }

    // Barra de titulo propria: deixa o clique passar para a janela (arrastar, maximizar com duplo clique).
    class BarraT : Panel
    {
        public BarraT() { SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true); }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x84) { m.Result = (IntPtr)(-1); return; }
            base.WndProc(ref m);
        }
    }

    class Tela : Control
    {
        public Tela()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
        }
        protected override bool IsInputKey(Keys k) { return true; }
    }

    // Ficha do painel da direita: empilha rotulos, campos e botoes de cima para baixo.
    class Ficha : Panel
    {
        const int M = 18;
        public const int W = 330;
        int y = 12;
        static readonly Font F = Ui.F(9f), FN = Ui.F(9f, FontStyle.Bold);
        // a ficha e montada em medidas de 100%; Z() converte para a escala da tela na hora de posicionar
        static int Z(double v) { return Ui.Z(v); }

        public Ficha(bool sub)
        {
            Font = F;
            if (sub) Visible = false;
            else { Dock = DockStyle.Fill; AutoScroll = true; Visible = false; }
        }

        public Label Texto(string s, bool fraco)
        {
            int h = (int)Math.Ceiling(TextRenderer.MeasureText(s, F, new Size(Z(W), 0), TextFormatFlags.WordBreak).Height / Ui.S) + 4;
            Label l = new Label { Text = s, Left = Z(M), Top = Z(y), Width = Z(W), Height = Z(h), AutoSize = false, Tag = fraco ? "fraco" : null };
            Controls.Add(l);
            y += h + 4;
            return l;
        }

        public Label Secao(string s)
        {
            Label l = new Label { Text = s, Left = Z(M), Top = Z(y + 4), Width = Z(W), Height = Ui.Z(20), Font = FN };
            // titulo de grupo com um fio embaixo, como os grupos das caixas de dialogo do CAD
            Panel fio = new Panel { Left = Z(M), Top = Z(y + 25), Width = Z(W), Height = 1, Tag = "fio" };
            Controls.Add(l); Controls.Add(fio);
            y += 34;
            return l;
        }

        public TextBox Campo(string rotulo)
        {
            Label l = new Label { Text = rotulo, Left = Z(M), Top = Z(y + 3), Width = Ui.Z(200), Height = Ui.Z(20) };
            TextBox t = new TextBox { Left = Z(M + 206), Top = Z(y), Width = Z(W - 206), BorderStyle = BorderStyle.FixedSingle, TextAlign = HorizontalAlignment.Right };
            t.Tag = l;
            Controls.Add(l); Controls.Add(t);
            y += 29;
            return t;
        }

        public ComboBox Combo(params string[] itens)
        {
            ComboBox c = new ComboBox { Left = Z(M), Top = Z(y), Width = Z(W), DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat };
            c.Items.AddRange(itens);
            if (itens.Length > 0) c.SelectedIndex = 0;
            Controls.Add(c);
            y += 32;
            return c;
        }

        public CheckBox Check(string s)
        {
            CheckBox c = new CheckBox { Text = s, Left = Z(M), Top = Z(y), Width = Z(W), Height = Ui.Z(24) };
            Controls.Add(c);
            y += 27;
            return c;
        }

        public Button Botao(string s, bool primario, EventHandler h)
        {
            Button b = Novo(s, primario, h);
            b.SetBounds(Z(M), Z(y), Z(W), Z(34));
            Controls.Add(b);
            y += 40;
            return b;
        }

        // Botoes lado a lado, na mesma linha.
        public Button[] Linha(string[] nomes, EventHandler[] hs)
        {
            int n = nomes.Length, w = (W - 6 * (n - 1)) / n;
            Button[] r = new Button[n];
            for (int i = 0; i < n; i++)
            {
                r[i] = Novo(nomes[i], false, hs[i]);
                r[i].SetBounds(Z(M + i * (w + 6)), Z(y), Z(w), Z(32));
                Controls.Add(r[i]);
            }
            y += 38;
            return r;
        }

        static Button Novo(string s, bool primario, EventHandler h)
        {
            Button b = new Button { Text = s, FlatStyle = FlatStyle.Flat, Tag = primario ? "primario" : null, Cursor = Cursors.Hand };
            b.Click += h;
            return b;
        }

        public ListBox Lista(int altura)
        {
            ListBox l = new ListBox { Left = Z(M), Top = Z(y), Width = Z(W), Height = Z(altura), BorderStyle = BorderStyle.FixedSingle, IntegralHeight = false };
            Controls.Add(l);
            y += altura + 6;
            return l;
        }

        public T Livre<T>(T c, int altura) where T : Control
        {
            c.SetBounds(Z(M), Z(y), Z(W), Z(altura));
            Controls.Add(c);
            y += altura + 6;
            return c;
        }

        public Ficha Sub(int altura)
        {
            Ficha f = new Ficha(true);
            f.SetBounds(0, Z(y), Z(W + 2 * M), Z(altura));
            f.y = 0;
            Controls.Add(f);
            return f;
        }

        public void Espaco(int n) { y += n; }
        public int Altura { get { return y; } }

        public void Pintar(Tema t)
        {
            BackColor = t.UiFundo;
            ForeColor = t.UiTexto;
            foreach (Control c in Controls)
            {
                if (c is Ficha) ((Ficha)c).Pintar(t);
                else if (c is Button)
                {
                    Button b = (Button)c;
                    bool pri = (string)b.Tag == "primario";
                    b.BackColor = t.Escuro ? Color.FromArgb(72, 83, 102) : Color.FromArgb(232, 234, 238);
                    b.ForeColor = t.UiTexto;
                    b.FlatAppearance.BorderSize = 1;
                    b.FlatAppearance.BorderColor = pri ? t.Acento : (t.Escuro ? Color.FromArgb(112, 124, 144) : Color.FromArgb(170, 176, 186));
                    b.FlatAppearance.MouseOverBackColor = t.UiClaro;
                }
                else if (c is TextBox || c is ListBox || c is ComboBox) { c.BackColor = t.Campo; c.ForeColor = t.UiTexto; }
                else if (c is Label) { c.BackColor = t.UiFundo; c.ForeColor = (string)c.Tag == "fraco" ? t.UiTextoFraco : t.UiTexto; }
                else if (c is CheckBox) { c.BackColor = t.UiFundo; c.ForeColor = t.UiTexto; }
                else if ((string)c.Tag == "fio") c.BackColor = t.Escuro ? Color.FromArgb(92, 104, 124) : Color.FromArgb(200, 205, 213);
                else c.BackColor = t.UiFundo;
            }
        }
    }

    class Janela : Form
    {
        const string NOME = "Pórtico";
        const double TOL = 1e-4;   // m: nos mais proximos que isso sao o mesmo no

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int v, int tam);

        Modelo m = new Modelo();
        Resultado res = new Resultado();
        readonly List<object> sel = new List<object>();
        readonly Stack<string> desfazer = new Stack<string>();
        string atual, arquivo;
        bool sujo, carregando;

        Tema t = Tema.Criar(false);
        int ferr;                    // 0 livre (selecionar), 1 barra, 2 no, 3 aplicando um atributo com cliques
        int vista;                   // 0 estrutura, 1 N, 2 Q, 3 M, 4 deformada
        int vistaRes = 3;            // ultimo diagrama visto na aba Resultados
        bool abaRes;                 // false = aba Modelo, true = aba Resultados
        string apNome = "";          // atributo sendo aplicado
        int apFiltro;                // 0 nos e barras, 1 so nos, 2 so barras
        bool apJanela;               // pode aplicar arrastando uma janela
        Action<No> acNo;
        Action<Barra, double> acBarra;   // double = posicao do clique na barra (0..1), ou -1
        Form dlgAtual;
        ToolStripProfessionalRenderer rnd;
        int modoPref;                // entrada digitada: 0 comprimento+angulo, 1 dx+dy, 2 x+y
        double esc = 60, ox = 80, oy = 400, fatorDiag = 1;
        bool temUlt, rotulos = true;
        double ux, uy;

        Point mouse, arrasto, botaoDir, c0, c1;
        bool dentro, pan, caixa;
        int marca;                   // 0 nada, 1 no, 2 meio da barra, 3 sobre a barra
        bool guia;
        object sobre;

        Tela tela;
        Panel lado, entrada, fita, fitaRes, fitaOp, abas, docs;
        BarraT barraTitulo;
        ToolStrip qat;
        Button btMin, btMax, btFechar, btMais;
        Button[] tabs;
        Label lbDoc;
        int abaAtual;
        ToolStripButton btPeso, btCortante, btRotulos, btGrade, btSnap;
        ComboBox rOpcao;
        readonly List<ToolStrip> faixas = new List<ToolStrip>();
        readonly List<Label> titulos = new List<Label>();
        readonly List<Panel> divisas = new List<Panel>();
        Label tituloFicha, info, lbEtapa, lbA, lbB, lbDica;
        ComboBox cbModo;
        TextBox tbA, tbB;
        Button btInserir;
        StatusStrip st;
        ToolStripStatusLabel status, coord, msg;
        ToolStripButton sGrade, sSnap;
        ToolStripTextBox tbGrade;
        ToolStripButton[] btVista = new ToolStripButton[6];
        readonly Dictionary<Barra, Verif> verifs = new Dictionary<Barra, Verif>();
        readonly List<KeyValuePair<ToolStripItem, int>> icones = new List<KeyValuePair<ToolStripItem, int>>();

        // fichas
        Ficha[] fichas = new Ficha[8];
        ComboBox pModo, pDiam, pClasse, pPivo;
        TextBox pSobra, pH, pB, pEsp, pFil, pPor, pBorda;
        CheckBox pRosca;
        Ficha pPerf, pDig;
        int nFam;                    // quantas familias de perfil de catalogo ha na lista de tipos de secao
        TextBox gA, gB, mE, sB, sH, sD, sA, sI, aKx, aKy, aKg, aRx, aRy, nFx, nFy, nMz, bQy, bQx, bQi, bQf;
        Label gLbl, mInfo, sProps;
        ComboBox mLista, sTipo, sSerie;
        ListBox sLista;
        CheckBox sDeitado, aX, aY, aG;
        Ficha sGer, sRet, sCirc, sGen;
        Panel sPrevia;

        static readonly string[] NomeVista = { "Estrutura", "Normal (kN)", "Cortante (kN)", "Momento fletor (kN.m)", "Deformada", "Verificação das barras" };
        static readonly string[] NomeFicha = { "Medidas", "Material", "Seção", "Apoio", "Rótula", "Carga no nó", "Carga na barra", "Placa de base" };
        readonly Font fonte = Ui.F(8.5f), fonteN = Ui.F(8.5f, FontStyle.Bold);

        public Janela(string abrir, int vistaInicial, int fichaInicial)
        {
            Text = NOME;
            Width = Ui.Z(1380); Height = Ui.Z(860);
            Rectangle area = Screen.PrimaryScreen.WorkingArea;   // nao abrir maior que a tela
            Width = Math.Min(Width, area.Width - 60); Height = Math.Min(Height, area.Height - 40);
            MinimumSize = new Size(Ui.Z(1000), Ui.Z(640));
            StartPosition = FormStartPosition.CenterScreen;
            Font = Ui.F(9f);
            LerConfig();
            Montar();
            atual = m.ParaXml();
            if (abrir != null && File.Exists(abrir)) Abrir(abrir);
            Recalcular();
            Titulo();
            if (vistaInicial > 0) { vistaRes = vistaInicial; Aba(true); }
            // 3o argumento (teste): abre a janela do atributo k
            if (fichaInicial > 0) Shown += delegate { BeginInvoke((MethodInvoker)delegate { if (fichaInicial == 8) { MostrarPlaca(); return; } if (fichaInicial == 9) { VerificarBarra(m.Barras[Math.Min(1, m.Barras.Count - 1)]); return; } if (fichaInicial == 2 && m.Barras.Count > 0) sel.Add(m.Barras[0]); Atributo(fichaInicial); }); };
        }

        // ---------- configuracao ----------

        static string ArqConfig { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Portico", "config.ini"); } }

        void LerConfig()
        {
            try
            {
                if (!File.Exists(ArqConfig)) return;
                foreach (string l in File.ReadAllLines(ArqConfig))
                {
                    string[] p = l.Split('=');
                    if (p.Length != 2) continue;
                    if (p[0] == "escuro") t = Tema.Criar(p[1].Trim() == "1");
                    else if (p[0] == "modo") int.TryParse(p[1], out modoPref);
                }
                modoPref = Math.Max(0, Math.Min(2, modoPref));
            }
            catch { }
        }

        void GravarConfig()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ArqConfig));
                File.WriteAllLines(ArqConfig, new[] { "escuro=" + (t.Escuro ? 1 : 0), "modo=" + modoPref });
            }
            catch { }
        }

        // ---------- montagem da janela ----------

        void Montar()
        {
            // barra de titulo propria, no molde do CAD: logotipo, acesso rapido, titulo no centro, botoes da janela
            barraTitulo = new BarraT { Dock = DockStyle.Top, Height = Ui.Z(34) };
            barraTitulo.Paint += delegate(object o, PaintEventArgs e) { PintarTitulo(e.Graphics); };
            barraTitulo.Resize += delegate { barraTitulo.Invalidate(); };
            qat = new ToolStrip();
            qat.Dock = DockStyle.None;
            qat.AutoSize = false;
            qat.CanOverflow = false;
            qat.GripStyle = ToolStripGripStyle.Hidden;
            qat.ImageScalingSize = new Size(Ui.Z(20), Ui.Z(20));
            qat.Padding = Padding.Empty;
            qat.SetBounds(Ui.Z(46), Ui.Z(3), Ui.Z(136), Ui.Z(28));
            Rapido("Novo (Ctrl+N)", 19, delegate { Novo(); });
            Rapido("Abrir (Ctrl+O)", 20, delegate { AbrirDialogo(); });
            Rapido("Salvar (Ctrl+S)", 21, delegate { Salvar(false); });
            Rapido("Desfazer (Ctrl+Z)", 22, delegate { Desfazer(); });
            faixas.Add(qat);
            btMin = BotaoJanela("", delegate { WindowState = FormWindowState.Minimized; });
            btMax = BotaoJanela("", delegate { WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized; });
            btFechar = BotaoJanela("", delegate { Close(); });
            barraTitulo.Controls.Add(qat);
            barraTitulo.Controls.Add(btMin); barraTitulo.Controls.Add(btMax); barraTitulo.Controls.Add(btFechar);
            Resize += delegate { btMax.Text = WindowState == FormWindowState.Maximized ? "" : ""; };

            // fita da aba Modelo: ferramentas de trabalho em grupos (nenhuma fica "ligada")
            ToolStrip g;
            fita = new Panel { Dock = DockStyle.Top, Height = ALT_FITA };
            g = Grupo(fita, "Desenhar");
            Botao(g, "Barra", 1, "Desenhar barras (B). Botão direito termina.", delegate { Ferramenta(1); });
            Botao(g, "Nó", 17, "Criar um nó: no vazio, ou sobre uma barra para dividi-la", delegate { Ferramenta(2); });
            Botao(g, "Medidas", 23, "Digitar o comprimento de uma barra ou a posição de um nó", delegate { Atributo(0); });
            g = Grupo(fita, "Propriedades");
            Botao(g, "Material", 24, "Material das barras", delegate { Atributo(1); });
            Botao(g, "Seção", 25, "Perfil de catálogo ou outra seção", delegate { Atributo(2); });
            g = Grupo(fita, "Vínculos");
            Botao(g, "Apoio", 3, "Apoios, molas e recalques", delegate { Atributo(3); });
            Botao(g, "Rótula", 6, "Rótulas nas pontas das barras", delegate { Atributo(4); });
            Botao(g, "Placa\nbase", 32, "Placa de base com parafusos no pé do pilar", delegate { Atributo(7); });
            g = Grupo(fita, "Cargas");
            Botao(g, "Carga\nno nó", 26, "Força e momento aplicados no nó", delegate { Atributo(5); });
            Botao(g, "Carga\nna barra", 27, "Carga distribuída na barra", delegate { Atributo(6); });
            g = Grupo(fita, "Modificar");
            ToolStripDropDownButton rem = new ToolStripDropDownButton("Remover");
            rem.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
            rem.TextImageRelation = TextImageRelation.ImageAboveText;
            rem.TextAlign = ContentAlignment.TopCenter;
            rem.ToolTipText = "Tirar apoio, rótula, carga ou placa: escolha o que tirar e clique nos nós ou barras";
            rem.AutoSize = false;
            rem.Size = new Size(Ui.Z(72), ALT_BOTAO);
            string[] nomesRem = { "Apoio", "Carga do nó", "Carga da barra", "Rótulas da barra", "Rótulas do nó", "Placa de base" };
            for (int i = 0; i < 6; i++)
            {
                int k = i;
                rem.DropDownItems.Add(new ToolStripMenuItem(nomesRem[i], null, delegate { Remover(k); }));
            }
            g.Items.Add(rem);
            icones.Add(new KeyValuePair<ToolStripItem, int>(rem, 31));
            Botao(g, "Apagar", 18, "Apagar os nós e barras selecionados (Del)", delegate { Excluir(); });
            g = Grupo(fita, "Vista");
            Botao(g, "Enquadrar", 15, "Mostrar a estrutura inteira (F4)", delegate { Enquadrar(); });
            Fechar(fita);

            // fita da aba Resultados: so visualizacoes
            fitaRes = new Panel { Dock = DockStyle.Top, Height = ALT_FITA, Visible = false };
            g = Grupo(fitaRes, "Diagramas");
            string[] curtoV = { "", "Normal", "Cortante", "Momento", "Deformada" };
            for (int i = 1; i < 5; i++)
            {
                int v = i;
                btVista[i] = Botao(g, curtoV[i], 8 + i, NomeVista[i], delegate { Vista(v); });
            }
            g = Grupo(fitaRes, "Escala do desenho");
            Botao(g, "Menor", 28, "Diminuir o desenho do diagrama", delegate { fatorDiag /= 1.4; tela.Invalidate(); });
            Botao(g, "Maior", 29, "Aumentar o desenho do diagrama", delegate { fatorDiag *= 1.4; tela.Invalidate(); });
            g = Grupo(fitaRes, "Verificar");
            btVista[5] = Botao(g, "Barras", 40, "Pinta cada barra de verde ou vermelho conforme passa ou não, com o aproveitamento", delegate { Vista(5); });
            Botao(g, "Comparar\nperfis", 41, "Compara o perfil da barra com os outros da mesma família: quais passam e quais não", delegate { CompararPerfis(); });
            Botao(g, "Placa\nbase", 32, "Parafusos e chapa da placa de base: o que passa e o que não", delegate { MostrarPlaca(); });
            g = Grupo(fitaRes, "Saída");
            Botao(g, "Exportar\nCSV", 39, "Salvar os resultados em planilha CSV (Ctrl+E)", delegate { Exportar(); });
            g = Grupo(fitaRes, "Vista");
            Botao(g, "Enquadrar", 15, "Mostrar a estrutura inteira (F4)", delegate { Enquadrar(); });
            Fechar(fitaRes);

            // fita da aba Opcoes: o que no CAD fica em Gerenciar / Exibir
            fitaOp = new Panel { Dock = DockStyle.Top, Height = ALT_FITA, Visible = false };
            g = Grupo(fitaOp, "Arquivo");
            Botao(g, "Salvar\ncomo", 21, "Salvar o modelo com outro nome", delegate { Salvar(true); });
            g = Grupo(fitaOp, "Cálculo");
            btPeso = Botao(g, "Peso\npróprio", 33, "Considerar o peso próprio das barras", delegate { m.PesoProprio = !m.PesoProprio; Registrar(); });
            btCortante = Botao(g, "Deform.\ncortante", 34, "Considerar a deformação por cortante (viga de Timoshenko)", delegate { m.Timoshenko = !m.Timoshenko; Registrar(); });
            g = Grupo(fitaOp, "Exibir");
            btRotulos = Botao(g, "Nomes\ndos perfis", 35, "Mostrar perfil e comprimento em cada barra", delegate { rotulos = !rotulos; Chaves(); });
            btGrade = Botao(g, "Grade", 36, "Mostrar a grade", delegate { m.MostrarGrade = !m.MostrarGrade; Chaves(); });
            btSnap = Botao(g, "Atrair\np/ grade", 37, "O clique do mouse cai nos pontos da grade", delegate { m.AtrairGrade = !m.AtrairGrade; Chaves(); });
            g = Grupo(fitaOp, "Aparência");
            Botao(g, "Tema", 16, "Alternar entre tema escuro e claro", delegate { t = Tema.Criar(!t.Escuro); AplicarTema(); });
            g = Grupo(fitaOp, "Ajuda");
            Botao(g, "Como\nusar", 38, "Resumo de uso (F1)", delegate { Ajuda(); });
            Fechar(fitaOp);

            // barra de baixo: grade, coordenadas, mensagens
            st = new StatusStrip();
            st.SizingGrip = false;
            sGrade = Chave("Grade", "Mostrar a grade", delegate { m.MostrarGrade = !m.MostrarGrade; Chaves(); });
            sSnap = Chave("Atrair para a grade", "O clique do mouse cai nos pontos da grade. Medidas digitadas nunca são arredondadas.", delegate { m.AtrairGrade = !m.AtrairGrade; Chaves(); });
            st.Items.Add(new ToolStripStatusLabel("  Espaçamento (m):"));
            tbGrade = new ToolStripTextBox { AutoSize = false, Width = Ui.Z(52), BorderStyle = BorderStyle.FixedSingle, TextBoxTextAlign = HorizontalAlignment.Right };
            tbGrade.KeyDown += delegate(object o, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; LerGrade(); tela.Focus(); } };
            tbGrade.LostFocus += delegate { LerGrade(); };
            st.Items.Add(tbGrade);
            coord = new ToolStripStatusLabel("") { AutoSize = false, Width = Ui.Z(210), TextAlign = ContentAlignment.MiddleLeft };
            st.Items.Add(coord);
            msg = new ToolStripStatusLabel("") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            st.Items.Add(msg);
            status = new ToolStripStatusLabel("") { TextAlign = ContentAlignment.MiddleRight };
            st.Items.Add(status);

            // desenho + faixa de medida digitada
            Panel centro = new Panel { Dock = DockStyle.Fill };
            tela = new Tela { Dock = DockStyle.Fill };
            tela.Paint += delegate(object o, PaintEventArgs e) { Desenhar(e.Graphics); };
            tela.MouseDown += TelaMouseDown;
            tela.MouseMove += TelaMouseMove;
            tela.MouseUp += TelaMouseUp;
            tela.MouseWheel += delegate(object o, MouseEventArgs e) { Zoom(e.Delta > 0 ? 1.2 : 1 / 1.2, e.X, e.Y); };
            tela.MouseDoubleClick += delegate(object o, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Middle) Enquadrar();
                else if (e.Button == MouseButtons.Left && ferr == 0 && abaRes && sel.Count == 1 && sel[0] is No && ((No)sel[0]).Placa != null) MostrarPlaca();
                else if (e.Button == MouseButtons.Left && ferr == 0 && abaRes && sel.Count == 1 && sel[0] is Barra) VerificarBarra((Barra)sel[0]);
                else if (e.Button == MouseButtons.Left && ferr == 0 && !abaRes && sel.Count == 1 && Medir(sel[0])) Registrar();
            };
            tela.MouseEnter += delegate { dentro = true; };
            tela.MouseLeave += delegate { dentro = false; sobre = null; tela.Invalidate(); };
            tela.KeyDown += delegate(object o, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Delete) Excluir();
                else if (e.KeyCode == Keys.Enter && ferr == 1) { temUlt = false; AtualizarEntrada(); tela.Invalidate(); }
            };
            tela.KeyPress += delegate(object o, KeyPressEventArgs e)
            {
                char ch = e.KeyChar;
                if ((ch == 'b' || ch == 'B') && ferr != 1 && !abaRes) { Ferramenta(1); e.Handled = true; }
                // comecou a digitar um numero com o mouse no desenho: vai para o primeiro campo da medida
                else if ((ferr == 1 || ferr == 2) && (char.IsDigit(ch) || ch == '-' || ch == ',' || ch == '.'))
                {
                    tbA.Focus(); tbA.Text = ch.ToString(); tbA.SelectionStart = 1; e.Handled = true;
                }
            };

            entrada = new Panel { Dock = DockStyle.Bottom, Height = Ui.Z(40) };
            lbEtapa = new Label { AutoSize = true, Left = Ui.Z(12), Top = Ui.Z(12), Font = Ui.F(9f, FontStyle.Bold) };
            cbModo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, Width = Ui.Z(168), Top = Ui.Z(8) };
            cbModo.Items.AddRange(new object[] { "Comprimento e ângulo", "Distância em X e em Y", "Coordenadas X e Y" });
            cbModo.SelectedIndex = modoPref;
            cbModo.SelectedIndexChanged += delegate { if (!carregando) { modoPref = cbModo.SelectedIndex; AtualizarEntrada(); tela.Invalidate(); } };
            lbA = new Label { AutoSize = true, Top = Ui.Z(12) };
            tbA = new TextBox { Width = Ui.Z(74), Top = Ui.Z(9), BorderStyle = BorderStyle.FixedSingle, TextAlign = HorizontalAlignment.Right };
            lbB = new Label { AutoSize = true, Top = Ui.Z(12) };
            tbB = new TextBox { Width = Ui.Z(74), Top = Ui.Z(9), BorderStyle = BorderStyle.FixedSingle, TextAlign = HorizontalAlignment.Right };
            btInserir = new Button { Text = "Inserir  (Enter)", FlatStyle = FlatStyle.Flat, Width = Ui.Z(108), Height = Ui.Z(26), Top = Ui.Z(7), Tag = "primario", Cursor = Cursors.Hand };
            btInserir.Click += delegate { InserirDigitado(); };
            lbDica = new Label { AutoSize = true, Top = Ui.Z(12) };
            KeyEventHandler kd = delegate(object o, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; InserirDigitado(); } };
            tbA.KeyDown += kd; tbB.KeyDown += kd;
            tbA.TextChanged += delegate { tela.Invalidate(); };
            tbB.TextChanged += delegate { tela.Invalidate(); };
            entrada.Controls.AddRange(new Control[] { lbEtapa, cbModo, lbA, tbA, lbB, tbB, btInserir, lbDica });
            centro.Controls.Add(tela);
            centro.Controls.Add(entrada);

            // paleta de consulta (so na aba Resultados)
            lado = new Panel { Dock = DockStyle.Right, Width = Ui.Z(290), Visible = false };
            tituloFicha = new Label { Dock = DockStyle.Top, Height = Ui.Z(26), Text = "CONSULTA", Font = Ui.F(8.5f), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(Ui.Z(10), 0, 0, 0) };
            info = new Label { Dock = DockStyle.Fill, Padding = new Padding(Ui.Z(12), Ui.Z(10), Ui.Z(8), Ui.Z(6)), Font = Ui.F(9.25f) };
            lado.Controls.Add(info);
            lado.Controls.Add(tituloFicha);
            MontarFichas();

            // abas da fita
            abas = new Panel { Dock = DockStyle.Top, Height = Ui.Z(28) };
            tabs = new[] { NovaAba("Modelo", 0), NovaAba("Resultados", 1), NovaAba("Opções", 2) };
            int xa = Ui.Z(8);
            foreach (Button b in tabs) { b.Left = xa; xa += b.Width + 2; abas.Controls.Add(b); }

            // aba do arquivo aberto, embaixo da fita
            docs = new Panel { Dock = DockStyle.Top, Height = Ui.Z(27) };
            lbDoc = new Label { AutoSize = false, Left = Ui.Z(8), Top = Ui.Z(3), Height = Ui.Z(24), Font = Ui.F(9f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter };
            btMais = new Button { Text = "+", FlatStyle = FlatStyle.Flat, Font = Ui.F(12f), Top = Ui.Z(1), Width = Ui.Z(28), Height = Ui.Z(25), TabStop = false };
            btMais.FlatAppearance.BorderSize = 0;
            btMais.Click += delegate { Novo(); };
            docs.Controls.Add(lbDoc); docs.Controls.Add(btMais);

            Controls.Add(centro);
            Controls.Add(lado);
            Controls.Add(docs);
            Controls.Add(fitaOp);
            Controls.Add(fitaRes);
            Controls.Add(fita);
            Controls.Add(abas);
            Controls.Add(barraTitulo);
            Controls.Add(st);

            HandleCreated += delegate { SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x27); };
            Load += delegate { AplicarTema(); Enquadrar(); tela.Focus(); };
            FormClosing += delegate(object o, FormClosingEventArgs e) { if (!PodeDescartar()) e.Cancel = true; else GravarConfig(); };
            Aba(0);
            AplicarTema();
        }

        static readonly int ALT_FITA = Ui.Z(98), ALT_BOTAO = Ui.Z(74);

        ToolStripButton Botao(ToolStrip ts, string texto, int icone, string dica, EventHandler acao)
        {
            ToolStripButton b = new ToolStripButton(texto, null, acao);
            b.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
            b.TextImageRelation = TextImageRelation.ImageAboveText;
            b.TextAlign = ContentAlignment.TopCenter;
            b.ImageAlign = ContentAlignment.TopCenter;
            b.ToolTipText = dica.Replace("\n", " ");
            b.AutoSize = false;
            int larg = 0;
            foreach (string l in texto.Split('\n')) larg = Math.Max(larg, TextRenderer.MeasureText(l, Font).Width);
            b.Size = new Size(Math.Max(Ui.Z(56), larg + Ui.Z(10)), ALT_BOTAO);
            ts.Items.Add(b);
            icones.Add(new KeyValuePair<ToolStripItem, int>(b, icone));
            return b;
        }

        // Botao pequeno da barra de acesso rapido, na barra de titulo.
        void Rapido(string dica, int icone, EventHandler acao)
        {
            ToolStripButton b = new ToolStripButton("", null, acao);
            b.DisplayStyle = ToolStripItemDisplayStyle.Image;
            b.ToolTipText = dica;
            b.AutoSize = false;
            b.Size = new Size(Ui.Z(30), Ui.Z(26));
            b.Margin = new Padding(Ui.Z(1), Ui.Z(1), Ui.Z(1), Ui.Z(1));
            qat.Items.Add(b);
            icones.Add(new KeyValuePair<ToolStripItem, int>(b, icone));
        }

        Button BotaoJanela(string glifo, EventHandler acao)
        {
            Button b = new Button { Text = glifo, Dock = DockStyle.Right, Width = Ui.Z(46), FlatStyle = FlatStyle.Flat, Font = new Font("Segoe MDL2 Assets", 8f), TabStop = false };
            b.FlatAppearance.BorderSize = 0;
            b.Click += acao;
            return b;
        }

        // Logotipo e titulo centralizado na barra de titulo.
        void PintarTitulo(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.ScaleTransform(Ui.S, Ui.S);
            float largura = barraTitulo.Width / Ui.S, altura = barraTitulo.Height / Ui.S;
            using (Brush ba = new SolidBrush(t.Acento))
            using (Font fl = Ui.F(11f / Ui.S, FontStyle.Bold))
            using (Font ft = Ui.F(9.5f / Ui.S))
            using (Brush bt = new SolidBrush(t.UiTexto))
            {
                g.FillRectangle(ba, 10, 6, 24, 22);
                g.DrawString("P", fl, Brushes.White, 13, 6);
                SizeF sz = g.MeasureString(Text, ft);
                float x = Math.Max(qat.Right / Ui.S + 12, (largura - sz.Width) / 2);
                g.DrawString(Text, ft, bt, x, (altura - sz.Height) / 2);
            }
        }

        // Grupo da fita: botoes em cima, nome do grupo embaixo, divisa fina a direita.
        ToolStrip Grupo(Panel fitaDona, string titulo)
        {
            Panel gp = new Panel { Height = ALT_FITA };
            ToolStrip ts = new ToolStrip();
            ts.Dock = DockStyle.Fill;
            ts.AutoSize = false;
            ts.CanOverflow = false;
            ts.GripStyle = ToolStripGripStyle.Hidden;
            ts.ImageScalingSize = new Size(Ui.Z(32), Ui.Z(32));
            ts.Padding = new Padding(Ui.Z(5), Ui.Z(3), Ui.Z(5), 0);
            Label lt = new Label { Text = titulo, Dock = DockStyle.Bottom, Height = Ui.Z(20), TextAlign = ContentAlignment.TopCenter, Font = Ui.F(8.75f) };
            Panel dv = new Panel { Dock = DockStyle.Right, Width = Ui.Z(1) };
            gp.Controls.Add(ts);
            gp.Controls.Add(lt);
            gp.Controls.Add(dv);
            gp.Tag = ts;
            fitaDona.Controls.Add(gp);
            faixas.Add(ts); titulos.Add(lt); divisas.Add(dv);
            return ts;
        }

        // Depois de criar os botoes: da a largura certa a cada grupo e enfileira da esquerda para a direita.
        static void Fechar(Panel fitaDona)
        {
            int x = 0;
            foreach (Control gp in fitaDona.Controls)
            {
                int w = Ui.Z(14);
                foreach (ToolStripItem it in ((ToolStrip)gp.Tag).Items) w += it.Width + it.Margin.Horizontal;
                gp.SetBounds(x, 0, w, ALT_FITA);
                x += w;
            }
        }

        ToolStripButton Chave(string texto, string dica, EventHandler acao)
        {
            ToolStripButton b = new ToolStripButton(texto, null, acao);
            b.DisplayStyle = ToolStripItemDisplayStyle.Text;
            b.ToolTipText = dica;
            b.Margin = new Padding(Ui.Z(2), Ui.Z(2), Ui.Z(2), Ui.Z(1));
            st.Items.Add(b);
            return b;
        }

        void Chaves()
        {
            sGrade.Checked = btGrade.Checked = m.MostrarGrade;
            sSnap.Checked = btSnap.Checked = m.AtrairGrade;
            sGrade.ForeColor = sGrade.Checked ? t.DestaqueTexto : t.UiTextoFraco;
            sSnap.ForeColor = sSnap.Checked ? t.DestaqueTexto : t.UiTextoFraco;
            if (!tbGrade.Focused) tbGrade.Text = Txt(m.Grade);
            btPeso.Checked = m.PesoProprio; btCortante.Checked = m.Timoshenko; btRotulos.Checked = rotulos;
            tela.Invalidate();
        }

        void LerGrade()
        {
            double v;
            if (Num.Ler(tbGrade.Text, out v) && v > 0) m.Grade = v;
            tbGrade.Text = Txt(m.Grade);
            tela.Invalidate();
        }

        void Titulo()
        {
            string doc = (arquivo == null ? "sem nome" : Path.GetFileName(arquivo)) + (sujo ? " *" : "");
            Text = NOME + " - " + doc;
            if (lbDoc != null)
            {
                lbDoc.Text = doc;
                lbDoc.Width = TextRenderer.MeasureText(doc, lbDoc.Font).Width + Ui.Z(28);
                btMais.Left = lbDoc.Right + Ui.Z(4);
                barraTitulo.Invalidate();
            }
        }

        // ---------- moldura da janela: tira a barra de titulo do sistema e poe a nossa no lugar ----------

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int L, T, R, B; }
        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr h, IntPtr depois, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")]
        static extern bool IsZoomed(IntPtr h);

        protected override void WndProc(ref Message msg)
        {
            const int WM_NCCALCSIZE = 0x83, WM_NCHITTEST = 0x84;
            if (msg.Msg == WM_NCCALCSIZE && msg.WParam != IntPtr.Zero)
            {
                RECT antes = (RECT)Marshal.PtrToStructure(msg.LParam, typeof(RECT));
                base.WndProc(ref msg);
                RECT r = (RECT)Marshal.PtrToStructure(msg.LParam, typeof(RECT));
                // so a faixa do titulo some; as bordas de redimensionar continuam as do sistema.
                // maximizada, a janela passa da tela na espessura da borda: desconta em cima tambem.
                r.T = antes.T + (IsZoomed(Handle) ? r.L - antes.L : 0);
                Marshal.StructureToPtr(r, msg.LParam, false);
                msg.Result = IntPtr.Zero;
                return;
            }
            if (msg.Msg == WM_NCHITTEST)
            {
                base.WndProc(ref msg);
                if ((int)msg.Result == 1 && barraTitulo != null)
                {
                    long lp = msg.LParam.ToInt64();
                    Point p = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
                    bool max = IsZoomed(Handle);
                    if (!max && p.Y < 6) msg.Result = (IntPtr)(p.X < 10 ? 13 : p.X > ClientSize.Width - 10 ? 14 : 12);   // redimensionar por cima
                    else if (p.Y < barraTitulo.Height) msg.Result = (IntPtr)2;                                         // arrastar pela barra
                }
                return;
            }
            base.WndProc(ref msg);
        }

        // Cores da moldura do sistema (dialogos usam a barra de titulo do Windows, na cor do tema).
        void Moldura(IntPtr h)
        {
            try
            {
                int v = t.Escuro ? 1 : 0;
                DwmSetWindowAttribute(h, 20, ref v, 4);
                int cor = t.UiBorda.R | (t.UiBorda.G << 8) | (t.UiBorda.B << 16);
                DwmSetWindowAttribute(h, 35, ref cor, 4);
                int borda = t.Acento.R | (t.Acento.G << 8) | (t.Acento.B << 16);
                DwmSetWindowAttribute(h, 34, ref borda, 4);
            }
            catch { }
        }

        void Ajuda()
        {
            MessageBox.Show(this,
                "ABA MODELO: montar a estrutura\n\n" +
                "Nenhuma ferramenta fica ligada. Você clica nela, usa, e o botão direito (ou Esc) encerra.\n\n" +
                "BARRA: clique os pontos. Para não depender da grade, depois do primeiro ponto digite o comprimento\n" +
                "na faixa de baixo e tecle Enter: a barra sai na direção do mouse. O ângulo é opcional.\n" +
                "Shift trava na horizontal/vertical. O cursor gruda sozinho em nós e barras.\n\n" +
                "APOIO, RÓTULA, CARGAS, SEÇÃO, MATERIAL: abre uma janela. Escolha, clique em Aceitar e depois\n" +
                "clique nos nós ou barras que vão receber (ou arraste uma janela). Botão direito encerra.\n" +
                "Se já houver nós ou barras selecionados, aplica direto neles.\n\n" +
                "REMOVER: escolha o que tirar (apoio, carga, rótula) e clique nos nós ou barras.\n\n" +
                "BOTÃO DIREITO sobre um nó ou barra: menu com tudo que dá para fazer nele, inclusive remover.\n" +
                "DUPLO CLIQUE num nó ou barra: digitar a posição ou o comprimento.\n\n" +
                "ABA RESULTADOS: só visualização\n\n" +
                "Normal, Cortante, Momento, Deformada. Passe o mouse sobre uma barra para ler o valor no ponto.\n" +
                "Clique num nó ou barra para ver o resumo no painel Consulta.\n\n" +
                "MOUSE\n\n" +
                "Roda = zoom. Arrastar com a roda ou com o botão direito = mover a vista. Duplo clique na roda = enquadrar.\n" +
                "Shift + clique soma à seleção. Arrastar no vazio = janela de seleção.",
                NOME, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ---------- atributos: abre a janela, Aceitar, clica onde quer aplicar, botao direito termina ----------

        static string Txt(double v) { return v.ToString("0.######", CultureInfo.GetCultureInfo("pt-BR")); }

        static double Val(TextBox tb)
        {
            double v;
            return Num.Ler(tb.Text, out v) ? v : 0;
        }

        void MontarFichas()
        {
            Ficha f;

            // 0 medidas
            f = fichas[0] = new Ficha(false);
            gLbl = f.Texto("Medidas da barra:", false);
            gA = f.Campo("X (m)"); gB = f.Campo("Y (m)");
            f.Texto("Na barra, o nó final se desloca para dar o comprimento e o ângulo digitados.", true);

            // 1 material
            f = fichas[1] = new Ficha(false);
            List<string> nm = new List<string>();
            foreach (Material mt in Biblioteca.Materiais) nm.Add(mt.Nome);
            nm.Add("Outro (digitar E)");
            mLista = f.Combo(nm.ToArray());
            mE = f.Campo("Módulo E (MPa)");
            mInfo = f.Texto("x\nx", true);
            mLista.SelectedIndexChanged += delegate { MaterialMudou(); };

            // 2 secao
            f = fichas[2] = new Ficha(false);
            List<string> tipos = new List<string>(Biblioteca.Familias);
            nFam = tipos.Count;
            tipos.Add("Retangular maciça"); tipos.Add("Circular maciça"); tipos.Add("Genérica (A e I)");
            sTipo = f.Combo(tipos.ToArray());
            sGer = f.Sub(268); sRet = f.Sub(268); sCirc = f.Sub(268); sGen = f.Sub(268);
            sSerie = sGer.Combo("Todas as bitolas");
            sLista = sGer.Lista(150);
            sProps = sGer.Texto("x\nx\nx\nx", true);
            sB = sRet.Campo("Largura b (cm)"); sH = sRet.Campo("Altura h (cm)");
            sD = sCirc.Campo("Diâmetro (cm)");
            sA = sGen.Campo("Área A (cm²)"); sI = sGen.Campo("Inércia I (cm4)");
            f.Espaco(270);
            sPrevia = f.Livre(new Panel(), 96);
            sPrevia.Paint += delegate(object o, PaintEventArgs e) { DesenharSecao(e.Graphics); };
            sDeitado = f.Check("Perfil deitado (flexão no eixo fraco)");
            sTipo.SelectedIndexChanged += delegate { if (!carregando) { EncherSeries(null); SecaoMudou(); } };
            sSerie.SelectedIndexChanged += delegate { if (!carregando) EncherPerfis(null); };
            sLista.SelectedIndexChanged += delegate { SecaoMudou(); };
            sDeitado.CheckedChanged += delegate { sPrevia.Invalidate(); };
            EventHandler rp = delegate { sPrevia.Invalidate(); };
            sB.TextChanged += rp; sH.TextChanged += rp; sD.TextChanged += rp;
            sB.Text = "20"; sH.Text = "50"; sD.Text = "30";
            EncherSeries("W 200 x 26,6");

            // 3 apoio
            f = fichas[3] = new Ficha(false);
            f.Linha(new[] { "Engaste", "Fixo", "Móvel", "Livre" }, new EventHandler[] {
                delegate { aX.Checked = true; aY.Checked = true; aG.Checked = true; }, delegate { aX.Checked = true; aY.Checked = true; aG.Checked = false; },
                delegate { aX.Checked = false; aY.Checked = true; aG.Checked = false; }, delegate { aX.Checked = false; aY.Checked = false; aG.Checked = false; } });
            aX = f.Check("Travar deslocamento em X");
            aY = f.Check("Travar deslocamento em Y");
            aG = f.Check("Travar o giro");
            f.Secao("Molas (apoio elástico)");
            aKx = f.Campo("Mola em X (kN/m)"); aKy = f.Campo("Mola em Y (kN/m)"); aKg = f.Campo("Mola de giro (kN.m/rad)");
            f.Secao("Recalque (só onde está travado)");
            aRx = f.Campo("Em X (mm)"); aRy = f.Campo("Em Y (mm)");
            aX.Checked = aY.Checked = aG.Checked = true;
            foreach (TextBox tb in new[] { aKx, aKy, aKg, aRx, aRy }) tb.Text = "0";

            // 4 rotula
            f = fichas[4] = new Ficha(false);
            rOpcao = f.Combo("Na ponta da barra em que eu clicar", "Nas duas pontas da barra", "No nó (todas as barras que chegam nele)");
            f.Texto("A rótula aparece como uma bolinha na ponta da barra. Para tirar, use Remover.", true);

            // 5 carga no no
            f = fichas[5] = new Ficha(false);
            nFx = f.Campo("Fx (kN)"); nFy = f.Campo("Fy (kN)"); nMz = f.Campo("Mz (kN.m)");
            f.Texto("Fx positivo para a direita. Fy positivo para cima: peso é negativo. Mz positivo no sentido anti-horário.", true);
            nFx.Text = "0"; nFy.Text = "-10"; nMz.Text = "0";

            // 6 carga na barra
            f = fichas[6] = new Ficha(false);
            f.Secao("Uniforme");
            bQy = f.Campo("Vertical (kN/m)"); bQx = f.Campo("Horizontal (kN/m)");
            f.Texto("Por metro de barra. Vertical para baixo é negativo (ex.: -15). Horizontal positivo é para a direita.", true);
            f.Secao("Linear, perpendicular à barra");
            bQi = f.Campo("No início (kN/m)"); bQf = f.Campo("No fim (kN/m)");
            f.Texto("Varia em linha reta do início ao fim da barra (triangular ou trapezoidal).", true);
            bQy.Text = "-10"; bQx.Text = "0"; bQi.Text = "0"; bQf.Text = "0";

            // 7 placa de base
            f = fichas[7] = new Ficha(false);
            f.Secao("Placa");
            pModo = f.Combo("Medidas pelo perfil do pilar + sobra", "Digitar as medidas da placa");
            pPerf = f.Sub(60); pDig = f.Sub(60);
            pSobra = pPerf.Campo("Sobra de cada lado (mm)");
            pH = pDig.Campo("Comprimento, no plano (mm)"); pB = pDig.Campo("Largura (mm)");
            f.Espaco(60);
            pEsp = f.Campo("Espessura (mm)");
            f.Secao("Parafusos");
            pFil = f.Campo("Fileiras (ao longo do plano)");
            pPor = f.Campo("Parafusos por fileira");
            pBorda = f.Campo("Do furo à borda (mm)");
            List<string> dm = new List<string>(), cl = new List<string>();
            foreach (double d in Parafusos.D) dm.Add("M" + d);
            for (int i = 0; i < Parafusos.Classes.Length; i++)
                cl.Add("Classe " + Parafusos.Classes[i] + "   (prova " + Parafusos.Sp[i] + " MPa, " + Parafusos.Faixa[i] + ")");
            pDiam = f.Combo(dm.ToArray());
            pClasse = f.Combo(cl.ToArray());
            pRosca = f.Check("Rosca no plano de corte");
            f.Secao("Tração nos parafusos");
            pPivo = f.Combo("Placa gira na borda comprimida", "Placa gira na mesa comprimida do perfil");
            f.Texto("O comprimento fica no plano do pórtico (direção do momento). Os parafusos vão no contorno da placa.", true);
            pModo.SelectedIndexChanged += delegate { pPerf.Visible = pModo.SelectedIndex == 0; pDig.Visible = pModo.SelectedIndex == 1; };
            pPerf.Visible = true;
            pSobra.Text = "100"; pH.Text = "400"; pB.Text = "300"; pEsp.Text = "19"; pFil.Text = "2"; pPor.Text = "2"; pBorda.Text = "50";
            pDiam.SelectedIndex = 4; pClasse.SelectedIndex = 3; pRosca.Checked = true;

            MaterialMudou();
            SecaoMudou();
        }

        // Mostra a ficha k numa janela com Aceitar / Cancelar.
        bool Dialogo(int k)
        {
            Ficha f = fichas[k];
            using (Form d = new Form())
            {
                d.Text = NomeFicha[k];
                d.FormBorderStyle = FormBorderStyle.FixedDialog;
                d.MaximizeBox = false; d.MinimizeBox = false; d.ShowInTaskbar = false;
                d.StartPosition = FormStartPosition.CenterParent;
                d.Font = Font;
                d.BackColor = t.UiFundo;
                d.ClientSize = new Size(Ui.Z(Ficha.W + 36), Ui.Z(f.Altura + 66));
                Panel pe = new Panel { Dock = DockStyle.Bottom, Height = Ui.Z(54), BackColor = t.UiTitulo };
                Button ok = BotaoDlg("OK", true), ca = BotaoDlg("Cancelar", false);
                ok.DialogResult = DialogResult.OK; ca.DialogResult = DialogResult.Cancel;
                ca.Location = new Point(d.ClientSize.Width - ca.Width - Ui.Z(18), Ui.Z(11));
                ok.Location = new Point(ca.Left - ok.Width - Ui.Z(8), Ui.Z(11));
                pe.Controls.Add(ok); pe.Controls.Add(ca);
                f.Pintar(t);
                f.AutoScroll = false;
                f.Visible = true;
                d.Controls.Add(f);
                d.Controls.Add(pe);
                d.AcceptButton = ok; d.CancelButton = ca;
                d.HandleCreated += delegate
                {
                    Moldura(d.Handle);
                };
                dlgAtual = d;
                bool r = d.ShowDialog(this) == DialogResult.OK;
                d.Controls.Remove(f);
                dlgAtual = null;
                return r;
            }
        }

        // Se houver um objeto selecionado, a janela abre com os valores dele.
        void CarregarFicha(int k)
        {
            No n = null; Barra b = null;
            foreach (object o in sel) { if (n == null) n = o as No; if (b == null) b = o as Barra; }
            if (k == 1 && b != null)
            {
                int i = mLista.Items.IndexOf(b.Material);
                if (i >= 0) mLista.SelectedIndex = i;
                else
                {
                    Material mt = Biblioteca.AcharMaterial(b.Material);
                    mLista.SelectedIndex = mLista.Items.Count - 1;
                    if (mt != null) mE.Text = Txt(mt.E);
                }
                MaterialMudou();
            }
            else if (k == 2 && b != null)
            {
                Secao s = Biblioteca.AcharSecao(b.Secao);
                sDeitado.Checked = b.EixoFraco;
                if (s != null && Biblioteca.Perfis.Contains(s))
                {
                    carregando = true;
                    sTipo.SelectedIndex = Biblioteca.Familias.IndexOf(s.Familia);
                    carregando = false;
                    EncherSeries(s.Nome);
                }
                SecaoMudou();
            }
            else if (k == 3 && n != null)
            {
                aX.Checked = n.ApoioX; aY.Checked = n.ApoioY; aG.Checked = n.ApoioGiro;
                aKx.Text = Txt(n.MolaX); aKy.Text = Txt(n.MolaY); aKg.Text = Txt(n.MolaGiro);
                aRx.Text = Txt(n.RecalqueX); aRy.Text = Txt(n.RecalqueY);
            }
            else if (k == 5 && n != null && n.TemCarga) { nFx.Text = Txt(n.Fx); nFy.Text = Txt(n.Fy); nMz.Text = Txt(n.Mz); }
            else if (k == 7 && n != null && n.Placa != null)
            {
                PlacaBase p = n.Placa;
                pModo.SelectedIndex = p.PeloPerfil ? 0 : 1;
                pSobra.Text = Txt(p.Sobra);
                if (!p.PeloPerfil) { pH.Text = Txt(p.H); pB.Text = Txt(p.B); }
                pEsp.Text = Txt(p.Esp); pFil.Text = p.Fileiras.ToString(); pPor.Text = p.PorFileira.ToString(); pBorda.Text = Txt(p.Borda);
                pDiam.SelectedIndex = Math.Max(0, Array.IndexOf(Parafusos.D, p.Diametro));
                pClasse.SelectedIndex = Math.Max(0, Array.IndexOf(Parafusos.Classes, p.Classe));
                pRosca.Checked = p.RoscaNoCorte;
                pPivo.SelectedIndex = p.PivoNaMesa ? 1 : 0;
            }
            else if (k == 6 && b != null && b.TemCarga)
            {
                bQy.Text = Txt(b.QGlobalY); bQx.Text = Txt(b.QGlobalX); bQi.Text = Txt(b.QLocalIni); bQf.Text = Txt(b.QLocalFim);
            }
        }

        // Botao de atributo: janela -> Aceitar -> aplica nos selecionados, ou espera os cliques.
        void Atributo(int k)
        {
            if (abaRes) Aba(false);
            ferr = 0; temUlt = false; caixa = false;
            AtualizarEntrada();
            acNo = null; acBarra = null;
            bool usarSel = true;
            if (k == 0)
            {
                if (sel.Count == 1) { if (Medir(sel[0])) Registrar(); return; }
                acNo = delegate(No n) { Medir(n); };
                acBarra = delegate(Barra b, double kk) { Medir(b); };
                usarSel = false;
            }
            else
            {
                CarregarFicha(k);
                if (!Dialogo(k)) { tela.Invalidate(); return; }
            }
            if (k == 1)
            {
                string nome;
                if (mLista.SelectedIndex == mLista.Items.Count - 1)
                {
                    if (Val(mE) <= 0) { Aviso("Material: faltou o módulo E."); return; }
                    nome = "E=" + Txt(Val(mE));
                }
                else nome = (string)mLista.SelectedItem;
                acBarra = delegate(Barra b, double kk) { b.Material = nome; };
            }
            else if (k == 2)
            {
                string nome = SecaoDaFicha();
                bool deitado = sDeitado.Checked;
                if (nome == null || Biblioteca.AcharSecao(nome) == null) { Aviso("Seção: faltaram as medidas."); return; }
                acBarra = delegate(Barra b, double kk) { b.Secao = nome; b.EixoFraco = deitado; };
            }
            else if (k == 3)
            {
                bool x = aX.Checked, y = aY.Checked, g = aG.Checked;
                double kx = Val(aKx), ky = Val(aKy), kg = Val(aKg), rx = Val(aRx), ry = Val(aRy);
                acNo = delegate(No n)
                {
                    n.ApoioX = x; n.ApoioY = y; n.ApoioGiro = g;
                    n.MolaX = kx; n.MolaY = ky; n.MolaGiro = kg; n.RecalqueX = rx; n.RecalqueY = ry;
                };
            }
            else if (k == 4)
            {
                int op = rOpcao.SelectedIndex;
                if (op == 0)
                {
                    // a ponta mais perto do clique
                    acBarra = delegate(Barra b, double kk) { if (kk < 0) return; if (kk < 0.5) b.RotulaInicio = true; else b.RotulaFim = true; };
                    usarSel = false;
                }
                else if (op == 1) acBarra = delegate(Barra b, double kk) { b.RotulaInicio = b.RotulaFim = true; };
                else acNo = delegate(No n) { RotulasDoNo(n, true); };
            }
            else if (k == 5)
            {
                double fx = Val(nFx), fy = Val(nFy), mz = Val(nMz);
                acNo = delegate(No n) { n.Fx = fx; n.Fy = fy; n.Mz = mz; };
            }
            else if (k == 6)
            {
                double qy = Val(bQy), qx = Val(bQx), qi = Val(bQi), qf = Val(bQf);
                acBarra = delegate(Barra b, double kk) { b.QGlobalY = qy; b.QGlobalX = qx; b.QLocalIni = qi; b.QLocalFim = qf; };
            }
            else if (k == 7)
            {
                PlacaBase pb = new PlacaBase
                {
                    PeloPerfil = pModo.SelectedIndex == 0, Sobra = Val(pSobra), H = Val(pH), B = Val(pB), Esp = Val(pEsp),
                    Fileiras = (int)Math.Round(Val(pFil)), PorFileira = (int)Math.Round(Val(pPor)), Borda = Val(pBorda),
                    Diametro = Parafusos.D[pDiam.SelectedIndex], Classe = Parafusos.Classes[pClasse.SelectedIndex],
                    RoscaNoCorte = pRosca.Checked, PivoNaMesa = pPivo.SelectedIndex == 1
                };
                if (pb.Fileiras < 1 || pb.PorFileira < 1 || pb.Fileiras * pb.PorFileira < 2 || pb.Borda <= 0)
                {
                    Aviso("Placa de base: confira o número de parafusos e a distância do furo à borda.");
                    return;
                }
                acNo = delegate(No n)
                {
                    n.Placa = pb.Copiar();
                    ResPlaca rp = PlacaCalc.Calcular(m, n);
                    if (rp.Erro != "" && !rp.Erro.StartsWith("Sem esforços")) { n.Placa = null; Aviso(rp.Erro); }
                };
            }
            Iniciar(NomeFicha[k], usarSel, k != 0);
        }

        // Placa vista de lado no desenho: chapa perpendicular ao pilar, com os parafusos para fora.
        void DesenharPlaca(Graphics g, No n, PointF p)
        {
            ResPlaca r = PlacaCalc.Calcular(m, n);
            if (r.H <= 0) return;
            Barra pilar = null;
            foreach (Barra b in m.Barras) if (b.N1 == n.Id || b.N2 == n.Id) pilar = b;
            if (pilar == null) return;
            No outro = m.NoPorId(pilar.N1 == n.Id ? pilar.N2 : pilar.N1);
            PointF q = P(outro);
            float dx = q.X - p.X, dy = q.Y - p.Y, l = (float)Math.Sqrt(dx * dx + dy * dy);
            if (l < 1) return;
            dx /= l; dy /= l;
            float nx = -dy, ny = dx, meio = Math.Max(15f, (float)(r.H / 2000 * esc));
            Color cor = t.Escuro ? Color.FromArgb(150, 190, 235) : Color.FromArgb(40, 90, 150);
            using (Pen pp = new Pen(cor, 4.5f))
            using (Pen pf = new Pen(cor, 1.8f))
            {
                g.DrawLine(pp, p.X - nx * meio, p.Y - ny * meio, p.X + nx * meio, p.Y + ny * meio);
                float fora = Math.Max(4f, meio - Math.Max(4f, (float)(n.Placa.Borda / 1000 * esc)));
                foreach (float s in new[] { -fora, fora })
                    g.DrawLine(pf, p.X + nx * s + dx * 7, p.Y + ny * s + dy * 7, p.X + nx * s - dx * 11, p.Y + ny * s - dy * 11);
            }
        }

        // ---------- janelas de verificacao (ideia do CYPE: ver o que passa e comparar com os outros da serie) ----------

        static readonly Color CorOk = Color.FromArgb(82, 196, 122), CorRuim = Color.FromArgb(238, 104, 98), CorQuase = Color.FromArgb(232, 180, 72);

        Form NovaJanela(string titulo, int larg, int alt)
        {
            Form d = new Form();
            d.Text = titulo;
            d.FormBorderStyle = FormBorderStyle.FixedDialog;
            d.MaximizeBox = false; d.MinimizeBox = false; d.ShowInTaskbar = false;
            d.StartPosition = FormStartPosition.CenterParent;
            d.Font = Font;
            d.BackColor = t.Ui;
            d.ForeColor = t.UiTexto;
            d.ClientSize = new Size(Ui.Z(larg), Ui.Z(alt));
            d.HandleCreated += delegate { Moldura(d.Handle); };
            d.KeyPreview = true;
            d.KeyDown += delegate(object o, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) d.Close(); };
            return d;
        }

        // Botao de dialogo no padrao do CAD: cinza, borda fina; o botao principal tem a borda azul.
        Button BotaoDlg(string texto, bool principal)
        {
            Button b = new Button { Text = texto, FlatStyle = FlatStyle.Flat, BackColor = t.Escuro ? Color.FromArgb(72, 83, 102) : Color.FromArgb(232, 234, 238), ForeColor = t.UiTexto };
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.BorderColor = principal ? t.Acento : (t.Escuro ? Color.FromArgb(112, 124, 144) : Color.FromArgb(170, 176, 186));
            b.FlatAppearance.MouseOverBackColor = t.UiClaro;
            b.Size = new Size(Math.Max(Ui.Z(96), TextRenderer.MeasureText(texto, Font).Width + Ui.Z(28)), Ui.Z(30));
            return b;
        }

        DataGridView Grade(params string[] colunas)
        {
            DataGridView g = new DataGridView();
            g.ReadOnly = true; g.MultiSelect = false;
            g.AllowUserToAddRows = false; g.AllowUserToDeleteRows = false; g.AllowUserToResizeRows = false;
            g.RowHeadersVisible = false;
            g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            g.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            g.BorderStyle = BorderStyle.None;
            g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            g.ColumnHeadersHeight = Ui.Z(28);
            g.RowTemplate.Height = Ui.Z(25);
            g.EnableHeadersVisualStyles = false;
            g.BackgroundColor = t.Campo; g.GridColor = t.Ui;
            g.DefaultCellStyle.BackColor = t.Campo; g.DefaultCellStyle.ForeColor = t.UiTexto;
            g.DefaultCellStyle.SelectionBackColor = t.Destaque; g.DefaultCellStyle.SelectionForeColor = Color.White;
            g.ColumnHeadersDefaultCellStyle.BackColor = t.UiTitulo; g.ColumnHeadersDefaultCellStyle.ForeColor = t.UiTexto;
            g.ColumnHeadersDefaultCellStyle.SelectionBackColor = t.UiTitulo; g.ColumnHeadersDefaultCellStyle.SelectionForeColor = t.UiTexto;
            foreach (string c in colunas)
            {
                int i = g.Columns.Add(c, c);
                g.Columns[i].SortMode = DataGridViewColumnSortMode.NotSortable;
            }
            return g;
        }

        // Medidor: nome, explicacao e uma barra que enche ate 100% (verde, amarelo perto do limite, vermelho se passou).
        float Medidor(Graphics g, float x, float y, float w, string nome, string detalhe, double ap, bool aplicavel)
        {
            Color cor = !aplicavel ? t.TextoFraco : ap > 1 ? CorRuim : ap > 0.9 ? CorQuase : CorOk;
            string pct = !aplicavel ? "—" : Math.Round(ap * 100) + "%";
            float hb = Ui.Z(10);
            using (Brush bt = new SolidBrush(t.UiTexto))
            using (Brush bf = new SolidBrush(t.UiTextoFraco))
            using (Brush bc = new SolidBrush(cor))
            using (Brush bfundo = new SolidBrush(t.Campo))
            using (Font fn = Ui.F(9.5f, FontStyle.Bold))
            using (Font fd = Ui.F(8.75f))
            {
                g.DrawString(nome, fn, bt, x, y);
                SizeF sp = g.MeasureString(pct, fn);
                g.DrawString(pct, fn, bc, x + w - sp.Width, y);
                y += g.MeasureString(nome, fn).Height + 1;
                g.DrawString(detalhe, fd, bf, new RectangleF(x, y, w, Ui.Z(40)));
                y += g.MeasureString(detalhe, fd, (int)w).Height + Ui.Z(4);
                g.FillRectangle(bfundo, x, y, w, hb);
                if (aplicavel) g.FillRectangle(bc, x, y, (float)(w * Math.Min(1, Math.Max(0.01, ap))), hb);
            }
            return y + hb + Ui.Z(16);
        }

        void Cabecalho(Label cab, bool aplicavel, bool passa, string texto)
        {
            cab.Text = "   " + (aplicavel ? (passa ? "PASSA" : "NÃO PASSA") : "SEM VERIFICAÇÃO") + "     " + texto;
            cab.BackColor = !aplicavel ? t.UiTitulo : passa ? Color.FromArgb(34, 96, 62) : Color.FromArgb(128, 44, 44);
            cab.ForeColor = Color.White;
        }

        void CompararPerfis()
        {
            if (sel.Count == 1 && sel[0] is Barra) VerificarBarra((Barra)sel[0]);
            else Aviso("Clique na barra que quer verificar e aperte Comparar perfis (ou dê duplo clique na barra).");
        }

        // Verificacao da barra + lista dos outros perfis da mesma familia com os mesmos esforcos.
        void VerificarBarra(Barra b)
        {
            if (!res.Ok || b.Res == null) { Aviso("Ainda não há resultados: " + res.Erro); return; }
            using (Form d = NovaJanela("Verificação da barra", 980, 620))
            {
                Label cab = new Label { Dock = DockStyle.Top, Height = Ui.Z(44), TextAlign = ContentAlignment.MiddleLeft, Font = Ui.F(11.5f, FontStyle.Bold) };
                Tela esq = new Tela { Dock = DockStyle.Left, Width = Ui.Z(390), BackColor = t.Ui };
                Panel rod = new Panel { Dock = DockStyle.Bottom, Height = Ui.Z(52), BackColor = t.UiTitulo };
                Panel dir = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Ui.Z(8), Ui.Z(10), Ui.Z(14), Ui.Z(10)) };
                Label lbFam = new Label { Dock = DockStyle.Top, Height = Ui.Z(26), ForeColor = t.UiTexto };
                ComboBox filtro = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = t.Campo, ForeColor = t.UiTexto };
                Panel folga = new Panel { Dock = DockStyle.Top, Height = Ui.Z(8) };
                DataGridView gr = Grade("Perfil", "kg/m", "Aproveitamento", "Limitado por", "Situação");
                gr.Dock = DockStyle.Fill;
                gr.Columns[0].FillWeight = 150; gr.Columns[1].FillWeight = 60; gr.Columns[2].FillWeight = 95; gr.Columns[3].FillWeight = 85; gr.Columns[4].FillWeight = 125;
                dir.Controls.Add(gr); dir.Controls.Add(folga); dir.Controls.Add(filtro); dir.Controls.Add(lbFam);

                Label l1 = new Label { Text = "Segurança mínima", AutoSize = true, Left = Ui.Z(14), Top = Ui.Z(17), ForeColor = t.UiTexto };
                TextBox tbSeg = new TextBox { Left = Ui.Z(134), Top = Ui.Z(14), Width = Ui.Z(52), BorderStyle = BorderStyle.FixedSingle, BackColor = t.Campo, ForeColor = t.UiTexto, TextAlign = HorizontalAlignment.Right, Text = Txt(m.Seguranca) };
                Label l2 = new Label { Text = "Flecha máxima  L /", AutoSize = true, Left = Ui.Z(206), Top = Ui.Z(17), ForeColor = t.UiTexto };
                TextBox tbFle = new TextBox { Left = Ui.Z(328), Top = Ui.Z(14), Width = Ui.Z(52), BorderStyle = BorderStyle.FixedSingle, BackColor = t.Campo, ForeColor = t.UiTexto, TextAlign = HorizontalAlignment.Right, Text = Txt(m.LimiteFlecha) };
                Button btUsar = BotaoDlg("Usar o perfil selecionado", true), btFim = BotaoDlg("Fechar", false);
                btFim.Location = new Point(d.ClientSize.Width - btFim.Width - Ui.Z(14), Ui.Z(11));
                btUsar.Location = new Point(btFim.Left - btUsar.Width - Ui.Z(8), Ui.Z(11));
                btFim.Click += delegate { d.Close(); };
                rod.Controls.AddRange(new Control[] { l1, tbSeg, l2, tbFle, btUsar, btFim });
                d.Controls.Add(dir); d.Controls.Add(esq); d.Controls.Add(rod); d.Controls.Add(cab);

                bool enchendo = false;
                Action encher = delegate
                {
                    enchendo = true;
                    Secao atual = Biblioteca.AcharSecao(b.Secao);
                    Verif va = Verificador.Barra(m, b, atual);
                    Cabecalho(cab, va.Aplicavel, va.Passa, b.Secao + (va.Aplicavel ? "   —   aproveitamento " + Math.Round(va.Ap * 100) + "% (" + va.Critico + ")" : "   —   " + va.Motivo));
                    bool catalogo = atual != null && atual.Familia != "";
                    lbFam.Text = catalogo ? "Outros perfis de \"" + atual.Familia + "\", com os mesmos esforços desta barra:" : "Esta seção não é de catálogo: não há família para comparar.";
                    if (filtro.Items.Count == 0)
                    {
                        filtro.Items.Add("Toda a família");
                        if (catalogo) filtro.Items.Add("Só a bitola " + atual.Serie);
                        filtro.SelectedIndex = catalogo && Biblioteca.DaFamilia(atual.Familia, null).Count > 120 ? 1 : 0;
                    }
                    List<Secao> cands = catalogo ? Biblioteca.DaFamilia(atual.Familia, filtro.SelectedIndex == 1 ? atual.Serie : null) : new List<Secao>();
                    if (!catalogo && atual != null) cands.Add(atual);
                    cands.Sort(delegate(Secao p, Secao q) { return p.Massa.CompareTo(q.Massa); });
                    gr.Rows.Clear();
                    bool achouLeve = false;
                    int linhaAtual = -1;
                    foreach (Secao s in cands)
                    {
                        Verif v = Verificador.Barra(m, b, s);
                        bool eAtual = s == atual;
                        string sit = !v.Aplicavel ? "—" : v.Passa ? "passa" : "não passa";
                        if (v.Passa && !achouLeve) { sit = "passa  (o mais leve)"; achouLeve = true; }
                        int i = gr.Rows.Add(s.Nome + (eAtual ? "   ◄ atual" : ""), s.Massa > 0 ? Num.Fmt(s.Massa, 1) : "", v.Aplicavel ? Math.Round(v.Ap * 100) + "%" : "—", v.Aplicavel ? v.Critico : "", sit);
                        gr.Rows[i].Tag = s;
                        Color cs = !v.Aplicavel ? t.UiTextoFraco : v.Passa ? CorOk : CorRuim;
                        gr.Rows[i].Cells[4].Style.ForeColor = cs; gr.Rows[i].Cells[4].Style.SelectionForeColor = cs;
                        gr.Rows[i].Cells[2].Style.ForeColor = cs; gr.Rows[i].Cells[2].Style.SelectionForeColor = cs;
                        if (eAtual) { linhaAtual = i; gr.Rows[i].DefaultCellStyle.Font = Ui.F(9f, FontStyle.Bold); }
                    }
                    if (linhaAtual >= 0) { gr.CurrentCell = gr.Rows[linhaAtual].Cells[0]; gr.FirstDisplayedScrollingRowIndex = Math.Max(0, linhaAtual - 6); }
                    enchendo = false;
                    esq.Invalidate();
                };
                esq.Paint += delegate(object o, PaintEventArgs e)
                {
                    Graphics g = e.Graphics;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                    Secao s = gr.CurrentRow != null ? gr.CurrentRow.Tag as Secao : Biblioteca.AcharSecao(b.Secao);
                    if (s == null) return;
                    Verif v = Verificador.Barra(m, b, s);
                    float x = Ui.Z(16), y = Ui.Z(14), w = esq.Width - Ui.Z(32);
                    using (Brush bt = new SolidBrush(t.UiTexto))
                    using (Brush bf = new SolidBrush(t.UiTextoFraco))
                    using (Font ft = Ui.F(12f, FontStyle.Bold))
                    using (Font fd = Ui.F(8.75f))
                    {
                        g.DrawString(s.Nome, ft, bt, x, y);
                        y += Ui.Z(26);
                        g.DrawString(b.Material + "     L = " + Num.Fmt(b.Res.L, 2) + " m" + (s.Nome == b.Secao ? "     (perfil atual)" : "     (perfil em teste)"), fd, bf, x, y);
                        y += Ui.Z(30);
                        if (!v.Aplicavel) { g.DrawString(v.Motivo, fd, bt, new RectangleF(x, y, w, Ui.Z(80))); return; }
                        y = Medidor(g, x, y, w, "Tensão normal", "N/A + M/W = " + Num.Fmt(v.Sigma, 1) + " MPa.   Limite fy/n = " + Num.Fmt(v.SigmaAdm, 1) + " MPa.", v.ApTensao, true);
                        y = Medidor(g, x, y, w, "Corte", "Q/As = " + Num.Fmt(v.Tau, 1) + " MPa.   Limite 0,577 fy/n = " + Num.Fmt(v.TauAdm, 1) + " MPa.", v.ApCorte, v.Tau > 0);
                        y = Medidor(g, x, y, w, "Flecha", Num.Fmt(v.Flecha * 1000, 1) + " mm.   Limite L/" + Txt(m.LimiteFlecha) + " = " + Num.Fmt(v.FlechaAdm * 1000, 1) + " mm.", v.ApFlecha, true);
                        y = Medidor(g, x, y, w, "Flambagem", v.Nc > 1e-6 ? "Compressão " + Num.Fmt(v.Nc, 1) + " kN.   Carga crítica / n = " + Num.Fmt(v.Ncr / m.Seguranca, 1) + " kN  (esbeltez " + Math.Round(v.Esbeltez) + ")." : "A barra não está comprimida.", v.ApFlamb, v.Nc > 1e-6);
                        g.DrawString("Verificação simplificada (tensão elástica, flecha e flambagem de Euler/Johnson com o comprimento da barra). Não substitui a NBR 8800: não há flambagem lateral nem local.",
                            fd, bf, new RectangleF(x, esq.Height - Ui.Z(74), w, Ui.Z(70)));
                    }
                };
                gr.SelectionChanged += delegate { if (!enchendo) esq.Invalidate(); };
                filtro.SelectedIndexChanged += delegate { if (!enchendo) encher(); };
                EventHandler criterios = delegate
                {
                    double a = Val(tbSeg), c = Val(tbFle);
                    if (a > 0 && c > 0 && (a != m.Seguranca || c != m.LimiteFlecha)) { m.Seguranca = a; m.LimiteFlecha = c; Registrar(); encher(); }
                };
                tbSeg.Leave += criterios; tbFle.Leave += criterios;
                KeyEventHandler kd = delegate(object o, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; criterios(o, e); } };
                tbSeg.KeyDown += kd; tbFle.KeyDown += kd;
                btUsar.Click += delegate
                {
                    Secao s = gr.CurrentRow != null ? gr.CurrentRow.Tag as Secao : null;
                    if (s == null || s.Nome == b.Secao) return;
                    b.Secao = s.Nome;
                    Registrar();   // recalcula a estrutura: os esforcos mudam um pouco com o perfil novo
                    encher();
                };
                encher();
                d.ShowDialog(this);
            }
            tela.Invalidate();
        }

        // Placa de base: veredito, planta com a tracao em cada parafuso, e as listas "o que passa" de parafuso e de chapa.
        void MostrarPlaca() { MostrarPlaca(null); }

        void MostrarPlaca(No forcar)
        {
            No no = forcar;
            if (no == null && sel.Count == 1 && sel[0] is No && ((No)sel[0]).Placa != null) no = (No)sel[0];
            if (no == null)
            {
                int qt = 0;
                foreach (No n in m.Nos) if (n.Placa != null) { no = n; qt++; }
                if (qt == 0) { Aviso("Não há placa de base no modelo. Na aba Modelo, use Placa base e clique no pé do pilar."); return; }
                if (qt > 1) { Aviso("Há mais de uma placa: clique no nó da placa que quer ver e aperte de novo (ou dê duplo clique no nó)."); return; }
            }
            ResPlaca r = PlacaCalc.Calcular(m, no);
            PlacaBase p = no.Placa;
            if (!r.TemEsforcos) { Aviso("Placa de base: " + r.Erro); return; }
            bool reabrir = false;
            double seg = m.Seguranca;
            bool okPar = r.NComb >= seg, okChapa = p.Esp >= r.EspNec - 1e-6;

            using (Form d = NovaJanela("Placa de base", 1040, 660))
            {
                Label cab = new Label { Dock = DockStyle.Top, Height = Ui.Z(44), TextAlign = ContentAlignment.MiddleLeft, Font = Ui.F(11.5f, FontStyle.Bold) };
                Cabecalho(cab, true, okPar && okChapa, okPar && okChapa ? "parafusos e chapa atendem com segurança " + Txt(seg)
                    : (!okPar ? "parafuso insuficiente" : "") + (!okPar && !okChapa ? " e " : "") + (!okChapa ? "chapa fina demais" : ""));
                Tela planta = new Tela { Dock = DockStyle.Left, Width = Ui.Z(410), BackColor = t.Fundo };
                planta.Paint += delegate(object o, PaintEventArgs e) { PlantaPlaca(e.Graphics, planta.ClientSize, r, p); };
                Panel rod = new Panel { Dock = DockStyle.Bottom, Height = Ui.Z(52), BackColor = t.UiTitulo };
                Tela med = new Tela { Dock = DockStyle.Top, Height = Ui.Z(214), BackColor = t.Ui };
                Panel listas = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Ui.Z(14), 0, Ui.Z(14), Ui.Z(10)) };
                Panel dir = new Panel { Dock = DockStyle.Fill };

                med.Paint += delegate(object o, PaintEventArgs e)
                {
                    Graphics g = e.Graphics;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                    float x = Ui.Z(16), y = Ui.Z(12), w = med.Width - Ui.Z(32);
                    using (Brush bf = new SolidBrush(t.UiTextoFraco))
                    using (Font fd = Ui.F(8.75f))
                    {
                        g.DrawString("No pé do pilar:  N = " + Num.Fmt(Math.Abs(r.N), 1) + " kN (" + (r.N >= 0 ? "tração" : "compressão") + "),  V = " + Num.Fmt(r.V, 1) + " kN,  M = " + Num.Fmt(Math.Abs(r.M), 1) + " kN.m.   " + r.Modelo, fd, bf, new RectangleF(x, y, w, Ui.Z(36)));
                        y += Ui.Z(40);
                    }
                    y = Medidor(g, x, y, w, "Parafuso  M" + Txt(p.Diametro) + " classe " + p.Classe,
                        "O mais puxado leva " + Num.Fmt(r.Tmax, 1) + " kN de tração e " + Num.Fmt(r.Fv, 1) + " kN de corte.   Segurança " + Seg(r.NComb) + "  (mínimo " + Txt(seg) + ").",
                        double.IsInfinity(r.NComb) ? 0 : seg / r.NComb, true);
                    Medidor(g, x, y, w, "Chapa  " + Txt(p.Esp) + " mm",
                        r.EspNec > 0 ? "Espessura necessária: " + Num.Fmt(r.EspNec, 1) + " mm  (fileira com " + Num.Fmt(r.TLinha, 1) + " kN a " + Num.Fmt(r.Braco, 0) + " mm da face do perfil, aço de " + Txt(r.FyChapa) + " MPa)."
                                     : "Sem tração nos parafusos: a chapa não é exigida à flexão.",
                        p.Esp > 0 ? r.EspNec / p.Esp : 2, true);
                };

                int linP = -1, linC = -1;
                DataGridView gp = Grade("Parafuso", "Segurança", "Situação");
                int ic = Array.IndexOf(Parafusos.Classes, p.Classe);
                for (int i = 0; i < Parafusos.D.Length; i++)
                {
                    double ns = PlacaCalc.SegurancaCom(r, p, i, ic);
                    bool passa = ns >= seg, at = Parafusos.D[i] == p.Diametro;
                    int k = gp.Rows.Add("M" + Parafusos.D[i] + (at ? "   ◄ atual" : ""), Seg(ns).Replace("  — NÃO PASSA", ""), passa ? "passa" : "não passa");
                    gp.Rows[k].Tag = i;
                    gp.Rows[k].Cells[2].Style.ForeColor = gp.Rows[k].Cells[2].Style.SelectionForeColor = passa ? CorOk : CorRuim;
                    if (at) { gp.Rows[k].DefaultCellStyle.Font = Ui.F(9f, FontStyle.Bold); linP = k; }
                }
                DataGridView gc = Grade("Chapa (mm)", "Situação");
                bool temAtual = false;
                List<double> esps = new List<double>(PlacaCalc.Chapas);
                if (!esps.Contains(p.Esp) && p.Esp > 0) { esps.Add(p.Esp); esps.Sort(); }
                foreach (double esp in esps)
                {
                    bool passa = esp >= r.EspNec - 1e-6, at = esp == p.Esp;
                    int k = gc.Rows.Add(Txt(esp) + (at ? "   ◄ atual" : ""), passa ? "passa" : "não passa");
                    gc.Rows[k].Tag = esp;
                    gc.Rows[k].Cells[1].Style.ForeColor = gc.Rows[k].Cells[1].Style.SelectionForeColor = passa ? CorOk : CorRuim;
                    if (at) { gc.Rows[k].DefaultCellStyle.Font = Ui.F(9f, FontStyle.Bold); temAtual = true; linC = k; }
                }
                Label tp = new Label { Text = "Parafusos da classe " + p.Classe + ", com os mesmos esforços", Height = Ui.Z(24), ForeColor = t.UiTexto };
                Label tc = new Label { Text = "Espessuras comerciais de chapa", Height = Ui.Z(24), ForeColor = t.UiTexto };
                Button usarP = BotaoDlg("Usar este parafuso", false), usarC = BotaoDlg("Usar esta espessura", false);
                listas.Controls.AddRange(new Control[] { tp, tc, gp, gc, usarP, usarC });
                EventHandler arrumar = delegate
                {
                    int w = (listas.ClientSize.Width - Ui.Z(28) - Ui.Z(14)) / 2, x1 = Ui.Z(14), x2 = x1 + w + Ui.Z(14);
                    int hg = listas.ClientSize.Height - Ui.Z(24) - Ui.Z(46);
                    tp.SetBounds(x1, 0, w, Ui.Z(24)); tc.SetBounds(x2, 0, w, Ui.Z(24));
                    gp.SetBounds(x1, Ui.Z(24), w, hg); gc.SetBounds(x2, Ui.Z(24), w, hg);
                    usarP.Location = new Point(x1, Ui.Z(24) + hg + Ui.Z(8)); usarC.Location = new Point(x2, Ui.Z(24) + hg + Ui.Z(8));
                };
                listas.Resize += arrumar;

                Label nota = new Label { AutoSize = false, Left = Ui.Z(14), Top = Ui.Z(8), Width = Ui.Z(820), Height = Ui.Z(38), ForeColor = t.UiTextoFraco, Font = Ui.F(8.5f),
                    Text = "Chapa calculada como balanço entre a fileira de parafusos e a face do perfil. Não verificado: pressão no concreto, ancoragem e solda." };
                Button btFim = BotaoDlg("Fechar", true);
                btFim.Location = new Point(d.ClientSize.Width - btFim.Width - Ui.Z(14), Ui.Z(11));
                btFim.Click += delegate { d.Close(); };
                rod.Controls.Add(nota); rod.Controls.Add(btFim);
                usarP.Click += delegate
                {
                    if (gp.CurrentRow == null) return;
                    p.Diametro = Parafusos.D[(int)gp.CurrentRow.Tag];
                    Registrar(); reabrir = true; d.Close();
                };
                usarC.Click += delegate
                {
                    if (gc.CurrentRow == null) return;
                    p.Esp = (double)gc.CurrentRow.Tag;
                    Registrar(); reabrir = true; d.Close();
                };
                dir.Controls.Add(listas); dir.Controls.Add(med);
                d.Controls.Add(dir); d.Controls.Add(planta); d.Controls.Add(rod); d.Controls.Add(cab);
                d.Shown += delegate
                {
                    arrumar(null, null);
                    if (linP >= 0) gp.CurrentCell = gp.Rows[linP].Cells[0];
                    if (linC >= 0) gc.CurrentCell = gc.Rows[linC].Cells[0]; else if (!temAtual) gc.ClearSelection();
                };
                d.ShowDialog(this);
            }
            if (reabrir) MostrarPlaca(no);
        }

        static string Seg(double n) { return double.IsInfinity(n) ? "sem solicitação" : Num.Fmt(n, 2) + (n < 1 ? "  — NÃO PASSA" : ""); }

        // Planta da placa: horizontal = plano do portico (eixo y local do pilar), vertical = largura.
        void PlantaPlaca(Graphics g, Size tam, ResPlaca r, PlacaBase p)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            if (r.H <= 0) return;
            float k = (float)Math.Min((tam.Width - 110) / r.H, (tam.Height - 150) / r.B);
            float cx = tam.Width / 2f, cy = tam.Height / 2f + 6;
            float lado = r.Espelhar ? -1 : 1;   // esquerda e direita iguais as do desenho do portico
            using (Brush bp = new SolidBrush(t.Escuro ? Color.FromArgb(60, 72, 90) : Color.FromArgb(226, 232, 240)))
            using (Pen pp = new Pen(t.Barra, 1.6f))
            using (Brush bperf = new SolidBrush(Color.FromArgb(150, t.Acento)))
            using (Pen pperf = new Pen(t.Acento, 1.4f))
            using (Brush bt = new SolidBrush(t.Texto))
            using (Brush bf = new SolidBrush(t.TextoFraco))
            {
                float w = (float)r.H * k, h = (float)r.B * k;
                g.FillRectangle(bp, cx - w / 2, cy - h / 2, w, h);
                g.DrawRectangle(pp, cx - w / 2, cy - h / 2, w, h);

                // perfil do pilar
                float ri = (float)r.RefIn * k, ro = (float)r.RefOut * k;
                if (r.Sec != null && r.Sec.Forma == "I")
                {
                    float tf = Math.Max(2f, (float)r.Sec.Tf * 1000 * k), tw = Math.Max(2f, (float)r.Sec.Tw * 1000 * k);
                    RectangleF[] rs = r.Deitado
                        ? new[] { new RectangleF(cx - ri / 2, cy - ro / 2, ri, tf), new RectangleF(cx - ri / 2, cy + ro / 2 - tf, ri, tf), new RectangleF(cx - tw / 2, cy - ro / 2, tw, ro) }
                        : new[] { new RectangleF(cx - ri / 2, cy - ro / 2, tf, ro), new RectangleF(cx + ri / 2 - tf, cy - ro / 2, tf, ro), new RectangleF(cx - ri / 2, cy - tw / 2, ri, tw) };
                    foreach (RectangleF q in rs) { g.FillRectangle(bperf, q); g.DrawRectangle(pperf, q.X, q.Y, q.Width, q.Height); }
                }
                else
                {
                    g.FillRectangle(bperf, cx - ri / 2, cy - ro / 2, ri, ro);
                    g.DrawRectangle(pperf, cx - ri / 2, cy - ro / 2, ri, ro);
                }

                // linha de giro
                if (!double.IsNaN(r.PivoY) && r.Tmax > 0)
                    using (Pen pg = new Pen(t.Guia, 1.4f) { DashStyle = DashStyle.Dash })
                    {
                        float x = cx + lado * (float)r.PivoY * k;
                        g.DrawLine(pg, x, cy - h / 2 - 14, x, cy + h / 2 + 14);
                        using (Brush bg = new SolidBrush(t.Guia)) g.DrawString("giro", fonte, bg, x - 12, cy + h / 2 + 16);
                    }

                // parafusos: quanto mais tracionado, mais vermelho
                float rf = Math.Max(7f, (float)p.Diametro * k / 2);
                foreach (double[] b in r.Paraf)
                {
                    float x = cx + lado * (float)b[0] * k, y = cy + (float)b[1] * k;
                    double fr = r.Tmax > 0 ? b[2] / r.Tmax : 0;
                    Color c = b[2] > 1e-6 ? Color.FromArgb(255, (int)(190 - 130 * fr), (int)(170 - 140 * fr)) : Color.FromArgb(150, 160, 172);
                    using (Brush bb = new SolidBrush(c)) g.FillEllipse(bb, x - rf, y - rf, 2 * rf, 2 * rf);
                    g.DrawEllipse(pp, x - rf, y - rf, 2 * rf, 2 * rf);
                    if (r.TemEsforcos)
                    {
                        string s = Num.Fmt(b[2], 1) + " kN";
                        SizeF sz = g.MeasureString(s, fonteN);
                        bool cima = b[1] <= 0;
                        g.DrawString(s, fonteN, bt, x - sz.Width / 2, cima ? y - rf - sz.Height - 1 : y + rf + 1);
                    }
                }

                // cotas
                string ch = Txt(r.H) + " mm  (no plano do pórtico)", cb = Txt(r.B) + " mm";
                SizeF s1 = g.MeasureString(ch, fonte);
                g.DrawString(ch, fonte, bf, cx - s1.Width / 2, cy + h / 2 + 34);
                g.TranslateTransform(cx - w / 2 - 34, cy);
                g.RotateTransform(-90);
                SizeF s2 = g.MeasureString(cb, fonte);
                g.DrawString(cb, fonte, bf, -s2.Width / 2, -s2.Height / 2);
                g.ResetTransform();
                g.DrawString("Tração em cada parafuso (kN)", fonteN, bt, 12, 10);
                g.DrawString("Vista de cima. Azul = perfil do pilar.", fonte, bf, 12, 12 + Ui.Z(18));
            }
        }

        void RotulasDoNo(No n, bool valor)
        {
            foreach (Barra b in m.Barras)
            {
                if (b.N1 == n.Id) b.RotulaInicio = valor;
                if (b.N2 == n.Id) b.RotulaFim = valor;
            }
        }

        // Remover: 0 apoio, 1 carga do no, 2 carga da barra, 3 rotulas da barra, 4 rotulas do no.
        void Remover(int k)
        {
            if (abaRes) Aba(false);
            acNo = null; acBarra = null;
            if (k == 0) acNo = delegate(No n) { n.ApoioX = n.ApoioY = n.ApoioGiro = false; n.MolaX = n.MolaY = n.MolaGiro = 0; n.RecalqueX = n.RecalqueY = 0; };
            else if (k == 1) acNo = delegate(No n) { n.Fx = n.Fy = n.Mz = 0; };
            else if (k == 2) acBarra = delegate(Barra b, double kk) { b.QGlobalX = b.QGlobalY = b.QLocalIni = b.QLocalFim = 0; };
            else if (k == 3) acBarra = delegate(Barra b, double kk) { b.RotulaInicio = b.RotulaFim = false; };
            else if (k == 5) acNo = delegate(No n) { n.Placa = null; };
            else acNo = delegate(No n) { RotulasDoNo(n, false); };
            string[] nomes = { "Remover apoio", "Remover carga do nó", "Remover carga da barra", "Remover rótulas da barra", "Remover rótulas do nó", "Remover placa de base" };
            Iniciar(nomes[k], true, true);
        }

        // Aplica nos que ja estao selecionados; se nao houver, fica esperando os cliques ate o botao direito.
        void Iniciar(string nome, bool usarSel, bool janela)
        {
            ferr = 0; temUlt = false; caixa = false;
            AtualizarEntrada();
            apNome = nome;
            apJanela = janela;
            apFiltro = acNo != null && acBarra != null ? 0 : acNo != null ? 1 : 2;
            if (usarSel)
            {
                int n = AplicarEm(sel, -1);
                if (n > 0)
                {
                    Registrar();
                    Aviso(apNome + ": aplicado em " + n + (apFiltro == 1 ? " nó(s)" : " barra(s)") + " que já estavam selecionados.");
                    sel.Clear();
                    Selecionou();
                    return;
                }
            }
            sel.Clear();
            Selecionou();
            ferr = 3;
            tela.Cursor = Cursors.Hand;
            Aviso("");
            tela.Invalidate();
        }

        int AplicarEm(List<object> objs, double k)
        {
            int n = 0;
            foreach (object o in objs.ToArray())
            {
                if (o is No && acNo != null) { acNo((No)o); n++; }
                else if (o is Barra && acBarra != null) { acBarra((Barra)o, k); n++; }
            }
            return n;
        }

        // Janela para digitar a medida de uma barra (comprimento, angulo) ou de um no (X, Y).
        bool Medir(object o)
        {
            No n = o as No; Barra b = o as Barra;
            ((Label)gA.Tag).Text = b != null ? "Comprimento (m)" : "X (m)";
            ((Label)gB.Tag).Text = b != null ? "Ângulo (graus)" : "Y (m)";
            gLbl.Text = b != null ? "Medidas da barra:" : "Posição do nó:";
            gA.Text = b != null ? Txt(Math.Round(m.Comp(b), 6)) : Txt(n.X);
            gB.Text = b != null ? Txt(Math.Round(m.Ang(b), 4)) : Txt(n.Y);
            sel.Clear(); sel.Add(o); tela.Invalidate();
            if (!Dialogo(0)) return false;
            double v1, v2;
            if (!Num.Ler(gA.Text, out v1) || !Num.Ler(gB.Text, out v2)) { Aviso("Medidas: faltou um valor."); return false; }
            if (n != null) { n.X = v1; n.Y = v2; }
            else
            {
                if (v1 <= 1e-6) { Aviso("O comprimento tem que ser maior que zero."); return false; }
                m.DefinirPolar(b, v1, v2);
            }
            return true;
        }

        void MaterialMudou()
        {
            bool outro = mLista.SelectedIndex == mLista.Items.Count - 1;
            mE.Enabled = outro;
            if (!outro)
            {
                Material mt = Biblioteca.Materiais[mLista.SelectedIndex];
                mE.Text = Txt(mt.E);
                mInfo.Text = "Poisson " + Txt(mt.Nu) + "    Densidade " + Txt(mt.Densidade) + " kg/m³\nG = " + Txt(Math.Round(mt.G)) + " MPa";
            }
            else mInfo.Text = "Digite o módulo de elasticidade.\nPoisson 0,3; sem peso próprio.";
        }

        // Bitolas da familia escolhida. Se "escolher" for o nome de um perfil, ja deixa ele selecionado.
        void EncherSeries(string escolher)
        {
            if (sTipo.SelectedIndex < 0 || sTipo.SelectedIndex >= nFam) return;
            string fam = Biblioteca.Familias[sTipo.SelectedIndex];
            List<Secao> todos = Biblioteca.DaFamilia(fam, null);
            Secao alvo = escolher == null ? null : Biblioteca.AcharSecao(escolher);
            carregando = true;
            sSerie.Items.Clear();
            sSerie.Items.Add("Todas as bitolas");
            foreach (Secao s in todos) if (!sSerie.Items.Contains(s.Serie)) sSerie.Items.Add(s.Serie);
            // familias grandes (tubos) abrem ja filtradas na primeira bitola
            int i = alvo != null && alvo.Familia == fam ? sSerie.Items.IndexOf(alvo.Serie) : (todos.Count > 100 ? 1 : 0);
            sSerie.SelectedIndex = Math.Max(0, i);
            carregando = false;
            EncherPerfis(escolher);
        }

        void EncherPerfis(string escolher)
        {
            if (sTipo.SelectedIndex < 0 || sTipo.SelectedIndex >= nFam) return;
            string serie = sSerie.SelectedIndex <= 0 ? null : (string)sSerie.SelectedItem;
            sLista.BeginUpdate();
            sLista.Items.Clear();
            foreach (Secao s in Biblioteca.DaFamilia(Biblioteca.Familias[sTipo.SelectedIndex], serie)) sLista.Items.Add(s.Nome);
            sLista.EndUpdate();
            int i = escolher == null ? -1 : sLista.Items.IndexOf(escolher);
            if (sLista.Items.Count > 0) sLista.SelectedIndex = i >= 0 ? i : 0;
        }

        string SecaoDaFicha()
        {
            int k = sTipo.SelectedIndex;
            if (k < nFam) return sLista.SelectedItem as string;
            if (k == nFam) return Val(sB) > 0 && Val(sH) > 0 ? "Ret " + Txt(Val(sB)) + "x" + Txt(Val(sH)) : null;
            if (k == nFam + 1) return Val(sD) > 0 ? "Circ " + Txt(Val(sD)) : null;
            return Val(sA) > 0 && Val(sI) > 0 ? "A=" + Txt(Val(sA)) + " I=" + Txt(Val(sI)) : null;
        }

        void SecaoMudou()
        {
            int k = sTipo.SelectedIndex;
            sGer.Visible = k < nFam; sRet.Visible = k == nFam; sCirc.Visible = k == nFam + 1; sGen.Visible = k == nFam + 2;
            Secao s = k < nFam ? Biblioteca.AcharSecao(sLista.SelectedItem as string) : null;
            if (s != null)
            {
                string l1, l2 = "";
                if (s.Forma == "L") l1 = "aba = " + Txt(s.H * 1000) + " mm     espessura = " + Txt(s.Tw * 1000) + " mm";
                else if (s.Forma == "TR") l1 = Txt(s.H * 1000) + " x " + Txt(s.Bf * 1000) + " mm     parede = " + Txt(s.Tw * 1000) + " mm";
                else if (s.Forma == "TC") l1 = "diâmetro = " + Txt(s.H * 1000) + " mm     parede = " + Txt(s.Tw * 1000) + " mm";
                else
                {
                    l1 = "d = " + Txt(s.H * 1000) + " mm     bf = " + Txt(s.Bf * 1000) + " mm";
                    l2 = "tw = " + Txt(s.Tw * 1000) + " mm     tf = " + Txt(s.Tf * 1000) + " mm";
                }
                sProps.Text = l1 + "\n" + l2 + (l2 == "" ? "" : "\n") +
                              "A = " + Txt(Math.Round(s.A * 1e4, 2)) + " cm²     massa = " + Txt(s.Massa) + " kg/m\n" +
                              "Ix = " + Txt(Math.Round(s.Ix * 1e8, 2)) + " cm4     Iy = " + Txt(Math.Round(s.Iy * 1e8, 2)) + " cm4";
            }
            sPrevia.Invalidate();
        }

        // Desenho da secao em escala.
        void DesenharSecao(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Secao s = Biblioteca.AcharSecao(SecaoDaFicha());
            if (s == null || s.H <= 0 || s.Forma == "GEN") return;
            float w = (float)(s.Bf > 0 ? s.Bf : s.H), h = (float)s.H;
            float k = Math.Min((sPrevia.Width - 20) / Math.Max(w, h), (sPrevia.Height - 12) / Math.Max(w, h));
            float cx = sPrevia.Width / 2f, cy = sPrevia.Height / 2f;
            float bw = w * k, d = h * k, x0 = cx - bw / 2, y0 = cy - d / 2;
            float tw = Math.Max(1.5f, (float)s.Tw * k), tf = Math.Max(1.5f, (float)s.Tf * k);
            using (Brush b = new SolidBrush(Color.FromArgb(90, t.Acento)))
            using (Brush fundo = new SolidBrush(t.UiFundo))
            using (Pen p = new Pen(t.Acento, 1.5f))
            {
                if (sDeitado.Checked) { g.TranslateTransform(cx, cy); g.RotateTransform(90); g.TranslateTransform(-cx, -cy); }
                PointF[] pts = null;
                switch (s.Forma)
                {
                    case "I":
                        pts = new[] { new PointF(x0, y0), new PointF(x0 + bw, y0), new PointF(x0 + bw, y0 + tf), new PointF(cx + tw / 2, y0 + tf),
                            new PointF(cx + tw / 2, y0 + d - tf), new PointF(x0 + bw, y0 + d - tf), new PointF(x0 + bw, y0 + d), new PointF(x0, y0 + d),
                            new PointF(x0, y0 + d - tf), new PointF(cx - tw / 2, y0 + d - tf), new PointF(cx - tw / 2, y0 + tf), new PointF(x0, y0 + tf) };
                        break;
                    case "U":
                        pts = new[] { new PointF(x0, y0), new PointF(x0 + bw, y0), new PointF(x0 + bw, y0 + tf), new PointF(x0 + tw, y0 + tf),
                            new PointF(x0 + tw, y0 + d - tf), new PointF(x0 + bw, y0 + d - tf), new PointF(x0 + bw, y0 + d), new PointF(x0, y0 + d) };
                        break;
                    case "L":
                        pts = new[] { new PointF(x0, y0), new PointF(x0 + tw, y0), new PointF(x0 + tw, y0 + d - tw), new PointF(x0 + bw, y0 + d - tw),
                            new PointF(x0 + bw, y0 + d), new PointF(x0, y0 + d) };
                        break;
                    case "TR":
                        g.FillRectangle(b, x0, y0, bw, d); g.FillRectangle(fundo, x0 + tw, y0 + tw, bw - 2 * tw, d - 2 * tw);
                        g.DrawRectangle(p, x0, y0, bw, d); g.DrawRectangle(p, x0 + tw, y0 + tw, bw - 2 * tw, d - 2 * tw);
                        break;
                    case "TC":
                        g.FillEllipse(b, x0, y0, d, d); g.FillEllipse(fundo, x0 + tw, y0 + tw, d - 2 * tw, d - 2 * tw);
                        g.DrawEllipse(p, x0, y0, d, d); g.DrawEllipse(p, x0 + tw, y0 + tw, d - 2 * tw, d - 2 * tw);
                        break;
                    case "CIRC":
                        g.FillEllipse(b, x0, y0, d, d); g.DrawEllipse(p, x0, y0, d, d);
                        break;
                    default:
                        g.FillRectangle(b, x0, y0, bw, d); g.DrawRectangle(p, x0, y0, bw, d);
                        break;
                }
                if (pts != null) { g.FillPolygon(b, pts); g.DrawPolygon(p, pts); }
                g.ResetTransform();
            }
        }

        // ---------- abas ----------

        Button NovaAba(string texto, int indice)
        {
            Button b = new Button { Text = texto, FlatStyle = FlatStyle.Flat, Font = Ui.F(9.5f), TabStop = false };
            b.SetBounds(0, Ui.Z(2), TextRenderer.MeasureText(texto, b.Font).Width + Ui.Z(26), Ui.Z(26));
            b.FlatAppearance.BorderSize = 0;
            b.Click += delegate { Aba(indice); };
            return b;
        }

        void Aba(bool resultados) { Aba(resultados ? 1 : 0); }

        // 0 Modelo = ferramentas de trabalho; 1 Resultados = so visualizacao; 2 Opcoes.
        void Aba(int k)
        {
            abaAtual = k;
            abaRes = k == 1;
            Ferramenta(0);
            fita.Visible = k == 0;
            fitaRes.Visible = k == 1;
            fitaOp.Visible = k == 2;
            lado.Visible = k == 1;
            Vista(k == 1 ? vistaRes : 0);
            EstiloAbas();
            Consulta();
            if (k == 1 && !res.Ok) Aviso("Ainda não há resultados: " + res.Erro);
        }

        void EstiloAbas()
        {
            abas.BackColor = t.UiBorda;
            for (int i = 0; i < tabs.Length; i++)
            {
                bool ativa = i == abaAtual;
                tabs[i].BackColor = ativa ? t.Ui : t.UiBorda;
                tabs[i].ForeColor = ativa ? Color.FromArgb(t.Escuro ? 255 : 0, t.Escuro ? 255 : 0, t.Escuro ? 255 : 0) : t.UiTextoFraco;
                tabs[i].FlatAppearance.MouseOverBackColor = ativa ? t.Ui : t.UiTitulo;
                tabs[i].FlatAppearance.MouseDownBackColor = t.Ui;
            }
        }

        // Quadro de consulta embaixo da ficha.
        void Consulta()
        {
            StringBuilder s = new StringBuilder();
            if (sel.Count == 0) s.Append("Nada selecionado.\n\n" + m.Nos.Count + " nós, " + m.Barras.Count + " barras.");
            else if (sel.Count > 1) s.Append(sel.Count + (sel[0] is No ? " nós" : " barras") + " selecionados.");
            else if (sel[0] is No)
            {
                No n = (No)sel[0];
                s.AppendLine("NÓ   x = " + Num.Fmt(n.X, 3) + " m    y = " + Num.Fmt(n.Y, 3) + " m");
                if (n.Res != null)
                {
                    s.AppendLine("Deslocamento X:  " + Num.Fmt(n.Res[0] * 1000, 3) + " mm");
                    s.AppendLine("Deslocamento Y:  " + Num.Fmt(n.Res[1] * 1000, 3) + " mm");
                    s.AppendLine("Giro:  " + n.Res[2].ToString("0.000000") + " rad");
                    if (n.TemApoio)
                        s.Append("Reações:  Rx " + Num.Fmt(n.Res[3], 2) + " kN   Ry " + Num.Fmt(n.Res[4], 2) + " kN\n                 M " + Num.Fmt(n.Res[5], 2) + " kN.m");
                }
                if (n.Placa != null)
                {
                    ResPlaca rp = PlacaCalc.Calcular(m, n);
                    s.Append("\n\nPLACA DE BASE\n");
                    if (rp.TemEsforcos) s.Append("Maior tração: " + Num.Fmt(rp.Tmax, 2) + " kN por parafuso\nCorte: " + Num.Fmt(rp.Fv, 2) + " kN por parafuso\nDuplo clique no nó para ver tudo.");
                    else s.Append(rp.Erro);
                }
            }
            else
            {
                Barra b = (Barra)sel[0];
                s.AppendLine("BARRA   " + b.Secao + (b.EixoFraco ? " (deitado)" : ""));
                s.AppendLine(b.Material);
                s.AppendLine("L = " + Num.Fmt(m.Comp(b), 3) + " m    ângulo " + Num.Fmt(m.Ang(b), 1) + "°");
                if (b.Res != null)
                {
                    s.AppendLine("Normal:  " + b.ResN + " kN");
                    s.AppendLine("Cortante:  " + b.ResQ + " kN");
                    s.AppendLine("Momento:  " + b.ResM + " kN.m");
                    s.Append("Flecha:  " + b.ResFlecha + " mm");
                }
                Verif vq;
                if (verifs.TryGetValue(b, out vq))
                    s.Append("\n\nVERIFICAÇÃO\n" + (vq.Aplicavel ? (vq.Passa ? "Passa" : "NÃO PASSA") + ": aproveitamento " + Math.Round(vq.Ap * 100) + "%\nLimitado por: " + vq.Critico + "\nDuplo clique na barra compara perfis." : vq.Motivo));
            }
            info.Text = s.ToString();
        }

        // ---------- tema ----------

        void AplicarTema()
        {
            rnd = new ToolStripProfessionalRenderer(new Cores(t));
            rnd.RoundedEdges = false;
            List<ToolStrip> todas = new List<ToolStrip>(faixas);
            todas.Add(st);
            foreach (ToolStrip ts in todas)
            {
                ts.Renderer = rnd;
                ts.BackColor = ts == st || ts == qat ? t.UiBorda : t.Ui;
                ts.ForeColor = t.UiTexto;
                Pintar(ts.Items);
            }
            tbGrade.BackColor = t.Campo; tbGrade.ForeColor = t.UiTexto;
            foreach (KeyValuePair<ToolStripItem, int> k in icones) k.Key.Image = Icone(k.Value);
            foreach (Label l in titulos) { l.BackColor = t.Ui; l.ForeColor = t.UiTexto; }
            foreach (Panel p in divisas) p.BackColor = t.UiBorda;
            fita.BackColor = t.Ui; fitaRes.BackColor = t.Ui; fitaOp.BackColor = t.Ui;
            EstiloAbas();
            BackColor = t.UiBorda;
            barraTitulo.BackColor = t.UiBorda;
            foreach (Button b in new[] { btMin, btMax, btFechar })
            {
                b.BackColor = t.UiBorda; b.ForeColor = t.UiTexto;
                b.FlatAppearance.MouseOverBackColor = b == btFechar ? Color.FromArgb(232, 17, 35) : t.UiClaro;
                b.FlatAppearance.MouseDownBackColor = b == btFechar ? Color.FromArgb(180, 20, 30) : t.Ui;
            }
            docs.BackColor = t.UiBorda;
            lbDoc.BackColor = t.Ui; lbDoc.ForeColor = t.Escuro ? Color.White : Color.Black;
            btMais.BackColor = t.UiBorda; btMais.ForeColor = t.UiTexto;
            btMais.FlatAppearance.MouseOverBackColor = t.UiClaro;
            tela.BackColor = t.Fundo;
            lado.BackColor = t.UiFundo;
            tituloFicha.BackColor = t.UiTitulo; tituloFicha.ForeColor = t.UiTexto;
            info.BackColor = t.UiFundo; info.ForeColor = t.UiTexto;
            entrada.BackColor = t.Ui;
            foreach (Control c in entrada.Controls)
            {
                if (c is TextBox || c is ComboBox) { c.BackColor = t.Campo; c.ForeColor = t.UiTexto; }
                else if (c is Button) { c.BackColor = t.Acento; c.ForeColor = Color.White; ((Button)c).FlatAppearance.BorderSize = 0; }
                else c.ForeColor = c == lbDica ? t.UiTextoFraco : t.UiTexto;
            }
            if (IsHandleCreated) Moldura(Handle);
            Chaves();
            StatusCalc();
            Invalidate(true);
        }

        void Pintar(ToolStripItemCollection itens)
        {
            foreach (ToolStripItem it in itens)
            {
                it.ForeColor = t.UiTexto;
                ToolStripDropDownItem mi = it as ToolStripDropDownItem;
                if (mi != null) { mi.DropDown.BackColor = t.UiFundo; Pintar(mi.DropDownItems); }
            }
        }

        // Icones no padrao das fitas do CAD: silhuetas solidas em cinza claro, com um detalhe em azul.
        // Sao desenhados aqui (grade de 32 x 32), na cor do tema.
        Bitmap Icone(int tipo)
        {
            Color cg = t.Escuro ? Color.FromArgb(206, 211, 219) : Color.FromArgb(82, 90, 103);     // corpo
            Color cd = t.Escuro ? Color.FromArgb(137, 146, 160) : Color.FromArgb(150, 158, 170);   // face secundaria
            Color ca = t.Escuro ? Color.FromArgb(86, 176, 236) : Color.FromArgb(0, 118, 196);      // detalhe
            Color cr = Color.FromArgb(236, 104, 98);
            Bitmap bmp = new Bitmap(Ui.Z(32), Ui.Z(32));   // ja na resolucao da tela, para nao borrar
            using (Graphics g = Graphics.FromImage(bmp))
            using (Brush G = new SolidBrush(cg))
            using (Brush D = new SolidBrush(cd))
            using (Brush A = new SolidBrush(ca))
            using (Brush R = new SolidBrush(cr))
            using (Brush F = new SolidBrush(t.Ui))
            using (Pen pG = new Pen(cg, 3.2f))
            using (Pen pD = new Pen(cd, 2.2f))
            using (Pen pA = new Pen(ca, 3f))
            using (Pen pR = new Pen(cr, 3.4f))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.ScaleTransform(Ui.S, Ui.S);
                switch (tipo)
                {
                    case 19:   // novo
                        g.FillPolygon(G, new[] { new PointF(8, 3), new PointF(19, 3), new PointF(25, 9), new PointF(25, 29), new PointF(8, 29) });
                        g.FillPolygon(D, new[] { new PointF(19, 3), new PointF(19, 9), new PointF(25, 9) });
                        break;
                    case 20:   // abrir
                        g.FillPolygon(D, new[] { new PointF(3, 8), new PointF(12, 8), new PointF(14, 11), new PointF(26, 11), new PointF(26, 26), new PointF(3, 26) });
                        g.FillPolygon(G, new[] { new PointF(3, 26), new PointF(8, 14), new PointF(30, 14), new PointF(25, 26) });
                        break;
                    case 21:   // salvar
                        g.FillPolygon(G, new[] { new PointF(4, 4), new PointF(23, 4), new PointF(28, 9), new PointF(28, 28), new PointF(4, 28) });
                        g.FillRectangle(F, 9, 4, 11, 8); g.FillRectangle(G, 16, 5.5f, 3, 5);
                        g.FillRectangle(F, 8, 17, 16, 11);
                        break;
                    case 22:   // desfazer
                        g.DrawArc(pG, 8, 10, 17, 15, 195, 245);
                        g.FillPolygon(G, new[] { new PointF(3, 12), new PointF(13, 7), new PointF(12, 18) });
                        break;
                    case 39:   // exportar planilha
                        g.FillRectangle(G, 4, 6, 24, 21);
                        g.FillRectangle(F, 6, 12, 9.5f, 5.5f); g.FillRectangle(F, 16.5f, 12, 9.5f, 5.5f);
                        g.FillRectangle(F, 6, 19, 9.5f, 6); g.FillRectangle(A, 16.5f, 19, 9.5f, 6);
                        break;
                    case 1:    // barra
                        g.DrawLine(pG, 7, 25, 25, 7);
                        g.FillRectangle(A, 3, 21, 8, 8); g.FillRectangle(A, 21, 3, 8, 8);
                        break;
                    case 17:   // no
                        g.FillRectangle(A, 6, 13, 12, 12);
                        g.DrawLine(pG, 23, 4, 23, 14); g.DrawLine(pG, 18, 9, 28, 9);
                        break;
                    case 23:   // medidas
                        g.DrawLine(pG, 4, 24, 28, 24);
                        g.DrawLine(pD, 5, 6, 5, 20); g.DrawLine(pD, 27, 6, 27, 20);
                        g.DrawLine(pD, 9, 12, 23, 12);
                        g.FillPolygon(A, new[] { new PointF(6, 12), new PointF(12, 8), new PointF(12, 16) });
                        g.FillPolygon(A, new[] { new PointF(26, 12), new PointF(20, 8), new PointF(20, 16) });
                        break;
                    case 24:   // material
                        g.FillPolygon(A, new[] { new PointF(9, 5), new PointF(27, 5), new PointF(23, 11), new PointF(5, 11) });
                        g.FillPolygon(G, new[] { new PointF(9, 13), new PointF(27, 13), new PointF(23, 19), new PointF(5, 19) });
                        g.FillPolygon(D, new[] { new PointF(9, 21), new PointF(27, 21), new PointF(23, 27), new PointF(5, 27) });
                        break;
                    case 25:   // secao (perfil I)
                        g.FillRectangle(G, 6, 4, 20, 5); g.FillRectangle(G, 6, 23, 20, 5); g.FillRectangle(A, 13.5f, 9, 5, 14);
                        break;
                    case 3:    // apoio
                        g.FillPolygon(G, new[] { new PointF(16, 8), new PointF(6, 23), new PointF(26, 23) });
                        g.FillRectangle(D, 4, 25, 24, 3);
                        g.FillRectangle(A, 12, 3, 8, 8);
                        break;
                    case 6:    // rotula
                        g.DrawLine(pG, 4, 25, 28, 7);
                        g.FillEllipse(F, 9.5f, 9.5f, 13, 13);
                        g.DrawEllipse(pA, 10.5f, 10.5f, 11, 11);
                        break;
                    case 32:   // placa de base
                        g.FillRectangle(G, 4, 7, 24, 18);
                        g.FillRectangle(D, 12, 11, 2.5f, 10); g.FillRectangle(D, 17.5f, 11, 2.5f, 10); g.FillRectangle(D, 12, 15, 8, 2);
                        g.FillEllipse(A, 5.5f, 8.5f, 5, 5); g.FillEllipse(A, 21.5f, 8.5f, 5, 5); g.FillEllipse(A, 5.5f, 18.5f, 5, 5); g.FillEllipse(A, 21.5f, 18.5f, 5, 5);
                        break;
                    case 26:   // carga no no
                        g.DrawLine(pA, 16, 3, 16, 14);
                        g.FillPolygon(A, new[] { new PointF(16, 20), new PointF(10, 12), new PointF(22, 12) });
                        g.FillRectangle(G, 11, 21, 10, 8);
                        break;
                    case 27:   // carga na barra
                        g.FillRectangle(G, 3, 24, 26, 4);
                        g.FillRectangle(A, 4, 5, 24, 2.5f);
                        for (int i = 0; i < 4; i++)
                        {
                            float x = 6 + i * 6.7f;
                            g.FillRectangle(A, x - 1, 6, 2.2f, 10);
                            g.FillPolygon(A, new[] { new PointF(x, 22), new PointF(x - 3.6f, 15), new PointF(x + 3.8f, 15) });
                        }
                        break;
                    case 40:   // verificar barras
                        g.DrawLine(pG, 5, 26, 27, 26);
                        using (Pen pv = new Pen(CorOk, 4.2f)) g.DrawLines(pv, new[] { new PointF(7, 13), new PointF(13, 19), new PointF(25, 5) });
                        break;
                    case 41:   // comparar perfis
                        g.FillRectangle(G, 4, 5, 15, 3.4f); g.FillRectangle(G, 4, 14, 15, 3.4f); g.FillRectangle(G, 4, 23, 15, 3.4f);
                        using (Brush bo = new SolidBrush(CorOk)) { g.FillEllipse(bo, 22, 3, 7, 7); g.FillEllipse(bo, 22, 12, 7, 7); }
                        g.FillEllipse(R, 22, 21, 7, 7);
                        break;
                    case 31:   // remover
                        g.FillPolygon(D, new[] { new PointF(12, 5), new PointF(3, 20), new PointF(21, 20) });
                        g.FillEllipse(R, 13, 13, 16, 16);
                        g.FillRectangle(Brushes.White, 16.5f, 19.5f, 9, 3);
                        break;
                    case 18:   // apagar
                        g.DrawLine(pR, 7, 7, 25, 25); g.DrawLine(pR, 25, 7, 7, 25);
                        break;
                    case 15:   // enquadrar
                        g.FillRectangle(G, 3, 3, 9, 3); g.FillRectangle(G, 3, 3, 3, 9);
                        g.FillRectangle(G, 20, 3, 9, 3); g.FillRectangle(G, 26, 3, 3, 9);
                        g.FillRectangle(G, 3, 26, 9, 3); g.FillRectangle(G, 3, 20, 3, 9);
                        g.FillRectangle(G, 20, 26, 9, 3); g.FillRectangle(G, 26, 20, 3, 9);
                        g.FillRectangle(A, 11, 11, 10, 10);
                        break;
                    case 16:   // tema
                        g.FillPie(G, 5, 5, 22, 22, 90, 180); g.FillPie(D, 5, 5, 22, 22, 270, 180);
                        break;
                    case 9:    // normal
                        g.FillRectangle(A, 5, 8, 22, 11); g.FillRectangle(G, 3, 21, 26, 4);
                        break;
                    case 10:   // cortante
                        g.FillRectangle(A, 5, 5, 11, 9); g.FillRectangle(A, 16, 18, 11, 9); g.FillRectangle(G, 3, 14, 26, 4);
                        break;
                    case 11:   // momento
                    case 28:
                    case 29:
                        using (GraphicsPath gp = new GraphicsPath())
                        {
                            float fundo = tipo == 28 ? 18 : 31;
                            gp.AddBezier(5, 9, 10, fundo, 22, fundo, 27, 9); gp.CloseFigure();
                            g.FillPath(A, gp);
                        }
                        g.FillRectangle(G, 3, 6, 26, 4);
                        if (tipo == 28) g.FillRectangle(G, 10, 23, 12, 3.2f);
                        if (tipo == 29) { g.FillEllipse(F, 16, 16, 15, 15); g.FillRectangle(G, 18, 22, 11, 3.2f); g.FillRectangle(G, 22, 18, 3.2f, 11); }
                        break;
                    case 12:   // deformada
                        using (Pen pt = new Pen(cd, 2f) { DashStyle = DashStyle.Dash }) g.DrawLine(pt, 3, 9, 29, 9);
                        g.DrawBezier(pA, 3, 9, 10, 27, 22, 27, 29, 9);
                        g.FillRectangle(G, 1, 6, 5, 6); g.FillRectangle(G, 26, 6, 5, 6);
                        break;
                    case 33:   // peso proprio
                        g.FillPolygon(G, new[] { new PointF(10, 11), new PointF(22, 11), new PointF(27, 28), new PointF(5, 28) });
                        g.DrawEllipse(pD, 12.5f, 3.5f, 7, 7);
                        g.FillPolygon(A, new[] { new PointF(16, 25), new PointF(11, 18), new PointF(21, 18) }); g.FillRectangle(A, 14.6f, 13, 2.8f, 6);
                        break;
                    case 34:   // deformacao por cortante
                        g.FillPolygon(G, new[] { new PointF(5, 9), new PointF(21, 5), new PointF(27, 23), new PointF(11, 27) });
                        g.FillPolygon(A, new[] { new PointF(3, 20), new PointF(8, 17), new PointF(8, 23) });
                        g.FillPolygon(A, new[] { new PointF(29, 12), new PointF(24, 9), new PointF(24, 15) });
                        break;
                    case 35:   // nomes
                        g.FillPolygon(G, new[] { new PointF(4, 6), new PointF(17, 6), new PointF(29, 18), new PointF(18, 28), new PointF(4, 15) });
                        g.FillEllipse(F, 7.5f, 9.5f, 5.5f, 5.5f);
                        break;
                    case 36:   // grade
                        g.FillRectangle(A, 12.5f, 12.5f, 7, 7);
                        foreach (float v in new[] { 4f, 11f, 19f, 26f }) { g.FillRectangle(G, v, 4, 2.2f, 24); g.FillRectangle(G, 4, v, 24, 2.2f); }
                        break;
                    case 37:   // atrair
                        g.DrawArc(new Pen(cg, 6.5f), 7, 3, 18, 18, 180, 180);
                        g.FillRectangle(G, 3.8f, 12, 6.5f, 9); g.FillRectangle(G, 21.8f, 12, 6.5f, 9);
                        g.FillRectangle(A, 3.8f, 22, 6.5f, 6); g.FillRectangle(A, 21.8f, 22, 6.5f, 6);
                        break;
                    case 38:   // ajuda
                        g.FillEllipse(G, 4, 4, 24, 24);
                        using (Font fq = Ui.F(14f, FontStyle.Bold)) g.DrawString("?", fq, F, 9.5f, 3.5f);
                        break;
                }
            }
            return bmp;
        }

        // ---------- estado ----------

        void Novo()
        {
            if (!PodeDescartar()) return;
            m = new Modelo();
            arquivo = null;
            Zerar();
            Ferramenta(1);
        }

        void Zerar()
        {
            desfazer.Clear();
            atual = m.ParaXml();
            sujo = false;
            sel.Clear();
            temUlt = false;
            Recalcular();
            Selecionou();
            Chaves();
            Enquadrar();
            Titulo();
        }

        // Chamar depois de qualquer alteracao no modelo.
        void Registrar()
        {
            desfazer.Push(atual);
            atual = m.ParaXml();
            sujo = true;
            Recalcular();
            Chaves();
            Titulo();
        }

        void Desfazer()
        {
            if (desfazer.Count == 0) { Aviso("Nada para desfazer."); return; }
            atual = desfazer.Pop();
            m = Modelo.DeXml(atual);
            sel.Clear();
            sobre = null;
            temUlt = false;
            sujo = true;
            Recalcular();
            Selecionou();
            Chaves();
            AtualizarEntrada();
            Titulo();
            Aviso("Desfeito.");
        }

        void Recalcular()
        {
            res = Solver.Resolver(m);
            verifs.Clear();
            if (res.Ok) foreach (Barra b in m.Barras) verifs[b] = Verificador.Barra(m, b, Biblioteca.AcharSecao(b.Secao));
            StatusCalc();
            if (info != null) Consulta();
            if (tela != null) tela.Invalidate();
        }

        void StatusCalc()
        {
            if (status == null) return;
            if (res.Ok)
            {
                status.ForeColor = t.Apoio;
                status.Text = "Calculado. Maior deslocamento de nó: " + Num.Fmt(res.MaxDesl * 1000, 2) + " mm" + (m.PesoProprio ? " (com peso próprio)" : "") + "  ";
            }
            else
            {
                status.ForeColor = m.Barras.Count == 0 ? t.UiTextoFraco : t.Carga;
                status.Text = res.Erro + (Biblioteca.AvisoPerfis != "" ? "  " + Biblioteca.AvisoPerfis : "") + "  ";
            }
        }

        void Aviso(string s) { msg.Text = s; }

        void Selecionou()
        {
            Consulta();
            tela.Invalidate();
        }

        void SelecionarTudo()
        {
            sel.Clear();
            foreach (Barra b in m.Barras) sel.Add(b);
            Selecionou();
        }

        void Ferramenta(int f)
        {
            ferr = f;
            temUlt = false;
            caixa = false;
            tela.Cursor = f == 0 ? Cursors.Default : Cursors.Cross;
            AtualizarEntrada();
            Aviso("");
            tela.Invalidate();
        }

        void Vista(int v)
        {
            vista = v;
            if (v > 0) vistaRes = v;
            for (int i = 1; i < 6; i++) if (btVista[i] != null) btVista[i].Checked = i == v;
            tela.Invalidate();
        }

        void Cancelar()
        {
            tbA.Clear(); tbB.Clear();
            if (ferr == 1 && temUlt) { temUlt = false; AtualizarEntrada(); }
            else if (ferr != 0) Ferramenta(0);
            else if (caixa) caixa = false;
            else if (sel.Count > 0) { sel.Clear(); Selecionou(); }
            tela.Focus();
            tela.Invalidate();
        }

        void Excluir()
        {
            if (sel.Count == 0) { Aviso("Nada selecionado para apagar."); return; }
            int n = sel.Count;
            foreach (object o in sel)
            {
                if (o is Barra) m.RemoverBarra((Barra)o);
                else if (o is No && m.Nos.Contains((No)o)) m.RemoverNo((No)o);
            }
            sel.Clear();
            sobre = null;
            Registrar();
            Selecionou();
            Aviso(n + " objeto(s) apagado(s).");
        }

        // ---------- arquivo ----------

        bool PodeDescartar()
        {
            if (!sujo) return true;
            DialogResult d = MessageBox.Show(this, "Salvar as alterações antes?", NOME, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (d == DialogResult.Cancel) return false;
            if (d == DialogResult.Yes) return Salvar(false);
            return true;
        }

        void AbrirDialogo()
        {
            if (!PodeDescartar()) return;
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Filter = "Modelo de pórtico (*.portico)|*.portico";
                if (d.ShowDialog(this) == DialogResult.OK) Abrir(d.FileName);
            }
        }

        void Abrir(string caminho)
        {
            try
            {
                m = Modelo.DeXml(File.ReadAllText(caminho, Encoding.UTF8));
                arquivo = caminho;
                Zerar();
                Ferramenta(0);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Não consegui abrir o arquivo.\n\n" + ex.Message, NOME, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        bool Salvar(bool como)
        {
            if (arquivo == null || como)
            {
                using (SaveFileDialog d = new SaveFileDialog())
                {
                    d.Filter = "Modelo de pórtico (*.portico)|*.portico";
                    if (d.ShowDialog(this) != DialogResult.OK) return false;
                    arquivo = d.FileName;
                }
            }
            try
            {
                File.WriteAllText(arquivo, m.ParaXml(), Encoding.UTF8);
                sujo = false;
                Titulo();
                Aviso("Salvo.");
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Não consegui salvar.\n\n" + ex.Message, NOME, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }

        void Exportar()
        {
            if (!res.Ok) { Aviso("Não há resultados para exportar: " + res.Erro); return; }
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Filter = "Planilha CSV (*.csv)|*.csv";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("NOS");
                sb.AppendLine("No;X (m);Y (m);Desl X (mm);Desl Y (mm);Giro (rad);Reacao X (kN);Reacao Y (kN);Reacao M (kN.m)");
                for (int i = 0; i < m.Nos.Count; i++)
                {
                    No n = m.Nos[i];
                    sb.AppendLine((i + 1) + ";" + Num.Fmt(n.X, 4) + ";" + Num.Fmt(n.Y, 4) + ";" + Num.Fmt(n.Res[0] * 1000, 4) + ";" + Num.Fmt(n.Res[1] * 1000, 4) + ";" +
                                  Num.Fmt(n.Res[2], 7) + ";" + Num.Fmt(n.Res[3], 3) + ";" + Num.Fmt(n.Res[4], 3) + ";" + Num.Fmt(n.Res[5], 3));
                }
                sb.AppendLine();
                sb.AppendLine("BARRAS");
                sb.AppendLine("Barra;No inicial;No final;Perfil;Material;L (m);N min (kN);N max (kN);Q min (kN);Q max (kN);M min (kN.m);M max (kN.m);Flecha (mm)");
                for (int i = 0; i < m.Barras.Count; i++)
                {
                    Barra b = m.Barras[i];
                    double[] e = new double[6];
                    for (int k = 0; k < 3; k++) b.Res.Extremos(k, out e[2 * k], out e[2 * k + 1]);
                    sb.Append((i + 1) + ";" + (m.Nos.IndexOf(m.NoPorId(b.N1)) + 1) + ";" + (m.Nos.IndexOf(m.NoPorId(b.N2)) + 1) + ";" + b.Secao + ";" + b.Material + ";" + Num.Fmt(b.Res.L, 4));
                    foreach (double v in e) sb.Append(";" + Num.Fmt(v, 3));
                    sb.AppendLine(";" + Num.Fmt(b.Res.FlechaMax() * 1000, 3));
                }
                try { File.WriteAllText(d.FileName, sb.ToString(), new UTF8Encoding(true)); Aviso("Resultados exportados."); }
                catch (Exception ex) { MessageBox.Show(this, "Não consegui salvar.\n\n" + ex.Message, NOME, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
        }

        // ---------- coordenadas e atracao ----------

        PointF P(double x, double y) { return new PointF((float)(ox + x * esc), (float)(oy - y * esc)); }
        PointF P(No n) { return P(n.X, n.Y); }

        No NoEm(Point p)
        {
            No melhor = null; double dm = 10;
            foreach (No n in m.Nos)
            {
                PointF q = P(n);
                double d = Math.Sqrt((q.X - p.X) * (q.X - p.X) + (q.Y - p.Y) * (q.Y - p.Y));
                if (d < dm) { dm = d; melhor = n; }
            }
            return melhor;
        }

        Barra BarraEm(Point p, out double tt)
        {
            Barra melhor = null; double dm = 7; tt = 0;
            foreach (Barra b in m.Barras)
            {
                PointF a = P(m.NoPorId(b.N1)), c = P(m.NoPorId(b.N2));
                double dx = c.X - a.X, dy = c.Y - a.Y, l2 = dx * dx + dy * dy;
                if (l2 < 1e-9) continue;
                double k = Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / l2));
                double qx = a.X + k * dx - p.X, qy = a.Y + k * dy - p.Y, d = Math.Sqrt(qx * qx + qy * qy);
                if (d < dm) { dm = d; melhor = b; tt = k; }
            }
            return melhor;
        }

        // O que o clique pega. f: 0 nos e barras, 1 so nos, 2 so barras. k = posicao na barra (0..1).
        object Pegar(Point p, int f, out double k)
        {
            k = 0;
            object a = f != 2 ? NoEm(p) : null;
            if (a == null && f != 1) a = BarraEm(p, out k);
            return a;
        }

        // Botao direito sobre um no ou barra: tudo que da para fazer com ele, inclusive remover.
        void MenuContexto(Point p)
        {
            double k;
            object alvo = Pegar(p, 0, out k);
            if (alvo == null) { if (sel.Count > 0) { sel.Clear(); Selecionou(); } return; }
            if (!sel.Contains(alvo)) { sel.Clear(); sel.Add(alvo); Selecionou(); }
            ContextMenuStrip c = new ContextMenuStrip();
            c.Renderer = rnd;
            c.BackColor = t.UiFundo;
            if (alvo is No)
            {
                c.Items.Add("Posição (X, Y)...", null, delegate { Atributo(0); });
                c.Items.Add("Apoio...", null, delegate { Atributo(3); });
                c.Items.Add("Carga no nó...", null, delegate { Atributo(5); });
                c.Items.Add("Placa de base...", null, delegate { Atributo(7); });
                c.Items.Add(new ToolStripSeparator());
                c.Items.Add("Remover apoio", null, delegate { Remover(0); });
                c.Items.Add("Remover carga", null, delegate { Remover(1); });
                c.Items.Add("Remover rótulas do nó", null, delegate { Remover(4); });
                c.Items.Add("Remover placa de base", null, delegate { Remover(5); });
                c.Items.Add(new ToolStripSeparator());
                c.Items.Add("Apagar nó", null, delegate { Excluir(); });
            }
            else
            {
                c.Items.Add("Comprimento e ângulo...", null, delegate { Atributo(0); });
                c.Items.Add("Seção...", null, delegate { Atributo(2); });
                c.Items.Add("Material...", null, delegate { Atributo(1); });
                c.Items.Add("Carga na barra...", null, delegate { Atributo(6); });
                c.Items.Add("Rótula...", null, delegate { Atributo(4); });
                c.Items.Add(new ToolStripSeparator());
                c.Items.Add("Remover carga", null, delegate { Remover(2); });
                c.Items.Add("Remover rótulas", null, delegate { Remover(3); });
                c.Items.Add(new ToolStripSeparator());
                c.Items.Add("Apagar barra", null, delegate { Excluir(); });
            }
            Pintar(c.Items);
            c.Show(tela, p);
        }

        // Ponto do mundo para o cursor: no > meio da barra > sobre a barra > horizontal/vertical > grade > livre.
        void Atrair(Point p, out double x, out double y)
        {
            marca = 0; guia = false;
            x = (p.X - ox) / esc; y = (oy - p.Y) / esc;
            No n = NoEm(p);
            if (n != null) { x = n.X; y = n.Y; marca = 1; return; }
            foreach (Barra b in m.Barras)
            {
                No a = m.NoPorId(b.N1), c = m.NoPorId(b.N2);
                PointF q = P((a.X + c.X) / 2, (a.Y + c.Y) / 2);
                if (Math.Abs(q.X - p.X) < 9 && Math.Abs(q.Y - p.Y) < 9) { x = (a.X + c.X) / 2; y = (a.Y + c.Y) / 2; marca = 2; return; }
            }
            double k;
            Barra bb = BarraEm(p, out k);
            if (bb != null)
            {
                No a = m.NoPorId(bb.N1), c = m.NoPorId(bb.N2);
                x = Math.Round(a.X + k * (c.X - a.X), 6); y = Math.Round(a.Y + k * (c.Y - a.Y), 6); marca = 3;
                return;
            }
            if (m.AtrairGrade && m.Grade > 0)
            {
                x = Math.Round(x / m.Grade) * m.Grade;
                y = Math.Round(y / m.Grade) * m.Grade;
            }
            if (temUlt && ferr == 1)
            {
                // Shift trava em 0/90; sem Shift, gruda em 0/90 quando esta quase la.
                double dx = x - ux, dy = y - uy, ang = Math.Atan2(dy, dx) * 180 / Math.PI, alvo = Math.Round(ang / 90) * 90;
                bool shift = (ModifierKeys & Keys.Shift) != 0;
                if (shift || Math.Abs(ang - alvo) < 3.5)
                {
                    double r = alvo * Math.PI / 180, l = dx * Math.Cos(r) + dy * Math.Sin(r);
                    x = ux + l * Math.Cos(r); y = uy + l * Math.Sin(r);
                    guia = true;
                }
            }
            x = Math.Round(x, 6); y = Math.Round(y, 6);
        }

        // Aplica o que estiver digitado na faixa de medida. completo = da para inserir so com o teclado.
        void ComDigitado(ref double x, ref double y, out bool completo)
        {
            completo = false;
            double a, b;
            bool ta = Num.Ler(tbA.Text, out a), tb = Num.Ler(tbB.Text, out b);
            int modo = (temUlt && ferr == 1) ? modoPref : 2;
            if (modo == 0)
            {
                if (!ta || a <= 0) return;
                double dx = x - ux, dy = y - uy, l = Math.Sqrt(dx * dx + dy * dy);
                if (tb) { dx = Math.Cos(b * Math.PI / 180); dy = Math.Sin(b * Math.PI / 180); l = 1; }
                else if (l < 1e-9) { dx = 1; dy = 0; l = 1; }
                x = ux + a * dx / l; y = uy + a * dy / l;
                marca = 0;
                completo = true;
            }
            else if (ta && tb)
            {
                x = modo == 1 ? ux + a : a; y = modo == 1 ? uy + b : b;
                marca = 0; guia = false;
                completo = true;
            }
            x = Math.Round(x, 9); y = Math.Round(y, 9);
        }

        void Enquadrar()
        {
            if (tela == null || tela.Width < 50) return;
            if (m.Nos.Count == 0 && !temUlt) { esc = 60; ox = 110; oy = tela.Height - 110; tela.Invalidate(); return; }
            double x0 = double.MaxValue, x1 = double.MinValue, y0 = double.MaxValue, y1 = double.MinValue;
            foreach (No n in m.Nos)
            {
                x0 = Math.Min(x0, n.X); x1 = Math.Max(x1, n.X); y0 = Math.Min(y0, n.Y); y1 = Math.Max(y1, n.Y);
            }
            if (temUlt) { x0 = Math.Min(x0, ux); x1 = Math.Max(x1, ux); y0 = Math.Min(y0, uy); y1 = Math.Max(y1, uy); }
            double w = Math.Max(x1 - x0, 1e-3), h = Math.Max(y1 - y0, 1e-3), mg = 120;
            esc = Math.Min((tela.Width - 2 * mg) / w, (tela.Height - 2 * mg) / h);
            esc = Math.Max(1, Math.Min(esc, 400));
            ox = tela.Width / 2.0 - (x0 + x1) / 2 * esc;
            oy = tela.Height / 2.0 + (y0 + y1) / 2 * esc;
            tela.Invalidate();
        }

        void Zoom(double f, int cx, int cy)
        {
            double novo = Math.Max(0.5, Math.Min(esc * f, 5000));
            f = novo / esc;
            ox = cx - (cx - ox) * f;
            oy = cy - (cy - oy) * f;
            esc = novo;
            tela.Invalidate();
        }

        // ---------- insercao de barras e nos ----------

        void AtualizarEntrada()
        {
            if (entrada == null) return;
            entrada.Visible = ferr == 1 || ferr == 2;
            bool segundo = ferr == 1 && temUlt;
            int modo = segundo ? modoPref : 2;
            carregando = true;
            cbModo.SelectedIndex = modo;
            carregando = false;
            cbModo.Enabled = segundo;
            lbEtapa.Text = ferr == 2 ? "Nó:" : segundo ? "Próximo ponto:" : "Primeiro ponto:";
            lbA.Text = modo == 0 ? "Comprimento (m)" : modo == 1 ? "Distância X (m)" : "X (m)";
            lbB.Text = modo == 0 ? "Ângulo (°)" : modo == 1 ? "Distância Y (m)" : "Y (m)";
            lbDica.Text = modo == 0 ? "Ângulo vazio = direção do mouse" : "";
            int x = lbEtapa.Right + 10;
            foreach (Control c in new Control[] { cbModo, lbA, tbA, lbB, tbB, btInserir, lbDica })
            {
                c.Left = x;
                x = c.Right + (c is Label ? 5 : 12);
            }
        }

        void InserirDigitado()
        {
            double x, y;
            Atrair(mouse, out x, out y);
            bool completo;
            ComDigitado(ref x, ref y, out completo);
            if (!completo)
            {
                Aviso(temUlt && ferr == 1 && modoPref == 0 ? "Digite o comprimento." : "Preencha os dois campos.");
                return;
            }
            tbA.Clear(); tbB.Clear();
            Ponto(x, y);
            PointF q = P(x, y);
            if (q.X < 20 || q.Y < 20 || q.X > tela.Width - 20 || q.Y > tela.Height - 20) Enquadrar();
            tbA.Focus();
        }

        public void Digitar(string medidas)
        {
            Ferramenta(1);
            foreach (string s in medidas.Split('|'))
            {
                string[] p = s.Split(';');
                tbA.Text = p[0];
                tbB.Text = p.Length > 1 ? p[1] : "";
                InserirDigitado();
            }
            Enquadrar();
        }

        void Ponto(double x, double y)
        {
            if (ferr == 2)
            {
                int antes = m.Nos.Count;
                m.ObterNo(x, y, TOL);
                if (m.Nos.Count > antes) Registrar();
                return;
            }
            if (!temUlt)
            {
                ux = x; uy = y; temUlt = true;
                AtualizarEntrada(); tela.Invalidate();
                return;
            }
            if (Math.Abs(x - ux) < TOL && Math.Abs(y - uy) < TOL) return;
            No a = m.ObterNo(ux, uy, TOL), c = m.ObterNo(x, y, TOL);
            m.AddBarra(a, c, TOL);
            ux = c.X; uy = c.Y;
            Registrar();
        }

        protected override bool ProcessCmdKey(ref Message msgK, Keys k)
        {
            if (k == Keys.Escape) { Cancelar(); return true; }
            switch (k)
            {
                case Keys.Control | Keys.N: Novo(); return true;
                case Keys.Control | Keys.O: AbrirDialogo(); return true;
                case Keys.Control | Keys.S: Salvar(false); return true;
                case Keys.Control | Keys.Shift | Keys.S: Salvar(true); return true;
                case Keys.Control | Keys.Z: Desfazer(); return true;
                case Keys.Control | Keys.A: SelecionarTudo(); return true;
                case Keys.Control | Keys.E: Exportar(); return true;
                case Keys.F1: Ajuda(); return true;
                case Keys.F4: Enquadrar(); return true;
            }
            return base.ProcessCmdKey(ref msgK, k);
        }

        // ---------- mouse ----------

        void TelaMouseDown(object o, MouseEventArgs e)
        {
            tela.Focus();
            mouse = e.Location;
            if (e.Button == MouseButtons.Middle || e.Button == MouseButtons.Right) { pan = true; arrasto = botaoDir = e.Location; return; }
            if (e.Button != MouseButtons.Left) return;
            if (ferr == 1 || ferr == 2)
            {
                double x, y;
                bool completo;
                Atrair(e.Location, out x, out y);
                ComDigitado(ref x, ref y, out completo);
                tbA.Clear(); tbB.Clear();
                Ponto(x, y);
                return;
            }
            double k;
            if (ferr == 3)
            {
                // aplicando um atributo: cada clique aplica na hora
                object a = Pegar(e.Location, apFiltro, out k);
                if (a == null) { if (apJanela) { caixa = true; c0 = c1 = e.Location; } return; }
                List<object> um = new List<object>();
                um.Add(a);
                AplicarEm(um, k);
                Registrar();
                return;
            }
            bool soma = (ModifierKeys & (Keys.Shift | Keys.Control)) != 0;
            object alvo = Pegar(e.Location, 0, out k);
            if (alvo == null)
            {
                if (!soma) sel.Clear();
                caixa = true; c0 = c1 = e.Location;
            }
            else if (soma)
            {
                if (sel.Count > 0 && sel[0].GetType() != alvo.GetType()) sel.Clear();
                if (sel.Contains(alvo)) sel.Remove(alvo); else sel.Add(alvo);
            }
            else { sel.Clear(); sel.Add(alvo); }
            Selecionou();
        }

        void TelaMouseMove(object o, MouseEventArgs e)
        {
            if (pan) { ox += e.X - arrasto.X; oy += e.Y - arrasto.Y; arrasto = e.Location; }
            if (caixa) c1 = e.Location;
            mouse = e.Location;
            dentro = true;
            sobre = null;
            if ((ferr == 0 || ferr == 3) && !caixa && !pan)
            {
                double k;
                sobre = vista > 0 ? BarraEm(mouse, out k) : Pegar(mouse, ferr == 3 ? apFiltro : 0, out k);
            }
            double wx = (mouse.X - ox) / esc, wy = (oy - mouse.Y) / esc;
            coord.Text = "   X " + Num.Fmt(wx, 3) + "     Y " + Num.Fmt(wy, 3);
            tela.Invalidate();
        }

        void TelaMouseUp(object o, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right || e.Button == MouseButtons.Middle)
            {
                pan = false;
                // botao direito sem arrastar encerra a sequencia / a ferramenta
                if (e.Button == MouseButtons.Right && Math.Abs(e.X - botaoDir.X) < 4 && Math.Abs(e.Y - botaoDir.Y) < 4)
                {
                    // sem comando ativo, o botao direito abre o menu do no/barra; com comando ativo, termina o comando
                    if (ferr == 0 && !caixa && !abaRes) MenuContexto(e.Location);
                    else Cancelar();
                }
                return;
            }
            if (!caixa) return;
            caixa = false;
            RectangleF r = RectangleF.FromLTRB(Math.Min(c0.X, c1.X), Math.Min(c0.Y, c1.Y), Math.Max(c0.X, c1.X), Math.Max(c0.Y, c1.Y));
            if (r.Width > 3 && r.Height > 3)
            {
                bool cruzando = c1.X < c0.X;
                int f = ferr == 3 ? apFiltro : 0;
                List<object> achados = new List<object>();
                if (f != 1)
                    foreach (Barra b in m.Barras)
                    {
                        PointF a = P(m.NoPorId(b.N1)), c = P(m.NoPorId(b.N2));
                        if (cruzando ? Cruza(r, a, c) : (r.Contains(a) && r.Contains(c))) achados.Add(b);
                    }
                if (achados.Count == 0 && f != 2)
                    foreach (No n in m.Nos) if (r.Contains(P(n))) achados.Add(n);
                if (ferr == 3)
                {
                    int n = AplicarEm(achados, -1);
                    if (n > 0) { Registrar(); Aviso(apNome + ": aplicado em " + n + "."); }
                    tela.Invalidate();
                    return;
                }
                if (achados.Count > 0 && sel.Count > 0 && sel[0].GetType() != achados[0].GetType()) sel.Clear();
                foreach (object a in achados) if (!sel.Contains(a)) sel.Add(a);
            }
            Selecionou();
        }

        static bool Cruza(RectangleF r, PointF a, PointF b)
        {
            if (r.Contains(a) || r.Contains(b)) return true;
            PointF p1 = new PointF(r.Left, r.Top), p2 = new PointF(r.Right, r.Top), p3 = new PointF(r.Right, r.Bottom), p4 = new PointF(r.Left, r.Bottom);
            return Seg(a, b, p1, p2) || Seg(a, b, p2, p3) || Seg(a, b, p3, p4) || Seg(a, b, p4, p1);
        }

        static bool Seg(PointF a, PointF b, PointF c, PointF d)
        {
            float d1 = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X), d2 = (b.X - a.X) * (d.Y - a.Y) - (b.Y - a.Y) * (d.X - a.X);
            float d3 = (d.X - c.X) * (a.Y - c.Y) - (d.Y - c.Y) * (a.X - c.X), d4 = (d.X - c.X) * (b.Y - c.Y) - (d.Y - c.Y) * (b.X - c.X);
            return d1 * d2 < 0 && d3 * d4 < 0;
        }

        // ---------- desenho ----------

        string Dica()
        {
            if (ferr == 1) return temUlt ? "Clique o próximo ponto, ou digite o comprimento embaixo e tecle Enter.  Shift = horizontal/vertical.  Botão direito ou Esc encerra."
                                         : "Clique o primeiro ponto da barra, ou digite X e Y embaixo e tecle Enter.";
            if (ferr == 2) return "Clique para criar um nó. Sobre uma barra, divide a barra em duas.  Botão direito ou Esc encerra.";
            if (ferr == 3) return "Clique " + (apFiltro == 1 ? "nos nós" : apFiltro == 2 ? "nas barras" : "no nó ou na barra") +
                                  (apJanela ? ", ou arraste uma janela" : "") + ".  Botão direito ou Esc encerra.";
            if (vista == 5) return "Verde passa, vermelho não passa; o número é o aproveitamento. Duplo clique numa barra compara com os outros perfis.";
            return vista > 0 ? "Passe o mouse sobre uma barra para ler o valor naquele ponto." : "";
        }

        string NomeComando() { return ferr == 1 ? "BARRA" : ferr == 2 ? "NÓ" : ferr == 3 ? apNome.ToUpper() : ""; }

        void Desenhar(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            DesenharGrade(g);

            using (Pen pb = new Pen(t.Barra, vista == 0 ? 2.4f : 1.4f))
            using (Pen ps = new Pen(t.Sel, 3.2f))
            using (Pen ph = new Pen(Color.FromArgb(90, t.Sel), 8f))
            using (Brush bt = new SolidBrush(t.TextoFraco))
            {
                foreach (Barra b in m.Barras)
                {
                    PointF a = P(m.NoPorId(b.N1)), c = P(m.NoPorId(b.N2));
                    if (b == sobre && vista == 0) g.DrawLine(ph, a, c);
                    Verif vf = null;
                    if (vista == 5 && res.Ok && verifs.TryGetValue(b, out vf))
                    {
                        // verificacao: verde passa, vermelho nao passa, cinza sem verificacao; o numero e o aproveitamento
                        Color cv = !vf.Aplicavel ? t.TextoFraco : vf.Passa ? CorOk : CorRuim;
                        using (Pen pv = new Pen(cv, sel.Contains(b) ? 6f : 4f)) g.DrawLine(pv, a, c);
                        string sv = vf.Aplicavel ? Math.Round(vf.Ap * 100) + "%" : "—";
                        SizeF zv = g.MeasureString(sv, fonteN);
                        using (Brush bv = new SolidBrush(cv)) g.DrawString(sv, fonteN, bv, (a.X + c.X) / 2 + Ui.Z(8), (a.Y + c.Y) / 2 - zv.Height - Ui.Z(4));
                    }
                    else g.DrawLine(sel.Contains(b) ? ps : pb, a, c);
                    float dx = c.X - a.X, dy = c.Y - a.Y, l = (float)Math.Sqrt(dx * dx + dy * dy);
                    if (l < 1) continue;
                    dx /= l; dy /= l;
                    if (b.RotulaInicio) Bolinha(g, a.X + dx * 10, a.Y + dy * 10);
                    if (b.RotulaFim) Bolinha(g, c.X - dx * 10, c.Y - dy * 10);
                    if (vista == 0 && rotulos && l > 90)
                    {
                        string s = b.Secao + "  ·  " + Num.Fmt(m.Comp(b), 2) + " m";
                        SizeF sz = g.MeasureString(s, fonte);
                        // texto do lado "de baixo/direita" da barra, para nao brigar com as cargas
                        float nx = -dy, ny = dx;
                        if (ny < 0 || (Math.Abs(ny) < 1e-3 && nx < 0)) { nx = -nx; ny = -ny; }
                        float mx = (a.X + c.X) / 2 + nx * (4 + sz.Height / 2 + Math.Abs(nx) * sz.Width / 2), my = (a.Y + c.Y) / 2 + ny * (4 + sz.Height / 2);
                        g.DrawString(s, fonte, bt, mx - sz.Width / 2, my - sz.Height / 2);
                    }
                }
            }

            if (vista == 0) foreach (Barra b in m.Barras) CargaBarra(g, b);
            if (res.Ok && vista >= 1 && vista <= 3) foreach (Barra b in m.Barras) Diagrama(g, b, vista - 1);
            if (res.Ok && vista == 4) Deformada(g);

            using (Brush bn = new SolidBrush(t.Barra))
            using (Brush bsel = new SolidBrush(t.Sel))
            using (Pen ph = new Pen(t.Sel, 1.5f))
                foreach (No n in m.Nos)
                {
                    PointF p = P(n);
                    DesenharApoio(g, n, p);
                    if (n.Placa != null) DesenharPlaca(g, n, p);
                    if (vista == 0) CargaNo(g, n, p);
                    bool s = sel.Contains(n);
                    float r = s ? 5 : 3;
                    g.FillRectangle(s ? bsel : bn, p.X - r, p.Y - r, 2 * r, 2 * r);
                    if (n == sobre) g.DrawRectangle(ph, p.X - 8, p.Y - 8, 16, 16);
                    if (res.Ok && vista > 0) Reacoes(g, n, p);
                }

            Ucs(g);
            Cubo(g);
            if ((ferr == 1 || ferr == 2) && dentro) Elastico(g);
            if (ferr == 0 && dentro && res.Ok && vista > 0) Leitura(g);
            if (caixa)
            {
                bool cruz = c1.X < c0.X;
                Color cc = cruz ? Color.FromArgb(60, 190, 90) : Color.FromArgb(60, 130, 255);
                RectangleF r = RectangleF.FromLTRB(Math.Min(c0.X, c1.X), Math.Min(c0.Y, c1.Y), Math.Max(c0.X, c1.X), Math.Max(c0.Y, c1.Y));
                using (Brush bf = new SolidBrush(Color.FromArgb(45, cc)))
                using (Pen pc = new Pen(cc, 1) { DashStyle = cruz ? DashStyle.Dash : DashStyle.Solid })
                {
                    g.FillRectangle(bf, r);
                    g.DrawRectangle(pc, r.X, r.Y, r.Width, r.Height);
                }
            }

            using (Brush bl = new SolidBrush(t.Vista[vista]))
            using (Brush bd = new SolidBrush(t.TextoFraco))
            {
                string leg = "[-][Frontal][" + NomeVista[vista] + "]";
                if (vista > 0 && !res.Ok) leg += "  —  sem resultados: " + res.Erro;
                g.DrawString(leg, fonteN, bl, 10, 8);
                float dy = vista == 4 ? 46 : 28;
                if (ferr != 0)
                {
                    // comando em andamento: etiqueta azul com o nome, e ao lado o que fazer
                    string nc = NomeComando();
                    SizeF sz = g.MeasureString(nc, fonteN);
                    using (Brush ba = new SolidBrush(t.Acento)) g.FillRectangle(ba, 10, dy, sz.Width + 12, sz.Height + 6);
                    g.DrawString(nc, fonteN, Brushes.White, 16, dy + 3);
                    using (Brush bt = new SolidBrush(t.Texto)) g.DrawString(Dica(), fonte, bt, 10 + sz.Width + 20, dy + 3);
                }
                else g.DrawString(Dica(), fonte, bd, 10, dy);
            }
        }

        // Icone dos eixos no canto de baixo, como o UCS do CAD.
        void Ucs(Graphics g)
        {
            float x = Ui.Z(28), y = tela.Height - Ui.Z(28), c = Ui.Z(46);
            using (Pen px = new Pen(Color.FromArgb(214, 80, 80), 1.6f))
            using (Pen py = new Pen(Color.FromArgb(90, 190, 100), 1.6f))
            using (Pen pq = new Pen(t.Texto, 1))
            using (Brush bx = new SolidBrush(px.Color))
            using (Brush by = new SolidBrush(py.Color))
            {
                g.DrawLine(px, x, y, x + c, y); g.DrawLine(py, x, y, x, y - c);
                g.DrawRectangle(pq, x - 4, y - 4, 8, 8);
                g.DrawString("X", fonteN, bx, x + c + 2, y - Ui.Z(8));
                g.DrawString("Y", fonteN, by, x - Ui.Z(5), y - c - Ui.Z(18));
            }
        }

        // Cubo de vista no canto de cima, como o ViewCube: aqui a vista e sempre a frontal.
        void Cubo(Graphics g)
        {
            float lado = Ui.Z(54), cx = tela.Width - Ui.Z(70), cy = Ui.Z(70), raio = Ui.Z(46);
            Color cinza = t.Escuro ? Color.FromArgb(120, 130, 146) : Color.FromArgb(150, 158, 170);
            using (Pen pa = new Pen(cinza, Ui.Z(5)))
            using (Brush bf = new SolidBrush(t.Escuro ? Color.FromArgb(198, 204, 213) : Color.FromArgb(236, 238, 242)))
            using (Pen pb = new Pen(cinza, 1))
            using (Brush bt = new SolidBrush(Color.FromArgb(52, 60, 74)))
            using (Brush bl = new SolidBrush(t.TextoFraco))
            using (Font fc = Ui.F(7.5f, FontStyle.Bold))
            {
                g.DrawEllipse(pa, cx - raio, cy - raio * 0.42f + lado * 0.52f, 2 * raio, raio * 0.84f);
                g.FillRectangle(bf, cx - lado / 2, cy - lado / 2, lado, lado);
                g.DrawRectangle(pb, cx - lado / 2, cy - lado / 2, lado, lado);
                SizeF sz = g.MeasureString("FRONTAL", fc);
                g.DrawString("FRONTAL", fc, bt, cx - sz.Width / 2, cy - sz.Height / 2);
                g.DrawString("S", fonteN, bl, cx - Ui.Z(4), cy + lado / 2 + raio * 0.84f - Ui.Z(4));
                g.DrawString("O", fonteN, bl, cx - raio - Ui.Z(16), cy + lado * 0.52f - Ui.Z(8));
                g.DrawString("L", fonteN, bl, cx + raio + Ui.Z(6), cy + lado * 0.52f - Ui.Z(8));
            }
        }

        void DesenharGrade(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.None;
            if (m.MostrarGrade && m.Grade > 0)
            {
                // grade adaptativa: se as linhas ficarem juntas demais, mostra de 5 em 5
                double passo = m.Grade;
                while (passo * esc < 9) passo *= 5;
                int i0 = (int)Math.Floor(-ox / (passo * esc)), i1 = (int)Math.Ceiling((tela.Width - ox) / (passo * esc));
                int j0 = (int)Math.Floor((oy - tela.Height) / (passo * esc)), j1 = (int)Math.Ceiling(oy / (passo * esc));
                using (Pen p1 = new Pen(t.GradeMenor, 1))
                using (Pen p5 = new Pen(t.GradeMaior, 1))
                {
                    for (int i = i0; i <= i1; i++)
                    {
                        float x = (float)Math.Round(ox + i * passo * esc);
                        g.DrawLine(i % 5 == 0 ? p5 : p1, x, 0, x, tela.Height);
                    }
                    for (int j = j0; j <= j1; j++)
                    {
                        float y = (float)Math.Round(oy - j * passo * esc);
                        g.DrawLine(j % 5 == 0 ? p5 : p1, 0, y, tela.Width, y);
                    }
                }
            }
            // origem
            using (Pen po = new Pen(t.TextoFraco, 1))
            {
                g.DrawLine(po, (float)ox - 9, (float)oy, (float)ox + 9, (float)oy);
                g.DrawLine(po, (float)ox, (float)oy - 9, (float)ox, (float)oy + 9);
            }
            g.SmoothingMode = SmoothingMode.AntiAlias;
        }

        void Bolinha(Graphics g, float x, float y)
        {
            using (Brush b = new SolidBrush(t.Fundo)) g.FillEllipse(b, x - 4.5f, y - 4.5f, 9, 9);
            using (Pen p = new Pen(t.Barra, 1.5f)) g.DrawEllipse(p, x - 4.5f, y - 4.5f, 9, 9);
        }

        static void Seta(Graphics g, Pen pen, PointF de, PointF para)
        {
            g.DrawLine(pen, de, para);
            float dx = para.X - de.X, dy = para.Y - de.Y, l = (float)Math.Sqrt(dx * dx + dy * dy);
            if (l < 2) return;
            dx /= l; dy /= l;
            float c = Math.Min(8, l * 0.6f);
            PointF[] tri = { para, new PointF(para.X - dx * c - dy * c * 0.4f, para.Y - dy * c + dx * c * 0.4f),
                             new PointF(para.X - dx * c + dy * c * 0.4f, para.Y - dy * c - dx * c * 0.4f) };
            using (Brush b = new SolidBrush(pen.Color)) g.FillPolygon(b, tri);
        }

        void DesenharApoio(Graphics g, No n, PointF p)
        {
            using (Pen pa = new Pen(t.Apoio, 1.8f))
            using (Brush ba = new SolidBrush(t.Apoio))
            using (Brush hachura = new HatchBrush(HatchStyle.BackwardDiagonal, t.Apoio, Color.Transparent))
            {
                if (n.ApoioX && n.ApoioY && n.ApoioGiro)
                {
                    g.FillRectangle(hachura, p.X - 10, p.Y - 10, 20, 20);
                    g.DrawRectangle(pa, p.X - 10, p.Y - 10, 20, 20);
                }
                else if (n.ApoioY && !n.ApoioGiro)
                {
                    PointF[] tri = { p, new PointF(p.X - 9, p.Y + 15), new PointF(p.X + 9, p.Y + 15) };
                    g.DrawPolygon(pa, tri);
                    if (n.ApoioX) g.FillRectangle(hachura, p.X - 13, p.Y + 15, 26, 6);
                    g.DrawLine(pa, p.X - 13, p.Y + (n.ApoioX ? 15 : 19), p.X + 13, p.Y + (n.ApoioX ? 15 : 19));
                }
                else if (n.ApoioX && !n.ApoioY && !n.ApoioGiro)
                {
                    PointF[] tri = { p, new PointF(p.X - 15, p.Y - 9), new PointF(p.X - 15, p.Y + 9) };
                    g.DrawPolygon(pa, tri);
                    g.DrawLine(pa, p.X - 19, p.Y - 13, p.X - 19, p.Y + 13);
                }
                else if (n.ApoioX || n.ApoioY || n.ApoioGiro)
                {
                    string s = "trava " + (n.ApoioX ? "X " : "") + (n.ApoioY ? "Y " : "") + (n.ApoioGiro ? "giro" : "");
                    g.DrawRectangle(pa, p.X - 7, p.Y - 7, 14, 14);
                    g.DrawString(s, fonte, ba, p.X + 9, p.Y + 6);
                }
                string k = (n.MolaX != 0 ? "mola X " : "") + (n.MolaY != 0 ? "mola Y " : "") + (n.MolaGiro != 0 ? "mola giro" : "");
                if (k != "") g.DrawString(k, fonte, ba, p.X + 9, p.Y - 20);
            }
        }

        void CargaNo(Graphics g, No n, PointF p)
        {
            if (!n.TemCarga) return;
            using (Pen pc = new Pen(t.Carga, 2))
            using (Brush bc = new SolidBrush(t.Carga))
            {
                const float c = 46;
                if (n.Fx != 0)
                {
                    float s = Math.Sign(n.Fx);
                    Seta(g, pc, new PointF(p.X - s * c, p.Y), new PointF(p.X - s * 5, p.Y));
                    g.DrawString(Num.Fmt(Math.Abs(n.Fx), 2) + " kN", fonte, bc, p.X - s * c - (s > 0 ? 50 : -4), p.Y - 18);
                }
                if (n.Fy != 0)
                {
                    float s = Math.Sign(n.Fy);
                    Seta(g, pc, new PointF(p.X, p.Y + s * c), new PointF(p.X, p.Y + s * 5));
                    g.DrawString(Num.Fmt(Math.Abs(n.Fy), 2) + " kN", fonte, bc, p.X + 5, p.Y + s * c - (s > 0 ? 4 : 12));
                }
                if (n.Mz != 0)
                {
                    g.DrawArc(pc, p.X - 17, p.Y - 17, 34, 34, 30, 270);
                    g.DrawString((n.Mz > 0 ? "↺ " : "↻ ") + Num.Fmt(Math.Abs(n.Mz), 2) + " kN.m", fonte, bc, p.X + 18, p.Y - 28);
                }
            }
        }

        void CargaBarra(Graphics g, Barra b)
        {
            if (!b.TemCarga) return;
            PointF a = P(m.NoPorId(b.N1)), c = P(m.NoPorId(b.N2));
            float bx = c.X - a.X, by = c.Y - a.Y, l = (float)Math.Sqrt(bx * bx + by * by);
            if (l < 4) return;
            int n = Math.Max(2, (int)(l / 22));
            using (Pen pc = new Pen(t.Carga, 1.2f))
            using (Brush bc = new SolidBrush(t.Carga))
            {
                double q = Math.Sqrt(b.QGlobalX * b.QGlobalX + b.QGlobalY * b.QGlobalY);
                if (q > 0)
                {
                    // seta na direcao da carga, com a ponta na barra
                    float dx = (float)(b.QGlobalX / q), dy = (float)(-b.QGlobalY / q);
                    const float h = 26;
                    for (int i = 0; i <= n; i++)
                    {
                        float x = a.X + bx * i / n, y = a.Y + by * i / n;
                        Seta(g, pc, new PointF(x - dx * h, y - dy * h), new PointF(x, y));
                    }
                    g.DrawLine(pc, a.X - dx * h, a.Y - dy * h, c.X - dx * h, c.Y - dy * h);
                    g.DrawString(Num.Fmt(q, 2) + " kN/m", fonte, bc, (a.X + c.X) / 2 - dx * (h + 14) - 22, (a.Y + c.Y) / 2 - dy * (h + 14) - 7);
                }
                double qm = Math.Max(Math.Abs(b.QLocalIni), Math.Abs(b.QLocalFim));
                if (qm > 0)
                {
                    // normal local (-s, c) em coordenadas de tela
                    float nx = by / l, ny = -bx / l;
                    const float h = 30;
                    PointF ant = PointF.Empty;
                    for (int i = 0; i <= n; i++)
                    {
                        float k = (float)i / n, x = a.X + bx * k, y = a.Y + by * k;
                        float v = (float)((b.QLocalIni + (b.QLocalFim - b.QLocalIni) * k) / qm) * h;
                        PointF cauda = new PointF(x - nx * v, y - ny * v);
                        if (Math.Abs(v) > 3) Seta(g, pc, cauda, new PointF(x, y));
                        if (i > 0) g.DrawLine(pc, ant, cauda);
                        ant = cauda;
                    }
                    float v0 = (float)(b.QLocalIni / qm) * h, v1 = (float)(b.QLocalFim / qm) * h;
                    if (b.QLocalIni != 0) g.DrawString(Num.Fmt(Math.Abs(b.QLocalIni), 2), fonte, bc, a.X - nx * (v0 + Math.Sign(v0) * 12) - 10, a.Y - ny * (v0 + Math.Sign(v0) * 12) - 7);
                    if (b.QLocalFim != 0) g.DrawString(Num.Fmt(Math.Abs(b.QLocalFim), 2) + " kN/m", fonte, bc, c.X - nx * (v1 + Math.Sign(v1) * 12) - 10, c.Y - ny * (v1 + Math.Sign(v1) * 12) - 7);
                }
            }
        }

        double MaxDiagrama(int tipo)
        {
            double mx = 0;
            foreach (Barra b in m.Barras)
            {
                if (b.Res == null) continue;
                double a, c;
                b.Res.Extremos(tipo, out a, out c);
                mx = Math.Max(mx, Math.Max(Math.Abs(a), Math.Abs(c)));
            }
            return mx;
        }

        void Diagrama(Graphics g, Barra b, int tipo)
        {
            ResBarra r = b.Res;
            if (r == null) return;
            double mx = MaxDiagrama(tipo);
            if (mx < 1e-9) return;
            double k = 70 * fatorDiag / mx;          // px por unidade
            double sinal = tipo == 2 ? -1 : 1;       // momento desenhado do lado tracionado
            No n1 = m.NoPorId(b.N1);
            PointF a = P(n1), c = P(m.NoPorId(b.N2));
            float nx = (float)-r.S, ny = (float)-r.C;   // normal local em tela
            const int NP = 32;
            PointF[] pol = new PointF[NP + 3];
            pol[0] = a;
            for (int i = 0; i <= NP; i++)
            {
                double x = r.L * i / NP, v = r.Valor(tipo, x) * k * sinal;
                PointF q = P(n1.X + r.C * x, n1.Y + r.S * x);
                pol[i + 1] = new PointF(q.X + nx * (float)v, q.Y + ny * (float)v);
            }
            pol[NP + 2] = c;
            Color cor = t.Vista[tipo + 1];
            using (Brush bf = new SolidBrush(Color.FromArgb(t.Escuro ? 55 : 38, cor)))
            using (Pen pd = new Pen(cor, 1.4f))
            using (Brush bt = new SolidBrush(cor))
            {
                g.FillPolygon(bf, pol);
                g.DrawLines(pd, pol);
                // valores: pontas e extremo interno
                List<double> xs = new List<double>();
                xs.Add(0); xs.Add(r.L);
                double xmn = 0, xmx = 0, vmn = double.MaxValue, vmx = double.MinValue;
                foreach (double x in r.Pontos(tipo))
                {
                    double v = r.Valor(tipo, x);
                    if (v < vmn) { vmn = v; xmn = x; }
                    if (v > vmx) { vmx = v; xmx = x; }
                }
                if (xmn > 0.03 * r.L && xmn < 0.97 * r.L) xs.Add(xmn);
                if (xmx > 0.03 * r.L && xmx < 0.97 * r.L && Math.Abs(xmx - xmn) > 0.03 * r.L) xs.Add(xmx);
                foreach (double x in xs)
                {
                    double val = r.Valor(tipo, x);
                    if (Math.Abs(val) < 0.005) continue;
                    double v = val * k * sinal;
                    PointF q = P(n1.X + r.C * x, n1.Y + r.S * x);
                    string s = Num.Fmt(val, 2);
                    SizeF sz = g.MeasureString(s, fonte);
                    float d = (float)v + Math.Sign(v) * 10;
                    // puxa o texto das pontas um pouco para dentro da barra
                    float dentroB = x == 0 ? 16 : x == r.L ? -16 : 0;
                    g.DrawString(s, fonte, bt, q.X + nx * d + (float)r.C * dentroB - sz.Width / 2, q.Y + ny * d - (float)r.S * dentroB - sz.Height / 2);
                }
            }
        }

        void Deformada(Graphics g)
        {
            double mx = res.MaxDesl;
            foreach (Barra b in m.Barras)
                if (b.Res != null)
                    for (int i = 0; i <= 10; i++)
                    {
                        double x = b.Res.L * i / 10, u = b.Res.Ux(x), v = b.Res.V(x);
                        mx = Math.Max(mx, Math.Sqrt(u * u + v * v));
                    }
            if (mx < 1e-12) return;
            double amp = 45 * fatorDiag / (mx * esc);
            using (Pen pd = new Pen(t.Vista[4], 2))
            using (Brush bt = new SolidBrush(t.Vista[4]))
            {
                foreach (Barra b in m.Barras)
                {
                    ResBarra r = b.Res;
                    if (r == null) continue;
                    No n1 = m.NoPorId(b.N1);
                    PointF[] pts = new PointF[33];
                    for (int i = 0; i <= 32; i++)
                    {
                        double x = r.L * i / 32, u = r.Ux(x) * amp, v = r.V(x) * amp;
                        pts[i] = P(n1.X + r.C * (x + u) - r.S * v, n1.Y + r.S * (x + u) + r.C * v);
                    }
                    g.DrawLines(pd, pts);
                }
                g.DrawString("Ampliada " + Math.Round(amp) + " vezes.  Maior deslocamento: " + Num.Fmt(mx * 1000, 2) + " mm", fonte, bt, 10, 26);
            }
        }

        void Reacoes(Graphics g, No n, PointF p)
        {
            if (!n.TemApoio || n.Res == null) return;
            string s = "";
            if (Math.Abs(n.Res[3]) > 0.005) s += "Rx = " + Num.Fmt(n.Res[3], 2) + " kN\n";
            if (Math.Abs(n.Res[4]) > 0.005) s += "Ry = " + Num.Fmt(n.Res[4], 2) + " kN\n";
            if (Math.Abs(n.Res[5]) > 0.005) s += "M = " + Num.Fmt(n.Res[5], 2) + " kN.m\n";
            if (s == "") return;
            using (Brush b = new SolidBrush(t.Apoio))
            {
                SizeF sz = g.MeasureString(s, fonte);
                g.DrawString(s, fonte, b, p.X - sz.Width / 2, p.Y + 24);
            }
        }

        void Caixinha(Graphics g, string s, float x, float y, Color borda)
        {
            SizeF sz = g.MeasureString(s, fonteN);
            if (x + sz.Width + 12 > tela.Width) x = tela.Width - sz.Width - 12;
            using (Brush bf = new SolidBrush(t.Escuro ? Color.FromArgb(240, 50, 56, 68) : Color.FromArgb(245, 255, 255, 255)))
            using (Pen pb = new Pen(borda, 1))
            using (Brush bt = new SolidBrush(t.Texto))
            {
                g.FillRectangle(bf, x, y, sz.Width + 10, sz.Height + 6);
                g.DrawRectangle(pb, x, y, sz.Width + 10, sz.Height + 6);
                g.DrawString(s, fonteN, bt, x + 5, y + 3);
            }
        }

        // Valor do diagrama no ponto da barra que esta sob o mouse.
        void Leitura(Graphics g)
        {
            double k;
            Barra b = BarraEm(mouse, out k);
            if (b == null || b.Res == null) return;
            ResBarra r = b.Res;
            double x = k * r.L;
            No n1 = m.NoPorId(b.N1);
            PointF q = P(n1.X + r.C * x, n1.Y + r.S * x);
            using (Brush bp = new SolidBrush(t.Vista[vista])) g.FillEllipse(bp, q.X - 4, q.Y - 4, 8, 8);
            string s = "a " + Num.Fmt(x, 2) + " m do início:   ";
            if (vista == 1) s += "N = " + Num.Fmt(r.N(x), 2) + " kN";
            else if (vista == 2) s += "Q = " + Num.Fmt(r.Q(x), 2) + " kN";
            else if (vista == 3) s += "M = " + Num.Fmt(r.M(x), 2) + " kN.m";
            else if (vista == 5)
            {
                Verif vf;
                if (!verifs.TryGetValue(b, out vf)) return;
                s = b.Secao + ":   " + (vf.Aplicavel ? (vf.Passa ? "passa" : "NÃO PASSA") + ", aproveitamento " + Math.Round(vf.Ap * 100) + "% (" + vf.Critico + ").   Duplo clique compara perfis." : vf.Motivo);
            }
            else
            {
                double u = r.Ux(x), v = r.V(x);
                s += "desloc. X " + Num.Fmt((r.C * u - r.S * v) * 1000, 2) + " mm,  Y " + Num.Fmt((r.S * u + r.C * v) * 1000, 2) + " mm";
            }
            Caixinha(g, s, mouse.X + 16, mouse.Y + 18, t.Vista[vista]);
        }

        // Barra em construcao: linha elastica, guia horizontal/vertical, marcador de atracao e medida ao vivo.
        void Elastico(Graphics g)
        {
            double x, y;
            bool completo;
            Atrair(mouse, out x, out y);
            ComDigitado(ref x, ref y, out completo);
            PointF q = P(x, y);
            if (temUlt && ferr == 1)
            {
                PointF a = P(ux, uy);
                double dx = x - ux, dy = y - uy, l = Math.Sqrt(dx * dx + dy * dy);
                if (guia && l > 1e-9)
                    using (Pen pr = new Pen(t.Guia, 1) { DashStyle = DashStyle.Dot })
                    {
                        float ex = (float)(dx / l) * 4000, ey = (float)(-dy / l) * 4000;
                        g.DrawLine(pr, a.X - ex, a.Y - ey, a.X + ex, a.Y + ey);
                    }
                using (Pen pe = new Pen(t.Elastico, 1.8f) { DashStyle = DashStyle.Dash }) g.DrawLine(pe, a, q);
                using (Brush be = new SolidBrush(t.Elastico)) g.FillRectangle(be, a.X - 4, a.Y - 4, 8, 8);
                if (l > 1e-9)
                    Caixinha(g, Num.Fmt(l, 3) + " m     " + Num.Fmt(Math.Atan2(dy, dx) * 180 / Math.PI, 1) + "°", q.X + 16, q.Y + 18, completo ? t.Sel : t.Elastico);
            }
            else Caixinha(g, "X " + Num.Fmt(x, 3) + "     Y " + Num.Fmt(y, 3), q.X + 16, q.Y + 18, t.Elastico);
            using (Pen pm = new Pen(t.Guia, 1.8f))
            using (Brush bm = new SolidBrush(t.Guia))
            {
                if (marca == 1) { g.DrawRectangle(pm, q.X - 7, q.Y - 7, 14, 14); g.DrawString("nó", fonte, bm, q.X + 10, q.Y - 22); }
                else if (marca == 2) { g.DrawPolygon(pm, new[] { new PointF(q.X, q.Y - 8), new PointF(q.X - 8, q.Y + 6), new PointF(q.X + 8, q.Y + 6) }); g.DrawString("meio", fonte, bm, q.X + 10, q.Y - 22); }
                else if (marca == 3) { g.DrawEllipse(pm, q.X - 6, q.Y - 6, 12, 12); g.DrawString("sobre a barra", fonte, bm, q.X + 10, q.Y - 22); }
                else if (completo) g.FillEllipse(bm, q.X - 4, q.Y - 4, 8, 8);
            }
        }
    }
}
