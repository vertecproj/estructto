// Vista 3D do pórtico espacial (fase 4 de docs/UNIFICACAO.md), com Viewport3D nativo do WPF.
// HelixToolkit não foi usado: câmera ortográfica, órbita, pan, zoom e picking cabem em poucas linhas com o
// Viewport3D e evitam uma dependência nova (ver docs/VISTA3D.md).
// Entrada neutra (Modelo3D): nós {id, x, y, z, apoios[6]}, barras {id, ni, nj, seção, rot}, e opcionalmente
// N/V/M por barra, aproveitamento e deslocamentos nodais. Eixos globais: X e Y horizontais, Z para cima.
// Mouse igual ao da vista 2D (Vista.cs): roda = zoom no cursor, botão do meio ou direito arrastando = pan,
// duplo clique no meio = enquadrar. Órbita: botão esquerdo arrastando (ou Shift + meio). Clique = escolher.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Portico;
using Material = System.Windows.Media.Media3D.Material;

namespace NovoFtool
{
    public sealed class No3D
    {
        public int Id; public double X, Y, Z;
        public bool[] Apoios = new bool[6];   // ux, uy, uz, rx, ry, rz travados
        public double[] Desl;                 // ux, uy, uz (m); null = sem resultado
        public object Origem;                 // objeto do modelo de origem (No do 2D), devolvido no evento Escolheu
    }

    public sealed class Barra3D
    {
        public int Id, Ni, Nj; public string Secao = ""; public double Rot;   // rot (graus) gira o eixo local y em torno da barra
        public double[] N, V, M;   // valores em estações igualmente espaçadas de ni a nj (kN, kN·m); null = sem diagrama
        public double? Aprov;      // aproveitamento (1 = no limite); null = sem verificação
        public object Origem;
    }

    public sealed class Modelo3D
    {
        public List<No3D> Nos = new List<No3D>();
        public List<Barra3D> Barras = new List<Barra3D>();

        // Eixo local y da barra: a vertical global projetada no plano normal à barra (barra vertical: -X·sinal),
        // girado de rot graus em torno da barra. É o lado para onde os diagramas são desenhados.
        public static Vector3D EixoY(Vector3D e, double rot)
        {
            e.Normalize();
            Vector3D y = Math.Abs(e.Z) > 0.999 ? new Vector3D(-Math.Sign(e.Z), 0, 0) : new Vector3D(0, 0, 1) - e.Z * e;
            y.Normalize();
            if (rot == 0) return y;
            double r = rot * Math.PI / 180;
            return y * Math.Cos(r) + Vector3D.CrossProduct(e, y) * Math.Sin(r);
        }

        // Adaptador do Modelo 2D (Nucleo.cs): plano XY do 2D vira o plano XZ (Y para cima -> Z para cima).
        // ponytail: usa só o resultado atual de cada barra (b.Res); envoltória entra quando o motor 3D chegar.
        public static Modelo3D De(Modelo m, Func<Barra, Verif> verificar)
        {
            var r = new Modelo3D();
            foreach (No n in m.Nos)
                r.Nos.Add(new No3D
                {
                    Id = n.Id, X = n.X, Y = 0, Z = n.Y, Origem = n,
                    Apoios = new[] { n.ApoioX, false, n.ApoioY, false, n.ApoioGiro, false },
                    Desl = n.Res == null ? null : new[] { n.Res[0], 0, n.Res[1] },
                });
            const int est = 20;
            foreach (Barra b in m.Barras)
            {
                var b3 = new Barra3D { Id = b.Id, Ni = b.N1, Nj = b.N2, Secao = b.Secao ?? "", Origem = b };
                ResBarra rb = b.Res;
                if (rb != null)
                {
                    // o 2D desenha em (-S, C); o eixo y 3D pode apontar ao contrário (barra para -X): corrige o sinal
                    Vector3D y = EixoY(new Vector3D(rb.C, 0, rb.S), 0);
                    double k = y.X * -rb.S + y.Z * rb.C < 0 ? -1 : 1;
                    b3.N = new double[est + 1]; b3.V = new double[est + 1]; b3.M = new double[est + 1];
                    for (int i = 0; i <= est; i++)
                    {
                        double x = rb.L * i / est;
                        b3.N[i] = k * rb.N(x); b3.V[i] = k * rb.Q(x); b3.M[i] = -k * rb.M(x);   // momento do lado tracionado
                    }
                }
                if (verificar != null && rb != null)
                {
                    Verif v = verificar(b);
                    if (v != null && v.Aplicavel) b3.Aprov = v.Ap;
                }
                r.Barras.Add(b3);
            }
            return r;
        }
    }

