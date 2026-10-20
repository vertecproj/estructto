// Dimensionamento automatico: perfis (o mais leve da familia que passa, iterando porque trocar o perfil muda os
// esforcos na estrutura hiperestatica) e soldas de filete nas pontas das barras.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Portico;

namespace NovoFtool
{
    public class LinhaSolda
    {
        [Browsable(false)] public Barra Obj { get; set; }
        [DisplayName("Barra")] public int Id { get; set; }
        [DisplayName("Ponta")] public string Ponta { get; set; }
        [DisplayName("Perfil")] public string Perfil { get; set; }
        [DisplayName("N (kN)")] public string N { get; set; }
        [DisplayName("V (kN)")] public string V { get; set; }
        [DisplayName("M (kN·m)")] public string M { get; set; }
        [DisplayName("Perna mínima (mm)")] public string HMin { get; set; }
        [DisplayName("Espessura do perfil (mm)")] public string Esp { get; set; }
        [DisplayName("Situação")] public string Sit { get; set; }
    }

    public partial class MainWindow
    {
        // ---------- perfis ----------

        // Grupo dimensionado junto: uma barra continua inteira, ou uma barra isolada.
        class GrupoDim
        {
            public List<Barra> Barras; public List<Secao> Opcoes; public int Min;   // Opcoes = familia do mais leve ao mais pesado; Min = menor indice permitido
            public string Nome { get { return Barras[0].Membro != 0 ? "B" + Barras[0].Membro : "barra " + Barras[0].Id; } }
            public int Indice { get { return Opcoes.FindIndex(s => s.Nome == Barras[0].Secao); } }
            public void Por(Secao s) { foreach (Barra b in Barras) b.Secao = s.Nome; }
        }

        // pior aproveitamento do grupo se ele tivesse o perfil s (esforcos atuais)
        double ApGrupo(GrupoDim g, Secao s)
        {
            double pior = 0;
            foreach (Barra b in g.Barras)
            {
                Verif v = VerificarAtual(b, s);
                if (!v.Aplicavel) return double.MaxValue;
                pior = Math.Max(pior, v.Ap);
                if (v.Aviso != "") pior = Math.Max(pior, 1.001 + v.Ap * 1e-3);   // parede esbelta / KL/r > 200: nao serve, mas ordena pelo menos ruim
            }
            return pior;
        }

        double PiorAp(IEnumerable<GrupoDim> gs)
        {
            double pior = 0;
            foreach (var g in gs) foreach (Barra b in g.Barras) { Verif v = VerificarAtual(b, Biblioteca.AcharSecao(b.Secao)); if (v.Aplicavel) pior = Math.Max(pior, v.Ap); }
            return pior;
        }

