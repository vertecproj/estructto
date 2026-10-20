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
        public string Leitura;                             // leitura do diagrama sob o mouse

        // ---------- cores (tema escuro Autodesk) ----------
        static SolidColorBrush Cor(byte r, byte g, byte b, byte a = 255) { var x = new SolidColorBrush(Color.FromArgb(a, r, g, b)); x.Freeze(); return x; }
        static Pen Caneta(Brush b, double w, double[] tr = null) { var p = new Pen(b, w); if (tr != null) p.DashStyle = new DashStyle(tr, 0); p.Freeze(); return p; }
        // as cores que mudam no modo de impressao (fundo branco) sao de instancia
        Brush Fundo = Cor(0x21, 0x28, 0x30);
        static readonly Pen GradeMenor = Caneta(Cor(140, 160, 180, 34), 1), GradeMaior = Caneta(Cor(140, 160, 180, 70), 1);
        static readonly Pen EixoX = Caneta(Cor(220, 80, 80, 170), 1.2), EixoY = Caneta(Cor(90, 200, 90, 170), 1.2);
        Brush BBarra = Cor(0xE8, 0xEC, 0xF0);
        Pen PBarra = Caneta(Cor(0xE8, 0xEC, 0xF0), 2.6);
        static readonly Pen PHover = Caneta(Cor(0x5A, 0xC8, 0xFA), 4), PSel = Caneta(Cor(0x0A, 0x9B, 0xE6), 5);
        Pen PApoio = Caneta(Cor(0x5A, 0xC8, 0xFA), 1.6), PCarga = Caneta(Cor(0xF2, 0x6B, 0x5B), 1.6), PReac = Caneta(Cor(0x4C, 0xD9, 0x64), 1.6);
        Pen PDef = Caneta(Cor(0xFF, 0xC1, 0x07), 2, new double[] { 4, 2 });
        static readonly Pen PFant = Caneta(Cor(0x0A, 0x9B, 0xE6), 1.5, new double[] { 5, 3 });
        static readonly Pen PEnv = Caneta(Cor(0xB0, 0xB8, 0xC4, 160), 1, new double[] { 3, 2 });
        static readonly Pen PGuia = Caneta(Cor(0x4C, 0xD9, 0x64, 200), 1, new double[] { 2, 3 }), PMarca = Caneta(Cor(0x4C, 0xD9, 0x64), 2);
        static readonly Pen PJanela = Caneta(Cor(0x3D, 0x8B, 0xFD), 1), PCruz = Caneta(Cor(0x4C, 0xD9, 0x64), 1, new double[] { 4, 3 });
        static readonly Brush BJanela = Cor(0x3D, 0x8B, 0xFD, 40), BCruz = Cor(0x4C, 0xD9, 0x64, 40);
        Brush BTexto = Cor(0xD0, 0xD6, 0xDC), BFundoTexto = Cor(0x21, 0x28, 0x30, 190);
        static readonly Brush BCaixa = Cor(0x1C, 0x5F, 0x86, 235);
        static readonly Brush BMedida = Cor(0xE8, 0xB4, 0x48), BFundoMedida = Cor(0x1F, 0x24, 0x2C, 235), BTextoMedida = Cor(0xF0, 0xF2, 0xF5);
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
                Fundo = Brushes.White; BBarra = Cor(0x20, 0x24, 0x2A); PBarra = Caneta(BBarra, 2.2);
                BTexto = Cor(0x20, 0x24, 0x2A); BFundoTexto = Cor(255, 255, 255, 210);
                PApoio = Caneta(Cor(0x00, 0x5A, 0x9C), 1.6); PCarga = Caneta(Cor(0xC6, 0x28, 0x28), 1.6); PReac = Caneta(Cor(0x1B, 0x7F, 0x3A), 1.6);
                PDef = Caneta(Cor(0xD0, 0x7A, 0x00), 2, new double[] { 4, 2 });
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
        static readonly Brush BPos = Cor(0x3D, 0x8B, 0xFD, 90), BNeg = Cor(0xF2, 0x6B, 0x5B, 90);
        static readonly Pen PPos = Caneta(Cor(0x3D, 0x8B, 0xFD), 1.4), PNeg = Caneta(Cor(0xF2, 0x6B, 0x5B), 1.4);
        static readonly Pen POk = Caneta(Cor(0x4C, 0xD9, 0x64), 5), PAtencao = Caneta(Cor(0xE8, 0xB4, 0x48), 5), PFalha = Caneta(Cor(0xF2, 0x4B, 0x4B), 5), PSemVer = Caneta(Cor(0x80, 0x88, 0x94), 4);
        static readonly Typeface Fonte = new Typeface("Segoe UI");

        public Vista()
        {
            Focusable = true; ClipToBounds = true; Cursor = Cursors.Arrow;
        }

        public Point Tela(double x, double y) { return new Point(ox + x * zoom, oy - y * zoom); }
        public Point Mundo(Point p) { return new Point((p.X - ox) / zoom, (oy - p.Y) / zoom); }
        public double TolMundo { get { return 8 / zoom; } }
        public Point PontoMouse { get { return mouseMundo; } }
        public Point MouseTela { get { return mouseTela; } }
        public bool Ocupado { get { return arrastando != null || janelaIni != null; } }

        // ================= mouse =================

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            Point p = e.GetPosition(this), w = Mundo(p);
            zoom = Math.Max(0.5, Math.Min(20000, zoom * (e.Delta > 0 ? 1.2 : 1 / 1.2)));
            ox = p.X - w.X * zoom; oy = p.Y + w.Y * zoom;
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
                Point w = Atrair(p);
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
                if ((p - panIni.Value).Length > 3) panMoveu = true;
                ox = panOrig.X + p.X - panIni.Value.X; oy = panOrig.Y + p.Y - panIni.Value.Y;
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

        protected override void OnMouseLeave(MouseEventArgs e) { mouseDentro = false; hover = null; Leitura = null; InvalidateVisual(); }

        void Avisar() { if (SelecaoMudou != null) SelecaoMudou(); InvalidateVisual(); }

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
            if (M.Nos.Count == 0 || ActualWidth < 10) { zoom = 60; ox = ActualWidth / 2 - 5 * zoom; oy = ActualHeight / 2 + 3 * zoom; InvalidateVisual(); return; }
            double x0 = M.Nos.Min(n => n.X), x1 = M.Nos.Max(n => n.X), y0 = M.Nos.Min(n => n.Y), y1 = M.Nos.Max(n => n.Y);
            double w = Math.Max(x1 - x0, 1e-3), h = Math.Max(y1 - y0, 1e-3);
            double margem = impressao ? 120 : 200;   // relatorio: figura aproveita o espaco (sem linha de comando por cima)
            zoom = Math.Min((ActualWidth - margem) / w, (ActualHeight - margem) / h);
            if (x1 - x0 < 1e-3 && y1 - y0 < 1e-3) zoom = 60;
            zoom = Math.Max(0.5, Math.Min(20000, zoom));
            // sobe ~40 px: a linha de comando flutua sobre a parte de baixo do desenho (como no AutoCAD)
            ox = ActualWidth / 2 - (x0 + x1) / 2 * zoom; oy = ActualHeight / 2 - (impressao ? 0 : 40) + (y0 + y1) / 2 * zoom;
            InvalidateVisual();
        }

        public void ZoomFator(double f)
        {
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
            var ft = Ft(s, Brushes.White, 11);
            Rect r = new Rect(p.X, p.Y, ft.Width + 12, ft.Height + 6);
            if (r.Right > ActualWidth - 4) r.X = ActualWidth - 4 - r.Width;
            if (r.Bottom > ActualHeight - 4) r.Y = p.Y - r.Height - 30;
            dc.DrawRoundedRectangle(fundo, null, r, 3, 3);
            dc.DrawText(ft, new Point(r.X + 6, r.Y + 3));
        }

        protected override void OnRender(DrawingContext dc)
        {
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
                if (Sel.Contains(b)) dc.DrawLine(PSel, pa, pc);
                else if (hover == b || membrosAcesos.Contains(b.Membro)) dc.DrawLine(PHover, pa, pc);
                Pen pb = PBarra;
                if (Diag == Diagrama.Verificacao && Resolvido && Verificar != null)
                {
                    Verif v = Verificar(b);
                    pb = !v.Aplicavel ? PSemVer : v.Ap > 1 ? PFalha : v.Ap > 0.9 ? PAtencao : POk;
                    if (v.Aplicavel) Texto(dc, (v.Ap * 100).ToString("0") + "%", pa + (pc - pa) / 2 + new Vector(0, -14), pb.Brush, 10);
                }
                dc.DrawLine(pb, pa, pc);
                Vector u = pc - pa; if (u.Length > 1e-6) u.Normalize();
                if (b.RotulaInicio) dc.DrawEllipse(Fundo, PBarra, pa + u * 8, 4.5, 4.5);
                if (b.RotulaFim) dc.DrawEllipse(Fundo, PBarra, pc - u * 8, 4.5, 4.5);
                if (MostrarCargas) CargaBarra(dc, b, pa, pc);
                if (MostrarNomes && (pc - pa).Length > 90)
                {
                    // texto paralelo a barra, sempre legivel (da esquerda para a direita / de baixo para cima)
                    Vector d = u; if (d.X < -1e-9 || (Math.Abs(d.X) <= 1e-9 && d.Y > 0)) d = -d;
                    double ang = Math.Atan2(d.Y, d.X) * 180 / Math.PI;
                    Point meio = pa + (pc - pa) / 2 + new Vector(d.Y, -d.X) * 11;   // do lado de cima / esquerdo da barra
                    dc.PushTransform(new RotateTransform(ang, meio.X, meio.Y));
                    Texto(dc, (b.Membro != 0 ? "B" + b.Membro + " · " : "") + b.Secao + " · " + Num.Fmt(M.Comp(b), 2) + " m", meio, BTexto, 10);
                    dc.Pop();
                }
            }
            foreach (No n in M.Nos)
            {
                Point p = Tela(n.X, n.Y);
                Apoio(dc, n, p);
                if (n.Placa != null) { dc.DrawLine(PApoio, new Point(p.X - 12, p.Y + 2), new Point(p.X + 12, p.Y + 2)); }
                bool sel = Sel.Contains(n);
                if (sel) dc.DrawRectangle(Cor(0x0A, 0x9B, 0xE6), null, new Rect(p.X - 5, p.Y - 5, 10, 10));   // alca
                else if (hover == n) dc.DrawEllipse(null, PHover, p, 7, 7);
                dc.DrawRectangle(BBarra, null, new Rect(p.X - 2.5, p.Y - 2.5, 5, 5));
                if (MostrarCargas) CargaNo(dc, n, p);
                if (Resolvido && MostrarReacoes && n.Res != null && n.TemApoio) Reacoes(dc, n, p);
            }

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
                Point a = Tela(InicioBarra.Value.X, InicioBarra.Value.Y), b = Tela(mouseMundo.X, mouseMundo.Y);
                dc.DrawLine(PFant, a, b);
            }
            // ferramenta Medir: linha tracejada amarela #E8B448 + etiqueta (em andamento ou fixa apos o 2o clique)
            if (!impressao && MedidaIni != null && (MedidaFim != null || (Ferr == Ferramenta.Medir && mouseDentro)))
            {
                Point p1 = MedidaIni.Value, p2 = MedidaFim ?? mouseMundo;
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
            if (mouseDentro && Ferr == Ferramenta.Barra && InicioBarra != null)
            {
                double dx = mouseMundo.X - InicioBarra.Value.X, dy = mouseMundo.Y - InicioBarra.Value.Y;
                Caixa(dc, "L " + Num.Fmt(Math.Sqrt(dx * dx + dy * dy), 3) + " m   ∠ " + Num.Fmt(Math.Atan2(dy, dx) * 180 / Math.PI, 1) + "°   (digite p/ medida exata)",
                    new Point(mouseTela.X + 18, mouseTela.Y + 22), BCaixa);
            }
            else if (mouseDentro && arrastando != null)
                Caixa(dc, "Nó " + arrastando.Id + ": X " + Num.Fmt(arrastando.X, 3) + "  Y " + Num.Fmt(arrastando.Y, 3), new Point(mouseTela.X + 18, mouseTela.Y + 22), BCaixa);
            else if (mouseDentro && Leitura != null)
                Caixa(dc, Leitura, new Point(mouseTela.X + 18, mouseTela.Y + 22), BCaixa);

            if (!string.IsNullOrEmpty(Etiqueta)) Caixa(dc, Etiqueta, new Point(10, 10), BCaixa);   // no topo: embaixo fica a linha de comando
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
            dc.DrawLine(pen, ponta - dir * comp, ponta);
            Vector nn = new Vector(-dir.Y, dir.X);
            dc.DrawLine(pen, ponta, ponta - dir * 7 + nn * 3.5);
            dc.DrawLine(pen, ponta, ponta - dir * 7 - nn * 3.5);
        }

        void CargaNo(DrawingContext dc, No n, Point p)
        {
            if (n.Fx != 0) { Vector d = new Vector(Math.Sign(n.Fx), 0); Seta(dc, PCarga, p - d * 4, d, 40); Texto(dc, Num.Fmt(Math.Abs(n.Fx), 2) + " kN", p - d * 54, PCarga.Brush, 10); }
            if (n.Fy != 0) { Vector d = new Vector(0, -Math.Sign(n.Fy)); Seta(dc, PCarga, p - d * 4, d, 40); Texto(dc, Num.Fmt(Math.Abs(n.Fy), 2) + " kN", p - d * 54, PCarga.Brush, 10); }
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
                Texto(dc, Num.Fmt(Math.Abs(n.Mz), 2) + " kN·m", new Point(p.X, p.Y + (n.Mz > 0 ? -26 : 26)), PCarga.Brush, 10);
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
            Texto(dc, string.Join("  ", rot) + " kN/m", pa + (pc - pa) / 2 + nn * (h + 12), PCarga.Brush, 10);
        }

        void Reacoes(DrawingContext dc, No n, Point p)
        {
            // envoltoria: faixa minimo / maximo de cada reacao; setas pela combinacao de maior reacao
            List<double[]> rs = EnvNos != null && EnvNos.ContainsKey(n) ? EnvNos[n] : new List<double[]> { n.Res };
            double[] r = rs.OrderByDescending(x => Math.Abs(x[4]) + Math.Abs(x[3])).First();
            if (Math.Abs(r[3]) > 1e-6) { Vector d = new Vector(Math.Sign(r[3]), 0); Seta(dc, PReac, p - d * 26, d, 30); }
            if (Math.Abs(r[4]) > 1e-6) Seta(dc, PReac, p + new Vector(0, r[4] > 0 ? 24 : 54), new Vector(0, r[4] > 0 ? -1 : 1), 30);
            string s = "";
            string[] nome = { "", "", "", "Rx", "Ry", "M" }, un = { "", "", "", " kN", " kN", " kN·m" };
            for (int k = 3; k <= 5; k++)
            {
                double mn = rs.Min(x => x[k]), mx = rs.Max(x => x[k]);
                if (Math.Max(Math.Abs(mn), Math.Abs(mx)) <= 1e-6) continue;
                s += nome[k] + " " + (rs.Count == 1 || Math.Abs(mx - mn) < 1e-9 ? Num.Fmt(mx, 2) : Num.Fmt(mn, 2) + " … " + Num.Fmt(mx, 2)) + un[k] + "\n";
            }
            if (s != "") Texto(dc, s.TrimEnd('\n'), new Point(p.X + 16, p.Y + 30), PReac.Brush, 10, false);
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

        public string LegendaDeformada()
        {
            double dmax; double f = EscalaDeformada(out dmax);
            return f <= 0 ? "" : "  Ampliada " + Num.Fmt(f, 0) + "×. Maior deslocamento: " + Num.Fmt(dmax * 1000, 2) + " mm" + (Envoltoria != null ? " (todas as combinações)" : "");
        }

        void DesenharDiagrama(DrawingContext dc)
        {
            if (Diag == Diagrama.Deformada)
            {
                double dmax; double f = EscalaDeformada(out dmax);
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
                if (MostrarValores && Envoltoria == null)
                    foreach (No n in M.Nos) if (n.Res != null)
                    {
                        double d = Math.Sqrt(n.Res[0] * n.Res[0] + n.Res[1] * n.Res[1]);
                        if (d > 1e-9 && d >= 0.05 * dmax) Texto(dc, Num.Fmt(d * 1000, 2) + " mm", Tela(n.X + f * n.Res[0], n.Y + f * n.Res[1]) + new Vector(0, -14), PDef.Brush, 10);
                    }
                return;
            }
            int tipo = Tipo;
            double vmax, esc = EscalaEsforco(out vmax);
            if (esc <= 0) { Texto(dc, "Esforço nulo em todas as barras.", new Point(ActualWidth / 2, 40), BTexto, 12); return; }
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
                Curva(dc, r0, a, xs, vMax, esc, sinal);
                if (rs.Count > 1) Curva(dc, r0, a, xs, vMin, esc, sinal);
                if (!MostrarValores) continue;
                if (rs.Count == 1) Rotulos(dc, r0, a, xs, vMax, esc, sinal, vmax, true);
                else { Rotulos(dc, r0, a, xs, vMax, esc, sinal, vmax, false); Rotulos(dc, r0, a, xs, vMin, esc, sinal, vmax, false); }
            }
        }

        // Preenche a area entre a barra e a curva v(x) (azul onde positivo, vermelho onde negativo).
        void Curva(DrawingContext dc, ResBarra r, No a, List<double> xs, Func<double, double> v, double esc, double sinal)
        {
            double nx = -r.S, ny = r.C;
            for (int i = 0; i + 1 < xs.Count; i++)
            {
                double xa = xs[i], xb = xs[i + 1];
                if (xb - xa < 1e-12) continue;
                double va = v(xa), vb = v(xb);
                Point ba = Tela(a.X + r.C * xa, a.Y + r.S * xa), bb = Tela(a.X + r.C * xb, a.Y + r.S * xb);
                Point ta = Tela(a.X + r.C * xa + nx * sinal * va * esc, a.Y + r.S * xa + ny * sinal * va * esc);
                Point tb = Tela(a.X + r.C * xb + nx * sinal * vb * esc, a.Y + r.S * xb + ny * sinal * vb * esc);
                var g = new StreamGeometry();
                using (var c = g.Open()) { c.BeginFigure(ba, true, true); c.LineTo(ta, false, false); c.LineTo(tb, false, false); c.LineTo(bb, false, false); }
                bool pos = (va + vb) >= 0;
                dc.DrawGeometry(pos ? BPos : BNeg, null, g);
                dc.DrawLine(pos ? PPos : PNeg, ta, tb);
            }
        }

        // Valores nas pontas e no extremo interno (um resultado), ou so os extremos da curva (envoltoria).
        void Rotulos(DrawingContext dc, ResBarra r, No a, List<double> xs, Func<double, double> v, double esc, double sinal, double vmax, bool pontas)
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
                Vector off = new Vector(nx * sinal * Math.Sign(val), -ny * sinal * Math.Sign(val)) * 12;
                Texto(dc, Num.Fmt(val, 2), t + off, val >= 0 ? PPos.Brush : PNeg.Brush, 10);
            }
        }

        // Leitura ao passar o mouse: esforco ou deslocamento no ponto da barra sob o cursor
        string LerDiagrama(Point tela)
        {
            if (!Resolvido || Diag == Diagrama.Nenhum || Ferr != Ferramenta.Selecionar) return null;
            Barra b = Acertar(tela) as Barra;
            if (b == null || b.Res == null) return null;
            No a = M.NoPorId(b.N1), c = M.NoPorId(b.N2);
            Point pa = Tela(a.X, a.Y), pc = Tela(c.X, c.Y);
            Vector ab = pc - pa; if (ab.LengthSquared < 1) return null;
            double t = Math.Max(0, Math.Min(1, Vector.Multiply(tela - pa, ab) / ab.LengthSquared)), x = t * b.Res.L;
            string pos = "Barra " + b.Id + " a " + Num.Fmt(x, 2) + " m do início: ";
            List<ResBarra> rs = Lista(b);
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
