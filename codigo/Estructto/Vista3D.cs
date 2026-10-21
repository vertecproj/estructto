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
        public int Cardinal = 5;   // ponto cardinal 1..9 (teclado numérico) ou 10 = centroide; o Render3D pode deslocar o perfil
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

    // Carga do caso ativo (Ações › Caso ativo), em eixos globais. Quem monta o Modelo3D já filtra pelo caso.
    // Nodal: No + F (kN). Pontual: Barra + T (0..1 de ni a nj) + F (kN). Distribuída: Barra + F (q em ni) e F2 (q em nj), kN/m.
    public sealed class Carga3D
    {
        public int No, Barra; public double T; public Vector3D F, F2; public bool Distribuida;
    }

    public sealed class Modelo3D
    {
        public List<No3D> Nos = new List<No3D>();
        public List<Barra3D> Barras = new List<Barra3D>();
        public List<Carga3D> Cargas = new List<Carga3D>();
        public List<Ligacao3D> Ligacoes = new List<Ligacao3D>();   // geometria do contrato (Render3DLigacoes.cs); vazio = nenhuma
        // Barras fora do plano/nível isolado: linhas claras sem picking nem atração (PlanosTrabalho.cs › FiltrarPlano).
        public List<(Point3D A, Point3D B)> Fantasmas = new List<(Point3D, Point3D)>();

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
            // placas de base calculadas (Ligações e bases › Placa de base) viram chapa + chumbadores no Render3D
            foreach (No n in m.Nos)
            {
                if (n.Placa == null) continue;
                Barra pilar = PlacaCalc.Pilar(m, n, out _, out _, out _, out _); if (pilar == null) continue;
                Barra3D b3 = r.Barras.FirstOrDefault(b => b.Origem == pilar); if (b3 == null) continue;
                Secao sec = Biblioteca.AcharSecao(pilar.Secao); if (sec == null) continue;
                var lig = LigacoesAmostra.DaPlaca2D(n.Id, b3, b3.Ni == n.Id, GeometriaPerfis.CriarContorno(Perfil3D.DoCatalogo(sec), 0.1), n.Placa, PlacaCalc.Calcular(m, n));
                if (lig != null) r.Ligacoes.Add(lig);
            }
            return r;
        }
    }

    // Ferramentas de entrada da Vista3D (docs/MODELO-ESPACIAL.md, "Desenhar no 3D"). Navegar = clique escolhe, arrastar orbita.
    public enum Ferramenta3D { Navegar, Selecionar, Barra, Pilar, Medir }

    // Plano de trabalho: horizontal (Normal 'Z', z = Valor) ou vertical (Normal 'X' ou 'Y'). U e W são os eixos do plano
    // (ortogonal e ângulo da entrada dinâmica: ângulo medido de U para W).
    public sealed class PlanoTrabalho
    {
        public readonly char Normal; public readonly double Valor; public readonly string Nome;
        public PlanoTrabalho(char normal, double valor, string nome = null)
        {
            Normal = char.ToUpperInvariant(normal);
            if (Normal != 'X' && Normal != 'Y' && Normal != 'Z') throw new ArgumentException("normal: X, Y ou Z");
            Valor = valor; Nome = nome ?? Normal.ToString().ToLowerInvariant() + " = " + Num.Fmt(valor, 2) + " m";
        }
        public static PlanoTrabalho Horizontal(double z, string nome = null) => new PlanoTrabalho('Z', z, nome);
        public static PlanoTrabalho EmX(double x, string nome = null) => new PlanoTrabalho('X', x, nome);
        public static PlanoTrabalho EmY(double y, string nome = null) => new PlanoTrabalho('Y', y, nome);
        public Vector3D N => Normal == 'X' ? new Vector3D(1, 0, 0) : Normal == 'Y' ? new Vector3D(0, 1, 0) : new Vector3D(0, 0, 1);
        public Vector3D U => Normal == 'X' ? new Vector3D(0, 1, 0) : new Vector3D(1, 0, 0);
        public Vector3D W => Normal == 'Z' ? new Vector3D(0, 1, 0) : new Vector3D(0, 0, 1);
        public Point3D Projetar(Point3D p) => p - N * (Vector3D.DotProduct((Vector3D)p, N) - Valor);
        public Point3D Ponto(double u, double w) => (Point3D)(N * Valor + U * u + W * w);
    }

    // Seleção múltipla da Vista3D: ids do motor (RefMotor.Id) ou, no modelo 2D, o Id do nó/barra em texto.
    public sealed class Selecao3D
    {
        public readonly HashSet<string> Nos = new HashSet<string>(), Barras = new HashSet<string>();
        public int Count => Nos.Count + Barras.Count;
    }

    public enum Diagrama3D { Nenhum, N, V, M }
    public enum VistaPadrao { Topo, Frente, Lado, Iso }

    public partial class Vista3D : Grid
    {
        public event Action<object> Escolheu;   // No3D ou Barra3D (Origem aponta para o objeto do modelo)

        Modelo3D mod = new Modelo3D();
        public Diagrama3D Diagrama;
        public bool MostrarAproveitamento, MostrarDeformada, MostrarLegenda = true;   // legenda própria (a janela principal já tem a do HUD)
        public bool MostrarCargas = true;        // Vista › Mostrar › Cargas (o mesmo botão do 2D)
        public double EscalaCargas = 1;
        // Setas por cima de tudo (sem teste de profundidade). Padrão: só no Analítico; no Realista elas respeitam as peças na frente.
        public bool? CargasSempre;
        public bool CargasSempreVisiveis => CargasSempre ?? !Realista;
        MeshGeometry3D malhaCargasDx, malhaNosDx;
        // octaedro em cada nó escolhido (a sobreposição 2D não tem profundidade)
        MeshGeometry3D NosEscolhidos()
        {
            var m = new MeshGeometry3D(); double r = raio * 0.01;
            foreach (No3D n in mod.Nos)
            {
                if (!EhSel(n, n.Origem)) continue;
                Point3D p = P(n);
                Point3D[] a = { p + new Vector3D(r, 0, 0), p + new Vector3D(0, r, 0), p + new Vector3D(-r, 0, 0), p + new Vector3D(0, -r, 0) };
                for (int i = 0; i < 4; i++) { Tri(m, a[i], a[(i + 1) % 4], p + new Vector3D(0, 0, r)); Tri(m, a[(i + 1) % 4], a[i], p - new Vector3D(0, 0, r)); }
            }

            return m.Positions.Count > 0 ? m : null;
        }
        public double EscalaDeformada = 1, EscalaDiagrama = 1;
        public object Selecionado;
        public bool Realista;                    // perfis reais, materiais, sombra e arestas (Config.Realista3D)
        public Func<string, Perfil3D> Perfil;    // nome da seção -> forma real (null/desconhecida = caixa fina)
        // Render3D (DX11) quando há placa de vídeo; senão o Realista nativo deste arquivo. ESTRUCTTO_3D=nativo força o nativo.
        Render3D render;
        public bool Sombra = true;               // Opções › Sombra: sombra projetada no Realista DX (ShadowMap3D)
        public bool SemDx;                       // estilo "Sombreado" (SeletorEstilo.cs): o Realista nativo mesmo com GPU
        bool Dx => Realista && !SemDx && Render3D.Disponivel;
        public bool LigacoesAmostra = Environment.GetEnvironmentVariable("ESTRUCTTO_3D_AMOSTRAS") == "1";   // sem ligações do motor: exemplos (capturas)
        readonly Dictionary<string, Forma> formas = new Dictionary<string, Forma>();
        readonly Dictionary<Model3D, (int[] Ini, object[] Obj)> faixas = new Dictionary<Model3D, (int[], object[])>();   // malhas juntadas: vértice inicial de cada barra
        double largArestas = 1;                  // largura de tela com que as arestas foram feitas
        readonly DispatcherTimer timerArestas = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };

        readonly Viewport3D vp = new Viewport3D { ClipToBounds = true, IsHitTestVisible = true };
        readonly OrthographicCamera cam = new OrthographicCamera { NearPlaneDistance = 0.001, FarPlaneDistance = double.PositiveInfinity };
        readonly ModelVisual3D cena = new ModelVisual3D();
        readonly Sobre sobre;

        // câmera: azimute e elevação (graus), largura visível (m), alvo
        double az = -55, el = 28, larg = 10; Point3D alvo;
        double raio = 5;
        Camera3 de, para; readonly Stopwatch relogio = new Stopwatch(); bool animando;
        const double DuracaoMs = 300;   // GUIA-DESIGN: transição de câmera 250–400 ms, CubicEase EaseOut

        Point? panIni, orbIni; bool moveu;

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
            marca = new MarcaSnap { IsHitTestVisible = false, Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, RenderTransform = posMarca };
            Children.Add(marca);

            MontarNavegacao();   // ViewCube e pivô da órbita (Vista3D.Navegacao.cs, ViewCube.cs)
            SizeChanged += (s, e) => { AplicarCamera(); AgendarArestas(); };
            // arestas têm ~1 px: depois do zoom, refaz a cena se a escala mudou bastante
            timerArestas.Tick += (s, e) =>
            {
                timerArestas.Stop();
                if (Realista && !Dx && Math.Abs(Math.Log(LarguraAresta() / largArestas)) > 0.3) Reconstruir();
            };
            IniciarSnap();
        }

        void AgendarArestas() { if (Realista) { timerArestas.Stop(); timerArestas.Start(); } }

        // ================= dados =================

        public void Carregar(Modelo3D m, bool enquadrar)
        {
            fantasmasNovos = (m?.Fantasmas.Count ?? 0) > 0 && mod.Fantasmas.Count == 0;
            mod = m ?? new Modelo3D();
            formas.Clear();   // as seções podem ter mudado
            nosPorId = new Dictionary<int, No3D>();
            foreach (No3D n in mod.Nos) if (!nosPorId.ContainsKey(n.Id)) nosPorId[n.Id] = n;
            if (Selecionado != null && !mod.Nos.Any(n => n.Origem == Selecionado || n == Selecionado) && !mod.Barras.Any(b => b.Origem == Selecionado || b == Selecionado)) Selecionado = null;
            // seleção múltipla: some o que deixou de existir (apagado, desfeito)
            int antes = Selecao.Nos.Count + Selecao.Barras.Count;
            Selecao.Nos.IntersectWith(mod.Nos.Select(Chave)); Selecao.Barras.IntersectWith(mod.Barras.Select(Chave));
            if (Selecao.Nos.Count + Selecao.Barras.Count != antes) SelecaoMudou?.Invoke();
            Reconstruir();
            if (enquadrar) Enquadrar(false);
        }

        Dictionary<int, No3D> nosPorId = new Dictionary<int, No3D>();
        bool fantasmasNovos;
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
        bool EhSel(object o, object origem)
        {
            if (Selecionado != null && (origem == Selecionado || o == Selecionado)) return true;
            return o is No3D n ? Selecao.Nos.Contains(Chave(n)) : o is Barra3D b && Selecao.Barras.Contains(Chave(b));
        }

        public void Reconstruir()
        {
            faixas.Clear();
            var g = new Model3DGroup();
            bool dx = Dx;
            if (!Realista || dx)   // DX: a cena nativa só tem cargas, diagramas e nós (sobrepostos ao Render3D)
            {
                g.Children.Add(new AmbientLight(Color.FromRgb(0x78, 0x7C, 0x86)));
                g.Children.Add(new DirectionalLight(Color.FromRgb(0x6C, 0x6C, 0x6C), new Vector3D(-0.4, 0.6, -0.7)));
                g.Children.Add(new DirectionalLight(Color.FromRgb(0x30, 0x30, 0x34), new Vector3D(0.5, -0.3, 0.4)));
            }

            Rect3D cx = Caixa();
            raio = Math.Max(1, new Vector3D(cx.SizeX, cx.SizeY, cx.SizeZ).Length / 2);
            double h = raio * 0.006;   // escala das setas e da grade; forma padrão das seções sem dados

            bool def = MostrarDeformada && mod.Nos.Any(n => n.Desl != null);
            double dmax = def ? mod.Nos.Where(n => n.Desl != null).Max(n => Math.Sqrt(n.Desl[0] * n.Desl[0] + n.Desl[1] * n.Desl[1] + n.Desl[2] * n.Desl[2])) : 0;
            double fDef = dmax < 1e-15 ? 0 : 0.08 * 2 * raio / dmax * EscalaDeformada;

            if (render != null && !dx) render.Vista.Visibility = Visibility.Collapsed;
            if (Realista) { if (dx) RealismoDx(cx, fDef, h); else Realismo(g, cx, fDef, h); }
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

            // Barras, nós, apoios do analítico, deformada e fantasmas: linhas e pontos de largura fixa na tela, desenhados pela
            // sobreposição a cada quadro (Vista3D.Linhas.cs). Aqui fica só o que é sólido: os blocos de apoio do Sombreado.
            linhaDef = fDef;
            if (Realista && !dx)
            {
                var mApoio = new MeshGeometry3D();
                foreach (No3D n in mod.Nos)
                {
                    if (!n.Apoios.Any(x => x)) continue;
                    double s = ApoioReal(n); Point3D p = P(n), bz = p - new Vector3D(0, 0, s * 1.4);
                    if (n.Apoios[3] || n.Apoios[4] || n.Apoios[5]) Prisma(mApoio, bz, p, s);   // engaste: bloco encostado no pilar
                    else
                    {
                        Point3D[] q = { bz + new Vector3D(-s, -s, 0), bz + new Vector3D(s, -s, 0), bz + new Vector3D(s, s, 0), bz + new Vector3D(-s, s, 0) };
                        for (int i = 0; i < 4; i++) Tri(mApoio, p, q[i], q[(i + 1) % 4]);   // rótula: pirâmide
                        Quad(mApoio, q[3], q[2], q[1], q[0]);
                    }
                }
                if (mApoio.Positions.Count > 0) g.Children.Add(Geo(mApoio, MatReal(TemaPlant.CorConcreto3D, false), true));
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

            malhaCargasDx = malhaNosDx = null;
            if (Realista && !CargasSempreVisiveis)   // nós escolhidos como sólido: com profundidade (DX: no Viewport3DX; nativo: na cena)
            {
                MeshGeometry3D mn = NosEscolhidos();
                if (mn != null) { if (dx) malhaNosDx = mn; else g.Children.Add(Geo(mn, Mat(TemaPlant.CorBarraSel, true), true)); }
            }
            // G6 r1: no Realista as setas só aparecem com "Cargas sempre visíveis" (senão cobriam as ligações nos close-ups)
            if (MostrarCargas && mod.Cargas.Count > 0 && (!Realista || CargasSempreVisiveis))
            {
                MeshGeometry3D mc = DesenharCargas();
                // DX sem "sempre visíveis": as setas vão para o Viewport3DX e passam pelo teste de profundidade (Render3D.mCargas)
                if (mc != null) { if (dx && !CargasSempreVisiveis) malhaCargasDx = mc; else g.Children.Add(Geo(mc, Mat(TemaPlant.CorCarga, true), true)); }   // fora do picking: o clique passa por elas
            }

            if (fantasmasNovos && mod.Fantasmas.Count > 0) { fantasmasNovos = false; Anim.Valor(pincelFantasma, Brush.OpacityProperty, 0, 1, Anim.Espacial); }   // entram com fade (linhas: Vista3D.Linhas.cs)

            cena.Content = g;
            sobre.InvalidateVisual();
        }

        // Setas das cargas (cor de carga do TemaPlant, fora do picking). A ponta toca o ponto de aplicação; o comprimento é
        // proporcional ao módulo, com escala própria: a maior força mede 11% do tamanho do modelo e a maior q 8% (vezes EscalaCargas).
        // Distribuída: "pente" de setas ao longo da barra com altura linear de q a q2 e a linha das caudas.
        // Realista: a ponta fica na face do perfil (meia largura da seção para fora), não no eixo escondido dentro dele.
        MeshGeometry3D DesenharCargas()
        {
            double fmax = mod.Cargas.Where(c => !c.Distribuida).Select(c => c.F.Length).DefaultIfEmpty(0).Max();
            double qmax = mod.Cargas.Where(c => c.Distribuida).Select(c => Math.Max(c.F.Length, c.F2.Length)).DefaultIfEmpty(0).Max();
            double lf = 0.22 * raio * EscalaCargas, lq = 0.16 * raio * EscalaCargas, t = raio * 0.0022, hc = raio * 0.011;
            var m = new MeshGeometry3D();
            var barras = mod.Barras.ToDictionary(b => b.Id);
            void Seta(Point3D ponta, Vector3D v, double len, double folga)
            {
                if (len < 1e-9 || v.Length < 1e-12) return;
                v.Normalize();
                ponta -= v * folga;
                double cab = Math.Min(hc * 2.6, len * 0.5);
                Point3D baseCab = ponta - v * cab;
                Prisma(m, ponta - v * len, baseCab, t);
                Vector3D u = Vector3D.CrossProduct(v, Math.Abs(v.Z) > 0.9 ? new Vector3D(1, 0, 0) : new Vector3D(0, 0, 1)); u.Normalize();
                Vector3D w = Vector3D.CrossProduct(v, u);
                double r = Math.Min(hc, cab * 0.45);
                Point3D[] q = { baseCab + (u + w) * r, baseCab + (w - u) * r, baseCab + (-u - w) * r, baseCab + (u - w) * r };
                for (int i = 0; i < 4; i++) Tri(m, ponta, q[(i + 1) % 4], q[i]);
                Quad(m, q[0], q[1], q[2], q[3]);
            }
            double Folga(Barra3D b) => Realista ? FormaDe(b.Secao, raio * 0.012).Larg / 2 : 0;
            foreach (Carga3D c in mod.Cargas)
            {
                if (!c.Distribuida && c.Barra == 0)
                {
                    No3D n = NoPorId(c.No); if (n == null || fmax < 1e-12) continue;
                    Seta(P(n), c.F, c.F.Length / fmax * lf, 0);
                    continue;
                }
                if (!barras.TryGetValue(c.Barra, out Barra3D b)) continue;
                No3D na = NoPorId(b.Ni), nc = NoPorId(b.Nj); if (na == null || nc == null) continue;
                Vector3D e = P(nc) - P(na);
                if (!c.Distribuida) { if (fmax > 1e-12) Seta(P(na) + e * c.T, c.F, c.F.Length / fmax * lf, Folga(b)); continue; }
                if (qmax < 1e-12) continue;
                int k = Math.Max(4, Math.Min(16, (int)Math.Round(e.Length / (raio * 0.07))));
                Point3D? cauda = null;
                for (int i = 0; i <= k; i++)
                {
                    double s = (double)i / k;
                    Vector3D q = c.F + (c.F2 - c.F) * s;
                    double len = q.Length / qmax * lq;
                    Point3D p = P(na) + e * s;
                    if (len < 1e-9) { cauda = p; continue; }
                    Vector3D dir = q / q.Length; double f = Folga(b);
                    Seta(p, q, len, f);
                    Point3D tc = p - dir * (len + f);
                    if (cauda != null) Prisma(m, cauda.Value, tc, t * 0.8);
                    cauda = tc;
                }
            }
            return m.Positions.Count > 0 ? m : null;
        }

        // ================= modo Realista =================
        // Perfis extrudados com a forma real, luz chave + enchimento + ambiente, contorno escuro (casca invertida: cópia
        // ~1 px maior que só desenha as faces de trás, como o "Sombreado com arestas" do Revit) e sombra suave projetada no
        // chão pela direção da luz chave. Tudo juntado em poucas malhas por material; o picking acha a barra pelo índice
        // do vértice (faixas). Viewport3D não tem shadow map: ver docs/VISTA3D.md.

        sealed class Laco { public Point[] P; public Vector[] N; public bool Liso; }   // N[k]: normal da aresta k->k+1 (liso: normal do vértice k)
        sealed class Forma { public readonly List<Laco> Lacos = new List<Laco>(); public readonly List<Point> Tampa = new List<Point>(); public double Larg; public bool Concreto; public GeometriaPerfis.Contorno Contorno; }

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

        // Contorno da seção (GeometriaPerfis.CriarContorno, o mesmo da exportação) com as normais do Realista nativo:
        // plana por aresta nas chapas, radial nos tubos (laço liso); furo (sentido horário) com a normal para dentro.
        static Forma CriarForma(Perfil3D p, double padrao)
        {
            GeometriaPerfis.Contorno c = GeometriaPerfis.CriarContorno(p, padrao);
            var f = new Forma { Larg = c.Larg, Concreto = c.Concreto, Contorno = c };
            for (int i = 0; i < c.Lacos.Count; i++)
            {
                Point[] q = c.Lacos[i]; int n = q.Length; var l = new Laco { P = q, N = new Vector[n], Liso = c.Liso[i] };
                double area = 0; for (int k = 0; k < n; k++) area += q[k].X * q[(k + 1) % n].Y - q[(k + 1) % n].X * q[k].Y;
                for (int k = 0; k < n; k++)
                {
                    if (l.Liso) { Vector r = (Vector)q[k]; r.Normalize(); l.N[k] = area < 0 ? -r : r; }
                    else { Vector t = q[(k + 1) % n] - q[k]; t.Normalize(); l.N[k] = new Vector(t.Y, -t.X); }
                }
                f.Lacos.Add(l);
            }
            f.Tampa.AddRange(c.Tampa);
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
            var casca = new Malha();
            foreach (Barra3D b in mod.Barras)
            {
                No3D na = NoPorId(b.Ni), nc = NoPorId(b.Nj); if (na == null || nc == null) continue;
                Point3D a = fDef > 0 ? Deformado(na, fDef) : P(na), c = fDef > 0 ? Deformado(nc, fDef) : P(nc);
                Vector3D e = c - a; if (e.Length < 1e-9) continue;
                Vector3D v = Modelo3D.EixoY(e, b.Rot + b.RotPerfil), u = Vector3D.CrossProduct(v, Norm(e));
                Forma f = FormaDe(b.Secao, h * 2);
                { Point dc = GeometriaPerfis.Cardinal(f.Contorno, b.Cardinal); a += u * dc.X + v * dc.Y; c += u * dc.X + v * dc.Y; }   // ponto cardinal
                Color cor = f.Concreto ? TemaPlant.CorConcreto3D : TemaPlant.CorAco3D;
                if (MostrarAproveitamento && b.Aprov.HasValue) cor = Mistura(cor, CorAprov(b.Aprov.Value), 0.72);   // tinge o material, mantém o sombreado
                if (EhSel(b, b.Origem)) cor = Mistura(cor, TemaPlant.CorBarraSel, 0.75);
                var k = (cor, !f.Concreto);
                if (!grupos.TryGetValue(k, out Malha m)) grupos[k] = m = new Malha();
                m.Marcar(b); Extrudar(m, f, a, c, u, v, 0);
                casca.Marcar(b); Extrudar(casca, f, a, c, u, v, largArestas);
            }
            var ao = new Malha();
            foreach (No3D n in mod.Nos)
                if (n.Z - z0 < raio * 0.01 + (n.Apoios.Any(x => x) ? ApoioReal(n) * 1.4 : 0) && largNo.TryGetValue(n.Id, out double l))
                {
                    double r = l * 2.2 + raio * 0.01;
                    QuadChao(ao, n.X - r, n.Y - r, n.X + r, n.Y + r, z0 + 2 * eps);
                }
            // ordem: transparentes no chão antes das peças opacas
            // V2: sem sombra projetada no nativo (a malha plana por barra gerava manchas soltas); fica só o contato suave (ao)
            if (ao.P.Count > 0) g.Children.Add(Modelo(ao, MatAo.Value));
            foreach (var kv in grupos) g.Children.Add(Modelo(kv.Value, MatReal(kv.Key.Item1, kv.Key.Item2)));
            if (casca.P.Count > 0) g.Children.Add(Modelo(casca, null, MatAresta.Value));
        }

        // Modo Realista no Render3D: a cena vai para o Viewport3DX (abaixo do Viewport3D nativo, que fica com cargas,
        // diagramas, nós e fantasmas por cima). Cores, posições deformadas e chão são os mesmos do Realismo nativo.
        void RealismoDx(Rect3D cx, double fDef, double h)
        {
            if (render == null)
            {
                render = new Render3D();
                Render3D r = render;
                r.Falhou += () => Dispatcher.BeginInvoke(new Action(() => { r.Descartar(); if (render == r) render = null; Reconstruir(); }));
                Children.Insert(Children.IndexOf(vp), render.Vista);
            }
            render.Vista.Visibility = Visibility.Visible;
            render.Sombra = Sombra;
            string sem = Environment.GetEnvironmentVariable("ESTRUCTTO_3D_SEM") ?? "";   // diagnóstico (Render3D.cs)
            vp.Visibility = sem.Contains("nativo") ? Visibility.Collapsed : Visibility.Visible;
            sobre.Visibility = sem.Contains("sobre") ? Visibility.Collapsed : Visibility.Visible;
            largNo.Clear();
            foreach (Barra3D b in mod.Barras)
            {
                double l = FormaDe(b.Secao, h * 2).Larg;
                foreach (int id in new[] { b.Ni, b.Nj }) largNo[id] = Math.Max(l, largNo.TryGetValue(id, out double l0) ? l0 : 0);
            }
            double z0 = cx.Z;
            foreach (No3D n in mod.Nos) if (n.Apoios.Any(x => x)) z0 = Math.Min(z0, n.Z - ApoioReal(n) * 1.4);
            var cena = new Cena3D
            {
                Mod = mod, Raio = raio, Z0 = z0, Caixa = cx, Cargas = malhaCargasDx, NosSel = malhaNosDx, Amostras = LigacoesAmostra,
                Forma = s => FormaDe(s, h * 2).Contorno,
                Pontos = b => { No3D a = NoPorId(b.Ni), c = NoPorId(b.Nj); return a == null || c == null ? (new Point3D(), new Point3D()) : (fDef > 0 ? Deformado(a, fDef) : P(a), fDef > 0 ? Deformado(c, fDef) : P(c)); },
                Selecionada = b => EhSel(b, b.Origem),
                Apoio = n =>
                {
                    if (!n.Apoios.Any(x => x)) return 0;
                    double s = ApoioReal(n);   // pedestal: cabe a placa de base (chapa mais larga da ligação do nó) com 40 mm de sobra
                    foreach (Ligacao3D l in mod.Ligacoes) if (l.No == n.Id) foreach (var ch in l.Chapas) s = Math.Max(s, ch.Contorno.Max(p => Math.Max(Math.Abs(p.X), Math.Abs(p.Y))) / 1000 / 2 + 0.04);
                    return s;
                },
                Cor = b =>
                {
                    Color cor = Render3D.CorBase(FormaDe(b.Secao, h * 2).Concreto);
                    if (MostrarAproveitamento && b.Aprov.HasValue) cor = Mistura(cor, CorAprov(b.Aprov.Value), 0.72);
                    if (EhSel(b, b.Origem)) cor = Mistura(cor, TemaPlant.CorBarraSel, 0.45);   // o brilho da seleção vem do pós-efeito
                    return cor;
                },
            };
            render.Montar(cena);
            render.Lod(PxPorM);
            AplicarCamera();
        }

        public Modelo3D ModeloAtual => mod;
        // Ligações vindas do motor (Render3DMotor.cs) para o modelo já carregado: redesenha o Realista.
        public void DefinirLigacoes(List<Ligacao3D> ligacoes) { mod.Ligacoes = ligacoes ?? new List<Ligacao3D>(); if (Realista) Reconstruir(); }
        public double UltimaMontagemDxMs => render?.UltimaMontagemMs ?? double.NaN;
        public double MedirQuadroDx() => Dx && render != null ? render.MedirQuadro() : double.NaN;
        public bool UsandoDx => Dx && render != null;

        // Capturas: o Render3D precisa desenhar antes de a janela ser lida pelo RenderTargetBitmap (Capturas.cs).
        public void PrepararCaptura() { if (Dx && render != null && IsVisible) render.PrepararCaptura(); }

        // Aproxima um ponto (close-ups das capturas): largura visível (m), azimute e elevação em graus, sem animação.
        public void Focar(Point3D p, double largura, double azv, double elv) { AtualizarNome("Detalhe"); IrPara(new Camera3 { Az = azv, El = elv, Larg = largura, Alvo = p }, false); }

        // ================= entrada: plano de trabalho, desenho e seleção =================
        // Igual à vista 2D (Vista.cs): atração a nó / meio / grade, ortogonal (F8 ou Shift) e polar 0/90 nos eixos do plano,
        // entrada dinâmica (comprimento, ângulo ou x,y,z digitados) junto ao cursor, Esc em cascata.
        // A criação vai para quem liga CriarBarras (MainWindow: lote do motor, um passo de desfazer por clique).

        public event Action<PlanoTrabalho> PlanoMudou;
        public event Action FerramentaMudou;
        public event Action SelecaoMudou;
        public Selecao3D Selecao { get; } = new Selecao3D();
        public Func<IList<(Point3D A, Point3D B)>, Task> CriarBarras;   // null = vista só de leitura (modelo 2D)
        public Func<double, double> AlturaAndar;                         // z clicado -> altura do pilar (null = 3 m; < 0 = para baixo)
        public double PassoGrade = 1;                                    // m
        public bool Orto;                                                // F8; Shift força na hora
        // Grade do plano ao desenhar (F7) e caixas da entrada dinâmica (F12). OSNAP/ORTO/Polar/Rastreio ficam no Snap3D (Vista3D.Snap.cs).
        public bool MostrarGradePlano = true, EntradaDinamica = true;
        // Eixos da grade estrutural (EixosX/Y, NomesX/Y, NiveisEixos): Vista3D.Grade.cs.

        PlanoTrabalho plano = PlanoTrabalho.Horizontal(0);
        public PlanoTrabalho PlanoAtual
        {
            get => plano;
            set { plano = value ?? PlanoTrabalho.Horizontal(0); AtualizarCursor(); PlanoMudou?.Invoke(plano); }
        }

        Ferramenta3D ferr;
        public Ferramenta3D Ferramenta
        {
            get => ferr;
            set
            {
                if (Pedindo) LimparPedido();
                gripQuente = null; Prompt = null;
                ferr = value; inicio = null; DinLimpar(); coord = "";
                Cursor = Desenhando ? Cursors.Cross : null;
                AtualizarCursor();
                FerramentaMudou?.Invoke();
            }
        }
        public bool Desenhando => ferr == Ferramenta3D.Barra || ferr == Ferramenta3D.Pilar || gripQuente != null || pedePonto != null || ferr == Ferramenta3D.Medir;   // Medir: cursor com snap (Vista3D.Snap.cs)
        public Point3D? InicioCorrente => inicio;   // 1º ponto da barra em andamento (null = nenhuma)
        public Point3D? PontoCursor => cursor3;    // ponto atraído sob o cursor (null = fora do plano)
        public string TipoAtracao => tipoSnap;      // Snap3D.Nome: "Extremidade", "Ponto médio", "Polar", "Ortogonal", "Grade", "Plano"...

        Point3D? inicio, cursor3; string tipoSnap; TipoSnap? tipoMarcado;
        Point mouseTela; bool mouseDentro;
        Point? caixaIni;
        readonly string[] din = { "", "" }; int dinCampo; string coord = "";
        readonly MarcaSnap marca; readonly TranslateTransform posMarca = new TranslateTransform();

        static string Chave(No3D n) => (n.Origem as RefMotor)?.Id ?? n.Id.ToString(CultureInfo.InvariantCulture);
        static string Chave(Barra3D b) => (b.Origem as RefMotor)?.Id ?? b.Id.ToString(CultureInfo.InvariantCulture);

        // Raio da câmera (ortográfica) pelo pixel até o plano. null = plano de perfil nesta vista.
        public Point3D? NoPlano(Point tela, PlanoTrabalho pl = null)
        {
            pl = pl ?? plano;
            var (o, d) = Raio(tela);   // ortográfica ou perspectiva (Vista3D.Navegacao.cs)
            double nd = Vector3D.DotProduct(pl.N, d);
            if (Math.Abs(nd) < 0.02) return null;
            double t = (pl.Valor - Vector3D.DotProduct(pl.N, (Vector3D)o)) / nd;
            if (perspectiva && t < 0) return null;   // plano atrás do olho
            return o + d * t;
        }

        static bool Ler(string t, out double v) => double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && double.IsFinite(v);
        bool DinTravado => inicio != null && (din[0] != "" || din[1] != "");
        void DinLimpar() { din[0] = din[1] = ""; dinCampo = 0; }

        // Ponto final: o do mouse, com comprimento/ângulo digitados no plano (o que não está travado vem do mouse).
        Point3D? PontoFinal()
        {
            if (!DinTravado || cursor3 == null) return cursor3;
            Point3D a = inicio.Value; Vector3D dv = cursor3.Value - a;
            double du = Vector3D.DotProduct(dv, plano.U), dw = Vector3D.DotProduct(dv, plano.W), dm = Math.Sqrt(du * du + dw * dw);
            double angM = dm < 1e-9 ? 0 : Math.Atan2(dw, du);
            bool tl = Ler(din[0], out double l), tg = Ler(din[1], out double g);
            double ang = tg ? g * Math.PI / 180 : angM, len = tl ? l : dm * Math.Max(0, Math.Cos(angM - ang));
            return a + plano.U * len * Math.Cos(ang) + plano.W * len * Math.Sin(ang);
        }

        // Coordenada digitada (AutoCAD): com 1º ponto, "x,y,z" é relativo; "@" relativo, "#" absoluto, "L<a" polar no plano. Snap3D.LerCoordenada.
        Point3D? PontoDigitado() => Snap3D.LerCoordenada(coord, BaseSnap, plano);

        void AtualizarCursor()
        {
            if (!Desenhando || !mouseDentro) { cursor3 = null; tipoSnap = null; }
            else (cursor3, tipoSnap) = AtrairSnap(mouseTela);
            Point3D? pf = PontoFinal();
            if (pf != null && tipoSnap != null)
            {
                Point s = Tela(pf.Value);
                marca.Tipo = DinTravado ? TipoSnap.Plano : snapRes?.Tipo ?? TipoSnap.Plano;
                posMarca.X = s.X; posMarca.Y = s.Y;
                marca.Visibility = Visibility.Visible;
                if (marca.Tipo != tipoMarcado) { tipoMarcado = marca.Tipo; marca.InvalidateVisual(); Anim.Entrar(marca, 0, Anim.Rapido); }   // aparece a cada troca de atração
            }
            else { marca.Visibility = Visibility.Collapsed; tipoMarcado = null; }
            CursorMudou?.Invoke(pf);
            sobre.InvalidateVisual();
        }

        // Mouse parado neste pixel (capturas de teste: elástico, atração e entrada dinâmica sem mouse de verdade).
        public void MoverTela(Point tela) { mouseTela = tela; mouseDentro = true; AtualizarCursor(); if (pedeObjeto != null) AtualizarRealce(tela); }

        // Clique na tela com a ferramenta atual (o mouse e as capturas de teste passam por aqui).
        public async Task ClicarTela(Point tela, ModifierKeys mods = ModifierKeys.None)
        {
            mouseTela = tela; mouseDentro = true;
            if (gripQuente == null && pedePonto == null && pedeObjeto != null) { await EscolherPedido(tela, mods); return; }   // o mesmo caminho do mouse (Clique)
            if (pedeValor) return;
            if (gripQuente == null && pedePonto == null && (ferr == Ferramenta3D.Selecionar || ferr == Ferramenta3D.Navegar)) { SelecionarClique(tela, mods); return; }
            AtualizarCursor();
            if (ferr == Ferramenta3D.Medir) { MedirClique(tela); return; }
            await Ponto(PontoFinal());
        }

        // Ponto confirmado (clique, Enter na entrada dinâmica ou coordenada digitada).
        async Task Ponto(Point3D? p)
        {
            if (p == null) { Aviso = "O plano de trabalho está de perfil nesta vista: gire a vista (Iso ou Topo)."; sobre.InvalidateVisual(); return; }
            Aviso = null;
            DinLimpar(); coord = "";
            if (gripQuente != null) { await SoltarGrip(p.Value); return; }       // Vista3D.Grips.cs
            if (pedePonto != null) { await PontoPedido(p.Value); return; }
            if (PontoSnap(p.Value)) { AtualizarCursor(); return; }   // gasta o snap único; Medir trata o ponto
            var segs = new List<(Point3D, Point3D)>();
            if (ferr == Ferramenta3D.Pilar)
            {
                double h = AlturaAndar?.Invoke(p.Value.Z) ?? 3;
                if (Math.Abs(h) < 1e-6) h = 3;
                Point3D q = p.Value + new Vector3D(0, 0, h);
                segs.Add(h > 0 ? (p.Value, q) : (q, p.Value));   // ni embaixo
            }
            else if (inicio == null) { inicio = p; AtualizarCursor(); return; }
            else
            {
                if ((p.Value - inicio.Value).Length < 1e-6) return;
                segs.Add((inicio.Value, p.Value));
                inicio = p;   // a corrente segue do último ponto
            }
            AtualizarCursor();
            if (CriarBarras != null) await CriarBarras(segs);
        }

        public string Aviso;   // mensagem da ferramenta (plano de perfil...) na etiqueta

        // Teclas da ferramenta. Devolve true se consumiu. A janela chama antes dos atalhos dela quando o documento é espacial.
        public bool Tecla(Key k)
        {
            if (TeclaSnap(k)) return true;   // F3, F8, F10, F11 e o Medir
            switch (k)
            {
                case Key.Escape:   // cascata: digitado > travado > grip quente > comando pedindo > barra em andamento > ferramenta > seleção
                    if (coord != "") coord = "";
                    else if (DinTravado) DinLimpar();
                    else if (gripQuente != null) { Esfriar(); return true; }
                    else if (Pedindo) { LimparPedido(); return true; }
                    else if (inicio != null) inicio = null;
                    else if (Desenhando) { Ferramenta = Ferramenta3D.Selecionar; return true; }
                    else if (Selecao.Count > 0 || Selecionado != null) { LimparSelecao(); Escolher(null); }
                    else return false;
                    AtualizarCursor();
                    return true;
                case Key.Enter: case Key.Space:   // Espaço = Enter (AutoCAD)
                    if (pedeObjeto != null || pedeValor) { TerminarPedido(); return true; }
                    if (!Desenhando) return false;   // nada em curso: a janela repete o último comando
                    if (coord != "") { Point3D? p = PontoDigitado(); if (p == null) { Aviso = "Coordenada inválida: use x,y,z  @dx,dy,dz  ou u,w no plano."; coord = ""; sobre.InvalidateVisual(); } else _ = Ponto(p); }
                    else if (DinTravado) _ = Ponto(PontoFinal());
                    else if (gripQuente != null) Esfriar();
                    else if (pedePonto != null) LimparPedido();                        // Enter vazio encerra o comando (COPIAR múltiplo…)
                    else if (inicio != null) { inicio = null; AtualizarCursor(); }   // Enter sem nada digitado termina a corrente
                    else Ferramenta = Ferramenta3D.Selecionar;                        // e, sem corrente, encerra a ferramenta
                    return true;
                case Key.Tab:
                    if (!Desenhando || inicio == null) return false;
                    dinCampo = 1 - dinCampo; AtualizarCursor(); return true;
                case Key.Back:
                    if (coord != "") coord = coord.Substring(0, coord.Length - 1);
                    else if (Desenhando && din[dinCampo] != "") din[dinCampo] = din[dinCampo].Substring(0, din[dinCampo].Length - 1);
                    else return false;
                    AtualizarCursor(); return true;
            }
            return false;
        }

        // Dígitos: comprimento/ângulo da entrada dinâmica; vírgula, ';' ou '@' passam a coordenada.
        public bool Digitar(string texto)
        {
            if (DigitarSnap(texto)) return true;   // letras: linha de comando (END, MID, ME...)
            if (!Desenhando || string.IsNullOrEmpty(texto)) return false;
            char c = texto[0];
            if (!(char.IsDigit(c) || c == '.' || c == '-' || c == ',' || c == ';' || c == '@')) return false;
            if (coord != "" || inicio == null || ferr == Ferramenta3D.Pilar || c == ',' || c == ';' || c == '@')
            {
                if (coord == "" && din[dinCampo] != "" && (c == ',' || c == ';')) { coord = din[dinCampo]; DinLimpar(); }
                if (coord.Length < 40) coord += c;
            }
            else if (din[dinCampo].Length < 9 && !(c == '.' && din[dinCampo].Contains(".")) && !(c == '-' && din[dinCampo] != "")) din[dinCampo] += c;
            Aviso = null;
            AtualizarCursor();
            return true;
        }

        // ---------- seleção ----------

        public void LimparSelecao()
        {
            if (Selecao.Count == 0) return;
            Selecao.Nos.Clear(); Selecao.Barras.Clear();
            Reconstruir(); SelecaoMudou?.Invoke();
        }

        // Troca (ou soma/tira) a seleção por ids. Para a edição (EdicaoEspacial.cs) e para a árvore.
        public void Selecionar(IEnumerable<string> nos, IEnumerable<string> barras, ModifierKeys mods = ModifierKeys.None)
        {
            bool tirar = mods.HasFlag(ModifierKeys.Control), somar = mods.HasFlag(ModifierKeys.Shift);
            if (!tirar && !somar) { Selecao.Nos.Clear(); Selecao.Barras.Clear(); }
            foreach (string n in nos ?? new string[0]) { if (tirar) Selecao.Nos.Remove(n); else Selecao.Nos.Add(n); }
            foreach (string b in barras ?? new string[0]) { if (tirar) Selecao.Barras.Remove(b); else Selecao.Barras.Add(b); }
            Reconstruir(); SelecaoMudou?.Invoke();
        }

        void SelecionarClique(Point tela, ModifierKeys mods)
        {
            object o = Acertar(tela);
            var nos = o is No3D n ? new[] { Chave(n) } : new string[0];
            var bs = o is Barra3D b ? new[] { Chave(b) } : new string[0];
            if (o == null && mods != ModifierKeys.None) return;   // Ctrl/Shift no vazio não limpa
            Selecionar(nos, bs, mods);
            if (mods == ModifierKeys.None) Escolher(o);           // um só: Propriedades mostra o objeto
        }

        // Esquerda -> direita = janela (só o que está inteiro dentro); direita -> esquerda = cruzamento (o que tocar).
        public void SelecionarCaixa(Point a, Point b, ModifierKeys mods = ModifierKeys.None)
        {
            var r = new Rect(a, b); bool cruz = b.X < a.X;
            var nos = mod.Nos.Where(n => r.Contains(Tela(P(n)))).Select(Chave).ToList();
            var bs = new List<string>();
            foreach (Barra3D x in mod.Barras)
            {
                No3D na = NoPorId(x.Ni), nc = NoPorId(x.Nj); if (na == null || nc == null) continue;
                Point p = Tela(P(na)), q = Tela(P(nc));
                if (r.Contains(p) && r.Contains(q) || cruz && (r.Contains(p) || r.Contains(q) || Corta(p, q, r))) bs.Add(Chave(x));
            }
            if (pedeSelecao)   // "Selecione objetos": só o tipo pedido; a janela soma e, com Shift, tira (Vista3D.Grips.cs)
            {
                if (filtroPedido == "barra") nos.Clear(); else if (filtroPedido == "no") bs.Clear();
                mods = mods.HasFlag(ModifierKeys.Shift) ? ModifierKeys.Control : ModifierKeys.Shift;
            }
            Selecionar(nos, bs, mods);
        }

        static bool Corta(Point p, Point q, Rect r)
        {
            Point[] c = { r.TopLeft, r.TopRight, r.BottomRight, r.BottomLeft };
            for (int i = 0; i < 4; i++) if (Segs(p, q, c[i], c[(i + 1) % 4])) return true;
            return false;
        }
        static bool Segs(Point a, Point b, Point c, Point d)
        {
            double Cr(Point o, Point x, Point y) => (x.X - o.X) * (y.Y - o.Y) - (x.Y - o.Y) * (y.X - o.X);
            return Cr(a, b, c) * Cr(a, b, d) <= 0 && Cr(c, d, a) * Cr(c, d, b) <= 0;
        }

        // ---------- desenho da entrada (sobreposição 2D) ----------

        static Pen Caneta(Brush b, double l, double[] traco = null)
        {
            var p = new Pen(b, l); if (traco != null) p.DashStyle = new DashStyle(traco, 0); p.Freeze(); return p;
        }
        static readonly Pen PGrade = Caneta(new SolidColorBrush(Color.FromArgb(0x70, 0x4A, 0x58, 0x6E)), 1);
        static readonly Pen PEixoPlano = Caneta(TemaPlant.BBordaFoco, 1, new double[] { 6, 4 });
        static readonly Pen PElastico = Caneta(TemaPlant.BMarcaSnap, 1.6);
        static readonly Typeface TfEntrada = new Typeface(TemaPlant.Fonte, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        static readonly Pen PGuia3 =Caneta(TemaPlant.BGuiaSnap, 1, new double[] { 2, 3 });

        void DesenharEntrada(DrawingContext dc, FrameworkElement alvoTxt)
        {
            FormattedText Ft(string s, Brush b, double tam) => new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, TfEntrada, tam, b, 1.0);
            if (Desenhando && ferr != Ferramenta3D.Medir && MostrarGradePlano) DesenharGrade(dc);
            DesenharEixos(dc);

            if (caixaIni != null && moveu)
            {
                Rect r = new Rect(caixaIni.Value, mouseTela); bool cruz = mouseTela.X < caixaIni.Value.X;
                dc.DrawRectangle(cruz ? TemaPlant.BCruzSelArea : TemaPlant.BJanelaSelArea, cruz ? Caneta(TemaPlant.BCruzSel, 1, new double[] { 4, 3 }) : Caneta(TemaPlant.BJanelaSel, 1), r);
            }

            Point3D? pf = PontoFinal();
            if (Desenhando && mouseDentro && pf != null)
            {
                Point s = Tela(pf.Value);
                if (inicio != null)   // elástico + medidas junto ao cursor
                {
                    Point a = Tela(inicio.Value);
                    if (cursor3 != null && DinTravado) dc.DrawLine(PGuia3, a, Tela(cursor3.Value));
                    dc.DrawLine(PElastico, a, s);
                    if (EntradaDinamica) CaixasDinamicas(dc, inicio.Value, pf.Value, s, Ft);
                }
                else if (ferr == Ferramenta3D.Pilar)
                {
                    double h = AlturaAndar?.Invoke(pf.Value.Z) ?? 3;
                    Point t = Tela(pf.Value + new Vector3D(0, 0, h));
                    dc.DrawLine(PElastico, s, t);
                    var ft = Ft("h = " + Num.Fmt(Math.Abs(h), 2) + " m" + (h < 0 ? " ↓" : ""), TemaPlant.BTexto1, 11);
                    dc.DrawRoundedRectangle(TemaPlant.BCaixaHud, null, new Rect(t.X + 10, t.Y - 4, ft.Width + 10, ft.Height + 4), 4, 4);
                    dc.DrawText(ft, new Point(t.X + 15, t.Y - 2));
                }
            }
            DesenharSnap(dc);   // caminhos de rastreio, pontos adquiridos, tooltip e medidas (Vista3D.Snap.cs)
            if (coord != "")
            {
                Point s = mouseDentro ? mouseTela : new Point(ActualWidth / 2, ActualHeight / 2);
                var ft = Ft("Coordenada: " + coord + "|", TemaPlant.BTexto1, 11.5);
                var r = new Rect(s.X + 18, s.Y + 22, Math.Max(168, ft.Width + 16), 22);
                dc.DrawRoundedRectangle(TemaPlant.BAcentoSutil, Caneta(TemaPlant.BBordaFoco, 1.5), r, 4, 4);
                dc.DrawText(ft, new Point(r.X + 8, r.Y + (r.Height - ft.Height) / 2));
            }

            // etiqueta da ferramenta (topo, centro)
            DesenharGrips(dc);
            DesenharRealce(dc);   // pré-realce do pedido (Vista3D.Grips.cs)
            string et = Aviso ?? (Prompt != null ? Prompt + (pedeSelecao ? " · " + Pl.N(Selecao.Count, "escolhido", "escolhidos") : "") + (Desenhando && tipoSnap != null ? " · plano " + plano.Nome + EstadoSnap() : "") : null);
            if (et == null && ferr == Ferramenta3D.Medir) et = EtiquetaMedir();
            else if (et == null && Desenhando)
                et = (ferr == Ferramenta3D.Barra ? (inicio == null ? "BARRA — clique o 1º ponto" : "BARRA — próximo ponto (Esc ou botão direito termina)")
                                                 : "PILAR — clique o ponto (até o nível de baixo; na base, até o de cima)")
                     + " · plano " + plano.Nome + EstadoSnap() + " · digite x,y,z ou @dx,dy,dz";
            else if (et == null && ferr == Ferramenta3D.Selecionar)
                et = Selecao.Count > 0 ? "SELECIONAR — " + Pl.N(Selecao.Nos.Count, "nó", "nós") + " · " + Pl.N(Selecao.Barras.Count, "barra", "barras") + " · Shift soma, Ctrl tira, Esc limpa"
                                       : "SELECIONAR — arraste: esq.→dir. janela, dir.→esq. cruzamento · Shift soma, Ctrl tira";
            if (et != null)
            {
                var ft = Ft(et, TemaPlant.BTexto1, 11.5);
                var r = new Rect(Math.Max(100, (ActualWidth - ft.Width) / 2 - 10), ActualHeight - ft.Height - 22, ft.Width + 20, ft.Height + 8);   // embaixo: o HUD e o seletor de vistas ocupam o topo
                dc.DrawRoundedRectangle(TemaPlant.BCaixaHud, new Pen(TemaPlant.BBordaSutil, 1), r, 6, 6);
                dc.DrawText(ft, new Point(r.X + 10, r.Y + 4));
            }
        }

        void CaixasDinamicas(DrawingContext dc, Point3D a, Point3D f, Point pc, Func<string, Brush, double, FormattedText> Ft)
        {
            Vector3D dv = f - a;
            double du = Vector3D.DotProduct(dv, plano.U), dw = Vector3D.DotProduct(dv, plano.W);
            string[] rot = { "Comprimento", "Ângulo" }, un = { "m", "°" };
            string[] val = { Num.Fmt(dv.Length, 3), Num.Fmt(Math.Atan2(dw, du) * 180 / Math.PI, 1) };
            double x0 = pc.X + 18, y0 = pc.Y + 22, w = 168, h = 22;
            if (x0 + w > ActualWidth - 4) x0 = pc.X - 18 - w;
            if (y0 + 2 * h + 22 > ActualHeight - 4) y0 = pc.Y - 22 - 2 * h - 20;
            for (int i = 0; i < 2; i++)
            {
                bool travado = din[i] != "", ativo = dinCampo == i;
                Rect r = new Rect(x0, y0 + i * (h + 2), w, h);
                dc.DrawRoundedRectangle(travado ? TemaPlant.BAcentoSutil : TemaPlant.BCaixaHud, Caneta(ativo ? TemaPlant.BBordaFoco : TemaPlant.BBordaSutil, ativo ? 1.5 : 1), r, 4, 4);
                var fr = Ft(rot[i], TemaPlant.BTexto2, 10.5);
                dc.DrawText(fr, new Point(r.X + 8, r.Y + (h - fr.Height) / 2));
                var fv = Ft(travado ? din[i] + (ativo ? "|" : "") : val[i], travado ? TemaPlant.BTexto1 : TemaPlant.BTexto2, 11.5);
                var fu = Ft(un[i], TemaPlant.BTexto2, 10.5);
                dc.DrawText(fu, new Point(r.Right - 8 - fu.Width, r.Y + (h - fu.Height) / 2));
                dc.DrawText(fv, new Point(r.Right - 12 - fu.Width - fv.Width, r.Y + (h - fv.Height) / 2));
            }
            var fd = Ft("Tab alterna · Enter confirma · Esc cancela", TemaPlant.BTexto2, 10);
            dc.DrawRectangle(TemaPlant.BCaixaHud, null, new Rect(x0, y0 + 2 * (h + 2), fd.Width + 8, fd.Height + 2));
            dc.DrawText(fd, new Point(x0 + 4, y0 + 2 * (h + 2) + 1));
        }

        // Grade do plano de trabalho na faixa do modelo (+ margem), eixos do plano tracejados.
        void DesenharGrade(DrawingContext dc)
        {
            Vector3D U = plano.U, W = plano.W;
            double u0 = -5, u1 = 5, w0 = -5, w1 = 5;
            foreach (No3D n in mod.Nos)
            {
                double u = Vector3D.DotProduct((Vector3D)P(n), U), w = Vector3D.DotProduct((Vector3D)P(n), W);
                u0 = Math.Min(u0, u); u1 = Math.Max(u1, u); w0 = Math.Min(w0, w); w1 = Math.Max(w1, w);
            }
            double passo = PassoGrade; while ((u1 - u0) / passo > 120 || (w1 - w0) / passo > 120) passo *= 2;   // ponytail: grade grossa em modelos enormes
            u0 = Math.Floor(u0 / passo - 2) * passo; u1 = Math.Ceiling(u1 / passo + 2) * passo;
            w0 = Math.Floor(w0 / passo - 2) * passo; w1 = Math.Ceiling(w1 / passo + 2) * passo;
            for (double u = u0; u <= u1 + 1e-9; u += passo) dc.DrawLine(Math.Abs(u) < 1e-9 ? PEixoPlano : PGrade, Tela(plano.Ponto(u, w0)), Tela(plano.Ponto(u, w1)));
            for (double w = w0; w <= w1 + 1e-9; w += passo) dc.DrawLine(Math.Abs(w) < 1e-9 ? PEixoPlano : PGrade, Tela(plano.Ponto(u0, w)), Tela(plano.Ponto(u1, w)));
        }

        // Marcador do AutoSnap (SnapOverlay.Marca: □ △ ○ ⊗ × ⊠ ⊥ ⧖ … //), com fade ao trocar.
        sealed class MarcaSnap : FrameworkElement
        {
            public TipoSnap Tipo;
            protected override void OnRender(DrawingContext dc) => SnapOverlay.Marca(dc, Tipo, new Point(0, 0));
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
                v.DesenharModelo(dc);          // barras, nós e apoios do analítico em linhas e pontos (Vista3D.Linhas.cs)
                v.DesenharEntrada(dc, this);   // plano, corrente, caixa de seleção, entrada dinâmica
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
                // chip das ligações propostas (grupos sem projeto, desenhados na cor CorProposta3D): canto inferior direito,
                // acima da legenda do aproveitamento
                if (v.Dx && v.render != null && v.render.Propostas > 0)
                {
                    var ft = Txt("Ligação proposta (sem projeto) · " + v.render.Propostas, TemaPlant.BTexto1, 11);
                    bool leg = v.MostrarLegenda && v.MostrarAproveitamento && v.mod.Barras.Any(b => b.Aprov.HasValue);
                    double w = ft.Width + 40, h = 24, x = ActualWidth - w - 14, y = ActualHeight - h - 14 - (leg ? 26 + 4 * 18 + 8 : 0);
                    dc.DrawRoundedRectangle(TemaPlant.B(TemaPlant.CorCaixaHud), new Pen(TemaPlant.BBordaSutil, 1), new Rect(x, y, w, h), 6, 6);
                    dc.DrawRoundedRectangle(TemaPlant.B(TemaPlant.CorProposta3D), null, new Rect(x + 10, y + 8, 18, 8), 2, 2);
                    dc.DrawText(ft, new Point(x + 34, y + (h - ft.Height) / 2));
                }
                if (v.mod.Nos.Count == 0 && !v.Desenhando)
                {
                    var ft = Txt(v.CriarBarras != null ? "Modelo vazio: digite L (barra) ou PI (pilar) e clique no plano — ou Modelo › Barra. Shift + arrastar orbita." : "Modelo vazio: desenhe barras na vista 2D.", TemaPlant.BTexto3, 12);
                    dc.DrawText(ft, new Point((ActualWidth - ft.Width) / 2, ActualHeight / 2));
                }
            }
        }
    }

}
