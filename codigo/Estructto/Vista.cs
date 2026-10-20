// Area de desenho no estilo AutoCAD/Revit: mundo em metros (Y para cima), zoom na roda, pan no botao do meio
// (ou direito arrastando), selecao por clique / janela (esq->dir) / cruzamento (dir->esq), atracao a no, meio,
// sobre a barra, grade e polar 0/90, alca para arrastar no, entrada dinamica junto ao cursor.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Portico;

namespace NovoFtool
{
    public enum Ferramenta { Selecionar, Barra, No, ApoioEngaste, ApoioRotula, ApoioRoloV, ApoioRoloH, ApoioLivre, Rotula, CargaNo, CargaBarra, Placa, Medir }
    public enum Diagrama { Nenhum, Deformada, Normal, Cortante, Momento, Verificacao }
    public enum Atracao { Nenhuma, No, Meio, Sobre, Grade, Polar, Origem, Intersecao, Perpendicular, Extensao, Rastreio }

    public class Vista : FrameworkElement
    {
        public Modelo M = new Modelo();
        public readonly List<object> Sel = new List<object>();   // nos OU barras (nunca misturados)
        public Ferramenta Ferr = Ferramenta.Selecionar;
        public Diagrama Diag = Diagrama.Nenhum;
        public bool MostrarReacoes = true, MostrarValores = true, MostrarCargas = true, MostrarNomes = true, Orto, Polar = true, AtrairObjetos = true, Resolvido;
        // modos de atracao a objetos (como o OSNAP do AutoCAD) e rastreamento (OTRACK)
        public bool SnapNo = true, SnapMeio = true, SnapIntersecao = true, SnapPerpendicular = true, SnapProximo = true, SnapExtensao = true, Rastrear = true;
        readonly List<Point> adquiridos = new List<Point>();   // nos sob o cursor viram pontos de rastreamento
        readonly List<Point> guiasRastreio = new List<Point>();
        public double EscalaDiag = 1, EscalaDef = 1;
        // animacoes (Anim.cs): diagrama cresce de 0 ao trocar de tipo; selecao aparece com fade
        public static readonly DependencyProperty CrescProperty = DependencyProperty.Register("Cresc", typeof(double), typeof(Vista), new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty RealceProperty = DependencyProperty.Register("Realce", typeof(double), typeof(Vista), new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));
        double Cresc => (double)GetValue(CrescProperty);
        public void Crescer() { Anim.Valor(this, CrescProperty, 0, 1, Anim.Espacial); }
        public string Etiqueta;                            // nome do comando ativo (canto superior esquerdo)
        public Func<Barra, Verif> Verificar;               // usado na vista Verificacao

        public event Action<Point> MouseMundo;
        public event Action<double, double> Clique;        // clique de ferramenta (ja atraido)
        public event Action SelecaoMudou;
        public event Action<No, Point, Point> NoArrastado; // no, posicao antiga, nova
        public event Action<object, Point> MenuContexto;   // objeto (ou null), ponto na tela
        public event Action FimComando;                    // botao direito com comando ativo
        public event Action<object> DuploClique;

        double zoom = 60, ox = 100, oy = 400;
        Point? panIni; Point panOrig; bool panMoveu;
        Point mouseTela, mouseMundo; bool mouseDentro;
        Atracao atracao; Point? guiaDe;
        object hover;
        Point? janelaIni;                                  // selecao por janela
        No arrastando; Point arrastoIni; bool arrastoMoveu;
        public Point? InicioBarra, MedidaIni, MedidaFim;
        public string Leitura;                             // leitura do diagrama sob o mouse (texto; o cartao usa Insp)
        public bool MapaCalor;                             // vista Verificacao como mapa de calor (faixas de aproveitamento)
        public Rect ReservaHud;                            // area do HUD (nenhum rotulo vai para ela)
        bool zoomUsuario;                                  // o usuario mexeu no zoom/pan: nao reenquadrar sozinho
        DadosInsp insp;                                    // ponto sob o mouse (cartao de inspecao)

        // ---------- rotulos com anti-sobreposicao ----------
        sealed class Rot { public string T; public Point P; public Brush Cor; public int Prio; public double Peso; public Vector Pref; public bool Forcar; }
        readonly List<Rot> rotulos = new List<Rot>();
        readonly List<Rect> ocupados = new List<Rect>();   // caixas ja usadas (nomes das barras, etiqueta)
        static readonly Dictionary<Brush, Pen> bordasPilula = new Dictionary<Brush, Pen>();
        sealed class DadosInsp { public Barra B; public double X; public Point Base, Alvo; public bool TemAlvo; public string Pos, N, V, M, D; public Brush CorAlvo; }

        // ---------- cores (tema escuro Autodesk) ----------
        static Pen Caneta(Brush b, double w, double[] tr = null) { var p = new Pen(b, w); if (tr != null) p.DashStyle = new DashStyle(tr, 0); p.Freeze(); return p; }
        // as cores que mudam no modo de impressao (fundo branco) sao de instancia
        // Todas as cores vem dos tokens do TemaPlant (secao "Canvas CAD" do GUIA-DESIGN).
        Brush Fundo = TemaPlant.BBgCanvas;
        static readonly Pen GradeMenor = Caneta(TemaPlant.BGradeMenor, 1), GradeMaior = Caneta(TemaPlant.BGradeMaior, 1);
        static readonly Pen EixoX = Caneta(TemaPlant.BEixoX, 1.2), EixoY = Caneta(TemaPlant.BEixoY, 1.2);
        Brush BBarra = TemaPlant.BBarra;
        Pen PBarra = Caneta(TemaPlant.BBarra, 2.4);
        static readonly Pen PHover = Caneta(TemaPlant.BBarraHover, 4), PSel = Caneta(TemaPlant.BBarraSel, 5);
        static readonly Pen PHoverCalor = Caneta(TemaPlant.BBarraHover, 9), PSelCalor = Caneta(TemaPlant.BBarraSel, 11);
        Pen PApoio = Caneta(TemaPlant.BApoio, 1.6), PCarga = Caneta(TemaPlant.BCarga, 1.6), PReac = Caneta(TemaPlant.BReacao, 1.6);
        Pen PDef = Caneta(TemaPlant.BDeformada, 2.4);   // deformada: tracado cheio; a forma original fica como fantasma
        Pen PFantasma = Caneta(TemaPlant.B(TemaPlant.CorBarra, 0x70), 1.3, new double[] { 4, 3 });
        static readonly Pen PFant = Caneta(TemaPlant.BBarraSel, 1.5, new double[] { 5, 3 });
        static readonly Pen PEnv = Caneta(TemaPlant.B(TemaPlant.CorDeformadaEnv, 160), 1, new double[] { 3, 2 });
        static readonly Pen PGuia = Caneta(TemaPlant.BGuiaSnap, 1, new double[] { 2, 3 }), PMarca = Caneta(TemaPlant.BMarcaSnap, 2);
        static readonly Pen PJanela = Caneta(TemaPlant.BJanelaSel, 1), PCruz = Caneta(TemaPlant.BCruzSel, 1, new double[] { 4, 3 });
        static readonly Brush BJanela = TemaPlant.BJanelaSelArea, BCruz = TemaPlant.BCruzSelArea;
        Brush BTexto = TemaPlant.BTexto1, BFundoTexto = TemaPlant.BFundoTextoCanvas;
        static readonly Brush BCaixa = TemaPlant.BCaixaHud;
        static readonly Brush BMedida = TemaPlant.BAlerta, BFundoMedida = TemaPlant.BFundoMedida, BTextoMedida = TemaPlant.BTexto1;
        static readonly Pen PMedida = Caneta(BMedida, 1.8, new double[] { 5, 3 }), PMedidaPonto = Caneta(BMedida, 1.8), PBordaMedida = Caneta(BMedida, 1);

        // Relatorio: fundo branco, sem grade, barras e textos escuros.
        bool impressao;
        public bool Impressao
        {
            get { return impressao; }
            set
            {
                impressao = value;
                if (!value) return;
                Fundo = Brushes.White; BBarra = TemaPlant.BImpTinta; PBarra = Caneta(BBarra, 2.2);
                BTexto = TemaPlant.BImpTinta; BFundoTexto = TemaPlant.BImpFundoTexto;
                PApoio = Caneta(TemaPlant.BImpApoio, 1.6); PCarga = Caneta(TemaPlant.BImpCarga, 1.6); PReac = Caneta(TemaPlant.BImpVerde, 1.6);
                PDef = Caneta(TemaPlant.BImpDef, 2.4);
                PFantasma = Caneta(TemaPlant.B(TemaPlant.C("#94A3B8"), 0xB0), 1.2, new double[] { 4, 3 });
            }
        }

        // Envoltoria: todas as combinacoes de cada barra/no (null = um resultado so, em b.Res / n.Res)
        public Dictionary<Barra, List<ResBarra>> Envoltoria;
        public Dictionary<No, List<double[]>> EnvNos;
        public List<string> NomesEnv;
        List<ResBarra> Lista(Barra b)
        {
            if (Envoltoria != null && Envoltoria.ContainsKey(b)) return Envoltoria[b];
            return b.Res != null ? new List<ResBarra> { b.Res } : new List<ResBarra>();
        }
        // cores por diagrama (0 = N, 1 = V, 2 = M): positivo e negativo (tokens do TemaPlant)
        static readonly Color[] CorPos = { TemaPlant.CorNormalPos, TemaPlant.CorCortante, TemaPlant.CorMomento };
        static readonly Color[] CorNeg = { TemaPlant.CorNormalNeg, TemaPlant.CorCortanteNeg, TemaPlant.CorMomentoNeg };
        static readonly Pen[] PPosD = { Caneta(TemaPlant.B(CorPos[0]), 1.8), Caneta(TemaPlant.B(CorPos[1]), 1.8), Caneta(TemaPlant.B(CorPos[2]), 1.8) };
        static readonly Pen[] PNegD = { Caneta(TemaPlant.B(CorNeg[0]), 1.8), Caneta(TemaPlant.B(CorNeg[1]), 1.8), Caneta(TemaPlant.B(CorNeg[2]), 1.8) };
        static readonly Pen[] PFechoPos = { Caneta(TemaPlant.B(CorPos[0], 0x80), 1), Caneta(TemaPlant.B(CorPos[1], 0x80), 1), Caneta(TemaPlant.B(CorPos[2], 0x80), 1) };
        static readonly Pen[] PFechoNeg = { Caneta(TemaPlant.B(CorNeg[0], 0x80), 1), Caneta(TemaPlant.B(CorNeg[1], 0x80), 1), Caneta(TemaPlant.B(CorNeg[2], 0x80), 1) };
        static readonly Pen POk = Caneta(TemaPlant.BSucesso, 5), PAtencao = Caneta(TemaPlant.BAlerta, 5), PFalha = Caneta(TemaPlant.BErro, 5), PSemVer = Caneta(TemaPlant.BTexto3, 4);
        // mapa de calor: verde <= 75%, ciano 75-90%, ambar 90-100%, vermelho > 100%
        static readonly Pen[] PCalor = { Caneta(TemaPlant.B(TemaPlant.CorCalor1), 6), Caneta(TemaPlant.B(TemaPlant.CorCalor2), 6), Caneta(TemaPlant.B(TemaPlant.CorCalor3), 6), Caneta(TemaPlant.B(TemaPlant.CorCalor4), 6) };
        internal static int FaixaCalor(double ap) { return ap > 1 ? 3 : ap > 0.9 ? 2 : ap > 0.75 ? 1 : 0; }
        static readonly Typeface Fonte = new Typeface("Segoe UI"), FonteNum = new Typeface("Cascadia Mono, Consolas, Segoe UI");

