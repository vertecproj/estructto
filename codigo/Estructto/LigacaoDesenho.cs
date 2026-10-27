// Desenho da ligação a partir da `geometria` do contrato (docs/v3/LIGACOES-CONTRATO.md, docs/v3/LIGACOES-UI.md):
// GeoLig = peças como prismas (chapa = contorno × espessura; cantoneira = duas abas; perfis = caixas e alma com recortes),
// parafusos e cordões de solda, no referencial local da ligação (mm). DesenhoLigacao projeta numa vista ortogonal
// (Elevação, Planta, Lateral: contorno, hachura da espessura, parafuso com cruz, símbolo de solda, cotas de passo,
// gabarito e bordas; tubo com geratrizes, bordas e a curva da sela) ou em 3D sólido com z-buffer (LigacaoDesenho3D.cs), e anima
// a troca de geometria (Anim.Valor na propriedade Prog).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Portico;
using static NovoFtool.UiAnalise;

namespace NovoFtool
{
    // Cada peça desenhada carrega Tag = campo editável que o MOTOR pôs na peça do contrato (`campo`; ligacoes_campos.py).

    public partial class GeoLig
    {
        // Camada: 0 perfil ligado, 1 chapa, 2 cantoneira. Grupo de falha: viga, suporte, chapas, cantoneiras.
        public class Prisma { public string Nome = "", Grupo = ""; public Point3D[] Base; public Vector3D N; public double T; public int Camada; public bool Falha; public string Tag = ""; }
        // Ref: aresta da chapa mais próxima (Ligacao3D.ArestaMaisPerto), para o sextavado do 3D ficar alinhado a ela
        public class Paraf { public Point3D O; public Vector3D E; public double D, L, Pega; public bool Falha; public string Tag = ""; public Vector3D? Ref; }
        // Ax/Lados/Face: onde o cordão assenta (Ligacao3D.Apoio), para o prisma do filete no 3D; Ax null = ao longo da barra que chega
        public class Cordao { public Point3D[] P; public double Perna; public bool Falha; public string Tag = ""; public Vector3D? Ax; public List<(Vector3D n, double desl)> Lados; public Ligacao3D.Chapa Face; }
        // Tubo redondo (contrato "tubos"): de O até O + E·L, diâmetro externo D, parede T. Corte (boca de peixe): só há material
        // onde a distância ao eixo (CO, CE) é ≥ CR.
        public class Tubo
        {
            public string Nome = "", Grupo = "", Tag = ""; public Point3D O; public Vector3D E; public double L, D, T; public bool Falha;
            public bool Corta; public Point3D CO; public Vector3D CE; public double CR;
            public (Vector3D u, Vector3D w) Base()
            {
                Vector3D u = Vector3D.CrossProduct(E, Math.Abs(E.Z) > 0.9 ? new Vector3D(1, 0, 0) : new Vector3D(0, 0, 1)); u.Normalize();
                return (u, Vector3D.CrossProduct(E, u));   // (u, w, E) direito
            }
            // onde começa o material na geratriz que passa por p (p na seção s = 0): saída do cilindro de corte (maior raiz), ou 0.
            // Mesma conta do Render3DGeometria.Extrudar (Corte.Raio).
            public double Inicio(Point3D p)
            {
                if (!Corta || CR <= 0) return 0;
                Vector3D A = CE; A.Normalize(); Vector3D w = p - CO;
                Vector3D wp = w - Vector3D.DotProduct(w, A) * A, ep = E - Vector3D.DotProduct(E, A) * A;
                double qa = ep.LengthSquared, qb = 2 * Vector3D.DotProduct(wp, ep), qc = wp.LengthSquared - CR * CR, disc = qb * qb - 4 * qa * qc;
                return qa > 1e-12 && disc >= 0 ? Math.Max(0, (Math.Sqrt(disc) - qb) / (2 * qa)) : 0;
            }
        }

        public readonly List<Prisma> Pecas = new List<Prisma>();
        public readonly List<Paraf> Parafs = new List<Paraf>();
        public readonly List<Cordao> Soldas = new List<Cordao>();
        public readonly List<Tubo> Tubos = new List<Tubo>();
        public bool Parede; public double XParede;          // apoio genérico (tipo sem perfil de apoio): face x = XParede
        public string NomeViga = "", NomeSuporte = "";

        static Point3D P3(JsonNode a) => a is JsonArray v && v.Count >= 3 ? new Point3D(D(v[0]), D(v[1]), D(v[2])) : new Point3D();
        static Vector3D V3(JsonNode a, Vector3D pad) => a is JsonArray v && v.Count >= 3 ? new Vector3D(D(v[0]), D(v[1]), D(v[2])) : pad;
        static bool Ok(double x) => !double.IsNaN(x) && !double.IsInfinity(x);

        public static Prisma Caixa(double x0, double x1, double y0, double y1, double z0, double z1, int camada, string grupo, string nome = "")
        {
            double zm = (z0 + z1) / 2;
            return new Prisma
            {
                Base = new[] { new Point3D(x0, y0, zm), new Point3D(x1, y0, zm), new Point3D(x1, y1, zm), new Point3D(x0, y1, zm) },
                N = new Vector3D(0, 0, 1), T = Math.Abs(z1 - z0), Camada = camada, Grupo = grupo, Nome = nome
            };
        }

