// Janela "Comparar com CYPE" (REVISAO-TELAS 3.4): escolher a obra do CYPE 3D 2016 (.ed3), comparar hipótese × grandeza
// (CYPE × Estructto × diferença %) e as taxas de aproveitamento por barra; "Importar como modelo" traz a obra para o
// motor e mostra o resumo (nós, barras, seções, casos, avisos). Rotas: POST /api/comparar_cype e /api/importar_cype
// com {nome, conteudo_base64}.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using static NovoFtool.UiPlaca;
using static NovoFtool.UiAnalise;

namespace NovoFtool
{
    public class JanelaCype : JanelaEscura
    {
        public event Action<JsonNode> Importado;   // {modelo, avisos} do motor
        readonly Func<JsonObject, Task<JsonNode>> comparar, importar;
        readonly Border hostCab = new Border(), hostCorpo = new Border();
        readonly TextBlock lblArq = new TextBlock();
        string arquivo, erro; byte[] conteudo;
        JsonNode cmp, imp;
        bool ocupado;

        static readonly Dictionary<string, string> Grandezas = new Dictionary<string, string>
        {
            ["desloc_max"] = "Deslocamento máximo", ["reacao_total_Fx"] = "Reação total Fx", ["reacao_total_Fy"] = "Reação total Fy",
            ["reacao_total_Fz"] = "Reação total Fz", ["reacao_max_apoio"] = "Maior reação num apoio", ["N_max"] = "Força normal máxima",
            ["V_max"] = "Cortante máxima", ["T_max"] = "Torção máxima", ["M_max"] = "Momento máximo"
        };

        public JanelaCype(Func<JsonObject, Task<JsonNode>> comparar, Func<JsonObject, Task<JsonNode>> importar)
        {
            this.comparar = comparar; this.importar = importar;
            Moldar(this, "Comparar com CYPE", 1180, 700);
            var barra = new DockPanel { Margin = new Thickness(12, 10, 12, 0) };
            var bt = Botao("Escolher obra .ed3…", "abrir", Escolher, "Abre uma obra do CYPE 3D 2016 (.ed3). O arquivo só é lido.");
            DockPanel.SetDock(bt, Dock.Left); barra.Children.Add(bt);
            lblArq.FontSize = 12; lblArq.Foreground = TemaPlant.BTexto2; lblArq.VerticalAlignment = VerticalAlignment.Center; lblArq.Margin = new Thickness(12, 0, 0, 0);
            lblArq.TextTrimming = TextTrimming.CharacterEllipsis;
            barra.Children.Add(lblArq);
            var corpo = new DockPanel();
            DockPanel.SetDock(barra, Dock.Top); corpo.Children.Add(barra);
            hostCorpo.Margin = new Thickness(12, 10, 12, 10);
            corpo.Children.Add(hostCorpo);
            var nota = Tx("Só leitura: a obra do CYPE não é alterada. Importar substitui o modelo do motor (Desfazer volta).", 11.5, TemaPlant.BTexto3);
            nota.TextTrimming = TextTrimming.CharacterEllipsis;
            Content = Raiz(hostCab, corpo, Rodape(nota,
                B("Fechar", Close, false, "Fecha a janela (Esc)."),
                B("Importar como modelo", () => Rodar(true), false, "Traz a obra do CYPE para o modelo: nós, barras, seções, casos e cargas."),
                B("Comparar", () => Rodar(false), true, "Calcula a obra no motor e compara com os resultados gravados pelo CYPE.")));
            Montar();
        }

        // API das capturas
        public void Mostrar(string nomeArquivo, JsonNode comparacao, JsonNode importacao) { arquivo = nomeArquivo; cmp = comparacao; imp = importacao; erro = null; Montar(); }

        void Escolher()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Obra do CYPE 3D (*.ed3)|*.ed3", Title = "Escolher obra do CYPE 3D" };
            if (dlg.ShowDialog(this) != true) return;
            try { conteudo = File.ReadAllBytes(dlg.FileName); arquivo = Path.GetFileName(dlg.FileName); cmp = null; imp = null; erro = null; }
            catch (Exception ex) { erro = "Não foi possível ler o arquivo: " + ex.Message; }
            Montar();
            if (erro == null) Rodar(false);
        }

