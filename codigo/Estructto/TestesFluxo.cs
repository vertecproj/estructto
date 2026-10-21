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
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using Portico;

namespace NovoFtool
{
    public static partial class Capturas
    {
        // ================================================================ entrada
        static string[] fluxosPedidos;

        // MainWindow → Capturas.Configurar: a própria janela do StartupUri roda os fluxos quando carrega.
        static string raizArg, envMotorOriginal;
        internal static void ConfigurarFluxos(MainWindow w, string pasta, string fluxos, string raiz = null)
        {
            raizArg = raiz; envMotorOriginal = Environment.GetEnvironmentVariable("ESTRUCTTO_MOTOR");
            pastaSaida = Path.GetFullPath(pasta);
            Directory.CreateDirectory(pastaSaida);
            foreach (var f in Directory.GetFiles(pastaSaida, "fluxo-*")) File.Delete(f);
            fluxosPedidos = (string.IsNullOrWhiteSpace(fluxos) ? "A,B,C,D,E" : fluxos).ToUpperInvariant().Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            GuardarConfigUsuario();
            Ativo = true;   // modo de teste: sem modais de recuperação, janelas sem animação (como --capturas)
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Left = -10000; w.Top = -10000; w.Width = 1600; w.Height = 900;
            w.ShowActivated = false; w.ShowInTaskbar = false;
            w.Loaded += (s, e) => w.Dispatcher.BeginInvoke(new Action(() => RodarFluxos(w)), DispatcherPriority.ApplicationIdle);
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
        static readonly HashSet<string> motoresVistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // o processo que o Motor.cs de fato abriu (ESTRUCTTO_MOTOR ganha do pacote ao lado do exe e do de %LOCALAPPDATA%; era E2E-08)
        static string ProcessoMotor()
        {
            try
            {
                var p = Priv<Process>(typeof(Motor), "proc"); if (p == null || p.HasExited) return null;
                var si = p.StartInfo;
                return MotorEmpacotado() ? si.FileName + " (empacotado)" : si.FileName + " " + si.Arguments.Split(' ').Take(2).LastOrDefault() + " em " + si.WorkingDirectory + " (fonte)";
            }
            catch { return null; }
        }
        static string SeguroCaminho(Process p) { try { return p.MainModule.FileName; } catch { return "?"; } }
        static bool MotorEmpacotado() { try { var p = Priv<Process>(typeof(Motor), "proc"); return p != null && !p.HasExited && Path.GetFileName(p.StartInfo.FileName).Equals("estructto-motor.exe", StringComparison.OrdinalIgnoreCase); } catch { return false; } }

        // raiz do repositório (exemplos/, codigo/motor/testes/dados_cype): 3º argumento, ESTRUCTTO_MOTOR ou subindo das pastas
        static string Raiz()
        {
            if (!string.IsNullOrEmpty(raizArg)) return Path.GetFullPath(raizArg);
            string env = Environment.GetEnvironmentVariable("ESTRUCTTO_MOTOR");
            if (!string.IsNullOrEmpty(env)) return Path.GetFullPath(Path.Combine(env, "..", ".."));
            foreach (string ini in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
                for (var d = new DirectoryInfo(ini); d != null; d = d.Parent)
                    if (Directory.Exists(Path.Combine(d.FullName, "codigo", "motor", "testes", "dados_cype"))) return d.FullName;
            throw new DirectoryNotFoundException("raiz do repositório não achada: passe-a como 3º argumento de --testes-fluxo");
        }
        static readonly List<string> caixasVistas = new List<string>();   // texto das Caixa (mensagens modais) que apareceram
        static string respostaCaixa;                                      // botão a clicar na próxima Caixa (null = fechar)

        static void RodarFluxos(MainWindow w)
        {
            var sw = Stopwatch.StartNew();
            VigiarCaixas();
            w.DefinirRealista(false);   // determinístico: a vista analítica, como no programa recém-instalado (a config do usuário volta no fim)
            {   // como as capturas: motor do worktree se ninguém disse outro (não no fluxo F, que exige o pacote sozinho)
                string dev = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "..", "motor"));
                if (!fluxosPedidos.Contains("F") && Environment.GetEnvironmentVariable("ESTRUCTTO_MOTOR") == null && Directory.Exists(Path.Combine(dev, "prossen_estrutural"))) Environment.SetEnvironmentVariable("ESTRUCTTO_MOTOR", dev);
            }
            string motor = "ESTRUCTTO_MOTOR = " + (Environment.GetEnvironmentVariable("ESTRUCTTO_MOTOR") ?? "(não definida)");
            var fluxos = new (string Id, string Titulo, Action<MainWindow, Rel> Corpo)[]
            {
                ("A", "galpão do zero", FluxoGalpao), ("B", "edifício de 4 pavimentos", FluxoEdificio), ("C", "arquivos legados e CYPE", FluxoLegado),
                ("D", "edição", FluxoEdicao), ("E", "robustez", FluxoRobustez), ("F", "só o motor empacotado (sem ESTRUCTTO_MOTOR)", FluxoEmpacotado),
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
                        string pm = ProcessoMotor(); if (pm != null) motoresVistos.Add(pm);
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
                sb.AppendLine("Testes de fluxo do Estructto (--testes-fluxo) · " + motor + " · " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
                sb.AppendLine("Processo do motor que rodou: " + (motoresVistos.Count == 0 ? "—" : string.Join(" | ", motoresVistos)));
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
        // Execuções --teste* usam a pasta própria do Config (%TEMP%\Estructto\config-automacao). O empresa.txt do relatório
        // (RelatorioMotor.ArqEmpresa) também (era E2E-11): a suíte confere que o do %AppData% do usuário ficou intacto e o devolve se não.
        // ponytail: não copia/restaura a pasta inteira — outras sessões do usuário gravam cópias de recuperação nela enquanto a suíte roda.
        static string ArqEmpresaUsuario => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NovoFtool", "empresa.txt");
        static byte[] empresaBytes; static DateTime empresaData;
        static void GuardarConfigUsuario()
        {
            var f = new FileInfo(ArqEmpresaUsuario);
            empresaBytes = f.Exists ? File.ReadAllBytes(f.FullName) : null; empresaData = f.Exists ? f.LastWriteTimeUtc : default;
        }
        // true = o arquivo do usuário estava intacto; senão devolve o conteúdo e a data originais
        static bool DevolverConfigUsuario()
        {
            try
            {
                var f = new FileInfo(ArqEmpresaUsuario);
                bool intacto = empresaBytes == null ? !f.Exists : f.Exists && f.LastWriteTimeUtc == empresaData && File.ReadAllBytes(f.FullName).SequenceEqual(empresaBytes);
                if (intacto) return true;
                if (empresaBytes == null) File.Delete(f.FullName);
                else { File.WriteAllBytes(f.FullName, empresaBytes); File.SetLastWriteTimeUtc(f.FullName, empresaData); }
                return false;
            }
            catch { return false; }
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
            r.Checar(DevolverConfigUsuario(), "abrir o relatório na execução automática não grava %AppData%\\NovoFtool\\empresa.txt do usuário");
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
            if (envMotorOriginal != null)
                r.Checar(!MotorEmpacotado(), "ESTRUCTTO_MOTOR definida: o motor da fonte é o que roda (rodou: " + ProcessoMotor() + ")");
            var v = w.vista3D;
            r.Checar(Niveis.Lista(d.Json).Count == 2, "2 níveis (Térreo e 6 m): " + string.Join(", ", Niveis.Lista(d.Json).Select(n => n.Nome + " " + Nf(n.Z))));
            r.Checar(v.PlanoAtual.Normal == 'Z' && Math.Abs(v.PlanoAtual.Valor - H) < 1e-9, "plano de trabalho no nível de 6 m (" + v.PlanoAtual.Nome + ")");
            r.Checar(v.EixosX.Length == 5 && v.EixosY.Length == 2, "a grade do novo projeto aparece na vista (eixos X " + v.EixosX.Length + ", filas Y " + v.EixosY.Length + "; esperado 5 e 2)");
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
            w.GirarDesenho3D(90);   // Ctrl+R: eixo forte no plano do pórtico (YZ), a tesoura chega na mesa do pilar
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
            w.GirarDesenho3D(270);   // volta a 0° para as próximas barras
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

            // --- vigas de beiral com a Barra e atração nos nós (cliques 3 px ao lado) no plano de 6 m, primeiro em planta
            Esperar(w.UsarSecao3D(pBeiral));
            void Beirais(string ondeFoto)
            {
                w.UsarFerramenta3D(Ferramenta3D.Barra); EsperarIdle();
                var atr = new List<string>();
                foreach (double y in ys)
                {
                    foreach (double x in xs) { v.MoverTela(T(x, y, H) + new Vector(3, -2)); atr.Add(v.TipoAtracao + " z=" + Nf(v.PontoCursor?.Z ?? double.NaN)); Esperar(v.ClicarTela(T(x, y, H) + new Vector(3, -2))); AteCond(() => true, 5, w); }
                    v.Tecla(Key.Escape);
                }
                v.Tecla(Key.Escape);
                r.L("atração perto dos topos (3 px ao lado, " + ondeFoto + "): " + string.Join(", ", atr.Distinct()));
            }
            v.MostrarVista(VistaPadrao.Topo, false); EsperarIdle();
            Beirais("planta");
            m = J(w);
            foreach (var kv in m["nos"].AsObject().Where(kv => Math.Abs((double)kv.Value["z"]) < 1e-9 && NoEm(m, (double)kv.Value["x"], (double)kv.Value["y"], H) == null))
                r.L("        nó novo no pé, fora do plano de 6 m: " + kv.Key + " (" + Nf((double)kv.Value["x"], 6) + "; " + Nf((double)kv.Value["y"], 6) + "; " + Nf((double)kv.Value["z"], 6) + ")");
            bool BeiraisNoTopo() { var mm = J(w); var bs = mm["barras"].AsObject().Where(kv => (string)kv.Value["secao"] == pBeiral).ToList(); return bs.Count == 8 && bs.All(kv => Math.Abs(Pos(mm, (string)kv.Value["ni"]).Z - H) < 1e-9 && Math.Abs(Pos(mm, (string)kv.Value["nj"]).Z - H) < 1e-9); }
            r.L("        beirais: " + string.Join("; ", m["barras"].AsObject().Where(kv => (string)kv.Value["secao"] == pBeiral).Select(kv => kv.Key + " " + Pos(m, (string)kv.Value["ni"]) + " → " + Pos(m, (string)kv.Value["nj"]))));
            bool beiralPlanta = NBarras(w) == 28 && NNos(w) == 25 && BeiraisNoTopo();
            r.Checar(beiralPlanta, "planta, plano 6 m: Barra com atração Extremidade nos topos dos pilares liga em z = 6 m (" + NBarras(w) + " barras, " + NNos(w) + " nós, no topo: " + BeiraisNoTopo() + ")");
            if (!beiralPlanta)
            {   // contorno do usuário: desfaz e desenha em isométrica, onde topo e pé não coincidem na tela
                Foto(w, "A-03a-beiral-errado", () => v.MostrarVista(VistaPadrao.Iso, false));
                for (int i = 0; i < 20 && NBarras(w) > 20; i++) { Comando(w, "U"); AteCond(() => true, 5, w); }
                r.Checar(NBarras(w) == 20 && NNos(w) == 25, "Ctrl+Z tirou as barras de beiral erradas (" + NBarras(w) + " barras, " + NNos(w) + " nós)");
                v.MostrarVista(VistaPadrao.Iso, false); v.Enquadrar(false); EsperarIdle();
                Beirais("isométrica");
            }
            m = J(w);
            r.Checar(NBarras(w) == 28 && NNos(w) == 25 && BeiraisNoTopo(), "8 vigas de beiral em z = 6 m pela atração nos nós, sem nó novo (" + NBarras(w) + " barras, " + NNos(w) + " nós)");
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
            // joelho = ponta da tesoura (viga inclinada W310 que nasce no topo do pilar), não o beiral
            var mj = J(w);
            var tesouraNoTopo = new HashSet<string>(mj["barras"].AsObject().Where(kv => (string)kv.Value["secao"] == pViga && Pos(mj, (string)kv.Value["ni"]).Z != Pos(mj, (string)kv.Value["nj"]).Z
                && Math.Min(Pos(mj, (string)kv.Value["ni"]).Z, Pos(mj, (string)kv.Value["nj"]).Z) > H - 1e-9 && Math.Min(Pos(mj, (string)kv.Value["ni"]).Z, Pos(mj, (string)kv.Value["nj"]).Z) < H + 1e-9).Select(kv => kv.Key));
            var gJoelho = grupos.Where(x => ((string)x["tipo"]).StartsWith("viga_pilar")).OrderByDescending(x => x["membros"].AsArray().Count(mb => tesouraNoTopo.Contains((string)mb["barra"]))).FirstOrDefault();
            r.L("        tesouras no topo dos pilares: " + string.Join(", ", tesouraNoTopo) + " · grupo escolhido " + (string)gJoelho?["id"] + ": " + string.Join(", ", gJoelho?["membros"]?.AsArray().Select(mb => (string)mb["barra"] + "/" + (string)mb["ponta"]) ?? new string[0]));
            r.Checar(gJoelho != null, "grupo da ligação pilar–viga (joelho): " + (string)gJoelho?["id"] + " " + (string)gJoelho?["tipo"]);
            if (gJoelho != null)
            {
                var t2 = w.CriarProjetistaGrupo(d, gJoelho, ger); Esperar(t2);
                var jg = t2.Result;
                r.Exigir(jg != null, "projetista do grupo do joelho abriu");
                jg.Width = 1460; jg.Height = 880; PrepararDialogoForaDaTela(jg); jg.Show(); EsperarIdle();
                Esperar(jg.Escolher("chapa_topo_estendida")); EsperarIdle();
                if (jg.Resultado != null && !UiAnalise.Bool(jg.Resultado, "ok")) { Esperar(jg.Otimizar()); EsperarIdle(); }
                FotoDialogo(jg, "A-13-joelho-chapa-topo");
                r.L("        joelho: taxa " + jg.Resultado?["taxa"] + " · " + (string)jg.Resultado?["criterio"] + " · reprovam: " + string.Join(" | ", jg.Resultado?["verificacoes"]?.AsArray().Where(x => !UiAnalise.Bool(x, "ok")).Select(x => (string)x["nome"] + " " + x["solicitante"] + "/" + x["resistente"] + " " + (string)x["unidade"] + " (" + (string)x["item"] + ")") ?? new string[0]));
                r.L("        joelho, dados: " + jg.Resultado?["dados"]?.ToJsonString() + " · esforços: " + jg.Resultado?["esforcos"]?.ToJsonString());
                bool okOtim = jg.Resultado != null && UiAnalise.Bool(jg.Resultado, "ok");
                r.Checar(okOtim, "Otimizar resolve a chapa de topo estendida no joelho (taxa " + jg.Resultado?["taxa"] + ", " + (string)jg.Resultado?["criterio"] + ")");
                if (!okOtim && ((string)jg.Resultado?["criterio"] ?? "").Contains("perna máxima"))
                {   // contorno do usuário: baixa a perna da solda que passou do máximo (a espessura mais fina) à mão
                    var dimin = new JsonObject();
                    foreach (var x in jg.Resultado["verificacoes"].AsArray().Where(x => !UiAnalise.Bool(x, "ok") && ((string)x["nome"]).Contains("perna máxima")))
                        dimin[((string)x["nome"]).Contains("alma") ? "b_alma" : "b_mesa"] = Math.Floor((double)x["resistente"]);
                    r.L("        contorno: " + dimin.ToJsonString());
                    jg.DefinirDados(dimin); Esperar(jg.CalcularAgora()); EsperarIdle();
                }
                r.Checar(jg.Resultado != null && UiAnalise.Bool(jg.Resultado, "ok"), "chapa de topo estendida no joelho passa (pior membro; taxa " + jg.Resultado?["taxa"] + ")");
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
            r.Checar(html.Contains("PROSSEN E2E"), "relatório leva a empresa do novo projeto (config.projeto.empresa = PROSSEN E2E) no carimbo");

            // --- salvar (.estructto), fechar, reabrir e comparar; F5 de novo dá os mesmos números
            d = w.EspacialAtivo;
            string arq = Path.Combine(pasta, "Galpao E2E.estructto");
            string antes = d.Json.ToJsonString(), gradeAntes = d.Json["config"]?["grade"]?.ToJsonString();
            double taxaAntes = TaxaMax(d);
            var taxas0 = d.PiorPorBarra.ToDictionary(kv => kv.Key, kv => DocumentoEspacial.Taxa(kv.Value));
            r.Exigir(w.SalvarEspacialEm(arq), "salvo em " + arq);
            w.FecharEspaciaisSemPerguntar(); EsperarIdle();
            r.Checar(w.EspacialAtivo == null, "documento fechado");
            Esperar(w.AbrirArquivo(arq));
            var d2 = w.EspacialAtivo;
            r.Exigir(d2 != null, "reaberto");
            r.Checar(JsonNode.DeepEquals(JsonNode.Parse(antes), d2.Json), "modelo reaberto igual ao salvo (nós, barras, cargas, ligações e config)");
            r.Checar(gradeAntes != null && d2.Json["config"]?["grade"]?.ToJsonString() == gradeAntes, "grade estrutural (config.grade) voltou");
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
            var casosE = J(w)["casos"].AsObject().Where(kv => (string)kv.Value["tipo"] == "S").Select(kv => kv.Key).ToList();
            string manualAcoes = Path.Combine(Raiz(), "docs", "manual", "03-acoes.md");
            if (File.Exists(manualAcoes) && casosE.Count > 0)
                r.Checar(!File.ReadAllText(manualAcoes).Contains("E (sismo)"), "manual 03-acoes.md diz o mesmo tipo de caso do sismo que o motor gera (\"S\")");
            r.Checar(mudouS && casosE.Count >= 2, "sismo gerado: " + stSismo + " · casos " + string.Join(", ", casosE));

            // --- F5, modal, estabilidade
            Esperar(w.CalcularEspacial());
            d = w.EspacialAtivo;
            r.Exigir(d.Calculado, "F5 calculou (" + w.StatusMsg.Text + ")");
            r.Checar(d.Combinacoes.Any(c => c.Contains("SX")) && d.Combinacoes.Any(c => c.Contains("SY")), "combinações com sismo SX e SY (" + d.Combinacoes.Count() + " combinações)");
            Foto(w, "B-04-aproveitamento", () => v.MostrarVista(VistaPadrao.Iso, false));
            var modal = Esperar(d.Consultar("/api/modal?n=6"));
            var per = modal?["periodos_s"]?.AsArray().Select(x => (double)x).ToList() ?? new List<double>();
            r.Checar(per.Count == 6 && per.All(x => x > 0) && per.Zip(per.Skip(1), (a, b) => a >= b - 1e-12).All(x => x), "modal: 6 períodos positivos e decrescentes (T1 = " + (per.Count > 0 ? Nf(per[0]) : "—") + " s)");
            var jm = new JanelaModal(modal); PrepararDialogoForaDaTela(jm); jm.Owner = w; jm.Show(); EsperarIdle();
            FotoDialogo(jm, "B-05-modal"); jm.Close();
            var est = Esperar(d.Consultar("/api/estabilidade"));
            var gz = (est?["gama_z"] as JsonObject)?.Select(kv => UiAnalise.D(kv.Value, "valor")).Where(x => !double.IsNaN(x)).ToList() ?? new List<double>();
            r.L("estabilidade: " + est?.ToJsonString().Substring(0, Math.Min(400, est?.ToJsonString().Length ?? 0)));
            // aço: o motor usa B2 (NBR 8800 anexo D) no lugar do γz (NBR 6118, só concreto)
            var b2 = (est?["B2"] as JsonObject)?.Select(kv => (kv.Key, UiAnalise.D(kv.Value, "max"))).Where(x => !double.IsNaN(x.Item2)).ToList() ?? new List<(string, double)>();
            r.Checar((bool?)est?["aplicavel"] == true && gz.Count == 0 && b2.Count == 2 && b2.All(x => x.Item2 >= 1 && x.Item2 < 2),
                     "estabilidade global do edifício de aço: B2 (NBR 8800 D.2.3) por direção " + string.Join(", ", b2.Select(x => x.Item1 + " " + Nf(x.Item2))) + "; γz vazio (só concreto)");
            var je = new JanelaEstabilidade(est); PrepararDialogoForaDaTela(je); je.Owner = w; je.Show(); EsperarIdle();
            FotoDialogo(je, "B-06-estabilidade"); je.Close();

            // --- relatório
            string html = RelatorioPdf(w, r, "B-07-relatorio");
            r.Checar(html.Contains("15421") && html.Contains("γ"), "relatório cita a NBR 15421 e a estabilidade (γz)");
        }

        // ================================================================ C) arquivos legados e CYPE
        static void FluxoLegado(MainWindow w, Rel r)
        {
            string raiz = Raiz();
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
                if (nome.EndsWith(".ftl"))
                {   // o .ftl não traz apoios nem cargas (Ftl.cs): o usuário tem de ser avisado ao abrir e o F5 tem de explicar
                    string st = w.StatusMsg.Text;
                    r.Checar(w.EspacialAtivo.Avisos.Any(x => x.Contains("Apoios")) || caixasVistas.Any(x => x.Contains("Apoios")), nome + ": ao abrir, avisa que apoios e cargas do .ftl não são lidos (avisos: " + string.Join("; ", w.EspacialAtivo.Avisos.Take(3)) + ")");
                    r.Checar(!w.EspacialAtivo.Calculado && st.Contains("mecanismo", StringComparison.OrdinalIgnoreCase), nome + ": sem apoios, o F5 recusa e aponta o mecanismo (" + st + ")");
                    r.Checar(!st.Contains("{\""), nome + ": a mensagem do F5 é legível, sem o JSON cru do motor (" + st + ")");
                }
                else r.Checar(w.EspacialAtivo.Calculado, nome + ": F5 (" + w.EspacialAtivo.Resumo() + " · status: " + w.StatusMsg.Text + ")");
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
            bool semResultadosVisto = false, fotoCype = false;
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
                catch (Exception ex)
                {   // obra sem resultados gravados pelo CYPE: o motor recusa; o usuário tem de ler o porquê
                    string det = null; try { det = (string)JsonNode.Parse((ex as MotorErro)?.Corpo ?? "")?["detalhe"]; } catch { }
                    string tela = ComandosAnalise.MensagemMotor(ex);
                    r.Checar(det != null && det.Contains("resultados"), nome + ": comparar recusado pelo motor: " + det);
                    if (!semResultadosVisto) r.Checar(tela.Contains("resultados"), nome + ": a janela Comparar CYPE mostra o motivo (mostra: " + tela + ")");
                    semResultadosVisto = true;
                    continue;
                }
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
                    if ((bool?)cmp["valido"] == true) r.Checar(pior <= 5, txt);
                }
                if ((bool?)cmp["valido"] == true && !fotoCype)
                {
                    fotoCype = true;
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
            v.Tecla(Key.Enter);   // limites escolhidos: Enter (docs/v3/FLUXO-COMANDOS.md)
            Esperar(v.EscolherPorCodigo(v.ObjetoPorId("barra", corte), Q(3, 1.2, 3)));
            AteCond(() => NoEm(J(w), 3, 1.5, 3) == null, 20, w);
            v.Tecla(Key.Enter);
            m = J(w);
            var pc = m["barras"][corte];
            r.Checar(pc != null && new[] { Pos(m, (string)pc["ni"]), Pos(m, (string)pc["nj"]) }.Any(p => (p - Q(3, 0, 3)).Length < 1e-6) && NoEm(m, 3, 1.5, 3) == null, "APARAR: o lado clicado (y > 0) saiu; a barra termina em (3; 0; 3)");
            Desfaz("APARAR", () => NoEm(J(w), 3, 1.5, 3) != null, () => NoEm(J(w), 3, 1.5, 3) == null);
            Comando(w, "EX");
            Esperar(v.EscolherPorCodigo(v.ObjetoPorId("barra", viga), Q(1, 0, 3)));
            v.Tecla(Key.Enter);   // limites escolhidos: Enter (docs/v3/FLUXO-COMANDOS.md)
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
            v.MostrarVista(VistaPadrao.Iso, false); v.Enquadrar(false); EsperarIdle();   // em planta topo e pé do pilar coincidem na tela
            Comando(w, "ME");
            r.Checar(v.Ferramenta == Ferramenta3D.Medir, "ME liga o Medir");
            v.Snap.Rastreio = false;
            void Clique(Point3D p) { v.MoverTela(T(p) + new Vector(2, 2)); Esperar(v.ClicarTela(T(p) + new Vector(2, 2))); EsperarIdle(); }
            Clique(Q(0, 0, 0)); Clique(Q(6, 0, 3));
            r.L("        medir: pontos " + string.Join(" → ", v.Medidor.Itens.Select(x => x.P.ToString() + (x.Barra != null ? " [barra " + x.Barra.Nome + "]" : ""))) + " · " + v.Medidor.Res?.Tipo + " " + v.Medidor.Res?.ValorTxt + " · plano " + v.PlanoAtual.Nome);
            r.Checar(v.Medidor.Res?.ValorTxt == "6,708 m", "MEDIR (0; 0; 0) → (6; 0; 3) = " + v.Medidor.Res?.ValorTxt + " (esperado 6,708 m)");
            Foto(w, "D-05-medir", () => { });
            v.Tecla(Key.Escape);
            var alvoM = J(w)["nos"].AsObject().Select(kv => Pos(J(w), kv.Key)).Where(p => Math.Abs(p.Z - 3) < 1e-9 && p.Y > 1).OrderByDescending(p => p.Y).ThenByDescending(p => p.X).First();
            string espM = ((alvoM - Q(0, 0, 3)).Length).ToString("0.000", CultureInfo.GetCultureInfo("pt-BR")) + " m";
            Clique(Q(0, 0, 3)); Clique(alvoM);
            r.L("        medir 2: pontos " + string.Join(" → ", v.Medidor.Itens.Select(x => x.P.ToString())) + " · " + v.Medidor.Res?.Tipo + " " + v.Medidor.Res?.ValorTxt);
            r.Checar(v.Medidor.Res?.ValorTxt == espM, "MEDIR (0; 0; 3) → " + alvoM + " = " + v.Medidor.Res?.ValorTxt + " (esperado " + espM + ")");
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

            // --- programa normal (processo à parte) com cópia de recuperação no %AppData% do usuário: a janela aparece em até 10 s
            //     (antes o diálogo de recuperação vinha antes do 1º quadro e a janela principal ficava invisível). Depois, --capturas
            //     não pode tocar na recuperação do usuário (antes gravava e apagava cópias lá).
            string exe = Process.GetCurrentProcess().MainModule.FileName;
            string recUsr = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NovoFtool", "recuperacao");
            string chaveSent = "e2e-sentinela-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string sentinela = Path.Combine(recUsr, chaveSent + ".estructto");
            Directory.CreateDirectory(recUsr);
            File.WriteAllText(sentinela, new JsonObject { ["formato"] = "estructto", ["versao"] = 3, ["modelo"] = JsonNode.Parse(jRec) }.ToJsonString(), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(recUsr, chaveSent + ".txt"), "", new UTF8Encoding(false));
            var outros = Process.GetProcessesByName("Estructto").Where(x => x.Id != Environment.ProcessId).Select(x => x.Id + " " + Path.GetDirectoryName(SeguroCaminho(x))).ToList();
            r.L("outras sessões do Estructto abertas (podem gravar na recuperação do usuário): " + (outros.Count == 0 ? "nenhuma" : string.Join("; ", outros)));
            try
            {
                var psiN = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) };
                psiN.Environment.Remove("ESTRUCTTO_MOTOR");
                var pn = Process.Start(psiN);
                var swN = Stopwatch.StartNew(); string titulo = "";
                try
                {
                    AteCond(() => { pn.Refresh(); titulo = pn.MainWindowTitle; return pn.HasExited || titulo != ""; }, 10);
                    double tJan = swN.Elapsed.TotalSeconds;
                    r.Checar(!pn.HasExited && titulo != "", "programa normal com cópia de recuperação: janela principal visível (MainWindowTitle = \"" + titulo + "\") em " + tJan.ToString("0.0", CultureInfo.InvariantCulture) + " s (limite 10 s)");
                }
                finally { try { if (!pn.HasExited) { pn.Kill(true); pn.WaitForExit(10000); } } catch { } }
                r.Checar(File.Exists(sentinela), "matar o programa no diálogo de recuperação não apaga a cópia");

                (string, long, DateTime)[] Foto() => Directory.GetFiles(recUsr).Select(f => new FileInfo(f)).Select(f => (f.Name, f.Length, f.LastWriteTimeUtc)).OrderBy(x => x.Name, StringComparer.Ordinal).ToArray();
                var antesR = Foto();
                string pastaCap = Path.Combine(Path.GetTempPath(), "estructto-e2e-capturas-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                var swC = Stopwatch.StartNew();
                Directory.CreateDirectory(pastaCap);
                string modeloCap = Path.Combine(Raiz(), "exemplos", "guincho-com-placa.estructto");
                var psiC = new ProcessStartInfo(exe, "--capturas \"" + pastaCap + "\" \"" + modeloCap + "\"") { UseShellExecute = false, WorkingDirectory = Path.Combine(Raiz(), "codigo", "Estructto"), RedirectStandardError = true };
                psiC.Environment.Remove("ESTRUCTTO_MOTOR");
                var errC = new StringBuilder();
                var pc = Process.Start(psiC);
                pc.ErrorDataReceived += (s2, e2) => { if (e2.Data != null && !e2.Data.Contains("Chrome_Widget")) lock (errC) errC.AppendLine(e2.Data); };
                pc.BeginErrorReadLine();
                bool terminou = AteCond(() => pc.HasExited, 900);
                if (!terminou) { try { pc.Kill(true); } catch { } }
                var depoisR = Foto();
                var mudou = antesR.Select(x => x.Item1).Except(depoisR.Select(x => x.Item1)).Select(x => "−" + x)
                    .Concat(depoisR.Select(x => x.Item1).Except(antesR.Select(x => x.Item1)).Select(x => "+" + x))
                    .Concat(antesR.Join(depoisR, x => x.Item1, x => x.Item1, (x, y) => (x, y)).Where(t => t.x.Item2 != t.y.Item2 || t.x.Item3 != t.y.Item3).Select(t => "~" + t.x.Item1)).ToList();
                int pngs = Directory.Exists(pastaCap) ? Directory.GetFiles(pastaCap, "*.png", SearchOption.AllDirectories).Length : 0;
                r.L("--capturas: " + (terminou ? "terminou" : "não terminou em 900 s") + " em " + swC.Elapsed.TotalSeconds.ToString("0", CultureInfo.InvariantCulture) + " s, código " + (terminou ? pc.ExitCode.ToString() : "—") + ", " + pngs + " PNG em " + pastaCap);
                string errosCap = Path.Combine(pastaCap, "erros.txt");
                if (File.Exists(errosCap)) r.L("        erros.txt: " + File.ReadAllText(errosCap).Substring(0, Math.Min(600, (int)new FileInfo(errosCap).Length)));
                if (errC.Length > 0) r.L("        stderr: " + errC.ToString().Substring(0, Math.Min(600, errC.Length)));
                r.Checar(terminou && pngs > 0, "--capturas rodou até o fim (" + pngs + " capturas)");
                r.Checar(mudou.Count == 0, "--capturas não tocou em %AppData%\\NovoFtool\\recuperacao (" + antesR.Length + " arquivos" + (mudou.Count > 0 ? "; mudou: " + string.Join(", ", mudou.Take(8)) : ", iguais") + ")");
                if (terminou && pc.ExitCode == 0) try { Directory.Delete(pastaCap, true); } catch { }
            }
            finally
            {
                try { File.Delete(sentinela); File.Delete(Path.Combine(recUsr, chaveSent + ".txt")); } catch { }
            }

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
            // morre DURANTE o cálculo: no modelo grande o F5 leva segundos, o kill cai dentro dele (no galpão o F5 às vezes acabava antes)
            {
                var t = w.CalcularEspacial();
                r.Checar(AteCond(() => w.OcupadoEsp, 10), "F5 do modelo grande em andamento");
                bool emCurso = !t.IsCompleted;
                Matar();
                Esperar(t);
                string msg = w.StatusMsg.Text;
                r.L("motor morto durante o F5 (em curso: " + emCurso + "): status = " + msg + " · calculado = " + w.EspacialAtivo.Calculado);
                r.Checar(emCurso, "o kill caiu durante o F5");
                // o modelo tem barras e casos: a mensagem não pode dizer que não tem (o motor novo sobe vazio e recebe o F5 sem o modelo)
                bool msgEngana = msg.Contains("não tem barras") || msg.Contains("sem_barras");
                bool msgClara = !msgEngana && msg.Contains("motor", StringComparison.OrdinalIgnoreCase) && (msg.Contains("encerr") || msg.Contains("reinici") || msg.Contains("parou") || msg.Contains("de novo"));
                r.Checar(msgClara, "motor morto durante o F5: a mensagem diz que o motor parou, sem acusar o modelo (status: " + msg.Substring(0, Math.Min(200, msg.Length)) + ")");
                Esperar(w.CalcularEspacial());
                r.Checar(w.EspacialAtivo.Calculado, "F5 seguinte recupera o modelo grande (" + w.StatusMsg.Text + ")");
            }
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

        // ================================================================ F) só o motor empacotado
        // O pacote (motor\estructto-motor.exe, PyInstaller) já saiu sem dados, exemplos e módulos (erro "interno" no .ed3, catálogo
        // vazio, exemplos e ligações sumidos) e os fluxos A–E, rodando com ESTRUCTTO_MOTOR, não viam. Este fluxo roda sem a variável
        // e exige o processo empacotado: catálogo, 19 tipos de ligação, exemplos, cada modelo do novo projeto, .ed3 real, desenho,
        // ligações, placa de base e relatório.
        static void FluxoEmpacotado(MainWindow w, Rel r)
        {
            r.Exigir(envMotorOriginal == null && Environment.GetEnvironmentVariable("ESTRUCTTO_MOTOR") == null, "ESTRUCTTO_MOTOR não definida (rode o fluxo F sem ela)");
            Esperar(Motor.Get("/api/exemplos"));
            r.L("processo do motor: " + ProcessoMotor());
            r.Exigir(MotorEmpacotado(), "o motor que respondeu é o empacotado (estructto-motor.exe): " + ProcessoMotor());

            // --- dados do pacote: catálogo, materiais, tipos de ligação (cada um com o próprio exemplo), memorial
            foreach (string f in new[] { "", "W310", "Ue 200", "U 50x25", "HP250" })
            {
                int n = Esperar(Motor.Get("/api/catalogo?filtro=" + Uri.EscapeDataString(f))).AsArray().Count;
                r.Checar(f == "" ? n >= 190 : n > 0, "catálogo do pacote: " + n + " perfis com \"" + f + "\"" + (f == "" ? " (fonte: 199; W, U, Ue, HP)" : ""));
            }
            var tipos = Esperar(Motor.Get("/api/ligacoes/tipos")).AsArray();
            r.Checar(tipos.Count >= 19, tipos.Count + " tipos de ligação no pacote (fonte: 19)");
            int okTipos = 0; var errosTipos = new List<string>();
            foreach (var t in tipos)
            {
                string tp = (string)t["tipo"];
                try
                {
                    var ex = t["exemplo"];
                    var res = Esperar(Motor.Post("/api/ligacoes/verificar", new JsonObject { ["tipo"] = tp, ["dados"] = ex?["dados"]?.DeepClone() ?? new JsonObject(), ["esforcos"] = ex?["esforcos"]?.DeepClone() ?? new JsonObject() }));
                    if (res?["taxa"] != null) okTipos++; else errosTipos.Add(tp + ": sem taxa");
                }
                catch (Exception e) { errosTipos.Add(tp + ": " + e.Message); }
            }
            r.Checar(okTipos == tipos.Count, "cada tipo de ligação verifica o próprio exemplo no pacote (" + okTipos + " de " + tipos.Count + ")" + (errosTipos.Count > 0 ? ": " + string.Join(" | ", errosTipos.Take(4)) : ""));
            try
            {
                var t0 = tipos.First(x => (string)x["tipo"] == "chapa_topo_estendida");
                var mem = Esperar(Motor.Post("/api/ligacoes/memorial", new JsonObject { ["tipo"] = "chapa_topo_estendida", ["dados"] = t0["exemplo"]["dados"].DeepClone(), ["esforcos"] = t0["exemplo"]["esforcos"].DeepClone() }));
                r.Checar(mem != null && mem.ToJsonString().Length > 500, "memorial da chapa de topo pelo pacote (" + (mem?.ToJsonString().Length ?? 0) + " caracteres)");
            }
            catch (Exception e) { r.Checar(false, "memorial da chapa de topo pelo pacote: " + e.Message); }

            // --- novo projeto com cada modelo da galeria (passo 3 com a grade padrão do modelo)
            string pasta = Path.Combine(pastaSaida, "fluxo-F-projetos");
            if (Directory.Exists(pasta)) Directory.Delete(pasta, true);
            foreach (var mi in ModeloInicial.Todos)
            {
                w.FecharEspaciaisSemPerguntar(); EsperarIdle();
                var dados = new NovoProjetoDados { Modelo = mi, Grade = mi.GradePadrao(), Nome = "F " + mi.Chave, Pasta = Path.Combine(pasta, mi.Chave), Cliente = "Cliente E2E", Engenheiro = "Eng. E2E",
                                                   Empresa = "PROSSEN E2E", Material = mi.Material, Pilar = mi.Pilar, Viga = mi.Viga, Normas = new List<string> { "NBR 8800" } };
                bool criou; try { criou = Esperar(Inicio.Acoes.Novo(w, dados)); } catch (Exception e) { r.Checar(false, "novo projeto \"" + mi.Titulo + "\": " + e.Message); continue; }
                var d = w.EspacialAtivo;
                if (!r.Checar(criou && d != null, "novo projeto \"" + mi.Titulo + "\" criado (" + w.StatusMsg.Text + ")")) continue;
                if (mi.Gerador == null) { r.Checar(NBarras(w) == 0 && w.vista3D.EixosX.Length > 0, "\"" + mi.Titulo + "\": só grade (" + w.vista3D.EixosX.Length + " eixos X)"); continue; }
                r.Checar(NBarras(w) > 0, "\"" + mi.Titulo + "\": " + NBarras(w) + " barras geradas pelo pacote");
                Esperar(w.CalcularEspacial());
                r.Checar(w.EspacialAtivo.Calculado && w.EspacialAtivo.PiorPorBarra.Count == NBarras(w), "\"" + mi.Titulo + "\": F5 no pacote (" + w.EspacialAtivo.Resumo() + ")");
                Foto(w, "F-novo-" + mi.Chave, () => w.vista3D.MostrarVista(VistaPadrao.Iso, false));
            }

            // --- desenhar barras (linha de comando, coordenadas digitadas) num modelo em branco e calcular
            w.FecharEspaciaisSemPerguntar(); EsperarIdle();
            Esperar(w.NovoDocumento(null, "Desenho F"));
            r.Exigir(w.EspacialAtivo != null, "modelo em branco");
            Esperar(w.UsarSecao3D(PerfilCatalogo("W250", "W250x32.7", "W250x25.3")));
            Comando(w, "L"); Comando(w, "0,0,0"); Comando(w, "@0,0,4"); AteCond(() => NBarras(w) == 1, 20, w);
            Comando(w, "@6,0,0"); AteCond(() => NBarras(w) == 2, 20, w);
            Comando(w, "@0,0,-4"); AteCond(() => NBarras(w) == 3, 20, w);
            w.vista3D.Tecla(Key.Escape); w.vista3D.Tecla(Key.Escape);
            var m = J(w);
            r.Exigir(NBarras(w) == 3 && NNos(w) == 4, "pórtico desenhado pela linha de comando (" + NBarras(w) + " barras, " + NNos(w) + " nós)");
            foreach (var pe in new[] { NoEm(m, 0, 0, 0), NoEm(m, 6, 0, 0) })
                Esperar(w.EditarEspacial("no", pe, new JsonObject { ["apoio"] = new JsonArray(true, true, true, true, true, true) }));
            string viga = BarraEntre(J(w), NoEm(J(w), 0, 0, 4), NoEm(J(w), 6, 0, 4));
            Esperar(w.OperarEspacial(dd => dd.ComJanela(async () =>
            {
                await Motor.Post("/api/modelo/caso", new JsonObject { ["nome"] = "G1", ["tipo"] = "G" });
                await Motor.Chamar("PATCH", "/api/config", new JsonObject { ["peso_proprio"] = "G1" });
            }), "teste: caso G1"));
            Esperar(w.OperarEspacial(dd => dd.Lote(new JsonArray(new JsonObject { ["tipo"] = "carga_distribuida", ["dados"] = new JsonObject { ["caso"] = "G1", ["barra"] = viga, ["q"] = new JsonArray(0.0, 0.0, -10.0) } })), "teste: carga"));
            Esperar(w.CalcularEspacial());
            r.Checar(w.EspacialAtivo.Calculado, "pórtico desenhado: F5 no pacote (" + w.StatusMsg.Text + ")");
            var reac = Esperar(w.EspacialAtivo.Consultar("/api/resultados?alvo=reacoes"));
            r.L("reações: " + reac?.ToJsonString().Substring(0, Math.Min(300, reac?.ToJsonString().Length ?? 0)));
            Foto(w, "F-desenho", () => w.vista3D.MostrarVista(VistaPadrao.Iso, false));

            // --- .ed3 real do CYPE com resultados gravados: importar, F5 e comparar
            string ed3 = Path.Combine(Raiz(), "codigo", "motor", "testes", "dados_cype", "PIPERACK TESTE 4.ed3");
            w.FecharEspaciaisSemPerguntar(); EsperarIdle();
            Esperar(w.AbrirArquivo(ed3));
            var dc = w.EspacialAtivo;
            if (r.Checar(dc != null && dc.Json["barras"].AsObject().Count > 0, "PIPERACK TESTE 4.ed3 importado pelo pacote (" + (dc == null ? w.StatusMsg.Text : dc.Json["barras"].AsObject().Count + " barras") + ")"))
            {
                Esperar(w.CalcularEspacial());
                r.Checar(w.EspacialAtivo.Calculado, "PIPERACK TESTE 4.ed3: F5 (" + w.StatusMsg.Text + ")");
                try
                {
                    var cmp = Esperar(Motor.Post("/api/comparar_cype", new JsonObject { ["nome"] = Path.GetFileName(ed3), ["conteudo_base64"] = Convert.ToBase64String(File.ReadAllBytes(ed3)) }));
                    var lin = cmp["tabela"]?.AsArray().Where(x => (string)x["grandeza"] == "desloc_max" && x["cype"] != null && x["nosso"] != null).ToList() ?? new List<JsonNode>();
                    double dif = lin.Select(x => Math.Abs((double)x["cype"] - (double)x["nosso"]) / Math.Max(1e-12, Math.Abs((double)x["cype"]))).DefaultIfEmpty(double.NaN).Max() * 100;
                    r.Checar(lin.Count > 0 && dif <= 5, "comparar CYPE pelo pacote: deslocamento máximo a " + Nf(dif, 2) + " % do CYPE");
                }
                catch (Exception e) { r.Checar(false, "comparar CYPE pelo pacote: " + e.Message); }
            }

            // --- exemplos do motor: abrir e calcular cada um
            var exemplos = Esperar(Motor.Get("/api/exemplos")).AsArray().Select(x => x is JsonObject o ? (string)o["nome"] : (string)x).ToList();
            r.Checar(exemplos.Count >= 4, exemplos.Count + " exemplos no pacote: " + string.Join(", ", exemplos));
            foreach (string ex in exemplos)
            {
                w.FecharEspaciaisSemPerguntar(); EsperarIdle();
                try { Esperar(w.AbrirEspacial(() => DocumentoEspacial.Exemplo(ex), "exemplo")); } catch (Exception e) { r.Checar(false, "exemplo " + ex + ": " + e.Message); continue; }
                if (!r.Checar(w.EspacialAtivo != null && NBarras(w) > 0, "exemplo " + ex + " abriu (" + (w.EspacialAtivo == null ? w.StatusMsg.Text : NBarras(w) + " barras") + ")")) continue;
                Esperar(w.CalcularEspacial());
                r.Checar(w.EspacialAtivo.Calculado, "exemplo " + ex + ": F5 (" + w.EspacialAtivo.Resumo() + ")");
            }

            // --- ligações no galpão de exemplo: grupos, dimensionar todos, projetista do joelho, placa de base, relatório
            w.FecharEspaciaisSemPerguntar(); EsperarIdle();
            Esperar(w.AbrirEspacial(() => DocumentoEspacial.Exemplo("galpao_aco"), "exemplo"));
            Esperar(w.CalcularEspacial());
            var dg = w.EspacialAtivo;
            r.Exigir(dg != null && dg.Calculado, "galpao_aco calculado");
            var ger = w.NovoGerenciadorLigacoes(dg); ger.Width = 1320; ger.Height = 780;
            PrepararDialogoForaDaTela(ger); ger.Show(); EsperarIdle();
            Esperar(ger.Carregar(true)); EsperarIdle();
            var grupos = Esperar2(dg.Ligacoes("GET", "/api/ligacoes/grupos")).AsArray();
            r.Checar(grupos.Count > 0, grupos.Count + " grupos de ligações no galpão de exemplo: " + string.Join(", ", grupos.Select(x => (string)x["tipo"]).Distinct()));
            FotoDialogo(ger, "F-ligacoes-grupos");
            var errosDim = new List<string>(); int okDim = 0;
            foreach (var g in grupos)
            {
                string gid = (string)g["id"];
                try { var dim = Esperar2(dg.Ligacoes("POST", "/api/ligacoes/grupos/" + gid + "/dimensionar")); if (dim?["governante"] != null || dim?["membros"] != null) okDim++; else errosDim.Add(gid + ": resposta sem governante/membros"); }
                catch (Exception e) { errosDim.Add(gid + " " + (string)g["tipo"] + ": " + ComandosAnalise.MensagemMotor(e) + " " + e.Message.Substring(0, Math.Min(160, e.Message.Length))); }
            }
            r.Checar(errosDim.Count == 0, "dimensionar cada grupo pelo pacote (" + okDim + " de " + grupos.Count + ")" + (errosDim.Count > 0 ? ": " + string.Join(" | ", errosDim.Take(4)) : ""));
            var gViga = grupos.FirstOrDefault(x => ((string)x["tipo"]).StartsWith("viga_pilar"));
            if (r.Checar(gViga != null, "grupo viga–pilar no galpão de exemplo (" + (string)gViga?["id"] + ")"))
            {
                var t2 = w.CriarProjetistaGrupo(dg, gViga, ger); Esperar(t2);
                var jg = t2.Result;
                if (r.Checar(jg != null, "projetista do grupo abriu"))
                {
                    jg.Width = 1460; jg.Height = 880; PrepararDialogoForaDaTela(jg); jg.Show(); EsperarIdle();
                    Esperar(jg.Escolher("chapa_topo_estendida")); EsperarIdle();
                    if (jg.Resultado != null && !UiAnalise.Bool(jg.Resultado, "ok")) { Esperar(jg.Otimizar()); EsperarIdle(); }
                    r.Checar(jg.Resultado?["taxa"] != null, "chapa de topo estendida calculada pelo pacote (taxa " + jg.Resultado?["taxa"] + ", " + (string)jg.Resultado?["criterio"] + ")");
                    FotoDialogo(jg, "F-projetista");
                    jg.Close();
                }
            }
            var gBase = grupos.FirstOrDefault(x => (string)x["tipo"] == "base");
            if (r.Checar(gBase != null, "grupo de bases no galpão de exemplo"))
            {
                var dimB = Esperar2(dg.Ligacoes("POST", "/api/ligacoes/grupos/" + (string)gBase["id"] + "/dimensionar"));
                var jpl = w.PlacaDoGrupo(dg, gBase, dimB, out No nb);
                if (r.Checar(jpl != null, "placa de base do grupo abriu"))
                {
                    PrepararDialogoForaDaTela(jpl); jpl.Show(); EsperarIdle();
                    jpl.DimensionarTudo(); EsperarIdle();
                    r.Checar(jpl.ResultadoMotor.Valido, "placa de base pelo pacote (taxa " + Nf(jpl.ResultadoMotor.Taxa) + ", " + jpl.Mensagem + ")");
                    jpl.Close();
                }
            }
            ger.Close(); EsperarIdle();
            RelatorioPdf(w, r, "F-relatorio");
        }

        // ================================================================ interferência (docs/v3/RENDER.md)
        // Estructto.exe --testes-interferencia <arquivo | galpao> [pasta]: abre o modelo (galpao = o do assistente de novo
        // projeto), identifica as ligações, monta o Realista com as ligações do motor e lista os pares de sólidos (barras,
        // chapas, cantoneiras) que se interpenetram mais de 1 mm (Render3DGeometria.Interferencias). Grava interferencia.txt,
        // o gerenciador de ligações (todos os nós, com os sem ligação) e close-ups: viga-pilar, gusset, placa de base e
        // pilar caixão/tubo. Sai com 0 = nenhum par.
        internal static void ConfigurarInterferencia(MainWindow w, string arquivo, string pasta)
        {
            pastaSaida = Path.GetFullPath(pasta ?? Path.Combine(Path.GetTempPath(), "estructto-interferencia"));
            Directory.CreateDirectory(pastaSaida);
            GuardarConfigUsuario();
            Ativo = true;
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Left = -10000; w.Top = -10000; w.Width = 1600; w.Height = 900;
            w.ShowActivated = false; w.ShowInTaskbar = false;
            w.Loaded += (s, e) => w.Dispatcher.BeginInvoke(new Action(() => RodarInterferencia(w, arquivo)), DispatcherPriority.ApplicationIdle);
        }

        static void RodarInterferencia(MainWindow w, string arquivo)
        {
            var sb = new StringBuilder(); int pares = -1;
            try
            {
                sb.AppendLine("Interferência · " + arquivo + " · ESTRUCTTO_MOTOR = " + (Environment.GetEnvironmentVariable("ESTRUCTTO_MOTOR") ?? "(não definida)"));
                VigiarCaixas();
                if (arquivo == "galpao")
                {
                    var mi = ModeloInicial.Por("galpao");
                    var g = new GradeNiveis { X = GradeNiveis.Espacamentos(mi.EixosX), Y = GradeNiveis.Espacamentos(mi.EixosY), Z = GradeNiveis.Cotas(mi.Niveis) };
                    Esperar(Inicio.Acoes.Novo(w, new NovoProjetoDados { Modelo = mi, Grade = g, Nome = "galpao-interferencia", Pasta = Path.Combine(pastaSaida, "projeto"), Material = mi.Material, Pilar = mi.Pilar, Viga = mi.Viga }));
                }
                else Esperar(w.AbrirArquivo(Path.GetFullPath(arquivo)));
                EsperarIdle();
                var d = w.EspacialAtivo ?? throw new InvalidOperationException("modelo não abriu");
                // gerenciador: identifica e lista todos os nós (os sem ligação com o motivo)
                var ger = w.NovoGerenciadorLigacoes(d); ger.Width = 1320; ger.Height = 780;
                PrepararDialogoForaDaTela(ger); ger.Show(); EsperarIdle();
                Esperar(ger.Carregar(true)); EsperarIdle();
                sb.AppendLine("gerenciador: " + ger.Resumo);
                CapturarDialogo(ger, "interf-gerenciador.png");
                ger.Close(); EsperarIdle();

                w.DefinirRealista(true); w.Atualizar3D(false); EsperarIdle();
                Esperar(w.TarefaLigacoes3D); EsperarIdle();
                var v = w.vista3D;
                if (!v.UsandoDx) throw new InvalidOperationException("Render3D (Direct3D 11) indisponível: sem placa de vídeo");
                sb.AppendLine("ligações na vista: " + v.ModeloAtual.Ligacoes.Count + " (com pontas do motor: " + v.ModeloAtual.Ligacoes.Count(l => l.Ponta != null) + ")");
                Render3D.Coleta = new List<(string, MalhaDx)>();
                v.DefinirLigacoes(v.ModeloAtual.Ligacoes); EsperarIdle();   // remonta a cena guardando os sólidos
                var todos = Render3D.Coleta; Render3D.Coleta = null;
                var col = todos.Where(x => !x.Nome.StartsWith("parafuso") && !x.Nome.StartsWith("excluir")).ToList();   // parafusos atravessam as chapas; barras curtas marcadas pelo motor (G5)
                sb.AppendLine("fora do teste: " + todos.Count(x => x.Nome.StartsWith("excluir")) + " sólidos marcados excluir_interferencia");
                var sw = Stopwatch.StartNew();
                var r = Render3DGeometria.Interferencias(col.Select(x => x.M).ToList());
                pares = r.Count;
                sb.AppendLine(col.Count + " sólidos · " + pares + " pares com interseção > 1 mm · " + sw.ElapsedMilliseconds + " ms");
                var porId = v.ModeloAtual.Barras.Where(b => b.Origem is RefMotor).ToDictionary(b => "barra " + ((RefMotor)b.Origem).Id);
                string Ponta(Barra3D b, int no)   // diagnóstico: corte e ligação da ponta de b no nó
                {
                    bool emI = b.Ni == no; Render3D.CortesColeta.TryGetValue((b, emI), out Corte k); Render3D.LigsColeta.TryGetValue((b.Id, emI), out Ligacao3D l);
                    string F(double x) => (x * 1000).ToString("0.#", CultureInfo.InvariantCulture);
                    return ((RefMotor)b.Origem).Id + (emI ? ".i" : ".j") + ": " + (k == null ? "sem corte" : "recuo " + F(k.Recuo) + (k.Motor ? " motor" : "") + (k.Extra != null ? " +angulo" : "") + (k.Raio > 0 ? " +boca" : "") + (k.Esquadria ? " esquadria" : ""))
                           + (l == null ? "" : " · " + l.Nome + (l.Pendente != null ? " [local]" : "") + " " + string.Join(",", (l.Ponta?.Recortes ?? l.Recortes).Select(x => x.Tipo + (x.Tipo == "cope" ? ":" + x.Posicao + " " + x.Altura.ToString("0") + "x" + x.Comprimento.ToString("0") : double.IsNaN(x.XPonta) ? " f" + x.Folga.ToString("0.#") : " x" + x.XPonta.ToString("0.#")))));
                }
                foreach (var (a, b, p) in r.OrderByDescending(x => x.Prof))
                {
                    sb.AppendLine("  " + (p * 1000).ToString("0.0", CultureInfo.InvariantCulture) + " mm  " + col[a].Nome + "  ×  " + col[b].Nome);
                    if (porId.TryGetValue(col[a].Nome, out Barra3D ba) && porId.TryGetValue(col[b].Nome, out Barra3D bb))
                        foreach (int no in new[] { ba.Ni, ba.Nj }.Intersect(new[] { bb.Ni, bb.Nj })) sb.AppendLine("      " + Ponta(ba, no) + "  |  " + Ponta(bb, no));
                    foreach (var (bar, lig) in new[] { (col[a].Nome, col[b].Nome), (col[b].Nome, col[a].Nome) })
                    {
                        var mn = System.Text.RegularExpressions.Regex.Match(lig, " nó (\\S+) ");
                        if (!porId.TryGetValue(bar, out Barra3D bx) || !mn.Success) continue;
                        var nv = v.ModeloAtual.Nos.FirstOrDefault(n => n.Origem is RefMotor rm && rm.Id == mn.Groups[1].Value);
                        if (nv != null && (bx.Ni == nv.Id || bx.Nj == nv.Id)) sb.AppendLine("      " + Ponta(bx, nv.Id));
                    }
                }

                // close-ups: o 1º nó de cada caso
                var json = d.Json; var grupos = Esperar2(d.Ligacoes("GET", "/api/ligacoes/grupos")).AsArray();
                var idNo = new Dictionary<string, int>(); foreach (var kv in json["nos"].AsObject()) idNo[kv.Key] = idNo.Count + 1;
                string NoDe(Func<JsonNode, bool> f) => grupos.Where(f).SelectMany(x => x["membros"].AsArray()).Select(m => (string)m["no"]).FirstOrDefault();
                string Tipo(string sec) => (string)json["secoes"]?[sec ?? ""]?["props"]?["tipo"] ?? "";
                string caixao = json["barras"].AsObject().Where(kv => Tipo((string)kv.Value["secao"]) is "dupla" or "tubo_circular" or "tubo_retangular").Select(kv => (string)kv.Value["nj"]).FirstOrDefault();
                string noPar = r.Select(p => System.Text.RegularExpressions.Regex.Match(col[p.A].Nome + " " + col[p.B].Nome, " nó (\\S+) ")).Where(m => m.Success).Select(m => m.Groups[1].Value).FirstOrDefault();
                foreach (var (nome, no) in new[] { ("pior-par", noPar), ("viga-pilar", NoDe(x => ((string)x["tipo"]).StartsWith("viga_pilar"))), ("gusset", NoDe(x => (string)x["tipo"] == "gusset_contraventamento")),
                                                   ("placa-base", NoDe(x => (string)x["tipo"] == "base")), ("caixao-tubo", caixao) })
                {
                    if (no == null || json["nos"]?[no] == null) { sb.AppendLine("captura " + nome + ": nenhum nó"); continue; }
                    var pn = Pos((JsonObject)json, no);
                    v.Focar(pn, nome == "placa-base" ? 1.4 : 2.0, -40, 22);
                    CapturarPrincipal(w, "interf-" + nome);
                    sb.AppendLine("captura " + nome + ": nó " + no + " (" + Nf(pn.X) + ", " + Nf(pn.Y) + ", " + Nf(pn.Z) + ")");
                }
            }
            catch (Exception ex) { sb.AppendLine("ERRO: " + ex); pares = -1; }
            finally
            {
                sb.AppendLine(pares == 0 ? "0 PARES" : pares > 0 ? pares + " PAR(ES)" : "FALHOU");
                File.WriteAllText(Path.Combine(pastaSaida, "interferencia.txt"), sb.ToString(), new UTF8Encoding(false));
                DevolverConfigUsuario();
                w.alterado = false;
                try { w.FecharEspaciaisSemPerguntar(); } catch { }
                Environment.Exit(pares == 0 ? 0 : 1);
            }
        }
    }
}