        // falhas: grupos reprovados (parafusos, soldas, chapas, cantoneiras, viga, suporte)
        public static GeoLig De(JsonNode g, ICollection<string> falhas = null)
        {
            var r = new GeoLig();
            if (g == null) return r;
            bool F(string k) => falhas != null && falhas.Contains(k);
            foreach (var c in Lista(g["chapas"]))
            {
                var o = P3(c["origem"]); var n = V3(c["normal"], new Vector3D(0, 1, 0)); var u = V3(c["u"], new Vector3D(1, 0, 0));
                n.Normalize(); u.Normalize();
                var v = c["v"] is JsonArray ? V3(c["v"], new Vector3D()) : Vector3D.CrossProduct(n, u);
                // W8: `origem` é uma face (G6, LIGACOES-CONTRATO.md); a base do Prisma é o plano médio → + n·t/2
                var pts = Lista(c["contorno"]).OfType<JsonArray>().Where(p => p.Count >= 2).Select(p => o + n * (D(c, "espessura") / 2) + D(p[0]) * u + D(p[1]) * v).ToArray();
                double t = D(c, "espessura");
                if (pts.Length >= 3 && Ok(t)) r.Pecas.Add(new Prisma { Base = pts, N = n, T = t, Camada = 1, Grupo = "chapas", Nome = S(c, "nome"), Falha = F("chapas"), Tag = S(c, "campo") });
            }
            LerW8(r, g, F);   // W8-CONTRAV: perfis das barras (perfis_barra) e linhas (Whitmore, cotas)
            foreach (var c in Lista(g["cantoneiras"]))
            {
                var o = P3(c["origem"]); var e = V3(c["eixo"], new Vector3D(0, 0, 1)); var u = V3(c["u"], new Vector3D(1, 0, 0));
                e.Normalize(); u.Normalize();
                var w = c["w"] is JsonArray ? V3(c["w"], new Vector3D()) : Vector3D.CrossProduct(e, u);
                w.Normalize();
                double L = D(c, "comprimento"), t = D(c, "espessura"), b1 = D(c["abas"]?[0]), b2 = D(c["abas"]?[1]);
                var m = Regex.Match(S(c, "perfil"), @"L\s*(\d+(?:[.,]\d+)?)\s*[xX]\s*(\d+(?:[.,]\d+)?)(?:\s*[xX]\s*(\d+(?:[.,]\d+)?))?");
                double Num(string s) => double.Parse(s.Replace(',', '.'), CultureInfo.InvariantCulture);
                if (m.Success && (!Ok(b1) || !Ok(t)))
                {
                    if (m.Groups[3].Success) { b1 = Num(m.Groups[1].Value); b2 = Num(m.Groups[2].Value); t = Num(m.Groups[3].Value); }
                    else { b1 = b2 = Num(m.Groups[1].Value); t = Num(m.Groups[2].Value); }
                }
                if (!Ok(b2)) b2 = b1;
                if (!Ok(L) || !Ok(b1) || !Ok(t)) continue;
                var o1 = o + w * t / 2; var o2 = o + u * t / 2;
                r.Pecas.Add(new Prisma { Base = new[] { o1, o1 + u * b1, o1 + u * b1 + e * L, o1 + e * L }, N = w, T = t, Camada = 2, Grupo = "cantoneiras", Nome = S(c, "perfil"), Falha = F("cantoneiras"), Tag = S(c, "campo") });
                r.Pecas.Add(new Prisma { Base = new[] { o2, o2 + w * b2, o2 + w * b2 + e * L, o2 + e * L }, N = u, T = t, Camada = 2, Grupo = "cantoneiras", Nome = S(c, "perfil"), Falha = F("cantoneiras"), Tag = S(c, "campo") });
            }
            foreach (var p in Lista(g["parafusos"]).Concat(Lista(g["porcas"])))
            {
                double d = D(p, "d"); if (!Ok(d)) d = D(p, "diametro");
                var e = V3(p["eixo"], new Vector3D(0, -1, 0)); e.Normalize();
                double L = D(p, "comprimento"); if (!Ok(L)) L = 1.2 * d;
                double pega = D(p, "pega"); if (!Ok(pega)) pega = Math.Max(d * 0.5, L - 1.2 * d);
                if (Ok(d) && p["origem"] is JsonArray) r.Parafs.Add(new Paraf { O = P3(p["origem"]), E = e, D = d, L = L, Pega = pega, Falha = F("parafusos"), Tag = S(p, "campo") });
            }
            foreach (var s in Lista(g["soldas"]))
            {
                var pts = Lista(s["pontos"]).OfType<JsonArray>().Select(P3).ToArray();
                if (pts.Length >= 2) r.Soldas.Add(new Cordao { P = pts, Perna = Ok(D(s, "perna")) ? D(s, "perna") : 5, Falha = F("soldas"), Tag = S(s, "campo") });
            }
            foreach (var t in Lista(g["tubos"]))
            {
                var e = V3(t["eixo"], new Vector3D(1, 0, 0)); if (e.Length < 1e-9) continue; e.Normalize();
                double L = D(t, "comprimento"), d = D(t, "d"), esp = D(t, "t");
                if (!Ok(L) || !Ok(d) || L <= 0 || d <= 0 || t["origem"] is not JsonArray) continue;
                if (!Ok(esp) || esp <= 0 || esp > d / 2) esp = d / 2;   // sem parede: maciço
                var tb = new Tubo { Nome = S(t, "nome"), Grupo = S(t, "grupo"), Tag = S(t, "campo"), O = P3(t["origem"]), E = e, L = L, D = d, T = esp };
                tb.Falha = F(tb.Grupo == "suporte" ? "suporte" : "viga");
                var c = t["corte"];
                if (c is JsonObject && c["origem"] is JsonArray && Ok(D(c, "r")) && D(c, "r") > 0)
                {
                    tb.CO = P3(c["origem"]); tb.CE = V3(c["eixo"], new Vector3D(0, 0, 1)); tb.CR = D(c, "r");
                    tb.Corta = tb.CE.Length > 1e-9;
                }
                r.Tubos.Add(tb);
            }
            // assentamento do 3D (mesmas regras do Render3D principal): aresta da chapa para o sextavado, apoio do cordão
            Ligacao3D lig = null;
            try { lig = Ligacao3D.DoJson(0, 0, g as JsonObject); } catch { }
            if (lig != null)
            {
                foreach (var b in r.Parafs) b.Ref = lig.ArestaMaisPerto(new Ligacao3D.Parafuso { Origem = (Vector3D)b.O, Eixo = b.E, D = b.D });
                foreach (var s in r.Soldas)
                {
                    var (ax, lados, face) = Ligacao3D.Apoio(lig, new Ligacao3D.Solda { Perna = s.Perna, Pontos = s.P.Select(q => (Vector3D)q).ToArray() });
                    s.Ax = ax; s.Lados = lados; s.Face = face;
                }
            }
            var ramal = r.Tubos.FirstOrDefault(q => q.Grupo != "suporte");
            if (ramal != null) foreach (var s in r.Soldas) if (s.Ax == null) s.Ax = ramal.E;   // sela: uma perna ao longo do ramal
            // perfis ligados (projeto.verificar_ligacao → geometria.membros) posicionados pelo contrato
            double xPonta = double.NaN, cs = 0, hs = 0, ci = 0, hi = 0;
            foreach (var rc in Lista(g["recortes"]))
            {
                var pr = rc["parametros"];
                if (S(rc, "tipo") == "recuo") xPonta = D(pr, "x_ponta");
                else if (S(rc, "tipo") == "cope")
                {
                    if (S(pr, "posicao") == "base") { hi = D(pr, "altura"); ci = D(pr, "comprimento"); }
                    else { hs = D(pr, "altura"); cs = D(pr, "comprimento"); }
                    if (Ok(D(pr, "x_inicio")) && !Ok(xPonta)) xPonta = D(pr, "x_inicio");
                }
            }
            var pecasLig = r.Pecas.ToList();
            double xMinLig = pecasLig.Count > 0 ? pecasLig.SelectMany(q => q.Base).Min(q => q.X) : 0;
            double xMaxLig = pecasLig.Count > 0 ? pecasLig.SelectMany(q => q.Base).Max(q => q.X) : 200;
            double zMinLig = pecasLig.Count > 0 ? pecasLig.SelectMany(q => q.Base).Min(q => q.Z) : -100;
            double zMaxLig = pecasLig.Count > 0 ? pecasLig.SelectMany(q => q.Base).Max(q => q.Z) : 100;
            JsonNode viga = Lista(g["membros"]).FirstOrDefault(m => S(m, "barra") == "viga");
            double dv = D(viga, "d");
            foreach (var mb in Lista(g["membros"]))
            {
                if (mb["pecas"] is JsonArray pcs)   // W8 (perfil_tubo_soldado): chapas do perfil prontas no motor (plano médio, ponta na sela)
                {
                    string gp = S(mb, "barra") == "viga" ? "viga" : "suporte";
                    if (gp == "viga") r.NomeViga = S(mb, "perfil"); else r.NomeSuporte = S(mb, "perfil");
                    foreach (var pc in pcs)
                    {
                        var bs = Lista(pc["base"]).OfType<JsonArray>().Select(P3).ToArray(); var n = V3(pc["normal"], new Vector3D(0, 0, 1)); double t = D(pc, "espessura");
                        if (bs.Length >= 3 && n.Length > 1e-9 && Ok(t) && t > 0) { n.Normalize(); r.Pecas.Add(new Prisma { Base = bs, N = n, T = t, Camada = 0, Grupo = gp, Nome = S(mb, "perfil"), Falha = F(gp), Tag = S(mb, "campo") }); }
                    }
                    continue;
                }
                double d = D(mb, "d"), bf = D(mb, "bf"), tw = D(mb, "tw"), tf = D(mb, "tf");
                if (!Ok(d) || !Ok(bf) || !Ok(tw) || !Ok(tf)) continue;
                string papel = S(mb, "barra");
                string tgm = S(mb, "campo");   // campo do perfil no padrão (motor); sem campo (viga esquemática) = não editável
                // G5 (docs/v3/LIGACOES-U.md): U = alma + 2 mesas que saem das costas da alma para um lado só. ab = lado das mesas.
                bool ehU = S(mb, "forma") == "U";
                double ab = ehU && D(mb, "abertura_y") < 0 ? -1 : 1, xg = ehU && Ok(D(mb, "xg")) ? D(mb, "xg") : 0;
                bool passante = Lista(g["membros"]).Count() == 1 && xMinLig < -1;   // emenda: a barra passa pelo nó (x < 0 e x > 0)
                if (papel == "viga" || passante)
                {
                    r.NomeViga = S(mb, "perfil");
                    double x0 = passante ? xMinLig - 0.55 * d : Ok(xPonta) ? xPonta : xMinLig, x1 = Math.Max(x0 + 1.25 * d, xMaxLig + 0.55 * d);
                    bool fv = F("viga");
                    if (!Ok(hs)) hs = 0; if (!Ok(hi)) hi = 0; if (!Ok(cs)) cs = 0; if (!Ok(ci)) ci = 0;
                    var web = new List<Point>();
                    if (hi > 0) { web.Add(new Point(x0, -d / 2 + hi)); web.Add(new Point(x0 + ci, -d / 2 + hi)); web.Add(new Point(x0 + ci, -d / 2 + tf)); }
                    else web.Add(new Point(x0, -d / 2 + tf));
                    web.Add(new Point(x1, -d / 2 + tf)); web.Add(new Point(x1, d / 2 - tf));
                    if (hs > 0) { web.Add(new Point(x0 + cs, d / 2 - tf)); web.Add(new Point(x0 + cs, d / 2 - hs)); web.Add(new Point(x0, d / 2 - hs)); }
                    else web.Add(new Point(x0, d / 2 - tf));
                    // viga U: centroide no eixo, costas da alma em y = −ab·xg (mesmo referencial das chapas do contrato)
                    double yb = -ab * xg, yAlma = ehU ? yb + ab * tw / 2 : 0, yM0 = ehU ? Math.Min(yb, yb + ab * bf) : -bf / 2, yM1 = ehU ? Math.Max(yb, yb + ab * bf) : bf / 2;
                    r.Pecas.Add(new Prisma { Base = web.Select(q => new Point3D(q.X, yAlma, q.Y)).ToArray(), N = new Vector3D(0, 1, 0), T = tw, Camada = 0, Grupo = "viga", Nome = r.NomeViga, Falha = fv, Tag = tgm });
                    r.Pecas.Add(Caixa(x0 + (hs > 0 ? cs : 0), x1, yM0, yM1, d / 2 - tf, d / 2, 0, "viga")); r.Pecas[r.Pecas.Count - 1].Tag = tgm;
                    r.Pecas.Add(Caixa(x0 + (hi > 0 ? ci : 0), x1, yM0, yM1, -d / 2, -d / 2 + tf, 0, "viga")); r.Pecas[r.Pecas.Count - 1].Tag = tgm;
                }
                else
                {
                    r.NomeSuporte = S(mb, "perfil");
                    string apoio = S(mb, "apoio");
                    double ext = Math.Max(Ok(dv) ? 0.95 * dv : 250, (zMaxLig - zMinLig) * 0.9);
                    double z0 = Math.Min(zMinLig, Ok(dv) ? -dv / 2 : zMinLig) - ext * 0.6, z1 = Math.Max(zMaxLig, Ok(dv) ? dv / 2 : zMaxLig) + ext * 0.6;
                    var lst = new List<Prisma>();
                    // apoio U (G5): o motor mede a face como no I (alma: x_face = tw/2; mesa: largura bf centrada). Alma no eixo com as
                    // costas para a ligação (+x) e as mesas para −x; na mesa, a mesa centrada em y e a alma em y = −ab·bf/2.
                    double mx0 = ehU ? tw / 2 - bf : -bf / 2, mx1 = ehU ? tw / 2 : bf / 2;
                    if (apoio.Contains("viga"))
                    {
                        double zt = Ok(dv) ? dv / 2 : zMaxLig, Ly = Math.Max(2.2 * bf, 380);
                        lst.Add(Caixa(-tw / 2, tw / 2, -Ly, Ly, zt - d + tf, zt - tf, 0, "suporte"));
                        lst.Add(Caixa(mx0, mx1, -Ly, Ly, zt - tf, zt, 0, "suporte"));
                        lst.Add(Caixa(mx0, mx1, -Ly, Ly, zt - d, zt - d + tf, 0, "suporte"));
                    }
                    else if (apoio.Contains("alma"))
                    {
                        lst.Add(Caixa(-tw / 2, tw / 2, -d / 2 + tf, d / 2 - tf, z0, z1, 0, "suporte"));
                        lst.Add(Caixa(mx0, mx1, d / 2 - tf, d / 2, z0, z1, 0, "suporte"));
                        lst.Add(Caixa(mx0, mx1, -d / 2, -d / 2 + tf, z0, z1, 0, "suporte"));
                    }
                    else
                    {
                        double ya = ehU ? -ab * (bf - tw) / 2 : 0;   // plano médio da alma do U
                        lst.Add(Caixa(-d / 2 + tf, d / 2 - tf, ya - tw / 2, ya + tw / 2, z0, z1, 0, "suporte"));
                        lst.Add(Caixa(d / 2 - tf, d / 2, -bf / 2, bf / 2, z0, z1, 0, "suporte"));
                        lst.Add(Caixa(-d / 2, -d / 2 + tf, -bf / 2, bf / 2, z0, z1, 0, "suporte"));
                    }
                    foreach (var q in lst) { q.Falha = F("suporte"); q.Nome = r.NomeSuporte; q.Tag = tgm; }
                    r.Pecas.InsertRange(0, lst);
                }
            }
            if (!Lista(g["membros"]).Any(m => S(m, "barra") != "viga") && r.Soldas.Any() && pecasLig.Count > 0 && r.Pecas.Any(q => q.Grupo == "viga"))
            {
                r.Parede = true; r.XParede = xMinLig;   // tipo antigo: apoio sem perfil
            }
            return r;
        }

