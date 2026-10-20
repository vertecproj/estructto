// Casos de carga, combinacoes e envoltorias na janela principal + dialogo "Casos e combinacoes".
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Portico;
using F = Fluent;

namespace NovoFtool
{
    public partial class MainWindow
    {
        ResultadosCasos resCasos;
        string resSel = "ativo";     // ativo | caso:i | comb:i | env:ELU | env:ELS | py:<combinacao> | pyenv:ELU | pyenv:ELS
        string motorXml;             // modelo (XML) enviado ao motor Python no ultimo calculo
        bool listasOcupadas;
        F.ComboBox cbCaso, cbResultado;

        // ---------- qual resultado esta na tela ----------

        List<Combinacao> CombinacoesDaEnvoltoria()
        {
            string tipo = resSel.StartsWith("env:") ? resSel.Substring(4) : "";
            return M.Combinacoes.Where(c => c.Tipo == tipo).ToList();
        }

        string NomeResultado()
        {
            int i;
            if (resSel.StartsWith("caso:") && int.TryParse(resSel.Substring(5), out i) && i < M.Casos.Count) return "caso " + M.Casos[i].Nome;
            if (resSel.StartsWith("comb:") && int.TryParse(resSel.Substring(5), out i) && i < M.Combinacoes.Count) return "combinação " + M.Combinacoes[i].Nome;
            if (resSel.StartsWith("env:")) return "envoltória " + resSel.Substring(4);
            if (resSel.StartsWith("py:")) return "motor Python, combinação " + resSel.Substring(3);
            if (resSel.StartsWith("pyenv:")) return "motor Python, envoltória " + resSel.Substring(6);
            return "caso " + (M.Casos.Count > 0 ? M.Casos[M.CasoAtivo].Nome : "");
        }

        List<double> FatoresAtuais()
        {
            int n = M.Casos.Count, i;
            if (resSel.StartsWith("caso:") && int.TryParse(resSel.Substring(5), out i) && i < n) return Analise.Unitario(n, i);
            if (resSel.StartsWith("comb:") && int.TryParse(resSel.Substring(5), out i) && i < M.Combinacoes.Count) return M.Combinacoes[i].Fatores;
            return Analise.Unitario(n, M.CasoAtivo);
        }

        List<ResBarra> PorCaso(Barra b) { return resCasos.Barras.Select(d => d[b.Id]).ToList(); }
        List<double[]> PorCaso(No n) { return resCasos.Nos.Select(d => d[n.Id]).ToList(); }

        // Poe em b.Res / n.Res o resultado escolhido; na envoltoria guarda tambem a lista de todas as combinacoes.
        void AplicarResultado()
        {
            if (resCasos == null) return;
            if (resSel.StartsWith("py") && AplicarMotor()) return;
            if (resSel.StartsWith("env:") && CombinacoesDaEnvoltoria().Count == 0)
            {
                Log("Não há combinações " + resSel.Substring(4) + ": crie em Cargas → Casos e combinações. Mostrando o caso ativo.");
                resSel = "ativo";
            }
            bool rot = resCasos.Rotativa;
            if (resSel.StartsWith("env:") || rot)
            {
                // envoltoria das combinacoes; com carga giratoria, cada resultado nas posicoes 0 e 180 graus
                var itens = resSel.StartsWith("env:") ? CombinacoesDaEnvoltoria().Select(c => (c.Nome, (IList<double>)c.Fatores)).ToList()
                                                      : new List<(string, IList<double>)> { (NomeResultado(), FatoresAtuais()) };
                var nomes = new List<string>(); var fats = new List<IList<double>>(); var pos = new List<bool>();
                foreach (var it in itens)
                {
                    nomes.Add(rot ? it.Item1 + " (0°)" : it.Item1); fats.Add(it.Item2); pos.Add(false);
                    if (rot) { nomes.Add(it.Item1 + " (180°)"); fats.Add(it.Item2); pos.Add(true); }
                }
                Vista.Envoltoria = M.Barras.ToDictionary(b => b, b => { var pc = PorCaso(b); var p180 = rot ? resCasos.Barras180.Select(d => d[b.Id]).ToList() : null; return fats.Select((f, i) => Analise.Combinar(pos[i] ? p180 : pc, f)).ToList(); });
                Vista.EnvNos = M.Nos.ToDictionary(n => n, n => { var pc = PorCaso(n); var p180 = rot ? resCasos.Nos180.Select(d => d[n.Id]).ToList() : null; return fats.Select((f, i) => Analise.CombinarNo(pos[i] ? p180 : pc, f)).ToList(); });
                Vista.NomesEnv = nomes;
                foreach (Barra b in M.Barras) b.Res = Vista.Envoltoria[b][0];
                foreach (No n in M.Nos) n.Res = Vista.EnvNos[n][0];
            }
            else
            {
                var f = FatoresAtuais();
                Vista.Envoltoria = null; Vista.EnvNos = null; Vista.NomesEnv = null;
                foreach (Barra b in M.Barras) b.Res = Analise.Combinar(PorCaso(b), f);
                foreach (No n in M.Nos) n.Res = Analise.CombinarNo(PorCaso(n), f);
            }
        }

