// Documento "Modelo espacial" (docs/MODELO-ESPACIAL.md): a fonte da verdade é o modelo do motor Python (o JSON de
// codigo/motor, ESPEC §5), não o Modelo 2D do Nucleo.cs. Abre .json do motor, obras .ed3 do CYPE e os exemplos do motor;
// gera edifício, galpão e pórtico 3D; mostra na Vista3D (sempre 3D); edita nó/barra por PATCH /api/modelo/{tipo}/{id};
// desfazer/refazer pelo motor; F5 = POST /api/calcular; salva o JSON do motor.
// O servidor guarda um modelo só: antes de cada operação o documento ativo se recoloca lá (Garantir).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Portico;

namespace NovoFtool
{
    // Nó ou barra do motor escolhido na vista (mesma instância entre recargas: a Vista3D compara por referência).
    public sealed class RefMotor { public string Tipo, Id; }

    public class DocumentoEspacial
    {
        public JsonObject Json;              // modelo do motor (cópia do último GET /api/modelo)
        public string Arquivo, Titulo;       // Arquivo: .json do motor (null = ainda não salvo)
        public List<string> Avisos = new List<string>();
        public JsonNode Calculo;             // POST /api/calcular (null = sem cálculo)
        public string CombDiagrama;          // combinação dos diagramas, deformada (a da barra mais solicitada)
        public Dictionary<string, JsonNode> PiorPorBarra = new Dictionary<string, JsonNode>();
        JsonObject esforcos, desl;
        readonly Dictionary<string, RefMotor> refs = new Dictionary<string, RefMotor>();
        static DocumentoEspacial noMotor;

        public bool Calculado => Calculo != null;
        public string Nome => Arquivo != null ? Path.GetFileName(Arquivo) : Titulo ?? "Modelo espacial";
        static string Url(string s) => Uri.EscapeDataString(s);
        static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

        public RefMotor Ref(string tipo, string id)
        {
            string k = tipo + "\n" + id;
            if (!refs.TryGetValue(k, out var r)) refs[k] = r = new RefMotor { Tipo = tipo, Id = id };
            return r;
        }

        // Recoloca este modelo no servidor (por arquivo na pasta de trabalho: "abrir" zera as pilhas de desfazer).
        // ponytail: trocar de aba entre dois modelos espaciais perde o desfazer do outro; guardar as pilhas se fizer falta.
        async Task Garantir()
        {
            await Motor.Get("/api/versao");
            if (noMotor == this && Motor.Dono == this) return;
            File.WriteAllText(Path.Combine(Motor.Pasta, "_espacial.json"), Json.ToJsonString());
            await Motor.Post("/api/modelo/abrir", new JsonObject { ["caminho"] = "_espacial.json" });
            noMotor = this; Motor.Dono = this;
        }

        async Task Recarregar()
        {
            Json = (JsonObject)await Motor.Get("/api/modelo");
            Calculo = null; esforcos = desl = null; CombDiagrama = null; PiorPorBarra.Clear();
        }

        // O modelo que `preparar` deixou no servidor vira um documento novo (sem pilha de desfazer).
        static async Task<DocumentoEspacial> Criar(Func<Task> preparar, string titulo, string arquivo)
        {
            var d = new DocumentoEspacial { Titulo = titulo, Arquivo = arquivo };
            await Motor.Get("/api/versao");
            noMotor = null;
            await preparar();
            await d.Recarregar();
            await d.Garantir();
            return d;
        }

        public static Task<DocumentoEspacial> Exemplo(string nome) => Criar(() => Motor.Post("/api/exemplos/" + Url(nome), new JsonObject()), nome, null);

        public static async Task<DocumentoEspacial> AbrirArquivo(string caminho)
        {
            if (caminho.EndsWith(".ed3", StringComparison.OrdinalIgnoreCase))
            {
                JsonNode r = null;
                var d = await Criar(async () => r = await Motor.Post("/api/importar_cype", new JsonObject
                {
                    ["nome"] = Path.GetFileName(caminho), ["conteudo_base64"] = Convert.ToBase64String(File.ReadAllBytes(caminho)),
                }), Path.GetFileNameWithoutExtension(caminho) + " (CYPE)", null);
                foreach (var a in r?["avisos"]?.AsArray() ?? new JsonArray()) d.Avisos.Add(a is JsonValue ? (string)a : a?.ToJsonString());
                return d;
            }
            var doc = new DocumentoEspacial { Arquivo = caminho, Json = (JsonObject)JsonNode.Parse(File.ReadAllText(caminho)) };
            await doc.Garantir();      // o motor valida ao abrir
            await doc.Recarregar();
            return doc;
        }

