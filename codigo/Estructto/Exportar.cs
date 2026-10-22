// Exportação do modelo 3D para DWG/DXF (ACadSharp) e STEP AP214 (texto ISO 10303-21 escrito aqui). docs/v3/EXPORTAR.md.
// Entrada: Modelo3D (documento espacial: DocumentoEspacial.ParaVista; 2D: Modelo3D.De) + forma real de cada seção
// (Perfil3D, a mesma do modo Realista). Coordenadas do modelo em m; arquivos em mm.
// Testes: Estructto.exe --testes-exportar <pasta> (TestesExportar, sem motor).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Media3D;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using CSMath;
using Microsoft.Win32;
using Portico;
using F = Fluent;

namespace NovoFtool
{
    // Uma barra pronta para exportar: nome (id da barra), seção, eixo (mm) e sólido extrudado (mm).
    public sealed class Peca { public string Id, Secao; public Point3D A, B; public Solido Solido; public bool Barra = true, Trecho; }   // Trecho: pedaço do cope de uma barra (sólido fechado à parte, sem linha analítica)   // Barra = false: chapa, cantoneira ou parafuso de ligação (sem linha analítica)

    public static class Exportador
    {
        const double Mm = 1000;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        public const string CamadaAnalitica = "ANALITICO";

        public static string IdBarra(Barra3D b) => b.Origem is RefMotor r ? r.Id : b.Origem is Barra b2 ? "B" + b2.Id : "B" + b.Id;

        // As peças são as mesmas do Realista (Render3D.Construir): barras com todos os cortes das pontas (plano, 2º plano
        // "angulo", cope, boca de peixe; pontas do motor quando a vista tem as ligações), chapas, cantoneiras e parafusos das
        // ligações. Cada sólido vira faces triangulares (mm). ponytail: seção sem dados = quadrado de 100 mm.
        public static List<Peca> Pecas(Modelo3D m, Func<string, Perfil3D> perfil)
        {
            var nos = new Dictionary<int, No3D>(); foreach (No3D n in m.Nos) nos[n.Id] = n;
            var formas = new Dictionary<string, GeometriaPerfis.Contorno>();
            GeometriaPerfis.Contorno Forma(string sec)
            {
                if (formas.TryGetValue(sec ?? "", out var f)) return f;
                Perfil3D p = null; try { p = perfil?.Invoke(sec ?? ""); } catch { }
                return formas[sec ?? ""] = GeometriaPerfis.CriarContorno(p, 0.1);
            }
            Point3D Pn(int id) => new Point3D(nos[id].X, nos[id].Y, nos[id].Z);
            var cena = new Cena3D
            {
                Mod = m, Forma = Forma, Cor = _ => System.Windows.Media.Colors.Gray, Selecionada = _ => false, Apoio = _ => 0,
                Pontos = b => nos.ContainsKey(b.Ni) && nos.ContainsKey(b.Nj) ? (Pn(b.Ni), Pn(b.Nj)) : (new Point3D(), new Point3D()),
            };
            var col = new List<Render3D.Peca3D>();
            Render3D.Construir(cena, col);
            var r = new List<Peca>();
            foreach (var pc in col)
            {
                MalhaDx malha = pc.M; var sol = new Solido();
                for (int t = 0; t + 2 < malha.T.Count; t += 3)
                    sol.Lados.Add(new[] { 0, 1, 2 }.Select(k => { var v = malha.P[malha.T[t + k]]; return new Point3D(Math.Round(v.X * Mm, 3), Math.Round(v.Y * Mm, 3), Math.Round(v.Z * Mm, 3)); }).ToArray());
                if (pc.Barra is Barra3D b)   // a barra vem com o sólido (Peca3D.Barra): ids repetidos não se confundem
                {
                    sol.Concreto = Forma(b.Secao).Concreto;
                    r.Add(new Peca { Id = IdBarra(b), Secao = b.Secao ?? "", A = new Point3D(nos[b.Ni].X * Mm, nos[b.Ni].Y * Mm, nos[b.Ni].Z * Mm), B = new Point3D(nos[b.Nj].X * Mm, nos[b.Nj].Y * Mm, nos[b.Nj].Z * Mm), Solido = sol, Trecho = pc.Trecho });
                }
                else r.Add(new Peca { Id = pc.Nome, Secao = pc.Tipo == "parafuso" ? "PARAFUSOS" : "LIGACOES", Barra = false, Solido = sol });
            }
            return r;
        }

        // Volume (mm³) de um sólido de faces planas (divergência; faces orientadas para fora).
        public static double Volume(Solido s)
        {
            double v = 0;
            foreach (Point3D[] f in s.Lados.Concat(s.TampaI).Concat(s.TampaJ).Concat(s.Triangulos.Count > 0 && s.Lados.Count == 0 ? s.Triangulos : new List<Point3D[]>()))
                for (int k = 1; k + 1 < f.Length; k++) v += Vector3D.DotProduct((Vector3D)f[0], Vector3D.CrossProduct((Vector3D)f[k], (Vector3D)f[k + 1])) / 6;
            return v;
        }

        // ================= DWG / DXF =================

        // ponytail: vírgula também vira _ (o DXF com vírgula no nome da camada perde a camada no ezdxf)
        public static string CamadaDe(Peca p) => !p.Barra ? p.Secao : (p.Solido.Concreto ? "CONCRETO_" : "ACO_") + Regex.Replace(p.Secao.Length == 0 ? "SEM_SECAO" : p.Secao, "[<>/\\\\\":;?*|=`,]", "_");

        // Uma POLYFACE MESH por barra (ACadSharp não gera ACIS para 3DSOLID) na camada da seção + uma LINE por barra na
        // camada ANALITICO. Unidades mm, versão AC1032 (2018).
        public static void Cad(string arq, List<Peca> pecas, bool dxf)
        {
            var doc = new CadDocument(ACadVersion.AC1032);
            doc.Header.InsUnits = ACadSharp.Types.Units.UnitsType.Millimeters;
            doc.Header.MeasurementUnits = ACadSharp.Header.MeasurementUnits.Metric;
            var camadas = new Dictionary<string, Layer>();
            Layer Camada(string nome, short cor)
            {
                if (camadas.TryGetValue(nome, out Layer l)) return l;
                l = new Layer(nome) { Color = new Color(cor) };
                doc.Layers.Add(l);
                return camadas[nome] = l;
            }
            Layer analitica = Camada(CamadaAnalitica, 1);
            short[] cores = { 8, 4, 3, 5, 6, 2, 30, 140, 200, 50, 9, 172 };
            foreach (Peca p in pecas)
            {
                Layer l = camadas.TryGetValue(CamadaDe(p), out Layer l0) ? l0 : Camada(CamadaDe(p), cores[(camadas.Count - 1) % cores.Length]);
                var pm = new PolyfaceMesh { Layer = l };
                var idx = new Dictionary<Point3D, short>();
                short Ix(Point3D q)
                {
                    if (idx.TryGetValue(q, out short i)) return i;
                    pm.Vertices.Add(new VertexFaceMesh(new XYZ(q.X, q.Y, q.Z)));
                    return idx[q] = (short)pm.Vertices.Count;   // 1-based
                }
                foreach (Point3D[] f in p.Solido.Lados.Concat(p.Solido.Triangulos))
                    pm.Faces.Add(new VertexFaceRecord { Index1 = Ix(f[0]), Index2 = Ix(f[1]), Index3 = Ix(f[2]), Index4 = f.Length > 3 ? Ix(f[3]) : (short)0 });
                doc.Entities.Add(pm);
                if (p.Barra && !p.Trecho) doc.Entities.Add(new Line(new XYZ(p.A.X, p.A.Y, p.A.Z), new XYZ(p.B.X, p.B.Y, p.B.Z)) { Layer = analitica });
            }
            if (dxf) DxfWriter.Write(arq, doc, false);
            else DwgWriter.Write(arq, doc);
        }

        // ================= STEP AP214 =================

        static string R(double d) { string s = d.ToString("0.0#########", Inv); return s == "-0.0" ? "0.0" : s; }

