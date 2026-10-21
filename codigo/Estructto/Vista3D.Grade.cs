// Grade estrutural (Plant 3D "Structure Grid"): eixos em X (1, 2, 3…) e filas em Y (A, B, C…) desenhados em cada nível
// (pavimentos do motor + térreo), linhas traço-ponto com o nome num balão na ponta; o nível do plano de trabalho fica mais
// forte. Os cruzamentos entram na atração do Snap3D (Vista3D.Snap.cs › Geometria). Fonte única: config.grade do motor
// (Novo projeto e GR gravam a mesma chave; Ctrl+Z desfaz pelo motor). docs/MODELO-ESPACIAL.md "Grade estrutural".
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace NovoFtool
{
    // config.grade = {"x": [vãos], "y": [vãos], "nomes_x": ["1", …], "nomes_y": ["A", …], "x0": m, "y0": m}. x0/y0 opcionais
    // (= 0; o Novo projeto não grava). Os níveis não ficam aqui: são os pavimentos do motor.
    public static class GradeEstrutural
    {
        public static double? Num(JsonNode n)
        {
            if (!(n is JsonValue v)) return null;
            if (v.TryGetValue(out double d)) return double.IsFinite(d) ? d : (double?)null;
            if (v.TryGetValue(out int i)) return i;
            return v.TryGetValue(out long l) ? l : (double?)null;
        }

        // Posições absolutas (m) e nomes dos eixos/filas. Sem grade = listas vazias.
        public static (double[] X, double[] Y, string[] NX, string[] NY) Ler(JsonNode g)
        {
            var o = g as JsonObject;
            double[] Pos(string k, string k0)
            {
                if (!(o?[k] is JsonArray a)) return new double[0];
                var r = new List<double> { Num(o[k0]) ?? 0 };
                foreach (var v in a.Select(Num)) if (v > 0) r.Add(Math.Round(r[^1] + v.Value, 9));
                return r.ToArray();
            }
            string[] Nomes(string k, int n, Func<int, string> padrao)
            {
                var a = (o?[k] as JsonArray)?.Select(x => x is JsonValue v && v.TryGetValue(out string s) ? s : null).ToList();
                return Enumerable.Range(0, n).Select(i => a != null && a.Count == n && !string.IsNullOrEmpty(a[i]) ? a[i] : padrao(i)).ToArray();
            }
            var x = Pos("x", "x0"); var y = Pos("y", "y0");
            return (x, y, Nomes("nomes_x", x.Length, GradeNiveis.NomeX), Nomes("nomes_y", y.Length, GradeNiveis.NomeY));
        }

        // Posições absolutas (GR, contêiner antigo) → config.grade.
        public static JsonObject Json(double[] xs, double[] ys)
        {
            var g = new JsonObject();
            void Eixo(double[] p, string k, string k0, string kn, Func<int, string> nome)
            {
                var s = p.OrderBy(v => v).Aggregate(new List<double>(), (l, v) => { if (l.Count == 0 || v - l[^1] > 1e-6) l.Add(v); return l; });
                if (s.Count == 0) return;
                if (Math.Abs(s[0]) > 1e-9) g[k0] = s[0];
                g[k] = new JsonArray(s.Skip(1).Select((v, i) => (JsonNode)Math.Round(v - s[i], 9)).ToArray());
                g[kn] = new JsonArray(s.Select((v, i) => (JsonNode)nome(i)).ToArray());
            }
            Eixo(xs, "x", "x0", "nomes_x", GradeNiveis.NomeX);
            Eixo(ys, "y", "y0", "nomes_y", GradeNiveis.NomeY);
            return g;
        }

        // GR sobre a cópia do modelo: config.grade + os pavimentos novos (sem diafragma). Vai num PUT (um Ctrl+Z).
        public static JsonObject Aplicar(JsonObject m, double[] xs, double[] ys, IEnumerable<(string Nome, double Z)> pavimentos)
        {
            var cfg = m["config"] as JsonObject ?? (JsonObject)(m["config"] = new JsonObject());
            cfg["grade"] = Json(xs, ys);
            var pav = m["pavimentos"] as JsonObject ?? (JsonObject)(m["pavimentos"] = new JsonObject());
            foreach (var (nome, z) in pavimentos) pav[nome] = new JsonObject { ["nome"] = nome, ["z"] = z, ["diafragma"] = false };
            return m;
        }
    }

    public partial class Vista3D
    {
        // Eixos da grade estrutural (posições em X e Y, m, e nomes) e os níveis onde ela é desenhada (MainWindow.AplicarGradeNaVista).
        public double[] EixosX = new double[0], EixosY = new double[0], NiveisEixos = new double[0];
        public string[] NomesX = new string[0], NomesY = new string[0];

        static readonly Pen PEixo = Caneta(TemaPlant.B(TemaPlant.Texto3, 0xB0), 1, new double[] { 10, 3, 2, 3 });
        static readonly Pen PEixoFraco = Caneta(TemaPlant.B(TemaPlant.Texto3, 0x48), 1, new double[] { 10, 3, 2, 3 });
        static readonly Pen PBalao = Caneta(TemaPlant.B(TemaPlant.Texto2, 0xC0), 1);
        static readonly Pen PBalaoFraco = Caneta(TemaPlant.B(TemaPlant.Texto2, 0x50), 1);

        // Cotas onde a grade aparece: o nível do plano de trabalho (horizontal; senão o térreo) primeiro, depois os outros níveis.
        IEnumerable<double> CotasEixos()
        {
            double z0 = plano.Normal == 'Z' ? plano.Valor : 0;
            yield return z0;
            foreach (double z in NiveisEixos.Distinct()) if (Math.Abs(z - z0) > 1e-6) yield return z;
        }

        // Linhas traço-ponto em cada nível com balões de nome fora da grade (na ponta de menor coordenada).
        void DesenharEixos(DrawingContext dc)
        {
            if (EixosX.Length == 0 && EixosY.Length == 0) return;
            double x0 = EixosX.DefaultIfEmpty(0).Min(), x1 = EixosX.DefaultIfEmpty(0).Max(), y0 = EixosY.DefaultIfEmpty(0).Min(), y1 = EixosY.DefaultIfEmpty(0).Max();
            double sobra = Math.Max(1.5, 0.08 * Math.Max(x1 - x0, y1 - y0));
            var cotas = CotasEixos().ToList();
            for (int k = cotas.Count - 1; k >= 0; k--)   // o nível ativo por último (por cima)
            {
                double z = cotas[k]; bool ativo = k == 0;
                void Linha(Point3D a, Point3D b, string nome)
                {
                    Point pa = Tela(a), pb = Tela(b);
                    dc.DrawLine(ativo ? PEixo : PEixoFraco, pa, pb);
                    if (string.IsNullOrEmpty(nome)) return;
                    Vector u = pa - pb; if (u.Length < 1) return; u.Normalize();
                    Point c = pa + u * 11;
                    dc.PushOpacity(ativo ? 1 : 0.45);
                    dc.DrawEllipse(TemaPlant.BCaixaHud, ativo ? PBalao : PBalaoFraco, c, 10, 10);
                    var ft = new FormattedText(nome, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, TfEntrada, 10.5, TemaPlant.BTexto1, 1.0);
                    dc.DrawText(ft, new Point(c.X - ft.Width / 2, c.Y - ft.Height / 2));
                    dc.Pop();
                }
                for (int i = 0; i < EixosX.Length; i++) Linha(new Point3D(EixosX[i], y0 - sobra, z), new Point3D(EixosX[i], y1 + sobra, z), i < NomesX.Length ? NomesX[i] : null);
                for (int j = 0; j < EixosY.Length; j++) Linha(new Point3D(x0 - sobra, EixosY[j], z), new Point3D(x1 + sobra, EixosY[j], z), j < NomesY.Length ? NomesY[j] : null);
            }
        }

        // Cruzamentos da grade em cada nível, o do plano de trabalho primeiro (no empate de distância na tela ele ganha).
        IEnumerable<Point3D> CruzamentosEixos() =>
            CotasEixos().SelectMany(z => EixosX.SelectMany(gx => EixosY.Select(gy => new Point3D(gx, gy, z))));
    }
}
