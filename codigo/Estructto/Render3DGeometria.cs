// Geometria do Render3D (DX11): malha acumulada com cor por vértice e arestas, recortes nas juntas (barras param na face
// da peça contínua, esquadria onde dois banzos se encontram) e primitivas das ligações (chapa, parafuso, cordão de solda).
// Tudo em m, no referencial global da Vista3D (X, Y horizontais, Z para cima). Só matemática: nada do Helix entra aqui.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Windows;
using System.Windows.Media.Media3D;
using HelixToolkit.Maths;
using Contorno = NovoFtool.GeometriaPerfis.Contorno;

namespace NovoFtool
{
    // Malha acumulada: posições, normais, cor por vértice e triângulos; arestas como pares de pontos (linhas de 1 px no DX).
    public sealed class MalhaDx
    {
        public readonly List<Vector3> P = new List<Vector3>(), N = new List<Vector3>();
        public readonly List<Color4> C = new List<Color4>();
        public readonly List<int> T = new List<int>();
        public readonly List<Vector3> L = new List<Vector3>();
        public readonly List<int> Ini = new List<int>(); public readonly List<object> Obj = new List<object>();   // picking: 1º vértice de cada objeto
        public bool Vazia => T.Count == 0;
        public void Marcar(object o) { Ini.Add(P.Count); Obj.Add(o); }

        public static Vector3 V(Point3D p) => new Vector3((float)p.X, (float)p.Y, (float)p.Z);
        public static Vector3 V(Vector3D v) => new Vector3((float)v.X, (float)v.Y, (float)v.Z);
        public static Color4 Cor(System.Windows.Media.Color c, float a = 1) => new Color4(c.R / 255f, c.G / 255f, c.B / 255f, a);

        void Vert(Point3D p, Vector3D n, Color4 c) { P.Add(V(p)); N.Add(V(n)); C.Add(c); }

        public void Tri(Point3D a, Point3D b, Point3D c, Color4 cor)
        {
            Vector3D n = Vector3D.CrossProduct(b - a, c - a); if (n.Length < 1e-18) return; n.Normalize();
            int i = P.Count; Vert(a, n, cor); Vert(b, n, cor); Vert(c, n, cor);
            T.Add(i); T.Add(i + 1); T.Add(i + 2);
        }

        public void Quad(Point3D a, Point3D b, Point3D c, Point3D d, Color4 cor)
        {
            Vector3D n = Vector3D.CrossProduct(c - a, d - b); if (n.Length < 1e-18) return; n.Normalize();
            Quad(a, b, c, d, n, n, n, n, cor);
        }

        public void Quad(Point3D a, Point3D b, Point3D c, Point3D d, Vector3D na, Vector3D nb, Vector3D nc, Vector3D nd, Color4 cor)
        {
            int i = P.Count; Vert(a, na, cor); Vert(b, nb, cor); Vert(c, nc, cor); Vert(d, nd, cor);
            T.Add(i); T.Add(i + 1); T.Add(i + 2); T.Add(i); T.Add(i + 2); T.Add(i + 3);
        }

        public void Aresta(Point3D a, Point3D b) { L.Add(V(a)); L.Add(V(b)); }

        // Caixa alinhada aos eixos (pedestais, blocos).
        public void Caixa(Point3D c, double dx, double dy, double dz, Color4 cor, bool arestas = false)
        {
            Vector3D x = new Vector3D(dx / 2, 0, 0), y = new Vector3D(0, dy / 2, 0), z = new Vector3D(0, 0, dz / 2);
            Point3D[] p = { c - x - y - z, c + x - y - z, c + x + y - z, c - x + y - z, c - x - y + z, c + x - y + z, c + x + y + z, c - x + y + z };
            int[][] f = { new[] { 0, 3, 2, 1 }, new[] { 4, 5, 6, 7 }, new[] { 0, 1, 5, 4 }, new[] { 1, 2, 6, 5 }, new[] { 2, 3, 7, 6 }, new[] { 3, 0, 4, 7 } };
            foreach (int[] q in f) Quad(p[q[0]], p[q[1]], p[q[2]], p[q[3]], cor);
            if (!arestas) return;
            int[] e = { 0, 1, 1, 2, 2, 3, 3, 0, 4, 5, 5, 6, 6, 7, 7, 4, 0, 4, 1, 5, 2, 6, 3, 7 };
            for (int k = 0; k < e.Length; k += 2) Aresta(p[e[k]], p[e[k + 1]]);
        }

