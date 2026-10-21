// Janela "Combinações" da aba Ações: lista só de leitura gerada pelo motor (GET /api/combinacoes), com filtro ELU/ELS,
// o fator γ·ψ de cada caso por combinação e, para a combinação selecionada, a separação em γ e ψ.
// Seletor de norma (config.combinacoes): NBR 8681 (padrão) ou NBR 8400:1984 5.6 (casos I, II e III, equipamentos de
// levantamento; acoes/nbr8400.py, port do Analise.GerarNbr8400 do Bruno), com o grupo (Mx) e o ψ.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using static NovoFtool.UiAcoes;

namespace NovoFtool
{
    public class JanelaCombinacoes : JanelaEscura
    {
        readonly List<(string nome, string estado, Dictionary<string, double> f)> combs = new List<(string, string, Dictionary<string, double>)>();
        readonly List<(string nome, string tipo, string sub)> casos = new List<(string, string, string)>();
        readonly Dictionary<string, JsonNode> detalhe = new Dictionary<string, JsonNode>(); // combinação -> {caso: {gamma, psi, produto}}
        string tipoUso = "comercial", filtro = "todas", sel;
        string norma = "nbr8681";   // config.combinacoes
        int grupo = 3; double psi = 1.15;   // config.nbr8400
        readonly Dictionary<string, JsonNode> info8400 = new Dictionary<string, JsonNode>(); // combinação -> {caso_8400, Mx, psi, nu, conversao}
        static readonly double[] Mx = { 1, 1, 1, 1.06, 1.12, 1.20 };   // NBR 8400 tabela 10
        readonly Border hostCab = new Border();
        readonly Border hostBarra = new Border(), hostTabela = new Border(), hostDetalhe = new Border();
        readonly TextBlock status = Status();

