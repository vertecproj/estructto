// 3D do projetista de ligações (aba "3D", quadrante 3D de "Vistas" e miniaturas da biblioteca): cena sólida com z-buffer num
// Viewport3D do WPF. Antes era um pintor por camadas (perfis, chapas, parafusos sempre por cima, só cabeça ou só porca pelo lado do
// observador): ao girar os parafusos flutuavam sobre as chapas e trocavam de cabeça para porca de repente (docs/v3/W7-3D.md).
// As malhas são as do Render3D principal (Render3DGeometria: MalhaDx.Chapa, Parafuso, Cordao, Extrudar com a boca de peixe),
// no referencial local da ligação (mm). Uma GeometryModel3D por peça (picking: Tag = campo do motor). Girar só move a câmera;
// a cena é reconstruída só quando a geometria muda (ou a cada quadro da animação de troca).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Color = System.Windows.Media.Color;
using Contorno = NovoFtool.GeometriaPerfis.Contorno;

namespace NovoFtool
{
    public static class Cena3DLig
    {
        // Tipo: perfil, chapa, tubo, parafuso, solda; Indice na lista de origem da GeoLig
        public sealed class Peca { public GeometryModel3D M; public string Tag = "", Tipo = ""; public int Indice; }

        public const int LadosTubo = 96;   // curva lisa na sela

        public static Model3DGroup Montar(GeoLig g, List<Peca> pecas)
        {
            var grupo = new Model3DGroup();
            void Add(MalhaDx m, Color c, string tag, string tipo, int i)
            {
                if (m.Vazia) return;
                var mat = new DiffuseMaterial(new SolidColorBrush(c)); mat.Freeze();
                var gm = new GeometryModel3D(Malha(m), mat); gm.Freeze();   // sem BackMaterial: faces encostadas têm normais opostas e o descarte separa
                grupo.Children.Add(gm);
                pecas?.Add(new Peca { M = gm, Tag = tag ?? "", Tipo = tipo, Indice = i });
            }
            Color Cor(bool falha, Color c) => falha ? TemaPlant.Erro : c;
            for (int i = 0; i < g.Pecas.Count; i++)
            {
                var pc = g.Pecas[i]; var m = new MalhaDx(); Prisma(m, pc);
                Add(m, Cor(pc.Falha, pc.Camada == 0 ? TemaPlant.LigPerfil3D : TemaPlant.LigChapa3D), pc.Tag, pc.Camada == 0 ? "perfil" : "chapa", i);
            }
            for (int i = 0; i < g.Tubos.Count; i++) { var m = new MalhaDx(); Tubo(m, g.Tubos[i]); Add(m, Cor(g.Tubos[i].Falha, TemaPlant.LigPerfil3D), g.Tubos[i].Tag, "tubo", i); }
            for (int i = 0; i < g.Parafs.Count; i++)
            {
                var b = g.Parafs[i]; var m = new MalhaDx(); Parafuso(m, b);
                Vector3D e = b.E; e.Normalize();
                // arruelas (raio 1,05·d, o resto ≤ 0,95·d) num tom mais escuro: cabeça, arruela e chapa não se fundem numa mancha
                var (arr, resto) = Separar(m, t => t.Max(p => { Vector3D r = new Point3D(p.X, p.Y, p.Z) - b.O; return (r - e * Vector3D.DotProduct(r, e)).Length; }) > 1.0 * b.D);
                Color c = Cor(b.Falha, TemaPlant.Realce);
                Add(resto, c, b.Tag, "parafuso", i);
                Add(arr, Color.FromRgb((byte)(c.R * 0.62), (byte)(c.G * 0.62), (byte)(c.B * 0.62)), b.Tag, "arruela", i);
            }
            for (int i = 0; i < g.Soldas.Count; i++) { var m = new MalhaDx(); Cordao(m, g.Soldas[i]); Add(m, Cor(g.Soldas[i].Falha, TemaPlant.LigSolda), g.Soldas[i].Tag, "solda", i); }
            grupo.Freeze();
            return grupo;
        }

