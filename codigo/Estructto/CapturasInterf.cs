// Bateria de interferência das ligações locais (LigacoesAmostra) no Realista, sem motor: centenas de cenas pequenas, uma
// por combinação de nó — pilar (perfil, giro) × vigas (perfil, cardinal, quais lados) × topo/meio; diagonal (perfil, giro,
// ângulo, lado) chegando no meio do pilar ou no nó viga-pilar; pórtico 2x2 contraventado. Para cada cena: sólidos
// (Render3D.Coleta), pares que se interpenetram > 1 mm (Render3DGeometria.Interferencias). interf3d.txt com tudo; captura
// (iso e trás) só das cenas com par. Sai com o nº de cenas reprovadas (0 = ok). Só elas: ESTRUCTTO_CAPTURAS=interf3d
// (ESTRUCTTO_INTERF=<trecho do nome> roda só as cenas que o contêm).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Media.Media3D;

namespace NovoFtool
{
    public static partial class Capturas
    {
        static readonly bool[] Engaste = { true, true, true, true, true, true }, Rotula = { true, true, true, false, false, false };

        // Pilar vertical no nó 2 (z = 3) com vigas horizontais nas direções dadas (graus no plano); topo: o pilar termina no nó.
        static Modelo3D CenaCruz(string pilar, double rotP, string viga, int card, bool topo, IEnumerable<double> dirs, string vigaY = null, int cardY = 0)
        {
            var m = new Modelo3D();
            m.Nos.Add(new No3D { Id = 1, X = 0, Y = 0, Z = 0, Apoios = Engaste });
            m.Nos.Add(new No3D { Id = 2, X = 0, Y = 0, Z = 3 });
            m.Barras.Add(new Barra3D { Id = 1, Ni = 1, Nj = 2, Secao = pilar, Rot = rotP });
            if (!topo) { m.Nos.Add(new No3D { Id = 3, X = 0, Y = 0, Z = 6 }); m.Barras.Add(new Barra3D { Id = 2, Ni = 2, Nj = 3, Secao = pilar, Rot = rotP }); }
            int id = 10;
            foreach (double a in dirs)
            {
                double r = a * Math.PI / 180;
                m.Nos.Add(new No3D { Id = id, X = Math.Round(4 * Math.Cos(r), 6), Y = Math.Round(4 * Math.Sin(r), 6), Z = 3, Apoios = Rotula });
                bool emY = Math.Abs(Math.Sin(r)) > 0.5 && vigaY != null;   // vigas em ±y com outro perfil/cardinal (alturas misturadas no nó)
                m.Barras.Add(new Barra3D { Id = id, Ni = 2, Nj = id, Secao = emY ? vigaY : viga, Cardinal = emY && cardY > 0 ? cardY : card });
                id++;
            }
            return m;
        }

        // Diagonal no plano xz chegando ao nó 2 (z = 2,5) de um pilar contínuo; dx = avanço horizontal (sinal = lado);
        // viga: com viga horizontal no nó (gusset viga-pilar).
        static Modelo3D CenaDiagonal(string pilar, double rotP, string diag, double rotD, double dx, double dz, string viga)
        {
            var m = new Modelo3D();
            m.Nos.Add(new No3D { Id = 1, X = 0, Y = 0, Z = 0, Apoios = Engaste });
            m.Nos.Add(new No3D { Id = 2, X = 0, Y = 0, Z = 2.5 }); m.Nos.Add(new No3D { Id = 3, X = 0, Y = 0, Z = 5 });
            m.Nos.Add(new No3D { Id = 4, X = dx, Y = 0, Z = 2.5 - dz, Apoios = Rotula });
            m.Barras.Add(new Barra3D { Id = 1, Ni = 1, Nj = 2, Secao = pilar, Rot = rotP });
            m.Barras.Add(new Barra3D { Id = 2, Ni = 2, Nj = 3, Secao = pilar, Rot = rotP });
            m.Barras.Add(new Barra3D { Id = 3, Ni = 4, Nj = 2, Secao = diag, Rot = rotD });
            if (viga != null) { m.Nos.Add(new No3D { Id = 5, X = 4 * Math.Sign(dx), Y = 0, Z = 2.5, Apoios = Rotula }); m.Barras.Add(new Barra3D { Id = 4, Ni = 2, Nj = 5, Secao = viga, Cardinal = 8 }); }
            return m;
        }

