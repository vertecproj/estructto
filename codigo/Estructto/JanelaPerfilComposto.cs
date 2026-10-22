// v0.6 Frente D. JanelaComposto: cria barra composta (2 perfis ou I + chapa) a partir do perfil base do catálogo; o motor
// calcula as propriedades (secoes.secao_dupla / secao_I_chapa, spec {"forma":"dupla"}). JanelaDisposicao: giro, espelhar e
// ponto cardinal das barras escolhidas, com o desenho da seção sobre o eixo. Desenho: GeometriaPerfis.CriarContorno (o do 3D).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using static NovoFtool.UiAnalise;
using static NovoFtool.UiPlaca;
using Portico;

namespace NovoFtool
{
    public static class DesenhoSecao
    {
        // Seção girada de giro (°) com o ponto cardinal sobre o eixo (cruz vermelha no centro do quadro).
        public static Canvas Desenhar(Perfil3D p, double giroGraus, int cardinal, double tam = 220)
        {
            var cv = new Canvas { Width = tam, Height = tam, Background = TemaPlant.BSup2, ClipToBounds = true };
            var f = GeometriaPerfis.CriarContorno(p, 0.1);
            Point dc = GeometriaPerfis.Cardinal(f, cardinal);
            double a = giroGraus * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
            double esc = 0.36 * tam / Math.Max(Math.Max(f.B, f.H), 1e-6);
            Point T(Point q) { double x = q.X + dc.X, y = q.Y + dc.Y; return new Point(tam / 2 + esc * (c * x - s * y), tam / 2 - esc * (s * x + c * y)); }
            foreach (var l in f.Lacos)
                cv.Children.Add(new Polygon { Points = new PointCollection(l.Select(T)), Fill = TemaPlant.BTexto3, Stroke = TemaPlant.BTexto1, StrokeThickness = 1 });
            cv.Children.Add(new Line { X1 = tam / 2 - 12, X2 = tam / 2 + 12, Y1 = tam / 2, Y2 = tam / 2, Stroke = Brushes.Red, StrokeThickness = 2 });
            cv.Children.Add(new Line { X1 = tam / 2, X2 = tam / 2, Y1 = tam / 2 - 12, Y2 = tam / 2 + 12, Stroke = Brushes.Red, StrokeThickness = 2 });
            return cv;
        }
    }

    public class JanelaComposto : JanelaEscura
    {
        public event Action<JsonObject> Criar;   // spec da seção composta para POST /api/modelo/secao
        static readonly Dictionary<string, (string Arr, string Rot)[]> Arranjos = new Dictionary<string, (string, string)[]>
        {
            ["U"] = new[] { ("caixao", "caixão (abas para dentro)"), ("I", "costas com costas (abas para fora)") },
            ["L"] = new[] { ("I", "costas com costas (T)"), ("cruz", "em cruz"), ("caixao", "em caixão (s = 0, soldado)") },
            ["I"] = new[] { ("lado_a_lado", "lado a lado"), ("I_chapa", "I + chapa na mesa superior") },
        };
        static readonly (string Lig, string Rot)[] Ligacoes = { ("soldada", "soldada contínua"), ("presilhas", "chapas espaçadoras/presilhas"), ("generica", "genérica") };

