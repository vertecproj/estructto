// Geometria pura dos perfis extrudados para a exportação (Exportar.cs): mesma forma do modo Realista da Vista3D.
// A Vista3D (modo Realista nativo e Render3D) usa o mesmo CriarContorno: forma única para a tela e para os arquivos.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media.Media3D;

namespace NovoFtool
{
    // Sólido de uma barra: faces laterais (quadriláteros), tampas (contornos: 1º externo, depois furos) e tampas em
    // triângulos. Tudo orientado com a normal para fora (anti-horário visto de fora).
    public sealed class Solido
    {
        public readonly List<Point3D[]> Lados = new List<Point3D[]>();
        public readonly List<Point3D[]> TampaI = new List<Point3D[]>(), TampaJ = new List<Point3D[]>();
        public readonly List<Point3D[]> Triangulos = new List<Point3D[]>();
        public bool Concreto;
    }

    public static class GeometriaPerfis
    {
        // Contorno da seção no plano (u, v) = (largura, altura). Lacos: externo anti-horário, furos horários. Tampa: triângulos.
        public sealed class Contorno
        {
            public readonly List<Point[]> Lacos = new List<Point[]>(); public readonly List<Point> Tampa = new List<Point>(); public bool Concreto;
            public readonly List<bool> Liso = new List<bool>();   // por laço: normal suave (tubo circular) ou plana (chapas)
            public double H, B, Tw, Tf;                           // altura (v), largura (u), alma e mesa, m
            public string Forma = "RET";                         // Perfil3D.Forma (I, U, L, TR, TC...): as ligações de exemplo põem as chapas pela alma
            public double Larg => Math.Max(H, B);
            public void Laco(Point[] p, bool liso = false) { Lacos.Add(p); Liso.Add(liso); }
        }

        static Point[] Poli(params double[] uv) { var p = new Point[uv.Length / 2]; for (int k = 0; k < p.Length; k++) p[k] = new Point(uv[2 * k], uv[2 * k + 1]); return p; }

        public const int SegmentosTubo = 24;
        static Point[] Circulo(double r)
        {
            var p = new Point[SegmentosTubo];
            for (int k = 0; k < p.Length; k++) { double a = 2 * Math.PI * k / p.Length; p[k] = new Point(r * Math.Cos(a), r * Math.Sin(a)); }
            return p;
        }

        static void Ret(Contorno f, double u0, double v0, double u1, double v1)
        { f.Tampa.AddRange(new[] { new Point(u0, v0), new Point(u1, v0), new Point(u1, v1), new Point(u0, v0), new Point(u1, v1), new Point(u0, v1) }); }

        static void Anel(Contorno f, Point[] ext, Point[] inn)
        {
            for (int i = 0; i < ext.Length; i++) { int j = (i + 1) % ext.Length; f.Tampa.AddRange(new[] { ext[i], ext[j], inn[j], ext[i], inn[j], inn[i] }); }
        }

        // Igual a Vista3D.CriarForma (padrao = lado quando a seção não tem dados).
        public static Contorno CriarContorno(Perfil3D p, double padrao)
        {
            var f = new Contorno();
            double h = p != null && p.H > 0 ? p.H : padrao, b = p != null && p.B > 0 ? p.B : h;
            f.Concreto = p?.Concreto ?? false; f.H = h; f.B = b; f.Forma = p?.Forma ?? "RET";
            double tw = Math.Min(p != null && p.Tw > 0 ? p.Tw : h * 0.06, b * 0.45), tf = Math.Min(p != null && p.Tf > 0 ? p.Tf : h * 0.08, h * 0.45);
            f.Tw = tw; f.Tf = tf;
            double hb = b / 2, hh = h / 2;
            switch (p?.Forma)
            {
                case "I":
                    f.Laco(Poli(-hb, -hh, hb, -hh, hb, -hh + tf, tw / 2, -hh + tf, tw / 2, hh - tf, hb, hh - tf, hb, hh, -hb, hh, -hb, hh - tf, -tw / 2, hh - tf, -tw / 2, -hh + tf, -hb, -hh + tf));
                    Ret(f, -hb, -hh, hb, -hh + tf); Ret(f, -tw / 2, -hh + tf, tw / 2, hh - tf); Ret(f, -hb, hh - tf, hb, hh);
                    break;
                case "U":
                    f.Laco(Poli(-hb, -hh, hb, -hh, hb, -hh + tf, -hb + tw, -hh + tf, -hb + tw, hh - tf, hb, hh - tf, hb, hh, -hb, hh));
                    Ret(f, -hb, -hh, hb, -hh + tf); Ret(f, -hb, -hh + tf, -hb + tw, hh - tf); Ret(f, -hb, hh - tf, hb, hh);
                    break;
                case "L":
                    f.Laco(Poli(-hb, -hh, hb, -hh, hb, -hh + tw, -hb + tw, -hh + tw, -hb + tw, hh, -hb, hh));
                    Ret(f, -hb, -hh, hb, -hh + tw); Ret(f, -hb, -hh + tw, -hb + tw, hh);
                    break;
                case "TR":
                {
                    double t = Math.Min(tw, Math.Min(hb, hh) * 0.9);
                    Point[] e = Poli(-hb, -hh, hb, -hh, hb, hh, -hb, hh), i = Poli(-hb + t, -hh + t, hb - t, -hh + t, hb - t, hh - t, -hb + t, hh - t);
                    f.Laco(e); f.Laco(i.Reverse().ToArray()); Anel(f, e, i);
                    break;
                }
                case "TC":
                {
                    double r = h / 2, t = Math.Min(tw, r * 0.9);
                    Point[] e = Circulo(r), i = Circulo(r - t);
                    f.Laco(e, true); f.Laco(i.Reverse().ToArray(), true); Anel(f, e, i);
                    break;
                }
                case "CIRC":
                {
                    Point[] e = Circulo(h / 2); f.Laco(e, true);
                    for (int k = 0; k < e.Length; k++) f.Tampa.AddRange(new[] { new Point(0, 0), e[k], e[(k + 1) % e.Length] });
                    break;
                }
                default:
                    f.Laco(Poli(-hb, -hh, hb, -hh, hb, hh, -hb, hh));
                    Ret(f, -hb, -hh, hb, hh);
                    break;
            }
            if (f.Lacos.Count == 1 && !f.Liso[0])   // I, U, L: tampa pelo próprio contorno (os retângulos davam vértices em T: frestas que piscam no 3D)
            {
                Point[] l = f.Lacos[0]; f.Tampa.Clear();
                foreach (int[] t in Render3DGeometria.Triangular(l)) { f.Tampa.Add(l[t[0]]); f.Tampa.Add(l[t[1]]); f.Tampa.Add(l[t[2]]); }
            }
            return f;
        }

