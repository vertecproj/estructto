// Janela "Análise modal" (REVISAO-TELAS 3.4): períodos, frequências e participação de massa por direção, com o
// acumulado e a meta de 90% da massa (NBR 15421, análise espectral). Clicar num modo dispara ModoEscolhido (a vista anima o modo).
// Dados: GET /api/modal → {periodos_s[], frequencias_hz[], participacao[[x,y,z]], modos[...]}.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using static NovoFtool.UiPlaca;
using static NovoFtool.UiAnalise;

namespace NovoFtool
{
    public class JanelaModal : JanelaEscura
    {
        public event Action<int> ModoEscolhido;   // número do modo (1, 2, ...)
        readonly double[] Per, f;
        readonly double[][] part;                  // [modo][x, y, z] em fração
        readonly Border corpoTabela = new Border(), painelModo = new Border();
        int sel;
        static readonly string[] Dir = { "X", "Y", "Z" };

        public JanelaModal(JsonNode d)
        {
            Per = Lista(d?["periodos_s"]).Select(x => D(x)).ToArray();
            f = Lista(d?["frequencias_hz"]).Select(x => D(x)).ToArray();
            part = Lista(d?["participacao"]).Select(p => Enumerable.Range(0, 3).Select(i => D(p?[i])).ToArray()).ToArray();
            Moldar(this, "Análise modal", 1080, 640);

            double[] tot = Enumerable.Range(0, 3).Select(i => part.Sum(p => double.IsNaN(p[i]) ? 0 : p[i])).ToArray();
            var chips = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            for (int i = 0; i < 2; i++)
            {
                bool ok = tot[i] >= 0.9;
                var c = Chip("Σ" + Dir[i] + "  " + Pct(tot[i]) + (ok ? "  ✓" : "  !"), ok ? Nivel.Ok : Nivel.Alerta,
                    "Massa mobilizada na direção " + Dir[i] + " somando os " + Per.Length + " modos." + (ok ? "" : " Abaixo de 90%: aumente o número de modos."), null, 12);
                c.Margin = new Thickness(6, 0, 0, 0); c.Padding = new Thickness(10, 4, 10, 4);
                chips.Children.Add(c);
            }
            Border cab;
            if (Per.Length == 0)
                cab = Cabecalho(Nivel.Neutro, "–", "Sem modos", "O motor não devolveu modos de vibração para este modelo.");
            else
            {
                bool ok = tot[0] >= 0.9 && tot[1] >= 0.9;
                cab = Cabecalho(ok ? Nivel.Ok : Nivel.Alerta, ok ? "✓" : "!",
                    Per.Length + " modos · fundamental T₁ = " + F(Per[0], 3, "s") + " (" + F(f[0], 2, "Hz") + ")",
                    ok ? "Os modos calculados mobilizam mais de 90% da massa em X e em Y (NBR 15421, análise espectral)."
                       : "Os modos calculados não mobilizam 90% da massa em X e Y (NBR 15421, análise espectral): calcule mais modos.", chips);
            }

            var corpo = new Grid { Margin = new Thickness(12, 10, 12, 10) };
            corpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            corpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
            corpo.Children.Add(corpoTabela);
            painelModo.Margin = new Thickness(10, 0, 0, 0);
            Grid.SetColumn(painelModo, 1); corpo.Children.Add(painelModo);

            var dica = Tx("Clique num modo para vê-lo na vista; o período e a massa mobilizada aparecem à direita.", 11.5, TemaPlant.BTexto3);
            Content = Raiz(cab, corpo, Rodape(dica,
                B("Fechar", Close, false, "Fecha a janela (Esc)."),
                B("Animar modo", () => ModoEscolhido?.Invoke(sel + 1), true, "Anima o modo selecionado na vista do modelo.")));
            Montar();
        }

        public void Selecionar(int modo) { sel = Math.Max(0, Math.Min(Per.Length - 1, modo)); if (grade == null) Montar(); else { grade.Selecionar(sel); MontarPainel(); } ModoEscolhido?.Invoke(sel + 1); }

