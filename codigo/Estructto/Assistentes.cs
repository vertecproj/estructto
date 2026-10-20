// Geradores de estruturas tipicas (estilo dos assistentes do CYPE): viga continua, portico de varios andares,
// trelica Pratt/Howe/Warren e galpao de duas aguas.
using System;
using System.Collections.Generic;
using System.Linq;
using Portico;

namespace NovoFtool
{
    public static class Assistentes
    {
        public static List<double> Lista(string s)
        {
            var r = new List<double>();
            foreach (string p in (s ?? "").Split(new[] { ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                // "3x5" = tres vaos de 5
                string[] rep = p.ToLowerInvariant().Split('x', '*');
                double v; int n;
                if (rep.Length == 2 && int.TryParse(rep[0], out n) && Num.Ler(rep[1], out v)) { for (int i = 0; i < n; i++) r.Add(v); }
                else if (Num.Ler(p, out v)) r.Add(v);
                else return null;
            }
            return r.Count > 0 && r.Count <= 500 && r.All(v => v > 0 && double.IsFinite(v)) ? r : null;
        }

        static Barra Ligar(Modelo m, double x1, double y1, double x2, double y2, string sec, string mat)
        {
            No a = m.ObterNo(x1, y1, 1e-6), b = m.ObterNo(x2, y2, 1e-6);
            int antes = m.Barras.Count;
            Barra ult = m.AddBarra(a, b, 1e-6);
            for (int i = antes; i < m.Barras.Count; i++) { m.Barras[i].Secao = sec; m.Barras[i].Material = mat; }
            return ult;
        }

        static void Apoio(No n, string tipo)
        {
            n.ApoioX = tipo != "movel"; n.ApoioY = true; n.ApoioGiro = tipo == "engaste";
        }

        public static Modelo Viga(List<double> vaos, double q, bool engastar, string sec, string mat)
        {
            var m = new Modelo();
            double x = 0;
            foreach (double v in vaos) { Barra b = Ligar(m, x, 0, x + v, 0, sec, mat); b.QGlobalY = q; x += v; }
            var nos = m.Nos.OrderBy(n => n.X).ToList();
            for (int i = 0; i < nos.Count; i++)
                Apoio(nos[i], engastar && (i == 0 || i == nos.Count - 1) ? "engaste" : i == 0 ? "fixo" : "movel");
            return m;
        }

        public static Modelo Portico(List<double> vaos, List<double> alturas, bool baseEngastada, double qViga, double vento, string secPilar, string secViga, string mat)
        {
            var m = new Modelo();
            var xs = new List<double> { 0 }; foreach (double v in vaos) xs.Add(xs.Last() + v);
            var ys = new List<double> { 0 }; foreach (double h in alturas) ys.Add(ys.Last() + h);
            for (int j = 0; j + 1 < ys.Count; j++)
                foreach (double x in xs) Ligar(m, x, ys[j], x, ys[j + 1], secPilar, mat);
            for (int j = 1; j < ys.Count; j++)
                for (int i = 0; i + 1 < xs.Count; i++) { Barra b = Ligar(m, xs[i], ys[j], xs[i + 1], ys[j], secViga, mat); b.QGlobalY = qViga; }
            foreach (No n in m.Nos)
            {
                if (Math.Abs(n.Y) < 1e-9) Apoio(n, baseEngastada ? "engaste" : "fixo");
                else if (Math.Abs(n.X) < 1e-9 && vento != 0) n.Fx = vento;   // vento no pilar da esquerda, por pavimento
            }
            return m;
        }

        public static Modelo Trelica(string tipo, double vao, double altura, int paineis, double cargaNo, string sec, string mat)
        {
            var m = new Modelo();
            double p = vao / paineis;
            for (int i = 0; i < paineis; i++)
            {
                Ligar(m, i * p, 0, (i + 1) * p, 0, sec, mat);                 // banzo inferior
                Ligar(m, i * p, altura, (i + 1) * p, altura, sec, mat);       // banzo superior
            }
            if (tipo == "Warren")
            {
                for (int i = 0; i < paineis; i++)
                {
                    if (i % 2 == 0) Ligar(m, i * p, 0, (i + 1) * p, altura, sec, mat); else Ligar(m, i * p, altura, (i + 1) * p, 0, sec, mat);
                }
                Ligar(m, 0, 0, 0, altura, sec, mat); Ligar(m, vao, 0, vao, altura, sec, mat);
                // montantes: sem eles, nos onde so chegam os dois banzos (rotulados e alinhados) ficam sem rigidez vertical
                for (int i = 1; i < paineis; i++) Ligar(m, i * p, 0, i * p, altura, sec, mat);
            }
            else
            {
                for (int i = 0; i <= paineis; i++) Ligar(m, i * p, 0, i * p, altura, sec, mat);   // montantes
                for (int i = 0; i < paineis; i++)
                {
                    bool esquerda = (i + 0.5) * p < vao / 2;
                    // Pratt: diagonais descem para o centro (tracionadas sob gravidade); Howe: o contrario
                    bool desce = tipo == "Pratt" ? esquerda : !esquerda;
                    if (desce) Ligar(m, i * p, altura, (i + 1) * p, 0, sec, mat); else Ligar(m, i * p, 0, (i + 1) * p, altura, sec, mat);
                }
            }
            foreach (Barra b in m.Barras) { b.RotulaInicio = true; b.RotulaFim = true; }
            foreach (No n in m.Nos)
            {
                if (Math.Abs(n.Y - altura) < 1e-9) n.Fy = cargaNo;
                if (Math.Abs(n.Y) < 1e-9 && Math.Abs(n.X) < 1e-9) Apoio(n, "fixo");
                if (Math.Abs(n.Y) < 1e-9 && Math.Abs(n.X - vao) < 1e-9) Apoio(n, "movel");
            }
            return m;
        }

        public static Modelo Galpao(double vao, double hPilar, double hCumeeira, double q, bool baseEngastada, string secPilar, string secViga, string mat)
        {
            var m = new Modelo();
            Ligar(m, 0, 0, 0, hPilar, secPilar, mat);
            Ligar(m, vao, 0, vao, hPilar, secPilar, mat);
            Barra a = Ligar(m, 0, hPilar, vao / 2, hPilar + hCumeeira, secViga, mat);
            Barra b = Ligar(m, vao / 2, hPilar + hCumeeira, vao, hPilar, secViga, mat);
            a.QGlobalY = q; b.QGlobalY = q;
            foreach (No n in m.Nos) if (Math.Abs(n.Y) < 1e-9) Apoio(n, baseEngastada ? "engaste" : "fixo");
            return m;
        }
    }
}
