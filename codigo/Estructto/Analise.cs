// Analise por casos de carga e combinacoes (superposicao linear).
// Cada caso e resolvido uma vez numa copia do modelo; uma combinacao e a soma dos resultados dos casos
// multiplicados pelos fatores (deslocamentos, esforcos de extremidade e cargas distribuidas sao lineares nas cargas).
using System;
using System.Collections.Generic;
using System.Linq;
using Portico;

namespace NovoFtool
{
    public class ResultadosCasos
    {
        public string Erro = "";
        public List<Dictionary<int, ResBarra>> Barras = new List<Dictionary<int, ResBarra>>();   // por caso: Id da barra -> resultado
        public List<Dictionary<int, double[]>> Nos = new List<Dictionary<int, double[]>>();      // por caso: Id do no -> ux, uy, giro, Rx, Ry, Rm
        // carga giratoria: os mesmos casos com o equipamento na posicao oposta (180 graus); vazio se nao ha carga giratoria
        public List<Dictionary<int, ResBarra>> Barras180 = new List<Dictionary<int, ResBarra>>();
        public List<Dictionary<int, double[]>> Nos180 = new List<Dictionary<int, double[]>>();
        public bool Rotativa { get { return Barras180.Count > 0; } }
    }

    public static class Analise
    {
        public static ResultadosCasos ResolverCasos(Modelo m)
        {
            var r = new ResultadosCasos();
            m.GarantirCasos();
            for (int i = 0; i < m.Casos.Count; i++)
            {
                Modelo c = Modelo.DeXml(m.ParaXml());
                c.AtivarCaso(i);
                c.PesoProprio = m.PesoProprio && m.Casos[i].PesoProprio;
                Resultado res = Solver.Resolver(c);
                if (!string.IsNullOrEmpty(res.Erro))
                {
                    r.Erro = m.Casos.Count > 1 ? "Caso \"" + m.Casos[i].Nome + "\": " + res.Erro : res.Erro;
                    return r;
                }
                r.Barras.Add(c.Barras.ToDictionary(b => b.Id, b => b.Res));
                r.Nos.Add(c.Nos.ToDictionary(n => n.Id, n => n.Res));
                if (m.TemRotativa)
                {
                    Modelo o = c.PosicaoOposta();
                    o.PesoProprio = c.PesoProprio;
                    Resultado ro = Solver.Resolver(o);
                    if (!string.IsNullOrEmpty(ro.Erro)) { r.Erro = "Posição 180° do equipamento: " + ro.Erro; return r; }
                    r.Barras180.Add(o.Barras.ToDictionary(b => b.Id, b => b.Res));
                    r.Nos180.Add(o.Nos.ToDictionary(n => n.Id, n => n.Res));
                }
            }
            return r;
        }

        public static ResBarra Combinar(IList<ResBarra> rs, IList<double> f)
        {
            ResBarra a = rs[0];
            var c = new ResBarra { L = a.L, C = a.C, S = a.S, EA = a.EA, EI = a.EI, GAs = a.GAs };
            for (int k = 0; k < rs.Count; k++)
            {
                double fk = k < f.Count ? f[k] : 0;
                if (fk == 0) continue;
                ResBarra r = rs[k];
                c.P1 += fk * r.P1; c.P2 += fk * r.P2; c.Q1 += fk * r.Q1; c.Q2 += fk * r.Q2;
                for (int i = 0; i < 6; i++) { c.U[i] += fk * r.U[i]; c.F[i] += fk * r.F[i]; }
            }
            c.Fechar();
            return c;
        }

        public static double[] CombinarNo(IList<double[]> rs, IList<double> f)
        {
            var c = new double[6];
            for (int k = 0; k < rs.Count; k++)
            {
                double fk = k < f.Count ? f[k] : 0;
                if (fk == 0 || rs[k] == null) continue;
                for (int i = 0; i < 6; i++) c[i] += fk * rs[k][i];
            }
            return c;
        }

        // Fatores de um caso isolado (1 no caso, 0 nos demais).
        public static List<double> Unitario(int n, int caso) { var f = Enumerable.Repeat(0.0, n).ToList(); f[caso] = 1; return f; }

