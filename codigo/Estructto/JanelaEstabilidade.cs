// Janela "Estabilidade" (REVISAO-TELAS 3.4): γz (NBR 6118 15.5.3) e B2 (NBR 8800 4.9.4 e Anexo D) por direção, com a régua
// 1,0 → 1,4 marcada nos limites (1,1 nós fixos / pequena; 1,3 γz e 1,4 B2 limite de aplicação), a classificação e,
// por combinação, as que exigem majoração ou excedem o limite. Dados: GET /api/estabilidade (projeto.estabilidade).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using static NovoFtool.UiPlaca;
using static NovoFtool.UiAnalise;

namespace NovoFtool
{
    public class JanelaEstabilidade : JanelaEscura
    {
        static readonly Dictionary<string, string> Classif = new Dictionary<string, string>
        {
            ["nos_fixos"] = "nós fixos", ["nos_moveis"] = "nós móveis", ["fora_do_limite"] = "fora do limite", ["instavel"] = "instável",
            ["pequena"] = "pequena deslocabilidade", ["media"] = "média deslocabilidade", ["grande"] = "grande deslocabilidade"
        };

        static Nivel NivelClass(string c) { return c == "nos_fixos" || c == "pequena" ? Nivel.Ok : c == "nos_moveis" || c == "media" ? Nivel.Alerta : Nivel.Erro; }
        static string NomeClass(string c) { return Classif.TryGetValue(c ?? "", out var s) ? s : c; }

