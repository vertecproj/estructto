// Ligações no 3D: a geometria do contrato (docs/v3/LIGACOES-CONTRATO.md, "geometria", mm, referencial local da ligação)
// lida para Ligacao3D, a conversão para o mundo e os exemplos feitos à mão (chapa de topo, chapa simples, gusset, placa de
// base com chumbadores) com que o renderer foi construído enquanto os tipos do motor não chegam. A placa de base do modelo 2D
// (PlacaBase/ResPlaca do Nucleo.cs) entra pelo mesmo caminho.
// Referencial local: origem no nó; x ao longo da barra que chega (saindo do nó); z = eixo forte/alto do perfil (o v da
// extrusão da Vista3D); y = x × z. Chapa: contorno (u, w) a partir de origem, espessura ao longo de +normal.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media.Media3D;
using Portico;
using Contorno = NovoFtool.GeometriaPerfis.Contorno;

namespace NovoFtool
{
    public sealed class Ligacao3D
    {
        public int No, Barra;   // nó e barra que chega (dá o referencial local)
        public string Nome = "";
        public sealed class Chapa { public string Nome; public Point[] Contorno; public Vector3D Origem, Normal, U; public double Espessura; }
        public sealed class Cantoneira { public string Perfil; public Vector3D Origem, Eixo, U; public double Comprimento; }
        public sealed class Parafuso { public double D, Comprimento; public Vector3D Origem, Eixo; }
        public sealed class Solda { public string Tipo = "filete"; public double Perna; public Vector3D[] Pontos; }
        public sealed class Recorte { public string Barra, Tipo; public double Folga = double.NaN, XPonta = double.NaN, Altura, Comprimento; }   // recuo: folga (vão até a face) ou x_ponta (posição absoluta da ponta); cope: altura e comprimento (ponytail: cope não é desenhado)
        public readonly List<Chapa> Chapas = new List<Chapa>();
        public readonly List<Cantoneira> Cantoneiras = new List<Cantoneira>();
        public readonly List<Parafuso> Parafusos = new List<Parafuso>();
        public readonly List<Solda> Soldas = new List<Solda>();
        public readonly List<Recorte> Recortes = new List<Recorte>();

        // Recorte "recuo" da barra dada ("viga" = a que chega; "pilar" = a contínua). Folga (m) = vão entre a ponta e a face
        // da peça contínua; XPonta (m) = posição da ponta a partir do nó (o motor manda x_ponta). NaN = sem recorte: vale a regra das juntas.
        public double RecuoDe(string barra) => Recortes.Where(r => r.Tipo == "recuo" && (r.Barra ?? "viga") == barra && !double.IsNaN(r.Folga)).Select(r => r.Folga / 1000).DefaultIfEmpty(double.NaN).First();
        public double PontaDe(string barra) => Recortes.Where(r => r.Tipo == "recuo" && (r.Barra ?? "viga") == barra && !double.IsNaN(r.XPonta)).Select(r => r.XPonta / 1000).DefaultIfEmpty(double.NaN).First();

        static Vector3D V3(JsonNode n) { var a = n?.AsArray(); return a == null || a.Count < 3 ? new Vector3D() : new Vector3D((double)a[0], (double)a[1], (double)a[2]); }
        static double D(JsonNode n, string k, double padrao = 0) { try { return n?[k] is JsonValue v && v.TryGetValue(out double d) ? d : padrao; } catch { return padrao; } }
        static JsonArray A(Vector3D v) => new JsonArray(v.X, v.Y, v.Z);

