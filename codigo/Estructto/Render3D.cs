// Render3D: o modo Realista da Vista3D em Direct3D 11 (HelixToolkit.Wpf.SharpDX 3.1): sombra projetada entre peças (shadow
// map), oclusão ambiente (SSAO), materiais PBR (aço galvanizado/pintado/primer e concreto), MSAA + FXAA, arestas de 1 px,
// chão com degradê e grade, brilho de seleção por pós-efeito, recortes nas juntas e ligações (Render3DGeometria/Render3DLigacoes).
// A Vista3D continua dona da câmera, do mouse, dos diagramas, das cargas e da seleção: o Render3D só recebe a cena (Cena3D),
// devolve o picking (Acertar) e segue a câmera. Sem placa de vídeo D3D11 (só WARP) ele não existe e a Vista3D usa o Realista
// nativo (Viewport3D). Decisão e medições: docs/VISTA3D.md e docs/v3/RENDER.md.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Windows;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using HelixToolkit;
using HelixToolkit.Maths;
using HelixToolkit.SharpDX;
using HelixToolkit.Wpf.SharpDX;
using Contorno = NovoFtool.GeometriaPerfis.Contorno;
using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;
using HxCamera = HelixToolkit.Wpf.SharpDX.OrthographicCamera;
using HxMesh = HelixToolkit.SharpDX.MeshGeometry3D;
using HxLine = HelixToolkit.SharpDX.LineGeometry3D;

namespace NovoFtool
{
    // O que a Vista3D entrega para montar a cena (posições já deformadas, cores já tingidas pelo aproveitamento e seleção).
    public sealed class Cena3D
    {
        public Modelo3D Mod;
        public Func<Barra3D, (Point3D A, Point3D C)> Pontos;
        public Func<string, Contorno> Forma;
        public Func<Barra3D, Color> Cor;
        public Func<Barra3D, bool> Selecionada;
        public Func<No3D, double> Apoio;   // meio lado do bloco de apoio (0 = sem apoio)
        public double Raio, Z0; public Rect3D Caixa;
        public bool Amostras;              // sem ligações do motor: gera as de exemplo (LigacoesAmostra.Gerar)
    }

    public sealed class Render3D
    {
        public enum Acabamento { Galvanizado, Pintado, Primer }
        // ponytail: sem botão; ESTRUCTTO_3D_ACO=pintado|primer troca o acabamento do aço
        public static Acabamento Aco = Enum.TryParse(Environment.GetEnvironmentVariable("ESTRUCTTO_3D_ACO") ?? "", true, out Acabamento a) ? a : Acabamento.Galvanizado;

        static bool? disponivel;
        // Placa de vídeo com Direct3D 11 (nível 10.0+) que não seja o WARP: o D3DImage do Helix não funciona com o WARP
        // (estouro de pilha ao criar o dispositivo D3D9Ex) — medido em docs/VISTA3D.md. ESTRUCTTO_3D=nativo força o Viewport3D.
        public static bool Disponivel
        {
            get
            {
                if (disponivel != null) return disponivel.Value;
                disponivel = false;
                if (string.Equals(Environment.GetEnvironmentVariable("ESTRUCTTO_3D"), "nativo", StringComparison.OrdinalIgnoreCase)) return false;
                try
                {
                    using (var f = new SharpDX.DXGI.Factory1())
                        foreach (var ad in f.Adapters1)
                            using (ad)
                            {
                                var d = ad.Description1;
                                if ((d.Flags & SharpDX.DXGI.AdapterFlags.Software) != 0 || d.VendorId == 0x1414) continue;
                                if (SharpDX.Direct3D11.Device.GetSupportedFeatureLevel(ad) >= SharpDX.Direct3D.FeatureLevel.Level_10_0) { disponivel = true; break; }
                            }
                }
                catch { disponivel = false; }
                return disponivel.Value;
            }
        }

        public event Action Falhou;   // o D3D falhou em tempo de execução: a Vista3D volta ao nativo
        public readonly Viewport3DX Vista;
        readonly HxCamera cam = new HxCamera { NearPlaneDistance = 1, FarPlaneDistance = 100 };
        readonly HelixToolkit.Wpf.SharpDX.PerspectiveCamera pcam = new HelixToolkit.Wpf.SharpDX.PerspectiveCamera { FieldOfView = 45 };
        readonly MeshGeometryModel3D mAco, mConcreto, mSel, mChao, mParafusos, mSoldas;
        readonly LineGeometryModel3D lArestas;
        readonly ShadowMap3D sombra;
        readonly List<(MeshGeometryModel3D M, int[] Ini, object[] Obj)> faixas = new List<(MeshGeometryModel3D, int[], object[])>();
        int quadros, nLig; double largSecao = 0.3;

