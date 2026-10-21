// Testes de fluxo ponta a ponta (docs/v3/BUGS-E2E.md): Estructto.exe --testes-fluxo <pasta> [A,B,C,D,E]
// Dirige a MainWindow de verdade (fora da tela, como as capturas) por fluxos de um engenheiro: galpão do zero (A),
// edifício de 4 pavimentos (B), arquivos legados e CYPE (C), edição (D) e robustez (E). Cada passo importante grava um PNG
// (fluxo-<X>-*.png) e cada conferência vai para fluxo-<X>.txt na hora (um travamento ainda deixa a evidência).
// Falha conhecida = "[CONHECIDO #E2E-nn]" (bug registrado em docs/v3/BUGS-E2E.md): conta à parte e não reprova a suíte.
// Resumo em testes-fluxo.txt ("TODOS PASSARAM" = nenhuma falha nova). Precisa do motor (ESTRUCTTO_MOTOR ou motor\ empacotado).
// Guarda e devolve %AppData%\NovoFtool (recentes, recuperação, empresa): o teste não deixa rastro na configuração do usuário.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using Portico;

namespace NovoFtool
{
    public static partial class Capturas
    {
        // ================================================================ entrada
        static string[] fluxosPedidos;

        // App.xaml.cs: a janela é criada aqui (sem StartupUri) e roda os fluxos quando carrega.
        internal static void IniciarFluxos(string pasta, string fluxos)
        {
            pastaSaida = Path.GetFullPath(pasta);
            Directory.CreateDirectory(pastaSaida);
            foreach (var f in Directory.GetFiles(pastaSaida, "fluxo-*")) File.Delete(f);
            fluxosPedidos = (string.IsNullOrWhiteSpace(fluxos) ? "A,B,C,D,E" : fluxos).ToUpperInvariant().Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            GuardarConfigUsuario();
            var w = new MainWindow();
            Ativo = true;   // modo de teste: sem modais de recuperação, janelas sem animação (como --capturas)
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Left = -10000; w.Top = -10000; w.Width = 1600; w.Height = 900;
            w.ShowActivated = false; w.ShowInTaskbar = false;
            w.Loaded += (s, e) => w.Dispatcher.BeginInvoke(new Action(() => RodarFluxos(w)), DispatcherPriority.ApplicationIdle);
            Application.Current.MainWindow = w;
            w.Show();
        }

        sealed class Rel
        {
            public readonly string Id; readonly string arq;
            public int Ok, Falhas, Conhecidos;
            public Rel(string id, string titulo) { Id = id; arq = Path.Combine(pastaSaida, "fluxo-" + id + ".txt"); File.WriteAllText(arq, "== Fluxo " + id + ": " + titulo + " ==" + Environment.NewLine, new UTF8Encoding(false)); }
            public void L(string s) => File.AppendAllText(arq, s + Environment.NewLine, new UTF8Encoding(false));
            public bool Checar(bool c, string m) { if (c) Ok++; else Falhas++; L((c ? "ok      " : "FALHA   ") + m); return c; }
            // bug registrado: a falha não reprova; se passar, avisa para tirar a marca
            public bool Conhecido(string id, bool c, string m)
            {
                if (c) { Ok++; L("ok      " + m + "  (era [CONHECIDO #" + id + "]: conferir e tirar a marca)"); }
                else { Conhecidos++; L("CONHEC. [CONHECIDO #" + id + "] " + m); }
                return c;
            }
            public void Exigir(bool c, string m) { if (!Checar(c, m)) throw new InvalidOperationException("passo obrigatório falhou: " + m); }
        }

        static readonly List<Rel> relatos = new List<Rel>();
        static readonly List<string> caixasVistas = new List<string>();   // texto das Caixa (mensagens modais) que apareceram
        static string respostaCaixa;                                      // botão a clicar na próxima Caixa (null = fechar)

        static void RodarFluxos(MainWindow w)
        {
            var sw = Stopwatch.StartNew();
            VigiarCaixas();
            {   // como as capturas: motor do worktree se ninguém disse outro
                string dev = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "..", "motor"));
                if (Environment.GetEnvironmentVariable("ESTRUCTTO_MOTOR") == null && Directory.Exists(dev)) Environment.SetEnvironmentVariable("ESTRUCTTO_MOTOR", dev);
            }
            string motor = File.Exists(Path.Combine(AppContext.BaseDirectory, "motor", "estructto-motor.exe")) ? "empacotado (motor\\estructto-motor.exe)" : "fonte (ESTRUCTTO_MOTOR = " + Environment.GetEnvironmentVariable("ESTRUCTTO_MOTOR") + ")";
            var fluxos = new (string Id, string Titulo, Action<MainWindow, Rel> Corpo)[]
            {
                ("A", "galpão do zero", FluxoGalpao), ("B", "edifício de 4 pavimentos", FluxoEdificio), ("C", "arquivos legados e CYPE", FluxoLegado),
                ("D", "edição", FluxoEdicao), ("E", "robustez", FluxoRobustez),
            };
            try
            {
                foreach (var f in fluxos.Where(x => fluxosPedidos.Contains(x.Id)))
                {
                    var r = new Rel(f.Id, f.Titulo); relatos.Add(r);
                    var t0 = Stopwatch.StartNew();
                    try { f.Corpo(w, r); }
                    catch (Exception ex) { r.Checar(false, "fluxo interrompido: " + ex.GetType().Name + ": " + ex.Message + " | status: " + w.StatusMsg.Text); r.L(ex.ToString()); }
                    finally
                    {
                        respostaCaixa = null;
                        foreach (Window x in Application.Current.Windows.Cast<Window>().Where(x => x != w).ToList()) { try { x.Close(); } catch { } }
                        try { w.FecharEspaciaisSemPerguntar(); EsperarIdle(); } catch (Exception ex) { r.L("fechar: " + ex.Message); }
                        r.L("tempo: " + (t0.ElapsedMilliseconds / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + " s · " + r.Ok + " ok, " + r.Falhas + " falhas, " + r.Conhecidos + " conhecidas");
                    }
                }
            }
            finally
            {
                var sb = new StringBuilder();
                sb.AppendLine("Testes de fluxo do Estructto (--testes-fluxo) · motor " + motor + " · " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
                foreach (var r in relatos) sb.AppendLine("Fluxo " + r.Id + ": " + r.Ok + " ok, " + r.Falhas + " falhas, " + r.Conhecidos + " conhecidas [CONHECIDO]");
                int ok = relatos.Sum(r => r.Ok), fa = relatos.Sum(r => r.Falhas), co = relatos.Sum(r => r.Conhecidos);
                sb.AppendLine("Total: " + (ok + fa + co) + " conferências · " + ok + " ok · " + fa + " falhas · " + co + " conhecidas · " + (sw.ElapsedMilliseconds / 1000.0).ToString("0", CultureInfo.InvariantCulture) + " s");
                if (caixasVistas.Count > 0) sb.AppendLine("Mensagens modais (Caixa) que apareceram: " + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", caixasVistas));
                sb.AppendLine(fa == 0 ? "TODOS PASSARAM" : fa + " FALHA(S)");
                File.WriteAllText(Path.Combine(pastaSaida, "testes-fluxo.txt"), sb.ToString(), new UTF8Encoding(false));
                DevolverConfigUsuario();
                w.alterado = false;
                Environment.Exit(fa == 0 ? 0 : 1);
            }
        }

        // ================================================================ configuração do usuário (%AppData%\NovoFtool)
        static string cfgReal, cfgCopia;
        static void GuardarConfigUsuario()
        {
            cfgReal = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NovoFtool");
            cfgCopia = Path.Combine(Path.GetTempPath(), "estructto-fluxo-cfg-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(cfgCopia);
            if (Directory.Exists(cfgReal))
                foreach (var f in Directory.GetFiles(cfgReal, "*", SearchOption.AllDirectories))
                {
                    string dst = Path.Combine(cfgCopia, Path.GetRelativePath(cfgReal, f));
                    Directory.CreateDirectory(Path.GetDirectoryName(dst)); File.Copy(f, dst);
                }
        }
        static void DevolverConfigUsuario()
        {
            try
            {
                if (!Directory.Exists(cfgReal)) return;
                var orig = new HashSet<string>(Directory.GetFiles(cfgCopia, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(cfgCopia, f)), StringComparer.OrdinalIgnoreCase);
                foreach (var f in Directory.GetFiles(cfgReal, "*", SearchOption.AllDirectories)) if (!orig.Contains(Path.GetRelativePath(cfgReal, f))) File.Delete(f);
                foreach (var rel in orig) { string dst = Path.Combine(cfgReal, rel); Directory.CreateDirectory(Path.GetDirectoryName(dst)); File.Copy(Path.Combine(cfgCopia, rel), dst, true); }
                Directory.Delete(cfgCopia, true);
            }
            catch { }
        }

        // ================================================================ utilitários
        static string Nf(double v, int c = 3) => v.ToString("0." + new string('#', c), CultureInfo.InvariantCulture);

        // Caixa (mensagem modal): anota o texto, grava o PNG e responde (botão respostaCaixa ou fechar) — senão a suíte travaria.
        static void VigiarCaixas()
        {
            var vistas = new HashSet<Window>();
            var t = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(150) };
            t.Tick += (s, e) =>
            {
                foreach (var c in Application.Current.Windows.OfType<Caixa>().Where(x => x.IsVisible && !vistas.Contains(x)).ToList())
                {
                    vistas.Add(c);
                    string txt = Textos(c);
                    caixasVistas.Add(txt.Replace(Environment.NewLine, " | "));
                    try { c.Left = -10000; c.Top = -10000; SalvarPngJanela(c, "fluxo-caixa-" + caixasVistas.Count + ".png"); } catch { }
                    var b = respostaCaixa == null ? null : Filhos<Button>(c).FirstOrDefault(x => (x.Content as string) == respostaCaixa);
                    respostaCaixa = null;
                    if (b != null) Clicar(b); else c.Close();
                }
            };
            t.Start();
        }

        static void SalvarPngJanela(Window j, string nome)
        {
            j.UpdateLayout();
            int lw = Math.Max(200, (int)j.ActualWidth), lh = Math.Max(120, (int)j.ActualHeight);
            var rtb = new RenderTargetBitmap(lw, lh, 96, 96, PixelFormats.Pbgra32);
            var fundo = new DrawingVisual();
            using (var dc = fundo.RenderOpen()) dc.DrawRectangle(j.Background ?? Brushes.Black, null, new Rect(0, 0, lw, lh));
            rtb.Render(fundo); rtb.Render(j);
            SalvarPng(rtb, Path.Combine(pastaSaida, nome));
        }

        static IEnumerable<T> Filhos<T>(DependencyObject raiz) where T : DependencyObject
        {
            if (raiz == null) yield break;
            var vistos = new HashSet<DependencyObject>();
            var pilha = new Stack<DependencyObject>(); pilha.Push(raiz);
            while (pilha.Count > 0)
            {
                var o = pilha.Pop();
                if (!vistos.Add(o)) continue;
                if (o is T t) yield return t;
                int n = o is Visual || o is Visual3D ? VisualTreeHelper.GetChildrenCount(o) : 0;
                for (int i = n - 1; i >= 0; i--) pilha.Push(VisualTreeHelper.GetChild(o, i));
                foreach (var l in LogicalTreeHelper.GetChildren(o).OfType<DependencyObject>()) pilha.Push(l);
            }
        }

        static string Textos(DependencyObject raiz) => string.Join(Environment.NewLine, Filhos<TextBlock>(raiz).Select(t => t.Text).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct());
        static Button BotaoTexto(DependencyObject raiz, string texto) => Filhos<Button>(raiz).FirstOrDefault(b => (b.Content as string) == texto);
        static void Clicar(Button b) { b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, b)); EsperarIdle(); }