        public Vista()
        {
            Focusable = true; ClipToBounds = true; Cursor = Cursors.Arrow;
            // enquadra sozinho enquanto o usuario nao mexer no zoom (abrir, calcular, redimensionar o painel)
            SizeChanged += (s, e) => { if (!zoomUsuario && !impressao && ActualWidth > 10 && ActualHeight > 10) ZoomTudo(); };
        }

        // Enquadra se o usuario ainda nao mexeu no zoom (usado ao abrir e ao calcular).
        public void EnquadrarLivre() { if (!zoomUsuario) ZoomTudo(); }

        public Point Tela(double x, double y) { return new Point(ox + x * zoom, oy - y * zoom); }
        public Point Mundo(Point p) { return new Point((p.X - ox) / zoom, (oy - p.Y) / zoom); }
        public double TolMundo { get { return 8 / zoom; } }
        public Point PontoMouse { get { return mouseMundo; } }

        // ---- entrada dinamica (caixinhas Comprimento/Angulo junto ao cursor, como no AutoCAD) ----
        // Din[0] = comprimento digitado (m), Din[1] = angulo digitado (graus). Texto nao vazio = valor travado.
        public readonly string[] Din = { "", "" };
        public int DinCampo;
        public bool DinAtivo { get { return DinBase != null; } }
        public Point? DinBase { get { return Ferr == Ferramenta.Barra ? InicioBarra : Ferr == Ferramenta.Medir && MedidaFim == null ? MedidaIni : null; } }
        public bool DinTravado { get { return DinAtivo && (Din[0] != "" || Din[1] != ""); } }
        public void DinLimpar() { Din[0] = ""; Din[1] = ""; DinCampo = 0; }
        static bool DinLer(string t, out double v) { v = 0; return t != "" && t != "-" && Num.Ler(t, out v) && double.IsFinite(v); }

        // Ponto final considerando os valores travados; o que nao esta travado vem do mouse (que ja respeita snap/orto).
        public Point PontoDinamico()
        {
            Point a = DinBase ?? mouseMundo;
            double dx = mouseMundo.X - a.X, dy = mouseMundo.Y - a.Y, dm = Math.Sqrt(dx * dx + dy * dy);
            double angM = dm < 1e-9 ? 0 : Math.Atan2(dy, dx);
            double l, g;
            bool tl = DinLer(Din[0], out l), tg = DinLer(Din[1], out g);
            if (!tl && !tg) return mouseMundo;
            double ang = tg ? g * Math.PI / 180 : angM;
            double len = tl ? l : dm * Math.Max(0, Math.Cos(angM - ang));
            if (tl && l < 0) { len = -l; ang += Math.PI; }
            return new Point(Math.Round(a.X + len * Math.Cos(ang), 9), Math.Round(a.Y + len * Math.Sin(ang), 9));
        }

        // Teclas da entrada dinamica. Devolve true se consumiu.
        public bool DinTecla(string texto, Key tecla)
        {
            if (!DinAtivo) return false;
            if (tecla == Key.Tab) { DinCampo = 1 - DinCampo; InvalidateVisual(); return true; }
            if (tecla == Key.Back) { string t = Din[DinCampo]; if (t.Length > 0) { Din[DinCampo] = t.Substring(0, t.Length - 1); InvalidateVisual(); return true; } return false; }
            if (!string.IsNullOrEmpty(texto))
            {
                char c = texto[0];
                if (char.IsDigit(c) || c == '.' || (c == '-' && Din[DinCampo] == ""))
                {
                    if (c == '.' && Din[DinCampo].Contains(".")) return true;
                    if (Din[DinCampo].Length < 9) Din[DinCampo] += c;
                    InvalidateVisual(); return true;
                }
            }
            return false;
        }
        public Point MouseTela { get { return mouseTela; } }
        public bool Ocupado { get { return arrastando != null || janelaIni != null; } }

        // ================= mouse =================

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            Point p = e.GetPosition(this), w = Mundo(p);
            zoom = Math.Max(0.5, Math.Min(20000, zoom * (e.Delta > 0 ? 1.2 : 1 / 1.2)));
            ox = p.X - w.X * zoom; oy = p.Y + w.Y * zoom;
            zoomUsuario = true;
            InvalidateVisual();
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            Focus();
            Point p = e.GetPosition(this);
            if (e.ChangedButton == MouseButton.Middle && e.ClickCount == 2) { ZoomTudo(); return; }
            if (e.ChangedButton == MouseButton.Middle || e.ChangedButton == MouseButton.Right)
            {
                panIni = p; panOrig = new Point(ox, oy); panMoveu = false;
                CaptureMouse();
                return;
            }
            if (e.ChangedButton != MouseButton.Left) return;
            if (e.ClickCount == 2 && Ferr == Ferramenta.Selecionar) { object o = Acertar(p); if (o != null && DuploClique != null) DuploClique(o); return; }

            if (Ferr != Ferramenta.Selecionar)
            {
                Point w = DinTravado ? PontoDinamico() : Atrair(p);
                if (Clique != null) Clique(w.X, w.Y);
                return;
            }
            object alvo = Acertar(p);
            bool somar = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) || Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            // alca: no ja selecionado pode ser arrastado
            if (alvo is No n && Sel.Contains(n) && !somar)
            {
                arrastando = n; arrastoIni = new Point(n.X, n.Y); arrastoMoveu = false;
                CaptureMouse();
                return;
            }
            if (alvo == null)
            {
                janelaIni = p; CaptureMouse();
                if (!somar) { Sel.Clear(); Avisar(); }
                return;
            }
            if (somar)
            {
                if (Sel.Count > 0 && Sel[0].GetType() != alvo.GetType()) Sel.Clear();
                if (Sel.Contains(alvo)) Sel.Remove(alvo); else Sel.Add(alvo);
            }
            else { Sel.Clear(); Sel.Add(alvo); }
            Avisar();
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            Point p = e.GetPosition(this);
            if (panIni != null && (e.ChangedButton == MouseButton.Middle || e.ChangedButton == MouseButton.Right))
            {
                panIni = null; ReleaseMouseCapture();
                if (e.ChangedButton == MouseButton.Right && !panMoveu)
                {
                    if (Ferr != Ferramenta.Selecionar || InicioBarra != null || MedidaIni != null) { if (FimComando != null) FimComando(); }
                    else if (MenuContexto != null)
                    {
                        object o = Acertar(p);
                        if (o != null && !Sel.Contains(o)) { Sel.Clear(); Sel.Add(o); Avisar(); }
                        MenuContexto(o, p);
                    }
                }
                return;
            }
            if (arrastando != null)
            {
                No n = arrastando; arrastando = null; ReleaseMouseCapture();
                if (arrastoMoveu && NoArrastado != null) NoArrastado(n, arrastoIni, new Point(n.X, n.Y));
                return;
            }
            if (janelaIni != null)
            {
                Point a = janelaIni.Value; janelaIni = null; ReleaseMouseCapture();
                if ((p - a).Length > 4) SelecionarJanela(a, p, Keyboard.Modifiers != ModifierKeys.None);
                InvalidateVisual();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            Point p = e.GetPosition(this);
            mouseTela = p; mouseDentro = true;
            if (panIni != null)
            {
                if ((p - panIni.Value).Length > 3) { panMoveu = true; zoomUsuario = true; }
                ox =panOrig.X + p.X - panIni.Value.X; oy = panOrig.Y + p.Y - panIni.Value.Y;
            }
            if (arrastando != null)
            {
                Point w = Atrair(p, arrastando);
                if (Math.Abs(w.X - arrastando.X) + Math.Abs(w.Y - arrastando.Y) > 1e-12) arrastoMoveu = true;
                arrastando.X = Math.Round(w.X, 9); arrastando.Y = Math.Round(w.Y, 9);
            }
            mouseMundo = Ferr != Ferramenta.Selecionar ? Atrair(p) : Mundo(p);
            if (Ferr == Ferramenta.Selecionar && arrastando == null) atracao = Atracao.Nenhuma;
            hover = janelaIni == null && arrastando == null ? Acertar(p) : null;
            Leitura = LerDiagrama(p);
            if (MouseMundo != null) MouseMundo(mouseMundo);
            InvalidateVisual();
        }

        protected override void OnMouseLeave(MouseEventArgs e) { mouseDentro = false; hover = null; Leitura = null; insp = null; InvalidateVisual(); }

        // Posiciona o mouse por codigo (capturas de tela e testes): mesmo efeito de mover o cursor sobre a vista.
        public void SimularMouse(Point? p)
        {
            if (p == null) { OnMouseLeave(null); return; }
            mouseTela = p.Value; mouseDentro = true;
            mouseMundo = Mundo(p.Value);
            hover = Acertar(p.Value);
            Leitura = LerDiagrama(p.Value);
            InvalidateVisual();
        }

        void Avisar() { if (SelecaoMudou != null) SelecaoMudou(); Anim.Valor(this, RealceProperty, 0, 1, Anim.Rapido); InvalidateVisual(); }

        public void Selecionar(IEnumerable<object> objs)
        {
            Sel.Clear(); Sel.AddRange(objs); Avisar();
        }

        void SelecionarJanela(Point a, Point b, bool somar)
        {
            bool cruzando = b.X < a.X;
            Rect r = new Rect(a, b);
            List<object> barras = new List<object>(), nos = new List<object>();
            foreach (Barra br in M.Barras)
            {
                No n1 = M.NoPorId(br.N1), n2 = M.NoPorId(br.N2);
                Point p1 = Tela(n1.X, n1.Y), p2 = Tela(n2.X, n2.Y);
                bool dentro = r.Contains(p1) && r.Contains(p2);
                if (dentro || (cruzando && CruzaRet(p1, p2, r))) barras.Add(br);
            }
            foreach (No n in M.Nos) if (r.Contains(Tela(n.X, n.Y))) nos.Add(n);
            List<object> novos = barras.Count > 0 ? barras : nos;
            if (!somar || (Sel.Count > 0 && novos.Count > 0 && Sel[0].GetType() != novos[0].GetType())) Sel.Clear();
            foreach (object o in novos) if (!Sel.Contains(o)) Sel.Add(o);
            Avisar();
        }

        static bool CruzaRet(Point a, Point b, Rect r)
        {
            if (r.Contains(a) || r.Contains(b)) return true;
            Point[] c = { r.TopLeft, r.TopRight, r.BottomRight, r.BottomLeft };
            for (int i = 0; i < 4; i++) if (Cruza(a, b, c[i], c[(i + 1) % 4])) return true;
            return false;
        }

        static bool Cruza(Point p1, Point p2, Point p3, Point p4)
        {
            double d1 = Vector.CrossProduct(p4 - p3, p1 - p3), d2 = Vector.CrossProduct(p4 - p3, p2 - p3);
            double d3 = Vector.CrossProduct(p2 - p1, p3 - p1), d4 = Vector.CrossProduct(p2 - p1, p4 - p1);
            return d1 * d2 <= 0 && d3 * d4 <= 0;   // inclui encostar na borda/canto da caixa
        }

        // ================= atracao =================