    public enum Diagrama3D { Nenhum, N, V, M }
    public enum VistaPadrao { Topo, Frente, Lado, Iso }

    public class Vista3D : Grid
    {
        public event Action<object> Escolheu;   // No3D ou Barra3D (Origem aponta para o objeto do modelo)

        Modelo3D mod = new Modelo3D();
        public Diagrama3D Diagrama;
        public bool MostrarAproveitamento, MostrarDeformada, MostrarLegenda = true;   // legenda própria (a janela principal já tem a do HUD)
        public double EscalaDeformada = 1, EscalaDiagrama = 1;
        public object Selecionado;

        readonly Viewport3D vp = new Viewport3D { ClipToBounds = true, IsHitTestVisible = true };
        readonly OrthographicCamera cam = new OrthographicCamera { NearPlaneDistance = 0.001, FarPlaneDistance = double.PositiveInfinity };
        readonly ModelVisual3D cena = new ModelVisual3D();
        readonly Dictionary<Model3D, object> mapa = new Dictionary<Model3D, object>();
        readonly Sobre sobre;
        readonly TextBlock nomeVista;

        // câmera: azimute e elevação (graus), largura visível (m), alvo
        double az = -55, el = 28, larg = 10; Point3D alvo;
        double raio = 5;
        Camera3 de, para; readonly Stopwatch relogio = new Stopwatch(); bool animando;
        const double DuracaoMs = 300;   // GUIA-DESIGN: transição de câmera 250–400 ms, CubicEase EaseOut

        Point? panIni, orbIni, cliqueIni; bool moveu;

        struct Camera3 { public double Az, El, Larg; public Point3D Alvo; }