        async void Rodar(bool importarModelo)
        {
            if (ocupado) return;
            if (conteudo == null) { Escolher(); return; }
            ocupado = true;
            lblArq.Text = arquivo + " · " + (importarModelo ? "importando…" : "calculando e comparando…");
            try
            {
                var corpo = new JsonObject { ["nome"] = arquivo, ["conteudo_base64"] = Convert.ToBase64String(conteudo) };
                if (importarModelo) { imp = await importar(corpo); Importado?.Invoke(imp); }
                else cmp = await comparar(corpo);
                erro = null;
            }
            catch (Exception ex) { erro = ComandosAnalise.MensagemMotor(ex); }
            finally { ocupado = false; }
            Montar();
        }

        void Montar()
        {
            lblArq.Text = arquivo == null ? "Nenhuma obra escolhida" : arquivo;
            if (erro != null) hostCab.Child = Cabecalho(Nivel.Erro, "✕", "Não foi possível ler a obra", erro);
            else if (cmp == null && imp == null) hostCab.Child = Cabecalho(Nivel.Neutro, "–", "Escolha uma obra do CYPE 3D", "O motor calcula a obra com as mesmas cargas e compara deslocamentos, reações, esforços e taxas com os do CYPE.");

            var linhasT = Lista(cmp?["tabela"]).ToList();
            var taxas = Lista(cmp?["taxas"]).ToList();
            if (cmp != null && erro == null)
            {
                var difs = linhasT.Select(n => Math.Abs(D(n, "dif_pct"))).Where(x => !double.IsNaN(x)).ToList();
                double max = difs.Count > 0 ? difs.Max() : double.NaN;
                bool valido = cmp["valido"] == null || Bool(cmp, "valido");
                var nv = !valido ? Nivel.Erro : double.IsNaN(max) || max <= 2 ? Nivel.Ok : max <= 5 ? Nivel.Alerta : Nivel.Erro;
                string tit = !valido ? "Obra inválida para comparação" : nv == Nivel.Ok ? "Confere com o CYPE · maior diferença " + F(max, 2) + "%"
                           : "Diferença de " + F(max, 1) + "% em relação ao CYPE";
                var chips = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                foreach (var (rot, n, cnv) in new[] { ("≤ 2%", difs.Count(x => x <= 2), Nivel.Ok), ("2–5%", difs.Count(x => x > 2 && x <= 5), Nivel.Alerta), ("> 5%", difs.Count(x => x > 5), Nivel.Erro) })
                {
                    if (n == 0 && cnv != Nivel.Ok) continue;
                    var c = Chip(n + "  " + rot, cnv, "Grandezas com diferença " + rot + ".", null, 12); c.Margin = new Thickness(6, 0, 0, 0); c.Padding = new Thickness(10, 4, 10, 4); chips.Children.Add(c);
                }
                hostCab.Child = Cabecalho(nv, nv == Nivel.Ok ? "✓" : nv == Nivel.Alerta ? "!" : "✕", tit,
                    S(cmp, "arquivo") + " · " + Lista(cmp["hipoteses"]).Count() + " hipóteses · " + linhasT.Count + " grandezas · " + taxas.Count + " barras com taxa", chips);
            }
            else if (imp != null && erro == null)
                hostCab.Child = Cabecalho(Nivel.Ok, "✓", "Obra importada como modelo", "Calcule (F5) para ver resultados; use Comparar para conferir com o CYPE.");

            if (cmp == null && imp == null)
            {
                hostCorpo.Child = Vazio("importar", "Nenhuma obra carregada", "Clique em Escolher obra .ed3… — pode ser a pasta de projetos do CYPE; nada é gravado lá.");
                return;
            }
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (cmp != null)
            {
                var tab = Grade(new[] { new Col("Hipótese", 76), new Col("Grandeza", 0), new Col("CYPE", 92, true), new Col("Estructto", 92, true), new Col("Dif.", 76, true, "Diferença do Estructto em relação ao CYPE.") },
                    linhasT.Select(n =>
                    {
                        string un = S(n, "unidade");
                        return new UIElement[]
                        {
                            Tx(S(n, "hipotese"), 12, TemaPlant.BTexto2), Tx((Grandezas.TryGetValue(S(n, "grandeza"), out var gn) ? gn : S(n, "grandeza")) + (un != "" ? " [" + un + "]" : ""), 12, TemaPlant.BTexto1),
                            Num1(Valor(D(n, "cype"))), Num1(Valor(D(n, "nosso"))), Dif(D(n, "dif_pct"))
                        };
                    }).ToList());
                g.Children.Add(Cartao("Deslocamentos, reações e esforços por hipótese", tab, null, new Thickness(0, 0, 10, 0)));
            }
            var dir = new DockPanel();
            if (imp != null)
            {
                var ci = ResumoImportacao(imp); DockPanel.SetDock(ci, Dock.Top); dir.Children.Add(ci);
                if (cmp == null) { ci.Width = 520; ci.HorizontalAlignment = HorizontalAlignment.Left; dir.Children.Add(new Border()); }
            }
            if (cmp != null)
            {
                var sp = new DockPanel();
                var nt = Tx(S(cmp, "nota_taxas"), 11, TemaPlant.BTexto3, null, true); nt.Margin = new Thickness(0, 8, 0, 0);
                DockPanel.SetDock(nt, Dock.Bottom); sp.Children.Add(nt);
                sp.Children.Add(taxas.Count == 0 ? Vazio("info", "Sem taxas do CYPE", "A obra não tem verificação gravada.")
                    : Grade(new[] { new Col("Barra", 0), new Col("CYPE", 64, true), new Col("Estructto", 76, true), new Col("Dif.", 70, true) },
                        taxas.Select(n => new UIElement[]
                        {
                            Dica(Tx(S(n, "barra"), 12, TemaPlant.BTexto1, FontWeights.SemiBold), NomeCriterio(S(n, "criterio"))),
                            Num1(Pct(D(n, "cype"))), Num1(Pct(D(n, "nosso")), Cores(NivelAp(D(n, "nosso"), Bool(n, "ok"))).fg, true), Dif(D(n, "dif_pct"), true)
                        }).ToList()));
                dir.Children.Add(Cartao("Taxas de aproveitamento", sp, null, new Thickness(0)));
            }
            Grid.SetColumn(dir, cmp != null ? 1 : 0); if (cmp == null) Grid.SetColumnSpan(dir, 2);
            g.Children.Add(dir);
            hostCorpo.Child = g;
        }