        // Tubo de a até b: n lados, normais suaves (liso) ou planas; tampas opcionais. r = raio (hexágono: raio do círculo circunscrito).
        public void Tubo(Point3D a, Point3D b, double r, int n, bool liso, Color4 cor, bool tampas = true, double fase = 0)
        {
            Vector3D e = b - a; if (e.Length < 1e-12) return; e.Normalize();
            Vector3D u = Vector3D.CrossProduct(e, Math.Abs(e.Z) > 0.9 ? new Vector3D(1, 0, 0) : new Vector3D(0, 0, 1)); u.Normalize();
            Vector3D w = Vector3D.CrossProduct(e, u);
            var rad = new Vector3D[n];
            for (int k = 0; k < n; k++) { double t = 2 * Math.PI * k / n + fase; rad[k] = u * Math.Cos(t) + w * Math.Sin(t); }
            for (int k = 0; k < n; k++)
            {
                int j = (k + 1) % n;
                Point3D p0 = a + rad[k] * r, p1 = a + rad[j] * r, p2 = b + rad[j] * r, p3 = b + rad[k] * r;
                if (liso) Quad(p0, p1, p2, p3, rad[k], rad[j], rad[j], rad[k], cor); else Quad(p0, p1, p2, p3, cor);
            }
            if (!tampas) return;
            for (int k = 1; k < n - 1; k++)
            {
                Tri(b, b + rad[k] * r, b + rad[k + 1] * r, cor);
                Tri(a, a + rad[k + 1] * r, a + rad[k] * r, cor);
            }
        }

        // Chapa: polígono (m) no plano (u, w) a partir de origem, extrudado de 0 a esp ao longo de n. Arestas nas duas faces.
        public void Chapa(Point3D origem, Vector3D u, Vector3D w, Vector3D n, Point[] contorno, double esp, Color4 cor, bool arestas = true, bool faces = true)
        {
            if (contorno == null || contorno.Length < 3) return;
            int m = contorno.Length;
            var p0 = new Point3D[m]; var p1 = new Point3D[m];
            for (int k = 0; k < m; k++) { p0[k] = origem + u * contorno[k].X + w * contorno[k].Y; p1[k] = p0[k] + n * esp; }
            bool ccw = Area(contorno) > 0 == Vector3D.DotProduct(Vector3D.CrossProduct(u, w), n) > 0;   // anti-horário olhando contra n (vale para (u, w, n) canhoto)
            if (faces) foreach (int[] t in Render3DGeometria.Triangular(contorno))
            {
                if (ccw) { Tri(p1[t[0]], p1[t[1]], p1[t[2]], cor); Tri(p0[t[2]], p0[t[1]], p0[t[0]], cor); }
                else { Tri(p1[t[2]], p1[t[1]], p1[t[0]], cor); Tri(p0[t[0]], p0[t[1]], p0[t[2]], cor); }
            }
            for (int k = 0; k < m; k++)
            {
                int j = (k + 1) % m;
                if (faces) { if (ccw) Quad(p0[k], p0[j], p1[j], p1[k], cor); else Quad(p0[j], p0[k], p1[k], p1[j], cor); }
                if (arestas) { Aresta(p0[k], p0[j]); Aresta(p1[k], p1[j]); Aresta(p0[k], p1[k]); }
            }
        }

        static double Area(Point[] c)
        {
            double a = 0;
            for (int k = 0; k < c.Length; k++) { Point p = c[k], q = c[(k + 1) % c.Length]; a += p.X * q.Y - q.X * p.Y; }
            return a / 2;
        }
    }

