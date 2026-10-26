// Inspector: painel de propriedades WPF nativo (substitui o PropertyGrid do WinForms).
// Lê por reflexao (TypeDescriptor) as MESMAS propriedades que o PropertyGrid mostrava: [Browsable], [Category], [DisplayName],
// [Description], TypeConverter (valores padrao) e somente-leitura. Toda edicao grava a propriedade em todos os objetos
// selecionados e avisa o chamador pelo evento Alterado (que roda o mesmo caminho de antes: membros, Confirmar -> desfazer/recalculo).
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Portico;

namespace NovoFtool
{
    public class AcaoRapida
    {
        public string Grupo = "", Texto = "", Icone = "", Dica = "";
        public bool Ativo;
        public Action Executar;
    }

    // ---------- expressoes numericas simples: "2,5+0,3", "(10-2)/4", "-3*1,5" ----------
    public static class ExprNum
    {
        public static bool Avaliar(string s, out double v)
        {
            v = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            string t = s.Replace(',', '.').Replace(" ", "").Replace("−", "-").Replace("×", "*").Replace("÷", "/").Replace(" ", "");
            int pos = 0;
            try
            {
                double r = Soma(t, ref pos);
                if (pos != t.Length || double.IsNaN(r) || double.IsInfinity(r)) return false;
                v = r;
                return true;
            }
            catch { return false; }
        }

        static double Soma(string t, ref int p)
        {
            double a = Produto(t, ref p);
            while (p < t.Length && (t[p] == '+' || t[p] == '-'))
            {
                char op = t[p++];
                double b = Produto(t, ref p);
                a = op == '+' ? a + b : a - b;
            }
            return a;
        }

        static double Produto(string t, ref int p)
        {
            double a = Unario(t, ref p);
            while (p < t.Length && (t[p] == '*' || t[p] == '/'))
            {
                char op = t[p++];
                double b = Unario(t, ref p);
                if (op == '/' && b == 0) throw new DivideByZeroException();
                a = op == '*' ? a * b : a / b;
            }
            return a;
        }

        static double Unario(string t, ref int p)
        {
            if (p < t.Length && t[p] == '-') { p++; return -Unario(t, ref p); }
            if (p < t.Length && t[p] == '+') { p++; return Unario(t, ref p); }
            if (p < t.Length && t[p] == '(')
            {
                p++;
                double r = Soma(t, ref p);
                if (p >= t.Length || t[p] != ')') throw new FormatException();
                p++;
                return r;
            }
            int ini = p;
            while (p < t.Length && (char.IsDigit(t[p]) || t[p] == '.')) p++;
            if (p < t.Length && (t[p] == 'e' || t[p] == 'E') && p > ini)
            {
                int q = p + 1;
                if (q < t.Length && (t[q] == '+' || t[q] == '-')) q++;
                if (q < t.Length && char.IsDigit(t[q])) { while (q < t.Length && char.IsDigit(t[q])) q++; p = q; }
            }
            if (p == ini) throw new FormatException();
            return double.Parse(t.Substring(ini, p - ini), NumberStyles.Float, CultureInfo.InvariantCulture);
        }
    }

    // ---------- miniatura vetorial da secao, desenhada pelos dados do perfil ----------
    public static class MiniaturaPerfil
    {
        static readonly Dictionary<string, ImageSource> cache = new Dictionary<string, ImageSource>();

        public static ImageSource Criar(string nome, double tam)
        {
            string chave = nome + "|" + tam;
            ImageSource r;
            if (cache.TryGetValue(chave, out r)) return r;
            Secao s = null;
            try { s = Biblioteca.AcharSecao(nome); } catch { }
            r = Desenhar(s, tam);
            cache[chave] = r;
            return r;
        }

        static ImageSource Desenhar(Secao s, double tam)
        {
            var corpo = TemaPlant.BTexto2;
            var grupo = new DrawingGroup();
            var geo = new GeometryGroup { FillRule = FillRule.Nonzero };
            Pen contorno = null;
            double H = s != null && s.H > 0 ? s.H : 1, B = s != null && s.Bf > 0 ? s.Bf : H;
            string forma = s != null ? s.Forma : "GEN";
            double esc = (tam - 2) / Math.Max(H, B);
            double h = H * esc, b = B * esc;
            double x0 = (tam - b) / 2, y0 = (tam - h) / 2;
            Func<double, double> esp = m => Math.Max(m * esc, 1.5);
            double tw = s != null ? esp(s.Tw > 0 ? s.Tw : (s.Tf > 0 ? s.Tf : 0.004)) : 2, tf = s != null ? esp(s.Tf > 0 ? s.Tf : (s.Tw > 0 ? s.Tw : 0.004)) : 2;
            switch (forma)
            {
                case "I":
                    geo.Children.Add(new RectangleGeometry(new Rect(x0, y0, b, tf)));
                    geo.Children.Add(new RectangleGeometry(new Rect(x0, y0 + h - tf, b, tf)));
                    geo.Children.Add(new RectangleGeometry(new Rect(x0 + (b - tw) / 2, y0, tw, h)));
                    break;
                case "U":
                    geo.Children.Add(new RectangleGeometry(new Rect(x0, y0, tw, h)));
                    geo.Children.Add(new RectangleGeometry(new Rect(x0, y0, b, tf)));
                    geo.Children.Add(new RectangleGeometry(new Rect(x0, y0 + h - tf, b, tf)));
                    break;
                case "L":
                    geo.Children.Add(new RectangleGeometry(new Rect(x0, y0, tw, h)));
                    geo.Children.Add(new RectangleGeometry(new Rect(x0, y0 + h - tw, b, tw)));
                    break;
                case "TR":
                    geo.FillRule = FillRule.EvenOdd;
                    geo.Children.Add(new RectangleGeometry(new Rect(x0, y0, b, h), 1.5, 1.5));
                    geo.Children.Add(new RectangleGeometry(new Rect(x0 + tw, y0 + tw, Math.Max(b - 2 * tw, 0.5), Math.Max(h - 2 * tw, 0.5))));
                    break;
                case "TC":
                    geo.FillRule = FillRule.EvenOdd;
                    geo.Children.Add(new EllipseGeometry(new Rect(x0, y0, b, h)));
                    geo.Children.Add(new EllipseGeometry(new Rect(x0 + tw, y0 + tw, Math.Max(b - 2 * tw, 0.5), Math.Max(h - 2 * tw, 0.5))));
                    break;
                case "RET":
                    geo.Children.Add(new RectangleGeometry(new Rect(x0, y0, b, h)));
                    break;
                case "CIRC":
                    geo.Children.Add(new EllipseGeometry(new Rect(x0, y0, b, h)));
                    break;
                default:
                    double l = tam - 6;
                    geo.Children.Add(new RectangleGeometry(new Rect(3, 3, l, l), 2, 2));
                    contorno = new Pen(TemaPlant.BTexto3, 1.2) { DashStyle = DashStyles.Dash };
                    contorno.Freeze();
                    break;
            }
            geo.Freeze();
            grupo.Children.Add(new GeometryDrawing(new SolidColorBrush(Colors.Transparent), null, new RectangleGeometry(new Rect(0, 0, tam, tam))));
            grupo.Children.Add(new GeometryDrawing(contorno == null ? corpo : null, contorno, geo));
            grupo.Freeze();
            var di = new DrawingImage(grupo);
            di.Freeze();
            return di;
        }
    }

