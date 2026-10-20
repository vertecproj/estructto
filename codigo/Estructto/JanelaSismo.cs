// Janela "Sismo (NBR 15421)" da aba Ações: zona sísmica, classe do terreno, categoria de utilização e sistema
// sismo-resistente (R, Ω0, Cd da tabela 6). Grava em config.sismo (PATCH /api/config), gera SX/SY e a torção
// acidental (POST /api/acoes/gerar) e mostra as forças horizontais equivalentes por pavimento.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static NovoFtool.UiAcoes;

namespace NovoFtool
{
    public class JanelaSismo : JanelaEscura
    {
        int zona = 0;
        string classe = "C", categoria = "I", sistemaRes = "portico_aco_usual", sistemaT = "aco_portico";
        double lx = double.NaN, ly = double.NaN;
        JsonNode geracao;
        readonly Border hostEsq = new Border(), hostDir = new Border();
        readonly TextBlock status = Status();
        TextBox tLx, tLy;

        static readonly string[] Zonas = { "0", "1", "2", "3", "4" };
        static readonly string[] DicaZona =
        {
            "Zona 0: ag = 0,025g. Dispensa verificação sísmica.",
            "Zona 1: ag = 0,05g. Só forças de 1% do peso em cada direção (7.3.2).",
            "Zona 2: ag = 0,10g. Forças horizontais equivalentes.",
            "Zona 3: ag = 0,15g. Forças horizontais equivalentes.",
            "Zona 4: ag = 0,15g. Forças horizontais equivalentes."
        };
        static readonly string[] ClassesT = { "A", "B", "C", "D", "E" };
        static readonly string[] DicaClasse =
        {
            "A: rocha sã (vs ≥ 1500 m/s).", "B: rocha (760 ≤ vs < 1500 m/s).", "C: rocha alterada ou solo muito rígido (370 ≤ vs < 760 m/s).",
            "D: solo rígido (180 ≤ vs < 370 m/s).", "E: solo mole (vs < 180 m/s)."
        };
        static readonly string[] Categorias = { "I", "II", "III" };
        static readonly string[] DicaCategoria =
        {
            "Categoria I (I = 1,00): demais estruturas.",
            "Categoria II (I = 1,25): importância substancial (escolas, locais de grande público).",
            "Categoria III (I = 1,50): essenciais (hospitais, bombeiros, centrais de energia)."
        };
        // NBR 15421:2006 tabela 6: chave do motor -> (nome, R, Ω0, Cd)
        static readonly (string chave, string nome, double R, double O, double Cd)[] Sistemas =
        {
            ("portico_aco_especial", "Pórtico de aço momento-resistente especial", 8, 3, 5.5),
            ("portico_aco_intermediario", "Pórtico de aço momento-resistente intermediário", 4.5, 3, 4),
            ("portico_aco_usual", "Pórtico de aço momento-resistente usual", 3.5, 3, 3),
            ("contraventado_aco_especial", "Pórtico de aço contraventado especial", 6, 2, 5),
            ("contraventado_aco_usual", "Pórtico de aço contraventado usual", 3.25, 2, 3.25),
            ("portico_concreto_especial", "Pórtico de concreto especial", 8, 3, 5.5),
            ("portico_concreto_intermediario", "Pórtico de concreto intermediário", 5, 3, 4.5),
            ("portico_concreto_usual", "Pórtico de concreto usual", 3, 3, 2.5),
            ("pilar_parede_concreto_especial", "Pilares-parede de concreto especiais", 5, 2.5, 5),
            ("pilar_parede_concreto_usual", "Pilares-parede de concreto usuais", 4, 2.5, 4),
            ("dual_especial_pp_especial", "Dual: pórtico especial + pilares-parede especiais", 7, 2.5, 5.5),
            ("dual_especial_pp_usual", "Dual: pórtico especial + pilares-parede usuais", 6, 2.5, 5),
            ("dual_especial_contraventado_especial", "Dual: pórtico especial + contraventamento especial", 7, 2.5, 5.5),
            ("dual_intermediario_pp_especial", "Dual: pórtico intermediário + pilares-parede especiais", 6.5, 2.5, 5),
            ("dual_intermediario_pp_usual", "Dual: pórtico intermediário + pilares-parede usuais", 5.5, 2.5, 4.5),
            ("dual_usual_pp_usual", "Dual: pórtico usual + pilares-parede usuais", 4.5, 2.5, 4),
            ("pendulo_invertido", "Pêndulo invertido", 2.5, 2, 2.5),
        };
        // NBR 15421 9.2: período aproximado Ta = CT·hn^x
        static readonly (string chave, string nome)[] SistemasT =
        {
            ("aco_portico", "Pórtico de aço (CT 0,0724; x 0,8)"),
            ("concreto_portico", "Pórtico de concreto (CT 0,0466; x 0,9)"),
            ("aco_contraventado_trelica", "Aço contraventado (CT 0,0731; x 0,75)"),
            ("outros", "Outras estruturas (CT 0,0488; x 0,75)"),
        };

