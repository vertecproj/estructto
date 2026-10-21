// Cargas e casos do documento espacial (docs/MODELO-ESPACIAL.md, "Cargas e casos"): cargas pontuais na barra, distribuídas
// trapezoidais (q em ni, q2 em nj), carga em área distribuída às vigas de um nível por largura de influência, e as janelas
// do motor (Casos, Vento, Sismo, Pavimentos, Combinações) editando o modelo espacial em vez do Modelo 2D.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Portico;

namespace NovoFtool
{
    public partial class DocumentoEspacial
    {
        // POST /api/modelo/lote (atômico no motor).
        public Task Lote(JsonArray itens) => Motor.Exclusivo(async () =>
        {
            await Garantir();
            await Motor.Post("/api/modelo/lote", new JsonObject { ["itens"] = itens });
            TemHistorico = true;
            await Recarregar();
        });

        // GET com este modelo no servidor.
        public Task<JsonNode> Consultar(string rota) => Motor.Exclusivo(async () => { await Garantir(); return await Motor.Get(rota); });

        // Roda `abrir` (janela modal do motor) com este modelo no servidor e a fila do Motor travada; relê o modelo no fim.
        // true = o modelo mudou (o cálculo anterior é descartado).
        public Task<bool> ComJanela(Func<Task> abrir) => Motor.Exclusivo(async () =>
        {
            await Garantir();
            await abrir();
            var novo = (JsonObject)await Motor.Get("/api/modelo");
            if (novo.ToJsonString() == Json.ToJsonString()) return false;
            TemHistorico = true;
            await Recarregar();
            return true;
        });

        // Carga em área p (kN/m², para baixo) no nível z, regra de uma direção: só as vigas horizontais paralelas a X (emX) ou a Y
        // recebem; cada uma leva q = p · (metade da distância à paralela vizinha de cada lado, entre as que se sobrepõem em planta).
        // ponytail: O(n²) por nível; viga de borda leva só o lado interno (sem balanço de laje); a sobreposição parcial conta inteira;
        // não sabe onde há laje (vão sem laje também carrega). Laje em duas direções/charneiras: trocar esta função.
        public static List<(string barra, double q, double L)> CargaArea(JsonObject m, double z, double p, bool emX, double tol = 0.001)
        {
            var nos = m["nos"].AsObject();
            var vs = new List<(string id, double a, double b, double t)>();
            foreach (var kv in m["barras"].AsObject())
            {
                var ni = nos[(string)kv.Value["ni"]]; var nj = nos[(string)kv.Value["nj"]];
                if (ni == null || nj == null || Math.Abs((double)ni["z"] - z) > tol || Math.Abs((double)nj["z"] - z) > tol) continue;
                double x1 = (double)ni["x"], y1 = (double)ni["y"], x2 = (double)nj["x"], y2 = (double)nj["y"];
                double L = Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
                if (L < 1e-6 || Math.Abs(emX ? y2 - y1 : x2 - x1) > 1e-3 * L) continue;
                double s1 = emX ? x1 : y1, s2 = emX ? x2 : y2;
                vs.Add((kv.Key, Math.Min(s1, s2), Math.Max(s1, s2), emX ? (y1 + y2) / 2 : (x1 + x2) / 2));
            }
            var r = new List<(string, double, double)>();
            foreach (var v in vs)
            {
                var viz = vs.Where(o => Math.Min(o.b, v.b) - Math.Max(o.a, v.a) > 1e-6).Select(o => o.t - v.t).ToList();
                double mais = viz.Where(d => d > 1e-6).DefaultIfEmpty(0).Min(), menos = viz.Where(d => d < -1e-6).Select(d => -d).DefaultIfEmpty(0).Min();
                r.Add((v.id, p * (mais + menos) / 2, v.b - v.a));
            }
            return r;
        }

        // Cotas das vigas (nós com barra horizontal), de baixo para cima.
        public IEnumerable<double> NiveisComVigas()
        {
            var nos = Json["nos"].AsObject();
            return Json["barras"].AsObject().Select(kv => (nos[(string)kv.Value["ni"]], nos[(string)kv.Value["nj"]]))
                .Where(x => x.Item1 != null && x.Item2 != null && Math.Abs((double)x.Item1["z"] - (double)x.Item2["z"]) <= 0.001)
                .Select(x => Math.Round((double)x.Item1["z"], 3)).Distinct().OrderBy(z => z);
        }
    }

