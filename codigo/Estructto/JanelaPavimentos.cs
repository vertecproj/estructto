// Janela "Pavimentos" da aba Ações: tabela nome / cota z / diafragma rígido. Os pavimentos definem onde o vento
// e o sismo aplicam as forças. "Detectar pela geometria" cria um pavimento para cada cota de nós acima da base.
// Grava no motor por diferença: POST/PATCH/DELETE /api/modelo/pavimento.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using static NovoFtool.UiAcoes;

namespace NovoFtool
{
    public class JanelaPavimentos : JanelaEscura
    {
        class Pav { public string Orig, Nome; public double Z; public bool Diaf = true; public TextBox TNome, TZ; public CheckBox CDiaf; }

        readonly List<Pav> pavs = new List<Pav>();
        readonly Dictionary<string, (double z, bool diaf)> orig = new Dictionary<string, (double, bool)>();
        readonly List<double> cotasNos = new List<double>();
        readonly Border host = new Border();
        readonly TextBlock status = Status();

        // pavimentos = GET /api/modelo/pavimento; nos = GET /api/modelo/no (para detectar). Nulos: busca no motor.
        public JanelaPavimentos(JsonNode pavimentos = null, JsonNode nos = null)
        {
            Title = "Pavimentos"; Width = 760; Height = 600; MinWidth = 640; MinHeight = 420;
            var salvar = B("Aplicar pavimentos", Salvar, true, "Grava a lista de pavimentos no modelo.");
            var sv = new ScrollViewer { Content = host, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(16, 12, 16, 8) };
            Content = UiAcoes.Montar(this, Cabecalho("Pavimentos", null, "Cota de cada andar acima da base. Nós a até 1 mm da cota pertencem ao pavimento; o diafragma rígido trava ux, uy e rz no plano."),
                sv, Rodape(status, B("Cancelar", Close), salvar));
            if (pavimentos != null) Carregar(pavimentos, nos);
            else Loaded += async (s, e) =>
            {
                JsonNode p = null, n = null;
                if (await Tentar(status, async () => { p = await Motor.Get("/api/modelo/pavimento"); n = await Motor.Get("/api/modelo/no"); })) Carregar(p, n);
            };
            Montar();
        }

        void Carregar(JsonNode p, JsonNode nos)
        {
            pavs.Clear(); orig.Clear(); cotasNos.Clear();
            foreach (var x in Lista(p))
            {
                var v = new Pav { Orig = S(x?["nome"]), Nome = S(x?["nome"]), Z = D(x?["z"], 0), Diaf = x?["diafragma"] is JsonValue dv && dv.TryGetValue(out bool b) ? b : true };
                pavs.Add(v); orig[v.Orig] = (v.Z, v.Diaf);
            }
            pavs.Sort((a, b) => a.Z.CompareTo(b.Z));
            var zs = Lista(nos).Select(n => D(n?["z"], 0)).ToList();
            if (zs.Count > 0)
            {
                double base0 = zs.Min();
                foreach (double z in zs.OrderBy(z => z)) if (z > base0 + 1e-3 && (cotasNos.Count == 0 || z - cotasNos[^1] > 1e-3)) cotasNos.Add(z);
            }
            Montar();
        }

