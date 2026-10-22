// Desenho da seção por forma (B1 v0.6): I/H, U, Ue, L (igual/desigual), T, barra chata, tubo quadrado/retangular (com raio) e tubo redondo.
// A forma vem de props.tipo do motor (I, U, Ue, L, T, retangular, tubo_retangular, tubo_circular); desenho em escala, centrado, com cotas.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using static NovoFtool.UiPlaca;
using static NovoFtool.UiAnalise;

namespace NovoFtool
{
    public static class DesenhoSecao
    {
        // Geometria em px (origem no canto superior esquerdo, y para baixo). W, H já em escala.
        public static Geometry Forma(string tipo, string nome, double W, double H, double Tw, double Tf, double T, double esc)
        {
            switch (tipo)
            {
                case "tubo_circular":
                    return new CombinedGeometry(GeometryCombineMode.Exclude, new EllipseGeometry(new Rect(0, 0, W, H)), new EllipseGeometry(new Rect(T, T, Math.Max(0.5, W - 2 * T), Math.Max(0.5, H - 2 * T))));
                case "tubo_retangular":
                {
                    double re = Math.Min(2 * T, Math.Min(W, H) / 2.5), ri = Math.Max(0, re - T); // raio externo 2t (metalon/Vallourec), interno re − t
                    return new CombinedGeometry(GeometryCombineMode.Exclude,
                        new RectangleGeometry(new Rect(0, 0, W, H), re, re),
                        new RectangleGeometry(new Rect(T, T, Math.Max(0.5, W - 2 * T), Math.Max(0.5, H - 2 * T)), ri, ri));
                }
                case "retangular": return new RectangleGeometry(new Rect(0, 0, W, H));
            }
            var pts = new List<Point>();
            switch (tipo)
            {
                case "L": // perna vertical = d, horizontal = bf, espessura t
                    pts.AddRange(new[] { new Point(0, 0), new Point(T, 0), new Point(T, H - T), new Point(W, H - T), new Point(W, H), new Point(0, H) });
                    break;
                case "T": // mesa no topo (largura bf, espessura tf) e alma (tw) para baixo
                {
                    double xa = (W - Tw) / 2;
                    pts.AddRange(new[] { new Point(0, 0), new Point(W, 0), new Point(W, Tf), new Point(xa + Tw, Tf), new Point(xa + Tw, H), new Point(xa, H), new Point(xa, Tf), new Point(0, Tf) });
                    break;
                }
                case "U": case "Ue":
                {
                    // enrijecedor do Ue: 3º número do nome (Ue 127x50x17x2,65 → 17 mm)
                    var partes = nome.Split('x');
                    double lab = tipo == "Ue" ? (partes.Length >= 4 && Portico.Num.Ler(partes[2], out double dl) ? dl * esc : Math.Min(H * 0.25, W * 0.35)) : 0;
                    pts.AddRange(new[] { new Point(0, 0), new Point(W, 0) });
                    if (lab > 0) pts.AddRange(new[] { new Point(W, lab), new Point(W - Tf, lab), new Point(W - Tf, Tf) }); else pts.Add(new Point(W, Tf));
                    pts.AddRange(new[] { new Point(Tw, Tf), new Point(Tw, H - Tf) });
                    if (lab > 0) pts.AddRange(new[] { new Point(W - Tf, H - Tf), new Point(W - Tf, H - lab), new Point(W, H - lab), new Point(W, H) }); else pts.AddRange(new[] { new Point(W, H - Tf), new Point(W, H) });
                    pts.Add(new Point(0, H));
                    break;
                }
                default: // I / H
                {
                    double xa = (W - Tw) / 2;
                    pts.AddRange(new[] { new Point(0, 0), new Point(W, 0), new Point(W, Tf), new Point(xa + Tw, Tf), new Point(xa + Tw, H - Tf), new Point(W, H - Tf), new Point(W, H),
                        new Point(0, H), new Point(0, H - Tf), new Point(xa, H - Tf), new Point(xa, Tf), new Point(0, Tf) });
                    break;
                }
            }
            var geo = new StreamGeometry();
            using (var c = geo.Open()) { c.BeginFigure(pts[0], true, true); c.PolyLineTo(pts.Skip(1).ToList(), true, true); }
            geo.Freeze();
            return geo;
        }

        // p = JSON de /api/catalogo/{nome} (secao.props.tipo, d_mm, bf_mm, tw_mm, tf_mm, t_mm).
        public static FrameworkElement Cotado(JsonNode p)
        {
            double h = D(p, "d_mm"), b = D(p, "bf_mm"), tw = D(p, "tw_mm"), tf = D(p, "tf_mm"), t = D(p, "t_mm");
            string tipo = S(p?["secao"]?["props"], "tipo"), nome = S(p, "nome");
            if (double.IsNaN(h) || double.IsNaN(b)) return new Border();
            if (tipo == "" || tipo == null) tipo = S(p, "familia") == "Ue" ? "Ue" : S(p, "familia") == "U" ? "U" : "I";
            if (tipo == "tubo_circular") b = h;
            if (double.IsNaN(t)) t = double.IsNaN(tw) ? Math.Min(h, b) / 40 : tw;
            if (double.IsNaN(tw)) tw = t;
            if (double.IsNaN(tf)) tf = t;
            double esc = Math.Min(130 / h, 220 / b);
            double W = b * esc, H = h * esc;
            double Tw = Math.Max(1.5, tw * esc), Tf = Math.Max(1.5, tf * esc), T = Math.Max(1.5, t * esc);
            var geo = Forma(tipo, nome, W, H, Tw, Tf, T, esc);
            var cv = new Canvas { Width = W + 70, Height = H + 34 };
            double x0 = 46, y0 = 8;
            var forma = new Path { Data = geo, Fill = TemaPlant.BAcentoSutil, Stroke = TemaPlant.BRealce, StrokeThickness = 1.5, StrokeLineJoin = PenLineJoin.Miter };
            Canvas.SetLeft(forma, x0); Canvas.SetTop(forma, y0); cv.Children.Add(forma);
            cv.Children.Add(new Line { X1 = x0 - 12, Y1 = y0, X2 = x0 - 12, Y2 = y0 + H, Stroke = TemaPlant.BTexto2, StrokeThickness = 1 });
            string pre = tipo == "tubo_circular" ? "Ø " : "d = ";
            var td = Tx(pre + F(h, 0), 11, TemaPlant.BTexto2); td.RenderTransform = new RotateTransform(-90);
            Canvas.SetLeft(td, x0 - 30); Canvas.SetTop(td, y0 + H / 2 + 22); cv.Children.Add(td);
            if (tipo != "tubo_circular")
            {
                cv.Children.Add(new Line { X1 = x0, Y1 = y0 + H + 10, X2 = x0 + W, Y2 = y0 + H + 10, Stroke = TemaPlant.BTexto2, StrokeThickness = 1 });
                var tb = Tx("bf = " + F(b, 0) + " mm", 11, TemaPlant.BTexto2);
                Canvas.SetLeft(tb, x0 + W / 2 - 30); Canvas.SetTop(tb, y0 + H + 12); cv.Children.Add(tb);
            }
            return new Viewbox { Stretch = Stretch.None, Child = cv, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        }
    }
}