        public Point Atrair(Point tela, No ignorar = null)
        {
            atracao = Atracao.Nenhuma; guiaDe = null; guiasRastreio.Clear();
            Point w = Mundo(tela);
            Point? baseP = Ferr == Ferramenta.Barra ? InicioBarra : Ferr == Ferramenta.Medir && MedidaFim == null ? MedidaIni : (ignorar != null ? (Point?)arrastoIni : null);
            if (!AtrairObjetos && Ferr == Ferramenta.Medir)
            {
                No pertoNo = M.Nos.Where(n => n != ignorar).OrderBy(n => (Tela(n.X, n.Y) - tela).Length).FirstOrDefault();
                if (pertoNo != null && (Tela(pertoNo.X, pertoNo.Y) - tela).Length < 10) { atracao = Atracao.No; return new Point(pertoNo.X, pertoNo.Y); }
            }
            if (AtrairObjetos)
            {
                var barras = M.Barras.Select(b => new { b, a = M.NoPorId(b.N1), c = M.NoPorId(b.N2) }).Where(x => x.a != ignorar && x.c != ignorar).ToList();
                // 1) extremidade (no)
                No perto = M.Nos.Where(n => n != ignorar).OrderBy(n => (Tela(n.X, n.Y) - tela).Length).FirstOrDefault();   // o mais proximo, nao o primeiro
                if ((SnapNo || Ferr == Ferramenta.Medir) && perto != null && (Tela(perto.X, perto.Y) - tela).Length < 10)
                {
                    Point p = new Point(perto.X, perto.Y);
                    if (Rastrear && !adquiridos.Contains(p)) { adquiridos.Add(p); if (adquiridos.Count > 6) adquiridos.RemoveAt(0); }   // adquire para rastreamento
                    atracao = Atracao.No; return p;
                }
                if ((Tela(0, 0) - tela).Length < 12) { atracao = Atracao.Origem; return new Point(0, 0); }   // origem (0,0) sempre atrai
                // 2) intersecao entre barras
                if (SnapIntersecao)
                    for (int i = 0; i < barras.Count; i++)
                        for (int j = i + 1; j < barras.Count; j++)
                        {
                            Point? x = Intersecao(new Point(barras[i].a.X, barras[i].a.Y), new Point(barras[i].c.X, barras[i].c.Y), new Point(barras[j].a.X, barras[j].a.Y), new Point(barras[j].c.X, barras[j].c.Y), true);
                            if (x != null && (Tela(x.Value.X, x.Value.Y) - tela).Length < 10 && M.Nos.All(n => Math.Abs(n.X - x.Value.X) + Math.Abs(n.Y - x.Value.Y) > 1e-6))
                            { atracao = Atracao.Intersecao; return x.Value; }
                        }
                // 3) meio
                if (SnapMeio)
                    foreach (var x in barras)
                    {
                        Point meio = new Point((x.a.X + x.c.X) / 2, (x.a.Y + x.c.Y) / 2);
                        if ((Tela(meio.X, meio.Y) - tela).Length < 9) { atracao = Atracao.Meio; return meio; }
                    }
                // 4) perpendicular a uma barra a partir do ponto inicial
                if (SnapPerpendicular && baseP != null)
                    foreach (var x in barras)
                    {
                        Point a = new Point(x.a.X, x.a.Y), c = new Point(x.c.X, x.c.Y);
                        Vector ab = c - a; double l2 = ab.LengthSquared; if (l2 < 1e-12) continue;
                        double t = Vector.Multiply(baseP.Value - a, ab) / l2;
                        if (t < 0 || t > 1) continue;
                        Point pe = a + t * ab;
                        if ((pe - baseP.Value).Length > 1e-6 && (Tela(pe.X, pe.Y) - tela).Length < 10) { atracao = Atracao.Perpendicular; return pe; }
                    }
                // 5) sobre a barra (mais proximo)
                if (SnapProximo)
                    foreach (var x in barras)
                    {
                        Point pa = Tela(x.a.X, x.a.Y), pc = Tela(x.c.X, x.c.Y);
                        Vector ab = pc - pa; double l2 = ab.LengthSquared; if (l2 < 1) continue;
                        double t = Vector.Multiply(tela - pa, ab) / l2;
                        if (t <= 0.02 || t >= 0.98) continue;
                        if ((tela - (pa + t * ab)).Length < 7) { atracao = Atracao.Sobre; return new Point(x.a.X + t * (x.c.X - x.a.X), x.a.Y + t * (x.c.Y - x.a.Y)); }
                    }
                // 6) extensao: prolongamento da barra alem das pontas
                if (SnapExtensao)
                    foreach (var x in barras)
                    {
                        Point pa = Tela(x.a.X, x.a.Y), pc = Tela(x.c.X, x.c.Y);
                        Vector ab = pc - pa; double l2 = ab.LengthSquared; if (l2 < 1) continue;
                        double t = Vector.Multiply(tela - pa, ab) / l2;
                        if (t > -0.02 && t < 1.02) continue;
                        if ((tela - (pa + t * ab)).Length < 6)
                        {
                            Point q = new Point(x.a.X + t * (x.c.X - x.a.X), x.a.Y + t * (x.c.Y - x.a.Y));
                            atracao = Atracao.Extensao; guiaDe = t < 0 ? new Point(x.a.X, x.a.Y) : new Point(x.c.X, x.c.Y); return q;
                        }
                    }
            }
            // 7) rastreamento: alinhado na horizontal/vertical com nos adquiridos (passe o mouse num no) ou com o ponto inicial
            if (Rastrear)
            {
                var pts = new List<Point>(adquiridos);
                if (baseP != null && !pts.Contains(baseP.Value)) pts.Add(baseP.Value);
                Point? h = null, v = null; double dh = 7, dv = 7;
                foreach (Point p in pts)
                {
                    Point t = Tela(p.X, p.Y);
                    double ey = Math.Abs(t.Y - tela.Y), ex = Math.Abs(t.X - tela.X);
                    if (ey < dh && ex > 4) { dh = ey; h = p; }   // mesma altura do ponto
                    if (ex < dv && ey > 4) { dv = ex; v = p; }   // mesma vertical do ponto
                }
                if (h != null || v != null)
                {
                    Point r = new Point(v != null ? v.Value.X : w.X, h != null ? h.Value.Y : w.Y);
                    if (h != null) guiasRastreio.Add(h.Value);
                    if (v != null) guiasRastreio.Add(v.Value);
                    atracao = Atracao.Rastreio; return r;
                }
            }
            if (M.AtrairGrade && M.Grade > 0)
            {
                w = new Point(Math.Round(w.X / M.Grade) * M.Grade, Math.Round(w.Y / M.Grade) * M.Grade);
                atracao = Atracao.Grade;
            }
            if (baseP != null)
            {
                Point a = baseP.Value;
                double dx = w.X - a.X, dy = w.Y - a.Y;
                double ang = Math.Atan2(dy, dx) * 180 / Math.PI;
                bool forcar = Orto || Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
                double resto = ((ang % 90) + 90) % 90; double dist = Math.Min(resto, 90 - resto);
                if (forcar || (Polar && dist < 3.5))
                {
                    if (Math.Abs(dx) >= Math.Abs(dy)) w.Y = a.Y; else w.X = a.X;
                    atracao = Atracao.Polar; guiaDe = a;
                }
            }
            return w;
        }

        public void LimparRastreio() { adquiridos.Clear(); guiasRastreio.Clear(); }

        // intersecao de dois segmentos (ou retas, com soSegmentos = false)
        static Point? Intersecao(Point p1, Point p2, Point p3, Point p4, bool soSegmentos)
        {
            Vector r = p2 - p1, s = p4 - p3; double den = Vector.CrossProduct(r, s);
            if (Math.Abs(den) < 1e-12) return null;
            double t = Vector.CrossProduct(p3 - p1, s) / den, u = Vector.CrossProduct(p3 - p1, r) / den;
            if (soSegmentos && (t < -1e-9 || t > 1 + 1e-9 || u < -1e-9 || u > 1 + 1e-9)) return null;
            return p1 + t * r;
        }

        public object Acertar(Point tela)
        {
            No np = M.Nos.OrderBy(n => (Tela(n.X, n.Y) - tela).Length).FirstOrDefault();
            if (np != null && (Tela(np.X, np.Y) - tela).Length < 8) return np;
            Barra melhor = null; double dmin = 7;
            foreach (Barra b in M.Barras)
            {
                No a = M.NoPorId(b.N1), c = M.NoPorId(b.N2);
                double d = DistSeg(tela, Tela(a.X, a.Y), Tela(c.X, c.Y));
                if (d < dmin) { dmin = d; melhor = b; }
            }
            return melhor;
        }

        public No NoEm(double x, double y) { Point t = Tela(x, y); return M.Nos.FirstOrDefault(n => (Tela(n.X, n.Y) - t).Length < 10); }
        public Barra BarraEm(double x, double y) { return Acertar(Tela(x, y)) as Barra; }

        static double DistSeg(Point p, Point a, Point b)
        {
            Vector ab = b - a; double l2 = ab.LengthSquared;
            if (l2 < 1e-9) return (p - a).Length;
            double t = Math.Max(0, Math.Min(1, Vector.Multiply(p - a, ab) / l2));
            return (p - (a + t * ab)).Length;
        }

        // ================= vista =================

        // Enquadra num tamanho dado (figuras do relatorio, fora da tela).
        public void EnquadrarEm(double largura, double altura, double margem)
        {
            if (M.Nos.Count == 0) return;
            double x0 = M.Nos.Min(n => n.X), x1 = M.Nos.Max(n => n.X), y0 = M.Nos.Min(n => n.Y), y1 = M.Nos.Max(n => n.Y);
            zoom = Math.Min((largura - margem) / Math.Max(x1 - x0, 1e-3), (altura - margem) / Math.Max(y1 - y0, 1e-3));
            zoom = Math.Max(0.5, Math.Min(20000, zoom));
            ox = largura / 2 - (x0 + x1) / 2 * zoom; oy = altura / 2 + (y0 + y1) / 2 * zoom;
            InvalidateVisual();
        }

        public void ZoomTudo()
        {
            zoomUsuario = false;
            if (M.Nos.Count == 0 || ActualWidth < 10) { zoom = 60; ox = ActualWidth / 2 - 5 * zoom; oy = ActualHeight / 2 + 3 * zoom; InvalidateVisual(); return; }
            double x0 = M.Nos.Min(n => n.X), x1 = M.Nos.Max(n => n.X), y0 = M.Nos.Min(n => n.Y), y1 = M.Nos.Max(n => n.Y);
            double w = Math.Max(x1 - x0, 1e-3), h = Math.Max(y1 - y0, 1e-3);
            // margens em pixels: sobra espaco em cima (HUD, cargas), embaixo (apoios e reacoes) e nas laterais (valores)
            double mx = impressao ? 40 : 80, mt = impressao ? 40 : Math.Min(64, ActualHeight * 0.16), mb = impressao ? 40 : Math.Min(80, ActualHeight * 0.18);
            double aw = Math.Max(40, ActualWidth - 2 * mx), ah = Math.Max(40, ActualHeight - mt - mb);
            zoom = Math.Min(aw / w, ah / h);
            if (x1 - x0 < 1e-3 && y1 - y0 < 1e-3) zoom = 60;
            zoom = Math.Max(0.5, Math.Min(20000, Math.Min(zoom, 320)));
            ox = mx + aw / 2 - (x0 + x1) / 2 * zoom; oy = mt + ah / 2 + (y0 + y1) / 2 * zoom;
            InvalidateVisual();
        }

