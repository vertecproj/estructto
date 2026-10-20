// Janela "Combinações (NBR 8681)" da aba Ações: lista só de leitura gerada pelo motor (GET /api/combinacoes),
// com filtro ELU/ELS, o fator γ·ψ de cada caso por combinação e, para a combinação selecionada, a separação em γ e ψ.
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
        string tipoUso = "comercial", filtro = "todas", sel;
        readonly Border hostBarra = new Border(), hostTabela = new Border(), hostDetalhe = new Border();
        readonly TextBlock status = Status();

        static readonly Dictionary<string, string> NomeEstado = new Dictionary<string, string>
        {
            ["ELU"] = "ELU normal", ["ELU_EXC"] = "ELU excepcional", ["ELS_QP"] = "ELS quase permanente", ["ELS_FREQ"] = "ELS frequente", ["ELS_RARA"] = "ELS rara",
        };
        // NBR 8681 tabela 6 (ψ0, ψ1, ψ2), igual a acoes/combinacoes.py PSI
        static readonly Dictionary<string, double[]> Psi = new Dictionary<string, double[]>
        {
            ["residencial"] = new[] { 0.5, 0.4, 0.3 }, ["comercial"] = new[] { 0.7, 0.6, 0.4 }, ["escritorio"] = new[] { 0.7, 0.6, 0.4 },
            ["biblioteca"] = new[] { 0.8, 0.7, 0.6 }, ["garagem"] = new[] { 0.8, 0.7, 0.6 }, ["cobertura"] = new[] { 0.8, 0.7, 0.6 },
            ["vento"] = new[] { 0.6, 0.3, 0.0 }, ["temperatura"] = new[] { 0.6, 0.5, 0.3 },
        };
        static readonly double[] Gamas = { 1.0, 1.1, 1.15, 1.2, 1.25, 1.3, 1.35, 1.4, 1.5 };

        // combinacoes = GET /api/combinacoes; casos = GET /api/modelo/caso; tipoUso = config.tipo_uso. Nulos: busca no motor.
        public JanelaCombinacoes(JsonNode combinacoes = null, JsonNode casosJson = null, string tipoUso = null)
        {
            Title = "Combinações (NBR 8681)"; Width = 1180; Height = 720; MinWidth = 900; MinHeight = 480;
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
            var esq = new DockPanel { Margin = new Thickness(16, 12, 12, 8) };
            DockPanel.SetDock(hostBarra, Dock.Top); esq.Children.Add(hostBarra);
            esq.Children.Add(hostTabela);
            g.Children.Add(esq);
            var dir = new Border { BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(1, 0, 0, 0), Background = TemaPlant.BSup1, Padding = new Thickness(14, 12, 16, 8), Child = new ScrollViewer { Content = hostDetalhe, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
            Grid.SetColumn(dir, 1); g.Children.Add(dir);
            Content = UiAcoes.Montar(this, Cabecalho("Combinações", "NBR 8681", "Geradas a partir dos tipos e subtipos dos casos e do uso da edificação. Só leitura: mude os casos para mudar as combinações."),
                g, Rodape(status, B("Fechar", Close, true)));
            if (combinacoes != null) Carregar(combinacoes, casosJson, tipoUso);
            else Loaded += async (s, e) =>
            {
                JsonNode c = null, k = null, r = null;
                if (await Tentar(status, async () => { c = await Motor.Get("/api/combinacoes"); k = await Motor.Get("/api/modelo/caso"); r = await Motor.Get("/api/resumo"); }))
                    Carregar(c, k, S(r?["config"]?["tipo_uso"], "comercial"));
            };
            Montar();
        }

        void Carregar(JsonNode c, JsonNode k, string uso)
        {
            combs.Clear(); casos.Clear();
            tipoUso = string.IsNullOrEmpty(uso) ? "comercial" : uso;
            foreach (var x in Lista(k)) casos.Add((S(x?["nome"]), S(x?["tipo"]), S(x?["subtipo"])));
            foreach (var x in Lista(c))
            {
                var f = new Dictionary<string, double>();
                if (x?["fatores"] is JsonObject o) foreach (var kv in o) f[kv.Key] = D(kv.Value, 0);
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
            var dp = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var uso = UiPlaca.Tx("Uso da edificação: " + tipoUso + " · fator da célula = γ·ψ", 11.5, TemaPlant.BTexto3);
            DockPanel.SetDock(uso, Dock.Right); dp.Children.Add(uso);
            dp.Children.Add(seg);
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
            var linhas = new List<string[]>();
            foreach (var cs in casos)
            {
                if (!c.f.TryGetValue(cs.nome, out double f) || f == 0) continue;
                var (g, p) = Separar(cs, c.estado, f);
                linhas.Add(new[] { cs.nome, cs.tipo, F(g, 2), double.IsNaN(p) ? "—" : F(p, 2), F(f, 2) });
            }
            sp.Children.Add(TabelaFixa(new[] { "Caso", "Tipo", "γ", "ψ", "γ·ψ" }, new[] { false, false, true, true, true }, linhas, new[] { 0, 44, 52, 52, 58.0 }));
            sp.Children.Add(Nota("Expressão: " + string.Join(" + ", linhas.Select(l => l[4] + "·" + l[0]))));
            sp.Children.Add(Nota(c.estado.StartsWith("ELU")
                ? "ELU: Fd = Σ γg·Gk + γq·(Q1k + Σ ψ0j·Qjk). A ação variável principal entra com ψ = 1."
                : "ELS: γ = 1. Quase permanente usa ψ2; frequente, ψ1 na principal e ψ2 nas demais; rara, ψ1 nas secundárias."));
            hostDetalhe.Child = sp;
        }

        // Separa o fator γ·ψ do motor em γ e ψ. ponytail: dedução pela tabela de ψ (o motor só devolve o produto);
        // em empates raros (ex.: 1,2 = γ 1,2 principal ou 1,5·0,8 secundária) fica a leitura de ação principal.
        // Se o motor passar a devolver γ e ψ por caso, trocar por eles.
        (double g, double p) Separar((string nome, string tipo, string sub) c, string estado, double f)
        {
            if (estado.StartsWith("ELS")) return (1.0, c.tipo == "G" ? double.NaN : f);
            if (c.tipo != "Q" && c.tipo != "V") return (f, double.NaN);
            if (Gamas.Any(g => Math.Abs(g - f) < 1e-6)) return (f, 1.0);
            string uso = c.tipo == "V" ? "vento" : string.IsNullOrEmpty(c.sub) ? tipoUso : c.sub;
            if (Psi.TryGetValue(uso, out var psi))
                foreach (double p in psi.Where(p => p > 0))
                    if (Gamas.Any(g => Math.Abs(g * p - f) < 1e-6)) return (f / p, p);
            return (f, double.NaN);
        }
    }
}