        public JanelaEstabilidade(JsonNode d)
        {
            Moldar(this, "Estabilidade global", 1040, 660);
            var gz = d?["gama_z"] as JsonObject ?? new JsonObject();
            var b2 = d?["B2"] as JsonObject ?? new JsonObject();
            var por = d?["por_combinacao"] as JsonObject ?? new JsonObject();

            // ----- veredito: pior direção -----
            var regs = new List<(string tipo, string dir, double v, string cl, JsonNode n)>();
            foreach (var kv in gz) regs.Add(("γz", kv.Key, D(kv.Value, "valor"), S(kv.Value, "classificacao"), kv.Value));
            foreach (var kv in b2) regs.Add(("B2", kv.Key, D(kv.Value, "max"), S(kv.Value, "classificacao"), kv.Value));
            Border cab;
            if (!Bool(d, "aplicavel") || regs.Count == 0)
                cab = Cabecalho(Nivel.Neutro, "–", "Estabilidade não avaliada", "O modelo não tem pavimentos ou combinações com ações horizontais: γz e B2 não se aplicam.");
            else
            {
                var pior = regs.OrderByDescending(r => (int)NivelClass(r.cl) == (int)Nivel.Erro ? 3 : NivelClass(r.cl) == Nivel.Alerta ? 2 : 1).ThenByDescending(r => double.IsNaN(r.v) ? 99 : r.v).First();
                var nv = NivelClass(pior.cl);
                string tit = nv == Nivel.Ok ? "Estrutura de " + NomeClass(pior.cl) + " · " + pior.tipo + " máx. = " + F(pior.v, 3)
                           : nv == Nivel.Alerta ? "Efeitos de 2ª ordem relevantes · " + pior.tipo + " = " + F(pior.v, 3) + " em " + pior.dir
                           : "Fora do limite · " + pior.tipo + " = " + F(pior.v, 3) + " em " + pior.dir;
                string sub = nv == Nivel.Ok ? "Os efeitos globais de 2ª ordem podem ser desprezados (até 10% dos de 1ª ordem)."
                           : nv == Nivel.Alerta ? "Os esforços horizontais são majorados (0,95·γz ou B2) nas combinações indicadas abaixo."
                           : "Acima do limite de aplicação do processo simplificado: faça análise de 2ª ordem (P-Δ) ou enrijeça a estrutura.";
                var chips = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                foreach (var r in regs)
                {
                    var c = Chip(r.tipo + " " + r.dir + "  " + F(r.v, 3), NivelClass(r.cl), NomeClass(r.cl) + " — combinação " + S(r.n, "combinacao"), null, 12);
                    c.Margin = new Thickness(6, 0, 0, 0); c.Padding = new Thickness(10, 4, 10, 4); chips.Children.Add(c);
                }
                cab = Cabecalho(nv, nv == Nivel.Ok ? "✓" : nv == Nivel.Alerta ? "!" : "✕", tit, sub, chips);
            }

            // ----- corpo: um cartão por direção + tabela por combinação -----
            var corpo = new Grid { Margin = new Thickness(12, 10, 12, 10) };
            corpo.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            corpo.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var cartoes = new UniformGrid { Rows = 1 };
            foreach (var r in regs) cartoes.Children.Add(CartaoDirecao(r.tipo, r.dir, r.v, r.cl, r.n));
            corpo.Children.Add(cartoes);

            var linhas = new List<UIElement[]>();
            foreach (var kv in por)
            {
                var n = kv.Value;
                double g = D(n, "gama_z"), b = D(n, "B2");
                bool inst = Bool(n, "instavel");
                double v = double.IsNaN(g) ? b : g;
                double lim = double.IsNaN(g) ? 1.4 : 1.3;
                var nv = inst || v > lim ? Nivel.Erro : v > 1.1 ? Nivel.Alerta : Nivel.Ok;
                linhas.Add(new UIElement[]
                {
                    Tx(kv.Key, 12, TemaPlant.BTexto1), Tx(S(n, "direcao"), 12, TemaPlant.BTexto2), Num1(F(g, 3)), Num1(F(b, 3)),
                    Chip(inst ? "✕ instável" : nv == Nivel.Erro ? "✕ excede" : nv == Nivel.Alerta ? "! majorada" : "✓ ok", nv)
                });
            }
            var mj = d?["majoracao"];
            string titCombo = "Combinações que exigem atenção" + (mj != null ? " · " + F(D(mj, "n_majoradas"), 0) + " majoradas, " + F(D(mj, "n_excedem"), 0) + " excedem" : "");
            FrameworkElement tab = linhas.Count == 0
                ? Vazio("ok", "Nenhuma combinação exige majoração", "Em todas as combinações γz ≤ 1,1 e B2 ≤ 1,1: os esforços de 1ª ordem valem sem amplificação.")
                : Grade(new[] { new Col("Combinação", 0), new Col("Direção", 80), new Col("γz", 80, true), new Col("B2", 80, true), new Col("Situação", 120) }, linhas);
            var cardComb = Cartao(titCombo, tab, null, new Thickness(0, 10, 0, 0));
            Grid.SetRow(cardComb, 1); corpo.Children.Add(cardComb);

            var avisos = Lista(d?["avisos"]).Select(a => a.ToString()).ToList();
            UIElement esq = avisos.Count == 0
                ? Tx("γz: NBR 6118 15.5.3 (concreto) · B2: NBR 8800 4.9.4 e Anexo D (aço). Rigidez reduzida conforme cada norma.", 11.5, TemaPlant.BTexto3)
                : Chip("⚠ " + avisos.Count + (avisos.Count == 1 ? " aviso" : " avisos") + " do motor", Nivel.Alerta, string.Join("\n", avisos));
            Content = Raiz(cab, corpo, Rodape(esq, B("Fechar", Close, true, "Fecha a janela (Esc).")));
        }

