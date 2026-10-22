// Motor de atração (snap) da Vista3D, igual ao do AutoCAD: OSNAP, rastreamento (OTRACK), polar e ORTO.
// Spec: docs/v3/PESQUISA-PLANT.md §3; comportamento e regras: docs/v3/SNAP-MEDIR.md.
// Puro: recebe a geometria, a projeção mundo -> tela, o pixel do cursor, o plano de trabalho e o relógio (ms) e
// devolve o melhor ponto e o tipo. Não desenha nem lê teclado/mouse (quem desenha: SnapOverlay.cs; quem liga: Vista3D.Snap.cs).
// Testes: Estructto.exe --testes-snap <saida.txt> (TestesSnap, abaixo).
// ponytail: a projeção tem de ser afim (câmera ortográfica da Vista3D): o parâmetro ao longo de uma reta na tela vale no 3D.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media.Media3D;
using Portico;

namespace NovoFtool
{
    // Bits do OSMODE do AutoCAD nos modos de objeto; Grade e Plano são do Estructto.
    [Flags]
    public enum ModoSnap
    {
        Nenhum = 0, Extremidade = 1, Medio = 2, Centro = 4, No = 8, Intersecao = 32, Perpendicular = 128, Proximo = 512,
        Aparente = 2048, Extensao = 4096, Paralelo = 8192, Desligar = 16384, Grade = 1 << 16,
        Objetos = Extremidade | Medio | Centro | No | Intersecao | Perpendicular | Proximo | Aparente | Extensao | Paralelo,
    }

    public enum TipoSnap { Nenhum, Extremidade, Medio, Centro, No, Intersecao, Aparente, Perpendicular, Proximo, Extensao, Paralelo, Rastreio, Polar, Ortogonal, Grade, Plano }

    public sealed class SegSnap { public Point3D A, B; public object Dono; public bool Aresta; }          // Aresta: contorno do sólido (Realista)
    public sealed class CirculoSnap { public Point3D C; public Vector3D N; public double R; public object Dono; }
    public sealed class GeoSnap
    {
        public readonly List<Point3D> Nos = new List<Point3D>();
        public readonly List<SegSnap> Segs = new List<SegSnap>();
        public readonly List<CirculoSnap> Circulos = new List<CirculoSnap>();
    }

    // Linha pontilhada a desenhar: de O na direção D (Raio = só para a frente; senão a reta inteira).
    public struct GuiaSnap { public Point3D O; public Vector3D D; public bool Raio; }

    public sealed class ResultadoSnap
    {
        public Point3D? P; public TipoSnap Tipo; public object Dono;
        public string Dica;                                          // tooltip: "Extremidade", "Polar: 4,250 < 30°"
        public readonly List<GuiaSnap> Guias = new List<GuiaSnap>();
        public SegSnap Seg;                                          // objeto do snap (paralelo, perpendicular...)
        public string Nome => Snap3D.Nome(Tipo);
    }

    public sealed class Snap3D
    {
        // ---------- estado (barra de status) ----------
        public ModoSnap Modos = ModoSnap.Extremidade | ModoSnap.Medio | ModoSnap.Intersecao | ModoSnap.Perpendicular | ModoSnap.Extensao | ModoSnap.No
                              | ModoSnap.Centro | ModoSnap.Aparente | ModoSnap.Paralelo | ModoSnap.Grade;   // running (PESQUISA-PLANT §3.1 + CEN/APP/PAR)
        public ModoSnap? Unico;                 // one-off (Shift+botão direito ou END/MID...): vale um clique
        public bool Osnap = true;               // F3
        bool orto, polar = true;
        public bool Orto { get => orto; set { orto = value; if (value) polar = false; } }      // F8 (ORTO e Polar se excluem)
        public bool Polar { get => polar; set { polar = value; if (value) orto = false; } }    // F10
        public bool Rastreio = true;            // F11
        public bool RastreioPolar;              // false = "Track orthogonally only" (padrão do AutoCAD)
        public double PolarInc = 15;            // graus: 90, 45, 30, 22.5, 18, 15, 10, 5
        public double Abertura = 10;            // px (APERTURE)
        public double PassoGrade = 1;           // m
        public double TolInt = 1e-4;            // m: distância 3D abaixo da qual duas barras se cruzam de fato
        public const double TempoAquisicao = 500, Empate = 2;   // ms parado para adquirir; px de empate entre modos
        public const int MaxAdquiridos = 7;

        public readonly List<(Point3D P, TipoSnap Tipo)> Adquiridos = new List<(Point3D, TipoSnap)>();
        public readonly List<SegSnap> Paralelas = new List<SegSnap>();
        string pausaChave; double pausaIni; bool pausaFeita;

        public void LimparRastreio() { Adquiridos.Clear(); Paralelas.Clear(); pausaChave = null; }
        public void ConsumirUnico() { Unico = null; }

        // prioridade no empate (PESQUISA-PLANT §3.1 [P]): END > INT > MID > CEN > NOD > PER > EXT > NEA (APP logo após INT, PAR após EXT)
        static readonly TipoSnap[] Prioridade = { TipoSnap.Extremidade, TipoSnap.Intersecao, TipoSnap.Aparente, TipoSnap.Medio, TipoSnap.Centro, TipoSnap.No,
                                                  TipoSnap.Perpendicular, TipoSnap.Extensao, TipoSnap.Paralelo, TipoSnap.Proximo };

        public static string Nome(TipoSnap t)
        {
            switch (t)
            {
                case TipoSnap.Extremidade: return "Extremidade";
                case TipoSnap.Medio: return "Ponto médio";
                case TipoSnap.Centro: return "Centro";
                case TipoSnap.No: return "Nó";
                case TipoSnap.Intersecao: return "Interseção";
                case TipoSnap.Aparente: return "Interseção aparente";
                case TipoSnap.Perpendicular: return "Perpendicular";
                case TipoSnap.Proximo: return "Mais próximo";
                case TipoSnap.Extensao: return "Extensão";
                case TipoSnap.Paralelo: return "Paralelo";
                case TipoSnap.Rastreio: return "Rastreio";
                case TipoSnap.Polar: return "Polar";
                case TipoSnap.Ortogonal: return "Ortogonal";
                case TipoSnap.Grade: return "Grade";
                case TipoSnap.Plano: return "Plano";
            }
            return "";
        }

        // Abreviações one-off do AutoCAD (digitadas na linha de comando durante o pedido de ponto).
        public static ModoSnap? Palavra(string s)
        {
            switch ((s ?? "").Trim().ToUpperInvariant())
            {
                case "END": case "EXT.": return ModoSnap.Extremidade;
                case "MID": return ModoSnap.Medio;
                case "CEN": return ModoSnap.Centro;
                case "NOD": return ModoSnap.No;
                case "INT": return ModoSnap.Intersecao;
                case "APP": case "APINT": return ModoSnap.Aparente;
                case "PER": return ModoSnap.Perpendicular;
                case "NEA": return ModoSnap.Proximo;
                case "EXT": return ModoSnap.Extensao;
                case "PAR": return ModoSnap.Paralelo;
                case "NON": case "NONE": return ModoSnap.Desligar;
            }
            return null;
        }