    // Plano de corte de uma ponta de barra: ponto e normal (a extrusão termina nele), recuo a partir do nó e a peça contínua.
    public sealed class Corte
    {
        public Point3D P; public Vector3D N; public double Recuo; public Barra3D Contra; public bool Esquadria;
    }

    public static class Render3DGeometria
    {
        // ---------- juntas ----------

        public sealed class Ponta
        {
            public Barra3D B; public bool EmI; public Vector3D D;   // D: direção saindo do nó ao longo da barra
            public double L; public Contorno F; public Vector3D U, V; public Point3D No;
            public int Rank => Math.Abs(D.Z) > 0.95 ? 3 : Math.Abs(D.Z) < 0.2 ? 2 : 1;   // pilar > viga > diagonal
        }

        // Para cada ponta de barra que precisa de recorte, o plano de corte. Regras (docs/v3/RENDER.md):
        // pilar contínuo; viga para na face do pilar menos a folga; diagonal recua até a face da peça contínua (+ folga maior);
        // dois banzos do mesmo nível que se encontram sem peça contínua = esquadria pela bissetriz; viga secundária para na face
        // da viga mais alta. folga(b, emI) = vão entre a ponta e a face (m); recuoFixo(b, emI) = recuo imposto (NaN = regra).
        public static Dictionary<(Barra3D, bool), Corte> Juntas(Modelo3D mod, Func<Barra3D, (Point3D A, Point3D C)> pontos, Func<Barra3D, Contorno> forma,
                                                                  Func<Barra3D, bool, double> folga, Func<Barra3D, bool, double> recuoFixo)
        {
            var porNo = new Dictionary<int, List<Ponta>>();
            foreach (Barra3D b in mod.Barras)
            {
                var (a, c) = pontos(b); Vector3D e = c - a; double len = e.Length; if (len < 1e-9) continue; e /= len;
                Vector3D v = Modelo3D.EixoY(e, b.Rot + b.RotPerfil), u = Vector3D.CrossProduct(v, e);
                Contorno f = forma(b);
                foreach (bool emI in new[] { true, false })
                {
                    int id = emI ? b.Ni : b.Nj;
                    if (!porNo.TryGetValue(id, out var l)) porNo[id] = l = new List<Ponta>();
                    l.Add(new Ponta { B = b, EmI = emI, D = emI ? e : -e, L = len, F = f, U = u, V = v, No = emI ? a : c });
                }
            }
            var cortes = new Dictionary<(Barra3D, bool), Corte>();
            foreach (var kv in porNo)
            {
                var ord = kv.Value.OrderByDescending(p => p.Rank).ThenByDescending(p => p.F.H).ThenByDescending(p => p.L).ToList();
                Ponta top = ord[0];
                Ponta par = ord.Skip(1).FirstOrDefault(q => q.Rank == top.Rank && Vector3D.DotProduct(q.D, top.D) < -0.98);
                var continuas = new HashSet<Ponta> { top }; if (par != null) continuas.Add(par);
                int nTop = ord.Count(q => q.Rank == top.Rank);
                if (par == null && nTop == 2 && top.Rank <= 2)   // cumeeira ou canto de banzos: esquadria
                {
                    Ponta o2 = ord.First(q => q != top && q.Rank == top.Rank);
                    Vector3D n = top.D - o2.D; n.Normalize();
                    foreach (Ponta q in new[] { top, o2 }) cortes[(q.B, q.EmI)] = new Corte { P = q.No, N = n, Esquadria = true, Contra = q == top ? o2.B : top.B };
                    continuas.Add(o2);
                }
                foreach (Ponta q in ord)
                {
                    double fixo = recuoFixo(q.B, q.EmI);
                    if (continuas.Contains(q) && double.IsNaN(fixo)) continue;
                    double recuo = double.IsNaN(fixo) ? ord.Where(t => continuas.Contains(t) || t.Rank > q.Rank).Select(t => FaceDist(q.D, t)).DefaultIfEmpty(0).Max() + folga(q.B, q.EmI) : fixo;
                    recuo = Math.Min(recuo, 0.3 * q.L);
                    if (recuo <= 1e-6) continue;
                    cortes[(q.B, q.EmI)] = new Corte { P = q.No + q.D * recuo, N = q.D, Recuo = recuo, Contra = continuas.Contains(q) ? null : top.B };
                }
            }
            return cortes;
        }