    public partial class MainWindow
    {
        // Janela do motor (Casos, Vento, Sismo, Pavimentos, Combinações) sobre o documento espacial ativo.
        void JanelaMotorEsp(Action<Window> abrir) => _ = JanelaMotorEsp(w => { abrir(w); return Task.CompletedTask; });

        // abrir roda inteiro dentro do Motor.Exclusivo (await): a busca da janela vê este modelo no servidor.
        internal async Task JanelaMotorEsp(Func<Window, Task> abrir)
        {
            var d = Esp;
            if (d == null) return;
            if (espRodando) { MostrarToast("Aguarde: o motor ainda está trabalhando.", true); return; }
            espRodando = true;
            try
            {
                bool mudou = await d.ComJanela(() => abrir(this));
                if (mudou && Esp == d) DepoisDeMudarEspacial("Modelo espacial alterado pela janela do motor (casos, ações ou pavimentos).");
            }
            catch (Exception ex) { StatusMsg.Text = "⚠ Motor: " + ex.Message; Log(StatusMsg.Text); }
            finally { espRodando = false; }
            AvisarHistorico(d);
        }

        internal void RolarPainelEsp() => (painelEsp.Content as ScrollViewer)?.ScrollToEnd();
        internal void SelecionarNadaEsp() => SelecionarEsp(null);

