// Janela "Vento" da aba Ações, com seletor de norma:
//   NBR 6123 · edificação: V0, S1, S2 (categoria e classe), S3, dimensões e coeficientes; forças por pavimento (casos V±X/V±Y).
//   NBR 6123 · por barra (seção 8.1): w = Cf·q(z)·K·c·sen²α normal a cada barra (acoes/vento_barras.py; port do vento 2D do Bruno).
//   NBR 8400 · equipamentos de levantamento: vento de serviço (vw) e vento máximo (tabela 7), C da tabela 8 (acoes/nbr8400.py);
//     grupo (Mx) e ψ das combinações dos casos I/II/III, que podem virar o conjunto de combinações do modelo.
// Grava em config.vento (PATCH /api/config) e gera os casos (POST /api/acoes/gerar). A prévia mostra o que o motor devolveu.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using static NovoFtool.UiAcoes;

namespace NovoFtool
{
    public class JanelaVento : JanelaEscura
    {
        double V0 = 30, S1 = 1, S3 = 1, lx = double.NaN, ly = double.NaN, Ca = double.NaN;
        string cat = "II", classe = "B", perm = "quatro_faces", cpiTxt = "";
        bool coefAuto = true;
        readonly HashSet<string> dirs = new HashSet<string> { "+X", "-X", "+Y", "-Y" };
        readonly List<(string nome, double z)> pavs = new List<(string, double)>();
        double baseZ;
        JsonNode geracao;              // resultado do último "Gerar" (forças e parâmetros por caso)
        string casoSel = "V+X";
        // norma: "edificacao" | "barras" | "nbr8400" (config.vento.tipo: vazio/edificio/galpao | barras | nbr8400)
        string norma = "edificacao", classeBarras = "";   // classe "" = automática (NBR 6123 5.3.2)
        bool usarK = true, comb8400 = true;
        double vServ = 20, psi = 1.15;
        int grupo = 3;
        string combAtual = "nbr8681";

        readonly Border hostEsq = new Border(), hostDir = new Border(), hostCab = new Border();
        readonly TextBlock status = Status();
        TextBox tV0, tS1, tS3, tLx, tLy, tCa, tCpi, tVs, tPsi;

        static readonly string[] Cats = { "I", "II", "III", "IV", "V" };
        static readonly string[] DicaCat =
        {
            "Categoria I: superfícies lisas de grandes dimensões (mar calmo, lagos, rios).",
            "Categoria II: terrenos abertos, em nível, com poucos obstáculos isolados (campos, fazendas sem sebes).",
            "Categoria III: terrenos planos ou ondulados com obstáculos (sebes, muros, casas baixas esparsas).",
            "Categoria IV: obstáculos numerosos e pouco espaçados (subúrbios, zonas industriais, parques).",
            "Categoria V: obstáculos numerosos, grandes, altos e pouco espaçados (centros de grandes cidades)."
        };
        static readonly string[] Classes = { "A", "B", "C" };
        static readonly string[] DicaClasse =
        {
            "Classe A: maior dimensão da superfície frontal até 20 m.",
            "Classe B: maior dimensão entre 20 m e 50 m.",
            "Classe C: maior dimensão acima de 50 m."
        };
        // NBR 8400 tabela 10: Mx por grupo (as mesmas descrições do vento 2D do Bruno)
        static readonly double[] Mx = { 1, 1, 1, 1.06, 1.12, 1.20 };

        // resumo = GET /api/resumo (config + pavimentos + caixa); geracao = POST /api/acoes/gerar. Nulos: busca no motor.
        public JanelaVento(JsonNode resumo = null, JsonNode geracao = null)
        {
            Title = "Vento"; Width = 1120; Height = 720; MinWidth = 900; MinHeight = 520;
            var corpo = DuasColunas(hostEsq, hostDir, 450);
            var gerar = B("Gerar ações de vento", Gerar, true, "Grava os dados do vento no modelo e cria (ou recria) os casos de vento da norma escolhida.");
            var fechar = B("Fechar", Close);
            Content = UiAcoes.Montar(this, hostCab, corpo, Rodape(status, fechar, gerar));
            this.geracao = geracao;
            if (resumo != null) Carregar(resumo);
            else Loaded += async (s, e) => { JsonNode r = null; if (await Tentar(status, async () => r = await Motor.Get("/api/resumo"))) Carregar(r); else Montar(); };
            Montar();
        }

