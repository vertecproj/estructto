// Modo de capturas automaticas de tela para QA visual (--capturas <pasta_saida> [arquivo.estructto]).
// Abre o modelo, calcula (F5), renderiza a janela principal (1600x900 e 1280x720) e todos os dialogos/abas
// fora da tela com RenderTargetBitmap (96 dpi) + PngBitmapEncoder, e encerra com codigo 0 (ou 1 + erros.txt).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Text.Json.Nodes;
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
            if (args.Length >= 3 && args[1] == "--testes-atualizacao") { ConfigurarAtualizacao(w, args[2]); return true; }   // TestesAtualizacao.cs
            if (args.Length >= 3 && args[1] == "--testes-novo") { ConfigurarNovo(w, args[2]); return true; }   // TestesNovo.cs
            if (args.Length >= 3 && args[1] == "--testes-comandos") { ConfigurarComandos(w, args[2]); return true; }   // TestesComandos.cs
            if (args.Length >= 3 && args[1] == "--testes-interferencia") { ConfigurarInterferencia(w, args[2], args.Length >= 4 ? args[3] : null); return true; }   // TestesFluxo.cs
            if (args.Length >= 3 && args[1] == "--testes-fluxo") { ConfigurarFluxos(w, args[2], args.Length >= 4 ? args[3] : null, args.Length >= 5 ? args[4] : null); return true; }   // TestesFluxo.cs
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
        // contraventado: X de cantoneiras nos dois vãos da fachada y = 0 (gussets nas capturas das ligações).
        static Modelo3D Portico2x2(bool contraventado = false)
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
                if (i < 2) esp.Barras.Add(new Barra3D { Id = ++idb, Ni = nid(i, j, k + 1), Nj = nid(i + 1, j, k + 1), Secao = "W 310 x 38,7", Aprov = 0.3 + 0.75 * rnd.NextDouble(), Cardinal = 8 });   // vigas penduradas pela mesa superior
                if (j < 2) esp.Barras.Add(new Barra3D { Id = ++idb, Ni = nid(i, j, k + 1), Nj = nid(i, j + 1, k + 1), Secao = "W 310 x 38,7", Aprov = 0.3 + 0.75 * rnd.NextDouble(), Cardinal = 8 });
            }
            if (contraventado)
                for (int k = 0; k < 2; k++) for (int i = 0; i < 2; i++)
                {
                    esp.Barras.Add(new Barra3D { Id = ++idb, Ni = nid(i, 0, k), Nj = nid(i + 1, 0, k + 1), Secao = "L 3\" x 1/4\"", Aprov = 0.5 });
                    esp.Barras.Add(new Barra3D { Id = ++idb, Ni = nid(i + 1, 0, k), Nj = nid(i, 0, k + 1), Secao = "L 3\" x 1/4\"", Aprov = 0.5 });
                }
            return esp;
        }

        // 42) Vista 3D Realista x analítica: guincho (modelo 2D), pórtico espacial 2 x 2 e exemplos do motor (concreto e galpão).
        // Atalho para iterar só nelas: variável ESTRUCTTO_CAPTURAS=3d.
        // V2: sombra do chão. Pórtico 2x2 em duas vistas, Realista DX e Sombreado nativo (59-sombra-<estilo>-<vista>).
        static void CapturasSombra(MainWindow w)
        {
            Esperar(w.NovoEmBranco(true));
            var esp = Portico2x2();
            w.vista3D.Diagrama = Diagrama3D.Nenhum; w.vista3D.MostrarDeformada = false; w.vista3D.MostrarAproveitamento = false;
            w.vista3D.Carregar(esp, false);
            foreach (var (est, nome) in new[] { (EstiloVisual.Realista, "dx"), (EstiloVisual.Sombreado, "nativo") })
            {
                w.DefinirEstilo(est);
                foreach (var (az, el, vn) in new[] { (-60.0, 40.0, "alto"), (-30.0, 18.0, "rasante") })
                {
                    w.vista3D.Reconstruir();
                    w.vista3D.Focar(new System.Windows.Media.Media3D.Point3D(6, 5, 0), 26, az, el);
                    CapturarPrincipal(w, "59-sombra-" + nome + "-" + vn);
                }
            }
            w.FecharEspaciaisSemPerguntar();
            // regressão: obra real do usuário (plataforma elevada ~30 m), importada do CYPE; testes-aceite/ é gitignored (pula se faltar)
            string ed3 = null;
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null && ed3 == null; d = d.Parent) { string c = Path.Combine(d.FullName, "testes-aceite", "PLATAFORMA.ed3"); if (File.Exists(c)) ed3 = c; }
            if (ed3 == null) ed3 = new[] { "C:/dev/wt/v5-vista/testes-aceite/PLATAFORMA.ed3" }.FirstOrDefault(File.Exists);
            if (ed3 == null) return;
            Esperar(w.AbrirTarefa(ed3)); EsperarIdle();
            w.DefinirEstilo(EstiloVisual.Realista);
            foreach (var (az, el, vn) in new[] { (-60.0, 38.0, "alto"), (-30.0, 10.0, "rasante") })
            {
                w.vista3D.Enquadrar(az, el, false); EsperarIdle();
                CapturarPrincipal(w, "59-sombra-plataforma-" + vn);
            }
            w.FecharEspaciaisSemPerguntar();
        }

        // E1: chão do Realista. Modelo de 2 barras, pórtico 2x2, galpão e PLATAFORMA, vista alta e rasante (60-chao-<modelo>-<vista>).
        static void CapturasChao(MainWindow w)
        {
            // documentos reais (Propriedades e barra de estado batem com o uso): arquivos do motor gerados em C:/dev/build/modelos-chao + exemplos + PLATAFORMA
            var abrir = new List<(string, Func<System.Threading.Tasks.Task>)>();
            foreach (string n in new[] { "2barras", "2barras-1m" })
            {
                string f = "C:/dev/build/modelos-chao/" + n + ".json"; string nn = n;
                if (File.Exists(f)) abrir.Add((nn, () => w.AbrirTarefa(f)));
            }
            foreach (string ex in new[] { "portico_aco", "galpao_aco" }) { string e = ex; abrir.Add((e, () => w.AbrirEspacial(() => DocumentoEspacial.Exemplo(e), "exemplo"))); }
            string ed3 = new[] { "C:/dev/wt/v5-vista/testes-aceite/PLATAFORMA.ed3", "H:/20.  CLAUDE CODE AGENTES/14. CYPE/estructto/testes-aceite/PLATAFORMA.ed3" }.FirstOrDefault(File.Exists);
            if (ed3 != null) abrir.Add(("plataforma", () => w.AbrirTarefa(ed3)));
            foreach (var (nome, ab) in abrir)
            {
                Esperar(ab()); EsperarIdle(); w.DefinirEstilo(EstiloVisual.Realista);
                foreach (var (az, el, vn) in new[] { (-60.0, 38.0, "alto"), (-30.0, 8.0, "rasante") })
                { w.vista3D.Enquadrar(az, el, false); EsperarIdle(); CapturarPrincipal(w, "60-chao-" + nome + "-" + vn); }
                if (nome == "galpao_aco")
                {
                    w.vista3D.Enquadrar(-60, 38, false); w.Vista.Diag = Diagrama.Momento; w.Hud.Atualizar(); EsperarIdle(); CapturarPrincipal(w, "60-chao-chip-sem-calculo");   // E2: modelo desenhado, sem cálculo
                    w.Vista.Diag = Diagrama.Nenhum; w.Hud.Atualizar();
                }
                w.FecharEspaciaisSemPerguntar();
            }
        }

        // V1: seta e nó escolhido atrás de uma viga (pórtico 2x2 real). Sem "sempre visíveis" a viga os esconde (Realista DX e
        // Sombreado nativo); com ele a seta e o nó aparecem por cima.
        static void CapturasCargasProfundidade(MainWindow w)
        {
            Esperar(w.NovoEmBranco(true));
            var esp = Portico2x2();
            esp.Cargas.Add(new Carga3D { No = 17, F = new System.Windows.Media.Media3D.Vector3D(0, -10, -5) });   // nó (6, 10, 3,5): atrás da viga da frente
            w.vista3D.Diagrama = Diagrama3D.Nenhum; w.vista3D.MostrarDeformada = false; w.vista3D.MostrarAproveitamento = false;
            w.vista3D.Carregar(esp, false);
            w.vista3D.Escolher(esp.Nos[16]);
            foreach (var (est, nome) in new[] { (EstiloVisual.Realista, "dx"), (EstiloVisual.Sombreado, "nativo") })
            {
                w.DefinirEstilo(est);
                foreach (bool topo in new[] { false, true })
                {
                    w.vista3D.CargasSempre = topo;
                    w.vista3D.Reconstruir();
                    w.vista3D.Focar(new System.Windows.Media.Media3D.Point3D(6, 5, 3.5), 6, -90, 0);
                    CapturarPrincipal(w, "58-cargas-profundidade-" + nome + "-" + (topo ? "sempre" : "oculta"));
                }
            }
            w.vista3D.CargasSempre = null;
            w.FecharEspaciaisSemPerguntar();
        }

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
                w.vista3D.LigacoesAmostra = true;   // ligações de exemplo (LigacoesAmostra.Gerar) nos modelos sem ligações do motor
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
                if (real)
                {
                    w.vista3D.Perspectiva = true;
                    CapturarPrincipal(w, "42-realista-portico2x2-perspectiva");
                    // sobreposição: o nó escolhido (vista nativa por cima) tem de cair no nó das peças DX (FOV horizontal x vertical)
                    w.vista3D.MostrarVista(VistaPadrao.Iso, false);
                    w.vista3D.Escolher(esp.Nos.OrderByDescending(n => n.Z).ThenByDescending(n => n.X + n.Y).First());
                    CapturarPrincipal(w, "42-realista-portico2x2-perspectiva-no");
                    {   // picking em perspectiva: o meio de uma viga de cima, na frente, acha essa viga (DX e nativo na mesma câmera)
                        var vf = esp.Barras.Where(b => esp.Nos.First(n => n.Id == b.Ni).Z > 6 && esp.Nos.First(n => n.Id == b.Nj).Z > 6)
                                           .OrderBy(b => esp.Nos.First(n => n.Id == b.Ni).Y + esp.Nos.First(n => n.Id == b.Nj).Y).First();
                        No3D n1 = esp.Nos.First(n => n.Id == vf.Ni), n2 = esp.Nos.First(n => n.Id == vf.Nj);
                        object pk = w.vista3D.Acertar(w.vista3D.Tela(new Point3D((n1.X + n2.X) / 2, (n1.Y + n2.Y) / 2, (n1.Z + n2.Z) / 2)));
                        if (pk != vf) throw new InvalidOperationException("Realista: picking em perspectiva não achou a viga " + vf.Id + " (achou " + ((pk as Barra3D)?.Id.ToString() ?? pk?.ToString() ?? "nada") + ")");
                    }
                    w.vista3D.Escolher(null);
                    w.vista3D.Perspectiva = false;
                }
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

                    // close-ups das ligações: chapa de topo viga-pilar com parafusos, placa de base com chumbadores,
                    // vão contraventado com gussets (Render3DLigacoes.cs, exemplos no formato do contrato)
                    w.vista3D.LimparSelecao(); w.vista3D.Escolher(null);   // o halo da seleção (pós-efeito) passa por cima de tudo: fora dos close-ups
                    Func<int, int, int, int> nid = (i, j, k) => 1 + i + 3 * j + 9 * k;
                    Point3D PNo(Modelo3D m, int id) { var n = m.Nos.First(x => x.Id == id); return new Point3D(n.X, n.Y, n.Z); }
                    w.vista3D.Focar(PNo(esp, nid(2, 0, 1)) + new Vector3D(-0.1, 0, -0.05), 1.9, -35, 18);
                    CapturarPrincipal(w, "42-realista-ligacao-chapa-topo");
                    w.vista3D.Focar(PNo(esp, nid(2, 0, 1)), 2.2, -90, 90);
                    CapturarPrincipal(w, "42-realista-ligacao-chapa-topo-planta");
                    w.vista3D.Focar(PNo(esp, nid(2, 0, 1)), 2.2, -55, 28);
                    CapturarPrincipal(w, "42-realista-ligacao-chapa-topo-iso");
                    w.vista3D.Focar(PNo(esp, nid(2, 0, 0)) + new Vector3D(0, 0, 0.12), 1.3, -40, 26);
                    CapturarPrincipal(w, "42-realista-ligacao-placa-base");
                    // clique na placa de base (fora do pilar) não escolhe barra nenhuma (antes: a última barra da malha)
                    if (w.vista3D.Acertar(w.vista3D.Tela(PNo(esp, nid(2, 0, 0)) + new Vector3D(0.12, 0.12, 0.026))) is Barra3D bp)
                        throw new InvalidOperationException("Realista: clique na placa de base escolheu a barra " + bp.Id);
                    var cv = Portico2x2(true);
                    w.vista3D.Carregar(cv, false);
                    w.vista3D.Focar(new Point3D(6, 0, 3.5), 15, -90, 12);
                    CapturarPrincipal(w, "42-realista-contraventamento");
                    w.vista3D.Focar(PNo(cv, nid(1, 0, 1)) + new Vector3D(0, 0, -0.15), 2.4, -75, 14);
                    CapturarPrincipal(w, "42-realista-ligacao-gusset");
                    // diagonal que chega no meio de um pilar contínuo (sem viga no nó), uma seção de diagonal por captura: o gusset tem de ficar
                    // na face da diagonal (alma do W, costas do L/U, lado do tubo), soldado rente à face do pilar, com os parafusos na diagonal
                    foreach (var (secD, nomeD, rotD) in new[] { ("L 3\" x 1/4\"", "", 0.0), ("W 310 x 38,7", "-w", 0.0), ("W 310 x 38,7", "-w90", 90.0), ("U 8\" x 17,1", "-u", 0.0), ("Tubo 100x50x3,00", "-tubo", 0.0) })
                    {
                        var dg = new Modelo3D();
                        dg.Nos.Add(new No3D { Id = 1, X = 0, Y = 0, Z = 0, Apoios = new[] { true, true, true, true, true, true } });
                        dg.Nos.Add(new No3D { Id = 2, X = 0, Y = 0, Z = 2.5 }); dg.Nos.Add(new No3D { Id = 3, X = 0, Y = 0, Z = 5 });
                        dg.Nos.Add(new No3D { Id = 4, X = 2.5, Y = 0, Z = 0, Apoios = new[] { true, true, true, false, false, false } });
                        dg.Barras.Add(new Barra3D { Id = 1, Ni = 1, Nj = 2, Secao = "W 250 x 32,7", Aprov = 0.4 });
                        dg.Barras.Add(new Barra3D { Id = 2, Ni = 2, Nj = 3, Secao = "W 250 x 32,7", Aprov = 0.4 });
                        dg.Barras.Add(new Barra3D { Id = 3, Ni = 4, Nj = 2, Secao = secD, Aprov = 0.5, Rot = rotD });
                        w.vista3D.Carregar(dg, false);
                        w.vista3D.Focar(new Point3D(0, 0, 2.4), 1.6, -90, 10);
                        CapturarPrincipal(w, "42-realista-ligacao-gusset-pilar-meio" + nomeD);
                        w.vista3D.Focar(new Point3D(0, 0, 2.4), 1.6, -40, 20);
                        CapturarPrincipal(w, "42-realista-ligacao-gusset-pilar-meio" + nomeD + "-iso");
                        w.vista3D.Focar(new Point3D(0, 0, 2.4), 1.6, 140, 20);
                        CapturarPrincipal(w, "42-realista-ligacao-gusset-pilar-meio" + nomeD + "-tras");
                    }
                    // viga secundária (W 250) apoiada na alma da principal (W 310), topos alinhados: chapa simples + cope na mesa de cima
                    var sb = Portico2x2(false);
                    int na = sb.Nos.Max(n => n.Id) + 1, nb = na + 1;
                    sb.Nos.Add(new No3D { Id = na, X = 3, Y = 0, Z = 3.5 }); sb.Nos.Add(new No3D { Id = nb, X = 3, Y = 5, Z = 3.5 });
                    foreach (var (n0, n1, nm) in new[] { (nid(0, 0, 1), nid(1, 0, 1), na), (nid(0, 1, 1), nid(1, 1, 1), nb) })
                    {
                        var vb = sb.Barras.First(b => b.Ni == n0 && b.Nj == n1); int fim = vb.Nj; vb.Nj = nm;
                        sb.Barras.Add(new Barra3D { Id = sb.Barras.Max(b => b.Id) + 1, Ni = nm, Nj = fim, Secao = vb.Secao, Cardinal = 8, Aprov = vb.Aprov });
                    }
                    sb.Barras.Add(new Barra3D { Id = sb.Barras.Max(b => b.Id) + 1, Ni = na, Nj = nb, Secao = "W 250 x 32,7", Cardinal = 8, Aprov = 0.5 });
                    w.vista3D.Carregar(sb, false);
                    w.vista3D.Focar(new Point3D(3, 0.2, 3.4), 1.2, -35, 40);   // fora do reflexo da luz chave nas mesas
                    CapturarPrincipal(w, "42-realista-ligacao-cope");
                    w.vista3D.Carregar(esp, false);
                    w.vista3D.MostrarVista(VistaPadrao.Iso, false);
                }

                // desempenho: ~2.400 barras (12 x 10 vãos, 6 pavimentos), sem e com as ligações de exemplo; tempos em 42-tempos.txt
                foreach (bool amostras in real ? new[] { false, true } : new[] { false })
                {
                    w.vista3D.LigacoesAmostra = amostras;
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
                    CapturarPrincipal(w, "42-" + suf + "-2400barras" + (amostras ? "-ligacoes" : ""));
                    double tCaptura = sw.Elapsed.TotalMilliseconds;
                    sw.Restart();
                    object achou = null;
                    for (int r = 0; r < 20; r++) achou = w.vista3D.Acertar(new Point(w.vista3D.ActualWidth / 2, w.vista3D.ActualHeight / 2));
                    double tPick = sw.Elapsed.TotalMilliseconds / 20;
                    string dx = w.vista3D.UsandoDx ? string.Format(System.Globalization.CultureInfo.InvariantCulture, ", Render3D: montar cena {0:0} ms, quadro {1:0.0} ms", w.vista3D.UltimaMontagemDxMs, w.vista3D.MedirQuadroDx()) : "";
                    File.AppendAllText(Path.Combine(pastaSaida, "42-tempos.txt"), string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "{0}{8}: {1} barras, carregar+enquadrar {2:0} ms, 2 capturas {3:0} ms, picking {4:0.0} ms ({5}){6}{7}", suf, g.Barras.Count, tCarregar, tCaptura, tPick, achou?.GetType().Name ?? "nada", dx, Environment.NewLine, amostras ? " + ligações de exemplo" : ""));
                    w.vista3D.LigacoesAmostra = true;
                }
                w.MostrarVista3D(false);
                EsperarIdle();

                foreach (string ex in real ? new[] { "edificio_concreto", "galpao_aco" } : new[] { "edificio_concreto" })
                {
                    Esperar(w.AbrirEspacial(() => DocumentoEspacial.Exemplo(ex), "exemplo"));
                    EsperarIdle();
                    w.vista3D.MostrarVista(VistaPadrao.Iso, false);
                    CapturarPrincipal(w, "42-" + suf + "-" + ex);
                    if (real && ex == "galpao_aco")
                    {
                        // ligações reais do motor: identifica os grupos, dá um tipo a cada grupo viga-pilar/viga-viga e redesenha
                        Esperar(Motor.Post("/api/ligacoes/identificar"));
                        var tg = Motor.Get("/api/ligacoes/grupos"); Esperar(tg);
                        foreach (JsonNode g in tg.Result.AsArray())
                        {
                            string t = (string)g["tipo"], tl = t == "viga_pilar_mesa" ? "chapa_topo_estendida" : t == "viga_pilar_alma" || t == "viga_viga" ? "chapa_simples_alma" : null;
                            if (tl != null) Esperar(Motor.Post("/api/ligacoes/grupos/editar", new JsonObject { ["acao"] = "projeto", ["grupo"] = (string)g["id"], ["tipo_ligacao"] = tl, ["dados"] = new JsonObject() }));
                        }
                        w.Atualizar3D(false);
                        Esperar(w.TarefaLigacoes3D); EsperarIdle();
                        if (w.vista3D.ModeloAtual.Ligacoes.Count == 0) throw new InvalidOperationException("galpão: nenhuma ligação do motor chegou à Vista3D");
                        w.vista3D.MostrarVista(VistaPadrao.Iso, false);
                        CapturarPrincipal(w, "42-realista-galpao_aco-ligacoes");
                        w.vista3D.Focar(new Point3D(0, 0, 6), 2.4, -40, 20);
                        CapturarPrincipal(w, "42-realista-ligacao-motor-chapa-topo");
                        w.vista3D.Focar(new Point3D(0, 0, 0.15), 1.4, -40, 24);
                        CapturarPrincipal(w, "42-realista-ligacao-motor-placa-base");
                    }
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
        // 01) Tela inicial (1600x900 e 1280x720), novo projeto nos 3 passos, cada página do Arquivo e Opções.
        static void CapturasInicio(MainWindow w)
        {
            // recentes de verdade para a captura: um .json do motor na pasta de saída (fixado) + um caminho inexistente
            string rec = Path.Combine(pastaSaida, "Galpao demo.json");
            try
            {
                Esperar(w.AbrirEspacial(() => DocumentoEspacial.Exemplo("galpao_aco"), "exemplo"));
                File.WriteAllText(rec, w.EspacialAtivo.JsonFormatado());
                Config.AdicionarRecente(rec); Config.Fixar(rec, true);
                Config.AdicionarRecente(Path.Combine(pastaSaida, "Edificio sede.json"));
                w.FecharEspaciaisSemPerguntar();
            }
            catch (Exception ex) { File.AppendAllText(Path.Combine(pastaSaida, "erros.txt"), "01-recentes: " + ex.Message + Environment.NewLine); }

            w.MontarTelaInicial();
            w.TelaInicial.Visibility = Visibility.Visible;
            w.Bastidor.IsOpen = false;
            Esperar(w.MiniaturasInicio);
            CapturarPrincipal(w, "01-inicio", () => Esperar(w.MiniaturasInicio));

            foreach (string chave in new[] { "galpao", "vazio", "branco" })
            {
                var j = new JanelaNovoProjeto(w, chave) { Owner = w };
                PrepararDialogoForaDaTela(j);
                j.Show();
                for (int p = 0; p < 3; p++)
                {
                    j.IrPasso(p);
                    Esperar(w.MiniaturasInicio);
                    EsperarIdle(); System.Threading.Thread.Sleep(350); EsperarIdle();
                    Esperar(j.PreviaPronta);
                    EsperarIdle();
                    CapturarDialogo(j, "02-novo-projeto-" + chave + "-passo" + (p + 1) + ".png");
                }
                j.Close();
            }

            // páginas do Arquivo com um projeto aberto (Propriedades e Exportar precisam dele)
            if (File.Exists(rec)) { Esperar(w.AbrirEspacialArquivo(rec)); EsperarIdle(); }
            foreach (string pg in w.PaginasBs.ToList())
            {
                w.MostrarBs(pg);
                EsperarIdle();
                if (pg == "novo" || pg == "inicio") Esperar(w.MiniaturasInicio);
                EsperarIdle();
                CapturarPrincipal(w, "01-backstage-" + pg);
            }
            w.Bastidor.IsOpen = false;
            EsperarIdle();
            w.FecharEspaciaisSemPerguntar();

            // Criar de verdade: edifício 2x6 / 2x5 / 3 níveis em pastaSaida\Sede.json; abre em planta do 1º nível
            {
                var me = ModeloInicial.Por("edificio");
                var d = new NovoProjetoDados { Modelo = me, Grade = me.GradePadrao(), Nome = "Sede", Pasta = pastaSaida, Cliente = "Cliente Exemplo", Engenheiro = "Eng. Teste", Empresa = "PROSSEN", Material = me.Material, Pilar = me.Pilar, Viga = me.Viga, Normas = new List<string> { "NBR 8800" } };
                if (File.Exists(d.Arquivo)) File.Delete(d.Arquivo);
                Esperar(Inicio.Acoes.Novo(w, d));
                EsperarIdle();
                var doc = w.EspacialAtivo ?? throw new InvalidOperationException("novo projeto: documento não abriu");
                if (!File.Exists(d.Arquivo)) throw new InvalidOperationException("novo projeto: arquivo não foi salvo");
                var js = (JsonObject)JsonNode.Parse(File.ReadAllText(d.Arquivo));
                if (js["config"]?["grade"]?["x"]?.AsArray().Count != 2) throw new InvalidOperationException("novo projeto: config.grade não gravada");
                if ((string)js["config"]?["projeto"]?["empresa"] != "PROSSEN") throw new InvalidOperationException("novo projeto: config.projeto não gravado");
                if (Niveis.Lista(doc.Json).Count != 4) throw new InvalidOperationException("novo projeto: esperados 4 níveis, veio " + Niveis.Lista(doc.Json).Count);
                if (!string.Equals(Config.Recentes().FirstOrDefault(), d.Arquivo, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("novo projeto: não entrou nos recentes");
                CapturarPrincipal(w, "02-novo-projeto-criado");
                w.FecharEspaciaisSemPerguntar();
                Config.RemoverRecente(d.Arquivo);
            }
            w.TelaInicial.Visibility = Visibility.Collapsed;
            try { Config.RemoverRecente(rec); Config.RemoverRecente(Path.Combine(pastaSaida, "Edificio sede.json")); } catch { }
        }

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
            w.Faixa.SelectedTabItem = w.abaEditar;   // aba Editar: botões de modificar do espacial
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
            w.Faixa.SelectedTabItem = w.abaDesenhar;
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

        // 49) Fluxo completo de modelagem 3D (docs/MODELO-ESPACIAL.md, "Fluxo de modelagem"): modelo vazio, 2 níveis, grade 2×1
        // vãos desenhada com Barra no nível ativo + pilares (descem ao Térreo, apoio na base, seção do catálogo), andar copiado 2×
        // para cima, um pórtico escolhido por janela e espelhado, carga em área, F5 e aproveitamento no Realista com as setas
        // das cargas. Confere as contagens em cada passo. Só elas: ESTRUCTTO_CAPTURAS=fluxo3d.
        static void CapturasFluxo3D(MainWindow w)
        {
            void Falha(string s) => throw new InvalidOperationException("Fluxo 3D: " + s + " | " + w.StatusMsg.Text);
            var log = new StringBuilder();
            Esperar(w.AbrirEspacial(() => DocumentoEspacial.Vazio("Fluxo 3D"), "modelo vazio"));
            DocumentoEspacial D() => w.EspacialAtivo ?? throw new InvalidOperationException("Fluxo 3D: documento espacial sumiu");
            var J = new Func<System.Text.Json.Nodes.JsonObject>(() => D().Json);
            int Nos() => J()["nos"].AsObject().Count;
            int Barras() => J()["barras"].AsObject().Count;
            int Apoios() => J()["nos"].AsObject().Count(kv => kv.Value["apoio"] is System.Text.Json.Nodes.JsonArray a && a.Any(x => (bool)x));
            void Conta(string passo, int nos, int barras, int apoios = -1)
            {
                if (Nos() != nos || Barras() != barras || (apoios >= 0 && Apoios() != apoios))
                    Falha(passo + ": " + Nos() + " nós, " + Barras() + " barras, " + Apoios() + " apoios (esperado " + nos + ", " + barras + (apoios >= 0 ? ", " + apoios : "") + ")");
                log.AppendLine("ok " + passo + ": " + nos + " nós, " + barras + " barras" + (apoios >= 0 ? ", " + apoios + " apoios" : ""));
            }
            double Z(string no) => (double)J()["nos"][no]["z"];
            w.Faixa.SelectedTabItem = w.abaDesenhar;
            var v = w.vista3D;
            Conta("modelo vazio", 0, 0);
            CapturarPrincipal(w, "49-fluxo3d-1-vazio");

            // 2 níveis: Térreo (z = 0) + P1 (z = 3); um caso G para as cargas
            Esperar(w.OperarNiveis(() => Motor.Post("/api/modelo/pavimento", new System.Text.Json.Nodes.JsonObject { ["nome"] = "P1", ["z"] = 3.0, ["diafragma"] = true }), "teste: nível P1"));
            Esperar(w.OperarEspacial(dd => dd.ComJanela(() => Motor.Post("/api/modelo/caso", new System.Text.Json.Nodes.JsonObject { ["nome"] = "G", ["tipo"] = "G" })), "teste: caso G"));
            D().CasoAtivo = "G";   // o modelo vazio já nasce com o caso PP (peso próprio): o caso novo não vira o ativo sozinho
            if (Niveis.Lista(J()).Count != 2 || D().CasoAtivo != "G") Falha("níveis " + Niveis.Lista(J()).Count + " / caso " + D().CasoAtivo);
            log.AppendLine("ok 2 níveis (Térreo, P1), caso G ativo");

            // P1 ativo = plano de desenho; vigas da grade 2×1 (vãos de 6 m em X, 5 m em Y) com a ferramenta Barra
            w.AtivarNivel(3);
            if (v.PlanoAtual.Normal != 'Z' || Math.Abs(v.PlanoAtual.Valor - 3) > 1e-9) Falha("ativar P1 não trocou o plano de desenho: " + v.PlanoAtual.Nome);
            v.MostrarVista(VistaPadrao.Topo, false);
            v.ZoomFator(0.45);
            EsperarIdle();
            Point T(double x, double y, double z) => v.Tela(new System.Windows.Media.Media3D.Point3D(x, y, z));
            void Clicar(double x, double y, double z) { Esperar(v.ClicarTela(T(x, y, z))); EsperarIdle(); }
            void Corrente(params (double x, double y)[] pts) { foreach (var (x, y) in pts) Clicar(x, y, 3); v.Tecla(System.Windows.Input.Key.Escape); }
            w.UsarFerramenta3D(Ferramenta3D.Barra);
            Corrente((0, 0), (6, 0), (12, 0));
            Corrente((0, 5), (6, 5), (12, 5));
            Corrente((0, 0), (0, 5)); Corrente((6, 0), (6, 5)); Corrente((12, 0), (12, 5));
            Conta("grade 2×1 desenhada no P1", 6, 7);
            if (J()["barras"].AsObject().Any(kv => Math.Abs(Z((string)kv.Value["ni"]) - 3) > 1e-6 || Math.Abs(Z((string)kv.Value["nj"]) - 3) > 1e-6)) Falha("viga fora do plano z = 3");
            v.MoverTela(T(9, 2.5, 3));
            CapturarPrincipal(w, "49-fluxo3d-2-vigas-p1");

            // pilares: seção do catálogo (entra no modelo pelo motor), Apoio na base, clique nos nós do P1 desce até o Térreo
            string perfil = Esperar(Motor.Get("/api/catalogo?filtro=W250")).AsArray().Select(x => (string)x["nome"]).First(x => J()["secoes"][x] == null);
            Esperar(w.UsarSecao3D(perfil));
            if (J()["secoes"][perfil] == null) Falha("seção do catálogo não entrou no modelo: " + perfil);
            w.ApoioBase3D = true;
            w.UsarFerramenta3D(Ferramenta3D.Pilar);
            v.MoverTela(T(12, 5, 3));
            CapturarPrincipal(w, "49-fluxo3d-3-pilar-cursor");
            foreach (var (x, y) in new[] { (0.0, 0.0), (6.0, 0.0), (12.0, 0.0), (0.0, 5.0), (6.0, 5.0), (12.0, 5.0) }) Clicar(x, y, 3);
            Conta("pilares P1 → Térreo com apoio na base", 12, 13, 6);
            var pilares = J()["barras"].AsObject().Where(kv => (string)kv.Value["secao"] == perfil).ToList();
            if (pilares.Count != 6 || pilares.Any(kv => Math.Abs(Z((string)kv.Value["ni"])) > 1e-6 || Math.Abs(Z((string)kv.Value["nj"]) - 3) > 1e-6))
                Falha("pilares: " + pilares.Count + " com " + perfil + " (esperado 6, de z = 0 a z = 3)");
            if (J()["barras"].AsObject().Count(kv => (string)kv.Value["secao"] == "W310x38.7") != 7) Falha("vigas sem a seção padrão W310x38.7");
            w.ApoioBase3D = false;
            v.Tecla(System.Windows.Input.Key.Escape);
            CapturarPrincipal(w, "49-fluxo3d-3-pilares", () => v.MostrarVista(VistaPadrao.Iso, false));

            // copiar o andar 2× para cima (h = 3 m): um lote só
            w.Completo3D();
            Esperar(w.Lote(Niveis.CopiarParaCima(J(), 0, 3, 2, 3.0), "teste: copiar andar 2×"));
            Conta("andar copiado 2× para cima", 24, 39, 6);
            if (Niveis.Lista(J()).Count != 4) Falha("níveis depois da cópia: " + Niveis.Lista(J()).Count);
            CapturarPrincipal(w, "49-fluxo3d-4-copiado", () => v.MostrarVista(VistaPadrao.Iso, false));

            // elevação y = 0: só esse pórtico na vista; janela (esq. → dir.) pega ele inteiro; espelhar em Y = -2,5 (cópia em y = -5)
            w.Elevacao('y', 0);
            v.MostrarVista(VistaPadrao.Frente, false);
            EsperarIdle();
            if (v.PlanoAtual.Normal != 'Y') Falha("elevação não trocou o plano de desenho: " + v.PlanoAtual.Nome);
            v.Ferramenta = Ferramenta3D.Selecionar;
            v.SelecionarCaixa(T(-1, 0, -1), T(13, 0, 10));
            if (v.Selecao.Nos.Count != 12 || v.Selecao.Barras.Count != 15) Falha("janela no pórtico y = 0: " + v.Selecao.Nos.Count + " nós, " + v.Selecao.Barras.Count + " barras (esperado 12, 15)");
            CapturarPrincipal(w, "49-fluxo3d-5-janela-portico", () => v.MostrarVista(VistaPadrao.Frente, false));
            w.AbrirEdicaoEsp("espelhar");
            var pe = w.PainelEdicaoAberto;
            if (pe == null) Falha("painel Espelhar não abriu");
            ((ComboBox)pe.Campos["plano"]).SelectedItem = "Y"; ((TextBox)pe.Campos["valor"]).Text = "-2,5";
            EsperarIdle();
            CapturarDialogo(pe, "49-fluxo3d-5-painel-espelhar.png");
            Esperar(pe.Executar());
            pe.Close();
            Conta("pórtico espelhado", 36, 54, 9);
            v.LimparSelecao();
            w.Completo3D();
            v.Ferramenta = Ferramenta3D.Navegar;
            CapturarPrincipal(w, "49-fluxo3d-6-espelhado", () => v.MostrarVista(VistaPadrao.Iso, false));

            // carga em área no último nível (vigas em X): Σ q·L = p · área; + uma nodal e uma pontual para ver as três setas
            double zt = 9, p = 4;
            var r = DocumentoEspacial.CargaArea(J(), zt, p, true).Where(x => x.q > 1e-9).ToList();
            if (r.Count != 6 || Math.Abs(r.Sum(x => x.q * x.L) - p * 12 * 10) > 1e-6) Falha("carga em área: " + r.Count + " vigas, Σ q·L = " + r.Sum(x => x.q * x.L));
            Esperar(w.DistribuirArea(zt, p, true));
            if (J()["cargas_distribuidas"].AsArray().Count != 6) Falha("carga em área: " + J()["cargas_distribuidas"].AsArray().Count + " distribuídas (esperado 6)");
            bool Em(System.Text.Json.Nodes.JsonNode n, double x, double y, double z) => Math.Abs((double)n["x"] - x) < 1e-6 && Math.Abs((double)n["y"] - y) < 1e-6 && Math.Abs((double)n["z"] - z) < 1e-6;
            string noTopo = J()["nos"].AsObject().First(kv => Em(kv.Value, 0, -5, zt)).Key;
            string vigaY = J()["barras"].AsObject().First(kv => Em(J()["nos"][(string)kv.Value["ni"]], 6, 0, zt) && Em(J()["nos"][(string)kv.Value["nj"]], 6, 5, zt)).Key;
            Esperar(w.OperarEspacial(async dd =>
            {
                await dd.Adicionar("carga_nodal", new System.Text.Json.Nodes.JsonObject { ["caso"] = dd.CasoAtivo, ["no"] = noTopo, ["F"] = new System.Text.Json.Nodes.JsonArray(20.0, 0.0, 0.0, 0.0, 0.0, 0.0) });
                await dd.Adicionar("carga_pontual", new System.Text.Json.Nodes.JsonObject { ["caso"] = dd.CasoAtivo, ["barra"] = vigaY, ["a"] = 2.5, ["F"] = new System.Text.Json.Nodes.JsonArray(0.0, 0.0, -15.0) });
            }, "teste: nodal e pontual"));
            int cargas = D().ParaVista().Cargas.Count;
            if (cargas != 8) Falha("setas na vista: " + cargas + " (esperado 8: 6 distribuídas, 1 nodal, 1 pontual)");
            log.AppendLine("ok carga em área: 6 vigas, Σ q·L = " + Num.Fmt(p * 120, 0) + " kN; 8 cargas do caso G na vista");
            CapturarPrincipal(w, "49-fluxo3d-7-cargas", () => v.MostrarVista(VistaPadrao.Iso, false));

            // F5 e aproveitamento: analítico e Realista, com as setas
            Esperar(w.CalcularEspacial());
            if (!D().Calculado || D().PiorPorBarra.Count != 54) Falha("F5: calculado " + D().Calculado + ", verificação de " + D().PiorPorBarra.Count + " barras (esperado 54)");
            w.MostrarDiagrama(Diagrama.Verificacao);
            log.AppendLine("ok F5: 54 barras verificadas; " + D().Resumo());
            bool real = Config.Realista3D;
            w.DefinirRealista(false);
            CapturarPrincipal(w, "49-fluxo3d-8-aproveitamento", () => v.MostrarVista(VistaPadrao.Iso, false));
            w.DefinirRealista(true);
            CapturarPrincipal(w, "49-fluxo3d-9-aproveitamento-realista", () => v.MostrarVista(VistaPadrao.Iso, false));
            w.DefinirRealista(real);
            File.WriteAllText(Path.Combine(pastaSaida, "49-fluxo3d.txt"), log.ToString(), new UTF8Encoding(false));
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
            w.PainelTabelas.Show(); w.PainelTabelas.IsSelected = true; EsperarIdle();
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

            (w as MainWindow)?.vista3D?.PrepararCaptura();   // Render3D (D3DImage) desenha 2 quadros antes da leitura
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
                TemaPlant.AjustarFaixa(w, TemaPlant.Selo(24));
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

                // 01) Tela inicial, novo projeto e Backstage (CapturasInicio; só elas: ESTRUCTTO_CAPTURAS=inicio)
                {
                    string dev = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "..", "motor"));
                    if (Environment.GetEnvironmentVariable("ESTRUCTTO_MOTOR") == null && Directory.Exists(dev)) Environment.SetEnvironmentVariable("ESTRUCTTO_MOTOR", dev);
                }
                CapturasInicio(w);
                if (Environment.GetEnvironmentVariable("ESTRUCTTO_CAPTURAS") == "inicio")
                {
                    w.alterado = false;
                    w.Close();
                    Environment.Exit(0);
                }

                // Abre o modelo (o .estructto antigo é convertido para 3D) e calcula (F5)
                {
                    string dev = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "..", "motor"));
                    if (Environment.GetEnvironmentVariable("ESTRUCTTO_MOTOR") == null && Directory.Exists(dev)) Environment.SetEnvironmentVariable("ESTRUCTTO_MOTOR", dev);
                }
                Esperar(w.AbrirTarefa(caminhoModelo));
                if (Environment.GetEnvironmentVariable("ESTRUCTTO_CAPTURAS") == "modelo")
                {   // só o modelo dado (Realista com as ligações do motor): vista iso + close-up em cada nó com ligação; depois sai
                    w.DefinirRealista(true); w.MostrarDiagrama(Diagrama.Nenhum); w.MostrarVista3D(true); EsperarIdle();
                    w.Atualizar3D(false); Esperar(w.TarefaLigacoes3D); EsperarIdle();
                    w.vista3D.MostrarVista(VistaPadrao.Iso, false);
                    CapturarPrincipal(w, "modelo-iso");
                    var m3 = w.vista3D.ModeloAtual;
                    foreach (int no in m3.Ligacoes.Select(l => l.No).Distinct())
                    {
                        var n = m3.Nos.First(x => x.Id == no); var p = new Point3D(n.X, n.Y, n.Z);
                        w.vista3D.Focar(p, 1.6, -90, 8); CapturarPrincipal(w, $"modelo-no{no}-frente");
                        w.vista3D.Focar(p, 1.6, -40, 22); CapturarPrincipal(w, $"modelo-no{no}-iso");
                        // perto da borda soldada: ~16 cm do nó para o lado da barra da ligação (na horizontal), de frente e de trás
                        var bl = m3.Barras.FirstOrDefault(b => b.Id == m3.Ligacoes.First(l => l.No == no).Barra);
                        var nb = bl == null ? null : m3.Nos.FirstOrDefault(x => x.Id == (bl.Ni == no ? bl.Nj : bl.Ni));
                        Vector3D dh = nb == null ? new Vector3D() : new Vector3D(nb.X - n.X, nb.Y - n.Y, 0); if (dh.Length > 1e-9) dh.Normalize();
                        var pp = p + dh * 0.16;
                        w.vista3D.Focar(pp, 0.45, -90, 5); CapturarPrincipal(w, $"modelo-no{no}-perto");   // solda e parafusos
                        w.vista3D.Focar(pp, 0.45, 90, 5); CapturarPrincipal(w, $"modelo-no{no}-perto-tras");
                        w.vista3D.Focar(pp, 0.45, -60, 30); CapturarPrincipal(w, $"modelo-no{no}-perto-iso");
                    }
                    w.alterado = false; w.Close(); Environment.Exit(0);
                }
                w.TelaInicial.Visibility = Visibility.Collapsed;
                EsperarIdle();
                CapturaAtualizacao(w);   // TestesAtualizacao.cs: faixa "Nova versão disponível"
                if (w.EspacialAtivo == null) throw new InvalidOperationException("o modelo não abriu como documento 3D: " + caminhoModelo);
                string so = Environment.GetEnvironmentVariable("ESTRUCTTO_CAPTURAS");
                Action<Action> so1 = f => { f(); w.alterado = false; w.Close(); Environment.Exit(0); };
                if (so == "chao") so1(() => CapturasChao(w));   // só as 60) de chão do Realista
                if (so == "sombra") so1(() => CapturasSombra(w));   // só as 59) de sombra do chão
                if (so == "cargasprof") so1(() => CapturasCargasProfundidade(w));   // só as 58) de cargas com profundidade
                if (so == "planos") so1(() => CapturasPlanos(w));                 // só as capturas 43) de níveis e planos
                if (so == "trelicas") so1(() => CapturasTrelicas(w));             // só as 50) de treliças e contraventamentos
                if (so == "ligacoes") so1(() => CapturasLigacoes(w));             // só as 49) do gerenciador de ligações
                if (so == "desenho3d") so1(() => CapturasDesenho3D(w));           // só as 43) do desenho no 3D
                if (so == "snap") so1(() => CapturasSnap(w));                     // só as 50-53) de snap e medir 3D
                if (so == "navegacao") so1(() => CapturasNavegacao(w));
                if (so == "estilo") so1(() => CapturasEstilo(w));                 // só as 57) do estilo visual           // só as 56) do pivô automático da órbita
                if (so == "fluxo3d") so1(() => CapturasFluxo3D(w));               // só as 49) do fluxo de modelagem 3D
                if (so == "a1") so1(() => CapturasA1(w, caminhoModelo));          // só as 54) do produto 3D único (A1)
                if (so == "3d") { bool antes3 = Config.Realista3D; so1(() => { CapturasRealista(w); w.DefinirRealista(antes3); }); }   // só as 42)
                if (so == "projetista") so1(() => CapturasProjetista(w));         // só as 51) do projetista de ligações
                if (so == "w73d") so1(() => CapturasW73d(w));                     // só as w73d-*: 3D do projetista (z-buffer), tubo, teste dos parafusos
                if (so == "placa") so1(() => CapturasPlaca(w, caminhoModelo));    // só as 55) da placa de base pelo motor
                if (so == "cmdline") so1(() => CapturasCmdline(w));
                if (so == "v06") so1(() => CapturarV06(w));                        // só as 47) da v0.6 B (faixa, painéis, seletor, em branco, formas do catálogo)
                if (so == "interf3d") { int np = CapturasInterf3D(w); w.alterado = false; w.Close(); Environment.Exit(np); }   // só as 62) bateria de interferência das ligações locais
                if (so == "geo") so1(() => CapturasGeo(w));                     // só as 61) da geometria das ligações (solda, enrijecedores, placa)
                if (so == "visual") so1(() => CapturasVisual(w));                 // só as 58) close-ups de parafusos, linhas e ligações propostas

                // 02) Modelo convertido (antes de calcular: aviso da conversão) e calculado
                w.MostrarDiagrama(Diagrama.Nenhum);
                w.Faixa.SelectedTabItem = w.abaDesenhar;
                w.vista3D.MostrarVista(VistaPadrao.Iso, false);
                CapturarPrincipal(w, "02-modelo-convertido");
                Esperar(w.CalcularEspacial());
                w.Toast.Visibility = Visibility.Collapsed;
                CapturarPrincipal(w, "02-modelo");

                // 03-06) Diagramas do motor (envoltória ELU) e 07) verificação
                foreach (var (dg, nome) in new[] { (Diagrama.Deformada, "03-deformada"), (Diagrama.Normal, "04-normal"), (Diagrama.Cortante, "05-cortante"), (Diagrama.Momento, "06-momento") })
                {
                    w.MostrarDiagrama(dg);
                    w.vista3D.MostrarVista(VistaPadrao.Frente, false);
                    CapturarPrincipal(w, nome);
                }
                w.MostrarDiagrama(Diagrama.Verificacao, true);
                w.AbasTabela.SelectedIndex = 2;
                CapturarPrincipal(w, "07-verificacao-barras");
                w.FiltrarCriticos(true);
                CapturarPrincipal(w, "07-verificacao-criticos");
                w.FiltrarCriticos(false);
                w.AbasTabela.SelectedIndex = 0;

                // 35) HUD da vista (momento) + recorte do cartão; 36) mapa de aproveitamento; 38) toast; 39) histórico
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
                w.vista3D.MostrarVista(VistaPadrao.Iso, false);
                CapturarPrincipal(w, "36-mapa-calor");
                w.AbasTabela.SelectedIndex = 0;
                w.MostrarDiagrama(Diagrama.Nenhum);
                w.MostrarToast("Calculado — envoltória ELU (motor). Pior barra 62% (flexão).", true);
                CapturarPrincipal(w, "38-toast");
                w.Toast.Visibility = Visibility.Collapsed;
                w.HistoricoCaixa.Visibility = Visibility.Visible;
                CapturarPrincipal(w, "39-historico");
                w.HistoricoCaixa.Visibility = Visibility.Collapsed;

                // 41) Relatório do motor (WebView2) com o anexo das placas pela ponte do pé de pilar (Legado3D.cs)
                try
                {
                    var anexo = w.AnexoPlacasEsp(); Esperar(anexo);
                    var jr = RelatorioMotor.Abrir(w, "guincho-com-placa", anexo.Result);
                    jr.Left = 0; jr.Top = 0;
                    Esperar(jr.Pronto);
                    EsperarIdle(); System.Threading.Thread.Sleep(800); EsperarIdle();
                    Esperar(jr.SalvarPng(Path.Combine(pastaSaida, "41-relatorio-motor.png")));
                    Esperar(jr.GravarPdf(Path.Combine(pastaSaida, "41-relatorio-motor.pdf")));
                    jr.Close();
                }
                catch (Exception ex) { File.AppendAllText(Path.Combine(pastaSaida, "erros.txt"), "41-relatorio-motor: " + ex.Message + Environment.NewLine); }

                // 09) Cada aba da faixa
                foreach (var tab in w.Faixa.Tabs.ToList())
                {
                    string slug = Slug(tab.Header as string);
                    w.vista3D.LimparSelecao();
                    w.Faixa.SelectedTabItem = tab;
                    EsperarIdle();
                    CapturarPrincipal(w, "09-ribbon-" + slug, () => { w.Faixa.SelectedTabItem = tab; TemaPlant.AjustarFaixa(w.Faixa, null); });
                }
                w.Faixa.SelectedTabItem = w.abaApoios;

                // 30-32) Painel Propriedades do documento 3D: modelo, nó do pé (com apoio) e barra
                {
                    var d0 = w.EspacialAtivo;
                    w.EscolherEspacial(null, null);
                    CapturarPrincipal(w, "30-propriedades-modelo");
                    string no0 = d0.Placas.Keys.FirstOrDefault() ?? d0.Json["nos"].AsObject().First().Key;
                    w.EscolherEspacial("no", no0);
                    CapturarPrincipal(w, "31-propriedades-no");
                    w.EscolherEspacial("barra", d0.Json["barras"].AsObject().First().Key);
                    CapturarPrincipal(w, "32-propriedades-barra");
                    w.EscolherEspacial(null, null);
                }

                // 38/39) Paleta de comandos (Ctrl+K) e linha de comando 3D: testes por código + capturas
                {
                    var logP = new StringBuilder();
                    int falhasP = 0;
                    try { falhasP = w.TestesPaletaEDinamica(logP); }
                    catch (Exception exP) { logP.AppendLine("EXCECAO: " + exP); falhasP = 1; }
                    finally { File.WriteAllText(Path.Combine(pastaSaida, "38-teste-paleta-entrada.txt"), logP.ToString(), new UTF8Encoding(false)); }
                    w.alterado = false;
                    w.vista3D.LimparSelecao();
                    EsperarIdle();
                    if (falhasP > 0) throw new InvalidOperationException("Testes da paleta/linha de comando falharam (ver 38-teste-paleta-entrada.txt).");
                    foreach (var par in new[] { new[] { "mo", "38-palette" }, new[] { "w310", "38-palette-perfil" }, new[] { "barra 1", "38-palette-objeto" }, new[] { "", "38-palette-vazia" } })
                    {
                        var pv = new PaletaVisual(w) { };
                        pv.Caixa.Text = par[0];
                        CapturarPrincipal(w, par[1], null, pv, () => new Point(Math.Max(10, (w.Width - pv.Width) / 2), 96));
                    }
                }

                // 20-22, 29) Placa de base pela ponte do pé de pilar (esforços ELU do motor): vistas, trocar chumbador, sugerir,
                // modo parafuso e comparar fixações
                {
                    var d0 = w.EspacialAtivo;
                    bool capturouParafuso = false;
                    foreach (string noP in d0.Placas.Keys.Concat(MainWindow.NosComPlaca(d0)).Distinct().ToList())
                    {
                        w.placaAberta = null;
                        Esperar(w.AbrirPlacaEsp(noP));
                        var jp = w.placaAberta;
                        if (jp == null) throw new InvalidOperationException("Placa de base do nó " + noP + " não abriu (ver o log).");
                        PrepararDialogoForaDaTela(jp);
                        jp.Show();
                        EsperarIdle();
                        string[] vistas = { "planta", "3d", "forcas", "ficha", "calculo" };
                        string pre = "20-placa-" + noP;
                        for (int v = 0; v < vistas.Length; v++)
                        {
                            int vv = v;
                            jp.MostrarAba(vv);
                            RenderizarJanela(jp, 1600, 900, pre + "-" + vistas[vv] + ".png", () => jp.MostrarAba(vv));
                            RenderizarJanela(jp, 1280, 720, pre + "-" + vistas[vv] + "-1280x720.png", () => jp.MostrarAba(vv));
                        }
                        jp.MostrarAba(0);
                        RenderizarJanela(jp, 1600, 900, pre + ".png", () => jp.MostrarAba(0));
                        RenderizarJanela(jp, 1600, 900, pre + "-trocar.png", () => { jp.MostrarAba(0); jp.AbrirTrocar(); });
                        jp.FecharTrocar();
                        if (!capturouParafuso)
                        {
                            RenderizarJanela(jp, 1600, 900, "29-placa-sugerir.png", () => { jp.MostrarAba(0); jp.AbrirSugerir(); });
                            RenderizarJanela(jp, 1600, 900, "29-placa-aplicar-em.png", () => { jp.MostrarAba(0); jp.AbrirAplicarEm(); });
                            jp.FecharFlutuantes();
                            jp.UsarModoParafuso(true);
                            RenderizarJanela(jp, 1600, 900, "21-placa-parafuso.png", () => jp.MostrarAba(0));
                            RenderizarJanela(jp, 1280, 720, "21-placa-parafuso-1280x720.png", () => jp.MostrarAba(1));
                            capturouParafuso = true;
                        }
                        jp.Close();
                        EsperarIdle();
                    }
                    if (w.placaComparar is var (mc, nc) && mc != null)
                    {
                        nc.Placa ??= new PlacaBase { PeloPerfil = true, Sobra = 100, H = 400, B = 300, Esp = 19, Fileiras = 2, PorFileira = 2, Borda = 50, Diametro = 16, Classe = "8.8", FyChapa = 250, Chumbador = "WB 58500" };
                        ResPlaca rpComp = PlacaCalc.Calcular(mc, nc);
                        var jc = new JanelaComparar(mc, nc, rpComp, nc.Placa, new[] { "CB 12300", "WB 12400", "WQE 500 1/2\"" });
                        PrepararDialogoForaDaTela(jc);
                        jc.Show();
                        EsperarIdle();
                        RenderizarJanela(jc, 1600, 900, "22-comparar.png");
                        RenderizarJanela(jc, 1280, 720, "22-comparar-1280x720.png");
                        jc.Close();
                        EsperarIdle();
                    }
                }

                // 26) JanelaAssistente para cada tipo (o resultado abre em 3D)
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

                // 42) Vista 3D Realista (perfis reais, sombra, arestas) lado a lado com a analítica; começa no guincho convertido
                bool realAntes = Config.Realista3D;
                CapturasRealista(w);
                CapturasEstilo(w);   // 57) estilo visual: seletor da vista, chip da barra de status, VS
                CapturasVisual(w);
                w.DefinirRealista(false);

                // 40) Vista 3D analítica de um pórtico espacial de entrada neutra (Modelo3D direto, sem motor)
                {
                    Esperar(w.NovoEmBranco(true));
                    var esp = Portico2x2();
                    w.vista3D.Diagrama = Diagrama3D.Nenhum; w.vista3D.MostrarDeformada = false; w.vista3D.MostrarAproveitamento = true;
                    w.vista3D.Carregar(esp, false);
                    w.vista3D.Escolher(esp.Barras[4]);
                    w.vista3D.MostrarVista(VistaPadrao.Iso, false);
                    CapturarPrincipal(w, "40-vista3d-espacial");
                    w.vista3D.MostrarAproveitamento = false; w.vista3D.MostrarDeformada = true; w.vista3D.EscalaDeformada = 1;
                    w.vista3D.Carregar(esp, false);
                    CapturarPrincipal(w, "40-vista3d-espacial-deformada");
                    w.vista3D.MostrarDeformada = false;
                    w.FecharEspaciaisSemPerguntar();
                }
                w.DefinirRealista(realAntes);

                // 41) Modelo espacial gerado; 43) níveis e planos; 48) edição em lote; 43) desenho; 49) fluxo; 50) treliças;
                // 49) ligações; 50-53) snap e medir; 54) produto 3D único (A1)
                CapturasEspacial(w);
                CapturasPlanos(w);
                CapturasEdicao(w);
                CapturasDesenho3D(w);
                CapturasFluxo3D(w);
                CapturasTrelicas(w);
                CapturasLigacoes(w);
                // 51) Projetista de ligações (biblioteca, chapa simples ao vivo, falha, otimizado, aplicar ao grupo)
                CapturasProjetista(w);
                // 50-53) Snap e Medir 3D (marcadores de cada modo, rastreio, polar, medidas)
                CapturasSnap(w);
                // 56) Pivô automático da órbita e zoom no cursor
                CapturasNavegacao(w);
                CapturasA1(w, caminhoModelo);

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
                // vento por barra (NBR 6123 8.1) e NBR 8400 (port do vento 2D do Bruno; amostras reais do motor, guincho 3D)
                var vb = Amostras.Ler("vento-barras");
                Cap(new JanelaVento(vb["resumo"], vb["geracao"]), "29-acoes-vento-barras.png");
                Cap(new JanelaVento(vb["resumo"]), "29-acoes-vento-barras-sem-gerar.png");
                var v8 = Amostras.Ler("vento-8400");
                Cap(new JanelaVento(v8["resumo"], v8["geracao"]), "29-acoes-vento-8400.png");
                var c8 = Amostras.Ler("combinacoes-8400");
                Cap(new JanelaCombinacoes(c8["comb"], c8["casos"], "comercial", c8["config"]), "33-acoes-combinacoes-8400.png");
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

            // 44) ligações: o projetista (JanelaLigacaoProjeto) usa o motor real — capturas 51) em CapturasProjetista

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
            var jka = new JanelaCatalogo(Amostras.Ler("catalogo"), n => System.Threading.Tasks.Task.FromResult(n == "W250x32.7" ? perfil : null), Amostras.Ler("catalogo-arvore"));
            Cap(jka, "46-catalogo-arvore", () =>
            {
                jka.EscolherNo("Gerdau (ABNT)", "W/HP", "W"); jka.Escolher("W250x32.7"); EsperarIdle();
                RenderizarJanela(jka, (int)jka.Width, (int)jka.Height, "46-catalogo-arvore.png");
            });
            jka.Close();
            // v0.6 D: perfil composto (2 UPN 200 a partir de props de motor) e disposição do perfil
            var upn = new JsonObject { ["props"] = new JsonObject { ["tipo"] = "U", ["h"] = 0.2, ["b"] = 0.075, ["tw"] = 0.0085, ["tf"] = 0.0115 } };
            var jco = new JanelaComposto("UPN 200", upn);
            Cap(jco, "49-composto-2upn");
            jco.Close();
            var l2 = new JsonObject { ["props"] = new JsonObject { ["tipo"] = "L", ["h"] = 0.0508, ["b"] = 0.0508, ["t"] = 0.00635 } };
            var jcl = new JanelaComposto("L 2\" x 1/4\"", l2);
            Cap(jcl, "49-composto-2L");
            jcl.Close();
            var jd = new JanelaDisposicao(new Perfil3D { Forma = "U", H = 0.2, B = 0.075, Tw = 0.0085, Tf = 0.0115 }, 90, 7, 3);
            Cap(jd, "49-disposicao");
            jd.Close();
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