        // resumo = GET /api/resumo; geracao = POST /api/acoes/gerar. Nulos: busca no motor.
        public JanelaSismo(JsonNode resumo = null, JsonNode geracao = null)
        {
            Title = "Sismo (NBR 15421)"; Width = 1080; Height = 700; MinWidth = 900; MinHeight = 520;
            var gerar = B("Gerar ações sísmicas", Gerar, true, "Grava os dados do sismo no modelo e cria (ou recria) os casos SX, SY e a torção acidental SX_TOR/SY_TOR.");
            Content = UiAcoes.Montar(this, Cabecalho("Sismo", "NBR 15421", "Forças horizontais equivalentes: H = Cs·W, distribuídas pela altura com Fx = Cvx·H."),
                DuasColunas(hostEsq, hostDir, 450), Rodape(status, B("Fechar", Close), gerar));
            this.geracao = geracao;
            if (resumo != null) Carregar(resumo);
            else Loaded += async (s, e) => { JsonNode r = null; if (await Tentar(status, async () => r = await Motor.Get("/api/resumo"))) Carregar(r); };
            Montar();
        }

        void Carregar(JsonNode r)
        {
            var c = r?["config"]?["sismo"];
            if (c != null)
            {
                zona = (int)D(c["zona"], 0);
                classe = S(c["classe_terreno"], classe).ToUpperInvariant();
                categoria = S(c["categoria_utilizacao"], categoria).ToUpperInvariant();
                if (c["sistema_resistente"] != null) sistemaRes = S(c["sistema_resistente"]);
                if (c["sistema"] != null) sistemaT = S(c["sistema"]);
                lx = D(c["dimensoes"]?["lx"]); ly = D(c["dimensoes"]?["ly"]);
            }
            Montar();
        }

        void Montar()
        {
            var sp = new StackPanel();
            sp.Children.Add(Cartao("① Local", new StackPanel
            {
                Children =
                {
                    Linha("Zona", Escolha(Zonas, Zonas, zona.ToString(), v => { Ler(); zona = int.Parse(v); Montar(); }, DicaZona), dica: "Zona sísmica (NBR 15421 figura 1)"),
                    Nota(DicaZona[zona]),
                    Linha("Terreno", Escolha(ClassesT, ClassesT, classe, v => { Ler(); classe = v; Montar(); }, DicaClasse), dica: "Classe do terreno (tabela 2)", topo: 10),
                    Nota(DicaClasse[Array.IndexOf(ClassesT, classe)]),
                }
            }));
            sp.Children.Add(Cartao("② Utilização", new StackPanel
            {
                Children =
                {
                    Linha("Categoria", Escolha(Categorias, Categorias, categoria, v => { Ler(); categoria = v; Montar(); }, DicaCategoria), dica: "Categoria de utilização (tabela 4)"),
                    Nota(DicaCategoria[Array.IndexOf(Categorias, categoria)]),
                }
            }));
            var sis = Sistemas.FirstOrDefault(x => x.chave == sistemaRes);
            var cbS = UiPlaca.Combo(Sistemas.Select(x => x.nome), sis.nome, v => { Ler(); sistemaRes = Sistemas.First(x => x.nome == v).chave; Montar(); }, 284, "Sistema básico sismo-resistente (NBR 15421 tabela 6): define R, Ω0 e Cd.");
            var cbT = UiPlaca.Combo(SistemasT.Select(x => x.nome), SistemasT.FirstOrDefault(x => x.chave == sistemaT).nome, v => { Ler(); sistemaT = SistemasT.First(x => x.nome == v).chave; Montar(); }, 284, "Tipo de estrutura para o período aproximado Ta = CT·hn^x (NBR 15421 9.2).");
            cbS.Margin = cbT.Margin = new Thickness(0); cbS.HorizontalAlignment = cbT.HorizontalAlignment = HorizontalAlignment.Left;
            var coefs = Horiz(Chip("R", sis.R), Chip("Ω0", sis.O), Chip("Cd", sis.Cd));
            sp.Children.Add(Cartao("③ Sistema resistente", new StackPanel
            {
                Children =
                {
                    Linha("Sistema", cbS),
                    Linha("", coefs),
                    Linha("Período", cbT, topo: 10),
                }
            }));
            sp.Children.Add(Cartao("④ Planta (torção acidental)", new StackPanel
            {
                Children =
                {
                    Linha("Planta", Horiz(Num(out tLx, lx, "m", 96, "Dimensão da planta em X para a excentricidade acidental de 5%. Vazio = medida do vento ou da caixa do modelo."), Sep("×"), Num(out tLy, ly, "m", 96, "Dimensão da planta em Y para a excentricidade acidental de 5%."))),
                }
            }));
            hostEsq.Child = sp;
            MontarPrevia();
        }