        static TextBlock TxEsp(string s, double tam = 12, bool fraco = false) => new TextBlock { Text = s, FontSize = tam, Foreground = fraco ? TemaPlant.BTexto2 : TemaPlant.BTexto1, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        static void SecaoEsp(StackPanel sp, string t) => sp.Children.Add(new TextBlock { Text = t.ToUpperInvariant(), FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = TemaPlant.BTexto2, Margin = new Thickness(0, 10, 0, 4) });

        // Campos curtos rotulados numa linha; devolve as caixas para ler depois.
        static List<TextBox> CamposEsp(StackPanel sp, string titulo, string[] rots, double[] ini)
        {
            var wp = new WrapPanel { Margin = new Thickness(0, 1, 0, 1) };
            if (titulo != null) wp.Children.Add(new TextBlock { Text = titulo, Width = 40, Foreground = TemaPlant.BTexto2, VerticalAlignment = VerticalAlignment.Center });
            var tbs = new List<TextBox>();
            for (int i = 0; i < rots.Length; i++)
            {
                wp.Children.Add(new TextBlock { Text = rots[i], Foreground = TemaPlant.BTexto2, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
                var tb = JanelaEscura.Campo(Num.Fmt(ini[i], 2), 52); tb.Margin = new Thickness(0, 0, 8, 2);
                tbs.Add(tb); wp.Children.Add(tb);
            }
            sp.Children.Add(wp);
            return tbs;
        }

        bool LerEsp(List<TextBox> tbs, out double[] v)
        {
            v = new double[tbs.Count];
            for (int i = 0; i < v.Length; i++)
                if (!double.TryParse(tbs[i].Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v[i])) { StatusMsg.Text = "⚠ Número inválido: " + tbs[i].Text; return false; }
            return true;
        }

        static void BotoesEsp(StackPanel sp, params (string txt, Action a, bool prim)[] bs)
        {
            var wp = new WrapPanel { Margin = new Thickness(0, 2, 0, 6) };
            foreach (var (t, a, p) in bs) { var b = JanelaEscura.B(t, a, p); b.Margin = new Thickness(0, 0, 6, 0); wp.Children.Add(b); }
            sp.Children.Add(wp);
        }

        static double[] Vet(JsonNode n, int k = 3) => n?.AsArray().Take(k).Select(x => (double)x).ToArray() ?? new double[k];

        // Painel da barra: distribuídas (uniforme ou trapezoidal, global) e pontuais (a desde ni, global), editar e apagar.
        void PainelCargasBarra(StackPanel sp, DocumentoEspacial d, string id, Action casoCargas)
        {
            var el = d.Json["barras"][id]; var nos = d.Json["nos"];
            var ni = nos[(string)el["ni"]]; var nj = nos[(string)el["nj"]];
            double L = Math.Sqrt(new[] { "x", "y", "z" }.Sum(k => Math.Pow((double)nj[k] - (double)ni[k], 2)));
            JsonNode q2De(double[] v) => v[3] == v[0] && v[4] == v[1] && v[5] == v[2] ? null : new JsonArray(v[3], v[4], v[5]);
            string[] q = { "qx", "qy", "qz" }, f = { "Fx", "Fy", "Fz" };

            SecaoEsp(sp, "Carga distribuída (global, kN/m)");
            sp.Children.Add(TxEsp("Trapezoidal: valor em ni e em nj (iguais = uniforme).", 11, true));
            foreach (var c in (d.Json["cargas_distribuidas"] as JsonArray ?? new JsonArray()).Where(c => (string)c["barra"] == id).ToList())
            {
                string cid = (string)c["id"]; var v1 = Vet(c["q"]); var v2 = c["q2"] == null ? v1 : Vet(c["q2"]);
                sp.Children.Add(TxEsp((string)c["caso"] + " · " + cid + (c["q2"] == null ? " · uniforme" : " · trapezoidal"), 11.5));
                var tbs = CamposEsp(sp, "ni", q, v1).Concat(CamposEsp(sp, "nj", q, v2)).ToList();
                BotoesEsp(sp, ("Gravar", () => { if (LerEsp(tbs, out var v)) _ = Espacial(dd => dd.Editar("carga_distribuida", cid, new JsonObject { ["q"] = new JsonArray(v[0], v[1], v[2]), ["q2"] = q2De(v) }), "Carga " + cid + " alterada."); }, false),
                              ("Apagar", () => _ = Espacial(dd => dd.Remover("carga_distribuida", cid), "Carga " + cid + " apagada."), false));
            }
            casoCargas();
            if (d.CasoAtivo != null)
            {
                var tn = CamposEsp(sp, "ni", q, new double[] { 0, 0, -5 }).Concat(CamposEsp(sp, "nj", q, new double[] { 0, 0, -5 })).ToList();
                BotoesEsp(sp, ("Adicionar distribuída", () =>
                {
                    if (!LerEsp(tn, out var v)) return;
                    var dados = new JsonObject { ["caso"] = d.CasoAtivo, ["barra"] = id, ["q"] = new JsonArray(v[0], v[1], v[2]) };
                    if (q2De(v) is JsonNode q2) dados["q2"] = q2;
                    _ = Espacial(dd => dd.Adicionar("carga_distribuida", dados), "Carga distribuída na barra " + id + " (" + d.CasoAtivo + ").");
                }, true));
            }

            SecaoEsp(sp, "Cargas pontuais (global, kN)");
            sp.Children.Add(TxEsp("a = distância desde ni ao longo da barra (0 a " + Num.Fmt(L, 2) + " m).", 11, true));
            foreach (var c in (d.Json["cargas_pontuais"] as JsonArray ?? new JsonArray()).Where(c => (string)c["barra"] == id).ToList())
            {
                string cid = (string)c["id"];
                sp.Children.Add(TxEsp((string)c["caso"] + " · " + cid, 11.5));
                var tbs = CamposEsp(sp, "a (m)", new[] { "" }, new[] { (double)c["a"] }).Concat(CamposEsp(sp, "F", f, Vet(c["F"]))).ToList();
                BotoesEsp(sp, ("Gravar", () => { if (LerEsp(tbs, out var v)) _ = Espacial(dd => dd.Editar("carga_pontual", cid, new JsonObject { ["a"] = v[0], ["F"] = new JsonArray(v[1], v[2], v[3]) }), "Carga " + cid + " alterada."); }, false),
                              ("Apagar", () => _ = Espacial(dd => dd.Remover("carga_pontual", cid), "Carga " + cid + " apagada."), false));
            }
            if (d.CasoAtivo != null)
            {
                var tp = CamposEsp(sp, "a (m)", new[] { "" }, new[] { L / 2 }).Concat(CamposEsp(sp, "F", f, new double[] { 0, 0, -10 })).ToList();
                BotoesEsp(sp, ("Adicionar pontual", () =>
                {
                    if (LerEsp(tp, out var v))
                        _ = Espacial(dd => dd.Adicionar("carga_pontual", new JsonObject { ["caso"] = dd.CasoAtivo, ["barra"] = id, ["a"] = v[0], ["F"] = new JsonArray(v[1], v[2], v[3]) }), "Carga pontual na barra " + id + " (" + d.CasoAtivo + ").");
                }, true));
            }
        }

        // Painel do modelo (sem escolha): casos e janelas do motor + carga em área num nível.
        void PainelCargaArea(StackPanel sp, DocumentoEspacial d, Action casoCargas)
        {
            SecaoEsp(sp, "Casos e ações");
            BotoesEsp(sp, ("Casos de carga…", () => JanelaMotorEsp(ComandosAcoes.AbrirCasos), false), ("Pavimentos…", () => JanelaMotorEsp(ComandosAcoes.AbrirPavimentos), false));
            BotoesEsp(sp, ("Vento…", () => JanelaMotorEsp(ComandosAcoes.AbrirVento), false), ("Sismo…", () => JanelaMotorEsp(ComandosAcoes.AbrirSismo), false));

            var niveis = d.NiveisComVigas().Where(z => z > 0.001).ToList();
            if (niveis.Count == 0) return;
            SecaoEsp(sp, "Carga em área (kN/m²)");
            sp.Children.Add(TxEsp("Vai para as vigas do nível na direção escolhida: q = p × (metade do vão até a viga paralela de cada lado).", 11, true));
            var cbZ = new ComboBox(); foreach (var z in niveis) cbZ.Items.Add(Num.Fmt(z, 2)); cbZ.SelectedIndex = niveis.Count - 1;
            var cbDir = new ComboBox(); cbDir.Items.Add("vigas em X"); cbDir.Items.Add("vigas em Y"); cbDir.SelectedIndex = 0;
            foreach (var (rot, cb) in new[] { ("Nível z (m)", cbZ), ("Recebem", cbDir) })
            {
                var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) }); g.ColumnDefinitions.Add(new ColumnDefinition());
                g.Children.Add(TxEsp(rot, 12, true)); Grid.SetColumn(cb, 1); g.Children.Add(cb);
                sp.Children.Add(g);
            }
            var tb = CamposEsp(sp, null, new[] { "p" }, new double[] { 3 });
            casoCargas();
            if (d.CasoAtivo != null)
                BotoesEsp(sp, ("Distribuir nas vigas", () => { if (LerEsp(tb, out var v)) _ = DistribuirArea(niveis[cbZ.SelectedIndex], v[0], cbDir.SelectedIndex == 0); }, true));
        }