        // Lê o "geometria" do resultado de uma ligação do motor (contrato). Chaves ausentes = listas vazias.
        public static Ligacao3D DoJson(int no, int barra, JsonObject g, string nome = "")
        {
            var l = new Ligacao3D { No = no, Barra = barra, Nome = nome ?? "" };
            if (g == null) return l;
            foreach (var c in g["chapas"]?.AsArray() ?? new JsonArray())
                l.Chapas.Add(new Chapa { Nome = (string)c["nome"], Origem = V3(c["origem"]), Normal = V3(c["normal"]), U = V3(c["u"]), Espessura = D(c, "espessura"),
                                         Contorno = (c["contorno"]?.AsArray() ?? new JsonArray()).Select(p => new Point((double)p[0], (double)p[1])).ToArray() });
            foreach (var c in g["cantoneiras"]?.AsArray() ?? new JsonArray())
                l.Cantoneiras.Add(new Cantoneira { Perfil = (string)c["perfil"], Origem = V3(c["origem"]), Eixo = V3(c["eixo"]), U = V3(c["u"]), Comprimento = D(c, "comprimento") });
            foreach (var p in g["parafusos"]?.AsArray() ?? new JsonArray())
                l.Parafusos.Add(new Parafuso { D = D(p, "d"), Comprimento = D(p, "comprimento"), Origem = V3(p["origem"]), Eixo = V3(p["eixo"]) });
            foreach (var s in g["soldas"]?.AsArray() ?? new JsonArray())
                l.Soldas.Add(new Solda { Tipo = (string)s["tipo"] ?? "filete", Perna = D(s, "perna"), Pontos = (s["pontos"]?.AsArray() ?? new JsonArray()).Select(V3).ToArray() });
            foreach (var r in g["recortes"]?.AsArray() ?? new JsonArray())
            {
                var pr = r["parametros"];
                l.Recortes.Add(new Recorte { Barra = (string)r["barra"], Tipo = (string)r["tipo"], Folga = D(pr, "folga", double.NaN), XPonta = D(pr, "x_ponta", double.NaN), Altura = D(pr, "altura"), Comprimento = D(pr, "comprimento") });
            }
            return l;
        }

        // Escreve no formato do contrato (os exemplos passam por aqui e voltam por DoJson: o renderer só lê o contrato).
        public JsonObject ParaJson()
        {
            var g = new JsonObject
            {
                ["chapas"] = new JsonArray(Chapas.Select(c => (JsonNode)new JsonObject { ["nome"] = c.Nome, ["contorno"] = new JsonArray(c.Contorno.Select(p => (JsonNode)new JsonArray(p.X, p.Y)).ToArray()),
                                                                                    ["origem"] = A(c.Origem), ["normal"] = A(c.Normal), ["u"] = A(c.U), ["espessura"] = c.Espessura }).ToArray()),
                ["cantoneiras"] = new JsonArray(Cantoneiras.Select(c => (JsonNode)new JsonObject { ["perfil"] = c.Perfil, ["origem"] = A(c.Origem), ["eixo"] = A(c.Eixo), ["u"] = A(c.U), ["comprimento"] = c.Comprimento }).ToArray()),
                ["parafusos"] = new JsonArray(Parafusos.Select(p => (JsonNode)new JsonObject { ["d"] = p.D, ["comprimento"] = p.Comprimento, ["origem"] = A(p.Origem), ["eixo"] = A(p.Eixo) }).ToArray()),
                ["soldas"] = new JsonArray(Soldas.Select(s => (JsonNode)new JsonObject { ["tipo"] = s.Tipo, ["perna"] = s.Perna, ["pontos"] = new JsonArray(s.Pontos.Select(p => (JsonNode)A(p)).ToArray()) }).ToArray()),
                ["recortes"] = new JsonArray(Recortes.Select(r => (JsonNode)new JsonObject { ["barra"] = r.Barra, ["tipo"] = r.Tipo,
                                                                                        ["parametros"] = r.Tipo != "recuo" ? new JsonObject { ["altura"] = r.Altura, ["comprimento"] = r.Comprimento } : double.IsNaN(r.XPonta) ? new JsonObject { ["folga"] = r.Folga } : new JsonObject { ["x_ponta"] = r.XPonta } }).ToArray()),
            };
            return g;
        }
    }

    // Exemplos no formato do contrato. Dimensões em mm; o perfil da barra que chega vem do Contorno (m).
    public static class LigacoesAmostra
    {
        static Point[] Ret(double x0, double y0, double x1, double y1) => new[] { new Point(x0, y0), new Point(x1, y0), new Point(x1, y1), new Point(x0, y1) };

        // Contorno externo do perfil (m) no referencial local da ligação (mm): y = s·u, z = v. s = -1 na ponta i, +1 na ponta j.
        static Vector3D[] ContornoLocal(Contorno f, double x, double s)
        {
            Point[] l = f.Lacos[0];
            return l.Select(p => new Vector3D(x, s * p.X * 1000, p.Y * 1000)).Concat(new[] { new Vector3D(x, s * l[0].X * 1000, l[0].Y * 1000) }).ToArray();
        }

