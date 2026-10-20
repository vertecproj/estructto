// Modo de capturas automaticas de tela para QA visual (--capturas <pasta_saida> [arquivo.estructto]).
// Abre o modelo, calcula (F5), renderiza a janela principal (1600x900 e 1280x720) e todos os dialogos/abas
// fora da tela com RenderTargetBitmap (96 dpi) + PngBitmapEncoder, e encerra com codigo 0 (ou 1 + erros.txt).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Portico;
using F = Fluent;

namespace NovoFtool
{
    public static partial class Capturas
    {
        public static bool Ativo { get; private set; }
        static string pastaSaida;
        static string arquivoEntrada;

        public static bool Configurar(MainWindow w, string[] args)
        {
            if (args == null) return false;
            for (int i = 1; i < args.Length; i++)
            {
                string a = args[i] ?? "";
                if (string.Equals(a, "--capturas", StringComparison.OrdinalIgnoreCase))
                {
                    pastaSaida = i + 1 < args.Length ? args[i + 1] : null;
                    if (i + 2 < args.Length && !args[i + 2].StartsWith("--"))
                        arquivoEntrada = args[i + 2];
                    Ativo = true;
                    break;
                }
                if (a.StartsWith("--capturas=", StringComparison.OrdinalIgnoreCase))
                {
                    pastaSaida = a.Substring("--capturas=".Length).Trim('"');
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                        arquivoEntrada = args[i + 1];
                    Ativo = true;
                    break;
                }
            }
            if (!Ativo) return false;

            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Left = -10000;
            w.Top = -10000;
            w.Width = 1600;
            w.Height = 900;
            w.ShowActivated = false;
            w.ShowInTaskbar = false;
            w.Loaded += (s, e) => w.Dispatcher.BeginInvoke(new Action(() => Executar(w)), DispatcherPriority.ApplicationIdle);
            return true;
        }

        public static void GravarErro(Exception ex)
        {
            try
            {
                string dir = string.IsNullOrWhiteSpace(pastaSaida) ? Environment.CurrentDirectory : pastaSaida;
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "erros.txt"), ex.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }

