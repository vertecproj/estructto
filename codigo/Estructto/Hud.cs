// HUD flutuante no canto da vista (GUIA-DESIGN 5.12): seletor segmentado [Deformada | N | V | M | % Aprov.],
// escala do diagrama (− trilho +), caso/combinacao atual e legenda do diagrama ativo (unidade, maximo e minimo).
// Cartao translucido sobre o canvas: unico elemento com sombra (elevacao so para flutuantes).
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace NovoFtool
{
    public sealed class HudVista : Border
    {
        readonly Vista vista;
        readonly Action<Diagrama, bool> escolher;     // diagrama, mapa de calor
        readonly Func<string> caso;                   // texto do caso/combinacao atual
        readonly Action<double> fator;                // multiplica a escala do diagrama ativo

        sealed class Item { public Border Caixa; public TextBlock Texto; public Diagrama D; public bool Calor; }
        readonly List<Item> itens = new List<Item>();
        readonly StackPanel linhaEscala = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        readonly Border preenchimento = new Border { Height = 4, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, Background = TemaPlant.BAcento };
        readonly Ellipse polegar = new Ellipse { Width = 12, Height = 12, Fill = TemaPlant.BTexto1, Stroke = TemaPlant.BAcento, StrokeThickness = 2, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        readonly Grid trilho = new Grid { Width = 104, Height = 18, Background = Brushes.Transparent, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock txCaso = new TextBlock { FontSize = 11, Foreground = TemaPlant.BTexto2, Margin = new Thickness(0, 8, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        readonly TextBlock txLegenda = new TextBlock { FontSize = 11, Foreground = TemaPlant.BTexto1, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap, MaxWidth = 280 };
        readonly StackPanel faixas = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        bool arrastando, compacto;
        public bool Compacto { get { return compacto; } set { if (compacto == value) return; compacto = value; Atualizar(); } }

        const double LogMin = -1.2, LogMax = 1.2;   // escala do trilho: de 0,06x a 16x da escala automatica

        public HudVista(Vista v, Action<Diagrama, bool> escolher, Func<string> caso, Action<double> fator)
        {
            vista = v; this.escolher = escolher; this.caso = caso; this.fator = fator;
            HorizontalAlignment = HorizontalAlignment.Left; VerticalAlignment = VerticalAlignment.Top; Margin = new Thickness(10);
            Background = TemaPlant.BCaixaHud; BorderBrush = TemaPlant.BBordaSutil; BorderThickness = new Thickness(1);
            CornerRadius = new CornerRadius(6); Padding = new Thickness(8, 7, 8, 8);
            Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 4, Opacity = 0.45, Color = Colors.Black, Direction = 270 };
            SnapsToDevicePixels = true;

            var pilha = new StackPanel();
            // seletor segmentado
            var seg = new Border { Background = TemaPlant.BSup0, BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(2), HorizontalAlignment = HorizontalAlignment.Left };
            var fila = new StackPanel { Orientation = Orientation.Horizontal };
            Adicionar(fila, "Deformada", Diagrama.Deformada, false, "Configuração deformada (ampliada), com a forma original em fantasma.");
            Adicionar(fila, "N", Diagrama.Normal, false, "Esforço normal N (kN): azul tração, vermelho compressão.");
            Adicionar(fila, "V", Diagrama.Cortante, false, "Esforço cortante V (kN).");
            Adicionar(fila, "M", Diagrama.Momento, false, "Momento fletor M (kN·m), do lado tracionado.");
            Adicionar(fila, "% Aprov.", Diagrama.Verificacao, true, "Mapa de aproveitamento: verde até 75%, ciano até 90%, âmbar até 100%, vermelho acima.");
            seg.Child = fila;
            pilha.Children.Add(seg);

            // escala: "Escala  −  ----o----  +"
            linhaEscala.Children.Add(new TextBlock { Text = "Escala", FontSize = 11, Foreground = TemaPlant.BTexto3, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 8, 0) });
            linhaEscala.Children.Add(BotaoMini("−", "Reduz a escala do diagrama (tecla −).", 1 / 1.4));
            var fundoTrilho = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = TemaPlant.BSup3, VerticalAlignment = VerticalAlignment.Center };
            trilho.Children.Add(fundoTrilho); trilho.Children.Add(preenchimento); trilho.Children.Add(polegar);
            trilho.ToolTip = "Arraste para mudar a escala do diagrama.";
            trilho.MouseLeftButtonDown += (s, e) => { arrastando = true; trilho.CaptureMouse(); Mover(e.GetPosition(trilho).X); e.Handled = true; };
            trilho.MouseMove += (s, e) => { if (arrastando) Mover(e.GetPosition(trilho).X); };
            trilho.MouseLeftButtonUp += (s, e) => { arrastando = false; trilho.ReleaseMouseCapture(); };
            linhaEscala.Children.Add(new Border { Child = trilho, Margin = new Thickness(6, 0, 6, 0) });
            linhaEscala.Children.Add(BotaoMini("+", "Amplia a escala do diagrama (tecla +).", 1.4));
            pilha.Children.Add(linhaEscala);

            pilha.Children.Add(txCaso);
            pilha.Children.Add(txLegenda);
            pilha.Children.Add(faixas);
            Child = pilha;
            SizeChanged += (s, e) => vista.ReservaHud = new Rect(0, 0, Margin.Left + ActualWidth + 8, Margin.Top + ActualHeight + 8);
            Atualizar();
        }

        void Adicionar(StackPanel pai, string texto, Diagrama d, bool calor, string dica)
        {
            var t = new TextBlock { Text = texto, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Foreground = TemaPlant.BTexto2 };
            var b = new Border { Child = t, CornerRadius = new CornerRadius(3), Padding = new Thickness(10, 0, 10, 0), Height = 24, MinWidth = 28, Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(1), Cursor = Cursors.Hand, ToolTip = dica };
            var it = new Item { Caixa = b, Texto = t, D = d, Calor = calor };
            b.MouseEnter += (s, e) => { if (!Ativo(it)) b.Background = TemaPlant.BSup2; };
            b.MouseLeave += (s, e) => { if (!Ativo(it)) b.Background = Brushes.Transparent; };
            b.MouseLeftButtonUp += (s, e) =>
            {
                escolher(Ativo(it) ? Diagrama.Nenhum : it.D, it.Calor);
                vista.Focus(); e.Handled = true;
            };
            System.Windows.Automation.AutomationProperties.SetName(b, texto);
            itens.Add(it); pai.Children.Add(b);
        }

        FrameworkElement BotaoMini(string texto, string dica, double f)
        {
            var t = new TextBlock { Text = texto, FontSize = 14, Foreground = TemaPlant.BTexto2, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -2, 0, 0) };
            var b = new Border { Child = t, Width = 20, Height = 20, CornerRadius = new CornerRadius(3), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = dica };
            b.MouseEnter += (s, e) => { b.Background = TemaPlant.BSup2; t.Foreground = TemaPlant.BTexto1; };
            b.MouseLeave += (s, e) => { b.Background = Brushes.Transparent; t.Foreground = TemaPlant.BTexto2; };
            b.MouseLeftButtonUp += (s, e) => { fator(f); vista.Focus(); e.Handled = true; };
            System.Windows.Automation.AutomationProperties.SetName(b, dica);
            return b;
        }

        bool Ativo(Item it) { return vista.Diag == it.D; }

        double EscalaAtual { get { return vista.Diag == Diagrama.Deformada ? vista.EscalaDef : vista.EscalaDiag; } }

        void Mover(double x)
        {
            double t = Math.Max(0, Math.Min(1, (x - 6) / (trilho.Width - 12)));
            double alvo = Math.Pow(10, LogMin + t * (LogMax - LogMin));
            double atual = EscalaAtual;
            if (atual > 0 && Math.Abs(alvo / atual - 1) > 0.005) fator(alvo / atual);
        }

        void Posicionar()
        {
            double lg = Math.Log10(Math.Max(1e-6, EscalaAtual));
            double t = Math.Max(0, Math.Min(1, (lg - LogMin) / (LogMax - LogMin)));
            double x = 6 + t * (trilho.Width - 12);
            polegar.Margin = new Thickness(x - 6, 0, 0, 0);
            preenchimento.Width = Math.Max(0, x);
        }

        // Chamado sempre que o diagrama, o caso ou a escala mudam.
        public void Atualizar()
        {
            foreach (var it in itens)
            {
                bool on = Ativo(it);
                it.Caixa.Background = on ? TemaPlant.BSup2 : Brushes.Transparent;
                it.Caixa.BorderBrush = on ? TemaPlant.BBorda : Brushes.Transparent;
                it.Texto.Foreground = on ? TemaPlant.BTexto1 : TemaPlant.BTexto2;
                it.Texto.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
            }
            bool temDiag = vista.Diag != Diagrama.Nenhum;
            bool comEscala = vista.Diag == Diagrama.Deformada || vista.Diag == Diagrama.Normal || vista.Diag == Diagrama.Cortante || vista.Diag == Diagrama.Momento;
            linhaEscala.Visibility = comEscala && vista.Resolvido ? Visibility.Visible : Visibility.Collapsed;
            if (comEscala) Posicionar();
            txCaso.Text = caso();
            txCaso.Visibility = (vista.Resolvido || temDiag) && !compacto ? Visibility.Visible : Visibility.Collapsed;

            faixas.Children.Clear();
            string leg = "";
            if (temDiag && !vista.Resolvido) leg = "Sem resultados: calcule a estrutura (F5).";
            else if (vista.Diag == Diagrama.Verificacao)
            {
                leg = vista.MapaCalor ? "⚠ acima de 90%  ·  ✕ não passa" : "Verificação por barra";
                var cores = vista.MapaCalor
                    ? new[] { Tuple.Create(TemaPlant.CorCalor1, "≤ 75"), Tuple.Create(TemaPlant.CorCalor2, "90"), Tuple.Create(TemaPlant.CorCalor3, "100"), Tuple.Create(TemaPlant.CorCalor4, "> 100 %") }
                    : new[] { Tuple.Create(TemaPlant.CorCalor1, "passa"), Tuple.Create(TemaPlant.CorCalor3, "> 90 %"), Tuple.Create(TemaPlant.CorCalor4, "não passa") };
                foreach (var c in cores)
                {
                    faixas.Children.Add(new Border { Width = 18, Height = 6, CornerRadius = new CornerRadius(3), Background = TemaPlant.B(c.Item1), VerticalAlignment = VerticalAlignment.Center });
                    faixas.Children.Add(new TextBlock { Text = c.Item2, FontSize = 10.5, Foreground = TemaPlant.BTexto2, Margin = new Thickness(4, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
                }
            }
            else if (temDiag) leg = Nome(vista.Diag) + "  ·  " + vista.ResumoDiagrama();
            txLegenda.Text = leg;
            txLegenda.Visibility = leg == "" ? Visibility.Collapsed : Visibility.Visible;
            faixas.Visibility = faixas.Children.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        static string Nome(Diagrama d)
        {
            switch (d)
            {
                case Diagrama.Deformada: return "Deformada";
                case Diagrama.Normal: return "Normal N (kN)";
                case Diagrama.Cortante: return "Cortante V (kN)";
                case Diagrama.Momento: return "Momento M (kN·m)";
                default: return "";
            }
        }
    }
}