        // NBR 15421 tabela 1 (limite superior da faixa; zona 4 = 0,15g), igual ao motor (acoes/sismo.py AG_ZONA)
        static readonly double[] AgZona = { 0.025, 0.05, 0.10, 0.15, 0.15 };

        static Border Chip(string rot, double v) { var c = UiPlaca.Chip(rot + " = " + F(v, 2).TrimEnd('0').TrimEnd(','), UiPlaca.Nivel.Neutro, null, null, 11); c.Margin = new Thickness(0, 0, 6, 0); return c; }
        static TextBlock Sep(string s) { var t = UiPlaca.Tx(s, 12, TemaPlant.BTexto2); t.Margin = new Thickness(8, 0, 8, 0); return t; }

        bool Ler()
        {
            bool a = UiAcoes.Ler(tLx, out double x, true, 0), b = UiAcoes.Ler(tLy, out double y, true, 0);
            if (a) lx = x; if (b) ly = y;
            if (!(a && b)) { status.Text = "✕ Dimensões da planta: números positivos em metros (ou vazio)."; status.Foreground = TemaPlant.BErro; }
            return a && b;
        }

        async void Gerar()
        {
            if (!Ler()) return;
            var c = new JsonObject { ["zona"] = zona };
            if (zona > 0)
            {
                c["classe_terreno"] = classe; c["categoria_utilizacao"] = categoria; c["sistema_resistente"] = sistemaRes; c["sistema"] = sistemaT;
                if (!double.IsNaN(lx) && !double.IsNaN(ly)) c["dimensoes"] = new JsonObject { ["lx"] = lx, ["ly"] = ly };
            }
            JsonNode g = null;
            if (!await Tentar(status, async () => { await Motor.Chamar("PATCH", "/api/config", new JsonObject { ["sismo"] = c }); g = await Motor.Post("/api/acoes/gerar"); }, "Gerando as ações sísmicas…")) return;
            geracao = g;
            var casos = Lista(g?["casos"]).Select(x => S(x)).Where(x => x.StartsWith("S")).ToList();
            status.Text = casos.Count == 0 ? "Zona 0: nenhum caso sísmico gerado." : "✓ " + casos.Count + " casos sísmicos gerados (" + string.Join(", ", casos) + ").";
            status.Foreground = casos.Count == 0 ? TemaPlant.BTexto3 : TemaPlant.BSucesso;
            MontarPrevia();
        }