        void Carregar(JsonNode r)
        {
            var cfg = r?["config"];
            var v = cfg?["vento"];
            if (v != null)
            {
                string tipo = S(v["tipo"]);
                norma = tipo == "barras" ? "barras" : tipo == "nbr8400" ? "nbr8400" : "edificacao";
                V0 = D(v["V0"], V0); S1 = D(v["S1"], 1); S3 = D(v["S3"], 1);
                cat = S(v["categoria"], cat).ToUpperInvariant();
                classe = S(v["classe"], classe).ToUpperInvariant();
                classeBarras = S(v["classe"]).ToUpperInvariant();
                usarK = v["usar_K"]?.GetValue<bool>() ?? true;
                vServ = D(v["v_servico"], 20);
                lx = D(v["dimensoes"]?["lx"]); ly = D(v["dimensoes"]?["ly"]);
                Ca = D(v["Ca"]);
                if (v["permeabilidade"] != null) perm = S(v["permeabilidade"]);
                if (v["cpi"] is JsonArray cp) cpiTxt = string.Join("; ", cp.Select(x => F(D(x), 2)));
                coefAuto = double.IsNaN(Ca) && v["cpi"] == null;
                if (v["direcoes"] is JsonArray ds) { dirs.Clear(); foreach (var d in ds) dirs.Add(S(d)); }
            }
            combAtual = S(cfg?["combinacoes"], "nbr8681");
            if (norma == "nbr8400") comb8400 = combAtual == "nbr8400";
            grupo = (int)D(cfg?["nbr8400"]?["grupo"], 3); psi = D(cfg?["nbr8400"]?["psi"], 1.15);
            if (norma == "nbr8400") casoSel = "VS+X";
            pavs.Clear();
            foreach (var p in Lista(r?["pavimentos"])) pavs.Add((S(p?["nome"]), D(p?["z"], 0)));
            pavs.Sort((a, b) => a.z.CompareTo(b.z));
            baseZ = D(r?["caixa"]?["min"]?[2], 0);
            Montar();
        }

        // ---------- entradas ----------
        void Montar()
        {
            hostCab.Child = norma == "barras"
                ? Cabecalho("Vento", "NBR 6123 · 8.1", "Força em cada barra: w = Cf·q(z)·K·c·sen²α, normal ao eixo (Cf da tabela 26/27 pela seção, K da tabela 28).")
                : norma == "nbr8400"
                ? Cabecalho("Vento", "NBR 8400", "Equipamentos de levantamento: Pa = vw²/1,6 em serviço e vento máximo da tabela 7; w = C·Pa·c·sen²α em cada barra.")
                : Cabecalho("Vento", "NBR 6123", "Força de arrasto por pavimento a partir da pressão dinâmica q(z) = 0,613·(V0·S1·S2·S3)².");
            Title = norma == "nbr8400" ? "Vento (NBR 8400)" : "Vento (NBR 6123)";
            var sp = new StackPanel();
            sp.Children.Add(Cartao("Norma", Escolha(new[] { "edificacao", "barras", "nbr8400" }, new[] { "6123 · edificação", "6123 · por barra", "8400 · equipamentos" }, norma,
                v => { Ler(); norma = v; geracao = null; casoSel = v == "nbr8400" ? "VS+X" : "V+X"; Montar(); },
                new[] { "NBR 6123:2023: arrasto da edificação por pavimento (Ca da figura 4) ou galpão (Cpe/Cpi por pórtico).",
                        "NBR 6123:2023 seção 8.1: barras prismáticas expostas (estrutura aberta, torre, guincho): carga distribuída em cada barra.",
                        "NBR 8400:1984: pontes rolantes, pórticos e guindastes. Vento de serviço e vento máximo, combinações dos casos I, II e III." })));
            if (norma == "nbr8400") Montar8400(sp);
            else if (norma == "barras") MontarBarras(sp);
            else MontarEdificacao(sp);
            hostEsq.Child = sp;
            MontarPrevia();
        }