        // Gerador do motor + cargas uniformes G e Q (kN/m, para baixo) em todas as barras com papel "viga".
        public static Task<DocumentoEspacial> Gerar(JsonObject dados, string titulo, double g, double q) => Criar(async () =>
        {
            await Motor.Post("/api/modelo/novo", new JsonObject { ["nome"] = titulo });
            await Motor.Post("/api/gerar-estrutura", dados);
            if (g == 0 && q == 0) return;
            var m = await Motor.Get("/api/modelo");
            var itens = new JsonArray();
            foreach (var kv in m["barras"].AsObject())
            {
                if ((string)kv.Value["params"]?["papel"] != "viga") continue;
                foreach (var (caso, v) in new[] { ("G1", g), ("Q1", q) })
                    if (v != 0) itens.Add(new JsonObject { ["tipo"] = "carga_distribuida", ["dados"] = new JsonObject { ["caso"] = caso, ["barra"] = kv.Key, ["q"] = new JsonArray(0.0, 0.0, -v) } });
            }
            await Motor.Post("/api/modelo/lote", new JsonObject { ["itens"] = itens });
        }, titulo, null);

        public async Task Editar(string tipo, string id, JsonObject campos)
        {
            await Garantir();
            await Motor.Chamar("PATCH", "/api/modelo/" + tipo + "/" + Url(id), campos);
            await Recarregar();
        }

        // false = pilha vazia
        public async Task<bool> Trocar(bool desfazer)
        {
            await Garantir();
            try { await Motor.Post(desfazer ? "/api/desfazer" : "/api/refazer", new JsonObject()); }
            catch (MotorErro e) when (e.Status == 409) { return false; }
            await Recarregar();
            return true;
        }

        public async Task Calcular()
        {
            await Garantir();
            var c = await Motor.Post("/api/calcular", new JsonObject());
            PiorPorBarra.Clear();
            foreach (var l in c["verificacao"]?.AsArray() ?? new JsonArray())
            {
                string b = (string)l["barra"];
                if (!PiorPorBarra.TryGetValue(b, out var at) || Taxa(l) > Taxa(at)) PiorPorBarra[b] = l;
            }
            var pior = PiorPorBarra.Values.OrderByDescending(Taxa).FirstOrDefault();
            CombDiagrama = (string)pior?["combinacao"] ?? c["combinacoes"]?.AsArray().Select(x => (string)x["nome"]).FirstOrDefault();
            if (CombDiagrama != null && CombDiagrama.Contains("Envolt")) CombDiagrama = null;
            string q = CombDiagrama == null ? "" : "&combinacao=" + Url(CombDiagrama);
            esforcos = (await Motor.Get("/api/resultados?alvo=esforcos" + q)) as JsonObject;
            desl = (await Motor.Get("/api/resultados?alvo=deslocamentos" + q)) as JsonObject;
            Calculo = c;
        }

        public double[] Desl(string no) => desl?[no] is JsonArray a ? a.Take(3).Select(x => (double)x).ToArray() : null;

        public static double Taxa(JsonNode l) => l?["taxa"] == null ? -1 : (double)l["taxa"];

        public string Resumo()
        {
            var j = Json; int nn = j["nos"].AsObject().Count, nb = j["barras"].AsObject().Count;
            string s = "Modelo espacial (motor) — " + Pl.N(nn, "nó", "nós") + " · " + Pl.N(nb, "barra", "barras");
            if (!Calculado) return s + " · F5 calcula (combinações NBR 8681, verificação por norma).";
            int ncomb = Calculo["combinacoes"]?.AsArray().Count ?? 0;
            int reprov = PiorPorBarra.Values.Count(l => l["ok"] != null && !(bool)l["ok"]);
            var p = PiorPorBarra.Values.OrderByDescending(Taxa).FirstOrDefault();
            return s + " · " + Pl.N(ncomb, "combinação", "combinações") + " · " + (reprov == 0 ? "todas as barras passam" : "⚠ " + Pl.N(reprov, "barra não passa", "barras não passam")) +
                   (p != null && Taxa(p) >= 0 ? " · pior: " + (string)p["barra"] + " " + Num.Fmt(Taxa(p) * 100, 0) + "% (" + (string)p["criterio"] + ")" : "") +
                   (CombDiagrama != null ? " · diagramas: " + CombDiagrama : "");
        }

        public string JsonFormatado() => Json.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

