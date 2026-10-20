// Janela "Casos de carga" da aba Ações (substitui a matriz editável de casos × combinações): lista simples com
// nome, tipo (G/Q/V/S/E) e subtipo. O subtipo escolhe γ (origem de G) e ψ (uso de Q) na NBR 8681/8800; em V é
// o grupo de ventos mutuamente exclusivos. Grava por diferença: POST/PATCH/DELETE /api/modelo/caso.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using static NovoFtool.UiAcoes;

namespace NovoFtool
{
    public class JanelaCasosCarga : JanelaEscura
    {
        class Caso { public string Orig, Nome, Tipo = "Q", Sub = ""; public TextBox TNome, TSub; public ComboBox CSub; }

        readonly List<Caso> casos = new List<Caso>();
        readonly Dictionary<string, (string tipo, string sub)> orig = new Dictionary<string, (string, string)>();
        readonly Border host = new Border();
        readonly TextBlock status = Status();

        static readonly string[] Tipos = { "G", "Q", "V", "S", "E" };
        static readonly string[] NomesTipo = { "G · permanente", "Q · variável", "V · vento", "S · sismo", "E · excepcional" };
        static readonly (string v, string nome)[] SubG =
        {
            ("", "Geral (γg 1,50)"), ("metalica", "Peso próprio metálico (1,25)"), ("pre_moldada", "Pré-moldada (1,30)"),
            ("moldada_local", "Moldada no local (1,35)"), ("industrializada_in_loco", "Industrializada in loco (1,40)"),
        };
        static readonly (string v, string nome)[] SubQ =
        {
            ("", "Uso da edificação"), ("residencial", "Residencial (ψ0 0,5)"), ("comercial", "Comercial (ψ0 0,7)"), ("escritorio", "Escritório (ψ0 0,7)"),
            ("biblioteca", "Biblioteca (ψ0 0,8)"), ("garagem", "Garagem (ψ0 0,8)"), ("cobertura", "Cobertura (ψ0 0,8)"), ("temperatura", "Temperatura (ψ0 0,6)"),
        };
        static readonly Regex Auto = new Regex(@"^(V[+-][XY].*|S[XY](_TOR)?)$");

        // casos = GET /api/modelo/caso. Nulo: busca no motor.
        public JanelaCasosCarga(JsonNode casosJson = null)
        {
            Title = "Casos de carga"; Width = 860; Height = 620; MinWidth = 720; MinHeight = 420;
            var sv = new ScrollViewer { Content = host, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(16, 12, 16, 8) };
            Content = UiAcoes.Montar(this, Cabecalho("Casos de carga", "NBR 8681", "Cada caso agrupa cargas da mesma natureza. O tipo e o subtipo definem os coeficientes γ e ψ das combinações."),
                sv, Rodape(status, B("Cancelar", Close), B("Aplicar casos", Salvar, true, "Grava a lista de casos no modelo.")));
            if (casosJson != null) Carregar(casosJson);
            else Loaded += async (s, e) => { JsonNode c = null; if (await Tentar(status, async () => c = await Motor.Get("/api/modelo/caso"))) Carregar(c); };
            Montar();
        }

        void Carregar(JsonNode j)
        {
            casos.Clear(); orig.Clear();
            foreach (var x in Lista(j))
            {
                var c = new Caso { Orig = S(x?["nome"]), Nome = S(x?["nome"]), Tipo = S(x?["tipo"], "G"), Sub = S(x?["subtipo"]) };
                casos.Add(c); orig[c.Orig] = (c.Tipo, c.Sub);
            }
            Montar();
        }

