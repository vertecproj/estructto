// Solda de filete em volta do perfil (pilar na placa de base, ou ponta de barra soldada em outra peca).
// Referencia: pasta "referências" — Tabela solda (Shigley, Tab. 9-1/9-2: area de garganta e segundo momento
// unitario do grupo de soldas) e Tabela solda - Resistencia eletrodo (tensao admissivel de corte no filete por
// eletrodo, AISC: E60 124 ... E120 248 MPa). Tudo e tratado como corte na garganta (metodo do Shigley):
//   tau' = V / A          (corte primario)
//   tau'' = N / A + M c / I,  I = 0,707 h Iu   (normal no plano da garganta)
//   tau = raiz(tau'^2 + tau''^2)  <=  tau admissivel do eletrodo   (a tabela ja inclui a seguranca da AISC)
// Grupos (soldado todo o contorno):
//   I, U, TR, RET, L e generico: retangulo b x d    A = 1,414 h (b + d)   Iu = d^2 (3b + d) / 6     (casos 6/8)
//   TC, CIRC:                    circulo raio r     A = 1,414 pi h r      Iu = pi r^3              (caso 9)
using System;
using System.Collections.Generic;
using Portico;

namespace NovoFtool
{
    public class ResSolda
    {
        public string Erro = "";
        public string Grupo = "";
        public double H, A, I, Tau1, Tau2, Tau, TauAdm, Ap;   // mm, mm2, mm4, MPa
        public double L, LNec, Massa;                          // mm, mm, kg
        public double HMin;                                // menor perna da tabela que passa (0 = nenhuma)
        public double Espessura;                               // espessura da parede/mesa do perfil, mm (limite pratico da perna)
        public double TChapa, HMinNorma;                       // espessura da chapa (mm) e perna minima da NBR 8800 Tab. 10 / AWS D1.1 pela parte mais fina
        public bool AbaixoMinimo { get { return HMinNorma > 0 && H < HMinNorma - 1e-9; } }
        public bool MaiorQueParede { get { return Espessura > 0 && H > Espessura + 1e-9; } }
        public double NEquiv { get { return Tau > 1e-12 ? 0.577 * (TauAdm / 0.30) / Tau : double.PositiveInfinity; } }   // seguranca equivalente: 0,577.Fexx / tau
        public bool Passa { get { return Erro == "" && Ap <= 1 && !AbaixoMinimo && !MaiorQueParede; } }
    }

    public static class Solda
    {
        public static readonly string[] Eletrodos = { "E60", "E70", "E80", "E90", "E100", "E110", "E120" };
        public static readonly double[] TauAdm = { 124, 145, 165, 186, 207, 228, 248 };          // MPa
        public static readonly double[] Pernas = { 2, 3, 5, 6, 8, 10, 11, 12, 16, 20, 22, 25 };  // mm (tabela)

        public static double Admissivel(string eletrodo)
        {
            int i = Array.IndexOf(Eletrodos, string.IsNullOrEmpty(eletrodo) ? "E70" : eletrodo.ToUpperInvariant().Replace("XX", ""));
            return TauAdm[i < 0 ? 1 : i];
        }