        // Entrada neutra da Vista3D. O eixo local y da Vista3D é o z local do motor (mesma regra de solver.eixos_locais),
        // então os diagramas são N, Vz e My (My < 0 no vão de viga com gravidade = desenhado para baixo, lado tracionado).
        // ponytail: estações tratadas como igualmente espaçadas (carga pontual na barra desloca um pouco o desenho).
        public Modelo3D ParaVista()
        {
            var r = new Modelo3D();
            var ids = new Dictionary<string, int>();
            foreach (var kv in Json["nos"].AsObject())
            {
                var n = kv.Value; int id = ids.Count + 1; ids[kv.Key] = id;
                var ap = n["apoio"]?.AsArray().Select(x => x != null && (bool)x).ToArray() ?? new bool[6];
                var dd = desl?[kv.Key]?.AsArray();
                r.Nos.Add(new No3D { Id = id, X = (double)n["x"], Y = (double)n["y"], Z = (double)n["z"], Apoios = ap.Length == 6 ? ap : new bool[6], Origem = Ref("no", kv.Key),
                                     Desl = dd == null ? null : new[] { (double)dd[0], (double)dd[1], (double)dd[2] } });
            }
            int ib = 0;
            foreach (var kv in Json["barras"].AsObject())
            {
                var b = kv.Value;
                if (!ids.TryGetValue((string)b["ni"], out int ni) || !ids.TryGetValue((string)b["nj"], out int nj)) continue;
                var b3 = new Barra3D { Id = ++ib, Ni = ni, Nj = nj, Secao = (string)b["secao"] ?? "", Rot = (double)(b["rot"] ?? 0.0) * 180 / Math.PI, Origem = Ref("barra", kv.Key) };
                var e = esforcos?[kv.Key];
                if (e != null) { b3.N = Vetor(e["N"]); b3.V = Vetor(e["Vz"]); b3.M = Vetor(e["My"]); }
                if (PiorPorBarra.TryGetValue(kv.Key, out var l) && Taxa(l) >= 0) b3.Aprov = Taxa(l);
                r.Barras.Add(b3);
            }
            return r;
        }

        static double[] Vetor(JsonNode n) => n is JsonArray a ? a.Select(x => (double)x).ToArray() : null;   // envoltória vem como {max, min}: sem diagrama
    }

    // ================= janela principal: modo "modelo espacial" =================
    public partial class MainWindow
    {
        DocumentoEspacial Esp => docAtivo >= 0 && docAtivo < docs.Count ? docs[docAtivo].Esp : null;
        RefMotor selEsp;
        bool modoEsp, escolhaEspLigada, espRodando;
        readonly ContentControl painelEsp = new ContentControl();

        static bool EhEspacial(string caminho) => caminho.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || caminho.EndsWith(".ed3", StringComparison.OrdinalIgnoreCase);

        internal async Task AbrirEspacial(Func<Task<DocumentoEspacial>> criar, string oque)
        {
            Bastidor.IsOpen = false;
            StatusMsg.Text = "Motor Python: " + oque + "…";
            var antes = Mouse.OverrideCursor; Mouse.OverrideCursor = Cursors.AppStarting;
            DocumentoEspacial d;
            try { d = await criar(); }
            catch (Exception ex) { Caixa.Erro(this, "Não foi possível abrir o modelo espacial", ex.Message); StatusMsg.Text = "⚠ " + ex.Message; return; }
            finally { Mouse.OverrideCursor = antes; }
            GuardarDoc();
            PrepararDocNovo();
            docs[docAtivo].Esp = d;
            selEsp = null;
            NovoModelo(new Modelo(), d.Arquivo);
            alterado = d.Arquivo == null;
            AtualizarTitulo();
            AplicarModoEspacial();
            Log("Modelo espacial: " + d.Nome + " (" + d.Resumo() + ")");
            foreach (string a in d.Avisos) Log("  aviso: " + a, false);
        }

        internal Task AbrirEspacialArquivo(string caminho) => AbrirEspacial(() => DocumentoEspacial.AbrirArquivo(caminho), "abrindo " + Path.GetFileName(caminho));

        // Chamado ao trocar de documento: liga ou desliga a vista 3D forçada e o painel de propriedades do motor.
        void AplicarModoEspacial()
        {
            var d = Esp;
            if (d == null)
            {
                if (!modoEsp) return;
                modoEsp = false;
                PainelProp.Content = Insp;
                MostrarVista3D(false);
                return;
            }
            modoEsp = true;
            TelaInicial.Visibility = Visibility.Collapsed;
            MostrarVista3D(true);
            vista3D.MostrarLegenda = true;
            if (!escolhaEspLigada)
            {
                escolhaEspLigada = true;
                vista3D.Escolheu += o =>
                {
                    if (Esp == null) return;
                    selEsp = (o as No3D)?.Origem as RefMotor ?? (o as Barra3D)?.Origem as RefMotor;
                    MontarPainelEspacial();
                };
            }
            PainelProp.Content = painelEsp;
            AtualizarEspacial3D(true);
            Aviso.Visibility = Visibility.Collapsed;
        }