        // Chapa de topo estendida viga-pilar (mesa do pilar): chapa na face do pilar, 8 parafusos, solda em todo o contorno da
        // viga, enrijecedores no pilar nos níveis das mesas. face = distância do nó à face do pilar (m).
        public static Ligacao3D ChapaDeTopo(int no, Barra3D viga, bool emI, Contorno fViga, double face, double tfPilar, double faceY)
        {
            double H = fViga.H * 1000, B = fViga.B * 1000, tf = fViga.Tf * 1000, s = emI ? -1 : 1;
            double esp = H < 300 ? 16 : 20, ext = H < 400 ? 60 : 80, d = H < 300 ? 16 : 20;
            double bp = Math.Max(B, 150), hp = H + 2 * ext, x0 = Math.Round(face * 1000, 1);
            var l = new Ligacao3D { No = no, Barra = viga.Id, Nome = "Chapa de topo estendida" };
            l.Chapas.Add(new Ligacao3D.Chapa { Nome = "chapa de topo", Origem = new Vector3D(x0, -bp / 2, -hp / 2), Normal = new Vector3D(1, 0, 0), U = new Vector3D(0, 1, 0), Espessura = esp, Contorno = Ret(0, 0, bp, hp) });
            double g = bp - 70;
            foreach (double z in new[] { H / 2 + ext / 2, H / 2 - tf - 35, -(H / 2 - tf - 35), -(H / 2 + ext / 2) })
                foreach (double y in new[] { -g / 2, g / 2 })
                    l.Parafusos.Add(new Ligacao3D.Parafuso { D = d, Comprimento = esp + tfPilar * 1000 + 12, Origem = new Vector3D(x0 + esp, y, z), Eixo = new Vector3D(-1, 0, 0) });
            l.Soldas.Add(new Ligacao3D.Solda { Perna = 6, Pontos = ContornoLocal(fViga, x0 + esp, s) });
            double tfc = tfPilar * 1000, hw = Math.Max(40, faceY * 1000 - tfc);
            foreach (double z in new[] { H / 2 - tf / 2, -(H / 2 - tf / 2) })   // enrijecedores entre as mesas do pilar
                l.Chapas.Add(new Ligacao3D.Chapa { Nome = "enrijecedor", Origem = new Vector3D(-(x0 - tfc), -hw, z - 5), Normal = new Vector3D(0, 0, 1), U = new Vector3D(1, 0, 0), Espessura = 10, Contorno = Ret(0, 0, 2 * (x0 - tfc), 2 * hw) });
            l.Recortes.Add(new Ligacao3D.Recorte { Barra = "viga", Tipo = "recuo", Folga = esp });
            return l;
        }

        // Chapa simples (shear tab) soldada na peça contínua e parafusada na alma da viga (3 parafusos).
        public static Ligacao3D ChapaSimples(int no, Barra3D viga, bool emI, Contorno fViga, double face)
        {
            double H = fViga.H * 1000, tw = fViga.Tw * 1000, x0 = Math.Round(face * 1000, 1), esp = 10, lp = 100, hp = Math.Max(90, H - 80);
            var l = new Ligacao3D { No = no, Barra = viga.Id, Nome = "Chapa simples" };
            l.Chapas.Add(new Ligacao3D.Chapa { Nome = "chapa simples", Origem = new Vector3D(x0, tw / 2, -hp / 2), Normal = new Vector3D(0, 1, 0), U = new Vector3D(0, 0, 1), Espessura = esp, Contorno = Ret(0, 0, hp, lp) });
            int n = hp >= 170 ? 3 : 2; double passo = Math.Min(70, (hp - 60) / Math.Max(1, n - 1));
            for (int k = 0; k < n; k++)
                l.Parafusos.Add(new Ligacao3D.Parafuso { D = 16, Comprimento = esp + tw + 14, Origem = new Vector3D(x0 + 50, tw / 2 + esp, (k - (n - 1) / 2.0) * passo), Eixo = new Vector3D(0, -1, 0) });
            l.Soldas.Add(new Ligacao3D.Solda { Perna = 6, Pontos = new[] { new Vector3D(x0, tw / 2 + esp / 2, -hp / 2), new Vector3D(x0, tw / 2 + esp / 2, hp / 2) } });
            l.Recortes.Add(new Ligacao3D.Recorte { Barra = "viga", Tipo = "recuo", Folga = 12 });
            return l;
        }

