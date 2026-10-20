// Janela "Verificar barra" sobre o motor (REVISAO-TELAS 3.4): veredito + uma barra de capacidade por critério da norma,
// com o item da NBR, a combinação determinante e o valor solicitante × resistente; à direita, a taxa por combinação,
// a armadura (concreto) e os avisos. Dados: GET /api/verificacao/{barra} (projeto.verificacao_barra).
// Aço/PFF: o motor dá detalhes.taxas {critério: taxa} e a resistência de cada bloco (NcRd, MRd, VRd...); o solicitante
// mostrado é taxa × resistência. Concreto: detalhes.verificacoes [{criterio, taxa, item, combinacao, solicitante, resistente}].
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using static NovoFtool.UiPlaca;
using static NovoFtool.UiAnalise;

namespace NovoFtool
{
    public class JanelaVerificarBarraMotor : JanelaEscura
    {
        public class Item { public string Criterio, NomeItem, Comb, Valor; public double Taxa; public bool Ok = true; }

        // critério do aço/PFF → (bloco de detalhes, chave da resistência, unidade, símbolo)
        static readonly Dictionary<string, (string bloco, string rd, string un, string sim)> MapaAco = new Dictionary<string, (string, string, string, string)>
        {
            ["normal"] = ("compressao", "NcRd", "kN", "N"), ["normal_compressao"] = ("compressao", "NcRd", "kN", "N"),
            ["tracao"] = ("tracao", "NtRd", "kN", "N"), ["compressao"] = ("compressao", "NcRd", "kN", "N"),
            ["flexao_x"] = ("flexao_x", "MRd", "kN·m", "Mx"), ["flexao_y"] = ("flexao_y", "MRd", "kN·m", "My"),
            ["cortante"] = ("cortante", "VRd", "kN", "V"), ["cortante_y"] = ("cortante_y", "VRd", "kN", "Vy"), ["cortante_x"] = ("cortante_x", "VRd", "kN", "Vx"),
            ["torcao"] = ("torcao", "TRd", "kN·m", "T")
        };

        public static List<Item> Itens(JsonNode d)
        {
            var l = new List<Item>();
            var det = d?["detalhes"];
            string combDet = S(d, "combinacao");
            if (det?["verificacoes"] is JsonArray vs)   // concreto
            {
                foreach (var v in vs)
                {
                    string c = S(v, "criterio");
                    l.Add(new Item { Criterio = c, Taxa = D(v, "taxa"), NomeItem = S(v, "item"), Comb = S(v, "combinacao"), Valor = ValorConcreto(c, v), Ok = v?["ok"] == null || Bool(v, "ok") });
                }
            }
            else if (det?["taxas"] is JsonObject tx)   // aço e PFF
            {
                bool pff = S(d, "tipo") == "pff";
                foreach (var kv in tx)
                {
                    string c = kv.Key; double t = D(kv.Value);
                    if (double.IsNaN(t)) continue;
                    var it = new Item { Criterio = c, Taxa = t, Comb = combDet };
                    if (c == "normal" && det["compressao"] == null && det["tracao"] != null) c = "tracao";
                    if (MapaAco.TryGetValue(c, out var mp) && det[mp.bloco] is JsonObject bl)
                    {
                        double rd = D(bl, mp.rd);
                        if (mp.bloco == "compressao" && double.IsNaN(rd)) rd = D(bl, "NcRd");
                        it.NomeItem = S(bl, "item");
                        it.Valor = double.IsNaN(rd) ? "" : mp.sim + ",Sd = " + F(t * rd, 2) + "  ≤  " + mp.sim + ",Rd = " + F(rd, 2, mp.un);
                    }
                    else if (c == "esbeltez" && det["esbeltez"] is JsonObject es)
                    {
                        it.NomeItem = S(es, "item");
                        it.Valor = "λ = " + F(D(es, "valor"), 0) + "  ≤  " + F(D(es, "limite"), 0);
                    }
                    else if (c == "distorcional" && det["flexao_x"]?["distorcional"] is JsonObject di)
                    {
                        it.NomeItem = S(di, "item");
                        double rd = D(di, "MRd_dist");
                        it.Valor = double.IsNaN(rd) ? "" : "M,Sd = " + F(t * rd, 2) + "  ≤  M,dist,Rd = " + F(rd, 2, "kN·m");
                    }
                    else
                    {
                        it.NomeItem = c == S(d, "criterio") ? S(d, "item") : c == "interacao" ? (pff ? "9.9" : "5.5.1.2") : c == "momento_cortante" ? "9.8.4" : "";
                        it.Valor = "Σ = " + F(t, 3) + "  ≤  1,000";
                    }
                    l.Add(it);
                }
            }
            return l.OrderByDescending(x => x.Taxa).ToList();
        }