        static string ResolverCaminhoModelo(string arq)
        {
            string padraoRel = Path.Combine("..", "..", "exemplos", "guincho-com-placa.estructto");
            if (!string.IsNullOrWhiteSpace(arq))
            {
                if (File.Exists(arq)) return Path.GetFullPath(arq);
                string p1 = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, arq));
                if (File.Exists(p1)) return p1;
                string p2 = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, arq));
                if (File.Exists(p2)) return p2;
                throw new FileNotFoundException("Arquivo de modelo nao encontrado: " + arq, arq);
            }

            var bases = new List<string> { Environment.CurrentDirectory, AppContext.BaseDirectory };
            foreach (string b in bases)
            {
                try
                {
                    string c = Path.GetFullPath(Path.Combine(b, padraoRel));
                    if (File.Exists(c)) return c;
                }
                catch { }

                DirectoryInfo d = new DirectoryInfo(b);
                while (d != null)
                {
                    string c1 = Path.Combine(d.FullName, "exemplos", "guincho-com-placa.estructto");
                    if (File.Exists(c1)) return c1;
                    string c2 = Path.Combine(d.FullName, "codigo", "Estructto", padraoRel);
                    try { if (File.Exists(c2)) return Path.GetFullPath(c2); } catch { }
                    d = d.Parent;
                }
            }
            throw new FileNotFoundException("Modelo padrao guincho-com-placa.estructto nao encontrado.");
        }

        static void Esperar(System.Threading.Tasks.Task t)
        {
            var limite = DateTime.Now.AddMinutes(5);
            while (!t.IsCompleted && DateTime.Now < limite) { EsperarIdle(); System.Threading.Thread.Sleep(15); }
            if (!t.IsCompleted) throw new TimeoutException("tarefa do motor não terminou em 5 min");
            t.GetAwaiter().GetResult();
        }

        // Pórtico espacial 2 x 2 vãos, 2 pavimentos (entrada neutra da Vista3D), com aproveitamento e deslocamentos inventados.
        static Modelo3D Portico2x2()
        {
            var esp = new Modelo3D(); int idn = 0;
            var rnd = new Random(7);
            for (int k = 0; k <= 2; k++) for (int j = 0; j <= 2; j++) for (int i = 0; i <= 2; i++)
                esp.Nos.Add(new No3D { Id = ++idn, X = i * 6, Y = j * 5, Z = k * 3.5, Apoios = k == 0 ? new[] { true, true, true, true, true, true } : new bool[6],
                                       Desl = new[] { 0.004 * k, 0.002 * k, -0.001 * k * (i == 1 || j == 1 ? 2 : 1) } });
            Func<int, int, int, int> nid = (i, j, k) => 1 + i + 3 * j + 9 * k;
            int idb = 0;
            for (int k = 0; k < 2; k++) for (int j = 0; j <= 2; j++) for (int i = 0; i <= 2; i++)
            {
                esp.Barras.Add(new Barra3D { Id = ++idb, Ni = nid(i, j, k), Nj = nid(i, j, k + 1), Secao = "W 250 x 32,7", Aprov = 0.4 + 0.7 * rnd.NextDouble() });
                if (i < 2) esp.Barras.Add(new Barra3D { Id = ++idb, Ni = nid(i, j, k + 1), Nj = nid(i + 1, j, k + 1), Secao = "W 310 x 38,7", Aprov = 0.3 + 0.75 * rnd.NextDouble() });
                if (j < 2) esp.Barras.Add(new Barra3D { Id = ++idb, Ni = nid(i, j, k + 1), Nj = nid(i, j + 1, k + 1), Secao = "W 310 x 38,7", Aprov = 0.3 + 0.75 * rnd.NextDouble() });
            }
            return esp;
        }

        // 42) Vista 3D Realista x analítica: guincho (modelo 2D), pórtico espacial 2 x 2 e exemplos do motor (concreto e galpão).
        // Atalho para iterar só nelas: variável ESTRUCTTO_CAPTURAS=3d.
        static void CapturasRealista(MainWindow w)
        {
            var abaVista = w.Faixa.Tabs.First(t => (t.Header as string) == "Vista");
            foreach (bool real in new[] { true, false })
            {
                string suf = real ? "realista" : "analitico";
                w.DefinirRealista(real);
                w.Faixa.SelectedTabItem = abaVista;
                w.MostrarDiagrama(Diagrama.Nenhum);
                w.MostrarVista3D(true);
                EsperarIdle();
                w.vista3D.MostrarVista(VistaPadrao.Iso, false);
                CapturarPrincipal(w, "42-" + suf + "-guincho");
                if (real)
                {
                    foreach (var (d, nome) in new[] { (Diagrama.Verificacao, "aproveitamento"), (Diagrama.Deformada, "deformada") })
                    {
                        w.MostrarDiagrama(d, d == Diagrama.Verificacao);
                        w.Faixa.SelectedTabItem = abaVista;
                        EsperarIdle();
                        w.vista3D.MostrarVista(VistaPadrao.Iso, false);
                        CapturarPrincipal(w, "42-realista-guincho-" + nome);
                    }
                    w.MostrarDiagrama(Diagrama.Nenhum);
                    w.Faixa.SelectedTabItem = abaVista;
                }

                var esp = Portico2x2();
                w.vista3D.Diagrama = Diagrama3D.Nenhum; w.vista3D.MostrarDeformada = false; w.vista3D.MostrarAproveitamento = false;
                w.vista3D.Carregar(esp, false);
                w.vista3D.MostrarVista(VistaPadrao.Iso, false);
                CapturarPrincipal(w, "42-" + suf + "-portico2x2");
                w.vista3D.ZoomFator(3.5);
                CapturarPrincipal(w, "42-" + suf + "-portico2x2-detalhe");
                w.vista3D.MostrarVista(VistaPadrao.Iso, false);
                if (real)
                {
                    w.vista3D.MostrarAproveitamento = true;
                    w.vista3D.Carregar(esp, false);
                    w.vista3D.Escolher(esp.Barras[4]);
                    if (w.vista3D.Selecionado != esp.Barras[4]) throw new InvalidOperationException("Realista: escolher barra falhou");
                    CapturarPrincipal(w, "42-realista-portico2x2-aproveitamento");
                    w.vista3D.MostrarAproveitamento = false; w.vista3D.MostrarDeformada = true;
                    w.vista3D.Carregar(esp, false);
                    CapturarPrincipal(w, "42-realista-portico2x2-deformada");
                    w.vista3D.MostrarDeformada = false;
                    w.vista3D.Carregar(esp, false);
                    w.vista3D.MostrarVista(VistaPadrao.Frente, false);
                    CapturarPrincipal(w, "42-realista-portico2x2-frente");
                }

                // desempenho: ~2.400 barras (12 x 10 vãos, 6 pavimentos); tempos em 42-tempos.txt
                {
                    var g = new Modelo3D(); int nx = 12, ny = 10, nz = 6;
                    Func<int, int, int, int> id = (i, j, k) => 1 + i + (nx + 1) * (j + (ny + 1) * k);
                    for (int k = 0; k <= nz; k++) for (int j = 0; j <= ny; j++) for (int i = 0; i <= nx; i++)
                        g.Nos.Add(new No3D { Id = id(i, j, k), X = i * 6, Y = j * 5, Z = k * 3.2, Apoios = k == 0 ? new[] { true, true, true, true, true, true } : new bool[6] });
                    for (int k = 0; k < nz; k++) for (int j = 0; j <= ny; j++) for (int i = 0; i <= nx; i++)
                    {
                        g.Barras.Add(new Barra3D { Id = g.Barras.Count + 1, Ni = id(i, j, k), Nj = id(i, j, k + 1), Secao = "W 250 x 32,7" });
                        if (i < nx) g.Barras.Add(new Barra3D { Id = g.Barras.Count + 1, Ni = id(i, j, k + 1), Nj = id(i + 1, j, k + 1), Secao = "W 310 x 38,7" });
                        if (j < ny) g.Barras.Add(new Barra3D { Id = g.Barras.Count + 1, Ni = id(i, j, k + 1), Nj = id(i, j + 1, k + 1), Secao = "W 310 x 38,7" });
                    }
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    w.vista3D.Carregar(g, false);
                    w.vista3D.MostrarVista(VistaPadrao.Iso, false);
                    double tCarregar = sw.Elapsed.TotalMilliseconds;
                    sw.Restart();
                    CapturarPrincipal(w, "42-" + suf + "-2400barras");
                    double tCaptura = sw.Elapsed.TotalMilliseconds;
                    sw.Restart();
                    object achou = null;
                    for (int r = 0; r < 20; r++) achou = w.vista3D.Acertar(new Point(w.vista3D.ActualWidth / 2, w.vista3D.ActualHeight / 2));
                    double tPick = sw.Elapsed.TotalMilliseconds / 20;
                    File.AppendAllText(Path.Combine(pastaSaida, "42-tempos.txt"), string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "{0}: {1} barras, carregar+enquadrar {2:0} ms, 2 capturas {3:0} ms, picking {4:0.0} ms ({5}){6}", suf, g.Barras.Count, tCarregar, tCaptura, tPick, achou?.GetType().Name ?? "nada", Environment.NewLine));
                }
                w.MostrarVista3D(false);
                EsperarIdle();

                foreach (string ex in real ? new[] { "edificio_concreto", "galpao_aco" } : new[] { "edificio_concreto" })
                {
                    Esperar(w.AbrirEspacial(() => DocumentoEspacial.Exemplo(ex), "exemplo"));
                    EsperarIdle();
                    w.vista3D.MostrarVista(VistaPadrao.Iso, false);
                    CapturarPrincipal(w, "42-" + suf + "-" + ex);
                    if (real && ex == "edificio_concreto")
                    {
                        Esperar(w.CalcularEspacial());
                        w.MostrarDiagrama(Diagrama.Verificacao);
                        EsperarIdle();
                        w.vista3D.MostrarVista(VistaPadrao.Iso, false);
                        CapturarPrincipal(w, "42-realista-edificio_concreto-aproveitamento");
                        w.MostrarDiagrama(Diagrama.Nenhum);
                    }
                    w.FecharEspaciaisSemPerguntar();
                    EsperarIdle();
                }
            }
        }

        // 43) Níveis e planos (PlanosTrabalho.cs): edifício de 1 andar, painel Níveis, copiar o andar 3× para cima (4 andares,
        // um lote só: um desfazer volta tudo), nível isolado em planta, gerar pilares (regra pura).
        static void CapturasPlanos(MainWindow w)
        {
            var jg = new JanelaGerar3D("edificio");
            if (!jg.Montar()) throw new InvalidOperationException("JanelaGerar3D: valores padrão inválidos");
            jg.Dados["alturas"] = new System.Text.Json.Nodes.JsonArray(3.0);
            jg.Close();
            Esperar(w.AbrirEspacial(() => DocumentoEspacial.Gerar(jg.Dados, "Edifício 1 andar", jg.G, jg.Q), "gerando"));
            var d = w.EspacialAtivo ?? throw new InvalidOperationException("modelo espacial não abriu");
            int nb = d.Json["barras"].AsObject().Count, nc = d.Json["cargas_distribuidas"].AsArray().Count;
            var niv = Niveis.Lista(d.Json);
            if (niv.Count != 2 || !niv[0].Terreo) throw new InvalidOperationException("níveis: esperado Térreo + 1 pavimento, veio " + niv.Count);

            void Painel(string arq)
            {
                var jn = new JanelaNiveis(w);
                PrepararDialogoForaDaTela(jn);
                jn.Show(); EsperarIdle();
                CapturarDialogo(jn, arq);
                jn.Close();
            }
            Painel("43-niveis-painel-1andar.png");

            // copiar o andar de P1 (entre z=0 e z=3) 3× para cima, h = 3 m
            double z1 = niv[1].Z;
            Esperar(w.Lote(Niveis.CopiarParaCima(d.Json, 0, z1, 3, 3.0), "teste: copiar pavimento"));
            d = w.EspacialAtivo;
            niv = Niveis.Lista(d.Json);
            if (niv.Count != 5 || d.Json["barras"].AsObject().Count != 4 * nb || d.Json["cargas_distribuidas"].AsArray().Count != 4 * nc)
                throw new InvalidOperationException($"copiar pavimento: {niv.Count} níveis, {d.Json["barras"].AsObject().Count} barras (esperado {4 * nb}), {d.Json["cargas_distribuidas"].AsArray().Count} cargas (esperado {4 * nc})");
            EsperarIdle();
            w.vista3D.MostrarVista(VistaPadrao.Iso, false);
            CapturarPrincipal(w, "43-niveis-copiado-4-andares");
            Painel("43-niveis-painel.png");

            // isolar o 2º andar (z 3..6) em planta, com os outros em fantasma
            w.AtivarNivel(niv[2].Z);
            EsperarIdle();
            int vis = w.FiltrarPlano(d.ParaVista()).Barras.Count;
            if (vis != nb) throw new InvalidOperationException("nível isolado: " + vis + " barras na vista, esperado " + nb);
            CapturarPrincipal(w, "43-niveis-isolado-planta");
            w.vista3D.MostrarVista(VistaPadrao.Iso, false);
            EsperarIdle();
            CapturarPrincipal(w, "43-niveis-isolado-fantasmas");
            w.Elevacao('y', Niveis.Cotas(d.Json, "y")[0]);
            EsperarIdle();
            CapturarPrincipal(w, "43-niveis-elevacao-y");
            w.Completo3D();
            if (w.FiltrarPlano(d.ParaVista()).Barras.Count != 4 * nb) throw new InvalidOperationException("3D completo não voltou ao modelo inteiro");

            // um desfazer volta o lote inteiro
            Esperar(w.DesfazerEspacial());
            if (w.EspacialAtivo.Json["barras"].AsObject().Count != nb || Niveis.Lista(w.EspacialAtivo.Json).Count != 2) throw new InvalidOperationException("desfazer não voltou o lote inteiro: " + w.StatusMsg.Text);

            // gerar pilares: nada a fazer de P1 ao térreo (já há pilares); sob o térreo, um pilar e um nó por nó
            var j = w.EspacialAtivo.Json;
            if (Niveis.GerarPilares(j, z1, 0).Count != 0) throw new InvalidOperationException("gerar pilares duplicou pilares existentes");
            int n0 = j["nos"].AsObject().Count(kv => Math.Abs((double)kv.Value["z"]) < 1e-3);
            if (Niveis.GerarPilares(j, 0, -2).Count != 2 * n0) throw new InvalidOperationException("gerar pilares: esperado nó + pilar por nó do térreo");
        }

        // Pelo caminho real: botão da faixa → painel → Aplicar (um PUT /api/modelo) → um Ctrl+Z desfaz a operação inteira.
        static void CapturasEdicao(MainWindow w)
        {
            var jg = new JanelaGerar3D("portico_plano");
            PrepararDialogoForaDaTela(jg);
            jg.Show();
            EsperarIdle();
            if (!jg.Montar()) throw new InvalidOperationException("JanelaGerar3D (pórtico plano): valores padrão inválidos");
            jg.Close();
            Esperar(w.AbrirEspacial(() => DocumentoEspacial.Gerar(jg.Dados, "Pórtico (edição)", jg.G, jg.Q), "gerando"));
            Func<int> nb = () => w.EspacialAtivo.Json["barras"].AsObject().Count;
            w.Faixa.SelectedTabItem = w.abaModelo;   // aba Modelo: grupo Modificar com os botões do espacial
            int b0 = nb(), c0 = w.EspacialAtivo.Json["cargas_distribuidas"].AsArray().Count;
            Action<string, string, (string, string)[]> rodar = (op, png, campos) =>
            {
                w.AbrirEdicaoEsp(op);
                var p = w.PainelEdicaoAberto ?? throw new InvalidOperationException("painel " + op + " não abriu");
                EsperarIdle();
                foreach (var (k, v) in campos) { if (p.Campos[k] is ComboBox cb) cb.SelectedItem = v; else ((TextBox)p.Campos[k]).Text = v; }
                EsperarIdle();
                CapturarDialogo(p, "48-painel-" + op + ".png");
                Esperar(p.Executar());
                p.Close();
                EsperarIdle();
                w.vista3D.MostrarVista(VistaPadrao.Iso, false);
                CapturarPrincipal(w, "48-edicao-" + png);
            };
            rodar("copiar", "copiado-3x", new[] { ("dy", "6"), ("n", "3") });
            if (nb() != 4 * b0 || w.EspacialAtivo.Json["cargas_distribuidas"].AsArray().Count != 4 * c0) throw new InvalidOperationException("copiar 3×: esperado " + 4 * b0 + " barras, há " + nb() + " " + w.StatusMsg.Text);
            Esperar(w.DesfazerEspacial());
            if (nb() != b0) throw new InvalidOperationException("um Ctrl+Z não desfez a cópia inteira: " + nb());
            Esperar(w.OperarEspacial(d => d.Trocar(false), null));
            if (nb() != 4 * b0) throw new InvalidOperationException("refazer não voltou a cópia");
            rodar("espelhar", "espelhado", new[] { ("plano", "Y"), ("valor", "-3") });
            if (nb() != 8 * b0) throw new InvalidOperationException("espelhar: esperado " + 8 * b0 + " barras, há " + nb());
            rodar("rotacionar", "rotacionado", new[] { ("centro", "6; 0"), ("ang", "30"), ("n", "0") });
            if (nb() != 8 * b0) throw new InvalidOperationException("rotacionar mudou o número de barras");
            Esperar(w.CalcularEspacial());
            if (!w.EspacialAtivo.Calculado) throw new InvalidOperationException("modelo editado não calculou: " + w.StatusMsg.Text);
            w.FecharEspaciaisSemPerguntar();
            EsperarIdle();
        }

        // 43) Desenhar no 3D (Vista3D.cs, docs/MODELO-ESPACIAL.md): pórtico de 2 vãos desenhado com cliques na tela
        // (ferramenta Barra no plano y = 0, comprimento digitado, coordenada @dx,dy,dz, Pilar no plano z = 0),
        // seleção por janela e cruzamento, desfazer pelo motor. Só elas: ESTRUCTTO_CAPTURAS=desenho3d.
        static void CapturasDesenho3D(MainWindow w)
        {
            void Falha(string s) => throw new InvalidOperationException("Desenho 3D: " + s + " | " + w.StatusMsg.Text);
            Esperar(w.AbrirEspacial(() => DocumentoEspacial.Vazio("Pórtico desenhado"), "modelo vazio"));
            var d = w.EspacialAtivo ?? throw new InvalidOperationException("Desenho 3D: modelo vazio não abriu");
            w.Faixa.SelectedTabItem = w.abaModelo;
            EsperarIdle();
            var v = w.vista3D;
            v.MostrarVista(VistaPadrao.Iso, false);
            v.ZoomFator(0.75);
            int Nos() => d.Json["nos"].AsObject().Count;
            int Barras() => d.Json["barras"].AsObject().Count;
            Point T(double x, double y, double z) => v.Tela(new System.Windows.Media.Media3D.Point3D(x, y, z));
            void Clicar(double x, double y, double z) { Esperar(v.ClicarTela(T(x, y, z))); EsperarIdle(); }

            w.UsarFerramenta3D(Ferramenta3D.Barra);
            v.PlanoAtual = PlanoTrabalho.EmY(0);   // pórtico no plano XZ
            if (v.Ferramenta != Ferramenta3D.Barra) Falha("ferramenta Barra não ligou");
            v.MoverTela(T(0, 0, 0));
            EsperarIdle();
            CapturarPrincipal(w, "43-desenho3d-plano");
            Clicar(0, 0, 0); Clicar(0, 0, 3); Clicar(6, 0, 3); Clicar(12, 0, 3);
            if (Barras() != 3 || Nos() != 4) Falha("corrente de 3 barras: " + Barras() + " barras, " + Nos() + " nós");
            // último pilar pelo comprimento digitado: mouse para baixo, "3" + Enter
            v.MoverTela(T(12, 0, 1.2));
            if (v.TipoAtracao != "Polar" && v.TipoAtracao != "Ortogonal") Falha("polar na vertical do plano não atraiu: " + v.TipoAtracao);
            v.Digitar("3");
            EsperarIdle();
            CapturarPrincipal(w, "43-desenho3d-elastico");
            v.Tecla(System.Windows.Input.Key.Enter);
            EsperarAte(() => Barras() == 4);
            v.Tecla(System.Windows.Input.Key.Escape);
            if (v.InicioCorrente != null) Falha("Esc não terminou a corrente");
            if (!d.Json["nos"].AsObject().Any(kv => Math.Abs((double)kv.Value["x"] - 12) < 1e-6 && Math.Abs((double)kv.Value["z"]) < 1e-6)) Falha("comprimento digitado não chegou a (12; 0; 0)");
            // diagonal por coordenada relativa digitada
            Clicar(0, 0, 0);
            foreach (char c in "@6,0,3") v.Digitar(c.ToString());
            v.Tecla(System.Windows.Input.Key.Enter);
            EsperarAte(() => Barras() == 5);
            v.Tecla(System.Windows.Input.Key.Escape);
            if (Nos() != 5) Falha("diagonal deveria reaproveitar o nó (6; 0; 3): " + Nos() + " nós");

            // pilar central no plano z = 0: base clicada, topo reaproveita o nó (6; 0; 3)
            w.UsarFerramenta3D(Ferramenta3D.Pilar);
            v.PlanoAtual = PlanoTrabalho.Horizontal(0);
            v.MoverTela(T(6, 0, 0));
            EsperarIdle();
            CapturarPrincipal(w, "43-desenho3d-pilar");
            Clicar(6, 0, 0);
            if (Barras() != 6 || Nos() != 6) Falha("pilar: " + Barras() + " barras, " + Nos() + " nós");
            v.Tecla(System.Windows.Input.Key.Escape);   // sai do Pilar -> Selecionar
            if (v.Ferramenta != Ferramenta3D.Selecionar) Falha("Esc não trocou para Selecionar");
            CapturarPrincipal(w, "43-desenho3d-portico", () => v.MostrarVista(VistaPadrao.Iso, false));   // enquadra no tamanho da captura

            // janela (esq -> dir): só o que está inteiro dentro; cruzamento (dir -> esq): o que tocar. Vista de frente: caixa = retângulo em XZ.
            int selMudou = 0; v.SelecaoMudou += () => selMudou++;
            int janela = 0, cruz = 0;
            Rect Caixa() { v.MostrarVista(VistaPadrao.Frente, false); return new Rect(T(-0.5, 0, -0.5), T(6.5, 0, 3.5)); }
            CapturarPrincipal(w, "43-desenho3d-selecao-janela", () => { Rect r = Caixa(); v.SelecionarCaixa(r.TopLeft, r.BottomRight); janela = v.Selecao.Barras.Count; });
            CapturarPrincipal(w, "43-desenho3d-selecao-cruzamento", () => { Rect r = Caixa(); v.SelecionarCaixa(r.BottomRight, r.TopLeft); cruz = v.Selecao.Barras.Count; });
            if (janela != 4 || cruz != 5 || selMudou < 2) Falha("janela " + janela + " / cruzamento " + cruz + " barras (esperado 4 / 5), eventos " + selMudou);
            v.Tecla(System.Windows.Input.Key.Escape);
            if (v.Selecao.Count != 0) Falha("Esc não limpou a seleção");

            Esperar(w.DesfazerEspacial());
            if (Barras() != 5) Falha("desfazer não tirou o pilar: " + Barras() + " barras");
            File.AppendAllText(Path.Combine(pastaSaida, "43-desenho3d.txt"), "ok: 6 barras desenhadas, janela " + janela + ", cruzamento " + cruz + ", desfazer -> " + Barras() + Environment.NewLine);
            w.FecharEspaciaisSemPerguntar();
            EsperarIdle();
        }

        static void EsperarAte(Func<bool> cond)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!cond()) { if (sw.ElapsedMilliseconds > 20000) throw new TimeoutException("Desenho 3D: motor não respondeu"); EsperarIdle(); System.Threading.Thread.Sleep(20); }
            EsperarIdle();
        }

        static void CapturasEspacial(MainWindow w)
        {
            var jg = new JanelaGerar3D("edificio");
            PrepararDialogoForaDaTela(jg);
            jg.Show();
            EsperarIdle();
            CapturarDialogo(jg, "41-gerar3d-edificio.png");
            if (!jg.Montar()) throw new InvalidOperationException("JanelaGerar3D: valores padrão inválidos");
            jg.Close();
            Esperar(w.AbrirEspacial(() => DocumentoEspacial.Gerar(jg.Dados, jg.Titulo, jg.G, jg.Q), "gerando"));
            var d = w.EspacialAtivo ?? throw new InvalidOperationException("modelo espacial não abriu");
            EsperarIdle();
            w.vista3D.MostrarVista(VistaPadrao.Iso, false);
            CapturarPrincipal(w, "41-espacial-edificio");

            // editar (PATCH) e desfazer pelo motor
            string barra = d.Json["barras"].AsObject().First(kv => (string)kv.Value["params"]?["papel"] == "viga").Key;
            string sec0 = (string)d.Json["barras"][barra]["secao"], sec1 = (string)d.Json["barras"].AsObject().First(kv => (string)kv.Value["secao"] != sec0).Value["secao"];
            Esperar(w.EditarEspacial("barra", barra, new System.Text.Json.Nodes.JsonObject { ["secao"] = sec1 }));
            if ((string)w.EspacialAtivo.Json["barras"][barra]["secao"] != sec1) throw new InvalidOperationException("PATCH da barra não mudou a seção");
            Esperar(w.DesfazerEspacial());
            if ((string)w.EspacialAtivo.Json["barras"][barra]["secao"] != sec0) throw new InvalidOperationException("desfazer do motor não voltou a seção: " + w.StatusMsg.Text);

            Esperar(w.CalcularEspacial());
            if (!d.Calculado) throw new InvalidOperationException("cálculo do motor falhou: " + w.StatusMsg.Text);
            EsperarIdle();
            w.vista3D.MostrarVista(VistaPadrao.Iso, false);
            CapturarPrincipal(w, "41-espacial-aproveitamento");
            var pior = d.ParaVista().Barras.OrderByDescending(b => b.Aprov ?? 0).First();
            w.vista3D.Escolher(pior);
            EsperarIdle();
            CapturarPrincipal(w, "41-espacial-barra");
            foreach (var (dg, nome) in new[] { (Diagrama.Momento, "momento"), (Diagrama.Deformada, "deformada") })
            {
                w.MostrarDiagrama(dg);
                EsperarIdle();
                w.vista3D.MostrarVista(VistaPadrao.Iso, false);
                CapturarPrincipal(w, "41-espacial-" + nome);
            }

            // árvore e tabelas mostram o modelo espacial e os resultados do motor; HUD diz o que está na tela
            if (!w.TextoResultado.Contains("envoltória")) throw new InvalidOperationException("HUD do espacial: " + w.TextoResultado);
            if (!(w.TabNos.ItemsSource is IEnumerable<LinhaNoEsp> ln) || ln.Count() != d.Json["nos"].AsObject().Count) throw new InvalidOperationException("tabela de nós não mostra o modelo espacial");
            w.PainelTabelas.IsSelected = true;
            foreach (var (aba, nome) in new[] { (0, "nos"), (1, "barras"), (2, "verificacao") })
            {
                w.AbasTabela.SelectedIndex = aba;
                EsperarIdle();
                CapturarPrincipal(w, "41-espacial-tabela-" + nome);
            }
            w.AbasTabela.SelectedIndex = 0;

            // combinação dos diagramas (padrão: envoltória ELU)
            string comb = d.Combinacoes.First(c => c.StartsWith("ELU"));
            Esperar(w.MostrarCombinacaoEspacial(comb));
            if (d.CombDiagrama != comb || w.TextoResultado != comb) throw new InvalidOperationException("seletor de combinação não trocou: " + w.TextoResultado);
            w.MostrarDiagrama(Diagrama.Momento);
            EsperarIdle();
            w.vista3D.MostrarVista(VistaPadrao.Iso, false);
            CapturarPrincipal(w, "41-espacial-combinacao");
            Esperar(w.MostrarCombinacaoEspacial(null));
            if (d.CombDiagrama != null) throw new InvalidOperationException("não voltou à envoltória");

            // criar e apagar nó/barra, cargas por caso, seção do catálogo
            int nb0 = d.Json["barras"].AsObject().Count;
            var n0 = d.Json["nos"].AsObject().First(kv => (double)kv.Value["z"] > 0);
            string nNovo = null, bNova = null;
            Esperar(w.OperarEspacial(async dd =>
            {
                nNovo = await dd.Adicionar("no", new System.Text.Json.Nodes.JsonObject { ["x"] = (double)n0.Value["x"], ["y"] = (double)n0.Value["y"] - 2, ["z"] = (double)n0.Value["z"] });
                string perfil = (await Motor.Get("/api/catalogo?filtro=W200")).AsArray().Select(x => (string)x["nome"]).First(x => dd.Json["secoes"][x] == null);
                await dd.GarantirSecao(perfil);
                bNova = await dd.Adicionar("barra", new System.Text.Json.Nodes.JsonObject { ["ni"] = n0.Key, ["nj"] = nNovo, ["secao"] = perfil, ["material"] = (string)dd.Json["barras"].AsObject().First().Value["material"] });
                await dd.Adicionar("carga_nodal", new System.Text.Json.Nodes.JsonObject { ["caso"] = dd.CasoAtivo, ["no"] = nNovo, ["F"] = new System.Text.Json.Nodes.JsonArray(0.0, 0.0, -10.0, 0.0, 0.0, 0.0) });
                await dd.Adicionar("carga_distribuida", new System.Text.Json.Nodes.JsonObject { ["caso"] = dd.CasoAtivo, ["barra"] = bNova, ["q"] = new System.Text.Json.Nodes.JsonArray(0.0, 0.0, -3.0) });
            }, "teste: nó, barra (perfil do catálogo) e cargas"));
            d = w.EspacialAtivo;
            if (bNova == null || d.Json["barras"].AsObject().Count != nb0 + 1 || !d.Json["cargas_nodais"].AsArray().Any(c => (string)c["no"] == nNovo) || !d.Json["cargas_distribuidas"].AsArray().Any(c => (string)c["barra"] == bNova))
                throw new InvalidOperationException("criar nó/barra/cargas pelo motor falhou: " + w.StatusMsg.Text);
            w.EscolherEspacial("barra", bNova);
            EsperarIdle();
            CapturarPrincipal(w, "41-espacial-barra-nova");
            w.EscolherEspacial("no", nNovo);
            EsperarIdle();
            CapturarPrincipal(w, "41-espacial-no-novo");
            CapturasCargas(w, bNova);   // CargasEspacial.cs: pontual, trapezoidal, área, casos, vento/sismo/pavimentos
            d = w.EspacialAtivo;
            Esperar(w.OperarEspacial(dd => dd.Remover("no", nNovo), "teste: apagar nó"));
            if (d.Json["barras"].AsObject().Count != nb0 || d.Json["nos"][nNovo] != null) throw new InvalidOperationException("apagar nó (cascata) falhou: " + w.StatusMsg.Text);

            // salvar (JSON do motor) e abrir de novo; exemplo do motor
            string tmp = Path.Combine(Path.GetTempPath(), "estructto-espacial-teste.json");
            File.WriteAllText(tmp, d.JsonFormatado());
            Esperar(w.AbrirEspacialArquivo(tmp));
            if (w.EspacialAtivo == d || w.EspacialAtivo.Json["barras"].AsObject().Count != d.Json["barras"].AsObject().Count) throw new InvalidOperationException("JSON salvo não reabriu igual");
            if (!string.Equals(Config.Recentes().FirstOrDefault(), tmp, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException(".json aberto não entrou nos recentes");
            Esperar(w.AbrirEspacial(() => DocumentoEspacial.Exemplo("galpao_aco"), "exemplo"));
            Esperar(w.CalcularEspacial());
            EsperarIdle();
            w.vista3D.MostrarVista(VistaPadrao.Iso, false);
            CapturarPrincipal(w, "41-espacial-exemplo-galpao");
            // CYPE: Importar como modelo (JanelaCype.Importado) abre um documento espacial com o modelo devolvido
            string ed3 = Path.Combine(Environment.GetEnvironmentVariable("ESTRUCTTO_MOTOR") ?? "", "testes", "dados_cype", "novo.ed3");
            if (File.Exists(ed3))
            {
                Esperar(w.AbrirEspacial(async () =>
                {
                    var imp = await Motor.Post("/api/importar_cype", new System.Text.Json.Nodes.JsonObject { ["nome"] = "novo.ed3", ["conteudo_base64"] = Convert.ToBase64String(File.ReadAllBytes(ed3)) });
                    return await DocumentoEspacial.DoModelo((System.Text.Json.Nodes.JsonObject)imp["modelo"].DeepClone(), "novo (CYPE)", imp["avisos"]);
                }, "importando"));
                if (w.EspacialAtivo?.Json["barras"]?.AsObject().Count is not > 0) throw new InvalidOperationException("obra do CYPE não abriu como documento espacial");
                // F5 logo depois de importar (a ativação da aba ainda pode estar recolocando o modelo no motor)
                Esperar(w.CalcularEspacial());
                if (!w.EspacialAtivo.Calculado) throw new InvalidOperationException("F5 depois de importar do CYPE não calculou: " + w.StatusMsg.Text);
                w.MostrarDiagrama(Diagrama.Verificacao);
                EsperarIdle();
                w.vista3D.MostrarVista(VistaPadrao.Iso, false);
                CapturarPrincipal(w, "42-espacial-importado-cype");
            }
            string troca = w.EspacialAtivo.Troca;
            w.FecharEspaciaisSemPerguntar();
            if (File.Exists(troca)) throw new InvalidOperationException("arquivo de troca do documento espacial não foi apagado ao fechar");
            w.MostrarDiagrama(Diagrama.Nenhum);
            EsperarIdle();
        }

        static void EsperarIdle()
        {
            var disp = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
            disp.Invoke(new Action(() => { }), DispatcherPriority.Loaded);
            disp.Invoke(new Action(() => { }), DispatcherPriority.Render);
            disp.Invoke(new Action(() => { }), DispatcherPriority.Background);
            disp.Invoke(new Action(() => { }), DispatcherPriority.ContextIdle);
            disp.Invoke(new Action(() => { }), DispatcherPriority.ApplicationIdle);
        }

        static string Slug(string s)
        {
            if (string.IsNullOrEmpty(s)) return "aba";
            string norm = s.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (char c in norm)
            {
                UnicodeCategory cat = CharUnicodeInfo.GetUnicodeCategory(c);
                if (cat == UnicodeCategory.NonSpacingMark) continue;
                if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
                else if (c == ' ' || c == '-' || c == '_') sb.Append('-');
            }
            return sb.ToString().Trim('-');
        }

        static void SalvarPng(BitmapSource bmp, string caminho)
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var fs = File.Create(caminho))
                enc.Save(fs);
        }

        static void RenderizarJanela(Window w, int larg, int alt, string nomeArquivo, Action aposLayout = null, FrameworkElement sobrepor = null, Func<Point> calcPosSobrepor = null)
        {
            w.Width = larg;
            w.Height = alt;
            EsperarIdle();
            w.Measure(new Size(larg, alt));
            w.Arrange(new Rect(0, 0, larg, alt));
            w.UpdateLayout();
            if (aposLayout != null)
            {
                aposLayout();
                w.UpdateLayout();
            }
            EsperarIdle();
            w.Measure(new Size(larg, alt));
            w.Arrange(new Rect(0, 0, larg, alt));
            w.UpdateLayout();
            if (aposLayout != null)
            {
                aposLayout();
                w.UpdateLayout();
            }

            var rtb = new RenderTargetBitmap(larg, alt, 96, 96, PixelFormats.Pbgra32);
            var fundo = new DrawingVisual();
            using (var dc = fundo.RenderOpen())
                dc.DrawRectangle(w.Background ?? JanelaEscura.BFundo, null, new Rect(0, 0, larg, alt));
            rtb.Render(fundo);
            rtb.Render(w);

            if (sobrepor != null)
            {
                Point posSobrepor = calcPosSobrepor != null ? calcPosSobrepor() : default;
                sobrepor.Measure(new Size(larg, alt));
                Size ds = sobrepor.DesiredSize;
                int sw = Math.Max(1, (int)Math.Ceiling(ds.Width > 1 ? ds.Width : sobrepor.ActualWidth));
                int sh = Math.Max(1, (int)Math.Ceiling(ds.Height > 1 ? ds.Height : sobrepor.ActualHeight));
                sobrepor.Arrange(new Rect(0, 0, sw, sh));
                sobrepor.UpdateLayout();
                var rtbSub = new RenderTargetBitmap(sw, sh, 96, 96, PixelFormats.Pbgra32);
                rtbSub.Render(sobrepor);
                var dvSub = new DrawingVisual();
                using (var dc = dvSub.RenderOpen())
                    dc.DrawImage(rtbSub, new Rect(posSobrepor.X, posSobrepor.Y, sw, sh));
                rtb.Render(dvSub);
            }

            SalvarPng(rtb, Path.Combine(pastaSaida, nomeArquivo));
        }

        static void CapturarPrincipal(MainWindow w, string prefixo, Action aposLayout = null, FrameworkElement sobrepor = null, Func<Point> calcPosSobrepor = null)
        {
            Action prep = () =>
            {
                TemaPlant.AjustarFaixa(w, TemaPlant.Selo());
                if (w.M.Nos.Count > 0) w.Vista.ZoomTudo();
                aposLayout?.Invoke();
                w.Vista.InvalidateVisual();
            };

            prep();
            EsperarIdle();
            RenderizarJanela(w, 1600, 900, prefixo + ".png", prep, sobrepor, calcPosSobrepor);
            RenderizarJanela(w, 1280, 720, prefixo + "-1280x720.png", prep, sobrepor, calcPosSobrepor);

            w.Width = 1600;
            w.Height = 900;
            w.Measure(new Size(1600, 900));
            w.Arrange(new Rect(0, 0, 1600, 900));
            w.UpdateLayout();
            if (w.M.Nos.Count > 0) w.Vista.ZoomTudo();
            EsperarIdle();
        }

        static void PrepararDialogoForaDaTela(Window d)
        {
            d.WindowStartupLocation = WindowStartupLocation.Manual;
            d.Left = -10000;
            d.Top = -10000;
            d.ShowActivated = false;
            d.ShowInTaskbar = false;
        }

        static void CapturarDialogo(Window d, string nomeArquivo, Action aposLayout = null)
        {
            int larg = !double.IsNaN(d.Width) && d.Width > 0 ? (int)Math.Round(d.Width) : Math.Max(400, (int)Math.Round(d.ActualWidth));
            int alt = !double.IsNaN(d.Height) && d.Height > 0 && d.SizeToContent == SizeToContent.Manual
                ? (int)Math.Round(d.Height)
                : Math.Max(180, (int)Math.Round(d.ActualHeight));
            if (d.SizeToContent == SizeToContent.Height || d.SizeToContent == SizeToContent.WidthAndHeight)
            {
                d.Measure(new Size(larg, double.PositiveInfinity));
                alt = Math.Max(180, (int)Math.Ceiling(d.DesiredSize.Height));
            }
            RenderizarJanela(d, larg, alt, nomeArquivo, aposLayout);
        }

        // Folha de icones: nome embaixo, em 32 px e 16 px, sobre o fundo dado. zoom > 1: so os de 16 px, ampliados (vizinho mais proximo).
        static void FolhaIcones(string arquivo, Color fundo, int zoom = 1, int tam = 16)
        {
            const int colunas = 12;
            var painel = new WrapPanel { Width = zoom > 1 ? (tam > 16 ? 12 * 48 : 560) : colunas * 96, Background = new SolidColorBrush(fundo), UseLayoutRounding = true, SnapsToDevicePixels = true };
            foreach (string n in Icones.Nomes)
            {
                var celula = new StackPanel { Width = zoom > 1 ? (tam > 16 ? 48 : 56) : 96, Height = zoom > 1 ? (tam > 16 ? 48 : 32) : 76, Margin = new Thickness(0) };
                if (zoom > 1)
                {
                    var ic = Icones.Criar(n, tam); ((FrameworkElement)ic).HorizontalAlignment = HorizontalAlignment.Center; ((FrameworkElement)ic).Margin = new Thickness(0, 8, 0, 8);
                    celula.Children.Add(ic);
                }
                else
                {
                    var linha = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 6) };
                    linha.Children.Add(Icones.Criar(n, 32));
                    var i16 = Icones.Criar(n, 16); ((FrameworkElement)i16).Margin = new Thickness(14, 8, 0, 0);
                    linha.Children.Add(i16);
                    celula.Children.Add(linha);
                    celula.Children.Add(new TextBlock { Text = n, FontFamily = new FontFamily("Segoe UI"), FontSize = 10, Foreground = TemaPlant.BTexto3, HorizontalAlignment = HorizontalAlignment.Center });
                }
                painel.Children.Add(celula);
            }
            painel.Measure(new Size(painel.Width, 4000));
            painel.Arrange(new Rect(0, 0, painel.Width, painel.DesiredSize.Height));
            painel.UpdateLayout();
            int pw = (int)Math.Ceiling(painel.Width), ph = (int)Math.Ceiling(painel.DesiredSize.Height);
            var rtb = new RenderTargetBitmap(pw, ph, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(painel);
            BitmapSource final = rtb;
            if (zoom > 1)
            {
                var dv = new DrawingVisual();
                RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.NearestNeighbor);
                using (var dc = dv.RenderOpen()) dc.DrawImage(rtb, new Rect(0, 0, pw * zoom, ph * zoom));
                var rz = new RenderTargetBitmap(pw * zoom, ph * zoom, 96, 96, PixelFormats.Pbgra32);
                rz.Render(dv);
                final = rz;
            }
            SalvarPng(final, Path.Combine(pastaSaida, arquivo));
        }

        public static void Executar(MainWindow w)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(pastaSaida))
                    throw new ArgumentException("Informe a pasta de saida: Estructto.exe --capturas <pasta_saida> [arquivo.estructto]");
                pastaSaida = Path.GetFullPath(pastaSaida);
                Directory.CreateDirectory(pastaSaida);
                string arqErros = Path.Combine(pastaSaida, "erros.txt");
                if (File.Exists(arqErros)) File.Delete(arqErros);

                string caminhoModelo = ResolverCaminhoModelo(arquivoEntrada);

                // 00) Folhas de icones (fundo do tema, fundo da ribbon e ampliada 3x)
                FolhaIcones("00-icones.png", TemaPlant.BgCanvas);
                FolhaIcones("00-icones-ribbon.png", TemaPlant.Surface1);
                FolhaIcones("00-icones-16x3.png", TemaPlant.Surface1, 3);
                FolhaIcones("00-icones-32x2.png", TemaPlant.Surface1, 2, 32);

                // 01) Tela inicial e Backstage
                w.MontarTelaInicial();
                w.TelaInicial.Visibility = Visibility.Visible;
                w.Bastidor.IsOpen = false;
                CapturarPrincipal(w, "01-inicio");

                w.Bastidor.IsOpen = true;
                EsperarIdle();
                CapturarPrincipal(w, "01-backstage");
                w.MostrarRecentesBs();
                EsperarIdle();
                CapturarPrincipal(w, "01-backstage-recentes");
                w.Bastidor.IsOpen = false;
                EsperarIdle();

                // Abre o modelo e calcula (F5)
                w.Abrir(caminhoModelo);
                w.TelaInicial.Visibility = Visibility.Collapsed;
                w.Resolver(true);
                EsperarIdle();
                if (Environment.GetEnvironmentVariable("ESTRUCTTO_CAPTURAS") == "planos")   // só as capturas 43) de níveis e planos
                {
                    string dev = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "..", "motor"));
                    if (Environment.GetEnvironmentVariable("ESTRUCTTO_MOTOR") == null && Directory.Exists(dev)) Environment.SetEnvironmentVariable("ESTRUCTTO_MOTOR", dev);
                    CapturasPlanos(w);
                    w.alterado = false;
                    w.Close();
                    Environment.Exit(0);
                }
                if (Environment.GetEnvironmentVariable("ESTRUCTTO_CAPTURAS") == "ligacoes")   // só as capturas 49) do gerenciador de ligações
                {
                    CapturasLigacoes(w);
                    w.alterado = false;
                    w.Close();
                    Environment.Exit(0);
                }
                if (Environment.GetEnvironmentVariable("ESTRUCTTO_CAPTURAS") == "desenho3d")   // só as capturas 43) do desenho no 3D
                {
                    CapturasDesenho3D(w);
                    w.alterado = false;
                    w.Close();
                    Environment.Exit(0);
                }
                if (Environment.GetEnvironmentVariable("ESTRUCTTO_CAPTURAS") == "3d")   // só as capturas 42) da vista 3D
                {
                    string dev = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "..", "motor"));
                    if (Environment.GetEnvironmentVariable("ESTRUCTTO_MOTOR") == null && Directory.Exists(dev)) Environment.SetEnvironmentVariable("ESTRUCTTO_MOTOR", dev);
                    bool antes = Config.Realista3D;
                    CapturasRealista(w);
                    w.DefinirRealista(antes);
                    w.alterado = false;
                    w.Close();
                    Environment.Exit(0);
                }

                // 02) Modelo aberto
                w.MostrarDiagrama(Diagrama.Nenhum);
                w.Faixa.SelectedTabItem = w.abaModelo;
                CapturarPrincipal(w, "02-modelo");

                // 03) Deformada
                w.MostrarDiagrama(Diagrama.Deformada);
                CapturarPrincipal(w, "03-deformada");

                // 04) Normal
                w.MostrarDiagrama(Diagrama.Normal);
                CapturarPrincipal(w, "04-normal");

                // 05) Cortante
                w.MostrarDiagrama(Diagrama.Cortante);
                CapturarPrincipal(w, "05-cortante");

                // 06) Momento
                w.MostrarDiagrama(Diagrama.Momento);
                CapturarPrincipal(w, "06-momento");

                // 07) Verificacao de barras
                w.MostrarDiagrama(Diagrama.Verificacao);
                w.AbasTabela.SelectedIndex = 2;
                CapturarPrincipal(w, "07-verificacao-barras");
                w.FiltrarCriticos(true);
                CapturarPrincipal(w, "07-verificacao-criticos");
                w.FiltrarCriticos(false);
                w.AbasTabela.SelectedIndex = 0;

                // 08) Ferramenta Medir com uma medida feita entre 2 nos
                w.MostrarDiagrama(Diagrama.Nenhum);
                w.Faixa.SelectedTabItem = w.abaModelo;
                No nMedA = w.M.NoPorId(4) ?? w.M.Nos.FirstOrDefault();
                No nMedB = w.M.NoPorId(3) ?? w.M.Nos.LastOrDefault();
                CapturarPrincipal(w, "08-medir", () =>
                {
                    if (nMedA != null && nMedB != null)
                    {
                        w.UsarFerramenta(Ferramenta.Medir);
                        w.PontoMedida(nMedA.X, nMedA.Y);
                        w.PontoMedida(nMedB.X, nMedB.Y);
                    }
                });
                w.UsarFerramenta(Ferramenta.Selecionar);

                // 35) HUD da vista (momento) + recorte do cartao; 36) mapa de calor; 37) inspecao com o mouse; 38) toast; 39) historico aberto
                w.MostrarDiagrama(Diagrama.Momento);
                w.Faixa.SelectedTabItem = w.abaAnalise;
                CapturarPrincipal(w, "35-hud");
                {
                    w.Hud.Measure(new Size(600, 300)); w.Hud.Arrange(new Rect(0, 0, w.Hud.DesiredSize.Width, w.Hud.DesiredSize.Height)); w.Hud.UpdateLayout();
                    var rh = new RenderTargetBitmap((int)Math.Ceiling(w.Hud.DesiredSize.Width) * 2, (int)Math.Ceiling(w.Hud.DesiredSize.Height) * 2, 192, 192, PixelFormats.Pbgra32);
                    var dvh = new DrawingVisual();
                    using (var dch = dvh.RenderOpen()) { dch.DrawRectangle(TemaPlant.BBgCanvas, null, new Rect(0, 0, 2000, 2000)); }
                    rh.Render(dvh); rh.Render(w.Hud);
                    SalvarPng(rh, Path.Combine(pastaSaida, "35-hud-cartao.png"));
                    w.UpdateLayout();
                }
                w.MostrarDiagrama(Diagrama.Verificacao, true);
                w.AbasTabela.SelectedIndex = 2;
                CapturarPrincipal(w, "36-mapa-calor");
                w.AbasTabela.SelectedIndex = 0;
                foreach (var tipoInsp in new[] { Diagrama.Momento, Diagrama.Deformada })
                {
                    w.MostrarDiagrama(tipoInsp);
                    Barra bInsp = w.M.Barras.Where(x => x.Res != null).OrderByDescending(x => w.M.Comp(x)).FirstOrDefault();
                    if (bInsp == null) break;
                    Action simula = () =>
                    {
                        No ia = w.M.NoPorId(bInsp.N1), ic = w.M.NoPorId(bInsp.N2);
                        w.Vista.SimularMouse(w.Vista.Tela(ia.X + (ic.X - ia.X) * 0.42, ia.Y + (ic.Y - ia.Y) * 0.42));
                    };
                    CapturarPrincipal(w, tipoInsp == Diagrama.Momento ? "37-inspecao-mouse" : "37-inspecao-mouse-deformada", simula);
                    w.Vista.SimularMouse(null);
                }
                w.MostrarDiagrama(Diagrama.Nenhum);
                w.MostrarToast("Calculado — caso G - Permanente. ΣRx = 0,00 kN  ΣRy = 5,88 kN", true);
                CapturarPrincipal(w, "38-toast");
                w.Toast.Visibility = Visibility.Collapsed;
                w.HistoricoCaixa.Visibility = Visibility.Visible;
                CapturarPrincipal(w, "39-historico");
                w.HistoricoCaixa.Visibility = Visibility.Collapsed;
                w.Vista.Etiqueta = "BARRA — próximo ponto: clique, ou digite o comprimento (na direção do mouse), x,y, @dx,dy, @L<ângulo.   Botão direito/Esc termina.";
                CapturarPrincipal(w, "39-etiqueta");
                w.Vista.Etiqueta = null;
                w.MostrarDiagrama(Diagrama.Nenhum);

                // 40) Resultado do motor Python (envoltoria ELU NBR 8681, verificacao NBR 8800): momento, mapa de calor e reacoes.
                // Em desenvolvimento acha o motor em ..\motor (capturar.ps1 roda de codigo\Estructto).
                {
                    string dev = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "..", "motor"));
                    if (Environment.GetEnvironmentVariable("ESTRUCTTO_MOTOR") == null && Directory.Exists(dev)) Environment.SetEnvironmentVariable("ESTRUCTTO_MOTOR", dev);
                    string xml = w.M.ParaXml();
                    MotorResultado rm = null;
                    try { rm = System.Threading.Tasks.Task.Run(() => MotorModelo.Calcular(Modelo.DeXml(xml))).GetAwaiter().GetResult(); }
                    catch (Exception ex) { File.AppendAllText(Path.Combine(pastaSaida, "erros.txt"), "40-motor-python: " + ex.Message + Environment.NewLine); }
                    if (rm != null)
                    {
                        w.MostrarMotor(rm, xml);
                        w.Faixa.SelectedTabItem = w.abaAnalise;
                        w.MostrarDiagrama(Diagrama.Momento);
                        CapturarPrincipal(w, "40-motor-python-momento");
                        w.MostrarDiagrama(Diagrama.Verificacao, true);
                        w.AbasTabela.SelectedIndex = 2;
                        CapturarPrincipal(w, "40-motor-python-mapa-calor");
                        w.AbasTabela.SelectedIndex = 0;
                        bool reac = w.Vista.MostrarReacoes; w.Vista.MostrarReacoes = true;
                        w.MostrarDiagrama(Diagrama.Nenhum);
                        CapturarPrincipal(w, "40-motor-python-reacoes");
                        w.Vista.MostrarReacoes = reac;
                        // 41) Relatório do motor (WebView2) com o anexo das placas: janela real na tela (o WebView2 não desenha fora dela)
                        try
                        {
                            var jr = RelatorioMotor.Abrir(w, "guincho-com-placa", w.AnexoPlacas);
                            jr.Left = 0; jr.Top = 0;
                            Esperar(jr.Pronto);
                            EsperarIdle(); System.Threading.Thread.Sleep(800); EsperarIdle();
                            Esperar(jr.SalvarPng(Path.Combine(pastaSaida, "41-relatorio-motor.png")));
                            Esperar(jr.GravarPdf(Path.Combine(pastaSaida, "41-relatorio-motor.pdf")));
                            jr.Close();
                        }
                        catch (Exception ex) { File.AppendAllText(Path.Combine(pastaSaida, "erros.txt"), "41-relatorio-motor: " + ex.Message + Environment.NewLine); }
                        w.EscolherResultado("ativo");
                    }
                    Motor.Desligar();
                }

                // 09) Cada aba da Ribbon (incluindo abas contextuais Barra e No)
                foreach (var tab in w.Faixa.Tabs.ToList())
                {
                    if (tab == w.ctxBarra || tab == w.ctxNo) continue;
                    string slug = Slug(tab.Header as string);
                    w.Vista.Selecionar(new object[0]);
                    w.Faixa.SelectedTabItem = tab;
                    EsperarIdle();
                    CapturarPrincipal(w, "09-ribbon-" + slug, () =>
                    {
                        w.Faixa.SelectedTabItem = tab;
                        TemaPlant.AjustarFaixa(w.Faixa, null);
                    });
                }

                if (w.M.Barras.Count > 0 && w.ctxBarra != null)
                {
                    w.Vista.Selecionar(new object[] { w.M.Barras[0] });
                    w.Faixa.SelectedTabItem = w.ctxBarra;
                    EsperarIdle();
                    CapturarPrincipal(w, "09-ribbon-barra", () =>
                    {
                        w.Vista.Selecionar(new object[] { w.M.Barras[0] });
                        w.Faixa.SelectedTabItem = w.ctxBarra;
                        TemaPlant.AjustarFaixa(w.Faixa, null);
                    });
                }

                if (w.M.Nos.Count > 0 && w.ctxNo != null)
                {
                    w.Vista.Selecionar(new object[] { w.M.Nos[0] });
                    w.Faixa.SelectedTabItem = w.ctxNo;
                    EsperarIdle();
                    CapturarPrincipal(w, "09-ribbon-no", () =>
                    {
                        w.Vista.Selecionar(new object[] { w.M.Nos[0] });
                        w.Faixa.SelectedTabItem = w.ctxNo;
                        TemaPlant.AjustarFaixa(w.Faixa, null);
                    });
                }

                w.Vista.Selecionar(new object[0]);
                w.Faixa.SelectedTabItem = w.abaModelo;
                EsperarIdle();

                // 10) Menu de contexto
                object alvoMenu = (object)w.M.Barras.FirstOrDefault() ?? w.M.Nos.FirstOrDefault();
                if (alvoMenu != null) w.Vista.Selecionar(new[] { alvoMenu });
                ContextMenu cm = w.CriarMenuContexto(alvoMenu);
                cm.PlacementTarget = w.Vista;
                cm.IsOpen = true;
                EsperarIdle();
                Func<Point> posMenu = () =>
                {
                    Point pVista = new Point(w.Vista.ActualWidth * 0.48, w.Vista.ActualHeight * 0.42);
                    if (alvoMenu is Barra bm)
                    {
                        No na = w.M.NoPorId(bm.N1), nb = w.M.NoPorId(bm.N2);
                        if (na != null && nb != null)
                            pVista = w.Vista.Tela((na.X + nb.X) / 2, (na.Y + nb.Y) / 2);
                    }
                    try { return w.Vista.TranslatePoint(pVista, w); }
                    catch { return new Point(520, 320); }
                };
                CapturarPrincipal(w, "10-menu-contexto", null, cm, posMenu);
                cm.IsOpen = false;
                w.Vista.Selecionar(new object[0]);
                EsperarIdle();

                // 30-34) Inspector WPF: modelo, no, barra, carga e multi-selecao (tela inteira + recorte do painel completo)
                {
                    Action<string, IEnumerable<object>> inspetor = (nome, sel) =>
                    {
                        Action prep = () => w.Vista.Selecionar(sel.ToArray());
                        prep();
                        EsperarIdle();
                        CapturarPrincipal(w, nome, prep);
                        w.Insp.UpdateLayout();
                        SalvarPng(w.Insp.Renderizar(), Path.Combine(pastaSaida, nome + "-painel.png"));
                    };
                    inspetor("30-inspector-modelo", new object[0]);
                    No noI = w.M.Nos.FirstOrDefault(x => x.TemApoio && x.Res != null) ?? w.M.Nos.FirstOrDefault();
                    if (noI != null) inspetor("31-inspector-no", new object[] { noI });
                    Barra barI = w.M.Barras.FirstOrDefault(x => x.Res != null) ?? w.M.Barras.FirstOrDefault();
                    if (barI != null) inspetor("32-inspector-barra", new object[] { barI });
                    object cargaI = (object)w.M.Barras.FirstOrDefault(x => x.TemCarga) ?? w.M.Nos.FirstOrDefault(x => x.TemCarga);
                    if (cargaI != null) inspetor("33-inspector-carga", new object[] { cargaI });
                    if (w.M.Barras.Count > 1) inspetor("34-inspector-multi", w.M.Barras.Take(3).Cast<object>().ToList());

                    // seletor de perfil (popup nao entra na captura da janela: renderiza o conteudo a parte)
                    if (barI != null)
                    {
                        w.Vista.Selecionar(new object[] { barI });
                        EsperarIdle();
                        var pc = w.Insp.ConteudoSeletorPerfil("");
                        if (pc != null)
                        {
                            var fundoPop = new Border { Background = TemaPlant.BSup1, Padding = new Thickness(12), Child = pc };
                            fundoPop.Measure(new Size(330, 420)); fundoPop.Arrange(new Rect(0, 0, 330, fundoPop.DesiredSize.Height)); fundoPop.UpdateLayout();
                            var rb = new RenderTargetBitmap(330, Math.Max(1, (int)Math.Ceiling(fundoPop.DesiredSize.Height)), 96, 96, PixelFormats.Pbgra32);
                            rb.Render(fundoPop);
                            SalvarPng(rb, Path.Combine(pastaSaida, "34-inspector-perfil-popup.png"));
                            fundoPop.Child = null;
                            w.Insp.DevolverSeletorPerfil(pc);
                        }
                    }

                    // teste por codigo: editar um campo pelo Inspector muda o modelo (com expressao) e o desfazer volta
                    var log = new StringBuilder();
                    Action<bool, string> confere = (ok, msg) => { log.AppendLine((ok ? "OK    " : "FALHA ") + msg); if (!ok) throw new InvalidOperationException("Teste do Inspector falhou: " + msg); };
                    try
                    {
                        Barra tb = w.M.Barras.First();
                        int idB = tb.Id;
                        double q0 = tb.QGlobalY;
                        w.Vista.Selecionar(new object[] { tb });
                        EsperarIdle();
                        confere(w.Insp.NomesDosCampos.Contains("QGlobalY") && w.Insp.NomesDosCampos.Contains("Secao") && w.Insp.NomesDosCampos.Contains("RotulaInicio"), "campos da barra presentes (" + w.Insp.NomesDosCampos.Count() + ")");
                        w.alterado = false;
                        confere(w.Insp.DefinirTexto("QGlobalY", "-12+2,5"), "DefinirTexto aceitou");
                        confere(Math.Abs(w.M.Barras.First(b => b.Id == idB).QGlobalY - (-9.5)) < 1e-9, "q vertical = -12+2,5 -> -9,5 (expressao e virgula)");
                        confere(w.alterado, "modelo marcado como modificado");
                        w.Desfazer();
                        EsperarIdle();
                        confere(Math.Abs(w.M.Barras.First(b => b.Id == idB).QGlobalY - q0) < 1e-9, "desfazer devolveu q vertical = " + q0);

                        // valor invalido nao altera
                        w.Vista.Selecionar(new object[] { w.M.Barras.First(b => b.Id == idB) });
                        EsperarIdle();
                        w.Insp.DefinirTexto("QGlobalY", "abc");
                        confere(Math.Abs(w.M.Barras.First(b => b.Id == idB).QGlobalY - q0) < 1e-9, "texto invalido nao altera o modelo");

                        // multi-selecao aplica a todos
                        var tres = w.M.Barras.Take(3).Cast<object>().ToList();
                        int[] ids = tres.Cast<Barra>().Select(b => b.Id).ToArray();
                        w.Vista.Selecionar(tres);
                        EsperarIdle();
                        w.Insp.DefinirTexto("QGlobalX", "1,5");
                        confere(ids.All(i => Math.Abs(w.M.Barras.First(b => b.Id == i).QGlobalX - 1.5) < 1e-9), "multi-selecao: q horizontal = 1,5 em " + ids.Length + " barras");
                        w.Desfazer();
                        EsperarIdle();

                        // no: coordenada com expressao
                        No nt = w.M.Nos.First();
                        int idN = nt.Id; double x0 = nt.X;
                        w.Vista.Selecionar(new object[] { nt });
                        EsperarIdle();
                        w.Insp.DefinirTexto("X", "(" + x0.ToString(CultureInfo.InvariantCulture) + "+1)*2/2");
                        confere(Math.Abs(w.M.NoPorId(idN).X - (x0 + 1)) < 1e-9, "no: X = (x+1)*2/2 -> x+1");
                        w.Desfazer();
                        EsperarIdle();
                        confere(Math.Abs(w.M.NoPorId(idN).X - x0) < 1e-9, "desfazer devolveu X do no");
                        log.AppendLine("TUDO OK");
                    }
                    finally
                    {
                        File.WriteAllText(Path.Combine(pastaSaida, "35-teste-inspector.txt"), log.ToString(), new UTF8Encoding(false));
                    }
                    w.alterado = false;
                    w.Vista.Selecionar(new object[0]);
                    EsperarIdle();
                }

                // 38/39) Paleta de comandos (Ctrl+K) e entrada dinamica: testes por codigo + capturas
                {
                    var logP = new StringBuilder();
                    int falhasP = 0;
                    try { falhasP = w.TestesPaletaEDinamica(logP); }
                    catch (Exception exP) { logP.AppendLine("EXCECAO: " + exP); falhasP = 1; }
                    finally { File.WriteAllText(Path.Combine(pastaSaida, "38-teste-paleta-entrada.txt"), logP.ToString(), new UTF8Encoding(false)); }
                    w.alterado = false;
                    w.Vista.Selecionar(new object[0]);
                    EsperarIdle();
                    if (falhasP > 0) throw new InvalidOperationException("Testes da paleta/entrada dinamica falharam (ver 38-teste-paleta-entrada.txt).");

                    foreach (var par in new[] { new[] { "mo", "38-palette" }, new[] { "w 200", "38-palette-perfil" }, new[] { "nó 3", "38-palette-objeto" }, new[] { "", "38-palette-vazia" } })
                    {
                        string consulta = par[0];
                        var pv = new PaletaVisual(w) { };
                        pv.Caixa.Text = consulta;
                        CapturarPrincipal(w, par[1], null, pv, () => new Point(Math.Max(10, (w.Width - pv.Width) / 2), 96));
                    }

                    w.PrepararCapturaDinamica();
                    CapturarPrincipal(w, "39-entrada-dinamica", () => w.AjustarCapturaDinamica());
                    w.EncerrarCapturaDinamica();
                    EsperarIdle();
                }

                // 20) JanelaPlaca para cada no com placa e para o no 4 (pilar inclinado)
                var nosPlaca = w.M.Nos.Where(x => x.Placa != null).ToList();
                No no4 = w.M.NoPorId(4);
                if (no4 != null && !nosPlaca.Contains(no4) && PlacaCalc.Pilar(w.M, no4, out Secao _, out double _, out double _, out string _) != null)
                    nosPlaca.Add(no4);

                bool capturouParafuso = false;
                foreach (No no in nosPlaca)
                {
                    bool tinhaPlaca = no.Placa != null;
                    var jp = new JanelaPlaca(w.M, no);
                    jp.Candidatos = w.M.Nos.Where(x => x != no && (x.Placa != null || x.TemApoio) && PlacaCalc.Pilar(w.M, x, out Secao _, out double _, out double _, out string _) != null).ToList();
                    jp.AvaliarEm = (noCand, pl) =>
                    {
                        var p0 = noCand.Placa; noCand.Placa = pl;
                        try { return w.PlacaPior(noCand); }
                        finally { noCand.Placa = p0; }
                    };
                    PrepararDialogoForaDaTela(jp);
                    jp.Show();
                    EsperarIdle();

                    if (!tinhaPlaca)
                    {
                        jp.UsarModoParafuso(false);
                        EsperarIdle();
                    }

                    // Cada vista do seletor (Planta | 3D | Forças | Ficha | Cálculo) em 1600x900 e 1280x720
                    string[] vistas = { "planta", "3d", "forcas", "ficha", "calculo" };
                    string pre = "20-placa-no" + no.Id;
                    for (int v = 0; v < vistas.Length; v++)
                    {
                        int vv = v;
                        jp.MostrarAba(vv);
                        RenderizarJanela(jp, 1600, 900, pre + "-" + vistas[vv] + ".png", () => jp.MostrarAba(vv));
                        RenderizarJanela(jp, 1280, 720, pre + "-" + vistas[vv] + "-1280x720.png", () => jp.MostrarAba(vv));
                    }
                    jp.MostrarAba(0);
                    RenderizarJanela(jp, 1600, 900, pre + ".png", () => jp.MostrarAba(0));

                    // Painel "Trocar chumbador" aberto
                    RenderizarJanela(jp, 1600, 900, pre + "-trocar.png", () => { jp.MostrarAba(0); jp.AbrirTrocar(); });
                    jp.FecharTrocar();

                    // 29) Sugerir (popup leve) e Aplicar também em… (pior caso)
                    if (no.Id == 1 || nosPlaca.Count == 1)
                    {
                        RenderizarJanela(jp, 1600, 900, "29-placa-sugerir.png", () => { jp.MostrarAba(0); jp.AbrirSugerir(); });
                        RenderizarJanela(jp, 1280, 720, "29-placa-sugerir-1280x720.png", () => { jp.MostrarAba(0); jp.AbrirSugerir(); });
                        RenderizarJanela(jp, 1600, 900, "29-placa-aplicar-em.png", () => { jp.MostrarAba(0); jp.AbrirAplicarEm(); });
                        RenderizarJanela(jp, 1280, 720, "29-placa-aplicar-em-1280x720.png", () => { jp.MostrarAba(0); jp.AbrirAplicarEm(); });
                        jp.FecharFlutuantes();
                    }

                    // 21) Modo parafuso
                    if (!capturouParafuso)
                    {
                        jp.UsarModoParafuso(true);
                        RenderizarJanela(jp, 1600, 900, "21-placa-parafuso.png", () => jp.MostrarAba(0));
                        RenderizarJanela(jp, 1280, 720, "21-placa-parafuso-1280x720.png", () => jp.MostrarAba(1));
                        capturouParafuso = true;
                    }

                    jp.Close();
                    EsperarIdle();
                }

                // 22) JanelaComparar com 3 fixacoes
                No noComp = w.M.Nos.FirstOrDefault(x => x.Placa != null) ?? w.M.Nos.FirstOrDefault();
                if (noComp != null)
                {
                    ResPlaca rpComp = PlacaCalc.Calcular(w.M, noComp);
                    var jc = new JanelaComparar(w.M, noComp, rpComp, noComp.Placa, new[] { "CB 12300", "WB 12400", "WQE 500 1/2\"" });
                    PrepararDialogoForaDaTela(jc);
                    jc.Show();
                    EsperarIdle();
                    RenderizarJanela(jc, 1600, 900, "22-comparar.png");
                    RenderizarJanela(jc, 1280, 720, "22-comparar-1280x720.png");
                    jc.Close();
                    EsperarIdle();
                }

                // 23) JanelaVerificacao para cada barra
                foreach (Barra b in w.M.Barras)
                {
                    var jv = new JanelaVerificacao(w.M, b);
                    PrepararDialogoForaDaTela(jv);
                    jv.Show();
                    EsperarIdle();
                    CapturarDialogo(jv, "23-verificacao-barra" + b.Id + ".png");
                    jv.SoPassam(true);
                    EsperarIdle();
                    CapturarDialogo(jv, "23-verificacao-barra" + b.Id + "-passam.png");
                    jv.Close();
                    EsperarIdle();
                }

                // 24) JanelaCasos
                var jCasos = new JanelaCasos(w.M);
                PrepararDialogoForaDaTela(jCasos);
                jCasos.Show();
                EsperarIdle();
                CapturarDialogo(jCasos, "24-casos.png");
                jCasos.Close();
                EsperarIdle();

                var mComb = Modelo.DeXml(w.M.ParaXml()); mComb.Combinacoes = Analise.GerarNbr(mComb.Casos);
                var jCasos2 = new JanelaCasos(mComb);
                PrepararDialogoForaDaTela(jCasos2);
                jCasos2.Show();
                EsperarIdle();
                CapturarDialogo(jCasos2, "24-casos-combinacoes.png");
                jCasos2.Close();
                EsperarIdle();

                // 25) JanelaRelatorio
                string nomeMod = Path.GetFileNameWithoutExtension(caminhoModelo);
                var jRel = new JanelaRelatorio(w.MontarRelatorio, nomeMod);
                PrepararDialogoForaDaTela(jRel);
                jRel.Show();
                EsperarIdle();
                CapturarDialogo(jRel, "25-relatorio.png", EsperarIdle);
                jRel.Close();
                EsperarIdle();

                // 26) JanelaAssistente para cada tipo
                string matPadrao = Biblioteca.Materiais.Count > 0 ? Biblioteca.Materiais[0].Nome : "Aço ASTM A572 Gr.50";
                foreach (string tipo in new[] { "viga", "portico", "trelica", "galpao" })
                {
                    var ja = new JanelaAssistente(tipo, "W 200 x 26,6", matPadrao);
                    PrepararDialogoForaDaTela(ja);
                    ja.Show();
                    EsperarIdle();
                    CapturarDialogo(ja, "26-assistente-" + tipo + ".png", () => ja.EnquadrarPrevia());
                    ja.Close();
                    EsperarIdle();
                }

                // 40) Vista 3D: modelo 2D convertido (plano XZ) e um pórtico espacial de entrada neutra
                var abaVista3 = w.Faixa.Tabs.First(t => (t.Header as string) == "Vista");
                w.Faixa.SelectedTabItem = abaVista3;
                w.MostrarDiagrama(Diagrama.Nenhum);
                w.MostrarVista3D(true);
                EsperarIdle();
                w.vista3D.MostrarVista(VistaPadrao.Iso, false);
                CapturarPrincipal(w, "40-vista3d-iso");
                foreach (var (d, nome) in new[] { (Diagrama.Deformada, "deformada"), (Diagrama.Momento, "momento"), (Diagrama.Normal, "normal"), (Diagrama.Verificacao, "aproveitamento") })
                {
                    w.MostrarDiagrama(d, d == Diagrama.Verificacao);
                    w.Faixa.SelectedTabItem = abaVista3;
                    EsperarIdle();
                    w.vista3D.MostrarVista(VistaPadrao.Iso, false);
                    CapturarPrincipal(w, "40-vista3d-" + nome);
                }
                w.MostrarDiagrama(Diagrama.Momento);
                w.Faixa.SelectedTabItem = abaVista3;
                foreach (var (v, nome) in new[] { (VistaPadrao.Frente, "frente"), (VistaPadrao.Topo, "topo"), (VistaPadrao.Lado, "lado") })
                {
                    w.vista3D.MostrarVista(v, false);
                    CapturarPrincipal(w, "40-vista3d-" + nome);
                }
                // pórtico espacial 2 x 2 vãos, 2 pavimentos, com aproveitamento, deformada e uma barra escolhida
                bool realAntes = Config.Realista3D;
                w.DefinirRealista(false);   // 40) é a vista analítica; a Realista tem as capturas 42)
                var esp = Portico2x2();
                w.vista3D.Diagrama = Diagrama3D.Nenhum; w.vista3D.MostrarDeformada = false; w.vista3D.MostrarAproveitamento = true;
                w.vista3D.Carregar(esp, false);
                w.vista3D.Escolher(esp.Barras[4]);
                w.vista3D.MostrarVista(VistaPadrao.Iso, false);
                CapturarPrincipal(w, "40-vista3d-espacial");
                w.vista3D.MostrarAproveitamento = false; w.vista3D.MostrarDeformada = true; w.vista3D.EscalaDeformada = 1;
                w.vista3D.Carregar(esp, false);
                CapturarPrincipal(w, "40-vista3d-espacial-deformada");
                w.MostrarVista3D(false);
                w.MostrarDiagrama(Diagrama.Nenhum);
                EsperarIdle();

                // 42) Vista 3D Realista (perfis reais, sombra, arestas) lado a lado com a analítica
                CapturasRealista(w);
                w.DefinirRealista(realAntes);
                w.MostrarVista3D(false);

                // 41) Modelo espacial (motor Python, DocumentoEspacial.cs): edifício gerado em 3D, calculado, mapa de aproveitamento
                CapturasEspacial(w);
                CapturasPlanos(w);   // 43) níveis e planos de trabalho
                // 48) Edição em lote do modelo espacial (EdicaoEspacial.cs): pórtico copiado 3×, espelhado e rotacionado
                CapturasEdicao(w);
                // 43) Desenhar no 3D com o mouse
                CapturasDesenho3D(w);
                // 49) Gerenciador de ligações (grupos, pior do grupo, placa da pior base)
                CapturasLigacoes(w);

                // 27) Caixa de mensagem
                var jCaixa = Caixa.Criar(null, "Salvar alterações?",
                    "O modelo \"" + Path.GetFileName(caminhoModelo) + "\" tem alterações que ainda não foram salvas.",
                    "Se não salvar, as alterações feitas desde o último salvamento serão perdidas.",
                    new[] { ("Salvar", MessageBoxResult.Yes, true), ("Não salvar", MessageBoxResult.No, false), ("Cancelar", MessageBoxResult.Cancel, false) },
                    "!", JanelaEscura.BAmarelo);
                PrepararDialogoForaDaTela(jCaixa);
                jCaixa.Show();
                EsperarIdle();
                CapturarDialogo(jCaixa, "27-caixa-mensagem.png");
                jCaixa.Close();
                EsperarIdle();

                // 28) JanelaAjuda
                var jAjuda = new JanelaAjuda();
                PrepararDialogoForaDaTela(jAjuda);
                jAjuda.Show();
                EsperarIdle();
                CapturarDialogo(jAjuda, "28-ajuda.png");
                jAjuda.Buscar("carga");
                EsperarIdle();
                CapturarDialogo(jAjuda, "28-ajuda-busca.png");
                jAjuda.Buscar("zzzz");
                EsperarIdle();
                CapturarDialogo(jAjuda, "28-ajuda-vazio.png");
                jAjuda.Close();
                EsperarIdle();

                // 29) Janelas da aba Ações com dados de amostra do motor (edifício 4 pavimentos; sem chamar o motor)
                var am = System.Text.Json.Nodes.JsonNode.Parse(AmostraAcoes);
                void Cap(Window j, string nome) { PrepararDialogoForaDaTela(j); j.Show(); EsperarIdle(); CapturarDialogo(j, nome); j.Close(); EsperarIdle(); }
                Cap(new JanelaVento(am["resumo"], am["geracao"]), "29-acoes-vento.png");
                Cap(new JanelaVento(am["resumo"]), "29-acoes-vento-sem-gerar.png");
                Cap(new JanelaSismo(am["resumo"], am["geracao"]), "30-acoes-sismo.png");
                Cap(new JanelaSismo(System.Text.Json.Nodes.JsonNode.Parse("{\"config\":{}}")), "30-acoes-sismo-zona0.png");
                Cap(new JanelaPavimentos(am["pav"], am["nos"]), "31-acoes-pavimentos.png");
                Cap(new JanelaPavimentos(new System.Text.Json.Nodes.JsonArray(), am["nos"]), "31-acoes-pavimentos-vazio.png");
                Cap(new JanelaCasosCarga(am["casos"]), "32-acoes-casos.png");
                Cap(new JanelaCombinacoes(am["comb"], am["casos"], "comercial"), "33-acoes-combinacoes.png");
                CapturarAnalise();

                w.alterado = false;
                w.Close();
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                GravarErro(ex);
                Environment.Exit(1);
            }
        }

        // Amostra real do motor: exemplo edificio_concreto com vento (V0 35 m/s, cat. IV, classe B) e sismo (zona 2, terreno C), gerada por projeto.gerar_acoes.
        const string AmostraAcoes = """
            {"resumo":{"nome":"Edifício de concreto, 3x2 vãos de 5 m, 4 pavimentos","n_nos":60,"n_barras":116,"n_casos":10,"n_cargas":520,"casos":[{"nome":"G1","tipo":"G"},{"nome":"Q1","tipo":"Q"},{"nome":"V+X","tipo":"V"},{"nome":"V-X","tipo":"V"},{"nome":"V+Y","tipo":"V"},{"nome":"V-Y","tipo":"V"},{"nome":"SX","tipo":"S"},{"nome":"SY","tipo":"S"},{"nome":"SX_TOR","tipo":"S"},{"nome":"SY_TOR","tipo":"S"}],"pavimentos":[{"nome":"P1","z":3.0,"diafragma":true},{"nome":"P2","z":6.0,"diafragma":true},{"nome":"P3","z":9.0,"diafragma":true},{"nome":"P4","z":12.0,"diafragma":true}],"caixa":{"min":[0.0,0.0,0.0],"max":[15.0,10.0,12.0]},"peso_proprio":"G1","config":{"tipo_uso":"escritorio","variabilidade":"grandes","pdelta":false,"armadura":"CA-50","cobrimento_m":0.03,"caa":"II","tipo_edificacao":null,"vento":{"V0":35,"S1":1.0,"S3":1.0,"categoria":"IV","classe":"B","direcoes":["+X","-X","+Y","-Y"],"permeabilidade":"quatro_faces"},"sismo":{"zona":2,"classe_terreno":"C","categoria_utilizacao":"II","sistema_resistente":"portico_concreto_intermediario","sistema":"concreto_portico"}}},"geracao":{"casos":["V+X","V-X","V+Y","V-Y","SX","SY","SX_TOR","SY_TOR"],"forcas":{"V+X":[{"pavimento":"P1","z":3.0,"F_kN":13.167573738688315,"F_vet_kN":[13.167573738688315,0.0,0.0],"S2":0.7638643679894911,"Vk_m_s":26.73525287963219,"q_kN_m2":0.4381563066277212},{"pavimento":"P2","z":6.0,"F_kN":13.78164537544787,"F_vet_kN":[13.78164537544787,0.0,0.0],"S2":0.7814728740857426,"Vk_m_s":27.35155059300099,"q_kN_m2":0.45858978706283504},{"pavimento":"P3","z":9.0,"F_kN":15.251897760729706,"F_vet_kN":[15.251897760729706,0.0,0.0],"S2":0.8221012623778792,"Vk_m_s":28.77354418322577,"q_kN_m2":0.5075130259016599},{"pavimento":"P4","z":12.0,"F_kN":8.194615368463477,"F_vet_kN":[8.194615368463477,0.0,0.0],"S2":0.8522022120586455,"Vk_m_s":29.827077422052593,"q_kN_m2":0.5453582376427063}],"V-X":[{"pavimento":"P1","z":3.0,"F_kN":13.167573738688315,"F_vet_kN":[-13.167573738688315,0.0,0.0],"S2":0.7638643679894911,"Vk_m_s":26.73525287963219,"q_kN_m2":0.4381563066277212},{"pavimento":"P2","z":6.0,"F_kN":13.78164537544787,"F_vet_kN":[-13.78164537544787,0.0,0.0],"S2":0.7814728740857426,"Vk_m_s":27.35155059300099,"q_kN_m2":0.45858978706283504},{"pavimento":"P3","z":9.0,"F_kN":15.251897760729706,"F_vet_kN":[-15.251897760729706,0.0,0.0],"S2":0.8221012623778792,"Vk_m_s":28.77354418322577,"q_kN_m2":0.5075130259016599},{"pavimento":"P4","z":12.0,"F_kN":8.194615368463477,"F_vet_kN":[-8.194615368463477,0.0,0.0],"S2":0.8522022120586455,"Vk_m_s":29.827077422052593,"q_kN_m2":0.5453582376427063}],"V+Y":[{"pavimento":"P1","z":3.0,"F_kN":23.6874258545269,"F_vet_kN":[0.0,23.6874258545269,0.0],"S2":0.7638643679894911,"Vk_m_s":26.73525287963219,"q_kN_m2":0.4381563066277212},{"pavimento":"P2","z":6.0,"F_kN":24.792092261092925,"F_vet_kN":[0.0,24.792092261092925,0.0],"S2":0.7814728740857426,"Vk_m_s":27.35155059300099,"q_kN_m2":0.45858978706283504},{"pavimento":"P3","z":9.0,"F_kN":27.436960256893798,"F_vet_kN":[0.0,27.436960256893798,0.0],"S2":0.8221012623778792,"Vk_m_s":28.77354418322577,"q_kN_m2":0.5075130259016599},{"pavimento":"P4","z":12.0,"F_kN":14.7414662563478,"F_vet_kN":[0.0,14.7414662563478,0.0],"S2":0.8522022120586455,"Vk_m_s":29.827077422052593,"q_kN_m2":0.5453582376427063}],"V-Y":[{"pavimento":"P1","z":3.0,"F_kN":23.6874258545269,"F_vet_kN":[0.0,-23.6874258545269,0.0],"S2":0.7638643679894911,"Vk_m_s":26.73525287963219,"q_kN_m2":0.4381563066277212},{"pavimento":"P2","z":6.0,"F_kN":24.792092261092925,"F_vet_kN":[0.0,-24.792092261092925,0.0],"S2":0.7814728740857426,"Vk_m_s":27.35155059300099,"q_kN_m2":0.45858978706283504},{"pavimento":"P3","z":9.0,"F_kN":27.436960256893798,"F_vet_kN":[0.0,-27.436960256893798,0.0],"S2":0.8221012623778792,"Vk_m_s":28.77354418322577,"q_kN_m2":0.5075130259016599},{"pavimento":"P4","z":12.0,"F_kN":14.7414662563478,"F_vet_kN":[0.0,-14.7414662563478,0.0],"S2":0.8522022120586455,"Vk_m_s":29.827077422052593,"q_kN_m2":0.5453582376427063}],"SX":[{"pavimento":"P1","z":3.0,"F_kN":32.508536223277915,"F_vet_kN":[32.508536223277915,0.0,0.0]},{"pavimento":"P2","z":6.0,"F_kN":65.01707244655583,"F_vet_kN":[65.01707244655583,0.0,0.0]},{"pavimento":"P3","z":9.0,"F_kN":97.52560866983374,"F_vet_kN":[97.52560866983374,0.0,0.0]},{"pavimento":"P4","z":12.0,"F_kN":123.23003266033255,"F_vet_kN":[123.23003266033255,0.0,0.0]}],"SY":[{"pavimento":"P1","z":3.0,"F_kN":32.508536223277915,"F_vet_kN":[0.0,32.508536223277915,0.0]},{"pavimento":"P2","z":6.0,"F_kN":65.01707244655583,"F_vet_kN":[0.0,65.01707244655583,0.0]},{"pavimento":"P3","z":9.0,"F_kN":97.52560866983374,"F_vet_kN":[0.0,97.52560866983374,0.0]},{"pavimento":"P4","z":12.0,"F_kN":123.23003266033255,"F_vet_kN":[0.0,123.23003266033255,0.0]}],"SX_TOR":[{"pavimento":"P1","z":3.0,"F_kN":0.0,"F_vet_kN":[0.0,0.0,0.0],"Mz_kNm":16.254268111638957},{"pavimento":"P2","z":6.0,"F_kN":0.0,"F_vet_kN":[0.0,0.0,0.0],"Mz_kNm":32.508536223277915},{"pavimento":"P3","z":9.0,"F_kN":0.0,"F_vet_kN":[0.0,0.0,0.0],"Mz_kNm":48.76280433491687},{"pavimento":"P4","z":12.0,"F_kN":0.0,"F_vet_kN":[0.0,0.0,0.0],"Mz_kNm":61.61501633016628}],"SY_TOR":[{"pavimento":"P1","z":3.0,"F_kN":0.0,"F_vet_kN":[0.0,0.0,0.0],"Mz_kNm":24.381402167458436},{"pavimento":"P2","z":6.0,"F_kN":0.0,"F_vet_kN":[0.0,0.0,0.0],"Mz_kNm":48.76280433491687},{"pavimento":"P3","z":9.0,"F_kN":0.0,"F_vet_kN":[0.0,0.0,0.0],"Mz_kNm":73.14420650237531},{"pavimento":"P4","z":12.0,"F_kN":0.0,"F_vet_kN":[0.0,0.0,0.0],"Mz_kNm":92.42252449524942}]},"parametros":{"V+X":{"V0":35.0,"S1":1.0,"S3":1.0,"categoria":"IV","classe":"B","Ca":1.00174097230528,"l1":10.0,"l2":15.0,"h_topo":12.0,"h_l1":1.2,"l1_l2":0.6666666666666666,"S2_topo":0.8522022120586455,"Vk_topo_m_s":29.827077422052593,"q_topo_kN_m2":0.5453582376427063,"F_total_kN":50.39573224332937,"T1_estimado_s":0.5285469402368173,"edicao":"NBR 6123:2023"},"V-X":{"V0":35.0,"S1":1.0,"S3":1.0,"categoria":"IV","classe":"B","Ca":1.00174097230528,"l1":10.0,"l2":15.0,"h_topo":12.0,"h_l1":1.2,"l1_l2":0.6666666666666666,"S2_topo":0.8522022120586455,"Vk_topo_m_s":29.827077422052593,"q_topo_kN_m2":0.5453582376427063,"F_total_kN":50.39573224332937,"T1_estimado_s":0.5285469402368173,"edicao":"NBR 6123:2023"},"V+Y":{"V0":35.0,"S1":1.0,"S3":1.0,"categoria":"IV","classe":"B","Ca":1.2013686286135166,"l1":15.0,"l2":10.0,"h_topo":12.0,"h_l1":0.8,"l1_l2":1.5,"S2_topo":0.8522022120586455,"Vk_topo_m_s":29.827077422052593,"q_topo_kN_m2":0.5453582376427063,"F_total_kN":90.65794462886143,"T1_estimado_s":0.5285469402368173,"edicao":"NBR 6123:2023"},"V-Y":{"V0":35.0,"S1":1.0,"S3":1.0,"categoria":"IV","classe":"B","Ca":1.2013686286135166,"l1":15.0,"l2":10.0,"h_topo":12.0,"h_l1":0.8,"l1_l2":1.5,"S2_topo":0.8522022120586455,"Vk_topo_m_s":29.827077422052593,"q_topo_kN_m2":0.5453582376427063,"F_total_kN":90.65794462886143,"T1_estimado_s":0.5285469402368173,"edicao":"NBR 6123:2023"},"SX":{"ag":0.1,"Ca":1.2,"Cv":1.7,"ags0":0.12,"ags1":0.17,"Ta":0.43616321775288647,"Cup":1.7,"T":0.43616321775288647,"k":1.0,"Cs":0.07500000000000001,"R":5,"I":1.25,"zona":2,"classe_terreno":"C","sistema":"concreto_portico","edicao":"NBR 15421:2006","H_kN":318.28125000000006,"W_kN":4243.75,"sistema_resistente":"portico_concreto_intermediario","Cd":4.5,"Omega0":3},"SY":{"ag":0.1,"Ca":1.2,"Cv":1.7,"ags0":0.12,"ags1":0.17,"Ta":0.43616321775288647,"Cup":1.7,"T":0.43616321775288647,"k":1.0,"Cs":0.07500000000000001,"R":5,"I":1.25,"zona":2,"classe_terreno":"C","sistema":"concreto_portico","edicao":"NBR 15421:2006","H_kN":318.28125000000006,"W_kN":4243.75,"sistema_resistente":"portico_concreto_intermediario","Cd":4.5,"Omega0":3},"SX_TOR":{"e_m":0.5,"L_perp_m":10.0,"edicao":"NBR 15421:2006"},"SY_TOR":{"e_m":0.75,"L_perp_m":15.0,"edicao":"NBR 15421:2006"}},"avisos":[{"nivel":"aviso","codigo":"vento_dimensoes_da_caixa","mensagem":"dimensões do vento tiradas da caixa do modelo (lx = 15 m, ly = 10 m)","ref":null},{"nivel":"aviso","codigo":"sismo_edicao_2006","mensagem":"Ações sísmicas pela NBR 15421:2006; a edição 2023 alterou o mapa de zonas e o espectro (valores não confirmados em fonte pública). Verificar zona e resultado.","ref":null}]},"pav":[{"nome":"P1","z":3.0,"diafragma":true,"id":"P1"},{"nome":"P2","z":6.0,"diafragma":true,"id":"P2"},{"nome":"P3","z":9.0,"diafragma":true,"id":"P3"},{"nome":"P4","z":12.0,"diafragma":true,"id":"P4"}],"nos":[{"z":0.0},{"z":3.0},{"z":6.0},{"z":9.0},{"z":12.0}],"casos":[{"nome":"G1","tipo":"G","subtipo":"","id":"G1"},{"nome":"Q1","tipo":"Q","subtipo":"escritorio","id":"Q1"},{"nome":"V+X","tipo":"V","subtipo":"vento","id":"V+X"},{"nome":"V-X","tipo":"V","subtipo":"vento","id":"V-X"},{"nome":"V+Y","tipo":"V","subtipo":"vento","id":"V+Y"},{"nome":"V-Y","tipo":"V","subtipo":"vento","id":"V-Y"},{"nome":"SX","tipo":"S","subtipo":"","id":"SX"},{"nome":"SY","tipo":"S","subtipo":"","id":"SY"},{"nome":"SX_TOR","tipo":"S","subtipo":"","id":"SX_TOR"},{"nome":"SY_TOR","tipo":"S","subtipo":"","id":"SY_TOR"}],"comb":[{"nome":"ELU G","estado":"ELU","fatores":{"G1":1.4},"detalhe":{"G1":{"gamma":1.4,"psi":1.0,"produto":1.4}}},{"nome":"ELU V+X princ.","estado":"ELU","fatores":{"G1":1.4,"V+X":1.4},"detalhe":{"G1":{"gamma":1.4,"psi":1.0,"produto":1.4},"V+X":{"gamma":1.4,"psi":1.0,"produto":1.4}}},{"nome":"ELU V-X princ.","estado":"ELU","fatores":{"G1":1.4,"V-X":1.4},"detalhe":{"G1":{"gamma":1.4,"psi":1.0,"produto":1.4},"V-X":{"gamma":1.4,"psi":1.0,"produto":1.4}}},{"nome":"ELU V+Y princ.","estado":"ELU","fatores":{"G1":1.4,"V+Y":1.4},"detalhe":{"G1":{"gamma":1.4,"psi":1.0,"produto":1.4},"V+Y":{"gamma":1.4,"psi":1.0,"produto":1.4}}},{"nome":"ELU V-Y princ.","estado":"ELU","fatores":{"G1":1.4,"V-Y":1.4},"detalhe":{"G1":{"gamma":1.4,"psi":1.0,"produto":1.4},"V-Y":{"gamma":1.4,"psi":1.0,"produto":1.4}}},{"nome":"ELU Q1 princ.","estado":"ELU","fatores":{"G1":1.4,"Q1":1.4},"detalhe":{"G1":{"gamma":1.4,"psi":1.0,"produto":1.4},"Q1":{"gamma":1.4,"psi":1.0,"produto":1.4}}},{"nome":"ELU Q1 princ. + V+X","estado":"ELU","fatores":{"G1":1.4,"Q1":1.4,"V+X":0.84},"detalhe":{"G1":{"gamma":1.4,"psi":1.0,"produto":1.4},"Q1":{"gamma":1.4,"psi":1.0,"produto":1.4},"V+X":{"gamma":1.4,"psi":0.6,"produto":0.84}}},{"nome":"ELU V+X princ. + Q1","estado":"ELU","fatores":{"G1":1.4,"Q1":0.9799999999999999,"V+X":1.4},"detalhe":{"G1":{"gamma":1.4,"psi":1.0,"produto":1.4},"Q1":{"gamma":1.4,"psi":0.7,"produto":0.9799999999999999},"V+X":{"gamma":1.4,"psi":1.0,"produto":1.4}}},{"nome":"ELU Q1 princ. + V-X","estado":"ELU","fatores":{"G1":1.4,"Q1":1.4,"V-X":0.84},"detalhe":{"G1":{"gamma":1.4,"psi":1.0,"produto":1.4},"Q1":{"gamma":1.4,"psi":1.0,"produto":1.4},"V-X":{"gamma":1.4,"psi":0.6,"produto":0.84}}},{"nome":"ELU V-X princ. + Q1","estado":"ELU","fatores":{"G1":1.4,"Q1":0.9799999999999999,"V-X":1.4},"detalhe":{"G1":{"gamma":1.4,"psi":1.0,"produto":1.4},"Q1":{"gamma":1.4,"psi":0.7,"produto":0.9799999999999999},"V-X":{"gamma":1.4,"psi":1.0,"produto":1.4}}},{"nome":"ELU Q1 princ. + V+Y","estado":"ELU","fatores":{"G1":1.4,"Q1":1.4,"V+Y":0.84},"detalhe":{"G1":{"gamma":1.4,"psi":1.0,"produto":1.4},"Q1":{"gamma":1.4,"psi":1.0,"produto":1.4},"V+Y":{"gamma":1.4,"psi":0.6,"produto":0.84}}},{"nome":"ELU V+Y princ. + Q1","estado":"ELU","fatores":{"G1":1.4,"Q1":0.9799999999999999,"V+Y":1.4},"detalhe":{"G1":{"gamma":1.4,"psi":1.0,"produto":1.4},"Q1":{"gamma":1.4,"psi":0.7,"produto":0.9799999999999999},"V+Y":{"gamma":1.4,"psi":1.0,"produto":1.4}}},{"nome":"ELU Q1 princ. + V-Y","estado":"ELU","fatores":{"G1":1.4,"Q1":1.4,"V-Y":0.84},"detalhe":{"G1":{"gamma":1.4,"psi":1.0,"produto":1.4},"Q1":{"gamma":1.4,"psi":1.0,"produto":1.4},"V-Y":{"gamma":1.4,"psi":0.6,"produto":0.84}}},{"nome":"ELU V-Y princ. + Q1","estado":"ELU","fatores":{"G1":1.4,"Q1":0.9799999999999999,"V-Y":1.4},"detalhe":{"G1":{"gamma":1.4,"psi":1.0,"produto":1.4},"Q1":{"gamma":1.4,"psi":0.7,"produto":0.9799999999999999},"V-Y":{"gamma":1.4,"psi":1.0,"produto":1.4}}},{"nome":"ELU G fav","estado":"ELU","fatores":{"G1":1.0},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0}}},{"nome":"ELU V+X princ. G fav","estado":"ELU","fatores":{"G1":1.0,"V+X":1.4},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"V+X":{"gamma":1.4,"psi":1.0,"produto":1.4}}},{"nome":"ELU V-X princ. G fav","estado":"ELU","fatores":{"G1":1.0,"V-X":1.4},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"V-X":{"gamma":1.4,"psi":1.0,"produto":1.4}}},{"nome":"ELU V+Y princ. G fav","estado":"ELU","fatores":{"G1":1.0,"V+Y":1.4},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"V+Y":{"gamma":1.4,"psi":1.0,"produto":1.4}}},{"nome":"ELU V-Y princ. G fav","estado":"ELU","fatores":{"G1":1.0,"V-Y":1.4},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"V-Y":{"gamma":1.4,"psi":1.0,"produto":1.4}}},{"nome":"ELU Q1 princ. G fav","estado":"ELU","fatores":{"G1":1.0,"Q1":1.4},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"Q1":{"gamma":1.4,"psi":1.0,"produto":1.4}}},{"nome":"ELU Q1 princ. + V+X G fav","estado":"ELU","fatores":{"G1":1.0,"Q1":1.4,"V+X":0.84},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"Q1":{"gamma":1.4,"psi":1.0,"produto":1.4},"V+X":{"gamma":1.4,"psi":0.6,"produto":0.84}}},{"nome":"ELU V+X princ. + Q1 G fav","estado":"ELU","fatores":{"G1":1.0,"Q1":0.9799999999999999,"V+X":1.4},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"Q1":{"gamma":1.4,"psi":0.7,"produto":0.9799999999999999},"V+X":{"gamma":1.4,"psi":1.0,"produto":1.4}}},{"nome":"ELU Q1 princ. + V-X G fav","estado":"ELU","fatores":{"G1":1.0,"Q1":1.4,"V-X":0.84},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"Q1":{"gamma":1.4,"psi":1.0,"produto":1.4},"V-X":{"gamma":1.4,"psi":0.6,"produto":0.84}}},{"nome":"ELU V-X princ. + Q1 G fav","estado":"ELU","fatores":{"G1":1.0,"Q1":0.9799999999999999,"V-X":1.4},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"Q1":{"gamma":1.4,"psi":0.7,"produto":0.9799999999999999},"V-X":{"gamma":1.4,"psi":1.0,"produto":1.4}}},{"nome":"ELU Q1 princ. + V+Y G fav","estado":"ELU","fatores":{"G1":1.0,"Q1":1.4,"V+Y":0.84},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"Q1":{"gamma":1.4,"psi":1.0,"produto":1.4},"V+Y":{"gamma":1.4,"psi":0.6,"produto":0.84}}},{"nome":"ELU V+Y princ. + Q1 G fav","estado":"ELU","fatores":{"G1":1.0,"Q1":0.9799999999999999,"V+Y":1.4},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"Q1":{"gamma":1.4,"psi":0.7,"produto":0.9799999999999999},"V+Y":{"gamma":1.4,"psi":1.0,"produto":1.4}}},{"nome":"ELU Q1 princ. + V-Y G fav","estado":"ELU","fatores":{"G1":1.0,"Q1":1.4,"V-Y":0.84},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"Q1":{"gamma":1.4,"psi":1.0,"produto":1.4},"V-Y":{"gamma":1.4,"psi":0.6,"produto":0.84}}},{"nome":"ELU V-Y princ. + Q1 G fav","estado":"ELU","fatores":{"G1":1.0,"Q1":0.9799999999999999,"V-Y":1.4},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"Q1":{"gamma":1.4,"psi":0.7,"produto":0.9799999999999999},"V-Y":{"gamma":1.4,"psi":1.0,"produto":1.4}}},{"nome":"ELU EXC SX + SX_TOR","estado":"ELU_EXC","fatores":{"G1":1.2,"SX":1.0,"SX_TOR":1.0,"Q1":0.4},"detalhe":{"G1":{"gamma":1.2,"psi":1.0,"produto":1.2},"SX":{"gamma":1.0,"psi":1.0,"produto":1.0},"SX_TOR":{"gamma":1.0,"psi":1.0,"produto":1.0},"Q1":{"gamma":1.0,"psi":0.4,"produto":0.4}}},{"nome":"ELU EXC SX + SX_TOR sem Q1","estado":"ELU_EXC","fatores":{"G1":1.2,"SX":1.0,"SX_TOR":1.0},"detalhe":{"G1":{"gamma":1.2,"psi":1.0,"produto":1.2},"SX":{"gamma":1.0,"psi":1.0,"produto":1.0},"SX_TOR":{"gamma":1.0,"psi":1.0,"produto":1.0}}},{"nome":"ELU EXC SX + SX_TOR G fav","estado":"ELU_EXC","fatores":{"G1":1.0,"SX":1.0,"SX_TOR":1.0,"Q1":0.4},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"SX":{"gamma":1.0,"psi":1.0,"produto":1.0},"SX_TOR":{"gamma":1.0,"psi":1.0,"produto":1.0},"Q1":{"gamma":1.0,"psi":0.4,"produto":0.4}}},{"nome":"ELU EXC SX + SX_TOR sem Q1 G fav","estado":"ELU_EXC","fatores":{"G1":1.0,"SX":1.0,"SX_TOR":1.0},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"SX":{"gamma":1.0,"psi":1.0,"produto":1.0},"SX_TOR":{"gamma":1.0,"psi":1.0,"produto":1.0}}},{"nome":"ELU EXC SX - SX_TOR","estado":"ELU_EXC","fatores":{"G1":1.2,"SX":1.0,"SX_TOR":-1.0,"Q1":0.4},"detalhe":{"G1":{"gamma":1.2,"psi":1.0,"produto":1.2},"SX":{"gamma":1.0,"psi":1.0,"produto":1.0},"SX_TOR":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"Q1":{"gamma":1.0,"psi":0.4,"produto":0.4}}},{"nome":"ELU EXC SX - SX_TOR sem Q1","estado":"ELU_EXC","fatores":{"G1":1.2,"SX":1.0,"SX_TOR":-1.0},"detalhe":{"G1":{"gamma":1.2,"psi":1.0,"produto":1.2},"SX":{"gamma":1.0,"psi":1.0,"produto":1.0},"SX_TOR":{"gamma":-1.0,"psi":1.0,"produto":-1.0}}},{"nome":"ELU EXC SX - SX_TOR G fav","estado":"ELU_EXC","fatores":{"G1":1.0,"SX":1.0,"SX_TOR":-1.0,"Q1":0.4},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"SX":{"gamma":1.0,"psi":1.0,"produto":1.0},"SX_TOR":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"Q1":{"gamma":1.0,"psi":0.4,"produto":0.4}}},{"nome":"ELU EXC SX - SX_TOR sem Q1 G fav","estado":"ELU_EXC","fatores":{"G1":1.0,"SX":1.0,"SX_TOR":-1.0},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"SX":{"gamma":1.0,"psi":1.0,"produto":1.0},"SX_TOR":{"gamma":-1.0,"psi":1.0,"produto":-1.0}}},{"nome":"ELU EXC -SX + SX_TOR","estado":"ELU_EXC","fatores":{"G1":1.2,"SX":-1.0,"SX_TOR":1.0,"Q1":0.4},"detalhe":{"G1":{"gamma":1.2,"psi":1.0,"produto":1.2},"SX":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"SX_TOR":{"gamma":1.0,"psi":1.0,"produto":1.0},"Q1":{"gamma":1.0,"psi":0.4,"produto":0.4}}},{"nome":"ELU EXC -SX + SX_TOR sem Q1","estado":"ELU_EXC","fatores":{"G1":1.2,"SX":-1.0,"SX_TOR":1.0},"detalhe":{"G1":{"gamma":1.2,"psi":1.0,"produto":1.2},"SX":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"SX_TOR":{"gamma":1.0,"psi":1.0,"produto":1.0}}},{"nome":"ELU EXC -SX + SX_TOR G fav","estado":"ELU_EXC","fatores":{"G1":1.0,"SX":-1.0,"SX_TOR":1.0,"Q1":0.4},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"SX":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"SX_TOR":{"gamma":1.0,"psi":1.0,"produto":1.0},"Q1":{"gamma":1.0,"psi":0.4,"produto":0.4}}},{"nome":"ELU EXC -SX + SX_TOR sem Q1 G fav","estado":"ELU_EXC","fatores":{"G1":1.0,"SX":-1.0,"SX_TOR":1.0},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"SX":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"SX_TOR":{"gamma":1.0,"psi":1.0,"produto":1.0}}},{"nome":"ELU EXC -SX - SX_TOR","estado":"ELU_EXC","fatores":{"G1":1.2,"SX":-1.0,"SX_TOR":-1.0,"Q1":0.4},"detalhe":{"G1":{"gamma":1.2,"psi":1.0,"produto":1.2},"SX":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"SX_TOR":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"Q1":{"gamma":1.0,"psi":0.4,"produto":0.4}}},{"nome":"ELU EXC -SX - SX_TOR sem Q1","estado":"ELU_EXC","fatores":{"G1":1.2,"SX":-1.0,"SX_TOR":-1.0},"detalhe":{"G1":{"gamma":1.2,"psi":1.0,"produto":1.2},"SX":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"SX_TOR":{"gamma":-1.0,"psi":1.0,"produto":-1.0}}},{"nome":"ELU EXC -SX - SX_TOR G fav","estado":"ELU_EXC","fatores":{"G1":1.0,"SX":-1.0,"SX_TOR":-1.0,"Q1":0.4},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"SX":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"SX_TOR":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"Q1":{"gamma":1.0,"psi":0.4,"produto":0.4}}},{"nome":"ELU EXC -SX - SX_TOR sem Q1 G fav","estado":"ELU_EXC","fatores":{"G1":1.0,"SX":-1.0,"SX_TOR":-1.0},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"SX":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"SX_TOR":{"gamma":-1.0,"psi":1.0,"produto":-1.0}}},{"nome":"ELU EXC SY + SY_TOR","estado":"ELU_EXC","fatores":{"G1":1.2,"SY":1.0,"SY_TOR":1.0,"Q1":0.4},"detalhe":{"G1":{"gamma":1.2,"psi":1.0,"produto":1.2},"SY":{"gamma":1.0,"psi":1.0,"produto":1.0},"SY_TOR":{"gamma":1.0,"psi":1.0,"produto":1.0},"Q1":{"gamma":1.0,"psi":0.4,"produto":0.4}}},{"nome":"ELU EXC SY + SY_TOR sem Q1","estado":"ELU_EXC","fatores":{"G1":1.2,"SY":1.0,"SY_TOR":1.0},"detalhe":{"G1":{"gamma":1.2,"psi":1.0,"produto":1.2},"SY":{"gamma":1.0,"psi":1.0,"produto":1.0},"SY_TOR":{"gamma":1.0,"psi":1.0,"produto":1.0}}},{"nome":"ELU EXC SY + SY_TOR G fav","estado":"ELU_EXC","fatores":{"G1":1.0,"SY":1.0,"SY_TOR":1.0,"Q1":0.4},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"SY":{"gamma":1.0,"psi":1.0,"produto":1.0},"SY_TOR":{"gamma":1.0,"psi":1.0,"produto":1.0},"Q1":{"gamma":1.0,"psi":0.4,"produto":0.4}}},{"nome":"ELU EXC SY + SY_TOR sem Q1 G fav","estado":"ELU_EXC","fatores":{"G1":1.0,"SY":1.0,"SY_TOR":1.0},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"SY":{"gamma":1.0,"psi":1.0,"produto":1.0},"SY_TOR":{"gamma":1.0,"psi":1.0,"produto":1.0}}},{"nome":"ELU EXC SY - SY_TOR","estado":"ELU_EXC","fatores":{"G1":1.2,"SY":1.0,"SY_TOR":-1.0,"Q1":0.4},"detalhe":{"G1":{"gamma":1.2,"psi":1.0,"produto":1.2},"SY":{"gamma":1.0,"psi":1.0,"produto":1.0},"SY_TOR":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"Q1":{"gamma":1.0,"psi":0.4,"produto":0.4}}},{"nome":"ELU EXC SY - SY_TOR sem Q1","estado":"ELU_EXC","fatores":{"G1":1.2,"SY":1.0,"SY_TOR":-1.0},"detalhe":{"G1":{"gamma":1.2,"psi":1.0,"produto":1.2},"SY":{"gamma":1.0,"psi":1.0,"produto":1.0},"SY_TOR":{"gamma":-1.0,"psi":1.0,"produto":-1.0}}},{"nome":"ELU EXC SY - SY_TOR G fav","estado":"ELU_EXC","fatores":{"G1":1.0,"SY":1.0,"SY_TOR":-1.0,"Q1":0.4},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"SY":{"gamma":1.0,"psi":1.0,"produto":1.0},"SY_TOR":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"Q1":{"gamma":1.0,"psi":0.4,"produto":0.4}}},{"nome":"ELU EXC SY - SY_TOR sem Q1 G fav","estado":"ELU_EXC","fatores":{"G1":1.0,"SY":1.0,"SY_TOR":-1.0},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"SY":{"gamma":1.0,"psi":1.0,"produto":1.0},"SY_TOR":{"gamma":-1.0,"psi":1.0,"produto":-1.0}}},{"nome":"ELU EXC -SY + SY_TOR","estado":"ELU_EXC","fatores":{"G1":1.2,"SY":-1.0,"SY_TOR":1.0,"Q1":0.4},"detalhe":{"G1":{"gamma":1.2,"psi":1.0,"produto":1.2},"SY":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"SY_TOR":{"gamma":1.0,"psi":1.0,"produto":1.0},"Q1":{"gamma":1.0,"psi":0.4,"produto":0.4}}},{"nome":"ELU EXC -SY + SY_TOR sem Q1","estado":"ELU_EXC","fatores":{"G1":1.2,"SY":-1.0,"SY_TOR":1.0},"detalhe":{"G1":{"gamma":1.2,"psi":1.0,"produto":1.2},"SY":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"SY_TOR":{"gamma":1.0,"psi":1.0,"produto":1.0}}},{"nome":"ELU EXC -SY + SY_TOR G fav","estado":"ELU_EXC","fatores":{"G1":1.0,"SY":-1.0,"SY_TOR":1.0,"Q1":0.4},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"SY":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"SY_TOR":{"gamma":1.0,"psi":1.0,"produto":1.0},"Q1":{"gamma":1.0,"psi":0.4,"produto":0.4}}},{"nome":"ELU EXC -SY + SY_TOR sem Q1 G fav","estado":"ELU_EXC","fatores":{"G1":1.0,"SY":-1.0,"SY_TOR":1.0},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"SY":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"SY_TOR":{"gamma":1.0,"psi":1.0,"produto":1.0}}},{"nome":"ELU EXC -SY - SY_TOR","estado":"ELU_EXC","fatores":{"G1":1.2,"SY":-1.0,"SY_TOR":-1.0,"Q1":0.4},"detalhe":{"G1":{"gamma":1.2,"psi":1.0,"produto":1.2},"SY":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"SY_TOR":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"Q1":{"gamma":1.0,"psi":0.4,"produto":0.4}}},{"nome":"ELU EXC -SY - SY_TOR sem Q1","estado":"ELU_EXC","fatores":{"G1":1.2,"SY":-1.0,"SY_TOR":-1.0},"detalhe":{"G1":{"gamma":1.2,"psi":1.0,"produto":1.2},"SY":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"SY_TOR":{"gamma":-1.0,"psi":1.0,"produto":-1.0}}},{"nome":"ELU EXC -SY - SY_TOR G fav","estado":"ELU_EXC","fatores":{"G1":1.0,"SY":-1.0,"SY_TOR":-1.0,"Q1":0.4},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"SY":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"SY_TOR":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"Q1":{"gamma":1.0,"psi":0.4,"produto":0.4}}},{"nome":"ELU EXC -SY - SY_TOR sem Q1 G fav","estado":"ELU_EXC","fatores":{"G1":1.0,"SY":-1.0,"SY_TOR":-1.0},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"SY":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"SY_TOR":{"gamma":-1.0,"psi":1.0,"produto":-1.0}}},{"nome":"ELU EXC SX + 0.3*SY + SX_TOR","estado":"ELU_EXC","fatores":{"G1":1.2,"SX":1.0,"SY":0.3,"SX_TOR":1.0,"Q1":0.4},"detalhe":{"G1":{"gamma":1.2,"psi":1.0,"produto":1.2},"SX":{"gamma":1.0,"psi":1.0,"produto":1.0},"SY":{"gamma":0.3,"psi":1.0,"produto":0.3},"SX_TOR":{"gamma":1.0,"psi":1.0,"produto":1.0},"Q1":{"gamma":1.0,"psi":0.4,"produto":0.4}}},{"nome":"ELU EXC SX + 0.3*SY + SX_TOR sem Q1","estado":"ELU_EXC","fatores":{"G1":1.2,"SX":1.0,"SY":0.3,"SX_TOR":1.0},"detalhe":{"G1":{"gamma":1.2,"psi":1.0,"produto":1.2},"SX":{"gamma":1.0,"psi":1.0,"produto":1.0},"SY":{"gamma":0.3,"psi":1.0,"produto":0.3},"SX_TOR":{"gamma":1.0,"psi":1.0,"produto":1.0}}},{"nome":"ELU EXC SX + 0.3*SY + SX_TOR G fav","estado":"ELU_EXC","fatores":{"G1":1.0,"SX":1.0,"SY":0.3,"SX_TOR":1.0,"Q1":0.4},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"SX":{"gamma":1.0,"psi":1.0,"produto":1.0},"SY":{"gamma":0.3,"psi":1.0,"produto":0.3},"SX_TOR":{"gamma":1.0,"psi":1.0,"produto":1.0},"Q1":{"gamma":1.0,"psi":0.4,"produto":0.4}}},{"nome":"ELU EXC SX + 0.3*SY + SX_TOR sem Q1 G fav","estado":"ELU_EXC","fatores":{"G1":1.0,"SX":1.0,"SY":0.3,"SX_TOR":1.0},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"SX":{"gamma":1.0,"psi":1.0,"produto":1.0},"SY":{"gamma":0.3,"psi":1.0,"produto":0.3},"SX_TOR":{"gamma":1.0,"psi":1.0,"produto":1.0}}},{"nome":"ELU EXC SX + 0.3*SY - SX_TOR","estado":"ELU_EXC","fatores":{"G1":1.2,"SX":1.0,"SY":0.3,"SX_TOR":-1.0,"Q1":0.4},"detalhe":{"G1":{"gamma":1.2,"psi":1.0,"produto":1.2},"SX":{"gamma":1.0,"psi":1.0,"produto":1.0},"SY":{"gamma":0.3,"psi":1.0,"produto":0.3},"SX_TOR":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"Q1":{"gamma":1.0,"psi":0.4,"produto":0.4}}},{"nome":"ELU EXC SX + 0.3*SY - SX_TOR sem Q1","estado":"ELU_EXC","fatores":{"G1":1.2,"SX":1.0,"SY":0.3,"SX_TOR":-1.0},"detalhe":{"G1":{"gamma":1.2,"psi":1.0,"produto":1.2},"SX":{"gamma":1.0,"psi":1.0,"produto":1.0},"SY":{"gamma":0.3,"psi":1.0,"produto":0.3},"SX_TOR":{"gamma":-1.0,"psi":1.0,"produto":-1.0}}},{"nome":"ELU EXC SX + 0.3*SY - SX_TOR G fav","estado":"ELU_EXC","fatores":{"G1":1.0,"SX":1.0,"SY":0.3,"SX_TOR":-1.0,"Q1":0.4},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"SX":{"gamma":1.0,"psi":1.0,"produto":1.0},"SY":{"gamma":0.3,"psi":1.0,"produto":0.3},"SX_TOR":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"Q1":{"gamma":1.0,"psi":0.4,"produto":0.4}}},{"nome":"ELU EXC SX + 0.3*SY - SX_TOR sem Q1 G fav","estado":"ELU_EXC","fatores":{"G1":1.0,"SX":1.0,"SY":0.3,"SX_TOR":-1.0},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"SX":{"gamma":1.0,"psi":1.0,"produto":1.0},"SY":{"gamma":0.3,"psi":1.0,"produto":0.3},"SX_TOR":{"gamma":-1.0,"psi":1.0,"produto":-1.0}}},{"nome":"ELU EXC SX - 0.3*SY + SX_TOR","estado":"ELU_EXC","fatores":{"G1":1.2,"SX":1.0,"SY":-0.3,"SX_TOR":1.0,"Q1":0.4},"detalhe":{"G1":{"gamma":1.2,"psi":1.0,"produto":1.2},"SX":{"gamma":1.0,"psi":1.0,"produto":1.0},"SY":{"gamma":-0.3,"psi":1.0,"produto":-0.3},"SX_TOR":{"gamma":1.0,"psi":1.0,"produto":1.0},"Q1":{"gamma":1.0,"psi":0.4,"produto":0.4}}},{"nome":"ELU EXC SX - 0.3*SY + SX_TOR sem Q1","estado":"ELU_EXC","fatores":{"G1":1.2,"SX":1.0,"SY":-0.3,"SX_TOR":1.0},"detalhe":{"G1":{"gamma":1.2,"psi":1.0,"produto":1.2},"SX":{"gamma":1.0,"psi":1.0,"produto":1.0},"SY":{"gamma":-0.3,"psi":1.0,"produto":-0.3},"SX_TOR":{"gamma":1.0,"psi":1.0,"produto":1.0}}},{"nome":"ELU EXC SX - 0.3*SY + SX_TOR G fav","estado":"ELU_EXC","fatores":{"G1":1.0,"SX":1.0,"SY":-0.3,"SX_TOR":1.0,"Q1":0.4},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"SX":{"gamma":1.0,"psi":1.0,"produto":1.0},"SY":{"gamma":-0.3,"psi":1.0,"produto":-0.3},"SX_TOR":{"gamma":1.0,"psi":1.0,"produto":1.0},"Q1":{"gamma":1.0,"psi":0.4,"produto":0.4}}},{"nome":"ELU EXC SX - 0.3*SY + SX_TOR sem Q1 G fav","estado":"ELU_EXC","fatores":{"G1":1.0,"SX":1.0,"SY":-0.3,"SX_TOR":1.0},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"SX":{"gamma":1.0,"psi":1.0,"produto":1.0},"SY":{"gamma":-0.3,"psi":1.0,"produto":-0.3},"SX_TOR":{"gamma":1.0,"psi":1.0,"produto":1.0}}},{"nome":"ELU EXC SX - 0.3*SY - SX_TOR","estado":"ELU_EXC","fatores":{"G1":1.2,"SX":1.0,"SY":-0.3,"SX_TOR":-1.0,"Q1":0.4},"detalhe":{"G1":{"gamma":1.2,"psi":1.0,"produto":1.2},"SX":{"gamma":1.0,"psi":1.0,"produto":1.0},"SY":{"gamma":-0.3,"psi":1.0,"produto":-0.3},"SX_TOR":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"Q1":{"gamma":1.0,"psi":0.4,"produto":0.4}}},{"nome":"ELU EXC SX - 0.3*SY - SX_TOR sem Q1","estado":"ELU_EXC","fatores":{"G1":1.2,"SX":1.0,"SY":-0.3,"SX_TOR":-1.0},"detalhe":{"G1":{"gamma":1.2,"psi":1.0,"produto":1.2},"SX":{"gamma":1.0,"psi":1.0,"produto":1.0},"SY":{"gamma":-0.3,"psi":1.0,"produto":-0.3},"SX_TOR":{"gamma":-1.0,"psi":1.0,"produto":-1.0}}},{"nome":"ELU EXC SX - 0.3*SY - SX_TOR G fav","estado":"ELU_EXC","fatores":{"G1":1.0,"SX":1.0,"SY":-0.3,"SX_TOR":-1.0,"Q1":0.4},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"SX":{"gamma":1.0,"psi":1.0,"produto":1.0},"SY":{"gamma":-0.3,"psi":1.0,"produto":-0.3},"SX_TOR":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"Q1":{"gamma":1.0,"psi":0.4,"produto":0.4}}},{"nome":"ELU EXC SX - 0.3*SY - SX_TOR sem Q1 G fav","estado":"ELU_EXC","fatores":{"G1":1.0,"SX":1.0,"SY":-0.3,"SX_TOR":-1.0},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"SX":{"gamma":1.0,"psi":1.0,"produto":1.0},"SY":{"gamma":-0.3,"psi":1.0,"produto":-0.3},"SX_TOR":{"gamma":-1.0,"psi":1.0,"produto":-1.0}}},{"nome":"ELU EXC -SX + 0.3*SY + SX_TOR","estado":"ELU_EXC","fatores":{"G1":1.2,"SX":-1.0,"SY":0.3,"SX_TOR":1.0,"Q1":0.4},"detalhe":{"G1":{"gamma":1.2,"psi":1.0,"produto":1.2},"SX":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"SY":{"gamma":0.3,"psi":1.0,"produto":0.3},"SX_TOR":{"gamma":1.0,"psi":1.0,"produto":1.0},"Q1":{"gamma":1.0,"psi":0.4,"produto":0.4}}},{"nome":"ELU EXC -SX + 0.3*SY + SX_TOR sem Q1","estado":"ELU_EXC","fatores":{"G1":1.2,"SX":-1.0,"SY":0.3,"SX_TOR":1.0},"detalhe":{"G1":{"gamma":1.2,"psi":1.0,"produto":1.2},"SX":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"SY":{"gamma":0.3,"psi":1.0,"produto":0.3},"SX_TOR":{"gamma":1.0,"psi":1.0,"produto":1.0}}},{"nome":"ELU EXC -SX + 0.3*SY + SX_TOR G fav","estado":"ELU_EXC","fatores":{"G1":1.0,"SX":-1.0,"SY":0.3,"SX_TOR":1.0,"Q1":0.4},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"SX":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"SY":{"gamma":0.3,"psi":1.0,"produto":0.3},"SX_TOR":{"gamma":1.0,"psi":1.0,"produto":1.0},"Q1":{"gamma":1.0,"psi":0.4,"produto":0.4}}},{"nome":"ELU EXC -SX + 0.3*SY + SX_TOR sem Q1 G fav","estado":"ELU_EXC","fatores":{"G1":1.0,"SX":-1.0,"SY":0.3,"SX_TOR":1.0},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"SX":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"SY":{"gamma":0.3,"psi":1.0,"produto":0.3},"SX_TOR":{"gamma":1.0,"psi":1.0,"produto":1.0}}},{"nome":"ELU EXC -SX + 0.3*SY - SX_TOR","estado":"ELU_EXC","fatores":{"G1":1.2,"SX":-1.0,"SY":0.3,"SX_TOR":-1.0,"Q1":0.4},"detalhe":{"G1":{"gamma":1.2,"psi":1.0,"produto":1.2},"SX":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"SY":{"gamma":0.3,"psi":1.0,"produto":0.3},"SX_TOR":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"Q1":{"gamma":1.0,"psi":0.4,"produto":0.4}}},{"nome":"ELU EXC -SX + 0.3*SY - SX_TOR sem Q1","estado":"ELU_EXC","fatores":{"G1":1.2,"SX":-1.0,"SY":0.3,"SX_TOR":-1.0},"detalhe":{"G1":{"gamma":1.2,"psi":1.0,"produto":1.2},"SX":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"SY":{"gamma":0.3,"psi":1.0,"produto":0.3},"SX_TOR":{"gamma":-1.0,"psi":1.0,"produto":-1.0}}},{"nome":"ELU EXC -SX + 0.3*SY - SX_TOR G fav","estado":"ELU_EXC","fatores":{"G1":1.0,"SX":-1.0,"SY":0.3,"SX_TOR":-1.0,"Q1":0.4},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"SX":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"SY":{"gamma":0.3,"psi":1.0,"produto":0.3},"SX_TOR":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"Q1":{"gamma":1.0,"psi":0.4,"produto":0.4}}},{"nome":"ELU EXC -SX + 0.3*SY - SX_TOR sem Q1 G fav","estado":"ELU_EXC","fatores":{"G1":1.0,"SX":-1.0,"SY":0.3,"SX_TOR":-1.0},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"SX":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"SY":{"gamma":0.3,"psi":1.0,"produto":0.3},"SX_TOR":{"gamma":-1.0,"psi":1.0,"produto":-1.0}}},{"nome":"ELU EXC -SX - 0.3*SY + SX_TOR","estado":"ELU_EXC","fatores":{"G1":1.2,"SX":-1.0,"SY":-0.3,"SX_TOR":1.0,"Q1":0.4},"detalhe":{"G1":{"gamma":1.2,"psi":1.0,"produto":1.2},"SX":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"SY":{"gamma":-0.3,"psi":1.0,"produto":-0.3},"SX_TOR":{"gamma":1.0,"psi":1.0,"produto":1.0},"Q1":{"gamma":1.0,"psi":0.4,"produto":0.4}}},{"nome":"ELU EXC -SX - 0.3*SY + SX_TOR sem Q1","estado":"ELU_EXC","fatores":{"G1":1.2,"SX":-1.0,"SY":-0.3,"SX_TOR":1.0},"detalhe":{"G1":{"gamma":1.2,"psi":1.0,"produto":1.2},"SX":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"SY":{"gamma":-0.3,"psi":1.0,"produto":-0.3},"SX_TOR":{"gamma":1.0,"psi":1.0,"produto":1.0}}},{"nome":"ELU EXC -SX - 0.3*SY + SX_TOR G fav","estado":"ELU_EXC","fatores":{"G1":1.0,"SX":-1.0,"SY":-0.3,"SX_TOR":1.0,"Q1":0.4},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"SX":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"SY":{"gamma":-0.3,"psi":1.0,"produto":-0.3},"SX_TOR":{"gamma":1.0,"psi":1.0,"produto":1.0},"Q1":{"gamma":1.0,"psi":0.4,"produto":0.4}}},{"nome":"ELU EXC -SX - 0.3*SY + SX_TOR sem Q1 G fav","estado":"ELU_EXC","fatores":{"G1":1.0,"SX":-1.0,"SY":-0.3,"SX_TOR":1.0},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"SX":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"SY":{"gamma":-0.3,"psi":1.0,"produto":-0.3},"SX_TOR":{"gamma":1.0,"psi":1.0,"produto":1.0}}},{"nome":"ELU EXC -SX - 0.3*SY - SX_TOR","estado":"ELU_EXC","fatores":{"G1":1.2,"SX":-1.0,"SY":-0.3,"SX_TOR":-1.0,"Q1":0.4},"detalhe":{"G1":{"gamma":1.2,"psi":1.0,"produto":1.2},"SX":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"SY":{"gamma":-0.3,"psi":1.0,"produto":-0.3},"SX_TOR":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"Q1":{"gamma":1.0,"psi":0.4,"produto":0.4}}},{"nome":"ELU EXC -SX - 0.3*SY - SX_TOR sem Q1","estado":"ELU_EXC","fatores":{"G1":1.2,"SX":-1.0,"SY":-0.3,"SX_TOR":-1.0},"detalhe":{"G1":{"gamma":1.2,"psi":1.0,"produto":1.2},"SX":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"SY":{"gamma":-0.3,"psi":1.0,"produto":-0.3},"SX_TOR":{"gamma":-1.0,"psi":1.0,"produto":-1.0}}},{"nome":"ELU EXC -SX - 0.3*SY - SX_TOR G fav","estado":"ELU_EXC","fatores":{"G1":1.0,"SX":-1.0,"SY":-0.3,"SX_TOR":-1.0,"Q1":0.4},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"SX":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"SY":{"gamma":-0.3,"psi":1.0,"produto":-0.3},"SX_TOR":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"Q1":{"gamma":1.0,"psi":0.4,"produto":0.4}}},{"nome":"ELU EXC -SX - 0.3*SY - SX_TOR sem Q1 G fav","estado":"ELU_EXC","fatores":{"G1":1.0,"SX":-1.0,"SY":-0.3,"SX_TOR":-1.0},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"SX":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"SY":{"gamma":-0.3,"psi":1.0,"produto":-0.3},"SX_TOR":{"gamma":-1.0,"psi":1.0,"produto":-1.0}}},{"nome":"ELU EXC SY + 0.3*SX + SY_TOR","estado":"ELU_EXC","fatores":{"G1":1.2,"SY":1.0,"SX":0.3,"SY_TOR":1.0,"Q1":0.4},"detalhe":{"G1":{"gamma":1.2,"psi":1.0,"produto":1.2},"SY":{"gamma":1.0,"psi":1.0,"produto":1.0},"SX":{"gamma":0.3,"psi":1.0,"produto":0.3},"SY_TOR":{"gamma":1.0,"psi":1.0,"produto":1.0},"Q1":{"gamma":1.0,"psi":0.4,"produto":0.4}}},{"nome":"ELU EXC SY + 0.3*SX + SY_TOR sem Q1","estado":"ELU_EXC","fatores":{"G1":1.2,"SY":1.0,"SX":0.3,"SY_TOR":1.0},"detalhe":{"G1":{"gamma":1.2,"psi":1.0,"produto":1.2},"SY":{"gamma":1.0,"psi":1.0,"produto":1.0},"SX":{"gamma":0.3,"psi":1.0,"produto":0.3},"SY_TOR":{"gamma":1.0,"psi":1.0,"produto":1.0}}},{"nome":"ELU EXC SY + 0.3*SX + SY_TOR G fav","estado":"ELU_EXC","fatores":{"G1":1.0,"SY":1.0,"SX":0.3,"SY_TOR":1.0,"Q1":0.4},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"SY":{"gamma":1.0,"psi":1.0,"produto":1.0},"SX":{"gamma":0.3,"psi":1.0,"produto":0.3},"SY_TOR":{"gamma":1.0,"psi":1.0,"produto":1.0},"Q1":{"gamma":1.0,"psi":0.4,"produto":0.4}}},{"nome":"ELU EXC SY + 0.3*SX + SY_TOR sem Q1 G fav","estado":"ELU_EXC","fatores":{"G1":1.0,"SY":1.0,"SX":0.3,"SY_TOR":1.0},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"SY":{"gamma":1.0,"psi":1.0,"produto":1.0},"SX":{"gamma":0.3,"psi":1.0,"produto":0.3},"SY_TOR":{"gamma":1.0,"psi":1.0,"produto":1.0}}},{"nome":"ELU EXC SY + 0.3*SX - SY_TOR","estado":"ELU_EXC","fatores":{"G1":1.2,"SY":1.0,"SX":0.3,"SY_TOR":-1.0,"Q1":0.4},"detalhe":{"G1":{"gamma":1.2,"psi":1.0,"produto":1.2},"SY":{"gamma":1.0,"psi":1.0,"produto":1.0},"SX":{"gamma":0.3,"psi":1.0,"produto":0.3},"SY_TOR":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"Q1":{"gamma":1.0,"psi":0.4,"produto":0.4}}},{"nome":"ELU EXC SY + 0.3*SX - SY_TOR sem Q1","estado":"ELU_EXC","fatores":{"G1":1.2,"SY":1.0,"SX":0.3,"SY_TOR":-1.0},"detalhe":{"G1":{"gamma":1.2,"psi":1.0,"produto":1.2},"SY":{"gamma":1.0,"psi":1.0,"produto":1.0},"SX":{"gamma":0.3,"psi":1.0,"produto":0.3},"SY_TOR":{"gamma":-1.0,"psi":1.0,"produto":-1.0}}},{"nome":"ELU EXC SY + 0.3*SX - SY_TOR G fav","estado":"ELU_EXC","fatores":{"G1":1.0,"SY":1.0,"SX":0.3,"SY_TOR":-1.0,"Q1":0.4},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"SY":{"gamma":1.0,"psi":1.0,"produto":1.0},"SX":{"gamma":0.3,"psi":1.0,"produto":0.3},"SY_TOR":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"Q1":{"gamma":1.0,"psi":0.4,"produto":0.4}}},{"nome":"ELU EXC SY + 0.3*SX - SY_TOR sem Q1 G fav","estado":"ELU_EXC","fatores":{"G1":1.0,"SY":1.0,"SX":0.3,"SY_TOR":-1.0},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"SY":{"gamma":1.0,"psi":1.0,"produto":1.0},"SX":{"gamma":0.3,"psi":1.0,"produto":0.3},"SY_TOR":{"gamma":-1.0,"psi":1.0,"produto":-1.0}}},{"nome":"ELU EXC SY - 0.3*SX + SY_TOR","estado":"ELU_EXC","fatores":{"G1":1.2,"SY":1.0,"SX":-0.3,"SY_TOR":1.0,"Q1":0.4},"detalhe":{"G1":{"gamma":1.2,"psi":1.0,"produto":1.2},"SY":{"gamma":1.0,"psi":1.0,"produto":1.0},"SX":{"gamma":-0.3,"psi":1.0,"produto":-0.3},"SY_TOR":{"gamma":1.0,"psi":1.0,"produto":1.0},"Q1":{"gamma":1.0,"psi":0.4,"produto":0.4}}},{"nome":"ELU EXC SY - 0.3*SX + SY_TOR sem Q1","estado":"ELU_EXC","fatores":{"G1":1.2,"SY":1.0,"SX":-0.3,"SY_TOR":1.0},"detalhe":{"G1":{"gamma":1.2,"psi":1.0,"produto":1.2},"SY":{"gamma":1.0,"psi":1.0,"produto":1.0},"SX":{"gamma":-0.3,"psi":1.0,"produto":-0.3},"SY_TOR":{"gamma":1.0,"psi":1.0,"produto":1.0}}},{"nome":"ELU EXC SY - 0.3*SX + SY_TOR G fav","estado":"ELU_EXC","fatores":{"G1":1.0,"SY":1.0,"SX":-0.3,"SY_TOR":1.0,"Q1":0.4},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"SY":{"gamma":1.0,"psi":1.0,"produto":1.0},"SX":{"gamma":-0.3,"psi":1.0,"produto":-0.3},"SY_TOR":{"gamma":1.0,"psi":1.0,"produto":1.0},"Q1":{"gamma":1.0,"psi":0.4,"produto":0.4}}},{"nome":"ELU EXC SY - 0.3*SX + SY_TOR sem Q1 G fav","estado":"ELU_EXC","fatores":{"G1":1.0,"SY":1.0,"SX":-0.3,"SY_TOR":1.0},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"SY":{"gamma":1.0,"psi":1.0,"produto":1.0},"SX":{"gamma":-0.3,"psi":1.0,"produto":-0.3},"SY_TOR":{"gamma":1.0,"psi":1.0,"produto":1.0}}},{"nome":"ELU EXC SY - 0.3*SX - SY_TOR","estado":"ELU_EXC","fatores":{"G1":1.2,"SY":1.0,"SX":-0.3,"SY_TOR":-1.0,"Q1":0.4},"detalhe":{"G1":{"gamma":1.2,"psi":1.0,"produto":1.2},"SY":{"gamma":1.0,"psi":1.0,"produto":1.0},"SX":{"gamma":-0.3,"psi":1.0,"produto":-0.3},"SY_TOR":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"Q1":{"gamma":1.0,"psi":0.4,"produto":0.4}}},{"nome":"ELU EXC SY - 0.3*SX - SY_TOR sem Q1","estado":"ELU_EXC","fatores":{"G1":1.2,"SY":1.0,"SX":-0.3,"SY_TOR":-1.0},"detalhe":{"G1":{"gamma":1.2,"psi":1.0,"produto":1.2},"SY":{"gamma":1.0,"psi":1.0,"produto":1.0},"SX":{"gamma":-0.3,"psi":1.0,"produto":-0.3},"SY_TOR":{"gamma":-1.0,"psi":1.0,"produto":-1.0}}},{"nome":"ELU EXC SY - 0.3*SX - SY_TOR G fav","estado":"ELU_EXC","fatores":{"G1":1.0,"SY":1.0,"SX":-0.3,"SY_TOR":-1.0,"Q1":0.4},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"SY":{"gamma":1.0,"psi":1.0,"produto":1.0},"SX":{"gamma":-0.3,"psi":1.0,"produto":-0.3},"SY_TOR":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"Q1":{"gamma":1.0,"psi":0.4,"produto":0.4}}},{"nome":"ELU EXC SY - 0.3*SX - SY_TOR sem Q1 G fav","estado":"ELU_EXC","fatores":{"G1":1.0,"SY":1.0,"SX":-0.3,"SY_TOR":-1.0},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"SY":{"gamma":1.0,"psi":1.0,"produto":1.0},"SX":{"gamma":-0.3,"psi":1.0,"produto":-0.3},"SY_TOR":{"gamma":-1.0,"psi":1.0,"produto":-1.0}}},{"nome":"ELU EXC -SY + 0.3*SX + SY_TOR","estado":"ELU_EXC","fatores":{"G1":1.2,"SY":-1.0,"SX":0.3,"SY_TOR":1.0,"Q1":0.4},"detalhe":{"G1":{"gamma":1.2,"psi":1.0,"produto":1.2},"SY":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"SX":{"gamma":0.3,"psi":1.0,"produto":0.3},"SY_TOR":{"gamma":1.0,"psi":1.0,"produto":1.0},"Q1":{"gamma":1.0,"psi":0.4,"produto":0.4}}},{"nome":"ELU EXC -SY + 0.3*SX + SY_TOR sem Q1","estado":"ELU_EXC","fatores":{"G1":1.2,"SY":-1.0,"SX":0.3,"SY_TOR":1.0},"detalhe":{"G1":{"gamma":1.2,"psi":1.0,"produto":1.2},"SY":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"SX":{"gamma":0.3,"psi":1.0,"produto":0.3},"SY_TOR":{"gamma":1.0,"psi":1.0,"produto":1.0}}},{"nome":"ELU EXC -SY + 0.3*SX + SY_TOR G fav","estado":"ELU_EXC","fatores":{"G1":1.0,"SY":-1.0,"SX":0.3,"SY_TOR":1.0,"Q1":0.4},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"SY":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"SX":{"gamma":0.3,"psi":1.0,"produto":0.3},"SY_TOR":{"gamma":1.0,"psi":1.0,"produto":1.0},"Q1":{"gamma":1.0,"psi":0.4,"produto":0.4}}},{"nome":"ELU EXC -SY + 0.3*SX + SY_TOR sem Q1 G fav","estado":"ELU_EXC","fatores":{"G1":1.0,"SY":-1.0,"SX":0.3,"SY_TOR":1.0},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"SY":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"SX":{"gamma":0.3,"psi":1.0,"produto":0.3},"SY_TOR":{"gamma":1.0,"psi":1.0,"produto":1.0}}},{"nome":"ELU EXC -SY + 0.3*SX - SY_TOR","estado":"ELU_EXC","fatores":{"G1":1.2,"SY":-1.0,"SX":0.3,"SY_TOR":-1.0,"Q1":0.4},"detalhe":{"G1":{"gamma":1.2,"psi":1.0,"produto":1.2},"SY":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"SX":{"gamma":0.3,"psi":1.0,"produto":0.3},"SY_TOR":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"Q1":{"gamma":1.0,"psi":0.4,"produto":0.4}}},{"nome":"ELU EXC -SY + 0.3*SX - SY_TOR sem Q1","estado":"ELU_EXC","fatores":{"G1":1.2,"SY":-1.0,"SX":0.3,"SY_TOR":-1.0},"detalhe":{"G1":{"gamma":1.2,"psi":1.0,"produto":1.2},"SY":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"SX":{"gamma":0.3,"psi":1.0,"produto":0.3},"SY_TOR":{"gamma":-1.0,"psi":1.0,"produto":-1.0}}},{"nome":"ELU EXC -SY + 0.3*SX - SY_TOR G fav","estado":"ELU_EXC","fatores":{"G1":1.0,"SY":-1.0,"SX":0.3,"SY_TOR":-1.0,"Q1":0.4},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"SY":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"SX":{"gamma":0.3,"psi":1.0,"produto":0.3},"SY_TOR":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"Q1":{"gamma":1.0,"psi":0.4,"produto":0.4}}},{"nome":"ELU EXC -SY + 0.3*SX - SY_TOR sem Q1 G fav","estado":"ELU_EXC","fatores":{"G1":1.0,"SY":-1.0,"SX":0.3,"SY_TOR":-1.0},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"SY":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"SX":{"gamma":0.3,"psi":1.0,"produto":0.3},"SY_TOR":{"gamma":-1.0,"psi":1.0,"produto":-1.0}}},{"nome":"ELU EXC -SY - 0.3*SX + SY_TOR","estado":"ELU_EXC","fatores":{"G1":1.2,"SY":-1.0,"SX":-0.3,"SY_TOR":1.0,"Q1":0.4},"detalhe":{"G1":{"gamma":1.2,"psi":1.0,"produto":1.2},"SY":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"SX":{"gamma":-0.3,"psi":1.0,"produto":-0.3},"SY_TOR":{"gamma":1.0,"psi":1.0,"produto":1.0},"Q1":{"gamma":1.0,"psi":0.4,"produto":0.4}}},{"nome":"ELU EXC -SY - 0.3*SX + SY_TOR sem Q1","estado":"ELU_EXC","fatores":{"G1":1.2,"SY":-1.0,"SX":-0.3,"SY_TOR":1.0},"detalhe":{"G1":{"gamma":1.2,"psi":1.0,"produto":1.2},"SY":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"SX":{"gamma":-0.3,"psi":1.0,"produto":-0.3},"SY_TOR":{"gamma":1.0,"psi":1.0,"produto":1.0}}},{"nome":"ELU EXC -SY - 0.3*SX + SY_TOR G fav","estado":"ELU_EXC","fatores":{"G1":1.0,"SY":-1.0,"SX":-0.3,"SY_TOR":1.0,"Q1":0.4},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"SY":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"SX":{"gamma":-0.3,"psi":1.0,"produto":-0.3},"SY_TOR":{"gamma":1.0,"psi":1.0,"produto":1.0},"Q1":{"gamma":1.0,"psi":0.4,"produto":0.4}}},{"nome":"ELU EXC -SY - 0.3*SX + SY_TOR sem Q1 G fav","estado":"ELU_EXC","fatores":{"G1":1.0,"SY":-1.0,"SX":-0.3,"SY_TOR":1.0},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"SY":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"SX":{"gamma":-0.3,"psi":1.0,"produto":-0.3},"SY_TOR":{"gamma":1.0,"psi":1.0,"produto":1.0}}},{"nome":"ELU EXC -SY - 0.3*SX - SY_TOR","estado":"ELU_EXC","fatores":{"G1":1.2,"SY":-1.0,"SX":-0.3,"SY_TOR":-1.0,"Q1":0.4},"detalhe":{"G1":{"gamma":1.2,"psi":1.0,"produto":1.2},"SY":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"SX":{"gamma":-0.3,"psi":1.0,"produto":-0.3},"SY_TOR":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"Q1":{"gamma":1.0,"psi":0.4,"produto":0.4}}},{"nome":"ELU EXC -SY - 0.3*SX - SY_TOR sem Q1","estado":"ELU_EXC","fatores":{"G1":1.2,"SY":-1.0,"SX":-0.3,"SY_TOR":-1.0},"detalhe":{"G1":{"gamma":1.2,"psi":1.0,"produto":1.2},"SY":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"SX":{"gamma":-0.3,"psi":1.0,"produto":-0.3},"SY_TOR":{"gamma":-1.0,"psi":1.0,"produto":-1.0}}},{"nome":"ELU EXC -SY - 0.3*SX - SY_TOR G fav","estado":"ELU_EXC","fatores":{"G1":1.0,"SY":-1.0,"SX":-0.3,"SY_TOR":-1.0,"Q1":0.4},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"SY":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"SX":{"gamma":-0.3,"psi":1.0,"produto":-0.3},"SY_TOR":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"Q1":{"gamma":1.0,"psi":0.4,"produto":0.4}}},{"nome":"ELU EXC -SY - 0.3*SX - SY_TOR sem Q1 G fav","estado":"ELU_EXC","fatores":{"G1":1.0,"SY":-1.0,"SX":-0.3,"SY_TOR":-1.0},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"SY":{"gamma":-1.0,"psi":1.0,"produto":-1.0},"SX":{"gamma":-0.3,"psi":1.0,"produto":-0.3},"SY_TOR":{"gamma":-1.0,"psi":1.0,"produto":-1.0}}},{"nome":"ELS QP","estado":"ELS_QP","fatores":{"G1":1.0,"Q1":0.4},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"Q1":{"gamma":1.0,"psi":0.4,"produto":0.4}}},{"nome":"ELS FREQ V+X princ.","estado":"ELS_FREQ","fatores":{"G1":1.0,"V+X":0.3},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"V+X":{"gamma":1.0,"psi":0.3,"produto":0.3}}},{"nome":"ELS RARA V+X princ.","estado":"ELS_RARA","fatores":{"G1":1.0,"V+X":1.0},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"V+X":{"gamma":1.0,"psi":1.0,"produto":1.0}}},{"nome":"ELS FREQ V-X princ.","estado":"ELS_FREQ","fatores":{"G1":1.0,"V-X":0.3},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"V-X":{"gamma":1.0,"psi":0.3,"produto":0.3}}},{"nome":"ELS RARA V-X princ.","estado":"ELS_RARA","fatores":{"G1":1.0,"V-X":1.0},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"V-X":{"gamma":1.0,"psi":1.0,"produto":1.0}}},{"nome":"ELS FREQ V+Y princ.","estado":"ELS_FREQ","fatores":{"G1":1.0,"V+Y":0.3},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"V+Y":{"gamma":1.0,"psi":0.3,"produto":0.3}}},{"nome":"ELS RARA V+Y princ.","estado":"ELS_RARA","fatores":{"G1":1.0,"V+Y":1.0},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"V+Y":{"gamma":1.0,"psi":1.0,"produto":1.0}}},{"nome":"ELS FREQ V-Y princ.","estado":"ELS_FREQ","fatores":{"G1":1.0,"V-Y":0.3},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"V-Y":{"gamma":1.0,"psi":0.3,"produto":0.3}}},{"nome":"ELS RARA V-Y princ.","estado":"ELS_RARA","fatores":{"G1":1.0,"V-Y":1.0},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"V-Y":{"gamma":1.0,"psi":1.0,"produto":1.0}}},{"nome":"ELS FREQ Q1 princ.","estado":"ELS_FREQ","fatores":{"G1":1.0,"Q1":0.6},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"Q1":{"gamma":1.0,"psi":0.6,"produto":0.6}}},{"nome":"ELS RARA Q1 princ.","estado":"ELS_RARA","fatores":{"G1":1.0,"Q1":1.0},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"Q1":{"gamma":1.0,"psi":1.0,"produto":1.0}}},{"nome":"ELS RARA Q1 princ. + V+X","estado":"ELS_RARA","fatores":{"G1":1.0,"Q1":1.0,"V+X":0.3},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"Q1":{"gamma":1.0,"psi":1.0,"produto":1.0},"V+X":{"gamma":1.0,"psi":0.3,"produto":0.3}}},{"nome":"ELS FREQ V+X princ. + Q1","estado":"ELS_FREQ","fatores":{"G1":1.0,"Q1":0.4,"V+X":0.3},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"Q1":{"gamma":1.0,"psi":0.4,"produto":0.4},"V+X":{"gamma":1.0,"psi":0.3,"produto":0.3}}},{"nome":"ELS RARA V+X princ. + Q1","estado":"ELS_RARA","fatores":{"G1":1.0,"Q1":0.6,"V+X":1.0},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"Q1":{"gamma":1.0,"psi":0.6,"produto":0.6},"V+X":{"gamma":1.0,"psi":1.0,"produto":1.0}}},{"nome":"ELS RARA Q1 princ. + V-X","estado":"ELS_RARA","fatores":{"G1":1.0,"Q1":1.0,"V-X":0.3},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"Q1":{"gamma":1.0,"psi":1.0,"produto":1.0},"V-X":{"gamma":1.0,"psi":0.3,"produto":0.3}}},{"nome":"ELS FREQ V-X princ. + Q1","estado":"ELS_FREQ","fatores":{"G1":1.0,"Q1":0.4,"V-X":0.3},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"Q1":{"gamma":1.0,"psi":0.4,"produto":0.4},"V-X":{"gamma":1.0,"psi":0.3,"produto":0.3}}},{"nome":"ELS RARA V-X princ. + Q1","estado":"ELS_RARA","fatores":{"G1":1.0,"Q1":0.6,"V-X":1.0},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"Q1":{"gamma":1.0,"psi":0.6,"produto":0.6},"V-X":{"gamma":1.0,"psi":1.0,"produto":1.0}}},{"nome":"ELS RARA Q1 princ. + V+Y","estado":"ELS_RARA","fatores":{"G1":1.0,"Q1":1.0,"V+Y":0.3},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"Q1":{"gamma":1.0,"psi":1.0,"produto":1.0},"V+Y":{"gamma":1.0,"psi":0.3,"produto":0.3}}},{"nome":"ELS FREQ V+Y princ. + Q1","estado":"ELS_FREQ","fatores":{"G1":1.0,"Q1":0.4,"V+Y":0.3},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"Q1":{"gamma":1.0,"psi":0.4,"produto":0.4},"V+Y":{"gamma":1.0,"psi":0.3,"produto":0.3}}},{"nome":"ELS RARA V+Y princ. + Q1","estado":"ELS_RARA","fatores":{"G1":1.0,"Q1":0.6,"V+Y":1.0},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"Q1":{"gamma":1.0,"psi":0.6,"produto":0.6},"V+Y":{"gamma":1.0,"psi":1.0,"produto":1.0}}},{"nome":"ELS RARA Q1 princ. + V-Y","estado":"ELS_RARA","fatores":{"G1":1.0,"Q1":1.0,"V-Y":0.3},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"Q1":{"gamma":1.0,"psi":1.0,"produto":1.0},"V-Y":{"gamma":1.0,"psi":0.3,"produto":0.3}}},{"nome":"ELS FREQ V-Y princ. + Q1","estado":"ELS_FREQ","fatores":{"G1":1.0,"Q1":0.4,"V-Y":0.3},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"Q1":{"gamma":1.0,"psi":0.4,"produto":0.4},"V-Y":{"gamma":1.0,"psi":0.3,"produto":0.3}}},{"nome":"ELS RARA V-Y princ. + Q1","estado":"ELS_RARA","fatores":{"G1":1.0,"Q1":0.6,"V-Y":1.0},"detalhe":{"G1":{"gamma":1.0,"psi":1.0,"produto":1.0},"Q1":{"gamma":1.0,"psi":0.6,"produto":0.6},"V-Y":{"gamma":1.0,"psi":1.0,"produto":1.0}}}]}
            """;
        // 40–46) Janelas de análise sobre o motor, alimentadas pelos JSON reais embutidos (amostras/*.json).
        static void CapturarAnalise()
        {
            void Cap(Window j, string nome, Action depois = null)
            {
                PrepararDialogoForaDaTela(j);
                j.Show();
                EsperarIdle();
                RenderizarJanela(j, (int)j.Width, (int)j.Height, nome + ".png");
                RenderizarJanela(j, 1280, 720, nome + "-1280x720.png");
                if (depois != null) { depois(); EsperarIdle(); }
            }

            var jm = new JanelaModal(Amostras.Ler("modal"));
            Cap(jm, "40-modal", () => { jm.Selecionar(2); EsperarIdle(); RenderizarJanela(jm, (int)jm.Width, (int)jm.Height, "40-modal-modo3.png"); });
            jm.Close();

            var je = new JanelaEstabilidade(Amostras.Ler("estabilidade"));
            Cap(je, "41-estabilidade");
            je.Close();
            var est2 = Amostras.Ler("estabilidade");   // variação: γz em Y acima de 1,1 com majoração
            est2["gama_z"]["Y"]["valor"] = 1.18; est2["gama_z"]["Y"]["classificacao"] = "nos_moveis"; est2["gama_z"]["Y"]["majoracao"] = 1.121; est2["majoracao"]["n_majoradas"] = 1;
            est2["por_combinacao"] = new System.Text.Json.Nodes.JsonObject { ["ELU Q1 princ. + V+Y"] = new System.Text.Json.Nodes.JsonObject { ["direcao"] = "Y", ["gama_z"] = 1.18, ["B2"] = null, ["instavel"] = false } };
            var je2 = new JanelaEstabilidade(est2);
            Cap(je2, "41-estabilidade-majorada");
            je2.Close();

            var jl = new JanelaVerificacaoBarras(Amostras.Ler("verificacao"));
            Cap(jl, "42-verificacao-barras", () =>
            {
                jl.Filtrar(false, "pff"); EsperarIdle(); RenderizarJanela(jl, (int)jl.Width, (int)jl.Height, "42-verificacao-barras-pff.png");
                jl.Filtrar(true, ""); EsperarIdle(); RenderizarJanela(jl, (int)jl.Width, (int)jl.Height, "42-verificacao-barras-reprovadas.png");
                jl.Filtrar(false, ""); jl.Ordenar(0, false); EsperarIdle(); RenderizarJanela(jl, (int)jl.Width, (int)jl.Height, "42-verificacao-barras-por-nome.png");
            });
            jl.Close();
            var vrep = Amostras.Ler("verificacao");   // variação visual: duas barras acima de 100%
            vrep[0]["taxa"] = 1.08; vrep[0]["ok"] = false; vrep[3]["taxa"] = 0.95;
            var jl2 = new JanelaVerificacaoBarras(vrep);
            Cap(jl2, "42-verificacao-barras-com-reprovada");
            jl2.Close();

            foreach (var nome in new[] { "aco", "concreto" })
            {
                var jb = new JanelaVerificarBarraMotor(Amostras.Ler("verificacao-barra-" + nome));
                Cap(jb, "43-verificar-barra-" + nome);
                jb.Close();
            }

            var jg = new JanelaLigacao(Amostras.Ler("tipos-ligacao"), "parafuso", null);
            Cap(jg, "44-ligacao-vazia");
            jg.Preencher("parafuso", new System.Text.Json.Nodes.JsonObject { ["d_mm"] = 16, ["t"] = 0.0095, ["lf"] = 0.03 },
                new System.Text.Json.Nodes.JsonObject { ["Ft"] = 20, ["Fv"] = 35 }, Amostras.Ler("ligacao-parafuso"));
            EsperarIdle();
            RenderizarJanela(jg, (int)jg.Width, (int)jg.Height, "44-ligacao-parafuso.png");
            RenderizarJanela(jg, 1280, 720, "44-ligacao-parafuso-1280x720.png");
            jg.Preencher("chapa_simples", new System.Text.Json.Nodes.JsonObject(), new System.Text.Json.Nodes.JsonObject(), null);
            EsperarIdle();
            RenderizarJanela(jg, (int)jg.Width, (int)jg.Height, "44-ligacao-chapa-simples.png");
            jg.Close();

            var jc = new JanelaCype(null, null);
            Cap(jc, "45-cype-vazia", () =>
            {
                jc.Mostrar("PIPERACK TESTE 4.ed3", Amostras.Ler("comparar-cype"), null); EsperarIdle();
                RenderizarJanela(jc, (int)jc.Width, (int)jc.Height, "45-cype-comparar.png");
                RenderizarJanela(jc, 1280, 720, "45-cype-comparar-1280x720.png");
                jc.Mostrar("PIPERACK TESTE 4.ed3", null, Amostras.Ler("importar-cype")); EsperarIdle();
                RenderizarJanela(jc, (int)jc.Width, (int)jc.Height, "45-cype-importar.png");
            });
            jc.Close();

            var perfil = Amostras.Ler("perfil"); var perfilUe = Amostras.Ler("perfil-ue");
            var jk = new JanelaCatalogo(Amostras.Ler("catalogo"), n => System.Threading.Tasks.Task.FromResult(n == "W250x32.7" ? perfil : n == "Ue 127x50x17x2,65" ? perfilUe : null));
            Cap(jk, "46-catalogo", () =>
            {
                jk.Escolher("W250x32.7"); EsperarIdle();
                RenderizarJanela(jk, (int)jk.Width, (int)jk.Height, "46-catalogo-perfil.png");
                RenderizarJanela(jk, 1280, 720, "46-catalogo-perfil-1280x720.png");
                jk.Filtrar("Ue", ""); jk.Escolher("Ue 127x50x17x2,65"); EsperarIdle();
                RenderizarJanela(jk, (int)jk.Width, (int)jk.Height, "46-catalogo-ue.png");
                jk.Filtrar("", "zzz"); EsperarIdle();
                RenderizarJanela(jk, (int)jk.Width, (int)jk.Height, "46-catalogo-vazio.png");
            });
            jk.Close();
            EsperarIdle();
            CapturarVerificacao5000();
        }
    
        // 47) Verificação de barras com 5000 linhas sintéticas (prova da virtualização): tempo de abrir, reordenar, filtrar,
        // buscar e selecionar gravado em tempos-5000.txt na pasta das capturas.
        static void CapturarVerificacao5000()
        {
            var base0 = Amostras.Ler("verificacao").AsArray();
            var arr = new System.Text.Json.Nodes.JsonArray();
            var rnd = new Random(42);
            for (int i = 0; i < 5000; i++)
            {
                var n = base0[i % base0.Count].DeepClone();
                n["barra"] = "B" + (i + 1);
                double t = Math.Round(rnd.NextDouble() * 1.15, 3);
                if (S0(n, "criterio") != "nao_verificada") { n["taxa"] = t; n["ok"] = t <= 1.0; }
                arr.Add(n);
            }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var log = new StringBuilder().AppendLine("Verificação de barras, 5000 linhas sintéticas (GradeVirtual)");
            void Marca(string o) { EsperarIdle(); log.AppendLine(o + ": " + sw.ElapsedMilliseconds + " ms"); sw.Restart(); }
            var j = new JanelaVerificacaoBarras(arr);
            PrepararDialogoForaDaTela(j);
            j.Show();
            Marca("abrir (construir + layout + 1º quadro)");
            RenderizarJanela(j, (int)j.Width, (int)j.Height, "47-verificacao-5000.png");
            sw.Restart();
            j.Ordenar(0, false); Marca("reordenar por barra");
            j.Filtrar(true, ""); Marca("filtrar só reprovadas");
            RenderizarJanela(j, (int)j.Width, (int)j.Height, "47-verificacao-5000-reprovadas.png");
            sw.Restart();
            j.Filtrar(false, ""); j.Ordenar(6, true); Marca("voltar a todas, por aproveitamento");
            j.Close();
            EsperarIdle();
            File.WriteAllText(Path.Combine(pastaSaida, "tempos-5000.txt"), log.ToString(), new UTF8Encoding(false));
            Console.WriteLine(log);
        }
        static string S0(System.Text.Json.Nodes.JsonNode n, string k) { try { return n?[k]?.GetValue<string>() ?? ""; } catch { return ""; } }
    }
}