        // Placa de base (miniatura da biblioteca): pilar W 250 sobre chapa 420 × 360 × 19 com 4 chumbadores
        public static GeoLig PlacaBase()
        {
            var r = new GeoLig();
            double d = 253, bf = 254, tw = 8.6, tf = 14.2, H = 600;
            r.Pecas.Add(Caixa(-d / 2 + tf, d / 2 - tf, -tw / 2, tw / 2, 0, H, 0, "suporte"));
            r.Pecas.Add(Caixa(d / 2 - tf, d / 2, -bf / 2, bf / 2, 0, H, 0, "suporte"));
            r.Pecas.Add(Caixa(-d / 2, -d / 2 + tf, -bf / 2, bf / 2, 0, H, 0, "suporte"));
            r.Pecas.Add(Caixa(-210, 210, -180, 180, -19, 0, 1, "chapas"));
            foreach (var (x, y) in new[] { (-165.0, -135.0), (165.0, -135.0), (165.0, 135.0), (-165.0, 135.0) })
                r.Parafs.Add(new Paraf { O = new Point3D(x, y, 30), E = new Vector3D(0, 0, -1), D = 19, L = 160, Pega = 49 });
            return r;
        }

        // interpolação (mesma estrutura) para animar a troca; null = estrutura diferente (o desenho faz fade)
        public static GeoLig Misturar(GeoLig a, GeoLig b, double t)
        {
            if (a == null || b == null || a.Pecas.Count != b.Pecas.Count || a.Parafs.Count != b.Parafs.Count || a.Soldas.Count != b.Soldas.Count || a.Tubos.Count != b.Tubos.Count) return null;
            for (int i = 0; i < a.Pecas.Count; i++) if (a.Pecas[i].Base.Length != b.Pecas[i].Base.Length) return null;
            for (int i = 0; i < a.Soldas.Count; i++) if (a.Soldas[i].P.Length != b.Soldas[i].P.Length) return null;
            Point3D LP(Point3D p, Point3D q) => new Point3D(p.X + (q.X - p.X) * t, p.Y + (q.Y - p.Y) * t, p.Z + (q.Z - p.Z) * t);
            double L(double x, double y) => x + (y - x) * t;
            var r = new GeoLig { Parede = b.Parede, XParede = L(a.XParede, b.XParede), NomeViga = b.NomeViga, NomeSuporte = b.NomeSuporte };
            r.Linhas.AddRange(b.Linhas); r.VistaX = b.VistaX; r.VistaZ = b.VistaZ;
            for (int i = 0; i < b.Pecas.Count; i++)
            {
                var pa = a.Pecas[i]; var pb = b.Pecas[i];
                r.Pecas.Add(new Prisma { Base = pb.Base.Select((q, k) => LP(pa.Base[k], q)).ToArray(), N = pb.N, T = L(pa.T, pb.T), Camada = pb.Camada, Grupo = pb.Grupo, Nome = pb.Nome, Falha = pb.Falha, Tag = pb.Tag });
            }
            for (int i = 0; i < b.Parafs.Count; i++)
            {
                var pa = a.Parafs[i]; var pb = b.Parafs[i];
                r.Parafs.Add(new Paraf { O = LP(pa.O, pb.O), E = pb.E, D = L(pa.D, pb.D), L = L(pa.L, pb.L), Pega = L(pa.Pega, pb.Pega), Falha = pb.Falha, Tag = pb.Tag, Ref = pb.Ref });
            }
            for (int i = 0; i < b.Soldas.Count; i++)
            {
                var sb = b.Soldas[i];
                r.Soldas.Add(new Cordao { P = sb.P.Select((q, k) => LP(a.Soldas[i].P[k], q)).ToArray(), Perna = L(a.Soldas[i].Perna, sb.Perna), Falha = sb.Falha, Tag = sb.Tag, Ax = sb.Ax, Lados = sb.Lados, Face = sb.Face });
            }
            for (int i = 0; i < b.Tubos.Count; i++)
            {
                var ta = a.Tubos[i]; var tb = b.Tubos[i];
                r.Tubos.Add(new Tubo { Nome = tb.Nome, Grupo = tb.Grupo, Tag = tb.Tag, Falha = tb.Falha, O = LP(ta.O, tb.O), E = tb.E, L = L(ta.L, tb.L), D = L(ta.D, tb.D), T = L(ta.T, tb.T),
                                       Corta = tb.Corta, CO = tb.CO, CE = tb.CE, CR = tb.Corta && ta.Corta ? L(ta.CR, tb.CR) : tb.CR });
            }
            return r;
        }

        // trecho do tubo junto ao nó (origem local): a ligação de tubo não tem chapa, o enquadramento vem daqui
        static IEnumerable<Point3D> PertoDoNo(Tubo t)
        {
            double s = Math.Max(0, Math.Min(t.L, Vector3D.DotProduct(new Point3D() - t.O, t.E)));
            var (u, w) = t.Base();
            foreach (double ds in new[] { -t.D, t.D })
            {
                var c = t.O + t.E * Math.Max(0, Math.Min(t.L, s + ds));
                yield return c + u * t.D / 2; yield return c - u * t.D / 2; yield return c + w * t.D / 2; yield return c - w * t.D / 2;
            }
        }

