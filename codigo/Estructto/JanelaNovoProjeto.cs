// Novo projeto em 3 passos com prévia ao vivo (docs/v3/INICIO.md): (1) modelo, (2) dados do projeto, (3) grade de eixos e
// níveis como o "Structure Grid" do Plant 3D ("3x6 2x5"). Criar → Inicio.Acoes.Novo gera, salva e abre em planta.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using Ui = NovoFtool.UiPlaca;

namespace NovoFtool
{
    public class JanelaNovoProjeto : JanelaEscura
    {
        public NovoProjetoDados Dados;
        readonly MainWindow w;
        ModeloInicial modelo;
        int passo;
        readonly Grid conteudo = new Grid();
        readonly StackPanel cabecalho = new StackPanel { Orientation = Orientation.Horizontal };
        readonly Border previaCx = new Border { Background = TemaPlant.BBgCanvas, BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), ClipToBounds = true };
        readonly Image previaImg = new Image { Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, SnapsToDevicePixels = true };
        readonly TextBlock previaTxt = T("", 11.5, BFraco), previaTit = T("", 13, null, FontWeights.SemiBold), erro = T("", 12, BVermelho);
        readonly Button btVoltar, btAvancar;
        readonly Dictionary<string, Button> cartoes = new Dictionary<string, Button>();
        readonly Dictionary<string, TextBox> c = new Dictionary<string, TextBox>();
        readonly Dictionary<string, CheckBox> normas = new Dictionary<string, CheckBox>();
        readonly TextBlock resumoGrade = T("", 11.5, BFraco);
        readonly DispatcherTimer tmPrevia = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        int versaoPrevia;
        internal Task PreviaPronta = Task.CompletedTask;
        static readonly string[] Passos = { "Modelo", "Projeto", "Grade e níveis" };
        static readonly string[] NormasTodas = { "NBR 8800", "NBR 6118", "NBR 14762" };