        void MontarPrevia()
        {
            var sp = new StackPanel();
            var px = geracao?["parametros"]?["SX"];
            if (zona > 0)
            {
                double ag = px != null && !double.IsNaN(D(px["ag"])) ? D(px["ag"]) : AgZona[zona];
                var g = new GraficoEspectro(ag, classe, px != null ? D(px["T"]) : double.NaN) { Margin = new Thickness(0, 0, 0, 10) };
                sp.Children.Add(UiPlaca.TituloSecao("Espectro de projeto Sa(T) · NBR 15421 6.3", null, 0));
                sp.Children.Add(g);
                Anim.Valor(g, GraficoEspectro.ProgressoProperty, 0, 1, Anim.Espacial);
            }
            if (px != null) sp.Children.Add(Resumo(
                ("ag", F(D(px?["ag"]), 3), "g", "Aceleração sísmica horizontal de projeto (fração de g)."),
                ("Cs", F(D(px?["Cs"]), 4), null, "Coeficiente de resposta sísmica (NBR 15421 9.1)."),
                ("T", F(D(px?["T"]), 3), "s", "Período usado no cálculo (Ta limitado por Cup·Ta)."),
                ("W", F(D(px?["W_kN"]), 0), "kN", "Peso sísmico (G + parcela de Q)."),
                ("H", F(D(px?["H_kN"]), 1), "kN", "Força horizontal total na base em cada direção.")));
            sp.Children.Add(UiPlaca.TituloSecao("Forças horizontais por pavimento", null, px != null ? 2 : 0));
            var fx = Lista(geracao?["forcas"]?["SX"]).ToList();
            var fy = Lista(geracao?["forcas"]?["SY"]).ToList();
            if (zona == 0 && geracao == null || fx.Count == 0)
            {
                var vz = zona == 0
                    ? UiTabela.Vazio("info", "Zona 0: sem verificação sísmica", "A NBR 15421 dispensa a análise sísmica na zona 0. Escolha a zona do local da obra à esquerda.")
                    : UiTabela.Vazio("calc", "Forças ainda não geradas", "Clique em \"Gerar ações sísmicas\" para calcular Cs, o peso sísmico e as forças de cada pavimento.");
                vz.Margin = new Thickness(0, 40, 0, 0); sp.Children.Add(vz);
            }
            else
            {
                var tx = Lista(geracao?["forcas"]?["SX_TOR"]).ToList();
                var ty = Lista(geracao?["forcas"]?["SY_TOR"]).ToList();
                var linhas = fx.AsEnumerable().Reverse().Select(f =>
                {
                    string p = S(f?["pavimento"]);
                    var y = fy.FirstOrDefault(a => S(a?["pavimento"]) == p);
                    var mx = tx.FirstOrDefault(a => S(a?["pavimento"]) == p);
                    var my = ty.FirstOrDefault(a => S(a?["pavimento"]) == p);
                    return new[] { p, F(D(f?["z"]), 2), F(D(f?["F_kN"]), 2), F(D(y?["F_kN"]), 2), mx == null ? "—" : F(D(mx["Mz_kNm"]), 2), my == null ? "—" : F(D(my["Mz_kNm"]), 2) };
                });
                sp.Children.Add(TabelaFixa(new[] { "Pavimento", "z (m)", "Fx SX (kN)", "Fy SY (kN)", "Mz SX_TOR (kN·m)", "Mz SY_TOR (kN·m)" }, new[] { false, true, true, true, true, true }, linhas, new[] { 0, 64, 92, 92, 124, 124.0 }));
                if (px != null)
                    sp.Children.Add(Nota("R = " + F(D(px["R"]), 2) + " · I = " + F(D(px["I"]), 2) + " · Cd = " + F(D(px["Cd"]), 2) + " · Ω0 = " + F(D(px["Omega0"]), 2) + " · Ta = " + F(D(px["Ta"]), 3) + " s · k = " + F(D(px["k"]), 2)));
            }
            if (geracao != null) sp.Children.Add(Avisos(Lista(geracao["avisos"]).Where(a => !S(a?["codigo"]).StartsWith("vento"))));
            hostDir.Child = sp;
        }
    }

    // Espectro de resposta de projeto, NBR 15421:2006 6.3 (ags0 = Ca·ag, ags1 = Cv·ag; Ca, Cv da tabela 3 com
    // interpolação linear em ag entre 0,10g e 0,15g, como o motor). Marca o período T da estrutura (quando gerado).
    // Progresso 0→1 desenha a curva da esquerda para a direita (Anim.Valor; nas capturas sai em 1).
    public class GraficoEspectro : FrameworkElement
    {
        public static readonly DependencyProperty ProgressoProperty = DependencyProperty.Register("Progresso", typeof(double), typeof(GraficoEspectro),
            new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));
        public double Progresso { get => (double)GetValue(ProgressoProperty); set => SetValue(ProgressoProperty, value); }

        static readonly Dictionary<string, (double ca1, double ca2, double cv1, double cv2)> Tab3 = new()
        {
            ["A"] = (0.8, 0.8, 0.8, 0.8), ["B"] = (1.0, 1.0, 1.0, 1.0), ["C"] = (1.2, 1.2, 1.7, 1.7),
            ["D"] = (1.6, 1.5, 2.4, 2.2), ["E"] = (2.5, 2.1, 3.5, 3.4),
        };
        readonly double ag, Ca, Cv, T;
        const double TMax = 3.0;

        public GraficoEspectro(double ag, string classe, double T)
        {
            this.ag = ag; this.T = T; Height = 200;
            var c = Tab3.TryGetValue(classe, out var v) ? v : Tab3["C"];
            double t = Math.Min(Math.Max((ag - 0.10) / 0.05, 0), 1);
            Ca = c.ca1 + (c.ca2 - c.ca1) * t; Cv = c.cv1 + (c.cv2 - c.cv1) * t;
            ToolTip = "Sa(T) = ags0·(18,75·T·Ca/Cv + 1) até T = 0,08·Cv/Ca; 2,5·ags0 até 0,4·Cv/Ca; depois ags1/T.";
        }