        // ---------- resultado do motor Python (docs/UNIFICACAO-MOTOR.md) ----------
        // "py:<combinacao>" ou "pyenv:ELU|ELS": b.Res / n.Res e a envoltoria vem do MotorResultado (nao do Nucleo.cs).
        // Falso se nao ha resultado do motor valido para o modelo da tela (volta para o caso ativo).
        bool AplicarMotor()
        {
            List<string> nomes = null;
            if (MotorValido())
            {
                if (resSel.StartsWith("py:")) nomes = ResMotor.Combinacoes.Where(c => c == resSel.Substring(3)).ToList();
                else { string est = resSel.Substring(6); nomes = ResMotor.Combinacoes.Where(c => (ResMotor.EstadoDe.TryGetValue(c, out var e) ? e : "").StartsWith(est)).ToList(); }
            }
            if (nomes == null || nomes.Count == 0) { resSel = "ativo"; return false; }
            var env = M.Barras.ToDictionary(b => b, b => nomes.Select(c => MotorModelo.ParaResBarra(M, b, PorCaso(b)[0], ResMotor.PorCombinacao[c])).ToList());
            var envNos = M.Nos.ToDictionary(n => n, n => nomes.Select(c => (double[])ResMotor.PorCombinacao[c].Nos[n.Id].Clone()).ToList());
            if (nomes.Count == 1) { Vista.Envoltoria = null; Vista.EnvNos = null; Vista.NomesEnv = null; }
            else { Vista.Envoltoria = env; Vista.EnvNos = envNos; Vista.NomesEnv = nomes; }
            foreach (Barra b in M.Barras) b.Res = env[b][0];
            foreach (No n in M.Nos) n.Res = envNos[n][0];
            return true;
        }

        // o resultado do motor so vale para o modelo que foi enviado (qualquer edicao o descarta)
        bool MotorValido()
        {
            if (ResMotor != null && motorXml != M.ParaXml()) { ResMotor = null; Log("Modelo alterado: o resultado do motor Python foi descartado. Calcule de novo (F5)."); }
            return ResMotor != null;
        }

        // Verificacao NBR 8800 do motor Python (pior linha da barra), quando a tela mostra o resultado do motor
        Verif VerificarMotor(Barra b)
        {
            if (!resSel.StartsWith("py") || ResMotor == null) return null;
            var vs = ResMotor.Verificacao.Where(x => x.Barra == b.Id).ToList();
            if (vs.Count == 0) return null;
            var p = vs.OrderByDescending(x => x.Taxa ?? double.MaxValue).First();
            if (!p.Taxa.HasValue) return new VerifMotor { Aplicavel = false, Motivo = "não verificada pelo motor (" + p.Criterio + ")" };
            return new VerifMotor { Aplicavel = true, Ap = p.Taxa.Value, Critico = p.Criterio + " · " + p.Norma + " " + p.Item + " · " + p.Combinacao };
        }

        IEnumerable<ResBarra> ResultadosDe(Barra b)
        {
            if (Vista.Envoltoria != null && Vista.Envoltoria.ContainsKey(b)) return Vista.Envoltoria[b];
            return b.Res != null ? new[] { b.Res } : new ResBarra[0];
        }

        IEnumerable<double[]> ResultadosDe(No n)
        {
            if (Vista.EnvNos != null && Vista.EnvNos.ContainsKey(n)) return Vista.EnvNos[n];
            return n.Res != null ? new[] { n.Res } : new double[0][];
        }