        static List<(string nome, Modelo3D m, Point3D alvo)> CenasInterf()
        {
            var c = new List<(string, Modelo3D, Point3D)>();
            string N(string s) => s.Replace(" ", "").Replace("\"", "").Replace("/", "-").Replace(",", ".");
            const string U8 = "U 8\" x 17,1";
            var pilares = new[] { "W 250 x 32,7", "W 200 x 15,0", "W 310 x 38,7", "HP 250 x 85,0", "Tubo 100x50x3,00", "Tubo 150x150x2,65", U8 };
            var vigas = new[] { "W 310 x 38,7", "W 200 x 15,0", "W 410 x 46,1", U8, "Tubo 100x50x3,00" };
            var lados = new (string n, double[] d)[] { ("4", new[] { 0.0, 90, 180, 270 }), ("mesas", new[] { 0.0, 180 }), ("almas", new[] { 90.0, 270 }), ("1x", new[] { 0.0 }), ("1y", new[] { 90.0 }), ("L", new[] { 0.0, 90 }), ("3", new[] { 0.0, 90, 180 }) };
            foreach (string p in pilares)
                foreach (double rp in new[] { 0.0, 90 })
                    foreach (string vg in vigas)
                        foreach (int card in new[] { 5, 8 })
                            foreach (bool topo in new[] { true, false })
                                foreach (var (ln, ds) in lados)
                                {
                                    if (vg != vigas[0] && ln != "4") continue;   // ponytail: perfis de viga extras só com 4 vigas
                                    c.Add(($"cruz {N(p)} r{rp} {N(vg)} c{card} {(topo ? "topo" : "meio")} {ln}", CenaCruz(p, rp, vg, card, topo, ds), new Point3D(0, 0, 3)));
                                }
            foreach (string p in new[] { "W 250 x 32,7", "W 310 x 38,7", "HP 250 x 85,0" })
                foreach (double rp in new[] { 0.0, 90 })
                    foreach (string vx in vigas) foreach (string vy in vigas)
                        foreach (var (cx, cy) in new[] { (8, 8), (5, 5), (8, 5), (5, 8), (8, 2) })
                            foreach (bool topo in new[] { true, false })
                            {
                                if (vx == vy && cx == cy) continue;   // já nas cenas de cima
                                c.Add(($"mista {N(p)} r{rp} {N(vx)} c{cx} / {N(vy)} c{cy} {(topo ? "topo" : "meio")}", CenaCruz(p, rp, vx, cx, topo, new[] { 0.0, 90, 180, 270 }, vy, cy), new Point3D(0, 0, 3)));
                            }
            var diags = new[] { "L 3\" x 1/4\"", "W 310 x 38,7", "W 200 x 15,0", U8, "Tubo 100x50x3,00", "Tubo Ø76,2x2,65" };
            foreach (string p in new[] { "W 250 x 32,7", "W 310 x 38,7", "Tubo 100x50x3,00", "Tubo 150x150x2,65" })
                foreach (double rp in new[] { 0.0, 90 })
                    foreach (string dg in diags)
                        foreach (double rd in new[] { 0.0, 90 })
                            foreach (var (dx, dz) in new[] { (2.5, 2.5), (-2.5, 2.5), (4.0, 2.5), (2.0, 2.5), (2.5, -2.5) })
                                foreach (string vg in new[] { null, "W 310 x 38,7" })
                                    c.Add(($"diag {N(p)} r{rp} {N(dg)} r{rd} {dx}x{dz}{(vg == null ? "" : " +viga")}", CenaDiagonal(p, rp, dg, rd, dx, dz, vg), new Point3D(0, 0, 2.5)));
            foreach (var (_, m, _) in c)   // seção fora do catálogo vira perfil genérico e a cena não testa nada (o "UPN 200" das capturas antigas)
                foreach (Barra3D b in m.Barras) if (Portico.Biblioteca.AcharSecao(b.Secao) == null) throw new InvalidOperationException("seção fora do catálogo: " + b.Secao);
            c.Add(("portico2x2-cv", Portico2x2(true), new Point3D(6, 0, 3.5)));
            c.Add(("portico2x2", Portico2x2(false), new Point3D(6, 0, 3.5)));
            return c;
        }

        static string CaixaMm(MalhaDx m)
        {
            string F(float x) => (x * 1000).ToString("0", CultureInfo.InvariantCulture);
            return "x " + F(m.P.Min(q => q.X)) + ".." + F(m.P.Max(q => q.X)) + "  y " + F(m.P.Min(q => q.Y)) + ".." + F(m.P.Max(q => q.Y)) + "  z " + F(m.P.Min(q => q.Z)) + ".." + F(m.P.Max(q => q.Z));
        }