        static string ValorConcreto(string c, JsonNode v)
        {
            double s = D(v, "solicitante"), r = D(v, "resistente");
            switch (c)
            {
                case "fissuracao_wk": return "wk = " + F(s, 3) + "  ≤  " + F(r, 2, "mm");
                case "flecha": return "δ = " + F(s * 1000, 1) + "  ≤  " + F(r * 1000, 1, "mm") + (S(v, "razao") != "" ? " (" + S(v, "razao") + ")" : "");
                case "x_d": return "x/d = " + F(D(v, "x_d"), 3) + "  ≤  " + F(D(v, "x_d_lim"), 2);
                case "As_max": return "As = " + F(D(v, "As"), 2) + "  ≤  " + F(D(v, "As_max"), 2, "cm²");
                case "cobrimento": return "c = " + F(D(v, "adotado") * 1000, 0) + "  ≥  " + F(D(v, "exigido") * 1000, 0, "mm");
                case "cisalhamento_VRd2": case "cisalhamento_VRd3": return "VSd = " + F(s, 1) + "  ≤  " + F(r, 1, "kN");
            }
            return double.IsNaN(s) || double.IsNaN(r) ? "" : "Sd = " + F(s, 3) + "  ≤  Rd = " + F(r, 3);
        }

        public JanelaVerificarBarraMotor(JsonNode d)
        {
            string barra = S(d, "barra");
            Moldar(this, "Verificar barra — " + barra, 1180, 700);
            var itens = Itens(d);
            bool nao = S(d, "criterio") == "nao_verificada";
            double taxa = D(d, "taxa");
            bool ok = Bool(d, "ok");
            var nv = nao ? Nivel.Neutro : NivelAp(taxa, ok);
            string tit = nao ? "Barra " + barra + " não verificada"
                       : (nv == Nivel.Erro ? "Não passa — " : "Passa · ") + "usa " + Pct(taxa) + " em " + NomeCriterio(S(d, "criterio"));
            string sub = S(d, "secao") + " · " + S(d, "material") + " · L = " + F(D(d, "L_m"), 2, "m") + " · " + S(d, "norma") +
                         (nao ? "" : "      determinante: " + S(d, "combinacao") + ", x = " + F(D(d, "x_m"), 2, "m") + ", item " + S(d, "item"));
            var chips = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var cTipo = Chip(NomeTipo(S(d, "tipo")), Nivel.Info, "Tipo de barra e norma de verificação.", null, 12); cTipo.Padding = new Thickness(10, 4, 10, 4);
            chips.Children.Add(cTipo);
            var cab = Cabecalho(nv, nao ? "–" : nv == Nivel.Erro ? "✕" : nv == Nivel.Alerta ? "!" : "✓", tit, sub, chips);

            var corpo = new Grid { Margin = new Thickness(12, 10, 12, 10) };
            corpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            corpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(380) });