        // Centraliza e enquadra os pontos (selecao por nome na paleta): aproxima ate no maximo a escala de ZoomTudo.
        public void EnquadrarPontos(IList<Point> pts)
        {
            if (pts == null || pts.Count == 0 || ActualWidth < 10) return;
            double x0 = pts.Min(p => p.X), x1 = pts.Max(p => p.X), y0 = pts.Min(p => p.Y), y1 = pts.Max(p => p.Y);
            double w = x1 - x0, h = y1 - y0;
            if (w < 1e-3 && h < 1e-3) { zoom = Math.Max(zoom, 80); }
            else zoom = Math.Max(0.5, Math.Min(Math.Min(320, 20000), Math.Min((ActualWidth - 260) / Math.Max(w, 1e-3), (ActualHeight - 200) / Math.Max(h, 1e-3))));
            zoomUsuario = true;
            ox = ActualWidth / 2 - (x0 + x1) / 2 * zoom; oy = ActualHeight / 2 + (y0 + y1) / 2 * zoom;
            InvalidateVisual();
        }

        public void ZoomFator(double f)
        {
            zoomUsuario = true;
            Point c = new Point(ActualWidth / 2, ActualHeight / 2), w = Mundo(c);
            zoom = Math.Max(0.5, Math.Min(20000, zoom * f));
            ox = c.X - w.X * zoom; oy = c.Y + w.Y * zoom;
            InvalidateVisual();
        }

        public void Garantir(Point mundo)
        {
            Point t = Tela(mundo.X, mundo.Y);
            if (t.X < 20 || t.Y < 20 || t.X > ActualWidth - 20 || t.Y > ActualHeight - 20) ZoomTudo();
        }

        // ================= desenho =================

        double Dip { get { return VisualTreeHelper.GetDpi(this).PixelsPerDip; } }

        FormattedText Ft(string s, Brush b, double tam) { return new FormattedText(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Fonte, tam, b, Dip); }

        void Texto(DrawingContext dc, string s, Point p, Brush b, double tam = 11, bool centro = true)
        {
            var ft = Ft(s, b, tam);
            Point q = centro ? new Point(p.X - ft.Width / 2, p.Y - ft.Height / 2) : p;
            dc.DrawRectangle(BFundoTexto, null, new Rect(q.X - 2, q.Y, ft.Width + 4, ft.Height));
            dc.DrawText(ft, q);
        }

        void Caixa(DrawingContext dc, string s, Point p, Brush fundo)
        {
            var ft = Ft(s, TemaPlant.BTexto1, 11);
            Rect r = new Rect(p.X, p.Y, ft.Width + 12, ft.Height + 6);
            if (r.Right > ActualWidth - 4) r.X = ActualWidth - 4 - r.Width;
            if (r.Bottom > ActualHeight - 4) r.Y = p.Y - r.Height - 30;
            dc.DrawRoundedRectangle(fundo, null, r, 3, 3);
            dc.DrawText(ft, new Point(r.X + 6, r.Y + 3));
        }

        FormattedText FtEtiqueta()
        {
            var fe = Ft(Etiqueta, TemaPlant.BTexto1, 11);
            fe.MaxTextWidth = Math.Max(60, ActualWidth - 34); fe.MaxLineCount = 1; fe.Trimming = TextTrimming.CharacterEllipsis;
            return fe;
        }

        // ---------- rotulos em pilula com anti-sobreposicao ----------

        static Vector Gira(Vector v, double graus)
        {
            double a = graus * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
            return new Vector(v.X * c - v.Y * s, v.X * s + v.Y * c);
        }

        static Rect Aabb(Point c, double w, double h, double graus)
        {
            double a = graus * Math.PI / 180, cs = Math.Abs(Math.Cos(a)), sn = Math.Abs(Math.Sin(a));
            double bw = w * cs + h * sn, bh = w * sn + h * cs;
            return new Rect(c.X - bw / 2, c.Y - bh / 2, bw, bh);
        }

        static Pen BordaPilula(Brush cor)
        {
            Pen p;
            if (bordasPilula.TryGetValue(cor, out p)) return p;
            var b = (Brush)cor.CloneCurrentValue(); b.Opacity = 0.55; b.Freeze();
            p = new Pen(b, 1); p.Freeze();
            bordasPilula[cor] = p;
            return p;
        }

        // Pede um rotulo: sera desenhado no fim, perto de "p" (com "pref" como deslocamento preferido), sem cobrir outros.
        // prio menor = mais importante; peso maior vence dentro da mesma prioridade; forcar = nao omitir.
        void Rotulo(string t, Point p, Brush cor, Vector pref, int prio, double peso = 0, bool forcar = false)
        {
            rotulos.Add(new Rot { T = t, P = p, Cor = cor, Pref = pref.Length < 1e-6 ? new Vector(0, -14) : pref, Prio = prio, Peso = peso, Forcar = forcar });
        }

        void DescarregarRotulos(DrawingContext dc)
        {
            if (rotulos.Count == 0) return;
            var colocados = new List<Rect>(ocupados);
            if (!impressao && ReservaHud.Width > 0) colocados.Add(ReservaHud);
            foreach (No n in M.Nos)
            {
                Point p = Tela(n.X, n.Y);
                colocados.Add(n.TemApoio ? new Rect(p.X - 16, p.Y - 8, 32, 34) : new Rect(p.X - 4, p.Y - 4, 8, 8));
            }
            var segs = M.Barras.Select(b => { No a = M.NoPorId(b.N1), c = M.NoPorId(b.N2); return Tuple.Create(Tela(a.X, a.Y), Tela(c.X, c.Y)); }).ToList();
            Rect area = new Rect(4, 4, Math.Max(10, ActualWidth - 8), Math.Max(10, ActualHeight - 8));
            double[] angulos = { 0, 35, -35, 70, -70, 105, -105, 140, -140, 180 };
            double[] mult = { 1, 1.5, 2.1, 2.9, 3.9, 5.2 };
            // area coberta (px²) de um retangulo sobre o que ja esta ocupado: usada se nenhum lugar livre servir
            Func<Rect, double> cobertura = rc =>
            {
                double s = 0; Rect infl = Rect.Inflate(rc, 2, 2);
                foreach (Rect o in colocados) { Rect i = Rect.Intersect(o, infl); if (!i.IsEmpty) s += i.Width * i.Height; }
                foreach (var sg in segs) if (CruzaRet(sg.Item1, sg.Item2, Rect.Inflate(rc, 3, 3))) s += 400;
                return s;
            };
            foreach (Rot r in rotulos.OrderBy(x => x.Prio).ThenByDescending(x => x.Peso))
            {
                FormattedText ft = Ft(r.T, r.Cor, 10.5);
                double w = ft.Width + 10, h = ft.Height + 3, pl = r.Pref.Length;
                Rect? achou = null, primeiro = null; double melhorCob = double.MaxValue;
                foreach (double m in mult)
                {
                    foreach (double a in angulos)
                    {
                        Point c = r.P + Gira(r.Pref, a) * m;
                        Rect rc = new Rect(c.X - w / 2, c.Y - h / 2, w, h);
                        if (!area.Contains(rc)) continue;
                        if (r.Forcar) { double cob = cobertura(rc) + (m - 1) * 30; if (cob < melhorCob) { melhorCob = cob; primeiro = rc; } }
                        else if (primeiro == null) primeiro = rc;
                        Rect infl = Rect.Inflate(rc, 2, 2);
                        if (colocados.Any(o => o.IntersectsWith(infl))) continue;
                        Rect inflB = Rect.Inflate(rc, 3, 3);
                        if (segs.Any(sg => CruzaRet(sg.Item1, sg.Item2, inflB))) continue;
                        achou = rc; break;
                    }
                    if (achou != null) break;
                }
                if (achou == null && r.Forcar)
                {
                    Rect fx = primeiro ?? new Rect(Math.Max(4, Math.Min(ActualWidth - w - 4, r.P.X + r.Pref.X - w / 2)), Math.Max(4, Math.Min(ActualHeight - h - 4, r.P.Y + r.Pref.Y - h / 2)), w, h);
                    achou = fx;
                }
                if (achou == null) continue;   // sem lugar: omite (o valor continua na tabela e no cartao do mouse)
                Rect f = achou.Value;
                Point centro = new Point(f.X + f.Width / 2, f.Y + f.Height / 2);
                if ((centro - r.P).Length > pl * 1.25 + 8)   // deslocado: fio fino ate o ponto de origem
                    dc.DrawLine(BordaPilula(r.Cor), r.P, new Point(Math.Max(f.Left, Math.Min(f.Right, r.P.X)), Math.Max(f.Top, Math.Min(f.Bottom, r.P.Y))));
                dc.DrawRoundedRectangle(BFundoTexto, BordaPilula(r.Cor), f, 3, 3);
                dc.DrawText(ft, new Point(f.X + 5, f.Y + 1.5));
                colocados.Add(f);
            }
            rotulos.Clear();
        }