        static readonly Vector3D Luz = Norm(new Vector3D(0.45, 0.5, -0.74));          // chave: a mesma do Realista nativo (esquerda-alto da Iso)
        static readonly Vector3D LuzEnch = Norm(new Vector3D(-0.8, 0.25, -0.35));     // enchimento frio, do outro lado
        static readonly Vector3D LuzContra = Norm(new Vector3D(0.3, -0.85, -0.3));    // contraluz: desenha a silhueta por trás
        static Vector3D Norm(Vector3D v) { v.Normalize(); return v; }
        static Vector3D V3(Vector3D v) => v;

        public Render3D()
        {
            Vista = new Viewport3DX
            {
                EffectsManager = new DefaultEffectsManager(),
                Camera = cam,
                BackgroundColor = TemaPlant.BgCanvas,   // opaco: o fundo transparente do D3DImage sai branco no RenderTargetBitmap (capturas)
                ShowViewCube = false, ShowCoordinateSystem = false,
                IsRotationEnabled = false, IsPanEnabled = false, IsZoomEnabled = false, IsMoveEnabled = false,
                IsHitTestVisible = false, Focusable = false, EnableMouseButtonHitTest = false,
                MSAA = MSAALevel.Four, FXAALevel = FXAALevel.Medium,
                IsShadowMappingEnabled = true,
                EnableSSAO = true, SSAOQuality = SSAOQuality.High, SSAOIntensity = 1.0, SSAOSamplingRadius = 0.3f,
                EnableDesignModeRendering = false,
            };
            Vista.OnRendered += (s, e) => quadros++;
            // diagnóstico: ESTRUCTTO_3D_SEM=sombra,ssao,arestas,fxaa desliga partes do pipeline (capturas de comparação)
            string sem = Environment.GetEnvironmentVariable("ESTRUCTTO_3D_SEM") ?? "";
            if (sem.Contains("sombra")) Vista.IsShadowMappingEnabled = false;
            if (sem.Contains("ssao")) Vista.EnableSSAO = false;
            if (sem.Contains("fxaa")) Vista.FXAALevel = FXAALevel.None;
            Vista.RenderExceptionOccurred += (s, e) => { disponivel = false; Falhou?.Invoke(); };

            // estúdio: chave quente (sombra), enchimento frio, contraluz; somadas ficam abaixo de 1 para não estourar o aço claro
            Vista.Items.Add(new AmbientLight3D { Color = Color.FromRgb(0x30, 0x34, 0x3C) });
            Vista.Items.Add(new DirectionalLight3D { Color = Color.FromRgb(0xF2, 0xE8, 0xD6), Direction = Luz });          // 1ª direcional = sombra
            Vista.Items.Add(new DirectionalLight3D { Color = Color.FromRgb(0x30, 0x3A, 0x4C), Direction = LuzEnch });
            Vista.Items.Add(new DirectionalLight3D { Color = Color.FromRgb(0x48, 0x54, 0x6A), Direction = LuzContra });
            sombra = new ShadowMap3D { Resolution = new Size(4096, 4096), Intensity = 0.7, Bias = 0.0012, AutoCoverCompleteScene = true };
            Vista.Items.Add(sombra);
            Vista.Items.Add(new PostEffectMeshOutlineBlur { EffectName = "selecao", Color = TemaPlant.CorBarraSel, ScaleX = 1.2f, ScaleY = 1.2f, NumberOfBlurPass = 1 });   // halo justo: o pós-efeito passa por cima de tudo

            PBRMaterial Pbr(float metal, float rugo, float refl = 0.5f) => new PBRMaterial
            {
                AlbedoColor = new Color4(1, 1, 1, 1), MetallicFactor = metal, RoughnessFactor = rugo, ReflectanceFactor = refl,
                VertexColorBlendingFactor = 1f, RenderShadowMap = true, AmbientOcclusionFactor = 1f,
            };
            var (metal, rugo) = Aco == Acabamento.Primer ? (0.0f, 0.78f) : Aco == Acabamento.Pintado ? (0.08f, 0.58f) : (0.12f, 0.5f);
            // opaco: a passagem transparente do Helix ignora a profundidade das peças (grade aparecia por cima de tudo)
            mChao = new MeshGeometryModel3D { Material = Pbr(0f, 0.95f, 0.25f), IsThrowingShadow = false, CullMode = SharpDX.Direct3D11.CullMode.None };
            mConcreto = new MeshGeometryModel3D { Material = Pbr(0f, 0.9f, 0.35f), IsThrowingShadow = true };
            mAco = new MeshGeometryModel3D { Material = Pbr(metal, rugo), IsThrowingShadow = true };
            mSel = new MeshGeometryModel3D { Material = Pbr(metal, rugo), IsThrowingShadow = true, PostEffects = "selecao" };
            mParafusos = new MeshGeometryModel3D { Material = Pbr(0.6f, 0.42f), IsThrowingShadow = true };
            mSoldas = new MeshGeometryModel3D { Material = Pbr(0.2f, 0.85f), IsThrowingShadow = false };
            lArestas = new LineGeometryModel3D { Color = TemaPlant.CorArestaDx3D, Thickness = 0.9, Smoothness = 0.6, DepthBias = -4, IsThrowingShadow = false };
            foreach (var m in new Element3D[] { mChao, mConcreto, mAco, mSel, mParafusos, mSoldas, lArestas }) Vista.Items.Add(m);
        }