        // "min / max" de N (0), Q (1) ou M (2) sobre o resultado da tela (inclui todas as combinacoes da envoltoria)
        void Extremos(Barra b, int tipo, out double mn, out double mx)
        {
            mn = double.MaxValue; mx = double.MinValue;
            foreach (ResBarra r in ResultadosDe(b)) { double a, c; r.Extremos(tipo, out a, out c); mn = Math.Min(mn, a); mx = Math.Max(mx, c); }
            if (mn > mx) { mn = mx = 0; }
        }

        string FaixaBarra(Barra b, int tipo) { double a, c; Extremos(b, tipo, out a, out c); return Num.Fmt(a, 2) + " / " + Num.Fmt(c, 2); }

        double Flecha(Barra b) { double f = 0; foreach (ResBarra r in ResultadosDe(b)) { double x = r.FlechaMax(); if (Math.Abs(x) > Math.Abs(f)) f = x; } return f; }

        string FaixaNo(No n, int k, double esc, int casas)
        {
            var vs = ResultadosDe(n).Select(r => r[k] * esc).ToList();
            if (vs.Count == 0) return "";
            if (vs.Count == 1) return k == 2 ? vs[0].ToString("0.000000") : Num.Fmt(vs[0], casas);
            return k == 2 ? vs.Min().ToString("0.000000") + " / " + vs.Max().ToString("0.000000") : Num.Fmt(vs.Min(), casas) + " / " + Num.Fmt(vs.Max(), casas);
        }

        // Verificacao do resultado da tela; na envoltoria, a pior combinacao (com o nome dela em "Critico").
        Verif VerificarAtual(Barra b, Secao sec)
        {
            if (Vista.Envoltoria == null || !Vista.Envoltoria.ContainsKey(b)) return Verificador.Barra(M, b, sec);
            ResBarra orig = b.Res; Verif pior = null; int ip = 0, i = 0;
            foreach (ResBarra r in Vista.Envoltoria[b])
            {
                b.Res = r;
                Verif v = Verificador.Barra(M, b, sec);
                if (pior == null || (v.Aplicavel && (!pior.Aplicavel || v.Ap > pior.Ap))) { pior = v; ip = i; }
                i++;
            }
            b.Res = orig;
            // o aviso de inversao so vale se NENHUMA combinacao/posicao comprime a barra
            if (pior != null && pior.AvisoInversao != "" && Vista.Envoltoria[b].Any(r => { double mn, mx; r.Extremos(0, out mn, out mx); return mn < -1e-6; }))
                pior.AvisoInversao = "";
            if (pior != null && pior.Aplicavel && Vista.NomesEnv != null) pior.Critico += " (" + Vista.NomesEnv[ip] + ")";
            return pior ?? Verificador.Barra(M, b, sec);
        }

        // Na envoltoria, as janelas de verificacao e de placa mostram a combinacao critica: coloca-a em b.Res/n.Res.
        string FixarCombinacaoCritica(Func<int, double> nota)
        {
            if (Vista.Envoltoria == null || Vista.NomesEnv == null) return null;
            int melhor = 0; double pior = double.MinValue;
            for (int i = 0; i < Vista.NomesEnv.Count; i++) { double v = nota(i); if (v > pior) { pior = v; melhor = i; } }
            foreach (Barra b in M.Barras) b.Res = Vista.Envoltoria[b][melhor];
            foreach (No n in M.Nos) n.Res = Vista.EnvNos[n][melhor];
            return Vista.NomesEnv[melhor];
        }

        void RestaurarResultado() { AplicarResultado(); Vista.InvalidateVisual(); }

        // ---------- listas da faixa ----------

