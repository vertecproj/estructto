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
    public sealed class Peca { public string Id, Secao; public Point3D A, B; public Solido Solido; }

    public static class Exportador
    {
        const double Mm = 1000;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        public const string CamadaAnalitica = "ANALITICO";

        public static string IdBarra(Barra3D b) => b.Origem is RefMotor r ? r.Id : b.Origem is Barra b2 ? "B" + b2.Id : "B" + b.Id;

        // ponytail: seção sem dados = quadrado de 100 mm (a Vista3D usa um lado proporcional ao modelo)
        public static List<Peca> Pecas(Modelo3D m, Func<string, Perfil3D> perfil)
        {
            var nos = m.Nos.ToDictionary(n => n.Id);
            var formas = new Dictionary<string, GeometriaPerfis.Contorno>();
            var r = new List<Peca>();
            foreach (Barra3D b in m.Barras)
            {
                if (!nos.TryGetValue(b.Ni, out No3D ni) || !nos.TryGetValue(b.Nj, out No3D nj)) continue;
                Point3D a = new Point3D(ni.X, ni.Y, ni.Z), c = new Point3D(nj.X, nj.Y, nj.Z);
                if ((c - a).Length < 1e-9) continue;
                string sec = b.Secao ?? "";
                if (!formas.TryGetValue(sec, out var f))
                {
                    Perfil3D p = null; try { p = perfil?.Invoke(sec); } catch { }
                    formas[sec] = f = GeometriaPerfis.CriarContorno(p, 0.1);
                }
                r.Add(new Peca
                {
                    Id = IdBarra(b), Secao = sec, A = new Point3D(a.X * Mm, a.Y * Mm, a.Z * Mm), B = new Point3D(c.X * Mm, c.Y * Mm, c.Z * Mm),
                    Solido = GeometriaPerfis.Extrudar(f, a, c, b.Rot + b.RotPerfil, Mm),
                });
            }
            return r;
        }

        // ================= DWG / DXF =================

        // ponytail: vírgula também vira _ (o DXF com vírgula no nome da camada perde a camada no ezdxf)
        public static string CamadaDe(Peca p) => (p.Solido.Concreto ? "CONCRETO_" : "ACO_") + Regex.Replace(p.Secao.Length == 0 ? "SEM_SECAO" : p.Secao, "[<>/\\\\\":;?*|=`,]", "_");

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
                doc.Entities.Add(new Line(new XYZ(p.A.X, p.A.Y, p.A.Z), new XYZ(p.B.X, p.B.Y, p.B.Z)) { Layer = analitica });
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
                Face(p.Solido.TampaI); Face(p.Solido.TampaJ);
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
        public static string Rodar(string pasta, string raiz)
        {
            var sb = new StringBuilder(); int falhas = 0;
            void Ok(string nome, bool c) { sb.AppendLine((c ? "ok    " : "FALHA ") + nome); if (!c) falhas++; }
            Directory.CreateDirectory(pasta);
            Render3DGeometria.Testar((c, nome) => Ok("render3d: " + nome, c));   // juntas, extrusão com corte, triangulação
            LigacoesAmostra.Testar((c, nome) => Ok("ligacoes3d: " + nome, c));   // contrato ida e volta, chapa de topo, placa de base

            Modelo m2 = Modelo.DeXml(File.ReadAllText(Path.Combine(raiz, "exemplos", "guincho-coluna-escorada.estructto")));
            var guincho = Modelo3D.De(m2, null);
            var esp = new DocumentoEspacial { Json = JsonNode.Parse(File.ReadAllText(Path.Combine(raiz, "codigo", "motor", "exemplos", "edificio_concreto.json"))).AsObject() };
            var edificio = esp.ParaVista();
            Perfil3D PerfilEsp(string nome) => esp.Json["secoes"]?[nome] is JsonObject s ? Perfil3D.DoMotor(s) : null;
            Perfil3D Perfil2D(string nome) => Perfil3D.DoCatalogo(Biblioteca.AcharSecao(nome));

            foreach (var (nome, mod, perfil) in new[] { ("guincho", guincho, (Func<string, Perfil3D>)Perfil2D), ("edificio", edificio, PerfilEsp) })
            {
                var pecas = Exportador.Pecas(mod, perfil);
                Ok(nome + ": uma peça por barra (" + pecas.Count + ")", pecas.Count == mod.Barras.Count && pecas.Count > 0);
                Ok(nome + ": sólidos fechados e orientados", pecas.All(p => Exportador.Fechado(p.Solido)));
                var camadas = new HashSet<string>(pecas.Select(Exportador.CamadaDe)) { Exportador.CamadaAnalitica };
                double x0 = mod.Nos.Min(n => n.X) * 1000, x1 = mod.Nos.Max(n => n.X) * 1000, y0 = mod.Nos.Min(n => n.Y) * 1000, y1 = mod.Nos.Max(n => n.Y) * 1000, z0 = mod.Nos.Min(n => n.Z) * 1000, z1 = mod.Nos.Max(n => n.Z) * 1000;
                double folga = pecas.SelectMany(p => p.Solido.TampaI.SelectMany(q => q).Select(q => (q - p.A).Length).Concat(p.Solido.TampaJ.SelectMany(q => q).Select(q => (q - p.B).Length))).Max() + 1e-6;

                foreach (bool dxf in new[] { false, true })
                {
                    string arq = Path.Combine(pasta, nome + (dxf ? ".dxf" : ".dwg"));
                    Exportador.Cad(arq, pecas, dxf);
                    CadDocument d = dxf ? DxfReader.Read(arq) : DwgReader.Read(arq);
                    string t = nome + (dxf ? " DXF" : " DWG");
                    var linhas = d.Entities.OfType<Line>().ToList(); var malhas = d.Entities.OfType<PolyfaceMesh>().ToList();
                    Ok(t + ": " + linhas.Count + " linhas e " + malhas.Count + " malhas = barras", linhas.Count == pecas.Count && malhas.Count == pecas.Count);
                    Ok(t + ": linhas na camada " + Exportador.CamadaAnalitica, linhas.All(l => l.Layer?.Name == Exportador.CamadaAnalitica));
                    var lidas = new HashSet<string>(d.Layers.Select(l => l.Name));
                    Ok(t + ": " + camadas.Count + " camadas (seção/tipo + analítica)", camadas.All(lidas.Contains) && malhas.Select(p => p.Layer?.Name).Distinct().Count() == camadas.Count - 1);
                    Ok(t + ": faces das malhas = " + pecas.Sum(p => p.Solido.Lados.Count + p.Solido.Triangulos.Count), malhas.Sum(p => p.Faces.Count) == pecas.Sum(p => p.Solido.Lados.Count + p.Solido.Triangulos.Count));
                    Ok(t + ": unidades mm, versão AC1032", d.Header.InsUnits == ACadSharp.Types.Units.UnitsType.Millimeters && d.Header.Version == ACadVersion.AC1032);
                    var pl = linhas.SelectMany(l => new[] { l.StartPoint, l.EndPoint }).ToList();
                    bool Perto(double a, double b) => Math.Abs(a - b) < 1e-6;
                    Ok(t + ": caixa das linhas = caixa dos nós (mm)", Perto(pl.Min(p => p.X), x0) && Perto(pl.Max(p => p.X), x1) && Perto(pl.Min(p => p.Y), y0) && Perto(pl.Max(p => p.Y), y1) && Perto(pl.Min(p => p.Z), z0) && Perto(pl.Max(p => p.Z), z1));
                    var pm = malhas.SelectMany(p => p.Vertices.Select(v => v.Location)).ToList();
                    Ok(t + ": caixa das malhas = caixa dos nós ± meia seção (" + folga.ToString("0.#", CultureInfo.InvariantCulture) + " mm)",
                        pm.Min(p => p.X) >= x0 - folga && pm.Max(p => p.X) <= x1 + folga && pm.Min(p => p.Y) >= y0 - folga && pm.Max(p => p.Y) <= y1 + folga && pm.Min(p => p.Z) >= z0 - folga && pm.Max(p => p.Z) <= z1 + folga &&
                        pm.Min(p => p.X) <= x0 + 1e-6 && pm.Max(p => p.X) >= x1 - 1e-6 && pm.Min(p => p.Y) <= y0 + 1e-6 && pm.Max(p => p.Y) >= y1 - 1e-6 && pm.Min(p => p.Z) <= z0 + 1e-6 && pm.Max(p => p.Z) >= z1 - 1e-6);
                }

                string stp = Exportador.Step(pecas, nome, nome + ".stp");
                File.WriteAllText(Path.Combine(pasta, nome + ".stp"), stp, new UTF8Encoding(false));
                var erros = new List<string>();
                int nEnt = Exportador.ConferirStep(stp, erros);
                // entidades esperadas: 13 de contexto + 7 da montagem; por peça: pontos + 2/laço + 5/face + 13
                int esperado = 13 + 7 + pecas.Sum(p =>
                {
                    var lacos = p.Solido.Lados.Concat(p.Solido.TampaI).Concat(p.Solido.TampaJ).ToList();
                    return lacos.SelectMany(l => l).Distinct().Count() + 2 * lacos.Count + 5 * (p.Solido.Lados.Count + 2) + 4 + 5 + 1 + 4;
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
            sp.Children.Add(new TextBlock { Text = "Exportar o modelo 3D", FontSize = 22, FontWeight = FontWeights.Light, Margin = new Thickness(0, 0, 0, 14) });
            foreach (var (fmt, dica) in new[] { ("DWG", "AutoCAD 2018 (AC1032): uma malha por barra na camada da seção + eixos analíticos, em mm."), ("DXF", "O mesmo conteúdo do DWG em DXF (texto)."), ("STEP", "STEP AP214: uma peça (sólido de faces planas) por barra, numa montagem, em mm.") })
            {
                var b = new F.Button { Header = fmt + "...", ToolTip = dica, Margin = new Thickness(0, 0, 0, 6), HorizontalAlignment = HorizontalAlignment.Left };
                b.Click += (s, e) => { Bastidor.IsOpen = false; ExportarModelo(fmt); };
                sp.Children.Add(b);
                sp.Children.Add(new TextBlock { Text = dica, Foreground = TemaPlant.BTexto2, Margin = new Thickness(0, 0, 0, 12), TextWrapping = TextWrapping.Wrap });
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