        // Texto STEP: apóstrofo dobrado, barra invertida dobrada, fora do ASCII = \X2\hhhh\X0\.
        static string S(string s)
        {
            var sb = new StringBuilder("'");
            foreach (char c in s ?? "")
            {
                if (c == '\'') sb.Append("''");
                else if (c == '\\') sb.Append("\\\\");
                else if (c >= 32 && c < 127) sb.Append(c);
                else sb.Append("\\X2\\").Append(((int)c).ToString("X4")).Append("\\X0\\");
            }
            return sb.Append('\'').ToString();
        }

        // Montagem (PRODUCT do modelo) com um PRODUCT por barra ("B12 W310x38.7"); cada barra = FACETED_BREP de faces planas
        // (FACE_SURFACE + PLANE + POLY_LOOP; tampa do tubo com FACE_BOUND do furo). Posição já no sólido (transformação identidade).
        public static string Step(List<Peca> pecas, string titulo, string arqNome = "modelo.stp")
        {
            var sb = new StringBuilder(); int n = 0;
            int E(string corpo) { sb.Append('#').Append(++n).Append('=').Append(corpo).Append(";\n"); return n; }
            string L(IEnumerable<int> ids) => "(" + string.Join(",", ids.Select(i => "#" + i)) + ")";

            int ctx = E("APPLICATION_CONTEXT('automotive design')");
            E("APPLICATION_PROTOCOL_DEFINITION('international standard','automotive_design',2000,#" + ctx + ")");
            int pctx = E("PRODUCT_CONTEXT('',#" + ctx + ",'mechanical')");
            int pdctx = E("PRODUCT_DEFINITION_CONTEXT('part definition',#" + ctx + ",'design')");
            int uL = E("(LENGTH_UNIT()NAMED_UNIT(*)SI_UNIT(.MILLI.,.METRE.))");
            int uA = E("(NAMED_UNIT(*)PLANE_ANGLE_UNIT()SI_UNIT($,.RADIAN.))");
            int uS = E("(NAMED_UNIT(*)SI_UNIT($,.STERADIAN.)SOLID_ANGLE_UNIT())");
            int unc = E("UNCERTAINTY_MEASURE_WITH_UNIT(LENGTH_MEASURE(0.01),#" + uL + ",'distance_accuracy_value','')");
            int geo = E("(GEOMETRIC_REPRESENTATION_CONTEXT(3)GLOBAL_UNCERTAINTY_ASSIGNED_CONTEXT((#" + unc + "))GLOBAL_UNIT_ASSIGNED_CONTEXT((#" + uL + ",#" + uA + ",#" + uS + "))REPRESENTATION_CONTEXT('3D',''))");
            int o = E("CARTESIAN_POINT('',(0.0,0.0,0.0))"), dz = E("DIRECTION('',(0.0,0.0,1.0))"), dx = E("DIRECTION('',(1.0,0.0,0.0))");
            int eixo = E("AXIS2_PLACEMENT_3D('',#" + o + ",#" + dz + ",#" + dx + ")");

            (int pd, int pds) Produto(string id, string nome)
            {
                int p = E("PRODUCT(" + S(id) + "," + S(nome) + ",'',(#" + pctx + "))");
                E("PRODUCT_RELATED_PRODUCT_CATEGORY('part',$,(#" + p + "))");
                int pdf = E("PRODUCT_DEFINITION_FORMATION('','',#" + p + ")");
                int pd = E("PRODUCT_DEFINITION('design','',#" + pdf + ",#" + pdctx + ")");
                return (pd, E("PRODUCT_DEFINITION_SHAPE('','',#" + pd + ")"));
            }

            var (pdMont, pdsMont) = Produto(titulo, titulo);
            int srMont = E("SHAPE_REPRESENTATION('',(#" + eixo + "),#" + geo + ")");
            E("SHAPE_DEFINITION_REPRESENTATION(#" + pdsMont + ",#" + srMont + ")");

            int k = 0;
            foreach (Peca p in pecas)
            {
                string nome = p.Id + " " + p.Secao;
                var pts = new Dictionary<Point3D, int>();
                int Pt(Point3D q) => pts.TryGetValue(q, out int i) ? i : pts[q] = E("CARTESIAN_POINT('',(" + R(q.X) + "," + R(q.Y) + "," + R(q.Z) + "))");
                var faces = new List<int>();
                void Face(IList<Point3D[]> lacos)
                {
                    Point3D[] ext = lacos[0];
                    Vector3D nn = new Vector3D();   // Newell
                    for (int i = 0; i < ext.Length; i++) { Point3D a = ext[i], b = ext[(i + 1) % ext.Length]; nn += new Vector3D((a.Y - b.Y) * (a.Z + b.Z), (a.Z - b.Z) * (a.X + b.X), (a.X - b.X) * (a.Y + b.Y)); }
                    nn.Normalize();
                    Vector3D rx = ext[1] - ext[0]; rx -= Vector3D.DotProduct(rx, nn) * nn; rx.Normalize();
                    var bounds = new List<int>();
                    for (int i = 0; i < lacos.Count; i++)
                    {
                        int lp = E("POLY_LOOP(''," + L(lacos[i].Select(Pt).ToList()) + ")");
                        bounds.Add(E((i == 0 ? "FACE_OUTER_BOUND" : "FACE_BOUND") + "('',#" + lp + ",.T.)"));
                    }
                    int pl = E("PLANE('',#" + E("AXIS2_PLACEMENT_3D('',#" + Pt(ext[0]) + ",#" + E("DIRECTION('',(" + R(nn.X) + "," + R(nn.Y) + "," + R(nn.Z) + "))") + ",#" + E("DIRECTION('',(" + R(rx.X) + "," + R(rx.Y) + "," + R(rx.Z) + "))") + ")") + ")");
                    faces.Add(E("FACE_SURFACE(''," + L(bounds) + ",#" + pl + ",.T.)"));
                }
                foreach (Point3D[] q in p.Solido.Lados) Face(new[] { q });
                if (p.Solido.TampaI.Count > 0) Face(p.Solido.TampaI); if (p.Solido.TampaJ.Count > 0) Face(p.Solido.TampaJ);
                int shell = E("CLOSED_SHELL(''," + L(faces) + ")");
                int brep = E("FACETED_BREP(" + S(nome) + ",#" + shell + ")");
                int sr = E("FACETED_BREP_SHAPE_REPRESENTATION(" + S(nome) + ",(#" + brep + ",#" + eixo + "),#" + geo + ")");
                var (pd, pds) = Produto(p.Id, nome);
                E("SHAPE_DEFINITION_REPRESENTATION(#" + pds + ",#" + sr + ")");
                int nauo = E("NEXT_ASSEMBLY_USAGE_OCCURRENCE(" + S("NAUO" + (++k)) + "," + S(nome) + ",'',#" + pdMont + ",#" + pd + ",$)");
                int pdsN = E("PRODUCT_DEFINITION_SHAPE('','',#" + nauo + ")");
                int idt = E("ITEM_DEFINED_TRANSFORMATION('',''," + "#" + eixo + ",#" + eixo + ")");
                int rr = E("(REPRESENTATION_RELATIONSHIP('',''," + "#" + sr + ",#" + srMont + ")REPRESENTATION_RELATIONSHIP_WITH_TRANSFORMATION(#" + idt + ")SHAPE_REPRESENTATION_RELATIONSHIP())");
                E("CONTEXT_DEPENDENT_SHAPE_REPRESENTATION(#" + rr + ",#" + pdsN + ")");
            }

            string data = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", Inv);
            return "ISO-10303-21;\nHEADER;\nFILE_DESCRIPTION(('Estructto - modelo 3D'),'2;1');\nFILE_NAME(" + S(arqNome) + ",'" + data + "',(''),(''),'Estructto','Estructto','');\n" +
                   "FILE_SCHEMA(('AUTOMOTIVE_DESIGN { 1 0 10303 214 1 1 1 1 }'));\nENDSEC;\nDATA;\n" + sb + "ENDSEC;\nEND-ISO-10303-21;\n";
        }