        protected override void OnRender(DrawingContext dc)
        {
            rotulos.Clear(); ocupados.Clear();
            dc.DrawRectangle(Fundo, null, new Rect(0, 0, ActualWidth, ActualHeight));
            if (M.MostrarGrade && !impressao) DesenharGrade(dc);
            Point o = Tela(0, 0);
            if (!impressao) { dc.DrawLine(EixoX, o, new Point(o.X + 40, o.Y)); dc.DrawLine(EixoY, o, new Point(o.X, o.Y - 40)); }
            if (!impressao) { Texto(dc, "X", new Point(o.X + 48, o.Y), EixoX.Brush, 10); Texto(dc, "Y", new Point(o.X, o.Y - 48), EixoY.Brush, 10); }

            if (Resolvido && Diag != Diagrama.Nenhum && Diag != Diagrama.Verificacao) DesenharDiagrama(dc);

            // barra continua: os outros trechos acendem junto com o trecho selecionado ou sob o mouse
            var membrosAcesos = new HashSet<int>(Sel.OfType<Barra>().Select(x => x.Membro));
            if (hover is Barra hb) membrosAcesos.Add(hb.Membro);
            membrosAcesos.Remove(0);
            foreach (Barra b in M.Barras)
            {
                No a = M.NoPorId(b.N1), c = M.NoPorId(b.N2);
                Point pa = Tela(a.X, a.Y), pc = Tela(c.X, c.Y);
                bool calor = MapaCalor && Diag == Diagrama.Verificacao && Resolvido && Verificar != null;
                if (Sel.Contains(b)) { dc.PushOpacity((double)GetValue(RealceProperty)); dc.DrawLine(calor ? PSelCalor : PSel, pa, pc); dc.Pop(); }
                else if (hover == b || membrosAcesos.Contains(b.Membro)) dc.DrawLine(calor ? PHoverCalor : PHover, pa, pc);
                Pen pb = PBarra;
                Vector u = pc - pa; if (u.Length > 1e-6) u.Normalize();
                if (Diag == Diagrama.Deformada && Resolvido) pb = PFantasma;   // forma original: so um fantasma tracejado
                if (Diag == Diagrama.Verificacao && Resolvido && Verificar != null)
                {
                    Verif v = Verificar(b);
                    if (!v.Aplicavel) pb = PSemVer;
                    else
                    {
                        pb = calor ? PCalor[FaixaCalor(v.Ap)] : v.Ap > 1 ? PFalha : v.Ap > 0.9 ? PAtencao : POk;
                        Vector cima = new Vector(u.Y, -u.X); if (cima.Y > 0) cima = -cima;
                        string selo = v.Ap > 1 ? "✕ " : v.Ap > 0.9 ? "⚠ " : "";   // codificacao dupla: cor + simbolo
                        Rotulo(selo + (v.Ap * 100).ToString("0") + "%", pa + (pc - pa) / 2, pb.Brush, cima * 15, 1, v.Ap, v.Ap > 0.9);
                    }
                }
                dc.DrawLine(pb, pa, pc);
                if (b.RotulaInicio) dc.DrawEllipse(Fundo, PBarra, pa + u * 8, 4.5, 4.5);
                if (b.RotulaFim) dc.DrawEllipse(Fundo, PBarra, pc - u * 8, 4.5, 4.5);
                if (MostrarCargas) CargaBarra(dc, b, pa, pc);
                if (MostrarNomes && (pc - pa).Length > 90 && !(Resolvido && (Diag == Diagrama.Normal || Diag == Diagrama.Cortante || Diag == Diagrama.Momento)))
                {
                    // texto paralelo a barra, sempre legivel (da esquerda para a direita / de baixo para cima)
                    Vector d = u; if (d.X < -1e-9 || (Math.Abs(d.X) <= 1e-9 && d.Y > 0)) d = -d;
                    double ang = Math.Atan2(d.Y, d.X) * 180 / Math.PI;
                    Point meio = pa + (pc - pa) / 2 + new Vector(d.Y, -d.X) * 11;   // do lado de cima / esquerdo da barra
                    string nomeB = (b.Membro != 0 ? "B" + b.Membro + " · " : "") + b.Secao + " · " + Num.Fmt(M.Comp(b), 2) + " m";
                    dc.PushTransform(new RotateTransform(ang, meio.X, meio.Y));
                    Texto(dc, nomeB, meio, BTexto, 10);
                    dc.Pop();
                    // reserva o espaco do texto (em pedacos ao longo da barra) para os rotulos nao taparem
                    double wn = Ft(nomeB, BTexto, 10).Width + 6; int nn = Math.Max(1, (int)Math.Ceiling(wn / 24));
                    for (int i = 0; i < nn; i++) ocupados.Add(Aabb(meio + d * ((i + 0.5) * wn / nn - wn / 2), wn / nn, 14, ang));
                }
            }
            foreach (No n in M.Nos)
            {
                Point p = Tela(n.X, n.Y);
                Apoio(dc, n, p);
                if (n.Placa != null) { dc.DrawLine(PApoio, new Point(p.X - 12, p.Y + 2), new Point(p.X + 12, p.Y + 2)); }
                bool sel = Sel.Contains(n);
                if (sel) dc.DrawRectangle(TemaPlant.BBarraSel, null, new Rect(p.X - 5, p.Y - 5, 10, 10));   // alca
                else if (hover == n) dc.DrawEllipse(null, PHover, p, 7, 7);
                dc.DrawRectangle(BBarra, null, new Rect(p.X - 2.5, p.Y - 2.5, 5, 5));
                if (MostrarCargas) CargaNo(dc, n, p);
                if (Resolvido && MostrarReacoes && n.Res != null && n.TemApoio) Reacoes(dc, n, p);
            }
            if (!string.IsNullOrEmpty(Etiqueta))   // reserva o lugar da etiqueta do comando (embaixo, a esquerda)
            {
                var fe = FtEtiqueta();
                ocupados.Add(new Rect(10, ActualHeight - fe.Height - 16 - 6, Math.Min(ActualWidth - 20, fe.Width + 12), fe.Height + 12));
            }
            DescarregarRotulos(dc);
            if (Resolvido && Diag != Diagrama.Nenhum && Diag != Diagrama.Verificacao && Ferr == Ferramenta.Selecionar && mouseDentro && insp != null) DesenharInspecao(dc);

            // janela de selecao
            if (janelaIni != null)
            {
                Rect r = new Rect(janelaIni.Value, mouseTela);
                bool cruz = mouseTela.X < janelaIni.Value.X;
                dc.DrawRectangle(cruz ? BCruz : BJanela, cruz ? PCruz : PJanela, r);
            }

            // elastico da barra
            if (Ferr == Ferramenta.Barra && InicioBarra != null && mouseDentro)
            {
                Point pd = PontoDinamico(); Point a = Tela(InicioBarra.Value.X, InicioBarra.Value.Y), b = Tela(pd.X, pd.Y);
                dc.DrawLine(PFant, a, b);
            }
            // ferramenta Medir: linha tracejada amarela #E8B448 + etiqueta (em andamento ou fixa apos o 2o clique)
            if (!impressao && MedidaIni != null && (MedidaFim != null || (Ferr == Ferramenta.Medir && mouseDentro)))
            {
                Point p1 = MedidaIni.Value, p2 = MedidaFim ?? PontoDinamico();
                Point a = Tela(p1.X, p1.Y), b = Tela(p2.X, p2.Y);
                dc.DrawLine(PMedida, a, b);
                dc.DrawEllipse(null, PMedidaPonto, a, 4, 4);
                dc.DrawEllipse(null, PMedidaPonto, b, 4, 4);
                string txt = TextoMedida(p1, p2);
                var ft = Ft(txt, BTextoMedida, 11);
                Point pos = MedidaFim == null ? new Point(mouseTela.X + 18, mouseTela.Y + 22)
                                              : new Point((a.X + b.X) / 2 - (ft.Width + 14) / 2, (a.Y + b.Y) / 2 - ft.Height - 16);
                Rect rm = new Rect(pos.X, pos.Y, ft.Width + 14, ft.Height + 6);
                if (rm.X < 4) rm.X = 4;
                if (rm.Right > ActualWidth - 4) rm.X = ActualWidth - 4 - rm.Width;
                if (rm.Y < 4) rm.Y = 4;
                if (rm.Bottom > ActualHeight - 4) rm.Y = ActualHeight - 4 - rm.Height;
                dc.DrawRoundedRectangle(BFundoMedida, PBordaMedida, rm, 3, 3);
                dc.DrawText(ft, new Point(rm.X + 7, rm.Y + 3));
            }
            // guia polar e marcador de atracao
            if (mouseDentro && (Ferr != Ferramenta.Selecionar || arrastando != null))
            {
                Point c = Tela(mouseMundo.X, mouseMundo.Y);
                if (guiaDe != null)
                {
                    Point g = Tela(guiaDe.Value.X, guiaDe.Value.Y);
                    Vector d = c - g; if (d.Length > 1) { d.Normalize(); dc.DrawLine(PGuia, g - d * 2000, g + d * 4000); }
                }
                // linhas de rastreamento: pontilhadas do ponto adquirido ate o cursor, com o "+" no ponto
                foreach (Point gp in guiasRastreio)
                {
                    Point g = Tela(gp.X, gp.Y);
                    dc.DrawLine(PGuia, g, c);
                    dc.DrawLine(PMarca, new Point(g.X - 4, g.Y), new Point(g.X + 4, g.Y)); dc.DrawLine(PMarca, new Point(g.X, g.Y - 4), new Point(g.X, g.Y + 4));
                }
                foreach (Point ap in adquiridos)   // pontos adquiridos: cruzinha pequena, como no AutoCAD
                {
                    Point g = Tela(ap.X, ap.Y);
                    dc.DrawLine(PGuia, new Point(g.X - 3, g.Y), new Point(g.X + 3, g.Y)); dc.DrawLine(PGuia, new Point(g.X, g.Y - 3), new Point(g.X, g.Y + 3));
                }
                string nome = null;
                switch (atracao)
                {
                    case Atracao.Intersecao: dc.DrawLine(PMarca, new Point(c.X - 6, c.Y - 6), new Point(c.X + 6, c.Y + 6)); dc.DrawLine(PMarca, new Point(c.X - 6, c.Y + 6), new Point(c.X + 6, c.Y - 6)); nome = "Interseção"; break;
                    case Atracao.Perpendicular: dc.DrawLine(PMarca, new Point(c.X - 6, c.Y + 6), new Point(c.X + 6, c.Y + 6)); dc.DrawLine(PMarca, new Point(c.X - 6, c.Y + 6), new Point(c.X - 6, c.Y - 6)); dc.DrawLine(PMarca, new Point(c.X - 6, c.Y), new Point(c.X, c.Y)); dc.DrawLine(PMarca, new Point(c.X, c.Y), new Point(c.X, c.Y + 6)); nome = "Perpendicular"; break;
                    case Atracao.Extensao: nome = "Extensão"; break;
                    case Atracao.Rastreio:
                        nome = guiasRastreio.Count == 2 ? "Rastreio: interseção" : "Rastreio";
                        dc.DrawLine(PMarca, new Point(c.X - 5, c.Y - 5), new Point(c.X + 5, c.Y + 5)); dc.DrawLine(PMarca, new Point(c.X - 5, c.Y + 5), new Point(c.X + 5, c.Y - 5)); break;
                    case Atracao.No: dc.DrawRectangle(null, PMarca, new Rect(c.X - 6, c.Y - 6, 12, 12)); nome = "Nó"; break;
                    case Atracao.Meio:
                        var tri = new StreamGeometry();
                        using (var k = tri.Open()) { k.BeginFigure(new Point(c.X, c.Y - 7), false, true); k.LineTo(new Point(c.X - 7, c.Y + 5), true, false); k.LineTo(new Point(c.X + 7, c.Y + 5), true, false); }
                        dc.DrawGeometry(null, PMarca, tri); nome = "Meio"; break;
                    case Atracao.Origem: dc.DrawRectangle(null, PMarca, new Rect(c.X - 6, c.Y - 6, 12, 12)); dc.DrawEllipse(null, PMarca, c, 3, 3); nome = "Origem (0, 0)"; break;
                case Atracao.Sobre: dc.DrawEllipse(null, PMarca, c, 6, 6); nome = "Sobre a barra"; break;
                    case Atracao.Grade: dc.DrawLine(PMarca, new Point(c.X - 6, c.Y), new Point(c.X + 6, c.Y)); dc.DrawLine(PMarca, new Point(c.X, c.Y - 6), new Point(c.X, c.Y + 6)); nome = "Grade"; break;
                    case Atracao.Polar: nome = "Polar"; break;
                }
                if (nome != null && atracao != Atracao.Polar && Leitura == null) Texto(dc, nome, new Point(c.X + 10, c.Y + 10), PMarca.Brush, 10, false);
            }
            // entrada dinamica: medidas junto ao cursor
            if (mouseDentro && DinAtivo && !impressao) DesenharEntradaDinamica(dc);
            else if (mouseDentro && arrastando != null)
                Caixa(dc, "Nó " + arrastando.Id + ": X " + Num.Fmt(arrastando.X, 3) + "  Y " + Num.Fmt(arrastando.Y, 3), new Point(mouseTela.X + 18, mouseTela.Y + 22), BCaixa);
            else if (mouseDentro && Leitura != null && insp == null)
                Caixa(dc, Leitura, new Point(mouseTela.X + 18, mouseTela.Y + 22), BCaixa);

            // etiqueta do comando ativo: canto inferior esquerdo da vista (a linha de comando fica fora do desenho)
            if (!string.IsNullOrEmpty(Etiqueta) && !impressao)
            {
                var fe = FtEtiqueta();
                Rect re = new Rect(10, ActualHeight - fe.Height - 16 - 6, Math.Min(ActualWidth - 20, fe.Width + 14), fe.Height + 10);
                dc.DrawRoundedRectangle(BCaixa, BordaPilula(TemaPlant.BBordaSutil), re, 4, 4);
                dc.DrawText(fe, new Point(re.X + 7, re.Y + 5));
            }
        }