        // Uma chamada /api/modelo/lote com uma carga distribuída por viga.
        internal Task DistribuirArea(double z, double p, bool emX)
        {
            var d = Esp;
            var r = DocumentoEspacial.CargaArea(d.Json, z, p, emX).Where(x => x.q > 1e-9).ToList();
            if (r.Count == 0) { MostrarToast("Nenhuma viga " + (emX ? "em X" : "em Y") + " com vizinha paralela no nível z = " + Num.Fmt(z, 2) + " m.", true); return Task.CompletedTask; }
            string caso = d.CasoAtivo;
            var itens = new JsonArray(r.Select(x => (JsonNode)new JsonObject { ["tipo"] = "carga_distribuida", ["dados"] = new JsonObject { ["caso"] = caso, ["barra"] = x.barra, ["q"] = new JsonArray(0.0, 0.0, -x.q) } }).ToArray());
            return Espacial(dd => dd.Lote(itens), "Carga em área " + Num.Fmt(p, 2) + " kN/m² (" + caso + ") no nível z = " + Num.Fmt(z, 2) + " m: " + Pl.N(r.Count, "viga", "vigas") +
                " " + (emX ? "em X" : "em Y") + ", Σ q·L = " + Num.Fmt(r.Sum(x => x.q * x.L), 1) + " kN.");
        }
    }