        void Montar()
        {
            var sp = new StackPanel();
            var detectar = UiPlaca.Botao("Detectar pela geometria", "busca", Detectar, cotasNos.Count == 0 ? "O modelo não tem nós acima da base." : "Cria um pavimento para cada cota de nós acima da base que ainda não tem pavimento (" + cotasNos.Count + " cotas).");
            detectar.IsEnabled = cotasNos.Count > 0; detectar.Opacity = cotasNos.Count > 0 ? 1 : 0.45;
            sp.Children.Add(UiPlaca.TituloSecao("Pavimentos (" + pavs.Count + ")", detectar, 0));
            if (pavs.Count == 0)
            {
                var vz = UiTabela.Vazio("tabela", "Nenhum pavimento", "Sem pavimentos, o vento de edifício e o sismo não têm onde aplicar as forças. Use \"Detectar pela geometria\" ou \"+ Novo pavimento\".");
                vz.Margin = new Thickness(0, 30, 0, 20); sp.Children.Add(vz);
            }
            else
            {
                var cab = new Grid { Margin = new Thickness(0, 4, 0, 2) };
                Colunas(cab);
                Cel(cab, UiPlaca.Tx("Nome", 11, TemaPlant.BTexto2, FontWeights.SemiBold), 0);
                var cz = UiPlaca.Tx("Cota z", 11, TemaPlant.BTexto2, FontWeights.SemiBold); cz.HorizontalAlignment = HorizontalAlignment.Right; cz.Margin = new Thickness(0, 0, 10, 0);
                Cel(cab, cz, 1);
                Cel(cab, UiPlaca.Tx("Diafragma rígido", 11, TemaPlant.BTexto2, FontWeights.SemiBold), 2);
                sp.Children.Add(cab);
                foreach (var p in pavs.OrderByDescending(x => x.Z))
                {
                    var pp = p;
                    var g = new Grid { Margin = new Thickness(0, 4, 0, 0) };
                    Colunas(g);
                    p.TNome = new TextBox { Text = p.Nome, Height = 28, VerticalContentAlignment = VerticalAlignment.Center, Background = TemaPlant.BSup3, Foreground = TemaPlant.BTexto1, BorderBrush = TemaPlant.BBorda, CaretBrush = TemaPlant.BTexto1, Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(0, 0, 10, 0), ToolTip = "Nome do pavimento (ex.: Térreo, 1º andar, Cobertura)." };
                    Cel(g, p.TNome, 0);
                    var n = Num(out p.TZ, p.Z, "m", 110, "Cota do pavimento (m), medida no mesmo eixo Z dos nós.");
                    n.Margin = new Thickness(0, 0, 18, 0); n.HorizontalAlignment = HorizontalAlignment.Right;
                    Cel(g, n, 1);
                    p.CDiaf = new CheckBox { IsChecked = p.Diaf, Content = "sim", Foreground = TemaPlant.BTexto1, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Diafragma rígido: as lajes travam os nós do pavimento no plano (ux, uy, rz iguais)." };
                    Cel(g, p.CDiaf, 2);
                    var rem = UiPlaca.Link("Remover", () => { Ler(); pavs.Remove(pp); Montar(); }, "Remove este pavimento da lista.", 11.5);
                    rem.Foreground = TemaPlant.BTexto2;
                    Cel(g, rem, 3);
                    sp.Children.Add(g);
                }
            }
            var novo = UiPlaca.Botao("+ Novo pavimento", null, () =>
            {
                Ler();
                double z = pavs.Count > 0 ? pavs.Max(x => x.Z) + 3 : 3;
                pavs.Add(new Pav { Nome = "P" + (pavs.Count + 1), Z = z });
                Montar();
            }, "Acrescenta um pavimento 3 m acima do mais alto.");
            novo.HorizontalAlignment = HorizontalAlignment.Left; novo.Margin = new Thickness(0, 12, 0, 0);
            sp.Children.Add(novo);
            host.Child = sp;
        }

        static void Colunas(Grid g)
        {
            foreach (double w in new[] { 260, 140, 140, 80.0 }) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });
        }
        static void Cel(Grid g, FrameworkElement e, int c) { Grid.SetColumn(e, c); g.Children.Add(e); }

        void Detectar()
        {
            Ler();
            int k = pavs.Count;
            foreach (double z in cotasNos)
                if (!pavs.Any(p => Math.Abs(p.Z - z) <= 1e-3)) pavs.Add(new Pav { Nome = "P" + (++k), Z = z });
            pavs.Sort((a, b) => a.Z.CompareTo(b.Z));
            Montar();
        }

        bool Ler()
        {
            bool ok = true;
            foreach (var p in pavs.Where(p => p.TNome != null))
            {
                p.Nome = p.TNome.Text.Trim();
                p.Diaf = p.CDiaf.IsChecked == true;
                if (UiAcoes.Ler(p.TZ, out double z)) p.Z = z; else ok = false;
                if (p.Nome == "") { p.TNome.BorderBrush = TemaPlant.BErro; ok = false; } else p.TNome.BorderBrush = TemaPlant.BBorda;
            }
            if (!ok) { status.Text = "✕ Cada pavimento precisa de nome e de uma cota numérica."; status.Foreground = TemaPlant.BErro; }
            else if (pavs.GroupBy(p => p.Nome).Any(g => g.Count() > 1)) { status.Text = "✕ Há pavimentos com o mesmo nome."; status.Foreground = TemaPlant.BErro; ok = false; }
            return ok;
        }

        async void Salvar()
        {
            try
            {
                if (!Ler()) return;
                bool ok = await Tentar(status, async () =>
                {
                    var ficam = pavs.Where(p => p.Orig != null && p.Orig == p.Nome).Select(p => p.Orig).ToHashSet();
                    foreach (var nome in orig.Keys.Where(n => !ficam.Contains(n)))
                        await Motor.Chamar("DELETE", "/api/modelo/pavimento/" + Uri.EscapeDataString(nome));
                    foreach (var p in pavs)
                    {
                        var d = new JsonObject { ["z"] = p.Z, ["diafragma"] = p.Diaf };
                        if (p.Orig != null && p.Orig == p.Nome)
                        {
                            var o = orig[p.Orig];
                            if (Math.Abs(o.z - p.Z) > 1e-9 || o.diaf != p.Diaf) await Motor.Chamar("PATCH", "/api/modelo/pavimento/" + Uri.EscapeDataString(p.Nome), d);
                        }
                        else { d["nome"] = p.Nome; await Motor.Post("/api/modelo/pavimento", d); }
                    }
                }, "Gravando os pavimentos…");
                if (ok) { DialogResult = true; }
            }
            catch (Exception ex) { Caixa.Erro(this, Title, ex.Message); }   // async void: exceção solta derruba o programa
        }
    }
}