        public IEnumerable<Point3D> Foco()
        {
            foreach (var p in Pecas.Where(q => q.Camada > 0)) foreach (var q in p.Base) yield return q;
            foreach (var b in Parafs) { yield return b.O; yield return b.O + b.E * b.Pega; }
            foreach (var s in Soldas) foreach (var q in s.P) yield return q;
            foreach (var t in Tubos) foreach (var q in PertoDoNo(t)) yield return q;
        }

        public IEnumerable<Point3D> Pontos()
        {
            foreach (var p in Pecas) foreach (var q in p.Base) { yield return q + p.N * p.T / 2; yield return q - p.N * p.T / 2; }
            foreach (var b in Parafs) { yield return b.O - b.E * 0.7 * b.D; yield return b.O + b.E * b.L; }
            foreach (var s in Soldas) foreach (var q in s.P) yield return q;
            foreach (var t in Tubos)
            {
                var (u, w) = t.Base();
                foreach (var c in new[] { t.O, t.O + t.E * t.L }) { yield return c + u * t.D / 2; yield return c - u * t.D / 2; yield return c + w * t.D / 2; yield return c - w * t.D / 2; }
            }
        }
    }

    public enum VistaLig { Elevacao, Planta, Lateral, Iso }

    // Elevação, Planta e Lateral: desenho técnico 2D (este arquivo). 3D (VistaLig.Iso): cena sólida com z-buffer num Viewport3D
    // (LigacaoDesenho3D.cs).
    public partial class DesenhoLigacao : FrameworkElement
    {
        public static readonly DependencyProperty ProgProperty = DependencyProperty.Register("Prog", typeof(double), typeof(DesenhoLigacao),
            new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((DesenhoLigacao)d).ProgMudou()));
        public double Prog { get => (double)GetValue(ProgProperty); set => SetValue(ProgProperty, value); }

        public VistaLig Vista;
        public bool Miniatura;
        public string Titulo;
        GeoLig geo, anterior;
        // vista inicial do 3D: do lado da viga (+x) e da face da alma (+y), de cima: cabeças da chapa de topo e da chapa simples à vista
        const double YawIni = 50, PitchIni = 22;
        double yaw = YawIni, pitch = PitchIni;
        Point? arrasto; double yawIni, pitchIni;

        public DesenhoLigacao(VistaLig v, bool miniatura = false)
        {
            Vista = v; Miniatura = miniatura; ClipToBounds = true; SnapsToDevicePixels = true;
            if (v == VistaLig.Iso) Iniciar3D();
            if (v == VistaLig.Iso && !miniatura)
            {
                Cursor = Cursors.SizeAll;
                ToolTip = "Arraste para girar · roda = zoom · clique numa peça = vai ao campo · duplo clique = vista inicial";
                System.Windows.Controls.ToolTipService.SetInitialShowDelay(this, 900);
            }
        }

        public GeoLig Geo => geo;