        // Duas caixinhas encostadas no cursor: Comprimento e Angulo. A ativa tem anel de foco; a travada (digitada) fica em destaque.
        void DesenharEntradaDinamica(DrawingContext dc)
        {
            Point a = DinBase.Value, f = PontoDinamico();
            double dx = f.X - a.X, dy = f.Y - a.Y;
            string[] rot = { "Comprimento", "Ângulo" }, un = { "m", "°" };
            string[] val = { Num.Fmt(Math.Sqrt(dx * dx + dy * dy), 3), Num.Fmt(Math.Atan2(dy, dx) * 180 / Math.PI, 1) };
            Point pc = Tela(f.X, f.Y);
            double x0 = pc.X + 18, y0 = pc.Y + 22, w = 168, h = 22;
            if (x0 + w > ActualWidth - 4) x0 = pc.X - 18 - w;
            if (y0 + 2 * h + 22 > ActualHeight - 4) y0 = pc.Y - 22 - 2 * h - 20;
            for (int i = 0; i < 2; i++)
            {
                bool travado = Din[i] != "", ativo = DinCampo == i;
                Rect r = new Rect(x0, y0 + i * (h + 2), w, h);
                var pen = new Pen(ativo ? TemaPlant.BBordaFoco : TemaPlant.BBordaSutil, ativo ? 1.5 : 1); pen.Freeze();
                dc.DrawRoundedRectangle(travado ? TemaPlant.BAcentoSutil : BCaixa, pen, r, 4, 4);
                var fr = Ft(rot[i], TemaPlant.BTexto2, 10.5);
                dc.DrawText(fr, new Point(r.X + 8, r.Y + (h - fr.Height) / 2));
                string txt = travado ? Din[i] + (ativo ? "|" : "") : val[i];
                var fv = Ft(txt, travado ? TemaPlant.BTexto1 : TemaPlant.BTexto2, 11.5);
                var fu = Ft(un[i], TemaPlant.BTexto2, 10.5);
                dc.DrawText(fu, new Point(r.Right - 8 - fu.Width, r.Y + (h - fu.Height) / 2));
                dc.DrawText(fv, new Point(r.Right - 12 - fu.Width - fv.Width, r.Y + (h - fv.Height) / 2));
            }
            var fd = Ft("Tab alterna · Enter confirma · Esc cancela", TemaPlant.BTexto2, 10);
            dc.DrawRectangle(BFundoTexto, null, new Rect(x0, y0 + 2 * (h + 2), fd.Width + 8, fd.Height + 2));
            dc.DrawText(fd, new Point(x0 + 4, y0 + 2 * (h + 2) + 1));
        }

        public static string TextoMedida(Point a, Point b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y, d = Math.Sqrt(dx * dx + dy * dy);
            double ang = Math.Atan2(dy, dx) * 180 / Math.PI;
            return "L = " + Num.Fmt(d, 3) + " m   ΔX = " + Num.Fmt(dx, 3) + " m   ΔY = " + Num.Fmt(dy, 3) + " m   ∠ " + Num.Fmt(ang, 1) + "°";
        }

        void DesenharGrade(DrawingContext dc)
        {
            double g = M.Grade > 0 ? M.Grade : 1;
            while (g * zoom < 9) g *= 5;
            Point a = Mundo(new Point(0, ActualHeight)), b = Mundo(new Point(ActualWidth, 0));
            long i0 = (long)Math.Floor(a.X / g), i1 = (long)Math.Ceiling(b.X / g), j0 = (long)Math.Floor(a.Y / g), j1 = (long)Math.Ceiling(b.Y / g);
            if (i1 - i0 > 3000 || j1 - j0 > 3000) return;
            for (long i = i0; i <= i1; i++) { double x = ox + i * g * zoom; dc.DrawLine(i % 5 == 0 ? GradeMaior : GradeMenor, new Point(x, 0), new Point(x, ActualHeight)); }
            for (long j = j0; j <= j1; j++) { double y = oy - j * g * zoom; dc.DrawLine(j % 5 == 0 ? GradeMaior : GradeMenor, new Point(0, y), new Point(ActualWidth, y)); }
        }

        void Apoio(DrawingContext dc, No n, Point p)
        {
            if (!n.TemApoio) return;
            if (n.ApoioX && n.ApoioY && n.ApoioGiro)
            {
                dc.DrawLine(PApoio, new Point(p.X - 14, p.Y + 4), new Point(p.X + 14, p.Y + 4));
                for (int k = -14; k <= 10; k += 6) dc.DrawLine(PApoio, new Point(p.X + k, p.Y + 12), new Point(p.X + k + 6, p.Y + 4));
                dc.DrawLine(PApoio, p, new Point(p.X, p.Y + 4));
            }
            else if (n.ApoioX || n.ApoioY)
            {
                bool lateral = n.ApoioX && !n.ApoioY;
                Point v2, v3;
                if (!lateral) { v2 = new Point(p.X - 9, p.Y + 14); v3 = new Point(p.X + 9, p.Y + 14); }
                else { v2 = new Point(p.X - 14, p.Y - 9); v3 = new Point(p.X - 14, p.Y + 9); }
                var g = new StreamGeometry();
                using (var c = g.Open()) { c.BeginFigure(p, false, true); c.LineTo(v2, true, false); c.LineTo(v3, true, false); }
                dc.DrawGeometry(null, PApoio, g);
                bool rolo = !(n.ApoioX && n.ApoioY);
                if (!lateral)
                {
                    if (rolo) dc.DrawLine(PApoio, new Point(p.X - 12, p.Y + 17), new Point(p.X + 12, p.Y + 17));
                    dc.DrawLine(PApoio, new Point(p.X - 12, p.Y + (rolo ? 21 : 16)), new Point(p.X + 12, p.Y + (rolo ? 21 : 16)));
                }
                else
                {
                    dc.DrawLine(PApoio, new Point(p.X - 17, p.Y - 12), new Point(p.X - 17, p.Y + 12));
                    dc.DrawLine(PApoio, new Point(p.X - 21, p.Y - 12), new Point(p.X - 21, p.Y + 12));
                }
                if (n.ApoioGiro) dc.DrawRectangle(PApoio.Brush, null, new Rect(p.X - 3, p.Y - 3, 6, 6));
            }
            else if (n.ApoioGiro) Texto(dc, "trava giro", new Point(p.X + 24, p.Y + 14), PApoio.Brush, 9);
            if (n.MolaX != 0 || n.MolaY != 0 || n.MolaGiro != 0) Texto(dc, "mola", new Point(p.X + 18, p.Y + 22), PApoio.Brush, 9);
        }

        void Seta(DrawingContext dc, Pen pen, Point ponta, Vector dir, double comp)
        {
            if (dir.Length < 1e-9) return;
            dir.Normalize();
            if (comp > 6) dc.DrawLine(pen, ponta - dir * comp, ponta - dir * 6);
            Vector nn = new Vector(-dir.Y, dir.X);
            var g = new StreamGeometry();   // ponta cheia: seta limpa
            using (var c = g.Open()) { c.BeginFigure(ponta, true, true); c.LineTo(ponta - dir * 8 + nn * 3.6, false, false); c.LineTo(ponta - dir * 8 - nn * 3.6, false, false); }
            g.Freeze();
            dc.DrawGeometry(pen.Brush, null, g);
        }

        void CargaNo(DrawingContext dc, No n, Point p)
        {
            if (n.Fx != 0) { Vector d = new Vector(Math.Sign(n.Fx), 0); Seta(dc, PCarga, p - d * 4, d, 40); Rotulo(Num.Fmt(Math.Abs(n.Fx), 2) + " kN", p - d * 44, PCarga.Brush, -d * 26, 2); }
            if (n.Fy != 0) { Vector d = new Vector(0, -Math.Sign(n.Fy)); Seta(dc, PCarga, p - d * 4, d, 40); Rotulo(Num.Fmt(Math.Abs(n.Fy), 2) + " kN", p - d * 44, PCarga.Brush, -d * 16, 2); }
            if (n.Mz != 0)
            {
                var g = new StreamGeometry();
                using (var c = g.Open())
                {
                    c.BeginFigure(new Point(p.X + 16, p.Y), false, false);
                    c.ArcTo(new Point(p.X - 16, p.Y), new Size(16, 16), 0, false, n.Mz > 0 ? SweepDirection.Counterclockwise : SweepDirection.Clockwise, true, false);
                }
                dc.DrawGeometry(null, PCarga, g);
                Seta(dc, PCarga, new Point(p.X - 16, p.Y), new Vector(0, n.Mz > 0 ? 1 : -1), 1);
                Rotulo(Num.Fmt(Math.Abs(n.Mz), 2) + " kN·m", new Point(p.X, p.Y + (n.Mz > 0 ? -20 : 20)), PCarga.Brush, new Vector(0, n.Mz > 0 ? -10 : 10), 2);
            }
        }

        void CargaBarra(DrawingContext dc, Barra b, Point pa, Point pc)
        {
            if (!b.TemCarga) return;
            Vector u = pc - pa; double l = u.Length; if (l < 1e-6) return;
            u.Normalize();
            Vector nn = new Vector(u.Y, -u.X); if (nn.Y > 0) nn = -nn;
            int k = Math.Max(2, (int)(l / 22));
            List<string> rot = new List<string>();
            double h = 28;
            if (b.QGlobalY != 0)
            {
                Vector d = new Vector(0, b.QGlobalY < 0 ? 1 : -1);
                dc.DrawLine(PCarga, pa - d * h, pc - d * h);
                for (int i = 0; i <= k; i++) Seta(dc, PCarga, pa + (pc - pa) * i / k - d * 2, d, h - 2);
                rot.Add("qy " + Num.Fmt(b.QGlobalY, 2));
            }
            if (b.QGlobalX != 0)
            {
                Vector d = new Vector(Math.Sign(b.QGlobalX), 0);
                for (int i = 0; i <= k; i++) Seta(dc, PCarga, pa + (pc - pa) * i / k + nn * 6 + d * 10, d, 14);
                rot.Add("qx " + Num.Fmt(b.QGlobalX, 2));
            }
            if (b.QLocalIni != 0 || b.QLocalFim != 0)
            {
                double mx = Math.Max(Math.Abs(b.QLocalIni), Math.Abs(b.QLocalFim));
                Vector ly = new Vector(u.Y, -u.X);   // +y local na tela
                for (int i = 0; i <= k; i++)
                {
                    double q = b.QLocalIni + (b.QLocalFim - b.QLocalIni) * i / (double)k;
                    if (Math.Abs(q) < 1e-12) continue;
                    Seta(dc, PCarga, pa + (pc - pa) * i / k, ly * Math.Sign(q), Math.Max(8, h * Math.Abs(q) / mx));
                }
                rot.Add("q⊥ " + Num.Fmt(b.QLocalIni, 2) + "→" + Num.Fmt(b.QLocalFim, 2));
            }
            Rotulo(string.Join("  ", rot) + " kN/m", pa + (pc - pa) / 2 + nn * h, PCarga.Brush, nn * 14, 2);
        }

