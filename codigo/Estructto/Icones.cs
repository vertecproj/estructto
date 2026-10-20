// Icones proprios (desenho original, sala limpa) seguindo o VOCABULARIO grafico da faixa do AutoCAD 2027 escuro,
// levantado nas capturas ampliadas (C:\dev\ux-teste\b-*.png):
//  1. objeto original em CINZA-CLARO, resultado/alvo da acao em AZUL-CLARO (ex.: copiar = cinza -> azul);
//  2. modificador pequeno no canto inferior direito: brilho amarelo = criar; X vermelho = remover; seta azul = executar;
//  3. "objeto" grande = chapado cinza-escuro (#5B6475) com contorno claro de 2 px; papel = cinza-claro com dobra;
//  4. linhas finas de 1 px nitidas, alcas quadradas azuis nas pontas; sem contorno escuro, sem volume.
// Camada = (preenchimento, traco, espessura, geometria). Espessura negativa = pixels de tela. "~" = tracejado.
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NovoFtool
{
    public static class Icones
    {
        const string L = "#D9DCE1",    // cinza-claro (linhas, papel, objeto original)
                     L2 = "#A3ABB7",   // cinza medio (dobra, secundario)
                     E = "#5B6475",    // cinza-escuro (corpo de objeto)
                     W = "#F4F6F8",    // branco (ferramentas do acesso rapido, cursor)
                     A = "#5FB6EF",    // azul-claro (resultado)
                     Af = "#555FB6EF", // azul translucido (area)
                     Y = "#F6C453",    // amarelo
                     R = "#E8565A",    // vermelho
                     K = "#1E232B",    // contorno do cursor
                     N = null;

        static string F(string fmt, params object[] a) { return string.Format(CultureInfo.InvariantCulture, fmt, a); }
        static string Q(double x, double y) { return F("M{0},{1} h4 v4 h-4 Z", x - 2, y - 2); }   // alca 4x4

        // ---- modificadores (canto inferior direito) ----
        static (string, string, double, string)[] Brilho(double x, double y) =>
            new[] { (Y, (string)N, 0.0, F("M{0},{1} L{2},{3} L{4},{1} L{2},{5} Z", x - 6, y, x, y - 1.6, x + 6, y + 1.6)),
                    (Y, N, 0.0, F("M{0},{1} L{2},{3} L{4},{1} L{2},{5} Z", x - 1.6, y, x, y - 6, x + 1.6, y + 6)) };
        static (string, string, double, string)[] Xis(double x, double y) =>
            new[] { (R, (string)N, 0.0, F("M{0},{1} l2,-2 l3,3 l3,-3 l2,2 l-3,3 l3,3 l-2,2 l-3,-3 l-3,3 l-2,-2 l3,-3 Z", x - 5, y - 3)) };
        static (string, string, double, string)[] Seta(double x, double y) =>
            new[] { (A, (string)N, 0.0, F("M{0},{1} L{2},{3} L{0},{4} Z", x - 4, y - 6, x + 5, y, y + 6)) };

        static (string f, string s, double w, string d)[] C(params (string, string, double, string)[][] partes) { return partes.SelectMany(p => p).ToArray(); }
        static (string, string, double, string)[] P(params (string, string, double, string)[] c) { return c; }

        static readonly Dictionary<string, (string f, string s, double w, string d)[]> D = new Dictionary<string, (string, string, double, string)[]>
        {
            // ---- acesso rapido / arquivo (brancos, como os do AutoCAD) ----
            ["novo"] = P((W, N, 0, "M7,3 H19 L26,10 V29 H7 Z"), (L2, N, 0, "M19,3 V10 H26 Z")),
            ["abrir"] = P((N, W, -2, "M4,8 H12 L14,10 H26 V14 M4,8 V26 H24 L28,15 H9 L4,26")),
            ["salvar"] = P((W, N, 0, "M4,4 H24 L28,8 V28 H4 Z"), (E, N, 0, "M9,4 H22 V12 H9 Z M8,17 H24 V28 H8 Z"), (W, N, 0, "M17,5.5 H20 V10.5 H17 Z")),
            ["desfazer"] = P((W, N, 0, "M11,6 L3,13 L11,20 V15.5 H19 A5.5,5.5 0 0 1 19,26.5 H13 V29.5 H19 A8.5,8.5 0 0 0 19,12.5 H11 Z")),
            ["refazer"] = P((W, N, 0, "M21,6 L29,13 L21,20 V15.5 H13 A5.5,5.5 0 0 0 13,26.5 H19 V29.5 H13 A8.5,8.5 0 0 1 13,12.5 H21 Z")),
            ["importar"] = P((L, N, 0, "M6,3 H18 L24,9 V29 H6 Z"), (L2, N, 0, "M18,3 V9 H24 Z"), (A, N, 0, "M13.5,11 H16.5 V18 H20 L15,24 L10,18 H13.5 Z")),
            ["exportar"] = P((L, N, 0, "M6,3 H18 L24,9 V29 H6 Z"), (L2, N, 0, "M18,3 V9 H24 Z"), (A, N, 0, "M13.5,25 H16.5 V18 H20 L15,12 L10,18 H13.5 Z")),
            ["relatorio"] = C(P((L, N, 0, "M5,3 H19 L25,9 V29 H5 Z"), (L2, N, 0, "M19,3 V9 H25 Z"), (E, N, 0, "M9,13 H21 V14.5 H9 Z M9,17 H21 V18.5 H9 Z M9,21 H16 V22.5 H9 Z")), Seta(26, 25)),
            ["tabela"] = P((N, L, -1, "M3.5,5.5 H28.5 V26.5 H3.5 Z M3.5,12.5 H28.5 M3.5,19.5 H28.5 M11.5,5.5 V26.5 M20.5,5.5 V26.5")),

            // ---- desenhar / modificar ----
            ["sel"] = P((W, K, -1, "M10.5,3.5 V25.5 L15.5,20.5 L19,28.5 L22.5,27 L19,19.5 H26 Z")),
            ["barra"] = P((N, L, -1, "M6.5,25.5 L25.5,6.5"), (A, N, 0, Q(6, 26) + " " + Q(26, 6))),
            ["no"] = C(P((N, L, -1, "M2.5,13.5 H25.5"), (A, N, 0, "M10,10.5 h7 v7 h-7 Z")), Brilho(25, 24)),
            ["medir"] = P((E, L, -1, "M3.5,19.5 H28.5 V27.5 H3.5 Z"), (N, L, -1, "M8.5,19.5 V23.5 M13.5,19.5 V25.5 M18.5,19.5 V23.5 M23.5,19.5 V25.5 M4.5,6.5 V15.5 M27.5,6.5 V15.5"), (N, Y, -1.6, "~M5.5,10.5 H26.5"), (Y, N, 0, "M4.5,10.5 L9,7.5 V13.5 Z M27.5,10.5 L23,7.5 V13.5 Z")),
            ["apagar"] = P((Y, N, 0, "M13,23 L26,10 L30,14 L17,27 Z"), (R, N, 0, "M13,23 L17,27 L14,30 L10,26 Z"), (N, A, -1, "M17.5,29.5 H30.5"), (N, L, -1, "M2.5,29.5 H9.5")),
            ["dividir"] = P((N, L, -1, "M3.5,16.5 H28.5"), (A, N, 0, Q(4, 16.5) + " " + Q(12.3, 16.5) + " " + Q(20.6, 16.5) + " " + Q(28, 16.5))),
            ["mover"] = P((W, N, 0, "M16,2 L21,7 H17.5 V14.5 H25 V11 L30,16 L25,21 V17.5 H17.5 V25 H21 L16,30 L11,25 H14.5 V17.5 H7 V21 L2,16 L7,11 V14.5 H14.5 V7 H11 Z")),
            ["copiar"] = P((N, L, -2, "M8,5 A4,4 0 1 0 8,13 A4,4 0 1 0 8,5"), (N, W, -2, "M15,7 H22 V12"), (W, N, 0, "M19,11 L22,15 L25,11 Z"), (N, A, -2, "M11,19 A4.5,4.5 0 1 0 11,28 A4.5,4.5 0 1 0 11,19 M21,19 A4.5,4.5 0 1 0 21,28 A4.5,4.5 0 1 0 21,19")),
            ["espelhar"] = P((N, L, -2, "M13,27 H4 L13,6 Z"), (N, A, -2, "M19,27 H28 L19,6 Z"), (N, L2, -1, "~M16.5,3 V30")),
            ["assist"] = C(P((N, L, -2, "M4,28 L19,13"), (L, N, 0, "M18,11 L21,14 L19,16 L16,13 Z")), Brilho(24, 8), P((A, N, 0, Q(26, 20)))),

            // ---- apoios: simbolo de engenharia em traco cinza, no (alvo) em azul ----
            ["engaste"] = P((N, L, -1, "M16.5,8 V20.5 M4.5,20.5 H28.5 M7,21 L4,26 M12,21 L9,26 M17,21 L14,26 M22,21 L19,26 M27,21 L24,26"), (A, N, 0, Q(16.5, 6.5))),
            ["rotula"] = P((N, L, -1, "M16.5,8.5 L24.5,20.5 H8.5 Z M4.5,23.5 H28.5 M7,24 L4,29 M12,24 L9,29 M17,24 L14,29 M22,24 L19,29 M27,24 L24,29"), (A, N, 0, Q(16.5, 7))),
            ["rolo"] = P((N, L, -1, "M16.5,8.5 L23.5,18.5 H9.5 Z M10,21.5 A2,2 0 1 0 14,21.5 A2,2 0 1 0 10,21.5 M19,21.5 A2,2 0 1 0 23,21.5 A2,2 0 1 0 19,21.5 M4.5,25.5 H28.5"), (A, N, 0, Q(16.5, 7))),
            ["roloh"] = P((N, L, -1, "M25.5,16.5 L15.5,9.5 V23.5 Z M12,10 A2,2 0 1 0 12,14 A2,2 0 1 0 12,10 M12,19 A2,2 0 1 0 12,23 A2,2 0 1 0 12,19 M7.5,4.5 V28.5"), (A, N, 0, Q(27, 16.5))),
            ["livre"] = C(P((N, L2, -1, "~M14.5,6.5 L22.5,19.5 H6.5 Z M3.5,22.5 H25.5")), Xis(25, 25)),
            ["rotulab"] = P((N, L, -1, "M1.5,16.5 H10.5 M22.5,16.5 H30.5"), (N, A, -2, "M11,16.5 A5.5,5.5 0 1 0 22,16.5 A5.5,5.5 0 1 0 11,16.5")),
            ["placa"] = P((E, L, -2, "M11,3 H22 V19 H11 Z"), (A, N, 0, "M3,19 H30 V24 H3 Z"), (L, N, 0, "M6,24 H9 V30 H6 Z M24,24 H27 V30 H24 Z")),
            ["eixo"] = P((E, L, -1, "M7.5,4.5 H25.5 V8.5 H18.5 V24.5 H25.5 V28.5 H7.5 V24.5 H14.5 V8.5 H7.5 Z"), (N, A, -1, "~M1.5,16.5 H31.5")),
            ["pincel"] = P((N, L, -1, "M2.5,26.5 L18.5,10.5"), (A, N, 0, Q(2.5, 26.5) + " " + Q(18.5, 10.5)), (Y, N, 0, "M21,4 L28,11 L24,15 L17,8 Z"), (L, N, 0, "M24,15 L26,17 L22,25 L16,19 L24,15 Z")),

            // ---- cargas ----
            ["cargano"] = P((A, N, 0, "M15,3 H18 V16 H22 L16.5,23 L11,16 H15 Z"), (N, L, -1, "M3.5,26.5 H29.5"), (L, N, 0, Q(16.5, 26.5))),
            ["cargadist"] = P((N, A, -1, "M3.5,4.5 H29.5 M6.5,4.5 V13 M16.5,4.5 V13 M26.5,4.5 V13"), (A, N, 0, "M3,12 H10 L6.5,17 Z M13,12 H20 L16.5,17 Z M23,12 H30 L26.5,17 Z"), (N, L, -1, "M2.5,21.5 H30.5"), (L, N, 0, Q(3, 21.5) + " " + Q(30, 21.5))),
            ["peso"] = P((E, L, -2, "M9,12 H23 L27,29 H5 Z"), (N, L, -2, "M12.5,12 A3.5,3.5 0 1 1 19.5,12")),
            ["limpar"] = C(P((N, L, -1, "M3.5,4.5 H22.5 M6.5,4.5 V12 M13,4.5 V12 M19.5,4.5 V12 M2.5,17.5 H22.5"), (L, N, 0, "M4,11 H9 L6.5,15 Z M10.5,11 H15.5 L13,15 Z M17,11 H22 L19.5,15 Z")), Xis(25, 25)),

            // ---- analise / resultados ----
            ["calc"] = P((N, L, -2, "M5,27 V9 H21 V27 M2,27 H24"), (A, N, 0, "M19,14 L30,21.5 L19,29 Z")),
            ["def"] = P((N, L, -1, "~M3.5,8.5 H28.5"), (N, A, -2, "M3.5,8.5 C10,26 22,26 28.5,8.5"), (L, N, 0, Q(3.5, 8.5) + " " + Q(28.5, 8.5))),
            ["normal"] = P((Af, A, -1, "M3.5,9.5 H28.5 V19.5 H3.5 Z"), (N, L, -1, "M2.5,19.5 H29.5")),
            ["cortante"] = P((Af, A, -1, "M3.5,7.5 H16.5 V16.5 H3.5 Z M16.5,16.5 H28.5 V25.5 H16.5 Z"), (N, L, -1, "M2.5,16.5 H29.5")),
            ["momento"] = P((Af, A, -1, "M3.5,7.5 Q16,40 28.5,7.5 Z"), (N, L, -1, "M2.5,7.5 H29.5")),
            ["verif"] = P((N, L, -1, "M3.5,8.5 H25.5 M3.5,8.5 V22.5 M25.5,8.5 V22.5"), (L, N, 0, Q(3.5, 8.5) + " " + Q(25.5, 8.5)), (A, N, 0, "M14,22 L17,19 L20,22 L27,15 L30,18 L20,28 Z")),
            ["comparar"] = P((L, N, 0, "M4,15 H9 V28 H4 Z M18,19 H23 V28 H18 Z"), (A, N, 0, "M11,7 H16 V28 H11 Z"), (N, L2, -1, "M1.5,28.5 H30.5")),
            ["reac"] = P((N, L, -1, "M16.5,4.5 L23.5,15.5 H9.5 Z M4.5,18.5 H28.5"), (A, N, 0, "M15,31 V25 H11.5 L16.5,19.5 L21.5,25 H18 V31 Z")),
            ["valores"] = P((Af, A, -1, "M3.5,6.5 Q16,34 28.5,6.5 Z"), (N, L, -1, "M2.5,6.5 H29.5"), (W, N, 0, "M10,24 H22 V30 H10 Z"), (E, N, 0, "M12,26 H20 V28 H12 Z")),
            ["nomes"] = P((L, N, 0, "M5,27 L13,5 H17 L25,27 H21 L19,21 H11 L9,27 Z M12.2,17.5 H17.8 L15,9.5 Z"), (N, A, -1, "M23.5,28.5 H30.5")),
            ["mais"] = P((Af, A, -1, "M3.5,8.5 Q13,30 22.5,8.5 Z"), (N, L, -1, "M2.5,8.5 H23.5"), (A, N, 0, "M24,17 H27 V22 H32 V25 H27 V30 H24 V25 H19 V22 H24 Z")),
            ["menos"] = P((Af, A, -1, "M3.5,8.5 Q13,30 22.5,8.5 Z"), (N, L, -1, "M2.5,8.5 H23.5"), (A, N, 0, "M19,22 H32 V25 H19 Z")),

            // ---- vista ----
            ["zoomtudo"] = P((N, L, -1, "M10.5,10.5 H21.5 V21.5 H10.5 Z"), (A, N, 0, "M3,3 H10 V5 H5 V10 H3 Z M22,3 H29 V10 H27 V5 H22 Z M29,22 V29 H22 V27 H27 V22 Z M3,22 H5 V27 H10 V29 H3 Z")),
            ["zoommais"] = P((N, L, -2, "M13,4 A9,9 0 1 0 13,22 A9,9 0 1 0 13,4"), (L, N, 0, "M19.5,18 L29,27.5 L27.5,29 L18,19.5 Z"), (A, N, 0, "M8,12 H18 V14 H8 Z M12,8 H14 V18 H12 Z")),
            ["zoommenos"] = P((N, L, -2, "M13,4 A9,9 0 1 0 13,22 A9,9 0 1 0 13,4"), (L, N, 0, "M19.5,18 L29,27.5 L27.5,29 L18,19.5 Z"), (A, N, 0, "M8,12 H18 V14 H8 Z")),
            ["painel"] = P((N, L, -1, "M3.5,4.5 H28.5 V27.5 H3.5 Z M3.5,9.5 H28.5"), (L2, N, 0, "M7,13 H25 V14 H7 Z M7,17 H25 V18 H7 Z M7,21 H18 V22 H7 Z")),
            ["aplicar"] = P((A, N, 0, "M4,16 L8,12 L13,17 L24,6 L28,10 L13,25 Z")),
            ["ajuda"] = P((N, L, -2, "M16,4 A12,12 0 1 0 16,28 A12,12 0 1 0 16,4"), (L, N, 0, "M12,12 A4,4 0 1 1 18,15.5 C16.5,16.3 17,17.5 17,19 H14.5 C14.5,16.5 14.3,15 16.5,13.6 A1.8,1.8 0 1 0 14,12 Z M14.5,21.5 H17 V24 H14.5 Z")),
        };

        public static UIElement Criar(string nome, double tam)
        {
            (string f, string s, double w, string d)[] camadas;
            if (!D.TryGetValue(nome, out camadas)) camadas = D["novo"];
            double k = 32.0 / tam;   // espessura "em pixels" -> unidades do quadro de 32
            var g = new DrawingGroup();
            foreach (var c in camadas)
            {
                string geo = c.d; bool tracejado = geo.StartsWith("~");
                if (tracejado) geo = geo.Substring(1);
                Brush f = c.f == null ? null : new SolidColorBrush((Color)ColorConverter.ConvertFromString(c.f));
                Pen p = null;
                if (c.s != null && c.w != 0)
                {
                    double esp = c.w < 0 ? -c.w * k : c.w;
                    p = new Pen(new SolidColorBrush((Color)ColorConverter.ConvertFromString(c.s)), esp) { LineJoin = PenLineJoin.Miter, StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat };
                    if (tracejado) p.DashStyle = new DashStyle(new double[] { 2, 2 }, 0);
                }
                g.Children.Add(new GeometryDrawing(f, p, Geometry.Parse(geo)));
            }
            if (tam >= 24) g.Transform = new ScaleTransform(1.12, 1.12, 16, 16);   // grande ocupa o quadro de 32 px
            var raiz = new DrawingGroup { ClipGeometry = new RectangleGeometry(new Rect(0, 0, 32, 32)) };
            raiz.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            raiz.Children.Add(g);
            raiz.Freeze();
            return new Image { Source = new DrawingImage(raiz), Width = tam, Height = tam, Stretch = Stretch.Uniform, SnapsToDevicePixels = true };
        }
    }
}