        UIElement CaixaDirecoes()
        {
            var chks = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var d in new[] { "+X", "-X", "+Y", "-Y" })
            {
                string dd = d;
                var cb = new CheckBox { Content = d.Replace("-", "−"), IsChecked = dirs.Contains(d), Foreground = TemaPlant.BTexto1, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Gerar o vento no sentido " + d };
                cb.Checked += (s, e) => dirs.Add(dd);
                cb.Unchecked += (s, e) => dirs.Remove(dd);
                chks.Children.Add(cb);
            }
            return chks;
        }

        Border CartaoVelocidade() => Cartao("① Velocidade básica", new StackPanel
        {
            Children =
            {
                Linha("V0", Horiz(Num(out tV0, V0, "m/s", 96, "Velocidade básica do vento (isopletas, NBR 6123 fig. 1), rajada de 3 s a 10 m de altura."), Nota0("isopletas: 30 a 50 m/s")), dica: "Velocidade básica (m/s)"),
                Linha("S1 · S3", Horiz(Num(out tS1, S1, null, 80, "Fator topográfico S1: 1,00 terreno plano; 0,90 vale protegido; talude/morro > 1 (NBR 6123 5.2)."), Sep("S3"), Num(out tS3, S3, null, 80, "Fator estatístico S3 (NBR 6123 tabela 4): grupo 1 = 1,11; grupo 2 = 1,06; grupo 3 = 1,00 (residências, comércio, indústria); grupo 4 = 0,95; grupo 5 = 0,83.")), dica: "Fator topográfico S1 e fator estatístico S3"),
                Nota("S1: plano 1,00 · vale 0,90. S3: grupo 3 (comum) 1,00; essenciais 1,11; sem ocupação 0,95; temporárias 0,83."),
            }
        });

        void MontarEdificacao(StackPanel sp)
        {
            sp.Children.Add(CartaoVelocidade());
            sp.Children.Add(Cartao("② Rugosidade do terreno (S2)", new StackPanel
            {
                Children =
                {
                    Linha("Categoria", Horiz(Escolha(Cats, Cats, cat, v => { Ler(); cat = v; Montar(); }, DicaCat), Sep("Classe"), Escolha(Classes, Classes, classe, v => { Ler(); classe = v; Montar(); }, DicaClasse)), dica: "Categoria de rugosidade (I a V) e classe da edificação (A, B, C)"),
                    Nota(cat + ": " + DicaCat[Array.IndexOf(Cats, cat)].Substring(DicaCat[Array.IndexOf(Cats, cat)].IndexOf(':') + 2) + " " + classe + ": " + DicaClasse[Array.IndexOf(Classes, classe)].Substring(10)),
                }
            }));
            sp.Children.Add(Cartao("③ Edificação", new StackPanel
            {
                Children =
                {
                    Linha("Planta", Horiz(Num(out tLx, lx, "m", 96, "Dimensão da planta na direção X. Vazio = medida da caixa do modelo. l1 é a dimensão perpendicular ao vento."), Sep("×"), Num(out tLy, ly, "m", 96, "Dimensão da planta na direção Y (vazio = da caixa do modelo).")), dica: "lx × ly"),
                    Linha("Direções", CaixaDirecoes(), topo: 8),
                }
            }));
            var coef = new StackPanel();
            coef.Children.Add(Escolha(new[] { "auto", "manual" }, new[] { "Automático", "Manual" }, coefAuto ? "auto" : "manual", v => { Ler(); coefAuto = v == "auto"; Montar(); },
                new[] { "Ca pela figura 4 (edifício) e Cpe pelas tabelas 4 e 5 (galpão); Cpi pela permeabilidade.", "Digitar o coeficiente de arrasto Ca e os valores de Cpi." }));
            if (coefAuto)
            {
                coef.Children.Add(Linha("Permeab.", Escolha(new[] { "quatro_faces", "duas_faces_opostas" }, new[] { "4 faces", "2 faces opostas" }, perm, v => { Ler(); perm = v; Montar(); },
                    new[] { "Quatro faces igualmente permeáveis: Cpi = 0 ou −0,3 (NBR 6123 6.2.5).", "Duas faces opostas permeáveis: Cpi = +0,2 ou −0,3 (NBR 6123 6.2.5)." }), dica: "Permeabilidade das faces (Cpi)", topo: 10));
            }
            else
            {
                coef.Children.Add(Linha("Ca", Num(out tCa, Ca, null, 96, "Coeficiente de arrasto (edifícios). Vazio = figura 4."), dica: "Coeficiente de arrasto", topo: 10));
                var c = new TextBox { Text = cpiTxt, Width = 160, Height = 28, VerticalContentAlignment = VerticalAlignment.Center, Background = TemaPlant.BSup3, Foreground = TemaPlant.BTexto1, BorderBrush = TemaPlant.BBorda, CaretBrush = TemaPlant.BTexto1, Padding = new Thickness(4, 0, 4, 0), HorizontalAlignment = HorizontalAlignment.Left, ToolTip = "Coeficientes de pressão interna, separados por ponto e vírgula (ex.: 0,2; −0,3). Um caso de vento por valor (galpões)." };
                tCpi = c;
                coef.Children.Add(Linha("Cpi", c, dica: "Pressão interna"));
                coef.Children.Add(Nota("Cpi separados por ; — cada valor gera um caso por direção (galpões)."));
            }
            sp.Children.Add(Cartao("④ Coeficientes", coef));
        }