        // Atualizar3D redireciona para cá quando o documento ativo é espacial.
        void AtualizarEspacial3D(bool enquadrar)
        {
            var d = Esp;
            if (d == null || vista3D == null) return;
            Diagrama dg = Vista.Diag;
            bool c = d.Calculado;
            vista3D.Diagrama = !c ? Diagrama3D.Nenhum : dg == Diagrama.Normal ? Diagrama3D.N : dg == Diagrama.Cortante ? Diagrama3D.V : dg == Diagrama.Momento ? Diagrama3D.M : Diagrama3D.Nenhum;
            vista3D.MostrarDeformada = c && dg == Diagrama.Deformada;
            vista3D.MostrarAproveitamento = c && dg == Diagrama.Verificacao;
            vista3D.EscalaDeformada = Vista.EscalaDef; vista3D.EscalaDiagrama = Vista.EscalaDiag;
            vista3D.Selecionado = selEsp;
            vista3D.Carregar(d.ParaVista(), enquadrar);
            Vista.Resolvido = c;   // o HUD (seletor de diagrama, escala) segue o cálculo do motor; o Modelo 2D do documento é vazio
            AtualizarLegenda();
            if (selEsp != null && vista3D.Selecionado == null) selEsp = null;
            StatusMsg.Text = d.Resumo();
            MontarPainelEspacial();
        }

        void DepoisDeMudarEspacial(string msg)
        {
            alterado = true;
            AtualizarTitulo();
            AtualizarEspacial3D(false);
            if (msg != null) Log(msg);
        }

        async Task Espacial(Func<DocumentoEspacial, Task> f, string msg)
        {
            var d = Esp;
            if (d == null || espRodando) return;
            espRodando = true;
            try { await f(d); if (Esp == d) DepoisDeMudarEspacial(msg); }
            catch (Exception ex) { StatusMsg.Text = "⚠ Motor: " + ex.Message; Log(StatusMsg.Text); }
            finally { espRodando = false; }
        }

        async Task TrocarEspacial(bool desfazer)
        {
            bool ok = false;
            await Espacial(async d => ok = await d.Trocar(desfazer), null);
            Log(ok ? (desfazer ? "Desfeito." : "Refeito.") : desfazer ? "Nada para desfazer." : "Nada para refazer.");
        }