        public JanelaComposto(string baseNome, JsonNode baseSecao)
        {
            Moldar(this, "Perfil composto", 640, 470);
            var pb = Perfil3D.DoMotor(baseSecao as JsonObject);
            string forma = pb?.Forma == "L" ? "L" : pb?.Forma == "U" ? "U" : pb?.Forma == "I" ? "I" : null;
            var cab = Cabecalho(Nivel.Info, "⧉", "Perfil composto de " + baseNome,
                forma == null ? "Perfil base não suportado: use U laminado, cantoneira ou I." : "Propriedades por Steiner no motor; NBR 8800 5.3.4 (esbeltez de cada perfil entre ligações ≤ ½ da do conjunto).");
            var host = new Border();
            var lst = new StackPanel { Width = 330 };
            var arr = forma == null ? new (string, string)[0] : Arranjos[forma];
            var cbArr = new ComboBox { Width = 240 }; foreach (var a in arr) cbArr.Items.Add(a.Item2); cbArr.SelectedIndex = arr.Length > 0 ? 0 : -1;
            var cbLig = new ComboBox { Width = 240 }; foreach (var l in Ligacoes) cbLig.Items.Add(l.Rot); cbLig.SelectedIndex = 0;
            TextBox tbS = Campo("0", 80, "Afastamento entre os perfis (mm)"), tbL = Campo("", 80, "Distância entre presilhas/chapas espaçadoras (mm), para 5.3.4"),
                    tbBp = Campo(pb != null ? (pb.B * 1e3).ToString("0", CultureInfo.InvariantCulture) : "", 80, "Largura da chapa (mm)"), tbTp = Campo("10", 80, "Espessura da chapa (mm)");
            double Mm(TextBox t) => Num.Ler(t.Text, out double v) && v >= 0 ? v / 1000 : 0;
            string Arr() => cbArr.SelectedIndex >= 0 ? arr[cbArr.SelectedIndex].Item1 : null;
            void Prever()
            {
                if (pb == null || Arr() == null) return;
                host.Child = DesenhoSecao.Desenhar(new Perfil3D { Forma = "DUPLA", Arranjo = Arr(), S = Mm(tbS), ChapaB = Mm(tbBp), ChapaT = Mm(tbTp), Comp = pb }, 0, 5, 240);
            }
            foreach (var t in new[] { tbS, tbBp, tbTp }) t.TextChanged += (s, e) => Prever();
            cbArr.SelectionChanged += (s, e) => Prever();
            lst.Children.Add(UiAcoes.Linha("Arranjo", cbArr, 90));
            lst.Children.Add(UiAcoes.Linha("Ligação", cbLig, 90));
            lst.Children.Add(UiAcoes.Linha("s (mm)", tbS, 90));
            lst.Children.Add(UiAcoes.Linha("Presilhas a (mm)", tbL, 90));
            lst.Children.Add(UiAcoes.Linha("Chapa b (mm)", tbBp, 90, "Só I + chapa"));
            lst.Children.Add(UiAcoes.Linha("Chapa t (mm)", tbTp, 90, "Só I + chapa"));
            var corpo = new DockPanel { Margin = new Thickness(12) };
            DockPanel.SetDock(lst, Dock.Left); corpo.Children.Add(lst); corpo.Children.Add(host);
            var msg = Tx("", 11.5, TemaPlant.BTexto3);
            Content = Raiz(cab, corpo, Rodape(msg, B("Cancelar", Close), B("Criar composto", () =>
            {
                string a = Arr(); if (a == null) return;
                double s = Mm(tbS), l = Mm(tbL);
                string lig = Ligacoes[Math.Max(cbLig.SelectedIndex, 0)].Lig;
                string rot = arr[cbArr.SelectedIndex].Item2.Split(' ')[0];
                var spec = new JsonObject { ["forma"] = "dupla", ["base"] = new JsonObject { ["perfil"] = baseNome }, ["arranjo"] = a };
                if (a == "I_chapa")
                {
                    spec["chapa"] = new JsonObject { ["b"] = Mm(tbBp), ["t"] = Mm(tbTp) };
                    spec["nome"] = baseNome + " + CH " + Num.Fmt(Mm(tbBp) * 1e3, 1) + "x" + Num.Fmt(Mm(tbTp) * 1e3, 1);
                }
                else
                {
                    spec["ligacao"] = lig; spec["s"] = s;
                    if (l > 0) spec["l_ligacoes"] = l;
                    if (lig == "presilhas") spec["chapas_espacadoras"] = true;
                    spec["nome"] = "2" + baseNome + " " + a + " " + lig + (s > 0 ? " s=" + Num.Fmt(s * 1e3, 1) : "");
                }
                Criar?.Invoke(spec); Close();
            }, true, "Cria a seção no modelo (o motor calcula A, I, W, Z, r, J, Cw).")));
            Prever();
        }
    }

    public class JanelaDisposicao : JanelaEscura
    {
        public event Action<double, int> Aplicar;   // giro (°) e cardinal (1..10)
        public double Giro; public int Cardinal;

        public JanelaDisposicao(Perfil3D p, double giro, int cardinal, int nBarras)
        {
            Giro = giro; Cardinal = cardinal;
            Moldar(this, "Disposição do perfil", 560, 440);
            var cab = Cabecalho(Nivel.Info, "⊕", "Disposição do perfil · " + nBarras + (nBarras == 1 ? " barra" : " barras"),
                "Giro em torno do eixo, espelho e ponto cardinal (cruz vermelha = eixo da barra). A análise usa o eixo; o desenho desloca o perfil.");
            var host = new Border();
            void Prever() => host.Child = DesenhoSecao.Desenhar(p, Giro, Cardinal, 240);
            var tbG = Campo(Num.Fmt(giro, 1), 70, "Giro livre (°)");
            tbG.TextChanged += (s, e) => { if (Num.Ler(tbG.Text, out double g)) { Giro = g; Prever(); } };
            var giros = new WrapPanel();
            foreach (int g in new[] { 0, 90, 180, 270 }) giros.Children.Add(B(g + "°", () => tbG.Text = g.ToString()));
            // ponytail: espelhar = +180° (exato para I, U, tubos e compostas simétricas; cantoneira simples gira em vez de espelhar)
            giros.Children.Add(B("Espelhar", () => tbG.Text = Num.Fmt((Giro + 180) % 360, 1), false, "Espelha o perfil (giro + 180°)."));
            var grade = new UniformGrid { Rows = 3, Columns = 3, Width = 180, Margin = new Thickness(0, 8, 0, 0) };
            foreach (int c in new[] { 7, 8, 9, 4, 5, 6, 1, 2, 3 }) { int cc = c; grade.Children.Add(B(c.ToString(), () => { Cardinal = cc; Prever(); })); }
            var esq = new StackPanel { Width = 270 };
            esq.Children.Add(UiAcoes.Linha("Giro (°)", tbG, 70));
            esq.Children.Add(giros);
            esq.Children.Add(Tx("Ponto cardinal", 12, TemaPlant.BTexto2));
            esq.Children.Add(grade);
            esq.Children.Add(B("Centroide (10)", () => { Cardinal = 10; Prever(); }));
            var corpo = new DockPanel { Margin = new Thickness(12) };
            DockPanel.SetDock(esq, Dock.Left); corpo.Children.Add(esq); corpo.Children.Add(host);
            Content = Raiz(cab, corpo, Rodape(Tx("Um Ctrl+Z desfaz.", 11.5, TemaPlant.BTexto3), B("Cancelar", Close),
                B("Aplicar", () => { Aplicar?.Invoke(Giro, Cardinal); Close(); }, true)));
            Prever();
        }
    }
}
