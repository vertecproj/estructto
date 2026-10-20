// Vista3D: câmera e mouse, igual ao AutoCAD/Plant 3D (docs/v3/PESQUISA-PLANT.md §2, docs/VISTA3D.md "Navegação").
//   roda = zoom no cursor · arrastar o botão do meio = pan · duplo clique do meio = enquadrar
//   Shift + meio arrastando = órbita transparente (não cancela o comando em curso) · Shift + esquerdo arrastando = órbita (touchpad)
//   esquerdo arrastando sem Shift = janela (esq.→dir.) ou cruzamento (dir.→esq.) · clique = ponto do comando, grip ou escolha
//   botão direito (sem arrastar) = Enter: termina a corrente / o pedido em curso.
// Pivô da órbita: centro da seleção; sem seleção, o ponto do modelo sob o cursor; sem nada, o centro do modelo.
// Ortográfica ou perspectiva (Perspectiva); planta, elevações e as 26 vistas do ViewCube são câmeras com transição de 300 ms.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace NovoFtool
{
    public partial class Vista3D
    {
        // ================= câmera =================

        readonly PerspectiveCamera pcam = new PerspectiveCamera { FieldOfView = 45 };
        bool perspectiva;
        public event Action CameraMudou;   // o ViewCube redesenha; quem mais precisar (Snap3D) também pode ouvir

        // Projeção: false = ortográfica (padrão, como planta/elevação de CAD); true = perspectiva (FOV horizontal 45°).
        public bool Perspectiva
        {
            get => perspectiva;
            set { if (perspectiva == value) return; perspectiva = value; AplicarCamera(); AgendarArestas(); AtualizarCursor(); }
        }

        Camera3 Atual() { return new Camera3 { Az = az, El = el, Larg = larg, Alvo = alvo }; }

        void Direcoes(out Vector3D d, out Vector3D cima, out Vector3D dir)
        {
            double a = az * Math.PI / 180, e = el * Math.PI / 180;
            d = new Vector3D(Math.Cos(e) * Math.Cos(a), Math.Cos(e) * Math.Sin(a), Math.Sin(e));          // do alvo para o olho
            cima = new Vector3D(-Math.Sin(e) * Math.Cos(a), -Math.Sin(e) * Math.Sin(a), Math.Cos(e));
            dir = Vector3D.CrossProduct(-d, cima);
        }

        double TanMeioFov => Math.Tan(pcam.FieldOfView * Math.PI / 360);
        double DistPersp => larg / 2 / TanMeioFov;          // distância do olho ao alvo: no plano do alvo cabem `larg` metros
        double FocalPx => Math.Max(1, ActualWidth) / 2 / TanMeioFov;
        Point3D Olho { get { Direcoes(out Vector3D d, out _, out _); return alvo + d * (perspectiva ? DistPersp : raio * 6); } }

        void AplicarCamera()
        {
            Direcoes(out Vector3D d, out Vector3D cima, out _);
            if (perspectiva)
            {
                double dist = DistPersp;
                pcam.Position = alvo + d * dist; pcam.LookDirection = -d; pcam.UpDirection = cima;
                pcam.NearPlaneDistance = Math.Max(0.01, dist * 0.02); pcam.FarPlaneDistance = dist + raio * 20;
                if (vp.Camera != pcam) vp.Camera = pcam;
            }
            else
            {
                cam.Position = alvo + d * raio * 6;
                cam.LookDirection = -d;
                cam.UpDirection = cima;
                cam.Width = larg;
                cam.NearPlaneDistance = raio; cam.FarPlaneDistance = raio * 14;   // faixa justa: melhor precisão de profundidade (chão x sombra)
                if (vp.Camera != cam) vp.Camera = cam;
            }
            sobre.InvalidateVisual();
            cubo?.InvalidateVisual();
            pivoVis?.InvalidateVisual();
            CameraMudou?.Invoke();
        }

        void IrPara(Camera3 c, bool animar)
        {
            if (!animar || !IsVisible || !Anim.Ligado)
            {
                Parar(); az = c.Az; el = c.El; larg = c.Larg; alvo = c.Alvo; AplicarCamera(); AtualizarCursor();
                if (Realista && Math.Abs(Math.Log(LarguraAresta() / largArestas)) > 0.3) Reconstruir();   // arestas na escala nova já
                return;
            }
            de = Atual(); para = c;
            para.Az = de.Az + ((c.Az - de.Az) % 360 + 540) % 360 - 180;   // caminho mais curto
            relogio.Restart();
            if (!animando) { animando = true; CompositionTarget.Rendering += Passo; }
        }

        void Parar() { if (animando) { animando = false; CompositionTarget.Rendering -= Passo; } }

        void Passo(object s, EventArgs e)
        {
            double t = Math.Min(1, relogio.Elapsed.TotalMilliseconds / DuracaoMs), k = 1 - Math.Pow(1 - t, 3);   // CubicEase EaseOut (Anim.Saida)
            az = de.Az + (para.Az - de.Az) * k; el = de.El + (para.El - de.El) * k; larg = de.Larg + (para.Larg - de.Larg) * k;
            alvo = de.Alvo + (para.Alvo - de.Alvo) * k;
            AplicarCamera();
            if (t >= 1) { Parar(); AgendarArestas(); AtualizarCursor(); }
        }

        double LargEnquadrada(double azv, double elv)
        {
            // projeta os cantos da caixa no plano da tela da direção pedida
            double a = azv * Math.PI / 180, e = elv * Math.PI / 180;
            var d = new Vector3D(Math.Cos(e) * Math.Cos(a), Math.Cos(e) * Math.Sin(a), Math.Sin(e));
            var cima = new Vector3D(-Math.Sin(e) * Math.Cos(a), -Math.Sin(e) * Math.Sin(a), Math.Cos(e));
            Vector3D dir = Vector3D.CrossProduct(-d, cima);
            Rect3D cx = Caixa(); Point3D c = new Point3D(cx.X + cx.SizeX / 2, cx.Y + cx.SizeY / 2, cx.Z + cx.SizeZ / 2);
            double w = 0, hh = 0;
            for (int i = 0; i < 8; i++)
            {
                Vector3D v = new Point3D(cx.X + (i & 1) * cx.SizeX, cx.Y + ((i >> 1) & 1) * cx.SizeY, cx.Z + ((i >> 2) & 1) * cx.SizeZ) - c;
                w = Math.Max(w, Math.Abs(Vector3D.DotProduct(v, dir))); hh = Math.Max(hh, Math.Abs(Vector3D.DotProduct(v, cima)));
            }
            double asp = ActualWidth > 1 && ActualHeight > 1 ? ActualWidth / ActualHeight : 16.0 / 9;
            return Math.Max(1, 2 * Math.Max(w, hh * asp) * (perspectiva ? 1.45 : 1.25));
        }

        public void Enquadrar(bool animar = true) { Enquadrar(az, el, animar); }

        public void ZoomFator(double f) { var c = Atual(); c.Larg /= f; IrPara(c, false); }   // > 1 aproxima, em torno do centro

        void Enquadrar(double azv, double elv, bool animar)
        {
            Rect3D cx = Caixa();
            IrPara(new Camera3 { Az = azv, El = elv, Larg = LargEnquadrada(azv, elv), Alvo = new Point3D(cx.X + cx.SizeX / 2, cx.Y + cx.SizeY / 2, cx.Z + cx.SizeZ / 2) }, animar);
        }

        // Zoom na seleção (Z O): caixa dos nós escolhidos (e das pontas das barras), na direção atual.
        public void EnquadrarSelecao()
        {
            var pts = PontosSelecao().ToList();
            if (pts.Count == 0) { Enquadrar(); return; }
            Point3D c = new Point3D(pts.Average(p => p.X), pts.Average(p => p.Y), pts.Average(p => p.Z));
            Direcoes(out _, out Vector3D cima, out Vector3D dir);
            double w = pts.Max(p => Math.Abs(Vector3D.DotProduct(p - c, dir))), h = pts.Max(p => Math.Abs(Vector3D.DotProduct(p - c, cima)));
            double asp = ActualWidth > 1 && ActualHeight > 1 ? ActualWidth / ActualHeight : 16.0 / 9;
            IrPara(new Camera3 { Az = az, El = el, Alvo = c, Larg = Math.Max(raio * 0.05, 2 * Math.Max(w, h * asp) * 1.4) }, true);
        }

        public void MostrarVista(VistaPadrao v, bool animar = true)
        {
            switch (v)
            {
                case VistaPadrao.Topo: AtualizarNome("Topo"); Enquadrar(-90, 90, animar); break;
                case VistaPadrao.Frente: AtualizarNome("Frente (XZ)"); Enquadrar(-90, 0, animar); break;
                case VistaPadrao.Lado: AtualizarNome("Direita (YZ)"); Enquadrar(0, 0, animar); break;
                default: AtualizarNome("Isométrica"); Enquadrar(-55, 28, animar); break;
            }
        }

        // Vista pela direção do olho (do alvo para o olho), como as 26 regiões do ViewCube. Planta e base mantêm X para a direita.
        public void IrParaDirecao(Vector3D v, string nome, bool animar = true)
        {
            if (v.Length < 1e-9) return;
            v.Normalize();
            double elv = Math.Asin(Math.Max(-1, Math.Min(1, v.Z))) * 180 / Math.PI;
            double azv = Math.Abs(Math.Abs(elv) - 90) < 1e-6 ? -90 : Math.Atan2(v.Y, v.X) * 180 / Math.PI;
            AtualizarNome(nome);
            Enquadrar(azv, elv, animar);
        }

        string nomeAtual = "Isométrica";
        public string NomeVista => nomeAtual;
        void AtualizarNome(string s) { nomeAtual = s; cubo?.InvalidateVisual(); }

        // ================= projeção: ponto ↔ pixel =================

        double Escala { get { return larg / Math.Max(1, ActualWidth); } }   // m por pixel no plano do alvo

        // ponto 3D -> pixel da vista (ortográfica ou perspectiva). Atrás do olho: muito longe (fora de qualquer tolerância).
        public Point Tela(Point3D q)
        {
            Direcoes(out Vector3D d, out Vector3D cima, out Vector3D dir);
            if (!perspectiva)
            {
                Vector3D r = q - alvo;
                return new Point(ActualWidth / 2 + Vector3D.DotProduct(r, dir) / Escala, ActualHeight / 2 - Vector3D.DotProduct(r, cima) / Escala);
            }
            Vector3D ro = q - Olho;
            double z = -Vector3D.DotProduct(ro, d);
            if (z < 1e-6) return new Point(-1e6, -1e6);
            double f = FocalPx;
            return new Point(ActualWidth / 2 + Vector3D.DotProduct(ro, dir) / z * f, ActualHeight / 2 - Vector3D.DotProduct(ro, cima) / z * f);
        }

        // Raio do pixel: origem e direção (unitária). Ortográfica: paralelo à vista, origem no plano do alvo recuada.
        public (Point3D O, Vector3D D) Raio(Point tela)
        {
            Direcoes(out Vector3D d, out Vector3D cima, out Vector3D dir);
            double sx = tela.X - ActualWidth / 2, sy = ActualHeight / 2 - tela.Y;
            if (!perspectiva) return (alvo + dir * sx * Escala + cima * sy * Escala + d * raio * 6, -d);
            double f = FocalPx;
            Vector3D rd = -d + dir * (sx / f) + cima * (sy / f); rd.Normalize();
            return (Olho, rd);
        }

        // ================= montagem: ViewCube e pivô =================

        CuboVista cubo; MarcaPivo pivoVis;

        void MontarNavegacao()
        {
            cubo = new CuboVista(this) { Width = 132, Height = 150, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 8, 8, 0) };
            pivoVis = new MarcaPivo(this) { IsHitTestVisible = false };
            Children.Add(pivoVis);
            Children.Add(cubo);
        }

        // Esfera escura com contorno branco no pivô enquanto a órbita dura (como o AutoCAD).
        sealed class MarcaPivo : FrameworkElement
        {
            readonly Vista3D v;
            public MarcaPivo(Vista3D v) { this.v = v; }
            static readonly Pen Contorno = Caneta(Brushes.White, 1.5);
            protected override void OnRender(DrawingContext dc)
            {
                if (v.pivoP == null || v.modo != Modo.Orbita) return;
                Point s = v.Tela(v.pivoP.Value);
                dc.DrawEllipse(TemaPlant.B(TemaPlant.CorPivo), Contorno, s, 5, 5);
            }
        }

        // ================= mouse =================

        enum Modo { Nada, Clique, Orbita, Pan, Caixa, Arrasto }
        Modo modo;
        Point3D? pivoP;
        Point pressIni;
        MouseButton botaoIni;

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            Parar();
            Point p = e.GetPosition(this);
            Direcoes(out _, out Vector3D cima, out Vector3D dir);
            double sx = (p.X - ActualWidth / 2) * Escala, sy = (ActualHeight / 2 - p.Y) * Escala;
            Point3D sob = alvo + dir * sx + cima * sy;   // ponto sob o cursor (no plano do alvo) fica parado
            larg = Math.Max(raio * 0.002, Math.Min(raio * 200, larg * (e.Delta > 0 ? 1 / 1.2 : 1.2)));
            sx = (p.X - ActualWidth / 2) * Escala; sy = (ActualHeight / 2 - p.Y) * Escala;
            alvo = sob - dir * sx - cima * sy;
            AplicarCamera(); AgendarArestas(); AtualizarCursor();
            e.Handled = true;
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            Focus();
            Point p = e.GetPosition(this);
            mouseTela = p; mouseDentro = true;
            if (modo != Modo.Nada) return;   // outro botão já está arrastando
            if (e.ChangedButton == MouseButton.Middle && e.ClickCount == 2) { Enquadrar(); return; }
            bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            Parar();
            pressIni = p; botaoIni = e.ChangedButton; moveu = false;
            if (e.ChangedButton == MouseButton.Middle) { if (shift) IniciarOrbita(p); else { modo = Modo.Pan; panIni = p; } CaptureMouse(); return; }
            if (e.ChangedButton == MouseButton.Right) { modo = Modo.Clique; CaptureMouse(); return; }
            if (e.ChangedButton != MouseButton.Left) return;
            // grip sob o cursor: pressionar já o deixa quente; arrastar e soltar = destino (como clicar duas vezes)
            if (!shift && gripQuente == null && !Desenhando && pedeObjeto == null && GripSob(p) is Grip g) { Esquentar(g); modo = Modo.Arrasto; CaptureMouse(); return; }
            modo = Modo.Clique;
            CaptureMouse();
        }

        // Pivô: seleção > ponto do modelo sob o cursor > centro do modelo.
        void IniciarOrbita(Point tela)
        {
            modo = Modo.Orbita; orbIni = tela;
            var sel = PontosSelecao().ToList();
            if (sel.Count > 0) pivoP = new Point3D(sel.Average(q => q.X), sel.Average(q => q.Y), sel.Average(q => q.Z));
            else if (PontoSob(tela) is Point3D q) pivoP = q;
            else { Rect3D c = Caixa(); pivoP = new Point3D(c.X + c.SizeX / 2, c.Y + c.SizeY / 2, c.Z + c.SizeZ / 2); }
            AtualizarNome("Órbita");
            pivoVis.InvalidateVisual();
        }

        // Ponto 3D da peça sob o pixel (raycast nas malhas da cena; inclui grade, setas e fantasmas).
        Point3D? PontoSob(Point tela)
        {
            Point3D? achado = null;
            VisualTreeHelper.HitTest(vp, null, r =>
            {
                if (r is RayMeshGeometry3DHitTestResult h) { achado = h.PointHit; return HitTestResultBehavior.Stop; }
                return HitTestResultBehavior.Continue;
            }, new PointHitTestParameters(TranslatePoint(tela, vp)));
            return achado;
        }

        // Nós escolhidos e pontas das barras escolhidas (pivô, zoom na seleção).
        IEnumerable<Point3D> PontosSelecao()
        {
            foreach (No3D n in mod.Nos) if (Selecao.Nos.Contains(Chave(n))) yield return P(n);
            foreach (Barra3D b in mod.Barras)
                if (Selecao.Barras.Contains(Chave(b)))
                {
                    No3D a = NoPorId(b.Ni), c = NoPorId(b.Nj);
                    if (a != null) yield return P(a);
                    if (c != null) yield return P(c);
                }
        }

        // Órbita restrita (AutoCAD 3DORBIT): horizontal = azimute em torno do Z global, vertical = elevação. O pivô não sai do lugar na tela.
        public void Orbitar(double dx, double dy)
        {
            Point3D pv = pivoP ?? alvo;
            Point s0 = Tela(pv);
            az -= dx * 0.4; el = Math.Max(-90, Math.Min(90, el + dy * 0.4));
            Direcoes(out _, out Vector3D cima, out Vector3D dir);
            alvo = pv - dir * (s0.X - ActualWidth / 2) * Escala - cima * (ActualHeight / 2 - s0.Y) * Escala;   // alvo no plano do pivô
            AtualizarNome("Órbita");
            AplicarCamera();
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            if (modo != Modo.Nada) return;
            mouseDentro = false; marca.Visibility = Visibility.Collapsed; gripHover = null; sobre.InvalidateVisual();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            Point p = e.GetPosition(this);
            mouseTela = p; mouseDentro = true;
            Vector dv = p - pressIni;
            switch (modo)
            {
                case Modo.Clique:
                    if (dv.Length < 3) { if (Desenhando) AtualizarCursor(); return; }
                    moveu = true;
                    bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
                    if (botaoIni == MouseButton.Left && shift) IniciarOrbita(pressIni);   // Shift + esquerdo = órbita (touchpad)
                    else if (botaoIni == MouseButton.Left && !Desenhando && pedeObjeto == null) { modo = Modo.Caixa; caixaIni = pressIni; }
                    else { modo = Modo.Arrasto; if (Desenhando) AtualizarCursor(); return; }   // desenhando: arrastar e soltar = clicar onde soltou
                    break;
                case Modo.Arrasto:
                    moveu = moveu || dv.Length >= 3;
                    AtualizarCursor(); return;
                case Modo.Caixa:
                    sobre.InvalidateVisual(); return;
                case Modo.Pan:
                {
                    Vector dp = p - panIni.Value;
                    if (dp.Length > 0) moveu = true;
                    Direcoes(out _, out Vector3D cima, out Vector3D dir);
                    alvo = alvo - dir * dp.X * Escala + cima * dp.Y * Escala;
                    panIni = p; AplicarCamera();
                    return;
                }
            }
            if (modo == Modo.Orbita)
            {
                Vector d2 = p - orbIni.Value;
                orbIni = p;
                Orbitar(d2.X, d2.Y);
                pivoVis.InvalidateVisual();
                return;
            }
            // sem botão: cursor do comando, grips sob o mouse
            if (Desenhando) AtualizarCursor();
            else
            {
                var g = gripQuente == null ? GripSob(p) : null;
                if (!Equals(g, gripHover)) { gripHover = g; Cursor = g != null ? Cursors.Hand : CursorFerramenta; sobre.InvalidateVisual(); }
            }
        }

        Cursor CursorFerramenta => Desenhando ? Cursors.Cross : null;

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            if (modo == Modo.Nada || e.ChangedButton != botaoIni) return;
            Point p = e.GetPosition(this);
            var m = modo; modo = Modo.Nada;
            ReleaseMouseCapture();
            switch (m)
            {
                case Modo.Caixa:
                {
                    Point a = caixaIni.Value; caixaIni = null;
                    SelecionarCaixa(a, p, Keyboard.Modifiers);
                    sobre.InvalidateVisual();
                    return;
                }
                case Modo.Orbita:
                    pivoP = null; orbIni = null; pivoVis.InvalidateVisual();
                    AtualizarCursor();   // o comando continua: elástico e atração na câmera nova
                    AgendarArestas();
                    return;
                case Modo.Pan:
                    panIni = null; AtualizarCursor(); return;
                case Modo.Arrasto:
                    if (gripQuente != null && moveu) { AtualizarCursor(); _ = Ponto(PontoFinal()); return; }   // grip arrastado e solto
                    if (Desenhando && botaoIni == MouseButton.Left) { _ = ClicarTela(p, Keyboard.Modifiers); return; }
                    return;
            }
            // clique (sem arrastar)
            if (botaoIni == MouseButton.Right) { Tecla(Key.Enter); return; }   // botão direito = Enter (termina a corrente/pedido)
            if (botaoIni != MouseButton.Left) return;
            Clique(p, Keyboard.Modifiers);
        }

        // Clique esquerdo: ponto do comando/grip > objeto pedido > grip > escolha.
        void Clique(Point p, ModifierKeys mods)
        {
            if (Desenhando) { _ = ClicarTela(p, mods); return; }
            if (pedeObjeto != null) { _ = EscolherPedido(p, mods); return; }
            if (GripSob(p) is Grip g) { Esquentar(g); return; }
            SelecionarClique(p, mods);   // Navegar também escolhe: a seleção (Selecao) é uma só para a edição
        }

        public object Acertar(Point p)
        {
            object achado = null;
            VisualTreeHelper.HitTest(vp, null, r =>
            {
                if (r is RayMeshGeometry3DHitTestResult h)
                {
                    object o = null;
                    if (mapa.TryGetValue(h.ModelHit, out object o1)) o = o1;
                    else if (faixas.TryGetValue(h.ModelHit, out var fx))   // malha juntada: a barra dona do vértice
                    {
                        int k = Array.BinarySearch(fx.Ini, h.VertexIndex1);
                        o = fx.Obj[k >= 0 ? k : Math.Max(0, ~k - 1)];
                    }
                    if (o == null) return HitTestResultBehavior.Continue;
                    if (o is No3D || achado == null) achado = o;   // nó tem prioridade sobre a barra que passa por ele
                    return o is No3D ? HitTestResultBehavior.Stop : HitTestResultBehavior.Continue;
                }
                return HitTestResultBehavior.Continue;
            }, new PointHitTestParameters(TranslatePoint(p, vp)));
            // Realista não desenha os nós: vale o nó a até 8 px do clique (os da barra acertada, ou qualquer um no vazio)
            if (Realista && !(achado is No3D))
            {
                IEnumerable<No3D> cand = achado is Barra3D bb ? new[] { NoPorId(bb.Ni), NoPorId(bb.Nj) } : mod.Nos;
                double dm = 8;
                foreach (No3D n in cand)
                {
                    if (n == null) continue;
                    double dd = (Tela(P(n)) - p).Length;
                    if (dd < dm) { dm = dd; achado = n; }
                }
            }
            // barra fina (analítico): vale a barra a até 6 px do eixo na tela
            if (achado == null)
            {
                double dm = 6;
                foreach (Barra3D b in mod.Barras)
                {
                    No3D a = NoPorId(b.Ni), c = NoPorId(b.Nj); if (a == null || c == null) continue;
                    double dd = DistSeg(p, Tela(P(a)), Tela(P(c)));
                    if (dd < dm) { dm = dd; achado = b; }
                }
            }
            return achado;
        }

        static double DistSeg(Point p, Point a, Point b)
        {
            Vector ab = b - a; double l2 = ab.LengthSquared;
            double t = l2 < 1e-12 ? 0 : Math.Max(0, Math.Min(1, Vector.Multiply(p - a, ab) / l2));
            return (p - (a + ab * t)).Length;
        }

        public void Escolher(object o)
        {
            object origem = o is No3D n ? n.Origem ?? n : o is Barra3D b ? b.Origem ?? b : null;
            Selecionado = origem;
            Reconstruir();
            Escolheu?.Invoke(o);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.F4) { Enquadrar(); e.Handled = true; }
            else if (Tecla(e.Key)) e.Handled = true;
            base.OnKeyDown(e);
        }

        protected override void OnTextInput(TextCompositionEventArgs e)
        {
            if (Digitar(e.Text)) e.Handled = true;
            base.OnTextInput(e);
        }

        // ---------- simulação (capturas e testes: o mesmo caminho do mouse, sem mouse de verdade) ----------

        // Shift + arrastar de `de` até `ate` (pixels): órbita transparente; o comando em curso continua.
        public void SimularOrbita(Point de, Point ate, int passos = 12)
        {
            IniciarOrbita(de);
            Point ant = de;
            for (int i = 1; i <= passos; i++)
            {
                Point q = de + (ate - de) * i / (double)passos;
                Orbitar(q.X - ant.X, q.Y - ant.Y); ant = q;
            }
            modo = Modo.Nada; pivoP = null; orbIni = null;
            AtualizarCursor();
        }
    }
}