        // Segue a câmera da Vista3D: ortográfica (largOuFov = largura visível, m) ou perspectiva (largOuFov = FOV horizontal, graus).
        public void Camera(bool perspectiva, Point3D pos, Vector3D olhar, Vector3D cima, double largOuFov, double perto, double longe)
        {
            if (perspectiva)
            {
                pcam.Position = pos; pcam.LookDirection = olhar; pcam.UpDirection = cima; pcam.FieldOfView = largOuFov;
                pcam.NearPlaneDistance = perto; pcam.FarPlaneDistance = longe;
                if (Vista.Camera != pcam) Vista.Camera = pcam;
            }
            else
            {
                cam.Position = pos; cam.LookDirection = olhar; cam.UpDirection = cima; cam.Width = largOuFov;
                cam.NearPlaneDistance = perto; cam.FarPlaneDistance = longe;
                if (Vista.Camera != cam) Vista.Camera = cam;
            }
        }

        // ================= cena =================

        // Exposição: o PBR do Helix com as 3 luzes rende ~2,7× o albedo numa face horizontal iluminada; as cores do
        // TemaPlant são as cores-alvo na tela, então o albedo entra dividido por isso.
        // Chão (horizontal, recebe as 3 luzes): 0,42. Peças (faces verticais recebem menos luz e o SSAO escurece as quinas): 0,6.
        const float Expo = 0.42f, ExpoPecas = 0.6f;
        static Color4 Cor(Color c, float expo = ExpoPecas) { var k = MalhaDx.Cor(c); return new Color4(k.Red * expo, k.Green * expo, k.Blue * expo, 1); }
        static Color CorAco => Aco == Acabamento.Primer ? TemaPlant.CorPrimer3D : Aco == Acabamento.Pintado ? TemaPlant.CorAcoPintado3D : TemaPlant.CorAco3D;
        public static Color CorBase(bool concreto) => concreto ? TemaPlant.CorConcreto3D : CorAco;

        public void Montar(Cena3D c)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Modelo3D mod = c.Mod;
            var nos = new Dictionary<int, No3D>();
            foreach (No3D n in mod.Nos) if (!nos.ContainsKey(n.Id)) nos[n.Id] = n;
            var barras = mod.Barras.ToDictionary(b => b.Id, b => b);
            Func<Barra3D, Contorno> forma = b => c.Forma(b.Secao);
            double FolgaPadrao(Barra3D b, bool emI)
            {
                if (forma(b).Concreto) return 0;
                if (!nos.TryGetValue(b.Ni, out No3D a) || !nos.TryGetValue(b.Nj, out No3D d)) return 0.012;
                double dz = Math.Abs(d.Z - a.Z), len = (P(d) - P(a)).Length;
                return dz > 0.2 * len && dz < 0.95 * len ? 0.04 : 0.012;   // diagonal: espaço do gusset
            }
            var cortes = Render3DGeometria.Juntas(mod, c.Pontos, forma, FolgaPadrao, (b, emI) => double.NaN);

