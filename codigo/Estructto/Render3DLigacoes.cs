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
    // Peça de chapa/cantoneira escolhível na cena (Render3D.Marcar): o clique leva ao campo da ligação (TagLig, JanelaLigacaoProjeto)
    public sealed class PecaLig { public Ligacao3D Lig; public string Tag; public double T; }   // T: distância do olho (só parafuso)

    public sealed class Ligacao3D
    {
        public int No, Barra;   // nó e barra que chega (dá o referencial local)
        public string Nome = "", Grupo = "";   // Grupo: id do grupo de ligações do motor (clique na peça abre o projetista dele)
        // Proposta: o grupo não tem projeto; é a geometria padrão sugerida (desenhada esmaecida, com o chip "Proposta" na vista).
        // Pendente: proposta sem geometria do motor; o Render3D faz a do tipo de nó (LigacoesAmostra.Proposta) com as juntas.
        public bool Proposta; public string Pendente;
        // Projeto ligacao_tracao num gusset (Render3DMotor): desenhado pelo gusset local com t, d (mm), nº de parafusos e passo s (mm) do projeto
        public (double t, double d, int n, double s)? Tracao;
        public sealed class Chapa
        {
            public string Nome, Campo; public Point[] Contorno; public Vector3D Origem, Normal, U; public double Espessura;
            public Vector3D N1 { get { Vector3D n = Normal; n.Normalize(); return n; } }
            public Vector3D U1 { get { Vector3D u = U; u.Normalize(); return u; } }
            public Point Q(Vector3D p) => new Point(Vector3D.DotProduct(p - Origem, U1), Vector3D.DotProduct(p - Origem, Vector3D.CrossProduct(N1, U1)));   // (u, w) do contorno
            public double H(Vector3D p) => Vector3D.DotProduct(p - Origem, N1);   // altura sobre a face de origem (0 .. Espessura = dentro)
            public double Borda(Point q)   // distância (mm) ao contorno
            {
                double m = double.MaxValue;
                for (int k = 0; k < Contorno.Length; k++)
                {
                    Point a = Contorno[k], b = Contorno[(k + 1) % Contorno.Length]; double dx = b.X - a.X, dy = b.Y - a.Y, L2 = dx * dx + dy * dy; if (L2 < 1e-9) continue;
                    double t = Math.Max(0, Math.Min(1, ((q.X - a.X) * dx + (q.Y - a.Y) * dy) / L2));
                    m = Math.Min(m, Math.Sqrt(Math.Pow(a.X + t * dx - q.X, 2) + Math.Pow(a.Y + t * dy - q.Y, 2)));
                }
                return m;
            }
        }
        // Onde a solda assenta (docs/v3/GEOMETRIA-LIGACOES.md), no referencial local (mm):
        // (1) borda de uma chapa (enrijecedor, nervura; pontas e meio do cordão no contorno, dentro da espessura): ax = para dentro
        //     da chapa, no plano dela; lados = ±normal (perna que assenta na peça de apoio) com o deslocamento da raiz até a face
        //     (o motor manda o cordão no plano médio: a solda ficava dentro da chapa, e o cordão ∥ à barra nem era desenhado);
        // (2) face de uma chapa não paralela à barra (placa de base, chapa de topo): face = essa chapa (o Render3D recorta o cordão
        //     pelo contorno dela: solda não fica no ar além da borda); (3) nenhum: (null, null, null), como antes.
        public static (Vector3D? ax, List<(Vector3D n, double desl)> lados, Chapa face) Apoio(Ligacao3D l, Solda sd)
        {
            var pts = sd.Pontos; const double tol = 1.0;
            if (pts == null || pts.Length < 2) return (null, null, null);
            Vector3D meio = (pts[0] + pts[pts.Length - 1]) / 2;
            foreach (bool borda in new[] { true, false })   // borda antes: o enrijecedor encostado na face da chapa de topo é borda dele
                foreach (var ch in l.Chapas)
                {
                    if (ch.Contorno == null || ch.Contorno.Length < 3 || ch.Normal.Length < 0.5 || ch.U.Length < 0.5) continue;
                    double esp = ch.Espessura;
                    if (!pts.All(p => ch.H(p) > -tol && ch.H(p) < esp + tol)) continue;
                    if (!borda)
                    {
                        bool naFace = pts.All(p => Math.Abs(ch.H(p)) < tol) || pts.All(p => Math.Abs(ch.H(p) - esp) < tol);
                        if (naFace && Math.Abs(ch.N1.X) > 0.2) return (null, null, ch);
                        continue;
                    }
                    if (!pts.All(p => ch.Borda(ch.Q(p)) < tol) || ch.Borda(ch.Q(meio)) >= tol) continue;
                    Vector3D sg = pts[1] - pts[0]; if (sg.Length < 1e-6) continue; sg.Normalize();
                    Vector3D n = ch.N1, ax = Vector3D.CrossProduct(n, sg); if (ax.Length < 1e-6) continue; ax.Normalize();
                    Vector3D cw = Vector3D.CrossProduct(n, ch.U1);
                    double cx = ch.Contorno.Average(q => q.X), cy = ch.Contorno.Average(q => q.Y);
                    if (Vector3D.DotProduct(ch.Origem + ch.U1 * cx + cw * cy - pts[0], ax) < 0) ax = -ax;   // para dentro da chapa
                    double h = pts.Average(p => ch.H(p));
                    var lados = Math.Abs(h) < tol ? new List<(Vector3D, double)> { (-n, 0.0) } : Math.Abs(h - esp) < tol ? new List<(Vector3D, double)> { (n, 0.0) }
                              : new List<(Vector3D, double)> { (n, esp - h), (-n, h) };
                    return (ax, lados, null);
                }
            return (null, null, null);
        }
        public sealed class Cantoneira { public string Perfil, Campo; public Vector3D Origem, Eixo, U; public double Comprimento; }
        public sealed class Parafuso { public string Campo; public double D, Comprimento, Pega = double.NaN; public Vector3D Origem, Eixo; public double[][] Pilha = new double[0][]; }   // Pilha: [t0, t1] (mm ao longo do eixo) das peças apertadas fora de `chapas` (aba da barra, cantoneira); pega: espessura apertada (NaN = estimada do comprimento)
        public sealed class Solda { public string Tipo = "filete"; public double Perna; public Vector3D[] Pontos; }
        public sealed class Recorte { public string Barra, Tipo; public double Folga = double.NaN, XPonta = double.NaN, Altura, Comprimento; public string Posicao = "topo"; }   // recuo: folga (vão até a face) ou x_ponta (posição absoluta da ponta); cope: posição (topo/base), altura e comprimento a partir da ponta (Render3D desenha)
        public readonly List<Chapa> Chapas = new List<Chapa>();
        public readonly List<Cantoneira> Cantoneiras = new List<Cantoneira>();
        // Direção (local) da aresta do contorno da chapa mais próxima do parafuso, entre as chapas com normal ∥ eixo (hexágono alinhado a ela).
        public Vector3D? ArestaMaisPerto(Parafuso pf)
        {
            Vector3D e = pf.Eixo; if (e.Length < 1e-9) return null; e.Normalize();
            double melhor = double.PositiveInfinity; Vector3D? r = null;
            foreach (var ch in Chapas)
            {
                Vector3D n = ch.Normal, u = ch.U; if (n.Length < 0.5 || u.Length < 0.5 || ch.Contorno == null || ch.Contorno.Length < 3) continue;
                n.Normalize(); u.Normalize(); if (Math.Abs(Vector3D.DotProduct(n, e)) < 0.99) continue;
                Vector3D w = Vector3D.CrossProduct(n, u); Vector3D rel = pf.Origem - ch.Origem;
                var q = new Point(Vector3D.DotProduct(rel, u), Vector3D.DotProduct(rel, w));
                for (int k = 0; k < ch.Contorno.Length; k++)
                {
                    Point a = ch.Contorno[k], b = ch.Contorno[(k + 1) % ch.Contorno.Length]; double dx = b.X - a.X, dy = b.Y - a.Y, L2 = dx * dx + dy * dy; if (L2 < 1e-9) continue;
                    double t = Math.Max(0, Math.Min(1, ((q.X - a.X) * dx + (q.Y - a.Y) * dy) / L2)), dist = Math.Sqrt(Math.Pow(a.X + t * dx - q.X, 2) + Math.Pow(a.Y + t * dy - q.Y, 2));
                    if (dist < melhor - 1e-9) { melhor = dist; r = u * dx + w * dy; }
                }
            }
            return r;
        }
        public readonly List<Parafuso> Parafusos = new List<Parafuso>();
        public readonly List<Solda> Soldas = new List<Solda>();
        public readonly List<Recorte> Recortes = new List<Recorte>();
        // Ponta da barra que chega calculada pelo motor (geometria.pontas, barra 0; docs/v3/PONTAS-CONTRATO.md): plano de corte no
        // referencial local da ligação, em m, e os recortes (cope com Posicao topo/base/esq/dir e Altura/Comprimento em mm,
        // angulo com Folga em mm, boca_peixe). null = modelo/ligação sem pontas: vale a regra das juntas (Render3DGeometria.Juntas).
        public sealed class PontaMotor { public Vector3D P, N; public readonly List<Recorte> Recortes = new List<Recorte>(); }
        public PontaMotor Ponta;
        // G5 (opcionais, tolerante se faltarem): excluir_interferencia (barra curta que o motor marcou: fora do teste de
        // interferência); gusset com apoio_dir (direção do apoio no referencial local) e lado (+1/−1: lado y da chapa). Com
        // apoio_dir o Render3D usa o referencial do motor como veio, sem adivinhar.
        public bool Excluir; public Vector3D? ApoioDir; public double Lado = double.NaN;
        public Vector3D? ApoioMundo; public double DeslU;
        // Folga no topo do pilar (mm): quanto o pilar fica abaixo do topo da chapa de topo da viga que chega no topo dele
        // (geometria.folga_topo, opcional). NaN/0 = pilar até o topo da chapa (padrão; docs/v3/GEOMETRIA-LIGACOES.md).
        public double FolgaTopo = double.NaN;
        public double XMax = double.NaN;   // G6 r4: x_max (mm) que o motor recebeu; a guarda AparaEm usa o mesmo limite
        // G6 r3: guarda. Corta as chapas em x = xMax (mm, local ao longo da barra; Sutherland–Hodgman) só se nenhum parafuso
        // passa de xMax − 1,5·d (borda mínima ~Tab. 14); senão não mexe e devolve false (a ligação fica pendente, com motivo).
        public static bool AparaEm(Ligacao3D l, double xMax)
        {
            double X(Chapa ch, Point p) { Vector3D w = Vector3D.CrossProduct(ch.Normal, ch.U); return ch.Origem.X + ch.U.X * p.X + w.X * p.Y; }
            if (!l.Chapas.Any(ch => ch.Contorno.Any(p => X(ch, p) > xMax + 1e-6))) return true;   // cabe: nada a fazer
            if (l.Parafusos.Any(pf => pf.Origem.X > xMax - 1.5 * pf.D)) return false;
            foreach (var ch in l.Chapas)
            {
                var o = new List<Point>(); int n = ch.Contorno.Length;
                for (int k = 0; k < n; k++)
                {
                    Point p = ch.Contorno[k], q = ch.Contorno[(k + 1) % n]; double xp = X(ch, p), xq = X(ch, q);
                    if (xp <= xMax) o.Add(p);
                    if ((xp <= xMax) != (xq <= xMax)) { double t = (xMax - xp) / (xq - xp); o.Add(new Point(p.X + t * (q.X - p.X), p.Y + t * (q.Y - p.Y))); }
                }
                ch.Contorno = o.ToArray();
            }
            l.Chapas.RemoveAll(ch => ch.Contorno.Length < 3);
            return true;
        }
        // G6: y do contrato do gusset no mundo (normal da chapa), com x = eixo da diagonal saindo do nó: apoio_dir local (ax, 0, az) =
        // eixo do apoio no mundo → z = (S − (S·x)x)/az, y = x × z. null sem apoio_dir/apoio no mundo.
        public Vector3D? YGusset(Vector3D x)
        {
            if (!(ApoioDir is Vector3D ad) || !(ApoioMundo is Vector3D sm) || Math.Abs(ad.Z) < 1e-6) return null;
            sm.Normalize(); if (Math.Sign(Vector3D.DotProduct(sm, x)) != Math.Sign(ad.X) && Math.Abs(ad.X) > 1e-9) sm = -sm;
            Vector3D zp = sm - Vector3D.DotProduct(sm, x) * x;
            if (zp.Length < 1e-6) return null;
            zp.Normalize(); if (ad.Z < 0) zp = -zp;
            return Vector3D.CrossProduct(x, zp);
        }   // DeslU (m): centroide do U − centro do envolvente, em u   // G6: eixo do apoio no mundo (Render3DMotor); com ApoioDir fixa a rotação do gusset em torno da diagonal

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
                l.Chapas.Add(new Chapa { Nome = (string)c["nome"], Campo = (string)c["campo"], Origem = V3(c["origem"]), Normal = V3(c["normal"]), U = V3(c["u"]), Espessura = D(c, "espessura"),
                                         Contorno = (c["contorno"]?.AsArray() ?? new JsonArray()).Select(p => new Point((double)p[0], (double)p[1])).ToArray() });
            foreach (var c in g["cantoneiras"]?.AsArray() ?? new JsonArray())
                l.Cantoneiras.Add(new Cantoneira { Perfil = (string)c["perfil"], Campo = (string)c["campo"], Origem = V3(c["origem"]), Eixo = V3(c["eixo"]), U = V3(c["u"]), Comprimento = D(c, "comprimento") });
            foreach (var p in g["parafusos"]?.AsArray() ?? new JsonArray())
                l.Parafusos.Add(new Parafuso { Campo = (string)p["campo"], D = D(p, "d"), Comprimento = D(p, "comprimento"), Pega = D(p, "pega", double.NaN), Origem = V3(p["origem"]), Eixo = V3(p["eixo"]), Pilha = (p["pilha"]?.AsArray() ?? new JsonArray()).Select(x => new[] { (double)x[0], (double)x[1] }).ToArray() });
            foreach (var s in g["soldas"]?.AsArray() ?? new JsonArray())
                l.Soldas.Add(new Solda { Tipo = (string)s["tipo"] ?? "filete", Perna = D(s, "perna"), Pontos = (s["pontos"]?.AsArray() ?? new JsonArray()).Select(V3).ToArray() });
            foreach (var r in g["recortes"]?.AsArray() ?? new JsonArray())
            {
                var pr = r["parametros"];
                l.Recortes.Add(new Recorte { Barra = (string)r["barra"], Tipo = (string)r["tipo"], Folga = D(pr, "folga", double.NaN), XPonta = D(pr, "x_ponta", double.NaN), Altura = D(pr, "altura"), Comprimento = D(pr, "comprimento"), Posicao = (string)pr?["posicao"] ?? "topo" });
            }
            var pm = (g["pontas"] as JsonArray)?.FirstOrDefault(p => D(p, "barra", -1) == 0 && p?["plano"] != null);
            bool B(JsonNode n, string k) { try { return n?[k] is JsonValue v && v.TryGetValue(out bool x) && x; } catch { return false; } }
            var gus = (g["chapas"] as JsonArray)?.FirstOrDefault(c => (string)c?["nome"] == "gusset");
            l.Excluir = B(g, "excluir_interferencia") || B(pm, "excluir_interferencia");
            l.FolgaTopo = D(g, "folga_topo", double.NaN);
            foreach (var n in new[] { g, gus, pm })
            {
                if (n?["apoio_dir"] is JsonArray ad && ad.Count >= 3) l.ApoioDir = V3(ad);
                var ld = n?["lado"];
                if (ld is JsonValue lv) { if (lv.TryGetValue(out double x)) l.Lado = Math.Sign(x); else if (lv.TryGetValue(out string t)) l.Lado = t.StartsWith("-") ? -1 : 1; }
            }
            if (pm != null)
            {
                l.Ponta = new PontaMotor { P = V3(pm["plano"]["ponto"]), N = V3(pm["plano"]["normal"]) };
                foreach (var r in pm["recortes"]?.AsArray() ?? new JsonArray())
                {
                    string t = (string)r["tipo"], face = (string)r["face"];
                    l.Ponta.Recortes.Add(new Recorte { Barra = "viga", Tipo = t, Altura = (t == "rasgo" ? D(r, "largura") : D(r, "altura")) * 1000, Comprimento = D(r, "comprimento") * 1000, Folga = D(r, "folga", double.NaN) * 1000,   // rasgo: Altura = largura (mm)
                                                       Posicao = face == "sup" ? "topo" : face == "inf" ? "base" : face ?? "topo" });
                }
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
                ["parafusos"] = new JsonArray(Parafusos.Select(p => { var o = new JsonObject { ["d"] = p.D, ["comprimento"] = p.Comprimento, ["origem"] = A(p.Origem), ["eixo"] = A(p.Eixo) }; if (!double.IsNaN(p.Pega)) o["pega"] = p.Pega; if (p.Pilha.Length > 0) o["pilha"] = new JsonArray(p.Pilha.Select(x => (JsonNode)new JsonArray(x[0], x[1])).ToArray()); return (JsonNode)o; }).ToArray()),
                ["soldas"] = new JsonArray(Soldas.Select(s => (JsonNode)new JsonObject { ["tipo"] = s.Tipo, ["perna"] = s.Perna, ["pontos"] = new JsonArray(s.Pontos.Select(p => (JsonNode)A(p)).ToArray()) }).ToArray()),
                ["recortes"] = new JsonArray(Recortes.Select(r => (JsonNode)new JsonObject { ["barra"] = r.Barra, ["tipo"] = r.Tipo,
                                                                                        ["parametros"] = r.Tipo != "recuo" ? new JsonObject { ["posicao"] = r.Posicao, ["altura"] = r.Altura, ["comprimento"] = r.Comprimento } : double.IsNaN(r.XPonta) ? new JsonObject { ["folga"] = r.Folga } : new JsonObject { ["x_ponta"] = r.XPonta } }).ToArray()),
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
        public static Ligacao3D ChapaDeTopo(int no, Barra3D viga, bool emI, Contorno fViga, double face, double tfPilar, double faceY, bool enrijecer = true, double largFace = double.NaN)
        {
            double H = fViga.H * 1000, B = fViga.B * 1000, tf = fViga.Tf * 1000, s = emI ? -1 : 1;
            double esp = H < 300 ? 16 : 20, ext = H < 400 ? 60 : 80, d = H < 300 ? 16 : 20;
            double bp = Math.Max(B, Math.Min(150, double.IsNaN(largFace) ? 150 : largFace * 1000)), hp = H + 2 * ext, x0 = Math.Round(face * 1000, 1);   // pilar estreito: a chapa não passa da face dele
            var l = new Ligacao3D { No = no, Barra = viga.Id, Nome = "Chapa de topo estendida" };
            l.Chapas.Add(new Ligacao3D.Chapa { Nome = "chapa de topo", Origem = new Vector3D(x0, -bp / 2, -hp / 2), Normal = new Vector3D(1, 0, 0), U = new Vector3D(0, 1, 0), Espessura = esp, Contorno = Ret(0, 0, bp, hp) });
            double g = bp - 70;
            foreach (double z in new[] { H / 2 + ext / 2, H / 2 - tf - 35, -(H / 2 - tf - 35), -(H / 2 + ext / 2) })
                foreach (double y in new[] { -g / 2, g / 2 })
                    l.Parafusos.Add(new Ligacao3D.Parafuso { D = d, Comprimento = esp + tfPilar * 1000 + 12, Pega = esp + tfPilar * 1000, Origem = new Vector3D(x0 + esp, y, z), Eixo = new Vector3D(-1, 0, 0) });
            l.Soldas.Add(new Ligacao3D.Solda { Perna = 6, Pontos = ContornoLocal(fViga, x0 + esp, s) });
            double tfc = tfPilar * 1000, hw = Math.Max(40, faceY * 1000 - tfc);
            if (enrijecer) foreach (double z in new[] { H / 2 - tf / 2, -(H / 2 - tf / 2) })   // enrijecedores entre as mesas do pilar (só pilar I: no U/tubo a alma não é centrada)
                l.Chapas.Add(new Ligacao3D.Chapa { Nome = "enrijecedor", Origem = new Vector3D(-(x0 - tfc), -hw, z - 5), Normal = new Vector3D(0, 0, 1), U = new Vector3D(1, 0, 0), Espessura = 10, Contorno = Ret(0, 0, 2 * (x0 - tfc), 2 * hw) });
            l.Recortes.Add(new Ligacao3D.Recorte { Barra = "viga", Tipo = "recuo", Folga = esp });
            return l;
        }

        // Face da peça onde a chapa encosta, no referencial local (mm): alma do I (centrada, lado +y); costas da alma do U e da
        // aba do L (u = −B/2, y = s·u); lado do tubo. sinal = lado para onde a chapa cresce (+1 = +y).
        static (double y, double sinal) FaceChapa(Contorno f, bool emI)
        {
            double s = emI ? -1 : 1;
            if (f.Forma == "U" || f.Forma == "L") return (-s * f.B / 2 * 1000, -s);
            if (f.Forma == "TR" || f.Forma == "TC" || f.Forma == "CIRC") return (f.B / 2 * 1000, 1);
            return (f.Tw / 2 * 1000, 1);
        }

        // Chapa simples (shear tab) soldada na peça contínua e parafusada na alma da viga (3 parafusos). Com perfil de apoio I
        // (viga em viga): a chapa vai na alma do apoio, a viga entra até 12 mm da alma (x_ponta) com cope em cada mesa que bate
        // nas mesas do apoio (regra do contrato pontas). Apoio U/L/tubo: a viga para na face (a alma do U não é centrada).
        // A chapa fica na face da alma da viga (I) ou nas costas da alma (U, L), com altura que cabe na viga.
        public static Ligacao3D ChapaSimples(int no, Barra3D viga, bool emI, Contorno fViga, double face, Contorno fApoio = null)
        {
            bool naAlma = fApoio != null && fApoio.Forma == "I" && !fApoio.Concreto && fApoio.Lacos.Count == 1 && fApoio.Tw > 0 && fApoio.Tf > 0;
            if (naAlma) face = fApoio.Tw / 2;
            var (yf, sg) = FaceChapa(fViga, emI);
            bool costas = fViga.Forma == "U" || fViga.Forma == "L" || fViga.Forma == "TR" || fViga.Forma == "TC" || fViga.Forma == "CIRC";
            double H = fViga.H * 1000, tw = fViga.Tw * 1000, tf = fViga.Tf * 1000, x0 = Math.Round(face * 1000, 1), esp = 10, lp = 100;
            double hp = Math.Min(Math.Max(90, H - 80), costas ? H - 10 : H - 2 * tf - 10);   // entre as mesas (I) ou nas costas (U, L, tubo)
            if (naAlma) hp = Math.Min(hp, fApoio.H * 1000 - 2 * (fApoio.Tf * 1000 + 15));
            hp = Math.Max(hp, 20);
            var l = new Ligacao3D { No = no, Barra = viga.Id, Nome = "Chapa simples" };
            l.Chapas.Add(new Ligacao3D.Chapa { Nome = "chapa simples", Origem = new Vector3D(x0, yf, -sg * hp / 2), Normal = new Vector3D(0, sg, 0), U = new Vector3D(0, 0, sg), Espessura = esp, Contorno = Ret(0, 0, hp, lp) });
            int n = hp >= 170 ? 3 : hp >= 80 ? 2 : 1; double passo = n > 1 ? Math.Min(70, (hp - 60) / (n - 1)) : 0;
            double pega = esp + tw;
            for (int k = 0; k < n; k++)
                l.Parafusos.Add(new Ligacao3D.Parafuso { D = 16, Comprimento = pega + 14, Pega = pega, Origem = new Vector3D(x0 + 50, yf + sg * esp, (k - (n - 1) / 2.0) * passo), Eixo = new Vector3D(0, -sg, 0) });
            foreach (double y in new[] { yf, yf + sg * esp })   // filete dos dois lados da chapa
                l.Soldas.Add(new Ligacao3D.Solda { Perna = 6, Pontos = new[] { new Vector3D(x0, y, -hp / 2), new Vector3D(x0, y, hp / 2) } });
            if (!naAlma) { l.Recortes.Add(new Ligacao3D.Recorte { Barra = "viga", Tipo = "recuo", Folga = 12 }); return l; }
            l.Recortes.Add(new Ligacao3D.Recorte { Barra = "viga", Tipo = "recuo", XPonta = x0 + 12 });
            double livre = fApoio.H / 2 - fApoio.Tf, alt = Math.Round(fApoio.Tf * 1000 + 15), comp = Math.Round((fApoio.B - fApoio.Tw) / 2 * 1000 + 25);
            if (fViga.H / 2 > livre - 1e-9)   // ponytail: eixos da viga e do apoio no mesmo nível (sem os pontos cardinais)
                foreach (string pos in new[] { "topo", "base" })
                    l.Recortes.Add(new Ligacao3D.Recorte { Barra = "viga", Tipo = "cope", Posicao = pos, Altura = Math.Round((fViga.H / 2 - livre) * 1000 + 15), Comprimento = comp });
            else l.Recortes.Add(new Ligacao3D.Recorte { Barra = "viga", Tipo = "cope", Posicao = "topo", Altura = alt, Comprimento = comp });
            return l;
        }

        // Gusset de diagonal: chapa paralela ao plano xz local, encostada na face da diagonal (FaceChapa: costas do U/L, lado do
        // I ou do tubo; antes cortava a peça ao meio), parafusos ao longo dela, solda na peça contínua. recuo = recuo da diagonal (m).
        public static Ligacao3D Gusset(int no, Barra3D diag, bool emI, Contorno fDiag, double recuo)
        {
            double B = Math.Max(fDiag.B, fDiag.H) * 1000, t = Math.Max(fDiag.Tw, 0.006) * 1000, esp = 10;
            double xr = Math.Round(recuo * 1000, 1), xb = 0.4 * xr, x1 = xr + 220;
            var (yf, sg) = FaceChapa(fDiag, emI);
            if (fDiag.Forma == "I") yf = fDiag.B / 2 * 1000;   // I: por fora da ponta das mesas
            var l = new Ligacao3D { No = no, Barra = diag.Id, Nome = "Gusset" };
            l.Chapas.Add(new Ligacao3D.Chapa { Nome = "gusset", Origem = new Vector3D(0, yf, 0), Normal = new Vector3D(0, sg, 0), U = new Vector3D(sg, 0, 0), Espessura = esp,
                                               Contorno = new[] { new Point(sg * xb, -(B / 2 + 90)), new Point(sg * x1, -(B / 2 + 30)), new Point(sg * x1, B / 2 + 30), new Point(sg * xb, B / 2 + 90) } });
            for (int k = 0; k < 3; k++)
                l.Parafusos.Add(new Ligacao3D.Parafuso { D = 16, Comprimento = esp + t + 20, Pega = esp + t, Origem = new Vector3D(xr + 40 + 60 * k, yf + sg * esp, 0), Eixo = new Vector3D(0, -sg, 0) });
            foreach (double y in new[] { yf, yf + sg * esp })
                l.Soldas.Add(new Ligacao3D.Solda { Perna = 6, Pontos = new[] { new Vector3D(xb, y, -(B / 2 + 90)), new Vector3D(xb, y, B / 2 + 90) } });
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
                l.Parafusos.Add(new Ligacao3D.Parafuso { D = d, Comprimento = 200 + esp + 0.8 * d + 8, Pega = 200 + esp, Origem = new Vector3D(-200, deitado ? hy : hz, deitado ? hz : hy), Eixo = new Vector3D(1, 0, 0) });
            l.Soldas.Add(new Ligacao3D.Solda { Perna = solda > 0 ? solda : 8, Pontos = ContornoLocal(fPilar, esp, s) });
            l.Recortes.Add(new Ligacao3D.Recorte { Barra = "pilar", Tipo = "recuo", Folga = esp });
            return l;
        }

        // v0.6 A2 (motor: pontas.ponta_sobre_placa): barra inclinada sobre a placa de base. g = vertical do mundo no referencial
        // local (x ao longo da barra, saindo do nó). A placa fica horizontal (face de baixo no nível do nó), centrada na projeção
        // da seção no plano de cima, com a mesma sobra e a mesma distância dos furos às bordas; chumbadores verticais; a barra é
        // cortada no plano de cima (Ponta do motor). Barra vertical (ou deitada/descendo): nada muda.
        public static void AssentarPlaca(Ligacao3D l, Vector3D g, Contorno f)
        {
            var ch = l.Chapas.FirstOrDefault(c => c.Nome == "placa de base");
            g.Normalize();
            if (ch == null || g.X > 0.9999 || g.X < 0.2 || ch.Contorno.Length < 3) return;
            if (Math.Abs(ch.Normal.X - 1) > 1e-9 || Math.Abs(ch.Normal.Length - 1) > 1e-9) return;   // já assentada: a ligação é reaproveitada entre renders (2ª passada deslocava os chumbadores para fora da placa)
            Vector3D xb = new Vector3D(1, 0, 0), a1 = xb - g * g.X; a1.Normalize(); Vector3D a2 = Vector3D.CrossProduct(g, a1);
            double esp = ch.Espessura, ly = ch.Contorno.Max(p => p.X) - ch.Contorno.Min(p => p.X), lz = ch.Contorno.Max(p => p.Y) - ch.Contorno.Min(p => p.Y);
            Vector3D cOld = ch.Origem + ch.U * ((ch.Contorno.Max(p => p.X) + ch.Contorno.Min(p => p.X)) / 2) + Vector3D.CrossProduct(ch.Normal, ch.U) * ((ch.Contorno.Max(p => p.Y) + ch.Contorno.Min(p => p.Y)) / 2);
            Vector3D T = xb * (esp / g.X);   // eixo da barra no plano de cima
            // pegada da seção (caixa) projetada ao longo da barra no plano de cima, em (a1, a2) a partir de T
            var pts = (f.Lacos.Count > 0 ? f.Lacos[0] : new Point[0]).Select(p => new Vector3D(0, p.X * 1000, p.Y * 1000)).ToList();
            if (pts.Count == 0) return;
            var pg = pts.Select(c => { Vector3D q = c + xb * (Vector3D.DotProduct(T - c, g) / g.X) - T; return new Point(Vector3D.DotProduct(q, a1), Vector3D.DotProduct(q, a2)); }).ToList();
            double sobra = Math.Max(0, Math.Min((ly - f.B * 1000) / 2, (lz - f.H * 1000) / 2));
            double m1 = (pg.Max(p => p.X) + pg.Min(p => p.X)) / 2, m2 = (pg.Max(p => p.Y) + pg.Min(p => p.Y)) / 2;
            double L1 = pg.Max(p => p.X) - pg.Min(p => p.X) + 2 * sobra, L2 = pg.Max(p => p.Y) - pg.Min(p => p.Y) + 2 * sobra;
            Vector3D cb = T + a1 * m1 + a2 * m2 - g * esp;   // centro da face de baixo
            ch.Origem = cb - a1 * (L1 / 2) - a2 * (L2 / 2); ch.Normal = g; ch.U = a1; ch.Contorno = Ret(0, 0, L1, L2);
            // furos: mesma distância às bordas; a1 casa com o eixo local (y ou z) mais alinhado
            bool a1y = Math.Abs(a1.Y) >= Math.Abs(a1.Z);
            foreach (var pf in l.Parafusos)
            {
                Vector3D r = pf.Origem - cOld; double py = r.Y, pz = r.Z;
                double q1 = a1y ? py : pz, h1 = a1y ? ly / 2 : lz / 2, q2 = a1y ? pz : py, h2 = a1y ? lz / 2 : ly / 2;
                double n1 = Math.Sign(q1) * (L1 / 2 - (h1 - Math.Abs(q1))), n2 = Math.Sign(q2) * (L2 / 2 - (h2 - Math.Abs(q2)));
                pf.Origem = cb + a1 * n1 + a2 * n2 + g * pf.Origem.X; pf.Eixo = g;   // Origem.X = −200: embutido abaixo da placa
            }
            foreach (var sd in l.Soldas) sd.Pontos = sd.Pontos?.Select(p => p + xb * (Vector3D.DotProduct(T - p, g) / g.X)).ToArray();
            l.Recortes.RemoveAll(r => r.Tipo == "recuo" && r.Barra == "pilar");
            l.Ponta = new Ligacao3D.PontaMotor { P = T / 1000, N = -g };
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
                double face = c.Face;
                if (vert.Contains(b)) continue;
                if (c.Raio > 0) continue;   // v0.6 A3: tubo em tubo = solda direta com boca de peixe (motor tubo_tubo_soldado), sem chapa
                bool horiz = Math.Abs(nos[b.Nj].Z - nos[b.Ni].Z) < 0.2 * (P(nos[b.Nj]) - P(nos[b.Ni])).Length;
                if (!horiz) r.Add(Gusset(no, b, emI, f, c.Recuo));
                else if (vert.Contains(c.Contra) && c.NaMesa) { Contorno fp = forma(c.Contra); r.Add(ChapaDeTopo(no, b, emI, f, face, fp.Tf, Math.Max(fp.B, fp.H) / 2, fp.Forma == "I", fp.B)); }
                else r.Add(ChapaSimples(no, b, emI, f, face, forma(c.Contra)));
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

        // Chapas de topo de cumeeira/joelho (emenda em ângulo, dois banzos em esquadria): uma chapa no plano da esquadria
        // (nLocal = normal do plano no referencial local da barra, apontando para dentro dela) e, com parafusos, 4 linhas de 2
        // atravessando as duas chapas (a outra barra chama sem parafusos).
        public static double SobraCumeeira = 10;   // mm além do perfil, em cima e embaixo (parâmetro; 0 = rente ao perfil)
        public static Ligacao3D Cumeeira(int no, Barra3D b, Contorno f, Vector3D nLocal, bool parafusos)
        {
            nLocal.Normalize(); if (nLocal.X < 0) nLocal = -nLocal;
            Vector3D U = new Vector3D(0, 1, 0) - nLocal * nLocal.Y; if (U.Length < 1e-6) return null; U.Normalize();
            Vector3D W = Vector3D.CrossProduct(nLocal, U);
            // GEOMETRIA-LIGACOES: proposta ajustada (flush) — a chapa termina no perfil + SobraCumeeira (antes estendida 60–80 mm
            // acima e abaixo, com parafusos fora das mesas: chapa sobrando além do perfil); parafusos só entre as mesas.
            double H = f.H * 1000, B = f.B * 1000, esp = H < 300 ? 16 : 20, d = H < 300 ? 16 : 20, ext = SobraCumeeira;
            double hc = H / Math.Max(0.3, Math.Abs(W.Z)), bp = Math.Max(B, 150), hp = hc + 2 * ext, g = bp - 70;
            var l = new Ligacao3D { No = no, Barra = b.Id, Nome = "Chapa de topo (cumeeira)" };
            l.Recortes.Add(new Ligacao3D.Recorte { Barra = "viga", Tipo = "recuo", XPonta = esp });   // a barra termina na face da chapa (Juntas recua o plano da esquadria)
            l.Chapas.Add(new Ligacao3D.Chapa { Nome = "chapa de topo", Origem = U * (-bp / 2) + W * (-hp / 2), Normal = nLocal, U = U, Espessura = esp, Contorno = Ret(0, 0, bp, hp) });
            if (parafusos)
                foreach (double w in new[] { hc / 2 - 45, -(hc / 2 - 45) })
                    foreach (double u in new[] { -g / 2, g / 2 })
                        l.Parafusos.Add(new Ligacao3D.Parafuso { D = d, Comprimento = 2 * esp + 0.9 * d + 12, Pega = 2 * esp, Origem = U * u + W * w + nLocal * esp, Eixo = -nLocal });
            return l;
        }

        // Proposta do tipo de nó para uma ligação pendente (grupo sem projeto que o motor não desenhou): gusset nas diagonais,
        // chapas de topo na emenda em ângulo, chapa de topo na viga–pilar rígida, chapa simples no resto. Vazio = nada a desenhar.
        public static List<Ligacao3D> Proposta(Ligacao3D p, Modelo3D mod, Dictionary<(Barra3D, bool), Corte> cortes, Func<Barra3D, Contorno> forma,
                                               Func<Barra3D, bool, double> folga, Func<Barra3D, int, Vector3D, Vector3D> paraLocal)
        {
            var r = new List<Ligacao3D>();
            Barra3D b = mod.Barras.FirstOrDefault(x => x.Id == p.Barra); if (b == null) return r;
            bool emI = b.Ni == p.No;
            if (!cortes.TryGetValue((b, emI), out Corte c) || c.Contra == null) return r;
            Contorno f = forma(b), fc = forma(c.Contra); if (f.Concreto || fc.Concreto) return r;
            if (c.Raio > 0) return r;   // v0.6 A3: tubo em tubo, boca de peixe sem chapa
            if (fc.Forma == "TR" || fc.Forma == "TC" || fc.Forma == "CIRC") return r;   // perfil em tubo: solda com recorte do motor, sem chapa proposta
            var nos = mod.Nos.Where(n => n.Id == b.Ni || n.Id == b.Nj).ToDictionary(n => n.Id);
            if (nos.Count < 2) return r;
            Vector3D e = P(nos[b.Nj]) - P(nos[b.Ni]); double dz = Math.Abs(e.Z) / e.Length;
            if (c.Esquadria)
            {
                bool cEmI = c.Contra.Ni == p.No;
                if (!cortes.TryGetValue((c.Contra, cEmI), out Corte c2)) return r;
                r.Add(Cumeeira(p.No, b, f, paraLocal(b, p.No, c.N), true));
                r.Add(Cumeeira(p.No, c.Contra, fc, paraLocal(c.Contra, p.No, c2.N), false));
                r.RemoveAll(x => x == null);
                return r;
            }
            if (dz > 0.95) return r;   // pilar que para em algo: fica sem proposta
            string tipo = p.Pendente ?? "";
            double face = c.Face;
            if (tipo.StartsWith("gusset") || tipo.StartsWith("no_trelica") || dz > 0.2) r.Add(Gusset(p.No, b, emI, f, c.Recuo));
            else if (tipo.StartsWith("viga_pilar_mesa") && tipo.Contains("rigida") && fc.Lacos.Count == 1 && c.NaMesa) r.Add(ChapaDeTopo(p.No, b, emI, f, face, fc.Tf, Math.Max(fc.B, fc.H) / 2, fc.Forma == "I", fc.B));
            else r.Add(ChapaSimples(p.No, b, emI, f, face, fc));
            return r;
        }

        // Referencial local → mundo (origem o no nó, x ao longo da barra, z = v, y = x × z canhoto). Barra inclinada contra pilar
        // (corte Inclinada): gira em torno de y até x ficar na normal da face e sobe a origem até o eixo da barra cruzar a face, para
        // a chapa (desenhada como se a viga chegasse perpendicular) ficar rente à mesa e a viga terminar nela.
        public static (Point3D o, Vector3D x, Vector3D y, Vector3D z) Referencial(Point3D o, Vector3D x, Vector3D z, Corte k)
        {
            Vector3D y = Vector3D.CrossProduct(x, z);
            if (k == null || !k.Inclinada) return (o, x, y, z);
            Vector3D xn = k.N - y * Vector3D.DotProduct(k.N, y); xn.Normalize();
            o += x * (k.Face / Vector3D.DotProduct(x, xn)) - xn * k.Face;
            return (o, xn, y, Vector3D.CrossProduct(y, xn));
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
            {   // solda: borda do enrijecedor (plano médio) e face da placa de base (docs/v3/GEOMETRIA-LIGACOES.md)
                var le = new Ligacao3D();
                le.Chapas.Add(new Ligacao3D.Chapa { Nome = "enrijecedor do pilar", Origem = new Vector3D(0, 0, -5), Normal = new Vector3D(0, 0, 1), U = new Vector3D(1, 0, 0), Espessura = 10, Contorno = new[] { new Point(-100, 3), new Point(100, 3), new Point(100, 75), new Point(-100, 75) } });
                var se = new Ligacao3D.Solda { Perna = 5, Pontos = new[] { new Vector3D(-100, 3, 0), new Vector3D(100, 3, 0) } };   // ∥ à barra (x): antes nem era desenhado
                le.Soldas.Add(se);
                var (ax, lados, face) = Ligacao3D.Apoio(le, se);
                afirmar(face == null && ax is Vector3D a1 && Math.Abs(a1.Y - 1) < 1e-9 && lados.Count == 2 && lados.All(q => Math.Abs(q.desl - 5) < 1e-9 && Math.Abs(Math.Abs(q.n.Z) - 1) < 1e-9),
                        "solda de enrijecedor: pernas na chapa (para dentro) e no apoio (±normal), raiz a meia espessura do plano médio");
                Ligacao3D pp = PlacaDeBase(1, new Barra3D { Id = 1, Ni = 1, Nj = 2 }, true, f, 200, 120, 19, 16, new[] { (-60.0, -35.0) }, 6);
                var (ax2, l2, f2) = Ligacao3D.Apoio(pp, pp.Soldas[0]);
                var dentro = Render3DGeometria.Intervalos(f2.Q(new Vector3D(19, -100, 0)), f2.Q(new Vector3D(19, 100, 0)), f2.Contorno);
                afirmar(f2 != null && f2.Nome == "placa de base" && ax2 == null && dentro.Count == 1 && Math.Abs((dentro[0].Item2 - dentro[0].Item1) * 200 - 120) < 1e-6,
                        "solda da placa de base menor que o perfil: assenta na face da placa e é recortada nos 120 mm da chapa");
                Ligacao3D pd = PlacaDeBase(1, new Barra3D { Id = 1, Ni = 1, Nj = 2 }, true, f, 400, 300, 19, 16, new[] { (-150.0, -100.0) }, 6, true);
                afirmar(pd.Chapas[0].Contorno.Max(q => q.X) == 400 && pd.Chapas[0].Contorno.Max(q => q.Y) == 300 && pd.Parafusos[0].Origem.Y == -150 && pd.Parafusos[0].Origem.Z == -100,
                        "placa deitada (eixo fraco): H no eixo y local (largura do perfil), furos girados junto");
            }
            afirmar(pb.Parafusos.Count == 2 && pb.Parafusos[0].Origem.Z == -150 && pb.Parafusos[0].Origem.Y == -100 && Math.Abs(pb.RecuoDe("pilar") - 0.025) < 1e-12 && double.IsNaN(pb.RecuoDe("viga")), "placa de base: H no eixo z local, chumbadores verticais, pilar recua a espessura");
        }
    }
}
