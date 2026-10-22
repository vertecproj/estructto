// Menu Arquivo (Fluent Backstage) com páginas reais: Início, Novo, Abrir, Salvar, Salvar como, Exportar, Importar, Relatório,
// Propriedades do projeto, Opções e Sobre (docs/v3/INICIO.md). Cores: tokens do TemaPlant; transição: Anim.Entrar.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using F = Fluent;
using Ui = NovoFtool.UiPlaca;

namespace NovoFtool
{
    public partial class MainWindow
    {
        readonly Dictionary<string, F.BackstageTabItem> paginasBs = new Dictionary<string, F.BackstageTabItem>();
        readonly Dictionary<string, Func<FrameworkElement>> montaBs = new Dictionary<string, Func<FrameworkElement>>();
        internal IEnumerable<string> PaginasBs => paginasBs.Keys;

        void MontarBastidores()
        {
            BsAbas.Items.Clear();
            PaginaBs("inicio", "Início", "Recentes e atalhos para começar.", PaginaInicioBs);
            PaginaBs("novo", "Novo", "Modelos para um projeto novo (Ctrl+N).", PaginaNovoBs);
            PaginaBs("abrir", "Abrir", "Procurar um arquivo ou reabrir um recente (Ctrl+O).", PaginaAbrirBs);
            BsAbas.Items.Add(BotaoBs("Salvar", "Ctrl+S", () => Salvar(false), "Salva as alterações do projeto atual."));
            BsAbas.Items.Add(BotaoBs("Salvar como…", "Ctrl+Shift+S", () => Salvar(true), "Salva uma cópia com outro nome ou em outra pasta."));
            BsAbas.Items.Add(new F.SeparatorTabItem());
            PaginaBs("exportar", "Exportar", "DWG, DXF ou STEP do modelo 3D.", PaginaExportarBs);
            PaginaBs("importar", "Importar", "CYPE 3D (.ed3) ou Ftool (.ftl).", PaginaImportarBs);
            BsAbas.Items.Add(BotaoBs("Relatório", "Ctrl+P", AbrirRelatorio, "Memorial de cálculo do projeto (HTML, carimbo com a empresa)."));
            BsAbas.Items.Add(new F.SeparatorTabItem());
            PaginaBs("propriedades", "Propriedades do projeto", "Cliente, engenheiro, empresa e normas do projeto aberto.", PaginaPropriedadesBs);
            PaginaBs("opcoes", "Opções", "Acento, autor e empresa padrão, unidades, salvamento automático.", PaginaOpcoesBs);
            PaginaBs("sobre", "Sobre", "Versão, licenças e créditos.", PaginaSobreBs);
            BsAbas.Items.Add(BotaoBs("Procurar atualizações", "", () => ProcurarAtualizacao(true), "Consulta se há uma versão nova do Estructto e mostra o aviso com o botão Instalar."));
            BsAbas.Items.Add(new F.SeparatorTabItem());
            BsAbas.Items.Add(BotaoBs("Sair", "Alt+F4", Close, "Fecha o programa (pergunta antes caso existam alterações não salvas)."));
            BsAbas.SelectedItem = paginasBs["inicio"];
            Bastidor.IsOpenChanged += (s, e) => { if (Bastidor.IsOpen) RemontarBs(); };
            BsAbas.SelectionChanged += (s, e) => { if (BsAbas.SelectedItem is F.BackstageTabItem it && paginasBs.ContainsValue(it)) { RemontarBs(); Anim.Entrar(it.Content as FrameworkElement, 6, Anim.Rapido); } };
        }

        void PaginaBs(string chave, string titulo, string dica, Func<FrameworkElement> monta)
        {
            var it = new F.BackstageTabItem { Header = titulo, ToolTip = dica, FocusVisualStyle = null };
            paginasBs[chave] = it; montaBs[chave] = monta;
            BsAbas.Items.Add(it);
        }

        F.Button BotaoBs(string texto, string atalho, Action acao, string dica = null)
        {
            var b = new F.Button { Header = texto, ToolTip = dica != null ? (object)Dica(texto, dica, atalho) : atalho };
            b.Click += (s, e) => { Bastidor.IsOpen = false; acao(); };
            return b;
        }