    public static partial class Capturas
    {
        // 48) Cargas do documento espacial: pontual e trapezoidal na barra, carga em área num nível do edifício, casos pelo motor.
        static void CapturasCargas(MainWindow w, string barra)
        {
            var d = w.EspacialAtivo;
            Esperar(w.OperarEspacial(async dd =>
            {
                await dd.Adicionar("carga_pontual", new JsonObject { ["caso"] = dd.CasoAtivo, ["barra"] = barra, ["a"] = 1.0, ["F"] = new JsonArray(0.0, 0.0, -12.0) });
                await dd.Adicionar("carga_distribuida", new JsonObject { ["caso"] = dd.CasoAtivo, ["barra"] = barra, ["q"] = new JsonArray(0.0, 0.0, -2.0), ["q2"] = new JsonArray(0.0, 0.0, -6.0) });
            }, "teste: pontual e trapezoidal"));
            d = w.EspacialAtivo;
            var cp = d.Json["cargas_pontuais"].AsArray().FirstOrDefault(c => (string)c["barra"] == barra) ?? throw new InvalidOperationException("carga pontual não entrou: " + w.StatusMsg.Text);
            if (!d.Json["cargas_distribuidas"].AsArray().Any(c => (string)c["barra"] == barra && c["q2"] != null)) throw new InvalidOperationException("q2 não entrou: " + w.StatusMsg.Text);
            Esperar(w.EditarEspacial("carga_pontual", (string)cp["id"], new JsonObject { ["a"] = 1.5 }));
            if ((double)w.EspacialAtivo.Json["cargas_pontuais"].AsArray().First(c => (string)c["id"] == (string)cp["id"])["a"] != 1.5) throw new InvalidOperationException("editar carga pontual falhou");
            w.EscolherEspacial("barra", barra);
            EsperarIdle();
            w.RolarPainelEsp();   // seção das cargas pontuais
            EsperarIdle();
            CapturarPrincipal(w, "48-espacial-carga-pontual");

            // carga em área no nível mais alto: Σ q·L = p · área coberta pelas vigas em X (grade regular do gerador)
            d = w.EspacialAtivo;
            double z = d.NiveisComVigas().Max(), p = 4;
            var r = DocumentoEspacial.CargaArea(d.Json, z, p, true);
            var nos = d.Json["nos"].AsObject().Select(kv => kv.Value).Where(n => Math.Abs((double)n["z"] - z) < 0.001).ToList();
            double area = (nos.Max(n => (double)n["x"]) - nos.Min(n => (double)n["x"])) * (nos.Max(n => (double)n["y"]) - nos.Min(n => (double)n["y"]));
            double soma = r.Sum(x => x.q * x.L);
            if (Math.Abs(soma - p * area) > 1e-6 * p * area) throw new InvalidOperationException("carga em área: Σ q·L = " + soma + " ≠ p·A = " + p * area);
            int n0 = d.Json["cargas_distribuidas"].AsArray().Count;
            Esperar(w.DistribuirArea(z, p, true));
            if (w.EspacialAtivo.Json["cargas_distribuidas"].AsArray().Count != n0 + r.Count(x => x.q > 1e-9)) throw new InvalidOperationException("lote da carga em área falhou: " + w.StatusMsg.Text);
            w.EscolherEspacial("barra", r.OrderByDescending(x => x.q).First().barra);
            EsperarIdle();
            w.vista3D.MostrarVista(VistaPadrao.Iso, false);
            CapturarPrincipal(w, "48-espacial-carga-area");
            w.SelecionarNadaEsp();
            EsperarIdle();
            w.RolarPainelEsp();
            EsperarIdle();
            CapturarPrincipal(w, "48-espacial-painel-modelo");

            // casos: a janela lê os casos do modelo espacial; um caso criado no motor volta para o documento ao reler
            d = w.EspacialAtivo;
            var jc = new JanelaCasosCarga(Esperar(d.Consultar("/api/modelo/caso")));
            PrepararDialogoForaDaTela(jc); jc.Show(); EsperarIdle();
            CapturarDialogo(jc, "48-espacial-casos.png");
            jc.Close();
            bool mudou = Esperar(d.ComJanela(() => Motor.Post("/api/modelo/caso", new JsonObject { ["nome"] = "Q_teste", ["tipo"] = "Q" })));
            if (!mudou || d.Json["casos"]?["Q_teste"] == null) throw new InvalidOperationException("caso novo não voltou ao documento espacial");

            // vento, sismo e pavimentos leem o modelo do motor: com o espacial no servidor, mostram o edifício
            Esperar(d.Ativar());
            foreach (var (jan, nome) in new (Window, string)[] { (new JanelaPavimentos(), "pavimentos"), (new JanelaVento(), "vento"), (new JanelaSismo(), "sismo") })
            {
                PrepararDialogoForaDaTela(jan); jan.Show();
                for (int i = 0; i < 200; i++) { EsperarIdle(); System.Threading.Thread.Sleep(15); }   // a janela lê o motor no Loaded
                CapturarDialogo(jan, "48-espacial-" + nome + ".png");
                jan.Close();
            }
        }

        static T Esperar<T>(Task<T> t) { Esperar((Task)t); return t.Result; }
    }
}