        // troca a geometria; a mudança é animada (posições interpoladas ou fade) com o valor final já gravado
        public void Mostrar(GeoLig g, bool animar = true)
        {
            anterior = geo == null ? null : (Prog < 1 ? GeoLig.Misturar(anterior, geo, Prog) ?? geo : geo);
            geo = g;
            if (animar && anterior != null) Anim.Valor(this, ProgProperty, 0, 1, 260);
            else { anterior = null; Prog = 1; InvalidateVisual(); }
            if (Vista == VistaLig.Iso) Reconstruir();
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            if (Miniatura) return;
            descida = e.GetPosition(this);
            if (Vista != VistaLig.Iso) { CaptureMouse(); e.Handled = true; return; }
            if (e.ClickCount == 2) { yaw = YawIni; pitch = PitchIni; zoom = 1; AtualizarCamera(); e.Handled = true; return; }
            arrasto = e.GetPosition(this); yawIni = yaw; pitchIni = pitch; CaptureMouse(); e.Handled = true;
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            var p = e.GetPosition(this);
            if (!Miniatura && ClicouPeca != null && !IsMouseCaptured) Cursor = PecaEm(p) != null ? Cursors.Hand : Vista == VistaLig.Iso ? Cursors.SizeAll : Cursors.Arrow;
            if (arrasto == null || !IsMouseCaptured) return;
            yaw = yawIni - (p.X - arrasto.Value.X) * 0.45;
            pitch = Math.Max(-80, Math.Min(85, pitchIni + (p.Y - arrasto.Value.Y) * 0.45));
            AtualizarCamera();
        }
        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            arrasto = null; bool tinha = IsMouseCaptured; ReleaseMouseCapture();
            if (tinha && descida is Point d0 && (e.GetPosition(this) - d0).Length < 4 && PecaEm(d0) is string tag) ClicouPeca?.Invoke(tag);
            descida = null;
        }

        // base da vista: R (direita), U (cima), F (para o observador)
        (Vector3D R, Vector3D U, Vector3D F) Base()
        {
            // W8-CONTRAV: o motor pode dar os eixos da vista (`vista`: x = horizontal da elevação, z = vertical) no plano xz
            var vx = geo?.VistaX ?? new Vector3D(1, 0, 0); var vz = geo?.VistaZ ?? new Vector3D(0, 0, 1); var vy = new Vector3D(0, 1, 0);
            switch (Vista)
            {
                case VistaLig.Elevacao: return (vx, vz, Vector3D.CrossProduct(vx, vz));
                case VistaLig.Planta: return (vx, vy, Vector3D.CrossProduct(vx, vy));
                case VistaLig.Lateral: return (vy, vz, Vector3D.CrossProduct(vy, vz));
                default:
                    double y = yaw * Math.PI / 180, p = pitch * Math.PI / 180;
                    var F = Math.Cos(p) * Math.Cos(y) * vx + Math.Cos(p) * Math.Sin(y) * vy + Math.Sin(p) * vz;
                    var R = Vector3D.CrossProduct(vz, F); R.Normalize();
                    var U = Vector3D.CrossProduct(F, R);
                    return (R, U, F);
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            double W = ActualWidth, H = ActualHeight;
            dc.DrawRectangle(TemaPlant.BBgCanvas, null, new Rect(0, 0, W, H));
            if (Vista == VistaLig.Iso) return;   // o 3D é o Viewport3D filho; título e triedro na sobreposição
            if (geo == null || W < 40 || H < 40) return;
            double t = Prog;
            var mix = anterior == null || t >= 1 ? null : GeoLig.Misturar(anterior, geo, t);
            if (mix != null) Desenhar(dc, mix, 1);
            else
            {
                if (anterior != null && t < 1) Desenhar(dc, anterior, 1 - t);
                Desenhar(dc, geo, anterior != null && t < 1 ? t : 1);
            }
        }

        // clique sem arrastar numa peca: Tag (campo do contrato) ou null
        public event Action<string> ClicouPeca;
        Func<Point3D, Point> proj; (Vector3D R, Vector3D U, Vector3D F) bs; double escUlt;
        Point? descida;

        // capturas/testes: um ponto da tela onde o clique cai numa chapa (varre a caixa da 1a chapa)
        public Point? CentroChapa()
        {
            var pc = geo?.Pecas.FirstOrDefault(q => q.Camada == 1);
            if (pc == null || proj == null) return null;
            var pts = pc.Base.Select(proj).ToList();
            double x0 = pts.Min(q => q.X), x1 = pts.Max(q => q.X), y0 = pts.Min(q => q.Y), y1 = pts.Max(q => q.Y);
            for (int a = 0; a <= 8; a++) for (int b = 0; b <= 8; b++)
            {
                var q = new Point(x0 + (x1 - x0) * (0.1 + 0.1 * a), y0 + (y1 - y0) * (0.1 + 0.1 * b));
                if (PecaEm(q) is string t && !string.IsNullOrEmpty(t) && geo.Pecas.Any(z => z.Camada == 1 && z.Tag == t)) return q;
            }
            return null;
        }

        public string PecaEm(Point p)
        {
            if (Vista == VistaLig.Iso) return PecaEm3D(p);
            if (geo == null || proj == null) return null;
            foreach (var b in geo.Parafs)
            {
                if (string.IsNullOrEmpty(b.Tag)) continue;
                double r = Math.Max(5, b.D * escUlt * 0.8);
                if ((proj(b.O) - p).Length <= r || (proj(b.O + b.E * b.Pega) - p).Length <= r) return b.Tag;
            }
            foreach (var s in geo.Soldas.Where(q => !string.IsNullOrEmpty(q.Tag)))
            {
                var sp = s.P.Select(proj).ToList();
                for (int i = 1; i < sp.Count; i++) if (DistSeg(p, sp[i - 1], sp[i]) <= 4) return s.Tag;
            }
            string melhor = null; double zm = double.NegativeInfinity;
            foreach (var pc in geo.Pecas.Where(q => !string.IsNullOrEmpty(q.Tag)))
            {
                var poly = Casco(pc.Base.SelectMany(q => new[] { proj(q + pc.N * pc.T / 2), proj(q - pc.N * pc.T / 2) }).ToList());
                if (poly.Count < 3 || !Dentro(poly, p)) continue;
                double z = pc.Base.Average(q => Dot(q, bs.F)) + pc.Camada * 1e4;   // pecas da ligacao por cima dos perfis
                if (z > zm) { zm = z; melhor = pc.Tag; }
            }
            foreach (var tb in geo.Tubos.Where(q => !string.IsNullOrEmpty(q.Tag)))
            {
                if (!FaixasTubo(tb, proj, out _, out _, out _).Any(f => Dentro(f.ToList(), p))) continue;
                double z = Dot(tb.O + tb.E * tb.L / 2, bs.F);   // como perfil (camada 0)
                if (z > zm) { zm = z; melhor = tb.Tag; }
            }
            return melhor;
        }
        static bool Dentro(List<Point> poly, Point p)
        {
            bool dentro = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                if ((poly[i].Y > p.Y) != (poly[j].Y > p.Y) && p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X) dentro = !dentro;
            return dentro;
        }
        static double DistSeg(Point p, Point a, Point b)
        {
            var ab = b - a; double l2 = ab.LengthSquared; if (l2 < 1e-9) return (p - a).Length;
            double t = Math.Max(0, Math.Min(1, ((p - a) * ab) / l2));
            return (p - (a + ab * t)).Length;
        }

        void Desenhar(DrawingContext dc, GeoLig g, double opac)
        {
            double W = ActualWidth, H = ActualHeight;
            var (R, U, F) = Base();
            var pts = g.Pontos().ToList();
            if (pts.Count == 0) return;
            double x0 = pts.Min(p => Dot(p, R)), x1 = pts.Max(p => Dot(p, R)), y0 = pts.Min(p => Dot(p, U)), y1 = pts.Max(p => Dot(p, U));
            // enquadra a ligação (chapas, parafusos, soldas) com folga; os perfis continuam além da borda, como num detalhe
            var foco = g.Foco().ToList();
            if (foco.Count > 0)
            {
                double fx0 = foco.Min(p => Dot(p, R)), fx1 = foco.Max(p => Dot(p, R)), fy0 = foco.Min(p => Dot(p, U)), fy1 = foco.Max(p => Dot(p, U));
                double folga = Math.Max((Vista == VistaLig.Iso && !Miniatura ? 0.75 : 0.38) * Math.Max(fx1 - fx0, fy1 - fy0), Miniatura ? 70 : 90);
                x0 = Math.Max(x0, fx0 - folga); x1 = Math.Min(x1, fx1 + folga); y0 = Math.Max(y0, fy0 - folga); y1 = Math.Min(y1, fy1 + folga);
            }
            double mL = Miniatura ? 8 : 34, mR = Miniatura ? 8 : 92, mT = Miniatura ? 8 : 40, mB = Miniatura ? 8 : 58;
            if (Vista == VistaLig.Iso && !Miniatura) { mL = mR = 24; mT = 36; mB = 24; }
            double esc = Math.Min((W - mL - mR) / Math.Max(1, x1 - x0), (H - mT - mB) / Math.Max(1, y1 - y0));
            if (esc <= 0) return;
            double ox = mL + (W - mL - mR - (x1 - x0) * esc) / 2 - x0 * esc, oy = mT + (H - mT - mB - (y1 - y0) * esc) / 2 + y1 * esc;
            Point P(Point3D p) => new Point(ox + Dot(p, R) * esc, oy - Dot(p, U) * esc);
            if (g == geo) { proj = P; bs = (R, U, F); escUlt = esc; }
            if (opac < 1) dc.PushOpacity(opac);
            Orto(dc, g, R, U, F, P, esc);
            if (!Miniatura && Titulo != null)
            {
                var tt = Txt(Titulo.ToUpperInvariant(), 10.5, TemaPlant.BTexto2, FontWeights.SemiBold);
                dc.DrawText(tt, new Point(12, 10));
            }
            if (!Miniatura && g == geo) Triedro(dc, R, U, F, new Point(40, H - 34));
            if (opac < 1) dc.Pop();
        }

        // eixos dos esforços (Passo 1): x = eixo da viga (Fx, Mx), y = fora do plano da alma (Fy, My), z = vertical (Fz, Mz).
        // Eixo apontando para o observador = ponto (⊙), para dentro = cruz (⊗).
        void Triedro(DrawingContext dc, Vector3D R, Vector3D U, Vector3D F, Point o)
        {
            foreach (var (v, nome, cor) in new[] { (new Vector3D(1, 0, 0), "x", TemaPlant.BEixoX), (new Vector3D(0, 1, 0), "y", TemaPlant.BEixoY), (new Vector3D(0, 0, 1), "z", TemaPlant.BAcento) })
            {
                var d = new Vector(Vector3D.DotProduct(v, R), -Vector3D.DotProduct(v, U)) * 22;
                var pen = new Pen(cor, 2);
                Point fim;
                if (d.Length < 4)
                {
                    bool frente = Vector3D.DotProduct(v, F) > 0;
                    dc.DrawEllipse(null, pen, o, 5, 5);
                    if (frente) dc.DrawEllipse(cor, null, o, 1.8, 1.8);
                    else { dc.DrawLine(pen, o + new Vector(-3.5, -3.5), o + new Vector(3.5, 3.5)); dc.DrawLine(pen, o + new Vector(-3.5, 3.5), o + new Vector(3.5, -3.5)); }
                    fim = o + new Vector(7, 7);
                }
                else
                {
                    fim = o + d;
                    var u = d / d.Length; var nn = new Vector(-u.Y, u.X);
                    dc.DrawLine(pen, o, fim);
                    dc.DrawGeometry(cor, null, Poli(new[] { fim + u * 4, fim - u * 4 + nn * 3.5, fim - u * 4 - nn * 3.5 }));
                    fim += u * 9;
                }
                var tx = Txt(nome, 11, cor, FontWeights.SemiBold);
                dc.DrawText(tx, new Point(fim.X - tx.Width / 2, fim.Y - tx.Height / 2));
            }
        }

        static double Dot(Point3D p, Vector3D v) => p.X * v.X + p.Y * v.Y + p.Z * v.Z;

        FormattedText Txt(string s, double tam, Brush cor, FontWeight? peso = null) =>
            new FormattedText(s ?? "", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(TemaPlant.Fonte, FontStyles.Normal, peso ?? FontWeights.Normal, FontStretches.Normal), tam, cor, VisualTreeHelper.GetDpi(this).PixelsPerDip);

        static StreamGeometry Poli(IList<Point> p, bool fechar = true)
        {
            var g = new StreamGeometry();
            using (var c = g.Open()) { c.BeginFigure(p[0], true, fechar); c.PolyLineTo(p.Skip(1).ToList(), true, true); }
            g.Freeze();
            return g;
        }

        static List<Point> Casco(List<Point> p)   // casco convexo (cadeia monótona)
        {
            var s = p.Distinct().OrderBy(q => q.X).ThenBy(q => q.Y).ToList();
            if (s.Count < 3) return s;
            double X(Point o, Point a, Point b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
            var h = new List<Point>();
            foreach (var q in s) { while (h.Count >= 2 && X(h[h.Count - 2], h[h.Count - 1], q) <= 0) h.RemoveAt(h.Count - 1); h.Add(q); }
            int lo = h.Count + 1;
            for (int i = s.Count - 2; i >= 0; i--) { var q = s[i]; while (h.Count >= lo && X(h[h.Count - 2], h[h.Count - 1], q) <= 0) h.RemoveAt(h.Count - 1); h.Add(q); }
            h.RemoveAt(h.Count - 1);
            return h;
        }

        // ---------------------------------------------------------------- vistas ortogonais
        void Orto(DrawingContext dc, GeoLig g, Vector3D R, Vector3D U, Vector3D F, Func<Point3D, Point> P, double esc)
        {
            double lw = Miniatura ? 0.8 : 1;
            // perfis, depois chapas e cantoneiras (as peças da ligação ficam por cima)
            foreach (var camada in new[] { 0, 1, 2 })
            {
                // tubos com os perfis; W8: o receptor antes dos perfis (o perfil que encosta nele fica na frente na lateral)
                if (camada == 0) foreach (var tb in g.Tubos.Where(q => q.Grupo == "suporte")) DesenharTubo(dc, tb, P, lw);
                if (camada == 1) foreach (var tb in g.Tubos.Where(q => q.Grupo != "suporte")) DesenharTubo(dc, tb, P, lw);
                foreach (var pc in g.Pecas.Where(q => q.Camada == camada))
                {
                    bool frente = Math.Abs(Vector3D.DotProduct(pc.N, F)) > 0.85;
                    var contorno = frente ? pc.Base.Select(q => P(q)).ToList()
                        : Casco(pc.Base.SelectMany(q => new[] { P(q + pc.N * pc.T / 2), P(q - pc.N * pc.T / 2) }).ToList());
                    if (contorno.Count < 3) continue;
                    var geom = Poli(contorno);
                    Brush fundo = pc.Falha ? TemaPlant.BErroFundo : camada == 0 ? TemaPlant.BLigPerfil : TemaPlant.BPlacaFundo;
                    Brush traco = pc.Falha ? TemaPlant.BErro : camada == 0 ? TemaPlant.BTexto2 : TemaPlant.BPlacaContorno;
                    dc.DrawGeometry(fundo, null, geom);
                    if (!frente && camada > 0)   // chapa de topo/de lado: hachura da espessura
                    {
                        dc.PushClip(geom);
                        var b = geom.Bounds; var ph = new Pen(pc.Falha ? TemaPlant.BErro : TemaPlant.BPlacaHachura, 1);
                        for (double d = -b.Height; d < b.Width + b.Height; d += Miniatura ? 4 : 5) dc.DrawLine(ph, new Point(b.Left + d, b.Top), new Point(b.Left + d - b.Height, b.Bottom));
                        dc.Pop();
                    }
                    dc.DrawGeometry(null, new Pen(traco, (camada == 0 ? 1.2 : 1.7) * lw) { LineJoin = PenLineJoin.Round }, geom);
                }
            }
            if (g.Parede && !Miniatura && Math.Abs(R.X) > 0.9)
            {
                double x = P(new Point3D(g.XParede, 0, 0)).X;
                var pp = new Pen(TemaPlant.BBarra, 2);
                dc.DrawLine(pp, new Point(x, 30), new Point(x, ActualHeight - 50));
                var ph = new Pen(TemaPlant.BTexto3, 1);
                for (double y = 34; y < ActualHeight - 50; y += 9) dc.DrawLine(ph, new Point(x, y), new Point(x - 8, y + 8));
                var ta = Txt("apoio", 10.5, TemaPlant.BTexto3);
                dc.DrawText(ta, new Point(x - ta.Width - 12, 34));
            }
            // soldas: cordão com triângulos (símbolo de filete); de topo vira o triângulo do filete
            foreach (var s in g.Soldas)
            {
                var sp = s.P.Select(P).ToList();
                double comp = 0; for (int i = 1; i < sp.Count; i++) comp += (sp[i] - sp[i - 1]).Length;
                Brush cor = s.Falha ? TemaPlant.BErro : TemaPlant.BLigSolda;
                if (comp > 6)
                {
                    dc.DrawGeometry(null, new Pen(cor, Miniatura ? 1.6 : 2.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, Poli(sp, false));
                    if (!Miniatura)
                        for (int i = 1; i < sp.Count; i++)
                        {
                            var a = sp[i - 1]; var v = sp[i] - a; double L = v.Length; if (L < 1) continue; v /= L;
                            var n = new Vector(-v.Y, v.X); double k = Math.Max(3, Math.Min(6, s.Perna * esc * 0.8));
                            for (double d = 6; d < L - 3; d += 11)
                            {
                                var q = a + v * d;
                                dc.DrawGeometry(cor, null, Poli(new[] { q, q + v * k, q + n * k }));
                            }
                        }
                }
                else
                {
                    var q = sp[0]; double k = Math.Max(4, s.Perna * esc * 1.2);
                    dc.DrawGeometry(cor, null, Poli(new[] { new Point(q.X, q.Y - k), new Point(q.X + k, q.Y), q }));
                }
            }
            // parafusos
            foreach (var b in g.Parafs)
            {
                double fe = Vector3D.DotProduct(b.E, F);
                Brush cor = b.Falha ? TemaPlant.BErro : TemaPlant.BRealce;
                if (Math.Abs(fe) > 0.85)
                {
                    var c = P(b.O); double r = Math.Max(Miniatura ? 1.5 : 3, b.D / 2 * esc);
                    if (!Miniatura && fe < 0)   // cabeça para o observador: sextavado
                    {
                        double s = 0.85 * b.D * esc;
                        var hx = Enumerable.Range(0, 6).Select(i => new Point(c.X + s * Math.Cos(Math.PI / 3 * i + Math.PI / 6), c.Y + s * Math.Sin(Math.PI / 3 * i + Math.PI / 6))).ToList();
                        dc.DrawGeometry(null, new Pen(TemaPlant.B(TemaPlant.Realce, 110), 1), Poli(hx));
                    }
                    dc.DrawEllipse(TemaPlant.BBgCanvas, new Pen(cor, Miniatura ? 1 : 1.6), c, r, r);
                    if (!Miniatura)
                    {
                        var pc = new Pen(cor, 0.9);
                        dc.DrawLine(pc, new Point(c.X - r * 1.6, c.Y), new Point(c.X + r * 1.6, c.Y));
                        dc.DrawLine(pc, new Point(c.X, c.Y - r * 1.6), new Point(c.X, c.Y + r * 1.6));
                    }
                }
                else
                {
                    var e2 = new Vector(Vector3D.DotProduct(b.E, R), -Vector3D.DotProduct(b.E, U)); if (e2.Length < 1e-6) continue; e2.Normalize();
                    var n = new Vector(-e2.Y, e2.X);
                    var a = P(b.O); double d = b.D * esc, L = b.L * esc, ph = 0.65 * b.D * esc, pg = b.Pega * esc;
                    Point Q(double ao, double tr) => a + e2 * ao + n * tr;
                    var pen = new Pen(cor, Miniatura ? 0.8 : 1.3);
                    dc.DrawGeometry(TemaPlant.BBgCanvas, pen, Poli(new[] { Q(0, -d / 2), Q(L, -d / 2), Q(L, d / 2), Q(0, d / 2) }));
                    dc.DrawGeometry(TemaPlant.B(TemaPlant.Realce, 60), pen, Poli(new[] { Q(-ph, -0.85 * d), Q(0, -0.85 * d), Q(0, 0.85 * d), Q(-ph, 0.85 * d) }));
                    if (pg + 0.8 * b.D * esc < L + 1)
                        dc.DrawGeometry(TemaPlant.B(TemaPlant.Realce, 60), pen, Poli(new[] { Q(pg, -0.85 * d), Q(pg + 0.8 * b.D * esc, -0.85 * d), Q(pg + 0.8 * b.D * esc, 0.85 * d), Q(pg, 0.85 * d) }));
                    if (!Miniatura) dc.DrawLine(new Pen(cor, 0.7) { DashStyle = new DashStyle(new double[] { 6, 3, 1, 3 }, 0) }, Q(-ph - 4, 0), Q(L + 4, 0));
                }
            }
            if (!Miniatura) { Cotas(dc, g, R, U, F, P, esc); Rotulos(dc, g, R, U, F, P, esc); Linhas(dc, g, P); }
        }

        // Tubo na vista ortogonal: faixas entre geratrizes vizinhas (a união é a silhueta, inclusive a ponta em sela), as duas
        // geratrizes de contorno, a curva de interseção (início de cada geratriz, GeoLig.Tubo.Inicio) e as bordas da ponta livre.
        const int NTubo = 72;
        static List<Point[]> FaixasTubo(GeoLig.Tubo t, Func<Point3D, Point> P, out List<Point> ini, out List<Point> fim, out List<Point> fimInt)
        {
            var (u, w) = t.Base(); double R0 = t.D / 2, Ri = R0 - t.T;
            ini = new List<Point>(); fim = new List<Point>(); fimInt = new List<Point>();
            for (int k = 0; k < NTubo; k++)
            {
                double th = 2 * Math.PI * k / NTubo; var rad = u * Math.Cos(th) + w * Math.Sin(th);
                var p0 = t.O + rad * R0;
                ini.Add(P(p0 + t.E * t.Inicio(p0))); fim.Add(P(p0 + t.E * t.L)); fimInt.Add(P(t.O + rad * Ri + t.E * t.L));
            }
            var r = new List<Point[]>();
            for (int k = 0; k < NTubo; k++)
            {
                int j = (k + 1) % NTubo;
                var q = new[] { ini[k], ini[j], fim[j], fim[k] };
                double a = 0; for (int i = 0; i < 4; i++) a += q[i].X * q[(i + 1) % 4].Y - q[(i + 1) % 4].X * q[i].Y;
                if (a < 0) Array.Reverse(q);   // todas no mesmo sentido: a regra não nula faz a união
                r.Add(q);
            }
            return r;
        }

        void DesenharTubo(DrawingContext dc, GeoLig.Tubo t, Func<Point3D, Point> P, double lw)
        {
            var faixas = FaixasTubo(t, P, out var ini, out var fim, out var fimInt);
            var g = new StreamGeometry { FillRule = FillRule.Nonzero };
            using (var c = g.Open()) foreach (var f in faixas) { c.BeginFigure(f[0], true, true); c.PolyLineTo(f.Skip(1).ToList(), false, false); }
            g.Freeze();
            Brush fundo = t.Falha ? TemaPlant.BErroFundo : TemaPlant.BLigPerfil, traco = t.Falha ? TemaPlant.BErro : TemaPlant.BTexto2;
            dc.DrawGeometry(fundo, null, g);
            var ponta = new StreamGeometry { FillRule = FillRule.EvenOdd };   // anel da ponta livre (aparece de topo)
            using (var c = ponta.Open()) foreach (var l in new[] { fim, fimInt }) { c.BeginFigure(l[0], true, true); c.PolyLineTo(l.Skip(1).ToList(), false, false); }
            ponta.Freeze();
            var pen = new Pen(traco, 1.2 * lw) { LineJoin = PenLineJoin.Round };
            dc.DrawGeometry(fundo, pen, ponta);
            dc.DrawGeometry(null, pen, Poli(ini));
            var e2 = P(t.O + t.E * t.L) - P(t.O);
            if (e2.Length > 1)   // geratrizes de contorno: as mais afastadas do eixo na tela
            {
                var n = new Vector(-e2.Y, e2.X); n.Normalize();
                int kmax = 0, kmin = 0;
                double Dn(int k) => (Vector)fim[k] * n;
                for (int k = 1; k < NTubo; k++) { if (Dn(k) > Dn(kmax)) kmax = k; if (Dn(k) < Dn(kmin)) kmin = k; }
                foreach (int k in new[] { kmax, kmin }) dc.DrawLine(pen, ini[k], fim[k]);
            }
        }

        // cotas: cadeias de borda → furos → borda na horizontal (embaixo) e na vertical (à direita) da chapa com mais furos
        void Cotas(DrawingContext dc, GeoLig g, Vector3D R, Vector3D U, Vector3D F, Func<Point3D, Point> P, double esc)
        {
            var placas = g.Pecas.Where(q => q.Camada > 0).ToList();
            if (placas.Count == 0 || g.Parafs.Count == 0) return;
            foreach (var horizontal in new[] { true, false })
            {
                var eixo = horizontal ? R : U;
                var furos = g.Parafs.Where(b => Math.Abs(Vector3D.DotProduct(b.E, eixo)) < 0.3).ToList();
                if (furos.Count == 0) continue;
                (double a, double b, bool frente, double area, int n) Faixa(GeoLig.Prisma pc)
                {
                    var c = pc.Base.SelectMany(q => new[] { q + pc.N * pc.T / 2, q - pc.N * pc.T / 2 }).ToList();
                    double a = c.Min(q => Dot(q, eixo)), b = c.Max(q => Dot(q, eixo));
                    var outro = horizontal ? U : R;
                    double oa = c.Min(q => Dot(q, outro)), ob = c.Max(q => Dot(q, outro));
                    int n = furos.Count(f => Dot(f.O, eixo) > a - 0.5 && Dot(f.O, eixo) < b + 0.5 && Dot(f.O, outro) > oa - f.L && Dot(f.O, outro) < ob + f.L);
                    return (a, b, Math.Abs(Vector3D.DotProduct(pc.N, F)) > 0.85, (b - a) * (ob - oa), n);
                }
                // empate de furos: a chapa (gusset) antes da cantoneira que passa por ela (W7-LIG: a cantoneira segue além do gusset)
                var cands = placas.Select(pc => (pc, f: Faixa(pc))).Where(x => x.f.n > 0).ToList();
                // W8-RIG: empate final (talas de mesa de cima e de baixo da emenda): a peça mais para fora do lado da cadeia
                // (mais embaixo na horizontal, mais à direita na vertical); antes a de cima punha a cadeia no meio do perfil
                double Fora(GeoLig.Prisma pc) => pc.Base.SelectMany(q => new[] { P(q + pc.N * pc.T / 2), P(q - pc.N * pc.T / 2) }).Max(q => horizontal ? q.Y : q.X);
                var cand = cands.OrderByDescending(x => x.f.n).ThenBy(x => x.pc.Camada == 1 ? 0 : 1).ThenByDescending(x => x.f.frente).ThenBy(x => x.f.area).ThenByDescending(x => Fora(x.pc)).FirstOrDefault();
                if (cand.pc == null) continue;
                var pl = cand.pc;
                var vals = new List<double> { cand.f.a, cand.f.b };
                // bordas das outras peças furadas dentro da faixa (ponta da cantoneira, talão e ponta da aba): recuo, e1, g e b − g na cadeia
                foreach (var o in cands.Where(o => o.pc.Camada != pl.Camada && Math.Abs(Vector3D.DotProduct(o.pc.N, eixo)) < 0.85))
                    foreach (var v in new[] { o.f.a, o.f.b }) if (v > cand.f.a + 0.5 && v < cand.f.b - 0.5) vals.Add(v);
                // W8-RIG: só os furos que atravessam a chapa (fuste dentro dela nos outros dois eixos); na emenda, os parafusos
                // da mesa entravam na cadeia da tala da alma ("55 80 55" com total 270)
                bool Atravessa(GeoLig.Paraf f)
                {
                    var cc = pl.Base.SelectMany(q => new[] { q + pl.N * pl.T / 2, q - pl.N * pl.T / 2 }).ToList();
                    foreach (var ax in new[] { horizontal ? U : R, F })
                    {
                        double p0 = Dot(f.O, ax), p1 = Dot(f.O + f.E * f.L, ax), m = f.D / 2 * Math.Sqrt(Math.Max(0, 1 - Math.Pow(Vector3D.DotProduct(f.E, ax), 2))) + 0.5;
                        if (Math.Max(p0, p1) + m < cc.Min(q => Dot(q, ax)) || Math.Min(p0, p1) - m > cc.Max(q => Dot(q, ax))) return false;
                    }
                    return true;
                }
                foreach (var f in furos) { double v = Dot(f.O, eixo); if (v > cand.f.a - 0.5 && v < cand.f.b + 0.5 && Atravessa(f)) vals.Add(v); }
                vals = vals.OrderBy(v => v).Aggregate(new List<double>(), (l, v) => { if (l.Count == 0 || v - l[l.Count - 1] > 0.5) l.Add(v); return l; });
                if (vals.Count < 3) continue;
                var cs = pl.Base.SelectMany(q => new[] { P(q + pl.N * pl.T / 2), P(q - pl.N * pl.T / 2) }).ToList();
                // W8-RIG: a cadeia fica fora também das outras peças e parafusos da ligação na mesma faixa (na emenda, a
                // tala da alma punha a cadeia em cima da tala da mesa de baixo e das porcas)
                double fa = vals[0], fb = vals[vals.Count - 1];
                var fora = new List<Point>(cs);
                foreach (var o in placas)
                {
                    var oc = o.Base.SelectMany(q => new[] { q + o.N * o.T / 2, q - o.N * o.T / 2 }).ToList();
                    if (oc.Max(q => Dot(q, eixo)) > fa + 0.5 && oc.Min(q => Dot(q, eixo)) < fb - 0.5) fora.AddRange(oc.Select(P));
                }
                foreach (var f in g.Parafs)
                    if (Dot(f.O, eixo) > fa - 0.5 && Dot(f.O, eixo) < fb + 0.5) { fora.Add(P(f.O)); fora.Add(P(f.O + f.E * f.L)); }
                double pos = horizontal ? Math.Min(ActualHeight - 22, fora.Max(q => q.Y) + 26) : Math.Min(ActualWidth - 30, fora.Max(q => q.X) + 30);
                var pen = new Pen(TemaPlant.BTexto2, 1);
                var penL = new Pen(TemaPlant.BTextoMudo, 0.8) { DashStyle = new DashStyle(new double[] { 2, 3 }, 0) };
                Point Tela(double v) { var p = P(new Point3D(0, 0, 0) + eixo * v); return horizontal ? new Point(p.X, pos) : new Point(pos, p.Y); }
                var tl = vals.Select(Tela).ToList();
                dc.DrawLine(pen, tl[0], tl[tl.Count - 1]);
                foreach (var q in tl)
                {
                    var d = new Vector(4, -4);
                    dc.DrawLine(new Pen(TemaPlant.BTexto2, 1.3), q - d, q + d);
                    var desde = horizontal ? new Point(q.X, cs.Max(c => c.Y) + 3) : new Point(cs.Max(c => c.X) + 3, q.Y);
                    dc.DrawLine(penL, desde, q);
                }
                // trechos; trechos internos iguais seguidos viram "n × s"
                var seg = new List<(int i0, int i1, double v, int n)>();
                for (int i = 0; i < vals.Count - 1; i++)
                {
                    double v = vals[i + 1] - vals[i];
                    bool interno = i > 0 && i < vals.Count - 2;
                    if (interno && seg.Count > 0 && seg[seg.Count - 1].i0 > 0 && Math.Abs(seg[seg.Count - 1].v - v) < 0.5 && seg[seg.Count - 1].i1 == i) { var u = seg[seg.Count - 1]; seg[seg.Count - 1] = (u.i0, i + 1, v, u.n + 1); }
                    else seg.Add((i, i + 1, v, 1));
                }
                string Fmt(double v) => Num.Fmt(v, Math.Abs(v - Math.Round(v)) < 0.05 ? 0 : 1);
                bool cortou = false;
                foreach (var s in seg)
                {
                    string txt = (s.n > 1 ? s.n + " × " : "") + Fmt(s.v);
                    var ft = Txt(txt, 11, TemaPlant.BTexto1);
                    var m = new Point((tl[s.i0].X + tl[s.i1].X) / 2, (tl[s.i0].Y + tl[s.i1].Y) / 2);
                    double lpx = (tl[s.i1] - tl[s.i0]).Length;
                    if ((horizontal ? ft.Width : ft.Height) + 4 > lpx) { cortou = true; continue; }   // não cabe: fica só o traço (o total vai abaixo)
                    Rect rr = horizontal ? new Rect(m.X - ft.Width / 2 - 3, m.Y + 3, ft.Width + 6, ft.Height) : new Rect(m.X + 5, m.Y - ft.Height / 2, ft.Width + 6, ft.Height);
                    dc.DrawRoundedRectangle(TemaPlant.BFundoTextoCanvas, null, rr, 2, 2);
                    dc.DrawText(ft, new Point(rr.X + 3, rr.Y));
                }
                if (cortou || seg.Count >= 3)   // cota total da chapa, uma linha mais para fora
                {
                    var off = horizontal ? new Vector(0, 22) : new Vector(46, 0);
                    Point a = tl[0] + off, b = tl[tl.Count - 1] + off;
                    dc.DrawLine(pen, a, b);
                    foreach (var q in new[] { a, b }) { dc.DrawLine(new Pen(TemaPlant.BTexto2, 1.3), q - new Vector(4, -4), q + new Vector(4, -4)); dc.DrawLine(penL, q - off, q); }
                    var ft = Txt(Fmt(vals[vals.Count - 1] - vals[0]), 11, TemaPlant.BTexto1, FontWeights.SemiBold);
                    var m = new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2);
                    Rect rr = horizontal ? new Rect(m.X - ft.Width / 2 - 3, m.Y + 3, ft.Width + 6, ft.Height) : new Rect(m.X + 5, m.Y - ft.Height / 2, ft.Width + 6, ft.Height);
                    dc.DrawRoundedRectangle(TemaPlant.BFundoTextoCanvas, null, rr, 2, 2);
                    dc.DrawText(ft, new Point(rr.X + 3, rr.Y));
                }
                // W7-LIG: na elevação, topo da chapa/cantoneira ao topo da viga (campo "topo da chapa ao topo viga")
                var vg = g.Pecas.Where(q => q.Grupo == "viga").SelectMany(q => q.Base.SelectMany(p => new[] { p + q.N * q.T / 2, p - q.N * q.T / 2 })).ToList();
                if (!horizontal && Vista == VistaLig.Elevacao && vg.Count > 0)
                {
                    double vt = vg.Max(q => Dot(q, eixo)), b0 = vals[vals.Count - 1];
                    if (vt > b0 + 0.5)
                    {
                        Point a = Tela(b0), b = Tela(vt);
                        dc.DrawLine(pen, a, b);
                        foreach (var q in new[] { a, b }) dc.DrawLine(new Pen(TemaPlant.BTexto2, 1.3), q - new Vector(4, -4), q + new Vector(4, -4));
                        dc.DrawLine(penL, new Point(vg.Select(P).Min(q => q.X), b.Y), b);
                        var ft = Txt(Fmt(vt - b0), 11, TemaPlant.BTexto1);
                        Rect rr = new Rect(a.X + 5, (a.Y + b.Y) / 2 - ft.Height / 2, ft.Width + 6, ft.Height);
                        dc.DrawRoundedRectangle(TemaPlant.BFundoTextoCanvas, null, rr, 2, 2);
                        dc.DrawText(ft, new Point(rr.X + 3, rr.Y));
                    }
                }
            }
        }

        // rótulos: perfis, chapa (espessura × altura) e a chamada de solda (linha de referência + triângulo + perna)
        void Rotulos(DrawingContext dc, GeoLig g, Vector3D R, Vector3D U, Vector3D F, Func<Point3D, Point> P, double esc)
        {
            void Caixa(string s, Point p, Brush cor, bool esquerda = false)
            {
                var ft = Txt(s, 11, cor);
                var r = new Rect(esquerda ? p.X - ft.Width - 8 : p.X, p.Y, ft.Width + 8, ft.Height + 2);
                if (r.Right > ActualWidth - 4) r.X = ActualWidth - 4 - r.Width;
                if (r.X < 4) r.X = 4;
                dc.DrawRoundedRectangle(TemaPlant.BFundoTextoCanvas, null, r, 3, 3);
                dc.DrawText(ft, new Point(r.X + 4, r.Y + 1));
            }
            var viga = g.Pecas.Where(q => q.Grupo == "viga").SelectMany(q => q.Base).ToList();
            if (viga.Count > 0 && g.NomeViga != "" && Vista != VistaLig.Lateral)
            {
                var ps = viga.Select(P).ToList();
                Caixa(g.NomeViga.Replace("x", "×"), new Point(ps.Max(q => q.X) - 4, ps.Min(q => q.Y) - 20), TemaPlant.BRealceClaro, true);
            }
            // cantos do sólido (base ± N·T/2): a base da Caixa é o plano médio e, na lateral, punha o rótulo do pilar no meio do desenho
            var sup = g.Pecas.Where(q => q.Grupo == "suporte").SelectMany(q => q.Base.SelectMany(p => new[] { p + q.N * q.T / 2, p - q.N * q.T / 2 })).ToList();
            if (sup.Count > 0 && g.NomeSuporte != "")
            {
                var ps = sup.Select(P).ToList();
                Caixa(g.NomeSuporte.Replace("x", "×"), new Point(ps.Min(q => q.X), Math.Max(30, ps.Min(q => q.Y) + 2)), TemaPlant.BTexto2);
            }
            var ch = g.Pecas.FirstOrDefault(q => q.Camada > 0 && Math.Abs(Vector3D.DotProduct(q.N, F)) > 0.85);
            if (ch != null && Vista == VistaLig.Elevacao)
            {
                var ps = ch.Base.Select(P).ToList();
                double alt = ch.Base.Max(q => Dot(q, U)) - ch.Base.Min(q => Dot(q, U));
                string nome = ch.Camada == 2 ? ch.Nome : "ch " + Num.Fmt(ch.T, Math.Abs(ch.T - Math.Round(ch.T)) < 0.05 ? 0 : 1) + " × " + Num.Fmt(alt, 0);
                Caixa(nome, new Point(ps.Min(q => q.X) + 4, Math.Max(30, ps.Min(q => q.Y) - 22)), ch.Falha ? TemaPlant.BErro : TemaPlant.BTexto1);
            }
            // W7-LIG: cantoneira sobre um gusset (a chapa já levou o rótulo): nome do perfil na ponta da barra
            var ca = ch != null && ch.Camada == 1 ? g.Pecas.FirstOrDefault(q => q.Camada == 2 && Math.Abs(Vector3D.DotProduct(q.N, F)) > 0.85) : null;
            if (ca != null && Vista == VistaLig.Elevacao)
            {
                var ps = ca.Base.Select(P).ToList();
                Caixa(ca.Nome.Replace(" x ", " × "), new Point(ps.Max(q => q.X) - 4, Math.Max(30, ps.Min(q => q.Y) - 22)), ca.Falha ? TemaPlant.BErro : TemaPlant.BRealceClaro, true);
            }
            var sd = g.Soldas.FirstOrDefault();
            if (sd != null && Vista != VistaLig.Planta)
            {
                var sp = sd.P.Select(P).ToList();
                double comp = 0; for (int i = 1; i < sp.Count; i++) comp += (sp[i] - sp[i - 1]).Length;
                if (comp > 10)
                {
                    var m = new Point((sp[0].X + sp[1].X) / 2, (sp[0].Y + sp[1].Y) / 2);
                    var cotov = new Point(m.X - 34, m.Y + 34); var fim = new Point(cotov.X - 46, cotov.Y);
                    Brush cor = sd.Falha ? TemaPlant.BErro : TemaPlant.BTexto2;
                    var pen = new Pen(cor, 1);
                    dc.DrawLine(pen, m, cotov); dc.DrawLine(pen, cotov, fim);
                    var v = m - cotov; v.Normalize(); var n = new Vector(-v.Y, v.X);
                    dc.DrawGeometry(cor, null, Poli(new[] { m, m - v * 7 + n * 3, m - v * 7 - n * 3 }));
                    double xm = (cotov.X + fim.X) / 2;
                    dc.DrawGeometry(cor, null, Poli(new[] { new Point(xm - 5, cotov.Y), new Point(xm + 5, cotov.Y), new Point(xm - 5, cotov.Y + 9) }));
                    var ft = Txt(Num.Fmt(sd.Perna, Math.Abs(sd.Perna - Math.Round(sd.Perna)) < 0.05 ? 0 : 1), 11, sd.Falha ? TemaPlant.BErro : TemaPlant.BTexto1, FontWeights.SemiBold);
                    dc.DrawText(ft, new Point(fim.X - ft.Width - 4, cotov.Y - ft.Height / 2));
                }
            }
        }
    }
}
