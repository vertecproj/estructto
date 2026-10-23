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
        public System.Windows.Media.Media3D.MeshGeometry3D Cargas, NosSel;   // setas e nós escolhidos com teste de profundidade (null = nenhum)   // setas das cargas com teste de profundidade (null = nenhuma)
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
        readonly MeshGeometryModel3D mDecal, mAco, mConcreto, mSel, mChao, mParafusos, mSoldas, mProp, mPropDet, mCargas, mNos;
        readonly LineGeometryModel3D lArestas, lGrade;
        readonly ShadowMap3D sombra;
        public bool Sombra { set { Vista.IsShadowMappingEnabled = value && !(Environment.GetEnvironmentVariable("ESTRUCTTO_3D_SEM") ?? "").Contains("sombra"); } }
        readonly List<(MeshGeometryModel3D M, int[] Ini, object[] Obj)> faixas = new List<(MeshGeometryModel3D, int[], object[])>();
        int quadros, nLig; double largSecao = 0.3;
        // --testes-interferencia: não nulo = Montar guarda aqui cada sólido (barra, chapa, cantoneira; parafusos e soldas não)
        public static List<Peca3D> Coleta;
        // Sólido da cena guardado à parte (teste de interferência, exportação). Barra = a barra dona (cada trecho do cope é um
        // sólido fechado, Trecho = true); Lig = a ligação dona (chapa, cantoneira, parafuso). Excluir: barra curta demais para a
        // ligação (motor: excluir_interferencia/BarraCurta; WPF: recuos das duas pontas ≥ comprimento) — fora da interferência.
        public sealed class Peca3D { public string Nome, Tipo; public MalhaDx M; public Barra3D Barra; public Ligacao3D Lig; public bool Trecho, Excluir; public Point3D[] Apertadas; }   // Apertadas: pontos do fuste no meio de cada peça de `pilha` (C4: têm de estar dentro de uma barra)
        // Barras curtas demais para a ligação na última cena montada (IdBarra): o gerenciador avisa.
        public static readonly HashSet<string> Curtas = new HashSet<string>();
        // G6 r4 (diagnóstico do --testes-interferencia): causa por barra curta — "recuos", "motor" (recusa x_max) ou "guarda" (AparaEm)
        public static readonly Dictionary<string, string> CurtasCausa = new Dictionary<string, string>();
        public static readonly HashSet<string> Locais = new HashSet<string>();   // G6: barras com chapa de proposta local (sem ligação do motor), para o gerenciador
        public static Dictionary<(Barra3D, bool), Corte> CortesColeta; public static Dictionary<(int, bool), Ligacao3D> LigsColeta;   // diagnóstico do teste
        public int Propostas { get; private set; }   // ligações propostas desenhadas (chip na vista)
        public const int MaxPropostas = 600;         // ponytail: modelos enormes ficam com as 600 primeiras; subir se pedirem

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
            Vista.RenderExceptionOccurred += ErroRender;
            // E1 fix: registra o adaptador usado (diagnóstico de "cena escura"). WARP/software já é recusado por Disponivel (cai no nativo); se o dispositivo
            // falhar em tempo de execução, ErroRender devolve a Vista3D ao nativo, então SSAO/ambiente quebrados não chegam a desenhar um disco escuro.
            try
            {
                using (var f = new SharpDX.DXGI.Factory1())
                    foreach (var ad in f.Adapters1) using (ad) { var d = ad.Description1; Registrar("adaptador DXGI: " + d.Description + ((d.Flags & SharpDX.DXGI.AdapterFlags.Software) != 0 ? " (software)" : "") + "; SSAO " + (Vista.EnableSSAO ? "ligado" : "desligado")); }
            }
            catch (Exception ex) { Registrar("adaptador: erro " + ex.Message); }
            Vista.SizeChanged += (s, e) => pcam.FieldOfView = Vfov();   // o FOV vertical depende do aspecto

            // estúdio: chave quente (sombra), enchimento frio, contraluz; somadas ficam abaixo de 1 para não estourar o aço claro
            Vista.Items.Add(new AmbientLight3D { Color = sem.Contains("ambiente") ? Colors.Black : Color.FromRgb(0x30, 0x34, 0x3C) });   // diagnóstico: ambiente 0
            Vista.Items.Add(new DirectionalLight3D { Color = Color.FromRgb(0xF2, 0xE8, 0xD6), Direction = Luz });          // 1ª direcional = sombra
            Vista.Items.Add(new DirectionalLight3D { Color = Color.FromRgb(0x30, 0x3A, 0x4C), Direction = LuzEnch });
            Vista.Items.Add(new DirectionalLight3D { Color = Color.FromRgb(0x48, 0x54, 0x6A), Direction = LuzContra });
            sombra = new ShadowMap3D { Resolution = new Size(4096, 4096), Intensity = 0.55, Bias = 0.0012, AutoCoverCompleteScene = true };
            Vista.Items.Add(sombra);
            Vista.Items.Add(new PostEffectMeshOutlineBlur { EffectName = "selecao", Color = TemaPlant.CorBarraSel, ScaleX = 1.2f, ScaleY = 1.2f, NumberOfBlurPass = 1 });   // halo justo: o pós-efeito passa por cima de tudo

            // reflexo: céu de estúdio procedural (só nos materiais; o fundo continua o canvas)
            Vista.Items.Add(new EnvironmentMap3D { Texture = TextureModel.Create(CeuDds()), SkipRendering = true });
            PBRMaterial Pbr(float metal, float rugo, float refl = 0.5f, bool ceu = false) => new PBRMaterial
            {
                AlbedoColor = new Color4(1, 1, 1, 1), MetallicFactor = metal, RoughnessFactor = rugo, ReflectanceFactor = refl,
                VertexColorBlendingFactor = 1f, RenderShadowMap = true, AmbientOcclusionFactor = 1f, RenderEnvironmentMap = ceu,
                EmissiveColor = new Color4(0.075f, 0.08f, 0.09f, 1),   // piso de legibilidade: sem luz/SSAO/ambiente as pecas ainda aparecem
            };
            var (metal, rugo) = Aco == Acabamento.Primer ? (0.0f, 0.78f) : Aco == Acabamento.Pintado ? (0.08f, 0.5f) : (0.3f, 0.5f);   // galvanizado: metálico com o céu
            // opaco: a passagem transparente do Helix ignora a profundidade das peças (grade aparecia por cima de tudo)
            // chao e decal: so emissivo + alfa por vertice (E1 fix; ver Chao). Transparentes: desenham depois das pecas, com teste de profundidade.
            mChao = new MeshGeometryModel3D { Material = new PhongMaterial { DiffuseColor = new Color4(0, 0, 0, 1), AmbientColor = new Color4(0, 0, 0, 1), SpecularColor = new Color4(0, 0, 0, 1), EmissiveColor = MalhaDx.Cor(TemaPlant.CorChao3D), VertexColorBlendingFactor = 1f }, IsTransparent = true, IsThrowingShadow = false, CullMode = SharpDX.Direct3D11.CullMode.None };
            mDecal = new MeshGeometryModel3D { Material = new PhongMaterial { DiffuseColor = new Color4(0, 0, 0, 1), AmbientColor = new Color4(0, 0, 0, 1), SpecularColor = new Color4(0, 0, 0, 1), EmissiveColor = new Color4(0, 0, 0, 1), VertexColorBlendingFactor = 1f }, IsTransparent = true, IsThrowingShadow = false, CullMode = SharpDX.Direct3D11.CullMode.None };
            lGrade = new LineGeometryModel3D { Color = System.Windows.Media.Colors.White, Thickness = 0.8, Smoothness = 0.4, IsThrowingShadow = false, Visibility = Visibility.Collapsed };
            mConcreto = new MeshGeometryModel3D { Material = Pbr(0f, 0.9f, 0.35f), IsThrowingShadow = true };
            mAco = new MeshGeometryModel3D { Material = Pbr(metal, rugo, 0.5f, true), IsThrowingShadow = true };
            mSel = new MeshGeometryModel3D { Material = Pbr(metal, rugo, 0.5f, true), IsThrowingShadow = true, PostEffects = "selecao" };
            mParafusos = new MeshGeometryModel3D { Material = Pbr(0.7f, 0.38f, 0.5f, true), IsThrowingShadow = true };
            mSoldas = new MeshGeometryModel3D { Material = Pbr(0.2f, 0.85f), IsThrowingShadow = false };
            // propostas (grupos sem projeto): fosco e frio, um tom fora do aço (TemaPlant.CorProposta3D, chip na vista)
            mProp = new MeshGeometryModel3D { Material = Pbr(0f, 0.75f, 0.35f), IsThrowingShadow = true };
            mPropDet = new MeshGeometryModel3D { Material = Pbr(0f, 0.75f, 0.35f), IsThrowingShadow = false };
            // sólidos fechados com as normais para fora (Render3DGeometria.Fechada nos testes): a face de trás some. Assim a arruela
            // encostada na chapa, a solda na peça e a chapa no pilar não disputam o mesmo plano (z-fighting) e nada parece oco.
            foreach (var m in new[] { mConcreto, mAco, mSel, mParafusos, mSoldas, mProp, mPropDet }) m.CullMode = SharpDX.Direct3D11.CullMode.Back;
            mCargas = new MeshGeometryModel3D { Material = new PhongMaterial { DiffuseColor = new Color4(0, 0, 0, 1), EmissiveColor = MalhaDx.Cor(TemaPlant.CorCarga) }, IsThrowingShadow = false, IsHitTestVisible = false, CullMode = SharpDX.Direct3D11.CullMode.None };
            mNos = new MeshGeometryModel3D { Material = new PhongMaterial { DiffuseColor = new Color4(0, 0, 0, 1), EmissiveColor = MalhaDx.Cor(TemaPlant.CorBarraSel) }, IsThrowingShadow = false, IsHitTestVisible = false, CullMode = SharpDX.Direct3D11.CullMode.None };
            lArestas = new LineGeometryModel3D { Color = TemaPlant.CorArestaDx3D, Thickness = 0.9, Smoothness = 0.6, DepthBias = -4, IsThrowingShadow = false };
            foreach (var m in new Element3D[] { mChao, mDecal, lGrade, mConcreto, mAco, mSel, mParafusos, mSoldas, mProp, mPropDet, mCargas, mNos, lArestas }) Vista.Items.Add(m);
            mChao.IsHitTestVisible = mDecal.IsHitTestVisible = lGrade.IsHitTestVisible = lArestas.IsHitTestVisible = false;   // picking (Acertar) mais barato: o chão e as arestas não contam
        }

        // Mapa de ambiente procedural: cubo DDS BGRA8 n×n com mips (o PBR lê o mip pela rugosidade). Mundo com Z para cima:
        // zênite azul-acinzentado, horizonte claro, chão escuro quente e uma janela de luz larga do lado da luz chave (-Luz).
        // Faces na ordem do D3D (+X −X +Y −Y +Z −Z), direção do texel (s, t) pela convenção do D3D.
        static System.IO.MemoryStream CeuDds(int n = 64)
        {
            int mips = (int)Math.Log2(n) + 1;
            var ms = new System.IO.MemoryStream(); var w = new System.IO.BinaryWriter(ms);
            w.Write(0x20534444u);   // "DDS "
            uint[] h = new uint[31];
            h[0] = 124; h[1] = 0x1 | 0x2 | 0x4 | 0x1000 | 0x20000; h[2] = (uint)n; h[3] = (uint)n; h[4] = (uint)(n * 4); h[6] = (uint)mips;
            h[18] = 32; h[19] = 0x41; h[21] = 32; h[22] = 0x00FF0000; h[23] = 0x0000FF00; h[24] = 0x000000FF; h[25] = 0xFF000000;   // pixel format BGRA8
            h[26] = 0x1000 | 0x8 | 0x400000; h[27] = 0x200 | 0xFC00;   // textura, complexa, mips; cubo com as 6 faces
            foreach (uint x in h) w.Write(x);
            Vector3D sol = -Luz;
            Vector3D Ceu(Vector3D d)
            {
                d.Normalize(); double z = d.Z;
                Vector3D zen = new Vector3D(0.50, 0.56, 0.66), hor = new Vector3D(0.80, 0.81, 0.83), chao = new Vector3D(0.16, 0.15, 0.14);
                Vector3D c = z >= 0 ? hor + (zen - hor) * Math.Pow(z, 0.6) : hor + (chao - hor) * Math.Min(1, Math.Pow(-z * 4, 0.7));
                double j = Math.Max(0, Vector3D.DotProduct(d, sol)); c += new Vector3D(1, 0.97, 0.9) * (0.3 * Math.Pow(j, 16));   // janela de luz
                return c;
            }
            for (int f = 0; f < 6; f++)
            {
                var px = new Vector3D[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        double sc = 2 * (x + 0.5) / n - 1, tc = 2 * (y + 0.5) / n - 1;
                        Vector3D d = f switch { 0 => new Vector3D(1, -tc, -sc), 1 => new Vector3D(-1, -tc, sc), 2 => new Vector3D(sc, 1, tc), 3 => new Vector3D(sc, -1, -tc), 4 => new Vector3D(sc, -tc, 1), _ => new Vector3D(-sc, -tc, -1) };
                        px[y * n + x] = Ceu(d);
                    }
                for (int m = 0, k = n; m < mips; m++, k /= 2)
                {
                    foreach (Vector3D c in px)
                    {
                        byte B(double v) => (byte)Math.Round(255 * Math.Max(0, Math.Min(1, v)));
                        w.Write(B(c.Z)); w.Write(B(c.Y)); w.Write(B(c.X)); w.Write((byte)255);
                    }
                    if (k == 1) break;
                    var px2 = new Vector3D[k / 2 * (k / 2)];   // mip seguinte: média 2×2
                    for (int y = 0; y < k / 2; y++) for (int x = 0; x < k / 2; x++)
                        px2[y * (k / 2) + x] = (px[2 * y * k + 2 * x] + px[2 * y * k + 2 * x + 1] + px[(2 * y + 1) * k + 2 * x] + px[(2 * y + 1) * k + 2 * x + 1]) / 4;
                    px = px2;
                }
            }
            ms.Position = 0; return ms;
        }

        // Um aviso só (o Helix pode repetir a exceção a cada quadro); quem recebe chama Descartar.
        static void Registrar(string t) { try { System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "estructto-render.log"), DateTime.Now.ToString("s") + " " + t + "\r\n"); } catch { } }
        void ErroRender(object s, EventArgs e) { Vista.RenderExceptionOccurred -= ErroRender; disponivel = false; Falhou?.Invoke(); }

        // Depois de Falhou: tira o viewport da árvore e libera o dispositivo D3D (EffectsManager).
        public void Descartar()
        {
            Vista.RenderExceptionOccurred -= ErroRender;
            (Vista.Parent as System.Windows.Controls.Panel)?.Children.Remove(Vista);
            var em = Vista.EffectsManager; Vista.EffectsManager = null;
            (em as IDisposable)?.Dispose();
        }

        // A Vista3D usa FOV horizontal (como o WPF); o PerspectiveCamera do Helix SharpDX é vertical (PerspectiveFovRH):
        // vfov = 2·atan(tan(hfov/2)/aspecto), senão as peças DX saem menores que a sobreposição nativa (capturas -perspectiva-no).
        double hfov = 45;
        double Vfov()
        {
            double w = Vista.ActualWidth, h = Vista.ActualHeight;
            return w < 1 || h < 1 ? hfov : 2 * Math.Atan(Math.Tan(hfov * Math.PI / 360) * h / w) * 180 / Math.PI;
        }

        // Segue a câmera da Vista3D: ortográfica (largOuFov = largura visível, m) ou perspectiva (largOuFov = FOV horizontal, graus).
        public void Camera(bool perspectiva, Point3D pos, Vector3D olhar, Vector3D cima, double largOuFov, double perto, double longe)
        {
            if (perspectiva)
            {
                pcam.Position = pos; pcam.LookDirection = olhar; pcam.UpDirection = cima; hfov = largOuFov; pcam.FieldOfView = Vfov();
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
        static Color4 Cor(Color c, float expo = ExpoPecas)
        {
            var k = MalhaDx.Cor(c);
            if (expo == ExpoPecas) { float l = (k.Red + k.Green + k.Blue) / 3, m = 0.28f; if (l < m && l > 0) { float f = m / l; k = new Color4(Math.Min(1, k.Red * f), Math.Min(1, k.Green * f), Math.Min(1, k.Blue * f), 1); } }   // peças nunca somem no fundo (aço muito escuro)
            return new Color4(k.Red * expo, k.Green * expo, k.Blue * expo, 1); }
        static Color CorAco => Aco == Acabamento.Primer ? TemaPlant.CorPrimer3D : Aco == Acabamento.Pintado ? TemaPlant.CorAcoPintado3D : TemaPlant.CorAco3D;
        public static Color CorBase(bool concreto) => concreto ? TemaPlant.CorConcreto3D : CorAco;

        public sealed class ParafPick { public Ligacao3D Lig; public string Campo; public Point3D A; public Vector3D V; public double R; }
        public sealed class Montagem { public readonly List<ParafPick> Parafs = new List<ParafPick>(); public MalhaDx Aco, Conc, Sel, Arestas, Paraf, Solda, Prop, PropDet; public int Propostas, NLig; public double LargSecao = 0.3; }

        public void Montar(Cena3D c)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var g = Construir(c, Coleta);
            Propostas = g.Propostas; nLig = g.NLig; largSecao = g.LargSecao;
            MalhaDx mallAco = g.Aco, mallConc = g.Conc, mallSel = g.Sel, arestas = g.Arestas, mallParaf = g.Paraf, mallSolda = g.Solda, mallProp = g.Prop, mallPropDet = g.PropDet;
            var mallChao = new MalhaDx(); var mallDecal = new MalhaDx(); var gP = new List<Vector3>(); var gC = new List<Color4>(); Chao(c, mallChao, mallDecal, gP, gC);

            faixas.Clear(); pickParaf = g.Parafs;
            Aplicar(mAco, mallAco); Aplicar(mConcreto, mallConc); Aplicar(mSel, mallSel); Aplicar(mParafusos, mallParaf); Aplicar(mSoldas, mallSolda); Aplicar(mChao, mallChao); Aplicar(mDecal, mallDecal);
            if (gP.Count > 0) { lGrade.Geometry = new HxLine { Positions = new Vector3Collection(gP), Colors = new Color4Collection(gC), Indices = new IntCollection(Enumerable.Range(0, gP.Count)) }; lGrade.Visibility = Visibility.Visible; } else { lGrade.Geometry = null; lGrade.Visibility = Visibility.Collapsed; } Aplicar(mProp, mallProp); Aplicar(mPropDet, mallPropDet);
            var mallCargas = new MalhaDx();
            if (c.Cargas != null)
            {
                var ps = c.Cargas.Positions; Color4 cc = MalhaDx.Cor(TemaPlant.CorCarga);
                var ti = c.Cargas.TriangleIndices;
                for (int i = 0; i + 2 < ti.Count; i += 3) mallCargas.Tri(ps[ti[i]], ps[ti[i + 1]], ps[ti[i + 2]], cc);
            }
            Aplicar(mCargas, mallCargas);
            var mallNos = new MalhaDx();
            if (c.NosSel != null)
            {
                var ps = c.NosSel.Positions; var ti = c.NosSel.TriangleIndices; Color4 cc = MalhaDx.Cor(TemaPlant.CorBarraSel);
                for (int i = 0; i + 2 < ti.Count; i += 3) mallNos.Tri(ps[ti[i]], ps[ti[i + 1]], ps[ti[i + 2]], cc);
            }
            Aplicar(mNos, mallNos);
            if (arestas.L.Count > 0 && !(Environment.GetEnvironmentVariable("ESTRUCTTO_3D_SEM") ?? "").Contains("arestas"))
            {
                lArestas.Geometry = new HxLine { Positions = new Vector3Collection(arestas.L), Indices = new IntCollection(Enumerable.Range(0, arestas.L.Count)) };
                lArestas.Visibility = Visibility.Visible;
            }
            else { lArestas.Geometry = null; lArestas.Visibility = Visibility.Collapsed; }

            Vista.SSAOSamplingRadius = (float)Math.Max(0.12, Math.Min(0.6, largSecao * 0.9));
            lodAr = lodDet = null;   // cena nova: o próximo Lod decide sem histerese
            UltimaMontagemMs = sw.Elapsed.TotalMilliseconds;
            Vista.InvalidateRender();
        }

        // Geometria da cena (barras com as pontas cortadas, pedestais, ligações) sem o Direct3D: o Render3D e o exportador
        // DWG/STEP usam a mesma. coleta != null: cada sólido (barra, chapa, cantoneira, parafuso) também à parte.
        public static Montagem Construir(Cena3D c, List<Peca3D> coleta)
        {
            var g = new Montagem();
            lock (Curtas) Curtas.Clear();   // CurtasCausa fica: a 1ª montagem dá a causa (Excluir da guarda persiste na ligação)
            lock (Locais) Locais.Clear();
            Modelo3D mod = c.Mod;
            var nos = new Dictionary<int, No3D>();
            foreach (No3D n in mod.Nos) if (!nos.ContainsKey(n.Id)) nos[n.Id] = n;
            var barras = mod.Barras.ToDictionary(b => b.Id, b => b);
            Func<Barra3D, Contorno> forma = b => b.Espelho ? GeometriaPerfis.Espelhar(c.Forma(b.Secao)) : c.Forma(b.Secao);
            double FolgaPadrao(Barra3D b, bool emI)
            {
                if (forma(b).Concreto) return 0;
                if (!nos.TryGetValue(b.Ni, out No3D a) || !nos.TryGetValue(b.Nj, out No3D d)) return 0.012;
                double dz = Math.Abs(d.Z - a.Z), len = (P(d) - P(a)).Length;
                return dz > 0.2 * len && dz < 0.95 * len ? 0.04 : 0.012;   // diagonal: espaço do gusset
            }
            var cortes = Render3DGeometria.Juntas(mod, c.Pontos, forma, FolgaPadrao, (b, emI) => double.NaN);

            // ligações: as do modelo (projetadas e propostas), ou os exemplos (capturas / ESTRUCTTO_3D_AMOSTRAS=1).
            // Pendente = proposta sem geometria do motor: a do tipo de nó, feita aqui com as juntas; "*" = modelo sem grupos de
            // ligação (nunca identificado): propostas em todas as juntas (LigacoesAmostra.Gerar).
            var ligs = mod.Ligacoes.Where(l => l.Pendente == null).ToList();
            Vector3D ParaLocal(Barra3D b, int no, Vector3D v)
            {
                var (pa, pd) = c.Pontos(b); Vector3D e = pd - pa; e.Normalize();
                Vector3D x = b.Ni == no ? e : -e, z = Modelo3D.EixoY(e, b.Rot + b.RotPerfil), y = Vector3D.CrossProduct(x, z);
                return new Vector3D(Vector3D.DotProduct(v, x), Vector3D.DotProduct(v, y), Vector3D.DotProduct(v, z));
            }
            foreach (Ligacao3D p in mod.Ligacoes.Where(l => l.Pendente != null && l.Pendente != "*").Take(MaxPropostas))
                foreach (Ligacao3D q in LigacoesAmostra.Proposta(p, mod, cortes, forma, FolgaPadrao, ParaLocal))
                {
                    q.Proposta = p.Tracao == null; q.Nome = p.Nome; q.Pendente = p.Pendente; q.Grupo = p.Grupo; ligs.Add(q);   // G6: Pendente marca a proposta local (o motor não desenha)
                    if (p.Tracao is var (tt, dd, nn, ss) && q.Chapas.FirstOrDefault(ch => ch.Nome == "gusset") is Ligacao3D.Chapa gq && q.Parafusos.Count > 0)
                    {   // projeto ligacao_tracao: espessura e parafusos do projeto (ponytail: numa linha só ao longo da diagonal)
                        var p0 = q.Parafusos[0]; double sg = Math.Sign(gq.Normal.Y), tg = gq.Espessura;
                        gq.Espessura = tt; q.Parafusos.Clear();
                        for (int k = 0; k < Math.Max(1, nn); k++)
                            q.Parafusos.Add(new Ligacao3D.Parafuso { D = dd, Comprimento = p0.Comprimento - tg + tt, Pega = p0.Pega - tg + tt, Origem = p0.Origem + new Vector3D(k * ss, sg * (tt - tg), 0), Eixo = p0.Eixo });
                        foreach (var sd in q.Soldas.Skip(1)) sd.Pontos = sd.Pontos.Select(v => v + new Vector3D(0, sg * (tt - tg), 0)).ToArray();
                    }
                    if (q.Chapas.Count > 0 && barras.TryGetValue(q.Barra, out Barra3D bq)) lock (Locais) Locais.Add(Exportador.IdBarra(bq));
                }
            bool todas = mod.Ligacoes.Any(l => l.Pendente == "*");
            if (ligs.Count == 0 && (c.Amostras || todas))
                foreach (Ligacao3D q in LigacoesAmostra.Gerar(mod, cortes, forma, FolgaPadrao).Take(c.Amostras ? int.MaxValue : MaxPropostas)) { q.Proposta = !c.Amostras; ligs.Add(q); }
            g.Propostas = ligs.Count(l => l.Proposta);
            var porPonta = new Dictionary<(int, bool), Ligacao3D>();
            foreach (Ligacao3D l in ligs) if (barras.TryGetValue(l.Barra, out Barra3D bl)) porPonta[(l.Barra, l.No == bl.Ni)] = l;
            // referencial local da ligação na ponta (b, emI) sem o ajuste da viga inclinada: origem no eixo do perfil deslocado, x saindo do nó, z = altura
            (Point3D o, Vector3D x, Vector3D y, Vector3D z) Local(Barra3D b, bool emI)
            {
                var (pa, pd) = c.Pontos(b); Vector3D e = pd - pa; e.Normalize();
                Vector3D v = Modelo3D.EixoY(e, b.Rot + b.RotPerfil), u = Vector3D.CrossProduct(v, e);
                Point dc = GeometriaPerfis.Cardinal(forma(b), b.Cardinal);
                return LigacoesAmostra.Referencial((emI ? pa : pd) + u * dc.X + v * dc.Y, emI ? e : -e, v, null);
            }
            bool v5 = ligs.Any(l => l.Ponta != null);   // modelo com pontas do motor: regras novas do fallback; sem elas, v0.4 (antes do A2, que põe Ponta na placa)
            // v0.6 A2 (pontas.ponta_sobre_placa): barra inclinada sobre placa de base = placa horizontal centrada na projeção da
            // barra, chumbadores verticais e a barra cortada no plano de cima da placa (antes a placa ia perpendicular à barra)
            foreach (Ligacao3D l in ligs)
                if (l.Chapas.Any(ch => ch.Nome == "placa de base") && barras.TryGetValue(l.Barra, out Barra3D bp))
                {
                    var (_, x, y, z) = Local(bp, l.No == bp.Ni); Vector3D up = new Vector3D(0, 0, 1);
                    LigacoesAmostra.AssentarPlaca(l, new Vector3D(Vector3D.DotProduct(up, x), Vector3D.DotProduct(up, y), Vector3D.DotProduct(up, z)), forma(bp));
                }
            // placa de base no nó: as outras barras que chegam nele (viga no pé do pilar) param fora da placa (meia diagonal + 12 mm)
            var placaNo = new Dictionary<int, (int Pilar, double R)>();
            foreach (Ligacao3D l in ligs)
                foreach (var ch in l.Chapas.Where(x => x.Nome == "placa de base" && x.Contorno.Length > 0))
                {
                    double xs = ch.Contorno.Max(p => p.X) - ch.Contorno.Min(p => p.X), ys = ch.Contorno.Max(p => p.Y) - ch.Contorno.Min(p => p.Y);
                    placaNo[l.No] = (l.Barra, Math.Sqrt(xs * xs + ys * ys) / 2 / 1000);
                }
            if (ligs.Count > 0)
            {
                Ligacao3D Lig(Barra3D b, bool emI) => porPonta.TryGetValue((b.Id, emI), out Ligacao3D l) ? l : null;
                double Placa(Barra3D b, bool emI) => v5 && Lig(b, emI) == null && placaNo.TryGetValue(emI ? b.Ni : b.Nj, out var pl) && pl.Pilar != b.Id ? pl.R + 0.012 : double.NaN;
                cortes = Render3DGeometria.Juntas(mod, c.Pontos, forma,
                    (b, emI) => { double r = Lig(b, emI)?.RecuoDe("viga") ?? double.NaN; return double.IsNaN(r) ? FolgaPadrao(b, emI) : r; },
                    (b, emI) => { Ligacao3D l = Lig(b, emI); if (l == null) return Placa(b, emI); double x = l.PontaDe("viga"); return double.IsNaN(x) ? l.RecuoDe("pilar") : x; },
                    (b, emI) =>   // pontas do motor (contrato pontas): plano e recortes calculados lá; sem elas, a regra acima
                    {
                        var pm = Lig(b, emI)?.Ponta; if (pm == null || pm.N.Length < 1e-9) return null;
                        var (o, x, y, z) = Local(b, emI);
                        var k = new Corte { P = o + x * pm.P.X + y * pm.P.Y + z * pm.P.Z, N = x * pm.N.X + y * pm.N.Y + z * pm.N.Z };
                        var ang = pm.Recortes.FirstOrDefault(r => r.Tipo == "angulo");
                        if (ang != null) k.FolgaAngulo = double.IsNaN(ang.Folga) ? 0.012 : ang.Folga / 1000;
                        k.Boca = pm.Recortes.Any(r => r.Tipo == "boca_peixe");
                        return k;
                    }, v5);
            }
            // Topo do pilar com ligação de chapa de topo (viga chegando no topo, joelho do pórtico): o pilar sobe até o topo da
            // chapa menos a folga da ligação (Ligacao3D.FolgaTopo, padrão 0) — a chapa termina alinhada ao pilar e os enrijecedores
            // do pilar no nível da mesa de cima ficam dentro dele (antes o pilar parava no nó, no eixo da viga, e metade da chapa e o
            // enrijecedor de cima ficavam soltos acima do pilar). Só pilar vertical que termina no nó e fica abaixo dele.
            foreach (Ligacao3D l in ligs)
            {
                if (!l.Chapas.Any(ch => (ch.Nome ?? "").StartsWith("chapa de topo")) || !barras.TryGetValue(l.Barra, out Barra3D bv)) continue;
                bool ei = l.No == bv.Ni;
                if (!cortes.TryGetValue((bv, ei), out Corte kv) || kv.Contra == null) continue;
                Barra3D pl = kv.Contra; var (qa, qd) = c.Pontos(pl); Vector3D ep = qd - qa;
                if (ep.Length < 1e-9 || Math.Abs(ep.Z) < 0.95 * ep.Length || (pl.Ni != l.No && pl.Nj != l.No)) continue;
                bool topoI = pl.Ni == l.No; Point3D topo = topoI ? qa : qd, baixo = topoI ? qd : qa;
                if (baixo.Z >= topo.Z) continue;
                if (cortes.TryGetValue((pl, topoI), out Corte kp) && (kp.Esquadria || kp.Motor)) continue;
                var (av, dv) = c.Pontos(bv); Vector3D e = dv - av; if (e.Length < 1e-9) continue; e.Normalize();
                Vector3D vz = Modelo3D.EixoY(e, bv.Rot + bv.RotPerfil); Point dcv = GeometriaPerfis.Cardinal(forma(bv), bv.Cardinal);
                var (o, x, y, z) = LigacoesAmostra.Referencial((ei ? av : dv) + Vector3D.CrossProduct(vz, e) * dcv.X + vz * dcv.Y, ei ? e : -e, vz, kv);
                if (l.DeslU != 0) o += Vector3D.CrossProduct(vz, e) * l.DeslU;
                double zmax = double.NegativeInfinity;
                foreach (var ch in l.Chapas)
                {
                    Vector3D cw = Vector3D.CrossProduct(ch.Normal, ch.U);
                    foreach (Point q in ch.Contorno) foreach (double tt in new[] { 0, ch.Espessura })
                    {
                        Vector3D pl3 = ch.Origem + ch.U * q.X + cw * q.Y + ch.Normal * tt;
                        zmax = Math.Max(zmax, (o + (x * pl3.X + y * pl3.Y + z * pl3.Z) / 1000).Z);
                    }
                }
                double sobe = zmax - topo.Z - (double.IsNaN(l.FolgaTopo) ? 0 : l.FolgaTopo / 1000);
                if (sobe <= 1e-4 || sobe > 2) continue;
                if (kp != null && kp.P.Z >= topo.Z + sobe) continue;   // outra ligação do nó já subiu mais
                cortes[(pl, topoI)] = new Corte { P = topo + new Vector3D(0, 0, sobe), N = new Vector3D(0, 0, 1), Contra = kp?.Contra };
            }
            g.NLig = ligs.Count;
            if (coleta != null) { CortesColeta = cortes; LigsColeta = porPonta; }
            string NomeLig(Ligacao3D l) => (l.Excluir ? "excluir · " : "") + "ligação " + (l.Nome ?? "") + (l.Proposta ? " (proposta)" : "") + " · barra " + (barras.TryGetValue(l.Barra, out Barra3D bn) && bn.Origem is RefMotor rn ? rn.Id : l.Barra.ToString())
                                         + " nó " + (nos.TryGetValue(l.No, out No3D nn) && nn.Origem is RefMotor ro ? ro.Id : l.No.ToString());

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
                // G6 r2: chapas das duas pontas que se alcançariam (barra curta, ex.: B1091 de 101 mm) param no meio da barra
                double meio = (d - a).Length / 2 * 1000;
                bool pelosRecuos = (ci?.Recuo ?? 0) + (cj?.Recuo ?? 0) >= (d - a).Length - 1e-6;
                string causa = pelosRecuos ? "recuos" : new[] { true, false }.Any(ei => porPonta.TryGetValue((b.Id, ei), out Ligacao3D lm) && lm.Excluir) ? "motor" : null;
                // G6 r4: limite = o x_max que o motor recebeu; sem ele, meio (outra ponta com ligação) ou comprimento menos o recuo
                // da outra ponta (antes: sempre o meio — recusava ligação que o motor projetou sem x_max, ex.: B1080 de 350 mm)
                double Limite(Ligacao3D l, bool ei) => !double.IsNaN(l.XMax) ? l.XMax : porPonta.ContainsKey((b.Id, !ei)) ? meio
                    : 2 * meio - ((ei ? cj : ci)?.Recuo ?? 0) * 1000;
                foreach (bool ei in new[] { true, false })   // no-op em barra comum; o motor já projeta dentro do x_max (G6 r3)
                    if (porPonta.TryGetValue((b.Id, ei), out Ligacao3D lq) && lq.Pendente == null && !Ligacao3D.AparaEm(lq, Limite(lq, ei)))
                    {
                        lq.Excluir = true;   // cortaria parafuso/borda: pendente → barra curta demais para a ligação (gerenciador)
                        causa ??= "guarda";
                    }
                bool curta = pelosRecuos || new[] { true, false }.Any(ei => porPonta.TryGetValue((b.Id, ei), out Ligacao3D lx) && lx.Excluir);
                if (curta) lock (Curtas) { Curtas.Add(Exportador.IdBarra(b)); CurtasCausa.TryAdd(Exportador.IdBarra(b), (causa ?? "?") + string.Concat(new[] { true, false }.Select(ei => porPonta.TryGetValue((b.Id, ei), out Ligacao3D ld) ? " " + (ei ? "i:" : "j:") + ld.Nome + (ld.Pendente != null ? "(local)" : "") : ""))); }
                if (curta) foreach (bool ei in new[] { true, false }) if (porPonta.TryGetValue((b.Id, ei), out Ligacao3D lc)) lc.Excluir = true;   // G6: a ligação da barra curta também sai
                void Guardar(MalhaDx sm, bool trecho) { if (sm.T.Count > 0) coleta.Add(new Peca3D { Nome = (curta ? "excluir · " : "") + "barra " + Exportador.IdBarra(b) + (trecho ? " (cope)" : ""), Tipo = "barra", M = sm, Barra = b, Trecho = trecho, Excluir = curta }); }
                // cope (recortes "cope" da ligação na ponta): trechos da ponta com o contorno cortado, a peça cheia começa depois
                Corte Cope(bool emI, Corte k)
                {
                    if (!porPonta.TryGetValue((b.Id, emI), out Ligacao3D l)) return k;
                    // G6: rasgo do gusset na ponta do tubo: trecho com o contorno em duas peças em C (a chapa passa pelo rasgo)
                    var rg = l.Ponta?.Recortes.FirstOrDefault(r => r.Tipo == "rasgo" && r.Altura > 0 && r.Comprimento > 0);
                    if (rg != null && f.Forma == "TC" && l.YGusset(emI ? e : -e) is Vector3D yr
                        && Render3DGeometria.Rasgada(f, new System.Windows.Vector(Vector3D.DotProduct(yr, u), Vector3D.DotProduct(yr, v)), rg.Altura / 2000) is Contorno fr)
                    {
                        Corte r0 = k ?? new Corte { P = emI ? a : d, N = e };
                        Corte r1 = new Corte { P = r0.P + (emI ? e : -e) * (rg.Comprimento / 1000), N = r0.N };
                        Corte q1 = emI ? r0 : r1, q2 = emI ? r1 : r0;
                        Render3DGeometria.Extrudar(alvo, fr, a, d, u, v, q1, q2, Cor(c.Cor(b)), false);
                        if (coleta != null) { var sm = new MalhaDx(); Render3DGeometria.Extrudar(sm, fr, a, d, u, v, q1, q2, Cor(c.Cor(b)), false); Guardar(sm, true); }
                        Render3DGeometria.Extrudar(arestas, fr, a, d, u, v, q1, q2, Cor(c.Cor(b)), true, false);
                        return r1;
                    }
                    var cs = (l.Ponta?.Recortes ?? l.Recortes).Where(r => r.Tipo == "cope" && (r.Barra ?? "viga") == "viga" && r.Altura > 0 && r.Comprimento > 0).ToList();
                    if (cs.Count == 0) return k;
                    Vector3D dentro = emI ? e : -e;
                    double lado = Vector3D.DotProduct(Vector3D.CrossProduct(dentro, v), u);   // +1: o "esq" do contrato (+y local = x × z) é +u
                    Corte k0 = k ?? new Corte { P = emI ? a : d, N = e }, ant = k0;
                    Corte Em(double x) => new Corte { P = k0.P + dentro * x, N = k0.N };
                    foreach (double cc in cs.Select(r => r.Comprimento / 1000).Distinct().OrderBy(x => x))
                    {
                        var ativos = cs.Where(r => r.Comprimento / 1000 >= cc - 1e-9).ToList();   // copes que ainda valem neste trecho
                        double vMax = f.H / 2 - ativos.Where(r => r.Posicao != "base").Select(r => r.Altura / 1000).DefaultIfEmpty(0).Max();
                        double vMin = -f.H / 2 + ativos.Where(r => r.Posicao == "base").Select(r => r.Altura / 1000).DefaultIfEmpty(0).Max();
                        double Lat(string p) => ativos.Where(r => r.Posicao == p).Select(r => r.Altura / 1000).DefaultIfEmpty(0).Max();
                        double cEsq = f.B / 2 - Lat("esq"), cDir = f.B / 2 - Lat("dir");   // meia largura que sobra de cada lado
                        double uMax = lado > 0 ? cEsq : cDir, uMin = -(lado > 0 ? cDir : cEsq);
                        Corte k1 = Em(cc);
                        Contorno fc = Render3DGeometria.Recortar(f, vMin, vMax, uMin < -f.B / 2 + 1e-9 ? double.NegativeInfinity : uMin, uMax > f.B / 2 - 1e-9 ? double.PositiveInfinity : uMax);
                        if (fc != null)
                        {
                            Corte p1 = emI ? ant : k1, p2 = emI ? k1 : ant;
                            Render3DGeometria.Extrudar(alvo, fc, a, d, u, v, p1, p2, Cor(c.Cor(b)), false);
                            if (coleta != null) { var sm = new MalhaDx(); Render3DGeometria.Extrudar(sm, fc, a, d, u, v, p1, p2, Cor(c.Cor(b)), false); Guardar(sm, true); }
                            Render3DGeometria.Extrudar(arestas, fc, a, d, u, v, p1, p2, Cor(c.Cor(b)), true, false);
                        }
                        ant = k1;
                    }
                    return ant;
                }
                ci = Cope(true, ci); cj = Cope(false, cj);
                Render3DGeometria.Extrudar(alvo, f, a, d, u, v, ci, cj, Cor(c.Cor(b)), false);
                if (coleta != null) { var sm = new MalhaDx(); Render3DGeometria.Extrudar(sm, f, a, d, u, v, ci, cj, Cor(c.Cor(b)), false); Guardar(sm, false); }
                Render3DGeometria.Extrudar(arestas, f, a, d, u, v, ci, cj, Cor(c.Cor(b)), true, false);   // só as arestas
            }
            g.LargSecao = nLarg > 0 ? somaLarg / nLarg : 0.3;

            // pedestais dos apoios (concreto)
            Color4 corConc = Cor(TemaPlant.CorConcreto3D);
            mallConc.Marcar(null);   // picking: o que vem depois não é barra
            foreach (No3D n in mod.Nos)
            {
                double s = c.Apoio(n); if (s <= 0) continue;
                mallConc.Caixa(new Point3D(n.X, n.Y, n.Z - s * 0.7), 2 * s, 2 * s, 1.4 * s, corConc, true);
            }

            // ligações (contrato → mundo)
            var mallParaf = new MalhaDx(); var mallSolda = new MalhaDx(); var mallProp = new MalhaDx(); var mallPropDet = new MalhaDx();
            Color4 corChapaP = Cor(Aco == Acabamento.Galvanizado ? TemaPlant.CorChapa3D : CorAco), corParafP = Cor(TemaPlant.CorParafuso3D), corSoldaP = Cor(TemaPlant.CorSolda3D), corProp = Cor(TemaPlant.CorProposta3D);
            // Gusset: o contrato supõe o apoio com a meia largura do perfil numa direção; no mundo o apoio pode estar girado. Se um
            // canto da chapa entra na peça de apoio, a chapa anda no plano dela, para longe do eixo do apoio, até encostar na face.
            // ponytail: só a chapa anda (parafusos e solda ficam); some quando o contrato trouxer a orientação do apoio.
            Vector3D FolgaGusset(Point3D org, Vector3D uu, Vector3D ww, Vector3D nn, Point[] ct, double esp, Barra3D sup, Vector3D xd)
            {
                var (sa, sd) = c.Pontos(sup); Vector3D es = sd - sa; if (es.Length < 1e-9) return new Vector3D(); es.Normalize();
                Vector3D vs = Modelo3D.EixoY(es, sup.Rot + sup.RotPerfil), us = Vector3D.CrossProduct(vs, es);
                Contorno fs = forma(sup); Point dcs = GeometriaPerfis.Cardinal(fs, sup.Cardinal);
                Point3D os = sa + us * dcs.X + vs * dcs.Y;
                Vector3D nz = xd - Vector3D.DotProduct(xd, es) * es; nz -= Vector3D.DotProduct(nz, nn) * nn;   // no plano da chapa, para longe do apoio
                if (nz.Length < 1e-6) return new Vector3D(); nz.Normalize();
                double ext = fs.Lacos.SelectMany(z => z).Max(p => Vector3D.DotProduct(us * p.X + vs * p.Y, nz));
                double dmin = double.MaxValue;
                foreach (Point p in ct) foreach (double t in new[] { 0, esp }) dmin = Math.Min(dmin, Vector3D.DotProduct(org + uu * p.X + ww * p.Y + nn * t - os, nz));
                return dmin < ext && ext - dmin < 0.1 ? nz * (ext - dmin) : new Vector3D();   // mais de 10 cm: geometria estranha, fica como veio
            }
            mallAco.Marcar(null);   // chapas e cantoneiras não são escolhíveis (sem isso o picking dava a última barra)
            foreach (Ligacao3D l in ligs)
            {
                // proposta: chapas, parafusos e soldas numa cor fria e fosca, em malhas próprias
                MalhaDx mallChapas = l.Proposta ? mallProp : mallAco, mallP = l.Proposta ? mallPropDet : mallParaf, mallS = l.Proposta ? mallPropDet : mallSolda;
                Color4 corChapa = l.Proposta ? corProp : corChapaP, corParaf = l.Proposta ? corProp : corParafP, corSolda = l.Proposta ? corProp : corSoldaP;
                if (!barras.TryGetValue(l.Barra, out Barra3D b)) continue;
                var (a, d) = c.Pontos(b); Vector3D e = d - a; if (e.Length < 1e-9) continue; e.Normalize();
                bool emI = l.No == b.Ni;
                Point dc = GeometriaPerfis.Cardinal(forma(b), b.Cardinal);
                cortes.TryGetValue((b, emI), out Corte kl);
                var (o, x, y, z) = LigacoesAmostra.Referencial((emI ? a : d) + Vector3D.CrossProduct(Modelo3D.EixoY(e, b.Rot + b.RotPerfil), e) * dc.X + Modelo3D.EixoY(e, b.Rot + b.RotPerfil) * dc.Y,
                                                               emI ? e : -e, Modelo3D.EixoY(e, b.Rot + b.RotPerfil), kl);   // contrato: y = x × z (canhoto); origem no eixo do perfil deslocado
                if (l.DeslU != 0) o += Vector3D.CrossProduct(Modelo3D.EixoY(e, b.Rot + b.RotPerfil), e) * l.DeslU;   // G6: origem do contrato no centroide do U
                var gus = l.Chapas.FirstOrDefault(ch => ch.Nome == "gusset");
                // C4: perfil U lapeado no gusset tem de encostar a ALMA (o contrato aperta a aba/alma de espessura t sob a cabeça). Se a alma está do
                // lado de y' que fica longe da chapa (abertura do U virada para ela), espelha y' (a peça passa ao outro lado do gusset).
                Vector3D EscolheY(Vector3D yy, Vector3D zz)
                {
                    var f = forma(b); if (gus == null || f.Forma != "U" || f.Lacos.Count == 0) return yy;
                    Vector3D vb = Modelo3D.EixoY(e, b.Rot + b.RotPerfil), ub = Vector3D.CrossProduct(vb, e);
                    bool Teia(Vector3D ay)
                    {
                        double ext = f.Lacos[0].Min(q => Vector3D.DotProduct(ub * q.X + vb * q.Y, ay)) + 0.5 * Math.Max(f.Tw, 0.002);
                        Vector3D pt = ay * ext;   // ponto no plano da seção: profundidade `ext` ao longo de ay, centro em z'
                        return Dentro2D(f.Lacos[0], new Point(Vector3D.DotProduct(pt, ub), Vector3D.DotProduct(pt, vb)));
                    }
                    return !Teia(yy) && Teia(-yy) ? -yy : yy;
                }
                if (gus != null && kl?.Contra != null && l.ApoioDir == null)   // com apoio_dir (G5) o referencial do motor vale como veio
                {
                    // gusset do motor ou da proposta (sem Ponta: antes ficava no referencial da diagonal, girado e no eixo do pilar): o contrato põe o apoio no plano xz local (ângulo θ) e a peça encostada na chapa pelo lado +y.
                    // No mundo: z' = direção do apoio sem a componente em x (apoio "para a frente"), y' = x × z', e a origem anda em y'
                    // até a face da diagonal do lado −y' encostar na face +y da chapa.
                    var (sa, sd) = c.Pontos(kl.Contra); Vector3D ds = sd - sa;
                    if (Vector3D.DotProduct(ds, x) < 0) ds = -ds;
                    Vector3D zp = ds - Vector3D.DotProduct(ds, x) * x;
                    if (zp.Length > 1e-6)
                    {
                        zp.Normalize(); Vector3D yp = EscolheY(Vector3D.CrossProduct(x, zp), zp);
                        Vector3D vb = Modelo3D.EixoY(e, b.Rot + b.RotPerfil), ub = Vector3D.CrossProduct(vb, e);
                        double ymin = forma(b).Lacos.SelectMany(z => z).Min(q => Vector3D.DotProduct(ub * q.X + vb * q.Y, yp));
                        (o, y, z) = (o + yp * (ymin - gus.Espessura / 2000), yp, zp);
                    }
                }
                else if (gus != null && l.YGusset(x) is Vector3D yg)
                {   // G6: apoio_dir local = eixo do apoio no mundo (Ligacao3D.YGusset; contrato canhoto: z = y × x)
                    (y, z) = (EscolheY(yg, Vector3D.CrossProduct(yg, x)), Vector3D.CrossProduct(yg, x));
                    if (forma(b).Forma != "TC" && forma(b).Forma != "CIRC")   // perfil aberto: o contrato põe a peça do lado +y da chapa (sem rasgo); encosta na face −y
                    {
                        Vector3D vb = Modelo3D.EixoY(e, b.Rot + b.RotPerfil), ub = Vector3D.CrossProduct(vb, e);
                        o += y * (forma(b).Lacos.SelectMany(z => z).Min(q => Vector3D.DotProduct(ub * q.X + vb * q.Y, y)) - gus.Espessura / 2000);
                    }
                }
                else if (l.Ponta != null && gus != null && l.ApoioDir == null)   // sem apoio conhecido: só encosta a chapa na face −y da peça
                {   // (tubo: chapa a R + esp/2 do eixo, sobre a superfície, não rasgando o tubo)
                    y = EscolheY(y, z);
                    Vector3D vb = Modelo3D.EixoY(e, b.Rot + b.RotPerfil), ub = Vector3D.CrossProduct(vb, e);
                    double ymin = forma(b).Lacos.SelectMany(z => z).Min(q => Vector3D.DotProduct(ub * q.X + vb * q.Y, y));
                    o += y * (ymin - gus.Espessura / 2000);
                }
                if (gus != null && l.Ponta == null && l.ApoioDir == null && kl?.Contra != null && gus.Contorno.Length == 4 && gus.Contorno[0].X == gus.Contorno[3].X)
                {   // gusset proposto (LigacoesAmostra.Gusset): a borda soldada (pontos 0 e 3, u = xb) era ⟂ à diagonal e tocava o pilar por um
                    // canto; inclina para ficar ∥ ao eixo do apoio (x local = xb − w·sx/sz, z local = −w). Pontos 0 e 3 iguais = ainda não feito.
                    var (sa, sd) = c.Pontos(kl.Contra); Vector3D ds = sd - sa; ds.Normalize();
                    double sx = Vector3D.DotProduct(ds, x), sz = Vector3D.DotProduct(ds, z), sg = Math.Sign(gus.U.X);
                    if (Math.Abs(sz) > 0.2)
                    {
                        foreach (int k in new[] { 0, 3 }) { Point q = gus.Contorno[k]; gus.Contorno[k] = new Point(sg * (sg * q.X - q.Y * sx / sz), q.Y); }
                        foreach (var s in l.Soldas.Where(s => s.Pontos?.Length == 2 && s.Pontos[0].X == s.Pontos[1].X))   // filetes da borda, no mesmo plano
                            s.Pontos = s.Pontos.Select(p => new Vector3D(p.X + p.Z * sx / sz, p.Y, p.Z)).ToArray();
                    }
                }
                Point3D W(Vector3D p) => o + (x * p.X + y * p.Y + z * p.Z) / 1000;
                Vector3D Wv(Vector3D p) { Vector3D r = x * p.X + y * p.Y + z * p.Z; if (r.Length > 1e-12) r.Normalize(); return r; }
                foreach (var ch in l.Chapas)
                {
                    Vector3D nn = Wv(ch.Normal), uu = Wv(ch.U); if (nn.Length < 0.5 || uu.Length < 0.5) continue;
                    Vector3D ww = Wv(Vector3D.CrossProduct(ch.Normal, ch.U));   // no local: o referencial do contrato (y = x × z) é canhoto, o produto no mundo inverteria
                    Point[] ct = ch.Contorno.Select(p => new Point(p.X / 1000, p.Y / 1000)).ToArray();
                    Point3D org = W(ch.Origem);
                    if (ch.Nome == "gusset" && l.ApoioDir == null) for (int it = 0; it < 3; it++) foreach (Barra3D sup in mod.Barras.Where(q => q != b && (q.Ni == l.No || q.Nj == l.No))) org += FolgaGusset(org, uu, ww, nn, ct, ch.Espessura / 1000, sup, x);   // cada peça do nó (3 voltas: um afastamento pode empurrar para outra)
                    if (l.Proposta) mallChapas.Marcar(new PecaLig { Lig = l }); else if (!string.IsNullOrEmpty(ch.Campo)) mallChapas.Marcar(new PecaLig { Lig = l, Tag = ch.Campo }); else mallChapas.Marcar(null);   // proposta: clique abre o gerenciador no grupo (Aplicar troca / escolher padrão)   // clique na chapa: vai ao campo (sem isso o picking dava a última barra)
                    mallChapas.Chapa(org, uu, ww, nn, ct, ch.Espessura / 1000, corChapa, false);
                    if (coleta != null) { var sc = new MalhaDx(); sc.Chapa(org, uu, ww, nn, ct, ch.Espessura / 1000, corChapa, false); coleta.Add(new Peca3D { Nome = NomeLig(l) + " " + ch.Nome, Tipo = "chapa", M = sc, Lig = l, Excluir = l.Excluir }); }
                    arestas.Chapa(org, uu, ww, nn, ct, ch.Espessura / 1000, corChapa, true, false);
                }
                foreach (var ca in l.Cantoneiras)
                {
                    Perfil3D p = PerfilL(ca.Perfil); if (p == null) continue;
                    Contorno f = GeometriaPerfis.CriarContorno(p, 0.05);
                    Vector3D ex = Wv(ca.Eixo), uu = Wv(ca.U); if (ex.Length < 0.5 || uu.Length < 0.5) continue;
                    Vector3D vv = Vector3D.CrossProduct(ex, uu);   // ponytail: triedro direito no mundo (a aba pode sair espelhada no referencial canhoto)
                    Point3D pa = W(ca.Origem), pb = pa + ex * (ca.Comprimento / 1000);
                    if (l.Proposta) mallChapas.Marcar(new PecaLig { Lig = l }); else if (!string.IsNullOrEmpty(ca.Campo)) mallChapas.Marcar(new PecaLig { Lig = l, Tag = ca.Campo }); else mallChapas.Marcar(null);
                    Render3DGeometria.Extrudar(mallChapas, f, pa, pb, uu, vv, null, null, corChapa, false);
                    if (coleta != null) { var sc = new MalhaDx(); Render3DGeometria.Extrudar(sc, f, pa, pb, uu, vv, null, null, corChapa, false); coleta.Add(new Peca3D { Nome = NomeLig(l) + " cantoneira", Tipo = "cantoneira", M = sc, Lig = l, Excluir = l.Excluir }); }
                    Render3DGeometria.Extrudar(arestas, f, pa, pb, uu, vv, null, null, corChapa, true, false);
                }
                foreach (var pf in l.Parafusos)
                {
                    Vector3D ep = Wv(pf.Eixo); Vector3D? refU = l.ArestaMaisPerto(pf) is Vector3D ar ? Wv(ar) : (Vector3D?)null;   // hexágono alinhado à aresta mais próxima da chapa que o parafuso atravessa
                    Render3DGeometria.Parafuso(mallP, W(pf.Origem), ep, pf.D / 1000, pf.Comprimento / 1000, corParaf, pf.Pega / 1000, refU);
                    if (!l.Proposta && !string.IsNullOrEmpty(pf.Campo)) g.Parafs.Add(new ParafPick { Lig = l, Campo = pf.Campo, A = W(pf.Origem), V = ep * (pf.Comprimento / 1000), R = Math.Max(0.9 * pf.D / 1000, 0.006) });   // clique no parafuso: raio x segmento (sem octree)
                    if (coleta != null) { var sp = new MalhaDx(); Render3DGeometria.Parafuso(sp, W(pf.Origem), ep, pf.D / 1000, pf.Comprimento / 1000, corParaf, pf.Pega / 1000, refU); coleta.Add(new Peca3D { Nome = "parafuso · " + NomeLig(l), Tipo = "parafuso", M = sp, Lig = l, Excluir = true, Apertadas = pf.Pilha.Select(x => W(pf.Origem + pf.Eixo * ((x[0] + x[1]) / 2 / Math.Max(1e-9, pf.Eixo.Length)))).ToArray() }); }
                }
                foreach (var sd in l.Soldas)
                {
                    if (sd.Pontos == null || sd.Pontos.Length < 2) continue;
                    var (axs, lados, face) = Ligacao3D.Apoio(l, sd);   // onde a solda assenta: face de chapa (recorta), borda de chapa (enrijecedor) ou nenhuma
                    Render3DGeometria.Cordao(mallS, sd.Pontos.Select(W).ToList(), sd.Perna / 1000, corSolda, axs == null ? x : Wv(axs.Value),
                                             lados?.Select(q => (Wv(q.n), q.desl / 1000)).ToList(),
                                             face == null ? null : (pa, pb) => Render3DGeometria.Intervalos(face.Q(ParaLig(pa)), face.Q(ParaLig(pb)), face.Contorno));
                }
                Vector3D ParaLig(Point3D pw) { Vector3D r = (pw - o) * 1000; return new Vector3D(Vector3D.DotProduct(r, x), Vector3D.DotProduct(r, y), Vector3D.DotProduct(r, z)); }   // x, y, z ortonormais
            }

            g.Aco = mallAco; g.Conc = mallConc; g.Sel = mallSel; g.Arestas = arestas; g.Paraf = mallParaf; g.Solda = mallSolda; g.Prop = mallProp; g.PropDet = mallPropDet;
            return g;
        }

        public double UltimaMontagemMs { get; private set; }

        void Aplicar(MeshGeometryModel3D m, MalhaDx malha)
        {
            if (malha.Vazia) { m.Geometry = null; m.Visibility = Visibility.Collapsed; return; }   // null: o Lod não reacende a malha velha
            var hx = new HxMesh { Positions = new Vector3Collection(malha.P), Normals = new Vector3Collection(malha.N), Colors = new Color4Collection(malha.C), Indices = new IntCollection(malha.T) };
            if (malha.Obj.Count > 0) hx.UpdateOctree();   // só as malhas das peças (picking rápido; a das ligações é grande e montaria devagar)
            m.Geometry = hx;
            m.Visibility = Visibility.Visible;
            if (malha.Obj.Count > 0) faixas.Add((m, malha.Ini.ToArray(), malha.Obj.ToArray()));
        }

        static Point3D P(No3D n) => new Point3D(n.X, n.Y, n.Z);
        static double Suave(double t) { t = Math.Max(0, Math.Min(1, t)); return t * t * (3 - 2 * t); }

        static bool Dentro2D(Point[] poly, Point p)
        {
            bool d = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                if ((poly[i].Y > p.Y) != (poly[j].Y > p.Y) && p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X) d = !d;
            return d;
        }
        internal static Perfil3D PerfilL(string nome)   // "L76x6.3" (mm) → cantoneira de abas iguais; ponytail: só esse formato
        {
            if (string.IsNullOrEmpty(nome) || nome[0] != 'L') return null;
            var p = nome.Substring(1).Replace(',', '.').Split('x', 'X');
            if (p.Length < 2 || !double.TryParse(p[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double b)
                || !double.TryParse(p[p.Length - 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double t)) return null;
            return new Perfil3D { Forma = "L", H = b / 1000, B = b / 1000, Tw = t / 1000, Tf = t / 1000 };
        }

        // Chão independente de iluminação (E1 fix): material só emissivo (nenhuma luz, SSAO ou sombra entra na cor) e o desvanecimento é
        // alfa radial até 0 no aro: o que sobra é o próprio canvas. Mesmo que a luz/SSAO falhe no dispositivo, nunca aparece um disco escuro.
        // Sombra de contato = decal preto com alfa (sob o modelo e sob cada apoio), malha à parte; grade = linhas (sem luz) com o mesmo alfa.
        void Chao(Cena3D c, MalhaDx m, MalhaDx decal, List<Vector3> gradeP, List<Color4> gradeC)
        {
            Rect3D cx = c.Caixa; double raio = c.Raio, z = c.Z0;
            double xm = cx.X + cx.SizeX / 2, ym = cx.Y + cx.SizeY / 2, R = Math.Min(400, Math.Max(5, Math.Max(cx.SizeX, cx.SizeY) / 2 + raio * 0.9));
            const float aChao = 0.95f;
            Color4 Branco(double r) { double t = 1 - Suave((r / R - 0.25) / 0.75); return new Color4(0, 0, 0, (float)(aChao * t)); }   // a cor vem do EmissiveColor do material
            const int seg = 72; double[] aneis = { 0.25, 0.45, 0.65, 0.8, 0.9, 1.0 };
            var ant = Enumerable.Repeat(new Point3D(xm, ym, z), seg + 1).ToArray(); double rAnt = 0;
            foreach (double fr in aneis)
            {
                double r = fr * R; var cur = new Point3D[seg + 1];
                for (int k = 0; k <= seg; k++) { double t = 2 * Math.PI * k / seg; cur[k] = new Point3D(xm + r * Math.Cos(t), ym + r * Math.Sin(t), z); }
                for (int k = 0; k < seg; k++)
                {
                    if (rAnt == 0) Tri(m, cur[k + 1], ant[k], cur[k], Branco(r), Branco(0), Branco(r));
                    else Quad(m, ant[k], cur[k], cur[k + 1], ant[k + 1], Branco(rAnt), Branco(r), Branco(r), Branco(rAnt));
                }
                ant = cur; rAnt = r;
            }
            // decal de sombra: preto com alfa, 3 anéis; um sob a projeção do modelo em planta e um por apoio
            double zd = z + raio * 0.0004;
            void Mancha(double mx, double my, double rx, double ry, float a0)
            {
                var a = Enumerable.Repeat(new Point3D(mx, my, zd), 25).ToArray(); double fa = 0;
                foreach (double f in new[] { 0.4, 0.7, 1.0 })
                {
                    var b = new Point3D[25];
                    for (int k = 0; k <= 24; k++) { double t = 2 * Math.PI * k / 24; b[k] = new Point3D(mx + rx * f * Math.Cos(t), my + ry * f * Math.Sin(t), zd); }
                    Color4 Cd(double ff) => new Color4(0, 0, 0, (float)(a0 * (1 - Suave(ff))));
                    for (int k = 0; k < 24; k++)
                    {
                        if (fa == 0) Tri(decal, b[k + 1], a[k], b[k], Cd(f), Cd(0), Cd(f));
                        else Quad(decal, a[k], b[k], b[k + 1], a[k + 1], Cd(fa), Cd(f), Cd(f), Cd(fa));
                    }
                    a = b; fa = f;
                }
            }
            Mancha(xm, ym, cx.SizeX / 2 + raio * 0.12 + 0.3, cx.SizeY / 2 + raio * 0.12 + 0.3, 0.22f);
            if (c.Mod != null && c.Apoio != null)
                foreach (No3D n in c.Mod.Nos) { double s = c.Apoio(n); if (s > 0) Mancha(n.X, n.Y, s * 2.6, s * 2.6, 0.45f); }
            // grade: linhas, passo igual ao do analítico, alfa some com o raio
            double passo = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(1e-6, raio / 2)))); if (raio / passo > 12) passo *= 2.5; if (raio / passo > 12) passo *= 2;
            double g0x = Math.Floor((xm - R) / passo) * passo, g1x = Math.Ceiling((xm + R) / passo) * passo, g0y = Math.Floor((ym - R) / passo) * passo, g1y = Math.Ceiling((ym + R) / passo) * passo;
            double zg = z + raio * 0.0008; Color cg = TemaPlant.CorGrade3D;
            Color4 CorG(double x, double y) { double r = Math.Sqrt((x - xm) * (x - xm) + (y - ym) * (y - ym)); return new Color4(cg.R / 255f, cg.G / 255f, cg.B / 255f, (float)(1 - Suave((r / R - 0.1) / 0.7))); }
            void Seg(double x0, double y0, double x1, double y1)
            {
                if ((x0 - xm) * (x0 - xm) + (y0 - ym) * (y0 - ym) > R * R * 0.8 && (x1 - xm) * (x1 - xm) + (y1 - ym) * (y1 - ym) > R * R * 0.8) return;
                gradeP.Add(new Vector3((float)x0, (float)y0, (float)zg)); gradeP.Add(new Vector3((float)x1, (float)y1, (float)zg)); gradeC.Add(CorG(x0, y0)); gradeC.Add(CorG(x1, y1));
            }
            for (double x = g0x; x <= g1x + 1e-9; x += passo) for (double y = g0y; y < g1y - 1e-9; y += passo / 2) Seg(x, y, x, y + passo / 2);
            for (double y = g0y; y <= g1y + 1e-9; y += passo) for (double x = g0x; x < g1x - 1e-9; x += passo / 2) Seg(x, y, x + passo / 2, y);
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
        // com poucas ligações (≤ 80). Histerese (acendem em 6/40 px, apagam abaixo de 4/28 px): o zoom e a órbita em perspectiva
        // mudam um pouco a escala e não podem ficar acendendo e apagando os parafusos (a Vista3D nem chama durante a órbita).
        bool? lodAr, lodDet;
        public void Lod(double pxPorM)
        {
            double px = pxPorM * largSecao;
            bool ar = lodAr == true ? px >= 4 : px >= 6, det = nLig <= 80 || (lodDet == true ? px >= 28 : px >= 40);
            lodAr = ar; lodDet = det;
            Visibility va = ar && lArestas.Geometry != null ? Visibility.Visible : Visibility.Collapsed;
            if (lArestas.Visibility != va) lArestas.Visibility = va;
            foreach (var m in new[] { mParafusos, mSoldas, mPropDet })
            {
                Visibility vd = det && m.Geometry != null ? Visibility.Visible : Visibility.Collapsed;
                if (m.Visibility != vd) m.Visibility = vd;
            }
        }

        // Barra sob o pixel (coordenadas da Vista): a mais próxima da câmera; chapas, parafusos e chão não contam.
        // `ponto`: o 1º toque fora do chão (barra, chapa ou parafuso), para o pivô da órbita e o zoom no cursor.
        List<ParafPick> pickParaf = new List<ParafPick>();
        public int NParafusosClicaveis => pickParaf.Count;
        public List<Point3D> CentrosParafusos() => pickParaf.Select(b => b.A + b.V / 2).ToList();   // autotestes

        // Parafuso mais perto do olho cruzado pelo raio (distância raio–eixo ≤ raio da cabeça): O(n) simples, ~0,2 µs por parafuso.
        // `antes`: só vale se o ponto de contato fica antes de `limite` (distância do olho ao 1º sólido) + meio parafuso.
        public PecaLig AcertarParafuso(Point3D o, Vector3D d, double limite) => AcertarParafuso(pickParaf, o, d, limite);

        public static PecaLig AcertarParafuso(List<ParafPick> lista, Point3D o, Vector3D d, double limite)
        {
            PecaLig melhor = null; double tm = double.MaxValue;
            foreach (var b in lista)
            {
                Vector3D w0 = b.A - o, v = b.V; double a = Vector3D.DotProduct(v, v); if (a < 1e-12) continue;
                double bb = Vector3D.DotProduct(v, d), c = Vector3D.DotProduct(d, d), dd = Vector3D.DotProduct(v, w0), ee = Vector3D.DotProduct(d, w0);
                double den = a * c - bb * bb; if (den < 1e-12) continue;
                double s = Math.Max(0, Math.Min(1, (bb * ee - c * dd) / den));   // ponto do eixo (0..1)
                double t = (bb * s + ee) / c; if (t < 0) continue;
                Vector3D q = (b.A + v * s) - (o + d * t);
                if (q.Length > b.R || t > limite + v.Length * 0.5 + 0.02 || t >= tm) continue;
                tm = t; melhor = new PecaLig { Lig = b.Lig, Tag = b.Campo, T = t };
            }
            return melhor;
        }

        // medição (capturas): ms por clique do teste de parafusos com n parafusos espalhados (≈ 30 m de obra)
        public static double MedirParafusos(int n, int cliques = 500)
        {
            var rnd = new Random(7); var lista = new List<ParafPick>();
            for (int i = 0; i < n; i++) lista.Add(new ParafPick { Lig = null, Campo = "d", A = new Point3D(rnd.NextDouble() * 30, rnd.NextDouble() * 30, rnd.NextDouble() * 30), V = new Vector3D(0, 0.06, 0), R = 0.014 });
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int k = 0; k < cliques; k++)
                AcertarParafuso(lista, new Point3D(rnd.NextDouble() * 30, -20, rnd.NextDouble() * 30), new Vector3D(0, 1, 0), 40);
            return sw.Elapsed.TotalMilliseconds / cliques;
        }

        public object Acertar(Point p) => Acertar(p, out _);

        public object Acertar(Point p, out Point3D? ponto)
        {
            ponto = null;
            var hits = Vista.FindHits(p);
            if (hits == null) return null;
            foreach (var h in hits.OrderBy(h => h.Distance))
            {
                if (h.ModelHit == mChao || h.ModelHit == mChao.SceneNode) continue;
                ponto ??= new Point3D(h.PointHit.X, h.PointHit.Y, h.PointHit.Z);
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