        void Reacoes(DrawingContext dc, No n, Point p)
        {
            // envoltoria: faixa minimo / maximo de cada reacao; setas pela combinacao de maior reacao
            List<double[]> rs = EnvNos != null && EnvNos.ContainsKey(n) ? EnvNos[n] : new List<double[]> { n.Res };
            double[] r = rs.OrderByDescending(x => Math.Abs(x[4]) + Math.Abs(x[3])).First();
            if (Math.Abs(r[3]) > 1e-6) { Vector d = new Vector(Math.Sign(r[3]), 0); Seta(dc, PReac, p - d * 26, d, 30); }
            if (Math.Abs(r[4]) > 1e-6) Seta(dc, PReac, p + new Vector(0, r[4] > 0 ? 24 : 54), new Vector(0, r[4] > 0 ? -1 : 1), 30);
            string[] nome = { "", "", "", "Rx", "Ry", "M" }, un = { "", "", "", " kN", " kN", " kN·m" };
            for (int k = 3; k <= 5; k++)
            {
                double mn = rs.Min(x => x[k]), mx = rs.Max(x => x[k]);
                if (Math.Max(Math.Abs(mn), Math.Abs(mx)) <= 0.005) continue;
                string s = nome[k] + " " + (rs.Count == 1 || Math.Abs(mx - mn) < 1e-9 ? Num.Fmt(mx, 2) : Num.Fmt(mn, 2) + " … " + Num.Fmt(mx, 2)) + un[k];
                // um rotulo por componente, junto a cauda da propria seta
                if (k == 3) { Vector d = new Vector(Math.Sign(r[3]), 0); Rotulo(s, p - d * 56, PReac.Brush, -d * 30, 0, 3, true); }
                else if (k == 4) Rotulo(s, new Point(p.X, p.Y + (r[4] > 0 ? 54 : 54)), PReac.Brush, new Vector(0, 14), 0, 2, true);
                else Rotulo(s, new Point(p.X + 20, p.Y + 10), PReac.Brush, new Vector(40, 8), 0, 1, true);
            }
        }

        // ================= diagramas =================

        double Extensao()
        {
            if (M.Nos.Count == 0) return 1;
            return Math.Max(Math.Max(M.Nos.Max(n => n.X) - M.Nos.Min(n => n.X), M.Nos.Max(n => n.Y) - M.Nos.Min(n => n.Y)), 0.1);
        }

        int Tipo { get { return Diag == Diagrama.Normal ? 0 : Diag == Diagrama.Cortante ? 1 : 2; } }

        double EscalaEsforco(out double vmax)
        {
            vmax = 0;
            foreach (Barra b in M.Barras)
                foreach (ResBarra r in Lista(b))
                {
                    double mn, mx; r.Extremos(Tipo, out mn, out mx);
                    vmax = Math.Max(vmax, Math.Max(Math.Abs(mn), Math.Abs(mx)));
                }
            return vmax < 1e-9 ? 0 : 0.12 * Extensao() / vmax * EscalaDiag;
        }

        double EscalaDeformada(out double dmax)
        {
            dmax = 0;
            foreach (Barra b in M.Barras)
                foreach (ResBarra r in Lista(b))
                    for (int i = 0; i <= 20; i++) { double x = r.L * i / 20; dmax = Math.Max(dmax, Math.Sqrt(Math.Pow(r.V(x), 2) + Math.Pow(r.Ux(x), 2))); }
            return dmax < 1e-15 ? 0 : 0.08 * Extensao() / dmax * EscalaDef;
        }

        // Texto da legenda do HUD: unidade, maximo e minimo do diagrama ativo (ou ampliacao da deformada).
        public Func<string> ResumoExterno;   // modelo espacial (DocumentoEspacial.cs): a legenda vem dos resultados do motor

        public string ResumoDiagrama()
        {
            if (ResumoExterno != null) return ResumoExterno();
            if (!Resolvido) return "sem resultados";
            if (Diag == Diagrama.Deformada)
            {
                double dm; double f = EscalaDeformada(out dm);
                return f <= 0 ? "sem deslocamento" : "ampliada " + Num.Fmt(f, 0) + "×  ·  máx. " + Num.Fmt(dm * 1000, 2) + " mm" + (Envoltoria != null ? " (todas as combinações)" : "");
            }
            if (Diag == Diagrama.Normal || Diag == Diagrama.Cortante || Diag == Diagrama.Momento)
            {
                double mn = double.MaxValue, mx = double.MinValue;
                foreach (Barra b in M.Barras) foreach (ResBarra r in Lista(b)) { double a, c; r.Extremos(Tipo, out a, out c); mn = Math.Min(mn, a); mx = Math.Max(mx, c); }
                if (mn > mx) return "sem esforços";
                return "máx. " + Num.Fmt(mx, 2) + "  ·  mín. " + Num.Fmt(mn, 2) + (Tipo == 2 ? " kN·m" : " kN");
            }
            return "";
        }

        public string LegendaDeformada()
        {
            double dmax; double f = EscalaDeformada(out dmax);
            return f <= 0 ? "" : "  Ampliada " + Num.Fmt(f, 0) + "×. Maior deslocamento: " + Num.Fmt(dmax * 1000, 2) + " mm" + (Envoltoria != null ? " (todas as combinações)" : "");
        }

        void DesenharDiagrama(DrawingContext dc)
        {
            if (Diag == Diagrama.Deformada)
            {
                double dmax; double f = EscalaDeformada(out dmax) * Cresc;
                if (f <= 0) return;
                foreach (Barra b in M.Barras)
                {
                    No a = M.NoPorId(b.N1);
                    foreach (ResBarra r in Lista(b))
                    {
                        var g = new StreamGeometry();
                        using (var c = g.Open())
                            for (int i = 0; i <= 30; i++)
                            {
                                double x = r.L * i / 30, ul = r.Ux(x), vl = r.V(x);
                                Point q = Tela(a.X + r.C * x + f * (r.C * ul - r.S * vl), a.Y + r.S * x + f * (r.S * ul + r.C * vl));
                                if (i == 0) c.BeginFigure(q, false, false); else c.LineTo(q, true, false);
                            }
                        dc.DrawGeometry(null, Envoltoria != null ? PEnv : PDef, g);
                    }
                }
                if (Envoltoria == null)
                    foreach (No n in M.Nos) if (n.Res != null)
                    {
                        double d = Math.Sqrt(n.Res[0] * n.Res[0] + n.Res[1] * n.Res[1]);
                        if (d <= 1e-9) continue;
                        Point q = Tela(n.X + f * n.Res[0], n.Y + f * n.Res[1]);
                        dc.DrawEllipse(Fundo, PDef, q, 3.2, 3.2);   // no deslocado
                        if (MostrarValores && d >= 0.05 * dmax) Rotulo(Num.Fmt(d * 1000, 2) + " mm", q, PDef.Brush, new Vector(0, -16), 3, d);
                    }
                return;
            }
            int tipo = Tipo;
            double vmax, esc = EscalaEsforco(out vmax);
            if (esc <= 0) { Texto(dc, "Esforço nulo em todas as barras.", new Point(ActualWidth / 2, 96), BTexto, 12); return; }
            esc *= Cresc;
            double sinal = tipo == 2 ? -1 : 1;   // momento do lado tracionado
            foreach (Barra b in M.Barras)
            {
                List<ResBarra> rs = Lista(b);
                if (rs.Count == 0) continue;
                ResBarra r0 = rs[0]; No a = M.NoPorId(b.N1);
                List<double> xs = new List<double>();
                for (int i = 0; i <= 40; i++) xs.Add(r0.L * i / 40);
                foreach (ResBarra r in rs) xs.AddRange(r.Pontos(tipo));
                xs.Sort();
                Func<double, double> vMax = x => rs.Max(r => r.Valor(tipo, x)), vMin = x => rs.Min(r => r.Valor(tipo, x));
                Curva(dc, r0, a, xs, vMax, esc, sinal, tipo);
                if (rs.Count > 1) Curva(dc, r0, a, xs, vMin, esc, sinal, tipo);
                if (!MostrarValores) continue;
                if (rs.Count == 1) Rotulos(dc, r0, a, xs, vMax, esc, sinal, vmax, true, tipo);
                else { Rotulos(dc, r0, a, xs, vMax, esc, sinal, vmax, false, tipo); Rotulos(dc, r0, a, xs, vMin, esc, sinal, vmax, false, tipo); }
            }
        }

        // Preenche a area entre a barra e a curva v(x) (azul onde positivo, vermelho onde negativo).
        // Em trechos de mesmo sinal: preenchimento translucido com gradiente (forte na curva, suave na linha de base) + contorno nitido.
        void Curva(DrawingContext dc, ResBarra r, No a, List<double> xs, Func<double, double> v, double esc, double sinal, int tipo)
        {
            double nx = -r.S, ny = r.C;
            Func<double, Point> baseEm = x => Tela(a.X + r.C * x, a.Y + r.S * x);
            Func<double, double, Point> curvaEm = (x, val) => Tela(a.X + r.C * x + nx * sinal * val * esc, a.Y + r.S * x + ny * sinal * val * esc);
            // divide a curva nos cruzamentos por zero
            var pedacos = new List<List<KeyValuePair<double, double>>>();
            var atual = new List<KeyValuePair<double, double>>();
            double xp = 0, vp = 0; bool tem = false;
            foreach (double x in xs)
            {
                double val = v(x);
                if (tem && x - xp < 1e-12) continue;
                if (tem && vp * val < 0)
                {
                    double xc = xp + (x - xp) * vp / (vp - val);
                    atual.Add(new KeyValuePair<double, double>(xc, 0)); pedacos.Add(atual);
                    atual = new List<KeyValuePair<double, double>> { new KeyValuePair<double, double>(xc, 0) };
                }
                atual.Add(new KeyValuePair<double, double>(x, val));
                xp = x; vp = val; tem = true;
            }
            if (atual.Count > 1) pedacos.Add(atual);
            foreach (var pc in pedacos)
            {
                var pico = pc.OrderByDescending(k => Math.Abs(k.Value)).First();
                bool pos = pico.Value >= 0;
                Color cor = pos ? CorPos[tipo] : CorNeg[tipo];
                Pen pen = pos ? PPosD[tipo] : PNegD[tipo];
                var pts = pc.Select(k => curvaEm(k.Key, k.Value)).ToList();
                var geo = new StreamGeometry();
                using (var c = geo.Open())
                {
                    c.BeginFigure(baseEm(pc[0].Key), true, true);
                    foreach (Point q in pts) c.LineTo(q, false, false);
                    c.LineTo(baseEm(pc[pc.Count - 1].Key), false, false);
                }
                geo.Freeze();
                Point bp = baseEm(pico.Key), tp = curvaEm(pico.Key, pico.Value);
                if ((tp - bp).Length > 0.5)
                {
                    var gr = new LinearGradientBrush(Color.FromArgb(0x70, cor.R, cor.G, cor.B), Color.FromArgb(0x10, cor.R, cor.G, cor.B), 0)
                    { MappingMode = BrushMappingMode.Absolute, StartPoint = tp, EndPoint = bp };
                    gr.Freeze();
                    dc.DrawGeometry(gr, null, geo);
                }
                // contorno so na curva (nao na linha de base); fechos finos nas pontas
                var linha = new StreamGeometry();
                using (var c = linha.Open()) { c.BeginFigure(pts[0], false, false); foreach (Point q in pts.Skip(1)) c.LineTo(q, true, true); }
                linha.Freeze();
                dc.DrawGeometry(null, pen, linha);
                Pen fecho = pos ? PFechoPos[tipo] : PFechoNeg[tipo];
                if (Math.Abs(pc[0].Value) > 1e-9) dc.DrawLine(fecho, baseEm(pc[0].Key), pts[0]);
                if (Math.Abs(pc[pc.Count - 1].Value) > 1e-9) dc.DrawLine(fecho, baseEm(pc[pc.Count - 1].Key), pts[pts.Count - 1]);
            }
        }