        // Distância, ao longo de d (saindo do nó), até sair da caixa da seção da peça contínua.
        public static double FaceDist(Vector3D d, Ponta top)
        {
            double du = Vector3D.DotProduct(d, top.U), dv = Vector3D.DotProduct(d, top.V), t = double.PositiveInfinity;
            if (Math.Abs(du) > 1e-6) t = Math.Min(t, top.F.B / 2 / Math.Abs(du));
            if (Math.Abs(dv) > 1e-6) t = Math.Min(t, top.F.H / 2 / Math.Abs(dv));
            return double.IsInfinity(t) ? 0 : t;
        }

        // ---------- extrusão ----------

        // Extrusão do contorno de a até c com os planos de corte das pontas (null = reta no nó); (u, v, e) triedro direito.
        public static void Extrudar(MalhaDx m, Contorno f, Point3D a, Point3D c, Vector3D u, Vector3D v, Corte ca, Corte cc, Color4 cor, bool arestas, bool faces = true)
        {
            Vector3D e = c - a; double len = e.Length; if (len < 1e-9) return; e /= len;
            Point3D Fim(Point3D p, Corte k)
            {
                if (k == null) return p;
                double nd = Vector3D.DotProduct(e, k.N); if (Math.Abs(nd) < 1e-6) return p;
                return p + e * (Vector3D.DotProduct(k.P - p, k.N) / nd);
            }
            if (Vector3D.DotProduct(Fim(c, cc) - Fim(a, ca), e) <= 1e-6) return;   // recortes engoliram a barra
            for (int li = 0; li < f.Lacos.Count; li++)
            {
                Point[] q = f.Lacos[li]; bool liso = f.Liso[li]; int n = q.Length;
                var pa = new Point3D[n]; var pc = new Point3D[n]; var nr = new Vector3D[n];
                for (int k = 0; k < n; k++)
                {
                    Vector3D off = u * q[k].X + v * q[k].Y;
                    pa[k] = Fim(a + off, ca); pc[k] = Fim(c + off, cc);
                    nr[k] = off; if (nr[k].Length > 1e-12) nr[k].Normalize();   // liso: normal radial
                }
                for (int k = 0; k < n; k++)
                {
                    int j = (k + 1) % n;
                    if (faces) { if (liso) m.Quad(pa[k], pa[j], pc[j], pc[k], nr[k], nr[j], nr[j], nr[k], cor); else m.Quad(pa[k], pa[j], pc[j], pc[k], cor); }
                    if (arestas) { if (!liso) m.Aresta(pa[k], pc[k]); m.Aresta(pa[k], pa[j]); m.Aresta(pc[k], pc[j]); }
                }
            }
            if (!faces) return;
            for (int t = 0; t + 2 < f.Tampa.Count; t += 3)   // tampas nos planos de corte
            {
                Point3D A(Point p) => Fim(a + u * p.X + v * p.Y, ca);
                Point3D Cc(Point p) => Fim(c + u * p.X + v * p.Y, cc);
                m.Tri(Cc(f.Tampa[t]), Cc(f.Tampa[t + 1]), Cc(f.Tampa[t + 2]), cor);
                m.Tri(A(f.Tampa[t + 2]), A(f.Tampa[t + 1]), A(f.Tampa[t]), cor);
            }
        }

        // ---------- primitivas das ligações ----------