        // N (kN, tracao +), V (kN), M (kN.m): esforcos na ponta soldada. h em mm.
        public static ResSolda Calcular(Secao sec, bool deitado, double N, double V, double M, double h, string eletrodo, double tChapa = 0)
        {
            var r = new ResSolda { H = h, TauAdm = Admissivel(eletrodo) };
            if (sec == null || sec.H <= 0) { r.Erro = "Seção sem medidas para o grupo de solda."; return r; }
            double d = (deitado ? (sec.Bf > 0 ? sec.Bf : sec.H) : sec.H) * 1000;   // no plano da flexao
            double b = (deitado ? sec.H : (sec.Bf > 0 ? sec.Bf : sec.H)) * 1000;
            double c, iu, mExtra = 0;   // mExtra: N·e do centroide do perfil ao do grupo (U no eixo fraco), N·mm
            double lu, iuU, cU, eU;      // C# 5 (csc do Windows, portico\compilar.ps1): sem "out var"
            if (sec.Forma == "TC" || sec.Forma == "CIRC")
            {
                double raio = sec.H * 1000 / 2;
                r.A = 1.414 * Math.PI * h * raio; iu = Math.PI * raio * raio * raio; c = raio;
                r.Grupo = "contorno circular Ø" + Num.Fmt(sec.H * 1000, 1) + " mm";
            }
            else if (GrupoU(sec, deitado, out lu, out iuU, out cU, out eU))
            {
                r.A = 0.707 * h * lu; iu = iuU; c = cU; mExtra = Math.Abs(N) * eU * 1e3;
                r.Grupo = "contorno aberto do U, " + Num.Fmt(lu, 0) + " mm";
            }
            else
            {
                r.A = 1.414 * h * (b + d); iu = d * d * (3 * b + d) / 6; c = d / 2;
                r.Grupo = "contorno " + Num.Fmt(b, 0) + " × " + Num.Fmt(d, 0) + " mm";
            }
            r.I = 0.707 * h * iu;
            r.Tau1 = Math.Abs(V) * 1000 / r.A;
            r.Tau2 = Math.Abs(N) * 1000 / r.A + (Math.Abs(M) * 1e6 + mExtra) * c / r.I;
            r.Tau = Math.Sqrt(r.Tau1 * r.Tau1 + r.Tau2 * r.Tau2);
            r.Ap = r.Tau / r.TauAdm;
            double t = sec.Forma == "I" || sec.Forma == "U" ? Math.Min(sec.Tf, sec.Tw) : sec.Tw > 0 ? sec.Tw : 0;
            r.Espessura = t * 1000;
            r.TChapa = tChapa;
            r.HMinNorma = PernaMinimaNorma(Math.Min(r.Espessura > 0 ? r.Espessura : 1e9, tChapa > 0 ? tChapa : 1e9));
            r.HMin = Minima(sec, deitado, N, V, M, eletrodo, tChapa);
            r.L = r.A / (0.707 * h);                        // comprimento total do cordao (contorno)
            r.LNec = r.L * Math.Min(1, r.Ap);               // τ ~ 1/L: cordao que bastaria com esta perna
            r.Massa = h * h / 2 * r.L * 7850e-9;            // metal depositado (triangulo h×h/2), kg
            return r;
        }

        // W12-OUT: grupo de solda do U (UPN, U laminado, U dobrado) = contorno ABERTO (costas da alma, 2 mesas, pontas e faces
        // internas), espelho de placa_base._grupo_u do motor (docs/v3/W12-OUT-U.md). mm; por garganta unitária: comprimento len,
        // iu em torno do centroide do grupo na direção do plano, c até a fibra extrema e e = centroide do grupo → centroide do
        // perfil (só no eixo fraco; o xg sai das chapas retas). Falso: seção sem medidas, volta ao retângulo.
        static bool GrupoU(Secao sec, bool deitado, out double len, out double iu, out double c, out double e)
        {
            len = iu = c = e = 0;
            if (sec == null || sec.Forma != "U") return false;
            double h = sec.H * 1000, b = (sec.Bf > 0 ? sec.Bf : sec.H) * 1000, tw = sec.Tw * 1000, tf = (sec.Tf > 0 ? sec.Tf : sec.Tw) * 1000;
            if (h <= 0 || b <= 0 || tw <= 0 || tf <= 0 || 2 * tf >= h || tw >= b) return false;
            double u0 = -b / 2, ut = b / 2, ui = u0 + tw, h2 = h / 2;
            var pts = new List<double[]> { new[] { u0, -h2 }, new[] { ut, -h2 }, new[] { ut, -h2 + tf }, new[] { ui, -h2 + tf },
                                           new[] { ui, h2 - tf }, new[] { ut, h2 - tf }, new[] { ut, h2 }, new[] { u0, h2 } };
            int k = deitado ? 0 : 1;   // coordenada do plano da flexão: u (eixo fraco) ou v
            double cu = 0, cv = 0;
            var comp = new double[pts.Count];   // comprimento de cada lado (do vértice i ao i+1)
            for (int i = 0; i < pts.Count; i++)
            {
                double[] p = pts[i], q = pts[(i + 1) % pts.Count];
                double l = Math.Sqrt((q[0] - p[0]) * (q[0] - p[0]) + (q[1] - p[1]) * (q[1] - p[1]));
                comp[i] = l; len += l; cu += (p[0] + q[0]) / 2 * l; cv += (p[1] + q[1]) / 2 * l;
            }
            cu /= len; cv /= len;
            double cg = deitado ? cu : cv;
            for (int i = 0; i < pts.Count; i++)
            {
                double a = pts[i][k] - cg, z = pts[(i + 1) % pts.Count][k] - cg;
                iu += comp[i] * (a * a + a * z + z * z) / 3;   // ∫ q² ds de um segmento linear
            }
            foreach (double[] p in pts) c = Math.Max(c, Math.Abs(p[k] - cg));
            if (deitado)
            {
                double aw = h * tw, af = 2 * (b - tw) * tf;
                double xg = (aw * tw / 2 + af * (tw + (b - tw) / 2)) / (aw + af);
                e = Math.Abs(cu - (xg - b / 2));
            }
            return true;
        }