        // Conferência estrita do texto STEP: cada linha de DATA = "#n=...;", ids únicos, parênteses e aspas fechados,
        // toda referência #k definida. Devolve o número de entidades; erros na lista.
        public static int ConferirStep(string txt, List<string> erros)
        {
            int a = txt.IndexOf("\nDATA;\n", StringComparison.Ordinal), b = txt.IndexOf("\nENDSEC;\nEND-ISO-10303-21;", a + 1, StringComparison.Ordinal);
            if (!txt.StartsWith("ISO-10303-21;") || a < 0 || b < 0) { erros.Add("cabeçalho/fim"); return 0; }
            var ids = new HashSet<int>(); var refs = new List<(int, int)>();
            foreach (string ln in txt.Substring(a + 7, b - a - 7).Split('\n'))
            {
                var m = Regex.Match(ln, @"^#(\d+)=(.*);$");
                if (!m.Success) { erros.Add("linha: " + ln); continue; }
                int id = int.Parse(m.Groups[1].Value);
                if (!ids.Add(id)) erros.Add("id repetido #" + id);
                string c = Regex.Replace(m.Groups[2].Value, "'([^']|'')*'", "$");   // tira o texto
                if (c.Contains('\'')) erros.Add("aspas #" + id);
                int prof = 0; foreach (char ch in c) { if (ch == '(') prof++; else if (ch == ')' && --prof < 0) break; }
                if (prof != 0) erros.Add("parênteses #" + id);
                foreach (Match r in Regex.Matches(c, @"#(\d+)")) refs.Add((id, int.Parse(r.Groups[1].Value)));
            }
            foreach (var (de, para) in refs) if (!ids.Contains(para)) erros.Add("#" + de + " aponta para #" + para + " (não existe)");
            return ids.Count;
        }

        // Sólido fechado e bem orientado: cada aresta orientada aparece uma vez e a oposta também (faces laterais + tampas).
        public static bool Fechado(Solido s)
        {
            var arestas = new Dictionary<(Point3D, Point3D), int>();
            foreach (Point3D[] l in s.Lados.Concat(s.TampaI).Concat(s.TampaJ))
                for (int i = 0; i < l.Length; i++) { var e = (l[i], l[(i + 1) % l.Length]); arestas[e] = arestas.TryGetValue(e, out int c) ? c + 1 : 1; }
            return arestas.Count > 0 && arestas.All(kv => kv.Value == 1 && arestas.TryGetValue((kv.Key.Item2, kv.Key.Item1), out int r) && r == 1);
        }
    }

    // Estructto.exe --testes-exportar <pasta>: exporta o guincho (2D) e o edifício do motor e confere relendo.
    public static class TestesExportar
    {
        // v0.6 Frente A (docs/v3/PONTAS-CONTRATO.md, v0.6): modelos mínimos de cada caso, 0 pares de interferência e sem vão > 2 mm.
        // Captura simples (WPF Viewport3D fora da tela) das peças da cena em volta de um ponto: antes/depois do relatório.
        static void Png(string arq, List<Render3D.Peca3D> col, Point3D centro, double raio, Vector3D olhar)
        {
            var grp = new Model3DGroup(); grp.Children.Add(new AmbientLight(System.Windows.Media.Color.FromRgb(80, 80, 90)));
            olhar.Normalize();
            grp.Children.Add(new DirectionalLight(System.Windows.Media.Color.FromRgb(200, 200, 200), olhar + new Vector3D(0.3, 0, -0.6)));
            grp.Children.Add(new DirectionalLight(System.Windows.Media.Color.FromRgb(90, 90, 90), new Vector3D(-olhar.X, -olhar.Y, -0.3)));
            foreach (var pc in col)
            {
                var g = new MeshGeometry3D(); foreach (var q in pc.M.P) g.Positions.Add(new Point3D(q.X, q.Y, q.Z)); foreach (int i in pc.M.T) g.TriangleIndices.Add(i);
                var c = pc.Tipo == "barra" ? System.Windows.Media.Color.FromRgb(150, 165, 180) : pc.Tipo == "parafuso" ? System.Windows.Media.Color.FromRgb(200, 170, 60) : System.Windows.Media.Color.FromRgb(90, 110, 140);
                var mat = new System.Windows.Media.Media3D.DiffuseMaterial(new System.Windows.Media.SolidColorBrush(c));
                grp.Children.Add(new GeometryModel3D(g, mat) { BackMaterial = mat });
            }
            var vp = new Viewport3D { Width = 640, Height = 480, Camera = new PerspectiveCamera(centro - olhar * raio * 5, olhar, new Vector3D(0, 0, 1), 45) };
            vp.Children.Add(new ModelVisual3D { Content = grp });
            var bd = new Border { Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(20, 24, 32)), Child = vp, Width = 640, Height = 480 };
            bd.Measure(new Size(640, 480)); bd.Arrange(new Rect(0, 0, 640, 480)); bd.UpdateLayout();
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(640, 480, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); bmp.Render(bd);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder(); enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            using var fs = File.Create(arq); enc.Save(fs);
        }