        // divide a malha por triângulo (vértices copiados): (os que passam no teste, os outros)
        static (MalhaDx sim, MalhaDx nao) Separar(MalhaDx m, Func<System.Numerics.Vector3[], bool> teste)
        {
            var r = (sim: new MalhaDx(), nao: new MalhaDx());
            for (int k = 0; k + 2 < m.T.Count; k += 3)
            {
                var t = new[] { m.P[m.T[k]], m.P[m.T[k + 1]], m.P[m.T[k + 2]] };
                var d = teste(t) ? r.sim : r.nao; int i0 = d.P.Count;
                for (int q = 0; q < 3; q++) { d.P.Add(t[q]); d.N.Add(m.N[m.T[k + q]]); d.C.Add(m.C[m.T[k + q]]); d.T.Add(i0 + q); }
            }
            return r;
        }

        // Arestas de chapas e perfis como prismas finos de seção quadrada (o Viewport3D não tem linhas): largura em mm ≈ 1 px na
        // escala atual; ficam no z-buffer como as peças (aresta escondida fica escondida).
        public static GeometryModel3D Arestas(GeoLig g, double largura)
        {
            var lin = new MalhaDx();
            foreach (var pc in g.Pecas) Prisma(lin, pc, true);
            var m = new MalhaDx(); double h = largura / 2;
            for (int k = 0; k + 1 < lin.L.Count; k += 2)
            {
                Point3D a = new Point3D(lin.L[k].X, lin.L[k].Y, lin.L[k].Z), b = new Point3D(lin.L[k + 1].X, lin.L[k + 1].Y, lin.L[k + 1].Z);
                Vector3D e = b - a; if (e.Length < 1e-6) continue; e.Normalize();
                Vector3D u = Vector3D.CrossProduct(e, Math.Abs(e.Z) < 0.9 ? new Vector3D(0, 0, 1) : new Vector3D(1, 0, 0)); u.Normalize();
                Vector3D v = Vector3D.CrossProduct(e, u);
                Vector3D[] c = { (u + v) * h, (v - u) * h, (-u - v) * h, (u - v) * h };
                for (int q = 0; q < 4; q++) { var c0 = c[q]; var c1 = c[(q + 1) % 4]; m.QuadN(a + c0, a + c1, b + c1, b + c0, c0 + c1, default); }
            }
            if (m.Vazia) return null;
            var mat = new DiffuseMaterial(new SolidColorBrush(TemaPlant.BgCanvas)); mat.Freeze();
            var gm = new GeometryModel3D(Malha(m), mat); gm.Freeze();
            return gm;
        }

        static MeshGeometry3D Malha(MalhaDx m)
        {
            var pos = new Point3DCollection(m.P.Count); var nor = new Vector3DCollection(m.N.Count);
            foreach (var p in m.P) pos.Add(new Point3D(p.X, p.Y, p.Z));
            foreach (var v in m.N) nor.Add(new Vector3D(v.X, v.Y, v.Z));
            var mg = new MeshGeometry3D { Positions = pos, Normals = nor, TriangleIndices = new Int32Collection(m.T) };
            mg.Freeze();
            return mg;
        }

        // prisma da GeoLig (base no plano médio, ±T/2 em N) pela chapa do Render3D (tampas por orelhas: alma com cope serve)
        static void Prisma(MalhaDx m, GeoLig.Prisma pc, bool soArestas = false)
        {
            var b = pc.Base; Vector3D n = pc.N;
            if (b == null || b.Length < 3 || n.Length < 1e-9 || pc.T <= 0) return;
            n.Normalize();
            Vector3D u = new Vector3D();
            for (int k = 0; k < b.Length && u.Length < 1e-6; k++) { u = b[(k + 1) % b.Length] - b[k]; u -= n * Vector3D.DotProduct(u, n); }
            if (u.Length < 1e-6) return;
            u.Normalize(); Vector3D w = Vector3D.CrossProduct(n, u);
            var ct = b.Select(p => new Point(Vector3D.DotProduct(p - b[0], u), Vector3D.DotProduct(p - b[0], w))).ToArray();
            m.Chapa(b[0] - n * pc.T / 2, u, w, n, ct, pc.T, default, soArestas, !soArestas);
        }