        void DimensionarPerfis(bool soSelecao)
        {
            if (!Vista.Resolvido) { Log("Calcule a estrutura antes de dimensionar (veja o painel Avisos)."); return; }
            var alvo = soSelecao ? Vista.Sel.OfType<Barra>().ToList() : M.Barras.ToList();
            if (alvo.Count == 0) { Log("Selecione as barras a dimensionar, ou use \"Dimensionar todas\"."); return; }
            // barra continua entra inteira, mesmo que so um trecho esteja selecionado
            foreach (int mm in alvo.Where(b => b.Membro != 0).Select(b => b.Membro).Distinct().ToList())
                foreach (Barra b in M.Trechos(mm)) if (!alvo.Contains(b)) alvo.Add(b);
            var inicial = M.Barras.ToDictionary(b => b, b => b.Secao);
            var grupos = new List<GrupoDim>();
            foreach (var gr in alvo.GroupBy(b => b.Membro == 0 ? -b.Id : b.Membro))
            {
                Secao atual = Biblioteca.AcharSecao(gr.First().Secao);
                if (atual == null || string.IsNullOrEmpty(atual.Familia)) continue;   // secao digitada: nao ha familia para escolher
                Material mat = Biblioteca.AcharMaterial(gr.First().Material);
                Func<Secao, double> massa = s => s.Massa > 0 ? s.Massa : (mat != null ? mat.Densidade * s.A : s.A);
                var g = new GrupoDim { Barras = gr.ToList(), Opcoes = Biblioteca.DaFamilia(atual.Familia, null).OrderBy(massa).ToList() };
                g.Por(atual);
                grupos.Add(g);
            }
            int iter = 0, subidas = 0;
            for (int volta = 0; volta < 40; volta++)
            {
                // 1) cada grupo: o perfil mais leve (acima do minimo travado) em que todos os trechos passam
                for (int k = 0; k < 8; k++, iter++)
                {
                    bool mudou = false;
                    foreach (var g in grupos)
                    {
                        Secao escolha = null, melhor = null; double apMelhor = double.MaxValue;
                        for (int i = g.Min; i < g.Opcoes.Count; i++)
                        {
                            double ap = ApGrupo(g, g.Opcoes[i]);
                            if (ap <= 1) { escolha = g.Opcoes[i]; break; }
                            if (ap < apMelhor) { apMelhor = ap; melhor = g.Opcoes[i]; }
                        }
                        Secao nova = escolha ?? melhor;   // nenhum passa: fica com o de menor aproveitamento, nao com qualquer um
                        if (nova != null && nova.Nome != g.Barras[0].Secao) { g.Por(nova); mudou = true; }
                    }
                    if (!mudou) break;
                    Resolver(false);                    // novos esforcos e deslocamentos com os perfis novos
                    if (!Vista.Resolvido) break;
                }
                if (!Vista.Resolvido) break;
                var falham = grupos.Where(g => ApGrupo(g, Biblioteca.AcharSecao(g.Barras[0].Secao)) > 1).ToList();
                if (falham.Count == 0) break;
                // 2) algum grupo nao passa nem com o maior perfil (ex.: flecha do topo depende do pilar de baixo):
                //    sobe um tamanho no grupo que mais reduz o pior aproveitamento por kg, trava esse minimo e repete
                double antesAp = PiorAp(grupos);
                GrupoDim escolhido = null; int idxEsc = -1; double melhorGanho = 0;
                foreach (var g in grupos)
                {
                    int i0 = g.Indice; if (i0 < 0) continue;
                    Secao velho = g.Opcoes[i0];
                    // proximo "tamanho": o mais leve que seja pelo menos 10% mais rigido (a lista por peso mistura diametros)
                    int i1 = g.Opcoes.FindIndex(i0 + 1, s => s.Ix > velho.Ix * 1.1 && s.Iy > velho.Iy * 1.1);
                    if (i1 < 0) continue;
                    Secao novo = g.Opcoes[i1];
                    g.Por(novo); Resolver(false);
                    double depoisAp = Vista.Resolvido ? PiorAp(grupos) : double.MaxValue;
                    g.Por(velho);
                    double kg = Math.Max(1e-6, g.Barras.Sum(b => Massa(b, novo.Nome) - Massa(b, velho.Nome)));
                    double ganho = (antesAp - depoisAp) / kg;
                    if (ganho > melhorGanho) { melhorGanho = ganho; escolhido = g; idxEsc = i1; }
                }
                Resolver(false);
                if (escolhido == null) break;   // nada melhora: sem solucao nas familias atuais
                escolhido.Min = idxEsc; escolhido.Por(escolhido.Opcoes[idxEsc]); subidas++;
                Resolver(false);
            }
            // 3) alivio: com tudo passando, tenta para cada grupo o perfil mais leve que ainda deixa TODOS passando
            //    (recalculando a estrutura a cada tentativa, porque a rigidez de um muda os outros)
            Func<bool> todosPassam = () => Vista.Resolvido && grupos.All(x => ApGrupo(x, Biblioteca.AcharSecao(x.Barras[0].Secao)) <= 1);
            if (todosPassam())
                for (int rep = 0; rep < 3; rep++)
                {
                    bool aliviou = false;
                    foreach (var g in grupos.OrderByDescending(x => x.Barras.Sum(b => Massa(b, b.Secao))))
                    {
                        int i0 = g.Indice; if (i0 <= 0) continue;
                        Secao velho = g.Opcoes[i0];
                        for (int i = 0; i < i0; i++)
                        {
                            if (ApGrupo(g, g.Opcoes[i]) > 1.5) continue;   // corte rapido: nem com os esforcos atuais chega perto
                            g.Por(g.Opcoes[i]); Resolver(false);
                            if (todosPassam()) { aliviou = true; break; }
                            g.Por(velho); Resolver(false);
                        }
                    }
                    if (!aliviou) break;
                }
            var semSolucao = grupos.Where(g => ApGrupo(g, Biblioteca.AcharSecao(g.Barras[0].Secao)) > 1).ToList();
            int trocadas = alvo.Count(b => inicial[b] != b.Secao);
            double antes = alvo.Sum(b => Massa(b, inicial[b])), depois = alvo.Sum(b => Massa(b, b.Secao));
            string msg = "Dimensionamento: " + trocadas + " de " + alvo.Count + " trecho(s) trocado(s) em " + iter + " iteração(ões)"
                + (subidas > 0 ? " (" + subidas + " aumento(s) em peças vizinhas para fechar a flecha/rigidez)" : "") + ". Peso das barras: "
                + Num.Fmt(antes, 0) + " → " + Num.Fmt(depois, 0) + " kg.";
            msg += semSolucao.Count > 0 ? " ⚠ Ainda não passa: " + string.Join(", ", semSolucao.Select(g => g.Nome)) + " — troque a família ou a geometria."
                                        : " ✓ Todas passam.";
            Confirmar(msg);
        }

