// Aba Ações: comandos que abrem as janelas novas (vento, sismo, pavimentos, casos, combinações) e as peças de
// interface que elas compartilham. Os botões da faixa são ligados pelo líder (MontarFaixa) chamando estes métodos.
// Todas as janelas conversam com o motor Python (Motor.cs); com dados prontos no construtor (capturas) não chamam o motor.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Portico;

namespace NovoFtool
{
    public static class ComandosAcoes
    {
        public static void AbrirVento(Window dono) { Abrir(dono, new JanelaVento()); }
        public static void AbrirSismo(Window dono) { Abrir(dono, new JanelaSismo()); }
        public static void AbrirPavimentos(Window dono) { Abrir(dono, new JanelaPavimentos()); }
        public static void AbrirCasos(Window dono) { Abrir(dono, new JanelaCasosCarga()); }
        public static void AbrirCombinacoes(Window dono) { Abrir(dono, new JanelaCombinacoes()); }

        static void Abrir(Window dono, Window j) { j.Owner = dono; j.ShowDialog(); }
    }

    // Peças comuns das janelas da aba Ações (tokens do TemaPlant; mesmo vocabulário visual da JanelaPlaca)
    public static class UiAcoes
    {
        // ---------- leitura do JSON do motor ----------
        public static double D(JsonNode n, double padrao = double.NaN)
        {
            return n is JsonValue v && v.TryGetValue(out double d) ? d : padrao;
        }
        public static string S(JsonNode n, string padrao = "")
        {
            return n is JsonValue v && v.TryGetValue(out string s) ? s : n is JsonValue ? n.ToJsonString().Trim('"') : padrao;
        }
        public static IEnumerable<JsonNode> Lista(JsonNode n) { return n as JsonArray ?? new JsonArray(); }

        public static string F(double v, int casas) { return double.IsNaN(v) ? "—" : Portico.Num.Fmt(v, casas); }

        // Erro do motor: {"erro", "detalhe", "erros":[{campo, mensagem}]} → texto curto
        public static string Mensagem(Exception e)
        {
            if (e is MotorErro me)
            {
                try
                {
                    var j = JsonNode.Parse(me.Message);
                    var erros = Lista(j?["erros"]).Select(x => (S(x?["campo"]) + ": " + S(x?["mensagem"])).TrimStart(':', ' ')).ToList();
                    if (erros.Count > 0) return string.Join(" · ", erros);
                    if (j?["detalhe"] != null) return S(j["detalhe"]);
                }
                catch (JsonException) { }
                return me.Message;
            }
            return e.Message;
        }

        // Roda uma chamada ao motor mostrando "trabalhando" e o erro na linha de status
        public static async Task<bool> Tentar(TextBlock status, Func<Task> f, string ocupado = "Consultando o motor de cálculo…")
        {
            status.Text = ocupado; status.Foreground = TemaPlant.BTexto3;
            Mouse.OverrideCursor = Cursors.Wait;
            try { await f(); status.Text = ""; return true; }
            catch (Exception e) { status.Text = "✕ " + Mensagem(e); status.Foreground = TemaPlant.BErro; return false; }
            finally { Mouse.OverrideCursor = null; }
        }

