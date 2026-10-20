// Desenho do snap e do Medir sobre a Vista3D (2D, por cima do Viewport3D, sem teste de profundidade):
// marcadores do AutoSnap iguais aos do AutoCAD, linhas de rastreio pontilhadas, tooltip, cotas, arcos e área do Medir.
// Cores e medidas: docs/v3/PESQUISA-PLANT.md §3.1, §3.3 e §4.3; cores só de TemaPlant.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace NovoFtool
{
    public static class SnapOverlay
    {
        public const double Tam = 11;   // lado do marcador (px), traço 2 px + contorno escuro 1 px

        static Pen Caneta(Brush b, double l, double[] traco = null, PenLineCap cap = PenLineCap.Flat)
        {
            var p = new Pen(b, l) { StartLineCap = cap, EndLineCap = cap, LineJoin = PenLineJoin.Miter };
            if (traco != null) p.DashStyle = new DashStyle(traco, 0);
            p.Freeze(); return p;
        }
        static readonly Brush BContorno = TemaPlant.B(TemaPlant.CorSnapContorno);
        static readonly Pen PMarca = Caneta(TemaPlant.BSnapAuto, 2), PFundo = Caneta(BContorno, 4);
        static readonly Pen PRastreio = Caneta(TemaPlant.BRastreio, 1, new double[] { 4, 4 });
        static readonly Pen PMais = Caneta(TemaPlant.BRastreio, 1.5), PMaisFundo = Caneta(BContorno, 3.5);
        static readonly Typeface Tf = new Typeface(TemaPlant.Fonte, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        static readonly Typeface TfB = new Typeface(TemaPlant.Fonte, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        static readonly Brush BPilula = TemaPlant.B(Color.FromArgb(0xD9, 0x0E, 0x11, 0x16));   // fundo da etiqueta da cota: escuro 85%

        public static FormattedText Texto(string s, Brush b, double tam, bool negrito = false) =>
            new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, negrito ? TfB : Tf, tam, b, 1.0);

        // Marcador do AutoSnap no ponto c (o desenho de cada modo é o do AutoCAD).
        public static void Marca(DrawingContext dc, TipoSnap t, Point c)
        {
            double r = Tam / 2;
            var g = new StreamGeometry();
            using (var k = g.Open())
            {
                void L(double x1, double y1, double x2, double y2) { k.BeginFigure(new Point(c.X + x1, c.Y + y1), false, false); k.LineTo(new Point(c.X + x2, c.Y + y2), true, false); }
                void Poli(bool fechar, params double[] xy)
                {
                    k.BeginFigure(new Point(c.X + xy[0], c.Y + xy[1]), false, fechar);
                    for (int i = 2; i < xy.Length; i += 2) k.LineTo(new Point(c.X + xy[i], c.Y + xy[i + 1]), true, false);
                }
                switch (t)
                {
                    case TipoSnap.Extremidade: Poli(true, -r, -r, r, -r, r, r, -r, r); break;                                   // □
                    case TipoSnap.Medio: Poli(true, 0, -r * 1.1, r * 1.05, r * 0.75, -r * 1.05, r * 0.75); break;                // △
                    case TipoSnap.Intersecao: L(-r, -r, r, r); L(-r, r, r, -r); break;                                          // ×
                    case TipoSnap.Aparente: Poli(true, -r, -r, r, -r, r, r, -r, r); L(-r * 0.6, -r * 0.6, r * 0.6, r * 0.6); L(-r * 0.6, r * 0.6, r * 0.6, -r * 0.6); break;   // ⊠
                    case TipoSnap.Perpendicular: Poli(false, -r, -r, -r, r, r, r); Poli(false, -r, 0, 0, 0, 0, r); break;       // ⊥ (canto reto com o quadradinho)
                    case TipoSnap.Proximo: Poli(true, -r, -r, r, -r, -r, r, r, r); break;                                       // ⧖ ampulheta
                    case TipoSnap.Paralelo: L(-r, r * 0.5, -r * 0.1, -r); L(r * 0.1, r, r, -r * 0.5); break;                    // //
                    case TipoSnap.Polar: case TipoSnap.Ortogonal: case TipoSnap.Rastreio: L(-3, -3, 3, 3); L(-3, 3, 3, -3); break;   // × pequeno no ponto do caminho
                    case TipoSnap.Grade: L(-4, 0, 4, 0); L(0, -4, 0, 4); break;
                }
            }
            g.Freeze();
            bool circ = t == TipoSnap.Centro || t == TipoSnap.No;
            if (circ)
            {
                dc.DrawEllipse(null, PFundo, c, r, r); dc.DrawEllipse(null, PMarca, c, r, r);   // ○
                if (t == TipoSnap.No)                                                            // ⊗
                {
                    double q = r * 0.62;
                    foreach (var p in new[] { PFundo, PMarca }) { dc.DrawLine(p, c + new Vector(-q, -q), c + new Vector(q, q)); dc.DrawLine(p, c + new Vector(-q, q), c + new Vector(q, -q)); }
                }
                return;
            }
            if (t == TipoSnap.Extensao)   // … três pontos
            {
                for (int i = -1; i <= 1; i++) { dc.DrawRectangle(BContorno, null, new Rect(c.X + i * 5 - 2, c.Y - 2, 4, 4)); dc.DrawRectangle(TemaPlant.BSnapAuto, null, new Rect(c.X + i * 5 - 1.2, c.Y - 1.2, 2.4, 2.4)); }
                return;
            }
            dc.DrawGeometry(null, PFundo, g);
            dc.DrawGeometry(null, PMarca, g);
        }

        // + ciano dos pontos adquiridos pelo rastreio
        public static void Adquirido(DrawingContext dc, Point c)
        {
            foreach (var p in new[] { PMaisFundo, PMais }) { dc.DrawLine(p, c + new Vector(-3.5, 0), c + new Vector(3.5, 0)); dc.DrawLine(p, c + new Vector(0, -3.5), c + new Vector(0, 3.5)); }
        }

        // Caminho pontilhado (rastreio, polar, extensão, paralelo) atravessando a vista.
        public static void Guia(DrawingContext dc, GuiaSnap gs, Func<Point3D, Point> tela, Size area)
        {
            Point a = tela(gs.O); Vector v = tela(gs.O + gs.D) - a;
            if (v.Length < 1e-6) return;
            v.Normalize();
            double l = area.Width + area.Height + (a - new Point(area.Width / 2, area.Height / 2)).Length;
            dc.DrawLine(PRastreio, gs.Raio ? a : a - v * l, a + v * l);
        }

        // Tooltip do snap (nome do modo; rastreio/polar: "Polar: 4,250 < 30°") ao lado do cursor.
        // acima = as caixas da entrada dinâmica ocupam o lado de baixo do cursor
        public static void Dica(DrawingContext dc, string s, Point cursor, Size area, bool acima = false)
        {
            if (string.IsNullOrEmpty(s)) return;
            var ft = Texto(s, TemaPlant.BTexto1, 11.5);
            var r = new Rect(cursor.X + 16, acima ? cursor.Y - 16 - ft.Height - 6 : cursor.Y + 16, ft.Width + 14, ft.Height + 6);
            if (r.Y < 4) r.Y = cursor.Y + 16;
            if (r.Right > area.Width - 4) r.X = cursor.X - 16 - r.Width;
            if (r.Bottom > area.Height - 4) r.Y = cursor.Y - 16 - r.Height;
            dc.DrawRoundedRectangle(TemaPlant.BCaixaHud, Caneta(TemaPlant.BSnapAuto, 1), r, 3, 3);
            dc.DrawText(ft, new Point(r.X + 7, r.Y + 3));
        }

        // ---------------- Medir ----------------

        public static Brush CorMedir(int i) => i switch { 1 => TemaPlant.BMedida2, 2 => TemaPlant.BErro, 3 => TemaPlant.BSucesso, 4 => TemaPlant.BRealce, 5 => TemaPlant.B(TemaPlant.CorMedidaArea), _ => TemaPlant.BMedida };

        static void Pilula(DrawingContext dc, string s, Point c, Brush borda)
        {
            var ft = Texto(s, Brushes.White, 12, true);
            var r = new Rect(c.X - ft.Width / 2 - 7, c.Y - ft.Height / 2 - 3, ft.Width + 14, ft.Height + 6);
            dc.DrawRoundedRectangle(BPilula, Caneta(borda, 1), r, r.Height / 2, r.Height / 2);
            dc.DrawText(ft, new Point(r.X + 7, r.Y + 3));
        }

        static void Seta(DrawingContext dc, Point ponta, Vector dir, Brush b)
        {
            dir.Normalize(); var n = new Vector(-dir.Y, dir.X);
            var g = new StreamGeometry();
            using (var k = g.Open()) { k.BeginFigure(ponta, true, true); k.LineTo(ponta - dir * 10 + n * 3.5, true, false); k.LineTo(ponta - dir * 10 - n * 3.5, true, false); }
            g.Freeze(); dc.DrawGeometry(b, null, g);
        }

        // Cota temporária: linha 1,5 px com setas cheias de 10 px e a etiqueta em pílula no meio.
        public static void Cota(DrawingContext dc, CotaMedida k, Func<Point3D, Point> tela)
        {
            Point a = tela(k.A), b = tela(k.B); Vector v = b - a; Brush cor = CorMedir(k.Cor);
            var pen = Caneta(cor, k.Fina ? 1 : 1.5, k.Tracejada ? new double[] { 5, 3 } : null);
            dc.DrawLine(pen, a, b);
            if (!k.Fina && v.Length > 26) { Seta(dc, a, -v, cor); Seta(dc, b, v, cor); }
            if (!string.IsNullOrEmpty(k.Texto)) Pilula(dc, k.Texto, a + v / 2, cor);
        }

        // Arco do ângulo: raio 40 px na tela (maior se o ângulo projetado for estreito), no menor giro entre as duas direções.
        public static void Arco(DrawingContext dc, ArcoMedida k, Func<Point3D, Point> tela)
        {
            Point v = tela(k.V); Vector d1 = tela(k.V + k.D1) - v, d2 = tela(k.V + k.D2) - v;
            if (d1.Length < 1e-6 || d2.Length < 1e-6) { Pilula(dc, k.Texto, v + new Vector(0, -28), TemaPlant.BMedida); return; }
            double a1 = Math.Atan2(d1.Y, d1.X), a2 = Math.Atan2(d2.Y, d2.X), da = a2 - a1;
            while (da > Math.PI) da -= 2 * Math.PI; while (da < -Math.PI) da += 2 * Math.PI;
            double R = Math.Max(40, Math.Min(110, 30 / Math.Max(1e-3, Math.Abs(da))));   // arco com pelo menos ~30 px de comprimento
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(v + new Vector(Math.Cos(a1), Math.Sin(a1)) * R, false, false);
                c.ArcTo(v + new Vector(Math.Cos(a2), Math.Sin(a2)) * R, new Size(R, R), 0, false, da > 0 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true, false);
            }
            g.Freeze();
            var pen = Caneta(TemaPlant.BMedida, 1.5);
            dc.DrawGeometry(null, pen, g);
            foreach (var d in new[] { d1, d2 }) { var u = d; u.Normalize(); dc.DrawLine(Caneta(TemaPlant.BMedida, 1, new double[] { 3, 3 }), v, v + u * (R + 8)); }
            double am = a1 + da / 2;
            Pilula(dc, k.Texto, v + new Vector(Math.Cos(am), Math.Sin(am)) * (R + 18), TemaPlant.BMedida);
        }

        public static void Poligono(DrawingContext dc, IList<Point3D> pts, Func<Point3D, Point> tela)
        {
            if (pts == null || pts.Count < 3) return;
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(tela(pts[0]), true, true);
                foreach (Point3D p in pts.Skip(1)) c.LineTo(tela(p), true, false);
            }
            g.Freeze();
            Color v = TemaPlant.CorMedidaArea;
            dc.DrawGeometry(TemaPlant.B(Color.FromArgb(0x40, v.R, v.G, v.B)), Caneta(TemaPlant.B(v), 1.5), g);
        }

        // barra escolhida: contorno grosso translúcido (laranja = seleção 1, azul = seleção 2)
        public static void Destaque(DrawingContext dc, Point3D a, Point3D b, int cor, Func<Point3D, Point> tela)
        {
            Color c = cor == 1 ? TemaPlant.CorMedida2 : TemaPlant.CorMedida;
            dc.DrawLine(Caneta(TemaPlant.B(Color.FromArgb(0x70, c.R, c.G, c.B)), 7, null, PenLineCap.Round), tela(a), tela(b));
            dc.DrawLine(Caneta(TemaPlant.B(c), 1.5), tela(a), tela(b));
        }

        // ponto medido: bolinha de 6 px (laranja / azul)
        public static void PontoMedido(DrawingContext dc, Point p, int cor)
        {
            dc.DrawEllipse(cor == 1 ? TemaPlant.BMedida2 : TemaPlant.BMedida, Caneta(BContorno, 1), p, 3.5, 3.5);
        }
    }
}