        // parafuso completo (Render3DGeometria.Parafuso: cabeça + arruela na face de entrada O, fuste, arruela + porca depois da pega,
        // ponta passando) com o sextavado alinhado à aresta da chapa, e a rosca aparente na ponta (anéis no passo ~0,125·d)
        public static void Parafuso(MalhaDx m, GeoLig.Paraf b)
        {
            if (b.D <= 0 || b.E.Length < 1e-9) return;
            Vector3D e = b.E; e.Normalize();
            Render3DGeometria.Parafuso(m, b.O, e, b.D, b.L, default, b.Pega, b.Ref);
            double d = b.D, f = Render3DGeometria.FolgaParafuso * d, pe = Render3DGeometria.PegaEfetiva(d, b.L, b.Pega);
            double s0 = pe + f + 0.14 * d + 0.8 * d, s1 = Math.Max(b.L, s0 + 0.25 * d);
            for (double s = s0 + 0.06 * d; s + 0.05 * d < s1; s += 0.125 * d)
                m.Tubo(b.O + e * s, b.O + e * (s + 0.05 * d), 0.535 * d, 10, true, default);
        }

        // ponta do fuste (base da rosca) e extensão: o teste de ancoragem confere a malha contra isto
        public static (double ini, double fim) Extensao(GeoLig.Paraf b)
        {
            double d = b.D, f = Render3DGeometria.FolgaParafuso * d, pe = Render3DGeometria.PegaEfetiva(d, b.L, b.Pega);
            return (-(f + 0.14 * d + 0.65 * d), Math.Max(b.L, pe + f + 0.14 * d + 0.8 * d + 0.25 * d));
        }

        // filete: prisma triangular ao longo do cordão, assentado como no Render3D (Ligacao3D.Apoio guardado na GeoLig)
        static void Cordao(MalhaDx m, GeoLig.Cordao s)
        {
            var face = s.Face;
            Render3DGeometria.Cordao(m, s.P, s.Perna, default, s.Ax ?? new Vector3D(1, 0, 0), s.Lados,
                face == null ? null : (pa, pb) => Render3DGeometria.Intervalos(face.Q((Vector3D)pa), face.Q((Vector3D)pb), face.Contorno));
        }

        // casca do tubo (anel × comprimento) com a ponta cortada pelo cilindro do receptor (Render3DGeometria.Extrudar, Corte.Raio)
        static void Tubo(MalhaDx m, GeoLig.Tubo t)
        {
            var (u, w) = t.Base();
            double R = t.D / 2, r = Math.Max(0, R - t.T);
            Point[] Circ(double rr) => Enumerable.Range(0, LadosTubo).Select(k => new Point(rr * Math.Cos(2 * Math.PI * k / LadosTubo), rr * Math.Sin(2 * Math.PI * k / LadosTubo))).ToArray();
            Point[] ext = Circ(R), inn = Circ(r);
            var f = new Contorno { Forma = "TC", H = t.D, B = t.D, Tw = t.T };
            f.Laco(ext, true);
            if (r > 1e-6) f.Laco(inn.Reverse().ToArray(), true);
            for (int i = 0; i < LadosTubo; i++) { int j = (i + 1) % LadosTubo; f.Tampa.AddRange(new[] { ext[i], ext[j], inn[j], ext[i], inn[j], inn[i] }); }
            var ca = t.Corta ? new Corte { P = t.O, N = t.E, Raio = t.CR, EixoO = t.CO, EixoA = t.CE } : null;
            Render3DGeometria.Extrudar(m, f, t.O, t.O + t.E * t.L, u, w, ca, null, default, false);
        }
    }

