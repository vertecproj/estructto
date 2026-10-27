// W8-CONTRAV (docs/v3/W8-CONTRAV.md): peças do contrato que só o projetista desenha (o Realista desenha as barras do modelo).
// `perfis_barra`: placas no formato de `chapas` (origem numa face, sólido até origem + espessura·normal) com as abas, almas e
// mesas reais da barra ligada ao gusset (diagonal, montante, banzo); `camada` 0 = perfil (banzo), senão por cima do gusset
// como a cantoneira da W7-LIG. `linhas`: estilo "tracejada" (espraiamento de Whitmore) ou "cota" (cota explícita do motor,
// traçada para o lado `lado`), com `texto`.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Portico;
using static NovoFtool.UiAnalise;

namespace NovoFtool
{
    public partial class GeoLig
    {
        public class Linha { public Point3D[] P; public string Txt = ""; public bool Cota; public Vector3D Lado; }
        public readonly List<Linha> Linhas = new List<Linha>();
        public Vector3D? VistaX, VistaZ;   // `vista` {x, z}: eixos horizontal e vertical da elevação (banzo/viga na horizontal)

        static void LerW8(GeoLig r, JsonNode g, Func<string, bool> F)
        {
            if (g["vista"] is JsonObject vs && vs["x"] is JsonArray && vs["z"] is JsonArray)
            {
                var x = V3(vs["x"], new Vector3D(1, 0, 0)); var z = V3(vs["z"], new Vector3D(0, 0, 1));
                if (x.Length > 1e-9 && z.Length > 1e-9) { x.Normalize(); z.Normalize(); r.VistaX = x; r.VistaZ = z; }
            }
            foreach (var c in Lista(g["perfis_barra"]))
            {
                var o = P3(c["origem"]); var n = V3(c["normal"], new Vector3D(0, 1, 0)); var u = V3(c["u"], new Vector3D(1, 0, 0));
                n.Normalize(); u.Normalize();
                var v = Vector3D.CrossProduct(n, u); double t = D(c, "espessura");
                var pts = Lista(c["contorno"]).OfType<JsonArray>().Where(p => p.Count >= 2).Select(p => o + n * (t / 2) + D(p[0]) * u + D(p[1]) * v).ToArray();
                bool perfil = D(c, "camada") == 0;
                if (perfil) r.NomeSuporte = S(c, "perfil");   // rótulo do banzo, como o do apoio
                if (pts.Length >= 3 && Ok(t))
                    r.Pecas.Add(new Prisma { Base = pts, N = n, T = t, Camada = perfil ? 0 : 2, Grupo = perfil ? "suporte" : "cantoneiras", Nome = S(c, "perfil"), Falha = F(perfil ? "suporte" : "cantoneiras"), Tag = S(c, "campo") });
            }
            foreach (var l in Lista(g["linhas"]))
            {
                var pts = Lista(l["pontos"]).OfType<JsonArray>().Select(P3).ToArray();
                if (pts.Length >= 2) r.Linhas.Add(new Linha { P = pts, Txt = S(l, "texto"), Cota = S(l, "estilo") == "cota", Lado = V3(l["lado"], new Vector3D(0, 0, 1)) });
            }
        }
    }

    public partial class DesenhoLigacao
    {
        // linhas do motor na vista: some se ficar de topo (menos de 14 px); texto com fundo, como nas cotas
        void Linhas(DrawingContext dc, GeoLig g, Func<Point3D, Point> P)
        {
            void Etiqueta(string s, Point m, bool forte = false)
            {
                if (string.IsNullOrEmpty(s)) return;
                var ft = Txt(s, 11, forte ? TemaPlant.BTexto1 : TemaPlant.BRealceClaro);
                var rr = new Rect(m.X - ft.Width / 2 - 3, m.Y - ft.Height / 2, ft.Width + 6, ft.Height);
                dc.DrawRoundedRectangle(TemaPlant.BFundoTextoCanvas, null, rr, 2, 2);
                dc.DrawText(ft, new Point(rr.X + 3, rr.Y));
            }
            foreach (var l in g.Linhas)
            {
                var sp = l.P.Select(P).ToList();
                double comp = 0; int k = 1;
                for (int i = 1; i < sp.Count; i++) { comp += (sp[i] - sp[i - 1]).Length; if ((sp[i] - sp[i - 1]).Length > (sp[k] - sp[k - 1]).Length) k = i; }
                if (comp < 14) continue;
                if (!l.Cota)
                {
                    // espraiamento visto de lado (todos os pontos numa reta na tela): não desenha
                    var e0 = sp.OrderByDescending(q => (q - sp[0]).Length).First() - sp[0];
                    if (e0.Length < 1 || sp.Max(q => Math.Abs(Vector.CrossProduct(e0, q - sp[0])) / e0.Length) < 4) continue;
                    dc.DrawGeometry(null, new Pen(TemaPlant.BRealceClaro, 1) { DashStyle = new DashStyle(new double[] { 6, 4 }, 0) }, Poli(sp, false));
                    var a0 = sp[k - 1]; var d0 = sp[k] - a0; var n0 = new Vector(-d0.Y, d0.X); n0.Normalize();
                    Etiqueta(l.Txt, a0 + d0 / 2 + n0 * 10);
                    continue;
                }
                var d = sp[1] - sp[0]; if (d.Length < 14) continue;
                var u = d / d.Length; var n = new Vector(-u.Y, u.X);
                var lado = P(l.P[0] + l.Lado * 10) - sp[0];
                if (lado.Length < 1e-6 || Math.Abs(lado / lado.Length * n) < 0.2) { if (Math.Abs(u.X) >= Math.Abs(u.Y) ? n.Y < 0 : n.X < 0) n = -n; }
                else if (lado * n < 0) n = -n;
                Point a = sp[0] + n * 18, b = sp[1] + n * 18;
                var pen = new Pen(TemaPlant.BTexto2, 1); var penL = new Pen(TemaPlant.BTextoMudo, 0.8) { DashStyle = new DashStyle(new double[] { 2, 3 }, 0) };
                dc.DrawLine(pen, a, b);
                foreach (var (q, o) in new[] { (a, sp[0]), (b, sp[1]) })
                {
                    dc.DrawLine(new Pen(TemaPlant.BTexto2, 1.3), q - new Vector(4, -4), q + new Vector(4, -4));
                    dc.DrawLine(penL, o + n * 3, q + n * 4);
                }
                Etiqueta(l.Txt, a + (b - a) / 2 + n * 9, true);
            }
        }
    }
}
