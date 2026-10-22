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
        // C4: auto-teste dos parafusos no referencial local do contrato (mm): eixo ∥ normal da chapa (±1°), fuste atravessa todas as
        // chapas da pega, cabeça (origem) e porca (origem + pega) fora delas. Devolve o nº de falhas; descreve as primeiras em sb.
        internal static int VerificarParafusos(IEnumerable<Ligacao3D> ligs, StringBuilder sb)
        {
            int total = 0, falhas = 0, semChapa = 0; var porMotivo = new Dictionary<string, int>(); var ex = new List<string>();
            const double tol = 0.5;   // mm
            foreach (var l in ligs)
                foreach (var pf in l.Parafusos)
                {
                    total++;
                    Vector3D e = pf.Eixo; if (e.Length < 1e-9) { Falha(l, "eixo nulo"); continue; } e.Normalize();
                    double pega = double.IsNaN(pf.Pega) || pf.Pega <= 0 ? pf.Comprimento - 0.9 * pf.D - 14 : pf.Pega;
                    bool achou = false; var motivos = new List<string>();
                    foreach (var ch in l.Chapas)
                    {
                        Vector3D n = ch.Normal; if (n.Length < 0.5) continue; n.Normalize();
                        double cos = Vector3D.DotProduct(e, n);
                        // chapa "da pega": o ponto do parafuso, projetado no plano, cai dentro do contorno e a faixa da chapa cruza o fuste
                        Vector3D w = Vector3D.CrossProduct(ch.Normal, ch.U); w.Normalize(); Vector3D u = ch.U; u.Normalize();
                        double h0 = Vector3D.DotProduct(pf.Origem - ch.Origem, n);   // altura da cabeça sobre a face 0 da chapa
                        Vector3D rel = pf.Origem - ch.Origem - n * h0;
                        Point q = new Point(Vector3D.DotProduct(rel, u), Vector3D.DotProduct(rel, w));
                        if (!Dentro(ch.Contorno, q)) continue;
                        double ta = 0, tb = 0;   // faixa da chapa ao longo do fuste
                        if (Math.Abs(cos) >= 1e-3) { double t0 = -h0 / cos, t1 = (ch.Espessura - h0) / cos; ta = Math.Min(t0, t1); tb = Math.Max(t0, t1); }
                        if (Math.Abs(cos) < 0.5 && (h0 < -tol || h0 > ch.Espessura + tol)) continue;   // paralelo e fora da faixa: não é desta chapa
                        achou = true;
                        if (Math.Abs(cos) < Math.Cos(Math.PI / 180)) { motivos.Add(ch.Nome + ": eixo a " + (Math.Acos(Math.Min(1, Math.Abs(cos))) * 180 / Math.PI).ToString("0.0", CultureInfo.InvariantCulture) + "° da normal"); continue; }
                        if (ta < -tol) motivos.Add(ch.Nome + ": cabeça dentro/atrás da chapa (t=" + ta.ToString("0.0", CultureInfo.InvariantCulture) + ")");
                        if (tb > pf.Comprimento + tol) motivos.Add(ch.Nome + ": fuste não chega ao fim da chapa");
                        else if (tb > pega + tol) motivos.Add(ch.Nome + ": porca dentro da chapa (pega " + pega.ToString("0.0", CultureInfo.InvariantCulture) + " < " + tb.ToString("0.0", CultureInfo.InvariantCulture) + ")");
                    }
                    if (!achou) { semChapa++; continue; }   // fixa só perfis (sem chapa no contrato): nada a conferir
                    if (motivos.Count > 0) Falha(l, string.Join("; ", motivos.Distinct()));

                    void Falha(Ligacao3D lg, string m)
                    {
                        falhas++; string k = m.Contains("°") ? "eixo≠normal" : m.Contains("cabeça") ? "cabeça dentro" : m.Contains("porca") ? "porca dentro" : m.Contains("fuste") ? "fuste curto" : m;
                        porMotivo[k] = porMotivo.TryGetValue(k, out int c) ? c + 1 : 1;
                        if (ex.Count < 12) ex.Add("  " + lg.Nome + " nó " + lg.No + " d" + pf.D + " eixo(" + e.X.ToString("0.##", CultureInfo.InvariantCulture) + "," + e.Y.ToString("0.##", CultureInfo.InvariantCulture) + "," + e.Z.ToString("0.##", CultureInfo.InvariantCulture) + ") L" + pf.Comprimento + " pega " + pf.Pega + ": " + m);
                    }
                }
            sb.AppendLine("parafusos: " + total + " (" + semChapa + " sem chapa no contrato) · " + falhas + " com falha" + (porMotivo.Count > 0 ? " [" + string.Join(", ", porMotivo.Select(kv => kv.Key + " " + kv.Value)) + "]" : ""));
            foreach (var s in ex) sb.AppendLine(s);
            return falhas;
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