            // esquerda: critérios
            var lista = new StackPanel();
            foreach (var it in itens) lista.Children.Add(LinhaCriterio(it));
            FrameworkElement esq = itens.Count == 0
                ? Vazio("info", "Sem verificações", nao ? "O motor não verifica este tipo de barra ou seção." : "O motor não devolveu os critérios desta barra.")
                : new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = lista };
            var cardEsq = Cartao("Verificações da norma · " + itens.Count + " critérios", esq, null, new Thickness(0, 0, 10, 0));
            corpo.Children.Add(cardEsq);

            // direita: por combinação, armadura, avisos
            var dir = new Grid();
            dir.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            dir.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var por = Lista(d?["por_combinacao"]).OrderByDescending(n => D(n, "taxa")).ToList();
            FrameworkElement tPor = por.Count == 0
                ? Vazio("info", "Sem detalhamento por combinação", "Só a combinação determinante foi verificada.")
                : Grade(new[] { new Col("Combinação", 0), new Col("Taxa", 144) },
                    por.Select(n => new UIElement[] { Dica(Tx(S(n, "combinacao"), 12, TemaPlant.BTexto1), NomeCriterio(S(n, "criterio"))), CelTaxa(D(n, "taxa"), D(n, "taxa") <= 1, 80) }).ToList(),
                    por.FindIndex(n => S(n, "combinacao") == S(d, "combinacao")));
            dir.Children.Add(Cartao("Por combinação (ELU)", tPor, null, new Thickness(0)));
            var extra = new StackPanel();
            if (d?["armadura"] is JsonObject arm)
            {
                var pares = new List<(string, string, string)>();
                foreach (var (k, rot, un) in new[] { ("As_cm2", "As (inferior)", "cm²"), ("As_neg_cm2", "As (superior)", "cm²"), ("As2_cm2", "As′ comprimida", "cm²"), ("Asw_s_cm2_m", "Asw/s", "cm²/m"), ("Asl_torcao_cm2", "Asl torção", "cm²"), ("As_pilar_cm2", "As pilar", "cm²") })
                    if (!double.IsNaN(D(arm, k)) && D(arm, k) > 0) pares.Add((rot, F(D(arm, k), 2, un), null));
                if (S(arm, "barras") != "") pares.Add(("Barras", S(arm, "barras"), "Sugestão de bitolas para As."));
                extra.Children.Add(Cartao("Armadura calculada", Ficha(pares, 92), null, new Thickness(0, 10, 0, 0)));
            }
            var avisos = Lista(d?["avisos"]).Select(a => a.ToString()).ToList();
            if (avisos.Count > 0)
            {
                var sp = new StackPanel();
                foreach (var a in avisos) { var t = Tx("⚠  " + a, 11.5, TemaPlant.BAlertaClaro, null, true); t.Margin = new Thickness(0, 0, 0, 4); sp.Children.Add(t); }
                extra.Children.Add(Cartao("Avisos do motor", sp, null, new Thickness(0, 10, 0, 0)));
            }
            Grid.SetRow(extra, 1); dir.Children.Add(extra);
            Grid.SetColumn(dir, 1); corpo.Children.Add(dir);

            var nota = Tx("Aço/PFF: solicitante = taxa × resistência na combinação determinante. Resultado do motor; exige conferência do engenheiro (ART).", 11.5, TemaPlant.BTexto3);
            nota.TextTrimming = TextTrimming.CharacterEllipsis;
            Content = Raiz(cab, corpo, Rodape(nota, B("Fechar", Close, true, "Fecha a janela (Esc).")));
        }

        // Linha de capacidade (GUIA 5.6): nome + item | Sd ≤ Rd | % + chip; trilho de 6 px; combinação embaixo.
        public static FrameworkElement LinhaCriterio(Item it)
        {
            var nv = NivelAp(it.Taxa, it.Ok);
            var c = Cores(nv);
            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            var l1 = new Grid();
            l1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            l1.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            l1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
            l1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
            var nome = new StackPanel { Orientation = Orientation.Horizontal };
            nome.Children.Add(Tx(NomeCriterio(it.Criterio), 12.5, TemaPlant.BTexto1, FontWeights.Medium));
            if (!string.IsNullOrEmpty(it.NomeItem))
            {
                var ch = Chip("NBR " + it.NomeItem, Nivel.Neutro, "Item da norma que define esta verificação.", null, 10.5);
                ch.Margin = new Thickness(8, 0, 0, 0); ch.MaxWidth = 260;
                ((TextBlock)ch.Child).TextTrimming = TextTrimming.CharacterEllipsis;
                nome.Children.Add(ch);
            }
            l1.Children.Add(nome);
            var val = Tx(it.Valor ?? "", 11.5, TemaPlant.BTexto2); val.FontFamily = TemaPlant.FonteMono; val.Margin = new Thickness(12, 0, 12, 0);
            Grid.SetColumn(val, 1); l1.Children.Add(val);
            var pct = Num1(Pct(it.Taxa), c.fg, true); Grid.SetColumn(pct, 2); l1.Children.Add(pct);
            var chip = Chip(nv == Nivel.Erro ? "✕ falha" : nv == Nivel.Alerta ? "⚠ alerta" : "✓ passa", nv, null, null, 10.5);
            chip.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(chip, 3); l1.Children.Add(chip);
            sp.Children.Add(l1);
            var barra = BarraCap(it.Taxa, it.Ok, 6); ((FrameworkElement)barra).Margin = new Thickness(0, 5, 0, 3);
            sp.Children.Add(barra);
            if (!string.IsNullOrEmpty(it.Comb)) sp.Children.Add(Tx(it.Comb, 11, TemaPlant.BTexto3));
            return sp;
        }
    }
}