        // Remonta só a página selecionada (recentes e propriedades mudam a cada abertura).
        void RemontarBs()
        {
            foreach (var kv in paginasBs)
                if (kv.Value == BsAbas.SelectedItem) { try { kv.Value.Content = montaBs[kv.Key](); } catch (Exception ex) { kv.Value.Content = Pagina(kv.Value.Header.ToString(), "⚠ " + ex.Message); } }
        }

        internal void MostrarBs(string chave) { Bastidor.IsOpen = true; BsAbas.SelectedItem = paginasBs[chave]; RemontarBs(); }
        internal void MostrarRecentesBs() => MostrarBs("inicio");

        // Moldura comum: título leve, subtítulo e conteúdo rolável, largura máxima de leitura.
        static ScrollViewer Pagina(string titulo, string sub, params UIElement[] filhos)
        {
            var sp = new StackPanel { Margin = new Thickness(40, 28, 40, 28), MaxWidth = 1040, HorizontalAlignment = HorizontalAlignment.Left };
            sp.Children.Add(new TextBlock { Text = titulo, FontSize = TemaPlant.TamDisplay, FontWeight = FontWeights.Medium, FontFamily = TemaPlant.FonteTitulo, Foreground = TemaPlant.BTexto1 });
            if (sub != null) sp.Children.Add(new TextBlock { Text = sub, FontSize = 12, Foreground = TemaPlant.BTexto2, Margin = new Thickness(0, 4, 0, 18), TextWrapping = TextWrapping.Wrap });
            foreach (var f in filhos) sp.Children.Add(f);
            return new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        }

        static Border CartaoBs(UIElement conteudo, double pad = 16)
        {
            return new Border { Background = TemaPlant.BSup2, BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(pad), Child = conteudo, Margin = new Thickness(0, 0, 0, 14) };
        }