        static void V06Geo(Action<string, bool> Ok, string pasta)
        {
            string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
            Cena3D Cena(Modelo3D md, Func<string, Perfil3D> pf) => new Cena3D { Mod = md, Forma = s => GeometriaPerfis.CriarContorno(pf(s), 0.1), Cor = _ => System.Windows.Media.Colors.Gray, Selecionada = _ => false, Apoio = _ => 0,
                Pontos = b => { No3D p = md.Nos.First(n => n.Id == b.Ni), q = md.Nos.First(n => n.Id == b.Nj); return (new Point3D(p.X, p.Y, p.Z), new Point3D(q.X, q.Y, q.Z)); } };
            IEnumerable<Point3D> Vs(MalhaDx m) => m.P.Select(p => new Point3D(p.X, p.Y, p.Z));
            Perfil3D Pf(string s) => s == "P" ? new Perfil3D { Forma = "I", H = 0.2, B = 0.2, Tw = 0.008, Tf = 0.012 } : s == "V" ? new Perfil3D { Forma = "I", H = 0.3, B = 0.15, Tw = 0.006, Tf = 0.01 }
                                   : s == "TQ" ? new Perfil3D { Forma = "TQ", H = 0.1, B = 0.1, Tw = 0.005 } : new Perfil3D { Forma = "TC", H = s == "TC168" ? 0.168 : 0.114, Tw = 0.006 };
            Ligacao3D Marca(int no, int barra) => new Ligacao3D { No = no, Barra = barra, Nome = "marca v5", Ponta = new Ligacao3D.PontaMotor { P = new Vector3D(0.112, 0, 0), N = new Vector3D(-1, 0, 0) } };

            // A1: pilar I200 terminal (0..3 m) com viga I300 na mesa, na alma (pilar girado) e viga contínua sobre o pilar
            foreach (var (caso, rot, cont) in new[] { ("mesa", 0.0, false), ("alma", Math.PI / 2, false), ("viga contínua", 0.0, true) })
            {
                var m = new Modelo3D(); m.Nos.Add(new No3D { Id = 1 }); m.Nos.Add(new No3D { Id = 2, Z = 3 }); m.Nos.Add(new No3D { Id = 3, X = 4, Z = 3 }); m.Nos.Add(new No3D { Id = 4, X = -4, Z = 3 });
                var pil = new Barra3D { Id = 1, Ni = 1, Nj = 2, Secao = "P", Rot = rot }; m.Barras.Add(pil); m.Barras.Add(new Barra3D { Id = 2, Ni = 2, Nj = 3, Secao = "V" });
                if (cont) m.Barras.Add(new Barra3D { Id = 3, Ni = 4, Nj = 2, Secao = "V" });
                m.Ligacoes.Add(Marca(2, 2));
                var col = new List<Render3D.Peca3D>(); Render3D.Construir(Cena(m, Pf), col);
                Png(Path.Combine(pasta, "v06-A1-" + (cont ? "continua" : caso) + ".png"), col, new Point3D(0, 0, 3), 0.35, new Vector3D(1, 1.6, -0.5));
                double ztop = Vs(col.First(p => p.Barra == pil).M).Max(p => p.Z);
                int ip = Render3DGeometria.Interferencias(col.Where(p => p.Tipo == "barra").Select(p => p.M).ToList()).Count;
                Ok("v0.6 A1: pilar terminal, viga " + caso + ": pilar vai ao topo da viga (z " + F(ztop * 1000) + " mm = 3150), " + ip + " pares", Math.Abs(ztop - 3.15) < 1e-6 && ip == 0);
            }
            {   // joelho: viga I300 subindo a 10° no pilar terminal, chapa de topo do motor (16 mm, ponta a 116 mm): chapa na face do
                // pilar (vertical), viga cortada no plano vertical, pilar até o canto mais alto da viga na face
                double al = 10 * Math.PI / 180;
                var m = new Modelo3D(); m.Nos.Add(new No3D { Id = 1 }); m.Nos.Add(new No3D { Id = 2, Z = 3 }); m.Nos.Add(new No3D { Id = 3, X = 4 * Math.Cos(al), Z = 3 + 4 * Math.Sin(al) });
                var pil = new Barra3D { Id = 1, Ni = 1, Nj = 2, Secao = "P" }; m.Barras.Add(pil); var vg = new Barra3D { Id = 2, Ni = 2, Nj = 3, Secao = "V" }; m.Barras.Add(vg);
                var lg = new Ligacao3D { No = 2, Barra = 2, Nome = "chapa de topo", Ponta = new Ligacao3D.PontaMotor { P = new Vector3D(0.116, 0, 0), N = new Vector3D(-1, 0, 0) } };
                lg.Chapas.Add(new Ligacao3D.Chapa { Nome = "chapa de topo", Origem = new Vector3D(100, -75, -210), Normal = new Vector3D(1, 0, 0), U = new Vector3D(0, 1, 0), Espessura = 16, Contorno = new[] { new System.Windows.Point(0, 0), new System.Windows.Point(150, 0), new System.Windows.Point(150, 420), new System.Windows.Point(0, 420) } });
                m.Ligacoes.Add(lg);
                var col = new List<Render3D.Peca3D>(); Render3D.Construir(Cena(m, Pf), col);
                Png(Path.Combine(pasta, "v06-A1-joelho.png"), col, new Point3D(0.1, 0, 3.05), 0.4, new Vector3D(0.2, 1, -0.3));
                double ztop = Vs(col.First(p => p.Barra == pil).M).Max(p => p.Z), esperado = 3 + 0.1 * Math.Tan(al) + 0.15 / Math.Cos(al);
                var vc = Vs(col.First(p => p.Tipo == "chapa").M).ToList(); double xs = vc.Max(p => p.X) - vc.Min(p => p.X);
                int ip = Render3DGeometria.Interferencias(col.Where(p => p.Tipo == "barra" || p.Tipo == "chapa").Select(p => p.M).ToList()).Count;
                double xv = Vs(col.First(p => p.Barra == vg).M).Min(p => p.X);
                Ok("v0.6 A1: joelho a 10°: pilar até " + F(ztop * 1000) + " mm (canto da viga na face " + F(esperado * 1000) + "), chapa vertical (espessura em x " + F(xs * 1000) + " mm), viga começa em x " + F(xv * 1000) + " mm, " + ip + " pares",
                   Math.Abs(ztop - esperado) < 1e-6 && Math.Abs(xs - 0.016) < 1e-6 && Math.Abs(xv - 0.116) < 1e-6 && ip == 0);
            }
            {   // sem v5 (modelo sem pontas do motor): também (decisão v0.6: a v0.4 registrava o defeito)
                var m = new Modelo3D(); m.Nos.Add(new No3D { Id = 1 }); m.Nos.Add(new No3D { Id = 2, Z = 3 }); m.Nos.Add(new No3D { Id = 3, X = 4, Z = 3 });
                var pil = new Barra3D { Id = 1, Ni = 1, Nj = 2, Secao = "P" }; m.Barras.Add(pil); m.Barras.Add(new Barra3D { Id = 2, Ni = 2, Nj = 3, Secao = "V" });
                var col = new List<Render3D.Peca3D>(); Render3D.Construir(Cena(m, Pf), col);
                Ok("v0.6 A1: sem pontas do motor o pilar também vai ao topo da viga", Math.Abs(Vs(col.First(p => p.Barra == pil).M).Max(p => p.Z) - 3.15) < 1e-6);
            }

            // A2: tubo quadrado 100 inclinado 60° sobre placa de base
            {
                var m = new Modelo3D(); m.Nos.Add(new No3D { Id = 1, X = 1, Y = 1 }); m.Nos.Add(new No3D { Id = 2, X = 1 + 1.5, Y = 1, Z = 1.5 * Math.Sqrt(3) });
                var b = new Barra3D { Id = 1, Ni = 1, Nj = 2, Secao = "TQ" }; m.Barras.Add(b);
                var fb = GeometriaPerfis.CriarContorno(Pf("TQ"), 0.1);
                m.Ligacoes.Add(LigacoesAmostra.PlacaDeBase(1, b, true, fb, 300, 300, 19, 16, new List<(double, double)> { (-100, -100), (100, -100), (100, 100), (-100, 100) }, 0));
                var col = new List<Render3D.Peca3D>(); Render3D.Construir(Cena(m, Pf), col);
                Png(Path.Combine(pasta, "v06-A2-placa.png"), col, new Point3D(1.05, 1, 0.05), 0.3, new Vector3D(0.3, 1, -0.45));
                var tb = col.First(p => p.Barra == b); var pl = col.First(p => p.Tipo == "chapa"); var ch = col.Where(p => p.Tipo == "parafuso").ToList();
                var vp = Vs(pl.M).ToList(); var vb = Vs(tb.M).ToList();
                double zmin = vb.Min(p => p.Z), plZ0 = vp.Min(p => p.Z), plZ1 = vp.Max(p => p.Z);
                // centro da placa x pegada da barra no plano de cima
                var pe = vb.Where(p => p.Z < 0.019 + 1e-6).ToList();
                double cx = (vp.Min(p => p.X) + vp.Max(p => p.X)) / 2, cy = (vp.Min(p => p.Y) + vp.Max(p => p.Y)) / 2, px = (pe.Min(p => p.X) + pe.Max(p => p.X)) / 2, py = (pe.Min(p => p.Y) + pe.Max(p => p.Y)) / 2;
                bool vert = ch.Count == 4 && ch.All(p => { var v = Vs(p.M).ToList(); return v.Max(q => q.X) - v.Min(q => q.X) < 0.05 && v.Max(q => q.Z) - v.Min(q => q.Z) > 0.15; });
                int ip = Render3DGeometria.Interferencias(new[] { tb.M, pl.M }).Count;
                Ok("v0.6 A2: barra a 60° sobre a placa: placa horizontal (z " + F(plZ0 * 1000) + "–" + F(plZ1 * 1000) + " mm), barra assenta (z mín " + F(zmin * 1000) + " mm = 19), centro da placa a "
                   + F(Math.Sqrt((cx - px) * (cx - px) + (cy - py) * (cy - py)) * 1000) + " mm da pegada, chumbadores verticais, " + ip + " pares",
                   Math.Abs(plZ0) < 1e-6 && Math.Abs(plZ1 - 0.019) < 1e-6 && Math.Abs(zmin - 0.019) < 1e-6 && pe.Count >= 4 && Math.Abs(cx - px) < 0.002 && Math.Abs(cy - py) < 0.002 && vert && ip == 0);
            }

            // A3: diagonal TC114 chegando no pilar TC168 contínuo, com proposta de ligação (modelo nunca identificado): boca de peixe
            foreach (bool v5 in new[] { false, true })
            {
                var m = new Modelo3D(); m.Nos.Add(new No3D { Id = 1 }); m.Nos.Add(new No3D { Id = 2, Z = 3 }); m.Nos.Add(new No3D { Id = 3, Z = 6 }); m.Nos.Add(new No3D { Id = 4, X = 2, Z = 1 }); m.Nos.Add(new No3D { Id = 5, X = 3, Z = 3 });
                m.Barras.Add(new Barra3D { Id = 1, Ni = 1, Nj = 2, Secao = "TC168" }); m.Barras.Add(new Barra3D { Id = 2, Ni = 2, Nj = 3, Secao = "TC168" });
                var dg = new Barra3D { Id = 3, Ni = 2, Nj = 4, Secao = "TC114" }; m.Barras.Add(dg); m.Barras.Add(new Barra3D { Id = 4, Ni = 2, Nj = 5, Secao = "V" });
                m.Ligacoes.Add(new Ligacao3D { Pendente = "*", Proposta = true });
                if (v5)
                {   // + tubo_tubo_soldado do motor na diagonal (como o motor devolve: ponta boca_peixe no fundo da sela, filete, sem chapa)
                    m.Ligacoes.Add(Marca(2, 4));
                    double R = 0.084, r = 0.057, sn = Math.Sin(Math.Atan2(2, 2));
                    var gp = new Ligacao3D { No = 2, Barra = 3, Nome = "tubo-tubo soldado", Proposta = true, Ponta = new Ligacao3D.PontaMotor { P = new Vector3D(Math.Sqrt(R * R - r * r) / sn, 0, 0), N = new Vector3D(-1, 0, 0) } };
                    gp.Ponta.Recortes.Add(new Ligacao3D.Recorte { Barra = "viga", Tipo = "boca_peixe" });
                    gp.Soldas.Add(new Ligacao3D.Solda { Perna = 5, Pontos = new[] { new Vector3D(120, 0, 57), new Vector3D(120, 57, 0), new Vector3D(120, 0, -57) } });
                    m.Ligacoes.Add(gp);
                }
                var col = new List<Render3D.Peca3D>(); Render3D.Construir(Cena(m, Pf), col);
                Png(Path.Combine(pasta, "v06-A3-tubo" + (v5 ? "-v5" : "") + ".png"), col, new Point3D(0.15, 0, 2.9), 0.35, new Vector3D(0, 1, -0.15));
                var tb = col.First(p => p.Barra == dg); var pils = col.Where(p => p.Barra != null && p.Barra.Secao == "TC168").Select(p => p.M).ToList();
                double rmin = Vs(tb.M).Min(p => Math.Sqrt(p.X * p.X + p.Y * p.Y));
                bool solta = col.Any(p => p.Lig != null && p.Lig.Barra == dg.Id && (p.Tipo == "chapa" || p.Tipo == "parafuso" || p.Tipo == "cantoneira"));
                int ip = pils.Sum(pm => Render3DGeometria.Interferencias(new[] { tb.M, pm }).Count);
                Ok("v0.6 A3: tubo em tubo" + (v5 ? " (v5)" : "") + ": boca de peixe encosta no pilar (menor raio " + F(rmin * 1000) + " mm, R = 84), sem chapa nem parafuso no nó do tubo, " + ip + " pares",
                   Math.Abs(rmin - 0.084) < 0.002 && rmin > 0.084 - 1e-4 && !solta && ip == 0);
            }
        }

