// ViewCube da Vista3D (canto superior direito), como o do AutoCAD/Plant 3D (docs/v3/PESQUISA-PLANT.md §2.2):
// 26 regiões clicáveis (6 faces = vistas ortogonais, 12 arestas = vistas "meias", 8 cantos = três-quartos), casinha = Home
// (isométrica), arrastar o cubo orbita e, ao soltar perto de uma das 26 vistas (10°), encaixa nela. Abaixo: nome da vista e
// o alternador Ortográfica/Perspectiva. O cubo gira junto com a câmera (projeção ortográfica dos eixos da vista).
using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace NovoFtool
{
    public partial class Vista3D
    {
        sealed class CuboVista : FrameworkElement
        {
            readonly Vista3D v;
            const double Meio = 25;                     // meia aresta do cubo na tela (px)
            Point Centro => new Point(ActualWidth / 2 + 6, 58);
            Rect Casa => new Rect(Centro.X - 58, Centro.Y - 54, 18, 16);
            Rect Proj => new Rect(0, ActualHeight - 20, ActualWidth, 18);

            // faces: normal, eixo u (direita do texto), eixo w (cima do texto), rótulo
            static readonly (Vector3D N, Vector3D U, Vector3D W, string Nome)[] Faces =
            {
                (new Vector3D(0, 0, 1), new Vector3D(1, 0, 0), new Vector3D(0, 1, 0), "TOPO"),
                (new Vector3D(0, 0, -1), new Vector3D(1, 0, 0), new Vector3D(0, -1, 0), "BASE"),
                (new Vector3D(0, -1, 0), new Vector3D(1, 0, 0), new Vector3D(0, 0, 1), "FRENTE"),
                (new Vector3D(0, 1, 0), new Vector3D(-1, 0, 0), new Vector3D(0, 0, 1), "TRÁS"),
                (new Vector3D(1, 0, 0), new Vector3D(0, 1, 0), new Vector3D(0, 0, 1), "DIREITA"),
                (new Vector3D(-1, 0, 0), new Vector3D(0, -1, 0), new Vector3D(0, 0, 1), "ESQUERDA"),
            };

            (int F, int A, int B)? hover;   // face, coluna (-1, 0, 1) e linha (-1, 0, 1) sob o mouse
            bool hoverCasa, hoverProj;
            Point? arrIni; bool arrastou;

            public CuboVista(Vista3D v) { this.v = v; Cursor = Cursors.Hand; ToolTip = null; }

            static readonly Typeface Tf = new Typeface(TemaPlant.Fonte, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
            FormattedText Txt(string s, Brush b, double tam) => new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Tf, tam, b, 1.0);

            Point Proj2(Vector3D p)
            {
                v.Direcoes(out _, out Vector3D cima, out Vector3D dir);
                return Centro + new Vector(Vector3D.DotProduct(p, dir), -Vector3D.DotProduct(p, cima)) * Meio;
            }

            // faces voltadas para o olho, da mais funda para a mais próxima
            int[] Visiveis()
            {
                v.Direcoes(out Vector3D d, out _, out _);
                return Enumerable.Range(0, 6).Where(i => Vector3D.DotProduct(Faces[i].N, d) > 1e-3).OrderBy(i => Vector3D.DotProduct(Faces[i].N, d)).ToArray();
            }

            // Coordenadas (a, b) ∈ [-1, 1]² do pixel na face i (inversa do paralelogramo projetado). null = fora.
            (double A, double B)? NaFace(int i, Point m)
            {
                var f = Faces[i];
                Point c = Proj2(f.N); Vector u = Proj2(f.N + f.U) - c, w = Proj2(f.N + f.W) - c, r = m - c;
                double det = u.X * w.Y - u.Y * w.X;
                if (Math.Abs(det) < 1e-6) return null;
                double a = (r.X * w.Y - r.Y * w.X) / det, b = (u.X * r.Y - u.Y * r.X) / det;
                return Math.Abs(a) <= 1 && Math.Abs(b) <= 1 ? (a, b) : ((double, double)?)null;
            }

            (int F, int A, int B)? Regiao(Point m)
            {
                foreach (int i in Visiveis().Reverse())
                    if (NaFace(i, m) is (double a, double b))
                        return (i, a < -1 / 3.0 ? -1 : a > 1 / 3.0 ? 1 : 0, b < -1 / 3.0 ? -1 : b > 1 / 3.0 ? 1 : 0);
                return null;
            }

            static Vector3D Direcao((int F, int A, int B) r) { var f = Faces[r.F]; return f.N + f.U * r.A + f.W * r.B; }

            static string NomeDe((int F, int A, int B) r)
            {
                if (r.A == 0 && r.B == 0) return Cap(Faces[r.F].Nome);
                Vector3D d = Direcao(r);
                string s = (d.Z > 0 ? "Topo " : d.Z < 0 ? "Base " : "") + (d.Y < 0 ? "frente " : d.Y > 0 ? "trás " : "") + (d.X > 0 ? "direita" : d.X < 0 ? "esquerda" : "");
                return s.Trim();
            }
            static string Cap(string s) => s.Substring(0, 1) + s.Substring(1).ToLowerInvariant();

            protected override void OnRender(DrawingContext dc)
            {
                dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));   // área toda clicável
                v.Direcoes(out Vector3D d, out _, out _);
                var aresta = new Pen(TemaPlant.B(TemaPlant.CorCuboAresta), 1);
                foreach (int i in Visiveis())
                {
                    var f = Faces[i];
                    Point p00 = Proj2(f.N - f.U + f.W), p10 = Proj2(f.N + f.U + f.W), p11 = Proj2(f.N + f.U - f.W), p01 = Proj2(f.N - f.U - f.W);
                    var g = new StreamGeometry();
                    using (var c = g.Open()) { c.BeginFigure(p00, true, true); c.PolyLineTo(new[] { p10, p11, p01 }, true, true); }
                    double luz = 0.75 + 0.25 * Vector3D.DotProduct(f.N, d);   // face de frente mais clara
                    var cor = TemaPlant.CorCuboFace;
                    dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(cor.A, (byte)(cor.R * luz), (byte)(cor.G * luz), (byte)(cor.B * luz))), aresta, g);
                    // rótulo no plano da face: matriz afim do retângulo do texto para o paralelogramo
                    var ft = Txt(f.Nome, TemaPlant.BTexto1, 9.5);
                    double tw = Math.Max(ft.Width + 6, 40), th = 40;
                    Vector ux = (p10 - p00) / tw, wy = (p01 - p00) / th;
                    if (Vector3D.DotProduct(f.N, d) > 0.15)
                    {
                        dc.PushTransform(new MatrixTransform(new Matrix(ux.X, ux.Y, wy.X, wy.Y, p00.X, p00.Y)));
                        dc.DrawText(ft, new Point((tw - ft.Width) / 2, (th - ft.Height) / 2));
                        dc.Pop();
                    }
                    if (hover is (int hf, int ha, int hb) && hf == i)   // região sob o mouse (face, aresta ou canto)
                    {
                        double a0 = ha == -1 ? -1 : ha == 0 ? -1 / 3.0 : 1 / 3.0, a1 = ha == -1 ? -1 / 3.0 : ha == 0 ? 1 / 3.0 : 1;
                        double b0 = hb == -1 ? -1 : hb == 0 ? -1 / 3.0 : 1 / 3.0, b1 = hb == -1 ? -1 / 3.0 : hb == 0 ? 1 / 3.0 : 1;
                        Point q(double a, double b) => Proj2(f.N + f.U * a + f.W * b);
                        var gh = new StreamGeometry();
                        using (var c = gh.Open()) { c.BeginFigure(q(a0, b0), true, true); c.PolyLineTo(new[] { q(a1, b0), q(a1, b1), q(a0, b1) }, true, true); }
                        dc.DrawGeometry(TemaPlant.B(TemaPlant.CorCuboHover), null, gh);
                    }
                }
                // casinha (Home)
                Rect h = Casa;
                var pc = new Pen(hoverCasa ? TemaPlant.BRealce : TemaPlant.BTexto2, 1.4) { LineJoin = PenLineJoin.Round };
                var casa = Geometry.Parse(FormattableString.Invariant($"M{h.X + 2},{h.Y + 8} L{h.X + 9},{h.Y + 1} L{h.X + 16},{h.Y + 8} M{h.X + 4},{h.Y + 7} V{h.Y + 15} H{h.X + 14} V{h.Y + 7}"));
                dc.DrawGeometry(null, pc, casa);
                // nome da vista + projeção
                var fn = Txt(v.NomeVista, TemaPlant.BTexto3, 10.5);
                dc.DrawText(fn, new Point((ActualWidth - fn.Width) / 2 + 6, ActualHeight - 38));
                var fp = Txt((v.Perspectiva ? "Perspectiva" : "Ortográfica") + " ▾", hoverProj ? TemaPlant.BRealce : TemaPlant.BTexto2, 10.5);
                dc.DrawText(fp, new Point((ActualWidth - fp.Width) / 2 + 6, ActualHeight - 20));
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                Point m = e.GetPosition(this);
                if (arrIni != null)
                {
                    Vector dv = m - arrIni.Value;
                    if (!arrastou && dv.Length < 3) return;
                    arrastou = true; arrIni = m;
                    v.pivoP = null; v.Orbitar(dv.X * 1.6, dv.Y * 1.6);   // arrastar o cubo orbita em torno do alvo
                    e.Handled = true; return;
                }
                var r = Regiao(m); bool hc = Casa.Contains(m), hp = Proj.Contains(m);
                if (!Equals(r, hover) || hc != hoverCasa || hp != hoverProj)
                {
                    hover = r; hoverCasa = hc; hoverProj = hp;
                    ToolTip = hc ? "Home: vista isométrica" : hp ? "Alterna perspectiva / ortográfica" : r is (int, int, int) rr ? NomeDe(rr) : null;
                    InvalidateVisual();
                }
                e.Handled = true;
            }

            protected override void OnMouseLeave(MouseEventArgs e) { hover = null; hoverCasa = hoverProj = false; InvalidateVisual(); }

            protected override void OnMouseDown(MouseButtonEventArgs e)
            {
                e.Handled = true;
                if (e.ChangedButton != MouseButton.Left) return;
                v.Parar();
                arrIni = e.GetPosition(this); arrastou = false;
                CaptureMouse();
            }

            protected override void OnMouseUp(MouseButtonEventArgs e)
            {
                e.Handled = true;
                if (e.ChangedButton != MouseButton.Left || arrIni == null) return;
                Point m = e.GetPosition(this);
                ReleaseMouseCapture(); arrIni = null;
                if (arrastou) { Encaixar(); return; }
                if (Casa.Contains(m)) { v.MostrarVista(VistaPadrao.Iso); return; }
                if (Proj.Contains(m)) { v.Perspectiva = !v.Perspectiva; InvalidateVisual(); return; }
                if (Regiao(m) is (int, int, int) r) v.IrParaDirecao(Direcao(r), NomeDe(r));
            }

            // Solta o arraste: se a direção ficou a menos de 10° de uma das 26 vistas, gira até ela.
            void Encaixar()
            {
                v.Direcoes(out Vector3D d, out _, out _);
                (double ang, (int, int, int) r) melhor = (180, (0, 0, 0));
                for (int f = 0; f < 6; f++) for (int a = -1; a <= 1; a++) for (int b = -1; b <= 1; b++)
                {
                    var dir = Direcao((f, a, b)); dir.Normalize();
                    double ang = Vector3D.AngleBetween(dir, d);
                    if (ang < melhor.ang) melhor = (ang, (f, a, b));
                }
                if (melhor.ang < 10) v.IrParaDirecao(Direcao(melhor.r), NomeDe(melhor.r));
            }

            // para testes/capturas: clique numa região pela direção (face, coluna, linha)
            public void ClicarRegiao(int face, int a, int b) => v.IrParaDirecao(Direcao((face, a, b)), NomeDe((face, a, b)), false);
        }

        // ViewCube pelo código (capturas/testes): face 0..5 = Topo, Base, Frente, Trás, Direita, Esquerda; a, b ∈ {-1, 0, 1}.
        public void CuboClicar(int face, int a, int b) => cubo.ClicarRegiao(face, a, b);
    }
}