        // NBR 6123 8.1: a janela do vento 2D do Bruno (Vento.cs) no 3D — V0, S1, S3, categoria, classe automática, K
        void MontarBarras(StackPanel sp)
        {
            sp.Children.Add(CartaoVelocidade());
            var clVal = new[] { "", "A", "B", "C" };
            var clTx = new[] { "Auto", "A", "B", "C" };
            var clDica = new[] { "Automática pela maior dimensão da superfície frontal (5.3.2): altura × largura perpendicular ao vento, da caixa do modelo." }.Concat(DicaClasse).ToArray();
            sp.Children.Add(Cartao("② Rugosidade do terreno (S2)", new StackPanel
            {
                Children =
                {
                    Linha("Categoria", Escolha(Cats, Cats, cat, v => { Ler(); cat = v; Montar(); }, DicaCat), dica: "Categoria de rugosidade (I a V)"),
                    Linha("Classe", Escolha(clVal, clTx, classeBarras, v => { Ler(); classeBarras = v; Montar(); }, clDica), dica: "Classe (A, B, C) ou automática"),
                    Nota(DicaCat[Array.IndexOf(Cats, cat)] + " S2 = bm·Fr·(z/10)^p no ponto mais alto de cada barra (z acima da base)."),
                }
            }));
            var ck = new CheckBox { Content = UiPlaca.Tx("Fator de comprimento finito K (tabela 28)", 12, TemaPlant.BTexto1), IsChecked = usarK, VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "K reduz a força de barras curtas (l/c pequeno). l/c dobra na barra que toca um apoio (8.1.3); l/c > 100: K = 1. Desmarcado: K = 1 (lado seguro)." };
            ck.Checked += (s, e) => usarK = true; ck.Unchecked += (s, e) => usarK = false;
            sp.Children.Add(Cartao("③ Barras", new StackPanel
            {
                Children =
                {
                    Linha("Direções", CaixaDirecoes(), topo: 2),
                    Linha("K", ck, topo: 8),
                    Nota("Cf pela forma da seção (tabela 26): I/H 0,9 a 1,9 (vento nas mesas) ou 2,05 a 1,6 (paralelo); U 0,6 / 2,05; cantoneira 2,1; " +
                         "tubo e barra retangular pela relação profundidade/largura; circular pela tabela 27 (Re = 70 000·Vk·d, com o subcrítico conferido)."),
                    Nota("Não inclui: proteção entre barras (reticulados), vizinhança, vento dinâmico, equipamentos e fechamentos (lance no caso de vento)."),
                }
            }));
        }