        // Gusset de diagonal: chapa no plano da diagonal, parafusos ao longo dela, solda na peça contínua. recuo = recuo da diagonal (m).
        public static Ligacao3D Gusset(int no, Barra3D diag, bool emI, Contorno fDiag, double recuo)
        {
            double B = Math.Max(fDiag.B, fDiag.H) * 1000, t = Math.Max(fDiag.Tw, 0.006) * 1000, esp = 10;
            double xr = Math.Round(recuo * 1000, 1), xb = 0.4 * xr, x1 = xr + 220;
            var l = new Ligacao3D { No = no, Barra = diag.Id, Nome = "Gusset" };
            l.Chapas.Add(new Ligacao3D.Chapa { Nome = "gusset", Origem = new Vector3D(0, esp / 2, 0), Normal = new Vector3D(0, -1, 0), U = new Vector3D(1, 0, 0), Espessura = esp,
                                               Contorno = new[] { new Point(xb, -(B / 2 + 90)), new Point(x1, -(B / 2 + 30)), new Point(x1, B / 2 + 30), new Point(xb, B / 2 + 90) } });
            for (int k = 0; k < 3; k++)
                l.Parafusos.Add(new Ligacao3D.Parafuso { D = 16, Comprimento = esp + t + 20, Origem = new Vector3D(xr + 40 + 60 * k, esp / 2 + t / 2 + 2, 0), Eixo = new Vector3D(0, -1, 0) });
            l.Soldas.Add(new Ligacao3D.Solda { Perna = 6, Pontos = new[] { new Vector3D(xb, 0, -(B / 2 + 90)), new Vector3D(xb, 0, B / 2 + 90) } });
            l.Recortes.Add(new Ligacao3D.Recorte { Barra = "viga", Tipo = "recuo", Folga = xr });
            return l;
        }

        // Placa de base com chumbadores: chapa H × B × esp sob o pilar (x = 0..esp), chumbadores verticais com porca e arruela
        // em cima, solda em todo o contorno do pilar. furos (hy ao longo de H, hz ao longo de B) em mm. H fica no eixo z local
        // (forte) salvo deitado (pilar de eixo fraco no plano: H no eixo y).
        public static Ligacao3D PlacaDeBase(int no, Barra3D pilar, bool emI, Contorno fPilar, double H, double B, double esp, double d, IList<(double hy, double hz)> furos, double solda, bool deitado = false)
        {
            double s = emI ? -1 : 1;
            var l = new Ligacao3D { No = no, Barra = pilar.Id, Nome = "Placa de base" };
            double ly = deitado ? H : B, lz = deitado ? B : H;
            l.Chapas.Add(new Ligacao3D.Chapa { Nome = "placa de base", Origem = new Vector3D(0, -ly / 2, -lz / 2), Normal = new Vector3D(1, 0, 0), U = new Vector3D(0, 1, 0), Espessura = esp, Contorno = Ret(0, 0, ly, lz) });
            foreach (var (hy, hz) in furos)
                l.Parafusos.Add(new Ligacao3D.Parafuso { D = d, Comprimento = 200 + esp + 0.8 * d + 8, Origem = new Vector3D(-200, deitado ? hy : hz, deitado ? hz : hy), Eixo = new Vector3D(1, 0, 0) });
            l.Soldas.Add(new Ligacao3D.Solda { Perna = solda > 0 ? solda : 8, Pontos = ContornoLocal(fPilar, esp, s) });
            l.Recortes.Add(new Ligacao3D.Recorte { Barra = "pilar", Tipo = "recuo", Folga = esp });
            return l;
        }

        // Placa de base do modelo 2D (Nucleo.cs: PlacaBase do nó + ResPlaca calculada).
        public static Ligacao3D DaPlaca2D(int no, Barra3D pilar, bool emI, Contorno fPilar, PlacaBase p, ResPlaca r)
        {
            if (p == null || r == null || r.Erro != "" || r.H <= 0 || r.B <= 0) return null;
            var furos = r.Paraf.Select(q => (q[0], q[1])).ToList();
            return PlacaDeBase(no, pilar, emI, fPilar, r.H, r.B, p.Esp > 0 ? p.Esp : 20, r.Dia > 0 ? r.Dia : 16, furos, p.Solda, r.Deitado);
        }