        internal async Task CalcularEspacial()
        {
            var d = Esp;
            if (d == null || espRodando) return;
            espRodando = true;
            var barra = new ProgressBar { IsIndeterminate = true, Width = 90, Height = 6, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            StatusBotoes.Children.Insert(0, barra);
            StatusMsg.Text = "Motor Python: calculando (combinações NBR 8681 e verificação por norma)…";
            try
            {
                await d.Calcular();
                if (Esp != d) return;
                if (Vista.Diag == Diagrama.Nenhum) MostrarDiagrama(Diagrama.Verificacao);   // passa por Atualizar3D
                else AtualizarEspacial3D(false);
                Log(d.Resumo());
            }
            catch (Exception ex) { StatusMsg.Text = "⚠ Motor: " + ex.Message; Log(StatusMsg.Text); }
            finally { StatusBotoes.Children.Remove(barra); espRodando = false; }
        }

        bool SalvarEspacial(bool como)
        {
            var d = Esp;
            string c = d.Arquivo;
            if (como || c == null)
            {
                var dlg = new SaveFileDialog { Filter = "Modelo do motor (*.json)|*.json", FileName = (c == null ? d.Nome : Path.GetFileNameWithoutExtension(c)) + ".json" };
                if (dlg.ShowDialog(this) != true) return false;
                c = dlg.FileName;
            }
            try
            {
                File.WriteAllText(c, d.JsonFormatado());   // Json = último GET /api/modelo (atualizado depois de cada mudança)
                d.Arquivo = c; arquivo = c; alterado = false;
                AtualizarTitulo();
                Log("Salvo: " + c);
                return true;
            }
            catch (Exception ex) { Caixa.Erro(this, "Não foi possível salvar", ex.Message); return false; }
        }

        // ---------- painel de propriedades (nó, barra ou o modelo) ----------

        void MontarPainelEspacial()
        {
            var d = Esp;
            if (d == null) return;
            var sp = new StackPanel { Margin = new Thickness(12, 10, 12, 12) };
            Action<string, string> cab = (t, s) =>
            {
                sp.Children.Add(new TextBlock { Text = t, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = TemaPlant.BTexto1 });
                sp.Children.Add(new TextBlock { Text = s, FontSize = 11.5, Foreground = TemaPlant.BTexto2, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 10) });
            };
            Action<string> secao = t => sp.Children.Add(new TextBlock { Text = t.ToUpperInvariant(), FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = TemaPlant.BTexto2, Margin = new Thickness(0, 10, 0, 4) });
            Action<string, UIElement> linha = (rot, ed) =>
            {
                var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
                g.ColumnDefinitions.Add(new ColumnDefinition());
                g.Children.Add(new TextBlock { Text = rot, Foreground = TemaPlant.BTexto2, VerticalAlignment = VerticalAlignment.Center });
                Grid.SetColumn(ed, 1); g.Children.Add(ed);
                sp.Children.Add(g);
            };
            Func<string, TextBlock> leitura = s => new TextBlock { Text = s, Foreground = TemaPlant.BTexto1, TextWrapping = TextWrapping.Wrap };
            // campo numérico: Enter ou sair do campo manda o PATCH
            Func<double, Action<double>, TextBox> num = (v, aplicar) =>
            {
                var tb = JanelaEscura.Campo(Num.Fmt(v, 3), 120);
                tb.HorizontalAlignment = HorizontalAlignment.Left;
                string orig = tb.Text;
                Action ok = () =>
                {
                    if (tb.Text == orig) return;
                    if (double.TryParse(tb.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double x)) { orig = tb.Text; aplicar(x); }
                    else tb.Text = orig;
                };
                tb.KeyDown += (s, e) => { if (e.Key == Key.Enter) { ok(); e.Handled = true; } };
                tb.LostKeyboardFocus += (s, e) => ok();
                return tb;
            };
            Func<IEnumerable<string>, string, Action<string>, ComboBox> combo = (itens, atual, aplicar) =>
            {
                var cb = new ComboBox { IsEditable = false };
                foreach (string i in itens) cb.Items.Add(i);
                cb.SelectedItem = atual;
                cb.SelectionChanged += (s, e) => { if (cb.SelectedItem is string x && x != atual) aplicar(x); };
                return cb;
            };

            JsonNode el = selEsp == null ? null : d.Json[selEsp.Tipo == "no" ? "nos" : "barras"]?[selEsp.Id];
            if (el == null)
            {
                selEsp = null;
                var j = d.Json;
                cab((string)j["nome"] is string n && n != "" ? n : d.Nome, "Modelo espacial do motor Python. Clique numa barra ou nó da vista para editar.");
                secao("Modelo");
                foreach (var (r, k) in new[] { ("Nós", "nos"), ("Barras", "barras"), ("Seções", "secoes"), ("Materiais", "materiais"), ("Casos", "casos"), ("Pavimentos", "pavimentos") })
                    linha(r, leitura((j[k] as JsonObject)?.Count.ToString() ?? "0"));
                linha("Casos", leitura(string.Join(", ", (j["casos"] as JsonObject)?.Select(x => x.Key) ?? new string[0])));
                secao("Cálculo");
                sp.Children.Add(leitura(d.Calculado ? d.Resumo() : "Sem cálculo: F5 calcula pelo motor (NBR 8681, 8800, 14762, 6118)."));
            }
            else if (selEsp.Tipo == "no")
            {
                string id = selEsp.Id;
                cab("Nó " + id, "(" + Num.Fmt((double)el["x"], 2) + "; " + Num.Fmt((double)el["y"], 2) + "; " + Num.Fmt((double)el["z"], 2) + ") m");
                secao("Posição (m)");
                foreach (string k in new[] { "x", "y", "z" })
                {
                    string kk = k;
                    linha(k.ToUpperInvariant(), num((double)el[k], v => _ = Espacial(dd => dd.Editar("no", id, new JsonObject { [kk] = v }), "Nó " + id + ": " + kk + " = " + Num.Fmt(v, 3) + " m.")));
                }
                secao("Apoio (travado)");
                var ap = el["apoio"]?.AsArray().Select(x => x != null && (bool)x).ToArray() ?? new bool[6];
                var wp = new WrapPanel();
                string[] gl = { "ux", "uy", "uz", "rx", "ry", "rz" };
                for (int i = 0; i < 6; i++)
                {
                    int k = i;
                    var ck = new CheckBox { Content = gl[i], IsChecked = ap[i], Foreground = TemaPlant.BTexto1, Margin = new Thickness(0, 2, 12, 2) };
                    ck.Click += (s, e) =>
                    {
                        var novo = (bool[])ap.Clone(); novo[k] = ck.IsChecked == true;
                        _ = Espacial(dd => dd.Editar("no", id, new JsonObject { ["apoio"] = new JsonArray(novo.Select(x => (JsonNode)x).ToArray()) }), "Nó " + id + ": apoio " + gl[k] + (novo[k] ? " travado." : " liberado."));
                    };
                    wp.Children.Add(ck);
                }
                sp.Children.Add(wp);
                var dl = d.Desl(id);
                if (dl != null)
                {
                    secao("Deslocamento (" + d.CombDiagrama + ")");
                    linha("ux, uy, uz", leitura(string.Join(" · ", dl.Select(x => Num.Fmt(x * 1000, 2))) + " mm"));
                }
            }
            else
            {
                string id = selEsp.Id;
                cab("Barra " + id + " · " + (string)el["secao"], (string)el["material"] + " · " + (string)el["tipo"] + " · " + (string)el["ni"] + " → " + (string)el["nj"]);
                secao("Barra");
                linha("Seção", combo(d.Json["secoes"].AsObject().Select(x => x.Key), (string)el["secao"], v => _ = Espacial(dd => dd.Editar("barra", id, new JsonObject { ["secao"] = v }), "Barra " + id + ": seção " + v + ".")));
                linha("Material", combo(d.Json["materiais"].AsObject().Select(x => x.Key), (string)el["material"], v => _ = Espacial(dd => dd.Editar("barra", id, new JsonObject { ["material"] = v }), "Barra " + id + ": material " + v + ".")));
                linha("Giro (°)", num((double)(el["rot"] ?? 0.0) * 180 / Math.PI, v => _ = Espacial(dd => dd.Editar("barra", id, new JsonObject { ["rot"] = v * Math.PI / 180 }), "Barra " + id + ": giro " + Num.Fmt(v, 1) + "°.")));
                linha("Papel", leitura((string)el["params"]?["papel"] ?? "—"));
                if (d.PiorPorBarra.TryGetValue(id, out var v0))
                {
                    secao("Verificação (" + (string)v0["norma"] + ")");
                    double t = DocumentoEspacial.Taxa(v0);
                    linha("Taxa", new TextBlock { Text = t < 0 ? "—" : Num.Fmt(t * 100, 0) + "%", FontWeight = FontWeights.SemiBold, Foreground = t > 1 ? TemaPlant.BErro : t > 0.9 ? TemaPlant.BAlerta : TemaPlant.BSucesso });
                    linha("Critério", leitura((string)v0["criterio"] + " · " + (string)v0["item"]));
                    linha("Combinação", leitura((string)v0["combinacao"] ?? "—"));
                }
                else if (d.Calculado) { secao("Verificação"); sp.Children.Add(leitura("Barra não verificada pelo motor.")); }
            }
            painelEsp.Content = new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = TemaPlant.BSup1 };
        }

