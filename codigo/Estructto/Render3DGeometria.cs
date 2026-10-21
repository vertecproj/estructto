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

        public void TriN(Point3D a, Point3D b, Point3D c, Vector3D n, Color4 cor)   // vira o triângulo para a normal dada
        {
            if (Vector3D.DotProduct(Vector3D.CrossProduct(b - a, c - a), n) < 0) (b, c) = (c, b);
            Tri(a, b, c, cor);
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

        public void QuadN(Point3D a, Point3D b, Point3D c, Point3D d, Vector3D n, Color4 cor)   // vira o quadrilátero para a normal dada
        {
            if (Vector3D.DotProduct(Vector3D.CrossProduct(c - a, d - b), n) < 0) (b, d) = (d, b);
            Quad(a, b, c, d, cor);
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
            for (int k = 0; k < n; k++)   // leque fechado a partir do centro (antes faltavam 2 fatias: a peça parecia oca)
            {
                int j = (k + 1) % n;
                Tri(b, b + rad[k] * r, b + rad[j] * r, cor);
                Tri(a, a + rad[j] * r, a + rad[k] * r, cor);
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
        // Face: distância do nó à face da peça contínua, medida na normal da face (= ao longo da barra se ela chega perpendicular).
        // Inclinada: barra inclinada contra pilar; o plano de corte é paralelo à face do pilar (N = normal da face, horizontal), não perpendicular à barra.
        public double Face; public bool Inclinada;
        public bool NaMesa;   // a barra chega na face da mesa do pilar (eixo forte); falso = chega na ponta das mesas, ligação na alma (eixo fraco)
        // Recortes de sólido além do plano (contrato pontas, docs/v3/PONTAS-CONTRATO.md): Extra = 2º plano (recorte "angulo":
        // paralelo à face do apoio); Raio > 0 = cilindro do tubo receptor (boca de peixe, eixo EixoA por EixoO). Vale o mais fundo na barra.
        public Corte Extra; public Point3D EixoO; public Vector3D EixoA; public double Raio;
        public bool Motor;    // plano vindo das pontas do motor (sem a regra das juntas)
        public double FolgaAngulo = double.NaN; public bool Boca;   // pedidos do motor: recorte "angulo" (folga) e "boca_peixe"; Juntas monta no mundo
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
        // da viga mais alta. v5 (modelo com pontas do motor): plano angulo na barra oblíqua, boca de peixe, esquadria recuada
        // pela chapa e contínua = maior área; sem v5 fica a regra da v0.4 (Review Focus 3). folga(b, emI) = vão entre a ponta e a face (m); recuoFixo(b, emI) = recuo imposto (NaN = regra).
        public static Dictionary<(Barra3D, bool), Corte> Juntas(Modelo3D mod, Func<Barra3D, (Point3D A, Point3D C)> pontos, Func<Barra3D, Contorno> forma,
                                                                  Func<Barra3D, bool, double> folga, Func<Barra3D, bool, double> recuoFixo,
                                                                  Func<Barra3D, bool, Corte> motor = null, bool v5 = false)
        {
            var porNo = new Dictionary<int, List<Ponta>>();
            foreach (Barra3D b in mod.Barras)
            {
                var (a, c) = pontos(b); Vector3D e = c - a; double len = e.Length; if (len < 1e-9) continue; e /= len;
                Vector3D v = Modelo3D.EixoY(e, b.Rot + b.RotPerfil), u = Vector3D.CrossProduct(v, e);
                Contorno f = forma(b);
                Point dc = GeometriaPerfis.Cardinal(f, b.Cardinal); a += u * dc.X + v * dc.Y; c += u * dc.X + v * dc.Y;
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
                var ord = kv.Value.OrderByDescending(p => p.Rank).ThenByDescending(p => v5 ? AreaSecao(p.F) : p.F.H).ThenByDescending(p => p.L).ToList();   // mesma classe: a de maior área é a contínua (tesoura HP, não a terça Ue de mesma altura)
                Ponta top = ord[0];
                Ponta par = ord.Skip(1).FirstOrDefault(q => q.Rank == top.Rank && Vector3D.DotProduct(q.D, top.D) < -0.98);
                var continuas = new HashSet<Ponta> { top }; if (par != null) continuas.Add(par);
                int nTop = ord.Count(q => q.Rank == top.Rank);
                if (par == null && nTop == 2 && top.Rank <= 2)   // cumeeira ou canto de banzos: esquadria
                {
                    Ponta o2 = ord.First(q => q != top && q.Rank == top.Rank);
                    Vector3D n = top.D - o2.D; n.Normalize();
                    foreach (Ponta q in new[] { top, o2 })   // recuoFixo (chapa de topo da cumeeira): o plano recua a espessura da chapa para dentro da barra
                    {
                        double off = recuoFixo(q.B, q.EmI); Vector3D nq = Vector3D.DotProduct(n, q.D) > 0 ? n : -n;
                        cortes[(q.B, q.EmI)] = new Corte { P = q.No + nq * (double.IsNaN(off) || !v5 ? 0 : off), N = n, Esquadria = true, Contra = q == top ? o2.B : top.B };
                    }
                    continuas.Add(o2);
                }
                foreach (Ponta q in ord)
                {
                    Corte km = motor?.Invoke(q.B, q.EmI);
                    if (km != null)   // pontas do motor: o plano manda (sem o limite 0,3·L); angulo e boca de peixe montados no mundo
                    {
                        km.Motor = true; km.Recuo = Vector3D.DotProduct(km.P - q.No, q.D); km.Contra = q == top ? null : top.B;
                        km.NaMesa = top.F.H * Math.Abs(Vector3D.DotProduct(q.D, top.U)) <= top.F.B * Math.Abs(Vector3D.DotProduct(q.D, top.V));
                        if (!double.IsNaN(km.FolgaAngulo)) km.Extra = Angulo(q, top, km.FolgaAngulo);
                        if (km.Boca) Boca(km, q, top);
                        cortes[(q.B, q.EmI)] = km;
                        continue;
                    }
                    if (cortes.TryGetValue((q.B, q.EmI), out Corte ke) && ke.Esquadria) continue;   // já cortada na esquadria
                    double fixo = recuoFixo(q.B, q.EmI);
                    if (continuas.Contains(q) && double.IsNaN(fixo)) continue;
                    double fd = ord.Where(t => continuas.Contains(t) || t.Rank > q.Rank).Select(t => FaceDist(q.D, t)).DefaultIfEmpty(0).Max();
                    double recuo = double.IsNaN(fixo) ? fd + folga(q.B, q.EmI) : fixo;
                    if (q.Rank == 1 && double.IsNaN(fixo)) recuo = Math.Min(recuo, 0.3 * q.L);   // só diagonal: viga curta demais some (cortada até a chapa) em vez de entrar no apoio
                    if (recuo <= 1e-6) continue;
                    var k = new Corte { P = q.No + q.D * recuo, N = q.D, Recuo = recuo, Face = fd, Contra = continuas.Contains(q) ? null : top.B };
                    // viga inclinada (água do telhado) contra pilar: a chapa fica rente à face do pilar (vertical) e a viga é cortada nesse plano;
                    // a folga (espessura da chapa) conta na normal da face, não ao longo da barra.
                    if (top.Rank == 3 && q.Rank == 2 && k.Contra != null)
                    {
                        Vector3D nf = q.D - Vector3D.DotProduct(q.D, top.D) * top.D; double cs = nf.Length;
                        if (cs > 0.5 && cs < 1 - 1e-6)
                        {
                            nf /= cs;
                            k.N = nf; k.Inclinada = true; k.Face = fd * cs;
                            k.P = q.No + q.D * fd + nf * (recuo - fd);
                        }
                    }
                    k.NaMesa = top.F.H * Math.Abs(Vector3D.DotProduct(q.D, top.U)) <= top.F.B * Math.Abs(Vector3D.DotProduct(q.D, top.V));
                    if (v5 && k.Contra != null && Redondo(q.F) && Redondo(top.F) && q.F.H <= top.F.H + 1e-9) { k.P = q.No; k.N = q.D; k.Inclinada = false; Boca(k, q, top); }   // tubo em tubo: boca de peixe
                    else if (v5 && k.Contra != null && !k.Inclinada && (q.Rank == 1 || double.IsNaN(fixo))) k.Extra = Angulo(q, top, folga(q.B, q.EmI));   // barra oblíqua ao apoio (diagonal, água do telhado): nenhum canto entra nele
                    cortes[(q.B, q.EmI)] = k;
                }
            }
            return cortes;
        }

        // Área da seção (m²): soma com sinal dos laços (furos giram ao contrário).
        static double AreaSecao(Contorno f)
        {
            double a = 0;
            foreach (Point[] l in f.Lacos) for (int k = 0; k < l.Length; k++) a += l[k].X * l[(k + 1) % l.Length].Y - l[(k + 1) % l.Length].X * l[k].Y;
            return Math.Abs(a / 2);
        }

        static bool Redondo(Contorno f) => !f.Concreto && f.Liso.Count > 0 && f.Liso[0];

        // Recorte "angulo": plano paralelo à face do apoio (no plano barra–apoio), afastado dela pela folga.
        static Corte Angulo(Ponta q, Ponta top, double folga)
        {
            Vector3D perp = q.D - Vector3D.DotProduct(q.D, top.D) * top.D;
            if (perp.Length < 1e-6) return null;
            perp.Normalize();
            double ext = top.F.B / 2 * Math.Abs(Vector3D.DotProduct(perp, top.U)) + top.F.H / 2 * Math.Abs(Vector3D.DotProduct(perp, top.V));
            return new Corte { P = top.No + perp * (ext + folga), N = perp };
        }

        // Boca de peixe: cilindro do receptor; o recuo informado é o fundo da sela.
        static void Boca(Corte k, Ponta q, Ponta top)
        {
            k.EixoO = top.No; k.EixoA = top.D; k.Raio = top.F.H / 2;
            double s = Vector3D.CrossProduct(q.D, top.D).Length, r = q.F.H / 2;
            if (!k.Motor && s > 1e-6) k.Recuo = Math.Sqrt(Math.Max(0, k.Raio * k.Raio - r * r)) / s;
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
            // ponta: plano, 2º plano (angulo) e cilindro (boca de peixe); vale o corte mais fundo na barra (ini: maior t; fim: menor)
            double Plano(Point3D p, Corte k) { double nd = Vector3D.DotProduct(e, k.N); return Math.Abs(nd) < 1e-6 ? double.NaN : Vector3D.DotProduct(k.P - p, k.N) / nd; }
            double Fundo(double t0, double t1, bool ini) => double.IsNaN(t1) ? t0 : double.IsNaN(t0) ? t1 : ini ? Math.Max(t0, t1) : Math.Min(t0, t1);
            Point3D Fim(Point3D p, Corte k, bool ini)
            {
                if (k == null) return p;
                double t = Plano(p, k);
                if (k.Extra != null) t = Fundo(t, Plano(p, k.Extra), ini);
                if (k.Raio > 0)
                {
                    Vector3D A = k.EixoA, w = p - k.EixoO; A.Normalize();
                    Vector3D wp = w - Vector3D.DotProduct(w, A) * A, ep = e - Vector3D.DotProduct(e, A) * A;
                    double qa = ep.LengthSquared, qb = 2 * Vector3D.DotProduct(wp, ep), qc = wp.LengthSquared - k.Raio * k.Raio, disc = qb * qb - 4 * qa * qc;
                    if (qa > 1e-9 && disc >= 0) t = Fundo(t, ((ini ? 1 : -1) * Math.Sqrt(disc) - qb) / (2 * qa), ini);
                }
                return double.IsNaN(t) ? p : p + e * t;
            }
            if (Vector3D.DotProduct(Fim(c, cc, false) - Fim(a, ca, true), e) <= 1e-6) return;   // recortes engoliram a barra
            for (int li = 0; li < f.Lacos.Count; li++)
            {
                Point[] q = f.Lacos[li]; bool liso = f.Liso[li]; int n = q.Length;
                double area = 0; for (int k = 0; k < n; k++) area += q[k].X * q[(k + 1) % n].Y - q[(k + 1) % n].X * q[k].Y;   // furo (horário): normal radial para dentro
                var pa = new Point3D[n]; var pc = new Point3D[n]; var nr = new Vector3D[n];
                for (int k = 0; k < n; k++)
                {
                    Vector3D off = u * q[k].X + v * q[k].Y;
                    pa[k] = Fim(a + off, ca, true); pc[k] = Fim(c + off, cc, false);
                    nr[k] = area < 0 ? -off : off; if (nr[k].Length > 1e-12) nr[k].Normalize();   // liso: normal radial (para fora do material)
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
                Point3D A(Point p) => Fim(a + u * p.X + v * p.Y, ca, true);
                Point3D Cc(Point p) => Fim(c + u * p.X + v * p.Y, cc, false);
                m.Tri(Cc(f.Tampa[t]), Cc(f.Tampa[t + 1]), Cc(f.Tampa[t + 2]), cor);
                m.Tri(A(f.Tampa[t + 2]), A(f.Tampa[t + 1]), A(f.Tampa[t]), cor);
            }
        }

        // ---------- primitivas das ligações ----------

        // Parafuso sextavado, cada peça um sólido fechado: cabeça + arruela, fuste, arruela + porca e a ponta do fuste
        // passando da porca. cabeca = face de apoio da cabeça; eixo = cabeça -> porca. pega = espessura apertada (a arruela da
        // porca encosta nela); sem pega (NaN), a do comprimento do motor (pega + porca + arruela + fios: ligacoes_rigidas
        // _comprimento_parafuso). As arruelas ficam a Folga da chapa: sem faces no mesmo plano (z-fighting).
        public const double FolgaParafuso = 0.03;   // × d (0,6 mm no M20)
        public static void Parafuso(MalhaDx m, Point3D cabeca, Vector3D eixo, double d, double comp, Color4 cor, double pega = double.NaN)
        {
            if (eixo.Length < 1e-12 || d <= 0 || comp <= 0) return;
            eixo.Normalize();
            double rc = 0.95 * d, hc = 0.65 * d, ra = 1.05 * d, ha = 0.14 * d, rp = 0.95 * d, hp = 0.8 * d, f = FolgaParafuso * d;
            if (double.IsNaN(pega) || pega <= 0) pega = comp - 0.9 * d - 0.014;
            pega = Math.Max(Math.Min(pega, comp - hp - ha), 0.3 * comp);
            Point3D Em(double x) => cabeca + eixo * x;
            m.Tubo(Em(-f - ha - hc), Em(-f - ha), rc, 6, false, cor);                     // cabeça
            m.Tubo(Em(-f - ha), Em(-f), ra, 12, true, cor);                               // arruela
            double ponta = Math.Max(comp, pega + f + ha + hp + 0.25 * d);
            m.Tubo(Em(-f - ha * 0.5), Em(ponta), d / 2, 10, true, cor);                    // fuste, da arruela até passar da porca
            m.Tubo(Em(pega + f), Em(pega + f + ha), ra, 12, true, cor);                   // arruela da porca
            m.Tubo(Em(pega + f + ha), Em(pega + f + ha + hp), rp, 6, false, cor, true, Math.PI / 6);   // porca
        }

        // Cordão de filete: prisma triangular (pernas iguais) ao longo da polilinha. ax = direção da peça que chega (x local do
        // contrato: a solda fica na face da chapa/apoio e sobe pela peça). Polilinha fechada (contorno do perfil): uma perna
        // em ax e a outra para fora do contorno (pela orientação do laço, vale nos cantos côncavos do I). Aberta: dos dois lados.
        // ponytail: sem esquadria nos cantos do laço (as pontas dos prismas se cruzam).
        public static void Cordao(MalhaDx m, IList<Point3D> pts, double perna, Color4 cor, Vector3D ax)
        {
            double p = Math.Max(0.003, perna);
            if (pts.Count < 2 || ax.Length < 1e-12) return;
            ax.Normalize();
            bool laco = pts.Count > 3 && (pts[0] - pts[pts.Count - 1]).Length < 1e-6;
            double sinal = 0;   // laço anti-horário em torno de ax: s × ax aponta para fora
            if (laco) { Vector3D area = new Vector3D(); for (int k = 0; k + 1 < pts.Count; k++) area += Vector3D.CrossProduct((Vector3D)pts[k], (Vector3D)pts[k + 1]); sinal = Vector3D.DotProduct(area, ax) >= 0 ? 1 : -1; }
            for (int k = 0; k + 1 < pts.Count; k++)
            {
                Vector3D sg = pts[k + 1] - pts[k]; if (sg.Length < 1e-9) continue; sg.Normalize();
                Vector3D o = Vector3D.CrossProduct(sg, ax); if (o.Length < 1e-6) continue; o.Normalize();
                foreach (Vector3D w in laco ? new[] { o * sinal } : new[] { o, -o })
                {
                    Point3D a = pts[k], b = pts[k + 1];
                    Vector3D h = ax + w; h.Normalize();   // face exposta (hipotenusa)
                    Point3D a1 = a + ax * p, a2 = a + w * p, b1 = b + ax * p, b2 = b + w * p;
                    if (Vector3D.DotProduct(Vector3D.CrossProduct(a2 - a1, b2 - a1), h) >= 0) m.Quad(a1, a2, b2, b1, h, h, h, h, cor);
                    else m.Quad(a1, b1, b2, a2, h, h, h, h, cor);
                    m.TriN(a, a1, a2, -sg, cor); m.TriN(b, b1, b2, sg, cor);
                    // pernas (encostadas na chapa e na peça): o prisma fecha; com o descarte de faces de trás não brigam com elas
                    m.QuadN(a, b, b1, a1, -w, cor); m.QuadN(a, a2, b2, b, -ax, cor);
                }
            }
        }

        // Contorno com cope: corta o laço único (I, U, L; tubos e maciços não) fora da faixa vMin ≤ v ≤ vMax (Sutherland-Hodgman,
        // basta para um semiplano horizontal), tampa refeita por orelhas. null = nada sobra ou seção sem cope.
        // uMin/uMax: copes laterais (esq/dir do contrato pontas: bordas das mesas da viga mais larga que o vão entre as mesas do apoio).
        public static Contorno Recortar(Contorno f, double vMin, double vMax, double uMin = double.NegativeInfinity, double uMax = double.PositiveInfinity)
        {
            if (f.Concreto || f.Lacos.Count != 1 || vMax - vMin < 1e-4 || uMax - uMin < 1e-4) return null;
            List<Point> Corta(List<Point> q, double lim, bool abaixo, bool emU = false)
            {
                if (double.IsInfinity(lim)) return q;
                var r = new List<Point>();
                double C(Point x) => emU ? x.X : x.Y;
                bool Dentro(Point x) => abaixo ? C(x) <= lim : C(x) >= lim;
                for (int i = 0; i < q.Count; i++)
                {
                    Point a = q[i], b = q[(i + 1) % q.Count];
                    if (Dentro(a)) r.Add(a);
                    if (Dentro(a) != Dentro(b)) { double s = (lim - C(a)) / (C(b) - C(a)); r.Add(new Point(a.X + (b.X - a.X) * s, a.Y + (b.Y - a.Y) * s)); }
                }
                return r;
            }
            var l = Corta(Corta(Corta(Corta(f.Lacos[0].ToList(), vMax, true), vMin, false), uMax, true, true), uMin, false, true);
            if (l.Count < 3) return null;
            var c = new Contorno { H = vMax - vMin, B = Math.Min(f.B, uMax - uMin), Tw = f.Tw, Tf = f.Tf, Forma = f.Forma };
            Point[] arr = l.ToArray(); c.Laco(arr);
            foreach (int[] t in Triangular(arr)) { c.Tampa.Add(arr[t[0]]); c.Tampa.Add(arr[t[1]]); c.Tampa.Add(arr[t[2]]); }
            return c;
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

        // Sólido fechado e para fora: cada aresta orientada tem a sua inversa (sem buraco nem T) e o volume com sinal é > 0
        // (os triângulos giram anti-horário vistos de fora, como o descarte de faces de trás do Render3D espera).
        public static (bool Fechada, double Volume) Fechada(MalhaDx m)
        {
            var arestas = new Dictionary<(long, long, long, long, long, long), int>();
            (long, long, long) K(Vector3 p) => ((long)Math.Round(p.X * 1e6), (long)Math.Round(p.Y * 1e6), (long)Math.Round(p.Z * 1e6));
            double vol = 0;
            for (int t = 0; t + 2 < m.T.Count; t += 3)
            {
                Vector3 a = m.P[m.T[t]], b = m.P[m.T[t + 1]], c = m.P[m.T[t + 2]];
                vol += Vector3.Dot(a, Vector3.Cross(b, c)) / 6.0;
                foreach (var (p, q) in new[] { (a, b), (b, c), (c, a) })
                {
                    var (x, y, z) = K(p); var (x2, y2, z2) = K(q);
                    var chave = (x, y, z, x2, y2, z2); var inv = (x2, y2, z2, x, y, z);
                    if (arestas.TryGetValue(inv, out int n) && n > 0) arestas[inv] = n - 1;
                    else arestas[chave] = (arestas.TryGetValue(chave, out int k) ? k : 0) + 1;
                }
            }
            return (arestas.Values.All(n => n == 0), vol);
        }

        // ---------- interferência (Estructto.exe --testes-interferencia) ----------

        // Pares de sólidos (malhas fechadas) que se interpenetram mais que tol (m): AABB e depois pontos de cada malha
        // (vértices e pontos ao longo das arestas, a cada ≤ passo) dentro da outra (raio par/ímpar) e longe da superfície dela
        // mais que tol — encostar (chapa na mesa, solda na face) não conta. Devolve (i, j, profundidade em m).
        // ponytail: amostra arestas, não faz interseção triângulo-triângulo; uma lâmina fina cruzando sem vértice dentro escapa.
        public static List<(int A, int B, double Prof)> Interferencias(IList<MalhaDx> s, double tol = 0.001, double passo = 0.02)
        {
            var caixa = s.Select(m =>
            {
                if (m.P.Count == 0) return (Min: new Vector3(float.MaxValue), Max: new Vector3(float.MinValue));
                Vector3 mi = m.P[0], ma = m.P[0];
                foreach (Vector3 p in m.P) { mi = Vector3.Min(mi, p); ma = Vector3.Max(ma, p); }
                return (Min: mi, Max: ma);
            }).ToList();
            float t = (float)tol;
            var r = new List<(int, int, double)>();
            for (int i = 0; i < s.Count; i++)
                for (int j = i + 1; j < s.Count; j++)
                {
                    var (a0, a1) = caixa[i]; var (b0, b1) = caixa[j];
                    if (a0.X > b1.X - t || b0.X > a1.X - t || a0.Y > b1.Y - t || b0.Y > a1.Y - t || a0.Z > b1.Z - t || b0.Z > a1.Z - t) continue;
                    double p = Math.Max(Penetracao(s[i], s[j], caixa[j], tol, passo), Penetracao(s[j], s[i], caixa[i], tol, passo));
                    if (p > tol) r.Add((i, j, p));
                }
            return r;
        }

        // Maior distância à superfície de b de um ponto de a que está dentro de b (0 = nenhum).
        static double Penetracao(MalhaDx a, MalhaDx b, (Vector3 Min, Vector3 Max) cb, double tol, double passo)
        {
            float t = (float)tol; double pior = 0;
            bool NaCaixa(Vector3 p) => p.X > cb.Min.X + t && p.X < cb.Max.X - t && p.Y > cb.Min.Y + t && p.Y < cb.Max.Y - t && p.Z > cb.Min.Z + t && p.Z < cb.Max.Z - t;
            void Testa(Vector3 p)
            {
                if (!NaCaixa(p) || !Dentro(b, p)) return;
                double d = Distancia(b, p);
                if (d > pior) pior = d;
            }
            for (int k = 0; k + 2 < a.T.Count; k += 3)
                for (int e = 0; e < 3; e++)
                {
                    Vector3 p = a.P[a.T[k + e]], q = a.P[a.T[k + (e + 1) % 3]];
                    int n = Math.Max(1, (int)Math.Ceiling((q - p).Length() / passo));
                    for (int m = 0; m < n; m++) Testa(Vector3.Lerp(p, q, (float)m / n));
                }
            // face atravessando face (duas chapas em X sem vértice nem aresta dentro da outra): grade no interior dos triângulos
            // que tocam a caixa de b
            for (int k = 0; k + 2 < a.T.Count; k += 3)
            {
                Vector3 p0 = a.P[a.T[k]], p1 = a.P[a.T[k + 1]], p2 = a.P[a.T[k + 2]];
                Vector3 mi = Vector3.Min(p0, Vector3.Min(p1, p2)), ma = Vector3.Max(p0, Vector3.Max(p1, p2));
                if (mi.X > cb.Max.X || ma.X < cb.Min.X || mi.Y > cb.Max.Y || ma.Y < cb.Min.Y || mi.Z > cb.Max.Z || ma.Z < cb.Min.Z) continue;
                int n = Math.Max(2, (int)Math.Ceiling(Math.Max((p1 - p0).Length(), Math.Max((p2 - p1).Length(), (p0 - p2).Length())) / passo));
                for (int i = 1; i < n; i++) for (int j = 1; i + j < n; j++) Testa(p0 + (p1 - p0) * ((float)i / n) + (p2 - p0) * ((float)j / n));
            }
            return pior > tol ? pior : 0;
        }

        static bool Dentro(MalhaDx b, Vector3 o)
        {
            var dir = Vector3.Normalize(new Vector3(0.5773f, 0.6129f, 0.5397f));   // direção "torta": não corre ao longo de arestas
            int n = 0;
            for (int k = 0; k + 2 < b.T.Count; k += 3)
            {
                Vector3 v0 = b.P[b.T[k]], e1 = b.P[b.T[k + 1]] - v0, e2 = b.P[b.T[k + 2]] - v0;
                Vector3 h = Vector3.Cross(dir, e2); float det = Vector3.Dot(e1, h);
                if (Math.Abs(det) < 1e-12f) continue;
                Vector3 s = o - v0; float u = Vector3.Dot(s, h) / det; if (u < 0 || u > 1) continue;
                Vector3 q = Vector3.Cross(s, e1); float v = Vector3.Dot(dir, q) / det; if (v < 0 || u + v > 1) continue;
                if (Vector3.Dot(e2, q) / det > 0) n++;
            }
            return n % 2 == 1;
        }

        static double Distancia(MalhaDx b, Vector3 p)
        {
            double d = double.MaxValue;
            for (int k = 0; k + 2 < b.T.Count; k += 3) d = Math.Min(d, DistTri(p, b.P[b.T[k]], b.P[b.T[k + 1]], b.P[b.T[k + 2]]));
            return d;
        }

        // Distância ponto-triângulo (Ericson, Real-Time Collision Detection 5.1.5).
        static double DistTri(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap); if (d1 <= 0 && d2 <= 0) return (p - a).Length();
            Vector3 bp = p - b; float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp); if (d3 >= 0 && d4 <= d3) return (p - b).Length();
            float vc = d1 * d4 - d3 * d2; if (vc <= 0 && d1 >= 0 && d3 <= 0) return (p - (a + ab * (d1 / (d1 - d3)))).Length();
            Vector3 cp = p - c; float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp); if (d6 >= 0 && d5 <= d6) return (p - c).Length();
            float vb = d5 * d2 - d1 * d6; if (vb <= 0 && d2 >= 0 && d6 <= 0) return (p - (a + ac * (d2 / (d2 - d6)))).Length();
            float va = d3 * d6 - d5 * d4; if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0) return (p - (b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6))))).Length();
            float den = 1 / (va + vb + vc); return (p - (a + ab * (vb * den) + ac * (vc * den))).Length();
        }

        // Cópia literal do Juntas da v0.4 (commit 635d160), só para o teste de Review Focus 3: modelo sem pontas do motor
        // tem de sair com os mesmos planos (TestesExportar).
        internal static Dictionary<(Barra3D, bool), Corte> JuntasV04(Modelo3D mod, Func<Barra3D, (Point3D A, Point3D C)> pontos, Func<Barra3D, Contorno> forma,
                                                                  Func<Barra3D, bool, double> folga, Func<Barra3D, bool, double> recuoFixo)
        {
            var porNo = new Dictionary<int, List<Ponta>>();
            foreach (Barra3D b in mod.Barras)
            {
                var (a, c) = pontos(b); Vector3D e = c - a; double len = e.Length; if (len < 1e-9) continue; e /= len;
                Vector3D v = Modelo3D.EixoY(e, b.Rot + b.RotPerfil), u = Vector3D.CrossProduct(v, e);
                Contorno f = forma(b);
                Point dc = GeometriaPerfis.Cardinal(f, b.Cardinal); a += u * dc.X + v * dc.Y; c += u * dc.X + v * dc.Y;
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
                    double fd = ord.Where(t => continuas.Contains(t) || t.Rank > q.Rank).Select(t => FaceDist(q.D, t)).DefaultIfEmpty(0).Max();
                    double recuo = double.IsNaN(fixo) ? fd + folga(q.B, q.EmI) : fixo;
                    recuo = Math.Min(recuo, 0.3 * q.L);
                    if (recuo <= 1e-6) continue;
                    var k = new Corte { P = q.No + q.D * recuo, N = q.D, Recuo = recuo, Face = fd, Contra = continuas.Contains(q) ? null : top.B };
                    // viga inclinada (água do telhado) contra pilar: a chapa fica rente à face do pilar (vertical) e a viga é cortada nesse plano;
                    // a folga (espessura da chapa) conta na normal da face, não ao longo da barra.
                    if (top.Rank == 3 && q.Rank == 2 && k.Contra != null)
                    {
                        Vector3D nf = q.D - Vector3D.DotProduct(q.D, top.D) * top.D; double cs = nf.Length;
                        if (cs > 0.5 && cs < 1 - 1e-6)
                        {
                            nf /= cs;
                            k.N = nf; k.Inclinada = true; k.Face = fd * cs;
                            k.P = q.No + q.D * fd + nf * (recuo - fd);
                        }
                    }
                    k.NaMesa = top.F.H * Math.Abs(Vector3D.DotProduct(q.D, top.U)) <= top.F.B * Math.Abs(Vector3D.DotProduct(q.D, top.V));
                    cortes[(q.B, q.EmI)] = k;
                }
            }
            return cortes;
        }

        // Autoverificação (Estructto.exe --testes-exportar também chama): triangulação, recortes e sólidos fechados.
        public static void Testar(Action<bool, string> afirmar)
        {
            {   // parafuso, chapa, caixa, perfil I extrudado e cordão de solda: fechados, normais para fora
                var cb = new Color4(1, 1, 1, 1);
                var mp = new MalhaDx(); Parafuso(mp, new Point3D(0.1, 0.2, 0.3), new Vector3D(0.3, -1, 0.2), 0.02, 0.06, cb, 0.035);
                var (fp, vp) = Fechada(mp);
                afirmar(fp && vp > 0, "parafuso: cabeça, arruelas, fuste e porca fechados e para fora (volume " + (vp * 1e6).ToString("0.0") + " cm³)");
                var mc = new MalhaDx(); mc.Chapa(new Point3D(1, 0, 0), new Vector3D(0, 1, 0), new Vector3D(0, 0, 1), new Vector3D(-1, 0, 0), new[] { new Point(0, 0), new Point(0.3, 0), new Point(0.3, 0.2), new Point(0.1, 0.25), new Point(0, 0.2) }, 0.016, cb, false);
                var (fc, vc) = Fechada(mc);
                afirmar(fc && Math.Abs(vc / (0.016 * (0.3 * 0.2 + 0.3 * 0.05 / 2)) - 1) < 1e-4, "chapa (referencial canhoto): fechada, volume = área × espessura");
                var mx = new MalhaDx(); mx.Caixa(new Point3D(1, 2, 3), 0.4, 0.5, 0.6, cb);
                var (fx, vx) = Fechada(mx);
                afirmar(fx && Math.Abs(vx - 0.12) < 1e-6, "caixa (pedestal): fechada, volume 0,12 m³");
                Contorno fi = GeometriaPerfis.CriarContorno(new Perfil3D { Forma = "I", H = 0.3, B = 0.15, Tw = 0.006, Tf = 0.01 }, 0.1);
                var me = new MalhaDx(); Extrudar(me, fi, new Point3D(0, 0, 0), new Point3D(2, 0, 0), new Vector3D(0, 1, 0), new Vector3D(0, 0, 1), new Corte { P = new Point3D(0.1, 0, 0), N = new Vector3D(1, 0, 0.3) }, null, cb, false);   // com corte oblíquo
                var (fe, _) = Fechada(me);
                var mt = new MalhaDx(); Contorno ft = GeometriaPerfis.CriarContorno(new Perfil3D { Forma = "TC", H = 0.2, Tw = 0.008 }, 0.1);
                Extrudar(mt, ft, new Point3D(0, 0, 0), new Point3D(0, 0, 3), new Vector3D(1, 0, 0), new Vector3D(0, 1, 0), null, null, cb, false);
                var (ftb, vtb) = Fechada(mt);
                afirmar(fe && ftb && vtb > 0, "perfil I e tubo circular extrudados: fechados (tampas sem T), para fora");
                var ms = new MalhaDx(); Cordao(ms, new[] { new Point3D(0, 0, 0), new Point3D(0, 0.2, 0) }, 0.008, cb, new Vector3D(1, 0, 0));
                var (fs, vs) = Fechada(ms);
                afirmar(fs && Math.Abs(vs / (2 * 0.2 * 0.008 * 0.008 / 2) - 1) < 1e-3, "cordão de solda (dois lados): prismas fechados, volume 2 × L × p²/2");
            }
            {   // cope de 40 mm no topo de um I 300×150 (mesa 10): some a mesa de cima, a tampa fecha com a área do resto
                Contorno fi = GeometriaPerfis.CriarContorno(new Perfil3D { Forma = "I", H = 0.3, B = 0.15, Tw = 0.006, Tf = 0.01 }, 0.1);
                Contorno fc = Recortar(fi, -0.15, 0.15 - 0.04);
                double At = 0; for (int t = 0; fc != null && t + 2 < fc.Tampa.Count; t += 3) { Point p = fc.Tampa[t], q = fc.Tampa[t + 1], r = fc.Tampa[t + 2]; At += Math.Abs((q.X - p.X) * (r.Y - p.Y) - (r.X - p.X) * (q.Y - p.Y)) / 2; }
                double Aesp = 0.15 * 0.01 + (0.3 - 0.04 - 0.01) * 0.006;   // mesa de baixo + alma até o corte
                afirmar(fc != null && fc.Lacos[0].Max(p => p.Y) <= 0.11 + 1e-9 && Math.Abs(At - Aesp) < 1e-7, "cope: contorno cortado em v = H/2 − 40 mm, área " + (At * 1e4).ToString("0.00") + " cm²");
            }
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

            foreach (double rotPilar in new[] { 0.0, 90 })   // viga inclinada (≈ 10 %) contra pilar de eixo forte e de eixo fraco: chapa rente à face, viga termina nela
            {
                var md = new Modelo3D();
                md.Nos.Add(new No3D { Id = 1 }); md.Nos.Add(new No3D { Id = 2, Z = 6 }); md.Nos.Add(new No3D { Id = 3, X = 10, Z = 7 });
                var pl = new Barra3D { Id = 1, Ni = 1, Nj = 2, Rot = rotPilar }; var vg = new Barra3D { Id = 2, Ni = 2, Nj = 3 };
                md.Barras.AddRange(new[] { pl, vg });
                var nd = md.Nos.ToDictionary(n => n.Id);
                Func<Barra3D, (Point3D, Point3D)> pts = b => (new Point3D(nd[b.Ni].X, 0, nd[b.Ni].Z), new Point3D(nd[b.Nj].X, 0, nd[b.Nj].Z));
                Func<Barra3D, Contorno> frm = b => b == pl ? fPilar : fViga;
                Func<Barra3D, bool, double> fg = (b, emI) => 0.012;
                var cs = Juntas(md, pts, frm, fg, (b, emI) => double.NaN);
                string rt = rotPilar == 0 ? "eixo forte" : "eixo fraco";
                Corte kj = cs[(vg, true)];
                bool forte = rotPilar == 0;
                double meia = forte ? 0.15 : 0.10;   // meia profundidade do pilar na direção da viga (H/2 ou B/2)
                afirmar(kj.Inclinada && Math.Abs(kj.N.Z) < 1e-9 && kj.NaMesa == forte && Math.Abs(kj.Face - meia) < 1e-9, "viga inclinada / pilar " + rt + ": corte vertical (paralelo à face), Face = " + meia);
                var lg = LigacoesAmostra.Gerar(md, cs, frm, fg).FirstOrDefault(q => q.Barra == vg.Id);
                afirmar(lg != null, "viga inclinada / pilar " + rt + ": ligação gerada");
                if (lg != null)
                {
                    // segunda passada como no Render3D: recortes da ligação mandam na ponta da viga
                    var cs2 = Juntas(md, pts, frm, (b, emI) => { double r = lg.RecuoDe("viga"); return double.IsNaN(r) ? fg(b, emI) : r; }, (b, emI) => { double x = lg.PontaDe("viga"); return double.IsNaN(x) ? lg.RecuoDe("pilar") : x; });
                    Corte k2 = cs2[(vg, true)];
                    var (pa, pc) = pts(vg); Vector3D ev = pc - pa; ev.Normalize();
                    var (o, lx, ly, lz) = LigacoesAmostra.Referencial(pa, ev, Modelo3D.EixoY(ev, 0), k2);
                    Point3D W(Vector3D p) => o + (lx * p.X + ly * p.Y + lz * p.Z) / 1000;
                    var ch = lg.Chapas[0];
                    Vector3D wv = Vector3D.CrossProduct(ch.Normal, ch.U);
                    var cantos = ch.Contorno.Select(p => W(ch.Origem + ch.U * p.X + wv * p.Y)).ToList();
                    double esp = ch.Espessura / 1000;
                    if (forte)   // chapa de topo na mesa: toda a chapa em x = H/2; viga termina na face externa da chapa, parafusos saem dela
                    {
                        double gap = cantos.Max(p => Math.Abs(Math.Abs(p.X) - meia)) * 1000;
                        afirmar(gap < 1, "viga inclinada / pilar " + rt + ": chapa rente à mesa do pilar, folga " + gap.ToString("0.00") + " mm (< 1 mm)");
                        afirmar(Math.Abs(k2.P.X - (meia + esp)) < 1e-3 && lg.Parafusos.All(pf => Math.Abs(W(pf.Origem).X - (meia + esp)) < 1e-3), "viga inclinada / pilar " + rt + ": viga termina na chapa e parafusos saem da face externa dela");
                    }
                    else         // chapa simples na alma: começa na face da alma (x = tw/2); viga para 12 mm depois
                    {
                        double gap = Math.Abs(cantos.Min(p => Math.Abs(p.X)) - fPilar.Tw / 2) * 1000;
                        afirmar(gap < 1, "viga inclinada / pilar " + rt + ": chapa começa na face da alma, folga " + gap.ToString("0.00") + " mm (< 1 mm)");
                        afirmar(Math.Abs(Math.Abs(k2.P.X) - (fPilar.Tw / 2 + 0.012)) < 1e-3, "viga inclinada / pilar " + rt + ": viga para 12 mm da alma");
                    }
                }
            }

            {   // interferência: caixas sobrepostas 5 cm = 1 par; encostadas = 0
                var cb = new Color4(1, 1, 1, 1);
                MalhaDx Cx(double x) { var mx = new MalhaDx(); mx.Caixa(new Point3D(x, 0, 0), 0.2, 0.2, 0.2, cb); return mx; }
                var ip = Interferencias(new[] { Cx(0), Cx(0.15) }); var it = Interferencias(new[] { Cx(0), Cx(0.2) });
                // duas chapas em cruz (16 mm × 300 × 300): os vértices de uma ficam fora da outra; o teste de face acha
                var ma = new MalhaDx(); ma.Caixa(new Point3D(0, 0, 0), 0.3, 0.3, 0.016, cb); var mb = new MalhaDx(); mb.Caixa(new Point3D(0.07, 0.03, 0), 0.06, 0.016, 0.3, cb);
                var ix = Interferencias(new[] { ma, mb });
                afirmar(ix.Count == 1 && ix[0].Prof > 0.005, "interferência: chapas em cruz (face atravessando face) = 1 par (prof. " + (ix.Count > 0 ? (ix[0].Prof * 1000).ToString("0.0") : "—") + " mm)");
                afirmar(ip.Count == 1 && ip[0].Prof > 0.02 && ip[0].Prof <= 0.05 + 1e-6 && it.Count == 0, "interferência: caixas sobrepostas 5 cm = 1 par (prof. " + (ip.Count > 0 ? (ip[0].Prof * 1000).ToString("0") : "—") + " mm); encostadas = 0");
                // pórtico acima com os cortes das juntas: pilar, viga e diagonal (recorte angulo) não se interpenetram
                MalhaDx Solido(Barra3D b, Dictionary<(Barra3D, bool), Corte> cs, Contorno f)
                {
                    var ms = new MalhaDx(); var a0 = new Point3D(nos[b.Ni].X, 0, nos[b.Ni].Z); var c0 = new Point3D(nos[b.Nj].X, 0, nos[b.Nj].Z);
                    Vector3D eb = c0 - a0; eb.Normalize(); Vector3D vb = Modelo3D.EixoY(eb, 0), ub = Vector3D.CrossProduct(vb, eb);
                    cs.TryGetValue((b, true), out Corte k1); cs.TryGetValue((b, false), out Corte k2);
                    Extrudar(ms, f, a0, c0, ub, vb, k1, k2, cb, false); return ms;
                }
                var c5 = Juntas(mod, b => (new Point3D(nos[b.Ni].X, 0, nos[b.Ni].Z), new Point3D(nos[b.Nj].X, 0, nos[b.Nj].Z)), b => b == pilar ? fPilar : fViga, (b, emI) => 0.01, (b, emI) => double.NaN, null, true);
                var pecas = new[] { Solido(pilar, c5, fPilar), Solido(viga, c5, fViga), Solido(diag, c5, fViga) };
                var ij = Interferencias(pecas);
                afirmar(ij.Count == 0 && c5[(diag, true)].Extra != null && cd.Extra == null, "diagonal com recorte angulo: pilar, viga e diagonal sem interferência (" + ij.Count + " pares)");
                // pontas do motor: plano no recuo dado (sem o limite 0,3·L) e cope lateral (esq) no contorno
                var cm = Juntas(mod, b => (new Point3D(nos[b.Ni].X, 0, nos[b.Ni].Z), new Point3D(nos[b.Nj].X, 0, nos[b.Nj].Z)), b => b == pilar ? fPilar : fViga,
                                (b, emI) => 0.01, (b, emI) => double.NaN, (b, emI) => b == viga && emI ? new Corte { P = new Point3D(1.5, 0, 3), N = new Vector3D(-1, 0, 0) } : null);
                afirmar(cm[(viga, true)].Motor && Math.Abs(cm[(viga, true)].Recuo - 1.5) < 1e-9 && Math.Abs(cortes[(viga, true)].Recuo - 0.16) < 1e-9, "pontas do motor: recuo 1,5 m sem o limite 0,3·L; sem elas, a regra das juntas (0,16 m)");
                Contorno fl = Recortar(fViga, -fViga.H / 2, fViga.H / 2, double.NegativeInfinity, fViga.B / 2 - 0.02);
                afirmar(fl != null && Math.Abs(fl.Lacos[0].Max(p => p.X) - (fViga.B / 2 - 0.02)) < 1e-9 && fl.Lacos[0].Min(p => p.X) < -fViga.B / 2 + 1e-9, "cope lateral: borda das mesas cortada em u = B/2 − 20 mm, o outro lado inteiro");
                // tubo em tubo (quadro tubular): boca de peixe, ramal Ø 114 em banzo Ø 168 a 60°, sem interferência e encostado no banzo
                var mt = new Modelo3D(); mt.Nos.Add(new No3D { Id = 1 }); mt.Nos.Add(new No3D { Id = 2, X = 4 }); mt.Nos.Add(new No3D { Id = 3, X = 2 }); mt.Nos.Add(new No3D { Id = 4, X = 3, Z = 1.732 });
                var b1 = new Barra3D { Id = 1, Ni = 1, Nj = 3 }; var b2 = new Barra3D { Id = 2, Ni = 3, Nj = 2 }; var br = new Barra3D { Id = 3, Ni = 3, Nj = 4 };
                mt.Barras.AddRange(new[] { b1, b2, br });
                var nt = mt.Nos.ToDictionary(n => n.Id);
                Contorno fB = GeometriaPerfis.CriarContorno(new Perfil3D { Forma = "TC", H = 0.168, Tw = 0.008 }, 0.1), fR = GeometriaPerfis.CriarContorno(new Perfil3D { Forma = "TC", H = 0.114, Tw = 0.006 }, 0.1);
                Func<Barra3D, (Point3D, Point3D)> pt = b => (new Point3D(nt[b.Ni].X, 0, nt[b.Ni].Z), new Point3D(nt[b.Nj].X, 0, nt[b.Nj].Z));
                var ct = Juntas(mt, pt, b => b == br ? fR : fB, (b, emI) => 0, (b, emI) => double.NaN, null, true);
                MalhaDx Sol(Barra3D b) { var ms = new MalhaDx(); var (a0, c0) = pt(b); Vector3D eb = c0 - a0; eb.Normalize(); Vector3D vb = Modelo3D.EixoY(eb, 0), ub = Vector3D.CrossProduct(vb, eb);
                                         ct.TryGetValue((b, true), out Corte k1); ct.TryGetValue((b, false), out Corte k2); Extrudar(ms, b == br ? fR : fB, a0, c0, ub, vb, k1, k2, cb, false); return ms; }
                MalhaDx sr = Sol(br);
                double rmin = sr.P.Min(p => Math.Sqrt(p.Y * p.Y + p.Z * p.Z));   // distância ao eixo do banzo (eixo X)
                afirmar(ct.TryGetValue((br, true), out Corte kb) && kb.Raio > 0 && Interferencias(new[] { Sol(b1), Sol(b2), sr }).Count == 0 && Math.Abs(rmin - 0.084) < 1e-3,
                        "boca de peixe: ramal tubular sem interferência com o banzo e encostado nele (menor distância ao eixo " + (rmin * 1000).ToString("0.0") + " mm, R = 84)");
            }

            var m = new MalhaDx();
            Vector3D e = new Vector3D(1, 0, 0), v = new Vector3D(0, 0, 1), u = Vector3D.CrossProduct(v, e);
            Extrudar(m, fViga, new Point3D(0, 0, 0), new Point3D(4, 0, 0), u, v, new Corte { P = new Point3D(0.5, 0, 0), N = e, Recuo = 0.5 }, null, new Color4(1, 1, 1, 1), true);
            afirmar(m.P.Count > 0 && m.P.Min(p => p.X) >= 0.5f - 1e-5f && m.P.Max(p => p.X) <= 4 + 1e-5f, "extrusão começa no plano de corte (x = 0,5)");
        }
    }
}