        static readonly Dictionary<string, string> NomeEstado = new Dictionary<string, string>
        {
            ["ELU"] = "ELU normal", ["ELU_EXC"] = "ELU excepcional", ["ELS_QP"] = "ELS quase permanente", ["ELS_FREQ"] = "ELS frequente", ["ELS_RARA"] = "ELS rara",
        };
        // combinacoes = GET /api/combinacoes; casos = GET /api/modelo/caso; tipoUso = config.tipo_uso; config = config do
        // resumo (combinacoes, nbr8400). Nulos: busca no motor.
        public JanelaCombinacoes(JsonNode combinacoes = null, JsonNode casosJson = null, string tipoUso = null, JsonNode config = null)
        {
            Title = "Combinações"; Width = 1180; Height = 720; MinWidth = 900; MinHeight = 480;
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
            var esq = new DockPanel { Margin = new Thickness(16, 12, 12, 8) };
            DockPanel.SetDock(hostBarra, Dock.Top); esq.Children.Add(hostBarra);
            esq.Children.Add(hostTabela);
            g.Children.Add(esq);
            var dir = new Border { BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(1, 0, 0, 0), Background = TemaPlant.BSup1, Padding = new Thickness(14, 12, 16, 8), Child = new ScrollViewer { Content = hostDetalhe, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
            Grid.SetColumn(dir, 1); g.Children.Add(dir);
            Content = UiAcoes.Montar(this, hostCab, g, Rodape(status, B("Fechar", Close, true)));
            if (combinacoes != null) Carregar(combinacoes, casosJson, tipoUso, config);
            else Loaded += async (s, e) => await Recarregar();
            Montar();
        }

        async System.Threading.Tasks.Task Recarregar()
        {
            JsonNode c = null, k = null, r = null;
            if (await Tentar(status, async () => { c = await Motor.Get("/api/combinacoes"); k = await Motor.Get("/api/modelo/caso"); r = await Motor.Get("/api/resumo"); }))
                Carregar(c, k, S(r?["config"]?["tipo_uso"], "comercial"), r?["config"]);
        }

        // Troca o conjunto de combinações do modelo (PATCH /api/config) e relê a lista
        async void MudarNorma(JsonObject patch)
        {
            try
            {
                if (await Tentar(status, () => Motor.Chamar("PATCH", "/api/config", patch), "Gerando as combinações…")) await Recarregar();
            }
            catch (Exception ex) { Caixa.Erro(this, Title, ex.Message); }   // async void: exceção solta derruba o programa
        }

        JsonObject Patch8400() => new JsonObject { ["combinacoes"] = "nbr8400", ["nbr8400"] = new JsonObject { ["grupo"] = grupo, ["psi"] = psi } };

        void Carregar(JsonNode c, JsonNode k, string uso, JsonNode cfg = null)
        {
            combs.Clear(); casos.Clear(); detalhe.Clear(); info8400.Clear();
            tipoUso = string.IsNullOrEmpty(uso) ? "comercial" : uso;
            norma = S(cfg?["combinacoes"], "nbr8681");
            grupo = (int)D(cfg?["nbr8400"]?["grupo"], 3); psi = D(cfg?["nbr8400"]?["psi"], 1.15);
            foreach (var x in Lista(k)) casos.Add((S(x?["nome"]), S(x?["tipo"]), S(x?["subtipo"])));
            foreach (var x in Lista(c))
            {
                var f = new Dictionary<string, double>();
                if (x?["fatores"] is JsonObject o) foreach (var kv in o) f[kv.Key] = D(kv.Value, 0);
                detalhe[S(x?["nome"])] = x?["detalhe"];
                if (x?["nbr8400"] != null) info8400[S(x?["nome"])] = x["nbr8400"];
                combs.Add((S(x?["nome"]), S(x?["estado"]), f));
            }
            sel = combs.FirstOrDefault().nome;
            Montar();
        }

        IEnumerable<(string nome, string estado, Dictionary<string, double> f)> Visiveis()
        {
            return combs.Where(c => filtro == "todas" || c.estado.StartsWith(filtro == "elu" ? "ELU" : "ELS"));
        }

        void Montar()
        {
            int nElu = combs.Count(c => c.estado.StartsWith("ELU")), nEls = combs.Count - nElu;
            var seg = Escolha(new[] { "todas", "elu", "els" }, new[] { "Todas (" + combs.Count + ")", "ELU (" + nElu + ")", "ELS (" + nEls + ")" }, filtro, v => { filtro = v; Montar(); },
                new[] { "Todas as combinações.", "Estados limites últimos: resistência e estabilidade (normais e excepcionais).", "Estados limites de serviço: deslocamentos (quase permanentes, frequentes e raras)." });
            bool n84 = norma == "nbr8400";
            hostCab.Child = Cabecalho("Combinações", n84 ? "NBR 8400" : "NBR 8681", n84
                ? "Equipamentos de levantamento (5.6): I = Mx·(G + ψ·Q), II = I + vento de serviço, III = G + vento máximo. Só leitura: mude os casos, o grupo ou o ψ."
                : "Geradas a partir dos tipos e subtipos dos casos e do uso da edificação. Só leitura: mude os casos para mudar as combinações.");
            Title = n84 ? "Combinações (NBR 8400)" : "Combinações (NBR 8681)";
            var segNorma = Escolha(new[] { "nbr8681", "nbr8400" }, new[] { "NBR 8681", "NBR 8400" }, norma,
                v => MudarNorma(v == "nbr8400" ? Patch8400() : new JsonObject { ["combinacoes"] = "nbr8681" }),
                new[] { "NBR 8681:2003 (edificações): γ e ψ por tipo de caso; ELU normais e excepcionais, ELS quase permanente, frequente e rara.",
                        "NBR 8400:1984 (pontes rolantes, pórticos, guindastes): casos I, II e III com Mx e ψ; o motor multiplica cada caso por ν/γa1 (tensões admissíveis σe/ν)." });
            var dp = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            FrameworkElement info;
            if (n84)
            {
                var gs = Enumerable.Range(1, 6).Select(i => i.ToString()).ToArray();
                var bxPsi = Num(out TextBox tPsi, psi, null, 64, "Coeficiente dinâmico ψ (tabela 5). Enter aplica.");
                tPsi.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter && UiAcoes.Ler(tPsi, out double v, false, 0)) { psi = v; MudarNorma(Patch8400()); } };
                info = Horiz(UiPlaca.Tx("Grupo", 11.5, TemaPlant.BTexto2), Gap(6),
                    Escolha(gs, gs, grupo.ToString(), v => { grupo = int.Parse(v); MudarNorma(Patch8400()); }, gs.Select(x => "Grupo " + x + ": Mx = " + F(Mx[int.Parse(x) - 1], 2) + " (tabela 10)").ToArray()),
                    Gap(10), UiPlaca.Tx("Mx = " + F(Mx[Math.Max(1, Math.Min(6, grupo)) - 1], 2) + " · ψ", 11.5, TemaPlant.BTexto2), Gap(6), bxPsi);
            }
            else info = UiPlaca.Tx("Uso da edificação: " + tipoUso + " · fator da célula = γ·ψ", 11.5, TemaPlant.BTexto3);
            info.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(info, Dock.Right); dp.Children.Add(info);
            dp.Children.Add(Horiz(segNorma, Gap(12), seg));
            hostBarra.Child = dp;

            var vis = Visiveis().ToList();
            if (combs.Count == 0)
            {
                hostTabela.Child = UiTabela.Vazio("tabela", "Nenhuma combinação", "Cadastre os casos de carga (Ações › Casos…) com tipo G, Q, V, S ou E. As combinações da NBR 8681 são geradas a partir deles.");
                hostDetalhe.Child = null;
                return;
            }
            // só os casos que aparecem em alguma combinação visível
            var usados = casos.Select(c => c.nome).Where(n => vis.Any(c => c.f.ContainsKey(n) && c.f[n] != 0)).ToList();
            var dg = Tabela();
            dg.CanUserSortColumns = true; dg.CanUserReorderColumns = false; dg.CanUserResizeRows = false;
            dg.Columns.Add(UiTabela.Texto("Combinação", "[0]", 230));
            dg.Columns.Add(UiTabela.Texto("Estado", "[1]", 150));
            var cabDir = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader), dg.ColumnHeaderStyle);
            cabDir.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Right));
            for (int i = 0; i < usados.Count; i++)
            {
                var col = UiTabela.Texto(usados[i], "[" + (i + 2) + "]", Math.Max(64, 9 * usados[i].Length + 20), true);
                col.HeaderStyle = cabDir;
                dg.Columns.Add(col);
            }
            var linhas = vis.Select(c => new[] { c.nome, NomeEstado.TryGetValue(c.estado, out var ne) ? ne : c.estado }
                .Concat(usados.Select(n => c.f.TryGetValue(n, out double v) && v != 0 ? F(v, 2) : "")).ToArray()).ToList();
            dg.ItemsSource = linhas;
            int iSel = vis.FindIndex(c => c.nome == sel);
            if (iSel < 0 && vis.Count > 0) { iSel = 0; sel = vis[0].nome; }
            if (iSel >= 0) dg.SelectedIndex = iSel;
            dg.SelectionChanged += (s, e) => { if (dg.SelectedItem is string[] l) { sel = l[0]; MontarDetalhe(); } };
            hostTabela.Child = new Border { BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), ClipToBounds = true, Child = dg };
            MontarDetalhe();
        }

        void MontarDetalhe()
        {
            var sp = new StackPanel();
            var c = combs.FirstOrDefault(x => x.nome == sel);
            if (c.nome == null) { hostDetalhe.Child = null; return; }
            sp.Children.Add(UiPlaca.TituloSecao(c.nome, UiPlaca.Chip(NomeEstado.TryGetValue(c.estado, out var ne) ? ne : c.estado, c.estado.StartsWith("ELU") ? UiPlaca.Nivel.Info : UiPlaca.Nivel.Neutro, null, null, 10.5), 0));
            if (info8400.TryGetValue(c.nome, out var i8)) { MontarDetalhe8400(sp, c, i8); hostDetalhe.Child = sp; return; }
            var linhas = new List<string[]>();
            foreach (var cs in casos)
            {
                if (!c.f.TryGetValue(cs.nome, out double f) || f == 0) continue;
                var d = detalhe.GetValueOrDefault(c.nome)?[cs.nome];
                linhas.Add(new[] { cs.nome, cs.tipo, F(D(d?["gamma"]), 2), cs.tipo is "Q" or "V" ? F(D(d?["psi"]), 2) : "—", F(D(d?["produto"], f), 2) });
            }
            sp.Children.Add(TabelaFixa(new[] { "Caso", "Tipo", "γ", "ψ", "γ·ψ" }, new[] { false, false, true, true, true }, linhas, new[] { 0, 44, 52, 52, 58.0 }));
            sp.Children.Add(Nota("Expressão: " + string.Join(" + ", linhas.Select(l => l[4] + "·" + l[0]))));
            sp.Children.Add(Nota(c.estado.StartsWith("ELU")
                ? "ELU: Fd = Σ γg·Gk + γq·(Q1k + Σ ψ0j·Qjk). A ação variável principal entra com ψ = 1."
                : "ELS: γ = 1. Quase permanente usa ψ2; frequente, ψ1 na principal e ψ2 nas demais; rara, ψ1 nas secundárias."));
            hostDetalhe.Child = sp;
        }

        static FrameworkElement Gap(double w) => new Border { Width = w };

        // NBR 8400: fator = (Mx ou 1)·(1 ou ψ)·ν/γa1 (acoes/nbr8400.py)
        void MontarDetalhe8400(StackPanel sp, (string nome, string estado, Dictionary<string, double> f) c, JsonNode i8)
        {
            string caso = S(i8["caso_8400"]);
            double k = D(i8["conversao"], 1);
            var linhas = casos.Where(cs => c.f.TryGetValue(cs.nome, out double v) && v != 0)
                .Select(cs => new[] { cs.nome, cs.tipo, F(c.f[cs.nome] / k, 3), F(c.f[cs.nome], 3) }).ToList();
            sp.Children.Add(TabelaFixa(new[] { "Caso", "Tipo", "8400", "× ν/γa1" }, new[] { false, false, true, true }, linhas, new[] { 0, 44, 64, 70.0 }));
            var res = Resumo(("Caso", caso, null, "Caso de solicitação da NBR 8400 5.6."), ("Mx", F(D(i8["Mx"]), 2), null, "Tabela 10, pelo grupo."),
                ("ν", F(D(i8["nu"]), 2), null, "Tensão admissível σe/ν do caso (5.6)."), ("ν/γa1", F(k, 3), null, "Conversão para estados limites (γa1 = 1,10)."));
            res.Margin = new Thickness(0, 10, 0, 0);
            sp.Children.Add(res);
            sp.Children.Add(Nota("Expressão: " + string.Join(" + ", linhas.Select(l => l[3] + "·" + l[0]))));
            sp.Children.Add(Nota(caso == "I" ? "Caso I: serviço normal sem vento, Mx·(G + ψ·Q)."
                : caso == "II" ? "Caso II: serviço normal com o vento limite de serviço, Mx·(G + ψ·Q) + Vserviço (um sentido por vez)."
                : caso == "III" ? "Caso III: fora de serviço, G + vento máximo, sem carga (5.6.3)."
                : "ELS (flecha): G + Q, com o vento de serviço quando houver; γ = 1."));
            if (caso != "ELS") sp.Children.Add(Nota("A NBR 8400 usa tensões admissíveis σe/ν. O motor verifica pela NBR 8800 (resistência/γa1): multiplicar o caso por ν/γa1 dá a mesma folga no escoamento (aproximada na flambagem)."));
        }
    }
}