        // Combinacoes normais da NBR 8681 / NBR 8800 (estruturas de aco, edificacoes):
        //   ELU: gama_g = 1,4 (desfavoravel) ou 1,0 (favoravel, so com vento); sobrecarga gama_q = 1,5; vento 1,4;
        //        psi0 = 0,5 (sobrecarga, locais sem predominancia de equipamentos) e 0,6 (vento).
        //   ELS: quase permanente G + psi2.Q (psi2 = 0,3 sobrecarga; 0 vento) e frequentes G + psi1.Qprincipal + psi2.Qdemais
        //        (psi1 = 0,4 sobrecarga; 0,3 vento).
        // Casos do tipo E (excepcional) nao entram nas combinacoes normais.
        // Combinacoes do modelo: as da NBR 8400 se o vento do modelo e da 8400, senao as da NBR 8681/8800.
        public static List<Combinacao> Gerar(Modelo m)
        {
            return m.Vento != null && m.Vento.Norma == "8400" ? GerarNbr8400(m.Casos, m.Vento) : GerarNbr(m.Casos);
        }

        public static bool VentoMaximo(CasoCarga c) { return c.Tipo == "V" && c.Nome.StartsWith("Vento máximo"); }

        // Combinacoes da NBR 8400:1984 (5.6), metodo das tensoes admissiveis: cargas sem majoracao por gama, so com Mx (Tabela 10).
        //   Caso I:   Mx.(SG + psi.SL)                      (servico normal sem vento)
        //   Caso II:  Mx.(SG + psi.SL) + SW                 (servico normal com vento limite de servico, uma direcao por vez)
        //   Caso III: SG + SWmax                            (fora de servico, vento maximo, sem carga)
        // Efeitos horizontais SH, choques e temperatura nao sao gerados (lance como casos proprios e combine a mao).
        // O programa verifica todas com o mesmo coeficiente de seguranca do modelo; a 8400 usa tensoes admissiveis diferentes
        // por caso (sigma_e/1,5, /1,33 e /1,1), o que o programa nao distingue. Tipo "ELU" = verificacao de tensoes; "ELS" = flecha.
        public static List<Combinacao> GerarNbr8400(List<CasoCarga> casos, ParamVento p)
        {
            var lista = new List<Combinacao>();
            int n = casos.Count;
            double mx = Vento8400.MxGrupo(p.Grupo), psi = p.Psi;
            var g = Enumerable.Range(0, n).Where(i => casos[i].Tipo == "G").ToList();
            var q = Enumerable.Range(0, n).Where(i => casos[i].Tipo == "Q").ToList();
            var serv = Enumerable.Range(0, n).Where(i => casos[i].Tipo == "V" && !VentoMaximo(casos[i])).ToList();
            var maxi = Enumerable.Range(0, n).Where(i => VentoMaximo(casos[i])).ToList();
            Combinacao Nova(string nome, string tipo) { var c = new Combinacao { Nome = nome, Tipo = tipo }; for (int i = 0; i < n; i++) c.Fatores.Add(0); return c; }
            Combinacao Carga(string nome, string tipo, double fg, double fq)
            {
                var c = Nova(nome, tipo);
                foreach (int i in g) c.Fatores[i] = Math.Round(fg, 4);
                foreach (int i in q) c.Fatores[i] = Math.Round(fq, 4);
                return c;
            }
            string mxTxt = Num.Fmt(mx, 2);
            lista.Add(Carga("ELU 1 - Caso I: " + mxTxt + "·(G + " + Num.Fmt(psi, 2) + "·Q)", "ELU", mx, mx * psi));
            int k = 2;
            foreach (int w in serv)
            {
                var c = Carga("ELU " + k++ + " - Caso II: " + mxTxt + "·(G + " + Num.Fmt(psi, 2) + "·Q) + " + casos[w].Nome, "ELU", mx, mx * psi);
                c.Fatores[w] = 1; lista.Add(c);
            }
            foreach (int w in maxi)
            {
                var c = Carga("ELU " + k++ + " - Caso III: G + " + casos[w].Nome, "ELU", 1, 0);
                c.Fatores[w] = 1; lista.Add(c);
            }
            lista.Add(Carga("ELS 1 - G + Q (flecha)", "ELS", 1, 1));
            int f = 2;
            foreach (int w in serv)
            {
                var c = Carga("ELS " + f++ + " - G + Q + " + casos[w].Nome, "ELS", 1, 1);
                c.Fatores[w] = 1; lista.Add(c);
            }
            return lista;
        }