        // Exemplos automáticos para um modelo sem ligações do motor (capturas, ESTRUCTTO_3D_AMOSTRAS=1): placa de base em todo
        // apoio com pilar; chapa de topo onde uma viga para num pilar; chapa simples onde para em outra viga; gusset nas diagonais.
        // folga(b, emI) = folga da regra das juntas, descontada para achar a face.
        public static List<Ligacao3D> Gerar(Modelo3D mod, Dictionary<(Barra3D, bool), Corte> cortes, Func<Barra3D, Contorno> forma, Func<Barra3D, bool, double> folga)
        {
            var r = new List<Ligacao3D>();
            var nos = mod.Nos.ToDictionary(n => n.Id);
            var vert = new HashSet<Barra3D>(mod.Barras.Where(b => nos.ContainsKey(b.Ni) && nos.ContainsKey(b.Nj) && Math.Abs(nos[b.Nj].Z - nos[b.Ni].Z) > 0.95 * (P(nos[b.Nj]) - P(nos[b.Ni])).Length));
            foreach (var kv in cortes)
            {
                var (b, emI) = kv.Key; Corte c = kv.Value;
                if (c.Esquadria || c.Contra == null) continue;
                Contorno f = forma(b); if (f.Concreto || forma(c.Contra).Concreto) continue;
                int no = emI ? b.Ni : b.Nj;
                double face = c.Recuo - folga(b, emI);
                if (vert.Contains(b)) continue;
                bool horiz = Math.Abs(nos[b.Nj].Z - nos[b.Ni].Z) < 0.2 * (P(nos[b.Nj]) - P(nos[b.Ni])).Length;
                if (!horiz) r.Add(Gusset(no, b, emI, f, c.Recuo));
                else if (vert.Contains(c.Contra)) { Contorno fp = forma(c.Contra); r.Add(ChapaDeTopo(no, b, emI, f, face, fp.Tf, Math.Max(fp.B, fp.H) / 2)); }
                else r.Add(ChapaSimples(no, b, emI, f, face));
            }
            foreach (No3D n in mod.Nos.Where(n => n.Apoios.Any(x => x)))
            {
                Barra3D p = mod.Barras.FirstOrDefault(b => vert.Contains(b) && (b.Ni == n.Id || b.Nj == n.Id));
                if (p == null) continue;
                Contorno f = forma(p); if (f.Concreto) continue;
                double H = Math.Ceiling((f.H * 1000 + 2 * 60) / 10) * 10, B = Math.Ceiling((Math.Max(f.B, 0.15) * 1000 + 2 * 60) / 10) * 10, borda = 40;
                var furos = new List<(double, double)> { (-H / 2 + borda, -B / 2 + borda), (H / 2 - borda, -B / 2 + borda), (H / 2 - borda, B / 2 - borda), (-H / 2 + borda, B / 2 - borda) };
                r.Add(PlacaDeBase(n.Id, p, p.Ni == n.Id, f, H, B, 25, 20, furos, 8));
            }
            return r;
        }

        static Point3D P(No3D n) => new Point3D(n.X, n.Y, n.Z);

        // Autoverificação: ida e volta pelo JSON do contrato e geometria de uma chapa de topo.
        public static void Testar(Action<bool, string> afirmar)
        {
            Contorno f = GeometriaPerfis.CriarContorno(new Perfil3D { Forma = "I", H = 0.31, B = 0.165, Tw = 0.0066, Tf = 0.0097 }, 0.1);
            var viga = new Barra3D { Id = 7, Ni = 1, Nj = 2 };
            Ligacao3D l = ChapaDeTopo(2, viga, false, f, 0.125, 0.012, 0.125);
            afirmar(l.Chapas.Count == 3 && l.Parafusos.Count == 8 && l.Soldas.Count == 1 && Math.Abs(l.RecuoDe("viga") - 0.020) < 1e-9, "chapa de topo: 3 chapas, 8 parafusos, 1 solda, recuo = espessura 20 mm");
            afirmar(Math.Abs(l.Chapas[0].Origem.X - 125) < 1e-9 && l.Parafusos.All(p => Math.Abs(p.Origem.X - 145) < 1e-9 && p.Eixo.X == -1), "chapa na face do pilar (x = 125), parafusos na face externa (x = 145) apontando para o pilar");
            Ligacao3D v = Ligacao3D.DoJson(l.No, l.Barra, l.ParaJson(), l.Nome);
            afirmar(v.Chapas.Count == l.Chapas.Count && v.Parafusos.Count == l.Parafusos.Count && v.Soldas[0].Pontos.Length == l.Soldas[0].Pontos.Length && Math.Abs(v.RecuoDe("viga") - l.RecuoDe("viga")) < 1e-12
                    && v.Chapas[0].Contorno.SequenceEqual(l.Chapas[0].Contorno), "ida e volta pelo JSON do contrato");
            Ligacao3D pb = PlacaDeBase(1, new Barra3D { Id = 1, Ni = 1, Nj = 2 }, true, f, 400, 300, 25, 20, new[] { (-150.0, -100.0), (150.0, 100.0) }, 8);
            afirmar(pb.Parafusos.Count == 2 && pb.Parafusos[0].Origem.Z == -150 && pb.Parafusos[0].Origem.Y == -100 && Math.Abs(pb.RecuoDe("pilar") - 0.025) < 1e-12 && double.IsNaN(pb.RecuoDe("viga")), "placa de base: H no eixo z local, chumbadores verticais, pilar recua a espessura");
        }
    }
}