    public partial class DesenhoLigacao
    {
        Viewport3D vp; ModelVisual3D cena, arestas; double arestasMm; OrthographicCamera cam; DirectionalLight luzChave, luzEnch; Sobreposicao sobre;
        readonly List<Cena3DLig.Peca> pecas3D = new List<Cena3DLig.Peca>();
        GeoLig construida; Point3D alvo; double largura = 600, zoom = 1;
        const double Distancia = 20000;   // mm do alvo até a câmera (ortográfica: só a profundidade)

        public double UltimaReconstrucaoMs { get; private set; }
        public int Reconstrucoes { get; private set; }
        public IReadOnlyList<Cena3DLig.Peca> Pecas3D => pecas3D;
        public Func<Point3D, Point> Projecao => proj;

        sealed class Sobreposicao : FrameworkElement
        {
            readonly DesenhoLigacao d;
            public Sobreposicao(DesenhoLigacao d) { this.d = d; IsHitTestVisible = false; }
            protected override void OnRender(DrawingContext dc) => d.DesenharSobre(dc);
        }

        void Iniciar3D()
        {
            cam = new OrthographicCamera { NearPlaneDistance = 1, FarPlaneDistance = 2 * Distancia };
            luzChave = new DirectionalLight(TemaPlant.CorLuzChave3D, new Vector3D(0, 0, -1));
            luzEnch = new DirectionalLight(TemaPlant.CorLuzEnch3D, new Vector3D(0, 0, 1));
            cena = new ModelVisual3D(); arestas = new ModelVisual3D();
            vp = new Viewport3D { Camera = cam, ClipToBounds = true, IsHitTestVisible = !Miniatura };
            var luzes = new Model3DGroup(); luzes.Children.Add(new AmbientLight(TemaPlant.CorLuzAmb3D)); luzes.Children.Add(luzChave); luzes.Children.Add(luzEnch);
            vp.Children.Add(new ModelVisual3D { Content = luzes });
            vp.Children.Add(cena); vp.Children.Add(arestas);
            sobre = new Sobreposicao(this);
            AddVisualChild(vp); AddVisualChild(sobre);
        }

        protected override int VisualChildrenCount => vp == null ? 0 : 2;
        protected override Visual GetVisualChild(int i) => i == 0 ? vp : (Visual)sobre;
        protected override Size MeasureOverride(Size s) { vp?.Measure(s); sobre?.Measure(s); return new Size(); }
        protected override Size ArrangeOverride(Size s)
        {
            if (vp != null) { vp.Arrange(new Rect(s)); sobre.Arrange(new Rect(s)); AtualizarCamera(s); }
            return s;
        }

        void ProgMudou() { if (Vista == VistaLig.Iso) Reconstruir(); }

        void Reconstruir()
        {
            if (vp == null) return;
            var g = geo;
            if (g != null && anterior != null && Prog < 1) g = GeoLig.Misturar(anterior, geo, Prog) ?? geo;   // estrutura diferente: troca direto
            if (ReferenceEquals(g, construida)) return;
            var sw = Stopwatch.StartNew();
            pecas3D.Clear();
            cena.Content = g == null ? null : Cena3DLig.Montar(g, pecas3D);
            construida = g; arestasMm = 0;
            if (g != null) Enquadrar(g);
            AtualizarCamera();
            UltimaReconstrucaoMs = sw.Elapsed.TotalMilliseconds; Reconstrucoes++;
        }