        // ---------- resolução ----------

        struct Cand { public Point3D P; public double D; public TipoSnap T; public object Dono; public SegSnap Seg; public GuiaSnap? G; }
        struct Caminho { public Point3D O; public Vector3D D; public bool Raio, DaBase, Forcado; public TipoSnap Origem; public string Ang; public double T, Dist; }

        // c = pixel do cursor; base0 = 1º ponto do comando (null = nenhum); tMs = relógio; inverterOrto = Shift segurado;
        // olho = direção do alvo para o olho (interseção aparente: vale o ponto mais perto de quem olha).
        public ResultadoSnap Resolver(GeoSnap g, Func<Point3D, Point> tela, Func<Point, Point3D?> noPlano, PlanoTrabalho pl, Point c,
                                      Point3D? base0, double tMs, bool inverterOrto = false, Vector3D? olho = null)
        {
            var r = new ResultadoSnap();
            bool objetos = Unico != null ? Unico != ModoSnap.Desligar : Osnap;
            ModoSnap m = Unico != null && Unico != ModoSnap.Desligar ? Unico.Value : Modos;
            double ap = Abertura;

            // 1) modos de objeto. Extensão e paralelo são caminhos: antes de valerem, cruzam com o rastreio/polar (2a).
            Cand? adiado = null;
            if (objetos && g != null)
            {
                Cand? melhor = Objetos(g, tela, c, base0, m, olho, pl);
                Pausa(melhor, g, tela, c, base0, tMs, m);
                if (melhor != null)
                {
                    Cand k = melhor.Value;
                    r.P = k.P; r.Tipo = k.T; r.Dono = k.Dono; r.Seg = k.Seg; r.Dica = Nome(k.T);
                    if (k.G != null) r.Guias.Add(k.G.Value);
                    if (k.T != TipoSnap.Extensao && k.T != TipoSnap.Paralelo) return r;
                    r.Dica += ": " + Num.Fmt((k.P - k.G.Value.O).Length, 3);
                    adiado = k;
                }
            }
            else pausaChave = null;

            // 2) caminhos: ORTO forçado, polar a partir do 1º ponto, rastreio a partir dos pontos adquiridos
            var cams = new List<Caminho>();
            bool ortoAgora = base0 != null && (orto ^ inverterOrto);
            Vector3D U = pl.U, W = pl.W, N = pl.N;
            string eixoN = pl.Normal.ToString();
            if (ortoAgora)
            {
                Caminho? f = null; double dm = double.MaxValue;
                foreach (var (d, nome) in new[] { (U, "0°"), (W, "90°"), (N, "+" + eixoN) })
                {
                    Caminho? k = NaReta(new Caminho { O = base0.Value, D = d, Ang = nome, DaBase = true, Forcado = true, Origem = TipoSnap.Ortogonal }, tela, c);
                    if (k != null && k.Value.Dist < dm) { dm = k.Value.Dist; f = k; }
                }
                if (f != null)
                {
                    var k = f.Value;
                    Point3D? qp = k.D != N ? noPlano(c) : null;   // eixo no plano: a componente do cursor no plano (como o AutoCAD), não a da tela
                    if (qp != null) { Vector3D dq = qp.Value - k.O; dq -= N * Vector3D.DotProduct(dq, N); k.T = Vector3D.DotProduct(dq, k.D); }
                    if (k.T < 0) { k.Ang = Oposto(k.Ang); }
                    cams.Add(k);
                }
            }
            else if (polar && base0 != null)
                foreach (var (d, nome) in Direcoes(U, W, N, eixoN, PolarInc))
                {
                    Caminho? k = NaReta(new Caminho { O = base0.Value, D = d, Raio = true, Ang = nome, DaBase = true, Origem = TipoSnap.Polar }, tela, c);
                    if (k != null && k.Value.Dist < ap) cams.Add(k.Value);
                }
            if (Rastreio && objetos)
            {
                PlanoTrabalho pr = base0 != null ? new PlanoTrabalho(pl.Normal, Vector3D.DotProduct((Vector3D)base0.Value, N)) : pl;
                foreach (var (pa, tipo) in Adquiridos)
                {
                    Point3D o = pr.Projetar(pa);
                    var dirs = RastreioPolar ? Direcoes(U, W, N, eixoN, PolarInc).Select(x => (x.D, x.Nome, true)) : new[] { (U, "0°", false), (W, "90°", false) };
                    foreach (var (d, nome, raio) in dirs.Concat(new[] { (N, "+" + eixoN, false) }))
                    {
                        Point3D oo = d == N ? pa : o;   // vertical: pela coordenada real do ponto
                        Caminho? k = NaReta(new Caminho { O = oo, D = d, Raio = raio, Ang = nome, Origem = tipo }, tela, c);
                        if (k != null && k.Value.Dist < ap) { var kk = k.Value; if (!raio && kk.T < 0) kk.Ang = Oposto(kk.Ang); cams.Add(kk); }
                    }
                }
            }

            if (adiado != null)
            {
                GuiaSnap ga = adiado.Value.G.Value;
                cams.Insert(0, new Caminho { O = ga.O, D = ga.D, Raio = true, Ang = "", Origem = adiado.Value.T });
            }

            // 2a) cruzamento de dois caminhos (origens diferentes)
            Point3D? xMelhor = null; double xd = ap; (Caminho A, Caminho B) xPar = default;
            for (int i = 0; i < cams.Count; i++)
                for (int j = i + 1; j < cams.Count; j++)
                {
                    Caminho a = cams[i], b = cams[j];
                    if ((a.O - b.O).Length < 1e-9) continue;
                    if (!Retas(a.O, a.D, b.O, b.D, out double ta, out double tb, out double gap) || gap > 1e-6) continue;
                    if ((a.Raio && ta <= 1e-9) || (b.Raio && tb <= 1e-9)) continue;
                    Point3D x = a.O + a.D * ta; double dd = (tela(x) - c).Length;
                    if (dd < xd) { xd = dd; xMelhor = x; xPar = (a, b); }
                }
            if (xMelhor != null)
            {
                r = new ResultadoSnap { P = xMelhor, Tipo = TipoSnap.Rastreio };
                r.Dica = Dica(xPar.A, xMelhor.Value) + ", " + Dica(xPar.B, xMelhor.Value);
                r.Guias.Add(Guia(xPar.A)); r.Guias.Add(Guia(xPar.B));
                return r;
            }

            if (adiado != null) return r;   // extensão/paralelo sem cruzamento: valem como snap de objeto

            // 2b) um caminho: o forçado (ORTO), senão o mais perto do cursor
            if (cams.Count > 0)
            {
                Caminho k = cams.FirstOrDefault(x => x.Forcado);
                if (!k.Forcado) k = cams.OrderBy(x => x.Dist).First();
                double t = k.T;
                if (m.HasFlag(ModoSnap.Grade) && PassoGrade > 0)   // passo da grade ao longo do caminho (como o antigo orto+grade)
                {
                    double tg = Math.Round(t / PassoGrade) * PassoGrade;
                    if ((tela(k.O + k.D * tg) - c).Length < ap && (!k.Raio || tg > 0)) t = tg;
                }
                Point3D p = k.O + k.D * t;
                r.P = p; r.Tipo = k.Forcado ? TipoSnap.Ortogonal : k.DaBase ? TipoSnap.Polar : TipoSnap.Rastreio;
                r.Dica = Dica(k, p); r.Guias.Add(Guia(k));
                return r;
            }

            // 3) grade do plano, 4) plano
            Point3D? q = noPlano(c);
            if (q == null) { r.Tipo = TipoSnap.Nenhum; return r; }
            if (m.HasFlag(ModoSnap.Grade) && PassoGrade > 0)
            {
                double Gr(double x) => Math.Round(x / PassoGrade) * PassoGrade;
                Point3D gp = pl.Ponto(Gr(Vector3D.DotProduct((Vector3D)q.Value, U)), Gr(Vector3D.DotProduct((Vector3D)q.Value, W)));
                if ((tela(gp) - c).Length < ap) { r.P = gp; r.Tipo = TipoSnap.Grade; r.Dica = "Grade"; return r; }
            }
            r.P = q; r.Tipo = TipoSnap.Plano; return r;
        }