        // Cartão de ação grande (ícone, título, descrição) usado em Importar/Exportar/Início.
        Button AcaoBs(string icone, string titulo, string desc, Action acao, double larg = 250)
        {
            var dp = new DockPanel { Width = larg - 24 };
            var ic = (FrameworkElement)Icone(icone, 26); ic.Margin = new Thickness(0, 2, 12, 0); ic.VerticalAlignment = VerticalAlignment.Top; DockPanel.SetDock(ic, Dock.Left); dp.Children.Add(ic);
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = titulo, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = TemaPlant.BTexto1 });
            sp.Children.Add(new TextBlock { Text = desc, FontSize = 11.5, Foreground = TemaPlant.BTexto3, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
            dp.Children.Add(sp);
            var b = CartaoBotao(dp, acao, titulo + ". " + desc, new Thickness(12), titulo);
            b.Margin = new Thickness(0, 0, 10, 10); b.VerticalAlignment = VerticalAlignment.Top;
            return b;
        }

        FrameworkElement ListaRecentesBs(int max, Action remontar)
        {
            var rec = Inicio.Acoes.Recentes().Take(max).ToList();
            if (rec.Count == 0) return new TextBlock { Text = "Nenhum projeto aberto ainda.", Foreground = TemaPlant.BTexto3, Margin = new Thickness(0, 4, 0, 10) };
            var g = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
            foreach (var (c, fx) in rec) { var ca = CartaoRecente(c, fx, false, remontar); ca.Margin = new Thickness(0, 0, 10, 6); g.Children.Add(ca); }
            return g;
        }

        // ---------- páginas ----------
        FrameworkElement PaginaInicioBs()
        {
            var acoes = new WrapPanel();
            acoes.Children.Add(AcaoBs("novo", "Novo projeto", "Modelo, dados do projeto e grade de eixos/níveis. Ctrl+N.", () => NovoProjeto()));
            acoes.Children.Add(AcaoBs("abrir", "Abrir…", "Projeto .json do motor. Ctrl+O.", () => Inicio.Acoes.AbrirDialogo(this)));
            acoes.Children.Add(AcaoBs("3d", "Tela inicial", "Galeria de modelos, recentes e aprender.", () => { Bastidor.IsOpen = false; MontarTelaInicial(); TelaInicial.Visibility = Visibility.Visible; }));
            var lista = ListaRecentesBs(8, RemontarBs);
            miniaturasPendentes.Clear();
            return Pagina("Início", null, acoes, Rotulo("RECENTES", new Thickness(0, 6, 0, 8)), lista);
        }

        FrameworkElement PaginaNovoBs()
        {
            var wp = new WrapPanel();
            miniaturasPendentes.Clear();
            foreach (var m in ModeloInicial.Todos) { var c = CartaoModelo(m, mm => NovoProjeto(mm.Chave)); c.Margin = new Thickness(0, 0, 10, 10); wp.Children.Add(c); }
            MiniaturasInicio = CarregarMiniaturas();
            var dica = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            dica.Children.Add(Tecla("Ctrl+N")); dica.Children.Add(new TextBlock { Text = "  abre o mesmo diálogo com o último modelo usado.", FontSize = 11.5, Foreground = TemaPlant.BTexto3, VerticalAlignment = VerticalAlignment.Center });
            return Pagina("Novo projeto", "Escolha um modelo: o diálogo seguinte pede nome, pasta, cliente, normas e a grade de eixos e níveis, e o arquivo já nasce salvo.", wp, dica);
        }

        FrameworkElement PaginaAbrirBs()
        {
            var acoes = new WrapPanel();
            acoes.Children.Add(AcaoBs("abrir", "Procurar…", "Projeto .json do motor, obra .ed3 do CYPE ou .ftl do Ftool.", () => Inicio.Acoes.AbrirDialogo(this)));
            acoes.Children.Add(AcaoBs("3d", "Exemplos prontos", "Galpão, edifício e treliça do motor, para explorar.", () => Inicio.Acoes.Exemplos(this)));
            var lista = ListaRecentesBs(12, RemontarBs);
            return Pagina("Abrir", null, acoes, Rotulo("RECENTES (fixados primeiro)", new Thickness(0, 6, 0, 8)), lista);
        }

        FrameworkElement PaginaExportarBs()
        {
            var wp = new WrapPanel();
            foreach (var (fmt, ic, dica) in new[] { ("DWG", "exportar", "AutoCAD 2018: uma malha por barra na camada da seção + eixos analíticos, em mm."), ("DXF", "exportar", "O mesmo conteúdo do DWG em texto (DXF)."), ("STEP", "3d", "AP214: um sólido de faces planas por barra, numa montagem, em mm.") })
                wp.Children.Add(AcaoBs(ic, fmt + "…", dica, () => ExportarModelo(fmt)));
            var est = EspacialAtivo == null ? new TextBlock { Text = "Abra um projeto 3D para exportar.", Foreground = TemaPlant.BTexto3 } : null;
            return est == null ? Pagina("Exportar", "O modelo 3D vai inteiro, com perfis reais; os desenhos abrem no AutoCAD, BricsCAD, Inventor ou SolidWorks.", wp)
                               : Pagina("Exportar", "O modelo 3D vai inteiro, com perfis reais.", est, wp);
        }

        FrameworkElement PaginaImportarBs()
        {
            var wp = new WrapPanel();
            wp.Children.Add(AcaoBs("cype", "CYPE 3D (.ed3)", "Obra completa: nós, barras, perfis, materiais, apoios, casos e cargas. Avisos do que não converteu.", () => Inicio.Acoes.ImportarCype(this), 320));
            wp.Children.Add(AcaoBs("importar", "Ftool (.ftl)", "Geometria, seções, materiais e casos de carga de um pórtico plano, convertido para o 3D.", () => Inicio.Acoes.ImportarFtool(this), 320));
            return Pagina("Importar", "O arquivo importado vira um projeto novo; salve-o com Ctrl+S.", wp);
        }

        FrameworkElement PaginaPropriedadesBs()
        {
            var d = EspacialAtivo;
            if (d == null)
            {
                var vazio = CartaoBs(new TextBlock { Text = "Nenhum projeto 3D aberto. Crie um (Ctrl+N) ou abra um .json do motor para editar cliente, engenheiro, empresa e normas.", Foreground = TemaPlant.BTexto2, TextWrapping = TextWrapping.Wrap });
                return Pagina("Propriedades do projeto", null, vazio);
            }
            var pj = d.Json["config"]?["projeto"] as JsonObject ?? new JsonObject();
            var grade = d.Json["config"]?["grade"] as JsonObject;
            var campos = new Dictionary<string, TextBox>();
            var g = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
            int r = 0;
            void Linha(string rot, FrameworkElement ed)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var t = new TextBlock { Text = rot, FontSize = 12, Foreground = TemaPlant.BTexto2, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 8) };
                Grid.SetRow(t, r); g.Children.Add(t);
                ed.Margin = new Thickness(0, 0, 0, 8); Grid.SetRow(ed, r); Grid.SetColumn(ed, 1); g.Children.Add(ed); r++;
            }
            TextBox Campo(string k, string v) { var tb = JanelaEscura.Campo(v ?? "", 360); tb.Height = 28; tb.VerticalContentAlignment = VerticalAlignment.Center; tb.Padding = new Thickness(8, 0, 8, 0); campos[k] = tb; return tb; }
            Linha("Nome", new TextBlock { Text = d.Nome, Foreground = TemaPlant.BTexto1, FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            Linha("Arquivo", new TextBlock { Text = d.Arquivo ?? "ainda não salvo", Foreground = TemaPlant.BTexto3, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, ToolTip = d.Arquivo });
            Linha("Cliente", Campo("cliente", (string)pj["cliente"]));
            Linha("Engenheiro", Campo("engenheiro", (string)pj["engenheiro"]));
            Linha("Empresa", Campo("empresa", (string)pj["empresa"]));
            var ns = new StackPanel { Orientation = Orientation.Horizontal };
            var atuais = (pj["normas"]?.AsArray() ?? new JsonArray()).Select(x => (string)x).ToList();
            var cbs = new List<CheckBox>();
            foreach (string n in new[] { "NBR 8800", "NBR 6118", "NBR 14762" }) { var cb = new CheckBox { Content = n, IsChecked = atuais.Contains(n), Foreground = TemaPlant.BTexto1, Margin = new Thickness(0, 0, 14, 0) }; cbs.Add(cb); ns.Children.Add(cb); }
            Linha("Normas", ns);
            Linha("Unidades", new TextBlock { Text = "kN · m · kN·m · MPa (fixas no cálculo)", Foreground = TemaPlant.BTexto2, VerticalAlignment = VerticalAlignment.Center });
            string gradeTxt = grade == null ? "sem grade (desenhada à mão ou importada)" :
                "eixos " + string.Join(", ", (grade["nomes_x"]?.AsArray() ?? new JsonArray()).Select(x => (string)x)) + " · filas " + string.Join(", ", (grade["nomes_y"]?.AsArray() ?? new JsonArray()).Select(x => (string)x));
            Linha("Grade", new TextBlock { Text = gradeTxt, Foreground = TemaPlant.BTexto2, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
            var niv = Niveis.Lista(d.Json);
            Linha("Níveis", new TextBlock { Text = string.Join(" · ", niv.Select(n => n.Nome + " (z = " + n.Z.ToString("0.00") + " m)")), Foreground = TemaPlant.BTexto2, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
            Linha("Modelo", new TextBlock { Text = d.Resumo(), Foreground = TemaPlant.BTexto2, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
            var estado = new TextBlock { FontSize = 11.5, Foreground = TemaPlant.BTexto3, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            var bts = new StackPanel { Orientation = Orientation.Horizontal };
            var aplicar = JanelaEscura.B("Aplicar", async () =>
            {
                var novo = new JsonObject { ["cliente"] = campos["cliente"].Text.Trim(), ["engenheiro"] = campos["engenheiro"].Text.Trim(), ["empresa"] = campos["empresa"].Text.Trim(), ["normas"] = new JsonArray(cbs.Where(x => x.IsChecked == true).Select(x => (JsonNode)(string)x.Content).ToArray()) };
                foreach (var kv in pj) if (!novo.ContainsKey(kv.Key)) novo[kv.Key] = kv.Value?.DeepClone();
                try { await d.Executar(() => Motor.Chamar("PATCH", "/api/config", new JsonObject { ["projeto"] = novo })); alterado = true; AtualizarTitulo(); estado.Text = "Aplicado (Ctrl+S salva no arquivo)."; }
                catch (Exception ex) { estado.Text = "⚠ " + ex.Message; }
            }, true, "Grava cliente, engenheiro, empresa e normas no projeto (config.projeto do .json).");
            aplicar.Margin = new Thickness(0); bts.Children.Add(aplicar); bts.Children.Add(estado);
            return Pagina("Propriedades do projeto", "Dados informativos do projeto: vão para o carimbo e a capa do relatório. Não mudam o cálculo.", CartaoBs(g), bts);
        }

        FrameworkElement PaginaOpcoesBs()
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(380) });
            int r = 0;
            void Linha(string rot, FrameworkElement ed, string sub = null)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var t = new TextBlock { Text = rot, FontSize = 12, Foreground = TemaPlant.BTexto2, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 10, 12) };
                Grid.SetRow(t, r); g.Children.Add(t);
                var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 12) }; sp.Children.Add(ed);
                if (sub != null) sp.Children.Add(new TextBlock { Text = sub, FontSize = 10.5, Foreground = TemaPlant.BTexto3, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap });
                Grid.SetRow(sp, r); Grid.SetColumn(sp, 1); g.Children.Add(sp); r++;
            }
            TextBox Campo(string v, Action<string> set) { var tb = JanelaEscura.Campo(v, 380); tb.Height = 28; tb.VerticalContentAlignment = VerticalAlignment.Center; tb.Padding = new Thickness(8, 0, 8, 0); tb.LostFocus += (s, e) => set(tb.Text.Trim()); return tb; }

            // acento: amostras clicáveis
            var ac = new StackPanel { Orientation = Orientation.Horizontal };
            string atual = Config.Opcoes.Acento;
            foreach (var (nome, hex) in Config.Opcoes.Acentos)
            {
                bool sel = string.Equals(hex, atual, StringComparison.OrdinalIgnoreCase);
                var b = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Background = TemaPlant.B(hex), BorderBrush = sel ? TemaPlant.BTexto1 : Brushes.Transparent, BorderThickness = new Thickness(2), Margin = new Thickness(0, 0, 8, 0), Cursor = System.Windows.Input.Cursors.Hand, ToolTip = nome };
                b.MouseLeftButtonDown += (s, e) => { Config.Opcoes.Acento = hex; TemaPlant.AplicarAcento(Application.Current, hex); RemontarBs(); };
                System.Windows.Automation.AutomationProperties.SetName(b, "Acento " + nome);
                ac.Children.Add(b);
            }
            Linha("Cor de acento", ac, "Faixa, botões primários e seleção. Alguns realces só mudam ao reiniciar.");
            Linha("Engenheiro padrão", Campo(Config.Opcoes.Autor, v => Config.Opcoes.Autor = v), "Preenchido em cada projeto novo.");
            Linha("Empresa padrão", Campo(Config.Opcoes.Empresa, v => Config.Opcoes.Empresa = v), "Carimbo do relatório e campo Empresa do projeto novo.");
            Linha("Pasta dos projetos", Campo(Config.Opcoes.PastaProjetos, v => Config.Opcoes.PastaProjetos = v), "Pasta sugerida ao criar um projeto.");
            var un = Ui.Combo(new[] { "kN · m", "tf · m" }, Config.Opcoes.Unidades, v => { Config.Opcoes.Unidades = v; StatusUnid.Text = v == "tf · m" ? "tf · m · tf·m" : "m · kN · kN·m"; }, 140, "Só a exibição na barra de status; o modelo e o relatório são sempre em kN e m.");
            Linha("Unidades exibidas", un, "O cálculo é sempre em kN, m, kN·m e MPa.");
            // ponytail: unidades só na barra de status; conversão de tabelas/inspetor quando alguém pedir
            var st = Ui.Stepper(Config.Opcoes.AutosalvarMin + " min", () => MudarAutosalvar(-1), () => MudarAutosalvar(1), 110, null, "Intervalo da cópia de recuperação (1 a 30 min).");
            Linha("Salvamento automático", st, "Cópia de recuperação em %AppData%\\NovoFtool; o arquivo do projeto só muda com Ctrl+S.");
            var atu = new StackPanel();
            var cbAtu = new CheckBox { Content = "Procurar atualizações ao iniciar", IsChecked = Config.Opcoes.ProcurarAtualizacoes, Foreground = TemaPlant.BTexto1, Margin = new Thickness(0, 4, 0, 8) };
            cbAtu.Click += (s, e) => Config.Opcoes.ProcurarAtualizacoes = cbAtu.IsChecked == true;
            var agora = JanelaEscura.B("Procurar agora", () => { Bastidor.IsOpen = false; ProcurarAtualizacao(true); }, false, "Consulta o GitHub e mostra o aviso se houver versão nova.");
            agora.Margin = new Thickness(0); agora.HorizontalAlignment = HorizontalAlignment.Left;
            atu.Children.Add(cbAtu); atu.Children.Add(agora);
            Linha("Atualizações", atu, "Versão instalada " + Atualizador.VersaoAtual + ". A instalação só acontece com o SHA-256 publicado conferindo.");
            var reset = JanelaEscura.B("Restaurar painéis", () =>
            {
                foreach (var la in AvalonDock.Layout.Extensions.Descendents(Docas.Layout).OfType<AvalonDock.Layout.LayoutAnchorable>()) if (la.IsHidden) la.Show();
                MostrarToast("Painéis Projeto, Tabelas, Avisos e Propriedades de volta.", true);
            }, false, "Mostra de novo os painéis ocultos (Projeto, Tabelas, Avisos, Propriedades) nos lugares padrão.");
            reset.Margin = new Thickness(0); reset.HorizontalAlignment = HorizontalAlignment.Left;
            Linha("Layout", reset, "Painéis fechados ou arrastados voltam ao padrão.");
            return Pagina("Opções", "Preferências do programa, guardadas em %AppData%\\NovoFtool\\opcoes.txt.", CartaoBs(g, 20));
        }

