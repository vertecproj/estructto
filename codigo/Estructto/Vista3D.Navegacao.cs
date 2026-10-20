// Vista3D: câmera e mouse (órbita, pan, zoom, picking). Ver docs/VISTA3D.md.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using System.Text.Json.Nodes;
using Portico;
using Material = System.Windows.Media.Media3D.Material;
using F = Fluent;


namespace NovoFtool
{
    public partial class Vista3D
    {
        // ================= câmera =================

        Camera3 Atual() { return new Camera3 { Az = az, El = el, Larg = larg, Alvo = alvo }; }

        void Direcoes(out Vector3D d, out Vector3D cima, out Vector3D dir)
        {
            double a = az * Math.PI / 180, e = el * Math.PI / 180;
            d = new Vector3D(Math.Cos(e) * Math.Cos(a), Math.Cos(e) * Math.Sin(a), Math.Sin(e));          // do alvo para o olho
            cima = new Vector3D(-Math.Sin(e) * Math.Cos(a), -Math.Sin(e) * Math.Sin(a), Math.Cos(e));
            dir = Vector3D.CrossProduct(-d, cima);
        }

        void AplicarCamera()
        {
            Direcoes(out Vector3D d, out Vector3D cima, out _);
            cam.Position = alvo + d * raio * 6;
            cam.LookDirection = -d;
            cam.UpDirection = cima;
            cam.Width = larg;
            cam.NearPlaneDistance = raio; cam.FarPlaneDistance = raio * 14;   // faixa justa: melhor precisão de profundidade (chão x sombra)
            sobre.InvalidateVisual();
        }

        void IrPara(Camera3 c, bool animar)
        {
            if (!animar || !IsVisible)
            {
                Parar(); az = c.Az; el = c.El; larg = c.Larg; alvo = c.Alvo; AplicarCamera();
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
            double t = Math.Min(1, relogio.Elapsed.TotalMilliseconds / DuracaoMs), k = 1 - Math.Pow(1 - t, 3);   // CubicEase EaseOut
            az = de.Az + (para.Az - de.Az) * k; el = de.El + (para.El - de.El) * k; larg = de.Larg + (para.Larg - de.Larg) * k;
            alvo = de.Alvo + (para.Alvo - de.Alvo) * k;
            AplicarCamera();
            if (t >= 1) { Parar(); AgendarArestas(); }
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
            return Math.Max(1, 2 * Math.Max(w, hh * asp) * 1.25);
        }

        public void Enquadrar(bool animar = true) { Enquadrar(az, el, animar); }

        public void ZoomFator(double f) { var c = Atual(); c.Larg /= f; IrPara(c, false); }   // > 1 aproxima, em torno do centro

        void Enquadrar(double azv, double elv, bool animar)
        {
            Rect3D cx = Caixa();
            IrPara(new Camera3 { Az = azv, El = elv, Larg = LargEnquadrada(azv, elv), Alvo = new Point3D(cx.X + cx.SizeX / 2, cx.Y + cx.SizeY / 2, cx.Z + cx.SizeZ / 2) }, animar);
        }

        public void MostrarVista(VistaPadrao v, bool animar = true)
        {
            switch (v)
            {
                case VistaPadrao.Topo: AtualizarNome("Topo"); Enquadrar(-90, 90, animar); break;
                case VistaPadrao.Frente: AtualizarNome("Frente (XZ)"); Enquadrar(-90, 0, animar); break;
                case VistaPadrao.Lado: AtualizarNome("Lado (YZ)"); Enquadrar(0, 0, animar); break;
                default: AtualizarNome("Isométrica"); Enquadrar(-55, 28, animar); break;
            }
        }

        void AtualizarNome(string s) { nomeVista.Text = s; }

        // ================= mouse =================

        double Escala { get { return larg / Math.Max(1, ActualWidth); } }   // m por pixel

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            Parar();
            Point p = e.GetPosition(this);
            Direcoes(out _, out Vector3D cima, out Vector3D dir);
            double sx = (p.X - ActualWidth / 2) * Escala, sy = (ActualHeight / 2 - p.Y) * Escala;
            Point3D sob = alvo + dir * sx + cima * sy;   // ponto sob o cursor fica parado
            larg = Math.Max(raio * 0.002, Math.Min(raio * 200, larg * (e.Delta > 0 ? 1 / 1.2 : 1.2)));
            sx =(p.X - ActualWidth / 2) * Escala; sy = (ActualHeight / 2 - p.Y) * Escala;
            alvo = sob - dir * sx - cima * sy;
            AplicarCamera(); AgendarArestas();
            e.Handled = true;
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            Focus();
            Point p = e.GetPosition(this);
            if (e.ChangedButton == MouseButton.Middle && e.ClickCount == 2) { Enquadrar(); return; }
            bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            if (e.ChangedButton == MouseButton.Left && ferr == Ferramenta3D.Selecionar) { Parar(); caixaIni = p; moveu = false; CaptureMouse(); }   // janela/cruzamento
            else if ((e.ChangedButton == MouseButton.Middle && shift) || e.ChangedButton == MouseButton.Left) { Parar(); orbIni = p; cliqueIni = e.ChangedButton == MouseButton.Left ? p : (Point?)null; moveu = false; CaptureMouse(); }
            else if (e.ChangedButton == MouseButton.Middle || e.ChangedButton == MouseButton.Right) { Parar(); panIni = p; moveu = false; CaptureMouse(); }
        }