            // ligações: as do modelo, ou os exemplos (capturas / ESTRUCTTO_3D_AMOSTRAS=1)
            var ligs = new List<Ligacao3D>(mod.Ligacoes);
            if (ligs.Count == 0 && c.Amostras) ligs.AddRange(LigacoesAmostra.Gerar(mod, cortes, forma, FolgaPadrao));
            if (ligs.Count > 0)
            {
                var porPonta = new Dictionary<(int, bool), Ligacao3D>();
                foreach (Ligacao3D l in ligs) if (barras.TryGetValue(l.Barra, out Barra3D b)) porPonta[(l.Barra, l.No == b.Ni)] = l;
                Ligacao3D Lig(Barra3D b, bool emI) => porPonta.TryGetValue((b.Id, emI), out Ligacao3D l) ? l : null;
                cortes = Render3DGeometria.Juntas(mod, c.Pontos, forma,
                    (b, emI) => { double r = Lig(b, emI)?.RecuoDe("viga") ?? double.NaN; return double.IsNaN(r) ? FolgaPadrao(b, emI) : r; },
                    (b, emI) => { Ligacao3D l = Lig(b, emI); if (l == null) return double.NaN; double x = l.PontaDe("viga"); return double.IsNaN(x) ? l.RecuoDe("pilar") : x; });
            }
            nLig = ligs.Count;

            // peças
            var mallAco = new MalhaDx(); var mallConc = new MalhaDx(); var mallSel = new MalhaDx(); var arestas = new MalhaDx();
            double somaLarg = 0; int nLarg = 0;
            foreach (Barra3D b in mod.Barras)
            {
                var (a, d) = c.Pontos(b); Vector3D e = d - a; if (e.Length < 1e-9) continue; e.Normalize();
                Vector3D v = Modelo3D.EixoY(e, b.Rot + b.RotPerfil), u = Vector3D.CrossProduct(v, e);
                Contorno f = forma(b);
                Point dc = GeometriaPerfis.Cardinal(f, b.Cardinal); Vector3D desl = u * dc.X + v * dc.Y;   // ponto cardinal: o eixo da barra fica nesse ponto da seção
                a += desl; d += desl;
                somaLarg += f.Larg; nLarg++;
                MalhaDx alvo = c.Selecionada(b) ? mallSel : f.Concreto ? mallConc : mallAco;
                alvo.Marcar(b);
                cortes.TryGetValue((b, true), out Corte ci); cortes.TryGetValue((b, false), out Corte cj);
                Render3DGeometria.Extrudar(alvo, f, a, d, u, v, ci, cj, Cor(c.Cor(b)), false);
                Render3DGeometria.Extrudar(arestas, f, a, d, u, v, ci, cj, Cor(c.Cor(b)), true, false);   // só as arestas
            }
            largSecao = nLarg > 0 ? somaLarg / nLarg : 0.3;

            // pedestais dos apoios (concreto)
            Color4 corConc = Cor(TemaPlant.CorConcreto3D);
            foreach (No3D n in mod.Nos)
            {
                double s = c.Apoio(n); if (s <= 0) continue;
                mallConc.Caixa(new Point3D(n.X, n.Y, n.Z - s * 0.7), 2 * s, 2 * s, 1.4 * s, corConc, true);
            }

