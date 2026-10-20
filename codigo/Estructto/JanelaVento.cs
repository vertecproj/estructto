// Janela "Vento (NBR 6123)" da aba Ações: V0, S1, S2 (categoria e classe), S3, dimensões da edificação e
// coeficientes (automáticos pela norma ou manuais). Grava em config.vento (PATCH /api/config) e gera os casos
// V±X/V±Y (POST /api/acoes/gerar). A prévia mostra q(z) e a força de arrasto de cada pavimento.
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

        readonly Border hostEsq = new Border(), hostDir = new Border();
        readonly TextBlock status = Status();
        TextBox tV0, tS1, tS3, tLx, tLy, tCa, tCpi;

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

        // resumo = GET /api/resumo (config + pavimentos + caixa); geracao = POST /api/acoes/gerar. Nulos: busca no motor.
        public JanelaVento(JsonNode resumo = null, JsonNode geracao = null)
        {
            Title = "Vento (NBR 6123)"; Width = 1080; Height = 700; MinWidth = 900; MinHeight = 520;
            var corpo = DuasColunas(hostEsq, hostDir, 450);
            var gerar = B("Gerar ações de vento", Gerar, true, "Grava os dados do vento no modelo e cria (ou recria) os casos V+X, V−X, V+Y e V−Y com as forças por pavimento.");
            var fechar = B("Fechar", Close);
            Content = UiAcoes.Montar(this, Cabecalho("Vento", "NBR 6123", "Força de arrasto por pavimento a partir da pressão dinâmica q(z) = 0,613·(V0·S1·S2·S3)²."), corpo, Rodape(status, fechar, gerar));
            this.geracao = geracao;
            if (resumo != null) Carregar(resumo);
            else Loaded += async (s, e) => { JsonNode r = null; if (await Tentar(status, async () => r = await Motor.Get("/api/resumo"))) Carregar(r); else Montar(); };
            Montar();
        }

        void Carregar(JsonNode r)
        {
            var v = r?["config"]?["vento"];
            if (v != null)
            {
                V0 = D(v["V0"], V0); S1 = D(v["S1"], 1); S3 = D(v["S3"], 1);
                cat = S(v["categoria"], cat).ToUpperInvariant(); classe = S(v["classe"], classe).ToUpperInvariant();
                lx = D(v["dimensoes"]?["lx"]); ly = D(v["dimensoes"]?["ly"]);
                Ca = D(v["Ca"]);
                if (v["permeabilidade"] != null) perm = S(v["permeabilidade"]);
                if (v["cpi"] is JsonArray cp) cpiTxt = string.Join("; ", cp.Select(x => F(D(x), 2)));
                coefAuto = double.IsNaN(Ca) && v["cpi"] == null;
                if (v["direcoes"] is JsonArray ds) { dirs.Clear(); foreach (var d in ds) dirs.Add(S(d)); }
            }
            pavs.Clear();
            foreach (var p in Lista(r?["pavimentos"])) pavs.Add((S(p?["nome"]), D(p?["z"], 0)));
            pavs.Sort((a, b) => a.z.CompareTo(b.z));
            baseZ = D(r?["caixa"]?["min"]?[2], 0);
            Montar();
        }

        // ---------- entradas ----------
        void Montar()
        {
            var sp = new StackPanel();
            sp.Children.Add(Cartao("① Velocidade básica", new StackPanel
            {
                Children =
                {
                    Linha("V0", Horiz(Num(out tV0, V0, "m/s", 96, "Velocidade básica do vento (isopletas, NBR 6123 fig. 1), rajada de 3 s a 10 m de altura."), Nota0("isopletas: 30 a 50 m/s")), dica: "Velocidade básica (m/s)"),
                    Linha("S1 · S3", Horiz(Num(out tS1, S1, null, 80, "Fator topográfico S1: 1,00 terreno plano; 0,90 vale protegido; talude/morro > 1 (NBR 6123 5.2)."), Sep("S3"), Num(out tS3, S3, null, 80, "Fator estatístico S3 (NBR 6123 tabela 4): grupo 1 = 1,11; grupo 2 = 1,06; grupo 3 = 1,00 (residências, comércio, indústria); grupo 4 = 0,95; grupo 5 = 0,83.")), dica: "Fator topográfico S1 e fator estatístico S3"),
                    Nota("S1: plano 1,00 · vale 0,90. S3: grupo 3 (comum) 1,00; essenciais 1,11."),
                }
            }));
            sp.Children.Add(Cartao("② Rugosidade do terreno (S2)", new StackPanel
            {
                Children =
                {
                    Linha("Categoria", Horiz(Escolha(Cats, Cats, cat, v => { Ler(); cat = v; Montar(); }, DicaCat), Sep("Classe"), Escolha(Classes, Classes, classe, v => { Ler(); classe = v; Montar(); }, DicaClasse)), dica: "Categoria de rugosidade (I a V) e classe da edificação (A, B, C)"),
                    Nota(cat + ": " + DicaCat[Array.IndexOf(Cats, cat)].Substring(DicaCat[Array.IndexOf(Cats, cat)].IndexOf(':') + 2) + " " + classe + ": " + DicaClasse[Array.IndexOf(Classes, classe)].Substring(10)),
                }
            }));
            var chks = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var d in new[] { "+X", "-X", "+Y", "-Y" })
            {
                string dd = d;
                var cb = new CheckBox { Content = d.Replace("-", "−"), IsChecked = dirs.Contains(d), Foreground = TemaPlant.BTexto1, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Gerar o caso de vento V" + d };
                cb.Checked += (s, e) => dirs.Add(dd);
                cb.Unchecked += (s, e) => dirs.Remove(dd);
                chks.Children.Add(cb);
            }
            sp.Children.Add(Cartao("③ Edificação", new StackPanel
            {
                Children =
                {
                    Linha("Planta", Horiz(Num(out tLx, lx, "m", 96, "Dimensão da planta na direção X. Vazio = medida da caixa do modelo. l1 é a dimensão perpendicular ao vento."), Sep("×"), Num(out tLy, ly, "m", 96, "Dimensão da planta na direção Y (vazio = da caixa do modelo).")), dica: "lx × ly"),
                    Linha("Direções", chks, topo: 8),
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
            hostEsq.Child = sp;
            MontarPrevia();
        }

        static TextBlock Nota0(string s) { var t = UiPlaca.Tx(s, 11, TemaPlant.BTexto3); t.Margin = new Thickness(10, 0, 0, 0); return t; }
        static TextBlock Sep(string s) { var t = UiPlaca.Tx(s, 12, TemaPlant.BTexto2); t.Margin = new Thickness(8, 0, 8, 0); return t; }

        bool Ler()
        {
            bool ok = true;
            ok &= UiAcoes.Ler(tV0, out double v0, false, 0); if (ok) V0 = v0;
            ok &= UiAcoes.Ler(tS1, out double s1, false, 0); if (ok) S1 = s1;
            ok &= UiAcoes.Ler(tS3, out double s3, false, 0); if (ok) S3 = s3;
            ok &= UiAcoes.Ler(tLx, out double a, true, 0); if (ok) lx = a;
            ok &= UiAcoes.Ler(tLy, out double b, true, 0); if (ok) ly = b;
            if (!coefAuto)
            {
                ok &= UiAcoes.Ler(tCa, out double ca, true, 0); if (ok) Ca = ca;
                cpiTxt = tCpi.Text.Trim();
            }
            if (!ok) { status.Text = "✕ Corrija os campos em vermelho (números positivos, ex.: 32 ou 1,05)."; status.Foreground = TemaPlant.BErro; }
            return ok;
        }

        JsonObject Config()
        {
            var v = new JsonObject { ["V0"] = V0, ["S1"] = S1, ["S3"] = S3, ["categoria"] = cat, ["classe"] = classe, ["direcoes"] = new JsonArray(dirs.OrderBy(Ordem).Select(d => (JsonNode)d).ToArray()) };
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

        static int Ordem(string d) { return Array.IndexOf(new[] { "+X", "-X", "+Y", "-Y" }, d); }

        async void Gerar()
        {
            if (!Ler()) return;
            if (dirs.Count == 0) { status.Text = "✕ Marque ao menos uma direção."; status.Foreground = TemaPlant.BErro; return; }
            JsonNode g = null;
            bool ok = await Tentar(status, async () =>
            {
                await Motor.Chamar("PATCH", "/api/config", new JsonObject { ["vento"] = Config() });
                g = await Motor.Post("/api/acoes/gerar");
            }, "Gerando as ações de vento…");
            if (!ok) return;
            geracao = g;
            var casos = Lista(g?["casos"]).Select(x => S(x)).Where(x => x.StartsWith("V")).ToList();
            if (!casos.Contains(casoSel) && casos.Count > 0) casoSel = casos[0];
            status.Text = "✓ " + casos.Count + " casos de vento gerados (" + string.Join(", ", casos) + ")."; status.Foreground = TemaPlant.BSucesso;
            MontarPrevia();
        }

        // ---------- prévia ----------
        void MontarPrevia()
        {
            var sp = new StackPanel();
            var casosV = geracao == null ? new List<string>() : Lista(geracao["casos"]).Select(x => S(x)).Where(x => x.StartsWith("V")).ToList();
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
                var vz = UiTabela.Vazio("tabela", "Sem pavimentos", "Cadastre os pavimentos (Ações › Pavimentos…) para calcular q(z) e as forças de vento por andar.");
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