        static string Valor(double v) { return double.IsNaN(v) ? "—" : Math.Abs(v) < 1e-9 ? "0" : Portico.Num.Fmt(v, Math.Abs(v) >= 100 ? 1 : Math.Abs(v) >= 1 ? 2 : 3); }

        // neutro: taxas (só ordem de grandeza, ver nota_taxas) não ganham cor de passa/falha
        static FrameworkElement Dif(double d, bool neutro = false)
        {
            if (double.IsNaN(d)) return Dica(Num1("—", TemaPlant.BTexto3), "Sem diferença percentual: valores próximos de zero.");
            double a = Math.Abs(d);
            Brush cor = neutro ? TemaPlant.BTexto2 : a <= 2 ? TemaPlant.BSucesso : a <= 5 ? TemaPlant.BAlerta : TemaPlant.BErro;
            return Num1((d > 0 ? "+" : d < 0 ? "−" : "") + Portico.Num.Fmt(a, a < 10 ? 2 : 1) + "%", cor, true);
        }

        static Border ResumoImportacao(JsonNode imp)
        {
            var m = imp?["modelo"];
            int Conta(string k) { var x = m?[k]; return x is JsonArray a ? a.Count : x is JsonObject o ? o.Count : 0; }
            var pares = new List<(string, string, string)>
            {
                ("Nome", S(m, "nome"), null), ("Nós", Conta("nos").ToString(), null), ("Barras", Conta("barras").ToString(), null), ("Seções", Conta("secoes").ToString(), null),
                ("Materiais", Conta("materiais").ToString(), null), ("Casos", Conta("casos").ToString(), null),
                ("Cargas", (Conta("cargas_nodais") + Conta("cargas_distribuidas") + Conta("cargas_pontuais")).ToString(), "Nodais + distribuídas + pontuais."), ("Pavimentos", Conta("pavimentos").ToString(), null)
            };
            var sp = new StackPanel();
            sp.Children.Add(Ficha(pares, 76));
            foreach (var a in Lista(imp?["avisos"])) { var t = Tx("⚠  " + a, 11.5, TemaPlant.BAlertaClaro, null, true); t.Margin = new Thickness(0, 4, 0, 0); sp.Children.Add(t); }
            var c = Cartao("Modelo importado", sp, null, new Thickness(0, 0, 0, 10));
            return c;
        }
    }
}
