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
            f.Concreto = p?.Concreto ?? false; f.H = h; f.B = b;
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
            return f;
        }

        // Extrusão de a até b com o mesmo triedro da Vista3D: v = Modelo3D.EixoY(e, rot), u = v × e. k = escala (m → mm: 1000).
        public static Solido Extrudar(Contorno f, Point3D a, Point3D b, double rotGraus, double k)
        {
            var s = new Solido { Concreto = f.Concreto };
            Vector3D e = b - a; if (e.Length < 1e-9) return s;
            Vector3D v = Modelo3D.EixoY(e, rotGraus), u = Vector3D.CrossProduct(v, e / e.Length);
            Point3D A = new Point3D(a.X * k, a.Y * k, a.Z * k), B = new Point3D(b.X * k, b.Y * k, b.Z * k);
            Point3D Em(Point3D o, Point q) => o + u * (q.X * k) + v * (q.Y * k);
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