        double Massa(Barra b, string secao)
        {
            Secao s = Biblioteca.AcharSecao(secao); Material mat = Biblioteca.AcharMaterial(b.Material);
            if (s == null) return 0;
            double kgm = s.Massa > 0 ? s.Massa : (mat != null ? mat.Densidade * s.A : 0);
            return kgm * M.Comp(b);
        }

        // ---------- soldas nas pontas ----------

        string Eletrodo { get { return string.IsNullOrEmpty(M.Eletrodo) ? "E70" : M.Eletrodo; } }

        void AtualizarSoldas()
        {
            if (cbEletrodo != null) { int ie = Array.IndexOf(Solda.Eletrodos, Eletrodo); if (ie >= 0 && cbEletrodo.SelectedIndex != ie) cbEletrodo.SelectedIndex = ie; }   // arquivo aberto com outro eletrodo
            var linhas = new List<LinhaSolda>();
            if (Vista.Resolvido)
                foreach (Barra b in M.Barras)
                {
                    Secao s = Biblioteca.AcharSecao(b.Secao);
                    for (int ponta = 0; ponta < 2; ponta++)
                    {
                        if ((ponta == 0 && b.RotulaInicio) || (ponta == 1 && b.RotulaFim)) continue;   // rotula: ligacao articulada, nao e solda de momento
                        int noId = ponta == 0 ? b.N1 : b.N2;
                        // pior combinacao (envoltoria) ou resultado atual
                        var lista = Vista.Envoltoria != null && Vista.Envoltoria.ContainsKey(b) ? Vista.Envoltoria[b] : new List<ResBarra> { b.Res };
                        double hPior = -1; double nP = 0, vP = 0, mP = 0;
                        foreach (ResBarra r in lista)
                        {
                            if (r == null) continue;
                            double x = ponta == 0 ? 0 : r.L, n = r.N(x), v = r.Q(x), m = r.M(x);
                            double h = Solda.Minima(s, b.EixoFraco, n, v, m, Eletrodo);
                            double chave = h == 0 ? 1e9 : h;
                            if (chave > hPior) { hPior = chave; nP = n; vP = v; mP = m; }
                        }
                        if (hPior < 0) continue;
                        var rs = Solda.Calcular(s, b.EixoFraco, nP, vP, mP, hPior >= 1e9 ? 25 : hPior, Eletrodo);
                        string sit = hPior >= 1e9 ? "nem 25 mm passa" : rs.Espessura > 0 && hPior > rs.Espessura ? "perna maior que a parede do perfil" : "ok";
                        linhas.Add(new LinhaSolda
                        {
                            Obj = b, Id = b.Id, Ponta = (ponta == 0 ? "início" : "fim") + " (nó " + noId + ")", Perfil = b.Secao,
                            N = Num.Fmt(nP, 2), V = Num.Fmt(vP, 2), M = Num.Fmt(mP, 2),
                            HMin = hPior >= 1e9 ? "> 25" : Num.Fmt(hPior, 0), Esp = rs.Espessura > 0 ? Num.Fmt(rs.Espessura, 1) : "—", Sit = sit
                        });
                    }
                }
            TabSoldas.ItemsSource = linhas;
            TabSoldas.AutoGeneratingColumn -= ColunaSolda; TabSoldas.AutoGeneratingColumn += ColunaSolda;
            AbaSoldas.Header = "Soldas (" + Eletrodo + "XX)";
            AbaSoldas.ToolTip = "Perna mínima de solda de filete nas pontas sem rótula de cada barra, calculada para o eletrodo " + Eletrodo + "XX.";
        }

        static void ColunaSolda(object s, DataGridAutoGeneratingColumnEventArgs e)
        {
            var pd = e.PropertyDescriptor as PropertyDescriptor;
            if (pd == null) return;
            if (!pd.IsBrowsable) { e.Cancel = true; return; }
            e.Column.Header = pd.DisplayName;
        }
    }
}