        public static List<Combinacao> GerarNbr(List<CasoCarga> casos)
        {
            var lista = new List<Combinacao>();
            int n = casos.Count;
            Func<string, double> gama = t => t == "V" ? 1.4 : 1.5;
            Func<string, double> psi0 = t => t == "V" ? 0.6 : 0.5;
            Func<string, double> psi1 = t => t == "V" ? 0.3 : 0.4;
            Func<string, double> psi2 = t => t == "V" ? 0.0 : 0.3;
            var variaveis = Enumerable.Range(0, n).Where(i => casos[i].Tipo == "Q" || casos[i].Tipo == "V").ToList();

            Combinacao Nova(string nome, string tipo) { var c = new Combinacao { Nome = nome, Tipo = tipo }; for (int i = 0; i < n; i++) c.Fatores.Add(0); return c; }

            if (variaveis.Count == 0)
            {
                var c = Nova("ELU 1 - 1,4G", "ELU");
                for (int i = 0; i < n; i++) if (casos[i].Tipo == "G") c.Fatores[i] = 1.4;
                lista.Add(c);
            }
            // Casos de vento sao direcoes alternativas (ex.: vento da esquerda e da direita): nunca entram juntos.
            // Com a sobrecarga principal, gera uma combinacao para cada direcao de vento secundario.
            var ventos = variaveis.Where(i => casos[i].Tipo == "V").ToList();
            int k = 1;
            foreach (int p in variaveis)
            {
                bool pVento = casos[p].Tipo == "V";
                var opcoesVento = pVento || ventos.Count == 0 ? new List<int> { -1 } : ventos;
                foreach (int w in opcoesVento)
                {
                    var c = Nova("ELU " + k++ + " - " + casos[p].Nome + " principal" + (w >= 0 ? " + " + casos[w].Nome : ""), "ELU");
                    for (int i = 0; i < n; i++)
                    {
                        if (casos[i].Tipo == "G") c.Fatores[i] = 1.4;
                        else if (i == p) c.Fatores[i] = gama(casos[i].Tipo);
                        else if (casos[i].Tipo == "V" && (pVento || i != w)) c.Fatores[i] = 0;
                        else if (variaveis.Contains(i)) c.Fatores[i] = Math.Round(gama(casos[i].Tipo) * psi0(casos[i].Tipo), 3);
                    }
                    lista.Add(c);
                }
            }
            foreach (int p in variaveis.Where(i => casos[i].Tipo == "V"))
            {
                // vento de succao com o peso proprio favoravel
                var c = Nova("ELU " + k++ + " - 1,0G + " + casos[p].Nome, "ELU");
                for (int i = 0; i < n; i++) if (casos[i].Tipo == "G") c.Fatores[i] = 1.0; else if (i == p) c.Fatores[i] = 1.4;
                lista.Add(c);
            }
            var qp = Nova("ELS QP - quase permanente", "ELS");
            for (int i = 0; i < n; i++) qp.Fatores[i] = casos[i].Tipo == "G" ? 1 : variaveis.Contains(i) ? psi2(casos[i].Tipo) : 0;
            lista.Add(qp);
            int f = 1;
            foreach (int p in variaveis)
            {
                var c = Nova("ELS F" + f++ + " - frequente, " + casos[p].Nome, "ELS");
                for (int i = 0; i < n; i++)
                    c.Fatores[i] = casos[i].Tipo == "G" ? 1 : i == p ? psi1(casos[i].Tipo) : variaveis.Contains(i) ? psi2(casos[i].Tipo) : 0;
                lista.Add(c);
            }
            return lista;
        }
    }
}