        // Para as capturas e testes: o documento espacial ativo e um jeito de fechar todos sem perguntar.
        internal DocumentoEspacial EspacialAtivo => Esp;
        internal Task EditarEspacial(string tipo, string id, JsonObject campos) => Espacial(d => d.Editar(tipo, id, campos), "Editado: " + tipo + " " + id + ".");
        internal Task DesfazerEspacial() => TrocarEspacial(true);
        internal void FecharEspaciaisSemPerguntar()
        {
            GuardarDoc();
            docs.RemoveAll(x => x.Esp != null);
            if (docs.Count == 0) docs.Add(new Documento());
            CarregarDoc(0);
        }

        // ---------- gerar, exemplos, tela inicial ----------

        internal void AbrirGerador3D(string tipo = null)
        {
            Bastidor.IsOpen = false;
            var j = new JanelaGerar3D(tipo) { Owner = this };
            if (j.ShowDialog() == true) _ = AbrirEspacial(() => DocumentoEspacial.Gerar(j.Dados, j.Titulo, j.G, j.Q), "gerando " + j.Titulo);
        }

        internal async void AbrirExemplosEspaciais()
        {
            Bastidor.IsOpen = false;
            JsonNode lista;
            try { lista = await Motor.Get("/api/exemplos"); }
            catch (Exception ex) { Caixa.Erro(this, "Motor de cálculo indisponível", ex.Message); return; }
            var jan = new JanelaEscura { Title = "Exemplos do motor", Width = 460, SizeToContent = SizeToContent.Height, Owner = this, ResizeMode = ResizeMode.NoResize };
            var sp = new StackPanel { Margin = new Thickness(16) };
            sp.Children.Add(JanelaEscura.T("Modelos espaciais prontos do motor Python (abrem numa aba nova, em 3D).", 12, JanelaEscura.BFraco));
            foreach (var e in lista.AsArray())
            {
                string nome = (string)e["nome"];
                var b = Cartao((string)e["descricao"], nome, () => { jan.Close(); _ = AbrirEspacial(() => DocumentoEspacial.Exemplo(nome), "abrindo o exemplo " + nome); }, "3d");
                b.Margin = new Thickness(0, 8, 0, 0);
                sp.Children.Add(b);
            }
            jan.Content = sp;
            jan.ShowDialog();
        }