        public Vista3D()
        {
            Background = TemaPlant.BBgCanvas;
            Focusable = true;
            ClipToBounds = true;
            vp.Camera = cam;
            vp.Children.Add(cena);
            Children.Add(vp);
            sobre = new Sobre(this) { IsHitTestVisible = false };
            Children.Add(sobre);

            // seletor de vistas (canto superior direito; o HUD dos diagramas fica no esquerdo)
            var barra = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(12, 0, 0, 12) };
            var caixa = new Border { Child = barra, Background = TemaPlant.B(TemaPlant.CorCaixaHud), BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(3),
                                     HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 12, 12, 0) };
            foreach (var (txt, v, dica) in new[] { ("Topo", VistaPadrao.Topo, "Planta: olha de cima (Z para fora da tela)."), ("Frente", VistaPadrao.Frente, "Elevação XZ: a mesma vista do desenho 2D."),
                                                   ("Lado", VistaPadrao.Lado, "Elevação YZ."), ("Iso", VistaPadrao.Iso, "Vista isométrica.") })
            {
                var t = new TextBlock { Text = txt, Foreground = TemaPlant.BTexto2, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
                var bt = new Border { Child = t, Padding = new Thickness(9, 3, 9, 3), CornerRadius = new CornerRadius(4), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = dica };
                bt.MouseEnter += (s, e) => { bt.Background = TemaPlant.BSup3; t.Foreground = TemaPlant.BTexto1; };
                bt.MouseLeave += (s, e) => { bt.Background = Brushes.Transparent; t.Foreground = TemaPlant.BTexto2; };
                bt.MouseLeftButtonDown += (s, e) => { MostrarVista(v); e.Handled = true; };
                barra.Children.Add(bt);
            }
            nomeVista = new TextBlock { Foreground = TemaPlant.BTexto3, FontSize = 11, Margin = new Thickness(6, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
            barra.Children.Add(new Border { Width = 1, Background = TemaPlant.BBordaSutil, Margin = new Thickness(4, 3, 4, 3) });
            barra.Children.Add(nomeVista);
            Children.Add(caixa);
            AtualizarNome("Isométrica");
            SizeChanged += (s, e) => AplicarCamera();
        }

        // ================= dados =================

        public void Carregar(Modelo3D m, bool enquadrar)
        {
            mod = m ?? new Modelo3D();
            if (Selecionado != null && !mod.Nos.Any(n => n.Origem == Selecionado || n == Selecionado) && !mod.Barras.Any(b => b.Origem == Selecionado || b == Selecionado)) Selecionado = null;
            Reconstruir();
            if (enquadrar) Enquadrar(false);
        }

        No3D NoPorId(int id) { return mod.Nos.FirstOrDefault(n => n.Id == id); }
        static Point3D P(No3D n) { return new Point3D(n.X, n.Y, n.Z); }

        Rect3D Caixa()
        {
            if (mod.Nos.Count == 0) return new Rect3D(-5, -5, 0, 10, 10, 5);
            double x0 = mod.Nos.Min(n => n.X), y0 = mod.Nos.Min(n => n.Y), z0 = mod.Nos.Min(n => n.Z);
            return new Rect3D(x0, y0, z0, mod.Nos.Max(n => n.X) - x0, mod.Nos.Max(n => n.Y) - y0, mod.Nos.Max(n => n.Z) - z0);
        }

        // ================= cena =================

        static Material Mat(Color c, bool emissivo = false)
        {
            var g = new MaterialGroup();
            g.Children.Add(new DiffuseMaterial(new SolidColorBrush(c)));
            if (emissivo) g.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(c.A, (byte)(c.R / 4), (byte)(c.G / 4), (byte)(c.B / 4)))));
            g.Freeze();
            return g;
        }

        // Prisma de seção quadrada (lado 2h) de a até b.
        static void Prisma(MeshGeometry3D m, Point3D a, Point3D b, double h)
        {
            Vector3D e = b - a; if (e.Length < 1e-12) return; e.Normalize();
            Vector3D u = Vector3D.CrossProduct(e, Math.Abs(e.Z) > 0.9 ? new Vector3D(1, 0, 0) : new Vector3D(0, 0, 1)); u.Normalize();
            Vector3D w = Vector3D.CrossProduct(e, u);
            Vector3D[] cs = { (u + w) * h, (w - u) * h, (-u - w) * h, (u - w) * h };
            for (int i = 0; i < 4; i++)
            {
                Vector3D c1 = cs[i], c2 = cs[(i + 1) % 4];
                Quad(m, a + c1, b + c1, b + c2, a + c2);
            }
            Quad(m, a + cs[3], a + cs[2], a + cs[1], a + cs[0]);
            Quad(m, b + cs[0], b + cs[1], b + cs[2], b + cs[3]);
        }

        static void Quad(MeshGeometry3D m, Point3D p0, Point3D p1, Point3D p2, Point3D p3)
        {
            int i = m.Positions.Count;
            m.Positions.Add(p0); m.Positions.Add(p1); m.Positions.Add(p2); m.Positions.Add(p3);
            m.TriangleIndices.Add(i); m.TriangleIndices.Add(i + 1); m.TriangleIndices.Add(i + 2);
            m.TriangleIndices.Add(i); m.TriangleIndices.Add(i + 2); m.TriangleIndices.Add(i + 3);
        }

        static void Tri(MeshGeometry3D m, Point3D p0, Point3D p1, Point3D p2)
        {
            int i = m.Positions.Count;
            m.Positions.Add(p0); m.Positions.Add(p1); m.Positions.Add(p2);
            m.TriangleIndices.Add(i); m.TriangleIndices.Add(i + 1); m.TriangleIndices.Add(i + 2);
        }

        static void Cubo(MeshGeometry3D m, Point3D c, double h) { Prisma(m, c - new Vector3D(0, 0, h), c + new Vector3D(0, 0, h), h); }

        GeometryModel3D Geo(MeshGeometry3D m, Material mat, bool duasFaces = false)
        {
            var g = new GeometryModel3D(m, mat);
            if (duasFaces) g.BackMaterial = mat;
            return g;
        }

        Point3D Deformado(No3D n, double f)
        {
            if (n.Desl == null) return P(n);
            return new Point3D(n.X + f * n.Desl[0], n.Y + f * n.Desl[1], n.Z + f * n.Desl[2]);
        }

        static Color CorAprov(double ap)
        {
            int f = Vista.FaixaCalor(ap);
            return f == 3 ? TemaPlant.CorCalor4 : f == 2 ? TemaPlant.CorCalor3 : f == 1 ? TemaPlant.CorCalor2 : TemaPlant.CorCalor1;
        }

        public void Reconstruir()
        {
            mapa.Clear();
            var g = new Model3DGroup();
            g.Children.Add(new AmbientLight(Color.FromRgb(0x78, 0x7C, 0x86)));
            g.Children.Add(new DirectionalLight(Color.FromRgb(0x6C, 0x6C, 0x6C), new Vector3D(-0.4, 0.6, -0.7)));
            g.Children.Add(new DirectionalLight(Color.FromRgb(0x30, 0x30, 0x34), new Vector3D(0.5, -0.3, 0.4)));

            Rect3D cx = Caixa();
            raio = Math.Max(1, new Vector3D(cx.SizeX, cx.SizeY, cx.SizeZ).Length / 2);
            double h = raio * 0.006, hn = h * 2.0;   // meia espessura da barra e do nó

            bool def = MostrarDeformada && mod.Nos.Any(n => n.Desl != null);
            double dmax = def ? mod.Nos.Where(n => n.Desl != null).Max(n => Math.Sqrt(n.Desl[0] * n.Desl[0] + n.Desl[1] * n.Desl[1] + n.Desl[2] * n.Desl[2])) : 0;
            double fDef = dmax < 1e-15 ? 0 : 0.08 * 2 * raio / dmax * EscalaDeformada;

            // grade no plano do piso (z mínimo): referência de profundidade, discreta como a grade do 2D
            {
                double passo = Math.Pow(10, Math.Floor(Math.Log10(raio / 2))); if (raio / passo > 12) passo *= 2.5; if (raio / passo > 12) passo *= 2;
                double gx0 = Math.Floor((cx.X - raio * 0.3) / passo) * passo, gx1 = Math.Ceiling((cx.X + cx.SizeX + raio * 0.3) / passo) * passo;
                double gy0 = Math.Floor((cx.Y - raio * 0.3) / passo) * passo, gy1 = Math.Ceiling((cx.Y + cx.SizeY + raio * 0.3) / passo) * passo;
                double z = cx.Z, t = h * 0.2;
                var mg = new MeshGeometry3D();
                for (double x = gx0; x <= gx1 + 1e-9; x += passo) Quad(mg, new Point3D(x - t, gy0, z), new Point3D(x + t, gy0, z), new Point3D(x + t, gy1, z), new Point3D(x - t, gy1, z));
                for (double y = gy0; y <= gy1 + 1e-9; y += passo) Quad(mg, new Point3D(gx0, y - t, z), new Point3D(gx1, y - t, z), new Point3D(gx1, y + t, z), new Point3D(gx0, y + t, z));
                var mat = new EmissiveMaterial(TemaPlant.B(TemaPlant.CorGradeMaior)); mat.Freeze();
                g.Children.Add(Geo(mg, mat, true));
            }

            // barras (uma geometria por barra para o picking e a cor)
            foreach (Barra3D b in mod.Barras)
            {
                No3D a = NoPorId(b.Ni), c = NoPorId(b.Nj); if (a == null || c == null) continue;
                bool sel = b.Origem == Selecionado || b == Selecionado;
                Color cor = sel ? TemaPlant.Texto1   // branco: não colide com as cores do aproveitamento
                          : MostrarAproveitamento && b.Aprov.HasValue ? CorAprov(b.Aprov.Value)
                          : def || Diagrama != Diagrama3D.Nenhum ? TemaPlant.TextoMudo : TemaPlant.CorBarra;
                var m = new MeshGeometry3D();
                Prisma(m, P(a), P(c), (MostrarAproveitamento && b.Aprov.HasValue ? h * 1.6 : h) * (sel ? 1.8 : 1));
                var geo = Geo(m, Mat(cor, true));
                mapa[geo] = b; g.Children.Add(geo);
            }

            // nós e apoios
            var mApoio = new MeshGeometry3D();
            foreach (No3D n in mod.Nos)
            {
                var m = new MeshGeometry3D(); Cubo(m, P(n), hn);
                var geo = Geo(m, Mat(n.Origem == Selecionado || n == Selecionado ? TemaPlant.CorBarraSel : TemaPlant.Texto2, true));
                mapa[geo] = n; g.Children.Add(geo);
                if (!n.Apoios.Any(x => x)) continue;
                double s = raio * 0.035; Point3D p = P(n), bz = p - new Vector3D(0, 0, s * 1.4);
                if (n.Apoios[3] || n.Apoios[4] || n.Apoios[5])
                    Prisma(mApoio, bz, p - new Vector3D(0, 0, hn), s);   // engaste: bloco
                else
                {
                    Point3D[] q = { bz + new Vector3D(-s, -s, 0), bz + new Vector3D(s, -s, 0), bz + new Vector3D(s, s, 0), bz + new Vector3D(-s, s, 0) };
                    for (int i = 0; i < 4; i++) Tri(mApoio, p, q[i], q[(i + 1) % 4]);   // rótula: pirâmide
                    Quad(mApoio, q[3], q[2], q[1], q[0]);
                }
            }
            if (mApoio.Positions.Count > 0) g.Children.Add(Geo(mApoio, Mat(TemaPlant.CorApoio, true), true));

            // deformada (cordas entre nós deslocados; ponytail: sem a elástica dentro da barra)
            if (fDef > 0)
            {
                var m = new MeshGeometry3D();
                foreach (Barra3D b in mod.Barras)
                {
                    No3D a = NoPorId(b.Ni), c = NoPorId(b.Nj); if (a == null || c == null) continue;
                    Prisma(m, Deformado(a, fDef), Deformado(c, fDef), h * 1.2);
                }
                g.Children.Add(Geo(m, Mat(TemaPlant.CorDeformada, true)));
            }

            // diagramas N/V/M no plano (barra, eixo local y)
            if (Diagrama != Diagrama3D.Nenhum)
            {
                Func<Barra3D, double[]> val = b => Diagrama == Diagrama3D.N ? b.N : Diagrama == Diagrama3D.V ? b.V : b.M;
                double vmax = mod.Barras.Select(val).Where(v => v != null).SelectMany(v => v).Select(Math.Abs).DefaultIfEmpty(0).Max();
                double esc = vmax < 1e-9 ? 0 : 0.12 * 2 * raio / vmax * EscalaDiagrama;
                Color cPos = Diagrama == Diagrama3D.N ? TemaPlant.CorNormalPos : Diagrama == Diagrama3D.V ? TemaPlant.CorCortante : TemaPlant.CorMomento;
                Color cNeg = Diagrama == Diagrama3D.N ? TemaPlant.CorNormalNeg : Diagrama == Diagrama3D.V ? TemaPlant.CorCortanteNeg : TemaPlant.CorMomentoNeg;
                var mp = new MeshGeometry3D(); var mn = new MeshGeometry3D();
                if (esc > 0)
                    foreach (Barra3D b in mod.Barras)
                    {
                        double[] v = val(b); No3D a = NoPorId(b.Ni), c = NoPorId(b.Nj);
                        if (v == null || v.Length < 2 || a == null || c == null) continue;
                        Vector3D e = P(c) - P(a), y = Modelo3D.EixoY(e, b.Rot);
                        int k = v.Length - 1;
                        for (int i = 0; i < k; i++)
                        {
                            Point3D p0 = P(a) + e * i / k, p1 = P(a) + e * (i + 1) / k;
                            double v0 = v[i], v1 = v[i + 1];
                            if (v0 * v1 < 0)   // corta no zero para não misturar as cores
                            {
                                double t = v0 / (v0 - v1); Point3D pz = p0 + (p1 - p0) * t;
                                Tri(v0 > 0 ? mp : mn, p0, pz, p0 + y * v0 * esc);
                                Tri(v1 > 0 ? mp : mn, pz, p1, p1 + y * v1 * esc);
                            }
                            else Quad(v0 + v1 >= 0 ? mp : mn, p0, p1, p1 + y * v1 * esc, p0 + y * v0 * esc);
                        }
                    }
                foreach (var (m, c) in new[] { (mp, cPos), (mn, cNeg) })
                    if (m.Positions.Count > 0)
                    {
                        var mat = new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(0x8C, c.R, c.G, c.B))); mat.Freeze();
                        g.Children.Add(Geo(m, mat, true));
                    }
            }

            cena.Content = g;
            sobre.InvalidateVisual();
        }

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
            sobre.InvalidateVisual();
        }

        void IrPara(Camera3 c, bool animar)
        {
            if (!animar || !IsVisible) { Parar(); az = c.Az; el = c.El; larg = c.Larg; alvo = c.Alvo; AplicarCamera(); return; }
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
            if (t >= 1) Parar();
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
            AplicarCamera();
            e.Handled = true;
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            Focus();
            Point p = e.GetPosition(this);
            if (e.ChangedButton == MouseButton.Middle && e.ClickCount == 2) { Enquadrar(); return; }
            bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            if ((e.ChangedButton == MouseButton.Middle && shift) || e.ChangedButton == MouseButton.Left) { Parar(); orbIni = p; cliqueIni = e.ChangedButton == MouseButton.Left ? p : (Point?)null; moveu = false; CaptureMouse(); }
            else if (e.ChangedButton == MouseButton.Middle || e.ChangedButton == MouseButton.Right) { Parar(); panIni = p; moveu = false; CaptureMouse(); }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            Point p = e.GetPosition(this);
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
            bool clique = cliqueIni != null && !moveu && e.ChangedButton == MouseButton.Left;
            orbIni = null; panIni = null; cliqueIni = null;
            ReleaseMouseCapture();
            if (clique) Escolher(Acertar(p));
        }

        public object Acertar(Point p)
        {
            object achado = null;
            VisualTreeHelper.HitTest(vp, null, r =>
            {
                if (r is RayMeshGeometry3DHitTestResult h && mapa.TryGetValue(h.ModelHit, out object o))
                {
                    if (o is No3D || achado == null) achado = o;   // nó tem prioridade sobre a barra que passa por ele
                    return o is No3D ? HitTestResultBehavior.Stop : HitTestResultBehavior.Continue;
                }
                return HitTestResultBehavior.Continue;
            }, new PointHitTestParameters(TranslatePoint(p, vp)));
            return achado;
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
            base.OnKeyDown(e);
        }

        // ================= sobreposição 2D: gizmo dos eixos e legenda do aproveitamento =================

        sealed class Sobre : FrameworkElement
        {
            readonly Vista3D v;
            public Sobre(Vista3D v) { this.v = v; }
            static readonly Typeface Tf = new Typeface(TemaPlant.Fonte, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

            FormattedText Txt(string s, Brush b, double tam) { return new FormattedText(s, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Tf, tam, b, 1.0); }

            protected override void OnRender(DrawingContext dc)
            {
                v.Direcoes(out Vector3D d, out Vector3D cima, out Vector3D dir);
                // gizmo: canto inferior esquerdo
                Point o = new Point(50, ActualHeight - 50);
                dc.DrawEllipse(TemaPlant.B(TemaPlant.CorCaixaHud), new Pen(TemaPlant.BBordaSutil, 1), o, 38, 38);
                var eixos = new[] { (new Vector3D(1, 0, 0), TemaPlant.Erro, "X"), (new Vector3D(0, 1, 0), TemaPlant.Sucesso, "Y"), (new Vector3D(0, 0, 1), TemaPlant.Realce, "Z") };
                foreach (var (ax, cor, nome) in eixos.OrderBy(t => Vector3D.DotProduct(t.Item1, d)))   // de trás para a frente
                {
                    Vector s = new Vector(Vector3D.DotProduct(ax, dir), -Vector3D.DotProduct(ax, cima)) * 24;
                    var br = TemaPlant.B(cor);
                    dc.DrawLine(new Pen(br, 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, o, o + s);
                    if (s.Length < 3) { dc.DrawEllipse(br, null, o, 3, 3); }
                    var ft = Txt(nome, br, 11);
                    Vector u = s.Length < 1e-6 ? new Vector(0, 0) : s / s.Length;
                    dc.DrawText(ft, o + s + u * 8 - new Vector(ft.Width / 2, ft.Height / 2));
                }

                // legenda do aproveitamento: canto inferior direito
                if (v.MostrarLegenda && v.MostrarAproveitamento && v.mod.Barras.Any(b => b.Aprov.HasValue))
                {
                    var itens = new[] { (TemaPlant.CorCalor1, "≤ 75%"), (TemaPlant.CorCalor2, "75–90%"), (TemaPlant.CorCalor3, "90–100%"), (TemaPlant.CorCalor4, "> 100%") };
                    double w = 112, h = 26 + itens.Length * 18, x = ActualWidth - w - 14, y = ActualHeight - h - 14;
                    dc.DrawRoundedRectangle(TemaPlant.B(TemaPlant.CorCaixaHud), new Pen(TemaPlant.BBordaSutil, 1), new Rect(x, y, w, h), 6, 6);
                    dc.DrawText(Txt("Aproveitamento", TemaPlant.BTexto2, 11), new Point(x + 10, y + 7));
                    for (int i = 0; i < itens.Length; i++)
                    {
                        double yy = y + 26 + i * 18;
                        dc.DrawRoundedRectangle(TemaPlant.B(itens[i].Item1), null, new Rect(x + 10, yy + 3, 18, 8), 2, 2);
                        dc.DrawText(Txt(itens[i].Item2, TemaPlant.BTexto1, 11), new Point(x + 36, yy - 1));
                    }
                }
                if (v.mod.Nos.Count == 0)
                {
                    var ft = Txt("Modelo vazio: desenhe barras na vista 2D.", TemaPlant.BTexto3, 12);
                    dc.DrawText(ft, new Point((ActualWidth - ft.Width) / 2, ActualHeight / 2));
                }
            }
        }
    }

    // Alternância 2D/3D na janela principal (botão "3D" da aba Vista).
    public partial class MainWindow
    {
        internal Vista3D vista3D;
        public bool Em3D { get { return vista3D != null && vista3D.Visibility == Visibility.Visible; } }

        public void AlternarVista3D() { MostrarVista3D(!Em3D); }

        public void MostrarVista3D(bool ligar)
        {
            if (ligar && vista3D == null)
            {
                vista3D = new Vista3D { Visibility = Visibility.Collapsed, MostrarLegenda = false };
                GridVista.Children.Insert(GridVista.Children.IndexOf(Vista) + 1, vista3D);   // acima do 2D, abaixo do HUD e avisos
                vista3D.Escolheu += o =>
                {
                    if (Esp != null) return;   // modelo espacial: DocumentoEspacial.cs trata a escolha
                    object alvo = o is No3D n ? n.Origem : o is Barra3D b ? b.Origem : null;
                    Vista.Sel.Clear();
                    if (alvo != null) Vista.Sel.Add(alvo);
                    SelecaoMudou();
                };
            }
            if (vista3D == null || (!ligar && Esp != null)) return;   // modelo espacial: sempre 3D
            bool primeira = ligar && vista3D.Visibility != Visibility.Visible;
            vista3D.Visibility = ligar ? Visibility.Visible : Visibility.Collapsed;
            Vista.Visibility = ligar ? Visibility.Hidden : Visibility.Visible;
            if (ligar) { Atualizar3D(primeira); vista3D.Focus(); } else Vista.Focus();
        }

        // Recarrega a vista 3D a partir do modelo 2D e do diagrama ativo (chamado depois de cada cálculo).
        internal void Atualizar3D(bool enquadrar = false)
        {
            if (Esp != null) { AtualizarEspacial3D(enquadrar); return; }
            if (!Em3D) return;
            Diagrama d = Vista.Diag;
            vista3D.Diagrama = !Vista.Resolvido ? Diagrama3D.Nenhum : d == Diagrama.Normal ? Diagrama3D.N : d == Diagrama.Cortante ? Diagrama3D.V : d == Diagrama.Momento ? Diagrama3D.M : Diagrama3D.Nenhum;
            vista3D.MostrarDeformada = Vista.Resolvido && d == Diagrama.Deformada;
            vista3D.MostrarAproveitamento = Vista.Resolvido && d == Diagrama.Verificacao;
            vista3D.EscalaDeformada = Vista.EscalaDef; vista3D.EscalaDiagrama = Vista.EscalaDiag;
            vista3D.Selecionado = Vista.Sel.Count == 1 ? Vista.Sel[0] : null;
            vista3D.Carregar(Modelo3D.De(M, Vista.Resolvido ? Vista.Verificar : null), enquadrar);
        }
    }
}