        public static string Rodar(string pasta, string raiz)
        {
            var sb = new StringBuilder(); int falhas = 0;
            void Ok(string nome, bool c) { sb.AppendLine((c ? "ok    " : "FALHA ") + nome); if (!c) falhas++; }
            Directory.CreateDirectory(pasta);
            Render3DGeometria.Testar((c, nome) => Ok("render3d: " + nome, c));   // juntas, extrusão com corte, triangulação
            LigacoesAmostra.Testar((c, nome) => Ok("ligacoes3d: " + nome, c));   // contrato ida e volta, chapa de topo, placa de base
            {   // onda final: a troca do motor é proposta por membro; "Aplicar troca" manda grupo + barra + nó (não o grupo inteiro)
                var tr = new System.Text.Json.Nodes.JsonObject { ["grupo"] = "G7", ["barra"] = "B3", ["no"] = "N9", ["tipo"] = "solda_direta_alma", ["motivo"] = "m", ["dados"] = new System.Text.Json.Nodes.JsonObject { ["perfil_viga"] = "UPN 50", ["b_solda"] = 3 } };
                var e = DocumentoEspacial.PedidoAplicarTroca(tr);
                Ok("ligacoes3d: Aplicar troca pede grupo, barra e nó com o tipo proposto", (string)e["grupo"] == "G7" && (string)e["barra"] == "B3" && (string)e["no"] == "N9" && (string)e["tipo_ligacao"] == "solda_direta_alma" && (string)e["dados"]?["perfil_viga"] == "UPN 50" && e["acao"] == null);
                var dA = new DocumentoEspacial(); var dB = new DocumentoEspacial(); dA.TrocasPropostas.Add(tr);
                Ok("ligacoes3d: propostas de troca são por documento (abrir outro modelo não herda)", dB.TrocasPropostas.Count == 0 && dA.TrocasPropostas.Count == 1);
            }
            {   // ponto cardinal 8 (topo) numa viga I de 200 mm: o sólido exportado desce H/2 (topo no eixo, base 200 mm abaixo)
                var mc = new Modelo3D(); mc.Nos.Add(new No3D { Id = 1 }); mc.Nos.Add(new No3D { Id = 2, X = 5 });
                mc.Barras.Add(new Barra3D { Id = 1, Ni = 1, Nj = 2, Secao = "I200", Cardinal = 8 });
                var pc = Exportador.Pecas(mc, _ => new Perfil3D { Forma = "I", H = 0.2, B = 0.1, Tw = 0.006, Tf = 0.008 });
                var zs = pc[0].Solido.Lados.SelectMany(q => q).Select(q => q.Z).ToList();
                Ok("exportar: cardinal 8 desloca o sólido H/2 (z " + zs.Min().ToString("0.#", CultureInfo.InvariantCulture) + " a " + zs.Max().ToString("0.#", CultureInfo.InvariantCulture) + " mm)",
                   Math.Abs(zs.Max()) < 1e-6 && Math.Abs(zs.Min() + 200) < 1e-6);
            }

            {   // pontas cortadas como no Realista: viga I 200 chega num pilar I 200 (mesa) e para na face + 12 mm (x = 112 mm)
                var mc = new Modelo3D(); mc.Nos.Add(new No3D { Id = 1 }); mc.Nos.Add(new No3D { Id = 2, Z = 3 }); mc.Nos.Add(new No3D { Id = 3, X = 4, Z = 3 });
                mc.Barras.Add(new Barra3D { Id = 1, Ni = 1, Nj = 2, Secao = "I200" }); mc.Barras.Add(new Barra3D { Id = 2, Ni = 2, Nj = 3, Secao = "I200" });
                var pc = Exportador.Pecas(mc, _ => new Perfil3D { Forma = "I", H = 0.2, B = 0.1, Tw = 0.006, Tf = 0.008 });
                double xmin = pc[1].Solido.Lados.SelectMany(q => q).Min(q => q.X);
                Ok("exportar: viga cortada na face do pilar + folga (início em x = " + xmin.ToString("0.#", CultureInfo.InvariantCulture) + " mm), sólido fechado", Math.Abs(xmin - 112) < 1e-6 && Exportador.Fechado(pc[1].Solido));
            }

            Modelo m2 = Modelo.DeXml(File.ReadAllText(Path.Combine(raiz, "exemplos", "guincho-coluna-escorada.estructto")));
            var guincho = Modelo3D.De(m2, null);
            var esp = new DocumentoEspacial { Json = JsonNode.Parse(File.ReadAllText(Path.Combine(raiz, "codigo", "motor", "exemplos", "edificio_concreto.json"))).AsObject() };
            var edificio = esp.ParaVista();
            Perfil3D PerfilEsp(string nome) => esp.Json["secoes"]?[nome] is JsonObject s ? Perfil3D.DoMotor(s) : null;
            Perfil3D Perfil2D(string nome) => Perfil3D.DoCatalogo(Biblioteca.AcharSecao(nome));

            {   // cope do motor: viga I 300 (pontas: plano a 50 mm, cope de 40 × 100 mm na mesa de cima) chega em viga I 400 contínua;
                // o sólido exportado perde o volume do cope (= malha do Realista ± 1 %) e a chapa da ligação sai junto
                var mc = new Modelo3D(); mc.Nos.Add(new No3D { Id = 1 }); mc.Nos.Add(new No3D { Id = 2, X = 4 }); mc.Nos.Add(new No3D { Id = 3, Y = -3 }); mc.Nos.Add(new No3D { Id = 4, Y = 3 });
                var vg = new Barra3D { Id = 1, Ni = 1, Nj = 2, Secao = "I300" };
                mc.Barras.Add(vg); mc.Barras.Add(new Barra3D { Id = 2, Ni = 3, Nj = 1, Secao = "I400" }); mc.Barras.Add(new Barra3D { Id = 3, Ni = 1, Nj = 4, Secao = "I400" });
                var lg = new Ligacao3D { No = 1, Barra = 1, Nome = "teste cope", Ponta = new Ligacao3D.PontaMotor { P = new Vector3D(0.05, 0, 0), N = new Vector3D(-1, 0, 0) } };
                lg.Ponta.Recortes.Add(new Ligacao3D.Recorte { Barra = "viga", Tipo = "cope", Posicao = "topo", Altura = 40, Comprimento = 100 });
                lg.Chapas.Add(new Ligacao3D.Chapa { Nome = "chapa simples", Origem = new Vector3D(10, 3, -100), Normal = new Vector3D(0, 1, 0), U = new Vector3D(0, 0, 1), Espessura = 10, Contorno = new[] { new System.Windows.Point(0, 0), new System.Windows.Point(200, 0), new System.Windows.Point(200, 100), new System.Windows.Point(0, 100) } });
                mc.Ligacoes.Add(lg);
                Perfil3D Pf(string sec) => sec == "I300" ? new Perfil3D { Forma = "I", H = 0.3, B = 0.15, Tw = 0.006, Tf = 0.01 } : new Perfil3D { Forma = "I", H = 0.4, B = 0.2, Tw = 0.008, Tf = 0.012 };
                var pc = Exportador.Pecas(mc, Pf);
                Cena3D Cena(Modelo3D md, Func<string, Perfil3D> pf) => new Cena3D { Mod = md, Forma = s => GeometriaPerfis.CriarContorno(pf(s), 0.1), Cor = _ => System.Windows.Media.Colors.Gray, Selecionada = _ => false, Apoio = _ => 0,
                    Pontos = b => { No3D p = md.Nos.First(n => n.Id == b.Ni), q = md.Nos.First(n => n.Id == b.Nj); return (new Point3D(p.X, p.Y, p.Z), new Point3D(q.X, q.Y, q.Z)); } };
                var pvs = pc.Where(p => p.Barra && p.Id == Exportador.IdBarra(vg)).ToList();
                double A = 0.15 * 0.01 * 2 + 0.006 * 0.28, Acope = 0.15 * 0.01 + 0.006 * 0.03;   // seção e o que o cope tira (mesa + 30 mm de alma)
                double esperado = (A * (4 - 0.05) - Acope * 0.1) * 1e9, vol = pvs.Sum(p => Exportador.Volume(p.Solido));
                var col = new List<Render3D.Peca3D>(); Render3D.Construir(Cena(mc, Pf), col);
                double volRealista = col.Where(x => x.Barra == vg).Sum(x => Render3DGeometria.Fechada(x.M).Volume) * 1e9;
                Ok("exportar: viga com cope do motor, volume " + (vol / 1e3).ToString("0", CultureInfo.InvariantCulture) + " cm³ = Realista " + (volRealista / 1e3).ToString("0", CultureInfo.InvariantCulture) + " = esperado " + (esperado / 1e3).ToString("0", CultureInfo.InvariantCulture) + " (± 1 %)",
                   Math.Abs(vol / esperado - 1) < 0.01 && Math.Abs(vol / volRealista - 1) < 0.01);
                Ok("exportar: viga com cope = " + pvs.Count + " sólidos (trecho do cope + resto), cada um estanque (toda aresta em 2 faces) e com volume finito",
                   pvs.Count == 2 && pvs.All(p => Exportador.Fechado(p.Solido) && double.IsFinite(Exportador.Volume(p.Solido)) && Exportador.Volume(p.Solido) > 0));
                // tubo Ø 114 em tubo Ø 168 (boca de peixe, modelo com pontas) e gusset do motor num tubo: chapa sobre a superfície
                var mt = new Modelo3D(); mt.Nos.Add(new No3D { Id = 1 }); mt.Nos.Add(new No3D { Id = 2, X = 4 }); mt.Nos.Add(new No3D { Id = 3, X = 2 }); mt.Nos.Add(new No3D { Id = 4, X = 3, Z = 1.732 });
                mt.Barras.Add(new Barra3D { Id = 1, Ni = 1, Nj = 3, Secao = "TC168" }); mt.Barras.Add(new Barra3D { Id = 2, Ni = 3, Nj = 2, Secao = "TC168" }); var br = new Barra3D { Id = 3, Ni = 3, Nj = 4, Secao = "TC114" }; mt.Barras.Add(br);
                mt.Ligacoes.Add(new Ligacao3D { No = 1, Barra = 1, Nome = "marca v5", Ponta = new Ligacao3D.PontaMotor { P = new Vector3D(0.001, 0, 0), N = new Vector3D(-1, 0, 0) } });   // modelo com pontas: regras novas
                Perfil3D Pt(string sec) => new Perfil3D { Forma = "TC", H = sec == "TC168" ? 0.168 : 0.114, Tw = 0.006 };
                var pt = Exportador.Pecas(mt, Pt).Where(p => p.Barra && p.Id == Exportador.IdBarra(br)).ToList();
                Ok("exportar: tubo com boca de peixe estanque e com volume finito (" + pt.Count + " sólido)", pt.Count == 1 && Exportador.Fechado(pt[0].Solido) && double.IsFinite(Exportador.Volume(pt[0].Solido)) && Exportador.Volume(pt[0].Solido) > 0);
                var mg = new Modelo3D(); mg.Nos.Add(new No3D { Id = 1 }); mg.Nos.Add(new No3D { Id = 2, Z = 3 }); mg.Nos.Add(new No3D { Id = 3, Z = 1.5 }); mg.Nos.Add(new No3D { Id = 4, X = 2, Z = 3.5 });
                mg.Barras.Add(new Barra3D { Id = 1, Ni = 1, Nj = 3, Secao = "W" }); mg.Barras.Add(new Barra3D { Id = 2, Ni = 3, Nj = 2, Secao = "W" }); var dg = new Barra3D { Id = 3, Ni = 3, Nj = 4, Secao = "TC114" }; mg.Barras.Add(dg);
                var lgg = new Ligacao3D { No = 3, Barra = 3, Nome = "gusset tubo", Ponta = new Ligacao3D.PontaMotor { P = new Vector3D(0.3, 0, 0), N = new Vector3D(-1, 0, 0) } };
                lgg.Chapas.Add(new Ligacao3D.Chapa { Nome = "gusset", Origem = new Vector3D(0, 5, 0), Normal = new Vector3D(0, -1, 0), U = new Vector3D(1, 0, 0), Espessura = 10,
                                                     Contorno = new[] { new System.Windows.Point(150, -150), new System.Windows.Point(600, -150), new System.Windows.Point(600, 150), new System.Windows.Point(150, 150) } });   // como o motor: chapa no eixo (rasgaria o tubo)
                mg.Ligacoes.Add(lgg);
                Perfil3D Pg(string sec) => sec == "W" ? new Perfil3D { Forma = "I", H = 0.25, B = 0.25, Tw = 0.009, Tf = 0.014 } : new Perfil3D { Forma = "TC", H = 0.114, Tw = 0.006 };
                var cg = new List<Render3D.Peca3D>(); Render3D.Construir(Cena(mg, Pg), cg);
                var tubo = cg.First(x => x.Barra == dg); var chg = cg.First(x => x.Tipo == "chapa");
                var ig = Render3DGeometria.Interferencias(new[] { tubo.M, chg.M });
                Ok("gusset do motor num tubo: chapa sobre a superfície (R + esp/2 do eixo), sem interferência com o tubo (" + ig.Count + " pares)", ig.Count == 0);
                // G6: gusset do motor centrado no tubo com o rasgo na ponta (apoio_dir): o tubo é aberto, sem interferência; export estanque
                var mr = new Modelo3D(); foreach (var n in mg.Nos) mr.Nos.Add(n); foreach (var b in mg.Barras) mr.Barras.Add(b);
                var lgr = new Ligacao3D { No = 3, Barra = 3, Nome = "gusset rasgo", Ponta = new Ligacao3D.PontaMotor { P = new Vector3D(0.3, 0, 0), N = new Vector3D(-1, 0, 0) },
                                          ApoioDir = new Vector3D(0.7071, 0, 0.7071), ApoioMundo = new Vector3D(0, 0, 1) };
                lgr.Ponta.Recortes.Add(new Ligacao3D.Recorte { Barra = "viga", Tipo = "rasgo", Altura = 12, Comprimento = 260 });
                lgr.Chapas.Add(new Ligacao3D.Chapa { Nome = "gusset", Origem = new Vector3D(0, 5, 0), Normal = new Vector3D(0, -1, 0), U = new Vector3D(1, 0, 0), Espessura = 10,
                                                     Contorno = new[] { new System.Windows.Point(150, -150), new System.Windows.Point(550, -150), new System.Windows.Point(550, 150), new System.Windows.Point(150, 150) } });
                mr.Ligacoes.Add(lgr);
                var cr = new List<Render3D.Peca3D>(); Render3D.Construir(Cena(mr, Pg), cr);
                var tr = cr.Where(x => x.Barra == dg).ToList(); var chr = cr.First(x => x.Tipo == "chapa");
                int ir = tr.Sum(x => Render3DGeometria.Interferencias(new[] { x.M, chr.M }).Count);
                var pr = Exportador.Pecas(mr, Pg).Where(p => p.Barra && p.Id == Exportador.IdBarra(dg)).ToList(); var er = new List<string>(); Exportador.ConferirStep(Exportador.Step(Exportador.Pecas(mr, Pg), "rasgo", "rasgo.stp"), er);
                Ok("gusset centrado no tubo com rasgo: " + tr.Count + " sólidos do tubo (trecho rasgado + resto), " + ir + " pares com a chapa; export " + pr.Count + " sólidos estanques, STEP sem erro",
                   tr.Count == 2 && ir == 0 && pr.Count == 2 && pr.All(p => Exportador.Fechado(p.Solido) && Exportador.Volume(p.Solido) > 0) && er.Count == 0);
                // ids repetidos (duas barras com o mesmo id do motor): cada sólido fica com a sua barra
                var md = new Modelo3D(); md.Nos.Add(new No3D { Id = 1 }); md.Nos.Add(new No3D { Id = 2, X = 3 }); md.Nos.Add(new No3D { Id = 3, Y = 5 }); md.Nos.Add(new No3D { Id = 4, X = 3, Y = 5 });
                md.Barras.Add(new Barra3D { Id = 1, Ni = 1, Nj = 2, Secao = "W", Origem = new RefMotor { Id = "B1" } }); md.Barras.Add(new Barra3D { Id = 2, Ni = 3, Nj = 4, Secao = "W", Origem = new RefMotor { Id = "B1" } });
                var pd = Exportador.Pecas(md, Pg);
                Ok("exportar: ids repetidos — 2 peças de barra, cada uma na sua posição (y 0 e 5000 mm)", pd.Count(p => p.Barra) == 2 && pd.Select(p => p.A.Y).OrderBy(y => y).SequenceEqual(new[] { 0.0, 5000.0 }) && pd.All(p => p.Solido.Lados.SelectMany(q => q).All(q => Math.Abs(q.Y - p.A.Y) < 200)));
                Ok("exportar: chapa da ligação sai como peça (" + pc.Count(p => !p.Barra) + ")", pc.Any(p => !p.Barra && p.Id.Contains("chapa simples") && Math.Abs(Exportador.Volume(p.Solido) - 200 * 100 * 10) < 1));
                string arqC = Path.Combine(pasta, "cope.dwg"); Exportador.Cad(arqC, pc, false);
                var dc = DwgReader.Read(arqC); var errC = new List<string>(); Exportador.ConferirStep(Exportador.Step(pc, "cope", "cope.stp"), errC);
                Ok("exportar: DWG com " + dc.Entities.OfType<PolyfaceMesh>().Count() + " malhas (barras + chapa) e STEP sem erro", dc.Entities.OfType<PolyfaceMesh>().Count() == pc.Count && errC.Count == 0);
            }

            V06Geo(Ok, pasta);

            {   // Review Focus 3: modelo sem pontas do motor (guincho .estructto, galpão do motor) = planos de corte da v0.4
                var gal = new DocumentoEspacial { Json = JsonNode.Parse(File.ReadAllText(Path.Combine(raiz, "codigo", "motor", "exemplos", "galpao_aco.json"))).AsObject() };
                Perfil3D PerfilGal(string nome) => gal.Json["secoes"]?[nome] is JsonObject s ? Perfil3D.DoMotor(s) : null;
                foreach (var (nome, mod, perfil) in new[] { ("guincho", guincho, (Func<string, Perfil3D>)Perfil2D), ("galpao_aco", gal.ParaVista(), PerfilGal) })
                {
                    var nos = mod.Nos.ToDictionary(n => n.Id);
                    Func<Barra3D, (Point3D, Point3D)> pts = b => (new Point3D(nos[b.Ni].X, nos[b.Ni].Y, nos[b.Ni].Z), new Point3D(nos[b.Nj].X, nos[b.Nj].Y, nos[b.Nj].Z));
                    Func<Barra3D, GeometriaPerfis.Contorno> fm = b => GeometriaPerfis.CriarContorno(perfil(b.Secao ?? ""), 0.1);
                    Func<Barra3D, bool, double> fg = (b, emI) => fm(b).Concreto ? 0 : 0.012, nan = (b, emI) => double.NaN;
                    var novo = Render3DGeometria.Juntas(mod, pts, fm, fg, nan); var v04 = Render3DGeometria.JuntasV04(mod, pts, fm, fg, nan);
                    // v0.6 (decisão do coordenador): a v0.4 deixava o pilar terminal parar no nó (defeito do 9.jpg). Fora isso, iguais;
                    // os planos novos só podem ser o topo de pilar terminal (vertical, além do nó, Recuo < 0)
                    var topos = novo.Where(kv => !v04.ContainsKey(kv.Key)).ToList();
                    bool soTopos = topos.All(kv => kv.Value.Recuo < 0 && Math.Abs(kv.Value.N.Z) > 0.999);
                    int dif = v04.Count(kv => !novo.TryGetValue(kv.Key, out Corte k) || (k.P - kv.Value.P).Length > 1e-9 || (k.N - kv.Value.N).Length > 1e-9)
                              + novo.Values.Count(k => k.Extra != null || k.Raio > 0);
                    Ok("sem pontas (v0.4): " + nome + " com os mesmos " + v04.Count + " planos de corte da v0.4 (" + dif + " diferentes) + " + topos.Count + " topos de pilar terminal (v0.6)", dif == 0 && soTopos && v04.Count > 0);
                }
            }

            foreach (var (nome, mod, perfil) in new[] { ("guincho", guincho, (Func<string, Perfil3D>)Perfil2D), ("edificio", edificio, PerfilEsp) })
            {
                var pecas = Exportador.Pecas(mod, perfil);
                Ok(nome + ": uma peça por barra (" + pecas.Count(p => p.Barra && !p.Trecho) + ") + " + pecas.Count(p => !p.Barra) + " de ligação", pecas.Count(p => p.Barra && !p.Trecho) == mod.Barras.Count && pecas.Count > 0);
                Ok(nome + ": sólidos fechados e orientados", pecas.All(p => Exportador.Fechado(p.Solido)));
                var camadas = new HashSet<string>(pecas.Select(Exportador.CamadaDe)) { Exportador.CamadaAnalitica };
                double x0 = mod.Nos.Min(n => n.X) * 1000, x1 = mod.Nos.Max(n => n.X) * 1000, y0 = mod.Nos.Min(n => n.Y) * 1000, y1 = mod.Nos.Max(n => n.Y) * 1000, z0 = mod.Nos.Min(n => n.Z) * 1000, z1 = mod.Nos.Max(n => n.Z) * 1000;
                double Dseg(Point3D q, Point3D a, Point3D b) { Vector3D ab = b - a; double t = Math.Max(0, Math.Min(1, Vector3D.DotProduct(q - a, ab) / ab.LengthSquared)); return (q - (a + ab * t)).Length; }
                double folga = pecas.Where(p => p.Barra).SelectMany(p => p.Solido.Lados.SelectMany(q => q).Select(q => Dseg(q, p.A, p.B))).Max() + 1e-3;   // meia seção

                foreach (bool dxf in new[] { false, true })
                {
                    string arq = Path.Combine(pasta, nome + (dxf ? ".dxf" : ".dwg"));
                    Exportador.Cad(arq, pecas, dxf);
                    CadDocument d = dxf ? DxfReader.Read(arq) : DwgReader.Read(arq);
                    string t = nome + (dxf ? " DXF" : " DWG");
                    var linhas = d.Entities.OfType<Line>().ToList(); var malhas = d.Entities.OfType<PolyfaceMesh>().ToList();
                    Ok(t + ": " + linhas.Count + " linhas e " + malhas.Count + " malhas = barras", linhas.Count == pecas.Count(p => p.Barra && !p.Trecho) && malhas.Count == pecas.Count);
                    Ok(t + ": linhas na camada " + Exportador.CamadaAnalitica, linhas.All(l => l.Layer?.Name == Exportador.CamadaAnalitica));
                    var lidas = new HashSet<string>(d.Layers.Select(l => l.Name));
                    Ok(t + ": " + camadas.Count + " camadas (seção/tipo + analítica)", camadas.All(lidas.Contains) && malhas.Select(p => p.Layer?.Name).Distinct().Count() == camadas.Count - 1);
                    Ok(t + ": faces das malhas = " + pecas.Sum(p => p.Solido.Lados.Count + p.Solido.Triangulos.Count), malhas.Sum(p => p.Faces.Count) == pecas.Sum(p => p.Solido.Lados.Count + p.Solido.Triangulos.Count));
                    Ok(t + ": unidades mm, versão AC1032", d.Header.InsUnits == ACadSharp.Types.Units.UnitsType.Millimeters && d.Header.Version == ACadVersion.AC1032);
                    var pl = linhas.SelectMany(l => new[] { l.StartPoint, l.EndPoint }).ToList();
                    bool Perto(double a, double b) => Math.Abs(a - b) < 1e-6;
                    Ok(t + ": caixa das linhas = caixa dos nós (mm)", Perto(pl.Min(p => p.X), x0) && Perto(pl.Max(p => p.X), x1) && Perto(pl.Min(p => p.Y), y0) && Perto(pl.Max(p => p.Y), y1) && Perto(pl.Min(p => p.Z), z0) && Perto(pl.Max(p => p.Z), z1));
                    var pm = malhas.Where(p => p.Layer?.Name != "LIGACOES" && p.Layer?.Name != "PARAFUSOS").SelectMany(p => p.Vertices.Select(v => v.Location)).ToList();
                    Ok(t + ": caixa das malhas = caixa dos nós ± meia seção (" + folga.ToString("0.#", CultureInfo.InvariantCulture) + " mm)",
                        pm.Min(p => p.X) >= x0 - folga && pm.Max(p => p.X) <= x1 + folga && pm.Min(p => p.Y) >= y0 - folga && pm.Max(p => p.Y) <= y1 + folga && pm.Min(p => p.Z) >= z0 - folga && pm.Max(p => p.Z) <= z1 + folga &&
                        pm.Min(p => p.X) <= x0 + folga && pm.Max(p => p.X) >= x1 - folga && pm.Min(p => p.Y) <= y0 + folga && pm.Max(p => p.Y) >= y1 - folga && pm.Min(p => p.Z) <= z0 + folga && pm.Max(p => p.Z) >= z1 - folga);   // pontas cortadas: o extremo fica a menos de meia seção do nó
                }

                string stp = Exportador.Step(pecas, nome, nome + ".stp");
                File.WriteAllText(Path.Combine(pasta, nome + ".stp"), stp, new UTF8Encoding(false));
                var erros = new List<string>();
                int nEnt = Exportador.ConferirStep(stp, erros);
                // entidades esperadas: 13 de contexto + 7 da montagem; por peça: pontos + 2/laço + 5/face + 13
                int esperado = 13 + 7 + pecas.Sum(p =>
                {
                    var lacos = p.Solido.Lados.Concat(p.Solido.TampaI).Concat(p.Solido.TampaJ).ToList();
                    return lacos.SelectMany(l => l).Distinct().Count() + 2 * lacos.Count + 5 * (p.Solido.Lados.Count + (p.Solido.TampaI.Count > 0 ? 1 : 0) + (p.Solido.TampaJ.Count > 0 ? 1 : 0)) + 4 + 5 + 1 + 4;
                });
                Ok(nome + " STEP: " + nEnt + " entidades (esperado " + esperado + ")", nEnt == esperado);
                Ok(nome + " STEP: referências resolvidas" + (erros.Count > 0 ? " — " + string.Join("; ", erros.Take(5)) : ""), erros.Count == 0);
                Ok(nome + " STEP: " + pecas.Count + " FACETED_BREP e PRODUCT por barra", Regex.Matches(stp, "=FACETED_BREP\\(").Count == pecas.Count && Regex.Matches(stp, "=PRODUCT\\(").Count == pecas.Count + 1);
            }
            sb.AppendLine(falhas == 0 ? "TODOS PASSARAM" : falhas + " FALHA(S)");
            return sb.ToString();
        }
    }

    // ================= interface: Arquivo › Exportar e paleta =================
    public partial class MainWindow
    {
        F.BackstageTabItem TabExportar()
        {
            var sp = new StackPanel { Margin = new Thickness(30, 20, 30, 20), MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left };
            sp.Children.Add(new TextBlock { Text = "Exportar o modelo 3D", FontSize = TemaPlant.TamDisplay, FontWeight = FontWeights.Medium, FontFamily = TemaPlant.FonteTitulo, Margin = new Thickness(0, 0, 0, 14) });
            foreach (var (fmt, dica) in new[] { ("DWG", "AutoCAD 2018 (AC1032): uma malha por barra na camada da seção + eixos analíticos, em mm."), ("DXF", "O mesmo conteúdo do DWG em DXF (texto)."), ("STEP", "STEP AP214: uma peça (sólido de faces planas) por barra, numa montagem, em mm.") })
            {
                // cartoes de acao iguais aos de Importar (Backstage.cs AcaoBs): icone da familia + titulo + descricao
                string f = fmt;
                sp.Children.Add(AcaoBs(f.ToLowerInvariant(), f + "…", dica, () => { Bastidor.IsOpen = false; ExportarModelo(f); }, 480));
            }
            return new F.BackstageTabItem { Header = "Exportar", Content = sp, ToolTip = "Exporta o modelo 3D para DWG, DXF ou STEP." };
        }

        void RegistrarExportar()
        {
            Extra("Exportar DWG", null, "salvar", null, "autocad dwg cad 3d malha", "Exporta o modelo 3D para DWG (AutoCAD 2018, mm).", () => ExportarModelo("DWG"));
            Extra("Exportar DXF", null, "salvar", null, "autocad dxf cad 3d", "Exporta o modelo 3D para DXF (mm).", () => ExportarModelo("DXF"));
            Extra("Exportar STEP", null, "salvar", null, "step stp ap214 sólido tekla revit inventor", "Exporta o modelo 3D para STEP AP214 (mm).", () => ExportarModelo("STEP"));
        }

        internal void ExportarModelo(string fmt)
        {
            Modelo3D mod = Esp != null ? Esp.ParaVista() : Modelo3D.De(M, null);
            if (mod.Barras.Count == 0) { Log("Nada para exportar: o modelo não tem barras."); return; }
            string baseNome = Esp != null ? Path.GetFileNameWithoutExtension(Esp.Nome) : arquivo == null ? "modelo" : Path.GetFileNameWithoutExtension(arquivo);
            string ext = fmt == "STEP" ? "stp" : fmt.ToLowerInvariant();
            var d = new SaveFileDialog { Filter = fmt + " (*." + ext + ")|*." + ext, FileName = baseNome + "." + ext };
            if (d.ShowDialog(this) != true) return;
            try
            {
                var pecas = Exportador.Pecas(mod, PerfilDaSecao);
                if (fmt == "STEP") File.WriteAllText(d.FileName, Exportador.Step(pecas, baseNome, Path.GetFileName(d.FileName)), new UTF8Encoding(false));
                else Exportador.Cad(d.FileName, pecas, fmt == "DXF");
                Log(fmt + " exportado: " + Pl.N(pecas.Count, "barra", "barras") + " → " + d.FileName);
            }
            catch (Exception ex) { Log("Falha ao exportar " + fmt + ": " + ex.Message); }
        }
    }
}