        // Ponto cardinal (docs/MODELO-ESPACIAL.md, Plant 3D): 1..9 como o teclado numérico olhando a seção com v para cima
        // (7 8 9 em cima, 4 5 6 no meio, 1 2 3 embaixo; 4 = lado +u), 10 = centroide. Devolve o deslocamento (u, v) em m a somar
        // ao eixo da barra para o contorno (centrado em 0) ficar com esse ponto sobre o eixo: cardinal 2 (base) = perfil sobe H/2.
        public static Point Cardinal(Contorno f, int cardinal)
        {
            if (cardinal == 10)
            {
                double A = 0, su = 0, sv = 0;   // centroide pelos triângulos da tampa
                for (int t = 0; t + 2 < f.Tampa.Count; t += 3)
                {
                    Point p = f.Tampa[t], q = f.Tampa[t + 1], r = f.Tampa[t + 2];
                    double a = Math.Abs((q.X - p.X) * (r.Y - p.Y) - (r.X - p.X) * (q.Y - p.Y)) / 2;
                    A += a; su += a * (p.X + q.X + r.X) / 3; sv += a * (p.Y + q.Y + r.Y) / 3;
                }
                return A > 0 ? new Point(-su / A, -sv / A) : new Point();
            }
            if (cardinal < 1 || cardinal > 9 || cardinal == 5) return new Point();
            int col = (cardinal - 1) % 3, lin = (cardinal - 1) / 3;   // col 0 = 1/4/7 (+u), lin 0 = 1/2/3 (base)
            return new Point(col == 0 ? -f.B / 2 : col == 2 ? f.B / 2 : 0, lin == 0 ? f.H / 2 : lin == 2 ? -f.H / 2 : 0);
        }

        // Extrusão de a até b com o mesmo triedro da Vista3D: v = Modelo3D.EixoY(e, rot), u = v × e. k = escala (m → mm: 1000).
        // ca, cb: planos de corte das pontas (Render3DGeometria.Juntas, m); só o plano (o 2º plano e a boca de peixe deixariam
        // as tampas fora de um plano, e o STEP facetado quer faces planas). ponytail: some quando o STEP tiver faces curvas.
        public static Solido Extrudar(Contorno f, Point3D a, Point3D b, double rotGraus, double k, int cardinal = 5, Corte ca = null, Corte cb = null)
        {
            var s = new Solido { Concreto = f.Concreto };
            Vector3D e = b - a; if (e.Length < 1e-9) return s;
            Vector3D v = Modelo3D.EixoY(e, rotGraus), u = Vector3D.CrossProduct(v, e / e.Length);
            Point dc = Cardinal(f, cardinal); a += u * dc.X + v * dc.Y; b += u * dc.X + v * dc.Y;
            Vector3D en = e / e.Length;
            Point3D Em(Point3D o, Point q)
            {
                Point3D p = o + u * q.X + v * q.Y;   // m
                Corte c = o == a ? ca : cb; double nd = c == null ? 0 : Vector3D.DotProduct(en, c.N);
                if (Math.Abs(nd) > 1e-6) p += en * (Vector3D.DotProduct(c.P - p, c.N) / nd);
                return new Point3D(p.X * k, p.Y * k, p.Z * k);
            }
            Point3D A = a, B = b;
            foreach (Point[] l in f.Lacos)
            {
                int n = l.Length;
                for (int i = 0; i < n; i++) { int j = (i + 1) % n; s.Lados.Add(new[] { Em(A, l[i]), Em(A, l[j]), Em(B, l[j]), Em(B, l[i]) }); }
                s.TampaJ.Add(l.Select(q => Em(B, q)).ToArray());                // normal +e: como está
                s.TampaI.Add(l.Reverse().Select(q => Em(A, q)).ToArray());      // normal -e: invertido
            }
            for (int t = 0; t < f.Tampa.Count; t += 3)
            {
                s.Triangulos.Add(new[] { Em(B, f.Tampa[t]), Em(B, f.Tampa[t + 1]), Em(B, f.Tampa[t + 2]) });
                s.Triangulos.Add(new[] { Em(A, f.Tampa[t + 2]), Em(A, f.Tampa[t + 1]), Em(A, f.Tampa[t]) });
            }
            return s;
        }
    }
}