        void MontarCasosNaFaixa(F.RibbonTabItem abaCargas, F.RibbonTabItem abaRes)
        {
            cbCaso = new F.ComboBox { Header = "Caso ativo", IsEditable = false, Width = 250, ToolTip = new F.ScreenTip { Title = "Caso de carga ativo", Text = "As cargas aplicadas e mostradas no desenho são as deste caso. Troque para lançar as cargas de outro caso (G, Q, vento...).", Width = 300 } };
            cbCaso.SelectionChanged += (s, e) =>
            {
                if (listasOcupadas || cbCaso.SelectedIndex < 0 || cbCaso.SelectedIndex == M.CasoAtivo) return;
                M.AtivarCaso(cbCaso.SelectedIndex);
                Confirmar("Caso ativo: " + M.Casos[M.CasoAtivo].Nome + ". As cargas lançadas agora vão para este caso.");
            };
            var g = new F.RibbonGroupBox { Header = "Casos de carga" };
            g.Items.Add(cbCaso);
            g.Items.Add(Botao("tabela", "Casos e combinações", AbrirCasos, "Cria casos (permanente, sobrecarga, vento...) e combinações ELU/ELS, ou gera as combinações da NBR 8681/8800.", "CASOS"));
            abaCargas.Groups.Insert(0, g);

            cbResultado = new F.ComboBox { Header = "Mostrar", IsEditable = false, Width = 290, ToolTip = new F.ScreenTip { Title = "Resultado mostrado", Text = "Um caso isolado, uma combinação, ou a envoltória (mínimos e máximos) de todas as combinações ELU ou ELS. Diagramas, tabelas, verificação e relatório usam esta escolha.", Width = 300 } };
            cbResultado.SelectionChanged += (s, e) =>
            {
                if (listasOcupadas || !(cbResultado.SelectedItem is ComboBoxItem it)) return;
                resSel = (string)it.Tag;
                Atualizar();
                Log("Mostrando: " + NomeResultado() + ".");
            };
            var g2 = new F.RibbonGroupBox { Header = "Resultado" };
            g2.Items.Add(cbResultado);
            abaRes.Groups.Insert(0, g2);
        }

        void AtualizarListasCasos()
        {
            if (cbCaso == null) return;
            listasOcupadas = true;
            M.GarantirCasos();
            cbCaso.Items.Clear();
            foreach (CasoCarga c in M.Casos) cbCaso.Items.Add(new ComboBoxItem { Content = c.Nome + "  [" + c.Tipo + "]", ToolTip = "Ativa o caso de carga \"" + c.Nome + "\" para exibir e lançar cargas nele." });
            cbCaso.SelectedIndex = M.CasoAtivo;
            cbResultado.Items.Clear();
            void Add(string txt, string tag, string dica) { cbResultado.Items.Add(new ComboBoxItem { Content = txt, Tag = tag, ToolTip = dica }); }
            Add("Caso ativo (" + M.Casos[M.CasoAtivo].Nome + ")", "ativo", "Exibe os esforços, deslocamentos e verificações apenas do caso de carga ativo.");
            if (M.Casos.Count > 1) for (int i = 0; i < M.Casos.Count; i++) Add("Caso: " + M.Casos[i].Nome, "caso:" + i, "Exibe os resultados isolados do caso \"" + M.Casos[i].Nome + "\".");
            for (int i = 0; i < M.Combinacoes.Count; i++) Add(M.Combinacoes[i].Tipo + ": " + M.Combinacoes[i].Nome, "comb:" + i, "Exibe os resultados da combinação \"" + M.Combinacoes[i].Nome + "\" (" + M.Combinacoes[i].Tipo + ").");
            if (M.Combinacoes.Any(c => c.Tipo == "ELU")) Add("Envoltória ELU (mín./máx.)", "env:ELU", "Exibe a envoltória de valores mínimos e máximos entre todas as combinações de Estado Limite Último (ELU).");
            if (M.Combinacoes.Any(c => c.Tipo == "ELS")) Add("Envoltória ELS (mín./máx.)", "env:ELS", "Exibe a envoltória de valores mínimos e máximos entre todas as combinações de Estado Limite de Serviço (ELS).");
            if (MotorValido())
            {
                string Est(string c) { return ResMotor.EstadoDe.TryGetValue(c, out var e) ? e : ""; }
                if (ResMotor.Combinacoes.Any(c => Est(c).StartsWith("ELU"))) Add("Python: envoltória ELU (NBR 8681/8800)", "pyenv:ELU", "Resultados do motor Python: envoltória das combinações ELU da NBR 8681 e verificação das barras pela NBR 8800.");
                if (ResMotor.Combinacoes.Any(c => Est(c).StartsWith("ELS"))) Add("Python: envoltória ELS", "pyenv:ELS", "Resultados do motor Python: envoltória das combinações de serviço da NBR 8681.");
                foreach (string c in ResMotor.Combinacoes) Add("Python " + Est(c) + ": " + c, "py:" + c, "Resultados do motor Python na combinação \"" + c + "\"; a verificação NBR 8800 mostra a combinação crítica de cada barra.");
            }
            var sel = cbResultado.Items.Cast<ComboBoxItem>().FirstOrDefault(x => (string)x.Tag == resSel);
            if (sel == null) { resSel = "ativo"; sel = (ComboBoxItem)cbResultado.Items[0]; }
            cbResultado.SelectedItem = sel;
            listasOcupadas = false;
        }