        protected override void OnMouseLeave(MouseEventArgs e) { mouseDentro = false; marca.Visibility = Visibility.Collapsed; sobre.InvalidateVisual(); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            Point p = e.GetPosition(this);
            mouseTela = p; mouseDentro = true;
            if (caixaIni != null) { if ((p - caixaIni.Value).Length > 4) moveu = true; sobre.InvalidateVisual(); return; }
            if (orbIni == null && panIni == null && Desenhando) { AtualizarCursor(); return; }
            if (orbIni != null)
            {
                Vector dv = p - orbIni.Value;
                if (!moveu && dv.Length < 3) return;
                moveu = true;
                az -= dv.X * 0.4; el = Math.Max(-90, Math.Min(90, el + dv.Y * 0.4));
                orbIni = p; AtualizarNome("Órbita"); AplicarCamera();
            }
            else if (panIni != null)
            {
                Vector dv = p - panIni.Value;
                if (dv.Length > 0) moveu = true;
                Direcoes(out _, out Vector3D cima, out Vector3D dir);
                alvo = alvo - dir * dv.X * Escala + cima * dv.Y * Escala;
                panIni = p; AplicarCamera();
            }
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            Point p = e.GetPosition(this);
            if (caixaIni != null && e.ChangedButton == MouseButton.Left)
            {
                Point a = caixaIni.Value; caixaIni = null; ReleaseMouseCapture();
                if (moveu) SelecionarCaixa(a, p, Keyboard.Modifiers); else SelecionarClique(p, Keyboard.Modifiers);
                sobre.InvalidateVisual();
                return;
            }
            bool clique = cliqueIni != null && !moveu && e.ChangedButton == MouseButton.Left;
            bool direito = panIni != null && !moveu && e.ChangedButton == MouseButton.Right;
            orbIni = null; panIni = null; cliqueIni = null;
            ReleaseMouseCapture();
            if (direito && inicio != null) { inicio = null; DinLimpar(); AtualizarCursor(); }   // botão direito termina a corrente (igual ao 2D)
            if (!clique) return;
            if (Desenhando) _ = ClicarTela(p);
            else SelecionarClique(p, Keyboard.Modifiers);   // Navegar também escolhe: a seleção (Selecao) é uma só para a edição
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
            return achado;
        }

        // ponto 3D -> pixel da vista (câmera ortográfica)
        public Point Tela(Point3D q)
        {
            Direcoes(out _, out Vector3D cima, out Vector3D dir);
            Vector3D r = q - alvo;
            return new Point(ActualWidth / 2 + Vector3D.DotProduct(r, dir) / Escala, ActualHeight / 2 - Vector3D.DotProduct(r, cima) / Escala);
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

    }
}