        static int CapturasInterf3D(MainWindow w)
        {
            w.DefinirRealista(true);
            w.Faixa.SelectedTabItem = w.Faixa.Tabs.First(t => (t.Header as string) == "Vista");
            w.MostrarDiagrama(Diagrama.Nenhum);
            w.MostrarVista3D(true);
            EsperarIdle();
            var v = w.vista3D; v.LigacoesAmostra = true; v.LimparSelecao(); v.Escolher(null);
            var sb = new StringBuilder(); var resumo = new StringBuilder(); var modelo = new StringBuilder(); int falhas = 0, n = 0, capt = 0;
            string filtro = Environment.GetEnvironmentVariable("ESTRUCTTO_INTERF");
            foreach (var (nome, m, alvo) in CenasInterf().Where(x => string.IsNullOrEmpty(filtro) || x.nome.Contains(filtro)))
            {
                n++;
                try
                {
                    v.Carregar(m, false); EsperarIdle();
                    Render3D.Coleta = new List<Render3D.Peca3D>();
                    v.DefinirLigacoes(v.ModeloAtual.Ligacoes); EsperarIdle();
                    var col = Render3D.Coleta.Where(x => !x.Excluir).ToList(); Render3D.Coleta = null;
                    var r = Render3DGeometria.Interferencias(col.Select(x => x.M).ToList()).Where(x => col[x.A].Barra == null || col[x.A].Barra != col[x.B].Barra).ToList();
                    // barras sem nó em comum que se cruzam (X de contraventamento sem nó no meio): choque do modelo, não de ligação; à parte
                    bool Modelo((int A, int B, double Prof) x) => col[x.A].Barra is Barra3D ba && col[x.B].Barra is Barra3D bb && !new[] { ba.Ni, ba.Nj }.Intersect(new[] { bb.Ni, bb.Nj }).Any();
                    foreach (var x in r.Where(Modelo)) modelo.AppendLine(nome + ": " + (x.Prof * 1000).ToString("0.0", CultureInfo.InvariantCulture) + " mm  " + col[x.A].Nome + "  ×  " + col[x.B].Nome);
                    r = r.Where(x => !Modelo(x)).ToList();
                    if (r.Count == 0 && string.IsNullOrEmpty(filtro)) continue;   // com filtro captura também as aprovadas (conferência visual)
                    if (r.Count > 0) falhas++;
                    if (r.Count > 0) resumo.AppendLine(nome + ": " + r.Count + " pares, pior " + (r.Max(x => x.Prof) * 1000).ToString("0.0", CultureInfo.InvariantCulture) + " mm");
                    sb.AppendLine(nome + ": " + col.Count + " sólidos (" + col.Count(x => x.Tipo == "chapa") + " chapas), " + r.Count + " pares");
                    foreach (var (a, b, p) in r.OrderByDescending(x => x.Prof))
                    {
                        sb.AppendLine("  " + (p * 1000).ToString("0.0", CultureInfo.InvariantCulture) + " mm  " + col[a].Nome + "  ×  " + col[b].Nome);
                        if (!string.IsNullOrEmpty(filtro)) foreach (int k in new[] { a, b }) sb.AppendLine("      " + CaixaMm(col[k].M));   // diagnóstico: caixa (mm) de cada sólido
                    }
                    if (capt++ < 60)
                    {
                        string arq = "62-interf-" + new string(nome.Select(ch => char.IsLetterOrDigit(ch) || ch == '.' || ch == '-' ? ch : '_').ToArray());
                        double dist = nome.StartsWith("portico") ? 15 : 0.9;
                        v.Focar(alvo, dist, -40, 22); CapturarPrincipal(w, arq);
                        v.Focar(alvo, dist, 140, 22); CapturarPrincipal(w, arq + "-tras");
                        if (!string.IsNullOrEmpty(filtro)) { v.Focar(alvo, dist * 1.8, -90, 5); CapturarPrincipal(w, arq + "-frente"); v.Focar(alvo, dist * 1.8, -40, 22); CapturarPrincipal(w, arq + "-longe"); }
                    }
                }
                catch (Exception ex) { falhas++; resumo.AppendLine(nome + ": ERRO " + ex.Message); Render3D.Coleta = null; }
            }
            File.WriteAllText(Path.Combine(pastaSaida, "interf3d.txt"), "cenas " + n + " · reprovadas " + falhas + "\r\n\r\n" + resumo + "\r\n" + sb + "\r\nchoques do modelo (barras sem nó em comum; não reprovam):\r\n" + modelo);
            return falhas;
        }
    }
}