        void Montar()
        {
            if (Per.Length == 0) { corpoTabela.Child = Vazio("info", "Nenhum modo calculado", "Verifique se o modelo tem massa (peso próprio ou cargas permanentes)."); painelModo.Child = null; return; }
            var cols = new[]
            {
                new Col("Modo", 56), new Col("T [s]", 80, true), new Col("f [Hz]", 80, true),
                new Col("Part. X", 0, true, "Fração da massa mobilizada pelo modo na direção X."), new Col("Part. Y", 0, true), new Col("Part. Z", 0, true),
                new Col("Σ X", 70, true, "Massa acumulada em X até este modo."), new Col("Σ Y", 70, true, "Massa acumulada em Y até este modo.")
            };
            var acum = new double[Per.Length, 2];   // Σ X, Σ Y até o modo i
            double sx = 0, sy = 0;
            for (int i = 0; i < Per.Length; i++) { var p = P(i); sx += Nz(p[0]); sy += Nz(p[1]); acum[i, 0] = sx; acum[i, 1] = sy; }
            UIElement[] Linha(int i)
            {
                var p = P(i);
                int dom = Dominante(p);
                var l = new List<UIElement> { Tx((i + 1).ToString(), 12, TemaPlant.BTexto1, FontWeights.SemiBold), Num1(F(Per[i], 3)), Num1(F(f.Length > i ? f[i] : double.NaN, 2)) };
                for (int k = 0; k < 3; k++) l.Add(CelPart(p[k], k == dom));
                l.Add(Num1(Pct(acum[i, 0]), acum[i, 0] >= 0.9 ? TemaPlant.BSucesso : TemaPlant.BTexto2));
                l.Add(Num1(Pct(acum[i, 1]), acum[i, 1] >= 0.9 ? TemaPlant.BSucesso : TemaPlant.BTexto2));
                return l.ToArray();
            }
            grade = new GradeVirtual(cols, Per.Length, Linha, sel, i => Selecionar(i), i => Selecionar(i),
                dicaLinha: i => "Modo " + (i + 1) + ": T = " + F(Per[i], 3, "s") + ", predominante em " + Dir[Dominante(P(i))] + ".");
            corpoTabela.Child = grade;
            MontarPainel();
        }

        GradeVirtual grade;
        double[] P(int i) { return part.Length > i ? part[i] : new[] { double.NaN, double.NaN, double.NaN }; }

        static double Nz(double v) { return double.IsNaN(v) ? 0 : v; }
        static int Dominante(double[] p) { int k = 0; for (int i = 1; i < 3; i++) if (Nz(p[i]) > Nz(p[k])) k = i; return k; }

        static FrameworkElement CelPart(double v, bool dom)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            var barra = new Grid { Height = 6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            barra.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, Nz(v)), GridUnitType.Star) });
            barra.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0, 1 - Nz(v)), GridUnitType.Star) });
            var tr = new Border { Background = TemaPlant.BSup2, CornerRadius = new CornerRadius(3) }; Grid.SetColumnSpan(tr, 2); barra.Children.Add(tr);
            if (Nz(v) > 0.005) barra.Children.Add(new Border { Background = dom ? TemaPlant.BRealce : TemaPlant.BTextoMudo, CornerRadius = new CornerRadius(3) });
            g.Children.Add(barra);
            var t = Num1(Nz(v) < 0.0005 ? "0%" : (v * 100).ToString(v < 0.1 ? "0.0" : "0") + "%", dom && Nz(v) > 0.05 ? TemaPlant.BTexto1 : TemaPlant.BTexto3, dom && Nz(v) > 0.05);
            Grid.SetColumn(t, 1); g.Children.Add(t);
            return g;
        }

        void MontarPainel()
        {
            var p = P(sel);
            int dom = Dominante(p);
            var sp = new StackPanel();
            var topo = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            topo.Children.Add(Tx("Modo " + (sel + 1), 18, TemaPlant.BTexto1, FontWeights.Bold));
            var c = Chip(Nz(p[dom]) > 0.05 ? "translação " + Dir[dom] : "torção / local", Nivel.Info, "Direção com maior participação de massa neste modo.");
            c.Margin = new Thickness(10, 0, 0, 0); topo.Children.Add(c);
            sp.Children.Add(topo);
            sp.Children.Add(Ficha(new List<(string, string, string)>
            {
                ("Período", F(Per[sel], 3, "s"), "T = 1/f"), ("Frequência", F(f[sel], 2, "Hz"), null),
                ("ω", F(2 * Math.PI * f[sel], 2, "rad/s"), "Frequência angular ω = 2πf"), ("Massa X", Pct(Nz(p[0])), null),
                ("Massa Y", Pct(Nz(p[1])), null), ("Massa Z", Pct(Nz(p[2])), null)
            }, 72));
            var acum = new StackPanel();
            for (int k = 0; k < 3; k++)
            {
                double s = part.Take(sel + 1).Sum(x => Nz(x[k]));
                var lin = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                lin.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
                lin.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                lin.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
                lin.Children.Add(Tx(Dir[k], 12, TemaPlant.BTexto2));
                var b = BarraCap(s, true, 6); Grid.SetColumn(b, 1); lin.Children.Add(b);
                var pc = Num1(Pct(s), s >= 0.9 ? TemaPlant.BSucesso : TemaPlant.BTexto1, true); Grid.SetColumn(pc, 2); lin.Children.Add(pc);
                acum.Children.Add(lin);
            }
            var cartAcum = Cartao("Massa acumulada até o modo " + (sel + 1), acum, null, new Thickness(0, 10, 0, 0));
            cartAcum.Padding = new Thickness(0, 10, 0, 0); cartAcum.Background = null; cartAcum.BorderThickness = new Thickness(0, 1, 0, 0);
            sp.Children.Add(cartAcum);
            var nota = Tx("Meta: 90% da massa em cada direção horizontal (NBR 15421, análise espectral).", 11.5, TemaPlant.BTexto3, null, true);
            nota.Margin = new Thickness(0, 8, 0, 0); sp.Children.Add(nota);
            painelModo.Child = Cartao("Modo selecionado", sp, null, new Thickness(0));
        }
    }
}