        // Parafuso sextavado: cabeça + arruela, fuste, arruela + porca na ponta. cabeca = face de apoio da cabeça; eixo = cabeça -> porca.
        public static void Parafuso(MalhaDx m, Point3D cabeca, Vector3D eixo, double d, double comp, Color4 cor)
        {
            if (eixo.Length < 1e-12 || d <= 0 || comp <= 0) return;
            eixo.Normalize();
            double rc = 0.95 * d, hc = 0.65 * d, ra = 1.05 * d, ha = 0.14 * d, rp = 0.95 * d, hp = 0.8 * d;
            m.Tubo(cabeca - eixo * (ha + hc), cabeca - eixo * ha, rc, 6, false, cor);     // cabeça
            m.Tubo(cabeca - eixo * ha, cabeca, ra, 12, true, cor);                        // arruela
            m.Tubo(cabeca, cabeca + eixo * comp, d / 2, 8, true, cor, false);             // fuste
            Point3D fim = cabeca + eixo * comp;
            m.Tubo(fim - eixo * (hp + ha), fim - eixo * hp, ra, 12, true, cor);          // arruela da porca
            m.Tubo(fim - eixo * hp, fim, rp, 6, false, cor, true, Math.PI / 6);           // porca
        }

        // Cordão de solda: tubo fino ao longo da polilinha (raio ~ perna/2). ponytail: sem o perfil triangular do filete.
        public static void Cordao(MalhaDx m, IList<Point3D> pts, double perna, Color4 cor)
        {
            double r = Math.Max(0.002, perna * 0.55);
            for (int k = 0; k + 1 < pts.Count; k++) m.Tubo(pts[k], pts[k + 1], r, 6, true, cor, true);
        }

