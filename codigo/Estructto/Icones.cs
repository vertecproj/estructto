// Icones do Estructto v2 - vocabulario unico, desenhado no quadro de 32x32 (ver docs/v2/GUIA-DESIGN.md, item F).
//  * Traco de 2 unidades (= 2 px a 32 px, 1 px a 16 px), pontas e juncoes ARREDONDADAS, sem contorno escuro.
//  * Duotone: o corpo do objeto leva a cor do acento com ~18% de opacidade; o traco leva a cor cheia.
//  * Cores = tokens do TemaPlant e as MESMAS do canvas: estrutura cinza-claro, apoio/selecao azul, carga ambar,
//    reacao verde, deformada turquesa, N azul/vermelho, V roxo, M verde. Remocao = vermelho.
//  * Os apoios, cargas, esforcos, ligacoes e ferramentas CAD sao desenho proprio; os glifos de UI geral
//    (busca, fechar, filtro, olho, config, setas, aviso...) vem do Tabler Icons (MIT) em IconesTabler.cs.
//  * Pixel-snapping: as coordenadas sao convertidas para pixels de tela (considerando o DPI) e encaixadas na grade
//    (tracos de largura impar caem em n+0,5, pares em n) - por isso o icone de 16 px nao borra.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NovoFtool
{
    public static partial class Icones
    {
        sealed class Cam { public Color? F; public Color? S; public double W; public string D; public bool Tracejado; public double Q = 32; }

        // ---- cores (tokens) ----
        static readonly Color L = TemaPlant.CorBarra,       // estrutura / objeto
                              L2 = TemaPlant.Texto2,        // secundario, hachuras
                              A = TemaPlant.Realce,         // azul: apoio, selecao, resultado
                              Y = TemaPlant.CorCarga,       // ambar: cargas
                              R = TemaPlant.Erro,           // vermelho: remover, compressao
                              G = TemaPlant.CorReacao,      // verde: reacoes / ok
                              Mo = TemaPlant.CorMomento,    // verde: momento fletor
                              Vc = TemaPlant.CorCortante,   // roxo: cortante
                              Np = TemaPlant.CorNormalPos,  // azul: normal de tracao
                              Df = TemaPlant.CorDeformada;  // turquesa: deformada
        const byte AlfaDuo = 0x2E;   // ~18%

        static string F(string fmt, params object[] a) { return string.Format(CultureInfo.InvariantCulture, fmt, a); }
        static Color Alfa(Color c, byte a) { return Color.FromArgb(a, c.R, c.G, c.B); }

        // ---- camadas ----
        static Cam[] S(Color c, string d, double w = 2) { return new[] { new Cam { S = c, W = w, D = d } }; }
        static Cam[] Sd(Color c, string d, double w = 2) { return new[] { new Cam { S = c, W = w, D = d, Tracejado = true } }; }
        static Cam[] Du(Color c, string d, double w = 2) { return new[] { new Cam { F = Alfa(c, AlfaDuo), S = c, W = w, D = d } }; }
        static Cam[] Fa(Color c, string d, byte a = AlfaDuo) { return new[] { new Cam { F = Alfa(c, a), D = d } }; }
        static Cam[] Fb(Color c, string d) { return new[] { new Cam { F = c, D = d } }; }
        static Cam[] Pt(Color c, double x, double y, double r = 2) { return Fb(c, F("M{0},{1} a{2},{2} 0 1 0 {3},0 a{2},{2} 0 1 0 {4},0 Z", x - r, y, r, 2 * r, -2 * r)); }
        static Cam[] Hd(double x, double y) { return Fb(A, F("M{0},{1} h5 v5 h-5 Z", x - 2.5, y - 2.5)); }   // alca quadrada azul 5x5
        static Cam[] Cx(double x, double y, double r = 5) { return Du(Y, F("M{0},{1} a{2},{2} 0 1 0 {3},0 a{2},{2} 0 1 0 {4},0 Z", x - r, y, r, 2 * r, -2 * r)); }
        static Cam[] Xr(double x, double y) { return S(R, F("M{0},{1} l6,6 M{2},{1} l-6,6", x, y, x + 6)); }   // X vermelho 6x6 (remover)
        static Cam[] C(params Cam[][] p) { return p.SelectMany(x => x).ToArray(); }

        // glifo do Tabler (grade 24): paths a partir de 'acentoDe' recebem a cor 'ac'
        static Cam[] TB(string chave, Color c, Color? ac = null, int acentoDe = 99, int acentoAte = 99)
        {
            var r = new List<Cam>();
            string[] ds = TBD[chave];
            for (int i = 0; i < ds.Length; i++)
                r.Add(new Cam { S = (ac != null && i >= acentoDe && i < acentoAte) ? ac.Value : c, W = 1.5, D = ds[i], Q = 24 });
            return r.ToArray();
        }

        static Dictionary<string, Cam[]> dic;
        static Dictionary<string, Cam[]> D { get { return dic ?? (dic = Montar()); } }   // preguicoso: TBD (outro arquivo parcial) precisa estar pronto

        static Dictionary<string, Cam[]> Montar()
        {
            return new Dictionary<string, Cam[]>
            {
                // ---------------- arquivo / acesso rapido (Tabler) ----------------
                ["novo"] = TB("novo", L, A, 1, 2),
                ["abrir"] = TB("abrir", L),
                ["salvar"] = TB("salvar", L),
                ["desfazer"] = TB("desfazer", L),
                ["refazer"] = TB("refazer", L),
                ["importar"] = C(TB("importar", L), S(A, "M10,22 H21 M16,17 L21,22 L16,27", 2)),
                ["exportar"] = TB("exportar", L, A, 1, 99),
                ["relatorio"] = TB("relatorio", L, A, 2, 99),
                ["tabela"] = C(Fa(A, "M3.5,3.5 H28.5 V11.5 H3.5 Z"), TB("tabela", L)),

                // ---------------- desenhar / modificar ----------------
                ["sel"] = Du(L, "M9,5 V24 L13.5,19.5 L17,27 L20.5,25.5 L17,18.5 H23.5 Z"),
                ["barra"] = C(S(L, "M7,25 L25,7"), Hd(7, 25), Hd(25, 7)),
                ["no"] = C(S(L2, "M3,16 H11 M21,16 H29"), Du(A, "M11,16 a5,5 0 1 0 10,0 a5,5 0 1 0 -10,0 Z")),
                ["medir"] = TB("medir", L, A, 6, 99),
                ["apagar"] = TB("apagar", L),
                ["dividir"] = C(S(L, "M4,16 H28"), S(A, "M10,11 V21 M16,11 V21 M22,11 V21")),
                ["mover"] = TB("mover", L),
                ["copiar"] = TB("copiar", A, L2, 1, 2),
                ["espelhar"] = C(TB("espelhar", L2, A, 1, 2)),
                ["assist"] = C(S(L, "M5,27 L17,15"), Du(A, "M22,3 Q22,9 28,9 Q22,9 22,15 Q22,9 16,9 Q22,9 22,3 Z", 1.5), Du(L2, "M10,6 Q10,8.5 12.5,8.5 Q10,8.5 10,11 Q10,8.5 7.5,8.5 Q10,8.5 10,6 Z", 1.5)),
                ["dimens"] = C(Du(L, "M6,5 H20 V8.5 H14.5 V21.5 H20 V25 H6 V21.5 H11.5 V8.5 H6 Z"), Du(A, "M25,13 Q25,18 30,18 Q25,18 25,23 Q25,18 20,18 Q25,18 25,13 Z", 1.5)),

                ["unir"] = C(S(L, "M3,21 H14 M18,21 H29"), S(A, "M10,15 V10 H22 V15")),
                ["tirar"] = C(S(L, "M3,22 H11 M21,22 H29"), S(A, "M11,12 H21"), S(A, "M16,19 V8 M12.5,11.5 L16,8 L19.5,11.5")),
                ["separar"] = C(S(L, "M3,16 H10 M13,16 H19 M22,16 H29"), S(A, "M11.5,10 V22 M20.5,10 V22")),
                ["selmembro"] = C(S(A, "M6,16 H26"), Hd(5, 16), Hd(16, 16), Hd(27, 16)),

                // ---------------- apoios (estrutura cinza, apoio em azul) ----------------
                ["engaste"] = C(S(L, "M16,4 V20"), S(A, "M6,20 H26", 3), S(L2, "M10,23 L7,27 M16,23 L13,27 M22,23 L19,27 M28,23 L25,27", 1.5)),
                ["rotula"] = C(S(L, "M16,3 V9"), Du(A, "M16,9 L24,21 H8 Z"), S(A, "M5,21 H27"), S(L2, "M10,24 L7,28 M16,24 L13,28 M22,24 L19,28 M28,24 L25,28", 1.5)),
                ["rolo"] = C(S(L, "M16,3 V7"), Du(A, "M16,7 L23,17 H9 Z"), Pt(L2, 12, 20, 2.2), Pt(L2, 20, 20, 2.2), S(A, "M5,24 H27"), S(L2, "M9,27 L8,28 M15,27 L14,28 M21,27 L20,28", 1.5)),
                ["roloh"] = C(S(L, "M3,16 H7"), Du(A, "M7,16 L17,9 V23 Z"), Pt(L2, 20, 12, 2.2), Pt(L2, 20, 20, 2.2), S(A, "M24,5 V27")),
                ["livre"] = C(Sd(L2, "M16,7 L24,20 H8 Z"), Sd(L2, "M5,23 H27"), Xr(21, 21)),
                ["rotulab"] = C(S(L, "M2,16 H11 M21,16 H30"), Du(A, "M11,16 a5,5 0 1 0 10,0 a5,5 0 1 0 -10,0 Z")),
                ["placa"] = C(S(L, "M12,3 V18 M20,3 V18"), Du(A, "M5,18 H27 V22 H5 Z"), S(L2, "M9,22 V29 M23,22 V29")),
                ["parafuso"] = C(Du(L, "M9,4 H23 V9 H9 Z"), S(L, "M13,9 V28 M19,9 V28"), S(L2, "M13,14 H19 M13,19 H19 M13,24 H19", 1.5)),
                ["solda"] = C(Du(L2, "M3,22 H29 V27 H3 Z"), Du(L, "M11,3 H15 V22 H11 Z"), Du(Y, "M15,22 V15 L22,22 Z")),
                ["eixo"] = C(Du(L, "M7,5 H25 V9 H18 V23 H25 V27 H7 V23 H14 V9 H7 Z"), Sd(A, "M2,16 H30", 1.5)),
                ["pincel"] = TB("pincel", L, A, 1, 99),

                // ---------------- cargas (ambar, como no canvas) ----------------
                ["cargano"] = C(S(Y, "M16,4 V20"), S(Y, "M10,15 L16,21 L22,15"), S(L, "M4,27 H28"), Pt(L, 16, 27, 2.5)),
                ["cargadist"] = C(Fa(Y, "M4,6 H28 V17 H4 Z"), S(Y, "M4,6 H28"), S(Y, "M7,6 V19 M16,6 V19 M25,6 V19"), S(Y, "M4,15 L7,19 L10,15 M13,15 L16,19 L19,15 M22,15 L25,19 L28,15"), S(L, "M3,26 H29")),
                ["cargamom"] = C(S(Y, "M9.6,22.4 A9,9 0 1 1 22.4,22.4"), Fb(Y, "M19.6,25.2 L25.9,24.5 L20.3,18.9 Z"), Pt(L, 16, 16, 2.5)),
                ["peso"] = C(Du(Y, "M9,13 H23 L27,27 H5 Z"), S(Y, "M12.5,13 A3.5,3.5 0 1 1 19.5,13")),
                ["limpar"] = C(S(Y, "M4,6 H22 M7,6 V16 M13,6 V16 M19,6 V16"), S(Y, "M4,14 L7,17 L10,14 M10,14 L13,17 L16,14 M16,14 L19,17 L22,14", 1.5), S(L, "M3,25 H20"), Xr(21, 18)),

                // ---------------- analise / resultados ----------------
                ["calc"] = Du(A, "M9,5.5 L26,16 L9,26.5 Z"),
                ["def"] = C(Sd(L2, "M4,9 H28", 1.5), S(Df, "M4,9 C10,29 22,29 28,9"), Pt(L2, 4, 9, 2.2), Pt(L2, 28, 9, 2.2)),
                ["normal"] = C(Fa(Np, "M5,8 H27 V16 H5 Z"), S(Np, "M5,16 V8 H27 V16"), S(L2, "M3,16 H29", 1.5)),
                ["cortante"] = C(Fa(Vc, "M5,16 V8 H16 V24 H27 V16 Z"), S(Vc, "M5,16 V8 H16 V24 H27 V16"), S(L2, "M3,16 H29", 1.5)),
                ["momento"] = C(Fa(Mo, "M5,8 Q16,32 27,8 Z"), S(Mo, "M5,8 Q16,32 27,8"), S(L2, "M3,8 H29", 1.5)),
                ["verif"] = C(Fa(L2, "M4,5 H28 V10 H4 Z", 0x26), Fb(G, "M4,5 H17 V10 H4 Z"), Fa(L2, "M4,13 H28 V18 H4 Z", 0x26), Fb(Y, "M4,13 H23 V18 H4 Z"), Fa(L2, "M4,21 H28 V26 H4 Z", 0x26), Fb(R, "M4,21 H28 V26 H4 Z")),
                ["comparar"] = C(Du(L2, "M5,15 H10 V27 H5 Z"), Du(A, "M13.5,6 H18.5 V27 H13.5 Z"), Du(L2, "M22,19 H27 V27 H22 Z"), S(L2, "M3,27 H29")),
                ["reac"] = C(Du(L, "M16,4 L23,15 H9 Z"), S(L2, "M5,18 H27"), S(G, "M16,29 V21"), S(G, "M12,24 L16,20 L20,24")),
                ["valores"] = C(Fa(Mo, "M4,5 Q16,25 28,5 Z"), S(Mo, "M4,5 Q16,25 28,5"), Du(L2, "M8,19 H24 V28 H8 Z", 1.5), S(L, "M11,22.5 H21 M11,25.5 H17", 1.5)),
                ["nomes"] = C(S(L, "M6,26 L16,5 L26,26 M9.5,19 H22.5")),
                ["mais"] = C(Fa(Mo, "M3,6 Q12,26 21,6 Z"), S(Mo, "M3,6 Q12,26 21,6"), S(L2, "M2,6 H22", 1.5), S(A, "M24,19 V29 M19,24 H29")),
                ["menos"] = C(Fa(Mo, "M3,6 Q12,26 21,6 Z"), S(Mo, "M3,6 Q12,26 21,6"), S(L2, "M2,6 H22", 1.5), S(A, "M19,24 H29")),

                // ---------------- vista ----------------
                ["zoomtudo"] = C(Du(A, "M11,11 H21 V21 H11 Z"), TB("zoomtudo", L)),
                ["zoommais"] = TB("zoommais", L, A, 1, 3),
                ["3d"] = TB("3d", L, A, 1, 4),
                ["zoommenos"] = TB("zoommenos", L, A, 1, 2),
                ["painel"] = TB("painel", L),
                ["aplicar"] = TB("check", A),
                ["ajuda"] = TB("ajuda", L),

                // ---------------- barra de status ----------------
                ["grade"] = TB("grade", L2),
                ["snap"] = TB("ima", L2),
                ["ortho"] = C(S(L2, "M7,5 V25 H27"), S(A, "M7,25 H13", 2)),
                ["polar"] = TB("angulo", L2),
                ["objsnap"] = C(Du(A, "M10,10 H22 V22 H10 Z"), S(L2, "M16,3 V10 M16,22 V29 M3,16 H10 M22,16 H29")),
                ["rastro"] = C(Sd(L2, "M16,3 V29 M3,16 H29", 1.5), Du(A, "M11,16 a5,5 0 1 0 10,0 a5,5 0 1 0 -10,0 Z")),

                // ---------------- UI geral (Tabler) ----------------
                ["busca"] = TB("busca", L2),
                ["fechar"] = TB("fechar", L2),
                ["filtro"] = TB("filtro", L2),
                ["olho"] = TB("olho", L2),
                ["olhooff"] = TB("olhooff", L2),
                ["config"] = TB("config", L2),
                ["setad"] = TB("setad", L2),
                ["setae"] = TB("setae", L2),
                ["setac"] = TB("setac", L2),
                ["setab"] = TB("setab", L2),
                ["maismais"] = TB("maismais", L2),
                ["menosmenos"] = TB("menosmenos", L2),
                ["chevb"] = TB("chevb", L2),
                ["chevd"] = TB("chevd", L2),
                ["aviso"] = TB("aviso", Y),
                ["info"] = TB("info", A),
                ["ok"] = TB("ok", G),
                ["erro"] = TB("erro", R),
            };
        }

        // ordem das folhas de icones (00-icones.png)
        public static readonly string[] Nomes = {
            "novo","abrir","salvar","desfazer","refazer","importar","exportar","relatorio","tabela",
            "sel","barra","no","medir","apagar","dividir","mover","copiar","espelhar","assist","dimens",
            "engaste","rotula","rolo","roloh","livre","rotulab","placa","parafuso","solda","eixo","pincel",
            "cargano","cargadist","cargamom","peso","limpar",
            "calc","def","normal","cortante","momento","verif","comparar","reac","valores","unir","tirar","separar","selmembro","nomes","mais","menos",
            "zoomtudo","zoommais","zoommenos","3d","painel","aplicar","ajuda",
            "grade","snap","ortho","polar","objsnap","rastro",
            "busca","fechar","filtro","olho","olhooff","config","setad","setae","setac","setab","maismais","menosmenos","chevb","chevd","aviso","info","ok","erro" };

        // ---------------- motor ----------------
        static double DpiAtual()
        {
            try { var w = Application.Current?.MainWindow; if (w != null && w.IsLoaded) return VisualTreeHelper.GetDpi(w).DpiScaleX; } catch { }
            return 1.0;
        }

        static readonly Regex Tok = new Regex(@"[MmLlHhVvCcSsQqAaZz]|-?(?:\d*\.\d+|\d+\.?)(?:[eE][-+]?\d+)?", RegexOptions.Compiled);

        // converte o "d" (origem em unidades do quadro q) para pixels de tela, tudo absoluto, com os pontos encaixados na grade
        static string Mapear(string d, double s, bool traco, int wpx, double q = 32, double ampl = 1)
        {
            Func<double, double> sn = v => !traco ? Math.Round(v) : (wpx % 2 == 1 ? Math.Floor(v + 0.0001) + 0.5 : Math.Round(v));
            // ponto de partida do encaixe: traco impar -> centro do pixel; como floor(v)+0.5 pode puxar para baixo, usa-se round(v-0.5)+0.5
            if (traco && wpx % 2 == 1) sn = v => Math.Round(v - 0.5) + 0.5;
            var m = Tok.Matches(d);
            var t = new List<string>(); foreach (Match x in m) t.Add(x.Value);
            var sb = new StringBuilder();
            int i = 0; char cmd = 'M';
            double cx = 0, cy = 0, sx = 0, sy = 0, lcx = 0, lcy = 0; char prev = ' ';
            Func<double> num = () => double.Parse(t[i++], CultureInfo.InvariantCulture);
            double off = q * s * (1 - ampl) / 2;   // ampliacao em torno do centro (icones de 16 px ganham ~12%)
            s = s * ampl;
            Action<double, double> pt = (x, y) => sb.Append(F("{0:0.###},{1:0.###} ", sn(x * s + off), sn(y * s + off)));
            Action<double, double> cp = (x, y) => sb.Append(F("{0:0.###},{1:0.###} ", x * s + off, y * s + off));   // pontos de controle: sem encaixe
            while (i < t.Count)
            {
                if (char.IsLetter(t[i][0])) { cmd = t[i][0]; i++; if (cmd == 'Z' || cmd == 'z') { sb.Append("Z "); cx = sx; cy = sy; prev = 'z'; continue; } }
                bool rel = char.IsLower(cmd);
                switch (char.ToUpperInvariant(cmd))
                {
                    case 'M': { double x = num(), y = num(); if (rel) { x += cx; y += cy; } cx = sx = x; cy = sy = y; sb.Append("M "); pt(x, y); cmd = rel ? 'l' : 'L'; prev = 'M'; break; }
                    case 'L': { double x = num(), y = num(); if (rel) { x += cx; y += cy; } cx = x; cy = y; sb.Append("L "); pt(x, y); prev = 'L'; break; }
                    case 'H': { double x = num(); if (rel) x += cx; cx = x; sb.Append("L "); pt(cx, cy); prev = 'H'; break; }
                    case 'V': { double y = num(); if (rel) y += cy; cy = y; sb.Append("L "); pt(cx, cy); prev = 'V'; break; }
                    case 'C': { double x1 = num(), y1 = num(), x2 = num(), y2 = num(), x = num(), y = num(); if (rel) { x1 += cx; y1 += cy; x2 += cx; y2 += cy; x += cx; y += cy; } sb.Append("C "); cp(x1, y1); cp(x2, y2); pt(x, y); lcx = x2; lcy = y2; cx = x; cy = y; prev = 'C'; break; }
                    case 'S': { double x2 = num(), y2 = num(), x = num(), y = num(); if (rel) { x2 += cx; y2 += cy; x += cx; y += cy; } double x1 = prev == 'C' ? 2 * cx - lcx : cx, y1 = prev == 'C' ? 2 * cy - lcy : cy; sb.Append("C "); cp(x1, y1); cp(x2, y2); pt(x, y); lcx = x2; lcy = y2; cx = x; cy = y; prev = 'C'; break; }
                    case 'Q': { double x1 = num(), y1 = num(), x = num(), y = num(); if (rel) { x1 += cx; y1 += cy; x += cx; y += cy; } sb.Append("Q "); cp(x1, y1); pt(x, y); cx = x; cy = y; prev = 'Q'; break; }
                    case 'A': { double rx = num(), ry = num(), rot = num(), la = num(), sw = num(), x = num(), y = num(); if (rel) { x += cx; y += cy; } double rxs = rx * s, rys = ry * s; if (Math.Abs(rx - ry) < 1e-6 && Math.Abs(Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) - 2 * rx) < 0.05) { double dx = sn(x * s + off) - sn(cx * s + off), dy = sn(y * s + off) - sn(cy * s + off); rxs = rys = Math.Sqrt(dx * dx + dy * dy) / 2; } sb.Append(F("A {0:0.###},{1:0.###} {2} {3} {4} ", rxs, rys, rot, la, sw)); pt(x, y); cx = x; cy = y; prev = 'A'; break; }
                    default: i++; break;
                }
            }
            return sb.ToString();
        }

        static readonly Dictionary<string, ImageSource> cache = new Dictionary<string, ImageSource>();
        static readonly Dictionary<Color, Brush> pinceis = new Dictionary<Color, Brush>();
        static Brush Pincel(Color c) { Brush b; if (!pinceis.TryGetValue(c, out b)) { var sb = new SolidColorBrush(c); sb.Freeze(); pinceis[c] = b = sb; } return b; }

        static ImageSource Fonte(string nome, double tam)
        {
            double dpi = DpiAtual();
            string chave = nome + "|" + tam.ToString(CultureInfo.InvariantCulture) + "|" + dpi.ToString(CultureInfo.InvariantCulture);
            ImageSource src;
            if (cache.TryGetValue(chave, out src)) return src;
            Cam[] camadas;
            if (!D.TryGetValue(nome, out camadas)) camadas = D["novo"];
            double px = tam * dpi;   // lado do icone em pixels de tela
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, px, px))));
            foreach (var c in camadas)
            {
                double s = px / c.Q;
                int wpx = c.S != null ? Math.Max(1, (int)Math.Round(c.W * s, MidpointRounding.AwayFromZero)) : 0;
                Geometry geo = Geometry.Parse(Mapear(c.D, s, c.S != null && wpx > 0, wpx, c.Q, tam <= 20 ? 1.12 : 1.0));
                if (geo.CanFreeze) geo.Freeze();
                Pen p = null;
                if (c.S != null && wpx > 0)
                {
                    p = new Pen(Pincel(c.S.Value), wpx) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                    if (c.Tracejado) { p.DashStyle = new DashStyle(new double[] { 2, 2 }, 0); p.DashCap = PenLineCap.Flat; }
                    p.Freeze();
                }
                g.Children.Add(new GeometryDrawing(c.F != null ? Pincel(c.F.Value) : null, p, geo));
            }
            var raiz = new DrawingGroup { Transform = new ScaleTransform(1 / dpi, 1 / dpi) };
            raiz.Children.Add(g);
            raiz.Freeze();
            var di = new DrawingImage(raiz);
            di.Freeze();
            cache[chave] = di;
            return di;
        }

        public static UIElement Criar(string nome, double tam)
        {
            return new Image { Source = Fonte(nome, tam), Width = tam, Height = tam, Stretch = Stretch.None, SnapsToDevicePixels = true, UseLayoutRounding = true };
        }

        // para botoes que trocam a cor do glifo (rodape): so o contorno, uma cor, geometria no quadro 'tam'
        public static bool Existe(string nome) { return D.ContainsKey(nome); }
    }
}
