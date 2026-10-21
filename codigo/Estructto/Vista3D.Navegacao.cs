// Vista3D: câmera e mouse, igual ao AutoCAD/Plant 3D (docs/v3/PESQUISA-PLANT.md §2, docs/VISTA3D.md "Navegação").
//   roda = zoom no cursor · arrastar o botão do meio = pan · duplo clique do meio = enquadrar
//   Shift + meio arrastando = órbita transparente (não cancela o comando em curso) · Shift + esquerdo arrastando = órbita (touchpad)
//   esquerdo arrastando sem Shift = janela (esq.→dir.) ou cruzamento (dir.→esq.) · clique = ponto do comando, grip ou escolha
//   botão direito (sem arrastar) = Enter: termina a corrente / o pedido em curso.
// Pivô automático (Fusion 360/Revit/Inventor), sem chave: a peça sob o cursor > o nó/barra mais perto (30 px) > o plano de
// trabalho > o centro do que aparece do modelo (PivoSob). A roda usa o mesmo ponto (Zoom).
// Ortográfica ou perspectiva (Perspectiva); planta, elevações e as 26 vistas do ViewCube são câmeras com transição de 300 ms.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
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
                if (render != null && Dx) { render.Camera(true, pcam.Position, pcam.LookDirection, pcam.UpDirection, pcam.FieldOfView, pcam.NearPlaneDistance, pcam.FarPlaneDistance); render.Lod(FocalPx / dist); }
            }
            else
            {
                cam.Position = alvo + d * raio * 6;
                cam.LookDirection = -d;
                cam.UpDirection = cima;
                cam.Width = larg;
                cam.NearPlaneDistance = raio; cam.FarPlaneDistance = raio * 14;   // faixa justa: melhor precisão de profundidade (chão x sombra)
                if (vp.Camera != cam) vp.Camera = cam;
                if (render != null && Dx) { render.Camera(false, cam.Position, cam.LookDirection, cam.UpDirection, larg, raio, raio * 14); render.Lod(1 / Escala); }
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
                if (Realista && !Dx && Math.Abs(Math.Log(LarguraAresta() / largArestas)) > 0.3) Reconstruir();   // arestas na escala nova já
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

        // Projeta os nós (não os cantos da caixa, que sobram nas vistas iso) no plano da tela da direção pedida: largura com
        // margem de 15% (perspectiva 35%: o que está perto cresce) e alvo no centro do que aparece.
        Camera3 Enquadramento(double azv, double elv)
        {
            double a = azv * Math.PI / 180, e = elv * Math.PI / 180;
            var d = new Vector3D(Math.Cos(e) * Math.Cos(a), Math.Cos(e) * Math.Sin(a), Math.Sin(e));
            var cima = new Vector3D(-Math.Sin(e) * Math.Cos(a), -Math.Sin(e) * Math.Sin(a), Math.Cos(e));
            Vector3D dir = Vector3D.CrossProduct(-d, cima);
            Rect3D cx = Caixa(); Point3D c = new Point3D(cx.X + cx.SizeX / 2, cx.Y + cx.SizeY / 2, cx.Z + cx.SizeZ / 2);
            var pts = mod.Nos.Count > 0 ? mod.Nos.Select(n => new Point3D(n.X, n.Y, n.Z))
                                        : Enumerable.Range(0, 8).Select(i => new Point3D(cx.X + (i & 1) * cx.SizeX, cx.Y + ((i >> 1) & 1) * cx.SizeY, cx.Z + ((i >> 2) & 1) * cx.SizeZ));
            double u0 = double.MaxValue, u1 = double.MinValue, v0 = double.MaxValue, v1 = double.MinValue;
            foreach (Point3D p in pts)
            {
                double du = Vector3D.DotProduct(p - c, dir), dv = Vector3D.DotProduct(p - c, cima);
                u0 = Math.Min(u0, du); u1 = Math.Max(u1, du); v0 = Math.Min(v0, dv); v1 = Math.Max(v1, dv);
            }
            double asp = ActualWidth > 1 && ActualHeight > 1 ? ActualWidth / ActualHeight : 16.0 / 9;
            return new Camera3 { Az = azv, El = elv, Larg = Math.Max(1, Math.Max(u1 - u0, (v1 - v0) * asp) * (perspectiva ? 1.35 : 1.15)),
                                 Alvo = c + dir * ((u0 + u1) / 2) + cima * ((v0 + v1) / 2) };
        }

        public void Enquadrar(bool animar = true) { Enquadrar(az, el, animar); }

        public void ZoomFator(double f) { var c = Atual(); c.Larg /= f; IrPara(c, false); }   // > 1 aproxima, em torno do centro

        void Enquadrar(double azv, double elv, bool animar)
        {
            IrPara(Enquadramento(azv, elv), animar);
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

        // Marca do pivô: ponto ciano com anel enquanto a órbita dura; some com Anim.Medio ao soltar (TerminarOrbita).
        sealed class MarcaPivo : FrameworkElement
        {
            readonly Vista3D v;
            public MarcaPivo(Vista3D v) { this.v = v; }
            static readonly Pen Anel = Caneta(TemaPlant.B(TemaPlant.CorPivoAnel), 1.25);
            static readonly Pen Halo = Caneta(TemaPlant.B(TemaPlant.CorSnapContorno), 3);
            static readonly Pen Borda = Caneta(TemaPlant.B(TemaPlant.CorSnapContorno), 1);
            protected override void OnRender(DrawingContext dc)
            {
                if (v.marcaPivo is not Point3D p) return;
                Point s = v.Tela(p);
                dc.DrawEllipse(null, Halo, s, 8, 8);
                dc.DrawEllipse(null, Anel, s, 8, 8);
                dc.DrawEllipse(TemaPlant.B(TemaPlant.CorPivo), Borda, s, 3, 3);
            }
        }

        // ================= mouse =================

        enum Modo { Nada, Clique, Orbita, Pan, Caixa, Arrasto }
        Modo modo;
        Point3D? pivoP, marcaPivo;   // marcaPivo fica depois de soltar, enquanto a marca some
        Point pressIni;
        MouseButton botaoIni;

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            Zoom(e.GetPosition(this), e.Delta > 0);
            e.Handled = true;
        }

        // Roda: zoom rumo ao ponto sob o cursor (o mesmo do pivô, PivoSob), que fica parado na tela.
        // Ortográfica: muda a largura em torno dele. Perspectiva: dolly do olho pelo raio do cursor, com o alvo junto na
        // profundidade do ponto: chega perto sem atravessar; sem ponto à frente (ou colado nele) anda um passo fixo, sem empacar.
        public void Zoom(Point p, bool aproximar)
        {
            Parar();
            double f = aproximar ? 1 / 1.2 : 1.2;
            var (pt, fonte) = PivoSob(p);
            Direcoes(out Vector3D d, out Vector3D cima, out Vector3D dir);
            if (!perspectiva)
            {
                AncorarAlvo(pt);   // alvo na profundidade do ponto: o recorte da câmera fica em volta do que se olha
                double sx = (p.X - ActualWidth / 2) * Escala, sy = (ActualHeight / 2 - p.Y) * Escala;
                Point3D sob = alvo + dir * sx + cima * sy;   // ponto sob o cursor (no plano do alvo) fica parado
                larg = Math.Max(raio * 0.002, Math.Min(raio * 200, larg * f));
                sx = (p.X - ActualWidth / 2) * Escala; sy = (ActualHeight / 2 - p.Y) * Escala;
                alvo = sob - dir * sx - cima * sy;
            }
            else
            {
                if (!aproximar && DistPersp > raio * 100) return;
                var (o, rd) = Raio(p);
                double t = Vector3D.DotProduct(pt - o, rd);
                if (fonte == 'e' || t < raio * 0.02)
                    alvo += rd * (Math.Max(DistPersp, raio * 0.02) * (aproximar ? 0.17 : -0.2));   // olho e alvo juntos, larg igual
                else
                {
                    Point3D olho = o + rd * (t * (1 - f));
                    double z = t * f * Vector3D.DotProduct(rd, -d);   // profundidade do ponto vista do olho novo
                    larg = 2 * z * TanMeioFov;
                    alvo = olho - d * z;
                }
            }
            AplicarCamera(); AgendarArestas(); AtualizarCursor();
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            Focus();
            if (MouseSnap(e)) return;   // Shift/Ctrl + botão direito: menu de snap único (Vista3D.Snap.cs)
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

        // Começo da órbita (Shift + arrastar, ViewCube): o pivô sai de PivoSob e fica parado até soltar.
        public void IniciarOrbita(Point tela)
        {
            modo = Modo.Orbita; orbIni = tela;
            pivoP = PivoSob(tela).P;
            AncorarAlvo(pivoP.Value);   // perspectiva: o olho fica, o alvo vai para a profundidade do pivô (a imagem não pula)
            marcaPivo = pivoP;
            pivoVis.BeginAnimation(OpacityProperty, null); pivoVis.Opacity = 1;
            AtualizarNome("Órbita");
            pivoVis.InvalidateVisual();
        }

        public void TerminarOrbita()
        {
            if (modo == Modo.Orbita) modo = Modo.Nada;
            pivoP = null; orbIni = null;
            Anim.Valor(pivoVis, OpacityProperty, 1, 0, Anim.Medio);
            AtualizarCursor();   // o comando continua: elástico e atração na câmera nova
            AgendarArestas();
        }

        public Point3D? PivoAtual => pivoP;

        // Leva o plano do alvo à profundidade de p sem mudar a imagem. Perspectiva: mesmo olho, larg da distância nova.
        void AncorarAlvo(Point3D p)
        {
            Direcoes(out Vector3D d, out _, out _);
            if (!perspectiva) { alvo += d * Vector3D.DotProduct(p - alvo, d); return; }
            Point3D olho = Olho;
            double z = Vector3D.DotProduct(olho - p, d);
            if (z < raio * 1e-4) return;   // atrás do olho ou colado nele: fica como está
            larg = 2 * z * TanMeioFov;
            alvo = olho - d * z;
        }

        internal const double TolPivoPx = 30;

        // Ponto em torno do qual orbitar/aproximar, pelo cursor. Fonte: 'a' peça sob o cursor (barras e nós; no Render3D
        // também chapas e parafusos), 'b' nó ou barra mais perto do raio (até TolPivoPx), 'c' plano de trabalho (perto do
        // modelo), 'd' centro dos nós que aparecem, 'e' nada (o alvo).
        public (Point3D P, char Fonte) PivoSob(Point tela)
        {
            Acertar(tela, out Point3D? pa);
            if (pa is Point3D a) return (a, 'a');
            if (MaisPerto(tela, TolPivoPx) is Point3D b) return (b, 'b');
            Rect3D cx = Caixa(); Point3D c0 = new Point3D(cx.X + cx.SizeX / 2, cx.Y + cx.SizeY / 2, cx.Z + cx.SizeZ / 2);
            if (mod.Nos.Count > 0 && NoPlano(tela) is Point3D c && (c - c0).Length <= raio * 2) return (c, 'c');
            double x0 = double.MaxValue, y0 = x0, z0 = x0, x1 = double.MinValue, y1 = x1, z1 = x1; int nv = 0;
            foreach (No3D n in mod.Nos)
            {
                Point s = Tela(P(n));
                if (s.X < 0 || s.Y < 0 || s.X > ActualWidth || s.Y > ActualHeight) continue;
                nv++; x0 = Math.Min(x0, n.X); x1 = Math.Max(x1, n.X); y0 = Math.Min(y0, n.Y); y1 = Math.Max(y1, n.Y); z0 = Math.Min(z0, n.Z); z1 = Math.Max(z1, n.Z);
            }
            if (nv > 0) return (new Point3D((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2), 'd');
            return (alvo, 'e');
        }

        // Nó ou ponto de barra mais perto do raio do cursor, até `tol` px na tela (o nó ganha 4 px de vantagem).
        Point3D? MaisPerto(Point tela, double tol)
        {
            var (o, rd) = Raio(tela);
            Point3D fim = o + rd * (raio * 400 + larg * 10);
            Point3D? achado = null; double dm = tol;
            foreach (No3D n in mod.Nos)
            {
                double dd = (Tela(P(n)) - tela).Length - 4;
                if (dd < dm) { dm = dd; achado = P(n); }
            }
            foreach (Barra3D b in mod.Barras)
            {
                No3D a = NoPorId(b.Ni), c = NoPorId(b.Nj); if (a == null || c == null) continue;
                Snap3D.Segmentos(o, fim, P(a), P(c), out _, out _, out _, out Point3D q);
                double dd = (Tela(q) - tela).Length;
                if (dd < dm) { dm = dd; achado = q; }
            }
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
                    TerminarOrbita();
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

        public object Acertar(Point p) => Acertar(p, out _);

        // `ponto`: onde o raio tocou a peça (nó/barra da cena nativa ou peça do Render3D); null se achou só pela tolerância.
        object Acertar(Point p, out Point3D? ponto)
        {
            object achado = null; Point3D? pt = null;
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
                    // nó tem prioridade sobre a barra que passa por ele; um nó lá atrás, em outro ponto do raio, não
                    if (achado == null) { achado = o; pt = h.PointHit; }
                    else if ((h.PointHit - pt.Value).Length > raio * 0.03) return HitTestResultBehavior.Stop;
                    else if (o is No3D) { achado = o; pt = h.PointHit; }
                    return o is No3D ? HitTestResultBehavior.Stop : HitTestResultBehavior.Continue;
                }
                return HitTestResultBehavior.Continue;
            }, new PointHitTestParameters(TranslatePoint(p, vp)));
            if (achado == null && Dx && render != null)   // peças no Render3D
            {
                achado = render.Acertar(TranslatePoint(p, render.Vista), out Point3D? pr);
                pt ??= pr;
            }
            ponto = pt;
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
        // soltar = false: continua orbitando (a marca do pivô fica na tela, para a captura); TerminarOrbita solta.
        public void SimularOrbita(Point de, Point ate, int passos = 12, bool soltar = true)
        {
            IniciarOrbita(de);
            Point ant = de;
            for (int i = 1; i <= passos; i++)
            {
                Point q = de + (ate - de) * i / (double)passos;
                Orbitar(q.X - ant.X, q.Y - ant.Y); ant = q;
            }
            if (soltar) TerminarOrbita();
        }
    }

    // Estructto.exe --testes-navegacao <saida.txt>: pivô automático, órbita em torno dele e zoom no cursor numa Vista3D
    // fora da janela (1200 × 800 px, analítica), no pórtico de 12 × 10 vãos e 6 pavimentos das capturas (2.430 barras).
    public static class TestesNavegacao
    {
        public static string Rodar()
        {
            var sb = new StringBuilder(); int falhas = 0;
            void Ok(string nome, bool c) { sb.AppendLine((c ? "ok    " : "FALHA ") + nome); if (!c) falhas++; }
            string F(double x) => x.ToString("0.###", CultureInfo.InvariantCulture);

            var g = new Modelo3D(); int nx = 12, ny = 10, nz = 6;
            int Id(int i, int j, int k) => 1 + i + (nx + 1) * (j + (ny + 1) * k);
            for (int k = 0; k <= nz; k++) for (int j = 0; j <= ny; j++) for (int i = 0; i <= nx; i++)
                g.Nos.Add(new No3D { Id = Id(i, j, k), X = i * 6, Y = j * 5, Z = k * 3.2 });
            for (int k = 0; k < nz; k++) for (int j = 0; j <= ny; j++) for (int i = 0; i <= nx; i++)
            {
                g.Barras.Add(new Barra3D { Id = g.Barras.Count + 1, Ni = Id(i, j, k), Nj = Id(i, j, k + 1) });
                if (i < nx) g.Barras.Add(new Barra3D { Id = g.Barras.Count + 1, Ni = Id(i, j, k + 1), Nj = Id(i + 1, j, k + 1) });
                if (j < ny) g.Barras.Add(new Barra3D { Id = g.Barras.Count + 1, Ni = Id(i, j, k + 1), Nj = Id(i, j + 1, k + 1) });
            }
            var v = new Vista3D();
            v.Carregar(g, false);
            v.Measure(new Size(1200, 800)); v.Arrange(new Rect(0, 0, 1200, 800)); v.UpdateLayout();
            v.MostrarVista(VistaPadrao.Iso, false);
            double W = v.ActualWidth, H = v.ActualHeight;
            bool NaTela(Point s) => s.X >= 0 && s.Y >= 0 && s.X <= W && s.Y <= H;
            Point3D J = new Point3D(6, 0, 16);                        // junta viga-pilar do 5º pavimento, na fachada da frente (nada na frente dela)
            Point3D M = new Point3D(21, 0, 19.2);                     // meio da viga da cobertura (18; 0; 19,2)-(24; 0; 19,2)

            // desempenho: pivô em 20 pixels da vista inteira
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 20; i++) v.PivoSob(new Point(W * (0.2 + 0.03 * i), H * (0.3 + 0.02 * i)));
            double ms = sw.Elapsed.TotalMilliseconds / 20;
            Ok("pivô em " + F(ms) + " ms por pedido (" + g.Barras.Count + " barras; meta < 5 ms)", ms < 5);

            foreach (bool persp in new[] { false, true })
            {
                string nome = persp ? "perspectiva" : "ortográfica";
                v.Perspectiva = persp;
                // 1) perto de uma junta (6 m de largura visível), junta fora do centro: órbita de 30°
                v.Focar(J + new Vector3D(1.2, 0, 0.6), 6, -55, 28);
                Point c = v.Tela(J);
                var (pv, fonte) = v.PivoSob(c);
                Ok(nome + ": pivô na junta sob o cursor (fonte " + fonte + ", a " + F((pv - J).Length) + " m)", (fonte == 'a' || fonte == 'b') && (pv - J).Length < 1);
                Point canto = v.Tela(new Point3D(72, 50, 0));
                v.SimularOrbita(c, c, 1, false);   // só começar: o alvo vai para a profundidade do pivô sem mexer na imagem
                double pulo = (v.Tela(new Point3D(72, 50, 0)) - canto).Length;
                v.TerminarOrbita();
                Ok(nome + ": começar a órbita não pula a vista (" + F(pulo) + " px)", pulo < 1e-6);
                Point s0 = v.Tela(pv), j0 = v.Tela(J);
                v.SimularOrbita(c, c + new Vector(75, 0), 12, false);   // 75 px × 0,4°/px = 30° de azimute
                Point3D? piv = v.PivoAtual;
                double dpx = piv is Point3D pp ? (v.Tela(pp) - s0).Length : 1e9;
                Point j1 = v.Tela(J);
                v.TerminarOrbita();
                Ok(nome + ": órbita de 30° com o pivô parado na tela (desvio " + F(dpx) + " px)", piv is Point3D pq0 && (pq0 - pv).Length < 1e-6 && dpx < 1);
                Ok(nome + ": a junta continua na tela, perto de onde estava (" + F((j1 - j0).Length) + " px)", NaTela(j1) && (j1 - j0).Length < 150);

                // 2) pivô na barra sob o cursor (meio da viga, longe dos nós)
                v.Focar(M, 10, -55, 28);
                var (pb, fb) = v.PivoSob(v.Tela(M) + new Vector(2, 1));
                Snap3D.Segmentos(pb, pb, new Point3D(18, 0, 19.2), new Point3D(24, 0, 19.2), out _, out _, out _, out Point3D eixo);
                Ok(nome + ": pivô na viga sob o cursor (fonte " + fb + ", a " + F((pb - eixo).Length) + " m do eixo)", fb == 'a' && (pb - eixo).Length < 0.5 && (pb - M).Length < 1);

                // 4) roda: o ponto do mundo sob o cursor fica no mesmo pixel
                v.Focar(M, 20, -55, 28);
                Point cur = v.Tela(M) + new Vector(2, 1);
                var (pz, _) = v.PivoSob(cur);
                double pior = 0;
                for (int i = 0; i < 4; i++) { v.Zoom(cur, true); pior = Math.Max(pior, (v.Tela(pz) - cur).Length); }
                for (int i = 0; i < 2; i++) { v.Zoom(cur, false); pior = Math.Max(pior, (v.Tela(pz) - cur).Length); }
                Ok(nome + ": zoom no cursor: o ponto acertado fica no pixel (desvio " + F(pior) + " px)", pior < 1);
            }

            // 3) sem peça perto: plano de trabalho (vista de topo, no meio de um vão, no térreo)
            v.Perspectiva = false;
            v.PlanoAtual = PlanoTrabalho.Horizontal(0);
            Point3D Q = new Point3D(33, 22.5, 0);
            v.Focar(Q, 8, -90, 90);
            var (pq, fq) = v.PivoSob(v.Tela(Q));
            Ok("plano de trabalho: fonte " + fq + " em (" + F(pq.X) + "; " + F(pq.Y) + "; " + F(pq.Z) + ")", fq == 'c' && (pq - Q).Length < 1e-6);
            v.MostrarVista(VistaPadrao.Frente, false);   // plano horizontal de perfil, cursor no céu: centro do que aparece
            var (pd, fd) = v.PivoSob(new Point(W / 2, 40));
            Ok("céu na vista de frente: centro dos nós visíveis (fonte " + fd + ")", fd == 'd' && Math.Abs(pd.X - 36) < 1e-6 && Math.Abs(pd.Z - 9.6) < 1e-6);

            // 5) perspectiva: a roda chega perto da junta (sem empacar no alvo) e, no vazio, anda sempre
            v.Perspectiva = true;
            v.Focar(new Point3D(36, 25, 9.6), 40, -55, 28);   // alvo no centro do modelo, junta J lá na frente
            Point cj = v.Tela(J);
            double d0 = (v.Raio(cj).O - J).Length;
            for (int i = 0; i < 20; i++) v.Zoom(cj, true);
            double d1 = (v.Raio(cj).O - J).Length;
            Ok("perspectiva: 20 cliques da roda levam o olho de " + F(d0) + " m a " + F(d1) + " m da junta", d1 < d0 * 0.1 && (v.Tela(J) - cj).Length < 1);
            v.MostrarVista(VistaPadrao.Frente, false);
            Point ceu = new Point(W / 2, 40);
            Point3D o0 = v.Raio(ceu).O, oa = o0; double passoMin = double.MaxValue, dist0 = (v.Raio(new Point(W / 2, H / 2)).O - new Point3D(36, 25, 9.6)).Length;
            for (int i = 0; i < 60; i++) { v.Zoom(ceu, true); Point3D ob = v.Raio(ceu).O; passoMin = Math.Min(passoMin, (ob - oa).Length); oa = ob; }
            Ok("perspectiva no vazio: 60 cliques andam " + F((oa - o0).Length) + " m (alvo estava a " + F(dist0) + " m), passo mínimo " + F(passoMin) + " m",
               (oa - o0).Length > dist0 && passoMin > 0.1);

            sb.AppendLine(falhas == 0 ? "TODOS PASSARAM" : falhas + " FALHA(S)");
            return sb.ToString();
        }
    }
}