        public JanelaNovoProjeto(MainWindow dono, string modeloInicial = null)
        {
            w = dono; modelo = ModeloInicial.Por(modeloInicial);
            Title = "Novo projeto"; Width = 1000; Height = 640; ResizeMode = ResizeMode.NoResize; MinWidth = 900; MinHeight = 560;
            var raiz = new Grid { Margin = new Thickness(20, 16, 20, 16) };
            raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            raiz.RowDefinitions.Add(new RowDefinition());
            raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // cabeçalho: título + passos
            var cab = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
            DockPanel.SetDock(cabecalho, Dock.Right); cab.Children.Add(cabecalho);
            cab.Children.Add(T("Novo projeto", 20, BTexto, FontWeights.Light));
            raiz.Children.Add(cab);

            // conteúdo: formulário | prévia
            conteudo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(430) });
            conteudo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            conteudo.ColumnDefinitions.Add(new ColumnDefinition());
            var pv = new DockPanel();
            var pvCab = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            // título em cima, resumo/aviso embaixo com quebra: aviso longo do motor não espreme o título (texto em pé)
            DockPanel.SetDock(previaTit, Dock.Top); previaTit.TextTrimming = TextTrimming.CharacterEllipsis; pvCab.Children.Add(previaTit);
            previaTxt.TextWrapping = TextWrapping.Wrap; previaTxt.Margin = new Thickness(0, 2, 0, 0); pvCab.Children.Add(previaTxt);
            DockPanel.SetDock(pvCab, Dock.Top); pv.Children.Add(pvCab);
            previaCx.Child = previaImg;
            previaCx.SizeChanged += (s, e) => PedirPrevia();
            pv.Children.Add(previaCx);
            Grid.SetColumn(pv, 2); conteudo.Children.Add(pv);
            Grid.SetRow(conteudo, 1); raiz.Children.Add(conteudo);

            // rodapé
            var rod = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
            var bts = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var btCancelar = B("Cancelar", Close, false, "Fecha sem criar nada (Esc)."); btCancelar.IsCancel = true;
            btVoltar = B("Voltar", () => IrPasso(passo - 1), false, "Passo anterior.");
            btAvancar = B("Avançar", Avancar, true, "Próximo passo (Enter).");
            bts.Children.Add(btCancelar); bts.Children.Add(btVoltar); bts.Children.Add(btAvancar);
            DockPanel.SetDock(bts, Dock.Right); rod.Children.Add(bts);
            erro.VerticalAlignment = VerticalAlignment.Center; rod.Children.Add(erro);
            Grid.SetRow(rod, 2); raiz.Children.Add(rod);
            Content = raiz;

            MontarCampos();
            tmPrevia.Tick += (s, e) => { tmPrevia.Stop(); PreviaPronta = AtualizarPrevia(); };
            IrPasso(0);
        }

        // ---------- campos (criados uma vez; cada passo mostra os seus) ----------
        void MontarCampos()
        {
            foreach (var (k, v) in new[] { ("nome", "Projeto " + DateTime.Now.ToString("yyyy-MM-dd")), ("pasta", Config.Opcoes.PastaProjetos), ("cliente", ""), ("engenheiro", Config.Opcoes.Autor), ("empresa", Config.Opcoes.Empresa),
                                           ("ex", modelo.EixosX), ("ey", modelo.EixosY), ("ez", modelo.Niveis), ("material", modelo.Material), ("pilar", modelo.Pilar), ("viga", modelo.Viga) })
            {
                c[k] = Campo(v, 260); c[k].Height = 28; c[k].VerticalContentAlignment = VerticalAlignment.Center; c[k].Padding = new Thickness(8, 0, 8, 0);
                c[k].TextChanged += (s, e) => { erro.Text = ""; if (passo == 2) PedirPrevia(); };
            }
            foreach (string n in NormasTodas) normas[n] = new CheckBox { Content = n, IsChecked = n != "NBR 14762", Foreground = BTexto, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        }

        static Grid Linha(string rot, FrameworkElement ed, string dica = null, string sub = null)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(128) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            var r = T(rot, 12, BFraco); r.VerticalAlignment = VerticalAlignment.Center; r.Margin = new Thickness(0, 0, 10, 0); g.Children.Add(r);
            var sp = new StackPanel(); sp.Children.Add(ed);
            if (sub != null) sp.Children.Add(T(sub, 10.5, TemaPlant.BTexto3));
            if (dica != null) ed.ToolTip = dica;
            Grid.SetColumn(sp, 1); g.Children.Add(sp);
            return g;
        }

        FrameworkElement PassoModelo()
        {
            var sp = new StackPanel();
            sp.Children.Add(T("Escolha por onde começar. Tudo é 3D: a grade de eixos e os níveis viram planos de trabalho; as barras do modelo podem ser editadas ou apagadas depois.", 12, BFraco));
            sp.Children[0].SetValue(MarginProperty, new Thickness(0, 0, 0, 12));
            var wp = new WrapPanel();
            cartoes.Clear();
            foreach (var m in ModeloInicial.Todos)
            {
                var b = w.CartaoModelo(m, Escolher, 112, 68, true);
                b.Margin = new Thickness(0, 0, 8, 8); b.Width = 134;
                cartoes[m.Chave] = b; wp.Children.Add(b);
            }
            sp.Children.Add(wp);
            w.MiniaturasInicio = w.CarregarMiniaturas();
            Realcar();
            return sp;
        }

        void Escolher(ModeloInicial m)
        {
            modelo = m;
            c["ex"].Text = m.EixosX; c["ey"].Text = m.EixosY; c["ez"].Text = m.Niveis; c["material"].Text = m.Material; c["pilar"].Text = m.Pilar; c["viga"].Text = m.Viga;
            Realcar(); PedirPrevia();
        }

        void Realcar()
        {
            foreach (var kv in cartoes)
            {
                bool sel = kv.Key == modelo.Chave;
                kv.Value.Background = sel ? TemaPlant.BAcentoSutil : TemaPlant.BSup1;
                kv.Value.BorderBrush = sel ? TemaPlant.BAcento : TemaPlant.BBordaSutil;
            }
        }

        FrameworkElement PassoProjeto()
        {
            var sp = new StackPanel();
            sp.Children.Add(Linha("Nome", c["nome"], "Nome do projeto e do arquivo (.json)."));
            var pasta = new DockPanel();
            var bp = Ui.Botao("…", null, EscolherPasta, "Escolhe a pasta onde o projeto será salvo."); bp.Margin = new Thickness(6, 0, 0, 0); DockPanel.SetDock(bp, Dock.Right); pasta.Children.Add(bp);
            c["pasta"].Width = double.NaN; pasta.Children.Add(c["pasta"]);
            sp.Children.Add(Linha("Pasta", pasta, null, "O arquivo nasce salvo: nada de \"Sem título\"."));
            sp.Children.Add(Linha("Cliente", c["cliente"]));
            sp.Children.Add(Linha("Engenheiro", c["engenheiro"], "Responsável técnico (padrão em Arquivo › Opções)."));
            sp.Children.Add(Linha("Empresa", c["empresa"], "Vai para o carimbo do relatório."));
            var un = Ui.Chip("kN · m · kN·m · MPa", Ui.Nivel.Neutro, "Unidades fixas do motor: forças em kN, comprimentos em m, momentos em kN·m, tensões em MPa."); un.HorizontalAlignment = HorizontalAlignment.Left;
            sp.Children.Add(Linha("Unidades", un, null, "Fixas no cálculo; a exibição muda em Opções."));
            var ns = new StackPanel { Orientation = Orientation.Horizontal }; foreach (var cb in normas.Values) ns.Children.Add(cb);
            sp.Children.Add(Linha("Normas", ns, null, "Aço laminado/soldado, concreto armado, formados a frio."));
            sp.Children.Add(Linha("Vento e sismo", T("Opcional, depois: Ações › Vento (NBR 6123) e Ações › Sismo (NBR 15421).", 11.5, BFraco)));
            return sp;
        }

        void EscolherPasta()
        {
            var d = new Microsoft.Win32.OpenFolderDialog { Title = "Pasta do projeto", InitialDirectory = Directory.Exists(c["pasta"].Text) ? c["pasta"].Text : null };
            if (d.ShowDialog(this) == true) c["pasta"].Text = d.FolderName;
        }

        FrameworkElement PassoGrade()
        {
            var sp = new StackPanel();
            sp.Children.Add(T("Vãos entre eixos em metros: \"3x6 2x5\" = três de 6 m e dois de 5 m. Eixos 1, 2, 3… em X; filas A, B, C… em Y; níveis N0, N+3,50… pela altura de cada andar.", 11.5, BFraco));
            sp.Children[0].SetValue(MarginProperty, new Thickness(0, 0, 0, 12));
            sp.Children.Add(Linha(modelo.RotX, c["ex"]));
            sp.Children.Add(Linha(modelo.RotY, c["ey"]));
            sp.Children.Add(Linha(modelo.RotZ, c["ez"]));
            resumoGrade.Margin = new Thickness(0, 0, 0, 12); resumoGrade.TextWrapping = TextWrapping.Wrap;
            sp.Children.Add(resumoGrade);
            if (modelo.Gerador != null)
            {
                sp.Children.Add(Ui.TituloSecao("Barras do modelo", null, 4));
                sp.Children.Add(Linha("Material", c["material"], "Nome do catálogo (A-572 GR.50, A36) ou C30 para concreto."));
                sp.Children.Add(Linha(modelo.Chave == "trelica" ? "Banzos" : "Pilares", c["pilar"], "Perfil do catálogo (W310x38.7) ou \"Ret 25x50\" em cm."));
                sp.Children.Add(Linha(modelo.Chave == "trelica" ? "Diagonais" : "Vigas", c["viga"]));
            }
            else sp.Children.Add(T("Só a grade e os níveis: desenhe pilares e vigas com Barra (B) na planta de cada nível.", 11.5, BFraco));
            return sp;
        }

        // ---------- passos ----------
        internal void IrPasso(int p)
        {
            passo = Math.Max(0, Math.Min(2, p));
            erro.Text = "";
            cabecalho.Children.Clear();
            for (int i = 0; i < Passos.Length; i++)
            {
                bool at = i == passo, feito = i < passo;
                var it = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(i == 0 ? 0 : 18, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                var num = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = at ? TemaPlant.BAcento : feito ? TemaPlant.BSup3 : Brushes.Transparent, BorderBrush = at ? TemaPlant.BAcento : TemaPlant.BBorda, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 7, 0) };
                num.Child = new TextBlock { Text = feito ? "✓" : (i + 1).ToString(), FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = at ? TemaPlant.BTextoSobreAcento : TemaPlant.BTexto2, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                it.Children.Add(num);
                it.Children.Add(new TextBlock { Text = Passos[i], FontSize = 12, FontWeight = at ? FontWeights.SemiBold : FontWeights.Normal, Foreground = at ? TemaPlant.BTexto1 : TemaPlant.BTexto3, VerticalAlignment = VerticalAlignment.Center });
                cabecalho.Children.Add(it);
            }
            var painel = passo == 0 ? PassoModelo() : passo == 1 ? PassoProjeto() : PassoGrade();
            var sv = new ScrollViewer { Content = painel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            for (int i = conteudo.Children.Count - 1; i >= 0; i--) if (Grid.GetColumn(conteudo.Children[i]) == 0) conteudo.Children.RemoveAt(i);
            conteudo.Children.Add(sv);
            Anim.Entrar(painel, 6, Anim.Rapido);
            btVoltar.Visibility = passo == 0 ? Visibility.Collapsed : Visibility.Visible;
            btAvancar.Content = passo == 2 ? "Criar projeto" : "Avançar";
            btAvancar.ToolTip = passo == 2 ? "Gera o modelo no motor, salva o arquivo na pasta e abre em planta no primeiro nível (Enter)." : "Próximo passo (Enter).";
            PedirPrevia();
            Dispatcher.BeginInvoke(new Action(() => { (passo == 1 ? c["nome"] : passo == 2 ? c["ex"] : (Control)btAvancar).Focus(); if (passo == 1) c["nome"].SelectAll(); }), DispatcherPriority.Input);
        }

        void Avancar()
        {
            if (passo == 1 && !ValidarProjeto()) return;
            if (passo < 2) { IrPasso(passo + 1); return; }
            if (!ValidarProjeto()) { IrPasso(1); return; }
            GradeNiveis g;
            try { g = LerGrade(); } catch (FormatException ex) { erro.Text = "Confira os vãos: \"" + ex.Message + "\" não é \"n×valor\" nem um número."; return; }
            if (modelo.Gerador == null && g.X.Count == 0 && g.Y.Count == 0) { erro.Text = "Informe ao menos um vão em X ou em Y."; return; }
            Dados = new NovoProjetoDados
            {
                Modelo = modelo, Grade = g, Nome = c["nome"].Text.Trim(), Pasta = c["pasta"].Text.Trim(), Cliente = c["cliente"].Text.Trim(), Engenheiro = c["engenheiro"].Text.Trim(), Empresa = c["empresa"].Text.Trim(),
                Material = c["material"].Text.Trim(), Pilar = c["pilar"].Text.Trim(), Viga = c["viga"].Text.Trim(), Normas = normas.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToList(),
            };
            if (File.Exists(Dados.Arquivo)) { erro.Text = "Já existe " + Path.GetFileName(Dados.Arquivo) + " nessa pasta: mude o nome ou a pasta."; IrPasso(1); return; }
            Config.Opcoes.PastaProjetos = Dados.Pasta;
            if (Dados.Engenheiro != "" && Config.Opcoes.Autor == "") Config.Opcoes.Autor = Dados.Engenheiro;
            if (Dados.Empresa != "" && Config.Opcoes.Empresa == "") Config.Opcoes.Empresa = Dados.Empresa;
            DialogResult = true;
        }

        bool ValidarProjeto()
        {
            string nome = c["nome"].Text.Trim(), pasta = c["pasta"].Text.Trim();
            if (nome == "" || nome.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) { erro.Text = "Nome do projeto: obrigatório, sem \\ / : * ? \" < > |."; return false; }
            if (pasta == "" || pasta.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || !Path.IsPathRooted(pasta)) { erro.Text = "Pasta: informe um caminho completo (ex.: C:\\Projetos)."; return false; }
            return true;
        }

        GradeNiveis LerGrade()
        {
            var g = new GradeNiveis { X = GradeNiveis.Espacamentos(c["ex"].Text), Y = GradeNiveis.Espacamentos(c["ey"].Text) };
            g.Z = GradeNiveis.Cotas(c["ez"].Text);
            return g;
        }

        // ---------- prévia ----------
        void PedirPrevia() { tmPrevia.Stop(); tmPrevia.Start(); }

        async Task AtualizarPrevia()
        {
            int v = ++versaoPrevia;
            GradeNiveis g;
            try { g = LerGrade(); }
            catch (FormatException ex) { resumoGrade.Text = "⚠ \"" + ex.Message + "\": use \"n×valor\" ou um número por vão."; return; }
            var br = new List<(Point3D, Point3D)>(); var ax = new List<(Point3D, Point3D)>();
            string aviso = "";
            var dados = modelo.Dados(g, c["material"].Text.Trim(), c["pilar"].Text.Trim(), c["viga"].Text.Trim());
            JsonNode p = null;
            if (dados != null)
            {
                try { p = await Inicio.Acoes.Previa(dados); (br, ax) = Miniatura3D.DePrevia(p); }
                catch (MotorErro ex) { string m = ex.Message; try { m = System.Text.Json.Nodes.JsonNode.Parse(m)?["detalhe"]?.ToString() ?? m; } catch { } aviso = "⚠ " + m; }
                catch (Exception) { aviso = "Motor indisponível: prévia só da grade."; }
            }
            if (v != versaoPrevia) return;
            bool mostrarGrade = modelo.Chave != "galpao" && modelo.Chave != "trelica";
            double W = previaCx.ActualWidth - 2, H = previaCx.ActualHeight - 2;
            previaImg.Source = W > 20 && H > 20 ? Miniatura3D.Desenhar(br, ax, mostrarGrade ? g : null, W, H) : null;
            previaImg.Width = W > 20 ? W : 0; previaImg.Height = H > 20 ? H : 0;
            string eixos = (g.X.Count > 0 ? "eixos 1–" + (g.X.Count + 1) + " (" + Cotas(GradeNiveis.Acumular(g.X)) + ")" : "sem eixos em X") + " · "
                         + (g.Y.Count > 0 ? "filas A–" + GradeNiveis.NomeY(g.Y.Count) + " (" + Cotas(GradeNiveis.Acumular(g.Y)) + ")" : "sem filas em Y") + " · "
                         + "níveis " + string.Join(", ", g.Z.Select(GradeNiveis.NomeZ));
            resumoGrade.Text = eixos;
            previaTit.Text = modelo.Titulo + (passo == 2 ? "" : "  —  grade padrão");
            previaTxt.Text = aviso != "" ? aviso : p != null ? (int)p["nos_criados"] + " nós · " + (int)p["barras_criadas"] + " barras · " + g.Z.Count + " níveis" : (g.X.Count + 1) * (g.Y.Count + 1) + " cruzamentos · " + g.Z.Count + " níveis";
        }

        static string Cotas(List<double> z) => string.Join(" ", z.Select(v => v.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("pt-BR")))) + " m";
    }
}