        static double CalcularSimples(Secao sec, bool deitado, double N, double V, double M, double h, double adm)
        {
            double d = (deitado ? (sec.Bf > 0 ? sec.Bf : sec.H) : sec.H) * 1000, b = (deitado ? sec.H : (sec.Bf > 0 ? sec.Bf : sec.H)) * 1000, c, iu, a;
            double mExtra = 0, lu, iuU, cU, eU;
            if (sec.Forma == "TC" || sec.Forma == "CIRC") { double raio = sec.H * 500; a = 1.414 * Math.PI * h * raio; iu = Math.PI * raio * raio * raio; c = raio; }
            else if (GrupoU(sec, deitado, out lu, out iuU, out cU, out eU)) { a = 0.707 * h * lu; iu = iuU; c = cU; mExtra = Math.Abs(N) * eU * 1e3; }
            else { a = 1.414 * h * (b + d); iu = d * d * (3 * b + d) / 6; c = d / 2; }
            double t1 = Math.Abs(V) * 1000 / a, t2 = Math.Abs(N) * 1000 / a + (Math.Abs(M) * 1e6 + mExtra) * c / (0.707 * h * iu);
            return Math.Sqrt(t1 * t1 + t2 * t2) / adm;
        }

        // Perna minima do filete pela parte MAIS FINA ligada (NBR 8800 Tabela 10 / AWS D1.1 Tab. 5.8): resfriamento e trinca na ZTA.
        //   t <= 6,35 -> 3 mm;  6,35 < t <= 12,5 -> 5 mm;  12,5 < t <= 19 -> 6 mm;  t > 19 -> 8 mm.  (t em mm; sem espessura -> 0)
        public static double PernaMinimaNorma(double t)
        {
            if (t <= 0 || t >= 1e8) return 0;
            return t <= 6.35 ? 3 : t <= 12.5 ? 5 : t <= 19 ? 6 : 8;
        }

        // Perna minima de tabela para os esforcos dados (0 = nem 25 mm passa).
        public static double Minima(Secao sec, bool deitado, double N, double V, double M, string eletrodo, double tChapa = 0)
        {
            double adm = Admissivel(eletrodo);
            double tw = (sec.Forma == "I" || sec.Forma == "U" ? Math.Min(sec.Tf, sec.Tw) : sec.Tw) * 1000;
            double hn = PernaMinimaNorma(Math.Min(tw > 0 ? tw : 1e9, tChapa > 0 ? tChapa : 1e9));
            foreach (double p in Pernas) if (p >= hn - 1e-9 && CalcularSimples(sec, deitado, N, V, M, p, adm) <= 1) return p;
            return 0;
        }