        void Montar()
        {
            var sp = new StackPanel();
            var cont = string.Join(" · ", Tipos.Select(t => (t, n: casos.Count(c => c.Tipo == t))).Where(x => x.n > 0).Select(x => x.n + " " + x.t));
            sp.Children.Add(UiPlaca.TituloSecao("Casos (" + casos.Count + ")", cont == "" ? null : UiPlaca.Tx(cont, 11, TemaPlant.BTexto3), 0));
            if (casos.Count == 0)
            {
                var vz = UiTabela.Vazio("tabela", "Nenhum caso de carga", "Crie ao menos o caso de cargas permanentes (G) para lançar cargas e gerar as combinações.");
                vz.Margin = new Thickness(0, 30, 0, 20); sp.Children.Add(vz);
            }
            else
            {
                var cab = new Grid { Margin = new Thickness(0, 4, 0, 2) };
                Colunas(cab);
                Cel(cab, UiPlaca.Tx("Nome", 11, TemaPlant.BTexto2, FontWeights.SemiBold), 0);
                Cel(cab, UiPlaca.Tx("Tipo", 11, TemaPlant.BTexto2, FontWeights.SemiBold), 1);
                Cel(cab, UiPlaca.Tx("Subtipo (γ / ψ / grupo)", 11, TemaPlant.BTexto2, FontWeights.SemiBold), 2);
                sp.Children.Add(cab);
                foreach (var c in casos) sp.Children.Add(LinhaCaso(c));
            }
            var novo = UiPlaca.Botao("+ Novo caso", null, () => { Ler(); casos.Add(new Caso { Nome = "Q" + (casos.Count(x => x.Tipo == "Q") + 1), Tipo = "Q" }); Montar(); }, "Acrescenta um caso de carga variável (troque o tipo se precisar).");
            novo.HorizontalAlignment = HorizontalAlignment.Left; novo.Margin = new Thickness(0, 12, 0, 0);
            sp.Children.Add(novo);
            sp.Children.Add(Nota("Casos marcados \"automático\" são recriados por Vento… e Sismo… (Gerar ações). Remover um caso remove também as cargas dele."));
            host.Child = sp;
        }

        FrameworkElement LinhaCaso(Caso c)
        {
            bool auto = c.Orig != null && Auto.IsMatch(c.Orig);
            var g = new Grid { Margin = new Thickness(0, 4, 0, 0), MinHeight = 28 };
            Colunas(g);
            if (c.Orig == null)
            {
                c.TNome = new TextBox { Text = c.Nome, Height = 28, VerticalContentAlignment = VerticalAlignment.Center, Background = TemaPlant.BSup3, Foreground = TemaPlant.BTexto1, BorderBrush = TemaPlant.BBorda, CaretBrush = TemaPlant.BTexto1, Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(0, 0, 10, 0), ToolTip = "Nome do caso (ex.: G1, Sobrecarga, Telhas). Depois de aplicado o nome não muda." };
                Cel(g, c.TNome, 0);
            }
            else
            {
                var n = UiAcoes.Horiz(UiPlaca.Tx(c.Nome, 12.5, TemaPlant.BTexto1, FontWeights.SemiBold));
                if (auto) { var ch = UiPlaca.Chip("automático", UiPlaca.Nivel.Info, "Gerado por Vento… ou Sismo…; é recriado a cada \"Gerar ações\".", null, 10.5); ch.Margin = new Thickness(8, 0, 0, 0); n.Children.Add(ch); }
                Cel(g, n, 0);
            }
            var tipo = UiPlaca.Combo(NomesTipo, NomesTipo[Math.Max(0, Array.IndexOf(Tipos, c.Tipo))], v => { Ler(); c.Tipo = Tipos[Array.IndexOf(NomesTipo, v)]; c.Sub = ""; Montar(); }, 150,
                "Natureza da ação (NBR 8681): G permanente, Q variável de uso, V vento, S sismo, E excepcional.");
            tipo.IsEnabled = !auto; if (auto) tipo.Opacity = 0.45;
            Cel(g, tipo, 1);
            c.CSub = null; c.TSub = null;
            if (c.Tipo == "G" || c.Tipo == "Q")
            {
                var lista = c.Tipo == "G" ? SubG : SubQ;
                var atual = lista.FirstOrDefault(x => x.v == c.Sub);
                c.CSub = UiPlaca.Combo(lista.Select(x => x.nome), atual.nome ?? lista[0].nome, v => { }, 230,
                    c.Tipo == "G" ? "Origem da ação permanente: define γg (NBR 8800 tabela 1)." : "Uso da sobrecarga: define ψ0, ψ1 e ψ2 (NBR 8681 tabela 6). \"Uso da edificação\" segue a configuração do modelo.");
                Cel(g, c.CSub, 2);
            }
            else if (c.Tipo == "V")
            {
                c.TSub = new TextBox { Text = c.Sub, Width = 230, Height = 28, HorizontalAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Center, Background = TemaPlant.BSup3, Foreground = TemaPlant.BTexto1, BorderBrush = TemaPlant.BBorda, CaretBrush = TemaPlant.BTexto1, Padding = new Thickness(4, 0, 4, 0), IsEnabled = !auto, Opacity = auto ? 0.45 : 1, ToolTip = "Grupo de vento: casos do mesmo grupo nunca entram juntos numa combinação (vazio = \"vento\")." };
                Cel(g, c.TSub, 2);
            }
            else Cel(g, UiPlaca.Tx(c.Tipo == "S" ? "exclusivo; só em combinações excepcionais" : "só em combinações excepcionais", 11.5, TemaPlant.BTexto3), 2);
            var rem = UiPlaca.Link("Remover", () => { Ler(); casos.Remove(c); Montar(); }, "Remove o caso e todas as cargas lançadas nele.", 11.5);
            rem.Foreground = TemaPlant.BTexto2;
            Cel(g, rem, 3);
            return g;
        }