        static GuiaSnap Guia(Caminho k) => new GuiaSnap { O = k.O, D = k.D, Raio = k.Raio || k.DaBase };
        static string Dica(Caminho k, Point3D p) => (k.DaBase ? (k.Forcado ? "Ortogonal" : "Polar") : Nome(k.Origem)) + ": " + Num.Fmt((p - k.O).Length, 3) + (string.IsNullOrEmpty(k.Ang) ? "" : " < " + k.Ang);
        static string Oposto(string ang)
        {
            if (ang.StartsWith("+")) return "−" + ang.Substring(1);
            if (ang.EndsWith("°") && double.TryParse(ang.TrimEnd('°').Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double a)) return Ang((a + 180) % 360);
            return ang;
        }
        static string Ang(double a) { a = Math.Round(a, 1); return (Math.Abs(a - Math.Round(a)) < 1e-9 ? Math.Round(a).ToString(CultureInfo.InvariantCulture) : a.ToString("0.0", CultureInfo.GetCultureInfo("pt-BR"))) + "°"; }

        // direções polares no plano (de U para W) + a normal do plano (vertical, nos dois sentidos)
        static IEnumerable<(Vector3D D, string Nome)> Direcoes(Vector3D U, Vector3D W, Vector3D N, string eixoN, double inc)
        {
            if (!(inc > 0)) inc = 90;
            for (double a = 0; a < 360 - 1e-9; a += inc)
            {
                double r = a * Math.PI / 180;
                yield return (U * Math.Cos(r) + W * Math.Sin(r), Ang(a));
            }
            yield return (N, "+" + eixoN);
            yield return (-N, "−" + eixoN);
        }

        // ponto da reta mais perto do cursor na tela (projeção afim); null = reta de ponta para a câmera
        static Caminho? NaReta(Caminho k, Func<Point3D, Point> tela, Point c)
        {
            Point s0 = tela(k.O); Vector v = tela(k.O + k.D) - s0;
            if (v.LengthSquared < 1e-6) return null;
            double t = Vector.Multiply(c - s0, v) / v.LengthSquared;
            if (k.Raio && t <= 0) return null;
            k.T = t; k.Dist = (s0 + v * t - c).Length;
            return k;
        }

        // ---------- modos de objeto ----------

        Cand? Objetos(GeoSnap g, Func<Point3D, Point> tela, Point c, Point3D? base0, ModoSnap m, Vector3D? olho, PlanoTrabalho pl = null)
        {
            double ap = Abertura;
            var cs = new List<Cand>();
            void Pt(Point3D p, TipoSnap t, object dono, SegSnap s = null) { double d = (tela(p) - c).Length; if (d < ap) cs.Add(new Cand { P = p, D = d, T = t, Dono = dono, Seg = s }); }

            if (m.HasFlag(ModoSnap.No)) foreach (Point3D n in g.Nos) Pt(n, TipoSnap.No, null);
            var perto = new List<(SegSnap S, Point A, Point B, double T, double D)>();
            foreach (SegSnap s in g.Segs)
            {
                if (m.HasFlag(ModoSnap.Extremidade)) { Pt(s.A, TipoSnap.Extremidade, s.Dono, s); Pt(s.B, TipoSnap.Extremidade, s.Dono, s); }
                if (m.HasFlag(ModoSnap.Medio)) Pt(s.A + (s.B - s.A) / 2, TipoSnap.Medio, s.Dono, s);
                Point a = tela(s.A), b = tela(s.B);
                double d = DistSeg(c, a, b, out double t);
                if (d < ap) perto.Add((s, a, b, t, d));
            }
            if (perto.Count > 60) perto = perto.OrderBy(x => x.D).Take(60).ToList();   // ponytail: limita os pares de interseção perto do cursor

            if (m.HasFlag(ModoSnap.Centro))
                foreach (CirculoSnap k in g.Circulos)
                {
                    Vector3D n = k.N; n.Normalize();
                    Vector3D u = Vector3D.CrossProduct(n, Math.Abs(n.Z) > 0.9 ? new Vector3D(1, 0, 0) : new Vector3D(0, 0, 1)); u.Normalize();
                    Vector3D w = Vector3D.CrossProduct(n, u);
                    double d = (tela(k.C) - c).Length;
                    Point ant = tela(k.C + u * k.R);
                    for (int i = 1; i <= 32; i++)
                    {
                        double a = 2 * Math.PI * i / 32;
                        Point q = tela(k.C + (u * Math.Cos(a) + w * Math.Sin(a)) * k.R);
                        d = Math.Min(d, DistSeg(c, ant, q, out _)); ant = q;
                    }
                    if (d < ap) cs.Add(new Cand { P = k.C, D = d, T = TipoSnap.Centro, Dono = k.Dono });
                }

            if (m.HasFlag(ModoSnap.Intersecao) || m.HasFlag(ModoSnap.Aparente))
                for (int i = 0; i < perto.Count; i++)
                    for (int j = i + 1; j < perto.Count; j++)
                    {
                        SegSnap s1 = perto[i].S, s2 = perto[j].S;
                        Segmentos(s1.A, s1.B, s2.A, s2.B, out double t1, out double t2, out Point3D p1, out Point3D p2);
                        if ((p1 - p2).Length < TolInt)
                        {
                            if (m.HasFlag(ModoSnap.Intersecao)) Pt(p1 + (p2 - p1) / 2, TipoSnap.Intersecao, s1.Dono, s1);
                            continue;
                        }
                        if (!m.HasFlag(ModoSnap.Aparente)) continue;
                        if (!Segs2D(perto[i].A, perto[i].B, perto[j].A, perto[j].B, out double u1, out double u2)) continue;
                        Point3D q1 = s1.A + (s1.B - s1.A) * u1, q2 = s2.A + (s2.B - s2.A) * u2;
                        Vector3D o = olho ?? new Vector3D(0, 0, 1);
                        bool primeiro = Vector3D.DotProduct((Vector3D)q1, o) >= Vector3D.DotProduct((Vector3D)q2, o);
                        Pt(primeiro ? q1 : q2, TipoSnap.Aparente, primeiro ? s1.Dono : s2.Dono, primeiro ? s1 : s2);
                    }

            if (m.HasFlag(ModoSnap.Perpendicular) && base0 != null)
                foreach (var x in perto)
                {
                    Vector3D e = x.S.B - x.S.A; double l2 = e.LengthSquared; if (l2 < 1e-18) continue;
                    double t = Vector3D.DotProduct(base0.Value - x.S.A, e) / l2;
                    if (t < -1e-9 || t > 1 + 1e-9) continue;
                    Point3D f = x.S.A + e * t;
                    if ((f - base0.Value).Length < 1e-9) continue;
                    cs.Add(new Cand { P = f, D = (tela(f) - c).Length, T = TipoSnap.Perpendicular, Dono = x.S.Dono, Seg = x.S });
                }

            if (m.HasFlag(ModoSnap.Extensao))
                foreach (var (pa, _) in Adquiridos)
                    foreach (SegSnap s in g.Segs)
                    {
                        if (s.Aresta) continue;
                        Point3D outro = (s.A - pa).Length < 1e-9 ? s.B : (s.B - pa).Length < 1e-9 ? s.A : new Point3D(double.NaN, 0, 0);
                        if (double.IsNaN(outro.X)) continue;
                        Vector3D d = pa - outro; if (d.Length < 1e-12) continue; d.Normalize();
                        Caminho? k = NaReta(new Caminho { O = pa, D = d, Raio = true }, tela, c);
                        if (k == null || k.Value.Dist >= ap || k.Value.T < 1e-6) continue;
                        cs.Add(new Cand { P = pa + d * k.Value.T, D = k.Value.Dist, T = TipoSnap.Extensao, Dono = s.Dono, Seg = s, G = new GuiaSnap { O = pa, D = d, Raio = true } });
                    }

            if (m.HasFlag(ModoSnap.Paralelo) && base0 != null)
                foreach (SegSnap s in Paralelas)
                {
                    Vector3D d = s.B - s.A; if (d.Length < 1e-12) continue; d.Normalize();
                    Caminho? k = NaReta(new Caminho { O = base0.Value, D = d }, tela, c);
                    if (k == null || k.Value.Dist >= ap || Math.Abs(k.Value.T) < 1e-6) continue;
                    Vector3D dd = k.Value.T > 0 ? d : -d;
                    cs.Add(new Cand { P = base0.Value + d * k.Value.T, D = k.Value.Dist, T = TipoSnap.Paralelo, Dono = s.Dono, Seg = s, G = new GuiaSnap { O = base0.Value, D = dd, Raio = true } });
                }

            // Mais próximo só entra quando nenhum outro modo achou ponto (senão ele ganharia sempre: está a 0 px)
            if (cs.Count == 0 && m.HasFlag(ModoSnap.Proximo))
                foreach (var x in perto) cs.Add(new Cand { P = x.S.A + (x.S.B - x.S.A) * x.T, D = x.D, T = TipoSnap.Proximo, Dono = x.S.Dono, Seg = x.S });

            if (cs.Count == 0) return null;
            double dmin = cs.Min(x => x.D);
            Cand best = cs.Where(x => x.D <= dmin + Empate).OrderBy(x => Array.IndexOf(Prioridade, x.T)).ThenBy(x => x.D).First();
            // E2E-04: pontos do mesmo tipo no mesmo pixel (em planta, topo e pé do pilar): vale o do plano de trabalho,
            // depois o mais perto de quem olha. Antes valia a ordem da lista (a ponta A, o pé).
            Point pb = tela(best.P); Vector3D oo = olho ?? new Vector3D(0, 0, 1);
            double Fora(Point3D p) => pl == null ? 0 : Math.Round((p - pl.Projetar(p)).Length, 6);
            return cs.Where(x => x.T == best.T && (tela(x.P) - pb).Length < 0.5)
                     .OrderBy(x => Fora(x.P)).ThenByDescending(x => Vector3D.DotProduct((Vector3D)x.P, oo)).First();
        }