        // NBR 8400:1984 5.5.4.1 e 5.6: as entradas da janela 2D do Bruno (vw, grupo, ψ) no 3D
        void Montar8400(StackPanel sp)
        {
            sp.Children.Add(Cartao("① Vento", new StackPanel
            {
                Children =
                {
                    Linha("vw serviço", Horiz(Num(out tVs, vServ, "m/s", 96, "Velocidade do vento limite de serviço (tabela 7: 20 m/s → Pa = 250 N/m²). Pa = vw²/1,6."), Nota0("Pa = " + F(vServ * vServ / 1.6, 0) + " N/m²")), 90, "Vento limite de serviço"),
                    Linha("Máximo", UiPlaca.Tx("800 N/m² (0–20 m) · 1100 (20–100 m) · 1300 (> 100 m)", 12, TemaPlant.BTexto2), 90, "Vento máximo fora de serviço (tabela 7), pela altura do topo de cada barra.", 8),
                    Linha("Direções", CaixaDirecoes(), 90, topo: 8),
                    Nota("C da tabela 8: perfis abertos e retangulares como viga de alma cheia (l/h = L/c: 1,2 a 1,6); tubo redondo como elemento tubular (d·√Pa/10 ≤ 1 → 1,2; > 1 → 0,7)."),
                }
            }));
            var gs = Enumerable.Range(1, 6).Select(i => i.ToString()).ToArray();
            var ck = new CheckBox { Content = UiPlaca.Tx("Usar as combinações da NBR 8400 (casos I, II e III)", 12, TemaPlant.BTexto1), IsChecked = comb8400, VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Marcado: o modelo passa a combinar pela NBR 8400 5.6 (Ações › Combinações mostra). Desmarcado: continua NBR 8681 (os ventos de serviço e máximo nunca entram juntos)." };
            ck.Checked += (s, e) => comb8400 = true; ck.Unchecked += (s, e) => comb8400 = false;
            sp.Children.Add(Cartao("② Combinações", new StackPanel
            {
                Children =
                {
                    Linha("Grupo", Horiz(Escolha(gs, gs, grupo.ToString(), v => { Ler(); grupo = int.Parse(v); Montar(); }, gs.Select(g => "Grupo " + g + ": Mx = " + F(Mx[int.Parse(g) - 1], 2) + " (tabela 10)").ToArray()), Nota0("Mx = " + F(Mx[grupo - 1], 2))), 90, "Grupo da estrutura (tabela 4) → Mx (tabela 10)"),
                    Linha("ψ", Horiz(Num(out tPsi, psi, null, 80, "Coeficiente dinâmico ψ da carga de serviço (tabela 5). 1,15 = ponte rolante com vL ≤ 0,25 m/s."), Nota0("tabela 5")), 90, "Coeficiente dinâmico", 8),
                    Linha("", ck, 90, topo: 8),
                    Nota("I = Mx·(G + ψ·Q) · II = I + vento de serviço · III = G + vento máximo (sem carga). ELS: G + Q (+ vento de serviço)."),
                    Nota("O motor verifica por estados limites: cada caso é multiplicado por ν/γa1 (ν = 1,5 / 1,33 / 1,1; γa1 = 1,10) para dar a folga das tensões admissíveis σe/ν."),
                }
            }));
        }

        static TextBlock Nota0(string s) { var t = UiPlaca.Tx(s, 11, TemaPlant.BTexto3); t.Margin = new Thickness(10, 0, 0, 0); t.VerticalAlignment = VerticalAlignment.Center; return t; }
        static TextBlock Sep(string s) { var t = UiPlaca.Tx(s, 12, TemaPlant.BTexto2); t.Margin = new Thickness(8, 0, 8, 0); return t; }

        bool Ler()
        {
            bool ok = true;
            if (norma == "nbr8400")
            {
                ok &= UiAcoes.Ler(tVs, out double vs, false, 0); if (ok) vServ = vs;
                ok &= UiAcoes.Ler(tPsi, out double ps, false, 0); if (ok) psi = ps;
            }
            else
            {
                ok &= UiAcoes.Ler(tV0, out double v0, false, 0); if (ok) V0 = v0;
                ok &= UiAcoes.Ler(tS1, out double s1, false, 0); if (ok) S1 = s1;
                ok &= UiAcoes.Ler(tS3, out double s3, false, 0); if (ok) S3 = s3;
            }
            if (norma == "edificacao")
            {
                ok &= UiAcoes.Ler(tLx, out double a, true, 0); if (ok) lx = a;
                ok &= UiAcoes.Ler(tLy, out double b, true, 0); if (ok) ly = b;
                if (!coefAuto)
                {
                    ok &= UiAcoes.Ler(tCa, out double ca, true, 0); if (ok) Ca = ca;
                    cpiTxt = tCpi.Text.Trim();
                }
            }
            if (!ok) { status.Text = "✕ Corrija os campos em vermelho (números positivos, ex.: 32 ou 1,05)."; status.Foreground = TemaPlant.BErro; }
            return ok;
        }

        JsonArray Direcoes() => new JsonArray(dirs.OrderBy(Ordem).Select(d => (JsonNode)d).ToArray());

        JsonObject Config()
        {
            if (norma == "nbr8400") return new JsonObject { ["tipo"] = "nbr8400", ["v_servico"] = vServ, ["direcoes"] = Direcoes() };
            var v = new JsonObject { ["V0"] = V0, ["S1"] = S1, ["S3"] = S3, ["categoria"] = cat, ["direcoes"] = Direcoes() };
            if (norma == "barras")
            {
                v["tipo"] = "barras"; v["usar_K"] = usarK;
                if (classeBarras != "") v["classe"] = classeBarras;
                return v;
            }
            v["classe"] = classe;
            if (!double.IsNaN(lx) && !double.IsNaN(ly)) v["dimensoes"] = new JsonObject { ["lx"] = lx, ["ly"] = ly };
            if (coefAuto) v["permeabilidade"] = perm;
            else
            {
                if (!double.IsNaN(Ca)) v["Ca"] = Ca;
                var cpis = cpiTxt.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(x => Portico.Num.Ler(x.Trim().Replace('−', '-'), out double c) ? c : double.NaN).ToList();
                if (cpis.Count > 0 && cpis.All(double.IsFinite)) v["cpi"] = new JsonArray(cpis.Select(c => (JsonNode)c).ToArray());
            }
            return v;
        }

        // config do PATCH: o vento e, ligado a ele, o conjunto de combinações (NBR 8400 com o vento da 8400, como no 2D)
        JsonObject Patch()
        {
            var o = new JsonObject { ["vento"] = Config() };
            if (norma == "nbr8400")
            {
                o["nbr8400"] = new JsonObject { ["grupo"] = grupo, ["psi"] = psi };
                o["combinacoes"] = comb8400 ? "nbr8400" : "nbr8681";
            }
            else if (combAtual == "nbr8400") o["combinacoes"] = "nbr8681";   // a 8400 sai junto com o vento dela
            return o;
        }

        static int Ordem(string d) { return Array.IndexOf(new[] { "+X", "-X", "+Y", "-Y" }, d); }

        async void Gerar()
        {
            try
            {
                if (!Ler()) return;
                if (dirs.Count == 0) { status.Text = "✕ Marque ao menos uma direção."; status.Foreground = TemaPlant.BErro; return; }
                JsonNode g = null;
                var patch = Patch();
                bool ok = await Tentar(status, async () =>
                {
                    await Motor.Chamar("PATCH", "/api/config", patch);
                    g = await Motor.Post("/api/acoes/gerar");
                }, "Gerando as ações de vento…");
                if (!ok) return;
                geracao = g;
                combAtual = S(patch["combinacoes"], combAtual);
                var casos = CasosVento();
                if (!casos.Contains(casoSel) && casos.Count > 0) casoSel = casos[0];
                status.Text = "✓ " + casos.Count + " casos de vento gerados (" + string.Join(", ", casos) + ")" +
                              (norma == "nbr8400" ? (comb8400 ? "; combinações NBR 8400 (casos I, II e III)." : "; combinações NBR 8681.") : ".");
                status.Foreground = TemaPlant.BSucesso;
                MontarPrevia();
            }
            catch (Exception ex) { Caixa.Erro(this, Title, ex.Message); }   // async void: exceção solta derruba o programa
        }

        List<string> CasosVento() => geracao == null ? new List<string>() : Lista(geracao["casos"]).Select(x => S(x)).Where(x => x.StartsWith("V")).ToList();

        // ---------- prévia ----------
        void MontarPrevia()
        {
            if (norma == "edificacao") { MontarPreviaPavimentos(); return; }
            var sp = new StackPanel();
            var casosV = CasosVento();
            var par = geracao?["parametros"]?[casoSel];
            var linhas = Lista(geracao?["forcas"]?[casoSel]).Where(x => x?["barra"] != null).ToList();
            bool b8400 = norma == "nbr8400";
            double wMax = linhas.Count == 0 ? double.NaN : linhas.Max(x => D(x["w_kN_m"], 0));
            if (b8400)
                sp.Children.Add(Resumo(
                    ("Vento", casoSel.StartsWith("VM") ? "máximo" : casoSel.StartsWith("VS") ? "serviço" : "—", null, "Vento limite de serviço (VS) ou vento máximo fora de serviço (VM), tabela 7."),
                    ("Pa máx.", linhas.Count == 0 ? "—" : F(linhas.Max(x => D(x["Pa_N_m2"], 0)), 0), "N/m²", "Maior pressão aerodinâmica entre as barras do caso."),
                    ("w máx.", F(wMax, 3), "kN/m", "Maior carga distribuída normal a uma barra."),
                    ("Força total", F(D(par?["F_total_kN"]), 1), "kN", "Soma de w·L das barras do caso.")));
            else
                sp.Children.Add(Resumo(
                    ("Classe", S(par?["classe"], "—"), null, "Classe usada no S2 (5.3.2); automática pela maior dimensão frontal: " + F(D(par?["dimensao_frontal_m"]), 1) + " m."),
                    ("q máx.", linhas.Count == 0 ? "—" : F(linhas.Max(x => D(x["q_kN_m2"], 0)), 3), "kN/m²", "Maior pressão dinâmica q(z) entre as barras (no topo de cada uma)."),
                    ("w máx.", F(wMax, 3), "kN/m", "Maior carga distribuída normal a uma barra."),
                    ("Força total", F(D(par?["F_total_kN"]), 1), "kN", "Soma de w·L das barras do caso.")));
            var dirBar = casosV.Count > 0 ? Escolha(casosV.ToArray(), casosV.Select(c => c.Replace("-", "−")).ToArray(), casoSel, v => { casoSel = v; MontarPrevia(); }) : null;
            sp.Children.Add(UiPlaca.TituloSecao("Carga em cada barra", dirBar, 2));
            if (geracao == null)
            {
                var vz = UiTabela.Vazio("tabela", "Ainda não gerado", "Clique em \"Gerar ações de vento\": o motor calcula a carga de cada barra e cria os casos " + (b8400 ? "VS±X/VS±Y (serviço) e VM±X/VM±Y (máximo)." : "V±X/V±Y."));
                vz.Margin = new Thickness(0, 40, 0, 0); sp.Children.Add(vz);
                hostDir.Child = sp;
                return;
            }
            const int Max = 40;
            var mostrar = linhas.OrderByDescending(x => D(x["w_kN_m"], 0)).Take(Max);
            if (b8400)
                sp.Children.Add(TabelaFixa(new[] { "Barra", "z (m)", "Pa (N/m²)", "C", "c (mm)", "sen²α", "w (kN/m)" }, new[] { false, true, true, true, true, true, true },
                    mostrar.Select(x => new[] { S(x["barra"]), F(D(x["z"]), 2), F(D(x["Pa_N_m2"]), 0), F(D(x["C"]), 2), F(D(x["c_m"]) * 1000, 0), F(D(x["sen2"]), 2), D(x["w_kN_m"], 0) < 1e-9 ? "—" : F(D(x["w_kN_m"]), 3) }),
                    new[] { 0, 64, 80, 52, 64, 56, 76.0 }));
            else
                sp.Children.Add(TabelaFixa(new[] { "Barra", "z (m)", "S2", "q (kN/m²)", "Cf", "c (mm)", "K", "sen²α", "w (kN/m)" }, new[] { false, true, true, true, true, true, true, true, true },
                    mostrar.Select(x => new[] { S(x["barra"]), F(D(x["z"]), 2), F(D(x["S2"]), 3), F(D(x["q_kN_m2"]), 3), F(D(x["Cf"]), 2), F(D(x["c_m"]) * 1000, 0), F(D(x["K"]), 2), F(D(x["sen2"]), 2), D(x["w_kN_m"], 0) < 1e-9 ? "—" : F(D(x["w_kN_m"]), 3) }),
                    new[] { 0, 56, 52, 70, 46, 56, 44, 50, 70.0 }));
            if (linhas.Count > Max) sp.Children.Add(Nota("Mostradas as " + Max + " barras mais carregadas de " + linhas.Count + "."));
            foreach (var f in linhas.GroupBy(x => S(x["forma"])).Select(g => g.First()).Take(5))
                sp.Children.Add(Nota(S(f["forma"]) + " (barra " + S(f["barra"]) + "): " + S(f["fonte"]) + "."));
            sp.Children.Add(Nota("Barra paralela ao vento não recebe carga (sen²α = 0). z = topo da barra acima da base; c = dimensão da seção vista pelo vento."));
            sp.Children.Add(Avisos(Lista(geracao["avisos"]).Where(a => S(a?["codigo"]).StartsWith("vento"))));
            hostDir.Child = sp;
        }

        void MontarPreviaPavimentos()
        {
            var sp = new StackPanel();
            var casosV = CasosVento();
            var par = geracao?["parametros"]?[casoSel];
            sp.Children.Add(Resumo(
                ("q no topo", F(D(par?["q_topo_kN_m2"]), 2), "kN/m²", "Pressão dinâmica na cota do último pavimento."),
                ("Vk no topo", F(D(par?["Vk_topo_m_s"]), 1), "m/s", "Velocidade característica Vk = V0·S1·S2·S3 no topo."),
                ("Ca", F(D(par?["Ca"]), 2), null, "Coeficiente de arrasto usado no caso selecionado (gerado)."),
                ("Força total", F(D(par?["F_total_kN"]), 1), "kN", "Soma das forças de arrasto do caso selecionado.")));

            var dirBar = casosV.Count > 0
                ? Escolha(casosV.ToArray(), casosV.Select(c => c.Replace("-", "−")).ToArray(), casoSel, v => { casoSel = v; MontarPrevia(); })
                : null;
            sp.Children.Add(UiPlaca.TituloSecao("Pressão e força por pavimento", dirBar, 2));
            if (pavs.Count == 0)
            {
                var vz = UiTabela.Vazio("tabela", "Sem pavimentos", "Cadastre os pavimentos (Ações › Pavimentos…) para calcular q(z) e as forças de vento por andar. Estrutura aberta (torre, guincho)? Use \"6123 · por barra\".");
                vz.Margin = new Thickness(0, 40, 0, 0); sp.Children.Add(vz);
            }
            else
            {
                var forcas = Lista(geracao?["forcas"]?[casoSel]).ToList();
                var linhas = pavs.AsEnumerable().Reverse().Select(p =>
                {
                    var f = forcas.FirstOrDefault(x => S(x?["pavimento"]) == p.nome);
                    return new[] { p.nome, F(p.z, 2), f == null ? "—" : F(D(f["S2"]), 3), f == null ? "—" : F(D(f["q_kN_m2"]), 3), f == null ? "—" : F(D(f["F_kN"]), 2) };
                });
                sp.Children.Add(TabelaFixa(new[] { "Pavimento", "z (m)", "S2", "q(z) (kN/m²)", "F " + casoSel.Replace("-", "−") + " (kN)" }, new[] { false, true, true, true, true }, linhas, new[] { 0, 90, 80, 120, 120.0 }));
                if (geracao == null) sp.Children.Add(Nota("S2, q(z) e as forças por pavimento vêm do motor e aparecem depois de \"Gerar ações de vento\"."));
                else if (par != null) sp.Children.Add(Nota("l1 = " + F(D(par["l1"]), 2) + " m · l2 = " + F(D(par["l2"]), 2) + " m · h/l1 = " + F(D(par["h_l1"]), 2) + " · l1/l2 = " + F(D(par["l1_l2"]), 2) + " · T1 ≈ " + F(D(par["T1_estimado_s"]), 2) + " s"));
            }
            if (geracao != null) sp.Children.Add(Avisos(Lista(geracao["avisos"]).Where(a => S(a?["codigo"]).StartsWith("vento"))));
            hostDir.Child = sp;
        }
    }
}