        // alvo e largura pela ligação (chapas, parafusos, soldas, tubo junto ao nó): fixos ao girar (a vista não "respira")
        void Enquadrar(GeoLig g)
        {
            var f = g.Foco().ToList(); if (f.Count == 0) f = g.Pontos().ToList();
            if (f.Count == 0) return;
            double x0 = f.Min(p => p.X), x1 = f.Max(p => p.X), y0 = f.Min(p => p.Y), y1 = f.Max(p => p.Y), z0 = f.Min(p => p.Z), z1 = f.Max(p => p.Z);
            alvo = new Point3D((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2);
            double diag = new Vector3D(x1 - x0, y1 - y0, z1 - z0).Length;
            largura = Math.Max(diag * (Miniatura ? 1.5 : 2.3), Miniatura ? 200 : 300);
        }

        void AtualizarCamera() => AtualizarCamera(new Size(ActualWidth, ActualHeight));
        void AtualizarCamera(Size s)
        {
            if (cam == null) return;
            var (R, U, F) = Base();
            double W = Math.Max(1, s.Width), H = Math.Max(1, s.Height);
            double larg = largura / zoom * Math.Max(1, W / H);   // a ligação cabe na menor dimensão
            cam.Width = larg; cam.Position = alvo + F * Distancia; cam.LookDirection = -F; cam.UpDirection = U;
            Vector3D k = -F - U * 0.6 + R * 0.45; k.Normalize(); luzChave.Direction = k;   // luz chave do alto à esquerda, atrás do observador
            Vector3D e = -F * 0.3 + U * 0.5 - R * 0.8; e.Normalize(); luzEnch.Direction = e;
            double esc = W / larg; Point3D a = alvo;
            double mm = 1.1 / esc;   // ~1 px; refeitas só quando o zoom muda bastante (girar não refaz)
            if (construida != null && !Miniatura && (arestasMm <= 0 || mm / arestasMm > 1.3 || mm / arestasMm < 0.77)) { arestas.Content = Cena3DLig.Arestas(construida, mm); arestasMm = mm; }
            else if (construida == null) arestas.Content = null;
            proj = p => new Point(W / 2 + Vector3D.DotProduct(p - a, R) * esc, H / 2 - Vector3D.DotProduct(p - a, U) * esc);
            bs = (R, U, F); escUlt = esc;
            sobre?.InvalidateVisual();
        }

        // capturas/testes: ângulo da câmera (yaw, pitch em graus, como o arrasto)
        public void Girar(double yawGraus, double pitchGraus) { yaw = yawGraus; pitch = Math.Max(-80, Math.Min(85, pitchGraus)); AtualizarCamera(); }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            if (Vista != VistaLig.Iso || Miniatura) return;
            zoom = Math.Max(0.3, Math.Min(12, zoom * Math.Pow(1.15, e.Delta / 120.0)));
            AtualizarCamera(); e.Handled = true;
        }

        // peça mais perto do olho sob o ponto (hit-test do WPF: z real)
        public Cena3DLig.Peca Peca3DEm(Point p)
        {
            if (vp == null || cena.Content == null) return null;
            Cena3DLig.Peca r = null;
            VisualTreeHelper.HitTest(vp, null, h =>
            {
                if (h is RayMeshGeometry3DHitTestResult rm && pecas3D.FirstOrDefault(q => ReferenceEquals(q.M, rm.ModelHit)) is Cena3DLig.Peca pc) { r = pc; return HitTestResultBehavior.Stop; }
                return HitTestResultBehavior.Continue;
            }, new PointHitTestParameters(p));
            return r;
        }
        string PecaEm3D(Point p) { var pc = Peca3DEm(p); return pc == null || pc.Tag == "" ? null : pc.Tag; }

        void DesenharSobre(DrawingContext dc)
        {
            if (Miniatura || geo == null) return;
            if (Titulo != null) dc.DrawText(Txt(Titulo.ToUpperInvariant(), 10.5, TemaPlant.BTexto2, FontWeights.SemiBold), new Point(12, 10));
            var (R, U, F) = bs;
            Triedro(dc, R, U, F, new Point(40, ActualHeight - 34));
        }
    }
}