            // ligações (contrato → mundo)
            var mallParaf = new MalhaDx(); var mallSolda = new MalhaDx();
            Color4 corChapa = Cor(Aco == Acabamento.Galvanizado ? TemaPlant.CorChapa3D : CorAco), corParaf = Cor(TemaPlant.CorParafuso3D), corSolda = Cor(TemaPlant.CorSolda3D);
            foreach (Ligacao3D l in ligs)
            {
                if (!barras.TryGetValue(l.Barra, out Barra3D b)) continue;
                var (a, d) = c.Pontos(b); Vector3D e = d - a; if (e.Length < 1e-9) continue; e.Normalize();
                bool emI = l.No == b.Ni;
                Vector3D x = emI ? e : -e, z = Modelo3D.EixoY(e, b.Rot + b.RotPerfil), y = Vector3D.CrossProduct(x, z);   // contrato: y = x × z (canhoto)
                Point dc = GeometriaPerfis.Cardinal(forma(b), b.Cardinal);
                Point3D o = (emI ? a : d) + Vector3D.CrossProduct(z, e) * dc.X + z * dc.Y;   // origem no eixo do perfil deslocado
                Point3D W(Vector3D p) => o + (x * p.X + y * p.Y + z * p.Z) / 1000;
                Vector3D Wv(Vector3D p) { Vector3D r = x * p.X + y * p.Y + z * p.Z; if (r.Length > 1e-12) r.Normalize(); return r; }
                foreach (var ch in l.Chapas)
                {
                    Vector3D nn = Wv(ch.Normal), uu = Wv(ch.U); if (nn.Length < 0.5 || uu.Length < 0.5) continue;
                    Vector3D ww = Wv(Vector3D.CrossProduct(ch.Normal, ch.U));   // no local: o referencial do contrato (y = x × z) é canhoto, o produto no mundo inverteria
                    mallAco.Chapa(W(ch.Origem), uu, ww, nn, ch.Contorno.Select(p => new Point(p.X / 1000, p.Y / 1000)).ToArray(), ch.Espessura / 1000, corChapa, false);
                    arestas.Chapa(W(ch.Origem), uu, ww, nn, ch.Contorno.Select(p => new Point(p.X / 1000, p.Y / 1000)).ToArray(), ch.Espessura / 1000, corChapa, true, false);
                }
                foreach (var ca in l.Cantoneiras)
                {
                    Perfil3D p = PerfilL(ca.Perfil); if (p == null) continue;
                    Contorno f = GeometriaPerfis.CriarContorno(p, 0.05);
                    Vector3D ex = Wv(ca.Eixo), uu = Wv(ca.U); if (ex.Length < 0.5 || uu.Length < 0.5) continue;
                    Vector3D vv = Vector3D.CrossProduct(ex, uu);   // ponytail: triedro direito no mundo (a aba pode sair espelhada no referencial canhoto)
                    Point3D pa = W(ca.Origem), pb = pa + ex * (ca.Comprimento / 1000);
                    Render3DGeometria.Extrudar(mallAco, f, pa, pb, uu, vv, null, null, corChapa, false);
                    Render3DGeometria.Extrudar(arestas, f, pa, pb, uu, vv, null, null, corChapa, true, false);
                }
                foreach (var pf in l.Parafusos) Render3DGeometria.Parafuso(mallParaf, W(pf.Origem), Wv(pf.Eixo), pf.D / 1000, pf.Comprimento / 1000, corParaf);
                foreach (var sd in l.Soldas) if (sd.Pontos != null && sd.Pontos.Length >= 2) Render3DGeometria.Cordao(mallSolda, sd.Pontos.Select(W).ToList(), sd.Perna / 1000, corSolda);
            }

            var mallChao = new MalhaDx(); Chao(c, mallChao);

            faixas.Clear();
            Aplicar(mAco, mallAco); Aplicar(mConcreto, mallConc); Aplicar(mSel, mallSel); Aplicar(mParafusos, mallParaf); Aplicar(mSoldas, mallSolda); Aplicar(mChao, mallChao);
            if (arestas.L.Count > 0 && !(Environment.GetEnvironmentVariable("ESTRUCTTO_3D_SEM") ?? "").Contains("arestas"))
            {
                lArestas.Geometry = new HxLine { Positions = new Vector3Collection(arestas.L), Indices = new IntCollection(Enumerable.Range(0, arestas.L.Count)) };
                lArestas.Visibility = Visibility.Visible;
            }
            else lArestas.Visibility = Visibility.Collapsed;

            Vista.SSAOSamplingRadius = (float)Math.Max(0.12, Math.Min(0.6, largSecao * 0.9));
            UltimaMontagemMs = sw.Elapsed.TotalMilliseconds;
            Vista.InvalidateRender();
        }

        public double UltimaMontagemMs { get; private set; }

        void Aplicar(MeshGeometryModel3D m, MalhaDx malha)
        {
            if (malha.Vazia) { m.Visibility = Visibility.Collapsed; return; }
            m.Geometry = new HxMesh { Positions = new Vector3Collection(malha.P), Normals = new Vector3Collection(malha.N), Colors = new Color4Collection(malha.C), Indices = new IntCollection(malha.T) };
            m.Visibility = Visibility.Visible;
            if (malha.Obj.Count > 0) faixas.Add((m, malha.Ini.ToArray(), malha.Obj.ToArray()));
        }