        // Aquisição por pausa (OTRACK/EXT): cursor parado TempoAquisicao ms sobre o mesmo ponto adquire; de novo, remove.
        // Parado sobre uma barra (sem ponto) com o paralelo ligado e um 1º ponto: adquire a direção da barra.
        void Pausa(Cand? k, GeoSnap g, Func<Point3D, Point> tela, Point c, Point3D? base0, double tMs, ModoSnap m)
        {
            string chave = null; SegSnap seg = null;
            if (k != null && k.Value.T != TipoSnap.Proximo && k.Value.T != TipoSnap.Extensao && k.Value.T != TipoSnap.Paralelo && k.Value.T != TipoSnap.Perpendicular)
                chave = "P" + Math.Round(k.Value.P.X, 6) + ";" + Math.Round(k.Value.P.Y, 6) + ";" + Math.Round(k.Value.P.Z, 6);
            else if (base0 != null && m.HasFlag(ModoSnap.Paralelo) && (k == null || k.Value.T == TipoSnap.Proximo || k.Value.T == TipoSnap.Perpendicular))
            {
                double dm = Abertura;
                foreach (SegSnap s in g.Segs) { if (s.Aresta) continue; double d = DistSeg(c, tela(s.A), tela(s.B), out _); if (d < dm) { dm = d; seg = s; } }
                if (seg != null) chave = "S" + g.Segs.IndexOf(seg);
            }
            if (chave == null) { pausaChave = null; return; }
            if (chave != pausaChave) { pausaChave = chave; pausaIni = tMs; pausaFeita = false; return; }
            if (pausaFeita || tMs - pausaIni < TempoAquisicao) return;
            pausaFeita = true;
            if (seg != null) { Paralelas.Clear(); Paralelas.Add(seg); return; }
            if (!(Rastreio || m.HasFlag(ModoSnap.Extensao))) return;
            int i = Adquiridos.FindIndex(a => (a.P - k.Value.P).Length < 1e-9);
            if (i >= 0) Adquiridos.RemoveAt(i);
            else { Adquiridos.Add((k.Value.P, k.Value.T)); if (Adquiridos.Count > MaxAdquiridos) Adquiridos.RemoveAt(0); }
        }

        // ---------- geometria ----------

        public static double DistSeg(Point c, Point a, Point b, out double t)
        {
            Vector ab = b - a; double l2 = ab.LengthSquared;
            t = l2 < 1e-12 ? 0 : Math.Max(0, Math.Min(1, Vector.Multiply(c - a, ab) / l2));
            return (a + ab * t - c).Length;
        }

        static bool Segs2D(Point a, Point b, Point c, Point d, out double t, out double u)
        {
            Vector r = b - a, s = d - c; double den = r.X * s.Y - r.Y * s.X;
            t = u = 0;
            if (Math.Abs(den) < 1e-9) return false;
            Vector q = c - a;
            t = (q.X * s.Y - q.Y * s.X) / den; u = (q.X * r.Y - q.Y * r.X) / den;
            return t >= 0 && t <= 1 && u >= 0 && u <= 1;
        }