        // Triangulação por orelhas (polígono simples, qualquer sentido). Devolve índices.
        public static List<int[]> Triangular(Point[] c)
        {
            var r = new List<int[]>(); int n = c.Length; if (n < 3) return r;
            var idx = Enumerable.Range(0, n).ToList();
            double area = 0; for (int k = 0; k < n; k++) area += c[k].X * c[(k + 1) % n].Y - c[(k + 1) % n].X * c[k].Y;
            if (area < 0) idx.Reverse();
            double Cr(Point a, Point b, Point p) => (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
            int guarda = 0;
            while (idx.Count > 3 && guarda++ < 10 * n)
            {
                bool cortou = false;
                for (int i = 0; i < idx.Count; i++)
                {
                    int i0 = idx[(i + idx.Count - 1) % idx.Count], i1 = idx[i], i2 = idx[(i + 1) % idx.Count];
                    Point a = c[i0], b = c[i1], d = c[i2];
                    if (Cr(a, b, d) <= 1e-15) continue;   // reflexo
                    bool dentro = false;
                    foreach (int j in idx)
                    {
                        if (j == i0 || j == i1 || j == i2) continue;
                        Point p = c[j];
                        if (Cr(a, b, p) >= 0 && Cr(b, d, p) >= 0 && Cr(d, a, p) >= 0) { dentro = true; break; }
                    }
                    if (dentro) continue;
                    r.Add(new[] { i0, i1, i2 }); idx.RemoveAt(i); cortou = true; break;
                }
                if (!cortou) break;   // polígono degenerado: o resto vira um leque
            }
            for (int i = 1; i + 1 < idx.Count; i++) r.Add(new[] { idx[0], idx[i], idx[i + 1] });
            return r;
        }

        // Autoverificação (Estructto.exe --testes-exportar também chama): triangulação e recortes.
        public static void Testar(Action<bool, string> afirmar)
        {
            var quad = new[] { new Point(0, 0), new Point(1, 0), new Point(1, 1), new Point(0, 1) };
            afirmar(Triangular(quad).Count == 2, "triangular quadrado = 2 triângulos");
            var l = new[] { new Point(0, 0), new Point(2, 0), new Point(2, 1), new Point(1, 1), new Point(1, 2), new Point(0, 2) };   // L côncavo
            var tri = Triangular(l);
            double soma = tri.Sum(t => Math.Abs((l[t[1]].X - l[t[0]].X) * (l[t[2]].Y - l[t[0]].Y) - (l[t[2]].X - l[t[0]].X) * (l[t[1]].Y - l[t[0]].Y)) / 2);
            afirmar(tri.Count == 4 && Math.Abs(soma - 3) < 1e-9, "triangular L côncavo = 4 triângulos, área 3");

            // pórtico: pilar contínuo, viga para na face do pilar + folga; diagonal recua mais; cumeeira em esquadria
            var mod = new Modelo3D();
            mod.Nos.Add(new No3D { Id = 1 }); mod.Nos.Add(new No3D { Id = 2, Z = 3 }); mod.Nos.Add(new No3D { Id = 3, X = 4, Z = 3 }); mod.Nos.Add(new No3D { Id = 4, X = 2, Z = 4 });
            var pilar = new Barra3D { Id = 1, Ni = 1, Nj = 2 }; var viga = new Barra3D { Id = 2, Ni = 2, Nj = 3 }; var diag = new Barra3D { Id = 3, Ni = 1, Nj = 3 };
            var r1 = new Barra3D { Id = 4, Ni = 2, Nj = 4 }; var r2 = new Barra3D { Id = 5, Ni = 4, Nj = 3 };
            mod.Barras.AddRange(new[] { pilar, viga, diag, r1, r2 });
            var nos = mod.Nos.ToDictionary(n => n.Id);
            Contorno fPilar = GeometriaPerfis.CriarContorno(new Perfil3D { Forma = "I", H = 0.30, B = 0.20, Tw = 0.01, Tf = 0.012 }, 0.1);
            Contorno fViga = GeometriaPerfis.CriarContorno(new Perfil3D { Forma = "I", H = 0.25, B = 0.15, Tw = 0.008, Tf = 0.01 }, 0.1);
            var cortes = Juntas(mod, b => (new Point3D(nos[b.Ni].X, 0, nos[b.Ni].Z), new Point3D(nos[b.Nj].X, 0, nos[b.Nj].Z)), b => b == pilar ? fPilar : fViga,
                                (b, emI) => 0.01, (b, emI) => double.NaN);
            afirmar(!cortes.ContainsKey((pilar, true)) && !cortes.ContainsKey((pilar, false)), "pilar contínuo (sem corte)");
            afirmar(cortes.TryGetValue((viga, true), out Corte cv) && Math.Abs(cv.Recuo - (0.15 + 0.01)) < 1e-9 && cv.Contra == pilar, "viga para na face da mesa do pilar (H/2 = 0,15) + folga 0,01");
            afirmar(cortes.TryGetValue((diag, true), out Corte cd) && cd.Recuo > 0.10 && cd.Contra == pilar, "diagonal recua além da face do pilar");
            afirmar(cortes.TryGetValue((r1, false), out Corte c1) && c1.Esquadria && cortes.TryGetValue((r2, true), out Corte c2) && c2.Esquadria, "cumeeira: esquadria nos dois banzos");
            afirmar(Math.Abs(Vector3D.DotProduct(c1.N, new Vector3D(1, 0, 0))) > 0.99, "plano da esquadria vertical na cumeeira simétrica");

            var m = new MalhaDx();
            Vector3D e = new Vector3D(1, 0, 0), v = new Vector3D(0, 0, 1), u = Vector3D.CrossProduct(v, e);
            Extrudar(m, fViga, new Point3D(0, 0, 0), new Point3D(4, 0, 0), u, v, new Corte { P = new Point3D(0.5, 0, 0), N = e, Recuo = 0.5 }, null, new Color4(1, 1, 1, 1), true);
            afirmar(m.P.Count > 0 && m.P.Min(p => p.X) >= 0.5f - 1e-5f && m.P.Max(p => p.X) <= 4 + 1e-5f, "extrusão começa no plano de corte (x = 0,5)");
        }
    }
}
