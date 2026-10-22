using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media.Media3D;

namespace NovoFtool
{
    public static partial class Capturas
    {
        // C4: auto-teste dos parafusos no referencial local do contrato (mm), sobre o que o render desenha. A pilha apertada =
        // chapas + cantoneiras do contrato + `pilha` do parafuso (aba da barra ligada, fora do contrato). Falha se: eixo não ∥ normal
        // da chapa (±1°); cabeça dentro da pilha; fuste não passa da pilha; porca (pega EFETIVA de Render3DGeometria.PegaEfetiva,
        // depois do limite) dentro da pilha ou pega encurtada pelo limite.
        internal static int VerificarParafusos(IEnumerable<Ligacao3D> ligs, StringBuilder sb)
        {
            int total = 0, falhas = 0, semPilha = 0; var porMotivo = new Dictionary<string, int>(); var ex = new List<string>();
            const double tol = 0.5, passo = 0.25;   // mm
            var perfis = new Dictionary<string, GeometriaPerfis.Contorno>();
            foreach (var l in ligs)
                foreach (var pf in l.Parafusos)
                {
                    total++;
                    Vector3D e = pf.Eixo; if (e.Length < 1e-9) { Falha(l, pf, "eixo nulo", e); continue; } e.Normalize();
                    double efet = Render3DGeometria.PegaEfetiva(pf.D / 1000, pf.Comprimento / 1000, pf.Pega / 1000) * 1000;
                    var motivos = new List<string>();
                    if (!double.IsNaN(pf.Pega) && pf.Pega > 0 && efet < pf.Pega - 0.05) motivos.Add("pega encurtada pelo limite (" + pf.Pega.ToString("0.0", CultureInfo.InvariantCulture) + " → " + efet.ToString("0.0", CultureInfo.InvariantCulture) + "): comprimento curto");
                    var ocup = new SortedSet<int>();   // amostras (índice = t/passo) dentro de alguma peça apertada
                    int i0 = (int)Math.Floor(-0.8 * pf.D / passo), i1 = (int)Math.Ceiling(pf.Comprimento / passo);
                    for (int i = i0; i <= i1; i++)
                    {
                        double t = i * passo; Vector3D P = pf.Origem + e * t; bool dentro = false;
                        foreach (var ch in l.Chapas)
                        {
                            Vector3D n = ch.Normal, u = ch.U; if (n.Length < 0.5 || u.Length < 0.5) continue; n.Normalize(); u.Normalize();
                            Vector3D w = Vector3D.CrossProduct(n, u), rel = P - ch.Origem; double h = Vector3D.DotProduct(rel, n);
                            if (h < 0 || h > ch.Espessura || !Dentro(ch.Contorno, new Point(Vector3D.DotProduct(rel, u), Vector3D.DotProduct(rel, w)))) continue;
                            dentro = true;
                            if (Math.Abs(Vector3D.DotProduct(e, n)) < Math.Cos(Math.PI / 180) && !motivos.Any(m => m.StartsWith(ch.Nome + ": eixo")))
                                motivos.Add(ch.Nome + ": eixo a " + (Math.Acos(Math.Min(1, Math.Abs(Vector3D.DotProduct(e, n)))) * 180 / Math.PI).ToString("0.0", CultureInfo.InvariantCulture) + "° da normal");
                        }
                        if (!dentro) foreach (var ca in l.Cantoneiras) if (DentroCantoneira(ca, P, perfis)) { dentro = true; break; }
                        if (!dentro) foreach (var pl in pf.Pilha) if (t >= pl[0] && t <= pl[1]) { dentro = true; break; }
                        if (dentro) ocup.Add(i);
                    }
                    if (ocup.Count == 0) semPilha++;
                    else
                    {
                        double ini = ocup.Min * passo, fim = (ocup.Max + 1) * passo;
                        if (ini < -tol) motivos.Add("cabeça dentro da pilha (t=" + ini.ToString("0.0", CultureInfo.InvariantCulture) + ")");
                        if (fim > pf.Comprimento + tol) motivos.Add("fuste não passa da pilha (fim " + fim.ToString("0.0", CultureInfo.InvariantCulture) + " > L " + pf.Comprimento.ToString("0.0", CultureInfo.InvariantCulture) + ")");
                        else if (fim > efet + tol) motivos.Add("porca dentro da pilha (pega " + efet.ToString("0.0", CultureInfo.InvariantCulture) + " < " + fim.ToString("0.0", CultureInfo.InvariantCulture) + ")");
                    }
                    if (motivos.Count > 0) Falha(l, pf, string.Join("; ", motivos.Distinct()), e);
                }
            sb.AppendLine("parafusos: " + total + " (" + semPilha + " sem peça apertada no contrato) · " + falhas + " com falha" + (porMotivo.Count > 0 ? " [" + string.Join(", ", porMotivo.Select(kv => kv.Key + " " + kv.Value)) + "]" : ""));
            foreach (var s in ex) sb.AppendLine(s);
            return falhas;

            void Falha(Ligacao3D lg, Ligacao3D.Parafuso pf, string m, Vector3D e)
            {
                falhas++; string k = m.Contains("encurtada") ? "pega encurtada" : m.Contains("°") ? "eixo≠normal" : m.Contains("cabeça") ? "cabeça dentro" : m.Contains("porca") ? "porca dentro" : m.Contains("fuste") ? "fuste curto" : m;
                porMotivo[k] = porMotivo.TryGetValue(k, out int c) ? c + 1 : 1;
                if (ex.Count < 12) ex.Add("  " + lg.Nome + " nó " + lg.No + " d" + pf.D + " L" + pf.Comprimento + " pega " + pf.Pega + ": " + m);
            }
        }

