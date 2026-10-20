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
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using System.Text.Json.Nodes;
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
        public double RotPerfil;   // giro extra (graus) só do desenho do perfil no modo Realista (2D: perfil deitado = 90)
        public object Origem;
    }

    // Forma real da seção para o modo Realista: I, U, L, TR (tubo retangular/caixão), TC (tubo circular), RET, CIRC.
    // H = altura (no eixo local y da Vista3D = z local do motor), B = largura, Tw/Tf = espessuras (tubos: Tw), m.
    public sealed class Perfil3D
    {
        public string Forma = "RET"; public double H, B, Tw, Tf;
        public bool Concreto;   // ponytail: seção maciça (RET/CIRC) = concreto; ler o material da barra se aparecer aço maciço

        static Perfil3D DeAI(double a, double i)   // caixa equivalente: mesma área e mesma inércia forte
        {
            if (!(a > 0) || !(i > 0)) return null;
            double h = Math.Sqrt(12 * i / a);
            return new Perfil3D { H = h, B = a / h };
        }

        // props de uma seção do motor (secoes.py: tipo, h, b, tw, tf, t...)
        public static Perfil3D DoMotor(JsonObject s)
        {
            var p = s?["props"] as JsonObject;
            double V(JsonObject o, string k) { try { return o?[k] is JsonValue v && v.TryGetValue(out double d) ? d : 0; } catch { return 0; } }
            double h = V(p, "h"), b = V(p, "b"), tw = V(p, "tw"), tf = V(p, "tf"), t = V(p, "t");
            switch ((string)p?["tipo"])
            {
                case "I": return new Perfil3D { Forma = "I", H = h, B = b, Tw = tw, Tf = tf };
                case "U": case "Ue": return new Perfil3D { Forma = "U", H = h, B = b, Tw = tw > 0 ? tw : t, Tf = tf > 0 ? tf : t };   // ponytail: Ue sem os enrijecedores
                case "L": return new Perfil3D { Forma = "L", H = h, B = b, Tw = t, Tf = t };
                case "tubo_retangular": return new Perfil3D { Forma = "TR", H = h, B = b, Tw = t };
                case "tubo_circular": return new Perfil3D { Forma = "TC", H = h, B = h, Tw = t };
                case "retangular": return new Perfil3D { Forma = "RET", H = h, B = b, Concreto = true };
                case "circular": return new Perfil3D { Forma = "CIRC", H = h, B = h, Concreto = true };
                case "dupla":
                    if ((string)p["composicao"]?["arranjo"] == "caixao") return new Perfil3D { Forma = "TR", H = h, B = b, Tw = tw > 0 ? tw : t };
                    return new Perfil3D { Forma = "I", H = h, B = b, Tw = 2 * (tw > 0 ? tw : t), Tf = tf > 0 ? tf : t };   // ponytail: 2L costas com costas vira I
            }
            return DeAI(V(p, "A") > 0 ? V(p, "A") : V(s, "A"), V(p, "Ix") > 0 ? V(p, "Ix") : V(s, "Iy"));
        }

        // perfil do catálogo do Estructto (Nucleo.cs, perfis-*.csv)
        public static Perfil3D DoCatalogo(Secao s)
        {
            if (s == null) return null;
            switch (s.Forma)
            {
                case "I": case "U": return new Perfil3D { Forma = s.Forma, H = s.H, B = s.Bf, Tw = s.Tw, Tf = s.Tf };
                case "L": return new Perfil3D { Forma = "L", H = s.H, B = s.Bf > 0 ? s.Bf : s.H, Tw = s.Tw, Tf = s.Tw };
                case "TR": return new Perfil3D { Forma = "TR", H = s.H, B = s.Bf > 0 ? s.Bf : s.H, Tw = s.Tw };
                case "TC": return new Perfil3D { Forma = "TC", H = s.H, B = s.H, Tw = s.Tw };
                case "RET": return new Perfil3D { Forma = "RET", H = s.H, B = s.Bf, Concreto = true };
                case "CIRC": return new Perfil3D { Forma = "CIRC", H = s.H, B = s.H, Concreto = true };
            }
            return DeAI(s.A, s.Ix);
        }
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
                var b3 = new Barra3D { Id = b.Id, Ni = b.N1, Nj = b.N2, Secao = b.Secao ?? "", Origem = b, RotPerfil = b.EixoFraco ? 90 : 0 };
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
        public bool Realista;                    // perfis reais, materiais, sombra e arestas (Config.Realista3D)
        public Func<string, Perfil3D> Perfil;    // nome da seção -> forma real (null/desconhecida = caixa fina)
        readonly Dictionary<string, Forma> formas = new Dictionary<string, Forma>();
        readonly Dictionary<Model3D, (int[] Ini, object[] Obj)> faixas = new Dictionary<Model3D, (int[], object[])>();   // malhas juntadas: vértice inicial de cada barra
        double largArestas = 1;                  // largura de tela com que as arestas foram feitas
        readonly DispatcherTimer timerArestas = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };

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
            SizeChanged += (s, e) => { AplicarCamera(); AgendarArestas(); };
            // arestas têm ~1 px: depois do zoom, refaz a cena se a escala mudou bastante
            timerArestas.Tick += (s, e) =>
            {
                timerArestas.Stop();
                if (Realista && Math.Abs(Math.Log(LarguraAresta() / largArestas)) > 0.3) Reconstruir();
            };
        }

        void AgendarArestas() { if (Realista) { timerArestas.Stop(); timerArestas.Start(); } }

        // ================= dados =================

        public void Carregar(Modelo3D m, bool enquadrar)
        {
            mod = m ?? new Modelo3D();
            formas.Clear();   // as seções podem ter mudado
            nosPorId = new Dictionary<int, No3D>();
            foreach (No3D n in mod.Nos) if (!nosPorId.ContainsKey(n.Id)) nosPorId[n.Id] = n;
            if (Selecionado != null && !mod.Nos.Any(n => n.Origem == Selecionado || n == Selecionado) && !mod.Barras.Any(b => b.Origem == Selecionado || b == Selecionado)) Selecionado = null;
            Reconstruir();
            if (enquadrar) Enquadrar(false);
        }

        Dictionary<int, No3D> nosPorId = new Dictionary<int, No3D>();
        No3D NoPorId(int id) { return nosPorId.TryGetValue(id, out No3D n) ? n : null; }
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

        // sem seleção nada está escolhido (Origem null == Selecionado null pintava tudo)
        bool EhSel(object o, object origem) { return Selecionado != null && (origem == Selecionado || o == Selecionado); }

        public void Reconstruir()
        {
            mapa.Clear(); faixas.Clear();
            var g = new Model3DGroup();
            if (!Realista)
            {
                g.Children.Add(new AmbientLight(Color.FromRgb(0x78, 0x7C, 0x86)));
                g.Children.Add(new DirectionalLight(Color.FromRgb(0x6C, 0x6C, 0x6C), new Vector3D(-0.4, 0.6, -0.7)));
                g.Children.Add(new DirectionalLight(Color.FromRgb(0x30, 0x30, 0x34), new Vector3D(0.5, -0.3, 0.4)));
            }

            Rect3D cx = Caixa();
            raio = Math.Max(1, new Vector3D(cx.SizeX, cx.SizeY, cx.SizeZ).Length / 2);
            double h = raio * 0.006, hn = h * 2.0;   // meia espessura da barra e do nó

            bool def = MostrarDeformada && mod.Nos.Any(n => n.Desl != null);
            double dmax = def ? mod.Nos.Where(n => n.Desl != null).Max(n => Math.Sqrt(n.Desl[0] * n.Desl[0] + n.Desl[1] * n.Desl[1] + n.Desl[2] * n.Desl[2])) : 0;
            double fDef = dmax < 1e-15 ? 0 : 0.08 * 2 * raio / dmax * EscalaDeformada;

            if (Realista) Realismo(g, cx, fDef, h);
            // grade no plano do piso (z mínimo): referência de profundidade, discreta como a grade do 2D
            else {
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
            if (!Realista)
            foreach (Barra3D b in mod.Barras)
            {
                No3D a = NoPorId(b.Ni), c = NoPorId(b.Nj); if (a == null || c == null) continue;
                bool sel = EhSel(b, b.Origem);
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
                bool selNo = EhSel(n, n.Origem);
                if (!Realista || selNo)   // Realista: nós escolhidos pela tela (Acertar), só o selecionado aparece
                {
                    var m = new MeshGeometry3D(); Cubo(m, P(n), hn);
                    var geo = Geo(m, Mat(selNo ? TemaPlant.CorBarraSel : TemaPlant.Texto2, true));
                    mapa[geo] = n; g.Children.Add(geo);
                }
                if (!n.Apoios.Any(x => x)) continue;
                double s = Realista ? ApoioReal(n) : raio * 0.035; Point3D p = P(n), bz = p - new Vector3D(0, 0, s * 1.4);
                if (n.Apoios[3] || n.Apoios[4] || n.Apoios[5])
                    Prisma(mApoio, bz, p - new Vector3D(0, 0, Realista ? 0 : hn), s);   // engaste: bloco (Realista: encostado no pilar)
                else
                {
                    Point3D[] q = { bz + new Vector3D(-s, -s, 0), bz + new Vector3D(s, -s, 0), bz + new Vector3D(s, s, 0), bz + new Vector3D(-s, s, 0) };
                    for (int i = 0; i < 4; i++) Tri(mApoio, p, q[i], q[(i + 1) % 4]);   // rótula: pirâmide
                    Quad(mApoio, q[3], q[2], q[1], q[0]);
                }
            }
            if (mApoio.Positions.Count > 0) g.Children.Add(Geo(mApoio, Realista ? MatReal(TemaPlant.CorConcreto3D, false) : Mat(TemaPlant.CorApoio, true), true));

            // deformada (cordas entre nós deslocados; ponytail: sem a elástica dentro da barra).
            // Realista: as peças já estão na posição deformada; aqui fica a posição original em linhas finas.
            if (fDef > 0)
            {
                var m = new MeshGeometry3D();
                foreach (Barra3D b in mod.Barras)
                {
                    No3D a = NoPorId(b.Ni), c = NoPorId(b.Nj); if (a == null || c == null) continue;
                    if (Realista) Prisma(m, P(a), P(c), h * 0.5);
                    else Prisma(m, Deformado(a, fDef), Deformado(c, fDef), h * 1.2);
                }
                g.Children.Add(Geo(m, Mat(Realista ? TemaPlant.TextoMudo : TemaPlant.CorDeformada, true)));
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

        // ================= modo Realista =================
        // Perfis extrudados com a forma real, luz chave + enchimento + ambiente, contorno escuro (casca invertida: cópia
        // ~1 px maior que só desenha as faces de trás, como o "Sombreado com arestas" do Revit) e sombra suave projetada no
        // chão pela direção da luz chave. Tudo juntado em poucas malhas por material; o picking acha a barra pelo índice
        // do vértice (faixas). Viewport3D não tem shadow map: ver docs/VISTA3D.md.

        sealed class Laco { public Point[] P; public Vector[] N; public bool Liso; }   // N[k]: normal da aresta k->k+1 (liso: normal do vértice k)
        sealed class Forma { public readonly List<Laco> Lacos = new List<Laco>(); public readonly List<Point> Tampa = new List<Point>(); public double Larg; public bool Concreto; }

        sealed class Malha
        {
            public readonly Point3DCollection P = new Point3DCollection();
            public readonly Vector3DCollection N = new Vector3DCollection();
            public readonly Int32Collection T = new Int32Collection();
            public readonly PointCollection UV = new PointCollection();
            public readonly List<int> Ini = new List<int>(); public readonly List<object> Obj = new List<object>();
            public void Marcar(object o) { Ini.Add(P.Count); Obj.Add(o); }
            public void Quad(int i) { T.Add(i); T.Add(i + 1); T.Add(i + 2); T.Add(i); T.Add(i + 2); T.Add(i + 3); }
        }

        // luz chave (direção em que a luz anda): fixa no mundo, como o sol do Revit; vem da esquerda-alto da vista Iso
        static readonly Vector3D Luz = Norm(new Vector3D(0.45, 0.5, -0.74));
        static readonly Vector3D LuzEnch = Norm(new Vector3D(-0.85, -0.2, -0.35));
        static Vector3D Norm(Vector3D v) { v.Normalize(); return v; }

        static Laco Poli(params double[] uv)
        {
            int n = uv.Length / 2; var l = new Laco { P = new Point[n], N = new Vector[n] };
            for (int k = 0; k < n; k++) l.P[k] = new Point(uv[2 * k], uv[2 * k + 1]);
            for (int k = 0; k < n; k++) { Vector t = l.P[(k + 1) % n] - l.P[k]; t.Normalize(); l.N[k] = new Vector(t.Y, -t.X); }   // anti-horário: normal para fora
            return l;
        }

        static Laco Circulo(double r)
        {
            const int n = 24; var l = new Laco { P = new Point[n], N = new Vector[n], Liso = true };
            for (int k = 0; k < n; k++) { double a = 2 * Math.PI * k / n; l.N[k] = new Vector(Math.Cos(a), Math.Sin(a)); l.P[k] = new Point(r * l.N[k].X, r * l.N[k].Y); }
            return l;
        }

        // contorno de furo: sentido horário, normal para dentro do furo (fora do material)
        static Laco Furo(Laco ccw)
        {
            if (!ccw.Liso) return Poli(ccw.P.Reverse().SelectMany(p => new[] { p.X, p.Y }).ToArray());
            return new Laco { P = ccw.P.Reverse().ToArray(), N = ccw.N.Reverse().Select(v => -v).ToArray(), Liso = true };
        }

        static void Ret(Forma f, double u0, double v0, double u1, double v1)
        { f.Tampa.AddRange(new[] { new Point(u0, v0), new Point(u1, v0), new Point(u1, v1), new Point(u0, v0), new Point(u1, v1), new Point(u0, v1) }); }

        static void Anel(Forma f, Point[] ext, Point[] inn)   // dois contornos anti-horários com o mesmo número de pontos
        {
            for (int i = 0; i < ext.Length; i++) { int j = (i + 1) % ext.Length; f.Tampa.AddRange(new[] { ext[i], ext[j], inn[j], ext[i], inn[j], inn[i] }); }
        }

        // Contorno da seção no plano (u, v) = (largura, altura); v é o eixo local y da Vista3D. padrao = lado sem dados.
        static Forma CriarForma(Perfil3D p, double padrao)
        {
            var f = new Forma();
            double h = p != null && p.H > 0 ? p.H : padrao, b = p != null && p.B > 0 ? p.B : h;
            f.Larg = Math.Max(h, b); f.Concreto = p?.Concreto ?? false;
            double tw = Math.Min(p != null && p.Tw > 0 ? p.Tw : h * 0.06, b * 0.45), tf = Math.Min(p != null && p.Tf > 0 ? p.Tf : h * 0.08, h * 0.45);
            double hb = b / 2, hh = h / 2;
            switch (p?.Forma)
            {
                case "I":
                    f.Lacos.Add(Poli(-hb, -hh, hb, -hh, hb, -hh + tf, tw / 2, -hh + tf, tw / 2, hh - tf, hb, hh - tf, hb, hh, -hb, hh, -hb, hh - tf, -tw / 2, hh - tf, -tw / 2, -hh + tf, -hb, -hh + tf));
                    Ret(f, -hb, -hh, hb, -hh + tf); Ret(f, -tw / 2, -hh + tf, tw / 2, hh - tf); Ret(f, -hb, hh - tf, hb, hh);
                    break;
                case "U":   // alma do lado -u
                    f.Lacos.Add(Poli(-hb, -hh, hb, -hh, hb, -hh + tf, -hb + tw, -hh + tf, -hb + tw, hh - tf, hb, hh - tf, hb, hh, -hb, hh));
                    Ret(f, -hb, -hh, hb, -hh + tf); Ret(f, -hb, -hh + tf, -hb + tw, hh - tf); Ret(f, -hb, hh - tf, hb, hh);
                    break;
                case "L":   // abas em -u (vertical) e -v (horizontal)
                    f.Lacos.Add(Poli(-hb, -hh, hb, -hh, hb, -hh + tw, -hb + tw, -hh + tw, -hb + tw, hh, -hb, hh));
                    Ret(f, -hb, -hh, hb, -hh + tw); Ret(f, -hb, -hh + tw, -hb + tw, hh);
                    break;
                case "TR":
                {
                    double t = Math.Min(tw, Math.Min(hb, hh) * 0.9);
                    Laco e = Poli(-hb, -hh, hb, -hh, hb, hh, -hb, hh), i = Poli(-hb + t, -hh + t, hb - t, -hh + t, hb - t, hh - t, -hb + t, hh - t);
                    f.Lacos.Add(e); f.Lacos.Add(Furo(i)); Anel(f, e.P, i.P);
                    break;
                }
                case "TC":
                {
                    double r = h / 2, t = Math.Min(tw, r * 0.9);
                    Laco e = Circulo(r), i = Circulo(r - t);
                    f.Lacos.Add(e); f.Lacos.Add(Furo(i)); Anel(f, e.P, i.P);
                    break;
                }
                case "CIRC":
                {
                    Laco e = Circulo(h / 2); f.Lacos.Add(e);
                    for (int k = 0; k < e.P.Length; k++) f.Tampa.AddRange(new[] { new Point(0, 0), e.P[k], e.P[(k + 1) % e.P.Length] });
                    break;
                }
                default:   // RET, caixa equivalente (A, I) ou sem dados
                    f.Lacos.Add(Poli(-hb, -hh, hb, -hh, hb, hh, -hb, hh));
                    Ret(f, -hb, -hh, hb, hh);
                    break;
            }
            return f;
        }

        Forma FormaDe(string secao, double padrao)
        {
            secao = secao ?? "";
            if (formas.TryGetValue(secao, out Forma f)) return f;
            Perfil3D p = null;
            try { p = Perfil?.Invoke(secao); } catch { }
            return formas[secao] = CriarForma(p, padrao);
        }

        // contorno afastado de d para fora (quina: em esquadro pela bissetriz)
        static Point[] Deslocar(Laco l, double d)
        {
            int n = l.P.Length; var q = new Point[n];
            for (int k = 0; k < n; k++)
            {
                if (l.Liso) { q[k] = l.P[k] + l.N[k] * d; continue; }
                Vector n1 = l.N[(k + n - 1) % n], n2 = l.N[k], m = n1 + n2;
                if (m.Length < 1e-9) m = n2;
                m.Normalize();
                q[k] = l.P[k] + m * (d / Math.Max(0.35, m * n2));
            }
            return q;
        }

        // Extrusão da forma de a até b; (u, v, e) é um triedro direito. d > 0 = casca do contorno (afastada e prolongada de d).
        static void Extrudar(Malha m, Forma f, Point3D a, Point3D b, Vector3D u, Vector3D v, double d)
        {
            Vector3D e = b - a; double len = e.Length; if (len < 1e-9) return; e /= len;
            if (d > 0) { a -= e * d; b += e * d; }
            foreach (Laco l in f.Lacos)
            {
                Point[] q = d > 0 ? Deslocar(l, d) : l.P; int n = q.Length;
                for (int k = 0; k < n; k++)
                {
                    int j = (k + 1) % n, i0 = m.P.Count;
                    Vector3D pk = u * q[k].X + v * q[k].Y, pj = u * q[j].X + v * q[j].Y;
                    m.P.Add(a + pk); m.P.Add(a + pj); m.P.Add(b + pj); m.P.Add(b + pk);
                    Vector3D nk = u * l.N[k].X + v * l.N[k].Y, nj = l.Liso ? u * l.N[j].X + v * l.N[j].Y : nk;   // liso: normal suave (tubo); senão plana
                    m.N.Add(nk); m.N.Add(nj); m.N.Add(nj); m.N.Add(nk);
                    m.Quad(i0);
                }
            }
            for (int lado = 0; lado < 2; lado++)   // tampas simples nas pontas
            {
                Point3D o = lado == 0 ? b : a; Vector3D nn = lado == 0 ? e : -e;
                for (int k = 0; k < f.Tampa.Count; k += 3)
                {
                    int i0 = m.P.Count;
                    for (int s = 0; s < 3; s++)
                    {
                        Point t = f.Tampa[k + (lado == 0 ? s : 2 - s)];
                        if (d > 0) t = new Point(t.X + Math.Sign(t.X) * d, t.Y + Math.Sign(t.Y) * d);
                        m.P.Add(o + u * t.X + v * t.Y); m.N.Add(nn);
                    }
                    m.T.Add(i0); m.T.Add(i0 + 1); m.T.Add(i0 + 2);
                }
            }
        }

        static Color Mistura(Color a, Color b, double t)
        {
            return Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
        }

        static Material MatReal(Color c, bool aco)
        {
            var g = new MaterialGroup();
            g.Children.Add(new DiffuseMaterial(new SolidColorBrush(c)));
            if (aco) g.Children.Add(new SpecularMaterial(new SolidColorBrush(TemaPlant.CorBrilhoAco3D), 30));
            g.Freeze();
            return g;
        }

        // pincel de alfa (cor da sombra) desenhado em bitmap; ViewportUnits absoluto = coordenadas de textura 0..1 da malha
        static ImageBrush PincelAlfa(int w, int hgt, Func<double, double, double> alfa)
        {
            var px = new byte[w * hgt * 4]; Color c = TemaPlant.CorSombra3D;
            for (int y = 0; y < hgt; y++)
                for (int x = 0; x < w; x++)
                {
                    double a = Math.Max(0, Math.Min(1, alfa((x + 0.5) / w, (y + 0.5) / hgt)));
                    int i = (y * w + x) * 4;
                    px[i] = (byte)(c.B * a); px[i + 1] = (byte)(c.G * a); px[i + 2] = (byte)(c.R * a); px[i + 3] = (byte)(255 * a);
                }
            var bmp = BitmapSource.Create(w, hgt, 96, 96, PixelFormats.Pbgra32, null, px, w * 4); bmp.Freeze();
            var br = new ImageBrush(bmp) { ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 1, 1) }; br.Freeze();
            return br;
        }

        static double Suave(double t) { t = Math.Max(0, Math.Min(1, t)); return t * t * (3 - 2 * t); }

        // sombra: u = através (borda borrada nos dois lados), v = altura da peça (mais alta = mais clara)
        static readonly Lazy<Material> MatSombra = new Lazy<Material>(() =>
        {
            var m = new DiffuseMaterial(PincelAlfa(64, 32, (u, v) => 0.62 * Suave(1.15 * (1 - Math.Abs(2 * u - 1))) * (1 - 0.55 * v))); m.Freeze(); return m;
        });
        // "oclusão ambiente" falsa: mancha radial escura no pé dos pilares
        static readonly Lazy<Material> MatAo = new Lazy<Material>(() =>
        {
            var m = new DiffuseMaterial(PincelAlfa(48, 48, (u, v) => { double r = Math.Sqrt((2 * u - 1) * (2 * u - 1) + (2 * v - 1) * (2 * v - 1)); return 0.6 * Math.Pow(Math.Max(0, 1 - r), 2.2); })); m.Freeze(); return m;
        });
        static readonly Lazy<Material> MatChao = new Lazy<Material>(() =>
        {
            Color c = TemaPlant.CorChao3D;
            var br = new RadialGradientBrush(new GradientStopCollection
            {
                new GradientStop(Color.FromArgb(0xFF, c.R, c.G, c.B), 0), new GradientStop(Color.FromArgb(0xC8, c.R, c.G, c.B), 0.5), new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1),
            });
            br.Freeze();
            var m = new EmissiveMaterial(br); m.Freeze(); return m;
        });
        static readonly Lazy<Material> MatAresta = new Lazy<Material>(() => { var m = new DiffuseMaterial(new SolidColorBrush(TemaPlant.CorAresta3D)); m.Freeze(); return m; });

        double LarguraAresta() { return 0.85 * larg / Math.Max(200, ActualWidth); }   // ~0,85 px na tela

        static void QuadChao(Malha m, double x0, double y0, double x1, double y1, double z)
        {
            int i0 = m.P.Count;
            m.P.Add(new Point3D(x0, y0, z)); m.P.Add(new Point3D(x1, y0, z)); m.P.Add(new Point3D(x1, y1, z)); m.P.Add(new Point3D(x0, y1, z));
            for (int k = 0; k < 4; k++) m.N.Add(new Vector3D(0, 0, 1));
            m.UV.Add(new Point(0, 0)); m.UV.Add(new Point(1, 0)); m.UV.Add(new Point(1, 1)); m.UV.Add(new Point(0, 1));
            m.Quad(i0);
        }

        // Sombra da barra a-b no plano z0 (projeção pela luz chave), larga na medida da peça e mais borrada quanto mais alta.
        static void Sombra(Malha m, Point3D a, Point3D b, double larg, double z0, double z, double alt)
        {
            Point3D Proj(Point3D p) { double t = (p.Z - z0) / -Luz.Z; return new Point3D(p.X + Luz.X * t, p.Y + Luz.Y * t, z); }
            Point3D pa = Proj(a), pb = Proj(b);
            var d = new Vector(pb.X - pa.X, pb.Y - pa.Y);
            if (d.Length < 1e-9) d = new Vector(Luz.X, Luz.Y);
            d.Normalize();
            var nr = new Vector(-d.Y, d.X);
            double ha = larg * 0.7 + 0.03 * (a.Z - z0), hb = larg * 0.7 + 0.03 * (b.Z - z0);
            double va = Math.Min(1, Math.Max(0, (a.Z - z0) / alt)), vb = Math.Min(1, Math.Max(0, (b.Z - z0) / alt));
            int i0 = m.P.Count;
            m.P.Add(new Point3D(pa.X - nr.X * ha, pa.Y - nr.Y * ha, z)); m.UV.Add(new Point(0, va));
            m.P.Add(new Point3D(pb.X - nr.X * hb, pb.Y - nr.Y * hb, z)); m.UV.Add(new Point(0, vb));
            m.P.Add(new Point3D(pb.X + nr.X * hb, pb.Y + nr.Y * hb, z)); m.UV.Add(new Point(1, vb));
            m.P.Add(new Point3D(pa.X + nr.X * ha, pa.Y + nr.Y * ha, z)); m.UV.Add(new Point(1, va));
            for (int k = 0; k < 4; k++) m.N.Add(new Vector3D(0, 0, 1));
            m.Quad(i0);
        }

        GeometryModel3D Modelo(Malha m, Material frente, Material tras = null)
        {
            var mg = new MeshGeometry3D { Positions = m.P, Normals = m.N, TriangleIndices = m.T };
            if (m.UV.Count > 0) mg.TextureCoordinates = m.UV;
            mg.Freeze();
            var g = new GeometryModel3D(mg, frente) { BackMaterial = tras };
            g.Freeze();
            if (m.Obj.Count > 0) faixas[g] = (m.Ini.ToArray(), m.Obj.ToArray());
            return g;
        }

        readonly Dictionary<int, double> largNo = new Dictionary<int, double>();
        double ApoioReal(No3D n) { return Math.Min(raio * 0.035, Math.Max(raio * 0.006, 1.3 * (largNo.TryGetValue(n.Id, out double l) ? l : 0))); }   // meio lado do bloco

        void Realismo(Model3DGroup g, Rect3D cx, double fDef, double h)
        {
            g.Children.Add(new AmbientLight(TemaPlant.CorLuzAmb3D));
            g.Children.Add(new DirectionalLight(TemaPlant.CorLuzChave3D, Luz));
            g.Children.Add(new DirectionalLight(TemaPlant.CorLuzEnch3D, LuzEnch));

            // largura da peça mais larga em cada nó: tamanho do bloco de apoio e da mancha escura no pé
            largNo.Clear();
            foreach (Barra3D b in mod.Barras)
            {
                double l = FormaDe(b.Secao, h * 2).Larg;
                foreach (int id in new[] { b.Ni, b.Nj }) largNo[id] = Math.Max(l, largNo.TryGetValue(id, out double l0) ? l0 : 0);
            }
            double z0 = cx.Z, eps = raio * 0.0015, alt = Math.Max(cx.SizeZ, raio * 0.2);
            foreach (No3D n in mod.Nos) if (n.Apoios.Any(x => x)) z0 = Math.Min(z0, n.Z - ApoioReal(n) * 1.4);   // chão no pé dos blocos

            // chão: degradê radial que some no fundo do canvas
            {
                double xm = cx.X + cx.SizeX / 2, ym = cx.Y + cx.SizeY / 2, r = Math.Max(cx.SizeX, cx.SizeY) / 2 + raio * 1.1;
                var m = new Malha(); QuadChao(m, xm - r, ym - r, xm + r, ym + r, z0);
                g.Children.Add(Modelo(m, MatChao.Value));
            }

            largArestas = LarguraAresta();
            var grupos = new Dictionary<(Color, bool), Malha>();
            var casca = new Malha(); var sombra = new Malha();
            foreach (Barra3D b in mod.Barras)
            {
                No3D na = NoPorId(b.Ni), nc = NoPorId(b.Nj); if (na == null || nc == null) continue;
                Point3D a = fDef > 0 ? Deformado(na, fDef) : P(na), c = fDef > 0 ? Deformado(nc, fDef) : P(nc);
                Vector3D e = c - a; if (e.Length < 1e-9) continue;
                Vector3D v = Modelo3D.EixoY(e, b.Rot + b.RotPerfil), u = Vector3D.CrossProduct(v, Norm(e));
                Forma f = FormaDe(b.Secao, h * 2);
                Color cor = f.Concreto ? TemaPlant.CorConcreto3D : TemaPlant.CorAco3D;
                if (MostrarAproveitamento && b.Aprov.HasValue) cor = Mistura(cor, CorAprov(b.Aprov.Value), 0.72);   // tinge o material, mantém o sombreado
                if (EhSel(b, b.Origem)) cor = Mistura(cor, TemaPlant.CorBarraSel, 0.75);
                var k = (cor, !f.Concreto);
                if (!grupos.TryGetValue(k, out Malha m)) grupos[k] = m = new Malha();
                m.Marcar(b); Extrudar(m, f, a, c, u, v, 0);
                casca.Marcar(b); Extrudar(casca, f, a, c, u, v, largArestas);
                Sombra(sombra, a, c, f.Larg, z0, z0 + eps, alt);
            }
            var ao = new Malha();
            foreach (No3D n in mod.Nos)
                if (n.Z - z0 < raio * 0.01 + (n.Apoios.Any(x => x) ? ApoioReal(n) * 1.4 : 0) && largNo.TryGetValue(n.Id, out double l))
                {
                    double r = l * 2.2 + raio * 0.01;
                    QuadChao(ao, n.X - r, n.Y - r, n.X + r, n.Y + r, z0 + 2 * eps);
                }
            // ordem: transparentes no chão antes das peças opacas
            if (sombra.P.Count > 0) g.Children.Add(Modelo(sombra, MatSombra.Value));
            if (ao.P.Count > 0) g.Children.Add(Modelo(ao, MatAo.Value));
            foreach (var kv in grupos) g.Children.Add(Modelo(kv.Value, MatReal(kv.Key.Item1, kv.Key.Item2)));
            if (casca.P.Count > 0) g.Children.Add(Modelo(casca, null, MatAresta.Value));
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
        Point Tela(Point3D q)
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
        Fluent.ToggleButton tgRealista;
        public bool Em3D { get { return vista3D != null && vista3D.Visibility == Visibility.Visible; } }

        // Botão "Realista" (Vista › Navegar): perfis reais com sombra ou vista analítica; a escolha fica em Config.
        public void DefinirRealista(bool ligar)
        {
            Config.Realista3D = ligar;
            if (tgRealista != null) tgRealista.IsChecked = ligar;
            if (vista3D != null) { vista3D.Realista = ligar; vista3D.Reconstruir(); }
            if (ligar && !Em3D) MostrarVista3D(true);
        }

        // Forma real da seção: do modelo do motor (props do JSON) ou do catálogo do Estructto (perfis-*.csv).
        Perfil3D PerfilDaSecao(string nome)
        {
            if (Esp?.Json?["secoes"]?[nome] is JsonObject s) return Perfil3D.DoMotor(s);
            return Perfil3D.DoCatalogo(Biblioteca.AcharSecao(nome));
        }

        public void AlternarVista3D() { MostrarVista3D(!Em3D); }

        public void MostrarVista3D(bool ligar)
        {
            if (ligar && vista3D == null)
            {
                vista3D = new Vista3D { Visibility = Visibility.Collapsed, MostrarLegenda = false, Realista = Config.Realista3D, Perfil = PerfilDaSecao };
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
