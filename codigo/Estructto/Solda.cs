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
            double c, iu;
            if (sec.Forma == "TC" || sec.Forma == "CIRC")
            {
                double raio = sec.H * 1000 / 2;
                r.A = 1.414 * Math.PI * h * raio; iu = Math.PI * raio * raio * raio; c = raio;
                r.Grupo = "contorno circular Ø" + Num.Fmt(sec.H * 1000, 1) + " mm";
            }
            else
            {
                r.A = 1.414 * h * (b + d); iu = d * d * (3 * b + d) / 6; c = d / 2;
                r.Grupo = "contorno " + Num.Fmt(b, 0) + " × " + Num.Fmt(d, 0) + " mm";
            }
            r.I = 0.707 * h * iu;
            r.Tau1 = Math.Abs(V) * 1000 / r.A;
            r.Tau2 = Math.Abs(N) * 1000 / r.A + Math.Abs(M) * 1e6 * c / r.I;
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

        static double CalcularSimples(Secao sec, bool deitado, double N, double V, double M, double h, double adm)
        {
            double d = (deitado ? (sec.Bf > 0 ? sec.Bf : sec.H) : sec.H) * 1000, b = (deitado ? sec.H : (sec.Bf > 0 ? sec.Bf : sec.H)) * 1000, c, iu, a;
            if (sec.Forma == "TC" || sec.Forma == "CIRC") { double raio = sec.H * 500; a = 1.414 * Math.PI * h * raio; iu = Math.PI * raio * raio * raio; c = raio; }
            else { a = 1.414 * h * (b + d); iu = d * d * (3 * b + d) / 6; c = d / 2; }
            double t1 = Math.Abs(V) * 1000 / a, t2 = Math.Abs(N) * 1000 / a + Math.Abs(M) * 1e6 * c / (0.707 * h * iu);
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
    }
}