        // Pontos mais próximos entre os segmentos p1-q1 e p2-q2 (Ericson, Real-Time Collision Detection §5.1.9).
        public static void Segmentos(Point3D p1, Point3D q1, Point3D p2, Point3D q2, out double s, out double t, out Point3D c1, out Point3D c2)
        {
            Vector3D d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
            double a = d1.LengthSquared, e = d2.LengthSquared, f = Vector3D.DotProduct(d2, r);
            const double eps = 1e-18;
            if (a <= eps && e <= eps) { s = t = 0; }
            else if (a <= eps) { s = 0; t = Lim(f / e); }
            else
            {
                double cc = Vector3D.DotProduct(d1, r);
                if (e <= eps) { t = 0; s = Lim(-cc / a); }
                else
                {
                    double b = Vector3D.DotProduct(d1, d2), den = a * e - b * b;
                    s = den > eps * a * e ? Lim((b * f - cc * e) / den) : 0;
                    t = (b * s + f) / e;
                    if (t < 0) { t = 0; s = Lim(-cc / a); }
                    else if (t > 1) { t = 1; s = Lim((b - cc) / a); }
                }
            }
            c1 = p1 + d1 * s; c2 = p2 + d2 * t;
        }
        static double Lim(double x) => Math.Max(0, Math.Min(1, x));

        // Retas infinitas o1 + d1·t1 e o2 + d2·t2: parâmetros dos pontos mais próximos e a folga entre elas. false = paralelas.
        public static bool Retas(Point3D o1, Vector3D d1, Point3D o2, Vector3D d2, out double t1, out double t2, out double gap)
        {
            Vector3D r = o1 - o2;
            double a = d1.LengthSquared, b = Vector3D.DotProduct(d1, d2), e = d2.LengthSquared, c = Vector3D.DotProduct(d1, r), f = Vector3D.DotProduct(d2, r);
            double den = a * e - b * b;
            t1 = t2 = gap = 0;
            if (Math.Abs(den) < 1e-12 * a * e) return false;
            t1 = (b * f - c * e) / den; t2 = (a * f - b * c) / den;
            gap = ((o1 + d1 * t1) - (o2 + d2 * t2)).Length;
            return true;
        }

        // C1: direção da medida digitada. Segue o ponto 3D do cursor (já com Polar/Orto/snap de objeto, inclusive ponto alto).
        // Vertical (+Z/-Z) só pelo rastreio em Z: cursor a até 10 px da vertical que passa pelo 1º ponto (linha tracejada + "Z"),
        // ou plano de trabalho de perfil na vista (cursor3 null). Chão visto de longe/de cima continua horizontal.
        // telaA/telaZ: tela do 1º ponto e de 1º ponto + 1 m em Z. null = sem direção.
        public static Vector3D? DirecaoDigitada(Point3D a, Point3D? cursor3, Point telaA, Point telaZ, Point telaMouse, bool travaZ = false)
        {
            int z = TrilhaZ(telaA, telaZ, telaMouse, travaZ);
            if (z != 0) return new Vector3D(0, 0, z);
            if (cursor3 == null) return null;
            Vector3D d = cursor3.Value - a;
            if (d.Length < 1e-9) return null;
            d.Normalize(); return d;
        }

        // Rastreio em Z: +1/-1 se o mouse está a ≤ 10 px da reta vertical (na tela) que passa pelo 1º ponto, para cima/baixo; 0 = fora.
        // travaZ (tecla Z): vertical sempre, para cima/baixo conforme o mouse. Sem a trava: se o chão (direção do cursor na tela) e Z
        // caem quase na mesma linha da tela (< 5°, vista de frente), o chão vence e a trilha não vale.
        public static int TrilhaZ(Point telaA, Point telaZ, Point telaMouse, bool travaZ = false)
        {
            Vector z = telaZ - telaA, m = telaMouse - telaA;
            if (z.Length < 4) return 0;   // Z de ponta (vista de topo)
            z.Normalize();
            if (travaZ) return m.Length < 1 || Vector.Multiply(m, z) >= 0 ? 1 : -1;
            if (m.Length < 6) return 0;   // mouse em cima do ponto
            double ang = Math.Abs(Vector.AngleBetween(z, m)); if (ang < 5 || ang > 175) return 0;
            double perp = Math.Abs(m.X * z.Y - m.Y * z.X);
            return perp <= 10 ? (Vector.Multiply(m, z) >= 0 ? 1 : -1) : 0;
        }

        // C2: grip só esquenta num clique (soltar a menos de 6 px de onde apertou); arrastar a partir dele não faz nada.
        public const double LimiarGrip = 6;
        public static bool CliqueNoGrip(Vector arrasto) => arrasto.Length < LimiarGrip;

        // ---------- entrada digitada (AutoCAD, entrada dinâmica) ----------
        // "x,y,z" ou "u,w" (no plano): com 1º ponto é RELATIVO a ele (padrão do AutoCAD do 2º ponto em diante); "@" relativo
        // explícito; "#" absoluto; "L<a" polar no plano (relativo; a de U para W, graus). Ponto decimal (a vírgula separa).
        public static Point3D? LerCoordenada(string texto, Point3D? base0, PlanoTrabalho pl)
        {
            string s = (texto ?? "").Trim(); if (s == "") return null;
            bool abs = s.StartsWith("#"), rel = s.StartsWith("@");
            if (abs || rel) s = s.Substring(1).Trim();
            bool relativo = !abs && (rel || base0 != null);
            Point3D origem = base0 ?? new Point3D();
            static bool L(string x, out double v) => double.TryParse(x.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && double.IsFinite(v);
            if (s.Contains("<"))
            {
                var pa = s.Split('<');
                if (pa.Length != 2 || !L(pa[0], out double len) || !L(pa[1], out double ang)) return null;
                double a = ang * Math.PI / 180;
                Vector3D dv = (pl.U * Math.Cos(a) + pl.W * Math.Sin(a)) * len;
                return (relativo ? origem : pl.Ponto(0, 0)) + dv;
            }
            var v = s.Split(new[] { ',', ';' }).Select(x => L(x, out double d) ? d : double.NaN).ToArray();
            if (v.Any(double.IsNaN) || (v.Length != 2 && v.Length != 3)) return null;
            if (v.Length == 3) { var d3 = new Vector3D(v[0], v[1], v[2]); return relativo ? origem + d3 : (Point3D)d3; }
            return relativo ? origem + pl.U * v[0] + pl.W * v[1] : pl.Ponto(v[0], v[1]);
        }
    }