        static void Colunas(Grid g) { foreach (double w in new[] { 250, 166, 250, 80.0 }) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) }); }
        static void Cel(Grid g, FrameworkElement e, int c) { e.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(e, c); g.Children.Add(e); }

        bool Ler()
        {
            bool ok = true;
            foreach (var c in casos)
            {
                if (c.TNome != null) { c.Nome = c.TNome.Text.Trim(); c.TNome.BorderBrush = c.Nome == "" ? TemaPlant.BErro : TemaPlant.BBorda; ok &= c.Nome != ""; }
                if (c.CSub != null) { var lista = c.Tipo == "G" ? SubG : SubQ; c.Sub = lista.FirstOrDefault(x => x.nome == (string)c.CSub.SelectedItem).v ?? ""; }
                if (c.TSub != null) c.Sub = c.TSub.Text.Trim();
            }
            if (!ok) { status.Text = "✕ Dê um nome a cada caso novo."; status.Foreground = TemaPlant.BErro; }
            else if (casos.GroupBy(c => c.Nome).Any(x => x.Count() > 1)) { status.Text = "✕ Há casos com o mesmo nome."; status.Foreground = TemaPlant.BErro; ok = false; }
            return ok;
        }

        async void Salvar()
        {
            try
            {
                if (!Ler()) return;
                bool ok = await Tentar(status, async () =>
                {
                    var ficam = casos.Where(c => c.Orig != null).Select(c => c.Orig).ToHashSet();
                    foreach (var n in orig.Keys.Where(n => !ficam.Contains(n)))
                        await Motor.Chamar("DELETE", "/api/modelo/caso/" + Uri.EscapeDataString(n) + "?cascata=true");
                    foreach (var c in casos)
                    {
                        var d = new JsonObject { ["tipo"] = c.Tipo, ["subtipo"] = c.Sub };
                        if (c.Orig == null) { d["nome"] = c.Nome; await Motor.Post("/api/modelo/caso", d); }
                        else if (orig[c.Orig] != (c.Tipo, c.Sub)) await Motor.Chamar("PATCH", "/api/modelo/caso/" + Uri.EscapeDataString(c.Orig), d);
                    }
                }, "Gravando os casos…");
                if (ok) DialogResult = true;
            }
            catch (Exception ex) { Caixa.Erro(this, Title, ex.Message); }   // async void: exceção solta derruba o programa
        }
    }
}