        // C4: cada ponto de `Apertadas` (meio da aba da barra ligada, no mundo) tem de estar dentro de algum sólido de barra desenhado
        // (a diagonal tem de chegar e lapear o gusset); senão o parafuso aperta o ar. Só valem as barras ligadas no nó da ligação
        // (a barra da ligação e o apoio): uma barra vizinha qualquer que passe ali não conta (onda final, C4).
        internal static int VerificarApertadas(IList<Render3D.Peca3D> todos, StringBuilder sb)
        {
            int n = 0, falhas = 0;
            foreach (var pc in todos.Where(x => x.Apertadas != null && x.Apertadas.Length > 0))
            {
                var barras = todos.Where(x => x.Tipo == "barra" && x.M != null && x.Barra != null && pc.Lig != null && (x.Barra.Ni == pc.Lig.No || x.Barra.Nj == pc.Lig.No)).Select(x => x.M).ToList();
                foreach (var q in pc.Apertadas)
                {
                    n++; var p = new System.Numerics.Vector3((float)q.X, (float)q.Y, (float)q.Z);
                    if (barras.Any(m => Render3DGeometria.Dentro(m, p))) continue;
                    falhas++; if (falhas <= 6) sb.AppendLine("  parafuso sem barra sob a cabeça: " + pc.Nome + " [x local do parafuso " + pc.Lig.Parafusos.Min(x => x.Origem.X).ToString("0") + ".." + pc.Lig.Parafusos.Max(x => x.Origem.X).ToString("0") + " mm; ponta " + (pc.Lig.Ponta == null ? "nenhuma" : "P.x " + (pc.Lig.Ponta.P.X * 1000).ToString("0") + " N.x " + pc.Lig.Ponta.N.X.ToString("0.00")) + " recuo_barra " + pc.Lig.RecuoDe("diagonal").ToString("0.000") + "/" + pc.Lig.RecuoDe("viga").ToString("0.000") + " xmax " + pc.Lig.XMax + "] mais perto: " + todos.Where(x => x.Tipo == "barra" && x.M != null).Select(x => (x.Nome, Render3DGeometria.Distancia(x.M, p) * 1000)).OrderBy(x => x.Item2).Select(x => x.Nome + " " + x.Item2.ToString("0.0") + " mm").First() + " em (" + q.X.ToString("0.000", CultureInfo.InvariantCulture) + ", " + q.Y.ToString("0.000", CultureInfo.InvariantCulture) + ", " + q.Z.ToString("0.000", CultureInfo.InvariantCulture) + ")");
                }
            }
            sb.AppendLine("peças apertadas (aba da barra): " + n + " pontos · " + falhas + " fora de qualquer barra");
            return falhas;
        }

        // Ponto P (mm, local) dentro da cantoneira (extrusão do contorno do perfil L; triedro como o do Render3D: v = −(eixo × u) no referencial canhoto do contrato).
        static bool DentroCantoneira(Ligacao3D.Cantoneira ca, Vector3D P, Dictionary<string, GeometriaPerfis.Contorno> cache)
        {
            if (!cache.TryGetValue(ca.Perfil ?? "", out var f))
            {
                var pr = Render3D.PerfilL(ca.Perfil); f = pr == null ? null : GeometriaPerfis.CriarContorno(pr, 0.05); cache[ca.Perfil ?? ""] = f;
            }
            if (f == null) return false;
            Vector3D ex = ca.Eixo, u = ca.U; if (ex.Length < 0.5 || u.Length < 0.5) return false; ex.Normalize(); u.Normalize();
            Vector3D v = -Vector3D.CrossProduct(ex, u), rel = P - ca.Origem; double x = Vector3D.DotProduct(rel, ex);
            if (x < 0 || x > ca.Comprimento) return false;
            var q = new Point(Vector3D.DotProduct(rel, u) / 1000, Vector3D.DotProduct(rel, v) / 1000);
            return f.Lacos.Any(lc => Dentro(lc, q));
        }

        static bool Dentro(Point[] poly, Point p)
        {
            bool d = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                if ((poly[i].Y > p.Y) != (poly[j].Y > p.Y) && p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X) d = !d;
            return d;
        }
    }
}