        void AbrirCasos()
        {
            var j = new JanelaCasos(M) { Owner = this };
            if (j.ShowDialog() != true || j.Resultado == null) return;
            var m = j.Resultado;
            foreach (Barra b in m.Barras) b.Dono = m;
            Vista.M = m; Vista.Sel.Clear();
            Confirmar(Pl.N(m.Casos.Count, "caso", "casos") + " e " + Pl.N(m.Combinacoes.Count, "combinação", "combinações") + ".");
            SelecaoMudou();
        }

        internal void EscolherResultado(string tag)
        {
            resSel = tag;
            Atualizar();
            Log("Mostrando: " + NomeResultado() + ".");
        }
    }

    // ---------- dialogo: casos de carga e combinacoes ----------

    public class JanelaCasos : JanelaEscura
    {
        public Modelo Resultado;
        readonly Modelo m;
        readonly StackPanel corpo = new StackPanel { Margin = new Thickness(18, 14, 18, 8) };
        readonly TextBlock erro = T("", 11, BVermelho);
        readonly List<(TextBox nome, ComboBox tipo, CheckBox pp)> lc = new List<(TextBox, ComboBox, CheckBox)>();
        readonly List<(TextBox nome, ComboBox tipo, List<TextBox> f)> lk = new List<(TextBox, ComboBox, List<TextBox>)>();
        static readonly string[] TiposCaso = { "G", "Q", "V", "E" };
        static readonly string[] NomesTipo = { "G - permanente", "Q - variável (sobrecarga)", "V - vento", "E - excepcional" };

        public JanelaCasos(Modelo origem)
        {
            m = Modelo.DeXml(origem.ParaXml());
            Title = "Casos de carga e combinações"; Width = 980; Height = 640; MinWidth = 760; MinHeight = 420;
            var raiz = new DockPanel();
            var rod = new DockPanel { Margin = new Thickness(18, 6, 18, 12) };
            var bts = new StackPanel { Orientation = Orientation.Horizontal };
            var btOk = B("OK", Ok, true); btOk.ToolTip = "Aplica os casos de carga e as combinações definidos nesta janela ao modelo.";
            var btCanc = B("Cancelar", Close); btCanc.ToolTip = "Fecha a janela sem alterar os casos nem as combinações do modelo (Esc).";
            bts.Children.Add(btOk); bts.Children.Add(btCanc);
            DockPanel.SetDock(bts, Dock.Right); rod.Children.Add(bts);
            rod.Children.Add(erro);
            DockPanel.SetDock(rod, Dock.Bottom); raiz.Children.Add(rod);
            raiz.Children.Add(new ScrollViewer { Content = corpo, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto });
            Content = raiz;
            Montar();
        }

        static ComboBox Combo(string[] itens, int sel, double larg, string dica = null)
        {
            var c = new ComboBox { Width = larg, Margin = new Thickness(0, 0, 6, 0), ToolTip = dica };
            foreach (string s in itens) c.Items.Add(s);
            c.SelectedIndex = Math.Max(0, sel);
            return c;
        }