    // Estructto.exe --testes-snap <saida.txt>: o motor de snap sem janela (projeção de topo, 50 px por metro).
    public static class TestesSnap
    {
        public static string Rodar()
        {
            var sb = new StringBuilder(); int falhas = 0;
            void Ok(string nome, bool c) { sb.AppendLine((c ? "ok    " : "FALHA ") + nome); if (!c) falhas++; }
            bool Perto(Point3D? a, double x, double y, double z) => a != null && (a.Value - new Point3D(x, y, z)).Length < 1e-6;

            // vista de topo: x para a direita, y para cima, 50 px/m, origem no pixel (100, 500); olho em +Z
            Point Tela(Point3D p) => new Point(100 + 50 * p.X, 500 - 50 * p.Y);
            Point Px(double x, double y) => Tela(new Point3D(x, y, 0));
            PlanoTrabalho pl = PlanoTrabalho.Horizontal(0);
            Point3D? NoPlano(Point s) => new Point3D((s.X - 100) / 50, (500 - s.Y) / 50, 0);
            var olho = new Vector3D(0, 0, 1);

            // B1 (0,0)-(6,0); B2 (3,-2)-(3,2) cruza B1 em (3,0); B3 (8,0)-(8,4); B4 (0,5)-(4,5) em z = 3 (cruza B2 só na vista? não: B2 vai até y=2)
            // B5 (5,-1,3)-(5,3,3): passa sobre B1 em z = 3 (interseção só aparente na vista de topo)
            var g = new GeoSnap();
            void Seg(double x1, double y1, double z1, double x2, double y2, double z2, string dono) => g.Segs.Add(new SegSnap { A = new Point3D(x1, y1, z1), B = new Point3D(x2, y2, z2), Dono = dono });
            Seg(0, 0, 0, 6, 0, 0, "B1"); Seg(3, -2, 0, 3, 2, 0, "B2"); Seg(8, 0, 0, 8, 4, 0, "B3"); Seg(0, 5, 0, 4, 5, 0, "B4"); Seg(5, -1, 3, 5, 3, 3, "B5");
            g.Nos.AddRange(new[] { new Point3D(0, 0, 0), new Point3D(6, 0, 0), new Point3D(10, 2, 0) });   // (10,2): nó solto
            g.Circulos.Add(new CirculoSnap { C = new Point3D(8, 4, 0), N = new Vector3D(0, 0, 1), R = 0.1, Dono = "B3" });

            ResultadoSnap R(Snap3D s, Point c, Point3D? b = null, double t = 0, bool shift = false) => s.Resolver(g, Tela, NoPlano, pl, c, b, t, shift, olho);
            Snap3D Novo() => new Snap3D { PassoGrade = 1 };

            // --- modos de objeto ---
            var s1 = Novo();
            var r = R(s1, Px(6, 0) + new Vector(4, 3));
            Ok("extremidade a 5 px: □ em (6,0,0)", r.Tipo == TipoSnap.Extremidade && Perto(r.P, 6, 0, 0));
            r = R(s1, Px(6, 0) + new Vector(11, 0));
            Ok("fora da abertura (11 px): não pega a extremidade", r.Tipo != TipoSnap.Extremidade);
            r = R(s1, Px(1.5, 0) + new Vector(0, -4));
            Ok("no meio do vão de B1, sem ponto perto: plano", r.Tipo == TipoSnap.Plano);
            r = R(s1, Px(2, 5) + new Vector(3, 3));
            Ok("ponto médio: △ em (2,5,0)", r.Tipo == TipoSnap.Medio && Perto(r.P, 2, 5, 0));
            r = R(s1, Px(3, 0) + new Vector(2, 2));
            Ok("interseção real B1×B2: × em (3,0,0) (vence o meio de B1 no mesmo ponto pela prioridade)", r.Tipo == TipoSnap.Intersecao && Perto(r.P, 3, 0, 0));
            r = R(s1, Px(5, 0) + new Vector(1, -2));
            Ok("interseção aparente B1 (z=0) × B5 (z=3): ponto em B5 (mais perto do olho)", r.Tipo == TipoSnap.Aparente && Perto(r.P, 5, 0, 3) && (string)r.Dono == "B5");
            r = R(s1, Px(8, 4) + new Vector(0, 5.5));
            Ok("centro: passar na borda do tubo pega o centro (8,4,0)", r.Tipo == TipoSnap.Centro && Perto(r.P, 8, 4, 0));
            r = R(s1, Px(10, 2) + new Vector(-3, 2));
            Ok("nó solto: ⊗ em (10,2,0)", r.Tipo == TipoSnap.No && Perto(r.P, 10, 2, 0));
            r = R(s1, Px(0, 0) + new Vector(1, 1));
            Ok("empate nó × extremidade no mesmo ponto: extremidade (prioridade)", r.Tipo == TipoSnap.Extremidade);
            // E2E-04: pilar (20,20,0)-(20,20,6) visto de topo: pé e topo no mesmo pixel; a ponta A (1ª da lista) é o pé
            var gp = new GeoSnap(); gp.Segs.Add(new SegSnap { A = new Point3D(20, 20, 0), B = new Point3D(20, 20, 6), Dono = "P1" });
            Point3D? Pilar(PlanoTrabalho pp) => Novo().Resolver(gp, Tela, NoPlano, pp, Px(20, 20) + new Vector(2, 1), null, 0, false, olho).P;
            Ok("E2E-04 planta, plano z=6: extremidade no topo (20,20,6)", Perto(Pilar(PlanoTrabalho.Horizontal(6)), 20, 20, 6));
            Ok("E2E-04 planta, plano z=0: extremidade no pé (20,20,0)", Perto(Pilar(PlanoTrabalho.Horizontal(0)), 20, 20, 0));
            Ok("E2E-04 planta, plano z=3 (nenhuma ponta nele): a mais perto do olho (20,20,6)", Perto(Pilar(PlanoTrabalho.Horizontal(3)), 20, 20, 6));
            r = R(s1, Px(8, 1.3) + new Vector(4, 0), new Point3D(10, 1, 0));
            Ok("perpendicular de (10,1) em B3: pé (8,1,0)", r.Tipo == TipoSnap.Perpendicular && Perto(r.P, 8, 1, 0));
            var sNea = Novo(); sNea.Modos = ModoSnap.Proximo | ModoSnap.Extremidade;
            r = R(sNea, Px(1.3, 0) + new Vector(0, 4));
            Ok("mais próximo: (1,3; 0; 0) em B1", r.Tipo == TipoSnap.Proximo && Perto(r.P, 1.3, 0, 0));
            r = R(sNea, Px(6, 0) + new Vector(-3, 2));
            Ok("mais próximo não engole a extremidade a 3 px", r.Tipo == TipoSnap.Extremidade);

            // --- one-off e F3 ---
            var s2 = Novo(); s2.Unico = ModoSnap.Medio;
            r = R(s2, Px(6, 0) + new Vector(2, 0));
            Ok("one-off MID: ignora a extremidade sob o cursor", r.Tipo != TipoSnap.Extremidade);
            r = R(s2, Px(3, 0) + new Vector(2, 0));
            Ok("one-off MID: pega o meio de B1", r.Tipo == TipoSnap.Medio && Perto(r.P, 3, 0, 0));
            s2.ConsumirUnico();
            r = R(s2, Px(6, 0) + new Vector(2, 0));
            Ok("depois do clique o running volta", r.Tipo == TipoSnap.Extremidade);
            s2.Unico = ModoSnap.Desligar;
            r = R(s2, Px(6, 0) + new Vector(2, 0));
            Ok("NON: sem snap de objeto", r.Tipo == TipoSnap.Grade || r.Tipo == TipoSnap.Plano);
            var s3 = Novo(); s3.Osnap = false;
            r = R(s3, Px(6, 0) + new Vector(2, 0));
            Ok("F3 desligado: sem snap de objeto", r.Tipo != TipoSnap.Extremidade);
            Ok("palavras END/MID/INT/PER/NEA/NON", Snap3D.Palavra("end") == ModoSnap.Extremidade && Snap3D.Palavra("MID") == ModoSnap.Medio && Snap3D.Palavra("int") == ModoSnap.Intersecao
                && Snap3D.Palavra("PER") == ModoSnap.Perpendicular && Snap3D.Palavra("nea") == ModoSnap.Proximo && Snap3D.Palavra("NON") == ModoSnap.Desligar && Snap3D.Palavra("BARRA") == null);

            // --- polar e ORTO ---
            var s4 = Novo(); s4.PolarInc = 30;
            Point3D b0 = new Point3D(0, 10, 0);
            double a30 = Math.PI / 6;
            r = R(s4, Tela(b0 + new Vector3D(Math.Cos(a30), Math.Sin(a30), 0) * 4.25) + new Vector(1, 1.732), b0);
            Ok("polar 30°: tooltip \"Polar: 4,250 < 30°\"", r.Tipo == TipoSnap.Polar && r.Dica == "Polar: 4,250 < 30°");
            Ok("polar 30°: ponto no raio de 30°", r.P != null && Math.Abs(Math.Atan2(r.P.Value.Y - 10, r.P.Value.X) * 180 / Math.PI - 30) < 1e-6);
            r = R(s4, Tela(b0 + new Vector3D(Math.Cos(a30), Math.Sin(a30), 0) * 4.02) + new Vector(1, -1), b0);
            Ok("polar + grade: passo de 1 m ao longo do caminho (4,000)", r.Tipo == TipoSnap.Polar && r.Dica == "Polar: 4,000 < 30°");
            s4.Orto = true;
            Ok("ORTO liga e desliga o Polar", !s4.Polar);
            r = R(s4, Tela(b0 + new Vector3D(3, 0.8, 0)), b0);
            Ok("ORTO: força o eixo X (3,10,0)", r.Tipo == TipoSnap.Ortogonal && Perto(r.P, 3, 10, 0));
            r = R(s4, Tela(b0 + new Vector3D(-0.1, -2.05, 0)), b0);
            Ok("ORTO: eixo Y para baixo (0,8,0) com tooltip −", r.Tipo == TipoSnap.Ortogonal && Perto(r.P, 0, 8, 0) && r.Dica == "Ortogonal: 2,000 < 270°");
            r = R(s4, Tela(b0 + new Vector3D(3, 0.8, 0)), null, 0, true);
            Ok("ORTO sem 1º ponto não age", r.Tipo != TipoSnap.Ortogonal);
            s4.Polar = true;
            Ok("Polar liga e desliga o ORTO", !s4.Orto);
            r = R(s4, Tela(b0 + new Vector3D(3, 0.8, 0)), b0, 0, true);
            Ok("Shift inverte o ORTO (Polar ligado)", r.Tipo == TipoSnap.Ortogonal && Perto(r.P, 3, 10, 0));

            // --- rastreamento: pausa de 0,5 s adquire; dois caminhos se cruzam ---
            var s5 = Novo(); s5.Polar = false;
            R(s5, Px(8, 4) + new Vector(1, 0), null, 0);
            R(s5, Px(8, 4) + new Vector(1, 0), null, 300);
            Ok("300 ms parado: ainda não adquiriu", s5.Adquiridos.Count == 0);
            R(s5, Px(8, 4) + new Vector(1, 0), null, 520);
            Ok("520 ms parado: adquiriu (8,4,0)", s5.Adquiridos.Count == 1 && Perto(s5.Adquiridos[0].P, 8, 4, 0));
            R(s5, Px(6, 0) + new Vector(1, 0), null, 600);
            R(s5, Px(6, 0) + new Vector(1, 0), null, 1200);
            Ok("2º ponto adquirido (6,0,0)", s5.Adquiridos.Count == 2);
            r = R(s5, Px(12, 4) + new Vector(0, 3), null, 1300);
            Ok("caminho horizontal de (8,4): rastreio em (12,4,0)", r.Tipo == TipoSnap.Rastreio && Perto(r.P, 12, 4, 0) && r.Dica == "Extremidade: 4,000 < 0°");
            r = R(s5, Px(6, 4) + new Vector(3, -2), null, 1400);
            Ok("cruzamento: vertical de (6,0) × horizontal de (8,4) = (6,4,0)", r.Tipo == TipoSnap.Rastreio && Perto(r.P, 6, 4, 0) && r.Guias.Count == 2);
            R(s5, Px(6, 0), null, 2000); R(s5, Px(6, 0), null, 2600);
            Ok("pausar de novo sobre o ponto adquirido remove", s5.Adquiridos.Count == 1);
            s5.LimparRastreio();
            Ok("limpar rastreio", s5.Adquiridos.Count == 0);

            // --- extensão: adquire a ponta (6,0) e segue além dela ---
            var s6 = Novo(); s6.Polar = false; s6.Rastreio = false;
            R(s6, Px(6, 0), null, 0); R(s6, Px(6, 0), null, 600);
            r = R(s6, Px(7.5, 0) + new Vector(0, 3), null, 700);
            Ok("extensão de B1 além de (6,0): (7,5; 0; 0) com tooltip \"Extensão: 1,500\"", r.Tipo == TipoSnap.Extensao && Perto(r.P, 7.5, 0, 0) && r.Dica == "Extensão: 1,500");

            // --- extensão × rastreio: a extensão de B1 cruza a vertical do nó (10,2) em (10,0)
            var s9 = Novo(); s9.Polar = false;
            R(s9, Px(6, 0), null, 0); R(s9, Px(6, 0), null, 600);
            R(s9, Px(10, 2), null, 700); R(s9, Px(10, 2), null, 1300);
            Ok("2 pontos adquiridos (ponta e nó)", s9.Adquiridos.Count == 2);
            r = R(s9, Px(10, 0) + new Vector(2, 2), null, 1400);
            Ok("extensão × caminho do nó: (10,0,0) \"Extensão: 4,000, Nó: 2,000 < 270°\"", r.Tipo == TipoSnap.Rastreio && Perto(r.P, 10, 0, 0) && r.Dica == "Extensão: 4,000, Nó: 2,000 < 270°" && r.Guias.Count == 2);
            r = R(s9, Px(8.5, 0) + new Vector(0, 2), null, 1500);
            Ok("só a extensão (sem cruzamento): vale como snap de objeto", r.Tipo == TipoSnap.Extensao && Perto(r.P, 8.5, 0, 0));

            // --- paralelo: pausa sobre B3 (vertical) com 1º ponto em (11,0) ---
            var s7 = Novo(); s7.Polar = false;
            Point3D b7 = new Point3D(11, 0, 0);
            R(s7, Px(8, 2.5) + new Vector(1, 0), b7, 0); R(s7, Px(8, 2.5) + new Vector(1, 0), b7, 600);
            Ok("paralelo: adquiriu a direção de B3", s7.Paralelas.Count == 1 && (string)s7.Paralelas[0].Dono == "B3");
            r = R(s7, Px(11, 3) + new Vector(4, 0), b7, 700);
            Ok("paralelo a B3 a partir de (11,0): (11,3,0)", r.Tipo == TipoSnap.Paralelo && Perto(r.P, 11, 3, 0));

            // --- grade e plano ---
            var s8 = Novo();
            r = R(s8, Px(12, 7) + new Vector(3, 3));
            Ok("grade: (12,7,0)", r.Tipo == TipoSnap.Grade && Perto(r.P, 12, 7, 0));
            r = R(s8, Px(12.4, 7.4));
            Ok("plano: (12,4; 7,4; 0)", r.Tipo == TipoSnap.Plano && Perto(r.P, 12.4, 7.4, 0));

            // --- geometria ---
            Snap3D.Segmentos(new Point3D(0, 0, 0), new Point3D(2, 0, 0), new Point3D(1, -1, 1), new Point3D(1, 1, 1), out _, out _, out Point3D c1, out Point3D c2);
            Ok("segmentos reversos: pontos mais próximos (1,0,0)-(1,0,1)", Perto(c1, 1, 0, 0) && Perto(c2, 1, 0, 1));
            Snap3D.Segmentos(new Point3D(0, 0, 0), new Point3D(1, 0, 0), new Point3D(3, 1, 0), new Point3D(3, 2, 0), out _, out _, out c1, out c2);
            Ok("segmentos afastados: ponta (1,0,0) e ponta (3,1,0)", Perto(c1, 1, 0, 0) && Perto(c2, 3, 1, 0));

            // --- coordenada digitada ---
            Point3D bb = new Point3D(1, 2, 3);
            Ok("\"4,5,6\" com 1º ponto = relativo (5,7,9)", Perto(Snap3D.LerCoordenada("4,5,6", bb, pl), 5, 7, 9));
            Ok("\"#4,5,6\" = absoluto", Perto(Snap3D.LerCoordenada("#4,5,6", bb, pl), 4, 5, 6));
            Ok("\"@1,1\" no plano z=0 = (2,3,3)", Perto(Snap3D.LerCoordenada("@1,1", bb, pl), 2, 3, 3));
            Ok("\"4,5,6\" sem 1º ponto = absoluto", Perto(Snap3D.LerCoordenada("4,5,6", null, pl), 4, 5, 6));
            Ok("\"2<90\" polar relativo = (1,4,3)", Perto(Snap3D.LerCoordenada("2<90", bb, pl), 1, 4, 3));
            Ok("\"1,x\" inválido", Snap3D.LerCoordenada("1,x", bb, pl) == null);

            // --- C1: medida digitada segue o cursor em 3D; vertical só pelo rastreio em Z; C2: limiar do grip ---
            var A0 = new Point3D(0, 0, 0); Point tA = new Point(300, 400), tZ = new Point(300, 350);   // Z aparece 50 px/m para cima
            bool Igual(Vector3D? v, double x, double y, double zz) => v != null && (v.Value - new Vector3D(x, y, zz)).Length < 1e-9;
            Ok("C1 mouse na horizontal: direção do cursor no plano (+X)", Igual(Snap3D.DirecaoDigitada(A0, new Point3D(4, 0, 0), tA, tZ, new Point(380, 410)), 1, 0, 0));
            // chão se afastando do observador em 3 inclinações de vista: o cursor sobe na tela mas NÃO está sobre a vertical: fica horizontal
            foreach (double pitch in new[] { 20.0, 35.0, 60.0 })
            {
                double k = Math.Cos(pitch * Math.PI / 180);   // 1 m de profundidade (+Y) sobe k*50 px na tela; Z sobe 50*cos(pitch) → escalas só mudam o tamanho
                Point tZp = new Point(300, 400 - 50 * Math.Cos(pitch * Math.PI / 180)); Point cur = new Point(300 + 60, 400 - 60 * Math.Sin(pitch * Math.PI / 180) - 20);
                Ok("C1 chão se afastando (pitch " + pitch + "°): fica horizontal (+Y)", Igual(Snap3D.DirecaoDigitada(A0, new Point3D(0, 4, 0), tA, tZp, cur), 0, 1, 0));
            }
            Ok("C1 cursor na linha de rastreio Z (6 px, ângulo > 5°): vertical +Z", Igual(Snap3D.DirecaoDigitada(A0, new Point3D(0, 4, 0), tA, tZ, new Point(306, 380)), 0, 0, 1));
            Ok("C1 rastreio Z para baixo: -Z", Igual(Snap3D.DirecaoDigitada(A0, new Point3D(0, 4, 0), tA, tZ, new Point(294, 440)), 0, 0, -1));
            Ok("C1 11 px fora da linha Z: horizontal", Igual(Snap3D.DirecaoDigitada(A0, new Point3D(0, 4, 0), tA, tZ, new Point(311, 300)), 0, 1, 0));
            Ok("C1 cursor num ponto alto (snap): direção inclinada (3,0,4)/5", Igual(Snap3D.DirecaoDigitada(A0, new Point3D(3, 0, 4), tA, tZ, new Point(380, 410)), 0.6, 0, 0.8));
            Ok("C1 Polar 30° (cursor já atraído): a medida sai a 30°", Igual(Snap3D.DirecaoDigitada(A0, new Point3D(4 * Math.Cos(Math.PI / 6), 4 * Math.Sin(Math.PI / 6), 0), tA, tZ, new Point(380, 360)), Math.Cos(Math.PI / 6), Math.Sin(Math.PI / 6), 0));
            Ok("C1 Orto (cursor no eixo X) + rastreio Z: vertical ainda possível", Igual(Snap3D.DirecaoDigitada(A0, new Point3D(4, 0, 0), tA, tZ, new Point(306, 380)), 0, 0, 1) && Igual(Snap3D.DirecaoDigitada(A0, new Point3D(4, 0, 0), tA, tZ, new Point(380, 400)), 1, 0, 0));
            Ok("C1 plano de perfil (cursor3 nulo) fora da trilha: sem direção", Snap3D.DirecaoDigitada(A0, null, tA, tZ, new Point(380, 400)) == null);
            Ok("C1 vista de frente: chão e Z na mesma linha da tela (<5°): horizontal", Igual(Snap3D.DirecaoDigitada(A0, new Point3D(0, 4, 0), tA, tZ, new Point(300, 300)), 0, 1, 0));
            Ok("C1 vista de frente + trava Z (tecla Z): vertical +Z, e -Z com o mouse abaixo", Igual(Snap3D.DirecaoDigitada(A0, new Point3D(0, 4, 0), tA, tZ, new Point(300, 300), true), 0, 0, 1) && Igual(Snap3D.DirecaoDigitada(A0, new Point3D(0, 4, 0), tA, tZ, new Point(300, 480), true), 0, 0, -1));
            Ok("C1 vista de topo (Z de ponta): nunca vertical", Snap3D.TrilhaZ(tA, tA, new Point(300, 300)) == 0);
            Ok("C2 5 px de arrasto ainda é clique no grip", Snap3D.CliqueNoGrip(new Vector(3, 4)));
            Ok("C2 6 px ou mais é arrasto: grip não esquenta", !Snap3D.CliqueNoGrip(new Vector(6, 0)) && !Snap3D.CliqueNoGrip(new Vector(4, 5)));

            // --- medidas (Medir3D.cs) ---
            TestesMedir.Rodar(Ok);

            sb.AppendLine(falhas == 0 ? "TODOS PASSARAM" : falhas + " FALHA(S)");
            return sb.ToString();
        }
    }
}