        // Valores nas pontas e no extremo interno (um resultado), ou so os extremos da curva (envoltoria).
        void Rotulos(DrawingContext dc, ResBarra r, No a, List<double> xs, Func<double, double> v, double esc, double sinal, double vmax, bool pontas, int tipo)
        {
            double nx = -r.S, ny = r.C;
            List<double> marcas = new List<double>();
            if (pontas) { marcas.Add(0); marcas.Add(r.L); }
            double xmx = xs.OrderByDescending(v).First(), xmn = xs.OrderBy(v).First();
            foreach (double x in new[] { xmx, xmn })
            {
                double val = v(x);
                bool difPontas = !pontas || (Math.Abs(val - v(0)) > 0.01 * vmax && Math.Abs(val - v(r.L)) > 0.01 * vmax && x > 0.02 * r.L && x < 0.98 * r.L);
                if (difPontas && !marcas.Any(m => Math.Abs(m - x) < 0.02 * r.L)) marcas.Add(x);
            }
            foreach (double x in marcas)
            {
                double val = v(x);
                if (Math.Abs(val) < 0.005) continue;
                Point t = Tela(a.X + r.C * x + nx * sinal * val * esc, a.Y + r.S * x + ny * sinal * val * esc);
                Point b0 = Tela(a.X + r.C * x, a.Y + r.S * x);
                Vector off = t - b0; if (off.Length < 1) off = new Vector(nx * sinal * Math.Sign(val), -ny * sinal * Math.Sign(val));
                off.Normalize();
                // rotulo do lado de fora da curva (longe da barra); valores maiores tem preferencia
                Rotulo(Num.Fmt(val, 2), t, val >= 0 ? PPosD[tipo].Brush : PNegD[tipo].Brush, off * 16, 0, Math.Abs(val));
            }
        }

        // Dados do cartao de inspecao: x, N(x), V(x), M(x) e delta(x) no ponto da barra sob o mouse.
        DadosInsp MontarInsp(Barra b, No a, double x, List<ResBarra> rs)
        {
            var d = new DadosInsp { B = b, X = x, Pos = "Barra " + b.Id + "  ·  x = " + Num.Fmt(x, 2) + " m" };
            Func<int, string> faixa = tp =>
            {
                double mn = rs.Min(r => r.Valor(tp, x)), mx = rs.Max(r => r.Valor(tp, x));
                return Math.Abs(mx - mn) < 1e-9 ? Num.Fmt(mx, 2) : Num.Fmt(mn, 2) + " … " + Num.Fmt(mx, 2);
            };
            d.N = faixa(0); d.V = faixa(1); d.M = faixa(2);
            d.D = Num.Fmt(rs.Max(r => Math.Sqrt(Math.Pow(r.Ux(x), 2) + Math.Pow(r.V(x), 2))) * 1000, 2);
            ResBarra r0 = rs[0];
            d.Base = Tela(a.X + r0.C * x, a.Y + r0.S * x);
            if (rs.Count == 1)
            {
                if (Diag == Diagrama.Deformada)
                {
                    double dm; double f = EscalaDeformada(out dm); double ul = r0.Ux(x), vl = r0.V(x);
                    d.Alvo = Tela(a.X + r0.C * x + f * (r0.C * ul - r0.S * vl), a.Y + r0.S * x + f * (r0.S * ul + r0.C * vl));
                    d.TemAlvo = f > 0; d.CorAlvo = PDef.Brush;
                }
                else
                {
                    double vmax; double esc = EscalaEsforco(out vmax); int tp = Tipo; double val = r0.Valor(tp, x), sg = tp == 2 ? -1 : 1;
                    d.Alvo = Tela(a.X + r0.C * x - r0.S * sg * val * esc, a.Y + r0.S * x + r0.C * sg * val * esc);
                    d.TemAlvo = esc > 0; d.CorAlvo = (val >= 0 ? PPosD[tp] : PNegD[tp]).Brush;
                }
            }
            return d;
        }

        static readonly Pen PMarcaInsp = Caneta(TemaPlant.BTexto1, 1.4), PRealceInsp = Caneta(TemaPlant.BRealce, 2), PBordaCartao = Caneta(TemaPlant.BBordaSutil, 1);

        // Marcador na barra + cartao com x, N, V, M e delta (a linha do diagrama ativo fica realcada).
        void DesenharInspecao(DrawingContext dc)
        {
            DadosInsp d = insp;
            if (d.TemAlvo && (d.Alvo - d.Base).Length > 1) dc.DrawLine(PGuia, d.Base, d.Alvo);
            dc.DrawEllipse(Fundo, PRealceInsp, d.Base, 4.5, 4.5);
            if (d.TemAlvo) dc.DrawEllipse(d.CorAlvo, PMarcaInsp, d.Alvo, 4, 4);

            var linhas = new[]
            {
                new { Nome = "N", V = d.N, U = "kN", Cor = (Brush)TemaPlant.B(CorPos[0]), Ativa = Diag == Diagrama.Normal },
                new { Nome = "V", V = d.V, U = "kN", Cor = (Brush)TemaPlant.B(CorPos[1]), Ativa = Diag == Diagrama.Cortante },
                new { Nome = "M", V = d.M, U = "kN·m", Cor = (Brush)TemaPlant.B(CorPos[2]), Ativa = Diag == Diagrama.Momento },
                new { Nome = "δ", V = d.D, U = "mm", Cor = (Brush)TemaPlant.BDeformada, Ativa = Diag == Diagrama.Deformada },
            };
            var cab = Ft(d.Pos, TemaPlant.BTexto2, 11);
            var vals = linhas.Select(l => new FormattedText(l.V, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, FonteNum, 11.5, TemaPlant.BTexto1, Dip)).ToList();
            var unis = linhas.Select(l => Ft(l.U, TemaPlant.BTexto2, 10.5)).ToList();
            double wv = vals.Max(f => f.Width), wu = unis.Max(f => f.Width);
            const double pad = 9, linhaH = 19;
            double w = Math.Max(cab.Width, 28 + wv + 6 + wu) + 2 * pad, h = 8 + cab.Height + 6 + linhas.Length * linhaH + 4;
            Point pos = new Point(mouseTela.X + 18, mouseTela.Y + 22);
            if (pos.X + w > ActualWidth - 4) pos.X = mouseTela.X - w - 14;
            if (pos.Y + h > ActualHeight - 4) pos.Y = mouseTela.Y - h - 14;
            pos.X = Math.Max(4, pos.X); pos.Y = Math.Max(4, pos.Y);
            dc.DrawRoundedRectangle(BCaixa, PBordaCartao, new Rect(pos.X, pos.Y, w, h), 6, 6);
            dc.DrawText(cab, new Point(pos.X + pad, pos.Y + 8));
            double y = pos.Y + 8 + cab.Height + 6;
            for (int i = 0; i < linhas.Length; i++)
            {
                var l = linhas[i];
                if (l.Ativa) dc.DrawRoundedRectangle(TemaPlant.BAcentoSutil, null, new Rect(pos.X + 4, y, w - 8, linhaH), 3, 3);
                dc.DrawEllipse(l.Cor, null, new Point(pos.X + pad + 3, y + linhaH / 2), 3, 3);
                dc.DrawText(Ft(l.Nome, l.Ativa ? TemaPlant.BTexto1 : TemaPlant.BTexto2, 11.5), new Point(pos.X + pad + 12, y + 2));
                dc.DrawText(vals[i], new Point(pos.X + pad + 28 + wv - vals[i].Width, y + 1.5));
                dc.DrawText(unis[i], new Point(pos.X + pad + 28 + wv + 6, y + 3));
                y += linhaH;
            }
        }

        // Leitura ao passar o mouse: esforco ou deslocamento no ponto da barra sob o cursor
        string LerDiagrama(Point tela)
        {
            insp = null;
            if (!Resolvido || Diag == Diagrama.Nenhum || Ferr != Ferramenta.Selecionar) return null;
            Barra b = Acertar(tela) as Barra;
            if (b == null || b.Res == null) return null;
            No a = M.NoPorId(b.N1), c = M.NoPorId(b.N2);
            Point pa = Tela(a.X, a.Y), pc = Tela(c.X, c.Y);
            Vector ab = pc - pa; if (ab.LengthSquared < 1) return null;
            double t = Math.Max(0, Math.Min(1, Vector.Multiply(tela - pa, ab) / ab.LengthSquared)), x = t * b.Res.L;
            string pos = "Barra " + b.Id + " a " + Num.Fmt(x, 2) + " m do início: ";
            List<ResBarra> rs = Lista(b);
            if (Diag != Diagrama.Verificacao && rs.Count > 0) insp = MontarInsp(b, a, x, rs);
            Func<int, string, string, string> faixa = (tp, nome, un) =>
            {
                double mn = rs.Min(r => r.Valor(tp, x)), mx = rs.Max(r => r.Valor(tp, x));
                return pos + nome + " = " + (rs.Count == 1 ? Num.Fmt(mx, 2) : "mín " + Num.Fmt(mn, 2) + " … máx " + Num.Fmt(mx, 2)) + " " + un;
            };
            switch (Diag)
            {
                case Diagrama.Normal: return faixa(0, "N", "kN");
                case Diagrama.Cortante: return faixa(1, "Q", "kN");
                case Diagrama.Momento: return faixa(2, "M", "kN·m");
                case Diagrama.Deformada:
                {
                    double ul = b.Res.Ux(x), vl = b.Res.V(x);
                    if (rs.Count > 1) return pos + "deslocamento máx. " + Num.Fmt(rs.Max(r => Math.Sqrt(Math.Pow(r.Ux(x), 2) + Math.Pow(r.V(x), 2))) * 1000, 3) + " mm (todas as combinações)";
                    return pos + "dX = " + Num.Fmt((b.Res.C * ul - b.Res.S * vl) * 1000, 3) + " mm  dY = " + Num.Fmt((b.Res.S * ul + b.Res.C * vl) * 1000, 3) + " mm";
                }
                case Diagrama.Verificacao:
                {
                    if (Verificar == null) return null;
                    Verif v = Verificar(b);
                    return v.Aplicavel ? "Barra " + b.Id + ": " + (v.Passa ? "passa" : "NÃO PASSA") + " — " + (v.Ap * 100).ToString("0") + "% (" + v.Critico + ")" : "Barra " + b.Id + ": " + v.Motivo;
                }
            }
            return null;
        }

        // Desenho fora da tela (relatorio): ajusta o tamanho, enquadra e devolve a imagem.
        public System.Windows.Media.Imaging.BitmapSource Imagem(int largura, int altura)
        {
            Width = largura; Height = altura;
            Measure(new Size(largura, altura));
            Arrange(new Rect(0, 0, largura, altura));
            UpdateLayout();
            EnquadrarEm(largura, altura, 110);
            // desenha direto num DrawingVisual (renderizar o proprio controle fora da arvore deslocava a figura)
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.PushClip(new RectangleGeometry(new Rect(0, 0, largura, altura)));
                OnRender(dc);
                dc.Pop();
            }
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(largura * 2, altura * 2, 192, 192, PixelFormats.Pbgra32);
            bmp.Render(dv);
            bmp.Freeze();
            return bmp;
        }
    }
}