        void MudarAutosalvar(int d)
        {
            Config.Opcoes.AutosalvarMin = Math.Max(1, Math.Min(30, Config.Opcoes.AutosalvarMin + d));
            autoSalvar.Interval = TimeSpan.FromMinutes(Config.Opcoes.AutosalvarMin);
            RemontarBs();
        }

        FrameworkElement PaginaSobreBs()
        {
            var cab = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
            cab.Children.Add(new Image { Source = TemaPlant.Selo(64), Width = 64, Height = 64, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center });
            var t = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            t.Children.Add(Marca(30, TemaPlant.BTexto1));
            string ver = Atualizador.VersaoAtual;
            t.Children.Add(new TextBlock { Text = "Modelagem e cálculo de estruturas 3D · versão " + ver, FontSize = 12, Foreground = TemaPlant.BTexto2 });
            cab.Children.Add(t);
            var txt = new StackPanel();
            foreach (string l in new[] {
                "Motor de cálculo PROSSEN Estrutural (Python): pórtico espacial, NBR 8800:2008, NBR 6118:2014, NBR 14762:2010, NBR 8681, NBR 6123 (vento) e NBR 15421 (sismo).",
                "Interface WPF (.NET 8) com Fluent.Ribbon, AvalonDock e ACadSharp (DWG/DXF). Ícones Tabler (MIT). Fonte da marca: Barlow (OFL).",
                "Ferramenta de apoio: todo resultado exige conferência pelo engenheiro responsável (ART).",
                "Projeto do Gabriel e do Bruno — PROSSEN Engenharia. Código: github.com/vertecproj/estructto." })
                txt.Children.Add(new TextBlock { Text = l, FontSize = 12, Foreground = TemaPlant.BTexto2, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
            return Pagina("Sobre", null, cab, CartaoBs(txt));
        }
    }
}