        // ---------- layout ----------
        public static TextBlock Status()
        {
            return new TextBlock { FontSize = 11.5, Foreground = TemaPlant.BTexto3, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        }

        // Cabeçalho da janela: título + norma + texto de apoio (sem faixa colorida)
        public static FrameworkElement Cabecalho(string titulo, string norma, string apoio)
        {
            var sp = new StackPanel();
            var l1 = new StackPanel { Orientation = Orientation.Horizontal };
            l1.Children.Add(UiPlaca.Tx(titulo, 16, TemaPlant.BTexto1, FontWeights.SemiBold));
            if (norma != null) { var n = UiPlaca.Chip(norma, UiPlaca.Nivel.Neutro, null, null, 10.5); n.Margin = new Thickness(10, 1, 0, 0); l1.Children.Add(n); }
            sp.Children.Add(l1);
            if (apoio != null) { var a = UiPlaca.Tx(apoio, 11.5, TemaPlant.BTexto3, null, true); a.Margin = new Thickness(0, 3, 0, 0); sp.Children.Add(a); }
            return new Border { Background = TemaPlant.BSup1, BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(16, 10, 16, 10), Child = sp };
        }

        // Rodapé: status à esquerda, botões à direita (um só primário)
        public static FrameworkElement Rodape(TextBlock status, params UIElement[] botoes)
        {
            var dp = new DockPanel { Margin = new Thickness(14, 8, 14, 8) };
            var bts = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (var b in botoes) { if (b is Button bb) { bb.Height = 28; bb.MinWidth = 92; } bts.Children.Add(b); }
            DockPanel.SetDock(bts, Dock.Right);
            dp.Children.Add(bts);
            dp.Children.Add(status);
            return new Border { BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(0, 1, 0, 0), Background = TemaPlant.BSup1, Child = dp };
        }

        // Cartão de passo (Surface.2, borda sutil, raio 6)
        public static Border Cartao(string titulo, UIElement corpo, UIElement direita = null)
        {
            var sp = new StackPanel();
            if (titulo.Length > 2 && titulo[0] >= '①' && titulo[0] <= '⑨')
            {
                // "① Título": selo numerado do passo, igual ao da JanelaPlaca
                var cab = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
                cab.Children.Add(new Border
                {
                    Width = 20, Height = 20, CornerRadius = new CornerRadius(10), BorderBrush = TemaPlant.BAcento, BorderThickness = new Thickness(1.5), Background = TemaPlant.BAcentoSutil, Margin = new Thickness(0, 0, 8, 0),
                    Child = new TextBlock { Text = (titulo[0] - '①' + 1).ToString(), FontSize = 11, FontWeight = FontWeights.Bold, Foreground = TemaPlant.BRealce, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                });
                cab.Children.Add(UiPlaca.Tx(titulo.Substring(2).ToUpperInvariant(), 11, TemaPlant.BTexto2, FontWeights.SemiBold));
                sp.Children.Add(cab);
            }
            else sp.Children.Add(UiPlaca.TituloSecao(titulo, direita, 0));
            sp.Children.Add(corpo);
            return new Border
            {
                Background = TemaPlant.BSup2, BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 9, 12, 11), Margin = new Thickness(0, 0, 0, 8), Child = sp
            };
        }

        // Linha "rótulo | controles" com coluna de rótulos fixa
        public static Grid Linha(string rotulo, UIElement conteudo, double largRot = 80, string dica = null, double topo = 6)
        {
            var g = new Grid { Margin = new Thickness(0, topo, 0, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(largRot) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.Children.Add(UiPlaca.Dica(UiPlaca.Tx(rotulo, 12, TemaPlant.BTexto2), dica));
            Grid.SetColumn((FrameworkElement)conteudo, 1);
            g.Children.Add(conteudo);
            return g;
        }

        public static StackPanel Horiz(params UIElement[] els)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (var e in els) if (e != null) sp.Children.Add(e);
            return sp;
        }

        public static TextBlock Nota(string s) { var t = UiPlaca.Tx(s, 11, TemaPlant.BTexto3, null, true); t.Margin = new Thickness(0, 6, 0, 0); return t; }

        // Campo numérico com unidade (visual do CaixaNum); o valor é lido de .Text quando a janela aplica
        public static Border Num(out TextBox tb, double valor, string unidade, double larg = 96, string dica = null, int casas = 2)
        {
            tb = new TextBox
            {
                Text = double.IsNaN(valor) ? "" : Portico.Num.Fmt(valor, casas), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = TemaPlant.BTexto1, CaretBrush = TemaPlant.BTexto1,
                TextAlignment = TextAlignment.Right, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(4, 0, 3, 0), FontSize = 12.5
            };
            Typography.SetNumeralAlignment(tb, FontNumeralAlignment.Tabular);
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(tb);
            if (!string.IsNullOrEmpty(unidade)) { var u = UiPlaca.Tx(unidade, 11, TemaPlant.BTexto2); u.Margin = new Thickness(0, 0, 7, 0); u.IsHitTestVisible = false; Grid.SetColumn(u, 1); g.Children.Add(u); }
            var bd = new Border { Height = 28, Width = larg, CornerRadius = new CornerRadius(4), Background = TemaPlant.BSup3, BorderBrush = TemaPlant.BBorda, BorderThickness = new Thickness(1), Child = g, Cursor = Cursors.IBeam };
            var t = tb;
            t.GotKeyboardFocus += (s, e) => { bd.BorderBrush = TemaPlant.BBordaFoco; t.SelectAll(); };
            t.LostKeyboardFocus += (s, e) => bd.BorderBrush = TemaPlant.BBorda;
            return UiPlaca.Dica(bd, dica);
        }

        // Lê um campo; vazio = NaN (quando permitido). Marca a borda em vermelho se inválido.
        public static bool Ler(TextBox tb, out double v, bool vazioOk = false, double min = double.NegativeInfinity)
        {
            v = double.NaN;
            var bd = tb.Parent is Grid g ? g.Parent as Border : null;
            if (vazioOk && string.IsNullOrWhiteSpace(tb.Text)) { if (bd != null) bd.BorderBrush = TemaPlant.BBorda; return true; }
            bool ok = Portico.Num.Ler(tb.Text, out v) && double.IsFinite(v) && v > min;
            if (bd != null) bd.BorderBrush = ok ? TemaPlant.BBorda : TemaPlant.BErro;
            return ok;
        }

        // Segmentado de escolha única que guarda a escolha e redesenha
        public static Border Escolha(string[] valores, string[] textos, string atual, Action<string> mudar, string[] dicas = null)
        {
            var itens = new List<(string, bool, Action, string)>();
            for (int i = 0; i < valores.Length; i++) { string v = valores[i]; itens.Add((textos[i], v == atual, (Action)(() => { if (v != atual) mudar(v); }), dicas?[i])); }
            return UiPlaca.Segmentado(itens);
        }

        // Resumo executivo: blocos rótulo / valor / unidade lado a lado
        public static Border Resumo(params (string rot, string val, string un, string dica)[] itens)
        {
            var g = new UniformGrid { Rows = 1 };
            foreach (var it in itens)
            {
                var sp = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
                sp.Children.Add(UiPlaca.Tx(it.rot, 11, TemaPlant.BTexto2));
                var v = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
                var tv = UiPlaca.Tx(it.val, 18, TemaPlant.BTexto1, FontWeights.SemiBold);
                Typography.SetNumeralAlignment(tv, FontNumeralAlignment.Tabular);
                v.Children.Add(tv);
                if (!string.IsNullOrEmpty(it.un) && it.val != "—") { var u = UiPlaca.Tx(it.un, 11.5, TemaPlant.BTexto2); u.Margin = new Thickness(4, 4, 0, 0); v.Children.Add(u); }
                sp.Children.Add(v);
                g.Children.Add(UiPlaca.Dica(sp, it.dica));
            }
            return new Border { Background = TemaPlant.BSup2, BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 10), Child = g };
        }

        // Tabela compacta (linhas de 26 px, cabeçalho Surface.2, números à direita)
        public static FrameworkElement TabelaFixa(string[] cab, bool[] numero, IEnumerable<string[]> linhas, double[] larguras = null)
        {
            var g = new Grid();
            for (int c = 0; c < cab.Length; c++)
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = larguras != null && larguras[c] > 0 ? new GridLength(larguras[c]) : new GridLength(1, GridUnitType.Star) });
            int r = 0;
            void Celula(string s, int col, Brush cor, FontWeight peso, Brush fundo)
            {
                if (col == 0) { var f = new Border { Background = fundo, BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(0, 0, 0, 1), MinHeight = 26 }; Grid.SetRow(f, r); Grid.SetColumnSpan(f, cab.Length); g.Children.Add(f); }
                var t = UiPlaca.Tx(s, r == 0 ? 11 : 12, cor, peso);
                t.Margin = new Thickness(10, 4, 10, 4);
                t.TextTrimming = TextTrimming.CharacterEllipsis;
                if (numero[col]) { t.HorizontalAlignment = HorizontalAlignment.Right; Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular); }
                Grid.SetRow(t, r); Grid.SetColumn(t, col); g.Children.Add(t);
            }
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int c = 0; c < cab.Length; c++) Celula(cab[c], c, TemaPlant.BTexto2, FontWeights.SemiBold, TemaPlant.BSup2);
            foreach (var l in linhas)
            {
                r++;
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                for (int c = 0; c < cab.Length; c++) Celula(c < l.Length ? l[c] : "", c, c == 0 ? TemaPlant.BTexto1 : TemaPlant.BTexto1, FontWeights.Normal, r % 2 == 1 ? TemaPlant.BSup0 : TemaPlant.BSup1);
            }
            return new Border { BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), ClipToBounds = true, Child = g };
        }

        // Avisos do motor como lista de chips "⚠"
        public static FrameworkElement Avisos(IEnumerable<JsonNode> avisos)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            foreach (var a in avisos)
            {
                var (fg, bg, bd) = UiPlaca.Cores(S(a?["nivel"]) == "erro" ? UiPlaca.Nivel.Erro : UiPlaca.Nivel.Alerta);
                var t = UiPlaca.Tx((S(a?["nivel"]) == "erro" ? "✕ " : "⚠ ") + S(a?["mensagem"]), 11.5, fg, null, true);
                sp.Children.Add(new Border { Background = bg, BorderBrush = bd, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 4, 8, 5), Margin = new Thickness(0, 0, 0, 4), Child = t });
            }
            return sp;
        }

        // Janela de duas colunas: entradas (rolagem) | prévia
        public static Grid DuasColunas(UIElement esquerda, UIElement direita, double largEsq)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(largEsq) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var sv = new ScrollViewer { Content = esquerda, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(16, 12, 10, 8) };
            g.Children.Add(sv);
            var sd = new ScrollViewer { Content = direita, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(14, 12, 16, 8) };
            var bd = new Border { BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(1, 0, 0, 0), Background = TemaPlant.BSup1, Child = sd };
            Grid.SetColumn(bd, 1);
            g.Children.Add(bd);
            return g;
        }

        public static FrameworkElement Montar(Window w, FrameworkElement cab, FrameworkElement corpo, FrameworkElement rodape)
        {
            var dp = new DockPanel();
            DockPanel.SetDock(cab, Dock.Top); dp.Children.Add(cab);
            DockPanel.SetDock(rodape, Dock.Bottom); dp.Children.Add(rodape);
            dp.Children.Add(corpo);
            return dp;
        }
    }
}
