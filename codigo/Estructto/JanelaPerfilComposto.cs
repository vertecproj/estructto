// v0.6 Frente D. JanelaComposto: cria barra composta (2 perfis ou I + chapa) a partir do perfil base do catálogo; o motor
// calcula as propriedades (secoes.secao_dupla / secao_I_chapa, spec {"forma":"dupla"}). JanelaDisposicao: giro, espelhar e
// ponto cardinal das barras escolhidas, com o desenho da seção sobre o eixo. Desenho: GeometriaPerfis.CriarContorno (o do 3D).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
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
        public static Canvas Desenhar(Perfil3D p, double giroGraus, int cardinal, double tam = 220, bool espelho = false)
        {
            var cv = new Canvas { Width = tam, Height = tam, Background = TemaPlant.BSup2, ClipToBounds = true };
            var f = GeometriaPerfis.CriarContorno(p, 0.1);
            if (espelho) f = GeometriaPerfis.Espelhar(f);
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
                var spec = new JsonObject { ["forma"] = "dupla", ["base"] = new JsonObject { ["perfil"] = baseNome }, ["arranjo"] = a };
                if (a == "I_chapa")
                {
                    spec["chapa"] = new JsonObject { ["b"] = Mm(tbBp), ["t"] = Mm(tbTp) };
                    spec["nome"] = baseNome + " + CH " + Num.Fmt(Mm(tbBp) * 1e3, 1) + "x" + Num.Fmt(Mm(tbTp) * 1e3, 1);
                }
                else spec = Composto.Spec(new JsonObject { ["perfil"] = baseNome }, baseNome, a, lig, s, l);
                Criar?.Invoke(spec); Close();
            }, true, "Cria a seção no modelo (o motor calcula A, I, W, Z, r, J, Cw).")));
            Prever();
        }
    }

    // W9: perfil duplo a partir de um perfil simples (2U caixão/costas, 2L costas/cruz, 2I lado a lado): nome, spec do motor
    // ({"forma":"dupla"}, secoes.secao_dupla) e o perfil base de uma seção do modelo. Ver docs/v3/W9-COMPOSTO.md.
    public static class Composto
    {
        public static readonly Dictionary<string, (string Arr, string Rot, string Dica)[]> Opcoes = new Dictionary<string, (string, string, string)[]>
        {
            ["U"] = new[] { ("", "Simples", "Um perfil só."), ("caixao", "Caixão", "Dois U boca a boca (abas para dentro): vira um tubo."),
                            ("I", "Costas com costas", "Dois U encostados pelas almas (abas para fora): vira um I.") },
            ["L"] = new[] { ("", "Simples", "Um perfil só."), ("I", "Costas com costas", "Duas cantoneiras pelas abas: vira um T."), ("cruz", "Cruz", "Duas cantoneiras pelas quinas (estrela).") },
            ["I"] = new[] { ("", "Simples", "Um perfil só."), ("lado_a_lado", "Lado a lado", "Dois I com as mesas afastadas s.") },
        };
        static readonly Dictionary<string, string> Palavra = new Dictionary<string, string> { ["caixao"] = "caixao", ["I"] = "costas", ["cruz"] = "cruz", ["lado_a_lado"] = "lado-a-lado" };

        public static string Forma(Perfil3D p) => p?.Forma == "U" || p?.Forma == "L" || p?.Forma == "I" ? p.Forma : null;

        // "2UPN 200 caixao soldado", "2U 150x50x3,00 costas chapas a=500 s=10": o nome leva tudo o que muda o cálculo
        public static string Nome(string baseNome, string arr, string lig, double s, double a) =>
            "2" + baseNome + " " + Palavra[arr] + (lig == "presilhas" ? " chapas" + (a > 0 ? " a=" + Num.Fmt(a * 1e3, 0) : "") : lig == "generica" ? " generica" : " soldado")
            + (s > 0 ? " s=" + (s * 1e3).ToString("0.#", CultureInfo.GetCultureInfo("pt-BR")) : "");

        public static JsonObject Spec(JsonObject baseSpec, string baseNome, string arr, string lig, double s, double a)
        {
            var spec = new JsonObject { ["forma"] = "dupla", ["base"] = baseSpec, ["arranjo"] = arr, ["ligacao"] = lig, ["s"] = s, ["nome"] = Nome(baseNome, arr, lig, s, a) };
            if (a > 0) spec["l_ligacoes"] = a;
            if (lig == "presilhas") spec["chapas_espacadoras"] = true;
            return spec;
        }

        static double V(JsonNode n, string k) { try { return n?[k] is JsonValue v && v.TryGetValue(out double d) ? d : 0; } catch { return 0; } }

        // (base, arranjo, ligação, s, a) de uma seção do modelo; não composta = ela mesma com arranjo "" (simples)
        public static (string Base, string Arr, string Lig, double S, double A) Ler(string nome, JsonNode secao)
        {
            var co = secao?["props"]?["composicao"] as JsonObject;
            if (co?["componente"] == null || (string)co["arranjo"] == "I_chapa") return (nome, "", "soldada", 0, 0);
            return ((string)co["nome_componente"] ?? nome, (string)co["arranjo"], (string)co["ligacao"] ?? "soldada", V(co, "s"), V(co, "l_ligacoes"));
        }

        // Spec do perfil base: o nome do catálogo; reserva = as medidas (seção do modelo que não é do catálogo)
        public static List<JsonObject> SpecsBase(string baseNome, JsonNode secao)
        {
            var r = new List<JsonObject> { new JsonObject { ["perfil"] = baseNome } };
            var p = secao?["props"] as JsonObject;
            JsonObject m = null;
            if (p?["composicao"]?["componente"] is JsonObject c) m = (JsonObject)c.DeepClone();
            else if (p != null)
            {
                JsonObject Med(string forma, params string[] ks) { var o = new JsonObject { ["forma"] = forma }; foreach (var k in ks) o[k] = V(p, k); return o; }
                switch ((string)p["tipo"])
                {
                    case "U": m = p["tw"] != null ? Med("U", "h", "b", "tw", "tf") : Med("U_pf", "h", "b", "t", "ri"); break;
                    case "Ue": m = Med("Ue", "h", "b", "D", "t", "ri"); break;
                    case "L": m = Med("cantoneira", "b", "h", "t"); break;
                    case "I": m = Med("I", "h", "b", "tw", "tf"); break;
                }
            }
            if (m != null) { m["nome"] = baseNome; r.Add(m); }
            return r;
        }

        // Seção no formato do modelo, calculada pelo motor sem mexer nele (POST /api/secao/previa); tenta as specs em ordem.
        public static async Task<JsonObject> Calcular(IList<JsonObject> specs)
        {
            Exception ult = null;
            foreach (var s in specs)
            {
                try { return (JsonObject)(await Motor.Post("/api/secao/previa", s))["secao"].DeepClone(); }
                catch (Exception ex) { ult = ex; }
            }
            throw ult ?? new InvalidOperationException("seção sem spec");
        }

        // Barra com esta seção é verificada pela NBR 14762 (o mesmo que projeto._tipo_padrao do motor)
        public static bool Pff(JsonNode props)
        {
            string t = (string)props?["tipo"];
            string fc = (string)props?["composicao"]?["componente"]?["forma"];
            return t == "Ue" || (t == "U" && props["t"] != null && props["tw"] == null) || (t == "dupla" && (fc == "U_pf" || fc == "Ue"));
        }
    }

    public class JanelaDisposicao : JanelaEscura
    {
        public event Action<double, int, bool> Aplicar;   // giro (°), cardinal (1..10) e espelho
        public double Giro; public int Cardinal; public bool Espelho;
        // W9 perfil duplo: seção nova das barras (null = mantém) e as specs para o motor calcular se ela ainda não está no modelo
        public string NovaSecao; public List<JsonObject> SpecsNova;
        public string Arranjo = "", Ligacao = "soldada"; public double S, A;   // m
        readonly Action<string, string, double?, double?> definirDuplo;
        readonly Func<string> nomeAlvo;
        public string NomeAlvo => nomeAlvo();

        // capturas/testes: o mesmo que clicar no bloco Perfil duplo (arr "" = simples; s e a em mm)
        public void DefinirDuplo(string arr, string lig = null, double? sMm = null, double? aMm = null) => definirDuplo?.Invoke(arr, lig, sMm, aMm);
        public void Confirmar() { Calcular(); Aplicar?.Invoke(Giro, Cardinal, Espelho); Close(); }
        Action Calcular = () => { };

        public JanelaDisposicao(Perfil3D p, double giro, int cardinal, int nBarras, bool espelho = false, string secaoNome = null, JsonNode secao = null)
        {
            Giro = giro; Cardinal = cardinal; Espelho = espelho;
            Moldar(this, "Disposição do perfil", 720, 640);
            var cab = Cabecalho(Nivel.Info, "⊕", "Disposição do perfil · " + nBarras + (nBarras == 1 ? " barra" : " barras"),
                "Giro, espelho, ponto cardinal (cruz vermelha = eixo da barra) e perfil duplo. A análise usa o eixo; o desenho desloca o perfil.");
            var host = new Border { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 0, 0, 0) };
            var (baseNome, arr0, lig0, s0, a0) = Composto.Ler(secaoNome, secao);
            var pb = arr0 != "" && p?.Comp != null ? p.Comp : p;
            string forma = Composto.Forma(pb);
            Arranjo = arr0; Ligacao = lig0; S = s0; A = a0;
            nomeAlvo = () => secaoNome == null ? null : Arranjo == "" ? baseNome : Composto.Nome(baseNome, Arranjo, Ligacao, S, A);
            var msg = Tx("", 11.5, TemaPlant.BTexto3, null, true);
            var nomeSec = Tx("", 12, TemaPlant.BTexto1, FontWeights.SemiBold, true);
            var desenho = new Border();
            var cv = new StackPanel(); cv.Children.Add(desenho);
            nomeSec.Margin = new Thickness(0, 8, 0, 0); nomeSec.Width = 260;
            if (secaoNome != null) cv.Children.Add(nomeSec);
            host.Child = cv;
            void Prever()
            {
                var pd = Arranjo == "" ? pb : new Perfil3D { Forma = "DUPLA", Arranjo = Arranjo, S = S, Comp = pb };
                desenho.Child = DesenhoSecao.Desenhar(pd, Giro, Cardinal, 260, Espelho);
                nomeSec.Text = NomeAlvo ?? "";
            }
            var tbG = Campo(Num.Fmt(giro, 1), 70, "Giro livre (°)"); tbG.HorizontalAlignment = HorizontalAlignment.Left;
            tbG.TextChanged += (s, e) => { if (Num.Ler(tbG.Text, out double g)) { Giro = g; Prever(); } };
            var giros = new WrapPanel { Margin = new Thickness(-6, 6, 0, 0) };
            foreach (int g in new[] { 0, 90, 180, 270 }) giros.Children.Add(B(g + "°", () => tbG.Text = g.ToString()));
            var ckE = new CheckBox { Content = "Espelhar", IsChecked = espelho, Foreground = TemaPlant.BTexto1, Margin = new Thickness(6, 6, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                                     ToolTip = "Reflete a seção em torno do eixo vertical local (L, U, Ue, T assimétricos); não é giro de 180°." };
            ckE.Click += (s, e) => { Espelho = ckE.IsChecked == true; Prever(); };
            giros.Children.Add(ckE);
            var grade = new UniformGrid { Rows = 3, Columns = 3, Width = 3 * 86, Margin = new Thickness(-6, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            foreach (int c in new[] { 7, 8, 9, 4, 5, 6, 1, 2, 3 }) { int cc = c; grade.Children.Add(B(c.ToString(), () => { Cardinal = cc; Prever(); })); }
            var esq = new StackPanel { Width = 380 };
            esq.Children.Add(UiAcoes.Linha("Giro (°)", tbG, 70));
            esq.Children.Add(giros);
            var tCard = Tx("Ponto cardinal", 12, TemaPlant.BTexto2); tCard.Margin = new Thickness(0, 10, 0, 0);
            esq.Children.Add(tCard);
            esq.Children.Add(grade);
            var bCent = B("Centroide (10)", () => { Cardinal = 10; Prever(); }); bCent.HorizontalAlignment = HorizontalAlignment.Left; bCent.Margin = new Thickness(0, 6, 0, 0);
            esq.Children.Add(bCent);

            // ---- Perfil duplo (W9) ----
            var tDup = Tx("Perfil duplo", 12, TemaPlant.BTexto2, FontWeights.SemiBold); tDup.Margin = new Thickness(0, 16, 0, 4);
            esq.Children.Add(tDup);
            if (forma == null || secaoNome == null)
                esq.Children.Add(Tx(secaoNome == null ? "Escolha barras de uma seção do modelo." : "Só para U (UPN, U laminado, U e Ue dobrados), cantoneira L e I.", 11.5, TemaPlant.BTexto3, null, true));
            else
            {
                var ops = Composto.Opcoes[forma];
                var seg = new Segmentado(false, ops.Select(o => new[] { o.Rot, null, o.Dica }).ToArray());
                var ligs = new[] { ("soldada", "solda contínua"), ("presilhas", "chapas espaçadoras") };
                var cbLig = new ComboBox { Width = 200, HorizontalAlignment = HorizontalAlignment.Left, ToolTip = "Como os dois perfis se ligam ao longo da barra." };
                foreach (var l in ligs) cbLig.Items.Add(l.Item2);
                TextBox tbS = Campo(Num.Fmt(S * 1e3, 1), 80, "Afastamento entre os perfis (mm): 0 = encostados."),
                        tbA = Campo(A > 0 ? Num.Fmt(A * 1e3, 0) : "", 80, "Distância entre chapas espaçadoras (mm): l/r de cada perfil ≤ ½ do KL/r do conjunto.");
                tbS.HorizontalAlignment = tbA.HorizontalAlignment = HorizontalAlignment.Left;
                var lnLig = UiAcoes.Linha("Ligação", cbLig, 110); var lnS = UiAcoes.Linha("Afastamento s (mm)", tbS, 110); var lnA = UiAcoes.Linha("Chapas a cada (mm)", tbA, 110);
                bool montando = false;
                void Atualizar()
                {
                    montando = true;
                    int i = Array.FindIndex(ops, o => o.Arr == Arranjo); seg.Definir(i < 0 ? 0 : i);
                    cbLig.SelectedIndex = Ligacao == "presilhas" ? 1 : 0;
                    montando = false;
                    bool dup = Arranjo != "";
                    lnLig.Visibility = lnS.Visibility = dup ? Visibility.Visible : Visibility.Collapsed;
                    lnA.Visibility = dup && Ligacao == "presilhas" ? Visibility.Visible : Visibility.Collapsed;
                    string t;
                    if (!dup) t = arr0 != "" ? "Volta ao perfil simples " + baseNome + "." : "Um perfil só. Escolha Caixão ou Costas com costas para unir dois.";
                    else if (Ligacao == "soldada" && S == 0 && Arranjo == "caixao") t = "Boca a boca soldados em toda a extensão: seção única (tubo fechado).";
                    else if (Ligacao == "soldada" && S == 0) t = "Encostados e soldados em toda a extensão: trabalham como um perfil só.";
                    else if (Ligacao == "soldada") t = "Afastados sem chapas espaçadoras: a norma não cobre (a barra reprova na compressão). Use chapas espaçadoras.";
                    else t = "Chapas espaçadoras: l/r de cada perfil entre chapas ≤ ½ do KL/r do conjunto (NBR 8800 5.3.4 / NBR 14762 9.7.5)."
                             + (A > 0 ? "" : " Informe a distância: sem ela a compressão não é verificada.")
                             + " My (eixo de simetria): 2 × o perfil isolado, sem ação conjunta das chapas (a favor da segurança).";
                    msg.Text = t;
                    Prever();
                }
                seg.Escolheu += i => { if (montando) return; Arranjo = ops[i].Arr; Atualizar(); };
                cbLig.SelectionChanged += (s, e) =>
                {
                    if (montando || cbLig.SelectedIndex < 0) return;
                    Ligacao = ligs[cbLig.SelectedIndex].Item1;
                    if (Ligacao == "presilhas" && A <= 0) { A = 0.5; tbA.Text = "500"; }   // ponto de partida visível; o projetista ajusta
                    Atualizar();
                };
                double Mm(TextBox t) => Num.Ler(t.Text, out double v) && v >= 0 ? v / 1000 : 0;
                tbS.TextChanged += (s, e) => { if (montando) return; S = Mm(tbS); Atualizar(); };
                tbA.TextChanged += (s, e) => { if (montando) return; A = Mm(tbA); Atualizar(); };
                definirDuplo = (arr, lig, sm, am) =>
                {
                    Arranjo = arr ?? ""; if (lig != null) { Ligacao = lig; cbLig.SelectedIndex = lig == "presilhas" ? 1 : 0; }
                    if (sm != null) tbS.Text = Num.Fmt(sm.Value, 1);
                    if (am != null) tbA.Text = Num.Fmt(am.Value, 0);
                    Atualizar();
                };
                esq.Children.Add(seg);
                esq.Children.Add(lnLig); esq.Children.Add(lnS); esq.Children.Add(lnA);
                msg.Margin = new Thickness(0, 8, 0, 0);
                esq.Children.Add(msg);
                Atualizar();
                Calcular = () =>
                {
                    string alvo = NomeAlvo;
                    if (alvo == null || alvo == secaoNome) return;
                    NovaSecao = alvo;
                    var bases = Composto.SpecsBase(baseNome, secao);
                    SpecsNova = Arranjo == "" ? bases : bases.Select(b => Composto.Spec(b, baseNome, Arranjo, Ligacao, S, A)).ToList();
                };
            }
            var corpo = new DockPanel { Margin = new Thickness(12) };
            DockPanel.SetDock(esq, Dock.Left); corpo.Children.Add(esq); corpo.Children.Add(host);
            Content = Raiz(cab, corpo, Rodape(Tx("Um Ctrl+Z desfaz tudo (seção, giro, cardinal).", 11.5, TemaPlant.BTexto3), B("Cancelar", Close),
                B("Aplicar", Confirmar, true)));
            Prever();
        }
    }
}