        // Cartão de uma direção: valor grande, régua 1,0–1,4 com faixas, classificação e memória curta.
        static Border CartaoDirecao(string tipo, string dir, double v, string cl, JsonNode n)
        {
            var nv = NivelClass(cl);
            var c = Cores(nv);
            var sp = new StackPanel();
            var topo = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var chip = Chip((nv == Nivel.Ok ? "✓ " : nv == Nivel.Alerta ? "! " : "✕ ") + NomeClass(cl), nv);
            DockPanel.SetDock(chip, Dock.Right); topo.Children.Add(chip);
            var val = new StackPanel { Orientation = Orientation.Horizontal };
            val.Children.Add(Tx(tipo + " =", 14, TemaPlant.BTexto2, FontWeights.SemiBold));
            var tv = Tx(" " + F(v, 3), 20, c.fg, FontWeights.Bold); Typography.SetNumeralAlignment(tv, FontNumeralAlignment.Tabular);
            val.Children.Add(tv);
            topo.Children.Add(val);
            sp.Children.Add(topo);
            sp.Children.Add(Regua(v, tipo == "γz" ? 1.3 : 1.4));
            var pares = new List<(string, string, string)> { ("Combinação", S(n, "combinacao"), "Combinação que dá o maior valor nesta direção.") };
            if (tipo == "γz")
            {
                pares.Add(("M1 (tombamento)", F(D(n, "M1_kNm"), 1, "kN·m"), "Momento de tombamento das ações horizontais (1ª ordem)."));
                pares.Add(("ΔM (2ª ordem)", F(D(n, "dM_kNm"), 1, "kN·m"), "Soma das cargas verticais × deslocamentos horizontais."));
                pares.Add(("Majoração", F(D(n, "majoracao"), 3), "Fator aplicado aos esforços horizontais (0,95·γz quando 1,1 < γz ≤ 1,3)."));
            }
            else pares.Add(("Pavimento", S(n, "pavimento"), "Pavimento com o maior B2."));
            var g = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(118) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < pares.Count; i++)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });
                var r = Dica(Tx(pares[i].Item1, 12, TemaPlant.BTexto2), pares[i].Item3); Grid.SetRow(r, i); g.Children.Add(r);
                var t = Num1(pares[i].Item2); t.TextTrimming = TextTrimming.CharacterEllipsis; Dica(t, pares[i].Item2); Grid.SetRow(t, i); Grid.SetColumn(t, 1); g.Children.Add(t);
            }
            sp.Children.Add(g);
            string item = S(n, "item");
            if (item != "") { var it = Tx(item, 11, TemaPlant.BTexto3); it.Margin = new Thickness(0, 6, 0, 0); it.TextTrimming = TextTrimming.CharacterEllipsis; sp.Children.Add(Dica(it, item + (S(n, "rigidez") != "" ? "\n" + S(n, "rigidez") : ""))); }
            return Cartao("Direção " + dir, sp, null, new Thickness(0, 0, 10, 0));
        }

        // Régua 1,0 → 1,4: faixas verde (≤ 1,1), âmbar (≤ lim), vermelha; marcador no valor.
        static FrameworkElement Regua(double v, double lim)
        {
            const double v0 = 1.0, v1 = 1.4;
            var sp = new StackPanel();
            var g = new Grid { Height = 8 };
            double[] lims = { v0, 1.1, lim, v1 };
            Brush[] cores = { TemaPlant.BSucessoFundo, TemaPlant.BAlertaFundo, TemaPlant.BErroFundo };
            for (int i = 0; i < 3; i++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, lims[i + 1] - lims[i]), GridUnitType.Star) });
            for (int i = 0; i < 3; i++)
            {
                var b = new Border { Background = cores[i], BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(i == 0 ? 0 : 1, 0, 0, 0) };
                Grid.SetColumn(b, i); g.Children.Add(b);
            }
            double fr = double.IsNaN(v) ? 0 : Math.Max(0, Math.Min(1, (v - v0) / (v1 - v0)));
            var preench = new Grid();
            preench.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, fr), GridUnitType.Star) });
            preench.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, 1 - fr), GridUnitType.Star) });
            var cor = v > lim ? TemaPlant.BErro : v > 1.1 ? TemaPlant.BAlerta : TemaPlant.BSucesso;
            preench.Children.Add(new Border { Background = cor, Height = 4, VerticalAlignment = VerticalAlignment.Center, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, -1, 0) });
            var marca = new Border { Width = 3, Height = 14, Background = TemaPlant.BTexto1, CornerRadius = new CornerRadius(1), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, -3, -2, -3) };
            preench.Children.Add(marca);
            Grid.SetColumnSpan(preench, 3); g.Children.Add(preench);
            sp.Children.Add(new Border { CornerRadius = new CornerRadius(3), ClipToBounds = false, Child = g });
            var esc = new Grid { Margin = new Thickness(0, 3, 0, 0) };
            foreach (var cd in g.ColumnDefinitions) esc.ColumnDefinitions.Add(new ColumnDefinition { Width = cd.Width });
            for (int i = 0; i < 3; i++)
            {
                var t = Tx(Num1Txt(lims[i]), 10.5, TemaPlant.BTexto3); Grid.SetColumn(t, i); esc.Children.Add(t);
            }
            var tf = Tx(Num1Txt(v1), 10.5, TemaPlant.BTexto3); tf.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(tf, 2); esc.Children.Add(tf);
            sp.Children.Add(esc);
            return sp;
        }

        static string Num1Txt(double x) { return Portico.Num.Fmt(x, 1); }
    }
}