        public double Sa(double T)
        {
            double ags0 = Ca * ag, ags1 = Cv * ag;
            if (T <= 0.08 * Cv / Ca) return ags0 * (18.75 * T * Ca / Cv + 1.0);
            if (T <= 0.4 * Cv / Ca) return 2.5 * ags0;
            return ags1 / T;
        }

        static FormattedText Txt(string s, Brush b, double tam = 10.5) =>
            new FormattedText(s, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(TemaPlant.Fonte, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), tam, b, 1.0);
        static string N(double v, int c) => v.ToString("F" + c, System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));

        protected override void OnRender(DrawingContext dc)
        {
            double W = ActualWidth, H = ActualHeight, x0 = 44, y0 = 8, x1 = W - 12, y1 = H - 24;
            if (W < 80) return;
            dc.DrawRectangle(TemaPlant.BBgCanvas, new Pen(TemaPlant.BBordaSutil, 1), new Rect(0.5, 0.5, W - 1, H - 1));
            double sMax = 2.5 * Ca * ag * 1.2;
            Point P(double t, double s) => new Point(x0 + (x1 - x0) * t / TMax, y1 - (y1 - y0) * s / sMax);
            var grade = new Pen(TemaPlant.BGradeMaior, 1); grade.Freeze();
            var eixo = new Pen(TemaPlant.BBorda, 1); eixo.Freeze();
            for (int i = 0; i <= 6; i++)
            {
                var a = P(i * 0.5, 0); dc.DrawLine(grade, new Point(a.X, y0), new Point(a.X, y1));
                var ft = Txt(N(i * 0.5, 1), TemaPlant.BTexto3); dc.DrawText(ft, new Point(a.X - ft.Width / 2, y1 + 4));
            }
            for (int i = 0; i <= 4; i++)
            {
                double s = sMax * i / 4; var a = P(0, s); dc.DrawLine(grade, new Point(x0, a.Y), new Point(x1, a.Y));
                var ft = Txt(N(s, 3), TemaPlant.BTexto3); dc.DrawText(ft, new Point(x0 - ft.Width - 4, a.Y - ft.Height / 2));
            }
            dc.DrawLine(eixo, new Point(x0, y0), new Point(x0, y1)); dc.DrawLine(eixo, new Point(x0, y1), new Point(x1, y1));
            var un = Txt("T (s)", TemaPlant.BTexto2); dc.DrawText(un, new Point(x1 - un.Width, y1 - un.Height - 2));
            dc.DrawText(Txt("Sa (g)", TemaPlant.BTexto2), new Point(x0 + 4, y0));

            // curva até Progresso·TMax
            double tFim = TMax * Math.Max(0, Math.Min(1, Progresso));
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(P(0, Sa(0)), false, false);
                for (int i = 1; i <= 300; i++) { double t = TMax * i / 300; if (t > tFim) { g.LineTo(P(tFim, Sa(tFim)), true, false); break; } g.LineTo(P(t, Sa(t)), true, true); }
            }
            geo.Freeze();
            dc.DrawGeometry(null, new Pen(TemaPlant.BAcento, 2) { LineJoin = PenLineJoin.Round }, geo);

            // legenda dos parâmetros
            var leg = Txt("ag = " + N(ag, 3) + "g · Ca = " + N(Ca, 2) + " · Cv = " + N(Cv, 2) + " · patamar 2,5·ags0 = " + N(2.5 * Ca * ag, 3) + "g", TemaPlant.BTexto2);
            dc.DrawText(leg, new Point(x1 - leg.Width - 2, y0));

            // período da estrutura
            if (!double.IsNaN(T) && T > 0 && T <= TMax && T <= tFim)
            {
                var p = P(T, Sa(T));
                var tr = new Pen(TemaPlant.BAlerta, 1) { DashStyle = DashStyles.Dash }; tr.Freeze();
                dc.DrawLine(tr, new Point(p.X, y1), p); dc.DrawLine(tr, new Point(x0, p.Y), p);
                dc.DrawEllipse(TemaPlant.BAlerta, null, p, 4, 4);
                var ft = Txt("T = " + N(T, 3) + " s · Sa = " + N(Sa(T), 3) + "g", TemaPlant.BTexto1, 11);
                double lx = p.X + 8 + ft.Width > x1 ? p.X - 8 - ft.Width : p.X + 8;
                var r = new Rect(lx - 3, p.Y - ft.Height - 6, ft.Width + 6, ft.Height + 2);
                dc.DrawRectangle(TemaPlant.BFundoTextoCanvas, null, r);
                dc.DrawText(ft, new Point(lx, r.Y + 1));
            }
        }
    }
}