    // ---------- contexto para os TypeConverter (ListaSecoes olha Instance) ----------
    sealed class ContextoProp : ITypeDescriptorContext
    {
        readonly object inst; readonly PropertyDescriptor pd;
        public ContextoProp(IList<object> alvos, PropertyDescriptor pd) { inst = alvos.Count == 1 ? alvos[0] : alvos.ToArray(); this.pd = pd; }
        public IContainer Container { get { return null; } }
        public object Instance { get { return inst; } }
        public PropertyDescriptor PropertyDescriptor { get { return pd; } }
        public bool OnComponentChanging() { return true; }
        public void OnComponentChanged() { }
        public object GetService(Type t) { return null; }
    }

    // ---------- controles do Inspector ----------

    // Pilula segmentada (Sim/Nao, apoios...). Indice -1 = nenhum ativo (valores diferentes na multi-selecao).
    public class Segmentado : Border
    {
        readonly List<Border> itens = new List<Border>();
        readonly List<TextBlock> textos = new List<TextBlock>();
        int sel = -1;
        public event Action<int> Escolheu;

        public Segmentado(params string[][] defs) : this(false, defs) { }

        public Segmentado(bool compacto, string[][] defs)   // cada def: { texto, icone (opcional), dica (opcional) }
        {
            Background = TemaPlant.BSup0; BorderBrush = TemaPlant.BBordaSutil; BorderThickness = new Thickness(1);
            CornerRadius = new CornerRadius(5); Padding = new Thickness(2); Height = 28; Focusable = true; HorizontalAlignment = HorizontalAlignment.Left;
            FocusVisualStyle = null;
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            Child = sp;
            for (int i = 0; i < defs.Length; i++)
            {
                int idx = i;
                string[] d = defs[i];
                var linha = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                bool comIcone = d.Length > 1 && !string.IsNullOrEmpty(d[1]) && Icones.Existe(d[1]);
                if (comIcone) { var ic = Icones.Criar(d[1], 16); ((FrameworkElement)ic).Margin = new Thickness(0, 0, compacto ? 4 : 5, 0); linha.Children.Add(ic); }
                var tx = new TextBlock { Text = d[0], FontSize = compacto ? 11.5 : 12, VerticalAlignment = VerticalAlignment.Center, Foreground = TemaPlant.BTexto2 };
                linha.Children.Add(tx);
                var it = new Border
                {
                    CornerRadius = new CornerRadius(3), Padding = new Thickness(comIcone ? (compacto ? 5 : 8) : 12, 0, comIcone ? (compacto ? 7 : 10) : 12, 0), BorderThickness = new Thickness(1),
                    BorderBrush = Brushes.Transparent, Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = linha, MinWidth = comIcone ? 0 : 40
                };
                if (d.Length > 2 && !string.IsNullOrEmpty(d[2])) it.ToolTip = d[2];
                it.MouseLeftButtonDown += (s, e) => { Focus(); Escolheu?.Invoke(idx); e.Handled = true; };
                it.MouseEnter += (s, e) => { if (idx != sel) it.Background = TemaPlant.BSup2; };
                it.MouseLeave += (s, e) => { if (idx != sel) it.Background = Brushes.Transparent; };
                sp.Children.Add(it); itens.Add(it); textos.Add(tx);
            }
            GotKeyboardFocus += (s, e) => BorderBrush = TemaPlant.BBordaFoco;
            LostKeyboardFocus += (s, e) => BorderBrush = TemaPlant.BBordaSutil;
            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Left && sel > 0) { Escolheu?.Invoke(sel - 1); e.Handled = true; }
                else if (e.Key == Key.Right && sel < itens.Count - 1) { Escolheu?.Invoke(sel + 1); e.Handled = true; }
                else if (e.Key == Key.Space || e.Key == Key.Enter) { Escolheu?.Invoke((Math.Max(sel, -1) + 1) % itens.Count); e.Handled = true; }
            };
        }

        public void Definir(int i)
        {
            sel = i;
            for (int k = 0; k < itens.Count; k++)
            {
                bool a = k == i;
                itens[k].Background = a ? TemaPlant.BAcentoSutil : Brushes.Transparent;
                itens[k].BorderBrush = a ? TemaPlant.BAcento : Brushes.Transparent;
                textos[k].Foreground = a ? TemaPlant.BTexto1 : TemaPlant.BTexto2;
                textos[k].FontWeight = a ? FontWeights.SemiBold : FontWeights.Normal;
            }
        }
    }

    // Campo numerico com unidade integrada: Enter aplica, Esc cancela, setas incrementam, aceita expressao e virgula/ponto.
    public class CampoNumerico : Grid
    {
        readonly TextBox tb;
        readonly Border err;
        readonly TextBlock un, varios;
        bool interno, sujo;
        public Func<string, bool> Confirmou;          // devolve false se o texto for invalido
        public Action<int, ModifierKeys> Passo;        // +1 / -1
        public Action Cancelou;

        public string Texto { get { return tb.Text; } }
        public bool TemFoco { get { return tb.IsKeyboardFocusWithin; } }

        public CampoNumerico(string unidade)
        {
            Height = 28;
            tb = new TextBox { TextAlignment = TextAlignment.Right, Height = 28, VerticalContentAlignment = VerticalAlignment.Center };
            un = new TextBlock { Text = unidade ?? "", FontSize = 11, Foreground = TemaPlant.BTexto2, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 7, 0), IsHitTestVisible = false };
            un.Measure(new Size(200, 28));
            // o TextBox do tema aplica o Padding duas vezes (template + texto): metade em cada um dá o recuo desejado
            tb.Padding = new Thickness(3, 0, ((string.IsNullOrEmpty(unidade) ? 0 : un.DesiredSize.Width) + 6) / 2, 0);
            varios = new TextBlock { Text = "(vários)", FontSize = 11.5, FontStyle = FontStyles.Italic, Foreground = TemaPlant.BTexto3, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, string.IsNullOrEmpty(unidade) ? 8 : un.DesiredSize.Width + 12, 0), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
            err = new Border { BorderBrush = TemaPlant.BErro, BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(4), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
            Children.Add(tb); Children.Add(varios); Children.Add(un); Children.Add(err);

            tb.TextChanged += (s, e) => { if (!interno) { sujo = true; LimparErro(); varios.Visibility = Visibility.Collapsed; } };
            tb.GotKeyboardFocus += (s, e) => tb.Dispatcher.BeginInvoke(new Action(() => tb.SelectAll()), System.Windows.Threading.DispatcherPriority.Input);
            tb.PreviewMouseLeftButtonDown += (s, e) => { if (!tb.IsKeyboardFocusWithin) { e.Handled = true; tb.Focus(); } };
            tb.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { Aplicar(false); e.Handled = true; }
                else if (e.Key == Key.Escape) { Reverter(); e.Handled = true; }
                else if (e.Key == Key.Up || e.Key == Key.Down)
                {
                    Passo?.Invoke(e.Key == Key.Up ? 1 : -1, Keyboard.Modifiers);
                    e.Handled = true;
                }
            };
            tb.LostKeyboardFocus += (s, e) => { if (sujo) Aplicar(true); };
        }

        void Aplicar(bool aoSair)
        {
            string t = tb.Text;
            if (!sujo && !aoSair) { tb.SelectAll(); return; }
            if (string.IsNullOrWhiteSpace(t)) { Reverter(); return; }
            sujo = false;
            bool ok = Confirmou == null || Confirmou(t);
            if (!ok) { sujo = true; if (aoSair) Reverter(); }
        }

        void Reverter()
        {
            sujo = false; LimparErro();
            Cancelou?.Invoke();
        }

        public void Definir(string texto, bool misto)
        {
            if (sujo && tb.IsKeyboardFocusWithin) return;
            interno = true;
            tb.Text = misto ? "" : (texto ?? "");
            interno = false;
            sujo = false;
            varios.Visibility = misto ? Visibility.Visible : Visibility.Collapsed;
            LimparErro();
        }

        public void ForcarTexto(string texto)   // para testes e atalhos: escreve e confirma como se tivesse digitado
        {
            interno = true; tb.Text = texto; interno = false; sujo = true;
            Aplicar(false);
        }

        public void MostrarErro(string msg)
        {
            if (err.Visibility != Visibility.Visible) tooltipBase = ToolTip;
            err.Visibility = Visibility.Visible;
            ToolTip = msg;
        }

        public void LimparErro()
        {
            if (err.Visibility == Visibility.Visible) { err.Visibility = Visibility.Collapsed; ToolTip = tooltipBase; }
        }

        object tooltipBase;

        public void Descartar() { sujo = false; }
    }

    // ---------- linhas (uma por propriedade) ----------

    abstract class Linha
    {
        public Inspector P;
        public PropertyDescriptor Pd;
        public string Rotulo = "", Unidade = "";
        public FrameworkElement Editor;
        public bool Misto;
        protected object Valor;

        protected IList<object> Alvos { get { return P.Alvos; } }

        protected void LerValor()
        {
            object v0 = null; Misto = false;
            for (int i = 0; i < Alvos.Count; i++)
            {
                object v = Pd.GetValue(Alvos[i]);
                if (i == 0) v0 = v; else if (!Equals(v, v0)) Misto = true;
            }
            Valor = v0;
        }

        protected bool Aplicar(object novo)
        {
            bool mudou = false;
            try
            {
                foreach (object t in Alvos)
                {
                    if (!Equals(Pd.GetValue(t), novo)) { Pd.SetValue(t, novo); mudou = true; }
                }
            }
            catch (Exception) { Atualizar(); return false; }
            if (mudou) P.Notificar(Pd.DisplayName);
            Atualizar();
            return true;
        }

        public abstract void Atualizar();
        public abstract bool DefinirTexto(string t);
    }

    sealed class LinhaNum : Linha
    {
        readonly CampoNumerico campo; readonly bool inteiro;
        public LinhaNum(Inspector p, PropertyDescriptor pd, string unidade)
        {
            P = p; Pd = pd; Unidade = unidade; inteiro = pd.PropertyType == typeof(int);
            campo = new CampoNumerico(unidade) { HorizontalAlignment = HorizontalAlignment.Stretch };
            Editor = campo;
            campo.Confirmou = t => DefinirTexto(t);
            campo.Cancelou = () => Atualizar();
            campo.Passo = (dir, mod) =>
            {
                double v;
                if (!ExprNum.Avaliar(campo.Texto, out v)) { LerValor(); v = Misto ? 0 : Convert.ToDouble(Valor); }
                double passo = Unidade == "m" ? 0.1 : 1;
                if ((mod & ModifierKeys.Shift) != 0) passo *= 10;
                if ((mod & ModifierKeys.Control) != 0) passo /= 10;
                campo.Descartar();
                Aplicar(Conv(Math.Round(v + dir * passo, 6)));
            };
        }

        object Conv(double v) { return inteiro ? (object)(int)Math.Round(v) : v; }

        public static string Fmt(double v)
        {
            if (Math.Abs(v) < 1e-12) v = 0;
            return Math.Round(v, 6).ToString("0.######", CultureInfo.GetCultureInfo("pt-BR"));
        }

        public override void Atualizar()
        {
            LerValor();
            campo.Definir(Misto ? "" : Fmt(Convert.ToDouble(Valor)), Misto);
        }

        public override bool DefinirTexto(string t)
        {
            double v;
            if (!ExprNum.Avaliar(t, out v)) { campo.MostrarErro("Valor inválido. Digite um número ou uma expressão simples, como 2,5+0,3."); return false; }
            campo.LimparErro();
            return Aplicar(Conv(v));
        }

        public void ForcarTexto(string t) { campo.ForcarTexto(t); }
    }

    sealed class LinhaSimNao : Linha
    {
        readonly Segmentado seg;
        public LinhaSimNao(Inspector p, PropertyDescriptor pd)
        {
            P = p; Pd = pd;
            seg = new Segmentado(new[] { "Sim" }, new[] { "Não" });
            Editor = seg;
            seg.Escolheu += i => Aplicar(i == 0);
        }
        public override void Atualizar() { LerValor(); seg.Definir(Misto ? -1 : (bool)Valor ? 0 : 1); }
        public override bool DefinirTexto(string t) { return Aplicar(t.Trim().ToLowerInvariant().StartsWith("s")); }
    }

    sealed class LinhaCombo : Linha
    {
        readonly ComboBox cb; readonly bool exclusivo; readonly TextBlock varios;
        bool interno; List<string> itens = new List<string>();
        public LinhaCombo(Inspector p, PropertyDescriptor pd, bool exclusivo)
        {
            P = p; Pd = pd; this.exclusivo = exclusivo;
            cb = new ComboBox { Height = 28, IsEditable = !exclusivo, VerticalContentAlignment = VerticalAlignment.Center };
            varios = new TextBlock { Text = "(vários)", FontSize = 11.5, FontStyle = FontStyles.Italic, Foreground = TemaPlant.BTexto3, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(9, 0, 0, 0), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
            var g = new Grid(); g.Children.Add(cb); g.Children.Add(varios);
            Editor = g;
            cb.SelectionChanged += (s, e) => { if (interno || e.AddedItems.Count == 0) return; Aplicar(e.AddedItems[0] as string); };
            if (!exclusivo)
            {
                cb.AddHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler((s, e) =>
                {
                    if (e.Key == Key.Enter && !cb.IsDropDownOpen) { Texto(cb.Text); e.Handled = true; }
                    else if (e.Key == Key.Escape && !cb.IsDropDownOpen) { Atualizar(); e.Handled = true; }
                }), true);
                cb.LostKeyboardFocus += (s, e) => { if (!cb.IsKeyboardFocusWithin && !interno) Texto(cb.Text); };
                cb.GotKeyboardFocus += (s, e) => varios.Visibility = Visibility.Collapsed;
            }
        }

        void Texto(string t)
        {
            t = (t ?? "").Trim();
            if (t == "" || t == "(vários)") { Atualizar(); return; }
            LerValor();
            if (!Misto && Equals(Valor, t)) return;
            Aplicar(t);
        }

        public override void Atualizar()
        {
            LerValor();
            var conv = Pd.Converter;
            var ctx = new ContextoProp(Alvos, Pd);
            var lista = new List<string>();
            if (conv.GetStandardValuesSupported(ctx))
                foreach (object o in conv.GetStandardValues(ctx)) lista.Add(Convert.ToString(o));
            interno = true;
            if (!lista.SequenceEqual(itens)) { itens = lista; cb.ItemsSource = lista; }
            string atual = Misto ? null : Valor as string;
            if (exclusivo) cb.SelectedItem = atual; else { cb.SelectedItem = null; cb.Text = atual ?? ""; }
            interno = false;
            varios.Visibility = Misto ? Visibility.Visible : Visibility.Collapsed;
        }

        public override bool DefinirTexto(string t) { return Aplicar(t); }
    }

    sealed class LinhaTexto : Linha
    {
        readonly TextBox tb; bool interno, sujo;
        public LinhaTexto(Inspector p, PropertyDescriptor pd)
        {
            P = p; Pd = pd;
            tb = new TextBox { Height = 28, VerticalContentAlignment = VerticalAlignment.Center };
            Editor = tb;
            tb.TextChanged += (s, e) => { if (!interno) sujo = true; };
            tb.GotKeyboardFocus += (s, e) => tb.Dispatcher.BeginInvoke(new Action(() => tb.SelectAll()), System.Windows.Threading.DispatcherPriority.Input);
            tb.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { Gravar(); e.Handled = true; }
                else if (e.Key == Key.Escape) { sujo = false; Atualizar(); e.Handled = true; }
            };
            tb.LostKeyboardFocus += (s, e) => { if (sujo) Gravar(); };
        }
        void Gravar() { if (!sujo) return; sujo = false; Aplicar(tb.Text.Trim()); }
        public override void Atualizar()
        {
            if (sujo && tb.IsKeyboardFocusWithin) return;
            LerValor();
            interno = true; tb.Text = Misto ? "" : (Valor as string ?? ""); interno = false; sujo = false;
            tb.ToolTip = Misto ? "Valores diferentes entre os itens selecionados (vários)." : null;
        }
        public override bool DefinirTexto(string t) { sujo = false; return Aplicar(t.Trim()); }
    }

    sealed class LinhaLeitura : Linha
    {
        readonly TextBlock tx, un;
        public LinhaLeitura(Inspector p, PropertyDescriptor pd, string unidade)
        {
            P = p; Pd = pd; Unidade = unidade;
            tx = new TextBlock { FontFamily = TemaPlant.Fonte, FontSize = 12, Foreground = TemaPlant.BTexto1, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            Typography.SetNumeralAlignment(tx, FontNumeralAlignment.Tabular);
            un = new TextBlock { Text = unidade ?? "", FontSize = 11, Foreground = TemaPlant.BTexto2, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 0, 0) };
            var sp = new StackPanel { Orientation = Orientation.Horizontal, MinHeight = 24, VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(tx); sp.Children.Add(un);
            Editor = sp;
        }
        public override void Atualizar()
        {
            LerValor();
            string s = Misto ? "(vários)" : Convert.ToString(Valor);
            bool vazio = string.IsNullOrWhiteSpace(s);
            tx.Text = vazio ? "—" : s;
            tx.Foreground = vazio ? TemaPlant.BTextoMudo : Misto ? TemaPlant.BTexto3 : TemaPlant.BTexto1;
            un.Visibility = vazio || Misto ? Visibility.Collapsed : Visibility.Visible;
        }
        public override bool DefinirTexto(string t) { return false; }
    }

    // Seletor de perfil: botao com miniatura + popup com busca e lista (virtualizada) com miniaturas.
    // O mesmo controle no inspetor (LinhaPerfil) e no projetista de ligações (JanelaLigacaoProjeto).
    public sealed class SeletorPerfil : Grid
    {
        readonly Func<IEnumerable<string>> fonte; readonly Func<string> atual; readonly Action<string> escolher;
        public bool AceitaDigitado = true;     // "Ret 20x50", "Circ 30"... (só o pórtico entende; o motor de ligações não)
        public string RotuloContagem = " nesta família";
        readonly Border botao; readonly Image mini; readonly TextBlock nome; readonly Popup pop; readonly TextBox busca; readonly ListBox lista; readonly TextBlock contagem;
        List<string> todos = new List<string>();

        sealed class ConvMini : IValueConverter
        {
            public object Convert(object v, Type t, object p, CultureInfo c) { return MiniaturaPerfil.Criar(v as string, 20); }
            public object ConvertBack(object v, Type t, object p, CultureInfo c) { throw new NotSupportedException(); }
        }

        public SeletorPerfil(Func<IEnumerable<string>> fonte, Func<string> atual, Action<string> escolher)
        {
            this.fonte = fonte; this.atual = atual; this.escolher = escolher;
            mini = new Image { Width = 22, Height = 22, Margin = new Thickness(5, 0, 6, 0), SnapsToDevicePixels = true };
            nome = new TextBlock { FontSize = 12, Foreground = TemaPlant.BTexto1, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var seta = new Path { Data = Geometry.Parse("M0,0 L4,4 L8,0"), Stroke = TemaPlant.BTexto2, StrokeThickness = 1.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 8, 0) };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(nome, 1); Grid.SetColumn(seta, 2);
            g.Children.Add(mini); g.Children.Add(nome); g.Children.Add(seta);
            botao = new Border { Height = 28, Background = TemaPlant.BSup3, BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Cursor = Cursors.Hand, Child = g, Focusable = true, FocusVisualStyle = null };
            botao.MouseEnter += (s, e) => botao.BorderBrush = TemaPlant.BBorda;
            botao.MouseLeave += (s, e) => botao.BorderBrush = botao.IsKeyboardFocusWithin || pop.IsOpen ? TemaPlant.BBordaFoco : TemaPlant.BBordaSutil;
            botao.GotKeyboardFocus += (s, e) => botao.BorderBrush = TemaPlant.BBordaFoco;
            botao.LostKeyboardFocus += (s, e) => { if (!pop.IsOpen) botao.BorderBrush = TemaPlant.BBordaSutil; };
            botao.MouseLeftButtonDown += (s, e) => { botao.Focus(); Abrir(); e.Handled = true; };
            botao.KeyDown += (s, e) => { if (e.Key == Key.Enter || e.Key == Key.Space || e.Key == Key.Down) { Abrir(); e.Handled = true; } };

            busca = new TextBox { Height = 28, Margin = new Thickness(0, 0, 0, 4), VerticalContentAlignment = VerticalAlignment.Center };
            var dicaBusca = new TextBlock { Text = "Buscar perfil ou digitar (ex.: Ret 20x50)", FontSize = 11.5, Foreground = TemaPlant.BTextoMudo, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 4) };
            var gb = new Grid(); gb.Children.Add(busca); gb.Children.Add(dicaBusca);
            contagem = new TextBlock { FontSize = 11, Foreground = TemaPlant.BTexto3, Margin = new Thickness(2, 0, 0, 3) };
            lista = new ListBox { MaxHeight = 264, BorderThickness = new Thickness(0), Background = Brushes.Transparent };
            ScrollViewer.SetHorizontalScrollBarVisibility(lista, ScrollBarVisibility.Disabled);
            VirtualizingPanel.SetIsVirtualizing(lista, true);
            VirtualizingPanel.SetVirtualizationMode(lista, VirtualizationMode.Recycling);
            var fi = new FrameworkElementFactory(typeof(Image));
            fi.SetBinding(Image.SourceProperty, new Binding(".") { Converter = new ConvMini() });
            fi.SetValue(FrameworkElement.WidthProperty, 20.0); fi.SetValue(FrameworkElement.HeightProperty, 20.0);
            fi.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 0, 8, 0));
            var ft = new FrameworkElementFactory(typeof(TextBlock));
            ft.SetBinding(TextBlock.TextProperty, new Binding("."));
            ft.SetValue(TextBlock.FontSizeProperty, 12.0);
            ft.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            var fp = new FrameworkElementFactory(typeof(StackPanel));
            fp.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            fp.AppendChild(fi); fp.AppendChild(ft);
            lista.ItemTemplate = new DataTemplate { VisualTree = fp };
            lista.PreviewMouseLeftButtonUp += (s, e) =>
            {
                var it = ItemsControl.ContainerFromElement(lista, e.OriginalSource as DependencyObject) as ListBoxItem;
                if (it != null) Escolher(it.Content as string);
            };
            busca.TextChanged += (s, e) => { dicaBusca.Visibility = busca.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; Filtrar(); };
            busca.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Down) { lista.SelectedIndex = Math.Min(lista.SelectedIndex + 1, lista.Items.Count - 1); lista.ScrollIntoView(lista.SelectedItem); e.Handled = true; }
                else if (e.Key == Key.Up) { lista.SelectedIndex = Math.Max(lista.SelectedIndex - 1, 0); lista.ScrollIntoView(lista.SelectedItem); e.Handled = true; }
                else if (e.Key == Key.Enter) { Escolher((lista.SelectedItem ?? (lista.Items.Count > 0 ? lista.Items[0] : null)) as string); e.Handled = true; }
                else if (e.Key == Key.Escape) { pop.IsOpen = false; botao.Focus(); e.Handled = true; }
            };
            var sp = new StackPanel { Margin = new Thickness(0) };
            sp.Children.Add(gb); sp.Children.Add(contagem); sp.Children.Add(lista);
            var caixa = new Border
            {
                Background = TemaPlant.BSup2, BorderBrush = TemaPlant.BBorda, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(6),
                Margin = new Thickness(0, 2, 8, 8), Child = sp, MinWidth = 280,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 4, Opacity = 0.45, Color = Colors.Black, Direction = 270 }
            };
            pop = new Popup { PlacementTarget = botao, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, Child = caixa, PopupAnimation = PopupAnimation.None };
            pop.Opened += (s, e) => botao.BorderBrush = TemaPlant.BBordaFoco;
            pop.Closed += (s, e) => botao.BorderBrush = botao.IsKeyboardFocusWithin ? TemaPlant.BBordaFoco : TemaPlant.BBordaSutil;
            Children.Add(botao); Children.Add(pop);
        }

        public FrameworkElement RetirarConteudo() { var c = (FrameworkElement)pop.Child; pop.Child = null; return c; }
        public void DevolverConteudo(FrameworkElement c) { pop.Child = c; }

        public void Abrir()
        {
            todos = fonte().ToList();
            ((FrameworkElement)pop.Child).Width = Math.Max(280, botao.ActualWidth + 8);
            busca.Text = "";
            Filtrar();
            if (atual() is string v) { lista.SelectedItem = v; if (lista.SelectedItem != null) lista.ScrollIntoView(lista.SelectedItem); }
            pop.IsOpen = true;
            busca.Dispatcher.BeginInvoke(new Action(() => { busca.Focus(); Keyboard.Focus(busca); }), System.Windows.Threading.DispatcherPriority.Input);
        }

        public void Preparar(string textoBusca)
        {
            todos = fonte().ToList();
            ((FrameworkElement)pop.Child).Width = 296;
            busca.Text = textoBusca ?? "";
            Filtrar();
            if (atual() is string v) lista.SelectedItem = v;
        }

        public void Filtrar()
        {
            string q = (busca.Text ?? "").Trim();
            var toks = q.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var r = todos.Where(n => toks.All(t => n.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
            // texto digitado que e uma secao valida (Ret 20x50, Circ 30, A=.. I=..): oferece usar como esta
            if (AceitaDigitado && q.Length > 0 && !todos.Any(n => string.Equals(n, q, StringComparison.OrdinalIgnoreCase)))
            {
                Secao s = null; try { s = Biblioteca.AcharSecao(q); } catch { }
                if (s != null) r.Insert(0, q);
            }
            lista.ItemsSource = r;
            contagem.Text = r.Count + (r.Count == 1 ? " perfil" : " perfis") + (q.Length == 0 ? RotuloContagem : "");
            if (r.Count > 0 && q.Length > 0) lista.SelectedIndex = 0;
        }

        void Escolher(string n)
        {
            if (string.IsNullOrEmpty(n)) return;
            pop.IsOpen = false;
            escolher(n);
            botao.Focus();
        }

        // capturas/testes: escolhe o primeiro da lista filtrada, como Enter na busca
        public void EscolherPrimeiro() => Escolher(lista.Items.Count > 0 ? lista.Items[0] as string : null);

        // mostra o perfil no botão; misto = vários itens selecionados com perfis diferentes
        public void Mostrar(string n, bool misto = false)
        {
            if (misto) { nome.Text = "(vários)"; nome.FontStyle = FontStyles.Italic; nome.Foreground = TemaPlant.BTexto3; mini.Source = MiniaturaPerfil.Criar(null, 22); botao.ToolTip = "Perfis diferentes entre os itens selecionados."; }
            else
            {
                nome.Text = Inspector.NomeBonito(n); nome.FontStyle = FontStyles.Normal; nome.Foreground = TemaPlant.BTexto1;
                mini.Source = MiniaturaPerfil.Criar(n, 22);
                Secao s = null; try { s = Biblioteca.AcharSecao(n); } catch { }
                botao.ToolTip = s != null ? s.Descricao + (s.Massa > 0 ? " — " + Num.Fmt(s.Massa, 1) + " kg/m" : "") + ". Clique para trocar." : "Perfil não reconhecido: " + n;
            }
        }

    }

    sealed class LinhaPerfil : Linha
    {
        readonly SeletorPerfil sel;

        public LinhaPerfil(Inspector p, PropertyDescriptor pd)
        {
            P = p; Pd = pd;
            sel = new SeletorPerfil(() => Pd.Converter.GetStandardValues(new ContextoProp(Alvos, Pd)).Cast<object>().Select(o => Convert.ToString(o)),
                                    () => { LerValor(); return Misto ? null : Valor as string; }, n => Aplicar(n));
            Editor = sel;
        }

        public FrameworkElement RetirarConteudo() => sel.RetirarConteudo();
        public void DevolverConteudo(FrameworkElement c) => sel.DevolverConteudo(c);
        public void Abrir() => sel.Abrir();
        public void Preparar(string textoBusca) => sel.Preparar(textoBusca);

        public override void Atualizar() { LerValor(); sel.Mostrar(Misto ? null : Valor as string, Misto); }

        public override bool DefinirTexto(string t) { return Aplicar(t.Trim()); }
    }

    // ---------- o painel ----------
    public class Inspector : UserControl
    {
        static readonly HashSet<string> recolhidas = new HashSet<string> { "Resultados" };
        static readonly Regex RxUnidade = new Regex(@"^(.*?)\s*\(([^()]*)\)\s*$", RegexOptions.Compiled);
        static readonly Regex RxNumeracao = new Regex(@"^\d+\.\s*", RegexOptions.Compiled);

        public event Action<IList<object>, string> Alterado;
        public Func<Barra, Verif> Verificar;
        public Func<IList<object>, IEnumerable<AcaoRapida>> Acoes;

        readonly ScrollViewer rolagem;
        readonly Border conteudo;
        readonly StackPanel cabecalho, secoes;
        Modelo modelo;
        List<object> alvos = new List<object>();
        readonly List<Linha> linhas = new List<Linha>();
        bool selecaoVazia;

        internal IList<object> Alvos { get { return alvos; } }

        public Inspector()
        {
            FontFamily = TemaPlant.Fonte; FontSize = 12; UseLayoutRounding = true; SnapsToDevicePixels = true;
            Background = TemaPlant.BSup1;
            cabecalho = new StackPanel();
            secoes = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            var interno = new StackPanel();
            interno.Children.Add(cabecalho); interno.Children.Add(secoes);
            conteudo = new Border { Padding = new Thickness(12, 10, 12, 12), Child = interno, Background = TemaPlant.BSup1 };
            rolagem = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = conteudo, Background = TemaPlant.BSup1 };
            Content = rolagem;
        }

        internal void Notificar(string rotulo) { Alterado?.Invoke(alvos, rotulo); }

        public static string NomeBonito(string secao)
        {
            if (string.IsNullOrEmpty(secao)) return "—";
            return Regex.Replace(secao, @"(?<=[\d\s])x(?=[\d\s])", "×");
        }

        // ---- API ----
        public void Mostrar(Modelo m, IList<object> sel)
        {
            var novos = sel == null || sel.Count == 0 ? new List<object> { m } : sel.ToList();
            bool igual = novos.Count == alvos.Count && modelo == m && novos.Zip(alvos, (a, b) => ReferenceEquals(a, b)).All(x => x) && linhas.Count > 0;
            modelo = m; alvos = novos; selecaoVazia = sel == null || sel.Count == 0;
            if (igual) foreach (var l in linhas) l.Atualizar();
            else { Reconstruir(); rolagem.ScrollToTop(); }
            MontarCabecalho();
        }

        // Para testes e atalhos: escreve o texto no editor da propriedade (pelo nome da propriedade) e confirma como se tivesse digitado.
        public bool DefinirTexto(string propriedade, string texto)
        {
            var l = linhas.FirstOrDefault(x => x.Pd.Name == propriedade);
            if (l == null) return false;
            var n = l as LinhaNum;
            if (n != null) { n.ForcarTexto(texto); return true; }
            return l.DefinirTexto(texto);
        }

        public IEnumerable<string> NomesDosCampos { get { return linhas.Select(l => l.Pd.Name); } }
        public IEnumerable<string> RotulosDosCampos { get { return linhas.Select(l => l.Pd.DisplayName); } }

        public bool TrocarPerfil()
        {
            var l = linhas.OfType<LinhaPerfil>().FirstOrDefault();
            if (l == null) return false;
            l.Abrir();
            return true;
        }

        // Conteudo do popup de perfis (popups nao entram na captura da janela, entao a captura o renderiza a parte).
        public FrameworkElement ConteudoSeletorPerfil(string busca)
        {
            var l = linhas.OfType<LinhaPerfil>().FirstOrDefault();
            if (l == null) return null;
            l.Preparar(busca);
            return l.RetirarConteudo();
        }

        public void DevolverSeletorPerfil(FrameworkElement conteudoPopup)
        {
            var l = linhas.OfType<LinhaPerfil>().FirstOrDefault();
            if (l != null) l.DevolverConteudo(conteudoPopup);
        }

        // Imagem do conteudo inteiro (inclusive o que esta abaixo da dobra) para revisao visual.
        public BitmapSource Renderizar()
        {
            conteudo.UpdateLayout();
            int w = Math.Max(1, (int)Math.Ceiling(conteudo.ActualWidth)), h = Math.Max(1, (int)Math.Ceiling(conteudo.ActualHeight));
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            var fundo = new DrawingVisual();
            using (var dc = fundo.RenderOpen()) dc.DrawRectangle(TemaPlant.BSup1, null, new Rect(0, 0, w, h));
            rtb.Render(fundo);
            rtb.Render(conteudo);
            return rtb;
        }

        // ---- reflexao ----
        List<PropertyDescriptor> Propriedades()
        {
            var attrs = new Attribute[] { BrowsableAttribute.Yes };
            var r = new List<PropertyDescriptor>();
            foreach (PropertyDescriptor pd in TypeDescriptor.GetProperties(alvos[0], attrs))
            {
                bool comum = true;
                for (int i = 1; i < alvos.Count && comum; i++)
                {
                    var o = TypeDescriptor.GetProperties(alvos[i], attrs)[pd.Name];
                    comum = o != null && o.PropertyType == pd.PropertyType;
                }
                if (comum) r.Add(pd);
            }
            return r;
        }

        static string Estilizar(string categoria)
        {
            string c = RxNumeracao.Replace(categoria ?? "", "");
            return string.IsNullOrWhiteSpace(c) ? "Outros" : c;
        }

        Linha CriarLinha(PropertyDescriptor pd, out string rotulo, out string unidade)
        {
            string dn = pd.DisplayName;
            rotulo = dn; unidade = "";
            Type t = pd.PropertyType;
            bool numero = t == typeof(double) || t == typeof(int);
            bool leitura = pd.IsReadOnly;
            if (numero || leitura)
            {
                var m = RxUnidade.Match(dn);
                if (m.Success) { rotulo = m.Groups[1].Value; unidade = m.Groups[2].Value.Replace("kN.m", "kN·m"); }
            }
            var ctx = new ContextoProp(alvos, pd);
            var conv = pd.Converter;
            bool padrao = false, exclusivo = false;
            try { padrao = conv != null && conv.GetStandardValuesSupported(ctx); exclusivo = padrao && conv.GetStandardValuesExclusive(ctx); } catch { }
            Linha l;
            if (leitura) l = new LinhaLeitura(this, pd, unidade);
            else if (pd.Name == "Secao" && padrao) l = new LinhaPerfil(this, pd);
            else if (t == typeof(bool)) l = new LinhaSimNao(this, pd);
            else if (padrao && t == typeof(string)) l = new LinhaCombo(this, pd, exclusivo);
            else if (numero) l = new LinhaNum(this, pd, unidade);
            else l = new LinhaTexto(this, pd);
            l.Rotulo = rotulo; l.Unidade = unidade;
            return l;
        }

        void Reconstruir()
        {
            secoes.Children.Clear();
            linhas.Clear();
            var props = Propriedades();
            var grupos = props.GroupBy(p => p.Category ?? "").OrderBy(g => g.Key, StringComparer.Ordinal);
            foreach (var g in grupos)
            {
                string nome = Estilizar(g.Key);
                var painel = new StackPanel();
                var ls = new List<Linha>();
                foreach (var pd in g)
                {
                    string rot, un;
                    var l = CriarLinha(pd, out rot, out un);
                    l.Atualizar();
                    string dica = string.IsNullOrWhiteSpace(pd.Description) ? pd.DisplayName : pd.DisplayName + "\n" + pd.Description;
                    l.Editor.ToolTip = l is LinhaPerfil ? null : dica;
                    linhas.Add(l); ls.Add(l);
                }
                for (int i = 0; i < ls.Count; i++)
                {
                    // pares curtos (X|Y, Fx|Fy): duas colunas por linha
                    if (i + 1 < ls.Count && Curto(ls[i]) && Curto(ls[i + 1])) { painel.Children.Add(LinhaPar(ls[i], ls[i + 1])); i++; }
                    else painel.Children.Add(LinhaSimples(ls[i]));
                }
                var ex = new Expander
                {
                    Header = new TextBlock { Text = nome.ToUpperInvariant(), FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = TemaPlant.BTexto2 },
                    IsExpanded = !recolhidas.Contains(nome), Content = painel, Margin = new Thickness(0, 0, 0, 4)
                };
                var estilo = TryFindResource("SecaoInspector") as Style;
                if (estilo != null) ex.Style = estilo;
                ex.Expanded += (s, e) => recolhidas.Remove(nome);
                ex.Collapsed += (s, e) => recolhidas.Add(nome);
                secoes.Children.Add(ex);
            }
        }

        static bool Curto(Linha l) { return l is LinhaNum && l.Rotulo.Length <= 4; }

        static TextBlock Etiqueta(Linha l)
        {
            var dn = l.Pd.DisplayName;
            string dica = string.IsNullOrWhiteSpace(l.Pd.Description) ? dn : dn + "\n" + l.Pd.Description;
            var tb = new TextBlock { Text = l.Rotulo, FontSize = 12, Foreground = TemaPlant.BTexto2, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, ToolTip = dica, Margin = new Thickness(0, 2, 8, 2) };
            ToolTipService.SetShowDuration(tb, 30000);
            return tb;
        }

        static FrameworkElement LinhaSimples(Linha l)
        {
            if (l is LinhaPerfil || l is LinhaCombo || l is LinhaTexto)
            {
                // editores de texto longo: rotulo em cima, editor na largura toda (nada de nome de perfil cortado)
                var sp = new StackPanel { Margin = new Thickness(0, 3, 0, 3) };
                var e2 = Etiqueta(l); e2.Margin = new Thickness(0, 0, 0, 3); e2.FontSize = 11.5;
                l.Editor.HorizontalAlignment = HorizontalAlignment.Stretch;
                sp.Children.Add(e2); sp.Children.Add(l.Editor);
                return sp;
            }
            var g = new Grid { Margin = new Thickness(0, 1, 0, 1), MinHeight = 28 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(128) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            var et = Etiqueta(l);
            l.Editor.VerticalAlignment = VerticalAlignment.Center;
            if (l is LinhaNum) { l.Editor.HorizontalAlignment = HorizontalAlignment.Left; l.Editor.Width = 112; }
            else if (l is LinhaSimNao) l.Editor.HorizontalAlignment = HorizontalAlignment.Left;
            Grid.SetColumn(l.Editor, 1);
            g.Children.Add(et); g.Children.Add(l.Editor);
            return g;
        }

        static FrameworkElement LinhaPar(Linha a, Linha b)
        {
            var g = new Grid { Margin = new Thickness(0, 1, 0, 1), MinHeight = 28 };
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            for (int k = 0; k < 2; k++)
            {
                var l = k == 0 ? a : b;
                var c = new Grid();
                c.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
                c.ColumnDefinitions.Add(new ColumnDefinition());
                var et = Etiqueta(l); et.Margin = new Thickness(0, 0, 4, 0);
                l.Editor.VerticalAlignment = VerticalAlignment.Center;
                l.Editor.Width = double.NaN; l.Editor.HorizontalAlignment = HorizontalAlignment.Stretch;
                Grid.SetColumn(l.Editor, 1);
                c.Children.Add(et); c.Children.Add(l.Editor);
                Grid.SetColumn(c, k * 2);
                g.Children.Add(c);
            }
            return g;
        }

        // ---- cabecalho ----
        static TextBlock T(string s, double tam, Brush cor, FontWeight? peso = null)
        {
            var t = new TextBlock { Text = s, FontSize = tam, Foreground = cor, FontWeight = peso ?? FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center };
            Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
            return t;
        }

        static Border Mini(string rot, string valor, string un)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(T(rot + " ", 11, TemaPlant.BTexto3));
            sp.Children.Add(T(valor, 11.5, TemaPlant.BTexto1, FontWeights.SemiBold));
            if (!string.IsNullOrEmpty(un)) sp.Children.Add(T(" " + un, 11, TemaPlant.BTexto2));
            return new Border { Background = TemaPlant.BSup1, BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(0, 0, 4, 4), Child = sp };
        }

        static Brush CorFaixa(double ap) { return ap > 1 ? TemaPlant.BErro : ap > 0.9 ? TemaPlant.BAlerta : ap > 0.75 ? TemaPlant.BAcentoHover : TemaPlant.BSucesso; }

        static Border Chip(double ap)
        {
            string txt; Brush fg, bg, bd;
            if (ap > 1) { txt = "✕ NÃO PASSA"; fg = TemaPlant.BErro; bg = TemaPlant.BErroFundo; bd = TemaPlant.BErroBorda; }
            else if (ap > 0.9) { txt = "⚠ ALERTA"; fg = TemaPlant.BAlerta; bg = TemaPlant.BAlertaFundo; bd = TemaPlant.BAlertaBorda; }
            else { txt = "✓ PASSA"; fg = TemaPlant.BSucesso; bg = TemaPlant.BSucessoFundo; bd = TemaPlant.BSucessoBorda; }
            return new Border { Background = bg, BorderBrush = bd, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 2, 6, 2), VerticalAlignment = VerticalAlignment.Center, Child = T(txt, 10.5, fg, FontWeights.SemiBold) };
        }

        static FrameworkElement BarraCapacidade(double ap)
        {
            double f = Math.Max(0, Math.Min(1, ap));
            var g = new Grid { Height = 6, Margin = new Thickness(0, 6, 0, 2) };
            g.Children.Add(new Border { Background = TemaPlant.BSup0, CornerRadius = new CornerRadius(3) });
            var linha = new Grid();
            linha.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(f, GridUnitType.Star) });
            linha.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - f, GridUnitType.Star) });
            if (f > 0) linha.Children.Add(new Border { Background = CorFaixa(ap), CornerRadius = new CornerRadius(3) });
            g.Children.Add(linha);
            return g;
        }

        Verif VerifDe(Barra b)
        {
            try { if (Verificar == null || b.Res == null) return null; var v = Verificar(b); return v != null && v.Aplicavel ? v : null; }
            catch { return null; }
        }

        static double MaiorAbs(Barra b, int tipo)
        {
            double mn, mx; b.Res.Extremos(tipo, out mn, out mx);
            return Math.Abs(mn) > Math.Abs(mx) ? mn : mx;
        }

        void MontarCabecalho()
        {
            cabecalho.Children.Clear();
            var cartao = new StackPanel();
            string icone, titulo, sub;
            var barras = alvos.OfType<Barra>().ToList();
            var nos = alvos.OfType<No>().ToList();
            if (selecaoVazia)
            {
                icone = "config"; titulo = "Modelo";
                sub = modelo.Nos.Count + " nós · " + modelo.Barras.Count + " barras";
            }
            else if (barras.Count == 1)
            {
                var b = barras[0];
                icone = "barra"; titulo = "Barra " + b.Id + " · " + NomeBonito(b.Secao);
                sub = b.Material + (b.Dono != null ? " · " + Num.Fmt(b.Dono.Comp(b), 2) + " m" : "") + (b.Membro != 0 ? " · contínua B" + b.Membro : "");
            }
            else if (barras.Count > 1) { icone = "barra"; titulo = barras.Count + " barras"; sub = "Campos em comum. Valores diferentes aparecem como (vários); editar aplica a todas."; }
            else if (nos.Count == 1)
            {
                var n = nos[0];
                icone = "no"; titulo = "Nó " + n.Id;
                sub = "(" + Num.Fmt(n.X, 2) + "; " + Num.Fmt(n.Y, 2) + ") m" + (n.TemApoio ? " · " + NomeApoio(n) : "") + (n.Placa != null ? " · com placa" : "");
            }
            else { icone = "no"; titulo = nos.Count + " nós"; sub = "Campos em comum. Valores diferentes aparecem como (vários); editar aplica a todos."; }

            var topo = new Grid();
            topo.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            topo.ColumnDefinitions.Add(new ColumnDefinition());
            var ic = Icones.Criar(icone, 16); ((FrameworkElement)ic).Margin = new Thickness(0, 2, 8, 0); ((FrameworkElement)ic).VerticalAlignment = VerticalAlignment.Top;
            topo.Children.Add(ic);
            var tt = new StackPanel();
            var tTitulo = T(titulo, 14, TemaPlant.BTexto1, FontWeights.SemiBold); tTitulo.TextWrapping = TextWrapping.Wrap; tTitulo.ToolTip = titulo;
            var tSub = T(sub, 11, TemaPlant.BTexto2); tSub.TextWrapping = TextWrapping.Wrap; tSub.Margin = new Thickness(0, 2, 0, 0);
            tt.Children.Add(tTitulo); tt.Children.Add(tSub);
            Grid.SetColumn(tt, 1);
            topo.Children.Add(tt);
            cartao.Children.Add(topo);

            // resumo do resultado
            if (barras.Count > 0)
            {
                Verif pior = null; Barra bp = null;
                foreach (var b in barras) { var v = VerifDe(b); if (v != null && (pior == null || v.Ap > pior.Ap)) { pior = v; bp = b; } }
                if (pior != null)
                {
                    var l1 = new Grid { Margin = new Thickness(0, 10, 0, 0) };
                    l1.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    l1.ColumnDefinitions.Add(new ColumnDefinition());
                    l1.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var chip = Chip(pior.Ap);
                    var tapro = T("Aproveitamento", 11, TemaPlant.BTexto2); tapro.Margin = new Thickness(8, 0, 0, 0);
                    var pct = T(Num.Fmt(pior.Ap * 100, 0) + "%", 18, CorFaixa(pior.Ap), FontWeights.Bold);
                    Grid.SetColumn(tapro, 1); Grid.SetColumn(pct, 2);
                    l1.Children.Add(chip); l1.Children.Add(tapro); l1.Children.Add(pct);
                    cartao.Children.Add(l1);
                    cartao.Children.Add(BarraCapacidade(pior.Ap));
                    string lim = string.IsNullOrEmpty(pior.Critico) ? "" : "Limitado por: " + pior.Critico;
                    if (barras.Count > 1) lim = "Pior: barra " + bp.Id + (lim == "" ? "" : " · " + lim.Replace("Limitado por: ", ""));
                    if (lim != "") { var tl = T(lim, 11, TemaPlant.BTexto2); tl.TextWrapping = TextWrapping.Wrap; tl.Margin = new Thickness(0, 2, 0, 0); cartao.Children.Add(tl); }
                }
                if (barras.Count == 1 && barras[0].Res != null)
                {
                    var b = barras[0];
                    var w = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
                    w.Children.Add(Mini("N", Num.Fmt(MaiorAbs(b, 0), 1), "kN"));
                    w.Children.Add(Mini("V", Num.Fmt(MaiorAbs(b, 1), 1), "kN"));
                    w.Children.Add(Mini("M", Num.Fmt(MaiorAbs(b, 2), 1), "kN·m"));
                    cartao.Children.Add(w);
                }
            }
            else if (nos.Count == 1 && nos[0].Res != null)
            {
                var n = nos[0];
                var w = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
                if (n.TemApoio)
                {
                    w.Children.Add(Mini("Rx", Num.Fmt(n.Res[3], 1), "kN"));
                    w.Children.Add(Mini("Ry", Num.Fmt(n.Res[4], 1), "kN"));
                    if (n.ApoioGiro || n.MolaGiro != 0) w.Children.Add(Mini("M", Num.Fmt(n.Res[5], 1), "kN·m"));
                }
                else
                {
                    w.Children.Add(Mini("ux", Num.Fmt(n.Res[0] * 1000, 2), "mm"));
                    w.Children.Add(Mini("uy", Num.Fmt(n.Res[1] * 1000, 2), "mm"));
                }
                cartao.Children.Add(w);
            }
            if (selecaoVazia)
            {
                var dica = T("Selecione uma barra ou um nó no desenho para editar suas propriedades.", 11, TemaPlant.BTexto3);
                dica.TextWrapping = TextWrapping.Wrap; dica.Margin = new Thickness(0, 6, 0, 0);
                cartao.Children.Add(dica);
            }

            // acoes rapidas
            var acoes = new List<AcaoRapida>();
            if (Acoes != null && !selecaoVazia) { try { acoes.AddRange(Acoes(alvos)); } catch { } }
            if (barras.Count > 0 && linhas.OfType<LinhaPerfil>().Any())
                acoes.Add(new AcaoRapida { Grupo = "", Texto = "Trocar perfil", Icone = "busca", Dica = "Abre a busca de perfis da família da barra (ou digite uma seção, como Ret 20x50).", Executar = () => TrocarPerfil() });
            foreach (var grupo in acoes.GroupBy(a => a.Grupo))
            {
                var gl = grupo.ToList();
                var cab = T(grupo.Key.ToUpperInvariant(), 10, TemaPlant.BTexto3, FontWeights.SemiBold); cab.Margin = new Thickness(0, 10, 0, 4);
                if (grupo.Key != "") cartao.Children.Add(cab); else cab.Margin = new Thickness(0, 6, 0, 0);
                if (grupo.Key == "") cartao.Children.Add(new Border { Height = 4 });
                if (gl.Count > 1 && gl.Any(a => a.Ativo) || grupo.Key == "Apoio")
                {
                    var seg = new Segmentado(true, gl.Select(a => new[] { a.Texto, a.Icone, a.Dica }).ToArray());
                    int ativo = gl.FindIndex(a => a.Ativo);
                    seg.Definir(ativo);
                    seg.Escolheu += i => gl[i].Executar?.Invoke();
                    cartao.Children.Add(seg);
                }
                else
                {
                    var wp = new WrapPanel();
                    foreach (var a in gl) wp.Children.Add(BotaoAcao(a));
                    cartao.Children.Add(wp);
                }
            }

            cabecalho.Children.Add(new Border
            {
                Background = TemaPlant.BSup2, BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12), Child = cartao
            });
        }

        static FrameworkElement BotaoAcao(AcaoRapida a)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (!string.IsNullOrEmpty(a.Icone) && Icones.Existe(a.Icone)) { var ic = Icones.Criar(a.Icone, 16); ((FrameworkElement)ic).Margin = new Thickness(0, 0, 6, 0); sp.Children.Add(ic); }
            sp.Children.Add(T(a.Texto, 12, TemaPlant.BTexto1, FontWeights.SemiBold));
            var b = new Border
            {
                Height = 28, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(0, 0, 6, 4), CornerRadius = new CornerRadius(4), Cursor = Cursors.Hand,
                Background = TemaPlant.BSup3, BorderBrush = TemaPlant.BBorda, BorderThickness = new Thickness(1), Child = sp, Focusable = true, FocusVisualStyle = null, ToolTip = a.Dica
            };
            System.Windows.Automation.AutomationProperties.SetName(b, a.Texto);
            b.MouseEnter += (s, e) => b.BorderBrush = TemaPlant.BBordaFoco;
            b.MouseLeave += (s, e) => b.BorderBrush = b.IsKeyboardFocused ? TemaPlant.BBordaFoco : TemaPlant.BBorda;
            b.GotKeyboardFocus += (s, e) => b.BorderBrush = TemaPlant.BBordaFoco;
            b.LostKeyboardFocus += (s, e) => b.BorderBrush = TemaPlant.BBorda;
            b.MouseLeftButtonUp += (s, e) => a.Executar?.Invoke();
            b.KeyDown += (s, e) => { if (e.Key == Key.Enter || e.Key == Key.Space) { a.Executar?.Invoke(); e.Handled = true; } };
            return b;
        }

        static string NomeApoio(No n)
        {
            if (n.ApoioX && n.ApoioY && n.ApoioGiro) return "engaste";
            if (n.ApoioX && n.ApoioY) return "fixo";
            if (n.ApoioY) return "móvel";
            if (n.ApoioX) return "móvel em X";
            return "mola/giro";
        }
    }
}