        void Montar()
        {
            corpo.Children.Clear(); lc.Clear(); lk.Clear();
            corpo.Children.Add(UiPlaca.TituloSecao("Casos de carga", null, 0));
            corpo.Children.Add(Ajuda("As cargas são lançadas no caso ativo (aba Ações). O peso próprio, se ligado, entra no caso marcado."));
            for (int i = 0; i < m.Casos.Count; i++)
            {
                int ii = i;
                var linha = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
                var nome = Campo(m.Casos[i].Nome, 240); nome.Margin = new Thickness(0, 0, 6, 0);
                nome.ToolTip = "Nome descritivo deste caso de carga (ex.: Peso próprio, Sobrecarga, Vento 0°).";
                var tipo = Combo(NomesTipo, Array.IndexOf(TiposCaso, m.Casos[i].Tipo), 190, "Natureza da ação segundo a NBR 8681/8800 (usada para gerar automaticamente os coeficientes γ e ψ nas combinações).");
                var pp = new CheckBox { Content = "peso próprio", IsChecked = m.Casos[i].PesoProprio, Foreground = BTexto, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 10, 0), ToolTip = "Quando marcado, inclui o peso próprio das barras neste caso de carga (se o peso próprio estiver ligado no modelo)." };
                linha.Children.Add(nome); linha.Children.Add(tipo); linha.Children.Add(pp);
                var ex = B("Excluir", () => { Ler(); if (m.Casos.Count > 1) { m.RemoverCaso(ii); Montar(); } else erro.Text = "O modelo precisa de pelo menos um caso."; });
                ex.IsEnabled = m.Casos.Count > 1;
                ex.ToolTip = "Remove este caso de carga e todas as cargas lançadas nele.";
                linha.Children.Add(ex);
                if (i == m.CasoAtivo) { var ch = UiPlaca.Chip("ativo", UiPlaca.Nivel.Info, "Caso que recebe as cargas lançadas na aba Ações."); ch.Margin = new Thickness(6, 0, 0, 0); linha.Children.Add(ch); }
                corpo.Children.Add(linha);
                lc.Add((nome, tipo, pp));
            }
            var novo = B("+ Novo caso", () => { Ler(); m.AdicionarCaso(new CasoCarga { Nome = "Q" + m.Casos.Count + " - Sobrecarga", Tipo = "Q" }); Montar(); });
            novo.HorizontalAlignment = HorizontalAlignment.Left; novo.Margin = new Thickness(0, 10, 0, 14);
            novo.ToolTip = "Adiciona um novo caso de carga ao modelo (sobrecarga, vento, equipamento...).";
            corpo.Children.Add(novo);
            corpo.Children.Add(new Border { Height = 1, Background = TemaPlant.BBordaSutil, Margin = new Thickness(0, 0, 0, 14) });