        FrameworkElement LinhaEspacialInicio(bool comp)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, comp ? 6 : 14) };
            sp.Children.Add(new TextBlock { Text = "MODELO ESPACIAL (3D, MOTOR PYTHON)", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = TemaPlant.BTexto2, Margin = new Thickness(0, 0, 0, 6) });
            var wp = new WrapPanel();
            foreach (var (t, ic, a, dica) in new (string, string, Action, string)[]
            {
                ("Edifício 3D", "3d", () => AbrirGerador3D("edificio"), "Gera um edifício de pórticos espaciais (vãos em X e Y, pavimentos) no motor Python."),
                ("Galpão 3D", "3d", () => AbrirGerador3D("galpao"), "Gera um galpão (pórticos, terças, contraventamento) no motor Python."),
                ("Pórtico 3D", "3d", () => AbrirGerador3D("portico_plano"), "Gera um pórtico plano de vários vãos e pavimentos no motor Python (6 graus de liberdade por nó)."),
                ("Exemplos do motor…", "abrir", AbrirExemplosEspaciais, "Abre um dos modelos de exemplo do motor (edifício de concreto, galpão de aço…)."),
                ("Abrir .json / .ed3…", "abrir", AbrirDialogo, "Abre um modelo do motor (.json) ou importa uma obra do CYPE 3D (.ed3)."),
            })
            {
                var b = UiPlaca.Botao(t, ic, a, dica);
                b.Margin = new Thickness(0, 0, 8, 6);
                wp.Children.Add(b);
            }
            sp.Children.Add(wp);
            return sp;
        }
    }

    // Parâmetros do gerador do motor (ESPEC §6.3: edificio, galpao, portico_plano) + cargas nas vigas.
    public class JanelaGerar3D : JanelaEscura
    {
        public JsonObject Dados; public string Titulo; public double G, Q;
        readonly ComboBox cbTipo = new ComboBox { Width = 220 };
        readonly Dictionary<string, TextBox> c = new Dictionary<string, TextBox>();
        readonly Dictionary<string, FrameworkElement> linhas = new Dictionary<string, FrameworkElement>();
        readonly TextBlock erro = T("", 12, BVermelho);
        static readonly string[] Tipos = { "edificio", "galpao", "portico_plano" };
        static readonly string[] NomesTipo = { "Edifício (pórticos espaciais)", "Galpão", "Pórtico plano" };

        public JanelaGerar3D(string tipo)
        {
            Title = "Gerar estrutura 3D (motor)"; Width = 520; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
            var sp = new StackPanel { Margin = new Thickness(18) };
            sp.Children.Add(T("Gera o modelo espacial no motor Python. Listas: valores separados por espaço (ex.: 6 6 5). Seções do catálogo (W310x38.7) ou \"Ret 25x50\" (cm) para concreto; material A-572 GR.50 ou C30.", 11.5, BFraco));
            foreach (string t in NomesTipo) cbTipo.Items.Add(t);
            Linha(sp, "tipo", "Tipo", cbTipo);
            foreach (var (k, r, v) in new[] {
                ("vaos_x", "Vãos em X (m)", "6 6"), ("vaos_y", "Vãos em Y (m)", "5 5"), ("alturas", "Alturas (m)", "3,5 3,5 3,5"),
                ("vao", "Vão (m)", "20"), ("altura_pilar", "Altura do pilar (m)", "6"), ("altura_cumeeira", "Cumeeira (m)", "8"),
                ("n_modulos", "Módulos", "5"), ("espacamento", "Espaçamento (m)", "6"), ("terca", "Terça", "Ue 250x85x25x3,00"),
                ("material", "Material", "A-572 GR.50"), ("pilar", "Pilar", "W310x38.7"), ("viga", "Viga", "W360x51"), ("apoio", "Apoio (engaste/rotula)", "engaste"),
                ("g", "G nas vigas (kN/m)", "12"), ("q", "Q nas vigas (kN/m)", "8") })
            {
                c[k] = Campo(v, 220);
                Linha(sp, k, r, c[k]);
            }
            sp.Children.Add(erro);
            var bts = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            bts.Children.Add(B("Cancelar", Close));
            bts.Children.Add(B("Gerar", Confirmar, true));
            sp.Children.Add(bts);
            Content = sp;
            cbTipo.SelectionChanged += (s, e) => Mostrar();
            cbTipo.SelectedIndex = Math.Max(0, Array.IndexOf(Tipos, tipo ?? "edificio"));
        }

        void Linha(StackPanel sp, string k, string rot, FrameworkElement ed)
        {
            var g = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.Children.Add(T(rot, 12, BFraco));
            ed.HorizontalAlignment = HorizontalAlignment.Left;
            Grid.SetColumn(ed, 1); g.Children.Add(ed);
            linhas[k] = g; sp.Children.Add(g);
        }

        string TipoAtual => Tipos[Math.Max(0, cbTipo.SelectedIndex)];

        void Mostrar()
        {
            string t = TipoAtual;
            var so = new Dictionary<string, string[]> { ["edificio"] = new[] { "vaos_x", "vaos_y", "alturas" }, ["portico_plano"] = new[] { "vaos_x", "alturas" },
                                                        ["galpao"] = new[] { "vao", "altura_pilar", "altura_cumeeira", "n_modulos", "espacamento", "terca" } };
            var todos = so.Values.SelectMany(x => x).Distinct();
            foreach (string k in todos) linhas[k].Visibility = so[t].Contains(k) ? Visibility.Visible : Visibility.Collapsed;
            if (t == "galpao" && c["pilar"].Text == "W310x38.7") { c["pilar"].Text = "W360x51"; c["viga"].Text = "HP250x62"; c["g"].Text = "1,5"; c["q"].Text = "1,5"; }
        }

        static double D(string s) => double.Parse(s.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture);
        static JsonArray Lista(string s) => new JsonArray(s.Split(new[] { ' ', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries).Select(x => (JsonNode)D(x)).ToArray());

        // "Ret 25x50" (cm) -> seção retangular; o resto é nome do catálogo do motor
        static JsonNode Secao(string s)
        {
            s = s.Trim();
            if (s.StartsWith("Ret", StringComparison.OrdinalIgnoreCase))
            {
                var p = s.Substring(3).Split(new[] { 'x', 'X', '×' }, StringSplitOptions.RemoveEmptyEntries);
                return new JsonObject { ["forma"] = "retangular", ["b"] = D(p[0]) / 100, ["h"] = D(p[1]) / 100 };
            }
            return s;
        }

        void Confirmar() { if (Montar()) DialogResult = true; }

        // Lê os campos para Dados/Titulo/G/Q. false = campo inválido (mensagem na janela).
        internal bool Montar()
        {
            try
            {
                string t = TipoAtual;
                string mat = c["material"].Text.Trim();
                var d = new JsonObject
                {
                    ["tipo"] = t,
                    ["material"] = mat.Length > 1 && (mat[0] == 'C' || mat[0] == 'c') && char.IsDigit(mat[1]) ? new JsonObject { ["concreto"] = D(mat.Substring(1)) } : (JsonNode)mat,
                    ["apoio"] = c["apoio"].Text.Trim().ToLowerInvariant().Replace("ó", "o"),
                };
                var sec = new JsonObject { ["pilar"] = Secao(c["pilar"].Text), ["viga"] = Secao(c["viga"].Text) };
                if (t == "galpao")
                {
                    foreach (string k in new[] { "vao", "altura_pilar", "altura_cumeeira", "espacamento" }) d[k] = D(c[k].Text);
                    d["n_modulos"] = int.Parse(c["n_modulos"].Text.Trim());
                    if (c["terca"].Text.Trim() != "") sec["terca"] = new JsonObject { ["perfil"] = c["terca"].Text.Trim() };
                    // sem contraventamento o galpão é hipostático na direção longitudinal (igual ao exemplo galpao_aco do motor)
                    sec["contraventamento"] = new JsonObject { ["forma"] = "tubo_circular", ["d"] = 0.2191, ["t"] = 0.00635 };
                    d["contraventamento"] = true;
                }
                else
                {
                    d["vaos_x"] = Lista(c["vaos_x"].Text); d["alturas"] = Lista(c["alturas"].Text);
                    if (t == "edificio") d["vaos_y"] = Lista(c["vaos_y"].Text);
                }
                d["secoes"] = sec;
                Dados = d; G = D(c["g"].Text); Q = D(c["q"].Text);
                Titulo = t == "galpao" ? "Galpão " + c["vao"].Text + " m × " + c["n_modulos"].Text + " módulos"
                       : (t == "edificio" ? "Edifício " : "Pórtico ") + ((JsonArray)d["vaos_x"]).Count + (t == "edificio" ? "×" + ((JsonArray)d["vaos_y"]).Count : "") + " vãos, " + Pl.N(((JsonArray)d["alturas"]).Count, "pavimento", "pavimentos");
                return true;
            }
            catch (FormatException) { erro.Text = "Confira os números (use espaço entre os valores das listas)."; }
            catch (IndexOutOfRangeException) { erro.Text = "Seção retangular: \"Ret b x h\" em cm (ex.: Ret 25x50)."; }
            return false;
        }
    }
}