        static Point3D P(No3D n) => new Point3D(n.X, n.Y, n.Z);
        static double Suave(double t) { t = Math.Max(0, Math.Min(1, t)); return t * t * (3 - 2 * t); }

        static Perfil3D PerfilL(string nome)   // "L76x6.3" (mm) → cantoneira de abas iguais; ponytail: só esse formato
        {
            if (string.IsNullOrEmpty(nome) || nome[0] != 'L') return null;
            var p = nome.Substring(1).Replace(',', '.').Split('x', 'X');
            if (p.Length < 2 || !double.TryParse(p[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double b)
                || !double.TryParse(p[p.Length - 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double t)) return null;
            return new Perfil3D { Forma = "L", H = b / 1000, B = b / 1000, Tw = t / 1000, Tf = t / 1000 };
        }

        // Chão: disco com degradê radial (cor do chão → cor do canvas, some no fundo) e grade de linhas que esmaece junto.
        void Chao(Cena3D c, MalhaDx m)
        {
            Rect3D cx = c.Caixa; double raio = c.Raio, z = c.Z0;
            double xm = cx.X + cx.SizeX / 2, ym = cx.Y + cx.SizeY / 2, R = Math.Max(cx.SizeX, cx.SizeY) / 2 + raio * 1.3;
            Color cc = TemaPlant.CorChao3D, cf = TemaPlant.BgCanvas, cg = TemaPlant.CorGrade3D;   // a borda chega à cor do canvas (Expo) e some
            Color cfA = cf;
            Color4 CorR(double r, Color centro) { double t = Suave((r / R - 0.2) / 0.8); return Cor(Mistura(centro, cfA, t), Expo); }
            const int seg = 72; double[] aneis = { 0.3, 0.55, 0.75, 0.9, 1.0, 3.0 };   // o último anel já é a cor do canvas: a borda fica fora da tela
            var ant = Enumerable.Repeat(new Point3D(xm, ym, z), seg + 1).ToArray(); double rAnt = 0;
            foreach (double fr in aneis)
            {
                double r = fr * R; var cur = new Point3D[seg + 1];
                for (int k = 0; k <= seg; k++) { double t = 2 * Math.PI * k / seg; cur[k] = new Point3D(xm + r * Math.Cos(t), ym + r * Math.Sin(t), z); }
                for (int k = 0; k < seg; k++)
                {
                    if (rAnt == 0) Tri(m, cur[k + 1], ant[k], cur[k], CorR(r, cc), CorR(0, cc), CorR(r, cc));
                    else Quad(m, ant[k], cur[k], cur[k + 1], ant[k + 1], CorR(rAnt, cc), CorR(r, cc), CorR(r, cc), CorR(rAnt, cc));
                }
                ant = cur; rAnt = r;
            }
            // grade: passo igual ao do analítico, linhas finas um fio acima do chão, cor que some com o raio
            double passo = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(1e-6, raio / 2)))); if (raio / passo > 12) passo *= 2.5; if (raio / passo > 12) passo *= 2;
            double g0x = Math.Floor((xm - R) / passo) * passo, g1x = Math.Ceiling((xm + R) / passo) * passo, g0y = Math.Floor((ym - R) / passo) * passo, g1y = Math.Ceiling((ym + R) / passo) * passo;
            double w = raio * 0.0012, zg = z + raio * 0.0006;
            Color4 CorG(double x, double y) { double r = Math.Sqrt((x - xm) * (x - xm) + (y - ym) * (y - ym)); double t = Suave((r / R - 0.05) / 0.7); return Cor(Mistura(cg, Mistura(cc, cfA, Suave((r / R - 0.2) / 0.8)), t), Expo); }
            bool Dentro(double x, double y) => (x - xm) * (x - xm) + (y - ym) * (y - ym) <= R * R * 0.8;
            for (double x = g0x; x <= g1x + 1e-9; x += passo)
                for (double y = g0y; y < g1y - 1e-9; y += passo)
                    if (Dentro(x, y) || Dentro(x, y + passo)) Quad(m, new Point3D(x - w, y, zg), new Point3D(x + w, y, zg), new Point3D(x + w, y + passo, zg), new Point3D(x - w, y + passo, zg), CorG(x, y), CorG(x, y), CorG(x, y + passo), CorG(x, y + passo));
            for (double y = g0y; y <= g1y + 1e-9; y += passo)
                for (double x = g0x; x < g1x - 1e-9; x += passo)
                    if (Dentro(x, y) || Dentro(x + passo, y)) Quad(m, new Point3D(x, y - w, zg), new Point3D(x + passo, y - w, zg), new Point3D(x + passo, y + w, zg), new Point3D(x, y + w, zg), CorG(x, y), CorG(x + passo, y), CorG(x + passo, y), CorG(x, y));
        }

        static void Tri(MalhaDx m, Point3D a, Point3D b, Point3D c, Color4 ca, Color4 cb, Color4 cc)
        {
            int i = m.P.Count; Vector3 n = new Vector3(0, 0, 1);
            m.P.Add(MalhaDx.V(a)); m.P.Add(MalhaDx.V(b)); m.P.Add(MalhaDx.V(c)); m.N.Add(n); m.N.Add(n); m.N.Add(n); m.C.Add(ca); m.C.Add(cb); m.C.Add(cc);
            m.T.Add(i); m.T.Add(i + 1); m.T.Add(i + 2);
        }
        static void Quad(MalhaDx m, Point3D a, Point3D b, Point3D c, Point3D d, Color4 ca, Color4 cb, Color4 cc, Color4 cd)
        {
            int i = m.P.Count; Vector3 n = new Vector3(0, 0, 1);
            foreach (var p in new[] { a, b, c, d }) { m.P.Add(MalhaDx.V(p)); m.N.Add(n); }
            m.C.Add(ca); m.C.Add(cb); m.C.Add(cc); m.C.Add(cd);
            m.T.Add(i); m.T.Add(i + 1); m.T.Add(i + 2); m.T.Add(i); m.T.Add(i + 2); m.T.Add(i + 3);
        }
        static Color Mistura(Color a, Color b, double t) => Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

        // ================= nível de detalhe, picking, captura =================

        // pxPorM = pixels por metro na tela. Arestas só quando a seção tem ≥ 6 px; parafusos e soldas só perto ou em modelos
        // com poucas ligações (≤ 80).
        public void Lod(double pxPorM)
        {
            bool ar = pxPorM * largSecao >= 6, det = nLig <= 80 || pxPorM * largSecao >= 40;
            Visibility va = ar && lArestas.Geometry != null ? Visibility.Visible : Visibility.Collapsed;
            if (lArestas.Visibility != va) lArestas.Visibility = va;
            foreach (var m in new[] { mParafusos, mSoldas })
            {
                Visibility vd = det && m.Geometry != null ? Visibility.Visible : Visibility.Collapsed;
                if (m.Visibility != vd) m.Visibility = vd;
            }
        }

        // Barra sob o pixel (coordenadas da Vista): a mais próxima da câmera; chapas, parafusos e chão não contam.
        public object Acertar(Point p)
        {
            var hits = Vista.FindHits(p);
            if (hits == null) return null;
            foreach (var h in hits.OrderBy(h => h.Distance))
            {
                foreach (var (m, ini, obj) in faixas)
                {
                    if (h.ModelHit != m && h.ModelHit != m.SceneNode) continue;
                    int vi = h.TriangleIndices.Item1;
                    int k = Array.BinarySearch(ini, vi);
                    return obj[k >= 0 ? k : Math.Max(0, ~k - 1)];
                }
            }
            return null;
        }

        // Captura (RenderTargetBitmap da janela): o D3DImage precisa de 2 quadros novos antes de a janela ser lida.
        public void PrepararCaptura()
        {
            int alvo = quadros + 2;
            Vista.InvalidateRender();
            long t0 = Environment.TickCount64;
            while (quadros < alvo && Environment.TickCount64 - t0 < 2000)
            {
                Vista.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
                Thread.Sleep(15);
                Vista.InvalidateRender();
            }
        }

        // Tempo de um quadro (ms), medido com n quadros forçados (capturas de desempenho).
        public double MedirQuadro(int n = 20)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew(); int ini = quadros;
            long t0 = Environment.TickCount64;
            while (quadros < ini + n && Environment.TickCount64 - t0 < 5000) { Vista.InvalidateRender(); Vista.Dispatcher.Invoke(() => { }, DispatcherPriority.Render); }
            return quadros > ini ? sw.Elapsed.TotalMilliseconds / (quadros - ini) : double.NaN;
        }
    }
}