        // Por que a solda com perna h nao passa e o que fazer (so diagnostico; o calculo e o de Calcular).
        // Ordem: erro do perfil -> parede mais fina que a perna minima de norma (nenhuma perna cabe) -> menor perna
        // da tabela que cabe na parede e passa com o mesmo eletrodo -> eletrodo mais forte -> sem solucao na tabela.
        public static DiagSolda Diagnosticar(Secao sec, bool deitado, double N, double V, double M, double h, string eletrodo, double tChapa = 0)
        {
            DiagSolda d = new DiagSolda();
            string el = string.IsNullOrEmpty(eletrodo) ? "E70" : eletrodo.ToUpperInvariant().Replace("XX", "");
            ResSolda r = Calcular(sec, deitado, N, V, M, h, el, tChapa);
            if (r.Erro != "") { d.Porque = r.Erro; d.OQueFazer = "Confira o perfil do pilar."; d.SemSolucao = true; return d; }
            if (r.Passa) return d;
            double t = r.Espessura, hn = r.HMinNorma;
            if (t > 0 && hn > t + 1e-9)
            {
                d.Porque = "parede do perfil " + Num.Fmt(t, 2) + " mm < perna mínima de norma " + Num.Fmt(hn, 0) + " mm (NBR 8800 Tab. 10).";
                d.OQueFazer = "perna máx. ≈ " + Num.Fmt(t, 1) + " mm. Use perfil com parede ≥ " + Num.Fmt(hn, 0) +
                              " mm, chapa de reforço/luva no pé do pilar ou detalhe a ligação à parte.";
                d.PernaMax = t;
                d.SemSolucao = true;
                return d;
            }
            if (r.MaiorQueParede) d.Porque = "perna de " + Num.Fmt(h, 0) + " mm maior que a parede do perfil (" + Num.Fmt(t, 1) + " mm).";
            else if (r.AbaixoMinimo) d.Porque = "perna de " + Num.Fmt(h, 0) + " mm abaixo do mínimo de norma (" + Num.Fmt(hn, 0) + " mm pela peça mais fina).";
            else d.Porque = "τ = " + Num.Fmt(r.Tau, 0) + " MPa passa do admissível do " + el + "XX (" + Num.Fmt(r.TauAdm, 0) + " MPa) com perna de " + Num.Fmt(h, 0) + " mm.";
            int ie = Array.IndexOf(Eletrodos, el);
            if (ie < 0) ie = 1;
            for (int k = ie; k < Eletrodos.Length; k++)
            {
                foreach (double p in Pernas)
                {
                    if (p < hn - 1e-9 || (t > 0 && p > t + 1e-9)) continue;
                    if (CalcularSimples(sec, deitado, N, V, M, p, TauAdm[k]) <= 1)
                    {
                        d.Perna = p;
                        d.Eletrodo = Eletrodos[k];
                        d.OQueFazer = k == ie ? "use perna de " + Num.Fmt(p, 0) + " mm." : "nenhuma perna que cabe passa com " + el + "XX: use " + Eletrodos[k] + "XX com perna de " + Num.Fmt(p, 0) + " mm.";
                        return d;
                    }
                }
            }
            double pm = t > 0 ? Math.Min(t, Pernas[Pernas.Length - 1]) : Pernas[Pernas.Length - 1];
            d.PernaMax = pm;
            d.SemSolucao = true;
            d.OQueFazer = "nem a maior perna que cabe (" + Num.Fmt(pm, 0) + " mm) com " + Eletrodos[Eletrodos.Length - 1] + "XX passa: use pilar maior, nervuras (enrijecedores) no pé ou detalhe a ligação à parte.";
            return d;
        }
    }

    // Diagnostico da solda reprovada: Porque vazio = passa. Perna/Eletrodo > 0 = sugestao aplicavel com um clique.
    public class DiagSolda
    {
        public string Porque = "", OQueFazer = "", Eletrodo = "";
        public double Perna, PernaMax;
        public bool SemSolucao;
        public bool Passa { get { return Porque == ""; } }
    }
}