            corpo.Children.Add(UiPlaca.TituloSecao("Combinações", null, 4));
            corpo.Children.Add(Ajuda("Fator de cada caso em cada combinação. \"Gerar\" cria as combinações normais da NBR 8681/8800 (γg 1,4 ou 1,0 favorável; sobrecarga 1,5 e ψ0 0,5; vento 1,4 e ψ0 0,6; ELS quase permanente e frequentes). Confira os fatores para o seu caso. Com cargas majoradas (ELU), ajuste a \"Segurança\" da aba Dimensionamento (ex.: 1,1 = γa1)."));
            var cab = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0), Visibility = m.Combinacoes.Count == 0 ? Visibility.Collapsed : Visibility.Visible };
            cab.Children.Add(Cab("Nome", 246)); cab.Children.Add(Cab("Tipo", 86));
            foreach (CasoCarga c in m.Casos) cab.Children.Add(Cab(c.Nome, 86));
            corpo.Children.Add(cab);
            for (int i = 0; i < m.Combinacoes.Count; i++)
            {
                int ii = i;
                Combinacao k = m.Combinacoes[i];
                var linha = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
                var nome = Campo(k.Nome, 240); nome.Margin = new Thickness(0, 0, 6, 0);
                nome.ToolTip = "Nome desta combinação de ações (ex.: ELU 1: 1,4G + 1,5Q).";
                var tipo = Combo(new[] { "ELU", "ELS" }, k.Tipo == "ELS" ? 1 : 0, 80, "Classificação da combinação: ELU (Estado Limite Último, resistência/estabilidade) ou ELS (Estado Limite de Serviço, flechas/deslocamentos).");
                linha.Children.Add(nome); linha.Children.Add(tipo);
                var fs = new List<TextBox>();
                for (int c = 0; c < m.Casos.Count; c++)
                {
                    var tb = Campo(Num.Fmt(k.Fator(c), 3).TrimEnd('0').TrimEnd(','), 80); tb.Margin = new Thickness(0, 0, 6, 0);
                    if (tb.Text == "") tb.Text = "0";
                    tb.ToolTip = "Fator multiplicador do caso \"" + m.Casos[c].Nome + "\" na combinação \"" + k.Nome + "\" (ex.: 1,4; 1,5; 0,75; 0).";
                    fs.Add(tb); linha.Children.Add(tb);
                }
                var exk = B("Excluir", () => { Ler(); m.Combinacoes.RemoveAt(ii); Montar(); });
                exk.ToolTip = "Remove esta combinação da lista.";
                linha.Children.Add(exk);
                corpo.Children.Add(linha);
                lk.Add((nome, tipo, fs));
            }
            if (m.Combinacoes.Count == 0) { var ev = UiTabela.Vazio("tabela", "Nenhuma combinação", "Os resultados são os de cada caso isolado. Use \"Gerar combinações\" para criar as da NBR 8681/8800."); ev.Margin = new Thickness(0, 14, 0, 10); corpo.Children.Add(ev); }
            var bt = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            var nk = B("+ Nova combinação", () =>
            {
                Ler();
                var c = new Combinacao { Nome = "Combinação " + (m.Combinacoes.Count + 1), Tipo = "ELU" };
                foreach (CasoCarga cc in m.Casos) c.Fatores.Add(1);
                m.Combinacoes.Add(c); Montar();
            });
            nk.Margin = new Thickness(0, 0, 6, 0);
            nk.ToolTip = "Adiciona manualmente uma combinação de carga com fator inicial 1,0 para cada caso.";
            bt.Children.Add(nk);
            var bg = B("Gerar combinações NBR 8681/8800", () =>
            {
                Ler();
                if (m.Combinacoes.Count > 0 && Caixa.Mostrar(this, "Gerar combinações", "Substituir as " + m.Combinacoes.Count + " combinações atuais pelas geradas?", null, new[] { ("Substituir", MessageBoxResult.Yes, true), ("Cancelar", MessageBoxResult.Cancel, false) }, "?", JanelaEscura.BAzul) != MessageBoxResult.Yes) return;
                m.Combinacoes = Analise.GerarNbr(m.Casos); Montar();
            });
            bg.ToolTip = "Gera automaticamente as combinações normais ELU e ELS da NBR 8681/8800 a partir dos tipos (G, Q, V, E) dos casos cadastrados.";
            bt.Children.Add(bg);
            corpo.Children.Add(bt);
        }

        static UIElement Ajuda(string s) { var t = T(s, 11.5, TemaPlant.BTexto3); t.MaxWidth = 860; t.HorizontalAlignment = HorizontalAlignment.Left; t.Margin = new Thickness(0, 0, 0, 4); return t; }

        static UIElement Cab(string s, double larg) { var t = T(s, 11, BFraco, FontWeights.SemiBold); t.Width = larg; t.TextTrimming = TextTrimming.CharacterEllipsis; t.TextWrapping = TextWrapping.NoWrap; t.ToolTip = s; return t; }

        bool Ler()
        {
            erro.Text = "";
            for (int i = 0; i < lc.Count && i < m.Casos.Count; i++)
            {
                string nome = lc[i].nome.Text.Trim();
                if (nome == "") { erro.Text = "Dê um nome a todos os casos."; lc[i].nome.BorderBrush = BVermelho; return false; }
                m.Casos[i].Nome = nome;
                m.Casos[i].Tipo = TiposCaso[Math.Max(0, lc[i].tipo.SelectedIndex)];
                m.Casos[i].PesoProprio = lc[i].pp.IsChecked == true;
            }
            for (int i = 0; i < lk.Count && i < m.Combinacoes.Count; i++)
            {
                Combinacao k = m.Combinacoes[i];
                k.Nome = lk[i].nome.Text.Trim() == "" ? "Combinação " + (i + 1) : lk[i].nome.Text.Trim();
                k.Tipo = lk[i].tipo.SelectedIndex == 1 ? "ELS" : "ELU";
                for (int c = 0; c < lk[i].f.Count; c++)
                {
                    double v;
                    if (!Num.Ler(lk[i].f[c].Text, out v) || !double.IsFinite(v)) { erro.Text = k.Nome + ": fator inválido (\"" + lk[i].f[c].Text + "\"). Use números, ex.: 1,4."; lk[i].f[c].BorderBrush = BVermelho; return false; }
                    while (k.Fatores.Count <= c) k.Fatores.Add(0);
                    k.Fatores[c] = v;
                }
            }
            if (m.Casos.Count(c => c.PesoProprio) > 1) { erro.Text = "Marque o peso próprio em um caso só."; return false; }
            return true;
        }

        void Ok()
        {
            if (!Ler()) return;
            Resultado = m;
            DialogResult = true;
        }
    }
}