        static readonly BindingFlags Todos = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        static T Priv<T>(object o, string nome) { var t = o as Type ?? o.GetType(); var f = t.GetField(nome, Todos); return (T)f.GetValue(o is Type ? null : o); }
        static void PrivSet(object o, string nome, object v) { var t = o as Type ?? o.GetType(); t.GetField(nome, Todos).SetValue(o is Type ? null : o, v); }
        static object PrivCall(object o, string nome, params object[] a) { var t = o as Type ?? o.GetType(); return t.GetMethod(nome, Todos).Invoke(o is Type ? null : o, a); }

        // espera a condição bombeando a fila da interface (sem espera fixa)
        static bool AteCond(Func<bool> cond, double seg = 60, MainWindow w = null)
        {
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < seg)
            {
                bool ok; try { ok = cond(); } catch { ok = false; }
                if (ok && (w == null || !w.OcupadoEsp)) { EsperarIdle(); return true; }
                EsperarIdle(); System.Threading.Thread.Sleep(15);
            }
            return false;
        }
        static async Task AteAsync(Func<bool> cond, double seg = 60)
        {
            var sw = Stopwatch.StartNew();
            while (!cond() && sw.Elapsed.TotalSeconds < seg) await Task.Delay(20);
        }

        static void Foto(MainWindow w, string nome, Action prep = null)
        {
            Action p = () => { TemaPlant.AjustarFaixa(w, TemaPlant.Selo(24)); prep?.Invoke(); };
            RenderizarJanela(w, 1600, 900, "fluxo-" + nome + ".png", p);
            w.Width = 1600; w.Height = 900; w.UpdateLayout();
        }
        static void FotoDialogo(Window j, string nome) { try { CapturarDialogo(j, "fluxo-" + nome + ".png"); } catch (Exception ex) { File.AppendAllText(Path.Combine(pastaSaida, "fluxo-erros-png.txt"), nome + ": " + ex.Message + Environment.NewLine); } }

        static JsonObject J(MainWindow w) => w.EspacialAtivo?.Json ?? throw new InvalidOperationException("nenhum documento espacial ativo");
        static int NNos(MainWindow w) => J(w)["nos"].AsObject().Count;
        static int NBarras(MainWindow w) => J(w)["barras"].AsObject().Count;
        static Point3D Pos(JsonObject m, string no) { var n = m["nos"][no]; return new Point3D((double)n["x"], (double)n["y"], (double)n["z"]); }
        static string NoEm(JsonObject m, double x, double y, double z, double tol = 1e-4) =>
            m["nos"].AsObject().FirstOrDefault(kv => Math.Abs((double)kv.Value["x"] - x) < tol && Math.Abs((double)kv.Value["y"] - y) < tol && Math.Abs((double)kv.Value["z"] - z) < tol).Key;
        static string BarraEntre(JsonObject m, string a, string b) =>
            m["barras"].AsObject().FirstOrDefault(kv => ((string)kv.Value["ni"] == a && (string)kv.Value["nj"] == b) || ((string)kv.Value["ni"] == b && (string)kv.Value["nj"] == a)).Key;
        // barras repetidas (mesmo par de nós) e nós soltos (sem barra)
        static int Duplicadas(JsonObject m) => m["barras"].AsObject().Select(kv => string.Join("|", new[] { (string)kv.Value["ni"], (string)kv.Value["nj"] }.OrderBy(x => x, StringComparer.Ordinal))).GroupBy(x => x).Count(g => g.Count() > 1);
        static int Soltos(JsonObject m) { var usados = new HashSet<string>(m["barras"].AsObject().SelectMany(kv => new[] { (string)kv.Value["ni"], (string)kv.Value["nj"] })); return m["nos"].AsObject().Count(kv => !usados.Contains(kv.Key)); }
        static string PerfilCatalogo(string filtro, params string[] preferidos)
        {
            var nomes = Esperar(Motor.Get("/api/catalogo?filtro=" + Uri.EscapeDataString(filtro))).AsArray().Select(x => (string)x["nome"]).ToList();
            return preferidos.FirstOrDefault(nomes.Contains) ?? nomes.FirstOrDefault() ?? throw new InvalidOperationException("catálogo sem " + filtro);
        }
        static double TaxaMax(DocumentoEspacial d) => d.PiorPorBarra.Values.Select(DocumentoEspacial.Taxa).DefaultIfEmpty(-1).Max();
        static bool Rel9(double a, double b) => Math.Abs(a - b) <= 1e-9 * Math.Max(1, Math.Max(Math.Abs(a), Math.Abs(b)));

        static void Comando(MainWindow w, string c) { w.ExecutarComando(c); EsperarIdle(); }

        // janela do motor (Vento, Sismo...) sobre o documento ativo, como JanelaMotorEsp, mas não modal (a suíte dirige a janela)
        static bool ComJanelaMotor(MainWindow w, Window jan, Func<Window, Task> dirigir)
        {
            var d = w.EspacialAtivo;
            bool mudou = Esperar(d.ComJanela(async () =>
            {
                PrepararDialogoForaDaTela(jan); jan.Owner = w; jan.Show();
                try { await dirigir(jan); } finally { jan.Close(); }
            }));
            if (mudou) PrivCall(w, "DepoisDeMudarEspacial", "teste: janela do motor");
            EsperarIdle();
            return mudou;
        }

        // relatório do motor (WebView2) em PDF; devolve o HTML gerado
        static string RelatorioPdf(MainWindow w, Rel r, string nome)
        {
            var anexo = Esperar(w.AnexoPlacasEsp());
            var d = w.EspacialAtivo;
            var jr = RelatorioMotor.Abrir(w, d.Arquivo != null ? Path.GetFileNameWithoutExtension(d.Arquivo) : d.Nome, anexo);
            jr.Left = -10000; jr.Top = 0;
            string pdf = Path.Combine(pastaSaida, "fluxo-" + nome + ".pdf");
            try
            {
                Esperar(jr.Pronto);
                r.Exigir(jr.Pronto.IsCompleted, "relatório do motor carregou no WebView2");
                try { Esperar(jr.SalvarPng(Path.Combine(pastaSaida, "fluxo-" + nome + ".png"))); } catch (Exception ex) { r.L("png do relatório: " + ex.Message); }
                Esperar(jr.GravarPdf(pdf));
            }
            finally { jr.Close(); }
            var fi = new FileInfo(pdf);
            bool ehPdf = fi.Exists && fi.Length > 20000 && Encoding.ASCII.GetString(File.ReadAllBytes(pdf), 0, 4) == "%PDF";
            r.Checar(ehPdf, "relatório PDF gravado: " + pdf + " (" + (fi.Exists ? fi.Length / 1024 + " kB" : "não existe") + ")");
            string html = File.ReadAllText(Path.Combine(Motor.Pasta, "relatorio-estructto.html"));
            r.Checar(html.Contains("engenheiro") || html.Contains("ART"), "relatório traz o aviso de responsabilidade (engenheiro/ART)");
            return html;
        }

        // ================================================================ A) galpão do zero
        // Grade 4 × 6 m (X) × 12 m (Y), 2 níveis (0 e 6 m) pelo novo projeto; pilares e tesouras de alma cheia desenhados (Pilar
        // com clique nos cruzamentos, Barra com coordenadas digitadas e snaps); terças, X nos vãos extremos e treliças pela
        // galeria; G, Q, peso próprio e vento NBR 6123; F5; verificação; ligações agrupadas, pior placa de base e ligação
        // pilar-viga (chapa de topo); DWG/STEP; PDF; salvar, fechar, reabrir e comparar.
        static void FluxoGalpao(MainWindow w, Rel r)
        {
            string pasta = Path.Combine(pastaSaida, "fluxo-A-projeto");
            if (Directory.Exists(pasta)) Directory.Delete(pasta, true);
            double[] xs = { 0, 6, 12, 18, 24 }, ys = { 0, 12 };
            const double H = 6, Hc = 7.5;

            // --- novo projeto (API do assistente): "Vazio com grade"
            var mi = ModeloInicial.Por("vazio");
            var g = new GradeNiveis { X = GradeNiveis.Espacamentos("4x6"), Y = GradeNiveis.Espacamentos("12"), Z = GradeNiveis.Cotas("6") };
            var dados = new NovoProjetoDados { Modelo = mi, Grade = g, Nome = "Galpao E2E", Pasta = pasta, Cliente = "Cliente E2E", Engenheiro = "Eng. E2E", Empresa = "PROSSEN E2E",
                                               Material = mi.Material, Pilar = mi.Pilar, Viga = mi.Viga, Normas = new List<string> { "NBR 8800", "NBR 6123" } };
            r.Exigir(Esperar(Inicio.Acoes.Novo(w, dados)), "novo projeto criado pelo assistente (Inicio.Acoes.Novo)");
            var d = w.EspacialAtivo;
            r.Exigir(d != null && File.Exists(dados.Arquivo), "documento aberto e gravado em " + dados.Arquivo);
            var v = w.vista3D;
            r.Checar(Niveis.Lista(d.Json).Count == 2, "2 níveis (Térreo e 6 m): " + string.Join(", ", Niveis.Lista(d.Json).Select(n => n.Nome + " " + Nf(n.Z))));
            r.Checar(v.PlanoAtual.Normal == 'Z' && Math.Abs(v.PlanoAtual.Valor - H) < 1e-9, "plano de trabalho no nível de 6 m (" + v.PlanoAtual.Nome + ")");
            r.Conhecido("E2E-01", v.EixosX.Length == 5 && v.EixosY.Length == 2, "a grade do novo projeto aparece na vista (eixos X " + v.EixosX.Length + ", filas Y " + v.EixosY.Length + "; esperado 5 e 2)");
            if (v.EixosX.Length != 5)
            {   // contorno do usuário: GR com as mesmas posições
                Esperar(w.AplicarGrade(xs, ys, new[] { 0.0, H }));
                r.Checar(v.EixosX.Length == 5 && v.EixosY.Length == 2, "GR: grade aplicada à mão (5 eixos, 2 filas)");
            }
            Foto(w, "A-01-novo-projeto", () => v.MostrarVista(VistaPadrao.Topo, false));

            // --- pilares: ferramenta Pilar, clique nos cruzamentos da grade no nível de 6 m (descem ao térreo, apoio engastado)
            string pPilar = PerfilCatalogo("W310", "W310x38.7"), pViga = pPilar, pBeiral = PerfilCatalogo("W150", "W150x13"), pTerca = PerfilCatalogo("Ue 200", "Ue 200x75x25x2,65", "Ue 200x75x25x3,00");
            r.L("perfis: pilar/viga " + pPilar + " · beiral " + pBeiral + " · terça " + pTerca);
            Esperar(w.UsarSecao3D(pPilar));
            w.ApoioBase3D = true;
            w.UsarFerramenta3D(Ferramenta3D.Pilar);
            v.MostrarVista(VistaPadrao.Topo, false); v.Enquadrar(false); v.ZoomFator(0.7); EsperarIdle();
            Point T(double x, double y, double z) => v.Tela(new Point3D(x, y, z));
            var snaps = new List<string>();
            foreach (double y in ys) foreach (double x in xs)
            {
                v.MoverTela(T(x, y, H)); snaps.Add(v.TipoAtracao ?? "—");
                Esperar(v.ClicarTela(T(x, y, H)));
                AteCond(() => true, 5, w);
            }
            r.L("atração nos cliques do Pilar: " + string.Join(", ", snaps.Distinct()));
            w.ApoioBase3D = false;
            v.Tecla(Key.Escape);
            var m = J(w);
            bool pilaresOk = xs.All(x => ys.All(y => NoEm(m, x, y, 0) is string a && NoEm(m, x, y, H) is string b && BarraEntre(m, a, b) != null));
            r.Exigir(pilaresOk && NBarras(w) == 10, "10 pilares de z = 0 a 6 m nos cruzamentos (" + NBarras(w) + " barras, " + NNos(w) + " nós)");
            r.Checar(m["nos"].AsObject().Count(kv => Math.Abs((double)kv.Value["z"]) < 1e-9 && kv.Value["apoio"] is JsonArray ap && ap.All(x => (bool)x)) == 10, "Apoio na base: os 10 pés engastados");
            Foto(w, "A-02-pilares", () => v.MostrarVista(VistaPadrao.Iso, false));

            // --- vigas de cobertura (2 águas) com coordenadas digitadas na linha de comando: #x,y,z absoluto e @ relativo
            w.MostrarVista3D(true);
            foreach (double x in xs)
            {
                int nb0 = NBarras(w);
                Comando(w, "L");
                Comando(w, x.ToString(CultureInfo.InvariantCulture) + ",0,6");
                Comando(w, "@0,6,1.5");
                AteCond(() => NBarras(w) == nb0 + 1, 20, w);
                Comando(w, "@0,6,-1.5");
                AteCond(() => NBarras(w) == nb0 + 2, 20, w);
                v.Tecla(Key.Escape); v.Tecla(Key.Escape);
            }
            m = J(w);
            bool vigasOk = xs.All(x => NoEm(m, x, 6, Hc) is string c && BarraEntre(m, NoEm(m, x, 0, H), c) != null && BarraEntre(m, c, NoEm(m, x, 12, H)) != null);
            r.Exigir(vigasOk && NBarras(w) == 20 && NNos(w) == 25, "vigas de cobertura digitadas ligadas aos topos dos pilares (" + NBarras(w) + " barras, " + NNos(w) + " nós; esperado 20 e 25)");

            // --- vigas de beiral com a Barra e atração nos nós (cliques) no plano de 6 m
            Esperar(w.UsarSecao3D(pBeiral));
            w.UsarFerramenta3D(Ferramenta3D.Barra);
            v.MostrarVista(VistaPadrao.Topo, false); EsperarIdle();
            var atr = new List<string>();
            foreach (double y in ys)
            {
                foreach (double x in xs) { v.MoverTela(T(x, y, H) + new Vector(3, -2)); atr.Add(v.TipoAtracao ?? "—"); Esperar(v.ClicarTela(T(x, y, H) + new Vector(3, -2))); AteCond(() => true, 5, w); }
                v.Tecla(Key.Escape);
            }
            v.Tecla(Key.Escape);
            r.L("atração perto dos topos (3 px ao lado): " + string.Join(", ", atr.Distinct()));
            m = J(w);
            r.Checar(NBarras(w) == 28 && NNos(w) == 25, "8 vigas de beiral pela atração nos nós, sem nó novo (" + NBarras(w) + " barras, " + NNos(w) + " nós)");
            r.Checar(Duplicadas(m) == 0 && Soltos(m) == 0, "sem barras repetidas nem nós soltos");
            Foto(w, "A-03-porticos", () => v.MostrarVista(VistaPadrao.Iso, false));

            // --- terças pela galeria (uma água por vez): seleção das 5 vigas da água e "Terças e tirantes"
            string Agua(double x, bool esq) { var mm = J(w); return BarraEntre(mm, NoEm(mm, x, esq ? 0 : 12, H), NoEm(mm, x, 6, Hc)); }
            foreach (bool esq in new[] { true, false })
            {
                var agua = xs.Select(x => Agua(x, esq)).ToList();
                r.Exigir(agua.All(x => x != null), "água " + (esq ? "esquerda" : "direita") + ": 5 vigas encontradas");
                v.Selecionar(null, agua); EsperarIdle();
                var jt = w.AbrirTrelicas("tercas"); PrepararDialogoForaDaTela(jt); EsperarIdle();
                var campos = Priv<Dictionary<string, Control>>(jt, "campos");
                ((TextBox)campos["espacamento"]).Text = "1,5"; ((TextBox)campos["tirantes"]).Text = "0"; ((TextBox)campos["s_terca"]).Text = pTerca;
                Esperar(jt.Atualizar());
                r.Checar(jt.UltimaPrevia != null, "prévia das terças (" + jt.Status + ")");
                if (esq) FotoDialogo(jt, "A-04-galeria-tercas");
                int nb0 = NBarras(w);
                r.Checar(Esperar2(jt.Inserir()), "terças inseridas na água " + (esq ? "esquerda" : "direita") + " (" + (NBarras(w) - nb0) + " barras novas)");
                jt.Close(); v.LimparSelecao();
            }
            m = J(w);
            var tercas = m["barras"].AsObject().Where(kv => (string)kv.Value["secao"] == pTerca).ToList();
            bool tercasOk = tercas.Count > 0 && tercas.All(kv => { var a = Pos(m, (string)kv.Value["ni"]); var b = Pos(m, (string)kv.Value["nj"]); return Math.Abs(a.Y - b.Y) < 1e-6 && Math.Abs(a.Z - b.Z) < 1e-6 && Math.Abs(Math.Abs(a.X - b.X) - 6) < 1e-6; });
            r.Checar(tercasOk, tercas.Count + " terças de 6 m paralelas a X, apoiadas nas vigas");
            r.Checar(Duplicadas(m) == 0 && Soltos(m) == 0, "terças: sem barras repetidas nem nós soltos (" + NBarras(w) + " barras)");

            // --- contraventamento em X nos vãos extremos das duas fachadas (2 pilares selecionados → X)
            int nX0 = NBarras(w);
            foreach (double y in ys) foreach (var (x1, x2) in new[] { (0.0, 6.0), (18.0, 24.0) })
            {
                var mm = J(w);
                string c1 = BarraEntre(mm, NoEm(mm, x1, y, 0), NoEm(mm, x1, y, H)), c2 = BarraEntre(mm, NoEm(mm, x2, y, 0), NoEm(mm, x2, y, H));
                v.Selecionar(null, new[] { c1, c2 }); EsperarIdle();
                var jx = w.AbrirTrelicas("cx"); PrepararDialogoForaDaTela(jx); EsperarIdle();
                Esperar(jx.Atualizar());
                if (y == 0 && x1 == 0) FotoDialogo(jx, "A-05-galeria-contravento");
                r.Checar(Esperar2(jx.Inserir()), "X entre os pilares " + c1 + " e " + c2 + " (y = " + Nf(y) + ")");
                jx.Close(); v.LimparSelecao();
            }
            r.Checar(NBarras(w) == nX0 + 8, "4 X = 8 diagonais (" + (NBarras(w) - nX0) + ")");

            // --- treliças pela galeria nos pórticos internos: Pratt de duas águas "entre os nós" (topos dos pilares), sobre a viga
            int nT0 = NBarras(w);
            foreach (double x in new[] { 6.0, 12.0, 18.0 })
            {
                var mm = J(w);
                var jp = w.AbrirTrelicas("pratt"); PrepararDialogoForaDaTela(jp); EsperarIdle();
                var campos = Priv<Dictionary<string, Control>>(jp, "campos");
                ((ComboBox)campos["banzo"]).SelectedIndex = 2; EsperarIdle();   // duas águas (remonta os campos)
                campos = Priv<Dictionary<string, Control>>(jp, "campos");
                ((TextBox)campos["altura"]).Text = "0"; ((TextBox)campos["altura2"]).Text = "1,5"; ((TextBox)campos["paineis"]).Text = "10";
                ((TextBox)campos["s_banzo"]).Text = pViga; ((TextBox)campos["s_diag"]).Text = "Tubo 60,3x3,6"; ((TextBox)campos["s_mont"]).Text = "Tubo 60,3x3,6";
                ((ComboBox)campos["apoios"]).SelectedIndex = 3;   // livre: sobre os pilares
                ((TextBox)campos["alvo"]).Text = NoEm(mm, x, 0, H) + " " + NoEm(mm, x, 12, H);
                Esperar(jp.Atualizar());
                r.Checar(jp.UltimaPrevia != null, "prévia da Pratt em x = " + Nf(x) + ": " + jp.Status);
                if (x == 6) FotoDialogo(jp, "A-06-galeria-pratt");
                r.Checar(Esperar2(jp.Inserir()), "Pratt inserida em x = " + Nf(x) + " (" + jp.Status + ")");
                jp.Close();
            }
            m = J(w);
            r.Checar(NBarras(w) > nT0, "treliças: " + (NBarras(w) - nT0) + " barras novas (banzo inferior, montantes e diagonais)");
            r.Checar(Duplicadas(m) == 0, "treliças: o banzo superior reaproveita a viga e as terças (sem barra repetida)");
            r.Checar(Soltos(m) == 0, "treliças: sem nós soltos");
            Foto(w, "A-07-estrutura", () => v.MostrarVista(VistaPadrao.Iso, false));

            // --- ações: casos G1/Q1, peso próprio em G1, telhas e sobrecarga nas terças, vento NBR 6123
            Esperar(w.OperarEspacial(dd => dd.ComJanela(async () =>
            {
                await Motor.Post("/api/modelo/caso", new JsonObject { ["nome"] = "G1", ["tipo"] = "G" });
                await Motor.Post("/api/modelo/caso", new JsonObject { ["nome"] = "Q1", ["tipo"] = "Q" });
                await Motor.Chamar("PATCH", "/api/config", new JsonObject { ["peso_proprio"] = "G1" });
            }), "teste: casos e peso próprio"));
            r.Checar(J(w)["casos"]?["G1"] != null && J(w)["casos"]?["Q1"] != null && (string)J(w)["peso_proprio"] == "G1", "casos G1 (com peso próprio) e Q1");
            double esp = 6.185 / 5;   // terças a cada ~1,24 m
            var itens = new JsonArray();
            foreach (var kv in J(w)["barras"].AsObject().Where(kv => (string)kv.Value["secao"] == pTerca))
                foreach (var (caso, p) in new[] { ("G1", 0.15), ("Q1", 0.25) })
                    itens.Add(new JsonObject { ["tipo"] = "carga_distribuida", ["dados"] = new JsonObject { ["caso"] = caso, ["barra"] = kv.Key, ["q"] = new JsonArray(0.0, 0.0, -p * esp) } });
            Esperar(w.OperarEspacial(dd => dd.Lote(itens), "teste: telhas e sobrecarga"));
            r.Checar(J(w)["cargas_distribuidas"].AsArray().Count == itens.Count, itens.Count + " cargas distribuídas nas terças");
            string statusVento = null;
            bool mudouV = ComJanelaMotor(w, new JanelaVento(), async jan =>
            {
                await AteAsync(() => Priv<System.Collections.IList>(jan, "pavs").Count > 0, 20);
                Priv<TextBox>(jan, "tV0").Text = "35";
                BotaoTexto(jan, "Gerar ações de vento")?.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                var st = Priv<TextBlock>(jan, "status");
                await AteAsync(() => st.Text.StartsWith("✓") || st.Text.StartsWith("✕"), 60);
                statusVento = st.Text;
                FotoDialogo(jan, "A-08-vento");
            });
            var casosV = J(w)["casos"].AsObject().Where(kv => (string)kv.Value["tipo"] == "V").Select(kv => kv.Key).ToList();
            r.Checar(mudouV && casosV.Count >= 4, "vento NBR 6123 (V0 = 35 m/s): " + statusVento + " · casos " + string.Join(", ", casosV));

            // --- F5
            Esperar(w.CalcularEspacial());
            d = w.EspacialAtivo;
            r.Exigir(d.Calculado, "F5 calculou (" + w.StatusMsg.Text + ")");
            r.Checar(d.PiorPorBarra.Count == NBarras(w), "verificação de todas as barras (" + d.PiorPorBarra.Count + " de " + NBarras(w) + ")");
            r.L("resumo: " + d.Resumo());
            Foto(w, "A-09-aproveitamento", () => v.MostrarVista(VistaPadrao.Iso, false));
            bool real0 = Config.Realista3D;
            w.DefinirRealista(true);
            Foto(w, "A-09-aproveitamento-realista", () => v.MostrarVista(VistaPadrao.Iso, false));
            w.DefinirRealista(real0);

            // --- Verificação de barras (janela do motor) = verificação do documento
            var ver = Esperar(d.Consultar("/api/verificacao"));
            var jvb = new JanelaVerificacaoBarras(ver); PrepararDialogoForaDaTela(jvb); jvb.Owner = w; jvb.Show(); EsperarIdle();
            jvb.Ordenar(2, true); EsperarIdle();
            FotoDialogo(jvb, "A-10-verificacao-barras");
            jvb.Close();
            var linhasV = (ver as JsonArray ?? ver?["linhas"] as JsonArray ?? ver?["verificacao"] as JsonArray ?? new JsonArray()).ToList();
            var porBarra = linhasV.GroupBy(l => (string)l["barra"]).ToDictionary(x => x.Key, x => x.Max(DocumentoEspacial.Taxa));
            r.Checar(porBarra.Count == d.PiorPorBarra.Count && porBarra.All(kv => d.PiorPorBarra.TryGetValue(kv.Key, out var l) && Rel9(kv.Value, DocumentoEspacial.Taxa(l))),
                     "janela Verificação de barras = F5 (" + porBarra.Count + " barras, pior taxa " + Nf(porBarra.Values.DefaultIfEmpty(0).Max()) + ")");

            // --- ligações: agrupar, pior placa de base, ligação pilar–viga com chapa de topo
            var ger = w.NovoGerenciadorLigacoes(d); ger.Width = 1320; ger.Height = 780;
            PrepararDialogoForaDaTela(ger); ger.Show(); EsperarIdle();
            Esperar(ger.Carregar(true)); EsperarIdle();
            var grupos = Esperar2(d.Ligacoes("GET", "/api/ligacoes/grupos")).AsArray();
            r.L("grupos: " + string.Join("; ", grupos.Select(x => (string)x["id"] + " " + (string)x["tipo"] + " (" + x["membros"]?.AsArray().Count + ")")));
            r.Checar(grupos.Count >= 3, grupos.Count + " grupos de ligações identificados");
            FotoDialogo(ger, "A-11-ligacoes-grupos");
            var gBase = grupos.FirstOrDefault(x => (string)x["tipo"] == "base");
            r.Checar(gBase != null && gBase["membros"].AsArray().Count == 10, "grupo das bases com os 10 pés de pilar (" + gBase?["membros"]?.AsArray().Count + ")");
            if (gBase != null)
            {
                string gid = (string)gBase["id"];
                ger.Escolher(gid); EsperarIdle();
                Esperar(ger.DimensionarSelecionado()); EsperarIdle();
                var dimB = Esperar2(d.Ligacoes("POST", "/api/ligacoes/grupos/" + gid + "/dimensionar"));
                r.Checar(dimB?["governante"] != null, "pior base do grupo: " + (string)dimB?["governante"]?["barra"] + " (" + (string)dimB?["governante"]?["combinacao"] + ")");
                var jpl = w.PlacaDoGrupo(d, gBase, dimB, out No nb);
                r.Checar(jpl != null, "placa de base do grupo abriu");
                if (jpl != null)
                {
                    PrepararDialogoForaDaTela(jpl); jpl.Show(); EsperarIdle();
                    jpl.DimensionarTudo(); EsperarIdle();
                    var rp = jpl.ResultadoMotor;
                    RenderizarJanela(jpl, 1600, 900, "fluxo-A-12-placa-pior-base.png", () => jpl.MostrarAba(0));
                    r.Checar(rp.Valido && rp.Ok, "placa da pior base dimensionada passa (taxa " + Nf(rp.Taxa) + ", " + jpl.Mensagem + ")");
                    if (nb?.Placa != null)
                    {
                        Esperar(d.Ligacoes("POST", "/api/ligacoes/grupos/editar", new JsonObject { ["acao"] = "projeto", ["grupo"] = gid, ["tipo_ligacao"] = "placa_base", ["dados"] = PlacaMotor.Dados(nb.Placa) }));
                        var dim2 = Esperar2(d.Ligacoes("POST", "/api/ligacoes/grupos/" + gid + "/dimensionar"));
                        var reprov = dim2?["membros"]?.AsArray().Where(x => x["ok"] != null && !(bool)x["ok"]).Count() ?? -1;
                        r.Checar(reprov == 0, "placa aplicada ao grupo: as 10 bases passam (" + reprov + " reprovadas)");
                    }
                    jpl.Close();
                }
            }
            var gJoelho = grupos.FirstOrDefault(x => ((string)x["tipo"]).StartsWith("viga_pilar"));
            r.Checar(gJoelho != null, "grupo da ligação pilar–viga (joelho): " + (string)gJoelho?["id"] + " " + (string)gJoelho?["tipo"]);
            if (gJoelho != null)
            {
                var t2 = w.CriarProjetistaGrupo(d, gJoelho, ger); Esperar(t2);
                var jg = t2.Result;
                r.Exigir(jg != null, "projetista do grupo do joelho abriu");
                jg.Width = 1460; jg.Height = 880; PrepararDialogoForaDaTela(jg); jg.Show(); EsperarIdle();
                Esperar(jg.Escolher("chapa_topo_estendida")); EsperarIdle();
                if (jg.Resultado != null && !Bool(jg.Resultado, "ok")) { Esperar(jg.Otimizar()); EsperarIdle(); }
                FotoDialogo(jg, "A-13-joelho-chapa-topo");
                r.Checar(jg.Resultado != null && Bool(jg.Resultado, "ok"), "chapa de topo estendida no joelho passa (pior membro)");
                Esperar(jg.AplicarAoGrupo()); EsperarIdle();
                var gj = Esperar2(d.Ligacoes("GET", "/api/ligacoes/grupos")).AsArray().First(x => (string)x["id"] == (string)gJoelho["id"]);
                r.Checar((string)gj["projeto"]?["tipo_ligacao"] == "chapa_topo_estendida", "projeto gravado no grupo do joelho (" + (string)gj["projeto"]?["tipo_ligacao"] + ")");
                FotoDialogo(jg, "A-13-joelho-aplicado");
                jg.Close();
            }
            ger.Close(); EsperarIdle();

            // --- exportar DWG e STEP (o mesmo caminho do Arquivo › Exportar, sem a caixa de salvar)
            var pecas = Exportador.Pecas(d.ParaVista(), v.Perfil);
            string dwg = Path.Combine(pastaSaida, "fluxo-A-galpao.dwg"), stp = Path.Combine(pastaSaida, "fluxo-A-galpao.stp");
            Exportador.Cad(dwg, pecas, false);
            var cad = ACadSharp.IO.DwgReader.Read(dwg);
            r.Checar(cad.Entities.OfType<ACadSharp.Entities.Line>().Count() == NBarras(w) && cad.Entities.OfType<ACadSharp.Entities.PolyfaceMesh>().Count() == NBarras(w), "DWG relido: uma linha e uma malha por barra (" + NBarras(w) + ")");
            string step = Exportador.Step(pecas, "Galpao E2E", Path.GetFileName(stp));
            File.WriteAllText(stp, step, new UTF8Encoding(false));
            var errosStep = new List<string>(); Exportador.ConferirStep(step, errosStep);
            r.Checar(errosStep.Count == 0 && System.Text.RegularExpressions.Regex.Matches(step, "=FACETED_BREP\\(").Count == NBarras(w), "STEP: um sólido por barra, referências resolvidas" + (errosStep.Count > 0 ? " — " + string.Join("; ", errosStep.Take(3)) : ""));

            // --- relatório PDF
            string html = RelatorioPdf(w, r, "A-14-relatorio");
            r.Conhecido("E2E-02", html.Contains("PROSSEN E2E"), "relatório leva a empresa do novo projeto (config.projeto.empresa = PROSSEN E2E) no carimbo");

            // --- salvar (.estructto), fechar, reabrir e comparar; F5 de novo dá os mesmos números
            d = w.EspacialAtivo;
            string arq = Path.Combine(pasta, "Galpao E2E.estructto");
            string antes = d.Json.ToJsonString(), gradeAntes = d.Grade?.ToJsonString();
            double taxaAntes = TaxaMax(d);
            var taxas0 = d.PiorPorBarra.ToDictionary(kv => kv.Key, kv => DocumentoEspacial.Taxa(kv.Value));
            r.Exigir(w.SalvarEspacialEm(arq), "salvo em " + arq);
            w.FecharEspaciaisSemPerguntar(); EsperarIdle();
            r.Checar(w.EspacialAtivo == null, "documento fechado");
            Esperar(w.AbrirArquivo(arq));
            var d2 = w.EspacialAtivo;
            r.Exigir(d2 != null, "reaberto");
            r.Checar(JsonNode.DeepEquals(JsonNode.Parse(antes), d2.Json), "modelo reaberto igual ao salvo (nós, barras, cargas, ligações e config)");
            r.Checar(d2.Grade?.ToJsonString() == gradeAntes, "grade estrutural voltou");
            r.Checar(v.EixosX.Length == 5, "eixos da grade na vista depois de reabrir");
            Esperar(w.CalcularEspacial());
            r.Checar(d2.Calculado && d2.PiorPorBarra.Count == taxas0.Count && taxas0.All(kv => d2.PiorPorBarra.TryGetValue(kv.Key, out var l) && Rel9(kv.Value, DocumentoEspacial.Taxa(l))),
                     "F5 depois de reabrir: as mesmas taxas (pior " + Nf(TaxaMax(d2)) + " = " + Nf(taxaAntes) + ")");
            Foto(w, "A-15-reaberto", () => v.MostrarVista(VistaPadrao.Iso, false));
        }

        // ================================================================ B) edifício de 4 pavimentos
        static void FluxoEdificio(MainWindow w, Rel r)
        {
            string pasta = Path.Combine(pastaSaida, "fluxo-B-projeto");
            if (Directory.Exists(pasta)) Directory.Delete(pasta, true);
            var mi = ModeloInicial.Por("edificio");
            var g = new GradeNiveis { X = GradeNiveis.Espacamentos("3x6"), Y = GradeNiveis.Espacamentos("2x5"), Z = GradeNiveis.Cotas("4x3,2") };
            var dados = new NovoProjetoDados { Modelo = mi, Grade = g, Nome = "Edificio E2E", Pasta = pasta, Cliente = "Cliente E2E", Engenheiro = "Eng. E2E", Empresa = "PROSSEN E2E",
                                               Material = mi.Material, Pilar = mi.Pilar, Viga = mi.Viga, Normas = new List<string> { "NBR 8800", "NBR 15421" } };
            r.Exigir(Esperar(Inicio.Acoes.Novo(w, dados)), "edifício pelo modelo da galeria (4 pavimentos de 3,2 m, 3×6 por 2×5 m)");
            var d = w.EspacialAtivo; var v = w.vista3D;
            var m = J(w);
            int pil = m["barras"].AsObject().Count(kv => (string)kv.Value["params"]?["papel"] == "pilar"), vig = m["barras"].AsObject().Count(kv => (string)kv.Value["params"]?["papel"] == "viga");
            r.Checar(pil == 4 * 12 && vig == 4 * 17, "pilares " + pil + " (esperado 48) e vigas " + vig + " (esperado 68)");
            r.Checar(Niveis.Lista(m).Count == 5, "5 níveis (Térreo + 4)");
            Foto(w, "B-01-edificio", () => v.MostrarVista(VistaPadrao.Iso, false));

            // --- seção dos pilares em lote (Propriedades em lote) e um Ctrl+Z
            string nova = PerfilCatalogo("W310", "W310x52", "W310x44.5");
            Esperar(w.OperarEspacial(dd => dd.GarantirSecao(nova), "teste: seção do catálogo"));
            var pilares = J(w)["barras"].AsObject().Where(kv => (string)kv.Value["params"]?["papel"] == "pilar").Select(kv => kv.Key).ToList();
            string secViga = (string)J(w)["barras"].AsObject().First(kv => (string)kv.Value["params"]?["papel"] == "viga").Value["secao"];
            v.Selecionar(null, pilares); EsperarIdle();
            w.AbrirEdicaoEsp("propriedades");
            var pe = w.PainelEdicaoAberto; r.Exigir(pe != null, "painel Propriedades em lote abriu");
            PrepararDialogoForaDaTela(pe);
            ((ComboBox)pe.Campos["secao"]).SelectedItem = nova; EsperarIdle();
            FotoDialogo(pe, "B-02-propriedades-lote");
            Esperar(pe.Executar()); pe.Close();
            m = J(w);
            r.Checar(pilares.All(b => (string)m["barras"][b]["secao"] == nova) && m["barras"].AsObject().Where(kv => (string)kv.Value["params"]?["papel"] == "viga").All(kv => (string)kv.Value["secao"] == secViga),
                     "48 pilares com " + nova + "; vigas continuam " + secViga);
            Comando(w, "U"); AteCond(() => (string)J(w)["barras"][pilares[0]]["secao"] != nova, 20, w);
            r.Checar(pilares.All(b => (string)J(w)["barras"][b]["secao"] != nova), "um Ctrl+Z desfaz o lote inteiro");
            Comando(w, "REDO"); AteCond(() => (string)J(w)["barras"][pilares[0]]["secao"] == nova, 20, w);
            r.Checar(pilares.All(b => (string)J(w)["barras"][b]["secao"] == nova), "Ctrl+Y refaz o lote");
            v.LimparSelecao();

            // --- carga em área (laje) em todos os pavimentos: Σ q·L = p · área
            double area = 18 * 10, p = 2.5;
            foreach (double z in new[] { 3.2, 6.4, 9.6, 12.8 })
            {
                var rr = DocumentoEspacial.CargaArea(J(w), z, p, true);
                Esperar(w.DistribuirArea(z, p, true));
                r.Checar(Math.Abs(rr.Sum(x => x.q * x.L) - p * area) < 1e-6 * p * area, "laje " + Nf(p) + " kN/m² em z = " + Nf(z) + ": Σ q·L = " + Nf(rr.Sum(x => x.q * x.L)) + " kN (p·A = " + Nf(p * area) + ")");
            }
            Esperar(w.OperarEspacial(dd => dd.ComJanela(() => Motor.Chamar("PATCH", "/api/config", new JsonObject { ["peso_proprio"] = "G1" })), "teste: peso próprio"));
            r.Checar((string)J(w)["peso_proprio"] == "G1", "peso próprio no caso G1");

            // --- sismo NBR 15421 (zona 2, terreno D) pela janela
            string stSismo = null;
            bool mudouS = ComJanelaMotor(w, new JanelaSismo(), async jan =>
            {
                await AteAsync(() => Priv<TextBlock>(jan, "status").Text == "", 20);
                PrivSet(jan, "zona", 2); PrivSet(jan, "classe", "D"); PrivCall(jan, "Montar");
                BotaoTexto(jan, "Gerar ações sísmicas")?.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                var st = Priv<TextBlock>(jan, "status");
                await AteAsync(() => st.Text.StartsWith("✓") || st.Text.StartsWith("✕"), 60);
                stSismo = st.Text;
                FotoDialogo(jan, "B-03-sismo");
            });
            var casosE = J(w)["casos"].AsObject().Where(kv => (string)kv.Value["tipo"] == "E").Select(kv => kv.Key).ToList();
            r.Checar(mudouS && casosE.Count >= 2, "sismo gerado: " + stSismo + " · casos " + string.Join(", ", casosE));

            // --- F5, modal, estabilidade
            Esperar(w.CalcularEspacial());
            d = w.EspacialAtivo;
            r.Exigir(d.Calculado, "F5 calculou (" + w.StatusMsg.Text + ")");
            r.Checar(d.Combinacoes.Any(c => c.Contains("S")), "combinações com sismo (" + d.Combinacoes.Count() + " combinações)");
            Foto(w, "B-04-aproveitamento", () => v.MostrarVista(VistaPadrao.Iso, false));
            var modal = Esperar(d.Consultar("/api/modal?n=6"));
            var per = modal?["periodos_s"]?.AsArray().Select(x => (double)x).ToList() ?? new List<double>();
            r.Checar(per.Count == 6 && per.All(x => x > 0) && per.Zip(per.Skip(1), (a, b) => a >= b - 1e-12).All(x => x), "modal: 6 períodos positivos e decrescentes (T1 = " + (per.Count > 0 ? Nf(per[0]) : "—") + " s)");
            var jm = new JanelaModal(modal); PrepararDialogoForaDaTela(jm); jm.Owner = w; jm.Show(); EsperarIdle();
            FotoDialogo(jm, "B-05-modal"); jm.Close();
            var est = Esperar(d.Consultar("/api/estabilidade"));
            var gz = (est?["gama_z"] as JsonObject)?.Select(kv => UiAnalise.D(kv.Value, "valor")).Where(x => !double.IsNaN(x)).ToList() ?? new List<double>();
            r.L("estabilidade: " + est?.ToJsonString().Substring(0, Math.Min(400, est?.ToJsonString().Length ?? 0)));
            r.Checar(gz.Count > 0 && gz.All(x => x >= 1 && x < 2), "γz por direção: " + string.Join(", ", gz.Select(x => Nf(x))));
            var je = new JanelaEstabilidade(est); PrepararDialogoForaDaTela(je); je.Owner = w; je.Show(); EsperarIdle();
            FotoDialogo(je, "B-06-estabilidade"); je.Close();

            // --- relatório
            string html = RelatorioPdf(w, r, "B-07-relatorio");
            r.Checar(html.Contains("15421") && html.Contains("γ"), "relatório cita a NBR 15421 e a estabilidade (γz)");
        }

        // ================================================================ C) arquivos legados e CYPE
        static void FluxoLegado(MainWindow w, Rel r)
        {
            string raiz = Path.GetFullPath(Path.Combine(Environment.GetEnvironmentVariable("ESTRUCTTO_MOTOR") ?? Path.Combine(Environment.CurrentDirectory, "..", "motor"), "..", ".."));
            r.L("raiz do repositório: " + raiz);
            var arqs = Directory.GetFiles(Path.Combine(raiz, "exemplos"), "guincho-*.estructto").Concat(Directory.GetFiles(Path.Combine(raiz, "exemplos"), "*.ftl")).OrderBy(x => x).ToList();
            r.Checar(arqs.Count >= 2, arqs.Count + " arquivos legados em exemplos/");
            foreach (string a in arqs)
            {
                string nome = Path.GetFileName(a);
                w.FecharEspaciaisSemPerguntar(); EsperarIdle();
                Esperar(w.AbrirArquivo(a));
                var d = w.EspacialAtivo;
                if (!r.Checar(d != null && d.Json["barras"].AsObject().Count > 0, nome + ": abriu convertido para 3D (" + (d == null ? w.StatusMsg.Text : d.Json["barras"].AsObject().Count + " barras, origem " + Path.GetFileName(d.Origem)) + ")")) continue;
                r.Checar(d.Origem == a, nome + ": marcado como convertido (Salvar não sobrescreve o antigo)");
                if (nome.Contains("placa")) r.Checar(d.Placas.Count > 0 || MainWindow.GruposComPlaca(d).Any(), nome + ": placas de base vieram junto");
                Esperar(w.CalcularEspacial());
                r.Checar(w.EspacialAtivo.Calculado, nome + ": F5 (" + w.EspacialAtivo.Resumo() + ")");
                Foto(w, "C-" + Slug(Path.GetFileNameWithoutExtension(a)), () => w.vista3D.MostrarVista(VistaPadrao.Iso, false));
                // salvar convertido em .estructto e reabrir
                string novo = Path.Combine(pastaSaida, "fluxo-C-" + Slug(Path.GetFileNameWithoutExtension(a)) + " (3D).estructto");
                string j0 = w.EspacialAtivo.Json.ToJsonString();
                r.Checar(w.SalvarEspacialEm(novo), nome + ": salvo no formato novo");
                w.FecharEspaciaisSemPerguntar(); Esperar(w.AbrirArquivo(novo));
                r.Checar(w.EspacialAtivo != null && JsonNode.DeepEquals(JsonNode.Parse(j0), w.EspacialAtivo.Json) && w.EspacialAtivo.Origem == null, nome + ": reaberto do .estructto 3D igual");
            }

            // --- CYPE (.ed3 dos testes do motor): importar, F5 e comparar com os resultados gravados pelo CYPE
            var ed3s = Directory.GetFiles(Path.Combine(raiz, "codigo", "motor", "testes", "dados_cype"), "*.ed3").OrderBy(x => Path.GetFileName(x) == "novo.ed3" ? 0 : 1).ToList();
            r.Checar(ed3s.Count > 0, ed3s.Count + " obras .ed3");
            foreach (string e in ed3s)
            {
                string nome = Path.GetFileName(e);
                w.FecharEspaciaisSemPerguntar(); EsperarIdle();
                var t0 = Stopwatch.StartNew();
                Esperar(w.AbrirArquivo(e));
                var d = w.EspacialAtivo;
                if (!r.Checar(d != null && d.Json["barras"].AsObject().Count > 0, nome + ": importado (" + (d == null ? w.StatusMsg.Text : d.Json["barras"].AsObject().Count + " barras, " + d.Avisos.Count + " avisos") + ", " + t0.ElapsedMilliseconds + " ms)")) continue;
                Esperar(w.CalcularEspacial());
                r.Checar(w.EspacialAtivo.Calculado, nome + ": F5 (" + w.StatusMsg.Text + ")");
                if (nome == "novo.ed3") Foto(w, "C-cype-novo", () => w.vista3D.MostrarVista(VistaPadrao.Iso, false));
                JsonNode cmp;
                try { cmp = Esperar(Motor.Post("/api/comparar_cype", new JsonObject { ["nome"] = nome, ["conteudo_base64"] = Convert.ToBase64String(File.ReadAllBytes(e)) })); }
                catch (Exception ex) { r.Checar(false, nome + ": /api/comparar_cype: " + ComandosAnalise.MensagemMotor(ex)); continue; }
                var tab = cmp["tabela"]?.AsArray().ToList() ?? new List<JsonNode>();
                r.Checar(tab.Count > 0, nome + ": comparação com " + tab.Count + " linhas (válido = " + cmp["valido"] + ")");
                // deslocamentos e reações relevantes (≥ 5 % do maior da grandeza) devem bater; esforço é conferido por ordem de grandeza
                foreach (var grp in tab.GroupBy(x => (string)x["grandeza"]))
                {
                    var lin = grp.Where(x => x["cype"] != null && x["nosso"] != null).ToList();
                    if (lin.Count == 0) continue;
                    double maior = lin.Max(x => Math.Abs((double)x["cype"]));
                    var rel = lin.Where(x => Math.Abs((double)x["cype"]) >= 0.05 * maior && maior > 1e-9).ToList();
                    double pior = rel.Select(x => Math.Abs((double)x["cype"] - (double)x["nosso"]) / Math.Max(1e-12, Math.Abs((double)x["cype"]))).DefaultIfEmpty(0).Max() * 100;
                    var piorL = rel.OrderByDescending(x => Math.Abs((double)x["cype"] - (double)x["nosso"]) / Math.Max(1e-12, Math.Abs((double)x["cype"]))).FirstOrDefault();
                    string txt = nome + ": " + grp.Key + " — " + rel.Count + " valores relevantes, maior diferença " + pior.ToString("0.0", CultureInfo.InvariantCulture) + " %" +
                                 (piorL != null ? " (" + (string)piorL["hipotese"] + ": CYPE " + Nf((double)piorL["cype"], 4) + " × " + Nf((double)piorL["nosso"], 4) + ")" : "");
                    r.L("        " + txt);
                    if (nome == "novo.ed3") r.Checar(pior <= 5, txt);
                }
                if (nome == "novo.ed3")
                {
                    var jc = new JanelaCype(c => Motor.Post("/api/comparar_cype", c), c => Motor.Post("/api/importar_cype", c));
                    PrepararDialogoForaDaTela(jc); jc.Owner = w; jc.Show(); jc.Mostrar(nome, cmp, null); EsperarIdle();
                    FotoDialogo(jc, "C-cype-comparar"); jc.Close();
                }
            }
        }

        // ================================================================ D) edição
        static void FluxoEdicao(MainWindow w, Rel r)
        {
            Esperar(w.NovoDocumento(null, "Edição E2E"));
            var d = w.EspacialAtivo; var v = w.vista3D;
            r.Exigir(d != null, "modelo em branco (planta, térreo)");
            Point3D Q(double x, double y, double z) => new Point3D(x, y, z);
            Esperar(w.OperarEspacial(dd => dd.DesenharBarras(new[] { (Q(0, 0, 0), Q(0, 0, 3)), (Q(6, 0, 0), Q(6, 0, 3)), (Q(0, 0, 3), Q(6, 0, 3)) }, null, true), "teste: pórtico"));
            r.Exigir(NBarras(w) == 3 && NNos(w) == 4, "pórtico 6 × 3 m (3 barras, 4 nós)");
            Point T(Point3D p) => v.Tela(p);
            void Desfaz(string oque, Func<bool> antes, Func<bool> depois)
            {
                Comando(w, "U"); r.Checar(AteCond(antes, 20, w), oque + ": Ctrl+Z desfaz");
                Comando(w, "REDO"); r.Checar(AteCond(depois, 20, w), oque + ": Ctrl+Y refaz");
            }
            v.MostrarVista(VistaPadrao.Iso, false); v.Enquadrar(false);

            // CO: tudo escolhido, ponto base e destino digitados
            Comando(w, "SELTODAS");
            Comando(w, "CO"); Comando(w, "0,0,0"); Comando(w, "@0,5,0");
            AteCond(() => NBarras(w) == 6, 20, w);
            v.Tecla(Key.Enter);
            r.Checar(NBarras(w) == 6 && NoEm(J(w), 6, 5, 3) != null, "COPIAR: cópia em y = 5 (" + NBarras(w) + " barras)");
            Desfaz("COPIAR", () => NBarras(w) == 3, () => NBarras(w) == 6);
            Foto(w, "D-01-copiar", () => v.MostrarVista(VistaPadrao.Iso, false));

            // MI: o pórtico y = 0 espelhado na linha y = −1 (em planta)
            v.Selecionar(J(w)["nos"].AsObject().Where(kv => Math.Abs((double)kv.Value["y"]) < 1e-9).Select(kv => kv.Key), null); EsperarIdle();
            var bsY0 = J(w)["barras"].AsObject().Where(kv => Math.Abs(Pos(J(w), (string)kv.Value["ni"]).Y) < 1e-9 && Math.Abs(Pos(J(w), (string)kv.Value["nj"]).Y) < 1e-9).Select(kv => kv.Key).ToList();
            v.Selecionar(null, bsY0); EsperarIdle();
            Comando(w, "MI"); Comando(w, "0,-1,0"); Comando(w, "@6,0,0");
            AteCond(() => NBarras(w) == 9, 20, w);
            r.Checar(NBarras(w) == 9 && NoEm(J(w), 6, -2, 3) != null && NoEm(J(w), 0, -2, 0) != null, "ESPELHAR: cópia em y = −2 (" + NBarras(w) + " barras)");
            Desfaz("ESPELHAR", () => NBarras(w) == 6, () => NBarras(w) == 9);

            // RO: a cópia espelhada gira 90° em torno de (0; 0): (6; −2) vai para (2; 6)
            var espelho = J(w)["barras"].AsObject().Where(kv => Math.Abs(Pos(J(w), (string)kv.Value["ni"]).Y + 2) < 1e-9).Select(kv => kv.Key).ToList();
            v.Selecionar(null, espelho); EsperarIdle();
            Comando(w, "RO"); Comando(w, "0,0,0"); Comando(w, "90");
            AteCond(() => NoEm(J(w), 2, 6, 3) != null, 20, w);
            r.Checar(NoEm(J(w), 2, 6, 3) != null && NoEm(J(w), 6, -2, 3) == null && NBarras(w) == 9, "ROTACIONAR 90°: (6; −2; 3) → (2; 6; 3)");
            Desfaz("ROTACIONAR", () => NoEm(J(w), 6, -2, 3) != null, () => NoEm(J(w), 2, 6, 3) != null);
            v.LimparSelecao();

            // AR: matriz 3 × 1 × 1 (8 m) do pórtico original
            v.Selecionar(null, bsY0); EsperarIdle();
            Comando(w, "AR");
            var pm = w.PainelEdicaoAberto; r.Exigir(pm != null, "MATRIZ: painel abriu");
            PrepararDialogoForaDaTela(pm);
            ((TextBox)pm.Campos["nx"]).Text = "3"; ((TextBox)pm.Campos["ex"]).Text = "8";
            FotoDialogo(pm, "D-02-matriz");
            Esperar(pm.Executar()); pm.Close();
            r.Checar(NBarras(w) == 15 && NoEm(J(w), 22, 0, 3) != null, "MATRIZ 3×: +6 barras até x = 22 (" + NBarras(w) + ")");
            Desfaz("MATRIZ", () => NBarras(w) == 9, () => NBarras(w) == 15);
            v.LimparSelecao();

            // TR e EX: viga transversal que cruza a viga y = 0 e uma que não chega nela
            Esperar(w.OperarEspacial(dd => dd.DesenharBarras(new[] { (Q(3, -1.5, 3), Q(3, 1.5, 3)), (Q(4, -3, 3), Q(4, -1, 3)) }, null), "teste: barras de corte"));
            var m = J(w);
            string viga = BarraEntre(m, NoEm(m, 0, 0, 3), NoEm(m, 6, 0, 3)), corte = BarraEntre(m, NoEm(m, 3, -1.5, 3), NoEm(m, 3, 1.5, 3)), curta = BarraEntre(m, NoEm(m, 4, -3, 3), NoEm(m, 4, -1, 3));
            r.Exigir(viga != null && corte != null && curta != null, "barras para aparar/estender");
            Comando(w, "TR");
            Esperar(v.EscolherPorCodigo(v.ObjetoPorId("barra", viga), Q(1, 0, 3)));
            Esperar(v.EscolherPorCodigo(v.ObjetoPorId("barra", corte), Q(3, 1.2, 3)));
            AteCond(() => NoEm(J(w), 3, 1.5, 3) == null, 20, w);
            v.Tecla(Key.Enter);
            m = J(w);
            var pc = m["barras"][corte];
            r.Checar(pc != null && new[] { Pos(m, (string)pc["ni"]), Pos(m, (string)pc["nj"]) }.Any(p => (p - Q(3, 0, 3)).Length < 1e-6) && NoEm(m, 3, 1.5, 3) == null, "APARAR: o lado clicado (y > 0) saiu; a barra termina em (3; 0; 3)");
            Desfaz("APARAR", () => NoEm(J(w), 3, 1.5, 3) != null, () => NoEm(J(w), 3, 1.5, 3) == null);
            Comando(w, "EX");
            Esperar(v.EscolherPorCodigo(v.ObjetoPorId("barra", viga), Q(1, 0, 3)));
            Esperar(v.EscolherPorCodigo(v.ObjetoPorId("barra", curta), Q(4, -1.2, 3)));
            AteCond(() => NoEm(J(w), 4, 0, 3) != null, 20, w);
            v.Tecla(Key.Enter);
            r.Checar(NoEm(J(w), 4, 0, 3) != null && NoEm(J(w), 4, -1, 3) == null, "ESTENDER: a ponta perto do clique foi até a viga (4; 0; 3)");
            Desfaz("ESTENDER", () => NoEm(J(w), 4, -1, 3) != null, () => NoEm(J(w), 4, 0, 3) != null);
            Foto(w, "D-03-aparar-estender", () => v.MostrarVista(VistaPadrao.Topo, false));

            // grips: ponta (nó) +1 m em z; meio da barra +1 m em y
            m = J(w);
            string topo = NoEm(m, 6, 5, 3), vigaY5 = BarraEntre(m, NoEm(m, 0, 5, 3), topo);
            v.Selecionar(null, new[] { vigaY5 }); EsperarIdle();
            r.Checar(v.EsquentarGrip("no", topo), "grip da ponta esquentou");
            Foto(w, "D-04-grip", () => { });
            v.EntrarTexto("@0,0,1");
            AteCond(() => Math.Abs((double)J(w)["nos"][topo]["z"] - 4) < 1e-9, 20, w);
            r.Checar(Math.Abs((double)J(w)["nos"][topo]["z"] - 4) < 1e-9, "grip da ponta: nó levado a z = 4 com as barras ligadas");
            Desfaz("grip da ponta", () => Math.Abs((double)J(w)["nos"][topo]["z"] - 3) < 1e-9, () => Math.Abs((double)J(w)["nos"][topo]["z"] - 4) < 1e-9);
            v.Selecionar(null, new[] { vigaY5 }); EsperarIdle();
            r.Checar(v.EsquentarGrip("meio", vigaY5), "grip do meio esquentou");
            int nb = NBarras(w);
            v.EntrarTexto("@0,1,0");
            AteCond(() => J(w)["barras"][vigaY5] is JsonNode bb && Math.Abs(Pos(J(w), (string)bb["ni"]).Y - 6) < 1e-9, 20, w);
            var bm = J(w)["barras"][vigaY5];
            r.Checar(bm != null && Math.Abs(Pos(J(w), (string)bm["ni"]).Y - 6) < 1e-9 && Math.Abs(Pos(J(w), (string)bm["nj"]).Y - 6) < 1e-9, "grip do meio: barra movida para y = 6");
            Desfaz("grip do meio", () => Math.Abs(Pos(J(w), (string)J(w)["barras"][vigaY5]["ni"]).Y - 5) < 1e-9, () => Math.Abs(Pos(J(w), (string)J(w)["barras"][vigaY5]["ni"]).Y - 6) < 1e-9);
            v.LimparSelecao();

            // MEDIR: distância entre (0; 0; 0) e (6; 0; 3) e entre dois nós da cópia
            Comando(w, "ME");
            r.Checar(v.Ferramenta == Ferramenta3D.Medir, "ME liga o Medir");
            v.Snap.Rastreio = false;
            void Clique(Point3D p) { v.MoverTela(T(p) + new Vector(2, 2)); Esperar(v.ClicarTela(T(p) + new Vector(2, 2))); EsperarIdle(); }
            Clique(Q(0, 0, 0)); Clique(Q(6, 0, 3));
            r.Checar(v.Medidor.Res?.ValorTxt == "6,708 m", "MEDIR (0; 0; 0) → (6; 0; 3) = " + v.Medidor.Res?.ValorTxt + " (esperado 6,708 m)");
            Foto(w, "D-05-medir", () => { });
            v.Tecla(Key.Escape);
            Clique(Q(0, 0, 3)); Clique(Q(6, 5, 3));
            r.Checar(v.Medidor.Res?.ValorTxt == "7,810 m", "MEDIR (0; 0; 3) → (6; 5; 3) = " + v.Medidor.Res?.ValorTxt + " (esperado 7,810 m)");
            v.Tecla(Key.Escape); v.Tecla(Key.Escape);
            v.Snap.Rastreio = true;
            r.Checar(v.Ferramenta == Ferramenta3D.Selecionar, "Esc em cascata sai do Medir");
            Esperar(w.CalcularEspacial());
            r.Checar(w.EspacialAtivo.Calculado || w.StatusMsg.Text.Contains("Motor"), "modelo editado calcula ou o motor explica (" + w.StatusMsg.Text + ")");
        }

        // ================================================================ E) robustez
        static void FluxoRobustez(MainWindow w, Rel r)
        {
            var v = w.vista3D;
            // --- o motor morre no meio da sessão
            Esperar(w.AbrirEspacial(() => DocumentoEspacial.Exemplo("galpao_aco"), "exemplo"));
            Esperar(w.CalcularEspacial());
            var d = w.EspacialAtivo;
            r.Exigir(d.Calculado, "galpão de exemplo calculado");
            var taxas = d.PiorPorBarra.ToDictionary(kv => kv.Key, kv => DocumentoEspacial.Taxa(kv.Value));
            Process MotorProc() => Priv<Process>(typeof(Motor), "proc");
            void Matar() { var p = MotorProc(); try { p?.Kill(true); p?.WaitForExit(10000); } catch { } }
            Matar();
            r.Checar(!Motor.Ligado, "motor morto (kill no processo)");
            string antes = w.StatusMsg.Text;
            Esperar(w.CalcularEspacial());
            r.L("depois do kill, F5: " + w.StatusMsg.Text);
            bool recuperou = w.EspacialAtivo.Calculado && w.EspacialAtivo.PiorPorBarra.Count == taxas.Count && taxas.All(kv => Rel9(kv.Value, DocumentoEspacial.Taxa(w.EspacialAtivo.PiorPorBarra[kv.Key])));
            r.Checar(recuperou, "F5 depois do motor morto: o motor sobe de novo, o modelo volta e as taxas são as mesmas");
            Foto(w, "E-01-motor-reiniciado", () => v.MostrarVista(VistaPadrao.Iso, false));
            // morre DURANTE o cálculo
            var t = w.CalcularEspacial();
            AteCond(() => w.OcupadoEsp, 5);
            Matar();
            Esperar(t);
            string msg = w.StatusMsg.Text;
            r.L("motor morto durante o F5: status = " + msg + " · calculado = " + w.EspacialAtivo.Calculado);
            bool msgClara = w.EspacialAtivo.Calculado || (msg.Contains("motor", StringComparison.OrdinalIgnoreCase) && (msg.Contains("encerr") || msg.Contains("reinici") || msg.Contains("parou") || msg.Contains("de novo") || msg.Contains("F5")));
            r.Conhecido("E2E-03", msgClara, "motor morto durante o F5: mensagem clara ou cálculo refeito (status: " + msg + ")");
            Esperar(w.CalcularEspacial());
            r.Checar(w.EspacialAtivo.Calculado, "F5 seguinte recupera (" + w.StatusMsg.Text + ")");
            // edição com o motor morto: o documento não pode perder a alteração
            Matar();
            string barra = w.EspacialAtivo.Json["barras"].AsObject().First().Key;
            string secNova = w.EspacialAtivo.Json["secoes"].AsObject().Select(kv => kv.Key).First(s => s != (string)w.EspacialAtivo.Json["barras"][barra]["secao"]);
            Esperar(w.EditarEspacial("barra", barra, new JsonObject { ["secao"] = secNova }));
            r.Checar((string)J(w)["barras"][barra]["secao"] == secNova, "edição logo depois do motor morto: aplicada no motor novo (" + w.StatusMsg.Text + ")");

            // --- entradas inválidas nos diálogos
            int nb0 = NBarras(w); string j0 = J(w).ToJsonString();
            v.Selecionar(null, new[] { barra }); EsperarIdle();
            w.AbrirEdicaoEsp("copiar");
            var pe = w.PainelEdicaoAberto; PrepararDialogoForaDaTela(pe);
            ((TextBox)pe.Campos["dx"]).Text = "abc";
            Esperar(pe.Executar());
            string erroPainel = Textos(pe);
            FotoDialogo(pe, "E-02-copiar-invalido");
            r.Checar(J(w).ToJsonString() == j0 && erroPainel.Contains("inválido"), "Copiar com dx = abc: mensagem no painel e modelo intacto");
            ((TextBox)pe.Campos["dx"]).Text = "1"; ((TextBox)pe.Campos["n"]).Text = "-2";
            Esperar(pe.Executar());
            r.Checar(J(w).ToJsonString() == j0 && Textos(pe).Contains("inteiro"), "Copiar com Cópias = −2: recusado com mensagem");
            pe.Close(); v.LimparSelecao();
            string stV = null; string ventoAntes = J(w)["config"]?["vento"]?.ToJsonString();
            ComJanelaMotor(w, new JanelaVento(), async jan =>
            {
                await AteAsync(() => Priv<TextBlock>(jan, "status").Text == "", 20);
                Priv<TextBox>(jan, "tV0").Text = "-5";
                BotaoTexto(jan, "Gerar ações de vento")?.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                await AteAsync(() => Priv<TextBlock>(jan, "status").Text.StartsWith("✕"), 10);
                stV = Priv<TextBlock>(jan, "status").Text;
                FotoDialogo(jan, "E-03-vento-invalido");
            });
            r.Checar(stV != null && stV.StartsWith("✕") && J(w)["config"]?["vento"]?.ToJsonString() == ventoAntes, "Vento com V0 = −5: recusado (" + stV + ") e config intacta");
            var jt = w.AbrirTrelicas("pratt"); PrepararDialogoForaDaTela(jt); EsperarIdle();
            var campos = Priv<Dictionary<string, Control>>(jt, "campos");
            ((TextBox)campos["vao"]).Text = "doze";
            Esperar(jt.Atualizar());
            int nbT = NBarras(w);
            bool ins = Esperar2(jt.Inserir());
            r.Checar(!ins && NBarras(w) == nbT && jt.Status.StartsWith("⚠"), "Treliça com vão = doze: não insere e explica (" + jt.Status + ")");
            ((TextBox)campos["vao"]).Text = "0";
            Esperar(jt.Atualizar());
            r.Checar(jt.UltimaPrevia == null && jt.Status.Length > 2, "Treliça com vão = 0: o motor recusa com mensagem (" + jt.Status + ")");
            FotoDialogo(jt, "E-04-trelica-invalida");
            jt.Close();
            var jgr = new JanelaGrade("0,@6,@x", "0,@5", "0,@3");
            r.Checar(!jgr.Ler() && Textos(jgr).Length > 0, "Grade com @x: recusada com mensagem");
            jgr.Close();
            bool recusou = false; try { GradeNiveis.Espacamentos("3x0"); } catch (FormatException) { recusou = true; }
            r.Checar(recusou, "novo projeto com vão 3x0: recusado (FormatException)");
            Comando(w, "L"); v.EntrarTexto("1,2,x"); EsperarIdle();
            r.Checar(NBarras(w) == nb0 && (v.Aviso ?? "").Contains("inválida"), "coordenada digitada 1,2,x: aviso na vista (" + v.Aviso + ")");
            v.Tecla(Key.Escape); v.Tecla(Key.Escape);

            // --- autosave e recuperação
            // (uma cópia por documento em %AppData%\NovoFtool\recuperacao)
            var timer = Priv<DispatcherTimer>(w, "autoSalvar");
            var intervalo = timer.Interval;
            string chave = w.EspacialAtivo.Chave;
            Config.ApagarRecuperacao(chave);
            w.alterado = true;
            timer.Interval = TimeSpan.FromMilliseconds(300);
            r.Checar(AteCond(() => Config.TemRecuperacao(chave), 10), "autosave gravou a cópia de recuperação do documento");
            timer.Interval = intervalo;
            var rec = Config.LerRecuperacoes().FirstOrDefault(x => x.Chave == chave);
            r.Checar(rec.Conteudo != null && JsonNode.DeepEquals(JsonNode.Parse(rec.Conteudo)["modelo"], J(w)), "cópia de recuperação = modelo atual (contêiner .estructto)");
            string jRec = J(w).ToJsonString();
            w.FecharEspaciaisSemPerguntar(); EsperarIdle();
            if (rec.Conteudo != null)
            {
                Config.SalvarRecuperacao(chave, rec.Conteudo, rec.Original);   // como se o programa tivesse caído com a cópia no disco
                respostaCaixa = "Recuperar";
                bool okRec = Esperar(w.OferecerRecuperacao(chave, rec.Conteudo, rec.Original));
                r.Checar(okRec && w.EspacialAtivo != null && JsonNode.DeepEquals(JsonNode.Parse(jRec), w.EspacialAtivo.Json) && (w.EspacialAtivo.Titulo ?? "").Contains("recuperado"), "Recuperar abre a cópia (" + w.EspacialAtivo?.Titulo + ")");
                r.Checar(caixasVistas.Any(c => c.Contains("Recuperar o modelo?")), "a pergunta de recuperação apareceu");
                r.Checar(!Config.TemRecuperacao(chave) && w.EspacialAtivo != null && Config.TemRecuperacao(w.EspacialAtivo.Chave), "cópia antiga apagada; o documento recuperado tem a cópia dele");
                Foto(w, "E-05-recuperado", () => v.MostrarVista(VistaPadrao.Iso, false));
                if (w.EspacialAtivo != null) Config.ApagarRecuperacao(w.EspacialAtivo.Chave);
                w.FecharEspaciaisSemPerguntar(); EsperarIdle();
            }
            Config.ApagarRecuperacao(chave);

            // --- modelo de ~3.000 barras: abrir, calcular, quadro do Realista
            var big = new JsonObject
            {
                ["tipo"] = "edificio", ["material"] = "A-572 GR.50", ["apoio"] = "engaste",
                ["vaos_x"] = new JsonArray(Enumerable.Repeat(6.0, 10).Select(x => (JsonNode)x).ToArray()), ["vaos_y"] = new JsonArray(Enumerable.Repeat(5.0, 10).Select(x => (JsonNode)x).ToArray()),
                ["alturas"] = new JsonArray(Enumerable.Repeat(3.2, 9).Select(x => (JsonNode)x).ToArray()), ["secoes"] = new JsonObject { ["pilar"] = "W310x38.7", ["viga"] = "W310x38.7" },
            };
            var sw = Stopwatch.StartNew();
            Esperar(w.AbrirEspacial(() => DocumentoEspacial.Gerar(big, "Grande E2E", 10, 5), "gerando"));
            double tAbrir = sw.Elapsed.TotalSeconds;
            int nGrande = NBarras(w);
            r.Checar(nGrande >= 3000, "modelo grande: " + nGrande + " barras, " + NNos(w) + " nós");
            r.Checar(tAbrir < 30, "gerar e abrir em " + tAbrir.ToString("0.0", CultureInfo.InvariantCulture) + " s (limite 30 s)");
            sw.Restart();
            Esperar(w.CalcularEspacial());
            double tCalc = sw.Elapsed.TotalSeconds;
            r.Checar(w.EspacialAtivo.Calculado, "F5 no modelo grande (" + w.StatusMsg.Text + ")");
            r.Checar(tCalc < 300, "F5 em " + tCalc.ToString("0.0", CultureInfo.InvariantCulture) + " s (limite 300 s)");
            bool real0 = Config.Realista3D;
            sw.Restart();
            w.DefinirRealista(true); EsperarIdle();
            double tReal = sw.Elapsed.TotalSeconds;
            double quadro = v.MedirQuadroDx();
            sw.Restart();
            Foto(w, "E-06-grande-realista", () => v.MostrarVista(VistaPadrao.Iso, false));
            double tFoto = sw.Elapsed.TotalSeconds;
            r.L("Realista: ligar " + tReal.ToString("0.00", CultureInfo.InvariantCulture) + " s · quadro DX " + (double.IsNaN(quadro) ? "— (sem GPU: render nativo)" : quadro.ToString("0.0", CultureInfo.InvariantCulture) + " ms") + " · captura " + tFoto.ToString("0.00", CultureInfo.InvariantCulture) + " s");
            r.Checar(tReal < 20 && (double.IsNaN(quadro) || quadro < 1000), "Realista no modelo grande: liga em " + tReal.ToString("0.0", CultureInfo.InvariantCulture) + " s, quadro " + (double.IsNaN(quadro) ? "nativo" : quadro.ToString("0", CultureInfo.InvariantCulture) + " ms"));
            w.DefinirRealista(real0);
        }
    }
}
